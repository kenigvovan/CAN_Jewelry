using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canjewelry.src.render
{
    /// <summary>
    /// Draws a gem bigger or smaller by its weight. The render info's transform is the item's own,
    /// shared by every stack of it, so a scaled copy is made once per transform and scale.
    /// </summary>
    public static class CANGemDrawScale
    {
        private static readonly ConditionalWeakTable<ModelTransform, Dictionary<float, ModelTransform>> scaled = new();

        public static void Apply(ref ItemRenderInfo renderinfo, float scale)
        {
            ModelTransform original = renderinfo.Transform;
            if (original == null || scale == 1f) return;

            Dictionary<float, ModelTransform> byScale = scaled.GetOrCreateValue(original);
            if (!byScale.TryGetValue(scale, out ModelTransform copy))
            {
                copy = original.Clone();
                copy.ScaleXYZ.Set(original.ScaleXYZ.X * scale, original.ScaleXYZ.Y * scale, original.ScaleXYZ.Z * scale);
                byScale[scale] = copy;
            }
            renderinfo.Transform = copy;
        }
    }
}
