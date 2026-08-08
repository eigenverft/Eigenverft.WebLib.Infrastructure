# ConfigurationSetCoordinator – Developer Demo / Review Notes

> Purpose: manual review of the current `ConfigurationSetCoordinator` feature from an application developer's point of view.
>
> This document records realistic `Program.Main` examples, implemented behavior, and edge cases discovered while the feature was developed. The shorter `configuration-sets.md` is the current technical contract; `configuration-sets-use-cases.md` contains the product-oriented examples.

## 1. What the feature is trying to be

A `ConfigurationSetCoordinator` represents one independent named axis with one active value:

```text
ProxySet       = Stable | Next | Experimental
EnvironmentSet = Development | Production
BuildSet       = Stable | Candidate
ThemeSet       = Light | Dark | HighContrast
```

Several axes may exist at the same time:

```text
EnvironmentSet = Production
ProxySet       = Experimental
BuildSet       = Stable
ThemeSet       = Dark
```

A set value can be purely logical, or it can coordinate one or more switchable JSON sources.

The coordinator itself does not know what `Stable`, `Production`, `Dark`, or `Candidate` mean.

### How the examples should be read

Where a state store is used, three different artifacts are involved and should be shown together:

```text
Program.Main
    = defines set names, allowed values and bound JSON participants

ConfigurationSets.json
    = central desired-set selection
    = lives in the host content root when a relative path is used

AppSettings/<Set>/<Value>/...
    = the actual configuration data selected by the set
```

A useful visual pattern is therefore:

```text
ContentRoot/
├── ConfigurationSets.json      <- central set selector, when StateStore is used
└── AppSettings/
    └── ...                     <- actual lane-specific configuration
```

If an example does **not** call `AddConfigurationSetStateFile(...)`, there is no central `ConfigurationSets.json` for that example. The coordinator can still be selected from code or by an explicit runtime consumer.

---

## 2. Important result of this review: a set with one value is valid

**Current and tested.**

A set does not need to have an actual alternative target.

This is useful while developing a feature incrementally:

```csharp
var proxySet = builder.AddConfigurationSet(
    "ProxySet",
    "Stable");

proxySet.AddSwitchableJson(
        "AppSettings/Proxy",
    "Routes.json");
```

Directory layout:

```text
AppSettings/
└── Proxy/
    └── Stable/
        └── Routes.json
```

There is no possible value switch yet because only `Stable` is allowed.

If this one-value set is also registered with a state store:

```csharp
builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

the central file is still valid and self-describing:

```json
{
  "ConfigurationSets": {
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable" ]
    }
  }
}
```

There is still nothing to switch to, but the set can later gain another allowed value without changing the overall control-file model.

The set still provides:

```text
named DI identity
status
allowed-value metadata
bound participant metadata
state-file representation
EventHub integration
future-compatible directory layout
```

A call such as:

```csharp
coordinator.TrySwitch("Stable");
```

returns `AlreadyActive` and remains observable, but all change flags are false.

A call such as:

```csharp
coordinator.TrySwitch("Experimental");
```

is rejected because the value is not allowed.

Later, the application can extend the set without moving the existing file:

```csharp
var proxySet = builder.AddConfigurationSet(
    "ProxySet",
    "Stable",
    "Experimental");
```

and add:

```text
AppSettings/
└── Proxy/
    ├── Stable/
    │   └── Routes.json
    └── Experimental/
        └── Routes.json
```

That makes the one-value form useful as an **extension-first development pattern**, not a degenerate special case.

### Review opinion

Do **not** require two values in `ConfigurationSetDefinition`.

A single-value set is useful even if no runtime switching is ever added.

---

## 3. Smallest useful `Program.Main`

### Current

No state file, no runtime file watcher, no administrative switching.

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "ProxySet",
        "Stable")
    .AddSwitchableJson(
                "AppSettings/Proxy",
        "Routes.json");

var host = builder.Build();
await host.RunAsync();
```

This loads the initial directory shape:

```text
AppSettings/
└── Proxy/
    └── Stable/
        └── Routes.json
```

Later adding `Next` does not require changing the abstraction, only extending the allowed values and adding another sibling directory:

```text
AppSettings/
└── Proxy/
    ├── Stable/
    │   └── Routes.json
    └── Next/
        └── Routes.json
```

There is no external control file. The active value is defined by code.

This is a reasonable first development stage.

---

## 4. One set, several files in the same directory

### Current

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "ProxySet",
        "Stable",
        "Next",
        "Experimental")
    .AddSwitchableJson(
        "AppSettings/Proxy",
        [
            "Routes.json",
            "EdgeFilters.json",
            "Behaviors.json",
        ]);

