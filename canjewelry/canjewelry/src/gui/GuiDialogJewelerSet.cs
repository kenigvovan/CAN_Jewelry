using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
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
using Vintagestory.ServerMods.NoObf;

namespace canjewelry.src.jewelry
{
    public class GuiDialogJewelerSet : GuiDialogBlockEntity
    {
        GuiElementVerticalTabs groupOfInterests;
        public float Width { get; private set; }
        public float Height { get; private set; }
        int selectedDropSocket = 0;
        // Extraction can destroy the gem or the whole piece, so it is confirmed first. The dialog
        // is not immediate mode: setting these and rebuilding the composer is what shows the
        // confirmation, -1 meaning nothing is pending.
        int pendingExtractGem = -1;
        int pendingExtractSocket = -1;
        // Renders the piece into its own framebuffer, which GuiElementItemPreview then paints.
        // Created lazily so a dialog that never shows a piece does not allocate a framebuffer.
        gui.JewelerItemPreview itemPreview;
        // The preview hangs over the world left of the dialog, as in the gem inspection, at the
        // size of JewelerItemPreview's framebuffer. Inside the dialog the left column holds what
        // belongs to the whole piece (effects, inscription, compatible gems), the right one the
        // piece's slot and a card per socket.
        const int PreviewSize = gui.JewelerItemPreview.DefaultFboSize;
        const int PreviewGap = 20;
        // The preview and the dialog together sit this far right of the screen's centre.
        const int GroupShiftX = 80;
        const int WorkColumnX = 260;
        // Inventory: slot 0 the piece, 1..4 a gem per socket, 5..8 a socket per socket.
        const int MaxSockets = 4;
        const int CardsTop = 96;
        const int CardHeight = 94;
        const int CardGap = 6;
        const int CardButtonWidth = 120;
        const string BudgetBarKey = "caratbudgetbar";
        // Filled while composing, applied once the bar exists.
        float? budgetUsed, budgetMax;

        public GuiDialogJewelerSet(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPos, ICoreClientAPI capi) : base(dialogTitle, inventory, blockEntityPos, capi)
        {
            if (IsDuplicate)
            {
                return;
            }
            this.Width = 760;
            this.Height = 680;
            capi.World.Player.InventoryManager.OpenInventory((IInventory)inventory);
            SetupDialog();
        }

        public void SetupDialog()
        {
            // Constant on purpose: the confirmation strip appears and disappears as buttons are
            // pressed, and sizing the dialog to its current contents made the whole window jump
            // around. The room it needs is simply always reserved.
            this.Height = 680;
            // Rebuilt on every slot change, so text typed for the inscription is carried over.
            string typedInscription = SingleComposer?.GetTextInput("inscriptioninput")?.GetText();

            // Shifted right by half the preview, so the two together stay centred; kept even
            // without a piece, so placing one does not move the dialog.
            ElementBounds elementBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset((PreviewSize + PreviewGap) / 2.0 + GroupShiftX, 0);
            ElementBounds backgroundBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding).WithFixedSize(Width, Height - 90);
            backgroundBounds.BothSizing = ElementSizing.Fixed;

            var jewelerComposer = this.SingleComposer = this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()] = this.capi.Gui.CreateCompo("jewelersetgui" + this.BlockEntityPosition?.ToString(), elementBounds)
                .AddShadedDialogBG(backgroundBounds)
                .AddDialogTitleBar(Lang.Get("canjewelry:jewelset_gui_name"), new Action(this.OnTitleBarClose))
                .BeginChildElements(backgroundBounds);

            ItemStack encrustable = this.Inventory[0].Itemstack;
            int maxSocketNumber = Math.Min(EncrustableCB.GetMaxAmountSockets(encrustable), MaxSockets);
            double columnWidth = this.Width - WorkColumnX;
            budgetUsed = budgetMax = null;

            // Header: the piece's slot, its name and the carats its sockets hold.
            jewelerComposer.AddItemSlotGrid(this.Inventory, new Action<object>(this.DoSendPacket), 1, new[] { 0 },
                ElementBounds.Fixed(WorkColumnX, 30, 48, 48), "socketsslots");
            AddItemPreview(jewelerComposer, 30);
            AddHeader(jewelerComposer, encrustable, columnWidth);

