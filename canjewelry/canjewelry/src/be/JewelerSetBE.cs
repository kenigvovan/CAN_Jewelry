using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using canjewelry.src.inventories;
using canjewelry.src.items;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.jewelry
{
    public class JewelerSetBE : BlockEntityOpenableContainer, ITexPositionSource
    {
        public InventoryJewelerSet inventory;
        protected MeshData mesh;
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;
        protected CollectibleObject nowTesselatingObj;
        protected Shape nowTesselatingShape;
        GuiDialogJewelerSet renameGui;
        // public static Vec3f centerVector = new Vec3f(0.5f, 0.5f, 0.5f);
        public override InventoryBase Inventory => this.inventory;

        public override string InventoryClassName => "canjewelerset";

        public Size2i AtlasSize => this.capi.BlockTextureAtlas.Size;
        public JewelerSetBE()
        {
            this.inventory = new InventoryJewelerSet((string)null, (ICoreAPI)null);
            // this.inventory.SlotModified += new Action<int>(this.OnSlotModified);
            this.inventory.Pos = this.Pos;
            // this.meshes = new MeshData[this.inventory.Count - 1];
            //this.inventory = new InventoryJewelerSet(4, (string)null, (ICoreAPI)null);
            //  this.inventory.OnInventoryClosed += new OnInventoryClosedDelegate(this.OnInventoryClosed);
            //this.inventory.OnInventoryOpened += new OnInventoryOpenedDelegate(this.OnInvOpened);
            // this.inventory.SlotModified += new Action<int>(this.OnSlotModified);

            // this.inventory.Pos = this.Pos;
            //this.inventory[0].MaxSlotStackSize = 1;
            this.inventory.OnInventoryClosed += new OnInventoryClosedDelegate(this.OnInventoryClosed);
            this.inventory.OnInventoryOpened += new OnInventoryOpenedDelegate(this.OnInvOpened);

        }
        private void OnInventoryClosed(IPlayer player)
        {
            this.renameGui?.Dispose();
            this.renameGui = (GuiDialogJewelerSet)null;
        }
        protected virtual void OnInvOpened(IPlayer player) => this.inventory.PutLocked = false;
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
                this.sapi = api as ICoreServerAPI;
            else
                this.capi = api as ICoreClientAPI;
            this.inventory.LateInitialize("canjewelerset-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            //this.inventory.LateInitialize("canrenamecollectible-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);         
            //this.mesh = new MeshData();
            //this.RegisterGameTickListener
            this.inventory.Pos = this.Pos;
            foreach (var it in this.inventory)
            {
                this.inventory[0].MaxSlotStackSize = 1;
                //this.inventory[0].CanHold += canHoldSocket;
            }
            //this.UpdateMesh(0);
            this.MarkDirty(true);
        }
        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                Dictionary<string, CompositeTexture> dictionary = this.nowTesselatingObj is Vintagestory.API.Common.Item nowTesselatingObj ? nowTesselatingObj.Textures : (Dictionary<string, CompositeTexture>)(this.nowTesselatingObj as Block).Textures;
                AssetLocation texturePath = (AssetLocation)null;
                CompositeTexture compositeTexture;
                if (dictionary.TryGetValue(textureCode, out compositeTexture))
                    texturePath = compositeTexture.Baked.BakedName;
                if ((object)texturePath == null && dictionary.TryGetValue("all", out compositeTexture))
                    texturePath = compositeTexture.Baked.BakedName;
                if ((object)texturePath == null)
                    this.nowTesselatingShape?.Textures.TryGetValue(textureCode, out texturePath);
                if ((object)texturePath == null)
                    texturePath = new AssetLocation(textureCode);
                return this.getOrCreateTexPos(texturePath);
            }
        }
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            this.inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            if (this.Api == null)
                return;
            this.inventory.AfterBlocksLoaded(this.Api.World);
            if (this.Api.Side != EnumAppSide.Client)
                return;
            //this.UpdateMesh(0);
        }
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute tree1 = (ITreeAttribute)new TreeAttribute();
            this.inventory.ToTreeAttributes(tree1);
            tree["inventory"] = (IAttribute)tree1;
        }
        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            IClientWorldAccessor clientWorldAccessor = (IClientWorldAccessor)Api.World;
            if (packetid == 5000)
            {
                if (renameGui != null)
                {
                    if (renameGui?.IsOpened() ?? false)
                    {
                        renameGui.TryClose();
                    }

                    renameGui?.Dispose();
                    renameGui = null;
                    return;
                }

                TreeAttribute treeAttribute = new TreeAttribute();
                string dialogTitle;
                int cols;
                using (MemoryStream input = new MemoryStream(data))
                {
                    BinaryReader binaryReader = new BinaryReader(input);
                    binaryReader.ReadString();
                    dialogTitle = binaryReader.ReadString();
                    cols = binaryReader.ReadByte();
                    treeAttribute.FromBytes(binaryReader);
                }

                Inventory.FromTreeAttributes(treeAttribute);
                Inventory.ResolveBlocksOrItems();
                renameGui = new GuiDialogJewelerSet(dialogTitle, Inventory, Pos, capi);
                /*Block block = Api.World.BlockAccessor.GetBlock(Pos);
                string text = block.Attributes?["openSound"]?.AsString();
                string text2 = block.Attributes?["closeSound"]?.AsString();
                AssetLocation assetLocation = (text == null) ? null : AssetLocation.Create(text, block.Code.Domain);
                AssetLocation assetLocation2 = (text2 == null) ? null : AssetLocation.Create(text2, block.Code.Domain);
                invDialog.OpenSound = (assetLocation ?? OpenSound);
                invDialog.CloseSound = (assetLocation2 ?? CloseSound);*/
                renameGui.TryOpen();
            }

            if (packetid == 1001)
            {
                clientWorldAccessor.Player.InventoryManager.CloseInventory(Inventory);
                if (renameGui?.IsOpened() ?? false)
                {
                    renameGui?.TryClose();
                }

                renameGui?.Dispose();
                renameGui = null;
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
                    this.capi.BlockTextureAtlas.InsertTextureCached(texturePath, (IBitmap)bitmap, out int _, out texPos);
                }
                else
                    this.capi.World.Logger.Warning("For render in block " + this.Block.Code?.ToString() + ", item {0} defined texture {1}, not no such texture found.", (object)this.nowTesselatingObj.Code, (object)texturePath);
            }
            return texPos;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (this.Api.World is IServerWorldAccessor)
            {
                if (byPlayer.Entity.ServerControls.CtrlKey)
                {
                    if (byPlayer.InventoryManager.ActiveHotbarSlot.Empty)
                    {
                        this.inventory[0].TryPutInto(byPlayer.Entity.World, byPlayer.InventoryManager.ActiveHotbarSlot, 1);
                    }
                    else
                    {
                        byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(byPlayer.Entity.World, this.inventory[0], 1);
                    }
                    return true;
                }
                byte[] array;
                using (MemoryStream output = new MemoryStream())
                {
                    BinaryWriter stream = new BinaryWriter((Stream)output);
                    stream.Write("BlockEntityJewelerSet");
                    stream.Write("123");
                    stream.Write((byte)4);
                    TreeAttribute tree = new TreeAttribute();
                    this.inventory.ToTreeAttributes((ITreeAttribute)tree);
                    tree.ToBytes(stream);
                    array = output.ToArray();
                }
         ((ICoreServerAPI)this.Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, this.Pos.X, this.Pos.Y, this.Pos.Z, 5000, array);
                byPlayer.InventoryManager.OpenInventory((IInventory)this.inventory);
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid < 1000)
            {
                this.inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos).MarkModified();
            }
            else
            {
                if (packetid == 1001 && player.InventoryManager != null)
                {
                    player.InventoryManager.CloseInventory((IInventory)this.inventory);
                }
                if (packetid == 1004)
                {
                    EncrustableFunctions.TryToAddSocket(this.inventory);
                }
                else if (packetid == 1005)
                {
                    //check target item is here and has place
                    //for 1-3 slots
                    //check if null try to place if slotN exists at target
                    //set null if taken
                    EncrustableFunctions.TryToEncrustGemsIntoSockets(this.inventory);
                }

            }
        }
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            return base.OnTesselation(mesher, tessThreadTesselator);
        }
    }
}
