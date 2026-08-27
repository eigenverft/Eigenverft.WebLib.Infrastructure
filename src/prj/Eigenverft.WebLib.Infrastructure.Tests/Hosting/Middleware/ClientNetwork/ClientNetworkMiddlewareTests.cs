using System.Net;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Features;
using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Middleware.ClientNetwork;

[TestClass]
public sealed class ClientNetworkMiddlewareTests
{
    [TestMethod]
    public async Task FeatureContainsNormalizedActualRemoteIpAddress()
    {
        DefaultHttpContext context = CreateContext("203.0.113.7");

        await InvokeAsync(context);

        IClientNetworkFeature feature = context.GetRequiredFeature<IClientNetworkFeature>();
        Assert.AreEqual(IPAddress.Parse("203.0.113.7"), feature.RemoteIpAddress);
        Assert.AreEqual(0, feature.ForwardedIpChain.Count);
        Assert.IsFalse(feature.HasMalformedForwardedIpInformation);
    }

    [TestMethod]
    public async Task Ipv4MappedIpv6ActualAddressIsNormalizedToIpv4()
    {
        DefaultHttpContext context = CreateContext("::ffff:192.0.2.44");

        await InvokeAsync(context);

        IClientNetworkFeature feature = context.GetRequiredFeature<IClientNetworkFeature>();
        Assert.AreEqual(IPAddress.Parse("192.0.2.44"), feature.RemoteIpAddress);
    }

    [TestMethod]
    public async Task ForwardedAndXForwardedForInformationShareOneTypedChainWithoutTrustEvaluation()
    {
        DefaultHttpContext context = CreateContext("10.0.0.5");
        context.Request.Headers["Forwarded"] = "for=198.51.100.1;proto=https, for=\"[2001:db8::1]:4711\"";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10, ::ffff:203.0.113.11";

        await InvokeAsync(context);

        IClientNetworkFeature feature = context.GetRequiredFeature<IClientNetworkFeature>();
        Assert.AreEqual(4, feature.ForwardedIpChain.Count);

        Assert.AreEqual(ClientForwardedIpSource.Forwarded, feature.ForwardedIpChain[0].Source);
        Assert.AreEqual(IPAddress.Parse("198.51.100.1"), feature.ForwardedIpChain[0].Address);
        Assert.IsFalse(feature.ForwardedIpChain[0].IsMalformed);

        Assert.AreEqual(ClientForwardedIpSource.Forwarded, feature.ForwardedIpChain[1].Source);
        Assert.AreEqual(IPAddress.Parse("2001:db8::1"), feature.ForwardedIpChain[1].Address);

        Assert.AreEqual(ClientForwardedIpSource.XForwardedFor, feature.ForwardedIpChain[2].Source);
        Assert.AreEqual(IPAddress.Parse("203.0.113.10"), feature.ForwardedIpChain[2].Address);

        Assert.AreEqual(ClientForwardedIpSource.XForwardedFor, feature.ForwardedIpChain[3].Source);
        Assert.AreEqual(IPAddress.Parse("203.0.113.11"), feature.ForwardedIpChain[3].Address);
        Assert.IsFalse(feature.HasMalformedForwardedIpInformation);
    }

    [TestMethod]
    public async Task MalformedForwardedInformationIsRetainedAndFlaggedInsteadOfRejected()
    {
        DefaultHttpContext context = CreateContext("10.0.0.5");
        context.Request.Headers["Forwarded"] = "for=unknown, for=\"[2001:db8::1]oops\"";
        context.Request.Headers["X-Forwarded-For"] = "not-an-ip, 192.0.2.2:443";

        await InvokeAsync(context);

        IClientNetworkFeature feature = context.GetRequiredFeature<IClientNetworkFeature>();
        Assert.AreEqual(4, feature.ForwardedIpChain.Count);
        Assert.IsTrue(feature.HasMalformedForwardedIpInformation);

        Assert.IsTrue(feature.ForwardedIpChain[0].IsMalformed);
        Assert.IsNull(feature.ForwardedIpChain[0].Address);
        Assert.AreEqual("unknown", feature.ForwardedIpChain[0].RawValue);

        Assert.IsTrue(feature.ForwardedIpChain[1].IsMalformed);
        Assert.IsTrue(feature.ForwardedIpChain[2].IsMalformed);

        Assert.IsFalse(feature.ForwardedIpChain[3].IsMalformed);
        Assert.AreEqual(IPAddress.Parse("192.0.2.2"), feature.ForwardedIpChain[3].Address);
    }

    private static DefaultHttpContext CreateContext(string remoteIpAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIpAddress);
        return context;
    }

    private static Task InvokeAsync(HttpContext context)
    {
        var middleware = new ClientNetworkMiddleware(static _ => Task.CompletedTask);
        return middleware.InvokeAsync(context);
    }
}
