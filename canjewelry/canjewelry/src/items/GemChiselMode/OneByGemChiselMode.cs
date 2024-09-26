using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace canjewelry.src.items.GemChiselMode
{
    public class OneByGemChiselMode: GemChiselMode
    {
        public override DrawSkillIconDelegate DrawAction(ICoreClientAPI capi) => ItemClay.Drawcreate1_svg;
    }
}
