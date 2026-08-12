# Release Notes

## 2026-08-12

- Replaced WebLib's duplicated generic directory-layout implementation with `Eigenverft.NetLib.Infrastructure`.
- Kept `WebApplicationBuilderFactory.CreateWithDefaultDirectory()` as the web-specific adapter while `DefaultDirectory`, layout validation, registration, and `GetDirectoryLayout()` now come from NetLib.
- Updated directory-layout tests and package documentation for the shared NetLib model.
- Moved the host-independent certificate primitives and their generic tests to `Eigenverft.NetLib.Infrastructure.Security.Certificates`; WebLib now consumes the released NetLib package from its Kestrel/SNI layer.
- Removed 1,091 lines of duplicated certificate implementation/test code while retaining the existing Kestrel certificate behavior.
