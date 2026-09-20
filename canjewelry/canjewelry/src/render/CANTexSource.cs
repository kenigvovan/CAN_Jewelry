using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace canjewelry.src.render
{
    /// <summary>
    /// Hands the tesselator an atlas position per texture code, with room for codes the caller wants
    /// pointed somewhere else — the stone and the metal of a gem cutting table, the wood of a wire
    /// bench, the colour of a gem.
    ///
    /// <para>Written once because it was written nine times: every block entity, block and item of
    /// this mod that builds a mesh carried its own copy of the same twenty lines, down to the
    /// "not no such texture found" in the warning. Two of those copies were identical but for the
    /// class name.</para>
    ///
    /// <para>An instance per mesh build, never a field on a <see cref="Block"/> or an
    /// <see cref="Item"/>: those are one object for the whole world, while meshes are built from the
    /// main thread and from the chunk tesselation thread at the same time. The copies that lived on
    /// the collectible mutated a shared dictionary mid-build, which is how an item ends up wearing
    /// another one's texture.</para>
    /// </summary>
    public class CANTexSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;
        private readonly ITextureAtlasAPI atlas;
        private readonly IDictionary<string, CompositeTexture> fallback;
        private readonly string forLogging;

        /// <summary>
        /// Texture codes pointed at an asset of the caller's choosing. Looked at before the fallback
        /// textures, which is what makes one shape serve every stone or metal variant.
        /// </summary>
        public readonly Dictionary<string, AssetLocation> Overrides = new Dictionary<string, AssetLocation>();

        /// <param name="atlas">Where the textures are inserted — the block atlas for anything drawn
        /// in the world, the item atlas for anything drawn as an item.</param>
        /// <param name="fallback">The textures of the block or item being drawn, consulted for codes
        /// with no override. Null is allowed: then only overrides and "all" answer.</param>
        /// <param name="forLogging">Named in the warning when a texture cannot be found.</param>
        public CANTexSource(ICoreClientAPI capi, ITextureAtlasAPI atlas,
            IDictionary<string, CompositeTexture> fallback, string forLogging)
        {
            this.capi = capi;
            this.atlas = atlas;
            this.fallback = fallback;
            this.forLogging = forLogging;
        }

        public Size2i AtlasSize => atlas.Size;

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (Overrides.TryGetValue(textureCode, out AssetLocation overridden))
                {
                    return GetOrInsert(overridden);
                }

                AssetLocation texturePath = null;
                if (fallback != null)
                {
                    // "all" is the shape author's way of texturing everything with one entry, and
                    // every copy of this code honoured it.
                    if (fallback.TryGetValue(textureCode, out CompositeTexture texture)
                        || fallback.TryGetValue("all", out texture))
                    {
                        texturePath = texture?.Baked?.BakedName;
                    }
                }

                return GetOrInsert(texturePath);
            }
        }

        /// <summary>
        /// The atlas position of a texture, inserting it first when the atlas has not seen it — a
        /// texture named by an override is usually not part of any block or item and is therefore
        /// not in the atlas already.
        /// </summary>
        private TextureAtlasPosition GetOrInsert(AssetLocation texturePath)
        {
            if (texturePath == null) return null;

            TextureAtlasPosition texPos = atlas[texturePath];
            if (texPos != null) return texPos;

            IAsset asset = capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset == null)
            {
                capi.World.Logger.Warning("[canjewelry] {0}: texture {1} is not there, drawing without it",
                    forLogging, texturePath);
                return null;
            }

            atlas.GetOrInsertTexture(texturePath, out int _, out texPos, () => asset.ToBitmap(capi));
            return texPos;
        }
    }
}
