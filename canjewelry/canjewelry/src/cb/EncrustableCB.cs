using canjewelry.src.inventories;
using canjewelry.src.items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace canjewelry.src.CB
{
    public class EncrustableCB : CollectibleBehavior
    {
        public EncrustableCB(CollectibleObject collObj) : base(collObj)
        {
            
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        public static bool TryAddSocket(InventoryBase inventory, ItemSlot encrustable, ItemSlot socketSlot, int socketNumber)
        {
            inventory.TakeLocked = true;
           // ItemStack encrustable = encrustableSlot.Itemstack;
            if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_NUMBER_STRING))
            {
                //client can send us anything
                if (socketNumber + 1 > encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt())
                {
                    inventory.TakeLocked = false;
                    return false;
                }

                //already has itree -> has socket alteast 1
                if (encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                {
                    var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                    if (tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) >= encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt())
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    else
                    {
                        //if socket is already there, just skip
                        if(tree.HasAttribute("slot" + socketNumber))
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }


                        if (!(socketSlot.Itemstack != null && socketSlot.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.LEVEL_OF_SOSCKET_STRING)))
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }

                        if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_TIERS_STRING))
                        {
                            var tiersList = encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();
                            if(tiersList.Count() < tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER))
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }

                            if (tiersList[socketNumber].AsInt() < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }
                        }

                        tree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) + 1);
                        ITreeAttribute socketSlotTree = new TreeAttribute();
                        socketSlotTree.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
                        socketSlotTree.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                        socketSlotTree.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
                        socketSlot.TakeOut(1);
                        socketSlot.MarkDirty();
                        encrustable.MarkDirty();
                        tree["slot" + socketNumber] = socketSlotTree;
                        inventory.TakeLocked = false;
                        return true;
                    }
                }
                else
                {
                    if (encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt() < 1)
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    if (!(socketSlot.Itemstack != null && socketSlot.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.LEVEL_OF_SOSCKET_STRING)))
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }

                    if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_TIERS_STRING))
                    {
                        var tiersList = encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();


                        if (tiersList[socketNumber].AsInt() < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }
                    }

                    ITreeAttribute socketSlotTree = new TreeAttribute();

                    socketSlotTree.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
                    socketSlotTree.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                    socketSlotTree.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());

                    ITreeAttribute socketEncrusted = new TreeAttribute();
                    socketEncrusted.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, 1);
                    socketEncrusted["slot" + socketNumber] = socketSlotTree;
                    socketSlot.TakeOut(1);
                    socketSlot.MarkDirty();
                    encrustable.Itemstack.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = socketEncrusted;
                    encrustable.MarkDirty();
                    inventory.TakeLocked = false;
                    return true;
                }
            }
            inventory.TakeLocked = false;
            return false;
        }
        public static bool TryToEncrustGemsIntoSockets(InventoryBase inventory, ItemSlot encrustable, ItemSlot gem_slot, int socket_number)
        {
            inventory.TakeLocked = true;
            if (encrustable.Itemstack != null && encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
            {
                if(gem_slot.Empty || !gem_slot.Itemstack.Collectible.Attributes.KeyExists("canGemType"))
                {
                    inventory.TakeLocked = false;
                    return false;
                }
                var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);

                if(tree.HasAttribute("slot" + socket_number))
                {
                    ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + socket_number);
                    //socket level is lower than gem type
                    if (treeSocket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE) < gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt())
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    //item cannot have this gem encrusted
                    if (!canItemContainThisGem(gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last(), encrustable.Itemstack))
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }

                    treeSocket.SetInt("size", gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt());
                    treeSocket.SetString("gemtype", gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last());
                    treeSocket.SetString("attributeBuff", gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString());


                    treeSocket.SetFloat("attributeBuffValue", canjewelry.config.gems_buffs
                        [gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString()][gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
                    if (encrustable.Itemstack.Item is CANItemSimpleNecklace)
                    {
                        encrustable.Itemstack.Attributes.SetString("gem", gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last());
                    }
                    else if (encrustable.Itemstack.Item is CANItemTiara)
                    {
                        encrustable.Itemstack.Attributes.SetString("gem_" + (socket_number + 1), gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last());
                    }

                    gem_slot.TakeOut(1);
                    gem_slot.MarkDirty();
                    encrustable.MarkDirty();
                    inventory.TakeLocked = false;
                    return true;
                }

            }
            inventory.TakeLocked = false;
            return false;
        }
        public static bool canItemContainThisGem(string gemType, ItemStack targetItemStack)
        {
            if (canjewelry.config.buffNameToPossibleItem.TryGetValue(gemType, out var hashSetClasses))
            {
                foreach (var it in hashSetClasses)
                {
                    if (WildcardUtil.Match("*" + it + "*", targetItemStack.Collectible.Code.Path))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
