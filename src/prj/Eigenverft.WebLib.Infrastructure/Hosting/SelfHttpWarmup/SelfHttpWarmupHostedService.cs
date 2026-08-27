using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup
{
    internal sealed class SelfHttpWarmupHostedService : BackgroundService
    {
        private const string UserAgent = "Eigenverft.WebLib.Infrastructure/SelfHttpWarmup";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger<SelfHttpWarmupHostedService> _logger;
        private readonly IOptions<SelfHttpWarmupOptions> _options;

        public SelfHttpWarmupHostedService(
            IHttpClientFactory httpClientFactory,
            IHostApplicationLifetime applicationLifetime,
            ILogger<SelfHttpWarmupHostedService> logger,
            IOptions<SelfHttpWarmupOptions> options)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await WaitForApplicationStartedAsync(
                    _applicationLifetime.ApplicationStarted,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            SelfHttpWarmupOptions options = _options.Value;
            if (!options.Enabled)
            {
                return;
            }

            if (options.InitialDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(options.InitialDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            string[] targets = options.TargetUrls ?? Array.Empty<string>();
            if (targets.Length == 0)
            {
                _logger.LogDebug("Self-HTTP warmup skipped because no target URLs are configured.");
                return;
            }

            TimeSpan requestTimeout = options.RequestTimeout > TimeSpan.Zero
                ? options.RequestTimeout
                : TimeSpan.FromSeconds(5);

            HttpClient client = _httpClientFactory.CreateClient(SelfHttpWarmupServiceCollectionExtensions.HttpClientName);

            foreach (string rawTarget in targets)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                string target = rawTarget?.Trim() ?? string.Empty;
                if (!TryParseTarget(target, out Uri? uri))
                {
                    _logger.LogWarning(
                        "Self-HTTP warmup target '{Target}' is not an absolute HTTP or HTTPS URL and was skipped.",
                        target);
                    continue;
                }

                long startedAt = Stopwatch.GetTimestamp();

                try
                {
                    using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCancellation.CancelAfter(requestTimeout);

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutCancellation.Token).ConfigureAwait(false);

                    _logger.LogDebug(
                        "Self-HTTP warmup completed for {Target} with status {StatusCode} in {ElapsedMilliseconds:F1} ms.",
                        uri,
                        (int)response.StatusCode,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "Self-HTTP warmup timed out for {Target} after {Timeout}.",
                        uri,
                        requestTimeout);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Self-HTTP warmup failed for {Target}.", uri);
                }
            }
        }

        private static async Task WaitForApplicationStartedAsync(
            CancellationToken applicationStarted,
            CancellationToken stoppingToken)
        {
            if (applicationStarted.IsCancellationRequested)
            {
                return;
            }

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = applicationStarted.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                started);

            await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        }

        private static bool TryParseTarget(string target, out Uri? uri)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }
    }
}
