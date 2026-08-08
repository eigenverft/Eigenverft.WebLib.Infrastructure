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
            Assert.AreEqual("Development", environment.GetProperty("Value").GetString());
            CollectionAssert.AreEqual(
                new[] { "Development", "Production" },
                environment.GetProperty("AllowedValues").EnumerateArray().Select(value => value.GetString()).ToArray());

            JsonElement proxy = sets.GetProperty("ProxySet");
            Assert.AreEqual("Stable", proxy.GetProperty("Value").GetString());
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
                      "Value": "Experimental",
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
                      "Value": "MadeUp",
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
                    "ProxySet": { "Value": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] },
                    "TypoSet": { "Value": "Anything", "AllowedValues": [ "Anything" ] }
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
                    "EnvironmentSet": { "Value": "Production", "AllowedValues": [ "Development", "Production" ] },
                    "ProxySet": { "Value": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] }
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
                      "Value": "Experimental",
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
        public void MissingKnownSetInOlderFileUsesCurrentValueAndIsAddedBackDuringCanonicalization()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "EnvironmentSet": {
                      "Value": "Production",
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

            _ = builder.AddConfigurationSetStateFile("ConfigurationSets.json", reloadOnChange: false);

            Assert.AreEqual("Production", environment.ActiveValue);
            Assert.AreEqual("Stable", proxy.ActiveValue);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "ConfigurationSets.json")));
            Assert.IsTrue(document.RootElement.GetProperty("ConfigurationSets").TryGetProperty("ProxySet", out JsonElement proxyState));
            Assert.AreEqual("Stable", proxyState.GetProperty("Value").GetString());
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
                    "ProxySet": { "Value": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] },
                    "FeatureSet": { "Value": "Next", "AllowedValues": [ "Default", "Next" ] }
                  }
                }
                """);

            Assert.IsTrue(observed.Wait(TimeSpan.FromSeconds(5)), "The complete state-file to IConfiguration switch did not finish in time.");
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMarker"]);
            Assert.AreEqual("Next", builder.Configuration["FeatureMarker"]);

            ConfigurationSetStateStoreStatus status = store.GetStatus();
            Assert.AreEqual(2, status.Sets.Count);
            Assert.AreEqual("Experimental", status.Sets.Single(set => set.Name == "ProxySet").ActiveValue);
            Assert.AreEqual("Next", status.Sets.Single(set => set.Name == "FeatureSet").ActiveValue);
            Assert.IsTrue(status.Sets.All(set => set.IsConsistent));
            CollectionAssert.AreEqual(new[] { "proxy-settings" }, status.Sets.Single(set => set.Name == "ProxySet").BoundParticipantNames.ToArray());
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
                    "EnvironmentSet": { "Value": "Production", "AllowedValues": [ "Development", "Production" ] },
                    "ProxySet": { "Value": "Experimental", "AllowedValues": [ "Stable", "Experimental" ] }
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
            Assert.AreEqual("Production", status.Sets.Single(set => set.Name == "EnvironmentSet").ActiveValue);
            Assert.AreEqual("Stable", status.Sets.Single(set => set.Name == "ProxySet").ActiveValue);
            Assert.IsTrue(status.Sets.All(set => set.IsConsistent));
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
                          "Value": "Experimental",
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
