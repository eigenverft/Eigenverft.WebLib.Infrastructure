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
            Assert.IsTrue(partial.SourceChanged);
            Assert.IsTrue(partial.ConfigurationChanged);
            Assert.IsTrue(partial.HasChanges);
            Assert.AreEqual(1, partial.ParticipantResults.Count);
            Assert.AreEqual("first", partial.ParticipantResults[0].Name);
            Assert.IsTrue(partial.ParticipantResults[0].SourceChanged);
            Assert.IsTrue(partial.ParticipantResults[0].ConfigurationChanged);
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

        [TestMethod]
        public void BuilderBindingWiresSetBeforeBuildAndUsesKeyedRuntimeAfterBuild()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{ \"Value\": \"Stable\" }");
            directory.Write("Experimental.json", "{ \"Value\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "Stable.json");
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            builder.BindSwitchableJsonToConfigurationSet(
                "ProxySet",
                "settings",
                value => $"{value}.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");
            ISwitchableJsonConfiguration settings =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("settings");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.AreEqual("Experimental", builder.Configuration["Value"]);
            Assert.AreEqual(Path.Combine(directory.Path, "Experimental.json"), settings.CurrentSourcePath);
            CollectionAssert.AreEqual(new[] { "settings" }, coordinator.BoundParticipantNames.ToArray());
        }

        [TestMethod]
        public void BuilderBindingAllowsCoordinatorAndSwitchableRegistrationInEitherOrder()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{ \"Value\": \"Stable\" }");
            directory.Write("Next.json", "{ \"Value\": \"Next\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next"]);
            builder.AddSwitchableJsonFile("settings", "Stable.json");

            builder.BindSwitchableJsonToConfigurationSet(
                "ProxySet",
                "settings",
                value => $"{value}.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Next");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Next", builder.Configuration["Value"]);
        }

        [TestMethod]
        public void BuilderBindingFailsFastWhenReferencedRegistrationIsMissing()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{}");
            HostApplicationBuilder missingSetBuilder = CreateBuilder(directory.Path);
            missingSetBuilder.AddSwitchableJsonFile("settings", "Stable.json");

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                missingSetBuilder.BindSwitchableJsonToConfigurationSet(
                    "ProxySet",
                    "settings",
                    _ => "Stable.json"));

            HostApplicationBuilder missingSwitchableBuilder = CreateBuilder(directory.Path);
            _ = missingSwitchableBuilder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable"]);

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                missingSwitchableBuilder.BindSwitchableJsonToConfigurationSet(
                    "ProxySet",
                    "settings",
                    _ => "Stable.json"));
        }

        [TestMethod]
        public void BuilderBindingRejectsParticipantThatDoesNotRepresentInitialSetValue()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{ \"Value\": \"Stable\" }");
            directory.Write("Experimental.json", "{ \"Value\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("settings", "Experimental.json");
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                builder.BindSwitchableJsonToConfigurationSet(
                    "ProxySet",
                    "settings",
                    value => $"{value}.json"));
        }

        [TestMethod]
        public void SingleFileConvenienceDerivesInitialSourceAndSwitchesConfiguration()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("Proxy", "Stable", "ProxySettings.json"), "{ \"ProxyMode\": \"Stable\" }");
            directory.Write(Path.Combine("Proxy", "Experimental", "ProxySettings.json"), "{ \"ProxyMode\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            builder.AddSwitchableJsonToConfigurationSet(
                setName: "ProxySet",
                switchableName: "proxy-settings",
                rootPath: "Proxy",
                fileName: "ProxySettings.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");
            ISwitchableJsonConfiguration settings =
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("proxy-settings");

            Assert.AreEqual("Stable", builder.Configuration["ProxyMode"]);
            Assert.AreEqual(
                Path.Combine(directory.Path, "Proxy", "Stable", "ProxySettings.json"),
                settings.CurrentSourcePath);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMode"]);
            Assert.AreEqual(
                Path.Combine(directory.Path, "Proxy", "Experimental", "ProxySettings.json"),
                settings.CurrentSourcePath);
        }

        [TestMethod]
        public void MultipleFileConvenienceRegistersIndependentSourcesInSharedSetDirectory()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("AppSettings", "Stable", "ProxySettings.json"), "{ \"ProxyMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Stable", "EdgeFilters.json"), "{ \"FilterMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Stable", "Behaviors.json"), "{ \"BehaviorMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Experimental", "ProxySettings.json"), "{ \"ProxyMode\": \"Experimental\" }");
            directory.Write(Path.Combine("AppSettings", "Experimental", "EdgeFilters.json"), "{ \"FilterMode\": \"Experimental\" }");
            directory.Write(Path.Combine("AppSettings", "Experimental", "Behaviors.json"), "{ \"BehaviorMode\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            builder.AddSwitchableJsonToConfigurationSet(
                "ProxySet",
                "AppSettings",
                [
                    ("proxy-settings", "ProxySettings.json"),
                    ("edge-filters", "EdgeFilters.json"),
                    ("behaviors", "Behaviors.json"),
                ]);

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");

            CollectionAssert.AreEqual(
                new[] { "proxy-settings", "edge-filters", "behaviors" },
                coordinator.BoundParticipantNames.ToArray());
            Assert.AreEqual("Stable", builder.Configuration["ProxyMode"]);
            Assert.AreEqual("Stable", builder.Configuration["FilterMode"]);
            Assert.AreEqual("Stable", builder.Configuration["BehaviorMode"]);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMode"]);
            Assert.AreEqual("Experimental", builder.Configuration["FilterMode"]);
            Assert.AreEqual("Experimental", builder.Configuration["BehaviorMode"]);
            Assert.AreEqual(
                Path.Combine(directory.Path, "AppSettings", "Experimental", "ProxySettings.json"),
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("proxy-settings").CurrentSourcePath);
            Assert.AreEqual(
                Path.Combine(directory.Path, "AppSettings", "Experimental", "EdgeFilters.json"),
                host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>("edge-filters").CurrentSourcePath);
        }

        [TestMethod]
        public void MultipleFileConvenienceRollsBackWholeBatchWhenLaterInitialSourceIsInvalid()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("AppSettings", "Stable", "First.json"), "{ \"First\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Stable", "Second.json"), "{ invalid json");
            directory.Write(Path.Combine("AppSettings", "Experimental", "First.json"), "{ \"First\": \"Experimental\" }");
            directory.Write(Path.Combine("AppSettings", "Experimental", "Second.json"), "{ \"Second\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            bool failed = false;
            try
            {
                builder.AddSwitchableJsonToConfigurationSet(
                    "ProxySet",
                    "AppSettings",
                    [
                        ("first", "First.json"),
                        ("second", "Second.json"),
                    ]);
            }
            catch (Exception)
            {
                failed = true;
            }

            Assert.IsTrue(failed, "The invalid second initial JSON source should reject the batch.");
            Assert.AreEqual(0, coordinator.BoundParticipantNames.Count);
            Assert.AreEqual("Stable", coordinator.ActiveValue);

            directory.Write(Path.Combine("AppSettings", "Stable", "Second.json"), "{ \"Second\": \"Stable\" }");

            builder.AddSwitchableJsonToConfigurationSet(
                "ProxySet",
                "AppSettings",
                [
                    ("first", "First.json"),
                    ("second", "Second.json"),
                ]);

            using IHost host = builder.Build();
            Assert.AreEqual("Stable", builder.Configuration["First"]);
            Assert.AreEqual("Stable", builder.Configuration["Second"]);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", builder.Configuration["First"]);
            Assert.AreEqual("Experimental", builder.Configuration["Second"]);
            Assert.AreEqual(2, coordinator.BoundParticipantNames.Count);
        }

        [TestMethod]
        public void SingleValueSetCanOwnSwitchableJsonWithoutAnyAlternativeValue()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                Path.Combine("AppSettings", "Stable", "Settings.json"),
                "{ \"Mode\": \"StableOnly\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "ProxySet",
                "Stable");
            registration.AddSwitchableJson(
                "AppSettings",
                "Settings.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            ConfigurationSetNotification? observed = null;
            using IDisposable subscription = hub.Subscribe("ProxySet", notification => observed = notification);

            Assert.AreEqual(1, coordinator.AllowedValues.Count);
            Assert.AreEqual("Stable", coordinator.AllowedValues[0]);
            Assert.AreEqual("Stable", coordinator.ActiveValue);
            Assert.AreEqual("StableOnly", builder.Configuration["Mode"]);
            CollectionAssert.AreEqual(
                new[] { "ProxySet:AppSettings/Settings.json" },
                coordinator.BoundParticipantNames.ToArray());

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Stable");

            Assert.AreEqual(ConfigurationSetSwitchStatus.AlreadyActive, result.Status);
            Assert.IsFalse(result.HasChanges);
            Assert.IsNotNull(observed);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchAlreadyActive, observed.Kind);
            Assert.AreSame(result, observed.Result);
        }

        [TestMethod]
        public void FluentResolverCanSwitchFileNamePatternWithoutValueDirectories()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("EnvironmentSettings.Development.json", "{ \"EnvironmentMarker\": \"Development\" }");
            directory.Write("EnvironmentSettings.Production.json", "{ \"EnvironmentMarker\": \"Production\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "EnvironmentSet",
                "Development",
                "Production");
            registration.AddSwitchableJson(value => $"EnvironmentSettings.{value}.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("EnvironmentSet");

            Assert.AreEqual("Development", builder.Configuration["EnvironmentMarker"]);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Production");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Production", builder.Configuration["EnvironmentMarker"]);
            Assert.IsTrue(result.ValueChanged);
            Assert.IsTrue(result.SourceChanged);
            Assert.IsTrue(result.ConfigurationChanged);
        }

        [TestMethod]
        public void FluentResolverCanMapProxyRoutingSetToArbitraryFileNames()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "proxy-routing-safe.json",
                "{ \"ReverseProxy\": { \"Routes\": { \"main\": { \"ClusterId\": \"stable-cluster\" } } } }");
            directory.Write(
                "candidate-routing-v2.json",
                "{ \"ReverseProxy\": { \"Routes\": { \"main\": { \"ClusterId\": \"candidate-cluster\" } } } }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "ProxySet",
                "Stable",
                "Candidate");
            registration.AddSwitchableJson(value => value switch
            {
                "Stable" => "proxy-routing-safe.json",
                "Candidate" => "candidate-routing-v2.json",
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            });

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");

            Assert.AreEqual("stable-cluster", builder.Configuration["ReverseProxy:Routes:main:ClusterId"]);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Candidate");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("candidate-cluster", builder.Configuration["ReverseProxy:Routes:main:ClusterId"]);
        }

        [TestMethod]
        public void FluentResolverAllowsDifferentSetValuesToShareTheSameSource()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Shared.json", "{ \"SharedMarker\": \"Same\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "BehaviorSet",
                "A",
                "B");
            registration.AddSwitchableJson(_ => "Shared.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("BehaviorSet");

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("B");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("B", coordinator.ActiveValue);
            Assert.IsTrue(result.ValueChanged);
            Assert.IsFalse(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.IsTrue(result.HasChanges);
            Assert.AreEqual("Same", builder.Configuration["SharedMarker"]);
        }

        [TestMethod]
        public void FluentResolverMappingIsEvaluatedOnceAndFrozenAtRegistration()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("Stable.json", "{ \"Mode\": \"Stable\" }");
            directory.Write("Next.json", "{ \"Mode\": \"Next\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            int resolverCalls = 0;

            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "RoutingSet",
                "Stable",
                "Next");
            registration.AddSwitchableJson(value =>
            {
                resolverCalls++;
                return $"{value}.json";
            });

            Assert.AreEqual(2, resolverCalls);

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("RoutingSet");
            _ = coordinator.TrySwitch("Next");

            Assert.AreEqual(2, resolverCalls);
            Assert.AreEqual("Next", builder.Configuration["Mode"]);
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
                string? parent = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

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
