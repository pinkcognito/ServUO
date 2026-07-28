using System.Collections.Generic;

using Server.Custom.AIAgents;
using Server.Mobiles;

namespace Server.Tests.Tier2;

// Issue #39: headless coverage for the tier-gate wiring (Acquaintance->chat,
// Friend->follow), the §11 bonded-awake-exception predicate, and the direct
// Mobile-touching behavior hooks (OnAttackedBy/OnHealedBy/co-combat) that
// Tier 1's pure tests (CompanionBondTests, CompanionBondBehaviorTests) can't
// reach because they need a real Mobile/BotAI/ControlMaster relationship.
// Mirrors CompanionCombatIntegrationTests' (#68) and
// CompanionPresenceIntegrationTests' (#38) shape.
//
// Corrected 2026-07-28 (owner review on PR #16): defend/attack are NOT
// bond-tier gated - a recruited companion (quest-gated, sharing in loot)
// obeys combat orders by default; bond x risk x reward compliance is #63
// Reaction & Resolve's numeric job, not built yet. Defend_WorksRegardlessOfBondTier
// below locks that in; the old Bonded-gate rejection/acceptance tests are
// gone.
//
// A plain BaseCreature (CompanionWorldFixture.CreateAttacker) stands in for
// the recruiting owner throughout: every check this issue wires
// (BotAI.MeetsBondTier, OnAttackedBy, OnHealedBy, co-combat) only ever
// compares companion.ControlMaster by Mobile reference equality, never
// anything PlayerMobile-specific - and constructing a real PlayerMobile is
// out of this fixture's reach (CompanionWorldFixture's own doc comment: no
// Server.Items.* construction, which a real player's starting equipment
// would need).
//
// The Friend->follow gate is asserted via BotAI.MeetsBondTier directly
// (a Tier 2 test-only seam, same precedent as DebugCombatAI) rather than
// through Think()'s actual movement: that would compete with
// base.Think()'s own wander RNG (WalkRandomInHome), which
// CompanionCombatIntegrationTests' own doc comment already documents as
// out of this harness's reach for deterministic assertions.
//
// Coordinates below stay inside the fixture's TestMap bounds
// (Map.SectorSize * 8 = 128 tiles square, CompanionWorldFixture.cs) -
// the "defend" tests resolve their target through the real
// BotAI.NearbyMobileCandidates -> Map.GetMobilesInRange sector scan, which
// (unlike CompanionCombatIntegrationTests' Aggressors-list-based combat
// tests, or CompanionPresenceIntegrationTests' serialization-only tests)
// silently returns nothing for a Mobile placed outside the registered
// map's sector grid.
public class CompanionBondTierGateIntegrationTests : IClassFixture<CompanionWorldFixture>
{
    private readonly CompanionWorldFixture _fixture;

