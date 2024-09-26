using canjewelry.src.be;
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
using Vintagestory.GameContent;

namespace canjewelry.src.items
{
    public class CANRoughGemItem: Item, IGemCuttingWorkable
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
            return (from r in canjewelry.gemCuttingRecipes
                    where r.Ingredient.SatisfiesAsIngredient(stack, true)
                    orderby r.Output.ResolvedItemstack.Collectible.Code
                    select r).ToList<GemCuttingRecipe>();
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
            /*for (int i = 0; i < 12; i++)
            {
                for (int j = 0; j < 12; j++)
                {
                    if (api.World.Rand.NextDouble() < 0.2)
                    {
                        voxels[i, 6, 2 + j] = 1;
                    }
                }
            }*/
        }
        public ItemStack TryPlaceOn(ItemStack stack, BlockEntityGemCuttingTable beGemCuttingTable)
        {
            if (!this.CanWork(stack))
            {
                return null;
            }
            Item item = this.api.World.GetItem(new AssetLocation("canjewelry:gemcuttingworkitem"));  //this.Variant["metal"]
            // + this.Variant["gemtype"]
            
            if (item == null)
            {
                return null;
            }
            ItemStack workItemStack = new ItemStack(item, 1);
            ITreeAttribute gemItemAttribute = new TreeAttribute();
            gemItemAttribute.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, this.Variant["gemtype"]);
            workItemStack.Attributes = gemItemAttribute;
            workItemStack.Collectible.SetTemperature(this.api.World, workItemStack, stack.Collectible.GetTemperature(this.api.World, stack), true);
            if (beGemCuttingTable.WorkItemStack == null)
            {
                CANRoughGemItem.CreateVoxelsFromRoughGem(this.api, ref beGemCuttingTable.Voxels, false);
            }
            else
            {
                return null;
                /*if (this.isBlisterSteel)
                {
                    return null;
                }*/
                if (!string.Equals(beGemCuttingTable.WorkItemStack.Collectible.Variant["metal"], stack.Collectible.Variant["metal"]))
                {
                    if (this.api.Side == EnumAppSide.Client)
                    {
                        (this.api as ICoreClientAPI).TriggerIngameError(this, "notequal", Lang.Get("Must be the same metal to add voxels", Array.Empty<object>()));
                    }
                    return null;
                }
                if (ItemIngot.AddVoxelsFromIngot(ref beGemCuttingTable.Voxels) == 0)
                {
                    if (this.api.Side == EnumAppSide.Client)
                    {
                        (this.api as ICoreClientAPI).TriggerIngameError(this, "requireshammering", Lang.Get("Try hammering down before adding additional voxels", Array.Empty<object>()));
                    }
                    return null;
                }
            }
            return workItemStack;
        }
    }

}
