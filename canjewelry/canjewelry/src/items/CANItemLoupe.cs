using Vintagestory.API.Common;

namespace canjewelry.src.items
{
    /// <summary>
    /// The jeweler's loupe. Does nothing on its own: held in the left hand, it lets a rough gem in
    /// the right hand be appraised (see CANGemAppraisalCB).
    /// </summary>
    public class CANItemLoupe : Item
    {
        public static bool IsHeldInLeftHand(EntityAgent byEntity)
            => byEntity?.LeftHandItemSlot?.Itemstack?.Collectible is CANItemLoupe;

        /// <summary>
        /// How far either side of the truth the weight this loupe shows may reach, 0 for the
        /// exact weight. Null when the loupe does not say: the config's LoupeRangeFraction then.
        /// </summary>
        public static float? WeighHalfWidth(EntityAgent byEntity)
        {
            var attrs = byEntity?.LeftHandItemSlot?.Itemstack?.Collectible?.Attributes;
            return attrs?["weighHalfWidth"].Exists == true ? attrs["weighHalfWidth"].AsFloat() : null;
        }
    }
}
