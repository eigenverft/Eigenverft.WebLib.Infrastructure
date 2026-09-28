# Eigenverft.WebLib.RequestTrafficLogging

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.WebLib.RequestTrafficLogging?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.WebLib.RequestTrafficLogging?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.WebLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE)

ASP.NET Core request traffic logging that combines framework HTTP capture with one structured completion record and explicit outcome semantics.

## ✨ At a glance

| Capability | Details |
| --- | --- |
| Record | One combined request/response record with pipeline outcome and duration. |
| Capture | Framework HTTP Logging provides optional bounded body capture; the default fields are Core and Routing. |
| Header safety | Allow-listed headers and sensitive-value redaction are the defaults. |
| Dependency | Uses `Eigenverft.WebLib.ClientNetwork` transitively for optional forwarded-IP details. |

## 📦 Installation

```shell
dotnet add package Eigenverft.WebLib.RequestTrafficLogging
```

## 🚀 Quick start

```csharp
using Eigenverft.WebLib.RequestTrafficLogging;

builder.Services.AddRequestTrafficLogging();

WebApplication app = builder.Build();
app.UseRequestTrafficLogging();
```

The package registers and activates ASP.NET Core `UseHttpLogging()` internally. Do not add a second `UseHttpLogging()` in the same linear pipeline. Place `UseRequestTrafficLogging()` before exception-handling middleware when handled exceptions should be classified as faulted with their final response status.

## Record shape and completion semantics

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

The body text itself remains the framework-owned `RequestBody` or `ResponseBody` field. Its related WebLib metadata uses `Request.Body.ContentType`, `Request.Body.DeclaredLength`, `Request.Body.Truncated` and the corresponding `Response.Body.*` names. `DeclaredLength` is the HTTP `Content-Length` when known, not a byte counter invented by the logger. Body field groups are opt-in; configured body limits default to 4 KiB.

To populate forwarded-IP details, enable `app.UseClientNetworkFeature()` before the logging middleware. The feature's position relative to trusted Forwarded Headers determines which remote peer address it observes.

## Header capture and sensitivity

For a deliberate full-header diagnostic session, opt in to raw header capture:

```csharp
builder.Services.AddRequestTrafficLogging(options =>
{
    options.Fields = RequestTrafficLoggingFields.All;
    options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
});
```

`AllRaw` captures every incoming request and outgoing response header value, including previously unknown header names, bearer credentials and cookies. Multiple values are recorded individually as `Request.Header.Name[0]`, `Request.Header.Name[1]`, and similarly under `Response.Header.*`. The default `AllowListed` mode and `SensitiveValueMode` behavior remain unchanged. Raw mode requires the corresponding `RequestHeaders` or `ResponseHeaders` field flag and does not expand body limits or change middleware placement. Protect the resulting logs accordingly.

## 🎯 Target frameworks

Targets `net8.0` and `net10.0`; .NET 9 applications can consume the compatible `net8.0` asset.

## 🔗 Project links

- [Repository](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure)
- [Solution and tests](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/sln/Eigenverft.WebLib.RequestTrafficLogging)
- [Project source](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/tree/main/src/prj/Eigenverft.WebLib.RequestTrafficLogging)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.WebLib.RequestTrafficLogging)

## 📄 License

MIT; see the [repository license](https://github.com/eigenverft/Eigenverft.WebLib.Infrastructure/blob/main/LICENSE).
