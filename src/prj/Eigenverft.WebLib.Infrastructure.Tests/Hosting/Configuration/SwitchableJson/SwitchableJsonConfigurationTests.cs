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
            IConfigurationBuilder configurationBuilder = builder.Configuration;
            int sourceCountBefore = configurationBuilder.Sources.Count;

            _ = Assert.ThrowsExactly<FileNotFoundException>(() =>
                builder.AddSwitchableJsonFile("settings", "missing.json"));

            Assert.AreEqual(sourceCountBefore, configurationBuilder.Sources.Count);
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
            SwitchableJsonConfigurationEventKind? lifecycleKind = null;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));
            using IDisposable? optionsSubscription = monitor.OnChange((_, _) => Interlocked.Increment(ref optionsChangeCount));
            runtime.LifecycleChanged += (_, args) =>
            {
                lifecycleKind = args.Kind;
                Interlocked.Increment(ref lifecycleCount);
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
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchSucceeded, lifecycleKind);
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
            Assert.IsTrue(observedEvent.SourceChanged);
            Assert.IsFalse(observedEvent.ConfigurationChanged);
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
            SwitchableJsonConfigurationEventKind? lifecycleKind = null;
            runtime.LifecycleChanged += (_, args) =>
            {
                lifecycleKind = args.Kind;
                Interlocked.Increment(ref lifecycleCount);
            };

            _ = Assert.ThrowsExactly<FileNotFoundException>(() => runtime.TrySwitch("missing.json"));

            Assert.AreEqual(1, lifecycleCount);
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchRejected, lifecycleKind);
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

        [TestMethod]
        public async Task ActiveSourceEffectiveChangeReloadsConfigurationAndRaisesLifecycleEvent()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{ \"Value\": \"B\" }"));

            Assert.AreEqual("B", builder.Configuration["Value"]);
            Assert.IsFalse(observedEvent.SourceChanged);
            Assert.IsTrue(observedEvent.ConfigurationChanged);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), observedEvent.CurrentSourcePath);
            Assert.AreEqual(1, reloadCount);
        }

        [TestMethod]
        public async Task ActiveSourceLogicalNoOpRaisesLifecycleWithoutConfigurationReload()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"A\": 1, \"B\": 2 }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{\n  \"B\": 2,\n  \"A\": 1\n}"));

            Assert.IsFalse(observedEvent.SourceChanged);
            Assert.IsFalse(observedEvent.ConfigurationChanged);
            Assert.AreEqual("1", builder.Configuration["A"]);
            Assert.AreEqual("2", builder.Configuration["B"]);
            Assert.AreEqual(0, reloadCount);
        }

        [TestMethod]
        public async Task InvalidActiveSourceReloadKeepsLastKnownGoodAndRaisesFailureEvent()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int reloadCount = 0;
            using IDisposable reloadSubscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected,
                () => directory.Write("A.json", "{ invalid-json }"));

            Assert.AreEqual("A", builder.Configuration["Value"]);
            Assert.AreEqual(SwitchableJsonFailureKind.InvalidJson, observedEvent.FailureKind);
            Assert.IsFalse(observedEvent.SourceChanged);
            Assert.IsFalse(observedEvent.ConfigurationChanged);
            Assert.AreEqual(0, reloadCount);
        }

        [TestMethod]
        public async Task WatcherFollowsSuccessfulSourceSwitchAndIgnoresOldSource()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int activeReloadEvents = 0;
            runtime.LifecycleChanged += (_, args) =>
            {
                if (args.Kind is SwitchableJsonConfigurationEventKind.ActiveSourceReloaded or
                    SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected)
                {
                    Interlocked.Increment(ref activeReloadEvents);
                }
            };

            _ = runtime.TrySwitch("B.json");
            directory.Write("A.json", "{ \"Value\": \"A2\" }");
            await Task.Delay(300);

            Assert.AreEqual("B", builder.Configuration["Value"]);
            Assert.AreEqual(0, activeReloadEvents);

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("B.json", "{ \"Value\": \"B2\" }"));

            Assert.IsTrue(observedEvent.ConfigurationChanged);
            Assert.AreEqual("B2", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "B.json"), runtime.CurrentSourcePath);

            _ = runtime.TrySwitch("A.json");
            SwitchableJsonConfigurationEventArgs backOnA = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{ \"Value\": \"A3\" }"));

            Assert.IsTrue(backOnA.ConfigurationChanged);
            Assert.AreEqual("A3", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public async Task ReloadOnChangeFalseDoesNotObservePhysicalFileChanges()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json", reloadOnChange: false);
            using IHost host = builder.Build();

            directory.Write("A.json", "{ \"Value\": \"B\" }");
            await Task.Delay(300);

            Assert.AreEqual("A", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task OptionalMissingInitialSourceCanBecomeActiveWhenFileIsCreated()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                optional: true,
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            Assert.IsNull(builder.Configuration["Value"]);

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{ \"Value\": \"Created\" }"));

            Assert.IsTrue(observedEvent.ConfigurationChanged);
            Assert.AreEqual("Created", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "A.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public async Task StaleOldSourceNotificationCannotOverwriteNewSourceAfterSwitch()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 100);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            directory.Write("A.json", "{ \"Value\": \"A2\" }");
            SwitchableJsonSwitchResult switchResult = runtime.TrySwitch("B.json");
            await Task.Delay(400);

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, switchResult.Status);
            Assert.AreEqual(Path.Combine(directory.Path, "B.json"), runtime.CurrentSourcePath);
            Assert.AreEqual("B", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task QuickActiveSourceWritesConvergeOnCompleteLatestSnapshot()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Marker\": \"Initial\", \"Value\": \"0\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 100);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonConfigurationEventArgs observedEvent = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () =>
                {
                    directory.Write("A.json", "{ \"Marker\": \"First\", \"Value\": \"1\" }");
                    directory.Write("A.json", "{ \"Marker\": \"Latest\", \"Value\": \"2\" }");
                });

            Assert.IsTrue(observedEvent.ConfigurationChanged);
            Assert.AreEqual("Latest", builder.Configuration["Marker"]);
            Assert.AreEqual("2", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task HostDisposalDisposesWatcherAndRuntimeProvider()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int lifecycleCount = 0;
            runtime.LifecycleChanged += (_, _) => Interlocked.Increment(ref lifecycleCount);

            host.Dispose();
            directory.Write("A.json", "{ \"Value\": \"B\" }");
            await Task.Delay(300);

            Assert.AreEqual(0, lifecycleCount);
            _ = Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.TrySwitch("A.json"));
        }

        [TestMethod]
        public async Task LogicallyIdenticalSwitchStillMovesWatcherToNewSource()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"Same\" }");
            directory.Write("B.json", "{ \"Value\": \"Same\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonSwitchResult switchResult = runtime.TrySwitch("B.json");
            Assert.IsFalse(switchResult.ConfigurationChanged);
            Assert.AreEqual(Path.Combine(directory.Path, "B.json"), runtime.CurrentSourcePath);

            directory.Write("A.json", "{ \"Value\": \"OldSourceChanged\" }");
            await Task.Delay(300);
            Assert.AreEqual("Same", builder.Configuration["Value"]);

            SwitchableJsonConfigurationEventArgs reloaded = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("B.json", "{ \"Value\": \"NewSourceChanged\" }"));

            Assert.IsTrue(reloaded.ConfigurationChanged);
            Assert.AreEqual("NewSourceChanged", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task OptionalMissingNestedSourceCanAppearLaterWithoutProfileSemantics()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                Path.Combine("Arbitrary", "Later", "A.json"),
                optional: true,
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            string nestedDirectory = Path.Combine(directory.Path, "Arbitrary", "Later");
            string nestedFile = Path.Combine(nestedDirectory, "A.json");
            Assert.IsNull(builder.Configuration["Value"]);

            SwitchableJsonConfigurationEventArgs reloaded = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () =>
                {
                    Directory.CreateDirectory(nestedDirectory);
                    File.WriteAllText(nestedFile, "{ \"Value\": \"Appeared\" }");
                });

            Assert.IsTrue(reloaded.ConfigurationChanged);
            Assert.AreEqual("Appeared", builder.Configuration["Value"]);
            Assert.AreEqual(nestedFile, runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void ThrowingLifecycleObserverCannotChangeManualSwitchOutcomeOrBlockOtherObservers()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            int laterObserverCount = 0;

            runtime.LifecycleChanged += (_, _) => throw new InvalidOperationException("observer failure");
            runtime.LifecycleChanged += (_, _) => Interlocked.Increment(ref laterObserverCount);

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("B", builder.Configuration["Value"]);
            Assert.AreEqual(1, laterObserverCount);
        }

        [TestMethod]
        public async Task ThrowingLifecycleObserverCannotBreakWatcherReloads()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            runtime.LifecycleChanged += (_, _) => throw new InvalidOperationException("observer failure");

            _ = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{ \"Value\": \"B\" }"));
            Assert.AreEqual("B", builder.Configuration["Value"]);

            _ = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write("A.json", "{ \"Value\": \"C\" }"));
            Assert.AreEqual("C", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task DeletedActiveSourceKeepsLastKnownGoodAndReloadsAfterRecreation()
        {
            using var directory = new TemporaryDirectory();
            string sourcePath = directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                "A.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonConfigurationEventArgs rejected = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected,
                () => File.Delete(sourcePath));

            Assert.AreEqual(SwitchableJsonFailureKind.SourceNotFound, rejected.FailureKind);
            Assert.AreEqual("A", builder.Configuration["Value"]);

            SwitchableJsonConfigurationEventArgs restored = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => File.WriteAllText(sourcePath, "{ \"Value\": \"Restored\" }"));

            Assert.IsTrue(restored.ConfigurationChanged);
            Assert.AreEqual("Restored", builder.Configuration["Value"]);
        }

        [TestMethod]
        public async Task ExplicitDotPrefixedSourceIsWatched()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(".settings.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile(
                "settings",
                ".settings.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 50);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            SwitchableJsonConfigurationEventArgs reloaded = await WaitForLifecycleAsync(
                runtime,
                SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                () => directory.Write(".settings.json", "{ \"Value\": \"B\" }"));

            Assert.IsTrue(reloaded.ConfigurationChanged);
            Assert.AreEqual("B", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void ConfigurationSourcesRebuildKeepsRuntimeHandleOperational()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationBuilder configurationBuilder = builder.Configuration;

            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Temporary"] = "remove-me" });
            IConfigurationSource removableSource = configurationBuilder.Sources.Last();
            builder.AddSwitchableJsonFile("settings", "A.json");

            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            Assert.IsTrue(configurationBuilder.Sources.Remove(removableSource));
            Assert.AreEqual("A", builder.Configuration["Value"]);

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("B", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "B.json"), runtime.CurrentSourcePath);
        }

        [TestMethod]
        public void FailedSecondRegistrationCannotDisposePreviouslyRegisteredRuntime()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            builder.AddSwitchableJsonFile("first", "A.json");
            _ = Assert.ThrowsExactly<FileNotFoundException>(() =>
                builder.AddSwitchableJsonFile("second", "missing.json"));

            using IHost host = builder.Build();
            ISwitchableJsonConfiguration first =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("first");

            SwitchableJsonSwitchResult result = first.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("B", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void ThrowingConfigurationChangeObserverCannotTurnCommittedSwitchIntoFailure()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            SwitchableJsonConfigurationEventKind? lifecycleKind = null;
            runtime.LifecycleChanged += (_, args) => lifecycleKind = args.Kind;
            using IDisposable subscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => throw new InvalidOperationException("configuration observer failure"));

            SwitchableJsonSwitchResult result = runtime.TrySwitch("B.json");

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.IsTrue(result.ConfigurationChanged);
            Assert.AreEqual("B", builder.Configuration["Value"]);
            Assert.AreEqual(SwitchableJsonConfigurationEventKind.SwitchSucceeded, lifecycleKind);
        }

        [TestMethod]
        public async Task ConfigurationChangeObserverCanWaitForConcurrentConfigurationReadWithoutDeadlock()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            var callbackRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () =>
                {
                    try
                    {
                        Task<string?> readTask = Task.Run(() => builder.Configuration["Value"]);
                        if (!readTask.Wait(TimeSpan.FromSeconds(1)))
                        {
                            throw new TimeoutException("Configuration read was blocked by the reload callback path.");
                        }

                        callbackRead.TrySetResult(readTask.Result);
                    }
                    catch (Exception exception)
                    {
                        callbackRead.TrySetException(exception);
                    }
                });

            Task<SwitchableJsonSwitchResult> switchTask = Task.Run(() => runtime.TrySwitch("B.json"));
            SwitchableJsonSwitchResult result = await switchTask.WaitAsync(TimeSpan.FromSeconds(3));
            string? observedValue = await callbackRead.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.AreEqual(SwitchableJsonSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("B", observedValue);
            Assert.AreEqual("B", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void ExplicitConfigurationRootReloadKeepsFrameworkReloadSemantics()
        {
            using var directory = new TemporaryDirectory();
            string sourcePath = directory.Write("A.json", "{ \"Value\": \"A\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            using IHost host = builder.Build();
            int reloadCount = 0;
            int lifecycleCount = 0;
            ISwitchableJsonConfiguration runtime =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            runtime.LifecycleChanged += (_, _) => Interlocked.Increment(ref lifecycleCount);
            using IDisposable subscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref reloadCount));

            ((IConfigurationRoot)builder.Configuration).Reload();

            Assert.AreEqual(1, reloadCount);
            Assert.AreEqual(0, lifecycleCount);
            Assert.AreEqual("A", builder.Configuration["Value"]);

            File.WriteAllText(sourcePath, "{ invalid-json }");
            _ = Assert.ThrowsExactly<FormatException>(() => ((IConfigurationRoot)builder.Configuration).Reload());

            Assert.AreEqual("A", builder.Configuration["Value"]);
            Assert.AreEqual(1, reloadCount);
            Assert.AreEqual(0, lifecycleCount);
        }

        private static async Task<SwitchableJsonConfigurationEventArgs> WaitForLifecycleAsync(
            ISwitchableJsonConfiguration runtime,
            SwitchableJsonConfigurationEventKind kind,
            Action trigger)
        {
            var completion = new TaskCompletionSource<SwitchableJsonConfigurationEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<SwitchableJsonConfigurationEventArgs>? handler = null;
            handler = (_, args) =>
            {
                if (args.Kind != kind)
                {
                    return;
                }

                runtime.LifecycleChanged -= handler;
                completion.TrySetResult(args);
            };

            runtime.LifecycleChanged += handler;

            try
            {
                trigger();
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                runtime.LifecycleChanged -= handler;
            }
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
