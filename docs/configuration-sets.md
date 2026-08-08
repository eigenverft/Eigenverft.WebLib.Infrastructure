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

Set names and values are intentionally strings because they are also stable, operator-visible identities in desired-state documents and external control surfaces. Applications that want compile-time reuse can define constants around those identities. The library does not implicitly serialize enums with `ToString()`, because renaming an enum member should not silently rename persisted configuration state.

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

When the whole group needs non-default source behavior, the same convenience path accepts shared `SwitchableJsonRegistrationOptions` instead of forcing each file back into low-level registration:

```csharp
builder
    .AddConfigurationSet(
        "OperationalProfile",
        "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Operations",
        new SwitchableJsonRegistrationOptions
        {
            Optional = true,
            ReloadOnChange = true,
            ReloadDelayMilliseconds = 500,
            RuntimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood,
        },
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");
```

The files remain independent switchable provider/runtimes for loading, active-file watching, last-known-good handling and lifecycle observation. Once bound, however, the configuration set owns manual source selection for that runtime: direct participant `PrepareSwitch`/`TrySwitch` calls are rejected, and one runtime cannot belong to two sets at the same time. The set coordinator performs a preflight across every bound participant before the first commit. Prepared participant state is then committed without publishing observer notifications; after all successful commits and coordinator state finalization, `IConfiguration` reload and lifecycle notifications are released outside coordinator locks. A consumer reacting to a successful multi-file switch therefore sees the final committed baseline rather than a notification after each intermediate participant commit.

A preparation rejection leaves every participant on the previous successfully coordinated set value. A rare race can still make a later prepared commit stale after an earlier participant has already committed; that outcome is reported as `PartiallyCommitted` and sets `IsConsistent` to `false`. This improves observer semantics but deliberately does not claim a fully atomic multi-provider transaction.

## Central state file

A state store can materialize and read a self-describing central control file:

```csharp
builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "DesiredValue": "Primary",
      "AllowedValues": [
        "Primary",
        "Canary",
        "Failover"
      ]
    }
  }
}
```

`DesiredValue` is the persisted requested state and may differ from the running `ActiveValue`, for example with `StartupOnly` or after a rejected runtime activation. `AllowedValues` and `ApplyMode` are descriptive metadata materialized from code; editing them does not authorize new values or change policy.

The built-in JSON store treats this as a complete authoritative desired-state document: every configuration set registered before the store is added must be present, and unknown or missing set entries reject the document before any set is changed. Canonical materialization always writes the full registered set collection.

A relative state-file path is resolved from the host content root:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Routing/
        └── ...
```

With state-file watching enabled, editing `DesiredValue` can trigger a coordinated runtime switch. With `watchForChanges: false`, the control file is applied at startup and not watched during the running host.

Each set can additionally declare a code-owned desired-state apply mode:

```csharp
builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .ApplyMode(ConfigurationSetApplyMode.StartupOnly);

builder
    .AddConfigurationSet(
        "RoutingProfile",
        "Primary",
        "Failover"); // Runtime is the default.
