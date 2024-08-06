using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace canjewelry.src.items
{
    public class CANItemWireHank: Item
    {
        public override string GetHeldItemName(ItemStack itemStack)
        {
            string variant = itemStack.Item.Variant.Get("metal");
            return Lang.Get("game:material-" + variant) + Lang.Get("canjewelry:item-wire-hank");
        }
    }
}
