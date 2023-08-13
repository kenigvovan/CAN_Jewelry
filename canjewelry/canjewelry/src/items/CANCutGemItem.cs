using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canjewelry.src.jewelry
{
    public class CANCutGemItem: Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string buffName = inSlot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString();
            if (buffName.Equals("maxhealthExtraPoints"))
            {
                dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName)).Append(" +" + Config.Current.gems_buffs.Val[buffName][inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
            }
            else
            {
                float buffValue = Config.Current.gems_buffs.Val[buffName][inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()] * 100;
                dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName));
                dsc.Append(buffValue > 0 ? " +" + buffValue + "%" : " " + buffValue + "%");
            }
        }
    }
}
