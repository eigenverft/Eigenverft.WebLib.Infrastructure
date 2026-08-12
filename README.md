# Eigenverft.WebLib.Infrastructure

Shared ASP.NET Core hosting infrastructure for self-contained Eigenverft web
applications.

## Current scope

Generic application infrastructure lives in `Eigenverft.NetLib.Infrastructure`.
WebLib intentionally adds only ASP.NET Core-specific adapters on top of that shared
foundation.

The current WebLib production surface is deliberately small:

- `WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates an ASP.NET
  Core `WebApplicationBuilder` while reusing NetLib's executable-rooted directory
  layout, validation, writable probes, and DI registration. WebLib adds only the
  web-specific `ContentRootPath`, `WebRootPath`, and semantic `"Web"` layout entry.
- `AspNetDataProtectionStringTransforms` adapts ASP.NET Core Data Protection to
  NetLib's `ReversibleStringTransform` model. Generic transforms and persisted
  configuration-value codecs remain in NetLib.
- `ConfigureKestrelSniFromConfiguration(...)` and the Kestrel/SNI runtime state are
  WebLib-specific because they depend directly on ASP.NET Core and Kestrel. Shared
  certificate creation/loading primitives come from NetLib.

Generic configuration infrastructure is provided by NetLib, including
`ConfigurationSets`, `SwitchableJson`, JSON source preparation,
`ConfigurationValueCodec`, configuration-source reset helpers, and configuration
precedence diagnostics. Bootstrap logging, certificates, machine binding, Base92,
and reversible transforms also live in NetLib.

The former WebLib `JsonSettings` environment/encoder facade, including
`EncodeAndAddEnvironmentJsonSettings(...)`, has been removed. New code should use
NetLib's `ConfigurationSets` + `SwitchableJson` model instead of relying on a WebLib
compatibility facade.

Typical web-host setup remains intentionally small:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory();
var directories = builder.GetDirectoryLayout();

string settingsDirectory =
    directories[DefaultDirectory.ApplicationSettings];
```
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

## Release preparation

NuGet packaging, DocFX, package-health checks, and the reusable CI/CD PowerShell support are prepared under `.github/workflows/`. The repository intentionally does **not** contain `.github/workflows/cicd.yml` yet, so no GitHub Actions release workflow is enabled by this preparation alone.

## License

MIT. See [LICENSE](LICENSE).
