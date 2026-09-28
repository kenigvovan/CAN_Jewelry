using ProtoBuf;
using System.Collections.Generic;
using System.Linq;

namespace canjewelry.src
{
    /// <summary>
    /// Carries the gem cutting accessibility settings in both directions: server to client to open
    /// the settings dialogue with the current values in it, client to server when an admin saves.
    /// </summary>
    [ProtoContract]
    public class CuttingSettingsPacket
    {
        [ProtoMember(1)]
        public int VoxelsPerClick;

        [ProtoMember(2)]
        public bool DurabilityPerVoxel;

        [ProtoMember(3)]
        public bool InstantComplete;

        [ProtoMember(4)]
        public bool SpareRecipeVoxels;

        [ProtoMember(5)]
        public int HoldStrikeIntervalMs;

        [ProtoMember(6)]
        public string AccessMode;

        [ProtoMember(7)]
        public List<string> Whitelist = new List<string>();

        [ProtoMember(8)]
        public List<string> Blacklist = new List<string>();

        public static CuttingSettingsPacket FromConfig(Config config)
        {
            return new CuttingSettingsPacket
            {
                VoxelsPerClick = config.cuttingVoxelsPerClick,
                DurabilityPerVoxel = config.cuttingDurabilityPerVoxel,
                InstantComplete = config.cuttingInstantComplete,
                SpareRecipeVoxels = config.cuttingSpareRecipeVoxels,
                HoldStrikeIntervalMs = config.cuttingHoldStrikeIntervalMs,
                AccessMode = config.cuttingAccessMode,
                // Copies, not the config's own sets: the dialogue edits these freely and only the
                // save packet is allowed to change what the server holds.
                Whitelist = config.cuttingWhitelist == null ? new List<string>() : config.cuttingWhitelist.ToList(),
                Blacklist = config.cuttingBlacklist == null ? new List<string>() : config.cuttingBlacklist.ToList()
            };
        }

        /// <summary>
        /// Names as the config wants them: no blanks, no duplicates. A hand written packet can
        /// carry either, and a blank name would sit in the list matching nobody.
        /// </summary>
        public static HashSet<string> SanitiseNames(List<string> names)
        {
            var result = new HashSet<string>();
            if (names == null) return result;

            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name.Trim());
            }
            return result;
        }
    }
}
