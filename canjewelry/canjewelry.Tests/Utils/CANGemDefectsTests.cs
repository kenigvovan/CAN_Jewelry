using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src.utils;
using Vintagestory.API.Datastructures;
using Xunit;
using static canjewelry.src.Config;

namespace canjewelry.Tests.Utils
{
    public class CANGemDefectsTests
    {
        private static GemView View(float rotX = 0, float rotY = 0) => new GemView
        {
            Origin = new[] { 0.5f, 0f, 0.5f },
            Rotation = new[] { rotX, rotY, 0f },
            PosX = 150, PosY = 150, PosZ = 100, Size = 165
        };

        [Fact]
        public void Place_OnlyLocalTraits_InsideTheEllipsoid()
        {
            var cfg = new GemCaratsConfig();
            List<GemDefect> defects = CANGemDefects.Place(cfg, new[] { "crack", "inclusions", "cloudy" }, new Random(3));

            // One crack, a scatter of 1-3 inclusion specks, nothing for the whole-stone cloudiness.
            Assert.Equal(1, defects.Count(d => d.Trait == "crack"));
            Assert.InRange(defects.Count(d => d.Trait == "inclusions"), 1, 3);
            Assert.DoesNotContain(defects, d => d.Trait == "cloudy");
            foreach (GemDefect d in defects)
            {
                double r = Math.Sqrt(d.Pos.Sum(c => (double)c * c));
                Assert.True(Math.Abs(r - cfg.Inspection.Spread) < 1e-3, "a defect is off the gem's surface");
            }
            // A crack runs along the surface: its plane holds the outward direction.
            GemDefect crack = defects.Find(d => d.Trait == "crack")!;
            Assert.True(Math.Abs(crack.Normal!.Zip(crack.Pos, (n, p) => n * p).Sum()) < 1e-3);
            Assert.NotNull(defects.Find(d => d.Trait == "crack")!.Normal);
            Assert.Null(defects.Find(d => d.Trait == "inclusions")!.Normal);
        }

        // The ray from the middle leaves the shape where its outermost box ends.
        [Fact]
        public void SurfaceAt_FindsWhereTheShapeEnds()
        {
            var boxes = new List<float[]>
            {
                new[] { 0f, 0f, 0f, 1f, 1f, 1f },
                // A smaller lump on top: the surface upwards is its top, sideways the big box.
                new[] { 0.4f, 1f, 0.4f, 0.6f, 1.5f, 0.6f }
            };
            float[] side = CANGemDefects.SurfaceAt(boxes, new[] { 1f, 0f, 0f })!;
            float[] up = CANGemDefects.SurfaceAt(boxes, new[] { 0f, 1f, 0f })!;

            // Bounds are 0..1 by 0..1.5 by 0..1, middle at y 0.75.
            Assert.Equal(1f, side[0], 3);
            Assert.Equal(1f, up[1], 3);
            Assert.Null(CANGemDefects.SurfaceAt(new List<float[]>(), new[] { 1f, 0f, 0f }));
        }

        // The same seed grows the same shape - server and client must agree on it.
        [Fact]
        public void RoughShape_SameSeedSameBoxes_OtherSeedOther()
        {
            var a = CANGemRoughShape.Boxes(7, "flawed");
            var b = CANGemRoughShape.Boxes(7, "flawed");
            var c = CANGemRoughShape.Boxes(8, "flawed");

            Assert.InRange(a.Count, 4, 7);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);
            Assert.False(a.Count == c.Count && a.Zip(c, (x, y) => x.SequenceEqual(y)).All(e => e));
            // Bigger rough quality, bigger gem.
            Assert.True(CANGemDefects.BoundsOf(CANGemRoughShape.Boxes(7, "normal"))![1][0]
                > CANGemDefects.BoundsOf(CANGemRoughShape.Boxes(7, "chipped"))![1][0]);
        }

        // Ground out speck by speck: the trait stays while any piece of it is left.
        [Fact]
        public void RemovePiece_TraitGoesWithItsLastPiece()
        {
            var defects = new List<GemDefect>
            {
                new GemDefect { Trait = "inclusions", Pos = new[] { 0.1f, 0f, 0f } },
                new GemDefect { Trait = "inclusions", Pos = new[] { -0.1f, 0f, 0f } },
                new GemDefect { Trait = "crack", Pos = new[] { 0f, 0.1f, 0f }, Normal = new[] { 1f, 0f, 0f } }
            };
            var traits = new List<string> { "inclusions", "crack" };

            Assert.False(CANGemDefects.RemovePiece(defects, traits, 0));
            Assert.Contains("inclusions", traits);
            Assert.True(CANGemDefects.RemovePiece(defects, traits, 0));
            Assert.Equal(new[] { "crack" }, traits);
            Assert.Single(defects);
        }

