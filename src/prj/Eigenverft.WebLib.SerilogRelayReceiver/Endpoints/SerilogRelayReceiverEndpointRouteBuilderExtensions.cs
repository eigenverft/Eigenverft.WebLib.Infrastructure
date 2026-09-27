using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

                    catch (InvalidOperationException)
                    {
                        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
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
                    IHostApplicationLifetime applicationLifetime = context.RequestServices
                        .GetRequiredService<IHostApplicationLifetime>();
                    try
                    {
                        await handler.HandleAsync(
                            batch,
                            applicationLifetime.ApplicationStopping).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (applicationLifetime.ApplicationStopping.IsCancellationRequested)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                });
        }
    }
}
