# Eigenverft.WebLib.Infrastructure

Modern ASP.NET Core library for reusable Eigenverft web-infrastructure
contracts and orchestration primitives.

The initial product boundary follows the infrastructure described by
`Eigenverft.Web.EdgeReverseProxy`: deploy a new component version into the
inactive blue-green slot, validate it, activate it, and retain the previous
slot for a fast rollback.

## Intended responsibilities

- expose shared ASP.NET Core health and infrastructure endpoints;
- provide an executable-rooted, writable application directory layout for self-contained hosts;
- model blue and green deployment slots and their state;
- inventory installed and active component versions;
- stage releases into an inactive slot without overwriting a running version;
- start, stop, and observe component instances through explicit adapters;
- run bounded health and routing probes before activation;
- switch the active slot and preserve rollback information;
- expose deterministic operations that a narrow control plane can authorize.

## Boundaries

This library provides reusable web and hosting contracts. Concrete MCP tools,
product-specific endpoints, authentication policy, and user-facing
administration remain in their respective host projects.

The core contract also avoids a generic remote shell. Platform-specific
filesystem, process, service-manager, and routing integrations should be
provided through narrow adapters so the deployment state machine remains
independently testable.

## Self-contained host directories

`WebApplicationBuilderFactory.CreateWithDefaultDirectory(...)` creates a host rooted at
`AppContext.BaseDirectory`, assigns `ContentRootPath` and `WebRootPath`, creates the
configured direct-child folders, verifies that they are writable, and registers the
resolved `AppDirectoryLayout` for access before and after `Build()`.

The library targets `net8.0` and `net10.0`. A `net9.0` consumer uses the compatible
`net8.0` asset; preview target frameworks are intentionally excluded.

## Infrastructure role

```text
Eigenverft.Web.ControlPlaneMcp
               │
               ▼
Eigenverft.WebLib.Infrastructure
               │
               ├── deployment storage adapter
               ├── process or service adapter
               ├── health probe adapter
               └── activation and routing adapter
```

`Eigenverft.Web.EdgeReverseProxy` remains the stable public entry point. It is
not coupled to the control-plane transport or to the implementation details of
this library.

## Build and test

```powershell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

The initial scaffold establishes the product identity and the fundamental slot
invariant. It also provides `MapInfrastructureLiveness`, the common
process-liveness endpoint used by infrastructure hosts. Deployment
orchestration will be added as the control-plane contracts are implemented.

## License

MIT. See [LICENSE](LICENSE).
