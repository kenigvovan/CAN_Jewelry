using canjewelry.src;
using canjewelry.Tests.Stubs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace canjewelry.Tests.Helpers
{
    /// <summary>
    /// Factory helpers for creating ItemStack instances in tests.
    /// Centralises all the boilerplate so individual test files stay readable.
    /// </summary>
    public static class ItemStackBuilder
    {
        // ── Socketable items ────────────────────────────────────────────────

        /// <summary>
        /// Item that can receive sockets. tiers.Length == max sockets, values == required socket tier.
        /// E.g. new[]{2,3} → 2 sockets, first requires tier-2 socket, second requires tier-3.
        /// </summary>
        public static ItemStack CreateSocketableItem(int[] tiers)
        {
            var item = new TestItem();
            item.Attributes = new JsonObject(JToken.Parse($@"{{
                ""{CANJWConstants.SOCKETS_NUMBER_STRING}"": {tiers.Length},
                ""{CANJWConstants.SOCKETS_TIERS_STRING}"": {JsonConvert.SerializeObject(tiers)}
            }}"));
            item.Code = new AssetLocation("canjewelry:sword-iron");
            return new ItemStack(item);
        }

        /// <summary>
        /// Socket item (e.g. cansocket-iron) with the given tier level.
        /// </summary>
        public static ItemStack CreateSocketItem(int level)
        {
            var item = new TestItem();
            item.Attributes = new JsonObject(JToken.Parse($@"{{
                ""{CANJWConstants.LEVEL_OF_SOSCKET_STRING}"": {level}
            }}"));
            item.Code = new AssetLocation("canjewelry:cansocket-iron");
            return new ItemStack(item);
        }

        /// <summary>
        /// Adds the canencrusted tree entry for a single socket on an existing stack.
        /// Optionally pre-fills that socket with buff data (simulates an already-encrusted gem).
        /// </summary>
        public static void AddSocket(ItemStack stack, int socketNumber, int socketLevel,
            string[]? buffNames = null, float[]? buffValues = null)
        {
            ITreeAttribute encrustedTree;
            if (!stack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
            {
                encrustedTree = new TreeAttribute();
                encrustedTree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, 0);
                stack.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = encrustedTree;
            }
            else
            {
                encrustedTree = stack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            }

            ITreeAttribute socketTree = new TreeAttribute();
            socketTree.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, socketLevel);
            socketTree.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
            socketTree.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");

            if (buffNames != null && buffValues != null)
            {
                socketTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(buffNames);
                socketTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(buffValues);
            }

            encrustedTree["slot" + socketNumber] = socketTree;
            encrustedTree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER,
                encrustedTree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) + 1);
        }

        // ── Cut gems ────────────────────────────────────────────────────────

        /// <summary>
        /// Cut gem ready to be placed in a socket. Already has buff data inside CUT_GEM_TREE.
        /// </summary>
        public static ItemStack CreateCutGem(string gemType, int gemTier, string cuttingType,
            string[] buffNames, float[] buffValues)
        {
            var stack = CreateUnprocessedCutGem(gemType, gemTier, cuttingType);
            var tree = stack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(buffNames);
            tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(buffValues);
            return stack;
        }

        /// <summary>
        /// Cut gem as it exists right after cutting — only has CUT_GEM_TREE with cuttingtype set.
        /// ApplyCuttingBuff will compute and write buff data into it.
        /// </summary>
        public static ItemStack CreateUnprocessedCutGem(string gemType, int gemTier, string cuttingType)
        {
            var item = new TestItem();
            item.Attributes = new JsonObject(JToken.Parse($@"{{""canGemType"": {gemTier}}}"));
            item.Variant = new Vintagestory.API.Util.RelaxedReadOnlyDictionary<string, string>(
                new Dictionary<string, string> { { "gemtype", gemType } });
            item.Code = new AssetLocation($"canjewelry:gem-cut-{gemType}");

            var stack = new ItemStack(item);
            ITreeAttribute cutGemTree = new TreeAttribute();
            cutGemTree.SetString(CANJWConstants.CUTTING_TYPE, cuttingType);
            stack.Attributes[CANJWConstants.CUT_GEM_TREE] = cutGemTree;
            return stack;
        }

        // ── Config ──────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal Config for ApplyCuttingBuff tests.
        /// Uses single-value stat ranges so GetRandomMainValue / GetRandomSecondaryValue
        /// are deterministic (always return the one configured value).
        /// </summary>
        public static Config BuildMinimalConfig(
            string gemType = "diamond",
            int gemTier = 1,
            string mainBuff = "canspeed",
            string secondaryBuff = "candamage")
        {
            var secondaryStats = string.IsNullOrEmpty(secondaryBuff)
                ? new HashSet<string>()
                : new HashSet<string> { secondaryBuff };

            var buffs = new Dictionary<string, Config.BuffAttributes>
            {
                {
                    mainBuff, new Config.BuffAttributes(
                        mainStatValueRange: new Dictionary<int, float[]>
                            { { gemTier, new[] { 0.15f } } },          // deterministic: always 0.15
                        secondaryStatValueRange: null,
                        possibleSecondaryStats: secondaryStats
                    )
                }
            };
            // A baguette's secondary value comes from the secondary stat's own entry, in its units.
            if (!string.IsNullOrEmpty(secondaryBuff))
            {
                buffs[secondaryBuff] = new Config.BuffAttributes(
                    mainStatValueRange: null,
                    secondaryStatValueRange: new Dictionary<int, float[]>
                        { { gemTier, new[] { 0.05f } } },              // deterministic: always 0.05
                    possibleSecondaryStats: null
                );
            }

            return new Config
            {
                PossibleGemBuffs = new Dictionary<string, HashSet<string>>
                {
                    { gemType, new HashSet<string> { mainBuff } }
                },
                BuffAttributesDict = buffs,
                CuttingAttributesDict = new Dictionary<string, Config.CuttingAttributes>
                {
                    { "round",    new Config.CuttingAttributes(new[] { 1.0f }) },
                    { "baguette", new Config.CuttingAttributes(new[] { 1.0f }) },
                    { "pear",     new Config.CuttingAttributes(new[] { 1.0f }) },
                }
            };
        }
    }
}
