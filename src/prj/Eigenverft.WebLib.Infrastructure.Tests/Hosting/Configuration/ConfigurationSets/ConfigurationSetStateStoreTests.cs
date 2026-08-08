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
            JsonElement sets = document.RootElement.GetProperty("Sets");

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
                  "Sets": {
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
            JsonElement proxyState = document.RootElement.GetProperty("Sets").GetProperty("ProxySet");
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
                  "Sets": {
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
                  "Sets": {
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
                  "Sets": {
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
                  "Sets": {
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
                  "Sets": {
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
            Assert.IsTrue(document.RootElement.GetProperty("Sets").TryGetProperty("ProxySet", out JsonElement proxyState));
            Assert.AreEqual("Stable", proxyState.GetProperty("Value").GetString());
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
