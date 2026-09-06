using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using canjewelry.src.blocks;
using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.items;
using canjewelry.src.jewelry;
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
    public enum EnumVoxelMaterial
    {
        Empty = 0,
        Metal = 1,
        Slag = 2,
        Placeholder1 = 3,
    }

    public class BlockEntityGemCuttingTable : BlockEntity, IRotatable, ITexPositionSource
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
        public Size2i AtlasSize => this.capi.BlockTextureAtlas.Size;
        public string stoneType = "granite";
        public string metalType = "copper";
        public Dictionary<string, AssetLocation> tmpAssets = new Dictionary<string, AssetLocation>();

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (tmpAssets.TryGetValue(textureCode, out var assetCode))
                {
                    return this.getOrCreateTexPos(assetCode);
                }

                Dictionary<string, CompositeTexture> dictionary;
                dictionary = new Dictionary<string, CompositeTexture>();
                foreach (var it in this.Block.Textures)
                {
                    dictionary.Add(it.Key, it.Value);
                }
                AssetLocation texturePath = (AssetLocation)null;
                CompositeTexture compositeTexture;
                if (dictionary.TryGetValue(textureCode, out compositeTexture))
                    texturePath = compositeTexture.Baked.BakedName;
                if ((object)texturePath == null && dictionary.TryGetValue("all", out compositeTexture))
                    texturePath = compositeTexture.Baked.BakedName;

                return this.getOrCreateTexPos(texturePath);
            }
        }
        private TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            TextureAtlasPosition texPos = this.capi.BlockTextureAtlas[texturePath];
            if (texPos == null)
            {
                IAsset asset = this.capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                
                if (asset != null)
                {
                    BitmapRef bitmap = asset.ToBitmap(this.capi);
                    this.capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out int _, out texPos, () => asset.ToBitmap(this.Api as ICoreClientAPI));
                }
                else
                {
                    this.capi.World.Logger.Warning("For render in block " + this.Block.Code?.ToString() + ", item {0} defined texture {1}, not no such texture found.", "", (object)texturePath);
                }
            }
            return texPos;
        }
        private MeshData getMesh(ITesselatorAPI tesselator)
        {
            Dictionary<string, MeshData> lanternMeshes = ObjectCacheUtil.GetOrCreate<Dictionary<string, MeshData>>(this.Api, "gemCuttingTableBlockMeshes", () => new Dictionary<string, MeshData>());
            MeshData mesh = null;
            BlockGemCuttingTable block = this.Api.World.BlockAccessor.GetBlock(this.Pos) as BlockGemCuttingTable;
            if (block == null)
            {
                return null;
            }
            lanternMeshes.Clear();
            this.tmpAssets["granite"] = new AssetLocation("game:block/stone/polishedrock/" + this.stoneType + ".png");
            this.tmpAssets["iron"] = new AssetLocation("game:block/metal/sheet/" + this.metalType + "1.png");
            if (lanternMeshes.TryGetValue(string.Concat(new string[]
            {
                this.stoneType, this.metalType
            }), out mesh))
            {
                return mesh;
            }

            return lanternMeshes[string.Concat(new string[]
            {
                this.stoneType, this.metalType
            })] = GenMesh(this.Api as ICoreClientAPI, null, tesselator, this);
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape = null, ITesselatorAPI tesselator = null, ITexPositionSource textureSource = null)
        {
            /*if (tesselator == null)
            {
                tesselator = capi.Tesselator;
            }*/
            //curAtlas = capi.BlockTextureAtlas;
            /*if (textureSource != null)
            {
                tmpTextureSource = textureSource;
            }
            else
            {
                tmpTextureSource = tesselator.GetTextureSource(this);
            }*/
            if (shape == null)
            {
                shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json").Clone();
            }

            if (shape == null)
            {
                return null;
            }

            //AtlasSize = capi.BlockTextureAtlas.Size;
            //var f = (BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
            tesselator.TesselateShape("gemcuttingtable", shape, out var modeldata, this);
            return modeldata;
        }
        public BlockEntityGemCuttingTable() : base() { }
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            workItemStack?.ResolveBlockOrItem(api.World);

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
                    Voxels = new byte[16, 14, 16];
                    ItemStack outstack = SelectedRecipe.Output.ResolvedItemstack.Clone();
                    EncrustableCB.ApplyCuttingBuff(outstack);
                    float mistakeValueMult = Math.Max(0, 1 - (float)(canjewelry.config.minFineForMistake + Config.rand.NextDouble() * (canjewelry.config.maxFineForMistake - canjewelry.config.minFineForMistake)) * mistakes);

                    EncrustableCB.ReduceBuffValueBecauseOfMistakes(outstack, mistakeValueMult);
                    //ApplyCuttingBuff(outstack);
                    //outstack.Collectible.SetTemperature(Api.World, outstack, workItemStack.Collectible.GetTemperature(Api.World, workItemStack));
                    EncrustableCB.FireCutCompletionEvents(byPlayer, workItemStack, outstack);
                    workItemStack = null;

                    SelectedRecipeId = -1;

                    if (byPlayer?.InventoryManager.TryGiveItemstack(outstack) == true)
                    {
                        Api.World.PlaySoundFor(new AssetLocation("game:sounds/player/collect"), byPlayer, false, 24);
                    }
                    else
                    {
                        Api.World.SpawnItemEntity(outstack, Pos.ToVec3d().Add(0.5, 0.626, 0.5));
                    }

                    RegenMeshAndSelectionBoxes();
                    MarkDirty(true);
                    Api.World.BlockAccessor.MarkBlockDirty(Pos);
                    //rotation = 0;
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
                    (Api as ICoreClientAPI).TriggerIngameError(this, "toolowtier", Lang.Get("Working this metal needs a tier {0} anvil", requiredTier));
                }

                return false;
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
                    var list = gemBehavior.GetMatchingRecipes(stack);
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
                        return false;
                    }
                }

                returnOnCancelStack = slot.TakeOut(1);
                slot.MarkDirty();

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
                Voxels = new byte[16, 14, 16];
                ItemStack outstack = SelectedRecipe.Output.ResolvedItemstack.Clone();
                EncrustableCB.ApplyCuttingBuff(outstack);
                //ApplyCuttingBuff(outstack);
                //outstack.Collectible.SetTemperature(Api.World, outstack, workItemStack.Collectible.GetTemperature(Api.World, workItemStack));
                EncrustableCB.FireCutCompletionEvents(byPlayer, workItemStack, outstack);
                workItemStack = null;

                SelectedRecipeId = -1;

                if (byPlayer?.InventoryManager.TryGiveItemstack(outstack) == true)
                {
                    Api.World.PlaySoundFor(new AssetLocation("game:sounds/player/collect"), byPlayer, false, 24);
                }
                else
                {
                    Api.World.SpawnItemEntity(outstack, Pos.ToVec3d().Add(0.5, 0.626, 0.5));
                }

                RegenMeshAndSelectionBoxes();
                MarkDirty();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                rotation = 0;
            }
        }

        public void ditchWorkItemStack(IPlayer byPlayer = null)
        {
            if (workItemStack == null) return;

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

            if (byPlayer == null || !byPlayer.InventoryManager.TryGiveItemstack(ditchedStack))
            {
                Api.World.SpawnItemEntity(ditchedStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            clearWorkSpace();
        }

        protected void clearWorkSpace()
        {
            workItemStack = null;
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
            if (workItemStack != null)
            {
                workItemStack.Attributes.SetBytes("voxels", serializeVoxels(Voxels));
                workItemStack.Attributes.SetInt("selectedRecipeId", SelectedRecipeId);

                Api.World.SpawnItemEntity(workItemStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Voxels = deserializeVoxels(tree.GetBytes("voxels"));
            workItemStack = tree.GetItemstack("workItemStack");
            SelectedRecipeId = tree.GetInt("selectedRecipeId", -1);
            this.stoneType = tree.GetString("stoneType");
            this.metalType = tree.GetString("metalType");
            
            if (Api != null && workItemStack != null)
            {
                workItemStack.ResolveBlockOrItem(Api.World);
            }

            RegenMeshAndSelectionBoxes();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("voxels", serializeVoxels(Voxels));
            tree.SetItemstack("workItemStack", workItemStack);
            tree.SetInt("selectedRecipeId", SelectedRecipeId);
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

            List<ItemStack> stacks = recipes
                .Select(r => r.Output.ResolvedItemstack)
                .ToList()
            ;

            IClientWorldAccessor clientWorld = (IClientWorldAccessor)Api.World;
            ICoreClientAPI capi = Api as ICoreClientAPI;

            dlg?.Dispose();
            dlg = new GuiDialogBlockEntityRecipeSelector(
                Lang.Get("Select smithing recipe"),
                stacks.ToArray(),
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
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
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

    public enum EnumAnvilPacket
    {
        OpenDialog = 1000,
        SelectRecipe = 1001,
        OnUserOver = 1002,
        CancelSelect = 1003
    }
}
