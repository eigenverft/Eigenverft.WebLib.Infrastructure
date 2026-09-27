# Eigenverft.WebLib.SerilogRelayReceiver

ASP.NET Core receiver for `Eigenverft.NetLib.SerilogRelay` log batches with a built-in
Entity Framework Core persistence path.

## 1.0 storage direction

Entity Framework Core is the built-in durable persistence integration.

The receiver package references EF Core itself, but it does **not** choose a database provider.
The host chooses and configures SQLite, SQL Server, PostgreSQL/Npgsql, or another EF Core
provider that satisfies the host's durability requirements.

The host also owns connection strings, migrations, database lifecycle, retention, and querying.

Custom `ISerilogRelayBatchHandler` implementations remain supported for applications that need a
queue, multiple backends, or non-EF processing.

## EF Core setup

Use a host-owned `DbContext` and add the receiver model to it:

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

Register an `IDbContextFactory<TDbContext>` with the provider selected by the host, then register the built-in receiver handler. The handler creates and disposes one isolated DbContext per accepted batch, so its `SaveChangesAsync` cannot accidentally flush unrelated tracked changes from another request service. SQLite is shown only as an example provider:

```csharp
builder.Services.AddDbContextFactory<LoggingDbContext>(
    options => options.UseSqlite(
        builder.Configuration.GetConnectionString("Logging")));

builder.Services
    .AddSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>();
```

Map the durable endpoint:

```csharp
app.MapSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>(
    "/api/v1/logs",
    options =>
    {
        options.BearerToken =
            builder.Configuration["CentralLogging:BearerToken"];

        options.MaximumBatchEvents = 100;
    });
```

The same receiver registration works with another EF Core provider by changing the host's `AddDbContextFactory` provider configuration. Provider packages are not dependencies of this package.

## EF Core model and migrations

`ConfigureSerilogRelayReceiver()` adds `SerilogRelayReceivedEvent` to the host model.

The default relational table name is:

```text
SerilogRelayReceivedEvents
```

`ReceiveId` is the receiver-local generated primary key. `EventId` is indexed but deliberately
**not unique**. If a sender retries after a lost response, the same logical event can therefore be
stored again as another physical receive instead of being rejected or silently discarded.

The model also indexes `BatchId`, `ReceivedAtUtc`, and
`(ApplicationId, ReceivedAtUtc)`.

Because the entity lives in the host's DbContext model, the host's normal EF Core migration workflow owns schema creation and upgrades. Registering a factory does not change that model ownership. The receiver package does not ship provider-specific migrations.

For throwaway/test databases, `EnsureCreated()` can be useful. For production databases that use
migrations, use the host's normal migration workflow instead of mixing it with `EnsureCreated()`.

## Durable acceptance semantics

The built-in EF Core handler:

- creates a fresh host-configured DbContext from `IDbContextFactory<TDbContext>` for the batch;
- maps every validated event to one `SerilogRelayReceivedEvent`;
- preserves batch metadata including protocol version, batch id, batch timestamp, and batch count;
- preserves all current event fields from the sender;
- adds one receiver-side `ReceivedAtUtc` timestamp for the physical receive;
- adds the complete batch to the DbContext and calls `SaveChangesAsync` once;
- returns success only after that save completes.

For normal relational EF Core providers, one `SaveChanges` call is transactionally protected by
the provider/EF Core transaction semantics. The repository tests exercise this with SQLite and
verify that a failure while persisting the second event leaves zero rows from the batch.

A provider used for production must provide durability/atomicity appropriate for the endpoint's
contract. Provider-specific behavior remains the host's responsibility.

## Request ownership and cancellation

Before a complete batch has been received and validated, request processing uses
`HttpContext.RequestAborted`.

After validation, durable handling receives the host application's stopping token instead of the
client request-abort token. Therefore:

- a client disconnect after durable handoff does not by itself cancel EF Core persistence;
- host shutdown can cancel an in-flight save;
- host-shutdown cancellation returns `503 Service Unavailable`;
- successful durable completion returns `204 No Content`.

This boundary is exercised with real Kestrel requests in the test suite.

## Endpoint behavior

`MaximumBatchEvents` defaults to `100`, matching the current SerilogRelay sender maximum batch
default.

`BearerToken` is optional. Null, empty, or whitespace disables bearer-token validation. When
configured, the endpoint requires an exact `Authorization: Bearer <token>` value.

Options belong to the mapped endpoint rather than global receiver state. Multiple endpoints can
therefore use different authentication and limits, including when they use the same DbContext.

A single endpoint is not tied to one application. Valid batches may contain events from different
`ApplicationId`, `MachineId`, and `ProcessId` values.

SerilogRelay wire JSON is deserialized with receiver-owned ASP.NET Web-compatible JSON settings.
Global host `HttpJsonOptions` cannot silently change relay property naming/case behavior.

## Protocol validation

The receiver validates:

- protocol version `1`;
- canonical GUID `BatchId`;
- `Count` matching the number of events;
- the endpoint's `MaximumBatchEvents`;
- no `null` entries inside `Logs`;
- canonical GUID `EventId`;
- non-empty `ApplicationId`;
- positive `ProcessId`.

Transport results are intentionally simple:

- `204`: durable handling completed;
- `400`: malformed or protocol-invalid payload;
- `401`: bearer authentication failed;
- `415`: unsupported JSON media type or charset;
- `503`: durable handling was cancelled because the host is stopping;
- other `5xx`: unexpected receiver/handler/storage failure.

## Custom handler path

Applications that do not want the built-in EF Core persistence can still register their own
handler:

```csharp
builder.Services.AddSerilogRelayReceiver<MyRelayBatchHandler>();

app.MapSerilogRelayReceiver<MyRelayBatchHandler>(
    "/api/v1/logs");
```

The custom handler remains responsible for making its own durable-acceptance guarantees before it
returns successfully.

Request-body byte limits, application allow-lists, retention policy, and richer acknowledgement
payloads are not part of the 1.0 receiver contract.
