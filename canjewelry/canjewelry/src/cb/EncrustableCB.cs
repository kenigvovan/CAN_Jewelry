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

                    if (gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString().Equals("candurability"))
                    {

                        if (currentMaxDurability < 1)
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }

                        string currentGemAttributeBuff = treeSocket.GetString("attributeBuff");
                        if (currentGemAttributeBuff?.Equals("candurability") ?? false)
                        {
                            int currentGemSize = treeSocket.GetInt("size");
                            //here we because the slot already had durability gem buff, if it was lower tier we need add some, same then just replace, higher set lower
                            //if durability left is lower than we can extract then return and do nothing
                            //durability max we get from an item attribute of json
                            int gem_slot_size = gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt();
                            if (currentGemSize - gem_slot_size < 0)
                            {
                                //make higher dur

                                float currentDurabilityBuffOnTree = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                                float currentBuff = canjewelry.config.gems_buffs[gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString()][currentGemSize.ToString()];
                                float newBuff = canjewelry.config.gems_buffs[gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString()][gem_slot_size.ToString()];

                                if (currentDurability > 0)
                                {
                                    currentDurability = (int)((float)currentDurability / (1 + currentBuff) * (1 + newBuff));
                                    encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                                }
                                newBuff -= currentBuff;
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuffOnTree + newBuff);


                            }
                            else if (currentGemSize - gem_slot_size >= 0)
                            {
                                //why would you replace with the same or lower
                                inventory.TakeLocked = false;
                                return false;
                            }

                        }
                        else
                        {
                            //here we just add durability
                            float currentDurabilityBuff = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                            float newAdditionalBuff = canjewelry.config.gems_buffs[gem_slot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString()][gem_slot.Itemstack.Collectible.Attributes["canGemType"].ToString()];
                            if (currentDurabilityBuff == 0)
                            {
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, newAdditionalBuff);
                            }
                            else
                            {
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuff + newAdditionalBuff);
                            }

                            if (currentDurability > 0)
                            {
                                currentDurability = (int)((float)currentDurability * (1 + newAdditionalBuff));
                                encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                            }


                            treeSocket.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)EnumGemBuffType.ONE_TIME_APPLIED);
                        }
                    }
                    else
                    {
                        //but we also can have different or no gem encrusted but want to add durability now
                        //need to calculate how many to adds
                        string currentGemAttributeBuff = treeSocket.GetString("attributeBuff");
                        if (currentGemAttributeBuff != null && currentGemAttributeBuff.Equals("candurability"))
                        {
                            float currentDurabilityBuff = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                            int currentGemSize = treeSocket.GetInt("size");
                            float currentDurabilityBuffOnTree = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                            float gemyBuffValue = treeSocket.GetFloat("attributeBuffValue");

                            if (currentDurability > 0)
                            {
                                currentDurability = (int)((float)currentDurability / (1 + gemyBuffValue));
                                //currentDurability = 1;
                                encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                            }

                            currentDurabilityBuffOnTree -= gemyBuffValue;

                            if (currentDurabilityBuffOnTree == 0)
                            {
                                tree.RemoveAttribute(CANJWConstants.CANDURABILITY_STRING);
                            }
                            else
                            {
                                tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuffOnTree);
                            }
                        }
                        treeSocket.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)EnumGemBuffType.STATS_BUFF);

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
