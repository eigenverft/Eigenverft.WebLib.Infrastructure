using System;
using System.IO;
using System.Linq;
using System.Threading;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetSwitchableJsonBindingTests
    {
        [TestMethod]
        public void BindingRequiresParticipantToMatchInitialSetValue()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("A.json", "{ \"Value\": \"A\" }");
            directory.Write("B.json", "{ \"Value\": \"B\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "A.json");
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration settings =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                coordinator.BindSwitchableJson(
                    settings,
                    value => value == "Stable" ? "B.json" : "A.json"));

            Assert.AreEqual(0, coordinator.BoundParticipantNames.Count);
            Assert.IsTrue(coordinator.IsConsistent);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.AreEqual("A", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void SuccessfulSetSwitchCoordinatesAllBoundSources()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("ProxyStable.json", "{ \"ProxyMarker\": \"Stable\" }");
            directory.Write("ProxyExperimental.json", "{ \"ProxyMarker\": \"Experimental\" }");
            directory.Write("FilterStable.json", "{ \"FilterMarker\": \"Stable\" }");
            directory.Write("FilterExperimental.json", "{ \"FilterMarker\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("proxy", "ProxyStable.json");
            builder.AddSwitchableJsonFile("filters", "FilterStable.json");
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration proxy =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("proxy");
            ISwitchableJsonConfiguration filters =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("filters");

            coordinator
                .BindSwitchableJson(proxy, value => $"Proxy{value}.json")
                .BindSwitchableJson(filters, value => $"Filter{value}.json");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.IsConsistent);
            Assert.IsTrue(coordinator.IsConsistent);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMarker"]);
            Assert.AreEqual("Experimental", builder.Configuration["FilterMarker"]);
            Assert.AreEqual(Path.Combine(directory.Path, "ProxyExperimental.json"), proxy.CurrentSourcePath);
            Assert.AreEqual(Path.Combine(directory.Path, "FilterExperimental.json"), filters.CurrentSourcePath);
            CollectionAssert.AreEqual(new[] { "proxy", "filters" }, coordinator.BoundParticipantNames.ToArray());
        }

        [TestMethod]
        public void RejectedPreparationLeavesEveryBoundSourceOnPreviousSet()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("ProxyStable.json", "{ \"ProxyMarker\": \"Stable\" }");
            directory.Write("ProxyExperimental.json", "{ \"ProxyMarker\": \"Experimental\" }");
            directory.Write("FilterStable.json", "{ \"FilterMarker\": \"Stable\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("proxy", "ProxyStable.json");
            builder.AddSwitchableJsonFile("filters", "FilterStable.json");
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration proxy =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("proxy");
            ISwitchableJsonConfiguration filters =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("filters");

            coordinator
                .BindSwitchableJson(proxy, value => $"Proxy{value}.json")
                .BindSwitchableJson(filters, value => $"Filter{value}.json");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, result.Status);
            Assert.AreEqual(ConfigurationSetSwitchFailureKind.ParticipantPreparationRejected, result.FailureKind);
            Assert.AreEqual("filters", result.FailedParticipantName);
            Assert.IsTrue(result.IsConsistent);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.AreEqual("Stable", builder.Configuration["ProxyMarker"]);
            Assert.AreEqual("Stable", builder.Configuration["FilterMarker"]);
            Assert.AreEqual(Path.Combine(directory.Path, "ProxyStable.json"), proxy.CurrentSourcePath);
            Assert.AreEqual(Path.Combine(directory.Path, "FilterStable.json"), filters.CurrentSourcePath);
        }

        [TestMethod]
        public void LogicalSetSwitchIsObservableEvenWhenEffectiveConfigurationDoesNotChange()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{ \"Value\": \"Same\" }");
            directory.Write("Experimental.json", "{ \"Value\": \"Same\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "Stable.json");
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration settings =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");
            coordinator.BindSwitchableJson(settings, value => $"{value}.json");
            int configurationReloadCount = 0;
            ConfigurationSetEventArgs? setEvent = null;
            using IDisposable subscription = ChangeToken.OnChange(
                ((IConfiguration)builder.Configuration).GetReloadToken,
                () => Interlocked.Increment(ref configurationReloadCount));
            coordinator.LifecycleChanged += (_, args) => setEvent = args;

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.AreEqual(Path.Combine(directory.Path, "Experimental.json"), settings.CurrentSourcePath);
            Assert.AreEqual(0, configurationReloadCount);
            Assert.IsNotNull(setEvent);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchSucceeded, setEvent.Kind);
        }

        [TestMethod]
        public void CommitRaceMarksSetInconsistentAndLaterSwitchCanReconcileIt()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("FirstStable.json", "{ \"First\": \"Stable\" }");
            directory.Write("FirstExperimental.json", "{ \"First\": \"Experimental\" }");
            directory.Write("SecondStable.json", "{ \"Second\": \"Stable\" }");
            directory.Write("SecondExperimental.json", "{ \"Second\": \"Experimental\" }");
            directory.Write("SecondExternal.json", "{ \"Second\": \"External\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("first", "FirstStable.json");
            builder.AddSwitchableJsonFile("second", "SecondStable.json");
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            using IHost host = builder.Build();
            ISwitchableJsonConfiguration first =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("first");
            ISwitchableJsonConfiguration second =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("second");

            coordinator
                .BindSwitchableJson(first, value => $"First{value}.json")
                .BindSwitchableJson(second, value => $"Second{value}.json");

            EventHandler<SwitchableJsonConfigurationEventArgs>? sabotage = null;
            sabotage = (_, args) =>
            {
                if (args.Kind == SwitchableJsonConfigurationEventKind.SwitchSucceeded &&
                    Path.GetFileName(args.CurrentSourcePath) == "FirstExperimental.json")
                {
                    _ = second.TrySwitch("SecondExternal.json");
                }
            };
            first.LifecycleChanged += sabotage;

            ConfigurationSetSwitchResult partial = coordinator.TrySwitch("Experimental");
            first.LifecycleChanged -= sabotage;

            Assert.AreEqual(ConfigurationSetSwitchStatus.PartiallyCommitted, partial.Status);
            Assert.AreEqual(ConfigurationSetSwitchFailureKind.PartialCommit, partial.FailureKind);
            Assert.AreEqual("second", partial.FailedParticipantName);
            Assert.IsFalse(partial.IsConsistent);
            Assert.IsFalse(coordinator.IsConsistent);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.AreEqual("Experimental", builder.Configuration["First"]);
            Assert.AreEqual("External", builder.Configuration["Second"]);

            ConfigurationSetSwitchResult reconciled = coordinator.TrySwitch("Stable");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, reconciled.Status);
            Assert.IsFalse(reconciled.ValueChanged);
            Assert.IsTrue(reconciled.IsConsistent);
            Assert.IsTrue(coordinator.IsConsistent);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.AreEqual("Stable", builder.Configuration["First"]);
            Assert.AreEqual("Stable", builder.Configuration["Second"]);
        }

        private static HostApplicationBuilder CreateBuilder(string contentRootPath)
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRootPath,
                DisableDefaults = true,
            });
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "Eigenverft.WebLib.Infrastructure.ConfigurationSets.Tests",
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
