# Release Notes

## 2026-08-12

- Replaced WebLib's duplicated generic directory-layout implementation with `Eigenverft.NetLib.Infrastructure`.
- Kept `WebApplicationBuilderFactory.CreateWithDefaultDirectory()` as the web-specific adapter while `DefaultDirectory`, layout validation, registration, and `GetDirectoryLayout()` now come from NetLib.
- Updated directory-layout tests and package documentation for the shared NetLib model.
