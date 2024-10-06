using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Server;
using System.Collections;
using canjewelry.src.blocks;
using System.Reflection.Metadata;
using Vintagestory.API.Config;
using Vintagestory.API.Client;
using canjewelry.src.inventories;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using static canjewelry.src.OldConfig;

namespace canjewelry.src.be
{
    public class CANBEWireDrawingBench: BlockEntityContainer, ITexPositionSource
    {     
        public EnumMountAngleMode AngleMode
        {
            get
            {
                return EnumMountAngleMode.FixateYaw;
            }
        }
        private BlockEntityAnimationUtil animUtil
        {
            get
            {
                BEBehaviorAnimatable behavior = base.GetBehavior<BEBehaviorAnimatable>();
                if (behavior == null)
                {
                    return null;
                }
                return behavior.animUtil;
            }
        }
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;
        MeshData defaultMesh = null;
        public Size2i AtlasSize => this.capi.BlockTextureAtlas.Size;
        public InventoryBase inventory;
        public Dictionary<string, AssetLocation> tmpAssets = new Dictionary<string, AssetLocation>();
        public override InventoryBase Inventory => this.inventory;

        public override string InventoryClassName => "canwiredrawingbench";
        public string woodType;

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if(tmpAssets.TryGetValue(textureCode, out var assetCode))
                {
                    return this.getOrCreateTexPos(assetCode);
                }
   
