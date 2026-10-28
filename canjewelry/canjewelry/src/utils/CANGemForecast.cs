using System;
using System.Linq;
using Vintagestory.API.Datastructures;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    /// <summary>
    /// What a rough gem is heading for, from what the player knows of it only: the weight range
    /// the loupe showed, the traits and flaw pieces found. Hidden ones never move a forecast.
    /// </summary>
    public static class CANGemForecast
    {
        /// <summary>The range the loupe showed, else the usual range of the gem's quality; null without either.</summary>
        public static float[] KnownRoughRange(GemCaratsConfig cfg, ITreeAttribute tree, string quality, string gemType)
        {
            float[] shown = CANGemAppraisal.GetAppraisal(tree) >= CANGemAppraisal.Loupe ? CANGemAppraisal.GetCaratsRange(tree) : null;
            return shown ?? CANGemAppraisal.RoughRange(cfg, quality, gemType);
        }

        /// <summary>
        /// The cut weight a rough range comes to at these yields: lowest yield for the low end,
        /// highest for the high one, the known traits' share, what mistakes kept, and the cap.
        /// </summary>
        public static float[] CutWeight(GemCaratsConfig cfg, float[] rough, float minYield, float maxYield, ITreeAttribute tree, float kept = 1f)
        {
            float share = CANGemAppraisal.TraitYieldMult(cfg, CANGemAppraisal.GetKnownTraits(tree)) * kept;
            return new[]
            {
                Math.Min(CANGemAppraisal.RoundCarats(rough[0] * minYield * share, cfg.Step), cfg.MaxCutCarats),
                Math.Min(CANGemAppraisal.RoundCarats(rough[1] * maxYield * share, cfg.Step), cfg.MaxCutCarats)
            };
        }

        /// <summary>The cut weight for one cut type.</summary>
        public static float[] CutWeight(GemCaratsConfig cfg, float[] rough, string cuttingType, string gemType, ITreeAttribute tree, float kept = 1f)
        {
            float yield = CANGemAppraisal.CutYield(cfg, cuttingType, gemType);
            return CutWeight(cfg, rough, yield, yield, tree, kept);
        }

        /// <summary>The cut weight over every cut the config knows, from the lowest yield to the highest.</summary>
        public static float[] CutWeightAnyCut(GemCaratsConfig cfg, float[] rough, string gemType, ITreeAttribute tree)
        {
            float[] yields = canjewelry.config.CuttingAttributesDict.Keys.Select(cut => CANGemAppraisal.CutYield(cfg, cut, gemType)).ToArray();
            return yields.Length == 0 ? null : CutWeight(cfg, rough, yields.Min(), yields.Max(), tree);
        }

        /// <summary>Flaw pieces whose trait the player already found.</summary>
        public static string[] KnownFlawPieces(ITreeAttribute tree)
        {
            if (!CANGemDefects.HasDefects(tree)) return new string[0];
            string[] known = CANGemAppraisal.GetKnownTraits(tree);
            return CANGemDefects.Read(tree).Select(d => d.Trait).Where(known.Contains).ToArray();
        }

        /// <summary>What the found flaw pieces take off the buffs, as a share: 0.1 for -10%.</summary>
        public static float KnownFlawLoss(GemCaratsConfig cfg, ITreeAttribute tree)
            => cfg.TraitsAffectBuffs ? 1f - CANGemAppraisal.PieceBuffMult(cfg, KnownFlawPieces(tree)) : 0f;

        /// <summary>The gem type's multiplier and that of the traits and flaw pieces found.</summary>
        public static float KnownBuffMult(GemCaratsConfig cfg, ITreeAttribute tree, string gemType)
        {
            float traits = cfg.TraitsAffectBuffs
                ? CANGemAppraisal.TraitBuffMult(cfg, CANGemAppraisal.GetKnownTraits(tree)) * CANGemAppraisal.PieceBuffMult(cfg, KnownFlawPieces(tree))
                : 1f;
            return cfg.BuffMultOf(gemType) * traits;
        }

        /// <summary>
        /// The main stat a cut gem of this weight can roll, times mult (cut, gem, traits). Null
        /// when buffs go by tiers or the stat has no curve.
        /// </summary>
        public static float[] MainBuff(GemCaratsConfig cfg, BuffAttributes attrs, float[] cutWeight, float mult)
        {
            BuffCurve curve = cfg.ScalesByCarats && cutWeight != null ? attrs?.MainCurve(cfg) : null;
            if (curve == null) return null;
            float[] range = CANGemBuffMath.Range(curve, cutWeight[0], cutWeight[1]);
            return new[] { Math.Min(range[0] * mult, range[1] * mult), Math.Max(range[0] * mult, range[1] * mult) };
        }
    }
}
