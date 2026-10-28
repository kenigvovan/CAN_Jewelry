using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using canjewelry.src.be;
using canjewelry.src.jewelry;
using canjewelry.src.utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using static canjewelry.src.Config;

namespace canjewelry.src.items.resource
{
    public class CANRoughGemItem: Item
    {
        public bool CanWork(ItemStack stack)
        {
            return true;
        }

        public ItemStack GetBaseMaterial(ItemStack stack)
        {
            return stack;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if(inSlot.Empty)
            {
                return;
            }
            ItemStack itemStack = inSlot.Itemstack;

            string gemType = itemStack.Collectible.Variant["gemtype"];
            if (canjewelry.config.TurnOffBuffs)
            {
                return;
            }
            bool mainStatHeaderAdded = false;
            int gemTier = itemStack.Collectible.Attributes?["canGemType"].AsInt() ?? 0;
            if (gemType != null && canjewelry.config.PossibleGemBuffs.TryGetValue(gemType, out var possibleBuffs))
            {
                foreach(var buffName in possibleBuffs)
                {
                    if (canjewelry.config.BuffAttributesDict.TryGetValue(buffName, out BuffAttributes buffAttributes))
                    {
                        float[] forecast = ForecastRange(itemStack, gemType, buffAttributes);
                        if (forecast != null)
                        {
                            if (!mainStatHeaderAdded)
                            {
                                dsc.AppendLine(Lang.Get("canjewelry:rough-gem-possible-stats-header"));
                                mainStatHeaderAdded = true;
                            }
                            dsc.Append(CANBuffText.Name(buffName)).Append(' ').AppendLine(CANBuffText.Range(buffName, forecast));
                            continue;
                        }
                        // A tier the config does not list is skipped instead of throwing in the tooltip.
                        if (buffAttributes.MainStatValueRange == null
                            || !buffAttributes.MainStatValueRange.TryGetValue(gemTier, out float[] range)
                            || range == null || range.Length < 2)
                        {
                            continue;
                        }
                        if (!mainStatHeaderAdded)
                        {
                            dsc.AppendLine(Lang.Get("canjewelry:rough-gem-possible-stats-header"));
                            mainStatHeaderAdded = true;
                        }
                        if (buffName.Equals("maxhealthExtraPoints"))
                        {
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName))
                                .Append(string.Format(" {0}/{1}", range[0], range[1]))
                                .AppendLine();
                        }
                        else
                        {
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName))
                                .Append(string.Format("{0}/{1}", (range[0] > 0
                                                                    ? " +" + Math.Round(range[0] * 100, 3)
                                                                    : Math.Round(range[0] * 100, 3)) + "%",
                                                                range[1] > 0
                                                                    ? " +" + Math.Round(range[1] * 100, 2)
                                                                    : Math.Round(range[1] * 100, 2))).Append("%")
                                .AppendLine();
                
                        }
                    }

                }
                return;
            }

            if (inSlot.Itemstack.Collectible.Attributes.KeyExists("canGemTypeToAttribute"))
            {
                string buffName = inSlot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString();
                if (buffName.Equals("maxhealthExtraPoints"))
                {
                    if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict)
                        && buffValuesDict.TryGetValue(gemTier.ToString(), out float flatValue))
                    {
                        dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName)).Append(" +" + flatValue);
                    }
                }
                else
                {
                    if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict)
                        && buffValuesDict.TryGetValue(gemTier.ToString(), out float rawValue))
                    {
                        float buffValue = rawValue * 100;
                        dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName));
                        dsc.Append(buffValue > 0 ? " +" + Math.Round(buffValue) + "%" : " " + Math.Round(buffValue) + "%");
                    }
                }
            }
            dsc.AppendLine();
            dsc.Append(Lang.Get("canjewelry:need_to_be_processed"));
        }

        /// <summary>
        /// The base main stat this gem can end up with when weight drives buffs, over every cut,
        /// before the cut's and polishing's multipliers. Null when buffs go by tiers.
        /// </summary>
        private static float[] ForecastRange(ItemStack stack, string gemType, BuffAttributes buffAttributes)
        {
            var cfg = canjewelry.config.GemCarats;
            if (cfg == null) return null;
            ITreeAttribute tree = stack.Attributes;
            float[] rough = CANGemForecast.KnownRoughRange(cfg, tree, CANGemSize.RoughQualityOf(stack), gemType);
            float[] cut = rough == null ? null : CANGemForecast.CutWeightAnyCut(cfg, rough, gemType, tree);
            return CANGemForecast.MainBuff(cfg, buffAttributes, cut, CANGemForecast.KnownBuffMult(cfg, tree, gemType));
        }

        public override void OnBeforeRender(Vintagestory.API.Client.ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref Vintagestory.API.Client.ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            // The inspection works out where flaws land from the unscaled transform.
            if (itemstack.Attributes.GetBool(render.CANGemInspectMesh.ViewKey)) return;
            render.CANGemDrawScale.Apply(ref renderinfo, utils.CANGemAppraisal.RoughDrawScale(canjewelry.config?.GemCarats,
                itemstack.Attributes, utils.CANGemSize.RoughQualityOf(itemstack), utils.CANGemSize.RoughGemTypeOf(itemstack)));
        }

        public List<GemCuttingRecipe> GetMatchingRecipes(ItemStack stack)
        {
            return cb.CANGemCuttableCB.FindMatchingRecipes(stack, checkRecipeAttributes: false);
        }

        public int GetRequiredGemCuttingTableTier(ItemStack stack)
        {
            return 0;
        }
        public static int AddVoxelsFromRoughGem(ref byte[,,] voxels)
        {
            int num = 0;
            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    int k = 0;
                    int num2 = 0;
                    for (; k < 6; k++)
                    {
                        if (num2 >= 2)
                        {
                            break;
                        }

                        if (voxels[4 + i, k, 6 + j] == 0)
                        {
                            voxels[4 + i, k, 6 + j] = 1;
                            num2++;
                            num++;
                        }
                    }
                }
            }

            return num;
        }
        public static void CreateVoxelsFromRoughGem(ICoreAPI api, ref byte[,,] voxels, bool isBlisterSteel = false)
        {
            voxels = new byte[16, 14, 16];
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    for (int k = 0; k < 9; k++)
                    {
                        voxels[3 + i, j, 3 + k] = 1;
                        if (isBlisterSteel)
                        {
                            if (api.World.Rand.NextDouble() < 0.5)
                            {
                                voxels[3 + i, j, 3 + k] = 1;
                            }

                            if (api.World.Rand.NextDouble() < 0.5)
                            {
                                voxels[3 + i, j, 3 + k] = 2;
                            }
                        }
                    }
                }
            }
            //north
            for(int i = 0; i < 11; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[2 + i, j, 2] = 1;
                    }
                }
            }
            //south
            for (int i = 0; i < 11; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[2 + i, j, 12] = 1;
                    }
                }
            }
            //west
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[2, j, 2 + i] = 1;
                    }
                }
            }
            //east
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[12, j, 2 + i] = 1;
                    }
                }
            }
            //top
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 9; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[3 + i, 6, 3 + j] = 1;
                    }
                }
            }
        }
    }

}
