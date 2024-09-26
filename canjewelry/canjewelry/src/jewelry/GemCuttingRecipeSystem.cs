using Cairo;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace canjewelry.src.jewelry
{
    public class GemCuttingRecipeSystem
    {
        public class GemCuttingRecipeRegistry<T> : RecipeRegistryBase where T : IByteSerializable, new()
        {

            public List<GemCuttingRecipe> Recipes;

            public GemCuttingRecipeRegistry()
            {
                Recipes = new List<GemCuttingRecipe>();
            }

            public GemCuttingRecipeRegistry(List<GemCuttingRecipe> recipes)
            {
                Recipes = recipes;
            }

            public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
            {
                using MemoryStream input = new MemoryStream(data);
                BinaryReader reader = new BinaryReader(input);
                for (int i = 0; i < quantity; i++)
                {
                    GemCuttingRecipe item = new GemCuttingRecipe();
                    item.FromBytes(reader, resolver);
                    Recipes.Add(item);
                }
            }

            public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
            {
                quantity = Recipes.Count;
                using MemoryStream memoryStream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(memoryStream);
                foreach (GemCuttingRecipe recipe in Recipes)
                {
                    recipe.ToBytes(writer);
                }

                data = memoryStream.ToArray();
            }
        }

        public class PotionCauldronRecipeLoader : RecipeLoader
        {
            public override double ExecuteOrder()
            {
                return 100.0;
            }
            public override bool ShouldLoad(EnumAppSide side)
            {
                return true;
                return base.ShouldLoad(side);
            }
            public override void Start(ICoreAPI api)
            {
                base.Start(api);
                canjewelry.gemCuttingRecipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<GemCuttingRecipe>>("gemcuttingrecipes").Recipes;
            }
            public override void AssetsLoaded(ICoreAPI api)
            {
                LoadPotionCauldronRecipes(api);
                var c = 3;
            }
            public override void StartServerSide(ICoreServerAPI api)
            {
                this.api = api;
            }

            public override void Dispose()
            {
                base.Dispose();
            }

            public void LoadFoodRecipes()
            {
                this.LoadPotionCauldronRecipes(api);
            }

            public void LoadFoodRecipesClient(IClientPlayer byPlayer)
            {
                capi.Event.RegisterCallback((dt =>
                {
                    this.LoadPotionCauldronRecipes(capi);
                }
                ), 30 * 1000);

            }

            public void LoadPotionCauldronRecipes(ICoreAPI api)
            {
                Dictionary<AssetLocation, JToken> many = null;
                if (api.Side == EnumAppSide.Server)
                {
                    many = api.Assets.GetMany<JToken>(api.Logger, "recipes/gemcutting", null);
                }
                else
                {
                    return;
                }
                int num = 0;
                foreach (KeyValuePair<AssetLocation, JToken> keyValuePair in many)
                {
                    GemCuttingRecipe potionCauldronRecipe = keyValuePair.Value.ToObject<GemCuttingRecipe>();
                    bool flag2 = !potionCauldronRecipe.Enabled;
                    if (flag2)
                    {
                        continue;
                    }
                    GemCuttingRecipe potionCauldronRecipe2 = potionCauldronRecipe;
                    Dictionary<string, string[]> nameToCodeMapping = potionCauldronRecipe.GetNameToCodeMapping(api.World);
                    if (nameToCodeMapping.Count > 0)
                    {
                        List<GemCuttingRecipe> subRecipes = new List<GemCuttingRecipe>();
                        int qCombs = 0;
                        bool first = true;
                        foreach (KeyValuePair<string, string[]> val2 in nameToCodeMapping)
                        {
                            if (first)
                            {
                                qCombs = val2.Value.Length;
                            }
                            else
                            {
                                qCombs *= val2.Value.Length;
                            }
                            first = false;
                        }
                        first = true;
                        foreach (KeyValuePair<string, string[]> val3 in nameToCodeMapping)
                        {
                            string variantCode = val3.Key;
                            string[] variants = val3.Value;
                            for (int i = 0; i < qCombs; i++)
                            {
                                GemCuttingRecipe rec;
                                if (first)
                                {
                                    subRecipes.Add(rec = potionCauldronRecipe2.Clone());
                                }
                                else
                                {
                                    rec = subRecipes[i];
                                }
                                if (rec.Ingredients != null)
                                {
                                    foreach (IRecipeIngredient ingred in rec.Ingredients)
                                    {
                                        if (ingred.Name == variantCode)
                                        {
                                            ingred.Code = ingred.Code.CopyWithPath(ingred.Code.Path.Replace("*", variants[i % variants.Length]));
                                        }
                                    }
                                }
                                rec.Output.FillPlaceHolder(val3.Key, variants[i % variants.Length]);
                            }
                            first = false;
                        }
                        if (subRecipes.Count == 0)
                        {
                            this.api.World.Logger.Warning("{1} file {0} make uses of wildcards, but no blocks or item matching those wildcards were found.", new object[]
                            {
                        
                            });
                        }
                        foreach (GemCuttingRecipe subRecipe in subRecipes)
                        {
                            if (!subRecipe.Resolve(api.World, "gem cutting"))
                            {
                                //quantityIgnored++;
                                continue;
                            }
                            subRecipe.RecipeId = canjewelry.gemCuttingRecipes.Count() + 1;
                            canjewelry.gemCuttingRecipes.Add(subRecipe);
                            //RegisterMethod(subRecipe);
                            //quantityRegistered++;
                        }
                    }

                }
                ////////////////////////////////////////////////
                /*if (api.Side != EnumAppSide.Server)
                {
                    return;
                }
                Dictionary<AssetLocation, JToken> many = null;
                if (api.Side == EnumAppSide.Server)
                {
                    many = api.Assets.GetMany<JToken>(api.Logger, "recipes/gemcutting", null);
                }
                int num = 0;
                foreach (KeyValuePair<AssetLocation, JToken> keyValuePair in many)
                {
                    bool flag = keyValuePair.Value is JObject;
                    if (flag)
                    {
                        GemCuttingRecipe potionCauldronRecipe = keyValuePair.Value.ToObject<GemCuttingRecipe>();
                        bool flag2 = !potionCauldronRecipe.Enabled;
                        if (flag2)
                        {
                            continue;
                        }
                        GemCuttingRecipe potionCauldronRecipe2 = potionCauldronRecipe;
                        IWorldAccessor world = api.World;
                        string str = "mixing recipe ";
                        AssetLocation key = keyValuePair.Key;
                        potionCauldronRecipe2.Resolve(world, str + ((key != null) ? key.ToString() : null));

                        canjewelry.gemCuttingRecipes.Add(potionCauldronRecipe);
                        num++;
                    }
                    bool flag3 = keyValuePair.Value is JArray;
                    if (flag3)
                    {
                        foreach (JToken jtoken in (keyValuePair.Value as JArray))
                        {
                            GemCuttingRecipe potionCauldronRecipe3 = jtoken.ToObject<GemCuttingRecipe>();
                            bool flag4 = !potionCauldronRecipe3.Enabled;
                            if (!flag4)
                            {
                                GemCuttingRecipe potionCauldronRecipe4 = potionCauldronRecipe3;
                                IWorldAccessor world2 = api.World;
                                string str2 = "mixing recipe ";
                                AssetLocation key2 = keyValuePair.Key;
                                potionCauldronRecipe4.Resolve(world2, str2 + ((key2 != null) ? key2.ToString() : null));
                                canjewelry.gemCuttingRecipes.Add(potionCauldronRecipe3);
                                num++;
                            }
                        }
                    }
                }
                api.World.Logger.Event("{0} gem cutting recipes loaded", new object[]
                {
                    num
                });*/
            }

            public ICoreServerAPI api;
            public ICoreClientAPI capi;
        }
    }
}
