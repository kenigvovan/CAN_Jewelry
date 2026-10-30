using canjewelry.src.be;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace canjewelry.src.cb
{
    /// <summary>
    /// Sneak + right click with a gem on the top of a solid block puts down a heap of gems there
    /// (canjewelry:cangempile) with this gem in it. Adding to an existing heap is the heap's own
    /// interaction.
    /// </summary>
    public class CANGemPileableCB : CollectibleBehavior
    {
        public CANGemPileableCB(CollectibleObject collObj) : base(collObj) { }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (!firstEvent || blockSel == null || !byEntity.Controls.ShiftKey || blockSel.Face != BlockFacing.UP) return;

            IWorldAccessor world = byEntity.World;
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null) return;

            BlockPos below = blockSel.Position;
            BlockPos at = below.UpCopy();

            Block pileBlock = world.GetBlock(new AssetLocation("canjewelry:cangempile"));
            if (pileBlock == null) return;

            // Not on the gem workstations: sneak + click with a gem is how they are used.
            Block belowBlock = world.BlockAccessor.GetBlock(below);
            if (IsWorkstation(belowBlock)) return;

            // Only on a solid top, into an empty spot, where the player may build.
            if (!belowBlock.SideSolid[BlockFacing.UP.Index]) return;
            if (world.BlockAccessor.GetBlock(at).Replaceable < 6000) return;
            if (!world.Claims.TryAccess(player, at, EnumBlockAccessFlags.BuildOrBreak)) return;

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;
            if (world.Side != EnumAppSide.Server) return;

            world.BlockAccessor.SetBlock(pileBlock.BlockId, at);
            if (world.BlockAccessor.GetBlockEntity(at) is CANBEGemPile pile && !pile.TryAdd(player, slot))
            {
                world.BlockAccessor.SetBlock(0, at);
            }
        }

        private static bool IsWorkstation(Block block)
            => block is blocks.BlockGemCuttingTable
            || block is blocks.BlockJewelGrinder
            || block is blocks.CANWireDrawingBench
            || block is blocks.CANBlockPan
            || block is jewelry.JewelerSetBlock;
    }
}
