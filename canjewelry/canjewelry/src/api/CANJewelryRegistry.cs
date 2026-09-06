using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace canjewelry.src.api
{
    /// <summary>
    /// Registry of config defaults contributed by content mods.
    ///
    /// Why this exists: the core builds its default config in StartPre, synchronously and before
    /// assets are loaded (canjewelry.StartPre -> loadConfig -> AddMissingDefaults). Socket defaults
    /// for a content mod's jewelry therefore cannot arrive through assets, and there is no phase to
    /// squeeze between those calls. A content mod fills this registry from its own StartPre while
    /// running at a lower ExecuteOrder, and the core's builders merge it in.
    ///
    /// Registering after the core has read the registry is not an error: it logs a warning and is
    /// still recorded, because AddMissingDefaults re-runs the builders on every start and will pick
    /// it up next launch. Degradation is soft by design.
    /// </summary>
    public static class CANJewelryRegistry
    {
        private static readonly Dictionary<string, int[]> extraSocketCounts = new Dictionary<string, int[]>();
        private static readonly Dictionary<string, Config.CustomVariantSocketsTiers> extraVariantSockets
            = new Dictionary<string, Config.CustomVariantSocketsTiers>();
        private static readonly Dictionary<string, HashSet<string>> extraItemGroupMembers
            = new Dictionary<string, HashSet<string>>();
        private static readonly List<string> materialAttributeKeys = new List<string>();

        /// <summary>
        /// Set once the core has consumed the registry; only controls warnings. Cleared by the
        /// core's constructor at the start of every mod-load run: the registry is static while the
        /// Pre phase runs once per side in a local game, so without the reset the second run's
        /// replayed registrations would all warn spuriously.
        /// </summary>
        internal static bool Sealed { get; set; }

        /// <summary>Set by the core so late registrations can be reported. May be null.</summary>
        internal static ILogger Logger { get; set; }

        internal static IReadOnlyDictionary<string, int[]> ExtraSocketCounts => extraSocketCounts;
        internal static IReadOnlyCollection<Config.CustomVariantSocketsTiers> ExtraVariantSockets
            => extraVariantSockets.Values;
        internal static IReadOnlyDictionary<string, HashSet<string>> ExtraItemGroupMembers => extraItemGroupMembers;

        /// <summary>
        /// Material attribute keys, in priority order: the core's base set first, then whatever
        /// content mods added. Read by CANItemWearable when naming an item.
        /// </summary>
        public static IReadOnlyList<string> MaterialAttributeKeys => materialAttributeKeys;

        /// <summary>
        /// Default socket layout for items matching a wildcard code, e.g.
        /// <c>RegisterDefaultSockets("canjewelry:canring-*", 1)</c>.
        /// Re-registering the same wildcard overwrites; it never duplicates.
        /// </summary>
        public static void RegisterDefaultSockets(string itemCodeWildcard, params int[] socketTiers)
        {
            if (string.IsNullOrEmpty(itemCodeWildcard) || socketTiers == null) return;
            if (Sealed) WarnIfSealed(nameof(RegisterDefaultSockets), itemCodeWildcard, Assembly.GetCallingAssembly());
            extraSocketCounts[itemCodeWildcard] = (int[])socketTiers.Clone();
        }

        /// <summary>
        /// Default socket layout that varies by the value of one key — the case of a tiara whose
        /// socket count depends on its "carcassus" metal.
        /// Keyed by item code, so re-registering the same item replaces the whole entry.
        /// <para>
        /// <paramref name="variantKey"/> is looked up on the stack attributes first and then among
        /// the item's own code variants, so it works for both ways an adornment can carry its
        /// material: "carcassus" on a tiara's stack, or the "loop" variant group of a coronet whose
        /// metal lives in its code (cancoronet-gold).
        /// </para>
        /// </summary>
        public static void RegisterDefaultVariantSockets(string itemCode, string variantKey,
                                                         IDictionary<string, int[]> tiersByVariantValue)
        {
            if (string.IsNullOrEmpty(itemCode) || string.IsNullOrEmpty(variantKey)) return;
            if (tiersByVariantValue == null || tiersByVariantValue.Count == 0) return;
            if (Sealed) WarnIfSealed(nameof(RegisterDefaultVariantSockets), itemCode, Assembly.GetCallingAssembly());

            var copy = new Dictionary<string, int[]>(tiersByVariantValue.Count);
            foreach (var pair in tiersByVariantValue)
            {
                if (pair.Value == null) continue;
                copy[pair.Key] = (int[])pair.Value.Clone();
            }

            extraVariantSockets[itemCode] = new Config.CustomVariantSocketsTiers(itemCode, variantKey, copy);
        }

        /// <summary>
        /// Adds code fragments to a "$group" used by the config — most importantly "jewelry",
        /// which decides both the $jewelry group and which gems may be encrusted into which items.
        /// Additive: several mods can contribute to the same group.
        /// </summary>
        public static void RegisterItemGroupMembers(string groupName, params string[] codeFragments)
        {
            if (string.IsNullOrEmpty(groupName) || codeFragments == null || codeFragments.Length == 0) return;
            if (Sealed) WarnIfSealed(nameof(RegisterItemGroupMembers), groupName, Assembly.GetCallingAssembly());

            if (!extraItemGroupMembers.TryGetValue(groupName, out var set))
            {
                set = new HashSet<string>();
                extraItemGroupMembers[groupName] = set;
            }
            foreach (string fragment in codeFragments)
            {
                if (!string.IsNullOrEmpty(fragment)) set.Add(fragment);
            }
        }

        /// <summary>
        /// Adds stack attributes that name an item's material, appended after the core's base set.
        /// Only needed for jewelry using a material key the core does not know.
        /// </summary>
        public static void RegisterMaterialAttributeKeys(params string[] attributeKeys)
        {
            if (attributeKeys == null) return;
            foreach (string key in attributeKeys)
            {
                if (string.IsNullOrEmpty(key) || materialAttributeKeys.Contains(key)) continue;
                materialAttributeKeys.Add(key);
            }
        }

        /// <summary>
        /// Seeds the base material keys. Called by the core before anything reads the registry;
        /// separate from the public API so the base set stays first in priority order.
        /// </summary>
        internal static void SeedMaterialAttributeKeys(params string[] attributeKeys)
        {
            if (attributeKeys == null) return;
            for (int i = 0; i < attributeKeys.Length; i++)
            {
                string key = attributeKeys[i];
                if (string.IsNullOrEmpty(key) || materialAttributeKeys.Contains(key)) continue;
                materialAttributeKeys.Insert(i, key);
            }
        }

        private static void WarnIfSealed(string method, string subject, Assembly callingAssembly)
        {
            if (!Sealed) return;
            string caller = callingAssembly?.GetName()?.Name ?? "unknown assembly";
            Logger?.Warning(
                "[canjewelry] {0}(\"{1}\") from {2} arrived after config defaults were built. " +
                "It is recorded and will apply on the next start.", method, subject, caller);
        }
    }
}
