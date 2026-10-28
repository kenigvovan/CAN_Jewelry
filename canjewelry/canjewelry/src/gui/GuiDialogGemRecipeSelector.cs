using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace canjewelry.src.gui
{
    /// <summary>
    /// The vanilla recipe selector, but each recipe carries its own text - the forecast for the gem
    /// on the table - with room for several lines under the grid.
    /// </summary>
    public class GuiDialogGemRecipeSelector : GuiDialogGeneric
    {
        private readonly BlockPos blockEntityPos;
        private readonly List<SkillItem> skillItems = new List<SkillItem>();
        private readonly Action<int> onSelectedRecipe;
        private readonly Action onCancelSelect;
        private int prevSlotOver = -1;
        private bool didSelect;

        public GuiDialogGemRecipeSelector(ItemStack[] outputs, string[] descriptions, Action<int> onSelectedRecipe, Action onCancelSelect,
            BlockPos blockEntityPos, ICoreClientAPI capi)
            : base(Lang.Get("Select Recipe"), capi)
        {
            this.blockEntityPos = blockEntityPos;
            this.onSelectedRecipe = onSelectedRecipe;
            this.onCancelSelect = onCancelSelect;

            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            for (int i = 0; i < outputs.Length; i++)
            {
                var dummySlot = new DummySlot(outputs[i]);
                skillItems.Add(new SkillItem
                {
                    Code = outputs[i].Collectible.Code.Clone(),
                    Name = outputs[i].GetName(),
                    Description = descriptions != null && i < descriptions.Length ? descriptions[i] ?? "" : "",
                    RenderHandler = (code, dt, posX, posY) =>
                    {
                        double scsize = GuiElement.scaled(size - 5.0);
                        capi.Render.RenderItemstackToGui(dummySlot, posX + scsize / 2.0, posY + scsize / 2.0, 100.0,
                            (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize), -1, true, false, true);
                    }
                });
            }
            SetupDialog();
        }

        private void SetupDialog()
        {
            int count = Math.Max(1, skillItems.Count);
            int cols = Math.Min(count, 7);
            int rows = (int)Math.Ceiling(count / (float)cols);
            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double innerWidth = Math.Max(340.0, cols * size);

            ElementBounds gridBounds = ElementBounds.Fixed(0.0, 30.0, innerWidth, rows * size);
            ElementBounds nameBounds = ElementBounds.Fixed(0.0, rows * size + 50.0, innerWidth, 30.0);
            ElementBounds descBounds = nameBounds.BelowCopy(0.0, 5.0).WithFixedHeight(DescHeight());
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            SingleComposer = capi.Gui.CreateCompo("canjewelry-gemrecipeselect" + blockEntityPos, ElementStdBounds.AutosizedMainDialog)
                .AddShadedDialogBG(bgBounds, true, 5.0, 0.75f)
                .AddDialogTitleBar(Lang.Get("Select Recipe"), () => TryClose())
                .BeginChildElements(bgBounds)
                .AddSkillItemGrid(skillItems, cols, rows, OnSlotClick, gridBounds, "skillitemgrid")
                .AddDynamicText("", CairoFont.WhiteSmallishText(), nameBounds, "name")
                .AddDynamicText("", CairoFont.WhiteDetailText(), descBounds, "desc")
                .EndChildElements()
                .Compose();
            SingleComposer.GetSkillItemGrid("skillitemgrid").OnSlotOver = OnSlotOver;
        }

        // Tall enough for the longest description, so hovering never moves the dialog.
        private double DescHeight()
        {
            int lines = 1;
            foreach (var item in skillItems)
            {
                lines = Math.Max(lines, item.Description.Split('\n').Length);
            }
            return lines * 20.0 + 10.0;
        }

        private void OnSlotOver(int num)
        {
            if (num >= skillItems.Count || num == prevSlotOver) return;
            prevSlotOver = num;
            SingleComposer.GetDynamicText("name").SetNewText(skillItems[num].Name);
            SingleComposer.GetDynamicText("desc").SetNewText(skillItems[num].Description);
        }

        private void OnSlotClick(int num)
        {
            onSelectedRecipe(num);
            didSelect = true;
            TryClose();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (!didSelect) onCancelSelect();
        }

        public override bool PrefersUngrabbedMouse => false;
    }
}
