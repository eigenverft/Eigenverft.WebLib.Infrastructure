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

## C4: use-site options overrides without the legacy monitor

### Legacy behavior and problems

Legacy `ConfiguredOptionsMonitor<TOptions>` wrapped an `IOptionsMonitor<TOptions>`, created a new `TOptions`, shallow-copied public writable properties by reflection, and then applied a `UseX(Action<TOptions>)` delegate. RequestFilters middleware extensions passed that decorator directly to middleware so a concrete pipeline use could keep the normal reloadable baseline and override only the values that differed there.

The consumer idea remains useful, but that implementation is not a safe foundation:

- the reflection copy is shallow, so mutable reference-valued properties can remain shared with the global instance;
- reflection plus a `new()` constraint exists only to manufacture that copy;
- `CurrentValue` and `Get(name)` return overlaid copies, while `OnChange` forwards the inner monitor's unmodified values;
- the library owns monitor behavior that the framework already provides.

### Framework-based composition

WebLib keeps the feature-specific consumer API but rebuilds each local variant through the standard registered options components. Reusable middleware libraries can call `ApplicationBuilderMiddlewareExtensions.CreateUseSiteOptionsMonitor<TOptions>` from their own `UseX(options => ...)` overloads; ordinary applications normally use the feature-specific middleware API instead. The public method delegates to an internal implementation that creates a framework `OptionsFactory<TOptions>` from the registered configure, post-configure, and validation services, appends the use-site delegate as the final post-configure step, and places that factory behind a separate framework `OptionsMonitor<TOptions>` with its own cache and the registered change-token sources.

The resulting order is:

```text
code defaults
→ registered Configure steps (including configuration binding)
→ later AddX code configuration
→ registered PostConfigure steps
→ local UseX(options => ...) override
→ registered validation
```

Because `OptionsFactory<TOptions>` constructs a fresh options instance before replaying that pipeline, a use-site override does not need reflection cloning. Configuration helpers such as NetLib collection-replacement binding remain responsible for how code defaults and configuration form the baseline; the local override simply runs after that baseline has been rebuilt.

The separate `OptionsMonitor<TOptions>` preserves reload semantics: when a registered change token fires, its isolated cache is invalidated, the baseline is rebuilt from current configuration, and the same local override is applied again. `OnChange` subscribers receive that rebuilt value after the local override has been reapplied. The application's registered/global options monitor is never mutated, and other middleware uses have separate monitors/caches and therefore remain unaffected.

### Mutable reference values

Fresh factory-created options isolate normal code-default collections, arrays, dictionaries, nested mutable objects, and values rebuilt by configuration binding. One explicit boundary remains: if consumer-owned configure/post-configure code deliberately assigns the same mutable object instance (for example a captured singleton list) to every newly created options instance, WebLib does not deep-clone that shared object. Avoid deliberately shared mutable instances when use-site mutation is required.

### Canonical host redirect example

Configure the shared baseline during service registration and override only the values that differ at one middleware placement:

```csharp
builder.Services.AddCanonicalHostRedirect(options =>
{
    options.PrimaryApexHost = "example.com";
});

var app = builder.Build();

app.UseCanonicalHostRedirect(options =>
{
    options.HttpsTargetPort = 8443;
});
```

The `HttpsTargetPort` override belongs only to this concrete middleware use. A different `UseCanonicalHostRedirect()` call still uses the shared baseline.

Platform references:

- [Options pattern in .NET](https://learn.microsoft.com/dotnet/core/extensions/options)
- [OptionsFactory<TOptions>](https://learn.microsoft.com/dotnet/api/microsoft.extensions.options.optionsfactory-1)
- [OptionsMonitor<TOptions>](https://learn.microsoft.com/dotnet/api/microsoft.extensions.options.optionsmonitor-1)
