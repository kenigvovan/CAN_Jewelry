using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.api
{
    /// <summary>
    /// Where the gem of each socket sits on the model, resolved from the loaded rules. The rule
    /// model lives in CANGemVisualRule.cs, the loading in CANGemVisualLoader.cs and the debug
    /// menu's session tuning in CANGemVisualOverrides.cs — all parts of this same class.
    ///
    /// <para>Poses are content, not a server setting: they are the same for everyone, change only
    /// with the model and are never touched by the server. So they live in assets rather than in
    /// <see cref="Config"/> — no network sync, and a content mod adds poses for its own items with
    /// a single JSON file and no code.</para>
    ///
    /// <para>Resolution walks four levels, most specific first: exact item code → wildcard match →
    /// item group (<c>Config.item_groups</c>) → global default. That way one pose per group already
    /// gives every pickaxe a sensible gem, and only the models that stand out need tuning by hand.
    /// Within a level the higher <c>priority</c> wins.</para>
    /// </summary>
    public static partial class CANGemVisualRegistry
    {
        // Mostly the main client thread: OnBeforeRender runs there, and so does the gear shape
        // assembly. Hence plain dictionaries throughout.
        //
        // The exception is items inside a block (tool rack, shelf, display case): those ask
        // EncrustableCB.GenMesh from the chunk tesselation thread. Left unsynchronised on purpose -
        // writes are rare (rules arriving on join, the debug menu) and the worst case is one chunk
        // logging an exception and being redrawn later. Frequent writes would change that.
        private const int TierExact = 3;
        private const int TierWildcard = 2;
        private const int TierGroup = 1;
        private const int TierDefault = 0;

        private static readonly List<CANGemVisualRule> rules = new List<CANGemVisualRule>();

        // Families declared by the pose files themselves, as code patterns. Client side and purely
        // about where a gem sits, unlike the config's item groups, which also decide socket counts.
        private static readonly Dictionary<string, string[]> visualGroups
            = new Dictionary<string, string[]>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>Families declared for poses, by name. Read by the debug menu.</summary>
        public static IReadOnlyDictionary<string, string[]> VisualGroups => visualGroups;

        /// <summary>
        /// Declares a family of items for poses, or redeclares one. Used by the debug menu, which
        /// writes it into the exported rule so it outlives the session.
        /// </summary>
        public static void DefineGroup(string name, string[] patterns)
        {
            if (string.IsNullOrEmpty(name) || patterns == null || patterns.Length == 0) return;

            visualGroups[name] = patterns;
            ClearResolveCache();
        }

        /// <summary>
        /// Takes a pose family back off, so its items fall back to their own rules. Only families
        /// declared for poses — the config's item groups belong to the server.
        /// </summary>
        public static bool RemoveGroup(string name)
        {
            if (string.IsNullOrEmpty(name) || !visualGroups.Remove(name)) return false;

            ForgetGroupSource(name);
            ClearResolveCache();
            return true;
        }

        /// <summary>The patterns of a pose family, null when no such family is declared.</summary>
        public static string[] GroupPatterns(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            visualGroups.TryGetValue(name, out string[] patterns);
            return patterns;
        }

        /// <summary>
        /// Puts one item into a pose family: drops the exclusion that was keeping it out, and adds
        /// <paramref name="pattern"/> (the item's code, or a wildcard for its whole family) when
        /// nothing in the family catches the item yet. Returns the new patterns, or null when there
        /// was nothing to change — the family is not one of ours, a wildcard exclusion stands in the
        /// way (handed back in <paramref name="blockedBy"/>), or a pattern already covers the item
        /// (handed back in <paramref name="coveredBy"/>, which is what a group whose patterns are
        /// too broad looks like from here). Only the patterns are worked out; declaring them is
        /// <see cref="DefineGroup"/>, so the caller can write the file alongside.
        /// </summary>
        public static string[] AddToGroup(string name, AssetLocation code, string pattern,
            out string blockedBy, out string coveredBy)
        {
            blockedBy = null;
            coveredBy = null;
            string[] patterns = GroupPatterns(name);
            if (patterns == null || code == null || string.IsNullOrEmpty(pattern)) return null;

            var kept = new List<string>(patterns.Length + 1);
            bool changed = false;

            foreach (string existing in patterns)
            {
                if (string.IsNullOrEmpty(existing)) continue;
                if (existing[0] != '!' || !PatternMatches(existing.Substring(1), code))
                {
                    kept.Add(existing);
                    continue;
                }

                // The exclusion this is here to undo is the one aimed at this item alone. A wildcard
                // exclusion speaks for a whole family, so dropping it would quietly pull back in
                // items nobody asked about - the caller is told about it instead.
                if (existing.IndexOf('*') < 0)
                {
                    changed = true;
                    continue;
                }
                blockedBy = existing;
                kept.Add(existing);
            }

            if (blockedBy != null) return null;

            foreach (string existing in kept)
            {
                if (existing[0] == '!' || !PatternMatches(existing, code)) continue;

                // Already spoken for. Worth saying which pattern does it: a family declared with the
                // wildcard the menu suggests speaks for far more than the items meant, and this is
                // the only place that shows it.
                coveredBy = existing;
                break;
            }
            if (coveredBy == null)
            {
                kept.Add(pattern);
                changed = true;
            }

            return changed ? kept.ToArray() : null;
        }

        /// <summary>
        /// A pattern covering the family an item obviously belongs to: everything up to its last
        /// dash, which is where the material usually sits ("pickaxe-copper" gives "pickaxe-*").
        /// </summary>
        public static string SuggestPattern(AssetLocation code)
        {
            if (code == null) return null;

            string path = code.Path;
            int dash = path.LastIndexOf('-');
            return dash <= 0 ? path : path.Substring(0, dash) + "-*";
        }

        // Resolution walks every rule and can run for any item on screen, so the answer is kept.
        // Values may be null — "this item has no pose" is worth caching too. Dropped whenever the
        // rules or the item groups behind them change (ClearResolveCache).
        private static readonly ConcurrentDictionary<string, ModelTransform> poseCache
            = new ConcurrentDictionary<string, ModelTransform>();

        /// <inheritdoc cref="render.CANGemDebug.TargetRedirect"/>
        public static string DebugTargetRedirect
        {
            get => render.CANGemDebug.TargetRedirect;
            set => render.CANGemDebug.TargetRedirect = value;
        }

        /// <summary>Number of loaded rules. 0 means nothing will ever render a gem.</summary>
        public static int RuleCount => rules.Count;

        /// <summary>The middle of a tesselated model, where the far side of a gem is mirrored around.</summary>
        public static readonly Vec3f DefaultFlipCenter = new Vec3f(0.5f, 0f, 0.5f);

        /// <summary>
        /// The first config item group the item belongs to, null when it belongs to none. Only for
        /// places that genuinely want one name (a suggestion in the menu); anything that looks a
        /// subject up has to try every family the item is in — see <see cref="FindGroups"/>.
        /// </summary>
        public static string FindGroup(AssetLocation code)
        {
            List<string> groups = FindGroups(code);
            return groups.Count == 0 ? null : groups[0];
        }

        // Which families an item belongs to is a wildcard match of every pattern of every group, and
        // it is asked for on every resolve of every item on screen. It only changes when the groups
        // themselves do, which is what ClearResolveCache covers.
        private static readonly ConcurrentDictionary<string, List<string>> groupCache
            = new ConcurrentDictionary<string, List<string>>();

        private static readonly List<string> noGroups = new List<string>();

        /// <summary>Every family an item belongs to, own groups before the config's.</summary>
        public static List<string> FindGroups(AssetLocation code)
        {
            if (code == null) return noGroups;

            string key = code.ToShortString();
            if (groupCache.TryGetValue(key, out List<string> cached)) return cached;

            var found = new List<string>();

            // Own groups first: an item covered by one of those was put there deliberately, for the
            // sake of where its gem sits.
            foreach (string name in visualGroups.Keys)
            {
                if (ItemIsInGroup(code, name)) found.Add(name);
            }
            var groups = canjewelry.config?.item_groups;
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (!found.Contains(group.Key) && ItemIsInGroup(code, group.Key)) found.Add(group.Key);
                }
            }

            groupCache[key] = found;
            return found;
        }

        /// <summary>
        /// Drops the resolved poses. Has to run whenever the rules or the item groups they lean on
        /// change — on the client the config arrives from the server after the assets are loaded,
        /// and a stale answer would keep placing gems by the previous groups.
        /// </summary>
        public static void ClearResolveCache()
        {
            poseCache.Clear();
            groupCache.Clear();
        }

        /// <summary>
        /// The pose for one socket of one item, or null when no rule covers it (the caller then
        /// renders no gem). The returned transform is shared — clone it before changing anything.
        /// </summary>
        public static ModelTransform ResolvePose(ItemStack stack, int socketIndex, EnumItemRenderTarget target,
            int side = CANGemVisualSide.Front)
        {
            return ResolvePose(stack, socketIndex, CANGemVisualTarget.FromRenderTarget(target), side);
        }

        /// <inheritdoc cref="ResolvePose(ItemStack, int, EnumItemRenderTarget, int)"/>
        public static ModelTransform ResolvePose(ItemStack stack, int socketIndex, string target,
            int side = CANGemVisualSide.Front)
        {
            if (stack?.Collectible?.Code == null) return null;

            // The preview of the debug menu goes through the GUI path whatever target is being
            // tuned, so it says here which target it actually means.
            if (DebugTargetRedirect != null && target == CANGemVisualTarget.Gui) target = DebugTargetRedirect;

            // The far side switch of the debug menu. Answered before anything is cached: it is
            // tuning state, and the cache would hand out yesterday's answer for the rest of the session.
            if (side == CANGemVisualSide.Back && flipOverrides.Count > 0)
            {
                bool? tunedFlip = FindFlipOverride(stack.Collectible.Code, target);
                if (tunedFlip == false) return null;
                if (tunedFlip == true)
                {
                    ModelTransform tunedBack = overrides.Count == 0
                        ? null
                        : FindOverride(stack.Collectible.Code, socketIndex, target, side, CutOf(stack, socketIndex), SizeOf(stack, socketIndex));
                    if (tunedBack != null) return tunedBack;

                    ModelTransform near = ResolvePose(stack, socketIndex, target, CANGemVisualSide.Front);
                    return near == null ? null : FlipPose(near, DefaultFlipCenter);
                }
            }

            if (overrides.Count > 0)
            {
                ModelTransform tuned = FindOverride(stack.Collectible.Code, socketIndex, target, side, CutOf(stack, socketIndex), SizeOf(stack, socketIndex));
                if (tuned != null) return tuned;
            }
            if (rules.Count == 0) return null;

            // The cut is part of the key: rules may place a baguette differently from a round gem,
            // and a cache that ignored it would hand out the first answer for every cut after that.
            string key = stack.Collectible.Code.ToShortString() + "|" + target + "|" + socketIndex
                         + "|" + CANGemVisualSide.Name(side)
                         + "|" + CutOf(stack, socketIndex) + "|" + SizeOf(stack, socketIndex);
            if (poseCache.TryGetValue(key, out var cached)) return cached;

            ModelTransform pose = Resolve(stack, socketIndex, target, side, null);
            poseCache[key] = pose;
            return pose;
        }

        /// <summary>
        /// The far side pose of a near side one: the gem turned half around the model, so it sits
        /// on the opposite face still looking outwards. A plain mirror would do the same to the
        /// position but turn the shape inside out, and the gem would be lit from within.
        ///
        /// <para>Exact as long as the pose has no roll (rotation z): the half turn folds into the
        /// yaw only when nothing is applied after it.</para>
        /// </summary>
        public static ModelTransform FlipPose(ModelTransform front, Vec3f center)
        {
            Vec3f around = center ?? DefaultFlipCenter;

            ModelTransform back = front.Clone();
            back.Translation.X = 2f * around.X - front.Translation.X - 2f * front.Origin.X;
            back.Translation.Z = 2f * around.Z - front.Translation.Z - 2f * front.Origin.Z;
            back.Rotation.X = -front.Rotation.X;
            back.Rotation.Y = front.Rotation.Y + 180f;
            back.Rotation.Z = -front.Rotation.Z;
            return back;
        }

        /// <summary>
        /// A subject narrowed to one kind of gem: "game:pickaxe-copper@baguette#3". Poses are tuned
        /// and exported per subject, so this is what keeps one cut or size apart from the rest.
        /// Passing a null subject returns the suffix alone.
        /// </summary>
        public static string WithVariant(string subject, string cut, int? size)
        {
            string suffix = (string.IsNullOrEmpty(cut) ? "" : "@" + cut) + (size == null ? "" : "#" + size.Value);
            return subject + suffix;
        }

        /// <summary>Splits a subject made by <see cref="WithVariant"/> back into its parts.</summary>
        public static string SplitVariant(string subject, out string cut, out int? size)
        {
            cut = null;
            size = null;
            if (subject == null) return null;

            int hash = subject.LastIndexOf('#');
            if (hash >= 0 && int.TryParse(subject.Substring(hash + 1), out int parsed))
            {
                size = parsed;
                subject = subject.Substring(0, hash);
            }

            int at = subject.LastIndexOf('@');
            if (at < 0) return subject;

            cut = subject.Substring(at + 1);
            return subject.Substring(0, at);
        }

        /// <summary>
        /// The gear shape element a gem of this item hangs off when the item is worn, or null when
        /// no rule names one — the caller then picks the first element of the gear itself.
        /// </summary>
        public static string ResolveAttachElement(ItemStack stack, int socketIndex,
            int side = CANGemVisualSide.Front)
        {
            if (stack?.Collectible?.Code == null) return null;

            string tuned = FindTuned(stack.Collectible.Code, subject => GetAttachOverride(subject, side));

            // The far side falls back to what the near side was given, so one pick serves both until
            // they are deliberately parted.
            if (tuned == null && side == CANGemVisualSide.Back)
            {
                tuned = FindTuned(stack.Collectible.Code,
                    subject => GetAttachOverride(subject, CANGemVisualSide.Front));
            }
            if (tuned != null) return tuned;

            PoseHit hit = ResolveHit(stack, socketIndex, CANGemVisualTarget.Body, side, null);
            return hit.Rule?.AttachElementFor(CANGemVisualTarget.Body, side);
        }

        /// <summary>
        /// Whether a rule keeps this item's gems off the model — as armour's does until its poses
        /// are tuned. Says nothing about the switch in the debug menu, which is asked first and can
        /// bring the gems back; this is what the switch shows when it has not been touched.
        /// </summary>
        public static bool HiddenByRule(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) return false;

            string cut = CutOf(stack, 0);
            int size = SizeOf(stack, 0);
            int bestTier = -1;
            bool hidden = false;
            foreach (var rule in rules)
            {
                int tier = MatchTier(rule, stack, cut, size);
                if (tier < bestTier) continue;
                // Same tier: the hiding rule is the one that would answer, it wins outright there.
                if (tier > bestTier) { bestTier = tier; hidden = rule.Hidden; continue; }

                hidden |= rule.Hidden;
            }
            return hidden;
        }

        /// <summary>A pose together with the rule that answered — the rule also owns the flip settings.</summary>
        private struct PoseHit
        {
            public ModelTransform Pose;
            public CANGemVisualRule Rule;
        }

        /// <summary>
        /// Shared body of <see cref="ResolvePose(ItemStack, int, string, int)"/>. When
        /// <paramref name="trace"/> is given, every step is written into it for the dump command.
        /// </summary>
        private static ModelTransform Resolve(ItemStack stack, int socketIndex, string target, int side, StringBuilder trace)
        {
            return ResolveHit(stack, socketIndex, target, side, trace).Pose;
        }

        private static PoseHit ResolveHit(ItemStack stack, int socketIndex, string target, int side, StringBuilder trace)
        {
            // Switched off - or deliberately back on - by hand this session, before any rule gets a
            // say. Switched on matters as much as off: a family shipped hidden (armour, until its
            // poses are tuned) could otherwise never be worked on in the debug menu, the rule hiding
            // it winning over the very switch meant to bring it back.
            bool? tunedHidden = hiddenOverrides.Count == 0 ? null : FindHiddenOverride(stack.Collectible.Code);
            if (tunedHidden == true)
            {
                trace?.AppendLine("switched off by hand for this item or its group");
                return default;
            }

            string cut = CutOf(stack, socketIndex);
            int size = SizeOf(stack, socketIndex);
            var candidates = new List<CANGemVisualRule>();
            var tiers = new List<int>();
            foreach (var rule in rules)
            {
                int tier = MatchTier(rule, stack, cut, size);
                if (tier < 0) continue;
                candidates.Add(rule);
                tiers.Add(tier);
            }
            if (candidates.Count == 0)
            {
                trace?.AppendLine("no rule matches this item");
                return default;
            }

            // Most specific level first, higher priority first inside a level.
            var order = new List<int>();
            for (int i = 0; i < candidates.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                if (tiers[a] != tiers[b]) return tiers[b] - tiers[a];
                // A rule written for this cut or size is the more specific answer at the same level.
                int narrowA = Narrowness(candidates[a]);
                int narrowB = Narrowness(candidates[b]);
                if (narrowA != narrowB) return narrowB - narrowA;
                return candidates[b].Priority - candidates[a].Priority;
            });

            // The rule that answers the near side owns the far side too, so the search for a far
            // side pose stops there. An exported rule leaves "socketsBack" out whenever the far side
            // is just the near side turned around, and without this the item would take its far side
            // from whatever broader rule does carry one - a gem placed by this item's own rule on
            // one face and by a family rule on the other.
            PoseHit near = side == CANGemVisualSide.Back
                ? ResolveHit(stack, socketIndex, target, CANGemVisualSide.Front, null)
                : default;
            CANGemVisualRule nearRule = near.Rule;

            // Switched off by that rule means no far side at all, poses written for it included: a
            // "socketsBack" left in a file must not bring back a gem the file says not to draw.
            if (nearRule != null && !nearRule.FlipsFor(target))
            {
                trace?.AppendLine("far side switched off by " + nearRule.Source);
                return default;
            }

            foreach (int i in order)
            {
                // A rule that hides wins outright: the point of it is to keep a family of items
                // clean while a more general rule would still have drawn on them. Unless the switch
                // in the menu was deliberately turned on for this subject, which is how a family
                // shipped hidden is picked up and tuned.
                if (candidates[i].Hidden)
                {
                    if (tunedHidden != false)
                    {
                        trace?.AppendLine(string.Format("{0} {1} (priority {2}): hidden, no gem is drawn",
                            TierName(tiers[i]), candidates[i].Source, candidates[i].Priority));
                        return default;
                    }

                    trace?.AppendLine(string.Format("{0} {1} (priority {2}): hidden, but switched on by hand",
                        TierName(tiers[i]), candidates[i].Source, candidates[i].Priority));
                }

                ModelTransform pose = candidates[i].PoseFor(socketIndex, target, side);
                // No angle brackets anywhere in this text: the chat renders it as VTML and would
                // read a stray '>' as a closing tag, dropping the line instead of printing it.
                trace?.AppendLine(string.Format("{0} {1} (priority {2}): {3}",
                    TierName(tiers[i]), candidates[i].Source, candidates[i].Priority,
                    pose == null ? "no pose for socket " + socketIndex : Describe(pose)));
                if (pose != null) return new PoseHit { Pose = pose, Rule = candidates[i] };

                // Past the rule that placed the near side: anything below it speaks less
                // specifically for this item, so its far side is not ours to take.
                if (nearRule != null && ReferenceEquals(candidates[i], nearRule)) break;
            }

            // Nothing was written for the far side of this socket, which is the normal case: it is
            // the near side pose turned around, so one tuned pose already puts the gem on both
            // faces. Writing "socketsBack" later takes over here without moving the near side.
            if (side == CANGemVisualSide.Back)
            {
                if (near.Pose == null)
                {
                    trace?.AppendLine("no near side pose to derive the far side from");
                    return default;
                }
                ModelTransform flipped = FlipPose(near.Pose, near.Rule.FlipCenterFor());
                trace?.AppendLine("far side derived from the near side pose of " + near.Rule.Source
                                  + ": " + Describe(flipped));
                return new PoseHit { Pose = flipped, Rule = near.Rule };
            }

            // Every match ran out of poses before this socket index — an item with more sockets
            // than the rule author tuned. Reuse the last pose of the best match rather than
            // dropping the gem: a gem in the wrong spot is a visible bug report, a missing one is not.
            CANGemVisualRule best = candidates[order[0]];
            ModelTransform fallback = best.LastPose(target, side);
            trace?.AppendLine("socket " + socketIndex + " beyond every rule, reusing the last pose of "
                              + best.Source + ": "
                              + (fallback == null ? "none" : Describe(fallback)));
            return fallback == null ? default : new PoseHit { Pose = fallback, Rule = best };
        }

        /// <summary>
        /// The cut of the gem in one socket, as the rules spell it, or the round cut when the socket
        /// says nothing. Poses can differ per cut, so this is part of what a rule is matched on.
        /// </summary>
        public static string CutOf(ItemStack stack, int socketIndex)
        {
            string cut = SocketOf(stack, socketIndex)?.GetString(CANJWConstants.CUTTING_TYPE);
            return string.IsNullOrEmpty(cut) ? CANJWConstants.CUTTING_ROUND : cut;
        }

        /// <summary>The size of the gem in one socket: 1 normal, 2 flawless, 3 exquisite.</summary>
        public static int SizeOf(ItemStack stack, int socketIndex)
        {
            int size = SocketOf(stack, socketIndex)?.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE) ?? 1;
            return size < 1 ? 1 : size;
        }

        private static ITreeAttribute SocketOf(ItemStack stack, int socketIndex)
        {
            return stack?.Attributes?.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING)?
                .GetTreeAttribute("slot" + socketIndex);
        }

        /// <summary>
        /// How narrowly a rule is written: a rule naming the cut and the size answers before one
        /// naming only the cut, which in turn answers before one written for every gem.
        /// </summary>
        private static int Narrowness(CANGemVisualRule rule)
        {
            return (rule.Cut != null ? 1 : 0) + (rule.Size != null ? 1 : 0);
        }

        /// <summary>How specifically the rule speaks for this item, -1 when it does not.</summary>
        private static int MatchTier(CANGemVisualRule rule, ItemStack stack, string cut, int size)
        {
            // A rule written for one cut or size has nothing to say about the others.
            if (rule.Cut != null && !rule.Cut.Equals(cut, StringComparison.InvariantCultureIgnoreCase)) return -1;
            if (rule.Size != null && rule.Size.Value != size) return -1;

            AssetLocation code = stack.Collectible.Code;
            int best = -1;

            if (rule.Match != null)
            {
                foreach (string pattern in rule.Match)
                {
                    if (string.IsNullOrEmpty(pattern)) continue;
                    string subject = pattern.Contains(':') ? code.ToShortString() : code.Path;
                    if (!WildcardUtil.Match(pattern, subject)) continue;
                    int tier = pattern.Contains('*') ? TierWildcard : TierExact;
                    if (tier > best) best = tier;
                }
            }
            if (best >= TierWildcard) return best;

            if (rule.Group != null && ItemIsInGroup(code, rule.Group) && TierGroup > best) best = TierGroup;
            if (rule.IsGlobalDefault && TierDefault > best) best = TierDefault;

            return best;
        }

        /// <summary>
        /// Group membership, the same fragment match the socket config uses: a group holds code
        /// fragments ("pickaxe", "chain"), not full codes.
        /// </summary>
        private static bool ItemIsInGroup(AssetLocation code, string groupName)
        {
            // A group declared for poses wins the name: it was written for this job, while the
            // config's groups answer to the socket counts as well.
            if (visualGroups.TryGetValue(groupName, out string[] patterns))
            {
                // A pattern starting with "!" throws items back out again, which is how one item
                // leaves a family without the wildcard being written around it:
                // "pickaxe-*, !pickaxe-gold". An exclusion always wins.
                bool included = false;
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrEmpty(pattern)) continue;

                    bool exclude = pattern[0] == '!';
                    string match = exclude ? pattern.Substring(1) : pattern;
                    if (!PatternMatches(match, code)) continue;
                    if (exclude) return false;

                    included = true;
                }
                return included;
            }

            var groups = canjewelry.config?.item_groups;
            if (groups == null || !groups.TryGetValue(groupName, out var members) || members == null) return false;

            foreach (string fragment in members)
            {
                if (string.IsNullOrEmpty(fragment)) continue;
                if (WildcardUtil.Match("*" + fragment + "*", code.Path)) return true;
            }
            return false;
        }

        /// <summary>
        /// One pattern of a pose family against one item code, the "!" already taken off. A pattern
        /// that names a domain is matched against the full code, one that does not against the path
        /// alone, so "pickaxe-*" works without anyone writing the domain out.
        /// </summary>
        private static bool PatternMatches(string pattern, AssetLocation code)
        {
            if (string.IsNullOrEmpty(pattern) || code == null) return false;

            string subject = pattern.Contains(":") ? code.ToShortString() : code.Path;
            return WildcardUtil.Match(pattern, subject);
        }

        private static string TierName(int tier)
        {
            switch (tier)
            {
                case TierExact: return "exact";
                case TierWildcard: return "wildcard";
                case TierGroup: return "group";
                default: return "default";
            }
        }

        private static string Describe(ModelTransform tf)
        {
            return string.Format("translation ({0}, {1}, {2}) rotation ({3}, {4}, {5}) origin ({6}, {7}, {8}) scale ({9}, {10}, {11})",
                tf.Translation.X, tf.Translation.Y, tf.Translation.Z,
                tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z,
                tf.Origin.X, tf.Origin.Y, tf.Origin.Z,
                tf.ScaleXYZ.X, tf.ScaleXYZ.Y, tf.ScaleXYZ.Z);
        }

        /// <summary>
        /// Human readable account of how the pose for this socket is arrived at — which rules
        /// match, in which order, and which one finally answers. Feeds the debug command.
        /// </summary>
        public static string DumpResolve(ItemStack stack, int socketIndex, string target)
        {
            if (stack?.Collectible?.Code == null) return "no item";

            var sb = new StringBuilder();
            sb.Append(stack.Collectible.Code).Append("  socket ").Append(socketIndex)
              .Append("  target ").Append(target).AppendLine();
            sb.Append(rules.Count).AppendLine(" rule(s) loaded");

            // Both sides of the socket, since one gem is drawn twice - a gem seen by the owner and
            // by nobody else is exactly the far side going missing.
            for (int side = 0; side < CANGemVisualSide.Count; side++)
            {
                sb.Append("--- ").Append(CANGemVisualSide.Name(side)).AppendLine(" side");

                ModelTransform tuned = FindOverride(stack.Collectible.Code, socketIndex, target, side, CutOf(stack, socketIndex), SizeOf(stack, socketIndex));
                if (tuned != null)
                {
                    sb.Append("tuned in the debug menu, overrides every rule: ").AppendLine(Describe(tuned));
                }

                // The rule chain is printed even when a tuned pose wins, so it stays visible what
                // the item would fall back to once the override is exported or dropped.
                ModelTransform fromRules = Resolve(stack, socketIndex, target, side, sb);
                ModelTransform pose = tuned ?? fromRules;
                sb.Append("result: ").AppendLine(pose == null ? "no gem rendered" : Describe(pose));
            }
            return sb.ToString();
        }
    }
}
