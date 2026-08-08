# Configuration Sets – Use Cases and Positioning Notes

This document is product/marketing-oriented. For the exact current API and guarantees, see `configuration-sets.md`.

## Core positioning

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

## Hero use case 1: Release Channel

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

## Hero use case 2: Operational / Incident Profile

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

## Hero use case 3: Resilience Profile

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

## Hero use case 4: Routing Profile

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

## Hero use case 5: Diagnostics Profile

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

## Secondary examples

Theme or branding profiles remain useful demonstrations of generality, but they are weaker primary backend examples because many theme choices are user-specific rather than process-wide.

Environment-style examples are intentionally not recommended as primary marketing material. .NET already has a familiar environment-configuration model, and a running production process switching itself to a `Development` profile is usually not the story this feature should lead with.

Build-related examples are also secondary because `Build` suggests compile/deployment-time state rather than runtime configuration.

## What to promise today

Current implementation can credibly claim:

- named independent configuration profiles;
- one or many JSON sources per profile;
- arbitrary per-value source paths;
- runtime switching;
- per-profile `Runtime` / `StartupOnly` desired-state apply modes;
- visible read-only `ApplyMode` metadata and pending-restart status;
- global watcher disable when no runtime state-file edits should be observed;
- self-describing allowed values;
- preflight of all bound sources before the first commit;
- last-known-good behavior when a candidate cannot prepare;
- explicit partial-commit consistency reporting;
- DI-wide lifecycle events;
- detailed status and change information.

Mixed per-profile `Runtime` / `StartupOnly` behavior is now part of the implementation. A startup-only edit remains visible as desired state with pending-restart status until the next host startup.

Programmatic runtime control is available through the persistence-neutral `IConfigurationSetManager`; `TrySwitchRuntime(...)` is intentionally ephemeral. Optional persistent desired-state control is available through `IConfigurationSetDesiredStateStore.TrySetDesiredValue(...)`, which honors `Runtime` / `StartupOnly` without requiring a controller to know about the built-in JSON file implementation.

## Short positioning candidates

> Feature flags switch behavior. Configuration Sets switch complete reviewed configuration baselines.

> Switch between reviewed configuration profiles instead of editing production settings one key at a time.

> Named, coordinated configuration baselines on top of .NET `IConfiguration`.

> Independent configuration axes without combinatorial `appsettings` profile names.
