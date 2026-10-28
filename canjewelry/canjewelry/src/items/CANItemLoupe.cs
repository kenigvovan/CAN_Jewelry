using System.Collections.Generic;
using canjewelry.src.items.resource;
using canjewelry.src.render;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace canjewelry.src.items
{
    /// <summary>
    /// The jeweler's loupe. Does nothing on its own: held in the left hand, it lets a rough gem in
    /// the right hand be appraised (see CANGemAppraisalCB).
    /// </summary>
    public class CANItemLoupe : Item
    {
        /// <summary>Stack attribute with the metal of the wire the frame was made of.</summary>
        public const string FrameMetalKey = "frameMetal";
        private const string MeshRefsCacheKey = "canjewelry:loupeMeshRefs";

        public static bool IsHeldInLeftHand(EntityAgent byEntity)
            => byEntity?.LeftHandItemSlot?.Itemstack?.Collectible is CANItemLoupe;

        /// <summary>
        /// How far either side of the truth the weight this loupe shows may reach, 0 for the
        /// exact weight. Null when the loupe does not say: the config's LoupeRangeFraction then.
        /// </summary>
        public static float? WeighHalfWidth(EntityAgent byEntity)
        {
            var attrs = byEntity?.LeftHandItemSlot?.Itemstack?.Collectible?.Attributes;
            return attrs?["weighHalfWidth"].Exists == true ? attrs["weighHalfWidth"].AsFloat() : null;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
            foreach (ItemSlot slot in allInputSlots)
            {
                if (slot.Itemstack?.Collectible is CANItemWireHank wire && wire.Variant["metal"] != null)
                {
                    outputSlot.Itemstack?.Attributes.SetString(FrameMetalKey, wire.Variant["metal"]);
                    return;
                }
            }
        }

        private Dictionary<string, MultiTextureMeshRef> MeshRefs
            => ObjectCacheUtil.GetOrCreate(api, MeshRefsCacheKey, () => new Dictionary<string, MultiTextureMeshRef>());

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string metal = itemstack.Attributes.GetString(FrameMetalKey);
            if (metal != null)
            {
                string key = Code.ToShortString() + "-" + metal;
                if (!MeshRefs.TryGetValue(key, out MultiTextureMeshRef modelref) || modelref.Disposed)
                {
                    MeshData mesh = GenMesh(capi, metal);
                    if (mesh != null) modelref = MeshRefs[key] = capi.Render.UploadMultiTextureMesh(mesh);
                }
                if (modelref != null) renderinfo.ModelRef = modelref;
            }
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        // The frame takes the texture of the wire it was made of, so metals added by patches work too.
        private MeshData GenMesh(ICoreClientAPI capi, string metal)
        {
            Item wire = capi.World.GetItem(new AssetLocation("canjewelry:canwirehank-" + metal));
            AssetLocation metalTexture = wire?.Textures != null && wire.Textures.TryGetValue("metal", out CompositeTexture tex)
                ? tex.Baked?.BakedName
                : null;
            if (metalTexture == null) return null;

            Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
            if (shape == null) return null;

            var texSource = new CANTexSource(capi, capi.ItemTextureAtlas, Textures, "loupe " + Code);
            texSource.Overrides["metal"] = metalTexture;
            capi.Tesselator.TesselateShape("loupe", shape, out MeshData mesh, texSource, null, 0, 0, 0, null, null);
            return mesh;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (api is ICoreClientAPI)
            {
                var refs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, MeshRefsCacheKey);
                if (refs != null)
                {
                    foreach (MultiTextureMeshRef meshRef in refs.Values) meshRef?.Dispose();
                    refs.Clear();
                    ObjectCacheUtil.Delete(api, MeshRefsCacheKey);
                }
            }
            base.OnUnloaded(api);
        }
    }
}
