using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Eigenverft.WebLib.Infrastructure.Security.Protection
{
    /// <summary>
    /// Provides the minimal Windows DPAPI LocalMachine primitive needed by the settings codecs.
    /// </summary>
    /// <remarks>
    /// This helper wraps the Windows operating-system API directly so the library does not need the
    /// System.Security.Cryptography.ProtectedData NuGet package. LocalMachine scope binds protected bytes to the Windows
    /// machine, not to an administrator or individual user; Windows permits another user on the same machine to unprotect a
    /// LocalMachine payload. This helper therefore adds a machine-context requirement, not a privilege boundary. It is not a
    /// cross-platform abstraction: callers that require DPAPI explicitly opt into a Windows-only layer.
    /// </remarks>
    internal static class DpapiMachineProtection
    {
        private const string NotAvailableMessage =
            "Windows DPAPI machine-scope protection is available only on Windows.";
        private const int CryptprotectUiForbidden = 0x1;
        private const int CryptprotectLocalMachine = 0x4;

        public static byte[] Protect(byte[] clearBytes)
        {
            ArgumentNullException.ThrowIfNull(clearBytes);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(NotAvailableMessage);
            }

            DataBlob input = CreateInputBlob(clearBytes);
            DataBlob output = default;

            try
            {
                if (!CryptProtectData(
                        ref input,
                        null,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptprotectUiForbidden | CryptprotectLocalMachine,
                        out output))
                {
                    throw new CryptographicException(Marshal.GetLastWin32Error());
                }

                return CopyOutputBlob(output);
            }
            finally
            {
                FreeInputBlob(input);
                FreeOutputBlob(output);
            }
        }

        public static bool TryUnprotect(byte[] protectedBytes, out byte[] clearBytes)
        {
            ArgumentNullException.ThrowIfNull(protectedBytes);

            clearBytes = Array.Empty<byte>();

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            DataBlob input = CreateInputBlob(protectedBytes);
            DataBlob output = default;
            IntPtr description = IntPtr.Zero;

            try
            {
                if (!CryptUnprotectData(
                        ref input,
                        out description,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptprotectUiForbidden,
                        out output))
                {
                    return false;
                }

                clearBytes = CopyOutputBlob(output);
                return true;
            }
            finally
            {
                FreeInputBlob(input);
                FreeOutputBlob(output);

                if (description != IntPtr.Zero)
                {
                    _ = LocalFree(description);
                }
            }
        }

        private static DataBlob CreateInputBlob(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return default;
            }

            IntPtr data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            return new DataBlob { Size = bytes.Length, Data = data };
        }

        private static byte[] CopyOutputBlob(DataBlob blob)
        {
            if (blob.Size == 0 || blob.Data == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            byte[] bytes = new byte[blob.Size];
            Marshal.Copy(blob.Data, bytes, 0, blob.Size);
            return bytes;
        }

        private static void FreeInputBlob(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob.Data);
            }
        }

        private static void FreeOutputBlob(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                _ = LocalFree(blob.Data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? dataDescription,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            out IntPtr dataDescription,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
