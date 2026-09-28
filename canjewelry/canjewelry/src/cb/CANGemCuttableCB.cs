using canjewelry.src.be;
using canjewelry.src.items;
using canjewelry.src.items.resource;
using canjewelry.src.jewelry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.cb
{
    public class CANGemCuttableCB: CollectibleBehavior
    {
        public static ICoreAPI aapi;
        public CANGemCuttableCB(CollectibleObject collObj) : base(collObj)
        {
        }
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            aapi = api;
        }
        public int GetRequiredGemCuttingTableTier(ItemStack stack)
        { 
            return 0; 
        }

        public List<GemCuttingRecipe> GetMatchingRecipes(ItemStack stack)
        {
            return FindMatchingRecipes(stack, checkRecipeAttributes: true);
        }

        // How many broken recipe/stack combinations have been logged so far. Capped so a
        // companion mod probing this method every tick cannot flood the log.
        private static int matchErrorsLogged;

        /// <summary>
        /// Shared, defensive recipe matcher. This method is public API surface for companion mods
        /// (AdvancedGeology invokes it through reflection on a game tick), so a single recipe or
        /// stack it cannot digest must be skipped, never thrown: an exception escaping here once
        /// took whole servers down via the tick-error threshold.
        /// </summary>
        internal static List<GemCuttingRecipe> FindMatchingRecipes(ItemStack stack, bool checkRecipeAttributes)
        {
            var result = new List<GemCuttingRecipe>();
            var recipes = canjewelry.gemCuttingRecipes;
            if (recipes == null || stack?.Collectible == null || stack.Attributes == null)
            {
                return result;
            }

            foreach (var recipe in recipes)
            {
                try
                {
                    // Recipes whose output never resolved cannot be selected or sorted anyway.
                    if (recipe?.Ingredient == null || recipe.Output?.ResolvedItemstack?.Collectible == null) continue;

                    if (!recipe.Ingredient.SatisfiesAsIngredient(stack, true)) continue;

                    if (checkRecipeAttributes
                        && recipe.Ingredient.RecipeAttributes != null
                        && !stack.Attributes.ToJsonToken().Equals(recipe.Ingredient.RecipeAttributes.ToAttribute().ToJsonToken()))
                    {
                        continue;
                    }

                    result.Add(recipe);
                }
                catch (Exception e)
                {
                    if (matchErrorsLogged++ < 10)
                    {
                        aapi?.Logger.Warning(
                            "[canjewelry] Skipping gem cutting recipe {0} while matching stack {1}: {2}",
                            recipe?.Name, stack.Collectible.Code, e);
                    }
                }
            }

            return result.OrderBy(r => r.Output.ResolvedItemstack.Collectible.Code).ToList();
        }

        public bool CanWork(ItemStack stack)
        {
            return true;
        }

        public ItemStack TryPlaceOn(ItemStack stack, BlockEntityGemCuttingTable beGemCuttingTable)
        {
            if(stack.Item is CANItemGemCuttingWorkItem)
            {
                if (beGemCuttingTable.WorkItemStack != null)
                {
                    return null;
                }
                try
                {
                    beGemCuttingTable.Voxels = BlockEntityGemCuttingTable.deserializeVoxels(stack.Attributes.GetBytes("voxels", null));
                    beGemCuttingTable.SelectedRecipeId = stack.Attributes.GetInt("selectedRecipeId", 0);
                }
                catch (Exception)
                {
                }
                return stack.Clone();
            }
            if (!this.CanWork(stack))
            {
                return null;
            }
            Item item = beGemCuttingTable.Api.World.GetItem(new AssetLocation("canjewelry:gemcuttingworkitem"));  //this.Variant["metal"]
                                                                                                     // + this.Variant["gemtype"]

            if (item == null)
            {
                return null;
            }
            ItemStack workItemStack = new ItemStack(item, 1);
            ITreeAttribute gemItemAttribute = new TreeAttribute();
            string gemType = stack.Collectible.Variant[CANJWConstants.GEM_TYPE_IN_SOCKET];
            if(gemType == null)
            {
                gemType = stack.Collectible.Variant["ore"];
            }
            gemItemAttribute.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, gemType);
            string qualityName = stack.Collectible.Variant["quality"];
            if (stack.Attributes.HasAttribute("potential"))
            {
                string potName = stack.Attributes.GetString("potential");
                switch (potName)
                {
                    case "low":
                        qualityName = "chipped";
                        break;
                    case "medium":
                        qualityName = "flawed";
                        break;
                    case "high":
                        qualityName = "normal";
                        break;
                }
            }
            gemItemAttribute.SetString(CANJWConstants.ENCRUSTED_GEM_SIZE, qualityName);
            workItemStack.Attributes = gemItemAttribute;
            //workItemStack.Collectible.SetTemperature(beGemCuttingTable.Api.World, workItemStack, stack.Collectible.GetTemperature(beGemCuttingTable.Api.World, stack), true);
            if (beGemCuttingTable.WorkItemStack == null)
            {
                CANRoughGemItem.CreateVoxelsFromRoughGem(beGemCuttingTable.Api, ref beGemCuttingTable.Voxels, false);
            }
            else
            {
                return null;
            }
            return workItemStack;
        }

        public ItemStack GetBaseMaterial(ItemStack stack)
        {
            if (stack.Item is CANItemGemCuttingWorkItem)
            {
                Item item = aapi.World.GetItem(AssetLocation.Create("canjewelry:gem-rough-" + stack.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE) + "-" + stack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET)));
                //Item item = api.World.GetItem(AssetLocation.Create("ingot-" + Variant["metal"], Attributes?["baseMaterialDomain"].AsString("game")));
                if(item == null)
                {
                    item = aapi.World.GetItem(AssetLocation.Create("game:gem-" + stack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET)) + "-rough");
                }
                if (item == null)
                {
                    throw new Exception(string.Format("Base material for not found, there is no item with code 'ingot'"));
                }
                return new ItemStack(item);                
            }
            return stack;
        }
    }
}
