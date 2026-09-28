using System;
using System.Collections.Generic;
using System.Text;
using canjewelry.src.api;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.items
{
    public abstract class CANItemWearable : Item, IContainedMeshSource, ITexPositionSource, IWearableShapeSupplier, IAttachableToEntity
    {
        protected Shape nowTesselatingShape;
        public static AssetLocation NotVisTexture;

        protected ITextureAtlasAPI curAtlas;
        protected ICoreClientAPI capi;
        protected float offY;
        protected float curOffY;
        protected readonly Dictionary<string, AssetLocation> tmpTextures = new Dictionary<string, AssetLocation>();

        public StatModifiers StatModifers;
        public EnumCharacterDressType DressType { get; protected set; }
        public int RequiresBehindSlots { get; set; }
        public virtual Size2i AtlasSize => curAtlas.Size;

        protected abstract string MeshrefsCacheName { get; }

        /// <summary>
        /// Base set the core knows about. Content mods append their own through
        /// CANJewelryRegistry.RegisterMaterialAttributeKeys, and a single itemtype can override
        /// the whole list with the canMaterialAttrKeys attribute.
        /// </summary>
        public static readonly string[] BaseMaterialAttrKeys = { "metal", "loop", "carcassus", "construction" };

        /// <summary>
        /// Material keys for this item, in priority order: itemtype attribute, else the registry
        /// (which already starts with the base set).
        /// </summary>
        protected IReadOnlyList<string> MaterialAttrKeys
        {
            get
            {
                string[] fromJson = Attributes?[CANJewelryAttributes.MaterialAttrKeys]?.AsArray<string>(null);
                if (fromJson != null && fromJson.Length > 0) return fromJson;

                var fromRegistry = CANJewelryRegistry.MaterialAttributeKeys;
                return fromRegistry.Count > 0 ? fromRegistry : BaseMaterialAttrKeys;
            }
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            var materialAttrKeys = MaterialAttrKeys;
            string mat = null;
            foreach (string k in materialAttrKeys)
            {
                mat = itemStack.Attributes.GetString(k, null);
                if (mat != null) break;
            }
            if (mat == null)
            {
                var variant = itemStack.Item?.Variant;
                if (variant != null)
                {
                    foreach (string k in materialAttrKeys)
                    {
                        mat = variant[k];
                        if (mat != null) break;
                    }
                }
            }
            mat ??= "steel";
            return Lang.Get("canjewelry:itemname-" + Code.FirstCodePart())
                   + " (" + Lang.Get("game:material-" + mat) + ")";
        }

        protected Dictionary<int, MultiTextureMeshRef> meshrefs
            => ObjectCacheUtil.GetOrCreate(api, MeshrefsCacheName, () => new Dictionary<int, MultiTextureMeshRef>());

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.curOffY = (this.offY = this.FpHandTransform.Translation.Y);
            this.capi = api as ICoreClientAPI;

            AddAllTypesToCreativeInventory();

            string value = Attributes["clothescategory"].AsString();
            EnumCharacterDressType result = EnumCharacterDressType.Unknown;
            Enum.TryParse<EnumCharacterDressType>(value, ignoreCase: true, out result);
            DressType = result;

            JsonObject jsonObject = Attributes?["statModifiers"];
            if (jsonObject != null && jsonObject.Exists)
            {
                try { StatModifers = jsonObject.AsObject<StatModifiers>(); }
                catch (Exception ex)
                {
                    api.World.Logger.Error("Failed loading statModifiers for item/block {0}. Will ignore. Exception: {1}", Code, ex);
                    StatModifers = null;
                }
            }

            jsonObject = Attributes?["defaultProtLoss"];
            if (jsonObject != null && jsonObject.Exists)
            {
                try { jsonObject.AsObject<ProtectionModifiers>(); }
                catch (Exception ex2)
                {
                    api.World.Logger.Error("Failed loading defaultProtLoss for item/block {0}. Will ignore. Exception: {1}", Code, ex2);
                }
            }
        }

        protected virtual void AddAllTypesToCreativeInventory()
        {
            var vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);
            if (vg == null || !vg.TryGetValue("metal", out string[] metals)) return;

            var stacks = new List<JsonItemStack>();
            foreach (string metal in metals)
            {
                stacks.Add(GenJstack(string.Format("{{ metal: \"{0}\"}}", metal)));
            }
            this.CreativeInventoryStacks = new[]
            {
                new CreativeTabAndStackList
                {
                    Stacks = stacks.ToArray(),
                    Tabs = new[] { "general", "items", "canjewelry" }
                }
            };
        }

        protected virtual string JstackResolveLabel => Code.Path + " type";

        protected JsonItemStack GenJstack(string json)
        {
            var jstack = new JsonItemStack
            {
                Code = this.Code,
                Type = EnumItemClass.Item,
                Attributes = new JsonObject(JToken.Parse(json))
            };
            jstack.Resolve(this.api.World, JstackResolveLabel, true);
            return jstack;
        }

        protected TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            ICoreClientAPI clientApi = api as ICoreClientAPI;
            curAtlas.GetOrInsertTexture(texturePath, out _, out var texPos, delegate
            {
                IAsset asset = clientApi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null) return asset.ToBitmap(clientApi);
                clientApi.World.Logger.Warning("Item {0} defined texture {1}, not no such texture found.", Code, texturePath);
                return null;
            }, 0.1f);
            return texPos;
        }

        public virtual TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (this.tmpTextures.TryGetValue(textureCode, out var res))
                    return this.getOrCreateTexPos(res);

                AssetLocation value = null;
                if (textureCode == "metal" && Textures.TryGetValue("metal", out var metalTex))
                    value = metalTex.Base;

                if (Textures.TryGetValue(textureCode, out var value2))
                    value = value2.Baked.BakedName;

                if (value == null && Textures.TryGetValue("all", out value2))
                    value = value2.Baked.BakedName;

                if (value == null)
                    nowTesselatingShape?.Textures?.TryGetValue(textureCode, out value);

                if (value == null)
                {
                    api.World.Logger.Debug("[CANItemWearable] {0}: texture key '{1}' not found, falling back to AssetLocation('{1}')", Code, textureCode);
                    value = new AssetLocation(textureCode);
                }

                return getOrCreateTexPos(value);
            }
        }

        protected abstract void FillTextureDict(Dictionary<string, AssetLocation> dict, ItemStack itemStack);

        public virtual MeshData genMesh(ICoreClientAPI capi, ItemStack itemstack, ITexPositionSource texSource)
        {
            this.tmpTextures.Clear();
            this.FillTextureDict(tmpTextures, itemstack);
            JsonObject attributes = itemstack.Collectible.Attributes;
            EntityProperties entityType = capi.World.GetEntityType(new AssetLocation(attributes?["wearerEntityCode"].ToString() ?? "player"));
            Shape loadedShape = entityType.Client.LoadedShape;
            AssetLocation @base = entityType.Client.Shape.Base;
            Shape shape = new Shape
            {
                Elements = loadedShape.CloneElements(),
                Animations = loadedShape.CloneAnimations(),
                AnimationsByCrc32 = loadedShape.AnimationsByCrc32,
                JointsById = loadedShape.JointsById,
                TextureWidth = loadedShape.TextureWidth,
                TextureHeight = loadedShape.TextureHeight,
                Textures = null
            };
            MeshData modeldata;
            if (attributes["wearableInvShape"].Exists)
            {
                AssetLocation shapePath = new AssetLocation("shapes/" + attributes["wearableInvShape"]?.ToString() + ".json");
                Shape invShape = Vintagestory.API.Common.Shape.TryGet(capi, shapePath);
                capi.Tesselator.TesselateShape(itemstack.Collectible, invShape, out modeldata);
            }
            else
            {
                CompositeShape compositeShape = (attributes["attachShape"].Exists ? attributes["attachShape"].AsObject<CompositeShape>(null, itemstack.Collectible.Code.Domain) : ((itemstack.Class == EnumItemClass.Item) ? itemstack.Item.Shape : itemstack.Block.Shape));
                if (compositeShape == null)
                {
                    capi.World.Logger.Warning("Wearable shape {0} {1} does not define a shape through either the shape property or the attachShape Attribute. Item will be invisible.", itemstack.Class, itemstack.Collectible.Code);
                    return null;
                }

                AssetLocation assetLocation = compositeShape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
                Shape attachedShape = Vintagestory.API.Common.Shape.TryGet(capi, assetLocation);
                if (attachedShape == null)
                {
                    capi.World.Logger.Warning("Wearable shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Item will be invisible.", compositeShape.Base, itemstack.Class, itemstack.Collectible.Code, assetLocation);
                    return null;
                }

                shape.StepParentShape(attachedShape, assetLocation.ToShortString(), @base.ToShortString(), capi.Logger, delegate { });
                if (compositeShape.Overlays != null)
                {
                    foreach (var overlayShape in compositeShape.Overlays)
                    {
                        Shape overlay = Vintagestory.API.Common.Shape.TryGet(capi, overlayShape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"));
                        if (overlay == null)
                            capi.World.Logger.Warning("Wearable shape {0} overlay {4} defined in {1} {2} not found or errored, was supposed to be at {3}. Item will be invisible.", compositeShape.Base, itemstack.Class, itemstack.Collectible.Code, assetLocation, overlayShape.Base);
                        else
                            shape.StepParentShape(overlay, overlayShape.Base.ToShortString(), @base.ToShortString(), capi.Logger, delegate { });
                    }
                }

                nowTesselatingShape = shape;
                capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out modeldata, texSource, new Vec3f());
                nowTesselatingShape = null;
            }

            return modeldata;
        }

        public virtual MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
            => GenMesh(slot.Itemstack, targetAtlas, atBlockPos);

        public virtual MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos forBlockPos = null)
        {
            ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
            curAtlas = targetAtlas;
            if (targetAtlas == coreClientAPI.ItemTextureAtlas)
            {
                return genMesh(coreClientAPI, itemstack, this);
            }

            MeshData meshData = genMesh(coreClientAPI, itemstack, this);
            meshData.RenderPassesAndExtraBits.Fill(MeshDefaultRenderPass);
            return meshData;
        }

        protected virtual short MeshDefaultRenderPass => 1;

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (target == EnumItemRenderTarget.HandTp)
            {
                bool sneak = capi.World.Player.Entity.Controls.Sneak;
                this.curOffY += ((sneak ? 0.4f : this.offY) - this.curOffY) * renderinfo.dt * 8f;
                renderinfo.Transform.Translation.X = this.curOffY;
                renderinfo.Transform.Translation.Y = this.curOffY * 1.2f;
                renderinfo.Transform.Translation.Z = this.curOffY * 1.2f;
            }
            int meshrefid = itemstack.TempAttributes.GetInt("meshRefId", 0);
            if (meshrefid == 0 || !this.meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
            {
                int id = this.meshrefs.Count + 1;
                MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(this.GenMesh(itemstack, capi.ItemTextureAtlas));
                renderinfo.ModelRef = (this.meshrefs[id] = modelref);
                itemstack.TempAttributes.SetInt("meshRefId", id);
            }
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        public virtual Shape GetShape(ItemStack stack, Entity forEntity, string texturePrefixCode)
        {
            JsonObject attrObj = stack.Collectible.Attributes;
            CompositeShape compGearShape = (!attrObj["attachShape"].Exists)
                ? ((stack.Class == EnumItemClass.Item) ? stack.Item.Shape : stack.Block.Shape)
                : attrObj["attachShape"].AsObject<CompositeShape>(null, stack.Collectible.Code.Domain);

            AssetLocation shapePath = compGearShape.Base.CopyWithPath("shapes/" + compGearShape.Base.Path + ".json");
            Shape gearShape = Vintagestory.API.Common.Shape.TryGet(api, shapePath);
            if (gearShape == null)
            {
                api.World.Logger.Warning("Entity armor shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Armor piece will be invisible.",
                    compGearShape.Base, stack.Class, stack.Collectible.Code, shapePath);
                return null;
            }
            return gearShape;
        }

        public virtual void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict)
        {
            if (this.api.Side is EnumAppSide.Server) return;

            // Local dict on purpose: the item is a singleton shared by every stack, and nothing
            // reads the tmpTextures field after this call - it is only a texSource backing store
            // for genMesh. Filling it here would just leave one stack's textures lying around.
            var textures = new Dictionary<string, AssetLocation>();
            FillTextureDict(textures, stack);

            foreach (var texture in textures)
            {
                CompositeTexture ctex = new CompositeTexture { Base = texture.Value };
                AssetLocation armorTexLoc = texture.Value;
                int textureSubId;

                (this.api as ICoreClientAPI).EntityTextureAtlas.GetOrInsertTexture(armorTexLoc, out textureSubId, out _, () =>
                {
                    IAsset texAsset = this.capi.Assets.TryGet(armorTexLoc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                    return texAsset?.ToBitmap(capi);
                });

                ctex.Baked = new BakedCompositeTexture { BakedName = armorTexLoc, TextureSubId = textureSubId };
                intoDict[texture.Key] = ctex;
            }
        }

        public abstract string GetCategoryCode(ItemStack stack);

        public virtual CompositeShape GetAttachedShape(ItemStack stack, string slotCode) => this.Shape;
        public virtual string[] GetDisableElements(ItemStack stack) => null;
        public virtual string[] GetKeepElements(ItemStack stack) => null;
        public virtual string GetTexturePrefixCode(ItemStack stack) => GetMeshCacheKey(stack);
        public virtual bool IsAttachable(Entity toEntity, ItemStack itemStack) => true;

        public virtual string GetMeshCacheKey(ItemSlot slot) => GetMeshCacheKey(slot.Itemstack);
        public abstract string GetMeshCacheKey(ItemStack itemstack);

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if ((api as ICoreClientAPI).Settings.Bool["extendedDebugInfo"])
            {
                if (inSlot.Itemstack.Attributes.HasAttribute("clothescategory"))
                {
                    dsc.AppendLine(Lang.Get("Cloth Category: {0}", Lang.Get("canjewelry:clothcategory-" + inSlot.Itemstack.Attributes.GetString("clothescategory"))));
                }
                else if (DressType == EnumCharacterDressType.Unknown)
                {
                    dsc.AppendLine(Lang.Get("Cloth Category: Unknown"));
                }
                else
                {
                    dsc.AppendLine(Lang.Get("Cloth Category: {0}", Lang.Get("canjewelry:clothcategory-" + inSlot.Itemstack.ItemAttributes["clothescategory"].AsString())));
                }
            }
        }
    }
}
