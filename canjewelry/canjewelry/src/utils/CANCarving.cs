using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using static canjewelry.src.Config;

namespace canjewelry.src.utils
{
    /// <summary>
    /// Carving bone, antler and other materials on the gem cutting table. The material sets the
    /// ceiling, the work how close to it a piece gets: its carat budget is the form's base, times
    /// the material's ceiling, times how cleanly it was carved. Polishing adds luster, which
    /// scales the buffs of the jewelry the piece ends up in.
    /// </summary>
    public static class CANCarving
    {
        private const string FallbackTexture = "game:block/creature/bone";
        private static readonly int[] FallbackBlock = { 9, 3, 5 };

        public static bool IsCarvable(CollectibleObject collectible)
            => collectible?.Attributes?[CANJWConstants.CARVABLE].Exists == true;

        public static bool IsCarving(ItemStack workItem)
            => workItem?.Attributes?.HasAttribute(CANJWConstants.CARVE_MATERIAL) == true;

        public static bool IsPolishable(ItemStack stack)
            => stack?.Collectible?.Attributes?[CANJWConstants.POLISHABLE].AsBool(false) == true;

        /// <summary>The material's ceiling, the value for its size variant first.</summary>
        public static float CeilingOf(CollectibleObject material)
        {
            JsonObject carvable = material.Attributes[CANJWConstants.CARVABLE];
            string size = material.Variant["size"];
            if (size != null && carvable["ceilingBySize"][size].Exists)
            {
                return carvable["ceilingBySize"][size].AsFloat(1f);
            }
            return carvable["ceiling"].AsFloat(1f);
        }

        /// <summary>Writes onto a fresh work item what the carving needs to know of its material.</summary>
        public static void WriteWorkItem(CollectibleObject material, ITreeAttribute tree)
        {
            tree.SetString(CANJWConstants.CARVE_MATERIAL, material.Code.ToString());
            tree.SetString(CANJWConstants.CARVE_TEXTURE, material.Attributes[CANJWConstants.CARVABLE]["texture"].AsString(FallbackTexture));
            tree.SetFloat(CANJWConstants.CARVE_CEILING, CeilingOf(material));
            string name = material.Attributes[CANJWConstants.CARVABLE]["name"].AsString(null);
            if (name != null) tree.SetString(CANJWConstants.CARVE_NAME, name);
        }

        public static string TextureOf(ItemStack workItem)
            => workItem.Attributes.GetString(CANJWConstants.CARVE_TEXTURE, FallbackTexture);

        /// <summary>
        /// The material's block of voxels, centred the way recipe patterns are. Gem cutting recipes
        /// are rotated, so "block" is [characters per pattern line, layers, lines per layer].
        /// </summary>
        public static byte[,,] CreateVoxels(CollectibleObject material)
        {
            int[] block = material.Attributes[CANJWConstants.CARVABLE]["block"].AsArray<int>(null);
            if (block == null || block.Length < 3) block = FallbackBlock;
            int sx = Math.Clamp(block[0], 1, 16), sy = Math.Clamp(block[1], 1, 14), sz = Math.Clamp(block[2], 1, 16);
            int startX = (16 - sx) / 2, startZ = (16 - sz) / 2;

            var voxels = new byte[16, 14, 16];
            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                        voxels[startX + x, y, startZ + z] = 1;
            return voxels;
        }

        /// <summary>The material a work item was carved from, null when it no longer exists.</summary>
        public static ItemStack MaterialOf(IWorldAccessor world, ItemStack workItem)
        {
            var code = new AssetLocation(workItem.Attributes.GetString(CANJWConstants.CARVE_MATERIAL));
            CollectibleObject material = (CollectibleObject)world.GetItem(code) ?? world.GetBlock(code);
            return material == null ? null : new ItemStack(material);
        }

        /// <summary>Carat budget of a piece; accuracy is 1 for a flawless carving.</summary>
        public static float Budget(float formBudget, ItemStack workItem, float accuracy, float step)
        {
            float ceiling = workItem.Attributes.GetFloat(CANJWConstants.CARVE_CEILING, 1f);
            return Math.Max(step, CANGemAppraisal.RoundCarats(formBudget * ceiling * Math.Clamp(accuracy, 0f, 1f), step));
        }

        /// <summary>
        /// Turns the recipe's output into the finished piece: the budget onto the stack in place of
        /// the form's base, and no luster yet, so an unpolished piece reads as such.
        /// </summary>
        public static void FinishPiece(ItemStack piece, ItemStack workItem, float accuracy, float step)
        {
            float formBudget = piece.Attributes.GetFloat(CANJWConstants.CARVE_BUDGET, 1f);
            piece.Attributes.RemoveAttribute(CANJWConstants.CARVE_BUDGET);
            piece.Attributes.SetFloat(CANJWConstants.CARAT_BUDGET, Budget(formBudget, workItem, accuracy, step));
            piece.Attributes.SetFloat(CANJWConstants.LUSTER, 0f);

            ITreeAttribute work = workItem.Attributes;
            piece.Attributes.SetString(CANJWConstants.PIECE_MATERIAL, work.GetString(CANJWConstants.CARVE_MATERIAL));
            piece.Attributes.SetString(CANJWConstants.PIECE_TEXTURE, TextureOf(workItem));
            string name = work.GetString(CANJWConstants.CARVE_NAME);
            if (name != null) piece.Attributes.SetString(CANJWConstants.PIECE_NAME, name);
        }

