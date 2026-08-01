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

The library targets `net8.0` and `net10.0`. A `net9.0` consumer uses the
compatible `net8.0` asset; preview target frameworks are intentionally
excluded.

The current package surface is limited to these hosting-directory primitives.

## Build and test

```powershell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

## License

MIT. See [LICENSE](LICENSE).
