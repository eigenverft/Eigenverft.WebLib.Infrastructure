# Initial SerilogRelay Receiver Design Direction

## Status of this document

This document records the current design direction for the first reusable
`Eigenverft.WebLib.SerilogRelayReceiver` implementation and the storage/handler work that is
expected to follow.

It is intentionally **non-normative**. These notes are not a frozen protocol specification and
are not release requirements carved in stone. They capture the current reasoning so later
implementation work can start from an explicit shared understanding instead of rediscovering
the same trade-offs.

Where later implementation experience, operational evidence, or a simpler design suggests a
better choice, this document should be updated.

## Overall direction

The sender and receiver should remain loosely coupled.

The SerilogRelay sink has a simple job:

- persist logs locally while necessary;
- attempt to deliver batches;
- consider a successful HTTP response to mean the batch no longer needs to remain pending;
- retry when delivery did not complete successfully.

The receiver has a different job:

- authenticate and validate an incoming batch;
- hand the accepted batch to application-specific durable processing;
- return success only after that processing has completed according to the endpoint's rules.

The sink should not need to understand receiver database layout, duplicate classification,
storage topology, retention policy, or other server-side implementation details.

### Why

Client-side logging code is expensive to change after deployment. A server can normally be
replaced or upgraded centrally. Keeping the client contract small lets receiver/storage behavior
evolve without forcing applications that use the sink to update merely because the server gained
new internal handling states.

For that reason, richer HTTP status codes may still be useful diagnostics, but the sink should
not grow a detailed dependency on receiver-specific meanings unless a future requirement proves
that necessary.

## Successful acceptance and HTTP 2xx

### Current direction

A successful receiver response should mean that the receiver has completed the durable handling
required by that endpoint.

A handler merely observing a batch, placing it into volatile memory, or starting asynchronous
work is not enough for success if losing the process at that point could lose the logs.

For a database-backed endpoint, the practical interpretation is normally:

> the batch has been stored/committed according to that handler's durable storage contract.

For a durable queue-backed endpoint, successful enqueueing to that durable queue may satisfy the
same intent.

### Why

The sink uses a successful response as the point at which it can stop treating its claimed rows
as pending delivery. Returning success before durable acceptance would move the data-loss window
from the client spool to volatile server state.

## Batch as the acceptance unit

### Current direction

The HTTP batch is treated as one acceptance unit from the sender's point of view.

The receiver should not return success until all handling required for that batch by the mapped
endpoint has completed.

For a single transactional database this will likely mean storing the whole batch in one
transaction where practical.

For an endpoint that intentionally targets multiple durable backends, a later backend may fail
after an earlier backend already committed. The initial direction is **not** to introduce a
distributed transaction protocol merely to avoid this case.

Instead:

- no success is returned until every required backend has accepted the batch;
- a retry may revisit a backend that already accepted some or all of the data;
- each backend/handler should therefore tolerate repeat delivery safely enough for its purpose.

### Why

This preserves a simple sender contract while avoiding the complexity and operational coupling of
distributed transactions.

## Event identity and repeated delivery

### Current sender behavior

The current SerilogRelay sink creates each `EventId` with `Guid.NewGuid()` and already stores it
locally under a globally unique constraint. It does not namespace the identifier by
`ApplicationId`.

A random GUID/UUID v4 has 122 random bits. Approximate birthday-collision probabilities are:

- 1,000,000 generated event ids: about `9.4e-26`;
- 10,000,000 generated event ids: about `9.4e-24`;
- 1,000,000,000 generated event ids: about `9.4e-20`.

### Current direction

A genuine random collision is therefore not treated as a normal operating case that deserves a
complex receiver protocol.

If the receiver sees the same `EventId` more than once, the overwhelmingly more likely
explanation is repeated delivery of the same logical log event, for example:

1. the receiver stored the event;
2. the HTTP response was lost or the client timed out;
3. the sink could not know that storage succeeded;
4. the sink sends the event again.

