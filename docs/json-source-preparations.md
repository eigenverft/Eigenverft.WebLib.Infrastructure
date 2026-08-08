# JSON candidate preparation

This feature adds one extension boundary between JSON parsing and configuration publication.

It is intentionally compatible with the existing encoded-settings stack. Existing public APIs, persisted `enc:` formats, V1 defaults, and shortcuts remain unchanged, while their reversible value operations are now shared through a neutral transform layer.

The three ownership layers are deliberately different:

```text
ReversibleStringTransform
    reversible string operation only
    no JSON, no enc: wrapper, no provider lifecycle
            ↓
JsonSettingsValueCodec
    persisted enc: framing + token/version compatibility
    nested codec composition + migration contract
            ↓
JsonConfigurationCandidatePreparation
    apply decode/validation/normalization to an isolated candidate
    reject before publication on failure
```

This means the same AES, DPAPI, Base92, ROT13, Caesar, Data Protection, and machine-binding operations can be tested and reused independently without making either the encoded-settings stack or SwitchableJson own those implementations.

The application-facing idea is simple:

```text
JSON file
   ↓
parse isolated candidate snapshot
   ↓
CandidatePreparation
   ↓
commit or reject
   ↓
IConfiguration
```

A candidate preparation may decode values, validate them, normalize them, or perform several such steps as one reusable bundle.

## Neutral reversible transforms

The lowest reusable layer can be used without JSON configuration at all:

```csharp
ReversibleStringTransform transform =
    ReversibleStringTransforms.AesPassword("example-password");

string payload = transform.Apply("secret-value");

if (!transform.TryReverse(payload, out string clearText))
{
    throw new InvalidOperationException("The value cannot be reversed in this transform context.");
}
```

`payload` above is only the transform payload. It does **not** contain the JSON-settings `enc:<token>:` framing. `JsonSettingsValueCodec` adds and validates that persistence contract when the same operation is used by `JsonSettingsValueEncoders`, `EncodeAndAddEnvironmentJsonSettings`, decoded JSON providers, or candidate preparations.

Raw transform `Compose(...)` is likewise value-level composition only. It does not insert persistence framing between stages. The established `JsonSettingsValueEncoders.Compose(...)` remains the correct API when nested persisted wrappers and V1 compatibility matter.

## Application-facing API

Build the behavior once:

```csharp
JsonConfigurationCandidatePreparation defaultWindows =
    JsonConfigurationCandidatePreparations.DefaultWindows(
        settingsProtectionPassword,
        settingsStateDirectory);
```

Then assign that one object to any switchable JSON registration:

```csharp
builder.AddSwitchableJsonFile(
    "settings",
    "AppSettings/Stable.json",
    new SwitchableJsonRegistrationOptions
    {
        CandidatePreparation = defaultWindows
    });
```

`SwitchableJson` does not know what `DefaultWindows` means. It knows only that the parsed candidate must successfully pass the supplied preparation before it can be committed.

The same preparation object can be reused:

```csharp
var defaultWindows =
    JsonConfigurationCandidatePreparations.DefaultWindows(
        settingsProtectionPassword,
        settingsStateDirectory);

var proxyOptions = new SwitchableJsonRegistrationOptions
{
    CandidatePreparation = defaultWindows
};

var kestrelOptions = new SwitchableJsonRegistrationOptions
{
    CandidatePreparation = defaultWindows
};
```

## Existing codec concepts are available as candidate preparations

`JsonConfigurationCandidatePreparations` adapts the existing value codecs rather than copying their cryptographic or persisted-format implementations.

The current factory mirrors the codec-producing surface of `JsonSettingsValueEncoders`:

```text
Base64
Base92JsonSafe
Rot13
Caesar(shift)
DpapiMachine
DpapiMachineBase64Url
AesPassword(password)
AesPassword(passwordAsciiBytes)
PhysicalMachineBoundAes()
DataProtection(keyDirectoryPath)
DataProtection(keyDirectoryPath, applicationName, purpose)
Default(password, keyDirectoryPath)
Default(passwordAsciiBytes, keyDirectoryPath)
DefaultWindows(password, keyDirectoryPath)
DefaultWindows(passwordAsciiBytes, keyDirectoryPath)
DpapiWithRot13()
DpapiWithCaesar(shift)
```

For an arbitrary existing codec:

```csharp
JsonSettingsValueCodec codec = MyCodecFactory.Create(...);

JsonConfigurationCandidatePreparation preparation =
    JsonConfigurationCandidatePreparations.Decode(codec);
```

A codec-backed candidate preparation scans the parsed values and replaces a value only when that codec can completely decode it. Plain values and values belonging to another codec remain unchanged, matching the existing explicit-codec loading semantics.

## Compose several candidate operations

Candidate preparations can be bundled:

```csharp
var preparation =
    JsonConfigurationCandidatePreparations.Compose(
        JsonConfigurationCandidatePreparations.DefaultWindows(
            settingsProtectionPassword,
            settingsStateDirectory),
        new ReverseProxySchemaPreparation(),
        new RequireHttpsEndpointsPreparation());
```

Then registration still sees exactly one object:

```csharp
new SwitchableJsonRegistrationOptions
{
    CandidatePreparation = preparation
}
```

Candidate `Compose(...)` means **execution order**:

