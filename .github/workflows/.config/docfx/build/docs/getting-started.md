# Getting started

Install the package:

```shell
dotnet add package Eigenverft.WebLib.Infrastructure
```

Create a web host with the standard executable-rooted directory layout:

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory();
var layout = builder.GetDirectoryLayout();

var app = builder.Build();
app.Run();
```

To add a runtime-switchable JSON source:

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

The switchable provider keeps its normal .NET configuration-provider position. Switching changes its active source and snapshot, not its precedence in the provider stack.

See the generated API reference for configuration diagnostics, named configuration sets, JSON settings codecs, bootstrap logging, certificate helpers, and Kestrel/SNI configuration.
