using System.Linq;
using canjewelry.src.utils;
using Xunit;

namespace canjewelry.Tests.Utils
{
    public class CANGemSplitterTests
    {
        [Fact]
        public void Pieces_NeverWeighMoreThanWhatIsLeft()
        {
            float[] pieces = CANGemSplitter.Pieces(3.0f, new[] { 0.5f, 0.5f }, 0.1f, 0.1f, 0.3f)!;
            Assert.Equal(new[] { 1.3f, 1.3f }, pieces);
            Assert.True(pieces.Sum() <= 3.0f * 0.9f + 0.001f);
        }

        [Fact]
        public void Pieces_SharesAreNormalised()
        {
            Assert.Equal(new[] { 0.9f, 0.9f, 0.9f }, CANGemSplitter.Pieces(3.0f, new[] { 1f, 1f, 1f }, 0.1f, 0.1f, 0.3f));
            Assert.Equal(new[] { 1.8f, 0.8f }, CANGemSplitter.Pieces(3.0f, new[] { 7f, 3f }, 0.1f, 0.1f, 0.3f));
        }

        private static canjewelry.src.Config.GemCaratsConfig Cfg() => new canjewelry.src.Config.GemCaratsConfig();

        // Along a crack the player found the gem parts cleanly: less loss, and the crack is gone.
        [Fact]
        public void Cleave_AlongAFoundCrack_LosesLess_AndTheCrackIsGone()
        {
            var cfg = Cfg();
            string[] traits = { "crack", "cloudy" };
            Assert.Equal(cfg.Split.CrackLoss, CANGemSplitter.CleaveLoss(cfg, traits, new[] { "crack" }));
            Assert.Equal(cfg.Split.Loss, CANGemSplitter.CleaveLoss(cfg, traits, new string[0]));

            var pieces = CANGemSplitter.Cleave(cfg, 6f, traits, new[] { "crack" }, new[] { 1f, 1f },
                CANGemSplitter.CleaveLoss(cfg, traits, new[] { "crack" }), new System.Random(1))!;
            Assert.Equal(2, pieces.Count);
            Assert.All(pieces, p => Assert.DoesNotContain("crack", p.Traits));
            // A trait that is not local belongs to the whole stone: every piece has it.
            Assert.All(pieces, p => Assert.Contains("cloudy", p.Traits));
        }

        // A crack nobody found stays in one piece, unseen; so does an inclusion.
        [Fact]
        public void Cleave_UnfoundFlaws_GoToOnePieceEach()
        {
            var cfg = Cfg();
            var pieces = CANGemSplitter.Cleave(cfg, 6f, new[] { "crack", "inclusions" }, new[] { "inclusions" },
                new[] { 1f, 1f, 1f }, cfg.Split.Loss, new System.Random(3))!;

            Assert.Equal(1, pieces.Count(p => p.Traits.Contains("crack")));
            Assert.Equal(1, pieces.Count(p => p.Traits.Contains("inclusions")));
            Assert.All(pieces, p => Assert.DoesNotContain("crack", p.Known));
            Assert.Equal(pieces.Single(p => p.Traits.Contains("inclusions")).Known, new[] { "inclusions" });
        }

        [Fact]
        public void Cleave_PieceBelowTheMinimum_IsRefused()
        {
            var cfg = Cfg();
            Assert.Null(CANGemSplitter.Cleave(cfg, 1.5f, new string[0], new string[0], new[] { 1f, 1f }, cfg.Split.Loss, new System.Random(1)));
        }

        [Fact]
        public void Pieces_TooSmall_OrOnePiece_IsNull()
        {
            Assert.Null(CANGemSplitter.Pieces(0.8f, new[] { 0.5f, 0.5f }, 0.1f, 0.1f, 0.4f));
            Assert.Null(CANGemSplitter.Pieces(3f, new[] { 1f }, 0.1f, 0.1f, 0.3f));
            Assert.Null(CANGemSplitter.Pieces(3f, null, 0.1f, 0.1f, 0.3f));
            Assert.Null(CANGemSplitter.Pieces(3f, new[] { 1f, 0f }, 0.1f, 0.1f, 0.3f));
        }
    }
}