    public CompanionBondTierGateIntegrationTests(CompanionWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly Dictionary<string, double> MeleeSkills = new() { ["Swordsmanship"] = 88 };

    [Fact]
    public void HandlesOnSpeech_FalseBelowAcquaintanceForARecruitedCompanion()
    {
        var companion = _fixture.CreateCompanion(new Point3D(10, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(11, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold - 1;

            var botAi = (BotAI)companion.AIObject;

            Assert.False(botAi.HandlesOnSpeech(owner));
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void HandlesOnSpeech_TrueAtAcquaintanceForARecruitedCompanion()
    {
        var companion = _fixture.CreateCompanion(new Point3D(15, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(16, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold;

            var botAi = (BotAI)companion.AIObject;

            Assert.True(botAi.HandlesOnSpeech(owner));
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void HandlesOnSpeech_TrueForAnUnrecruitedCompanionRegardlessOfScore()
    {
        // #65's own framing: "until recruited, the persona is an ordinary
        // (possibly conversational) NPC" - the tier gate must not apply
        // before there's an owner bond to gate.
        var companion = _fixture.CreateCompanion(new Point3D(20, 10, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(21, 10, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;

            Assert.True(botAi.HandlesOnSpeech(speaker));
        }
        finally
        {
            companion.Delete();
            speaker.Delete();
        }
    }

    [Fact]
    public void MeetsBondTier_FalseBelowFriendForARecruitedCompanion()
    {
        var companion = _fixture.CreateCompanion(new Point3D(25, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(26, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.FriendThreshold - 1;

            var botAi = (BotAI)companion.AIObject;

            Assert.False(botAi.MeetsBondTier(CompanionBond.Tier.Friend));
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void MeetsBondTier_TrueAtFriendForARecruitedCompanion()
    {
        var companion = _fixture.CreateCompanion(new Point3D(30, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(31, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.FriendThreshold;

            var botAi = (BotAI)companion.AIObject;

            Assert.True(botAi.MeetsBondTier(CompanionBond.Tier.Friend));
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void MeetsBondTier_TrueForAnUnrecruitedCompanionRegardlessOfScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(35, 10, 0), MeleeSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;

            Assert.True(botAi.MeetsBondTier(CompanionBond.Tier.Bonded));
        }
        finally
        {
            companion.Delete();
        }
    }

    // Issue #39 correction (owner review on PR #16, 2026-07-28): defend has
    // no bond-*tier* gate (no Friend/Bonded threshold to clear) - superseded
    // by issue #63's numeric compliance roll, which reads bond/loyalty as a
    // continuous input instead. The two tests below replace the old
    // "always works" lock-in: a companion still defends readily against a
    // target it can handle, but a badly outmatched order (against a
    // default-strength CreateAttacker, str=500, next to a fresh companion's
    // Swordsmanship 88) now fails its morale check and flees rather than
    // wading in - "obeys combat orders by default" no longer means
    // "unconditionally."
    [Fact]
    public void Defend_Complies_WhenCompanionCanHandleTheTarget()
    {
        var companion = _fixture.CreateCompanion(new Point3D(40, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(41, 10, 0));
        var threat = _fixture.CreateAttacker(new Point3D(45, 10, 0), str: 1);
        threat.Name = "Threat";

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.MinScore;

            var botAi = (BotAI)companion.AIObject;
            Assert.Equal(CompanionBond.Tier.Stranger, companion.AffinityTier);

            // Deterministic: with a target this weak, effective morale is
            // comfortably positive, and a roll of 7 (2d6's own average)
            // holds it. See ReactionResolveTests for the pure-function
            // coverage of every roll/margin.
            botAi.DebugDiceRoller = new FixedDiceRoller(7);

            botAi.ApplyActions(new List<DecisionAction>
            {
                new DecisionAction { Type = "defend", Target = threat.Name },
            });

            Assert.Equal(threat, botAi.GuardTarget);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
            threat.Delete();
        }
    }

    [Fact]
    public void Defend_Refuses_WhenBadlyOutmatched()
    {
        var companion = _fixture.CreateCompanion(new Point3D(42, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(43, 10, 0));
        // CreateAttacker's default str=500 is far beyond this companion's
        // reach (100 hp, Swordsmanship 88) - effective morale lands well
        // below zero, so this fails on every possible 2d6 roll (2-12); no
        // DebugDiceRoller override needed to make the assertion reliable.
        var threat = _fixture.CreateAttacker(new Point3D(46, 10, 0));
        threat.Name = "ToughThreat";

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.MinScore;

            var botAi = (BotAI)companion.AIObject;

            botAi.ApplyActions(new List<DecisionAction>
            {
                new DecisionAction { Type = "defend", Target = threat.Name },
            });

            Assert.Null(botAi.GuardTarget);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
            threat.Delete();
        }
    }

    // Issue #39 correction: the new co-combat input - "the companion
    // fighting/defending alongside its owner... raises bond," the
    // reciprocal of OnHealedBy/OnAttackedBy. Populates real DamageEntries
    // via Mobile.RegisterDamage (no Item/Corpse involved, unlike a full
    // Mobile.Kill()) and invokes the OnKilledBy handler directly - see that
    // handler's own doc comment (CompanionBondBehaviors.cs) for why.
    [Fact]
    public void OnKilledByCoCombatCheck_BothOwnerAndCompanionFoughtRaisesBondScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(50, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(51, 10, 0));
        var threat = _fixture.CreateAttacker(new Point3D(55, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold;

            threat.RegisterDamage(10, owner);
            threat.RegisterDamage(10, companion);

            CompanionBondBehaviors.OnKilledByCoCombatCheck(new OnKilledByEventArgs(threat, owner));

            Assert.True(companion.AffinityScore > CompanionBond.AcquaintanceThreshold);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
            threat.Delete();
        }
    }

    [Fact]
    public void OnKilledByCoCombatCheck_CompanionAloneDoesNotMoveBondScore()
    {
        // The owner never fought this threat - only the companion did (e.g.
        // it wandered off and picked its own fight) - not co-combat, no
        // bond effect either way.
        var companion = _fixture.CreateCompanion(new Point3D(56, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(57, 10, 0));
        var threat = _fixture.CreateAttacker(new Point3D(60, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold;

            threat.RegisterDamage(10, companion);

            CompanionBondBehaviors.OnKilledByCoCombatCheck(new OnKilledByEventArgs(threat, owner));

            Assert.Equal(CompanionBond.AcquaintanceThreshold, companion.AffinityScore);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
            threat.Delete();
        }
    }

    [Fact]
    public void IsBondedAwakeException_FalseBelowBondedTier()
    {
        var companion = _fixture.CreateCompanion(new Point3D(60, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(61, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.BondedThreshold - 1;

            var botAi = (BotAI)companion.AIObject;

            Assert.False(botAi.IsBondedAwakeException());
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void IsBondedAwakeException_FalseAtBondedTierWithoutAnOnlineOwner()
    {
        // The owner stand-in here has no real NetState (no network
        // connection in this headless harness), so IsTogetherWithOwner is
        // false per #38's own "Active + owner online" definition - the
        // exception must not apply without an online owner even at Bonded
        // tier. Exercising the true/true path needs a live NetState, which
        // is outside this harness's reach (same boundary
        // CompanionPresenceIntegrationTests already lives with for
        // presence).
        var companion = _fixture.CreateCompanion(new Point3D(65, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(66, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.BondedThreshold;

            var botAi = (BotAI)companion.AIObject;

            Assert.False(botAi.IsBondedAwakeException());
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void OnAttackedBy_OwnerLowersBondScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(70, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(71, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.FriendThreshold;

            companion.OnDamage(10, owner, false);

            Assert.True(companion.AffinityScore < CompanionBond.FriendThreshold);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void OnAttackedBy_NonOwnerDamageDoesNotMoveBondScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(75, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(76, 10, 0));
        var stranger = _fixture.CreateAttacker(new Point3D(77, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.FriendThreshold;

            companion.OnDamage(10, stranger, false);

            Assert.Equal(CompanionBond.FriendThreshold, companion.AffinityScore);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
            stranger.Delete();
        }
    }

    [Fact]
    public void OnHealedBy_OwnerRaisesBondScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(80, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(81, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold;

            var amount = 10;
            companion.OnHeal(ref amount, owner);

            Assert.True(companion.AffinityScore > CompanionBond.AcquaintanceThreshold);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void OnHealedBy_SelfHealDoesNotMoveBondScore()
    {
        var companion = _fixture.CreateCompanion(new Point3D(85, 10, 0), MeleeSkills);
        var owner = _fixture.CreateAttacker(new Point3D(86, 10, 0));

        try
        {
            companion.ControlMaster = owner;
            companion.AffinityScore = CompanionBond.AcquaintanceThreshold;

            var amount = 10;
            companion.OnHeal(ref amount, companion);

            Assert.Equal(CompanionBond.AcquaintanceThreshold, companion.AffinityScore);
        }
        finally
        {
            companion.Delete();
            owner.Delete();
        }
    }

    [Fact]
    public void SetPresence_TracksAndClearsDismissedSinceUtc()
    {
        var companion = _fixture.CreateCompanion(new Point3D(90, 10, 0), MeleeSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;

            Assert.Null(botAi.DismissedSinceUtc);

            botAi.SetPresence(CompanionPresence.Dismissed);
            Assert.NotNull(botAi.DismissedSinceUtc);

            botAi.SetPresence(CompanionPresence.Active);
            Assert.Null(botAi.DismissedSinceUtc);
        }
        finally
        {
            companion.Delete();
        }
    }
}
