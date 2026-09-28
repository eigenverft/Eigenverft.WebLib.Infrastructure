# Eigenverft.WebLib.Kestrel.Sni

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Kestrel.Sni?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Kestrel.Sni?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Configures startup-fixed Kestrel listeners and SNI-based certificate selection from application configuration.

## Use

```csharp
using Eigenverft.WebLib.Kestrel.Sni;

builder.WebHost.ConfigureKestrelSniFromConfiguration();
```

Provide the `KestrelSettings` section and one or more `CertificatesMappingSettings` mappings before building the host. Listener settings require a restart; certificate mappings can reload when their configuration source supports reload, and a failed reload keeps the last-known-good certificates. Store PFX passwords in protected configuration. NetLib Security.Certificates is a transitive dependency.

See the [package usage guide](../../prj/Eigenverft.WebLib.Kestrel.Sni/NugetAssets/Readme.md) for configuration and recovery behavior.

## Development

This directory contains the solution; the library and tests are under `../../prj/Eigenverft.WebLib.Kestrel.Sni/` and `../../prj/Eigenverft.WebLib.Kestrel.Sni.Tests/`.

```bash
dotnet restore
dotnet build
dotnet test
```
