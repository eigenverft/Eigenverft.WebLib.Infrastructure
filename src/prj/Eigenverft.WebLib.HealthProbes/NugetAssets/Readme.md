# Eigenverft.WebLib.HealthProbes

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.HealthProbes?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.HealthProbes?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

A fixed, dependency-free /health short-circuit for simple liveness probes, with suppression of browser favicon requests caused by that probe.

## ✨ At a glance

| Capability | What it does |
| --- | --- |
| UseHealthProbeFaviconAware() | Add the /health and probe-originated favicon behavior. |
| GET /health | Returns 200 OK, plain-text OK, and no-cache headers. |
| GET or HEAD /favicon.ico | Returns 204 only when the Referer path is exactly /health. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.HealthProbes
```

## 🚀 Quick start

### Add a simple liveness endpoint

```csharp
using Eigenverft.WebLib.HealthProbes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

app.UseHealthProbeFaviconAware();

// Register before middleware that the health request should bypass.
app.Run(context => context.Response.WriteAsync("application"));
```

## Package behavior

The middleware handles only GET and HEAD for the case-insensitive /health path. GET returns 200 with the body OK; HEAD returns the same status without a body. Both responses use text/plain; charset=utf-8 and set Cache-Control: no-store, no-cache plus Pragma: no-cache. Other methods continue through the pipeline.

A GET or HEAD for /favicon.ico returns 204 only when its Referer resolves to the exact /health path; a query string is allowed. Other favicon requests continue normally. Register the middleware early when probes must bypass later filters.

This is a fixed liveness response. It does not invoke ASP.NET Core health checks or inspect application dependencies. Map a separate readiness endpoint when health status must reflect application services.

## 🎯 Target frameworks

Targets .NET 8 and .NET 10. A .NET 9 application can consume the compatible net8.0 asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Package solution](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.HealthProbes)
- [Package source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.HealthProbes)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes)
- [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

## 📄 License

Licensed under the MIT License by Eigenverft.
