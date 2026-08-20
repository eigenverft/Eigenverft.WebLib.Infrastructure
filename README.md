# Eigenverft.WebLib.Infrastructure

ASP.NET Core adapters built on
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).
Host-independent infrastructure belongs to NetLib; WebLib contains only behavior that
depends directly on ASP.NET Core or Kestrel.

## Web-specific scope

### Web application directory integration

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates an ASP.NET Core
`WebApplicationBuilder` from NetLib's shared directory layout. WebLib adds the web-specific
`ContentRootPath`, `WebRootPath`, and semantic `"Web"` mapping for `wwwroot`.

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder =
    WebApplicationBuilderFactory.CreateWithDefaultDirectory();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string protectionKeysDirectory =
    directories[DefaultDirectory.ApplicationProtectionKeys];
```

### ASP.NET Core Data Protection adapter

`AspNetDataProtectionStringTransforms.DataProtection(...)` adapts an ASP.NET Core
`IDataProtectionProvider` to NetLib's `ReversibleStringTransform` abstraction while keeping
the application name, purpose, and persistent key-ring directory explicit.

For persisted configuration values,
`AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` adds the NetLib codec envelope.
Its directory-layout overload derives the standard key-ring path and entry-assembly name, leaving
only the application-specific purpose to the caller. The result is a normal
`ConfigurationValueCodec` and can be used independently or at any position in
`ConfigurationValueCodecs.Compose(...)`.

### Kestrel and SNI configuration

`ConfigureKestrelSniFromConfiguration(...)` is the top-level entry point for
configuration-driven HTTP/HTTPS listeners and reload-safe SNI certificate selection. It combines
Kestrel with NetLib's managed-certificate primitives while keeping listener policy and
certificate lifecycle in one place.

Add the configuration sources before the application is built, then call the extension once:

```csharp
using System;
using System.IO;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Sources;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.Kestrel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

WebApplicationBuilder builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory(args);

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string settingsDirectory = directories[DefaultDirectory.ApplicationSettings];

builder.ResetToMinimalConfigurationSources(includeCommandLineArguments: true);

builder.Configuration.AddJsonFile(
    Path.Combine(settingsDirectory, "KestrelSettings.json"),
    optional: false, reloadOnChange: false);

// Generate a different stable factor for each application.
byte[] applicationFactor =
{
    0x23, 0x52, 0x66, 0x37, 0x5A, 0x39, 0x27, 0x27,
    0x5E, 0x52, 0x6C, 0x2E, 0x36, 0x49, 0x45, 0x4E,
    0x79, 0x4A, 0x52, 0x43, 0x4E, 0x4D, 0x3F, 0x5E,
    0x50, 0x5A, 0x6A, 0x5F, 0x4E, 0x32, 0x28, 0x4E,
};

string configurationProtectionSecret =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PROTECTION_SECRET")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PROTECTION_SECRET is required.");

ConfigurationValueCodec certificatePasswordCodec =
    ConfigurationValueCodecs.Compose(
        ConfigurationValueCodecs.AesPassword(applicationFactor),
        ConfigurationValueCodecs.AesPassword(configurationProtectionSecret),
        ConfigurationValueCodecs.PhysicalMachineBoundAes(),
        AspNetDataProtectionConfigurationValueCodecs.DataProtection(
            directories, nameof(certificatePasswordCodec)));

var certificateSourceOptions = new SwitchableJsonRegistrationOptions
{
    ReloadOnChange = true,
    ValueProtection = JsonConfigurationValueProtection.ForPaths(
        certificatePasswordCodec, "CertificatesMappingSettings:*:Password"),
};

builder.AddSwitchableJsonFile(
    "KestrelCertificateMappings",
    Path.Combine(settingsDirectory, "CertificatesMappingSettings.json"),
    certificateSourceOptions);

builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: directories[DefaultDirectory.ApplicationCerts]);