var host = builder.Build();
await host.RunAsync();
```

Layout:

```text
AppSettings/Proxy/
├── Stable/
│   ├── Routes.json
│   ├── EdgeFilters.json
│   └── Behaviors.json
├── Next/
│   ├── Routes.json
│   ├── EdgeFilters.json
│   └── Behaviors.json
└── Experimental/
    ├── Routes.json
    ├── EdgeFilters.json
    └── Behaviors.json
```

The three files remain three independent `ISwitchableJsonConfiguration` runtimes.

The caller does not name those technical runtimes. The configuration-set convenience derives stable participant identities from the set name plus logical file path, for example `ProxySet:AppSettings/Proxy/EdgeFilters.json`. Those identities exist for keyed DI, status, failure diagnostics and logging; they are not another configuration concept the normal caller has to manage.

`ConfigurationSetCoordinator` only coordinates their set transition.

This is useful when a feature has several configuration files but all of them belong to the same deployment lane.

### The directory layout is convenience, not the contract

**Current and tested.**

The generic form is a full source-path resolver. A configuration set is therefore not restricted to `{root}/{value}/{file}`.

A value can select a directory:

```csharp
builder
    .AddConfigurationSet("EnvironmentSet", "Development", "Production")
    .AddSwitchableJson(
        value => $"AppSettings/Environment/{value}/EnvironmentSettings.json");
```

Or only the file name can vary:

```csharp
builder
    .AddConfigurationSet("EnvironmentSet", "Development", "Production")
    .AddSwitchableJson(
        value => $"AppSettings/Environment/EnvironmentSettings.{value}.json");
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Environment/
        ├── EnvironmentSettings.Development.json
        └── EnvironmentSettings.Production.json
```

And the values can map to completely unrelated names. This is useful for a reverse proxy where a tested stable routing table and a candidate routing table already have their own deployment names:

```csharp
builder
    .AddConfigurationSet("ProxySet", "Stable", "Candidate")
    .AddSwitchableJson(value => value switch
    {
        "Stable" => "AppSettings/Proxy/proxy-routing-safe.json",
        "Candidate" => "AppSettings/Proxy/candidate-routing-v2.json",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    });

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

The central control file still only selects the logical lane:

```json
{
  "ConfigurationSets": {
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Candidate" ]
    }
  }
}
```

The source files do not need to mirror the set names:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Proxy/
        ├── proxy-routing-safe.json
        └── candidate-routing-v2.json
```

For example, the stable routing source can contain ordinary `IConfiguration`-backed reverse-proxy routing data:

```json
{
  "ReverseProxy": {
    "Routes": {
      "main": {
        "ClusterId": "stable-cluster"
      }
    }
  }
}
```

Switching `ProxySet` to `Candidate` activates `candidate-routing-v2.json`; the coordinator does not care whether the path convention uses directories, suffixes, versioned filenames, or an explicit switch expression.

Multiple independently switchable JSON participants can use arbitrary mappings as well:

```csharp
var proxySet = builder.AddConfigurationSet("ProxySet", "Stable", "Candidate");

proxySet
    .AddSwitchableJson(value => $"AppSettings/Proxy/Routes.{value}.json")
    .AddSwitchableJson(value => $"AppSettings/Proxy/Clusters.{value}.json");
```

The resolver is evaluated once for every allowed value during registration and that mapping is frozen. Runtime switching does not call arbitrary application code again.

It is also valid for two different set values to resolve to the same physical file. In that case the logical `Value` can change while `SourceChanged` and `ConfigurationChanged` remain false.

---

## 5. Several independent sets in one application---

## 5. Several independent sets in one application

### Current

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "EnvironmentSet",
        "Development",
        "Production")
    .AddSwitchableJson(
                "AppSettings/Environment",
        "EnvironmentSettings.json");

builder
    .AddConfigurationSet(
        "ProxySet",
        "Stable",
        "Next",
        "Experimental")
    .AddSwitchableJson(
        "AppSettings/Proxy",
        [
            "Routes.json",
            "EdgeFilters.json",
        ]);

builder
    .AddConfigurationSet(
        "BuildSet",
        "Stable",
        "Candidate")
    .AddSwitchableJson(
                "AppSettings/Build",
        "BuildSettings.json");

var host = builder.Build();
await host.RunAsync();
```

The corresponding directory layout makes the independent axes visible:

```text
AppSettings/
├── Environment/
│   ├── Development/
│   │   └── EnvironmentSettings.json
│   └── Production/
│       └── EnvironmentSettings.json
├── Proxy/
│   ├── Stable/
│   │   ├── Routes.json
│   │   └── EdgeFilters.json
│   ├── Next/
│   │   ├── Routes.json
│   │   └── EdgeFilters.json
│   └── Experimental/
│       ├── Routes.json
│       └── EdgeFilters.json
└── Build/
    ├── Stable/
    │   └── BuildSettings.json
    └── Candidate/
        └── BuildSettings.json
```

