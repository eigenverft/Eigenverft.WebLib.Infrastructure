using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using BootstrapLoggerFacade = Eigenverft.WebLib.Infrastructure.Hosting.Logging.BootstrapLogger.BootstrapLogger;

namespace Eigenverft.WebLib.Infrastructure.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class BootstrapLoggerCharacterizationTests
    {
        private readonly List<IDisposable> _ownedLoggers = new();
        private Serilog.ILogger? _originalGlobalLogger;

        [TestInitialize]
        public void Initialize()
        {
            _originalGlobalLogger = Log.Logger;
            BootstrapLoggerFacade.ResetForTests();
        }

        [TestCleanup]
        public void Cleanup()
        {
            BootstrapLoggerFacade.ResetForTests();

            if (_originalGlobalLogger is not null)
            {
                Log.Logger = _originalGlobalLogger;
            }

            foreach (IDisposable logger in _ownedLoggers)
            {
                logger.Dispose();
            }

            _ownedLoggers.Clear();
        }

        [TestMethod]
        public void InfrastructureAssembly_HasNoCompileTimeSerilogReference()
        {
            string[] referencedAssemblyNames = typeof(BootstrapLoggerFacade)
                .Assembly
                .GetReferencedAssemblies()
                .Select(static assemblyName => assemblyName.Name ?? string.Empty)
                .Where(static assemblyName => assemblyName.StartsWith("Serilog", StringComparison.Ordinal))
                .ToArray();

            Assert.AreEqual(
                0,
                referencedAssemblyNames.Length,
                $"The production WebLib must discover optional Serilog support only through reflection and must not reference Serilog assemblies at compile time. Found: {string.Join(", ", referencedAssemblyNames)}");
        }

        [TestMethod]
        public void CreateLoggerFactory_UsesMicrosoftFallback_WhenGlobalLoggerIsSerilogDefaultSilentLogger()
        {
            Log.Logger = Logger.None;

            Type? consoleLoggingExtensions = Type.GetType(
                "Microsoft.Extensions.Logging.ConsoleLoggerExtensions, Microsoft.Extensions.Logging.Console",
                throwOnError: false);

            Assert.IsNotNull(
                consoleLoggingExtensions,
                "The Microsoft Console logging provider must be available in this characterization setup.");

            Assert.AreEqual(
                "Serilog.Core.Pipeline.SilentLogger",
                Log.Logger.GetType().FullName,
                "The characterization setup must use Serilog's default silent logger.");

            ILoggerFactory factory = BootstrapLoggerFacade.CreateLoggerFactory();

            StringAssert.StartsWith(
                factory.GetType().FullName,
                "Microsoft.Extensions.Logging.LoggerFactory",
                "Serilog's built-in default silent logger must be treated as not explicitly initialized so Console-capable Microsoft logging can be used instead.");
        }

        [TestMethod]
        public void CreateLoggerFactory_SelectsExplicitSerilogLogger_EvenWhenItHasNoSinks()
        {
            Logger explicitlyAssignedLogger = Own(new LoggerConfiguration().CreateLogger());
            Assert.IsFalse(ReferenceEquals(explicitlyAssignedLogger, Logger.None));

            Log.Logger = explicitlyAssignedLogger;
            ILoggerFactory factory = BootstrapLoggerFacade.CreateLoggerFactory();

            Assert.AreEqual(
                "Serilog.Extensions.Logging.SerilogLoggerFactory",
                factory.GetType().FullName,
                "Only Serilog's built-in default silent logger is rejected; an explicitly assigned logger is an intentional bootstrap channel even when it has no sinks.");
        }

        [TestMethod]
        public void CreatedBootstrapLogger_RemainsStableWhenGlobalSerilogLoggerIsReconfigured()
        {
            var firstSink = new CollectingSink();
            var secondSink = new CollectingSink();

            Logger firstLogger = Own(new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(firstSink)
                .CreateLogger());
            Logger secondLogger = Own(new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(secondSink)
                .CreateLogger());

            Log.Logger = firstLogger;
            Microsoft.Extensions.Logging.ILogger bootstrapLogger =
                BootstrapLoggerFacade.CreateLogger("BootstrapLoggerCharacterization");

            Log.Logger = secondLogger;
            bootstrapLogger.LogInformation("characterization-event");

            Assert.AreEqual(1, firstSink.Events.Count);
            Assert.AreEqual(0, secondSink.Events.Count);
            Assert.AreEqual(
                "characterization-event",
                firstSink.Events.Single().RenderMessage());
        }

        private Logger Own(Logger logger)
        {
            _ownedLoggers.Add(logger);
            return logger;
        }

        private sealed class CollectingSink : ILogEventSink
        {
            private readonly ConcurrentQueue<LogEvent> _events = new();

            internal IReadOnlyCollection<LogEvent> Events => _events.ToArray();

            public void Emit(LogEvent logEvent)
            {
                ArgumentNullException.ThrowIfNull(logEvent);
                _events.Enqueue(logEvent);
            }
        }
    }
}
