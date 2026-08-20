# Release Notes

## 1.0 public release — 2026-08-20

- Added the first public NuGet release of the ASP.NET Core-specific adapters built on `Eigenverft.NetLib.Infrastructure`.
- Updated the NetLib dependency to `1.0.20264.50794`, including the shared `ApplicationProtectionKeys` directory.
- Kept `WebApplicationBuilderFactory` focused on the ASP.NET Core content root, web root, and semantic `"Web"` directory projection.
- Initialized `WebRootPath` through `WebApplicationOptions` so clean deployments do not depend on a pre-existing `wwwroot` directory.
- Included the ASP.NET Core Data Protection transform adapter and configuration-driven Kestrel/SNI certificate handling.
- Added a Data Protection configuration-value codec factory that derives the standard key-ring path and application assembly identity.
- Published dedicated `net8.0` and `net10.0` assets.
- Adopted the shared Eigenverft CI/CD, DocFX, reporting, artifact-distribution, and deployment-channel documentation workflow.

## Pre-release consolidation — 2026-08-12

- Replaced WebLib's duplicated generic directory-layout implementation with `Eigenverft.NetLib.Infrastructure`.
- Kept `WebApplicationBuilderFactory.CreateWithDefaultDirectory()` as the web-specific adapter while `DefaultDirectory`, layout validation, registration, and `GetDirectoryLayout()` now come from NetLib.
- Updated directory-layout tests and package documentation for the shared NetLib model.
- Moved the host-independent certificate primitives and their generic tests to `Eigenverft.NetLib.Infrastructure.Security.Certificates`; WebLib now consumes the released NetLib package from its Kestrel/SNI layer.
- Removed 1,091 lines of duplicated certificate implementation/test code while retaining the existing Kestrel certificate behavior.
- Moved generic reversible string transforms, Base92 encoding, machine binding, and DPAPI protection to `Eigenverft.NetLib.Infrastructure`; WebLib retains only the ASP.NET Core Data Protection adapter.
- Moved `BootstrapLogger` and its Serilog characterization tests to NetLib, removed WebLib's duplicate implementation and test-only Serilog dependencies, and updated WebLib to NetLib `1.0.20264.39599`.
- Moved `ConfigurationSets`, `SwitchableJson`, JSON source preparation, configuration-value codecs, source-reset helpers, and configuration diagnostics to NetLib `1.0.20264.39698` and removed their WebLib duplicates.
- Removed the legacy WebLib `JsonSettings` environment/encoder facade instead of carrying it forward; WebLib now contains only ASP.NET Core-specific directory, Data Protection, and Kestrel/SNI adapters.
