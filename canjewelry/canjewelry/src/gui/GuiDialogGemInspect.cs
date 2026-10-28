using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src.cb;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canjewelry.src.gui
{
    /// <summary>
    /// The loupe's inspection: the rough gem large in a preview the player turns by dragging,
    /// its defects showing inside it depending on the angle and the light. A click (a press that
    /// barely moves) is sent to the server, which decides whether it found something.
    /// </summary>
    public class GuiDialogGemInspect : GuiDialog
    {
        private const int PreviewSize = 400;

        private readonly JewelerItemPreview preview;
        private GemInspectStatePacket state;
        private ItemStack gem;
        private bool finishedByServer;

        public string SessionId => state?.SessionId;
        public override string ToggleKeyCombinationCode => null;

        public GuiDialogGemInspect(ICoreClientAPI capi, GemInspectStatePacket state) : base(capi)
        {
            preview = new JewelerItemPreview(capi, canjewelry.config?.GemCarats?.Inspection?.FboSize ?? PreviewSize);
            Update(state);
        }

        public void Update(GemInspectStatePacket newState)
        {
            state = newState;
            Item item = capi.World.GetItem(new AssetLocation(newState.GemCode ?? ""));
            gem = item == null ? null : new ItemStack(item);
            gem?.Attributes.SetBool(render.CANGemInspectMesh.ViewKey, true);
            if (newState.ShapeSeed != 0) gem?.Attributes.SetInt(CANGemRoughShape.SeedKey, newState.ShapeSeed);
            if (newState.Finished)
            {
                finishedByServer = true;
                TryClose();
                return;
            }
            Compose();
        }

        private List<GemDefect> Defects()
        {
            var list = new List<GemDefect>();
            if (state?.DefectTraits == null || state.DefectPos == null) return list;
            for (int i = 0; i < state.DefectTraits.Length && state.DefectPos.Length >= i * 3 + 3; i++)
            {
                float[] n = state.DefectNormals != null && state.DefectNormals.Length >= i * 3 + 3
                    ? state.DefectNormals.Skip(i * 3).Take(3).ToArray() : null;
                list.Add(new GemDefect
                {
                    Trait = state.DefectTraits[i],
                    Pos = state.DefectPos.Skip(i * 3).Take(3).ToArray(),
                    Normal = n != null && (n[0] != 0 || n[1] != 0 || n[2] != 0) ? n : null
                });
            }
            return list;
        }

        // ---- lighting modes ----

        // How the gem is lit: every mode the light here and a wet gem allow, often more than one.
        private string[] modes = new string[0];
        // The light the dialog was last composed for, to recompose when it changes.
        private int shownLight = -1;
        private bool shownWater;

        private Dictionary<string, Config.InspectModeConfig> Modes
            => canjewelry.config?.GemCarats?.Inspection?.Modes ?? new Dictionary<string, Config.InspectModeConfig>();

        private int LightHere()
        {
            EntityPlayer player = capi.World.Player?.Entity;
            return player == null ? 0 : capi.World.BlockAccessor.GetLightLevel(player.Pos.AsBlockPos, EnumLightLevelType.MaxTimeOfDayLight);
        }

        // The gem itself, dipped in water before the inspection: dries out as the game hours pass.
        private bool InWaterHere() => state != null && state.WetUntil > capi.World.Calendar.TotalHours;

        private string[] ModesHere() => CANGemDefects.ModesFor(canjewelry.config?.GemCarats?.Inspection, LightHere(), InWaterHere());

        /// <summary>Every mode with its light range: the ones on now bright, the others greyed out.</summary>
        private string ModesVtml()
        {
            var parts = new List<string>();
            foreach (var pair in Modes)
            {
                // What the mode shows, so the player knows where to go for which flaw.
                float min = canjewelry.config?.GemCarats?.Inspection?.MinVisibility ?? 0.35f;
                string[] shows = pair.Value.Visibility?.Where(v => v.Value >= min).Select(v => v.Key).ToArray() ?? new string[0];
                string condition = pair.Value.RequiresWater
                    ? Lang.Get("canjewelry:inspect-mode-inwater")
                    : Lang.Get("canjewelry:inspect-light-range", pair.Value.MinLight, pair.Value.MaxLight);
                string name = Lang.Get("canjewelry:inspect-mode-" + pair.Key) + " (" + condition + ")"
                    + (shows.Length > 0 ? " — " + CANGemAppraisalCB.FormatTraits(shows) : "");
                // One mode a line; the font has no arrow glyphs, so a bullet marks the active ones.
                parts.Add(modes.Contains(pair.Key)
                    ? "<font color=\"#FFE680\"><strong>• " + name + "</strong></font>"
                    : "<font color=\"#707070\">" + name + "</font>");
            }
            return Lang.Get("canjewelry:inspect-light-here", LightHere()) + "<br>" + string.Join("<br>", parts);
        }

        private void Compose()
        {
            modes = ModesHere();

            // Two parts side by side: the gem with no backdrop at all, hanging over the world it
            // is looked at in, and the panel with everything to read.
            int size = preview.FboSize;
            const int column = 380;
            const int gap = 20;
            double panelWidth = column + 2 * GuiStyle.ElementToDialogPadding;

            ElementBounds gemDialog = ElementBounds.Fixed(EnumDialogArea.CenterMiddle,
                -(panelWidth + gap) / 2, 0, size, size);
            ElementBounds previewBounds = ElementBounds.Fixed(0, 0, size, size);

            foreach (var old in Composers.Values.ToList()) old.Dispose();
            // Only the gem over the world; the hint on how to handle it is in the panel.
            Composers["gem"] = capi.Gui.CreateCompo("canjewelry-geminspect-gem", gemDialog)
                .AddInteractiveElement(new GuiElementGemInspect(capi, previewBounds, preview, this), "preview")
                .Compose();

            // The text decides the panel's height: composed once to measure it, again to fit it,
            // with the button right under it - a fixed height let the text run over the button.
            string info = InfoVtml();
            double textHeight = 200;
            for (int pass = 0; pass < 2; pass++)
            {
                double top = GuiStyle.TitleBarHeight + 8;
                ElementBounds infoBounds = ElementBounds.Fixed(0, top, column, textHeight);
                ElementBounds doneBounds = ElementBounds.Fixed(0, top + textHeight + 10, column, 28);
                ElementBounds bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
                bg.BothSizing = ElementSizing.FitToChildren;
                bg.WithChildren(infoBounds, doneBounds);
                ElementBounds panelDialog = ElementStdBounds.AutosizedMainDialog
                    .WithAlignment(EnumDialogArea.CenterMiddle)
                    .WithFixedAlignmentOffset((size + gap) / 2.0, 0);

                Composers["panel"]?.Dispose();
                Composers["panel"] = capi.Gui.CreateCompo("canjewelry-geminspect-panel", panelDialog)
                    .AddShadedDialogBG(bg)
                    .AddDialogTitleBar(Lang.Get("canjewelry:inspect-title"), () => TryClose())
                    .BeginChildElements(bg)
                        .AddRichtext(info, CairoFont.WhiteSmallText(), infoBounds, "info")
                        .AddSmallButton(Lang.Get("canjewelry:inspect-done"), () => TryClose(), doneBounds)
                    .EndChildElements()
                    .Compose();

                // Measured in screen pixels; the bounds are in unscaled ones.
                double measured = Composers["panel"].GetRichtext("info").TotalHeight / RuntimeEnv.GUIScale;
                if (measured <= textHeight) break;
                textHeight = Math.Ceiling(measured) + 4;
            }
        }

        private static string Header(string text) => "<font color=\"#E8C77A\"><strong>" + text + "</strong></font><br>";

        /// <summary>The right column: how to do it, the light, and the gem as it stands.</summary>
        private string InfoVtml()
        {
            var sb = new System.Text.StringBuilder();

            sb.Append("<font color=\"#A8A8A8\"><i>").Append(Lang.Get("canjewelry:inspect-drag-hint")).Append("</i></font><br><br>");
            sb.Append(Header(Lang.Get("canjewelry:inspect-section-how")));
            for (int i = 1; i <= 4; i++)
            {
                sb.Append("<font color=\"#C8C8C8\">").Append(i).Append(". ").Append(Lang.Get("canjewelry:inspect-help-" + i)).Append("</font><br>");
            }

            sb.Append("<br>").Append(Header(Lang.Get("canjewelry:inspect-section-light")));
            sb.Append(ModesVtml()).Append("<br>");
            sb.Append("<font color=\"#C8C8C8\"><i>").Append(modes.Length == 0
                ? Lang.Get("canjewelry:inspect-mode-none")
                : string.Join("<br>", modes.Select(m => Lang.Get("canjewelry:inspect-mode-hint-" + m)))).Append("</i></font><br>");

            sb.Append("<br>").Append(Header(Lang.Get("canjewelry:inspect-section-gem")));
            sb.Append(Lang.Get("canjewelry:inspect-clicks", "<font color=\"#FFFFFF\"><strong>" + state.Attempts + "</strong></font>")).Append("<br>");
            string[] seenTraits = CANGemAppraisalCB.ShownTraits(state.SeenTraits);
            string seen = seenTraits.Length > 0 ? CANGemAppraisalCB.FormatTraits(seenTraits) : "-";
            sb.Append(Lang.Get("canjewelry:inspect-seen", seen)).Append("<br>");

            if (!string.IsNullOrEmpty(state.LastResult))
            {
                string color = state.LastResult == "miss" ? "#E07060" : state.LastResult == "unavailable" ? "#A0A0A0" : "#80D080";
                string last = state.LastResult == "miss" ? Lang.Get("canjewelry:inspect-miss")
                    : state.LastResult == "unavailable" ? Lang.Get("canjewelry:inspect-unavailable")
                    : Lang.Get("canjewelry:inspect-removed", Lang.Get("canjewelry:gem-trait-" + state.LastResult));
                sb.Append("<font color=\"").Append(color).Append("\">").Append(last).Append("</font>");
            }
            return sb.ToString();
        }

        public override void OnRenderGUI(float deltaTime)
        {
            // The player walked into the shade or the sun: the lighting, and the text, follow.
            int light = LightHere();
            bool water = InWaterHere();
            if (state != null && !state.Finished && (light != shownLight || water != shownWater))
            {
                shownLight = light;
                shownWater = water;
                Compose();
            }
            RefreshMesh();
            preview.Render(gem);
            base.OnRenderGUI(deltaTime);
        }

        /// <summary>
        /// Puts on the gem the flaws that show in this light from this side, as cubes. Rebuilt
        /// only when that set changes - turning the gem brings cracks in and out of sight.
        /// </summary>
        private void RefreshMesh()
        {
            if (gem?.Item == null || state == null) return;
            var shown = new List<GemDefect>();
            var insp = canjewelry.config?.GemCarats?.Inspection;
            if (insp != null && modes.Length > 0)
            {
                GemView view = CANGemInspectionServer.ViewFor(capi, gem, preview.RotationX, preview.RotationY, RuntimeEnv.GUIScale, preview.FboSize);
                shown = Defects().Where(d => CANGemDefects.Visibility(view, d, CANGemDefects.CombinedVisibility(insp, modes, d.Trait)) >= insp.MinVisibility).ToList();
            }
            float halfLength = insp?.CrackHalfLength ?? 0.35f;
            if (render.CANGemInspectMesh.Update(capi, gem, shown, string.Join(",", modes), halfLength))
            {
                preview.MarkDirty();
            }
        }

        internal void OnPreviewClick(ElementBounds bounds, int mouseX, int mouseY)
        {
            if (state == null || state.Attempts <= 0) return;
            float k = (float)(preview.FboSize / bounds.InnerWidth);
            float[][] t = CANGemInspectionServer.Transform(gem?.Collectible);
            // The server places defects in the same box from the same shape, so only the transform travels.
            canjewelry.clientChannel?.SendPacket(new GemInspectClickPacket
            {
                SessionId = state.SessionId,
                X = (float)((mouseX - bounds.renderX) * k),
                Y = (float)((mouseY - bounds.renderY) * k),
                RotationX = preview.RotationX,
                RotationY = preview.RotationY,
                GuiScale = RuntimeEnv.GUIScale,
                Origin = t[0],
                Translation = t[1],
                Scale = t[2],
                BaseRotation = t[3]
            });
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (!finishedByServer && state != null)
            {
                canjewelry.clientChannel?.SendPacket(new GemInspectClosePacket { SessionId = state.SessionId });
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            preview.Dispose();
            render.CANGemInspectMesh.Dispose();
        }
    }

    /// <summary>The preview of the inspection: left drag turns the gem, a click is a look for a defect.</summary>
    public class GuiElementGemInspect : GuiElement
    {
        private const int ClickSlack = 4;

        private readonly JewelerItemPreview preview;
        private readonly GuiDialogGemInspect dialog;
        private bool down;
        private int startX, startY, lastX, lastY;

        public GuiElementGemInspect(ICoreClientAPI capi, ElementBounds bounds, JewelerItemPreview preview, GuiDialogGemInspect dialog)
            : base(capi, bounds)
        {
            this.preview = preview;
            this.dialog = dialog;
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (preview.TextureId <= 0) return;
            // No backdrop: the world itself is behind the gem.
            api.Render.Render2DTexture(preview.TextureId, (float)Bounds.renderX, (float)Bounds.renderY,
                (float)Bounds.InnerWidth, (float)Bounds.InnerHeight);
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);
            down = true;
            startX = lastX = args.X;
            startY = lastY = args.Y;
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseMove(api, args);
            if (!down) return;
            preview.RotationY -= (args.X - lastX) * 0.5f;
            preview.RotationX -= (args.Y - lastY) * 0.5f;
            lastX = args.X;
            lastY = args.Y;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseUp(api, args);
            if (!down) return;
            down = false;
            if (Math.Abs(args.X - startX) <= ClickSlack && Math.Abs(args.Y - startY) <= ClickSlack
                && Bounds.PointInside(args.X, args.Y))
            {
                dialog.OnPreviewClick(Bounds, args.X, args.Y);
            }
        }
    }
}
