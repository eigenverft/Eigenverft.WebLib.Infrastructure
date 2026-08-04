# Eigenverft.WebLib.Infrastructure

Shared ASP.NET Core hosting infrastructure for self-contained Eigenverft web
applications.

## Current scope

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates a host
rooted at `AppContext.BaseDirectory`, assigns `ContentRootPath` and
`WebRootPath`, creates the configured direct-child folders, verifies that they
are writable, and registers the resolved `AppDirectoryLayout` before and after
`Build()`.

`DefaultDirectory` provides typed keys and conventional names for application
logs, data, certificates, settings, and static web content. Callers can retain
the defaults or provide explicit overrides.

`ResetToMinimalConfigurationSources(...)` from the
`Eigenverft.WebLib.Infrastructure.Hosting.Configuration` namespace replaces the
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

The library targets `net8.0` and `net10.0`. A `net9.0` consumer uses the
compatible `net8.0` asset; preview target frameworks are intentionally
excluded.

The current package surface is limited to these hosting-directory,
configuration-source, and JSON-settings primitives.

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
