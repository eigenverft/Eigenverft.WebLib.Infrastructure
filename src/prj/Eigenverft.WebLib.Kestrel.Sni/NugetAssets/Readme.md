# Eigenverft.WebLib.Kestrel.Sni

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Kestrel.Sni?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Kestrel.Sni?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Configuration-driven Kestrel HTTP/HTTPS listeners with SNI certificate selection and controlled certificate recovery.

## ✨ At a glance

| Capability | Details |
| --- | --- |
| Entry point | `ConfigureKestrelSniFromConfiguration(...)` configures listeners from `KestrelSettings`. |
| Certificate maps | Selects a PFX from `CertificatesMappingSettings` by requested SNI host. |
| Dependencies | Uses `Eigenverft.NetLib.Security.Certificates` transitively for managed certificate handling. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Kestrel.Sni
```

## 🚀 Quick start

Add configuration sources before building the app, then call the extension once:

```csharp
using Eigenverft.WebLib.Kestrel.Sni;

builder.WebHost.ConfigureKestrelSniFromConfiguration();
```

A minimal configuration shape is:

```json
{
  "KestrelSettings": {
    "HTTP_PORT": 8080,
    "HTTPS_PORT": 8443,
    "ListenScope": "Localhost",
    "Protocols": "Http1AndHttp2",
    "PreferLongestSuffixMatch": true,
    "TlsProtocolPolicy": "Default"
  },
  "CertificatesDirectory": "certs",
  "CertificatesMappingSettings": [
    {
      "SNI": "example.com",
      "FileName": "example.com.pfx",
      "Password": "load-from-protected-configuration"
    }
  ]
}
```

## Listener and certificate behavior

The `KestrelSettings` section is required (override its path with `kestrelSettingsSectionPath`). At least one listener must be enabled; HTTPS requires a valid certificate map even for HTTP-only listener mode. Listener ports, scope, protocols, TLS policy, match preference, and certificate directory are read at startup and require a host restart to change. `TlsProtocolPolicy` applies only when HTTPS is enabled. The certificate directory defaults to `certs` below the content root; an override takes precedence, and relative paths resolve against that root.

`CertificatesMappingSettings` maps an `SNI` suffix to a `FileName` and optional `Password`. The longest matching DNS suffix is preferred by default, with a DNS-label boundary; if no SNI or suffix matches, the first configured mapping is the fallback. Keep certificate passwords in protected configuration rather than committed plaintext. Certificate file paths must remain inside the configured certificate directory.

Only certificate mappings are hot-reloadable, and the configuration provider must emit reload notifications. A failed reload retains the last-known-good generation. Recovery defaults to `None`: the configured PFX must load, contain a private key, and be currently valid; an unusable initial certificate fails startup. `PreserveExisting` can generate a memory-only self-signed fallback without replacing the file. `ReplaceExpired` manages missing or expired PFX files; `ReplaceAnyUnusable` is intended only for fully application-managed files. Additional self-signed DNS/IP names apply only when recovery generates a certificate.

## 🎯 Target frameworks

Targets `net8.0` and `net10.0`; .NET 9 can consume the compatible `net8.0` asset.

## 🔗 Project links

- [Repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Solution and tests](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.Kestrel.Sni)
- [Project source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.Kestrel.Sni)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni)

## 📄 License

MIT; see the [repository license](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE).