```

The canonical state file materializes that policy as read-only descriptive metadata:

```json
{
  "ConfigurationSets": {
    "ReleaseChannel": {
      "DesiredValue": "Beta",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    },
    "RoutingProfile": {
      "DesiredValue": "Failover",
      "AllowedValues": [ "Primary", "Failover" ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Editing `ApplyMode` in JSON does not change policy; the code-owned value wins and is rematerialized. During a running host, a changed `StartupOnly` value becomes `DesiredValue` and is reported through `PendingRestartChanges` / `HasPendingRestart` without changing `ActiveValue`. The next host startup applies the desired value.

## Programmatic control without a state file

`ConfigurationSets.json` is optional. The code declaration always defines the complete allowed set plus its `InitialValue`:

```csharp
builder
    .AddConfigurationSet(
        "RoutingProfile",
        initialValue: "Primary",
        "Canary",
        "Failover")
    .AddSwitchableJson(value => $"AppSettings/Routing/Routes.{value}.json");
```

With no desired-state store, the process starts on `Primary`. A restart returns to that code-defined initial value. Runtime control is available through the automatically registered, persistence-neutral manager:

```csharp
var sets = services.GetRequiredService<IConfigurationSetManager>();

IReadOnlyList<ConfigurationSetStatus> status = sets.GetStatus();
// status[0]: Name, InitialValue, ActiveValue, AllowedValues, IsConsistent, participants

bool switched = sets.TrySwitchRuntime(
    "RoutingProfile",
    "Failover",
    out ConfigurationSetSwitchResult? result);

if (!switched)
{
    // result == null: unknown set name.
    // result != null: known set, but the runtime switch was rejected/partially committed.
}
else
{
    // The requested value is now the fully coordinated active value.
    // Successful configuration changes are already published when the call returns.
}
```

This is deliberately **ephemeral runtime control**. No persistence is implied by `TrySwitchRuntime(...)`. Its boolean follows normal `Try...` expectations: `true` means the requested value became (or already was) the fully coordinated active value; a non-null failed `result` still preserves the detailed rejection/partial-commit diagnostics.

For application/control-plane code, the intended entry points are deliberately narrow:

| Need | Preferred dependency |
| --- | --- |
| Inspect sets or perform an ephemeral live switch | `IConfigurationSetManager` |
| Read/write persistent desired state | `IConfigurationSetDesiredStateStore` |
| Explicitly operate the built-in JSON file (`Reload`, `Materialize`, `FilePath`) | `IConfigurationSetStateStore` |
| Advanced integration with one exact set | keyed `IConfigurationSetCoordinator` |

A controller therefore does not need keyed DI lookup and does not need a JSON state file merely to inspect or switch configuration sets.

## Optional persistent desired state

Persistence is a separate capability. The built-in `ConfigurationSets.json` adapter additionally registers the narrow, file-neutral interface:

```csharp
var desiredState = services.GetRequiredService<IConfigurationSetDesiredStateStore>();

IReadOnlyList<ConfigurationSetStateStatus> status =
    desiredState.GetDesiredStateStatus();

ConfigurationSetStateApplyResult result =
    desiredState.TrySetDesiredValue("RoutingProfile", "Failover");
```

A control plane depending on `IConfigurationSetDesiredStateStore` does not need to know about `FilePath`, `Reload()` or `Materialize()`. The current JSON implementation persists the canonical desired state before any live switch is attempted. For a `Runtime` set, the coordinator is then switched immediately. If candidate preparation rejects, desired state remains persisted while active runtime stays on last-known-good and `HasDesiredStateDrift` exposes the difference.

For a `StartupOnly` set, `TrySetDesiredValue(...)` persists the new value without changing the running coordinator and reports pending restart.

The file-specific `IConfigurationSetStateStore` remains available when an application intentionally wants local file operations such as `Reload()` or `Materialize()`. The persistence-neutral interface is the preferred dependency for a controller or external control-plane adapter.

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

## Regression coverage

The automated suite includes a realistic Program.Main-style system regression that combines:

```text
RoutingProfile
  -> arbitrary value => sourcePath mappings
  -> two coordinated JSON participants

OperationalProfile
  -> conventional per-value directories
  -> three coordinated JSON participants

ReleaseChannel
  -> StartupOnly desired state

ConfigurationSets.json
  -> real filesystem watcher

IConfigurationSetEventHub
  -> real IHostedService consumer

persistent TrySetDesiredValue(...)
  -> runtime persistence and switch

host restart
  -> pending StartupOnly value becomes active
```

The test asserts the resulting `IConfiguration` values, participant-level lifecycle information, pending-restart state, persistent desired state, and clean restart convergence.

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
- per-set `Runtime` / `StartupOnly` desired-state apply policy;
- visible code-owned `ApplyMode` metadata;
- desired-vs-active status, generic desired-state drift, and pending-restart reporting;
- explicit persistent `TrySetDesiredValue(...)` control distinct from ephemeral coordinator switching;
- persistent Runtime requests retain desired state when last-known-good runtime activation rejects;
- internal state-file writes do not echo as duplicate watcher apply events;
- global desired-state-file watcher disable through `watchForChanges: false`;
- runtime file watching when enabled.

The core state-management distinction needed by a later administrative HTTP or CLI integration is now explicit: those surfaces can choose persistent state-store control or ephemeral coordinator control rather than relying on hidden behavior.
