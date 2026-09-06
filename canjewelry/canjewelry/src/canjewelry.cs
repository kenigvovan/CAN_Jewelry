using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using canjewelry.src.api;
using canjewelry.src.bb;
using canjewelry.src.be;
using canjewelry.src.blocks;
using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.eb;
using canjewelry.src.gui;
using canjewelry.src.harmony;
using canjewelry.src.inventories;
using canjewelry.src.items;
using canjewelry.src.items.resource;
using canjewelry.src.jewelry;
using canjewelry.src.utils;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace canjewelry.src
{
    /// <summary>
    /// Main ModSystem class for the CAN Jewelry mod.
    /// Responsible for registering items, blocks, behaviors,
    /// loading configuration, handling networking,
    /// and applying Harmony patches.
    /// </summary>
    public class canjewelry: ModSystem
    {
        /// <summary>
        /// Singleton-style accessor used by static-context callers (EncrustableCB, BEs)
        /// to fire integration events without holding api references.
        /// </summary>
        public static canjewelry Instance { get; private set; }

        // ── Integration hooks ────────────────────────────────────────────────────
        // Companion mods (e.g. canjewelry-xskills) subscribe to these and mutate
        // the event objects to influence outcomes. Running with no subscribers
        // leaves vanilla math untouched.
        public event Action<src.integration.EncrustEvent> OnEncrustBuff;
        public event Action<src.integration.CutEvent>     OnCutGem;
        public event Action<src.integration.GrindEvent>   OnGrindStep;
        public event Action<src.integration.GrindStageStartEvent> OnGrindStageStart;
        public event Action<src.integration.PanEvent>      OnPanGem;
        public event Action<src.integration.PanTakeEvent>  OnPanMaterialTake;
        public event Action<src.integration.CanPanDropEvent> OnCanPanDrop;
        public event Action<src.integration.ExtractEvent>   OnExtractGem;
        public event Action<src.integration.CanExtractEvent> OnCanExtract;
        public event Action<src.integration.WireDrawEvent>  OnWireDrawn;
        public event Action<src.integration.CanInscribeEvent> OnCanInscribe;
        public event Action<src.integration.CANXpEvent>    OnXpAwarded;

        public void FireEncrust(src.integration.EncrustEvent e)   => OnEncrustBuff?.Invoke(e);
        public void FireCut    (src.integration.CutEvent     e)   => OnCutGem?.Invoke(e);
        public void FireGrind  (src.integration.GrindEvent   e)   => OnGrindStep?.Invoke(e);
        public void FireGrindStageStart(src.integration.GrindStageStartEvent e) => OnGrindStageStart?.Invoke(e);
        public void FirePan    (src.integration.PanEvent     e)   => OnPanGem?.Invoke(e);
        public void FirePanMaterialTake(src.integration.PanTakeEvent e) => OnPanMaterialTake?.Invoke(e);
        public void FireCanPanDrop(src.integration.CanPanDropEvent e) => OnCanPanDrop?.Invoke(e);

        // Companion-supplied drop entries that get merged into CANBlockPan.dropsBySourceMat
        // at block-load time. Kept separate from config.panningDrops (which is admin-persistent)
        // so companion contributions never end up written to canjewelry.json.
        public readonly System.Collections.Generic.Dictionary<string, src.utils.CANPanningDrop[]> runtimeExtraPanDrops
            = new System.Collections.Generic.Dictionary<string, src.utils.CANPanningDrop[]>();

        // Companion-mod entry point: extends panning drop tables at startup. Drops are
        // resolved against the world and added in-memory only — they do not persist into
        // canjewelry.json. Calling with the same material again appends rather than replacing.
        public void RegisterPanDrops(IWorldAccessor world, string material, src.utils.CANPanningDrop[] drops)
        {
            foreach (var drop in drops)
            {
                if (drop?.Code != null && !drop.Code.Path.Contains("{rocktype}"))
                {
                    drop.Resolve(world, "RegisterPanDrops:" + material, true);
                }
            }
            if (runtimeExtraPanDrops.TryGetValue(material, out var existing))
            {
                var combined = new src.utils.CANPanningDrop[existing.Length + drops.Length];
                System.Array.Copy(existing, 0, combined, 0, existing.Length);
                System.Array.Copy(drops, 0, combined, existing.Length, drops.Length);
                runtimeExtraPanDrops[material] = combined;
            }
            else
            {
                runtimeExtraPanDrops[material] = drops;
            }
        }
        public bool HasExtractSubscriber => OnExtractGem != null;
        public void FireExtract(src.integration.ExtractEvent e)     => OnExtractGem?.Invoke(e);
        public void FireCanExtract(src.integration.CanExtractEvent e) => OnCanExtract?.Invoke(e);
        public void FireWireDraw(src.integration.WireDrawEvent e) => OnWireDrawn?.Invoke(e);
        public void FireCanInscribe(src.integration.CanInscribeEvent e) => OnCanInscribe?.Invoke(e);
        public void FireXp     (src.integration.CANXpEvent   e)   => OnXpAwarded?.Invoke(e);

        /// <summary>
        /// Harmony instance used for runtime patching.
        /// </summary>
        public Harmony harmonyInstance;

        /// <summary>
        /// Unique Harmony ID for this mod.
        /// </summary>
        public const string harmonyID = "canjewelry.Patches";

        /// <summary>Companion mod holding the wearable adornments and the display stands.</summary>
        public const string AdornmentsModId = "canjewelryadornments";

        /// <summary>
        /// Client-side API reference.
        /// </summary>
        public static ICoreClientAPI capi;

        /// <summary>
        /// Server-side API reference.
        /// </summary>
        public static ICoreServerAPI sapi;

        /// <summary>
        /// Server network channel for config synchronization.
        /// </summary>
        internal static IServerNetworkChannel serverChannel;

        /// <summary>
        /// Client network channel for config synchronization.
        /// </summary>
        internal static IClientNetworkChannel clientChannel;

        /// <summary>
        /// Global mod configuration.
        /// </summary>
        public static Config config;

        /// <summary>
        /// Map of gem type → texture path.
        /// </summary>
        public static Dictionary<string, string> gems_textures = new();

        /// <summary>
        /// Map of gem type → PNG texture path.
        /// </summary>
        public static Dictionary<string, string> gems_textures_pngs;

        /// <summary>
        /// Loaded gem cutting recipes.
        /// </summary>
        public static List<GemCuttingRecipe> gemCuttingRecipes = new();

        private GuiDialogJewelryGuide guideDialog;
        private GuiDialogCuttingSettings cuttingSettingsDialog;
        /// <summary>
        /// Loaded wearable restrictions, keyed by "category/name". Filled from every mod that ships
        /// config/restrictions/** in the canjewelry domain, so a content mod can add a display
        /// category with nothing but a JSON file.
        /// </summary>
        private readonly Dictionary<string, RestrictionData> restrictions = [];
        /// <summary>
        /// Model transformations associated with restrictions.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, ModelTransform>> transformations = [];

        /// <summary>
        /// A new mod-load run begins. The engine constructs every ModSystem before running any
        /// StartPre (see ModLoader.instantiateMods), so this is the only spot where the core runs
        /// ahead of content mods regardless of ExecuteOrder. Reopen the registry here: it is
        /// static, while the Pre phase runs once per side in a local game and again on every world
        /// rejoin — without the reset the second run replays its registrations against a registry
        /// sealed by the first and warns on every one of them.
        /// </summary>
        public canjewelry()
        {
            CANJewelryRegistry.Sealed = false;
        }

        /// <summary>
        /// Executed before all mods are started.
        /// Adds an additional jewelry inventory to player defaults.
        /// </summary>
        public override void StartPre(ICoreAPI api)
        {
            // Content mods register their config defaults from their own StartPre at a lower
            // ExecuteOrder, so by now the registry holds everything they contributed. Seed the
            // core's own material keys first, keeping them ahead of registered ones in priority.
            CANJewelryRegistry.Logger = Mod?.Logger;
            CANJewelryRegistry.SeedMaterialAttributeKeys(items.CANItemWearable.BaseMaterialAttrKeys);

            // Config has to exist before any asset is loaded: Block.OnLoaded (CANBlockPan) reads
            // config.panningDrops, and on the client that runs before StartClientSide, so loading
            // it there only would leave the block with a null config and crash world join.
            if (config == null)
            {
                loadConfig(api);
            }

            // Anything registered from here on misses this run's defaults. It is still recorded and
            // applies on the next start, so the registry only warns.
            CANJewelryRegistry.Sealed = true;

            // Guard against appending twice within a single VS process (e.g. leave and rejoin a
            // world with the mod active), which would otherwise leave a duplicate entry behind.
            if (!PlayerInventoryManager.defaultInventories.Contains("additionaljewelrycharacter"))
            {
                PlayerInventoryManager.defaultInventories = PlayerInventoryManager.defaultInventories.Append("additionaljewelrycharacter");
            }
            base.StartPre(api);
        }

        /// <summary>
        /// Called on both client and server.
        /// Registers blocks, items, behaviors, entities,
        /// and applies core Harmony patches.
        /// </summary>
        public override void Start(ICoreAPI api)
        {
            Instance = this;
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
            api.RegisterCollectibleBehaviorClass("GemCuttableCB", typeof(CANGemCuttableCB));

            api.RegisterBlockBehaviorClass("CANTemporalGraspBB", typeof(CANTemporalGraspBB));

            api.RegisterBlockClass("BlockJewelGrinder", typeof(BlockJewelGrinder));
            api.RegisterBlockClass("BlockCANWireDrawingBench", typeof(CANWireDrawingBench));
            api.RegisterBlockEntityClass("BEJewelGrinder", typeof(BEJewelGrinder));
            api.RegisterBlockEntityClass("BEWireDrawingBench", typeof(CANBEWireDrawingBench));
            api.RegisterBlockEntityClass("BEGemCuttingTable", typeof(BlockEntityGemCuttingTable));

            api.RegisterItemClass("GrindLayerBlock", typeof(GrindLayerBlock));

            api.RegisterItemClass("ProcessedGem", typeof(ProcessedGem));
            api.RegisterItemClass("CANCutGemItem", typeof(CANCutGemItem));
            api.RegisterItemClass("CANRoughGemItem", typeof(CANRoughGemItem));
            
            // The wearable adornments and the display stands register themselves from the
            // canjewelryadornments mod. Only the crafting materials and gem-processing items
            // belong to the core.
            api.RegisterItemClass("CANItemWireHank", typeof(CANItemWireHank));
            api.RegisterItemClass("CANItemStrap", typeof(CANItemStrap));
            api.RegisterItemClass("CANItemSocket", typeof(CANItemSocket));
            api.RegisterItemClass("CANItemGemCuttingWorkItem", typeof(CANItemGemCuttingWorkItem));
            api.RegisterItemClass("CANItemGemChisel", typeof(CANItemGemChisel));

            api.RegisterBlockClass("CANBlockPan", typeof(CANBlockPan));
            api.RegisterBlockClass("BlockGemCuttingTable", typeof(BlockGemCuttingTable));
            api.RegisterEntityBehaviorClass("playeradditionaljewelryinventory", typeof(EntityBehaviorAdditionalJewelryPlayerInventory));
        }
        /// <summary>
        /// Client-side initialization.
        /// Handles GUI icons, client patches, config sync,
        /// and visual gem data.
        /// </summary>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;
            // Normally already loaded in StartPre; kept as a fallback for the case StartPre was skipped.
            if (config == null)
            {
                loadConfig(capi);
            }
            AddCustomIcons();
            ClientPatcher.ApplyPatches(capi, harmonyID, ref harmonyInstance);

            guideDialog = new GuiDialogJewelryGuide(api);
            api.Input.RegisterHotKey("canjewelryguide", "CAN Jewelry Guide", Vintagestory.API.Client.GlKeys.J, Vintagestory.API.Client.HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("canjewelryguide", comb =>
            {
                if (guideDialog.IsOpened()) guideDialog.TryClose();
                else guideDialog.TryOpen();
                return true;
            });

            clientChannel = api.Network.RegisterChannel("canjewelry");
            clientChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            clientChannel.RegisterMessageType(typeof(CuttingSettingsPacket));
            clientChannel.SetMessageHandler<CuttingSettingsPacket>((packet) =>
            {
                // Only ever arrives in response to "/canjewelry cutting gui", which the server
                // gates on the privilege, so there is nothing left to check here.
                cuttingSettingsDialog?.TryClose();
                cuttingSettingsDialog = new GuiDialogCuttingSettings(capi, packet);
                cuttingSettingsDialog.TryOpen();
            });
            clientChannel.SetMessageHandler<SyncCANJewelryPacket>((packet) =>
            {
                // The network handler swallows exceptions, so a config the client cannot parse
                // used to leave it silently unconfigured - no socket counts in tooltips, no gem
                // fitting anything - with nothing in the log to explain it. Parse into a local
                // first and keep the previous config if anything goes wrong.
                try
                {
                    Config received = JsonConvert.DeserializeObject<Config>(packet.CompressedConfig);
                    if (received == null)
                    {
                        capi.Logger.Error("[canjewelry] server sent an empty config, keeping the local one");
                        return;
                    }

                    // The server sends the config in its stored form, so the "$armor" references
                    // arrive unresolved and have to be expanded here too - otherwise the client
                    // would think a gem fits nothing but the literally listed codes.
                    received.ExpandItemGroups();
                    config = received;
                    AddBehaviorAndSocketNumber(capi);
                }
                catch (Exception e)
                {
                    capi.Logger.Error("[canjewelry] could not apply the config sent by the server, keeping the local one. {0}", e);
                }
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
            ClientMain.ClassRegistry.RegisterInventoryClass("additionaljewelrycharacter", typeof(InventoryCharacterAdditionalJewelry));
            api.Event.PlayerJoin += (IClientPlayer byPlayer) =>
            {
                if (byPlayer != null && capi.World.Player != null && byPlayer == capi.World.Player)
                {
                    if (clientChannel.Connected)
                    {
                        clientChannel.SendPacket(new SyncCANJewelryPacket()
                        {
                            CompressedConfig = ""
                        });
                    }
                    else
                    {
                        canjewelry.capi.Event.RegisterCallback((dt =>
                        {
                            if (clientChannel.Connected)
                            {
                                clientChannel.SendPacket(new SyncCANJewelryPacket()
                                {
                                    CompressedConfig = ""
                                });
                            }
                        }
                        ), 60 * 1000);
                    }
                }
            };
            
        }
        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            if (api.Side == EnumAppSide.Server)
            {
                var restrictionGroupsServer = DiscoverRestrictionGroups(api);
                LoadData(api, restrictionGroupsServer);
            }
            CANItemWearable.NotVisTexture = new AssetLocation("canjewelry:item/gem/notvis.png");
        }
        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            foreach (CollectibleObject obj in api.World.Collectibles)
            {
                foreach (var restriction in restrictions)
                {
                    transformations.TryGetValue(restriction.Key, out var transformation);
                    RestrictionsPatches.PatchCollectibleWhitelist(obj, restriction, transformation);
                }
            }
            Item[] cut_gems_items = api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*"));
            gems_textures = new();
            gems_textures_pngs = new();
            foreach (var gem in cut_gems_items)
            {
                //catch if not present?
                gems_textures.TryAdd(gem.Code.Path.Split('-').Last(), gem.Textures["gem"].Base.Domain + ":textures/" + gem.Textures["gem"].Base.Path);
                gems_textures_pngs.TryAdd(gem.Code.Path.Split('-').Last(), gem.Textures["gem"].Base.Domain + ":" + gem.Textures["gem"].Base.Path + ".png");
            }
            
        }
        private Dictionary<string, string[]> DiscoverRestrictionGroups(ICoreAPI api)
        {
            var restrictionGroups = new Dictionary<string, string[]>();
            string basePath = "config/restrictions/";

            var restrictionAssets = api.Assets.GetMany("config/restrictions", "canjewelry", false);

            foreach (var asset in restrictionAssets)
            {
                string fullPath = asset.Location.Path;

                string relativePath = fullPath[basePath.Length..];
                string[] pathParts = relativePath.Split('/');

                if (pathParts.Length >= 2)
                {
                    string folderName = pathParts[0];
                    string fileName = pathParts[1];

                    if (fileName.EndsWith(".json"))
                    {
                        fileName = fileName[..^5];
                    }

                    if (!restrictionGroups.TryGetValue(folderName, out string[] value))
                    {
                        value = [];
                        restrictionGroups[folderName] = value;
                    }

                    var currentFiles = value.ToList();
                    if (!currentFiles.Contains(fileName))
                    {
                        currentFiles.Add(fileName);
                        restrictionGroups[folderName] = [.. currentFiles];
                    }
                }
            }

            // Remove folders that have no files
            var foldersToRemove = restrictionGroups.Where(kvp => kvp.Value.Length == 0).Select(kvp => kvp.Key).ToList();
            foreach (var folder in foldersToRemove)
            {
                restrictionGroups.Remove(folder);
            }

            return restrictionGroups;
        }
        private void LoadData(ICoreAPI api, Dictionary<string, string[]> restrictionGroups)
        {
            foreach (var (category, names) in restrictionGroups)
            {
                foreach (var name in names)
                {
                    string restrictionPath = $"canjewelry:config/restrictions/{category}/{name}.json".Replace("//", "/");
                    string transformationPath = $"canjewelry:config/transformations/{category}/{name}.json".Replace("//", "/");

                    // Keyed by category/name, not name alone: two categories may hold the same file
                    // name (restrictions/rings/headware.json vs restrictions/clothes/headware.json)
                    // and the later one used to silently overwrite the earlier.
                    string key = $"{category}/{name}".Replace("//", "/").TrimStart('/');

                    restrictions[key] = api.LoadAsset<RestrictionData>(restrictionPath);

                    if (api.Assets.Exists(transformationPath))
                    {
                        transformations[key] = api.LoadAsset<Dictionary<string, ModelTransform>>(transformationPath);
                    }
                }
            }
        }
        public void AddCustomIcons()
        {
            List<string> iconList = new List<string> { "nose-side", "drop-earrings", "eye-left", "eye-right", "nose",
            "earrings-right", "earrings-left", "palm-right", "palm-left"};
            foreach (var icon in iconList)
            {
                capi.Gui.Icons.CustomIcons["canjewelry:" + icon] = delegate (Context ctx, int x, int y, float w, float h, double[] rgba)
                {
                    AssetLocation location = new AssetLocation("canjewelry:textures/icons/" + icon + ".svg");
                    IAsset svgAsset = capi.Assets.TryGet(location, true);
                    int value = ColorUtil.ColorFromRgba(44, 44, 44, 204);
                    capi.Gui.DrawSvg(svgAsset, ctx.GetTarget() as ImageSurface, x, y, (int)w, (int)h, new int?(value));
                };
            }
        }
        /// <summary>
        /// Server-side initialization.
        /// Loads config, applies server patches,
        /// registers commands and networking,
        /// and injects custom drops.
        /// </summary>
        /// <summary>
        /// The wearable adornments and the display stands ship as a separate mod since 0.7.0.
        /// Without it the core still runs, and items already in the world survive as engine
        /// placeholders — but the stands' block entities do not, so their contents are lost on the
        /// first save of an affected chunk. That is worth one loud banner.
        /// </summary>
        private void WarnIfAdornmentsMissing(ICoreAPI api)
        {
            if (api.ModLoader.IsModEnabled(AdornmentsModId)) return;
            if (config?.suppressAdornmentsWarning == true) return;

            Mod.Logger.Warning(
                "\n============================================================\n" +
                "  C&N Jewelry: the mod '{0}' is not installed.\n" +
                "  Wearable adornments and the display stands live there now.\n" +
                "  Jewelry already in chests or worn by players is kept and\n" +
                "  comes back intact once that mod is installed.\n" +
                "  The CONTENTS OF DISPLAY STANDS, however, are lost as soon\n" +
                "  as an affected chunk is saved. Take the stands apart before\n" +
                "  playing on without it.\n" +
                "  Set 'suppressAdornmentsWarning' in the config to hide this.\n" +
                "============================================================",
                AdornmentsModId);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;
            // Normally already loaded in StartPre; kept as a fallback for the case StartPre was skipped.
            if (config == null)
            {
                loadConfig(sapi);
            }
            ServerPatcher.ApplyPatches(api, harmonyID, ref harmonyInstance);
            WarnIfAdornmentsMissing(api);

            config.InitColors();
            api.RegisterEntityBehaviorClass("cangembuffaffected", typeof(CANGemBuffAffected));
            
            serverChannel = sapi.Network.RegisterChannel("canjewelry");
            serverChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => AddBehaviorAndSocketNumber(sapi));
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            commands.RegisterCommands.registerServerCommands(sapi);

            serverChannel.SetMessageHandler<SyncCANJewelryPacket>((player, packet) =>
            {
                sendNewValues(player);
            });

            serverChannel.RegisterMessageType(typeof(CuttingSettingsPacket));
            serverChannel.SetMessageHandler<CuttingSettingsPacket>((player, packet) =>
            {
                // A packet is a packet whoever sends it, so the privilege is checked here and not
                // only on the command that opens the dialogue.
                if (!player.HasPrivilege(Privilege.controlserver)) return;
                if (packet == null) return;

                // Values arrive from a dialogue that clamps them, but a hand written packet does
                // not have to, and cuttingVoxelsPerClick feeds an array walk.
                config.cuttingVoxelsPerClick = GameMath.Clamp(packet.VoxelsPerClick, 1, 8);
                config.cuttingHoldStrikeIntervalMs = GameMath.Clamp(packet.HoldStrikeIntervalMs, 0, 2000);
                config.cuttingDurabilityPerVoxel = packet.DurabilityPerVoxel;
                config.cuttingInstantComplete = packet.InstantComplete;
                config.cuttingSpareRecipeVoxels = packet.SpareRecipeVoxels;

                string mode = packet.AccessMode?.ToLowerInvariant();
                if (mode == "disabled" || mode == "enabled" || mode == "whitelist" || mode == "blacklist")
                {
                    config.cuttingAccessMode = mode;
                }

                config.cuttingWhitelist = CuttingSettingsPacket.SanitiseNames(packet.Whitelist);
                config.cuttingBlacklist = CuttingSettingsPacket.SanitiseNames(packet.Blacklist);

                commands.RegisterCommands.applyAndBroadcastConfig();
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
            ServerMain.ClassRegistry.RegisterInventoryClass("additionaljewelrycharacter", typeof(InventoryCharacterAdditionalJewelry));
        }
        public void OnPlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (!canjewelry.config.TurnOffBuffs)
            {
                var plBeh = byPlayer.Entity.GetBehavior<CANGemBuffAffected>();
                if (plBeh != null)
                {
                    if (!plBeh.initialized)
                    {
                        plBeh.TryToAddSlotModified();
                    }
                }
            }
        }
        public override void Dispose()
        {
            base.Dispose();
            // defaultInventories is a static field on the engine's PlayerInventoryManager and
            // outlives this mod's assembly. Undo the StartPre append, otherwise a stale
            // "additionaljewelrycharacter" entry remains after the mod is unloaded and the next
            // world load crashes trying to instantiate an inventory class that is no longer registered.
            PlayerInventoryManager.defaultInventories = PlayerInventoryManager.defaultInventories.Remove("additionaljewelrycharacter");
            if (harmonyInstance != null)
            {
                harmonyInstance.UnpatchAll(harmonyID);
                harmonyInstance.UnpatchAll(harmonyID + "_client");
                harmonyInstance.UnpatchAll(harmonyID + "_server");
            }
            guideDialog = null;
            capi = null;
            sapi = null;
            serverChannel = null;
            clientChannel = null;
            config = null;
            gems_textures?.Clear();
            gems_textures = null;
            gems_textures_pngs?.Clear();
            gems_textures_pngs = null;
            gemCuttingRecipes = null;
            CANItemWearable.NotVisTexture = null;
        }
        /// <summary>
        /// Adds socket behavior, gem attributes, and custom variants
        /// to items based on configuration.
        /// Can be executed on both client and server.
        /// </summary>
        /// <param name="serverSide">
        /// True if executed on server, false if on client.
        /// </param>
        public void AddBehaviorAndSocketNumber(ICoreAPI api)
        {
            // Socket counts only reach the tooltip through the attributes written below, and on
            // the client this runs solely from the config network handler. One line here tells
            // apart "the config never arrived" from "the config arrived but is empty".
            api.Logger.Notification("[canjewelry] applying config on {0}: {1} socket rules, {2} custom variants, {3} pan drops",
                api.Side, config.items_codes_with_socket_count_and_tiers.Count,
                config.custom_variants_sockets_tiers.Count, config.panningDrops?.Count ?? 0);

            // The rules below are about to be rewritten onto the items, so anything parsed from the
            // previous ones is stale. Matters most on the client, where this runs again once the
            // server's config arrives.
            EncrustableCB.ClearVariantTiersCache();

            ApplySimpleJewelry(api);
            ApplyGemBehaviors(api);
            ApplyCustomVariants(api);
            ApplyTemporalGrasp(api);
            ApplySocketLevels(api);
        }

        private void ApplySimpleJewelry(ICoreAPI api)
        {
            foreach (var it in config.items_codes_with_socket_count_and_tiers)
            {
                if (it.Value == null || it.Value.Length == 0) continue;

                Item[] items = api.World.SearchItems(new AssetLocation(it.Key));
                if (items.Length == 0)
                {
                    if (config.debugMode)
                        api.Logger.VerboseDebug($"[canjewelry] Item \"{it.Key}\" not found");
                    continue;
                }

                JToken tiersToken = JToken.Parse(JsonConvert.SerializeObject(it.Value));
                foreach (Item item in items)
                {
                    if (!item.HasBehavior<EncrustableCB>())
                        item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new EncrustableCB(item));

                    EnsureAttributes(item);
                    item.Attributes.Token[CANJWConstants.SOCKETS_TIERS_STRING] = tiersToken.DeepClone();
                    item.Attributes.Token[CANJWConstants.SOCKETS_NUMBER_STRING] = it.Value.Length;
                    item.Attributes = new JsonObject(item.Attributes.Token);
                }
            }
        }

        private void ApplyGemBehaviors(ICoreAPI api)
        {
            Item[] roughGems = api.World.SearchItems(new AssetLocation("canjewelry:gem-rough-*"))
                .Append(api.World.SearchItems(new AssetLocation("game:gem-*-rough")));

            foreach (var gem in roughGems)
            {
                string gemType = gem.Code.Path.Split('-').Last();
                if (config.gem_type_to_buff.ContainsKey(gemType))
                    gem.Attributes.Token["canGemTypeToAttribute"] = config.gem_type_to_buff[gemType];

                if (!gem.HasBehavior<CANGemCuttableCB>())
                    gem.CollectibleBehaviors = gem.CollectibleBehaviors.Append(new CANGemCuttableCB(gem));
            }

            foreach (var gem in api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*")))
            {
                string gemType = gem.Code.Path.Split('-').Last();
                if (config.gem_type_to_buff.ContainsKey(gemType))
                    gem.Attributes.Token["canGemTypeToAttribute"] = config.gem_type_to_buff[gemType];
            }
        }

        private void ApplyCustomVariants(ICoreAPI api)
        {
            foreach (var cvst in config.custom_variants_sockets_tiers)
            {
                Item[] items = api.World.SearchItems(new AssetLocation(cvst.ItemCode));
                if (items.Length == 0) continue;

                JToken tiersToken = JToken.Parse(JsonConvert.SerializeObject(cvst.SocketTiers));
                foreach (Item item in items)
                {
                    if (!item.HasBehavior<EncrustableCB>())
                        item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new EncrustableCB(item));

                    EnsureAttributes(item);
                    item.Attributes.Token[CANJWConstants.CAN_CUSTOM_VARIANTS] = tiersToken.DeepClone();
                    item.Attributes.Token[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY] = cvst.AttributeKey;
                }
            }
        }

        private void ApplyTemporalGrasp(ICoreAPI api)
        {
            if (!config.TemporalGraspEnabled) return;
            foreach (var code in config.TemporalGraspBlockList)
            {
                foreach (var block in api.World.SearchBlocks(new AssetLocation(code)))
                {
                    if (!block.HasBehavior<CANTemporalGraspBB>())
                        block.BlockBehaviors = block.BlockBehaviors.Append(new CANTemporalGraspBB(block));
                }
            }
        }

        private void ApplySocketLevels(ICoreAPI api)
        {
            foreach (var it in config.LevelOfSocketByType)
            {
                Item[] items = api.World.SearchItems(new AssetLocation(it.Key));
                if (items.Length == 0)
                {
                    if (config.debugMode)
                        api.Logger.VerboseDebug($"[canjewelry] Item \"{it.Key}\" not found");
                    continue;
                }

                JToken levelToken = JToken.Parse(JsonConvert.SerializeObject(it.Value));
                foreach (Item item in items)
                {
                    EnsureAttributes(item);
                    item.Attributes.Token[CANJWConstants.LEVEL_OF_SOSCKET_STRING] = levelToken.DeepClone();
                }
            }
        }

        private static void EnsureAttributes(Item item)
        {
            if (item.Attributes == null)
                item.Attributes = new JsonObject(JToken.Parse("{}"));
        }
        /// <summary>
        /// Sends the current configuration to a client.
        /// </summary>
        /// <param name="byPlayer">Target player</param>
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

        // The config is always written back after loading, and its stored shape changes between
        // versions (0.6.20 moved most sections to a compact form that older builds cannot read).
        // Copying the file aside before the first rewrite of a new version keeps a downgrade or a
        // botched migration recoverable. Never fatal: a failed backup only costs a warning.
        private void BackupConfigFile(ICoreAPI api, string fromVersion, string toVersion)
        {
            try
            {
                // Fully qualified: Cairo.Path is in scope here and collides with System.IO.Path.
                string fileName = this.Mod.Info.ModID + ".json";
                string path = System.IO.Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, fileName);
                if (!System.IO.File.Exists(path)) return;

                string backupName = string.Format("{0}_{1}.json.bak", this.Mod.Info.ModID,
                    string.IsNullOrEmpty(fromVersion) ? "pre-versioning" : fromVersion);
                string backupPath = System.IO.Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, backupName);
                // Keep the first backup made for that version: it is the one written by the
                // version being left, later runs would only overwrite it with migrated content.
                if (System.IO.File.Exists(backupPath)) return;

                System.IO.File.Copy(path, backupPath);
                api.Logger.Notification("[canjewelry] config of version {0} backed up to {1} before migrating to {2}",
                    fromVersion, backupName, toVersion);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[canjewelry] could not back up the config before migrating: {0}", e.Message);
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
            catch (Exception)
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
                // Seeds item_groups so the freshly written config has them, and folds the
                // copied item lists back into "$armor" style references on save.
                config.ExpandItemGroups();
                //make copy of the old config and new to old file
                try
                {
                    api.StoreModConfig<OldConfig>(oldConfig, this.Mod.Info.ModID + "_old.json");
                    api.StoreModConfig<Config>(config, this.Mod.Info.ModID + ".json");
                }
                catch(Exception)
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
                        int[] tmp = new int[itemIter.Value];
                        Array.Fill(tmp, 3);
                        config.items_codes_with_socket_count_and_tiers[itemIter.Key] = tmp;
                    }
                    config.items_codes_with_socket_count.Clear();
                    config.items_codes_with_socket_count["moved to items_codes_with_socket_count_and_tiers"] = 42;
                    if(config.LevelOfSocketByType.Count == 0)
                    {
                        config.LevelOfSocketByType = new Dictionary<string, int> { { "canjewelry:cansocket-gold", 2 },
                                                                { "canjewelry:cansocket-silver", 2 },
                                                                { "canjewelry:cansocket-bismuthbronze", 1 },
                                                                { "canjewelry:cansocket-tinbronze", 1},
                                                                { "canjewelry:cansocket-blackbronze", 1},
                                                                { "canjewelry:cansocket-iron", 2},
                                                                { "canjewelry:cansocket-meteoriciron", 2},
                                                                { "canjewelry:cansocket-steel", 3 }};
                    }
                }
                if (config == null)
                { 
                    config = new Config();
                    config.FillDefaultValues();
                    config.config_version = api.ModLoader.Mods.FirstOrDefault(mod => mod.Info.ModID == "canjewelry")?.Info.Version ?? "0.0.0";
                    // Points the serializer at this config's own groups before the first save.
                    config.ExpandItemGroups();
                    api.StoreModConfig<Config>(config, this.Mod.Info.ModID + ".json");
                    if (config.debugMode)
                    {
                        api.Logger.VerboseDebug("[canjewelry] " + this.Mod.Info.ModID + ".json" + " new config created and stored.");
                    }
                    return;
                }
                var currVersion = api.ModLoader.Mods.FirstOrDefault(mod => mod.Info.ModID == "canjewelry")?.Info.Version ?? "0.0.0";
                if (currVersion != null)
                {
                    if (currVersion != config.config_version)
                    {
                        BackupConfigFile(api, config.config_version, currVersion);
                        config.FillDefaultValues(true);
                        config.config_version = currVersion;
                    }
                }

                // Entries this mod version knows about and the config does not - typically items
                // or metals added since the config was written. Adding them is opt-in, but the
                // count is always reported so the gap does not stay invisible.
                int missingDefaults = config.AddMissingDefaults(config.add_missing_defaults_on_update);
                if (missingDefaults > 0)
                {
                    if (config.add_missing_defaults_on_update)
                    {
                        api.Logger.Notification("[canjewelry] added {0} socket entries missing from the config", missingDefaults);
                    }
                    else
                    {
                        // Missing entries are a state, not an event, so this would otherwise
                        // repeat on every single start for a config that is missing them on
                        // purpose. Kept at verbose level: whoever looks for the gap finds it.
                        api.Logger.VerboseDebug("[canjewelry] the config is missing {0} socket entries this version knows about - set add_missing_defaults_on_update to true to append them",
                            missingDefaults);
                    }
                }

                // Turns the "$armor" references into the actual item codes. Has to run before
                // anything reads buffNameToPossibleItem, and after the whole file is loaded so
                // that admin edited item_groups are already in place.
                config.ExpandItemGroups();

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
