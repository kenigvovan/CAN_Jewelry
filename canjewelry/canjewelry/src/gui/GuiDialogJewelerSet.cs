using canjewelry.src.CB;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;

namespace canjewelry.src.jewelry
{
    public class GuiDialogJewelerSet : GuiDialogBlockEntity
    {
        GuiElementVerticalTabs groupOfInterests;
        public float Width { get; private set; }
        public float Height { get; private set; }
        int selectedDropSocket = 0;
        public GuiDialogJewelerSet(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPos, ICoreClientAPI capi) : base(dialogTitle, inventory, blockEntityPos, capi)
        {            
            if (IsDuplicate)
            {
                return;
            }
            this.Width = 300;
            this.Height = 400;
            capi.World.Player.InventoryManager.OpenInventory((IInventory)inventory);
            SetupDialog();
        }
        public void SetupDialog()
        {
            ElementBounds closeButton = ElementBounds.Fixed(1000, 30, 0, 0).WithAlignment(EnumDialogArea.LeftFixed).WithFixedPadding(10.0, 2.0); 
            ElementBounds elementBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds backgroundBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding).WithFixedSize(Width, Height - 90); ;
            backgroundBounds.BothSizing = ElementSizing.Fixed;
           
            backgroundBounds.WithChildren(new ElementBounds[]
            {
                    //closeButton
            });
            var jewelerComposer =  this.SingleComposer = this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()] = this.capi.Gui.CreateCompo("jewelersetgui" + this.BlockEntityPosition?.ToString(), elementBounds).
                  AddShadedDialogBG(backgroundBounds).
                  AddDialogTitleBar(Lang.Get("canjewelry:jewelset_gui_name"), new Action(this.OnTitleBarClose))
                  .BeginChildElements(backgroundBounds);

            int chosenGroupTab = groupOfInterests == null ? 0 : groupOfInterests.ActiveElement;
            int fixedY1 = 60;

            var scaledSlotSize = (48);
            var scaledInsetSize = (48 + (48 / 12 * 2));        
            var scaledOffset = (48 / 12);

            ElementBounds encrustetItemBounds = backgroundBounds.FlatCopy().WithFixedSize(this.Width, 60).WithFixedPosition(0, 30);
            ElementBounds slotB = ElementBounds.FixedSize(48, 48).WithAlignment(EnumDialogArea.CenterMiddle);
            encrustetItemBounds.WithChild(slotB);
            //ElementBounds.Fixed(-120.0 + backgroundBounds.fixedX, 45.0 + backgroundBounds.fixedY, 50, 100);

            int[] intArr1 = new int[1];
            intArr1[0] = 0;
            jewelerComposer.AddItemSlotGrid(this.Inventory, new Action<object>((this).DoSendPacket), intArr1.Length, intArr1, slotB, "socketsslots");

            backgroundBounds.WithChild(encrustetItemBounds);

            //jewelerComposer.AddInset(encrustetItemBounds);