WebApplication app = builder.Build();
app.MapGet("/", () => "HTTPS is ready");
app.Run();
```

`ResetToMinimalConfigurationSources(...)` removes the implicit `appsettings*.json` providers.
The two explicit files then separate startup-fixed server policy from reloadable certificate
mappings. The switchable source is useful here because it combines last-known-good reloads with
targeted value protection: only `CertificatesMappingSettings:*:Password` is encoded on disk and
decoded before WebLib sees the candidate configuration. Generate a stable, application-specific
`applicationFactor` byte array; it avoids placing the factor in the assembly string table but
remains recoverable and is not a secret. Actual secrecy depends on protecting
`APP_CONFIGURATION_PROTECTION_SECRET`. This generic external deployment secret can protect other
application configuration too; the application-owned factor and selected path provide the
certificate-specific separation. It is not itself a certificate password. The machine-bound layer
means the encoded file must be provisioned on its target machine. The outer ASP.NET Core Data
Protection layer uses the persistent `ApplicationProtectionKeys` directory plus the stable
entry-assembly name and purpose. The convenience codec derives the key-ring path from `directories`
and the application name from `Assembly.GetEntryAssembly()`; neither depends on mutable host
configuration. Here, `nameof(certificatePasswordCodec)` is the persisted purpose, so treat that
variable name as a compatibility contract. Back up and retain the complete key ring while protected
values may still depend on it.

#### Defense in depth and limits

The persisted certificate password passes through the codecs in order:

```text
clear text → application byte factor → deployment secret → machine binding → Data Protection → JSON
```

An offline attacker must reverse every layer. In practical terms, that requires the protected JSON
value, the exact codec composition and order, the application factor obtainable from the assembly,
`APP_CONFIGURATION_PROTECTION_SECRET`, the original system/platform UUID used by the machine
binding, the complete Data Protection key ring, and the matching application name and purpose. The
live `ConfigurationValueCodec` instance is not required if the recipe can be reconstructed from the
assembly or source. The algorithms, recipe, and identifiers are not secrets; security comes from the
independently held secret and key material. Merely exposing the JSON file, executable, environment
secret, or key-ring directory alone is therefore insufficient. A complete application-directory
copy still lacks the deployment secret and original platform identity unless those were collected
separately.

This separation reduces the chance that one path-traversal bug, accidental file publication, backup
leak, or configuration disclosure immediately reveals the PFX password. It is defense in depth, not
a runtime security boundary. Code execution inside the application process, or equivalent access
under its identity, can read the already decoded configuration, inspect process state, or invoke the
same protection pipeline. A process dump, secret-bearing log, or endpoint that exposes configuration
can bypass several layers at once.

Availability is the inverse risk: losing any required factor can make the value permanently
unrecoverable. Retain the key ring and deployment secret, keep the application name and purpose
stable, and account for platform-UUID changes during VM cloning, migration, or reprovisioning. Omit
the machine-bound layer when portable restore or multi-machine use is more important than resistance
to an offline application-directory copy.

`KestrelSettings.json`:

```json
{
  "KestrelSettings": {
    "HTTP_PORT": 8080,
    "HTTPS_PORT": 8443,
    "ListenScope": "Localhost",
    "AddServerHeader": false,
    "Protocols": "Http1AndHttp2",
    "PreferLongestSuffixMatch": true,
    "TlsProtocolPolicy": "Default"
  }
}
```

`CertificatesMappingSettings.json`:

```json
{
  "CertificatesMappingSettings": [
    {
      "SNI": "localhost",
      "FileName": "localhost.pfx",
      "Password": "change-me",
      "CertificateRecoveryMode": "PreserveExisting",
      "AdditionalSelfSignedCertificateDnsNames": [
        "*.localhost"
      ],
      "AdditionalSelfSignedCertificateIpAddresses": [
        "127.0.0.1",
        "::1"
      ]
    }
  ]
}
```

The call performs the complete server setup:

- creates the configured localhost or any-IP HTTP/HTTPS listeners;
- applies HTTPS protocol and TLS policy;
- loads existing PFX files or creates missing self-signed TLS server certificates;
- selects certificates by exact SNI name or DNS-suffix boundary;
- uses the first mapping as the stable fallback when SNI is absent or unmatched;
- publishes mapping reloads atomically and retains the last-known-good generation when a reload
  fails.

Certificate-directory resolution uses the explicit `certDirOverride` first, then the top-level
`CertificatesDirectory` value, and finally `certs` below the content root. Relative paths are
resolved against the content root. Mapping paths and symbolic-link targets must remain inside the
selected certificate directory.

Listener settings are startup-fixed:

| Setting | Default | Behavior |
| --- | --- | --- |
| `HTTP_PORT` | disabled | Positive values enable a plaintext HTTP/1 listener. |
| `HTTPS_PORT` | disabled | Values from `1` through `65535` enable the SNI HTTPS listener. |
| `ListenScope` | `Localhost` | Use `AnyIP` to bind all available addresses. |
| `AddServerHeader` | `false` | Controls Kestrel's `Server` response header. |
| `Protocols` | `Http1AndHttp2` | HTTPS `HttpProtocols` value. |
| `PreferLongestSuffixMatch` | `true` | Tries the most-specific configured suffix first. |
| `TlsProtocolPolicy` | `Default` | `Default` = TLS 1.2/1.3; `Strict` = TLS 1.3 only. Compatibility and legacy modes require explicit opt-in. |

At least one listener must be enabled. Certificate mappings are required even for the HTTP-only
form of this SNI helper.

Each certificate mapping requires `SNI` and `FileName`. `Password` defaults to an empty string.
Provision its initial clear-text value on the target machine rather than committing it; registration
then rewrites matching values as codec envelopes while publishing clear text only in memory.
Additional DNS and IP values are used as typed SANs when WebLib generates a certificate.

`CertificateRecoveryMode` controls replacement of unusable files:

- `PreserveExisting` creates a missing PFX but never overwrites an existing unusable file;
- `ReplaceExpired` replaces only a PFX that was opened successfully and found expired;
- `ReplaceAnyUnusable` may replace files affected by password, import, read, or access failures
  and can therefore overwrite an externally managed certificate.

Only `CertificatesMappingSettings` is hot-reloadable. A configuration reload builds and validates
a complete replacement generation before publishing it. Changing ports, bind scope, protocols,
TLS policy, matching strategy, or the certificate directory requires a host restart. Replacing a
PFX file alone does not emit a configuration reload token; the configuration provider must also
signal a reload.

Earlier copies of this helper used a single `SanNames` array. When migrating, split it into
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses`, choose a recovery mode where necessary, and use
the `Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace instead of an application-local
or `Eigenverft.Routed.RequestFilters` implementation.

## Installation

```powershell
dotnet add package Eigenverft.WebLib.Infrastructure
```

The package references `Eigenverft.NetLib.Infrastructure` as its shared foundation.

## Build and test

```powershell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

## CI/CD and documentation

The repository uses the same reusable CI/CD workflow as NetLib for build, test, packaging,
reports, DocFX generation, artifact distribution, and deployment-channel documentation.

## License

MIT. See [LICENSE](LICENSE).
