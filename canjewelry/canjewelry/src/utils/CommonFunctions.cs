using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using Vintagestory.API.MathTools;

namespace canjewelry.src.utils
{
    public class CommonFunctions
    {
        public static bool tryFindColor(string inColorString, out int resColor)
        {
            Color clr = Color.FromName(inColorString);
            if (!clr.IsKnownColor)
            {
                resColor = Color.White.ToArgb();
                return false;
            }
            resColor = ColorUtil.ReverseColorBytes(clr.ToArgb());
            return true;
        }

        /// <summary>Gzipped UTF-8 of a payload sent over the network, null for an empty one.</summary>
        public static byte[] Gzip(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    gzip.Write(bytes, 0, bytes.Length);
                }
                return output.ToArray();
            }
        }

        /// <summary>The other half of <see cref="Gzip"/>; null when it cannot be read.</summary>
        public static string Gunzip(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;

            try
            {
                using (var input = new MemoryStream(payload))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
