# Introduction

`Eigenverft.WebLib.Infrastructure` provides ASP.NET Core-specific adapters built on
[`Eigenverft.NetLib.Infrastructure`](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure).
NetLib contains the application-neutral infrastructure and can be used independently of ASP.NET
Core. WebLib references NetLib, so WebLib consumers can use those shared primitives directly without
installing an additional infrastructure package.

WebLib itself stays focused on ASP.NET Core integration:

- executable-rooted ASP.NET Core content and web-root setup on top of NetLib's directory layout;
- ASP.NET Core Data Protection adapters for NetLib's composable configuration-value protection;
- Kestrel listener, TLS, SNI, and reload-safe certificate configuration built on NetLib certificate primitives.

Common supporting capabilities are provided by the referenced NetLib package and are intentionally
used in WebLib examples where they form part of a complete hosting setup. These include
configuration-source composition and diagnostics, runtime-switchable JSON configuration, named
configuration sets, configuration-value codecs and protection helpers, bootstrap logging, and the
generic certificate infrastructure.

Both libraries are application-neutral and avoid profile, tenant, proxy, filter, deployment, or
product-specific semantics. The WebLib package targets .NET 8 and .NET 10. Public WebLib APIs are
documented with XML documentation, and the repository CI/CD workflow generates the DocFX API
reference and build reports.
