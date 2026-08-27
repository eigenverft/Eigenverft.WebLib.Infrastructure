using System;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class WebLibFloodProtectionTests
{
    [TestMethod]
    public void DefaultsAreFiniteConfigurableStartingValues()
    {
        var options = new WebLibFloodProtectionOptions();

        Assert.AreEqual(40, options.BurstSize);
        Assert.AreEqual(10, options.RequestsPerSecond);
        Assert.AreEqual(20, options.QueueLimit);
        Assert.AreEqual(MissingClientIpBehavior.SharedPartition, options.MissingClientIpBehavior);
        Assert.IsNull(options.GlobalConcurrencyLimit);
    }

    [TestMethod]
    public void ClientIpPartitionKeyNormalizesMappedIpv4AndIpv6Scope()
    {
        var mappedContext = new DefaultHttpContext();
        mappedContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.17");

        var nativeContext = new DefaultHttpContext();
        nativeContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.17");

        var scopedIpv6Context = new DefaultHttpContext();
        scopedIpv6Context.Connection.RemoteIpAddress = new IPAddress(
            IPAddress.Parse("fe80::1234").GetAddressBytes(),
            7);

        Assert.AreEqual("192.0.2.17", ClientIpPartitionKey.Resolve(mappedContext));
        Assert.AreEqual(ClientIpPartitionKey.Resolve(nativeContext), ClientIpPartitionKey.Resolve(mappedContext));
        Assert.AreEqual("fe80::1234", ClientIpPartitionKey.Resolve(scopedIpv6Context));
    }

    [TestMethod]
    public void RegistrationUses429RejectionStatusCode()
    {
        using ServiceProvider provider = BuildProvider();
        RateLimiterOptions frameworkOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.AreEqual(StatusCodes.Status429TooManyRequests, frameworkOptions.RejectionStatusCode);
        Assert.IsNotNull(frameworkOptions.GlobalLimiter);
        Assert.IsNotNull(frameworkOptions.OnRejected);
    }

    [TestMethod]
    public void DifferentClientIpsReceiveIndependentTokenBuckets()
    {
        using ServiceProvider provider = BuildProvider(ConfigureSingleTokenNoQueue);
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var firstIp = CreateContext("192.0.2.10");
        var secondIp = CreateContext("192.0.2.11");

        using RateLimitLease firstLease = limiter.AttemptAcquire(firstIp, permitCount: 1);
        using RateLimitLease repeatedFirstIpLease = limiter.AttemptAcquire(firstIp, permitCount: 1);
        using RateLimitLease secondIpLease = limiter.AttemptAcquire(secondIp, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(repeatedFirstIpLease.IsAcquired);
        Assert.IsTrue(secondIpLease.IsAcquired);
    }

    [TestMethod]
    public void MappedIpv4AndNativeIpv4ShareOnePartition()
    {
        using ServiceProvider provider = BuildProvider(ConfigureSingleTokenNoQueue);
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var mapped = CreateContext("::ffff:198.51.100.9");
        var native = CreateContext("198.51.100.9");

        using RateLimitLease mappedLease = limiter.AttemptAcquire(mapped, permitCount: 1);
        using RateLimitLease nativeLease = limiter.AttemptAcquire(native, permitCount: 1);

        Assert.IsTrue(mappedLease.IsAcquired);
        Assert.IsFalse(nativeLease.IsAcquired);
    }

    [TestMethod]
    public void MissingClientIpUsesSharedPartitionByDefault()
    {
        using ServiceProvider provider = BuildProvider(ConfigureSingleTokenNoQueue);
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();

        using RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(secondLease.IsAcquired);
    }

    [TestMethod]
    public void MissingClientIpCanBypassOnlyThePerIpLimiter()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            ConfigureSingleTokenNoQueue(options);
            options.MissingClientIpBehavior = MissingClientIpBehavior.BypassPerIpLimit;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();

        using RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsTrue(secondLease.IsAcquired);
    }

    [TestMethod]
    public void MissingClientIpBypassStillUsesGlobalConcurrencyLimiter()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            ConfigureSingleTokenNoQueue(options);
            options.MissingClientIpBehavior = MissingClientIpBehavior.BypassPerIpLimit;
            options.GlobalConcurrencyLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();

        RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease blockedByGlobalLimit = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(blockedByGlobalLimit.IsAcquired);

        firstLease.Dispose();
        using RateLimitLease afterRelease = limiter.AttemptAcquire(second, permitCount: 1);
        Assert.IsTrue(afterRelease.IsAcquired);
    }

    [TestMethod]
    public async Task QueueLimitBoundsQueuedWorkForOneClient()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.BurstSize = 1;
            options.RequestsPerSecond = 1;
            options.QueueLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);
        var context = CreateContext("203.0.113.20");

        using RateLimitLease active = limiter.AttemptAcquire(context, permitCount: 1);
        Assert.IsTrue(active.IsAcquired);

        using var cancellation = new CancellationTokenSource();
        Task<RateLimitLease> queued = limiter.AcquireAsync(context, permitCount: 1, cancellation.Token).AsTask();

        RateLimiterStatistics? statistics = limiter.GetStatistics(context);
        Assert.IsNotNull(statistics);
        Assert.AreEqual(1L, statistics.CurrentQueuedCount);

        using RateLimitLease overflow = limiter.AttemptAcquire(context, permitCount: 1);
        Assert.IsFalse(overflow.IsAcquired);
        Assert.IsFalse(queued.IsCompleted);

        cancellation.Cancel();
        try
        {
            await queued;
            Assert.Fail("Expected the queued acquire to observe cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected: TaskCanceledException is also a valid cancellation result.
        }
    }

    [TestMethod]
    public async Task TokenBucketRejectionEmitsFrameworkRetryAfterMetadataAsHeader()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.BurstSize = 1;
            options.RequestsPerSecond = 1;
            options.QueueLimit = 0;
        });
        RateLimiterOptions frameworkOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);
        var context = CreateContext("203.0.113.21");

        using RateLimitLease accepted = limiter.AttemptAcquire(context, permitCount: 1);
        using RateLimitLease rejected = limiter.AttemptAcquire(context, permitCount: 1);

        Assert.IsTrue(accepted.IsAcquired);
        Assert.IsFalse(rejected.IsAcquired);
        Assert.IsTrue(rejected.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
        Assert.IsGreaterThan(TimeSpan.Zero, retryAfter);

        Assert.IsNotNull(frameworkOptions.OnRejected);
        await frameworkOptions.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = rejected },
            CancellationToken.None);

        Assert.IsTrue(context.Response.Headers.ContainsKey("Retry-After"));
    }

    [TestMethod]
    public void OptionalGlobalConcurrencyLimiterCapsDistinctClientsTogether()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.BurstSize = 100;
            options.RequestsPerSecond = 100;
            options.QueueLimit = 0;
            options.GlobalConcurrencyLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);
        var first = CreateContext("192.0.2.31");
        var second = CreateContext("192.0.2.32");

        RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease blockedByGlobalLimit = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(blockedByGlobalLimit.IsAcquired);

        firstLease.Dispose();

        using RateLimitLease afterRelease = limiter.AttemptAcquire(second, permitCount: 1);
        Assert.IsTrue(afterRelease.IsAcquired);
    }

    [TestMethod]
    public void InvalidQueueLimitFailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(options => options.QueueLimit = -1);

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibFloodProtectionOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(Action<WebLibFloodProtectionOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFloodProtection(configure);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static PartitionedRateLimiter<HttpContext> GetLimiter(ServiceProvider provider)
    {
        RateLimiterOptions options = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        return options.GlobalLimiter ?? throw new AssertFailedException("Expected WebLib flood protection to configure a global limiter.");
    }

    private static DefaultHttpContext CreateContext(string ipAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        return context;
    }

    private static void ConfigureSingleTokenNoQueue(WebLibFloodProtectionOptions options)
    {
        options.BurstSize = 1;
        options.RequestsPerSecond = 1;
        options.QueueLimit = 0;
    }
}
