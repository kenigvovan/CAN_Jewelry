using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
        bool canItemContainThisGem(string gemType, ItemStack targetItemStack)
        {
            if (Config.Current.buffNameToPossibleItem.Val.TryGetValue(gemType, out var hashSetClasses))
            {
                foreach (var it in hashSetClasses)
                {
                    if (targetItemStack.Item.Code.Path.Contains(it))
                    {
                        return true;
                    }

                }
            }
            return false;
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
                    this.inventory.TakeLocked = true;
                    if (this.inventory[0].Itemstack != null && this.inventory[0].Itemstack.Collectible.Attributes.KeyExists("canhavesocketsnumber"))
                    {
                        //already has itree -> has socket alteast 1
                        if (this.inventory[0].Itemstack.Attributes.HasAttribute("canencrusted"))
                        {
                            var tree = this.inventory[0].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                            if (tree.GetInt("socketsnumber") >= this.inventory[0].Itemstack.Collectible.Attributes["canhavesocketsnumber"].AsInt())
                            {
                                this.inventory.TakeLocked = false;
                                return;
                            }
                            else
                            {
                                if (!(this.inventory[1].Itemstack != null && this.inventory[1].Itemstack.Collectible.Attributes.KeyExists("levelOfSocket")))
                                {
                                    this.inventory.TakeLocked = false;
                                    return;
                                }

                                tree.SetInt("socketsnumber", tree.GetInt("socketsnumber") + 1);
                                ITreeAttribute socketSlotTree = new TreeAttribute();
                                socketSlotTree.SetInt("size", 0);
                                socketSlotTree.SetString("gemtype", "");
                                socketSlotTree.SetInt("sockettype", this.inventory[1].Itemstack.Collectible.Attributes["levelOfSocket"].AsInt());
                                this.inventory[1].Itemstack = null;
                                this.inventory[1].MarkDirty();
                                this.inventory[0].MarkDirty();
                                tree["slot" + (tree.GetInt("socketsnumber") - 1).ToString()] = socketSlotTree;
                            }
                        }
                        else
                        {
                            if (this.inventory[0].Itemstack.Collectible.Attributes["canhavesocketsnumber"].AsInt() < 1)
                            {
                                this.inventory.TakeLocked = false;
                                return;
                            }
                            if (!(this.inventory[1].Itemstack != null && this.inventory[1].Itemstack.Collectible.Attributes.KeyExists("levelOfSocket")))
                            {
                                this.inventory.TakeLocked = false;
                                return;
                            }

                            ITreeAttribute socketSlotTree = new TreeAttribute();

                            socketSlotTree.SetInt("size", 0);
                            socketSlotTree.SetString("gemtype", "");
                            socketSlotTree.SetInt("sockettype", this.inventory[1].Itemstack.Collectible.Attributes["levelOfSocket"].AsInt());

                            ITreeAttribute socketEncrusted = new TreeAttribute();
                            socketEncrusted.SetInt("socketsnumber", 1);
                            socketEncrusted["slot" + 0] = socketSlotTree;
                            this.inventory[1].TakeOut(1);
                            this.inventory[1].MarkDirty();
                            this.inventory[0].MarkDirty();
                            this.inventory[0].Itemstack.Attributes["canencrusted"] = socketEncrusted;
                        }
                    }
                    this.inventory.TakeLocked = false;
                    //check left item that can be encrusted
                    //if it at all can be ecnrusted
                    //can add more sockets to it
                    //if ok we take socket form slot
                    //create itree for left item and add info about socket
                }
                else if (packetid == 1005)
                {
                    //check target item is here and has place
                    //for 1-3 slots
                    //check if null try to place if slotN exists at target
                    //set null if taken
                    this.inventory.TakeLocked = true;
                    if (this.inventory[0].Itemstack != null && this.inventory[0].Itemstack.Attributes.HasAttribute("canencrusted"))
                    {
                        var tree = this.inventory[0].Itemstack.Attributes.GetTreeAttribute("canencrusted");
                        for (int i = 1; i < tree.GetInt("socketsnumber") + 1; i++)
                        {
                            ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + (i - 1).ToString());
                            if (this.inventory[i].Itemstack != null && this.inventory[i].Itemstack.Collectible.Attributes.KeyExists("canGemType"))
                            {
                                if (treeSocket.GetInt("sockettype") < this.inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt())
                                {
                                    this.inventory.TakeLocked = false;
                                    return;
                                }
                                if (!this.canItemContainThisGem(this.inventory[i].Itemstack.Collectible.Code.Path.Split('-').Last(), this.inventory[0].Itemstack))
                                {
                                    this.inventory.TakeLocked = false;
                                    return;
                                }
                                treeSocket.SetInt("size", this.inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt());
                                treeSocket.SetString("gemtype", this.inventory[i].Itemstack.Collectible.Code.Path.Split('-').Last());
                                treeSocket.SetString("attributeBuff", this.inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString());

                                var g = this.inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt();
                                var cb = Config.Current.gems_buffs.Val[this.inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString()][this.inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()];

                                var c = this.inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"];
                                var b = Config.Current.gems_buffs.Val[this.inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString()];
                                treeSocket.SetFloat("attributeBuffValue", Config.Current.gems_buffs.Val
                                    [this.inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString()][this.inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
                                this.inventory[i].TakeOut(1);
                                this.inventory[i].MarkDirty();
                                this.inventory[0].MarkDirty();
                                //this.inventory[i].Itemstack
                                // string tmp = "";
                                //treeSocket.SetString("size", this.inventory[i].Itemstack.Attributes)
                            }
                        }
                    }
                    this.inventory.TakeLocked = false;
                }

            }
        }
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            
            var f = BlockFacing.EAST;
            return base.OnTesselation(mesher, tessThreadTesselator);
        }
    }
}
