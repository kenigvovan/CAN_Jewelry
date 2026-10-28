using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src;
using canjewelry.src.utils;
using Vintagestory.API.Datastructures;
using Xunit;
using static canjewelry.src.Config;

namespace canjewelry.Tests.Utils
{
    public class CANGemAppraisalTests
    {
        private static GemCaratsConfig Cfg() => new GemCaratsConfig();

        private static bool OnStep(float value, float step)
            => Math.Abs(value / step - Math.Round(value / step)) < 0.001;

        [Theory]
        [InlineData("chipped")]
        [InlineData("flawed")]
        [InlineData("normal")]
        public void RollCarats_StaysInQualityRange_OnStep(string quality)
        {
            var cfg = Cfg();
            float[] range = CANGemAppraisal.RoughRange(cfg, quality, "ruby");
            var rand = new Random(1);
            for (int i = 0; i < 500; i++)
            {
                float c = CANGemAppraisal.RollCarats(range, cfg.Step, rand);
                Assert.InRange(c, range[0] - 0.001f, range[1] + 0.001f);
                Assert.True(OnStep(c, cfg.Step), c.ToString());
            }
        }

        [Fact]
        public void RoughRange_GemOverrideWins_OtherQualitiesFallBack()
        {
            var cfg = Cfg();
            cfg.GemOverrides["emerald"] = new GemCaratsOverride
            {
                RoughRanges = new Dictionary<string, float[]> { { "normal", new[] { 8f, 9f } } }
            };

            Assert.Equal(new[] { 8f, 9f }, CANGemAppraisal.RoughRange(cfg, "normal", "emerald"));
            Assert.Equal(cfg.RoughRanges["flawed"], CANGemAppraisal.RoughRange(cfg, "flawed", "emerald"));
            Assert.Equal(cfg.RoughRanges["normal"], CANGemAppraisal.RoughRange(cfg, "normal", "ruby"));
        }

        [Fact]
        public void RollTraits_RespectsMaxAndExcludes()
        {
            var cfg = Cfg();
            // A pristine stone first, excluding both flaws - as a config may define one.
            cfg.Traits = new Dictionary<string, GemTraitConfig>
            {
                { "pristine", new GemTraitConfig(null, 1f, 1f, false, new[] { "crack", "inclusions" }) },
                { "crack", cfg.Traits["crack"] },
                { "inclusions", cfg.Traits["inclusions"] }
            };
            foreach (var trait in cfg.Traits.Values) trait.Chance = new[] { 1f, 1f, 1f };
            cfg.Appraisal.MaxTraits = 3;
            Assert.Equal(new[] { "pristine" }, CANGemAppraisal.RollTraits(cfg, "chipped", "ruby", new Random(1)));

            cfg.Traits.Remove("pristine");
            cfg.Appraisal.MaxTraits = 1;
            Assert.Single(CANGemAppraisal.RollTraits(cfg, "chipped", "ruby", new Random(1)));
        }

        [Fact]
        public void RollTraits_TraitWeightZero_NeverRolls()
        {
            var cfg = Cfg();
            foreach (var trait in cfg.Traits.Values) trait.Chance = new[] { 1f, 1f, 1f };
            cfg.GemOverrides["emerald"] = new GemCaratsOverride { TraitWeights = new Dictionary<string, float> { { "crack", 0f } } };

            for (int i = 0; i < 50; i++)
            {
                Assert.DoesNotContain("crack", CANGemAppraisal.RollTraits(cfg, "normal", "emerald", new Random(i)));
            }
        }

        [Fact]
        public void LoupeRange_HoldsTruth_NarrowerThanQualityRange()
        {
            var cfg = Cfg();
            float[] quality = cfg.RoughRanges["flawed"];
            var rand = new Random(3);
            for (int i = 0; i < 500; i++)
            {
                float truth = CANGemAppraisal.RollCarats(quality, cfg.Step, rand);
                float[] range = CANGemAppraisal.LoupeRange(truth, quality, cfg.Appraisal.LoupeRangeFraction, cfg.Step, rand);
                Assert.InRange(truth, range[0], range[1]);
                Assert.True(range[1] - range[0] < quality[1] - quality[0]);
                Assert.True(OnStep(range[0], cfg.Step) && OnStep(range[1], cfg.Step));
            }
        }

