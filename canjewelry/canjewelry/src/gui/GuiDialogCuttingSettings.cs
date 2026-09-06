using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canjewelry.src.gui
{
    // Admin side settings for the gem cutting table's accessibility options. Opened by
    // "/canjewelry cutting gui", which is what hands over the values the server currently holds -
    // the client's own config copy would do as well, but going through the server keeps the
    // dialogue honest about what it is editing.
    public class GuiDialogCuttingSettings : GuiDialog
    {
        // Opened from a command rather than a key, so there is no combination to bind.
        public override string ToggleKeyCombinationCode => null;

        private const int DialogWidth = 780;
        private const int LabelWidth = 300;
        // A slider fills its bounds, so this is what decides how precisely a value can be dragged.
        private const int ControlWidth = 440;
        private const int RowHeight = 30;
        // Sliders draw to the full height of their bounds. The row they sit in has to be advanced
        // by this instead of RowHeight, or the next row overlaps and clips the bottom of them.
        private const int SliderHeight = 45;
        private const int RowSpacing = 12;
        // Wide enough to read a two line explanation without the tooltip covering the dialogue.
        private const int HoverTextWidth = 320;

        private static readonly string[] accessModes = { "disabled", "enabled", "whitelist", "blacklist" };

        private readonly CuttingSettingsPacket settings;

        // Which list the name editor works on, and what is currently typed into it. Both survive
        // the rebuild that follows every edit.
        private bool editingBlacklist;
        private string pendingName = "";
        // Selections of the two name dropdowns, kept by name rather than index: the option lists
        // are rebuilt after every edit and an index would point at the wrong person.
        private string selectedOnlineName;
        private string selectedListName;

        public GuiDialogCuttingSettings(ICoreClientAPI capi, CuttingSettingsPacket settings) : base(capi)
        {
            this.settings = settings;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeDialog();
            RefreshValues();
        }

        private void ComposeDialog()
        {
            CairoFont labelFont = CairoFont.WhiteSmallishText();
            CairoFont hintFont = CairoFont.WhiteDetailText();

            // The intro spans the full width, so it gets its own bounds rather than a label column
            // one. Three lines of room: the text wraps and a fixed 30px row would clip it.
            ElementBounds intro = ElementBounds.Fixed(0, 30, DialogWidth, 60);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            var composer = capi.Gui.CreateCompo("canjewelrycuttingsettings", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(T("title"), OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .AddStaticText(T("description"), hintFont, EnumTextOrientation.Left, intro, "lblDescription");

            // Every row is placed from an explicit y rather than chained BelowCopy calls: the chain
            // carried the previous row's height with it, so a taller slider row silently shifted
            // and squeezed everything after it.
            double y = 100;

            // Access mode. First because everything below it is inert when this says "nobody".
            AddRowLabel(composer, labelFont, LabelAt(y), "mode");
            composer.AddDropDown(accessModes, ModeNames(), 1, OnModeChanged, ControlAt(y, RowHeight), "mode");
            y += RowHeight + RowSpacing;

            AddRowLabel(composer, labelFont, LabelAt(y), "voxels");
            composer.AddSlider(OnVoxelsChanged, ControlAt(y, SliderHeight), "voxels");
            y += SliderHeight + RowSpacing;

            AddRowLabel(composer, labelFont, LabelAt(y), "durability");
            composer.AddSwitch(OnDurabilityChanged, ControlAt(y, RowHeight), "durability");
            y += RowHeight + RowSpacing;

            AddRowLabel(composer, labelFont, LabelAt(y), "spare");
            composer.AddSwitch(OnSpareChanged, ControlAt(y, RowHeight), "spare");
            y += RowHeight + RowSpacing;

            AddRowLabel(composer, labelFont, LabelAt(y), "instant");
            composer.AddSwitch(OnInstantChanged, ControlAt(y, RowHeight), "instant");
            y += RowHeight + RowSpacing;

            AddRowLabel(composer, labelFont, LabelAt(y), "hold");
            composer.AddSlider(OnHoldChanged, ControlAt(y, SliderHeight), "hold");
            y += SliderHeight + RowSpacing * 2;

            // Which of the two name lists the editor below works on. Kept separate from the mode
            // dropdown on purpose: an admin usually wants to fill a list before switching to the
            // mode that reads it.
            AddRowLabel(composer, labelFont, LabelAt(y), "list-choice");
            composer.AddDropDown(
                new[] { "whitelist", "blacklist" },
                new[] { T("list-whitelist"), T("list-blacklist") },
                editingBlacklist ? 1 : 0, OnListChoiceChanged, ControlAt(y, RowHeight), "listchoice");
            y += RowHeight + RowSpacing;

            // Contents of the chosen list, full width - names are long and there can be several.
            ElementBounds names = ElementBounds.Fixed(0, y, DialogWidth, 50);
            y += 50 + RowSpacing;

            // Pick an online player to add. Saves typing and, more to the point, saves getting a
            // name subtly wrong - the lists match exactly, so a typo silently does nothing.
            string[] onlineCodes = OnlinePlayerNames();
            ElementBounds addPick = ElementBounds.Fixed(0, y, 440, RowHeight);
            ElementBounds addButton = ElementBounds.Fixed(455, y, 160, RowHeight);
            y += RowHeight + RowSpacing;

            // Pick someone already on the list to take off it.
            string[] listCodes = CurrentListNames();
            ElementBounds removePick = ElementBounds.Fixed(0, y, 440, RowHeight);
            ElementBounds removeButton = ElementBounds.Fixed(455, y, 185, RowHeight);
            y += RowHeight + RowSpacing;

            // Manual entry stays: offline players are not in either dropdown, and someone has to
            // be addable before they ever log in.
            ElementBounds manualLabel = ElementBounds.Fixed(0, y, 200, RowHeight);
            ElementBounds nameInput = ElementBounds.Fixed(210, y, 230, RowHeight);
            ElementBounds manualAdd = ElementBounds.Fixed(455, y, 160, RowHeight);
            ElementBounds manualRemove = ElementBounds.Fixed(625, y, 155, RowHeight);
            y += RowHeight + RowSpacing;

            ElementBounds hint = ElementBounds.Fixed(0, y, DialogWidth, RowHeight);
            y += RowHeight + RowSpacing / 2;

            ElementBounds save = ElementBounds.Fixed(0, y, 150, RowHeight).WithAlignment(EnumDialogArea.RightTop);

            SingleComposer = composer
                    .AddStaticText(DescribeCurrentList(), hintFont, EnumTextOrientation.Left, names, "lblNames")

                    .AddDropDown(onlineCodes, OnlinePlayerLabels(onlineCodes), IndexOf(onlineCodes, selectedOnlineName),
                        OnOnlinePicked, addPick, "onlinePick")
                    .AddAutoSizeHoverText(T("list-online-hover"), CairoFont.WhiteDetailText(), HoverTextWidth, addPick)
                    .AddSmallButton(T("list-add"), OnAddOnlinePressed, addButton, EnumButtonStyle.Normal, "addOnline")

                    .AddDropDown(listCodes, ListNameLabels(listCodes), IndexOf(listCodes, selectedListName),
                        OnListMemberPicked, removePick, "listPick")
                    .AddAutoSizeHoverText(T("list-member-hover"), CairoFont.WhiteDetailText(), HoverTextWidth, removePick)
                    .AddSmallButton(T("list-remove"), OnRemoveListMemberPressed, removeButton, EnumButtonStyle.Normal, "removeMember")

                    .AddStaticText(T("list-manual"), labelFont, EnumTextOrientation.Left, manualLabel, "lblManual")
                    .AddAutoSizeHoverText(T("list-input-hover"), CairoFont.WhiteDetailText(), HoverTextWidth, manualLabel)
                    .AddTextInput(nameInput, OnNameChanged, CairoFont.WhiteSmallishText(), "nameInput")
                    .AddSmallButton(T("list-add"), OnAddPressed, manualAdd, EnumButtonStyle.Normal, "add")
                    .AddSmallButton(T("list-remove"), OnRemovePressed, manualRemove, EnumButtonStyle.Normal, "remove")

                    .AddStaticText(T("lists-hint"), hintFont, EnumTextOrientation.Left, hint, "lblListsHint")
                    .AddSmallButton(T("save"), OnSavePressed, save, EnumButtonStyle.Normal, "save")
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextInput("nameInput").SetValue(pendingName ?? "");
        }

        /// <summary>
        /// Online player names, or a single blank entry when there is nobody to list. A dropdown
        /// needs at least one option, and a blank code is what the add button treats as "nothing
        /// picked", so the empty case needs no special handling anywhere else.
        /// </summary>
        private string[] OnlinePlayerNames()
        {
            var names = capi.World.AllOnlinePlayers
                .Select(p => p.PlayerName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToArray();

            return names.Length == 0 ? new[] { "" } : names;
        }

        private string[] OnlinePlayerLabels(string[] codes)
        {
            return codes.Select(c => string.IsNullOrEmpty(c) ? T("list-none-online") : c).ToArray();
        }

        private string[] CurrentListNames()
        {
            var list = CurrentList;
            if (list == null || list.Count == 0) return new[] { "" };
            return list.OrderBy(n => n).ToArray();
        }

        private string[] ListNameLabels(string[] codes)
        {
            return codes.Select(c => string.IsNullOrEmpty(c) ? T("list-none-in-list") : c).ToArray();
        }

        private static int IndexOf(string[] codes, string value)
        {
            int index = System.Array.IndexOf(codes, value ?? "");
            return index < 0 ? 0 : index;
        }

        private void OnOnlinePicked(string code, bool selected)
        {
            selectedOnlineName = code;
        }

        private void OnListMemberPicked(string code, bool selected)
        {
            selectedListName = code;
        }

        private bool OnAddOnlinePressed()
        {
            AddName(selectedOnlineName);
            return true;
        }

        private bool OnRemoveListMemberPressed()
        {
            RemoveName(selectedListName);
            selectedListName = null;
            return true;
        }

        private List<string> CurrentList => editingBlacklist ? settings.Blacklist : settings.Whitelist;

        private string DescribeCurrentList()
        {
            var list = CurrentList;
            string body = list == null || list.Count == 0 ? T("list-empty") : string.Join(", ", list);
            return T(editingBlacklist ? "list-blacklist" : "list-whitelist") + ": " + body;
        }

        /// <summary>
        /// Rebuilds the whole dialogue. Cheaper to write than poking at individual elements, and
        /// the list display, the dropdown and the input all have to agree after every edit.
        /// </summary>
        private void Rebuild()
        {
            ComposeDialog();
            RefreshValues();
        }

        private void OnListChoiceChanged(string code, bool selected)
        {
            editingBlacklist = code == "blacklist";
            Rebuild();
        }

        private void OnNameChanged(string text)
        {
            pendingName = text;
        }

        private bool OnAddPressed()
        {
            AddName(pendingName);
            // Cleared so the next name can be typed straight away.
            pendingName = "";
            return true;
        }

        private bool OnRemovePressed()
        {
            RemoveName(pendingName);
            pendingName = "";
            return true;
        }

        /// <summary>
        /// Both entry paths end here. Nothing is sent to the server until Save, so this only edits
        /// the copy the dialogue is holding.
        /// </summary>
        private void AddName(string rawName)
        {
            string name = rawName?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var list = CurrentList;
            if (!list.Contains(name)) list.Add(name);
            Rebuild();
        }

        private void RemoveName(string rawName)
        {
            string name = rawName?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            CurrentList.Remove(name);
            Rebuild();
        }

        /// <summary>
        /// A row caption plus the tooltip that explains what the setting actually does. Every row
        /// gets one: the captions have to stay short to fit the column, which leaves no room to
        /// say what a setting means for the player at the table.
        /// </summary>
        private void AddRowLabel(GuiComposer composer, CairoFont font, ElementBounds bounds, string key)
        {
            composer
                .AddStaticText(T(key), font, EnumTextOrientation.Left, bounds, "lbl" + key)
                .AddAutoSizeHoverText(T(key + "-hover"), CairoFont.WhiteDetailText(), HoverTextWidth, bounds);
        }

        private static ElementBounds LabelAt(double y)
        {
            return ElementBounds.Fixed(0, y, LabelWidth, RowHeight);
        }

        private static ElementBounds ControlAt(double y, double height)
        {
            return ElementBounds.Fixed(LabelWidth + 20, y, ControlWidth, height);
        }

        private void RefreshValues()
        {
            int modeIndex = System.Array.IndexOf(accessModes, (settings.AccessMode ?? "enabled").ToLowerInvariant());
            SingleComposer.GetDropDown("mode").SetSelectedIndex(modeIndex < 0 ? 1 : modeIndex);

            SingleComposer.GetSlider("voxels").SetValues(settings.VoxelsPerClick, 1, 8, 1, " " + T("voxels-unit"));
            // 50ms steps: the interval only has to feel right, and a finer slider would make it
            // fiddly to land on a round number.
            SingleComposer.GetSlider("hold").SetValues(settings.HoldStrikeIntervalMs, 0, 2000, 50, " " + T("hold-unit"));

            SingleComposer.GetSwitch("durability").SetValue(settings.DurabilityPerVoxel);
            SingleComposer.GetSwitch("spare").SetValue(settings.SpareRecipeVoxels);
            SingleComposer.GetSwitch("instant").SetValue(settings.InstantComplete);
        }

        private string[] ModeNames()
        {
            string[] names = new string[accessModes.Length];
            for (int i = 0; i < accessModes.Length; i++) names[i] = T("mode-" + accessModes[i]);
            return names;
        }

        private void OnModeChanged(string code, bool selected)
        {
            settings.AccessMode = code;
        }

        private bool OnVoxelsChanged(int value)
        {
            settings.VoxelsPerClick = value;
            return true;
        }

        private bool OnHoldChanged(int value)
        {
            settings.HoldStrikeIntervalMs = value;
            return true;
        }

        private void OnDurabilityChanged(bool value)
        {
            settings.DurabilityPerVoxel = value;
        }

        private void OnSpareChanged(bool value)
        {
            settings.SpareRecipeVoxels = value;
        }

        private void OnInstantChanged(bool value)
        {
            settings.InstantComplete = value;
        }

        private bool OnSavePressed()
        {
            // The server checks the privilege again before applying: a packet is a packet, whoever
            // sends it. It also broadcasts the result, so this client's own config updates from
            // the same path everyone else's does.
            canjewelry.clientChannel?.SendPacket(settings);

            // Deliberately stays open - settings like these get adjusted in several passes, and
            // closing the window after every save would mean retyping the command each time. The
            // chat line is the only feedback that a save happened, since nothing on screen moves.
            capi.ShowChatMessage(T("saved"));
            return true;
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        private static string T(string key) => Lang.Get("canjewelry:cuttingsettings-" + key);
    }
}