The runtime may therefore be in this state:

```text
EnvironmentSet = Production
ProxySet       = Experimental
BuildSet       = Stable
```

No combined value such as `ProductionExperimentalStable` is required.

---

## 6. Self-describing state file

### Current

After all sets are registered:

```csharp
builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json");
```

For the relative path above, the store materializes `ContentRoot/ConfigurationSets.json` similar to:

```json
{
  "ConfigurationSets": {
    "EnvironmentSet": {
      "Value": "Development",
      "AllowedValues": [
        "Development",
        "Production"
      ]
    },
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [
        "Stable",
        "Next",
        "Experimental"
      ]
    },
    "BuildSet": {
      "Value": "Stable",
      "AllowedValues": [
        "Stable",
        "Candidate"
      ]
    }
  }
}
```

Important ownership rule:

```text
Value
  = operator-controlled desired value

AllowedValues
  = descriptive metadata materialized from code
  = NOT an authority that can create new valid values
```

Changing `AllowedValues` in JSON cannot authorize a value that the registered coordinator rejects.

### Current per-set apply metadata

`AllowedValues` serves as operator-facing, code-owned metadata. `ApplyMode` now follows the same principle: an editor does not have to know from memory whether changing a value is live or restart-only.

The canonical state file materializes `ApplyMode` next to `AllowedValues`:

```json
{
  "ConfigurationSets": {
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Next", "Experimental" ],
      "ApplyMode": "Runtime"
    },
    "BuildSet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Candidate" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

The ownership should be obvious to the editor:

```text
Value
  = editable desired value

AllowedValues
  = read-only/descriptive metadata materialized from code

ApplyMode
  = read-only/descriptive metadata materialized from code
  = tells the editor whether changing Value applies live or on next startup
```

"Read-only" here means **not authoritative through file editing**. The file is still physically editable, but successful canonicalization should restore the code-owned metadata just as it already does for `AllowedValues`.

---

## 7. Current hot-switch behavior

### Current

The default is:

```csharp
builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json",
    reloadOnChange: true);
```

or simply:

```csharp
builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json");
```

After the host starts, the state file is watched.

Changing:

```json
"ProxySet": {
  "Value": "Experimental"
}
```

can result in:

```text
ConfigurationSets.json
        ↓
StateStore reload
        ↓
ProxySet.TrySwitch("Experimental")
        ↓
all ProxySet participants prepare
        ↓
commit
        ↓
IConfiguration updated when effective values changed
        ↓
ConfigurationSet EventHub notification
```

This path is covered by end-to-end tests using a real host, real filesystem watcher, real switchable JSON providers, and `IConfiguration`.

---

## 8. Restart-only behavior already exists globally

### Current and tested

This is already possible:

```csharp
builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json",
    reloadOnChange: false);
```

Behavior:

```text
startup
  → ConfigurationSets.json is read
  → configured Value is applied

host is running
  → file may be edited
  → no automatic set switch happens

next application start
  → edited Value is read
  → new value is applied
```

This is exactly useful for settings that should be selectable through the state file but should only become active on restart.

A regression test now verifies this behavior explicitly.

### Example

Current running state:

```json
{
  "ConfigurationSets": {
    "BuildSet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Candidate" ]
    }
  }
}
```

Operator edits it to:

```json
{
  "ConfigurationSets": {
    "BuildSet": {
      "Value": "Candidate",
      "AllowedValues": [ "Stable", "Candidate" ]
    }
  }
}
```

With `reloadOnChange: false`:

```text
running process remains Stable
restart
new process starts as Candidate
```

---

## 9. Mixed runtime and startup-only sets

### Current and tested

Per-set state-file policy is now first-class even when one `ConfigurationSets.json` controls several independent axes:

```csharp
var routingProfile = builder
    .AddConfigurationSet(
        "RoutingProfile",
        "Primary",
        "Canary",
        "Failover");

var releaseChannel = builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly);

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

`Runtime` is the default, so the explicit call is only needed for `StartupOnly` unless code wants to make the default visible.

```text
RoutingProfile  -> Runtime
ReleaseChannel  -> StartupOnly
```

