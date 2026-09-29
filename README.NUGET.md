# 🧱 Eigenverft.WebLib.Infrastructure

<!-- Maintenance note: Keep README.NUGET.md aligned with this README for the shared landing, package table, badges, and historical monolith examples. README.NUGET.md must use absolute GitHub URLs for repository links. -->

[![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets: net8.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure#-target-frameworks) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

> [!IMPORTANT]
> `Eigenverft.WebLib.Infrastructure` is a frozen legacy monolith retained for compatibility. New development and new installations should use the individually versioned capability packages below. This repository and the split packages remain actively maintained.

## 🧩 Active capability packages

Choose only the capabilities your application needs. Each package guide documents its API, dependencies, and behavior.
Some capabilities use shared host-independent services from [Eigenverft.NetLib.Infrastructure](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure); each guide lists its package-specific dependencies.

| Capability package | Problem it solves / when to use it | NuGet | Current version / downloads | Guide |
| --- | --- | --- | --- | --- |
| `Eigenverft.WebLib.Hosting.DirectoryLayout` | An ASP.NET Core app needs content root, web root, wwwroot, and writable application directories to agree on one executable-rooted layout. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hosting.DirectoryLayout?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hosting.DirectoryLayout?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.Hosting.DirectoryLayout/Readme.md) |
| `Eigenverft.WebLib.CanonicalHostRedirect` | Public URLs must converge to one host and HTTPS target in a single redirect, including reverse-proxy deployments. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.CanonicalHostRedirect?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.CanonicalHostRedirect?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.CanonicalHostRedirect/Readme.md) |
| `Eigenverft.WebLib.HealthProbes` | A service needs a tiny liveness endpoint that bypasses later middleware and suppresses browser favicon noise. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.HealthProbes?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.HealthProbes?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.HealthProbes/Readme.md) |
| `Eigenverft.WebLib.Hsts` | Applications need consistent native HSTS defaults and configuration while keeping Development free of HSTS middleware. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hsts?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hsts?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.Hsts/Readme.md) |
| `Eigenverft.WebLib.Middleware.Primitives` | Reusable middleware needs isolated non-rejoining branches, use-site options, typed features, and explicit short-circuit responses. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.Middleware.Primitives?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Middleware.Primitives?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Middleware.Primitives) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.Middleware.Primitives/Readme.md) |
| `Eigenverft.WebLib.ClientNetwork` | Diagnostics need normalized peer and forwarded-IP observations without confusing those facts with trust decisions. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.ClientNetwork?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.ClientNetwork?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.ClientNetwork/Readme.md) |
| `Eigenverft.WebLib.Kestrel.Sni` | Kestrel listeners and SNI certificates must be configuration-driven, reload-safe, and recoverable without losing the last good generation. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.Kestrel.Sni?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Kestrel.Sni?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.Kestrel.Sni/Readme.md) |
| `Eigenverft.WebLib.RequestTrafficLogging` | Each request needs one bounded, structured request/response record with clear completion semantics and configurable sensitive-data handling. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficLogging?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficLogging?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.RequestTrafficLogging/Readme.md) |
| `Eigenverft.WebLib.RequestTrafficShaping` | A service needs layered per-client and server-wide rate limits plus optional concurrency protection using native ASP.NET Core primitives. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficShaping?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficShaping?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficShaping) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.RequestTrafficShaping/Readme.md) |
| `Eigenverft.WebLib.SerilogRelayReceiver` | SerilogRelay batches need validated ASP.NET Core ingestion with optional bearer authentication and host-owned durable persistence through EF Core or a custom batch handler. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.SerilogRelayReceiver) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.SerilogRelayReceiver?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.SerilogRelayReceiver) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.SerilogRelayReceiver?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.SerilogRelayReceiver) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.SerilogRelayReceiver/Readme.md) |
| `Eigenverft.WebLib.StaticFiles` | Applications must serve additional MIME types without replacing the existing ASP.NET Core content-type mappings. | [NuGet](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) | [![NuGet](https://img.shields.io/nuget/v/Eigenverft.WebLib.StaticFiles?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) [![Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.StaticFiles?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.StaticFiles) | [Package guide](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/src/sln/Eigenverft.WebLib.StaticFiles/Readme.md) |

## 🕰️ Legacy monolith reference

The examples below document the frozen `Eigenverft.WebLib.Infrastructure` monolith for existing consumers and migration work. They are not a current package map or a recommendation for new installations. The current split-package APIs are linked in the table above.

### ✨ Historical monolith capabilities

This snapshot describes features of the frozen monolith only; use the active package table above to select current packages.

| Capability | Problem solved | Starting point |
| --- | --- | --- |
| Kestrel and SNI | Configuration-driven listeners, host-name certificate selection, and last-known-good certificate reloads | `ConfigureKestrelSniFromConfiguration(...)` |
| Managed certificates | Existing PFX loading or policy-controlled self-signed recovery | `CertificateRecoveryMode` |
| Protected mappings | Persist certificate passwords through composable protection instead of leaving clear text after provisioning | `AspNetDataProtectionConfigurationValueCodecs` |
| Web host directories | Apply NetLib's executable-rooted layout to content root, web root, and `wwwroot` | `WebApplicationBuilderFactory` |
| Canonical redirects | Normalize apex/www/aliases and HTTPS in one redirect without leaking an incoming HTTP port into the HTTPS target | `AddCanonicalHostRedirect(...)` + `UseCanonicalHostRedirect()` |
| Health probe | Short-circuit GET/HEAD `/health` before later filters and suppress probe-originated `/favicon.ico` noise | `UseHealthProbeFaviconAware()` |
| HTML status responses | Write a small explicit HTML status response for middleware short-circuits using ASP.NET Core reason phrases | `WriteHtmlStatusResponseAsync(...)` |

### 📦 Legacy installation

> **Compatibility only:** This command installs the frozen monolith. New applications should install the required capability packages from the table above instead.

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

### 🚀 Historical monolith quick start

> The namespaces and APIs in this sample belong to the frozen monolith. For current APIs, use the linked package guides above.

Create an executable-rooted ASP.NET Core host while using NetLib's shared directory
layout:

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

`WebApplicationBuilderFactory` adds only the web-specific projection: ASP.NET Core
content/web roots and the semantic `"Web"` directory entry. Directory creation,
validation, writable probes, standard directory keys, and DI registration are
provided by NetLib.

### 🔥 Optional self-HTTP startup warmup — legacy-only

> **Migration:** Self-HTTP warmup was extracted to [Eigenverft.NetLib.Hosting.SelfHttpWarmup on NuGet](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) ([package guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Hosting.SelfHttpWarmup/Readme.md)). Use that NetLib package for new warmup integrations; the example below shows the historical WebLib API.

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

### 🔐 ASP.NET Core Data Protection adapter — legacy-only

> **Migration:** The ASP.NET Core Data Protection adapter was extracted to [Eigenverft.NetLib.Security.DataProtection on NuGet](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) ([package guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Security.DataProtection/Readme.md)). Use that NetLib package for current transforms and configuration-value codecs; the example below shows the historical WebLib API.

`AspNetDataProtectionStringTransforms` adapts an ASP.NET Core
`IDataProtectionProvider` to NetLib's `ReversibleStringTransform` abstraction.
The generic transform and configuration-value codec infrastructure remains in
NetLib, so Data Protection can participate without duplicating the generic codec
stack in WebLib.

`AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` provides the configuration-value
convenience layer. Pass the application directory layout and a stable purpose; WebLib derives the
standard key-ring path and entry-assembly discriminator. The returned `ConfigurationValueCodec` can
be used independently or at any position in `ConfigurationValueCodecs.Compose(...)`.

### 🧰 Small request-pipeline helpers

> These examples use monolith APIs. Current capabilities are separated across the packages in the overview; consult the relevant package guide before adopting a helper.

WebLib includes a few intentionally small ASP.NET Core helpers that are useful outside the larger
RequestFilters stack.

#### Canonical host and HTTPS redirect

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

#### Public use-site options monitor

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

#### Health probe and favicon suppression

`UseHealthProbeFaviconAware()` handles only GET and HEAD for `/health`, returns `200 OK` with `OK`
for GET, and short-circuits the rest of the pipeline. A GET or HEAD for `/favicon.ico` returns
`204 No Content` only when its `Referer` points to `/health`. Keep this middleware before filters that
a health probe must bypass.

#### Explicit HTML status response

For a middleware that intentionally terminates a request with an HTML response, use:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;

await context.Response.WriteHtmlStatusResponseAsync(StatusCodes.Status403Forbidden);
```

The helper uses `ReasonPhrases.GetReasonPhrase(...)`; WebLib does not maintain its own HTTP status
code description table. General application error handling remains the responsibility of ASP.NET
Core Status Code Pages or Problem Details.

#### Host filtering remains framework-owned

WebLib intentionally does not provide an `AddAllowedHosts` replacement. `WebApplication.CreateBuilder()`
already wires ASP.NET Core host filtering to the live configuration object. Clearing
`builder.Configuration.Sources` and adding replacement sources does not remove that wiring, so a
rebuilt `AllowedHosts` value is still consumed by the built-in host-filtering options.

#### HSTS

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

#### Request traffic shaping

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

#### Request traffic logging

Request traffic logging builds on ASP.NET Core HTTP Logging but keeps one combined, structured traffic event per request with explicit completion semantics:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging;

builder.Services.AddRequestTrafficLogging();

WebApplication app = builder.Build();
app.UseRequestTrafficLogging();
```

The completion layer distinguishes completed, aborted, and faulted requests while framework HTTP Logging owns request/response capture. Sensitive header values are redacted by default, and body capture is bounded. Register `UseRequestTrafficLogging()` before exception-handling middleware when handled exceptions should still be classified as faulted with their final handled response status.
### 🌐 Kestrel and SNI

> For new applications, use `Eigenverft.WebLib.Kestrel.Sni` from the table above. The Data Protection adapter in this historical setup moved to [Eigenverft.NetLib.Security.DataProtection on NuGet](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) ([package guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Security.DataProtection/Readme.md)); it is a separate NetLib package and is not included in `Eigenverft.WebLib.Kestrel.Sni`.

`ConfigureKestrelSniFromConfiguration(...)` is the package's top-level server setup. It configures
HTTP/HTTPS listeners, TLS policy, managed PFX files, SNI selection, and atomic certificate reloads
while using NetLib's certificate primitives underneath.

The setup separates startup policy, reloadable mappings, certificates, and protection keys:

```text
<application>/
├── AppSettings/
│   ├── KestrelSettings.json                ← startup-fixed listener policy
│   └── CertificatesMappingSettings.json    ← protected, reloadable SNI mappings
├── AppCerts/
│   └── localhost.pfx                       ← existing or WebLib-managed certificate
├── AppProtectionKeys/
│   └── ...                                 ← persistent Data Protection key ring
└── wwwroot/
```

#### Register configuration and protect certificate passwords

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
            // Resist an offline copy to another physical-machine identity.
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

The reset clears every existing source and then re-adds environment variables and command-line
arguments. The explicit JSON sources are registered afterwards and therefore have higher precedence
for overlapping keys.

#### Configure Kestrel and run

```csharp
// Resolve PFX files below NetLib's validated application certificate directory.
builder.WebHost.ConfigureKestrelSniFromConfiguration(
    certDirOverride: directories[DefaultDirectory.ApplicationCerts]);

WebApplication app = builder.Build();
app.Run();
```

The two JSON files keep startup-fixed listener configuration separate from reloadable certificate
mappings. The switchable source protects
only `CertificatesMappingSettings:*:Password` on disk, decodes it before publication, and retains
the last-known-good configuration after a rejected reload. Generate a stable application-specific
byte-array factor, protect `APP_CONFIGURATION_PROTECTION_SECRET`, and provision the file on its
target machine. This generic external deployment secret can protect other application configuration;
the factor and selected path separate this certificate use. It is not itself a certificate password.
The byte array avoids an assembly string-table entry but remains a recoverable structural factor
rather than a secret. The outer ASP.NET Core Data Protection layer uses the persistent
`ApplicationProtectionKeys` directory. The convenience codec derives that path from the directory
layout and its application discriminator from `Assembly.GetEntryAssembly()`, rather than mutable host
configuration. Preserve the complete key ring, application name, and purpose while protected values
may still need to be decoded. Because the purpose comes from
`nameof(certificatePasswordCodec)`, treat that variable name as a persisted compatibility contract.

#### Defense in depth and limits

The stored password is protected in this order:

```text
clear text → application byte factor → deployment secret → machine binding → Data Protection → JSON
```

Offline reversal requires the protected JSON value, exact codec composition and order, application
factor, deployment secret, original platform UUID, complete Data Protection key ring, and matching
application name and purpose. The live codec object is unnecessary if its recipe is reconstructed
from the assembly or source. A leak of only the JSON file, executable, environment secret, or
key-ring directory is insufficient. This makes accidental single-source exposure less likely to
reveal the PFX password.

It does not protect against code execution inside the application process: such an attacker can read
the decoded configuration or invoke the same pipeline. Losing any factor also prevents legitimate
recovery. Preserve the key ring and deployment secret, keep identities stable, and omit machine
binding when portable restore or multi-machine deployment is required.

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

The extension loads configured PFX files and performs self-signed recovery only when explicitly enabled.
It matches exact SNI names and DNS suffixes, prefers the longest suffix by default, and uses the
first mapping as the fallback when SNI is absent or unmatched.

Certificate-directory resolution uses the explicit override first, then the top-level
`CertificatesDirectory` value, and finally `certs` below the content root. Mapping paths and
symbolic-link targets cannot escape that directory.

| Setting | Default | Behavior |
| --- | --- | --- |
| `HTTP_PORT` | disabled | Positive values enable a plaintext HTTP/1 listener. |
| `HTTPS_PORT` | disabled | Values from `1` through `65535` enable the SNI HTTPS listener. |
| `ListenScope` | `Localhost` | Use `AnyIP` to bind all available addresses. |
| `AddServerHeader` | `false` | Controls Kestrel's `Server` response header. |
| `Protocols` | `Http1AndHttp2` | HTTPS `HttpProtocols` value. |
| `PreferLongestSuffixMatch` | `true` | Tries the most-specific configured suffix first. |
| `TlsProtocolPolicy` | `Default` | TLS 1.2/1.3 by default; `Strict` selects TLS 1.3 only. |

At least one listener and one usable certificate mapping are required. Provision the initial PFX
password on the target machine rather than committing it; NetLib rewrites the selected value as a
codec envelope during source registration and exposes clear text only in memory.

Recovery is opt-in. An omitted or invalid `CertificateRecoveryMode` selects `None`, which performs classic PFX loading without generating or persisting fallback certificates. `PreserveExisting` enables memory-only self-signed recovery without changing the configured path, `ReplaceExpired` permits missing-file creation and managed expiry renewal, and `ReplaceAnyUnusable` is for fully application-managed disposable certificates.

| PFX state or failure | `None` | `PreserveExisting` | `ReplaceExpired` | `ReplaceAnyUnusable` |
| --- | --- | --- | --- | --- |
| Missing file or parent directory | Fail; create nothing | Memory recovery only | Create and persist | Create and persist |
| Valid and contains a private key | Load | Load | Load | Load |
| Imported and expired | Fail | Keep + memory recovery | Replace | Replace |
| Imported but not yet valid | Fail | Keep + memory recovery | Keep + memory recovery | Replace |
| Imported but missing private key | Fail | Keep + memory recovery | Keep + memory recovery | Replace |
| Password mismatch, corrupt/unsupported PFX, or other import failure | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| I/O read failure | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement |
| Access denied | Fail | Keep + memory recovery | Keep + memory recovery | Authorize replacement; the write may still fail |
| Persistence or atomic-move failure during an authorized create/replace | Not applicable | Not applicable | Return generated certificate in memory and report the failure | Same |
| Concurrent creator wins missing-file race | Not applicable | Not applicable | Keep the winner; return this process’s generated certificate in memory | Same |

At startup, an invalid PFX under `None` fails without recovery. During reload, WebLib rejects an invalid candidate and keeps the last-known-good generation active. Memory recovery can keep TLS available at startup for explicit recovery modes. Deleting an application-managed self-signed PFX requests fresh creation only under `ReplaceExpired` or `ReplaceAnyUnusable`; `PreserveExisting` remains memory-only. `ReplaceAnyUnusable` can overwrite an externally managed certificate if selected incorrectly.

Only `CertificatesMappingSettings` is hot-reloadable. WebLib publishes a complete replacement
generation atomically and keeps the last-known-good certificates active if a reload fails.
Listener changes require a host restart. A PFX file change is observed on the next configuration
reload; changing the file alone does not emit a reload token.

When migrating from the earlier helper, replace `SanNames` with the typed
`AdditionalSelfSignedCertificateDnsNames` and
`AdditionalSelfSignedCertificateIpAddresses` properties and use the
`Eigenverft.WebLib.Infrastructure.Hosting.Kestrel` namespace.

### 📁 Isolated static and PWA hosting

> This historical example combines monolith APIs. Current static-file MIME mappings and isolated pipeline primitives are available in the separate `Eigenverft.WebLib.StaticFiles` and `Eigenverft.WebLib.Middleware.Primitives` packages; see their guides above.

Use `MapIsolated(...)` for URL subtrees that must be exclusively owned by a static/PWA branch and
`MapRemaining(...)` for the remaining shell pipeline. Both are thin wrappers over native non-rejoining
ASP.NET Core branch semantics; no separate routing or mount system is introduced.

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
    shell.UseEndpoints(endpoints => endpoints.MapRazorComponents<App>());
});
```

`MapRemaining` deliberately exposes a normal `IApplicationBuilder`; endpoint APIs such as `MapStaticAssets()` and
`MapRazorComponents<T>()` therefore stay inside native `UseEndpoints(...)` rather than requiring a WebLib-specific
hybrid pipeline/router builder.

`MapIsolated` preserves the matched path segment, so `/apps/...` resolves against `wwwroot/apps/...` using
normal ASP.NET Core default-file/static-file middleware. The web-app case composes native `UseDefaultFiles()` with
`UseStaticFiles(AdditionalMappings.WebApp)`; WebLib does not add a PWA-specific hosting primitive. Missing files end with the native branch 404 and
do not fall through into the shell. Outer `UseStatusCodePagesWithReExecute(...)` handling is disabled for
isolated requests so global re-execution cannot escape that ownership boundary.

Mappings are strictly additive to ASP.NET Core defaults: `AdditionalMappings.WebApp` backfills only `.br`
and `.dat`; `AdditionalMappings.Media` backfills `.avif` only on `net8.0` and is a no-op on `net10.0` where
that mapping is already built in. `AdditionalMappings.Combine(...)` composes typed groups. The underlying
`FileExtensionContentTypeProvider` remains internal, and there is no separate legacy-style
`UseStaticFilesWithPwaAndBlazorContentTypes(...)` API.

## 🎯 Target frameworks

All eleven capability packages in the overview ship dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Documentation](https://eigenverft.github.io/Eigenverft.WebLib.Infrastructure/docfx/production/)
- [Issues](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/issues)
- [Legacy monolith NuGet page (compatibility only)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
