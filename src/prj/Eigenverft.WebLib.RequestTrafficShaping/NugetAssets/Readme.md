# Eigenverft.WebLib.RequestTrafficShaping

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficShaping?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficShaping?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

ASP.NET Core rate limiting composed from per-client and server-wide token buckets plus an optional application-wide concurrency limit.

## ✨ At a glance

| Capability | Details |
| --- | --- |
| Registration | `AddRequestTrafficShaping(...)` binds and validates options from `RequestTrafficShaping`. |
| Pipeline | Activate the registered limiter with ASP.NET Core's `app.UseRateLimiter()`. |
| Defaults | Per-client: 10 requests/second, burst 40, queue 20; server-wide: 10,000 for rate, burst, and queue. |
| Concurrency | Optional `GlobalConcurrencyLimit` is off by default. Defaults are starting points, not capacity guarantees. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.RequestTrafficShaping
```

## 🚀 Quick start

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

The same settings can be supplied under the `RequestTrafficShaping` configuration section. Class defaults are applied first, configuration binding next, and the optional callback last.

## Limiter behavior and configuration

The chain is per-client token bucket, then aggregate server-wide token bucket, then the optional global concurrency limiter. Both token-bucket layers are enabled by default. Each uses a one-second replenishment period and a bounded oldest-first queue. Rejections return 429; a `Retry-After` header is added when native limiter metadata supplies a positive retry delay.

The per-client partition uses normalized `HttpContext.Connection.RemoteIpAddress` directly; this package does not depend on `Eigenverft.WebLib.ClientNetwork`. IPv4-mapped IPv6 is normalized to IPv4 and IPv6 scope IDs are ignored. When there is no peer IP, `MissingClientIpBehavior.SharedPartition` (the default) groups missing addresses together. `BypassPerIpLimit` skips only the per-client limiter; the server-wide and optional concurrency limits still apply.

When a trusted reverse proxy provides client IPs, configure ASP.NET Core Forwarded Headers and run it before `UseRateLimiter()`. Validate trust boundaries before accepting forwarded addresses.

Burst and request-rate values must be positive, queue limits non-negative, and `BurstSize` must be at least `RequestsPerSecond`. The optional concurrency limit must be positive when set. Options are validated on startup and construct the limiters then; changing configuration does not live-reconfigure running token buckets. Tune the generous server-wide starting values against representative application load.

## 🎯 Target frameworks

Targets `net8.0` and `net10.0`; .NET 9 applications can consume the compatible `net8.0` asset.

## 🔗 Project links

- [Repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Solution and tests](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.RequestTrafficShaping)
- [Project source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.RequestTrafficShaping)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping)

## 📄 License

MIT; see the [repository license](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE).
