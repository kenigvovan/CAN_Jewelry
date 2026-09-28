using canjewelry.src;
using canjewelry.Tests.Helpers;
using canjewelry.Tests.Stubs;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace canjewelry.Tests.EncrustableCB
{
    public class TryAddSocketTests
    {
        private readonly TestInventory _inv = new();

        // ── Early-exit guards ────────────────────────────────────────────────

        [Fact]
        public void SocketNumberExceedsMax_ReturnsFalse()
        {
            // tiers=[2] → max 1 socket (index 0 only); socketNumber=1 → 1+1 > 1 → false
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2 }));
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 1);

            Assert.False(result);
        }

        [Fact]
        public void SocketSlotEmpty_ReturnsFalse()
        {
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2 }));
            var emptySocketSlot = new TestItemSlot(_inv, null);

            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, emptySocketSlot, socketNumber: 0);

            Assert.False(result);
        }

        [Fact]
        public void AllSocketsAlreadyFilled_ReturnsFalse()
        {
            var stack = ItemStackBuilder.CreateSocketableItem(new[] { 2 });
            ItemStackBuilder.AddSocket(stack, socketNumber: 0, socketLevel: 2);
            var encrustableSlot = new TestItemSlot(_inv, stack);
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            // SOCKET_ADDED_NUMBER(1) >= maxSocketNumber(1) → false
            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 0);

            Assert.False(result);
        }

        [Fact]
        public void SocketAlreadyExistsAtSlot_ReturnsFalse()
        {
            var stack = ItemStackBuilder.CreateSocketableItem(new[] { 2, 2 });
            ItemStackBuilder.AddSocket(stack, socketNumber: 0, socketLevel: 2);
            var encrustableSlot = new TestItemSlot(_inv, stack);
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            // slot0 already in tree → false
            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 0);

            Assert.False(result);
        }

        [Fact]
        public void TierInsufficient_ReturnsFalse()
        {
            // item tier at slot0 = 1; socket requires level 2 → false
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 1 }));
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(level: 2));

            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 0);

            Assert.False(result);
        }

        // ── Bug: socketNumber out of tiersList bounds ─────────────────────
        // These tests CURRENTLY throw IndexOutOfRangeException.
        // After шаг 3 of the refactoring plan they should return false cleanly.

        [Fact]
        public void SocketNumberOutOfTiersRange_FirstSocket_ReturnsFalse_NotCrash()
        {
            // tiersList has 1 entry but socketNumber=1 is valid by maxSockets check (canhavesocketsnumber=2)
            var item = new TestItem();
            item.Attributes = new JsonObject(JToken.Parse(@"{
                ""canhavesocketsnumber"": 2,
                ""cansocketstiers"": [2]
            }"));
            item.Code = new AssetLocation("canjewelry:sword-iron");
            var encrustableSlot = new TestItemSlot(_inv, new ItemStack(item));
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            var ex = Record.Exception(() =>
                src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 1));

            // Currently: IndexOutOfRangeException. After fix: ex == null, result == false.
            Assert.Null(ex);
        }

        [Fact]
        public void SocketNumberOutOfTiersRange_SecondSocket_ReturnsFalse_NotCrash()
        {
            // Same as above but item already has one socket (takes the else-branch path)
            var item = new TestItem();
            item.Attributes = new JsonObject(JToken.Parse(@"{
                ""canhavesocketsnumber"": 2,
                ""cansocketstiers"": [2]
            }"));
            item.Code = new AssetLocation("canjewelry:sword-iron");
            var stack = new ItemStack(item);
            ItemStackBuilder.AddSocket(stack, socketNumber: 0, socketLevel: 2);
            var encrustableSlot = new TestItemSlot(_inv, stack);
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            var ex = Record.Exception(() =>
                src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 1));

            Assert.Null(ex);
        }

        // ── Forged packets: a client names the socket, so it can name anything ──

        [Fact]
        public void NegativeSocketNumber_ReturnsFalse_NotCrash()
        {
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2, 2 }));
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            bool result = false;
            var ex = Record.Exception(() =>
                result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: -1));

            Assert.Null(ex);
            Assert.False(result);
            Assert.False(_inv.TakeLocked);
        }

        [Fact]
        public void NullSocketSlot_ReturnsFalse_NotCrash()
        {
            // The jeweler set inventory hands back null for a slot index out of range rather than
            // throwing, so a forged slot number arrives here as no slot at all.
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2 }));

            bool result = false;
            var ex = Record.Exception(() =>
                result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, null, socketNumber: 0));

            Assert.Null(ex);
            Assert.False(result);
            Assert.False(_inv.TakeLocked);
        }

        // ── Happy path ────────────────────────────────────────────────────

        [Fact]
        public void FirstSocket_Valid_ReturnsTrue_AndCreatesTree()
        {
            var stack           = ItemStackBuilder.CreateSocketableItem(new[] { 2 });
            var encrustableSlot = new TestItemSlot(_inv, stack);
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 0);

            Assert.True(result);
            Assert.True(stack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING));
            var tree = stack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            Assert.True(tree.HasAttribute("slot0"));
            Assert.Equal(1, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
            Assert.True(socketSlot.ItemWasTaken);
            Assert.True(socketSlot.DirtyMarked);
            Assert.True(encrustableSlot.DirtyMarked);
        }

        [Fact]
        public void SecondSocket_Valid_ReturnsTrue_IncrementsCount()
        {
            var stack = ItemStackBuilder.CreateSocketableItem(new[] { 2, 2 });
            ItemStackBuilder.AddSocket(stack, socketNumber: 0, socketLevel: 2);
            var encrustableSlot = new TestItemSlot(_inv, stack);
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            bool result = src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 1);

            Assert.True(result);
            var tree = stack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            Assert.True(tree.HasAttribute("slot1"));
            Assert.Equal(2, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        }

        // ── Invariant: TakeLocked always released ─────────────────────────

        [Fact]
        public void TakeLocked_IsAlwaysReleasedAfterSuccess()
        {
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2 }));
            var socketSlot      = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketItem(2));

            src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, socketSlot, socketNumber: 0);

            Assert.False(_inv.TakeLocked);
        }

        [Fact]
        public void TakeLocked_IsAlwaysReleasedAfterFailure()
        {
            var encrustableSlot = new TestItemSlot(_inv, ItemStackBuilder.CreateSocketableItem(new[] { 2 }));
            var emptySocket     = new TestItemSlot(_inv, null);

            src.CB.EncrustableCB.TryAddSocket(_inv, encrustableSlot, emptySocket, socketNumber: 0);

            Assert.False(_inv.TakeLocked);
        }
    }
}
