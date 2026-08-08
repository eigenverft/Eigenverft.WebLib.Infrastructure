# Configuration Sets

`ConfigurationSet` groups one logical configuration axis behind a small set of named values such as `Stable`, `Candidate`, `Normal`, `Degraded`, or `Failover`.

The feature is built on normal .NET `IConfiguration`. It does not introduce a second settings system. Instead, it coordinates one or more switchable JSON configuration sources so that application code can reason about a semantic state such as `RoutingProfile = Failover` instead of editing several unrelated settings individually.

## Usage first: complete `Program.cs` examples

The examples in this section are intentionally application-oriented. They show the complete registration flow first; the detailed contracts and failure semantics follow afterward.

### 1. Code-only runtime switching

Use this when the process should always start from a code-defined `InitialValue` and runtime switches do not need to survive a restart.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigurationSet(
        "OperationalProfile",
        initialValue: "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Operations",
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");

var app = builder.Build();

app.MapGet(
    "/configuration-sets",
    (IConfigurationSetManager sets) => sets.GetStatus());

app.MapPost(
    "/configuration-sets/{name}/{value}",
    (string name, string value, IConfigurationSetManager sets) =>
    {
        bool switched = sets.TrySwitchRuntime(name, value, out ConfigurationSetSwitchResult? result);

        if (switched)
        {
            return Results.Ok(result);
        }

        return result is null
            ? Results.NotFound()
            : Results.Conflict(result);
    });

app.Run();
```

The endpoint is only an integration example; authentication, authorization and fleet orchestration belong to the application/control plane.

```text
ContentRoot/
└── AppSettings/
    └── Operations/
        ├── Normal/
        │   ├── Features.json
        │   ├── Resilience.json
        │   └── Diagnostics.json
        ├── Degraded/
        │   ├── Features.json
        │   ├── Resilience.json
        │   └── Diagnostics.json
        └── Incident/
            ├── Features.json
            ├── Resilience.json
            └── Diagnostics.json
```

No `ConfigurationSets.json` is required. The process starts on `Normal`; a successful runtime switch can move it to `Degraded` or `Incident`. After a restart it starts on `Normal` again.

### 2. Persistent desired state

Add the optional state store when an operator or control plane should select a value that survives process restarts.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigurationSet(
        "OperationalProfile",
        initialValue: "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Operations",
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");

var app = builder.Build();

app.MapGet(
    "/configuration-sets/desired",
    (IConfigurationSetDesiredStateStore desiredState) =>
        desiredState.GetDesiredStateStatus());

app.MapPut(
    "/configuration-sets/{name}/desired/{value}",
    (string name, string value, IConfigurationSetDesiredStateStore desiredState) =>
        Results.Ok(desiredState.TrySetDesiredValue(name, value)));

app.Run();
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Operations/
        ├── Normal/
        │   ├── Features.json
        │   ├── Resilience.json
        │   └── Diagnostics.json
        ├── Degraded/
        │   ├── Features.json
        │   ├── Resilience.json
        │   └── Diagnostics.json
        └── Incident/
            ├── Features.json
            ├── Resilience.json
            └── Diagnostics.json
```

A canonical state document is self-describing:

```json
{
  "ConfigurationSets": {
    "OperationalProfile": {
      "DesiredValue": "Degraded",
      "AllowedValues": [
        "Normal",
        "Degraded",
        "Incident"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

`DesiredValue` is operator/control-plane state. `AllowedValues` and `ApplyMode` are code-owned metadata and cannot grant the file additional capabilities.

### 3. Mixed production-style setup

Independent sets can use different source layouts and different apply policies in the same process. This example combines runtime routing, an operational baseline and a release channel that is intentionally applied only on startup.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigurationSet(
        "RoutingProfile",
        initialValue: "Primary",
        "Canary",
        "Failover")
    .AddSwitchableJson(value => value switch
    {
        "Primary" => "AppSettings/Routing/routes-primary.json",
        "Canary" => "AppSettings/Routing/routes-canary.json",
        "Failover" => "AppSettings/Routing/emergency-routing.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    })
    .AddSwitchableJson(value => value switch
    {
        "Primary" => "AppSettings/Routing/clusters-primary.json",
        "Canary" => "AppSettings/Routing/clusters-canary.json",
        "Failover" => "AppSettings/Routing/clusters-failover.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    });

builder
    .AddConfigurationSet(
        "OperationalProfile",
        initialValue: "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Operations",
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");

builder
    .AddConfigurationSet(
        "ReleaseChannel",
        initialValue: "Stable",
        "Beta")
    .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Features",
        "Features.json");

// Register the state file after all sets it should manage.
builder.AddConfigurationSetStateFile("ConfigurationSets.json");

var app = builder.Build();

app.MapGet(
    "/configuration-sets/runtime",
    (IConfigurationSetManager sets) => sets.GetStatus());

app.MapGet(
    "/configuration-sets/desired",
    (IConfigurationSetDesiredStateStore desiredState) =>
        desiredState.GetDesiredStateStatus());

app.Run();
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    ├── Routing/
    │   ├── routes-primary.json
    │   ├── routes-canary.json
    │   ├── emergency-routing.json
    │   ├── clusters-primary.json
    │   ├── clusters-canary.json
    │   └── clusters-failover.json
    ├── Operations/
    │   ├── Normal/
    │   │   ├── Features.json
    │   │   ├── Resilience.json
    │   │   └── Diagnostics.json
    │   ├── Degraded/
    │   │   ├── Features.json
    │   │   ├── Resilience.json
    │   │   └── Diagnostics.json
    │   └── Incident/
    │       ├── Features.json
    │       ├── Resilience.json
    │       └── Diagnostics.json
    └── Features/
        ├── Stable/
        │   └── Features.json
        └── Beta/
            └── Features.json
```

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "DesiredValue": "Failover",
      "AllowedValues": [ "Primary", "Canary", "Failover" ],
      "ApplyMode": "Runtime"
    },
    "OperationalProfile": {
      "DesiredValue": "Degraded",
      "AllowedValues": [ "Normal", "Degraded", "Incident" ],
      "ApplyMode": "Runtime"
    },
    "ReleaseChannel": {
      "DesiredValue": "Beta",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

In that state, routing and operational changes may become active immediately. `ReleaseChannel = Beta` is persisted as desired state but remains pending until the next host startup.

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

With state-file watching enabled, editing `DesiredValue` can trigger a coordinated runtime switch. When the host starts, the watcher is installed first and the store performs a catch-up reload so edits made after registration-time initialization are not missed. Host stop detaches the watcher again. With `watchForChanges: false`, the control file is applied at startup and not watched during the running host.

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

## Runtime consumer expectations and design boundaries

A Configuration Set changes `IConfiguration`; it does not imply that every service in the process automatically rebuilds itself. Consumers usually fall into one of three groups:

1. reload-aware consumers such as `IOptionsMonitor<T>` that can react automatically;
2. services that subscribe to `IConfigurationSetEventHub` and intentionally reinitialize after an effective change;
3. startup-only consumers that should normally use `ConfigurationSetApplyMode.StartupOnly`.

Heavy event work belongs to the consumer. EventHub subscribers are notifications, not transaction participants or a background-job framework; a subscriber can enqueue work if rebuilding is expensive.

When the built-in state file is used, fully compose all Configuration Sets — including their switchable JSON bindings and desired-state apply modes — before `AddConfigurationSetStateFile(...)`. The store initializes persistent desired state immediately from that finished runtime composition, captures the registered set collection, and treats its JSON document as a complete authoritative desired-state document.

The design deliberately keeps several boundaries:

- code owns `AllowedValues` and `ApplyMode`; editable JSON cannot grant itself new values or runtime-switch permission;
- one-value sets remain valid for incremental adoption;
- independent set axes stay independent rather than becoming one combinatorial mega-profile;
- application reinitialization remains application responsibility;
- bound switchable runtimes retain reload/LKG/observability behavior, while source selection is exclusively owned by their Configuration Set.

## Use cases and positioning

### Core positioning

> Feature flags switch behavior. Configuration Sets switch complete reviewed configuration baselines.

A configuration profile gives a semantic name to a group of settings that belong together. Instead of changing several production keys individually, an application can select a reviewed state such as:

```text
ReleaseChannel     = Beta
OperationalProfile = Degraded
ResilienceProfile  = UpstreamDegraded
RoutingProfile     = Failover
DiagnosticsProfile = Incident
```

These axes remain independent. The application does not need a combined profile name such as `Beta-Degraded-UpstreamDegraded-Failover-Incident`.

A second useful explanation for .NET developers is:

> From one environment-wide `appsettings.{Environment}.json` axis to independent, switchable configuration profiles on top of `IConfiguration`.

Configuration Sets do not replace normal .NET configuration. They give related configuration sources semantic names, coordinated switching and explicit consistency results.

### Hero use case 1: Release Channel

```text
ReleaseChannel = Stable | Beta | Lab
```

This is a strong companion to feature-management libraries. The feature-management layer still decides how individual flags, filters or variants behave. The Configuration Set chooses a complete reviewed feature baseline.

A particularly direct .NET integration is [`Microsoft.FeatureManagement`](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference): Microsoft documents that feature flags are built on the .NET configuration system and that any .NET configuration provider can supply their definitions. A switched `Features.json` therefore remains normal `IConfiguration`; `Microsoft.FeatureManagement` continues to own individual flag/filter/variant evaluation while `ReleaseChannel` selects the reviewed baseline.

```csharp
builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta",
        "Lab")
    .AddSwitchableJson(
        "AppSettings/Features",
        "Features.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Features/
        ├── Stable/
        │   └── Features.json
        ├── Beta/
        │   └── Features.json
        └── Lab/
            └── Features.json
```

```json
{
  "ConfigurationSets": {
    "ReleaseChannel": {
      "DesiredValue": "Beta",
      "AllowedValues": [
        "Stable",
        "Beta",
        "Lab"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Product message:

> Select a complete, reviewed feature baseline instead of coordinating a list of individual feature changes by hand.

### Hero use case 2: Operational / Incident Profile

```text
OperationalProfile = Normal | Degraded | Incident
```

This is the strongest general multi-file example because the selected profile can coordinate several operational concerns at once.

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
        "Diagnostics.json",
        "Caching.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Operations/
        ├── Normal/
        │   ├── Features.json
        │   ├── Resilience.json
        │   ├── Diagnostics.json
        │   └── Caching.json
        ├── Degraded/
        │   ├── Features.json
        │   ├── Resilience.json
        │   ├── Diagnostics.json
        │   └── Caching.json
        └── Incident/
            ├── Features.json
            ├── Resilience.json
            ├── Diagnostics.json
            └── Caching.json
```

```json
{
  "ConfigurationSets": {
    "OperationalProfile": {
      "DesiredValue": "Degraded",
      "AllowedValues": [
        "Normal",
        "Degraded",
        "Incident"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Possible meaning of `Normal -> Degraded`:

- disable expensive optional behavior;
- reduce retries;
- shorten timeouts;
- use a more aggressive circuit breaker;
- increase selected diagnostics;
- increase cache usage;
- avoid optional upstream dependencies.

The library does not define those policies. It coordinates the configuration baseline that the application has defined.

Product message:

> Production is degraded. Select a reviewed degraded-mode baseline instead of remembering which unrelated settings must be edited under pressure.

Avoid claiming fully atomic multi-file switching. The accurate wording is:

> Coordinated, preflighted configuration-profile switching with explicit consistency reporting.

### Hero use case 3: Resilience Profile

```text
ResilienceProfile = Normal | UpstreamDegraded | FailFast
```

This maps naturally to [`Microsoft.Extensions.Http.Resilience`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.http.resilience.httpstandardresiliencepipelinebuilderextensions.configure): its standard resilience pipeline can bind options from an `IConfigurationSection`. Microsoft also exposes `ResilienceHandlerContext.EnableReloads<TOptions>(...)` for rebuilding a resilience pipeline when the corresponding options change. Configuration Sets can therefore supply the reviewed configuration baseline while the Microsoft resilience stack owns retry, timeout, circuit-breaker and related runtime behavior.

```csharp
builder
    .AddConfigurationSet(
        "ResilienceProfile",
        "Normal",
        "UpstreamDegraded",
        "FailFast")
    .AddSwitchableJson(
        value => $"AppSettings/Resilience/HttpResilience.{value}.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Resilience/
        ├── HttpResilience.Normal.json
        ├── HttpResilience.UpstreamDegraded.json
        └── HttpResilience.FailFast.json
```

```json
{
  "ConfigurationSets": {
    "ResilienceProfile": {
      "DesiredValue": "UpstreamDegraded",
      "AllowedValues": [
        "Normal",
        "UpstreamDegraded",
        "FailFast"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

A conceptual profile might represent:

```text
Normal
  retry = normal
  timeout = normal
  circuit breaker = normal

UpstreamDegraded
  retry = reduced
  timeout = shorter
  circuit breaker = aggressive

FailFast
  retry = disabled
  timeout = very short
  circuit breaker = aggressive
```

Product message:

> Switch complete resilience strategies instead of editing retry, timeout and circuit-breaker values one by one.

### Hero use case 4: Routing Profile

```text
RoutingProfile = Primary | Canary | Failover
```

The source mapping does not have to follow a directory convention.

This is also a direct fit for [YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0). YARP loads routes and clusters through `IConfiguration`, accepts any `IConfiguration` source, and its configuration contract is reevaluated when configuration changes. A `RoutingProfile` can therefore select the complete route/cluster baseline while YARP remains responsible for proxy behavior.

```csharp
builder
    .AddConfigurationSet(
        "RoutingProfile",
        "Primary",
        "Canary",
        "Failover")
    .AddSwitchableJson(value => value switch
    {
        "Primary"  => "AppSettings/Routing/routes-primary.json",
        "Canary"   => "AppSettings/Routing/routes-canary.json",
        "Failover" => "AppSettings/Routing/emergency-routing.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    })
    .AddSwitchableJson(value => value switch
    {
        "Primary"  => "AppSettings/Routing/clusters-primary.json",
        "Canary"   => "AppSettings/Routing/clusters-canary.json",
        "Failover" => "AppSettings/Routing/clusters-failover.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    });

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Routing/
        ├── routes-primary.json
        ├── routes-canary.json
        ├── emergency-routing.json
        ├── clusters-primary.json
        ├── clusters-canary.json
        └── clusters-failover.json
```

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "DesiredValue": "Failover",
      "AllowedValues": [
        "Primary",
        "Canary",
        "Failover"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Product message:

> Promote, canary or fail over a complete routing baseline without teaching application code which files belong to that routing state.

### Hero use case 5: Diagnostics Profile

```text
DiagnosticsProfile = Standard | Verbose | Incident
```

```csharp
builder
    .AddConfigurationSet(
        "DiagnosticsProfile",
        "Standard",
        "Verbose",
        "Incident")
    .AddSwitchableJson(
        "AppSettings/Diagnostics",
        "Logging.json",
        "DependencyDiagnostics.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Diagnostics/
        ├── Standard/
        │   ├── Logging.json
        │   └── DependencyDiagnostics.json
        ├── Verbose/
        │   ├── Logging.json
        │   └── DependencyDiagnostics.json
        └── Incident/
            ├── Logging.json
            └── DependencyDiagnostics.json
```

```json
{
  "ConfigurationSets": {
    "DiagnosticsProfile": {
      "DesiredValue": "Incident",
      "AllowedValues": [
        "Standard",
        "Verbose",
        "Incident"
      ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Product message:

> Turn diagnostics on as a reviewed profile, not by editing several unrelated production settings during an incident.

### Secondary examples

Theme or branding profiles remain useful demonstrations of generality, but they are weaker primary backend examples because many theme choices are user-specific rather than process-wide.

Environment-style examples are intentionally not recommended as primary marketing material. .NET already has a familiar environment-configuration model, and a running production process switching itself to a `Development` profile is usually not the story this feature should lead with.

Build-related examples are also secondary because `Build` suggests compile/deployment-time state rather than runtime configuration.

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
