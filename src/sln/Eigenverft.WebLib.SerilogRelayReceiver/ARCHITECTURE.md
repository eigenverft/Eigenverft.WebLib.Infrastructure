# SerilogRelay Receiver Architecture

This document summarizes the stable architectural decisions of
`Eigenverft.WebLib.SerilogRelayReceiver`.

The public API and package README define the shipped contract. This document explains the design
behind that contract.

## Responsibilities

The SerilogRelay sender and receiver are intentionally loosely coupled.

The sender is responsible for:

- persisting logs locally while delivery is pending;
- sending batches to the configured receiver endpoint;
- treating a successful HTTP response as completed delivery;
- retrying when delivery does not complete successfully.

The receiver is responsible for:

- authenticating the request when endpoint authentication is configured;
- validating the SerilogRelay wire contract;
- handing the validated batch to durable processing;
- returning success only after the endpoint's durable handling has completed.

The sender does not need to understand receiver database layout, retention, storage partitioning,
or duplicate-query behavior.

## Batch acceptance

The HTTP batch is the sender-facing acceptance unit.

A successful response means the complete batch has satisfied the mapped endpoint's durable handling
contract. Starting background work or placing the batch only in volatile memory is not sufficient.

For the built-in EF Core path, durable acceptance completes after the batch has been persisted by
one `SaveChangesAsync` operation.

For a custom durable queue or multi-backend handler, the handler is responsible for defining and
completing its own acceptance guarantees before returning.

## Repeat delivery

`EventId` identifies the logical sender event, but repeated delivery is an expected distributed
systems condition. For example, the receiver may persist a batch successfully while the response
is lost before the sender observes it.

The built-in persistence model therefore uses a receiver-local `ReceiveId` primary key and keeps
`EventId` non-unique.

A repeated `EventId` is stored as another physical receive instead of becoming an HTTP conflict
or being silently discarded.

This keeps the transport contract simple and favors preserving logs over aggressive de-duplication.

## Endpoint and storage topology

An endpoint is not bound to one `ApplicationId`.

A valid batch may contain events from multiple applications, machines, and processes.

The receiver supports these topologies:

- one endpoint to one storage;
- multiple endpoints to one shared storage;
- multiple endpoints to separate storages;
- one endpoint to multiple durable backends through a custom handler.

Endpoint options are scoped per mapped endpoint, so separate endpoints can use different bearer
tokens, batch limits, handlers, or DbContext types.

Application-based routing and application allow-lists are not part of the 1.0 API.

## Entity Framework Core persistence

Entity Framework Core is the built-in persistence integration.

The receiver package references EF Core itself but does not select a concrete database provider.
The consuming host owns:

- the `DbContext` type;
- `IDbContextFactory<TDbContext>` registration;
- the concrete EF Core provider;
- connection strings;
- migrations and schema deployment;
- retention and querying;
- database lifecycle.

The built-in handler creates one isolated DbContext per accepted batch. This prevents receiver
persistence from flushing unrelated changes tracked by another request scope.

`ConfigureSerilogRelayReceiver()` adds `SerilogRelayReceivedEvent` to the host model.
The event's optional `ApplicationVersion` is kept per row with a 255 UTF-16 code unit maximum. Existing
events without version information leave this value null.

The EF handler limits batch/event timestamps and `TraceId` to 64 UTF-16 code units, `MachineId`
to 256, and `Level`/`SpanId` to 32 before saving to the supplied model. It keeps each value's
prefix without splitting surrogate pairs. Protocol validation preserves the original values;
custom handlers choose their own storage, filtering, truncation, or forwarding policy.

For relational providers, the default table name is:

`SerilogRelayReceivedEvents`

The model includes indexes for:

- `BatchId`;
- `EventId`;
- `ReceivedAtUtc`;
- `(ApplicationId, ReceivedAtUtc)`.

No concrete provider package or provider-specific migration is shipped by the receiver package.

## Request lifetime and durable work

Request processing has two ownership phases.

Before a complete batch has been received and validated, request processing follows
`HttpContext.RequestAborted`.

After validation, durable processing uses the host application's stopping token instead of the
client request-abort token.

As a result:

- a client disconnect after durable handoff does not by itself cancel accepted persistence;
- host shutdown can cancel in-flight durable processing;
- successful handler completion returns `204 No Content`;
- host-shutdown cancellation returns `503 Service Unavailable`.

## Wire protocol ownership

The SerilogRelay JSON payload is a protocol contract, not an application-local DTO convention.

The receiver therefore uses receiver-owned ASP.NET Web-compatible JSON settings. Global host
`HttpJsonOptions` cannot silently change the relay protocol's property naming or case behavior.

The receiver validates protocol version, batch identity, batch count, event identity,
`ApplicationId` and optional `ApplicationVersion` lengths, `ProcessId`, and endpoint batch-size
limits before durable handling begins. The default maximum is 256 events, accepting the matching sender's default spool and direct emergency batches, including during shutdown. Versions remain attached to their originating events even
when another application version sends the batch.

## HTTP result semantics

The receiver uses HTTP primarily as a transport-level success/failure signal:

- `204`: the selected handler completed its acceptance policy;
- `400`: malformed or protocol-invalid payload;
- `401`: bearer authentication failed;
- `415`: unsupported JSON media type or charset;
- `503`: durable handling was cancelled because the host is stopping;
- other `5xx`: unexpected receiver, handler, or storage failure.

Only a completed HTTP 204 acknowledges processing to the sender. This confirms completion
according to the selected handler's policy; it does not promise full-fidelity storage.
The built-in EF handler acknowledges only after saving. Other HTTP statuses, including 200
and 201, leave the events eligible for sender retries.

Storage-specific duplicate or conflict states are not part of the sender protocol.

## Extensibility

The built-in EF Core path is the default durable persistence integration, but it is not the only
supported architecture.

Applications can implement `ISerilogRelayBatchHandler` for:

- durable queues;
- multiple durable backends;
- non-EF persistence;
- application-specific processing.

The receiver deliberately leaves retention, application allow-lists, provider-specific migration
strategy, richer acknowledgement payloads, and multi-backend orchestration policy to consuming
applications or future versions.
