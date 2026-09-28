using canjewelry.src.api;
using canjewelry.src.CB;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.render
{
    /// <summary>One gem sitting in a socket, as read off the stack.</summary>
    public struct CANSocketGem
    {
        public int SocketIndex;
        public string GemType;
        /// <summary>1 normal, 2 flawless, 3 exquisite — the cut gem sizes.</summary>
        public int Size;
        public string CuttingType;
    }

    /// <summary>
    /// Builds the mesh of an encrusted item: the item's own mesh with a gem mesh laid on top for
    /// every filled socket, placed by the poses of <see cref="CANGemVisualRegistry"/>.
    ///
    /// <para>The whole thing is rebuilt rather than merged into the existing model:
    /// <c>ItemRenderInfo.ModelRef</c> is a GPU resource, there is no way back from it to a
    /// <see cref="MeshData"/> that another mesh could be added to.</para>
    /// </summary>
    public static class CANGemMeshBuilder
    {
        /// <inheritdoc cref="CANGemDebug.BaseOnly"/>
        public static bool DebugBaseOnly
        {
            get => CANGemDebug.BaseOnly;
            set => CANGemDebug.BaseOnly = value;
        }

        [System.ThreadStatic]
        private static StringBuilder keyBuilder;

        // Called for every item on screen every frame, so nothing here allocates until a gem is
        // actually found.
        /// <summary>No gems to draw. Shared — read it, do not add to it.</summary>
        public static readonly List<CANSocketGem> NoGems = new List<CANSocketGem>();

        private static readonly string[] SocketNames = BuildSocketNames(16);

        private static string[] BuildSocketNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = "slot" + i;
            return names;
        }

        private static string SocketName(int index)
            => index < SocketNames.Length ? SocketNames[index] : "slot" + index;

        /// <summary>
        /// The gems currently in the item's sockets, empty sockets skipped. The empty result is
        /// shared — read it, do not add to it.
        /// </summary>
        public static List<CANSocketGem> CollectGems(ItemStack stack)
        {
            ITreeAttribute tree = stack?.Attributes?.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            if (tree == null) return NoGems;

            List<CANSocketGem> result = null;
            int maxSockets = EncrustableCB.GetMaxAmountSockets(stack);
            for (int i = 0; i < maxSockets; i++)
            {
                ITreeAttribute socket = tree.GetTreeAttribute(SocketName(i));
                if (socket == null) continue;

                string gemType = socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                if (string.IsNullOrEmpty(gemType)) continue;

                if (result == null) result = new List<CANSocketGem>();
                result.Add(new CANSocketGem
                {
                    SocketIndex = i,
                    GemType = gemType,
                    Size = socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE),
                    CuttingType = socket.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND)
                });
            }
            return result ?? NoGems;
        }

        /// <summary>
        /// Identifies a built mesh. Has to carry what the gems are, not just the item: swapping a
        /// gem changes the model, and a key of item code alone would keep handing out the old one.
        /// </summary>
        public static string CacheKey(ItemStack stack, string target, List<CANSocketGem> gems)
        {
            // Reused: this is on the per frame path too, and the key itself is the only string that
            // has to come out of it. Rendering is one thread, but a static buffer is the kind of
            // thing that quietly breaks if that ever stops being true, hence ThreadStatic.
            StringBuilder sb = keyBuilder;
            if (sb == null) sb = keyBuilder = new StringBuilder(128);
            sb.Clear();

            sb.Append(stack.Collectible.Code.ToShortString());
            sb.Append('|').Append(target);
            foreach (var gem in gems)
            {
                sb.Append('|').Append(gem.SocketIndex).Append(':').Append(gem.GemType)
                  .Append(':').Append(gem.Size).Append(':').Append(gem.CuttingType);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The item's own mesh, the one our gems are added to.
        ///
        /// <para>Armour and clothing carry the vanilla <see cref="CollectibleBehaviorWearableAttachment"/>,
        /// whose own OnBeforeRender swaps the model for a full body mesh. Ours is appended last and
        /// therefore overwrites that, so for those items the base has to be that same full body
        /// mesh — otherwise a breastplate in hand would collapse into a flat item shape. Its GenMesh
        /// called with the item atlas returns exactly what its OnBeforeRender builds, so we ask it
        /// rather than repeat the assembly here.</para>
        /// </summary>
        // The base mesh depends on the item alone, never on its gems, but used to be rebuilt on
        // every miss of the composite cache - for armour that means cloning the wearer's shape,
        // step parenting the gear into it and tesselating the lot. Vanilla caches the same thing in
        // wearableAttachmentMeshRefs; this is our equivalent, on the cpu side.
        private static readonly ConcurrentDictionary<string, MeshData> baseMeshes
            = new ConcurrentDictionary<string, MeshData>();

        private const int BaseMeshCeiling = 128;

        /// <summary>
        /// Drops the cached base meshes. Needed when the shapes or the atlas behind them change —
        /// not when a pose does, which is the whole point of keeping them apart from the composites.
        /// </summary>
        public static void ClearBaseMeshCache()
        {
            baseMeshes.Clear();
        }

        /// <inheritdoc cref="BuildBase"/>
        /// <summary>The cached base mesh, as a copy the caller is free to add gems to.</summary>
        public static MeshData CachedBase(ICoreClientAPI capi, ItemStack stack, ITextureAtlasAPI atlas, bool wearableFullBody)
        {
            string key = BaseCacheKey(stack, capi, atlas, wearableFullBody);
            if (key != null && baseMeshes.TryGetValue(key, out MeshData cached))
            {
                return cached?.Clone();
            }

            MeshData built = BuildBase(capi, stack, atlas, wearableFullBody);
            if (key == null || built == null) return built;

            // Plain meshes, no gpu resources: when there are too many, throwing the lot away and
            // letting the few that are actually on screen come back costs a rebuild each.
            if (baseMeshes.Count >= BaseMeshCeiling) baseMeshes.Clear();
            baseMeshes[key] = built;
            return built.Clone();
        }

        /// <summary>What identifies the base mesh of this item, or null when it cannot be named.</summary>
        private static string BaseCacheKey(ItemStack stack, ICoreClientAPI capi, ITextureAtlasAPI atlas, bool wearableFullBody)
        {
            if (stack?.Collectible?.Code == null) return null;

            // The atlas is part of the identity: uvs are atlas relative.
            string prefix = atlas == capi.ItemTextureAtlas ? "i|" : "b|";
            if (!wearableFullBody) prefix += "flat|";

            if (wearableFullBody)
            {
                // Wearables build their mesh from more than the item code (the gear is fitted to the
                // wearer's shape, its textures are chosen per stack), and whoever builds it already
                // has a name for the result - use theirs. Without this a golden ring and a silver
                // one would share a cached mesh, since their item code is the same.
                IContainedMeshSource meshSource = OwnMeshSource(stack);
                if (meshSource != null)
                {
                    string own = meshSource.GetMeshCacheKey(new DummySlot(stack));
                    if (own != null) return prefix + "wearable|" + own;
                }
            }

            return prefix + stack.Collectible.Code.ToShortString();
        }

        /// <summary>
        /// Whoever builds this item's own full mesh, or null when the plain item shape is all there
        /// is. Two of them: the game's wearable attachment behaviour, which fits armour onto the
        /// wearer's shape, and an item class that builds its own mesh — this mod's jewelry does,
        /// because its shape is textured per stack (the metal, the gem in each socket) and the
        /// item's JSON names none of those textures. Tesselating such an item the ordinary way
        /// leaves every texture code unresolved and the item comes out blank.
        /// </summary>
        private static IContainedMeshSource OwnMeshSource(ItemStack stack)
        {
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null) return null;

            // With inheritance: armour and clothing carry CollectibleBehaviorWearable, which derives
            // from the attachment behaviour. GetBehavior<T> matches the exact type only and would
            // miss every one of them - the item was then tesselated as if its entity shape were an
            // item shape, and the model came out in pieces.
            // Only when the item really attaches to an entity. The behaviour's OnBeforeRender leaves
            // the model alone otherwise, but its GenMesh has no such check and would hand back the
            // bare wearer's body shape - which is what made a gem turn armour into a heap.
            var wearable = collectible.GetCollectibleBehavior<CollectibleBehaviorWearableAttachment>(withInheritance: true);
            if (wearable is IContainedMeshSource wearableMeshSource
                && IAttachableToEntity.FromCollectible(collectible) != null)
            {
                return wearableMeshSource;
            }

            return collectible as IContainedMeshSource;
        }

        public static MeshData BuildBase(ICoreClientAPI capi, ItemStack stack)
            => BuildBase(capi, stack, capi.ItemTextureAtlas, wearableFullBody: true);

        /// <param name="atlas">The atlas the mesh is textured against — the item atlas when the item
        /// is drawn as an item, the block atlas when it sits inside a block (a tool rack, a shelf).</param>
        /// <param name="wearableFullBody">Whether armour is built as the fitted full body mesh. True
        /// on the item render path, where the vanilla wearable behaviour does the same and ours has
        /// to match it; false inside a holder, which shows the plain item shape.</param>
        public static MeshData BuildBase(ICoreClientAPI capi, ItemStack stack, ITextureAtlasAPI atlas, bool wearableFullBody)
        {
            if (wearableFullBody)
            {
                IContainedMeshSource meshSource = OwnMeshSource(stack);
                if (meshSource != null) return meshSource.GenMesh(new DummySlot(stack), atlas, null);
            }

            MeshData mesh;
            if (stack.Class == EnumItemClass.Block && stack.Block != null)
            {
                capi.Tesselator.TesselateBlock(stack.Block, out mesh);
                return mesh;
            }

            if (stack.Item == null) return null;

            ITexPositionSource holderSource = atlas == capi.ItemTextureAtlas
                ? null
                : new HolderTexSource(capi, atlas, stack);

            // An item with a shape of its own. The game's own TesselateItem is used while its shape
            // is in a state the game can work with; when it is not, the shape is read from the
            // assets here and tesselated the same way (see UsableShape).
            if (stack.Item.Shape != null && !stack.Item.Shape.VoxelizeTexture)
            {
                Shape shape = UsableShape(capi, stack.Item.Shape.Base);
                if (shape == null) return null;

                if (holderSource == null)
                {
                    capi.Tesselator.TesselateShape(stack.Item, shape, out mesh, ShapeRotation(stack.Item.Shape));
                }
                else
                {
                    capi.Tesselator.TesselateShape("canjewelry base " + stack.Item.Code, shape, out mesh,
                        holderSource, ShapeRotation(stack.Item.Shape));
                }
                return mesh;
            }

            if (!CanTesselate(stack.Item)) return null;

            if (holderSource == null)
            {
                capi.Tesselator.TesselateItem(stack.Item, out mesh);
                return mesh;
            }

            capi.Tesselator.TesselateItem(stack.Item, out mesh, holderSource);
            return mesh;
        }

        /// <summary>
        /// The shape of an item, in a state that can actually be tesselated, or null when there is
        /// no such thing.
        ///
        /// <para>Item shapes are freed once the startup tesselation is done (<c>UnloadableShape</c>),
        /// and reloading one is the game's job — but <c>ShapeTesselator</c> never checks whether the
        /// reload worked, while <c>UnloadableShape.Load</c> sets <c>Loaded = true</c> before it reads
        /// the file. A shape that failed to come back therefore stays "loaded" holding null elements,
        /// and the tesselator walks into a NullReferenceException on its first foreach — which, from
        /// inside <c>OnBeforeRender</c>, takes down the render of the whole slot. Reading the file
        /// ourselves in that case costs one asset load per item and keeps the gems.</para>
        /// </summary>
        private static Shape UsableShape(ICoreClientAPI capi, AssetLocation shapeBase)
        {
            if (shapeBase == null) return null;

            Shape cached = capi.TesselatorManager.GetCachedShape(shapeBase);
            if (cached?.Elements != null) return cached;

            IAsset asset = capi.Assets.TryGet(shapeBase.Clone()
                .WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
            Shape own = asset?.ToObject<Shape>();
            return own?.Elements == null ? null : own;
        }

        /// <summary>The rotation a composite shape asks for, null when it asks for none.</summary>
        private static Vec3f ShapeRotation(CompositeShape shape)
        {
            if (shape.rotateX == 0f && shape.rotateY == 0f && shape.rotateZ == 0f) return null;

            return new Vec3f(shape.rotateX, shape.rotateY, shape.rotateZ);
        }

        /// <summary>
        /// Whether an item without a usable shape can still be drawn. The game voxelises its texture
        /// instead, and that path reads <c>texture.Baked.TextureSubId</c> unguarded — an item whose
        /// texture was never baked throws there rather than coming back as the unknown-item model,
        /// and an exception out of <c>OnBeforeRender</c> takes down the render of the whole slot.
        /// </summary>
        private static bool CanTesselate(Item item)
        {
            CompositeTexture texture = item.FirstTexture;
            if (item.Shape?.Base != null && item.Textures != null
                && item.Textures.TryGetValue(item.Shape.Base.ToShortString(), out CompositeTexture byShape))
            {
                texture = byShape;
            }

            // No texture at all is handled by the game itself - it hands back the unknown item model.
            return texture == null || texture.Baked != null;
        }

        /// <summary>
        /// The mesh of one cut gem, textured by gem type. Mirrors how the cut gem item builds
        /// itself (CANCutGemItem.GenMesh) — same shapes, same texture lookup.
        /// Null when the shape or the texture cannot be resolved.
        /// </summary>
        public static MeshData BuildGem(ICoreClientAPI capi, string gemType, int size, string cuttingType)
            => BuildGem(capi, gemType, size, cuttingType, capi.ItemTextureAtlas);

        /// <inheritdoc cref="BuildGem(ICoreClientAPI, string, int, string)"/>
        public static MeshData BuildGem(ICoreClientAPI capi, string gemType, int size, string cuttingType, ITextureAtlasAPI atlas)
        {
            IAsset shapeAsset = CANGemAssets.ShapeAsset(capi, size, cuttingType);
            if (shapeAsset == null) return null;

            AssetLocation texture = CANGemAssets.AtlasTexture(capi, gemType);
            if (texture == null) return null;

            capi.Tesselator.TesselateShape("canjewelry gem", shapeAsset.ToObject<Shape>(), out MeshData gemMesh,
                new GemTexSource(capi, atlas, texture), null, 0, 0, 0, null, null);
            return gemMesh;
        }

        /// <summary>
        /// Item mesh plus one gem per filled socket. Returns null when there is nothing to add or
        /// the base mesh cannot be built — the caller then leaves the vanilla model alone.
        /// </summary>
        public static MeshData BuildComposite(ICoreClientAPI capi, ItemStack stack, string target, List<CANSocketGem> gems)
            => BuildComposite(capi, stack, target, gems, capi.ItemTextureAtlas, wearableFullBody: true);

        /// <inheritdoc cref="BuildComposite(ICoreClientAPI, ItemStack, string, List{CANSocketGem})"/>
        /// <param name="atlas">See <see cref="BuildBase(ICoreClientAPI, ItemStack, ITextureAtlasAPI, bool)"/>.</param>
        /// <param name="wearableFullBody">See <see cref="BuildBase(ICoreClientAPI, ItemStack, ITextureAtlasAPI, bool)"/>.</param>
        public static MeshData BuildComposite(ICoreClientAPI capi, ItemStack stack, string target,
            List<CANSocketGem> gems, ITextureAtlasAPI atlas, bool wearableFullBody)
        {
            if (gems == null || gems.Count == 0) return null;

            MeshData mesh = CachedBase(capi, stack, atlas, wearableFullBody);
            if (mesh == null) return null;

            EnsureTextureIndices(mesh);
            if (DebugBaseOnly) return mesh;

            foreach (var gem in gems)
            {
                // One gem, drawn on both faces of the model. An item shape is flat: a single gem
                // laid on the near face is seen by whoever the face happens to point at - the
                // holder in first person, or everyone else, never both.
                for (int side = 0; side < CANGemVisualSide.Count; side++)
                {
                    ModelTransform pose = CANGemVisualRegistry.ResolvePose(stack, gem.SocketIndex, target, side);
                    if (pose == null || IsDegenerate(pose)) continue;

                    MeshData gemMesh = BuildGem(capi, gem.GemType, gem.Size, gem.CuttingType, atlas);
                    if (gemMesh == null) continue;

                    // AsMatrix already folds in the origin, so one transform covers translate,
                    // rotate and scale in the order the JSON declares them.
                    gemMesh.MatrixTransform(pose.AsMatrix);
                    GiveJointIds(mesh, gemMesh);
                    AlignOptionalBuffers(mesh, gemMesh);
                    mesh.AddMeshData(gemMesh);
                }
            }
            return mesh;
        }

        /// <summary>
        /// A pose scaled to nothing on any axis, which would draw nothing and crashes on the way
        /// there: <c>MeshData.MatrixTransform</c> runs every face normal through
        /// <c>BlockFacing.FromVector</c>, and a collapsed axis turns the normal along it into a zero
        /// length vector - FromVector then divides by that length, compares NaN angles, matches no
        /// face and returns null, which the caller dereferences.
        ///
        /// <para>A ModelTransform is T·R·S with R orthogonal, so its matrix is singular exactly when
        /// one of the scales is zero: checking the three scales is the whole test, not a guess at
        /// one. Non-zero but tiny scales are safe - the normal keeps a non-zero length.</para>
        /// </summary>
        public static bool IsDegenerate(ModelTransform pose)
        {
            const float epsilon = 0.0001f;
            return System.Math.Abs(pose.ScaleXYZ.X) < epsilon
                   || System.Math.Abs(pose.ScaleXYZ.Y) < epsilon
                   || System.Math.Abs(pose.ScaleXYZ.Z) < epsilon;
        }

        /// <summary>
        /// Gives every face of the mesh a texture index.
        ///
        /// <para>A mesh built for one texture does not bother filling them in, and that is fine
        /// until a gem brings a second texture along: <c>UploadMultiTextureMesh</c> then splits the
        /// mesh by <c>SplitByTextureId</c>, which reads <c>TextureIndices</c> by face number. With
        /// only the gem's own entries in there, the indices line up against the first faces of the
        /// item instead, and most of the model is sorted into a group that is never drawn — armour
        /// shows up as a single piece of itself. Filling them in first keeps face and index
        /// together.</para>
        /// </summary>
        private static void EnsureTextureIndices(MeshData mesh)
        {
            int faces = mesh.VerticesCount / MeshData.StandardVerticesPerFace;
            if (mesh.TextureIndicesCount >= faces) return;

            int textureId = mesh.TextureIds != null && mesh.TextureIds.Length > 0 ? mesh.TextureIds[0] : 0;
            while (mesh.TextureIndicesCount < faces) mesh.AddTextureId(textureId);
        }

        /// <summary>
        /// Drops the buffers of the gem mesh that the item mesh does not carry.
        ///
        /// <para>AddMeshData copies these buffer by buffer without checking that both sides have
        /// them: a gem carrying render passes or colour maps that the item has no array for either
        /// throws or leaves the result one buffer short of its vertices. The gem loses nothing that
        /// matters — an item mesh is drawn in one pass and takes no climate tint.</para>
        /// </summary>
        private static void AlignOptionalBuffers(MeshData baseMesh, MeshData gemMesh)
        {
            if (baseMesh.RenderPassesAndExtraBits == null)
            {
                gemMesh.RenderPassesAndExtraBits = null;
                gemMesh.RenderPassCount = 0;
            }

            if (baseMesh.ClimateColorMapIds == null || baseMesh.SeasonColorMapIds == null)
            {
                gemMesh.ClimateColorMapIds = null;
                gemMesh.SeasonColorMapIds = null;
                gemMesh.ColorMapIdsCount = 0;
            }

            if (baseMesh.XyzFaces == null)
            {
                gemMesh.XyzFaces = null;
                gemMesh.XyzFacesCount = 0;
            }
        }

        /// <summary>
        /// Gives the gem mesh a joint id per vertex when the mesh it is being added to has them.
        ///
        /// <para>Armour and clothing are tesselated as an entity (TesselateShapeWithJointIds), which
        /// puts one joint id per vertex into <c>CustomInts</c>. A gem shape is tesselated as an item
        /// and has none, and AddMeshData does not invent them: the joint buffer then stays shorter
        /// than the vertex count and the whole model is drawn with the vertex data read at the wrong
        /// offsets — a breastplate falls apart the moment a gem is set into it. The gem gets the
        /// joint of the nearest vertex of the item, which is also the joint it has to follow once
        /// the model is animated.</para>
        /// </summary>
        private static void GiveJointIds(MeshData baseMesh, MeshData gemMesh)
        {
            if (baseMesh?.CustomInts == null || gemMesh == null || gemMesh.VerticesCount == 0) return;

            var ints = new CustomMeshDataPartInt(gemMesh.VerticesCount)
            {
                InterleaveSizes = new int[] { 1 },
                InterleaveOffsets = new int[1],
                InterleaveStride = 0
            };
            int joint = NearestJointId(baseMesh, gemMesh);
            for (int i = 0; i < gemMesh.VerticesCount; i++) ints.Add(joint);
            gemMesh.CustomInts = ints;
        }

        /// <summary>The joint of the vertex of <paramref name="baseMesh"/> closest to the gem.</summary>
        private static int NearestJointId(MeshData baseMesh, MeshData gemMesh)
        {
            float cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < gemMesh.VerticesCount; i++)
            {
                cx += gemMesh.xyz[i * 3];
                cy += gemMesh.xyz[i * 3 + 1];
                cz += gemMesh.xyz[i * 3 + 2];
            }
            cx /= gemMesh.VerticesCount;
            cy /= gemMesh.VerticesCount;
            cz /= gemMesh.VerticesCount;

            int[] joints = baseMesh.CustomInts.Values;
            int best = 0;
            float bestDist = float.MaxValue;
            int count = System.Math.Min(baseMesh.VerticesCount, joints.Length);
            for (int i = 0; i < count; i++)
            {
                float dx = baseMesh.xyz[i * 3] - cx;
                float dy = baseMesh.xyz[i * 3 + 1] - cy;
                float dz = baseMesh.xyz[i * 3 + 2] - cz;
                float dist = dx * dx + dy * dy + dz * dz;
                if (dist >= bestDist) continue;

                bestDist = dist;
                best = joints[i];
            }
            return best;
        }

        /// <summary>
        /// Feeds the single "gem" texture code of the gem shapes. The cut gem item has the same
        /// thing built into the item class; a gem drawn on someone else's item needs it standalone.
        /// </summary>
        private class GemTexSource : ITexPositionSource
        {
            private readonly ICoreClientAPI capi;
            private readonly ITextureAtlasAPI atlas;
            private readonly AssetLocation texture;

            public GemTexSource(ICoreClientAPI capi, ITextureAtlasAPI atlas, AssetLocation texture)
            {
                this.capi = capi;
                this.atlas = atlas;
                this.texture = texture;
            }

            public Size2i AtlasSize => atlas.Size;

            public TextureAtlasPosition this[string textureCode] => TexPos(capi, atlas, texture);
        }

        /// <summary>
        /// Resolves an item's texture codes against an atlas that is not the item atlas — the block
        /// atlas of a holder. Same lookup order as <c>BlockEntityDisplay</c>: the collectible's own
        /// textures, its "all", the shape's table, then the code read as a path.
        /// </summary>
        private class HolderTexSource : ITexPositionSource
        {
            private readonly ICoreClientAPI capi;
            private readonly ITextureAtlasAPI atlas;
            private readonly IDictionary<string, CompositeTexture> textures;
            private readonly Shape shape;

            public HolderTexSource(ICoreClientAPI capi, ITextureAtlasAPI atlas, ItemStack stack)
            {
                this.capi = capi;
                this.atlas = atlas;
                textures = stack.Class == EnumItemClass.Item ? stack.Item?.Textures : stack.Block?.Textures;

                CompositeShape itemShape = stack.Item?.Shape;
                if (itemShape?.Base != null) shape = capi.TesselatorManager.GetCachedShape(itemShape.Base);
            }

            public Size2i AtlasSize => atlas.Size;

            public TextureAtlasPosition this[string textureCode]
            {
                get
                {
                    AssetLocation path = null;
                    if (textures != null)
                    {
                        if (textures.TryGetValue(textureCode, out CompositeTexture tex)
                            || textures.TryGetValue("all", out tex))
                        {
                            path = tex.Baked?.BakedName;
                        }
                    }
                    if (path == null) shape?.Textures?.TryGetValue(textureCode, out path);
                    if (path == null) path = new AssetLocation(textureCode);

                    return TexPos(capi, atlas, path) ?? atlas.UnknownTexturePosition;
                }
            }
        }

        /// <summary>
        /// The place of a texture in an atlas, inserting it there when it is not in yet. Null when
        /// there is no such texture at all.
        /// </summary>
        private static TextureAtlasPosition TexPos(ICoreClientAPI capi, ITextureAtlasAPI atlas, AssetLocation texture)
        {
            TextureAtlasPosition texpos = atlas[texture];
            if (texpos != null) return texpos;

            IAsset texAsset = capi.Assets.TryGet(
                texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"), true);
            if (texAsset == null)
            {
                capi.World.Logger.Warning("[canjewelry] texture {0} not found", texture);
                return null;
            }
            atlas.GetOrInsertTexture(texture, out _, out texpos, () => texAsset.ToBitmap(capi), 0f);
            return texpos;
        }
    }
}
