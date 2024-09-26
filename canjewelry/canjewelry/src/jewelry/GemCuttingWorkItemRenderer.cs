using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using canjewelry.src.be;
using canjewelry.src.items;

namespace canjewelry.src.jewelry
{
    public class GemCuttingWorkItemRenderer : IRenderer, IDisposable
    {
        private ICoreClientAPI api;

        private BlockPos pos;

        private MeshRef workItemMeshRef;

        private MeshRef recipeOutlineMeshRef;

        private ItemStack ingot;

        private int texId;

        private Vec4f outLineColorMul = new Vec4f(1f, 1f, 1f, 1f);

        protected Matrixf ModelMat = new Matrixf();

        private SurvivalCoreSystem coreMod;

        private BlockEntityGemCuttingTable beGemCuttingTable;

        private Vec4f glowRgb = new Vec4f();

        protected Vec3f origin = new Vec3f(0f, 0f, 0f);

        public double RenderOrder => 0.5;

        public int RenderRange => 24;

        public GemCuttingWorkItemRenderer(BlockEntityGemCuttingTable beGemCuttingTable, BlockPos pos, ICoreClientAPI capi)
        {
            this.pos = pos;
            api = capi;
            this.beGemCuttingTable = beGemCuttingTable;
            coreMod = capi.ModLoader.GetModSystem<SurvivalCoreSystem>();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (workItemMeshRef == null)
            {
                return;
            }

            if (stage == EnumRenderStage.AfterFinalComposition)
            {
                if (api.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is CANItemGemChisel)
                {
                    RenderRecipeOutLine();
                }

                return;
            }

            IRenderAPI render = api.Render;
            IClientWorldAccessor world = api.World;
            Vec3d cameraPos = world.Player.Entity.CameraPos;
            int num = (int)ingot.Collectible.GetTemperature(api.World, ingot);
            Vec4f lightRGBs = world.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
            int num2 = GameMath.Clamp((num - 550) / 2, 0, 255);
            float[] incandescenceColorAsColor4f = ColorUtil.GetIncandescenceColorAsColor4f(num);
            glowRgb.R = incandescenceColorAsColor4f[0];
            glowRgb.G = incandescenceColorAsColor4f[1];
            glowRgb.B = incandescenceColorAsColor4f[2];
            glowRgb.A = (float)num2 / 255f;
            render.GlDisableCullFace();
            IShaderProgram anvilShaderProg = coreMod.anvilShaderProg;
            anvilShaderProg.Use();
            render.BindTexture2d(texId);
            anvilShaderProg.Uniform("rgbaAmbientIn", render.AmbientColor);
            anvilShaderProg.Uniform("rgbaFogIn", render.FogColor);
            anvilShaderProg.Uniform("fogMinIn", render.FogMin);
            anvilShaderProg.Uniform("dontWarpVertices", 0);
            anvilShaderProg.Uniform("addRenderFlags", 0);
            anvilShaderProg.Uniform("fogDensityIn", render.FogDensity);
            anvilShaderProg.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
            anvilShaderProg.Uniform("rgbaLightIn", lightRGBs);
            anvilShaderProg.Uniform("rgbaGlowIn", glowRgb);
            anvilShaderProg.Uniform("extraGlow", num2);
            anvilShaderProg.UniformMatrix("modelMatrix", ModelMat.Identity().Translate((double)pos.X - cameraPos.X, (double)pos.Y - cameraPos.Y, (double)pos.Z - cameraPos.Z).Values);
            anvilShaderProg.UniformMatrix("viewMatrix", render.CameraMatrixOriginf);
            anvilShaderProg.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            render.RenderMesh(workItemMeshRef);
            anvilShaderProg.UniformMatrix("modelMatrix", render.CurrentModelviewMatrix);
            anvilShaderProg.Stop();
        }

