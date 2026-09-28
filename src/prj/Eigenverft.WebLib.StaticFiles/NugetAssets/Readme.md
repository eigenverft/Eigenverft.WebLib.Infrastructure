# Eigenverft.WebLib.StaticFiles

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.StaticFiles?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.StaticFiles?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Add typed, package-provided MIME mappings to ASP.NET Core static-file defaults without replacing the framework's content-type provider.

## ✨ At a glance

| Capability | Details |
| --- | --- |
| `AdditionalMappings.WebApp` | Adds `.br` and `.dat` as `application/octet-stream`; `.webmanifest` and `.wasm` are already framework defaults. |
| `AdditionalMappings.Media` | Adds `.avif` as `image/avif` on net8.0; it is a no-op on net10.0. |
| Composition | `AdditionalMappings.Combine(...)` combines typed groups and rejects conflicting MIME values. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.StaticFiles
```

## 🚀 Quick start

```csharp
using Eigenverft.WebLib.StaticFiles;

WebApplication app = builder.Build();
app.UseStaticFiles(AdditionalMappings.WebApp);
```

## Mapping behavior and composition

`StaticFileAdditionalMappings` is an opaque typed mapping group. The supplied extension retains the target framework's default MIME mappings and adds only extensions those defaults do not already define. Use the ordinary ASP.NET Core `UseStaticFiles()` overload when no additional group is needed.

`AdditionalMappings.Combine(...)` returns the union of its groups. Extension matching is case-insensitive; combining the same extension with different MIME values throws `InvalidOperationException`. On net8.0 `AdditionalMappings.Media` supplies `.avif`; on net10.0 ASP.NET Core already has that mapping, so the group is empty.

This package does not add a URL-mount or isolated-branch primitive. If an application needs an exclusive subtree, `MapIsolated(...)` is provided by `Eigenverft.WebLib.Middleware.Primitives` and can be used with this package's `UseStaticFiles(...)` extension.

## 🎯 Target frameworks

Targets `net8.0` and `net10.0`; .NET 9 applications can consume the compatible `net8.0` asset.

## 🔗 Project links

- [Repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Solution and tests](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.StaticFiles)
- [Project source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.StaticFiles)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles)

## 📄 License

MIT; see the [repository license](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE).
