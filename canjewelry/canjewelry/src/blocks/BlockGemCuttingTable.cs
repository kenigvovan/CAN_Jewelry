using canjewelry.src.be;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace canjewelry.src.blocks
{
    public class BlockGemCuttingTable : Block, ITexPositionSource
    {
        WorldInteraction[] interactions;
        public ITexPositionSource tmpTextureSource;
        //private ITexPositionSource ownTextureSource;
        private ICoreClientAPI capi;
        private ITextureAtlasAPI curAtlas;

        public Size2i AtlasSize { get; set; }
        public Dictionary<string, AssetLocation> tmpAssets = new Dictionary<string, AssetLocation>();
        private TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            /*if (texturePath == null)
            {
                var c3 = 3;
            }*/
            TextureAtlasPosition texPos = curAtlas[texturePath];
            if (texPos == null)
            {
                IAsset asset = canjewelry.capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null)
                {
                    BitmapRef bitmap = asset.ToBitmap(canjewelry.capi);
                    canjewelry.capi.BlockTextureAtlas.InsertTextureCached(texturePath, (IBitmap)bitmap, out int _, out texPos);
                }
                else
                {
                    canjewelry.capi.World.Logger.Warning("For render in block " + this.Code?.ToString() + ", item {0} defined texture {1}, not no such texture found.", "", (object)texturePath);
                }
            }
            return texPos;
        }
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
                foreach (var it in this.Textures)
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
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            AddAllTypesToCreativeInventory();
            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;

            this.AtlasSize = capi.BlockTextureAtlas.Size;

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

            interactions = ObjectCacheUtil.GetOrCreate(api, "anvilBlockInteractions" + ownMetalTier, () =>
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
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
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
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                            return bea?.WorkItemStack == null ? wi.Itemstacks : null;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-smith",
                        MouseButton = EnumMouseButton.Left,
                        Itemstacks = hammerStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                            return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                        }
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-anvil-rotateworkitem",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = hammerStacklist.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
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
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
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
                            BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                            return bea?.WorkItemStack == null ? null : new ItemStack[] { (bea.WorkItemStack.Collectible as IAnvilWorkable).GetBaseMaterial(bea.WorkItemStack) };
                        }
                    }
                };
            });
        }

        public void AddAllTypesToCreativeInventory()
        {
            List<JsonItemStack> stacks = new List<JsonItemStack>();
            Dictionary<string, string[]> vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);

            string[] metals = vg["metal"][0..2];
            string[] stones = vg["stone"][0..2];
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
            BlockEntityAnvil bect = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAnvil;
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
                float angledeg = Math.Abs(bea.MeshAngle * GameMath.RAD2DEG);
                selectionBoxes[0] = angledeg == 0 || angledeg == 180 ? SelectionBoxes[0] : SelectionBoxes[1];
                return selectionBoxes;
            }

            return base.GetSelectionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetSelectionBoxes(blockAccessor, pos);
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
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
                BlockEntityAnvil bect = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityAnvil;
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
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape = null, ITesselatorAPI tesselator = null, ITexPositionSource textureSource = null)
        {
            if (tesselator == null)
            {
                tesselator = capi.Tesselator;
            }
            curAtlas = capi.BlockTextureAtlas;
            if (textureSource != null)
            {
                tmpTextureSource = textureSource;
            }
            else
            {
                tmpTextureSource = tesselator.GetTextureSource(this);
            }
            if (shape == null)
            {
                shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json");               
            }

            if (shape == null)
            {
                return null;
            }
            
            AtlasSize = capi.BlockTextureAtlas.Size;
            //var f = (BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
            tesselator.TesselateShape("gemcuttingtable", shape, out var modeldata, this);
            return modeldata;
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (((itemstack != null) ? itemstack.Attributes : null) != null)
            {
                string stoneType = itemstack.Attributes.GetString("stone", "granite");
                string metalType = itemstack.Attributes.GetString("metal", "copper");
                this.tmpAssets["granite"] = new AssetLocation("game:block/stone/polishedrock/" + stoneType + ".png");
                this.tmpAssets["iron"] = new AssetLocation("game:block/metal/sheet/" + metalType + "1.png");

                string key = stoneType + metalType;
                renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate<MultiTextureMeshRef>(capi, key, delegate
                {
                    var c = base.LastCodePart(1);
                    Shape shape = null;
                    shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/gemcuttingtable.json");
                    this.AtlasSize = capi.BlockTextureAtlas.Size;
                    //this.matTexPosition = capi.BlockTextureAtlas.GetPosition(block, "up", false);
                    this.tmpTextureSource = capi.Tesselator.GetTextureSource(this);
                    MeshData meshdata;
                    meshdata = GenMesh(capi, shape, null, this);
                   // capi.Tesselator.TesselateShape("gemcuttingtable", shape, out meshdata, this);
                    return capi.Render.UploadMultiTextureMesh(meshdata);
                });
            }
        }
        
    }
}
