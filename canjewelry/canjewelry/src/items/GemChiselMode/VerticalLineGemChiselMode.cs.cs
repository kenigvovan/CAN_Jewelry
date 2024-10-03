using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.items.GemChiselMode
{
    public class VerticalLineGemChiselMode: GemChiselMode
    {
        public override DrawSkillIconDelegate DrawAction(ICoreClientAPI capi) => capi.Gui.Icons.Drawrepeat_svg;
    }
}
