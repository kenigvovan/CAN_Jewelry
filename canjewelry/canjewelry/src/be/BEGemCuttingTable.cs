using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using canjewelry.src.blocks;
using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.items;
using canjewelry.src.items.resource;
using canjewelry.src.jewelry;
using canjewelry.src.render;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.be
{
    // EnumVoxelMaterial and EnumAnvilPacket used to be declared here with exactly the members and
    // values vanilla gives them, shadowing Vintagestory.GameContent's own - which is why the rest of
    // the mod had to write "be.EnumVoxelMaterial" to say which one it meant. The vanilla ones are
    // used now; they are what the voxel bytes and the packet ids have always been.

    public class BlockEntityGemCuttingTable : BlockEntity, IRotatable
    {
        // Permanent data
        ItemStack workItemStack;
        public int SelectedRecipeId = -1;
        public byte[,,] Voxels = new byte[16, 14, 16]; // Only the first 2 bits of each byte are used and serialized
        // Temporary data
        float voxYOff = 10 / 16f;
        Cuboidf[] selectionBoxes = new Cuboidf[1];
        public int OwnMetalTier;
        GemCuttingWorkItemRenderer workitemRenderer;
        public int rotation = 0;
        public float MeshAngle;
        MeshData currentMesh;

        GuiDialog dlg;
        ItemStack returnOnCancelStack;


        public bool[,,] recipeVoxels
        {
            get
            {
                if (SelectedRecipe == null) return null;

                bool[,,] origVoxels = SelectedRecipe.Voxels;
                bool[,,] rotVoxels = new bool[origVoxels.GetLength(0), origVoxels.GetLength(1), origVoxels.GetLength(2)];

                if (rotation == 0) return origVoxels;

                for (int i = 0; i < rotation / 90; i++)
                {
                    for (int x = 0; x < origVoxels.GetLength(0); x++)
                    {
                        for (int y = 0; y < origVoxels.GetLength(1); y++)
                        {
                            for (int z = 0; z < origVoxels.GetLength(2); z++)
                            {
                                rotVoxels[z, y, x] = origVoxels[16 - x - 1, y, z];
                            }
                        }
                    }

                    origVoxels = (bool[,,])rotVoxels.Clone();
                }

                return rotVoxels;
            }
        }

        public GemCuttingRecipe SelectedRecipe
        {
            get { return canjewelry.gemCuttingRecipes.FirstOrDefault(r => r.RecipeId == SelectedRecipeId); }
        }

        public bool CanWorkCurrent
        {
            get { return workItemStack != null && workItemStack.Collectible.GetBehavior<CANGemCuttableCB>().CanWork(WorkItemStack); }
        }

        public ItemStack WorkItemStack
        {
            get { return workItemStack; }
        }
        private ICoreClientAPI capi;
        public string stoneType = "granite";
        public string metalType = "copper";

        /// <summary>
        /// The texture source for one mesh build: the block's own textures, with the stone and the
        /// metal of this particular table pointed at the material it was built from.
        /// </summary>
        private CANTexSource TexSource()
        {
            var source = new CANTexSource(this.capi, this.capi.BlockTextureAtlas, this.Block?.Textures,
                "gem cutting table " + this.Block?.Code);
            source.Overrides["granite"] = new AssetLocation("game:block/stone/polishedrock/" + this.stoneType + ".png");
            source.Overrides["iron"] = new AssetLocation("game:block/metal/sheet/" + this.metalType + "1.png");
            return source;
        }

        private MeshData getMesh(ITesselatorAPI tesselator)
        {
            // One entry per stone and metal combination, shared by every table in the world. It used
            // to be cleared on every call and then read, so it never once answered from the cache -
            // and clearing it threw away what every other table had just built.
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(this.Api,
                "canjewelry:gemCuttingTableBlockMeshes", () => new Dictionary<string, MeshData>());

            if (this.Api.World.BlockAccessor.GetBlock(this.Pos) is not BlockGemCuttingTable)
            {
                return null;
            }

            string key = this.stoneType + "-" + this.metalType;
            if (meshes.TryGetValue(key, out MeshData mesh)) return mesh;

            return meshes[key] = GenMesh(this.Api as ICoreClientAPI, null, tesselator, TexSource());
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape = null, ITesselatorAPI tesselator = null, ITexPositionSource textureSource = null)
        {
            if (shape == null)
            {
                shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json").Clone();
            }

            if (shape == null)
            {
                return null;
            }

            tesselator.TesselateShape("gemcuttingtable", shape, out var modeldata, textureSource ?? TexSource());
            return modeldata;
        }
        public BlockEntityGemCuttingTable() : base() { }
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            workItemStack?.ResolveBlockOrItem(api.World);
            // Read from the tree before Initialize, like the work item: unresolved, its collectible
            // is null, and handing it out took the client down.
            returnOnCancelStack?.ResolveBlockOrItem(api.World);

            if (api is ICoreClientAPI capi)
            {
                this.capi = api as ICoreClientAPI;
                if(currentMesh == null)
                {
                    this.currentMesh = this.getMesh(canjewelry.capi.Tesselator);
                }
                capi.Event.RegisterRenderer(workitemRenderer = new GemCuttingWorkItemRenderer(this, Pos, capi), EnumRenderStage.Opaque);
                capi.Event.RegisterRenderer(workitemRenderer, EnumRenderStage.AfterFinalComposition);

                RegenMeshAndSelectionBoxes();
                //this.currentMesh = this.getMesh(canjewelry.capi.Tesselator);
                //capi.Tesselator.TesselateBlock(Block, out this.getMesh(canjewelry.capi.Tesselator));
                capi.Event.ColorsPresetChanged += RegenMeshAndSelectionBoxes;
            }
            //string metalType = Block.Variant["metal"];
            //MetalPropertyVariant var;
           /* if (api.ModLoader.GetModSystem<SurvivalCoreSystem>().metalsByCode.TryGetValue(metalType, out var))
            {
              
                OwnMetalTier = var.Tier;
            }*/
        }


        internal Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
        {
            return selectionBoxes;
        }

        internal bool OnPlayerInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (world.Api.Side == EnumAppSide.Server && slot.Itemstack != null && slot.Itemstack?.Collectible is ItemHammer)
            {
                if(MatchesRecipeWithPossibleMistake(out int mistakes))
                {
                    FinishCut(byPlayer, mistakes);
                    return false;
                }
            }


            if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is CANItemGemChisel)
            {
                return RotateWorkItem(byPlayer.Entity.Controls.ShiftKey);
            }

            if (byPlayer.Entity.Controls.ShiftKey)
            {
                return TryPut(world, byPlayer, blockSel);
            }
            else
            {
                return TryTake(world, byPlayer, blockSel);
            }

        }

        // ---- cleaving a rough gem, before it is cut ----

        // Cleaving is a recipe like any cut: its pattern is the rough block with a groove chiselled
        // through it, and its output an icon item, cancleave-{preset}. Finishing it parts the gem.

        private const string CleavePrefix = "cancleave-";

        /// <summary>The preset of a cleave recipe (halves, thirds...), null for a cut.</summary>
        private static string CleavePresetOf(GemCuttingRecipe recipe)
        {
            string path = recipe?.Output?.ResolvedItemstack?.Collectible?.Code?.Path;
            return path != null && path.StartsWith(CleavePrefix) ? path.Substring(CleavePrefix.Length) : null;
        }

        /// <summary>
        /// The rough pieces a finished cleave gives, or null when the gem stays whole (a piece too
        /// light, or a subscriber said no). Mistakes cost weight, as in a cut.
        /// </summary>
        private List<ItemStack> CleavePieces(IPlayer byPlayer, ItemStack workItem, string preset, float mistakeMult)
        {
            var cfg = canjewelry.config.GemCarats;
            if (!cfg.Split.Presets.TryGetValue(preset, out float[] ratios)) return null;

            ITreeAttribute tree = workItem.Attributes;
            float carats = CANGemAppraisal.GetCarats(tree) * mistakeMult;
            string[] traits = CANGemAppraisal.GetTraits(tree);
            string[] known = CANGemAppraisal.GetKnownTraits(tree);

            var ev = new integration.SplitEvent
            {
                Player = byPlayer, Gem = workItem, Preset = preset,
                LossFraction = CANGemSplitter.CleaveLoss(cfg, traits, known)
            };
            canjewelry.Instance?.FireSplit(ev);
            if (!ev.Allowed) return null;

            List<CANGemSplitter.CleavedPiece> pieces = CANGemSplitter.Cleave(cfg, carats, traits, known, ratios, ev.LossFraction, Config.rand);
            if (pieces == null)
            {
                (byPlayer as IServerPlayer)?.SendIngameError("canjewelry-split",
                    Lang.Get("canjewelry:split-too-small", CANGemAppraisalCB.FormatCarats(cfg.Split.MinPieceCarats)));
                return null;
            }

            string gemType = tree.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET);
            int appraisal = CANGemAppraisal.GetAppraisal(tree);
            float[] range = CANGemAppraisal.GetCaratsRange(tree);
            var made = new List<ItemStack>();
            foreach (var piece in pieces)
            {
                ItemStack stack = CANGemSplitter.RoughStackFor(Api.World, cfg, gemType, piece.Carats);
                if (stack == null) return null;
                CANGemAppraisal.SetWeight(stack.Attributes, piece.Carats, piece.Traits);
                // A piece is a new stone: its own shape and clicks come with its first inspection.
                // What was seen of the whole carries over, the range scaled to the piece.
                if (appraisal >= CANGemAppraisal.Loupe)
                {
                    float ratio = carats > 0 ? piece.Carats / carats : 1f;
                    float[] pieceRange = range == null ? null : CANGemAppraisal.ScaleRange(range, ratio, cfg.Step);
                    CANGemAppraisal.SetKnowledge(stack.Attributes, CANGemAppraisal.Loupe, pieceRange, piece.Known);
                }
                made.Add(stack);
            }
            canjewelry.Instance?.FireXp(new integration.CANXpEvent { Player = byPlayer, Action = "split", Xp = 0f });
            return made;
        }

        private bool RotateWorkItem(bool ccw)
        {
            byte[,,] rotVoxels = new byte[16, 14, 16];

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 14; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (ccw)
                        {
                            rotVoxels[z, y, x] = Voxels[x, y, 16 - z - 1];
                        }
                        else
                        {
                            rotVoxels[z, y, x] = Voxels[16 - x - 1, y, z];
                        }

                    }
                }
            }

            rotation = (rotation + 90) % 360;

            this.Voxels = rotVoxels;
            RegenMeshAndSelectionBoxes();
            MarkDirty();

            return true;
        }

        private bool TryTake(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (workItemStack == null) return false;

            // The server hands the gem back and syncs the table; the client giving it too only
            // put a phantom stack in the inventory, or crashed on one it could not resolve.
            if (world.Side == EnumAppSide.Client) return true;
            ditchWorkItemStack(byPlayer);

            return true;
        }


        private bool TryPut(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (slot.Itemstack == null) return false;
            ItemStack stack = slot.Itemstack;

            if(!stack.Collectible.HasBehavior<CANGemCuttableCB>())
            {
                return false;
            }
            CANGemCuttableCB gemBehavior = stack.Collectible.GetBehavior<CANGemCuttableCB>();
            //IGemCuttingWorkable workableobj = stack.Collectible as IGemCuttingWorkable;

            /* foreach (var it in canjewelry.gemCuttingRecipes)
             {
                 canjewelry.capi.Logger.Error(it.Output.Code.ToString());
             }*/

            //if (workableobj == null) return false;
            int requiredTier = gemBehavior.GetRequiredGemCuttingTableTier(stack);
            if (requiredTier > OwnMetalTier)
            {
                if (world.Side == EnumAppSide.Client)
                {
                    (Api as ICoreClientAPI).TriggerIngameError(this, "toolowtier", Lang.Get("canjewelry:gemcuttingtable-toolowtier", requiredTier));
                }

                return false;
            }

            // Checked before TryPlaceOn, which overwrites Voxels: a gem with no recipe used to be
            // refused in silence and leave the grid of a rough gem behind.
            List<GemCuttingRecipe> matchingRecipes = null;
            if (workItemStack == null && stack.Collectible is not CANItemGemCuttingWorkItem)
            {
                matchingRecipes = gemBehavior.GetMatchingRecipes(stack);
                if (matchingRecipes.Count == 0)
                {
                    if (world.Side == EnumAppSide.Client)
                    {
                        (Api as ICoreClientAPI).TriggerIngameError(this, "norecipe", Lang.Get("canjewelry:gemcuttingtable-norecipe"));
                    }
                    return false;
                }
            }

            ItemStack newWorkItemStack = gemBehavior.TryPlaceOn(stack, this);
            if (newWorkItemStack != null)
            {
                if (workItemStack == null)
                {
                    workItemStack = newWorkItemStack;
                    rotation = workItemStack.Attributes.GetInt("rotation");
                }
                else if (workItemStack.Collectible is CANItemGemCuttingWorkItem wi && wi.isBlisterSteel) return false;

                if (SelectedRecipeId < 0)
                {
                    var list = matchingRecipes ?? gemBehavior.GetMatchingRecipes(stack);
                    //canjewelry.gemCuttingRecipes
                    if (list.Count == 1)
                    {
                        SelectedRecipeId = list[0].RecipeId;
                        //SelectedRecipe = list[0];
                    }
                    else if(list.Count > 1) 
                    {
                        if (world.Side == EnumAppSide.Client)
                        {
                            OpenDialog(stack);
                        }
                    }
                    else
                    {
                        workItemStack = null;
                        Voxels = new byte[16, 14, 16];
                        return false;
                    }
                }

                returnOnCancelStack = slot.TakeOut(1);
                slot.MarkDirty();

                // A gem put on unappraised gets its weight and traits here, unseen. Server only:
                // the client gets the work item with the next sync. The gem returned on cancel
                // carries the same truth, so taking it back off does not reroll it.
                if (Api.Side == EnumAppSide.Server && newWorkItemStack == workItemStack
                    && stack.Collectible is not CANItemGemCuttingWorkItem)
                {
                    CANGemAppraisalCB.ApplyTable(byPlayer, workItemStack);
                    CANGemAppraisal.CopyRoughKeys(workItemStack.Attributes, returnOnCancelStack.Attributes);
                }

                if (Api.Side == EnumAppSide.Server)
                {
                    // Let the server decide the shape, then send the stuff to client, and then show the correct voxels
                    // instead of the voxels flicker thing when both sides do it (due to voxel placement randomness in iron bloom and blister steel)
                    RegenMeshAndSelectionBoxes();
                }

                CheckIfFinished(byPlayer);
                MarkDirty();
                return true;
            }


            return false;
        }

        internal void OnBeginUse(IPlayer byPlayer, BlockSelection blockSel)
        {
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            if (((byItemStack != null) ? byItemStack.Attributes : null) != null)
            {
                this.stoneType = byItemStack.Attributes.GetString("stone", "granite");
                this.metalType = byItemStack.Attributes.GetString("metal", "copper");
            }
            base.OnBlockPlaced(byItemStack);
            if (this.Api.Side == EnumAppSide.Client)
            {
                this.currentMesh = this.getMesh(canjewelry.capi.Tesselator);
            }
        }


        internal void OnUseOver(IPlayer byPlayer, int selectionBoxIndex)
        {
            // box index 0 is the anvil itself
            if (selectionBoxIndex <= 0 || selectionBoxIndex >= selectionBoxes.Length) return;

            Cuboidf box = selectionBoxes[selectionBoxIndex];

            Vec3i voxelPos = new Vec3i((int)(16 * box.X1), (int)(16 * box.Y1) - 2, (int)(16 * box.Z1));

            OnUseOver(byPlayer, voxelPos, new BlockSelection() { Position = Pos, SelectionBoxIndex = selectionBoxIndex });
        }


        /// <summary>How far from the table a strike is still accepted, in blocks.</summary>
        private const double ChiselReach = 8;

        /// <summary>
        /// Whether this player may take a strike at the work item: a gem chisel in hand, a hammer in
        /// the off hand (creative excepted, as on the client), and close enough to the table to be
        /// touching it.
        /// </summary>
        private bool MayChisel(IPlayer byPlayer, ItemSlot slot)
        {
            if (slot?.Itemstack?.Item is not CANItemGemChisel) return false;

            EntityPlayer entity = byPlayer?.Entity;
            if (entity == null) return false;

            bool creative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (!creative && entity.LeftHandItemSlot?.Itemstack?.Collectible?.Tool != EnumTool.Hammer)
            {
                return false;
            }

            return entity.Pos.DistanceTo(Pos.ToVec3d().Add(0.5, 0.5, 0.5)) <= ChiselReach;
        }

        internal void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockSelection blockSel)
        {
            if (voxelPos == null)
            {
                return;
            }

            if (SelectedRecipe == null)
            {
                ditchWorkItemStack();
                return;
            }

            // voxelPos comes straight off a client packet, so it is not necessarily inside the
            // grid. An out of range one would take the server down on the Voxels lookup below.
            if (!isInsideGrid(voxelPos))
            {
                return;
            }

            // Send a custom network packet for server side, because
            // serverside blockselection index is inaccurate
            if (Api.Side == EnumAppSide.Client)
            {
                SendUseOverPacket(byPlayer, voxelPos);
            }


            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (slot.Itemstack == null || !CanWorkCurrent)
            {
                return;
            }

            // What the chisel checks before it ever sends the packet (CANItemGemChisel.
            // OnHeldAttackStart), checked again here because the packet does not have to come from
            // the chisel: without it any held item cut gems, from any distance, as fast as a client
            // cared to send.
            if (!MayChisel(byPlayer, slot))
            {
                return;
            }

            int toolMode = slot.Itemstack.Collectible.GetToolMode(slot, byPlayer, blockSel);

            float yaw = GameMath.Mod(byPlayer.Entity.Pos.Yaw, 2 * GameMath.PI);

            // Every accessibility setting below is gated on this, so a server can hand them to
            // named players only. The whole config reaches clients, so both sides answer alike.
            bool easyMode = canjewelry.config?.IsEasyCuttingEnabledFor(byPlayer?.PlayerName) == true;

            if (easyMode && canjewelry.config.cuttingInstantComplete)
            {
                fillVoxelsFromRecipe();
                playChiselSound(byPlayer);

                RegenMeshAndSelectionBoxes();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                Api.World.BlockAccessor.MarkBlockEntityDirty(Pos);
                slot.Itemstack.Collectible.DamageItem(Api.World, byPlayer.Entity, slot);

                CheckIfFinished(byPlayer);
                MarkDirty();
                return;
            }

            // Extra strikes are for the 1x1 mode only. The line modes already clear a whole row or
            // layer per click, so repeating them would just wipe the work item.
            int strikes = easyMode && toolMode == 0
                ? GameMath.Clamp(canjewelry.config.cuttingVoxelsPerClick, 1, 8)
                : 1;
            bool damagePerVoxel = !easyMode || canjewelry.config.cuttingDurabilityPerVoxel;
            bool spareRecipeVoxels = easyMode && canjewelry.config.cuttingSpareRecipeVoxels;

            bool struckAny = false;
            Vec3i target = voxelPos;

            for (int strike = 0; strike < strikes; strike++)
            {
                // The player aims the first strike; every one after it picks its own target, so
                // that a wider click does not eat voxels the recipe still needs.
                if (strike > 0)
                {
                    target = findNextVoxelToRemove();
                    if (target == null) break;
                }

                EnumVoxelMaterial voxelMat = (EnumVoxelMaterial)Voxels[target.X, target.Y, target.Z];
                if (voxelMat == EnumVoxelMaterial.Empty) break;

                spawnParticles(target, voxelMat, byPlayer);
                switch (toolMode)
                {
                    case 0:
                        OnSplit(target);
                        break;
                    case 1:
                        OnCleanHorizontal(target, BlockFacing.NORTH.FaceWhenRotatedBy(0, yaw - GameMath.PIHALF, 0), spareRecipeVoxels);
                        break;
                    case 2:
                        OnCleanVertical(target, BlockFacing.EAST.FaceWhenRotatedBy(0, yaw - GameMath.PIHALF, 0), spareRecipeVoxels);
                        break;
                }

                // Before the checks below, either of which can bail out of the method - the strike
                // landed, so it should be heard either way.
                if (!struckAny) playChiselSound(byPlayer);
                struckAny = true;

                if (damagePerVoxel || strike == 0)
                {
                    slot.Itemstack.Collectible.DamageItem(Api.World, byPlayer.Entity, slot);
                    // The chisel can break mid-click, which empties the slot we just read the
                    // tool mode from.
                    if (slot.Itemstack == null) break;
                }

                if (!HasAnyMetalVoxel())
                {
                    clearWorkSpace();
                    return;
                }
            }

            if (struckAny)
            {
                RegenMeshAndSelectionBoxes();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                Api.World.BlockAccessor.MarkBlockEntityDirty(Pos);
            }

            CheckIfFinished(byPlayer);
            MarkDirty();
        }

        private static bool isInsideGrid(Vec3i voxelPos)
        {
            return voxelPos.X >= 0 && voxelPos.X < 16
                && voxelPos.Y >= 0 && voxelPos.Y < 14
                && voxelPos.Z >= 0 && voxelPos.Z < 16;
        }

        private void playChiselSound(IPlayer byPlayer)
        {
            Api.World.PlaySoundAt(
                new AssetLocation("sounds/player/knap" + (Api.World.Rand.Next(2) > 0 ? 1 : 2)),
                Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
                byPlayer, true, 12f, 1f
            );
        }

        /// <summary>
        /// The next voxel the recipe has no use for, scanned in a fixed x/y/z order so client and
        /// server land on the same one and their grids stay identical. Only looks at the layers
        /// <see cref="MatchesRecipe"/> actually compares - anything above them never blocks the
        /// recipe from completing, so knocking it off would be wasted durability.
        /// </summary>
        private Vec3i findNextVoxelToRemove()
        {
            bool[,,] recipe = recipeVoxels;
            if (recipe == null) return null;

            int ymax = Math.Min(14, SelectedRecipe.QuantityLayers);

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < ymax; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (Voxels[x, y, z] == (byte)EnumVoxelMaterial.Empty) continue;
                        if (!recipeNeedsVoxel(recipe, x, y, z)) return new Vec3i(x, y, z);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Puts the grid into exactly the state <see cref="MatchesRecipe"/> asks for. Layers above
        /// the recipe's own height are cleared as well: MatchesRecipe ignores them, so leaving them
        /// would float leftovers over a piece that already counts as finished.
        /// </summary>
        private void fillVoxelsFromRecipe()
        {
            bool[,,] recipe = recipeVoxels;
            if (recipe == null) return;

            int ymax = Math.Min(14, SelectedRecipe.QuantityLayers);
            byte[,,] filled = new byte[16, 14, 16];

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < ymax; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        filled[x, y, z] = (byte)(recipeNeedsVoxel(recipe, x, y, z)
                            ? EnumVoxelMaterial.Metal
                            : EnumVoxelMaterial.Empty);
                    }
                }
            }

            Voxels = filled;
        }

        private void spawnParticles(Vec3i voxelPos, EnumVoxelMaterial voxelMat, IPlayer byPlayer)
        {
            Random rnd = Api.World.Rand;
            Vec3d spawnPos = Pos.ToVec3d().AddCopy(
                voxelPos.X / 16f + 0.03f,
                voxYOff + voxelPos.Y / 16f + 0.07f,
                voxelPos.Z / 16f + 0.03f
            );

            int color = ColorUtil.ToRgba(255, 170, 170, 170);
            if (workItemStack != null)
                color = workItemStack.Collectible.GetRandomColor(Api as ICoreClientAPI, workItemStack);

            for (int i = 0; i < 4; i++)
            {
                Api.World.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1f,
                    AddQuantity = 2f,
                    Color = color,
                    MinPos = spawnPos.Clone(),
                    AddPos = new Vec3d(0.0625, 0.01, 0.0625),
                    MinVelocity = new Vec3f(0f, 0.5f, 0f),
                    AddVelocity = new Vec3f(
                        5f * (float)(rnd.NextDouble() - 0.5f),
                        2.5f * (float)rnd.NextDouble(),
                        5f * (float)(rnd.NextDouble() - 0.5f)
                    ),
                    LifeLength = 0.3f + 0.2f * (float)rnd.NextDouble(),
                    GravityEffect = 1f,
                    MinSize = 0.04f,
                    MaxSize = 0.25f,
                    ParticleModel = EnumParticleModel.Cube,
                    SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.1f)
                }, byPlayer);
            }
        }


        internal string PrintDebugText()
        {
            GemCuttingRecipe recipe = SelectedRecipe;


            //EnumHelveWorkableMode? mode = (workItemStack?.Collectible as IGemCuttingWorkable)?.GetHelveWorkableMode(workItemStack, this);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Workitem: " + workItemStack);
            sb.AppendLine("Recipe: " + recipe?.Name);
            sb.AppendLine("Matches recipe: " + MatchesRecipe());
            //sb.AppendLine("Helve Workable: " + mode);

            return sb.ToString();
        }

        public virtual void OnHelveHammerHit()
        {
            if (workItemStack == null || !CanWorkCurrent) return;

            GemCuttingRecipe recipe = SelectedRecipe;
            if (recipe == null)
            {
                return;
            }

            //var mode = (workItemStack.Collectible as IGemCuttingWorkable).GetHelveWorkableMode(workItemStack, this);
            //if (mode == EnumHelveWorkableMode.NotWorkable) return;

            rotation = 0;
            int ymax = recipe.QuantityLayers;
            Vec3i usableMetalVoxel;
            /*if (mode == EnumHelveWorkableMode.TestSufficientVoxelsWorkable)
            {
                usableMetalVoxel = findFreeMetalVoxel();

                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        for (int y = 0; y < 6; y++)
                        {
                            bool requireMetalHere = y >= ymax ? false : recipe.Voxels[x, y, z];

                            EnumVoxelMaterial mat = (EnumVoxelMaterial)Voxels[x, y, z];

                            if (mat == EnumVoxelMaterial.Slag)
                            {
                                Voxels[x, y, z] = (byte)EnumVoxelMaterial.Empty;
                                onHelveHitSuccess(mat, null, x, y, z);
                                return;
                            }

                            if (requireMetalHere && usableMetalVoxel != null && mat == EnumVoxelMaterial.Empty)
                            {
                                Voxels[x, y, z] = (byte)EnumVoxelMaterial.Metal;
                                Voxels[usableMetalVoxel.X, usableMetalVoxel.Y, usableMetalVoxel.Z] = (byte)EnumVoxelMaterial.Empty;

                                onHelveHitSuccess(mat, usableMetalVoxel, x, y, z);
                                return;
                            }
                        }
                    }
                }

                if (usableMetalVoxel != null)
                {
                    Voxels[usableMetalVoxel.X, usableMetalVoxel.Y, usableMetalVoxel.Z] = (byte)EnumVoxelMaterial.Empty;
                    onHelveHitSuccess(EnumVoxelMaterial.Metal, null, usableMetalVoxel.X, usableMetalVoxel.Y, usableMetalVoxel.Z);
                    return;
                }
            }*/
            //else
            {

                for (int y = 13; y >= 0; y--)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        for (int x = 0; x < 16; x++)
                        {
                            bool requireMetalHere = y >= ymax ? false : recipe.Voxels[x, y, z];

                            EnumVoxelMaterial mat = (EnumVoxelMaterial)Voxels[x, y, z];

                            if (requireMetalHere && mat == EnumVoxelMaterial.Metal) continue;
                            if (!requireMetalHere && mat == EnumVoxelMaterial.Empty) continue;

                            if (requireMetalHere && mat == EnumVoxelMaterial.Empty)
                            {
                                Voxels[x, y, z] = (byte)EnumVoxelMaterial.Metal;
                            }
                            else
                            {
                                Voxels[x, y, z] = (byte)EnumVoxelMaterial.Empty;
                            }

                            onHelveHitSuccess(mat == EnumVoxelMaterial.Empty ? EnumVoxelMaterial.Metal : mat, null, x, y, z);

                            return;
                        }
                    }
                }
            }
        }

        void onHelveHitSuccess(EnumVoxelMaterial mat, Vec3i usableMetalVoxel, int x, int y, int z)
        {
            if (Api.World.Side == EnumAppSide.Client)
            {
                //spawnParticles(new Vec3i(x, y, z), mat == EnumVoxelMaterial.Empty ? EnumVoxelMaterial.Metal : mat, null);
                if (usableMetalVoxel != null) spawnParticles(usableMetalVoxel, EnumVoxelMaterial.Metal, null);
            }

            RegenMeshAndSelectionBoxes();
            CheckIfFinished(null);
        }

        private Vec3i findFreeMetalVoxel()
        {
            GemCuttingRecipe recipe = SelectedRecipe;

            int ymax = recipe.QuantityLayers;

            for (int y = 13; y >= 0; y--)
            {
                for (int z = 0; z < 14; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        bool requireMetalHere = y >= ymax ? false : recipe.Voxels[x, y, z];
                        EnumVoxelMaterial mat = (EnumVoxelMaterial)Voxels[x, y, z];

                        if (!requireMetalHere && mat == EnumVoxelMaterial.Metal) return new Vec3i(x, y, z);
                    }
                }
            }

            return null;
        }       
        public virtual void CheckIfFinished(IPlayer byPlayer)
        {
            if (SelectedRecipe == null) return;

            if (MatchesRecipe() && Api.World is IServerWorldAccessor)
            {
                FinishCut(byPlayer, 0);
            }
        }

        /// <summary>
        /// Hands out the cut gem and clears the table. The one way a cut ends: the hammer and the
        /// last chisel strike used to carry two copies of this that had drifted apart.
        /// </summary>
        /// <param name="mistakes">Voxels off the recipe; each one lowers the buff values.</param>
        protected void FinishCut(IPlayer byPlayer, int mistakes)
        {
            Voxels = new byte[16, 14, 16];
            ItemStack outstack = SelectedRecipe.Output.ResolvedItemstack.Clone();
            // A work item from before carats gets its truth only now.
            CANGemAppraisalCB.ApplyTable(byPlayer, workItemStack);
            float roughCarats = CANGemAppraisal.GetCarats(workItemStack.Attributes);
            float mistakeMult = mistakes > 0
                ? Math.Max(0, 1 - (float)(canjewelry.config.minFineForMistake + Config.rand.NextDouble() * (canjewelry.config.maxFineForMistake - canjewelry.config.minFineForMistake)) * mistakes)
                : 1f;
            var handOut = new List<ItemStack>();
            string cleave = CleavePresetOf(SelectedRecipe);
            if (cleave != null)
            {
                // A cleave gives rough pieces; if it cannot part the gem, the gem comes back whole.
                handOut = CleavePieces(byPlayer, workItemStack, cleave, mistakeMult)
                    ?? new List<ItemStack> { returnOnCancelStack ?? CANGemCuttableCB.RoughGemOfWorkItem(Api.World, workItemStack) };
            }
            else
            {
                // By carats a mistake costs weight, and the weight decides the buff; by tiers it costs buff.
                bool byCarats = canjewelry.config.GemCarats.ScalesByCarats;
                outstack = SetCutWeight(byPlayer, outstack, workItemStack, byCarats ? mistakeMult : 1f, out float cutCarats);
                EncrustableCB.ApplyCuttingBuff(outstack);
                if (!byCarats && mistakes > 0)
                {
                    EncrustableCB.ReduceBuffValueBecauseOfMistakes(outstack, mistakeMult);
                }
                EncrustableCB.FireCutCompletionEvents(byPlayer, workItemStack, outstack, roughCarats, cutCarats);
                handOut.Add(outstack);
            }
            workItemStack = null;
            returnOnCancelStack = null;

            SelectedRecipeId = -1;

            foreach (ItemStack stack in handOut)
            {
                if (byPlayer?.InventoryManager.TryGiveItemstack(stack) == true)
                {
                    Api.World.PlaySoundFor(new AssetLocation("game:sounds/player/collect"), byPlayer, false, 24);
                }
                else
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.626, 0.5));
                }
            }

            RegenMeshAndSelectionBoxes();
            MarkDirty(true);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
            rotation = 0;
        }

        /// <summary>
        /// While a gem is being worked: how many voxels the pattern needs are already chiselled off
        /// and what that costs, and - for a gem the loupe has seen - the weight the cut is heading
        /// for. Only from what the player knows: the range shown, the flaws found.
        /// </summary>
        private void AppendCutForecast(StringBuilder dsc)
        {
            GemCuttingRecipe recipe = SelectedRecipe;
            var cfg = canjewelry.config?.GemCarats;
            if (workItemStack == null || recipe == null || cfg == null) return;

            bool[,,] needed = recipeVoxels;
            int ymax = Math.Min(14, recipe.QuantityLayers);
            int mistakes = 0;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < ymax; y++)
                    for (int z = 0; z < 16; z++)
                        if (needed[x, y, z] && Voxels[x, y, z] == (byte)EnumVoxelMaterial.Empty) mistakes++;

            float fine = (canjewelry.config.minFineForMistake + canjewelry.config.maxFineForMistake) / 2;
            float kept = Math.Max(0f, 1f - fine * mistakes);
            if (mistakes > 0)
            {
                dsc.AppendLine(Lang.Get("canjewelry:cut-forecast-mistakes", mistakes, (int)Math.Round((1 - kept) * 100)));
            }

            AppendRecipeForecast(dsc, recipe, workItemStack.Attributes, workItemStack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET), kept);
        }

        /// <summary>
        /// Weight, known flaws and buffs a cut recipe gives this gem, from what the loupe showed.
        /// Nothing for a cleave or a gem not appraised yet.
        /// </summary>
        private static void AppendRecipeForecast(StringBuilder dsc, GemCuttingRecipe recipe, ITreeAttribute work, string gemType, float kept)
        {
            var cfg = canjewelry.config?.GemCarats;
            float[] shown = CANGemAppraisal.GetAppraisal(work) >= CANGemAppraisal.Loupe ? CANGemAppraisal.GetCaratsRange(work) : null;
            if (cfg == null || shown == null || CleavePresetOf(recipe) != null)
            {
                return;
            }

            string cuttingType = recipe.Output.ResolvedItemstack?.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.GetString(CANJWConstants.CUTTING_TYPE);
            float[] cut = CANGemForecast.CutWeight(cfg, shown, cuttingType, gemType, work, kept);
            dsc.AppendLine(Lang.Get("canjewelry:cut-forecast-weight", CANGemAppraisalCB.FormatCaratsRange(cut[0], cut[1])));

            float flawLoss = CANGemForecast.KnownFlawLoss(cfg, work);
            if (flawLoss > 0.001f)
            {
                dsc.AppendLine(Lang.Get("canjewelry:cut-forecast-flaws", (int)Math.Round(flawLoss * 100)));
            }

            // The main stats this cut can give before polishing.
            float[] grinding = canjewelry.config.CuttingAttributesDict.TryGetValue(cuttingType ?? "", out var cutting)
                ? cutting?.GrindingBuffIncreaseMultipliers : null;
            if (canjewelry.config.TurnOffBuffs || grinding == null || grinding.Length == 0 || gemType == null
                || !canjewelry.config.PossibleGemBuffs.TryGetValue(gemType, out var possibleBuffs))
            {
                return;
            }
            float mult = grinding[0] * CANGemForecast.KnownBuffMult(cfg, work, gemType);
            foreach (string buffName in possibleBuffs)
            {
                canjewelry.config.BuffAttributesDict.TryGetValue(buffName, out var attrs);
                float[] buff = CANGemForecast.MainBuff(cfg, attrs, cut, mult);
                if (buff != null) dsc.Append(CANBuffText.Name(buffName)).Append(' ').AppendLine(CANBuffText.Range(buffName, buff));
            }
        }

        /// <summary>
        /// Writes the cut gem's weight and traits into its tree. By carats the weight picks the
        /// gem's size, which may swap the item for another size; by tiers, or with KeepRecipeBucket,
        /// the recipe's size stays and the weight is moved into its range. Returns the gem to hand out.
        /// </summary>
        private ItemStack SetCutWeight(IPlayer byPlayer, ItemStack outstack, ItemStack workItem, float mistakeMult, out float carats)
        {
            var cfg = canjewelry.config.GemCarats;
            ITreeAttribute cutGemTree = outstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            if (cutGemTree == null)
            {
                cutGemTree = new TreeAttribute();
                outstack.Attributes[CANJWConstants.CUT_GEM_TREE] = cutGemTree;
            }

            string gemType = CANGemSize.GemTypeOf(outstack.Collectible);
            string[] traits = CANGemAppraisal.GetTraits(workItem.Attributes);
            string cuttingType = cutGemTree.GetString(CANJWConstants.CUTTING_TYPE);
            float rough = CANGemAppraisal.GetCarats(workItem.Attributes);

            // The share the cut keeps, open to a subscriber; the traits apply on top of it.
            var ev = new integration.CutYieldEvent
            {
                Player = byPlayer, WorkItem = workItem, CuttingType = cuttingType, RoughCarats = rough,
                Yield = CANGemAppraisal.CutYield(cfg, cuttingType, gemType)
            };
            canjewelry.Instance?.FireCutYield(ev);
            carats = Math.Max(cfg.Step, CANGemAppraisal.RoundCarats(
                rough * Math.Clamp(ev.Yield, 0f, 1f) * CANGemAppraisal.TraitYieldMult(cfg, traits), cfg.Step));
            int size = outstack.Collectible.Attributes?["canGemType"].AsInt(1) ?? 1;

            if (cfg.ScalesByCarats && !cfg.KeepRecipeBucket)
            {
                carats = Math.Max(cfg.Step, CANGemAppraisal.RoundCarats(carats * mistakeMult, cfg.Step));
                carats = Math.Min(carats, cfg.MaxCutCarats);
                int bucket = CANGemAppraisal.BucketForCarats(cfg, carats);
                Item resized = bucket == size ? null : Api.World.GetItem(CANGemSize.CutGemCode(CANGemSize.CutQualityClamped(bucket), gemType));
                if (resized != null)
                {
                    var swapped = new ItemStack(resized);
                    swapped.Attributes = outstack.Attributes.Clone();
                    outstack = swapped;
                    cutGemTree = outstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
                }
            }
            else
            {
                // The largest size has no upper bound, so the cap keeps it within a socket.
                carats = Math.Min(CANGemAppraisal.ClampToBucket(cfg, CANGemAppraisal.RoundCarats(carats * mistakeMult, cfg.Step), size),
                    Math.Max(cfg.MaxCutCarats, cfg.Step));
            }

            CANGemAppraisal.SetWeight(cutGemTree, carats, traits);
            // A gem never inspected gets its pieces rolled now; where they lie no longer matters.
            string[] pieces = CANGemDefects.Ensure(workItem.Attributes, cfg, Config.rand).Select(d => d.Trait).ToArray();
            cutGemTree[CANJWConstants.FLAW_PIECES] = new StringArrayAttribute(pieces);
            return outstack;
        }

        public void ditchWorkItemStack(IPlayer byPlayer = null)
        {
            if (workItemStack == null) return;

            ItemStack ditchedStack = StackToGiveBack();
            if (byPlayer == null || !byPlayer.InventoryManager.TryGiveItemstack(ditchedStack))
            {
                Api.World.SpawnItemEntity(ditchedStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            clearWorkSpace();
        }

        // Before a recipe is chosen the rough gem put on the table comes back; after, the work
        // item with its progress.
        private ItemStack StackToGiveBack()
        {
            ItemStack ditchedStack;
            if (SelectedRecipe == null)
            {
                ditchedStack = returnOnCancelStack ?? workItemStack.Collectible.GetBehavior<CANGemCuttableCB>().GetBaseMaterial(workItemStack);
                //float temp = workItemStack.Collectible.GetTemperature(Api.World, workItemStack);
                //ditchedStack.Collectible.SetTemperature(Api.World, ditchedStack, temp);
            }
            else
            {

                workItemStack.Attributes.SetBytes("voxels", serializeVoxels(Voxels));
                workItemStack.Attributes.SetInt("selectedRecipeId", SelectedRecipeId);
                workItemStack.Attributes.SetInt("rotation", rotation);

                if (workItemStack.Collectible is ItemIronBloom bloomItem)
                {
                    workItemStack.Attributes.SetInt("hashCode", bloomItem.GetWorkItemHashCode(workItemStack));
                }

                ditchedStack = workItemStack;
            }
            return ditchedStack;
        }

        protected void clearWorkSpace()
        {
            workItemStack = null;
            returnOnCancelStack = null;
            Voxels = new byte[16, 14, 16];
            RegenMeshAndSelectionBoxes();
            MarkDirty();
            rotation = 0;
            SelectedRecipeId = -1;
        }

        private bool MatchesRecipe()
        {
            if (SelectedRecipe == null) return false;

            int ymax = Math.Min(14, SelectedRecipe.QuantityLayers);

            bool[,,] recipeVoxels = this.recipeVoxels; // Otherwise we cause lag spikes

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < ymax; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        byte desiredMat = (byte)(recipeVoxels[x, y, z]
                                                ? EnumVoxelMaterial.Metal 
                                                : EnumVoxelMaterial.Empty);

                        if (Voxels[x, y, z] != desiredMat)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
        private bool MatchesRecipeWithPossibleMistake(out int mistakesCount)
        {
            mistakesCount = 0;
            if (SelectedRecipe == null) return false;

            int ymax = Math.Min(14, SelectedRecipe.QuantityLayers);

            bool[,,] recipeVoxels = this.recipeVoxels; // Otherwise we cause lag spikes

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < ymax; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        byte desiredMat = (byte)(recipeVoxels[x, y, z]
                                                ? EnumVoxelMaterial.Metal
                                                : EnumVoxelMaterial.Empty);

                        if (Voxels[x, y, z] != desiredMat)
                        {
                            if(desiredMat == 1 && Voxels[x, y, z] == 0)
                            {
                                mistakesCount++;
                                continue;
                            }
                            return false;
                        }
                    }
                }
            }

            return true;
        }
        bool HasAnyMetalVoxel()
        {
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 14; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (Voxels[x, y, z] == (byte)EnumVoxelMaterial.Metal) return true;
                    }
                }
            }

            return false;
        }
        public virtual void OnSplit(Vec3i voxelPos)
        {
            bool[,,] recipe = recipeVoxels;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int x = voxelPos.X + dx;
                        int y = voxelPos.Y + dy;
                        int z = voxelPos.Z + dz;

                        if (x < 0 || y < 0 || z < 0 || x >= 16 || y >= 14 || z >= 16) continue;
                        if (Voxels[x, y, z] == (byte)EnumVoxelMaterial.Empty) continue;

                        bool recipeNeedsThis = recipe != null
                            && x < recipe.GetLength(0)
                            && y < recipe.GetLength(1)
                            && z < recipe.GetLength(2)
                            && recipe[x, y, z];

                        if (!recipeNeedsThis)
                            Voxels[x, y, z] = 0;
                    }
                }
            }
        }
        public virtual void OnCleanHorizontal(Vec3i voxelPos, BlockFacing facing, bool spareRecipeVoxels = false)
        {
            bool[,,] recipe = spareRecipeVoxels ? recipeVoxels : null;

            for(int i = 0; i < 16; i++)
            {
                for(int j = 0; j < 16; j++)
                {
                    if (recipeNeedsVoxel(recipe, i, voxelPos.Y, j)) continue;
                    Voxels[i, voxelPos.Y, j] = 0;
                }
            }
        }
        public virtual void OnCleanVertical(Vec3i voxelPos, BlockFacing facing, bool spareRecipeVoxels = false)
        {
            bool[,,] recipe = spareRecipeVoxels ? recipeVoxels : null;

            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    if (facing == BlockFacing.NORTH || facing == BlockFacing.SOUTH)
                    {
                        if (recipeNeedsVoxel(recipe, voxelPos.X, i, j)) continue;
                        Voxels[voxelPos.X, i, j] = 0;
                    }
                    else
                    {
                        if (recipeNeedsVoxel(recipe, j, i, voxelPos.Z)) continue;
                        Voxels[j, i, voxelPos.Z] = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Whether the recipe wants a voxel at this spot. A null recipe means "spare nothing", so
        /// the line modes keep clearing everything unless the setting asks otherwise.
        /// </summary>
        private static bool recipeNeedsVoxel(bool[,,] recipe, int x, int y, int z)
        {
            return recipe != null
                && x < recipe.GetLength(0)
                && y < recipe.GetLength(1)
                && z < recipe.GetLength(2)
                && recipe[x, y, z];
        }

        public virtual void OnUpset(Vec3i voxelPos, BlockFacing towardsFace)
        {
            // Can only move metal
            if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] != (byte)EnumVoxelMaterial.Metal) return;
            // Can't move if metal is above
            if (voxelPos.Y < 5 && Voxels[voxelPos.X, voxelPos.Y + 1, voxelPos.Z] != (byte)EnumVoxelMaterial.Empty) return;

            Vec3i npos = voxelPos.Clone().Add(towardsFace);
            Vec3i opFaceDir = towardsFace.Opposite.Normali;

            if (npos.X < 0 || npos.X >= 16 || npos.Y < 0 || npos.Y >= 6 || npos.Z < 0 || npos.Z >= 16) return;

            if (voxelPos.Y > 0)
            {
                if (Voxels[npos.X, npos.Y, npos.Z] == (byte)EnumVoxelMaterial.Empty && Voxels[npos.X, npos.Y - 1, npos.Z] != (byte)EnumVoxelMaterial.Empty)
                {
                    if (npos.X < 0 || npos.X >= 16 || npos.Y < 0 || npos.Y >= 6 || npos.Z < 0 || npos.Z >= 16) return;

                    Voxels[npos.X, npos.Y, npos.Z] = (byte)EnumVoxelMaterial.Metal;
                    Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] = 0;
                    return;
                }
                else
                {
                    npos.Y++;

                    if (voxelPos.X + opFaceDir.X < 0 || voxelPos.X + opFaceDir.X >= 16 || voxelPos.Z + opFaceDir.Z < 0 || voxelPos.Z + opFaceDir.Z >= 16) return;

                    if (npos.Y < 6 && Voxels[npos.X, npos.Y, npos.Z] == (byte)EnumVoxelMaterial.Empty && Voxels[npos.X, npos.Y - 1, npos.Z] != (byte)EnumVoxelMaterial.Empty && Voxels[voxelPos.X + opFaceDir.X, voxelPos.Y, voxelPos.Z + opFaceDir.Z] == (byte)EnumVoxelMaterial.Empty)
                    {
                        Voxels[npos.X, npos.Y, npos.Z] = (byte)EnumVoxelMaterial.Metal;
                        Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] = (byte)EnumVoxelMaterial.Empty;
                        return;
                    }

                    if (!moveVoxelDownwards(voxelPos.Clone(), towardsFace, 1))
                    {
                        moveVoxelDownwards(voxelPos.Clone(), towardsFace, 2);
                    }
                }
                return;
            }


            npos.Y++;

            if (npos.X < 0 || npos.X >= 16 || npos.Y < 0 || npos.Y >= 6 || npos.Z < 0 || npos.Z >= 16) return;
            if (voxelPos.X + opFaceDir.X < 0 || voxelPos.X + opFaceDir.X >= 16 || voxelPos.Z + opFaceDir.Z < 0 || voxelPos.Z + opFaceDir.Z >= 16) return;

            if (npos.Y < 6 && Voxels[npos.X, npos.Y, npos.Z] == (byte)EnumVoxelMaterial.Empty && Voxels[npos.X, npos.Y - 1, npos.Z] != (byte)EnumVoxelMaterial.Empty && Voxels[voxelPos.X + opFaceDir.X, voxelPos.Y, voxelPos.Z + opFaceDir.Z] == (byte)EnumVoxelMaterial.Empty)
            {
                Voxels[npos.X, npos.Y, npos.Z] = (byte)EnumVoxelMaterial.Metal;
                Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] = (byte)EnumVoxelMaterial.Empty;
                return;
            }

        }

        private Vec3i getClosestBfs(Vec3i voxelPos, BlockFacing towardsFace, int maxDist)
        {
            Queue<Vec3i> nodesToVisit = new Queue<Vec3i>();
            HashSet<Vec3i> nodesVisited = new HashSet<Vec3i>();

            nodesToVisit.Enqueue(voxelPos);

            while (nodesToVisit.Count > 0)
            {
                Vec3i node = nodesToVisit.Dequeue();

                for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
                {
                    BlockFacing face = BlockFacing.HORIZONTALS[i];
                    Vec3i nnode = node.Clone().Add(face);

                    if (nnode.X < 0 || nnode.X >= 16 || nnode.Y < 0 || nnode.Y >= 6 || nnode.Z < 0 || nnode.Z >= 16) continue;
                    if (nodesVisited.Contains(nnode)) continue;
                    nodesVisited.Add(nnode);

                    double x = nnode.X - voxelPos.X;
                    double z = nnode.Z - voxelPos.Z;
                    double len = GameMath.Sqrt(x * x + z * z);

                    if (len > maxDist) continue;

                    x /= len;
                    z /= len;

                    if (towardsFace == null || Math.Abs((float)Math.Acos(towardsFace.Normalf.X * x + towardsFace.Normalf.Z * z)) < 25 * GameMath.DEG2RAD)
                    {
                        if (Voxels[nnode.X, nnode.Y, nnode.Z] == (byte)EnumVoxelMaterial.Empty)
                        {
                            return nnode;
                        }
                    }

                    if (Voxels[nnode.X, nnode.Y, nnode.Z] == (byte)EnumVoxelMaterial.Metal)
                    {
                        nodesToVisit.Enqueue(nnode);
                    }
                }
            }

            return null;
        }

        public virtual void OnHit(Vec3i voxelPos)
        {
            if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] != (byte)EnumVoxelMaterial.Metal) return;

            if (voxelPos.Y > 0)
            {
                int voxelsMoved = 0;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (voxelPos.X + dx < 0 || voxelPos.X + dx >= 16 || voxelPos.Z + dz < 0 || voxelPos.Z + dz >= 16) continue;

                        if (Voxels[voxelPos.X + dx, voxelPos.Y, voxelPos.Z + dz] == (byte)EnumVoxelMaterial.Metal)
                        {
                            voxelsMoved += moveVoxelDownwards(voxelPos.Clone().Add(dx, 0, dz), null, 1) ? 1 : 0;
                        }
                    }
                }

                if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumVoxelMaterial.Metal)
                {
                    voxelsMoved += moveVoxelDownwards(voxelPos.Clone(), null, 1) ? 1 : 0;
                }


                if (voxelsMoved == 0)
                {
                    Vec3i emptySpot = null;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            if (voxelPos.X + 2 * dx < 0 || voxelPos.X + 2 * dx >= 16 || voxelPos.Z + 2 * dz < 0 || voxelPos.Z + 2 * dz >= 16) continue;

                            bool spotEmpty = Voxels[voxelPos.X + 2 * dx, voxelPos.Y, voxelPos.Z + 2 * dz] == (byte)EnumVoxelMaterial.Empty;

                            if (Voxels[voxelPos.X + dx, voxelPos.Y, voxelPos.Z + dz] == (byte)EnumVoxelMaterial.Metal && spotEmpty)
                            {
                                Voxels[voxelPos.X + dx, voxelPos.Y, voxelPos.Z + dz] = (byte)EnumVoxelMaterial.Empty;

                                if (Voxels[voxelPos.X + 2 * dx, voxelPos.Y - 1, voxelPos.Z + 2 * dz] == (byte)EnumVoxelMaterial.Empty)
                                {
                                    Voxels[voxelPos.X + 2 * dx, voxelPos.Y - 1, voxelPos.Z + 2 * dz] = (byte)EnumVoxelMaterial.Metal;
                                }
                                else
                                {
                                    Voxels[voxelPos.X + 2 * dx, voxelPos.Y, voxelPos.Z + 2 * dz] = (byte)EnumVoxelMaterial.Metal;
                                }

                            }
                            else
                            {
                                if (spotEmpty) emptySpot = voxelPos.Clone().Add(dx, 0, dz);
                            }
                        }
                    }

                    if (emptySpot != null && Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumVoxelMaterial.Metal)
                    {
                        Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] = (byte)EnumVoxelMaterial.Empty;

                        if (Voxels[emptySpot.X, emptySpot.Y - 1, emptySpot.Z] == (byte)EnumVoxelMaterial.Empty)
                        {
                            Voxels[emptySpot.X, emptySpot.Y - 1, emptySpot.Z] = (byte)EnumVoxelMaterial.Metal;
                        }
                        else
                        {
                            Voxels[emptySpot.X, emptySpot.Y, emptySpot.Z] = (byte)EnumVoxelMaterial.Metal;
                        }


                    }
                }
            }
        }


        bool moveVoxelDownwards(Vec3i voxelPos, BlockFacing towardsFace, int maxDist)
        {
            int origy = voxelPos.Y;

            while (voxelPos.Y > 0)
            {
                voxelPos.Y--;

                Vec3i spos = getClosestBfs(voxelPos, towardsFace, maxDist);
                if (spos == null) continue;

                Voxels[voxelPos.X, origy, voxelPos.Z] = (byte)EnumVoxelMaterial.Empty;

                for (int y = 0; y <= spos.Y; y++)
                {
                    if (Voxels[spos.X, y, spos.Z] == (byte)EnumVoxelMaterial.Empty)
                    {
                        Voxels[spos.X, y, spos.Z] = (byte)EnumVoxelMaterial.Metal;
                        return true;
                    }
                }

                return true;
            }

            return false;
        }

        void RegenMeshAndSelectionBoxes()
        {
            if (workitemRenderer != null)
            {
                workitemRenderer.RegenMesh(workItemStack, Voxels, recipeVoxels);
            }

            List<Cuboidf> boxes = new List<Cuboidf>();
            boxes.Add(null);

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 7; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (Voxels[x, y, z] != (byte)EnumVoxelMaterial.Empty)
                        {
                            float py = y + 2;
                            boxes.Add(new Cuboidf(x / 16f, py / 16f, z / 16f, x / 16f + 1 / 16f, py / 16f + 1 / 16f, z / 16f + 1 / 16f));
                        }
                    }
                }
            }

            selectionBoxes = boxes.ToArray();
        }



        public override void OnBlockRemoved()
        {
            workitemRenderer?.Dispose();
            workitemRenderer = null;
            if (Api is ICoreClientAPI capi) capi.Event.ColorsPresetChanged -= RegenMeshAndSelectionBoxes;
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            // A work item without a recipe would not go back on a table: the gem was lost.
            if (workItemStack != null)
            {
                Api.World.SpawnItemEntity(StackToGiveBack(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Voxels = deserializeVoxels(tree.GetBytes("voxels"));
            workItemStack = tree.GetItemstack("workItemStack");
            SelectedRecipeId = tree.GetInt("selectedRecipeId", -1);
            rotation = tree.GetInt("rotation");
            returnOnCancelStack = tree.GetItemstack("returnOnCancelStack");
            string oldStone = this.stoneType;
            string oldMetal = this.metalType;
            this.stoneType = tree.GetString("stoneType", "granite");
            this.metalType = tree.GetString("metalType", "copper");

            if (Api != null)
            {
                workItemStack?.ResolveBlockOrItem(Api.World);
                returnOnCancelStack?.ResolveBlockOrItem(Api.World);
            }

            RegenMeshAndSelectionBoxes();

            // A client BE can be initialized before its data arrives (placed by another player,
            // landed after falling), and Initialize then built the granite/copper default mesh.
            if (Api is ICoreClientAPI clientApi
                && (currentMesh == null || oldStone != this.stoneType || oldMetal != this.metalType))
            {
                this.currentMesh = this.getMesh(clientApi.Tesselator);
                clientApi.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("voxels", serializeVoxels(Voxels));
            tree.SetItemstack("workItemStack", workItemStack);
            tree.SetInt("selectedRecipeId", SelectedRecipeId);
            tree.SetInt("rotation", rotation);
            tree.SetItemstack("returnOnCancelStack", returnOnCancelStack);
            tree.SetString("stoneType", this.stoneType);
            tree.SetString("metalType", this.metalType);
        }


        static int bitsPerByte = 2;
        static int partsPerByte = 8 / bitsPerByte;
        public static byte[] serializeVoxels(byte[,,] voxels)
        {
            byte[] data = new byte[16 * 14 * 16 / partsPerByte];
            int pos = 0;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 14; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        int bitpos = bitsPerByte * (pos % partsPerByte);
                        data[pos / partsPerByte] |= (byte)((voxels[x, y, z] & 0x3) << bitpos);
                        pos++;
                    }
                }
            }

            return data;
        }
        public static byte[,,] deserializeVoxels(byte[] data)
        {
            byte[,,] voxels = new byte[16, 14, 16];

            if (data == null || data.Length < 16 * 14 * 16 / partsPerByte) return voxels;

            int pos = 0;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 14; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        int bitpos = bitsPerByte * (pos % partsPerByte);
                        voxels[x, y, z] = (byte)((data[pos / partsPerByte] >> bitpos) & 0x3);

                        pos++;
                    }
                }
            }

            return voxels;
        }
        protected void SendUseOverPacket(IPlayer byPlayer, Vec3i voxelPos)
        {
            byte[] data;

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(ms);
                writer.Write(voxelPos.X);
                writer.Write(voxelPos.Y);
                writer.Write(voxelPos.Z);
                data = ms.ToArray();
            }

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(
                Pos,
                (int)EnumAnvilPacket.OnUserOver,
                data
            );
        }
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == (int)EnumAnvilPacket.SelectRecipe)
            {
                int recipeid = SerializerUtil.Deserialize<int>(data);
                GemCuttingRecipe recipe = canjewelry.gemCuttingRecipes.FirstOrDefault(r => r.RecipeId == recipeid);

                if (recipe == null)
                {
                    Api.World.Logger.Error("Client tried to selected smithing recipe with id {0}, but no such recipe exists!");
                    ditchWorkItemStack(player);
                    return;
                }
                var list = (WorkItemStack?.Collectible as CANItemGemCuttingWorkItem)?.GetMatchingRecipes(workItemStack);
                if (list == null || list.FirstOrDefault(r => r.RecipeId == recipeid) == null)
                {
                    Api.World.Logger.Error("Client tried to selected smithing recipe with id {0}, but it is not a valid one for the given work item stack!", recipe.RecipeId);
                    ditchWorkItemStack(player);
                    return;
                }


                SelectedRecipeId = recipe.RecipeId;

                // Tell server to save this chunk to disk again
                MarkDirty();
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();
            }

            if (packetid == (int)EnumAnvilPacket.CancelSelect)
            {
                ditchWorkItemStack(player);
                return;
            }

            if (packetid == (int)EnumAnvilPacket.OnUserOver)
            {
                Vec3i voxelPos;
                using (MemoryStream ms = new MemoryStream(data))
                {
                    BinaryReader reader = new BinaryReader(ms);
                    voxelPos = new Vec3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                }

                OnUseOver(player, voxelPos, new BlockSelection() { Position = Pos });
            }
        }
        internal void OpenDialog(ItemStack ingredient)
        {
            List<GemCuttingRecipe> recipes = ingredient.Collectible.GetBehavior<CANGemCuttableCB>().GetMatchingRecipes(ingredient);

            // A cleave's icon is drawn in the colour of the gem being cleaved.
            string gemType = CANGemSize.RoughGemTypeOf(ingredient);
            List<ItemStack> stacks = recipes
                .Select(r =>
                {
                    ItemStack icon = r.Output.ResolvedItemstack;
                    if (icon?.Collectible is not CANCleaveIcon || gemType == null) return icon;
                    icon = icon.Clone();
                    icon.Attributes.SetString(CANCleaveIcon.GemTypeKey, gemType);
                    return icon;
                })
                .ToList()
            ;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            string[] descriptions = recipes.Select(r => RecipeDescription(r, ingredient, gemType)).ToArray();

            dlg?.Dispose();
            dlg = new gui.GuiDialogGemRecipeSelector(
                stacks.ToArray(),
                descriptions,
                (selectedIndex) => {
                    SelectedRecipeId = recipes[selectedIndex].RecipeId;
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumAnvilPacket.SelectRecipe, SerializerUtil.Serialize(recipes[selectedIndex].RecipeId));
                },
                () => {
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumAnvilPacket.CancelSelect);
                },
                Pos,
                Api as ICoreClientAPI
            );

            dlg.TryOpen();
        }

        // What the selector shows on hover: the forecast for this gem, or a hint to appraise it first.
        private static string RecipeDescription(GemCuttingRecipe recipe, ItemStack ingredient, string gemType)
        {
            if (CleavePresetOf(recipe) != null)
            {
                return Lang.Get("canjewelry:recipe-cleave-desc", (int)Math.Round((canjewelry.config.GemCarats?.Split?.Loss ?? 0f) * 100));
            }
            var sb = new StringBuilder();
            AppendRecipeForecast(sb, recipe, ingredient.Attributes, gemType, 1f);
            return sb.Length > 0 ? sb.ToString().TrimEnd() : Lang.Get("canjewelry:cut-forecast-need-loupe");
        }
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            AppendCutForecast(dsc);
            /*dsc.AppendLine(Lang.Get("Tier {0} anvil", OwnMetalTier));

            if (workItemStack == null || SelectedRecipe == null)
            {
                return;
            }

            float temperature = workItemStack.Collectible.GetTemperature(Api.World, workItemStack);

            dsc.AppendLine(Lang.Get("Output: {0}", SelectedRecipe.Output?.ResolvedItemstack?.GetName()));

            if (temperature < 25)
            {
                dsc.AppendLine(Lang.Get("Temperature: Cold"));
            }
            else
            {
                dsc.AppendLine(Lang.Get("Temperature: {0}°C", (int)temperature));
            }


            if (!CanWorkCurrent)
            {
                dsc.AppendLine(Lang.Get("Too cold to work"));
            }*/
        }
        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            if (workItemStack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve) == false)
            {
                workItemStack = null;
            }
            workItemStack?.Collectible.OnLoadCollectibleMappings(Api.World, new DummySlot(workItemStack), oldBlockIdMapping, oldItemIdMapping, resolveImports);
        }
        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            if (workItemStack != null)
            {
                if (workItemStack.Class == EnumItemClass.Item)
                {
                    itemIdMapping[workItemStack.Id] = workItemStack.Item.Code;
                }
                else
                {
                    blockIdMapping[workItemStack.Id] = workItemStack.Block.Code;
                }
                workItemStack.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(workItemStack), blockIdMapping, itemIdMapping);
            }
        }
        public override void OnBlockUnloaded()
        {
            workitemRenderer?.Dispose();
            dlg?.TryClose();
            dlg?.Dispose();
            if (Api is ICoreClientAPI capi) capi.Event.ColorsPresetChanged -= RegenMeshAndSelectionBoxes;
        }
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if(currentMesh == null)
            {
                return false;
            }
            mesher.AddMeshData(currentMesh.Clone());
            return true;
        }
        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
        {
            MeshAngle = tree.GetFloat("meshAngle");
            MeshAngle -= degreeRotation * GameMath.DEG2RAD;
            tree.SetFloat("meshAngle", MeshAngle);
        }
    }

}
