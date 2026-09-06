using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using canjewelry.src.be;
using canjewelry.src.jewelry;
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
            if (canjewelry.config.PossibleGemBuffs.TryGetValue(gemType, out var possibleBuffs))
            {
                foreach(var buffName in possibleBuffs)
                {
                    if (canjewelry.config.BuffAttributesDict.TryGetValue(buffName, out BuffAttributes buffAttributes))
                    {
                        if(buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()] == null ||
                            buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()].Length < 2)
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
                                .Append(string.Format(" {0}/{1}", buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][0], buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][1]))
                                .AppendLine();                      
                        }
                        else
                        {
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName))
                                .Append(string.Format("{0}/{1}", (buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][0] > 0
                                                                                                    ? " +" + Math.Round(buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][0] * 100, 3)
                                                                                                    : Math.Round(buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][0] * 100, 3)) + "%",
                                                                    buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][1] > 0
                                                                                                    ? " +" + Math.Round(buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][1] * 100, 2)
                                                                                                    :  Math.Round(buffAttributes.MainStatValueRange[itemStack.Collectible.Attributes["canGemType"].AsInt()][1] * 100, 2))).Append("%")
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
                    if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict))
                    {
                        dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName)).Append(" +" + buffValuesDict[inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
                    }
                }
                else
                {
                    if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict))
                    {
                        float buffValue = buffValuesDict[inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()] * 100;
                        dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName));
                        dsc.Append(buffValue > 0 ? " +" + Math.Round(buffValue) + "%" : " " + Math.Round(buffValue) + "%");
                    }
                }
            }
            dsc.AppendLine();
            dsc.Append(Lang.Get("canjewelry:need_to_be_processed"));
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
        public ItemStack TryPlaceOn(ItemStack stack, BlockEntityGemCuttingTable beGemCuttingTable)
        {
            if (!CanWork(stack))
            {
                return null;
            }
            Item item = api.World.GetItem(new AssetLocation("canjewelry:gemcuttingworkitem"));  //this.Variant["metal"]
            // + this.Variant["gemtype"]
            
            if (item == null)
            {
                return null;
            }
            ItemStack workItemStack = new ItemStack(item, 1);
            ITreeAttribute gemItemAttribute = new TreeAttribute();
            gemItemAttribute.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, Variant[CANJWConstants.GEM_TYPE_IN_SOCKET]);
            gemItemAttribute.SetString(CANJWConstants.ENCRUSTED_GEM_SIZE, Variant["quality"]);
            workItemStack.Attributes = gemItemAttribute;
            //workItemStack.Collectible.SetTemperature(this.api.World, workItemStack, stack.Collectible.GetTemperature(this.api.World, stack), true);
            if (beGemCuttingTable.WorkItemStack == null)
            {
                CreateVoxelsFromRoughGem(api, ref beGemCuttingTable.Voxels, false);
            }
            else
            {
                return null;
            }
            return workItemStack;
        }
    }

}
