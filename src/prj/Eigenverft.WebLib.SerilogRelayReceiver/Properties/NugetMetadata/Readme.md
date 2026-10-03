# Eigenverft.WebLib.SerilogRelayReceiver

ASP.NET Core receiver for [`Eigenverft.NetLib.SerilogRelay`](https://www.nuget.org/packages/Eigenverft.NetLib.SerilogRelay) batches.

The package provides:

- HTTP ingestion and protocol validation for SerilogRelay batches;
- optional endpoint-scoped bearer-token authentication;
- built-in durable persistence through Entity Framework Core;
- provider-neutral storage integration: the host chooses SQLite, SQL Server, PostgreSQL/Npgsql, or another compatible EF Core provider;
- a custom `ISerilogRelayBatchHandler` path for queue, multi-backend, or non-EF scenarios.

## Supported frameworks

- .NET 8 (`net8.0`)
- .NET 10 (`net10.0`)

The package references the matching major version of `Microsoft.EntityFrameworkCore`. It does not reference a concrete database provider.

## Install

```bash
dotnet add package Eigenverft.WebLib.SerilogRelayReceiver
```

Add the EF Core provider package selected by the host separately.

## EF Core quick start

Define a host-owned DbContext and include the receiver model:

```csharp
public sealed class LoggingDbContext : DbContext
{
    public LoggingDbContext(DbContextOptions<LoggingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureSerilogRelayReceiver();
    }
}
```

Register an `IDbContextFactory<TDbContext>` with the provider selected by the host. SQLite is shown only as an example:

```csharp
builder.Services.AddDbContextFactory<LoggingDbContext>(
    options => options.UseSqlite(
        builder.Configuration.GetConnectionString("Logging")));

builder.Services
    .AddSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>();
```

Map the receiver endpoint:

```csharp
app.MapSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>(
    "/api/v1/logs",
    options =>
    {
        options.BearerToken =
            builder.Configuration["SerilogRelay:BearerToken"];
    });
```

The built-in handler creates and disposes one isolated DbContext per accepted batch. Its `SaveChangesAsync` therefore cannot accidentally persist unrelated tracked changes from another request scope.

The built-in EF Core handler fits bounded metadata to its supplied model before saving: batch/event timestamps and `TraceId` are limited to 64 UTF-16 code units, `MachineId` to 256, and `Level`/`SpanId` to 32. Oversized values retain their prefix without splitting a Unicode surrogate pair; `null` and empty strings remain distinct. This is the EF handler's storage policy. Custom handlers receive the original validated values and choose their own processing policy. `ApplicationId`, `ApplicationVersion`, event identifiers, and unbounded payload text retain their existing validation and storage semantics.

Changing the host's `AddDbContextFactory` provider configuration is enough to use another EF Core provider. Provider packages, connection strings, migrations, retention, and database lifecycle remain host-owned.

## EF Core model and migrations

`ConfigureSerilogRelayReceiver()` adds the public `SerilogRelayReceivedEvent` entity to the host model.

For relational providers the default table name is:

```text
SerilogRelayReceivedEvents
```

Important model semantics:

- `ReceiveId` is the receiver-local generated primary key;
- `EventId` is indexed but deliberately **not unique**;
- repeated delivery therefore creates another physical receive instead of being rejected or silently discarded;
- the model also indexes `BatchId`, `ReceivedAtUtc`, and `(ApplicationId, ReceivedAtUtc)`.
- `ApplicationVersion` stores the event creator's version, allows `NULL` for older senders, and has a 255 UTF-16 code unit maximum. A newer sender draining an older shared-spool entry does not replace its originating version.
- payload text fields preserve `null` and empty strings as distinct values; `BatchTimestamp`, `Timestamp`, `Level`, `RenderMessage`, and `MessageTemplate` are nullable, as are the optional machine, tracing, exception, and property fields.

Application identity and version have a shared 255 UTF-16 code unit wire/storage limit.
The current sender normalizes configured application IDs to ASCII. IDs longer than 255
characters become `_` + the first 189 normalized characters + `_` + the complete normalized
ID's lowercase SHA-256 hash (64 hexadecimal characters). That effective ID is recorded on
events and used by its default spool directory. Application versions retain at most 255
UTF-16 code units without splitting a surrogate pair. Both values are resolved at startup.

The receiver validates the received values and does not normalize, shorten, or recompute
either field, including when a newer process forwards an older event. Coordinate sender
and receiver upgrades: older senders and existing spool rows can still contain 256-character
values, which this receiver rejects with HTTP 400. Historical rows are not rewritten.

The receiver does not ship provider-specific migrations. Because the entity is part of the host's DbContext model, the host's normal EF Core migration workflow owns schema creation and upgrades. Existing receiver databases need the nullable `ApplicationVersion` column before using this model; `EnsureCreated` does not upgrade an existing schema.

Existing databases created with the earlier non-nullable model need a host-owned migration allowing `NULL` in `BatchTimestamp`, `Timestamp`, `Level`, `RenderMessage`, and `MessageTemplate`. Updating the package or calling `EnsureCreated()` does not change an existing table.

For temporary/test databases, `EnsureCreated()` can be useful. Production databases that use migrations should use the host's normal migration workflow instead of mixing migrations with `EnsureCreated()`.

## Sender and receiver responsibilities

This package is a matching receiver implementation, not a required peer of `Eigenverft.NetLib.SerilogRelay`. Either side can be implemented independently against the wire format and HTTP acknowledgment contract.

The sender handles bounded local buffering, outage retries, backlog delivery during normal operation, and bounded shutdown delivery. Only a completed HTTP 204 No Content response allows it to release the acknowledged events locally; every other status, including 200 and 201, leaves the events pending. It does not require a receiver-side persistence receipt or negotiate storage and payload limits.

The receiver owns its acceptance policy. The built-in EF Core handler saves every validated event before returning successfully. A custom handler or independently implemented receiver may forward, filter, or deliberately discard an event and still acknowledge it, for example when its policy excludes oversized events. Such acknowledgment is an intentional receiver decision; the sender will not retry those events. Every response other than a completed HTTP 204 keeps them pending for the sender's retry policy.

The sender's configurable 4 MiB batch target does not impose a limit on this receiver. A single event above that target can arrive in its own batch. Host/proxy request limits and receiver acceptance policy remain host concerns.

## Durable acceptance semantics

The built-in EF Core handler:

- creates a fresh host-configured DbContext for each accepted batch;
- maps every validated event to one `SerilogRelayReceivedEvent`;
- preserves event identity and originating version, fitting bounded metadata to the supplied model;
- adds receiver-side `ReceivedAtUtc`;
- adds the complete batch to the DbContext;
- calls `SaveChangesAsync` once;
- returns successfully only after that save completes.

For normal relational EF Core providers, the provider/EF Core transaction semantics protect one `SaveChanges` operation. The receiver test suite verifies with SQLite that a failure while persisting a later event leaves no rows from the batch.

A production provider must offer durability and atomicity appropriate for the endpoint contract. Provider-specific behavior remains the host's responsibility.

## Endpoint and storage topology

An endpoint is not tied to one `ApplicationId`. A valid batch may contain events from multiple applications, machines, and processes.

The built-in EF Core integration directly supports:

| Topology | Configuration |
| --- | --- |
| 1 endpoint → 1 storage | one mapped EF Core endpoint and one DbContext |
| n endpoints → 1 storage | multiple mappings using the same DbContext |
| n endpoints → n storages | mappings using different DbContext types/providers |

A single endpoint can target multiple durable backends through a custom `ISerilogRelayBatchHandler`. The receiver does not impose a distributed transaction protocol for that case.

Application-based storage routing, application allow-lists, and storage partitioning are not part of the 1.0 API.

## Request ownership and cancellation

Before a complete batch has been received and validated, request processing uses `HttpContext.RequestAborted`.

After validation, durable handling receives the host application's stopping token instead of the client request-abort token. Consequently:

- a client disconnect after durable handoff does not by itself cancel persistence;
- host shutdown can cancel in-flight durable work;
- host-shutdown cancellation returns `503 Service Unavailable`;
- successful handler completion returns `204 No Content`.

## Endpoint options

`SerilogRelayReceiverOptions` intentionally contains only:

- `BearerToken`: optional exact bearer token; null/empty/whitespace disables token validation;
- `MaximumBatchEvents`: maximum accepted event count, default `256`.

Options belong to each mapped endpoint, so different endpoints can use different authentication and limits.

The receiver's 256-event default accepts both matching sender defaults: up to 100 events per spool batch and 256 per direct emergency RAM batch, including during shutdown. No batch-limit override is required for the default pair. If either sender maximum changes, keep the receiver maximum at least as large as both sender maxima. These count limits are independent from the sender's JSON byte target and host/proxy request-size limits.

## Wire protocol behavior

The receiver validates:

- protocol version `1`;
- canonical GUID `BatchId`;
- `Count` matching the number of events;
- `MaximumBatchEvents`;
- no `null` entries in `Logs`;
- canonical GUID `EventId`;
- non-empty `ApplicationId` of at most 255 UTF-16 code units;
- optional `ApplicationVersion` containing 1 to 255 UTF-16 code units when supplied;
- positive `ProcessId`.

SerilogRelay wire JSON is deserialized with receiver-owned ASP.NET Web-compatible JSON settings. Global host `HttpJsonOptions` therefore cannot silently change the relay protocol's naming or case behavior.

HTTP results:

- `204`: the selected handler completed its acceptance policy;
- `400`: malformed or protocol-invalid payload;
- `401`: bearer authentication failed;
- `415`: unsupported JSON media type or charset;
- `503`: durable handling was cancelled because the host is stopping;
- other `5xx`: unexpected receiver, handler, or storage failure.

## Custom handler path

Applications that need queue-backed acceptance, multiple durable backends, or non-EF processing can supply their own handler:

```csharp
builder.Services.AddSerilogRelayReceiver<MyRelayBatchHandler>();

app.MapSerilogRelayReceiver<MyRelayBatchHandler>(
    "/api/v1/logs");
```

A custom handler must complete its chosen acceptance policy before returning successfully. If that policy promises durable storage or queue handoff, it must fulfill that promise before acknowledgment. Intentional filtering or discarding also completes acceptance and allows the sender to release those events.

## Not part of the 1.0 contract

The receiver intentionally does not define:

- request-body byte limits;
- application allow-lists or application-based storage routing;
- retention/cleanup policy;
- duplicate-query or deduplicated-view behavior;
- provider-specific migrations;
- multi-backend composition helpers;
- richer acknowledgement payloads.
