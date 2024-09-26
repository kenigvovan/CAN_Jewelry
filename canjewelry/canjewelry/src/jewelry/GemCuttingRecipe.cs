using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace canjewelry.src.jewelry
{
    public class GemCuttingRecipe : LayeredVoxelRecipe<GemCuttingRecipe>, IByteSerializable
    {
        public override int QuantityLayers
        {
            get
            {
                return 14;
            }
        }
        public override string RecipeCategoryCode
        {
            get
            {
                return "gem_cutting";
            }
        }
        protected override bool RotateRecipe
        {
            get
            {
                return true;
            }
        }
        public override GemCuttingRecipe Clone()
        {
            return new GemCuttingRecipe
            {
                Pattern = (string[][])this.Pattern.Clone(),
                Ingredient = base.Ingredient.Clone(),
                Output = this.Output.Clone(),
                Name = base.Name,
                RecipeId = this.RecipeId
            };
        }
        void IByteSerializable.ToBytes(BinaryWriter writer)
        {
            writer.Write(RecipeId);
            base.Ingredient.ToBytes(writer);
            writer.Write(Pattern.Length);
            for (int i = 0; i < Pattern.Length; i++)
            {
                writer.WriteArray(Pattern[i]);
            }
            ////
            writer.Write(base.Name.ToShortString());
            writer.Write((short)Output.Type);
            writer.Write(Output.Code.ToShortString());
            writer.Write(Output.StackSize);
            writer.Write(Output.ResolvedItemstack != null);

            if (Output.ResolvedItemstack != null)
            {
                Output.ResolvedItemstack.ToBytes(writer);
            }
            //Output.ToBytes(writer);
            //base.ToBytes(writer);
        }
        void IByteSerializable.FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            base.Ingredient = new CraftingRecipeIngredient();
            RecipeId = reader.ReadInt32();
            base.Ingredient.FromBytes(reader, resolver);
            int num = reader.ReadInt32();
            Pattern = new string[num][];
            for (int i = 0; i < Pattern.Length; i++)
            {
                Pattern[i] = reader.ReadStringArray();
            }

            base.Name = new AssetLocation(reader.ReadString());
            Output = new JsonItemStack();
            Output.FromBytes(reader, resolver.ClassRegistry);
            //Output.Attributes = new JsonObject(Output.ResolvedItemstack.Attributes.Clone().ToJsonToken());
            var c = Output.ResolvedItemstack.Attributes.Clone();
            Output.Resolve(resolver, "[Voxel recipe FromBytes]", base.Ingredient.Code);
            
            Output.ResolvedItemstack.Attributes = c;
           /* if (Output.Attributes. CANJWConstants.CUTTING_TYPE))
            {
                Output.ResolvedItemstack.Attributes.SetString(CANJWConstants.CUTTING_TYPE, Output.Attributes[CANJWConstants.CUTTING_TYPE].ToString());
            }*/
            GenVoxels();
            //base.FromBytes(reader, resolver);
        }
    }
}
