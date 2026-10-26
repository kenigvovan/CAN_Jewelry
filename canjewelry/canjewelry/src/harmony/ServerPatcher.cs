using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace canjewelry.src.harmony
{
    public static class ServerPatcher
    {
        public static void ApplyPatches(ICoreServerAPI sapi, string harmonyID, ref Harmony harmonyInstance)
        {
            harmonyInstance = new Harmony(harmonyID + "_server");
            if (!canjewelry.config.TurnOffBuffs)
            {
                harmonyInstance.Patch(typeof(Vintagestory.Server.CoreServerEventManager).GetMethod("TriggerAfterActiveSlotChanged"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_TriggerAfterActiveSlotChanged")));
            }
            harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("DestroyItem", new Type[] { typeof(IWorldAccessor), typeof(Entity), typeof(ItemSlot) }), prefix: new HarmonyMethod(typeof(harmPatch).GetMethod("Prefix_CollectibleObject_DestroyItem")));

            harmonyInstance.Patch(typeof(ServerWorldPlayerData).GetMethod("ToPacketForOtherPlayers"), transpiler: new HarmonyMethod(typeof(harmPatch).GetMethod("Transpiler_ServerWorldPlayerData_ToPacketForOtherPlayers")));

        }
    }
}
