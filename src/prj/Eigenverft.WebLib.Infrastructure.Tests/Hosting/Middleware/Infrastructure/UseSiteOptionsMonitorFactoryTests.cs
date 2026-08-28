using System;
using System.Collections.Generic;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides;
using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Middleware.Infrastructure
{
    [TestClass]
    public sealed class UseSiteOptionsMonitorFactoryTests
    {
        [TestMethod]
        public void BaselineUsesNormalRegisteredOptions()
        {
            var services = new ServiceCollection();
            services.AddOptions<TestOptions>();
            services.Configure<TestOptions>(options =>
            {
                options.TimeoutSeconds = 20;
                options.Mode = "registered";
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            IOptionsMonitor<TestOptions> options = provider.GetRequiredService<IOptionsMonitor<TestOptions>>();

            Assert.AreEqual(20, options.CurrentValue.TimeoutSeconds);
            Assert.AreEqual("registered", options.CurrentValue.Mode);
        }

        [TestMethod]
        public void LocalScalarOverrideDoesNotChangeGlobalBaseline()
        {
            var services = new ServiceCollection();
            services.AddOptions<TestOptions>();
            services.Configure<TestOptions>(options => options.TimeoutSeconds = 20);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 5);
            using IDisposable localDisposer = (IDisposable)local;

            Assert.AreEqual(5, local.CurrentValue.TimeoutSeconds);
            Assert.AreEqual(
                20,
                provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue.TimeoutSeconds);
        }

        [TestMethod]
        public void TwoLocalOverridesRemainIndependent()
        {
            var services = new ServiceCollection();
            services.AddOptions<TestOptions>();
            services.Configure<TestOptions>(options => options.TimeoutSeconds = 20);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> first = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 5);
            IOptionsMonitor<TestOptions> second = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 10);
            using IDisposable firstDisposer = (IDisposable)first;
            using IDisposable secondDisposer = (IDisposable)second;

            Assert.AreEqual(5, first.CurrentValue.TimeoutSeconds);
            Assert.AreEqual(10, second.CurrentValue.TimeoutSeconds);
            Assert.AreEqual(
                20,
                provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue.TimeoutSeconds);
        }

        [TestMethod]
        public void ConfigurationAndRegisteredCodeRunBeforeLocalOverride()
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foo:TimeoutSeconds"] = "30",
                ["Foo:Mode"] = "configuration",
            });

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions<TestOptions>().BindConfiguration("Foo");
            services.Configure<TestOptions>(options => options.TimeoutSeconds = 20);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 3);
            using IDisposable localDisposer = (IDisposable)local;

            Assert.AreEqual(3, local.CurrentValue.TimeoutSeconds);
            Assert.AreEqual("configuration", local.CurrentValue.Mode);

            TestOptions global = provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue;
            Assert.AreEqual(20, global.TimeoutSeconds);
            Assert.AreEqual("configuration", global.Mode);
        }

        [TestMethod]
        public void NetLibCollectionReplacementIsReplayedForTheLocalInstance()
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foo:Hosts:0"] = "example.com",
            });

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services
                .AddOptions<TestOptions>()
                .BindReplacingCollectionDefaults("Foo");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 3);
            using IDisposable localDisposer = (IDisposable)local;

            CollectionAssert.AreEqual(new[] { "example.com" }, local.CurrentValue.Hosts);
            Assert.AreEqual(3, local.CurrentValue.TimeoutSeconds);

            TestOptions global = provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue;
            CollectionAssert.AreEqual(new[] { "example.com" }, global.Hosts);
            Assert.AreEqual(30, global.TimeoutSeconds);
        }

        [TestMethod]
        public void LocalMutableCollectionChangesDoNotMutateTheGlobalOptions()
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foo:Hosts:0"] = "example.com",
            });

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services
                .AddOptions<TestOptions>()
                .BindReplacingCollectionDefaults("Foo");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.Hosts.Add("special.example.com"));
            using IDisposable localDisposer = (IDisposable)local;

            CollectionAssert.AreEqual(
                new[] { "example.com", "special.example.com" },
                local.CurrentValue.Hosts);
            CollectionAssert.AreEqual(
                new[] { "example.com" },
                provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue.Hosts);
        }

        [TestMethod]
        public void FreshOptionsInstanceIsolatesArraysDictionariesAndNestedObjectsCreatedByDefaults()
        {
            var services = new ServiceCollection();
            services.AddOptions<TestOptions>();

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(options =>
            {
                options.Aliases[0] = "local";
                options.Metadata["local"] = "yes";
                options.Nested.Value = "local";
            });
            using IDisposable localDisposer = (IDisposable)local;

            TestOptions localOptions = local.CurrentValue;
            TestOptions global = provider.GetRequiredService<IOptionsMonitor<TestOptions>>().CurrentValue;

            Assert.AreEqual("local", localOptions.Aliases[0]);
            Assert.AreEqual("default", global.Aliases[0]);
            Assert.AreEqual("yes", localOptions.Metadata["local"]);
            Assert.IsFalse(global.Metadata.ContainsKey("local"));
            Assert.AreEqual("local", localOptions.Nested.Value);
            Assert.AreEqual("default", global.Nested.Value);
        }

        [TestMethod]
        public void InvalidLocalOverrideRunsRegisteredValidationAndFails()
        {
            var services = new ServiceCollection();
            services
                .AddOptions<TestOptions>()
                .Validate(options => options.TimeoutSeconds > 0, "TimeoutSeconds must be positive.");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = -10);
            using IDisposable localDisposer = (IDisposable)local;

            Assert.ThrowsExactly<OptionsValidationException>(() => _ = local.CurrentValue);
        }

        [TestMethod]
        public void LocalOverrideRunsAfterRegisteredPostConfigureAndBeforeValidation()
        {
            var services = new ServiceCollection();
            services
                .AddOptions<TestOptions>()
                .Configure(options => options.Sequence += "|configure")
                .PostConfigure(options => options.Sequence += "|post")
                .Validate(
                    options => options.Sequence.EndsWith("|local", StringComparison.Ordinal),
                    "Local override must run before validation.");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.Sequence += "|local");
            using IDisposable localDisposer = (IDisposable)local;

            Assert.AreEqual("default|configure|post|local", local.CurrentValue.Sequence);
        }

        [TestMethod]
        public void OnChangePublishesTheRebuiltLocallyOverriddenValue()
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foo:TimeoutSeconds"] = "30",
                ["Foo:Mode"] = "A",
            });

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions<TestOptions>().BindConfiguration("Foo");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 5);
            using IDisposable localDisposer = (IDisposable)local;

            TestOptions? changed = null;
            using IDisposable? subscription = local.OnChange((options, _) => changed = options);

            configuration["Foo:TimeoutSeconds"] = "60";
            configuration["Foo:Mode"] = "B";
            ((IConfigurationRoot)configuration).Reload();

            Assert.IsNotNull(changed);
            Assert.AreEqual(5, changed.TimeoutSeconds);
            Assert.AreEqual("B", changed.Mode);
        }

        [TestMethod]
        public void ConfigurationReloadRebuildsTheLocalBaselineAndReappliesTheOverride()
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foo:TimeoutSeconds"] = "30",
                ["Foo:Mode"] = "A",
            });

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions<TestOptions>().BindConfiguration("Foo");

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            IOptionsMonitor<TestOptions> local = app.CreateUseSiteOptionsMonitor<TestOptions>(
                options => options.TimeoutSeconds = 5);
            using IDisposable localDisposer = (IDisposable)local;
            IOptionsMonitor<TestOptions> global = provider.GetRequiredService<IOptionsMonitor<TestOptions>>();

            Assert.AreEqual(5, local.CurrentValue.TimeoutSeconds);
            Assert.AreEqual("A", local.CurrentValue.Mode);
            Assert.AreEqual(30, global.CurrentValue.TimeoutSeconds);

            configuration["Foo:TimeoutSeconds"] = "60";
            configuration["Foo:Mode"] = "B";
            ((IConfigurationRoot)configuration).Reload();

            Assert.AreEqual(5, local.CurrentValue.TimeoutSeconds);
            Assert.AreEqual("B", local.CurrentValue.Mode);
            Assert.AreEqual(60, global.CurrentValue.TimeoutSeconds);
            Assert.AreEqual("B", global.CurrentValue.Mode);
        }

        public sealed class TestOptions
        {
            public int TimeoutSeconds { get; set; } = 30;

            public string Mode { get; set; } = "default";

            public List<string> Hosts { get; set; } = new() { "localhost" };

            public string[] Aliases { get; set; } = new[] { "default" };

            public Dictionary<string, string> Metadata { get; set; } = new()
            {
                ["default"] = "yes",
            };

            public NestedOptions Nested { get; set; } = new();

            public string Sequence { get; set; } = "default";
        }

        public sealed class NestedOptions
        {
            public string Value { get; set; } = "default";
        }
    }
}
