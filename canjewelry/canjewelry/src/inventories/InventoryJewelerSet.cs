using canjewelry.src.CB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.inventories
{
    public class InventoryJewelerSet : InventoryBase, ISlotProvider
    {
        private ItemSlot[] slots;
        public int invSize = 9;
        protected override ItemSlot NewSlot(int i)
        {
            // if (i == 0)
            {
                return new ItemSlotSurvival(this);
            }
        }
        public InventoryJewelerSet(string className, string instanceID, ICoreAPI api) : base(className, instanceID, api)
        {
            slots = GenEmptySlots(invSize);
        }
        public InventoryJewelerSet(string inventoryID, ICoreAPI api)
         : base(inventoryID, api)
        {
            slots = GenEmptySlots(invSize);
            foreach(var slot in slots) 
            {
                slot.MaxSlotStackSize = 1;
            }
            // this.outputSlot = new ItemSlotCraftingTableOutput((InventoryBase)this);
            // this.InvNetworkUtil = (IInventoryNetworkUtil)new CraftingInventoryNetworkUtil((InventoryBase)this, api);
        }
        public override ItemSlot this[int slotId]
        {
            get
            {
                if (slotId < 0 || slotId >= Count)
                    return null;
                return //slotId == this.GridSizeSq ? (ItemSlot)this.outputSlot : 
                    slots[slotId];
            }
            set
            {
                if (slotId < 0 || slotId >= Count)
                    throw new ArgumentOutOfRangeException("slotid");
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                //if (slotId == this.GridSizeSq)
                // this.outputSlot = (ItemSlotCraftingTableOutput)value;
                //else
                slots[slotId] = value;
            }
        }
        public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
        {
            //return base.CanContain(sinkSlot, sourceSlot);
            if (!base.CanContain(sinkSlot, sourceSlot))
            {
                return false;
            }
            
            int sinkId = sinkSlot.Inventory.GetSlotId(sinkSlot);
            if (sinkId == 0)
            {
                int maxSocketNumber = EncrustableCB.GetMaxAmountSockets(sourceSlot.Itemstack);
                if (sourceSlot.Itemstack.Collectible.HasBehavior<EncrustableCB>() && maxSocketNumber > 0)
                {
                    return true;
                }
            }
            else if (sinkId > 0 && sinkId < Count)
            {
                /*if (sourceSlot.Itemstack.Collectible.Code.Path.Contains("cansocket-") || sourceSlot.Itemstack.Collectible.Code.Path.Contains("gem-cut-"))
                {
                    return true;
                }*/
                int maxSocketNumber = EncrustableCB.GetMaxAmountSockets(slots[0].Itemstack);
                if (slots[0].Itemstack != null && maxSocketNumber > 0)
                {
                    int possibleSocketsNumber = maxSocketNumber;

                    if (possibleSocketsNumber > 0) 
                    { 
                        if(sinkId >= 1)
                        {
                            if (sinkId <= possibleSocketsNumber && sinkId > 0)
                            {
                                if (sourceSlot.Itemstack.Collectible.Code.Path.Contains("gem-cut-"))
                                {
                                    return true; 
                                }                             
                            }
                            else if(sinkId > 4 && sinkId <= 4 + possibleSocketsNumber)
                            {
                                if (sourceSlot.Itemstack.Collectible.Code.Path.Contains("cansocket-"))
                                {
                                    return true;
                                }
                            }                            
                        }
                    }
                }
                if (sinkSlot.Itemstack != null && sinkSlot.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                {
                    var tree = sinkSlot.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                }

            }
            return false;
        }
        public override int Count => invSize;

        public ItemSlot[] Slots => slots;

        public override void FromTreeAttributes(ITreeAttribute tree)
        {
            List<ItemSlot> modifiedSlots = new List<ItemSlot>();
            this.slots = this.SlotsFromTreeAttributes(tree, this.slots, modifiedSlots);
            for (int i = 0; i < modifiedSlots.Count; i++)
            {
                this.DidModifyItemSlot(modifiedSlots[i], null);
            }
            if (this.Api != null)
            {
                for (int j = 0; j < this.slots.Length; j++)
                {
                    this.slots[j].MaxSlotStackSize = 1;
                }
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            SlotsToTreeAttributes(slots, tree);
            //ResolveBlocksOrItems();
        }
    }
}
