using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.blocks
{
    public class CANBlockPan : Block, ITexPositionSource
    {
        public Size2i AtlasSize { get; set; }

        // Token: 0x06001ADF RID: 6879 RVA: 0x000ED068 File Offset: 0x000EB268
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.dropsBySourceMat = this.Attributes["panningDrops"].AsObject<Dictionary<string, PanningDrop[]>>(null);
            foreach (PanningDrop[] drops in this.dropsBySourceMat.Values)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    if (!drops[i].Code.Path.Contains("{rocktype}"))
                    {
                        drops[i].Resolve(api.World, "panningdrop", true);
                    }
                }
            }
            if (api.Side != EnumAppSide.Client)
            {
                return;
            }
            ICoreAPI api2 = api;
            //InteractionMatcherDelegate<>9__2;
            this.interactions = ObjectCacheUtil.GetOrCreate<WorldInteraction[]>(api, "panInteractions", delegate
            {
                List<ItemStack> stacks = new List<ItemStack>();
                foreach (Block block in api.World.Blocks)
                {
                    if (!(block.Code == null) && !block.IsMissing && block.CreativeInventoryTabs != null && block.CreativeInventoryTabs.Length != 0 && this.IsPannableMaterial(block))
                    {
                        stacks.Add(new ItemStack(block, 1));
                    }
                }
                ItemStack[] stacksArray = stacks.ToArray();
                WorldInteraction[] array = new WorldInteraction[2];
                array[0] = new WorldInteraction
                {
                    ActionLangCode = "heldhelp-addmaterialtopan",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = stacks.ToArray(),
                    GetMatchingStacks = delegate (WorldInteraction wi, BlockSelection bs, EntitySelection es)
                    {
                        ItemStack stack = (api as ICoreClientAPI).World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
                        if (this.GetBlockMaterialCode(stack) != null)
                        {
                            return null;
                        }
                        return stacksArray;
                    }
                };
                int num = 1;
                WorldInteraction worldInteraction = new WorldInteraction();
                worldInteraction.ActionLangCode = "heldhelp-pan";
                worldInteraction.MouseButton = EnumMouseButton.Right;
                InteractionMatcherDelegate shouldApply;
               /* if ((shouldApply = <> 9__2) == null)
                {
                    shouldApply = (<> 9__2 = delegate (WorldInteraction wi, BlockSelection bs, EntitySelection es)
                    {
                        ItemStack stack = (api as ICoreClientAPI).World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
                        return this.GetBlockMaterialCode(stack) != null;
                    });
                }*/
                /*
                 public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) => new WorldInteraction[3]
                {
                  new WorldInteraction()
                  {
                    ActionLangCode = "heldhelp-fill",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (InteractionMatcherDelegate) ((wi, bs, es) => (double) this.GetCurrentLitres(this.api.World, inSlot.Itemstack) < (double)   
                    this.CapacityLitres)
                  },
                 
                 
                 */
                //worldInteraction.ShouldApply = shouldApply;
                array[num] = worldInteraction;
                return array;
            });
        }

        // Token: 0x06001AE0 RID: 6880 RVA: 0x000ED170 File Offset: 0x000EB370
        private ItemStack Resolve(EnumItemClass type, string code)
        {
            if (type == EnumItemClass.Block)
            {
                Block block = this.api.World.GetBlock(new AssetLocation(code));
                if (block == null)
                {
                    this.api.World.Logger.Error("Failed resolving panning block drop with code {0}. Will skip.", new object[]
                    {
                        code
                    });
                    return null;
                }
                return new ItemStack(block, 1);
            }
            else
            {
                Item item = this.api.World.GetItem(new AssetLocation(code));
                if (item == null)
                {
                    this.api.World.Logger.Error("Failed resolving panning item drop with code {0}. Will skip.", new object[]
                    {
                        code
                    });
                    return null;
                }
                return new ItemStack(item, 1);
            }
        }

        // Token: 0x1700032E RID: 814
        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (textureCode == "material")
                {
                    return this.matTexPosition;
                }
                return this.ownTextureSource[textureCode];
            }
        }

        // Token: 0x06001AE2 RID: 6882 RVA: 0x000ED231 File Offset: 0x000EB431
        public string GetBlockMaterialCode(ItemStack stack)
        {
            if (stack == null)
            {
                return null;
            }
            ITreeAttribute attributes = stack.Attributes;
            if (attributes == null)
            {
                return null;
            }
            return attributes.GetString("materialBlockCode", null);
        }

        // Token: 0x06001AE3 RID: 6883 RVA: 0x000ED24F File Offset: 0x000EB44F
        public void SetMaterial(ItemSlot slot, Block block)
        {
            slot.Itemstack.Attributes.SetString("materialBlockCode", block.Code.ToShortString());
        }

        // Token: 0x06001AE4 RID: 6884 RVA: 0x000ED271 File Offset: 0x000EB471
        public void RemoveMaterial(ItemSlot slot)
        {
            slot.Itemstack.Attributes.RemoveAttribute("materialBlockCode");
        }

        // Token: 0x06001AE5 RID: 6885 RVA: 0x000ED288 File Offset: 0x000EB488
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string blockMaterialCode = this.GetBlockMaterialCode(itemstack);
            if (blockMaterialCode == null)
            {
                return;
            }
            string key = "pan-filled-" + blockMaterialCode + target.ToString();
            renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate<MeshRef>(capi, key, delegate
            {
                AssetLocation shapeloc = new AssetLocation("shapes/block/wood/pan/filled.json");
                Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapeloc);
                Block block = capi.World.GetBlock(new AssetLocation(blockMaterialCode));
                this.AtlasSize = capi.BlockTextureAtlas.Size;
                this.matTexPosition = capi.BlockTextureAtlas.GetPosition(block, "up", false);
                this.ownTextureSource = capi.Tesselator.GetTextureSource(this, 0, false);
                MeshData meshdata;
                capi.Tesselator.TesselateShape("filledpan", shape, out meshdata, this, null, 0, 0, 0, null, null);
                return capi.Render.UploadMesh(meshdata);
            });
        }

        // Token: 0x06001AE6 RID: 6886 RVA: 0x000ED300 File Offset: 0x000EB500
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent)
            {
                return;
            }
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer byPlayer = (entityPlayer != null) ? entityPlayer.Player : null;
            if (byPlayer == null)
            {
                return;
            }
            if (blockSel != null && !byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }
            string blockMatCode = this.GetBlockMaterialCode(slot.Itemstack);
            if (!byEntity.FeetInLiquid && this.api.Side == EnumAppSide.Client && blockMatCode != null)
            {
                (this.api as ICoreClientAPI).TriggerIngameError(this, "notinwater", Lang.Get("ingameerror-panning-notinwater", Array.Empty<object>()));
                return;
            }
            if (blockMatCode == null && blockSel != null)
            {
                this.TryTakeMaterial(slot, byEntity, blockSel.Position);
                slot.Itemstack.TempAttributes.SetBool("canpan", false);
                return;
            }
            if (blockMatCode != null)
            {
                slot.Itemstack.TempAttributes.SetBool("canpan", true);
            }
        }

        // Token: 0x06001AE7 RID: 6887 RVA: 0x000ED3DC File Offset: 0x000EB5DC
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if ((byEntity.Controls.TriesToMove || byEntity.Controls.Jump) && !byEntity.Controls.Sneak)
            {
                return false;
            }
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer byPlayer = (entityPlayer != null) ? entityPlayer.Player : null;
            if (byPlayer == null)
            {
                return false;
            }
            if (blockSel != null && !byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return false;
            }
            string blockMaterialCode = this.GetBlockMaterialCode(slot.Itemstack);
            if (blockMaterialCode == null || !slot.Itemstack.TempAttributes.GetBool("canpan", false))
            {
                return false;
            }
            Vec3d pos = byEntity.Pos.AheadCopy(0.4000000059604645).XYZ;
            pos.Y += byEntity.LocalEyePos.Y - 0.4000000059604645;
            if (secondsUsed > 0.5f && this.api.World.Rand.NextDouble() > 0.5)
            {
                Block block = this.api.World.GetBlock(new AssetLocation(blockMaterialCode));
                Vec3d particlePos = pos.Clone();
                particlePos.X += (double)(GameMath.Sin(-secondsUsed * 20f) / 5f);
                particlePos.Z += (double)(GameMath.Cos(-secondsUsed * 20f) / 5f);
                particlePos.Y -= 0.07000000029802322;
                IWorldAccessor world = byEntity.World;
                Vec3d pos2 = particlePos;
                ItemStack item = new ItemStack(block, 1);
                float radius = 0.3f;
                int quantity = (int)(1.5f + (float)this.api.World.Rand.NextDouble());
                float scale = 0.3f + (float)this.api.World.Rand.NextDouble() / 6f;
                EntityPlayer entityPlayer2 = byEntity as EntityPlayer;
                world.SpawnCubeParticles(pos2, item, radius, quantity, scale, (entityPlayer2 != null) ? entityPlayer2.Player : null, null);
            }
            if (byEntity.World is IClientWorldAccessor)
            {
                ModelTransform tf = new ModelTransform();
                tf.EnsureDefaultValues();
                tf.Origin.Set(0f, 0f, 0f);
                if (secondsUsed > 0.5f)
                {
                    tf.Translation.X = Math.Min(0.25f, GameMath.Cos(10f * secondsUsed) / 4f);
                    tf.Translation.Y = Math.Min(0.15f, GameMath.Sin(10f * secondsUsed) / 6.666f);
                    if (this.sound == null)
                    {
                        this.sound = (this.api as ICoreClientAPI).World.LoadSound(new SoundParams
                        {
                            Location = new AssetLocation("sounds/player/panning.ogg"),
                            ShouldLoop = false,
                            RelativePosition = true,
                            Position = new Vec3f(),
                            DisposeOnFinish = true,
                            Volume = 0.5f,
                            Range = 8f
                        });
                        this.sound.Start();
                    }
                }
                tf.Translation.X -= Math.Min(1.6f, secondsUsed * 4f * 1.57f);
                tf.Translation.Y -= Math.Min(0.1f, secondsUsed * 2f);
                tf.Translation.Z -= Math.Min(1f, secondsUsed * 180f);
                tf.Scale = 1f + Math.Min(0.6f, 2f * secondsUsed);
                byEntity.Controls.UsingHeldItemTransformAfter = tf;
                return secondsUsed <= 4f;
            }
            return true;
        }

        // Token: 0x06001AE8 RID: 6888 RVA: 0x000ED77D File Offset: 0x000EB97D
        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            if (cancelReason == EnumItemUseCancelReason.ReleasedMouse)
            {
                return false;
            }
            if (this.api.Side == EnumAppSide.Client)
            {
                ILoadedSound loadedSound = this.sound;
                if (loadedSound != null)
                {
                    loadedSound.Stop();
                }
                this.sound = null;
            }
            return true;
        }

        // Token: 0x06001AE9 RID: 6889 RVA: 0x000ED7AC File Offset: 0x000EB9AC
        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            ILoadedSound loadedSound = this.sound;
            if (loadedSound != null)
            {
                loadedSound.Stop();
            }
            this.sound = null;
            if (secondsUsed >= 3.4f)
            {
                string code = this.GetBlockMaterialCode(slot.Itemstack);
                if (this.api.Side == EnumAppSide.Server && code != null)
                {
                    this.CreateDrop(byEntity, code);
                }
                this.RemoveMaterial(slot);
                slot.MarkDirty();
                EntityBehaviorHunger behavior = byEntity.GetBehavior<EntityBehaviorHunger>();
                if (behavior == null)
                {
                    return;
                }
                behavior.ConsumeSaturation(4f);
            }
        }

        // Token: 0x06001AEA RID: 6890 RVA: 0x000ED824 File Offset: 0x000EBA24
        private void CreateDrop(EntityAgent byEntity, string fromBlockCode)
        {
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer player = (entityPlayer != null) ? entityPlayer.Player : null;
            PanningDrop[] drops = null;
            foreach (string val in this.dropsBySourceMat.Keys)
            {
                if (WildcardUtil.Match(val, fromBlockCode))
                {
                    drops = this.dropsBySourceMat[val];
                }
            }
            if (drops == null)
            {
                throw new InvalidOperationException("Coding error, no drops defined for source mat " + fromBlockCode);
            }
            Block block = this.api.World.GetBlock(new AssetLocation(fromBlockCode));
            string rocktype = (block != null) ? block.Variant["rock"] : null;
            drops.Shuffle(this.api.World.Rand);
            int i = 0;
            while (i < drops.Length)
            {
                PanningDrop drop = drops[i];
                double num = this.api.World.Rand.NextDouble();
                float extraMul = 1f;
                if (drop.DropModbyStat != null)
                {
                    extraMul = byEntity.Stats.GetBlended(drop.DropModbyStat);
                }
                float val2 = drop.Chance.nextFloat() * extraMul;
                ItemStack stack = drop.ResolvedItemstack;
                if (drops[i].Code.Path.Contains("{rocktype}"))
                {
                    stack = this.Resolve(drops[i].Type, drops[i].Code.Path.Replace("{rocktype}", rocktype));
                }
                if (num < (double)val2 && stack != null)
                {
                    stack = stack.Clone();
                    if (player == null || !player.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        this.api.World.SpawnItemEntity(stack, byEntity.ServerPos.XYZ, null);
                        return;
                    }
                    break;
                }
                else
                {
                    i++;
                }
            }
        }

        // Token: 0x06001AEB RID: 6891 RVA: 0x000ED9F8 File Offset: 0x000EBBF8
        public virtual bool IsPannableMaterial(Block block)
        {
            JsonObject attributes = block.Attributes;
            return attributes != null && attributes.IsTrue("pannable");
        }

        // Token: 0x06001AEC RID: 6892 RVA: 0x000EDA10 File Offset: 0x000EBC10
        protected virtual void TryTakeMaterial(ItemSlot slot, EntityAgent byEntity, BlockPos position)
        {
            Block block = this.api.World.BlockAccessor.GetBlock(position);
            if (this.IsPannableMaterial(block))
            {
                if (this.api.World.BlockAccessor.GetBlock(position.UpCopy(1)).Id != 0)
                {
                    if (this.api.Side == EnumAppSide.Client)
                    {
                        (this.api as ICoreClientAPI).TriggerIngameError(this, "noair", Lang.Get("ingameerror-panning-requireairabove", Array.Empty<object>()));
                    }
                    return;
                }
                string layer = block.Variant["layer"];
                if (layer != null)
                {
                    string baseCode = block.FirstCodePart(0) + "-" + block.FirstCodePart(1);
                    Block origblock = this.api.World.GetBlock(new AssetLocation(baseCode));
                    this.SetMaterial(slot, origblock);
                    if (layer == "1")
                    {
                        this.api.World.BlockAccessor.SetBlock(0, position);
                    }
                    else
                    {
                        AssetLocation code = block.CodeWithVariant("layer", (int.Parse(layer) - 1).ToString() ?? "");
                        Block reducedBlock = this.api.World.GetBlock(code);
                        this.api.World.BlockAccessor.SetBlock(reducedBlock.BlockId, position);
                    }
                    this.api.World.BlockAccessor.TriggerNeighbourBlockUpdate(position);
                }
                else
                {
                    string pannedBlock = block.Attributes["pannedBlock"].AsString(null);
                    Block reducedBlock2;
                    if (pannedBlock != null)
                    {
                        reducedBlock2 = this.api.World.GetBlock(AssetLocation.Create(pannedBlock, block.Code.Domain));
                    }
                    else
                    {
                        reducedBlock2 = this.api.World.GetBlock(block.CodeWithVariant("layer", "7"));
                    }
                    if (reducedBlock2 != null)
                    {
                        this.SetMaterial(slot, block);
                        this.api.World.BlockAccessor.SetBlock(reducedBlock2.BlockId, position);
                        this.api.World.BlockAccessor.TriggerNeighbourBlockUpdate(position);
                    }
                    else
                    {
                        this.api.Logger.Warning("Missing \"pannedBlock\" attribute for pannable block " + block.Code.ToShortString());
                    }
                }
                slot.MarkDirty();
            }
        }

        // Token: 0x06001AED RID: 6893 RVA: 0x000EDC4F File Offset: 0x000EBE4F
        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return this.interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }

        // Token: 0x04000EBC RID: 3772
        private ITexPositionSource ownTextureSource;

        // Token: 0x04000EBD RID: 3773
        private TextureAtlasPosition matTexPosition;

        // Token: 0x04000EBE RID: 3774
        private ILoadedSound sound;

        // Token: 0x04000EBF RID: 3775
        private Dictionary<string, PanningDrop[]> dropsBySourceMat;

        // Token: 0x04000EC0 RID: 3776
        private WorldInteraction[] interactions;
    }
}
