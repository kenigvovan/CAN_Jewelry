using canjewelry.src.api;
using canjewelry.src.CB;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.gui
{
    /// <summary>
    /// Turns what was tuned in the debug menu into a gemvisuals rule and writes it out.
    ///
    /// <para>Always into the user pose folder under ModConfig, never into the mod's own assets: when
    /// the mod runs unpacked, that folder is the build output and the next build wipes whatever was
    /// tuned into it. ModConfig survives builds and outranks the shipped rules, so it is the right
    /// place to work in; a placement that has settled is copied into the mod's resources by hand.</para>
    ///
    /// <para>A written file is put in force at once as well, so the rules answering for the rest of
    /// the session are the ones the next launch will read. It matters because the menu's tuning and
    /// a rule do not rank alike — tuning speaks for the subject it was made on, a rule for the tier
    /// it matches at — and an item with a rule of its own would otherwise take back over silently
    /// after a restart.</para>
    /// </summary>
    public class CANGemVisualRuleWriter
    {
        /// <summary>Above the shipped rules, so an exported file wins over what it was tuned against.</summary>
        private const int ExportPriority = 100;

        private readonly ICoreClientAPI capi;

        /// <summary>The item being tuned. Fills in the poses of sockets the menu never touched.</summary>
        private readonly ItemStack previewStack;

        private readonly int maxSockets;
        private readonly string[] targets;

        public CANGemVisualRuleWriter(ICoreClientAPI capi, ItemStack previewStack, int maxSockets, string[] targets)
        {
            this.capi = capi;
            this.previewStack = previewStack;
            this.maxSockets = maxSockets;
            this.targets = targets;
        }

        /// <summary>
        /// Writes the rule of one subject, and says whether there was anything to write. On the
        /// clipboard too when asked for by hand, so it can be pasted straight into an asset file —
        /// but not when the menu simply closes, which writes every subject touched and would leave
        /// the clipboard holding whichever came last.
        /// </summary>
        public bool Export(string forSubject, bool toClipboard = false)
        {
            CANGemVisualRule rule = BuildRule(forSubject);
            if (rule == null) return false;

            if (!Write(rule, FileNameFor(forSubject), toClipboard, out bool inForce) || !inForce) return false;

            // The rule now says everything the tuning said, and the two do not rank alike: tuning
            // answers for the subject it was made on, before any rule, while a rule answers at the
            // tier it matches. Letting the tuning stand would show a placement the next launch will
            // not - an item with an exact rule of its own, tuned through a family, is the case.
            // Dropped only once the file is in force, so a write that failed costs nothing.
            CANGemVisualRegistry.ClearOverrides(forSubject);
            return true;
        }

        /// <summary>
        /// Writes a family declaration on its own, so a group survives a restart before any pose is
        /// tuned against it. Its own file: the group's poses are written under
        /// <see cref="FileNameFor"/>, and a bare declaration must not overwrite them.
        /// </summary>
        public bool ExportGroup(string name, string[] patterns)
        {
            if (string.IsNullOrEmpty(name) || patterns == null || patterns.Length == 0) return false;

            // Above the pose file, which carries a declaration of its own: this one is the wording
            // just typed into the menu and has to be the one that answers.
            var rule = new CANGemVisualRule
            {
                Priority = ExportPriority + 1,
                DefineGroups = new Dictionary<string, string[]> { { name, patterns } }
            };
            // A declaration carries no poses, so it keeps no rule of its own - the family it names is
            // registered as the file is read back, which is all this one has to do.
            return Write(rule, "group-" + Sanitize(name) + ".json", false, out _);
        }

        /// <summary>
        /// Removes a family: its declaration file, its poses, and the rules both were read into.
        /// </summary>
        public bool ForgetGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            bool removed = Delete("group-" + Sanitize(name) + ".json");
            removed |= Forget("$" + name);
            return removed;
        }

        /// <summary>One written file and whatever was loaded from it, gone.</summary>
        private bool Delete(string fileName)
        {
            bool removed = CANGemVisualRegistry.RemoveRulesFrom(
                CANGemVisualRegistry.UserRuleSource(fileName)) > 0;

            string path = Path.Combine(CANGemVisualRegistry.UserRuleDirectory, fileName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    capi.ShowChatMessage("[canjewelry] removed " + path);
                    removed = true;
                }
            }
            catch (System.Exception e)
            {
                capi.Logger.Error("[canjewelry] could not remove {0}: {1}", path, e);
                capi.ShowChatMessage("[canjewelry] could not remove " + path + ", see the log");
            }
            return removed;
        }

        /// <summary>
        /// Writes one file and puts it in force. <paramref name="inForce"/> says whether it left a
        /// rule behind — false for a file that only declares a family, and for one that could not be
        /// read back.
        /// </summary>
        private bool Write(CANGemVisualRule rule, string fileName, bool toClipboard, out bool inForce)
        {
            inForce = false;
            string json = JsonConvert.SerializeObject(rule, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            string userDir = CANGemVisualRegistry.UserRuleDirectory;
            string path = Path.Combine(userDir, fileName);
            try
            {
                Directory.CreateDirectory(userDir);
                File.WriteAllText(path, json);
                if (toClipboard) capi.Forms.SetClipboardText(json);

                // In force straight away, in place of what the same file was read as at startup, so
                // what is on screen from here on is what the next launch will read.
                inForce = CANGemVisualRegistry.ApplyUserRuleFile(capi, fileName, json);
                EncrustableCB.ClearMeshCache(capi);
                capi.World?.Player?.Entity?.MarkShapeModified();

                capi.ShowChatMessage("[canjewelry] wrote " + path
                                     + (toClipboard ? " (also copied to the clipboard)" : ""));
            }
            catch (System.Exception e)
            {
                capi.Logger.Error("[canjewelry] could not write {0}: {1}", path, e);
                capi.ShowChatMessage("[canjewelry] could not write " + path + ", see the log");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Takes a subject's written rule back off: the file goes, and so do the rules it was read
        /// into at startup — otherwise a reset would hold only until the next launch, and not even
        /// that far, since the loaded rule keeps answering for the rest of the session.
        /// </summary>
        public bool Forget(string forSubject)
        {
            return Delete(FileNameFor(forSubject));
        }

        /// <summary>
        /// A group writes the file it was loaded from — "$mining" becomes mining.json — so tuning a
        /// family rewrites its rule instead of leaving two rules fighting over the same items. An
        /// item writes a file named after its code, a cut or a size of its own one of their own.
        /// </summary>
        public static string FileNameFor(string forSubject)
        {
            string plain = CANGemVisualRegistry.SplitVariant(forSubject, out string cut, out int? size);
            // Through Sanitize either way: a family is named by hand, and a name carrying a slash or
            // a ".." would otherwise write the file outside the pose folder.
            string name = Sanitize(plain.StartsWith("$") ? plain.Substring(1) : plain);
            if (cut != null) name += "-" + Sanitize(cut);
            if (size != null) name += "-size" + size.Value;
            return name + ".json";
        }

        /// <summary>
        /// Builds the rule to export. The "hand" poses are the rule's own; another target is written
        /// as an override only where it actually differs, so the file keeps the poses of targets
        /// this session never touched instead of dropping them. Sockets the menu never touched are
        /// filled from what the item resolves to now, so the file is complete.
        /// </summary>
        public CANGemVisualRule BuildRule(string forSubject)
        {
            // A subject tuned for one kind of gem carries it as "code@cut#size"; the rule says so
            // with its own "cut" and "size" fields and matches on the plain code.
            string plainSubject = CANGemVisualRegistry.SplitVariant(forSubject, out string forCut, out int? forSize);

            // A subject switched off needs no poses at all: the rule says "hidden" and is done.
            if (CANGemVisualRegistry.GetHiddenOverride(forSubject) == true)
            {
                var hiding = new CANGemVisualRule
                {
                    Priority = ExportPriority, Hidden = true, Cut = forCut, Size = forSize
                };
                NameSubject(hiding, plainSubject);
                return hiding;
            }
            if (!CANGemVisualRegistry.IsTuned(forSubject)) return null;

            int count = System.Math.Max(
                System.Math.Max(CANGemVisualRegistry.TunedSocketCount(forSubject), maxSockets), 1);

            var rule = new CANGemVisualRule { Priority = ExportPriority, Cut = forCut, Size = forSize };
            NameSubject(rule, plainSubject);

            string attachElement = CANGemVisualRegistry.GetAttachOverride(forSubject, CANGemVisualSide.Front);
            string attachElementBack = CANGemVisualRegistry.GetAttachOverride(forSubject, CANGemVisualSide.Back);
            rule.AttachElement = attachElement;
            rule.AttachElementBack = attachElementBack == attachElement ? null : attachElementBack;

            // Trailing repeats are dropped: a socket past the end of the list reuses the last pose,
            // so a row of identical sockets is one line in the file rather than three.
            ModelTransform[] hand = TrimRepeats(
                PosesFor(forSubject, CANGemVisualTarget.Hand, count, CANGemVisualSide.Front));
            rule.Sockets = hand;

            bool handFlip = FarSideOn(forSubject, CANGemVisualTarget.Hand);
            if (!handFlip) rule.Flip = false;
            // The far side is written out only where it is not simply the near side turned around,
            // so a file stays as short as what was actually tuned by hand.
            else rule.SocketsBack = TunedBackPoses(forSubject, CANGemVisualTarget.Hand, hand);

            foreach (string other in targets)
            {
                if (other == CANGemVisualTarget.Hand) continue;

                ModelTransform[] poses = TrimRepeats(PosesFor(forSubject, other, count, CANGemVisualSide.Front));
                bool flip = FarSideOn(forSubject, other);
                ModelTransform[] back = flip ? TunedBackPoses(forSubject, other, poses) : null;

                // Nothing to write when this target ends up at the same placement as the rule
                // itself, its far side included.
                bool sameBack = back == null
                    ? rule.SocketsBack == null
                    : rule.SocketsBack != null && PosesEqual(back, rule.SocketsBack);
                if (PosesEqual(poses, hand) && flip == handFlip && sameBack) continue;

                var over = new CANGemVisualTargetPoses { Sockets = poses };
                if (flip != handFlip) over.Flip = flip;
                // Without its own far side this target would fall back to the rule's, which was
                // tuned against different near side poses - so it is written out in full.
                if (flip && (back != null || rule.SocketsBack != null))
                {
                    over.SocketsBack = back ?? AutoBackPoses(poses);
                }

                rule.Targets ??= new Dictionary<string, CANGemVisualTargetPoses>();
                rule.Targets[other] = over;
            }
            return rule;
        }

        /// <summary>
        /// Points the rule at what it was tuned for. A family of our own travels with it, so the
        /// exported file stands on its own — the config's groups are already everywhere and need no
        /// declaration.
        /// </summary>
        private static void NameSubject(CANGemVisualRule rule, string plainSubject)
        {
            if (!plainSubject.StartsWith("$"))
            {
                rule.Match = new[] { plainSubject };
                return;
            }

            rule.Group = plainSubject.Substring(1);
            string[] patterns = CANGemVisualRegistry.GroupPatterns(rule.Group);
            if (patterns != null)
            {
                rule.DefineGroups = new Dictionary<string, string[]> { { rule.Group, patterns } };
            }
        }

        /// <summary>
        /// Drops poses at the end of the list that only repeat the one before them. A socket the
        /// list does not reach falls back to its last entry, so the shorter list says the same thing.
        /// </summary>
        private static ModelTransform[] TrimRepeats(ModelTransform[] poses)
        {
            if (poses == null || poses.Length < 2) return poses;

            int last = poses.Length - 1;
            while (last > 0 && PoseEquals(poses[last], poses[last - 1])) last--;
            if (last == poses.Length - 1) return poses;

            var trimmed = new ModelTransform[last + 1];
            System.Array.Copy(poses, trimmed, last + 1);
            return trimmed;
        }

        /// <summary>
        /// The far side poses of a target when they were moved off the derived ones, null when they
        /// still are exactly the near side turned around.
        /// </summary>
        private ModelTransform[] TunedBackPoses(string forSubject, string forTarget, ModelTransform[] front)
        {
            var back = new ModelTransform[front.Length];
            bool differs = false;
            for (int i = 0; i < front.Length; i++)
            {
                ModelTransform auto = CANGemVisualRegistry.FlipPose(front[i], CANGemVisualRegistry.DefaultFlipCenter);
                ModelTransform pose = CANGemVisualRegistry.GetOverride(forSubject, forTarget, i, CANGemVisualSide.Back)
                                      ?? (previewStack == null
                                          ? null
                                          : CANGemVisualRegistry.ResolvePose(previewStack, i, forTarget,
                                              CANGemVisualSide.Back));
                back[i] = pose ?? auto;
                if (pose != null && !PoseEquals(pose, auto)) differs = true;
            }
            return differs ? back : null;
        }

        private static ModelTransform[] AutoBackPoses(ModelTransform[] front)
        {
            var back = new ModelTransform[front.Length];
            for (int i = 0; i < front.Length; i++)
            {
                back[i] = CANGemVisualRegistry.FlipPose(front[i], CANGemVisualRegistry.DefaultFlipCenter);
            }
            return back;
        }

        /// <summary>Whether the far side gem is on for a subject and target, tuned value first.</summary>
        private bool FarSideOn(string forSubject, string forTarget)
        {
            bool? tuned = CANGemVisualRegistry.GetFlipOverride(forSubject, forTarget);
            if (tuned != null) return tuned.Value;

            return previewStack != null
                   && CANGemVisualRegistry.ResolvePose(previewStack, 0, forTarget, CANGemVisualSide.Back) != null;
        }

        private ModelTransform[] PosesFor(string forSubject, string forTarget, int count, int forSide)
        {
            var poses = new ModelTransform[count];
            for (int i = 0; i < count; i++)
            {
                poses[i] = CANGemVisualRegistry.GetOverride(forSubject, forTarget, i, forSide)
                           ?? (previewStack == null
                               ? null
                               : CANGemVisualRegistry.ResolvePose(previewStack, i, forTarget, forSide))
                           ?? ModelTransform.NoTransform;
            }
            return poses;
        }

        private static bool PosesEqual(ModelTransform[] a, ModelTransform[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!PoseEquals(a[i], b[i])) return false;
            }
            return true;
        }

        private static bool PoseEquals(ModelTransform a, ModelTransform b)
        {
            return a.Translation.Equals(b.Translation) && a.Rotation.Equals(b.Rotation)
                   && a.Origin.Equals(b.Origin) && a.ScaleXYZ.Equals(b.ScaleXYZ);
        }

        private static string Sanitize(string value)
        {
            var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            return new string(chars);
        }
    }
}
