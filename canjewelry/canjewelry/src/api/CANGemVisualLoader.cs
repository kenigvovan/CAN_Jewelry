using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canjewelry.src.api
{
    /// <summary>
    /// Where the pose rules come from: the mod's own assets, the folder a player or the debug menu
    /// writes into, and the poses an admin set on the server. All three land in the same list, in
    /// the same shape, differing only in the priority they are lifted by.
    /// </summary>
    public static partial class CANGemVisualRegistry
    {
        private const string AssetPath = "config/gemvisuals";

        /// <summary>
        /// Pose files a player may add or the debug menu exports, read on top of the assets.
        /// </summary>
        public static string UserRuleDirectory
            => Path.Combine(GamePaths.ModConfig, "canjewelry", "gemvisuals");

        /// <summary>Lifts user rules above every shipped rule that matches at the same tier.</summary>
        private const int UserRulePriorityBoost = 1000;

        /// <summary>Lifts the poses an admin set above local ones, so a server looks the same to all.</summary>
        private const int ServerRulePriorityBoost = 2000;

        private static string appliedServerHash = "";

        /// <summary>
        /// Fingerprint of the server poses in force here, empty when there are none. Sent with the
        /// config request so the server can skip poses this client already has.
        /// </summary>
        public static string AppliedServerRulesHash => appliedServerHash;

        /// <summary>
        /// Reads every <c>config/gemvisuals</c> asset of every domain. Safe to call again: the
        /// previous rules are dropped first.
        /// </summary>
        public static void LoadFromAssets(ICoreAPI api)
        {
            rules.Clear();
            visualGroups.Clear();
            groupSources.Clear();
            groupPriorities.Clear();
            serverGroups.Clear();
            ClearResolveCache();

            var assets = api.Assets.GetMany<CANGemVisualRule>(api.Logger, AssetPath);
            int poses = 0;
            foreach (var pair in assets)
            {
                if (Accept(api, pair.Value, pair.Key, 0, ref poses)) rules.Add(pair.Value);
            }

            int userRules = LoadUserRules(api, ref poses);

            api.Logger.Notification(
                "[canjewelry] gem visuals: {0} rule(s) from {1} asset file(s) and {2} user file(s), {3} pose(s)",
                rules.Count, assets.Count, userRules, poses);
        }

        /// <summary>
        /// Fills a missing pose folder with the mod's own <c>config/gemvisuals</c> files, so there is
        /// something to edit from the start. A folder that already exists is left alone, even if it
        /// is empty: that is how an admin opts out of the shipped poses.
        /// </summary>
        public static void SeedUserRulesFromAssets(ICoreAPI api)
        {
            string dir = UserRuleDirectory;
            if (Directory.Exists(dir)) return;

            int written = 0;
            try
            {
                Directory.CreateDirectory(dir);
                foreach (IAsset asset in api.Assets.GetMany(AssetPath, "canjewelry"))
                {
                    string path = Path.Combine(dir, asset.Name);
                    // In single player the client and the server may both get here at once.
                    if (File.Exists(path)) continue;

                    File.WriteAllText(path, asset.ToText());
                    written++;
                }
            }
            catch (Exception e)
            {
                api.Logger.Error("[canjewelry] could not fill {0} with the default gem poses: {1}", dir, e.Message);
                return;
            }

            api.Logger.Notification("[canjewelry] gem visuals: wrote {0} default pose file(s) to {1}", written, dir);
        }

        /// <summary>
        /// The pose files of the server's own folder, as one JSON array, or null when the admin put
        /// nothing there. Read on the server; what comes back is what gets sent to clients.
        /// </summary>
        public static string CollectUserRulesJson(ICoreAPI api)
        {
            string dir = UserRuleDirectory;
            if (!Directory.Exists(dir)) return null;

            var collected = new List<CANGemVisualRule>();
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    CANGemVisualRule rule = JsonConvert.DeserializeObject<CANGemVisualRule>(File.ReadAllText(file));
                    if (rule == null) continue;

                    // Files that only declare a family count too. They carry no pose, but a rule
                    // that speaks for a family is worthless without the wording that says which
                    // items the family holds - and that wording is usually narrower than the copy
                    // travelling inside the pose file, exclusions being written into it by hand.
                    if (rule.Sockets == null && rule.SocketsBack == null && rule.Targets == null
                        && rule.DefineGroups == null && !rule.Hidden)
                    {
                        continue;
                    }
                    collected.Add(rule);
                }
                catch (Exception e)
                {
                    api.Logger.Error("[canjewelry] could not read gemvisuals file {0}: {1}", file, e.Message);
                }
            }
            if (collected.Count == 0) return null;

            return JsonConvert.SerializeObject(collected,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>
        /// A short fingerprint of a rule payload, so a client can say what it already has and the
        /// server can skip sending the same poses again.
        /// </summary>
        public static string Fingerprint(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return "";

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Puts the poses an admin set on the server in force on this client, above every local
        /// rule — assets and the player's own files alike, so everyone on a server sees the gems
        /// in the same place. Applied in memory only: nothing is written, and the poses are gone
        /// again once the client leaves.
        ///
        /// <para>Passing empty rules takes previously received ones back off.</para>
        /// </summary>
        public static void ApplyServerRules(ICoreAPI api, string json, string hash)
        {
            rules.RemoveAll(r => r.FromServer);

            // The families the previous server declared go with its rules. Otherwise a name claimed
            // by one server would keep matching items on the next one, or in single player.
            foreach (string group in serverGroups)
            {
                visualGroups.Remove(group);
                groupSources.Remove(group);
                // With its priority as well, or the next declaration of that name would be measured
                // against a server that is no longer speaking and refused.
                groupPriorities.Remove(group);
            }
            serverGroups.Clear();

            appliedServerHash = hash ?? "";

            int poses = 0;
            int count = 0;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var received = JsonConvert.DeserializeObject<List<CANGemVisualRule>>(json);
                    var source = new AssetLocation("server", "gemvisuals");
                    foreach (CANGemVisualRule rule in received ?? new List<CANGemVisualRule>())
                    {
                        if (!Accept(api, rule, source, ServerRulePriorityBoost, ref poses, fromServer: true)) continue;

                        rule.FromServer = true;
                        rules.Add(rule);
                        count++;
                    }
                }
                catch (Exception e)
                {
                    api?.Logger?.Error("[canjewelry] could not read the gem poses sent by the server: {0}", e.Message);
                }
            }

            ClearResolveCache();
            api?.Logger?.Notification("[canjewelry] gem visuals: {0} rule(s) from the server, {1} pose(s)",
                count, poses);
        }

        /// <summary>
        /// Reads pose files a player put into <c>ModConfig/canjewelry/gemvisuals</c>, on top of the
        /// ones shipped in assets. That is where the debug menu writes when the mod runs packed,
        /// and it is how a pose survives a restart without rebuilding the mod.
        ///
        /// <para>A user rule outranks every shipped rule of the same tier, so tuning an item does
        /// not have to out-priority a file it cannot see.</para>
        /// </summary>
        private static int LoadUserRules(ICoreAPI api, ref int poses)
        {
            string dir = UserRuleDirectory;
            if (!Directory.Exists(dir)) return 0;

            int loaded = 0;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    CANGemVisualRule rule = JsonConvert.DeserializeObject<CANGemVisualRule>(File.ReadAllText(file));
                    AssetLocation source = UserRuleSource(Path.GetFileName(file));
                    if (!Accept(api, rule, source, UserRulePriorityBoost, ref poses)) continue;

                    rules.Add(rule);
                    loaded++;
                }
                catch (Exception e)
                {
                    // One bad file must not cost the player every other pose they tuned.
                    api.Logger.Error("[canjewelry] could not read gemvisuals file {0}: {1}", file, e.Message);
                }
            }
            return loaded;
        }

        /// <summary>
        /// Puts one just written pose file in force at once, in place of whatever the same file was
        /// read as before. Without this an exported rule would only answer after a restart, while
        /// the session kept running on the menu's tuning — and the two do not rank the same: tuning
        /// speaks for the subject it was made on, a rule for the tier it matches at, so an item with
        /// a rule of its own would quietly take back over on the next launch.
        ///
        /// <para>Returns whether the file now has a rule: a file that only declares a family
        /// registers the family and keeps none, which is not a failure.</para>
        /// </summary>
        public static bool ApplyUserRuleFile(ICoreAPI api, string fileName, string json)
        {
            AssetLocation source = UserRuleSource(fileName);
            RemoveRulesFrom(source);

            CANGemVisualRule rule;
            try
            {
                rule = JsonConvert.DeserializeObject<CANGemVisualRule>(json);
            }
            catch (Exception e)
            {
                api?.Logger?.Error("[canjewelry] could not read back {0}: {1}", fileName, e.Message);
                return false;
            }

            int poses = 0;
            bool kept = Accept(api, rule, source, UserRulePriorityBoost, ref poses);
            if (kept) rules.Add(rule);

            ClearResolveCache();
            return kept;
        }

        /// <summary>The source an own pose file is loaded under, by its file name.</summary>
        public static AssetLocation UserRuleSource(string fileName)
            => AssetLocation.Create("gemvisuals/" + fileName.ToLowerInvariant(), "modconfig");

        /// <summary>
        /// Takes the rules of one file back out. Deleting the file alone would not do: it was read
        /// at startup and its rules answer for the rest of the session.
        /// </summary>
        public static int RemoveRulesFrom(AssetLocation source)
        {
            if (source == null) return 0;

            int removed = rules.RemoveAll(rule => source.Equals(rule.Source));
            if (removed > 0) ClearResolveCache();
            return removed;
        }

        /// <summary>
        /// Makes a freshly read rule ready to be asked, and says whether it is worth keeping.
        /// Every source of rules — the mod's assets, the player's own folder, the server — goes
        /// through here, so they cannot drift apart in what they accept.
        ///
        /// <para>Poses are filled in with their defaults, because Translation and Rotation default
        /// to a sentinel value rather than to zero, and a file that leaves either out would
        /// otherwise offset the gem by a hair on every axis.</para>
        /// </summary>
        private static bool Accept(ICoreAPI api, CANGemVisualRule rule, AssetLocation source, int priorityBoost,
            ref int poses, bool fromServer = false)
        {
            if (rule == null) return false;

            rule.Source = source;
            // With the boost of its source: a declaration from the server outranks a local one the
            // same way its poses do, and the raw numbers alone would not say that.
            RegisterGroups(api, rule, fromServer, rule.Priority + priorityBoost);

            poses += EnsureDefaults(rule.Sockets);
            poses += EnsureDefaults(rule.SocketsBack);
            if (rule.Targets != null)
            {
                foreach (var over in rule.Targets.Values)
                {
                    poses += EnsureDefaults(over?.Sockets);
                    poses += EnsureDefaults(over?.SocketsBack);
                }
            }

            if (rule.Sockets == null && rule.SocketsBack == null && rule.Targets == null && !rule.Hidden)
            {
                // A file that only declares groups is doing its job; anything else carrying no poses
                // is a mistake worth saying out loud.
                if (rule.DefineGroups == null)
                {
                    api?.Logger?.Warning("[canjewelry] gemvisuals rule {0} declares no poses, ignored", source);
                }
                return false;
            }

            rule.Priority += priorityBoost;
            return true;
        }

        // Who declared each family, so a second file claiming the name can be told apart from the
        // same file being read again, and so the server's families can be taken back off.
        private static readonly Dictionary<string, AssetLocation> groupSources
            = new Dictionary<string, AssetLocation>(StringComparer.InvariantCultureIgnoreCase);

        private static readonly List<string> serverGroups = new List<string>();

        /// <summary>
        /// Takes the families a rule declares into the registry. The highest priority declaration
        /// wins a name, later files winning ties — the assets are walked in no guaranteed order, so
        /// picking by arrival alone would differ between launches. Two files declaring one name with
        /// different patterns is said out loud either way.
        /// </summary>
        private static void RegisterGroups(ICoreAPI api, CANGemVisualRule rule, bool fromServer, int priority)
        {
            if (rule.DefineGroups == null) return;

            foreach (var group in rule.DefineGroups)
            {
                if (string.IsNullOrEmpty(group.Key) || group.Value == null) continue;

                bool clashes = groupSources.TryGetValue(group.Key, out AssetLocation declaredIn)
                               && !declaredIn.Equals(rule.Source)
                               && visualGroups.TryGetValue(group.Key, out string[] existing)
                               && !PatternsEqual(existing, group.Value);

                // A group carries its declaration in the file of its poses as well as in one of its
                // own, so the same name legitimately arrives twice. Priority is what says which is
                // the current wording: the menu writes a redeclaration above the pose file.
                //
                // Checked whatever the source, not only across files: everything a server sends
                // arrives under one source, and going by arrival there would hand the name to
                // whichever file the folder listing happened to put last.
                if (groupPriorities.TryGetValue(group.Key, out int declaredAt) && declaredAt > priority)
                {
                    if (clashes)
                    {
                        api?.Logger?.Warning(
                            "[canjewelry] gemvisuals group '{0}' is declared in {1} and again in {2} with other patterns; {1} wins, it has the higher priority",
                            group.Key, declaredIn, rule.Source);
                    }
                    continue;
                }

                if (clashes)
                {
                    api?.Logger?.Warning(
                        "[canjewelry] gemvisuals group '{0}' is declared in {1} and again in {2} with other patterns; {2} wins",
                        group.Key, declaredIn, rule.Source);
                }

                visualGroups[group.Key] = group.Value;
                groupSources[group.Key] = rule.Source;
                groupPriorities[group.Key] = priority;
                if (fromServer && !serverGroups.Contains(group.Key)) serverGroups.Add(group.Key);
                else if (!fromServer) serverGroups.Remove(group.Key);
            }
        }

        // The priority each family was declared at, so a redeclaration can outrank the copy that
        // travels inside the group's pose file.
        private static readonly Dictionary<string, int> groupPriorities
            = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>Forgets who declared a family, so redeclaring it later is not reported as a clash.</summary>
        private static void ForgetGroupSource(string name)
        {
            groupSources.Remove(name);
            groupPriorities.Remove(name);
            serverGroups.Remove(name);
        }

        private static bool PatternsEqual(string[] left, string[] right)
        {
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.InvariantCultureIgnoreCase)) return false;
            }
            return true;
        }

        private static int EnsureDefaults(ModelTransform[] transforms)
        {
            if (transforms == null) return 0;
            int count = 0;
            foreach (var tf in transforms)
            {
                if (tf == null) continue;
                tf.EnsureDefaultValues();
                count++;
            }
            return count;
        }
    }
}