                Dictionary<string, CompositeTexture> dictionary;
                dictionary = new Dictionary<string, CompositeTexture>();
                foreach(var it in this.Block.Textures)
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
            if (texturePath == null)
            {
                var c3 = 3;
            }
            TextureAtlasPosition texPos = this.capi.BlockTextureAtlas[texturePath];
            if (texPos == null)
            {
                IAsset asset = this.capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null)
                {
                    BitmapRef bitmap = asset.ToBitmap(this.capi);
                    this.capi.BlockTextureAtlas.InsertTextureCached(texturePath, (IBitmap)bitmap, out int _, out texPos);
                }
                else
                {
                    this.capi.World.Logger.Warning("For render in block " + this.Block.Code?.ToString() + ", item {0} defined texture {1}, not no such texture found.", "", (object)texturePath);
                }
            }
            return texPos;
        }
        private Vec3f animRot = new Vec3f();
        public long listenerId;
        private float secondsPassed;
        public bool resultReady;
        private float meshangle;
        public virtual float MeshAngle
        {
            get
            {
                return this.meshangle;
            }
            set
            {
                this.meshangle = value;
                this.animRot.Y = value;
            }
        }
        public CANBEWireDrawingBench() {
            this.inventory = new InventoryGeneric(1, this.InventoryClassName + "-" + this.Pos, null);
            this.inventory.Pos = this.Pos;
        }
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize("canwiredrawingbench-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.facing = BlockFacing.FromCode(base.Block.LastCodePart(0));

            // this.Block.Textures
            if (api.Side == EnumAppSide.Server)
            {
                this.sapi = api as ICoreServerAPI;
                this.MeshAngle = (BlockFacing.FromCode(base.Block.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
            }
            else
                this.capi = api as ICoreClientAPI;
            if (api.Side == EnumAppSide.Client)
            {
                BlockEntityAnimationUtil animUtil = this.animUtil;
                if (animUtil == null)
                {
                    return;
                }
                //var rotatedIndex = (BlockFacing.FromCode(base.Block.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
                this.MeshAngle = (BlockFacing.FromCode(base.Block.LastCodePart(0)).HorizontalAngleIndex - 1) * 90;
                //animRot.Y = rotatedIndex;
                //animUtil.InitializeAnimator("wiring", (base.Block as CANWireDrawingBench).GetShape(EnumCurdsBundleState.BundledStick), null, this.animRot);
                var f = this.Pos.AddCopy(BlockFacing.FromCode(base.Block.LastCodePart(0)).GetCW());
                Block secondPlock = api.World.BlockAccessor.GetBlock(f);
                //canjewelry.capi.Render.UploadMultiTextureMesh
                string part = this.Block.LastCodePart(1);
                string orient = this.Block.LastCodePart(0);
                string wire = GetWireType();

                if (this.defaultMesh == null && woodType is not null)
                {
                    this.defaultMesh = this.getMesh(canjewelry.capi.Tesselator, part, this.animRot);
                    if (!this.inventory[0].Empty)
                    {
                        this.defaultMesh = animUtil.InitializeAnimator("wiring2" + string.Concat(new string[]
                        {
                        "head", orient, wire, woodType
                        }), Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"), this, this.animRot);
                    }
                }
            }        
        }
        private void onSqueezing(float dt)
        {
            this.secondsPassed += dt;
            if (this.secondsPassed > 3f)
            {
                BlockEntityAnimationUtil animUtil = this.animUtil;
                if (animUtil != null)
                {
                    animUtil.StopAnimation("wiring");
                }
                if(this.Api.Side == EnumAppSide.Server && !this.inventory[0].Empty)
                {
                    this.inventory[0].Itemstack = new ItemStack(canjewelry.sapi.World.GetItem(new AssetLocation("canjewelry:canwirehank-" + GetWireType())), canjewelry.config.wirehank_per_strap);
                }
                else
                {
                    this.tmpAssets.Remove("wire");

                }
                this.Api.World.UnregisterGameTickListener(this.listenerId);
                
                listenerId = 0;
                this.resultReady = true;
                this.MarkDirty(true);
            }
        }
        internal void StartSqueeze(IPlayer byPlayer)
        {
            if(this.listenerId != 0L || resultReady)
            {
                return;
            }
            if (this.Api.Side == EnumAppSide.Client)
            {
                this.startWiringAnim();
            }
            else
            {
                (this.Api as ICoreServerAPI).Network.BroadcastBlockEntityPacket(this.Pos.X, this.Pos.Y, this.Pos.Z, 1010, null);
            }
            this.Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/creak/woodcreak_2.ogg"), (double)this.Pos.X + 0.5, (double)this.Pos.Y + 0.5, (double)this.Pos.Z + 0.5, byPlayer, false, 32f, 3f);
            this.listenerId = this.Api.World.RegisterGameTickListener(new Action<float>(this.onSqueezing), 20, 0);
            this.secondsPassed = 0f;
        }
        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == 1010)
            {
                this.startWiringAnim();
            }
            base.OnReceivedServerPacket(packetid, data);
        }
        private void startWiringAnim()
        {
            this.animUtil.StartAnimation(new AnimationMetaData
            {
                Animation = "wiring",
                Code = "wiring",
                AnimationSpeed = 0.3f,
                EaseOutSpeed = 3f,
                EaseInSpeed = 3f
            });
        }
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            this.animUtil?.Dispose();
            base.OnBlockBroken(byPlayer);
        }
        public override void OnBlockUnloaded()
        {
            this.animUtil?.Dispose();
            base.OnBlockUnloaded();
        }
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            this.inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            this.resultReady = tree.GetBool("resultReady");
            if (!this.resultReady)
            {
                this.tmpAssets["wireready"] = new AssetLocation("canjewelry:item/gem/notvis.png");
            }
            this.MeshAngle = tree.GetFloat("meshAngle", this.MeshAngle);
            bool updateMesh = false;
            if(this.woodType is null)
            {
                updateMesh = true;
            }
            this.woodType = tree.GetString("woodType");
            if (this.Api != null && this.Api.Side == EnumAppSide.Client)
            {
                this.UpdateWirePart();
            }

        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute tree1 = (ITreeAttribute)new TreeAttribute();
            this.inventory.ToTreeAttributes(tree1);
            tree["inventory"] = (IAttribute)tree1;
            tree.SetBool("resultReady", this.resultReady);
            tree.SetString("woodType", this.woodType);
            tree.SetFloat("meshAngle", this.MeshAngle);
        }
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string part = this.Block.LastCodePart(1);
            if (!base.OnTesselation(mesher, tessThreadTesselator))
            {
                //this.defaultMesh = this.getMesh(tessThreadTesselator, part);
                if (this.defaultMesh == null)
                {
                    this.defaultMesh = this.getMesh(tessThreadTesselator, part);
                    if (this.defaultMesh == null)
                    {
                        return false;
                    }
                }
                mesher.AddMeshData(this.defaultMesh.Clone());
            }
            return true;
        }

        public string GetWireType()
        {
            if (!Inventory[0].Empty)
            {
                return Inventory[0].Itemstack.Item.LastCodePart();
            }
            return "";
        }
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            if (((byItemStack != null) ? byItemStack.Attributes : null) != null)
            {
                this.woodType = byItemStack.Attributes.GetString("type", "oak");
                string part = this.Block.LastCodePart(1);
                string orient = this.Block.LastCodePart(0);
                string wire = GetWireType();

                if (this.Api.Side == EnumAppSide.Client && this.defaultMesh == null && woodType is not null)
                {
                    //this.capi = canjewelry.capi;
                    //this.defaultMesh = this.getMesh(canjewelry.capi.Tesselator, part, this.animRot);
                    if (!this.inventory[0].Empty)
                    {
                        this.defaultMesh = animUtil.InitializeAnimator("wiring2" + string.Concat(new string[]
                        {
                        "head", orient, wire, woodType
                        }), Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"), this, this.animRot);
                    }
                }
            }
            if (this.Api.Side == EnumAppSide.Client && byItemStack != null)
            {
                this.UpdateWirePart();
            }
            base.OnBlockPlaced(byItemStack);
        }
        private MeshData getMesh(ITesselatorAPI tesselator, string part, Vec3f rotationDeg = null)
        {
            Dictionary<string, MeshData> lanternMeshes = ObjectCacheUtil.GetOrCreate<Dictionary<string, MeshData>>(this.Api, "blockLanternBlockMeshes", () => new Dictionary<string, MeshData>());
            MeshData mesh = null;
            CANWireDrawingBench block = this.Api.World.BlockAccessor.GetBlock(this.Pos) as CANWireDrawingBench;
            if (block == null)
            {
                return null;
            }
            //lanternMeshes.Clear();
            string orient = block.LastCodePart(0);
            string wire = GetWireType();
            if (!this.inventory[0].Empty && !this.resultReady)
            {
                this.tmpAssets["wire"] = this.inventory[0].Itemstack.Item.Textures["metal"].Base;
            }
            if (!this.inventory[0].Empty && this.resultReady)
            {
                this.tmpAssets["wireready"] = this.inventory[0].Itemstack.Item.Textures["metal"].Base;
                this.tmpAssets["wire"] = new AssetLocation("canjewelry:item/gem/notvis.png");
            }

            if (this.inventory[0].Empty)
            {
                this.tmpAssets["wire"] = new AssetLocation("canjewelry:item/gem/notvis.png");
            }

            this.tmpAssets["plank"] = new AssetLocation("game:block/wood/planks/" + this.woodType +"1.png");
            this.tmpAssets["debarked"] = new AssetLocation("game:block/wood/debarked/" + this.woodType + ".png");
            if (lanternMeshes.TryGetValue(string.Concat(new string[]
            {
                part, orient, wire, woodType
            }), out mesh))
            {
                return mesh;
            }
            
            return lanternMeshes[string.Concat(new string[]
            {
                part, orient, wire, woodType
            })] = GenMesh(this.Api as ICoreClientAPI, null,  tesselator, this, part, rotationDeg);
        }
        public MeshData GenMesh(ICoreClientAPI capi, Shape shape = null, ITesselatorAPI tesselator = null, ITexPositionSource textureSource = null, string part = "", Vec3f rotationDeg = null)
        {         
            if (shape == null)
            {
                if (part == "head")
                {
                    shape = Vintagestory.API.Common.Shape.TryGet(this.capi, "canjewelry:shapes/block/wiretable.json");
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

            tesselator.TesselateShape("blocklantern", shape, out var modeldata, this, this.animRot, 0, 0, 0);
            return modeldata;
        }
        public void UpdateWirePart()
        {
            Dictionary<string, MeshData> lanternMeshes = ObjectCacheUtil.GetOrCreate<Dictionary<string, MeshData>>(this.Api, "blockLanternBlockMeshes", () => new Dictionary<string, MeshData>());
            MeshData mesh = null;
            CANWireDrawingBench block = this.Api.World.BlockAccessor.GetBlock(this.Pos) as CANWireDrawingBench;
            if (block == null)
            {
                return;
            }
            if (!this.inventory[0].Empty && !this.resultReady) 
            {
                this.tmpAssets["wire"] = this.inventory[0].Itemstack.Item.Textures["metal"].Base;
            }
            if (!this.inventory[0].Empty && this.resultReady)
            {
                this.tmpAssets["wireready"] = this.inventory[0].Itemstack.Item.Textures["metal"].Base;
                this.tmpAssets["wire"] = new AssetLocation("canjewelry:item/gem/notvis.png");
            }

            if (this.inventory[0].Empty)
            {
                this.tmpAssets["wire"] = new AssetLocation("canjewelry:item/gem/notvis.png");
            }
            this.tmpAssets["plank"] = new AssetLocation("game:block/wood/planks/" + this.woodType + "1.png");
            this.tmpAssets["debarked"] = new AssetLocation("game:block/wood/debarked/" + this.woodType + ".png");
            string orient = block.LastCodePart(0);
            string wire = GetWireType();
            string part = block.LastCodePart(1);
            string key = string.Concat(new string[]
            {
                "head", orient, wire, woodType
            });

            if (lanternMeshes.TryGetValue(key, out mesh))
            {
                this.defaultMesh = mesh;
            }
            animUtil.InitializeAnimator("wiring2" + key, Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"), this, this.animRot);

            this.defaultMesh = GenMesh(this.Api as ICoreClientAPI, null, canjewelry.capi.Tesselator, this, "head", this.animRot);
            lanternMeshes[key] = this.defaultMesh;
        }
        private BlockFacing facing;
}
}
