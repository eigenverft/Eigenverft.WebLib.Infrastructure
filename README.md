# Eigenverft.WebLib.Infrastructure

Shared ASP.NET Core hosting infrastructure for self-contained Eigenverft web
applications.

## Current scope

The `Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout` namespace contains the executable-rooted directory-layout contract.

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates a host
rooted at `AppContext.BaseDirectory`, assigns `ContentRootPath` and
`WebRootPath`, creates the configured direct-child folders, verifies that they
are writable, and registers the resolved `AppDirectoryLayout` before and after
`Build()`.

`DefaultDirectory` provides typed keys and conventional names for application
logs, data, certificates, settings, and static web content. Callers can retain
the defaults or provide explicit overrides.

`ResetToMinimalConfigurationSources(...)` from the
`Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Sources` namespace replaces the
builder's configuration sources with a minimal stack: environment variables by
default and optionally the current process command-line arguments. Call it
before adding other configuration providers because it clears the existing
source collection.

The `Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings`
namespace separates two related capabilities:

- `AddEnvironmentJsonSettings(...)` loads a common JSON file followed by its
  `.{Environment}` override.
- `JsonSettingsFileEncoder` and `AddJsonFileWithDecodedValues(...)` encode
  selected values on disk and decode recognized values only in memory.

`EncodeAndAddEnvironmentJsonSettings(...)` composes both capabilities when an
application intentionally wants startup-time file encoding followed by decoded
configuration loading. Its JSON providers are appended and therefore override
configuration sources already present. Base64 is available for encoding only;
Windows DPAPI machine scope is available for machine-bound protection.

`BootstrapLogger<TCategoryName>.CreateLogger(...)` from the
`Eigenverft.WebLib.Infrastructure.Hosting.Logging.BootstrapLogger` namespace
provides a pre-host `Microsoft.Extensions.Logging.ILogger<TCategoryName>` before
the application DI container and host logging pipeline exist. Its public surface
remains provider-neutral. The production library has no Serilog package or
assembly reference; optional Serilog support is discovered exclusively through
reflection at runtime.

The bootstrap logger is intentionally a separate, stable process-wide diagnostic
channel rather than a live view of the later application logger. The first call
creates and caches one factory for all categories. When Serilog and its Microsoft
logging bridge are available, an explicitly initialized global
`Serilog.Log.Logger` present at that moment is captured. Serilog's built-in
`Logger.None`/`SilentLogger` is treated as not initialized and therefore selects
the Microsoft fallback instead. This keeps early diagnostics visible when the
Console provider is available. Every explicitly assigned Serilog logger remains
valid, even if it intentionally has no sinks; the library does not inspect sink
configuration. Replacing `Serilog.Log.Logger` later does not rebind existing
bootstrap loggers. The Microsoft fallback applies Console and the `Logging`
configuration section when their extensions are available. Configuration passed
after the first factory creation is intentionally ignored.

This separation allows startup diagnostics, including configuration-provider
precedence and collision checks, to report on the application logging
configuration without depending on that same configuration for their output.
The cached factory is process-owned; callers may create loggers from it but must
not dispose it.
Consumers that want a Serilog-backed bootstrap channel must assign
`Serilog.Log.Logger` before the first actual `BootstrapLogger` access. Merely
referencing Serilog and Console packages does not initialize either provider.

There are therefore two deliberately different bootstrap modes:

```csharp
private static readonly ILogger<Program> Logger =
    BootstrapLogger<Program>.CreateLogger();
```

`CreateLogger(...)` is the automatic best-effort mode. It captures an already
initialized Serilog global logger when available and otherwise uses Microsoft
logging. It does not create or configure Serilog itself.

```csharp
private static readonly ILogger<Program> Logger =
    BootstrapLogger<Program>.CreateRequiredSerilogLogger();
```

`CreateRequiredSerilogLogger(...)` is the explicit fail-fast mode. It creates an
isolated Serilog bootstrap pipeline from
`AppContext.BaseDirectory/AppSettings/BootstrapLoggerSettings.json`, assigns the
created instance to `Serilog.Log.Logger`, and exposes it through the same
provider-neutral `ILogger<TCategoryName>` surface. The consumer must reference
Serilog core, `Serilog.Settings.Configuration`,
`Serilog.Extensions.Logging`, and every sink or enricher named by the JSON file.
The WebLib continues to access all Serilog APIs exclusively through reflection.
Missing packages, a missing or invalid file, or an incompatible Serilog API are
startup errors; this mode never falls back to Microsoft logging.

