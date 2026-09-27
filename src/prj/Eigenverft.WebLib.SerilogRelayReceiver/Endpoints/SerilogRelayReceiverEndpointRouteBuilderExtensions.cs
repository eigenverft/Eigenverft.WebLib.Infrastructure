using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Maps SerilogRelay receiver endpoints.
    /// </summary>
    public static class SerilogRelayReceiverEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps one POST endpoint that authenticates, validates, and forwards SerilogRelay batches
        /// to the selected handler.
        /// </summary>
        public static IEndpointConventionBuilder MapSerilogRelayReceiver<THandler>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Action<SerilogRelayReceiverOptions>? configure = null)
            where THandler : class, ISerilogRelayBatchHandler
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

            var options = new SerilogRelayReceiverOptions();
            configure?.Invoke(options);

            if (options.MaximumBatchEvents <= 0)
            {
                throw new InvalidOperationException(
                    "SerilogRelayReceiverOptions.MaximumBatchEvents must be greater than zero.");
            }

            return endpoints.MapPost(
                pattern,
                async context =>
                {
                    if (!BearerTokenAuthentication.IsAuthorized(
                            options.BearerToken,
                            context.Request.Headers.Authorization.ToString()))
                    {
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    if (!context.Request.HasJsonContentType())
                    {
                        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                        return;
                    }

                    SerilogRelayBatch? batch;
                    try
                    {
                        batch = await context.Request
                            .ReadFromJsonAsync<SerilogRelayBatch>(
                                cancellationToken: context.RequestAborted)
                            .ConfigureAwait(false);
                    }
                    catch (JsonException)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (batch is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    string? validationError = SerilogRelayReceiverValidator.Validate(
                        batch,
                        options.MaximumBatchEvents);
                    if (validationError is not null)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync(
                            validationError,
                            context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    THandler handler = context.RequestServices.GetRequiredService<THandler>();
                    await handler.HandleAsync(
                        batch,
                        context.RequestAborted).ConfigureAwait(false);

                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                });
        }
    }
}
