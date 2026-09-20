using canjewelry.src.utils;
using ProtoBuf;

namespace canjewelry.src
{
    /// <summary>
    /// The gem poses an admin put into the server's own pose folder. Sent only when that folder
    /// holds something: poses are otherwise pure client content that ships with the mod, and a
    /// server that changed nothing has nothing to say about them.
    /// </summary>
    [ProtoContract]
    public class GemVisualsPacket
    {
        /// <summary>The rules as a gzipped JSON array, in the same shape as a gemvisuals file.</summary>
        [ProtoMember(3)]
        public byte[] RulesGz;

        /// <summary>The rules as JSON. Compressed on the way out, unpacked on the way in.</summary>
        public string Rules
        {
            get => CommonFunctions.Gunzip(RulesGz);
            set => RulesGz = CommonFunctions.Gzip(value);
        }

        /// <summary>
        /// Fingerprint of <see cref="Rules"/>. The client keeps it and sends it back with its next
        /// request, which is how the server knows not to send the same poses twice.
        /// </summary>
        [ProtoMember(2)]
        public string Hash;
    }
}
