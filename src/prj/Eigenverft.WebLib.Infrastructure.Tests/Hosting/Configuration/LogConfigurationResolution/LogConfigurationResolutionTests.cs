using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.LogConfigurationResolution;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class LogConfigurationResolutionTests
{
    [TestMethod]
    public void LogConfigurationResolution_LogsProviderOrderAndCollidingKeyResolution()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SharedKey"] = "__value_from_provider_0__",
                ["FirstOnly"] = "__value_only_in_provider_0__",
            });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SharedKey"] = "__value_from_provider_1__",
                ["SecondOnly"] = "__value_only_in_provider_1__",
            });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SharedKey"] = "__value_from_provider_2__",
                ["ThirdOnly"] = "__value_only_in_provider_2__",
            });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SharedKey"] = "__value_from_provider_3__",
                ["FourthOnly"] = "__value_only_in_provider_3__",
            });

        var logger = new RecordingLogger();

        WebApplicationBuilder result = builder.LogConfigurationResolution(logger);

        Assert.AreSame(builder, result);
        Assert.AreEqual("__value_from_provider_3__", builder.Configuration["SharedKey"]);
        Assert.AreEqual(3, logger.Entries.Count);

        Assert.AreEqual(LogLevel.Information, logger.Entries[0].Level);
        Assert.AreEqual("Config precedence (highest -> lowest): {Resolution}", logger.Entries[0].MessageTemplate);
        StringAssert.Contains(
            logger.Entries[0].Message,
            "Config precedence (highest -> lowest): memory -> memory -> memory -> memory");

        Assert.AreEqual(LogLevel.Warning, logger.Entries[1].Level);
        Assert.AreEqual("Configuration key collisions found: {Count}.", logger.Entries[1].MessageTemplate);
        Assert.AreEqual("Configuration key collisions found: 1.", logger.Entries[1].Message);

        Assert.AreEqual(LogLevel.Warning, logger.Entries[2].Level);
        Assert.AreEqual(
            "Config key collision on {Key}; winner {Winner} shadows {Shadowed}",
            logger.Entries[2].MessageTemplate);
        Assert.AreEqual(
            "Config key collision on SharedKey; winner memory shadows memory shadows memory shadows memory",
            logger.Entries[2].Message);

        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_only_in_provider_0__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_only_in_provider_1__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_only_in_provider_2__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_only_in_provider_3__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_from_provider_0__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_from_provider_1__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_from_provider_2__", StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("__value_from_provider_3__", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LogConfigurationResolution_WarnsWhenProviderCannotBeInspected()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["SharedKey"] = "memory-value" });
        ((IConfigurationBuilder)builder.Configuration).Add(new OpaqueConfigurationSource("SharedKey", "opaque-value"));

        var logger = new RecordingLogger();

        builder.LogConfigurationResolution(logger);

        Assert.AreEqual("opaque-value", builder.Configuration["SharedKey"]);
        Assert.IsTrue(logger.Entries.Any(entry =>
            entry.Message == "Configuration collision scan incomplete; provider OpaqueConfigurationProvider could not be inspected."));
        Assert.IsFalse(logger.Entries.Any(entry =>
            entry.Message.StartsWith("Configuration key collisions found:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LogProviderOrder_UsesJsonFileNameWithoutFullPath()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });

        builder.Configuration.Sources.Clear();
        string fullPath = Path.Combine(
            Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\",
            "very",
            "long",
            "deployment",
            "path",
            "ReverseProxySettings.Production.json");
        ((IConfigurationBuilder)builder.Configuration).Add(
            new TestJsonConfigurationSource(fullPath, new Dictionary<string, string?>()));

        var logger = new RecordingLogger();

        ConfigurationPrecedenceDiagnosticsExtensions.LogProviderOrder(builder.Configuration, logger);

        Assert.AreEqual(
            "Config precedence (highest -> lowest): json:ReverseProxySettings.Production.json",
            logger.Entries.Single().Message);
        Assert.IsFalse(logger.Entries.Single().Message.Contains("deployment", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string? messageTemplate = (state as IEnumerable<KeyValuePair<string, object?>>)?
                .FirstOrDefault(pair => pair.Key == "{OriginalFormat}")
                .Value as string;

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), messageTemplate));
        }
    }

    private sealed class OpaqueConfigurationSource(string key, string value) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new OpaqueConfigurationProvider(key, value);
        }
    }

    private sealed class OpaqueConfigurationProvider(string key, string value) : IConfigurationProvider
    {
        public bool TryGet(string requestedKey, out string? result)
        {
            if (string.Equals(requestedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                result = value;
                return true;
            }

            result = null;
            return false;
        }

        public void Set(string requestedKey, string? newValue)
        {
        }

        public IChangeToken GetReloadToken()
        {
            return new CancellationChangeToken(CancellationToken.None);
        }

        public void Load()
        {
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            return parentPath is null ? earlierKeys.Concat(new[] { key }).OrderBy(item => item, StringComparer.OrdinalIgnoreCase) : earlierKeys;
        }
    }

    private sealed class TestJsonConfigurationSource(
        string path,
        IReadOnlyDictionary<string, string?> values) : JsonConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Path = path;
            return new TestDataConfigurationProvider(values);
        }
    }

    private sealed class TestDataConfigurationProvider(IReadOnlyDictionary<string, string?> values) : ConfigurationProvider
    {
        public override void Load()
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, string? MessageTemplate);
}
