# 🧱 Eigenverft.WebLib.Infrastructure

<!-- Maintenance note: Keep README.NUGET.md aligned with this README for shared prose, examples, headings, badges, and feature descriptions. Use absolute NuGet/GitHub URLs there where this README can use repository-relative links; otherwise keep shared content in sync. -->

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.WebLib.Infrastructure?logo=mit)](LICENSE)

Production-oriented ASP.NET Core adapters built on
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).
WebLib turns configuration, certificate files, and the shared application directory layout into a
reload-safe Kestrel/SNI setup. It also connects ASP.NET Core Data Protection to NetLib's composable
configuration-value protection without duplicating host-independent infrastructure.

---

## ✨ At a glance

| Capability | Problem solved | Starting point |
| --- | --- | --- |
| Kestrel and SNI | Configure HTTP/HTTPS listeners, select certificates by host name, and retain the last-known-good certificate generation | `ConfigureKestrelSniFromConfiguration(...)` |
| Managed certificates | Load existing PFX files or create policy-controlled self-signed recovery certificates | `CertificateRecoveryMode` and NetLib certificate primitives |
| Protected certificate mappings | Keep persisted PFX passwords out of clear-text configuration after provisioning | `AspNetDataProtectionConfigurationValueCodecs` and SwitchableJson |
| Web host directories | Apply NetLib's executable-rooted layout to ASP.NET Core content root, web root, and `wwwroot` | `WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` |
| Canonical redirects | Normalize apex/www/aliases and HTTPS in one redirect without leaking an incoming HTTP port into the HTTPS target | `AddCanonicalHostRedirect(...)` + `UseCanonicalHostRedirect()` |
| Health probe | Short-circuit GET/HEAD `/health` before later filters and suppress probe-originated `/favicon.ico` noise | `UseHealthProbeFaviconAware()` |
| HTML status responses | Write a small explicit HTML status response for middleware short-circuits using ASP.NET Core reason phrases | `WriteHtmlStatusResponseAsync(...)` |

WebLib targets .NET 8 and .NET 10. Installing it also brings in
`Eigenverft.NetLib.Infrastructure` as the shared foundation.

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

## 🚀 Quick start

### Executable-rooted web application

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates an ASP.NET Core
`WebApplicationBuilder` from NetLib's shared directory layout. WebLib adds the web-specific
`ContentRootPath`, `WebRootPath`, and semantic `"Web"` mapping for `wwwroot`.

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder =
    WebApplicationBuilderFactory.CreateWithDefaultDirectory();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string webRoot = directories["Web"];

WebApplication app = builder.Build();
app.MapGet("/", () => $"Web root: {webRoot}");
app.Run();
```

## 🔥 Optional self-HTTP startup warmup

`AddSelfHttpWarmup(...)` can issue one pass of HTTP requests to the running application after startup
has completed. This is useful when a deployment should pay first-use costs such as JIT compilation,
dependency activation, TLS setup, and HTTP connection setup before normal traffic reaches selected
endpoints.

For code-based setup, passing the target URL is the normal path and opts in immediately:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup;

builder.Services.AddSelfHttpWarmup("https://localhost:8443/health");
```

Multiple targets use the same API. The optional delegate is only for small feature-level tuning:

```csharp
using System;
using Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup;

builder.Services.AddSelfHttpWarmup(
    new[]
    {
        "https://localhost:8443/health",
        "https://localhost:8443/",
    },
    options =>
    {
        options.InitialDelay = TimeSpan.FromSeconds(1);
        options.RequestTimeout = TimeSpan.FromSeconds(5);
    });
```

The parameterless `AddSelfHttpWarmup()` overload is the configuration-binding path. It remains
opt-in through `Enabled`; URL-based and code-based overloads enable warmup automatically. Values are
bound from the `SelfHttpWarmup` section before code-based options are applied.

```json
{
  "SelfHttpWarmup": {
    "Enabled": true,
    "InitialDelay": "00:00:01",
    "RequestTimeout": "00:00:05",
    "TargetUrls": [
      "https://localhost:8443/health",
      "https://localhost:8443/"
    ]
  }
}
```

