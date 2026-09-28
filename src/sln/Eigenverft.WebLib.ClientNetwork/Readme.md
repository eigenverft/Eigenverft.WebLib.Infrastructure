# Eigenverft.WebLib.ClientNetwork

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.ClientNetwork?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.ClientNetwork?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

ASP.NET Core middleware that exposes the current remote peer and parsed `Forwarded` / `X-Forwarded-For` values through `IClientNetworkFeature`.

## Use

```csharp
using Eigenverft.WebLib.ClientNetwork;

WebApplication app = builder.Build();
app.UseClientNetworkFeature();
```

Downstream code reads `context.Features.Get<IClientNetworkFeature>()`. The feature preserves malformed tokens and does not establish proxy trust. Put it before Forwarded Headers for the direct peer, or after trusted Forwarded Headers for the rewritten address. NetLib Networking and Middleware.Primitives are restored transitively.

See the [package usage guide](../../prj/Eigenverft.WebLib.ClientNetwork/NugetAssets/Readme.md) for API details.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.ClientNetwork/` and `../../prj/Eigenverft.WebLib.ClientNetwork.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
