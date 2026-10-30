using System;
using System.Collections.Generic;
using canjewelry.src.items.resource;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.be
{
    /// <summary>
    /// A small heap of loose gems, rough and cut mixed, one gem per slot: every gem keeps its own
    /// carats and traits, so none of them merge. Each is drawn with its own shape and stands on
    /// whatever is under it, measured from its own mesh, so a shape whose origin is off the gem
    /// does not make it float.
    ///
    /// Layers: 7 on the ground (a centre and a ring of 6), 4 on top of them, 1 on the very top.
    /// Ring gems lie against the middle one by their real radii; a gem above rests where it meets
    /// the gems under it, each taken as a dome of its own height and radius. Turn and tilt come
    /// from the position and the slot index, so a heap looks the same after a reload.
    /// </summary>
    public class CANBEGemPile : BlockEntityDisplay
    {
        public const int Capacity = 12;
        // How far a gem settles into the dome it rests on, as a share of that gem's height.
        private const float NestShare = 0.15f;
        // Ring distance as a share of the two radii: under one, as faceted gems are not round and
        // fit a little into each other's gaps.
        private const float ContactShare = 0.92f;
        // Used for a gem with no mesh to measure, in blocks.
        private const float FallbackRadius = 0.1f;

        // Direction of each place and its distance from the centre in radii (centre gem's plus its
        // own), with its layer, in filling order.
        private static readonly (float X, float Z, int Layer)[] Places = BuildPlaces();

        private readonly InventoryGeneric inv;

        public override InventoryBase Inventory => inv;
        public override string InventoryClassName => "cangempile";
        public override string AttributeTransformCode => "onGemPileTransform";

        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < inv.Count; i++) if (!inv[i].Empty) n++;
                return n;
            }
        }

        /// <summary>Height of the heap in blocks, for its selection and collision box.</summary>
        public float Height => Count <= 7 ? 0.14f : Count <= 11 ? 0.24f : 0.32f;

        public CANBEGemPile()
        {
            inv = new InventoryGeneric(Capacity, "cangempile-0", null, (_, inventory) => new ItemSlotGemPile(inventory));
        }

        public static bool IsGem(ItemStack stack)
            => stack?.Collectible is CANCutGemItem || stack?.Collectible is CANRoughGemItem;

        /// <summary>Puts one gem from the slot on top of the heap.</summary>
        public bool TryAdd(IPlayer byPlayer, ItemSlot from)
        {
            if (!IsGem(from?.Itemstack)) return false;

            for (int i = 0; i < inv.Count; i++)
            {
                if (!inv[i].Empty) continue;
                if (from.TryPutInto(Api.World, inv[i], 1) <= 0) return false;

                from.MarkDirty();
                MarkDirty(true);
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/loosestone1"), Pos.X + 0.5, Pos.Y, Pos.Z + 0.5, byPlayer, true, 12);
                return true;
            }
            return false;
        }

        /// <summary>Takes the top gem off; an empty heap goes away.</summary>
        public bool TryTake(IPlayer byPlayer)
        {
            for (int i = inv.Count - 1; i >= 0; i--)
            {
                if (inv[i].Empty) continue;

                ItemStack stack = inv[i].TakeOut(1);
                if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.3, 0.5));
                }
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/loosestone1"), Pos.X + 0.5, Pos.Y, Pos.Z + 0.5, byPlayer, true, 12);

                if (Count == 0) Api.World.BlockAccessor.SetBlock(0, Pos);
                else MarkDirty(true);
                return true;
            }
            return false;
        }

        public bool OnInteract(IPlayer byPlayer)
        {
            if (Api.Side != EnumAppSide.Server) return true;

            ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!hand.Empty)
            {
                return IsGem(hand.Itemstack) && TryAdd(byPlayer, hand);
            }
            return TryTake(byPlayer);
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] matrices = new float[Capacity][];
            if (Api is not ICoreClientAPI) return matrices;

            // Each gem measured from its own triangles: how far it reaches out from its upright axis
            // (the same whichever way it is turned) and how tall it is. Its box would leave the
            // empty corners of a round gem between neighbours.
            var shapes = new GemShape?[Capacity];
            for (int i = 0; i < Capacity; i++)
            {
                if (inv[i].Empty) continue;
                MeshData m = getMesh(inv[i]);
                if (m != null && TryMeasure(m, out GemShape s)) shapes[i] = s;
            }

            float centreRadius = shapes[0]?.Radius ?? FallbackRadius;
            var placed = new List<(float X, float Z, float Bottom, GemShape Shape)>();

            for (int i = 0; i < Capacity; i++)
            {
                if (inv[i].Empty) continue;

                var place = Places[i];
                GemShape shape = shapes[i] ?? new GemShape(0, 0, 0, FallbackRadius, FallbackRadius);

                // Out from the centre by the two gems' radii, so a ring gem touches the middle one.
                float reach = (centreRadius + shape.Radius) * ContactShare;
                float px = place.X * reach, pz = place.Z * reach;

                // On the ground, or on whatever lies under it: every gem below is a dome of its real
                // height and radius, and this one comes to rest where its bottom meets the highest.
                float floor = 0;
                if (place.Layer > 0)
                {
                    foreach (var below in placed)
                    {
                        float span = below.Shape.Radius + shape.Radius * 0.5f;
                        float d = (float)Math.Sqrt((px - below.X) * (px - below.X) + (pz - below.Z) * (pz - below.Z));
                        if (d >= span) continue;
                        float t = d / span;
                        float surface = below.Bottom + below.Shape.Height * (1 - t * t) - below.Shape.Height * NestShare;
                        floor = Math.Max(floor, surface);
                    }
                }
                placed.Add((px, pz, floor, shape));

                if (shapes[i] == null)
                {
                    matrices[i] = new Matrixf().Translate(0.5f + px, floor, 0.5f + pz).Values;
                    continue;
                }

                int seed = GameMath.MurmurHash3(Pos.X, Pos.Y * Capacity + i, Pos.Z);
                var rand = new Random(seed);
                float yaw = (float)(rand.NextDouble() * 360.0);
                float tilt = place.Layer == 0 ? 0 : (float)(rand.NextDouble() * 24.0 - 12.0);

                matrices[i] = new Matrixf()
                    .Translate(0.5f + px, floor, 0.5f + pz)
                    .RotateYDeg(yaw)
                    .RotateXDeg(tilt)
                    // The gem's own bottom centre to the origin, wherever its shape put it.
                    .Translate(-shape.CentreX, -shape.Bottom, -shape.CentreZ)
                    .Values;
            }
            return matrices;
        }

        /// <summary>A gem's real size: the centre of its upright axis, its bottom, height and reach.</summary>
        private readonly record struct GemShape(float CentreX, float CentreZ, float Bottom, float Height, float Radius);

        // The display builds its meshes once and keeps them until told otherwise; without this a
        // gem put on the heap was not drawn until the chunk was loaded again.
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            base.OnTesselation(mesher, tessThreadTesselator);
            return true; // the heap has no block mesh of its own, only the gems
        }

        private static (float, float, int)[] BuildPlaces()
        {
            var places = new (float, float, int)[Capacity];
            int n = 0;

            // Ground: a centre and a ring of six, one gem width out.
            places[n++] = (0, 0, 0);
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3;
                places[n++] = ((float)Math.Cos(a), (float)Math.Sin(a), 0);
            }

            // Second layer: four, half a width out, over the hollows between the ground ones.
            for (int k = 0; k < 4; k++)
            {
                double a = Math.PI / 4 + k * Math.PI / 2;
                places[n++] = ((float)(Math.Cos(a) * 0.55), (float)(Math.Sin(a) * 0.55), 1);
            }

            // Top.
            places[n] = (0, 0, 2);
            return places;
        }

        /// <summary>
        /// The gem's real size, over the triangles that have an area, so degenerate anchor elements
        /// do not count as part of it: the middle of its footprint, its bottom and height, and the
        /// farthest any of it reaches from the upright line through that middle.
        /// </summary>
        private static bool TryMeasure(MeshData mesh, out GemShape shape)
        {
            shape = default;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            var used = new List<int>();

            float[] xyz = mesh.xyz;
            int[] indices = mesh.Indices;
            for (int i = 0; i + 2 < mesh.IndicesCount; i += 3)
            {
                int a = indices[i] * 3, b = indices[i + 1] * 3, c = indices[i + 2] * 3;
                float abx = xyz[b] - xyz[a], aby = xyz[b + 1] - xyz[a + 1], abz = xyz[b + 2] - xyz[a + 2];
                float acx = xyz[c] - xyz[a], acy = xyz[c + 1] - xyz[a + 1], acz = xyz[c + 2] - xyz[a + 2];
                float nx = aby * acz - abz * acy, ny = abz * acx - abx * acz, nz = abx * acy - aby * acx;
                if (nx * nx + ny * ny + nz * nz < 1e-12f) continue;

                foreach (int v in new[] { a, b, c })
                {
                    minX = Math.Min(minX, xyz[v]); minY = Math.Min(minY, xyz[v + 1]); minZ = Math.Min(minZ, xyz[v + 2]);
                    maxX = Math.Max(maxX, xyz[v]); maxY = Math.Max(maxY, xyz[v + 1]); maxZ = Math.Max(maxZ, xyz[v + 2]);
                    used.Add(v);
                }
            }
            if (used.Count == 0) return false;

            float centreX = (minX + maxX) / 2, centreZ = (minZ + maxZ) / 2;
            float radiusSq = 0;
            foreach (int v in used)
            {
                float dx = xyz[v] - centreX, dz = xyz[v + 2] - centreZ;
                radiusSq = Math.Max(radiusSq, dx * dx + dz * dz);
            }

            shape = new GemShape(centreX, centreZ, minY, maxY - minY, (float)Math.Sqrt(radiusSq));
            return true;
        }
    }

    /// <summary>One loose gem, rough or cut.</summary>
    public class ItemSlotGemPile : ItemSlot
    {
        public override int MaxSlotStackSize => 1;

        public ItemSlotGemPile(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
            => CANBEGemPile.IsGem(sourceSlot?.Itemstack) && base.CanHold(sourceSlot);

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
            => CANBEGemPile.IsGem(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);
    }
}