            double cardsBottom = CardsTop;
            if (encrustable == null || maxSocketNumber <= 0)
            {
                // Without this the work column is simply blank and it is not obvious whether the
                // dialog is broken, the item is wrong, or something has to be placed first.
                jewelerComposer.AddStaticText(Lang.Get(encrustable == null
                        ? "canjewelry:jewelerset-place-item-hint"
                        : "canjewelry:jewelerset-no-sockets"),
                    CairoFont.WhiteSmallText(), ElementBounds.Fixed(WorkColumnX, CardsTop, columnWidth - 20, 60), "emptyhint");
            }
            else
            {
                var tree = encrustable.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                int[] tiers = EncrustableCB.GetSocketsTiers(encrustable);
                for (int i = 0; i < maxSocketNumber; i++)
                {
                    AddSocketCard(jewelerComposer, encrustable, tree, tiers, i, CardsTop + i * (CardHeight + CardGap), columnWidth);
                }
                cardsBottom = CardsTop + maxSocketNumber * (CardHeight + CardGap);
            }

            double leftTop = AddTotals(jewelerComposer, encrustable, encrustable != null ? 56 : 30);
            double inscriptionBottom = AddInscriptionSection(jewelerComposer, encrustable, leftTop + 10);
            AddCompatibleGems(jewelerComposer, encrustable, inscriptionBottom + 10);
            AddExtractConfirm(jewelerComposer, cardsBottom + 4, encrustable);

            jewelerComposer.Compose();

