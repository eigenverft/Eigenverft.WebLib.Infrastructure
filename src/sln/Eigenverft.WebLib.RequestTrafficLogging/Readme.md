# Eigenverft.WebLib.RequestTrafficLogging

The `.slnx` and this readme live in this folder. Open a terminal here for the commands below. The CLI finds the one solution in this directory; you do not pass a `.slnx` or `.csproj` path. Other libraries keep their own `.slnx` under `src/sln/<name>/`, so `dotnet` does not ask you to specify a solution.

```text
./                         you are here (this readme + Eigenverft.WebLib.RequestTrafficLogging.slnx)
../../prj/Eigenverft.WebLib.RequestTrafficLogging/    packable class library
../../prj/Eigenverft.WebLib.RequestTrafficLogging.Tests/  tests (not packed)
```

Package metadata, license, icon, and release notes live in `src/prj/Eigenverft.WebLib.RequestTrafficLogging/NugetAssets/`.

`--tl:off` is optional. Without it the CLI shows the compact terminal logger. Add `--tl:off` for the classic per-project log. The commands work either way.

## Restore and build

```bash
dotnet restore
dotnet build
```

## Test

The test project explicitly allows target frameworks to run in parallel. Test results and vulnerability reports are isolated per target framework. No parallelism switch is needed on the command line:

```bash
dotnet test
```

MSTest is explicitly configured for method-level parallel execution within one test assembly. Tests must therefore not share mutable global state.

After a test run, the links below point to generated reports. Each selected target framework writes its own files (`net8.0`, `net10.0`, …).

[Test results (trx)](../../prj/Eigenverft.WebLib.RequestTrafficLogging.Tests/MSTestResults/Eigenverft.WebLib.RequestTrafficLogging.Tests-net10.0.trx)
[Test results (html)](../../prj/Eigenverft.WebLib.RequestTrafficLogging.Tests/MSTestResults/result-net10.0.html)

## Pack

```bash
dotnet pack
```

Creates one `.nupkg` in `src/prj/Eigenverft.WebLib.RequestTrafficLogging/bin/Pack/` containing the library for all selected target frameworks. Test and optional benchmark projects are not packed.

Optional: copy the package to a local feed by setting `LocalPackagesDir` in the library project, or:

```bash
dotnet pack -p:LocalPackagesDir="path/to/local/packages"
```

## Publish

```bash
dotnet publish
```

Writes library output to `src/prj/Eigenverft.WebLib.RequestTrafficLogging/bin/Publish/` for the highest selected target framework. This is a class library, not an executable.

## CI

Use `-m:1` for the build so a pipeline does not depend on machine load. It avoids occasional file locks when the library is built as a solution project and as a test `ProjectReference` at the same time. Multi-target test execution is already configured as parallel in the test project. Run these commands from this folder so each library has exactly one `.slnx` in the working directory.

```bash
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-build
dotnet pack
```
