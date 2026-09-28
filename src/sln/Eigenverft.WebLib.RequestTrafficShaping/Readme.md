# Eigenverft.WebLib.RequestTrafficShaping

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficShaping?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficShaping?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Global ASP.NET Core rate limiting with per-client and server-wide token buckets plus an optional concurrency cap.

## Use

```csharp
using Eigenverft.WebLib.RequestTrafficShaping;

builder.Services.AddRequestTrafficShaping(options =>
{
    options.PerClient.BurstSize = 40;
    options.PerClient.RequestsPerSecond = 10;
    options.PerClient.QueueLimit = 20;
    options.ServerWide.BurstSize = 10_000;
    options.ServerWide.RequestsPerSecond = 10_000;
    options.ServerWide.QueueLimit = 10_000;
    // options.GlobalConcurrencyLimit = 500;
});

WebApplication app = builder.Build();
app.UseRateLimiter();
```

The default per-client partition uses normalized `HttpContext.Connection.RemoteIpAddress`; it does not consume ClientNetwork's feature. Configure trusted Forwarded Headers before rate limiting when requests arrive through a proxy. Both token buckets are enabled by default; `GlobalConcurrencyLimit` is optional. Treat the server-wide 10,000-per-second values as starting points and tune for the service.

See the [package usage guide](../../prj/Eigenverft.WebLib.RequestTrafficShaping/NugetAssets/Readme.md) for defaults, validation, missing-IP behavior, and configuration.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.RequestTrafficShaping/` and `../../prj/Eigenverft.WebLib.RequestTrafficShaping.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