Targets run sequentially once after startup. Shutdown cancels both the post-start delay and any
in-flight request. A request timeout or connection failure is logged and does not prevent later
targets from being attempted. Redirects are not followed. Standard platform certificate validation
remains enabled; self-HTTP warmup intentionally bypasses proxies so the request connects directly to
the configured target.

## 🔐 ASP.NET Core Data Protection adapter

`AspNetDataProtectionStringTransforms.DataProtection(...)` adapts an ASP.NET Core
`IDataProtectionProvider` to NetLib's `ReversibleStringTransform` abstraction while keeping
the application name, purpose, and persistent key-ring directory explicit.

For persisted configuration values,
`AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` adds the NetLib codec envelope.
Its directory-layout overload derives the standard key-ring path and entry-assembly name, leaving
only the application-specific purpose to the caller. The result is a normal
`ConfigurationValueCodec` and can be used independently or at any position in
`ConfigurationValueCodecs.Compose(...)`.

## 🧰 Small request-pipeline helpers

WebLib includes a few intentionally small ASP.NET Core helpers that are useful outside the larger
RequestFilters stack.

### Canonical host and HTTPS redirect

The normal case needs one registration and one middleware call:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.CanonicalHostRedirect;

builder.Services.AddCanonicalHostRedirect(options =>
    options.PrimaryApexHost = "example.com");

WebApplication app = builder.Build();
app.UseCanonicalHostRedirect();
```

The defaults canonicalize to `www`, require HTTPS, return `308 Permanent Redirect`, and target implicit
HTTPS/443. Set `RedirectFromHosts`, `Canonicalization = CanonicalHostMode.ToApex`, or one
`HttpsTargetPort` only when the deployment needs them. `AddCanonicalHostRedirect()` without a delegate
binds the `CanonicalHostRedirect` configuration section.

Configure shared defaults during registration and override only the values that differ for one middleware use:

```csharp
builder.Services.AddCanonicalHostRedirect(options =>
    options.PrimaryApexHost = "example.com");

WebApplication app = builder.Build();

app.UseCanonicalHostRedirect(options =>
{
    options.HttpsTargetPort = 8443;
});
```

That second delegate affects only this concrete middleware use. Another `UseCanonicalHostRedirect()` call keeps the shared baseline. Configuration reloads rebuild the current baseline first and then reapply the local override.

A redirect combines host and scheme normalization into one hop and preserves `PathBase`, path, and
query. Incoming HTTP ports are never copied to HTTPS. When a reverse proxy supplies the external scheme
or host, configure ASP.NET Core Forwarded Headers normally and call `UseForwardedHeaders()` before
`UseCanonicalHostRedirect()`.

### Public use-site options monitor

Reusable middleware libraries can expose a local `UseX(Action<TOptions>)` override without replacing ASP.NET Core's options architecture. Build an isolated monitor from the concrete application pipeline:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure;
using Microsoft.Extensions.Options;

IOptionsMonitor<MyOptions> localOptions =
    app.CreateUseSiteOptionsMonitor<MyOptions>(options =>
    {
        options.Mode = "local";
    });
```

The monitor starts from normal registered options configuration and configuration binding, runs registered `PostConfigure` steps, applies the local use-site override afterwards, and then runs registered validation. It uses the registered change-token sources, so configuration reload rebuilds the current baseline and reapplies the same local override; `OnChange` receives that rebuilt locally overridden value. The monitor has its own cache and fresh options instances, so local mutable changes do not alter the application's global options monitor.

### Health probe and favicon suppression

`UseHealthProbeFaviconAware()` handles only GET and HEAD for `/health`, returns `200 OK` with `OK`
for GET, and short-circuits the rest of the pipeline. A GET or HEAD for `/favicon.ico` returns
`204 No Content` only when its `Referer` points to `/health`. Keep this middleware before filters that
a health probe must bypass.

### Explicit HTML status response

For a middleware that intentionally terminates a request with an HTML response, use:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;

