using Cairo;
using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace canjewelry.src
{
    [HarmonyPatch]
    public class harmPatch
    {         
        public static MeshRef quadModel;
        public static float[] pMatrix = Mat4f.Create();
       
        public static void Prefix_AsyncRecompose(Vintagestory.API.Client.GuiElementItemstackInfo __instance)
        {

            if(__instance.curSlot == null || __instance.curSlot.Itemstack == null)
            {
                return;
            }
            
            
            ITreeAttribute encrustTree = __instance.curSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }
            
            ImageSurface textSurface = new ImageSurface(0, (int)GuiElement.scaled(180), (int)GuiElement.scaled(180));
            Context context = new Context(textSurface);
            double tr = GuiElement.scaledi(21);
            for (int p = 0; p < encrustTree.GetInt("socketsnumber"); p++)
            {
                int j = 0;
                int i = p;
                if (i > 1)
                {
                    j = 1;
                    i = 0;
                }
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, socketsTextureDict["socket-" + socketSlot.GetInt("sockettype")], 255);


                context.NewPath();
                context.LineTo(GuiElement.scaledi(3) + i * (tr), (int)GuiElement.scaled(12) + j * tr);
                context.LineTo(GuiElement.scaledi(12) + i * (tr), (int)GuiElement.scaled(3) + j * tr);
                context.LineTo(GuiElement.scaledi(21) + i * (tr), (int)GuiElement.scaled(12) + j * tr);
                context.LineTo(GuiElement.scaledi(12) + i * (tr), (int)GuiElement.scaled(21) + j * tr);
                // tr += 10;
                //context.Translate(tr * i, tr * j);
                context.ClosePath();
               // context.Translate(0, -5);
                context.SetSourceSurface(socketSurface, 0, 0);

                context.FillPreserve();
                context.Dispose();
            }
            LoadedTexture texture = new LoadedTexture(canjewelry.capi);
            canjewelry.capi.Gui.LoadOrUpdateCairoTexture(textSurface, true, ref __instance.texture);
            //canmods.capi.Render.Render2DLoadedTexture(texture, (int)__instance.Bounds.renderX, (int)__instance.Bounds.renderY , 0);
            context.Dispose();
            texture.Dispose();
            textSurface.Dispose();
            return;
        }

        public static void Postfix_ItemSlot_ActivateSlotLeftClick(Vintagestory.API.Common.ItemSlot __instance, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            /*if(!__result)
            {
                return;
            }*/
            if (__instance.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }
            if (__instance.Itemstack == null)
            {
                return;
            }

            if (__instance.Itemstack != null && sourceSlot.Itemstack != null)
            {
                if (sourceSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    if (__instance.Inventory.ClassName.Equals("character") && sourceSlot.Inventory.ClassName.Equals("mouse"))
                {
                    ITreeAttribute encrustTreeHere = sourceSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (sourceSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                return;
                }
            }
            if (!__instance.Inventory.ClassName.Equals("hotbar") && !__instance.Inventory.ClassName.Equals("character"))
            {
                return;
            }
            if (__instance.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                if (__instance.Inventory.ClassName.Equals("hotbar"))
                {
                    if (__instance.Inventory.GetSlotId(__instance) == (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                    {
                        if (!(__instance.Itemstack.Item != null && __instance.Itemstack.Item is ItemWearable))
                        {
                            ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                            {
                                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                                applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                            }
                        }
                    }
                }
                else if(__instance.Inventory.ClassName.Equals("character"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
                else
                {
                    ITreeAttribute encrustTree = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                        // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
                    }
                }
            }
        }

        public static void applyBuffFromItemStack(ITreeAttribute socketSlot, EntityPlayer ep, bool add)
        {
            if (!socketSlot.HasAttribute("attributeBuff"))
            {
                return;
            }
            if (!socketSlot.HasAttribute("attributeBuff"))
            {
                return;
            }
            if (add)
            {
                if (ep.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                {
                    ep.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", ep.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value + socketSlot.GetFloat("attributeBuffValue"), true);
                }
                else
                {
                    ep.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", socketSlot.GetFloat("attributeBuffValue"), true);
                }
            }
            else
            {
                if (ep.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                {
                    ep.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", ep.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
                }
                else
                {
                    ep.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", 0, true);
                }
            }
        }
        //Something dropped from inventory if it s hotbar we change only if it was activeslot, if there is character inventory we "-" buff, if another inventory we just ignore
        public static void Postfix_ItemSlot_TryFlipWith(Vintagestory.API.Common.ItemSlot __instance, ItemSlot itemSlot, ref bool __result)
        {
            //__instance from
            //itemSlot to


            /*
             * we get from armor slot
             * itemSlot(character) is null and __instance(hotbar) has item
             * 
             * we get from hotbar
             * itemSlot has item and __instance is null
            //<--0
            /*if(!__result)
            {
                return;
            }*/
            if (__instance.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }
    
            //__instance - source
            //itemSlot - sink

            //we are interested only in armor slots and hotbar
            if(!__instance.Inventory.ClassName.Equals("hotbar") && !__instance.Inventory.ClassName.Equals("character"))
            {
                return;
            }

            //if we get item with buff
            if(itemSlot.Inventory.ClassName.Equals("character") && itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                {
                    ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                    applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                }
            }
            else if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                if (__instance.Inventory.ClassName.Equals("hotbar"))
                {
                    if (!(__instance.Itemstack.Item != null && __instance.Itemstack.Item is ItemWearable))
                    {
                        if (__instance.Inventory.GetSlotId(__instance) == (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                        {
                            ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                            {
                                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                                applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                            }
                        }
                    }
                }
                else if(__instance.Inventory.ClassName.Equals("character"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
                else
                {
                    ITreeAttribute encrustTree = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                        // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
                    }
                }
            }

            //we can flip with empty slot and take armor from slot
            if(itemSlot.Inventory.ClassName.Equals("character") && __instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                {
                    ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                    applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                }
                return;
            }


            //buff is taken from us
            if(itemSlot.Itemstack == null || !itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                return;
            }

            if (itemSlot.Inventory.ClassName.Equals("hotbar"))
            {
                if (itemSlot.Inventory.GetSlotId(itemSlot) == (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                return;
            }

            if (!(itemSlot.Itemstack.Item != null && itemSlot.Itemstack.Item is ItemWearable))
            {
                ITreeAttribute encrustTreeEncrusted = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 0; i < encrustTreeEncrusted.GetInt("socketsnumber"); i++)
                {
                    ITreeAttribute socketSlot = encrustTreeEncrusted.GetTreeAttribute("slot" + i.ToString());
                    applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                    // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
                }
            }

            /////////////################
            /*if (itemSlot.Itemstack == null || !itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                return;
            }
            if(!(itemSlot.Inventory.ClassName.Equals("hotbar")))
            {
                return;
            }

            if (!(itemSlot.Inventory.ClassName.Equals("hotbar") && !itemSlot.Inventory.ClassName.Equals("character")))
            {
                return;
            }

            if (!itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                return;
            }
            if (itemSlot.Inventory.ClassName.Equals("hotbar"))
            {
                if (itemSlot.Inventory.GetSlotId(itemSlot) == (itemSlot.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                return;
            }
            ITreeAttribute encrustTree = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
            {
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
            }*/
        }

        //Move item little bit futher in slotGrid to be able to draw our own texture which we want to be in front of item texture
        public static Matrixf myReverseMul(Matrixf instance, float[] matrix, ItemSlot slot)
        {
            if(slot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                matrix[14] -= 40;
            }
           
            return instance.ReverseMul(matrix);
        }

        //We call our myReverseMul to be able move texture
        public static IEnumerable<CodeInstruction> Transpiler_RenderToGui(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            bool found2 = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "myReverseMul");

            int c = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                
                if (!found &&
                    codes[i].opcode == OpCodes.Callvirt && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 2].opcode == OpCodes.Callvirt && codes[i + 3].opcode == OpCodes.Ldarg_0 && codes[i - 1].opcode == OpCodes.Callvirt)
                {
                    found = true;
                    c++;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    continue;                  
                }
                if (!found2 &&
                   codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 2].opcode == OpCodes.Ldfld && codes[i + 3].opcode == OpCodes.Ldc_I4_1 && codes[i - 1].opcode == OpCodes.Callvirt)
                {
                    found2 = true;
                    c++;
                }
                yield return codes[i];
            }
        }
        //In RenderItemstackToGui we draw our own sockets textures if attribute is set
        public static void Transpiler_RenderInteractiveElements(Vintagestory.Client.NoObf.InventoryItemRenderer __instance, ItemSlot inSlot,
         double posX,
         double posY,
         double posZ,
         float size)
        {
            if(size > GuiElement.scaled(60))
            {
                return;
            }
            ImageSurface textSurface = new ImageSurface(0, (int)GuiElement.scaled(80), (int)GuiElement.scaled(80));
            Context context = new Context(textSurface);
            ITreeAttribute encrustTree = inSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }
            double width = 48 - GuiElement.scaled(8.0);
            double height = 48;
            double tr = GuiElement.scaledi(21);
            for (int p = 0; p < encrustTree.GetInt("socketsnumber"); p++)
            {
                int j = 0;
                int i = p;
                if (i > 1)
                {
                    j = 1;
                    i = 0;
                }
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, socketsTextureDict["socket-" + socketSlot.GetInt("sockettype")], 255);
               

                context.NewPath();
                context.LineTo(GuiElement.scaledi(3) + i * (tr), (int)GuiElement.scaled(12) + j * tr);
                context.LineTo(GuiElement.scaledi(12) + i * (tr), (int)GuiElement.scaled(3) + j * tr);
                context.LineTo(GuiElement.scaledi(21) + i * (tr), (int)GuiElement.scaled(12) + j * tr);
                context.LineTo(GuiElement.scaledi(12) + i * (tr), (int)GuiElement.scaled(21) + j * tr);
               // tr += 10;
                context.Translate(tr * i, tr * j);
                context.ClosePath();
                context.SetSourceSurface(socketSurface, 0, 0);

                context.FillPreserve();
                context.Translate(-tr * i, -tr * j);
                if (socketSlot.GetInt("size") > 0)
                {
                    if (socketSlot.GetInt("size") == 2)
                    {

                        context.NewPath();
                        context.Arc((int)GuiElement.scaled(12) + i * (tr), (int)GuiElement.scaled(12) + j * tr, (int)GuiElement.scaled(4), 0, 2 * 3.14);
                        context.ClosePath();

                        context.Translate(tr * i, tr * j);
                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, 0, 0);

                        context.Translate(-(tr * i), -tr * j);
                    }
                    else if (socketSlot.GetInt("size") == 1)
                    {
                        context.NewPath();
                        context.LineTo((int)GuiElement.scaled(7) + i * (tr), (int)GuiElement.scaled(15) + j * tr);
                        context.LineTo((int)GuiElement.scaled(12) + i * (tr), (int)GuiElement.scaled(7) + j * tr);
                        context.LineTo((int)GuiElement.scaled(17) + i * (tr), (int)GuiElement.scaled(15) + j * tr);
                        context.ClosePath();
                        context.Translate(i * tr, tr * j);

                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, 0, 0);

                        context.Translate(-i * tr, -tr * j);
                    }
                    else if (socketSlot.GetInt("size") == 3)
                    {
                        context.NewPath();
                        context.LineTo((int)GuiElement.scaled(8) + i * (int)GuiElement.scaled(tr), (int)GuiElement.scaled(8) + j * tr);
                        context.LineTo((int)GuiElement.scaled(15) + i * (int)GuiElement.scaled(tr), (int)GuiElement.scaled(8) + j * tr);
                        context.LineTo((int)GuiElement.scaled(15) + i * (int)GuiElement.scaled(tr), (int)GuiElement.scaled(15) + j * tr);
                        context.LineTo((int)GuiElement.scaled(8) + i * (int)GuiElement.scaled(tr), (int)GuiElement.scaled(15) + j * tr);
                        context.ClosePath();
                        context.Translate(i * tr, tr * j);

                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, 0, 0);

                        context.Translate(-i * tr, -tr * j);
                    }
                    context.FillPreserve();

                }
            }
            if (!inSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                return;
            }
            LoadedTexture texture = new LoadedTexture(canjewelry.capi);
            canjewelry.capi.Gui.LoadOrUpdateCairoTexture(textSurface, true, ref texture);
            canjewelry.capi.Render.Render2DLoadedTexture(texture, (float)(int)(posX - GuiElement.scaledi(22)), (float)(int)(posY - GuiElement.scaledi(22)), (float)((int)posZ));
            context.Dispose();
            texture.Dispose();
            textSurface.Dispose();
            return;
        }
        //Active slot item can have canecrusted attribute and buffs player, so we need to know when he change holding item
        public static void Postfix_TriggerAfterActiveSlotChanged(Vintagestory.Server.CoreServerEventManager __instance, IServerPlayer player,
            int fromSlot,
            int toSlot)
        {
            if(player == null)
            {
                return;
            }
            var playerHotbar = player.InventoryManager.GetHotbarInventory();
            if(playerHotbar == null)
            {
                return;
            }
            if (fromSlot < 11 && playerHotbar[fromSlot].Itemstack != null && playerHotbar[fromSlot].Itemstack.Attributes.HasAttribute("canencrusted"))
            {              
                if (!(playerHotbar[fromSlot].Itemstack.Item != null && playerHotbar[fromSlot].Itemstack.Item is ItemWearable))
                {
                    ITreeAttribute encrustTreeHere = playerHotbar[fromSlot].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    if (encrustTreeHere == null)
                    {
                        return;
                    }
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, player.Entity, false);
                    }
                }
            }
            if(toSlot < 11 && playerHotbar[toSlot].Itemstack != null && playerHotbar[toSlot].Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                if (!(playerHotbar[toSlot].Itemstack.Item != null && playerHotbar[toSlot].Itemstack.Item is ItemWearable))
                {
                    ITreeAttribute encrustTree = playerHotbar[toSlot].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    if (encrustTree == null)
                    {
                        return;
                    }
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, player.Entity, true);
                        //player.Entity.WatchedAttributes.MarkPathDirty("stats");
                    }
                }
            }
        }
        
        //Something dropped from inventory if it s hotbar we change only if it was activeslot, if there is character inventory we "-" buff, if another inventory we just ignore
        public static void Postfix_ItemSlot_TakeOut(Vintagestory.API.Common.ItemSlot __instance, int quantity)
        {
            //<--0
            if(__instance.Inventory == null || __instance.Itemstack == null)
            {
                return;
            }
            if (__instance.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }

            if ((!__instance.Inventory.ClassName.Equals("hotbar") && (!__instance.Inventory.ClassName.Equals("character"))))
            {
                return;
            }

            if (!__instance.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                return;
            }
            if (__instance.Inventory.ClassName.Equals("hotbar"))
            {

                if (!(__instance.Itemstack.Item != null && __instance.Itemstack.Item is ItemWearable))
                {
                    if (__instance.Inventory.GetSlotId(__instance) == (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                    {
                        ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                        {
                            ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                }
                return;
            }
            ITreeAttribute encrustTree = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
            {
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
            }
        }
        //We took new itemstack and check the same way, hotbar for activeslot, character invetory
        public static void Postfix_Collectible_DidModifyItemSlot(Vintagestory.API.Common.ItemSlot __instance, ItemSlot sinkSlot, ref ItemStackMoveOperation op)
        {
            //-->0
            if(sinkSlot.Inventory == null)
            {
                return;
            }
            if (sinkSlot != null && sinkSlot.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }
            if(sinkSlot.Itemstack == null)
            { return; }
            //if slot == null - was taken away
            // slot has item stack - got new item
            if (sinkSlot != null && sinkSlot.Inventory != null && (sinkSlot.Inventory.ClassName.Equals("hotbar") || sinkSlot.Inventory.ClassName.Equals("character")))
            {
                if (!sinkSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    return;
                }
                if(sinkSlot.Inventory.ClassName.Equals("hotbar"))
                {
                    if(sinkSlot.Inventory.GetSlotId(sinkSlot) != (sinkSlot.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                    {
                        return;
                    }
                }

                if (!(sinkSlot.Itemstack.Item != null && sinkSlot.Itemstack.Item is ItemWearable))
                {
                    ITreeAttribute encrustTree = sinkSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());

                        applyBuffFromItemStack(socketSlot, (sinkSlot.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
            }
        }

        //NOT USED
        public static void addSocketsOverlaysNotDrawItemDamage(ElementBounds[] slotBounds, int slotIndex, ItemSlot slot, LoadedTexture[] slotQuantityTextures, ImageSurface textSurface, Context context)
        {
            //memory leak
            var unsSlotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            if(textSurface == null)
            {
                textSurface = new ImageSurface(0, (int)slotBounds[slotIndex].InnerWidth, (int)slotBounds[slotIndex].InnerHeight);
                context = new Context(textSurface);
            }
            //ImageSurface textSurface = new ImageSurface(0, (int)slotBounds[slotIndex].InnerWidth, (int)slotBounds[slotIndex].InnerHeight);
           // Context context = new Context(textSurface);
            ITreeAttribute encrustTree = slot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }
            double width = slotBounds[slotIndex].InnerWidth - GuiElement.scaled(8.0);
            double height = slotBounds[slotIndex].InnerHeight;
            //context.Scale(GuiElement.scaled(0.5), GuiElement.scaled(0.5));
            for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
            {
                
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                if (socketSlot.GetString("gemtype").Equals(""))
                {
                    continue;
                }
                    //i++;
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, harmPatch.preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                //context.Rotate(0.9);
                double tr = unsSlotSize / 4;
                
                context.NewPath();
                context.LineTo((int)GuiElement.scaled(0), (int)GuiElement.scaled(unsSlotSize / 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(0) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 4), (int)GuiElement.scaled(unsSlotSize/ 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(unsSlotSize / 4) + i * (int)GuiElement.scaled(tr));
                //context.Translate(tr * i, 0);
                context.ClosePath();
                context.SetSourceSurface(socketSurface, 0, 0);
                
                context.FillPreserve();
                socketSurface.Dispose();
            }
            canjewelry.capi.Gui.LoadOrUpdateCairoTexture(textSurface, true, ref slotQuantityTextures[slotIndex]);
            context.Dispose();
            textSurface.Dispose();
            return;
        }
        public static MethodInfo GetItemStackFromItemSlot = typeof(ItemSlot).GetMethod("get_Itemstack");
        public static MethodInfo GetAttributesFromItemStack = typeof(ItemStack).GetMethod("get_Attributes");
        public static MethodInfo HasAttributeITreeAttribute = typeof(ITreeAttribute).GetMethod("HasAttribute");

        public static FieldInfo ElementBoundsSlotGrid = typeof(GuiElementItemSlotGridBase).GetField("slotBounds", BindingFlags.NonPublic | BindingFlags.Instance);
        //slotQuantityTextures
        public static FieldInfo slotQuantityTexturesSlotGrid = typeof(GuiElementItemSlotGridBase).GetField("slotQuantityTextures", BindingFlags.NonPublic | BindingFlags.Instance);
        //NOT USED
        public static IEnumerable<CodeInstruction> Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            bool found = false;
            bool foundSec = false;
            bool foundRet = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "addSocketsOverlaysNotDrawItemDamage");
            Label returnLabelNoAttribute = il.DefineLabel();
            Label returnLabelNoAttribute2 = il.DefineLabel();
            for (int i = 0; i < codes.Count; i++)
            {
                /*if(!foundRet && codes[i].opcode == OpCodes.Ldc_I4_0 && codes[i + 1].opcode == OpCodes.Ldarg_0 && codes[i + 2].opcode == OpCodes.Ldfld && codes[i - 1].opcode == OpCodes.Ret)
                {
                    codes[i].labels.Add(returnLabelNoAttribute);
                    foundRet = true;
                }*/
                //!draw
            if (!found && 
                    codes[i].opcode == OpCodes.Ldc_I4_1 && codes[i + 1].opcode == OpCodes.Ret && codes[i + 2].opcode == OpCodes.Ldc_I4_0 && codes[i - 1].opcode == OpCodes.Stelem_Ref)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetItemStackFromItemSlot);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetAttributesFromItemStack);
                    yield return new CodeInstruction(OpCodes.Ldstr, "canencrusted");
                    yield return new CodeInstruction(OpCodes.Callvirt, HasAttributeITreeAttribute);
                    yield return new CodeInstruction(OpCodes.Brfalse_S, returnLabelNoAttribute);

                   
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ElementBoundsSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, slotQuantityTexturesSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Ret);
                    codes[i].labels.Add(returnLabelNoAttribute);
                    found = true;
                }

                if (!foundSec &&
                        codes[i].opcode == OpCodes.Ldloc_2 && codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 2].opcode == OpCodes.Ldloc_1 && codes[i - 1].opcode == OpCodes.Call)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetItemStackFromItemSlot);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetAttributesFromItemStack);
                    yield return new CodeInstruction(OpCodes.Ldstr, "canencrusted");
                    yield return new CodeInstruction(OpCodes.Callvirt, HasAttributeITreeAttribute);
                    yield return new CodeInstruction(OpCodes.Brfalse_S, returnLabelNoAttribute2);

                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ElementBoundsSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, slotQuantityTexturesSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Ret);
                    codes[i].labels.Add(returnLabelNoAttribute2);
                    foundSec = true;
                }
                yield return codes[i];
            }
        }



        public static Dictionary<string, AssetLocation> preparedEncrustedGemsImages;
        public static Dictionary<string, AssetLocation> socketsTextureDict;
        //NOT USED
        public static void addSocketsOverlays(Context context, ItemSlot slot, int slotId, int slotIndex, ElementBounds[] slotBounds)
        {
            return;           
        }
        //NOT USED
        public static IEnumerable<CodeInstruction> Transpiler_ComposeSlotOverlays_Add_Socket_Overlays(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "addSocketsOverlays");

            for (int i = 0; i < codes.Count; i++)
            {
                if (!found &&
                    codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Ldloc_1 && codes[i + 2].opcode == OpCodes.Ldarg_0 && codes[i - 1].opcode == OpCodes.Call)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ElementBoundsSlotGrid);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    found = true;
                }
                yield return codes[i];
            }
        }
        private static LoadedTexture zoomed = new LoadedTexture(canjewelry.capi);
        static MethodInfo dynMethodGenContext = typeof(GuiElementItemSlotGridBase).GetMethod("genContext",
                   BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo dynMethodGenerateTexture = typeof(GuiElementItemSlotGridBase).GetMethod("generateTexture",
                  BindingFlags.NonPublic | BindingFlags.Instance, null,  new Type[] { typeof(ImageSurface), typeof(LoadedTexture).MakeByRefType(), typeof(bool) }, null);

        public static void Postfix_GetHeldItemInfo(Vintagestory.API.Common.CollectibleObject __instance, ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo)
        {
            ItemStack itemstack = inSlot.Itemstack;
            if(itemstack.Attributes.HasAttribute("canencrusted"))
            {
                var tree = itemstack.Attributes.GetTreeAttribute("canencrusted");
                int canHaveNsocketsMore = itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() - tree.GetInt("socketsnumber");
                if (canHaveNsocketsMore > 0)
                {
                    dsc.Append(Lang.Get("canjewelry:item-can-have-n-sockets", canHaveNsocketsMore)).Append("\n");
                }
                
                for (int i = 0; i < tree.GetInt("socketsnumber"); i++)
                {
                    var treeSlot = tree.GetTreeAttribute("slot" + i);
                    dsc.Append(Lang.Get("canjewelry:item-socket-tier", treeSlot.GetAsInt("sockettype")));
                    dsc.Append("\n");
                    if(treeSlot.GetString("gemtype") != "")
                    {
                        if (treeSlot.GetString("attributeBuff").Equals("maxhealthExtraPoints"))
                        {
                            dsc.Append(Lang.Get("canjewelry:socket-has-attribute", i, treeSlot.GetFloat("attributeBuffValue"))).Append(Lang.Get("canjewelry:buff-name-" + treeSlot.GetString("attributeBuff")));
                        }
                        else
                        {
                            dsc.Append(Lang.Get("canjewelry:socket-has-attribute-percent", i, treeSlot.GetFloat("attributeBuffValue") * 100)).Append(Lang.Get("canjewelry:buff-name-" + treeSlot.GetString("attributeBuff")));
                        }
                        dsc.Append('\n');
                    }
                }

            }
            else if(itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists("canhavesocketsnumber") && itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() > 0)
            {
                dsc.Append(Lang.Get("canjewelry:item-can-have-n-sockets", itemstack.ItemAttributes["canhavesocketsnumber"].AsInt()));
                dsc.Append("\n");
            }

            return;
        }
    }
}
