# Introduction

`Eigenverft.WebLib.Infrastructure` collects reusable ASP.NET Core hosting primitives that should not be duplicated across individual Eigenverft applications.

The library is intentionally application-neutral. It provides technical building blocks for hosting, configuration, protected settings, certificates, and Kestrel without introducing profile, tenant, proxy, filter, deployment, or product-specific semantics.

Major areas include:

- executable-rooted directory layout;
- configuration-source composition and resolution diagnostics;
- runtime-switchable JSON configuration;
- named configuration sets that coordinate multiple switchable JSON participants;
- composable JSON settings codecs and protection helpers;
- bootstrap logging before the final host logging pipeline exists;
- self-signed and managed certificate handling;
- Kestrel listener, TLS, and SNI configuration.

The package targets .NET 8 and .NET 10. Public APIs are documented with XML documentation, and the prepared CI/CD tooling can generate the API reference through DocFX. GitHub Actions publishing is intentionally not enabled until a `cicd.yml` workflow is added.
