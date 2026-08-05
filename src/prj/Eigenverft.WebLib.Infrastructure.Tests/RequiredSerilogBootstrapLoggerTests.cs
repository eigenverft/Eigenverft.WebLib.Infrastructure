using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;

using BootstrapLoggerFacade = Eigenverft.WebLib.Infrastructure.Hosting.Logging.BootstrapLogger.BootstrapLogger;
using RequiredBootstrapLogger = Eigenverft.WebLib.Infrastructure.Hosting.Logging.BootstrapLogger.BootstrapLogger<Eigenverft.WebLib.Infrastructure.Tests.RequiredSerilogBootstrapLoggerTests>;

namespace Eigenverft.WebLib.Infrastructure.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class RequiredSerilogBootstrapLoggerTests
    {
        private readonly List<string> _temporaryDirectories = new();
        private Serilog.ILogger? _originalGlobalLogger;

        [TestInitialize]
        public void Initialize()
        {
            _originalGlobalLogger = Log.Logger;
            BootstrapLoggerFacade.ResetForTests();
            Log.Logger = Logger.None;
        }

        [TestCleanup]
        public void Cleanup()
        {
            BootstrapLoggerFacade.ResetForTests();

            if (_originalGlobalLogger is not null)
            {
                Log.Logger = _originalGlobalLogger;
            }

            foreach (string directory in _temporaryDirectories)
            {
                TryDeleteDirectory(directory);
            }

            _temporaryDirectories.Clear();
        }

        [TestMethod]
        public void CreateRequiredSerilogLogger_UsesDefaultPathAndSerilogSection()
        {
            string baseDirectory = CreateTemporaryDirectory();
            string settingsDirectory = Path.Combine(baseDirectory, "AppSettings");
            Directory.CreateDirectory(settingsDirectory);
            string logFile = Path.Combine(baseDirectory, "bootstrap.log");
            File.WriteAllText(
                Path.Combine(settingsDirectory, "BootstrapLoggerSettings.json"),
                CreateFileSinkConfiguration("Serilog", logFile, "Information"));

            ILogger<RequiredSerilogBootstrapLoggerTests> logger =
                RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                    baseDirectory: baseDirectory);

            logger.LogInformation("required-default-bootstrap-event");
            BootstrapLoggerFacade.ResetForTests();

            string output = File.ReadAllText(logFile);
            StringAssert.Contains(output, "required-default-bootstrap-event");
        }

        [TestMethod]
        public void CreateRequiredSerilogLogger_UsesCustomSectionAndReusesMatchingIdentity()
        {
            string baseDirectory = CreateTemporaryDirectory();
            string configurationFile = Path.Combine(baseDirectory, "custom-bootstrap.json");
            string logFile = Path.Combine(baseDirectory, "custom-bootstrap.log");
            File.WriteAllText(
                configurationFile,
                CreateFileSinkConfiguration("BootstrapSerilog", logFile, "Information"));

            ILogger<RequiredSerilogBootstrapLoggerTests> genericLogger =
                RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                    configurationFile: configurationFile,
                    sectionName: "BootstrapSerilog");
            Microsoft.Extensions.Logging.ILogger namedLogger =
                BootstrapLoggerFacade.CreateRequiredSerilogLogger(
                    "RequiredCategory",
                    configurationFile: configurationFile,
                    sectionName: "BootstrapSerilog");

            genericLogger.LogInformation("generic-required-event");
            namedLogger.LogInformation("named-required-event");
            BootstrapLoggerFacade.ResetForTests();

            string output = File.ReadAllText(logFile);
            StringAssert.Contains(output, "generic-required-event");
            StringAssert.Contains(output, "named-required-event");
        }

        [TestMethod]
        public void CreateRequiredSerilogLogger_RejectsDifferentRequiredIdentityAfterInitialization()
        {
            string baseDirectory = CreateTemporaryDirectory();
            string configurationFile = Path.Combine(baseDirectory, "bootstrap.json");
            string logFile = Path.Combine(baseDirectory, "bootstrap.log");
            File.WriteAllText(
                configurationFile,
                CreateFileSinkConfiguration("Serilog", logFile, "Information"));

            _ = RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                configurationFile: configurationFile);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                    configurationFile: configurationFile,
                    reloadOnChange: true));

            StringAssert.Contains(exception.Message, "first BootstrapLogger initialization");
        }

        [TestMethod]
        public void CreateRequiredSerilogLogger_RejectsAutomaticBackendInitializedFirst()
        {
            _ = BootstrapLoggerFacade.CreateLoggerFactory();

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => RequiredBootstrapLogger.CreateRequiredSerilogLogger());

            StringAssert.Contains(exception.Message, "first BootstrapLogger initialization");
            StringAssert.Contains(exception.Message, "Microsoft");
        }

        [TestMethod]
        public void CreateRequiredSerilogLogger_ReportsMissingConfiguredSection()
        {
            string baseDirectory = CreateTemporaryDirectory();
            string configurationFile = Path.Combine(baseDirectory, "bootstrap.json");
            File.WriteAllText(configurationFile, "{ \"Other\": { \"Value\": true } }");

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                    configurationFile: configurationFile));

            StringAssert.Contains(exception.Message, "does not contain the section 'Serilog'");
        }

        [TestMethod]
        [Timeout(20_000)]
        public void CreateRequiredSerilogLogger_ReloadOnChangeUpdatesExistingMinimumLevel()
        {
            string baseDirectory = CreateTemporaryDirectory();
            string configurationFile = Path.Combine(baseDirectory, "reload-bootstrap.json");
            string logFile = Path.Combine(baseDirectory, "reload-bootstrap.log");
            File.WriteAllText(
                configurationFile,
                CreateFileSinkConfiguration("Serilog", logFile, "Error"));

            ILogger<RequiredSerilogBootstrapLoggerTests> logger =
                RequiredBootstrapLogger.CreateRequiredSerilogLogger(
                    configurationFile: configurationFile,
                    reloadOnChange: true);

            logger.LogInformation("before-level-reload");
            File.WriteAllText(
                configurationFile,
                CreateFileSinkConfiguration("Serilog", logFile, "Information"));

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            bool observed = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                logger.LogInformation("after-level-reload");
                Thread.Sleep(200);

                if (File.Exists(logFile) &&
                    ReadAllTextShared(logFile).Contains(
                        "after-level-reload",
                        StringComparison.Ordinal))
                {
                    observed = true;
                    break;
                }
            }

            BootstrapLoggerFacade.ResetForTests();

            Assert.IsTrue(
                observed,
                "reloadOnChange did not update Serilog's existing minimum level within the timeout.");
            string output = File.ReadAllText(logFile);
            Assert.IsFalse(
                output.Contains("before-level-reload", StringComparison.Ordinal),
                "The Information event emitted before the configured minimum level changed must remain filtered.");
        }

        private string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "Eigenverft.WebLib.Infrastructure.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _temporaryDirectories.Add(directory);
            return directory;
        }

        private static string CreateFileSinkConfiguration(
            string sectionName,
            string logFile,
            string minimumLevel)
        {
            string serializedLogFile = JsonSerializer.Serialize(logFile);

            return $$"""
            {
              "{{sectionName}}": {
                "Using": [ "Serilog.Sinks.File" ],
                "MinimumLevel": "{{minimumLevel}}",
                "WriteTo": [
                  {
                    "Name": "File",
                    "Args": {
                      "path": {{serializedLogFile}},
                      "shared": true
                    }
                  }
                ]
              }
            }
            """;
        }

        private static string ReadAllTextShared(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Test cleanup must not hide the primary assertion result.
            }
        }
    }
}
