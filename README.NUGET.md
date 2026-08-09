# 🧱 Eigenverft.WebLib.Infrastructure

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.WebLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Reusable ASP.NET Core hosting infrastructure for Eigenverft applications, with a deliberately application-neutral API surface.

The package focuses on hosting primitives that are useful beyond a single application: directory layout, configuration composition and diagnostics, switchable JSON sources, named configuration sets, protected JSON settings, bootstrap logging, certificate handling, and Kestrel/SNI configuration.

---

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.WebLib.Infrastructure` |
| Target frameworks | .NET 8 and .NET 10 |
| Hosting | ASP.NET Core / Generic Host infrastructure |
| Configuration | JSON settings, precedence diagnostics, switchable JSON sources, named configuration sets |
| Security helpers | Certificates, protected settings, machine-binding primitives |
| Web server | Kestrel listener and SNI certificate configuration |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

Or with the NuGet Package Manager:

```powershell
Install-Package Eigenverft.WebLib.Infrastructure
```

## 🚀 Quick start

Create an executable-rooted web host with the standard directory layout:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory();
var layout = builder.GetDirectoryLayout();

var app = builder.Build();
app.Run();
```

Add a named JSON source that can switch files at runtime without moving its position in the normal .NET configuration-provider stack:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Microsoft.Extensions.DependencyInjection;

builder.AddSwitchableJsonFile(
    name: "RuntimeSettings",
    initialPath: "AppSettings/runtime.json",
    optional: true,
    reloadOnChange: true);

var app = builder.Build();

var runtimeSettings = app.Services
    .GetRequiredKeyedService<ISwitchableJsonConfiguration>("RuntimeSettings");

runtimeSettings.TrySwitch("AppSettings/runtime-candidate.json");
```

The switchable provider is intentionally agnostic: paths may represent profiles, blue/green configurations, tenants, or any other caller-defined concept, but the provider itself knows none of those concepts.

## 🧩 Main capabilities

### 📁 Executable-rooted directory layout

`Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout` provides a typed application directory contract rooted at `AppContext.BaseDirectory`.

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates and validates the configured direct-child directories, assigns the ASP.NET Core content and web roots, and registers the resulting `AppDirectoryLayout` for access before and after `Build()`.

`DefaultDirectory` includes conventional locations for application logs, data, state, certificates, settings, static web content, and other shared hosting state. Callers can keep the defaults or provide explicit folder-name overrides.

### ⚙️ Configuration sources and resolution diagnostics

`ResetToMinimalConfigurationSources(...)` replaces the builder's default configuration stack with a small explicit base: environment variables and, optionally, command-line arguments. Use it before adding application-specific providers because it intentionally clears the existing source collection.

`LogConfigurationResolution(...)` reports configuration-provider precedence and key collisions without logging configuration values. It follows the normal .NET rule: provider registration order determines precedence, with the last provider winning a key collision.

### 🔄 Switchable JSON configuration

`AddSwitchableJsonFile(...)` adds a normal `IConfiguration` source plus a stable keyed-DI runtime handle.

The runtime primitive supports direct switching, optional active-file watching, prepare / commit / abort for higher-level coordinators, last-known-good behavior for failed runtime candidates, separate source-lifecycle and `IConfiguration` notifications, and stale-preparation protection.

A source switch with an identical effective key/value snapshot remains observable through the provider lifecycle but does not emit an unnecessary `IConfiguration` reload. Runtime switching does not remove/reinsert the provider, so its configuration-stack precedence remains stable.

### 🧭 Named configuration sets

Configuration Sets coordinate one named value across multiple switchable JSON participants. They support allowed-value validation, prepare/commit coordination, lifecycle events, consistency reporting after partial failures, and self-describing `ConfigurationSets.json` state.

Bindings can follow a directory convention or use explicit `value => sourcePath` mappings when source files do not share a common layout. The coordinator remains application-neutral; environment, tenant, profile, or control-plane meaning stays with the consuming application.

### 🔐 JSON settings encoding and protection

`Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings` separates file encoding from runtime loading.

The API includes environment-aware JSON loading, in-place encoding of selected settings, in-memory decoding, composable value codecs, and explicit representation / friction / protection layers.

Available building blocks include Base64/Base92 representations, ROT13/Caesar friction layers, password-based AES-GCM protection, ASP.NET Core Data Protection, Windows machine-scope DPAPI, and physical machine-binding helpers.

These mechanisms deliberately document their threat-model boundaries. Representation and friction layers are not cryptographic protection, machine binding is not authorization, password-derived protection is only as strong as its input entropy, and DPAPI machine scope is Windows-specific.

### 📝 Bootstrap logging

`BootstrapLogger<TCategoryName>` exposes a process-wide early `Microsoft.Extensions.Logging.ILogger<T>` before the application DI container and final logging pipeline exist.

`CreateLogger(...)` is best-effort and captures an already initialized global Serilog logger when available, otherwise using the Microsoft logging fallback. `CreateRequiredSerilogLogger(...)` is fail-fast and builds an isolated Serilog bootstrap pipeline from JSON configuration.

Serilog remains an optional consumer dependency: the production WebLib has no direct Serilog package reference and accesses the optional integration through reflection.

### 🔏 Certificates

`SelfSignedCertificateFactory.Create(...)` creates caller-owned self-signed certificates for TLS server/client, code-signing, and email-protection scenarios using RSA or ECDSA profiles.

`ManagedCertificateFile.LoadOrCreate(...)` loads an existing managed PFX or creates a recovery certificate according to an explicit `CertificateRecoveryMode`. Managed-file replacement is prepared and validated before the target is replaced.

### 🌐 Kestrel and SNI

`ConfigureKestrelSniFromConfiguration(...)` configures HTTP/HTTPS listeners, binding scope, HTTP protocols, TLS policy, Server-header behavior, and reloadable SNI certificate selection.

Certificate mappings are the hot-reload boundary. Changed generations are prepared before publication, and failed candidates retain the last-known-good usable snapshot.

## 🛡️ Security model

This package contains security-related primitives, but it does not claim to turn application settings or local files into an isolated secret store.

The protected-settings APIs are intended to add practical barriers against common offline-copy and lateral-movement scenarios by composing independent factors where appropriate. Applications remain responsible for operating-system permissions, secret rotation, deployment security, backup protection, and appropriate key/password management.

Certificate recovery is availability-oriented. A generated self-signed certificate can keep HTTPS cryptographically available but is not automatically trusted by clients.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset. Preview target frameworks are intentionally excluded.

## 🧪 Build and test

From the repository root:

```shell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx --configuration Release
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx --configuration Release
```

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Issues](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/issues)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
