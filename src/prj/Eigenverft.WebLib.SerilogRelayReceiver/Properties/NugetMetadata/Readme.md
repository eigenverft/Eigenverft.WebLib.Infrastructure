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

        options.MaximumBatchEvents = 100;
    });
```

The built-in handler creates and disposes one isolated DbContext per accepted batch. Its `SaveChangesAsync` therefore cannot accidentally persist unrelated tracked changes from another request scope.

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

The receiver does not ship provider-specific migrations. Because the entity is part of the host's DbContext model, the host's normal EF Core migration workflow owns schema creation and upgrades.

For temporary/test databases, `EnsureCreated()` can be useful. Production databases that use migrations should use the host's normal migration workflow instead of mixing migrations with `EnsureCreated()`.

## Durable acceptance semantics

The built-in EF Core handler:

- creates a fresh host-configured DbContext for each accepted batch;
- maps every validated event to one `SerilogRelayReceivedEvent`;
- preserves the current batch and event metadata;
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
- successful durable completion returns `204 No Content`.

## Endpoint options

`SerilogRelayReceiverOptions` intentionally contains only:

- `BearerToken`: optional exact bearer token; null/empty/whitespace disables token validation;
- `MaximumBatchEvents`: maximum accepted event count, default `100`.

Options belong to each mapped endpoint, so different endpoints can use different authentication and limits.

## Wire protocol behavior

The receiver validates:

- protocol version `1`;
- canonical GUID `BatchId`;
- `Count` matching the number of events;
- `MaximumBatchEvents`;
- no `null` entries in `Logs`;
- canonical GUID `EventId`;
- non-empty `ApplicationId`;
- positive `ProcessId`.

SerilogRelay wire JSON is deserialized with receiver-owned ASP.NET Web-compatible JSON settings. Global host `HttpJsonOptions` therefore cannot silently change the relay protocol's naming or case behavior.

HTTP results:

- `204`: durable handling completed;
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

A custom handler must complete its own durable-acceptance guarantees before returning successfully.

## Not part of the 1.0 contract

The receiver intentionally does not define:

- request-body byte limits;
- application allow-lists or application-based storage routing;
- retention/cleanup policy;
- duplicate-query or deduplicated-view behavior;
- provider-specific migrations;
- multi-backend composition helpers;
- richer acknowledgement payloads.
