using canjewelry.src.utils;
using ProtoBuf;

namespace canjewelry.src
{
    [ProtoContract]
    public class SyncCANJewelryPacket
    {
        /// <summary>The config as gzipped JSON — the name was always a promise, now it is true.</summary>
        [ProtoMember(3)]
        public byte[] ConfigGz;

        public string CompressedConfig
        {
            get => CommonFunctions.Gunzip(ConfigGz);
            set => ConfigGz = CommonFunctions.Gzip(value);
        }

        /// <summary>
        /// Sent by the client when it asks for the config: the fingerprint of the server gem poses
        /// it already has, empty when it has none. The server answers with poses only when its own
        /// fingerprint differs, so a reconnect or a second request costs nothing.
        /// </summary>
        [ProtoMember(2)]
        public string GemVisualsHash;
    }
}
