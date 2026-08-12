# Release Notes

## 2026-08-12

- Replaced WebLib's duplicated generic directory-layout implementation with `Eigenverft.NetLib.Infrastructure`.
- Kept `WebApplicationBuilderFactory.CreateWithDefaultDirectory()` as the web-specific adapter while `DefaultDirectory`, layout validation, registration, and `GetDirectoryLayout()` now come from NetLib.
- Updated directory-layout tests and package documentation for the shared NetLib model.
- Moved the host-independent certificate primitives and their generic tests to `Eigenverft.NetLib.Infrastructure.Security.Certificates`; WebLib now consumes the released NetLib package from its Kestrel/SNI layer.
- Removed 1,091 lines of duplicated certificate implementation/test code while retaining the existing Kestrel certificate behavior.
- Moved generic reversible string transforms, Base92 encoding, machine binding, and DPAPI protection to `Eigenverft.NetLib.Infrastructure`; WebLib retains only the ASP.NET Core Data Protection adapter.
- Moved `BootstrapLogger` and its Serilog characterization tests to NetLib, removed WebLib's duplicate implementation and test-only Serilog dependencies, and updated WebLib to NetLib `1.0.20264.39599`.
