using System;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Eigenverft.WebLib.Infrastructure.Hosting
{
    /// <summary>
    /// Provides small response helpers for middleware short-circuits.
    /// </summary>
    public static class HttpResponseExtensions
    {
        /// <summary>
        /// Writes a minimal HTML response containing the supplied status code and ASP.NET Core reason phrase.
        /// </summary>
        /// <remarks>
        /// This helper is intended for explicit middleware short-circuits. General application error handling should use
        /// ASP.NET Core status-code pages or Problem Details instead.
        /// </remarks>
        /// <param name="response">The response to write.</param>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="cancellationToken">A token that can cancel the response write.</param>
        /// <returns>A task representing the response write.</returns>
        public static Task WriteHtmlStatusResponseAsync(
            this HttpResponse response,
            int statusCode,
            CancellationToken cancellationToken = default)
        {
            if (response is null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            string reasonPhrase = ReasonPhrases.GetReasonPhrase(statusCode);
            string encodedReasonPhrase = HtmlEncoder.Default.Encode(reasonPhrase);

            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=utf-8";

            string separator = encodedReasonPhrase.Length == 0 ? string.Empty : " - ";
            string html = $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{statusCode}{separator}{encodedReasonPhrase}</title></head><body><p>{statusCode}{separator}{encodedReasonPhrase}</p></body></html>";

            return response.WriteAsync(html, cancellationToken);
        }
    }
}
