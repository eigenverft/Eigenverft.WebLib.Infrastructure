using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

using Eigenverft.WebLib.ClientNetwork;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Eigenverft.WebLib.RequestTrafficLogging
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

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                IClientNetworkFeature? clientNetwork = context.Features.Get<IClientNetworkFeature>();
                IPAddress? remoteAddress = clientNetwork?.RemoteIpAddress ?? context.Connection.RemoteIpAddress;

                state.LogContext.AddParameter("Connection.Remote.IpAddress", remoteAddress?.ToString());
            }

            if ((fields & RequestTrafficLoggingFields.Connection) != 0)
            {
                state.LogContext.AddParameter("Connection.Remote.Port", context.Connection.RemotePort);
                state.LogContext.AddParameter("Connection.Local.IpAddress", context.Connection.LocalIpAddress?.ToString());
                state.LogContext.AddParameter("Connection.Local.Port", context.Connection.LocalPort);
            }

            if ((fields & RequestTrafficLoggingFields.ForwardedInformation) != 0)
            {
                AddForwardedInformation(context, state);
            }

            if ((fields & RequestTrafficLoggingFields.Identity) != 0)
            {
                IIdentity? identity = context.User?.Identity;
                state.LogContext.AddParameter("Identity.Authenticated", identity?.IsAuthenticated ?? false);
                state.LogContext.AddParameter("Identity.Name", identity?.Name);
                state.LogContext.AddParameter("Identity.AuthenticationType", identity?.AuthenticationType);
            }

            if ((fields & RequestTrafficLoggingFields.Routing) != 0)
            {
                Endpoint? endpoint = context.GetEndpoint();
                state.LogContext.AddParameter("Routing.Endpoint", endpoint?.DisplayName);
                state.LogContext.AddParameter(
                    "Routing.Pattern",
                    endpoint is RouteEndpoint routeEndpoint ? routeEndpoint.RoutePattern.RawText : null);
            }

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                state.LogContext.AddParameter("Response.StatusCode", context.Response.StatusCode);
                state.LogContext.AddParameter("Response.Started", context.Response.HasStarted);
            }

            if ((fields & RequestTrafficLoggingFields.ResponseHeaders) != 0)
            {
                RequestTrafficLoggingInterceptor.AddHeaders(
                    state.LogContext,
                    context.Response.Headers,
                    state.Options.ResponseHeaders,
                    state.Options,
                    "Response.Header.");
            }

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                state.LogContext.AddParameter("Response.Body.ContentType", context.Response.ContentType);
                state.LogContext.AddParameter("Response.Body.DeclaredLength", context.Response.ContentLength);
            }

            if ((fields & RequestTrafficLoggingFields.ResponseBody) != 0)
            {
                AddResponseBodyMetadata(context, state);
            }

            state.LogContext.AddParameter("Pipeline.Outcome", outcome);

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                state.LogContext.AddParameter("Pipeline.Aborted", requestAborted);
                state.LogContext.AddParameter(
                    "Pipeline.DurationMs",
                    state.TimeProvider.GetElapsedTime(state.StartTimestamp).TotalMilliseconds);
                state.LogContext.AddParameter("Pipeline.ExceptionType", terminalException?.GetType().FullName);
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
                state.LogContext.AddParameter("Connection.ForwardedIpChain", null);
                state.LogContext.AddParameter("Connection.ForwardedIpMalformed", false);
                return;
            }

            IReadOnlyList<ClientForwardedIpAddress> chain = feature.ForwardedIpChain;
            var values = new string[chain.Count];
            for (var i = 0; i < chain.Count; i++)
            {
                ClientForwardedIpAddress item = chain[i];
                values[i] = item.Source + ":" + (item.Address?.ToString() ?? item.RawValue);
            }

            state.LogContext.AddParameter("Connection.ForwardedIpChain", string.Join(" -> ", values));
            state.LogContext.AddParameter(
                "Connection.ForwardedIpMalformed",
                feature.HasMalformedForwardedIpInformation);
        }

        private static void AddResponseBodyMetadata(HttpContext context, RequestTrafficLoggingState state)
        {
            long? declaredLength = context.Response.ContentLength;
            state.LogContext.AddParameter(
                "Response.Body.Truncated",
                IsKnownBodyLargerThanCaptureLimit(declaredLength, state.Options.ResponseBodyLimit));
        }

        private static bool? IsKnownBodyLargerThanCaptureLimit(long? totalBytes, int limit)
        {
            return totalBytes.HasValue ? totalBytes.Value > limit : null;
        }
    }
}