        private void RenderRecipeOutLine()
        {
            if (recipeOutlineMeshRef != null && !api.HideGuis)
            {
                IRenderAPI render = api.Render;
                IClientWorldAccessor world = api.World;
                EntityPos entityPos = world.Player.Entity.Pos;
                Vec3d cameraPos = world.Player.Entity.CameraPos;
                ModelMat.Set(render.CameraMatrixOriginf).Translate((double)pos.X - cameraPos.X, (double)pos.Y - cameraPos.Y, (double)pos.Z - cameraPos.Z);
                outLineColorMul.A = 1f - GameMath.Clamp((float)Math.Sqrt(entityPos.SquareDistanceTo(pos.X, pos.Y, pos.Z)) / 5f - 1f, 0f, 1f);
                float num2 = (render.LineWidth = 2f * api.Settings.Float["wireframethickness"]);
                render.GLEnableDepthTest();
                render.GlToggleBlend(blend: true);
                IShaderProgram engineShader = render.GetEngineShader(EnumShaderProgram.Wireframe);
                engineShader.Use();
                engineShader.Uniform("origin", origin);
                engineShader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
                engineShader.UniformMatrix("modelViewMatrix", ModelMat.Values);
                engineShader.Uniform("colorIn", outLineColorMul);
                render.RenderMesh(recipeOutlineMeshRef);
                engineShader.Stop();
                if (num2 != 1.6f)
                {
                    render.LineWidth = 1.6f;
                }

                render.GLDepthMask(on: false);
            }
        }

        public void RegenMesh(ItemStack workitemStack, byte[,,] voxels, bool[,,] recipeToOutlineVoxels)
        {
            workItemMeshRef?.Dispose();
            workItemMeshRef = null;
            ingot = workitemStack;
            if (workitemStack != null)
            {
                ObjectCacheUtil.Delete(api, workitemStack.Attributes.GetInt("meshRefId").ToString() ?? "");
                workitemStack.Attributes.RemoveAttribute("meshRefId");
                if (recipeToOutlineVoxels != null)
                {
                    RegenOutlineMesh(recipeToOutlineVoxels, voxels);
                }

                MeshData data = CANItemGemCuttingWorkItem.GenMesh(api, workitemStack, voxels, out texId);
                workItemMeshRef = api.Render.UploadMesh(data);
            }
        }

        private void RegenOutlineMesh(bool[,,] recipeToOutlineVoxels, byte[,,] voxels)
        {
            MeshData meshData = new MeshData(24, 36, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            meshData.SetMode(EnumDrawMode.Lines);
            int color = api.ColorPreset.GetColor("anvilColorGreen");
            int color2 = api.ColorPreset.GetColor("anvilColorRed");
            MeshData cube = LineMeshUtil.GetCube(color);
            MeshData cube2 = LineMeshUtil.GetCube(color2);
            for (int i = 0; i < cube.xyz.Length; i++)
            {
                cube.xyz[i] = cube.xyz[i] / 32f + 1f / 32f;
                cube2.xyz[i] = cube2.xyz[i] / 32f + 1f / 32f;
            }

            MeshData meshData2 = cube.Clone();
            int length = recipeToOutlineVoxels.GetLength(1);
            for (int j = 0; j < 16; j++)
            {
                for (int k = 0; k < 14; k++)
                {
                    for (int l = 0; l < 16; l++)
                    {
                        bool flag = k < length && recipeToOutlineVoxels[j, k, l];
                        be.EnumVoxelMaterial enumVoxelMaterial = (be.EnumVoxelMaterial)voxels[j, k, l];
                        if ((!flag || enumVoxelMaterial != be.EnumVoxelMaterial.Metal) && (flag || enumVoxelMaterial != 0))
                        {
                            float num = (float)j / 16f;
                            float num2 = 0.625f + (float)k / 16f;
                            float num3 = (float)l / 16f;
                            for (int m = 0; m < cube.xyz.Length; m += 3)
                            {
                                meshData2.xyz[m] = num + cube.xyz[m];
                                meshData2.xyz[m + 1] = num2 + cube.xyz[m + 1];
                                meshData2.xyz[m + 2] = num3 + cube.xyz[m + 2];
                            }

                            meshData2.Rgba = ((flag && enumVoxelMaterial == be.EnumVoxelMaterial.Empty) ? cube.Rgba : cube2.Rgba);
                            meshData.AddMeshData(meshData2);
                        }
                    }
                }
            }
            meshData.Translate(0, -0.5f, 0);
            recipeOutlineMeshRef?.Dispose();
            recipeOutlineMeshRef = null;
            if (meshData.VerticesCount > 0)
            {
                recipeOutlineMeshRef = api.Render.UploadMesh(meshData);
            }
        }

        public void Dispose()
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            api.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
            recipeOutlineMeshRef?.Dispose();
            workItemMeshRef?.Dispose();
        }
    }
}
