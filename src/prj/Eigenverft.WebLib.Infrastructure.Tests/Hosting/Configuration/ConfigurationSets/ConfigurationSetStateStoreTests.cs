using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetStateStoreTests
    {
        [TestMethod]
        public void MissingStateFileIsMaterializedWithCurrentValuesAndAllowedMetadata()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSetCoordinator(
                "EnvironmentSet",
                "Development",
                ["Development", "Production"]);
            _ = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);

            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            Assert.IsTrue(File.Exists(store.FilePath));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            JsonElement sets = document.RootElement.GetProperty("ConfigurationSets");

            JsonElement environment = sets.GetProperty("EnvironmentSet");
            Assert.AreEqual("Development", environment.GetProperty("DesiredValue").GetString());
            CollectionAssert.AreEqual(
                new[] { "Development", "Production" },
                environment.GetProperty("AllowedValues").EnumerateArray().Select(value => value.GetString()).ToArray());

            JsonElement proxy = sets.GetProperty("ProxySet");
            Assert.AreEqual("Stable", proxy.GetProperty("DesiredValue").GetString());
            CollectionAssert.AreEqual(
                new[] { "Stable", "Next", "Experimental" },
                proxy.GetProperty("AllowedValues").EnumerateArray().Select(value => value.GetString()).ToArray());

            using IHost host = builder.Build();
            Assert.AreSame(store, host.Services.GetRequiredService<IConfigurationSetStateStore>());
        }

        [TestMethod]
        public void ExistingStateValueIsAppliedAndAllowedMetadataIsCanonicalizedFromCoordinator()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ProxySet": {
                      "DesiredValue": "Experimental",
                      "AllowedValues": [ "SomethingElse" ]
                    }
                  }
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Next", "Experimental"]);

            _ = builder.AddConfigurationSetStateFile("ConfigurationSets.json", reloadOnChange: false);

            Assert.AreEqual("Experimental", proxy.ActiveValue);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "ConfigurationSets.json")));
            JsonElement proxyState = document.RootElement.GetProperty("ConfigurationSets").GetProperty("ProxySet");
            CollectionAssert.AreEqual(
                new[] { "Stable", "Next", "Experimental" },
                proxyState.GetProperty("AllowedValues").EnumerateArray().Select(value => value.GetString()).ToArray());
        }

        [TestMethod]
        public void FileAllowedValuesCannotAuthorizeValueRejectedByCoordinator()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ProxySet": {
                      "DesiredValue": "MadeUp",
                      "AllowedValues": [ "Stable", "MadeUp" ]
                    }
                  }
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                builder.AddConfigurationSetStateFile("ConfigurationSets.json", reloadOnChange: false));

            Assert.AreEqual("Stable", proxy.ActiveValue);
        }

        [TestMethod]
        public void UnknownSetRejectsDocumentBeforeKnownSetChanges()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ProxySet": { "DesiredValue": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] },
                    "TypoSet": { "DesiredValue": "Anything", "AllowedValues": [ "Anything" ] }
                  }
                }
                """);

            ConfigurationSetStateApplyResult result = store.Reload();

            Assert.AreEqual(ConfigurationSetStateApplyStatus.Rejected, result.Status);
            Assert.AreEqual(ConfigurationSetStateFailureKind.InvalidDocument, result.FailureKind);
            Assert.AreEqual(0, result.SetResults.Count);
            Assert.AreEqual("Stable", proxy.ActiveValue);
        }

        [TestMethod]
        public void IndependentSetsCanCompleteWithFailuresWithoutRollingBackSuccessfulAxis()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("ProxyStable.json", "{ \"Proxy\": \"Stable\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("proxy-settings", "ProxyStable.json");
            IConfigurationSetCoordinator environment = builder.AddConfigurationSetCoordinator(
                "EnvironmentSet",
                "Development",
                ["Development", "Production"]);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            builder.BindSwitchableJsonToConfigurationSet(
                "ProxySet",
                "proxy-settings",
                value => $"Proxy{value}.json");
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "EnvironmentSet": { "DesiredValue": "Production", "AllowedValues": [ "Development", "Production" ] },
                    "ProxySet": { "DesiredValue": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] }
                  }
                }
                """);

            ConfigurationSetStateApplyResult result = store.Reload();

            Assert.AreEqual(ConfigurationSetStateApplyStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(ConfigurationSetStateFailureKind.SetSwitchRejected, result.FailureKind);
            Assert.AreEqual(2, result.SetResults.Count);
            Assert.AreEqual("Production", environment.ActiveValue);
            Assert.AreEqual("Stable", proxy.ActiveValue);
            Assert.IsTrue(proxy.IsConsistent);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.SetResults[0].Status);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, result.SetResults[1].Status);
        }

        [TestMethod]
        public void RuntimeFileEditIsObservedAndSwitchesRequestedSet()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 25);
            using IHost host = builder.Build();
            host.StartAsync().GetAwaiter().GetResult();
            using var observed = new ManualResetEventSlim();
            ConfigurationSetStateStoreEventArgs? observedEvent = null;
            store.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.StateApplied &&
                    proxy.ActiveValue == "Experimental")
                {
                    observedEvent = args;
                    observed.Set();
                }
            };

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ProxySet": {
                      "DesiredValue": "Experimental",
                      "AllowedValues": [ "Stable", "Experimental" ]
                    }
                  }
                }
                """);

            Assert.IsTrue(observed.Wait(TimeSpan.FromSeconds(5)), "The state-file watcher did not apply the edit in time.");
            Assert.AreEqual("Experimental", proxy.ActiveValue);
            Assert.IsNotNull(observedEvent);
            host.StopAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ConfigurationSetStateApplyStatus.Succeeded, observedEvent.ApplyResult?.Status);
        }

        [TestMethod]
        public void MissingRegisteredSetRejectsAuthoritativeStateDocumentBeforeAnySetChanges()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "EnvironmentSet": {
                      "DesiredValue": "Production",
                      "AllowedValues": [ "Development", "Production" ]
                    }
                  }
                }
                """);
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator environment = builder.AddConfigurationSetCoordinator(
                "EnvironmentSet",
                "Development",
                ["Development", "Production"]);
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSetCoordinator(
                "ProxySet",
                "Stable",
                ["Stable", "Experimental"]);

            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                builder.AddConfigurationSetStateFile("ConfigurationSets.json", reloadOnChange: false));

            Assert.AreEqual("Development", environment.ActiveValue);
            Assert.AreEqual("Stable", proxy.ActiveValue);
        }

        [TestMethod]
        public void ProgramMainConvenienceFlowWatchesStateFileAndSwitchesBoundJsonAcrossTwoSets()
        {
            using var directory = new TemporaryDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "ProxySet", "Stable"));
            Directory.CreateDirectory(Path.Combine(directory.Path, "ProxySet", "Experimental"));
            Directory.CreateDirectory(Path.Combine(directory.Path, "FeatureSet", "Default"));
            Directory.CreateDirectory(Path.Combine(directory.Path, "FeatureSet", "Next"));
            directory.Write(Path.Combine("ProxySet", "Stable", "ProxySettings.json"), "{ \"ProxyMarker\": \"Stable\" }");
            directory.Write(Path.Combine("ProxySet", "Experimental", "ProxySettings.json"), "{ \"ProxyMarker\": \"Experimental\" }");
            directory.Write(Path.Combine("FeatureSet", "Default", "FeatureSettings.json"), "{ \"FeatureMarker\": \"Default\" }");
            directory.Write(Path.Combine("FeatureSet", "Next", "FeatureSettings.json"), "{ \"FeatureMarker\": \"Next\" }");

            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("proxy-settings", Path.Combine("ProxySet", "Stable", "ProxySettings.json"));
            builder.AddSwitchableJsonFile("feature-settings", Path.Combine("FeatureSet", "Default", "FeatureSettings.json"));
            IConfigurationSetStateStore store = builder.AddConfigurationSetsWithStateFile(
                "ConfigurationSets.json",
                ConfigurationSetDefinition.Create("ProxySet", "Stable", "Experimental"),
                ConfigurationSetDefinition.Create("FeatureSet", "Default", "Next"));
            builder.BindSwitchableJsonDirectoryToConfigurationSet("ProxySet", "proxy-settings", "ProxySet", "ProxySettings.json");
            builder.BindSwitchableJsonDirectoryToConfigurationSet("FeatureSet", "feature-settings", "FeatureSet", "FeatureSettings.json");

            using IHost host = builder.Build();
            host.StartAsync().GetAwaiter().GetResult();
            Assert.AreEqual("Stable", builder.Configuration["ProxyMarker"]);
            Assert.AreEqual("Default", builder.Configuration["FeatureMarker"]);

            using var observed = new ManualResetEventSlim();
            store.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.StateApplied &&
                    builder.Configuration["ProxyMarker"] == "Experimental" &&
                    builder.Configuration["FeatureMarker"] == "Next")
                {
                    observed.Set();
                }
            };

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ProxySet": { "DesiredValue": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] },
                    "FeatureSet": { "DesiredValue": "Next", "AllowedValues": [ "Default", "Next" ] }
                  }
                }
                """);

            Assert.IsTrue(observed.Wait(TimeSpan.FromSeconds(5)), "The complete state-file to IConfiguration switch did not finish in time.");
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMarker"]);
            Assert.AreEqual("Next", builder.Configuration["FeatureMarker"]);

            ConfigurationSetStateStoreStatus status = store.GetStatus();
            Assert.AreEqual(2, status.SetStates.Count);
            Assert.AreEqual("Experimental", status.SetStates.Single(set => set.Name == "ProxySet").ActiveValue);
            Assert.AreEqual("Next", status.SetStates.Single(set => set.Name == "FeatureSet").ActiveValue);
            Assert.IsTrue(status.SetStates.All(set => set.IsConsistent));
            CollectionAssert.AreEqual(new[] { "proxy-settings" }, status.SetStates.Single(set => set.Name == "ProxySet").BoundParticipantNames.ToArray());
            Assert.AreEqual(ConfigurationSetStateApplyStatus.Succeeded, status.LastApplyResult?.Status);

            host.StopAsync().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void StateFileApplyKeepsRejectedBoundSetOnLastKnownGoodWhileIndependentSetSwitches()
        {
            using var directory = new TemporaryDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "EnvironmentSet", "Development"));
            Directory.CreateDirectory(Path.Combine(directory.Path, "EnvironmentSet", "Production"));
            Directory.CreateDirectory(Path.Combine(directory.Path, "ProxySet", "Stable"));
            directory.Write(Path.Combine("EnvironmentSet", "Development", "Environment.json"), "{ \"EnvironmentMarker\": \"Development\" }");
            directory.Write(Path.Combine("EnvironmentSet", "Production", "Environment.json"), "{ \"EnvironmentMarker\": \"Production\" }");
            directory.Write(Path.Combine("ProxySet", "Stable", "Proxy.json"), "{ \"ProxyMarker\": \"Stable\" }");

            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddSwitchableJsonFile("environment-settings", Path.Combine("EnvironmentSet", "Development", "Environment.json"));
            builder.AddSwitchableJsonFile("proxy-settings", Path.Combine("ProxySet", "Stable", "Proxy.json"));
            IConfigurationSetStateStore store = builder.AddConfigurationSetsWithStateFile(
                "ConfigurationSets.json",
                ConfigurationSetDefinition.Create("EnvironmentSet", "Development", "Production"),
                ConfigurationSetDefinition.Create("ProxySet", "Stable", "Experimental"));
            builder.BindSwitchableJsonDirectoryToConfigurationSet("EnvironmentSet", "environment-settings", "EnvironmentSet", "Environment.json");
            builder.BindSwitchableJsonDirectoryToConfigurationSet("ProxySet", "proxy-settings", "ProxySet", "Proxy.json");

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "EnvironmentSet": { "DesiredValue": "Production", "AllowedValues": [ "Development", "Production" ] },
                    "ProxySet": { "DesiredValue": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] }
                  }
                }
                """);

            ConfigurationSetStateApplyResult result = store.Reload();

            Assert.AreEqual(ConfigurationSetStateApplyStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(ConfigurationSetStateFailureKind.SetSwitchRejected, result.FailureKind);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.SetResults.Single(item => item.Name == "EnvironmentSet").Status);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, result.SetResults.Single(item => item.Name == "ProxySet").Status);
            Assert.AreEqual("Production", builder.Configuration["EnvironmentMarker"]);
            Assert.AreEqual("Stable", builder.Configuration["ProxyMarker"]);

            ConfigurationSetStateStoreStatus status = store.GetStatus();
            Assert.AreEqual("Production", status.SetStates.Single(set => set.Name == "EnvironmentSet").ActiveValue);
            Assert.AreEqual("Stable", status.SetStates.Single(set => set.Name == "ProxySet").ActiveValue);
            Assert.IsTrue(status.SetStates.All(set => set.IsConsistent));
            Assert.AreSame(result, status.LastApplyResult);
        }

        [TestMethod]
        public void ReloadDisabledKeepsRuntimeValueUntilARecreatedBuilderReadsTheEditedStateFile()
        {
            using var directory = new TemporaryDirectory();

            HostApplicationBuilder firstBuilder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator firstCoordinator = firstBuilder.AddConfigurationSet(
                "ProxySet",
                "Stable",
                "Experimental").Coordinator;
            _ = firstBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            using (IHost firstHost = firstBuilder.Build())
            {
                firstHost.StartAsync().GetAwaiter().GetResult();

                directory.Write(
                    "ConfigurationSets.json",
                    """
                    {
                      "ConfigurationSets": {
                        "ProxySet": {
                          "DesiredValue": "Experimental",
                          "AllowedValues": [ "Stable", "Experimental" ]
                        }
                      }
                    }
                    """);

                Thread.Sleep(600);
                Assert.AreEqual("Stable", firstCoordinator.ActiveValue);

                firstHost.StopAsync().GetAwaiter().GetResult();
            }

            HostApplicationBuilder secondBuilder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator secondCoordinator = secondBuilder.AddConfigurationSet(
                "ProxySet",
                "Stable",
                "Experimental").Coordinator;
            _ = secondBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            Assert.AreEqual("Experimental", secondCoordinator.ActiveValue);
        }

        [TestMethod]
        public void MixedRuntimeAndStartupOnlyStateFileReloadDefersOnlyStartupOnlyAxis()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .Coordinator;
            IConfigurationSetCoordinator release = builder
                .AddConfigurationSet("ReleaseChannel", "Stable", "Beta")
                .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
                .Coordinator;

            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "RoutingProfile": {
                      "DesiredValue": "Failover",
                      "AllowedValues": [ "Primary", "Failover" ],
                      "ApplyMode": "StartupOnly"
                    },
                    "ReleaseChannel": {
                      "DesiredValue": "Beta",
                      "AllowedValues": [ "Stable", "Beta" ],
                      "ApplyMode": "Runtime"
                    }
                  }
                }
                """);

            ConfigurationSetStateApplyResult result = store.Reload();

            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.HasPendingRestart);
            Assert.AreEqual("Failover", routing.ActiveValue);
            Assert.AreEqual("Stable", release.ActiveValue);
            Assert.AreEqual(1, result.PendingRestartChanges.Count);
            Assert.AreEqual("ReleaseChannel", result.PendingRestartChanges[0].Name);
            Assert.AreEqual("Stable", result.PendingRestartChanges[0].ActiveValue);
            Assert.AreEqual("Beta", result.PendingRestartChanges[0].DesiredValue);
            Assert.AreEqual(ConfigurationSetApplyMode.StartupOnly, result.PendingRestartChanges[0].ApplyMode);

            ConfigurationSetStateStoreStatus status = store.GetStatus();
            ConfigurationSetStateStatus routingStatus = status.SetStates.Single(set => set.Name == "RoutingProfile");
            ConfigurationSetStateStatus releaseStatus = status.SetStates.Single(set => set.Name == "ReleaseChannel");
            Assert.AreEqual(ConfigurationSetApplyMode.Runtime, routingStatus.ApplyMode);
            Assert.AreEqual("Failover", routingStatus.DesiredValue);
            Assert.IsFalse(routingStatus.HasPendingRestart);
            Assert.AreEqual(ConfigurationSetApplyMode.StartupOnly, releaseStatus.ApplyMode);
            Assert.AreEqual("Beta", releaseStatus.DesiredValue);
            Assert.IsTrue(releaseStatus.HasPendingRestart);
            Assert.IsTrue(status.HasPendingRestart);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            JsonElement sets = document.RootElement.GetProperty("ConfigurationSets");
            Assert.AreEqual("Runtime", sets.GetProperty("RoutingProfile").GetProperty("ApplyMode").GetString());
            Assert.AreEqual("StartupOnly", sets.GetProperty("ReleaseChannel").GetProperty("ApplyMode").GetString());
            Assert.AreEqual("Beta", sets.GetProperty("ReleaseChannel").GetProperty("DesiredValue").GetString());
        }

        [TestMethod]
        public void StartupOnlyDesiredValueIsAppliedWhenNextHostStarts()
        {
            using var directory = new TemporaryDirectory();

            HostApplicationBuilder firstBuilder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator first = firstBuilder
                .AddConfigurationSet("ReleaseChannel", "Stable", "Beta")
                .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
                .Coordinator;
            IConfigurationSetStateStore firstStore = firstBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "ReleaseChannel": {
                      "DesiredValue": "Beta",
                      "AllowedValues": [ "Stable", "Beta" ],
                      "ApplyMode": "StartupOnly"
                    }
                  }
                }
                """);

            ConfigurationSetStateApplyResult runtimeReload = firstStore.Reload();
            Assert.IsTrue(runtimeReload.HasPendingRestart);
            Assert.AreEqual("Stable", first.ActiveValue);

            HostApplicationBuilder secondBuilder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator second = secondBuilder
                .AddConfigurationSet("ReleaseChannel", "Stable", "Beta")
                .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
                .Coordinator;
            IConfigurationSetStateStore secondStore = secondBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            Assert.AreEqual("Beta", second.ActiveValue);
            Assert.IsFalse(secondStore.GetStatus().HasPendingRestart);
            Assert.AreEqual("Beta", secondStore.GetStatus().SetStates.Single().DesiredValue);
        }

        [TestMethod]
        public void StateFileWatcherAppliesRuntimeAxisAndReportsStartupOnlyPendingRestart()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .Coordinator;
            IConfigurationSetCoordinator release = builder
                .AddConfigurationSet("ReleaseChannel", "Stable", "Beta")
                .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 25);

            using IHost host = builder.Build();
            host.StartAsync().GetAwaiter().GetResult();
            using var observed = new ManualResetEventSlim();
            ConfigurationSetStateApplyResult? observedResult = null;
            store.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.StateApplied &&
                    args.ApplyResult?.HasPendingRestart == true &&
                    routing.ActiveValue == "Failover")
                {
                    observedResult = args.ApplyResult;
                    observed.Set();
                }
            };

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "RoutingProfile": {
                      "DesiredValue": "Failover",
                      "AllowedValues": [ "Primary", "Failover" ]
                    },
                    "ReleaseChannel": {
                      "DesiredValue": "Beta",
                      "AllowedValues": [ "Stable", "Beta" ]
                    }
                  }
                }
                """);

            Assert.IsTrue(observed.Wait(TimeSpan.FromSeconds(5)), "The mixed apply-mode state-file edit was not observed in time.");
            Assert.AreEqual("Failover", routing.ActiveValue);
            Assert.AreEqual("Stable", release.ActiveValue);
            Assert.IsNotNull(observedResult);
            Assert.AreEqual(1, observedResult.PendingRestartChanges.Count);
            Assert.IsTrue(store.GetStatus().HasPendingRestart);

            host.StopAsync().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void EventHubSubscriberCanReadStateStoreFromAnotherThreadDuringPersistentRuntimeSwitch()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSet("RoutingProfile", "Primary", "Failover");
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);
            using IHost host = builder.Build();
            IConfigurationSetEventHub eventHub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            int blockedStatusReadCount = 0;
            int observedCount = 0;
            using IDisposable subscription = eventHub.Subscribe("RoutingProfile", notification =>
            {
                if (notification.Kind != ConfigurationSetEventKind.SwitchSucceeded)
                {
                    return;
                }

                Interlocked.Increment(ref observedCount);
                using var statusReadCompleted = new ManualResetEventSlim();
                var statusThread = new Thread(() =>
                {
                    _ = store.GetStatus();
                    statusReadCompleted.Set();
                })
                {
                    IsBackground = true,
                };
                statusThread.Start();

                if (!statusReadCompleted.Wait(TimeSpan.FromSeconds(1)))
                {
                    Interlocked.Increment(ref blockedStatusReadCount);
                }
            });

            ConfigurationSetStateApplyResult result =
                store.TrySetDesiredValue("RoutingProfile", "Failover");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, observedCount);
            Assert.AreEqual(0, blockedStatusReadCount);
            Assert.AreEqual("Failover", store.GetStatus().SetStates.Single().ActiveValue);
        }

        [TestMethod]
        public void PersistentRuntimeDesiredValueIsWrittenBeforeLiveSwitchAndPublishesLifecycle()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            ConfigurationSetStateStoreEventArgs? observed = null;
            store.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.DesiredValueUpdated)
                {
                    observed = args;
                }
            };

            ConfigurationSetStateApplyResult result = store.TrySetDesiredValue("RoutingProfile", "Failover");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.SetResults.Count);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.SetResults[0].Status);
            Assert.AreEqual("Failover", routing.ActiveValue);
            Assert.IsNotNull(observed);
            Assert.AreSame(result, observed.ApplyResult);

            ConfigurationSetStateStatus status = store.GetStatus().SetStates.Single();
            Assert.AreEqual("Failover", status.ActiveValue);
            Assert.AreEqual("Failover", status.DesiredValue);
            Assert.IsFalse(status.HasDesiredStateDrift);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            JsonElement routingState = document.RootElement.GetProperty("ConfigurationSets").GetProperty("RoutingProfile");
            Assert.AreEqual("Failover", routingState.GetProperty("DesiredValue").GetString());
            Assert.AreEqual("Runtime", routingState.GetProperty("ApplyMode").GetString());
        }

        [TestMethod]
        public void PersistentStartupOnlyDesiredValueIsWrittenAndReportedPendingWithoutLiveSwitch()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator release = builder
                .AddConfigurationSet("ReleaseChannel", "Stable", "Beta")
                .ApplyMode(ConfigurationSetApplyMode.StartupOnly)
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            ConfigurationSetStateApplyResult result = store.TrySetDesiredValue("ReleaseChannel", "Beta");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(0, result.SetResults.Count);
            Assert.IsTrue(result.HasPendingRestart);
            Assert.AreEqual(1, result.PendingRestartChanges.Count);
            Assert.AreEqual("Stable", release.ActiveValue);

            ConfigurationSetStateStoreStatus status = store.GetStatus();
            ConfigurationSetStateStatus releaseStatus = status.SetStates.Single();
            Assert.AreEqual("Stable", releaseStatus.ActiveValue);
            Assert.AreEqual("Beta", releaseStatus.DesiredValue);
            Assert.IsTrue(releaseStatus.HasDesiredStateDrift);
            Assert.IsTrue(releaseStatus.HasPendingRestart);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            JsonElement releaseState = document.RootElement.GetProperty("ConfigurationSets").GetProperty("ReleaseChannel");
            Assert.AreEqual("Beta", releaseState.GetProperty("DesiredValue").GetString());
            Assert.AreEqual("StartupOnly", releaseState.GetProperty("ApplyMode").GetString());
        }

        [TestMethod]
        public void PersistentRuntimeDesiredValueRemainsPersistedWhenCandidatePreparationRejectsAndCanLaterConverge()
        {
            using var directory = new TemporaryDirectory();
            directory.Write("routing-primary.json", "{ \"RouteMarker\": \"Primary\" }");

            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .AddSwitchableJson(value => value == "Primary" ? "routing-primary.json" : "routing-failover.json")
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            ConfigurationSetStateApplyResult rejected = store.TrySetDesiredValue("RoutingProfile", "Failover");

            Assert.AreEqual(ConfigurationSetStateApplyStatus.CompletedWithFailures, rejected.Status);
            Assert.AreEqual(ConfigurationSetStateFailureKind.SetSwitchRejected, rejected.FailureKind);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, rejected.SetResults.Single().Status);
            Assert.AreEqual("Primary", routing.ActiveValue);
            Assert.AreEqual("Primary", builder.Configuration["RouteMarker"]);

            ConfigurationSetStateStatus drift = store.GetStatus().SetStates.Single();
            Assert.AreEqual("Failover", drift.DesiredValue);
            Assert.AreEqual("Primary", drift.ActiveValue);
            Assert.IsTrue(drift.HasDesiredStateDrift);
            Assert.IsFalse(drift.HasPendingRestart);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath)))
            {
                Assert.AreEqual(
                    "Failover",
                    document.RootElement
                        .GetProperty("ConfigurationSets")
                        .GetProperty("RoutingProfile")
                        .GetProperty("DesiredValue")
                        .GetString());
            }

            directory.Write("routing-failover.json", "{ \"RouteMarker\": \"Failover\" }");
            ConfigurationSetStateApplyResult retried = store.Reload();

            Assert.IsTrue(retried.Succeeded);
            Assert.AreEqual("Failover", routing.ActiveValue);
            Assert.AreEqual("Failover", builder.Configuration["RouteMarker"]);
            Assert.IsFalse(store.GetStatus().HasDesiredStateDrift);
        }

        [TestMethod]
        public void DirectCoordinatorSwitchRemainsEphemeralAndDoesNotRewriteStateStoreDesiredValue()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            ConfigurationSetSwitchResult direct = routing.TrySwitch("Failover");

            Assert.IsTrue(direct.Succeeded);
            Assert.AreEqual("Failover", routing.ActiveValue);
            ConfigurationSetStateStatus state = store.GetStatus().SetStates.Single();
            Assert.AreEqual("Primary", state.DesiredValue);
            Assert.AreEqual("Failover", state.ActiveValue);
            Assert.IsTrue(state.HasDesiredStateDrift);
            Assert.IsFalse(state.HasPendingRestart);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            Assert.AreEqual(
                "Primary",
                document.RootElement
                    .GetProperty("ConfigurationSets")
                    .GetProperty("RoutingProfile")
                    .GetProperty("DesiredValue")
                    .GetString());
        }

        [TestMethod]
        public void PersistentDesiredValueWriteDoesNotEchoAsWatcherStateApply()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            _ = builder.AddConfigurationSet("RoutingProfile", "Primary", "Failover");
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 25);

            using IHost host = builder.Build();
            host.StartAsync().GetAwaiter().GetResult();

            int desiredValueEvents = 0;
            int watcherApplyEvents = 0;
            store.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.DesiredValueUpdated)
                {
                    Interlocked.Increment(ref desiredValueEvents);
                }
                else if (args.Kind == ConfigurationSetStateStoreEventKind.StateApplied)
                {
                    Interlocked.Increment(ref watcherApplyEvents);
                }
            };

            ConfigurationSetStateApplyResult result = store.TrySetDesiredValue("RoutingProfile", "Failover");
            Assert.IsTrue(result.Succeeded);

            Thread.Sleep(600);

            Assert.AreEqual(1, desiredValueEvents);
            Assert.AreEqual(0, watcherApplyEvents);
            host.StopAsync().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void PersistentDesiredValueDoesNotSwitchRuntimeWhenStateFilePersistenceFails()
        {
            using var directory = new TemporaryDirectory();
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            IConfigurationSetCoordinator routing = builder
                .AddConfigurationSet("RoutingProfile", "Primary", "Failover")
                .Coordinator;
            IConfigurationSetStateStore store = builder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            File.Delete(store.FilePath);
            Directory.CreateDirectory(store.FilePath);

            ConfigurationSetStateApplyResult result =
                store.TrySetDesiredValue("RoutingProfile", "Failover");

            Assert.AreEqual(ConfigurationSetStateApplyStatus.Rejected, result.Status);
            Assert.AreEqual(ConfigurationSetStateFailureKind.IoError, result.FailureKind);
            Assert.IsNotNull(result.Exception);
            Assert.AreEqual("Primary", routing.ActiveValue);

            ConfigurationSetStateStatus state = store.GetStatus().SetStates.Single();
            Assert.AreEqual("Primary", state.ActiveValue);
            Assert.AreEqual("Primary", state.DesiredValue);
            Assert.IsFalse(state.HasDesiredStateDrift);
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
                    "Eigenverft.WebLib.Infrastructure.ConfigurationSetStateStore.Tests",
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
