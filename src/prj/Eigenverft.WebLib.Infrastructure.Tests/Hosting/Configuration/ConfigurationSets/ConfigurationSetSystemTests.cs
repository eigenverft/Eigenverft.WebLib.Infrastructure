using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetSystemTests
    {
        [TestMethod]
        public void RealisticProgramMainSupportsMixedProfilesWatcherEventsPersistenceAndRestart()
        {
            using var directory = new TemporaryDirectory();
            WriteProfileFiles(directory);

            HostApplicationBuilder firstBuilder = CreateBuilder(directory.Path);
            RegisterRealisticConfigurationSets(firstBuilder);
            RegisterRecordingHostedService(firstBuilder);
            IConfigurationSetStateStore firstStore = firstBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: true,
                reloadDelayMilliseconds: 25);

            using IHost firstHost = firstBuilder.Build();
            RecordingConfigurationSetHostedService recorder =
                firstHost.Services.GetRequiredService<RecordingConfigurationSetHostedService>();

            firstHost.StartAsync().GetAwaiter().GetResult();

            Assert.AreEqual("primary-route", firstBuilder.Configuration["Routing:Route"]);
            Assert.AreEqual("primary-cluster", firstBuilder.Configuration["Routing:Cluster"]);
            Assert.AreEqual("normal-features", firstBuilder.Configuration["Operations:Features"]);
            Assert.AreEqual("normal-resilience", firstBuilder.Configuration["Operations:Resilience"]);
            Assert.AreEqual("normal-diagnostics", firstBuilder.Configuration["Operations:Diagnostics"]);
            Assert.AreEqual("stable-release", firstBuilder.Configuration["Release:Marker"]);

            using var stateApplied = new ManualResetEventSlim();
            ConfigurationSetStateApplyResult? watcherResult = null;
            firstStore.LifecycleChanged += (_, args) =>
            {
                if (args.Kind == ConfigurationSetStateStoreEventKind.StateApplied &&
                    args.ApplyResult?.HasPendingRestart == true)
                {
                    watcherResult = args.ApplyResult;
                    stateApplied.Set();
                }
            };

            directory.Write(
                "ConfigurationSets.json",
                """
                {
                  "ConfigurationSets": {
                    "RoutingProfile": {
                      "Value": "Failover",
                      "AllowedValues": [ "Primary", "Canary", "Failover" ],
                      "ApplyMode": "Runtime"
                    },
                    "OperationalProfile": {
                      "Value": "Degraded",
                      "AllowedValues": [ "Normal", "Degraded", "Incident" ],
                      "ApplyMode": "Runtime"
                    },
                    "ReleaseChannel": {
                      "Value": "Beta",
                      "AllowedValues": [ "Stable", "Beta" ],
                      "ApplyMode": "StartupOnly"
                    }
                  }
                }
                """);

            Assert.IsTrue(
                stateApplied.Wait(TimeSpan.FromSeconds(5)),
                "The realistic configuration-set watcher transition was not observed in time.");

            Assert.IsNotNull(watcherResult);
            Assert.IsTrue(watcherResult.Succeeded);
            Assert.AreEqual(2, watcherResult.SetResults.Count);
            Assert.AreEqual(1, watcherResult.PendingRestartChanges.Count);

            Assert.AreEqual("failover-route", firstBuilder.Configuration["Routing:Route"]);
            Assert.AreEqual("failover-cluster", firstBuilder.Configuration["Routing:Cluster"]);
            Assert.AreEqual("degraded-features", firstBuilder.Configuration["Operations:Features"]);
            Assert.AreEqual("degraded-resilience", firstBuilder.Configuration["Operations:Resilience"]);
            Assert.AreEqual("degraded-diagnostics", firstBuilder.Configuration["Operations:Diagnostics"]);
            Assert.AreEqual("stable-release", firstBuilder.Configuration["Release:Marker"]);

            ConfigurationSetStateStoreStatus watchedStatus = firstStore.GetStatus();
            ConfigurationSetStateStatus releaseStatus = watchedStatus.SetStates.Single(set => set.Name == "ReleaseChannel");
            Assert.AreEqual("Stable", releaseStatus.ActiveValue);
            Assert.AreEqual("Beta", releaseStatus.DesiredValue);
            Assert.IsTrue(releaseStatus.HasPendingRestart);
            Assert.IsTrue(watchedStatus.HasPendingRestart);

            ConfigurationSetNotification[] watchedNotifications = recorder.Snapshot();
            ConfigurationSetNotification routingNotification =
                watchedNotifications.Last(notification => notification.SetName == "RoutingProfile");
            ConfigurationSetNotification operationalNotification =
                watchedNotifications.Last(notification => notification.SetName == "OperationalProfile");

            Assert.AreEqual(ConfigurationSetEventKind.SwitchSucceeded, routingNotification.Kind);
            Assert.AreEqual(2, routingNotification.Result.ParticipantResults.Count);
            Assert.IsTrue(routingNotification.Result.SourceChanged);
            Assert.IsTrue(routingNotification.Result.ConfigurationChanged);

            Assert.AreEqual(ConfigurationSetEventKind.SwitchSucceeded, operationalNotification.Kind);
            Assert.AreEqual(3, operationalNotification.Result.ParticipantResults.Count);
            Assert.IsTrue(operationalNotification.Result.SourceChanged);
            Assert.IsTrue(operationalNotification.Result.ConfigurationChanged);
            Assert.IsFalse(watchedNotifications.Any(notification => notification.SetName == "ReleaseChannel"));

            ConfigurationSetStateApplyResult persistentRouting =
                firstStore.TrySetDesiredValue("RoutingProfile", "Primary");

            Assert.IsTrue(persistentRouting.Succeeded);
            Assert.AreEqual("primary-route", firstBuilder.Configuration["Routing:Route"]);
            Assert.AreEqual("primary-cluster", firstBuilder.Configuration["Routing:Cluster"]);
            Assert.AreEqual("Primary", firstStore.GetStatus().SetStates.Single(set => set.Name == "RoutingProfile").DesiredValue);

            firstHost.StopAsync().GetAwaiter().GetResult();

            HostApplicationBuilder secondBuilder = CreateBuilder(directory.Path);
            RegisterRealisticConfigurationSets(secondBuilder);
            IConfigurationSetStateStore secondStore = secondBuilder.AddConfigurationSetStateFile(
                "ConfigurationSets.json",
                reloadOnChange: false);

            using IHost secondHost = secondBuilder.Build();

            Assert.AreEqual("primary-route", secondBuilder.Configuration["Routing:Route"]);
            Assert.AreEqual("primary-cluster", secondBuilder.Configuration["Routing:Cluster"]);
            Assert.AreEqual("degraded-features", secondBuilder.Configuration["Operations:Features"]);
            Assert.AreEqual("degraded-resilience", secondBuilder.Configuration["Operations:Resilience"]);
            Assert.AreEqual("degraded-diagnostics", secondBuilder.Configuration["Operations:Diagnostics"]);
            Assert.AreEqual("beta-release", secondBuilder.Configuration["Release:Marker"]);

            ConfigurationSetStateStoreStatus restartedStatus = secondStore.GetStatus();
            Assert.IsFalse(restartedStatus.HasPendingRestart);
            Assert.IsFalse(restartedStatus.HasDesiredStateDrift);
            Assert.AreEqual("Beta", restartedStatus.SetStates.Single(set => set.Name == "ReleaseChannel").ActiveValue);
        }

        private static void RegisterRealisticConfigurationSets(HostApplicationBuilder builder)
        {
            builder
                .AddConfigurationSet(
                    "RoutingProfile",
                    "Primary",
                    "Canary",
                    "Failover")
                .AddSwitchableJson(value => value switch
                {
                    "Primary" => "AppSettings/Routing/routes-primary.json",
                    "Canary" => "AppSettings/Routing/routes-canary.json",
                    "Failover" => "AppSettings/Routing/emergency-routing.json",
                    _ => throw new ArgumentOutOfRangeException(nameof(value)),
                })
                .AddSwitchableJson(value => value switch
                {
                    "Primary" => "AppSettings/Routing/clusters-primary.json",
                    "Canary" => "AppSettings/Routing/clusters-canary.json",
                    "Failover" => "AppSettings/Routing/clusters-failover.json",
                    _ => throw new ArgumentOutOfRangeException(nameof(value)),
                });

            builder
                .AddConfigurationSet(
                    "OperationalProfile",
                    "Normal",
                    "Degraded",
                    "Incident")
                .AddSwitchableJson(
                    "AppSettings/Operations",
                    "Features.json",
                    "Resilience.json",
                    "Diagnostics.json");

            builder
                .AddConfigurationSet(
                    "ReleaseChannel",
                    "Stable",
                    "Beta")
                .StateFileApplyMode(ConfigurationSetStateApplyMode.StartupOnly)
                .AddSwitchableJson(
                    "AppSettings/Features",
                    "Features.json");
        }

        private static void RegisterRecordingHostedService(HostApplicationBuilder builder)
        {
            builder.Services.AddSingleton<RecordingConfigurationSetHostedService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<RecordingConfigurationSetHostedService>());
        }

        private static void WriteProfileFiles(TemporaryDirectory directory)
        {
            directory.Write(
                "AppSettings/Routing/routes-primary.json",
                "{ \"Routing\": { \"Route\": \"primary-route\" } }");
            directory.Write(
                "AppSettings/Routing/routes-canary.json",
                "{ \"Routing\": { \"Route\": \"canary-route\" } }");
            directory.Write(
                "AppSettings/Routing/emergency-routing.json",
                "{ \"Routing\": { \"Route\": \"failover-route\" } }");
            directory.Write(
                "AppSettings/Routing/clusters-primary.json",
                "{ \"Routing\": { \"Cluster\": \"primary-cluster\" } }");
            directory.Write(
                "AppSettings/Routing/clusters-canary.json",
                "{ \"Routing\": { \"Cluster\": \"canary-cluster\" } }");
            directory.Write(
                "AppSettings/Routing/clusters-failover.json",
                "{ \"Routing\": { \"Cluster\": \"failover-cluster\" } }");

            WriteOperationalProfile(directory, "Normal", "normal");
            WriteOperationalProfile(directory, "Degraded", "degraded");
            WriteOperationalProfile(directory, "Incident", "incident");

            directory.Write(
                "AppSettings/Features/Stable/Features.json",
                "{ \"Release\": { \"Marker\": \"stable-release\" } }");
            directory.Write(
                "AppSettings/Features/Beta/Features.json",
                "{ \"Release\": { \"Marker\": \"beta-release\" } }");
        }

        private static void WriteOperationalProfile(
            TemporaryDirectory directory,
            string profile,
            string marker)
        {
            directory.Write(
                $"AppSettings/Operations/{profile}/Features.json",
                $"{{ \"Operations\": {{ \"Features\": \"{marker}-features\" }} }}");
            directory.Write(
                $"AppSettings/Operations/{profile}/Resilience.json",
                $"{{ \"Operations\": {{ \"Resilience\": \"{marker}-resilience\" }} }}");
            directory.Write(
                $"AppSettings/Operations/{profile}/Diagnostics.json",
                $"{{ \"Operations\": {{ \"Diagnostics\": \"{marker}-diagnostics\" }} }}");
        }

        private static HostApplicationBuilder CreateBuilder(string contentRootPath)
        {
            return new HostApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    ContentRootPath = contentRootPath,
                    EnvironmentName = Environments.Production,
                });
        }

        private sealed class RecordingConfigurationSetHostedService : IHostedService, IDisposable
        {
            private readonly object _gate = new();
            private readonly IConfigurationSetEventHub _events;
            private readonly List<ConfigurationSetNotification> _notifications = new();
            private IDisposable? _subscription;

            public RecordingConfigurationSetHostedService(IConfigurationSetEventHub events)
            {
                _events = events;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _subscription = _events.Subscribe(notification =>
                {
                    lock (_gate)
                    {
                        _notifications.Add(notification);
                    }
                });

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _subscription?.Dispose();
                _subscription = null;
                return Task.CompletedTask;
            }

            public ConfigurationSetNotification[] Snapshot()
            {
                lock (_gate)
                {
                    return _notifications.ToArray();
                }
            }

            public void Dispose()
            {
                _subscription?.Dispose();
                _subscription = null;
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "Eigenverft.ConfigurationSet.SystemTests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Write(string relativePath, string content)
            {
                string fullPath = System.IO.Path.Combine(Path, relativePath);
                string? parent = System.IO.Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.WriteAllText(fullPath, content);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                    {
                        Directory.Delete(Path, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
