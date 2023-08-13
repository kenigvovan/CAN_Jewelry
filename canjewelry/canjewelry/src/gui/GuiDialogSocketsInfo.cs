using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.jewelry
{
    public class GuiDialogSocketsInfo : HudElement
    {
        private bool shouldRender = false;
        public LoadedTexture texture = new LoadedTexture(canjewelry.capi);
        public override EnumDialogType DialogType => EnumDialogType.HUD;
        private bool reCompose = false;
        private ItemSlot savedSlot;
        int outerBorder = 20;
        public override double DrawOrder => 0.9;
        public override bool Focusable => false;
        public GuiDialogSocketsInfo(ICoreClientAPI capi) : base(capi)
        {
            ComposeGuis();
        }
        public void ComposeGuis()
        {
            //Slot or itemstack is null
            if (savedSlot == null || savedSlot.Itemstack == null)
            {
                this.shouldRender = false;
                return;
            }
            //Has no number of socket(bug?)
            if(savedSlot.Itemstack.Collectible.Attributes == null || !savedSlot.Itemstack.Collectible.Attributes.KeyExists("canhavesocketsnumber"))
            {
                this.shouldRender = false;
                return;
            }
            //It is 0, we don't need to render
            int maxSocketsNumber = savedSlot.Itemstack.Collectible.Attributes["canhavesocketsnumber"].AsInt();
            if(maxSocketsNumber < 1)
            {
                this.shouldRender = false;
                return;
            }
            this.shouldRender = true;
            double border = GuiElement.scaled(12);
            double widthGlobal = maxSocketsNumber * GuiElement.scaled(54);
            double heightGlobal = GuiElement.scaled(54);
            int movedByMouseX = 0;
            int movedByMouseY = 0;
            ElementBounds tmp = null;
            /*foreach(var it in canmods.capi.OpenedGuis)
             {
                 if((it as GuiDialog).DebugName.Equals("HudMouseTools"))
                 {
                    tmp = ((ElementBounds)it.GetType().GetField("slotBounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(it));
                     movedByMouseX = (int)(((ElementBounds)it.GetType().GetField("slotBounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(it)).absFixedX * 0.65);
                     movedByMouseY = (int)(((ElementBounds)it.GetType().GetField("slotBounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(it)).absFixedY * 0.6);
                 }
             }*/
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightBottom);

            ElementBounds bgBounds = ElementBounds.Fixed(movedByMouseX, movedByMouseY, widthGlobal, heightGlobal);
            bgBounds.WithFixedHeight(heightGlobal).WithFixedWidth(widthGlobal);
            var a = widthGlobal - border * 2;
            var b = heightGlobal - border * 2;
            ElementBounds socketsBounds = ElementBounds.Fixed(0 , 0, widthGlobal, heightGlobal);
            bgBounds.WithChildren(socketsBounds);

            //Item doesn't have encrusted tree
            /*ITreeAttribute encrustTree = savedSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                this.shouldRender = false;
                return;
            }*/
            SingleComposer = capi.Gui.CreateCompo("myAwesomeDialog", dialogBounds)
                //.AddStaticElement()
               .AddDialogBG(bgBounds, false)
               //.AddInset(bgBounds)
              //.AddInset(socketsBounds)
               .AddStaticCustomDraw(socketsBounds, drawDelegateWithBounds)
            ;
            SingleComposer.Compose();

            //if (this.texture != null && this.texture.TextureId != 0)
               // capi.Render.Render2DLoadedTexture(this.texture, (float)dialogBounds.fixedWidth, (float)dialogBounds.fixedHeight, 255);
           
        }
        public void drawDelegateWithBounds(Context context, ImageSurface surface, ElementBounds currentBounds)
        {
            //ImageSurface textSurface = new ImageSurface(0, (int)GuiElement.scaled(180), (int)GuiElement.scaled(180));
            //Context context = new Context(textSurface);
            if(savedSlot == null || savedSlot.Itemstack == null)
            {
                return;
            }
            ITreeAttribute encrustTree = savedSlot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }
            int numberSockets = encrustTree.GetInt("socketsnumber");
            double tr = currentBounds.InnerWidth / numberSockets;
            for (int p = 0; p < numberSockets; p++)
            {
                int j = 0;
                int i = p;
                /*if (i > 1)
                {
                    j = 1;
                    i = 0;
                }*/
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, harmPatch.socketsTextureDict["socket-" + socketSlot.GetInt("sockettype")], 255);


                context.NewPath();
                var f2 = RuntimeEnv.GUIScale;

                var tmpMoveX = 0;// GuiElement.scaled(12);
                var tmpMoveY = 0;// GuiElement.scaled(12);
                var t = GuiElement.scaled(0) + i * GuiElement.scaled(tr) + tmpMoveX;
                context.LineTo(GuiElement.scaled(0) + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * GuiElement.scaled(tr) + tmpMoveY);
                context.LineTo(currentBounds.InnerHeight/2 + i * tr + tmpMoveX, GuiElement.scaled(0) + j * GuiElement.scaled(tr) + tmpMoveY);
                context.LineTo(currentBounds.InnerHeight + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * GuiElement.scaled(tr) + tmpMoveY);
                context.LineTo(currentBounds.InnerHeight / 2 + i * tr + tmpMoveX, currentBounds.InnerHeight+ j * GuiElement.scaled(tr) + tmpMoveY);
                // tr += 10;
                context.Translate(tr * i , tr * j );
                context.ClosePath();
               
                context.SetSourceSurface(socketSurface, (int)tmpMoveX, (int)tmpMoveY);
               context.Translate(-tr * i, -tr *j);
                context.FillPreserve();
                context.Stroke();
                if (socketSlot.GetInt("size") > 0)
                {
                    if (socketSlot.GetInt("size") == 2)
                    {

                        context.NewPath();
                        context.Arc(currentBounds.InnerHeight / 2 + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * tr + tmpMoveY, currentBounds.InnerHeight / 4, 0, 2 * 3.14);
                        context.ClosePath();

                        context.Translate(tr * i, tr * j);
                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, harmPatch.preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, (int)tmpMoveX, (int)tmpMoveY);

                        context.Translate(-(tr * i), -tr * j);
                    }
                    else if (socketSlot.GetInt("size") == 1)
                    {
                        context.NewPath();
                        context.LineTo(currentBounds.InnerHeight / 4 + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 2 + i * tr + tmpMoveX, currentBounds.InnerHeight / 4 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 4 * 3 + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 2 + i * tr+ tmpMoveX, currentBounds.InnerHeight / 4 * 3 + j * tr + tmpMoveY);
                        context.ClosePath();
                        context.Translate(i * tr, tr * j);

                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, harmPatch.preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, (int)tmpMoveX, (int)tmpMoveY);

                        context.Translate(-i * tr , -tr * j);
                    }
                    else if (socketSlot.GetInt("size") == 3)
                    {
                        context.NewPath();
                        context.LineTo(currentBounds.InnerHeight / 6 + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 4 + i * tr + tmpMoveX, currentBounds.InnerHeight / 4 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 2 + i * tr + tmpMoveX, currentBounds.InnerHeight / 7 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 12 * 9 + i * tr + tmpMoveX, currentBounds.InnerHeight / 4 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 6 * 5 + i * tr + tmpMoveX, currentBounds.InnerHeight / 2 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 12 * 9 + i * tr + tmpMoveX, currentBounds.InnerHeight / 12 * 9 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 2 + i * tr + tmpMoveX, currentBounds.InnerHeight / 6 * 5 + j * tr + tmpMoveY);
                        context.LineTo(currentBounds.InnerHeight / 5+ i * tr + tmpMoveX, currentBounds.InnerHeight / 6 * 4 + j * tr + tmpMoveY);
                        context.ClosePath();
                        context.Translate(i * tr, tr * j);

                        var gemSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, harmPatch.preparedEncrustedGemsImages[socketSlot.GetString("gemtype")], 255);
                        context.SetSourceSurface(gemSurface, (int)tmpMoveX, (int)tmpMoveY);

                        context.Translate(-i * tr, -tr * j);
                    }
                    context.FillPreserve();

                }
            }
        }


        public override bool OnMouseEnterSlot(ItemSlot slot)
        {
            if(slot == null || slot is ItemSlotCreative || slot.Itemstack == null)
            {
                this.shouldRender = false;
                //this.Composers.Remove("myAwesomeDialog");
               // capi.Gui.CreateCompo("myAwesomeDialog", dialogBounds)
            }
            else
            {
                savedSlot = slot;
                this.reCompose = true;
            }
            return base.OnMouseEnterSlot(slot);
        }
        public override bool OnMouseLeaveSlot(ItemSlot itemSlot)
        {
            this.shouldRender = false;
            //reCompose = true;
           this.Composers.Remove("single");
            return base.OnMouseLeaveSlot(itemSlot);
        }
        public override void OnRenderGUI(float deltaTime)
        {
            if (reCompose)
            {
                ComposeGuis();
                reCompose = false;
            }
            if (this.shouldRender)
            {
                base.OnRenderGUI(deltaTime);
            }
        }
    }
}
