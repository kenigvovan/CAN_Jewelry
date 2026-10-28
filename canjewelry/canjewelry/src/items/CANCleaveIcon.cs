using System.Collections.Generic;
using canjewelry.src.render;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.items
{
    /// <summary>
    /// The icon of a cleave recipe in the cutting table's recipe list: the gem in pieces. The
    /// list gives it the type of the gem on the table, and it is drawn in that gem's colour.
    /// </summary>
    public class CANCleaveIcon : Item
    {
        public const string GemTypeKey = "gemtype";

        private readonly Dictionary<string, MultiTextureMeshRef> meshes = new Dictionary<string, MultiTextureMeshRef>();

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            string gemType = itemstack.Attributes.GetString(GemTypeKey);
            if (string.IsNullOrEmpty(gemType)) return;

            if (!meshes.TryGetValue(gemType, out MultiTextureMeshRef mesh) || mesh.Disposed)
            {
                // "Shape" alone is this item's CompositeShape field, hence the full type name.
                Vintagestory.API.Common.Shape shape = Shape?.Base == null ? null
                    : capi.Assets.TryGet(Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"))?.ToObject<Vintagestory.API.Common.Shape>();
                if (shape == null) return;
                var texSource = new CANTexSource(capi, capi.ItemTextureAtlas, Textures, "cleave icon " + Code);
                texSource.Overrides["gem"] = new AssetLocation("canjewelry:item/gem/" + gemType);
                capi.Tesselator.TesselateShape("canjewelry cleave icon", shape, out MeshData data, texSource);
                mesh = meshes[gemType] = capi.Render.UploadMultiTextureMesh(data);
            }
            renderinfo.ModelRef = mesh;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            foreach (MultiTextureMeshRef mesh in meshes.Values) mesh?.Dispose();
            meshes.Clear();
            base.OnUnloaded(api);
        }
    }
}
