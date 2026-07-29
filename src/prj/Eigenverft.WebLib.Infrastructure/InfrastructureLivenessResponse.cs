namespace Eigenverft.WebLib.Infrastructure;

/// <summary>
/// Describes the process-level liveness state of an infrastructure component.
/// </summary>
/// <param name="Status">The stable machine-readable liveness status.</param>
public sealed record InfrastructureLivenessResponse(string Status);
