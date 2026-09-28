using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src.be;
using canjewelry.src.jewelry;
using canjewelry.src.render;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.blocks
{
    public class BlockGemCuttingTable : Block
    {
        WorldInteraction[] interactions;

        /// <summary>
        /// The texture source for one mesh build, with the stone and metal of the table in hand
        /// pointed at their materials. Per call rather than per block: a Block is one object for the
        /// whole world, while this is asked from the render thread and from chunk tesselation alike,
        /// and the dictionary this replaces was a field on that shared object.
        /// </summary>
        private CANTexSource TexSource(ICoreClientAPI capi, string stoneType, string metalType)
        {
            var source = new CANTexSource(capi, capi.BlockTextureAtlas, this.Textures,
                "gem cutting table " + this.Code);
            source.Overrides["granite"] = new AssetLocation("game:block/stone/polishedrock/" + stoneType + ".png");
            source.Overrides["iron"] = new AssetLocation("game:block/metal/sheet/" + metalType + "1.png");
            return source;
        }
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            AddAllTypesToCreativeInventory();
            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;

            Dictionary<string, MetalPropertyVariant> metalsByCode = new Dictionary<string, MetalPropertyVariant>();

            MetalProperty metals = api.Assets.TryGet("worldproperties/block/metal.json").ToObject<MetalProperty>();
            for (int i = 0; i < metals.Variants.Length; i++)
            {
                // Metals currently don't have a domain
                metalsByCode[metals.Variants[i].Code.Path] = metals.Variants[i];
            }

            string metalType = LastCodePart();
            int ownMetalTier = 0;
            if (metalsByCode.ContainsKey(metalType)) ownMetalTier = metalsByCode[metalType].Tier;

            // Our own key: this used to be "anvilBlockInteractions" + tier, the very key vanilla's
            // BlockAnvil uses, so whichever block loaded first decided what help text both showed.
            interactions = ObjectCacheUtil.GetOrCreate(api, "canjewelry:gemCuttingTableInteractions" + ownMetalTier, () =>
            {
                List<ItemStack> workableStacklist = new List<ItemStack>();
                List<ItemStack> hammerStacklist = new List<ItemStack>();


                bool viableTier = metalsByCode.ContainsKey(metalType) && metalsByCode[metalType].Tier <= ownMetalTier + 1;
                foreach (Item item in api.World.Items)
                {
                    if (item.Code == null) continue;

                    if (item is ItemIngot && viableTier)
                    {
                        workableStacklist.Add(new ItemStack(item));
                    }

                    if (item is ItemHammer)
                    {
                        hammerStacklist.Add(new ItemStack(item));
                    }
                }

                return new WorldInteraction[] {
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-takeworkable",
                        HotKeyCode = null,
                        MouseButton = EnumMouseButton.Right,
                        ShouldApply = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            return bea?.WorkItemStack != null;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-placeworkable",
                        HotKeyCode = "shift",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = workableStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            return bea?.WorkItemStack == null ? wi.Itemstacks : null;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-smith",
                        MouseButton = EnumMouseButton.Left,
                        Itemstacks = hammerStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-rotateworkitem",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = hammerStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-selecttoolmode",
                        HotKeyCode = "toolmodeselect",
                        MouseButton = EnumMouseButton.None,
                        Itemstacks = hammerStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-addvoxels",
                        HotKeyCode = "shift",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = workableStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityGemCuttingTable bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityGemCuttingTable;
                            // Null when the work item is not gem-cuttable, which is a "no hint"
                            // rather than a crash - the cast used to be to IAnvilWorkable.
                            ItemStack baseMaterial = (bea?.WorkItemStack?.Collectible as IGemCuttingWorkable)
                                ?.GetBaseMaterial(bea.WorkItemStack);
                            return baseMaterial == null ? null : new ItemStack[] { baseMaterial };
                        }
                    }
                };
            });
        }

        public void AddAllTypesToCreativeInventory()
        {
            List<JsonItemStack> stacks = new List<JsonItemStack>();
            Dictionary<string, string[]> vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);

            string[] metals = vg["metal"][..Math.Min(2, vg["metal"].Length)];
            string[] stones = vg["stone"][..Math.Min(2, vg["stone"].Length)];
            foreach (string metal in metals) 
            {
                foreach (string stone in stones)
                {
                    stacks.Add(this.genJstack(string.Format("{{ metal: \"{0}\", stone: \"{1}\"}}", metal, stone)));
                }
            }
            this.CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList
                {
                    Stacks = stacks.ToArray(),
                    Tabs = new string[]
                    {
                        "general",
                        "canjewelry"
                    }
                }
            };
        }
        private JsonItemStack genJstack(string json)
        {
            JsonItemStack jsonItemStack = new JsonItemStack();
            jsonItemStack.Code = this.Code;
            jsonItemStack.Type = EnumItemClass.Block;
            jsonItemStack.Attributes = new JsonObject(JToken.Parse(json));
            jsonItemStack.Resolve(this.api.World, "gemcuttingtable type", true);
            return jsonItemStack;
        }
        public override void OnDecalTesselation(IWorldAccessor world, MeshData decalMesh, BlockPos pos)
        {
            base.OnDecalTesselation(world, decalMesh, pos);
            BlockEntityGemCuttingTable bect = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGemCuttingTable;
            if (bect != null)
            {
                decalMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, bect.MeshAngle, 0);
            }
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntityGemCuttingTable bea = blockAccessor.GetBlockEntity(pos) as BlockEntityGemCuttingTable;
            if (bea != null)
            {
                Cuboidf[] selectionBoxes = bea.GetSelectionBoxes(blockAccessor, pos);
                selectionBoxes[0] = SelectionBoxes[0];
                return selectionBoxes;
            }

            return base.GetSelectionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetSelectionBoxes(blockAccessor, pos);
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntityGemCuttingTable bea = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityGemCuttingTable;
            if (bea != null)
            {
                if (bea.OnPlayerInteract(world, byPlayer, blockSel))
                {
                    return true;
                }

                return false;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool val = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

            if (val)
            {
                BlockEntityGemCuttingTable bect = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityGemCuttingTable;
                if (bect != null)
                {
                    BlockPos targetPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
                    double dx = byPlayer.Entity.Pos.X - (targetPos.X + blockSel.HitPosition.X);
                    double dz = byPlayer.Entity.Pos.Z - (targetPos.Z + blockSel.HitPosition.Z);
                    float angleHor = (float)Math.Atan2(dx, dz);

                    float deg22dot5rad = GameMath.PIHALF / 4;
                    float roundRad = ((int)Math.Round(angleHor / deg22dot5rad)) * deg22dot5rad;
                    bect.MeshAngle = roundRad;
                }
            }

            return val;
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape, ITesselatorAPI tesselator, ITexPositionSource textureSource)
        {
            tesselator ??= capi.Tesselator;
            shape ??= Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json");
            if (shape == null) return null;

            tesselator.TesselateShape("gemcuttingtable", shape, out var modeldata, textureSource);
            return modeldata;
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (itemstack?.Attributes == null) return;

            string stoneType = itemstack.Attributes.GetString("stone", "granite");
            string metalType = itemstack.Attributes.GetString("metal", "copper");

            // Under our own key: a bare "granitecopper" is a name any other mod could use for its
            // own mesh in the same shared cache.
            string key = "canjewelry:gemcuttingtable-" + stoneType + "-" + metalType;
            renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, key, delegate
            {
                Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json");
                MeshData meshdata = GenMesh(capi, shape, null, TexSource(capi, stoneType, metalType));
                return capi.Render.UploadMultiTextureMesh(meshdata);
            });
        }
    }
}
