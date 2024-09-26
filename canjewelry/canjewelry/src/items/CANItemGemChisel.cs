using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VSSurvivalMod.Systems.ChiselModes;
using canjewelry.src.items.GemChiselMode;
using canjewelry.src.be;

namespace canjewelry.src.items
{
    public class CANItemGemChisel : Item
    {
        public SkillItem[] ToolModes;
        SkillItem addMatItem;

        public static bool carvingTime = DateTime.Now.Month == 10 || DateTime.Now.Month == 11;
        public static bool AllowHalloweenEvent = true;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            ToolModes = ObjectCacheUtil.GetOrCreate(api, "gemChiselToolModes", () =>
            {
                var skillItems = new SkillItem[3] {
                        new SkillItem() {
                            Code = new AssetLocation("1size"),
                            Name = Lang.Get("1x1x1"),
                            Data = new OneByGemChiselMode()
                        },

                        new SkillItem() {
                            Code = new AssetLocation("2size"),
                            Name = Lang.Get("Horizontal"),
                            Data = new HorizontalLineGemChiselMode()
                        },

                        new SkillItem() {
                            Code = new AssetLocation("4size"),
                            Name = Lang.Get("Vertical"),
                            Data = new VerticalLineGemChiselMode()
                        }
                };

                if (api is ICoreClientAPI capi)
                {
                    skillItems = skillItems.Select(i => {
                        var chiselMode = (GemChiselMode.GemChiselMode)i.Data;
                        return i.WithIcon(capi, chiselMode.DrawAction(capi));
                    }).ToArray();
                }

                return skillItems;
            });
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            for (int i = 0; ToolModes != null && i < ToolModes.Length; i++)
            {
                ToolModes[i]?.Dispose();
            }

