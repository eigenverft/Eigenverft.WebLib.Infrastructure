# Eigenverft.WebLib.Hosting.DirectoryLayout

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hosting.DirectoryLayout?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hosting.DirectoryLayout?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Connects NetLib's validated semantic application-directory layout to an ASP.NET Core host's content root, web root, and `wwwroot`.

## Use

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

The factory roots the host at the executable directory and sets the web root to `wwwroot` below it. Directory creation and validation come from `Eigenverft.NetLib.Hosting.DirectoryLayout`, restored transitively. See the [package usage guide](../../prj/Eigenverft.WebLib.Hosting.DirectoryLayout/NugetAssets/Readme.md) for custom layouts and overrides.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.Hosting.DirectoryLayout/` and `../../prj/Eigenverft.WebLib.Hosting.DirectoryLayout.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
