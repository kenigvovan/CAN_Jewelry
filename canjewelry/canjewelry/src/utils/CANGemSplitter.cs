using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    /// <summary>
    /// Splitting a cut gem into lighter ones, and moving a gem's buffs along the weight curve -
    /// shared by the split and by the downgrade on extraction.
    /// </summary>
    public static class CANGemSplitter
    {
        /// <summary>
        /// Weights of the pieces: what is left after the loss, shared out by the ratios and rounded
        /// down to the step, so together they never weigh more than that. Null when there are fewer
        /// than two pieces or one would be lighter than minPiece.
        /// </summary>
        public static float[] Pieces(float carats, float[] ratios, float loss, float step, float minPiece)
        {
            if (ratios == null || ratios.Length < 2 || ratios.Any(r => r <= 0)) return null;

            float total = carats * (1 - Math.Clamp(loss, 0f, 1f));
            float sum = ratios.Sum();
            float[] pieces = ratios
                .Select(r => step > 0 ? (float)Math.Round(Math.Floor(total * r / sum / step + 0.001) * step, 3) : total * r / sum)
                .ToArray();
            return pieces.Any(p => p < minPiece - 0.001f || p <= 0) ? null : pieces;
        }

        /// <summary>One rough piece a cleave gives: its weight, its traits and what of them is known.</summary>
        public class CleavedPiece
        {
            public float Carats;
            public string[] Traits;
            public string[] Known;
        }

        /// <summary>
        /// What a rough gem loses to a cleave: little along a crack the player has found - the
        /// gem parts where it was already weak - the full share otherwise.
        /// </summary>
        public static float CleaveLoss(GemCaratsConfig cfg, string[] traits, string[] known)
            => traits.Contains("crack") && known.Contains("crack") ? cfg.Split.CrackLoss : cfg.Split.Loss;

        /// <summary>
        /// Cleaves a rough gem into pieces. A crack the player found is the cleaving plane and is
        /// gone from every piece; an unfound one stays in one piece, as does each other flaw with a
        /// place. Whole-stone traits go to every piece. Null when a piece would be too light.
        /// </summary>
        public static List<CleavedPiece> Cleave(GemCaratsConfig cfg, float carats, string[] traits, string[] known,
            float[] ratios, float loss, Random rand)
        {
            traits = traits ?? new string[0];
            known = known ?? new string[0];
            float[] weights = Pieces(carats, ratios, loss, cfg.Step, cfg.Split.MinPieceCarats);
            if (weights == null) return null;

            bool alongCrack = traits.Contains("crack") && known.Contains("crack");
            var pieces = weights.Select(w => new List<string>(traits.Where(t => !CANGemDefects.IsLocal(cfg, t)))).ToList();
            foreach (string trait in traits.Where(t => CANGemDefects.IsLocal(cfg, t)))
            {
                if (alongCrack && trait == "crack") continue;
                pieces[rand.Next(pieces.Count)].Add(trait);
            }

            return weights.Select((w, i) => new CleavedPiece
            {
                Carats = w,
                Traits = pieces[i].ToArray(),
                Known = pieces[i].Where(known.Contains).ToArray()
            }).ToList();
        }

        /// <summary>
        /// The rough gem item a piece of this weight is: the best quality whose range it reaches,
        /// ours when there is such an item, else the vanilla gem with the matching potential.
        /// </summary>
        public static ItemStack RoughStackFor(IWorldAccessor world, GemCaratsConfig cfg, string gemType, float carats)
        {
            string quality = CANGemSize.RoughChipped;
            foreach (string q in new[] { CANGemSize.RoughNormal, CANGemSize.RoughFlawed })
            {
                float[] range = CANGemAppraisal.RoughRange(cfg, q, gemType);
                if (range != null && carats >= range[0] - 0.001f) { quality = q; break; }
            }

            Item own = world.GetItem(CANGemSize.RoughGemCode(quality, gemType));
            if (own != null) return new ItemStack(own);
            Item vanilla = world.GetItem(CANGemSize.VanillaRoughGemCode(gemType));
            if (vanilla == null) return null;
            var stack = new ItemStack(vanilla);
            stack.Attributes.SetString("potential", CANGemSize.PotentialOfRoughQuality(quality));
            return stack;
        }

        /// <summary>
        /// Scales every buff of a cut gem tree from one weight to another along the curve, each by
        /// its own buff's exponent when it has one.
        /// </summary>
        public static void ScaleBuffs(ITreeAttribute cutGemTree, float from, float to, Config config)
        {
            string[] names = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
            float[] values = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;
            if (names == null || values == null) return;

            float[] scaled = (float[])values.Clone();
            for (int i = 0; i < scaled.Length && i < names.Length; i++)
            {
                float k = config.BuffAttributesDict.TryGetValue(names[i], out var attrs) && attrs?.CaratExponent != null
                    ? attrs.CaratExponent.Value
                    : config.GemCarats.BuffExponent;
                scaled[i] = CANGemBuffMath.Scale(scaled[i], from, to, k);
            }
            cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(scaled);
        }

        /// <summary>
        /// The gem as a lighter one: the item of the size the weight falls into, everything on the
        /// stack kept, buffs scaled, and - for a split - traits a split removes taken off.
        /// Null when no such item exists.
        /// </summary>
        public static ItemStack Resized(IWorldAccessor world, ItemStack gem, float from, float to, Config config, bool removeSplitTraits)
        {
            GemCaratsConfig cfg = config.GemCarats;
            string gemType = CANGemSize.GemTypeOf(gem.Collectible);
            Item item = world.GetItem(CANGemSize.CutGemCode(CANGemSize.CutQualityClamped(CANGemAppraisal.BucketForCarats(cfg, to)), gemType));
            if (item == null) return null;

            ITreeAttribute tree = gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.Clone() ?? new TreeAttribute();
            ScaleBuffs(tree, from, to, config);

            IEnumerable<string> traits = CANGemAppraisal.GetTraits(tree);
            if (removeSplitTraits)
            {
                traits = traits.Where(t => !(cfg.Traits.TryGetValue(t, out var trait) && trait?.RemovableBySplit == true));
            }
            CANGemAppraisal.SetWeight(tree, to, traits.ToArray());

            var resized = new ItemStack(item);
            resized.Attributes = gem.Attributes.Clone();
            resized.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
            return resized;
        }
    }
}