If the running host receives:

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "Value": "Failover",
      "AllowedValues": [ "Primary", "Canary", "Failover" ],
      "ApplyMode": "Runtime"
    },
    "ReleaseChannel": {
      "Value": "Beta",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

then the runtime result can be:

```text
RoutingProfile
  ActiveValue  = Failover
  DesiredValue = Failover

ReleaseChannel
  ActiveValue      = Stable
  DesiredValue     = Beta
  HasPendingRestart = true
```

The next host startup applies `Beta` for the startup-only set.

This mixed path is covered by tests using both explicit `Reload()` and a real file watcher.

---

## 10. Where the per-set switch policy lives

### Current architecture

The **Coordinator remains technically switchable**.

The policy that says whether a **state-file change automatically calls the coordinator at runtime** belongs to the state-file/controller layer, not to `ConfigurationSetCoordinator` itself.

```text
ConfigurationSetCoordinator
  = technical primitive that can coordinate a requested transition

ConfigurationSetStateStore
  = owns desired state and decides whether a state-file edit applies now
    or waits for startup

Admin endpoint / application service
  = may be another trigger entirely
```

That means `StartupOnly` does not make `IConfigurationSetCoordinator.TrySwitch(...)` disappear. It controls the state-file control plane.

---

## 11. Code and JSON ownership for ApplyMode

### Current behavior

Code is authoritative:

```csharp
builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly);
```

The state file materializes the policy:

```json
{
  "ConfigurationSets": {
    "ReleaseChannel": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

Ownership:

```text
Value
  = editable desired state

AllowedValues
  = read-only/descriptive metadata materialized from code

ApplyMode
  = read-only/descriptive metadata materialized from code
```

Changing JSON `ApplyMode` does not grant another capability. A successful canonicalization writes the registered code-owned mode back to the file.

---

## 12. Current mixed-policy API

The concise fluent form is the implemented API:

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

builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Features",
        "Features.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

For code that does not keep the fluent registration handle, the builder-level equivalent is available:

```csharp
builder.SetConfigurationSetStateApplyMode(
    "ReleaseChannel",
    ConfigurationSetStateApplyMode.StartupOnly);
```

The policy must be set before the state store is registered because the store freezes its coordinator/policy snapshot during startup.

---

## 13. Default policy

### Current

Per-set state-file policy defaults to:

```text
Runtime
```

and the state-store watcher defaults to enabled:

```csharp
reloadOnChange: true
```

So the shortest declaration is runtime-switchable from the central file.

A set that must wait for restart opts in explicitly:

```csharp
.StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
```

This keeps existing runtime-switch ergonomics while making restart-only behavior explicit and visible in `ConfigurationSets.json`.

---

## 14. Runtime manual switching without state-file watching

### Current

Even when the state file is restart-only:

```csharp
builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json",
    reloadOnChange: false);
```

an application service can still resolve a coordinator and explicitly call:

```csharp
var proxySet = services.GetRequiredKeyedService<IConfigurationSetCoordinator>(
    "ProxySet");

ConfigurationSetSwitchResult result = proxySet.TrySwitch("Experimental");
```

That is another reason not to equate:

```text
state file does not hot reload
```

with:

```text
coordinator can never switch at runtime
```

If the manually selected runtime value should survive restart, the application can materialize the current state again:

```csharp
var store = services.GetRequiredService<IConfigurationSetStateStore>();
store.Materialize();
```

Otherwise the next startup will apply whatever `Value` is still stored in `ConfigurationSets.json`.

---

## 15. Event consumer: logging hosted service

### Current

Any DI service can subscribe to all configuration-set lifecycle outcomes through `IConfigurationSetEventHub`.

A minimal hosted logging consumer can look like:

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
            ConfigurationSetSwitchResult result = notification.Result;

            _logger.LogInformation(
                "Configuration set {SetName}: {Previous} -> {Active}; " +
                "Status={Status}; ValueChanged={ValueChanged}; " +
                "SourceChanged={SourceChanged}; ConfigurationChanged={ConfigurationChanged}; " +
                "Consistent={Consistent}",
                notification.SetName,
                result.PreviousValue,
                result.ActiveValue,
                result.Status,
                result.ValueChanged,
                result.SourceChanged,
                result.ConfigurationChanged,
                result.IsConsistent);
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
```

This consumer receives one completed **set-level** event, even if the set contains several JSON participants.

---

## 16. Event consumer: service reinitialization

### Current

A service can decide how broad its trigger should be.

Only reinitialize when effective configuration values changed:

```csharp
_subscription = events.Subscribe(
    "ProxySet",
    notification =>
    {
        if (notification.Result.ConfigurationChanged)
        {
            Reinitialize();
        }
    });
```

React when a source identity changed even if contents are identical:

```csharp
if (notification.Result.SourceChanged)
{
    Reinitialize();
}
```

React to every logical set-value change:

```csharp
if (notification.Result.ValueChanged)
{
    Reinitialize();
}
```

