using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.render
{
    /// <summary>
    /// Where the shape and the texture of a cut gem live. A gem is drawn two ways — as a mesh laid
    /// on an item and as a shape element hung off a bone — and both have to reach for the same
    /// files, so the lookups sit here rather than once in each.
    /// </summary>
    public static class CANGemAssets
    {
        /// <summary>The name the shapes are filed under: 1 normal, 2 flawless, 3 exquisite.</summary>
        public static string QualityOf(int size)
        {
            return size >= 3 ? "exquisite" : (size == 2 ? "flawless" : "normal");
        }

        /// <summary>The shape of one cut gem, null with a warning when it is not there.</summary>
        public static IAsset ShapeAsset(ICoreClientAPI capi, int size, string cuttingType)
        {
            string quality = QualityOf(size);
            string cut = string.IsNullOrEmpty(cuttingType) ? CANJWConstants.CUTTING_ROUND : cuttingType;

            IAsset asset = capi.Assets.TryGet("canjewelry:shapes/item/gem/cut/" + quality + "/gem_" + cut + ".json");
            if (asset == null)
            {
                capi.Logger.Warning("[canjewelry] no gem shape for quality {0} cut {1}", quality, cut);
            }
            return asset;
        }

        /// <summary>
        /// The stored texture path of a gem type, falling back to the default gem. Carries the
        /// "textures/" prefix an asset lookup wants — see <see cref="ShapeTexture"/> for the other
        /// spelling.
        /// </summary>
        public static string StoredTexturePath(string gemType)
        {
            if (canjewelry.gems_textures == null) return null;

            if (!canjewelry.gems_textures.TryGetValue(gemType, out string path))
            {
                canjewelry.gems_textures.TryGetValue(CANJWConstants.FALLBACK_GEM_TYPE, out path);
            }
            return path;
        }

        /// <summary>
        /// The texture as the atlas knows it, for tesselating a gem mesh. Null with a warning when
        /// the gem type has no texture.
        /// </summary>
        public static AssetLocation AtlasTexture(ICoreClientAPI capi, string gemType)
        {
            string stored = StoredTexturePath(gemType);
            AssetLocation texture = stored == null ? null : capi.Assets.TryGet(stored + ".png")?.Location;
            if (texture == null)
            {
                capi.Logger.Warning("[canjewelry] no texture for gem type {0}", gemType);
            }
            return texture;
        }

        /// <summary>
        /// The texture as a shape's texture definition wants it: without the "textures/" prefix,
        /// which belongs to an asset lookup and not to a shape. Null when there is no texture.
        /// </summary>
        public static AssetLocation ShapeTexture(ICoreClientAPI capi, string gemType)
        {
            string stored = StoredTexturePath(gemType);
            if (stored == null)
            {
                capi.Logger.Warning("[canjewelry] no texture for gem type {0}", gemType);
                return null;
            }

            var location = new AssetLocation(stored);
            if (location.Path.StartsWith("textures/", System.StringComparison.Ordinal))
            {
                location.Path = location.Path.Substring("textures/".Length);
            }
            return location;
        }
    }
}
