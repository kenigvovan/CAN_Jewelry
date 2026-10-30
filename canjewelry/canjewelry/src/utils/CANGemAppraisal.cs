using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    public enum AppraiseStage
    {
        Loupe,
        // No longer fired: the grinder window is gone. Kept so the stages after it keep their values.
        Window,
        Table,
        Inspect
    }

    /// <summary>
    /// Gem weight and traits: rolling the hidden truth of a rough gem, what the loupe reveals of
    /// it, and the weight a cut keeps. Server-side callers only - every roll here would differ
    /// between the two sides.
    /// </summary>
    public static class CANGemAppraisal
    {
        public const int Unappraised = 0;
        public const int Loupe = 1;
        // Gems windowed on the grinder before it was removed still carry this level.
        public const int Window = 2;

        private const float Eps = 0.001f;

        public static float RoundCarats(float carats, float step)
        {
            if (step <= 0) return carats;
            return (float)Math.Round(Math.Round(carats / step) * step, 3);
        }

        private static float FloorToStep(float value, float step)
            => step <= 0 ? value : (float)Math.Round(Math.Floor(value / step + Eps) * step, 3);

        private static float CeilToStep(float value, float step)
            => step <= 0 ? value : (float)Math.Round(Math.Ceiling(value / step - Eps) * step, 3);

        /// <summary>Rough weight range of a quality, the gem's override first. Null when unknown.</summary>
        public static float[] RoughRange(GemCaratsConfig cfg, string quality, string gemType)
        {
            if (quality == null) return null;
            if (gemType != null
                && cfg.GemOverrides.TryGetValue(gemType, out var over)
                && over?.RoughRanges != null
                && over.RoughRanges.TryGetValue(quality, out float[] own)
                && IsRange(own))
            {
                return own;
            }
            return cfg.RoughRanges.TryGetValue(quality, out float[] range) && IsRange(range) ? range : null;
        }

        private static bool IsRange(float[] range) => range != null && range.Length >= 2 && range[0] <= range[1];

        public static float RollCarats(float[] range, float step, Random rand)
        {
            float value = range[0] + (float)rand.NextDouble() * (range[1] - range[0]);
            return Math.Max(step, RoundCarats(value, step));
        }

        /// <summary>
        /// Each trait in config order on its own chance, until MaxTraits are taken. A trait that an
        /// already taken one excludes, or that excludes one already taken, is skipped.
        /// </summary>
        public static string[] RollTraits(GemCaratsConfig cfg, string quality, string gemType, Random rand)
        {
            int qualityIndex = CANGemSize.RoughSizeOf(quality) - 1;
            if (qualityIndex < 0) return new string[0];

            cfg.GemOverrides.TryGetValue(gemType ?? "", out var over);
            var taken = new List<string>();
            foreach (var pair in cfg.Traits)
            {
                if (taken.Count >= cfg.Appraisal.MaxTraits) break;
                GemTraitConfig trait = pair.Value;
                if (trait?.Chance == null || trait.Chance.Length == 0) continue;

                float chance = trait.Chance[Math.Min(qualityIndex, trait.Chance.Length - 1)];
                if (over?.TraitWeights != null && over.TraitWeights.TryGetValue(pair.Key, out float weight)) chance *= weight;
                if (rand.NextDouble() >= chance) continue;

                bool clashes = taken.Any(t => Excludes(cfg, t, pair.Key) || Excludes(cfg, pair.Key, t));
                if (!clashes) taken.Add(pair.Key);
            }
            return taken.ToArray();
        }

        private static bool Excludes(GemCaratsConfig cfg, string trait, string other)
            => cfg.Traits.TryGetValue(trait, out var t) && t?.Excludes != null && t.Excludes.Contains(other);

        /// <summary>
        /// A range LoupeRangeFraction as wide as the quality's, holding the truth somewhere inside
        /// rather than in the middle, so its centre does not give the weight away.
        /// </summary>
        public static float[] LoupeRange(float truth, float[] qualityRange, float fraction, float step, Random rand)
        {
            float lo = Math.Min(qualityRange[0], truth);
            float hi = Math.Max(qualityRange[1], truth);
            float width = (hi - lo) * Math.Clamp(fraction, 0f, 1f);

            float start = truth - (float)rand.NextDouble() * width;
            start = Math.Clamp(start, lo, Math.Max(lo, hi - width));
            return new[] { FloorToStep(Math.Min(start, truth), step), CeilToStep(Math.Max(start + width, truth), step) };
        }

        /// <summary>
        /// The weight a loupe shows: halfWidth either side of the truth was given, as a range
        /// 2·halfWidth wide holding the truth somewhere inside; the truth itself for 0. Without
        /// a halfWidth, the old LoupeRange share of the quality's range.
        /// </summary>
        public static float[] ShownRange(GemCaratsConfig cfg, float truth, float[] qualityRange, float? halfWidth, Random rand)
        {
            if (halfWidth == null) return LoupeRange(truth, qualityRange, cfg.Appraisal.LoupeRangeFraction, cfg.Step, rand);
            if (halfWidth.Value <= 0) return new[] { truth, truth };

            float width = 2 * halfWidth.Value;
            float start = truth - (float)rand.NextDouble() * width;
            return new[] { Math.Max(cfg.Step, FloorToStep(Math.Min(start, truth), cfg.Step)), CeilToStep(Math.Max(start + width, truth), cfg.Step) };
        }

        public static bool IsExact(float[] range) => range != null && range[1] - range[0] < Eps;

        /// <summary>A shown range moved along with a weight change, e.g. from grinding a flaw out.</summary>
        public static float[] ScaleRange(float[] range, float ratio, float step)
            => new[] { RoundCarats(range[0] * ratio, step), RoundCarats(range[1] * ratio, step) };

        /// <summary>One of the traits not known yet, null when there is none left.</summary>
        public static string RevealOne(string[] traits, string[] known, Random rand)
        {
            string[] hidden = (traits ?? new string[0]).Where(t => known == null || !known.Contains(t)).ToArray();
            return hidden.Length == 0 ? null : hidden[rand.Next(hidden.Length)];
        }

        public static float TraitYieldMult(GemCaratsConfig cfg, string[] traits)
            => Product(cfg, traits, t => t.YieldMult);

        public static float TraitBuffMult(GemCaratsConfig cfg, string[] traits)
            => Product(cfg, traits, t => t.BuffMult);

        /// <summary>What the flaw pieces left in a gem take from its buff, one PieceBuffPenalty each.</summary>
        public static float PieceBuffMult(GemCaratsConfig cfg, IEnumerable<string> pieces)
        {
            float mult = 1f;
            foreach (string trait in pieces ?? new string[0])
            {
                if (cfg.Traits.TryGetValue(trait, out var t) && t != null) mult *= Math.Clamp(1f - t.PieceBuffPenalty, 0f, 1f);
            }
            return mult;
        }

        public static string[] GetFlawPieces(ITreeAttribute tree)
            => (tree?[CANJWConstants.FLAW_PIECES] as StringArrayAttribute)?.value ?? new string[0];

        private static float Product(GemCaratsConfig cfg, string[] traits, Func<GemTraitConfig, float> pick)
        {
            float mult = 1f;
            foreach (string trait in traits ?? new string[0])
            {
                if (cfg.Traits.TryGetValue(trait, out var t) && t != null) mult *= pick(t);
            }
            return mult;
        }

        public static float CutYield(GemCaratsConfig cfg, string cuttingType, string gemType)
        {
            if (gemType != null
                && cfg.GemOverrides.TryGetValue(gemType, out var over)
                && over?.CutYield != null
                && over.CutYield.TryGetValue(cuttingType ?? "", out float own))
            {
                return own;
            }
            return cfg.CutYield.TryGetValue(cuttingType ?? "", out float yield) ? yield : 0.5f;
        }

        public static float CutCarats(GemCaratsConfig cfg, float roughCarats, string cuttingType, string gemType, string[] traits)
        {
            float cut = roughCarats * CutYield(cfg, cuttingType, gemType) * TraitYieldMult(cfg, traits);
            return Math.Max(cfg.Step, RoundCarats(cut, cfg.Step));
        }

        /// <summary>Cut size 1.. a weight falls into: the first whose upper bound holds it.</summary>
        public static int BucketForCarats(GemCaratsConfig cfg, float carats)
        {
            int size = 1;
            while (cfg.BucketBounds.TryGetValue(size, out float bound))
            {
                if (carats <= bound + Eps) return size;
                size++;
            }
            return size;
        }

        /// <summary>The weight moved into the given cut size, for cuts whose size the recipe decides.</summary>
        public static float ClampToBucket(GemCaratsConfig cfg, float carats, int size)
        {
            float lo = cfg.BucketBounds.TryGetValue(size - 1, out float below) ? below + cfg.Step : cfg.Step;
            float hi = cfg.BucketBounds.TryGetValue(size, out float above) ? above : float.MaxValue;
            return RoundCarats(Math.Clamp(carats, lo, Math.Max(lo, hi)), cfg.Step);
        }

        /// <summary>Carats a socket of this tier holds; above the configured tiers the last step continues.</summary>
        public static float SocketCapacity(GemCaratsConfig cfg, int tier)
        {
            if (cfg.SocketCapacity.TryGetValue(tier, out float capacity)) return capacity;
            if (cfg.SocketCapacity.Count == 0 || tier <= 0) return 0f;

            int last = cfg.SocketCapacity.Keys.Max();
            if (tier < last) return 0f;
            float top = cfg.SocketCapacity[last];
            float step = cfg.SocketCapacity.TryGetValue(last - 1, out float below) ? top - below : 0f;
            return top + step * (tier - last);
        }

        /// <summary>
        /// Whether a cut gem fits a socket: by its weight against the socket's capacity, or by
        /// tiers, its size against the socket tier. Gems from before carats weigh their size's
        /// legacy weight, which fits the same sockets as before.
        /// </summary>
        public static bool FitsSocket(GemCaratsConfig cfg, int socketTier, int gemSize, ITreeAttribute cutGemTree)
        {
            if (cfg == null || !cfg.CapacityByCarats) return socketTier >= gemSize;
            float carats = HasCarats(cutGemTree) ? GetCarats(cutGemTree) : LegacyCutCarats(cfg, gemSize);
            return carats <= SocketCapacity(cfg, socketTier) + Eps;
        }

        /// <summary>
        /// How many carats all sockets of an item may hold together; null for no limit or with
        /// budgets turned off. The item's own "cancaratbudget" attribute first, then the config's
        /// CaratBudgets, then what content mods registered.
        /// </summary>
        public static float? BudgetOf(GemCaratsConfig cfg, Vintagestory.API.Common.ItemStack item)
        {
            if (cfg == null || !cfg.CaratBudgetEnabled || item?.Collectible == null) return null;

            // A carved piece carries its own budget, decided by how it was carved.
            float onStack = item.Attributes?.GetFloat(CANJWConstants.CARAT_BUDGET, 0) ?? 0;
            if (onStack > 0) return onStack;

            float own = item.Collectible.Attributes?[CANJWConstants.CARAT_BUDGET].AsFloat(0) ?? 0;
            if (own > 0) return own;

            Vintagestory.API.Common.AssetLocation code = item.Collectible.Code;
            foreach (var pair in cfg.CaratBudgets ?? new Dictionary<string, float>())
            {
                if (pair.Value > 0 && Vintagestory.API.Util.WildcardUtil.Match(new Vintagestory.API.Common.AssetLocation(pair.Key), code)) return pair.Value;
            }
            foreach (var pair in api.CANJewelryRegistry.ExtraCaratBudgets)
            {
                if (Vintagestory.API.Util.WildcardUtil.Match(new Vintagestory.API.Common.AssetLocation(pair.Key), code)) return pair.Value;
            }
            return null;
        }

        /// <summary>The carats of the gems set into an item, leaving one socket out (-1 for none).</summary>
        public static float CaratsInSockets(GemCaratsConfig cfg, Vintagestory.API.Common.ItemStack item, int exceptSocket)
        {
            ITreeAttribute sockets = item?.Attributes?.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            if (sockets == null) return 0f;

            float sum = 0f;
            foreach (var pair in sockets)
            {
                if (!(pair.Value is ITreeAttribute socket) || !pair.Key.StartsWith("slot")) continue;
                if (pair.Key == "slot" + exceptSocket) continue;
                if (string.IsNullOrEmpty(socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, ""))) continue;
                sum += CANGemSize.GetCutCarats(socket, socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE), cfg);
            }
            return sum;
        }

        public static float LegacyCutCarats(GemCaratsConfig cfg, int size)
        {
            if (cfg.LegacyCutCarats.TryGetValue(size, out float carats)) return carats;
            return size <= 1 ? 1f : (size == 2 ? 2f : 3.3f);
        }

        // ---- Water ----

        // Game hour until which a rough gem is wet from being dipped in water.
        public const string WetUntilKey = "gemwetuntil";

        public static bool IsWet(ITreeAttribute tree, double totalHours)
            => tree != null && tree.GetDouble(WetUntilKey) > totalHours;

        // ---- Drawn size ----

        /// <summary>
        /// How much bigger or smaller a gem is drawn than the usual one of its kind: the cube root of
        /// the weight ratio, within ±15% and in steps of 0.05 so the shared transforms stay few.
        /// </summary>
        public static float DrawScale(float carats, float nominal)
        {
            if (carats <= 0 || nominal <= 0) return 1f;
            float scale = Math.Clamp((float)Math.Cbrt(carats / nominal), 0.85f, 1.15f);
            return (float)Math.Round(Math.Round(scale / 0.05f) * 0.05f, 2);
        }

        /// <summary>A cut gem by its weight against the legacy weight of its size.</summary>
        public static float CutDrawScale(GemCaratsConfig cfg, ITreeAttribute cutGemTree, int size)
            => cfg == null || !HasCarats(cutGemTree) ? 1f : DrawScale(GetCarats(cutGemTree), LegacyCutCarats(cfg, size));

        /// <summary>
        /// A rough gem by what the player knows: the middle of the range they were shown against
        /// the middle of its quality's. Never the hidden truth, or the size would give it away.
        /// </summary>
        public static float RoughDrawScale(GemCaratsConfig cfg, ITreeAttribute tree, string quality, string gemType)
        {
            float[] shown = GetAppraisal(tree) >= Loupe ? GetCaratsRange(tree) : null;
            float[] usual = cfg == null ? null : RoughRange(cfg, quality, gemType);
            if (shown == null || usual == null) return 1f;
            return DrawScale((shown[0] + shown[1]) / 2, (usual[0] + usual[1]) / 2);
        }

        // ---- Trees: rough gems, work items, cut gem trees and sockets ----

        public static bool HasCarats(ITreeAttribute tree) => tree?.HasAttribute(CANJWConstants.CARATS) == true;

        public static float GetCarats(ITreeAttribute tree) => tree?.GetFloat(CANJWConstants.CARATS) ?? 0f;

        public static string[] GetTraits(ITreeAttribute tree)
            => (tree?[CANJWConstants.GEM_TRAITS] as StringArrayAttribute)?.value ?? new string[0];

        public static string[] GetKnownTraits(ITreeAttribute tree)
            => (tree?[CANJWConstants.GEM_TRAITS_KNOWN] as StringArrayAttribute)?.value ?? new string[0];

        public static int GetAppraisal(ITreeAttribute tree) => tree?.GetInt(CANJWConstants.APPRAISAL) ?? 0;

        public static float[] GetCaratsRange(ITreeAttribute tree)
        {
            float[] range = (tree?[CANJWConstants.CARATS_RANGE] as FloatArrayAttribute)?.value;
            return range != null && range.Length >= 2 ? range : null;
        }

        /// <summary>Weight and traits, as on a cut gem tree or a socket.</summary>
        public static void SetWeight(ITreeAttribute tree, float carats, string[] traits)
        {
            tree.SetFloat(CANJWConstants.CARATS, carats);
            tree[CANJWConstants.GEM_TRAITS] = new StringArrayAttribute((string[])(traits ?? new string[0]).Clone());
        }

        public static void SetKnowledge(ITreeAttribute tree, int appraisal, float[] range, string[] known)
        {
            tree.SetInt(CANJWConstants.APPRAISAL, appraisal);
            if (range != null) tree[CANJWConstants.CARATS_RANGE] = new FloatArrayAttribute((float[])range.Clone());
            tree[CANJWConstants.GEM_TRAITS_KNOWN] = new StringArrayAttribute((string[])(known ?? new string[0]).Clone());
        }

        private static readonly string[] RoughKeys =
        {
            CANJWConstants.CARATS, CANJWConstants.GEM_TRAITS, CANJWConstants.GEM_TRAITS_KNOWN,
            CANJWConstants.APPRAISAL, CANJWConstants.CARATS_RANGE,
            CANGemDefects.TraitsKey, CANGemDefects.PosKey, CANGemDefects.NormalKey,
            CANGemRoughShape.SeedKey
        };

        /// <summary>Copies the truth and what is known of it, rough gem to work item and back.</summary>
        public static void CopyRoughKeys(ITreeAttribute from, ITreeAttribute to)
        {
            if (from == null || to == null) return;
            foreach (string key in RoughKeys)
            {
                if (from.HasAttribute(key)) to[key] = from[key].Clone();
            }
        }

        public static void RemoveWeight(ITreeAttribute tree)
        {
            tree.RemoveAttribute(CANJWConstants.CARATS);
            tree.RemoveAttribute(CANJWConstants.GEM_TRAITS);
        }

        /// <summary>
        /// Rolls the truth of a rough gem (or its work item) once. Returns false when it already had
        /// one, which is what keeps a gem from being rerolled by appraising it again.
        /// </summary>
        public static bool EnsureTruth(ITreeAttribute tree, GemCaratsConfig cfg, string quality, string gemType, Random rand)
        {
            if (HasCarats(tree)) return false;

            float[] range = RoughRange(cfg, quality, gemType) ?? new[] { 1f, 1f };
            SetWeight(tree, RollCarats(range, cfg.Step, rand), RollTraits(cfg, quality, gemType, rand));
            return true;
        }
    }
}
