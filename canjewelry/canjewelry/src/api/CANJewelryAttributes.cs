namespace canjewelry.src.api
{
    /// <summary>
    /// Names of the JSON attributes the core reads off itemtypes supplied by content mods.
    /// Kept in one place so the documented contract and the code cannot drift apart.
    /// </summary>
    public static class CANJewelryAttributes
    {
        /// <summary>
        /// Collectible attribute selecting how encrusted gems are exposed on the ItemStack.
        /// Values: <see cref="GemVisualSingle"/> or <see cref="GemVisualIndexed"/>.
        /// Absent means the item shows no gem visual at all.
        /// </summary>
        public const string GemVisual = "canGemAttribute";

        /// <summary>One gem visual shared by the whole item, written to the flat "gem" attribute.</summary>
        public const string GemVisualSingle = "single";

        /// <summary>One gem visual per socket, written to "gem_1".."gem_N" (1-based).</summary>
        public const string GemVisualIndexed = "indexed";

        /// <summary>
        /// Collectible attribute holding a <see cref="CANDisplayPose"/> — the transform chain
        /// applied when the item sits on a jeweler set.
        /// </summary>
        public const string JewelerSetPose = "canJewelerSetPose";

        /// <summary>
        /// Collectible attribute holding a string array: which stack attributes name the item's
        /// material, in priority order. Overrides the registry and the core's base set.
        /// </summary>
        public const string MaterialAttrKeys = "canMaterialAttrKeys";

        /// <summary>Stack attribute written for a single-gem item.</summary>
        public const string GemStackKey = "gem";

        /// <summary>Prefix of the stack attributes written for an indexed-gem item.</summary>
        public const string GemStackKeyPrefix = "gem_";
    }
}
