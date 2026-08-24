---
_layout: landing
---

# {{appName}}

ASP.NET Core-specific hosting adapters built on `Eigenverft.NetLib.Infrastructure`, combining WebLib's web-host, Data Protection, and Kestrel/SNI integration with NetLib's shared infrastructure primitives.

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

WebLib provides the ASP.NET Core-specific layer:

- executable-rooted ASP.NET Core content and web-root setup;
- ASP.NET Core Data Protection adapters;
- Kestrel listener, TLS, SNI, and reload-safe certificate configuration.

The referenced NetLib package provides the shared host-independent layer used by these examples,
including directory layout, configuration-source composition and diagnostics, switchable JSON,
named configuration sets, configuration-value protection, bootstrap logging, and generic certificate
infrastructure.
