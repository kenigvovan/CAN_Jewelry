using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canjewelry.src.gui
{
    /// <summary>
    /// An item drawn on its own, with no slot around it, and the usual item tooltip on hover.
    /// Nothing can be taken from it: it only shows a copy of the stack.
    /// </summary>
    public class GuiElementItemIcon : GuiElement
    {
        private readonly DummySlot slot;
        // Borrowed from richtext itemstacks: builds and places the same tooltip the handbook shows.
        private readonly ItemstackComponentBase tooltip;

        public GuiElementItemIcon(ICoreClientAPI capi, ElementBounds bounds, ItemStack stack) : base(capi, bounds)
        {
            slot = new DummySlot(stack);
            tooltip = new ItemstackComponentBase(capi);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (slot.Itemstack == null) return;

            // Same share of the box an item takes in a regular 48px slot.
            float size = (float)(Math.Min(Bounds.InnerWidth, Bounds.InnerHeight) * 0.55);
            api.Render.RenderItemstackToGui(slot, Bounds.renderX + Bounds.InnerWidth / 2, Bounds.renderY + Bounds.InnerHeight / 2,
                100, size, ColorUtil.WhiteArgb, true, false, false);

            if (Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            {
                tooltip.RenderItemstackTooltip(slot, api.Input.MouseX, api.Input.MouseY, deltaTime);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            tooltip.Dispose();
        }
    }
}
