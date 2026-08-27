# 🧱 Eigenverft.WebLib.Infrastructure

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.WebLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Production-oriented ASP.NET Core adapters built on
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).

WebLib turns configuration, certificate files, and the shared application directory layout into a
reload-safe Kestrel/SNI setup. It also connects ASP.NET Core Data Protection to NetLib's composable
configuration-value protection without duplicating host-independent infrastructure.

---

## ✨ At a glance

| Capability | Problem solved | Starting point |
| --- | --- | --- |
| Kestrel and SNI | Configuration-driven listeners, host-name certificate selection, and last-known-good certificate reloads | `ConfigureKestrelSniFromConfiguration(...)` |
| Managed certificates | Existing PFX loading or policy-controlled self-signed recovery | `CertificateRecoveryMode` |
| Protected mappings | Persist certificate passwords through composable protection instead of leaving clear text after provisioning | `AspNetDataProtectionConfigurationValueCodecs` |
| Web host directories | Apply NetLib's executable-rooted layout to content root, web root, and `wwwroot` | `WebApplicationBuilderFactory` |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

## 🚀 Quick start

Create an executable-rooted ASP.NET Core host while using NetLib's shared directory
layout:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder =
    WebApplicationBuilderFactory.CreateWithDefaultDirectory();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string webRoot = directories["Web"];

WebApplication app = builder.Build();
app.MapGet("/", () => $"Web root: {webRoot}");
app.Run();
```

`WebApplicationBuilderFactory` adds only the web-specific projection: ASP.NET Core
content/web roots and the semantic `"Web"` directory entry. Directory creation,
validation, writable probes, standard directory keys, and DI registration are
provided by NetLib.

## 🔐 ASP.NET Core Data Protection adapter

`AspNetDataProtectionStringTransforms` adapts an ASP.NET Core
`IDataProtectionProvider` to NetLib's `ReversibleStringTransform` abstraction.
The generic transform and configuration-value codec infrastructure remains in
NetLib, so Data Protection can participate without duplicating the generic codec
stack in WebLib.

`AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` provides the configuration-value
convenience layer. Pass the application directory layout and a stable purpose; WebLib derives the
standard key-ring path and entry-assembly discriminator. The returned `ConfigurationValueCodec` can
be used independently or at any position in `ConfigurationValueCodecs.Compose(...)`.

## 🌐 Kestrel and SNI

`ConfigureKestrelSniFromConfiguration(...)` is the package's top-level server setup. It configures
HTTP/HTTPS listeners, TLS policy, managed PFX files, SNI selection, and atomic certificate reloads
while using NetLib's certificate primitives underneath.

The setup separates startup policy, reloadable mappings, certificates, and protection keys:

```text
<application>/
├── AppSettings/
│   ├── KestrelSettings.json                ← startup-fixed listener policy
│   └── CertificatesMappingSettings.json    ← protected, reloadable SNI mappings
├── AppCerts/
│   └── localhost.pfx                       ← existing or WebLib-managed certificate
├── AppProtectionKeys/
│   └── ...                                 ← persistent Data Protection key ring
└── wwwroot/
```

### Register configuration and protect certificate passwords

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

// Replace the implicit host sources with the selected process-level sources.
builder.ResetToMinimalConfigurationSources(
    includeCommandLineArguments: true,
    includeEnvironmentVariables: true);

// Listener policy is read once while Kestrel is configured.
builder.Configuration.AddJsonFile(
    path: Path.Combine(settingsDirectory, "KestrelSettings.json"),
    optional: false,
    reloadOnChange: false);

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
        codecs:
        [
            // Separate this application and purpose from another use of the deployment secret.
            ConfigurationValueCodecs.AesPassword(passwordAsciiBytes: applicationFactor),
            // Add the externally supplied secret factor.
            ConfigurationValueCodecs.AesPassword(password: configurationProtectionSecret),
            // Resist an offline copy to another physical-machine identity.
            ConfigurationValueCodecs.PhysicalMachineBoundAes(),
            // Persist the outer key material through ASP.NET Core Data Protection.
            AspNetDataProtectionConfigurationValueCodecs.DataProtection(
                directories: directories,
                purpose: nameof(certificatePasswordCodec)),
        ]);

SwitchableJsonRegistrationOptions certificateSourceOptions = new()
{
    // Follow and reload the active certificate-mapping file.
    ReloadOnChange = true,
    // Protect only certificate passwords, not routing or certificate file names.
    ValueProtection = JsonConfigurationValueProtection.ForPaths(
        codec: certificatePasswordCodec,
        patterns: ["CertificatesMappingSettings:*:Password"]),
};

// Publish complete mapping generations and keep last-known-good data on failure.
builder.AddSwitchableJsonFile(
    name: "KestrelCertificateMappings",
    initialPath: Path.Combine(settingsDirectory, "CertificatesMappingSettings.json"),
    options: certificateSourceOptions);
```

