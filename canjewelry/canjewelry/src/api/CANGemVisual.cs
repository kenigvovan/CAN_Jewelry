using Vintagestory.API.Common;

namespace canjewelry.src.api
{
    /// <summary>
    /// Single point that writes and clears the gem visual on an encrustable ItemStack.
    /// The core no longer needs to know which item classes exist: the item declares its mode
    /// through the <see cref="CANJewelryAttributes.GemVisual"/> collectible attribute, so a
    /// content mod can ship jewelry the core has never heard of.
    /// </summary>
    public static class CANGemVisual
    {
        /// <summary>
        /// Records <paramref name="gemType"/> as the visible gem for the given socket.
        /// No-op when the item declares no gem visual mode.
        /// </summary>
        /// <param name="socketIndex">0-based socket index, as used everywhere else in the code.</param>
        public static void SetGemVisual(ItemStack stack, int socketIndex, string gemType)
        {
            string key = ResolveKey(stack, socketIndex);
            if (key == null) return;
            stack.Attributes.SetString(key, gemType);
        }

        /// <summary>
        /// Removes the visible gem for the given socket. Symmetric to
        /// <see cref="SetGemVisual"/> — same item, same index, same key.
        /// </summary>
        public static void ClearGemVisual(ItemStack stack, int socketIndex)
        {
            string key = ResolveKey(stack, socketIndex);
            if (key == null) return;
            stack.Attributes.RemoveAttribute(key);
        }

        /// <summary>
        /// The stack attribute this item uses for the given socket, or null when the item
        /// carries no gem visual.
        /// </summary>
        public static string ResolveKey(ItemStack stack, int socketIndex)
        {
            string mode = stack?.Collectible?.Attributes?[CANJewelryAttributes.GemVisual]?.AsString(null);
            if (mode == null) return null;

            switch (mode)
            {
                case CANJewelryAttributes.GemVisualSingle:
                    return CANJewelryAttributes.GemStackKey;
                case CANJewelryAttributes.GemVisualIndexed:
                    // Historically 1-based on the stack, while sockets are 0-based in code.
                    return CANJewelryAttributes.GemStackKeyPrefix + (socketIndex + 1);
                default:
                    return null;
            }
        }
    }
}
