using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup
{
    internal sealed class SelfHttpWarmupConnector
    {
        internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(1);

        private readonly TimeSpan _connectTimeout;
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveAddressesAsync;

        internal SelfHttpWarmupConnector(
            TimeSpan connectTimeout,
            Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddressesAsync = null)
        {
            _connectTimeout = connectTimeout > TimeSpan.Zero
                ? connectTimeout
                : DefaultConnectTimeout;
            _resolveAddressesAsync = resolveAddressesAsync ?? Dns.GetHostAddressesAsync;
        }

        internal async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            return await ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
        }

        internal async ValueTask<Stream> ConnectAsync(
            DnsEndPoint endPoint,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endPoint);

            IPAddress[] addresses;
            try
            {
                addresses = await _resolveAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                throw new HttpRequestException(
                    $"DNS resolution failed for warmup host '{endPoint.Host}'.",
                    ex);
            }

            IPAddress[] orderedAddresses = OrderAddresses(addresses);
            if (orderedAddresses.Length == 0)
            {
                throw new HttpRequestException(
                    $"DNS resolution returned no usable addresses for warmup host '{endPoint.Host}'.");
            }

            Exception? lastError = null;

            foreach (IPAddress address in orderedAddresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(_connectTimeout);

                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(address, endPoint.Port),
                        attemptCancellation.Token).ConfigureAwait(false);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    socket.Dispose();
                    lastError = new TimeoutException(
                        $"Connection attempt to '{address}:{endPoint.Port}' timed out after {_connectTimeout}.",
                        ex);
                }
                catch (SocketException ex)
                {
                    socket.Dispose();
                    lastError = ex;
                }
            }

            throw new HttpRequestException(
                $"Failed to connect to warmup host '{endPoint.Host}:{endPoint.Port}' via any resolved address.",
                lastError);
        }

        private static IPAddress[] OrderAddresses(IEnumerable<IPAddress>? addresses)
        {
            return (addresses ?? Array.Empty<IPAddress>())
                .Where(static address =>
                    address.AddressFamily == AddressFamily.InterNetwork ||
                    address.AddressFamily == AddressFamily.InterNetworkV6)
                .Distinct()
                .OrderBy(static address =>
                    address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();
        }
    }
}
