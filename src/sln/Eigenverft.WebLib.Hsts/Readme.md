# Eigenverft.WebLib.Hsts

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hsts?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hsts?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

An environment-aware registration and activation wrapper around ASP.NET Core's native HSTS middleware.

## Use

```csharp
using System;
using Eigenverft.WebLib.Hsts;
using Microsoft.AspNetCore.Builder;

builder.Services.AddWebLibHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(180);
});

WebApplication app = builder.Build();
app.UseWebLibHsts();
app.Run();
```

The wrapper uses a 180-day max-age baseline and skips HSTS in Development. Native ASP.NET Core defaults for included subdomains, preload, and excluded hosts remain in effect unless configured. Place it early enough to cover short-circuit responses.

See the [package usage guide](../../prj/Eigenverft.WebLib.Hsts/NugetAssets/Readme.md) for configuration precedence and activation behavior.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.Hsts/` and `../../prj/Eigenverft.WebLib.Hsts.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
