using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace canjewelry.src.api
{
    /// <summary>
    /// What the debug menu is tuning right now: poses, far side switches, bones and families
    /// switched off. Tool state rather than mod data — it lives only for the session, wins over
    /// every loaded rule, and is written to a file only when the menu exports it.
    /// </summary>
    public static partial class CANGemVisualRegistry
    {
        // Keyed by OverrideKey; the subject is an item code or "$group".
        private static readonly Dictionary<string, ModelTransform> overrides
            = new Dictionary<string, ModelTransform>();

        /// <summary>
        /// Far side switched on or off in the debug menu, keyed subject and target. Same standing
        /// as a tuned pose: it wins over the rules and is written out by the export.
        /// </summary>
        private static readonly Dictionary<string, bool> flipOverrides = new Dictionary<string, bool>();

        // The gear element a body gem hangs off while it is being picked in the debug menu, keyed
        // by subject. Wins over what any rule says, the same as a tuned pose does.
        private static readonly Dictionary<string, string> attachOverrides = new Dictionary<string, string>();

        // Subjects whose gems are switched off for this session, so a family can be left alone
        // without editing a file first. Same keys as the poses: an item code or "$group".
        private static readonly Dictionary<string, bool> hiddenOverrides = new Dictionary<string, bool>();

        // Subjects whose only tuned poses came from a diagnostic command. They render like any other
        // override, but they are not somebody's work and must not be written into a config file when
        // the menu closes - a probe pose is deliberately absurd and would stay there for good.
        private static readonly HashSet<string> scratchSubjects
            = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        // Which subjects have a tuned pose at all, with the cut and size suffix stripped. Lets the
        // lookup below say "nothing tuned for this item" without building a single variant string.
        private static readonly HashSet<string> tunedBases
            = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>Key of one tuned pose. Subject is an item code, or "$" plus a group name.</summary>
        private static string OverrideKey(string subject, string target, int socketIndex, int side)
            => subject + "|" + target + "|" + socketIndex + "|" + CANGemVisualSide.Name(side);

        /// <summary>
        /// Every subject with anything tuned against it this session — a pose, a far side switch, a
        /// bone or a family switched off. What the keys look like stays in here; the debug menu only
        /// needs to know which subjects to write out.
        /// </summary>
        public static List<string> TunedSubjects()
        {
            var found = new List<string>();

            void Add(string subject)
            {
                if (subject == null || scratchSubjects.Contains(subject)) return;
                if (!found.Contains(subject)) found.Add(subject);
            }

            foreach (string key in overrides.Keys) Add(SubjectOfKey(key));
            foreach (string key in flipOverrides.Keys) Add(SubjectOfKey(key));
            foreach (string key in attachOverrides.Keys) Add(SubjectOfKey(key));
            foreach (string subject in hiddenOverrides.Keys) Add(subject);
            return found;
        }

        /// <summary>Whether anything at all is tuned against a subject.</summary>
        public static bool IsTuned(string subject)
        {
            return TunedSubjects().Contains(subject);
        }

        /// <summary>How many sockets a subject has tuned poses for, counting from its highest.</summary>
        public static int TunedSocketCount(string subject)
        {
            int count = 0;
            foreach (string key in overrides.Keys)
            {
                if (!TryParseOverrideKey(key, out string keySubject, out _, out int socketIndex, out _)) continue;
                if (keySubject != subject) continue;
                count = Math.Max(count, socketIndex + 1);
            }
            return count;
        }

        /// <summary>The subject part of any of the keys above — everything before the first bar.</summary>
        private static string SubjectOfKey(string key)
        {
            int bar = key.IndexOf('|');
            return bar < 0 ? key : key.Substring(0, bar);
        }

        /// <summary>Subject, target, socket and side of a key made by <see cref="OverrideKey"/>.</summary>
        private static bool TryParseOverrideKey(string key, out string subject, out string target,
            out int socketIndex, out int side)
        {
            subject = null;
            target = null;
            socketIndex = 0;
            side = CANGemVisualSide.Front;

            string[] parts = key.Split('|');
            if (parts.Length != 4 || !int.TryParse(parts[2], out socketIndex)) return false;

            subject = parts[0];
            target = parts[1];
            side = CANGemVisualSide.FromName(parts[3]);
            return true;
        }

        /// <remarks>
        /// Deliberately does not drop <c>poseCache</c>: the tuned poses are asked for before the
        /// cache is ever consulted in <see cref="ResolvePose(ItemStack, int, string, int)"/>, so a
        /// cached answer can never shadow one. Move that lookup below the cache and this has to
        /// clear it.
        /// </remarks>
        /// <param name="scratch">
        /// A throwaway pose set by a diagnostic command: in force for this session, never exported.
        /// The first deliberate edit of the same subject makes it a real tuning again.
        /// </param>
        public static void SetOverride(string subject, string target, int socketIndex, int side, ModelTransform pose,
            bool scratch = false)
        {
            overrides[OverrideKey(subject, target, socketIndex, side)] = pose;
            string tunedBase = SplitVariant(subject, out _, out _);
            if (tunedBase != null) tunedBases.Add(tunedBase);
            MarkScratch(subject, scratch);
        }

        private static void MarkScratch(string subject, bool scratch)
        {
            if (subject == null) return;

            if (scratch) scratchSubjects.Add(subject);
            else scratchSubjects.Remove(subject);
        }

        public static ModelTransform GetOverride(string subject, string target, int socketIndex, int side)
        {
            overrides.TryGetValue(OverrideKey(subject, target, socketIndex, side), out var pose);
            return pose;
        }

        /// <summary>Switches the gems of a subject off (or back on) for this session.</summary>
        public static void SetHiddenOverride(string subject, bool hidden)
        {
            if (subject == null) return;

            hiddenOverrides[subject] = hidden;
            MarkScratch(subject, false);
            ClearResolveCache();
        }

        public static bool? GetHiddenOverride(string subject)
        {
            if (subject == null) return null;

            return hiddenOverrides.TryGetValue(subject, out bool hidden) ? hidden : (bool?)null;
        }

        private static bool? FindHiddenOverride(AssetLocation code)
        {
            return FindTunedFlag(code, GetHiddenOverride);
        }

        /// <summary>Picks the gear element a body gem hangs off, per side, for this session.</summary>
        public static void SetAttachOverride(string subject, int side, string element)
        {
            if (subject == null) return;

            string key = AttachKey(subject, side);
            if (string.IsNullOrEmpty(element)) attachOverrides.Remove(key);
            else attachOverrides[key] = element;
            MarkScratch(subject, false);
        }

        public static string GetAttachOverride(string subject, int side)
        {
            if (subject == null) return null;

            attachOverrides.TryGetValue(AttachKey(subject, side), out string element);
            return element;
        }

        private static string AttachKey(string subject, int side)
            => subject + "|" + CANGemVisualSide.Name(side);

        public static string FlipKey(string subject, string target) => subject + "|" + target;

        public static void SetFlipOverride(string subject, string target, bool enabled)
        {
            flipOverrides[FlipKey(subject, target)] = enabled;
            MarkScratch(subject, false);
        }

        public static bool? GetFlipOverride(string subject, string target)
        {
            return flipOverrides.TryGetValue(FlipKey(subject, target), out bool enabled) ? enabled : (bool?)null;
        }

        private static bool? FindFlipOverride(AssetLocation code, string target)
        {
            return FindTunedFlag(code, subject => GetFlipOverride(subject, target));
        }

        /// <summary>Drops every tuned pose of one subject, so it falls back to its rules again.</summary>
        public static void ClearOverrides(string subject)
        {
            string prefix = subject + "|";
            var keys = new List<string>();
            foreach (string key in overrides.Keys)
            {
                if (key.StartsWith(prefix)) keys.Add(key);
            }
            foreach (string key in keys) overrides.Remove(key);
            for (int side = 0; side < CANGemVisualSide.Count; side++) attachOverrides.Remove(AttachKey(subject, side));
            hiddenOverrides.Remove(subject);
            scratchSubjects.Remove(subject);

            keys.Clear();
            foreach (string key in flipOverrides.Keys)
            {
                if (key.StartsWith(prefix)) keys.Add(key);
            }
            foreach (string key in keys) flipOverrides.Remove(key);

            RebuildTunedBases();
        }

        /// <summary>
        /// Drops the poses a subject has for one cut, or for one size, so it falls back to its
        /// general placement. Needed when the debug menu stops tuning per cut or per size: those
        /// poses are searched before the general one, and left behind they would keep winning while
        /// the sliders wrote somewhere else.
        /// </summary>
        public static void ClearVariantOverrides(string subject, string cut, int? size)
        {
            if (subject == null || (cut == null && size == null)) return;

            var keys = new List<string>();
            foreach (string key in overrides.Keys)
            {
                string keyBase = SplitVariant(SubjectOfKey(key), out string keyCut, out int? keySize);
                if (!string.Equals(keyBase, subject, StringComparison.InvariantCultureIgnoreCase)) continue;
                if (cut != null && !string.Equals(keyCut, cut, StringComparison.InvariantCultureIgnoreCase)) continue;
                if (size != null && keySize != size) continue;
                keys.Add(key);
            }
            foreach (string key in keys) overrides.Remove(key);

            RebuildTunedBases();
        }

        /// <summary>
        /// Copies every tuned pose of one cut of a subject onto another cut — every socket, both
        /// sides and every target at once. When the source cut has nothing of its own, the general
        /// poses of the subject are copied instead, which is what makes "start this cut off from
        /// how the rest sits" a single press.
        /// </summary>
        /// <returns>How many poses were copied.</returns>
        public static int CopyCutOverrides(string subject, string fromCut, string toCut, int? size = null)
        {
            if (subject == null || toCut == null
                || string.Equals(fromCut, toCut, StringComparison.InvariantCultureIgnoreCase))
            {
                return 0;
            }

            string target = WithVariant(subject, toCut, size);
            int copied = Copy(WithVariant(subject, fromCut, size), target);
            if (copied == 0) copied = Copy(WithVariant(subject, null, size), target);
            if (copied == 0 && size != null) copied = Copy(subject, target);

            if (copied > 0)
            {
                tunedBases.Add(subject);
                MarkScratch(subject, false);
                ClearResolveCache();
            }
            return copied;
        }

        /// <summary>
        /// Copies every tuned pose of one subject onto another — one item onto the family it belongs
        /// to, say, so a placement worked out on the steel pickaxe speaks for every pickaxe. The
        /// poses written for a particular cut or size come along under the new subject.
        /// </summary>
        /// <returns>How many poses were copied.</returns>
        public static int CopySubjectOverrides(string fromSubject, string toSubject)
        {
            if (fromSubject == null || toSubject == null
                || string.Equals(fromSubject, toSubject, StringComparison.InvariantCultureIgnoreCase))
            {
                return 0;
            }

            // Both the plain subject and every "@cut#size" of it, kept under the same variant.
            var pairs = new List<KeyValuePair<string, string>> { new(fromSubject, toSubject) };
            foreach (string variant in VariantsOf(fromSubject))
            {
                SplitVariant(variant, out string cut, out int? size);
                pairs.Add(new KeyValuePair<string, string>(variant, WithVariant(toSubject, cut, size)));
            }

            int copied = 0;
            foreach (var pair in pairs) copied += Copy(pair.Key, pair.Value);

            if (copied > 0)
            {
                tunedBases.Add(toSubject);
                MarkScratch(toSubject, false);
                ClearResolveCache();
            }
            return copied;
        }

        /// <summary>The tuned "@cut#size" subjects of one subject, the plain one aside.</summary>
        private static List<string> VariantsOf(string subject)
        {
            var found = new List<string>();
            foreach (string key in overrides.Keys)
            {
                string keySubject = SubjectOfKey(key);
                if (found.Contains(keySubject)) continue;

                string keyBase = SplitVariant(keySubject, out _, out _);
                if (!string.Equals(keyBase, subject, StringComparison.InvariantCultureIgnoreCase)) continue;
                if (string.Equals(keySubject, subject, StringComparison.InvariantCultureIgnoreCase)) continue;

                found.Add(keySubject);
            }
            return found;
        }

        /// <summary>Every pose of one subject onto another, keyed the same way.</summary>
        private static int Copy(string fromSubject, string toSubject)
        {
            var poses = new List<KeyValuePair<string, ModelTransform>>();
            foreach (var pair in overrides)
            {
                if (!string.Equals(SubjectOfKey(pair.Key), fromSubject, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                if (!TryParseOverrideKey(pair.Key, out _, out string poseTarget, out int socketIndex, out int side))
                {
                    continue;
                }
                poses.Add(new KeyValuePair<string, ModelTransform>(
                    OverrideKey(toSubject, poseTarget, socketIndex, side), pair.Value));
            }

            foreach (var pose in poses) overrides[pose.Key] = pose.Value?.Clone();
            return poses.Count;
        }

        private static void RebuildTunedBases()
        {
            tunedBases.Clear();
            foreach (string key in overrides.Keys)
            {
                string tunedBase = SplitVariant(SubjectOfKey(key), out _, out _);
                if (tunedBase != null) tunedBases.Add(tunedBase);
            }
        }

        /// <summary>Drops every tuning of this session, whatever subject it was made against.</summary>
        public static void ClearAllOverrides()
        {
            overrides.Clear();
            flipOverrides.Clear();
            attachOverrides.Clear();
            hiddenOverrides.Clear();
            scratchSubjects.Clear();
            tunedBases.Clear();
            ClearResolveCache();
        }

        /// <summary>
        /// A tuned value of the item itself, else of the first family of it that has one. Every
        /// family is tried: an item is often in several, and stopping at the first would make the
        /// answer depend on the order the groups happen to be declared in.
        /// </summary>
        private static T FindTuned<T>(AssetLocation code, System.Func<string, T> lookup) where T : class
        {
            T tuned = lookup(code.ToShortString());
            if (tuned != null) return tuned;

            foreach (string group in FindGroups(code))
            {
                tuned = lookup("$" + group);
                if (tuned != null) return tuned;
            }
            return null;
        }

        /// <inheritdoc cref="FindTuned{T}"/>
        private static bool? FindTunedFlag(AssetLocation code, System.Func<string, bool?> lookup)
        {
            bool? tuned = lookup(code.ToShortString());
            if (tuned != null) return tuned;

            foreach (string group in FindGroups(code))
            {
                tuned = lookup("$" + group);
                if (tuned != null) return tuned;
            }
            return null;
        }

        /// <summary>
        /// A tuned pose for this item: its own first, then the one of its group. Not cached — the
        /// debug menu changes these between frames, and the whole point is to see it at once.
        /// </summary>
        private static ModelTransform FindOverride(AssetLocation code, int socketIndex, string target, int side,
            string cut = null, int? size = null)
        {
            // Narrowest first: this cut in this size, then this cut, then this size, then the
            // placement written for every gem. One tuned pose therefore serves the lot until a
            // particular shape or size is deliberately given its own.
            if (!AnyTunedPose(code)) return null;

            var variants = new[]
            {
                WithVariant(null, cut, size),
                WithVariant(null, cut, null),
                WithVariant(null, null, size),
                ""
            };

            foreach (string variant in variants)
            {
                ModelTransform pose = FindTuned(code,
                    subject => GetOverride(subject + variant, target, socketIndex, side));
                if (pose != null) return pose;
            }
            return null;
        }

        /// <summary>Whether this item, or any family of it, has a tuned pose at all.</summary>
        private static bool AnyTunedPose(AssetLocation code)
        {
            if (tunedBases.Count == 0) return false;
            if (tunedBases.Contains(code.ToShortString())) return true;

            foreach (string group in FindGroups(code))
            {
                if (tunedBases.Contains("$" + group)) return true;
            }
            return false;
        }
    }
}
