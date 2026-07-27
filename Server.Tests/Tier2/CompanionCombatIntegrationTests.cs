using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Server.Custom.AIAgents;
using Server.Mobiles;

namespace Server.Tests.Tier2;

// Issue #68 Tier 2: headless integration coverage for the #57 acceptance
// criteria that Tier 1's pure-function tests (CombatStanceSelectorTests,
// SkillNameAliasesTests, etc.) can't reach because they need a real
// Mobile/Combatant/AI delegation loop. Time is advanced by calling
// BotAI.Think() directly rather than the real timer wheel (per the issue's
// own guidance), so every test here is deterministic and fast.
//
// What this harness stubs vs requires (documented per the issue's escalate
// clause): acquisition, stance selection, and Combatant assignment need no
// map data at all - TileMatrix.GetLandBlock returns an all-default,
// passable block when no map*.mul is on disk, which is also why the
// movement-driven flee/follow assertions below happen to work without one.
// Genuinely pathing-dependent behavior (flee-to-a-specific-safe-tile,
// obstacle-aware follow) is out of scope here and would need real map data
// (Tier 3 territory) - not covered by this PR.
public class CompanionCombatIntegrationTests : IClassFixture<CompanionWorldFixture>
{
    private readonly CompanionWorldFixture _fixture;

    public CompanionCombatIntegrationTests(CompanionWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly Dictionary<string, double> MeleeSkills = new() { ["Swordsmanship"] = 88 };

    // The very first Think() after a hit lazily creates the delegated combat
    // AI; that AI's own BaseAI ctor sets its fresh Action to Wander, whose
    // OnActionChanged side effect (BaseAI.cs) nulls m_Mobile.Combatant right
    // back out. It self-heals the next tick - DoActionGuard's AcquireFocusMob
    // re-finds the same attacker via the Aggressors list BotAI never cleared -
    // same as real play, where ticks run continuously and this never reads as
    // a dropped target. So tests advance a few ticks rather than asserting on
    // tick 1, matching the issue's own "assert after N ticks" framing.
    private static void RunTicks(BotAI botAi, int count = 3)
    {
        for (var i = 0; i < count; i++)
        {
            botAi.Think();
        }
    }

    [Fact]
    public void Provoke_CompanionEngagesAttacker()
    {
        var companion = _fixture.CreateCompanion(new Point3D(100, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(101, 100, 0));

        try
        {
            attacker.DoHarmful(companion);

            var botAi = (BotAI)companion.AIObject;
            RunTicks(botAi);

            Assert.Equal(attacker, companion.Combatant);
            Assert.Equal(ActionType.Combat, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void LowHp_CompanionFlees()
    {
        var companion = _fixture.CreateCompanion(new Point3D(110, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(111, 100, 0), str: 1000);

        try
        {
            // Below the 20%-of-max flee threshold (MeleeAI.DoActionCombat).
            companion.Hits = companion.HitsMax * 10 / 100;

            attacker.DoHarmful(companion);
            companion.Combatant = attacker;
            attacker.Combatant = companion;

            var botAi = (BotAI)companion.AIObject;
            RunTicks(botAi);

            Assert.Equal(ActionType.Flee, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void Stance_HighMagerySkillSelectsMageAI()
    {
        var companion = _fixture.CreateCompanion(new Point3D(120, 100, 0), new Dictionary<string, double> { ["Magery"] = 80 });
        var attacker = _fixture.CreateAttacker(new Point3D(121, 100, 0));

        try
        {
            attacker.DoHarmful(companion);

            var botAi = (BotAI)companion.AIObject;
            RunTicks(botAi);

            Assert.IsType<MageAI>(botAi.DebugCombatAI);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void Stance_HighSwordsmanshipSelectsMeleeAI()
    {
        var companion = _fixture.CreateCompanion(new Point3D(130, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(131, 100, 0));

        try
        {
            attacker.DoHarmful(companion);

            var botAi = (BotAI)companion.AIObject;
            RunTicks(botAi);

            Assert.IsType<MeleeAI>(botAi.DebugCombatAI);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void Follow_YieldsToSelfDefense()
    {
        var companion = _fixture.CreateCompanion(new Point3D(140, 100, 0), MeleeSkills);
        var friend = _fixture.CreateAttacker(new Point3D(145, 100, 0));
        var attacker = _fixture.CreateAttacker(new Point3D(141, 100, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.FollowTarget = friend;

            // No combatant yet: Think() should follow rather than idle.
            botAi.Think();
            Assert.Null(companion.Combatant);

            attacker.DoHarmful(companion);
            RunTicks(botAi);

            // Engaging self-defense takes over even though FollowTarget is
            // still set (BotAI.Think()'s combat-priority branch, issue #57).
            Assert.Equal(attacker, companion.Combatant);
            Assert.NotNull(botAi.FollowTarget);
            Assert.Equal(ActionType.Combat, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            friend.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public async Task Combat_NeverCallsDecideSidecar()
    {
        var companion = _fixture.CreateCompanion(new Point3D(150, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(151, 100, 0));

        var callCount = 0;
        var originalTransport = AsyncDecisionPump.Transport;
        AsyncDecisionPump.Transport = _ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<DecisionResponse>(null!);
        };

        try
        {
            attacker.DoHarmful(companion);

            var botAi = (BotAI)companion.AIObject;

            for (var i = 0; i < 10; i++)
            {
                botAi.Think();
            }

            Assert.Equal(0, callCount);
        }
        finally
        {
            AsyncDecisionPump.Transport = originalTransport;
            companion.Delete();
            attacker.Delete();
        }
    }

    // Regression test for pinkcognito/ServUO#7 (issue #57): a parallel
    // AITimer on the delegated combat AI used to tick combatAI.Think()
    // independently of BotAI's own gate, double-driving the mobile. Fails
    // if BotAI.CreateCombatAI ever stops calling combatAI.m_Timer.Stop().
    [Fact]
    public void DelegatedCombatAI_DoesNotRunItsOwnTimer()
    {
        var companion = _fixture.CreateCompanion(new Point3D(160, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(161, 100, 0));

        try
        {
            attacker.DoHarmful(companion);

            var botAi = (BotAI)companion.AIObject;
            RunTicks(botAi);

            Assert.False(botAi.DebugCombatAI.m_Timer.Running);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }
}
