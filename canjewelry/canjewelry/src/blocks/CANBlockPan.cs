using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src.utils;
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
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Bootstrap admin's config from the canpan.json seed when missing. After bootstrap
            // canjewelry.config.panningDrops is the canonical admin-editable source.
            var fromAttr = this.Attributes?["panningDrops"]
                ?.AsObject<Dictionary<string, CANPanningDrop[]>>(null);

            // Blocks are loaded before StartClientSide runs, so a config that was not picked up in
            // StartPre must not be dereferenced blindly - that used to be a hard crash on world join.
            if (canjewelry.config == null)
            {
                api.Logger.Warning("[canjewelry] config was not loaded before block loading, falling back to defaults for the pan drop table.");
                canjewelry.config = new Config();
            }
            if (canjewelry.config.panningDrops == null || canjewelry.config.panningDrops.Count == 0)
            {
                if (fromAttr != null)
                {
                    canjewelry.config.panningDrops = new Dictionary<string, CANPanningDrop[]>(fromAttr);
                }
                else
                {
                    canjewelry.config.FillDefaultValues();
                }
                if (api.Side == EnumAppSide.Server)
                {
                    api.StoreModConfig(canjewelry.config, "canjewelry.json");
                }
            }

            // Working table = admin's persistent config + runtime extras pushed by companion
            // mods via canjewelry.RegisterPanDrops. Companion contributions never flow back to
            // canjewelry.json so admins don't have to clean them up if a companion is removed.
            this.dropsBySourceMat = new Dictionary<string, CANPanningDrop[]>(canjewelry.config.panningDrops);
            if (canjewelry.Instance != null)
            {
                foreach (var kv in canjewelry.Instance.runtimeExtraPanDrops)
                {
                    if (this.dropsBySourceMat.TryGetValue(kv.Key, out var existing))
                    {
                        var combined = new CANPanningDrop[existing.Length + kv.Value.Length];
                        System.Array.Copy(existing, 0, combined, 0, existing.Length);
                        System.Array.Copy(kv.Value, 0, combined, existing.Length, kv.Value.Length);
                        this.dropsBySourceMat[kv.Key] = combined;
                    }
                    else
                    {
                        this.dropsBySourceMat[kv.Key] = kv.Value;
                    }
                }
            }
            foreach (CANPanningDrop[] drops in this.dropsBySourceMat.Values)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    if (drops[i].Code != null && !drops[i].Code.Path.Contains("{rocktype}"))
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
                array[num] = worldInteraction;
                return array;
            });
        }
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
        public void SetMaterial(ItemSlot slot, string materialCode)
        {
            slot.Itemstack.Attributes.SetString("materialBlockCode", materialCode);
        }
        public void RemoveMaterial(ItemSlot slot)
        {
            slot.Itemstack.Attributes.RemoveAttribute("materialBlockCode");
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string blockMaterialCode = this.GetBlockMaterialCode(itemstack);
            if (blockMaterialCode == null)
            {
                return;
            }
            string variant = this.Variant["metal"];
            string key = "canjewelry:pan-filled-" + blockMaterialCode + target.ToString() + variant;
            renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate<MultiTextureMeshRef>(capi, key, delegate
            {
                AssetLocation shapeloc = new AssetLocation("canjewelry:shapes/block/filled.json");
                Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapeloc);
                Block block = capi.World.GetBlock(new AssetLocation(blockMaterialCode));
                this.AtlasSize = capi.BlockTextureAtlas.Size;
                this.matTexPosition = GetMaterialTexPos(capi, block);
                this.ownTextureSource = capi.Tesselator.GetTextureSource(this, 0, false);
                MeshData meshdata;
                capi.Tesselator.TesselateShape("filledpan", shape, out meshdata, this, null, 0, 0, 0, null, null);
                return capi.Render.UploadMultiTextureMesh(meshdata);
            });
        }
        // Texture codes we try, in order, when picking the texture that fills the pan.
        private static readonly string[] materialTextureCodes = { "up", "all", "cube", "sides", "north", "ore1" };

        // The material block is not guaranteed to expose an "up" texture. The game expands an
        // "all" entry only into the texture codes the block's shape actually uses (see
        // TextureAtlasManager.ResolveTextureDict), so for a cube-drawtype block that means
        // up/down/north/..., but for a json-drawtype one it means whatever the shape declares.
        // Mods that turn the vanilla ores into json-drawtype blocks with 3D ore bits
        // (Visible Ores and Minerals) therefore leave neither "up" nor "all" behind, and asking
        // for "up" used to hand back the unknown-texture placeholder. Fall back through the
        // common codes and then through whatever the block does define.
        private static TextureAtlasPosition GetMaterialTexPos(ICoreClientAPI capi, Block block)
        {
            if (block == null)
            {
                return capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            foreach (string code in materialTextureCodes)
            {
                TextureAtlasPosition texPos = capi.BlockTextureAtlas.GetPosition(block, code, true);
                if (texPos != null)
                {
                    return texPos;
                }
            }
            foreach (string code in block.Textures.Keys)
            {
                TextureAtlasPosition texPos = capi.BlockTextureAtlas.GetPosition(block, code, true);
                if (texPos != null)
                {
                    return texPos;
                }
            }
            return capi.BlockTextureAtlas.UnknownTexturePosition;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity.Controls.ShiftKey)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
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

            string blockMatCode = this.GetBlockMaterialCode(slot.Itemstack);
            if (!byEntity.FeetInLiquid && this.api.Side == EnumAppSide.Client && blockMatCode != null)
            {
                (this.api as ICoreClientAPI).TriggerIngameError(this, "notinwater", Lang.Get("ingameerror-panning-notinwater", Array.Empty<object>()));
                return;
            }
            if (blockMatCode == null)
            {
                this.TryTakeMaterial(slot, byEntity);
                slot.Itemstack.TempAttributes.SetBool("canpan", false);
                return;
            }
            if (blockMatCode != null)
            {
                slot.Itemstack.TempAttributes.SetBool("canpan", true);
            }
        }
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

                slot.Itemstack.Collectible.DamageItem(this.api.World, byEntity, slot);
                slot.MarkDirty();
                EntityBehaviorHunger behavior = byEntity.GetBehavior<EntityBehaviorHunger>();
                if (behavior == null)
                {
                    return;
                }
                behavior.ConsumeSaturation(4f);
            }
        }
        public string CodePartsAfterFirst(string path)
        {
            int num = path.IndexOf('-') + 1;
            //int num2 = ((num <= 0) ? (-1) : path.IndexOf('-', num));
            if (num >= 0)
            {
                return path.Substring(num);
            }

            return path;
        }
        private void CreateDrop(EntityAgent byEntity, string fromBlockCode)
        {
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer player = (entityPlayer != null) ? entityPlayer.Player : null;
            CANPanningDrop[] drops = null;
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

            // Filter out perk-gated entries the player can't unlock. Entries without
            // requiresPerk pass through unchanged (preserves vanilla behaviour).
            drops = FilterDropsByPerk(drops, player);
            drops.Shuffle(this.api.World.Rand);
            int i = 0;
            while (i < drops.Length)
            {
                CANPanningDrop drop = drops[i];
                double num = this.api.World.Rand.NextDouble();
                float extraMul = 1f;
                if (drop.DropModbyStat != null)
                {
                    extraMul = byEntity.Stats.GetBlended(drop.DropModbyStat);
                }
                if (drop.Chance == null) { i++; continue; }
                float val2 = drop.Chance.nextFloat() * extraMul;
                ItemStack stack = drop.ResolvedItemstack;
                if (drops[i].Code != null && drops[i].Code.Path.Contains("{rocktype}"))
                {
                    stack = this.Resolve(drops[i].Type, drops[i].Code.Path.Replace("{rocktype}", rocktype));
                }
                if (num < (double)val2 && stack != null)
                {
                    stack = stack.Clone();
                    if (canjewelry.Instance != null && player != null)
                    {
                        var ev = new src.integration.PanEvent
                        {
                            Player = player,
                            Drop = stack,
                            SourceMaterial = fromBlockCode,
                        };
                        canjewelry.Instance.FirePan(ev);
                        stack = ev.Drop;
                        if (stack == null || stack.StackSize <= 0) return;
                    }
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
        // Returns true if at least one drop entry for this material is unlocked for the
        // player — used to short-circuit material consumption when every drop is gated and
        // the player has no perks that satisfy any of them.
        private bool PlayerHasAnyAllowedDrop(IPlayer player, string itemCode)
        {
            foreach (string val in this.dropsBySourceMat.Keys)
            {
                if (!WildcardUtil.Match(val, itemCode)) continue;
                foreach (var drop in this.dropsBySourceMat[val])
                {
                    if (IsDropAllowed(drop, player)) return true;
                }
                return false;
            }
            return false;
        }

        private static CANPanningDrop[] FilterDropsByPerk(CANPanningDrop[] source, IPlayer player)
        {
            // Fast path — nothing in the table is gated.
            bool anyGated = false;
            for (int i = 0; i < source.Length; i++)
            {
                if (!string.IsNullOrEmpty(source[i].requiresPerk)) { anyGated = true; break; }
            }
            if (!anyGated) return source;

            var list = new List<CANPanningDrop>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (IsDropAllowed(source[i], player)) list.Add(source[i]);
            }
            return list.ToArray();
        }

        private static bool IsDropAllowed(CANPanningDrop drop, IPlayer player)
        {
            if (string.IsNullOrEmpty(drop.requiresPerk)) return true;
            if (player == null || canjewelry.Instance == null) return false;
            var ev = new src.integration.CanPanDropEvent
            {
                Player = player,
                PerkCode = drop.requiresPerk,
            };
            canjewelry.Instance.FireCanPanDrop(ev);
            return ev.Allowed;
        }

        public virtual bool IsPannableMaterial(Block block)
        {
            JsonObject attributes = block.Attributes;
            return attributes != null && attributes.IsTrue("pannable");
        }
        protected virtual void TryTakeMaterial(ItemSlot slot, EntityAgent byEntity)
        {
            var hotbarSlotNumber = (byEntity as EntityPlayer).Player.InventoryManager.ActiveHotbarSlotNumber;
            if (hotbarSlotNumber < 9)
            {
                //0-8 are ok
                var player = (byEntity as EntityPlayer).Player;
                IInventory playerHotbar = player.InventoryManager.GetHotbarInventory();
                ItemSlot oreSlot = playerHotbar[hotbarSlotNumber + 1];

                //var c2 = base.FirstCodePart(0);
                if (oreSlot.Itemstack != null && oreSlot.Itemstack.Collectible != null)
                {
                    var collectible = oreSlot.Itemstack.Collectible;
                    if (collectible.Code.Path.Contains("stone-"))
                    {
                        if (oreSlot.Itemstack.StackSize < canjewelry.config.pan_take_per_use * 4)
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (oreSlot.Itemstack.StackSize < canjewelry.config.pan_take_per_use)
                        {
                            return;
                        }
                    }
                    string firstCodePart = collectible.FirstCodePart(0);
                    string itemCode;
                    if (firstCodePart.Equals("crystalizedore"))
                    {
                        itemCode = "ore-" + CodePartsAfterFirst(collectible.Code.ToShortString());
                    }
                    else if (collectible.Code.Path.Contains("stone-"))
                    {
                        itemCode = "game:rock-" + CodePartsAfterFirst(collectible.Code.SecondCodePart());
                    }
                    else
                    {
                        itemCode = collectible.Code.ToShortString();
                    }
                    bool dropFound = false;
                    foreach (string val in this.dropsBySourceMat.Keys)
                    {
                        if (WildcardUtil.Match(val, itemCode))
                        {
                            dropFound = true;
                            break;
                        }
                    }
                    if (!dropFound)
                    {
                        return;
                    }
                    // Pre-flight: if every drop in this material's table has a requiresPerk that
                    // the player can't satisfy, refuse to accept the material so the player
                    // doesn't spend it on a guaranteed empty pan.
                    if (byEntity is EntityPlayer epPan && epPan.Player != null && !PlayerHasAnyAllowedDrop(epPan.Player, itemCode))
                    {
                        return;
                    }

                    this.SetMaterial(slot, itemCode);

                    int amount = collectible.Code.Path.Contains("stone-")
                        ? canjewelry.config.pan_take_per_use * 4
                        : canjewelry.config.pan_take_per_use;

                    // Companion-mod hook — Lapidary "Light Touch" reduces this. Floor at 1.
                    if (canjewelry.Instance != null && byEntity is EntityPlayer ep && ep.Player != null)
                    {
                        var ev = new src.integration.PanTakeEvent
                        {
                            Player = ep.Player,
                            MaterialCode = itemCode,
                            Amount = amount,
                        };
                        canjewelry.Instance.FirePanMaterialTake(ev);
                        amount = System.Math.Max(1, ev.Amount);
                    }

                    oreSlot.TakeOut(amount);
                    oreSlot.MarkDirty();
                    slot.MarkDirty();
                    return;
                }
            }
        }
        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return this.interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }
        private ITexPositionSource ownTextureSource;
        private TextureAtlasPosition matTexPosition;
        private ILoadedSound sound;
        private Dictionary<string, CANPanningDrop[]> dropsBySourceMat;
        private WorldInteraction[] interactions;
    }
}
