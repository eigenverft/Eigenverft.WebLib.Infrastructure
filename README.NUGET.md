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

WebLib provides the ASP.NET Core/Kestrel-specific listener and SNI configuration
layer. Shared X.509 certificate creation and managed certificate-file handling are
provided by `Eigenverft.NetLib.Infrastructure.Security.Certificates`.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 📄 License

Licensed under the MIT License by Eigenverft.
