using canjewelry.src.cb;
using canjewelry.src.inventories;
using canjewelry.src.items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
            int maxSocketNumber = EncrustableCB.GetMaxAmountSockets(encrustable.Itemstack);
            if (encrustable.Itemstack != null && maxSocketNumber > 0)
            {
                //client can send us anything
                if (socketNumber + 1 > maxSocketNumber)
                {
                    inventory.TakeLocked = false;
                    return false;
                }

                //already has itree -> has socket alteast 1
                if (encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                {
                    var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                    if (tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) >= maxSocketNumber)
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    else
                    {
                        //if socket is already there, just skip
                        if (tree.HasAttribute("slot" + socketNumber))
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
                            if (tiersList.Count() < tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER))
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
                    if (maxSocketNumber < 1)
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
                if (gem_slot.Empty || !gem_slot.Itemstack.Collectible.Attributes.KeyExists("canGemType"))
                {
                    inventory.TakeLocked = false;
                    return false;
                }
                var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                var cutGemTree = gem_slot.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
                if(cutGemTree == null)
                {
                    return false;
                }
                if (tree.HasAttribute("slot" + socket_number))
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

                    int currentMaxDurability = encrustable.Itemstack.Collectible.GetMaxDurability(encrustable.Itemstack);
                    int currentDurability = encrustable.Itemstack.Attributes.GetInt("durability", 0);
                    string[] newBuffNames = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                    float[] newBuffValues = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                    string[] currentBuffNames = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
                    float[] currentBuffValues = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;

                    if ((newBuffNames?.Contains("candurability") ?? false) || (currentBuffNames?.Contains("candurability") ?? false) || currentMaxDurability < 1)
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }

                    //Go through current buffs
                    if (currentBuffNames != null)
                    {
                        for (int i = 0; i < currentBuffNames.Length; i++)
                        {
                            string tmpName = currentBuffNames[i];
                            float tmpValue = currentBuffValues[i];

                            //if it is dur buff then remove it's part
                            if (tmpName.Equals("candurability"))
                            {
                                float currentDurabilityBuffOnTree = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                                if (currentDurability > 0)
                                {
                                    currentDurability = (int)((float)currentDurability / (1 + tmpValue));
                                    //currentDurability = 1;
                                    encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                                }

                                currentDurabilityBuffOnTree -= tmpValue;

                                if (currentDurabilityBuffOnTree == 0)
                                {
                                    tree.RemoveAttribute(CANJWConstants.CANDURABILITY_STRING);
                                }
                                else
                                {
                                    tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuffOnTree);
                                }
                            }
                        }
                    }
                    for (int i = 0; i < newBuffNames.Length; i++)
                    {
                        string tmpName = newBuffNames[i];
                        float tmpValue = newBuffValues[i];

                        if (tmpName.Equals("candurability"))
                        {
                            float currentDurabilityBuff = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                            if (currentDurabilityBuff == 0)
                            {
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, tmpValue);
                            }
                            else
                            {
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuff + tmpValue);
                            }

                            if (currentDurability > 0)
                            {
                                currentDurability = (int)((float)currentDurability * (1 + tmpValue));
                                encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                            }
                        }


                    }
                    treeSocket.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)EnumGemBuffType.STATS_BUFF);
                    treeSocket.SetInt("size", gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt());
                    treeSocket.SetString("gemtype", gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last());
                    if(!treeSocket.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES))
                    {
                        treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(newBuffNames);
                        treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(newBuffValues);
                    }
                    else
                    {
                        (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value = newBuffNames;
                        (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value = newBuffValues;
                    }
                    

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

        public static int GetMaxAmountSockets(ItemStack itemstack)
        {
            if (itemstack != null)
            {
                if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS))
                {
                    string searchedValue = itemstack.Attributes.GetString(itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY].AsString(), null);
                    if (searchedValue != null)
                    {
                        var f = itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS];
                        if (f != null)
                        {
                            var valueDict = JsonConvert.DeserializeObject<Dictionary<string, int[]>>(f.ToString());
                            if (valueDict.TryGetValue(searchedValue, out var value))
                            {
                                return value.Length;
                            }
                        }
                    }
                }
                else if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists("canhavesocketsnumber") && itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() > 0)
                {
                    return itemstack.ItemAttributes["canhavesocketsnumber"].AsInt();
                }
            }
            return -1;
        }
        public static int[] GetSocketsTiers(ItemStack itemstack)
        {
            if (itemstack != null)
            {
                if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS))
                {
                    string searchedValue = itemstack.Attributes.GetString(itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY].AsString(), null);
                    if (searchedValue != null)
                    {
                        var f = itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS];
                        if (f != null)
                        {
                            var valueDict = JsonConvert.DeserializeObject<Dictionary<string, int[]>>(f.ToString());
                            if (valueDict.TryGetValue(searchedValue, out var value))
                            {
                                return value;
                            }
                        }
                    }
                }
                else if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists("canhavesocketsnumber") && itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() > 0)
                {
                    return itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray<int>();
                    /*var c = itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING];
                    var f = c.AsArray<int>();
                    return new int[] { 2 };
                    return new int[] { 1 };*/
                        //itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();
                }
            }
            return new int[0];
        }
    }
}
