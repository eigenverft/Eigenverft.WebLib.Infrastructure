# Eigenverft.WebLib.ClientNetwork

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.ClientNetwork?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.ClientNetwork?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

Collect normalized remote-peer and forwarded-IP observations for ASP.NET Core requests. It reports network facts; it does not decide which proxy or forwarded address is trusted.

## ✨ At a glance

| Capability | Details |
| --- | --- |
| Middleware | `app.UseClientNetworkFeature()` populates `IClientNetworkFeature` once per request. |
| Forwarded headers | Parses `Forwarded` and `X-Forwarded-For`, retaining source, raw token, parsed address, and malformed status. |
| Dependencies | Uses `Eigenverft.NetLib.Networking` and `Eigenverft.WebLib.Middleware.Primitives`; both are restored transitively. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.ClientNetwork
```

## 🚀 Quick start

```csharp
using Eigenverft.WebLib.ClientNetwork;

WebApplication app = builder.Build();
app.UseClientNetworkFeature();
```

Read `context.Features.Get<IClientNetworkFeature>()` downstream. The feature preserves malformed forwarded tokens and does not establish proxy trust.

## Feature behavior and ordering

`IClientNetworkFeature` exposes `RemoteIpAddress` (the normalized `HttpContext.Connection.RemoteIpAddress` at this middleware position), `ForwardedIpChain`, and `HasMalformedForwardedIpInformation`. Missing peer IP throws `InvalidOperationException`. Each chain item exposes `Source`, `RawValue`, optional `Address`, and `IsMalformed`. The chain collects `for=` entries from `Forwarded` first, then `X-Forwarded-For`; order is preserved within each source. Parsed IP endpoints are normalized, including bracketed IPv6 with a port; IPv4-mapped IPv6 becomes IPv4. Malformed tokens are retained with no parsed address and set the malformed flags.

Treat forwarded values as untrusted until your application applies its trusted-proxy policy. Place this middleware before Forwarded Headers to record the direct peer, or after trusted Forwarded Headers to record its rewritten address. `Eigenverft.WebLib.RequestTrafficLogging` uses this feature for forwarded-IP log fields when enabled; the NuGet dependency is transitive.

## 🎯 Target frameworks

Targets `net8.0` and `net10.0`; .NET 9 can consume the compatible `net8.0` asset.

## 🔗 Project links

- [Repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Solution and tests](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.ClientNetwork)
- [Project source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.ClientNetwork)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.ClientNetwork)

## 📄 License

MIT; see the [repository license](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE).
