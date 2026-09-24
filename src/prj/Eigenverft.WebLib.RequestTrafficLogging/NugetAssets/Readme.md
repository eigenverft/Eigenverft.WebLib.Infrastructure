# Eigenverft.WebLib.RequestTrafficLogging

ASP.NET Core request traffic logging middleware and HTTP logging field configuration with sensitive value controls.

Request traffic logging preserves the original allowlisted behavior from Eigenverft.WebLib.Infrastructure by default. It produces one combined request/response record with completion status and bounded body capture.

The default record uses hierarchical property names in a stable diagnostic order:

- request: `Request.*`, `Request.Header.*`, and `Request.Body.*`
- connection: `Connection.Remote.*`, `Connection.Local.*`, and forwarded-IP information
- identity and routing: `Identity.*` and `Routing.*`
- response: `Response.*`, `Response.Header.*`, and `Response.Body.*`
- pipeline result: `Pipeline.Outcome`, `Pipeline.Aborted`, `Pipeline.DurationMs`, and `Pipeline.ExceptionType`

`Pipeline.Outcome` describes how the middleware pipeline finished; it is independent of the HTTP status:

- `Completed`: the downstream pipeline returned normally and no handled-exception feature remained. This can coexist with `Pipeline.Aborted: true` when the cancellation signal was observed only at the final snapshot, as commonly happens after a completed streaming or SSE response.
- `Aborted`: an `OperationCanceledException` or `IOException` escaped while `RequestAborted` was set.
- `Faulted`: another exception escaped, or the exception-handler feature reports an exception that was handled into a response.

`Pipeline.Aborted` is the raw value of `RequestAborted.IsCancellationRequested` at completion and does not by itself determine `Pipeline.Outcome`. `Response.Started` is sampled at the same point. `Connection.ForwardedIpChain` is rendered as a readable ` -> `-separated chain.

The body text itself remains the framework-owned `RequestBody` or `ResponseBody` field. Its related WebLib metadata uses `Request.Body.ContentType`, `Request.Body.DeclaredLength`, `Request.Body.Truncated` and the corresponding `Response.Body.*` names. `DeclaredLength` is the HTTP `Content-Length` when known, not a byte counter invented by the logger.

For a deliberate full-header diagnostic session, opt in to raw header capture:

```csharp
builder.Services.AddRequestTrafficLogging(options =>
{
    options.Fields = RequestTrafficLoggingFields.All;
    options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
});
```

`AllRaw` captures every incoming request and outgoing response header value, including previously unknown header names, bearer credentials and cookies. Multiple values are recorded individually as `Request.Header.Name[0]`, `Request.Header.Name[1]`, and similarly under `Response.Header.*`. The default `AllowListed` mode and `SensitiveValueMode` behavior remain unchanged. Raw mode requires the corresponding `RequestHeaders` or `ResponseHeaders` field flag and does not expand body limits or change middleware placement. Protect the resulting logs accordingly.
