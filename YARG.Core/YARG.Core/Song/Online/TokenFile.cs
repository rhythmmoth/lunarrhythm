using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YARG.Core.Song
{
    /// <summary>
    /// Reads and writes the leaderboard sign-in file. On Windows its contents are encrypted with DPAPI for the current
    /// Windows user, so a copy of the file is useless on another PC or another account.
    /// </summary>
    internal static class TokenFile
    {
        private const string HEADER = "DPAPI1:";
        // Ties the encryption to this game, so other programs can't unprotect it with a plain call
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LunarRhythm leaderboard sign-in");

        public static bool Encrypted => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>The saved text (token, then name, one per line), or null if missing or unreadable.</summary>
        public static string? Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string text = File.ReadAllText(path);
            if (!text.StartsWith(HEADER, StringComparison.Ordinal))
            {
                return text; // saved before encryption; the caller saves it again, encrypted
            }
            try
            {
                return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(text.Substring(HEADER.Length))));
            }
            catch (Exception)
            {
                return null; // made by another Windows account or PC: sign in again
            }
        }

        public static bool IsPlainText(string path) =>
            File.Exists(path) && !File.ReadAllText(path).StartsWith(HEADER, StringComparison.Ordinal);

        public static void Write(string path, string text)
        {
            // shortcut: stays plain text off Windows (no DPAPI there); use the OS keychain if Linux/macOS builds ship
            string content = Encrypted
                ? HEADER + Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(text)))
                : text;
            File.WriteAllText(path, content);
        }

        // ---------- DPAPI (crypt32.dll), called directly so there's no extra library ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Protect(byte[] data) => Call(data, protect: true);

        private static byte[] Unprotect(byte[] data) => Call(data, protect: false);

        private static byte[] Call(byte[] data, bool protect)
        {
            var input = Pin(data, out var inputHandle);
            var entropy = Pin(Entropy, out var entropyHandle);
            var output = new DataBlob();
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref input, "LunarRhythm", ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
                if (!ok)
                {
                    throw new InvalidOperationException($"DPAPI failed ({Marshal.GetLastWin32Error()})");
                }
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                inputHandle.Free();
                entropyHandle.Free();
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }

        private static DataBlob Pin(byte[] data, out GCHandle handle)
        {
            handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            return new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
        }
    }
}