Generic "something meaningful changed" behavior:

```csharp
if (notification.Result.HasChanges)
{
    Reinitialize();
}
```

Important distinction:

```text
Set switched Stable -> Experimental
files are different paths
file contents are identical

ValueChanged         = true
SourceChanged        = true
ConfigurationChanged = false
HasChanges           = true
```

A consumer therefore does not have to infer semantics from `IConfiguration` reload behavior.

---

## 17. Heavy event work should be queued by the consumer

### Current design assumption

The EventHub is a synchronous in-process notification mechanism.

Callbacks are outside coordinator locks and subscriber exceptions are isolated, but a consumer should still avoid doing long blocking work directly in the callback.

A heavy reinitializer can use:

```text
ConfigurationSetEventHub
        ↓
short callback
        ↓
Channel / queue / signal
        ↓
BackgroundService
        ↓
expensive rebuild or reconnect
```

The infrastructure should not invent one global background execution policy because different consumers need different coalescing, retry, and ordering behavior.

---

## 18. ThemeSwitcher example

A theme is a good non-proxy example because it demonstrates that the abstraction is not really about deployments.

### Program.Main

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "ThemeSet",
        "Light",
        "Dark",
        "HighContrast")
    .AddSwitchableJson(
                "AppSettings/Theme",
        "Theme.json",
        reloadOnChange: true);

builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json");

var host = builder.Build();
await host.RunAsync();
```

Central `ConfigurationSets.json` in the content root:

```json
{
  "ConfigurationSets": {
    "ThemeSet": {
      "Value": "Light",
      "AllowedValues": [ "Light", "Dark", "HighContrast" ],
      "ApplyMode": "Runtime"
    }
  }
}
```

`Value` is the operator-controlled selector. `AllowedValues` and `ApplyMode` are code-owned descriptive metadata. This example uses the default per-set `Runtime` policy; changing the JSON `ApplyMode` does not change that registered policy.

Layout:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Theme/
        ├── Light/
        │   └── Theme.json
        ├── Dark/
        │   └── Theme.json
        └── HighContrast/
            └── Theme.json
```

Example lane file `AppSettings/Theme/Light/Theme.json`:

```json
{
  "Theme": {
    "Primary": "#111111",
    "Secondary": "#F5F5F5",
    "Accent": "#4A90E2"
  }
}
```

A consumer may use `IOptionsMonitor<ThemeOptions>` when normal configuration reload semantics are enough.

A renderer/cache that needs explicit reconstruction can additionally subscribe to `ThemeSet` through the EventHub.

### Why this example matters

It proves the model is really:

```text
named value set
+ optional coordinated configuration sources
```

not:

```text
environment/profile framework
```

---

## 19. Microsoft.FeatureManagement FeatureSet example

This is a particularly strong integration example because `Microsoft.FeatureManagement` uses the normal .NET `IConfiguration` system as its feature-definition source. The configuration-set layer does not implement feature evaluation itself; it only switches the JSON source that Microsoft.FeatureManagement reads.

### Program.Main

```csharp
using Microsoft.FeatureManagement;

var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "FeatureSet",
        "Stable",
        "Beta",
        "Lab")
    .AddSwitchableJson(
                "AppSettings/Features",
        "Features.json");

builder.Services.AddFeatureManagement();

builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json");
```

Central `ConfigurationSets.json`:

```json
{
  "ConfigurationSets": {
    "FeatureSet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Beta", "Lab" ]
    }
  }
}
```

Changing only `Value` to `Beta` selects the entire `Beta/Features.json` lane. The Microsoft feature flags inside that file are still evaluated by Microsoft.FeatureManagement.

Directory layout:

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

A `Stable/Features.json` can contain Microsoft's current `feature_management` schema:

```json
{
  "feature_management": {
    "feature_flags": [
      {
        "id": "NewCheckout",
        "enabled": false
      },
      {
        "id": "NewProxyPipeline",
        "enabled": false
      }
    ]
  }
}
```

`Beta/Features.json` can expose a broader lane:

```json
{
  "feature_management": {
    "feature_flags": [
      {
        "id": "NewCheckout",
        "enabled": true
      },
      {
        "id": "NewProxyPipeline",
        "enabled": false
      }
    ]
  }
}
```

and `Lab/Features.json` may enable both or use Microsoft feature filters / variants:

```json
{
  "feature_management": {
    "feature_flags": [
      {
        "id": "NewCheckout",
        "enabled": true
      },
      {
        "id": "NewProxyPipeline",
        "enabled": true
      }
    ]
  }
}
```

Application code remains ordinary Microsoft.FeatureManagement code:

