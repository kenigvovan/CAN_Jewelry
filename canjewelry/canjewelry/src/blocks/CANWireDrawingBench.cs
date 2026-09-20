using System;
using System.Collections.Generic;
using canjewelry.src.be;
using canjewelry.src.items.resource;
using canjewelry.src.render;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;

namespace canjewelry.src.blocks
{
    public class CANWireDrawingBench: Block
    {
        /// <summary>
        /// The texture source for one mesh build, with the wood of the bench in hand pointed at its
        /// planks. Per call, not a field: a Block is one object for the whole world (see
        /// <see cref="CANTexSource"/>).
        /// </summary>
        private CANTexSource TexSource(ICoreClientAPI capi, string woodType)
        {
            var source = new CANTexSource(capi, capi.BlockTextureAtlas, this.Textures,
                "wire drawing bench " + this.Code);
            source.Overrides["plank"] = new AssetLocation("game:block/wood/planks/" + woodType + "1.png");
            source.Overrides["debarked"] = new AssetLocation("game:block/wood/debarked/" + woodType + ".png");
            return source;
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
                MeshData meshdata = GenMesh(capi, shape, null, TexSource(capi, woodType));
                return capi.Render.UploadMultiTextureMesh(meshdata);
            });
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape, ITesselatorAPI tesselator, ITexPositionSource textureSource, string part = "", Vec3f rotationDeg = null)
        {
            tesselator ??= capi.Tesselator;
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

            tesselator.TesselateShape("canjewelry wiredrawingbench", shape, out var modeldata,
                textureSource, rotationDeg, 0, 0, 0);
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

                    ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
                    bool handHoldsStrap = heldSlot.Itemstack == null || heldSlot.Itemstack.Item is CANItemStrap;

                    // Moving items is the server's call. The guard used to be commented out, so both
                    // sides took from their own copy of the inventory and the client's guess lived
                    // until the next sync - the same reason the gem cutting table documents for its
                    // own voxel state. The client only says whether the interaction was handled, so
                    // that the arm swing and the packet happen; the server does the moving.
                    if (world.Side == EnumAppSide.Client)
                    {
                        if (!blockEntity.inventory[0].Empty) return true;
                        if (handHoldsStrap && heldSlot.Itemstack != null) return true;
                    }
                    else
                    {
                        if (handHoldsStrap)
                        {
                            if (blockEntity.inventory[0].Empty)
                            {
                                if (heldSlot.TryPutInto(world, blockEntity.inventory[0], 1) > 0)
                                {
                                    blockEntity.inventory.MarkSlotDirty(0);
                                    blockEntity.MarkDirty(true);
                                    return true;
                                }
                            }
                        }
                        if (blockEntity.inventory[0].TryPutInto(world, heldSlot, blockEntity.inventory[0].StackSize) > 0)
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
            }
            Block secondPlock = world.BlockAccessor.GetBlock(pos.AddCopy(facing));
            if (secondPlock is CANWireDrawingBench && secondPlock.LastCodePart(1) != headfoot)
            {
                world.BlockAccessor.SetBlock(0, pos.AddCopy(facing));
            }
            base.OnBlockRemoved(world, pos);
        }
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            ItemStack newItem = new ItemStack(world.BlockAccessor.GetBlock(base.CodeWithParts(new string[]
                {
                    "head",
                    "north"
                })), 1);
            int rotatedIndex = GameMath.Mod(BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex, 4);
            BlockFacing nowFacing = BlockFacing.HORIZONTALS_ANGLEORDER[rotatedIndex];
            var p = pos.AddCopy(nowFacing.Opposite);
            if (world.BlockAccessor.GetBlockEntity(pos.AddCopy(nowFacing.Opposite)) is CANBEWireDrawingBench blockEntity)
            {
                if (blockEntity.woodType != null)
                {
                    newItem.Attributes.SetString("type", blockEntity.woodType);
                }
            }
            else if(world.BlockAccessor.GetBlockEntity(pos) is CANBEWireDrawingBench blockEntity1)
            {
                if (blockEntity1.woodType != null)
                {
                    newItem.Attributes.SetString("type", blockEntity1.woodType);
                }
            }
            return new ItemStack[] { newItem };
                /*if (base.LastCodePart(1) == "feet")
                    if (world.BlockAccessor.GetBlockEntity(blockSel.Position.AddCopy(nowFacing.Opposite)) is CANBEWireDrawingBench blockEntity)
                newItem.Attributes.SetString("type", this.ty)*/
            /*var f = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            if (f[0] != null)
            {
                if (f[0].Collectible.Code.Path.Contains("feet"))
                {
                    //f[0].Collectible.Code.Path = f[0].Collectible.
                }
            }
            return f;*/
        }
        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            int rotatedIndex = GameMath.Mod(BlockFacing.FromCode(base.LastCodePart(0)).HorizontalAngleIndex - angle / 90, 4);
            BlockFacing nowFacing = BlockFacing.HORIZONTALS_ANGLEORDER[rotatedIndex];
            return base.CodeWithParts(nowFacing.Code);
        }
    }
}
