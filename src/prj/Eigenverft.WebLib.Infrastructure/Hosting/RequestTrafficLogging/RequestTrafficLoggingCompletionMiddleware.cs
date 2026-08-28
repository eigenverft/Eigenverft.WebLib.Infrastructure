using System;
using System.Collections.Generic;
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
                CompleteTrafficRecord(context, state, caughtException);
            }
        }

        private static void CompleteTrafficRecord(
            HttpContext context,
            RequestTrafficLoggingState state,
            Exception? caughtException)
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
                AddRequestBodyMetadata(context, state);
            }

            if ((fields & RequestTrafficLoggingFields.ResponseBody) != 0)
            {
                AddResponseBodyMetadata(context, state);
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
                    (caughtException is OperationCanceledException || caughtException is System.IO.IOException))
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

        private static void AddRequestBodyMetadata(HttpContext context, RequestTrafficLoggingState state)
        {
            long? totalBytes = context.Request.ContentLength;
            state.LogContext.AddParameter("RequestBodyTotalBytes", totalBytes);
            state.LogContext.AddParameter(
                "RequestBodyTruncated",
                IsKnownBodyLargerThanCaptureLimit(totalBytes, state.Options.RequestBodyLimit));
        }

        private static void AddResponseBodyMetadata(HttpContext context, RequestTrafficLoggingState state)
        {
            long? totalBytes = context.Response.ContentLength;
            state.LogContext.AddParameter("ResponseBodyTotalBytes", totalBytes);
            state.LogContext.AddParameter(
                "ResponseBodyTruncated",
                IsKnownBodyLargerThanCaptureLimit(totalBytes, state.Options.ResponseBodyLimit));
        }

        private static bool? IsKnownBodyLargerThanCaptureLimit(long? totalBytes, int limit)
        {
            return totalBytes.HasValue ? totalBytes.Value > limit : null;
        }
    }
}