```csharp
public sealed class CheckoutService
{
    private readonly IVariantFeatureManager _features;

    public CheckoutService(IVariantFeatureManager features)
    {
        _features = features;
    }

    public async Task ExecuteAsync()
    {
        if (await _features.IsEnabledAsync("NewCheckout"))
        {
            // new implementation
            return;
        }

        // stable implementation
    }
}
```

The important separation is:

```text
ConfigurationSetCoordinator
    switches FeatureSet Stable -> Beta -> Lab
            ↓
SwitchableJsonConfiguration
    publishes another Features.json through IConfiguration
            ↓
Microsoft.FeatureManagement
    evaluates flags, filters and variants
            ↓
application uses IVariantFeatureManager / IFeatureManager
```

That makes a `FeatureSet` more than an abstract example: it can act as a **coarse-grained lane selector around Microsoft's fine-grained feature-management library**.

For example, the application can switch the whole approved feature baseline from `Stable` to `Beta`, while Microsoft.FeatureManagement still decides individual feature enablement, targeting, time windows, or variants inside that baseline.

This is a good runtime-switch candidate because Microsoft.FeatureManagement is configuration-backed and designed for dynamic feature flag values. `ApplyMode` in `ConfigurationSets.json` now tells the operator explicitly whether this application's release/feature baseline follows file edits live or only at startup.

---

## 20. BuildSet / EnvironmentSet example where restart-only is more believable

Some values may configure services that are constructed only once during host startup.

Example:

```csharp
builder
    .AddConfigurationSet(
        "BuildSet",
        "Stable",
        "Candidate")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Build",
        "BuildSettings.json");

builder
    .AddConfigurationSet(
        "EnvironmentSet",
        "Development",
        "Production")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Environment",
        "EnvironmentSettings.json");
```

Directory layout:

```text
AppSettings/
├── Build/
│   ├── Stable/
│   │   └── BuildSettings.json
│   └── Candidate/
│       └── BuildSettings.json
└── Environment/
    ├── Development/
    │   └── EnvironmentSettings.json
    └── Production/
        └── EnvironmentSettings.json
```

If those values affect startup-only DI registrations, connection topology, or other non-reloadable objects, automatic runtime switching may be misleading.

The current per-set form keeps the watcher available for other runtime-switchable sets:

