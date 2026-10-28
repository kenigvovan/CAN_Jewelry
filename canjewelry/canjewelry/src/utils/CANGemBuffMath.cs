using System;
using System.Collections.Generic;
using System.Linq;

namespace canjewelry.src.utils
{
    /// <summary>
    /// A buff as a function of gem weight: mid·c^k, spread around it, the rolled base capped at Max.
    /// </summary>
    public sealed class BuffCurve
    {
        public float Mid;
        public float Spread;
        public float K;
        // Cap on the size of the rolled base value; 0 means none.
        public float Max;
    }

    public static class CANGemBuffMath
    {
        /// <summary>
        /// The curve of a buff. Whatever is not set explicitly is fitted from the old tier ranges,
        /// tier N taken at the weight anchor(N): the mid by least squares through zero, the spread
        /// as defaultSpread or else the tiers' average relative half width, the cap as the curve's top
        /// at capCarats or else the top tier's upper bound. Null when there is nothing to build it from.
        /// </summary>
        public static BuffCurve Fit(Dictionary<int, float[]> tiers, float? mid, float? spread, float? max, float k, Func<int, float> anchor,
            float? defaultSpread = null, float? capCarats = null)
        {
            var usable = (tiers ?? new Dictionary<int, float[]>())
                .Where(t => t.Value != null && t.Value.Length > 0 && anchor(t.Key) > 0)
                .ToList();
            if (mid == null && usable.Count == 0) return null;

            var curve = new BuffCurve
            {
                Mid = mid ?? FitPerCarat(usable, k, anchor),
                Spread = Math.Clamp(spread ?? defaultSpread ?? FitSpread(usable), 0f, 0.5f),
                K = k
            };
            curve.Max = Math.Abs(max ?? (capCarats > 0
                ? (float)(curve.Mid * Math.Pow(capCarats.Value, k) * (1 + curve.Spread))
                : TopOf(usable)));
            return curve;
        }

        private static float Low(float[] range) => range[0];
        private static float High(float[] range) => range[range.Length - 1];
        private static float Middle(float[] range) => (Low(range) + High(range)) / 2;

        public static float FitPerCarat(IEnumerable<KeyValuePair<int, float[]>> tiers, float k, Func<int, float> anchor)
        {
            double num = 0, den = 0;
            foreach (var tier in tiers)
            {
                double a = Math.Pow(anchor(tier.Key), k);
                num += Middle(tier.Value) * a;
                den += a * a;
            }
            return den > 0 ? (float)(num / den) : 0f;
        }

        public static float FitSpread(IEnumerable<KeyValuePair<int, float[]>> tiers)
        {
            var shares = tiers
                .Where(t => Math.Abs(Low(t.Value) + High(t.Value)) > 1e-6f)
                .Select(t => Math.Abs((High(t.Value) - Low(t.Value)) / (High(t.Value) + Low(t.Value))))
                .ToList();
            return shares.Count == 0 ? 0f : shares.Average();
        }

        private static float TopOf(List<KeyValuePair<int, float[]>> tiers)
        {
            if (tiers.Count == 0) return 0f;
            float[] top = tiers.OrderBy(t => t.Key).Last().Value;
            return Math.Max(Math.Abs(Low(top)), Math.Abs(High(top)));
        }

        /// <summary>Keeps the sign, caps the size; a cap of 0 or less caps nothing.</summary>
        public static float Clamp(float value, float max)
            => max > 0 ? Math.Clamp(value, -max, max) : value;

        /// <summary>The base value of a gem of this weight, before cut, grinding and trait multipliers.</summary>
        public static float Roll(BuffCurve curve, float carats, Random rand)
        {
            double noise = 1 + (rand.NextDouble() * 2 - 1) * curve.Spread;
            return (float)Math.Round(At(curve, carats, noise), 3);
        }

        /// <summary>Lowest and highest base value a gem of lo..hi carats can roll, spread included.</summary>
        public static float[] Range(BuffCurve curve, float lo, float hi)
        {
            float a = At(curve, lo, 1 - curve.Spread);
            float b = At(curve, hi, 1 + curve.Spread);
            return new[] { Math.Min(a, b), Math.Max(a, b) };
        }

        private static float At(BuffCurve curve, float carats, double noise)
            => Clamp((float)(curve.Mid * Math.Pow(Math.Max(0f, carats), curve.K) * noise), curve.Max);

        /// <summary>A value moved to another weight along the curve, as when a gem loses weight.</summary>
        public static float Scale(float value, float fromCarats, float toCarats, float k)
        {
            if (fromCarats <= 0) return value;
            return (float)(value * Math.Pow(Math.Max(0f, toCarats) / fromCarats, k));
        }
    }
}
