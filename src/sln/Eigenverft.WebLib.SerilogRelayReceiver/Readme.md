# Eigenverft.WebLib.SerilogRelayReceiver

Public solution entry point for the SerilogRelay receiver package.

`Eigenverft.WebLib.SerilogRelayReceiver` provides ASP.NET Core ingestion for
`Eigenverft.NetLib.SerilogRelay` batches with:

- protocol validation and endpoint-scoped bearer authentication;
- provider-neutral Entity Framework Core persistence;
- repeat-delivery-safe physical event storage;
- custom `ISerilogRelayBatchHandler` support for queue-backed, multi-backend, or non-EF processing;
- support for multiple applications per endpoint and independent options per mapped endpoint.

## Consumer documentation

The package README is the authoritative usage guide:

[NuGet package README](../../prj/Eigenverft.WebLib.SerilogRelayReceiver/Properties/NugetMetadata/Readme.md)

It covers installation, supported target frameworks, EF Core setup, provider ownership, migrations,
storage topology, HTTP behavior, durable acceptance semantics, and the custom-handler path.

## Architecture

The stable architectural decisions behind the receiver are summarized in:

[ARCHITECTURE.md](ARCHITECTURE.md)

## Projects

- `Eigenverft.WebLib.SerilogRelayReceiver`: packable receiver library.
- `Eigenverft.WebLib.SerilogRelayReceiver.Tests`: protocol, persistence, compatibility, and release-contract tests.

The package targets `net8.0` and `net10.0`.
