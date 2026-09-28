using canjewelry.src.api;
using canjewelry.src.CB;
using canjewelry.src.render;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.gui
{
    /// <summary>
    /// Tuning menu for where a gem sits on a model. Opened by a hotkey on the item in the active
    /// hotbar slot, it edits the poses of <see cref="CANGemVisualRegistry"/> in memory and exports
    /// them as a gemvisuals JSON into ModConfig, which is read on the next launch and outranks the
    /// rules shipped in assets. A placement that has settled is copied into the mod's resources by
    /// hand.
    ///
    /// <para>The item is previewed as a copy with every socket filled by the chosen gem, so poses
    /// can be tuned without owning the gems, and without touching what the player is holding.</para>
    ///
    /// <para>Strings are not translated on purpose: this is a tool for building the mod, not part
    /// of the game.</para>
    /// </summary>
    public class GuiDialogGemVisualDebug : GuiDialog
    {
        // Opened from a hotkey registered by the mod, so there is no combination to declare here.
        public override string ToggleKeyCombinationCode => null;

        // Poses are tuned by eye, so the preview is given most of the dialog: large enough to see
        // a single gem sit on a model, and zoomable on top of that.
        private const int PreviewSize = 420;
        // Rendered at more than it is painted at, so zooming in stays sharp instead of stretching.
        private const int PreviewFboSize = 640;
        private const int ColumnX = PreviewSize + 40;
        private const int LabelWidth = 110;
        private const int ControlWidth = 320;
        // Two columns of controls, not one: everything in a single column makes a dialog taller
        // than the screen it is meant to be tuned on. The sliders and the rows that move poses
        // around live in the second one.
        private const int ColumnWidth = LabelWidth + 10 + ControlWidth;
        private const int ColumnX2 = ColumnX + ColumnWidth + 30;
        private const int RowHeight = 30;
        // Sliders paint over the full height of their bounds; a RowHeight row clips them.
        private const int SliderHeight = 45;
        private const int RowSpacing = 8;
        // The slider gives up its right end to a box holding the exact value.
        private const int ValueBoxWidth = 90;
        private const int SliderWidth = ControlWidth - ValueBoxWidth - 10;

        private static readonly string[] targets =
        {
            CANGemVisualTarget.Hand, CANGemVisualTarget.Gui,
            CANGemVisualTarget.Ground, CANGemVisualTarget.Body
        };

        private static readonly string[] sides =
        {
            CANGemVisualSide.Name(CANGemVisualSide.Front), CANGemVisualSide.Name(CANGemVisualSide.Back)
        };

        private static readonly string[] cuttingTypes =
        {
            CANJWConstants.CUTTING_ROUND, CANJWConstants.CUTTING_BAGUETTE, CANJWConstants.CUTTING_PEAR
        };

        private static readonly string[] sizeLabels = { "normal", "flawless", "exquisite" };

        private static readonly string[] sliderKeys = { "tx", "ty", "tz", "rx", "ry", "rz", "scale" };

        private JewelerItemPreview itemPreview;

        /// <summary>A copy of the held item with every socket filled. Never the player's own stack.</summary>
        private ItemStack previewStack;
        private int maxSockets;

        /// <summary>What the poses are being written for: an item code, or "$" plus a group name.</summary>
        private string subject;
        private string itemSubject;
        /// <summary>Families the held item belongs to, as "$name" — own pose groups first.</summary>
        private readonly List<string> groupSubjects = new List<string>();

        private string target = CANGemVisualTarget.Hand;
        private int socketIndex;

        /// <summary>Which of the two placements of the gem is under the sliders, near face or far face.</summary>
        private int side = CANGemVisualSide.Front;

        private string gemType;
        private string cuttingType = CANJWConstants.CUTTING_ROUND;
        private int gemSize = 1;

        /// <summary>The pose under the sliders. A copy, so the rules behind it stay untouched.</summary>
        private ModelTransform current;

        /// <summary>A pose picked up by "Copy", to be dropped onto another socket or side.</summary>
        private ModelTransform clipboard;

        /// <summary>Set while the sliders write into the value boxes, so they do not write back.</summary>
        private bool syncingValueBoxes;

        /// <summary>Whether edits are written for the previewed cut alone rather than for every cut.</summary>
        private bool perCut;

        /// <summary>The cut the "Copy from" button takes poses from.</summary>
        private string copyFromCut;

        /// <summary>The subject the "Copy here" button takes poses from.</summary>
        private string copyFromSubject;

        /// <summary>Whether the group and copying rows are unfolded. Off: the dialog stays short.</summary>
        private bool showFamilies;

        /// <summary>Which column the row helpers are laying out into while the dialog is composed.</summary>
        private double columnX = ColumnX;

        /// <summary>The subject the "Copy there" button hands the tuned poses to.</summary>
        private string copyToSubject;

        /// <summary>The family picked in the "any group" row, this item's or not.</summary>
        private string otherGroup;

        /// <summary>
        /// The copy button that has been pressed once and is waiting to be pressed again, as
        /// "from:subject" or "to:subject". Null when none is armed.
        /// </summary>
        private string pendingCopy;

        /// <summary>When <see cref="pendingCopy"/> was armed, in client milliseconds.</summary>
        private long pendingCopyAtMs;

        /// <summary>The item code typed into the "copy from item" box, poses are taken from it.</summary>
        private string copyFromCode;

        /// <summary>The same for the previewed size: off, one placement serves every size.</summary>
        private bool perSize;

        /// <summary>Whether the sliders step in thousandths rather than hundredths.</summary>
        private bool fine;

        /// <summary>Contents of the two boxes that declare a family of items of one's own.</summary>
        private string newGroupName;
        private string newGroupPatterns;

        /// <summary>
        /// Whether "Add this item" writes the wildcard of the item's whole family of materials
        /// ("pickaxe-*") rather than the item's own code. Off by default: a family narrowed by hand
        /// is a family of named items, and the wildcard is what made it too broad to begin with.
        /// </summary>
        private bool addAsWildcard;

        /// <summary>The patterns box of the family being tuned, and the family it was filled for.</summary>
        private string groupPatternsEdit;
        private string groupPatternsEditFor;

        /// <summary>The item the group boxes were last filled in for, so a change of item refills them.</summary>
        private string itemSuggestedFor;

        public GuiDialogGemVisualDebug(ICoreClientAPI capi) : base(capi)
        {
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            // Nothing here draws a gem while the visuals are off, so say so rather than let the
            // sliders move an invisible one.
            if (!CANGemVisibility.Enabled(capi))
            {
                capi.ShowChatMessage("[canjewelry] gem visuals are switched off"
                                     + (canjewelry.config?.enableGemVisuals == false
                                         ? " by the server config (enableGemVisuals)"
                                         : " for this client (.cangemvisuals on)")
                                     + ", nothing will be drawn");
            }

            BuildPreviewStack();
            // The preview renders through the GUI path, so the registry is told which target the
            // menu actually means. Cleared again on close.
            CANGemVisualRegistry.DebugTargetRedirect = target;
            LoadCurrentPose();
            ComposeDialog();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            CANGemVisualRegistry.DebugTargetRedirect = null;
            EncrustableCB.ClearMeshCache(capi);

            // Tuned poses only live in memory, so a session's work would be gone at the next launch
            // if closing the menu were the same as discarding it. Every subject touched this session
            // is written out, not only the one that happens to be picked: poses, far side switches,
            // bones and families switched off all count.
            foreach (string touched in CANGemVisualRegistry.TunedSubjects()) Export(touched);
        }

        /// <summary>
        /// Takes a copy of the held item and fills all of its sockets with the chosen gem. Poses
        /// can then be tuned on any encrustable item, gems in the bag or not.
        /// </summary>
        private void BuildPreviewStack()
        {
            previewStack = null;
            maxSockets = 0;
            itemSubject = null;
            groupSubjects.Clear();

            ItemStack held = capi.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held?.Collectible?.Code == null) return;

            // A different item means a different family to suggest. Keeping the old patterns would
            // quietly declare the new group over the previous item's codes.
            if (itemSuggestedFor != null && itemSuggestedFor != held.Collectible.Code.ToShortString())
            {
                newGroupPatterns = null;
                newGroupName = null;
            }
            itemSuggestedFor = held.Collectible.Code.ToShortString();

            gemType ??= canjewelry.gems_textures?.Keys.OrderBy(k => k).FirstOrDefault()
                        ?? CANJWConstants.FALLBACK_GEM_TYPE;

            previewStack = held.Clone();
            maxSockets = EncrustableCB.GetMaxAmountSockets(previewStack);
            itemSubject = held.Collectible.Code.ToShortString();

            foreach (string group in CANGemVisualRegistry.FindGroups(held.Collectible.Code))
            {
                groupSubjects.Add("$" + group);
            }

            subject ??= itemSubject;
            if (subject != itemSubject && !groupSubjects.Contains(subject)) subject = itemSubject;

            if (socketIndex >= maxSockets) socketIndex = 0;
            FillSockets();
        }

        private void FillSockets()
        {
            FillSockets(previewStack, maxSockets);
            Invalidate();
        }

        /// <summary>
        /// Puts a gem of the previewed kind into every socket of a stack. Also used for the item a
        /// pose is copied from: a resolve answers by cut and size, so the source has to carry the
        /// same gem as the preview or it would answer for a different one.
        /// </summary>
        private void FillSockets(ItemStack stack, int sockets)
        {
            if (stack == null || sockets <= 0) return;

            ITreeAttribute tree = new TreeAttribute();
            tree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, sockets);
            for (int i = 0; i < sockets; i++)
            {
                ITreeAttribute socket = new TreeAttribute();
                // Highest tier: the preview only has to render, and a tier that is too low would
                // be refused by nothing here anyway.
                socket.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, 3);
                socket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, gemType);
                socket.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, gemSize);
                socket.SetString(CANJWConstants.CUTTING_TYPE, cuttingType);
                tree["slot" + i] = socket;
            }
            stack.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = tree;
        }

        /// <summary>
        /// Fills the sliders from the pose that is in force for the current subject, target and
        /// socket: the tuned one if there is one, otherwise whatever the rules answer, so tuning
        /// starts where the item already looks rather than at zero.
        /// </summary>
        private void LoadCurrentPose()
        {
            ModelTransform pose = subject == null
                ? null
                : CANGemVisualRegistry.GetOverride(PoseSubject(), target, socketIndex, side)
                  ?? CANGemVisualRegistry.GetOverride(subject, target, socketIndex, side);
            if (pose == null && previewStack != null)
            {
                // For the far side this is normally the near side pose turned around, so tuning it
                // starts from where the gem already is instead of from nothing.
                pose = CANGemVisualRegistry.ResolvePose(previewStack, socketIndex, target, side);
            }
            current = (pose ?? ModelTransform.NoTransform).Clone();
        }

        /// <summary>Writes the slider values back and makes the change visible at once.</summary>
        private void Apply()
        {
            if (subject == null) return;

            // Touching the far side sliders is what pins it down: from here on it is a pose of its
            // own and no longer follows the near side.
            string poseSubject = PoseSubject();
            if (IsWearable())
            {
                // Worn gear is drawn from the same full body model everywhere — in the gui, in hand
                // and on the wearer — so one pose serves every target. Writing only the target being
                // looked at would leave the others on whatever the assets say, which is exactly how
                // a gem ends up sitting higher in the gui than on the player.
                foreach (string other in targets)
                {
                    CANGemVisualRegistry.SetOverride(poseSubject, other, socketIndex, side, current.Clone());
                }
            }
            else
            {
                CANGemVisualRegistry.SetOverride(poseSubject, target, socketIndex, side, current);
            }
            Invalidate();
        }

        /// <summary>
        /// The mesh cache keys the built model by item and gems, not by pose, so a pose edit is
        /// only visible once the built meshes are dropped.
        /// </summary>
        private void Invalidate()
        {
            // Dropping every built mesh and rebuilding the whole player model is not cheap, and a
            // dragged slider asks for it dozens of times a second. At this rate the picture still
            // follows the mouse, and the work is done once instead of per pixel dragged.
            long now = capi.ElapsedMilliseconds;
            long since = now - lastInvalidateMs;
            if (since >= InvalidateIntervalMs)
            {
                lastInvalidateMs = now;
                ApplyInvalidate();
                return;
            }

            if (invalidatePending) return;

            invalidatePending = true;
            capi.Event.RegisterCallback(_ =>
            {
                invalidatePending = false;
                lastInvalidateMs = capi.ElapsedMilliseconds;
                ApplyInvalidate();
            }, (int)(InvalidateIntervalMs - since));
        }

        private const int InvalidateIntervalMs = 100;
        private long lastInvalidateMs;
        private bool invalidatePending;

        private void ApplyInvalidate()
        {
            EncrustableCB.ClearMeshCache(capi);
            itemPreview?.MarkDirty();

            // Gems on worn gear are part of the player's shape, not of an item mesh, so a pose edit
            // only reaches them once that shape is built again. This is what makes the sliders move
            // the gem on the wearer as well while the "body" target is being tuned.
            capi.World?.Player?.Entity?.MarkShapeModified();
        }

        private void ComposeDialog()
        {
            CairoFont labelFont = CairoFont.WhiteSmallishText();
            // Laying out starts in the first column every time, whatever the last compose left behind.
            columnX = ColumnX;

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            var composer = capi.Gui.CreateCompo("canjewelrygemvisualdebug", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Gem visual debug", () => TryClose())
                .BeginChildElements(bgBounds);

            if (previewStack == null)
            {
                composer.AddStaticText("Hold an item in the active hotbar slot, then reopen.",
                    labelFont, EnumTextOrientation.Left, ElementBounds.Fixed(0, 30, 420, 60), "lblEmpty");
                SingleComposer = composer.EndChildElements().Compose();
                return;
            }

            AddPreview(composer);

            double y = 30;

            // What is being tuned. The group entry writes one pose for every item of the family,
            // which is how hundreds of items get a sensible gem without being touched one by one.
            var subjectCodes = new List<string> { itemSubject };
            var subjectLabels = new List<string> { "this item: " + itemSubject };
            // Every family the item belongs to, own pose groups before the config's.
            foreach (string group in groupSubjects)
            {
                subjectCodes.Add(group);
                subjectLabels.Add("group: " + group);
            }
            AddRow(composer, labelFont, y, "subject");
            composer.AddDropDown(subjectCodes.ToArray(), subjectLabels.ToArray(),
                System.Math.Max(0, subjectCodes.IndexOf(subject)), OnSubjectChanged, ControlAt(y, RowHeight), "subject");
            y += RowHeight + RowSpacing;

            AddRow(composer, labelFont, y, "target");
            composer.AddDropDown(targets, targets, System.Array.IndexOf(targets, target),
                OnTargetChanged, ControlAt(y, RowHeight), "target");
            y += RowHeight + RowSpacing;

            string[] socketCodes = SocketCodes();
            AddRow(composer, labelFont, y, "socket");
            composer.AddDropDown(socketCodes, socketCodes, System.Array.IndexOf(socketCodes, socketIndex.ToString()),
                OnSocketChanged, ControlAt(y, RowHeight), "socket");
            y += RowHeight + RowSpacing;

            // A gem is drawn twice, once per face of the model, so which of the two the sliders
            // move has to be said here. "back" starts out as "front" turned around.
            AddRow(composer, labelFont, y, "side");
            composer.AddDropDown(sides, sides, side, OnSideChanged, ControlAt(y, RowHeight), "side");
            y += RowHeight + RowSpacing;

            // Only worn gear hangs off a bone, and only the body target is drawn on the wearer.
            if (target == CANGemVisualTarget.Body)
            {
                string[] attachCodes = AttachCodes();
                AddRow(composer, labelFont, y, "attach to");
                composer.AddDropDown(attachCodes, AttachLabels(attachCodes),
                    System.Math.Max(0, System.Array.IndexOf(attachCodes, CurrentAttach() ?? "")),
                    OnAttachChanged, ControlAt(y, RowHeight), "attach");
                y += RowHeight + RowSpacing;
            }

            // Switches the gems of this subject off entirely - the item alone while a code is
            // picked, the whole family while a group is. Exported as a "hidden" rule.
            AddSwitchRow(composer, labelFont, ref y, "draw gems", "draw", OnDrawGemsToggled);
            AddSwitchRow(composer, labelFont, ref y, "far side", "flip", OnFlipToggled);

            string[] gemTypes = GemTypes();
            AddRow(composer, labelFont, y, "gem");
            composer.AddDropDown(gemTypes, gemTypes, System.Array.IndexOf(gemTypes, gemType),
                OnGemTypeChanged, ControlAt(y, RowHeight), "gem");
            y += RowHeight + RowSpacing;

            AddRow(composer, labelFont, y, "cut");
            composer.AddDropDown(cuttingTypes, cuttingTypes, System.Array.IndexOf(cuttingTypes, cuttingType),
                OnCuttingChanged, ControlAt(y, RowHeight), "cut");
            y += RowHeight + RowSpacing;

            // Off by default: one placement usually suits every shape of gem, and only the odd cut
            // is given poses of its own.
            AddSwitchRow(composer, labelFont, ref y, "this cut only", "percut", OnPerCutToggled);

            // Whole-cut moves, as opposed to the single pose Copy and Paste deal in: take everything
            // another cut has (or, failing that, the general placement) as a starting point, or give
            // this cut back to the general placement the other cuts use.
            AddRow(composer, labelFont, y, "cut poses");
            composer.AddDropDown(cuttingTypes, cuttingTypes, System.Array.IndexOf(cuttingTypes, CopyFromCut()),
                OnCopyFromCutChanged, ElementBounds.Fixed(ColumnX + LabelWidth + 10, y, 100, RowHeight), "copyfromcut");
            composer.AddSmallButton("Copy from", OnCopyCutPressed,
                ElementBounds.Fixed(ColumnX + LabelWidth + 120, y, 100, RowHeight), EnumButtonStyle.Normal, "copycut");
            composer.AddSmallButton("Reset this cut", OnResetCutPressed,
                ElementBounds.Fixed(ColumnX + LabelWidth + 230, y, 130, RowHeight), EnumButtonStyle.Normal, "resetcut");
            y += RowHeight + RowSpacing;

            AddRow(composer, labelFont, y, "size");
            composer.AddDropDown(new[] { "1", "2", "3" }, sizeLabels, gemSize - 1,
                OnSizeChanged, ControlAt(y, RowHeight), "size");
            y += RowHeight + RowSpacing;

            AddSwitchRow(composer, labelFont, ref y, "this size only", "persize", OnPerSizeToggled);
            // Ten times finer steps on every slider: thousandths of a block, tenths of a degree.
            AddSwitchRow(composer, labelFont, ref y, "fine steps", "fine", OnFineToggled);

            // The second column, from the top again. The sliders lead it so that they keep the same
            // place on screen whatever else is folded in or out below them - a slider that moves
            // while it is being dragged for is worse than a tall dialog.
            columnX = ColumnX2;
            y = 30;

            AddSliderRow(composer, labelFont, ref y, "tx", OnTranslateX);
            AddSliderRow(composer, labelFont, ref y, "ty", OnTranslateY);
            AddSliderRow(composer, labelFont, ref y, "tz", OnTranslateZ);
            AddSliderRow(composer, labelFont, ref y, "rx", OnRotateX);
            AddSliderRow(composer, labelFont, ref y, "ry", OnRotateY);
            AddSliderRow(composer, labelFont, ref y, "rz", OnRotateZ);
            AddSliderRow(composer, labelFont, ref y, "scale", OnScaleChanged);

            y += RowSpacing;
            // Copy takes the pose under the sliders; paste drops it on whatever socket, side or
            // target is picked afterwards, which is how a second gem is placed like the first.
            composer.AddSmallButton("Copy pose", OnCopyPressed,
                ElementBounds.Fixed(columnX, y, 120, RowHeight), EnumButtonStyle.Normal, "copy");
            composer.AddSmallButton("Paste pose", OnPastePressed,
                ElementBounds.Fixed(columnX + 130, y, 120, RowHeight), EnumButtonStyle.Normal, "paste");
            composer.AddSmallButton("Paste to all sockets", OnPasteAllPressed,
                ElementBounds.Fixed(columnX + 260, y, 180, RowHeight), EnumButtonStyle.Normal, "pasteall");
            y += RowHeight + RowSpacing;

            composer.AddSmallButton("Reset", OnResetPressed,
                ElementBounds.Fixed(columnX, y, 120, RowHeight), EnumButtonStyle.Normal, "reset");
            composer.AddSmallButton("Export JSON", OnExportPressed,
                ElementBounds.Fixed(columnX + 130, y, 160, RowHeight), EnumButtonStyle.Normal, "export");
            y += RowHeight + RowSpacing;

            AddFamilyRows(composer, labelFont, ref y, subjectCodes);

            SingleComposer = composer.EndChildElements().Compose();

            // The pattern box starts filled in with the family the item plainly belongs to, so
            // making a group is usually a matter of typing a name and pressing the button. Both
            // boxes are written on every compose: the dialog is rebuilt whenever a dropdown changes
            // and the new box would otherwise come up empty while the typed value still stood
            // behind it - the next Create group would then use what was typed for another item.
            if (string.IsNullOrEmpty(newGroupPatterns) && previewStack?.Collectible?.Code != null)
            {
                newGroupPatterns = CANGemVisualRegistry.SuggestPattern(previewStack.Collectible.Code);
            }
            SingleComposer.GetTextInput("grouppatterns")?.SetValue(newGroupPatterns ?? "");
            SingleComposer.GetTextInput("groupname")?.SetValue(newGroupName ?? "");
            SingleComposer.GetTextInput("copyfromcode")?.SetValue(copyFromCode ?? "");

            // The patterns box shows what the family covers right now, refilled whenever the family
            // being tuned changes - but not while it is being edited for that same family, or every
            // rebuild of the dialog would undo the typing.
            if (subject != groupPatternsEditFor)
            {
                string[] current = subject != null && subject.StartsWith("$")
                    ? CANGemVisualRegistry.GroupPatterns(subject.Substring(1))
                    : null;
                groupPatternsEdit = current == null ? null : string.Join(", ", current);
                groupPatternsEditFor = subject;
            }
            SingleComposer.GetTextInput("editpatterns")?.SetValue(groupPatternsEdit ?? "");
            RefreshSliders();
        }

        /// <summary>
        /// The rows about families and about moving a placement from one subject to another. Folded
        /// away by default: it is a job of its own, done once in a while, while the sliders above
        /// are the job of every session — and unfolded it would make the dialog taller than a small
        /// screen.
        /// </summary>
        private void AddFamilyRows(GuiComposer composer, CairoFont labelFont, ref double y, List<string> subjectCodes)
        {
            composer.AddSmallButton(showFamilies ? "Groups and copying  [-]" : "Groups and copying  [+]",
                OnToggleFamiliesPressed,
                ElementBounds.Fixed(columnX, y, 250, RowHeight), EnumButtonStyle.Normal, "togglefamilies");
            y += RowHeight + RowSpacing;
            if (!showFamilies) return;

            // Takes a placement worked out on one subject over to another - this pickaxe onto the
            // whole family, most of the time. What the source has tuned comes across as it is; a
            // source that only follows the rules hands over what it looks like right now.
            var sources = subjectCodes.FindAll(code => code != subject);
            if (sources.Count > 0)
            {
                AddRow(composer, labelFont, y, "take poses from");
                composer.AddDropDown(sources.ToArray(), sources.ToArray(),
                    System.Math.Max(0, sources.IndexOf(CopyFromSubject(sources))), OnCopyFromSubjectChanged,
                    ElementBounds.Fixed(ControlX, y, 210, RowHeight), "copyfromsubject");
                composer.AddSmallButton("Copy here", OnCopySubjectPressed,
                    ElementBounds.Fixed(ControlX + 220, y, 110, RowHeight), EnumButtonStyle.Normal,
                    "copysubject");
                y += RowHeight + RowSpacing;

                // The same move in the other direction, which is the one everybody reaches for
                // first: this item's placement is right, hand it to the family. Without it the
                // family has to be made the subject before it can be given anything, and the sliders
                // are then no longer on the item that was just placed right.
                AddRow(composer, labelFont, y, "give poses to");
                composer.AddDropDown(sources.ToArray(), sources.ToArray(),
                    System.Math.Max(0, sources.IndexOf(CopyToSubject(sources))), OnCopyToSubjectChanged,
                    ElementBounds.Fixed(ControlX, y, 210, RowHeight), "copytosubject");
                composer.AddSmallButton("Copy there", OnCopyToSubjectPressed,
                    ElementBounds.Fixed(ControlX + 220, y, 110, RowHeight), EnumButtonStyle.Normal,
                    "copyto");
                y += RowHeight + RowSpacing;
            }

            // Every family there is, not just the ones this item is in, in either direction: this is
            // what carries a placement from one group to another when the item in hand belongs to
            // neither of them.
            string[] groupSubjectsAll = AllGroupSubjects();
            if (groupSubjectsAll.Length > 0)
            {
                AddRow(composer, labelFont, y, "any group");
                composer.AddDropDown(groupSubjectsAll, groupSubjectsAll,
                    System.Math.Max(0, System.Array.IndexOf(groupSubjectsAll, OtherGroup(groupSubjectsAll))),
                    OnOtherGroupChanged, ElementBounds.Fixed(ControlX, y, 210, RowHeight), "othergroup");
                composer.AddSmallButton("Copy here", OnCopyFromGroupPressed,
                    ElementBounds.Fixed(ControlX + 220, y, 110, RowHeight), EnumButtonStyle.Normal, "copygroup");
                composer.AddSmallButton("Copy there", OnCopyToGroupPressed,
                    ElementBounds.Fixed(ControlX + 340, y, 110, RowHeight), EnumButtonStyle.Normal, "copygroupto");
                y += RowHeight + RowSpacing;
            }

            // And any single item, typed out - the one thing no dropdown here can offer, since the
            // menu only ever knows about the item in hand.
            AddRow(composer, labelFont, y, "any item");
            composer.AddTextInput(ElementBounds.Fixed(ControlX, y, 210, RowHeight),
                OnCopyCodeTyped, CairoFont.WhiteDetailText(), "copyfromcode");
            composer.AddSmallButton("Copy here", OnCopyFromCodePressed,
                ElementBounds.Fixed(ControlX + 220, y, 110, RowHeight), EnumButtonStyle.Normal, "copycode");
            composer.AddSmallButton("Copy there", OnCopyToCodePressed,
                ElementBounds.Fixed(ControlX + 340, y, 110, RowHeight), EnumButtonStyle.Normal, "copycodeto");
            y += RowHeight + 2;
            composer.AddStaticText("item code, e.g. pickaxe-steel",
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(ControlX, y + 2, 450, 18), "copycodehint");
            y += 18 + RowSpacing;

            // The patterns of the family being tuned, to edit as they are. This is the only way to
            // narrow a family that was declared with the wildcard suggested when it was created:
            // "Add this item" cannot do it, since the wildcard already speaks for the item.
            string tunedGroup = subject != null && subject.StartsWith("$") ? subject.Substring(1) : null;
            string[] tunedPatterns = tunedGroup == null ? null : CANGemVisualRegistry.GroupPatterns(tunedGroup);
            if (tunedPatterns != null)
            {
                AddRow(composer, labelFont, y, "patterns");
                composer.AddTextInput(ElementBounds.Fixed(ControlX, y, 320, RowHeight),
                    text => groupPatternsEdit = text, CairoFont.WhiteDetailText(), "editpatterns");
                composer.AddSmallButton("Apply", OnApplyPatternsPressed,
                    ElementBounds.Fixed(ControlX + 330, y, 100, RowHeight), EnumButtonStyle.Normal,
                    "applypatterns");
                y += RowHeight + 2;
                composer.AddStaticText("what " + subject + " covers; codes and wildcards, ! to exclude",
                    CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                    ElementBounds.Fixed(ControlX, y + 2, 450, 18), "editpatternshint");
                y += 18 + RowSpacing;
            }

            // Which shape "Add this item" writes: the item alone, or the wildcard its whole family
            // of materials shares. A group of named items is the point of narrowing one, so the
            // item's own code is the default.
            AddSwitchRow(composer, labelFont, ref y, "add as wildcard", "addwildcard", OnAddAsWildcardToggled);

            // A family of one's own, made from the item in hand: a name and the codes it covers.
            // Poses are then tuned against "$name" like any other group, and the export carries the
            // declaration along so it is there on the next launch.
            AddRow(composer, labelFont, y, "new group");
            composer.AddTextInput(ElementBounds.Fixed(ControlX, y, 120, RowHeight),
                text => newGroupName = text, CairoFont.WhiteDetailText(), "groupname");
            composer.AddTextInput(ElementBounds.Fixed(ControlX + 130, y, 190, RowHeight),
                text => newGroupPatterns = text, CairoFont.WhiteDetailText(), "grouppatterns");
            composer.AddSmallButton("Create", OnCreateGroupPressed,
                ElementBounds.Fixed(ControlX + 330, y, 100, RowHeight), EnumButtonStyle.Normal, "creategroup");
            y += RowHeight + 2;
            composer.AddStaticText("name, then codes separated by commas",
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(ControlX, y + 2, 430, 18), "grouphint");
            y += 18 + RowSpacing;

            // Putting one item into the family being tuned, or taking it back out. A group is a list
            // of patterns, so neither is a list edit: joining adds the item's code (and drops the
            // exclusion that kept it out), leaving appends an exclusion.
            // Both on show whatever the subject, saying what they would do: hiding them whenever it
            // is not a group only leaves the reader looking for a button that is not there.
            bool onGroup = subject != null && subject.StartsWith("$");
            composer.AddSmallButton("Add this item", OnAddItemPressed,
                ElementBounds.Fixed(ControlX, y, 150, RowHeight), EnumButtonStyle.Normal, "additem");
            composer.AddSmallButton("Exclude this item", OnExcludeItemPressed,
                ElementBounds.Fixed(ControlX + 160, y, 170, RowHeight), EnumButtonStyle.Normal, "excludeitem");
            y += RowHeight + 2;
            composer.AddStaticText(
                onGroup ? "puts " + itemSubject + " into or out of " + subject : "pick a group as the subject first",
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(ControlX, y + 2, 430, 18), "excludehint");
            y += 18 + RowSpacing;
        }

        private void AddPreview(GuiComposer composer)
        {
            itemPreview ??= new JewelerItemPreview(capi, PreviewFboSize);

            ElementBounds previewEl = ElementBounds.FixedSize(PreviewSize, PreviewSize);
            previewEl.fixedX = 0;
            previewEl.fixedY = 30;
            composer.AddInset(previewEl.ForkBoundingParent(4, 4, 4, 4), 3);
            composer.AddInteractiveElement(new GuiElementItemPreview(capi, previewEl, itemPreview, zoomable: true),
                "itempreview");

            composer.AddStaticText("drag to rotate, right-drag to move, wheel to zoom",
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(0, 30 + PreviewSize + 8, PreviewSize, 18), "rotatehint");
            composer.AddStaticText("middle click puts the view back",
                CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                ElementBounds.Fixed(0, 30 + PreviewSize + 26, PreviewSize, 18), "viewhint");

            // A body gem is not drawn on the item at all: it is a shape element hung off a bone of
            // the wearer. The preview can only ever show the item, so it says so rather than let
            // the poses be tuned against a picture that does not match the player.
            if (target == CANGemVisualTarget.Body)
            {
                composer.AddStaticText(
                    "body poses use the same coordinates as the preview, but are drawn on the worn piece - watch yourself in third person",
                    CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorTime1),
                    ElementBounds.Fixed(0, 30 + PreviewSize + 46, PreviewSize, 60), "bodyhint");
            }
        }

        private void AddRow(GuiComposer composer, CairoFont font, double y, string caption)
        {
            composer.AddStaticText(caption, font, EnumTextOrientation.Left,
                ElementBounds.Fixed(columnX, y, LabelWidth, RowHeight), "lbl" + caption);
        }

        /// <summary>Where the control of a row starts, in the column being laid out.</summary>
        private double ControlX => columnX + LabelWidth + 10;

        /// <summary>A labelled on/off switch, the shape every switch in this dialog takes.</summary>
        private void AddSwitchRow(GuiComposer composer, CairoFont font, ref double y, string caption, string key,
            Action<bool> onToggled)
        {
            AddRow(composer, font, y, caption);
            composer.AddSwitch(onToggled,
                ElementBounds.Fixed(ControlX, y, RowHeight, RowHeight), key, RowHeight);
            y += RowHeight + RowSpacing;
        }

        /// <summary>
        /// A slider for dragging plus a box for typing. The slider only carries whole steps, so
        /// the box is what gets a pose off the step grid and onto an exact value.
        /// </summary>
        private void AddSliderRow(GuiComposer composer, CairoFont font, ref double y, string key, ActionConsumable<int> onChanged)
        {
            AddRow(composer, font, y, key);
            composer.AddSlider(onChanged,
                ElementBounds.Fixed(ControlX, y, SliderWidth, SliderHeight), key);
            composer.AddNumberInput(
                ElementBounds.Fixed(ControlX + 10 + SliderWidth, y + (SliderHeight - RowHeight) / 2.0,
                    ValueBoxWidth, RowHeight),
                text => OnValueTyped(key, text), CairoFont.WhiteDetailText(), key + "box");
            y += SliderHeight + RowSpacing;
        }

        private ElementBounds ControlAt(double y, double height)
            => ElementBounds.Fixed(ControlX, y, ControlWidth, height);

        /// <summary>The value one slider row stands for, in the unit shown in its box.</summary>
        private float ValueOf(string key) => key switch
        {
            "tx" => current.Translation.X,
            "ty" => current.Translation.Y,
            "tz" => current.Translation.Z,
            "rx" => current.Rotation.X,
            "ry" => current.Rotation.Y,
            "rz" => current.Rotation.Z,
            _ => current.ScaleXYZ.X
        };

        private void SetValueOf(string key, float value)
        {
            switch (key)
            {
                case "tx": current.Translation.X = value; break;
                case "ty": current.Translation.Y = value; break;
                case "tz": current.Translation.Z = value; break;
                case "rx": current.Rotation.X = value; break;
                case "ry": current.Rotation.Y = value; break;
                case "rz": current.Rotation.Z = value; break;
                default: current.ScaleXYZ.Set(value, value, value); break;
            }
        }

        /// <summary>
        /// A value typed into a box. Anything the box cannot read yet — an empty field, a lone
        /// minus sign midway through typing — is left alone rather than snapped to zero.
        /// </summary>
        private void OnValueTyped(string key, string text)
        {
            if (syncingValueBoxes) return;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return;

            // A scale of zero flattens the gem into a plane, which the mesh transform cannot handle
            // and which would draw nothing anyway.
            if (key == "scale" && value < 0.01f) value = 0.01f;

            SetValueOf(key, value);
            Apply();
            SyncSliderTo(key, value);
        }

        /// <summary>Moves the slider onto a typed value without disturbing the box being typed in.</summary>
        private void SyncSliderTo(string key, float value)
        {
            GuiElementSlider slider = SingleComposer?.GetSlider(key);
            if (slider == null) return;

            StepRange(key, out int min, out int max);
            int steps = (int)System.Math.Round(value * StepsPerUnit(key));
            slider.SetValues(GameMath.Clamp(steps, min, max), min, max, 1);
        }

        private string[] SocketCodes()
        {
            int count = System.Math.Max(1, maxSockets);
            var codes = new string[count];
            for (int i = 0; i < count; i++) codes[i] = i.ToString();
            return codes;
        }

        private string[] GemTypes()
        {
            var types = canjewelry.gems_textures?.Keys.OrderBy(k => k).ToArray();
            return types == null || types.Length == 0 ? new[] { CANJWConstants.FALLBACK_GEM_TYPE } : types;
        }

        /// <summary>
        /// Sliders carry whole numbers, so a value is stepped: hundredths of a block and whole
        /// degrees normally, thousandths and tenths of a degree once fine tuning is on.
        /// </summary>
        private float StepsPerUnit(string key)
        {
            bool rotation = key.Length == 2 && key[0] == 'r';
            if (rotation) return fine ? 10f : 1f;
            return fine ? 1000f : 100f;
        }

        /// <summary>The ends of a slider, in its own steps. The range is the same either way — only
        /// how finely it is divided changes.</summary>
        private void StepRange(string key, out int min, out int max)
        {
            float perUnit = StepsPerUnit(key);
            if (key == "scale")
            {
                min = 1;
                max = (int)(3 * perUnit);
            }
            else if (key.Length == 2 && key[0] == 'r')
            {
                max = (int)(180 * perUnit);
                min = -max;
            }
            else
            {
                max = (int)(2 * perUnit);
                min = -max;
            }
        }

        private void RefreshSliders()
        {
            foreach (string key in sliderKeys)
            {
                GuiElementSlider slider = SingleComposer.GetSlider(key);
                if (slider == null) continue;

                StepRange(key, out int min, out int max);
                int steps = (int)System.Math.Round(ValueOf(key) * StepsPerUnit(key));
                slider.SetValues(GameMath.Clamp(steps, min, max), min, max, 1);
            }
            SingleComposer.GetSwitch("flip")?.SetValue(FarSideOn());
            SingleComposer.GetSwitch("draw")?.SetValue(DrawGemsOn());
            SingleComposer.GetSwitch("fine")?.SetValue(fine);
            SingleComposer.GetSwitch("percut")?.SetValue(perCut);
            SingleComposer.GetSwitch("persize")?.SetValue(perSize);
            SingleComposer.GetSwitch("addwildcard")?.SetValue(addAsWildcard);
            RefreshValueBoxes();
        }

        /// <summary>
        /// Writes the exact values into their boxes. Guarded, because setting the text fires the
        /// change handler that would otherwise read it straight back in.
        /// </summary>
        private void RefreshValueBoxes()
        {
            foreach (string key in sliderKeys) SyncValueBox(key);
        }

        private void SyncValueBox(string key)
        {
            syncingValueBoxes = true;
            try
            {
                SingleComposer?.GetNumberInput(key + "box")?
                    .SetValue(ValueOf(key).ToString("0.###", CultureInfo.InvariantCulture));
            }
            finally
            {
                syncingValueBoxes = false;
            }
        }

        /// <summary>Whether the far side gem is on for the current subject and target.</summary>
        private bool FarSideOn()
        {
            if (subject == null) return false;

            bool? tuned = CANGemVisualRegistry.GetFlipOverride(subject, target);
            if (tuned != null) return tuned.Value;

            return previewStack != null
                   && CANGemVisualRegistry.ResolvePose(previewStack, socketIndex, target, CANGemVisualSide.Back) != null;
        }

        private void OnSubjectChanged(string code, bool selected)
        {
            subject = code;
            // An armed copy was armed against the subject that was being tuned a moment ago.
            pendingCopy = null;
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            // The copy rows and the group hints all name the subject, so they are laid out again.
            ComposeDialog();
        }

        private void OnTargetChanged(string code, bool selected)
        {
            target = code;
            CANGemVisualRegistry.DebugTargetRedirect = target;
            LoadCurrentPose();
            Invalidate();
            // The body target brings a control of its own and a different preview, so the dialog is
            // laid out again rather than only refreshed.
            ComposeDialog();
        }

        private void OnSideChanged(string code, bool selected)
        {
            side = CANGemVisualSide.FromName(code);
            LoadCurrentPose();
            Invalidate();
            // Each side has a bone of its own, so the attach list is laid out again with it.
            ComposeDialog();
        }

        /// <summary>
        /// Whether this subject gets a gem on the far side of the model at all. Off for models that
        /// are seen from every side anyway - a gem on the back of a breastplate helps nobody.
        /// </summary>
        private void OnFlipToggled(bool on)
        {
            if (subject == null) return;

            CANGemVisualRegistry.SetFlipOverride(subject, target, on);
            Invalidate();
        }

        /// <summary>
        /// The gear elements a body gem can hang off, with an empty first entry meaning "whatever
        /// the rules say, or the first element of the gear".
        /// </summary>
        private string[] AttachCodes()
        {
            var codes = new List<string> { "" };
            if (previewStack != null) codes.AddRange(CANGemEntityShape.GearElementNames(capi, previewStack));
            return codes.ToArray();
        }

        private static string[] AttachLabels(string[] codes)
        {
            var labels = new string[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                labels[i] = codes[i].Length == 0 ? "(automatic)" : codes[i];
            }
            return labels;
        }

        private string CurrentAttach()
        {
            return subject == null ? null : CANGemVisualRegistry.GetAttachOverride(subject, side);
        }

        /// <summary>
        /// Hangs the gem off another element of the gear. The pose is restated for the new element
        /// first, so the gem stays where it was on the wearer instead of jumping: what changes is
        /// which bone it now follows, not where it sits.
        /// </summary>
        private void OnAttachChanged(string code, bool selected)
        {
            if (subject == null) return;

            // Per side: the near and the far gem often belong to different parts of the piece.
            CANGemVisualRegistry.SetAttachOverride(subject, side, code);
            // The sliders hold the place in the player's own space, so the gem stays where it is:
            // writing it back against the new bone is all that has to happen.
            Apply();
            RefreshSliders();
        }

        /// <summary>
        /// Whether the item is worn gear, which is drawn from the wearer's model in every target.
        /// </summary>
        private bool IsWearable()
        {
            return previewStack?.Collectible != null
                   && IAttachableToEntity.FromCollectible(previewStack.Collectible) != null;
        }

        /// <summary>
        /// The gear element the gem hangs off as things stand: the one picked here, or the one the
        /// rules answer with, or the first element of the gear when nothing says otherwise.
        /// </summary>
        private string EffectiveAttach()
        {
            if (previewStack == null) return null;

            string resolved = CANGemVisualRegistry.ResolveAttachElement(previewStack, socketIndex, side);
            if (resolved != null) return resolved;

            List<string> names = CANGemEntityShape.GearElementNames(capi, previewStack);
            return names.Count > 0 ? names[0] : null;
        }

        /// <summary>
        /// What a pose edit is written for: every cut of gem, or only the one being previewed. The
        /// general subject is the default — one placement usually serves every shape of gem, and a
        /// cut is parted from the rest only where it has to be.
        /// </summary>
        private string PoseSubject()
        {
            return CANGemVisualRegistry.WithVariant(subject,
                perCut ? cuttingType : null,
                perSize ? gemSize : (int?)null);
        }

        private void OnSocketChanged(string code, bool selected)
        {
            int.TryParse(code, out socketIndex);
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
        }

        private void OnGemTypeChanged(string code, bool selected)
        {
            gemType = code;
            FillSockets();
        }

        private void OnCuttingChanged(string code, bool selected)
        {
            cuttingType = code;
            FillSockets();
            // With per-cut tuning on, another cut means another set of poses under the sliders.
            if (perCut)
            {
                LoadCurrentPose();
                RefreshSliders();
            }
        }

        /// <summary>
        /// Switches between tuning one placement for every cut and giving the previewed cut its own.
        /// Turning it on starts from the general placement, so parting a cut off is a nudge rather
        /// than a fresh start.
        /// </summary>
        /// <summary>The subject poses are taken from, the first offered one unless it was picked.</summary>
        private string CopyFromSubject(List<string> sources)
        {
            return sources.Contains(copyFromSubject) ? copyFromSubject : sources[0];
        }

        private void OnCopyFromSubjectChanged(string code, bool selected)
        {
            copyFromSubject = code;
            pendingCopy = null;
        }

        /// <summary>Folds the group and copying rows in or out, and rebuilds the dialog around them.</summary>
        private bool OnToggleFamiliesPressed()
        {
            showFamilies = !showFamilies;
            ComposeDialog();
            return true;
        }

        /// <summary>
        /// Where "Copy there" sends the poses, defaulting to the first family of the item — the
        /// usual errand: this one is placed right, give it to the rest.
        /// </summary>
        private string CopyToSubject(List<string> destinations)
        {
            return destinations.Contains(copyToSubject) ? copyToSubject : destinations[0];
        }

        private void OnCopyToSubjectChanged(string code, bool selected)
        {
            copyToSubject = code;
            pendingCopy = null;
        }

        /// <summary>
        /// Brings another subject's placement over to the one being tuned: its tuned poses if it has
        /// any, otherwise what it looks like right now — which is what makes "give the whole family
        /// what this pickaxe has" work even when the pickaxe only follows a rule.
        /// </summary>
        private bool CopyInto(string from)
        {
            if (subject == null || from == null) return true;
            if (string.Equals(from, subject, StringComparison.InvariantCultureIgnoreCase))
            {
                capi.ShowChatMessage("[canjewelry] " + from + " is the subject being tuned");
                return true;
            }
            if (!Confirmed("from:" + from, "this replaces the poses of " + subject + " with those of " + from))
            {
                return true;
            }

            int copied = CANGemVisualRegistry.CopySubjectOverrides(from, subject);
            if (copied == 0) copied = SnapshotResolvedInto(StackFor(from), subject);
            if (copied == 0)
            {
                capi.ShowChatMessage("[canjewelry] nothing to copy from " + from);
                return true;
            }

            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            capi.ShowChatMessage("[canjewelry] copied " + copied + " pose(s) from " + from + " to " + subject);
            return true;
        }

        /// <summary>
        /// The same move the other way: the subject being tuned hands its placement to another one.
        /// Kept as its own direction because taking it would mean making the receiver the subject
        /// first, and the sliders are then no longer on the item that was just placed right.
        /// </summary>
        private bool CopyOut(string to)
        {
            if (subject == null || to == null) return true;
            if (string.Equals(to, subject, StringComparison.InvariantCultureIgnoreCase))
            {
                capi.ShowChatMessage("[canjewelry] " + to + " is the subject being tuned");
                return true;
            }
            if (!Confirmed("to:" + to, "this replaces the poses of " + to + " with those of " + subject))
            {
                return true;
            }

            int copied = CANGemVisualRegistry.CopySubjectOverrides(subject, to);
            // Nothing tuned by hand on this subject: what it looks like now is a rule's doing, and
            // that is still the placement being handed over.
            if (copied == 0) copied = SnapshotResolvedInto(previewStack, to);
            if (copied == 0)
            {
                capi.ShowChatMessage("[canjewelry] nothing to copy from " + subject);
                return true;
            }

            // The poses given away are not the ones under the sliders, so nothing about the preview
            // changes - only the mesh cache, which may hold items of the receiving family.
            EncrustableCB.ClearMeshCache(capi);
            capi.ShowChatMessage("[canjewelry] copied " + copied + " pose(s) from " + subject + " to " + to
                                 + " - written on export or when this menu closes");
            return true;
        }

        /// <summary>How long a press stays armed before it has to be made again.</summary>
        private const long ConfirmWindowMs = 8000;

        /// <summary>
        /// Every copy button asks twice: a copy writes over every pose the receiving subject has,
        /// and a family can be hundreds of items, none of which are on screen to show what was lost.
        /// The first press says in chat what it would do, the second one within a few seconds does
        /// it. Arming another button, or changing what is being tuned, calls the first one off.
        ///
        /// <para>Said in chat rather than shown on the button on purpose: laying the dialog out
        /// again would run through <c>capi.Gui.CreateCompo</c>, which disposes the composer of the
        /// very click being handled.</para>
        /// </summary>
        private bool Confirmed(string action, string what)
        {
            long now = capi.ElapsedMilliseconds;
            if (pendingCopy == action && now - pendingCopyAtMs <= ConfirmWindowMs)
            {
                pendingCopy = null;
                return true;
            }

            pendingCopy = action;
            pendingCopyAtMs = now;
            capi.ShowChatMessage("[canjewelry] " + what + " - press the same button again to confirm");
            return false;
        }

        private bool OnCopySubjectPressed()
            => CopyInto(SingleComposer.GetDropDown("copyfromsubject")?.SelectedValue ?? copyFromSubject);

        private bool OnCopyToSubjectPressed()
            => CopyOut(SingleComposer.GetDropDown("copytosubject")?.SelectedValue ?? copyToSubject);

        private bool OnCopyFromGroupPressed()
            => CopyInto(SingleComposer.GetDropDown("othergroup")?.SelectedValue ?? otherGroup);

        private bool OnCopyToGroupPressed()
            => CopyOut(SingleComposer.GetDropDown("othergroup")?.SelectedValue ?? otherGroup);

        private bool OnCopyFromCodePressed() => CopyInto(TypedSubject(out _));

        private bool OnCopyToCodePressed() => CopyOut(TypedSubject(out _));

        /// <summary>
        /// What a subject looks like right now, as a stack to resolve poses against: the item
        /// itself, or any member of a family. Null when the subject names nothing that exists.
        /// </summary>
        private ItemStack StackFor(string forSubject)
        {
            if (forSubject.StartsWith("$")) return GroupStack(forSubject);

            AssetLocation code = CodeOf(forSubject);
            return code == null ? null : SourceStack(code);
        }

        /// <summary>Every family there is, this item's or not: ours first, then the config's.</summary>
        private string[] AllGroupSubjects()
        {
            var names = new List<string>();
            foreach (string name in CANGemVisualRegistry.VisualGroups.Keys) names.Add("$" + name);

            var configGroups = canjewelry.config?.item_groups;
            if (configGroups != null)
            {
                foreach (string name in configGroups.Keys)
                {
                    if (!names.Contains("$" + name)) names.Add("$" + name);
                }
            }
            names.Sort(StringComparer.InvariantCultureIgnoreCase);
            return names.ToArray();
        }

        /// <summary>The family picked in the "any group" row, the first one until one is picked.</summary>
        private string OtherGroup(string[] groups)
        {
            return System.Array.IndexOf(groups, otherGroup) >= 0 ? otherGroup : groups[0];
        }

        private void OnOtherGroupChanged(string code, bool selected)
        {
            otherGroup = code;
            // A copy armed against the group picked a moment ago must not go off against this one.
            pendingCopy = null;
        }

        /// <summary>
        /// The subject typed into the "copy with" box: a group as it stands ("$falxes"), or an item
        /// code, which is looked up so a typo is caught here rather than becoming a subject nothing
        /// will ever match. Null when the box is empty or names nothing — the reason is said in chat.
        /// The code is handed back as well, to build a stack of the item from.
        /// </summary>
        private string TypedSubject(out AssetLocation code, bool complain = true)
        {
            code = null;
            string typed = (SingleComposer?.GetTextInput("copyfromcode")?.GetText() ?? copyFromCode)?.Trim();
            if (string.IsNullOrEmpty(typed))
            {
                if (complain)
                {
                    capi.ShowChatMessage("[canjewelry] type an item code first, e.g. pickaxe-steel");
                }
                return null;
            }
            copyFromCode = typed;

            // A group typed here works as well, though the row above offers every one of them.
            if (typed.StartsWith("$")) return typed;

            code = CodeOf(typed);
            if (code == null)
            {
                if (complain) capi.ShowChatMessage("[canjewelry] no item or block with the code " + typed);
                return null;
            }
            return code.ToShortString();
        }

        /// <summary>The typed code changed, so a copy armed against the previous one is called off.</summary>
        private void OnCopyCodeTyped(string text)
        {
            copyFromCode = text;
            pendingCopy = null;
        }

        /// <summary>The item or block of a typed code, null when the code names neither.</summary>
        private AssetLocation CodeOf(string typed)
        {
            var code = new AssetLocation(typed);
            Item item = capi.World.GetItem(code);
            if (item != null) return item.Code;

            Block block = capi.World.GetBlock(code);
            return block?.Code;
        }

        /// <summary>
        /// A stack of the named item, socketed like the preview, to ask what its gems look like
        /// today. Null when the code names nothing.
        /// </summary>
        private ItemStack SourceStack(AssetLocation code)
        {
            Item item = capi.World.GetItem(code);
            ItemStack stack = item != null
                ? new ItemStack(item)
                : capi.World.GetBlock(code) is Block block ? new ItemStack(block) : null;
            if (stack == null) return null;

            FillSockets(stack, EncrustableCB.GetMaxAmountSockets(stack));
            return stack;
        }

        /// <summary>
        /// An item of a pose family, socketed like the preview, to ask what the family's gems look
        /// like. Needed because a family's poses usually live in a rule file rather than in this
        /// session's tuning, and a rule can only be asked about an item.
        ///
        /// <para>A member with a rule of its own answers with that rule rather than the family's,
        /// this being the first member found; it is the family being copied from that says which
        /// items those are.</para>
        /// </summary>
        private ItemStack GroupStack(string group)
        {
            string name = group.StartsWith("$") ? group.Substring(1) : group;

            foreach (Item item in capi.World.Items)
            {
                if (item?.Code == null) continue;

                bool member = false;
                foreach (string found in CANGemVisualRegistry.FindGroups(item.Code))
                {
                    if (!string.Equals(found, name, StringComparison.InvariantCultureIgnoreCase)) continue;
                    member = true;
                    break;
                }
                if (!member) continue;

                var stack = new ItemStack(item);
                int sockets = EncrustableCB.GetMaxAmountSockets(stack);
                if (sockets <= 0) continue;

                FillSockets(stack, sockets);
                return stack;
            }
            return null;
        }

        /// <summary>
        /// Writes what an item resolves to right now onto a subject — every target, both sides,
        /// every socket. Used when the source has nothing tuned of its own: what it looks like is
        /// then the placement being handed over.
        /// </summary>
        private int SnapshotResolvedInto(ItemStack fromStack, string toSubject)
        {
            if (fromStack == null) return 0;

            // The preview answers gui resolves with the target being tuned; asking for the real
            // targets here means putting that aside for the duration.
            string redirect = CANGemVisualRegistry.DebugTargetRedirect;
            CANGemVisualRegistry.DebugTargetRedirect = null;
            int written = 0;
            try
            {
                foreach (string poseTarget in targets)
                {
                    for (int poseSide = 0; poseSide < CANGemVisualSide.Count; poseSide++)
                    {
                        for (int i = 0; i < System.Math.Max(1, maxSockets); i++)
                        {
                            ModelTransform pose = CANGemVisualRegistry.ResolvePose(fromStack, i, poseTarget, poseSide);
                            if (pose == null) continue;

                            CANGemVisualRegistry.SetOverride(toSubject, poseTarget, i, poseSide, pose.Clone());
                            written++;
                        }
                    }
                }
            }
            finally
            {
                CANGemVisualRegistry.DebugTargetRedirect = redirect;
            }
            return written;
        }

        /// <summary>The cut poses are taken from, defaulting to one that is not the previewed one.</summary>
        private string CopyFromCut()
        {
            if (copyFromCut != null && copyFromCut != cuttingType) return copyFromCut;

            foreach (string cut in cuttingTypes)
            {
                if (cut != cuttingType) return cut;
            }
            return cuttingTypes[0];
        }

        private void OnCopyFromCutChanged(string code, bool selected)
        {
            copyFromCut = code;
        }

        /// <summary>
        /// Takes every pose of the picked cut onto the previewed one — all sockets, both sides, all
        /// targets — and switches to tuning this cut alone, since that is what was just made.
        /// </summary>
        private bool OnCopyCutPressed()
        {
            if (subject == null) return true;

            int copied = CANGemVisualRegistry.CopyCutOverrides(subject, CopyFromCut(), cuttingType,
                perSize ? gemSize : (int?)null);
            if (copied == 0)
            {
                capi.ShowChatMessage("[canjewelry] nothing tuned for " + CopyFromCut() + " to copy");
                return true;
            }

            perCut = true;
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            capi.ShowChatMessage("[canjewelry] copied " + copied + " pose(s) from " + CopyFromCut()
                                 + " to " + cuttingType);
            return true;
        }

        /// <summary>
        /// Drops what this cut had of its own, so it sits where every other cut does again.
        /// </summary>
        private bool OnResetCutPressed()
        {
            if (subject == null) return true;

            ForgetCut(cuttingType);
            perCut = false;
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            capi.ShowChatMessage("[canjewelry] " + cuttingType + " is back on the general placement");
            return true;
        }

        /// <summary>
        /// Drops everything one cut has of its own — this session's tuning, the files it was written
        /// into per size, and the rules read from them. Anything left behind is searched before the
        /// general placement and would go on winning.
        /// </summary>
        private void ForgetCut(string cut)
        {
            CANGemVisualRegistry.ClearVariantOverrides(subject, cut, null);

            Forget(CANGemVisualRegistry.WithVariant(subject, cut, null));
            for (int size = 1; size <= sizeLabels.Length; size++)
            {
                Forget(CANGemVisualRegistry.WithVariant(subject, cut, size));
            }
        }

        private void OnPerCutToggled(bool on)
        {
            perCut = on;
            // Off, the poses written for this cut have to go: they are searched before the general
            // one, so leaving them behind would keep them on screen while the sliders wrote into the
            // general placement. What is under the sliders right now becomes that general placement.
            if (!on) ForgetCut(cuttingType);
            Apply();
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
        }

        private void OnSizeChanged(string code, bool selected)
        {
            int.TryParse(code, out gemSize);
            FillSockets();
            // With per-size tuning on, another size means another set of poses under the sliders.
            if (perSize)
            {
                LoadCurrentPose();
                RefreshSliders();
            }
        }

        /// <summary>
        /// Switches between one placement for every size of gem and a placement for the previewed
        /// size alone. Same idea as the cut switch, and it starts from the general placement too.
        /// </summary>
        /// <summary>
        /// Turns the gems of this subject off and on. Off, no gem is drawn on it in any target, and
        /// the rules below are not consulted either — which is how one family is left alone while
        /// another is being worked on.
        /// </summary>
        private void OnDrawGemsToggled(bool on)
        {
            if (subject == null) return;

            CANGemVisualRegistry.SetHiddenOverride(subject, !on);
            Invalidate();
        }

        /// <summary>
        /// Whether gems are drawn for the current subject: what the switch was last set to, and
        /// failing that what the rules say. Reading the rules matters for a family shipped hidden —
        /// armour, until its poses are tuned — where a switch showing "on" over an empty preview
        /// would be the menu contradicting itself.
        /// </summary>
        private bool DrawGemsOn()
        {
            bool? tuned = CANGemVisualRegistry.GetHiddenOverride(subject);
            if (tuned != null) return !tuned.Value;

            return previewStack == null || !CANGemVisualRegistry.HiddenByRule(previewStack);
        }

        /// <summary>
        /// Ten times finer slider steps, same range. The values themselves do not change — only how
        /// small a nudge the slider can make.
        /// </summary>
        private void OnFineToggled(bool on)
        {
            fine = on;
            RefreshSliders();
        }

        private void OnPerSizeToggled(bool on)
        {
            perSize = on;
            // Same as the cut switch: the poses of this size would otherwise keep outranking the
            // general one after the switch says they no longer apply.
            if (!on)
            {
                CANGemVisualRegistry.ClearVariantOverrides(subject, null, gemSize);
                Forget(CANGemVisualRegistry.WithVariant(subject, null, gemSize));
                foreach (string cut in cuttingTypes)
                {
                    Forget(CANGemVisualRegistry.WithVariant(subject, cut, gemSize));
                }
            }
            Apply();
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
        }

        private bool OnTranslateX(int steps) => SliderMoved("tx", steps);
        private bool OnTranslateY(int steps) => SliderMoved("ty", steps);
        private bool OnTranslateZ(int steps) => SliderMoved("tz", steps);
        private bool OnRotateX(int steps) => SliderMoved("rx", steps);
        private bool OnRotateY(int steps) => SliderMoved("ry", steps);
        private bool OnRotateZ(int steps) => SliderMoved("rz", steps);
        private bool OnScaleChanged(int steps) => SliderMoved("scale", steps);

        /// <summary>
        /// A slider dragged to a whole step, which is a hundredth of a block or a degree — or a
        /// tenth of those with fine tuning on. The box beside it is written along, so the two never
        /// disagree, and so a dragged value can then be nudged by hand to any value at all.
        /// </summary>
        private bool SliderMoved(string key, int steps)
        {
            SetValueOf(key, steps / StepsPerUnit(key));
            Apply();
            SyncValueBox(key);
            return true;
        }

        /// <summary>
        /// Declares the family typed into the two boxes and switches to it, so the next slider move
        /// tunes the whole family. Left empty, the patterns default to the obvious family of the
        /// item in hand — every material of the same tool.
        /// </summary>
        private bool OnCreateGroupPressed()
        {
            string name = newGroupName?.Trim();
            if (string.IsNullOrEmpty(name) || previewStack?.Collectible?.Code == null)
            {
                capi.ShowChatMessage("[canjewelry] type a name for the group first");
                return true;
            }

            string[] patterns = (newGroupPatterns ?? "")
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
            if (patterns.Length == 0)
            {
                patterns = new[] { CANGemVisualRegistry.SuggestPattern(previewStack.Collectible.Code) };
            }

            CANGemVisualRegistry.DefineGroup(name, patterns);
            // Written out at once rather than waiting for the export: a group with no pose of its
            // own yet is not among the tuned subjects, so closing the menu would not save it and the
            // family would be gone on the next launch.
            Writer().ExportGroup(name, patterns);
            capi.ShowChatMessage("[canjewelry] group " + name + ": " + string.Join(", ", patterns));

            subject = "$" + name;
            BuildPreviewStack();
            LoadCurrentPose();
            Invalidate();
            ComposeDialog();
            return true;
        }

        private void OnAddAsWildcardToggled(bool on)
        {
            addAsWildcard = on;
        }

        /// <summary>
        /// Writes the patterns typed into the "patterns" box onto the family being tuned, in place
        /// of the ones it had. The way to narrow a family declared with a wildcard — "Add this item"
        /// leaves it alone, the wildcard already speaking for the item.
        /// </summary>
        private bool OnApplyPatternsPressed()
        {
            string name = subject != null && subject.StartsWith("$") ? subject.Substring(1) : null;
            if (name == null) return true;
            if (CANGemVisualRegistry.GroupPatterns(name) == null)
            {
                capi.ShowChatMessage("[canjewelry] " + subject + " is a config item group, not one of ours - "
                                     + "its members are the server's to say");
                return true;
            }

            string typed = SingleComposer.GetTextInput("editpatterns")?.GetText() ?? groupPatternsEdit;
            string[] patterns = (typed ?? "")
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
            if (patterns.Length == 0)
            {
                capi.ShowChatMessage("[canjewelry] a group with no patterns holds nothing - "
                                     + "type at least one code, or use Reset to drop the group");
                return true;
            }

            CANGemVisualRegistry.DefineGroup(name, patterns);
            Writer().ExportGroup(name, patterns);
            EncrustableCB.ClearMeshCache(capi);

            capi.ShowChatMessage("[canjewelry] " + subject + ": " + string.Join(", ", patterns));
            // The item in hand may have just left the family, which changes the subject list.
            BuildPreviewStack();
            LoadCurrentPose();
            Invalidate();
            ComposeDialog();
            return true;
        }

        /// <summary>
        /// Puts the previewed item into the family being tuned, so the family's poses start speaking
        /// for it. The mirror of <see cref="OnExcludeItemPressed"/>, and the way an item whose code
        /// none of the family's patterns catch joins it at all.
        /// </summary>
        private bool OnAddItemPressed()
        {
            string name = subject != null && subject.StartsWith("$") ? subject.Substring(1) : null;
            AssetLocation code = previewStack?.Collectible?.Code;
            if (code == null) return true;
            if (name == null)
            {
                capi.ShowChatMessage("[canjewelry] pick the group to put this item into in 'subject' first");
                return true;
            }
            if (CANGemVisualRegistry.GroupPatterns(name) == null)
            {
                capi.ShowChatMessage("[canjewelry] " + subject + " is a config item group, not one of ours - "
                                     + "make a group of your own to put items into");
                return true;
            }

            string pattern = addAsWildcard ? CANGemVisualRegistry.SuggestPattern(code) : code.ToShortString();
            string[] updated = CANGemVisualRegistry.AddToGroup(name, code, pattern,
                out string blockedBy, out string coveredBy);
            if (blockedBy != null)
            {
                capi.ShowChatMessage("[canjewelry] " + code.ToShortString() + " is kept out of " + subject
                                     + " by the pattern " + blockedBy + ", which speaks for more items than "
                                     + "this one - edit the patterns of the group to let it in");
                return true;
            }
            if (coveredBy != null)
            {
                capi.ShowChatMessage("[canjewelry] " + code.ToShortString() + " is already in " + subject
                                     + " through the pattern " + coveredBy
                                     + (coveredBy.Contains("*")
                                         ? " - if the group was meant to hold named items only, edit its "
                                           + "patterns in the 'patterns' row"
                                         : ""));
                return true;
            }
            if (updated == null) return true;

            CANGemVisualRegistry.DefineGroup(name, updated);
            Writer().ExportGroup(name, updated);
            EncrustableCB.ClearMeshCache(capi);
            // The patterns box is showing the wording from before this change.
            groupPatternsEditFor = null;

            capi.ShowChatMessage("[canjewelry] " + code.ToShortString() + " is in " + subject + ": "
                                 + string.Join(", ", updated));
            BuildPreviewStack();
            LoadCurrentPose();
            Invalidate();
            ComposeDialog();
            return true;
        }

        /// <summary>
        /// Adds the previewed item to the current family as an exclusion, so the family stops
        /// speaking for it and it falls back to its own rules.
        /// </summary>
        private bool OnExcludeItemPressed()
        {
            string name = subject != null && subject.StartsWith("$") ? subject.Substring(1) : null;
            string code = previewStack?.Collectible?.Code?.ToShortString();
            if (code == null) return true;
            if (name == null)
            {
                capi.ShowChatMessage("[canjewelry] pick the group to drop this item out of in 'subject' first");
                return true;
            }

            string[] patterns = CANGemVisualRegistry.GroupPatterns(name);
            if (patterns == null)
            {
                capi.ShowChatMessage("[canjewelry] " + subject + " is a config item group, not one of ours - "
                                     + "make a group of your own to exclude items from it");
                return true;
            }

            string exclusion = "!" + code;
            if (System.Array.Exists(patterns,
                p => string.Equals(p, exclusion, System.StringComparison.InvariantCultureIgnoreCase)))
            {
                capi.ShowChatMessage("[canjewelry] " + code + " is already out of " + subject);
                return true;
            }

            var updated = new string[patterns.Length + 1];
            System.Array.Copy(patterns, updated, patterns.Length);
            updated[patterns.Length] = exclusion;

            CANGemVisualRegistry.DefineGroup(name, updated);
            Writer().ExportGroup(name, updated);
            EncrustableCB.ClearMeshCache(capi);
            // The patterns box is showing the wording from before this change.
            groupPatternsEditFor = null;

            capi.ShowChatMessage("[canjewelry] " + code + " is out of " + subject + ": "
                                 + string.Join(", ", updated));
            BuildPreviewStack();
            LoadCurrentPose();
            Invalidate();
            ComposeDialog();
            return true;
        }

        private bool OnCopyPressed()
        {
            clipboard = current.Clone();
            capi.ShowChatMessage("[canjewelry] copied the pose of socket " + socketIndex);
            return true;
        }

        /// <summary>Puts the copied pose on the socket and side that are picked now.</summary>
        private bool OnPastePressed()
        {
            if (clipboard == null) return true;

            current = clipboard.Clone();
            Apply();
            RefreshSliders();
            return true;
        }

        /// <summary>
        /// Puts the copied pose on every socket of the current target and side at once. Useful for
        /// a row of identical sockets, which is then nudged apart one by one.
        /// </summary>
        private bool OnPasteAllPressed()
        {
            if (clipboard == null || subject == null) return true;

            // The same subject the sliders write to, so the paste lands where "this cut only" and
            // "this size only" say it should rather than always in the general placement.
            string poseSubject = PoseSubject();
            for (int i = 0; i < System.Math.Max(1, maxSockets); i++)
            {
                CANGemVisualRegistry.SetOverride(poseSubject, target, i, side, clipboard.Clone());
            }
            current = clipboard.Clone();
            Invalidate();
            RefreshSliders();
            return true;
        }

        /// <summary>Drops every tuned pose of the current subject, back to what the assets say.</summary>
        private bool OnResetPressed()
        {
            if (subject == null) return true;

            CANGemVisualRegistry.ClearOverrides(subject);

            // Everything this subject was ever written into: the general file and one per cut and
            // size. A reset that left them would be undone at the next launch.
            Forget(subject);
            for (int size = 1; size <= sizeLabels.Length; size++)
            {
                Forget(CANGemVisualRegistry.WithVariant(subject, null, size));
            }
            foreach (string cut in cuttingTypes) ForgetCut(cut);

            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            return true;
        }

        private bool OnExportPressed()
        {
            if (subject == null) return true;

            if (!Export(subject, toClipboard: true))
            {
                capi.ShowChatMessage("[canjewelry] nothing tuned for " + subject + " yet");
                return true;
            }

            // The tuning has become a rule and has been dropped, so the sliders are reloaded from
            // what the item resolves to now - which is what the next launch will show as well. A gem
            // that moves here is a rule of a narrower tier taking over, not a lost pose.
            LoadCurrentPose();
            RefreshSliders();
            Invalidate();
            return true;
        }

        private CANGemVisualRuleWriter Writer()
        {
            return new CANGemVisualRuleWriter(capi, previewStack, maxSockets, targets);
        }

        /// <summary>Hands a subject to the writer, which turns it into a rule and saves it.</summary>
        private bool Export(string forSubject, bool toClipboard = false)
        {
            return Writer().Export(forSubject, toClipboard);
        }

        /// <summary>
        /// Takes a subject's written rule back off — the file and the rules read from it. A reset
        /// that only cleared this session would be undone by the file at the next launch, and by
        /// the loaded rule right away.
        /// </summary>
        private void Forget(string forSubject)
        {
            Writer().Forget(forSubject);
        }


        // The preview owns a framebuffer, so it is rendered before the gui pass - switching
        // framebuffers mid-pass would disturb the dialog itself.
        public override void OnRenderGUI(float deltaTime)
        {
            itemPreview?.Render(previewStack);
            base.OnRenderGUI(deltaTime);
        }

        public override void Dispose()
        {
            base.Dispose();
            itemPreview?.Dispose();
            itemPreview = null;
        }
    }
}
