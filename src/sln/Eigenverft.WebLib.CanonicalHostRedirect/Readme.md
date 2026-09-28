# Eigenverft.WebLib.CanonicalHostRedirect

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.CanonicalHostRedirect?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.CanonicalHostRedirect?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Combines canonical-host and HTTPS normalization in a single permanent redirect.

## Use

```csharp
using Eigenverft.WebLib.CanonicalHostRedirect;
using Microsoft.AspNetCore.Builder;

builder.Services.AddCanonicalHostRedirect(options =>
    options.PrimaryApexHost = "example.com");

WebApplication app = builder.Build();
app.UseCanonicalHostRedirect();
app.Run();
```

By default the primary host group canonicalizes to `www` and redirects to HTTPS with status 308. Add aliases through `RedirectFromHosts` or select apex mode with `Canonicalization`. Place trusted Forwarded Headers before this middleware when a proxy supplies the external host or scheme. Middleware.Primitives is restored transitively.

See the [package usage guide](../../prj/Eigenverft.WebLib.CanonicalHostRedirect/NugetAssets/Readme.md) for redirect targets and option behavior.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.CanonicalHostRedirect/` and `../../prj/Eigenverft.WebLib.CanonicalHostRedirect.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
