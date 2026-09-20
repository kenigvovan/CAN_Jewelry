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
using canjewelry.src.render;
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

        internal const string AdditionalJewelryInventory = "additionaljewelrycharacter";

        internal static bool AdditionalJewelrySlotsEnabled => config?.enableAdditionalJewelrySlots != false;

        /// <summary>
        /// The player's extra jewelry inventory, or null when the slots are switched off. Saves made
        /// while they were on still carry the inventory, so the flag is checked here rather than
        /// relying on the inventory being absent.
        /// </summary>
        internal static IInventory GetAdditionalJewelryInventory(IPlayer player)
            => AdditionalJewelrySlotsEnabled ? player?.InventoryManager.GetOwnInventory(AdditionalJewelryInventory) : null;

        /// <summary>
        /// Gem poses the admin put into the server's pose folder, as JSON, or null when there are
        /// none. Read once at startup and handed to every client that does not have them yet.
        /// </summary>
        private static string serverGemVisuals;

        /// <summary>Ceiling on the pose payload the server hands out, in characters of JSON.</summary>
        private const int MaxGemVisualsChars = 4 * 1024 * 1024;

        /// <summary>Fingerprint of <see cref="serverGemVisuals"/>, empty when there are none.</summary>
        private static string serverGemVisualsHash = "";

        // What actually goes on the wire, compressed once instead of once per recipient. The config
        // is thousands of entries and the poses up to four megabytes, and both are the same bytes
        // for everyone - gzipping them per player made an admin command cost N compressions and
        // turned a request any client can send into a way to keep the server busy.
        private static byte[] configPayload;
        private static byte[] gemVisualsPayload;

        /// <summary>
        /// Throws away the prepared config bytes, so the next player to ask gets the current config.
        /// Called wherever the config changes - a command, or the file being loaded.
        /// </summary>
        internal static void InvalidateConfigPayload()
        {
            configPayload = null;
        }

        /// <summary>
        /// Everything derived from the config that has to be worked out again once it is replaced at
        /// runtime. One place for it, because the places that cache something from the config are
        /// not the places that know it changed - the pan built its drop table on block load, long
        /// before the server's config arrived, and kept panning by that table for the session.
        /// </summary>
        internal static void OnConfigReplaced()
        {
            InvalidateConfigPayload();
            // Poses resolve through the item groups the config owns, so answers cached against the
            // previous groups are no longer valid.
            CANGemVisualRegistry.ClearResolveCache();
            EncrustableCB.ClearVariantTiersCache();
            blocks.CANBlockPan.OnConfigReplaced();
        }

        // When each player last had the config sent to them, by player uid. A client asks once on
        // join, so anything more often than this is either a broken client or one trying to make the
        // server work; either way the answer it already has is still current.
        private static readonly Dictionary<string, long> lastConfigRequestMs = new Dictionary<string, long>();

        private const long ConfigRequestIntervalMs = 5000;

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

        // Event subscriptions this instance made, kept so Dispose can take them back off. A lambda
        // written straight into the += cannot be unsubscribed, and the engine's event bus outlives
        // a world: leaving them on means the next join runs the previous session's handlers too.
        private Action dropGemMeshes;
        private PlayerEventDelegate onClientPlayerJoin;
        private PlayerDelegate onPlayerDisconnect;

        /// <summary>
        /// How many sides of the mod are running. In single player the client and the server are two
        /// instances in one process sharing every static here, so whichever side is disposed first
        /// must not pull the shared state out from under the other.
        /// </summary>
        private static int liveSides;

        /// <summary>Which side this instance is, so Dispose only clears what it set up.</summary>
        private EnumAppSide side;

        /// <summary>Serialises the one-time config load between the two sides of a single player world.</summary>
        private static readonly object configLoadLock = new object();

        private GuiDialogJewelryGuide guideDialog;
        private GuiDialogCuttingSettings cuttingSettingsDialog;
        private GuiDialogGemVisualDebug gemVisualDebugDialog;
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
            //
            // Under a lock and checked again inside it: in single player both sides run StartPre,
            // and two of them finding config null at once meant loading the file twice and writing
            // it back twice - the second write against a config the first had already migrated.
            if (config == null)
            {
                lock (configLoadLock)
                {
                    if (config == null) loadConfig(api);
                }
            }

            // Anything registered from here on misses this run's defaults. It is still recorded and
            // applies on the next start, so the registry only warns.
            CANJewelryRegistry.Sealed = true;

            // Guard against appending twice within a single VS process (e.g. leave and rejoin a
            // world with the mod active), which would otherwise leave a duplicate entry behind.
            // The class stays registered either way: a save that already holds the inventory
            // would otherwise lose it on the server and break player data on the client.
            bool listed = PlayerInventoryManager.defaultInventories.Contains(AdditionalJewelryInventory);
            if (AdditionalJewelrySlotsEnabled && !listed)
            {
                PlayerInventoryManager.defaultInventories = PlayerInventoryManager.defaultInventories.Append(AdditionalJewelryInventory);
            }
            else if (!AdditionalJewelrySlotsEnabled && listed)
            {
                // Left behind by an earlier world of this process that had the slots on.
                PlayerInventoryManager.defaultInventories = PlayerInventoryManager.defaultInventories.Remove(AdditionalJewelryInventory);
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
            side = api.Side;
            liveSides++;
            base.Start(api);
            harmonyInstance = new Harmony(harmonyID);

            // Asking a freshly built Harmony instance what it has patched always answers "nothing",
            // so the old guard here never fired. What it meant to prevent is patching the same
            // method twice within one process - the two sides of a single player world both run
            // this - and that is what Harmony's own registry answers.
            if (!IsPatchedByUs(typeof(CollectibleObject).GetMethod("GetMaxDurability")))
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

            // Tuning menu for the gem poses. Same gate as the other admin tooling: creative, or the
            // privilege that the server commands ask for. It only edits client side visuals, but
            // it is a development tool and has no business being on a hotkey for everyone.
            api.Input.RegisterHotKey("canjewelrygemvisual", "CAN Jewelry gem visual debug",
                Vintagestory.API.Client.GlKeys.G, Vintagestory.API.Client.HotkeyType.GUIOrOtherControls, false, false, true);
            api.Input.SetHotKeyHandler("canjewelrygemvisual", comb =>
            {
                var player = api.World?.Player;
                if (player == null) return false;
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative
                    && !player.HasPrivilege(Vintagestory.API.Server.Privilege.controlserver))
                {
                    return false;
                }

                // Built once and reused: the dialogue reads the held item in OnGuiOpened, so a
                // fresh instance per press would buy nothing and leave the old ones registered.
                gemVisualDebugDialog ??= new GuiDialogGemVisualDebug(api);
                if (gemVisualDebugDialog.IsOpened()) gemVisualDebugDialog.TryClose();
                else gemVisualDebugDialog.TryOpen();
                return true;
            });

            commands.RegisterCommands.registerClientCommands(api);

            // A built composite holds the atlas positions of its textures, which a reload moves.
            // Without this the gems keep the old positions and come out wearing whatever texture
            // now sits there, until the player leaves the world.
            // Kept in a field rather than written inline: the same delegate object is needed again
            // in Dispose to take the subscription back off, and a fresh lambda would not match.
            dropGemMeshes = () =>
            {
                EncrustableCB.ClearMeshCache(api);
                CANGemMeshBuilder.ClearBaseMeshCache();
            };
            api.Event.ReloadTextures += dropGemMeshes;
            api.Event.ReloadShapes += dropGemMeshes;
            api.Event.LeaveWorld += dropGemMeshes;

            clientChannel = api.Network.RegisterChannel("canjewelry");
            clientChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            clientChannel.RegisterMessageType(typeof(GemVisualsPacket));
            clientChannel.RegisterMessageType(typeof(CuttingSettingsPacket));
            clientChannel.SetMessageHandler<GemVisualsPacket>((packet) =>
            {
                if (packet == null) return;

                // Our own world: the server behind it is this process, and it read the very folder
                // this client reads. Taking the poses back from it would only put a copy made when
                // the world started above the files themselves - and above anything the debug menu
                // writes while playing, since server poses outrank local ones. Ignored, so tuning
                // shows up at once here and a dedicated server still speaks for everyone else.
                if (capi.IsSinglePlayer) return;

                // Poses the admin set win over everything local, so whatever was already built with
                // the local ones has to be built again.
                CANGemVisualRegistry.ApplyServerRules(capi, packet.Rules, packet.Hash);
                EncrustableCB.ClearMeshCache(capi);
            });
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
                    OnConfigReplaced();
                    // The tab was set up from the local config before the server's arrived.
                    harmony.harmPatch.ApplyAdditionalJewelryTabVisibility();
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
            onClientPlayerJoin = byPlayer =>
            {
                if (byPlayer == null || capi?.World?.Player == null || byPlayer != capi.World.Player) return;

                if (clientChannel.Connected)
                {
                    RequestConfig();
                    return;
                }

                // The channel is not up yet, which happens on a slow join; ask again once it is.
                capi.Event.RegisterCallback(dt =>
                {
                    if (clientChannel != null && clientChannel.Connected) RequestConfig();
                }, 60 * 1000);
            };
            api.Event.PlayerJoin += onClientPlayerJoin;

        }
        /// <summary>
        /// Asks the server for the config and, with it, for the gem poses the client does not have.
        /// The hash goes along so the server can skip poses this client already carries — the retry
        /// path used to leave it out, which made the server resend the whole set.
        /// </summary>
        private static void RequestConfig()
        {
            clientChannel?.SendPacket(new SyncCANJewelryPacket()
            {
                GemVisualsHash = CANGemVisualRegistry.AppliedServerRulesHash
            });
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            if (api.Side == EnumAppSide.Server)
            {
                var restrictionGroupsServer = DiscoverRestrictionGroups(api);
                LoadData(api, restrictionGroupsServer);

                // The server never renders a gem, so it does not load poses for itself - it only
                // reads what the admin put in the pose folder, to hand it to the clients.
                CANGemVisualRegistry.SeedUserRulesFromAssets(api);
                serverGemVisuals = CANGemVisualRegistry.CollectUserRulesJson(api);

                // Sent to every player who joins, so a folder that has grown out of hand is refused
                // here rather than on the wire.
                if (serverGemVisuals != null && serverGemVisuals.Length > MaxGemVisualsChars)
                {
                    api.Logger.Error(
                        "[canjewelry] gem visuals: {0} holds {1} chars of poses, over the {2} limit - sending none",
                        CANGemVisualRegistry.UserRuleDirectory, serverGemVisuals.Length, MaxGemVisualsChars);
                    serverGemVisuals = null;
                }

                serverGemVisualsHash = CANGemVisualRegistry.Fingerprint(serverGemVisuals);
                if (serverGemVisuals != null)
                {
                    api.Logger.Notification("[canjewelry] gem visuals: sending the poses of {0} to clients",
                        CANGemVisualRegistry.UserRuleDirectory);
                }
            }
            // Gem poses are needed where the meshes are built, so unlike the restrictions above
            // this one is a client affair. The server never places a gem on a model.
            if (api.Side == EnumAppSide.Client)
            {
                CANGemVisualRegistry.LoadFromAssets(api);
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

            config.InitColors(api.Logger);
            api.RegisterEntityBehaviorClass("cangembuffaffected", typeof(CANGemBuffAffected));
            
            serverChannel = sapi.Network.RegisterChannel("canjewelry");
            serverChannel.RegisterMessageType(typeof(SyncCANJewelryPacket));
            serverChannel.RegisterMessageType(typeof(GemVisualsPacket));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => AddBehaviorAndSocketNumber(sapi));
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            commands.RegisterCommands.registerServerCommands(sapi);

            serverChannel.SetMessageHandler<SyncCANJewelryPacket>((player, packet) =>
            {
                // Anyone connected can send this, and answering it is the most expensive thing the
                // mod does per packet, so a client that asks again within seconds is simply told
                // nothing - the answer it already has has not changed.
                if (!AllowConfigRequest(player)) return;

                sendNewValues(player);
                sendGemVisualsIfChanged(player, packet?.GemVisualsHash);
            });
            onPlayerDisconnect = player => lastConfigRequestMs.Remove(player.PlayerUID);
            api.Event.PlayerDisconnect += onPlayerDisconnect;

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
            // Both sides share this static and either of them may have cleared it by now.
            if (config == null || byPlayer?.Entity == null) return;

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
        /// <summary>
        /// Whether one of our patch ids already sits on this method. Answered by Harmony's process
        /// wide registry rather than by our own instance, which is what makes it true for the second
        /// side of a single player world.
        /// </summary>
        private static bool IsPatchedByUs(System.Reflection.MethodBase method)
        {
            if (method == null) return false;

            HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null) return false;

            foreach (var patch in patches.Postfixes)
            {
                if (patch.owner != null && patch.owner.StartsWith(harmonyID)) return true;
            }
            foreach (var patch in patches.Prefixes)
            {
                if (patch.owner != null && patch.owner.StartsWith(harmonyID)) return true;
            }
            return false;
        }

        public override void Dispose()
        {
            base.Dispose();
            // defaultInventories is a static field on the engine's PlayerInventoryManager and
            // outlives this mod's assembly. Undo the StartPre append, otherwise a stale
            // "additionaljewelrycharacter" entry remains after the mod is unloaded and the next
            // world load crashes trying to instantiate an inventory class that is no longer registered.
            PlayerInventoryManager.defaultInventories = PlayerInventoryManager.defaultInventories.Remove("additionaljewelrycharacter");
            // Unpatching goes by patch id, not by the instance that applied it, so a fresh Harmony
            // undoes our patches whatever the field happens to hold - and it holds whichever of the
            // three instances was created last, since the side patchers overwrite it through a ref
            // parameter. Only this side's patches go here: in single player the other side is still
            // playing, and this used to unpatch its half as well.
            var unpatcher = new Harmony(harmonyID);
            unpatcher.UnpatchAll(side == EnumAppSide.Client ? harmonyID + "_client" : harmonyID + "_server");
            guideDialog = null;
            // Owns a framebuffer, so it has to be handed back rather than just dropped.
            gemVisualDebugDialog?.Dispose();
            gemVisualDebugDialog = null;

            // Every subscription this instance made, taken back off. Without this a second join in
            // one process runs the previous session's handlers as well - against a client api that
            // no longer has a world.
            if (side == EnumAppSide.Client)
            {
                if (capi != null)
                {
                    if (dropGemMeshes != null)
                    {
                        capi.Event.ReloadTextures -= dropGemMeshes;
                        capi.Event.ReloadShapes -= dropGemMeshes;
                        capi.Event.LeaveWorld -= dropGemMeshes;
                    }
                    if (onClientPlayerJoin != null) capi.Event.PlayerJoin -= onClientPlayerJoin;
                }
                dropGemMeshes = null;
                onClientPlayerJoin = null;

                capi = null;
                clientChannel = null;
            }
            else
            {
                if (sapi != null)
                {
                    sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
                    if (onPlayerDisconnect != null) sapi.Event.PlayerDisconnect -= onPlayerDisconnect;
                }
                onPlayerDisconnect = null;

                serverGemVisuals = null;
                serverGemVisualsHash = "";
                gemVisualsPayload = null;
                lastConfigRequestMs.Clear();

                sapi = null;
                serverChannel = null;
            }

            // What both sides share goes only when the last of them is gone - in single player the
            // other side is still running when the first Dispose arrives.
            if (--liveSides <= 0)
            {
                liveSides = 0;
                // The one patch applied from Start, which is per process rather than per side.
                unpatcher.UnpatchAll(harmonyID);
                harmonyInstance = null;

                config = null;
                configPayload = null;
                Config.ActiveItemGroups = null;
                gems_textures?.Clear();
                gems_textures = null;
                gems_textures_pngs?.Clear();
                gems_textures_pngs = null;
                BEJewelGrinder.gemTypeToColor?.Clear();
                CANItemWearable.NotVisTexture = null;
                // gemCuttingRecipes is filled and owned by GemCuttingRecipeSystem, and every reader
                // of it dereferences it without a null check. It used to be nulled here, which left
                // the other mod system holding a field this one had emptied.
                Instance = null;
            }
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
            if (byPlayer.ConnectionState == EnumClientState.Offline) return;

            // Serialised and compressed once and kept: the bytes are the same for every player, and
            // this used to be redone per recipient - an admin command on a full server meant one
            // full serialise and gzip of the whole config per player online.
            byte[] payload = configPayload;
            if (payload == null)
            {
                payload = configPayload = utils.CommonFunctions.Gzip(JsonConvert.SerializeObject(config));
            }

            serverChannel.SendPacket(new SyncCANJewelryPacket() { ConfigGz = payload }, byPlayer);
        }

        /// <summary>
        /// Whether this player's request for the config is answered. One on join is what the client
        /// sends; anything beyond that inside a few seconds gets nothing back.
        /// </summary>
        private static bool AllowConfigRequest(IServerPlayer byPlayer)
        {
            if (byPlayer?.PlayerUID == null) return false;

            long now = sapi?.World?.ElapsedMilliseconds ?? 0;
            if (lastConfigRequestMs.TryGetValue(byPlayer.PlayerUID, out long last)
                && now - last < ConfigRequestIntervalMs)
            {
                return false;
            }

            lastConfigRequestMs[byPlayer.PlayerUID] = now;
            return true;
        }

        /// <summary>
        /// Sends the gem poses the admin put into the server's pose folder, and only those: the
        /// poses that ship in assets are already on the client, and a server that changed nothing
        /// sends nothing at all.
        /// </summary>
        /// <param name="byPlayer">Target player</param>
        /// <param name="clientHash">Fingerprint of the poses the client says it already has</param>
        private void sendGemVisualsIfChanged(IServerPlayer byPlayer, string clientHash)
        {
            if (byPlayer.ConnectionState == EnumClientState.Offline) return;
            // Nothing here and nothing there: the client has no server poses to take back off.
            if (serverGemVisuals == null && string.IsNullOrEmpty(clientHash)) return;
            if (serverGemVisualsHash == (clientHash ?? "")) return;

            // Same story as the config, only bigger: up to four megabytes of poses, identical for
            // everyone, and the hash the client sends decides whether they are sent at all - so a
            // client that lies about its hash used to make the server gzip the lot again each time.
            if (gemVisualsPayload == null && serverGemVisuals != null)
            {
                gemVisualsPayload = utils.CommonFunctions.Gzip(serverGemVisuals);
            }

            serverChannel.SendPacket(new GemVisualsPacket()
            {
                RulesGz = gemVisualsPayload,
                Hash = serverGemVisualsHash
            },
            byPlayer);
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

        /// <summary>
        /// Whether the config file already carries a <c>config_version</c>, which every file of the
        /// current format has and no file of the old one does. Read as raw json rather than through
        /// a model, so a file that binds to neither shape still answers the question.
        /// </summary>
        private static bool HasConfigVersion(ICoreAPI api, string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, fileName);
                if (!System.IO.File.Exists(path)) return false;

                JObject parsed = JObject.Parse(System.IO.File.ReadAllText(path));
                return parsed[nameof(Config.config_version)] != null;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[canjewelry] could not look at {0} to tell its format apart: {1}",
                    fileName, e.Message);
                return false;
            }
        }

        private void loadConfig(ICoreAPI api)
        {
            string fileName = this.Mod.Info.ModID + ".json";

            // Which format the file is in is decided by config_version, not by whether the current
            // format fails to parse. The old test was "deserialising the new format threw", and the
            // only thing that actually threw was one value of the old shape - so a current config
            // missing that value read as an all-default old config and was then overwritten with
            // defaults, taking the admin's settings with it.
            OldConfig oldConfig = null;
            if (!HasConfigVersion(api, fileName))
            {
                try
                {
                    oldConfig = api.LoadModConfig<OldConfig>(fileName);
                }
                catch (Exception e)
                {
                    api.Logger.Warning("[canjewelry] {0} is neither the current nor the old config format: {1}",
                        fileName, e.Message);
                }
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
                try
                {
                    config = api.LoadModConfig<Config>(fileName);
                }
                catch (Exception e)
                {
                    // A hand edited file with a typo used to throw out of StartPre and fail the
                    // world load with nothing but a parser message. The broken file is kept, under
                    // a name that says what happened, and the world starts on defaults.
                    api.Logger.Error("[canjewelry] could not read {0}, starting from defaults. {1}", fileName, e);
                    BackupConfigFile(api, "unreadable", "defaults");
                    config = null;
                }
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
