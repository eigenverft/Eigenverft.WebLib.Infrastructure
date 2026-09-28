# Eigenverft.WebLib.Hosting.DirectoryLayout

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hosting.DirectoryLayout?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hosting.DirectoryLayout?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Executable-rooted ASP.NET Core hosting that shares NetLib’s validated application directory layout and connects it to content roots, web roots, and wwwroot.

## ✨ At a glance

| Capability | What it does |
| --- | --- |
| WebApplicationBuilderFactory.CreateWithDefaultDirectory | Creates a WebApplicationBuilder rooted at the executable directory. |
| IAppDirectoryLayout | Access the shared semantic directories, including the Web mapping. |
| Dependency | Eigenverft.NetLib.Hosting.DirectoryLayout is included transitively. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Hosting.DirectoryLayout
```

## 🚀 Quick start

### Executable-rooted web host

```csharp
using Eigenverft.NetLib.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Hosting.DirectoryLayout;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder =
    WebApplicationBuilderFactory.CreateWithDefaultDirectory(args);

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string settingsPath = directories[DefaultDirectory.ApplicationSettings];
string webRoot = builder.Environment.WebRootPath;

WebApplication app = builder.Build();
app.MapGet("/", () => $"Settings: {settingsPath}; web root: {webRoot}");
app.Run();
```

## Package behavior

<code>CreateWithDefaultDirectory(args)</code> roots ContentRootPath at AppContext.BaseDirectory, sets WebRootPath to wwwroot below that root, and delegates directory creation, layout registration, validation, and writable checks to NetLib. The created layout is available from the builder before Build and from application services afterwards.

The default semantic directories keep their NetLib names, such as AppSettings, AppCerts, AppData, AppState, and AppLogs. The Web key maps to wwwroot. Pass typed DefaultDirectory overrides to change selected application folders, or a string-keyed folder map for a custom layout. Unspecified typed entries keep their defaults.

The factory enforces the standard wwwroot name by default so ASP.NET Core’s web-root path and the semantic Web mapping cannot disagree. Use strictWwwrootName: false only when a custom web-root folder name is intentional. Folder names remain subject to NetLib’s layout validation.

## 🎯 Target frameworks

Targets .NET 8 and .NET 10. A .NET 9 application can consume the compatible net8.0 asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Package solution](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.Hosting.DirectoryLayout)
- [Package source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.Hosting.DirectoryLayout)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout)
- [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

## 📄 License

Licensed under the MIT License by Eigenverft.