The first storage implementation should prefer **preserving log information over rejecting it**.

Possible server-side strategies include:

- storing the repeated row again;
- storing it and marking it as a repeated delivery;
- relating it to an earlier stored row;
- presenting deduplicated views later while retaining the physical receives.

These are storage/query choices, not sink protocol requirements.

The initial direction is therefore **not** to introduce a receiver-side `409 Conflict` contract
for repeated `EventId` values and not to require the sink to understand duplicate/conflict
semantics.

### Why

For logging, losing a legitimate event because the receiver made an overconfident duplicate
decision is generally less desirable than retaining an extra physical copy that can later be
identified or filtered.

If real operational evidence later shows that strict de-duplication is valuable, the storage
design can add it without first expanding the sink protocol.

## Endpoint, application, and storage topology

### Applications

A receiver endpoint is not tied to one `ApplicationId`.

A valid batch may contain events from different applications, machines, and processes. The
receiver passes those identities through as event metadata.

Application-based routing, partitioning, authorization, or filtering may be added later where
useful, but is not assumed by the base receiver.

### Storage belongs to endpoint/application handling, not to the wire protocol

The current handler boundary remains intentionally storage-neutral:

```csharp
ValueTask HandleAsync(
    SerilogRelayBatch batch,
    CancellationToken cancellationToken);
```

Different endpoint/storage topologies should remain possible:

```text
1 endpoint : 1 storage
1 endpoint : n storages
n endpoints : 1 storage
```

Examples:

```text
/logs/customer-a -> Database A

/logs/internal ---+
                  +-> Shared Database
/logs/external ---+

/logs -> Database + durable archive/queue
```

The receiver package should not require SQLite, EF Core, a particular database engine, or a
particular queue implementation.

### Why

Storage is an application/deployment choice. Making it part of the receiver protocol options
would unnecessarily couple HTTP ingestion to one persistence architecture and would make
multiple endpoint configurations harder rather than easier.

## Endpoint-specific configuration

The direction remains that receiver options are scoped to a mapped endpoint rather than stored
as one global receiver configuration.

This permits separate endpoints to have different:

- bearer tokens;
- maximum batch-event limits;
- handler types;
- future storage/backend configuration owned by those handlers.

The current receiver options remain intentionally small:

```csharp
public sealed class SerilogRelayReceiverOptions
{
    public string? BearerToken { get; set; }

    public int MaximumBatchEvents { get; set; } = 100;
}
```

`MaximumRequestBodyBytes`, retention, database configuration, and storage limits remain deferred.

### Possible future application filtering

An endpoint may eventually benefit from an allow-list such as `AllowedApplicationIds`.

The working idea would be:

- no configured values: all application ids are allowed;
- configured values: every event in the batch must use an allowed application id;
- a batch containing a disallowed application is rejected as a whole.

This is only a possible future option, not a current requirement.

## HTTP result semantics

### Current direction

HTTP remains a transport-level indication, not a detailed storage protocol between sink and
receiver.

Existing receiver responses such as the following remain useful:

- `401`: authentication failed;
- `400`: request/protocol validation failed;
- `415`: unsupported request content type;
- `5xx`: a valid request could not currently be completed by receiver/handler processing;
- `2xx`: the batch has completed the endpoint's durable handling.

The sink should primarily care about whether delivery succeeded or did not succeed. It should not
need database-specific interpretations of statuses such as duplicate, conflict, storage shard,
or retention outcomes.

### Why

The receiver can evolve centrally. Adding server-side intelligence should not require a forced
client application upgrade merely because the server became more sophisticated.

## Handler failures and diagnostics

A handler/storage failure that prevents durable completion should prevent a success response.

Unexpected handler exceptions are valuable server-side diagnostic information. A future concrete
storage implementation may keep receiver/handler diagnostics separately, for example in a
dedicated diagnostic table.

