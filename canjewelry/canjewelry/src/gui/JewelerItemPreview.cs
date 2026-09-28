using System;
using System.Text;
using canjewelry.src.api;
using canjewelry.src.render;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace canjewelry.src.gui
{
    public class JewelerItemPreview : IDisposable
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientMain _game;
        private FrameBufferRef _fbo;
        private readonly InventoryItemRenderer _itemRenderer;
        private readonly DummySlot _dummySlot = new();

        public const int DefaultFboSize = 300;

        /// <summary>
        /// Side of the framebuffer the item is rendered into. A dialog that paints the preview
        /// larger than this asks for a bigger one, otherwise the texture is stretched and blurry.
        /// </summary>
        private readonly int _fboSize;

        // A render is a full framebuffer clear plus an item draw, and the owning dialog calls it
        // every frame. Redrawn only when the view or the item actually changed.
        private bool _dirty = true;
        private ItemStack _renderedStack;
        private string _renderedGems;

        /// <summary>
        /// Redraw the preview on the next frame. Callers say so when what the item looks like
        /// changed without the item itself changing — a pose edited in the debug menu, say.
        /// </summary>
        public void MarkDirty() => _dirty = true;

        private float _rotationY;
        private float _rotationX;
        private float _zoom = 1f;
        private float _panX;
        private float _panY;

        private void Set(ref float field, float value)
        {
            if (field == value) return;

            field = value;
            _dirty = true;
        }

        public float RotationY { get => _rotationY; set => Set(ref _rotationY, value); }
        public float RotationX { get => _rotationX; set => Set(ref _rotationX, value); }

        /// <summary>How much of the framebuffer the item fills. 1 is the default framing.</summary>
        public float Zoom { get => _zoom; set => Set(ref _zoom, value); }

        /// <summary>Where the item sits in the frame, in framebuffer pixels off its centre.</summary>
        public float PanX { get => _panX; set => Set(ref _panX, value); }
        public float PanY { get => _panY; set => Set(ref _panY, value); }

        /// <summary>Side of the framebuffer, so a caller can translate mouse pixels into pan.</summary>
        public int FboSize => _fboSize;

        /// <summary>Back to the default framing, for when the item has been dragged out of sight.</summary>
        public void ResetView()
        {
            Zoom = 1f;
            PanX = 0;
            PanY = 0;
            RotationX = 0;
            RotationY = 0;
        }

        public int TextureId => _fbo?.ColorTextureIds[0] ?? -1;

        public JewelerItemPreview(ICoreClientAPI capi, int fboSize = DefaultFboSize)
        {
            _capi = capi;
            _fboSize = fboSize;
            _game = (ClientMain)capi.World;
            _itemRenderer = new InventoryItemRenderer(_game);
            CreateFbo();
        }

        private void CreateFbo()
        {
            int size = _fboSize;
            var attrs = new FramebufferAttrs("canjewelry-jeweler-preview", size, size);
            attrs.Attachments = new FramebufferAttrsAttachment[]
            {
                new()
                {
                    AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                    Texture = new()
                    {
                        Width = size, Height = size,
                        PixelFormat = EnumTexturePixelFormat.Rgba,
                        PixelInternalFormat = EnumTextureInternalFormat.Rgba16f
                    }
                },
                new()
                {
                    AttachmentType = EnumFramebufferAttachment.DepthAttachment,
                    Texture = new()
                    {
                        Width = size, Height = size,
                        PixelFormat = EnumTexturePixelFormat.DepthComponent,
                        PixelInternalFormat = EnumTextureInternalFormat.DepthComponent32
                    }
                }
            };
            _fbo = _game.Platform.CreateFramebuffer(attrs);
        }

        public void Render(ItemStack stack)
        {
            if (_fbo == null || stack?.Collectible == null) return;

            // The gems are read off the stack rather than compared by reference alone: a dialog that
            // fills the sockets of the very same stack changes the picture without changing the item.
            string gems = CANGemMeshBuilder.CacheKey(stack, CANGemVisualTarget.Gui,
                CANGemMeshBuilder.CollectGems(stack));
            if (!ReferenceEquals(stack, _renderedStack) || gems != _renderedGems) _dirty = true;
            if (!_dirty) return;

            _dirty = false;
            _renderedStack = stack;
            _renderedGems = gems;

            var transform = stack.Collectible.GuiTransform;
            float prevRotY = transform.Rotation.Y;
            float prevRotX = transform.Rotation.X;
            transform.Rotation.Y = prevRotY + RotationY;
            transform.Rotation.X = prevRotX + RotationX;

            try
            {
                _game.Platform.GlEnableDepthTest();
                _game.Platform.GlDisableCullFace();
                _game.Platform.GlToggleBlend(true);
                _game.Platform.ClearFrameBuffer(_fbo, new float[] { 0, 0, 0, 0 },
                    clearDepthBuffer: true, clearColorBuffers: true);

                GL.Viewport(0, 0, _fboSize, _fboSize);
                _game.OrthoMode(_fboSize, _fboSize, true);

                _dummySlot.Itemstack = stack;
                // The item is drawn from its centre, so the size is what its longest side gets.
                // Bulky pieces (armor, coronets) reach past that box once rotated, which clipped
                // their edges against the framebuffer - hence the margin rather than 0.75.
                _itemRenderer.RenderItemstackToGui(_dummySlot,
                    _fboSize / 2.0 + PanX, _fboSize / 2.0 + PanY, 100,
                    _fboSize * 0.55f * Zoom, -1,
                    showStackSize: false);

                _game.PerspectiveMode();
                _game.Platform.LoadFrameBuffer(EnumFrameBuffer.Default);
            }
            catch (Exception e)
            {
                _capi.Logger.Error("[CANJewelerPreview] Render crashed: {0}", e);
                try { _game.PerspectiveMode(); } catch { }
                try { _game.Platform.LoadFrameBuffer(EnumFrameBuffer.Default); } catch { }
            }
            finally
            {
                transform.Rotation.Y = prevRotY;
                transform.Rotation.X = prevRotX;
            }
        }

        public void Dispose()
        {
            _game.Platform.DisposeFrameBuffer(_fbo);
            _itemRenderer.Dispose();
        }
    }
}
