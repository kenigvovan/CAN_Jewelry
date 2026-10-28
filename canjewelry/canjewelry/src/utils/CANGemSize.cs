using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.utils
{
    /// <summary>
    /// Gem sizes and the item codes built from them, in one place. Cut gems and rough gems use two
    /// different scales that share the numbers 1..3: cut normal/flawless/exquisite, rough
    /// chipped/flawed/normal.
    /// </summary>
    public static class CANGemSize
    {
        public const string CutNormal = "normal";
        public const string CutFlawless = "flawless";
        public const string CutExquisite = "exquisite";

        public const string RoughChipped = "chipped";
        public const string RoughFlawed = "flawed";
        public const string RoughNormal = "normal";

        private static readonly string[] CutQualities = { CutNormal, CutFlawless, CutExquisite };
        private static readonly string[] RoughQualities = { RoughChipped, RoughFlawed, RoughNormal };

        /// <summary>Cut quality of size 1..3, null outside that range.</summary>
        public static string CutQualityOrNull(int size)
            => size >= 1 && size <= CutQualities.Length ? CutQualities[size - 1] : null;

        /// <summary>Cut quality for drawing: anything below 2 is normal, anything above 3 exquisite.</summary>
        public static string CutQualityClamped(int size)
            => size >= 3 ? CutExquisite : (size == 2 ? CutFlawless : CutNormal);

        /// <summary>Size 1..3 of a cut quality name, 0 when unknown.</summary>
        public static int CutSizeOf(string quality)
            => Array.IndexOf(CutQualities, quality) + 1;

        public static string RoughQualityOrNull(int size)
            => size >= 1 && size <= RoughQualities.Length ? RoughQualities[size - 1] : null;

        public static int RoughSizeOf(string quality)
            => Array.IndexOf(RoughQualities, quality) + 1;

        /// <summary>Rough quality of a vanilla gem's "potential" attribute, null when unknown.</summary>
        public static string RoughQualityOfPotential(string potential)
        {
            switch (potential)
            {
                case "low": return RoughChipped;
                case "medium": return RoughFlawed;
                case "high": return RoughNormal;
                default: return null;
            }
        }

        /// <summary>The vanilla "potential" a rough quality stands for, null when unknown.</summary>
        public static string PotentialOfRoughQuality(string quality)
        {
            switch (quality)
            {
                case RoughChipped: return "low";
                case RoughFlawed: return "medium";
                case RoughNormal: return "high";
                default: return null;
            }
        }

        /// <summary>
        /// Rough quality of a rough gem stack: a vanilla gem's "potential" first, then our own
        /// quality variant. Vanilla gems without potential count as flawed.
        /// </summary>
        public static string RoughQualityOf(ItemStack stack)
        {
            if (stack == null) return null;
            string quality = stack.Attributes.HasAttribute("potential")
                ? RoughQualityOfPotential(stack.Attributes.GetString("potential"))
                : null;
            quality ??= stack.Collectible?.Variant["quality"];
            return RoughSizeOf(quality) > 0 ? quality : RoughFlawed;
        }

        /// <summary>Gem type of a rough gem stack, ours or vanilla.</summary>
        public static string RoughGemTypeOf(ItemStack stack)
            => stack?.Collectible?.Variant[CANJWConstants.GEM_TYPE_IN_SOCKET] ?? stack?.Collectible?.Variant["ore"];

        /// <summary>Weight of a cut gem tree or a socket; the legacy weight of its size when it has none.</summary>
        public static float GetCutCarats(ITreeAttribute tree, int size, Config.GemCaratsConfig cfg)
            => CANGemAppraisal.HasCarats(tree) ? CANGemAppraisal.GetCarats(tree) : CANGemAppraisal.LegacyCutCarats(cfg, size);

        /// <summary>Gem type from the last part of an item code: gem-cut-normal-ruby → ruby.</summary>
        public static string GemTypeOf(AssetLocation code)
        {
            string path = code?.Path;
            if (string.IsNullOrEmpty(path)) return null;

            int dash = path.LastIndexOf('-');
            return dash < 0 ? path : path.Substring(dash + 1);
        }

        public static string GemTypeOf(CollectibleObject collectible) => GemTypeOf(collectible?.Code);

        public static AssetLocation CutGemCode(string quality, string gemType)
            => new AssetLocation("canjewelry:gem-cut-" + quality + "-" + gemType);

        public static AssetLocation RoughGemCode(string quality, string gemType)
            => new AssetLocation("canjewelry:gem-rough-" + quality + "-" + gemType);

        public static AssetLocation VanillaRoughGemCode(string gemType)
            => new AssetLocation("game:gem-" + gemType + "-rough");

        /// <summary>
        /// The cut gem tree a socket's gem is rebuilt with: cut, buffs and grinding progress.
        /// Legacy sockets without the grinding flag count as fully ground.
        /// </summary>
        public static ITreeAttribute BuildCutGemTreeFromSocket(ITreeAttribute socket)
        {
            ITreeAttribute cutGemTree = new TreeAttribute();
            cutGemTree.SetString(CANJWConstants.CUTTING_TYPE, socket.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND));

            string[] buffNames = (socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
            float[] buffValues = (socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;
            if (buffNames != null && buffValues != null)
            {
                cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute((string[])buffNames.Clone());
                cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute((float[])buffValues.Clone());
            }

            cutGemTree.SetBool(CANJWConstants.GEM_FULL_PROCESSED, socket.GetBool(CANJWConstants.WAS_GROUND_BEFORE, true));

            // Sockets from before carats have none; the gem then reads the legacy weight of its size.
            if (CANGemAppraisal.HasCarats(socket))
            {
                CANGemAppraisal.SetWeight(cutGemTree, CANGemAppraisal.GetCarats(socket), CANGemAppraisal.GetTraits(socket));
            }
            return cutGemTree;
        }

        /// <summary>
        /// Writes the socket's gem onto a fresh cut gem stack: the cut gem tree, unfinished grinding
        /// and the anti-farming flag. Shared by extraction, the drop on breaking and previews.
        /// </summary>
        public static void ApplySocketToCutGem(ItemStack gemStack, ITreeAttribute socket)
        {
            ITreeAttribute cutGemTree = BuildCutGemTreeFromSocket(socket);
            gemStack.Attributes[CANJWConstants.CUT_GEM_TREE] = cutGemTree;

            if (!cutGemTree.GetBool(CANJWConstants.GEM_FULL_PROCESSED) && socket.HasAttribute("savedGrindLayerInfo"))
            {
                gemStack.Attributes["cangrindlayerinfo"] = socket["savedGrindLayerInfo"].Clone();
            }
            if (socket.GetBool("wasExtracted", false)) gemStack.Attributes.SetBool("wasExtracted", true);
        }

        /// <summary>The cut gem code of a socket's gem, null for an empty socket.</summary>
        public static AssetLocation CutGemCodeFromSocket(ITreeAttribute socket)
        {
            string gemType = socket?.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
            if (string.IsNullOrEmpty(gemType)) return null;

            // Clamped, as extraction always was: an old socket with an odd size still gives its gem back.
            return CutGemCode(CutQualityClamped(socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE)), gemType);
        }

        /// <summary>The gem set into a socket as a cut gem stack, null for an empty socket or unknown item.</summary>
        public static ItemStack BuildCutGemFromSocket(IWorldAccessor world, ITreeAttribute socket)
        {
            if (world == null || socket == null) return null;

            AssetLocation code = CutGemCodeFromSocket(socket);
            if (code == null) return null;

            Item gemItem = world.GetItem(code);
            if (gemItem == null) return null;

            ItemStack gemStack = new ItemStack(gemItem);
            ApplySocketToCutGem(gemStack, socket);
            return gemStack;
        }
    }
}
