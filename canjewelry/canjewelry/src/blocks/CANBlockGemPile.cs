using canjewelry.src.be;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canjewelry.src.blocks
{
    /// <summary>
    /// The heap of loose gems CANGemPileableCB puts down. It has no item of its own: broken, it
    /// drops the gems; right click with a gem adds one, with an empty hand takes the top one.
    /// </summary>
    public class CANBlockGemPile : Block
    {
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            // Lets the heap take a gem even while sneaking, which is how it was put down.
            PlacedPriorityInteract = true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is CANBEGemPile pile)
            {
                return pile.OnInteract(byPlayer);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
            => [Box(blockAccessor, pos)];

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
            => [Box(blockAccessor, pos)];

        private static Cuboidf Box(IBlockAccessor blockAccessor, BlockPos pos)
        {
            float height = (blockAccessor.GetBlockEntity(pos) as CANBEGemPile)?.Height ?? 0.14f;
            return new Cuboidf(0.25f, 0, 0.25f, 0.75f, height, 0.75f);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not CANBEGemPile pile) return [];

            var drops = new System.Collections.Generic.List<ItemStack>();
            foreach (ItemSlot slot in pile.Inventory)
            {
                if (!slot.Empty) drops.Add(slot.Itemstack.Clone());
            }
            pile.Inventory.Clear();
            return drops.ToArray();
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is CANBEGemPile pile)
            {
                foreach (ItemSlot slot in pile.Inventory)
                {
                    if (!slot.Empty) return slot.Itemstack.GetEmptyClone();
                }
            }
            return null;
        }
    }
}