The reset clears every existing source and then re-adds environment variables and command-line
arguments. The explicit JSON sources are registered afterwards and therefore have higher precedence
for overlapping keys.

### Configure Kestrel and run

```csharp
// Resolve PFX files below NetLib's validated application certificate directory.
builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: directories[DefaultDirectory.ApplicationCerts]);

WebApplication app = builder.Build();
app.Run();
```

The two JSON files keep startup-fixed listener configuration separate from reloadable certificate
mappings. The switchable source protects
only `CertificatesMappingSettings:*:Password` on disk, decodes it before publication, and retains
the last-known-good configuration after a rejected reload. Generate a stable application-specific
byte-array factor, protect `APP_CONFIGURATION_PROTECTION_SECRET`, and provision the file on its
target machine. This generic external deployment secret can protect other application configuration;
the factor and selected path separate this certificate use. It is not itself a certificate password.
The byte array avoids an assembly string-table entry but remains a recoverable structural factor
rather than a secret. The outer ASP.NET Core Data Protection layer uses the persistent
`ApplicationProtectionKeys` directory. The convenience codec derives that path from the directory
layout and its application discriminator from `Assembly.GetEntryAssembly()`, rather than mutable host
configuration. Preserve the complete key ring, application name, and purpose while protected values
may still need to be decoded. Because the purpose comes from
`nameof(certificatePasswordCodec)`, treat that variable name as a persisted compatibility contract.

### Defense in depth and limits

The stored password is protected in this order:

```text
clear text → application byte factor → deployment secret → machine binding → Data Protection → JSON
```

Offline reversal requires the protected JSON value, exact codec composition and order, application
factor, deployment secret, original platform UUID, complete Data Protection key ring, and matching
application name and purpose. The live codec object is unnecessary if its recipe is reconstructed
from the assembly or source. A leak of only the JSON file, executable, environment secret, or
key-ring directory is insufficient. This makes accidental single-source exposure less likely to
reveal the PFX password.

