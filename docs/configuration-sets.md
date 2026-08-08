# Configuration Sets

`ConfigurationSet` groups one logical configuration axis behind a small set of named values such as `Stable`, `Candidate`, `Normal`, `Degraded`, or `Failover`.

The feature is built on normal .NET `IConfiguration`. It does not introduce a second settings system. Instead, it coordinates one or more switchable JSON configuration sources so that application code can reason about a semantic state such as `RoutingProfile = Failover` instead of editing several unrelated settings individually.

## Core model

A configuration set has:

- a caller-defined set name;
- one initial active value;
- one or more allowed values;
- zero or more switchable JSON sources bound to that value axis;
- observable switch results and lifecycle events;
- optional persistence through `ConfigurationSets.json`.

Multiple sets are independent axes and may coexist in the same application.

```text
ReleaseChannel     = Stable
OperationalProfile = Normal
RoutingProfile     = Primary
DiagnosticsProfile = Standard
```

This avoids constructing combined names such as `Production-Stable-Normal-Primary`.

## Smallest registration

```csharp
var routingProfile = builder.AddConfigurationSet(
    "RoutingProfile",
    "Primary",
    "Canary",
    "Failover");
```

A set with only one value is valid and useful for incremental adoption:

```csharp
var routingProfile = builder.AddConfigurationSet(
    "RoutingProfile",
    "Primary");
```

A later release may add `Canary` or `Failover` without changing the abstraction.

## Conventional directory layout

The convenient directory form maps each set value to a sibling directory:

```csharp
builder
    .AddConfigurationSet(
        "RoutingProfile",
        "Primary",
        "Canary",
        "Failover")
    .AddSwitchableJson(
        "AppSettings/Routing",
        "Routes.json",
        "Clusters.json");
```

```text
ContentRoot/
└── AppSettings/
    └── Routing/
        ├── Primary/
        │   ├── Routes.json
        │   └── Clusters.json
        ├── Canary/
        │   ├── Routes.json
        │   └── Clusters.json
        └── Failover/
            ├── Routes.json
            └── Clusters.json
```

The directory convention is only convenience. It is not the underlying contract.

## Arbitrary source-path mapping

The canonical flexible form accepts a complete source-path resolver:

```csharp
builder
    .AddConfigurationSet(
        "RoutingProfile",
        "Primary",
        "Failover")
    .AddSwitchableJson(value => value switch
    {
        "Primary"  => "AppSettings/Routing/routes-primary.json",
        "Failover" => "AppSettings/Routing/emergency-routing-v3.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    });
```

The value may appear in the filename instead of a directory:

```csharp
builder
    .AddConfigurationSet(
        "ResilienceProfile",
        "Normal",
        "UpstreamDegraded",
        "FailFast")
    .AddSwitchableJson(
        value => $"AppSettings/Resilience/HttpResilience.{value}.json");
```

Two logical values may deliberately map to the same physical source. In that case the logical set value can change while `SourceChanged` and `ConfigurationChanged` remain false.

Resolvers are evaluated for all allowed values during startup and the resulting mapping is frozen. Runtime switches do not execute arbitrary resolver code again.

## Multiple files in one set

Several files can move together as one semantic configuration lane:

```csharp
builder
    .AddConfigurationSet(
        "OperationalProfile",
        "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Operations",
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");
```

The files remain independent `ISwitchableJsonConfiguration` runtimes. The set coordinator performs a preflight across every bound participant before the first commit.

A preparation rejection leaves every participant on the previous known-good set value. A rare failure after an earlier participant has already committed is reported as `PartiallyCommitted` and sets `IsConsistent` to `false`; the library does not claim a transaction that it cannot guarantee.

## Central state file

A state store can materialize and read a self-describing central control file:

```csharp
builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "Value": "Primary",
      "AllowedValues": [
        "Primary",
        "Canary",
        "Failover"
      ]
    }
  }
}
```

`Value` is the selected state. `AllowedValues` is descriptive metadata materialized from code; editing it does not authorize new values.

A relative state-file path is resolved from the host content root:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Routing/
        └── ...
```

With state-file watching enabled, editing `Value` can trigger a coordinated runtime switch. With `reloadOnChange: false`, the file is applied at startup and not watched during the running host.

Per-set mixed `Runtime` / `StartupOnly` behavior is not yet part of the current public contract. See `demo-dev-review.md` for the design discussion until that capability is implemented.

## Programmatic switching

The coordinator is available through keyed DI and can be switched directly:

```csharp
var coordinator = services.GetRequiredKeyedService<IConfigurationSetCoordinator>(
    "RoutingProfile");

ConfigurationSetSwitchResult result = coordinator.TrySwitch("Failover");
```

A direct coordinator switch is currently a runtime operation only. It does not automatically rewrite `ConfigurationSets.json`. This distinction is intentional until persistent programmatic switching receives its own explicit state-store API.

## Observability

Every completed coordinator operation has structured information including:

- previous, requested and active values;
- `ValueChanged`;
- `SourceChanged`;
- `ConfigurationChanged`;
- `HasChanges`;
- consistency state;
- failure classification;
- per-participant source and configuration changes.

A DI-wide event hub aggregates completed events from all registered configuration sets:

```csharp
public sealed class ConfigurationSetLogger : IHostedService, IDisposable
{
    private readonly IConfigurationSetEventHub _events;
    private readonly ILogger<ConfigurationSetLogger> _logger;
    private IDisposable? _subscription;

    public ConfigurationSetLogger(
        IConfigurationSetEventHub events,
        ILogger<ConfigurationSetLogger> logger)
    {
        _events = events;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _events.Subscribe(notification =>
        {
            _logger.LogInformation(
                "Configuration set {Name}: {Previous} -> {Active}; status={Status}; changed={Changed}",
                notification.Result.Name,
                notification.Result.PreviousValue,
                notification.Result.ActiveValue,
                notification.Result.Status,
                notification.Result.HasChanges);
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _subscription?.Dispose();
}
```

Subscriber failures are isolated from the already completed switch and from other subscribers.

## What Configuration Sets are not

Configuration Sets are not feature flags, environment detection, a deployment system, or a replacement for `IConfiguration`.

They add a semantic coordination layer above configuration sources: a named value represents a reviewed configuration baseline, and one set may coordinate multiple files that belong to that baseline.

## Current guarantees

- one or many independent configuration sets per process;
- one-value sets are valid;
- arbitrary source paths per value;
- one or many JSON participants per set;
- prepare-all before the first participant commit;
- last-known-good behavior when preparation rejects;
- explicit partial-commit consistency reporting;
- thread-safe coordinator switching;
- observable set-level events with participant detail;
- self-describing central state file;
- startup-only state-file behavior globally through `reloadOnChange: false`;
- runtime file watching when enabled.

## Current open V1 decisions

Two contracts remain intentionally explicit rather than hidden behind convenience behavior:

1. Mixed per-set `Runtime` / `StartupOnly` policy in one state file, including visible read-only policy metadata and pending-restart state.
2. A persistent programmatic switch API that changes desired state in `ConfigurationSets.json` as well as runtime state, distinct from the existing ephemeral `IConfigurationSetCoordinator.TrySwitch(...)` primitive.

Those should be resolved before an administrative HTTP integration claims a persistent control-plane contract.
