using System.Linq;
using canjewelry.src.jewelry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent.Mechanics;

namespace canjewelry.src.blocks
{
    public class BlockJewelGrinder : BlockMPBase
    {
        public override bool TryPlaceBlock(
         IWorldAccessor world,
         IPlayer byPlayer,
         ItemStack itemstack,
         BlockSelection blockSel,
         ref string failureCode)
        {
            int num = base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode) ? 1 : 0;
            if (num == 0)
                return num != 0;
            foreach (BlockFacing face in PowerInputFaces)
            {
                if (tryConnect(world, byPlayer, blockSel.Position, face)) break;
            }
            return num != 0;
        }
        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }
        public override bool OnBlockInteractStart(
          IWorldAccessor world,
          IPlayer byPlayer,
          BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEJewelGrinder blockEntity) {

                if (world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
                {
                    if (byPlayer.Entity.ServerControls.CtrlKey)
                    {
                        if (world is IServerWorldAccessor)
                        {
                            if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack == null || byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Item is GrindLayerBlock)
                            {
                                if (byPlayer.InventoryManager.ActiveHotbarSlot.TryFlipWith(blockEntity.inventory[0]))
                                {
                                    blockEntity.MarkDirty(true);
                                    blockEntity.inventory.MarkSlotDirty(0);
                                }
                            }
                        }
                        return true;
                    }
                }

                if (blockEntity.CanGrind() && blockSel.SelectionBoxIndex == 1)
                {
                    blockEntity.SetPlayerGrinding(byPlayer, true);
                    return true;
                }
            }           
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(
          float secondsUsed,
          IWorldAccessor world,
          IPlayer byPlayer,
          BlockSelection blockSel)
        {
            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEJewelGrinder blockEntity) || blockSel.SelectionBoxIndex != 1 && !blockEntity.Inventory.openedByPlayerGUIds.Contains(byPlayer.PlayerUID))
                return false;
            //blockEntity.IsGrinding(byPlayer);
            if (world.Api.Side != EnumAppSide.Client)
            {
                blockEntity.doGrind(byPlayer, secondsUsed);
            }
            else
            {
                blockEntity.StartGrindSound();
                byPlayer.InventoryManager.ActiveHotbarSlot.TryFlipWith(byPlayer.InventoryManager.ActiveHotbarSlot);
                blockEntity.SpawnGrindParticles(byPlayer, secondsUsed);
            }
            return true;
        }

        public override void OnBlockInteractStop(
          float secondsUsed,
          IWorldAccessor world,
          IPlayer byPlayer,
          BlockSelection blockSel)
        {
            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEJewelGrinder blockEntity))
                return;
            if (world.Side == EnumAppSide.Client)
                blockEntity.StopGrindSound();
            blockEntity.SetPlayerGrinding(byPlayer, false);
        }

        public override bool OnBlockInteractCancel(
          float secondsUsed,
          IWorldAccessor world,
          IPlayer byPlayer,
          BlockSelection blockSel,
          EnumItemUseCancelReason cancelReason)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEJewelGrinder blockEntity)
            {
                if (world.Side == EnumAppSide.Client)
                    blockEntity.StopGrindSound();
                blockEntity.SetPlayerGrinding(byPlayer, false);
            }
            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
          IWorldAccessor world,
          BlockSelection selection,
          IPlayer forPlayer)
        {
            if (selection.SelectionBoxIndex == 0)
            {
                return new WorldInteraction[1]
                {
                  new WorldInteraction()
                  {
                    ActionLangCode = "canjewelry:blockhelp-jewelgrinder-addremovelayer",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode="ctrl"
                  }
                }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
            }
            else
            {
                return new WorldInteraction[2]
                {
                    new WorldInteraction()
                    {
                      ActionLangCode = "blockhelp-quern-grind",
                      MouseButton = EnumMouseButton.Right,
                      ShouldApply =  (wi, bs, es) => world.BlockAccessor.GetBlockEntity(bs.Position) is BEJewelGrinder blockEntity && blockEntity.CanGrind()
                    },
                    new WorldInteraction()
                  {
                    ActionLangCode = "canjewelry:blockhelp-jewelgrinder-addremovelayer",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode="ctrl"
                  }
                }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
            }
        }

        public override void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
        {
        }
        // A bevel gear under the pan turns a horizontal axle into the vertical spindle, so any side works.
        private static readonly BlockFacing[] PowerInputFaces =
            { BlockFacing.DOWN, BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST };

        public override bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock)
        {
            // A grinder passes no power on, so two grinders side by side must not link up.
            return face != BlockFacing.UP && !(forBlock is BlockJewelGrinder);
        }
    }
}