It does not protect against code execution inside the application process: such an attacker can read
the decoded configuration or invoke the same pipeline. Losing any factor also prevents legitimate
recovery. Preserve the key ring and deployment secret, keep identities stable, and omit machine
binding when portable restore or multi-machine deployment is required.

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
      "Password": "change-me"
    }
  ]
}
```

The extension loads configured PFX files and performs self-signed recovery only when explicitly enabled.
It matches exact SNI names and DNS suffixes, prefers the longest suffix by default, and uses the
first mapping as the fallback when SNI is absent or unmatched.

Certificate-directory resolution uses the explicit override first, then the top-level
`CertificatesDirectory` value, and finally `certs` below the content root. Mapping paths and
symbolic-link targets cannot escape that directory.

| Setting | Default | Behavior |
| --- | --- | --- |
| `HTTP_PORT` | disabled | Positive values enable a plaintext HTTP/1 listener. |
| `HTTPS_PORT` | disabled | Values from `1` through `65535` enable the SNI HTTPS listener. |
| `ListenScope` | `Localhost` | Use `AnyIP` to bind all available addresses. |
| `AddServerHeader` | `false` | Controls Kestrel's `Server` response header. |
| `Protocols` | `Http1AndHttp2` | HTTPS `HttpProtocols` value. |
| `PreferLongestSuffixMatch` | `true` | Tries the most-specific configured suffix first. |
| `TlsProtocolPolicy` | `Default` | TLS 1.2/1.3 by default; `Strict` selects TLS 1.3 only. |

At least one listener and one usable certificate mapping are required. Provision the initial PFX
password on the target machine rather than committing it; NetLib rewrites the selected value as a
codec envelope during source registration and exposes clear text only in memory.

Recovery is opt-in. An omitted or invalid `CertificateRecoveryMode` selects `None`, which performs classic PFX loading without generating or persisting fallback certificates. `PreserveExisting` enables memory-only self-signed recovery without changing the configured path, `ReplaceExpired` permits missing-file creation and managed expiry renewal, and `ReplaceAnyUnusable` is for fully application-managed disposable certificates.

| PFX state or failure | `None` | `PreserveExisting` | `ReplaceExpired` | `ReplaceAnyUnusable` |
| --- | --- | --- | --- | --- |
| Missing file or parent directory | Fail; create nothing | Memory recovery only | Create and persist | Create and persist |
| Valid and contains a private key | Load | Load | Load | Load |
| Imported and expired | Fail | Keep + memory recovery | Replace | Replace |
| Imported but not yet valid | Fail | Keep + memory recovery | Keep + memory recovery | Replace |
| Imported but missing private key | Fail | Keep + memory recovery | Keep + memory recovery | Replace |
| Password mismatch, corrupt/unsupported PFX, or other import failure | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| I/O read failure | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| Access denied | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement; the write may still fail |
| Persistence or atomic-move failure during an authorized create/replace | Not applicable | Not applicable | Return generated certificate in memory and report the failure | Same |
| Concurrent creator wins missing-file race | Not applicable | Not applicable | Keep the winner; return this process’s generated certificate in memory | Same |

At startup, an invalid PFX under `None` fails without recovery. During reload, WebLib rejects an invalid candidate and keeps the last-known-good generation active. Memory recovery can keep TLS available at startup for explicit recovery modes. Deleting an application-managed self-signed PFX requests fresh creation only under `ReplaceExpired` or `ReplaceAnyUnusable`; `PreserveExisting` remains memory-only. `ReplaceAnyUnusable` can overwrite an externally managed certificate if selected incorrectly.

Only `CertificatesMappingSettings` is hot-reloadable. WebLib publishes a complete replacement
generation atomically and keeps the last-known-good certificates active if a reload fails.
Listener changes require a host restart. A PFX file change is observed on the next configuration
reload; changing the file alone does not emit a reload token.

When migrating from the earlier helper, replace `SanNames` with the typed
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses` properties and use the
`Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace.

## 📁 Isolated static and PWA hosting

Use `MapIsolated(...)` for URL subtrees that must be exclusively owned by a static/PWA branch and
`MapRemaining(...)` for the remaining shell pipeline. Both are thin wrappers over native non-rejoining
ASP.NET Core branch semantics; no separate routing or mount system is introduced.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Pipeline;
using Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles;

app.MapIsolated("/apps", apps =>
{
    apps.UsePwaHost();
});

app.MapIsolated("/downloads", downloads =>
{
    downloads.UseStaticFiles(AdditionalMappings.Media);
});

app.MapRemaining(shell =>
{
    shell.UseRouting();
    shell.UseEndpoints(endpoints => endpoints.MapRazorComponents<App>());
});
```

`MapRemaining` deliberately exposes a normal `IApplicationBuilder`; endpoint APIs such as `MapStaticAssets()` and
`MapRazorComponents<T>()` therefore stay inside native `UseEndpoints(...)` rather than requiring a WebLib-specific
hybrid pipeline/router builder.

`MapIsolated` preserves the matched path segment, so `/apps/...` resolves against `wwwroot/apps/...` using
normal ASP.NET Core static-file/file-server middleware. Missing files end with the native branch 404 and
do not fall through into the shell. Outer `UseStatusCodePagesWithReExecute(...)` handling is disabled for
isolated requests so global re-execution cannot escape that ownership boundary.

Mappings are strictly additive to ASP.NET Core defaults: `AdditionalMappings.WebApp` backfills only `.br`
and `.dat`; `AdditionalMappings.Media` backfills `.avif` only on `net8.0` and is a no-op on `net10.0` where
that mapping is already built in. `AdditionalMappings.Combine(...)` composes typed groups. The underlying
`FileExtensionContentTypeProvider` remains internal, and there is no separate legacy-style
`UseStaticFilesWithPwaAndBlazorContentTypes(...)` API.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Documentation](https://eigenverft.github.io/Eigenverft.WebLib.Infrastructure/docfx/production/)
- [Issues](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/issues)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
