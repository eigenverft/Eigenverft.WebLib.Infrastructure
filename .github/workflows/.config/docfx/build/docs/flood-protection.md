# Request flood protection

WebLib provides a small registration and options layer over the ASP.NET Core rate-limiting framework. It deliberately does **not** carry forward the legacy rolling buckets, hysteresis, or `Task.Delay` request shaping from `Eigenverft.Routed.RequestFilters`.

## Basic setup

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

builder.Services.AddWebLibFloodProtection(options =>
{
    options.TokenLimit = 40;
    options.TokensPerPeriod = 10;
    options.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
    options.QueueLimit = 20;
    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
});

// If a trusted reverse proxy supplies the client address, configure and run
// Forwarded Headers before rate limiting so Connection.RemoteIpAddress is correct.
app.UseForwardedHeaders();
app.UseRateLimiter();
```

The primary limiter is a `TokenBucketRateLimiter` partitioned by the normalized value of `HttpContext.Connection.RemoteIpAddress`. IPv4-mapped IPv6 addresses are normalized to IPv4, and IPv6 scope IDs are excluded from the partition identity. WebLib does not independently trust or parse forwarding headers; proxy trust remains the application's responsibility.

Requests without a client IP use one shared partition by default. `MissingClientIpBehavior.BypassPerIpLimit` can instead bypass only the per-IP token bucket. If the optional global concurrency guard is enabled, it still applies to missing-IP requests in bypass mode.

## Defaults and tuning

The convenience defaults are startup values, not universal security limits:

| Setting | Default | Meaning |
|---|---:|---|
| `TokenLimit` | `40` | Maximum accumulated tokens per client-IP partition; allows a short burst. |
| `TokensPerPeriod` | `10` | Tokens restored each replenishment period. |
| `ReplenishmentPeriod` | `1 second` | Default sustained rate is therefore approximately 10 requests/second per IP. |
| `QueueLimit` | `20` | At most 20 queued permits per IP after immediate tokens are exhausted. |
| `QueueProcessingOrder` | `OldestFirst` | Older queued requests are served first. |
| `MissingClientIpBehavior` | `SharedPartition` | Missing-IP traffic shares one token bucket instead of bypassing protection. |
| `RejectionStatusCode` | `429` | Returned by the ASP.NET Core middleware when the limiter rejects. |
| Global concurrency | disabled | No whole-application concurrency cap unless explicitly configured. |

Load-test and tune these values for each consumer. Traffic shape, endpoint cost, proxy topology, expected NAT concentration, and legitimate burst behavior all affect suitable limits.

The options can also be configured through the normal .NET options pipeline before or after the WebLib registration, for example:

```csharp
builder.Services.Configure<WebLibFloodProtectionOptions>(
    builder.Configuration.GetSection("WebLib:FloodProtection"));
builder.Services.AddWebLibFloodProtection();
```

## Rejection and observability

When the token bucket can estimate when another permit will become available, the framework exposes `MetadataName.RetryAfter`. WebLib copies that metadata to a `Retry-After` response header when available; it does not invent a retry time for limiters that cannot estimate one, such as a pure concurrency rejection.

Rejected requests are logged at warning level by default with the normalized client partition and any framework-provided retry delay. This can be disabled through `LogRejectedRequests`. The native ASP.NET Core rate-limiting middleware remains in use, so its normal diagnostics and metrics are preserved rather than replaced by a custom middleware implementation.

## Optional global concurrency guard

Set `GlobalConcurrencyPermitLimit` to add a `ConcurrencyLimiter` after the per-IP token bucket:

```csharp
builder.Services.AddWebLibFloodProtection(options =>
{
    options.GlobalConcurrencyPermitLimit = 200;
    options.GlobalConcurrencyQueueLimit = 20;
    options.GlobalConcurrencyQueueProcessingOrder = QueueProcessingOrder.OldestFirst;
});
```

The per-IP token bucket remains the primary flood-control mechanism. The concurrency limiter is an optional second guard for total in-flight work across all client IPs.

## Legacy replacement scope

`RequestRateSmoothing` is replaced by the framework token-bucket model described above. Its custom rolling buckets, hysteresis state, client cleanup loop, and delay steps are intentionally not ported.

`RequestDelayThrottling` is dropped rather than reimplemented. Its only distinct behavior was per-client counting followed by artificial delay and stale-client cleanup; it did not provide a separate protection case that needs to survive. Bounded framework queueing plus rejection when the queue is full replaces that delay-only shaping model.
