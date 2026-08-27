using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Middleware.Infrastructure;

[TestClass]
public sealed class ConfiguredOptionsDesignTests
{
    [TestMethod]
    public void ConfigureAllPlusNamedOptionsProvidesGlobalBaselineAndIndependentVariantWithoutCustomMonitor()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.ConfigureAll<SampleOptions>(options => options.GlobalValue = "global");
        services.Configure<SampleOptions>("local-variant", options => options.LocalValue = "local");

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<SampleOptions> monitor = provider.GetRequiredService<IOptionsMonitor<SampleOptions>>();

        SampleOptions defaultOptions = monitor.CurrentValue;
        SampleOptions localOptions = monitor.Get("local-variant");

        Assert.AreEqual("global", defaultOptions.GlobalValue);
        Assert.IsNull(defaultOptions.LocalValue);
        Assert.AreEqual("global", localOptions.GlobalValue);
        Assert.AreEqual("local", localOptions.LocalValue);
        Assert.AreNotSame(defaultOptions, localOptions);
    }

    private sealed class SampleOptions
    {
        public string? GlobalValue { get; set; }

        public string? LocalValue { get; set; }
    }
}
