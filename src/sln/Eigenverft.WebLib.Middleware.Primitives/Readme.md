# Eigenverft.WebLib.Middleware.Primitives

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Middleware.Primitives?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Middleware.Primitives?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Reusable ASP.NET Core helpers for non-rejoining pipeline branches, typed request features, middleware options, and status responses.

## Use

```csharp
using Eigenverft.WebLib.Middleware.Primitives.Pipeline;
using Microsoft.AspNetCore.Builder;

app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles();
});

app.MapRemaining(shell =>
{
    shell.UseRouting();
    shell.UseEndpoints(endpoints => endpoints.MapGet("/", () => "application shell"));
});
```

Declare isolated mappings before `MapRemaining`. `MapIsolated` owns its subtree without rejoining the remaining pipeline; `MapRemaining` is the final catch-all branch. The package also provides middleware registration/options helpers, typed feature access, and explicit HTML status responses. `CanonicalHostRedirect` consumes these helpers transitively.

See the [package usage guide](../../prj/Eigenverft.WebLib.Middleware.Primitives/NugetAssets/Readme.md) for behavior details and the remaining APIs.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.Middleware.Primitives/` and `../../prj/Eigenverft.WebLib.Middleware.Primitives.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
