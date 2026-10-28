using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    /// <summary>
    /// A crack or an inclusion at a place inside a rough gem. The place is in the gem's own
    /// box, -1..1 on each axis, so it fits whatever size the gem's shape has.
    /// </summary>
    public class GemDefect
    {
        public string Trait;
        public float[] Pos;
        // Facing of a crack's plane; null for an inclusion, which looks the same from every side.
        public float[] Normal;
    }

    /// <summary>
    /// How the gem is shown in the inspection preview: the item's gui transform turned by the
    /// player, drawn at a point of the framebuffer at a size. Enough to work out, on either side,
    /// where a defect lands on screen.
    /// </summary>
    public class GemView
    {
        public float[] Origin = { 0.5f, 0f, 0.5f };
        public float[] Translation = new float[3];
        public float[] Scale = { 1f, 1f, 1f };
        public float[] Rotation = new float[3];
        public float PosX, PosY, PosZ, Size;
        // Gui scale of the client; only the transform's translation is scaled with it.
        public float GuiScale = 1f;
        // The gem's box in model units, which a defect's -1..1 place is stretched over.
        public float[] BoundsMin = { 0f, 0f, 0f };
        public float[] BoundsMax = { 1f, 1f, 1f };
        // Mirrors the screen y, for a framebuffer drawn the other way up.
        public bool FlipY;
        // Which way screen z faces the viewer: 1 or -1 hides defects on the far side of the gem,
        // 0 shows them from every side.
        public float FrontZ;
    }

    /// <summary>
    /// The defects of a rough gem for the inspection: placing them, where they are on screen,
    /// how well they show and whether a click finds one. No client code, so the server checks
    /// a click with the very math the client drew with.
    /// </summary>
    public static class CANGemDefects
    {
        public const string TraitsKey = "gemdefecttraits";
        public const string PosKey = "gemdefectpos";
        public const string NormalKey = "gemdefectnormal";

        public static bool IsLocal(GemCaratsConfig cfg, string trait)
            => cfg.Traits.TryGetValue(trait, out var t) && t?.Local == true;

        /// <summary>
        /// One defect per local trait, on the gem's surface: the ellipsoid of radius Spread inside
        /// its -1..1 box. A crack runs along the surface, so its plane holds the outward direction.
        /// </summary>
        /// <param name="surfaceAt">Where the gem's surface lies in a direction from its middle, as a
        /// place in its -1..1 box (see <see cref="SurfaceFinder"/>); null for the ellipsoid of radius Spread.</param>
        public static List<GemDefect> Place(GemCaratsConfig cfg, IEnumerable<string> traits, Random rand, Func<float[], float[]> surfaceAt = null)
        {
            float surface = Math.Clamp(cfg.Inspection.Spread, 0.05f, 1f);
            var defects = new List<GemDefect>();
            foreach (string trait in traits.Where(t => IsLocal(cfg, t)))
            {
                int[] range = cfg.Traits.TryGetValue(trait, out var traitCfg) && traitCfg?.Pieces?.Length >= 2 ? traitCfg.Pieces : new[] { 1, 1 };
                int pieces = Math.Max(1, range[0] + rand.Next(Math.Max(0, range[1] - range[0]) + 1));
                for (int piece = 0; piece < pieces; piece++)
                {
                    defects.Add(PlaceOne(trait, rand, surfaceAt, surface));
                }
            }
            return defects;
        }

        private static GemDefect PlaceOne(string trait, Random rand, Func<float[], float[]> surfaceAt, float surface)
        {
            float[] dir = RandomUnit(rand);
            float[] pos = surfaceAt?.Invoke(dir) ?? new[] { dir[0] * surface, dir[1] * surface, dir[2] * surface };

            float[] normal = null;
            if (trait == "crack")
            {
                // A random direction with its outward part taken away lies along the surface.
                float[] r = RandomUnit(rand);
                float along = r[0] * dir[0] + r[1] * dir[1] + r[2] * dir[2];
                float[] t = { r[0] - along * dir[0], r[1] - along * dir[1], r[2] - along * dir[2] };
                float len = (float)Math.Sqrt(t[0] * t[0] + t[1] * t[1] + t[2] * t[2]);
                normal = len < 1e-4f ? Perpendicular(dir) : new[] { t[0] / len, t[1] / len, t[2] / len };
            }
            return new GemDefect { Trait = trait, Pos = pos, Normal = normal };
        }

        /// <summary>
        /// Grinds out one piece of a flaw. The trait leaves the gem only with its last piece;
        /// returns whether that was it.
        /// </summary>
        public static bool RemovePiece(List<GemDefect> defects, List<string> traits, int index)
        {
            string trait = defects[index].Trait;
            defects.RemoveAt(index);
            if (defects.Any(d => d.Trait == trait)) return false;
            traits.Remove(trait);
            return true;
        }

        private static float[] RandomUnit(Random rand)
        {
            double x, y, z, lenSq;
            do
            {
                x = rand.NextDouble() * 2 - 1; y = rand.NextDouble() * 2 - 1; z = rand.NextDouble() * 2 - 1;
                lenSq = x * x + y * y + z * z;
            } while (lenSq > 1 || lenSq < 1e-4);
            double len = Math.Sqrt(lenSq);
            return new[] { (float)(x / len), (float)(y / len), (float)(z / len) };
        }

        private static float[] Perpendicular(float[] v)
            => Math.Abs(v[1]) < 0.9f ? Normalize(new[] { v[2], 0f, -v[0] }) : Normalize(new[] { 0f, -v[2], v[1] });

        private static float[] Normalize(float[] v)
        {
            float len = (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return len < 1e-6f ? new[] { 1f, 0f, 0f } : new[] { v[0] / len, v[1] / len, v[2] / len };
        }

        // ---- the tree ----

        public static bool HasDefects(ITreeAttribute tree) => tree?.HasAttribute(TraitsKey) == true;

        public static List<GemDefect> Read(ITreeAttribute tree)
        {
            var result = new List<GemDefect>();
            string[] traits = (tree?[TraitsKey] as StringArrayAttribute)?.value;
            float[] pos = (tree?[PosKey] as FloatArrayAttribute)?.value;
            float[] normals = (tree?[NormalKey] as FloatArrayAttribute)?.value;
            if (traits == null || pos == null || pos.Length < traits.Length * 3) return result;

            for (int i = 0; i < traits.Length; i++)
            {
                float[] n = normals != null && normals.Length >= i * 3 + 3 ? normals.Skip(i * 3).Take(3).ToArray() : null;
                result.Add(new GemDefect
                {
                    Trait = traits[i],
                    Pos = pos.Skip(i * 3).Take(3).ToArray(),
                    // A zero normal stands for "none" in the flat array.
                    Normal = n != null && (n[0] != 0 || n[1] != 0 || n[2] != 0) ? n : null
                });
            }
            return result;
        }

        public static void Write(ITreeAttribute tree, List<GemDefect> defects)
        {
            tree[TraitsKey] = new StringArrayAttribute(defects.Select(d => d.Trait).ToArray());
            tree[PosKey] = new FloatArrayAttribute(defects.SelectMany(d => d.Pos).ToArray());
            tree[NormalKey] = new FloatArrayAttribute(defects.SelectMany(d => d.Normal ?? new float[3]).ToArray());
        }

        /// <summary>
        /// The gem's defects, placed now if it has traits but no places for them yet - a gem
        /// whose truth was rolled before the inspection existed, or by the table.
        /// </summary>
        public static List<GemDefect> Ensure(ITreeAttribute tree, GemCaratsConfig cfg, Random rand, Func<float[], float[]> surfaceAt = null)
        {
            if (HasDefects(tree)) return Read(tree);
            var defects = Place(cfg, CANGemAppraisal.GetTraits(tree), rand, surfaceAt);
            Write(tree, defects);
            return defects;
        }

        // ---- the view ----

        /// <summary>The model matrix the gui item renderer builds for this view.</summary>
        public static float[] ModelMatrix(GemView v)
        {
            float[] m = Mat4f.Create();
            Mat4f.Translate(m, m, (int)v.PosX - 3, (int)v.PosY - 1, v.PosZ);
            Mat4f.Translate(m, m, v.Origin[0] + v.Translation[0] * v.GuiScale, v.Origin[1] + v.Translation[1] * v.GuiScale,
                v.Origin[2] * v.Size + v.Translation[2] * v.GuiScale);
            Mat4f.Scale(m, m, v.Size * v.Scale[0], v.Size * v.Scale[1], v.Size * v.Scale[2]);
            Mat4f.RotateX(m, m, v.Rotation[0] * GameMath.DEG2RAD);
            Mat4f.RotateY(m, m, v.Rotation[1] * GameMath.DEG2RAD);
            Mat4f.RotateZ(m, m, v.Rotation[2] * GameMath.DEG2RAD);
            Mat4f.Translate(m, m, -v.Origin[0], -v.Origin[1], -v.Origin[2]);
            return m;
        }

        /// <summary>A place in the gem's -1..1 box as a point of its model.</summary>
        public static float[] ToModel(GemView v, float[] pos)
        {
            var m = new float[3];
            for (int i = 0; i < 3; i++) m[i] = v.BoundsMin[i] + (pos[i] + 1) / 2 * (v.BoundsMax[i] - v.BoundsMin[i]);
            return m;
        }

        /// <summary>Where a place in the gem lands in the framebuffer, in its pixels.</summary>
        public static float[] Project(GemView v, float[] pos)
        {
            float[] m = ToModel(v, pos);
            float[] p = Mat4f.MulWithVec4(ModelMatrix(v), new[] { m[0], m[1], m[2], 1f });
            return new[] { p[0], v.FlipY ? 2 * v.PosY - p[1] : p[1] };
        }

        /// <summary>
        /// A short stretch of a crack's plane through its place, as two places at its ends: drawn
        /// as a line it turns with the gem and shortens when the plane faces the viewer.
        /// </summary>
        public static float[][] CrackEnds(GemDefect d, float halfLength)
        {
            float[] n = d.Normal ?? new[] { 0f, 1f, 0f };
            // Any direction in the plane: the normal crossed with whichever axis it is least like.
            float[] axis = Math.Abs(n[1]) < 0.9f ? new[] { 0f, 1f, 0f } : new[] { 1f, 0f, 0f };
            float[] u = { n[1] * axis[2] - n[2] * axis[1], n[2] * axis[0] - n[0] * axis[2], n[0] * axis[1] - n[1] * axis[0] };
            float len = (float)Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            if (len < 1e-6f) return new[] { d.Pos, d.Pos };
            for (int i = 0; i < 3; i++) u[i] = u[i] / len * halfLength;
            return new[]
            {
                new[] { d.Pos[0] - u[0], d.Pos[1] - u[1], d.Pos[2] - u[2] },
                new[] { d.Pos[0] + u[0], d.Pos[1] + u[1], d.Pos[2] + u[2] }
            };
        }

        public static Vintagestory.API.Common.AssetLocation ShapeOf(Vintagestory.API.Common.CollectibleObject item)
            // Items and blocks each declare their own Shape; CollectibleObject has none.
            => (item as Vintagestory.API.Common.Item)?.Shape?.Base ?? (item as Vintagestory.API.Common.Block)?.Shape?.Base;

        /// <summary>
        /// A crack as it runs over the gem: a few points along its stretch, each nudged a little
        /// sideways (the same way every time for this crack) and laid onto the surface. Drawn as
        /// a jagged line and hit anywhere along it.
        /// </summary>
        public static List<float[]> CrackPath(GemDefect d, float halfLength, Func<float[], float[]> surfaceAt, int points = 5)
        {
            float[][] ends = CrackEnds(d, halfLength);
            // Sideways in the surface: across the crack's line, square to the way out.
            float[] radial = Normalize(d.Pos);
            float[] along = Normalize(new[] { ends[1][0] - ends[0][0], ends[1][1] - ends[0][1], ends[1][2] - ends[0][2] });
            float[] side = Normalize(new[]
            {
                radial[1] * along[2] - radial[2] * along[1],
                radial[2] * along[0] - radial[0] * along[2],
                radial[0] * along[1] - radial[1] * along[0]
            });
            var rand = new Random(d.Pos.Aggregate(17, (h, v) => h * 31 + (int)(v * 10000)));

            var path = new List<float[]>();
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                // The ends stay put, the middle wanders.
                float jitter = i == 0 || i == points - 1 ? 0f : (float)(rand.NextDouble() * 2 - 1) * halfLength * 0.35f;
                float[] p = new float[3];
                for (int k = 0; k < 3; k++) p[k] = ends[0][k] + (ends[1][k] - ends[0][k]) * t + side[k] * jitter;
                float len = (float)Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                float[] onSurface = surfaceAt != null && len > 1e-4f ? surfaceAt(new[] { p[0] / len, p[1] / len, p[2] / len }) : null;
                path.Add(onSurface ?? p);
            }
            return path;
        }

        private static readonly Dictionary<string, List<float[]>> boxesCache = new Dictionary<string, List<float[]>>();

        /// <summary>
        /// The boxes of an item's shape in model units, each min xyz then max xyz (children placed
        /// relative to their parent, rotations ignored). The same asset on both sides gives the
        /// same boxes. Empty when the shape cannot be read.
        /// </summary>
        public static List<float[]> ShapeBoxes(Vintagestory.API.Common.ICoreAPI api, Vintagestory.API.Common.CollectibleObject item)
        {
            Vintagestory.API.Common.AssetLocation shapeLoc = ShapeOf(item);
            if (api == null || shapeLoc == null) return new List<float[]>();
            string key = shapeLoc.ToString();
            lock (boxesCache)
            {
                if (boxesCache.TryGetValue(key, out List<float[]> cached)) return cached;
            }

            var shape = api.Assets.TryGet(shapeLoc.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"))
                ?.ToObject<Vintagestory.API.Common.Shape>();
            var boxes = new List<float[]>();
            void Walk(Vintagestory.API.Common.ShapeElement[] elements, double ox, double oy, double oz)
            {
                if (elements == null) return;
                foreach (var e in elements)
                {
                    if (e?.From == null || e.To == null) continue;
                    double fx = ox + e.From[0], fy = oy + e.From[1], fz = oz + e.From[2];
                    double tx = ox + e.To[0], ty = oy + e.To[1], tz = oz + e.To[2];
                    // An empty element is only a pivot for its children.
                    if (tx != fx && ty != fy && tz != fz)
                    {
                        boxes.Add(new[]
                        {
                            (float)Math.Min(fx, tx) / 16f, (float)Math.Min(fy, ty) / 16f, (float)Math.Min(fz, tz) / 16f,
                            (float)Math.Max(fx, tx) / 16f, (float)Math.Max(fy, ty) / 16f, (float)Math.Max(fz, tz) / 16f
                        });
                    }
                    Walk(e.Children, fx, fy, fz);
                }
            }
            Walk(shape?.Elements, 0, 0, 0);

            lock (boxesCache) boxesCache[key] = boxes;
            return boxes;
        }

        /// <summary>The box around all of a shape's boxes, min then max; null for none.</summary>
        public static float[][] BoundsOf(List<float[]> boxes)
        {
            if (boxes == null || boxes.Count == 0) return null;
            float[] min = { float.MaxValue, float.MaxValue, float.MaxValue };
            float[] max = { float.MinValue, float.MinValue, float.MinValue };
            foreach (float[] b in boxes)
            {
                for (int i = 0; i < 3; i++)
                {
                    min[i] = Math.Min(min[i], b[i]);
                    max[i] = Math.Max(max[i], b[i + 3]);
                }
            }
            return new[] { min, max };
        }

        /// <summary>The boxes of a gem stack: its own shape when it has a seed, else its item's.</summary>
        public static List<float[]> ShapeBoxes(Vintagestory.API.Common.ICoreAPI api, Vintagestory.API.Common.ItemStack stack)
            => CANGemRoughShape.HasSeed(stack)
                ? CANGemRoughShape.Boxes(CANGemRoughShape.SeedOf(stack), CANGemSize.RoughQualityOf(stack))
                : ShapeBoxes(api, stack?.Collectible);

        public static float[][] ShapeBounds(Vintagestory.API.Common.ICoreAPI api, Vintagestory.API.Common.ItemStack stack)
            => BoundsOf(ShapeBoxes(api, stack));

        /// <summary>
        /// Where the surface of a shape made of these boxes is in a direction from the middle of its
        /// bounds: the farthest point the ray from the middle leaves a box, as a place in the -1..1
        /// box. Null when the ray meets no box.
        /// </summary>
        public static float[] SurfaceAt(List<float[]> boxes, float[] dir)
        {
            float[][] bounds = BoundsOf(boxes);
            if (bounds == null) return null;
            float[] c = new float[3], half = new float[3], d = new float[3];
            for (int i = 0; i < 3; i++)
            {
                c[i] = (bounds[0][i] + bounds[1][i]) / 2;
                half[i] = Math.Max(1e-6f, (bounds[1][i] - bounds[0][i]) / 2);
                // Stretched with the box, so a direction means the same on a flat gem and a round one.
                d[i] = dir[i] * half[i];
            }

            float best = -1;
            foreach (float[] b in boxes)
            {
                float tmin = float.MinValue, tmax = float.MaxValue;
                bool miss = false;
                for (int i = 0; i < 3 && !miss; i++)
                {
                    if (Math.Abs(d[i]) < 1e-9f)
                    {
                        if (c[i] < b[i] || c[i] > b[i + 3]) miss = true;
                        continue;
                    }
                    float t1 = (b[i] - c[i]) / d[i], t2 = (b[i + 3] - c[i]) / d[i];
                    tmin = Math.Max(tmin, Math.Min(t1, t2));
                    tmax = Math.Min(tmax, Math.Max(t1, t2));
                }
                if (!miss && tmax >= Math.Max(tmin, 0f)) best = Math.Max(best, tmax);
            }
            if (best <= 0) return null;
            return new[] { dir[0] * best, dir[1] * best, dir[2] * best };
        }

        /// <summary>The gem's surface for <see cref="Place"/>, from its shape; null when the shape gives none.</summary>
        public static Func<float[], float[]> SurfaceFinder(Vintagestory.API.Common.ICoreAPI api, Vintagestory.API.Common.ItemStack stack)
        {
            List<float[]> boxes = ShapeBoxes(api, stack);
            return boxes.Count == 0 ? null : dir => SurfaceAt(boxes, dir);
        }

        /// <summary>
        /// How plainly a defect shows, 0..1: an inclusion always, a crack best when looked at
        /// along its plane, both scaled by how well the lighting shows its kind (the mode).
        /// </summary>
        public static float Visibility(GemView v, GemDefect d, float light)
        {
            float[] r = Mat4f.Create();
            Mat4f.RotateX(r, r, v.Rotation[0] * GameMath.DEG2RAD);
            Mat4f.RotateY(r, r, v.Rotation[1] * GameMath.DEG2RAD);
            Mat4f.RotateZ(r, r, v.Rotation[2] * GameMath.DEG2RAD);

            // How a flaw shows depends on the light only, not on the angle: an angle rule made
            // flaws blink in and out as the gem turned. The far side is hidden by the gem itself.
            float show = 1f;

            // On the far side of the gem it is out of sight; fades in as that side turns round.
            float posLen = (float)Math.Sqrt(d.Pos[0] * d.Pos[0] + d.Pos[1] * d.Pos[1] + d.Pos[2] * d.Pos[2]);
            if (v.FrontZ != 0 && posLen > 1e-4f)
            {
                float[] o = Mat4f.MulWithVec4(r, new[] { d.Pos[0] / posLen, d.Pos[1] / posLen, d.Pos[2] / posLen, 0f });
                show *= Math.Clamp(o[2] * v.FrontZ * 2f, 0f, 1f);
            }
            return Math.Clamp(show * light, 0f, 1f);
        }

        /// <summary>The defect a click at (x, y) finds, -1 for a miss: the nearest within reach that shows well enough.</summary>
        public static int Hit(GemView v, List<GemDefect> defects, float x, float y, float radius, float minVisibility,
            Func<GemDefect, float> modeVisibility, float crackHalfLength = 0f, Func<float[], float[]> surfaceAt = null)
        {
            int best = -1;
            float bestDist = radius;
            for (int i = 0; i < defects.Count; i++)
            {
                if (Visibility(v, defects[i], modeVisibility(defects[i])) < minVisibility) continue;
                // A crack is drawn as a jagged line, so anywhere along it counts.
                List<float[]> path = defects[i].Normal != null && crackHalfLength > 0
                    ? CrackPath(defects[i], crackHalfLength, surfaceAt)
                    : new List<float[]> { defects[i].Pos };
                float dist = float.MaxValue;
                for (int s = 0; s < path.Count; s++)
                {
                    float[] a = Project(v, path[s]);
                    float[] b = Project(v, path[Math.Min(s + 1, path.Count - 1)]);
                    dist = Math.Min(dist, DistanceToSegment(x, y, a, b));
                }
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        private static float DistanceToSegment(float x, float y, float[] a, float[] b)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1];
            float lenSq = dx * dx + dy * dy;
            float t = lenSq > 0 ? Math.Clamp(((x - a[0]) * dx + (y - a[1]) * dy) / lenSq, 0f, 1f) : 0f;
            float px = a[0] + t * dx - x, py = a[1] + t * dy - y;
            return (float)Math.Sqrt(px * px + py * py);
        }

        /// <summary>The mode the light at the player puts the inspection in: the first whose range holds it, null for none.</summary>
        /// <summary>
        /// Every mode the conditions allow at once - the light around and a wet gem are separate
        /// things, so the dark field and immersion can both be on. Empty when none is.
        /// </summary>
        public static string[] ModesFor(InspectionConfig insp, int lightLevel, bool wet = false)
            => insp?.Modes?.Where(m => m.Value?.Allows(lightLevel, wet) == true).Select(m => m.Key).ToArray() ?? new string[0];

        /// <summary>
        /// How plainly a trait shows under several modes at once: each a separate chance to spot
        /// it, so two weak ones together show more than either alone.
        /// </summary>
        public static float CombinedVisibility(InspectionConfig insp, IEnumerable<string> modes, string trait)
        {
            float unseen = 1f;
            foreach (string key in modes)
            {
                if (insp.Modes.TryGetValue(key, out var mode) && mode != null) unseen *= 1f - Math.Clamp(mode.VisibilityOf(trait), 0f, 1f);
            }
            return 1f - unseen;
        }
    }
}
