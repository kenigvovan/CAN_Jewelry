namespace canjewelry.src.render
{
    /// <summary>
    /// The diagnostic switches of the gem visuals, in one place. All of them are client side, all
    /// are off unless a <c>.cangem*</c> command turns them on, and none is read on a server.
    /// </summary>
    public static class CANGemDebug
    {
        /// <summary>
        /// Build the composite out of the item mesh alone, no gems added. What is on screen is then
        /// our rebuilt model rather than the vanilla one, which tells apart a mesh broken by adding
        /// gems from one broken by rebuilding the item at all.
        /// </summary>
        public static bool BaseOnly;

        /// <summary>
        /// Say what happens to every piece of worn gear that passes through the entity shape path,
        /// including why no gem was added.
        /// </summary>
        public static bool WornLog;

        /// <summary>
        /// While the debug menu is open, answer GUI resolves with the poses of this target instead.
        /// The 3D preview renders through the normal GUI path, so without this it could only ever
        /// show the "gui" pose and the other targets could not be tuned by eye. Null switches it off.
        /// </summary>
        public static string TargetRedirect;
    }
}
