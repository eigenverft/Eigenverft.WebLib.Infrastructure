using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.RequestTrafficLogging;

internal sealed class RequestTrafficLoggingTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    internal RequestTrafficLoggingTestHost(
        Action<RequestTrafficLoggingOptions>? configure = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        LoggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(minimumLevel);
            builder.AddProvider(LoggerProvider);
        });
        services.AddMetrics();
        services.AddRequestTrafficLogging(configure);
        var diagnosticListener = new DiagnosticListener("Eigenverft.WebLib.Infrastructure.Tests");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);

        _services = services.BuildServiceProvider();
    }

    internal RecordingLoggerProvider LoggerProvider { get; }

    internal RequestDelegate BuildPipeline(Action<IApplicationBuilder> configure, bool useTrafficLoggingTwice = false)
    {
        var app = new ApplicationBuilder(_services);
        app.UseRequestTrafficLogging();
        if (useTrafficLoggingTwice)
        {
            app.UseRequestTrafficLogging();
        }

        configure(app);
        return app.Build();
    }

    internal DefaultHttpContext CreateContext(string path = "/resource")
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _services,
            TraceIdentifier = "trace-test-123",
        };

        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test", 8443);
        context.Request.Path = path;
        context.Request.Protocol = "HTTP/1.1";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Connection.RemotePort = 52341;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Connection.LocalPort = 8443;
        context.Response.Body = new MemoryStream();
        return context;
    }

    internal CapturedLogRecord SingleTrafficRecord()
    {
        CapturedLogRecord[] records = LoggerProvider.Records
            .Where(static record => record.TryGetProperty("Event", out object? value) && Equals(value, "RequestTraffic"))
            .ToArray();

        Assert.AreEqual(1, records.Length, "Exactly one RequestTraffic record must be emitted per request.");
        return records[0];
    }

    public void Dispose()
    {
        _services.Dispose();
        LoggerProvider.Dispose();
    }
}

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<CapturedLogRecord> _records = new();

    internal IReadOnlyList<CapturedLogRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Add(CapturedLogRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly string _category;
        private readonly RecordingLoggerProvider _owner;

        internal RecordingLogger(string category, RecordingLoggerProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new List<KeyValuePair<string, object?>>();
            if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
            {
                properties.AddRange(structuredState);
            }

            _owner.Add(new CapturedLogRecord(
                _category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                properties));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class CapturedLogRecord
{
    private readonly IReadOnlyList<KeyValuePair<string, object?>> _properties;

    internal CapturedLogRecord(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception,
        IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        Category = category;
        Level = level;
        EventId = eventId;
        Message = message;
        Exception = exception;
        _properties = properties;
    }

    internal string Category { get; }

    internal LogLevel Level { get; }

    internal EventId EventId { get; }

    internal string Message { get; }

    internal Exception? Exception { get; }

    internal object? GetProperty(string name)
    {
        for (var i = _properties.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_properties[i].Key, name, StringComparison.Ordinal))
            {
                return _properties[i].Value;
            }
        }

        Assert.Fail($"Structured log property '{name}' was not present. Message: {Message}");
        return null;
    }

    internal bool TryGetProperty(string name, out object? value)
    {
        for (var i = _properties.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_properties[i].Key, name, StringComparison.Ordinal))
            {
                value = _properties[i].Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
