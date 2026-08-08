# Configuration Sets – Use Cases and Positioning Notes

This document is product/marketing-oriented. For the exact current API and guarantees, see `configuration-sets.md`.

## Core positioning

> Feature flags switch behavior. Configuration Sets switch entire known-good configuration baselines.

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
      "Value": "Beta",
      "AllowedValues": [
        "Stable",
        "Beta",
        "Lab"
      ]
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
      "Value": "Degraded",
      "AllowedValues": [
        "Normal",
        "Degraded",
        "Incident"
      ]
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

```text
ContentRoot/
├── ConfigurationSets.json
└── AppSettings/
    └── Resilience/
        ├── HttpResilience.Normal.json
        ├── HttpResilience.UpstreamDegraded.json
        └── HttpResilience.FailFast.json
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
      "Value": "Failover",
      "AllowedValues": [
        "Primary",
        "Canary",
        "Failover"
      ]
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
- global restart-only state-file behavior by disabling watching;
- self-describing allowed values;
- preflight of all bound sources before the first commit;
- last-known-good behavior when a candidate cannot prepare;
- explicit partial-commit consistency reporting;
- DI-wide lifecycle events;
- detailed status and change information.

Do not yet claim per-profile mixed `Runtime` / `StartupOnly` behavior inside one state file until that V1 capability is implemented.

Do not yet imply that a direct programmatic coordinator switch persists desired state across restart; direct `TrySwitch(...)` is currently runtime-only.

## Short positioning candidates

> Feature flags switch behavior. Configuration Sets switch entire known-good configuration baselines.

> Switch between reviewed configuration profiles instead of editing production settings one key at a time.

> Named, coordinated configuration baselines on top of .NET `IConfiguration`.

> Independent configuration axes without combinatorial `appsettings` profile names.
