# 🧱 Eigenverft.WebLib.Infrastructure

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.WebLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

ASP.NET Core adapters for
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).

WebLib intentionally stays thin and contains only the pieces that require ASP.NET Core
or Kestrel. Host-independent infrastructure belongs to NetLib.

---

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.WebLib.Infrastructure` |
| Target frameworks | .NET 8 and .NET 10 |
| Shared dependency | `Eigenverft.NetLib.Infrastructure` |
| Web hosting | `WebApplicationBuilderFactory` |
| ASP.NET security adapter | Data Protection reversible-string transform |
| Web server | Kestrel listener and SNI certificate configuration |
| License | MIT |

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
string protectionKeysDirectory =
    directories[DefaultDirectory.ApplicationProtectionKeys];

WebApplication app = builder.Build();
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

## 🌐 Kestrel and SNI

`ConfigureKestrelSniFromConfiguration(...)` is the package's top-level server setup. It configures
HTTP/HTTPS listeners, TLS policy, managed PFX files, SNI selection, and atomic certificate reloads
while using NetLib's certificate primitives underneath.

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

The extension loads existing PFX files or creates missing self-signed TLS server certificates.
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

At least one listener and one usable certificate mapping are required. Store real PFX passwords
in a protected configuration or secret provider rather than source control.

Recovery policy is explicit:

- `PreserveExisting` creates missing files but never overwrites an existing unusable PFX;
- `ReplaceExpired` replaces only a successfully opened, expired PFX;
- `ReplaceAnyUnusable` can also replace files affected by password, import, read, or access
  failures.

Only `CertificatesMappingSettings` is hot-reloadable. WebLib publishes a complete replacement
generation atomically and keeps the last-known-good certificates active if a reload fails.
Listener changes require a host restart. A PFX file change is observed on the next configuration
reload; changing the file alone does not emit a reload token.

When migrating from the earlier helper, replace `SanNames` with the typed
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses` properties and use the
`Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 📄 License

Licensed under the MIT License by Eigenverft.
