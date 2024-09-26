using canjewelry.src.CB;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using canjewelry.src.blocks;
using Newtonsoft.Json;
using Vintagestory.API.Util;
using Newtonsoft.Json.Linq;
using canjewelry.src.jewelry;
using canjewelry.src.items;
using canjewelry.src.eb;
using canjewelry.src.be;

namespace canjewelry.src
{
    public class canjewelry: ModSystem
    {
        public Harmony harmonyInstance;
        public const string harmonyID = "canjewelry.Patches";
        public static ICoreClientAPI capi;
        public static ICoreServerAPI sapi;
        internal static IServerNetworkChannel serverChannel;
        internal static IClientNetworkChannel clientChannel;
        public static Config config;
        public static Dictionary<string, string> gems_textures = new Dictionary<string, string>();
        public static List<GemCuttingRecipe> gemCuttingRecipes;
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            harmonyInstance = new Harmony(harmonyID);
            var p = harmonyInstance.GetPatchedMethods();
            if(p.All(it => it.Name != "GetMaxDurability"))
            {
                harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("GetMaxDurability"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_CollectibleObject_GetMaxDurability")));
            }
            
            api.RegisterBlockClass("JewelerSetBlock", typeof(JewelerSetBlock));
            api.RegisterBlockEntityClass("JewelerSetBE", typeof(JewelerSetBE));

            api.RegisterCollectibleBehaviorClass("Encrustable", typeof(EncrustableCB));

            api.RegisterBlockClass("BlockJewelGrinder", typeof(BlockJewelGrinder));
            api.RegisterBlockClass("BlockCANWireDrawingBench", typeof(CANWireDrawingBench));
            api.RegisterBlockEntityClass("BEJewelGrinder", typeof(BEJewelGrinder));
            api.RegisterBlockEntityClass("BEWireDrawingBench", typeof(CANBEWireDrawingBench));
            api.RegisterBlockEntityClass("BEGemCuttingTable", typeof(BlockEntityGemCuttingTable));

            api.RegisterItemClass("GrindLayerBlock", typeof(GrindLayerBlock));

            api.RegisterItemClass("ProcessedGem", typeof(ProcessedGem));
            api.RegisterItemClass("CANCutGemItem", typeof(CANCutGemItem));
            api.RegisterItemClass("CANRoughGemItem", typeof(CANRoughGemItem));
            
            api.RegisterItemClass("CANItemSimpleNecklace", typeof(CANItemSimpleNecklace));
            api.RegisterItemClass("CANItemTiara", typeof(CANItemTiara));
            api.RegisterItemClass("CANItemRottenKingMask", typeof(CANItemRottenKingMask));
            api.RegisterItemClass("CANItemCoronet", typeof(CANItemCoronet));
            api.RegisterItemClass("CANItemMonocle", typeof(CANItemMonocle));
            api.RegisterItemClass("CANItemWireHank", typeof(CANItemWireHank));
            api.RegisterItemClass("CANItemArmBand", typeof(CANItemArmBand));
            api.RegisterItemClass("CANItemStrap", typeof(CANItemStrap));
            api.RegisterItemClass("CANItemGemCuttingWorkItem", typeof(CANItemGemCuttingWorkItem));
            api.RegisterItemClass("CANItemGemChisel", typeof(CANItemGemChisel));
            

            api.RegisterBlockClass("CANBlockPan", typeof(CANBlockPan));
            api.RegisterBlockClass("BlockGemCuttingTable", typeof(BlockGemCuttingTable));
        }
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
           
            capi = api;
            loadConfig(capi);
            harmonyInstance = new Harmony(harmonyID);
            harmonyInstance.Patch(typeof(Vintagestory.API.Client.GuiElementItemSlotGridBase).GetMethod("ComposeSlotOverlays", BindingFlags.NonPublic | BindingFlags.Instance), transpiler: new HarmonyMethod(typeof(harmPatch).GetMethod("Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage")));

            harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("GetHeldItemInfo"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_GetHeldItemInfo")));

            harmonyInstance.Patch(typeof(CharacterSystem).GetMethod("StartClientSide"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_CharacterSystem_StartClientSide")));

            harmonyInstance.Patch(typeof(ItemChisel).GetMethod("OnHeldAttackStart"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_ItemChisel_OnHeldAttackStart")));

            clientChannel = api.Network.RegisterChannel("canjewelry");
            clientChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            clientChannel.SetMessageHandler<SyncCANJewelryPacket>((packet) =>
            {
                config = JsonConvert.DeserializeObject<Config>(packet.CompressedConfig);
                AddBehaviorAndSocketNumber(false);
            });

            //Set colors of processed gems on jewel grinder
            Item[] arrayResult = api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*"));
            foreach(var gem in arrayResult)
            {
                string gemType = gem.Code.Path.Split('-').Last();
                if(!BEJewelGrinder.gemTypeToColor.TryGetValue(gemType, out _))
                {
                    int color = gem.GetRandomColor(capi, null);
                    BEJewelGrinder.gemTypeToColor[gemType] = color;
                }
            }
            capi.Event.LevelFinalize += () =>
            {
                Item[] cut_gems_items = api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*"));

                foreach (var gem in cut_gems_items)
                {
                    //catch if not present?
                    gems_textures.TryAdd(gem.Code.Path.Split('-').Last(), gem.Textures["gem"].Base.Domain + ":textures/" + gem.Textures["gem"].Base.Path);
                }
                if (clientChannel.Connected)
                {
                    clientChannel.SendPacket(new SyncCANJewelryPacket()
                    {
                        CompressedConfig = ""
                    });
                }
            };
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            harmonyInstance = new Harmony(harmonyID);
            sapi = api;
            loadConfig(sapi);
            api.RegisterEntityBehaviorClass("cangembuffaffected", typeof(CANGemBuffAffected));

            harmonyInstance.Patch(typeof(Vintagestory.Server.CoreServerEventManager).GetMethod("TriggerAfterActiveSlotChanged"), postfix: new HarmonyMethod(typeof(harmPatch).GetMethod("Postfix_TriggerAfterActiveSlotChanged")));

            harmonyInstance.Patch(typeof(Vintagestory.API.Common.CollectibleObject).GetMethod("DamageItem"), transpiler: new HarmonyMethod(typeof(harmPatch).GetMethod("Transpiler_CollectibleObject_DamageItem")));

            //harmonyInstance.Patch(typeof(Vintagestory.API.Common.Block).GetMethod("GetDrops"), prefix: new HarmonyMethod(typeof(harmPatch).GetMethod("Prefix_GetDrops")));

            serverChannel = sapi.Network.RegisterChannel("canjewelry");
            serverChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => AddBehaviorAndSocketNumber());
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            commands.RegisterCommands.registerServerCommands(sapi);

            serverChannel.SetMessageHandler<SyncCANJewelryPacket>((player, packet) =>
            {
                sendNewValues(player);
            });
            foreach (var it in config.gems_drops_table)
            {
                Block[] found_blocks = api.World.SearchBlocks(new AssetLocation(it.Key));
                foreach (var block in found_blocks)
                {
                    List<BlockDropItemStack> blockDropsToAdd = new List<BlockDropItemStack>();
                    foreach (var dropInfo in it.Value)
                    {
                        ItemStack itemStack;
                        if (dropInfo.TypeCollectable == EnumItemClass.Item)
                        {
                            Item item = sapi.World.GetItem(new AssetLocation(dropInfo.NameCollectable));
                            if (item == null)
                            {
                                sapi.Logger.VerboseDebug(dropInfo.NameCollectable + " not found.");
                                continue;
                            }
                            itemStack = new ItemStack(item);
                        }
                        else
                        {
                            itemStack = new ItemStack(sapi.World.GetBlock(new AssetLocation(dropInfo.NameCollectable)));
                        }
                        BlockDropItemStack additionalDrop = new BlockDropItemStack();
                        additionalDrop.Type = dropInfo.TypeCollectable;
                        additionalDrop.Code = itemStack.Collectible.Code;
                        additionalDrop.ResolvedItemstack = itemStack;
                        additionalDrop.Quantity.avg = dropInfo.avg;
                        additionalDrop.Quantity.var = dropInfo.var;
                        additionalDrop.LastDrop = dropInfo.LastDrop;
                        additionalDrop.DropModbyStat = null;
                        blockDropsToAdd.Add(additionalDrop);
                    }
                    block.Drops = block.Drops.Append(blockDropsToAdd.ToArray());
                }
            }
        }
        public void OnPlayerNowPlaying(IServerPlayer byPlayer)
        {
            var plBeh = byPlayer.Entity.GetBehavior<CANGemBuffAffected>();
            if (plBeh != null)
            {
                if(!plBeh.initialized)
                {
                    plBeh.TryToAddSlotModified();
                }
            }
        }
        public override void Dispose()
        {
            base.Dispose();
            if (harmonyInstance != null)
            {
                harmonyInstance.UnpatchAll(harmonyID);
            }
        }
        public void AddBehaviorAndSocketNumber(bool serverSide = true)
        {
            ICoreAPI api = capi;
            if (serverSide)
            {
                api = sapi;
            }

            foreach (var it in config.items_codes_with_socket_count_and_tiers)
            {
                if(it.Value == null || it.Value.Length == 0)
                {
                    continue;
                }
                Item[] arrayResult = api.World.SearchItems(new AssetLocation(it.Key));
                if(arrayResult.Length > 0) 
                {
                    
                    foreach (Item item in arrayResult)
                    {
                        if (!item.HasBehavior<EncrustableCB>())
                        {
                            item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new EncrustableCB(item));
                        }
                        if(item.Attributes == null)
                        {
                            JToken jt = JToken.Parse("{}");
                            jt[CANJWConstants.SOCKETS_NUMBER_STRING] = it.Value.Length;
                            item.Attributes = new JsonObject(jt);
                            continue;
                        }

                        string s = JsonConvert.SerializeObject(it.Value);

                        //parse it for jarray
                        JToken k = JToken.Parse(s);
                        //set to item general attributes, accessible across all
                        item.Attributes.Token[CANJWConstants.SOCKETS_TIERS_STRING] = k;

                        item.Attributes.Token[CANJWConstants.SOCKETS_NUMBER_STRING] = it.Value.Length;

                        item.Attributes = new JsonObject(item.Attributes.Token);
                    }
                }
                else
                {
                    if (config.debugMode)
                    {
                        api.Logger.VerboseDebug(String.Format("[canjewelry] Item with \"{0}\" code not found", it.Key));
                    }
                }
            }         
            
            Item[] rough_gems_items = api.World.SearchItems(new AssetLocation("canjewelry:gem-rough-*"));

            foreach(var gem in rough_gems_items)
            {
                string code_item = gem.Code.Path.Split('-').Last();
                if(config.gem_type_to_buff.ContainsKey(code_item) )
                {
                    gem.Attributes.Token["canGemTypeToAttribute"] = config.gem_type_to_buff[code_item];
                }
            }

            Item[] cut_gems_items = api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*"));

            foreach (var gem in cut_gems_items)
            {
                //catch if not present?
                gems_textures.TryAdd(gem.Code.Path.Split('-').Last(), gem.Textures["gem"].Base.Domain + ":textures/" + gem.Textures["gem"].Base.Path);
            }
          
            foreach (var gem in cut_gems_items)
            {
                string code_item = gem.Code.Path.Split('-').Last();
                if (config.gem_type_to_buff.ContainsKey(code_item))
                {
                    gem.Attributes.Token["canGemTypeToAttribute"] = config.gem_type_to_buff[code_item];
                }
            }

            foreach(var it in config.custom_variants_sockets_tiers)
            {
                Item[] arrayResult = api.World.SearchItems(new AssetLocation(it.ItemCode));
                if (arrayResult.Length > 0)
                {
                    foreach (Item item in arrayResult)
                    {
                        if (!item.HasBehavior<EncrustableCB>())
                        {
                            item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new EncrustableCB(item));
                        }
                        if (item.Attributes == null)
                        {
                            JToken jt = JToken.Parse("{}");
                            item.Attributes = new JsonObject(jt);
                        }


                        string s = JsonConvert.SerializeObject(it.SocketTiers);

                        //parse it for jarray
                        JToken k = JToken.Parse(s);

                        //set to item general attributes, accessible across all
                        item.Attributes.Token[CANJWConstants.CAN_CUSTOM_VARIANTS] = k;
                        item.Attributes.Token[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY] = it.AttributeKey;
                    }
                }
                else
                {
                    if (config.debugMode)
                    {
                       // api.Logger.VerboseDebug(String.Format("[canjewelry] Item with \"{0}\" code not found", it.Key));
                    }
                }
            }
        }      
        public void sendNewValues(IServerPlayer byPlayer)
        {
            if (byPlayer.ConnectionState != EnumClientState.Offline)
            {
                serverChannel.SendPacket(new SyncCANJewelryPacket()
                {
                    CompressedConfig = JsonConvert.SerializeObject(config)
                },
                byPlayer);
            }
        }
        private void loadConfig(ICoreAPI api)
        {
            //Try to read old config
            OldConfig oldConfig = null;
            try
            {
                oldConfig = api.LoadModConfig<OldConfig>(this.Mod.Info.ModID + ".json");
            }
            catch (Exception e)
            {

            }
            //old config was found and we just copy values from it
            if (oldConfig != null)
            {
                config = new Config();
                config.grindTimeOneTick = oldConfig.grindTimeOneTick.Val;
                config.buffNameToPossibleItem = oldConfig.buffNameToPossibleItem.Val;
                config.gems_buffs = oldConfig.gems_buffs.Val;
                config.items_codes_with_socket_count = oldConfig.items_codes_with_socket_count.Val;
                //make copy of the old config and new to old file
                try
                {
                    api.StoreModConfig<OldConfig>(oldConfig, this.Mod.Info.ModID + "_old.json");
                    api.StoreModConfig<Config>(config, this.Mod.Info.ModID + ".json");
                }
                catch(Exception e)
                {

                }
                return;
            }
            //no old config, try to load new format
            else
            {
                //config = new Config();
                config = api.LoadModConfig<Config>(this.Mod.Info.ModID + ".json");
                if (config != null && config.items_codes_with_socket_count.Count != 1)
                {
                    foreach (var itemIter in config.items_codes_with_socket_count)
                    {
                        int[] tmp = new int[itemIter.Value].Fill(3);
                        config.items_codes_with_socket_count_and_tiers[itemIter.Key] = tmp;
                    }
                    config.items_codes_with_socket_count.Clear();
                    config.items_codes_with_socket_count["moved to items_codes_with_socket_count_and_tiers"] = 42;
                }
                if (config == null)
                { 
                    config = new Config();
                    config.FillDefaultValues();
                    config.config_version = api.ModLoader.Mods.FirstOrDefault(mod => mod.Info.ModID == "canjewelry")?.Info.Version ?? "0.0.0";
                    api.StoreModConfig<Config>(config, this.Mod.Info.ModID + ".json");
                    if (config.debugMode)
                    {
                        api.Logger.VerboseDebug("[canjewelry] " + this.Mod.Info.ModID + ".json" + " new config created and stored.");
                    }
                    return;
                }
                var currVersion = api.ModLoader.Mods.FirstOrDefault(mod => mod.Info.ModID == "canjewelry")?.Info.Version ?? "0.0.0";
                if(currVersion != null) 
                { 
                    if(currVersion != config.config_version)
                    {
                        config.FillDefaultValues(true);
                    }
                }


                api.StoreModConfig<Config>(config, this.Mod.Info.ModID + ".json");
                if (config.debugMode)
                {
                    api.Logger.VerboseDebug("[canjewelry] " + this.Mod.Info.ModID + ".json" + "config read and stored back.");
                }
                return;
            }
        }      
    }
}
