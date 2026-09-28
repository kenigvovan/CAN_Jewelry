using System;
using System.Collections.Generic;
using canjewelry.src.blocks;
using canjewelry.src.render;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.be
{
    public class CANBEWireDrawingBench: BlockEntityContainer
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
        public InventoryBase inventory;
        public override InventoryBase Inventory => this.inventory;

        public override string InventoryClassName => "canwiredrawingbench";
        public string woodType;
        // Set in StartSqueeze, consumed in onSqueezing — needed so OnWireDrawn knows who to refund.
        private IPlayer pendingSqueezer;

        /// <summary>
        /// The texture source for one mesh build: the block's own textures, plus the wood this bench
        /// was built from and the metal of the wire currently on it.
        /// </summary>
        private CANTexSource TexSource()
        {
            var source = new CANTexSource(this.capi, this.capi.BlockTextureAtlas, this.Block?.Textures,
                "wire drawing bench " + this.Block?.Code);

            source.Overrides["plank"] = new AssetLocation("game:block/wood/planks/" + this.woodType + "1.png");
            source.Overrides["debarked"] = new AssetLocation("game:block/wood/debarked/" + this.woodType + ".png");

            // The wire is drawn in one of two places depending on whether it is done, and the other
            // place gets the invisible texture rather than being left to the block's own.
            AssetLocation invisible = new AssetLocation("canjewelry:item/gem/notvis.png");
            AssetLocation metal = this.inventory[0].Empty
                ? null
                : this.inventory[0].Itemstack?.Item?.Textures?["metal"]?.Base;

            if (metal != null && !this.resultReady)
            {
                source.Overrides["wire"] = metal;
            }
            else if (metal != null)
            {
                source.Overrides["wireready"] = metal;
                source.Overrides["wire"] = invisible;
            }
            else
            {
                source.Overrides["wire"] = invisible;
            }

            return source;
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
                        }), Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"), TexSource(), this.animRot);
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
                    ItemStack output = new ItemStack(canjewelry.sapi.World.GetItem(new AssetLocation("canjewelry:canwirehank-" + GetWireType())), canjewelry.config.wirehank_per_strap);

                    var ev = new src.integration.WireDrawEvent
                    {
                        Player = this.pendingSqueezer,
                        Input = this.inventory[0].Itemstack,
                        Output = output,
                    };
                    canjewelry.Instance?.FireWireDraw(ev);

                    this.inventory[0].Itemstack = ev.Output;
                }
                // The client used to drop its "wire" texture override here; the override is now
                // worked out per mesh build from the inventory and resultReady, so there is nothing
                // left to undo.
                this.pendingSqueezer = null;
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
            this.pendingSqueezer = byPlayer;
            if (this.Api.Side == EnumAppSide.Client)
            {
                this.startWiringAnim();
            }
            else
            {
                (this.Api as ICoreServerAPI).Network.BroadcastBlockEntityPacket(this.Pos, 1010, null);
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
            this.MeshAngle = tree.GetFloat("meshAngle", this.MeshAngle);
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
                        }), Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"), TexSource(), this.animRot);
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
            // Under our own key: this used to sit under "blockLanternBlockMeshes", the key the
            // vanilla lantern uses, so the two shared one dictionary.
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(this.Api,
                "canjewelry:wireDrawingBenchMeshes", () => new Dictionary<string, MeshData>());

            if (this.Api.World.BlockAccessor.GetBlock(this.Pos) is not CANWireDrawingBench block)
            {
                return null;
            }

            // The wire state is part of the key as well as of the textures: an empty bench, one
            // being worked and one with the wire ready are three different meshes.
            string key = string.Concat(part, "-", block.LastCodePart(0), "-", GetWireType(), "-", woodType,
                "-", this.resultReady ? "ready" : this.inventory[0].Empty ? "empty" : "working");

            if (meshes.TryGetValue(key, out MeshData mesh)) return mesh;

            return meshes[key] = GenMesh(this.Api as ICoreClientAPI, null, tesselator, TexSource(), part, rotationDeg);
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

            tesselator.TesselateShape("canjewelry wiredrawingbench", shape, out var modeldata,
                textureSource ?? TexSource(), this.animRot, 0, 0, 0);
            return modeldata;
        }
        /// <summary>
        /// Builds the head of the bench again after the wire on it changed. Goes through the same
        /// cache as <see cref="getMesh"/> — it used to read that cache and then overwrite the entry
        /// with a fresh build regardless, so the lookup never saved anything.
        /// </summary>
        public void UpdateWirePart()
        {
            if (this.Api.World.BlockAccessor.GetBlock(this.Pos) is not CANWireDrawingBench block) return;

            this.defaultMesh = getMesh(canjewelry.capi.Tesselator, "head", this.animRot);

            string key = string.Concat("head-", block.LastCodePart(0), "-", GetWireType(), "-", woodType);
            animUtil?.InitializeAnimator("wiring2" + key,
                Vintagestory.API.Common.Shape.TryGet(canjewelry.capi, "canjewelry:shapes/block/wiretable.json"),
                TexSource(), this.animRot);
        }
        private BlockFacing facing;
}
}
