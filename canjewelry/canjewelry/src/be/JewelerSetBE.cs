using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using canjewelry.src.CB;
using canjewelry.src.gui;
using canjewelry.src.inventories;
using canjewelry.src.items;
using canjewelry.src.items.resource;
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
            this.inventory.SlotModified +=
            (int slotId) => {

                if (this.inventory.Slots[slotId].Empty)
                {
                    return;
                }
                ItemStack workStack = this.inventory.Slots[(int)slotId].Itemstack;
                if (workStack.Attributes == null)
                {
                    return;
                }
                if (workStack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE))
                {
                    return;
                }

                if (slotId > 0 && slotId < 4)
                {
                    if (workStack.Item is not CANCutGemItem)
                    {
                        return;
                    }
                    string newCuttingType = canjewelry.config.CuttingAttributesDict.Keys.ToArray().Shuffle(Config.rand).FirstOrDefault(CANJWConstants.CUTTING_ROUND);
                    ITreeAttribute tree = new TreeAttribute();
                    tree.SetString(CANJWConstants.CUTTING_TYPE, newCuttingType);
                    workStack.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
                    EncrustableCB.ApplyCuttingBuff(workStack);
                    return;
                }
                else if (slotId == 0)
                {
                    ITreeAttribute tree = workStack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                    if (tree == null)
                    {
                        return;
                    }
                    // Migrate legacy single-buff attributes to the new array form.
                    // Skip sockets that lack either attribute (partial data) but keep migrating the rest.
                    for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(workStack); i++)
                    {
                        ITreeAttribute socketSlot = tree.GetTreeAttribute("slot" + i);
                        if (socketSlot == null)
                        {
                            continue;
                        }
                        if (!socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE) || !socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF))
                        {
                            continue;
                        }
                        float currValue = socketSlot.GetFloat(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE);
                        string currBuffName = socketSlot.GetString(CANJWConstants.GEM_ATTRIBUTE_BUFF);

                        socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new string[] { currBuffName });
                        socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new float[] { currValue });
                        socketSlot.RemoveAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE);
                        socketSlot.RemoveAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF);
                    }


                }
            };
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
                                string selectedCutting = canjewelry.config.CuttingAttributesDict.Keys.ToArray().Shuffle(r).FirstOrDefault(CANJWConstants.CUTTING_ROUND);
                                ITreeAttribute tree = new TreeAttribute();
                                //gemStack.Attributes.SetString(CANJWConstants.CUTTING_TYPE, selectedCutting);
                                tree.SetString(CANJWConstants.CUTTING_TYPE, selectedCutting);
                                gemStack.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
                                EncrustableCB.ApplyCuttingBuff(gemStack);
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
                    // The dialog is not immediate mode: its composer has to be rebuilt for the
                    // change to show up.
                    renameGui?.SetupDialog();
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
                // The packet acts as a toggle: arriving while a dialog is up closes it.
                if (renameGui != null)
                {
                    CloseDialog();
                    return;
                }

                TreeAttribute treeAttribute = new TreeAttribute();
                string dialogTitle;
                using (MemoryStream input = new MemoryStream(data))
                {
                    BinaryReader binaryReader = new BinaryReader(input);
                    binaryReader.ReadString();
                    dialogTitle = binaryReader.ReadString();
                    binaryReader.ReadByte();   // cols (unused)
                    treeAttribute.FromBytes(binaryReader);
                }

                Inventory.FromTreeAttributes(treeAttribute);
                Inventory.ResolveBlocksOrItems();

                renameGui = new GuiDialogJewelerSet(dialogTitle, inventory, Pos, capi);
                renameGui.SetupDialog();
                renameGui.TryOpen();
            }

            if (packetid == 1001)
            {
                clientWorldAccessor.Player.InventoryManager.CloseInventory(Inventory);
                CloseDialog();
            }
        }

        // Clears the field before closing: TryClose() re-enters through
        // OnInventoryClosed(), which would otherwise dispose the dialog twice.
        private void CloseDialog()
        {
            GuiDialogJewelerSet dialog = renameGui;
            renameGui = null;

            if (dialog == null) return;

            dialog.TryClose();
            dialog.Dispose();
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
                    EncrustableCB.TryAddSocket(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber, player);

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

                    EncrustableCB.TryToEncrustGemsIntoSockets(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber, player);

                    //EncrustableFunctions.TryToEncrustGemsIntoSockets(this.inventory);
                }
                else if (packetid == 1007)
                {
                    // Inscribe: write a permanent text attribute on the jewelry in slot 0.
                    // Companion mods may gate this via OnCanInscribe (e.g. Jeweler.Inscriber perk).
                    // Server also validates length & charset; rejects silently on any failure.
                    if (inventory[0].Empty) return;
                    string text;
                    using (MemoryStream ms = new MemoryStream(data))
                    using (BinaryReader reader = new BinaryReader(ms))
                    {
                        text = reader.ReadString();
                    }
                    string sanitized = EncrustableCB.SanitizeInscription(text);
                    if (sanitized == null) return;
                    if (inventory[0].Itemstack.Attributes.HasAttribute(CANJWConstants.INSCRIPTION)) return;

                    var permitEv = new integration.CanInscribeEvent
                    {
                        Player = player,
                        Jewelry = inventory[0].Itemstack,
                    };
                    canjewelry.Instance?.FireCanInscribe(permitEv);
                    if (!permitEv.Allowed) return;

                    inventory[0].Itemstack.Attributes.SetString(CANJWConstants.INSCRIPTION, sanitized);
                    inventory[0].MarkDirty();
                }
                else if (packetid == 1006)
                {
                    // Extract: pulls a gem out of a specific socket. Output goes to the
                    // matching gem-input slot if empty, otherwise to the player.
                    TreeAttribute tree = new TreeAttribute();
                    int socketNumber;
                    int selectedSlotNum;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (BinaryReader reader = new BinaryReader(ms))
                        {
                            tree.FromBytes(reader);
                            socketNumber = tree.GetInt("selectedSocketSlot");
                            selectedSlotNum = tree.GetInt("selectedSlotNum");
                        }
                    }
                    EncrustableCB.TryExtractGem(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber, player);
                }
                else if (packetid == 1008)
                {
                    // Remove socket: pulls an (empty) socket back out and returns the socket item.
                    // Rejected server-side if a gem is still encrusted in that socket.
                    TreeAttribute tree = new TreeAttribute();
                    int socketNumber;
                    int selectedSlotNum;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (BinaryReader reader = new BinaryReader(ms))
                        {
                            tree.FromBytes(reader);
                            socketNumber = tree.GetInt("selectedSocketSlot");
                            selectedSlotNum = tree.GetInt("selectedSlotNum");
                        }
                    }
                    EncrustableCB.TryExtractSocket(this.inventory, inventory[0], inventory[selectedSlotNum], socketNumber, player);
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
         ((ICoreServerAPI)this.Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, this.Pos, 5000, array);
                byPlayer.InventoryManager.OpenInventory((IInventory)this.inventory);
            }
            return true;
        }
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            // Item-on-top mesh removed — the placed jewelry is now shown in the
            // dialog's 3D preview instead.
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
                    this.getOrCreateMesh(this.inventory[slotid], slotid);
                }
            }
            this.MarkDirty(true);
        }
        protected virtual string getMeshCacheKey(ItemSlot slot)
        {
            IContainedMeshSource meshSource = slot.Itemstack.Collectible as IContainedMeshSource;
            if (meshSource != null)
            {
                return meshSource.GetMeshCacheKey(slot);
            }
            return slot.Itemstack.Collectible.Code.ToString();
        }
        protected MeshData getMesh(ItemSlot slot)
        {
            string key = this.getMeshCacheKey(slot);
            MeshData meshdata;
            this.MeshCache.TryGetValue(key + this.facing, out meshdata);
            return meshdata;
        }

        private static readonly Vec3f MeshOrigin = new Vec3f(0.5f, 0.5f, 0.5f);

        // Per-weapon poses extracted from the OLD hardcoded chain. Same numbers,
        // structured as a dispatch table instead of a 9-branch if/else.
        // Match: Item.Code.Path.Contains(PathSubstring).
        private readonly struct WeaponPose
        {
            public readonly string PathSubstring;
            public readonly float Scale;
            public readonly float RotX, RotY, RotZ;
            public readonly float TrX, TrY, TrZ;

            public WeaponPose(string sub, float scale, float rx, float ry, float rz, float tx, float ty, float tz)
            { PathSubstring = sub; Scale = scale; RotX = rx; RotY = ry; RotZ = rz; TrX = tx; TrY = ty; TrZ = tz; }
        }

        private static readonly float PI = (float)Math.PI;
        private static readonly WeaponPose[] WeaponPoses = new[]
        {
            new WeaponPose("quarterstaff-plain-", 0.5f, 0,        PI * 0.6f, 0,           -0.2f, 10.5f/16, -0.2f),
            new WeaponPose("axe-long-plain-",     0.7f, 0,        PI * 0.6f, 0,           -0.2f, 12f/16,   -0.2f),
            new WeaponPose("sword-great-plain-",  0.6f, PI * 0.5f, 0,         PI * 0.45f, -0.2f, 8.5f/16,  -0.2f),
            new WeaponPose("sword-long-plain-",   0.6f, PI * 0.5f, 0,         PI * 0.45f, -0.2f, 8.5f/16,  -0.2f),
            new WeaponPose("sword-short-plain-",  0.6f, PI * 0.5f, 0,         PI * 0.45f, -0.2f, 8.5f/16,  -0.2f),
            new WeaponPose("javelin-plain-",      0.7f, PI * 0.5f, 0,         PI * 0.45f, -0.1f, 8.5f/16,   0.2f),
            new WeaponPose("pike-plain-",         0.5f, PI * 0.5f, 0,         PI * 0.45f, -0.1f, 8.5f/16,   0.6f),
            new WeaponPose("club-plain-",         0.6f, PI * 0.5f, 0,         PI * 0.45f, -0.2f, 8.5f/16,  -0.2f),
            new WeaponPose("halberd-plain-",      0.7f, PI * 0.5f, 0,         PI * 0.45f, -0.2f, 8.5f/16,   0.5f),
        };

        protected virtual MeshData getOrCreateMesh(ItemSlot slot, int index)
        {
            // While debugMode is on, skip the cache so iterating on poses (constants
            // in WeaponPoses or jewelry transforms) is visible after a single restart
            // without the previous mesh sticking around per-session.
            bool debugBypass = canjewelry.config?.debugMode == true;

            if (!debugBypass)
            {
                MeshData cached = this.getMesh(slot);
                if (cached != null) return cached;
            }

            MeshData mesh = BuildBaseMesh(slot);
            mesh.Scale(MeshOrigin, 0.5f, 0.5f, 0.5f);

            ApplyDisplayTransform(slot.Itemstack, mesh);
            ApplyFacingRotation(mesh);

            if (!debugBypass)
            {
                string newKey = this.getMeshCacheKey(slot);
                this.MeshCache[newKey + this.facing] = mesh;
            }
            return mesh;
        }

        private MeshData BuildBaseMesh(ItemSlot slot)
        {
            IContainedMeshSource meshSource = slot.Itemstack.Collectible as IContainedMeshSource;
            if (meshSource != null)
            {
                MeshData m = meshSource.GenMesh(slot, this.capi.BlockTextureAtlas, this.Pos);
                if (m != null) return m;
            }

            ICoreClientAPI cApi = this.Api as ICoreClientAPI;
            if (slot.Itemstack.Class == EnumItemClass.Block)
            {
                return cApi.TesselatorManager.GetDefaultBlockMesh(slot.Itemstack.Block).Clone();
            }

            this.nowTesselatingObj = slot.Itemstack.Collectible;
            this.nowTesselatingShape = slot.Itemstack.Item.Shape?.Base != null
                ? cApi.TesselatorManager.GetCachedShape(slot.Itemstack.Item.Shape.Base)
                : null;
            cApi.Tesselator.TesselateItem(slot.Itemstack.Item, out MeshData itemMesh, this);
            itemMesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.BlendNoCull);
            return itemMesh;
        }

        private void ApplyDisplayTransform(ItemStack stack, MeshData mesh)
        {
            // Jewelry poses used to be case 1 here, switching on four mod-owned item classes.
            // Dropped with the core/content split: the classes move to the content mod, and the
            // whole mesh path is dead anyway — OnTesselation adds no mesh, the placed item is
            // shown in the dialog's 3D preview instead.

            // 2. Known vanilla weapons via WeaponPoses table (poses preserved from OLD impl).
            if (TryApplyWeaponPose(stack, mesh)) return;

            // 3. Outfit items dispatch on -head- vs body.
            if (stack.Item != null && stack.Item.StorageFlags == EnumItemStorageFlags.Outfit)
            {
                if (stack.Collectible.Code.Path.Contains("-head-"))
                {
                    mesh.Rotate(MeshOrigin, 0, (float)Math.PI / 2, 0f);
                    mesh.Translate(-3f / 16, 0, 0f);
                }
                else
                {
                    mesh.Rotate(MeshOrigin, 0f, (float)Math.PI / 2, 0f);
                    mesh.Translate(0, 12f / 16, 0);
                    mesh.Rotate(MeshOrigin, (float)Math.PI / 2, 0f, 0f);
                    mesh.Translate(0, 9f / 16, -1);
                }
                return;
            }

            // 4. Default fallback for unknown items.
            mesh.Rotate(MeshOrigin, 0f, (float)Math.PI / 2, 0f);
            mesh.Translate(0, 13f / 16, 0);
        }

        private bool TryApplyWeaponPose(ItemStack stack, MeshData mesh)
        {
            string path = stack.Item?.Code?.Path;
            if (path == null) return false;

            foreach (var p in WeaponPoses)
            {
                if (path.Contains(p.PathSubstring))
                {
                    mesh.Scale(MeshOrigin, p.Scale, p.Scale, p.Scale);
                    mesh.Rotate(MeshOrigin, p.RotX, p.RotY, p.RotZ);
                    mesh.Translate(p.TrX, p.TrY, p.TrZ);
                    return true;
                }
            }
            return false;
        }

        private void ApplyFacingRotation(MeshData mesh)
        {
            if (this.facing == BlockFacing.SOUTH) mesh.Rotate(MeshOrigin, 0f, -2.35f, 0f);
            else if (this.facing == BlockFacing.NORTH) mesh.Rotate(MeshOrigin, 0f, 1.0f, 0f);
            else if (this.facing == BlockFacing.EAST) mesh.Rotate(MeshOrigin, 0f, -1.0f, 0f);
            else mesh.Rotate(MeshOrigin, 0f, 2.35f, 0f);
        }

        private void OnInventoryClosed(IPlayer player)
        {
            this.renameGui?.Dispose();
            this.renameGui = null;
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
