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

### Kestrel and SNI configuration

`ConfigureKestrelSniFromConfiguration(...)` is the top-level entry point for
configuration-driven HTTP/HTTPS listeners and reload-safe SNI certificate selection. It combines
Kestrel with NetLib's managed-certificate primitives while keeping listener policy and
certificate lifecycle in one place.

Add the configuration sources before the application is built, then call the extension once:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.Kestrel;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder =
    WebApplicationBuilderFactory.CreateWithDefaultDirectory(args);

IAppDirectoryLayout directories = builder.GetDirectoryLayout();

builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: directories[DefaultDirectory.ApplicationCerts]);

WebApplication app = builder.Build();
app.MapGet("/", () => "HTTPS is ready");
app.Run();
```

Minimal `appsettings.json`:

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
  },
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

Each certificate mapping requires `SNI` and `FileName`. `Password` defaults to an empty string;
store real PFX passwords in a protected configuration or secret provider rather than source
control. Additional DNS and IP values are used as typed SANs when WebLib generates a certificate.

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
