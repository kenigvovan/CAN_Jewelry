using canjewelry.src;
using canjewelry.src.CB;
using canjewelry.Tests.Helpers;
using System;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Xunit;

namespace canjewelry.Tests.EncrustableCB
{
    // Alias to avoid ambiguity between the canjewelry namespace and the canjewelry class
    using ModMain = canjewelry.src.canjewelry;

    public class ApplyCuttingBuffTests : IDisposable
    {
        private const string GemType      = "diamond";
        private const int    GemTier      = 1;
        private const string MainBuff     = "canspeed";
        private const string SecondaryBuff = "candamage";

        public ApplyCuttingBuffTests()
        {
            // Single-value ranges → main always 0.15, the secondary stat's own entry always 0.05
            ModMain.config = ItemStackBuilder.BuildMinimalConfig(GemType, GemTier, MainBuff, SecondaryBuff);
        }

        public void Dispose()
        {
            ModMain.config = null;
        }

        // ── Happy path ────────────────────────────────────────────────────

        [Fact]
        public void RoundCut_SetsSingleBuff_WithCorrectValue()
        {
            var stack = ItemStackBuilder.CreateUnprocessedCutGem(GemType, GemTier, "round");

            src.CB.EncrustableCB.ApplyCuttingBuff(stack);

            var (names, values) = ReadBuffsFromTree(stack);
            Assert.Single(names);
            Assert.Equal(MainBuff, names[0]);
            Assert.Equal(0.15f, values[0]);
        }

        [Fact]
        public void PearCut_SetsSingleBuff_WithCorrectValue()
        {
            var stack = ItemStackBuilder.CreateUnprocessedCutGem(GemType, GemTier, "pear");

            src.CB.EncrustableCB.ApplyCuttingBuff(stack);

            var (names, _) = ReadBuffsFromTree(stack);
            Assert.Single(names);
            Assert.Equal(MainBuff, names[0]);
        }

        [Fact]
        public void BaguetteCut_SetsTwoBuffs()
        {
            var stack = ItemStackBuilder.CreateUnprocessedCutGem(GemType, GemTier, "baguette");

            src.CB.EncrustableCB.ApplyCuttingBuff(stack);

            var (names, values) = ReadBuffsFromTree(stack);
            Assert.Equal(2, names.Length);
            Assert.Equal(MainBuff, names[0]);
            Assert.Equal(SecondaryBuff, names[1]);
            Assert.Equal(0.15f, values[0]);
            Assert.Equal(0.05f, values[1], precision: 4);
        }

        [Fact]
        public void GemTypeNotInPossibleBuffs_SetsEmptyArrays_DoesNotCrash()
        {
            var stack = ItemStackBuilder.CreateUnprocessedCutGem("unknowngem", GemTier, "round");

            src.CB.EncrustableCB.ApplyCuttingBuff(stack);

            var (names, _) = ReadBuffsFromTree(stack);
            Assert.Empty(names);
        }

        [Fact]
        public void NoCutGemTree_DoesNothing()
        {
            var item = new Stubs.TestItem();
            var stack = new Vintagestory.API.Common.ItemStack(item);
            // stack.Attributes has no CUT_GEM_TREE

            var ex = Record.Exception(() => src.CB.EncrustableCB.ApplyCuttingBuff(stack));

            Assert.Null(ex);
            Assert.False(stack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE));
        }

        // ── Bug cases — currently crash, after шаг 4 should not ──────────

        [Fact]
        public void UnknownCuttingType_DoesNotCrash()
        {
            // CuttingAttributesDict has no "unknown_cut" key → TryGetValue returns null
            // Currently: NullReferenceException. After fix: silent return.
            var stack = ItemStackBuilder.CreateUnprocessedCutGem(GemType, GemTier, "unknown_cut");

            var ex = Record.Exception(() => src.CB.EncrustableCB.ApplyCuttingBuff(stack));

            Assert.Null(ex);
        }

        [Fact]
        public void BaguetteCut_EmptySecondaryStats_SetsSingleBuff_DoesNotCrash()
        {
            // PossibleSecondaryStats is empty → Random.Next(0) throws ArgumentOutOfRangeException
            // Currently: crash. After fix: should fall back to single buff.
            ModMain.config = ItemStackBuilder.BuildMinimalConfig(GemType, GemTier, MainBuff, secondaryBuff: "");
            var stack = ItemStackBuilder.CreateUnprocessedCutGem(GemType, GemTier, "baguette");

            var ex = Record.Exception(() => src.CB.EncrustableCB.ApplyCuttingBuff(stack));

            Assert.Null(ex);
            if (ex == null)
            {
                var (names, _) = ReadBuffsFromTree(stack);
                Assert.Single(names); // only the primary buff
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static (string[] names, float[] values) ReadBuffsFromTree(
            Vintagestory.API.Common.ItemStack stack)
        {
            var tree   = stack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            var names  = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES]  as StringArrayAttribute)!.value;
            var values = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value;
            return (names, values);
        }
    }
}
