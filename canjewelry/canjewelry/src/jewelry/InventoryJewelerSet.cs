using canjewelry.src.CB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.jewelry
{
    public class InventoryJewelerSet : InventoryBase, ISlotProvider
    {
        private ItemSlot[] slots;
        public int invSize = 5;
        public InventoryJewelerSet(string className, string instanceID, ICoreAPI api) : base(className, instanceID, api)
        {
            slots = GenEmptySlots(invSize);
        }
        public InventoryJewelerSet(string inventoryID, ICoreAPI api)
         : base(inventoryID, api)
        {
            this.slots = this.GenEmptySlots(this.invSize);
           // this.outputSlot = new ItemSlotCraftingTableOutput((InventoryBase)this);
           // this.InvNetworkUtil = (IInventoryNetworkUtil)new CraftingInventoryNetworkUtil((InventoryBase)this, api);
        }
        public override ItemSlot this[int slotId]
        {
            get
            {
                if (slotId < 0 || slotId >= this.Count)
                    return (ItemSlot)null;
                return //slotId == this.GridSizeSq ? (ItemSlot)this.outputSlot : 
                    this.slots[slotId];
            }
            set
            {
                if (slotId < 0 || slotId >= this.Count)
                    throw new ArgumentOutOfRangeException("slotid");
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                //if (slotId == this.GridSizeSq)
                   // this.outputSlot = (ItemSlotCraftingTableOutput)value;
                //else
                    this.slots[slotId] = value;
            }
        }
        public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
        {
            if(!base.CanContain(sinkSlot, sourceSlot))
            {
                return false;
            }

            int sinkId = sinkSlot.Inventory.GetSlotId(sinkSlot);
            if(sinkId == 0)
            {
                if(sourceSlot.Itemstack.Collectible.HasBehavior<EncrustableCB>())
                {
                    return true;
                }
            }
            else if(sinkId > 0 && sinkId < this.Count)
            {
               if(sourceSlot.Itemstack.Collectible.Code.Path.Contains("cansocket-") || sourceSlot.Itemstack.Collectible.Code.Path.Contains("gem-cut-"))
               {
                    return true;
               }
            }
            return false;
        }
        public override int Count =>this.invSize;

        public ItemSlot[] Slots => this.slots;

        public override void FromTreeAttributes(ITreeAttribute tree)
        {
            ItemSlot[] itemSlotArray = this.SlotsFromTreeAttributes(tree);
            int? length1 = itemSlotArray?.Length;
            int length2 = this.slots.Length;
            if (!(length1.GetValueOrDefault() == length2 & length1.HasValue))
                return;
            this.slots = itemSlotArray;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            this.SlotsToTreeAttributes(this.slots, tree);
            this.ResolveBlocksOrItems();
        }
    }
}
