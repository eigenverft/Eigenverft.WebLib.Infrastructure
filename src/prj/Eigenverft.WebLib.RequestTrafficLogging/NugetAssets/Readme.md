# Eigenverft.WebLib.RequestTrafficLogging

ASP.NET Core request traffic logging middleware and HTTP logging field configuration with sensitive value controls.

Request traffic logging preserves the original allowlisted behavior from Eigenverft.WebLib.Infrastructure by default. It produces one combined request/response record with completion status and bounded body capture.

The default record uses hierarchical property names in a stable diagnostic order:

- request: `Request.*`, `Request.Header.*`, and `Request.Body.*`
- connection: `Connection.Remote.*`, `Connection.Local.*`, and forwarded-IP information
- identity and routing: `Identity.*` and `Routing.*`
- response: `Response.*`, `Response.Header.*`, and `Response.Body.*`
- pipeline result: `Pipeline.Outcome`, `Pipeline.Aborted`, `Pipeline.DurationMs`, and `Pipeline.ExceptionType`

`Pipeline.Outcome` describes whether the middleware pipeline returned, faulted, or was aborted; it is not the HTTP success status. `Response.Started` is sampled when the record completes. `Connection.ForwardedIpChain` is rendered as a readable ` -> `-separated chain.

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
