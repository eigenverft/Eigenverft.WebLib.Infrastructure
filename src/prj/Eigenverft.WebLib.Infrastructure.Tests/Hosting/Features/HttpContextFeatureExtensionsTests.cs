using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Features;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Features;

[TestClass]
public sealed class HttpContextFeatureExtensionsTests
{
    [TestMethod]
    public void GetAndTryGetReturnMissingFeatureWithoutCreatingParallelStorage()
    {
        var context = new DefaultHttpContext();

        Assert.IsNull(context.GetFeature<TestFeature>());
        Assert.IsFalse(context.TryGetFeature<TestFeature>(out TestFeature? feature));
        Assert.IsNull(feature);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.GetRequiredFeature<TestFeature>());
    }

    [TestMethod]
    public void SetGetRequiredAndRemoveOperateOnHttpContextFeatures()
    {
        var context = new DefaultHttpContext();
        var expected = new TestFeature("expected");

        context.SetFeature(expected);

        Assert.AreSame(expected, context.Features.Get<TestFeature>());
        Assert.AreSame(expected, context.GetFeature<TestFeature>());
        Assert.AreSame(expected, context.GetRequiredFeature<TestFeature>());
        Assert.IsTrue(context.TryGetFeature<TestFeature>(out TestFeature? found));
        Assert.AreSame(expected, found);
        Assert.IsTrue(context.RemoveFeature<TestFeature>());
        Assert.IsNull(context.Features.Get<TestFeature>());
        Assert.IsFalse(context.RemoveFeature<TestFeature>());
    }

    [TestMethod]
    public void GetOrCreateCreatesOnceAndStoresTheTypedFeature()
    {
        var context = new DefaultHttpContext();
        var factoryCalls = 0;

        TestFeature first = context.GetOrCreateFeature(() =>
        {
            factoryCalls++;
            return new TestFeature("created");
        });

        TestFeature second = context.GetOrCreateFeature(() =>
        {
            factoryCalls++;
            return new TestFeature("unexpected");
        });

        Assert.AreEqual(1, factoryCalls);
        Assert.AreSame(first, second);
        Assert.AreSame(first, context.Features.Get<TestFeature>());
    }

    [TestMethod]
    public void ParameterlessGetOrCreateUsesNewConstraintAndStoresFeature()
    {
        var context = new DefaultHttpContext();

        DefaultConstructibleFeature first = context.GetOrCreateFeature<DefaultConstructibleFeature>();
        DefaultConstructibleFeature second = context.GetOrCreateFeature<DefaultConstructibleFeature>();

        Assert.AreSame(first, second);
        Assert.AreSame(first, context.Features.Get<DefaultConstructibleFeature>());
    }

    private sealed class DefaultConstructibleFeature
    {
    }

    private sealed class TestFeature
    {
        public TestFeature(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }
}
