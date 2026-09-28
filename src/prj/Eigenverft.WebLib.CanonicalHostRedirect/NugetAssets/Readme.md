# Eigenverft.WebLib.CanonicalHostRedirect

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.CanonicalHostRedirect?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.CanonicalHostRedirect?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

A small ASP.NET Core middleware that combines canonical host and HTTPS normalization in one permanent redirect.

## ✨ At a glance

| Capability | What it does |
| --- | --- |
| AddCanonicalHostRedirect / UseCanonicalHostRedirect | Register configuration and add the redirect middleware. |
| CanonicalHostMode | Choose the apex or www form for the primary host and aliases. |
| Dependency | Uses Eigenverft.WebLib.Middleware.Primitives for isolated use-site options. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.CanonicalHostRedirect
```

## 🚀 Quick start

### Canonicalize host and scheme

```csharp
using Eigenverft.WebLib.CanonicalHostRedirect;
using Microsoft.AspNetCore.Builder;

builder.Services.AddCanonicalHostRedirect(options =>
    options.PrimaryApexHost = "example.com");

WebApplication app = builder.Build();
app.UseCanonicalHostRedirect();
app.Run();
```

## Package behavior

The normal defaults canonicalize the primary host group to www, require HTTPS, return 308 Permanent Redirect, and use implicit HTTPS port 443. Set RedirectFromHosts for additional host aliases or Canonicalization = CanonicalHostMode.ToApex to use the apex host. HttpsTargetPort may select one HTTPS port for every redirect; null and 443 omit the port. Incoming HTTP ports are never copied into the HTTPS target.

The redirect preserves PathBase, path, and query and combines host and scheme changes into one hop. A host that is not the primary host or a listed alias keeps its host name and is still redirected to HTTPS. Already-canonical HTTPS requests continue through the pipeline.

AddCanonicalHostRedirect() binds the CanonicalHostRedirect configuration section. The overload with an Action applies code configuration after binding. The UseCanonicalHostRedirect(Action<CanonicalHostRedirectOptions>) overload applies a local override to only that middleware use; configuration reload rebuilds the baseline and reapplies that override.

Place ASP.NET Core UseForwardedHeaders() before this middleware when a trusted reverse proxy supplies the client-facing host or scheme. PrimaryApexHost and RedirectFromHosts must not contain ports; configure HttpsTargetPort instead. Configured target ports must be between 1 and 65535.

## 🎯 Target frameworks

Targets .NET 8 and .NET 10. A .NET 9 application can consume the compatible net8.0 asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Package solution](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.CanonicalHostRedirect)
- [Package source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.CanonicalHostRedirect)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.CanonicalHostRedirect)
- [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

## 📄 License

Licensed under the MIT License by Eigenverft.
