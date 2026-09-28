using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.RequestTrafficLogging
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
                logContext.AddParameter("Request.Protocol", request.Protocol);
                logContext.AddParameter("Request.Method", request.Method);
                logContext.AddParameter("Request.Scheme", request.Scheme);
                logContext.AddParameter("Request.Host", request.Host.Value);
                logContext.AddParameter("Request.PathBase", request.PathBase);
                logContext.AddParameter("Request.Path", request.Path);
            }

            if ((fields & RequestTrafficLoggingFields.Query) != 0)
            {
                logContext.AddParameter("Request.QueryString", request.QueryString.Value);
            }

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                logContext.AddParameter("Request.UserAgent", request.Headers.UserAgent.ToString());
            }

            if ((fields & RequestTrafficLoggingFields.RequestHeaders) != 0)
            {
                AddHeaders(
                    logContext,
                    request.Headers,
                    options.RequestHeaders,
                    options,
                    "Request.Header.");
            }

            if ((fields & RequestTrafficLoggingFields.Core) != 0)
            {
                logContext.AddParameter("Request.Body.ContentType", request.ContentType);
                logContext.AddParameter("Request.Body.DeclaredLength", request.ContentLength);
            }

            if ((fields & RequestTrafficLoggingFields.RequestBody) != 0)
            {
                logContext.AddParameter(
                    "Request.Body.Truncated",
                    IsKnownBodyLargerThanCaptureLimit(request.ContentLength, options.RequestBodyLimit));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
        {
            ArgumentNullException.ThrowIfNull(logContext);

            // Response fields are emitted by the completion middleware so their order and final values are stable.
            return ValueTask.CompletedTask;
        }

        private static HttpLoggingFields ToHttpLoggingFields(RequestTrafficLoggingFields fields)
        {
            HttpLoggingFields result = HttpLoggingFields.None;

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

        internal static void AddHeaders(
            HttpLoggingInterceptorContext logContext,
            IHeaderDictionary headers,
            System.Collections.Generic.ISet<string> allowedHeaders,
            RequestTrafficLoggingOptions options,
            string propertyPrefix)
        {
            foreach (KeyValuePair<string, StringValues> header in headers)
            {
                string name = propertyPrefix + header.Key;
                bool sensitive = options.SensitiveHeaders.Contains(header.Key);
                bool include = options.HeaderCaptureMode == HeaderCaptureMode.AllRaw ||
                    (sensitive && options.SensitiveValueMode == SensitiveValueMode.Include) ||
                    (!sensitive && allowedHeaders.Contains(header.Key));

                if (include)
                {
                    AddHeaderValues(logContext, name, header.Value);
                }
                else
                {
                    logContext.AddParameter(name, "[Redacted]");
                }

                if (sensitive && options.SensitiveValueMode == SensitiveValueMode.Hash)
                {
                    AddHeaderHash(logContext, name, header.Value);
                }
            }
        }

        private static void AddHeaderValues(
            HttpLoggingInterceptorContext logContext,
            string propertyName,
            StringValues values)
        {
            if (values.Count <= 1)
            {
                logContext.AddParameter(
                    propertyName,
                    values.Count == 0 ? string.Empty : values[0] ?? string.Empty);
                return;
            }

            for (var index = 0; index < values.Count; index++)
            {
                logContext.AddParameter(
                    propertyName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                    values[index] ?? string.Empty);
            }
        }

        private static void AddHeaderHash(
            HttpLoggingInterceptorContext logContext,
            string propertyName,
            StringValues values)
        {
            string value = values.ToString();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            logContext.AddParameter(propertyName + ".Hash", "SHA256:" + Convert.ToHexString(hash));
        }

        private static bool? IsKnownBodyLargerThanCaptureLimit(long? declaredLength, int limit)
        {
            return declaredLength.HasValue ? declaredLength.Value > limit : null;
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
