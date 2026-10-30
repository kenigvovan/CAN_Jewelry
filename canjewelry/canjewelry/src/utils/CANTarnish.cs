using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    /// <summary>
    /// Worn metal darkens with time. The value, 0 clean to 1 fully tarnished, lives on the stack
    /// under "cantarnish"; CANGemBuffAffected raises it while the piece is worn, a polishing cloth
    /// (canPolishStrength) lowers it. It costs up to MaxBuffPenalty of the gem buffs and shows as
    /// a tint multiplied over the metal textures in two steps.
    /// </summary>
    public static class CANTarnish
    {
        public const string Attr = "cantarnish";
        private static readonly string[] MetalKeys = { "metal", "loop", "carcassus" };

        public static float Get(ItemStack stack) => stack?.Attributes?.GetFloat(Attr, 0f) ?? 0f;

        public static void Set(ItemStack stack, float value)
        {
            if (value <= 0f) stack.Attributes.RemoveAttribute(Attr);
            else stack.Attributes.SetFloat(Attr, Math.Min(1f, value));
        }

        /// <summary>The metal of a piece: a stack attribute on most, a code variant on some.</summary>
        public static string MetalOf(ItemStack stack)
        {
            if (stack == null) return null;
            foreach (string key in MetalKeys)
            {
                string metal = stack.Attributes.GetString(key, null);
                if (!string.IsNullOrEmpty(metal)) return metal;
            }
            foreach (string key in MetalKeys)
            {
                string metal = stack.Collectible?.Variant?[key];
                if (!string.IsNullOrEmpty(metal)) return metal;
            }
            return null;
        }

        /// <summary>Days of wear to tarnish fully; 0 when the metal does not tarnish.</summary>
        public static float DaysToFull(TarnishConfig cfg, ItemStack stack)
        {
            string metal = MetalOf(stack);
            if (cfg == null || metal == null || cfg.DaysToFullByMetal == null) return 0f;
            return cfg.DaysToFullByMetal.TryGetValue(metal, out float days) ? Math.Max(0f, days) : 0f;
        }

        /// <summary>0 clean, 1 dull, 2 dark.</summary>
        public static int Stage(TarnishConfig cfg, ItemStack stack)
        {
            if (cfg == null || !cfg.Enabled) return 0;
            float t = Get(stack);
            return t >= cfg.DarkFrom ? 2 : t >= cfg.DullFrom ? 1 : 0;
        }

        /// <summary>Gem buff multiplier, 1 when clean.</summary>
        public static float BuffMult(TarnishConfig cfg, ItemStack stack)
        {
            if (cfg == null || !cfg.Enabled) return 1f;
            return 1f - GameMath.Clamp(cfg.MaxBuffPenalty, 0f, 1f) * Get(stack);
        }

        /// <summary>The tint for the stack's look and how many times it goes on; null when clean.</summary>
        public static TarnishTint Overlay(TarnishConfig cfg, ItemStack stack)
        {
            int stage = Stage(cfg, stack);
            if (stage == 0) return null;

            string metal = MetalOf(stack);
            if (metal == null || cfg.TintByMetal == null || !cfg.TintByMetal.TryGetValue(metal, out string tint)) return null;
            return new TarnishTint(new AssetLocation(tint), stage);
        }

        /// <summary>Mesh and texture-prefix key part, so a darker look is a different mesh.</summary>
        public static string CacheSuffix(TarnishConfig cfg, ItemStack stack)
        {
            int stage = Stage(cfg, stack);
            return stage == 0 ? "" : "-tarnish" + stage;
        }

        /// <summary>Only metal gets the tint, not gems, cloth or leather.</summary>
        public static bool IsMetalTexture(AssetLocation texture) => texture?.Path?.Contains("metal/") == true;

        /// <summary>The texture with the tint multiplied over it (once per stage), baked for an atlas.</summary>
        public static CompositeTexture Tinted(ICoreClientAPI capi, AssetLocation texture, TarnishTint tint)
        {
            var overlays = new BlendedOverlayTexture[tint.Times];
            for (int i = 0; i < overlays.Length; i++)
            {
                overlays[i] = new BlendedOverlayTexture { Base = tint.Texture, BlendMode = EnumColorBlendMode.Multiply };
            }

            var ct = new CompositeTexture(texture) { BlendedOverlays = overlays };
            ct.Bake(capi.Assets);
            return ct;
        }
    }

    /// <summary>A tint texture and how many times it is multiplied over the metal.</summary>
    public sealed class TarnishTint
    {
        public readonly AssetLocation Texture;
        public readonly int Times;

        public TarnishTint(AssetLocation texture, int times)
        {
            Texture = texture;
            Times = times;
        }
    }
}