await context.Response.WriteHtmlStatusResponseAsync(StatusCodes.Status403Forbidden);
```

The helper uses `ReasonPhrases.GetReasonPhrase(...)`; WebLib does not maintain its own HTTP status
code description table. General application error handling remains the responsibility of ASP.NET
Core Status Code Pages or Problem Details.

### Host filtering remains framework-owned

WebLib intentionally does not provide an `AddAllowedHosts` replacement. `WebApplication.CreateBuilder()`
already wires ASP.NET Core host filtering to the live configuration object. Clearing
`builder.Configuration.Sources` and adding replacement sources does not remove that wiring, so a
rebuilt `AllowedHosts` value is still consumed by the built-in host-filtering options.

### HSTS

WebLib keeps HSTS framework-owned and adds a small registration/use-site wrapper with a 180-day default. Configuration binds from the `Hsts` section and an optional code callback is applied last:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Hsts;

builder.Services.AddWebLibHsts(options =>
{
    // Optional startup-time override after configuration binding.
    options.Preload = false;
});

WebApplication app = builder.Build();
app.UseWebLibHsts(); // no HSTS middleware in Development
```

### Request traffic shaping

Register WebLib's native rate-limiter composition and activate ASP.NET Core rate limiting:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

builder.Services.AddRequestTrafficShaping(options =>
{
    options.PerClient.Enabled = true;
    options.PerClient.BurstSize = 40;
    options.PerClient.RequestsPerSecond = 10;
    options.PerClient.QueueLimit = 20;

    // Server-wide WebLib starting policy; tune after representative consumer load tests.
    options.ServerWide.Enabled = true;
    options.ServerWide.BurstSize = 10_000;
    options.ServerWide.RequestsPerSecond = 10_000;
    options.ServerWide.QueueLimit = 10_000;

    // Optional orthogonal whole-application concurrency guard.
    // options.GlobalConcurrencyLimit = 500;
});

WebApplication app = builder.Build();
app.UseRateLimiter();
```

The same options bind at startup from `RequestTrafficShaping`; class defaults are applied first, JSON/configuration second, and the optional callback last:

```json
{
  "RequestTrafficShaping": {
    "PerClient": {
      "Enabled": true,
      "BurstSize": 40,
      "RequestsPerSecond": 10,
      "QueueLimit": 20
    },
    "ServerWide": {
      "Enabled": true,
      "BurstSize": 10000,
      "RequestsPerSecond": 10000,
      "QueueLimit": 10000
    }
  }
}
```

The limiter chain is per-client token bucket, then the shared server-wide token bucket, then the optional `GlobalConcurrencyLimit`. Both token buckets use the native .NET implementation with bounded oldest-first queues. `PerClient.Enabled` and `ServerWide.Enabled` both default to `true`; disabling either token-bucket layer skips only that layer. Server-wide `BurstSize`, `RequestsPerSecond`, and `QueueLimit` each default to `10,000` as generous WebLib infrastructure starting values, not as a capacity guarantee. Tune them after representative load tests for the consuming application. Both token-bucket option groups require positive burst/rate values, a non-negative queue, and `BurstSize >= RequestsPerSecond` because the current native mapping replenishes once per second. Retry timing remains based on native lease metadata. These settings configure limiter construction at startup; they do not live-reconfigure already running token buckets. If a trusted reverse proxy supplies the client IP, run Forwarded Headers before rate limiting.

### Request traffic logging

Request traffic logging builds on ASP.NET Core HTTP Logging but keeps one combined, structured traffic event per request with explicit completion semantics:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging;

builder.Services.AddRequestTrafficLogging();

WebApplication app = builder.Build();
app.UseRequestTrafficLogging();
```

The completion layer distinguishes completed, aborted, and faulted requests while framework HTTP Logging owns request/response capture. Sensitive header values are redacted by default, and body capture is bounded. Register `UseRequestTrafficLogging()` before exception-handling middleware when handled exceptions should still be classified as faulted with their final handled response status.
## 🌐 Kestrel and SNI

