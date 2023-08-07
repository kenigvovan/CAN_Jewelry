using canjewelry.src.blocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace canjewelry.src.jewelry
{
    public class InventoryJewelGrinder : InventoryBase, ISlotProvider
    {
        private ItemSlot[] slots;

        public ItemSlot[] Slots => this.slots;

        public InventoryJewelGrinder(string inventoryID, ICoreAPI api)
          : base(inventoryID, api)
        {
            this.slots = this.GenEmptySlots(1);
        }

        public InventoryJewelGrinder(string className, string instanceID, ICoreAPI api)
          : base(className, instanceID, api)
        {
            this.slots = this.GenEmptySlots(1);
        }

        public override int Count => 1;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= this.Count ? (ItemSlot)null : this.slots[slotId];
            set
            {
                if (slotId < 0 || slotId >= this.Count)
                    throw new ArgumentOutOfRangeException(nameof(slotId));
                this.slots[slotId] = value != null ? value : throw new ArgumentNullException(nameof(value));
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree) => this.slots = this.SlotsFromTreeAttributes(tree, this.slots);

        public override void ToTreeAttributes(ITreeAttribute tree) => this.SlotsToTreeAttributes(this.slots, tree);

        protected override ItemSlot NewSlot(int i) => (ItemSlot)new ItemSlotSurvival((InventoryBase)this);

        public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge) => targetSlot == this.slots[0] && sourceSlot.Itemstack.Collectible.GrindingProps != null ? 4f : base.GetSuitability(sourceSlot, targetSlot, isMerge);

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot) => this.slots[0];
        public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
        {
            if(sourceSlot.Itemstack == null || !(sourceSlot.Itemstack.Block is GrindLayerBlock))
            {
                return false;
            }
            return base.CanContain(sinkSlot, sourceSlot);
        }
    }
}
