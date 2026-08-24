# Getting started

Install the package:

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

Create a web host with the standard executable-rooted directory layout:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory();
var layout = builder.GetDirectoryLayout();

var app = builder.Build();
app.Run();
```

`Eigenverft.WebLib.Infrastructure` references `Eigenverft.NetLib.Infrastructure`, so the shared
host-independent NetLib primitives are available to WebLib consumers without installing an
additional infrastructure package. For example, add a runtime-switchable JSON source with the
NetLib configuration API:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
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

The switchable provider keeps its normal .NET configuration-provider position. Switching changes its
active source and snapshot, not its precedence in the provider stack. Switchable JSON, configuration
diagnostics, named configuration sets, configuration-value codecs, bootstrap logging, and generic
certificate helpers are provided by NetLib; WebLib builds its ASP.NET Core-specific hosting, Data
Protection, and Kestrel/SNI adapters on top of those shared primitives.

See the generated WebLib API reference for the ASP.NET Core-specific APIs and the
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)
repository for the shared infrastructure APIs.
