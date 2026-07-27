using System;

using Server.Items;

namespace Server.Custom.AIAgents
{
    // Issue #62: a lightweight, headlessly-testable stand-in for tile-based
    // HarvestSystem mining (server/Scripts/Services/Harvest/Mining.cs). Real
    // mining needs actual mountain/cave map tiles (HarvestDefinition.Tiles)
    // that Server.Tests' CompanionWorldFixture doesn't have - its own doc
    // comment already documents real terrain as Tier 3 territory, out of
    // scope for a headless fixture. This models "a depletable resource at a
    // location" directly as a placed Item instead: same precondition shape
    // real mining uses (a trained skill gates success), same "can be
    // exhausted" behavior the acceptance criteria need ("kill the ore vein
    // mid-plan"), but no .mul dependency. A real HarvestSystem adapter can
    // replace PlanExecutor's mine-step handler later without this class or
    // the executor's trigger logic changing - the same "one seam, swapped
    // later" shape as the planner itself.
    public class PlanResourceNode : Item
    {
        [Constructable]
        public PlanResourceNode()
            : this(typeof(IronOre), 5, SkillName.Mining)
        {
        }

        public PlanResourceNode(Type resourceType, int charges, SkillName skill)
            : base(0x19B7)
        {
            Movable = false;
            ResourceType = resourceType;
            ChargesRemaining = charges;
            Skill = skill;
        }

        public PlanResourceNode(Serial serial)
            : base(serial)
        {
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public Type ResourceType { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ChargesRemaining { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public SkillName Skill { get; set; }

        // Deterministic by design (no CheckSkill roll): this node exists to
        // make PlanExecutor's mine-step handling and the step-failed trigger
        // testable, not to reproduce Mining's own success-chance formula.
        // Success = the node has charges left and the harvester has any
        // training in the required skill (matches ActionValidator.CanUseSkill's
        // "trained" bar for use_skill).
        public bool TryHarvest(Mobile harvester, out Item result)
        {
            result = null;

            if (Deleted || ChargesRemaining <= 0 || harvester == null)
            {
                return false;
            }

            if (!ActionValidator.CanUseSkill(harvester.Skills[Skill].Base))
            {
                return false;
            }

            ChargesRemaining--;
            result = (Item)Activator.CreateInstance(ResourceType);
            return true;
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(0); // version
            writer.Write(ResourceType?.FullName ?? string.Empty);
            writer.Write(ChargesRemaining);
            writer.Write((int)Skill);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            reader.ReadInt();
            var typeName = reader.ReadString();
            ResourceType = string.IsNullOrEmpty(typeName) ? typeof(IronOre) : Type.GetType(typeName) ?? typeof(IronOre);
            ChargesRemaining = reader.ReadInt();
            Skill = (SkillName)reader.ReadInt();
        }
    }
}
