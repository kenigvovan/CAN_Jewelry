using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace canjewelry.src.jewelry
{
    public class JewelerSetBlock: Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is JewelerSetBE blockEntity))
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            blockEntity.OnPlayerRightClick(byPlayer, blockSel);
            return true;
        }
    }
}
