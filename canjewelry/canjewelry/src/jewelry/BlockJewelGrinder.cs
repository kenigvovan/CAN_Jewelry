using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent.Mechanics;

namespace canjewelry.src.jewelry
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
            this.tryConnect(world, byPlayer, blockSel.Position, BlockFacing.DOWN);
            return num != 0;
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos) => true;

        public override bool OnBlockInteractStart(
          IWorldAccessor world,
          IPlayer byPlayer,
          BlockSelection blockSel)
        {
            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEJewelGrinder blockEntity) || !blockEntity.CanGrind() || blockSel.SelectionBoxIndex != 1 && !blockEntity.Inventory.openedByPlayerGUIds.Contains(byPlayer.PlayerUID))
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            blockEntity.SetPlayerGrinding(byPlayer, true);
            return true;
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
                return new WorldInteraction[1]
                {
          new WorldInteraction()
          {
            ActionLangCode = "blockhelp-quern-addremoveitems",
            MouseButton = EnumMouseButton.Right
          }
                }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
            return new WorldInteraction[1]
            {
        new WorldInteraction()
        {
          ActionLangCode = "blockhelp-quern-grind",
          MouseButton = EnumMouseButton.Right,
          ShouldApply = (InteractionMatcherDelegate) ((wi, bs, es) => world.BlockAccessor.GetBlockEntity(bs.Position) is BEJewelGrinder blockEntity && blockEntity.CanGrind())
        }
            }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
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
