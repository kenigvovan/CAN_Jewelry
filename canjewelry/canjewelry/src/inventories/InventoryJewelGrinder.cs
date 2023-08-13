using canjewelry.src.blocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace canjewelry.src.inventories
{
    public class InventoryJewelGrinder : InventoryBase, ISlotProvider
    {
        private ItemSlot[] slots;

        public ItemSlot[] Slots => slots;

        public InventoryJewelGrinder(string inventoryID, ICoreAPI api)
          : base(inventoryID, api)
        {
            slots = GenEmptySlots(1);
        }

        public InventoryJewelGrinder(string className, string instanceID, ICoreAPI api)
          : base(className, instanceID, api)
        {
            slots = GenEmptySlots(1);
        }

        public override int Count => 1;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= Count ? null : slots[slotId];
            set
            {
                if (slotId < 0 || slotId >= Count)
                    throw new ArgumentOutOfRangeException(nameof(slotId));
                slots[slotId] = value != null ? value : throw new ArgumentNullException(nameof(value));
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree) => slots = SlotsFromTreeAttributes(tree, slots);

        public override void ToTreeAttributes(ITreeAttribute tree) => SlotsToTreeAttributes(slots, tree);

        protected override ItemSlot NewSlot(int i) => new ItemSlotSurvival(this);

        public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge) => targetSlot == slots[0] && sourceSlot.Itemstack.Collectible.GrindingProps != null ? 4f : base.GetSuitability(sourceSlot, targetSlot, isMerge);

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot) => slots[0];
        public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
        {
            if (sourceSlot.Itemstack == null || !(sourceSlot.Itemstack.Block is GrindLayerBlock))
            {
                return false;
            }
            return base.CanContain(sinkSlot, sourceSlot);
        }
    }
}
