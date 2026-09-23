# Eigenverft.WebLib.RequestTrafficLogging

ASP.NET Core request traffic logging middleware and HTTP logging field configuration with sensitive value controls.

Request traffic logging preserves the original allowlisted behavior from Eigenverft.WebLib.Infrastructure by default. It produces one combined request/response record with completion status and bounded body capture.

`PipelineOutcome` describes whether the middleware pipeline returned, faulted, or was aborted; it is not the HTTP success status. `ResponseStartedAtCapture` is sampled when the record completes. `ForwardedIpChain` is rendered as a readable ` -> `-separated chain.

For a deliberate full-header diagnostic session, opt in to raw header capture:

```csharp
builder.Services.AddRequestTrafficLogging(options =>
{
    options.Fields = RequestTrafficLoggingFields.All;
    options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
});
```

`AllRaw` captures every incoming request and outgoing response header value, including previously unknown header names, bearer credentials and cookies. Multiple values are recorded individually as `RequestHeader.Name[0]`, `RequestHeader.Name[1]`, and similarly for responses. The default `AllowListed` mode and `SensitiveValueMode` behavior remain unchanged. Raw mode requires the corresponding `RequestHeaders` or `ResponseHeaders` field flag and does not expand body limits or change middleware placement. Protect the resulting logs accordingly.
