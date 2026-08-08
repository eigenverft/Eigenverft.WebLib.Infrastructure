using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetEventHubTests
    {
        [TestMethod]
        public void FluentRegistrationHandleWiresSingleAndMultipleFiles()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("AppSettings", "Proxy", "Stable", "ProxySettings.json"), "{ \"ProxyMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Proxy", "Stable", "EdgeFilters.json"), "{ \"FilterMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Proxy", "Stable", "Behaviors.json"), "{ \"BehaviorMode\": \"Stable\" }");
            directory.Write(Path.Combine("AppSettings", "Proxy", "Experimental", "ProxySettings.json"), "{ \"ProxyMode\": \"Experimental\" }");
            directory.Write(Path.Combine("AppSettings", "Proxy", "Experimental", "EdgeFilters.json"), "{ \"FilterMode\": \"Experimental\" }");
            directory.Write(Path.Combine("AppSettings", "Proxy", "Experimental", "Behaviors.json"), "{ \"BehaviorMode\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);

            ConfigurationSetRegistration proxySet = builder.AddConfigurationSet(
                "ProxySet",
                "Stable",
                "Experimental");

            proxySet
                .AddSwitchableJson(
                    Path.Combine("AppSettings", "Proxy"),
                    "ProxySettings.json")
                .AddSwitchableJson(
                    Path.Combine("AppSettings", "Proxy"),
                    "EdgeFilters.json",
                    "Behaviors.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");

            Assert.AreSame(proxySet.Coordinator, coordinator);
            Assert.AreEqual("Stable", builder.Configuration["ProxyMode"]);
            Assert.AreEqual("Stable", builder.Configuration["FilterMode"]);
            Assert.AreEqual("Stable", builder.Configuration["BehaviorMode"]);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", builder.Configuration["ProxyMode"]);
            Assert.AreEqual("Experimental", builder.Configuration["FilterMode"]);
            Assert.AreEqual("Experimental", builder.Configuration["BehaviorMode"]);
            CollectionAssert.AreEqual(
                new[]
                {
                    "ProxySet:AppSettings/Proxy/ProxySettings.json",
                    "ProxySet:AppSettings/Proxy/EdgeFilters.json",
                    "ProxySet:AppSettings/Proxy/Behaviors.json",
                },
                coordinator.BoundParticipantNames.ToArray());
        }

        [TestMethod]
        public void FluentRegistrationDerivesDistinctNamesForSameFileNameInDifferentRoots()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("First", "Stable", "Settings.json"), "{ \"FirstValue\": \"A\" }");
            directory.Write(Path.Combine("Second", "Stable", "Settings.json"), "{ \"SecondValue\": \"B\" }");

            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            ConfigurationSetRegistration registration = builder.AddConfigurationSet("AppSet", "Stable");
            registration
                .AddSwitchableJson("First", "Settings.json")
                .AddSwitchableJson("Second", "Settings.json");

            using IHost host = builder.Build();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("AppSet");

            Assert.AreEqual("A", builder.Configuration["FirstValue"]);
            Assert.AreEqual("B", builder.Configuration["SecondValue"]);
            CollectionAssert.AreEqual(
                new[] { "AppSet:First/Settings.json", "AppSet:Second/Settings.json" },
                coordinator.BoundParticipantNames.ToArray());
        }

        [TestMethod]
        public void EventHubDistinguishesSourceSwitchFromEffectiveConfigurationChange()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("Proxy", "Stable", "Settings.json"), "{ \"Value\": \"Same\" }");
            directory.Write(Path.Combine("Proxy", "Experimental", "Settings.json"), "{ \"Value\": \"Same\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            ConfigurationSetRegistration registration = builder.AddConfigurationSet(
                "ProxySet",
                "Stable",
                "Experimental");
            registration.AddSwitchableJson("Proxy", "Settings.json");

            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");
            ConfigurationSetNotification? observed = null;
            using IDisposable subscription = hub.Subscribe(notification => observed = notification);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.IsNotNull(observed);
            Assert.AreSame(result, observed.Result);
            Assert.AreEqual("ProxySet", observed.SetName);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchSucceeded, observed.Kind);
            Assert.IsTrue(result.ValueChanged);
            Assert.IsTrue(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.IsTrue(result.HasChanges);
            Assert.AreEqual(1, result.ParticipantResults.Count);
            Assert.IsTrue(result.ParticipantResults[0].SourceChanged);
            Assert.IsFalse(result.ParticipantResults[0].ConfigurationChanged);
        }

        [TestMethod]
        public void EventHubReportsEffectiveConfigurationChangeWhenParticipantDataChanges()
        {
            using var directory = new TemporaryDirectory();
            directory.Write(Path.Combine("Proxy", "Stable", "Settings.json"), "{ \"Value\": \"Stable\" }");
            directory.Write(Path.Combine("Proxy", "Experimental", "Settings.json"), "{ \"Value\": \"Experimental\" }");
            HostApplicationBuilder builder = CreateBuilder(directory.Path);
            builder.AddConfigurationSet("ProxySet", "Stable", "Experimental")
                .AddSwitchableJson("Proxy", "Settings.json");

            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            IConfigurationSetCoordinator coordinator =
                host.Services.GetRequiredKeyedService<IConfigurationSetCoordinator>("ProxySet");
            ConfigurationSetNotification? observed = null;
            using IDisposable subscription = hub.Subscribe("ProxySet", notification => observed = notification);

            _ = coordinator.TrySwitch("Experimental");

            Assert.IsNotNull(observed);
            Assert.IsTrue(observed.Result.SourceChanged);
            Assert.IsTrue(observed.Result.ConfigurationChanged);
            Assert.IsTrue(observed.Result.HasChanges);
        }

        [TestMethod]
        public void AlreadyActiveNotificationIsObservableWithoutReportingChanges()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSet("ProxySet", "Stable", "Experimental").Coordinator;
            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            ConfigurationSetNotification? observed = null;
            using IDisposable subscription = hub.Subscribe(notification => observed = notification);

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Stable");

            Assert.IsNotNull(observed);
            Assert.AreEqual(ConfigurationSetEventKind.SwitchAlreadyActive, observed.Kind);
            Assert.AreEqual(ConfigurationSetSwitchStatus.AlreadyActive, result.Status);
            Assert.IsFalse(result.ValueChanged);
            Assert.IsFalse(result.SourceChanged);
            Assert.IsFalse(result.ConfigurationChanged);
            Assert.IsFalse(result.HasChanges);
            Assert.IsFalse(observed.HasChanges);
            Assert.AreEqual(0, result.ParticipantResults.Count);
        }

        [TestMethod]
        public void EventHubSupportsSetFilteringAndIdempotentUnsubscribe()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator environment = builder.AddConfigurationSet("EnvironmentSet", "Development", "Production").Coordinator;
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSet("ProxySet", "Stable", "Experimental").Coordinator;
            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            int allCount = 0;
            int proxyCount = 0;
            using IDisposable all = hub.Subscribe(_ => Interlocked.Increment(ref allCount));
            IDisposable proxyOnly = hub.Subscribe("ProxySet", _ => Interlocked.Increment(ref proxyCount));

            _ = environment.TrySwitch("Production");
            _ = proxy.TrySwitch("Experimental");
            proxyOnly.Dispose();
            proxyOnly.Dispose();
            _ = proxy.TrySwitch("Stable");

            Assert.AreEqual(3, allCount);
            Assert.AreEqual(1, proxyCount);
        }

        [TestMethod]
        public void ThrowingHubSubscriberCannotBlockLaterSubscribersOrChangeSwitchOutcome()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSet("ProxySet", "Stable", "Experimental").Coordinator;
            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            int laterCount = 0;
            using IDisposable first = hub.Subscribe(_ => throw new InvalidOperationException("observer failure"));
            using IDisposable second = hub.Subscribe(_ => Interlocked.Increment(ref laterCount));

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.AreEqual(ConfigurationSetSwitchStatus.Succeeded, result.Status);
            Assert.AreEqual("Experimental", coordinator.ActiveValue);
            Assert.AreEqual(1, laterCount);
        }

        [TestMethod]
        public async Task HubAssignsUniqueProcessWideSequencesUnderConcurrentSetSwitches()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator environment = builder.AddConfigurationSet("EnvironmentSet", "Development", "Production").Coordinator;
            IConfigurationSetCoordinator proxy = builder.AddConfigurationSet("ProxySet", "Stable", "Experimental").Coordinator;
            using IHost host = builder.Build();
            IConfigurationSetEventHub hub = host.Services.GetRequiredService<IConfigurationSetEventHub>();
            var sequences = new ConcurrentBag<long>();
            using IDisposable subscription = hub.Subscribe(notification => sequences.Add(notification.Sequence));

            Task[] operations = Enumerable.Range(0, 40)
                .Select(index => Task.Run(() =>
                {
                    if (index % 2 == 0)
                    {
                        _ = environment.TrySwitch(index % 4 == 0 ? "Production" : "Development");
                    }
                    else
                    {
                        _ = proxy.TrySwitch(index % 4 == 1 ? "Experimental" : "Stable");
                    }
                }))
                .ToArray();

            await Task.WhenAll(operations);

            Assert.AreEqual(40, sequences.Count);
            Assert.AreEqual(40, sequences.Distinct().Count());
            CollectionAssert.AreEqual(
                Enumerable.Range(1, 40).Select(value => (long)value).ToArray(),
                sequences.OrderBy(value => value).ToArray());
        }

        [TestMethod]
        public void HostedServiceCanSubscribeThroughDiAndObserveCompletedSetSwitch()
        {
            HostApplicationBuilder builder = CreateBuilder();
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSet("ProxySet", "Stable", "Experimental").Coordinator;
            builder.Services.AddSingleton<RecordingSetHostedService>();
            builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<RecordingSetHostedService>());
            using IHost host = builder.Build();
            host.StartAsync().GetAwaiter().GetResult();
            RecordingSetHostedService service = host.Services.GetRequiredService<RecordingSetHostedService>();

            ConfigurationSetSwitchResult result = coordinator.TrySwitch("Experimental");

            Assert.IsNotNull(service.LastNotification);
            Assert.AreSame(result, service.LastNotification.Result);
            Assert.AreEqual("ProxySet", service.LastNotification.SetName);
            host.StopAsync().GetAwaiter().GetResult();
        }

        private static HostApplicationBuilder CreateBuilder(string? contentRootPath = null)
        {
            return new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRootPath,
                DisableDefaults = true,
            });
        }

        private sealed class RecordingSetHostedService : IHostedService, IDisposable
        {
            private readonly IConfigurationSetEventHub _hub;
            private IDisposable? _subscription;

            public RecordingSetHostedService(IConfigurationSetEventHub hub)
            {
                _hub = hub;
            }

            public ConfigurationSetNotification? LastNotification { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _subscription = _hub.Subscribe(notification => LastNotification = notification);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _subscription?.Dispose();
                _subscription = null;
                return Task.CompletedTask;
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
                    "Eigenverft.WebLib.Infrastructure.ConfigurationSetEventHub.Tests",
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
