using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace canjewelry.src.admin
{
    /// <summary>Outcome of one admin edit: whether the config changed, and what to tell the admin.</summary>
    public sealed class AdminEditResult
    {
        public bool Changed;
        public bool IsError;
        /// <summary>Lang key without the domain.</summary>
        public string Message;
        public List<string> Args = new List<string>();

        public static AdminEditResult Ok(string message, params string[] args)
            => new AdminEditResult { Changed = true, Message = message, Args = args.ToList() };

        public static AdminEditResult Error(string message, params string[] args)
            => new AdminEditResult { IsError = true, Message = message, Args = args.ToList() };
    }

    /// <summary>
    /// The edits the admin dialogue can make to the socket and gem rules, and the list of defaults
    /// a config is missing. Works on a Config and nothing else - no API, no network - so the server
    /// handler stays a few lines and all of this can be tested without a running game.
    /// <para>
    /// Rules are disabled rather than deleted. A deleted key leaves the old attributes on items that
    /// are already loaded, and a rule some mod registers comes straight back from the registry,
    /// while an empty one is something the core already knows to apply: no sockets.
    /// </para>
    /// </summary>
    public static class AdminConfigService
    {
        public const int MaxSockets = 10;
        public const int MaxTier = 10;
        private const int MaxCodeLength = 200;

        public static AdminEditResult Apply(Config cfg, AdminConfigEditPacket p)
        {
            if (cfg == null || p == null) return AdminEditResult.Error("adminconfig-msg-bad-request");

            switch (p.Op)
            {
                case AdminConfigOp.SetSocketRule: return SetSocketRule(cfg, p.Code, p.TiersOrEmpty);
                case AdminConfigOp.DisableSocketRule: return DisableSocketRule(cfg, p.Code);
                case AdminConfigOp.SetVariantValue: return SetVariantValue(cfg, p.Code, p.AttributeKey, p.VariantValue, p.TiersOrEmpty);
                case AdminConfigOp.RemoveVariantValue: return RemoveVariantValue(cfg, p.Code, p.VariantValue);
                case AdminConfigOp.DisableVariantRule: return DisableVariantRule(cfg, p.Code);
                case AdminConfigOp.SetGemForItem: return SetGemForItem(cfg, p.GemType, p.Code, p.Allowed);
                case AdminConfigOp.AddMissing: return AddMissing(cfg, p.Ids ?? new List<string>());
                case AdminConfigOp.AddAllMissing: return AddMissing(cfg, null);
                default: return AdminEditResult.Error("adminconfig-msg-bad-request");
            }
        }

        // ---------------------------------------------------------------- sockets

        private static AdminEditResult SetSocketRule(Config cfg, string code, int[] tiers)
        {
            if (!ValidCode(code)) return AdminEditResult.Error("adminconfig-msg-bad-code");
            // An empty list means "no sockets", which has its own button - here it is a typo.
            if (tiers.Length == 0) return AdminEditResult.Error("adminconfig-msg-no-tiers");

            code = code.Trim();
            cfg.items_codes_with_socket_count_and_tiers[code] = SanitiseTiers(tiers);
            return AdminEditResult.Ok("adminconfig-msg-saved", code);
        }

        private static AdminEditResult DisableSocketRule(Config cfg, string code)
        {
            if (!ValidCode(code)) return AdminEditResult.Error("adminconfig-msg-bad-code");

            code = code.Trim();
            cfg.items_codes_with_socket_count_and_tiers[code] = new int[0];
            return AdminEditResult.Ok("adminconfig-msg-disabled", code);
        }

        private static AdminEditResult SetVariantValue(Config cfg, string code, string key, string value, int[] tiers)
        {
            if (!ValidCode(code)) return AdminEditResult.Error("adminconfig-msg-bad-code");
            if (string.IsNullOrWhiteSpace(value)) return AdminEditResult.Error("adminconfig-msg-no-value");

            code = code.Trim();
            value = value.Trim();
            key = key?.Trim();

            var rule = FindVariantRule(cfg, code);
            if (rule == null)
            {
                if (string.IsNullOrEmpty(key)) return AdminEditResult.Error("adminconfig-msg-no-key");
                rule = new Config.CustomVariantSocketsTiers(code, key, new Dictionary<string, int[]>());
                cfg.custom_variants_sockets_tiers.Add(rule);
            }
            else if (!string.IsNullOrEmpty(key) && key != rule.AttributeKey)
            {
                // The other values of the rule were written against the old key and would all
                // silently mean something else; changing the key is a new rule, not an edit.
                return AdminEditResult.Error("adminconfig-msg-key-mismatch", rule.AttributeKey);
            }

            rule.SocketTiers ??= new Dictionary<string, int[]>();
            // An empty list is allowed here: that one value gives no sockets, the rest still do.
            rule.SocketTiers[value] = SanitiseTiers(tiers);
            return AdminEditResult.Ok("adminconfig-msg-saved", code + " · " + value);
        }

        private static AdminEditResult RemoveVariantValue(Config cfg, string code, string value)
        {
            var rule = FindVariantRule(cfg, code?.Trim());
            if (rule?.SocketTiers == null || value == null || !rule.SocketTiers.Remove(value.Trim()))
            {
                return AdminEditResult.Error("adminconfig-msg-not-found");
            }
            return AdminEditResult.Ok("adminconfig-msg-removed", rule.ItemCode + " · " + value.Trim());
        }

        private static AdminEditResult DisableVariantRule(Config cfg, string code)
        {
            var rule = FindVariantRule(cfg, code?.Trim());
            if (rule == null) return AdminEditResult.Error("adminconfig-msg-not-found");

            rule.SocketTiers = new Dictionary<string, int[]>();
            return AdminEditResult.Ok("adminconfig-msg-disabled", rule.ItemCode);
        }

        public static Config.CustomVariantSocketsTiers FindVariantRule(Config cfg, string code)
            => code == null ? null : cfg.custom_variants_sockets_tiers.FirstOrDefault(r => r?.ItemCode == code);

        /// <summary>At most <see cref="MaxSockets"/> sockets, each tier in 1..<see cref="MaxTier"/>.</summary>
        public static int[] SanitiseTiers(int[] tiers)
        {
            if (tiers == null) return new int[0];
            return tiers.Take(MaxSockets).Select(t => Math.Clamp(t, 1, MaxTier)).ToArray();
        }

        private static bool ValidCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            code = code.Trim();
            return code.Length <= MaxCodeLength && !code.Any(char.IsWhiteSpace);
        }

        // ---------------------------------------------------------------- gems

        /// <summary>
        /// The fragments of a gem's list that make it fit an item - the same test
        /// EncrustableCB.canItemContainThisGem runs on the item's code path.
        /// </summary>
        public static List<string> MatchingFragments(IEnumerable<string> fragments, string itemPath)
        {
            var result = new List<string>();
            if (fragments == null || string.IsNullOrEmpty(itemPath)) return result;

            foreach (string fragment in fragments)
            {
                if (string.IsNullOrEmpty(fragment)) continue;
                if (WildcardUtil.Match("*" + fragment + "*", itemPath)) result.Add(fragment);
            }
            return result;
        }

        /// <summary>
        /// What is added to a gem's list to let it into an item: the first part of the code,
        /// "canring" for canring-right-simple. The same short form the default lists use.
        /// </summary>
        public static string FragmentFor(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath)) return itemPath;
            int dash = itemPath.IndexOf('-');
            return dash > 0 ? itemPath.Substring(0, dash) : itemPath;
        }

        private static AdminEditResult SetGemForItem(Config cfg, string gemType, string itemCode, bool allowed)
        {
            if (string.IsNullOrEmpty(gemType) || !cfg.buffNameToPossibleItem.TryGetValue(gemType, out var fragments) || fragments == null)
            {
                return AdminEditResult.Error("adminconfig-msg-unknown-gem", gemType ?? "");
            }
            if (!ValidCode(itemCode)) return AdminEditResult.Error("adminconfig-msg-bad-code");

            string path = new AssetLocation(itemCode.Trim()).Path;
            var matching = MatchingFragments(fragments, path);

            if (allowed)
            {
                if (matching.Count > 0) return new AdminEditResult();
                string fragment = FragmentFor(path);
                fragments.Add(fragment);
                return AdminEditResult.Ok("adminconfig-msg-gem-added", gemType, fragment);
            }

            // The item may still fit through its own JSON (canAllowedGems / canGemGroups), which
            // the config cannot take away.
            if (matching.Count == 0) return AdminEditResult.Error("adminconfig-msg-gem-not-in-config", gemType);

            // The lists are held expanded in memory, so a fragment that came from "$jewelry" is
            // removed like any other; the save then writes the gem's list out without the group.
            foreach (string fragment in matching) fragments.Remove(fragment);
            return AdminEditResult.Ok("adminconfig-msg-gem-removed", gemType, string.Join(", ", matching));
        }

        // ---------------------------------------------------------------- missing defaults

        public const string KindSocket = "socket";
        public const string KindVariant = "variant";
        public const string KindVariantValue = "variantvalue";
        public const string KindGem = "gem";

        /// <summary>
        /// Every default the config lacks: socket rules, variant rules or values of them, and item
        /// fragments that gems take by default. A config is never topped up on its own, so this is
        /// how something like a new ring design or a whole new item reaches an existing server.
        /// Fragment lists are compared expanded, so call it on a config that has been through
        /// ExpandItemGroups.
        /// </summary>
        public static List<AdminMissingEntry> CollectMissingDefaults(Config cfg)
        {
            var result = new List<AdminMissingEntry>();
            Walk(cfg, entry => { result.Add(entry); return false; });
            return result.OrderBy(e => e.Kind).ThenBy(e => e.Subject, StringComparer.Ordinal).ToList();
        }

        /// <summary>Adds the listed defaults, or all of them when <paramref name="ids"/> is null.</summary>
        private static AdminEditResult AddMissing(Config cfg, List<string> ids)
        {
            HashSet<string> wanted = ids == null ? null : new HashSet<string>(ids);
            int added = 0;
            Walk(cfg, entry =>
            {
                if (wanted != null && !wanted.Contains(entry.Id)) return false;
                added++;
                return true;
            });

            if (added == 0) return AdminEditResult.Error("adminconfig-msg-nothing-added");
            return AdminEditResult.Ok("adminconfig-msg-added", added.ToString());
        }

        /// <summary>
        /// One pass over the defaults. <paramref name="visit"/> sees each missing entry and returns
        /// whether to add it - collecting and adding are the same walk, so they cannot disagree on
        /// what counts as missing.
        /// </summary>
        private static void Walk(Config cfg, System.Func<AdminMissingEntry, bool> visit)
        {
            foreach (var entry in Config.BuildDefaultSocketCounts())
            {
                if (cfg.items_codes_with_socket_count_and_tiers.ContainsKey(entry.Key)) continue;

                var missing = new AdminMissingEntry
                {
                    Id = KindSocket + ":" + entry.Key, Kind = KindSocket, Subject = entry.Key, Detail = Tiers(entry.Value)
                };
                if (visit(missing)) cfg.items_codes_with_socket_count_and_tiers[entry.Key] = (int[])entry.Value.Clone();
            }

            foreach (var def in Config.BuildDefaultCustomVariantSockets())
            {
                if (def?.ItemCode == null || def.SocketTiers == null) continue;

                var existing = FindVariantRule(cfg, def.ItemCode);
                if (existing?.SocketTiers == null)
                {
                    var missing = new AdminMissingEntry
                    {
                        Id = KindVariant + ":" + def.ItemCode, Kind = KindVariant, Subject = def.ItemCode,
                        Detail = def.AttributeKey + ": " + string.Join("; ", def.SocketTiers.Select(t => t.Key + " " + Tiers(t.Value)))
                    };
                    if (!visit(missing)) continue;

                    // Copies: the registry's own object must never end up being edited through the config.
                    var copy = def.SocketTiers.ToDictionary(t => t.Key, t => (int[])t.Value.Clone());
                    if (existing == null) cfg.custom_variants_sockets_tiers.Add(new Config.CustomVariantSocketsTiers(def.ItemCode, def.AttributeKey, copy));
                    else existing.SocketTiers = copy;
                    continue;
                }

                foreach (var tier in def.SocketTiers)
                {
                    if (existing.SocketTiers.ContainsKey(tier.Key)) continue;

                    var missing = new AdminMissingEntry
                    {
                        Id = KindVariantValue + ":" + def.ItemCode + "|" + tier.Key, Kind = KindVariantValue,
                        Subject = def.ItemCode + " · " + tier.Key, Detail = Tiers(tier.Value)
                    };
                    if (visit(missing)) existing.SocketTiers[tier.Key] = (int[])tier.Value.Clone();
                }
            }

            // Grouped by fragment rather than listed per gem: "canring is missing from 30 gems" is
            // one decision, thirty rows would be thirty clicks for the same one.
            var gemsByFragment = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var def in Config.BuildDefaultBuffItemSets())
            {
                // A gem the admin removed altogether is left alone: that is a decision, not a gap.
                if (!cfg.buffNameToPossibleItem.TryGetValue(def.Key, out var current) || current == null) continue;

                foreach (string fragment in def.Value)
                {
                    if (current.Contains(fragment)) continue;
                    if (!gemsByFragment.TryGetValue(fragment, out var gems)) gemsByFragment[fragment] = gems = new List<string>();
                    gems.Add(def.Key);
                }
            }
            foreach (var pair in gemsByFragment)
            {
                var missing = new AdminMissingEntry
                {
                    Id = KindGem + ":" + pair.Key, Kind = KindGem, Subject = pair.Key, Detail = string.Join(", ", pair.Value)
                };
                if (!visit(missing)) continue;
                foreach (string gem in pair.Value) cfg.buffNameToPossibleItem[gem].Add(pair.Key);
            }
        }

        public static string Tiers(int[] tiers) => tiers == null || tiers.Length == 0 ? "[]" : string.Join(",", tiers);
    }
}
