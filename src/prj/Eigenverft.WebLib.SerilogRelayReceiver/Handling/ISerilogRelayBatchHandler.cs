using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Handles one validated SerilogRelay batch.
    /// </summary>
    public interface ISerilogRelayBatchHandler
    {
        /// <summary>
        /// Processes a batch after endpoint authentication and protocol validation have succeeded.
        /// </summary>
        /// <param name="batch">The validated batch. Events may originate from different applications.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>A task that completes when the handler has accepted the batch.</returns>
        ValueTask HandleAsync(
            SerilogRelayBatch batch,
            CancellationToken cancellationToken);
    }
}
