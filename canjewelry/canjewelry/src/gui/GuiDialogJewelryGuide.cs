using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canjewelry.src.gui
{
    // The jewelry guide. Content is built as VTML, which gives colours and line breaks for free,
    // so what would be tables becomes formatted text with item icons mixed in.
    public class GuiDialogJewelryGuide : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "canjewelryguide";

        // Wide enough for the five tab captions to fit on one row inside the frame.
        private const int DialogWidth = 800;
        private const int DialogHeight = 500;
        private const int TabsHeight = 30;

        // Tier colours, matching the socket colours used elsewhere in the mod.
        private const string ColTier1 = "#2FE147";
        private const string ColTier2 = "#5A7BFF";
        private const string ColTier3 = "#B45CE0";
        private const string ColHeader = "#FFD98A";

        private int currentTab;
        private readonly Dictionary<int, string> tierMetals = new Dictionary<int, string>();
        private readonly Dictionary<int, string> tierSizes = new Dictionary<int, string>();

        public GuiDialogJewelryGuide(ICoreClientAPI capi) : base(capi) { }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            // Rebuilt on every open: config and installed companion mods can change between opens.
            tierMetals.Clear();
            tierSizes.Clear();
            LoadTierMetals();
            LoadTierSizes();
            ComposeDialog();
        }

        private void ComposeDialog()
        {
            string[] tabNames =
            {
                G("tab-getting-started"), G("tab-jewelry-sockets"), G("tab-gems-buffs"),
                G("tab-cutting-styles"), G("tab-panning"),
            };
            GuiTab[] tabs = new GuiTab[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++) tabs[i] = new GuiTab { Name = tabNames[i], DataInt = i };

            // Inside the frame rather than above it: hung off the top edge the row overlapped the
            // title bar and the last tab fell outside the dialog.
            ElementBounds tabBounds = ElementBounds.Fixed(0, 26, DialogWidth, TabsHeight);
            ElementBounds textBounds = ElementBounds.Fixed(0, TabsHeight + 44, DialogWidth - 26, DialogHeight);

            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7).WithFixedWidth(20);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(tabBounds, insetBounds, clipBounds, scrollbarBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui.CreateCompo("canjewelryguide", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(G("title"), OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .AddHorizontalTabs(tabs, tabBounds, OnTabClicked, CairoFont.WhiteSmallishText(), CairoFont.WhiteSmallishText().WithColor(GuiStyle.ActiveButtonTextColor), "tabs")
                    .AddInset(insetBounds, 3)
                    .BeginClip(clipBounds)
                        .AddRichtext(BuildTabComponents(currentTab), textBounds, "content")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .EndChildElements()
                .Compose();

            SingleComposer.GetHorizontalTabs("tabs").activeElement = currentTab;

            // The scrollbar has to be told how tall the content turned out to be.
            GuiElementRichtext content = SingleComposer.GetRichtext("content");
            SingleComposer.GetScrollbar("scrollbar").SetHeights(DialogHeight, (float)Math.Max(content.TotalHeight, DialogHeight));
        }

        private void OnTabClicked(int index)
        {
            currentTab = index;
            ComposeDialog();
        }

        private void OnScroll(float value)
        {
            GuiElementRichtext content = SingleComposer.GetRichtext("content");
            content.Bounds.fixedY = 0 - value;
            content.Bounds.CalcWorldBounds();
        }

        private void OnTitleBarClose() => TryClose();

        // ── content ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Most tabs are plain vtml; the gem tab mixes in item icons, which vtml cannot express,
        /// so it assembles its components itself.
        /// </summary>
        private RichTextComponentBase[] BuildTabComponents(int tab)
        {
            if (tab == 1) return BuildJewelryComponents();
            if (tab == 2) return BuildGemsComponents();
            if (tab == 4) return BuildPanningComponents();

            string vtml = tab == 3 ? BuildCuttingTab() : BuildGettingStartedTab();
            return VtmlUtil.Richtextify(capi, vtml, CairoFont.WhiteDetailText());
        }

        private RichTextComponentBase[] BuildGemsComponents()
        {
            var cfg = canjewelry.config;
            var components = new List<RichTextComponentBase>();
            CairoFont font = CairoFont.WhiteDetailText();

            void Text(string vtml) => components.AddRange(VtmlUtil.Richtextify(capi, vtml, font));

            if (cfg == null)
            {
                Text(VtmlEscape(G("gems-intro")));
                return components.ToArray();
            }

            Text(VtmlEscape(G("gems-intro")) + "<br><br>");

            foreach (var kv in cfg.PossibleGemBuffs)
            {
                // Gem types whose items do not exist in this world come from companion mods that
                // are not installed - listing them would promise gems the player cannot obtain.
                Item gemItem = capi.World.GetItem(new AssetLocation("canjewelry:gem-cut-normal-" + kv.Key))
                               ?? capi.World.GetItem(new AssetLocation("canjewelry:gem-rough-normal-" + kv.Key));
                if (gemItem == null) continue;

                // The icon doubles as the gem's tooltip: hovering it shows the usual item info.
                components.Add(new ItemstackTextComponent(capi, new ItemStack(gemItem), 32, 6, EnumFloat.Inline));

                Text("<font color=\"" + ColHeader + "\"><strong>" + VtmlEscape(GemName(kv.Key)) + "</strong></font><br>");

                foreach (string statName in kv.Value)
                {
                    if (!cfg.BuffAttributesDict.TryGetValue(statName, out var attributes))
                    {
                        Text("  " + VtmlEscape(statName) + "<br>");
                        continue;
                    }

                    Text("  " + VtmlEscape(statName)
                        + "   <font color=\"" + ColTier1 + "\">T1 " + FormatRange(attributes.MainStatValueRange, 1) + "</font>"
                        + "   <font color=\"" + ColTier2 + "\">T2 " + FormatRange(attributes.MainStatValueRange, 2) + "</font>"
                        + "   <font color=\"" + ColTier3 + "\">T3 " + FormatRange(attributes.MainStatValueRange, 3) + "</font><br>");

                    if (attributes.PossibleSecondaryStats == null || attributes.PossibleSecondaryStats.Count == 0) continue;

                    // The old dialog hid these ranges behind a hover tooltip; here they are simply
                    // printed, since a static dialog has nowhere to hover.
                    Text("  " + VtmlEscape(G("col-secondary")) + ": " + VtmlEscape(string.Join(", ", attributes.PossibleSecondaryStats)) + "<br>"
                        + "  " + VtmlEscape(G("gems-secondary-range"))
                        + " <font color=\"" + ColTier1 + "\">" + FormatRange(attributes.SecondaryStatValueRange, 1) + "</font>"
                        + " / <font color=\"" + ColTier2 + "\">" + FormatRange(attributes.SecondaryStatValueRange, 2) + "</font>"
                        + " / <font color=\"" + ColTier3 + "\">" + FormatRange(attributes.SecondaryStatValueRange, 3) + "</font><br>");
                }
                Text("<br>");
            }

            Text(VtmlEscape(G("gems-note-negative")));
            return components.ToArray();
        }

        private string BuildGettingStartedTab()
        {
            var sb = new StringBuilder();
            Header(sb, G("overview-title"));
            Body(sb, G("overview-body"));

            for (int step = 1; step <= 6; step++)
            {
                Header(sb, G("step-" + step + "-title"));
                Body(sb, step == 4
                    ? Lang.Get("canjewelry:guide-step-4-body", TierMetals(1), TierMetals(2), TierMetals(3))
                    : G("step-" + step + "-body"));
            }

            Header(sb, G("section-gem-size-vs-tier"));
            Line(sb, Lang.Get("canjewelry:guide-tab0-tier-hint-1", TierMetals(1)), ColTier1);
            Line(sb, Lang.Get("canjewelry:guide-tab0-tier-hint-2", TierMetals(2)), ColTier2);
            Line(sb, Lang.Get("canjewelry:guide-tab0-tier-hint-3", TierMetals(3)), ColTier3);
            return sb.ToString();
        }

        private RichTextComponentBase[] BuildJewelryComponents()
        {
            var cfg = canjewelry.config;
            var components = new List<RichTextComponentBase>();
            CairoFont font = CairoFont.WhiteDetailText();

            void Text(string vtml) => components.AddRange(VtmlUtil.Richtextify(capi, vtml, font));
            void Icon(Item item) => components.Add(new ItemstackTextComponent(capi, new ItemStack(item), 32, 6, EnumFloat.Inline));

            Text("<font color=\"" + ColHeader + "\"><strong>" + VtmlEscape(G("section-simple-jewelry")) + "</strong></font><br>"
                // Without this the rows read as "Ring: 1 - 1" and the numbers mean nothing.
                + "<font color=\"#B0B0B0\">" + VtmlEscape(G("col-name") + " - " + G("col-sockets") + " - " + G("col-slot-tiers")) + "</font><br>");

            // Metal-scaled pieces are listed separately below, since their sockets vary by metal.
            var customCodes = new HashSet<string>();
            if (cfg != null)
            {
                foreach (var cvst in cfg.custom_variants_sockets_tiers) customCodes.Add(ItemKey(cvst.ItemCode));
            }

            var rows = new List<(Item Item, string Label, int Sockets, int[] Tiers)>();
            var seen = new HashSet<string>();
            foreach (Item item in capi.World.Items)
            {
                if (item?.Code == null || item.Code.Domain != "canjewelry" || item.Attributes == null) continue;

                int sockets = item.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt(0);
                if (sockets <= 0) continue;

                string key = item.Code.FirstCodePart();
                if (customCodes.Contains(key) || !seen.Add(key)) continue;

                int[] tiers = item.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray<int>(null);
                if (tiers == null || tiers.Length == 0)
                {
                    tiers = new int[sockets];
                    for (int i = 0; i < sockets; i++) tiers[i] = 1;
                }
                rows.Add((item, PrettyItemLabel(item.Code.ToString()), sockets, tiers));
            }
            rows.Sort((a, b) =>
            {
                int c = a.Sockets.CompareTo(b.Sockets);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var row in rows)
            {
                Icon(row.Item);
                Text(VtmlEscape(row.Label + ": " + row.Sockets + " - " + FormatTiers(row.Tiers)) + "<br>");
            }

            if (cfg != null && cfg.custom_variants_sockets_tiers.Count > 0)
            {
                Text("<br><font color=\"" + ColHeader + "\"><strong>" + VtmlEscape(G("section-metal-scaled-jewelry")) + "</strong></font><br>"
                    + "<font color=\"#B0B0B0\">" + VtmlEscape(G("col-socket-metal") + " - " + G("col-sockets") + " - " + G("col-slot-tiers")) + "</font><br>");

                foreach (var cvst in cfg.custom_variants_sockets_tiers)
                {
                    // The config code carries wildcards, so the icon comes from any resolved variant.
                    Item[] variants = capi.World.SearchItems(new AssetLocation(cvst.ItemCode + "-*"));
                    if (variants != null && variants.Length > 0) Icon(variants[0]);

                    Text("<font color=\"" + ColHeader + "\">" + VtmlEscape(PrettyItemLabel(cvst.ItemCode)) + "</font><br>");
                    if (cvst.SocketTiers == null) continue;

                    foreach (var kv in cvst.SocketTiers)
                    {
                        Text(VtmlEscape("  " + Lang.Get("canjewelry:guide-cvst-row",
                            Lang.Get("game:material-" + kv.Key).Trim(), kv.Value.Length, FormatTiers(kv.Value))) + "<br>");
                    }
                }
            }

            Text("<br><font color=\"" + ColTier1 + "\">" + VtmlEscape(Lang.Get("canjewelry:guide-tier-hint-1", TierMetals(1))) + "</font><br>"
                + "<font color=\"" + ColTier2 + "\">" + VtmlEscape(Lang.Get("canjewelry:guide-tier-hint-2", TierMetals(2))) + "</font><br>"
                + "<font color=\"" + ColTier3 + "\">" + VtmlEscape(Lang.Get("canjewelry:guide-tier-hint-3", TierMetals(3))) + "</font><br>");

            return components.ToArray();
        }

        private string BuildCuttingTab()
        {
            var sb = new StringBuilder();

            Line(sb, G("cut-round-title"), ColTier1);
            Body(sb, G("cut-round-desc"));
            Line(sb, G("cut-baguette-title"), ColTier2);
            Body(sb, G("cut-baguette-desc"));
            Line(sb, G("cut-pear-title"), "#FFB366");
            Body(sb, G("cut-pear-desc"));

            Header(sb, G("section-gem-size-reminder"));
            var carats = canjewelry.config?.GemCarats;
            for (int tier = 1; tier <= 3; tier++)
            {
                Line(sb, TierSize(tier) + " - " + TierMetals(tier) + " - " + G("col-tier-" + tier) + CaratsOfSize(carats, tier),
                    tier == 3 ? ColTier3 : (tier == 2 ? ColTier2 : ColTier1));
            }
            if (carats != null) Body(sb, G("carats-hint"));
            return sb.ToString();
        }

        // Matches "@(ore|crystalizedore)-{quality}-{ore}-.*" keys of the panning table.
        private static readonly Regex PanKeyRegex = new Regex(@"^@\([^)]+\)-(\w+)-(.+?)-\.\*$", RegexOptions.Compiled);
        private static readonly string[] QualityOrder = { "bountiful", "rich", "medium", "poor" };

        private RichTextComponentBase[] BuildPanningComponents()
        {
            var components = new List<RichTextComponentBase>();
            CairoFont font = CairoFont.WhiteDetailText();
            void Text(string vtml) => components.AddRange(VtmlUtil.Richtextify(capi, vtml, font));

            Text(VtmlEscape(G("panning-intro")) + "<br><br>");

            var drops = canjewelry.config?.panningDrops;
            if (drops == null || drops.Count == 0)
            {
                Text(VtmlEscape(G("panning-no-data")));
                return components.ToArray();
            }

            var byOre = new Dictionary<string, Dictionary<string, utils.CANPanningDrop[]>>(StringComparer.OrdinalIgnoreCase);
            var special = new List<KeyValuePair<string, utils.CANPanningDrop[]>>();

            foreach (var kv in drops)
            {
                Match m = PanKeyRegex.Match(kv.Key);
                if (!m.Success)
                {
                    special.Add(kv);
                    continue;
                }
                string oreName = m.Groups[2].Value;
                if (!byOre.TryGetValue(oreName, out var qualities))
                {
                    byOre[oreName] = qualities = new Dictionary<string, utils.CANPanningDrop[]>(StringComparer.OrdinalIgnoreCase);
                }
                qualities[m.Groups[1].Value] = kv.Value;
            }

            foreach (var ore in byOre.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
            {
                // Rows are built first: an ore whose every drop was filtered out is skipped whole,
                // heading and icon included.
                var rows = new List<(string Quality, utils.CANPanningDrop[] Drops)>();
                foreach (string quality in QualityOrder)
                {
                    if (!ore.Value.TryGetValue(quality, out var qualityDrops)) continue;
                    if (!qualityDrops.Any(d => d?.Code != null && capi.World.GetItem(d.Code) != null)) continue;
                    rows.Add((quality, qualityDrops));
                }
                if (rows.Count == 0) continue;

                AddOreIcon(components, ore.Key);
                Text("<font color=\"" + ColHeader + "\"><strong>" + VtmlEscape(OreName(ore.Key)) + "</strong></font><br>");

                foreach (var row in rows)
                {
                    Text(VtmlEscape("  " + PanningQualityName(row.Quality) + ": ") );
                    AddDropIcons(components, row.Drops, font);
                    Text("<br>");
                }
                Text("<br>");
            }

            foreach (var kv in special)
            {
                if (!kv.Value.Any(d => d?.Code != null && capi.World.GetItem(d.Code) != null)) continue;

                Text("<font color=\"" + ColHeader + "\">" + VtmlEscape(kv.Key) + "</font><br>  ");
                AddDropIcons(components, kv.Value, font);
                Text("<br><br>");
            }

            return components.ToArray();
        }

        /// <summary>
        /// Icon for the ore material. What actually goes into the pan is the chunk item, not the
        /// ore block, so items are looked up first and the block is only a fallback for ores that
        /// have no chunk of their own.
        /// </summary>
        private void AddOreIcon(List<RichTextComponentBase> components, string ore)
        {
            Item[] chunks = capi.World.SearchItems(new AssetLocation("game:ore-*-" + ore + "-*"));
            if (chunks != null && chunks.Length > 0)
            {
                components.Add(new ItemstackTextComponent(capi, new ItemStack(chunks[0]), 28, 4, EnumFloat.Inline));
                return;
            }

            Item[] nuggets = capi.World.SearchItems(new AssetLocation("game:nugget-" + ore));
            if (nuggets != null && nuggets.Length > 0)
            {
                components.Add(new ItemstackTextComponent(capi, new ItemStack(nuggets[0]), 28, 4, EnumFloat.Inline));
                return;
            }

            Block[] blocks = capi.World.SearchBlocks(new AssetLocation("game:ore-rich-" + ore + "-*"));
            if (blocks == null || blocks.Length == 0) blocks = capi.World.SearchBlocks(new AssetLocation("game:ore-poor-" + ore + "-*"));
            if (blocks != null && blocks.Length > 0)
            {
                components.Add(new ItemstackTextComponent(capi, new ItemStack(blocks[0]), 28, 4, EnumFloat.Inline));
            }
        }

        private void AddDropIcons(List<RichTextComponentBase> components, utils.CANPanningDrop[] drops, CairoFont font)
        {
            foreach (var drop in drops)
            {
                if (drop?.Code == null) continue;
                Item item = capi.World.GetItem(drop.Code);
                if (item == null) continue;

                components.Add(new ItemstackTextComponent(capi, new ItemStack(item), 24, 2, EnumFloat.Inline));
                components.AddRange(VtmlUtil.Richtextify(capi,
                    Math.Round((drop.Chance?.avg ?? 0) * 100f, 1) + "%  ", font));
            }
        }

        // ── formatting helpers ───────────────────────────────────────────────────

        private static void Header(StringBuilder sb, string text)
        {
            sb.Append("<br><font color=\"").Append(ColHeader).Append("\"><strong>")
              .Append(VtmlEscape(text)).Append("</strong></font><br>");
        }

        private static void Body(StringBuilder sb, string text, bool spaceAfter = true)
        {
            sb.Append(VtmlEscape(text)).Append("<br>");
            if (spaceAfter) sb.Append("<br>");
        }

        private static void Line(StringBuilder sb, string text, string color)
        {
            sb.Append("<font color=\"").Append(color).Append("\">").Append(VtmlEscape(text)).Append("</font><br>");
        }

        // Guide text comes from lang files and may legitimately contain angle brackets, which
        // vtml would otherwise read as markup.
        private static string VtmlEscape(string text) => text?.Replace("<", "&lt;").Replace(">", "&gt;") ?? "";

        private static string FormatTiers(int[] tiers) => tiers == null ? "" : string.Join(", ", tiers);

        private static string FormatRange(Dictionary<int, float[]> ranges, int tier)
        {
            if (ranges == null || !ranges.TryGetValue(tier, out float[] range) || range.Length == 0) return "-";
            if (range.Length == 1) return Math.Round(range[0], 3).ToString();
            return Math.Round(range[0], 3) + ".." + Math.Round(range[1], 3);
        }

        /// <summary>" · 1.6–2.5 ct, socket holds 2.5 ct": the weights of a cut size and what its socket tier holds.</summary>
        private static string CaratsOfSize(Config.GemCaratsConfig cfg, int size)
        {
            if (cfg == null) return "";
            float lo = cfg.BucketBounds.TryGetValue(size - 1, out float below) ? below + cfg.Step : cfg.Step;
            float hi = cfg.BucketBounds.TryGetValue(size, out float above) ? above : cfg.MaxCutCarats;
            return Lang.Get("canjewelry:guide-carats-range", cb.CANGemAppraisalCB.FormatCarats(lo), cb.CANGemAppraisalCB.FormatCarats(hi),
                cb.CANGemAppraisalCB.FormatCarats(utils.CANGemAppraisal.SocketCapacity(cfg, size)));
        }

        private string TierMetals(int tier) => tierMetals.TryGetValue(tier, out string s) ? s : "?";
        private string TierSize(int tier) => tierSizes.TryGetValue(tier, out string s) ? s : "?";

        private void LoadTierMetals()
        {
            var levels = canjewelry.config?.LevelOfSocketByType;
            if (levels == null) return;

            var byTier = new Dictionary<int, List<string>>();
            foreach (var kv in levels)
            {
                int dash = kv.Key.LastIndexOf('-');
                string metal = dash >= 0 ? kv.Key.Substring(dash + 1) : kv.Key;
                if (!byTier.TryGetValue(kv.Value, out var list)) byTier[kv.Value] = list = new List<string>();
                list.Add(Lang.Get("game:material-" + metal).Trim());
            }
            foreach (var kv in byTier) tierMetals[kv.Key] = string.Join(" / ", kv.Value);
        }

        private void LoadTierSizes()
        {
            foreach (Item item in capi.World.SearchItems(new AssetLocation("canjewelry:gem-rough-*")))
            {
                if (item?.Attributes == null) continue;
                int tier = item.Attributes["canGemType"].AsInt(0);
                if (tier <= 0 || tierSizes.ContainsKey(tier)) continue;

                string quality = item.Variant?["quality"];
                if (quality == null) continue;
                tierSizes[tier] = LangOr("canjewelry:gemsize-" + quality, quality);
            }
        }

        private static string G(string key) => Lang.Get("canjewelry:guide-" + key);

        private static string GemName(string gemType) => LangOr("canjewelry:gem-name-" + gemType, gemType);
        private static string OreName(string ore) => LangOr("canjewelry:ore-name-" + ore, ore);
        private static string PanningQualityName(string quality) => LangOr("canjewelry:panning-quality-" + quality, quality);

        /// <summary>Translation for the key, or the capitalized fallback when there is none.</summary>
        private static string LangOr(string key, string fallback)
        {
            string value = Lang.Get(key);
            return value == key ? Capitalize(fallback) : value;
        }

        private static string PrettyItemLabel(string itemCode)
        {
            int colon = itemCode.IndexOf(':');
            string domain = colon >= 0 ? itemCode.Substring(0, colon) : "canjewelry";
            string path = colon >= 0 ? itemCode.Substring(colon + 1) : itemCode;
            string baseKey = path.Split('-')[0];

            string key = domain + ":itemname-" + baseKey;
            string value = Lang.Get(key);
            if (!string.IsNullOrWhiteSpace(value) && value != key) return Capitalize(value.Trim());

            return Capitalize(baseKey.StartsWith("can") ? baseKey.Substring(3) : baseKey);
        }

        private static string ItemKey(string itemCode)
        {
            int colon = itemCode.IndexOf(':');
            string path = colon >= 0 ? itemCode.Substring(colon + 1) : itemCode;
            return path.Split('-')[0];
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