`ConfigureKestrelSniFromConfiguration(...)` is the top-level entry point for
configuration-driven HTTP/HTTPS listeners and reload-safe SNI certificate selection. It combines
Kestrel with NetLib's managed-certificate primitives while keeping listener policy and
certificate lifecycle in one place.

The complete setup uses this application-owned layout:

```text
<application>/
├── AppSettings/
│   ├── KestrelSettings.json                ← startup-fixed listener policy
│   └── CertificatesMappingSettings.json    ← protected, reloadable SNI mappings
├── AppCerts/
│   └── localhost.pfx                       ← existing or WebLib-managed certificate
├── AppProtectionKeys/
│   └── ...                                 ← persistent ASP.NET Core Data Protection key ring
└── wwwroot/                                ← ASP.NET Core web root
```

### Register configuration and protect certificate passwords

Add the configuration sources before the application is built:

```csharp
using System;
using System.IO;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Sources;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.Kestrel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

WebApplicationBuilder builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory(args);

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string settingsDirectory = directories[DefaultDirectory.ApplicationSettings];

// Replace the implicit host sources with the selected process-level sources.
builder.ResetToMinimalConfigurationSources(
    includeCommandLineArguments: true,
    includeEnvironmentVariables: true);

// Listener policy is read once while Kestrel is configured.
builder.Configuration.AddJsonFile(
    path: Path.Combine(settingsDirectory, "KestrelSettings.json"),
    optional: false,
    reloadOnChange: false);

// Generate a different stable factor for each application.
byte[] applicationFactor =
{
    0x23, 0x52, 0x66, 0x37, 0x5A, 0x39, 0x27, 0x27,
    0x5E, 0x52, 0x6C, 0x2E, 0x36, 0x49, 0x45, 0x4E,
    0x79, 0x4A, 0x52, 0x43, 0x4E, 0x4D, 0x3F, 0x5E,
    0x50, 0x5A, 0x6A, 0x5F, 0x4E, 0x32, 0x28, 0x4E,
};

string configurationProtectionSecret =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PROTECTION_SECRET")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PROTECTION_SECRET is required.");

ConfigurationValueCodec certificatePasswordCodec =
    ConfigurationValueCodecs.Compose(
        codecs:
        [
            // Separate this application and purpose from another use of the deployment secret.
            ConfigurationValueCodecs.AesPassword(passwordAsciiBytes: applicationFactor),
            // Add the externally supplied secret factor.
            ConfigurationValueCodecs.AesPassword(password: configurationProtectionSecret),
            // Resist an offline copy to a different physical-machine identity.
            ConfigurationValueCodecs.PhysicalMachineBoundAes(),
            // Persist the outer key material through ASP.NET Core Data Protection.
            AspNetDataProtectionConfigurationValueCodecs.DataProtection(
                directories: directories,
                purpose: nameof(certificatePasswordCodec)),
        ]);

SwitchableJsonRegistrationOptions certificateSourceOptions = new()
{
    // Follow and reload the active certificate-mapping file.
    ReloadOnChange = true,
    // Protect only certificate passwords, not routing or certificate file names.
    ValueProtection = JsonConfigurationValueProtection.ForPaths(
        codec: certificatePasswordCodec,
        patterns: ["CertificatesMappingSettings:*:Password"]),
};

// Publish complete mapping generations and keep last-known-good data on failure.
builder.AddSwitchableJsonFile(
    name: "KestrelCertificateMappings",
    initialPath: Path.Combine(settingsDirectory, "CertificatesMappingSettings.json"),
    options: certificateSourceOptions);
```

`ResetToMinimalConfigurationSources(...)` clears every existing configuration source, then this
example re-adds environment variables and command-line arguments. The explicit JSON sources are
registered afterwards and therefore have higher precedence for overlapping keys. The two files
separate startup-fixed server policy from reloadable certificate mappings.

### Configure Kestrel and run

The top-level WebLib extension consumes those sources and configures the complete server:

```csharp
// Resolve PFX files below NetLib's validated application certificate directory.
builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: directories[DefaultDirectory.ApplicationCerts]);

WebApplication app = builder.Build();
app.MapGet("/", () => "HTTPS is ready");
app.Run();
```

