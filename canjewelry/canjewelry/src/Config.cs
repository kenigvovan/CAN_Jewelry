using canjewelry.src.api;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canjewelry.src
{
    public class Config
    {
        public float grindTimeOneTick = 3;
        // Named item families referenced as "$armor" from buffNameToPossibleItem, so a gem that
        // accepts all armor is one entry instead of ninety. Whatever stands here wins: groups
        // that already exist are never topped up on a mod update, so items that later versions
        // add to their defaults have to be added here by hand.
        public Dictionary<string, HashSet<string>> item_groups = new Dictionary<string, HashSet<string>>();
        [JsonProperty(ItemConverterType = typeof(utils.ItemGroupSetConverter))]
        public Dictionary<string, HashSet<string>> buffNameToPossibleItem = new Dictionary<string, HashSet<string>>();
        [JsonProperty(ItemConverterType = typeof(utils.TierValuesConverter))]
        public Dictionary<string, Dictionary<string, float>> gems_buffs = new Dictionary<string, Dictionary<string, float>>();
        public Dictionary<string, int> items_codes_with_socket_count = new Dictionary<string, int>();
        [JsonProperty(ItemConverterType = typeof(utils.CompactIntArrayConverter))]
        public Dictionary<string, int[]> items_codes_with_socket_count_and_tiers = new Dictionary<string, int[]>();
        public HashSet<CustomVariantSocketsTiers> custom_variants_sockets_tiers = new HashSet<CustomVariantSocketsTiers>();
        public int pan_take_per_use;
        public Dictionary<string, string> gem_type_to_buff = new Dictionary<string, string>();
        public Dictionary<string, float> max_buff_values = new Dictionary<string, float>();
        public Dictionary<string, DropInfo[]> gems_drops_table = new Dictionary<string, DropInfo[]>();
        public bool debugMode;
        public float chance_gem_drop_on_item_broken;
        [JsonConverter(typeof(utils.CompactStringSetConverter))]
        public HashSet<string> buffs_to_show_gui = new HashSet<string>();
        public string config_version;
        [JsonProperty(ItemConverterType = typeof(utils.CompactStringSetConverter))]
        public Dictionary<string, HashSet<string>> PossibleGemBuffs = new Dictionary<string, HashSet<string>>();
        public Dictionary<string, BuffAttributes> BuffAttributesDict = new Dictionary<string, BuffAttributes>();
        public Dictionary<string, CuttingAttributes> CuttingAttributesDict = new Dictionary<string, CuttingAttributes>();
        public static Random rand = new Random();
        public int wirehank_per_strap = 4;
        [JsonConverter(typeof(utils.CompactStringArrayConverter))]
        public string[] socketTiersColorsWords = new string[0];
        [JsonConverter(typeof(utils.CompactStringArrayConverter))]
        public string[] socketTiersColors = new string[0];
        [JsonConverter(typeof(utils.CompactStringSetConverter))]
        public HashSet<string> TemporalGraspBlockList = new HashSet<string>();
        public bool TemporalGraspEnabled = true;
        public float minFineForMistake = 0.01f;
        public float maxFineForMistake = 0.08f;
        public bool TurnOffBuffs = false;
        // Accessibility settings for the gem cutting table, for players who have a hard time
        // picking out single voxels. Both are off/neutral by default, so an existing world plays
        // exactly as before. The whole config is mirrored to clients on join, so both sides
        // evaluate these identically and the voxel grid stays in sync.
        //
        // How many chisel strikes one click performs. Every strike past the first picks its own
        // target: the next voxel that the recipe does not want. Only applies to the 1x1 tool mode,
        // because the line modes already clear a whole row or layer per click.
        public int cuttingVoxelsPerClick = 1;
        // Whether each of those extra strikes costs chisel durability. Off means one click still
        // costs one point of durability no matter how many voxels it took off.
        public bool cuttingDurabilityPerVoxel = true;
        // Completes the selected recipe on the first strike. Blunt, but it is the only option that
        // helps a player who cannot work the grid at all.
        public bool cuttingInstantComplete = false;
        // Milliseconds between strikes while the attack button is held down on the table. 0 keeps
        // the vanilla behaviour of one strike per click.
        public int cuttingHoldStrikeIntervalMs = 0;
        // Makes the line tool modes skip voxels the recipe still needs, the way the 1x1 mode
        // already does. Without it a single mistimed line strike ruins the piece, which is the
        // failure these settings exist to prevent.
        public bool cuttingSpareRecipeVoxels = false;
        // Who the settings above apply to: "disabled", "enabled", "whitelist" or "blacklist".
        // The lists hold player names, matched case sensitively, same as the buff commands.
        public string cuttingAccessMode = "enabled";
        [JsonConverter(typeof(utils.CompactStringSetConverter))]
        public HashSet<string> cuttingWhitelist = new HashSet<string>();
        [JsonConverter(typeof(utils.CompactStringSetConverter))]
        public HashSet<string> cuttingBlacklist = new HashSet<string>();

        /// <summary>
        /// Whether the cutting accessibility settings apply to this player. Deliberately answered
        /// from the config alone, which every client receives in full on join: client and server
        /// both resolve a strike, so they have to agree on this without an extra round trip.
        /// </summary>
        public bool IsEasyCuttingEnabledFor(string playerName)
        {
            switch ((cuttingAccessMode ?? "enabled").ToLowerInvariant())
            {
                case "disabled": return false;
                case "whitelist": return playerName != null && cuttingWhitelist != null && cuttingWhitelist.Contains(playerName);
                case "blacklist": return playerName != null && (cuttingBlacklist == null || !cuttingBlacklist.Contains(playerName));
                default: return true;
            }
        }
        // When a mod update adds support for new items or metals, an existing config does not know
        // about them and that gear silently ends up without sockets. Turning this on lets the mod
        // append the missing entries on a version change, without touching anything already there.
        // Off by default: a missing entry may just as well be one that was deliberately removed.
        // Either way the log says how many entries are missing on startup.
        public bool add_missing_defaults_on_update = false;
        public float minGrinderProcessingSpeed = 0.3f;
        // doGrind tick counts per stage (lower = faster). Values get reduced by Lapidary's
        // Workshop Speedup perk when the companion mod is installed; without it these are the
        // raw stage durations. Floored at 1 server-side regardless of value.
        public int grinderStage1Counter = 10;
        public int grinderStage2Counter = 20;
        // Pan drops table. Null on first run — CANBlockPan.OnLoaded auto-bootstraps it from
        // the canpan.json block attribute and saves the config back, so admins get an editable
        // copy. Subsequent runs always read from here, ignoring the asset attribute.
        public Dictionary<string, utils.CANPanningDrop[]> panningDrops = null;
        public float gemExtractionReturnChance = 0.5f;
        public float jewelryBreakOnExtractionChance = 0.1f;
        // Whether the socket itself can be pulled back out of a jewelry/armor piece, and the
        // chance to recover the socket item when doing so. Default 100% keeps the historically
        // non-destructive behavior; lower it to make socket removal risky.
        public bool canExtractSocket = true;
        public float socketExtractionReturnChance = 0.25f;
        public Dictionary<string, int> LevelOfSocketByType = new Dictionary<string, int>();
        // Silences the startup banner shown when the adornments content mod is absent. For servers
        // that deliberately run the core alone and have already dealt with the display stands.
        public bool suppressAdornmentsWarning = false;

        // Empty on purpose: the adornments live in canjewelryadornments and register themselves
        // through CANJewelryRegistry.RegisterItemGroupMembers("jewelry", ...). The key itself stays
        // declared below so "$jewelry" in existing configs still resolves — to an empty list when
        // the core runs alone.
        private static readonly HashSet<string> BaseJewelrySets = new HashSet<string>();

        /// <summary>
        /// Base members plus anything content mods registered for the group. Deliberately computed
        /// on every read rather than cached in a static field: a content mod fills the registry from
        /// its own StartPre, which can run before this class is first touched.
        /// </summary>
        private static HashSet<string> ComposeGroup(string groupName, HashSet<string> baseMembers)
        {
            var result = new HashSet<string>(baseMembers);
            if (CANJewelryRegistry.ExtraItemGroupMembers.TryGetValue(groupName, out var extra))
            {
                result.UnionWith(extra);
            }
            return result;
        }

        private static HashSet<string> JewelrySets => ComposeGroup("jewelry", BaseJewelrySets);

        // The vanillaarmory entries used to be appended per gem by AddVanillaArmoryCompat, which
        // meant every gem carried them as literals and no group could be folded around them.
        // They belong to these families anyway, so they live here now. When that mod is absent
        // the codes simply match nothing.
        private static readonly HashSet<string> ArmorSets = new HashSet<string>
        {
            "brigandine", "plate", "chain", "scale", "-antique", "-tracker",
            "greenwich-head", "greenwich-body", "greenwich-legs",
            "viking-head", "viking-body", "viking-legs",
            "templar-head", "templar-body", "templar-legs",
            "landsknecht-head", "landsknecht-body", "landsknecht-legs",
            "hussar-head", "hussar-body", "hussar-legs",
            "gothic-head", "gothic-body", "gothic-legs",
            "dynasties-head", "dynasties-body", "dynasties-legs",
            "forlornzealot", "forlornacolyte",
            "sturdyleatherarmor", "bonearmor", "woodarmor"
        };

        private static readonly HashSet<string> MeleeWeaponSets = new HashSet<string>
        {
            "halberd", "mace", "spear", "rapier", "longsword", "zweihander", "messer", "falx",
            "ihammer", "tshammer", "biaxe", "tssword", "shammer", "hamb", "atgeir", "blade",
            "axe-long", "sword-long", "sword-great", "sword-short",
            "javelin-plain", "pike-plain", "club-plain", "mace-plain", "poleaxe-plain",
            "halberd-plain", "quarterstaff-plain", "claymore", "warhammer", "dagger",
            "cutlass", "hasta", "canopener", "walkingstick", "hidden-blade",
            "axe-bardiche", "axe-battle", "axe-bearded", "axe-double",
            "club-flanged", "club-morningstar", "club-spiked",
            "knife-baselard", "knife-khanjar", "knife-stiletto"
        };

        private static readonly HashSet<string> RangedSets = new HashSet<string>
        {
            "bow", "tbow-compound", "firearm-", "walkingstick-sling", "hidden-gun",
            "bow-ranger", "bow-yager"
        };

        private static readonly HashSet<string> ShieldSets = new HashSet<string>
        {
            "shield", "buckler", "forlorn-shield"
        };

        private static readonly HashSet<string> MiningToolSets = new HashSet<string>
        {
            "pickaxe", "shovel", "tunneler"
        };

        // Seed contents for the "$armor" style groups. Declared after the sets above because
        // static fields initialize in declaration order. Only ever used to fill a config that
        // has no item_groups yet - see item_groups for why they are not merged afterwards.
        private static Dictionary<string, HashSet<string>> DefaultItemGroups => new Dictionary<string, HashSet<string>>
        {
            { "armor",   ComposeGroup("armor",   ArmorSets) },
            { "jewelry", JewelrySets },
            { "melee",   ComposeGroup("melee",   MeleeWeaponSets) },
            { "mining",  ComposeGroup("mining",  MiningToolSets) },
            { "ranged",  ComposeGroup("ranged",  RangedSets) },
            { "shield",  ComposeGroup("shield",  ShieldSets) },
        };

        private static Dictionary<string, HashSet<string>> activeItemGroups;

        // The groups the serializer folds item lists against. Points at the loaded config's
        // item_groups once ExpandItemGroups has run; until then the defaults stand in, which is
        // what a config being created from scratch needs. Resolved lazily so a content mod's
        // registrations are not missed by a static initializer that ran too early.
        internal static Dictionary<string, HashSet<string>> ActiveItemGroups
        {
            get => activeItemGroups ??= DefaultItemGroups;
            set => activeItemGroups = value;
        }

        public void FillDefaultValues(bool onlyEmptyStructs = false)
        {
            if (item_groups.Count == 0) FillItemGroups();
            if (buffNameToPossibleItem.Count == 0) FillBuffItemSets();
            if (gems_buffs.Count == 0) FillGemBuffValues();
            if (items_codes_with_socket_count_and_tiers.Count == 0) FillSocketCounts();

            if (!onlyEmptyStructs || pan_take_per_use == 0) pan_take_per_use = 8;

            if (gem_type_to_buff.Count == 0)
            {
                FillGemTypeToBuff();
            }

            if (!onlyEmptyStructs || max_buff_values.Count == 0)
            {
                max_buff_values = new Dictionary<string, float>()
                {
                    { "walkspeed", 0.5f},
                    { "maxhealthExtraPoints", 25},
                    { "hungerrate", -0.2f}
                };
            }

            if (gems_drops_table.Count == 0) FillDropTable();

            debugMode = false;
            chance_gem_drop_on_item_broken = 0.2f;

            if (buffs_to_show_gui.Count == 0)
            {
                buffs_to_show_gui = new HashSet<string> { "walkspeed", "miningSpeedMul", "maxhealthExtraPoints", "meleeWeaponsDamage", "hungerrate", "wildCropDropRate", "wildCropDropRate",
                "armorDurabilityLoss", "oreDropRate",  "healingeffectivness", "rangedWeaponsDamage", "animalLootDropRate", "vesselContentsDropRate", "bowDrawingStrength", "animalSeekingRange",
                "armorWalkSpeedAffectedness", "rangedWeaponsSpeed", "mechanicalsDamage", "rangedWeaponsAcc"};
            }

            if (custom_variants_sockets_tiers.Count == 0) FillCustomVariantSockets();

            if (!onlyEmptyStructs || PossibleGemBuffs.Count == 0) FillPossibleGemBuffs();
            if (!onlyEmptyStructs || BuffAttributesDict.Count == 0) FillBuffAttributes();

            AddVanillaArmoryCompat();
        }

        private void FillItemGroups()
        {
            item_groups = new Dictionary<string, HashSet<string>>();
            // Copies, so editing a group in the config never mutates the shared static sets.
            foreach (var group in DefaultItemGroups) item_groups[group.Key] = new HashSet<string>(group.Value);
        }

        /// <summary>
        /// Replaces every "$group" reference in buffNameToPossibleItem with the group's contents.
        /// Deliberately not done inside the json converter: the converter would have to read the
        /// groups while the very same file is still being deserialized, which would make the
        /// result depend on field order. Here the whole config is already in memory.
        /// Call once after loading, before anything reads buffNameToPossibleItem.
        /// </summary>
        public void ExpandItemGroups()
        {
            // A config written before groups existed has none, so seed them once - otherwise the
            // section would be written back empty and stay empty forever.
            if (item_groups == null || item_groups.Count == 0) FillItemGroups();
            ActiveItemGroups = item_groups;
            if (buffNameToPossibleItem == null) return;

            foreach (string buffName in buffNameToPossibleItem.Keys.ToList())
            {
                HashSet<string> raw = buffNameToPossibleItem[buffName];
                if (raw == null) continue;

                HashSet<string> expanded = new HashSet<string>();
                foreach (string entry in raw)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;

                    string text = entry.Trim();
                    if (text[0] != '$')
                    {
                        expanded.Add(text);
                        continue;
                    }

                    string groupName = text.Substring(1);
                    if (!ActiveItemGroups.TryGetValue(groupName, out var group))
                    {
                        throw new InvalidOperationException(string.Format(
                            "[canjewelry] buffNameToPossibleItem.{0} references unknown item group \"{1}\". Known groups: {2}",
                            buffName, text, string.Join(", ", ActiveItemGroups.Keys.Select(k => "$" + k))));
                    }
                    expanded.UnionWith(group);
                }
                buffNameToPossibleItem[buffName] = expanded;
            }
        }

        private void FillBuffItemSets()
        {
            var armorAndShield = ArmorSets.Union(ShieldSets);
            // Snapshot once — JewelrySets is a property that recomposes the set on every read, and
            // the table below reads it three dozen times.
            var jewelrySets = JewelrySets;
            buffNameToPossibleItem = new Dictionary<string, HashSet<string>>
            {
                {"diamond",             armorAndShield.Union(jewelrySets).Union(new[]{"firearm-", "exoskeleton-", "walkingstick"}).ToHashSet()},

                {"corundum",           MiningToolSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"emerald",            armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"fluorite",           MeleeWeaponSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"lapislazuli",        armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"malachite",          ArmorSets.Union(jewelrySets).Union(new[]{"knife", "scythe", "exoskeleton-", "hidden-blade"}).ToHashSet()},
                {"olivine",            armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"uranium",            armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"quartz",             MiningToolSets.Union(jewelrySets).Union(new[]{"tspaxel", "exoskeleton-"}).ToHashSet()},
                {"ruby",               jewelrySets.Union(new[]{"bow", "tbow-compound", "firearm-", "walkingstick-sling", "hidden-gun", "exoskeleton-"}).ToHashSet()},
                {"citrine",            jewelrySets.Union(new[]{"knife", "exoskeleton-", "hidden-blade"}).ToHashSet()},
                {"berylaquamarine",    armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-", "walkingstick"}).ToHashSet()},
                {"berylbixbite",       MiningToolSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"corundumruby",       jewelrySets.Union(new[]{"bow", "tbow-compound", "walkingstick-sling", "hidden-gun", "exoskeleton-"}).ToHashSet()},
                {"corundumsapphire",   MiningToolSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"garnetalmandine",    jewelrySets.Union(new[]{"bow", "tspaxel", "tbow-compound", "walkingstick-sling", "exoskeleton-"}).ToHashSet()},
                {"garnetandradite",    armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"garnetgrossular",    MeleeWeaponSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"garnetpyrope",       jewelrySets.Union(new[]{"knife", "exoskeleton-", "hidden-blade"}).ToHashSet()},
                {"garnetspessartine",  jewelrySets.Union(new[]{"knife", "exoskeleton-", "hidden-blade"}).ToHashSet()},
                {"garnetuvarovite",    jewelrySets.Union(new[]{"bow", "walkingstick-sling", "exoskeleton-"}).ToHashSet()},
                {"spinelred",          armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"topazamber",         ArmorSets.Union(jewelrySets).Union(new[]{"knife", "exoskeleton-", "cutlass", "hasta", "canopener", "walkingstick", "hidden-blade"}).ToHashSet()},
                {"topazblue",          ArmorSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"topazpink",          jewelrySets.Union(new[]{"knife", "exoskeleton-", "hidden-blade"}).ToHashSet()},
                {"tourmalinerubellite",ArmorSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"tourmalineschorl",   MeleeWeaponSets.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"tourmalineverdelite",armorAndShield.Union(jewelrySets).Union(new[]{"exoskeleton-"}).ToHashSet()},
                {"tourmalinewatermelon",jewelrySets.Union(new[]{"bow", "tbow-compound", "walkingstick-sling", "hidden-gun", "exoskeleton-"}).ToHashSet()},
                {"amethyst",           MeleeWeaponSets.Union(ArmorSets).Union(jewelrySets).Union(ShieldSets)
                                           .Union(new[]{"bow", "knife", "axe-felling-", "prospectingpick-", "hammer-",
                                                       "shovel-", "hoe-", "saw-", "chisel-", "scythe-", "pickaxe-",
                                                       "tunneler", "firearm-", "exoskeleton-", "walkingstick-sling", "hidden-gun"}).ToHashSet()},
                {"topaz",              new HashSet<string>{"pickaxe", "shovel"}},
                {"pearl",              new HashSet<string>{"pickaxe", "shovel"}},
            };
        }

        private void FillGemBuffValues()
        {
            if (gems_buffs.Count == 0)
            {
                gems_buffs = new Dictionary<string, Dictionary<string, float>>
            (new Dictionary<string, Dictionary<string, float>>  {
                { "walkspeed", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.08f }
                    }
                },
                { "miningSpeedMul", new Dictionary<string, float>{
                    { "1", 0.03f },
                    { "2", 0.06f },
                    { "3", 0.09f }
                    }
                },
                { "maxhealthExtraPoints", new Dictionary<string, float>{
                    { "1", 1 },
                    { "2", 2 },
                    { "3", 4 }
                    }
                },
                { "meleeWeaponsDamage", new Dictionary<string, float>{
                    { "1", 0.03f },
                    { "2", 0.05f },
                    { "3", 0.08f }
                    }
                },
                { "hungerrate", new Dictionary<string, float>{
                    { "1", -0.03f },
                    { "2", -0.06f },
                    { "3", -0.1f }
                    }
                },
                { "wildCropDropRate", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.05f },
                    { "3", 0.09f }
                    }
                },
                { "armorDurabilityLoss", new Dictionary<string, float>{
                    { "1", -0.05f },
                    { "2", -0.1f },
                    { "3", -0.15f }
                    }
                },
                { "oreDropRate", new Dictionary<string, float>{
                    { "1", 0.05f },
                    { "2", 0.08f },
                    { "3", 0.11f }
                    }
                },
                { "healingeffectivness", new Dictionary<string, float>{
                    { "1", 0.05f },
                    { "2", 0.1f },
                    { "3", 0.12f }
                    }
                },
                { "rangedWeaponsDamage", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.09f }
                    }
                },
                { "animalLootDropRate", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.09f }
                    }
                },

                { "vesselContentsDropRate", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.09f }
                    }
                },

                { "animalSeekingRange", new Dictionary<string, float>{
                    { "1", -0.03f },
                    { "2", -0.05f },
                    { "3", -0.10f }
                    }
                },
                { "rangedWeaponsSpeed", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.05f },
                    { "3", 0.08f }
                    }
                },
                { "animalHarvestingTime", new Dictionary<string, float>{
                    { "1", -0.02f },
                    { "2", -0.05f },
                    { "3", -0.08f }
                    }
                },
                { "mechanicalsDamage", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.08f }
                    }
                },
                { "rangedWeaponsAcc", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.07f }
                    }
                },
                { "armorWalkSpeedAffectedness", new Dictionary<string, float>{
                    { "1", -0.02f },
                    { "2", -0.04f },
                    { "3", -0.07f }
                    }
                },
                { "bowDrawingStrength", new Dictionary<string, float>{
                    { "1", 0.01f },
                    { "2", 0.03f },
                    { "3", 0.06f }
                    }
                },
                { "candurability", new Dictionary<string, float>{
                    { "1", 0.2f },
                    { "2", 0.45f },
                    { "3", 0.95f }
                    }
                },
                { "temporalgrasp", new Dictionary<string, float>{
                    { "1", 0.2f },
                    { "2", 0.45f },
                    { "3", 0.95f }
                    }
                }
            });
            }
        }

        private void FillSocketCounts()
        {
            items_codes_with_socket_count = new Dictionary<string, int>();
            items_codes_with_socket_count_and_tiers = BuildDefaultSocketCounts();
        }

        private static Dictionary<string, int[]> BuildDefaultSocketCounts()
        {
            var defaults = new Dictionary<string, int[]>()
        {
            // The adornments' own socket defaults come from the canjewelryadornments mod through
            // CANJewelryRegistry — see the merge at the end of this method.
            #region Vanilla
            { "*knife-generic-gold", new int[1] {3} },
            { "*knife-generic-silver", new int[1] {3} },
            { "*knife-generic-iron",  new int[1] {3} },
            { "*knife-generic-meteoriciron", new int[2] {3, 3} },
            { "*knife-generic-steel", new int[3] {3, 3, 3} },

            {  "*pickaxe-blackbronze", new int[1] {3} },
            {  "*pickaxe-tinbronze", new int[1] {3} },
            {  "*pickaxe-bismuthbronze", new int[1] {3} },
            {  "*pickaxe-gold", new int[1] {3} },
            {  "*pickaxe-silver", new int[1] {3} },
            {  "*pickaxe-iron", new int[2] {3, 3} },
            {  "*pickaxe-meteoriciron", new int[2] {3, 3} },
            {  "*pickaxe-steel", new int[3] {3, 3, 3} },

            {  "*scythe-blackbronze", new int[1] {3} },
            {  "*scythe-tinbronze", new int[1] {3} },
            {  "*scythe-bismuthbronze", new int[1] {3} },
            {  "*scythe-gold", new int[1] {3} },
            {  "*scythe-iron", new int[2] {3, 3} },
            {  "*scythe-meteoriciron", new int[2] {3, 3} },
            {  "*scythe-steel", new int[3] {3, 3, 3} },

            {  "*shovel-blackbronze", new int[1] {3} },
            {  "*shovel-tinbronze", new int[1] {3} },
            {  "*shovel-bismuthbronze", new int[1] {3} },
            {  "*shovel-gold", new int[1] {3} },
            {  "*shovel-iron", new int[2] {3, 3} },
            {  "*shovel-meteoriciron", new int[2] {3, 3} },
            {  "*shovel-steel", new int[3] {3, 3, 3} },

            {  "armor-head-brigandine-*", new int[1] {3} },
            {  "armor-legs-brigandine-*", new int[1] {3} },
            {  "armor-body-brigandine-*", new int[2] {3, 3} },


            {  "armor-head-plate-*", new int[1] {3} },
            {  "armor-legs-plate-*", new int[1] {3} },
            {  "armor-body-plate-*", new int[2] {3, 3} },

            {  "armor-head-scale-*", new int[1] {3} },
            {  "armor-legs-scale-*", new int[1] {3} },
            {  "armor-body-scale-*", new int[2] {3, 3} },


            {  "armor-head-chain-*", new int[1] {3} },
            {  "armor-legs-chain-*", new int[1] {3} },
            {  "armor-body-chain-*", new int[2] {3, 3} },

            {  "armor-head-antique-*", new int[1] {3} },
            {  "armor-legs-antique-*", new int[1] {3} },
            {  "armor-body-antique-*", new int[2] {3, 3} },

            {  "xmelee:xzweihander-meteoriciron", new int[1] {3} },
            {  "xmelee:xzweihander-steel", new int[2] {3, 3} },

            {  "xmelee:xlongsword-meteoriciron", new int[1] {3} },
            {  "xmelee:xlongsword-steel", new int[2] {3, 3} },


            {  "xmelee:xhalberd-meteoriciron", new int[1] {3} },
            {  "xmelee:xhalberd-steel", new int[2] {3, 3} },


            {  "xmelee:xmesser-meteoriciron", new int[1] {3} },
            {  "xmelee:xmesser-steel", new int[2] {3, 3} },

            {  "xmelee:xmace-meteoriciron", new int[1] {3} },
            {  "xmelee:xmace-steel", new int[2] {3, 3} },

            {  "xmelee:xpike-meteoriciron", new int[1] {3} },
            {  "xmelee:xpike-steel", new int[2] {3, 3} },

            {  "xmelee:xrapier-meteoriciron", new int[1] {3} },
            {  "xmelee:xrapier-steel", new int[2] {3, 3} },


            {  "xmelee:xspear-meteoriciron", new int[1] {3} },
            {  "xmelee:xspear-steel", new int[2] {3, 3} },

            {  "*blade-falx-gold", new int[1] {3} },
            {  "*blade-falx-silver", new int[1] {3} },
            {  "*blade-falx-iron", new int[1] {3} },
            {  "*blade-falx-meteoriciron", new int[2] {3, 3} },
            {  "*blade-falx-steel", new int[2] {3, 3} },
            {  "*blade-blackguard-iron", new int[2] {3, 3} },
            {  "*blade-forlorn-iron", new int[2] {3, 3} },
            {  "*blade-longsword-admin", new int[3] {3, 3, 3} },

            {  "bow-simple", new int[1] {3} },
            {  "bow-recurve", new int[2] {3, 3} },
            {  "bow-long", new int[2] {3, 3} },

            #endregion
            #region tstools
            { "tstools:ihammer",  new int[1] { 3 } },
            { "tstools:tshammer",  new int[2] {3, 3}  },
            { "tstools:tspaxel",  new int[2] {3, 3}  },
            { "tstools:tspickaxe",  new int[2] {3, 3}  },
            { "tstools:biaxe",  new int[2] {3, 3}  },
            { "tstools:tssword",  new int[2] {3, 3}  },
            { "tstools:shammer",  new int[2] {3, 3}  },
            { "tstools:hamb",  new int[2] {3, 3}  },
            { "tstools:tbow-compound",  new int[2] {3, 3}  },

            #endregion
            #region swordz
            { "swordz:zweihander-iron-*",  new int[2] {3, 3}  },
            { "swordz:zweihander-meteoriciron-*",  new int[2] {3, 3}  },
            { "swordz:zweihander-steel-*",  new int[3] {3, 3, 3}  },

            { "swordz:atgeir-iron",  new int[2] {3, 3}  },
            { "swordz:atgeir-meteoriciron",  new int[2] {3, 3}  },
            { "swordz:atgeir-steel",  new int[3] {3, 3, 3}  },

            {  "swordz:armor-head-*", new int[1] {3} },
            {  "swordz:armor-legs-*", new int[1] {3} },
            {  "swordz:armor-body-*", new int[2] {3, 3} },

            { "swordz:tunneler-steel-*",  new int[2] {3, 3}  },
            { "swordz:tunneler-stainlesssteel-*",  new int[2] {3, 3}  },
            { "swordz:tunneler-titanium-*",  new int[3] {3, 3, 3}  },
            { "swordz:tunneler-mithril-*",  new int[3] {3, 3, 3}  },
            { "swordz:tunneler-adamant-*",  new int[3] {3, 3, 3}  },
            { "swordz:tunneler-orichalcum-*",  new int[3] {3, 3, 3}  },
            { "swordz:tunneler-aithril-*",  new int[4] {3, 3, 3, 3}  },

            { "swordz:pernach-iron-*",  new int[2] {3, 3}  },
            { "swordz:pernach-meteoriciron-*",  new int[2] {3, 3}  },
            { "swordz:pernach-steel-*",  new int[3] {3, 3, 3}  },

            { "swordz:warhammer-iron",  new int[2] {3, 3}  },
            { "swordz:warhammer-meteoriciron",  new int[2] {3, 3}  },
            { "swordz:warhammer-steel",  new int[3] {3, 3, 3}  },

            { "swordz:stiletto-iron",  new int[2] {3, 3}  },
            { "swordz:stiletto-meteoriciron",  new int[2] {3, 3}  },
            { "swordz:stiletto-steel",  new int[3] {3, 3, 3}  },


            { "swordz:knife-generic-stainelesssteel", new int[1] {3} },
             { "swordz:knife-generic-titanium", new int[2] {3, 3} },
             { "swordz:knife-generic-mithril", new int[2] {3, 3} },
             { "swordz:knife-generic-adamant", new int[2] {3, 3} },
             { "swordz:knife-generic-orichalcum", new int[2] {3, 3} },
             { "swordz:knife-generic-aithril", new int[3] {3, 3, 3} },

              { "swordz:sord-iron-*",  new int[2] {3, 3}  },
            { "swordz:sord-meteoriciron-*",  new int[2] {3, 3}  },
            { "swordz:sord-steel-*",  new int[3] {3, 3, 3}  },


            { "swordz:gladius-iron-*",  new int[2] {3, 3}  },


            { "swordz:kilij-iron-*",  new int[2] {3, 3}  },
            { "swordz:kilij-meteoriciron-*",  new int[2] {3, 3}  },
            { "swordz:kilij-steel-*",  new int[3] {3, 3, 3}  },

             { "swordz:longsword-iron-*",  new int[2] {3, 3}  },
            { "swordz:longsword-meteoriciron-*",  new int[2] {3, 3}  },
            { "swordz:longsword-steel-*",  new int[3] {3, 3, 3}  },

            { "diamondpick-steel",  new int[3] {3, 3, 3}  },
            #endregion
            #region armory
            { "armory:axe-long-plain-tinbronze",  new int[1] {2}  },
            { "armory:axe-long-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:axe-long-plain-blackbronze",  new int[1] {2}  },
            { "armory:axe-long-plain-iron",  new int[1] {3}  },
            { "armory:axe-long-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:axe-long-plain-steel",  new int[3] {3, 3, 3}  },

            { "armory:sword-great-plain-iron",  new int[1] {3}  },
            { "armory:sword-great-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:sword-great-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:sword-long-plain-tinbronze",  new int[1] {2}  },
            { "armory:sword-long-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:sword-long-plain-blackbronze",  new int[1] {2}  },
            { "armory:sword-long-plain-iron",  new int[1] {3}  },
            { "armory:sword-long-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:sword-long-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:sword-short-plain-tinbronze",  new int[1] {2}  },
            { "armory:sword-short-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:sword-short-plain-blackbronze",  new int[1] {2}  },
            { "armory:sword-short-plain-iron",  new int[1] {3}  },
            { "armory:sword-short-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:sword-short-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:javelin-plain-tinbronze",  new int[1] {2}  },
            { "armory:javelin-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:javelin-plain-blackbronze",  new int[1] {2}  },
            { "armory:javelin-plain-iron",  new int[1] {3}  },
            { "armory:javelin-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:javelin-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:pike-plain-tinbronze",  new int[1] {2}  },
            { "armory:pike-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:pike-plain-blackbronze",  new int[1] {2}  },
            { "armory:pike-plain-iron",  new int[1] {3}  },
            { "armory:pike-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:pike-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:club-plain-tinbronze",  new int[1] {2}  },
            { "armory:club-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:club-plain-blackbronze",  new int[1] {2}  },
            { "armory:club-plain-iron",  new int[1] {3}  },
            { "armory:club-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:club-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:mace-plain-tinbronze",  new int[1] {2}  },
            { "armory:mace-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:mace-plain-blackbronze",  new int[1] {2}  },
            { "armory:mace-plain-iron",  new int[1] {3}  },
            { "armory:mace-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:mace-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:poleaxe-plain-steel",  new int[2] {3, 3}  },

            { "armory:halberd-plain-iron",  new int[1] {3}  },
            { "armory:halberd-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:halberd-plain-steel",  new int[3] {3, 3, 3 }  },

            { "armory:quarterstaff-plain-tinbronze",  new int[1] {2}  },
            { "armory:quarterstaff-plain-bismuthbronze",  new int[1] {2}  },
            { "armory:quarterstaff-plain-blackbronze",  new int[1] {2}  },
            { "armory:quarterstaff-plain-iron",  new int[1] {3}  },
            { "armory:quarterstaff-plain-meteoriciron",  new int[2] {3, 3}  },
            { "armory:quarterstaff-plain-steel",  new int[3] {3, 3, 3 }  },

            {  "armory:armor-torso-brigandine-*", new int[1] {3} },
            {  "armory:armor-legs-brigandine-*", new int[1] {3} },
            {  "armory:armor-head-*-light", new int[1] {3} },

            {  "armory:armor-torso-plate-*", new int[1] {3} },
            {  "armory:armor-legs-plate-*", new int[1] {3} },
            {  "armory:armor-body-plate-*", new int[1] {3} },

            {  "armory:armor-head-chain-*", new int[1] {3} },
            {  "armory:armor-torso-chain-*", new int[1] {3} },
            #endregion
            #region blackguardadditions + FA armor mods
            {  "blackguardadditions:armor-head-antique-*", new int[1] {3} },
            {  "blackguardadditions:armor-legs-antique-*", new int[1] {3} },
            {  "blackguardadditions:armor-body-antique-*", new int[2] {3, 3} },
            {  "blackguardadditions:claymore", new int[3] {3, 3, 3 } },
            {  "blackguardadditions:dagger", new int[1] {3} },
            {  "blackguardadditions:warhammer", new int[3] {3, 3, 3 } },
            {  "blackguardadditions:blackguardbow-*", new int[2] {3, 3}  },
            {  "blackguardadditions:armor-head-tracker-*", new int[1] {3} },
            {  "blackguardadditions:armor-legs-tracker-*", new int[1] {3} },
            {  "blackguardadditions:armor-body-tracker-*", new int[2] {3, 3} },
            {  "fagreenwich:fa-greenwich-head-*", new int[1] {3} },
            {  "fagreenwich:fa-greenwich-body-*", new int[2] {3, 3} },
            {  "fagreenwich:fa-greenwich-legs-*", new int[1] {3} },

            {  "faviking:fa-viking-head-*", new int[1] {3} },
            {  "faviking:fa-viking-body-*", new int[2] {3, 3} },
            {  "faviking:fa-viking-legs-*", new int[1] {3} },

            {  "fatemplar:fa-templar-head-*", new int[1] {3} },
            {  "fatemplar:fa-templar-body-*", new int[2] {3, 3} },
            {  "fatemplar:fa-templar-legs-*", new int[1] {3} },

            {  "falandsknecht:fa-landsknecht-head-*", new int[1] {3} },
            {  "falandsknecht:fa-landsknecht-body-*", new int[2] {3, 3} },
            {  "falandsknecht:fa-landsknecht-legs-*", new int[1] {3} },

            {  "fagothic:fa-gothic-head-*", new int[1] {3} },
            {  "fagothic:fa-gothic-body-*", new int[2] {3, 3} },
            {  "fagothic:fa-gothic-legs-*", new int[1] {3} },

            {  "fahussar:fa-hussar-head-*", new int[1] {3} },
            {  "fahussar:fa-hussar-body-*", new int[2] {3, 3} },
            {  "fahussar:fa-hussar-legs-*", new int[1] {3} },

            {  "fadynasties:fa-dynasties-head-*", new int[1] {3} },
            {  "fadynasties:fa-dynasties-body-*", new int[2] {3, 3} },
            {  "fadynasties:fa-dynasties-legs-*", new int[1] {3} },


            #endregion
            #region firearms + exoskeletons
            {  "maltiezfirearms:firearm-arquebus-iron", new int[1] {1} },
            {  "maltiezfirearms:firearm-arquebus-metoriciron", new int[1] {2} },
            {  "maltiezfirearms:firearm-arquebus-rusted", new int[1] {2} },
            {  "maltiezfirearms:firearm-arquebus-steel", new int[1] {3} },

            {  "maltiezfirearms:firearm-blunderbuss-iron", new int[1] {1} },
            {  "maltiezfirearms:firearm-blunderbuss-metoriciron", new int[1] {2} },
            {  "maltiezfirearms:firearm-blunderbuss-rusted", new int[1] {2} },
            {  "maltiezfirearms:firearm-blunderbuss-steel", new int[1] {3} },

            {  "maltiezfirearms:firearm-carbine-iron", new int[1] {1} },
            {  "maltiezfirearms:firearm-carbine-metoriciron", new int[1] {2} },
            {  "maltiezfirearms:firearm-carbine-rusted", new int[1] {2} },
            {  "maltiezfirearms:firearm-carbine-steel", new int[1] {3} },

            {  "maltiezfirearms:firearm-jezail-steel", new int[1] {3} },

            {  "maltiezfirearms:firearm-musket-steel", new int[1] {3} },

            {  "maltiezfirearms:firearm-pistol-steel", new int[1] {3} },

            {  "exoskeletons:exoskeleton-legs-armored-*", new int[1] {3} },
            {  "exoskeletons:exoskeleton-arms-armored-*", new int[1] {3} },
            {  "exoskeletons:exoskeleton-legs-unarmored-*", new int[2] {3, 1} },
            {  "exoskeletons:exoskeleton-arms-unarmored-*", new int[2] {3, 1} },
            {  "exoskeletons:exoskeleton-torso-armored-*", new int[1] {3} },
            {  "exoskeletons:exoskeleton-torso-unarmored-*", new int[2] {3, 1} },

            {  "extrafirearms:firearm-arquebusflintlock-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-blunderbusdb-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-hallrifle-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-jezail-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-pistolaxe-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-pistol-iron-*", new int[1] {1} },
            {  "extrafirearms:firearm-pistol-meteoriciron-*", new int[1] {1} },
            {  "extrafirearms:firearm-pistolrevolver-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-pistol-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-superimposed-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-superimposed-iron-*", new int[1] {1} },
            {  "extrafirearms:firearm-superimposed-meteoriciron-*", new int[1] {1} },
            {  "extrafirearms:firearm-volleygun-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-wheelaxe-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-wheellockcarabine-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-wheellockcarabine-iron-*", new int[1] {1} },
            {  "extrafirearms:firearm-wheellockcarabine-meteoriciron-*", new int[1] {1} },
            {  "extrafirearms:firearm-wheellockrifle-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-wheellockrifle-iron-*", new int[1] {1} },
            {  "extrafirearms:firearm-wheellockrifle-meteoriciron-*", new int[1] {1} },
            {  "extrafirearms:firearm-arquebus-steel-*", new int[1] {3} },
            {  "extrafirearms:firearm-arquebus-iron-*", new int[1] {1} },
            {  "extrafirearms:firearm-arquebus-meteoriciron-*", new int[1] {1} },
            {  "extrafirearms:firearm-blunderbuss-steel-*", new int[1] {3} },

            #endregion
            #region AncientWeaponry
            { "ancientweaponry:cutlass-tinbronze", new int[1] {2} },
            { "ancientweaponry:cutlass-bismuthbronze", new int[1] {2} },
            { "ancientweaponry:cutlass-blackbronze", new int[1] {2} },
            { "ancientweaponry:cutlass-gold", new int[1] {3} },
            { "ancientweaponry:cutlass-silver", new int[1] {3} },
            { "ancientweaponry:cutlass-iron", new int[1] {3} },
            { "ancientweaponry:cutlass-meteoriciron", new int[2] {3, 3} },
            { "ancientweaponry:cutlass-steel", new int[2] {3, 3} },
            { "ancientweaponry:cutlass-lockalloy", new int[2] {3, 3} },

            { "ancientweaponry:zweihander-iron", new int[1] {3} },
            { "ancientweaponry:zweihander-gold", new int[1] {3} },
            { "ancientweaponry:zweihander-silver", new int[1] {3} },
            { "ancientweaponry:zweihander-meteoriciron", new int[2] {3, 3} },
            { "ancientweaponry:zweihander-steel", new int[3] {3, 3, 3} },
            { "ancientweaponry:zweihander-lockalloy", new int[2] {3, 3} },

            { "ancientweaponry:hasta-wooden-steel", new int[1] {3} },
            { "ancientweaponry:hasta-wooden-gold", new int[1] {3} },
            { "ancientweaponry:hasta-wooden-silver", new int[1] {3} },
            { "ancientweaponry:hasta-metal-steel", new int[2] {3, 3} },
            { "ancientweaponry:hasta-metal-gold", new int[1] {3} },
            { "ancientweaponry:hasta-metal-silver", new int[1] {3} },

            { "ancientweaponry:canopener-steel", new int[2] {3, 3} },
            { "ancientweaponry:canopener-gold", new int[1] {3} },
            { "ancientweaponry:canopener-silver", new int[1] {3} },

            #endregion
            #region ForlornAdditions
            { "forlornadditions:claymore", new int[2] {3, 3} },
            { "forlornadditions:dagger", new int[1] {3} },
            { "forlornadditions:warhammer", new int[2] {3, 3} },
            { "forlornadditions:forlornbow-forlorn", new int[2] {3, 3} },

            { "forlornadditions:armor-head-zealot-forlornzealot", new int[1] {3} },
            { "forlornadditions:armor-body-zealot-forlornzealot", new int[2] {3, 3} },
            { "forlornadditions:armor-legs-zealot-forlornzealot", new int[1] {3} },
            { "forlornadditions:armor-head-acolyte-forlornacolyte", new int[1] {3} },
            { "forlornadditions:armor-body-acolyte-forlornacolyte", new int[2] {3, 3} },
            { "forlornadditions:armor-legs-acolyte-forlornacolyte", new int[1] {3} },

            #endregion
            #region WalkingStick
            { "walkingstick:walkingstick", new int[1] {1} },
            { "walkingstick:walkingstick-fine", new int[1] {2} },
            { "walkingstick:walkingstick-blackthorn", new int[1] {1} },
            { "walkingstick:walkingstick-cowskull", new int[1] {1} },
            { "walkingstick:walkingstick-shepherds", new int[1] {1} },
            { "walkingstick:walkingstick-witch", new int[1] {2} },
            { "walkingstick:walkingstick-lantern", new int[1] {2} },
            { "walkingstick:walkingstick-jonas", new int[2] {2, 2} },
            { "walkingstick:walkingstick-reinforced", new int[2] {3, 2} },
            { "walkingstick:walkingstick-architect", new int[2] {3, 3} },
            { "walkingstick:walkingstick-pathfinder", new int[2] {3, 3} },
            { "walkingstick:walkingstick-blackguard", new int[2] {3, 3} },
            { "walkingstick:walkingstick-forlorn", new int[2] {3, 3} },
            { "walkingstick:hidden-blade", new int[2] {3, 3} },
            { "walkingstick:hidden-gun", new int[1] {3} },
            { "walkingstick:walkingstick-sling", new int[1] {3} },

            #endregion
            #region Shields
            { "shield-crude", new int[1] {1} },
            { "shield-blackguard", new int[1] {2} },
            { "roundshield-*-tinbronze", new int[1] {2} },
            { "roundshield-*-bismuthbronze", new int[1] {2} },
            { "roundshield-*-blackbronze", new int[1] {2} },
            { "roundshield-*-gold", new int[1] {3} },
            { "roundshield-*-silver", new int[1] {3} },
            { "roundshield-*-iron", new int[1] {3} },
            { "roundshield-*-meteoriciron", new int[1] {3} },
            { "roundshield-*-steel", new int[2] {3, 3} },

            #endregion
            #region MeteoricSteel
            { "*pickaxe-meteoricsteel", new int[3] {3, 3, 3} },
            { "*shovel-meteoricsteel", new int[3] {3, 3, 3} },
            { "*scythe-meteoricsteel", new int[3] {3, 3, 3} },
            { "*knife-generic-meteoricsteel", new int[3] {3, 3, 3} },
            { "*blade-falx-meteoricsteel", new int[3] {3, 3, 3} },
            { "spear-generic-meteoricsteel", new int[3] {3, 3, 3} },

            #endregion
            #region canadditionalmetals
            { "*pickaxe-blacksteel",        new int[3] {3, 3, 3} },
            { "*pickaxe-redsteel",          new int[3] {3, 3, 3} },
            { "*pickaxe-bluesteel",         new int[3] {3, 3, 3} },

            { "*shovel-blacksteel",         new int[3] {3, 3, 3} },
            { "*shovel-redsteel",           new int[3] {3, 3, 3} },
            { "*shovel-bluesteel",          new int[3] {3, 3, 3} },

            { "*scythe-blacksteel",         new int[3] {3, 3, 3} },
            { "*scythe-redsteel",           new int[3] {3, 3, 3} },
            { "*scythe-bluesteel",          new int[3] {3, 3, 3} },

            { "*knife-generic-blacksteel",  new int[3] {3, 3, 3} },
            { "*knife-generic-redsteel",    new int[3] {3, 3, 3} },
            { "*knife-generic-bluesteel",   new int[3] {3, 3, 3} },

            { "armory:axe-long-plain-blacksteel",    new int[3] {3, 3, 3} },
            { "armory:axe-long-plain-redsteel",      new int[3] {3, 3, 3} },
            { "armory:axe-long-plain-bluesteel",     new int[3] {3, 3, 3} },

            { "armory:sword-great-plain-blacksteel", new int[3] {3, 3, 3} },
            { "armory:sword-great-plain-redsteel",   new int[3] {3, 3, 3} },
            { "armory:sword-great-plain-bluesteel",  new int[3] {3, 3, 3} },

            { "armory:sword-long-plain-blacksteel",  new int[3] {3, 3, 3} },
            { "armory:sword-long-plain-redsteel",    new int[3] {3, 3, 3} },
            { "armory:sword-long-plain-bluesteel",   new int[3] {3, 3, 3} },

            { "armory:sword-short-plain-blacksteel", new int[3] {3, 3, 3} },
            { "armory:sword-short-plain-redsteel",   new int[3] {3, 3, 3} },
            { "armory:sword-short-plain-bluesteel",  new int[3] {3, 3, 3} },

            { "armory:javelin-plain-blacksteel",     new int[3] {3, 3, 3} },
            { "armory:javelin-plain-redsteel",       new int[3] {3, 3, 3} },
            { "armory:javelin-plain-bluesteel",      new int[3] {3, 3, 3} },

            { "armory:pike-plain-blacksteel",        new int[3] {3, 3, 3} },
            { "armory:pike-plain-redsteel",          new int[3] {3, 3, 3} },
            { "armory:pike-plain-bluesteel",         new int[3] {3, 3, 3} },

            { "armory:club-plain-blacksteel",        new int[3] {3, 3, 3} },
            { "armory:club-plain-redsteel",          new int[3] {3, 3, 3} },
            { "armory:club-plain-bluesteel",         new int[3] {3, 3, 3} },

            { "armory:mace-plain-blacksteel",        new int[3] {3, 3, 3} },
            { "armory:mace-plain-redsteel",          new int[3] {3, 3, 3} },
            { "armory:mace-plain-bluesteel",         new int[3] {3, 3, 3} },

            { "armory:poleaxe-plain-blacksteel",     new int[3] {3, 3, 3} },
            { "armory:poleaxe-plain-redsteel",       new int[3] {3, 3, 3} },
            { "armory:poleaxe-plain-bluesteel",      new int[3] {3, 3, 3} },

            { "armory:halberd-plain-blacksteel",     new int[3] {3, 3, 3} },
            { "armory:halberd-plain-redsteel",       new int[3] {3, 3, 3} },
            { "armory:halberd-plain-bluesteel",      new int[3] {3, 3, 3} },

            { "armory:quarterstaff-plain-blacksteel",new int[3] {3, 3, 3} },
            { "armory:quarterstaff-plain-redsteel",  new int[3] {3, 3, 3} },
            { "armory:quarterstaff-plain-bluesteel", new int[3] {3, 3, 3} },

            { "armory:sabre-plain-blacksteel",       new int[3] {3, 3, 3} },
            { "armory:sabre-plain-redsteel",         new int[3] {3, 3, 3} },
            { "armory:sabre-plain-bluesteel",        new int[3] {3, 3, 3} },

            { "*blade-falx-blacksteel",              new int[3] {3, 3, 3} },
            { "*blade-falx-redsteel",                new int[3] {3, 3, 3} },
            { "*blade-falx-bluesteel",               new int[3] {3, 3, 3} },
            { "*blade-falx-rosegold",                new int[3] {3, 3, 3} },
            { "*blade-falx-sterlingsilver",          new int[3] {3, 3, 3} },
            #endregion
           /* {  "game:shears-", new int[1] {3} },

            {  "game:shears-blackbronze", new int[1] {2} },
            {  "game:shears-tinbronze", new int[1] {2} },
            {  "game:shears-bismuthbronze", new int[1] {2} },
            {  "game:shears-gold", new int[1] {3} },
            {  "game:shears-silver", new int[1] {3} },
            {  "game:shears-iron", new int[2] {3, 1} },
            {  "game:shears-meteoriciron", new int[2] {3, 2} },
            {  "game:shears-steel", new int[2] {3, 3} },*/

        };

            // Content mods contribute their own jewelry here. Registered entries win over the
            // core's, so a content mod can also correct a default the core got wrong.
            foreach (var entry in CANJewelryRegistry.ExtraSocketCounts)
            {
                defaults[entry.Key] = entry.Value;
            }

            return defaults;
        }

        // =====================================================================
        // Vanilla Armory soft-compat. Makes that mod's weapons, shields and
        // armor gem-socketable, and lets the matching gems be encrusted into
        // them. Everything is added with TryAdd / HashSet.Add, so it is
        // idempotent and never overwrites a player's edits in canjewelry.json.
        // When Vanilla Armory is not installed SearchItems simply finds nothing,
        // so these entries are harmless. Invoked from FillDefaultValues, i.e. on
        // a fresh config and once per mod-version bump.
        // =====================================================================
        private void AddVanillaArmoryCompat()
        {
            // --- Sockets -----------------------------------------------------
            // Melee families all use the {code}-{type}-{metal} layout, so one
            // wildcard per metal covers every type in a family at once. Socket
            // count scales with the metal, mirroring the vanilla knife curve.
            string[] meleeFamilies = { "blade", "axe", "club", "knife", "spear" };
            foreach (string fam in meleeFamilies)
            {
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-*bronze",      new int[] { 2 });
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-gold",         new int[] { 3 });
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-silver",       new int[] { 3 });
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-iron",         new int[] { 3 });
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-meteoriciron", new int[] { 3, 3 });
                items_codes_with_socket_count_and_tiers.TryAdd($"vanillaarmory:{fam}-*-steel",        new int[] { 3, 3, 3 });
            }
            // Ornate spears carry the ornategold / ornatesilver metals.
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:spear-*-ornate*", new int[] { 3 });
            // Relic weapons (every family, metals relic0..relic5) — end-game, 2 sockets.
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:*-relic*", new int[] { 3, 3 });
            // Compound bow (ranged).
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:bow-*", new int[] { 3, 3 });
            // Shields.
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:buckler-*",      new int[] { 3 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:forlorn-shield", new int[] { 3, 3 });
            // Standalone armor: head/legs = 1 socket, body = 2. Improvised bone /
            // wood armor is weak, so it only gets tier-1 sockets (small gems).
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:sturdyleatherarmor-head", new int[] { 3 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:sturdyleatherarmor-legs", new int[] { 3 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:sturdyleatherarmor-body", new int[] { 3, 3 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:bonearmor-head", new int[] { 1 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:bonearmor-legs", new int[] { 1 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:bonearmor-body", new int[] { 1, 1 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:woodarmor-head", new int[] { 1 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:woodarmor-legs", new int[] { 1 });
            items_codes_with_socket_count_and_tiers.TryAdd("vanillaarmory:woodarmor-body", new int[] { 1, 1 });

            // Gem eligibility for vanillaarmory gear is no longer patched in here: its codes are
            // part of ArmorSets / MeleeWeaponSets / RangedSets / ShieldSets, so FillBuffItemSets
            // hands them to exactly the same gems this block used to. Keeping both would only
            // re-add the codes as literals and stop the "$armor" folding from working.
        }

        private void FillGemTypeToBuff()
        {
            gem_type_to_buff = new Dictionary<string, string>()
            {
                { "diamond", "walkspeed"},
                { "corundum", "miningSpeedMul"},
                { "emerald", "maxhealthExtraPoints"},
                { "fluorite", "meleeWeaponsDamage"},
                { "lapislazuli", "hungerrate" },
                { "malachite", "wildCropDropRate" },
                { "olivine_peridot", "armorDurabilityLoss"},
                { "olivine", "armorDurabilityLoss"},
                { "quartz", "oreDropRate"},
                { "uranium", "healingeffectivness"},
                { "ruby", "rangedWeaponsDamage"},
                { "citrine", "animalLootDropRate"},
                { "berylaquamarine",  "walkspeed"},
                { "berylbixbite",  "miningSpeedMul"},
                { "corundumruby",  "rangedWeaponsDamage"},
                { "corundumsapphire",  "vesselContentsDropRate"},
                { "garnetalmandine",  "bowDrawingStrength"},
                { "garnetandradite",  "animalSeekingRange"},
                { "garnetgrossular",  "vesselContentsDropRate"},
                { "garnetpyrope",  "animalLootDropRate"},
                { "garnetspessartine",  "meleeWeaponsDamage"},
                { "garnetuvarovite",  "rangedWeaponsSpeed"},
                { "spinelred",  "armorWalkSpeedAffectedness"},
                { "topazamber",  "meleeWeaponsDamage"},
                { "topazblue",  "wildCropDropRate"},
                { "topazpink",  "animalHarvestingTime"},
                { "tourmalinerubellite",  "wildCropDropRate"},
                { "tourmalineschorl",  "mechanicalsDamage"},
                { "tourmalineverdelite",  "armorDurabilityLoss"},
                { "tourmalinewatermelon",  "rangedWeaponsAcc"},
                { "amethyst", "candurability" },
                { "topaz", "temporalgrasp" },
                { "pearl", "maxhealthExtraPoints" }
            };
        }

        private void FillDropTable()
        {
            gems_drops_table = new Dictionary<string, DropInfo[]>()
            {
                 //malachite
                { "ore-*-malachite-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-malachite", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-malachite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-malachite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },            
                 //fluorite
                { "ore-*-pentlandite-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-fluorite", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-fluorite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-fluorite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                //corundum
                { "ore-*-rhodochrosite-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-corundum", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-corundum", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-corundum", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                //quartz
                { "ore-*-quartz-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-quartz", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-quartz", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-quartz", 0.0005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                //rocks
                { "rock-limestone", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-malachite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-malachite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-malachite", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-amethyst", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-amethyst", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-amethyst", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-granite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-quartz", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-quartz", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-quartz", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                    new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-ruby", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-ruby", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-ruby", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-citrine", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-citrine", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-citrine", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-whitemarble", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-malachite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-malachite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-malachite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-chalk", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-malachite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-malachite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-malachite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-greenmarble", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-malachite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-malachite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-malachite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-kimberlite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-diamond", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-diamond", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-diamond", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-ruby", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-ruby", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-ruby", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-suevite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-diamond", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-diamond", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-diamond", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-phyllite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-corundum", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-corundum", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-corundum", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                    new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-amethyst", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-amethyst", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-amethyst", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-shale", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-emerald", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-emerald", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-emerald", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-slate", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-fluorite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-fluorite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-fluorite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-claystone", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-quartz", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-quartz", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-quartz", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-topaz", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-topaz", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-topaz", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-andesite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-uranium", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-uranium", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-uranium", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-sandstone", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-olivine", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-olivine", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-olivine", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-conglomerate", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-fluorite", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-fluorite", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-fluorite", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-chert", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-uranium", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-uranium", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-uranium", 0.00005f, 0, true, "canjewelrygemsdroprate"),
                    new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-citrine", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-citrine", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-citrine", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-basalt", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-quartz", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-quartz", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-quartz", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                { "rock-peridotite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-olivine", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-olivine", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-olivine", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
                 { "rock-bauxite", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-lapislazuli", 0.001f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-lapislazuli", 0.0005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-lapislazuli", 0.00005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
               { "ore-lapislazuli-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-lapislazuli", 0.01f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-lapislazuli", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-lapislazuli", 0.0005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
               { "ore-quartz-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-quartz", 0.01f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-quartz", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-quartz", 0.0005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
               { "ore-fluorite-*", new DropInfo[]{
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-chipped-fluorite", 0.01f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-flawed-fluorite", 0.005f, 0, true, "canjewelrygemsdroprate"),
                   new DropInfo(EnumItemClass.Item, "canjewelry:gem-rough-normal-fluorite", 0.0005f, 0, true, "canjewelrygemsdroprate")
                   }
                },
            };
        }

        private void FillCustomVariantSockets()
        {
            if (custom_variants_sockets_tiers.Count == 0)
            {
                custom_variants_sockets_tiers = BuildDefaultCustomVariantSockets();
            }
        }

        private static HashSet<CustomVariantSocketsTiers> BuildDefaultCustomVariantSockets()
        {
            var custom_variants_sockets_tiers = new HashSet<CustomVariantSocketsTiers>();

            // Content mods contribute their own variant tables. Matched by ItemCode so a
            // registered entry replaces the core's for that item rather than sitting beside it —
            // the set is keyed by object identity and would otherwise hold both.
            foreach (var registered in CANJewelryRegistry.ExtraVariantSockets)
            {
                custom_variants_sockets_tiers.RemoveWhere(existing => existing.ItemCode == registered.ItemCode);
                custom_variants_sockets_tiers.Add(registered);
            }

            return custom_variants_sockets_tiers;
        }

        /// <summary>
        /// Counts socket entries that later mod versions introduced and this config does not have,
        /// optionally adding them. Only missing keys are ever added: values that were edited, and
        /// whole entries an admin rewrote, are left alone. Off by default - a missing entry can
        /// just as well be one the admin deliberately deleted.
        /// </summary>
        public int AddMissingDefaults(bool apply)
        {
            int missing = 0;

            foreach (var defaultEntry in BuildDefaultSocketCounts())
            {
                if (items_codes_with_socket_count_and_tiers.ContainsKey(defaultEntry.Key)) continue;
                missing++;
                if (apply) items_codes_with_socket_count_and_tiers[defaultEntry.Key] = defaultEntry.Value;
            }

            foreach (var defaultVariant in BuildDefaultCustomVariantSockets())
            {
                var existing = custom_variants_sockets_tiers.FirstOrDefault(v => v.ItemCode == defaultVariant.ItemCode);
                if (existing == null)
                {
                    missing++;
                    if (apply) custom_variants_sockets_tiers.Add(defaultVariant);
                    continue;
                }

                if (existing.SocketTiers == null)
                {
                    missing++;
                    if (apply) existing.SocketTiers = defaultVariant.SocketTiers;
                    continue;
                }

                foreach (var tier in defaultVariant.SocketTiers)
                {
                    if (existing.SocketTiers.ContainsKey(tier.Key)) continue;
                    missing++;
                    if (apply) existing.SocketTiers[tier.Key] = tier.Value;
                }
            }

            return missing;
        }

        private void FillPossibleGemBuffs()
        {
            PossibleGemBuffs = new Dictionary<string, HashSet<string>> {
                { "diamond", new HashSet<string>{ "walkspeed" } },
                { "corundum", new HashSet<string>{ "miningSpeedMul" } },
                { "emerald", new HashSet<string>{ "maxhealthExtraPoints" } },
                { "fluorite", new HashSet<string>{ "meleeWeaponsDamage" } },
                { "lapislazuli", new HashSet<string>{ "hungerrate" } },
                { "malachite", new HashSet<string>{ "wildCropDropRate" } },
                { "olivine", new HashSet<string>{ "armorDurabilityLoss" } },
                { "quartz", new HashSet<string>{ "oreDropRate" } },
                { "uranium", new HashSet<string>{ "healingeffectivness" } },
                { "ruby", new HashSet<string>{ "rangedWeaponsDamage" } },
                { "citrine", new HashSet<string>{ "animalLootDropRate" } },
                 { "berylaquamarine", new HashSet<string>{ "walkspeed" } },
                 { "berylbixbite", new HashSet<string>{ "miningSpeedMul" } },
                 { "corundumruby", new HashSet<string>{ "rangedWeaponsDamage" } },
                { "corundumsapphire", new HashSet<string>{ "vesselContentsDropRate" } },
                { "garnetalmandine", new HashSet<string>{ "bowDrawingStrength" } },
                { "garnetandradite", new HashSet<string>{ "animalSeekingRange" } },
                { "garnetgrossular", new HashSet<string>{ "vesselContentsDropRate" } },
                { "garnetpyrope", new HashSet<string>{ "animalLootDropRate" } },
                { "garnetspessartine", new HashSet<string>{ "meleeWeaponsDamage" } },
                { "topazamber", new HashSet<string>{ "meleeWeaponsDamage" } },
                { "topazblue", new HashSet<string>{ "wildCropDropRate" } },
                { "tourmalinerubellite", new HashSet<string>{ "wildCropDropRate" } },
                { "tourmalineverdelite", new HashSet<string>{ "armorDurabilityLoss" } },
                { "garnetuvarovite", new HashSet<string>{ "rangedWeaponsSpeed" } },
                { "spinelred", new HashSet<string>{ "armorWalkSpeedAffectedness" } },
                { "topazpink", new HashSet<string>{ "animalHarvestingTime" } },
                { "tourmalineschorl", new HashSet<string>{ "mechanicalsDamage" } },
                { "tourmalinewatermelon", new HashSet<string>{ "rangedWeaponsAcc" } },
                { "amethyst", new HashSet<string>{ "candurability" } },
                { "topaz", new HashSet<string>{ "temporalgrasp" } },
                { "pearl", new HashSet<string>{ "maxhealthExtraPoints" } }
            };
        }

        private void FillBuffAttributes()
        {
            BuffAttributesDict = new Dictionary<string, BuffAttributes>
            {
                {"walkspeed",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.03f } },
                        { 2, new float[]{ 0.04f, 0.06f } },
                        { 3, new float[]{ 0.07f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.04f, 0.04f } }
                    },
                    new HashSet<string>{ "miningSpeedMul" })},

                {"miningSpeedMul",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.03f } },
                        { 2, new float[]{ 0.04f, 0.06f } },
                        { 3, new float[]{ 0.07f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.03f } },
                        { 3, new float[]{ 0.04f, 0.05f } }
                    },
                    new HashSet<string>{ "walkspeed" })},
                 {"maxhealthExtraPoints",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 1f, 1.5f } },
                        { 2, new float[]{ 2f, 2.5f } },
                        { 3, new float[]{ 4f, 4.5f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.3f, 0.5f } },
                        { 2, new float[]{ 0.6f, 0.8f } },
                        { 3, new float[]{ 1f, 1.2f } }
                    },
                    new HashSet<string>{ "hungerrate", "animalLootDropRate" })},
                 {"meleeWeaponsDamage",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.03f } },
                        { 2, new float[]{ 0.04f, 0.05f } },
                        { 3, new float[]{ 0.06f, 0.08f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.03f } },
                        { 3, new float[]{ 0.04f, 0.05f } }
                    },
                    new HashSet<string>{ "armorDurabilityLoss", "hungerrate" })},
                 {"hungerrate",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.03f } },
                        { 2, new float[]{ -0.04f, -0.06f } },
                        { 3, new float[]{ -0.07f, -0.1f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.01f } },
                        { 2, new float[]{ -0.02f, -0.03f } },
                        { 3, new float[]{ -0.04f, -0.05f } }
                    },
                    new HashSet<string>{ "meleeWeaponsDamage",  "walkspeed"})},
                 {"wildCropDropRate",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.05f } },
                        { 3, new float[]{ 0.06f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.03f } },
                        { 3, new float[]{ 0.04f, 0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "animalSeekingRange" })},
                 {"armorDurabilityLoss",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.05f } },
                        { 2, new float[]{ -0.06f, -0.1f } },
                        { 3, new float[]{ -0.11f, -0.15f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.01f } },
                        { 2, new float[]{ -0.02f, -0.03f } },
                        { 3, new float[]{ -0.04f, -0.05f } }
                    },
                    new HashSet<string>{ "healingeffectivness", "armorWalkSpeedAffectedness" })},
                 {"oreDropRate",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.03f } },
                        { 2, new float[]{ 0.04f, 0.07f } },
                        { 3, new float[]{ 0.08f, 0.11f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.03f } },
                        { 3, new float[]{ 0.04f, 0.05f } }
                    },
                    new HashSet<string>{ "candurability" })},
                 {"healingeffectivness",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.05f } },
                        { 2, new float[]{ 0.06f, 0.1f } },
                        { 3, new float[]{ 0.11f, 0.13f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.03f } },
                        { 3, new float[]{ 0.04f, 0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "animalHarvestingTime" })},
                 {"rangedWeaponsDamage",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "rangedWeaponsAcc", "hungerrate" })}
                ,
                 {"animalLootDropRate",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "animalHarvestingTime", "hungerrate" })},
                 {"vesselContentsDropRate",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "maxhealthExtraPoints" })},
                 {"animalSeekingRange",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.02f } },
                        { 2, new float[]{ -0.03f, -0.04f } },
                        { 3, new float[]{ -0.05f, -0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.01f } },
                        { 2, new float[]{ -0.02f, -0.02f } },
                        { 3, new float[]{ -0.03f, -0.05f } }
                    },
                    new HashSet<string>{ "hungerrate", "meleeWeaponsDamage" })},
                 {"rangedWeaponsSpeed",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.08f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.04f } }
                    },
                    new HashSet<string>{ "armorWalkSpeedAffectedness", "bowDrawingStrength" })},
                 {"animalHarvestingTime",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.02f } },
                        { 2, new float[]{ -0.03f, -0.04f } },
                        { 3, new float[]{ -0.05f, -0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.01f } },
                        { 2, new float[]{ -0.02f, -0.02f } },
                        { 3, new float[]{ -0.03f, -0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "mechanicalsDamage" })},
                 {"mechanicalsDamage",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.08f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "maxhealthExtraPoints", "oreDropRate" })},
                 {"rangedWeaponsAcc",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.07f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "mechanicalsDamage" })},
                 {"armorWalkSpeedAffectedness",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.02f } },
                        { 2, new float[]{ -0.03f, -0.04f } },
                        { 3, new float[]{ -0.05f, -0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ -0.01f, -0.01f } },
                        { 2, new float[]{ -0.02f, -0.02f } },
                        { 3, new float[]{ -0.03f, -0.05f } }
                    },
                    new HashSet<string>{ "walkspeed", "hungerrate" })},
                 {"bowDrawingStrength",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.04f } },
                        { 3, new float[]{ 0.05f, 0.09f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.01f } },
                        { 2, new float[]{ 0.02f, 0.02f } },
                        { 3, new float[]{ 0.03f, 0.05f } }
                    },
                    new HashSet<string>{ "candurability", "armorDurabilityLoss" })}
                ,
                 {"candurability",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.2f } },
                        { 2, new float[]{ 0.29f, 0.45f } },
                        { 3, new float[]{ 0.7f, 0.95f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.09f } },
                        { 3, new float[]{ 0.1f, 0.15f } }
                    },
                    new HashSet<string>{ "meleeWeaponsDamage", "miningSpeedMul" })},
                  {"temporalgrasp",
                    new BuffAttributes(
                       new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.2f, 0.33f } },
                        { 2, new float[]{ 0.35f, 0.69f } },
                        { 3, new float[]{ 0.89f, 1f } }
                    }, new Dictionary<int, float[]>
                    {
                        { 1, new float[]{ 0.01f, 0.02f } },
                        { 2, new float[]{ 0.03f, 0.09f } },
                        { 3, new float[]{ 0.1f, 0.15f } }
                    },
                    new HashSet<string>{ "walkspeed", "miningSpeedMul" })},
            };

            CuttingAttributesDict = new Dictionary<string, CuttingAttributes>
            {
                { "round",    new CuttingAttributes(new float[] { 1, 1.33f, 1.65f }) },
                { "baguette", new CuttingAttributes(new float[] { 1, 1.33f, 1.5f }) },
                { "pear",     new CuttingAttributes(new float[] { 1.7f, 1.05f, 1.05f }) },
            };

            if (socketTiersColorsWords.Length == 0) socketTiersColorsWords = new string[] { "green", "blue", "purple" };
            if (socketTiersColors.Length == 0)      socketTiersColors      = new string[] { "2FE147", "2B3FF7", "9214C9" };

            if (TemporalGraspBlockList.Count == 0)
                TemporalGraspBlockList = new HashSet<string> { "game:soil-*", "game:rawclay-blue-*", "game:forestfloor-*",
                    "game:stalagsection-*", "game:ore-bountiful-*", "game:ore-rich-*", "game:ore-poor-*",
                    "game:ore-medium-*", "game:peat-*", "game:lakeice" };

            if (LevelOfSocketByType.Count == 0) LevelOfSocketByType = new Dictionary<string, int>
            {
                { "canjewelry:cansocket-gold",         2 },
                { "canjewelry:cansocket-silver",       2 },
                { "canjewelry:cansocket-bismuthbronze",1 },
                { "canjewelry:cansocket-tinbronze",    1 },
                { "canjewelry:cansocket-blackbronze",  1 },
                { "canjewelry:cansocket-iron",         2 },
                { "canjewelry:cansocket-meteoriciron", 2 },
                { "canjewelry:cansocket-steel",        3 },
            };

            if (panningDrops == null || panningDrops.Count == 0)
            {
                utils.CANPanningDrop D(string code, float chance) => new utils.CANPanningDrop
                {
                    Code = new AssetLocation(code),
                    Type = EnumItemClass.Item,
                    StackSize = 1,
                    Chance = new NatFloat(chance, 0f, EnumDistribution.UNIFORM)
                };
                utils.CANPanningDrop[] OreDrops(string gem, float n, float fl, float ch) => new[]
                {
                    D("canjewelry:gem-rough-normal-"  + gem, n),
                    D("canjewelry:gem-rough-flawed-"  + gem, fl),
                    D("canjewelry:gem-rough-chipped-" + gem, ch),
                };
                panningDrops = new Dictionary<string, utils.CANPanningDrop[]>
                {
                    { "game:stone-suevite",                                    OreDrops("diamond",    0.2f, 0.3f, 0.5f) },

                    { "@(ore|crystalizedore)-bountiful-hematite-.*",         OreDrops("corundum",   0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-hematite-.*",              OreDrops("corundum",   0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-hematite-.*",            new[] { D("canjewelry:gem-rough-flawed-corundum",  0.4f), D("canjewelry:gem-rough-chipped-corundum",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-hematite-.*",              new[] { D("canjewelry:gem-rough-chipped-corundum", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-malachite-.*",        OreDrops("malachite",  0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-malachite-.*",             OreDrops("malachite",  0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-malachite-.*",           new[] { D("canjewelry:gem-rough-flawed-malachite",  0.4f), D("canjewelry:gem-rough-chipped-malachite",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-malachite-.*",             new[] { D("canjewelry:gem-rough-chipped-malachite", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-bismuthinite-.*",     OreDrops("lapislazuli", 0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-bismuthinite-.*",          OreDrops("lapislazuli", 0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-bismuthinite-.*",        new[] { D("canjewelry:gem-rough-flawed-lapislazuli",  0.4f), D("canjewelry:gem-rough-chipped-lapislazuli",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-bismuthinite-.*",          new[] { D("canjewelry:gem-rough-chipped-lapislazuli", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-cassiterite-.*",      OreDrops("olivine",    0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-cassiterite-.*",           OreDrops("olivine",    0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-cassiterite-.*",         new[] { D("canjewelry:gem-rough-flawed-olivine",  0.4f), D("canjewelry:gem-rough-chipped-olivine",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-cassiterite-.*",           new[] { D("canjewelry:gem-rough-chipped-olivine", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-sphalerite-.*",       OreDrops("fluorite",   0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-sphalerite-.*",            OreDrops("fluorite",   0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-sphalerite-.*",          new[] { D("canjewelry:gem-rough-flawed-fluorite",  0.4f), D("canjewelry:gem-rough-chipped-fluorite",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-sphalerite-.*",            new[] { D("canjewelry:gem-rough-chipped-fluorite", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-quartz_nativesilver-.*", OreDrops("quartz",  0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-quartz_nativesilver-.*",      OreDrops("quartz",  0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-quartz_nativesilver-.*",    new[] { D("canjewelry:gem-rough-flawed-quartz",  0.4f), D("canjewelry:gem-rough-chipped-quartz",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-quartz_nativesilver-.*",      new[] { D("canjewelry:gem-rough-chipped-quartz", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-quartz_nativegold-.*",   OreDrops("quartz",  0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-quartz_nativegold-.*",        OreDrops("quartz",  0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-quartz_nativegold-.*",      new[] { D("canjewelry:gem-rough-flawed-quartz",  0.4f), D("canjewelry:gem-rough-chipped-quartz",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-quartz_nativegold-.*",        new[] { D("canjewelry:gem-rough-chipped-quartz", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-limonite-.*",         OreDrops("uranium",    0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-limonite-.*",              OreDrops("uranium",    0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-limonite-.*",            new[] { D("canjewelry:gem-rough-flawed-uranium",  0.4f), D("canjewelry:gem-rough-chipped-uranium",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-limonite-.*",              new[] { D("canjewelry:gem-rough-chipped-uranium", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-ilmenite-.*",         new[] { D("canjewelry:gem-rough-normal-diamond",  0.4f), D("canjewelry:gem-rough-flawed-diamond",  0.7f), D("canjewelry:gem-rough-chipped-diamond",  0.8f), D("canjewelry:gem-rough-normal-emerald",  0.4f), D("canjewelry:gem-rough-flawed-emerald",  0.7f), D("canjewelry:gem-rough-chipped-emerald",  0.8f) } },
                    { "@(ore|crystalizedore)-rich-ilmenite-.*",              new[] { D("canjewelry:gem-rough-normal-diamond",  0.2f), D("canjewelry:gem-rough-flawed-diamond",  0.5f), D("canjewelry:gem-rough-chipped-diamond",  0.6f), D("canjewelry:gem-rough-normal-emerald",  0.2f), D("canjewelry:gem-rough-flawed-emerald",  0.5f), D("canjewelry:gem-rough-chipped-emerald",  0.6f) } },
                    { "@(ore|crystalizedore)-medium-ilmenite-.*",            new[] { D("canjewelry:gem-rough-flawed-diamond",  0.4f), D("canjewelry:gem-rough-chipped-diamond",  0.5f), D("canjewelry:gem-rough-flawed-emerald",  0.4f), D("canjewelry:gem-rough-chipped-emerald",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-ilmenite-.*",              new[] { D("canjewelry:gem-rough-chipped-diamond", 0.4f), D("canjewelry:gem-rough-chipped-emerald", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-nativecopper-.*",     OreDrops("ruby",       0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-nativecopper-.*",          OreDrops("ruby",       0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-nativecopper-.*",        new[] { D("canjewelry:gem-rough-flawed-ruby",  0.4f), D("canjewelry:gem-rough-chipped-ruby",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-nativecopper-.*",          new[] { D("canjewelry:gem-rough-chipped-ruby", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-magnetite-.*",        OreDrops("citrine",    0.4f, 0.7f, 0.8f) },
                    { "@(ore|crystalizedore)-rich-magnetite-.*",             OreDrops("citrine",    0.2f, 0.5f, 0.6f) },
                    { "@(ore|crystalizedore)-medium-magnetite-.*",           new[] { D("canjewelry:gem-rough-flawed-citrine",  0.4f), D("canjewelry:gem-rough-chipped-citrine",  0.5f) } },
                    { "@(ore|crystalizedore)-poor-magnetite-.*",             new[] { D("canjewelry:gem-rough-chipped-citrine", 0.4f) } },

                    { "@(ore|crystalizedore)-bountiful-uranium-.*",          OreDrops("uranium",    0.5f, 0.75f, 0.9f) },
                    { "@(ore|crystalizedore)-rich-uranium-.*",               OreDrops("uranium",    0.3f, 0.6f,  0.7f) },
                    { "@(ore|crystalizedore)-medium-uranium-.*",             new[] { D("canjewelry:gem-rough-flawed-uranium",  0.5f), D("canjewelry:gem-rough-chipped-uranium",  0.6f) } },
                    { "@(ore|crystalizedore)-poor-uranium-.*",               new[] { D("canjewelry:gem-rough-chipped-uranium", 0.6f) } },

                    { "@(ore|crystalizedore)-bountiful-galena-.*",           OreDrops("amethyst",   0.5f, 0.75f, 0.9f) },
                    { "@(ore|crystalizedore)-rich-galena-.*",                OreDrops("amethyst",   0.3f, 0.6f,  0.7f) },
                    { "@(ore|crystalizedore)-medium-galena-.*",              new[] { D("canjewelry:gem-rough-flawed-amethyst",  0.5f), D("canjewelry:gem-rough-chipped-amethyst",  0.6f) } },
                    { "@(ore|crystalizedore)-poor-galena-.*",                new[] { D("canjewelry:gem-rough-chipped-amethyst", 0.6f) } },
                };
            }
        }
        [JsonConverter(typeof(utils.DropInfoConverter))]
        public class DropInfo
        {
            public EnumItemClass TypeCollectable;
            public string NameCollectable;
            public float avg;
            public float var;
            public bool LastDrop;
            public string DropModbyStat;
            public string attributes;

            public DropInfo(EnumItemClass TypeCollectable, string NameCollectable, float avg, float var, bool LastDrop, string DropModbyStat = "", string attributes = "")
            {
                this.TypeCollectable = TypeCollectable;
                this.NameCollectable = NameCollectable;
                this.avg = avg;
                this.var = var;
                this.LastDrop = LastDrop;
                this.DropModbyStat = DropModbyStat;
                this.attributes = attributes;
            }
        }
        public class CustomVariantSocketsTiers
        {
            public string ItemCode;
            public string AttributeKey;
            [JsonProperty(ItemConverterType = typeof(utils.CompactIntArrayConverter))]
            public Dictionary<string, int[]> SocketTiers;

            // Newtonsoft binds a parameterized constructor by parameter name and then ignores the
            // field attributes, so the compact "1,2,1" form would fail to read. With a default
            // constructor present it fills the fields directly and the converters apply.
            [JsonConstructor]
            public CustomVariantSocketsTiers() { }

            public CustomVariantSocketsTiers(string itemCode, string attributeKey, Dictionary<string, int[]> socketTiers)
            {
                this.ItemCode = itemCode;
                this.AttributeKey = attributeKey;
                this.SocketTiers = socketTiers;
            }
        }
        public class BuffAttributes
        {
            [JsonProperty(ItemConverterType = typeof(utils.CompactFloatArrayConverter))]
            public Dictionary<int, float[]> MainStatValueRange;
            [JsonProperty(ItemConverterType = typeof(utils.CompactFloatArrayConverter))]
            public Dictionary<int, float[]> SecondaryStatValueRange;
            [JsonConverter(typeof(utils.CompactStringSetConverter))]
            public HashSet<string> PossibleSecondaryStats;

            // See CustomVariantSocketsTiers: without a default constructor the field converters
            // are skipped on read and the compact "0.01,0.03" ranges fail to parse.
            [JsonConstructor]
            public BuffAttributes() { }

            public BuffAttributes(Dictionary<int, float[]> mainStatValueRange, Dictionary<int, float[]> secondaryStatValueRange, HashSet<string> possibleSecondaryStats)
            {
                MainStatValueRange = mainStatValueRange;
                SecondaryStatValueRange = secondaryStatValueRange;
                PossibleSecondaryStats = possibleSecondaryStats;
            }
            public float GetRandomMainValue(int tier)
            {
                if(this.MainStatValueRange == null || !this.MainStatValueRange.TryGetValue(tier, out float[] valueDict))
                {
                    return 0f;
                }
                if(valueDict.Count() == 1)
                {
                    return valueDict[0];
                }
                else
                {
                    return (float)(Config.rand.NextDouble() * (valueDict[1] - valueDict[0]) + valueDict[0]);
                }
            }
            public float GetRandomSecondaryValue(int tier)
            {
                if (this.SecondaryStatValueRange == null || !this.SecondaryStatValueRange.TryGetValue(tier, out float[] valueDict))
                {
                    return 0f;
                }
                if (valueDict.Count() == 1)
                {
                    return valueDict[0];
                }
                else
                {
                    return (float)(Config.rand.NextDouble() * (valueDict[1] - valueDict[0]) + valueDict[0]);
                }
            }
        }
        public class CuttingAttributes
        {
            [JsonConverter(typeof(utils.CompactFloatArrayConverter))]
            public float[] GrindingBuffIncreaseMultipliers;

            // See CustomVariantSocketsTiers: needed so the field converter applies on read.
            [JsonConstructor]
            public CuttingAttributes() { }

            public CuttingAttributes(float[] grindingBuffIncreaseMultipliers)
            {
                GrindingBuffIncreaseMultipliers = grindingBuffIncreaseMultipliers;
            }
        }
        public void InitColors()
        {
            List<string> tmpList = new List<string>();
            foreach (var it in socketTiersColorsWords)
            {
                utils.CommonFunctions.tryFindColor(it, out var colorInt);
                if (colorInt != 0)
                {
                    tmpList.Add(colorInt.ToString());
                }
            }
            socketTiersColors = tmpList.ToArray();
        }
    }
}
