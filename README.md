# Eigenverft.WebLib.Infrastructure

ASP.NET Core adapters built on
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).
Host-independent infrastructure belongs to NetLib; WebLib contains only behavior that
depends directly on ASP.NET Core or Kestrel.

## Web-specific scope

### Web application directory integration

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
string protectionKeysDirectory =
    directories[DefaultDirectory.ApplicationProtectionKeys];
```

### ASP.NET Core Data Protection adapter

`AspNetDataProtectionStringTransforms.DataProtection(...)` adapts an ASP.NET Core
`IDataProtectionProvider` to NetLib's `ReversibleStringTransform` abstraction while keeping
the application name, purpose, and persistent key-ring directory explicit.

### Kestrel and SNI configuration

`ConfigureKestrelSniFromConfiguration(...)` configures the ASP.NET Core server layer,
including HTTP/HTTPS listeners, protocol and TLS policy, SNI certificate selection, and
certificate refresh behavior. Shared certificate primitives remain in NetLib.

## Installation

```powershell
dotnet add package Eigenverft.WebLib.Infrastructure
```

The package references `Eigenverft.NetLib.Infrastructure` as its shared foundation.

## Build and test

```powershell
dotnet build src/Eigenverft.WebLib.Infrastructure.slnx
dotnet test src/Eigenverft.WebLib.Infrastructure.slnx
```

## CI/CD and documentation

The repository uses the same reusable CI/CD workflow as NetLib for build, test, packaging,
reports, DocFX generation, artifact distribution, and deployment-channel documentation.

## License

MIT. See [LICENSE](LICENSE).