The required mode accepts optional `configurationFile`, `baseDirectory`,
`sectionName`, and `reloadOnChange` arguments. The first three select the exact
isolated configuration source. `reloadOnChange` defaults to `false`; when enabled,
existing minimum-level overrides and level switches can follow JSON changes, but
the sink pipeline is not reconstructed. Environment-specific files, environment
variables, command-line arguments, DPAPI decoding, and the later application
logger configuration are intentionally not loaded by this API.

Both modes initialize the same process-wide bootstrap cache. Required Serilog
initialization must therefore be the first BootstrapLogger operation. Repeating
the required call with the same file, section, and reload setting reuses the
factory; requesting a different required identity or invoking it after automatic
initialization throws. When used in a static field initializer, a required-mode
failure can occur before the `Main` method body and may surface as a type
initialization failure. This strict behavior is intentional.

The `Eigenverft.WebLib.Infrastructure.Security.Certificates` namespace provides
certificate functionality independently from Kestrel and configuration:

- `SelfSignedCertificateFactory.Create(...)` creates caller-owned self-signed
  certificates for TLS server, TLS client, combined TLS, code-signing, or email
  protection purposes. RSA and ECDSA key profiles as well as separately typed
  DNS and IP subject alternative names are supported.
- `ManagedCertificateFile.LoadOrCreate(...)` loads a valid managed PFX or
  creates a self-signed recovery certificate when the file is missing, outside
  its validity period, unreadable, password-mismatched, or lacks a private key.
  `CertificateRecoveryMode` controls whether that recovery may replace an
  existing PFX.

Managed PFX replacement is written to a temporary file in the target directory,
loaded and validated, and only then moved over the target. If certificate
creation succeeds but persistence fails, the result exposes the file exception
and returns the usable certificate in memory with `Persisted == false`.
`LoadException` retains a read, access, or import error that caused recovery;
`ExistingFilePreserved` distinguishes deliberate protection from a failed
persistence attempt.
`ManagedCertificateResult.Certificate` is always owned and disposed by the
caller. The certificate feature has no dependency on ASP.NET Core hosting,
Kestrel, SNI matching, configuration reload, or logging.

`ConfigureKestrelSniFromConfiguration(...)` from the
`Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace configures HTTP
and HTTPS listeners, binding scope, HTTP protocols, the Kestrel Server header,
TLS protocol policy, and reloadable SNI certificate selection. It retains the
existing configuration contract used by the source applications:

```json
{
  "CertificatesDirectory": "certs",
  "KestrelSettings": {
    "HTTP_PORT": 8080,
    "HTTPS_PORT": 8443,
    "ListenScope": "Localhost",
    "AddServerHeader": false,
    "Protocols": "Http1AndHttp2AndHttp3",
    "PreferLongestSuffixMatch": true,
    "TlsProtocolPolicy": "Default"
  },
  "CertificatesMappingSettings": [
    {
      "SNI": "localhost",
      "FileName": "localhost.pfx",
      "Password": "yourPassword",
      "CertificateRecoveryMode": "PreserveExisting",
      "AdditionalSelfSignedCertificateDnsNames": [
        "*.localhost"
      ],
      "AdditionalSelfSignedCertificateIpAddresses": [
        "127.0.0.1"
      ]
    }
  ]
}
```

An application can override the configured certificate directory explicitly:

```csharp
builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: defaultDirs["ApplicationCerts"]);
```

The three existing top-level configuration areas also define their lifecycle:

- `CertificatesDirectory` is resolved once during startup.
- `KestrelSettings` is startup-fixed, including ports, binding, protocols, TLS
  policy, Server header, and the SNI matching strategy.
- `CertificatesMappingSettings` is the complete hot-reload boundary.

On configuration reload, the normalized certificate mappings, managed PFX
content fingerprints, and active certificate validity are compared with the
current snapshot.
A fully unchanged and usable generation is a no-op. A changed generation is
loaded fully before one immutable selection snapshot is published. This also
allows a configuration reload to pick up an externally replaced PFX even when
its mapping did not change. If the new generation cannot be built, the running
host retains its last-known-good snapshot. A candidate that needs a memory-only
recovery certificate also leaves a still-usable last-known-good snapshot active;
the memory-only candidate is published only when no complete usable generation
remains. Old published snapshots remain owned until host shutdown because a
certificate may still be used by a TLS handshake that began before the swap.
Each configured host owns its own state; there is no static process-wide
certificate selection state. PFX files do not receive a separate file watcher;
their state is reevaluated on configuration reload and host restart.

The mapping's `SNI` value is always included as a DNS SAN when a self-signed
certificate is generated, or as an IP SAN when the value is an IP address.
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses` add further SANs only to
automatically generated certificates; an existing valid PFX retains its own
certificate contents. IP values found in the DNS-name list are normalized to
IP SANs for tolerant handling of existing configuration.

