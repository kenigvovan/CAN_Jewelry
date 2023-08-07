using canjewelry.src.jewelry;
using canjewelry.src.CB;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using canjewelry.src.blocks;
using System.IO.Compression;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;
using Vintagestory.API.Util;
using System.Security.Claims;
using Newtonsoft.Json.Linq;

namespace canjewelry.src
{
    public class canjewelry: ModSystem
    {
        public static Harmony harmonyInstance;
        public const string harmonyID = "canjewelry.Patches";
        public static ICoreClientAPI capi;
        public static ICoreServerAPI sapi;
        internal static IServerNetworkChannel serverChannel;
        internal static IClientNetworkChannel clientChannel;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            harmonyInstance = new Harmony(harmonyID);
            
            api.RegisterBlockClass("JewelerSetBlock", typeof(JewelerSetBlock));
            api.RegisterBlockEntityClass("JewelerSetBE", typeof(JewelerSetBE));

            api.RegisterCollectibleBehaviorClass("Encrustable", typeof(EncrustableCB));

            api.RegisterBlockClass("BlockJewelGrinder", typeof(BlockJewelGrinder));
            api.RegisterBlockEntityClass("BEJewelGrinder", typeof(BEJewelGrinder));

            api.RegisterBlockClass("GrindLayerBlock", typeof(GrindLayerBlock));
            api.RegisterItemClass("ProcessedGem", typeof(ProcessedGem));
            api.RegisterItemClass("CANCutGemItem", typeof(CANCutGemItem));            
        }
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            //WildcardUtil.Match(new AssetLocation(key), obj.Code)
            harmPatch.preparedEncrustedGemsImages = new Dictionary<string, AssetLocation>();

            harmPatch.socketsTextureDict = new Dictionary<string, AssetLocation>();
            capi = api;
            harmonyInstance = new Harmony(harmonyID);
            harmPatch.socketsTextureDict.Add("socket-1", new AssetLocation("canjewelry:textures/item/tinbronze.png"));
            harmPatch.socketsTextureDict.Add("socket-2", new AssetLocation("canjewelry:textures/item/iron.png"));
            harmPatch.socketsTextureDict.Add("socket-3", new AssetLocation("canjewelry:textures/item/steel.png"));

            harmPatch.preparedEncrustedGemsImages.Add("diamond", new AssetLocation("canjewelry:item/gem/diamond.png"));

            harmPatch.preparedEncrustedGemsImages.Add("corundum", new AssetLocation("canjewelry:item/gem/corundum.png"));

            harmPatch.preparedEncrustedGemsImages.Add("emerald", new AssetLocation("canjewelry:item/gem/emerald.png"));

            harmPatch.preparedEncrustedGemsImages.Add("fluorite", new AssetLocation("canjewelry:item/gem/fluorite.png"));

            harmPatch.preparedEncrustedGemsImages.Add("lapislazuli", new AssetLocation("canjewelry:item/gem/lapislazuli.png"));

            harmPatch.preparedEncrustedGemsImages.Add("malachite", new AssetLocation("canjewelry:item/gem/malachite.png"));

            harmPatch.preparedEncrustedGemsImages.Add("olivine", new AssetLocation("canjewelry:item/gem/olivine.png"));

            harmPatch.preparedEncrustedGemsImages.Add("quartz", new AssetLocation("canjewelry:item/gem/quartz.png"));

            harmPatch.preparedEncrustedGemsImages.Add("uranium", new AssetLocation("canjewelry:item/gem/uranium.png"));
            //ItemSlot inSlot, double posX, double posY, double posZ, float size, int color, float dt, bool shading = true, bool origRotate = false, bool showStackSize = true
            harmonyInstance.Patch(typeof(Vintagestory.API.Client.GuiElementItemSlotGridBase).GetMethod("ComposeSlotOverlays", BindingFlags.NonPublic | BindingFlags.Instance), transpiler: new HarmonyMethod(typeof(harmPatch).GetMethod("Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage")));
            harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("GetHeldItemInfo"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_GetHeldItemInfo")));
            clientChannel = api.Network.RegisterChannel("canjewelry");
            clientChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            clientChannel.SetMessageHandler<SyncCANJewelryPacket>((packet) =>
            {
                Config.Current = JsonConvert.DeserializeObject<Config>(packet.CompressedConfig);
                AddBehaviorAndSocketNumber(false);
            });           
        }

