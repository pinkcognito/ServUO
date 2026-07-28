using System;

using Server.Engines.Quests;
using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Issue #65 part 1: the explicit, quest-gated bond start (resolves epic
    // #35 decision 2 - "not a bare command, not a purely emergent
    // threshold"). Built on the classic Server.Engines.Quests engine
    // (QuestSystem/QuestObjective, Scripts/Services/Quests/) - the same
    // framework SolenMatriarchQuest/TerribleHatchlingsQuest/etc. use - not a
    // new quest framework. PersonaCompanion isn't a BaseVendor/MondainQuester
    // (that would mean re-deriving the whole companion body from a vendor),
    // so the "quester" here is the plain BaseCreature companion itself,
    // offering the quest via a context-menu entry (PersonaCompanion.
    // AddCustomContextEntries) rather than MondainQuester's OnTalk/gump
    // dialog plumbing - QuestSystem has no dependency on BaseQuester, only
    // BaseQuester's *own* convenience methods do.
    //
    // The single objective is a gold gift (GiveGoldGiftObjective below) -
    // thematically it's the same "gift-giving" signal #39's future bond
    // deltas will weight heavily, and mechanically it reuses the exact same
    // gold-drop entry point (CompanionEconomy.TryHandleGift /
    // BaseCreature.OnGoldGiven) that issue #65's ongoing loot-share economy
    // uses - one code path for "hand the companion gold," two callers.
    //
    // Completion is immediate on the gift landing (GiveGoldGiftObjective.
    // OnComplete calls System.Complete() directly) rather than requiring a
    // "return to the quest giver to turn in" step - with one objective and
    // the quester **being** the reward's target, a separate turn-in gump
    // would be dialog for its own sake. Simplification is called out here,
    // not silently taken.
    public class CompanionRecruitmentQuest : QuestSystem
    {
        private static readonly Type[] m_TypeReferenceTable = { typeof(GiveGoldGiftObjective) };

        private Mobile m_Companion;

        public CompanionRecruitmentQuest(PlayerMobile from, PersonaCompanion companion)
            : base(from)
        {
            m_Companion = companion;
        }

        // Required by QuestSerializer.Construct (Activator.CreateInstance)
        // for deserialization - mirrors every other QuestSystem subclass.
        public CompanionRecruitmentQuest()
        {
        }

        public PersonaCompanion Companion => m_Companion as PersonaCompanion;

        public override Type[] TypeReferenceTable => m_TypeReferenceTable;

        public override object Name => string.Format("Recruit {0}", Companion?.Name ?? "your companion");

        public override object OfferMessage =>
            string.Format(
                "I've fought at your side, but I'm not yours to command - not yet. " +
                "Prove your good faith with a gift of {0} gold, freely given, and I'll follow you as my own.",
                GiveGoldGiftObjective.RequiredGoldGift);

        public override TimeSpan RestartDelay => TimeSpan.Zero;

        public override bool IsTutorial => false;

        public override int Picture => 0x2AB7; // a bag of gold

        public override void Accept()
        {
            base.Accept();

            AddObjective(new GiveGoldGiftObjective());
        }

        // The box restarts constantly (§5) - a companion despawned/deleted
        // (GM DespawnPersona, disabled persona) while a recruitment quest is
        // outstanding must not leave the player stuck with a permanently
        // uncompletable quest occupying their one classic-engine quest slot.
        public override void Slice()
        {
            if (Companion == null || Companion.Deleted)
            {
                From.SendMessage("The companion you were trying to recruit is no longer here.");
                Cancel();
                return;
            }

            base.Slice();
        }

        // Issue #65 part 1: the actual handoff. #38 (summon/dismiss)
        // consumes ControlMaster as its precondition; #39's future bond
        // tiers read the seeded AffinityScore this sets via
        // CompanionEconomy.Recruit.
        public override void Complete()
        {
            if (Companion != null && !Companion.Deleted)
            {
                CompanionEconomy.Recruit(Companion, From);
            }

            base.Complete();
        }

        public override void ChildSerialize(GenericWriter writer)
        {
            writer.WriteEncodedInt(0); // version

            writer.Write(m_Companion);
        }

        public override void ChildDeserialize(GenericReader reader)
        {
            var version = reader.ReadEncodedInt();

            m_Companion = reader.ReadMobile();
        }
    }

    // Issue #65 part 1's single objective: a bounded-progress gold gift,
    // driven by CompanionEconomy.TryHandleGift (via PersonaCompanion.
    // OnGoldGiven) rather than any client-menu interaction - dropping gold
    // on the companion IS "attempting the objective," the same physical
    // action the OfferMessage above describes.
    public class GiveGoldGiftObjective : QuestObjective
    {
        public const int RequiredGoldGift = 100;

        public override int MaxProgress => RequiredGoldGift;

        public override object Message =>
            string.Format("Give a gift of {0} gold to earn their trust.", RequiredGoldGift);

        public override void OnComplete()
        {
            this.System.Complete();
        }
    }
}
