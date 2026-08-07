using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Eigenverft.WebLib.Infrastructure.Security.MachineBinding
{
    /// <summary>
    /// Derives a stable, non-secret fingerprint from the current system/platform UUID for lightweight machine binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This helper is intentionally not a hardware security boundary and the returned fingerprint is not a secret. Its
    /// purpose is narrower: an attacker who copies an application's files to another machine should also need to obtain
    /// system information from the original machine before a value derived from this fingerprint can be reproduced.
    /// This adds an additional offline/lateral-movement step; it does not defend against an attacker who can already read
    /// the platform UUID on the source machine or inspect a process after the fingerprint has been derived.
    /// </para>
    /// <para>
    /// Version 1 deliberately uses one semantically comparable platform identifier on every supported operating system:
    /// the SMBIOS system UUID on Windows, the DMI product UUID on Linux, and IOPlatformUUID on macOS. Virtual machines
    /// generally expose a virtual platform UUID, so in that environment the binding is to the VM identity rather than to
    /// the physical host. Cloning or re-provisioning a VM may therefore preserve or change the binding depending on the
    /// hypervisor and provisioning process.
    /// </para>
    /// <para>
    /// The implementation intentionally has no additional management-library dependency. If broader machine inventory
    /// becomes useful later, Microsoft.Management.Infrastructure can be evaluated as an extension point; collecting a
    /// wider CIM/hardware inventory is explicitly outside the scope of this minimal V1 binding.
    /// </para>
    /// </remarks>
    public static class PhysicalMachineBinding
    {
        private const string FingerprintDomain = "Eigenverft.PhysicalMachineBinding.v1";
        private const uint RawSmbiosProvider = 0x52534D42; // Win32 multi-character constant 'RSMB'.
        private const string LinuxDmiProductUuidPath = "/sys/class/dmi/id/product_uuid";
        private const string LinuxVirtualDmiProductUuidPath = "/sys/devices/virtual/dmi/id/product_uuid";
        private const string MacIoKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string MacCoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint MacUtf8Encoding = 0x08000100;

        /// <summary>
        /// Gets the V1 machine fingerprint for the current system.
        /// </summary>
        /// <returns>
        /// An uppercase SHA-256 hexadecimal string derived from a versioned canonical representation of the platform UUID.
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">The current operating system is not supported.</exception>
        /// <exception cref="InvalidOperationException">
        /// The current operating system is supported, but no valid system/platform UUID is available.
        /// </exception>
        /// <remarks>
        /// Hashing normalizes the representation and avoids propagating the raw platform UUID through consuming APIs; it
        /// does not make the underlying UUID secret. The version/domain prefix is part of the fingerprint contract so a
        /// future binding strategy can coexist with V1 instead of silently changing previously derived AES material.
        /// </remarks>
        public static string GetFingerprint()
        {
            if (TryGetFingerprint(out string fingerprint))
            {
                return fingerprint;
            }

            if (!IsSupportedOperatingSystem())
            {
                throw new PlatformNotSupportedException(
                    "Physical machine binding is supported only on Windows, Linux, and macOS.");
            }

            throw new InvalidOperationException(
                "The current system does not expose a valid system/platform UUID required for physical machine binding.");
        }

        /// <summary>
        /// Attempts to derive the V1 machine fingerprint for the current system.
        /// </summary>
        /// <param name="fingerprint">Receives the uppercase SHA-256 hexadecimal fingerprint on success.</param>
        /// <returns><see langword="true"/> when a valid platform UUID was available; otherwise <see langword="false"/>.</returns>
        public static bool TryGetFingerprint(out string fingerprint)
        {
            fingerprint = string.Empty;

            if (!TryGetSystemPlatformUuid(out string platformUuid))
            {
                return false;
            }

            string canonical = string.Concat(
                FingerprintDomain,
                "\nsystem-platform-uuid=",
                platformUuid);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            fingerprint = Convert.ToHexString(hash);
            return true;
        }

        /// <summary>
        /// Attempts to read the normalized system/platform UUID used as the V1 binding source.
        /// </summary>
        /// <param name="platformUuid">Receives the canonical uppercase UUID in <c>D</c> format on success.</param>
        /// <returns><see langword="true"/> when a valid UUID is available; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// The returned UUID is machine information, not secret key material. Callers that only need a binding value should
        /// prefer <see cref="GetFingerprint"/> or <see cref="TryGetFingerprint"/> so the V1 normalization contract remains
        /// centralized here.
        /// </remarks>
        public static bool TryGetSystemPlatformUuid(out string platformUuid)
        {
            platformUuid = string.Empty;

            if (OperatingSystem.IsWindows())
            {
                return TryReadWindowsSmbiosUuid(out platformUuid);
            }

            if (OperatingSystem.IsLinux())
            {
                return TryReadLinuxDmiUuid(out platformUuid);
            }

            if (OperatingSystem.IsMacOS())
            {
                return TryReadMacPlatformUuid(out platformUuid);
            }

            return false;
        }

        private static bool IsSupportedOperatingSystem()
        {
            return OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        }

        private static bool TryReadWindowsSmbiosUuid(out string platformUuid)
        {
            platformUuid = string.Empty;
            uint requiredSize = GetSystemFirmwareTable(RawSmbiosProvider, 0, IntPtr.Zero, 0);

            if (requiredSize < 8 || requiredSize > int.MaxValue)
            {
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                uint written = GetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, requiredSize);
                if (written < 8 || written > requiredSize || written > int.MaxValue)
                {
                    return false;
                }

                byte[] raw = new byte[(int)written];
                Marshal.Copy(buffer, raw, 0, raw.Length);

                int tableLength = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4, 4));
                if (tableLength <= 0 || tableLength > raw.Length - 8)
                {
                    return false;
                }

                byte smbiosMajorVersion = raw[1];
                byte smbiosMinorVersion = raw[2];
                return TryFindSmbiosSystemUuid(
                    raw.AsSpan(8, tableLength),
                    smbiosMajorVersion,
                    smbiosMinorVersion,
                    out platformUuid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryFindSmbiosSystemUuid(
            ReadOnlySpan<byte> table,
            byte smbiosMajorVersion,
            byte smbiosMinorVersion,
            out string platformUuid)
        {
            platformUuid = string.Empty;
            int offset = 0;

            while (offset + 4 <= table.Length)
            {
                byte type = table[offset];
                int formattedLength = table[offset + 1];

                if (formattedLength < 4 || offset + formattedLength > table.Length)
                {
                    return false;
                }

                // SMBIOS Type 1 (System Information) stores its 16-byte UUID at structure offset 8.
                if (type == 1 && formattedLength >= 24)
                {
                    byte[] uuidBytes = table.Slice(offset + 8, 16).ToArray();
                    if (!IsUnspecifiedUuid(uuidBytes))
                    {
                        // SMBIOS 2.6 standardized little-endian encoding for the first three UUID fields. Guid(byte[])
                        // consumes that layout directly. Older SMBIOS versions used network byte order for all fields.
                        if (smbiosMajorVersion < 2 ||
                            (smbiosMajorVersion == 2 && smbiosMinorVersion < 6))
                        {
                            Reverse(uuidBytes, 0, 4);
                            Reverse(uuidBytes, 4, 2);
                            Reverse(uuidBytes, 6, 2);
                        }

                        Guid uuid = new(uuidBytes);
                        return TryNormalizeUuid(uuid.ToString("D"), out platformUuid);
                    }
                }

                int next = offset + formattedLength;
                while (next + 1 < table.Length && (table[next] != 0 || table[next + 1] != 0))
                {
                    next++;
                }

                if (next + 1 >= table.Length)
                {
                    return false;
                }

                offset = next + 2;
                if (type == 127)
                {
                    break;
                }
            }

            return false;
        }

        private static bool TryReadLinuxDmiUuid(out string platformUuid)
        {
            platformUuid = string.Empty;

            return TryReadUuidFile(LinuxDmiProductUuidPath, out platformUuid) ||
                TryReadUuidFile(LinuxVirtualDmiProductUuidPath, out platformUuid);
        }

        private static bool TryReadUuidFile(string path, out string platformUuid)
        {
            platformUuid = string.Empty;

            try
            {
                return File.Exists(path) &&
                    TryNormalizeUuid(File.ReadAllText(path).Trim(), out platformUuid);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryReadMacPlatformUuid(out string platformUuid)
        {
            platformUuid = string.Empty;
            IntPtr matching = IOServiceMatching("IOPlatformExpertDevice");
            if (matching == IntPtr.Zero)
            {
                return false;
            }

            uint service = IOServiceGetMatchingService(0, matching);
            if (service == 0)
            {
                return false;
            }

            IntPtr key = IntPtr.Zero;
            IntPtr property = IntPtr.Zero;

            try
            {
                key = CFStringCreateWithCString(IntPtr.Zero, "IOPlatformUUID", MacUtf8Encoding);
                if (key == IntPtr.Zero)
                {
                    return false;
                }

                property = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
                if (property == IntPtr.Zero)
                {
                    return false;
                }

                var buffer = new StringBuilder(128);
                return CFStringGetCString(property, buffer, buffer.Capacity, MacUtf8Encoding) &&
                    TryNormalizeUuid(buffer.ToString(), out platformUuid);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            finally
            {
                if (property != IntPtr.Zero)
                {
                    CFRelease(property);
                }

                if (key != IntPtr.Zero)
                {
                    CFRelease(key);
                }

                _ = IOObjectRelease(service);
            }
        }

        private static bool TryNormalizeUuid(string? value, out string normalized)
        {
            normalized = string.Empty;

            if (!Guid.TryParse(value, out Guid uuid) ||
                uuid == Guid.Empty ||
                uuid == new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"))
            {
                return false;
            }

            normalized = uuid.ToString("D").ToUpperInvariant();
            return true;
        }

        private static bool IsUnspecifiedUuid(byte[] bytes)
        {
            bool allZero = true;
            bool allOnes = true;

            foreach (byte value in bytes)
            {
                allZero &= value == 0;
                allOnes &= value == byte.MaxValue;
            }

            return allZero || allOnes;
        }

        private static void Reverse(byte[] bytes, int start, int length)
        {
            Array.Reverse(bytes, start, length);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            IntPtr firmwareTableBuffer,
            uint bufferSize);

        [DllImport(MacIoKitLibrary)]
        private static extern IntPtr IOServiceMatching([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(MacIoKitLibrary)]
        private static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

        [DllImport(MacIoKitLibrary)]
        private static extern IntPtr IORegistryEntryCreateCFProperty(
            uint entry,
            IntPtr key,
            IntPtr allocator,
            uint options);

        [DllImport(MacIoKitLibrary)]
        private static extern int IOObjectRelease(uint ioObject);

        [DllImport(MacCoreFoundationLibrary)]
        private static extern IntPtr CFStringCreateWithCString(
            IntPtr allocator,
            [MarshalAs(UnmanagedType.LPStr)] string value,
            uint encoding);

        [DllImport(MacCoreFoundationLibrary)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFStringGetCString(
            IntPtr theString,
            StringBuilder buffer,
            nint bufferSize,
            uint encoding);

        [DllImport(MacCoreFoundationLibrary)]
        private static extern void CFRelease(IntPtr cfObject);
    }
}
