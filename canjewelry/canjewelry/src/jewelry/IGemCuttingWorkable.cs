using canjewelry.src.be;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace canjewelry.src.jewelry
{
    public interface IGemCuttingWorkable
    {
        int GetRequiredGemCuttingTableTier(ItemStack stack);

        List<GemCuttingRecipe> GetMatchingRecipes(ItemStack stack);

        bool CanWork(ItemStack stack);

        ItemStack TryPlaceOn(ItemStack stack, BlockEntityGemCuttingTable beGemCuttingTable);

        ItemStack GetBaseMaterial(ItemStack stack);
    }
}