The switchable source combines last-known-good reloads with
targeted value protection: only `CertificatesMappingSettings:*:Password` is encoded on disk and
decoded before WebLib sees the candidate configuration. Generate a stable, application-specific
`applicationFactor` byte array; it avoids placing the factor in the assembly string table but
remains recoverable and is not a secret. Actual secrecy depends on protecting
`APP_CONFIGURATION_PROTECTION_SECRET`. This generic external deployment secret can protect other
application configuration too; the application-owned factor and selected path provide the
certificate-specific separation. It is not itself a certificate password. The machine-bound layer
means the encoded file must be provisioned on its target machine. The outer ASP.NET Core Data
Protection layer uses the persistent `ApplicationProtectionKeys` directory plus the stable
entry-assembly name and purpose. The convenience codec derives the key-ring path from `directories`
and the application name from `Assembly.GetEntryAssembly()`; neither depends on mutable host
configuration. Here, `nameof(certificatePasswordCodec)` is the persisted purpose, so treat that
variable name as a compatibility contract. Back up and retain the complete key ring while protected
values may still depend on it.

### Defense in depth and limits

The persisted certificate password passes through the codecs in order:

```text
clear text → application byte factor → deployment secret → machine binding → Data Protection → JSON
```

An offline attacker must reverse every layer. In practical terms, that requires the protected JSON
value, the exact codec composition and order, the application factor obtainable from the assembly,
`APP_CONFIGURATION_PROTECTION_SECRET`, the original system/platform UUID used by the machine
binding, the complete Data Protection key ring, and the matching application name and purpose. The
live `ConfigurationValueCodec` instance is not required if the recipe can be reconstructed from the
assembly or source. The algorithms, recipe, and identifiers are not secrets; security comes from the
independently held secret and key material. Merely exposing the JSON file, executable, environment
secret, or key-ring directory alone is therefore insufficient. A complete application-directory
copy still lacks the deployment secret and original platform identity unless those were collected
separately.

This separation reduces the chance that one path-traversal bug, accidental file publication, backup
leak, or configuration disclosure immediately reveals the PFX password. It is defense in depth, not
a runtime security boundary. Code execution inside the application process, or equivalent access
under its identity, can read the already decoded configuration, inspect process state, or invoke the
same protection pipeline. A process dump, secret-bearing log, or endpoint that exposes configuration
can bypass several layers at once.

Availability is the inverse risk: losing any required factor can make the value permanently
unrecoverable. Retain the key ring and deployment secret, keep the application name and purpose
stable, and account for platform-UUID changes during VM cloning, migration, or reprovisioning. Omit
the machine-bound layer when portable restore or multi-machine use is more important than resistance
to an offline application-directory copy.

`KestrelSettings.json`:

```json
{
  "KestrelSettings": {
    "HTTP_PORT": 8080,
    "HTTPS_PORT": 8443,
    "ListenScope": "Localhost",
    "AddServerHeader": false,
    "Protocols": "Http1AndHttp2",
    "PreferLongestSuffixMatch": true,
    "TlsProtocolPolicy": "Default"
  }
}
```

`CertificatesMappingSettings.json`:

```json
{
  "CertificatesMappingSettings": [
    {
      "SNI": "localhost",
      "FileName": "localhost.pfx",
      "Password": "change-me"
    }
  ]
}
```

The call performs the complete server setup:

- creates the configured localhost or any-IP HTTP/HTTPS listeners;
- applies HTTPS protocol and TLS policy;
- loads configured PFX files and performs self-signed recovery only when explicitly enabled;
- selects certificates by exact SNI name or DNS-suffix boundary;
- uses the first mapping as the stable fallback when SNI is absent or unmatched;
- publishes mapping reloads atomically and retains the last-known-good generation when a reload
  fails.

Certificate-directory resolution uses the explicit `certDirOverride` first, then the top-level
`CertificatesDirectory` value, and finally `certs` below the content root. Relative paths are
resolved against the content root. Mapping paths and symbolic-link targets must remain inside the
selected certificate directory.

