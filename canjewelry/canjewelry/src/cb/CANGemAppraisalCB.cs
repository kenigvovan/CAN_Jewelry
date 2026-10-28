using System;
using System.Globalization;
using System.Linq;
using System.Text;
using canjewelry.src.items;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canjewelry.src.cb
{
    /// <summary>
    /// Appraisal of rough gems, ours and vanilla alike: the loupe in the left hand and the
    /// silent roll on the cutting table. Attached next to
    /// CANGemCuttableCB, because vanilla rough gems are not our item class.
    /// </summary>
    public class CANGemAppraisalCB : CollectibleBehavior, IContainedMeshSource
    {
        // ---- shelves and display cases: a gem with its own shape keeps it there too ----

        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            ItemStack stack = slot?.Itemstack;
            ICoreClientAPI capi = canjewelry.capi;
            // Null makes the shelf draw the item's usual mesh.
            if (capi == null || !CANGemRoughShape.HasSeed(stack) || stack.Item == null) return null;

            Shape shape = CANGemRoughShape.ShapeFor(capi, stack);
            if (shape == null) return null;
            shape.ResolveReferences(capi.Logger, "canjewelry rough gem on display");
            var texSource = new render.CANTexSource(capi, targetAtlas, stack.Item.Textures, "rough gem on display " + stack.Item.Code);
            capi.Tesselator.TesselateShape("canjewelry rough gem", shape, out MeshData mesh, texSource);
            return mesh;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            ItemStack stack = slot?.Itemstack;
            string code = stack?.Collectible?.Code?.ToString();
            return CANGemRoughShape.HasSeed(stack) ? code + "|shape" + CANGemRoughShape.SeedOf(stack) : code;
        }

        public CANGemAppraisalCB(CollectibleObject collObj) : base(collObj)
        {
        }

        private static Config.GemCaratsConfig Cfg => canjewelry.config?.GemCarats;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (!firstEvent || slot.Empty || Cfg == null) return;

            // A right click on water wets the gems in hand: the inspection then sees more.
            Block target = blockSel == null ? null : byEntity.World.BlockAccessor.GetBlock(blockSel.Position, BlockLayersAccess.Fluid);
            if (target?.LiquidCode != null && target.LiquidCode.Contains("water"))
            {
                if (byEntity.Api.Side == EnumAppSide.Server)
                {
                    slot.Itemstack.Attributes.SetDouble(CANGemAppraisal.WetUntilKey,
                        byEntity.World.Calendar.TotalHours + (Cfg.Inspection?.WetHours ?? 1.0));
                    slot.MarkDirty();
                    byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/environment/smallsplash"), byEntity, null, true, 12f, 0.5f);
                }
                handHandling = EnumHandHandling.PreventDefault;
                handling = EnumHandling.PreventSubsequent;
                return;
            }

            if (!CANItemLoupe.IsHeldInLeftHand(byEntity)) return;

            // Shelves, display cases and the cutting table take the gem on a right click.
            if (blockSel != null && byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) != null) return;

            if (!MayAppraise(slot.Itemstack))
            {
                (byEntity.Api as ICoreClientAPI)?.TriggerIngameError(this, "canjewelry-appraised",
                    Lang.Get(Cfg.Inspection?.Enabled == true ? "canjewelry:inspect-no-clicks" : "canjewelry:appraisal-already"));
                return;
            }

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;

            // The inspection opens at once; only the plain loupe is held for a while.
            if (Cfg.Inspection?.Enabled == true && byEntity.Api.Side == EnumAppSide.Server)
            {
                CANGemInspectionServer.Start((byEntity as EntityPlayer)?.Player as IServerPlayer, slot);
            }
        }

        /// <summary>
        /// The inspection opens a gem again, wherever the player is, while it has clicks left;
        /// the plain loupe looks once.
        /// </summary>
        private static bool MayAppraise(ItemStack gem)
            => Cfg.Inspection?.Enabled == true
                ? CANGemInspectionServer.CanInspect(gem)
                : CANGemAppraisal.GetAppraisal(gem.Attributes) < CANGemAppraisal.Loupe;

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            // Only reached for an interaction this behavior started; a loupe put away mid-hold ends it.
            handling = EnumHandling.PreventSubsequent;
            if (Cfg == null || !CANItemLoupe.IsHeldInLeftHand(byEntity)) return false;
            // The inspection has already opened on the click: nothing to hold for.
            if (Cfg.Inspection?.Enabled == true) return false;
            return secondsUsed < Cfg.Appraisal.LoupeSeconds;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            if (byEntity.Api.Side != EnumAppSide.Server || slot.Empty || Cfg == null) return;
            if (secondsUsed < Cfg.Appraisal.LoupeSeconds - 0.1f || !CANItemLoupe.IsHeldInLeftHand(byEntity)) return;
            if (!MayAppraise(slot.Itemstack)) return;

            handling = EnumHandling.PreventSubsequent;
            IPlayer player = (byEntity as EntityPlayer)?.Player;

            // The inspection takes the gem itself and hands it back when the player is done.
            if (Cfg.Inspection?.Enabled == true)
            {
                CANGemInspectionServer.Start(player as IServerPlayer, slot);
                return;
            }

            // One gem leaves the stack: once appraised it no longer stacks with the others.
            ItemStack gem = slot.TakeOut(1);
            ApplyLoupe(player, gem);
            if (slot.Empty) slot.Itemstack = gem;
            else if (player?.InventoryManager.TryGiveItemstack(gem, true) != true)
            {
                byEntity.World.SpawnItemEntity(gem, byEntity.Pos.XYZ);
            }
            slot.MarkDirty();

            canjewelry.Instance?.FireXp(new integration.CANXpEvent { Player = player, Action = "appraise", Xp = 0f });
        }

        // The conversion recipes copy the input's attributes so an appraised gem keeps its weight and
        // traits; a vanilla gem's "potential" comes along too, and on our gem it would only stop it
        // from stacking with the ones that never were converted.
        public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe, ref EnumHandling handling)
        {
            ItemStack output = outputSlot?.Itemstack;
            if (output != null && !CANGemCuttableCB.IsVanillaRoughGem(output.Collectible))
            {
                output.Attributes.RemoveAttribute("potential");
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            if (inSlot.Empty || Cfg == null || !MayAppraise(inSlot.Itemstack))
            {
                return base.GetHeldInteractionHelp(inSlot, ref handling);
            }
            return new[]
            {
                new WorldInteraction { ActionLangCode = "canjewelry:heldhelp-appraise", MouseButton = EnumMouseButton.Right }
            };
        }

        // The inspection dialog draws its gem with the flaws on it; every other stack is left alone.
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (itemstack.Attributes.GetBool(render.CANGemInspectMesh.ViewKey) && render.CANGemInspectMesh.Current != null)
            {
                renderinfo.ModelRef = render.CANGemInspectMesh.Current;
                return;
            }
            // A gem that has been inspected has its own shape, in the hand and the inventory alike.
            MultiTextureMeshRef own = render.CANGemShapeMeshCache.Get(capi, itemstack);
            if (own != null) renderinfo.ModelRef = own;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (inSlot?.Itemstack == null) return;
            AppendRoughInfo(dsc, inSlot.Itemstack);
        }

        // ---- Stages, server side ----

        /// <summary>The loupe: rolls the truth if the gem has none, narrows the range, reveals a trait.</summary>
        public static void ApplyLoupe(IPlayer player, ItemStack gem)
        {
            var cfg = Cfg;
            ITreeAttribute tree = gem.Attributes;
            string quality = CANGemSize.RoughQualityOf(gem);
            string gemType = CANGemSize.RoughGemTypeOf(gem);
            CANGemAppraisal.EnsureTruth(tree, cfg, quality, gemType, Config.rand);

            var ev = FireAppraise(player, gem, AppraiseStage.Loupe, tree, 1);

            var known = CANGemAppraisal.GetKnownTraits(tree).ToList();
            for (int i = 0; i < ev.RevealCount; i++)
            {
                string trait = CANGemAppraisal.RevealOne(ev.Traits, known.ToArray(), Config.rand);
                if (trait == null) break;
                known.Add(trait);
            }

            float[] qualityRange = CANGemAppraisal.RoughRange(cfg, quality, gemType) ?? new[] { ev.Carats, ev.Carats };
            float[] range = CANGemAppraisal.ShownRange(cfg, ev.Carats, qualityRange, CANItemLoupe.WeighHalfWidth(player?.Entity), Config.rand);
            CANGemAppraisal.SetKnowledge(tree, CANGemAppraisal.Loupe, range, known.ToArray());
        }


        /// <summary>
        /// The cutting table: a gem put on it unappraised gets its truth there, unseen by the player.
        /// Works on the work item's tree; nothing happens when the gem already had a truth.
        /// </summary>
        public static void ApplyTable(IPlayer player, ItemStack workItem)
        {
            var cfg = Cfg;
            if (cfg == null) return;
            string quality = workItem.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE);
            string gemType = workItem.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET);
            if (!CANGemAppraisal.EnsureTruth(workItem.Attributes, cfg, quality, gemType, Config.rand)) return;
            FireAppraise(player, workItem, AppraiseStage.Table, workItem.Attributes, 0);
        }

        private static integration.AppraiseEvent FireAppraise(IPlayer player, ItemStack gem, AppraiseStage stage, ITreeAttribute tree, int revealCount)
        {
            var ev = new integration.AppraiseEvent
            {
                Player = player,
                Gem = gem,
                Stage = stage,
                Carats = CANGemAppraisal.GetCarats(tree),
                Traits = CANGemAppraisal.GetTraits(tree),
                RevealCount = revealCount
            };
            canjewelry.Instance?.FireAppraise(ev);

            ev.Traits ??= new string[0];
            ev.Carats = Math.Max(Cfg.Step, CANGemAppraisal.RoundCarats(ev.Carats, Cfg.Step));
            CANGemAppraisal.SetWeight(tree, ev.Carats, ev.Traits);
            return ev;
        }

        // ---- Tooltips ----

        public static string FormatCarats(float carats) => carats.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>"5.3–6.0", or just "5.4" when the weight is known exactly.</summary>
        public static string FormatCaratsRange(float lo, float hi)
            => Math.Abs(hi - lo) < 0.001f ? FormatCarats(lo) : FormatCarats(lo) + "–" + FormatCarats(hi);

        public static string FormatTraits(string[] traits)
            => string.Join(", ", traits.Select(t => Lang.Get("canjewelry:gem-trait-" + t)));

        /// <summary>
        /// The traits worth showing: those the config still has. A gem can carry one the config
        /// dropped since (cloudiness, clear water) - it does nothing any more, so it is not shown.
        /// </summary>
        public static string[] ShownTraits(string[] traits)
            => (traits ?? new string[0]).Where(t => Cfg == null || Cfg.Traits.ContainsKey(t)).ToArray();

        /// <summary>What the player knows of a rough gem or a work item. Never the hidden truth.</summary>
        public static void AppendRoughInfo(StringBuilder dsc, ItemStack stack)
            => AppendRoughInfo(dsc, stack.Attributes, CANGemSize.RoughQualityOf(stack), CANGemSize.RoughGemTypeOf(stack));

        public static void AppendRoughInfo(StringBuilder dsc, ITreeAttribute tree, string quality, string gemType)
        {
            if (canjewelry.capi?.World != null && CANGemAppraisal.IsWet(tree, canjewelry.capi.World.Calendar.TotalHours))
            {
                dsc.AppendLine(Lang.Get("canjewelry:gem-wet"));
            }
            if (tree.HasAttribute(CANGemInspectionServer.ClicksLeftKey))
            {
                dsc.AppendLine(Lang.Get("canjewelry:inspect-clicks-left", tree.GetInt(CANGemInspectionServer.ClicksLeftKey)));
            }
            int appraisal = CANGemAppraisal.GetAppraisal(tree);
            float[] range = CANGemAppraisal.GetCaratsRange(tree);

            if (appraisal < CANGemAppraisal.Loupe || range == null)
            {
                // The config can still be on its way from the server: then no range at all.
                float[] qualityRange = Cfg == null ? null : CANGemAppraisal.RoughRange(Cfg, quality, gemType);
                dsc.AppendLine(qualityRange == null
                    ? Lang.Get("canjewelry:appraisal-none")
                    : Lang.Get("canjewelry:appraisal-none-range", FormatCarats(qualityRange[0]), FormatCarats(qualityRange[1])));
                return;
            }

            string langKey = appraisal >= CANGemAppraisal.Window ? "canjewelry:appraisal-window" : "canjewelry:appraisal-loupe";
            dsc.AppendLine(Lang.Get(langKey, FormatCaratsRange(range[0], range[1])));

            string[] known = ShownTraits(CANGemAppraisal.GetKnownTraits(tree));
            dsc.AppendLine(known.Length > 0
                ? Lang.Get("canjewelry:appraisal-traits", FormatTraits(known))
                : Lang.Get(appraisal >= CANGemAppraisal.Window ? "canjewelry:appraisal-no-traits" : "canjewelry:appraisal-no-visible-traits"));
        }

        /// <summary>Weight and traits of a cut gem tree or a socket; nothing for gems from before carats.</summary>
        public static void AppendCutInfo(StringBuilder dsc, ITreeAttribute tree)
        {
            if (!CANGemAppraisal.HasCarats(tree)) return;
            dsc.AppendLine(Lang.Get("canjewelry:gem-carats", FormatCarats(CANGemAppraisal.GetCarats(tree))));
            string[] traits = ShownTraits(CANGemAppraisal.GetTraits(tree));
            if (traits.Length > 0) dsc.AppendLine(Lang.Get("canjewelry:appraisal-traits", FormatTraits(traits)));
        }
    }
}
