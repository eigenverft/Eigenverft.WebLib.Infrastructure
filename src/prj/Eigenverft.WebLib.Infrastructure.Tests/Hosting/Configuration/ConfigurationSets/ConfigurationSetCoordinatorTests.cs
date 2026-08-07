using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetCoordinatorTests
    {
        [TestMethod]
        public void DefinitionRequiresInitialValueToBeAllowed()
        {
            _ = Assert.ThrowsExactly<ArgumentException>(() =>
                new ConfigurationSetDefinition(
                    "ProxySet",
                    "Experimental",
                    ["Stable", "Next"]));
        }

        [TestMethod]
        public void DefinitionRejectsDuplicateAllowedValues()
        {
            _ = Assert.ThrowsExactly<ArgumentException>(() =>
                new ConfigurationSetDefinition(
                    "ProxySet",
                    "Stable",
                    ["Stable", "Stable"]));
        }

        [TestMethod]
        public void RegistrationReturnsSameInstanceThatIsAvailableThroughKeyedDi()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator registered = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);

            using IHost host = builder.Build();
            IConfigurationSetCoordinator resolved =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");

            Assert.AreSame(registered, resolved);
            Assert.AreEqual("ProxySet", resolved.Name);
            Assert.AreEqual("Stable", resolved.ActiveValue);
            CollectionAssert.AreEqual(
                new[] { "Stable", "Next", "Experimental" },
                resolved.AllowedValues.ToArray());
        }

        [TestMethod]
        public void MultipleNamedSetsAreIndependentAndCanBeActiveSimultaneously()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator environmentSet = builder.AddConfigurationSetCoordinator(
                "EnvironmentSet",
                "Development",
                ["Development", "Production"]);
            IConfigurationSetCoordinator proxySet = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);
            IConfigurationSetCoordinator buildSet = builder.AddConfigurationSetCoordinator(
                "BuildSet",
                "Stable",
                ["Stable", "Candidate"]);

            using IHost host = builder.Build();

            _ = environmentSet.TrySwitch("Production");
            _ = proxySet.TrySwitch("Experimental");
            _ = buildSet.TrySwitch("Candidate");

            Assert.AreEqual("Production", environmentSet.ActiveValue);
            Assert.AreEqual("Experimental", proxySet.ActiveValue);
            Assert.AreEqual("Candidate", buildSet.ActiveValue);
            Assert.AreSame(
                proxySet,
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet"));
        }

        [TestMethod]
        public void SuccessfulSwitchChangesValueAndPublishesLifecycle()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);
            ConfigurationSetEventArgs? observed = null;
            coordinator.LifecycleChanged += (_, args) => observed = args;

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.ValueChanged);
            Assert.AreEqual("Stable", result.PreviousValue);
            Assert.AreEqual("Experimental", result.RequestedValue);
            Assert.AreEqual("Experimental", result.ActiveValue);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.IsNotNull(observed);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchSucceeded, observed.Kind);
            Assert.AreSame(result, observed.Result);
        }

        [TestMethod]
        public void AlreadyActiveValueIsObservableNoOp()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            ConfigurationSetEventArgs? observed = null;
            coordinator.LifecycleChanged += (_, args) => observed = args;

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Stable");

            Assert.AreEqual(ConfigurationSetSwitchStatus.AlreadyActive, result.Status);
            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.ValueChanged);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.IsNotNull(observed);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchAlreadyActive, observed.Kind);
        }

        [TestMethod]
        public void DisallowedValueIsRejectedAndObservableWithoutChangingActiveValue()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next"]);
            ConfigurationSetEventArgs? observed = null;
            coordinator.LifecycleChanged += (_, args) => observed = args;

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, result.Status);
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ValueChanged);
            Assert.AreEqual(ConfigurationSetSwitchFailureKind.ValueNotAllowed, result.FailureKind);
            Assert.AreEqual("Stable", result.ActiveValue);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.IsNotNull(observed);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchRejected, observed.Kind);
        }

        [TestMethod]
        public void ThrowingLifecycleObserverCannotChangeOutcomeOrBlockLaterObservers()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            int laterObserverCount = 0;

            coordinator.LifecycleChanged += (_, _) => throw new InvalidOperationException("observer failure");
            coordinator.LifecycleChanged += (_, _) => laterObserverCount++;

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.AreEqual(1, laterObserverCount);
        }

        [TestMethod]
        public void DuplicateCoordinatorNameIsRejected()
        {
            HostApplicationBuilder builder = CreateBuilder();
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                builder.AddConfigurationSetCoordinator(
                    "ProxySet",
                    "Next",
                    ["Next", "Experimental"]));
        }

        [TestMethod]
        public async Task ConcurrentSwitchesProduceUniqueMonotonicLifecycleSequencesAndCoherentActiveValue()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);
            var results = new List<ConfigurationSetSwitchResult>();
            object resultGate = new();

            Task[] tasks = Enumerable.Range(0, 20)
                .Select(index => Task.Run(() =>
                {
                    string value = index % 2 == 0 ? "Next" : "Experimental";
                    ConfigurationSetSwitchResult result = coordinator.TrySwitch(value);
                    lock (resultGate)
                    {
                        results.Add(result);
                    }
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            long[] sequences = results.Select(result => result.Sequence).OrderBy(value => value).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(1, 20).Select(value => (long)value).ToArray(), sequences);
            Assert.IsTrue(coordinator.ActiveValue is "Next" or "Experimental");
            Assert.IsTrue(results.All(result => result.ActiveValue is "Next" or "Experimental"));
        }

        private static HostApplicationBuilder CreateBuilder()
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
            });
        }
    }
}