        [Fact]
        public void LoupeRange_TruthNotAlwaysCentred()
        {
            var cfg = Cfg();
            float[] quality = cfg.RoughRanges["normal"];
            var rand = new Random(5);
            var centres = Enumerable.Range(0, 50)
                .Select(_ => CANGemAppraisal.LoupeRange(6f, quality, 0.4f, cfg.Step, rand))
                .Select(r => Math.Round((r[0] + r[1]) / 2, 2))
                .Distinct()
                .Count();
            Assert.True(centres > 3);
        }

        [Fact]
        public void RevealOne_OnlyHiddenTruth()
        {
            var rand = new Random(1);
            Assert.Null(CANGemAppraisal.RevealOne(new string[0], null, rand));
            Assert.Null(CANGemAppraisal.RevealOne(new[] { "crack" }, new[] { "crack" }, rand));
            Assert.Equal("cloudy", CANGemAppraisal.RevealOne(new[] { "crack", "cloudy" }, new[] { "crack" }, rand));
        }

        [Fact]
        public void CutCarats_AppliesYieldAndCrack()
        {
            var cfg = Cfg();
            Assert.Equal(2.0f, CANGemAppraisal.CutCarats(cfg, 4.0f, "round", "ruby", new string[0]), 3);
            Assert.Equal(1.6f, CANGemAppraisal.CutCarats(cfg, 4.0f, "round", "ruby", new[] { "crack" }), 3);
            Assert.Equal(2.4f, CANGemAppraisal.CutCarats(cfg, 4.0f, "baguette", "ruby", new string[0]), 3);
        }

        [Theory]
        [InlineData(0.1f, 1)]
        [InlineData(1.5f, 1)]
        [InlineData(1.6f, 2)]
        [InlineData(2.5f, 2)]
        [InlineData(2.6f, 3)]
        [InlineData(9f, 3)]
        public void BucketForCarats_UsesUpperBounds(float carats, int size)
        {
            Assert.Equal(size, CANGemAppraisal.BucketForCarats(Cfg(), carats));
        }

        [Theory]
        [InlineData(3.0f, 1, 1.5f)]
        [InlineData(0.8f, 2, 1.6f)]
        [InlineData(2.0f, 2, 2.0f)]
        [InlineData(1.0f, 3, 2.6f)]
        [InlineData(5.0f, 3, 5.0f)]
        public void ClampToBucket_MovesIntoTheSize(float carats, int size, float expected)
        {
            var cfg = Cfg();
            float clamped = CANGemAppraisal.ClampToBucket(cfg, carats, size);
            Assert.Equal(expected, clamped, 3);
            Assert.Equal(size, CANGemAppraisal.BucketForCarats(cfg, clamped));
        }

        [Fact]
        public void LegacyCarats_FitTheirOwnSize()
        {
            var cfg = Cfg();
            for (int size = 1; size <= 3; size++)
            {
                Assert.Equal(size, CANGemAppraisal.BucketForCarats(cfg, CANGemAppraisal.LegacyCutCarats(cfg, size)));
            }
        }

        [Theory]
        [InlineData(1, 1.5f)]
        [InlineData(3, 3.5f)]
        [InlineData(4, 4.5f)]
        [InlineData(0, 0f)]
        public void SocketCapacity_ContinuesLastStep(int tier, float capacity)
        {
            Assert.Equal(capacity, CANGemAppraisal.SocketCapacity(Cfg(), tier), 3);
        }

