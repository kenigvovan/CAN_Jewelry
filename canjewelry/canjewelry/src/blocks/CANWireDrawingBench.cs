using Cairo;
using canjewelry.src.be;
using canjewelry.src.items;
using canjewelry.src.jewelry;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace canjewelry.src.blocks
{
    public class CANWireDrawingBench: Block, ITexPositionSource
    {
        public Size2i AtlasSize { get; set; }
        private ITexPositionSource ownTextureSource;
        public ITexPositionSource tmpTextureSource;
        
        private ITextureAtlasAPI curAtlas;
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
        private TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            TextureAtlasPosition texPos = (this.api as ClientCoreAPI).BlockTextureAtlas[texturePath];
            if (texPos == null)
            {
                IAsset asset = this.api.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null)
                {
                    BitmapRef bitmap = asset.ToBitmap((this.api as ClientCoreAPI));
                    (this.api as ClientCoreAPI).BlockTextureAtlas.InsertTextureCached(texturePath, (IBitmap)bitmap, out int _, out texPos);
                }
                else
                    (this.api as ClientCoreAPI).World.Logger.Warning("For render in block " + this.Code?.ToString() + ", item {0} defined texture {1}, not no such texture found.", "", (object)texturePath);
            }
            return texPos;
        }
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            string part = base.LastCodePart(1);
            string heading = this.Variant["side"];
            if (part == "head" && heading.Equals("west"))
            {
                AddAllTypesToCreativeInventory();
            }
        }
        public void AddAllTypesToCreativeInventory()
        {
            List<JsonItemStack> stacks = new List<JsonItemStack>();
            Dictionary<string, string[]> vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);
            Random r = new Random();

            string[] woodType = vg["woodType"][0..2];
            foreach (string loop in woodType)
            {
                stacks.Add(this.genJstack(string.Format("{{ type: \"{0}\" }}", loop)));            
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
            jsonItemStack.Resolve(this.api.World, "can wire drawing bench type", true);
            return jsonItemStack;
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string blockMaterialCode = "iron";//this.GetBlockMaterialCode(itemstack);
            if (blockMaterialCode == null)
            {
                return;
            }
            string woodType = itemstack.Attributes.GetString("type", "oak");
            //this.tmpAssets["plank"] = new AssetLocation("game:block/wood/planks/" + woodType + "1.png");
           // this.tmpAssets["debarked"] = new AssetLocation("game:block/wood/debarked/" + woodType + ".png");

            string key = "draw" + base.LastCodePart(0) + base.LastCodePart(1) + woodType;
            renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate<MultiTextureMeshRef>(capi, key, delegate
            {
                var c = base.LastCodePart(1);
                AssetLocation shapeloc = null;
                if (c == "head") {
                    shapeloc = new AssetLocation("canjewelry:shapes/block/wiretable.json");
                }
                else
                {
                    shapeloc = new AssetLocation("canjewelry:shapes/block/wiretable-feet.json");
                }
                Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapeloc);
                Block block = capi.World.GetBlock(new AssetLocation(blockMaterialCode));
                this.AtlasSize = capi.BlockTextureAtlas.Size;
                //this.matTexPosition = capi.BlockTextureAtlas.GetPosition(block, "up", false);
                this.ownTextureSource = capi.Tesselator.GetTextureSource(this, 0, false);
                
                string metalType = itemstack.Attributes.GetString("metal", "copper");
                this.tmpAssets["plank"] = new AssetLocation("game:block/wood/planks/" + woodType + "1.png");
                this.tmpAssets["debarked"] = new AssetLocation("game:block/wood/debarked/" + woodType + ".png");
                MeshData meshdata;
                meshdata = GenMesh(capi, shape, null, this);
                //capi.Tesselator.TesselateShape("filledpan", shape, out meshdata, this, new Vec3f(0, 0, 0), 0, 0, 0, null, null);
                return capi.Render.UploadMultiTextureMesh(meshdata);
            });
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape = null, ITesselatorAPI tesselator = null, ITexPositionSource textureSource = null, string part = "", Vec3f rotationDeg = null)
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
                if (part == "head")
                {
                    shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/wiretable.json");
                }
                else
                {
                    shape = Vintagestory.API.Common.Shape.TryGet(capi, "canjewelry:shapes/block/wiretable-feet.json");
                }
            }

            if (shape == null)
            {
                return null;
            }

            AtlasSize = capi.BlockTextureAtlas.Size;
            //var f = (BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
            tesselator.TesselateShape("blocklantern", shape, out var modeldata, this, rotationDeg, 0, 0, 0);
            return modeldata;
        }
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return false;
            }
            if (!this.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                return false;
            }
            BlockFacing[] horVer = Block.SuggestedHVOrientation(byPlayer, blockSel);
            horVer[0] = horVer[0].GetCW();
            BlockPos secondPos = blockSel.Position.AddCopy(horVer[0]);
            BlockSelection secondBlockSel = new BlockSelection
            {
                Position = secondPos,
                Face = BlockFacing.UP
            };
            if (!this.CanPlaceBlock(world, byPlayer, secondBlockSel, ref failureCode))
            {
                return false;
            }
            string code = horVer[0].Code;
            world.BlockAccessor.GetBlock(base.CodeWithParts(new string[]
            {
                "feet",
                code
            })).DoPlaceBlock(world, byPlayer, secondBlockSel, itemstack);
            AssetLocation feetCode = base.CodeWithParts(new string[]
            {
                "head",
                code
            });
            world.BlockAccessor.GetBlock(feetCode).DoPlaceBlock(world, byPlayer, blockSel, itemstack);
            return true;
        }
        
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return false;
            }
            string orient = base.LastCodePart(0);
            var c = BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex;
            int rotatedIndex = GameMath.Mod(BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex, 4);
            BlockFacing nowFacing = BlockFacing.HORIZONTALS_ANGLEORDER[rotatedIndex];
            var p = blockSel.Position.AddCopy(nowFacing.Opposite);
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position.AddCopy(nowFacing.Opposite)) is CANBEWireDrawingBench blockEntity)
            {
                if (base.LastCodePart(1) == "feet")
                {
                    if(blockEntity.listenerId != 0)
                    {
                        return false;
                    }
                    //if (world is IServerWorldAccessor)
                    {
                        if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack == null || byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Item is CANItemStrap)
                        {
                            if (blockEntity.inventory[0].Empty)
                            {
                                if (byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(world, blockEntity.inventory[0], 1) > 0)
                                {
                                    blockEntity.inventory.MarkSlotDirty(0);
                                    blockEntity.MarkDirty(true);                                    
                                    return true;
                                }
                            }                         
                        }
                        if (blockEntity.inventory[0].TryPutInto(world, byPlayer.InventoryManager.ActiveHotbarSlot, blockEntity.inventory[0].StackSize) > 0)
                        {
                            blockEntity.resultReady = false;
                            blockEntity.MarkDirty(true);
                            blockEntity.inventory.MarkSlotDirty(0);
                            return true;
                        }
                    }   
                }
            }
            BlockFacing facing = BlockFacing.FromCode(base.LastCodePart(0)).Opposite;
            CANBEWireDrawingBench beDrawingBench = world.BlockAccessor.GetBlockEntity((base.LastCodePart(1) == "feet") ? blockSel.Position.AddCopy(facing) : blockSel.Position) as CANBEWireDrawingBench;
            if (beDrawingBench == null || beDrawingBench.resultReady || beDrawingBench.inventory[0].Empty)
            {
                return false;
            }
            beDrawingBench.StartSqueeze(byPlayer);
            return true;         
        }
        public Shape GetShape()
        {
            var c = base.LastCodePart(1);
            string shapePath = "canjewelry:shapes/block/wiretable-" + c + ".json";
            /*switch (state)
            {
                case EnumCurdsBundleState.BundledStick:
                    shapePath = "shapes/block/food/curdbundle-stick.json";
                    break;
                case EnumCurdsBundleState.Opened:
                    shapePath = "shapes/item/food/dairy/cheese/linen-raw.json";
                    break;
                case EnumCurdsBundleState.OpenedSalted:
                    shapePath = "shapes/item/food/dairy/cheese/linen-salted.json";
                    break;
            }*/

            return Vintagestory.API.Common.Shape.TryGet(api, shapePath);
        }
        public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
        {
            return Lang.Get("canjewelry:block-canwiredrawingbench");
        }
        public override string GetHeldItemName(ItemStack itemStack)
        {
            return Lang.Get("canjewelry:block-canwiredrawingbench");
        }
        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            string headfoot = base.LastCodePart(1);
            BlockFacing facing = BlockFacing.FromCode(base.LastCodePart(0));
            if (base.LastCodePart(1) == "feet")
            {
                facing = facing.Opposite;
            }
            else
            {
                facing = facing;
            }
            Block secondPlock = world.BlockAccessor.GetBlock(pos.AddCopy(facing));
            if (secondPlock is CANWireDrawingBench && secondPlock.LastCodePart(1) != headfoot)
            {
                world.BlockAccessor.SetBlock(0, pos.AddCopy(facing));
            }
            base.OnBlockRemoved(world, pos);
        }
        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            int rotatedIndex = GameMath.Mod(BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex - angle / 90, 4);
            BlockFacing nowFacing = BlockFacing.HORIZONTALS_ANGLEORDER[rotatedIndex];
            return base.CodeWithParts(nowFacing.Code);
        }
    }
}
