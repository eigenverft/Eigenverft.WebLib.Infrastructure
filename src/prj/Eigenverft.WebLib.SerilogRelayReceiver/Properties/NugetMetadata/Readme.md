# Eigenverft.WebLib.SerilogRelayReceiver

ASP.NET Core receiver endpoint for `Eigenverft.NetLib.SerilogRelay` log batches.

## Registration

Register the application-specific handler:

```csharp
builder.Services.AddSerilogRelayReceiver<MyRelayBatchHandler>();
```

The handler is the storage/application boundary:

```csharp
public sealed class MyRelayBatchHandler : ISerilogRelayBatchHandler
{
    public ValueTask HandleAsync(
        SerilogRelayBatch batch,
        CancellationToken cancellationToken)
    {
        // Persist, enqueue, or otherwise process the validated batch.
        return ValueTask.CompletedTask;
    }
}
```

The receiver package does not choose SQLite, EF Core, a queue, retention, or another storage backend.

After a complete batch has been received and validated, the handler is invoked with the host application's stopping token rather than the HTTP request-abort token. A client disconnect therefore does not by itself cancel durable handler work that the server has already accepted.

## Endpoint

Map one receiver endpoint:

```csharp
app.MapSerilogRelayReceiver<MyRelayBatchHandler>(
    "/api/v1/logs",
    options =>
    {
        options.BearerToken =
            builder.Configuration["CentralLogging:BearerToken"];

        options.MaximumBatchEvents = 100;
    });
```

`MaximumBatchEvents` defaults to `100`, matching the current SerilogRelay sender maximum batch default.

`BearerToken` is optional. Null, empty, or whitespace disables bearer-token validation. When configured, the endpoint requires an exact `Authorization: Bearer <token>` value.

Options belong to the mapped endpoint rather than to global receiver state. Multiple endpoints can therefore use different authentication/limits and different handler types.

A single endpoint is not tied to one application. Valid batches may contain events from different `ApplicationId`, `MachineId`, and `ProcessId` values.

## Current protocol validation

The receiver currently validates:

- protocol version `1`;
- canonical GUID `BatchId`;
- `Count` matching the number of events;
- the endpoint's `MaximumBatchEvents`;
- canonical GUID `EventId`;
- non-empty `ApplicationId`;
- positive `ProcessId`.

Successful handler completion returns HTTP `204 No Content`.

Retention, database/backend configuration, request-body byte limits, and higher-level authentication models are intentionally outside this first receiver pass.

The repository's tests include an EF Core + SQLite durable reference handler to exercise transaction, repeat-delivery, and rollback semantics. EF Core and SQLite are test-only here and are not dependencies or required backends of the receiver package.