        [Fact]
        public void ReadWrite_RoundTrips()
        {
            var tree = new TreeAttribute();
            var defects = new List<GemDefect>
            {
                new GemDefect { Trait = "crack", Pos = new[] { 0.4f, 0.1f, 0.5f }, Normal = new[] { 0f, 1f, 0f } },
                new GemDefect { Trait = "inclusions", Pos = new[] { 0.6f, 0.1f, 0.4f } }
            };
            CANGemDefects.Write(tree, defects);
            List<GemDefect> back = CANGemDefects.Read(tree);

            Assert.Equal(2, back.Count);
            Assert.Equal(new[] { 0f, 1f, 0f }, back[0].Normal);
            Assert.Null(back[1].Normal);
            Assert.Equal(defects[1].Pos, back[1].Pos);
        }

        // A crack shows when looked at along its plane, not face on.
        [Fact]
        // The light decides, not the angle: a flaw does not blink in and out as the gem turns.
        public void Visibility_DependsOnTheLightOnly()
        {
            var crack = new GemDefect { Trait = "crack", Pos = new[] { 0.5f, 0f, 0.5f }, Normal = new[] { 0f, 0f, 1f } };
            var inclusion = new GemDefect { Trait = "inclusions", Pos = new[] { 0.5f, 0f, 0.5f } };

            foreach (float rot in new[] { 0f, 45f, 90f, 180f })
            {
                Assert.Equal(1f, CANGemDefects.Visibility(View(rotY: rot), crack, 1f), 3);
                Assert.Equal(0.5f, CANGemDefects.Visibility(View(rotY: rot), crack, 0.5f), 3);
                Assert.Equal(0.5f, CANGemDefects.Visibility(View(rotY: rot), inclusion, 0.5f), 3);
            }
        }

        // A defect on the far side of the gem is out of sight until that side is turned round.
        [Fact]
        public void Visibility_FarSideIsHidden()
        {
            var front = new GemDefect { Trait = "inclusions", Pos = new[] { 0f, 0f, 0.85f } };
            GemView facing = View();
            facing.FrontZ = 1f;
            GemView turned = View(rotY: 180);
            turned.FrontZ = 1f;

            Assert.Equal(1f, CANGemDefects.Visibility(facing, front, 1f), 3);
            Assert.Equal(0f, CANGemDefects.Visibility(turned, front, 1f), 3);
            Assert.Equal(-1, CANGemDefects.Hit(turned, new List<GemDefect> { front },
                CANGemDefects.Project(turned, front.Pos)[0], CANGemDefects.Project(turned, front.Pos)[1], 10, 0.3f, _ => 1f));
        }

        [Fact]
        public void Hit_FindsTheDefectUnderTheClick_WithinReach()
        {
            var defects = new List<GemDefect>
            {
                new GemDefect { Trait = "inclusions", Pos = new[] { 0.4f, 0.1f, 0.5f } },
                new GemDefect { Trait = "inclusions", Pos = new[] { 0.6f, 0.1f, 0.5f } }
            };
            GemView view = View(30, 40);
            float[] p = CANGemDefects.Project(view, defects[1].Pos);

            Assert.Equal(1, CANGemDefects.Hit(view, defects, p[0] + 3, p[1], 10, 0.3f, _ => 1f));
            Assert.Equal(-1, CANGemDefects.Hit(view, defects, p[0] + 60, p[1] + 60, 10, 0.3f, _ => 1f));
            // A lighting that hardly shows it does not let it be hit either.
            Assert.Equal(-1, CANGemDefects.Hit(view, defects, p[0], p[1], 10, 0.3f, _ => 0.2f));
        }

        [Fact]
        public void Hit_AnywhereAlongACrack()
        {
            var crack = new GemDefect { Trait = "crack", Pos = new[] { 0f, 0f, 0f }, Normal = new[] { 1f, 0f, 0f } };
            GemView view = View(rotY: 90);
            float[][] ends = CANGemDefects.CrackEnds(crack, 0.5f);
            float[] end = CANGemDefects.Project(view, ends[1]);

            Assert.Equal(0, CANGemDefects.Hit(view, new List<GemDefect> { crack }, end[0], end[1], 3, 0f, _ => 1f, 0.5f));
            // Taken as a point, the same click at the crack's end is too far from its middle.
            Assert.Equal(-1, CANGemDefects.Hit(view, new List<GemDefect> { crack }, end[0], end[1], 3, 0f, _ => 1f, 0f));
        }

