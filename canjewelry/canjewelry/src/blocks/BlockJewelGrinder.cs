using canjewelry.src.jewelry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
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
            tryConnect(world, byPlayer, blockSel.Position, BlockFacing.DOWN);
            return num != 0;
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos) => true;

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
                byPlayer.InventoryManager.ActiveHotbarSlot.TryFlipWith(byPlayer.InventoryManager.ActiveHotbarSlot);
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
                blockEntity.SetPlayerGrinding(byPlayer, false);
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

        public override bool HasMechPowerConnectorAt(
          IWorldAccessor world,
          BlockPos pos,
          BlockFacing face)
        {
            return face == BlockFacing.DOWN;
        }
    }
}
