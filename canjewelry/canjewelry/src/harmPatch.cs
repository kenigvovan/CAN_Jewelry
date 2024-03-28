using Cairo;
using canjewelry.src.CB;
using canjewelry.src.items;
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
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace canjewelry.src
{
    [HarmonyPatch]
    public class harmPatch
    {

        /***
          
         Comment were mostly written after some time after code was added, so at some points I had to guess what I meant before or rewrite some too hideous parts.
         Enter at your own risk.
         
         ***/

        /*
          
         Add or remove buff on player, buff type and value are taken from tree attribute and applied on ep.
          
         */
        public static void applyBuffFromItemStack(ITreeAttribute socketSlot, EntityPlayer ep, bool add)
        {
            if (!socketSlot.HasAttribute("attributeBuff"))
            {
                return;
            }

            float additionalValue = socketSlot.GetFloat("attributeBuffValue");
            string attributeBuffName = socketSlot.GetString("attributeBuff");
            float blendedStatValue = ep.Stats[attributeBuffName].GetBlended();
            canjewelry.config.max_buff_values.TryGetValue(attributeBuffName, out float buffThreshold);

            if (!ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrusted"))
            {
                ep.Stats.Set(attributeBuffName, "canencrusted", 0, true);
            }

            if (add)
            {
                //overflow already present just add to neg part and standard part
                if(ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                    if (additionalValue > 0)
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", (ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value) - additionalValue, true);
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", (ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value) + additionalValue, true);
                    }
                    //ep.Stats.Set(attributeBuffName, "canencrustedneg", ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value + additionalValue, true);
                }
                //no neg part, add additional and add neg difference
                else if (buffThreshold != 0 && additionalValue > 0 ?  Math.Abs(ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) > buffThreshold : (ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) < buffThreshold)
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                    if (additionalValue > 0)
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", -((ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value) - buffThreshold), true);
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", -(((ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value) - buffThreshold)), true);
                    }
                }
                //no neg part and under threshold
                else
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                }
            }
            else
            {
                //overflow already present
                if (ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value - additionalValue, true);
                    if (additionalValue > 0 ? ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value - additionalValue <= 0
                                            : ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value + additionalValue <= 0)
                    {
                        ep.Stats[attributeBuffName].Remove("canencrustedneg");
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value - additionalValue, true);
                    }
                }
                else
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value - additionalValue, true);
                }
            }

            //ep.Stats[attributeBuffName].Remove("canencrustedneg");

            //canjewelry.sapi.SendMessage(ep.Player, 0, add.ToString() + ep.Stats[attributeBuffName].GetBlended().ToString() + attributeBuffName, EnumChatType.Notification);
        }

        /*
         
         Handle right/left click on slot.

         */
        public static void Postfix_ItemSlot_ActivateSlotRightClick(Vintagestory.API.Common.ItemSlot __instance, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (__instance.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }

            //Because if two of them are present then tryFlip is called and we buff/debuff 2 times
            if(__instance.Itemstack != null && sourceSlot.Itemstack != null)
            {
                return;
            }
            if (__instance.Itemstack == null)
            {
                return;
            }
           /* if(sourceSlot.Itemstack == null)
            {
                return;
            }*/

            if (__instance.Itemstack != null && sourceSlot.Itemstack != null)
            {
                if (sourceSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    if (__instance.Inventory.ClassName.Equals("character") && sourceSlot.Inventory.ClassName.Equals("mouse"))
                    {
                        ITreeAttribute encrustTreeHere = sourceSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                        {
                            ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                            if (socketSlot != null)
                            {
                                applyBuffFromItemStack(socketSlot, (sourceSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                            }
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
                        if (!(__instance.Itemstack.Item != null && (__instance.Itemstack.Item is ItemWearable || __instance.Itemstack.Item is CANItemWearable)))
                        {
                            ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                            {
                                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                                applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                            }
                        }
                    }
                }
                else if (__instance.Inventory.ClassName.Equals("character"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                        }
                    }
                }
                else
                {
                    ITreeAttribute encrustTree = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                }
            }
        }

        public static void Postfix_ItemSlot_ActivateSlotLeftClick(Vintagestory.API.Common.ItemSlot __instance, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (!(op.MovedQuantity > 0))
            {
                return;
            }
            Postfix_ItemSlot_ActivateSlotRightClick(__instance, sourceSlot, ref op);
        }

       
        //Something dropped from inventory if it s hotbar we change only if it was activeslot, if there is character inventory we "-" buff, if another inventory we just ignore

        /*

         Triggered when we have item in mouse slot and click on another slot, point with mouse on one slot and press hotbar button (even active one)

        */
        public static void Postfix_ItemSlot_TryFlipWith(Vintagestory.API.Common.ItemSlot __instance, ItemSlot itemSlot, ref bool __result)
        {
            //__instance from
            //itemSlot to
            if(!__result)
            {
                return;
            }

            if (__instance.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }


            //in - itemSlot
            //out - __instance

            if (!__instance.Inventory.ClassName.Equals("character") && !__instance.Inventory.ClassName.Equals("hotbar") && !itemSlot.Inventory.ClassName.Equals("character") && !itemSlot.Inventory.ClassName.Equals("hotbar"))
            {
                return;
            }

            int newItemInSlotId = itemSlot.Inventory.GetSlotId(itemSlot);
            int oldItemInSlotId = __instance.Inventory.GetSlotId(__instance);
            /*int activeSlotId = (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber;
            bool newItemBuffApplied = false;*/


            //we got new item in wearable slot of character
            if (itemSlot.Inventory.ClassName.Equals("character"))
            {
                if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, true);
                        }
                    }
                }
                if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                }
            }
            else if (__instance.Inventory.ClassName.Equals("character"))
            {
                if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                }
                if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                        }
                    }
                }
            }
            //click with item in mouse on slot in hotbar
            else if (itemSlot.Inventory.ClassName.Equals("mouse") && __instance.Inventory.ClassName.Equals("hotbar"))
            {
                int activeSlotId = (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber;
                if (activeSlotId == oldItemInSlotId)
                {
                    if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted") && !(itemSlot.Itemstack.Item is ItemWearable || itemSlot.Itemstack.Item is CANItemWearable))
                    {
                        ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                        {
                            ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                            applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                    if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted") && !(__instance.Itemstack.Item is ItemWearable || __instance.Itemstack.Item is CANItemWearable))
                    {
                        ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                        {
                            ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                        }
                    }
                }
            }

            else if (itemSlot.Inventory.ClassName.Equals("mouse"))
            {
                if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
            }
            else if (__instance.Inventory.ClassName.Equals("xskillshotbar"))
            {

                int activeSlotId = (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber;
                //if (!__instance.Empty)
                {
                    if (activeSlotId == oldItemInSlotId)
                    {
                        if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
                        {
                            ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                            {
                                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                                applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                            }
                        }
                    }
                }
                //else
                {
                    if (activeSlotId == oldItemInSlotId)
                    {
                        if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                        {
                            ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                            {
                                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                                applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, true);
                            }
                        }
                    }
                }
            }
            else if (itemSlot.Inventory.ClassName.Equals("hotbar"))
            {
                if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted") && !(itemSlot.Itemstack.Item is ItemWearable || itemSlot.Itemstack.Item is CANItemWearable))
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (itemSlot.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted") && !(__instance.Itemstack.Item is ItemWearable || __instance.Itemstack.Item is CANItemWearable))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
            }
            else if (__instance.Inventory.ClassName.Equals("hotbar"))
            {
                if (itemSlot.Itemstack != null && itemSlot.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = itemSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < itemSlot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        //if it is chect there is no player there
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                    }
                }
                if (__instance.Itemstack != null && __instance.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, true);
                    }
                }
            }


            return;
        }

        //Active slot item can have canecrusted attribute and buffs player, so we need to know when he change holding item

        /*
        
         Catch active slot changed, also it catches moment when active change and we also flip slots. At that moment try_flip patch called as well
        
         */
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
                if (!(playerHotbar[fromSlot].Itemstack.Item != null && (playerHotbar[fromSlot].Itemstack.Item is ItemWearable || playerHotbar[fromSlot].Itemstack.Item is CANItemWearable)))
                {
                    ITreeAttribute encrustTreeHere = playerHotbar[fromSlot].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    if (encrustTreeHere == null)
                    {
                        return;
                    }
                    for (int i = 0; i < encrustTreeHere.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, player.Entity, false);
                        }
                    }
                }
            }
            if(toSlot < 11 && playerHotbar[toSlot].Itemstack != null && playerHotbar[toSlot].Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                if (!(playerHotbar[toSlot].Itemstack.Item != null && (playerHotbar[toSlot].Itemstack.Item is ItemWearable || playerHotbar[toSlot].Itemstack.Item is CANItemWearable)))
                {
                    ITreeAttribute encrustTree = playerHotbar[toSlot].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    if (encrustTree == null)
                    {
                        return;
                    }
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, player.Entity, true);
                        }
                    }
                }
            }
        }

        //Something dropped from inventory if it s hotbar we change only if it was activeslot, if there is character inventory we "-" buff, if another inventory we just ignore
        /*
         
         Catch droped item event or taken from slot.
         If it s hotbar we change only if it was activeslot
         If there is character inventory we "-" buff, if another inventory we just ignore

         */
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

                if (!(__instance.Itemstack.Item != null && (__instance.Itemstack.Item is ItemWearable || __instance.Itemstack.Item is CANItemWearable)))
                {
                    if (__instance.Inventory.GetSlotId(__instance) == (__instance.Inventory as InventoryBasePlayer).Player.InventoryManager.ActiveHotbarSlotNumber)
                    {
                        ITreeAttribute encrustTreeHere = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
                        {
                            ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());
                            applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                        }
                    }
                }
                return;
            }
            ITreeAttribute encrustTree = __instance.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            for (int i = 0; i < __instance.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
            {
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                if (socketSlot != null)
                {
                    applyBuffFromItemStack(socketSlot, (__instance.Inventory as InventoryBasePlayer).Player.Entity, false);
                }
                // (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", (__instance.Inventory as InventoryBasePlayer).Player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value - socketSlot.GetFloat("attributeBuffValue"), true);
            }
        }


        /*

         We took new itemstack and check the same way as for Postfix_ItemSlot_TakeOut, hotbar for activeslot, character invetory

        */
        public static void Postfix_ItemSlot_TryPutInto(Vintagestory.API.Common.ItemSlot __instance, ItemSlot sinkSlot, ref ItemStackMoveOperation op)
        {

            if(sinkSlot.Inventory == null)
            {
                return;
            }
            if (sinkSlot != null && sinkSlot.Inventory.Api.Side == EnumAppSide.Client)
            {
                return;
            }
            if(sinkSlot.Itemstack == null)
            { 
                return; 
            }
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

                if (!(sinkSlot.Itemstack.Item != null && (sinkSlot.Itemstack.Item is ItemWearable || sinkSlot.Itemstack.Item is CANItemWearable)))
                {
                    ITreeAttribute encrustTree = sinkSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < encrustTree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                        if (socketSlot != null)
                        {
                            applyBuffFromItemStack(socketSlot, (sinkSlot.Inventory as InventoryBasePlayer).Player.Entity, true);
                        }
                    }
                }
            }
        }

        /*
        
         Used in transplier Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage for GuiElementItemSlotGridBase.ComposeSlotOverlays()
         to draw color rhombus
        
         */
        public static void addSocketsOverlaysNotDrawItemDamage(ElementBounds[] slotBounds, int slotIndex, ItemSlot slot, LoadedTexture[] slotQuantityTextures, ImageSurface textSurface, Context context)
        {
            var unsSlotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            if(textSurface == null)
            {
                textSurface = new ImageSurface(0, (int)slotBounds[slotIndex].InnerWidth, (int)slotBounds[slotIndex].InnerHeight);
                context = new Context(textSurface);
            }
            
            ITreeAttribute encrustTree = slot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }

            for (int i = 0; i < slot.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(); i++)
            {
                
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                if (socketSlot == null || socketSlot.GetString("gemtype").Equals(""))
                {
                    continue;
                }

                if(!canjewelry.gems_textures.TryGetValue(socketSlot.GetString("gemtype"), out string assetPath))
                {
                    continue;
                }
                AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;
                /*AssetLocation asset = canjewelry.capi.Assets.TryGet("game:textures/block/stone/gem/" + socketSlot.GetString("gemtype") + ".png")?.Location;*/
                /*if (canjewelry.capi.Assets.TryGet("game:textures/block/stone/gem/" + socketSlot.GetString("gemtype") + ".png") == null)
                {
                    asset = canjewelry.capi.Assets.TryGet("game:textures/item/resource/ungraded/" + socketSlot.GetString("gemtype") + ".png")?.Location;
                }*/



                if (asset == null) { continue; }
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, asset, 255);

                double tr = unsSlotSize / 4;
                
                context.NewPath();
                
                context.LineTo((int)GuiElement.scaled(0), (int)GuiElement.scaled(unsSlotSize / 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(0) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 4), (int)GuiElement.scaled(unsSlotSize/ 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(unsSlotSize / 4) + i * (int)GuiElement.scaled(tr));
               
                context.ClosePath();
                
                context.SetSourceSurface(socketSurface, 0, (int)tr * i);

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
        
        public static FieldInfo slotQuantityTexturesSlotGrid = typeof(GuiElementItemSlotGridBase).GetField("slotQuantityTextures", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public static IEnumerable<CodeInstruction> Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            bool found = false;
            bool foundSec = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "addSocketsOverlaysNotDrawItemDamage");
            Label returnLabelNoAttribute = il.DefineLabel();
            Label returnLabelNoAttribute2 = il.DefineLabel();
            for (int i = 0; i < codes.Count; i++)
            {
 
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
                
                for (int i = 0; i < itemstack.ItemAttributes["canhavesocketsnumber"].AsInt(); i++)
                {
                    var treeSlot = tree.GetTreeAttribute("slot" + i);
                    if(treeSlot == null)
                    {
                        continue;
                    }
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
                        ;
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

        //Prefix_GetDrops
        public static void Prefix_GetDrops(Vintagestory.API.Common.Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            var f = 3;

            bool flag = false;
            List<ItemStack> list = new List<ItemStack>();
            BlockBehavior[] blockBehaviors = __instance.BlockBehaviors;
            foreach (BlockBehavior obj in blockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                ItemStack[] drops = obj.GetDrops(world, pos, byPlayer, ref dropQuantityMultiplier, ref handling);
                if (drops != null)
                {
                    list.AddRange(drops);
                }

                switch (handling)
                {
                    case EnumHandling.PreventSubsequent:
                        return;
                    case EnumHandling.PreventDefault:
                        flag = true;
                        break;
                }
            }

            if (flag)
            {
                return;
            }

            if (__instance.Drops == null)
            {
                return;
            }

            List<ItemStack> list2 = new List<ItemStack>();
            for (int j = 0; j < __instance.Drops.Length; j++)
            {
                BlockDropItemStack blockDropItemStack = __instance.Drops[j];
                if (blockDropItemStack.Tool.HasValue && (byPlayer == null || blockDropItemStack.Tool != byPlayer.InventoryManager.ActiveTool))
                {
                    continue;
                }

                float num = 1f;
                if (blockDropItemStack.DropModbyStat != null)
                {
                    num = byPlayer.Entity.Stats.GetBlended(blockDropItemStack.DropModbyStat);
                }

                ItemStack itemStack = __instance.Drops[j].GetNextItemStack(dropQuantityMultiplier * num);
                if (itemStack != null)
                {
                    if (itemStack.Collectible is IResolvableCollectible resolvableCollectible)
                    {
                        DummySlot dummySlot = new DummySlot(itemStack);
                        resolvableCollectible.Resolve(dummySlot, world);
                        itemStack = dummySlot.Itemstack;
                    }

                    list2.Add(itemStack);
                    if (__instance.Drops[j].LastDrop)
                    {
                        break;
                    }
                }
            }

            list2.AddRange(list);
            return;
        }
    }
}