        [Fact]
        public void FitsSocket_ByWeight_ByTiers_AndLegacy()
        {
            var cfg = Cfg();
            var heavy = new TreeAttribute();
            CANGemAppraisal.SetWeight(heavy, 2.6f, null);
            var edge = new TreeAttribute();
            CANGemAppraisal.SetWeight(edge, 2.5f, null);

            Assert.False(CANGemAppraisal.FitsSocket(cfg, 2, 2, heavy));
            Assert.True(CANGemAppraisal.FitsSocket(cfg, 2, 2, edge));
            Assert.True(CANGemAppraisal.FitsSocket(cfg, 3, 3, heavy));

            // Legacy gems fit the sockets they always fitted.
            var legacy = new TreeAttribute();
            for (int size = 1; size <= 3; size++)
            {
                for (int tier = 1; tier <= 3; tier++)
                {
                    Assert.Equal(tier >= size, CANGemAppraisal.FitsSocket(cfg, tier, size, legacy));
                }
            }

            cfg.CapacityMode = "tiers";
            Assert.True(CANGemAppraisal.FitsSocket(cfg, 2, 2, heavy));
            Assert.False(CANGemAppraisal.FitsSocket(cfg, 1, 2, edge));
        }

        [Theory]
        [InlineData(2.0f, 2.0f, 1f)]
        [InlineData(10f, 1f, 1.15f)]
        [InlineData(0.1f, 3.3f, 0.85f)]
        [InlineData(2.6f, 2.0f, 1.1f)]
        public void DrawScale_CubeRoot_ClampedAndStepped(float carats, float nominal, float expected)
        {
            Assert.Equal(expected, CANGemAppraisal.DrawScale(carats, nominal), 3);
        }

        // Before the loupe the drawn size says nothing; after it, only what the range says.
        [Fact]
        public void RoughDrawScale_UsesTheShownRange_NotTheTruth()
        {
            var cfg = Cfg();
            var tree = new TreeAttribute();
            CANGemAppraisal.SetWeight(tree, 7.0f, null);
            Assert.Equal(1f, CANGemAppraisal.RoughDrawScale(cfg, tree, "normal", "ruby"));

            CANGemAppraisal.SetKnowledge(tree, CANGemAppraisal.Loupe, new[] { 5.6f, 6.4f }, null);
            Assert.Equal(1f, CANGemAppraisal.RoughDrawScale(cfg, tree, "normal", "ruby"));
        }

        [Fact]
        public void EnsureTruth_RollsOnce()
        {
            var cfg = Cfg();
            var tree = new TreeAttribute();
            Assert.True(CANGemAppraisal.EnsureTruth(tree, cfg, "flawed", "ruby", new Random(1)));
            float first = CANGemAppraisal.GetCarats(tree);

            Assert.False(CANGemAppraisal.EnsureTruth(tree, cfg, "flawed", "ruby", new Random(99)));
            Assert.Equal(first, CANGemAppraisal.GetCarats(tree));
        }

        [Fact]
        public void CopyRoughKeys_CarriesTruthAndKnowledge()
        {
            var from = new TreeAttribute();
            CANGemAppraisal.SetWeight(from, 3.4f, new[] { "crack", "cloudy" });
            CANGemAppraisal.SetKnowledge(from, CANGemAppraisal.Loupe, new[] { 3.0f, 3.8f }, new[] { "crack" });

            var to = new TreeAttribute();
            CANGemAppraisal.CopyRoughKeys(from, to);

            Assert.Equal(3.4f, CANGemAppraisal.GetCarats(to));
            Assert.Equal(new[] { "crack", "cloudy" }, CANGemAppraisal.GetTraits(to));
            Assert.Equal(new[] { "crack" }, CANGemAppraisal.GetKnownTraits(to));
            Assert.Equal(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(to));
            Assert.Equal(new[] { 3.0f, 3.8f }, CANGemAppraisal.GetCaratsRange(to));
        }

