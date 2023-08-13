using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.items
{
    public class CANItemWearable : Item, IContainedMeshSource, ITexPositionSource
    {
        public virtual TextureAtlasPosition this[string textureCode] => throw new NotImplementedException();

        public virtual Size2i AtlasSize => throw new NotImplementedException();

        public virtual MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            throw new NotImplementedException();
        }

        public virtual string GetMeshCacheKey(ItemStack itemstack)
        {
            throw new NotImplementedException();
        }
    }
}