Potential diagnostic information could include:

- receive timestamp;
- endpoint identity;
- batch id;
- involved application ids;
- exception type/message/stack trace.

Secrets such as bearer tokens must not be recorded.

The reusable receiver package itself should not create its own diagnostic database merely to
support this. Diagnostics belong with the concrete host/storage implementation.

## Request cancellation and durable handling

### What `HttpContext.RequestAborted` means

`RequestAborted` represents the lifetime of the HTTP request/client connection. It can be
triggered because the client cancels, a timeout occurs, a proxy disconnects, the transport
breaks, or the sink shuts down while a request is in flight.

It does **not** necessarily mean that the server should undo work that it has already accepted.

### Current direction

There is a useful boundary between receiving the request and owning the durable work:

```text
HTTP body not fully received / not validated
    -> client/request cancellation may abort processing

batch fully received and validated
    -> handoff to durable handling

durable handling has begun
    -> client disappearance should not by itself undo or cancel accepted storage work
```

Once the server has taken ownership of a complete valid batch, it should normally finish durable
processing even if the client can no longer receive the response.

If storage then succeeds but the response is lost, the sink may send the same events again. That
is an expected distributed-systems failure mode and is one reason repeated delivery must be
tolerated.

### Implemented cancellation boundary

The handler API continues to receive a `CancellationToken`, but the endpoint now separates request ownership from durable-work ownership:

- body reading and request validation use `HttpContext.RequestAborted`;
- after a complete batch has been validated, the handler receives `IHostApplicationLifetime.ApplicationStopping`;
- a disappearing client therefore does not by itself cancel durable work already accepted by the server;
- host shutdown can still request cancellation of in-flight durable handling.

This behavior is exercised together with the first durable reference handler rather than being tied to a particular storage provider.

## First durable reference exercise

The test project now contains a concrete EF Core + SQLite handler used only to exercise the receiver contract. It is intentionally not product storage code and does not make EF Core or SQLite dependencies of `Eigenverft.WebLib.SerilogRelayReceiver`.

That reference demonstrates:

- one explicit transaction for the complete batch;
- success only after commit;
- rollback of the complete batch when handler processing fails;
- repeated `EventId` values are physically accepted again rather than rejected or converted into a `409` protocol;
- one batch can contain multiple applications;
- storage provider selection remains owned by the consuming application/handler.

## Things deliberately not decided yet

This document does not currently decide:

- concrete database technology or schema;
- EF Core versus direct database access;
- retention or cleanup rules;
- request-body byte limits;
- whether repeated receives are stored as independent rows, linked rows, or marked by a flag;
- exact duplicate-query behavior in UI/reporting;
- application allow-list API;
- storage partitioning by application;
- database-per-endpoint configuration API;
- multi-backend composition helpers;
- server-side diagnostic table schema;
- whether future handler APIs need cancellation semantics beyond the current host-application stopping token;
- handler retry policy inside the receiver;
- richer acknowledgement payloads.

These should be shaped when their first real implementation gives enough information to make the
choice useful.

## Working principles for the first storage implementation

The current direction can be summarized as:

1. Prefer preserving log information over clever de-duplication.
2. Keep sink behavior simple and stable.
3. Treat success as completion of the endpoint's durable handling.
4. Treat the batch as the sender-facing acceptance unit.
5. Permit repeat delivery without making it an HTTP protocol error.
6. Keep storage technology behind the application handler boundary.
7. Allow 1:1, 1:n, and n:1 endpoint/storage topologies.
8. Keep one endpoint capable of receiving multiple applications.
9. Do not let a disappearing HTTP client automatically erase already accepted durable work.
10. Revisit these directions when the first real storage implementation exposes better evidence.

The goal is not perfect exactly-once delivery. The goal is a durable, loss-resistant logging path
whose client contract stays small enough to evolve the receiver independently.
