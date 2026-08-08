using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Configuration.ConfigurationSets
{
    [TestClass]
    public sealed class ConfigurationSetManagerTests
    {
        [TestMethod]
        public void ManagerWorksWithoutStateFileAndSwitchesCompleteConfigurationBeforeReturning()
        {
            string root = CreateTempDirectory();
            try
            {
                WriteJson(root, "AppSettings/Routing/Primary/Routes.json", "primary-backend");
                WriteJson(root, "AppSettings/Routing/Failover/Routes.json", "fallback-backend");

                HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ContentRootPath = root,
                });

                builder
                    .AddConfigurationSet(
                        "RoutingProfile",
                        "Primary",
                        "Failover")
                    .AddSwitchableJson(
                        "AppSettings/Routing",
                        "Routes.json");

                using IHost host = builder.Build();
                IConfigurationSetManager manager = host.Services.GetRequiredService<IConfigurationSetManager>();
                IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();

                Assert.IsNull(host.Services.GetService<IConfigurationSetStateStore>());
                Assert.AreEqual("primary-backend", configuration["Routing:Backend"]);

                IReadOnlyList<ConfigurationSetStatus> all = manager.GetStatus();
                Assert.HasCount(1, all);
                Assert.AreEqual("RoutingProfile", all[0].Name);
                Assert.AreEqual("Primary", all[0].InitialValue);
                Assert.AreEqual("Primary", all[0].ActiveValue);
                CollectionAssert.AreEqual(
                    new[] { "Primary", "Failover" },
                    all[0].AllowedValues.ToArray());

                bool switched = manager.TrySwitchRuntime("RoutingProfile", "Failover", out ConfigurationSetSwitchResult? result);

                Assert.IsTrue(switched);
                Assert.IsNotNull(result);
                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual("Failover", result.ActiveValue);
                Assert.AreEqual("fallback-backend", configuration["Routing:Backend"]);

                Assert.IsTrue(manager.TrySwitchRuntime(
                    "RoutingProfile",
                    "Failover",
                    out ConfigurationSetSwitchResult? alreadyActive));
                Assert.IsNotNull(alreadyActive);
                Assert.AreEqual(ConfigurationSetSwitchStatus.AlreadyActive, alreadyActive.Status);

                Assert.IsTrue(manager.TryGetStatus("RoutingProfile", out ConfigurationSetStatus? status));
                Assert.IsNotNull(status);
                Assert.AreEqual("Primary", status.InitialValue);
                Assert.AreEqual("Failover", status.ActiveValue);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void ManagerTrySwitchRuntimeReturnsFalseForUnknownOrRejectedAndResultDistinguishesThem()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            _ = builder.AddConfigurationSet("OperationalProfile", "Normal", "Degraded", "Incident");

            using IHost host = builder.Build();
            IConfigurationSetManager manager = host.Services.GetRequiredService<IConfigurationSetManager>();

            Assert.IsFalse(manager.TrySwitchRuntime("MissingProfile", "Degraded", out ConfigurationSetSwitchResult? missing));
            Assert.IsNull(missing);

            Assert.IsFalse(manager.TrySwitchRuntime("OperationalProfile", "Unknown", out ConfigurationSetSwitchResult? rejected));
            Assert.IsNotNull(rejected);
            Assert.AreEqual(ConfigurationSetSwitchStatus.Rejected, rejected.Status);
            Assert.AreEqual(ConfigurationSetSwitchFailureKind.ValueNotAllowed, rejected.FailureKind);
        }

        [TestMethod]
        public void JsonStateStoreAlsoExposesPersistenceNeutralDesiredStateContract()
        {
            string root = CreateTempDirectory();
            try
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ContentRootPath = root,
                });

                _ = builder.AddConfigurationSet("OperationalProfile", "Normal", "Degraded", "Incident");
                _ = builder.AddConfigurationSetStateFile("ConfigurationSets.json", watchForChanges: false);

                using IHost host = builder.Build();
                IConfigurationSetDesiredStateStore desiredState =
                    host.Services.GetRequiredService<IConfigurationSetDesiredStateStore>();

                IReadOnlyList<ConfigurationSetStateStatus> initialStates = desiredState.GetDesiredStateStatus();
                Assert.HasCount(1, initialStates);
                ConfigurationSetStateStatus initial = initialStates[0];
                Assert.AreEqual("Normal", initial.InitialValue);
                Assert.AreEqual("Normal", initial.ActiveValue);
                Assert.AreEqual("Normal", initial.DesiredValue);

                ConfigurationSetStateApplyResult result =
                    desiredState.TrySetDesiredValue("OperationalProfile", "Degraded");

                Assert.IsTrue(result.Succeeded);
                IReadOnlyList<ConfigurationSetStateStatus> changedStates = desiredState.GetDesiredStateStatus();
                Assert.HasCount(1, changedStates);
                ConfigurationSetStateStatus changed = changedStates[0];
                Assert.AreEqual("Degraded", changed.ActiveValue);
                Assert.AreEqual("Degraded", changed.DesiredValue);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "Eigenverft.ConfigurationSetManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteJson(string root, string relativePath, string backend)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{{\"Routing\":{{\"Backend\":\"{backend}\"}}}}");
        }
    }
}
