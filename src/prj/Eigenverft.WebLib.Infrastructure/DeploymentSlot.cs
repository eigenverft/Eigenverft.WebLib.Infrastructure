namespace Eigenverft.WebLib.Infrastructure;

/// <summary>
/// Identifies one of the two independently deployable service slots.
/// </summary>
public enum DeploymentSlot
{
    /// <summary>
    /// Identifies the blue deployment slot.
    /// </summary>
    Blue,

    /// <summary>
    /// Identifies the green deployment slot.
    /// </summary>
    Green,
}