```csharp
builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

Central `ConfigurationSets.json` with the **current API**:

```json
{
  "ConfigurationSets": {
    "BuildSet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Candidate" ],
      "ApplyMode": "StartupOnly"
    },
    "EnvironmentSet": {
      "Value": "Development",
      "AllowedValues": [ "Development", "Production" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

The operator can now see directly that edits to these values wait for restart. Other sets in the same file may still use `"ApplyMode": "Runtime"` and hot-switch normally.

---

## 21. ProxySet example closer to the original stability use case

```csharp
var proxySet = builder
    .AddConfigurationSet(
        "ProxySet",
        "Stable",
        "Next",
        "Experimental")
    .AddSwitchableJson(
        "AppSettings/Proxy",
        [
            "Routes.json",
            "EdgeFilters.json",
            "Behaviors.json",
        ]);
```

To make this operationally selectable from the central file:

```csharp
builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

Central `ConfigurationSets.json`:

```json
{
  "ConfigurationSets": {
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Next", "Experimental" ]
    }
  }
}
```

Changing only `Value` to `Next` or `Experimental` selects the matching directory for **all three bound files as one coordinated set operation**.


Directory layout:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Proxy/
        ├── Stable/
        │   ├── Routes.json
        │   ├── EdgeFilters.json
        │   └── Behaviors.json
        ├── Next/
        │   ├── Routes.json
        │   ├── EdgeFilters.json
        │   └── Behaviors.json
        └── Experimental/
            ├── Routes.json
            ├── EdgeFilters.json
            └── Behaviors.json
```

Possible operational meaning:

```text
Stable
  = proven settings / filters / behavior

Next
  = integration-tested candidate

Experimental
  = production-observable experiment
```

The coordinator does not know these meanings.

A higher-level service may decide when to select each value.

---

## 22. One JSON file may itself contain many settings

A configuration set does not require one switchable source per setting object.

This is valid:

```csharp
builder
    .AddConfigurationSet(
        "ApplicationSet",
        "Stable",
        "Candidate")
    .AddSwitchableJson(
                "AppSettings/Application",
        "AppSettings.json");
```

Directory layout:

```text
AppSettings/
└── Application/
    ├── Stable/
    │   └── AppSettings.json
    └── Candidate/
        └── AppSettings.json
```

This is the case where one file carries many sections while the set still switches one source.

where `AppSettings.json` contains many sections:

```json
{
  "Proxy": {
    "Mode": "Stable"
  },
  "Features": {
    "Foo": true
  },
  "Theme": {
    "Primary": "#111111"
  }
}
```

The multiple-file convenience is useful, but not required.

Choosing one file or several files remains an application organization decision.

---

## 23. State-store registration order matters

### Current

The state store captures the coordinators known when it is registered.

Good:

```csharp
builder.AddConfigurationSet("ProxySet", "Stable", "Experimental");
builder.AddConfigurationSet("BuildSet", "Stable", "Candidate");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
```

Avoid registering another set afterward and expecting the existing store to discover it automatically.

A convenience wrapper may eventually make this harder to misuse, but the current contract should remain understood.

---

## 24. A set switch does not magically make every service dynamic

This is important for practical use.

Changing `IConfiguration` does not mean every object that originally consumed configuration will recreate itself.

Consumers fall into roughly three categories:

```text
1. IOptionsMonitor / reload-aware consumer
   → may react automatically

2. service with explicit ConfigurationSetEventHub subscription
   → can rebuild itself intentionally

3. startup-only service
   → should normally use restart-only set activation
```

The configuration infrastructure should expose enough information for the application to make this choice, but should not pretend all services are hot-reload capable.

---

## 25. Current event information is sufficient for most consumers

A `ConfigurationSetSwitchResult` exposes:

```text
Name
Status
PreviousValue
RequestedValue
ActiveValue
ValueChanged
SourceChanged
ConfigurationChanged
HasChanges
IsConsistent
FailureKind
FailedParticipantName
ParticipantResults
Sequence
Timestamp
```

That covers several useful reactions:

```text
log every attempt
log only successful transitions
reinitialize on effective config changes
reinitialize on source changes
react to logical value changes
raise an alert on partial commit
ignore AlreadyActive no-ops
```

I would not add more event types until a concrete consumer cannot express what it needs with this result.

---

## 26. Things I would deliberately not do

### Do not make `AllowedValues` in JSON authoritative

Code remains the authority.

### Do not let editable JSON grant itself runtime-switch permission

If an `ApplyMode` is displayed in JSON later, it should be metadata derived from registered policy.

### Do not make a one-value set invalid

It is useful for incremental adoption and future extension.

### Do not force every set to hot-switch

Startup-only configuration is a legitimate and probably common case.

### Do not put application reinitialization into the coordinator

The coordinator publishes what happened. Consumers decide what to rebuild.

### Do not turn the EventHub into a background job framework

Heavy consumers can queue work themselves.

### Do not bundle all set axes into one giant profile value

Independent axes are one of the strongest properties of the design.

---

## 27. Practical `Program.Main` candidate A – simple runtime-controlled application

Everything in the state file may hot-switch:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "ProxySet",
        "Stable",
        "Next",
        "Experimental")
    .AddSwitchableJson(
        "AppSettings/Proxy",
        [
            "Routes.json",
            "EdgeFilters.json",
        ]);

builder
    .AddConfigurationSet(
        "ThemeSet",
        "Light",
        "Dark")
    .AddSwitchableJson(
                "AppSettings/Theme",
        "Theme.json");

builder.AddConfigurationSetStateFile(
    "ConfigurationSets.json",
    reloadOnChange: true);

builder.Services.AddHostedService<ConfigurationSetLogger>();

var host = builder.Build();
await host.RunAsync();
```

Central `ConfigurationSets.json`:

```json
{
  "ConfigurationSets": {
    "ProxySet": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Next", "Experimental" ],
      "ApplyMode": "Runtime"
    },
    "ThemeSet": {
      "Value": "Light",
      "AllowedValues": [ "Light", "Dark" ],
      "ApplyMode": "Runtime"
    }
  }
}
```

Both sets use the default `Runtime` state-file policy, so edits to either `Value` can apply while the watcher is running.


Directory layout for this `Program.Main`:

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    ├── Proxy/
    │   ├── Stable/
    │   │   ├── Routes.json
    │   │   └── EdgeFilters.json
    │   ├── Next/
    │   │   ├── Routes.json
    │   │   └── EdgeFilters.json
    │   └── Experimental/
    │       ├── Routes.json
    │       └── EdgeFilters.json
    └── Theme/
        ├── Light/
        │   └── Theme.json
        └── Dark/
            └── Theme.json
```

This is fully representable with the current API.

## 28. Practical `Program.Main` candidate B – conservative restart-controlled application

A state file may still be watched globally while selected axes explicitly wait for restart:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Features",
        "Features.json");

builder
    .AddConfigurationSet(
        "ServiceTopology",
        "Primary",
        "Alternate")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        value => $"AppSettings/Topology/Services.{value}.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");

