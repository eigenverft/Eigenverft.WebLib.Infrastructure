using System;

using Eigenverft.WebLib.Infrastructure.Security.MachineBinding;

namespace Eigenverft.WebLib.Infrastructure.Tests.Security.MachineBinding;

[TestClass]
public sealed class PhysicalMachineBindingTests
{
    [TestMethod]
    public void CurrentMachineFingerprintIsStableWhenPlatformUuidIsAvailable()
    {
        bool uuidAvailable = PhysicalMachineBinding.TryGetSystemPlatformUuid(out string platformUuid);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsTrue(uuidAvailable, "The current Windows test host should expose an SMBIOS system UUID.");
        }
        else if (!uuidAvailable)
        {
            // Some Linux containers/VMs deliberately do not expose DMI platform identity. The helper's documented
            // contract is to fail softly through Try* rather than invent a semantically different fallback.
            Assert.IsFalse(PhysicalMachineBinding.TryGetFingerprint(out _));
            return;
        }

        Assert.IsTrue(Guid.TryParse(platformUuid, out Guid parsedUuid));
        Assert.AreNotEqual(Guid.Empty, parsedUuid);

        Assert.IsTrue(PhysicalMachineBinding.TryGetFingerprint(out string first));
        Assert.IsTrue(PhysicalMachineBinding.TryGetFingerprint(out string second));
        Assert.AreEqual(64, first.Length);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first, PhysicalMachineBinding.GetFingerprint());
    }
}