            ElementBounds slotsEl = encrustetItemBounds.BelowCopy().WithFixedSize(encrustetItemBounds.fixedWidth, encrustetItemBounds.fixedHeight - 20);
            slotsEl.fixedHeight += 40;
            //jewelerComposer.AddInset(slotsEl);
           // slotsEl.BothSizing = ElementSizing.FitToChildren;
            ItemStack encrustable = this.Inventory[0].Itemstack;
            if (encrustable != null && encrustable.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_NUMBER_STRING))
            {               
                int possibleSockets = encrustable.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt();
                ElementBounds tmpEl = slotsEl.FlatCopy().WithFixedSize(scaledSlotSize, scaledSlotSize);
                double center = slotsEl.fixedWidth / 2;
                double centerSlot = center - (possibleSockets % 2 == 1 ? scaledSlotSize / 2: 0);
                double startSlot = centerSlot - (((int)(possibleSockets / 2)) * scaledSlotSize);
                tmpEl.fixedX = startSlot;

                var tree = encrustable.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);

                for (int i = 0; i < possibleSockets; i++)
                {
                    if (tree != null && tree.HasAttribute("slot" + i))
                    {
                        string gemType = tree.GetTreeAttribute("slot" + i).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET);
                        if(!(gemType == ""))
                        {
                            int gemSizeInt = tree.GetTreeAttribute("slot" + i).GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE);
                            string gemSize = "normal";
                            if (gemSizeInt == 3)
                            {
                                gemSize = "exquisite";
                            }
                            else if(gemSizeInt == 2)
                            {
                                gemSize = "flawless";
                            }

                            var elGem = tmpEl.FlatCopy();
                            elGem.fixedY -= 48;
                            elGem.fixedX += 20;
                            elGem.fixedY += 20;
                            var bucketSatck = new ItemStack(capi.World.GetItem(new AssetLocation("canjewelry:gem-cut-" + gemSize + "-" + gemType)), 1);
                            var sli = new SlideshowItemstackTextComponent(capi, new ItemStack[] { bucketSatck }, 48, EnumFloat.Inline);
                            var rc = new RichTextComponentBase[] { sli };
                            tmpEl.fixedX += 20;
                            tmpEl.fixedY += 20;
                            if (bucketSatck != null)
                            {
                                SingleComposer.AddRichtext(rc, elGem, "gem_slot" + i);
                            }
                            //jewelerComposer.AddInset(tmpEl);
                            tmpEl = tmpEl.FlatCopy();
                            tmpEl.fixedX -= 20;
                            tmpEl.fixedY -= 20;

                        }
                        //jewelerComposer.AddInset(tmpEl);
                        int[] intArr = new int[1];
                        intArr[0] = i + 1;
                        jewelerComposer.AddItemSlotGrid(this.Inventory, new Action<object>(this.DoSendPacket), intArr.Length, intArr, tmpEl, "gemslot" + i);

                        ElementBounds buttonEl = ElementBounds.FixedSize(48, 24);
                        buttonEl.fixedX = tmpEl.fixedX;
                        buttonEl.fixedY = tmpEl.fixedY + tmpEl.fixedHeight + 4;
                        var elll = ElementBounds.FixedSize(40, 40);

                        int tmpI = i;
                        jewelerComposer.AddSmallButton(Lang.Get("+"),
                           new ActionConsumable(() =>
                           {
                               OnClickButtonAddGem(tmpI, tmpI + 1);
                               return true;
                           }),
                           buttonEl);
                    }
                    



                    tmpEl = tmpEl.FlatCopy();
                    tmpEl.fixedX += scaledSlotSize + scaledOffset * 2;
                }
                
                
            }

            ElementBounds socketsEl = slotsEl.BelowCopy().WithFixedSize(encrustetItemBounds.fixedWidth, encrustetItemBounds.fixedHeight);
            socketsEl.fixedY -= 20;
            socketsEl.fixedHeight += 40;
            //this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()].AddInset(socketsEl);

            if (encrustable != null && encrustable.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_NUMBER_STRING))
            {
                int possibleSockets = encrustable.Collectible.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt();
                ElementBounds tmpEl = socketsEl.FlatCopy().WithFixedSize(scaledSlotSize, scaledSlotSize);
                double center = slotsEl.fixedWidth / 2;
                double centerSlot = center - (possibleSockets % 2 == 1 ? scaledSlotSize / 2 : 0);
                double startSlot = centerSlot - (((int)(possibleSockets / 2)) * scaledSlotSize);
                tmpEl.fixedX = startSlot;

                var tree = encrustable.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);

                JsonObject[] tiersList = tiersList = encrustable.Collectible.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();
                string green = "#2FE147";
                string blue = "#2B3FF7";
                string purple = "#9214C9";
                for (int i = 0; i < possibleSockets; i++)
                {
                    if (tree != null && tree.HasAttribute("slot" + i))
                    {

                        int socketTier = tree.GetTreeAttribute("slot" + i).GetInt(CANJWConstants.ADDED_SOCKET_TYPE);

                        string socket_type_str = "tinbronze";

                        if (socketTier == 2)
                        {
                            socket_type_str = "iron";
                        }
                        else if (socketTier == 3)
                        {
                            socket_type_str = "steel";
                        }

                        var bucketSatck = new ItemStack(capi.World.GetItem(new AssetLocation("canjewelry:cansocket-" + socket_type_str)), 1);
                        var sli = new SlideshowItemstackTextComponent(capi, new ItemStack[] { bucketSatck }, 48, EnumFloat.Inline);
                        var rc = new RichTextComponentBase[] { sli };
                        tmpEl.fixedX += 20;
                        tmpEl.fixedY += 20;
                        if (bucketSatck != null)
                        {
                            SingleComposer.AddRichtext(rc, tmpEl, "socket_slot" + i);
                        }
                        //jewelerComposer.AddInset(tmpEl);
                        tmpEl = tmpEl.FlatCopy();
                        tmpEl.fixedX -= 20;
                        tmpEl.fixedY -= 20;
                        
                        tmpEl.fixedX += scaledSlotSize + scaledOffset * 2;
                        continue;
                    }
                    else
                    {
                        //this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()].AddInset(tmpEl);
                        int[] intArr = new int[1];
                        intArr[0] = i + 5;
                        this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()]
                        .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(((GuiDialogJewelerSet)this).DoSendPacket), intArr.Length, intArr, tmpEl, "socketsslot" + i);

                        ElementBounds buttonEl = ElementBounds.FixedSize(48, 24);
                        buttonEl.fixedX = tmpEl.fixedX;
                        buttonEl.fixedY = tmpEl.fixedY + tmpEl.fixedHeight + 4;
                        var elll = ElementBounds.FixedSize(40, 40);
                        if (tiersList != null)
                        {
                            int curTier = tiersList[i].AsInt();
                            if (curTier == 1)
                            {
                                this.Inventory[i + 5].HexBackgroundColor = green;
                            }
                            else if (curTier == 2)
                            {
                                this.Inventory[i + 5].HexBackgroundColor = blue;
                            }
                            else if (curTier == 3)
                            {
                                this.Inventory[i + 5].HexBackgroundColor = purple;
                            }
                        }
                        else
                        {
                            this.Inventory[i + 5].HexBackgroundColor = green;
                        }
                        int tmpI = i;
                        this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()].AddSmallButton(Lang.Get("+"),
                           new ActionConsumable(() =>
                           {
                               OnClickButtonAddSocket(tmpI, tmpI + 5);
                               return true;
                           }),
                           buttonEl);
                    }

                    tmpEl = tmpEl.FlatCopy();
                    tmpEl.fixedX += scaledSlotSize + scaledOffset * 2;
                }


            }

            this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()].Compose();
            return;
        }
        private void didSelectEntity(string code, bool selected)
        {
            if (selected)
            {
                if(int.TryParse(code, out int res))
                {
                    selectedDropSocket = res;
                    SetupDialog();
                }
                else
                {
                    selectedDropSocket = 0;
                    SetupDialog();
                }
            }
            else
            {
                selectedDropSocket = 0;
            }

        }
        private string[] GetAvailableGemTypes(ItemStack itemStack)
        {
            string itemCode = itemStack.Collectible.Code.Path;
            List<string> res = new List<string>();
            foreach(var gemTypeSetPair in canjewelry.config.buffNameToPossibleItem)
            {
                foreach(var it in gemTypeSetPair.Value)
                {                 
                    if (it.Contains("pick"))
                    {
                        var c = 3;
                    }
                    if (WildcardUtil.Match("*" + it + "*", itemCode))
                    {
                        res.Add(gemTypeSetPair.Key);
                    }
                }
            }
            return res.ToArray();
        }
        public static int getStringLength(string name)
        {
            using (var paint = new SKPaint())
            {
                paint.Typeface = SKTypeface.FromFamilyName("Times New Roman");

                paint.TextSize = 24f;
                var skBounds = SKRect.Empty;
                return (int)paint.MeasureText(name.AsSpan(), ref skBounds);

            }
        }
        public void ComposeAvailableGemTypesGui()
        {
            if(this.Inventory[0].Itemstack == null || !this.Inventory[0].Itemstack.Collectible.Attributes.KeyExists("canhavesocketsnumber"))
            {
                this.Composers.Remove("jewelersetgui-types");
                return;
            }
            string[] availableGemTypes = GetAvailableGemTypes(this.Inventory[0].Itemstack);
            if(availableGemTypes.Length < 1)
            {
                return;
            }
            ElementBounds leftDlgBounds = this.Composers["jewelersetgui" + this.BlockEntityPosition?.ToString()].Bounds;
            double b = leftDlgBounds.InnerHeight / (double)RuntimeEnv.GUIScale + 10.0;

            ElementBounds bgBounds = ElementBounds.Fixed(0.0, 0.0,
                235, leftDlgBounds.InnerHeight / (double)RuntimeEnv.GUIScale - GuiStyle.ElementToDialogPadding - 20.0 + b).WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds dialogBounds = bgBounds.ForkBoundingParent(0.0, 0.0, 0.0, 0.0)
                .WithAlignment(EnumDialogArea.LeftMiddle)
                .WithFixedAlignmentOffset((leftDlgBounds.renderX + leftDlgBounds.OuterWidth + 10.0) / (double)RuntimeEnv.GUIScale,  0);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            dialogBounds.BothSizing = ElementSizing.FitToChildren;
            dialogBounds.WithChild(bgBounds);
            ElementBounds textBounds = ElementBounds.FixedPos(EnumDialogArea.LeftTop,
                                                               0,
                                                                0);
            bgBounds.WithChildren(textBounds);

            this.Composers["jewelersetgui-types"] = this.capi.Gui.CreateCompo("jewelersetgui-types", dialogBounds).AddShadedDialogBG(bgBounds, false, 5.0, 0.75f);
            int maxWidth = availableGemTypes.OrderByDescending(s => s.Length).FirstOrDefault()?.Length ?? 20;
            for(int i = 0; i < availableGemTypes.Length; i++)
            {
                ElementBounds el = textBounds.CopyOffsetedSibling().WithFixedHeight(20)
                    .WithFixedWidth(maxWidth * 7)
                    .WithFixedPosition(0, i * 20);
                bgBounds.WithChildren(el);

                this.Composers["jewelersetgui-types"].AddStaticText(availableGemTypes[i], CairoFont.WhiteDetailText(), el);
            }
            this.Composers["jewelersetgui-types"].Compose();
        }
        private void OnTitleBarClose() => this.TryClose();
        public override void OnGuiClosed()
        {
            this.capi.Network.SendPacketClient(this.capi.World.Player.InventoryManager.CloseInventory((IInventory)this.Inventory));
            base.Inventory.SlotModified -= this.OnInventorySlotModified;
            base.OnGuiClosed();
        }
        private void OnGroupTabClicked(int clicked)
        {
            this.groupOfInterests.ActiveElement = clicked;
            this.SetupDialog();
        }
        private void OnGroupTabClicked2(int arg1, GuiTab tab)
        {
            //this.
            this.groupOfInterests.ActiveElement = arg1;
            this.SetupDialog();
            /*string layerGroupCode = this.tabnames[arg1];
            if (tab.Active)
            {
                this.renderLayerGroups.Remove(layerGroupCode);
            }
            else
            {
                this.renderLayerGroups.Add(layerGroupCode);
            }
            foreach (MapLayer ml in this.MapLayers)
            {
                if (ml.LayerGroupCode == layerGroupCode)
                {
                    ml.Active = tab.Active;
                }
            }
            this.updateMaplayerExtrasState();*/
        }
        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            base.Inventory.SlotModified += this.OnInventorySlotModified;

        }
        private void OnInventorySlotModified(int slotid)
        {
            if (slotid == 0)
            {
                this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupjewelersetdlg");
                this.capi.Event.EnqueueMainThreadTask(new Action(this.ComposeAvailableGemTypesGui), "setupavailabletypesdlg");
                
            }
        }
        public bool onClickBackButtonPutSocket()
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                //stream.Write("BlockEntityCANMarket");
                // stream.Write("123");
                // stream.Write((byte)4);
                TreeAttribute tree = new TreeAttribute();
                tree.SetInt("selectedSocketSlot", selectedDropSocket);
                tree.ToBytes(stream);
                array = output.ToArray();
            }

            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1004, array);
            //this.chosenCommand = enumChosenCommand.NO_CHOSEN_COMMAND;
            // this.buildWindow();
            return true;
        }
        public bool onClickBackButtonPutGem()
        {
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1005);
            //this.chosenCommand = enumChosenCommand.NO_CHOSEN_COMMAND;
            // this.buildWindow();
            return true;
        }

        public void OnClickButtonAddGem(int socketNum, int slotNum)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                TreeAttribute tree = new TreeAttribute();
                //in which slot in item we want socket to be added
                tree.SetInt("selectedSocketSlot", socketNum);
                //which slot of inventory contains socket item to be added
                tree.SetInt("selectedSlotNum", slotNum);
                tree.ToBytes(stream);
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1005, array);
        }

        public void OnClickButtonAddSocket(int socketNum, int slotNum)
        {
            byte[] array;
            using (MemoryStream output = new MemoryStream())
            {
                BinaryWriter stream = new BinaryWriter((Stream)output);
                TreeAttribute tree = new TreeAttribute();
                //in which slot in item we want socket to be added
                tree.SetInt("selectedSocketSlot", socketNum);
                //which slot of inventory contains socket item to be added
                tree.SetInt("selectedSlotNum", slotNum);
                tree.ToBytes(stream);
                array = output.ToArray();
            }
            this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition, 1004, array);
        }
    }
}
