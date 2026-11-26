using System;
using System.Text;

namespace YARG.Song
{
    /// <summary>
    /// The online API key, kept scrambled in Resources/OnlineApiKey.txt ("v1:" then base64) against a mask that lives
    /// here in the code, so neither the data files nor the code alone shows it. It only slows people down: the game has
    /// to send the key, so a determined person can still get it. The real protection is the server's checks.
    /// </summary>
    public static class OnlineKey
    {
        private const string PREFIX = "v1:";
        private static readonly byte[] Mask = { 0x0B, 0xF6, 0xD4, 0x03, 0x14, 0x0E, 0x13, 0xBE, 0x77, 0x83, 0x3F, 0xB2, 0x80, 0xCF, 0x14, 0x13, 0xB0, 0x47, 0xE0, 0x91, 0xF0, 0x70, 0x4F, 0x8A, 0xF3, 0xA9, 0xE6, 0x93, 0x18, 0xE1, 0xFE, 0x9E };

        /// <summary>The key from the resource's text: scrambled ("v1:...") or, from before scrambling, plain.</summary>
        public static string Read(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            stored = stored.Trim();
            if (!stored.StartsWith(PREFIX)) return stored;
            try
            {
                var bytes = Convert.FromBase64String(stored.Substring(PREFIX.Length));
                return Encoding.UTF8.GetString(Apply(bytes));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>The resource text for a key (the editor's "Scramble the API key" menu writes it).</summary>
        public static string Scramble(string key) => PREFIX + Convert.ToBase64String(Apply(Encoding.UTF8.GetBytes(key.Trim())));

        // XOR with the mask and the position: the same call scrambles and unscrambles
        private static byte[] Apply(byte[] data)
        {
            var output = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                output[i] = (byte) (data[i] ^ Mask[i % Mask.Length] ^ (i * 31 & 0xFF));
            }
            return output;
        }
    }
}