var host = builder.Build();
await host.RunAsync();
```

Central state:

```json
{
  "ConfigurationSets": {
    "ReleaseChannel": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    },
    "ServiceTopology": {
      "Value": "Primary",
      "AllowedValues": [ "Primary", "Alternate" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
```

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    ├── Features/
    │   ├── Stable/
    │   │   └── Features.json
    │   └── Beta/
    │       └── Features.json
    └── Topology/
        ├── Services.Primary.json
        └── Services.Alternate.json
```

Editing either value changes `DesiredValue`, but the running host keeps its existing `ActiveValue` until restart. `GetStatus().HasPendingRestart` makes that drift observable.

---

## 29. Practical `Program.Main` candidate C – mixed runtime/startup policy

This is the most representative end-state example:

```csharp
var builder = Host.CreateApplicationBuilder(args);

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

builder
    .AddConfigurationSet(
        "ReleaseChannel",
        "Stable",
        "Beta")
    .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
    .AddSwitchableJson(
        "AppSettings/Features",
        "Features.json");

builder.AddConfigurationSetStateFile("ConfigurationSets.json");
builder.Services.AddHostedService<ConfigurationSetLogger>();

var host = builder.Build();
await host.RunAsync();
```

Central state:

```json
{
  "ConfigurationSets": {
    "RoutingProfile": {
      "Value": "Primary",
      "AllowedValues": [ "Primary", "Canary", "Failover" ],
      "ApplyMode": "Runtime"
    },
    "OperationalProfile": {
      "Value": "Normal",
      "AllowedValues": [ "Normal", "Degraded", "Incident" ],
      "ApplyMode": "Runtime"
    },
    "ReleaseChannel": {
      "Value": "Stable",
      "AllowedValues": [ "Stable", "Beta" ],
      "ApplyMode": "StartupOnly"
    }
  }
}
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

Behavior:

```text
edit RoutingProfile     -> coordinated live routing-source switch
edit OperationalProfile -> coordinated live multi-file operational switch
edit ReleaseChannel     -> desired state changes; active state waits for restart
```

---

## 30. Current pending-restart diagnostics

Runtime state explicitly distinguishes desired and active values:

```text
Name           = ReleaseChannel
ActiveValue    = Stable
DesiredValue   = Beta
ApplyMode      = StartupOnly
PendingRestart = true
```

This information lives in `ConfigurationSetStateStoreStatus.SetStates` and `ConfigurationSetStateApplyResult.PendingRestartChanges`. It is intentionally runtime/diagnostic state rather than mutable status fields written into the control file.

---

## 31. Review summary

### Confirmed working now

```text
✓ one-value configuration sets
✓ later extension by adding allowed values
✓ several independent set axes
✓ conventional directory layouts
✓ arbitrary value => sourcePath mappings
✓ one or several JSON participants per set
✓ fluent Program.Main registration
✓ internally managed participant identities
✓ keyed DI coordinator access
✓ self-describing ConfigurationSets.json
✓ AllowedValues remain authoritative in code
✓ ApplyMode remains authoritative in code
✓ per-set Runtime / StartupOnly behavior in the same state file
✓ DesiredValue / ActiveValue / pending-restart diagnostics
✓ startup application of state-file values
✓ runtime watched state-file switching
✓ persistent StateStore.TrySetDesiredValue control
✓ generic desired-state drift diagnostics
✓ persistent Runtime failure keeps desired value while active remains LKG
✓ internal persistent writes do not echo as duplicate watcher applies
✓ global watcher disable when desired
✓ EventHub for arbitrary DI consumers
✓ per-set event subscription
✓ set-level aggregated change information
✓ distinction between logical/source/effective-config change
### Programmatic control is now explicit

```text
coordinator.TrySwitch(value)
  = technical / ephemeral runtime switch
  = does not rewrite ConfigurationSets.json

stateStore.TrySetDesiredValue(name, value)
  = persists desired state first
  = Runtime: then attempts live coordination
  = StartupOnly: remains pending until restart
  = rejected Runtime candidate: desired stays persisted, active stays LKG
```

This separation gives a later Admin API, CLI, or operator service a deliberate choice instead of hidden persistence behavior.

The implemented per-set default remains `Runtime`; `StartupOnly` is explicit opt-in. The remaining closure work is integration/regression coverage and optional higher-level diagnostics/control surfaces rather than another missing core state primitive.

That persistent control-plane API is the next state-management block. The implemented per-set default is `Runtime`; `StartupOnly` is explicit opt-in.

Given the stability-oriented use case, `StartupOnly` as the per-set default with explicit opt-in to runtime switching is worth serious consideration before the feature is considered final.
