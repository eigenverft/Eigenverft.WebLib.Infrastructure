# Eigenverft.WebLib.SerilogRelayReceiver

Maintainer documentation for the SerilogRelay receiver package and its test suite.

The package targets `net8.0` and `net10.0`, provides the HTTP receiver contract, and includes a provider-neutral Entity Framework Core persistence path. Concrete EF Core providers remain host-owned.

## Repository layout

```text
src/sln/Eigenverft.WebLib.SerilogRelayReceiver/
  Eigenverft.WebLib.SerilogRelayReceiver.slnx
  Readme.md
  INITIAL-DESIGN-DIRECTION.md

src/prj/Eigenverft.WebLib.SerilogRelayReceiver/
  Eigenverft.WebLib.SerilogRelayReceiver.csproj
  Properties/version.json
  Properties/NugetMetadata/

src/prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/
  Eigenverft.WebLib.SerilogRelayReceiver.Tests.csproj
```

Consumer-facing package metadata lives under `Properties/NugetMetadata/`:

- `Readme.md`: NuGet package README;
- `PackageReleaseNotes.txt`: NuGet release notes;
- `Icon-128x128.png`: package icon.

The package version is defined in `Properties/version.json` using Nerdbank.GitVersioning. The 1.0 release line is `1.0.0`; public releases are produced from `main`.

The design rationale is recorded in [INITIAL-DESIGN-DIRECTION.md](INITIAL-DESIGN-DIRECTION.md). It is non-normative; the public API, package README, release notes, and tests define the shipped contract.

## 1.0 receiver contract

The receiver supports two persistence paths:

- built-in EF Core persistence through `AddSerilogRelayReceiverEntityFrameworkCore<TDbContext>()` and `MapSerilogRelayReceiverEntityFrameworkCore<TDbContext>()`;
- custom `ISerilogRelayBatchHandler` implementations for queue, multi-backend, or non-EF scenarios.

The built-in path requires a host-registered `IDbContextFactory<TDbContext>`. The handler creates one isolated DbContext per accepted batch.

`ConfigureSerilogRelayReceiver()` adds `SerilogRelayReceivedEvent` to the host model. `EventId` is indexed but not unique, so repeat delivery remains a physical receive instead of becoming a conflict.

A receiver endpoint may contain events from multiple applications. Endpoint options are scoped per mapping and currently contain only bearer-token authentication and the maximum batch event count.

## Prerequisites

For the full local test matrix, install runtimes capable of executing both target frameworks:

- .NET 8 runtime;
- .NET 10 runtime.

A newer SDK can compile both target frameworks, but executing a `net8.0` test assembly still requires an appropriate .NET 8 runtime.

Run commands from this solution directory.

## Restore and build

```bash
dotnet restore
dotnet build -c Release --no-restore -m:1
```

`-m:1` avoids unnecessary concurrent builds of the library through both the solution and the test-project reference.

## Test

Full matrix:

```bash
dotnet test -c Release --no-build
```

Individual framework runs:

```bash
dotnet test -c Release --no-build -f net8.0
dotnet test -c Release --no-build -f net10.0
```

The test project isolates generated reports per target framework and allows TFM-level parallel execution.

Coverage measures only `Eigenverft.WebLib.SerilogRelayReceiver` and fails the test run if total line, branch, or method coverage falls below 100%.

The permanent `ReleaseContractTests` protect the 1.0 contract by freezing:

- exported public types;
- public entry-point and handler signatures;
- public batch/event/persistence member shapes;
- EF Core table, key, nullability, length, and index metadata;
- provider neutrality of the product assembly;
- relational schema generation through a host-selected provider.

An intentional breaking change must update those tests explicitly.

Generated outputs:

- [net10 TRX](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/MSTestResults/Eigenverft.WebLib.SerilogRelayReceiver.Tests-net10.0.trx)
- [net10 HTML](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/MSTestResults/result-net10.0.html)
- [net10 coverage](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/CoverletOutput/coverage.net10.0.opencover.xml)

The same directories contain per-TFM outputs when the corresponding framework is executed.

## Dependency and vulnerability reports

Building the test project generates package inventory and vulnerability reports for the packable receiver project under:

```text
src/prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/NugetReport/
```

Restore treats high (`NU1903`) and critical (`NU1904`) vulnerable packages as errors. Low and moderate findings remain warnings. The generated reports are diagnostic output and do not replace restore-time enforcement.

## Pack

```bash
dotnet pack -c Release --no-build
```

The package is written to:

```text
src/prj/Eigenverft.WebLib.SerilogRelayReceiver/bin/Pack/
```

The package must contain both supported TFMs, the NuGet README/icon/release notes metadata, and only provider-neutral EF Core product dependencies. Concrete database provider packages belong to consuming hosts.

`EnablePackageValidation` is enabled. After the first stable package is published, a package-validation baseline can be set deliberately for future compatibility checks.

## Release readiness

Before promoting the package to a stable 1.0 release:

1. restore succeeds without high/critical vulnerability errors;
2. Release build succeeds for `net8.0` and `net10.0` with no warnings/errors;
3. tests pass for both target frameworks;
4. 100% line/branch/method coverage remains satisfied;
5. `ReleaseContractTests` pass unchanged unless a contract change is intentional;
6. `dotnet pack -c Release --no-build` succeeds;
7. inspect the generated `.nuspec`/package to confirm EF Core is present but no concrete database provider is pulled into the product;
8. verify the package README and release notes describe the same public behavior as the code;
9. promote through the repository's normal branch/release flow.

This project is a packable class library. Its distribution artifact is the NuGet package; application deployment/publishing belongs to consuming hosts.
