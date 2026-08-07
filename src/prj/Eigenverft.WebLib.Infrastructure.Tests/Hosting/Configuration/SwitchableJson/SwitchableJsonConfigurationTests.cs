using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.SwitchableJson
{
    [TestClass]
    public sealed class SwitchableJsonConfigurationTests
    {
        [TestMethod]
        public void InitialSourceLoadsThroughNormalConfigurationStack()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", """
                {
                  "Timeout": 10,
                  "Nested": {
                    "Value": "A"
                  }
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            Assert.AreEqual("10", builder.Configuration["Timeout"]);
            Assert.AreEqual("A", builder.Configuration["Nested:Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void MissingOptionalInitialSourceStartsWithEmptyProvider()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile("settings", "missing.json", optional: true);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            Assert.IsNull(builder.Configuration["Missing"]);
            Assert.AreEqual(Path.Combine(directory.Path, "missing.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void MissingRequiredInitialSourceFailsAndDoesNotPublishRuntimeHandle()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            _ = Assert.ThrowsExactly<FileNotFoundException>(() =>
                builder.AddSwitchableJsonFile("settings", "missing.json"));

            Assert.IsFalse(builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(ISwitchableJsonConfiguration) &&
                descriptor.IsKeyedService &&
                Equals(descriptor.ServiceKey, "settings")));
        }

        [TestMethod]
        public void InvalidInitialJsonFailsDuringConfigurationRegistration()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("invalid.json", "{ invalid-json }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            _ = Assert.ThrowsExactly<FormatException>(() =>
                builder.AddSwitchableJsonFile("settings", "invalid.json"));
        }

        [TestMethod]
        public void ValuesThatLookEncodedRemainOrdinaryJsonStrings()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", """
                {
                  "Value": "enc:q7m2n4:aGVsbG8="
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile("settings", "A.json");

            Assert.AreEqual("enc:q7m2n4:aGVsbG8=", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void SwitchWithChangedDataPublishesOneConfigurationReloadAndUpdatesOptionsMonitor()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", """
                {
                  "Settings": {
                    "Timeout": 10
                  }
                }
                """);
            directory.Write("B.json", """
                {
                  "Settings": {
                    "Timeout": 20
                  }
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            builder.Services.Configure<TestSettings>(builder.Configuration.GetSection("Settings"));

            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            IOptionsMonitor<TestSettings> monitor = host.Services.GetRequiredService<IOptionsMonitor<TestSettings>>();
            int reloadCount = 0;
            int optionsChangeCount = 0;
            int lifecycleCount = 0;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));
            using IDisposable? optionsSubscription = monitor.OnChange((_, _) => Interlocked.Increment(ref optionsChangeCount));
            runtime.LifecycleChanged += (_, args) =>
            {
                Interlocked.Increment(ref lifecycleCount);
                Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchSucceeded, args.Kind);
            };

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.IsTrue(result.SourceChanged);
            Assert.IsTrue(result.ConfigurationChanged);
            Assert.AreEqual("20", builder.Configuration["Settings:Timeout"]);
            Assert.AreEqual(20, monitor.CurrentValue.Timeout);
            Assert.AreEqual(1, reloadCount);
            Assert.AreEqual(1, optionsChangeCount);
            Assert.AreEqual(1, lifecycleCount);
        }

        [TestMethod]
        public void SwitchWithLogicallyIdenticalDataChangesSourceWithoutConfigurationReload()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", """
                {
                  "A": 1,
                  "B": 2
                }
                """);
            directory.Write("B.json", """
                {
                  "B": 2,
                  "A": 1
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            int lifecycleCount = 0;
            SwitchableJsonConfigurationEventArgs? observedEvent = null;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));
            runtime.LifecycleChanged += (_, args) =>
            {
                observedEvent = args;
                Interlocked.Increment(ref lifecycleCount);
            };

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.IsTrue(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.AreEqual(Path.Combine(directory.Path, "B.json"), runtime.CurrentSourcePath);
            Assert.AreEqual(0, reloadCount);
            Assert.AreEqual(1, lifecycleCount);
            Assert.IsNotNull(observedEvent);
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchSucceeded, observedEvent.Kind);
            Assert.IsTrue(observedEvent.Result.SourceChanged);
            Assert.IsFalse(observedEvent.Result.ConfigurationChanged);
        }

        [TestMethod]
        public void SwitchToCurrentSourceIsObservableNoOpWithoutConfigurationReload()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            SwitchableJsonConfigurationEventKind? eventKind = null;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));
            runtime.LifecycleChanged += (_, args) => eventKind = args.Kind;

            SwitchableJsonSwitchResult result = runtime.TrySwitch("A.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.AlreadyCurrent, result.Status);
            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchAlreadyCurrent, eventKind);
            Assert.AreEqual(0, reloadCount);
        }

        [TestMethod]
        public void MissingRuntimeCandidateKeepsLastKnownGoodAndRaisesFailureEvent()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            SwitchableJsonConfigurationEventArgs? observedEvent = null;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));
            runtime.LifecycleChanged += (_, args) => observedEvent = args;

            SwitchableJsonSwitchResult result = runtime.TrySwitch("missing.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(SwitchableJsonFailureKind.SourceNotFound, result.FailureKind);
            Assert.IsFalse(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.AreEqual("A", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
            Assert.AreEqual(0, reloadCount);
            Assert.IsNotNull(observedEvent);
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchRejected, observedEvent.Kind);
        }

        [TestMethod]
        public void InvalidRuntimeCandidateKeepsLastKnownGood()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ invalid-json }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(SwitchableJsonFailureKind.InvalidJson, result.FailureKind);
            Assert.AreEqual("A", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void ThrowFailurePolicyPublishesFailureThenThrows()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                runtimeFailurePolicy: SwitchableJsonRuntimeFailurePolicy.Throw);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int lifecycleCount = 0;
            runtime.LifecycleChanged += (_, args) =>
            {
                Interlocked.Increment(ref lifecycleCount);
                Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchRejected, args.Kind);
            };

            _ = Assert.ThrowsExactly<FileNotFoundException>(() => runtime.TrySwitch("missing.json"));

            Assert.AreEqual(1, lifecycleCount);
            Assert.AreEqual("A", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void SwitchingAtoBtoAWorksInBothDirections()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult toB = runtime.TrySwitch("B.json");
            Assert.AreEqual("B", builder.Configuration["Value"]);
            SwitchableJsonSwitchResult toA = runtime.TrySwitch("A.json");

            Assert.IsTrue(toB.ConfigurationChanged);
            Assert.IsTrue(toA.ConfigurationChanged);
            Assert.AreEqual("A", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void MultipleNamedSourcesAreIndependentKeyedServices()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("FirstA.json", "{ \"First\": \"A\" }");
            directory.Write("FirstB.json", "{ \"First\": \"B\" }");
            directory.Write("Second.json", "{ \"Second\": \"S\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("first", "FirstA.json");
            builder.AddSwitchableJsonFile("second", "Second.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration first =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("first");
            ISwitchableJsonConfiguration second =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("second");

            _ = first.TrySwitch("FirstB.json");

            Assert.AreEqual("B", builder.Configuration["First"]);
            Assert.AreEqual("S", builder.Configuration["Second"]);
            Assert.AreEqual(Path.Combine(directory.Path, "FirstB.json"), first.CurrentSourcePath);
            Assert.AreEqual(Path.Combine(directory.Path, "Second.json"), second.CurrentSourcePath);
        }

        [TestMethod]
        public void DuplicateProviderNameIsRejectedDuringRegistration()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{}");
            directory.Write("B.json", "{}");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                builder.AddSwitchableJsonFile("settings", "B.json"));
        }

        [TestMethod]
        public async Task ConcurrentManualSwitchesAreSerializedAndFinishWithCoherentSnapshot()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Marker\": \"A\", \"Value\": \"1\" }");
            directory.Write("B.json", "{ \"Marker\": \"B\", \"Value\": \"2\" }");
            directory.Write("C.json", "{ \"Marker\": \"C\", \"Value\": \"3\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            var tasks = new List<Task>();
            for (int index = 0; index < 20; index++)
            {
                string target = index % 2 == 0 ? "B.json" : "C.json";
                tasks.Add(Task.Run(() => runtime.TrySwitch(target)));
            }

            await Task.WhenAll(tasks);

            string currentFile = Path.GetFileName(runtime.CurrentSourcePath);
            string expectedMarker = currentFile == "B.json" ? "B" : "C";
            string expectedValue = currentFile == "B.json" ? "2" : "3";
            Assert.AreEqual(expectedMarker, builder.Configuration["Marker"]);
            Assert.AreEqual(expectedValue, builder.Configuration["Value"]);
        }

        private static HostApplicationBuilder CreateBuilder(string contentRootPath)
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRootPath,
                DisableDefaults = true,
            });
        }

        private sealed class TestSettings
        {
            public int Timeout { get; set; }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "Eigenverft.WebLib.Infrastructure.SwitchableJson.Tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string fileName, string content)
            {
                string filePath = System.IO.Path.Combine(Path, fileName);
                File.WriteAllText(filePath, content);
                return filePath;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (DirectoryNotFoundException)
                {
                }
            }
        }
    }
}
