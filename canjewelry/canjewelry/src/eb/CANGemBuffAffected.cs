using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.items;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.eb
{
    /***
     * Behavior tracks player's armor/cloth slots + active hotbar slot and apply buff for the player. 
     * 
     */
    public class CANGemBuffAffected : EntityBehavior
    {
        public override string PropertyName()
        {
            return "cangembuffaffected";
        }
        public Dictionary<int, Dictionary<string, float>> savedBuffs;
        int triesToInit = 0;
        long callbackId = 0;
        public bool initialized = false;
        public CANGemBuffAffected(Entity entity) : base(entity)
        {
        }
        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();
            savedBuffs = new Dictionary<int, Dictionary<string, float>>();
            this.DeserializeBuffs();
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
        }
        private void EnqueTryAddAndWait()
        {           
            this.callbackId = canjewelry.sapi.Event.RegisterCallback((dt =>
            {
                TryToAddSlotModified();
            }
            ), 30 * 1000);
        }
        public bool TryToAddSlotModified()
        {            
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
            this.triesToInit++;
            canjewelry.sapi.Logger.VerboseDebug(String.Format("[canjewelry] Try #{0} to load behavior for {1}", this.triesToInit, player.PlayerName));         
            IInventory characterInv = player.InventoryManager.GetOwnInventory("character");
            InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory("hotbar");
            if (characterInv == null || playerHotbar == null)
            {
                canjewelry.sapi.Logger.VerboseDebug(String.Format("[canjewelry] Try #{0} failed to load behavior for {1}", this.triesToInit, player.PlayerName));
                EnqueTryAddAndWait();
                return false;
            }
            else
            {
                characterInv = player.InventoryManager.GetOwnInventory("character");
                playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory("hotbar");
                characterInv.SlotModified += OnSlotModifiedCharacterInv;
                playerHotbar.SlotModified += OnSlotModifiedHotbarInv;
                canjewelry.sapi.Logger.VerboseDebug(String.Format("[canjewelry] Try #{0} loaded behavior for {1}", this.triesToInit, player.PlayerName));
                this.callbackId = 0;
                initialized = true;
                return true;
            }
        }
        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
            savedBuffs = new Dictionary<int, Dictionary<string, float>>();
            this.DeserializeBuffs();
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
        }
        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
            if (player != null)
            {
                IInventory characterInv = player.InventoryManager.GetOwnInventory("character");
                if (characterInv != null)
                {
                    characterInv.SlotModified -= OnSlotModifiedCharacterInv;
                }
                InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory("hotbar");
                if (playerHotbar != null)
                {
                    playerHotbar.SlotModified -= OnSlotModifiedHotbarInv;
                }
            }
            if(this.callbackId != 0)
            {
                canjewelry.sapi.Event.UnregisterCallback(this.callbackId);
                this.callbackId = 0;
            }
            this.SerializeBuffs();
            initialized = false;
            base.OnEntityDespawn(despawn);           
        }
        private void SerializeBuffs()
        {
            (this.entity as EntityPlayer).Player.WorldData.SetModdata("canjewelrysavedbuffs", SerializerUtil.Serialize(this.savedBuffs));
        }
        private void DeserializeBuffs()
        {
            var loadedBuffs = (this.entity as EntityPlayer).Player.WorldData.GetModdata("canjewelrysavedbuffs");
            if (loadedBuffs != null)
            {
                this.savedBuffs = SerializerUtil.Deserialize<Dictionary<int, Dictionary<string, float>>>(loadedBuffs);
            }
        }
        private void OnSlotModifiedCharacterInv(int i)
        {
            if(!initialized)
            {
                return;
            }
            var inv = ((this.entity as EntityPlayer).Player as IServerPlayer).InventoryManager.GetOwnInventory("character");
            if(inv == null)
            {
                return;
            }
            ItemStack itemStack = inv[i].Itemstack;
            Dictionary<string, float> newBuffDict = GetItemStackBuffs(itemStack);
            if(savedBuffs.TryGetValue(i, out Dictionary<string, float> currentBuffDict))
            {
                if(currentBuffDict == null)
                {
                    canjewelry.sapi.Logger.VerboseDebug(String.Format("[canjewelry] {0} itemslot buff dict for character inv was null", i));
                    savedBuffs.Remove(i);
                    return;
                }
                //if there is diff or new buffs are empty
                if(!currentBuffDict.Equals(newBuffDict))
                {
                    ApplyBuffFromItemStack(currentBuffDict, this.entity as EntityPlayer, false);
                    if (newBuffDict.Count > 0)
                    {
                        ApplyBuffFromItemStack(newBuffDict, this.entity as EntityPlayer, true);
                        savedBuffs[i] = newBuffDict;
                    }
                    else
                    {
                        savedBuffs.Remove(i);
                    }
                }
            }
            else
            {
                if (newBuffDict.Count > 0)
                {
                    ApplyBuffFromItemStack(newBuffDict, this.entity as EntityPlayer, true);
                    savedBuffs[i] = newBuffDict;
                }
            }
        }
        public void OnSlotModifiedHotbarInv(int i)
        {
            if (!initialized || i > 10)
            {
                return;
            }
            if (i != ((this.entity as EntityPlayer).Player as IServerPlayer).InventoryManager.ActiveHotbarSlotNumber)
            {
                return;
            }
            
            ItemStack itemStack = ((this.entity as EntityPlayer).Player as IServerPlayer).InventoryManager.GetOwnInventory("hotbar")[i].Itemstack;
            if (itemStack == null || itemStack.Item == null || itemStack.Item is ItemWearable || itemStack.Item is CANItemWearable)
            {
                if (savedBuffs.TryGetValue(1 + (int)EnumCharacterDressType.ArmorLegs, out Dictionary<string, float> currentBuffDictD))
                {
                    ApplyBuffFromItemStack(currentBuffDictD, this.entity as EntityPlayer, false);
                    savedBuffs.Remove(1 + (int)EnumCharacterDressType.ArmorLegs);
                }
                return;
            }
            Dictionary<string, float> newBuffDict = GetItemStackBuffs(itemStack);
            if (savedBuffs.TryGetValue(1 + (int)EnumCharacterDressType.ArmorLegs, out Dictionary<string, float> currentBuffDict))
            {
                //if there is diff or new buffs are empty
                if (!currentBuffDict.Equals(newBuffDict))
                {
                    ApplyBuffFromItemStack(currentBuffDict, this.entity as EntityPlayer, false);
                    if (newBuffDict.Count > 0)
                    {
                        ApplyBuffFromItemStack(newBuffDict, this.entity as EntityPlayer, true);
                        savedBuffs[1 + (int)EnumCharacterDressType.ArmorLegs] = newBuffDict;
                    }
                    else
                    {
                        savedBuffs.Remove(1 + (int)EnumCharacterDressType.ArmorLegs);
                    }
                }
            }
            else
            {
                if (newBuffDict.Count > 0)
                {
                    ApplyBuffFromItemStack(newBuffDict, this.entity as EntityPlayer, true);
                    savedBuffs[1 + (int)EnumCharacterDressType.ArmorLegs] = newBuffDict;
                }
            }
        }
        public void OnActiveSlotSwapped(IServerPlayer player, int from, int to)
        {
            OnSlotModifiedHotbarInv(to);
        }
        public Dictionary<string, float> GetItemStackBuffs(ItemStack itemStack)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (itemStack != null && itemStack.Attributes.HasAttribute("canencrusted"))
            {
                ITreeAttribute encrustTreeHere = itemStack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(itemStack); i++)
                {
                    ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());

                    if (socketSlot == null || !socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF))
                    {
                        continue;
                    }
                    else
                    {                     
                        if (socketSlot.HasAttribute(CANJWConstants.GEM_BUFF_TYPE) && (EnumGemBuffType)socketSlot.GetInt(CANJWConstants.GEM_BUFF_TYPE) != EnumGemBuffType.STATS_BUFF)
                        {
                            continue;
                        }
                        if (socketSlot.GetString(CANJWConstants.GEM_ATTRIBUTE_BUFF) == "candurability")
                        {
                            continue;
                        }
                    }
                    
                    if (socketSlot != null)
                    {
                        float additionalValue = socketSlot.GetFloat("attributeBuffValue");
                        string attributeBuffName = socketSlot.GetString("attributeBuff");
                        if (result.TryGetValue(attributeBuffName, out float currentResult))
                        {
                            result[attributeBuffName] = currentResult + additionalValue;
                        }
                        else
                        {
                            result[attributeBuffName] = additionalValue;
                        }
                    }
                }
            }
            return result;
        }
        public static void ApplyBuffFromItemStack(Dictionary<string, float> buffsDict, EntityPlayer ep, bool add)
        {
            if (buffsDict == null)
            {
                return;
            }
            foreach (var buff in buffsDict)
            {
                string attributeBuffName = buff.Key;
                float additionalValue = buff.Value;
                

                if (!ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrusted"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", 0, true);
                }

                if (!ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrustedneg", 0, true);
                }

                if (add)
                {
                    float newValue = ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue;
                    ep.Stats.Set(attributeBuffName, "canencrusted", newValue, true);
                    if(canjewelry.config.max_buff_values.TryGetValue(attributeBuffName, out float buffThreshold))
                    {
                        if(buffThreshold > 0)
                        {
                            if(newValue - buffThreshold >= 0)
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", -(newValue - buffThreshold), true);
                            }
                            else
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", 0, true);
                            }
                        }
                        else
                        {
                            if (newValue - buffThreshold <= 0)
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", -(newValue - buffThreshold), true);
                            }
                            else
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", 0, true);
                            }
                        }
                    }
                }
                else
                {
                    float newValue = ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value - additionalValue;
                    ep.Stats.Set(attributeBuffName, "canencrusted", newValue, true);

                    if (canjewelry.config.max_buff_values.TryGetValue(attributeBuffName, out float buffThreshold))
                    {
                        if (buffThreshold > 0)
                        {
                            if (newValue - buffThreshold >= 0)
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", -(newValue - buffThreshold), true);
                            }
                            else
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", 0, true);
                            }
                        }
                        else
                        {
                            if (newValue - buffThreshold <= 0)
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", -(newValue - buffThreshold), true);
                            }
                            else
                            {
                                ep.Stats.Set(attributeBuffName, "canencrustedneg", 0, true);
                            }
                        }
                    }
                }
            }
        }

        public override void OnEntityRevive()
        {
            base.OnEntityRevive();
            savedBuffs.Clear();
            foreach (KeyValuePair<string, EntityFloatStats> stat in entity.Stats)
            {
                foreach (KeyValuePair<string, EntityStat<float>> keyValuePair in stat.Value.ValuesByKey.ToArray())
                {
                    if (keyValuePair.Key == "canencrusted")
                    {
                        stat.Value.Set(keyValuePair.Key, 0);
                        continue;
                    }
                    if (keyValuePair.Key == "canencrustedneg")
                    {
                        stat.Value.Remove(keyValuePair.Key);
                    }
                }
                entity.WatchedAttributes.MarkPathDirty("stats");
            }
            //go through hotbar active slot, character slots and apply all buffs
            IInventory playerBackpacks = (entity as EntityPlayer).Player.InventoryManager.GetHotbarInventory();
            if (playerBackpacks != null)
            {
                ItemSlot activeSlot = (entity as EntityPlayer).Player.InventoryManager.ActiveHotbarSlot;
                var itemStack = activeSlot.Itemstack;
                if (itemStack != null && itemStack.Item is not ItemWearable && itemStack.Item is not CANItemWearable)
                {                   
                    var newBuffs = GetItemStackBuffs(itemStack);
                    ApplyBuffFromItemStack(newBuffs, entity as EntityPlayer, true);
                    savedBuffs[1 + (int)EnumCharacterDressType.ArmorLegs] = newBuffs;
                }
            }

            IInventory charakterInv = (entity as EntityPlayer).Player.InventoryManager.GetOwnInventory("character");
            
            //playerBackpacks.Player
            if (charakterInv != null)
            {
                for (int i = 0; i < 16; ++i)
                {
                    if (charakterInv[i] != null)
                    {
                        ItemSlot itemSlot = charakterInv[i];
                        ItemStack itemStack = itemSlot.Itemstack;
                        if (itemStack != null)
                        {
                            var newBuffs = GetItemStackBuffs(itemStack);
                            ApplyBuffFromItemStack(newBuffs, entity as EntityPlayer, true);
                            savedBuffs[itemSlot.Inventory.GetSlotId(itemSlot)] = newBuffs;
                        }
                    }
                }
                
            }
        }
    }
}