        public void AddBehaviorAndSocketNumber(bool serverSide = true)
        {
            ICoreAPI api = capi;
            if (serverSide)
            {
                api = sapi;
            }
           // return;
            foreach (var it in Config.Current.items_codes_with_socket_count.Val)
            {
                Item[] arrayResult = api.World.SearchItems(new AssetLocation(it.Key));
                if(arrayResult.Length > 0) 
                {
                    foreach (Item item in arrayResult)
                    {
                        item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new EncrustableCB(item));
                        if(item.Attributes == null)
                        {
                            JToken jt = JToken.Parse("{}");                          
                            jt["canhavesocketsnumber"] = it.Value;
                            item.Attributes = new JsonObject(jt);
                            continue;
                        }
                        item.Attributes.Token["canhavesocketsnumber"] = it.Value;
                        item.Attributes = new JsonObject(item.Attributes.Token);
                    }
                }
                else
                {
                    api.Logger.Debug(String.Format("[canjewelry] Item with \"{0}\" code not found", it.Key));
                }
            }

        }
        public void onPlayerPlaying(IServerPlayer byPlayer)
        {
            IInventory charakterInv = byPlayer.InventoryManager.GetOwnInventory("character");
            InventoryBasePlayer playerHotbar = (InventoryBasePlayer)byPlayer.InventoryManager.GetOwnInventory("hotbar");
            charakterInv.SlotModified += (int slotId) => {
                if (charakterInv[slotId].Itemstack != null && charakterInv[slotId].Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    //do stuff
                }
            };
            playerHotbar.SlotModified += (int slotId) => {
                if (playerHotbar[slotId].Itemstack != null && playerHotbar[slotId].Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    ITreeAttribute tree = playerHotbar[slotId].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    for (int i = 0; i < tree.GetInt("socketsnumber"); i++)
                    {
                        ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + i);
                        if (treeSocket.GetInt("size") > 0)
                        {

                        }
                    }
                }
            };
        }
        public static void onPlayerRespawnRecalculateGemsBuffs(IServerPlayer player)
        {
            //go through all stats and delete "canencrusted" part
            foreach (KeyValuePair<string, EntityFloatStats> stat in player.Entity.Stats)
            {
                foreach (KeyValuePair<string, EntityStat<float>> keyValuePair in stat.Value.ValuesByKey)
                {
                    if (keyValuePair.Key == "canencrusted")
                    {
                        stat.Value.Set(keyValuePair.Key, 0);
                        //stat.Value.Remove(keyValuePair.Key);
                        break;
                    }
                }
                player.Entity.WatchedAttributes.MarkPathDirty("stats");
            }
            //go through hotbar active slot, character slots and apply all buffs
            IInventory playerBackpacks = player.InventoryManager.GetHotbarInventory();
            {
                //playerBackpacks.Player
                if (playerBackpacks != null)
                {
                    for (int i = 0; i < playerBackpacks.Count; ++i)
                    {
                        if (i != player.InventoryManager.ActiveHotbarSlotNumber || playerBackpacks[i].Itemstack == null || playerBackpacks[i].Itemstack.Item is ItemWearable)
                        {
                            continue;
                        }
                        if (playerBackpacks[i] != null)
                        {
                            ItemSlot itemSlot = playerBackpacks[i];
                            ItemStack itemStack = itemSlot.Itemstack;
                            if (itemStack != null)
                            {
                                if (itemStack.Attributes.HasAttribute("canencrusted"))
                                {
                                    ITreeAttribute encrustTree = itemStack.Attributes.GetTreeAttribute("canencrusted");
                                    if (encrustTree == null)
                                    {
                                        return;
                                    }
                                    for (int j = 0; j < encrustTree.GetInt("socketsnumber"); j++)
                                    {
                                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + j.ToString());
                                        if (!socketSlot.HasAttribute("attributeBuff"))
                                        {
                                            continue;
                                        }
                                        if (player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                                        {
                                            player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value + socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                        else
                                        {
                                            player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            IInventory charakterInv = player.InventoryManager.GetOwnInventory("character");
            {
                //playerBackpacks.Player
                if (charakterInv != null)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        if (charakterInv[i] != null)
                        {
                            ItemSlot itemSlot = charakterInv[i];
                            ItemStack itemStack = itemSlot.Itemstack;
                            if (itemStack != null)
                            {
                                if (itemStack.Attributes.HasAttribute("canencrusted"))
                                {
                                    ITreeAttribute encrustTree = itemStack.Attributes.GetTreeAttribute("canencrusted");
                                    if (encrustTree == null)
                                    {
                                        return;
                                    }
                                    for (int j = 0; j < encrustTree.GetInt("socketsnumber"); j++)
                                    {
                                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + j.ToString());
                                        if (!socketSlot.HasAttribute("attributeBuff"))
                                        {
                                            continue;
                                        }
                                        if (player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                                        {
                                            player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", player.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value + socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                        else
                                        {
                                            player.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            harmonyInstance = new Harmony(harmonyID);
            sapi = api;
            loadConfig();

            harmonyInstance.Patch(typeof(Vintagestory.API.Common.ItemSlot).GetMethod("TryPutInto", new[] { typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType() }), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_Collectible_DidModifyItemSlot")));
            harmonyInstance.Patch(typeof(Vintagestory.API.Common.ItemSlot).GetMethod("TakeOut"), prefix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemSlot_TakeOut")));
            harmonyInstance.Patch(typeof(Vintagestory.API.Common.ItemSlot).GetMethod("TryFlipWith"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemSlot_TryFlipWith")));
            harmonyInstance.Patch(typeof(Vintagestory.Server.CoreServerEventManager).GetMethod("TriggerAfterActiveSlotChanged"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_TriggerAfterActiveSlotChanged")));

            harmonyInstance.Patch(typeof(Vintagestory.API.Common.ItemSlot).GetMethod("ActivateSlotLeftClick", BindingFlags.NonPublic | BindingFlags.Instance), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemSlot_ActivateSlotLeftClick")));
            //ActivateSlotRightClick
            harmonyInstance.Patch(typeof(Vintagestory.API.Common.ItemSlot).GetMethod("ActivateSlotRightClick", BindingFlags.NonPublic | BindingFlags.Instance), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemSlot_ActivateSlotLeftClick")));

            api.Event.PlayerNowPlaying += onPlayerPlaying;
            api.Event.PlayerRespawn += onPlayerRespawnRecalculateGemsBuffs;

            serverChannel = sapi.Network.RegisterChannel("canjewelry");
            serverChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            api.Event.PlayerNowPlaying += sendNewValues;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, rr);         
        }
        public void rr()
        {
            AddBehaviorAndSocketNumber();
        }
        public void sendNewValues(IServerPlayer byPlayer)
        {
            sapi.Event.RegisterCallback((dt =>
            {
                if (byPlayer.ConnectionState == EnumClientState.Playing)
                {
                    serverChannel.SendPacket(new SyncCANJewelryPacket()
                    {
                        CompressedConfig = JsonConvert.SerializeObject(Config.Current)
                    },
                    byPlayer);
                }
            }
            ), 10 * 1000);
        }
        private void loadConfig()
        {
            try
            {
                Config.Current = sapi.LoadModConfig<Config>(this.Mod.Info.ModID + ".json");
                sapi.Logger.Debug("[canjewelry] " + this.Mod.Info.ModID + ".json" + " config loaded.");
                if (Config.Current != null)
                {
                    sapi.StoreModConfig<Config>(Config.Current, this.Mod.Info.ModID + ".json");
                    return;
                }
            }
            catch (Exception e)
            {
                sapi.Logger.Debug("[canjewelry] " + this.Mod.Info.ModID + ".json" + " config not found.");
            }

            Config.Current = new Config();
            sapi.StoreModConfig<Config>(Config.Current, this.Mod.Info.ModID + ".json");
            sapi.Logger.Debug("[canjewelry] " + this.Mod.Info.ModID + ".json" + " config created and stored.");
            return;
        }
        public override void Dispose()
        {
            base.Dispose();
            if (harmonyInstance != null)
            {
                harmonyInstance.UnpatchAll(harmonyID);
            }
        }

    }
}