        public static string PieceTextureOf(ItemStack stack)
            => stack?.Attributes?.GetString(CANJWConstants.PIECE_TEXTURE);

        /// <summary>The material's "name" lang key from cancarvable, else the material's own name.</summary>
        public static string PieceMaterialName(IWorldAccessor world, ItemStack stack)
        {
            ITreeAttribute attrs = stack?.Attributes;
            if (attrs == null) return null;
            string name = attrs.GetString(CANJWConstants.PIECE_NAME);
            if (name != null) return Lang.Get(name);

            string code = attrs.GetString(CANJWConstants.PIECE_MATERIAL);
            if (code == null || world == null) return null;
            var location = new AssetLocation(code);
            CollectibleObject material = (CollectibleObject)world.GetItem(location) ?? world.GetBlock(location);
            return material == null ? null : new ItemStack(material).GetName();
        }

        public static void CopyPieceMaterial(ItemStack from, ItemStack to)
        {
            foreach (string key in new[] { CANJWConstants.PIECE_MATERIAL, CANJWConstants.PIECE_TEXTURE, CANJWConstants.PIECE_NAME })
            {
                string value = from.Attributes.GetString(key);
                if (value != null) to.Attributes.SetString(key, value);
            }
        }

        /// <summary>
        /// Jewelry crafted from carved pieces: their budgets add up, unless the jewelry sets
        /// canbudgetfrompieces to false and keeps its own; it shines no more than its dullest
        /// piece. Inputs without a budget are not pieces and are left out. The jewelry takes the
        /// material of its first piece.
        /// </summary>
        public static void CombinePieces(ItemSlot[] inputs, ItemStack output, float step)
        {
            if (output == null) return;
            float budget = 0f, luster = 1f;
            int pieces = 0;
            foreach (ItemSlot slot in inputs ?? new ItemSlot[0])
            {
                ITreeAttribute attrs = slot?.Itemstack?.Attributes;
                if (attrs == null || !attrs.HasAttribute(CANJWConstants.CARAT_BUDGET)) continue;
                budget += attrs.GetFloat(CANJWConstants.CARAT_BUDGET);
                luster = Math.Min(luster, attrs.GetFloat(CANJWConstants.LUSTER));
                if (pieces == 0) CopyPieceMaterial(slot.Itemstack, output);
                pieces++;
            }
            if (pieces == 0) return;

            output.Attributes.SetFloat(CANJWConstants.LUSTER, luster);
            if (output.Collectible.Attributes?[CANJWConstants.BUDGET_FROM_PIECES].AsBool(true) == false)
            {
                output.Attributes.RemoveAttribute(CANJWConstants.CARAT_BUDGET);
                return;
            }
            output.Attributes.SetFloat(CANJWConstants.CARAT_BUDGET, Math.Max(step, CANGemAppraisal.RoundCarats(budget, step)));
        }

        public static float LusterAfterStage(CarvingConfig cfg, int stage)
            => cfg != null && cfg.LusterByStage.TryGetValue(stage, out float luster) ? luster : 1f;

        /// <summary>0 unpolished, 1 after the first grinder stage, 2 fully polished.</summary>
        public static int LusterLevel(ItemStack stack)
        {
            if (stack?.Attributes?.HasAttribute(CANJWConstants.LUSTER) != true) return 0;
            float luster = stack.Attributes.GetFloat(CANJWConstants.LUSTER);
            return luster >= 0.999f ? 2 : luster >= 0.499f ? 1 : 0;
        }

        /// <summary>Makes the faces drawn with the material texture shine, the more the higher the level.</summary>
        public static void ApplyLuster(Shape shape, int level, string textureCode = "material")
        {
            if (shape?.Elements == null || level <= 0) return;
            EnumReflectiveMode mode = level >= 2 ? EnumReflectiveMode.Medium : EnumReflectiveMode.Mild;
            foreach (ShapeElement element in shape.Elements) ApplyLuster(element, mode, textureCode);
        }

        private static void ApplyLuster(ShapeElement element, EnumReflectiveMode mode, string textureCode)
        {
            foreach (ShapeElementFace face in element.FacesResolved ?? new ShapeElementFace[0])
            {
                if (face != null && face.Texture?.TrimStart('#') == textureCode) face.ReflectiveMode = mode;
            }
            foreach (ShapeElement child in element.Children ?? new ShapeElement[0]) ApplyLuster(child, mode, textureCode);
        }

        /// <summary>What the luster of a stack does to its buffs; 1 for stacks without luster.</summary>
        public static float LusterBuffMult(CarvingConfig cfg, ItemStack stack)
        {
            if (cfg == null || stack?.Attributes?.HasAttribute(CANJWConstants.LUSTER) != true) return 1f;
            float luster = Math.Clamp(stack.Attributes.GetFloat(CANJWConstants.LUSTER), 0f, 1f);
            return cfg.LusterBuffMin + (1f - cfg.LusterBuffMin) * luster;
        }
    }
}
