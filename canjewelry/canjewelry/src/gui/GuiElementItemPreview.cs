using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.gui
{
    // Draws the rotatable 3D item preview inside a dialog. The rendering itself is done by
    // JewelerItemPreview, which renders the stack into its own framebuffer and hands out a
    // texture id - painting that texture is all this element does.
    //
    // The render pass is deliberately NOT started here: switching framebuffers in the middle of
    // the gui pass would disturb it. The owning dialog calls JewelerItemPreview.Render before
    // composing, and this element only shows the result of that.
    public class GuiElementItemPreview : GuiElement
    {
        // Far enough out to see a whole breastplate, far enough in to place a single gem.
        private const float MinZoom = 0.4f;
        private const float MaxZoom = 4f;
        private const float ZoomStep = 1.15f;

        private readonly JewelerItemPreview preview;
        private readonly bool zoomable;
        private bool dragging;
        private bool panning;
        private int lastMouseX;
        private int lastMouseY;

        public GuiElementItemPreview(ICoreClientAPI capi, ElementBounds bounds, JewelerItemPreview preview,
            bool zoomable = false)
            : base(capi, bounds)
        {
            this.preview = preview;
            this.zoomable = zoomable;
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (preview == null || preview.TextureId <= 0) return;

            api.Render.Render2DTexture(preview.TextureId,
                (float)Bounds.renderX, (float)Bounds.renderY,
                (float)Bounds.InnerWidth, (float)Bounds.InnerHeight);
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);

            // Middle click puts a piece that has been dragged or zoomed out of the frame back.
            if (zoomable && args.Button == EnumMouseButton.Middle)
            {
                preview?.ResetView();
                args.Handled = true;
                return;
            }

            dragging = true;
            // Right drag slides the piece around inside the frame, left drag turns it.
            panning = zoomable && args.Button == EnumMouseButton.Right;
            lastMouseX = args.X;
            lastMouseY = args.Y;
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseMove(api, args);
            if (!dragging || preview == null) return;

            int dx = args.X - lastMouseX;
            int dy = args.Y - lastMouseY;

            if (panning)
            {
                // The frame is painted at a different size than it is rendered at, so mouse pixels
                // are scaled into framebuffer pixels - otherwise the piece lags behind the cursor.
                float scale = Bounds.InnerWidth <= 0 ? 1f : (float)(preview.FboSize / Bounds.InnerWidth);
                preview.PanX += dx * scale;
                preview.PanY += dy * scale;
            }
            else
            {
                // Horizontal drag spins the piece, vertical tilts it.
                preview.RotationY -= dx * 0.5f;
                preview.RotationX -= dy * 0.5f;
            }

            lastMouseX = args.X;
            lastMouseY = args.Y;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseUp(api, args);
            dragging = false;
            panning = false;
        }

        // The composer offers the wheel to every element under the cursor first, so handling it
        // here keeps it from reaching whatever sits behind the dialog.
        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
        {
            if (!zoomable || preview == null) return;
            if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;

            args.SetHandled(true);

            // Multiplied rather than added: a step feels the same size at every zoom level.
            float zoom = preview.Zoom * (float)Math.Pow(ZoomStep, Math.Sign(args.deltaPrecise));
            preview.Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        }
    }
}
