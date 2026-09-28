# Eigenverft.WebLib.RequestTrafficLogging

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficLogging?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficLogging?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

A single structured request-traffic record built on ASP.NET Core HTTP Logging, with explicit completion, abort, and fault outcomes.

## Use

```csharp
using Eigenverft.WebLib.RequestTrafficLogging;

builder.Services.AddRequestTrafficLogging();

WebApplication app = builder.Build();
app.UseRequestTrafficLogging();
```

The middleware activates HTTP Logging itself; do not add another `UseHttpLogging()` to the same linear pipeline. Its default field groups are Core and Routing; headers and body capture are opt-in. Add `app.UseClientNetworkFeature()` before it when forwarded-IP details are required. Place it before exception-handling middleware to classify handled exceptions as faulted.

The [package usage guide](../../prj/Eigenverft.WebLib.RequestTrafficLogging/NugetAssets/Readme.md) documents the 1.0.0.8 record schema, outcome semantics, header modes, and body metadata.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.RequestTrafficLogging/` and `../../prj/Eigenverft.WebLib.RequestTrafficLogging.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
