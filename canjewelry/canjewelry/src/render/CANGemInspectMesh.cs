using System.Collections.Generic;
using System.Linq;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.render
{
    /// <summary>
    /// The rough gem as the inspection shows it: its own shape with the flaws the lighting reveals
    /// added as small cubes on its surface - an inclusion one speck, a crack a row of them. Drawn
    /// in place of the gem's mesh for the one stack the inspection dialog renders.
    /// </summary>
    public static class CANGemInspectMesh
    {
        /// <summary>Marks the stack the inspection renders, so the gem's behavior swaps in this mesh.</summary>
        public const string ViewKey = "geminspectview";

        private static MultiTextureMeshRef current;
        private static string currentKey;

        public static MultiTextureMeshRef Current => current;

        private const string DarkTexture = "game:block/metal/sheet/lead1";
        private const string LightTexture = "game:block/metal/sheet/silver1";

        /// <summary>
        /// Rebuilds the mesh for these flaws if they differ from the last build. Returns whether
        /// it changed, so the caller knows to redraw.
        /// </summary>
        public static bool Update(ICoreClientAPI capi, ItemStack gemStack, List<GemDefect> shown, string mode, float crackHalfLength)
        {
            Item gem = gemStack?.Item;
            string key = gem?.Code + "|" + CANGemRoughShape.SeedOf(gemStack) + "|" + mode + "|"
                + string.Join(";", shown.Select(d => d.Trait + string.Join(",", d.Pos)));
            if (key == currentKey && current != null && !current.Disposed) return false;

            Dispose();
            currentKey = key;
            float[][] bounds = CANGemDefects.ShapeBounds(capi, gemStack);
            if (gem == null || bounds == null) return true;

            // The gem's own shape when it has a seed, so the flaws sit on what the player sees.
            Shape shape = CANGemRoughShape.ShapeFor(capi, gemStack);
            if (shape == null) return true;

            var view = new GemView { BoundsMin = bounds[0], BoundsMax = bounds[1] };
            System.Func<float[], float[]> surfaceAt = CANGemDefects.SurfaceFinder(capi, gemStack);
            // The modes on, comma separated: in the dark field flaws glow, whatever else is on.
            bool dark = mode?.Split(',').Contains("darkfield") == true;
            var added = new List<ShapeElement>();
            foreach (GemDefect d in shown)
            {
                if (d.Normal == null)
                {
                    // An inclusion: a dark speck against light, a glowing one in the dark field.
                    added.Add(Cube(Outward(view, d.Pos, 0.25), 0.35, dark ? "defectlight" : "defectdark", dark ? 200 : 0));
                    continue;
                }

                // A crack: a jagged line of thin bars hugging the surface - dark against the light,
                // glowing in the dark field, where a crack scatters what light there is.
                List<float[]> path = CANGemDefects.CrackPath(d, crackHalfLength, surfaceAt);
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    added.Add(Bar(Outward(view, path[i], 0.08), Outward(view, path[i + 1], 0.08), 0.09,
                        dark ? "defectlight" : "defectdark", dark ? 160 : 0));
                }
            }
            shape.Elements = (shape.Elements ?? new ShapeElement[0]).Concat(added).ToArray();
            shape.ResolveReferences(capi.Logger, "canjewelry gem inspection");

            var texSource = new CANTexSource(capi, capi.ItemTextureAtlas, gem.Textures, "gem inspection " + gem.Code);
            texSource.Overrides["defectdark"] = new AssetLocation(DarkTexture);
            texSource.Overrides["defectlight"] = new AssetLocation(LightTexture);

            capi.Tesselator.TesselateShape("canjewelry gem inspection", shape, out MeshData mesh, texSource);
            current = capi.Render.UploadMultiTextureMesh(mesh);
            return true;
        }

        /// <summary>
        /// A place on the surface as a model point, pushed out from the gem's middle by a few
        /// tenths of a voxel so the flaw sits on the gem rather than sinking into it.
        /// </summary>
        private static float[] Outward(GemView view, float[] pos, double voxels)
        {
            float[] p = CANGemDefects.ToModel(view, pos);
            float[] c = CANGemDefects.ToModel(view, new[] { 0f, 0f, 0f });
            float dx = p[0] - c[0], dy = p[1] - c[1], dz = p[2] - c[2];
            float len = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-6f) return p;
            float push = (float)(voxels / 16.0) / len;
            return new[] { p[0] + dx * push, p[1] + dy * push, p[2] + dz * push };
        }

        /// <summary>A cube of the given half size (in voxels) around a point of the model.</summary>
        private static ShapeElement Cube(float[] modelPos, double half, string texture, int glow)
        {
            double x = modelPos[0] * 16, y = modelPos[1] * 16, z = modelPos[2] * 16;
            var faces = new Dictionary<string, ShapeElementFace>();
            foreach (string facing in new[] { "north", "east", "south", "west", "up", "down" })
            {
                faces[facing] = new ShapeElementFace { Texture = "#" + texture, Uv = new[] { 0f, 0f, 2f, 2f }, Glow = glow };
            }
            var element = new ShapeElement
            {
                Name = "canjewelry-defect",
                From = new[] { x - half, y - half, z - half },
                To = new[] { x + half, y + half, z + half },
                RotationOrigin = new[] { x, y, z },
                Faces = faces
            };
            element.TrimTextureNamesAndResolveFaces();
            return element;
        }

        /// <summary>
        /// A thin bar from one point of the model to another: a box along x, turned so x runs
        /// from a to b. Elements turn z first, then y (and x, unused here) about their origin.
        /// </summary>
        private static ShapeElement Bar(float[] a, float[] b, double halfThickness, string texture, int glow)
        {
            double ax = a[0] * 16, ay = a[1] * 16, az = a[2] * 16;
            double dx = b[0] * 16 - ax, dy = b[1] * 16 - ay, dz = b[2] * 16 - az;
            double length = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double cx = ax + dx / 2, cy = ay + dy / 2, cz = az + dz / 2;
            double half = length / 2 + halfThickness;

            var faces = new Dictionary<string, ShapeElementFace>();
            foreach (string facing in new[] { "north", "east", "south", "west", "up", "down" })
            {
                faces[facing] = new ShapeElementFace { Texture = "#" + texture, Uv = new[] { 0f, 0f, 2f, 2f }, Glow = glow };
            }
            var element = new ShapeElement
            {
                Name = "canjewelry-crack",
                From = new[] { cx - half, cy - halfThickness, cz - halfThickness },
                To = new[] { cx + half, cy + halfThickness, cz + halfThickness },
                RotationOrigin = new[] { cx, cy, cz },
                RotationY = System.Math.Atan2(-dz, dx) * 180 / System.Math.PI,
                RotationZ = System.Math.Atan2(dy, System.Math.Sqrt(dx * dx + dz * dz)) * 180 / System.Math.PI,
                Faces = faces
            };
            element.TrimTextureNamesAndResolveFaces();
            return element;
        }

        public static void Dispose()
        {
            current?.Dispose();
            current = null;
            currentKey = null;
        }
    }
}
