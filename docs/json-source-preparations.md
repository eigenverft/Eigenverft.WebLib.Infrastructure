# JSON Source Preparations

JSON source preparations add one small extension boundary between JSON parsing and configuration publication.

They are deliberately **not** a secret-management feature and they do not replace the existing encoded-settings APIs. `EncodeAndAddEnvironmentJsonSettings` and its related codec behavior remain independent and unchanged.

The contract is useful whenever a parsed JSON candidate should be inspected, validated or transformed before it becomes visible through `IConfiguration`.

## Mental model

```text
JSON file
   ↓
parse isolated snapshot
   ↓
preparation 1
   ↓
preparation 2
   ↓
...
   ↓
detach prepared snapshot
   ↓
owner validates that its runtime state is still current
   ↓
commit
   ↓
publish IConfiguration / lifecycle notifications
```

If a preparation throws, the candidate is rejected before it replaces provider state.

For `SwitchableJson`, preparation implementations execute outside the runtime state lock. If another runtime operation changes the active state while a preparation is running, the prepared candidate is rejected as stale instead of overwriting the newer state.

## Contract

A preparation implements:

```csharp
public interface IJsonConfigurationSourcePreparation
{
    void Prepare(JsonConfigurationSourcePreparationContext context);
}
```

`context.Values` is a mutable, flattened candidate snapshot. A preparation may modify those values during the current `Prepare` call.

The contract is intentionally narrow:

- preparation may inspect, validate or transform only the supplied candidate values;
- preparation must not select another source, publish configuration changes or mutate owner runtime state;
- throwing rejects the current candidate;
- preparations execute in registration order;
- a preparation may be called repeatedly or concurrently and must therefore be safe for concurrent use;
- the supplied value dictionary is valid only for the current call;
- owners publish a detached copy, so retaining and mutating the dictionary later cannot change live configuration;
- a successfully prepared candidate may still be discarded as stale if owner state changed while preparation was executing.

The library can protect its own provider/runtime state from failed or stale preparations. It cannot undo arbitrary external side effects performed by a preparation implementation. Such side effects are outside the preparation contract.

## Small proof implementation: XOR

`XorBase64JsonConfigurationSourcePreparation` exists to prove the contract with a deterministic transformation that is visibly different from the existing codec implementation.

It is **not encryption and not a security boundary**.

```csharp
var xor = new XorBase64JsonConfigurationSourcePreparation(
    key: 0x5A,
    "*Secret*");
```

For demonstration and test setup it also exposes:

```csharp
string persisted = xor.EncodeValue("my-value");
```

The persisted value looks like:

```text
xor1:<base64-payload>
```

The source file remains unchanged while configuration consumers receive the prepared clear value.

## Usage 1: standalone SwitchableJson

Directory:

```text
AppSettings/
└── Routing/
    ├── Stable.json
    └── Candidate.json
```

`Stable.json`:

```json
{
  "Mode": "Stable",
  "BackendSecret": "xor1:..."
}
```

Registration:

```csharp
var xor = new XorBase64JsonConfigurationSourcePreparation(
    0x5A,
    "*Secret*");

builder.AddSwitchableJsonFile(
    "routing-settings",
    "AppSettings/Routing/Stable.json",
    new SwitchableJsonRegistrationOptions
    {
        SourcePreparations = [xor]
    });
```

Runtime switching remains the normal SwitchableJson API:

```csharp
var runtime = services.GetRequiredKeyedService<ISwitchableJsonConfiguration>(
    "routing-settings");

SwitchableJsonSwitchResult result = runtime.TrySwitch(
    "AppSettings/Routing/Candidate.json");
```

Preparation happens before the candidate can replace the current snapshot.

## Usage 2: ConfigurationSet participant

Directory:

```text
AppSettings/
└── Routing/
    ├── Stable/
    │   └── Settings.json
    └── Candidate/
        └── Settings.json
```

Program setup:

```csharp
var xor = new XorBase64JsonConfigurationSourcePreparation(
    0x41,
    "*Secret*");

builder
    .AddConfigurationSet(
        "RoutingSet",
        "Stable",
        "Candidate")
    .AddSwitchableJson(
        "AppSettings/Routing",
        new SwitchableJsonRegistrationOptions
        {
            SourcePreparations = [xor]
        },
        "Settings.json");
```

The ConfigurationSet coordinator remains unaware of XOR or any other transformation. It coordinates the existing SwitchableJson participant contract; each participant prepares its candidate before reporting that it is ready to commit.

```csharp
ConfigurationSetSwitchResult result =
    coordinator.TrySwitch("Candidate");
```

If a participant preparation fails, the set is rejected before the normal coordinated commit phase begins.

## Usage 3: parallel environment JSON loader

For non-switchable JSON there is a separate load-only path using the same preparation contract:

```text
AppSettings/
├── ReverseProxySettings.json
└── ReverseProxySettings.Production.json
```

```csharp
var xor = new XorBase64JsonConfigurationSourcePreparation(
    0x2D,
    "*Secret*");

builder.AddPreparedEnvironmentJsonSettings(
    "AppSettings/ReverseProxySettings.json",
    xor);
```

The common file loads first and the environment-specific file overrides it, matching the normal environment JSON precedence model.

This API does **not** encode or rewrite files. It is a prepared load path only.

The existing API remains a separate concern:

```csharp
builder.EncodeAndAddEnvironmentJsonSettings(...);
```

No migration from one model to the other is implied by the preparation contract.

## Failure semantics

| Situation | Result |
| --- | --- |
| JSON/file load fails | existing JSON/SwitchableJson load failure semantics |
| preparation throws during SwitchableJson candidate preparation | candidate rejected with `SourcePreparationFailed` |
| runtime state changes while external preparation is running | candidate rejected as `StalePreparation` |
| active watched source preparation fails | last-known-good snapshot remains active and reload is rejected |
| prepared ordinary JSON provider reload fails | previous provider snapshot remains published; `FileConfigurationProvider` reports the reload error |
| observer throws after a successful commit | observer remains a notification consumer, not a transaction participant |

## Why the boundary is generic

The initial XOR implementation is intentionally trivial. The extension point is meant to remain independent of any one use case.

Potential consumers can include:

- schema or domain validation;
- value normalization;
- compatibility migrations;
- template/value expansion;
- signature or integrity checks;
- decoding or secret resolution, if a future design can satisfy the same side-effect and failure contract.

Those consumers should not require changes to ConfigurationSet coordination itself. The owner only needs to know whether a candidate was successfully prepared and is still current when it is committed.