Listener settings are startup-fixed:

| Setting | Default | Behavior |
| --- | --- | --- |
| `HTTP_PORT` | disabled | Positive values enable a plaintext HTTP/1 listener. |
| `HTTPS_PORT` | disabled | Values from `1` through `65535` enable the SNI HTTPS listener. |
| `ListenScope` | `Localhost` | Use `AnyIP` to bind all available addresses. |
| `AddServerHeader` | `false` | Controls Kestrel's `Server` response header. |
| `Protocols` | `Http1AndHttp2` | HTTPS `HttpProtocols` value. |
| `PreferLongestSuffixMatch` | `true` | Tries the most-specific configured suffix first. |
| `TlsProtocolPolicy` | `Default` | `Default` = TLS 1.2/1.3; `Strict` = TLS 1.3 only. Compatibility and legacy modes require explicit opt-in. |

At least one listener must be enabled. Certificate mappings are required even for the HTTP-only
form of this SNI helper.

Each certificate mapping requires `SNI` and `FileName`. `Password` defaults to an empty string.
Provision its initial clear-text value on the target machine rather than committing it; registration
then rewrites matching values as codec envelopes while publishing clear text only in memory.
Additional DNS and IP values are used as typed SANs only when an explicit recovery mode makes WebLib generate a self-signed certificate.

`CertificateRecoveryMode` expresses whether recovery is enabled and who owns the configured PFX. Omitting the setting, or providing an invalid value, selects `None`:

- `None` is the default and performs classic PFX loading. WebLib does not generate or persist a recovery certificate.
- `PreserveExisting` enables an in-memory self-signed fallback but never creates or replaces the configured PFX path.
- `ReplaceExpired` permits creation of a missing managed PFX and normal expiry renewal.
- `ReplaceAnyUnusable` is for fully application-managed, disposable certificates, such as self-signed certificates in internal proxy setups.

The complete decision matrix is:

| PFX state or failure | `None` | `PreserveExisting` | `ReplaceExpired` | `ReplaceAnyUnusable` |
| --- | --- | --- | --- | --- |
| File or parent directory is missing | Fail; do not create anything | Return self-signed recovery in memory only | Create, validate, and persist a new self-signed PFX | Same |
| Valid, currently active, and contains a private key | Load the existing PFX | Same | Same | Same |
| Successfully imported but expired | Fail; leave the file untouched | Keep the file; return self-signed recovery in memory | Atomically replace with a new self-signed PFX | Atomically replace with a new self-signed PFX |
| Successfully imported but not yet valid | Fail; leave the file untouched | Keep + memory recovery | Keep + memory recovery | Replace |
| Successfully imported but missing its private key | Fail; leave the file untouched | Keep + memory recovery | Keep + memory recovery | Replace |
| Password mismatch, corrupt PFX, unsupported content, or other import failure | Fail; leave the path untouched | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| I/O read failure, such as a sharing or device error | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| Access denied while reading | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement, although the same permissions may prevent the write |
| Persistence or atomic-move failure during an authorized create/replace | Not applicable | Not applicable | Return the generated certificate in memory and report the persistence failure | Same |
| Another process creates a previously missing file first | Not applicable | Not applicable | Do not overwrite the winner; return this process’s generated certificate in memory | Same |

“Keep + memory recovery” means the original file remains byte-for-byte untouched and a generated
self-signed certificate is available only for the running process. At initial startup it can keep the
TLS listener available. During reload, WebLib retains a complete usable last-known-good generation
instead of publishing a memory-only recovery generation. With `None`, an invalid initial PFX fails
startup; an invalid hot-reload candidate is rejected while the last-known-good generation remains active.

Deleting an application-managed self-signed PFX requests fresh creation only under `ReplaceExpired`
or `ReplaceAnyUnusable`. `PreserveExisting` stays memory-only, while `None` fails without recovery.
`ReplaceAnyUnusable` can overwrite an externally managed certificate if selected incorrectly, and
authorization to replace does not guarantee that filesystem permissions will allow the operation.

