using System;

namespace Eigenverft.NetLib.Infrastructure;

/// <summary>
/// Provides operations over the fixed blue-green slot pair.
/// </summary>
public static class DeploymentSlotExtensions
{
    /// <summary>
    /// Returns the slot that is inactive when <paramref name="activeSlot"/> is active.
    /// </summary>
    /// <param name="activeSlot">The currently active slot.</param>
    /// <returns>The other member of the blue-green slot pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="activeSlot"/> is not a defined <see cref="DeploymentSlot"/> value.
    /// </exception>
    public static DeploymentSlot GetInactiveSlot(this DeploymentSlot activeSlot)
    {
        return activeSlot switch
        {
            DeploymentSlot.Blue => DeploymentSlot.Green,
            DeploymentSlot.Green => DeploymentSlot.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(activeSlot), activeSlot, "Unknown deployment slot."),
        };
    }
}