SNI matching accepts an exact configured name or a suffix beginning at a DNS
label boundary. For example, `api.example.com` matches `example.com`, while
`notexample.com` does not. When no configured suffix matches or a client sends
no SNI value, the first usable mapping in configuration order is the fallback.
`PreferLongestSuffixMatch` changes only match precedence; the fallback remains
the first configured usable mapping.

Certificate recovery remains availability-oriented but is not implicitly
destructive. `CertificateRecoveryMode` is configured per mapping and is part of
the hot-reloadable mapping plan:

- `PreserveExisting` is the default. A genuinely missing PFX is created, while
  every existing unusable PFX remains unchanged and the generated recovery
  certificate is returned only in memory.
- `ReplaceExpired` additionally replaces a PFX that was successfully imported
  and found to be expired. It does not replace a not-yet-valid, unreadable,
  access-denied, corrupt, or password-mismatched file.
- `ReplaceAnyUnusable` explicitly allows the previous full self-healing
  behavior. It can overwrite an externally managed PFX after import, password,
  read, or access failures and should therefore be enabled only for mappings
  whose files the library is allowed to replace.

Missing files are finalized without overwrite, so a PFX placed concurrently by
another writer wins. Any generated self-signed certificate can keep HTTPS
cryptographically available, but it is not automatically trusted by clients.

`CertificatesDirectory` and `certDirOverride` accept either fully qualified
paths such as `D:\certs` or paths relative to the host content root such as
`certs`. Without either setting, the directory defaults to
`{ContentRoot}/certs`; `AppContext.BaseDirectory` is the root fallback when the
host supplies no content root.

`TlsProtocolPolicy: Default` is the recommended policy and enables TLS 1.2 and
TLS 1.3. `Strict` permits only TLS 1.3, `MaximumTlsCompatibility` additionally
permits TLS 1.0 and TLS 1.1, and `Legacy` also enables obsolete SSL protocols.
The policy is applied directly to the HTTPS endpoint created by this method.
An unknown `Protocols`, `TlsProtocolPolicy`, or `ListenScope` string uses the
safe default instead of preventing development startup. The plaintext listener
is intentionally HTTP/1; the HTTPS listener defaults to HTTP/1 and HTTP/2.

The library targets `net8.0` and `net10.0`. A `net9.0` consumer uses the
compatible `net8.0` asset; preview target frameworks are intentionally
excluded.

The current package surface is limited to these hosting-directory,
configuration-source, JSON-settings, bootstrap-logging, certificate, and Kestrel
SNI primitives.

## Related repositories

This library is developed library-first to keep reusable web infrastructure
consistent across independent Eigenverft applications and other codebases.
Application-specific behavior remains in each application.

The repositories listed below are context and source references only. When
work is requested in `Eigenverft.WebLib.Infrastructure`, every related
repository is read-only unless the request explicitly names that repository
and explicitly asks for a change there. Inspecting an implementation,
identifying reusable source, or migrating a feature into this library does not
authorize changes, commits, or pushes in the source or consumer repositories.
Likewise, an unqualified request to commit or push applies only to
`Eigenverft.WebLib.Infrastructure`.

- [`Eigenverft.Web.EdgeReverseProxy`](https://github.com/eigenverft/Eigenverft.Web.EdgeReverseProxy)
  currently consumes this library directly as a sibling project. It is an
  intentionally limited host for a different use case.
- [`Eigenverft.App.ReverseProxy`](https://github.com/eigenverft/Eigenverft.App.ReverseProxy)
  is the fully functional reverse-proxy application from which suitable shared
  infrastructure is being identified while the edge host is developed in
  parallel.
- [`Eigenverft.Routed.RequestFilters`](https://github.com/eigenverft/Eigenverft.Routed.RequestFilters)
  provides shared request-filtering functionality already used by
  `Eigenverft.App.ReverseProxy`. Application-neutral hosting primitives are
  migrated here when they belong in the common web-infrastructure layer.

## Build and test

```powershell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

## License

MIT. See [LICENSE](LICENSE).
