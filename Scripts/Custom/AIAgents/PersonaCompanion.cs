using System;
using System.Collections.Generic;

using Server.ContextMenus;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.AIAgents
{
    // Generic, data-driven companion (issue #32) - appearance and PersonaId
    // come from a DynamoDB persona item (via PersonaSync), not a hardcoded
    // [Constructable] body like TestCompanion. One instance per spawned
    // persona; PersonaSync tracks the mapping from persona_id to instance.
    public class PersonaCompanion : BaseCreature, IRevealableIntent
    {
        [Constructable]
        public PersonaCompanion()
            : base(AIType.AI_Melee, FightMode.Aggressor, 10, 1, 0.2, 0.4)
        {
        }

        private BotAI _botAI;

        protected override BaseAI ForcedAI => _botAI ?? (_botAI = new BotAI(this));

        public override bool ClickTitle => false;

        public string PersonaId => ((BotAI)AIObject).PersonaId;

        // Issue #65: the deterministic-half bond score (CompanionBond) and
        // the running unsettled loot-share balance (LootShareCalculator).
        // Zero/Stranger until CompanionEconomy.Recruit seeds it at
        // recruitment. Issue #39 (reconciled 2026-07-28: this is the earned
        // per-companion OWNER bond, not a per-player sidecar store - that
        // framing was dropped, see #61/#64 for per-player disposition/memory
        // instead) is the field this local, already-serialized (v3) score
        // continues to be moved by, via CompanionBondBehaviors' full set of
        // owner-behavior inputs.
        [CommandProperty(AccessLevel.GameMaster)]
        public int AffinityScore { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public CompanionBond.Tier AffinityTier => CompanionBond.GetTier(AffinityScore);

        [CommandProperty(AccessLevel.GameMaster)]
        public int LootOwed { get; set; }

        // Issue #61 (IRevealableIntent): resolved live from the persona
        // record via PersonaSync.GetIntent every time it's read, NOT a
        // stored/serialized field - disposition is computed, not
        // persisted, and #39 (built in parallel) already owns the one
        // serialized per-companion field this issue is allowed to touch
        // (AffinityScore, at serialization v3). Falls back to Neutral if
        // the persona has since been disabled/pruned from PersonaSync's
        // known set.
        [CommandProperty(AccessLevel.GameMaster)]
        public DispositionSystem.NpcIntent Intent => PersonaSync.GetIntent(PersonaId);

        // Rate-limit bookkeeping for CompanionEconomy.TryApplyBondDelta's
        // bounded bond deltas (issue #65 acceptance: "bounded,
        // rate-limited"; issue #39 extends this to a full set of behavior
        // inputs - see CompanionBondBehavior). Keyed by reason string
        // (CompanionBond.IsRateLimited's dictionary overload) rather than
        // one DateTime field per source, so adding another behavior input
        // never means adding another field here. Session-scoped by design -
        // not serialized, so a box restart trivially clears any in-flight
        // cooldown. That's an accepted minor gap (the box restarts on the
        // order of many idle-shutdown minutes, not a fast-enough loop to
        // meaningfully farm bond), not a silent one - see the PR notes.
        internal readonly Dictionary<string, DateTime> LastBondDeltaUtcByReason = new Dictionary<string, DateTime>();

        // Skill values are 0-120 on the live scale ServUO already uses
        // everywhere else (SetSkill takes the same scale) - a persona
        // author fat-fingering a bare 0-1000 "percent-ish" number would
        // otherwise silently create a bugged mobile.
        private const double MinSkillValue = 0.0;
        private const double MaxSkillValue = 120.0;

        // No engine-enforced cap for NPC stats, but personas come from an
        // external, human-authored DynamoDB item - clamp to a generous but
        // sane humanoid range so a bad value can't create a broken (e.g.
        // negative Hits/Stam/Mana) mobile.
        private const int MinStatValue = 1;
        private const int MaxStatValue = 1000;

        // Applied once at spawn time (PersonaSync.Spawn). Appearance is not
        // re-applied on later poll ticks for an already-spawned bot - editing
        // a persona's prompt goes live via the sidecar's own cache (§ issue
        // #32 Part 1); editing appearance/spawn point only affects the next
        // fresh spawn (despawn + re-deploy), which is outside this issue's
        // acceptance criteria.
        public void ConfigureFrom(PersonaConfig cfg)
        {
            var botAi = (BotAI)AIObject;
            botAi.BotId = $"{cfg.PersonaId}-01";
            botAi.PersonaId = cfg.PersonaId;

            Name = cfg.Name;
            Title = cfg.DisplayName;
            Body = cfg.Body;
            Hue = cfg.Hue;

            ApplySkills(cfg.Skills);
            ApplyStats(cfg.Stats);
        }

        // Issue #36: starting skills matching the persona's backstory.
        // Unknown skill names are ignored (logged) rather than rejecting the
        // whole persona - a typo in one skill shouldn't block spawning.
        private void ApplySkills(Dictionary<string, double> skills)
        {
            if (skills == null)
            {
                return;
            }

            foreach (var pair in skills)
            {
                if (!SkillNameAliases.TryParse(pair.Key, out var skill))
                {
                    Console.WriteLine("PersonaCompanion: persona '{0}' has unknown skill '{1}', ignoring", PersonaId, pair.Key);
                    continue;
                }

                var value = pair.Value;
                if (value < MinSkillValue || value > MaxSkillValue)
                {
                    Console.WriteLine(
                        "PersonaCompanion: persona '{0}' skill '{1}' value {2} out of range, clamping",
                        PersonaId, pair.Key, value);
                    value = Math.Min(Math.Max(value, MinSkillValue), MaxSkillValue);
                }

                SetSkill(skill, value);
            }
        }

        private void ApplyStats(Dictionary<string, int> stats)
        {
            if (stats == null)
            {
                return;
            }

            foreach (var pair in stats)
            {
                var value = Math.Min(Math.Max(pair.Value, MinStatValue), MaxStatValue);

                switch (pair.Key.ToLowerInvariant())
                {
                    case "str":
                        SetStr(value);
                        break;
                    case "dex":
                        SetDex(value);
                        break;
                    case "int":
                        SetInt(value);
                        break;
                    default:
                        Console.WriteLine("PersonaCompanion: persona '{0}' has unknown stat '{1}', ignoring", PersonaId, pair.Key);
                        break;
                }
            }
        }

        // Issue #61 part 2: the intent-tell delivery. Called by
        // IntentTells.CheckPassiveIntentTell once the passive Detect
        // Hidden roll succeeds for `perceiver` specifically - text is a
        // generic placeholder pending #60's authored bark tables (out of
        // scope here; disposition-gated barks are that issue's deliverable
        // 7). PrivateOverheadMessage, NEVER Emote() - gotcha (a): Emote is
        // a public broadcast (Server/Mobile.cs) and would leak the tell to
        // every bystander, not just the player who passed the roll.
        public void OnIntentRevealed(Mobile perceiver)
        {
            string text;
            switch (Intent)
            {
                case DispositionSystem.NpcIntent.Malign:
                    text = "*seems to regard you with quiet malice*";
                    break;
                case DispositionSystem.NpcIntent.Benign:
                    text = "*seems to regard you warmly*";
                    break;
                default:
                    text = "*seems to size you up*";
                    break;
            }

            PrivateOverheadMessage(MessageType.Emote, EmoteHue, true, text, perceiver.NetState);
        }

        // Issue #65 part 1: the recruitment quest's only objective is a
        // gold gift (CompanionRecruitmentQuest.GiveGoldGiftObjective); part
        // 2's ongoing loot-share settlement reuses the exact same "owner
        // hands the companion gold" moment. OnGoldGiven is the real
        // BaseCreature hook vendors/pets already use for this (see
        // CompanionEconomy.cs's doc comment) - not a new drag-drop
        // mechanic.
        public override bool OnGoldGiven(Mobile from, Gold dropped)
        {
            if (CompanionEconomy.TryHandleGift(this, from, dropped))
            {
                return true;
            }

            return base.OnGoldGiven(from, dropped);
        }

        // Issue #65 part 3: non-gold gear the owner drops on their recruited
        // companion joins the "party treasury" - which is just this
        // companion's own Backpack, the same container #59's `equip`
        // executor (BotAI.TryEquipItem) already reads from. Gated to the
        // owner only (PackHorse/PackAnimal.CheckAccess is the stock
        // precedent for "who may load a creature's pack," but that helper
        // requires Controlled == true, which BotAI deliberately never sets -
        // see BotAI.cs's CombatAI doc comment on why - so this is a narrower,
        // ControlMaster-only check instead of reusing PackAnimal itself).
        public override bool OnDragDrop(Mobile from, Item dropped)
        {
            if (ControlMaster != null && from == ControlMaster && !(dropped is Gold))
            {
                return AddToBackpack(dropped);
            }

            return base.OnDragDrop(from, dropped);
        }

        // Issue #39 negative input: the owner attacking their own recruited
        // companion. OnDamage is the real Mobile hook every damage source
        // already invokes (BaseCreature overrides it too, for its own
        // speech/aggro bookkeeping) - not a new combat-tracking mechanism.
        // Routed to CompanionBondBehaviors (the Mobile-touching glue for
        // #39's inputs) rather than decided inline, matching how
        // OnGoldGiven/OnDragDrop above route to CompanionEconomy.
        public override void OnDamage(int amount, Mobile from, bool willKill)
        {
            CompanionBondBehaviors.OnAttackedBy(this, from);

            base.OnDamage(amount, from, willKill);
        }

        // Issue #39 positive input: the owner healing their own recruited
        // companion (spell, bandage, or potion - OnHeal fires for all of
        // them, the same real Mobile hook BaseCreature's own OnHeal already
        // overrides for its own bookkeeping).
        public override void OnHeal(ref int amount, Mobile from)
        {
            base.OnHeal(ref amount, from);

            CompanionBondBehaviors.OnHealedBy(this, from);
        }

        // Issue #39 ambient inputs (time-together / abandoned): OnThink
        // fires every AI timer tick regardless of Presence (AITimer.OnTick
        // calls m_Mobile.OnThink() unconditionally, before the
        // Active/Dismissed branch in BotAI.Think()) - the same per-tick hook
        // BaseCreature already uses for its own paralysis/hidden-detection
        // bookkeeping, reused here rather than adding a second timer.
        public override void OnThink()
        {
            base.OnThink();

            CompanionBondBehaviors.OnThink(this);
        }

        // Issue #65 part 1: the recruitment offer. Not yet recruited =
        // ordinary NPC with no ControlMaster and no controllable orders (the
        // issue's own framing) - this entry is the only way that changes,
        // and it disappears the moment ControlMaster is set.
        public override void AddCustomContextEntries(Mobile from, List<ContextMenuEntry> list)
        {
            base.AddCustomContextEntries(from, list);

            if (ControlMaster == null && from is PlayerMobile && from.Alive)
            {
                list.Add(new RecruitCompanionEntry(this, from));
            }
        }

        public PersonaCompanion(Serial serial)
            : base(serial)
        {
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(3); // version

            var botAi = (BotAI)AIObject;
            writer.Write(botAi.BotId);
            writer.Write(botAi.PersonaId);

            // Issue #38: presence (Active/Dismissed), serialized like
            // BotId/PersonaId above - the box restarts constantly (§5), so
            // a dismissed companion must come back dismissed, not silently
            // re-activated.
            writer.Write((int)botAi.Presence);

            // Issue #65: bond state survives the constant idle-shutdown
            // restarts (§5) same as ControlMaster already does via
            // BaseCreature's own serialization.
            writer.Write(AffinityScore);
            writer.Write(LootOwed);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            var version = reader.ReadInt();

            // ChangeAIType (called from base.Deserialize) already re-created
            // BotAI via ForcedAI - without these, a companion that survives a
            // world save comes back with BotId/PersonaId == null (mirrors
            // TestCompanion.cs; the box restarts frequently, so this is the
            // common path, not an edge case).
            if (version >= 1)
            {
                var botAi = (BotAI)AIObject;
                botAi.BotId = reader.ReadString();
                botAi.PersonaId = reader.ReadString();

                // Issue #38: absent on a version-1 save (every companion
                // predating this issue) - BotAI's own field default
                // (Active) already covers that case, so there's nothing to
                // read; only version 2+ ever wrote this value.
                if (version >= 2)
                {
                    botAi.Presence = (CompanionPresence)reader.ReadInt();
                }
            }

            // Issue #65: absent on a version-1 or version-2 save (every
            // companion predating this issue, including #38's own
            // presence-only version 2) - AffinityScore/LootOwed default to
            // 0/Stranger already, matching an unrecruited companion; only
            // version 3+ ever wrote these values.
            if (version >= 3)
            {
                AffinityScore = reader.ReadInt();
                LootOwed = reader.ReadInt();
            }
        }
    }

    // Issue #65 part 1: the offer trigger, mirroring BaseQuester.TalkEntry's
    // idiom (same "Talk" verb/cliloc, same context-menu-driven quest offer)
    // without requiring PersonaCompanion to become a BaseQuester/BaseVendor.
    internal sealed class RecruitCompanionEntry : ContextMenuEntry
    {
        private readonly PersonaCompanion _companion;
        private readonly Mobile _from;

        public RecruitCompanionEntry(PersonaCompanion companion, Mobile from)
            : base(6146, 3) // "Talk"
        {
            _companion = companion;
            _from = from;
        }

        public override void OnClick()
        {
            if (!(_from is PlayerMobile player) || !player.Alive || _companion.Deleted || _companion.ControlMaster != null)
            {
                return;
            }

            if (player.Quest != null)
            {
                if (player.Quest is CompanionRecruitmentQuest existing && existing.Companion == _companion)
                {
                    player.SendMessage("You've already offered to prove yourself to {0}.", _companion.Name);
                }
                else
                {
                    player.SendMessage("Finish (or abandon) your current quest before starting a new one.");
                }

                return;
            }

            new CompanionRecruitmentQuest(player, _companion).SendOffer();
        }
    }
}
