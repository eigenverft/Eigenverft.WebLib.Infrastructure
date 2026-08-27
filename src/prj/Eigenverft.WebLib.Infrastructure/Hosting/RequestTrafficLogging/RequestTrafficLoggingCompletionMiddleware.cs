using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    internal sealed class RequestTrafficLoggingCompletionMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestTrafficLoggingCompletionMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            RequestTrafficLoggingState? state = context.Features.Get<RequestTrafficLoggingState>();
            if (state is null)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            RequestTrafficCountingReadStream? requestCounter = null;
            Stream? originalRequestBody = null;
            RequestTrafficCountingResponseBodyFeature? responseCounter = null;
            IHttpResponseBodyFeature? originalResponseBodyFeature = null;

            RequestTrafficLoggingFields fields = state.Options.Fields;

            if ((fields & RequestTrafficLoggingFields.RequestBody) != 0)
            {
                originalRequestBody = context.Request.Body;
                requestCounter = new RequestTrafficCountingReadStream(originalRequestBody);
                context.Request.Body = requestCounter;
            }

            if ((fields & RequestTrafficLoggingFields.ResponseBody) != 0)
            {
                originalResponseBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
                if (originalResponseBodyFeature is not null)
                {
                    responseCounter = new RequestTrafficCountingResponseBodyFeature(originalResponseBodyFeature);
                    context.Features.Set<IHttpResponseBodyFeature>(responseCounter);
                }
            }

            Exception? caughtException = null;
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                caughtException = exception;
                throw;
            }
            finally
            {
                try
                {
                    CompleteTrafficRecord(context, state, caughtException, requestCounter, responseCounter);
                }
                finally
                {
                    if (originalResponseBodyFeature is not null)
                    {
                        context.Features.Set(originalResponseBodyFeature);
                    }

                    if (originalRequestBody is not null)
                    {
                        context.Request.Body = originalRequestBody;
                    }
                }
            }
        }

        private static void CompleteTrafficRecord(
            HttpContext context,
            RequestTrafficLoggingState state,
            Exception? caughtException,
            RequestTrafficCountingReadStream? requestCounter,
            RequestTrafficCountingResponseBodyFeature? responseCounter)
        {
            RequestTrafficLoggingFields fields = state.Options.Fields;
            Exception? handledException = caughtException is null
                ? context.Features.Get<IExceptionHandlerFeature>()?.Error
                : null;
            Exception? terminalException = caughtException ?? handledException;
            bool requestAborted = context.RequestAborted.IsCancellationRequested;
            string outcome = ClassifyOutcome(caughtException, handledException, requestAborted);

            state.LogContext.AddParameter("Outcome", outcome);

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                IClientNetworkFeature? clientNetwork = context.Features.Get<IClientNetworkFeature>();
                IPAddress? remoteAddress = clientNetwork?.RemoteIpAddress ?? context.Connection.RemoteIpAddress;

                state.LogContext.AddParameter("RemoteIpAddress", remoteAddress?.ToString());
                state.LogContext.AddParameter("ResponseContentType", context.Response.ContentType);
                state.LogContext.AddParameter("ResponseContentLength", context.Response.ContentLength);
                state.LogContext.AddParameter("ResponseStarted", context.Response.HasStarted);
                state.LogContext.AddParameter("Aborted", requestAborted);
                state.LogContext.AddParameter(
                    "DurationMs",
                    state.TimeProvider.GetElapsedTime(state.StartTimestamp).TotalMilliseconds);
                state.LogContext.AddParameter("ExceptionType", terminalException?.GetType().FullName);
            }

            if ((fields & RequestTrafficLoggingFields.Routing) != 0)
            {
                Endpoint? endpoint = context.GetEndpoint();
                state.LogContext.AddParameter("Endpoint", endpoint?.DisplayName);
                state.LogContext.AddParameter(
                    "RoutePattern",
                    endpoint is RouteEndpoint routeEndpoint ? routeEndpoint.RoutePattern.RawText : null);
            }

            if ((fields & RequestTrafficLoggingFields.Identity) != 0)
            {
                IIdentity? identity = context.User?.Identity;
                state.LogContext.AddParameter("IdentityAuthenticated", identity?.IsAuthenticated ?? false);
                state.LogContext.AddParameter("IdentityName", identity?.Name);
                state.LogContext.AddParameter("IdentityAuthenticationType", identity?.AuthenticationType);
            }

            if ((fields & RequestTrafficLoggingFields.Connection) != 0)
            {
                state.LogContext.AddParameter("LocalIpAddress", context.Connection.LocalIpAddress?.ToString());
                state.LogContext.AddParameter("LocalPort", context.Connection.LocalPort);
                state.LogContext.AddParameter("RemotePort", context.Connection.RemotePort);
            }

            if ((fields & RequestTrafficLoggingFields.ForwardedInformation) != 0)
            {
                AddForwardedInformation(context, state);
            }

            if ((fields & RequestTrafficLoggingFields.RequestBody) != 0)
            {
                AddRequestBodyMetadata(context, state, requestCounter);
            }

            if ((fields & RequestTrafficLoggingFields.ResponseBody) != 0)
            {
                AddResponseBodyMetadata(context, state, responseCounter);
            }
        }

        private static string ClassifyOutcome(
            Exception? caughtException,
            Exception? handledException,
            bool requestAborted)
        {
            if (caughtException is not null)
            {
                if (requestAborted &&
                    (caughtException is OperationCanceledException || caughtException is IOException))
                {
                    return "Aborted";
                }

                return "Faulted";
            }

            if (handledException is not null)
            {
                return "Faulted";
            }

            return requestAborted ? "Aborted" : "Completed";
        }

        private static void AddForwardedInformation(HttpContext context, RequestTrafficLoggingState state)
        {
            IClientNetworkFeature? feature = context.Features.Get<IClientNetworkFeature>();
            if (feature is null)
            {
                state.LogContext.AddParameter("ForwardedIpChain", null);
                state.LogContext.AddParameter("HasMalformedForwardedIpInformation", false);
                return;
            }

            IReadOnlyList<ClientForwardedIpAddress> chain = feature.ForwardedIpChain;
            var values = new string[chain.Count];
            for (var i = 0; i < chain.Count; i++)
            {
                ClientForwardedIpAddress item = chain[i];
                values[i] = item.Source + ":" + (item.Address?.ToString() ?? item.RawValue);
            }

            state.LogContext.AddParameter("ForwardedIpChain", values);
            state.LogContext.AddParameter(
                "HasMalformedForwardedIpInformation",
                feature.HasMalformedForwardedIpInformation);
        }

        private static void AddRequestBodyMetadata(
            HttpContext context,
            RequestTrafficLoggingState state,
            RequestTrafficCountingReadStream? counter)
        {
            long bytesRead = counter?.BytesRead ?? 0L;
            int limit = state.Options.RequestBodyLimit;
            bool textCapture = IsFrameworkDefaultTextMediaType(context.Request.ContentType) && limit > 0;
            long capturedBytes = textCapture ? Math.Min(bytesRead, limit) : 0L;
            long? totalBytes = context.Request.ContentLength;

            if (totalBytes is null && counter?.ReachedEnd == true)
            {
                totalBytes = bytesRead;
            }

            state.LogContext.AddParameter("RequestBodyCapturedBytes", capturedBytes);
            state.LogContext.AddParameter("RequestBodyTotalBytes", totalBytes);
            state.LogContext.AddParameter(
                "RequestBodyTruncated",
                textCapture && bytesRead >= limit);
        }

        private static void AddResponseBodyMetadata(
            HttpContext context,
            RequestTrafficLoggingState state,
            RequestTrafficCountingResponseBodyFeature? counter)
        {
            long totalBytes = counter?.CountingStream.BytesWritten ?? 0L;
            int limit = state.Options.ResponseBodyLimit;
            bool textCapture = IsFrameworkDefaultTextMediaType(context.Response.ContentType) && limit > 0;
            long capturedBytes = textCapture ? Math.Min(totalBytes, limit) : 0L;

            state.LogContext.AddParameter("ResponseBodyCapturedBytes", capturedBytes);
            state.LogContext.AddParameter("ResponseBodyTotalBytes", totalBytes);
            state.LogContext.AddParameter(
                "ResponseBodyTruncated",
                textCapture && totalBytes > limit);
        }

        private static bool IsFrameworkDefaultTextMediaType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            int semicolon = contentType.IndexOf(';');
            string mediaType = (semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType).Trim();

            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) &&
                (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
                 mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase));
        }
    }
}