            addMatItem?.Dispose();
        }


        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity)
        {
            if ((api as ICoreClientAPI)?.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative) return null;

            return base.GetHeldTpUseAnimation(activeHotbarSlot, forEntity);
        }

        public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
        {
            if ((api as ICoreClientAPI)?.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative) return null;

            return base.GetHeldTpHitAnimation(slot, byEntity);
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;

            if (byEntity.LeftHandItemSlot?.Itemstack?.Collectible?.Tool != EnumTool.Hammer && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "nohammer", Lang.Get("Requires a hammer in the off hand"));
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            if (blockSel?.Position == null) return;
            var pos = blockSel.Position;
            Block block = byEntity.World.BlockAccessor.GetBlock(pos);

            if (blockSel != null)
            {
                BlockEntity be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
                BlockEntityGemCuttingTable bea = be as BlockEntityGemCuttingTable;
                if (bea == null)
                {
                    return;
                }
                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    bea.OnUseOver((byEntity as EntityPlayer).Player, blockSel.SelectionBoxIndex);
                    handling = EnumHandHandling.PreventDefault;
                }
            }
        }



        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            if (handling == EnumHandHandling.PreventDefault) return;

            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;

            if (blockSel?.Position == null) return;
            var pos = blockSel.Position;
            Block block = byEntity.World.BlockAccessor.GetBlock(pos);

            if (byEntity.LeftHandItemSlot?.Itemstack?.Collectible?.Tool != EnumTool.Hammer && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "nohammer", Lang.Get("Requires a hammer in the off hand"));
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }


            if (api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) == true)
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return;
            }

            if (!byEntity.World.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak))
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return;
            }

            if (block is BlockGroundStorage)
            {
                BlockEntityGroundStorage begs = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;
                var neslot = begs.Inventory.FirstNonEmptySlot;
                if (neslot != null && neslot.Itemstack.Block != null && IsChiselingAllowedFor(api, pos, neslot.Itemstack.Block, byPlayer))
                {
                    block = neslot.Itemstack.Block;
                }

                if (block.Code.Path == "pumpkin-fruit-4" && (!carvingTime || !AllowHalloweenEvent))
                {
                    byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                    api.World.BlockAccessor.MarkBlockDirty(pos);
                    return;
                }
            }

            if (!IsChiselingAllowedFor(api, pos, block, byPlayer))
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            if (block.Resistance > 100)
            {
                if (api.Side == EnumAppSide.Client)
                {
                    (api as ICoreClientAPI).TriggerIngameError(this, "tootoughtochisel", Lang.Get("This material is too strong to chisel"));
                }
                return;
            }


            if (blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }


            if (block is BlockChisel)
            {
                OnBlockInteract(byEntity.World, byPlayer, blockSel, false, ref handling);
                return;
            }

            Block chiseledblock = byEntity.World.GetBlock(new AssetLocation("chiseledblock"));

            byEntity.World.BlockAccessor.SetBlock(chiseledblock.BlockId, blockSel.Position);

            BlockEntityChisel be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityChisel;
            if (be == null) return;

            be.WasPlaced(block, null);

            if (carvingTime && block.Code.Path == "pumpkin-fruit-4")
            {
                be.AddMaterial(api.World.GetBlock(new AssetLocation("creativeglow-35")));
            }

            handling = EnumHandHandling.PreventDefaultAction;
        }

        public static bool IsChiselingAllowedFor(ICoreAPI api, BlockPos pos, Block block, IPlayer player)
        {
            if (block is BlockMicroBlock)
            {
                if (block is BlockChisel) return true;
                return false;   // Existing Microblocks (e.g. in ruins) cannot be further chiseled
            }

            return IsValidChiselingMaterial(api, pos, block, player);
        }

        public static bool IsValidChiselingMaterial(ICoreAPI api, BlockPos pos, Block block, IPlayer player)
        {
            // Can't use a chiseled block as a material in a chiseled block
            if (block is BlockChisel) return false;

            // 1. priority: microblockChiseling disabled
            ITreeAttribute worldConfig = api.World.Config;
            string mode = worldConfig.GetString("microblockChiseling");
            if (mode == "off") return false;

            // 1.5 priority: Disabled by code
            if (block is IConditionalChiselable icc || (icc = block.BlockBehaviors.FirstOrDefault(bh => bh is IConditionalChiselable) as IConditionalChiselable) != null)
            {
                string errorCode;
                if (icc?.CanChisel(api.World, pos, player, out errorCode) == false || icc?.CanChisel(api.World, pos, player, out errorCode) == false)
                {
                    (api as ICoreClientAPI)?.TriggerIngameError(icc, errorCode, Lang.Get(errorCode));
                    return false;
                }
            }

            // 2. priority: canChisel flag
            bool canChiselSet = block.Attributes?["canChisel"].Exists == true;
            bool canChisel = block.Attributes?["canChisel"].AsBool(false) == true;

            if (canChisel) return true;
            if (canChiselSet && !canChisel) return false;


            // 3. prio: Never non cubic blocks
            if (block.DrawType != EnumDrawType.Cube && block.Shape?.Base.Path != "block/basic/cube") return false;

            // 4. prio: Not decor blocks
            if (block.HasBehavior<BlockBehaviorDecor>()) return false;

            // Otherwise if in creative mode, sure go ahead
            if (player?.WorldData.CurrentGameMode == EnumGameMode.Creative) return true;

            // Lastly go by the config value
            if (mode == "stonewood")
            {
                // Saratys definitely required Exception to the rule #312
                if (block.Code.Path.Contains("mudbrick")) return true;

                return block.BlockMaterial == EnumBlockMaterial.Wood || block.BlockMaterial == EnumBlockMaterial.Stone || block.BlockMaterial == EnumBlockMaterial.Ore || block.BlockMaterial == EnumBlockMaterial.Ceramic;
            }

            return true;
        }


        public void OnBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, bool isBreak, ref EnumHandHandling handling)
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return;
            }
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            if (blockSel == null)
            {
                return null;
            }
            /*if (!(forPlayer.Entity.World.BlockAccessor.GetBlock(blockSel.Position) is BlockClayForm))
            {
                return null;
            }*/
            return this.ToolModes;

            return null;
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
        {
            return slot.Itemstack.Attributes.GetInt("toolMode");
        }

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
        {
            if (blockSel == null) return;
            var pos = blockSel.Position;
            var mouseslot = byPlayer.InventoryManager.MouseItemSlot;

            if (toolMode > ToolModes.Length - 1)
            {
                int matNum = toolMode - ToolModes.Length;
                BlockEntityChisel be = api.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityChisel;
                if (be != null && be.BlockIds.Length > matNum)
                {
                    slot.Itemstack.Attributes.SetInt("materialId", be.BlockIds[matNum]);
                    slot.MarkDirty();
                }

                return;
            }

            slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
        }
    }
}
