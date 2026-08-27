using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Middleware.Infrastructure;

[TestClass]
public sealed class ServiceProviderRegistrationExtensionsTests
{
    [TestMethod]
    public void RegisteredServiceIsCheckedWithoutActivation()
    {
        var activations = 0;
        var services = new ServiceCollection();
        services.AddSingleton<ActivationTrackedService>(_ =>
        {
            activations++;
            return new ActivationTrackedService();
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.EnsureServicesRegistered<ActivationTrackedService>();

        Assert.AreEqual(0, activations);
    }

    [TestMethod]
    public void MissingServiceProducesActionableRegistrationMessageWithoutActivation()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            provider.EnsureServicesRegistered<ActivationTrackedService>("Call services.AddTracked()."));

        StringAssert.Contains(exception.Message, typeof(ActivationTrackedService).FullName!);
        StringAssert.Contains(exception.Message, "Call services.AddTracked().");
    }

    [TestMethod]
    public void OpenGenericProbeIsRejectedInsteadOfBeingActivatedOrGuessed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IGenericService<>), typeof(GenericService<>));
        using ServiceProvider provider = services.BuildServiceProvider();

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            provider.EnsureServicesRegistered(typeof(IGenericService<>)));

        StringAssert.Contains(exception.Message, "Open generic service type");
        StringAssert.Contains(exception.Message, nameof(IServiceProviderIsService));
    }

    private sealed class ActivationTrackedService
    {
    }

    private interface IGenericService<T>
    {
    }

    private sealed class GenericService<T> : IGenericService<T>
    {
    }
}
