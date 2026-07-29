using System;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class DeploymentSlotExtensionsTests
{
    [TestMethod]
    [DataRow(DeploymentSlot.Blue, DeploymentSlot.Green)]
    [DataRow(DeploymentSlot.Green, DeploymentSlot.Blue)]
    public void GetInactiveSlotReturnsOtherSlot(
        DeploymentSlot activeSlot,
        DeploymentSlot expectedInactiveSlot)
    {
        Assert.AreEqual(expectedInactiveSlot, activeSlot.GetInactiveSlot());
    }

    [TestMethod]
    public void GetInactiveSlotRejectsUnknownSlot()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ((DeploymentSlot)int.MaxValue).GetInactiveSlot());
    }
}
