# Middleware infrastructure

WP3 extracts small, general ASP.NET Core middleware infrastructure from the legacy `Eigenverft.Routed.RequestFilters` design without shrinking or modifying that legacy package.

## Client network feature

`UseClientNetworkFeature()` installs shared middleware that creates one typed `IClientNetworkFeature` per request and stores it in `HttpContext.Features`.

Call `UseClientNetworkFeature()` before middleware such as `UseForwardedHeaders()` that rewrites `HttpContext.Connection.RemoteIpAddress`. The client-network feature captures the remote address as it exists at its position in the pipeline, so this ordering is required when `RemoteIpAddress` must represent the actual network peer.

The feature always exposes the normalized actual `HttpContext.Connection.RemoteIpAddress`. IPv4-mapped IPv6 addresses are normalized to IPv4. Existing standardized `Forwarded` `for=` values and `X-Forwarded-For` values are collected into one typed chain. Each entry preserves its source, raw token, normalized parsed IP when available, and malformed status.

This layer deliberately makes no trust, legitimacy, proxy-authority, or request-behavior decision. Downstream filters/evaluators own those decisions.

## Shared middleware composition

`UseMiddlewareOnce<T>()` keeps the legacy idea but narrows its contract: it deduplicates convention middleware within normal linear pipelines and native non-rejoining `Map` branches using a type-bound marker in `IApplicationBuilder.Properties`. It intentionally does not attempt graph-wide deduplication for rejoining `UseWhen` graphs. The generic `UseMiddleware<T>()` overload is retained for trimming/AOT friendliness.

`EnsureServicesRegistered(...)` keeps the developer-experience goal that `UseFoo()` should explain a missing `AddFoo()`, but registration checks now use `IServiceProviderIsService`. Required services are not instantiated merely to probe registration. Open generic definitions are rejected explicitly because the default .NET probe treats open generic definitions as non-resolvable; callers should check a representative closed service or a dedicated marker.

`HttpContextFeatureExtensions` is a thin typed facade over `HttpContext.Features` only: `GetFeature<T>()`, `GetRequiredFeature<T>()`, `TryGetFeature<T>()`, `SetFeature<T>()`, `RemoveFeature<T>()`, plus parameterless and factory-based `GetOrCreateFeature<T>()` overloads.

## C4: no ConfiguredOptionsMonitor port

### Legacy behavior and consumers

Legacy `ConfiguredOptionsMonitor<TOptions>` wrapped an `IOptionsMonitor<TOptions>`, created a new `TOptions`, shallow-copied public writable properties by reflection, and then applied a `UseX(Action<TOptions>)` delegate. Many legacy RequestFilters middleware extensions passed that decorator directly to middleware so global/reloadable DI options could be overlaid at pipeline-composition time.

That behavior is not a safe generic foundation:

- the clone is shallow, so mutable reference-valued properties remain shared with the global instance;
- reflection plus a `new()` constraint exists only to manufacture the copy;
- `CurrentValue` and `Get(name)` return overlaid copies, while `OnChange` forwards the inner monitor's unmodified values;
- it creates a second options-composition mechanism after the application service provider has already been built.

A workspace consumer review found no current WebLib `develop` consumer for this historical runtime-overlay model. The parallel WP1, WP4, WP5, and WP6 designs also explicitly do not require `UseX(Action<TOptions>)` local overrides.

### Current .NET pattern

Current .NET options already provide reloadable `IOptionsMonitor<TOptions>`, named options, `IOptionsFactory<TOptions>`, configure/post-configure actions, and monitor caching/invalidation. When a common baseline must apply to both default and named variants, configure that baseline for all names (for example with `ConfigureAll`) and then add the named configuration. `ConfiguredOptionsDesignTests` verifies this baseline-plus-independent-variant pattern on both target frameworks at compile time and on the available runtime.

Named options are **not** claimed to be a drop-in replacement for an arbitrary `UseX(Action<TOptions>)` supplied after DI has been built. A variant that is part of application configuration should be established during service registration. A genuinely pipeline-local immutable setting should be modeled explicitly by the concrete feature.

### Decision

WP3 therefore does **not** port `ConfiguredOptionsMonitor<TOptions>` and does not introduce a new generic local-options-overlay abstraction.

If a future real middleware needs multiple configured variants, prefer a feature-specific service-registration API using named options. If a future feature genuinely needs a pipeline-local setting at `UseX(...)` time, model it explicitly for that feature. Reconsider shared abstraction only after multiple current consumers demonstrate the same requirement.

Platform references used for the decision:

- [Options pattern in .NET](https://learn.microsoft.com/dotnet/core/extensions/options)
- [Factory-based middleware activation in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/middleware/extensibility?view=aspnetcore-10.0)
