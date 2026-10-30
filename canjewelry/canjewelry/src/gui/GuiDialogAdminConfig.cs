using canjewelry.src.admin;
using canjewelry.src.CB;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace canjewelry.src.gui
{
    /// <summary>
    /// The admin's view of canjewelry.json: socket rules, which gems fit an item, and the defaults
    /// the file is missing. Opened by "/canjewelry config". It never edits the config itself - every
    /// change is a packet, the server applies and broadcasts it, and the refresh that follows
    /// redraws this from the config the client was just sent. The file stays the one truth.
    /// <para>
    /// Rows are not edited in place: a few hundred rules would each be a text input rebuilt on every
    /// refresh. A row's "edit" loads it into the one edit card, the way the claims admin pages work.
    /// </para>
    /// </summary>
    public class GuiDialogAdminConfig : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        private const int TabSockets = 0;
        private const int TabGems = 1;
        private const int TabMissing = 2;

        private const double DialogWidth = 760;
        private const double TabsY = 26;
        private const double TabsHeight = 28;
        private const double ToolbarY = 66;
        private const double InputHeight = 28;
        private const double ContentY = 106;
        private const double RowHeight = 24;
        private const double SmallButtonHeight = 22;
        private const int PageSize = 12;

        private const double CardPadding = 8;
        private const double CardHeaderHeight = 33;
        private const double CardGap = 12;

        private const int GemsPerRow = 8;
        private const double GemCellWidth = 88;
        private const double GemCellHeight = 86;

        // Same palette as the claims pages, so admin windows across our mods read alike.
        private static readonly double[] SectionColor = { 0.95, 0.84, 0.52, 1.0 };
        private static readonly double[] LabelColor = { 0.74, 0.74, 0.74, 1.0 };
        private static readonly double[] ValueColor = { 1.00, 0.88, 0.45, 1.0 };
        private static readonly double[] DangerColor = { 0.90, 0.35, 0.30, 1.0 };
        private static readonly double[] SuccessColor = { 0.45, 0.80, 0.45, 1.0 };

        private List<AdminMissingEntry> missing;

        // Everything below survives the rebuild that follows each edit.
        private int currentTab = TabSockets;
        private string filter = "";
        private int socketsPage;
        private int missingPage;
        private ItemStack selectedItem;

        private string editCode = "";
        private string editKey = "";
        private string editValue = "";
        private string editTiers = "";

        /// <summary>Key of the destructive action waiting for its second click.</summary>
        private string pendingConfirm;

        private string message;
        private bool messageIsError;

        public GuiDialogAdminConfig(ICoreClientAPI capi, AdminConfigSnapshotPacket packet) : base(capi)
        {
            missing = packet.Missing ?? new List<AdminMissingEntry>();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Compose();
        }

        /// <summary>The server's answer to an edit, sent after the config it changed.</summary>
        public void Refresh(AdminConfigSnapshotPacket packet)
        {
            missing = packet.Missing ?? new List<AdminMissingEntry>();
            if (packet.Message != null)
            {
                SetMessage(Lang.Get("canjewelry:" + packet.Message, (packet.MessageArgs ?? new List<string>()).Cast<object>().ToArray()),
                    packet.MessageIsError);
            }
            Compose();
        }

        // ================================================================ layout

        private void Compose()
        {
            if (!IsOpened() && SingleComposer != null) return;

            GuiTab[] tabs =
            {
                new GuiTab { Name = T("tab-sockets"), DataInt = TabSockets },
                new GuiTab { Name = T("tab-gems"), DataInt = TabGems },
                new GuiTab { Name = T("tab-missing", missing.Count), DataInt = TabMissing },
            };

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            var compo = capi.Gui.CreateCompo("canjewelryadminconfig", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(T("title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddHorizontalTabs(tabs, ElementBounds.Fixed(0, TabsY, DialogWidth, TabsHeight), OnTabClicked,
                        CairoFont.WhiteSmallishText(), CairoFont.WhiteSmallishText().WithColor(GuiStyle.ActiveButtonTextColor), "tabs");

            var afterCompose = new List<Action>();
            AddToolbar(compo, afterCompose);

            double bottom;
            switch (currentTab)
            {
                case TabGems: bottom = AddGemsTab(compo, afterCompose); break;
                case TabMissing: bottom = AddMissingTab(compo); break;
                default: bottom = AddSocketsTab(compo, afterCompose); break;
            }

            // The outcome of the last action, one line under everything.
            if (!string.IsNullOrEmpty(message))
            {
                var font = CairoFont.WhiteSmallText().WithColor(messageIsError ? DangerColor : SuccessColor);
                var bounds = ElementBounds.Fixed(0, bottom, DialogWidth, 20);
                compo.AddStaticText(Fit(message, font, DialogWidth), font, bounds, "message");
                if (Fit(message, font, DialogWidth) != message) AddTooltip(compo, message, bounds, "message-tip");
            }
            else
            {
                compo.AddStaticText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, bottom, DialogWidth, 20), "message");
            }

            compo.EndChildElements();
            SingleComposer = compo.Compose();
            SingleComposer.GetHorizontalTabs("tabs").activeElement = currentTab;
            foreach (var action in afterCompose) action();
        }

        private void AddToolbar(GuiComposer compo, List<Action> afterCompose)
        {
            var filterBounds = ElementBounds.Fixed(0, ToolbarY, 220, InputHeight);
            compo.AddTextInput(filterBounds, v => filter = v ?? "", CairoFont.WhiteSmallText(), "filter");
            AddTooltip(compo, T("filter-hint"), filterBounds, "filter-tip");
            afterCompose.Add(() => SingleComposer.GetTextInput("filter").SetValue(filter));

            string filterCaption = T("filter");
            var filterButton = ElementBounds.Fixed(226, ToolbarY, ButtonWidth(filterCaption), InputHeight);
            compo.AddSmallButton(filterCaption, () =>
            {
                // Another set of rows: the old page number means nothing for it.
                socketsPage = 0;
                missingPage = 0;
                pendingConfirm = null;
                Compose();
                return true;
            }, filterButton, EnumButtonStyle.Normal, "filter-apply");

            string handCaption = T("from-hand");
            double handWidth = ButtonWidth(handCaption);
            var handButton = ElementBounds.Fixed(DialogWidth - handWidth, ToolbarY, handWidth, InputHeight);
            compo.AddSmallButton(handCaption, OnFromHand, handButton, EnumButtonStyle.Normal, "from-hand");
            AddTooltip(compo, T("from-hand-hint"), handButton, "from-hand-tip");
        }

        // ---------------------------------------------------------------- sockets tab

        private sealed class SocketRow
        {
            public bool IsVariantHeader;
            public bool IsVariantValue;
            public string Code;
            public string Key;
            public string Value;
            public int[] Tiers;
        }

        private List<SocketRow> SocketRows()
        {
            var cfg = canjewelry.config;
            var rows = new List<SocketRow>();
            string f = filter.Trim().ToLowerInvariant();

            // Variant rules first: an item under one takes its sockets from it, whatever the flat
            // rules say - the order GetMaxAmountSockets checks them in.
            foreach (var rule in cfg.custom_variants_sockets_tiers.Where(r => r?.ItemCode != null).OrderBy(r => r.ItemCode, StringComparer.Ordinal))
            {
                if (f.Length > 0 && !rule.ItemCode.ToLowerInvariant().Contains(f)) continue;

                rows.Add(new SocketRow { IsVariantHeader = true, Code = rule.ItemCode, Key = rule.AttributeKey });
                if (rule.SocketTiers == null) continue;
                foreach (var value in rule.SocketTiers.OrderBy(v => v.Key == "*" ? "" : v.Key, StringComparer.Ordinal))
                {
                    rows.Add(new SocketRow { IsVariantValue = true, Code = rule.ItemCode, Key = rule.AttributeKey, Value = value.Key, Tiers = value.Value });
                }
            }

            foreach (var rule in cfg.items_codes_with_socket_count_and_tiers.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                if (f.Length > 0 && !rule.Key.ToLowerInvariant().Contains(f)) continue;
                rows.Add(new SocketRow { Code = rule.Key, Tiers = rule.Value });
            }
            return rows;
        }

        private double AddSocketsTab(GuiComposer compo, List<Action> afterCompose)
        {
            var rows = SocketRows();
            int pages = Math.Max(1, (rows.Count + PageSize - 1) / PageSize);
            socketsPage = Math.Clamp(socketsPage, 0, pages - 1);

            double listHeight = PageSize * RowHeight + 6 + InputHeight;
            double y = ContentY;
            double inner = Frame(compo, y, listHeight, T("rules-card", rows.Count), "rules");

            if (rows.Count == 0)
            {
                compo.AddStaticText(T("no-rules"), CairoFont.WhiteSmallText().WithColor(LabelColor),
                    ElementBounds.Fixed(CardPadding, inner, DialogWidth - CardPadding * 2, RowHeight), "rules-empty");
            }

            var page = rows.Skip(socketsPage * PageSize).Take(PageSize).ToList();
            for (int i = 0; i < page.Count; i++)
            {
                AddSocketRow(compo, page[i], inner + i * RowHeight, i);
            }
            AddPager(compo, inner + PageSize * RowHeight + 6, socketsPage, pages, p => socketsPage = p, "rules");

            y += CardHeaderHeight + CardPadding * 2 + listHeight + CardGap;
            return AddEditCard(compo, y, afterCompose);
        }

        private void AddSocketRow(GuiComposer compo, SocketRow row, double y, int index)
        {
            double x = CardPadding;
            double width = DialogWidth - CardPadding * 2;

            // Buttons from the right edge inwards; whatever is left belongs to the text.
            var buttons = new List<(string caption, ActionConsumable onClick, string tip)>();
            if (row.IsVariantValue)
            {
                buttons.Add((T("edit"), () => LoadIntoEdit(row), null));
                buttons.Add((ConfirmCaption("remove", "rv-" + row.Code + "|" + row.Value), () => Confirmed("rv-" + row.Code + "|" + row.Value,
                    () => Send(new AdminConfigEditPacket { Op = AdminConfigOp.RemoveVariantValue, Code = row.Code, VariantValue = row.Value })), T("remove-hint")));
            }
            else if (row.IsVariantHeader)
            {
                buttons.Add((T("add-value"), () => LoadIntoEdit(row), null));
                buttons.Add((T("gems"), () => SelectRuleItem(row.Code), T("gems-hint")));
                buttons.Add((ConfirmCaption("disable", "dv-" + row.Code), () => Confirmed("dv-" + row.Code,
                    () => Send(new AdminConfigEditPacket { Op = AdminConfigOp.DisableVariantRule, Code = row.Code })), T("disable-hint")));
            }
            else
            {
                buttons.Add((T("edit"), () => LoadIntoEdit(row), null));
                buttons.Add((T("gems"), () => SelectRuleItem(row.Code), T("gems-hint")));
                if (row.Tiers != null && row.Tiers.Length > 0)
                {
                    buttons.Add((ConfirmCaption("disable", "ds-" + row.Code), () => Confirmed("ds-" + row.Code,
                        () => Send(new AdminConfigEditPacket { Op = AdminConfigOp.DisableSocketRule, Code = row.Code })), T("disable-hint")));
                }
            }

            double right = x + width;
            for (int b = buttons.Count - 1; b >= 0; b--)
            {
                double w = ButtonWidth(buttons[b].caption, small: true);
                right -= w;
                var bounds = ElementBounds.Fixed(right, y, w, SmallButtonHeight);
                string key = "row" + index + "-btn" + b;
                compo.AddSmallButton(buttons[b].caption, buttons[b].onClick, bounds, EnumButtonStyle.Small, key);
                if (buttons[b].tip != null) AddTooltip(compo, buttons[b].tip, bounds, key + "-tip");
                right -= 4;
            }

            // Tiers right-aligned against the buttons, so they line up down the page.
            const double tiersWidth = 110;
            bool disabled = !row.IsVariantHeader && (row.Tiers == null || row.Tiers.Length == 0);
            string tiersText = row.IsVariantHeader ? T("variant-key", row.Key)
                : disabled ? T("disabled") : T("tiers", AdminConfigService.Tiers(row.Tiers));
            var tiersFont = CairoFont.WhiteSmallText().WithColor(disabled ? DangerColor : row.IsVariantHeader ? LabelColor : ValueColor)
                                      .WithOrientation(EnumTextOrientation.Right);
            compo.AddStaticText(Fit(tiersText, tiersFont, tiersWidth), tiersFont,
                ElementBounds.Fixed(right - tiersWidth - 6, y + 2, tiersWidth, RowHeight), "row" + index + "-tiers");

            double labelX = row.IsVariantValue ? x + 18 : x;
            double labelWidth = right - tiersWidth - 12 - labelX;
            string label = row.IsVariantValue ? "· " + row.Value : row.Code;
            var labelFont = CairoFont.WhiteSmallText();
            if (row.IsVariantHeader) labelFont = labelFont.WithColor(SectionColor);
            else if (disabled) labelFont = labelFont.WithColor(LabelColor);
            string shown = Fit(label, labelFont, labelWidth);
            var labelBounds = ElementBounds.Fixed(labelX, y + 2, labelWidth, RowHeight);
            compo.AddStaticText(shown, labelFont, labelBounds, "row" + index + "-label");
            if (shown != label) AddTooltip(compo, label, labelBounds, "row" + index + "-full");
        }

        /// <summary>The one place a rule is written: a flat rule, or one value of a variant rule.</summary>
        private double AddEditCard(GuiComposer compo, double y, List<Action> afterCompose)
        {
            double height = 18 + InputHeight;
            double inner = Frame(compo, y, height, T("edit-card"), "edit");

            double x = CardPadding;
            (string key, string caption, double width, Func<string> get, Action<string> set, string tip)[] fields =
            {
                ("edit-code", T("col-code"), 300, () => editCode, v => editCode = v ?? "", T("col-code-hint")),
                ("edit-key", T("col-key"), 100, () => editKey, v => editKey = v ?? "", T("col-key-hint")),
                ("edit-value", T("col-value"), 100, () => editValue, v => editValue = v ?? "", T("col-value-hint")),
                ("edit-tiers", T("col-tiers"), 100, () => editTiers, v => editTiers = v ?? "", T("col-tiers-hint")),
            };

            foreach (var field in fields)
            {
                var captionBounds = ElementBounds.Fixed(x, inner, field.width, 16);
                compo.AddStaticText(field.caption, CairoFont.WhiteDetailText().WithColor(LabelColor), captionBounds, field.key + "-caption");
                var inputBounds = ElementBounds.Fixed(x, inner + 18, field.width, InputHeight);
                compo.AddTextInput(inputBounds, field.set, CairoFont.WhiteSmallText(), field.key);
                AddTooltip(compo, field.tip, captionBounds, field.key + "-tip");

                var captured = field;
                afterCompose.Add(() => SingleComposer.GetTextInput(captured.key).SetValue(captured.get()));
                x += field.width + 8;
            }

            string saveCaption = T("save");
            compo.AddSmallButton(saveCaption, OnSave,
                ElementBounds.Fixed(DialogWidth - CardPadding - ButtonWidth(saveCaption), inner + 18, ButtonWidth(saveCaption), InputHeight),
                EnumButtonStyle.Normal, "edit-save");

            return y + CardHeaderHeight + CardPadding * 2 + height + CardGap;
        }

        private bool LoadIntoEdit(SocketRow row)
        {
            pendingConfirm = null;
            editCode = row.Code ?? "";
            editKey = row.IsVariantHeader || row.IsVariantValue ? row.Key ?? "" : "";
            editValue = row.IsVariantValue ? row.Value ?? "" : "";
            editTiers = row.IsVariantHeader ? "" : string.Join(",", row.Tiers ?? new int[0]);
            Compose();
            return true;
        }

        private bool OnSave()
        {
            pendingConfirm = null;
            if (!TryParseTiers(editTiers, out int[] tiers))
            {
                SetMessage(T("msg-bad-tiers"), true);
                Compose();
                return true;
            }

            // Key and value together make it a variant rule; neither, a flat one. One without the
            // other is a half-filled form, not a guess to be made.
            bool hasKey = !string.IsNullOrWhiteSpace(editKey);
            bool hasValue = !string.IsNullOrWhiteSpace(editValue);
            if (hasValue)
            {
                Send(new AdminConfigEditPacket
                {
                    Op = AdminConfigOp.SetVariantValue, Code = editCode, AttributeKey = editKey, VariantValue = editValue, Tiers = tiers
                });
            }
            else if (hasKey)
            {
                SetMessage(T("msg-no-value-for-key"), true);
                Compose();
            }
            else
            {
                Send(new AdminConfigEditPacket { Op = AdminConfigOp.SetSocketRule, Code = editCode, Tiers = tiers });
            }
            return true;
        }

        private static bool TryParseTiers(string text, out int[] tiers)
        {
            tiers = new int[0];
            if (string.IsNullOrWhiteSpace(text)) return true;

            var parsed = new List<int>();
            foreach (string part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int tier) || tier < 1 || tier > AdminConfigService.MaxTier) return false;
                parsed.Add(tier);
            }
            if (parsed.Count > AdminConfigService.MaxSockets) return false;
            tiers = parsed.ToArray();
            return true;
        }

        // ---------------------------------------------------------------- gems tab

        private double AddGemsTab(GuiComposer compo, List<Action> afterCompose)
        {
            var cfg = canjewelry.config;
            string[] gemTypes = cfg.buffNameToPossibleItem.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            int rowsOfGems = Math.Max(1, (gemTypes.Length + GemsPerRow - 1) / GemsPerRow);
            double height = 48 + rowsOfGems * GemCellHeight;

            double y = ContentY;
            string title = selectedItem == null ? T("gems-card-none") : T("gems-card", selectedItem.GetName());
            double inner = Frame(compo, y, height, title, "gems");

            if (selectedItem == null)
            {
                compo.AddStaticText(T("gems-none"), CairoFont.WhiteSmallText().WithColor(LabelColor),
                    ElementBounds.Fixed(CardPadding, inner, DialogWidth - CardPadding * 2, 40), "gems-empty");
                return y + CardHeaderHeight + CardPadding * 2 + height + CardGap;
            }

            string path = selectedItem.Collectible.Code.Path;
            compo.AddInteractiveElement(new GuiElementItemIcon(capi, ElementBounds.Fixed(CardPadding, inner, 40, 40), selectedItem), "gems-item");
            compo.AddStaticText(selectedItem.Collectible.Code.ToString(), CairoFont.WhiteSmallText().WithColor(ValueColor),
                ElementBounds.Fixed(CardPadding + 48, inner + 2, 420, 20), "gems-code");
            compo.AddStaticText(T("gems-will-add", AdminConfigService.FragmentFor(path)), CairoFont.WhiteDetailText().WithColor(LabelColor),
                ElementBounds.Fixed(CardPadding + 48, inner + 22, 600, 18), "gems-fragment");

            double gridY = inner + 48;
            for (int i = 0; i < gemTypes.Length; i++)
            {
                string gem = gemTypes[i];
                double cx = CardPadding + (i % GemsPerRow) * GemCellWidth;
                double cy = gridY + (i / GemsPerRow) * GemCellHeight;

                var fragments = AdminConfigService.MatchingFragments(cfg.buffNameToPossibleItem[gem], path);
                bool fits = EncrustableCB.canItemContainThisGem(gem, selectedItem);
                // Fits without any fragment of the config: the item's own JSON lets it in.
                bool viaItem = fits && fragments.Count == 0;

                Item gemItem = capi.World.GetItem(new AssetLocation("canjewelry:gem-cut-normal-" + gem))
                               ?? capi.World.GetItem(new AssetLocation("canjewelry:gem-rough-normal-" + gem));
                var iconBounds = ElementBounds.Fixed(cx + (GemCellWidth - 40) / 2 - 4, cy, 40, 40);
                if (gemItem != null) compo.AddInteractiveElement(new GuiElementItemIcon(capi, iconBounds, new ItemStack(gemItem)), "gem-icon-" + gem);

                var nameFont = CairoFont.WhiteDetailText().WithColor(fits ? SuccessColor : LabelColor).WithOrientation(EnumTextOrientation.Center);
                compo.AddStaticText(Fit(gem, nameFont, GemCellWidth - 8), nameFont,
                    ElementBounds.Fixed(cx, cy + 40, GemCellWidth - 8, 16), "gem-name-" + gem);

                var switchBounds = ElementBounds.Fixed(cx + (GemCellWidth - 30) / 2 - 4, cy + 56, 30, 30);
                string captured = gem;
                compo.AddSwitch(on => OnGemToggled(captured, on), switchBounds, "gem-switch-" + gem, 24);
                AddTooltip(compo, viaItem ? T("gems-via-item")
                    : fragments.Count > 0 ? T("gems-via", string.Join(", ", fragments))
                    : T("gems-off"), switchBounds, "gem-tip-" + gem);

                afterCompose.Add(() => SingleComposer.GetSwitch("gem-switch-" + captured).SetValue(fits));
            }

            return y + CardHeaderHeight + CardPadding * 2 + height + CardGap;
        }

        private void OnGemToggled(string gem, bool on)
        {
            pendingConfirm = null;
            if (selectedItem == null) return;
            Send(new AdminConfigEditPacket
            {
                Op = AdminConfigOp.SetGemForItem, GemType = gem, Code = selectedItem.Collectible.Code.ToString(), Allowed = on
            });
        }

        // ---------------------------------------------------------------- missing tab

        private double AddMissingTab(GuiComposer compo)
        {
            string f = filter.Trim().ToLowerInvariant();
            var rows = missing.Where(m => f.Length == 0 || (m.Subject ?? "").ToLowerInvariant().Contains(f)).ToList();
            int pages = Math.Max(1, (rows.Count + PageSize - 1) / PageSize);
            missingPage = Math.Clamp(missingPage, 0, pages - 1);

            double height = 40 + PageSize * RowHeight + 6 + InputHeight;
            double y = ContentY;
            double inner = Frame(compo, y, height, T("missing-card", missing.Count), "missing");

            compo.AddStaticText(T("missing-hint"), CairoFont.WhiteDetailText().WithColor(LabelColor),
                ElementBounds.Fixed(CardPadding, inner, DialogWidth - 200, 34), "missing-hint");
            if (missing.Count > 0)
            {
                string allCaption = ConfirmCaption("add-all", "add-all", missing.Count);
                double w = ButtonWidth(allCaption);
                compo.AddSmallButton(allCaption, () => Confirmed("add-all",
                    () => Send(new AdminConfigEditPacket { Op = AdminConfigOp.AddAllMissing })),
                    ElementBounds.Fixed(DialogWidth - CardPadding - w, inner, w, InputHeight), EnumButtonStyle.Normal, "missing-all");
            }

            double listY = inner + 40;
            if (rows.Count == 0)
            {
                compo.AddStaticText(T("missing-none"), CairoFont.WhiteSmallText().WithColor(SuccessColor),
                    ElementBounds.Fixed(CardPadding, listY, DialogWidth - CardPadding * 2, RowHeight), "missing-empty");
            }

            var page = rows.Skip(missingPage * PageSize).Take(PageSize).ToList();
            for (int i = 0; i < page.Count; i++)
            {
                var entry = page[i];
                double rowY = listY + i * RowHeight;

                string addCaption = T("add");
                double w = ButtonWidth(addCaption, small: true);
                double right = DialogWidth - CardPadding - w;
                string id = entry.Id;
                compo.AddSmallButton(addCaption, () => Send(new AdminConfigEditPacket { Op = AdminConfigOp.AddMissing, Ids = new List<string> { id } }),
                    ElementBounds.Fixed(right, rowY, w, SmallButtonHeight), EnumButtonStyle.Small, "missing" + i + "-add");

                const double kindWidth = 120;
                compo.AddStaticText(T("kind-" + entry.Kind), CairoFont.WhiteSmallText().WithColor(LabelColor),
                    ElementBounds.Fixed(CardPadding, rowY + 2, kindWidth, RowHeight), "missing" + i + "-kind");

                double subjectWidth = 250;
                var subjectFont = CairoFont.WhiteSmallText();
                string subject = entry.Subject ?? "";
                var subjectBounds = ElementBounds.Fixed(CardPadding + kindWidth, rowY + 2, subjectWidth, RowHeight);
                compo.AddStaticText(Fit(subject, subjectFont, subjectWidth - 6), subjectFont, subjectBounds, "missing" + i + "-subject");

                string detail = entry.Kind == AdminConfigService.KindGem ? T("kind-gem-detail", entry.Detail) : entry.Detail ?? "";
                double detailWidth = right - 8 - (CardPadding + kindWidth + subjectWidth);
                var detailFont = CairoFont.WhiteSmallText().WithColor(ValueColor);
                var detailBounds = ElementBounds.Fixed(CardPadding + kindWidth + subjectWidth, rowY + 2, detailWidth, RowHeight);
                string shown = Fit(detail, detailFont, detailWidth);
                compo.AddStaticText(shown, detailFont, detailBounds, "missing" + i + "-detail");
                if (shown != detail || Fit(subject, subjectFont, subjectWidth - 6) != subject)
                {
                    AddTooltip(compo, subject + ": " + detail, detailBounds, "missing" + i + "-tip");
                }
            }
            AddPager(compo, listY + PageSize * RowHeight + 6, missingPage, pages, p => missingPage = p, "missing");

            return y + CardHeaderHeight + CardPadding * 2 + height + CardGap;
        }

        // ================================================================ actions

        private void OnTabClicked(int tab)
        {
            currentTab = tab;
            pendingConfirm = null;
            Compose();
        }

        /// <summary>
        /// Takes the held item: it becomes the item of the gems tab, and on the sockets tab the rule
        /// that gives it its sockets is looked up - or, when there is none, a new one is prefilled.
        /// </summary>
        private bool OnFromHand()
        {
            pendingConfirm = null;
            ItemStack held = capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held?.Item == null)
            {
                SetMessage(T("msg-hold-item"), true);
                Compose();
                return true;
            }

            selectedItem = held.Clone();
            selectedItem.StackSize = 1;

            if (currentTab == TabSockets)
            {
                var cfg = canjewelry.config;
                var variant = cfg.custom_variants_sockets_tiers.FirstOrDefault(r => r?.ItemCode != null && Matches(r.ItemCode, held.Item.Code));
                string flat = variant == null ? cfg.items_codes_with_socket_count_and_tiers.Keys.FirstOrDefault(k => Matches(k, held.Item.Code)) : null;

                if (variant != null || flat != null)
                {
                    filter = variant?.ItemCode ?? flat;
                    socketsPage = 0;
                    if (variant != null)
                    {
                        string value = EncrustableCB.ResolveVariantValue(held, variant.AttributeKey) ?? "*";
                        editCode = variant.ItemCode;
                        editKey = variant.AttributeKey;
                        editValue = value;
                        editTiers = variant.SocketTiers != null && variant.SocketTiers.TryGetValue(value, out var t) ? string.Join(",", t) : "";
                    }
                    else
                    {
                        editCode = flat;
                        editKey = editValue = "";
                        editTiers = string.Join(",", cfg.items_codes_with_socket_count_and_tiers[flat] ?? new int[0]);
                    }
                    SetMessage(T("msg-rule-found", filter), false);
                }
                else
                {
                    // Domain plus the first part of the code: the whole family, which is what a rule
                    // is usually written for. The admin can narrow it before saving.
                    editCode = held.Item.Code.Domain + ":" + AdminConfigService.FragmentFor(held.Item.Code.Path) + "-*";
                    editKey = editValue = "";
                    editTiers = "1";
                    SetMessage(T("msg-rule-new"), false);
                }
            }
            else if (currentTab == TabMissing)
            {
                filter = AdminConfigService.FragmentFor(held.Item.Code.Path);
                missingPage = 0;
            }

            Compose();
            return true;
        }

        /// <summary>What a rule's code covers: the same wildcard match SearchItems uses to apply it.</summary>
        private static bool Matches(string ruleCode, AssetLocation itemCode)
        {
            try
            {
                return WildcardUtil.Match(new AssetLocation(ruleCode), itemCode);
            }
            catch (Exception)
            {
                // A hand-written key the matcher cannot read simply covers nothing.
                return false;
            }
        }

        /// <summary>The gems tab needs a concrete item; a wildcard rule lends it its first match.</summary>
        private bool SelectRuleItem(string ruleCode)
        {
            pendingConfirm = null;
            Item item = null;
            try
            {
                item = capi.World.SearchItems(new AssetLocation(ruleCode)).FirstOrDefault();
            }
            catch (Exception)
            {
            }

            if (item == null)
            {
                SetMessage(T("msg-no-items-for-rule", ruleCode), true);
                Compose();
                return true;
            }

            selectedItem = new ItemStack(item);
            currentTab = TabGems;
            Compose();
            return true;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the second click on the same thing. One click on
        /// "disable" or "add all" is too easy to make by accident on a live server.
        /// </summary>
        private bool Confirmed(string key, Action action)
        {
            if (pendingConfirm == key)
            {
                pendingConfirm = null;
                action();
                return true;
            }
            pendingConfirm = key;
            SetMessage(T("confirm-again"), false);
            Compose();
            return true;
        }

        private string ConfirmCaption(string langKey, string confirmKey, params object[] args)
            => pendingConfirm == confirmKey ? T("confirm") : T(langKey, args);

        private bool Send(AdminConfigEditPacket packet)
        {
            canjewelry.clientChannel?.SendPacket(packet);
            return true;
        }

        private void SetMessage(string text, bool isError)
        {
            message = text;
            messageIsError = isError;
        }

        // ================================================================ widgets

        /// <summary>
        /// A framed card with a heading and a rule under it, as on the claims pages. Returns the y
        /// its content starts at.
        /// </summary>
        private static double Frame(GuiComposer compo, double y, double contentHeight, string title, string key)
        {
            double height = CardHeaderHeight + contentHeight + CardPadding * 2;
            compo.AddInset(ElementBounds.Fixed(0, y, DialogWidth, height), 2);

            var headerFont = CairoFont.WhiteSmallishText().WithColor(SectionColor);
            compo.AddStaticText(Fit(title, headerFont, DialogWidth - CardPadding * 2), headerFont,
                ElementBounds.Fixed(CardPadding, y + CardPadding, DialogWidth - CardPadding * 2, 20), key + "-title");
            compo.AddInset(ElementBounds.Fixed(CardPadding, y + CardPadding + 25, DialogWidth - CardPadding * 2, 2));

            return y + CardPadding + CardHeaderHeight;
        }

        private void AddPager(GuiComposer compo, double y, int page, int pages, Action<int> setPage, string key)
        {
            if (pages <= 1) return;

            double center = DialogWidth / 2;
            compo.AddSmallButton("<", () =>
            {
                setPage(Math.Max(0, page - 1));
                pendingConfirm = null;
                Compose();
                return true;
            }, ElementBounds.Fixed(center - 90, y, 30, InputHeight), EnumButtonStyle.Normal, key + "-prev");

            compo.AddStaticText(T("page", page + 1, pages),
                CairoFont.WhiteSmallText().WithColor(LabelColor).WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(center - 55, y + 5, 110, 20), key + "-page");

            compo.AddSmallButton(">", () =>
            {
                setPage(Math.Min(pages - 1, page + 1));
                pendingConfirm = null;
                Compose();
                return true;
            }, ElementBounds.Fixed(center + 60, y, 30, InputHeight), EnumButtonStyle.Normal, key + "-next");
        }

        /// <summary>Hover text sized to its own content, as the claims Tooltip widget does it.</summary>
        private static void AddTooltip(GuiComposer compo, string text, ElementBounds bounds, string key)
        {
            if (string.IsNullOrEmpty(text)) return;
            CairoFont font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);
            int width = (int)Math.Min(font.GetTextExtents(text).Width + 16, 360);
            compo.AddHoverText(text, font, width, bounds, key);
        }

        /// <summary>Shortens a caption with an ellipsis until it fits; static text would wrap instead.</summary>
        private static string Fit(string text, CairoFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text) || font.GetTextExtents(text).Width <= maxWidth) return text;

            string shortened = text;
            while (shortened.Length > 1 && font.GetTextExtents(shortened + "...").Width > maxWidth)
            {
                shortened = shortened.Substring(0, shortened.Length - 1);
            }
            return shortened + "...";
        }

        private static double ButtonWidth(string caption, bool small = false)
        {
            var font = small ? CairoFont.SmallButtonText() : CairoFont.ButtonText();
            return Math.Ceiling(font.GetTextExtents(caption).Width / RuntimeEnv.GUIScale) + 24;
        }

        private static string T(string key, params object[] args) => Lang.Get("canjewelry:adminconfig-" + key, args);
    }
}
