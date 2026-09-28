using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.items.resource
{
    public class CANCutGemItem: Item, IContainedMeshSource
    {
        /// <summary>The cut this gem was given, round until the cutting table says otherwise.</summary>
        private static string CuttingTypeOf(ItemStack stack)
        {
            ITreeAttribute tree = stack?.Attributes?.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            return tree?.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND)
                   ?? CANJWConstants.CUTTING_ROUND;
        }

        /// <summary>
        /// The texture source for one mesh build: this gem's colour, in whichever atlas the mesh is
        /// going into — the item atlas in hand, the block atlas inside a display case.
        /// </summary>
        private render.CANTexSource TexSource(ITextureAtlasAPI atlas)
        {
            string gemBase = Variant["gemtype"];
            if (!canjewelry.gems_textures.TryGetValue(gemBase, out string assetPath))
            {
                canjewelry.gems_textures.TryGetValue(CANJWConstants.FALLBACK_GEM_TYPE, out assetPath);
            }

            var source = new render.CANTexSource(api as ICoreClientAPI, atlas, Textures, "cut gem " + Code);
            AssetLocation texture = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;
            if (texture != null) source.Overrides["gem"] = texture;
            return source;
        }

        /// <summary>
        /// The uploaded meshes of cut gems, one per gem and cut, shared by every stack of them.
        /// Named for what it holds rather than for the class this was copied from.
        /// </summary>
        private Dictionary<string, MultiTextureMeshRef> meshrefs
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate(api, "canjewelry:cutGemMeshRefs",
                    () => new Dictionary<string, MultiTextureMeshRef>());
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            // Gpu meshes, so they have to be handed back. Nothing did that before and the dictionary
            // they sat in was never emptied either.
            if (api is ICoreClientAPI)
            {
                var refs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, "canjewelry:cutGemMeshRefs");
                if (refs != null)
                {
                    foreach (MultiTextureMeshRef meshRef in refs.Values) meshRef?.Dispose();
                    refs.Clear();
                    ObjectCacheUtil.Delete(api, "canjewelry:cutGemMeshRefs");
                }
            }

            base.OnUnloaded(api);
        }
        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
            => GenMesh(slot?.Itemstack, targetAtlas, atBlockPos);
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (target == EnumItemRenderTarget.HandTp)
            {
                /* bool sneak = capi.World.Player.Entity.Controls.Sneak;
                 this.curOffY += ((sneak ? 0.4f : this.offY) - this.curOffY) * renderinfo.dt * 8f;
                 renderinfo.Transform.Translation.X = this.curOffY;
                 renderinfo.Transform.Translation.Y = this.curOffY * 1.2f;
                 renderinfo.Transform.Translation.Z = this.curOffY * 1.2f;*/
            }
            // Keyed by what actually makes the mesh different - this gem and its cut - rather than
            // by a counter. The id used to be "however many are in the dictionary, plus one", so
            // every stack ever rendered added an entry that nothing disposed of and nothing reused.
            string key = Code.ToShortString() + "-" + CuttingTypeOf(itemstack);
            if (!meshrefs.TryGetValue(key, out MultiTextureMeshRef modelref) || modelref.Disposed)
            {
                MeshData mesh = GenMesh(itemstack, capi.ItemTextureAtlas, null);
                if (mesh == null)
                {
                    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                    return;
                }

                modelref = meshrefs[key] = capi.Render.UploadMultiTextureMesh(mesh);
            }

            renderinfo.ModelRef = modelref;
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        /// <summary>
        /// The gem as its cut shapes it, textured with its own colour. This and the slot overload
        /// above were two copies of the same body, differing only in how they reached the stack.
        /// </summary>
        public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            if (itemstack == null) return null;

            var capi = api as ICoreClientAPI;
            string shapePath = "canjewelry:shapes/item/gem/cut/" + Variant["quality"]
                               + "/gem_" + CuttingTypeOf(itemstack) + ".json";

            // A cut with no shape of its own is a mod or a config naming something that is not
            // there; the gem is then left to be drawn by its own item shape rather than crashing
            // inside the tesselator.
            Shape shapeCutGem = capi.Assets.TryGet(shapePath)?.ToObject<Shape>();
            if (shapeCutGem == null)
            {
                capi.World.Logger.Warning("[canjewelry] cut gem {0}: no shape at {1}", Code, shapePath);
                return null;
            }

            capi.Tesselator.TesselateShape("cut gem shape", shapeCutGem, out MeshData meshCutGem,
                TexSource(targetAtlas), null, 0, 0, 0, null, null);
            return meshCutGem;
        }
        public override string GetHeldItemName(ItemStack itemStack)
        {
            string bb = base.GetHeldItemName(itemStack);
            if (itemStack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE))
            {
                ITreeAttribute tree = itemStack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
                bb += " [" + Lang.Get("canjewelry:cut-gem-cutting-type-" + tree.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND)) + "]";
                //bb += "[" +  +"]";
            }
            return bb;
        }
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (!canjewelry.config.TurnOffBuffs)
            {
                if (inSlot.Itemstack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE))
                {
                    ITreeAttribute tree = inSlot.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
                    string[] buffNames = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                    float[] buffValues = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                    for (int i = 0; i < buffNames.Length; i++)
                    {
                        if (buffNames[i].Equals("maxhealthExtraPoints"))
                        {
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffNames[i])).Append(" +" + buffValues[i].ToString());
                            dsc.AppendLine();
                        }
                        else
                        {
                            if (canjewelry.config.gems_buffs.TryGetValue(buffNames[i], out var buffValuesDict))
                            {
                                dsc.Append(Lang.Get("canjewelry:buff-name-" + buffNames[i]));
                                dsc.Append(buffValues[i] * 100 > 0 ? " +" + Math.Round(buffValues[i] * 100, 3) + "%" : " " + Math.Round(buffValues[i] * 100, 3) + "%");
                                dsc.AppendLine();
                            }
                        }
                    }
                }
                else if (inSlot.Itemstack.Collectible.Attributes.KeyExists("canGemTypeToAttribute"))
                {
                    string buffName = inSlot.Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString();
                    if (buffName.Equals("maxhealthExtraPoints"))
                    {
                        if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict))
                        {
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName)).Append(" +" + buffValuesDict[inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
                        }
                    }
                    else if (buffName.Equals("candurability"))
                    {
                        if (canjewelry.config.gems_buffs.TryGetValue(buffName, out var buffValuesDict))
                        {
                            float buffValue = buffValuesDict[inSlot.Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()] * 100;
                            dsc.Append(Lang.Get("canjewelry:buff-name-" + buffName));
                            dsc.Append(buffValue > 0 ? " +" + Math.Round(buffValue) + "%" : " " + Math.Round(buffValue) + "%");
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
            }
            if (inSlot?.Itemstack?.Attributes.HasAttribute("cangrindlayerinfo") ?? false)
            {
                var cutGemTree = inSlot.Itemstack.Attributes.GetTreeAttribute("cangrindlayerinfo");
                if (cutGemTree == null)
                {
                    return;
                }
                if (cutGemTree.HasAttribute("grindtype"))
                {
                    dsc.AppendLine(Lang.Get("canjewelry:grinding-process-stage", cutGemTree.GetInt("grindtype")));
                }
                if (cutGemTree.HasAttribute("grindcounter"))
                {
                    dsc.AppendLine(Lang.Get("canjewelry:grinding-process-percent", string.Format("{0:0}", (((20 - cutGemTree.GetInt("grindcounter")) / 20.0) * 100)).ToString()));
                }
            }
        }
        /// <summary>
        /// Names the mesh a holder caches for this gem. The cut is read from the gem's own subtree,
        /// where it lives — it used to be read off the root of the stack, where it never is, so the
        /// key was the same string for every cut and one cached mesh served them all.
        /// </summary>
        public string GetMeshCacheKey(ItemSlot slot)
        {
            return Code.ToShortString() + "-" + CuttingTypeOf(slot?.Itemstack);
        }
    }
}
