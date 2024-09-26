using canjewelry.src.be;
using canjewelry.src.jewelry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.items
{
    public class CANItemGemCuttingWorkItem : Item, IGemCuttingWorkable
    {
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (!itemstack.Attributes.HasAttribute("voxels"))
            {
                CachedMeshRef ccmr = ObjectCacheUtil.GetOrCreate<CachedMeshRef>(capi, "clearWorkItem" + this.Variant["metal"], delegate
                {
                    byte[,,] voxels = new byte[16, 14, 16];
                    ItemIngot.CreateVoxelsFromIngot(capi, ref voxels, false);
                    int textureid;
                    MeshData mesh = CANItemGemCuttingWorkItem.GenMesh(capi, itemstack, voxels, out textureid);
                    return new CachedMeshRef
                    {
                        meshref = capi.Render.UploadMultiTextureMesh(mesh),
                        TextureId = textureid
                    };
                });
                renderinfo.ModelRef = ccmr.meshref;
                renderinfo.TextureId = ccmr.TextureId;
                base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                return;
            }
            int meshrefId = itemstack.Attributes.GetInt("meshRefId", -1);
            if (meshrefId == -1)
            {
                meshrefId = ++CANItemGemCuttingWorkItem.nextMeshRefId;
            }
            CachedMeshRef cmr = ObjectCacheUtil.GetOrCreate<CachedMeshRef>(capi, meshrefId.ToString() ?? "", delegate
            {
                byte[,,] voxels = CANItemGemCuttingWorkItem.GetVoxels(itemstack);
                int textureid;
                MeshData mesh = CANItemGemCuttingWorkItem.GenMesh(capi, itemstack, voxels, out textureid);
                return new CachedMeshRef
                {
                    meshref = capi.Render.UploadMultiTextureMesh(mesh),
                    TextureId = textureid
                };
            });
            renderinfo.ModelRef = cmr.meshref;
            renderinfo.TextureId = cmr.TextureId;
            itemstack.Attributes.SetInt("meshRefId", meshrefId);
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        public static MeshData GenMesh(ICoreClientAPI capi, ItemStack workitemStack, byte[,,] voxels, out int textureId)
        {
            textureId = 0;
            if (workitemStack == null)
            {
                return null;
            }
            MeshData workItemMesh = new MeshData(24, 36, false, true, true, true);
            workItemMesh.CustomBytes = new CustomMeshDataPartByte
            {
                Conversion = DataConversion.NormalizedFloat,
                Count = workItemMesh.VerticesCount,
                InterleaveSizes = new int[]
                {
                    1
                },
                Instanced = false,
                InterleaveOffsets = new int[1],
                InterleaveStride = 1,
                Values = new byte[workItemMesh.VerticesCount]
            };
            TextureAtlasPosition tposSlag;
            TextureAtlasPosition tposMetal;

            string gemBase = workitemStack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "diamond");
            if (!canjewelry.gems_textures.TryGetValue(gemBase, out string assetPath))
            {
                canjewelry.gems_textures.TryGetValue("diamond", out assetPath);
            }
            AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;
            tposMetal = capi.ItemTextureAtlas.GetPosition(capi.World.GetItem(new AssetLocation("canjewelry:gem-rough-normal-" + gemBase)), "gem");
            tposSlag = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("game:anvil-copper")), "ironbloom", false); ;
            /*if (workitemStack.Collectible.FirstCodePart(0) == "ironbloom")
            {
                tposSlag = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("anvil-copper")), "ironbloom", false);
                tposMetal = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("ingotpile")), "iron", false);
            }
            else
            {
                tposMetal = capi.BlockTextureAtlas.GetPosition(capi.World.GetBlock(new AssetLocation("ingotpile")), workitemStack.Collectible.Variant["metal"], false);
                tposSlag = tposMetal;
            }*/
            MeshData metalVoxelMesh = CubeMeshUtil.GetCubeOnlyScaleXyz(0.03125f, 0.03125f, new Vec3f(0.03125f, 0.03125f, 0.03125f));
            CubeMeshUtil.SetXyzFacesAndPacketNormals(metalVoxelMesh);
            metalVoxelMesh.CustomBytes = new CustomMeshDataPartByte
            {
                Conversion = DataConversion.NormalizedFloat,
                Count = metalVoxelMesh.VerticesCount,
                Values = new byte[metalVoxelMesh.VerticesCount]
            };
            textureId = tposMetal.atlasTextureId;
            for (int i = 0; i < 6; i++)
            {
                metalVoxelMesh.AddTextureId(textureId);
            }
            metalVoxelMesh.XyzFaces = (byte[])CubeMeshUtil.CubeFaceIndices.Clone();
            metalVoxelMesh.XyzFacesCount = 6;
            metalVoxelMesh.Rgba.Fill(byte.MaxValue);
            MeshData slagVoxelMesh = metalVoxelMesh.Clone();
            for (int j = 0; j < metalVoxelMesh.Uv.Length; j++)
            {
                if (j % 2 > 0)
                {
                    metalVoxelMesh.Uv[j] = tposMetal.y1 + metalVoxelMesh.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Height;
                    slagVoxelMesh.Uv[j] = tposSlag.y1 + slagVoxelMesh.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Height;
                }
                else
                {
                    metalVoxelMesh.Uv[j] = tposMetal.x1 + metalVoxelMesh.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Width;
                    slagVoxelMesh.Uv[j] = tposSlag.x1 + slagVoxelMesh.Uv[j] * 2f / (float)capi.BlockTextureAtlas.Size.Width;
                }
            }
            MeshData metVoxOffset = metalVoxelMesh.Clone();
            MeshData slagVoxOffset = slagVoxelMesh.Clone();
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 6; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        be.EnumVoxelMaterial mat = (be.EnumVoxelMaterial)voxels[x, y, z];
                        if (mat != be.EnumVoxelMaterial.Empty)
                        {
                            float px = (float)x / 16f;
                            float py = 0.625f + (float)y / 16f;
                            float pz = (float)z / 16f;
                            MeshData mesh = (mat == be.EnumVoxelMaterial.Metal) ? metalVoxelMesh : slagVoxelMesh;
                            MeshData meshVoxOffset = (mat == be.EnumVoxelMaterial.Metal) ? metVoxOffset : slagVoxOffset;
                            for (int k = 0; k < mesh.xyz.Length; k += 3)
                            {
                                meshVoxOffset.xyz[k] = px + mesh.xyz[k];
                                meshVoxOffset.xyz[k + 1] = py + mesh.xyz[k + 1];
                                meshVoxOffset.xyz[k + 2] = pz + mesh.xyz[k + 2];
                            }
                            float textureSize = 32f / (float)capi.BlockTextureAtlas.Size.Width;
                            float offsetX = px * textureSize;
                            float offsetY = py * 32f / (float)capi.BlockTextureAtlas.Size.Width;
                            float offsetZ = pz * textureSize;
                            for (int l = 0; l < mesh.Uv.Length; l += 2)
                            {
                                meshVoxOffset.Uv[l] = mesh.Uv[l] + GameMath.Mod(offsetX + offsetY, textureSize);
                                meshVoxOffset.Uv[l + 1] = mesh.Uv[l + 1] + GameMath.Mod(offsetZ + offsetY, textureSize);
                            }
                            for (int m = 0; m < meshVoxOffset.CustomBytes.Values.Length; m++)
                            {
                                byte glowSub = (byte)GameMath.Clamp(10 * (Math.Abs(x - 8) + Math.Abs(z - 8) + Math.Abs(y - 2)), 100, 250);
                                meshVoxOffset.CustomBytes.Values[m] = ((byte)((mat == be.EnumVoxelMaterial.Metal) ? 0 : glowSub));
                            }
                            workItemMesh.AddMeshData(meshVoxOffset);
                        }
                    }
                }
            }
            workItemMesh.Translate(0, -0.5f, 0);
            return workItemMesh;
        }
        public static byte[,,] GetVoxels(ItemStack workitemStack)
        {
            return BlockEntityGemCuttingTable.deserializeVoxels(workitemStack.Attributes.GetBytes("voxels", null));
        }
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            int recipeId = inSlot.Itemstack.Attributes.GetInt("selectedRecipeId", 0);
            GemCuttingRecipe recipe = canjewelry.gemCuttingRecipes.FirstOrDefault((GemCuttingRecipe r) => r.RecipeId == recipeId);
            if (recipe == null)
            {
                dsc.AppendLine("Unknown work item");
                return;
            }
            dsc.AppendLine(Lang.Get("Unfinished {0}", new object[]
            {
                recipe.Output.ResolvedItemstack.GetName()
            }));
        }
        public List<GemCuttingRecipe> GetMatchingRecipes(ItemStack stack)
        {
            stack = this.GetBaseMaterial(stack);
            return (from r in canjewelry.gemCuttingRecipes
                    where r.Ingredient.SatisfiesAsIngredient(stack, true)
                    orderby r.Output.ResolvedItemstack.Collectible.Code
                    select r).ToList<GemCuttingRecipe>();
        }
        public bool CanWork(ItemStack stack)
        {
            float temperature = stack.Collectible.GetTemperature(api.World, stack);
            float meltingpoint = stack.Collectible.GetMeltingPoint(api.World, null, new DummySlot(stack));

            if (stack.Collectible.Attributes?["workableTemperature"].Exists == true)
            {
                return stack.Collectible.Attributes["workableTemperature"].AsFloat(meltingpoint / 2) <= temperature;
            }

            return temperature >= meltingpoint / 2;
        }
        public ItemStack TryPlaceOn(ItemStack stack, BlockEntityGemCuttingTable beAnvil)
        {
            if (beAnvil.WorkItemStack != null)
            {
                return null;
            }
            try
            {
                beAnvil.Voxels = BlockEntityGemCuttingTable.deserializeVoxels(stack.Attributes.GetBytes("voxels", null));
                beAnvil.SelectedRecipeId = stack.Attributes.GetInt("selectedRecipeId", 0);
            }
            catch (Exception)
            {
            }
            return stack.Clone();
        }
        public ItemStack GetBaseMaterial(ItemStack stack)
        {
            Item item = api.World.GetItem(AssetLocation.Create("canjewelry:gem-rough-chipped-" + stack.Attributes.GetString("gemtype")));
            //Item item = api.World.GetItem(AssetLocation.Create("ingot-" + Variant["metal"], Attributes?["baseMaterialDomain"].AsString("game")));
            if (item == null)
            {
                throw new Exception(string.Format("Base material for {0} not found, there is no item with code 'ingot-{1}'", stack.Collectible.Code, Variant["metal"]));
            }
            return new ItemStack(item);
        }
        public EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
        {
            if (beAnvil.SelectedRecipe.Name.Path == "plate" || beAnvil.SelectedRecipe.Name.Path == "blistersteel")
            {
                return EnumHelveWorkableMode.TestSufficientVoxelsWorkable;
            }
            return EnumHelveWorkableMode.NotWorkable;
        }

        public int GetRequiredGemCuttingTableTier(ItemStack stack)
        {
            return 0;
        }

        private static int nextMeshRefId;
        public bool isBlisterSteel;
    }
}
