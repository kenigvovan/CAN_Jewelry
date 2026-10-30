using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.items;
using canjewelry.src.utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canjewelry.src.eb
{
    /***
     * Behavior tracks player's armor/cloth slots + active hotbar slot and apply buff for the player.
     *
     * The applied stat value is fully derived state: it is never accumulated by deltas, it is
     * recomputed from scratch out of the currently equipped items on every relevant change and
     * on login. Because of that nothing about it has to survive a restart - the stat is written
     * as non persistent and rebuilt on the next recompute.
     */
    public class CANGemBuffAffected : EntityBehavior
    {
        private const string CHARACTER_INV = "character";
        private const string HOTBAR_INV = "hotbar";
        private const string ADDITIONAL_INV = "additionaljewelrycharacter";

        // Stat key holding the summed up buff of all encrusted gems.
        private const string BUFF_STAT_KEY = "canencrusted";
        // Legacy key of the old delta implementation, kept only to be able to clean it up.
        private const string LEGACY_NEG_STAT_KEY = "canencrustedneg";
        // Legacy moddata key of the old "what was equipped" snapshot.
        private const string LEGACY_SAVED_BUFFS_KEY = "canjewelrysavedbuffs";

        public override string PropertyName() => "cangembuffaffected";

        int triesToInit = 0;
        long callbackId = 0;
        long recomputeCallbackId = 0;
        long tarnishTickId = 0;
        double lastTarnishDays = -1;
        public bool initialized = false;

        // What was actually written to the stats last time, after clamping. Pure optimization:
        // losing it only costs one redundant write, it can never make the value wrong.
        private readonly Dictionary<string, float> appliedTotals = new Dictionary<string, float>();
        // Set whenever the stats may hold buff values nobody here wrote: legacy keys restored
        // from an old save, an admin clearbuffs, a revive. Then the diff is not enough and the
        // whole stat set has to be swept once.
        private bool fullSweepPending = true;

        public CANGemBuffAffected(Entity entity) : base(entity) { }

        private IServerPlayer ServerPlayer => (entity as EntityPlayer)?.Player as IServerPlayer;

        // Player inventories may not exist yet on first attempt (login/teleport),
        // so retry every 30s until they do.
        private void EnqueTryAddAndWait()
        {
            this.callbackId = canjewelry.sapi.Event.RegisterCallback(dt => TryToAddSlotModified(), 30 * 1000);
        }

        public bool TryToAddSlotModified()
        {
            IServerPlayer player = ServerPlayer;
            this.triesToInit++;
            canjewelry.sapi.Logger.VerboseDebug(string.Format("[canjewelry] Try #{0} to load behavior for {1}", this.triesToInit, player.PlayerName));
            IInventory characterInv = player.InventoryManager.GetOwnInventory(CHARACTER_INV);
            InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory(HOTBAR_INV);
            if (characterInv == null || playerHotbar == null)
            {
                canjewelry.sapi.Logger.VerboseDebug(string.Format("[canjewelry] Try #{0} failed to load behavior for {1}", this.triesToInit, player.PlayerName));
                EnqueTryAddAndWait();
                return false;
            }

            characterInv.SlotModified += OnSlotModifiedCharacterInv;
            playerHotbar.SlotModified += OnSlotModifiedHotbarInv;

            var additionalInv = canjewelry.GetAdditionalJewelryInventory(player);
            if (additionalInv != null)
            {
                additionalInv.SlotModified += OnSlotModifiedAdditionalInv;
            }
            canjewelry.sapi.Logger.VerboseDebug(string.Format("[canjewelry] Try #{0} loaded behavior for {1}", this.triesToInit, player.PlayerName));
            this.callbackId = 0;
            initialized = true;

            var tarnishCfg = canjewelry.config?.Tarnish;
            if (tarnishCfg != null && tarnishCfg.Enabled && this.tarnishTickId == 0)
            {
                this.tarnishTickId = canjewelry.sapi.Event.RegisterGameTickListener(OnTarnishTick,
                    Math.Max(1, tarnishCfg.UpdateIntervalSeconds) * 1000);
            }

            // Buffs are not persisted, this rebuilds them for the session.
            RecomputeBuffs();
            return true;
        }

        // Worn pieces tarnish by in-game time, so sleeping and a faster calendar count as well.
        private void OnTarnishTick(float dt)
        {
            var cfg = canjewelry.config?.Tarnish;
            IServerPlayer player = ServerPlayer;
            if (cfg == null || !cfg.Enabled || player == null) return;

            double now = entity.World.Calendar.TotalDays;
            if (this.lastTarnishDays < 0)
            {
                this.lastTarnishDays = now;
                return;
            }
            double days = now - this.lastTarnishDays;
            this.lastTarnishDays = now;
            if (days <= 0) return;

            TarnishWorn(player.InventoryManager.GetOwnInventory(CHARACTER_INV), cfg, days);
            TarnishWorn(canjewelry.GetAdditionalJewelryInventory(player), cfg, days);
        }

        private static void TarnishWorn(IInventory inv, Config.TarnishConfig cfg, double days)
        {
            if (inv == null) return;
            for (int i = 0; i < inv.Count; i++)
            {
                ItemSlot slot = inv[i];
                ItemStack stack = slot?.Itemstack;
                if (stack?.Collectible is not CANItemWearable) continue;

                float daysToFull = CANTarnish.DaysToFull(cfg, stack);
                if (daysToFull <= 0f) continue;

                float before = CANTarnish.Get(stack);
                if (before >= 1f) continue;
                float after = Math.Min(1f, before + (float)(days / daysToFull));
                CANTarnish.Set(stack, after);

                // Synced (and the buffs recomputed through SlotModified) once per whole percent,
                // not on every tick.
                if ((int)(after * 100) != (int)(before * 100)) slot.MarkDirty();
            }
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            IServerPlayer player = ServerPlayer;
            if (player != null)
            {
                IInventory characterInv = player.InventoryManager.GetOwnInventory(CHARACTER_INV);
                if (characterInv != null) characterInv.SlotModified -= OnSlotModifiedCharacterInv;

                InventoryBasePlayer playerHotbar = (InventoryBasePlayer)player.InventoryManager.GetOwnInventory(HOTBAR_INV);
                if (playerHotbar != null) playerHotbar.SlotModified -= OnSlotModifiedHotbarInv;

                IInventory additionalInv = player.InventoryManager.GetOwnInventory(ADDITIONAL_INV);
                if (additionalInv != null) additionalInv.SlotModified -= OnSlotModifiedAdditionalInv;

                // Drop the orphaned snapshot of the old delta implementation.
                player.WorldData.RemoveModdata(LEGACY_SAVED_BUFFS_KEY);
            }
            if (this.callbackId != 0)
            {
                canjewelry.sapi.Event.UnregisterCallback(this.callbackId);
                this.callbackId = 0;
            }
            // A pending recompute would otherwise fire for a player that is already gone.
            if (this.recomputeCallbackId != 0)
            {
                canjewelry.sapi.Event.UnregisterCallback(this.recomputeCallbackId);
                this.recomputeCallbackId = 0;
            }
            if (this.tarnishTickId != 0)
            {
                canjewelry.sapi.Event.UnregisterGameTickListener(this.tarnishTickId);
                this.tarnishTickId = 0;
            }
            this.lastTarnishDays = -1;
            appliedTotals.Clear();
            fullSweepPending = true;
            initialized = false;
            base.OnEntityDespawn(despawn);
        }

        // Slot changes arrive in bursts (source slot, target slot, sometimes the cursor), so the
        // recompute is deferred to the next tick and runs once for the whole burst.
        private void ScheduleRecompute()
        {
            if (this.recomputeCallbackId != 0) return;
            this.recomputeCallbackId = canjewelry.sapi.Event.RegisterCallback(dt =>
            {
                this.recomputeCallbackId = 0;
                RecomputeBuffs();
            }, 0);
        }

        /// <summary>
        /// Sums up the gem buffs of everything the player currently wears/holds and writes the
        /// result to the player stats, replacing whatever was there before. Only stats whose
        /// value actually changed are touched; pass force to rebuild everything regardless,
        /// which is what the admin commands and a revive need.
        /// </summary>
        public void RecomputeBuffs(bool force = false)
        {
            IServerPlayer player = ServerPlayer;
            if (player == null) return;
            if (force) fullSweepPending = true;

            Dictionary<string, float> totals = new Dictionary<string, float>();
            // Only built when it is going to be read - the log line is the only consumer.
            StringBuilder debug = canjewelry.config.debugMode ? new StringBuilder() : null;

            AccumulateInventory(player.InventoryManager.GetOwnInventory(CHARACTER_INV), CHARACTER_INV, totals, debug);
            // Null when the extra jewelry slots are switched off, so whatever a save still keeps
            // in them gives no buffs.
            AccumulateInventory(canjewelry.GetAdditionalJewelryInventory(player), ADDITIONAL_INV, totals, debug);

            // Wearables count where they are worn, not in the hand. Since 1.22 vanilla armour and
            // clothes are plain items with the Wearable behavior, not ItemWearable.
            ItemStack activeStack = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (activeStack?.Item != null && !IsWearable(activeStack.Item))
            {
                Accumulate(activeStack, HOTBAR_INV + "/active", totals, debug);
            }

            ApplyTotals(totals, debug);

            if (debug != null)
            {
                canjewelry.sapi.Logger.Debug("[canjewelry] recompute buffs for {0}:{1}", player.PlayerName,
                    debug.Length == 0 ? " nothing found" : debug.ToString());
            }
        }

        private static bool IsWearable(Item item) =>
            item is ItemWearable || item is CANItemWearable
            || item.GetCollectibleBehavior<CollectibleBehaviorWearable>(withInheritance: true) != null;

        private void AccumulateInventory(IInventory inv, string invName, Dictionary<string, float> totals, StringBuilder debug)
        {
            if (inv == null)
            {
                debug?.Append(" | ").Append(invName).Append(": inventory is null");
                return;
            }
            for (int i = 0; i < inv.Count; i++)
            {
                Accumulate(inv[i]?.Itemstack, invName + "[" + i + "]", totals, debug);
            }
        }

        private void Accumulate(ItemStack itemStack, string source, Dictionary<string, float> totals, StringBuilder debug)
        {
            Dictionary<string, float> buffs = GetItemStackBuffs(itemStack);
            if (debug != null)
            {
                if (buffs.Count > 0)
                {
                    debug.Append(" | ").Append(source).Append(' ').Append(itemStack.Collectible?.Code).Append(':');
                    foreach (var buff in buffs) debug.Append(' ').Append(buff.Key).Append('=').Append(buff.Value);
                }
                else if (itemStack != null && itemStack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                {
                    // Encrusted, yet nothing was collected - either non stat gems or a parsing miss.
                    debug.Append(" | ").Append(source).Append(' ').Append(itemStack.Collectible?.Code)
                         .Append(": encrusted but no stat buffs, sockets=")
                         .Append(EncrustableCB.GetMaxAmountSockets(itemStack));
                }
            }

            float luster = CANCarving.LusterBuffMult(canjewelry.config?.Carving, itemStack);
            float tarnish = CANTarnish.BuffMult(canjewelry.config?.Tarnish, itemStack);
            foreach (var buff in buffs)
            {
                totals.TryGetValue(buff.Key, out float current);
                totals[buff.Key] = current + buff.Value * luster * tarnish;
            }
        }

        private void ApplyTotals(Dictionary<string, float> totals, StringBuilder debug)
        {
            EntityPlayer ep = entity as EntityPlayer;
            if (ep == null) return;

            Dictionary<string, float> newApplied = new Dictionary<string, float>();
            foreach (var buff in totals) newApplied[buff.Key] = ClampToThreshold(buff.Key, buff.Value);

            if (fullSweepPending)
            {
                // The stats are not trusted here, so the cache is dropped too - every value gets
                // written again below instead of being diffed against a stale snapshot.
                appliedTotals.Clear();
                SweepForeignBuffStats(ep, newApplied, debug);
                fullSweepPending = false;
            }

            // Gone since the last run. Remove, not Set(0): a zero entry stays in ValuesByKey and
            // keeps taking part in the blend.
            foreach (var previous in appliedTotals)
            {
                if (newApplied.ContainsKey(previous.Key)) continue;
                ep.Stats.Remove(previous.Key, BUFF_STAT_KEY);
                debug?.Append(" || removed ").Append(previous.Key);
            }

            // Stats.Set creates the category when it does not exist yet, so an unknown stat name
            // coming from the config is harmless here. Never read via ep.Stats[name] instead -
            // that indexer throws on a missing category.
            foreach (var buff in newApplied)
            {
                // Every Set rebuilds the whole stat tree and marks it dirty, so unchanged values
                // are skipped - most slot events do not move any buff at all.
                if (appliedTotals.TryGetValue(buff.Key, out float alreadyApplied) && alreadyApplied == buff.Value) continue;

                ep.Stats.Set(buff.Key, BUFF_STAT_KEY, buff.Value, false);
                debug?.Append(" || ").Append(buff.Key).Append(" raw=").Append(totals[buff.Key])
                     .Append(" applied=").Append(buff.Value)
                     .Append(" blended=").Append(ep.Stats.GetBlended(buff.Key));
            }

            appliedTotals.Clear();
            foreach (var buff in newApplied) appliedTotals[buff.Key] = buff.Value;
        }

        /// <summary>
        /// Drops buff values this behavior did not write itself: the legacy negative key, and
        /// canencrusted entries restored from a save made back when the stat was persistent.
        /// Categories that are about to be written again are left alone.
        /// </summary>
        private static void SweepForeignBuffStats(EntityPlayer ep, Dictionary<string, float> keep, StringBuilder debug)
        {
            // Collect first, mutate after: Stats.Set/Remove rebuilds the category dictionary and
            // must not run while its enumerator is live.
            List<string> touchedCategories = new List<string>();
            foreach (KeyValuePair<string, EntityFloatStats> stat in ep.Stats)
            {
                if (stat.Value.ValuesByKey.ContainsKey(BUFF_STAT_KEY) || stat.Value.ValuesByKey.ContainsKey(LEGACY_NEG_STAT_KEY))
                {
                    touchedCategories.Add(stat.Key);
                }
            }

            foreach (string category in touchedCategories)
            {
                ep.Stats.Remove(category, LEGACY_NEG_STAT_KEY);
                if (!keep.ContainsKey(category))
                {
                    ep.Stats.Remove(category, BUFF_STAT_KEY);
                    debug?.Append(" || cleared ").Append(category);
                }
            }
        }

        /// <summary>
        /// Caps our own summed up contribution at the configured limit. Only what this mod adds is
        /// counted - whatever vanilla or another mod puts on the same stat is none of our business.
        /// Sign of the threshold sets the direction: a positive one caps from above, a negative one
        /// from below.
        /// </summary>
        private static float ClampToThreshold(string buffName, float value)
        {
            if (!canjewelry.config.max_buff_values.TryGetValue(buffName, out float threshold)) return value;
            return threshold > 0 ? Math.Min(value, threshold) : Math.Max(value, threshold);
        }

        private void OnSlotModifiedAdditionalInv(int i)
        {
            if (!initialized) return;
            ScheduleRecompute();
        }

        private void OnSlotModifiedCharacterInv(int i)
        {
            if (!initialized) return;
            ScheduleRecompute();
        }

        public void OnSlotModifiedHotbarInv(int i)
        {
            if (!initialized) return;
            IServerPlayer player = ServerPlayer;
            if (player == null) return;

            // Only the active slot is a buff source, so everything else is irrelevant here.
            if (i != player.InventoryManager.ActiveHotbarSlotNumber) return;

            ScheduleRecompute();
        }

        public void OnActiveSlotSwapped(IServerPlayer player, int from, int to)
        {
            OnSlotModifiedHotbarInv(to);
        }

        public Dictionary<string, float> GetItemStackBuffs(ItemStack itemStack)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (itemStack == null || !itemStack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING)) return result;

            ITreeAttribute encrustTreeHere = itemStack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
            for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(itemStack); i++)
            {
                ITreeAttribute socketSlot = encrustTreeHere.GetTreeAttribute("slot" + i);
                if (socketSlot == null) continue;

                if (socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF))
                {
                    if (socketSlot.HasAttribute(CANJWConstants.GEM_BUFF_TYPE) && (EnumGemBuffType)socketSlot.GetInt(CANJWConstants.GEM_BUFF_TYPE) != EnumGemBuffType.STATS_BUFF)
                        continue;
                    string buffName = socketSlot.GetString(CANJWConstants.GEM_ATTRIBUTE_BUFF);
                    if (buffName == CANJWConstants.CANDURABILITY_STRING || buffName == CANJWConstants.TEMPORALGRASP) continue;

                    float additionalValue = socketSlot.GetFloat(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE);
                    string attributeBuffName = socketSlot.GetString(CANJWConstants.GEM_ATTRIBUTE_BUFF);
                    if (result.TryGetValue(attributeBuffName, out float currentResult))
                        result[attributeBuffName] = currentResult + additionalValue;
                    else
                        result[attributeBuffName] = additionalValue;
                }
                else if (socketSlot.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES))
                {
                    string[] buffNames = (socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                    float[] buffValues = (socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                    for (int j = 0; j < buffNames.Length; j++)
                    {
                        float additionalValue = buffValues[j];
                        string attributeBuffName = buffNames[j];
                        if (attributeBuffName.Equals(CANJWConstants.CANDURABILITY_STRING) || attributeBuffName.Equals(CANJWConstants.TEMPORALGRASP))
                            continue;
                        if (result.TryGetValue(attributeBuffName, out float currentResult))
                            result[attributeBuffName] = currentResult + additionalValue;
                        else
                            result[attributeBuffName] = additionalValue;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Removes every gem buff from the player, including the legacy keys left over by
        /// older versions of the mod.
        /// </summary>
        public static void ClearBuffs(EntityPlayer ep)
        {
            if (ep == null) return;

            List<string> touchedCategories = new List<string>();
            foreach (KeyValuePair<string, EntityFloatStats> stat in ep.Stats)
            {
                if (stat.Value.ValuesByKey.ContainsKey(BUFF_STAT_KEY) || stat.Value.ValuesByKey.ContainsKey(LEGACY_NEG_STAT_KEY))
                {
                    touchedCategories.Add(stat.Key);
                }
            }

            foreach (string category in touchedCategories)
            {
                ep.Stats.Remove(category, BUFF_STAT_KEY);
                ep.Stats.Remove(category, LEGACY_NEG_STAT_KEY);
            }

            // The diff cache no longer describes the stats, so the next recompute has to write
            // everything again instead of concluding that nothing changed.
            var beh = ep.GetBehavior<CANGemBuffAffected>();
            if (beh != null)
            {
                beh.appliedTotals.Clear();
                beh.fullSweepPending = true;
            }
        }

        public override void OnEntityRevive()
        {
            base.OnEntityRevive();
            RecomputeBuffs(true);
        }
    }
}
