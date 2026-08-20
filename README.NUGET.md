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

`AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` provides the configuration-value
convenience layer. Pass the application directory layout and a stable purpose; WebLib derives the
standard key-ring path and entry-assembly discriminator. The returned `ConfigurationValueCodec` can
be used independently or at any position in `ConfigurationValueCodecs.Compose(...)`.

## 🌐 Kestrel and SNI

`ConfigureKestrelSniFromConfiguration(...)` is the package's top-level server setup. It configures
HTTP/HTTPS listeners, TLS policy, managed PFX files, SNI selection, and atomic certificate reloads
while using NetLib's certificate primitives underneath.

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
app.Run();
```

This replaces the implicit `appsettings*.json` providers and keeps the startup-fixed listener
configuration separate from the reloadable certificate mappings. The switchable source protects
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

At least one listener and one usable certificate mapping are required. Provision the initial PFX
password on the target machine rather than committing it; NetLib rewrites the selected value as a
codec envelope during source registration and exposes clear text only in memory.

Recovery policy reflects certificate ownership: `PreserveExisting` protects original or externally
managed certificates, `ReplaceExpired` permits managed renewal, and `ReplaceAnyUnusable` is for
fully application-managed disposable certificates.

| PFX state or failure | `PreserveExisting` | `ReplaceExpired` | `ReplaceAnyUnusable` |
| --- | --- | --- | --- |
| Missing file or parent directory | Create and persist | Create and persist | Create and persist |
| Valid and contains a private key | Load | Load | Load |
| Imported and expired | Keep + memory recovery | Replace | Replace |
| Imported but not yet valid | Keep + memory recovery | Keep + memory recovery | Replace |
| Imported but missing private key | Keep + memory recovery | Keep + memory recovery | Replace |
| Password mismatch, corrupt/unsupported PFX, or other import failure | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| I/O read failure | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| Access denied | Keep + memory recovery | Keep + memory recovery | Authorize replacement; the write may still fail |
| Persistence or atomic-move failure during an authorized create/replace | Return generated certificate in memory and report the failure | Same | Same |
| Concurrent creator wins missing-file race | Keep the winner; return this process's generated certificate in memory | Same | Same |

At startup, memory recovery can keep TLS available. During reload, a complete usable last-known-good
generation remains active instead. Deleting an application-managed self-signed PFX requests fresh
creation under every mode; `ReplaceAnyUnusable` matters only while an unusable file remains present.
It can overwrite an externally managed certificate if selected incorrectly.

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
