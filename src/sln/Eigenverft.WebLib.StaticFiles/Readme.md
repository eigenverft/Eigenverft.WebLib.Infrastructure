# Eigenverft.WebLib.StaticFiles

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.StaticFiles?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.StaticFiles?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Typed additive MIME mappings for ASP.NET Core static-file middleware.

## Use

```csharp
using Eigenverft.WebLib.StaticFiles;

WebApplication app = builder.Build();
app.UseStaticFiles(AdditionalMappings.WebApp);
```

`WebApp` backfills `.br` and `.dat`; `Media` adds `.avif` only for net8.0 (net10.0 already includes it). Existing framework mappings are preserved. For isolated subtrees, combine this extension with `MapIsolated(...)` from Middleware.Primitives; that pipeline helper is a separate package.

See the [package usage guide](../../prj/Eigenverft.WebLib.StaticFiles/NugetAssets/Readme.md) for mappings and combination behavior.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.StaticFiles/` and `../../prj/Eigenverft.WebLib.StaticFiles.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
