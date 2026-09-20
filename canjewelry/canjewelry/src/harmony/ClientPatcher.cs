using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace canjewelry.src.harmony
{
    public static class ClientPatcher
    {
        public static void ApplyPatches(ICoreClientAPI capi, string harmonyID, ref Harmony harmonyInstance)
        {
            harmonyInstance = new Harmony(harmonyID + "_client");

            harmonyInstance.Patch(typeof(Vintagestory.API.Client.GuiElementItemSlotGridBase).GetMethod("ComposeSlotOverlays", BindingFlags.NonPublic | BindingFlags.Instance), transpiler: new HarmonyMethod(typeof(harmPatch).GetMethod("Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage")));

            harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("GetHeldItemInfo"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_GetHeldItemInfo")));

            harmonyInstance.Patch(typeof(CharacterSystem).GetMethod("StartClientSide"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_CharacterSystem_StartClientSide")));

            harmonyInstance.Patch(typeof(ItemChisel).GetMethod("OnHeldAttackStart"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemChisel_OnHeldAttackStart")));

            // Gems on worn gear. The static overload is the one every piece of gear goes through,
            // named by its parameter types because the instance overloads share the name.
            // Picked by shape rather than by an exact type list: the signature has changed between
            // game versions, and a miss here would silently cost every gem on worn gear.
            MethodInfo addGearToShape = typeof(EntityBehaviorContainer)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "addGearToShape"
                                     && m.GetParameters().Any(p => p.ParameterType == typeof(ItemStack)));
            if (addGearToShape != null)
            {
                harmonyInstance.Patch(addGearToShape,
                    postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_EntityBehaviorContainer_addGearToShape")));
                capi.Logger.Notification("[canjewelry] patched {0} for gems on worn gear", addGearToShape);
            }
            else
            {
                capi.Logger.Warning("[canjewelry] EntityBehaviorContainer.addGearToShape not found, gems will not show on worn gear");
            }

            // The DropMouseSlotItems prefix that used to sit here existed only to stop the ImGui
            // inventory grid from throwing the held stack into the world. The native slot grid
            // handles the mouse itself, so the patch is gone with it.
        }
    }
}
