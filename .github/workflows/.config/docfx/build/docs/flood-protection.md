# Request flood protection

WebLib provides a small registration and options layer over the ASP.NET Core rate-limiting framework. It deliberately does **not** carry forward the legacy rolling buckets, hysteresis, or `Task.Delay` request shaping from `Eigenverft.Routed.RequestFilters`.

## Basic setup

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

builder.Services.AddFloodProtection();

// If a trusted reverse proxy supplies the client address, run Forwarded Headers first
// so Connection.RemoteIpAddress contains the trusted client IP.
app.UseForwardedHeaders();
app.UseRateLimiter();
```

`AddFloodProtection()` configures the global ASP.NET Core limiter. `UseRateLimiter()` stays explicit because it is the native middleware that performs the actual request limiting.

The primary limiter is a `TokenBucketRateLimiter` partitioned by the normalized value of `HttpContext.Connection.RemoteIpAddress`. IPv4-mapped IPv6 addresses are normalized to IPv4, and IPv6 scope IDs are excluded from the partition identity. WebLib does not independently trust or parse forwarding headers; proxy trust remains the application's responsibility.

Requests without a client IP use one shared partition by default. `MissingClientIpBehavior.BypassPerIpLimit` can instead bypass only the per-IP token bucket. If the optional global concurrency guard is enabled, it still applies to missing-IP requests in bypass mode.

## Defaults and tuning

The defaults are only practical startup values, not universal security limits. Load-test and tune them for each consumer: endpoint cost, proxy topology, expected NAT concentration, legitimate burst behavior, and deployment capacity all matter.

| Setting | Default | Meaning |
|---|---:|---|
| `BurstSize` | `40` | Maximum accumulated tokens per client-IP partition. |
| `RequestsPerSecond` | `10` | Tokens replenished each second per client-IP partition. |
| `QueueLimit` | `20` | Maximum queued requests per client-IP partition after immediate tokens are exhausted. |
| `GlobalConcurrencyLimit` | disabled | Optional cap for total in-flight requests across all client IPs. |

Queued requests are processed `OldestFirst`. Token buckets replenish automatically once per second. Those framework mechanics are intentionally not exposed as first-class WebLib options.

A typical tuned setup remains one registration call:

```csharp
builder.Services.AddFloodProtection(options =>
{
    options.BurstSize = 60;
    options.RequestsPerSecond = 20;
    options.QueueLimit = 10;
    options.GlobalConcurrencyLimit = 200;
});
```

The options can also be configured through the normal .NET options pipeline, for example:

```csharp
builder.Services.Configure<WebLibFloodProtectionOptions>(
    builder.Configuration.GetSection("WebLib:FloodProtection"));
builder.Services.AddFloodProtection();
```

## Rejection, Retry-After, and observability

Rejected requests use HTTP `429 Too Many Requests`. When the token bucket provides `MetadataName.RetryAfter`, WebLib copies the framework-provided delay to the standard `Retry-After` response header. It does not invent a retry duration for limiters that cannot estimate one, such as a pure concurrency rejection.

The native ASP.NET Core rate-limiting middleware remains in use, so its built-in rate-limiting metrics and diagnostics stay available. WebLib does not add a second limiter engine or a separate metrics subsystem.

## Optional global concurrency guard

Set `GlobalConcurrencyLimit` to add a `ConcurrencyLimiter` after the per-IP token bucket:

```csharp
builder.Services.AddFloodProtection(options =>
{
    options.GlobalConcurrencyLimit = 200;
});
```

The global concurrency limiter uses no additional queue. The bounded per-IP queue remains the only queue configured by this convenience layer, which keeps total backlog behavior easy to reason about.

## Legacy replacement scope

`RequestRateSmoothing` is replaced by the framework token-bucket model described above. Its custom rolling buckets, hysteresis state, client cleanup loop, and delay steps are intentionally not ported.

`RequestDelayThrottling` is dropped rather than reimplemented. Its delay-only shaping behavior is replaced by bounded framework queueing plus rejection when the queue is full.