Only `CertificatesMappingSettings` is hot-reloadable. A configuration reload builds and validates
a complete replacement generation before publishing it. Changing ports, bind scope, protocols,
TLS policy, matching strategy, or the certificate directory requires a host restart. Replacing a
PFX file alone does not emit a configuration reload token; the configuration provider must also
signal a reload.

Earlier copies of this helper used a single `SanNames` array. When migrating, split it into
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses`, choose a recovery mode where necessary, and use
the `Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace instead of an application-local
or `Eigenverft.Routed.RequestFilters` implementation.

## 📁 Isolated static and PWA hosting

WebLib provides thin pipeline partitioning and static-file conveniences without introducing a separate
routing or mount system. `MapIsolated(...)` delegates to native ASP.NET Core `Map`, preserves the matched
path segment, and owns the matching URL subtree. `MapRemaining(...)` makes the shell pipeline explicit
after all isolated subtrees.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Pipeline;
using Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles;

app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles(AdditionalMappings.WebApp);
});

app.MapIsolated("/downloads", downloads =>
{
    downloads.UseStaticFiles(AdditionalMappings.Media);
});

app.MapRemaining(shell =>
{
    shell.UseRouting();
    shell.UseEndpoints(endpoints =>
    {
        // Configure the remaining Razor/Blazor/API endpoints here.
        endpoints.MapRazorComponents<App>();
    });
});
```

`MapRemaining` deliberately exposes a normal `IApplicationBuilder`; endpoint APIs such as `MapStaticAssets()` and
`MapRazorComponents<T>()` therefore stay inside native `UseEndpoints(...)` rather than requiring a WebLib-specific
hybrid pipeline/router builder.

Because the isolated branch keeps the matched request path, `/apps/index.html` resolves naturally against
`wwwroot/apps/index.html` and `/downloads/file.avif` against `wwwroot/downloads/file.avif`. The web-app branch
uses native ASP.NET Core `UseDefaultFiles()` plus the typed `UseStaticFiles(AdditionalMappings.WebApp)` extension.
A missing file reaches the native end of the isolated branch and returns 404; it does not rejoin `MapRemaining`,
Razor Components, or another shell fallback.
Outer status-code-page re-execution is also disabled for isolated requests so a global
`UseStatusCodePagesWithReExecute(...)` cannot transfer ownership of that branch-owned 404 to the shell.

Static-file mappings remain additive to the target framework defaults:

- `AdditionalMappings.WebApp` adds only `.br` and `.dat` as `application/octet-stream`; `.webmanifest` and
  `.wasm` already come from the ASP.NET Core defaults on both supported target frameworks.
- `AdditionalMappings.Media` adds `.avif` as `image/avif` on `net8.0`; it is intentionally a no-op on
  `net10.0`, where ASP.NET Core already provides that mapping.
- `AdditionalMappings.Combine(...)` composes typed groups when one branch needs more than one set.

The `FileExtensionContentTypeProvider` remains an internal implementation detail. There is no separate
PWA/Blazor-specific `UseStaticFilesWithPwaAndBlazorContentTypes(...)` API and no universal mount primitive.
Use `MapIsolated` + normal ASP.NET Core middleware for owned subtrees, then `MapRemaining` for the shell.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 📖 Documentation

- [Guides and API reference](https://eigenverft.github.io/Eigenverft.WebLib.Infrastructure/docfx/production/)

The repository CI/CD workflow handles build, test, packaging, reports, DocFX generation, artifact
distribution, and deployment-channel documentation.

## 🧪 Build and test

From the repository root:

```shell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

## 🚢 Releases

`main` is the production channel. Package releases are built, tested, documented, packed, and
published by the repository CI/CD workflow.

Package versions follow the Eigenverft Drydock timestamp-based versioning scheme. Published versions
and download history are available on [NuGet.org](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure).

## 🤝 Contributing and support

- 🐛 [Open an issue](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/issues)
- 🔧 [Submit a pull request](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/pulls)
- 📦 [View the package on NuGet.org](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](LICENSE) by Eigenverft.

---

<div align="center">
Made with ❤️ by Eigenverft
</div>
