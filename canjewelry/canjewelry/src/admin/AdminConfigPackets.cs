using ProtoBuf;
using System.Collections.Generic;

namespace canjewelry.src.admin
{
    /// <summary>What an <see cref="AdminConfigEditPacket"/> asks the server to change.</summary>
    public enum AdminConfigOp
    {
        None = 0,
        /// <summary>Code + Tiers: a flat rule in items_codes_with_socket_count_and_tiers.</summary>
        SetSocketRule = 1,
        /// <summary>Code: the flat rule is set to [] - the item keeps no sockets.</summary>
        DisableSocketRule = 2,
        /// <summary>Code + AttributeKey + VariantValue + Tiers: one value of a variant rule.</summary>
        SetVariantValue = 3,
        /// <summary>Code + VariantValue: that value is dropped from the variant rule.</summary>
        RemoveVariantValue = 4,
        /// <summary>Code: the whole variant rule is emptied - no value gives any sockets.</summary>
        DisableVariantRule = 5,
        /// <summary>GemType + Code (a concrete item) + Allowed.</summary>
        SetGemForItem = 6,
        /// <summary>Ids of entries from <see cref="AdminConfigSnapshotPacket.Missing"/>.</summary>
        AddMissing = 7,
        AddAllMissing = 8,
    }

    /// <summary>
    /// A default the config does not have: a socket rule, a value of a variant rule, or an item
    /// fragment that gems accept by default.
    /// </summary>
    [ProtoContract]
    public class AdminMissingEntry
    {
        /// <summary>"socket:code", "variant:code", "variantvalue:code|value" or "gem:fragment".</summary>
        [ProtoMember(1)] public string Id;
        [ProtoMember(2)] public string Kind;
        [ProtoMember(3)] public string Subject;
        /// <summary>The tiers to be added, or for a fragment the gems that lack it.</summary>
        [ProtoMember(4)] public string Detail;
    }

    /// <summary>
    /// Server to client: opens the admin dialogue, or refreshes it after an edit. The rules
    /// themselves are not in here - the client already holds the config the server just broadcast.
    /// </summary>
    [ProtoContract]
    public class AdminConfigSnapshotPacket
    {
        [ProtoMember(1)] public bool Open;
        [ProtoMember(2)] public List<AdminMissingEntry> Missing = new List<AdminMissingEntry>();
        /// <summary>
        /// Lang key (without the domain) of the result of the last edit, null when there is nothing
        /// to say. A key rather than text: the server has no idea which language the admin reads.
        /// </summary>
        [ProtoMember(3)] public string Message;
        [ProtoMember(4)] public bool MessageIsError;
        [ProtoMember(5)] public List<string> MessageArgs = new List<string>();
    }

    /// <summary>Client to server: one edit to the socket and gem rules.</summary>
    [ProtoContract]
    public class AdminConfigEditPacket
    {
        [ProtoMember(1)] public AdminConfigOp Op;
        [ProtoMember(2)] public string Code;
        // Protobuf sends an empty array as nothing at all; read it with TiersOrEmpty.
        [ProtoMember(3)] public int[] Tiers;
        [ProtoMember(4)] public string AttributeKey;
        [ProtoMember(5)] public string VariantValue;
        [ProtoMember(6)] public string GemType;
        [ProtoMember(7)] public bool Allowed;
        [ProtoMember(8)] public List<string> Ids = new List<string>();

        public int[] TiersOrEmpty => Tiers ?? new int[0];
    }
}
