using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace canjewelry.src
{
    [ProtoContract]
    public class SyncCANJewelryPacket
    {
        [ProtoMember(1)]
        public string CompressedConfig;
    }
}
