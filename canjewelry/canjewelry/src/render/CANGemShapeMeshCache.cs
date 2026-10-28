using System.Collections.Generic;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.render
{
    /// <summary>
    /// Meshes of rough gems with their own shape, built the first time one is drawn and dropped
    /// when it has gone longest unseen. A world can hold any number of such gems; only the ones
    /// on screen lately take memory.
    /// </summary>
    public static class CANGemShapeMeshCache
    {
        private const int Capacity = 256;

        private static readonly Dictionary<string, LinkedListNode<(string key, MultiTextureMeshRef mesh)>> byKey
            = new Dictionary<string, LinkedListNode<(string, MultiTextureMeshRef)>>();
        // Most recently used first.
        private static readonly LinkedList<(string key, MultiTextureMeshRef mesh)> order = new LinkedList<(string, MultiTextureMeshRef)>();

        /// <summary>The mesh of a gem with its own shape; null when it has none or the shape cannot be built.</summary>
        public static MultiTextureMeshRef Get(ICoreClientAPI capi, ItemStack stack)
        {
            if (!(stack?.Item is Item item) || !CANGemRoughShape.HasSeed(stack)) return null;
            string key = item.Code + "|" + CANGemRoughShape.SeedOf(stack);

            if (byKey.TryGetValue(key, out var node) && !node.Value.mesh.Disposed)
            {
                order.Remove(node);
                order.AddFirst(node);
                return node.Value.mesh;
            }
            if (node != null) Remove(node);

            Shape shape = CANGemRoughShape.ShapeFor(capi, stack);
            if (shape == null) return null;
            shape.ResolveReferences(capi.Logger, "canjewelry rough gem " + key);
            var texSource = new CANTexSource(capi, capi.ItemTextureAtlas, item.Textures, "rough gem " + item.Code);
            capi.Tesselator.TesselateShape("canjewelry rough gem", shape, out MeshData mesh, texSource);
            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(mesh);

            var added = order.AddFirst((key, meshRef));
            byKey[key] = added;
            while (order.Count > Capacity) Remove(order.Last);
            return meshRef;
        }

        private static void Remove(LinkedListNode<(string key, MultiTextureMeshRef mesh)> node)
        {
            order.Remove(node);
            byKey.Remove(node.Value.key);
            node.Value.mesh?.Dispose();
        }

        public static void Clear()
        {
            foreach (var entry in order) entry.mesh?.Dispose();
            order.Clear();
            byKey.Clear();
        }
    }
}
