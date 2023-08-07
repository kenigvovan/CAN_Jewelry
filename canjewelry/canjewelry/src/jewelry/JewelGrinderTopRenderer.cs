using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace canjewelry.src.jewelry
{
    public class JewelGrinderTopRenderer : IRenderer, IDisposable
    {
        internal bool ShouldRender;

        internal bool ShouldRotateManual;

        internal bool ShouldRotateAutomated;

        public BEBehaviorMPConsumer mechPowerPart;

        private ICoreClientAPI api;

        private BlockPos pos;

        public MeshRef meshref;

        public Matrixf ModelMat = new Matrixf();

        public float AngleRad;
        private BEJewelGrinder be;

        public double RenderOrder => 0.5;

        public int RenderRange => 24;

        public JewelGrinderTopRenderer(ICoreClientAPI coreClientAPI, BlockPos pos, MeshData mesh)
        {
            api = coreClientAPI;
            this.pos = pos;
            meshref = coreClientAPI.Render.UploadMesh(mesh);
        }
        public JewelGrinderTopRenderer(
          ICoreClientAPI coreClientAPI,
          BEJewelGrinder be,
          BlockPos pos,
          MeshData mesh)
        {
            this.api = coreClientAPI;
            this.pos = pos;
            this.be = be;
            this.meshref = coreClientAPI.Render.UploadMesh(mesh);
        }
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshref != null && ShouldRender)
            {
                IRenderAPI render = api.Render;
                Vec3d cameraPos = api.World.Player.Entity.CameraPos;
                render.GlDisableCullFace();
                render.GlToggleBlend(blend: true);
                IStandardShaderProgram standardShaderProgram = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
                standardShaderProgram.Tex2D = api.BlockTextureAtlas.AtlasTextures[0].TextureId;
                standardShaderProgram.ModelMatrix = ModelMat.Identity().Translate((double)pos.X - cameraPos.X, (double)pos.Y - cameraPos.Y, (double)pos.Z - cameraPos.Z).Translate(0.5f, 0f, 0.5f)
                    .RotateY(AngleRad)
                    .Translate(-0.5f, -0f, -0.5f)
                    .Scale(1f,1f,1f)
                    .Values;
                standardShaderProgram.ViewMatrix = render.CameraMatrixOriginf;
                standardShaderProgram.ProjectionMatrix = render.CurrentProjectionMatrix;
                render.RenderMesh(meshref);
                standardShaderProgram.Stop();
               /* if (ShouldRotateManual)
                {
                    AngleRad += deltaTime * 40f * ((float)Math.PI / 180f);
                }*/

                if (ShouldRotateAutomated)
                {
                    AngleRad = mechPowerPart.AngleRad;
                }
            }
        }

        public void Dispose()
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            meshref.Dispose();
        }
    }
}
