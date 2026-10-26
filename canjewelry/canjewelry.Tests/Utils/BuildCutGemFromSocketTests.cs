using canjewelry.src;
using canjewelry.src.utils;
using canjewelry.Tests.Stubs;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace canjewelry.Tests.Utils
{
    /// <summary>
    /// The gem a socket gives back on extraction, on breaking and in the Jeweler's Set preview.
    /// Sockets written by older versions lack some of the keys and must still give their gem back.
    /// </summary>
    public class BuildCutGemFromSocketTests
    {
        private static ITreeAttribute Socket(string gemType = "ruby", int size = 2)
        {
            ITreeAttribute socket = new TreeAttribute();
            socket.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, 3);
            socket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, gemType);
            socket.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, size);
            return socket;
        }

        private static ItemStack FreshGem() => new ItemStack(new TestItem());

        // ── Item code ──────────────────────────────────────────────────

        [Theory]
        [InlineData(1, "canjewelry:gem-cut-normal-ruby")]
        [InlineData(2, "canjewelry:gem-cut-flawless-ruby")]
        [InlineData(3, "canjewelry:gem-cut-exquisite-ruby")]
        public void Code_FollowsSocketSize(int size, string code)
        {
            Assert.Equal(code, CANGemSize.CutGemCodeFromSocket(Socket(size: size)).ToString());
        }

        [Fact]
        public void Code_OfOddLegacySize_IsNormal_AsExtractionAlwaysGave()
        {
            Assert.Equal("canjewelry:gem-cut-normal-ruby", CANGemSize.CutGemCodeFromSocket(Socket(size: 0)).ToString());
        }

        [Fact]
        public void Code_IsNull_ForEmptySocket()
        {
            Assert.Null(CANGemSize.CutGemCodeFromSocket(Socket(gemType: "")));
            Assert.Null(CANGemSize.CutGemCodeFromSocket(null!));
        }

        [Fact]
        public void BuildCutGem_IsNull_WithoutWorldOrSocket()
        {
            Assert.Null(CANGemSize.BuildCutGemFromSocket(null!, Socket()));
        }

        // ── Cut gem tree ───────────────────────────────────────────────

        [Fact]
        public void Tree_CopiesCutAndBuffs()
        {
            var socket = Socket();
            socket.SetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_BAGUETTE);
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { "walkspeed", "hungerrate" });
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { 0.05f, -0.02f });

            ITreeAttribute tree = CANGemSize.BuildCutGemTreeFromSocket(socket);

            Assert.Equal(CANJWConstants.CUTTING_BAGUETTE, tree.GetString(CANJWConstants.CUTTING_TYPE));
            Assert.Equal(new[] { "walkspeed", "hungerrate" }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)!.value);
            Assert.Equal(new[] { 0.05f, -0.02f }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value);
        }

        [Fact]
        public void Tree_BuffArrays_AreCopies_NotSharedWithSocket()
        {
            var socket = Socket();
            float[] values = { 0.05f };
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { "walkspeed" });
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(values);

            ITreeAttribute tree = CANGemSize.BuildCutGemTreeFromSocket(socket);
            (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value[0] = 1f;

            Assert.Equal(0.05f, values[0]);
        }

        [Fact]
        public void Tree_OfLegacySocket_DefaultsToRoundCut_FullyGround_NoBuffs()
        {
            ITreeAttribute tree = CANGemSize.BuildCutGemTreeFromSocket(Socket());

            Assert.Equal(CANJWConstants.CUTTING_ROUND, tree.GetString(CANJWConstants.CUTTING_TYPE));
            Assert.True(tree.GetBool(CANJWConstants.GEM_FULL_PROCESSED));
            Assert.False(tree.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Tree_KeepsGrindingFlag(bool ground)
        {
            var socket = Socket();
            socket.SetBool(CANJWConstants.WAS_GROUND_BEFORE, ground);

            Assert.Equal(ground, CANGemSize.BuildCutGemTreeFromSocket(socket).GetBool(CANJWConstants.GEM_FULL_PROCESSED));
        }

        // ── Whole stack ────────────────────────────────────────────────

        [Fact]
        public void Apply_RestoresUnfinishedGrinding()
        {
            var socket = Socket();
            socket.SetBool(CANJWConstants.WAS_GROUND_BEFORE, false);
            ITreeAttribute grind = new TreeAttribute();
            grind.SetInt("grindtype", 2);
            socket["savedGrindLayerInfo"] = grind;

            ItemStack gem = FreshGem();
            CANGemSize.ApplySocketToCutGem(gem, socket);

            Assert.Equal(2, gem.Attributes.GetTreeAttribute("cangrindlayerinfo").GetInt("grindtype"));
        }

        [Fact]
        public void Apply_IgnoresSavedGrinding_OfFullyGroundGem()
        {
            var socket = Socket();
            socket.SetBool(CANJWConstants.WAS_GROUND_BEFORE, true);
            socket["savedGrindLayerInfo"] = new TreeAttribute();

            ItemStack gem = FreshGem();
            CANGemSize.ApplySocketToCutGem(gem, socket);

            Assert.False(gem.Attributes.HasAttribute("cangrindlayerinfo"));
        }

        [Fact]
        public void Apply_CarriesWasExtracted()
        {
            var socket = Socket();
            socket.SetBool("wasExtracted", true);

            ItemStack gem = FreshGem();
            CANGemSize.ApplySocketToCutGem(gem, socket);

            Assert.True(gem.Attributes.GetBool("wasExtracted"));
        }

        [Fact]
        public void Apply_FreshGem_IsNotMarkedExtracted()
        {
            ItemStack gem = FreshGem();
            CANGemSize.ApplySocketToCutGem(gem, Socket());

            Assert.False(gem.Attributes.HasAttribute("wasExtracted"));
            Assert.NotNull(gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE));
        }

        // A gem rebuilt from a socket must be a gem the encrust path accepts again: it reads the
        // cut gem tree and both buff arrays.
        [Fact]
        public void Apply_GivesTreeEncrustAccepts()
        {
            var socket = Socket();
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { "walkspeed" });
            socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { 0.05f });

            ItemStack gem = FreshGem();
            CANGemSize.ApplySocketToCutGem(gem, socket);

            ITreeAttribute tree = gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            Assert.NotNull(tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute);
            Assert.NotNull(tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute);
        }
    }
}
