---
_layout: landing
---

# {{appName}}

Reusable ASP.NET Core hosting infrastructure for directory layout, configuration, runtime-switchable JSON, named configuration sets, protected settings, bootstrap logging, certificates, and Kestrel/SNI.

## Get started

Install the package and create an executable-rooted host:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory();
var app = builder.Build();
app.Run();
```

Continue with the [getting started guide](docs/getting-started.md), read the [introduction](docs/introduction.md), or browse the generated API reference.

## Core areas

- Executable-rooted directory layout and hosting defaults
- Configuration-source composition and precedence diagnostics
- Switchable JSON sources with prepare / commit / abort and last-known-good behavior
- Named configuration sets for coordinated multi-source switching and consistency reporting
- Composable JSON settings encoding and protection
- Early bootstrap logging with optional Serilog integration
- Self-signed and managed certificate helpers
- Kestrel listener, TLS, and SNI certificate configuration
