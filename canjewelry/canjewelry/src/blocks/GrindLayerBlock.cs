using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canjewelry.src.blocks
{
    public class GrindLayerBlock : Item
    {
        public override int GetItemDamageColor(ItemStack itemstack)
        {
            int maxDurability = GetMaxDurability(itemstack);
            if (maxDurability == 0)
            {
                return 0;
            }

            int num = GameMath.Clamp(100 * itemstack.Collectible.GetRemainingDurability(itemstack) / maxDurability, 0, 99);
            return GuiStyle.DamageColorGradient[num];
        }
    }
}
