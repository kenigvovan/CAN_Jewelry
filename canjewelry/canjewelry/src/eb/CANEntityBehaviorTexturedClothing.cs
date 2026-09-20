using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.eb
{
    public abstract class CANEntityBehaviorTexturedClothing: CANEntityBehaviorContainer
    {
        protected int skinTextureSubId;

        private ICoreClientAPI capi;

        private bool textureSpaceAllocated;

        public bool doReloadShapeAndSkin = true;
        TextureAtlasPosition myskinTexPos;

        protected TextureAtlasPosition skinTexPos
        {
            get
            {
                return myskinTexPos;
            }
            set
            {
                myskinTexPos = value;
            }
            /*get
            {
                return (entity.Properties.Client.Renderer as EntityShapeRenderer).skinTexPos;
            }
            set
            {
                (entity.Properties.Client.Renderer as EntityShapeRenderer).skinTexPos = value;
            }*/
        }

        public Size2i AtlasSize => capi.EntityTextureAtlas.Size;

        public event Action<LoadedTexture, TextureAtlasPosition, int> OnReloadSkin;

        public CANEntityBehaviorTexturedClothing(Entity entity)
            : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            capi = Api as ICoreClientAPI;
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
            reloadSkin();
        }

        public override ITexPositionSource GetTextureSource(ref EnumHandling handling)
        {
            if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
            {
                throw new InvalidOperationException("Potentially attempting to insert a texture into the atlas outside of the main thread (if this allocation causes a new atlas to be created).");
            }

            handling = EnumHandling.PassThrough;
            if (!textureSpaceAllocated)
            {
                TextureAtlasPosition textureAtlasPosition = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
                string text = entity.Properties.Attributes?["skinBaseTextureKey"].AsString();
                if (text != null)
                {
                    textureAtlasPosition = capi.EntityTextureAtlas.Positions[entity.Properties.Client.Textures[text].Baked.TextureSubId];
                }

                int width = (int)((textureAtlasPosition.x2 - textureAtlasPosition.x1) * (float)AtlasSize.Width);
                int height = (int)((textureAtlasPosition.y2 - textureAtlasPosition.y1) * (float)AtlasSize.Height);
                capi.EntityTextureAtlas.AllocateTextureSpace(width, height, out skinTextureSubId, out var texPos);
                skinTexPos = texPos;
                textureSpaceAllocated = true;
            }

            return null;
        }

        public void reloadSkin()
        {
            if (capi == null || !doReloadShapeAndSkin || skinTexPos == null)
            {
                return;
            }

            TextureAtlasPosition textureAtlasPosition = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
            string text = entity.Properties.Attributes?["skinBaseTextureKey"].AsString();
            if (text != null)
            {
                textureAtlasPosition = capi.EntityTextureAtlas.Positions[entity.Properties.Client.Textures[text].Baked.TextureSubId];
            }

            LoadedTexture loadedTexture = new LoadedTexture(null)
            {
                TextureId = textureAtlasPosition.atlasTextureId,
                Width = capi.EntityTextureAtlas.Size.Width,
                Height = capi.EntityTextureAtlas.Size.Height
            };
            capi.Render.GlToggleBlend(blend: false);
            capi.EntityTextureAtlas.RenderTextureIntoAtlas(skinTexPos.atlasTextureId, loadedTexture, (int)(textureAtlasPosition.x1 * (float)AtlasSize.Width), (int)(textureAtlasPosition.y1 * (float)AtlasSize.Height), (int)((textureAtlasPosition.x2 - textureAtlasPosition.x1) * (float)AtlasSize.Width), (int)((textureAtlasPosition.y2 - textureAtlasPosition.y1) * (float)AtlasSize.Height), skinTexPos.x1 * (float)capi.EntityTextureAtlas.Size.Width, skinTexPos.y1 * (float)capi.EntityTextureAtlas.Size.Height, -1f);
            capi.Render.GlToggleBlend(blend: true, EnumBlendMode.Overlay);
            this.OnReloadSkin?.Invoke(loadedTexture, skinTexPos, skinTextureSubId);
            // Null when the extra jewelry slots are switched off; the bare skin above still stands.
            InventoryBase inv = Inventory;
            if (inv == null) return;
            int[] array = new int[12]
            {
            3, 4, 2, 11, 9, 1, 7, 6, 0, 5,
            10, 8
            };
            foreach (int slotId in array)
            {
                ItemStack itemStack = inv[slotId]?.Itemstack;
                if (itemStack != null && !hideClothing && itemStack.Item.FirstTexture != null)
                {
                    int textureSubId = itemStack.Item.FirstTexture.Baked.TextureSubId;
                    TextureAtlasPosition textureAtlasPosition2 = capi.ItemTextureAtlas.Positions[textureSubId];
                    LoadedTexture fromTexture = new LoadedTexture(null)
                    {
                        TextureId = textureAtlasPosition2.atlasTextureId,
                        Width = capi.ItemTextureAtlas.Size.Width,
                        Height = capi.ItemTextureAtlas.Size.Height
                    };
                    capi.EntityTextureAtlas.RenderTextureIntoAtlas(skinTexPos.atlasTextureId, fromTexture, textureAtlasPosition2.x1 * (float)capi.ItemTextureAtlas.Size.Width, textureAtlasPosition2.y1 * (float)capi.ItemTextureAtlas.Size.Height, (textureAtlasPosition2.x2 - textureAtlasPosition2.x1) * (float)capi.ItemTextureAtlas.Size.Width, (textureAtlasPosition2.y2 - textureAtlasPosition2.y1) * (float)capi.ItemTextureAtlas.Size.Height, skinTexPos.x1 * (float)capi.EntityTextureAtlas.Size.Width, skinTexPos.y1 * (float)capi.EntityTextureAtlas.Size.Height);
                }
            }

            capi.Render.GlToggleBlend(blend: true);
            capi.Render.BindTexture2d(skinTexPos.atlasTextureId);
            capi.Render.GlGenerateTex2DMipmaps();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            capi?.EntityTextureAtlas.FreeTextureSpace(skinTextureSubId);
        }

        public override string PropertyName()
        {
            return "clothing2";
        }
    }
}
