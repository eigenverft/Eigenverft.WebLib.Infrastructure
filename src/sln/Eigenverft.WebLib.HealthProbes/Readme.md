# Eigenverft.WebLib.HealthProbes

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.HealthProbes?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.HealthProbes?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Adds a fixed liveness response at `/health` and suppresses favicon requests that originate from a browser opening that probe.

## Use

```csharp
using Eigenverft.WebLib.HealthProbes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

app.UseHealthProbeFaviconAware();

// Register before middleware that the health request should bypass.
app.Run(context => context.Response.WriteAsync("application"));
```

The middleware handles GET and HEAD only; `/health` returns a fixed `200 OK` and does not invoke ASP.NET Core health checks or inspect application dependencies. Register it early when probes should bypass later middleware. Use a separate readiness endpoint for dependency-aware status.

See the [package usage guide](../../prj/Eigenverft.WebLib.HealthProbes/NugetAssets/Readme.md) for response and favicon matching behavior.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.HealthProbes/` and `../../prj/Eigenverft.WebLib.HealthProbes.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
