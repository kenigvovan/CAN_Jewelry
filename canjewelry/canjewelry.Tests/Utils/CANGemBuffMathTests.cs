using System;
using System.Collections.Generic;
using canjewelry.src.utils;
using Xunit;
using static canjewelry.src.Config;

namespace canjewelry.Tests.Utils
{
    public class CANGemBuffMathTests
    {
        private static readonly Dictionary<int, float[]> Walkspeed = new()
        {
            { 1, new[] { 0.01f, 0.03f } },
            { 2, new[] { 0.04f, 0.06f } },
            { 3, new[] { 0.07f, 0.09f } }
        };

        private static float Anchor(int size) => CANGemAppraisal.LegacyCutCarats(new GemCaratsConfig(), size);

        // The fitted curve stays close to the old tier middles at the legacy weights.
        [Fact]
        public void Fit_KeepsOldTierMiddles_WithinFifteenPercent()
        {
            BuffCurve curve = CANGemBuffMath.Fit(Walkspeed, null, null, null, 1.2f, Anchor)!;
            foreach (var tier in Walkspeed)
            {
                float old = (tier.Value[0] + tier.Value[1]) / 2;
                float fitted = (float)(curve.Mid * Math.Pow(Anchor(tier.Key), 1.2));
                Assert.InRange(fitted, old * 0.85f, old * 1.15f);
            }
            Assert.InRange(curve.Spread, 0f, 0.5f);
            Assert.Equal(0.09f, curve.Max, 5);
        }

        [Fact]
        public void Fit_ExplicitValuesWin()
        {
            BuffCurve curve = CANGemBuffMath.Fit(Walkspeed, 0.5f, 0.1f, 2f, 1f, Anchor)!;
            Assert.Equal(0.5f, curve.Mid);
            Assert.Equal(0.1f, curve.Spread);
            Assert.Equal(2f, curve.Max);
        }

        // A given spread replaces the fitted one; the cap is the curve's top at the heaviest weight.
        [Fact]
        public void Fit_DefaultSpread_AndCapByWeight()
        {
            BuffCurve curve = CANGemBuffMath.Fit(Walkspeed, 0.02f, null, null, 1.6f, Anchor, 0.05f, 3.5f)!;
            Assert.Equal(0.05f, curve.Spread);
            Assert.Equal((float)(0.02 * Math.Pow(3.5, 1.6) * 1.05), curve.Max, 5);
            Assert.InRange(CANGemBuffMath.Roll(curve, 3.5f, new Random(2)), 0.02f * Math.Pow(3.5, 1.6) * 0.95 - 1e-3, curve.Max);

            BuffCurve own = CANGemBuffMath.Fit(Walkspeed, 0.02f, 0.2f, 0.07f, 1.6f, Anchor, 0.05f, 3.5f)!;
            Assert.Equal(0.2f, own.Spread);
            Assert.Equal(0.07f, own.Max);
        }

        [Fact]
        public void Range_SpansTheSpread_AndKeepsUnderTheCap()
        {
            var curve = new BuffCurve { Mid = 1f, Spread = 0.1f, K = 1f, Max = 3f };
            float[] range = CANGemBuffMath.Range(curve, 2f, 4f);
            Assert.Equal(2f * 0.9f, range[0], 4);
            Assert.Equal(3f, range[1], 4);
        }

        [Fact]
        public void Fit_NothingToFit_IsNull()
        {
            Assert.Null(CANGemBuffMath.Fit(null, null, null, null, 1.2f, Anchor));
            Assert.NotNull(CANGemBuffMath.Fit(null, 0.02f, null, null, 1.2f, Anchor));
        }

        [Fact]
        public void Fit_NegativeBuff_KeepsSign_CapsBySize()
        {
            var negative = new Dictionary<int, float[]> { { 1, new[] { -0.03f, -0.01f } }, { 3, new[] { -0.09f, -0.07f } } };
            BuffCurve curve = CANGemBuffMath.Fit(negative, null, null, null, 1.2f, Anchor)!;
            Assert.True(curve.Mid < 0);
            Assert.Equal(0.09f, curve.Max, 5);
            Assert.InRange(CANGemBuffMath.Roll(curve, 10f, new Random(1)), -0.09f, 0f);
        }

        [Fact]
        public void Roll_StaysWithinSpread_AndUnderTheCap()
        {
            var curve = new BuffCurve { Mid = 0.02f, Spread = 0.25f, K = 1.2f, Max = 0.09f };
            var rand = new Random(7);
            for (int i = 0; i < 500; i++)
            {
                float c = 0.5f + i * 0.01f;
                float mid = (float)(0.02 * Math.Pow(c, 1.2));
                float v = CANGemBuffMath.Roll(curve, c, rand);
                Assert.InRange(v, Math.Min(0.09f, mid * 0.75f) - 0.001f, Math.Min(0.09f, mid * 1.25f) + 0.001f);
            }
        }

        [Fact]
        public void Scale_FollowsTheCurve()
        {
            Assert.Equal(0.05f * (float)Math.Pow(0.5, 1.2), CANGemBuffMath.Scale(0.05f, 2f, 1f, 1.2f), 5);
            Assert.Equal(0.05f, CANGemBuffMath.Scale(0.05f, 0f, 1f, 1.2f));
        }

        [Theory]
        [InlineData(0.5f, 0.3f, 0.3f)]
        [InlineData(-0.5f, 0.3f, -0.3f)]
        [InlineData(0.5f, 0f, 0.5f)]
        public void Clamp_KeepsSign(float value, float max, float expected)
        {
            Assert.Equal(expected, CANGemBuffMath.Clamp(value, max));
        }

        [Fact]
        public void BuffAttributes_Curves_UseConfigExponent_UnlessOwn()
        {
            var cfg = new GemCaratsConfig();
            var attrs = new BuffAttributes(Walkspeed, Walkspeed, null);
            Assert.Equal(cfg.BuffExponent, attrs.MainCurve(cfg)!.K);

            attrs.CaratExponent = 1f;
            Assert.Equal(1f, attrs.SecondaryCurve(cfg)!.K);
        }
    }
}
