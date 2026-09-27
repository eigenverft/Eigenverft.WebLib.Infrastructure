# Eigenverft.WebLib.SerilogRelayReceiver

This folder is the per-library area under `src/sln/` for solution-level or cross-project files that should not sit next to a single `.csproj`.
The `.slnx` lives here; keep the folder while that is true. You can still add extra solution items here.

The `.slnx` and this readme live in this folder. Open a terminal here for the commands below. The CLI finds the one solution in this directory; you do not pass a `.slnx` or `.csproj` path. Other libraries keep their own `.slnx` under `src/sln/<name>/`, so `dotnet` does not ask you to specify a solution.

```text
./                         you are here (this readme + Eigenverft.WebLib.SerilogRelayReceiver.slnx)
../../prj/Eigenverft.WebLib.SerilogRelayReceiver/    packable class library
../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/  tests (not packed)
```

Package metadata, license, icon, and release notes live in `src/prj/Eigenverft.WebLib.SerilogRelayReceiver/Properties/NugetMetadata/`.


## Receiver shape

The library maps a POST endpoint through `MapSerilogRelayReceiver<THandler>(...)`.
Endpoint-specific options currently contain only:

- optional `BearerToken`;
- `MaximumBatchEvents`, default `100`.

Validated batches are forwarded to an application-provided `ISerilogRelayBatchHandler`. The
receiver is not tied to one `ApplicationId` and deliberately does not own database/backend or
retention policy.

Once a complete batch has passed authentication and validation, handler cancellation is tied to the host application's stopping token rather than the client request lifetime. The test project uses EF Core + SQLite only as a concrete durable reference; neither technology is part of the receiver package contract.

The functional suite also covers independent per-endpoint options, JSON subtype/charset handling, malformed and null event input, real client cancellation after durable handoff, expected host-shutdown cancellation, handler rollback, and repeat delivery. Global host `HttpJsonOptions` are also deliberately overridden in a regression test to verify that SerilogRelay wire deserialization remains protocol-owned and sender-compatible.


The current non-normative reasoning for durable acceptance, repeated delivery, endpoint/storage
topologies, HTTP coupling, handler diagnostics, and request cancellation is recorded in
[INITIAL-DESIGN-DIRECTION.md](INITIAL-DESIGN-DIRECTION.md). It is a working design note, not a
frozen protocol specification or release-requirement document.


`--tl:off` is optional. Without it the CLI shows the compact terminal logger. Add `--tl:off` for the classic per-project log. The commands work either way.

## Restore and build

```bash
dotnet restore
dotnet build
```

## Test

The test project explicitly allows target frameworks to run in parallel. Test results and other per-target-framework reports next to the test project are isolated. No parallelism switch is needed on the command line:

```bash
dotnet test
```

MSTest is explicitly configured for method-level parallel execution within one test assembly. Tests must therefore not share mutable global state.

After a test run, the links below point to generated reports. Each selected target framework writes its own files (`net8.0`, `net10.0`, …).

[Test results (trx)](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/MSTestResults/Eigenverft.WebLib.SerilogRelayReceiver.Tests-net10.0.trx)
[Test results (html)](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/MSTestResults/result-net10.0.html)
[Coverlet output](../../prj/Eigenverft.WebLib.SerilogRelayReceiver.Tests/CoverletOutput/coverage.net10.0.opencover.xml)

Coverlet measures only the class library (`[Eigenverft.WebLib.SerilogRelayReceiver]*`) and fails `dotnet test` if line, branch, or method coverage is under 100%.

## Pack

```bash
dotnet pack
```

Creates one `.nupkg` in `src/prj/Eigenverft.WebLib.SerilogRelayReceiver/bin/Pack/` containing the library for all selected target frameworks. Test and optional benchmark projects are not packed.

Restore fails this class library on high (`NU1903`) and critical (`NU1904`) vulnerable packages. Low and moderate stay warnings. `NugetReport` next to the tests lists that library's packages (txt/json) and is still info-only.

## Publish

The class library sets `IsPublishable` to `false`. Distribution is `dotnet pack`. To write output to `src/prj/Eigenverft.WebLib.SerilogRelayReceiver/bin/Publish/` for the default selected target framework, set `IsPublishable` to `true` and run. The default is defined in `src/prj/Eigenverft.WebLib.SerilogRelayReceiver/Properties/Build/SharedProject.props`:

```bash
dotnet publish
```

This is a class library, not an executable.

## CI

Use `-m:1` for the build so a pipeline does not depend on machine load. It avoids occasional file locks when the library is built as a solution project and as a test `ProjectReference` at the same time. Multi-target test execution is already configured as parallel in the test project. Run these commands from this folder so each library has exactly one `.slnx` in the working directory.

```bash
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-build
dotnet pack
```
