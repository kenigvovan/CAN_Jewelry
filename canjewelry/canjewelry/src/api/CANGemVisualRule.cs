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
    /// The render targets a gem pose can be tuned for. Kept as strings rather than
    /// <see cref="EnumItemRenderTarget"/> because the gem also has to be placed on worn armour,
    /// which is not an item render target at all.
    /// </summary>
    public static class CANGemVisualTarget
    {
        public const string Gui = "gui";
        public const string Hand = "hand";
        public const string Ground = "ground";
        /// <summary>The item worn on an entity — a different shape than the item shape.</summary>
        public const string Body = "body";

        /// <summary>
        /// First and third person hands share one pose: the gem sits on the item, and the item's
        /// own transforms already differ per target.
        /// </summary>
        public static string FromRenderTarget(EnumItemRenderTarget target)
        {
            switch (target)
            {
                case EnumItemRenderTarget.Gui: return Gui;
                case EnumItemRenderTarget.Ground: return Ground;
                default: return Hand;
            }
        }
    }

    /// <summary>
    /// The two places one socketed gem is drawn at. A tool model is flat: a gem laid on the near
    /// face is invisible from the other side, so the owner sees it and nobody else does, or the
    /// other way round. So every gem gets a second placement on the far face.
    ///
    /// <para>The far side is derived from the near one by default (see
    /// <see cref="CANGemVisualRegistry.FlipPose"/>), and can later be written out per socket
    /// (<c>socketsBack</c>) to sit somewhere slightly different — it stays the same one gem in the
    /// same socket either way.</para>
    /// </summary>
    public static class CANGemVisualSide
    {
        public const int Front = 0;
        public const int Back = 1;
        public const int Count = 2;

        public static string Name(int side) => side == Back ? "back" : "front";

        public static int FromName(string name) => name == "back" ? Back : Front;
    }

    /// <summary>Per-target override of the poses declared by a rule.</summary>
    public class CANGemVisualTargetPoses
    {
        [JsonProperty]
        public ModelTransform[] Sockets;

        /// <summary>Far side poses of this target, see <see cref="CANGemVisualRule.SocketsBack"/>.</summary>
        [JsonProperty("socketsBack")]
        public ModelTransform[] SocketsBack;

        /// <summary>Overrides <see cref="CANGemVisualRule.Flip"/> for this target alone.</summary>
        [JsonProperty("flip")]
        public bool? Flip;

        /// <summary>Overrides <see cref="CANGemVisualRule.AttachElement"/> for this target alone.</summary>
        [JsonProperty("attachElement")]
        public string AttachElement;

        /// <summary>Overrides <see cref="CANGemVisualRule.AttachElementBack"/> for this target alone.</summary>
        [JsonProperty("attachElementBack")]
        public string AttachElementBack;
    }

    /// <summary>
    /// One gemvisuals JSON file: where a gem sits on the items it matches.
    /// A rule matches by item code (exact or wildcard), by config item group, or is the global
    /// default; see <see cref="CANGemVisualRegistry"/> for how those rank against each other.
    /// </summary>
    public class CANGemVisualRule
    {
        /// <summary>
        /// Item codes this rule applies to. A pattern carrying a domain ("canjewelry:canring-*")
        /// is matched against the full code, one without it against the code path alone — the
        /// same shorthand the socket config uses.
        /// </summary>
        [JsonProperty]
        public string[] Match;

        /// <summary>
        /// A family of items this rule speaks for: either one declared by <see cref="DefineGroups"/>
        /// in any gemvisuals file, or a key of <c>Config.item_groups</c> (armor / melee / mining /
        /// ranged / shield). Own groups are looked at first.
        /// </summary>
        [JsonProperty]
        public string Group;

        /// <summary>
        /// Families of items declared for gem poses alone, as code patterns: <c>{"myaxes": ["axe-*",
        /// "*-hatchet"]}</c>. Any rule can then say <c>"group": "myaxes"</c>.
        ///
        /// <para>The item groups of the config are a server setting — they decide how many gems fit
        /// into a piece — so they cannot be shaped around what looks good. These are the client's
        /// own, live in the same files as the poses, and are free to cut the items any way the
        /// placement needs.</para>
        /// </summary>
        [JsonProperty("defineGroups")]
        public Dictionary<string, string[]> DefineGroups;

        /// <summary>Breaks ties between rules that match at the same level. Higher wins.</summary>
        [JsonProperty]
        public int Priority;

        /// <summary>
        /// Limits the rule to one cut of gem ("round", "baguette", "pear"). Left out, the rule
        /// speaks for every cut. A cut rule beats a general one of the same specificity, so the odd
        /// shape that needs its own placement gets a file of its own without disturbing the rest.
        /// </summary>
        [JsonProperty("cut")]
        public string Cut;

        /// <summary>
        /// Limits the rule to one size of gem: 1 normal, 2 flawless, 3 exquisite. Left out, the rule
        /// speaks for every size. Sizes are different shapes, so a big stone sometimes has to sit a
        /// little differently than a small one in the same socket.
        /// </summary>
        [JsonProperty("size")]
        public int? Size;

        /// <summary>Last resort for items no other rule speaks for.</summary>
        [JsonProperty("default")]
        public bool IsGlobalDefault;

        /// <summary>
        /// Draws no gem on the items this rule matches, and stops the search there — the rules it
        /// outranks are not consulted. This is how a family is left alone while the work is done on
        /// another: a rule of <c>{"group": "armor", "hidden": true, "priority": 50}</c> keeps gems
        /// off every piece of armour while tools keep theirs.
        /// </summary>
        [JsonProperty("hidden")]
        public bool Hidden;

        /// <summary>Pose per socket index. Shorter than the item's socket count is allowed —
        /// the missing indices fall through to the next matching rule.</summary>
        [JsonProperty]
        public ModelTransform[] Sockets;

        /// <summary>
        /// Where the far side gem of each socket sits. Optional: left out, the far side is the near
        /// side pose mirrored across the model. Written out only for models where the two sides are
        /// not symmetric and the gem has to sit somewhere slightly different on the back.
        /// </summary>
        [JsonProperty("socketsBack")]
        public ModelTransform[] SocketsBack;

        /// <summary>
        /// Whether a gem is drawn on the far side of the model at all. On for flat item shapes,
        /// which is why it defaults to on; off for models that are seen from every angle anyway
        /// (worn armour), where a second gem would just show up on the wearer's back.
        /// </summary>
        [JsonProperty("flip")]
        public bool? Flip;

        /// <summary>
        /// The vertical axis the far side is mirrored around, in model space. Defaults to the
        /// middle of the model, which is where a tesselated item shape sits.
        /// </summary>
        [JsonProperty("flipCenter")]
        public Vec3f FlipCenter;

        /// <summary>
        /// The shape element a gem on worn gear hangs off, by its name in the gear's own shape
        /// (without the texture prefix the game puts in front of it). Left out, the gem hangs off
        /// the first element of the gear, which is usually the piece itself.
        ///
        /// <para>Only the "body" target uses this: in hand and in the gui the gem sits on the item
        /// mesh and needs no bone to follow.</para>
        /// </summary>
        [JsonProperty("attachElement")]
        public string AttachElement;

        /// <summary>
        /// The element the far side gem hangs off, when it is not the same one as the near side —
        /// the two sides of a piece often sit on different halves of the model.
        /// </summary>
        [JsonProperty("attachElementBack")]
        public string AttachElementBack;

        /// <summary>Poses that differ for one render target, keyed by <see cref="CANGemVisualTarget"/>.</summary>
        [JsonProperty]
        public Dictionary<string, CANGemVisualTargetPoses> Targets;

        /// <summary>The asset this rule came from. Diagnostics only.</summary>
        [JsonIgnore]
        public AssetLocation Source;

        /// <summary>Whether the rule arrived from the server, and is dropped when new poses do.</summary>
        [JsonIgnore]
        public bool FromServer;

        private CANGemVisualTargetPoses Override(string target)
        {
            if (Targets == null || target == null) return null;
            Targets.TryGetValue(target, out var over);
            return over;
        }

        private static ModelTransform[] SocketsOf(ModelTransform[] front, ModelTransform[] back, int side)
            => side == CANGemVisualSide.Back ? back : front;

        internal ModelTransform PoseFor(int socketIndex, string target, int side)
        {
            CANGemVisualTargetPoses over = Override(target);
            ModelTransform[] tuned = over == null ? null : SocketsOf(over.Sockets, over.SocketsBack, side);
            if (tuned != null && socketIndex >= 0 && socketIndex < tuned.Length) return tuned[socketIndex];

            ModelTransform[] own = SocketsOf(Sockets, SocketsBack, side);
            if (own != null && socketIndex >= 0 && socketIndex < own.Length) return own[socketIndex];
            return null;
        }

        /// <summary>The last pose the rule declares, used when an item has more sockets than poses.</summary>
        internal ModelTransform LastPose(string target, int side)
        {
            CANGemVisualTargetPoses over = Override(target);
            ModelTransform[] tuned = over == null ? null : SocketsOf(over.Sockets, over.SocketsBack, side);
            if (tuned != null && tuned.Length > 0) return tuned[tuned.Length - 1];

            ModelTransform[] own = SocketsOf(Sockets, SocketsBack, side);
            if (own != null && own.Length > 0) return own[own.Length - 1];
            return null;
        }

        /// <summary>Whether this rule puts a gem on the far side of the model for that target.</summary>
        internal bool FlipsFor(string target)
        {
            CANGemVisualTargetPoses over = Override(target);
            if (over?.Flip != null) return over.Flip.Value;
            return Flip ?? true;
        }

        internal Vec3f FlipCenterFor() => FlipCenter ?? CANGemVisualRegistry.DefaultFlipCenter;

        /// <summary>
        /// The gear element a gem hangs off for that target and side, null when the rule says
        /// nothing. The far side falls back to the near side's element, which is right whenever the
        /// two gems sit on the same part of the piece.
        /// </summary>
        internal string AttachElementFor(string target, int side)
        {
            CANGemVisualTargetPoses over = Override(target);
            if (side == CANGemVisualSide.Back)
            {
                return over?.AttachElementBack ?? AttachElementBack ?? over?.AttachElement ?? AttachElement;
            }
            return over?.AttachElement ?? AttachElement;
        }
    }
}
