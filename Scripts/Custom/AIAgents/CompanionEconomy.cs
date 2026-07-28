using System;

using Server.Items;
using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Issue #65 parts 2+3 ("loot share" and "equip from party treasury"):
    // the Mobile/World-touching glue around CompanionBond and
    // LootShareCalculator's pure decisions. Two real ServUO hooks do all the
    // detection work, no new mechanic invented:
    //
    //  - BaseCreature.OnGoldGiven(Mobile, Gold) - the same virtual hook
    //    vanilla NPCs already use for "give gold to be taught a skill" /
    //    "give gold to a townsperson" (Scripts/Mobiles/Normal/BaseCreature.cs).
    //    PersonaCompanion overrides it and calls into TryHandleGift below.
    //  - EventSink.OnKilledBy - the same event the classic QuestSystem
    //    (Scripts/Services/Quests/QuestSystem.cs) already subscribes to for
    //    kill-counting objectives. We use Mobile.DamageEntries (the stock
    //    fame/karma "who helped kill this" list) to detect that a
    //    recruited companion actually took part, rather than inventing a
    //    combat-participation tracker.
    //
    // "Party treasury" (issue #65 part 3) is deliberately NOT a new
    // container type: it's the companion's own Backpack. #59's `equip`
    // executor (BotAI.TryEquipItem / FindBackpackItemByName) already reads
    // exclusively from there, validated through the real
    // Mobile.EquipItem/Item.CanEquip checks - "item owned/in-pool,
    // equippable, slot free" from the issue text is already satisfied by
    // that existing, untouched code. All this issue adds is a
    // server-validated way for gold/gear to *get* into that backpack (the
    // owner's gift, gated in PersonaCompanion.OnDragDrop/OnGoldGiven) and a
    // bond consequence for whether the owner shares fairly.
    //
    // Presence proxy: the issue frames loot share as "while Active and
    // present" (#38's presence state). #38 is being built in parallel and
    // isn't on this branch yet, so there is no Active flag to read. Using
    // DamageEntries as the participation check is actually a *stronger*
    // signal than "present" alone (the companion had to be there and
    // fighting, not just standing nearby) - once #38 lands, gating
    // ProcessAssistedKill on `companion.Active` too is a one-line AND, noted
    // inline below.
    public static class CompanionEconomy
    {
        public static void Initialize()
        {
            EventSink.OnKilledBy += OnKilledBy;
        }

        // Issue #65 part 1 (recruitment) and part 2 (ongoing loot share)
        // share one gold-drop entry point, since both are "the owner hands
        // the companion gold" - PersonaCompanion.OnGoldGiven routes here.
        // Returns true if this drop was consumed by the companion economy
        // (progressed a recruitment gift or settled owed loot share);
        // false lets the caller fall back to BaseCreature's own default
        // flavor-text handling.
        public static bool TryHandleGift(PersonaCompanion companion, Mobile from, Gold dropped)
        {
            if (companion == null || companion.Deleted || dropped == null || dropped.Deleted || dropped.Amount <= 0)
            {
                return false;
            }

            if (!(from is PlayerMobile player))
            {
                return false;
            }

            if (companion.ControlMaster == null)
            {
                return TryProgressRecruitmentGift(companion, player, dropped);
            }

            if (companion.ControlMaster == player)
            {
                SettleLootShare(companion, dropped.Amount);
                companion.AddToBackpack(dropped);
                return true;
            }

            // Someone other than the owner handing a recruited companion
            // gold isn't part of this issue's economy - let base handle it
            // (falls through to the stock "money is always welcome" flavor
            // text or a no-op).
            return false;
        }

        // Issue #65 part 1: recruitment is quest-gated (epic #35 decision
        // 2), not a bare gold handoff - only progresses the objective if
        // `player` is actively courting *this* companion via a real,
        // in-progress CompanionRecruitmentQuest.
        private static bool TryProgressRecruitmentGift(PersonaCompanion companion, PlayerMobile player, Gold dropped)
        {
            if (!(player.Quest is CompanionRecruitmentQuest quest) || quest.Companion != companion)
            {
                return false;
            }

            if (!(quest.FindObjective(typeof(GiveGoldGiftObjective)) is GiveGoldGiftObjective objective) ||
                objective.Completed)
            {
                return false;
            }

            objective.CurProgress += dropped.Amount;
            companion.AddToBackpack(dropped);

            player.SendMessage("{0} accepts your gift ({1}/{2} gold).",
                companion.Name, Math.Min(objective.CurProgress, objective.MaxProgress), objective.MaxProgress);

            return true;
        }

        // Issue #65 part 1: called once, at the moment
        // CompanionRecruitmentQuest.Complete() fires (the objective's own
        // OnComplete already guarantees this only runs once per quest).
        // This is the handoff #38 (summon/dismiss) builds on: ControlMaster
        // is the precondition their presence toggle checks, and the seeded
        // bond is what #39's tiers will read once that store exists.
        public static void Recruit(PersonaCompanion companion, PlayerMobile player)
        {
            if (companion == null || companion.Deleted || player == null)
            {
                return;
            }

            if (companion.ControlMaster != null)
            {
                // Lost a race against another player's recruitment quest for
                // the same not-yet-recruited persona - leave the winner's
                // bond alone rather than clobbering it. Any gold already
                // gifted during the losing quest stays in the companion's
                // backpack (a minor, accepted edge case - see PR notes).
                return;
            }

            companion.ControlMaster = player;
            companion.AffinityScore = CompanionBond.RecruitmentSeedScore;
            companion.LootOwed = 0;

            CompanionBondMetrics.Record("recruited", CompanionBond.RecruitmentSeedScore);
            Console.WriteLine("CompanionEconomy: '{0}' recruited by {1}, bond seeded to {2} ({3})",
                companion.PersonaId, player.Name, companion.AffinityScore, CompanionBond.GetTier(companion.AffinityScore));
        }

        // Issue #65 part 2: fires off the same EventSink.OnKilledBy the
        // classic QuestSystem already uses. Iterates DamageEntries (the
        // stock fame/karma "who helped" list, Server/Mobile.cs) rather than
        // a range/nearby check - a companion has to have actually fought,
        // not just be standing close, to be owed a share.
        private static void OnKilledBy(OnKilledByEventArgs e)
        {
            if (!(e.KilledBy is PlayerMobile owner) || e.Killed == null)
            {
                return;
            }

            var damageEntries = e.Killed.DamageEntries;
            if (damageEntries == null)
            {
                return;
            }

            // ProcessAssistedKill only touches companion/economy state, not
            // the damage list itself, so iterating it directly is safe.
            foreach (var entry in damageEntries)
            {
                if (entry.Damager is PersonaCompanion companion && !companion.Deleted &&
                    companion.ControlMaster == owner)
                {
                    ProcessAssistedKill(companion, e.Killed);
                }
            }
        }

        private static void ProcessAssistedKill(PersonaCompanion companion, Mobile killed)
        {
            var corpse = killed.Corpse;
            var goldTotal = corpse?.GetAmount(typeof(Gold)) ?? 0;
            var fairShare = LootShareCalculator.ComputeFairShare(goldTotal);

            if (fairShare <= 0)
            {
                return;
            }

            companion.LootOwed = LootShareCalculator.AccumulateOwed(companion.LootOwed, fairShare);

            if (!LootShareCalculator.IsShorted(companion.LootOwed))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (CompanionBond.IsRateLimited(companion.LastNegativeBondDeltaUtc, now, LootShareCalculator.RateLimitCooldown))
            {
                return;
            }

            ApplyBondDelta(companion, LootShareCalculator.LootShortedBondPenalty, "loot_shorted");
            companion.LastNegativeBondDeltaUtc = now;
        }

        private static void SettleLootShare(PersonaCompanion companion, int paymentAmount)
        {
            var newOwed = LootShareCalculator.ApplyPayment(companion.LootOwed, paymentAmount, out var settled);
            companion.LootOwed = newOwed;

            if (settled <= 0)
            {
                // Nothing owed - a plain gift with no debt to absorb it.
                // Generic "gifts raise bond" is #39's broader scope, not
                // this issue's loot-share mechanic (see class doc comment).
                return;
            }

            var now = DateTime.UtcNow;
            if (CompanionBond.IsRateLimited(companion.LastPositiveBondDeltaUtc, now, LootShareCalculator.RateLimitCooldown))
            {
                return;
            }

            ApplyBondDelta(companion, LootShareCalculator.LootShareBondBonus, "loot_shared");
            companion.LastPositiveBondDeltaUtc = now;
        }

        private static void ApplyBondDelta(PersonaCompanion companion, int rawDelta, string reason)
        {
            var clamped = CompanionBond.ClampDelta(rawDelta, CompanionBond.MaxDeltaMagnitude);
            companion.AffinityScore = CompanionBond.ClampScore(companion.AffinityScore + clamped);

            CompanionBondMetrics.Record(reason, clamped);
            Console.WriteLine("CompanionEconomy: '{0}' bond {1}{2} ({3}) -> {4} ({5})",
                companion.PersonaId, clamped >= 0 ? "+" : string.Empty, clamped, reason,
                companion.AffinityScore, CompanionBond.GetTier(companion.AffinityScore));
        }
    }
}
