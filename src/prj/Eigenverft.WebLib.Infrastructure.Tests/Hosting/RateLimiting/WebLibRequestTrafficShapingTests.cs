using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class WebLibRequestTrafficShapingTests
{
    [TestMethod]
    public void DefaultsAreFiniteConfigurableStartingValues()
    {
        var options = new WebLibRequestTrafficShapingOptions();

        Assert.IsNotNull(options.PerClient);
        Assert.IsTrue(options.PerClient.Enabled);
        Assert.AreEqual(40, options.PerClient.BurstSize);
        Assert.AreEqual(10, options.PerClient.RequestsPerSecond);
        Assert.AreEqual(20, options.PerClient.QueueLimit);
        Assert.AreEqual(MissingClientIpBehavior.SharedPartition, options.PerClient.MissingClientIpBehavior);
        Assert.IsNotNull(options.ServerWide);
        Assert.IsFalse(options.ServerWide.Enabled);
        Assert.AreEqual(0, options.ServerWide.BurstSize);
        Assert.AreEqual(0, options.ServerWide.RequestsPerSecond);
        Assert.AreEqual(0, options.ServerWide.QueueLimit);
        Assert.IsNull(options.GlobalConcurrencyLimit);
    }

    [TestMethod]
    public void ParameterlessRegistrationUsesClassDefaultsWithoutManualOptionsBinding()
    {
        using ServiceProvider provider = BuildProvider();
        WebLibRequestTrafficShapingOptions options = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.IsNotNull(options.PerClient);
        Assert.IsTrue(options.PerClient.Enabled);
        Assert.AreEqual(40, options.PerClient.BurstSize);
        Assert.AreEqual(10, options.PerClient.RequestsPerSecond);
        Assert.AreEqual(20, options.PerClient.QueueLimit);
        Assert.AreEqual(MissingClientIpBehavior.SharedPartition, options.PerClient.MissingClientIpBehavior);
        Assert.IsNotNull(options.ServerWide);
        Assert.IsFalse(options.ServerWide.Enabled);
        Assert.AreEqual(0, options.ServerWide.BurstSize);
        Assert.AreEqual(0, options.ServerWide.RequestsPerSecond);
        Assert.AreEqual(0, options.ServerWide.QueueLimit);
        Assert.IsNull(options.GlobalConcurrencyLimit);
    }

    [TestMethod]
    public void ConfigurationOverridesConfiguredValues()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:PerClient:BurstSize"] = "60",
            ["RequestTrafficShaping:PerClient:QueueLimit"] = "50",
        });

        using ServiceProvider provider = BuildProvider(configuration);
        WebLibRequestTrafficShapingOptions options = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.AreEqual(60, options.PerClient.BurstSize);
        Assert.AreEqual(10, options.PerClient.RequestsPerSecond);
        Assert.AreEqual(50, options.PerClient.QueueLimit);
    }

    [TestMethod]
    public void MissingConfigurationValuesLeaveClassDefaultsIntact()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:PerClient:BurstSize"] = "60",
        });

        using ServiceProvider provider = BuildProvider(configuration);
        WebLibRequestTrafficShapingOptions options = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.AreEqual(60, options.PerClient.BurstSize);
        Assert.AreEqual(10, options.PerClient.RequestsPerSecond);
        Assert.AreEqual(20, options.PerClient.QueueLimit);
        Assert.AreEqual(MissingClientIpBehavior.SharedPartition, options.PerClient.MissingClientIpBehavior);
        Assert.IsNotNull(options.ServerWide);
        Assert.IsFalse(options.ServerWide.Enabled);
        Assert.AreEqual(0, options.ServerWide.BurstSize);
        Assert.AreEqual(0, options.ServerWide.RequestsPerSecond);
        Assert.AreEqual(0, options.ServerWide.QueueLimit);
        Assert.IsNull(options.GlobalConcurrencyLimit);
    }

    [TestMethod]
    public void LambdaConfigurationOverridesJsonConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:PerClient:BurstSize"] = "60",
            ["RequestTrafficShaping:PerClient:QueueLimit"] = "50",
        });

        using ServiceProvider provider = BuildProvider(configuration, options =>
        {
            options.PerClient.BurstSize = 70;
            options.PerClient.RequestsPerSecond = 25;
        });
        WebLibRequestTrafficShapingOptions options = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.AreEqual(70, options.PerClient.BurstSize);
        Assert.AreEqual(25, options.PerClient.RequestsPerSecond);
        Assert.AreEqual(50, options.PerClient.QueueLimit);
    }

    [TestMethod]
    public void ServerWideConfigurationBindsAndLambdaAppliesLast()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:ServerWide:Enabled"] = "true",
            ["RequestTrafficShaping:ServerWide:BurstSize"] = "4",
            ["RequestTrafficShaping:ServerWide:RequestsPerSecond"] = "2",
            ["RequestTrafficShaping:ServerWide:QueueLimit"] = "200",
        });

        using ServiceProvider provider = BuildProvider(configuration, options =>
        {
            options.ServerWide.BurstSize = 6;
            options.ServerWide.QueueLimit = 500;
        });
        WebLibRequestTrafficShapingOptions options = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.IsTrue(options.ServerWide.Enabled);
        Assert.AreEqual(6, options.ServerWide.BurstSize);
        Assert.AreEqual(2, options.ServerWide.RequestsPerSecond);
        Assert.AreEqual(500, options.ServerWide.QueueLimit);
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
            options.PerClient.MissingClientIpBehavior = MissingClientIpBehavior.BypassPerIpLimit;
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
            options.PerClient.MissingClientIpBehavior = MissingClientIpBehavior.BypassPerIpLimit;
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
    public void MissingClientIpBypassStillUsesServerWideTokenBucket()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            ConfigureSingleTokenNoQueue(options);
            options.PerClient.MissingClientIpBehavior = MissingClientIpBehavior.BypassPerIpLimit;
            ConfigureServerWideTokenBucket(options, burstSize: 1, requestsPerSecond: 1, queueLimit: 0);
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();

        using RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(secondLease.IsAcquired);
        Assert.IsTrue(secondLease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    [TestMethod]
    public void DisabledPerClientStillUsesServerWideTokenBucket()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.Enabled = false;
            ConfigureServerWideTokenBucket(options, burstSize: 1, requestsPerSecond: 1, queueLimit: 0);
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        using RateLimitLease firstLease = limiter.AttemptAcquire(CreateContext("192.0.2.61"), permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(CreateContext("192.0.2.62"), permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(secondLease.IsAcquired);
        Assert.IsTrue(secondLease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    [TestMethod]
    public void DisabledPerClientStillUsesGlobalConcurrencyLimiter()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.Enabled = false;
            options.GlobalConcurrencyLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        RateLimitLease firstLease = limiter.AttemptAcquire(CreateContext("192.0.2.63"), permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(CreateContext("192.0.2.64"), permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(secondLease.IsAcquired);

        firstLease.Dispose();
        using RateLimitLease afterRelease = limiter.AttemptAcquire(CreateContext("192.0.2.64"), permitCount: 1);
        Assert.IsTrue(afterRelease.IsAcquired);
    }
    [TestMethod]
    public async Task QueueLimitBoundsQueuedWorkForOneClient()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 1;
            options.PerClient.RequestsPerSecond = 1;
            options.PerClient.QueueLimit = 1;
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
            options.PerClient.BurstSize = 1;
            options.PerClient.RequestsPerSecond = 1;
            options.PerClient.QueueLimit = 0;
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
    public void ServerWideTokenBucketCapsDistinctClientIpsTogether()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
            ConfigureServerWideTokenBucket(options, burstSize: 1, requestsPerSecond: 1, queueLimit: 0);
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var first = CreateContext("192.0.2.41");
        var second = CreateContext("192.0.2.42");

        using RateLimitLease firstLease = limiter.AttemptAcquire(first, permitCount: 1);
        using RateLimitLease secondLease = limiter.AttemptAcquire(second, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(secondLease.IsAcquired);
        Assert.IsTrue(secondLease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    [TestMethod]
    public void PerIpLimiterRunsBeforeServerWideLimiter()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            ConfigureSingleTokenNoQueue(options);
            ConfigureServerWideTokenBucket(options, burstSize: 2, requestsPerSecond: 1, queueLimit: 0);
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        var firstClient = CreateContext("198.51.100.41");
        var secondClient = CreateContext("198.51.100.42");

        using RateLimitLease firstLease = limiter.AttemptAcquire(firstClient, permitCount: 1);
        using RateLimitLease repeatedFirstClientLease = limiter.AttemptAcquire(firstClient, permitCount: 1);
        using RateLimitLease secondClientLease = limiter.AttemptAcquire(secondClient, permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(repeatedFirstClientLease.IsAcquired);
        Assert.IsTrue(secondClientLease.IsAcquired,
            "A per-IP rejection must stop the native chain before the shared server-wide token is consumed.");
    }

    [TestMethod]
    public async Task ServerWideQueueIsBoundedAfterPerIpLimiterAllowsRequest()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
            ConfigureServerWideTokenBucket(options, burstSize: 1, requestsPerSecond: 1, queueLimit: 1);
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        using RateLimitLease active = limiter.AttemptAcquire(CreateContext("203.0.113.41"), permitCount: 1);
        Assert.IsTrue(active.IsAcquired);

        using var cancellation = new CancellationTokenSource();
        Task<RateLimitLease> queued = limiter.AcquireAsync(
            CreateContext("203.0.113.42"),
            permitCount: 1,
            cancellation.Token).AsTask();

        Assert.IsFalse(queued.IsCompleted);
        RateLimiterStatistics? statistics = limiter.GetStatistics(CreateContext("203.0.113.42"));
        Assert.IsNotNull(statistics);
        Assert.AreEqual(1L, statistics.CurrentQueuedCount);

        ValueTask<RateLimitLease> overflowAcquire = limiter.AcquireAsync(
            CreateContext("203.0.113.43"),
            permitCount: 1,
            CancellationToken.None);
        Assert.IsTrue(overflowAcquire.IsCompletedSuccessfully,
            "A full server-wide queue should reject rather than enqueue more work.");
        using RateLimitLease overflow = overflowAcquire.Result;
        Assert.IsFalse(overflow.IsAcquired);

        cancellation.Cancel();
        try
        {
            await queued;
            Assert.Fail("Expected the globally queued acquire to observe cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected: TaskCanceledException is also a valid cancellation result.
        }
    }

    [TestMethod]
    public void NativeTokenBucketConfigurationUsesOldestFirstArrivalOrdering()
    {
        TokenBucketRateLimiterOptions options =
            WebLibRequestTrafficShapingRateLimiterOptionsSetup.CreateTokenBucketOptions(
                burstSize: 5,
                requestsPerSecond: 2,
                queueLimit: 500);

        Assert.AreEqual(QueueProcessingOrder.OldestFirst, options.QueueProcessingOrder);
        Assert.AreEqual(500, options.QueueLimit);
        Assert.IsTrue(options.AutoReplenishment);
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.ReplenishmentPeriod);

        // The server-wide limiter uses one shared partition. OldestFirst therefore provides FIFO arrival ordering only;
        // it does not introduce per-client fairness inside that global queue.
    }

    [TestMethod]
    public async Task ServerWideTokenBucketRejectionPreservesFrameworkRetryAfterMetadata()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
            ConfigureServerWideTokenBucket(options, burstSize: 1, requestsPerSecond: 1, queueLimit: 0);
        });
        RateLimiterOptions frameworkOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);
        var acceptedContext = CreateContext("203.0.113.44");
        var rejectedContext = CreateContext("203.0.113.45");

        using RateLimitLease accepted = limiter.AttemptAcquire(acceptedContext, permitCount: 1);
        using RateLimitLease rejected = limiter.AttemptAcquire(rejectedContext, permitCount: 1);

        Assert.IsTrue(accepted.IsAcquired);
        Assert.IsFalse(rejected.IsAcquired);
        Assert.IsTrue(rejected.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
        Assert.IsGreaterThan(TimeSpan.Zero, retryAfter);

        Assert.IsNotNull(frameworkOptions.OnRejected);
        await frameworkOptions.OnRejected(
            new OnRejectedContext { HttpContext = rejectedContext, Lease = rejected },
            CancellationToken.None);

        Assert.IsTrue(rejectedContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [TestMethod]
    public void OptionalGlobalConcurrencyLimiterCapsDistinctClientsTogether()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
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
    public void GlobalConcurrencyLimitRemainsIndependentWithServerWideTokenBucketEnabled()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
            ConfigureServerWideTokenBucket(options, burstSize: 3, requestsPerSecond: 1, queueLimit: 0);
            options.GlobalConcurrencyLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        RateLimitLease firstLease = limiter.AttemptAcquire(CreateContext("192.0.2.51"), permitCount: 1);
        using RateLimitLease blockedByConcurrency = limiter.AttemptAcquire(CreateContext("192.0.2.52"), permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(blockedByConcurrency.IsAcquired);
        Assert.IsFalse(blockedByConcurrency.TryGetMetadata(MetadataName.RetryAfter, out _),
            "The optional concurrency limiter remains a distinct non-rate-based rejection dimension.");

        firstLease.Dispose();

        using RateLimitLease afterRelease = limiter.AttemptAcquire(CreateContext("192.0.2.53"), permitCount: 1);
        Assert.IsTrue(afterRelease.IsAcquired);
    }

    [TestMethod]
    public void NativeChainDoesNotRollbackServerWideTokenWhenLaterConcurrencyLimiterRejects()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.BurstSize = 100;
            options.PerClient.RequestsPerSecond = 100;
            options.PerClient.QueueLimit = 0;
            ConfigureServerWideTokenBucket(options, burstSize: 2, requestsPerSecond: 1, queueLimit: 0);
            options.GlobalConcurrencyLimit = 1;
        });
        PartitionedRateLimiter<HttpContext> limiter = GetLimiter(provider);

        RateLimitLease firstLease = limiter.AttemptAcquire(CreateContext("198.51.100.51"), permitCount: 1);
        using RateLimitLease rejectedByConcurrency = limiter.AttemptAcquire(CreateContext("198.51.100.52"), permitCount: 1);

        Assert.IsTrue(firstLease.IsAcquired);
        Assert.IsFalse(rejectedByConcurrency.IsAcquired);

        firstLease.Dispose();

        using RateLimitLease thirdLease = limiter.AttemptAcquire(CreateContext("198.51.100.53"), permitCount: 1);
        Assert.IsFalse(thirdLease.IsAcquired,
            "The native chain disposes prior leases on failure, but token-bucket leases do not refund consumed tokens.");
        Assert.IsTrue(thirdLease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    [TestMethod]
    public void EnabledServerWideLimiterRequiresExplicitPositiveBurstAndRate()
    {
        using ServiceProvider provider = BuildProvider(options => options.ServerWide.Enabled = true);

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value);
    }

    [TestMethod]
    public void InvalidEnabledServerWideQueueLimitFailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.ServerWide.Enabled = true;
            options.ServerWide.BurstSize = 1;
            options.ServerWide.RequestsPerSecond = 1;
            options.ServerWide.QueueLimit = -1;
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value);
    }

    [TestMethod]
    public void DisabledServerWideDoesNotValidateUnusedRateValues()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.ServerWide.Enabled = false;
            options.ServerWide.BurstSize = -1;
            options.ServerWide.RequestsPerSecond = -1;
            options.ServerWide.QueueLimit = -1;
        });

        WebLibRequestTrafficShapingOptions options =
            provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.IsFalse(options.ServerWide.Enabled);
    }

    [TestMethod]
    public void InvalidQueueLimitFailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(options => options.PerClient.QueueLimit = -1);

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value);
    }

    [TestMethod]
    public void EnabledPerClientRequiresPositiveBurstAndRate()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.Enabled = true;
            options.PerClient.BurstSize = 0;
            options.PerClient.RequestsPerSecond = 0;
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value);
    }

    [TestMethod]
    public void DisabledPerClientDoesNotValidateUnusedRateValues()
    {
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.PerClient.Enabled = false;
            options.PerClient.BurstSize = -1;
            options.PerClient.RequestsPerSecond = -1;
            options.PerClient.QueueLimit = -1;
            options.PerClient.MissingClientIpBehavior = (MissingClientIpBehavior)12345;
        });

        WebLibRequestTrafficShapingOptions options =
            provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value;

        Assert.IsFalse(options.PerClient.Enabled);
    }
    [TestMethod]
    public void InvalidConfigurationStillFailsOptionsValidation()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:PerClient:QueueLimit"] = "-1",
        });

        using ServiceProvider provider = BuildProvider(configuration);

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WebLibRequestTrafficShapingOptions>>().Value);
    }

    [TestMethod]
    public void ValidateOnStartRemainsActive()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficShaping:PerClient:QueueLimit"] = "-1",
        });

        using ServiceProvider provider = BuildProvider(configuration);
        IStartupValidator startupValidator = provider.GetRequiredService<IStartupValidator>();

        Assert.ThrowsExactly<OptionsValidationException>(() => startupValidator.Validate());
    }

    private static ServiceProvider BuildProvider(Action<WebLibRequestTrafficShapingOptions>? configure = null)
    {
        return BuildProvider(new ConfigurationManager(), configure);
    }

    private static ServiceProvider BuildProvider(
        ConfigurationManager configuration,
        Action<WebLibRequestTrafficShapingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        if (configure is null)
        {
            services.AddRequestTrafficShaping();
        }
        else
        {
            services.AddRequestTrafficShaping(configure);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static PartitionedRateLimiter<HttpContext> GetLimiter(ServiceProvider provider)
    {
        RateLimiterOptions options = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        return options.GlobalLimiter ?? throw new AssertFailedException("Expected WebLib request traffic shaping to configure a global limiter.");
    }

    private static DefaultHttpContext CreateContext(string ipAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        return context;
    }

    private static void ConfigureServerWideTokenBucket(
        WebLibRequestTrafficShapingOptions options,
        int burstSize,
        int requestsPerSecond,
        int queueLimit)
    {
        options.ServerWide.Enabled = true;
        options.ServerWide.BurstSize = burstSize;
        options.ServerWide.RequestsPerSecond = requestsPerSecond;
        options.ServerWide.QueueLimit = queueLimit;
    }

    private static void ConfigureSingleTokenNoQueue(WebLibRequestTrafficShapingOptions options)
    {
        options.PerClient.BurstSize = 1;
        options.PerClient.RequestsPerSecond = 1;
        options.PerClient.QueueLimit = 0;
    }
}