        [Fact]
        public void BuildCutGemTreeFromSocket_CarriesWeight_LegacySocketHasNone()
        {
            var socket = new TreeAttribute();
            socket.SetString(CANJWConstants.CUTTING_TYPE, "round");
            Assert.False(CANGemAppraisal.HasCarats(CANGemSize.BuildCutGemTreeFromSocket(socket)));

            CANGemAppraisal.SetWeight(socket, 2.2f, new[] { "inclusions" });
            ITreeAttribute cut = CANGemSize.BuildCutGemTreeFromSocket(socket);
            Assert.Equal(2.2f, CANGemAppraisal.GetCarats(cut));
            Assert.Equal(new[] { "inclusions" }, CANGemAppraisal.GetTraits(cut));
        }

        [Fact]
        public void TraitBuffMult_MultipliesKnownTraits_IgnoresUnknown()
        {
            var cfg = Cfg();
            cfg.Traits["crack"].BuffMult = 0.9f;
            cfg.Traits["inclusions"].BuffMult = 0.8f;
            Assert.Equal(0.9f * 0.8f, CANGemAppraisal.TraitBuffMult(cfg, new[] { "crack", "inclusions", "nosuchtrait" }), 4);
            Assert.Equal(1f, CANGemAppraisal.TraitBuffMult(cfg, null));
        }

        [Fact]
        public void ShownRange_FineLoupe_IsExact()
        {
            float[] range = CANGemAppraisal.ShownRange(Cfg(), 5.4f, new[] { 5f, 7f }, 0f, new Random(1));
            Assert.Equal(new[] { 5.4f, 5.4f }, range);
            Assert.True(CANGemAppraisal.IsExact(range));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ShownRange_SimpleLoupe_HoldsTheTruth_WithinTwiceTheHalfWidth(int seed)
        {
            float[] range = CANGemAppraisal.ShownRange(Cfg(), 5.4f, new[] { 5f, 7f }, 0.3f, new Random(seed));
            Assert.InRange(5.4f, range[0], range[1]);
            // 0.6 wide, rounded outward to the 0.1 step.
            Assert.InRange(range[1] - range[0], 0.6f - 1e-3f, 0.8f + 1e-3f);
            Assert.False(CANGemAppraisal.IsExact(range));
        }

        [Fact]
        public void ShownRange_NoHalfWidth_FallsBackToTheQualityShare()
        {
            float[] range = CANGemAppraisal.ShownRange(Cfg(), 6f, new[] { 5f, 7f }, null, new Random(4));
            Assert.InRange(6f, range[0], range[1]);
            Assert.InRange(range[1] - range[0], 0.8f - 1e-3f, 1.0f + 1e-3f);
        }

        [Fact]
        public void ForecastCutWeight_TakesKnownTraitsAndMistakes_UnderTheCap()
        {
            var cfg = Cfg();
            var tree = new TreeAttribute();
            CANGemAppraisal.SetKnowledge(tree, CANGemAppraisal.Loupe, new[] { 5f, 7f }, new[] { "crack" });
            float crack = cfg.Traits["crack"].YieldMult;

            float[] cut = CANGemForecast.CutWeight(cfg, new[] { 5f, 7f }, 0.5f, 0.6f, tree, 0.9f);
            Assert.Equal(CANGemAppraisal.RoundCarats(5f * 0.5f * crack * 0.9f, cfg.Step), cut[0], 3);
            Assert.Equal(Math.Min(CANGemAppraisal.RoundCarats(7f * 0.6f * crack * 0.9f, cfg.Step), cfg.MaxCutCarats), cut[1], 3);

            float[] uncapped = CANGemForecast.CutWeight(cfg, new[] { 9f, 9f }, 0.6f, 0.6f, new TreeAttribute());
            Assert.Equal(cfg.MaxCutCarats, uncapped[1], 3);
        }

        [Fact]
        public void PieceBuffMult_TakesItsShareForEachPiece()
        {
            var cfg = Cfg();
            float penalty = cfg.Traits["inclusions"].PieceBuffPenalty;
            Assert.Equal((1 - penalty) * (1 - penalty), CANGemAppraisal.PieceBuffMult(cfg, new[] { "inclusions", "inclusions", "nosuchtrait" }), 4);
            Assert.Equal(1f, CANGemAppraisal.PieceBuffMult(cfg, null));
        }
    }
}
