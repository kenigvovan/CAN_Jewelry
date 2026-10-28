using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace canjewelry.src.utils
{
    /// <summary>
    /// A rough gem's own shape, grown from a seed it gets at its first inspection: a few lumps
    /// around its middle, sized by its quality. Built alike on server and client from the seed, so
    /// both find its surface in the same place. Gems without a seed keep their item's shape and
    /// stack as before.
    /// </summary>
    public static class CANGemRoughShape
    {
        public const string SeedKey = "gemshapeseed";

        public static bool HasSeed(ItemStack stack) => stack?.Attributes?.HasAttribute(SeedKey) == true;

        public static int SeedOf(ItemStack stack) => stack?.Attributes?.GetInt(SeedKey) ?? 0;

        private struct Lump
        {
            public double[] From, To;
            public float RotationY;
        }

        /// <summary>Lump sizes by rough quality: chipped, flawed, normal.</summary>
        private static double SizeOf(string quality)
            => quality == CANGemSize.RoughChipped ? 0.8 : quality == CANGemSize.RoughNormal ? 1.25 : 1.0;

        // In voxels, centred where the item shapes of rough gems sit: middle of the block, on the ground.
        private static List<Lump> Lumps(int seed, string quality)
        {
            var rand = new Random(seed);
            double size = SizeOf(quality);
            int count = 4 + rand.Next(4);
            var lumps = new List<Lump>();
            for (int i = 0; i < count; i++)
            {
                double hx = (0.6 + rand.NextDouble() * 0.6) * size;
                double hy = (0.4 + rand.NextDouble() * 0.5) * size;
                double hz = (0.6 + rand.NextDouble() * 0.6) * size;
                // The first lump is the core; the rest stick out of it in any direction.
                double ox = i == 0 ? 0 : (rand.NextDouble() * 2 - 1) * 1.1 * size;
                double oz = i == 0 ? 0 : (rand.NextDouble() * 2 - 1) * 1.1 * size;
                double oy = i == 0 ? 0 : rand.NextDouble() * 0.9 * size;
                double cx = 8 + ox, cz = 8 + oz, cy = hy + oy;
                // Not turned: the surface is found on these very boxes, and a turned lump would
                // cover flaws placed on the unturned one.
                lumps.Add(new Lump
                {
                    From = new[] { cx - hx, cy - hy, cz - hz },
                    To = new[] { cx + hx, cy + hy, cz + hz },
                    RotationY = 0f
                });
            }
            return lumps;
        }

        /// <summary>The lumps as boxes in model units (rotations left out), for finding the surface.</summary>
        public static List<float[]> Boxes(int seed, string quality)
            => Lumps(seed, quality).Select(l => new[]
            {
                (float)l.From[0] / 16f, (float)l.From[1] / 16f, (float)l.From[2] / 16f,
                (float)l.To[0] / 16f, (float)l.To[1] / 16f, (float)l.To[2] / 16f
            }).ToList();

        /// <summary>The lumps as a shape textured with the given code on every face.</summary>
        public static Shape BuildShape(int seed, string quality, string textureCode)
        {
            var elements = new List<ShapeElement>();
            int n = 0;
            foreach (Lump l in Lumps(seed, quality))
            {
                var faces = new Dictionary<string, ShapeElementFace>();
                foreach (string facing in new[] { "north", "east", "south", "west", "up", "down" })
                {
                    // The texture is sampled over the face's own size, as a hand-made shape would.
                    float w = (float)Math.Max(l.To[0] - l.From[0], l.To[2] - l.From[2]);
                    float h = (float)(facing == "up" || facing == "down" ? Math.Max(l.To[0] - l.From[0], l.To[2] - l.From[2]) : l.To[1] - l.From[1]);
                    float u = (n * 3) % 12;
                    faces[facing] = new ShapeElementFace { Texture = "#" + textureCode, Uv = new[] { u, u, u + w, u + h } };
                }
                var element = new ShapeElement
                {
                    Name = "lump" + n++,
                    From = l.From,
                    To = l.To,
                    RotationOrigin = new[] { (l.From[0] + l.To[0]) / 2, (l.From[1] + l.To[1]) / 2, (l.From[2] + l.To[2]) / 2 },
                    RotationY = l.RotationY,
                    Faces = faces
                };
                element.TrimTextureNamesAndResolveFaces();
                elements.Add(element);
            }
            return new Shape { Elements = elements.ToArray(), TextureWidth = 16, TextureHeight = 16 };
        }

        /// <summary>
        /// The shape a rough gem stack is drawn with: its own when it has a seed, else its item's.
        /// A fresh copy each call, safe to add elements to. Null when there is none.
        /// </summary>
        public static Shape ShapeFor(ICoreAPI api, ItemStack stack)
        {
            if (stack?.Collectible == null) return null;
            if (HasSeed(stack))
            {
                string texture = (stack.Collectible as Item)?.Textures?.Keys.FirstOrDefault() ?? "gem";
                return BuildShape(SeedOf(stack), CANGemSize.RoughQualityOf(stack), texture);
            }
            AssetLocation loc = CANGemDefects.ShapeOf(stack.Collectible);
            return loc == null ? null : api.Assets.TryGet(loc.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"))?.ToObject<Shape>();
        }
    }
}
