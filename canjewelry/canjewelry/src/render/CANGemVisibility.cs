using Vintagestory.API.Client;

namespace canjewelry.src.render
{
    /// <summary>
    /// Whether gems are drawn on models at all — the server's say and the player's own, in one
    /// place, because both have to agree before a single mesh is built.
    ///
    /// <para>The server decides whether the feature is available (<c>Config.enableGemVisuals</c>);
    /// that config is replaced wholesale on every join, so a player's choice cannot live in it. The
    /// choice is kept in the game's own client settings instead, which survive joins and need no
    /// file of the mod's own.</para>
    /// </summary>
    public static class CANGemVisibility
    {
        private const string SettingKey = "canjewelryShowGemVisuals";

        private const int Unknown = 0;
        private const int Shown = 1;
        private const int Hidden = 2;

        // Asked once per item per frame, and from the chunk tesselation thread as well, so the two
        // dictionary lookups behind the setting are kept out of that path. Only this class writes
        // the setting, so the only thing that can invalidate the answer is SetShowGems.
        //
        // An int rather than a bool?: that is two fields, and a torn read across the two threads
        // could report a value that was never written. Volatile for the same reason.
        private static volatile int showGems = Unknown;

        /// <summary>Whether this client wants to see gems on models. Defaults to yes.</summary>
        public static bool ShowGems(ICoreClientAPI capi)
        {
            int known = showGems;
            if (known != Unknown) return known == Shown;

            if (capi?.Settings?.Bool == null) return true;

            bool show = !capi.Settings.Bool.Exists(SettingKey) || capi.Settings.Bool[SettingKey];
            showGems = show ? Shown : Hidden;
            return show;
        }

        /// <summary>Remembers the player's choice across sessions.</summary>
        public static void SetShowGems(ICoreClientAPI capi, bool show)
        {
            showGems = show ? Shown : Hidden;
            if (capi?.Settings?.Bool == null) return;

            capi.Settings.Bool[SettingKey] = show;
        }

        /// <summary>
        /// Whether a gem should be drawn at all right now: the server allows it and the player wants
        /// it. Every path that builds a gem asks this first.
        /// </summary>
        public static bool Enabled(ICoreClientAPI capi)
        {
            if (canjewelry.config?.enableGemVisuals == false) return false;

            return ShowGems(capi);
        }
    }
}