        // A crack is a jagged line: same every time, ends fixed, laid onto the surface when there is one.
        [Fact]
        public void CrackPath_Jagged_Stable_OnTheSurface()
        {
            var crack = new GemDefect { Trait = "crack", Pos = new[] { 0f, 0.6f, 0f }, Normal = new[] { 1f, 0f, 0f } };
            List<float[]> a = CANGemDefects.CrackPath(crack, 0.4f, null);
            List<float[]> b = CANGemDefects.CrackPath(crack, 0.4f, null);
            float[][] ends = CANGemDefects.CrackEnds(crack, 0.4f);

            Assert.Equal(5, a.Count);
            for (int i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);
            Assert.Equal(ends[0], a[0]);
            Assert.Equal(ends[1], a[4]);

            // Laid onto a sphere of radius 0.9: every point ends up on it.
            List<float[]> onSphere = CANGemDefects.CrackPath(crack, 0.4f, dir => new[] { dir[0] * 0.9f, dir[1] * 0.9f, dir[2] * 0.9f });
            Assert.All(onSphere, p => Assert.Equal(0.9f, (float)Math.Sqrt(p.Sum(c => c * c)), 3));
        }

        [Fact]
        public void ToModel_StretchesTheBoxOverTheBounds()
        {
            var view = new GemView { BoundsMin = new[] { 0.4f, 0f, 0.3f }, BoundsMax = new[] { 0.6f, 0.2f, 0.5f } };
            Assert.Equal(new[] { 0.5f, 0.1f, 0.4f }, CANGemDefects.ToModel(view, new[] { 0f, 0f, 0f }));
            Assert.Equal(new[] { 0.6f, 0.2f, 0.5f }, CANGemDefects.ToModel(view, new[] { 1f, 1f, 1f }));
        }

        [Fact]
        public void Project_FollowsTheRotation()
        {
            float[] pos = { 0.8f, 0f, 0.5f };
            float[] a = CANGemDefects.Project(View(), pos);
            float[] b = CANGemDefects.Project(View(rotY: 90), pos);
            Assert.NotEqual(a[0], b[0], 1);
        }

        // Where the player stands decides the light mode: dark, shade, bright - one of them.
        [Theory]
        [InlineData(0, "darkfield")]
        [InlineData(5, "darkfield")]
        [InlineData(6, "reflected")]
        [InlineData(10, "reflected")]
        [InlineData(11, "transmitted")]
        [InlineData(32, "transmitted")]
        public void ModesFor_FollowTheLight(int light, string expected)
        {
            Assert.Equal(new[] { expected }, CANGemDefects.ModesFor(new InspectionConfig(), light));
        }

        // A wet gem adds immersion on top of whatever the light gives, in the dark too.
        [Theory]
        [InlineData(3, "darkfield")]
        [InlineData(8, "reflected")]
        [InlineData(15, "transmitted")]
        public void ModesFor_WetGem_AddsImmersion(int light, string byLight)
        {
            string[] modes = CANGemDefects.ModesFor(new InspectionConfig(), light, wet: true);
            Assert.Equal(2, modes.Length);
            Assert.Contains("immersion", modes);
            Assert.Contains(byLight, modes);
        }

        [Fact]
        public void ModesFor_LightNoModeCovers_IsEmpty()
        {
            var insp = new InspectionConfig();
            insp.Modes.Remove("reflected");
            Assert.Empty(CANGemDefects.ModesFor(insp, 8));
        }

        // Two modes are two chances to spot a flaw: together they show more than either.
        [Fact]
        public void CombinedVisibility_AddsUpAsChances()
        {
            var insp = new InspectionConfig();
            insp.Modes = new Dictionary<string, InspectModeConfig>
            {
                { "darkfield", new InspectModeConfig { Visibility = new Dictionary<string, float> { { "crack", 0.3f } } } },
                { "immersion", new InspectModeConfig { Visibility = new Dictionary<string, float> { { "crack", 0.4f } } } }
            };
            float dark = CANGemDefects.CombinedVisibility(insp, new[] { "darkfield" }, "crack");
            float wet = CANGemDefects.CombinedVisibility(insp, new[] { "immersion" }, "crack");
            float both = CANGemDefects.CombinedVisibility(insp, new[] { "darkfield", "immersion" }, "crack");

            Assert.Equal(1 - (1 - dark) * (1 - wet), both, 4);
            Assert.True(both > dark && both > wet);
            Assert.Equal(0f, CANGemDefects.CombinedVisibility(insp, new string[0], "crack"));
        }

        // Each mode shows one kind of flaw and not the other: cracks in the shade or wet,
        // inclusions in bright light or the dark.
        [Theory]
        [InlineData("reflected", "crack")]
        [InlineData("immersion", "crack")]
        [InlineData("transmitted", "inclusions")]
        [InlineData("darkfield", "inclusions")]
        public void Modes_ShowOneKindOfFlaw(string mode, string shows)
        {
            var m = new InspectionConfig().Modes[mode];
            string other = shows == "crack" ? "inclusions" : "crack";
            Assert.Equal(1f, m.VisibilityOf(shows));
            Assert.Equal(0f, m.VisibilityOf(other));
        }
    }
}