```text
DefaultWindows decode
        ↓
ReverseProxy schema validation
        ↓
HTTPS policy validation
        ↓
commit
```

This is intentionally different from `JsonSettingsValueEncoders.Compose(...)`. Codec composition describes **encoding order** and therefore decodes in reverse. `Default` and `DefaultWindows` adapt the already-composed existing codec as one candidate step, so their established persisted-format semantics cannot accidentally be rebuilt in the wrong order.

## ConfigurationSet usage

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
var defaultWindows =
    JsonConfigurationCandidatePreparations.DefaultWindows(
        settingsProtectionPassword,
        settingsStateDirectory);

builder
    .AddConfigurationSet(
        "RoutingSet",
        "Stable",
        "Candidate")
    .AddSwitchableJson(
        "AppSettings/Routing",
        new SwitchableJsonRegistrationOptions
        {
            CandidatePreparation = defaultWindows
        },
        "Settings.json");
```

The coordinator does not know about decoding, codecs, validation, or secret handling.

```text
ConfigurationSetCoordinator
        ↓
prepare participant candidate
        ↓
SwitchableJson
        ↓
CandidatePreparation
        ↓
participant ready or rejected
        ↓
normal coordinated commit
```

If candidate preparation fails, that participant never reaches the normal commit phase.

## Standalone prepared JSON

The same bundle can be used without switching:

```csharp
var preparation = JsonConfigurationCandidatePreparations.Base92JsonSafe;

configurationBuilder.AddPreparedJsonFile(
    "AppSettings/settings.json",
    preparation);
```

With `reloadOnChange: true`, standalone prepared JSON keeps the last successfully published snapshot when parsing or preparation of a physical file change fails. No reload notification is published for the rejected candidate; a later valid change can recover normally.

For `optional: true`, a missing file is the complete empty optional state. Because no source candidate was loaded, the preparation bundle is not invoked for that absence.

Or with the parallel environment loader:

```text
AppSettings/
├── ReverseProxySettings.json
└── ReverseProxySettings.Production.json
```

```csharp
var preparation =
    JsonConfigurationCandidatePreparations.DefaultWindows(
        settingsProtectionPassword,
        settingsStateDirectory);

builder.AddPreparedEnvironmentJsonSettings(
    "AppSettings/ReverseProxySettings.json",
    preparation);
```

This is load-only. It does not encode or rewrite either file.

The established write/load API remains separate:

```csharp
builder.EncodeAndAddEnvironmentJsonSettings(...);
```

## Low-level custom plugin contract

A developer who needs behavior not supplied by the factories implements only:

```csharp
public interface IJsonConfigurationSourcePreparation
{
    void Prepare(JsonConfigurationSourcePreparationContext context);
}
```

The context contains:

```csharp
context.SourcePath
context.Values
```

`Values` is an isolated, flattened candidate snapshot. For example:

```json
{
  "Backend": {
    "Url": "https://backend",
    "Secret": "encoded-value"
  }
}
```

is presented approximately as:

```text
Backend:Url    = https://backend
Backend:Secret = encoded-value
```

A custom implementation can modify that candidate:

```csharp
public sealed class RequireHttpsEndpointsPreparation
    : IJsonConfigurationSourcePreparation
{
    public void Prepare(JsonConfigurationSourcePreparationContext context)
    {
        if (context.Values.TryGetValue("Backend:Url", out string? value) &&
            value is not null &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backend URL must use HTTPS.");
        }
    }
}
```

It can then be used directly or bundled with factory preparations:

```csharp
var preparation =
    JsonConfigurationCandidatePreparations.Compose(
        JsonConfigurationCandidatePreparations.Base92JsonSafe,
        new RequireHttpsEndpointsPreparation());
```

## Contract

The low-level contract is deliberately narrow:

- a preparation may inspect, validate, or transform only the supplied candidate values;
- it must not select another source or publish configuration changes;
- throwing rejects the current candidate before provider state is committed;
- composed candidate steps execute in declaration order;
- implementations may be called repeatedly or concurrently and must be safe for concurrent use;
- the supplied dictionary is valid only during the current `Prepare` call;
- owners publish a detached copy, so retaining and mutating the dictionary later cannot alter live configuration;
- a prepared SwitchableJson candidate may still be rejected as stale when runtime state changed while external preparation was executing;
- external side effects performed by a custom plugin cannot be rolled back by the library and are outside the contract.

For `SwitchableJson`, custom preparation code executes outside the runtime state lock.

## Failure semantics

| Situation | Result |
| --- | --- |
| JSON/file load fails | existing JSON/SwitchableJson load-failure semantics |
| preparation throws during switch preparation | candidate rejected with `SourcePreparationFailed` |
| runtime state changes while preparation runs | candidate rejected as `StalePreparation` |
| watched active source preparation fails | last-known-good snapshot remains active |
| ordinary prepared JSON reload fails | previous provider snapshot remains published |
| observer throws after successful commit | observer remains notification-only; committed state stays committed |

## XOR proof implementation

`XorBase64JsonConfigurationSourcePreparation` remains a deliberately small proof that the generic plugin boundary is independent of the existing codec stack:

```csharp
var xor = new XorBase64JsonConfigurationSourcePreparation(
    0x5A,
    "*Secret*");
```

It is not encryption and not a security boundary. Its only purpose is to provide a deterministic custom implementation of the same low-level contract.
