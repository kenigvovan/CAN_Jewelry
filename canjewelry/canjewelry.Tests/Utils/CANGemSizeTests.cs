using canjewelry.src.utils;
using canjewelry.Tests.Stubs;
using Vintagestory.API.Common;
using Xunit;

namespace canjewelry.Tests.Utils
{
    public class CANGemSizeTests
    {
        [Theory]
        [InlineData(1, "normal")]
        [InlineData(2, "flawless")]
        [InlineData(3, "exquisite")]
        public void CutQuality_MapsSizesOneToThree(int size, string quality)
        {
            Assert.Equal(quality, CANGemSize.CutQualityOrNull(size));
            Assert.Equal(quality, CANGemSize.CutQualityClamped(size));
            Assert.Equal(size, CANGemSize.CutSizeOf(quality));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(-1)]
        public void CutQualityOrNull_IsNull_OutsideRange(int size)
        {
            Assert.Null(CANGemSize.CutQualityOrNull(size));
        }

        // The old mappings: extraction and drawing treated anything below 2 as normal and
        // anything from 3 up as exquisite.
        [Theory]
        [InlineData(0, "normal")]
        [InlineData(-5, "normal")]
        [InlineData(4, "exquisite")]
        public void CutQualityClamped_KeepsOldLenientMapping(int size, string quality)
        {
            Assert.Equal(quality, CANGemSize.CutQualityClamped(size));
        }

        [Theory]
        [InlineData(1, "chipped")]
        [InlineData(2, "flawed")]
        [InlineData(3, "normal")]
        public void RoughQuality_MapsSizesOneToThree(int size, string quality)
        {
            Assert.Equal(quality, CANGemSize.RoughQualityOrNull(size));
            Assert.Equal(size, CANGemSize.RoughSizeOf(quality));
        }

        [Fact]
        public void UnknownQuality_HasSizeZero()
        {
            Assert.Equal(0, CANGemSize.CutSizeOf("huge"));
            Assert.Equal(0, CANGemSize.RoughSizeOf(null!));
        }

        [Theory]
        [InlineData("low", "chipped")]
        [InlineData("medium", "flawed")]
        [InlineData("high", "normal")]
        public void Potential_RoundTripsThroughRoughQuality(string potential, string quality)
        {
            Assert.Equal(quality, CANGemSize.RoughQualityOfPotential(potential));
            Assert.Equal(potential, CANGemSize.PotentialOfRoughQuality(quality));
        }

        [Fact]
        public void UnknownPotential_IsNull()
        {
            Assert.Null(CANGemSize.RoughQualityOfPotential("extreme"));
            Assert.Null(CANGemSize.PotentialOfRoughQuality("flawless"));
        }

        // Must give exactly what Code.Path.Split('-').Last() gave, which it replaced.
        [Theory]
        [InlineData("canjewelry:gem-cut-normal-ruby", "ruby")]
        [InlineData("canjewelry:gem-rough-flawed-olivine_peridot", "olivine_peridot")]
        [InlineData("game:diamond", "diamond")]
        [InlineData("canjewelry:gem-cut-normal-", "")]
        public void GemTypeOf_TakesLastCodePart(string code, string gemType)
        {
            var location = new AssetLocation(code);
            Assert.Equal(location.Path.Split('-')[^1], CANGemSize.GemTypeOf(location));
            Assert.Equal(gemType, CANGemSize.GemTypeOf(location));
        }

        [Fact]
        public void GemTypeOf_IsNull_WithoutCode()
        {
            Assert.Null(CANGemSize.GemTypeOf((AssetLocation)null!));
            Assert.Null(CANGemSize.GemTypeOf((CollectibleObject)null!));
        }

        [Fact]
        public void GemTypeOf_ReadsCollectibleCode()
        {
            var item = new TestItem { Code = new AssetLocation("canjewelry:gem-cut-flawless-emerald") };
            Assert.Equal("emerald", CANGemSize.GemTypeOf(item));
        }

        [Fact]
        public void Codes_MatchItemNaming()
        {
            Assert.Equal("canjewelry:gem-cut-flawless-ruby", CANGemSize.CutGemCode("flawless", "ruby").ToString());
            Assert.Equal("canjewelry:gem-rough-chipped-ruby", CANGemSize.RoughGemCode("chipped", "ruby").ToString());
            Assert.Equal("game:gem-ruby-rough", CANGemSize.VanillaRoughGemCode("ruby").ToString());
        }
    }
}
