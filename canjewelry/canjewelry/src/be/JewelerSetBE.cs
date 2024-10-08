using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using canjewelry.src.be;
using canjewelry.src.CB;
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
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;
        protected CollectibleObject nowTesselatingObj;
        protected Shape nowTesselatingShape;
        GuiDialogJewelerSet renameGui;
        BlockFacing facing;
        public virtual string AttributeTransformCode => "groundTransform";
        public virtual string ClassCode
        {
            get
            {
                return this.InventoryClassName;
            }
        }
        protected Dictionary<string, MeshData> MeshCache
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate<Dictionary<string, MeshData>>(this.Api, "meshesJewelrySet-" + this.ClassCode, () => new Dictionary<string, MeshData>());
            }
        }
        public override InventoryBase Inventory => this.inventory;
        public override string InventoryClassName => "canjewelerset";
        public Size2i AtlasSize => this.capi.BlockTextureAtlas.Size;
        public JewelerSetBE()
        {
            this.inventory = new InventoryJewelerSet((string)null, (ICoreAPI)null);
            this.inventory.Pos = this.Pos;

            this.inventory.OnInventoryClosed += new OnInventoryClosedDelegate(this.OnInventoryClosed);
            this.inventory.OnInventoryOpened += new OnInventoryOpenedDelegate(this.OnInvOpened);        
        }
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
                this.sapi = api as ICoreServerAPI;
            else
                this.capi = api as ICoreClientAPI;
            this.inventory.LateInitialize("canjewelerset-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.inventory.Pos = this.Pos;
            if (this.capi != null)
            {
                this.inventory.SlotModified += (int slotId) =>
                {
                    if (slotId == 0)
                    {
                        this.UpdateMeshes();
                    }                   
                };
                this.UpdateMeshes();
                Block block = (this.Api as ICoreClientAPI).World.BlockAccessor.GetBlock(this.Pos);
                this.facing = BlockFacing.FromCode(block.LastCodePart());
            }
            if(this.Api.Side == EnumAppSide.Server)
            {
                this.inventory.SlotModified += (int slotId) =>
                {
                    if (slotId == 1)
                    {
                        ItemStack gemStack = this.inventory[slotId].Itemstack;
                        if (gemStack != null)
                        {
                            if(!gemStack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE))
                            {
                                Random r = new Random();
                                string selectedCutting = canjewelry.config.CuttingAttributesDict.Keys.ToArray().Shuffle(r).FirstOrDefault("round");
                                ITreeAttribute tree = new TreeAttribute();
                                //gemStack.Attributes.SetString(CANJWConstants.CUTTING_TYPE, selectedCutting);
                                tree.SetString(CANJWConstants.CUTTING_TYPE, selectedCutting);
                                gemStack.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
                                BlockEntityGemCuttingTable.ApplyCuttingBuff(gemStack);
                                this.inventory[slotId].MarkDirty();
                            }
                        }
                    }
                };
            }
            foreach (var it in this.inventory)
            {
                this.inventory[0].MaxSlotStackSize = 1;
            }

            this.inventory.SlotModified += (int num) => {
                if (this.inventory.Api.Side == EnumAppSide.Client)
                {
                    this.renameGui.SetupDialog();
                }
            };

            this.MarkDirty(true);
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
                    TreeAttribute tree = new TreeAttribute();
                    int socketNumber;
                    int selectedSlotNum;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (BinaryReader reader = new BinaryReader(ms))
                        {
                            tree.FromBytes(reader);
                            //in which slot in item we want socket to be added
                            socketNumber = tree.GetInt("selectedSocketSlot");
                            //which slot of the inventory contains socket item to be added
                            selectedSlotNum = tree.GetInt("selectedSlotNum");
                        }
                    }
                    EncrustableCB.TryAddSocket(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber);

                    //EncrustableFunctions.TryToAddSocket(this.inventory);
                }
                else if (packetid == 1005)
                {
                    //check target item is here and has place
                    //for 1-3 slots
                    //check if null try to place if slotN exists at target
                    //set null if taken
                    TreeAttribute tree = new TreeAttribute();
                    int socketNumber;
                    int selectedSlotNum;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (BinaryReader reader = new BinaryReader(ms))
                        {
                            tree.FromBytes(reader);
                            //in which slot in item we want socket to be added
                            socketNumber = tree.GetInt("selectedSocketSlot");
                            //which slot of the inventory contains socket item to be added
                            selectedSlotNum = tree.GetInt("selectedSlotNum");
                        }
                    }

                    EncrustableCB.TryToEncrustGemsIntoSockets(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber);

                    //EncrustableFunctions.TryToEncrustGemsIntoSockets(this.inventory);
                }

            }
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
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            for (int index = 0; index < 1; index++)
            {
                if (!inventory[0].Empty)
                {
                    mesher.AddMeshData(this.getMesh(inventory[0].Itemstack));
                }
            }
            /*var shape = new Shape
            {
                // Создание шейпа куба
                Elements = new[]
                {
                    new ShapeElement
                    {
                        From = new double[]{0, 0, 0},
                        To = new double[]{1, 12, 1},
                        FacesResolved = new ShapeElementFace[]
                        {
                            new ShapeElementFace { Texture = "top" },
                            new ShapeElementFace { Texture = "top" },
                            new ShapeElementFace { Texture = "top" },
                            new ShapeElementFace { Texture = "top" },
                            new ShapeElementFace { Texture = "top" },
                            new ShapeElementFace { Texture = "top" }
                        }
                    }
                }
            };

            // Применение шейпа к блоку
            //"jewelgrinder-top", Shape.TryGet(this.Api, "canjewelry:shapes/block/jewelgrinder-top.json"), out modeldata, (ITexPositionSource)this, new Vec3f(0.0f, block.Shape.rotateY, 0.0f)
            MeshData modeldata;
            canjewelry.capi.Tesselator.TesselateShape("block", shape, out modeldata, this);
            mesher.AddMeshData(modeldata);*/

            return false;
        }
        public void UpdateMeshes()
        {
            if (this.inventory == null)
            {
                return;
            }
            for (int slotid = 0; slotid < 1; slotid++)
            {
                if (!this.inventory[slotid].Empty)
                {
                    this.getOrCreateMesh(this.inventory[slotid].Itemstack, slotid);
                }
            }
            this.MarkDirty(true);
        }
        protected virtual string getMeshCacheKey(ItemStack stack)
        {
            IContainedMeshSource meshSource = stack.Collectible as IContainedMeshSource;
            if (meshSource != null)
            {
                return meshSource.GetMeshCacheKey(stack);
            }
            return stack.Collectible.Code.ToString();
        }
        protected MeshData getMesh(ItemStack stack)
        {
            string key = this.getMeshCacheKey(stack);
            MeshData meshdata;
            this.MeshCache.TryGetValue(key + this.facing, out meshdata);
            return meshdata;
        }
        protected virtual MeshData getOrCreateMesh(ItemStack stack, int index)
        {
            //this.MeshCache.Clear();
            //here
            MeshData mesh = this.getMesh(stack);
            //this.MeshCache.Clear();
            if (mesh != null)
            {               
                return mesh;
            }
            IContainedMeshSource meshSource = stack.Collectible as IContainedMeshSource;
            if (meshSource != null)
            {
                mesh = meshSource.GenMesh(stack, this.capi.BlockTextureAtlas, this.Pos);
            }
            if (mesh == null)
            {
                ICoreClientAPI capi = this.Api as ICoreClientAPI;
                if (stack.Class == EnumItemClass.Block)
                {
                    mesh = capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                }
                else
                {
                    this.nowTesselatingObj = stack.Collectible;
                    this.nowTesselatingShape = null;
                    CompositeShape shape = stack.Item.Shape;
                    if (((shape != null) ? shape.Base : null) != null)
                    {
                        this.nowTesselatingShape = capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                    }
                    capi.Tesselator.TesselateItem(stack.Item, out mesh, this);
                    mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.BlendNoCull);
                }
            }
            mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 0.5f, 0.5f, 0.5f);
            
            if(stack.Item is CANItemSimpleNecklace)
            {
                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 1.25f, 1.25f, 1.25f);
                mesh.Translate(1f/16, 2f / 16, 1f / 16);
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, ((float)Math.PI / 2), -((float)Math.PI / 6));
                mesh.Translate(-3f/16, -1f/16,3f/16);
            }
            else if(stack.Item is CANItemTiara)
            {
                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 1.6f, 1.6f, 1.6f);
                //mesh.Translate(1f / 16, 2f / 16, 1f / 16);
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, ((float)Math.PI / 4), -((float)Math.PI / 16));
                mesh.Translate(-1f / 16, -9f / 16, 3f / 16);
            }
            else if (stack.Item is CANItemRottenKingMask)
            {
                mesh.Translate(0, 13f / 16, 0);
                //mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 1.6f, 1.6f, 1.6f);
                //mesh.Translate(1f / 16, 2f / 16, 1f / 16);
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, ((float)Math.PI / 4), -((float)Math.PI / 16));
                //mesh.Translate(-1f / 16, -9f / 16, 3f / 16);
            }
            else if (stack.Item is CANItemCoronet)
            {
                mesh.Translate(0, 10f / 16, 0);
                //mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 1.6f, 1.6f, 1.6f);
                //mesh.Translate(1f / 16, 2f / 16, 1f / 16);
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, ((float)Math.PI / 4), -((float)Math.PI / 16));
                //mesh.Translate(-1f / 16, -9f / 16, 3f / 16);
            }
            else if(stack.Item != null && stack.Item.StorageFlags == EnumItemStorageFlags.Outfit)
            {
               
                if(stack.Collectible.Code.Path.Contains("-head-"))
                {
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, ((float)Math.PI / 2), 0f);
                    mesh.Translate(-3f/16, 0, 0f/16);
                    
                }
                else
                {
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0.0f, ((float)Math.PI / 2), 0f);
                    mesh.Translate(0, 12f / 16, 0);
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), ((float)Math.PI / 2), 0.0f, 0.0f);
                    mesh.Translate(0, 9f / 16, -1);
                }
            }
            else
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0.0f, ((float)Math.PI / 2), 0f);
                mesh.Translate(0, 13f / 16, 0);
            }



            if (this.facing == BlockFacing.SOUTH)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, -2.35f, 0f);
            }
            else if (this.facing == BlockFacing.NORTH)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, 1.0f, 0f);
            }
            else if (this.facing == BlockFacing.EAST)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, -1.0f, 0f);
            }
            else
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, 2.35f, 0f);
            }

            string key = this.getMeshCacheKey(stack);
            this.MeshCache[key + this.facing] = mesh;
            return mesh;
        }
        private void OnInventoryClosed(IPlayer player)
        {
            this.renameGui?.Dispose();
            this.renameGui = (GuiDialogJewelerSet)null;
        }
        protected virtual void OnInvOpened(IPlayer player) => this.inventory.PutLocked = false;    
        public TextureAtlasPosition this[string textureCode]
        {
            get
            {

                Dictionary<string, CompositeTexture> dictionary;
                if (this.nowTesselatingObj != null) {
                     dictionary   = this.nowTesselatingObj is Vintagestory.API.Common.Item nowTesselatingObj ? nowTesselatingObj.Textures : (Dictionary<string, CompositeTexture>)(this.nowTesselatingObj as Block).Textures;
                }
                else
                {
                    dictionary = new Dictionary<string, CompositeTexture>();
                    foreach(var it in (this.Block as Block).Textures)
                    {
                        dictionary[it.Key] = it.Value;
                    }
                }
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
    }
}
