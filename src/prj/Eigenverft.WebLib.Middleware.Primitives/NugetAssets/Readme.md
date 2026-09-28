# Eigenverft.WebLib.Middleware.Primitives

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Middleware.Primitives?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Middleware.Primitives?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Small ASP.NET Core building blocks for middleware composition, isolated pipeline branches, typed request features, and explicit response writing.

## ✨ At a glance

| Capability | What it does |
| --- | --- |
| Pipeline | MapIsolated and MapRemaining create non-rejoining branches. |
| Infrastructure | UseMiddlewareOnce, CreateUseSiteOptionsMonitor, and EnsureServicesRegistered support reusable middleware. |
| Features and responses | Typed HttpContext feature helpers and WriteHtmlStatusResponseAsync. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Middleware.Primitives
```

## 🚀 Quick start

### Split static subtrees from the application shell

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

## Package behavior

MapIsolated preserves the matched path segment for normal static-file middleware and prevents the branch from rejoining the remaining pipeline. It disables an already-active status-code-pages feature for that request, so branch-owned 404 handling cannot re-execute into the application shell. Declare all isolated mappings before MapRemaining. MapRemaining is an always-matching, non-rejoining branch with a normal IApplicationBuilder; endpoint routing stays in UseRouting and UseEndpoints.

UseMiddlewareOnce<TMiddleware>() avoids a second convention-based middleware registration in the same linear pipeline. ASP.NET Core branch builders inherit markers established before the branch; markers added inside a branch remain local. The helper does not deduplicate across rejoining pipeline graphs.

CreateUseSiteOptionsMonitor<TOptions>(configure) creates a reload-aware options monitor for one middleware use. It builds from registered configure and post-configure steps, applies the local override, then validates. The local monitor has its own cache and does not change global options. EnsureServicesRegistered<TService>(...) checks required registrations without activating services.

The Features namespace provides GetFeature, GetRequiredFeature, TryGetFeature, SetFeature, RemoveFeature, and GetOrCreateFeature helpers over HttpContext.Features.

WriteHtmlStatusResponseAsync(statusCode) writes a minimal HTML status page using ASP.NET Core’s reason phrase and HTML encoding. Use it for explicit middleware short-circuits; general application error handling belongs in Status Code Pages or Problem Details.

This package has no package dependencies. Eigenverft.WebLib.CanonicalHostRedirect builds on its middleware option helpers and includes this package transitively.

## 🎯 Target frameworks

Targets .NET 8 and .NET 10. A .NET 9 application can consume the compatible net8.0 asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Package solution](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.Middleware.Primitives)
- [Package source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.Middleware.Primitives)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives)
- [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

## 📄 License

Licensed under the MIT License by Eigenverft.
