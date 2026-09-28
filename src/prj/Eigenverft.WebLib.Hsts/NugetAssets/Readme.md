# Eigenverft.WebLib.Hsts

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.Hsts?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.Hsts?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

A small registration and environment-aware activation wrapper around ASP.NET Core’s native HSTS middleware.

## ✨ At a glance

| Capability | What it does |
| --- | --- |
| AddWebLibHsts() | Registers native HSTS with a 180-day max-age baseline and binds Hsts configuration. |
| AddWebLibHsts(Action<HstsOptions>) | Applies code overrides after configuration binding. |
| UseWebLibHsts() | Uses native HSTS outside Development and is a no-op in Development. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.Hsts
```

## 🚀 Quick start

### Register and activate native HSTS

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

## Package behavior

The wrapper uses ASP.NET Core HstsOptions and native UseHsts behavior. Its max-age baseline is 180 days. Native defaults remain in effect for IncludeSubDomains, Preload, and excluded hosts unless you configure them.

Options bind from the Hsts configuration section. When using the Action overload, the callback runs after configuration binding, so code values take precedence. UseWebLibHsts() skips middleware in Development; in other environments the native middleware emits the header only for HTTPS requests and retains ASP.NET Core’s excluded-host behavior.

Place UseWebLibHsts() early enough to wrap responses that may short-circuit later, including health probes and rate-limit rejections.

## 🎯 Target frameworks

Targets .NET 8 and .NET 10. A .NET 9 application can consume the compatible net8.0 asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Package solution](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.Hsts)
- [Package source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.Hsts)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.Hsts)
- [MIT License](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

## 📄 License

Licensed under the MIT License by Eigenverft.
