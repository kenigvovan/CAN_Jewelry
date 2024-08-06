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
    public class CANGemBuffAffected : EntityBehavior
    {
        public override string PropertyName()
        {
            return "cangembuffaffected";
        }
        public Dictionary<int, Dictionary<string, float>> savedBuffs;
        public CANGemBuffAffected(Entity entity) : base(entity)
        {
            //EnumCharacterDressType.UpperBodyOver
            savedBuffs = new Dictionary<int, Dictionary<string, float>>();
            this.DeserializeBuffs();
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
            if (player != null)
            {
                IInventory characterInv = player.InventoryManager.GetOwnInventory("character");
                characterInv.SlotModified += OnSlotModifiedCharacterInv;

                InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory("hotbar");
                //(player.Entity.Api as ICoreServerAPI).Event.AfterActiveSlotChanged += OnSlotModifiedHotbarInv;
                playerHotbar.SlotModified += OnSlotModifiedHotbarInv;
            }
        }
        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            IServerPlayer player = ((this.entity as EntityPlayer).Player as IServerPlayer);
            if (player != null)
            {
                IInventory characterInv = player.InventoryManager.GetOwnInventory("character");
                characterInv.SlotModified -= OnSlotModifiedCharacterInv;

                InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory("hotbar");
                //(player.Entity.Api as ICoreServerAPI).Event.AfterActiveSlotChanged += OnSlotModifiedHotbarInv;
                playerHotbar.SlotModified -= OnSlotModifiedHotbarInv;
            }
            this.SerializeBuffs();
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
            ItemStack itemStack = ((this.entity as EntityPlayer).Player as IServerPlayer).InventoryManager.GetOwnInventory("character")[i].Itemstack;
            Dictionary<string, float> newBuffDict = GetItemStackBuffs(itemStack);
            if(savedBuffs.TryGetValue(i, out Dictionary<string, float> currentBuffDict))
            {
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
            int i = to;
            //OnSlotModifiedHotbarInv(from);
            OnSlotModifiedHotbarInv(to);
        }
        private Dictionary<string, float> GetItemStackBuffs(ItemStack itemStack)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (itemStack != null && itemStack.Attributes.HasAttribute("canencrusted"))
            {
                ITreeAttribute encrustTreeHere = itemStack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(itemStack); i++)
                {
                    ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i.ToString());

                    if (!socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF))
                    {
                        continue;
                    }
                    else
                    {
                        if (socketSlot.HasAttribute(CANJWConstants.GEM_BUFF_TYPE) && (EnumGemBuffType)socketSlot.GetInt(CANJWConstants.GEM_BUFF_TYPE) != EnumGemBuffType.STATS_BUFF)
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

            foreach (var buff in buffsDict)
            {
                string attributeBuffName = buff.Key;
                float additionalValue = buff.Value;
                canjewelry.config.max_buff_values.TryGetValue(attributeBuffName, out float buffThreshold);

                if (!ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrusted"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", 0, true);
                }

                if (add)
                {
                    //overflow already present just add to neg part and standard part
                    if (ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
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
                    else if (buffThreshold != 0 && additionalValue > 0 ? Math.Abs(ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) > buffThreshold : (ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) < buffThreshold)
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
            }
            //ep.Stats[attributeBuffName].Remove("canencrustedneg");

            //canjewelry.sapi.SendMessage(ep.Player, 0, add.ToString() + ep.Stats[attributeBuffName].GetBlended().ToString() + attributeBuffName, EnumChatType.Notification);
        }
    }
}