            if (budgetMax != null)
            {
                jewelerComposer.GetStatbar(BudgetBarKey)?.SetValues(budgetUsed ?? 0f, 0f, budgetMax.Value);
            }
            if (!string.IsNullOrEmpty(typedInscription))
            {
                jewelerComposer.GetTextInput("inscriptioninput")?.SetValue(typedInscription);
            }
        }

        private string PreviewComposerKey => "jewelersetpreview" + this.BlockEntityPosition?.ToString();

        /// <summary>
        /// Rotatable preview of the piece, in a composer of its own with no backdrop, hanging over
        /// the world left of the dialog. Removed when the slot is empty - nothing to render.
        /// </summary>
        private void AddItemPreview(GuiComposer composer, double hintTop)
        {
            Composers[PreviewComposerKey]?.Dispose();
            Composers.Remove(PreviewComposerKey);
            if (this.Inventory[0].Itemstack == null) return;

            itemPreview ??= new gui.JewelerItemPreview(this.capi);

            double dialogWidth = this.Width + 2 * GuiStyle.ElementToDialogPadding;
            ElementBounds previewDialog = ElementBounds.Fixed(EnumDialogArea.CenterMiddle,
                -(dialogWidth + PreviewGap) / 2 + GroupShiftX, 0, PreviewSize, PreviewSize);
            Composers[PreviewComposerKey] = capi.Gui.CreateCompo(PreviewComposerKey, previewDialog)
                .AddInteractiveElement(new gui.GuiElementItemPreview(this.capi, ElementBounds.Fixed(0, 0, PreviewSize, PreviewSize), itemPreview), "itempreview")
                .Compose();

            // The preview responds to dragging, which is not discoverable on its own; over the
            // world the hint would not read, so it stays in the dialog.
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-drag-to-rotate"),
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(10, hintTop, WorkColumnX - 20, 18), "rotatehint");
        }

        // The preview owns a framebuffer, so it is rendered before the gui pass rather than from
        // inside the element - switching framebuffers mid-pass would disturb the dialog itself.
        public override void OnRenderGUI(float deltaTime)
        {
            itemPreview?.Render(this.Inventory[0].Itemstack);
            base.OnRenderGUI(deltaTime);
        }

        public override void Dispose()
        {
            base.Dispose();
            itemPreview?.Dispose();
            itemPreview = null;
        }

        /// <summary>The piece's name and, when it has a budget, the carats in it as a bar.</summary>
        private void AddHeader(GuiComposer composer, ItemStack encrustable, double width)
        {
            if (encrustable == null) return;
            composer.AddStaticText(encrustable.GetName(), CairoFont.WhiteSmallishText(),
                ElementBounds.Fixed(WorkColumnX + 60, 32, width - 70, 22), "itemname");

            var caratsCfg = canjewelry.config?.GemCarats;
            float? budget = caratsCfg == null ? null : CANGemAppraisal.BudgetOf(caratsCfg, encrustable);
            if (budget == null) return;

            float used = CANGemAppraisal.CaratsInSockets(caratsCfg, encrustable, -1);
            ElementBounds textEl = ElementBounds.Fixed(WorkColumnX + 60, 58, 110, 18);
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-budget",
                    CANGemAppraisalCB.FormatCarats(used), CANGemAppraisalCB.FormatCarats(budget.Value)),
                CairoFont.WhiteDetailText(), textEl, "caratbudget");
            ElementBounds barEl = ElementBounds.Fixed(WorkColumnX + 175, 62, width - 190, 10);
            composer.AddStatbar(barEl, new double[] { 0.85, 0.7, 0.3 }, BudgetBarKey);
            composer.AddHoverText(Lang.Get("canjewelry:jewelerset-budget-hint"), CairoFont.WhiteSmallText(), 260,
                ElementBounds.Fixed(WorkColumnX + 60, 56, width - 70, 20));
            budgetUsed = used;
            budgetMax = budget;
        }

        /// <summary>Where everything in one socket card goes; the card's top-left is (x, top).</summary>
        private class CardLayout
        {
            public readonly ElementBounds Frame, Title, Icon, SecondIcon, Info, GemInfo, ReadySlot, Primary, Secondary, Status;

            public CardLayout(double x, double top, double width)
            {
                double contentX = x + 8;
                double iconY = top + 26;
                double buttonsX = x + width - 18 - CardButtonWidth;
                double readySlotX = buttonsX - 8 - 48;
                Frame = ElementBounds.Fixed(x, top, width - 10, CardHeight);
                Title = ElementBounds.Fixed(contentX, top + 5, width - 26, 20);
                Icon = ElementBounds.Fixed(contentX, iconY, 48, 48);
                SecondIcon = ElementBounds.Fixed(contentX + 54, iconY, 48, 48);
                // Next to one icon, or to the socket and its gem.
                Info = ElementBounds.Fixed(contentX + 58, iconY + 2, readySlotX - 8 - contentX - 58, 46);
                GemInfo = ElementBounds.Fixed(contentX + 112, iconY, readySlotX - 8 - contentX - 112, 48);
                ReadySlot = ElementBounds.Fixed(readySlotX, iconY, 48, 48);
                Primary = ElementBounds.Fixed(buttonsX, iconY, CardButtonWidth, 22);
                Secondary = ElementBounds.Fixed(buttonsX, iconY + 26, CardButtonWidth, 22);
                Status = ElementBounds.Fixed(contentX, top + CardHeight - 19, width - 26, 16);
            }
        }

        /// <summary>
        /// One socket as a card: what sits in it, a slot for what goes in next with a line saying
        /// whether it fits and what it changes, and only the buttons that apply right now.
        /// </summary>
        private void AddSocketCard(GuiComposer composer, ItemStack encrustable, ITreeAttribute tree, int[] tiers, int i, double top, double width)
        {
            var card = new CardLayout(WorkColumnX, top, width);
            composer.AddInset(card.Frame, 2, 0.85f);

            ITreeAttribute socket = tree?.GetTreeAttribute("slot" + i);
            if (socket == null) AddEmptySocketCard(composer, card, i, tiers != null && i < tiers.Length ? tiers[i] : 1);
            else AddFilledSocketCard(composer, card, encrustable, socket, i);
        }

        /// <summary>No socket yet: a slot for one of the tier this place takes, and the button to set it.</summary>
        private void AddEmptySocketCard(GuiComposer composer, CardLayout card, int i, int slotTier)
        {
            string tierDesc = Lang.Get("canjewelry:jewelerset-tier" + slotTier + "-desc");
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-card-empty", i + 1, slotTier, tierDesc),
                CairoFont.WhiteSmallText(), card.Title, "cardtitle" + i);
            this.Inventory[i + 5].HexBackgroundColor = TierColor(slotTier);
            composer.AddItemSlotGrid(this.Inventory, new Action<object>(this.SendInvPacket), 1, new[] { i + 5 }, card.Icon, "socketsslot" + i);
            composer.AddHoverText(Lang.Get("canjewelry:jewelerset-socket-slot-hint", slotTier, tierDesc),
                CairoFont.WhiteSmallText(), 260, card.Icon.FlatCopy());
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-card-put-socket"),
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment), card.Info, "cardinfo" + i);
            composer.AddSmallButton(Lang.Get("canjewelry:jewelerset-add-socket"),
                () => { OnClickButtonAddSocket(i, i + 5); return true; }, card.Primary, EnumButtonStyle.Normal, "cardprimary" + i);
        }

        /// <summary>A socket, the gem in it if any, and the gem lying ready to go in.</summary>
        private void AddFilledSocketCard(GuiComposer composer, CardLayout card, ItemStack encrustable, ITreeAttribute socket, int i)
        {
            var caratsCfg = canjewelry.config?.GemCarats;
            int socketTier = socket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE);
            ItemStack socketStack = SocketItemOfTier(socketTier);
            string capacity = caratsCfg != null && caratsCfg.CapacityByCarats
                ? Lang.Get("canjewelry:jewelerset-card-capacity", CANGemAppraisalCB.FormatCarats(CANGemAppraisal.SocketCapacity(caratsCfg, socketTier)))
                : "";
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-card-socket", i + 1, socketStack?.GetName() ?? "", capacity),
                CairoFont.WhiteSmallText(), card.Title, "cardtitle" + i);

            bool hasGem = !string.IsNullOrEmpty(socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
            ItemStack inSocket = hasGem ? CANGemSize.BuildCutGemFromSocket(capi.World, socket) : null;
            AddDisplayItem(composer, card.Icon, socketStack, "cardsocket" + i);
            AddDisplayItem(composer, card.SecondIcon, inSocket, "cardgem" + i);

            string currentBuffs = BuffsOf(socket);
            string info = inSocket != null
                ? Lang.Get("canjewelry:jewelerset-card-gem", inSocket.GetName(), CANGemAppraisalCB.FormatCarats(GemCarats(inSocket)), currentBuffs)
                : Lang.Get("canjewelry:jewelerset-card-no-gem");
            composer.AddRichtext(Vtml(info), CairoFont.WhiteDetailText(), card.GemInfo, "cardinfo" + i);

            // The gem waiting to go in, backed green or red.
            ItemStack readyGem = this.Inventory[i + 1].Itemstack;
            string refusal = readyGem == null ? null : EncrustableCB.EncrustRefusal(readyGem, encrustable, socketTier, i);
            this.Inventory[i + 1].HexBackgroundColor = readyGem == null ? null : (refusal == null ? "#2FE147" : "#F03330");
            composer.AddItemSlotGrid(this.Inventory, new Action<object>(this.SendInvPacket), 1, new[] { i + 1 }, card.ReadySlot, "gemslot" + i);

            composer.AddSmallButton(Lang.Get(hasGem ? "canjewelry:jewelerset-swap" : "canjewelry:jewelerset-set-gem"),
                () => { OnClickButtonAddGem(i, i + 1); return true; }, card.Primary, EnumButtonStyle.Normal, "cardprimary" + i);
            if (hasGem)
            {
                composer.AddSmallButton(Lang.Get("canjewelry:jewelerset-extract"),
                    () => { pendingExtractGem = i; pendingExtractSocket = -1; Rebuild(); return true; },
                    card.Secondary, EnumButtonStyle.Normal, "removegem" + i);
                // Odds of recovering the gem and of destroying the piece, from config.
                composer.AddHoverText(Lang.Get("canjewelry:jewelerset-extract-tooltip",
                        (int)(canjewelry.config.gemExtractionReturnChance * 100f),
                        (int)(canjewelry.config.jewelryBreakOnExtractionChance * 100f)),
                    CairoFont.WhiteSmallText(), 280, card.Secondary.FlatCopy());
            }
            else if (canjewelry.config.canExtractSocket)
            {
                // The server refuses to take out a socket that still holds a gem.
                composer.AddSmallButton(Lang.Get("canjewelry:jewelerset-remove-socket-short"),
                    () => { pendingExtractSocket = i; pendingExtractGem = -1; Rebuild(); return true; },
                    card.Secondary, EnumButtonStyle.Normal, "removesocket" + i);
            }

            string status = ReadyGemStatus(readyGem, refusal, hasGem, socketTier, currentBuffs, out string color);
            if (!string.IsNullOrEmpty(status))
            {
                composer.AddRichtext("<font color=\"" + color + "\">" + Vtml(status) + "</font>",
                    CairoFont.WhiteDetailText(), card.Status, "cardstatus" + i);
            }
        }

        /// <summary>What the waiting gem would do in this socket, why it cannot go in, or what to put there.</summary>
        private static string ReadyGemStatus(ItemStack readyGem, string refusal, bool hasGem, int socketTier, string currentBuffs, out string color)
        {
            if (readyGem == null)
            {
                color = "#C8B88A";
                return hasGem ? "" : Lang.Get("canjewelry:jewelerset-card-put-gem");
            }
            if (refusal != null)
            {
                color = "#FF7366";
                return refusal;
            }
            color = "#8BE08B";
            string newBuffs = BuffsOf(readyGem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE));
            if (hasGem) return Lang.Get("canjewelry:jewelerset-card-swap", currentBuffs, newBuffs);
            string fit = SocketFitText(readyGem, socketTier);
            return string.IsNullOrEmpty(fit) ? newBuffs : Lang.Get("canjewelry:jewelerset-card-fits", fit, newBuffs);
        }

        // Buff names come from lang with their own font tags, so these texts go out as VTML.
        private static string Vtml(string text) => (text ?? "").Replace("\n", "<br>");

        private void Rebuild() => this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupjewelersetdlg");

        // Items only shown - the socket and gem in a card, the compatible gems: drawn without a
        // slot, with their tooltip, and nothing to take since they are copies.
        private void AddDisplayItem(GuiComposer composer, ElementBounds bounds, ItemStack stack, string key)
        {
            if (stack == null) return;
            composer.AddInteractiveElement(new gui.GuiElementItemIcon(capi, bounds, stack), key);
        }

        private static string TierColor(int tier) => tier == 3 ? "#9214C9" : (tier == 2 ? "#2B3FF7" : "#2FE147");

        private ItemStack SocketItemOfTier(int tier)
        {
            string code = "canjewelry:cansocket-tinbronze";
            foreach (var it in canjewelry.config.LevelOfSocketByType)
            {
                if (it.Value == tier) code = it.Key;
            }
            Item item = capi.World.GetItem(new AssetLocation(code));
            return item == null ? null : new ItemStack(item);
        }

        private static float GemCarats(ItemStack gem)
            => CANGemSize.GetCutCarats(gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE),
                gem.Collectible.Attributes?["canGemType"].AsInt(1) ?? 1, canjewelry.config?.GemCarats);

        private static string BuffsOf(ITreeAttribute tree)
        {
            string text = CANBuffText.Of(tree);
            return text.Length > 0 ? text : Lang.Get("canjewelry:jewelerset-no-effects");
        }

        /// <summary>
        /// Left column, under the rotate hint: what all the piece's gems give together.
        /// </summary>
        /// <returns>The y coordinate right below this section.</returns>
        private double AddTotals(GuiComposer composer, ItemStack encrustable, double top)
        {
            if (encrustable == null) return top;

            var totals = new Dictionary<string, float>();
            var tree = encrustable.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            for (int i = 0; tree != null && i < MaxSockets; i++)
            {
                ITreeAttribute socket = tree.GetTreeAttribute("slot" + i);
                string[] names = (socket?[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
                float[] values = (socket?[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;
                if (names == null || values == null) continue;
                for (int j = 0; j < Math.Min(names.Length, values.Length); j++)
                {
                    totals[names[j]] = (totals.TryGetValue(names[j], out float sum) ? sum : 0f) + values[j];
                }
            }

            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-totals"), CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(10, top, WorkColumnX - 20, 20), "totalslabel");
            string text = totals.Count == 0
                ? Lang.Get("canjewelry:jewelerset-totals-none")
                : string.Join("<br>", totals.Select(t => CANBuffText.Name(t.Key) + " " + CANBuffText.Value(t.Key, t.Value)));
            double height = Math.Max(1, totals.Count) * 18 + 4;
            composer.AddRichtext(text, CairoFont.WhiteDetailText(), ElementBounds.Fixed(10, top + 22, WorkColumnX - 20, height), "totalstext");
            return top + 22 + height;
        }

        /// <summary>The cut gem's weight against the socket's capacity; null when sockets go by tiers.</summary>
        private static string SocketFitText(ItemStack cutGem, int socketTier)
        {
            var caratsCfg = canjewelry.config?.GemCarats;
            if (caratsCfg == null || !caratsCfg.CapacityByCarats) return null;
            return Lang.Get("canjewelry:jewelerset-socket-fit", CANGemAppraisalCB.FormatCarats(GemCarats(cutGem)),
                CANGemAppraisalCB.FormatCarats(CANGemAppraisal.SocketCapacity(caratsCfg, socketTier)));
        }

        /// <summary>
        /// Engraving is permanent and one-off: once a piece carries an inscription the field is
        /// replaced by the text itself. A companion mod can veto engraving through CanInscribe,
        /// which is why the event is asked rather than assumed.
        /// </summary>
        /// <returns>The y coordinate right below this section.</returns>
        private double AddInscriptionSection(GuiComposer composer, ItemStack encrustable, double top)
        {
            if (encrustable == null) return top;

            // Left column: the engraving belongs to the piece rather than to any single socket.
            ElementBounds labelEl = ElementBounds.FixedSize(WorkColumnX - 20, 20);
            labelEl.fixedX = 10;
            labelEl.fixedY = top;

            string existing = encrustable.Attributes?.GetString(CANJWConstants.INSCRIPTION);
            if (!string.IsNullOrEmpty(existing))
            {
                // Header stays put, so an engraved piece does not just show a floating quote.
                composer.AddStaticText(Lang.Get("canjewelry:jewelerset-inscription"), CairoFont.WhiteSmallText(), labelEl, "inscriptionlabel");

                ElementBounds existingEl = labelEl.FlatCopy();
                existingEl.fixedY = labelEl.fixedY + labelEl.fixedHeight + 4;
                composer.AddStaticText("\"" + existing + "\"", CairoFont.WhiteDetailText(), existingEl, "inscriptiontext");
                return existingEl.fixedY + existingEl.fixedHeight;
            }

            var canInscribeEvent = new src.integration.CanInscribeEvent { Jewelry = encrustable };
            canjewelry.Instance?.FireCanInscribe(canInscribeEvent);
            if (!canInscribeEvent.Allowed)
            {
                composer.AddStaticText(Lang.Get("canjewelry:jewelerset-inscribe-locked"), CairoFont.WhiteSmallText(), labelEl, "inscriptionlocked");
                return labelEl.fixedY + labelEl.fixedHeight;
            }

            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-inscription"), CairoFont.WhiteSmallText(), labelEl, "inscriptionlabel");

            // Read off before the inset is added: ForkBoundingParent re-parents the input and
            // makes its fixedX/fixedY relative to that new frame, so they cannot be used after.
            double rowY = labelEl.fixedY + labelEl.fixedHeight + 4;

            ElementBounds inputEl = ElementBounds.FixedSize(WorkColumnX - 24, 26);
            inputEl.fixedX = 10;
            inputEl.fixedY = rowY;
            // The text input draws no background of its own, so it is invisible without an inset.
            composer.AddInset(inputEl.ForkBoundingParent(2, 2, 2, 2), 2);
            composer.AddTextInput(inputEl, null, CairoFont.WhiteSmallText(), "inscriptioninput");

            // Under the field rather than beside it - the left column is too narrow for both.
            ElementBounds inscribeEl = ElementBounds.FixedSize(130, 26);
            inscribeEl.fixedX = 10;
            inscribeEl.fixedY = rowY + 34;
            composer.AddSmallButton(Lang.Get("canjewelry:jewelerset-inscribe-btn"),
                new ActionConsumable(() =>
                {
                    string text = SingleComposer?.GetTextInput("inscriptioninput")?.GetText();
                    if (string.IsNullOrWhiteSpace(text)) return true;
                    SendInscribe(text);
                    return true;
                }), inscribeEl, EnumButtonStyle.Normal, "inscribebtn");

            // Engraving cannot be undone, so the rules are spelled out rather than hidden.
            ElementBounds hintEl = ElementBounds.FixedSize(WorkColumnX - 24, 40);
            hintEl.fixedX = 10;
            hintEl.fixedY = inscribeEl.fixedY + inscribeEl.fixedHeight + 6;
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-inscription-hint"),
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment), hintEl, "inscriptionhint");

            return hintEl.fixedY + hintEl.fixedHeight;
        }

        /// <summary>
        /// Gems this piece accepts, as icons rather than a list of names: fifteen rows of
        /// "tourmalinerubellite" needed a panel of their own hanging off the side of the dialog,
        /// while the icons fit under the inscription and say more at a glance. Hovering one gives
        /// the usual item tooltip, so the names are still one mouse-over away.
        /// </summary>
        private void AddCompatibleGems(GuiComposer composer, ItemStack encrustable, double top)
        {
            if (encrustable == null) return;
            string[] gemTypes = GetAvailableGemTypes(encrustable);
            if (gemTypes == null || gemTypes.Length == 0) return;

            ElementBounds labelEl = ElementBounds.FixedSize(WorkColumnX - 20, 20);
            labelEl.fixedX = 10;
            labelEl.fixedY = top;
            composer.AddStaticText(Lang.Get("canjewelry:jewelerset-compatible-gems"), CairoFont.WhiteSmallText(), labelEl, "compatiblegemslabel");

            // Plain icons with their tooltip, where a richtext itemstack drew small and off its place.
            const int iconSize = 44;
            const int perRow = 5;
            const int stride = 47;

            // Resolved up front: a gem type the world does not have (its item comes from another
            // mod) is dropped entirely.
            List<Item> gems = new List<Item>();
            foreach (string gemType in gemTypes)
            {
                // Prefer the cut gem; a type that only exists as a rough stone still gets shown.
                Item gemItem = capi.World.GetItem(new AssetLocation("canjewelry:gem-cut-normal-" + gemType))
                               ?? capi.World.GetItem(new AssetLocation("canjewelry:gem-rough-normal-" + gemType));
                if (gemItem != null) gems.Add(gemItem);
            }
            if (gems.Count == 0) return;

            double gridY = labelEl.fixedY + labelEl.fixedHeight + 4;
            for (int i = 0; i < gems.Count; i++)
            {
                ElementBounds iconEl = ElementBounds.Fixed(10 + (i % perRow) * stride, gridY + (i / perRow) * stride, iconSize, iconSize);
                AddDisplayItem(composer, iconEl, new ItemStack(gems[i]), "compatiblegem" + i);
            }
        }

        /// <summary>
        /// Confirmation strip for a pending extraction, with the odds taken from the config so the
        /// player sees what is being risked. Drawn below the socket cards and only while something
        /// is pending - the composer is rebuilt whenever that changes.
        /// </summary>
        private void AddExtractConfirm(GuiComposer composer, double top, ItemStack encrustable)
        {
            if (pendingExtractGem < 0 && pendingExtractSocket < 0) return;

            bool removingGem = pendingExtractGem >= 0;
            string name = encrustable?.GetName() ?? "";
            string question = removingGem
                ? Lang.Get("canjewelry:jewelerset-extract-confirm-body", name,
                    (int)(canjewelry.config.gemExtractionReturnChance * 100f),
                    (int)(canjewelry.config.jewelryBreakOnExtractionChance * 100f))
                : Lang.Get("canjewelry:jewelerset-remove-socket-confirm-body", name,
                    (int)(canjewelry.config.socketExtractionReturnChance * 100f));

            // The warning wraps to a different number of lines depending on the item's name and
            // the translation, so its height is measured rather than guessed - guessing is what
            // made the buttons land on top of the last line.
            CairoFont font = CairoFont.WhiteSmallText();
            double textWidth = this.Width - WorkColumnX - 20;
            double textHeight = this.capi.Gui.Text.GetMultilineTextHeight(font, question, textWidth, EnumLinebreakBehavior.Default)
                                / RuntimeEnv.GUIScale;

            ElementBounds textEl = ElementBounds.FixedSize(textWidth, textHeight + 6);
            textEl.fixedX = WorkColumnX;
            textEl.fixedY = top;
            composer.AddStaticText(question, font, textEl, "extractconfirmtext");

            ElementBounds yesEl = ElementBounds.FixedSize(150, 26);
            yesEl.fixedX = WorkColumnX;
            yesEl.fixedY = textEl.fixedY + textEl.fixedHeight + 8;
            composer.AddSmallButton(Lang.Get(removingGem
                    ? "canjewelry:jewelerset-extract-confirm-yes"
                    : "canjewelry:jewelerset-remove-socket"),
                new ActionConsumable(() =>
                {
                    if (pendingExtractGem >= 0) SendExtractGem(pendingExtractGem);
                    else SendExtractSocket(pendingExtractSocket);
                    ClearPendingExtract();
                    return true;
                }), yesEl, EnumButtonStyle.Normal, "extractconfirmyes");

            ElementBounds noEl = yesEl.FlatCopy();
            noEl.fixedX = WorkColumnX + 162;
            noEl.fixedWidth = 110;
            composer.AddSmallButton(Lang.Get("canjewelry:jewelerset-extract-confirm-no"),
                new ActionConsumable(() =>
                {
                    ClearPendingExtract();
                    return true;
                }), noEl, EnumButtonStyle.Normal, "extractconfirmno");
        }

        private void ClearPendingExtract()
        {
            pendingExtractGem = -1;
            pendingExtractSocket = -1;
            Rebuild();
        }
        private void didSelectEntity(string code, bool selected)
        {
            if (selected)
            {
                if(int.TryParse(code, out int res))
                {
                    selectedDropSocket = res;
                    SetupDialog();
                }
                else
                {
                    selectedDropSocket = 0;
                    SetupDialog();
                }
            }
            else
            {
                selectedDropSocket = 0;
            }

        }
        private string[] GetAvailableGemTypes(ItemStack itemStack)
        {
            return canjewelry.config.buffNameToPossibleItem.Keys
                .Where(gemType => EncrustableCB.canItemContainThisGem(gemType, itemStack))
                .ToArray();
        }
        public static int getStringLength(string name)
        {
            using (var skFont = new SKFont(SKTypeface.FromFamilyName("Times New Roman"), 24))
            {
                var skBounds = SKRect.Empty;
                return (int)skFont.MeasureText(name);
            }
        }

        private void OnTitleBarClose() => this.TryClose();
        public override void OnGuiClosed()
        {
            this.capi.Network.SendPacketClient(this.capi.World.Player.InventoryManager.CloseInventory((IInventory)this.Inventory));
            base.Inventory.SlotModified -= this.OnInventorySlotModified;
            base.OnGuiClosed();
        }
        private void OnGroupTabClicked(int clicked)
        {
            this.groupOfInterests.ActiveElement = clicked;
            this.SetupDialog();
        }
        private void OnGroupTabClicked2(int arg1, GuiTab tab)
        {
            this.groupOfInterests.ActiveElement = arg1;
            this.SetupDialog();
        }
        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            base.Inventory.SlotModified += this.OnInventorySlotModified;

        }
        // Any slot: the cards say what the gem lying in a slot would do, so they follow it.
        private void OnInventorySlotModified(int slotid)
        {
            Rebuild();
        }
        public bool onClickBackButtonPutSocket()
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                TreeAttribute tree = new TreeAttribute();
                tree.SetInt("selectedSocketSlot", selectedDropSocket);
                tree.ToBytes(stream);
                array = output.ToArray();
            }

            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1004, array);
            return true;
        }
        public bool onClickBackButtonPutGem()
        {
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1005);
            return true;
        }

        public void OnClickButtonAddGem(int socketNum, int slotNum)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                TreeAttribute tree = new TreeAttribute();
                //in which slot in item we want socket to be added
                tree.SetInt("selectedSocketSlot", socketNum);
                //which slot of inventory contains socket item to be added
                tree.SetInt("selectedSlotNum", slotNum);
                tree.ToBytes(stream);
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1005, array);
        }

        public void OnClickButtonAddSocket(int socketNum, int slotNum)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                TreeAttribute tree = new TreeAttribute();
                //in which slot in item we want socket to be added
                tree.SetInt("selectedSocketSlot", socketNum);
                //which slot of inventory contains socket item to be added
                tree.SetInt("selectedSlotNum", slotNum);
                tree.ToBytes(stream);
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1004, array);
        }
        private void SendInvPacket(object packet)
        {
            this.capi.Network.SendBlockEntityPacket(base.BlockEntityPosition.X, base.BlockEntityPosition.Y, base.BlockEntityPosition.Z, packet);
        }

        /// <summary>Pulls the gem out of socket <paramref name="socketNum"/>, may destroy it or the piece.</summary>
        private void SendExtractGem(int socketNum)
        {
            SendSocketAction(1006, socketNum, 1 + socketNum);
        }

        /// <summary>Pulls the socket itself back out of the piece.</summary>
        private void SendExtractSocket(int socketNum)
        {
            SendSocketAction(1008, socketNum, 5 + socketNum);
        }

        // Payload shape the block entity expects for both extract packets.
        private void SendSocketAction(int packetId, int socketNum, int slotNum)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter(output);
                TreeAttribute tree = new TreeAttribute();
                tree.SetInt("selectedSocketSlot", socketNum);
                tree.SetInt("selectedSlotNum", slotNum);
                tree.ToBytes(stream);
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, packetId, array);
        }

        private void SendInscribe(string text)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter(output);
                stream.Write(text ?? "");
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1007, array);
        }
    }
}
