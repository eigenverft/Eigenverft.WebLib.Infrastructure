using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class SelfHttpWarmupTests
{
    [TestMethod]
    public async Task WarmupWaitsForApplicationStarted()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = true,
                TargetUrls = ["http://localhost/warmup"],
            });

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(0, handler.RequestCount);

        lifetime.SignalStarted();
        await WaitUntilAsync(() => handler.RequestCount == 1);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("http://localhost/warmup", handler.RequestUris.Single().AbsoluteUri);
    }

    [TestMethod]
    public async Task WarmupSendsAllConfiguredTargets()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        lifetime.SignalStarted();

        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = true,
                TargetUrls =
                [
                    "http://localhost/one",
                    "https://localhost/two",
                    "http://localhost/three",
                ],
            });

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[]
            {
                "http://localhost/one",
                "https://localhost/two",
                "http://localhost/three",
            },
            handler.RequestUris.Select(static uri => uri.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task ConnectorFallsBackAcrossMultipleResolvedAddresses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int resolveCount = 0;
        var connector = new SelfHttpWarmupConnector(
            TimeSpan.FromMilliseconds(250),
            (host, cancellationToken) =>
            {
                Assert.AreEqual("warmup.test", host);
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref resolveCount);
                return Task.FromResult(new[]
                {
                    IPAddress.Parse("127.0.0.2"),
                    IPAddress.Loopback,
                });
            });

        Task<Socket> acceptedTask = listener.AcceptSocketAsync();
        await using System.IO.Stream connectedStream = await connector.ConnectAsync(
            new DnsEndPoint("warmup.test", port),
            CancellationToken.None);
        using Socket acceptedSocket = await acceptedTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, resolveCount);
        Assert.IsTrue(connectedStream.CanWrite);
        Assert.IsTrue(acceptedSocket.Connected);
    }

    [TestMethod]
    public async Task RequestTimeoutCancelsInFlightWarmup()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        lifetime.SignalStarted();

        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = true,
                RequestTimeout = TimeSpan.FromMilliseconds(75),
                TargetUrls = ["http://localhost/slow"],
            });

        await service.StartAsync(CancellationToken.None);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.Cancelled.WaitAsync(TimeSpan.FromSeconds(2));
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task ShutdownCancelsInFlightWarmup()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        lifetime.SignalStarted();

        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = true,
                RequestTimeout = TimeSpan.FromSeconds(30),
                TargetUrls = ["http://localhost/blocking"],
            });

        await service.StartAsync(CancellationToken.None);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);
        await handler.Cancelled.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task ShutdownBeforeApplicationStartedCancelsWithoutSending()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = true,
                TargetUrls = ["http://localhost/never"],
            });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task DisabledWarmupSendsNothing()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var lifetime = new TestHostApplicationLifetime();
        lifetime.SignalStarted();

        using SelfHttpWarmupHostedService service = CreateService(
            client,
            lifetime,
            new SelfHttpWarmupOptions
            {
                Enabled = false,
                TargetUrls = ["http://localhost/disabled"],
            });

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public void FeatureIsNotPresentWithoutRegistrationAndDefaultsToDisabled()
    {
        var services = new ServiceCollection();

        Assert.IsFalse(services.Any(static descriptor => descriptor.ServiceType == typeof(IHostedService)));
        Assert.IsFalse(new SelfHttpWarmupOptions().Enabled);
    }

    private static SelfHttpWarmupHostedService CreateService(
        HttpClient client,
        IHostApplicationLifetime lifetime,
        SelfHttpWarmupOptions options)
    {
        return new SelfHttpWarmupHostedService(
            new FixedHttpClientFactory(client),
            lifetime,
            NullLogger<SelfHttpWarmupHostedService>.Instance,
            Options.Create(options));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        internal FixedHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Uri> _requestUris = new();

        internal int RequestCount => _requestUris.Count;

        internal Uri[] RequestUris => _requestUris.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requestUris.Enqueue(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        internal Task Cancelled => _cancelled.Task;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        internal Task Started => _started.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult(true);
                throw;
            }
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopped = new();
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        internal void SignalStarted()
        {
            _started.Cancel();
        }

        public void StopApplication()
        {
            _stopping.Cancel();
        }
    }
}
