using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    internal sealed class RequestTrafficLoggingInterceptor : IHttpLoggingInterceptor
    {
        private readonly IOptionsMonitor<RequestTrafficLoggingOptions> _options;
        private readonly TimeProvider _timeProvider;

        public RequestTrafficLoggingInterceptor(
            IOptionsMonitor<RequestTrafficLoggingOptions> options,
            TimeProvider timeProvider)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
        {
            ArgumentNullException.ThrowIfNull(logContext);

            RequestTrafficLoggingOptions options = _options.CurrentValue;
            HttpContext httpContext = logContext.HttpContext;
            HttpRequest request = httpContext.Request;
            RequestTrafficLoggingFields fields = options.Fields;

            logContext.LoggingFields = ToHttpLoggingFields(fields);
            logContext.RequestBodyLogLimit = options.RequestBodyLimit;
            logContext.ResponseBodyLogLimit = options.ResponseBodyLimit;

            var state = new RequestTrafficLoggingState(
                logContext,
                options,
                _timeProvider,
                _timeProvider.GetTimestamp(),
                _timeProvider.GetUtcNow());
            httpContext.Features.Set(state);

            logContext.AddParameter("Event", "RequestTraffic");

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                logContext.AddParameter("TimestampUtc", state.TimestampUtc);
                logContext.AddParameter("TraceId", ResolveTraceId(httpContext));
                logContext.AddParameter("Host", request.Host.Value);
                logContext.AddParameter("RequestContentType", request.ContentType);
                logContext.AddParameter("RequestContentLength", request.ContentLength);
                logContext.AddParameter("UserAgent", request.Headers.UserAgent.ToString());
            }

            if ((fields & RequestTrafficLoggingFields.RequestHeaders) != 0 &&
                options.SensitiveValueMode == SensitiveValueMode.Hash)
            {
                AddSensitiveHeaderHashes(logContext, request.Headers, options.SensitiveHeaders, "RequestHeader.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
        {
            ArgumentNullException.ThrowIfNull(logContext);

            RequestTrafficLoggingState? state = logContext.HttpContext.Features.Get<RequestTrafficLoggingState>();
            if (state is null)
            {
                return ValueTask.CompletedTask;
            }

            if ((state.Options.Fields & RequestTrafficLoggingFields.ResponseHeaders) != 0 &&
                state.Options.SensitiveValueMode == SensitiveValueMode.Hash)
            {
                AddSensitiveHeaderHashes(
                    logContext,
                    logContext.HttpContext.Response.Headers,
                    state.Options.SensitiveHeaders,
                    "ResponseHeader.");
            }

            return ValueTask.CompletedTask;
        }

        private static HttpLoggingFields ToHttpLoggingFields(RequestTrafficLoggingFields fields)
        {
            HttpLoggingFields result = HttpLoggingFields.None;

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                result |=
                    HttpLoggingFields.RequestProtocol |
                    HttpLoggingFields.RequestMethod |
                    HttpLoggingFields.RequestScheme |
                    HttpLoggingFields.RequestPath |
                    HttpLoggingFields.ResponseStatusCode;
            }

            if ((fields & RequestTrafficLoggingFields.Query) != 0)
            {
                result |= HttpLoggingFields.RequestQuery;
            }

            if ((fields & RequestTrafficLoggingFields.RequestHeaders) != 0)
            {
                result |= HttpLoggingFields.RequestHeaders;
            }

            if ((fields & RequestTrafficLoggingFields.ResponseHeaders) != 0)
            {
                result |= HttpLoggingFields.ResponseHeaders;
            }

            if ((fields & RequestTrafficLoggingFields.RequestBody) != 0)
            {
                result |= HttpLoggingFields.RequestBody;
            }

            if ((fields & RequestTrafficLoggingFields.ResponseBody) != 0)
            {
                result |= HttpLoggingFields.ResponseBody;
            }

            return result;
        }

        private static string ResolveTraceId(HttpContext context)
        {
            Activity? activity = Activity.Current;
            if (activity is not null && activity.TraceId != default)
            {
                return activity.TraceId.ToString();
            }

            return context.TraceIdentifier;
        }

        private static void AddSensitiveHeaderHashes(
            HttpLoggingInterceptorContext logContext,
            IHeaderDictionary headers,
            System.Collections.Generic.ISet<string> sensitiveHeaders,
            string propertyPrefix)
        {
            foreach (string headerName in sensitiveHeaders)
            {
                if (!headers.TryGetValue(headerName, out StringValues values) || values.Count == 0)
                {
                    continue;
                }

                string value = values.ToString();
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
                logContext.AddParameter(propertyPrefix + headerName + "Hash", "SHA256:" + Convert.ToHexString(hash));
            }
        }
    }

    internal sealed class RequestTrafficLoggingState
    {
        public RequestTrafficLoggingState(
            HttpLoggingInterceptorContext logContext,
            RequestTrafficLoggingOptions options,
            TimeProvider timeProvider,
            long startTimestamp,
            DateTimeOffset timestampUtc)
        {
            LogContext = logContext;
            Options = options;
            TimeProvider = timeProvider;
            StartTimestamp = startTimestamp;
            TimestampUtc = timestampUtc;
        }

        public HttpLoggingInterceptorContext LogContext { get; }

        public RequestTrafficLoggingOptions Options { get; }

        public TimeProvider TimeProvider { get; }

        public long StartTimestamp { get; }

        public DateTimeOffset TimestampUtc { get; }
    }
}
