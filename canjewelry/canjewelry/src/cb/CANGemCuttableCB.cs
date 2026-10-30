using canjewelry.src.be;
using canjewelry.src.items;
using canjewelry.src.items.resource;
using canjewelry.src.jewelry;
using canjewelry.src.utils;
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
        public static bool IsVanillaRoughGem(CollectibleObject collectible)
        {
            return collectible?.Code?.Domain == "game"
                && collectible.Code.Path.StartsWith("gem-")
                && collectible.Code.Path.EndsWith("-rough");
        }

        /// <summary>
        /// Makes every rough gem placeable on the cutting table. Independent of the config, so it
        /// runs on both sides at load: on the client it used to happen only once the server's
        /// config packet was applied, and if that failed the table silently refused every gem.
        /// </summary>
        public static void AttachToRoughGems(ICoreAPI api)
        {
            Item[] roughGems = api.World.SearchItems(new AssetLocation("canjewelry:gem-rough-*"))
                .Append(api.World.SearchItems(new AssetLocation("game:gem-*-rough")));

            foreach (Item gem in roughGems)
            {
                if (!gem.HasBehavior<CANGemCuttableCB>())
                    gem.CollectibleBehaviors = gem.CollectibleBehaviors.Append(new CANGemCuttableCB(gem));
                if (!gem.HasBehavior<CANGemAppraisalCB>())
                    gem.CollectibleBehaviors = gem.CollectibleBehaviors.Append(new CANGemAppraisalCB(gem));
                // So a right click with a rough gem can reach water, to wet it for the inspection.
                gem.LiquidSelectable = true;
            }

            foreach (Item item in api.World.Items)
            {
                if (CANCarving.IsCarvable(item) && !item.HasBehavior<CANGemCuttableCB>())
                    item.CollectibleBehaviors = item.CollectibleBehaviors.Append(new CANGemCuttableCB(item));
            }
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
                        && !RecipeAttributesMatch(stack.Attributes, recipe.Ingredient.RecipeAttributes.ToAttribute() as ITreeAttribute))
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

            return result.OrderBy(ListOrder).ThenBy(r => r.Output.ResolvedItemstack.Collectible.Code.ToString()).ToList();
        }

        private static readonly string[] CutOrder = { CANJWConstants.CUTTING_ROUND, CANJWConstants.CUTTING_PEAR, CANJWConstants.CUTTING_BAGUETTE };
        private static readonly string[] CleaveOrder = { "halves", "thirds", "seventythirty" };

        /// <summary>
        /// Where a recipe goes in the table's list: the cuts first (round, pear, baguette), then
        /// the cleaves (halves, thirds, 70/30), anything else in between.
        /// </summary>
        private static int ListOrder(GemCuttingRecipe recipe)
        {
            ItemStack output = recipe.Output.ResolvedItemstack;
            string path = output.Collectible.Code.Path;
            if (path.StartsWith("cancleave-"))
            {
                int cleave = Array.IndexOf(CleaveOrder, path.Substring("cancleave-".Length));
                return 200 + (cleave < 0 ? 99 : cleave);
            }
            string cut = output.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.GetString(CANJWConstants.CUTTING_TYPE);
            int index = Array.IndexOf(CutOrder, cut);
            return index < 0 ? 100 : index;
        }

        /// <summary>
        /// Whether the stack carries every attribute the recipe names, with the same value. Extra
        /// attributes on the stack are allowed: the whole tree used to be compared, so anything new
        /// written onto a work item would have stopped every recipe from matching it.
        /// </summary>
        public static bool RecipeAttributesMatch(ITreeAttribute stackAttributes, ITreeAttribute recipeAttributes)
        {
            if (recipeAttributes == null) return true;
            if (stackAttributes == null) return recipeAttributes.Count == 0;

            foreach (var pair in recipeAttributes)
            {
                IAttribute own = stackAttributes[pair.Key];
                if (own == null || own.ToJsonToken() != pair.Value?.ToJsonToken()) return false;
            }
            return true;
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
            if (CANCarving.IsCarvable(stack.Collectible))
            {
                if (beGemCuttingTable.WorkItemStack != null) return null;
                CANCarving.WriteWorkItem(stack.Collectible, workItemStack.Attributes);
                beGemCuttingTable.Voxels = CANCarving.CreateVoxels(stack.Collectible);
                return workItemStack;
            }
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
                qualityName = CANGemSize.RoughQualityOfPotential(stack.Attributes.GetString("potential")) ?? qualityName;
            }
            gemItemAttribute.SetString(CANJWConstants.ENCRUSTED_GEM_SIZE, qualityName);
            // An appraised gem keeps its weight and traits through the cut and back on cancel.
            CANGemAppraisal.CopyRoughKeys(stack.Attributes, gemItemAttribute);
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
                return CANCarving.IsCarving(stack) ? CANCarving.MaterialOf(aapi.World, stack) : RoughGemOfWorkItem(aapi.World, stack);
            }
            return stack;
        }

        /// <summary>
        /// The rough gem a work item was made from: our own rough gem of that quality, or the
        /// vanilla one with the matching "potential" when there is no such item of ours.
        /// </summary>
        public static ItemStack RoughGemOfWorkItem(IWorldAccessor world, ItemStack workItem)
        {
            string quality = workItem.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE);
            string gemType = workItem.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET);

            Item item = world.GetItem(CANGemSize.RoughGemCode(quality, gemType));
            if (item != null)
            {
                var own = new ItemStack(item);
                CANGemAppraisal.CopyRoughKeys(workItem.Attributes, own.Attributes);
                return own;
            }

            item = world.GetItem(CANGemSize.VanillaRoughGemCode(gemType));
            if (item == null)
            {
                throw new Exception(string.Format("No rough gem for work item {0}: neither {1} nor {2} exists",
                    workItem.Collectible.Code, CANGemSize.RoughGemCode(quality, gemType), CANGemSize.VanillaRoughGemCode(gemType)));
            }

            var vanilla = new ItemStack(item);
            string potential = CANGemSize.PotentialOfRoughQuality(quality);
            if (potential != null) vanilla.Attributes.SetString("potential", potential);
            CANGemAppraisal.CopyRoughKeys(workItem.Attributes, vanilla.Attributes);
            return vanilla;
        }
    }
}
