using System;
using System.Collections.Generic;
using System.Linq;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canjewelry.src.cb
{
    /// <summary>
    /// The loupe's inspection on the server. The gem is taken out of the player's hand for the
    /// length of it and handed back at the end, so nothing the player does to their inventory
    /// meanwhile can swap it for another. Every click is checked here with the math the client
    /// drew with.
    /// </summary>
    public static class CANGemInspectionServer
    {
        private class Session
        {
            public string Id;
            public ItemStack Gem;
            public int Attempts;
            // From the loupe: a better lens forgives a less exact click.
            public float HitRadiusMult = 1f;
            public string LastResult = "";
            // Weight when the inspection began, to move a range already shown along with what it cost.
            public float CaratsAtStart;
            // How precisely the loupe in use reads the weight; see CANItemLoupe.WeighHalfWidth.
            public float? WeighHalfWidth;
        }

        /// <summary>Light at the player now: they may walk into the dark or the sun mid-inspection.</summary>
        private static int LightAt(IServerPlayer player)
            => player.Entity.World.BlockAccessor.GetLightLevel(player.Entity.Pos.AsBlockPos, EnumLightLevelType.MaxTimeOfDayLight);

        private static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();

        private static Config.GemCaratsConfig Cfg => canjewelry.config.GemCarats;

        // Clicks the gem has left, kept on it between inspections, so it can be looked at again
        // somewhere else with what remains.
        public const string ClicksLeftKey = "geminspectleft";

        /// <summary>Whether the loupe may open this gem: clicks left or never inspected.</summary>
        public static bool CanInspect(ItemStack gem)
        {
            ITreeAttribute tree = gem?.Attributes;
            if (tree == null) return false;
            return !tree.HasAttribute(ClicksLeftKey) || tree.GetInt(ClicksLeftKey) > 0;
        }

        /// <summary>
        /// The inspection preview's view of a gem, turned by the player. Gui transforms are loaded
        /// on the client only - on the server GuiTransform is null - so the client sends the one
        /// it drew with (<see cref="Transform"/>) and the server builds the view from that.
        /// </summary>
        public static GemView ViewFor(float[] origin, float[] translation, float[] scale, float[] rotation,
            float rotX, float rotY, float guiScale, int fboSize, float[][] bounds = null)
        {
            return new GemView
            {
                BoundsMin = bounds?[0] ?? new[] { 0f, 0f, 0f },
                BoundsMax = bounds?[1] ?? new[] { 1f, 1f, 1f },
                FlipY = canjewelry.config?.GemCarats?.Inspection?.FlipY == true,
                FrontZ = canjewelry.config?.GemCarats?.Inspection?.FrontZ ?? 0f,
                Origin = Vec3(origin, 0.5f, 0f, 0.5f),
                Translation = Vec3(translation, 0f, 0f, 0f),
                Scale = Vec3(scale, 1f, 1f, 1f),
                Rotation = new[] { Vec3(rotation, 0, 0, 0)[0] + rotX, Vec3(rotation, 0, 0, 0)[1] + rotY, Vec3(rotation, 0, 0, 0)[2] },
                PosX = fboSize / 2f,
                PosY = fboSize / 2f,
                // As JewelerItemPreview draws it; depth does not move the picture in an ortho view.
                PosZ = 100 + fboSize,
                Size = fboSize * 0.55f,
                GuiScale = guiScale
            };
        }

        /// <summary>A gem's gui transform as origin, translation, scale and rotation; the default item one when it has none.</summary>
        public static float[][] Transform(CollectibleObject gem)
        {
            ModelTransform t = gem?.GuiTransform ?? ModelTransform.ItemDefaultGui();
            return new[]
            {
                new[] { t.Origin.X, t.Origin.Y, t.Origin.Z },
                new[] { t.Translation.X, t.Translation.Y, t.Translation.Z },
                new[] { t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z },
                new[] { t.Rotation.X, t.Rotation.Y, t.Rotation.Z }
            };
        }

        public static GemView ViewFor(ICoreAPI api, ItemStack gem, float rotX, float rotY, float guiScale, int fboSize)
        {
            float[][] t = Transform(gem?.Collectible);
            return ViewFor(t[0], t[1], t[2], t[3], rotX, rotY, guiScale, fboSize, CANGemDefects.ShapeBounds(api, gem));
        }

        // A value from a packet: three finite numbers, else the fallback.
        private static float[] Vec3(float[] v, float x, float y, float z)
            => v != null && v.Length == 3 && v.All(f => !float.IsNaN(f) && !float.IsInfinity(f)) ? v : new[] { x, y, z };

        /// <summary>Takes one gem from the hand and opens the inspection. False when it cannot start.</summary>
        public static bool Start(IServerPlayer player, ItemSlot slot)
        {
            var cfg = Cfg;
            if (player == null || slot?.Itemstack == null || cfg?.Inspection?.Enabled != true) return false;
            if (sessions.ContainsKey(player.PlayerUID) || !CanInspect(slot.Itemstack)) return false;

            ItemStack gem = slot.TakeOut(1);
            slot.MarkDirty();
            CANGemAppraisal.EnsureTruth(gem.Attributes, cfg, CANGemSize.RoughQualityOf(gem), CANGemSize.RoughGemTypeOf(gem), Config.rand);
            // The gem gets its own shape now, if it has none yet: before the flaws, which sit on it.
            if (!CANGemRoughShape.HasSeed(gem)) gem.Attributes.SetInt(CANGemRoughShape.SeedKey, Config.rand.Next(1, int.MaxValue));
            CANGemDefects.Ensure(gem.Attributes, cfg, Config.rand, CANGemDefects.SurfaceFinder(player.Entity.Api, gem));
            JsonObject loupe = player.Entity.LeftHandItemSlot?.Itemstack?.Collectible?.Attributes;

            var ev = new integration.AppraiseEvent
            {
                Player = player,
                Gem = gem,
                Stage = AppraiseStage.Inspect,
                Carats = CANGemAppraisal.GetCarats(gem.Attributes),
                Traits = CANGemAppraisal.GetTraits(gem.Attributes),
                RevealCount = 0,
                Attempts = cfg.Inspection.Attempts + (loupe?["inspectAttemptsBonus"].AsInt(0) ?? 0)
            };
            canjewelry.Instance?.FireAppraise(ev);

            // The first inspection sets the gem's clicks; later ones go on with what is left.
            int clicks = gem.Attributes.HasAttribute(ClicksLeftKey) ? gem.Attributes.GetInt(ClicksLeftKey) : Math.Max(1, ev.Attempts);
            var session = new Session
            {
                Id = Guid.NewGuid().ToString("N"),
                Gem = gem,
                Attempts = clicks,
                CaratsAtStart = CANGemAppraisal.GetCarats(gem.Attributes),
                HitRadiusMult = GameMath.Clamp(loupe?["inspectHitRadiusMult"].AsFloat(1f) ?? 1f, 0.5f, 3f),
                WeighHalfWidth = items.CANItemLoupe.WeighHalfWidth(player.Entity)
            };
            sessions[player.PlayerUID] = session;
            Send(player, session, false);
            return true;
        }

        public static void Click(IServerPlayer player, GemInspectClickPacket packet)
        {
            if (player == null || packet == null) return;
            if (!sessions.TryGetValue(player.PlayerUID, out Session s) || s.Id != packet.SessionId || s.Attempts <= 0) return;

            var cfg = Cfg;
            var insp = cfg.Inspection;
            // The light where the player stands decides how the gem is lit. Light no mode covers
            // costs nothing: the player just could not see.
            string[] active = CANGemDefects.ModesFor(insp, LightAt(player),
                CANGemAppraisal.IsWet(s.Gem.Attributes, player.Entity.World.Calendar.TotalHours));
            if (active.Length == 0)
            {
                s.LastResult = "unavailable";
                Send(player, s, false);
                return;
            }
            ITreeAttribute tree = s.Gem.Attributes;
            List<GemDefect> defects = CANGemDefects.Read(tree);
            GemView view = ViewFor(packet.Origin, packet.Translation, packet.Scale, packet.BaseRotation,
                packet.RotationX, packet.RotationY, GameMath.Clamp(packet.GuiScale, 0.5f, 4f), insp.FboSize,
                CANGemDefects.ShapeBounds(player.Entity.Api, s.Gem));

            int hit = CANGemDefects.Hit(view, defects, packet.X, packet.Y, insp.HitRadius * s.HitRadiusMult, insp.MinVisibility,
                d => CANGemDefects.CombinedVisibility(insp, active, d.Trait), insp.CrackHalfLength, CANGemDefects.SurfaceFinder(player.Entity.Api, s.Gem));
            float carats = CANGemAppraisal.GetCarats(tree);
            List<string> traits = CANGemAppraisal.GetTraits(tree).ToList();
            if (hit >= 0)
            {
                GemDefect found = defects[hit];
                // One piece at a time; the trait goes with the last of them.
                CANGemDefects.RemovePiece(defects, traits, hit);
                var ev = new integration.DefectRemovedEvent
                {
                    Player = player,
                    Gem = s.Gem,
                    Trait = found.Trait,
                    CaratCost = cfg.Traits.TryGetValue(found.Trait, out var trait) ? trait.RemoveCost : 0f
                };
                canjewelry.Instance?.FireDefectRemoved(ev);
                carats *= 1 - Math.Clamp(ev.CaratCost, 0f, 1f);
                s.LastResult = found.Trait;
                player.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/block/rock-hit-pickaxe"), player.Entity, null, true, 8f, 0.6f);
            }
            else
            {
                carats *= 1 - insp.MissCost;
                s.LastResult = "miss";
                player.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/player/knap1"), player.Entity, null, true, 8f, 0.4f);
            }
            CANGemAppraisal.SetWeight(tree, Math.Max(cfg.Step, CANGemAppraisal.RoundCarats(carats, cfg.Step)), traits.ToArray());
            CANGemDefects.Write(tree, defects);

            s.Attempts--;
            if (s.Attempts <= 0) Finish(player);
            else Send(player, s, false);
        }

        public static void Close(IServerPlayer player, string sessionId)
        {
            if (player != null && sessions.TryGetValue(player.PlayerUID, out Session s) && s.Id == sessionId) Finish(player);
        }

        /// <summary>Ends the inspection, if one is open, and gives the gem back. Also on leaving the game.</summary>
        public static void Finish(IServerPlayer player)
        {
            if (player == null || !sessions.TryGetValue(player.PlayerUID, out Session s)) return;
            sessions.Remove(player.PlayerUID);

            var cfg = Cfg;
            ITreeAttribute tree = s.Gem.Attributes;
            float carats = CANGemAppraisal.GetCarats(tree);
            float[] qualityRange = CANGemAppraisal.RoughRange(cfg, CANGemSize.RoughQualityOf(s.Gem), CANGemSize.RoughGemTypeOf(s.Gem))
                ?? new[] { carats, carats };
            // The whole-stone traits were seen; whatever local ones are left stay hidden. What was
            // known before stays known, as long as the gem still has it.
            string[] traits = CANGemAppraisal.GetTraits(tree);
            string[] seen = CANGemAppraisal.GetKnownTraits(tree)
                .Concat(traits.Where(t => !CANGemDefects.IsLocal(cfg, t)))
                .Where(traits.Contains)
                .Distinct()
                .ToArray();
            // A range shown before only moves along with the weight the inspection cost; it is
            // read again only through a better loupe, so looking twice with the same one never
            // narrows it down.
            float[] range = CANGemAppraisal.GetCaratsRange(tree);
            if (range != null && s.CaratsAtStart > 0)
            {
                range = CANGemAppraisal.IsExact(range) ? new[] { carats, carats }
                    : CANGemAppraisal.ScaleRange(range, carats / s.CaratsAtStart, cfg.Step);
            }
            float[] fresh = CANGemAppraisal.ShownRange(cfg, carats, qualityRange, s.WeighHalfWidth, Config.rand);
            if (range == null || fresh[1] - fresh[0] < range[1] - range[0] - 0.001f) range = fresh;
            // Seen through the loupe at least; a window polished before stays.
            CANGemAppraisal.SetKnowledge(tree, Math.Max(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(tree)), range, seen);
            tree.SetInt(ClicksLeftKey, Math.Max(0, s.Attempts));

            if (player.Entity?.Alive == true && player.InventoryManager.TryGiveItemstack(s.Gem, true)) { }
            else if (player.Entity != null) player.Entity.World.SpawnItemEntity(s.Gem, player.Entity.Pos.XYZ);

            Send(player, s, true);
            canjewelry.Instance?.FireXp(new integration.CANXpEvent { Player = player, Action = "inspect", Xp = 0f });
        }

        public static bool HasSession(IServerPlayer player) => player != null && sessions.ContainsKey(player.PlayerUID);

        private static void Send(IServerPlayer player, Session s, bool finished)
        {
            List<GemDefect> defects = CANGemDefects.Read(s.Gem.Attributes);
            var cfg = Cfg;
            canjewelry.serverChannel?.SendPacket(new GemInspectStatePacket
            {
                SessionId = s.Id,
                Attempts = s.Attempts,
                LightLevel = player.Entity == null ? 0 : LightAt(player),
                DefectTraits = defects.Select(d => d.Trait).ToArray(),
                DefectPos = defects.SelectMany(d => d.Pos).ToArray(),
                DefectNormals = defects.SelectMany(d => d.Normal ?? new float[3]).ToArray(),
                SeenTraits = CANGemAppraisal.GetTraits(s.Gem.Attributes).Where(t => !CANGemDefects.IsLocal(cfg, t)).ToArray(),
                LastResult = s.LastResult,
                Finished = finished,
                GemCode = s.Gem.Collectible.Code.ToString(),
                ShapeSeed = CANGemRoughShape.SeedOf(s.Gem),
                WetUntil = s.Gem.Attributes.GetDouble(CANGemAppraisal.WetUntilKey)
            }, player);
        }
    }
}
