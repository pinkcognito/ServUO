using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Server.Custom.AIAgents;
using Server.Mobiles;

namespace Server.Tests.Tier2;

// Issue #38: headless coverage for the presence gate + serialization
// round-trip acceptance criteria that Tier 1 (TogetherEvaluatorTests) can't
// reach because it needs a real Mobile/AI/serializer, mirroring
// CompanionCombatIntegrationTests' shape for #57.
//
// Coordinates: CompanionWorldFixture's TestMap is only Map.SectorSize * 8 =
// 128 tiles wide/high (CompanionWorldFixture.cs). Reacquire-target logic
// (BaseAI.AcquireFocusMob, via MeleeAI.DoActionGuard's self-heal) silently
// fails to find anything once a mobile's sector falls outside that grid
// (Map.InternalGetSector returns the shared, untracked m_InvalidSector) -
// confirmed by direct experiment during this PR's review pass, where an
// identical scenario at x>=400 failed for an ACTIVE companion too, not just
// a dismissed one. Every coordinate below stays under 100 on both axes
// (comfortable margin under 128), grouped into Y-bands 40 tiles apart (each
// well beyond NearbyRange/RangePerception) with X sub-slots 20 tiles apart
// within a band, so no two tests' mobiles can ever perceive each other.
public class CompanionPresenceIntegrationTests : IClassFixture<CompanionWorldFixture>
{
    private readonly CompanionWorldFixture _fixture;

    public CompanionPresenceIntegrationTests(CompanionWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly Dictionary<string, double> MeleeSkills = new() { ["Swordsmanship"] = 88 };

    // Issue #38 design correction (Opus review): a dismissed companion must
    // not passively stand and die - dismissal suppresses PROACTIVE behavior
    // (wander/follow/goal ticks/decide), not reflexive self-defense. When
    // attacked, a dismissed companion engages exactly like an active one -
    // same CombatAI delegation, same Combatant.
    [Fact]
    public void Dismissed_DefendsSelfWhenAttacked()
    {
        var companion = _fixture.CreateCompanion(new Point3D(20, 20, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(21, 20, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            attacker.DoHarmful(companion);

            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            Assert.Equal(attacker, companion.Combatant);
            Assert.Equal(ActionType.Combat, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    // Mirrors CompanionCombatIntegrationTests.LowHp_CompanionFlees (#57)
    // but dismissed - proves the full combat-AI delegation (not just a
    // basic swing) stays available while dormant, per the review's "half
    // measure" finding: flee-when-low lives inside CombatAI, which the old
    // top-of-Think() early return skipped entirely for a dismissed bot.
    [Fact]
    public void Dismissed_FleesWhenLowHp()
    {
        var companion = _fixture.CreateCompanion(new Point3D(40, 20, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(41, 20, 0), str: 1000);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            // Below the 20%-of-max flee threshold (MeleeAI.DoActionCombat).
            companion.Hits = companion.HitsMax * 10 / 100;

            attacker.DoHarmful(companion);
            companion.Combatant = attacker;
            attacker.Combatant = companion;

            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            Assert.Equal(ActionType.Flee, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    // The other half of the correction: dismissed still means dormant when
    // nothing is actually threatening the companion - no wander, no
    // pursuing a stale FollowTarget.
    [Fact]
    public void Dismissed_DoesNotFollowOrWanderWhenUnthreatened()
    {
        var companion = _fixture.CreateCompanion(new Point3D(60, 20, 0), MeleeSkills);
        var friend = _fixture.CreateAttacker(new Point3D(65, 20, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            // Set directly (bypassing SetPresence's own clear) to model a
            // FollowTarget left over from before dismissal, or set by some
            // other path - either way Think() must ignore it while
            // dismissed and unthreatened.
            botAi.FollowTarget = friend;

            var before = companion.Location;

            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            Assert.Equal(before, companion.Location);
            Assert.Null(companion.Combatant);
        }
        finally
        {
            companion.Delete();
            friend.Delete();
        }
    }

    // Issue #38 design correction: the GuardTarget-defends-an-ally
    // heuristic (#59) is Active-only - a dismissed companion defends
    // ITSELF (covered above), not an ally it happens to be guarding.
    // Proactively wading into someone else's fight is exactly the
    // "proactive behavior" dismissal suppresses.
    [Fact]
    public void Dismissed_GuardTargetHeuristicIsActiveOnly()
    {
        var companion = _fixture.CreateCompanion(new Point3D(80, 20, 0), MeleeSkills);
        var ally = _fixture.CreateAttacker(new Point3D(85, 20, 0));
        var attacker = _fixture.CreateAttacker(new Point3D(90, 20, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);
            botAi.GuardTarget = ally;

            // Only the ally is attacked - companion itself is never
            // touched, so its own reflexive AggressiveAction never fires.
            attacker.DoHarmful(ally);

            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            Assert.Null(companion.Combatant);
            Assert.Equal(ActionType.Wander, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            ally.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void Dismissed_DropsStandingFollowAndGuardOrders()
    {
        var companion = _fixture.CreateCompanion(new Point3D(20, 60, 0), MeleeSkills);
        var friend = _fixture.CreateAttacker(new Point3D(25, 60, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.FollowTarget = friend;
            botAi.GuardTarget = friend;

            botAi.SetPresence(CompanionPresence.Dismissed);

            Assert.Null(botAi.FollowTarget);
            Assert.Null(botAi.GuardTarget);
        }
        finally
        {
            companion.Delete();
            friend.Delete();
        }
    }

    [Fact]
    public void Dismissed_ThinkKeepsAiTimerAlive()
    {
        // Issue #38: Think() must return true (not false) while dismissed -
        // AITimer.OnTick stops the timer outright on a false return, and
        // nothing re-arms it, which would strand a re-summoned companion
        // with a permanently-stopped AI timer.
        var companion = _fixture.CreateCompanion(new Point3D(40, 60, 0), MeleeSkills);

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            Assert.True(botAi.Think());
        }
        finally
        {
            companion.Delete();
        }
    }

    [Fact]
    public void Dismissed_HandlesOnSpeechIsFalse_NoDecideCallPossible()
    {
        var companion = _fixture.CreateCompanion(new Point3D(60, 60, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(61, 60, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            // This is the actual gate: the engine's own speech dispatch
            // (Mobile/Region) only ever calls OnSpeech for a handler whose
            // HandlesOnSpeech returned true for that speaker. False here
            // means OnSpeech - and therefore /decide - is unreachable via
            // speech while dismissed, regardless of what OnSpeech's own
            // body does.
            Assert.False(botAi.HandlesOnSpeech(speaker));
        }
        finally
        {
            companion.Delete();
            speaker.Delete();
        }
    }

    [Fact]
    public void Active_HandlesOnSpeechStillTrue()
    {
        var companion = _fixture.CreateCompanion(new Point3D(80, 60, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(81, 60, 0));

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

    // Issue #38 design correction: self-defense while dismissed is fully
    // deterministic (CombatAI delegation, same as an active companion) -
    // it must never reach the sidecar. Replaces the previous near-vacuous
    // "NeverCallsDecideSidecarEvenWhenSpokenTo" test (flagged in review:
    // it called Think() with an unused `speaker` and proved nothing about
    // speech) - that speech-path guarantee is now covered directly by
    // Dismissed_HandlesOnSpeechIsFalse_NoDecideCallPossible above (the
    // real gate: the engine's own speech dispatch never calls OnSpeech for
    // a handler whose HandlesOnSpeech returned false), and this test
    // instead covers the actually-new behavior this PR adds: dismissed
    // self-defense.
    [Fact]
    public async Task Dismissed_SelfDefenseNeverCallsDecideSidecar()
    {
        var companion = _fixture.CreateCompanion(new Point3D(20, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(21, 100, 0));

        var callCount = 0;
        var originalTransport = AsyncDecisionPump.Transport;
        AsyncDecisionPump.Transport = _ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<DecisionResponse>(null!);
        };

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            attacker.DoHarmful(companion);

            for (var i = 0; i < 10; i++)
            {
                botAi.Think();
            }

            // Sanity check that self-defense actually ran (otherwise this
            // test would trivially pass for the wrong reason).
            Assert.Equal(attacker, companion.Combatant);
            Assert.Equal(0, callCount);
        }
        finally
        {
            AsyncDecisionPump.Transport = originalTransport;
            companion.Delete();
            attacker.Delete();
        }
    }

    // Issue #38 acceptance: "the state survives a world save/restart."
    // Drives the real PersonaCompanion.Serialize/Deserialize pair (not a
    // hand-rolled stand-in) through an in-memory stream - the same
    // BinaryFileWriter/BinaryFileReader pair World.Save/World.Load use on
    // a real box, just backed by a MemoryStream instead of a Saves/ file so
    // this runs headless. Only Mobile-level state is exercised (no Items -
    // this fixture's Item-construction boundary, see CompanionWorldFixture's
    // doc comment); PersonaCompanion carries no items, so that's exactly
    // what production does too.
    [Fact]
    public void Dismissed_PresenceRoundTripsThroughSerialization()
    {
        var companion = _fixture.CreateCompanion(new Point3D(40, 100, 0), MeleeSkills);
        var botAi = (BotAI)companion.AIObject;
        botAi.SetPresence(CompanionPresence.Dismissed);

        try
        {
            using var stream = new MemoryStream();

            var writer = new BinaryFileWriter(stream, true);
            companion.Serialize(writer);
            writer.Flush();

            stream.Position = 0;

            var reader = new BinaryFileReader(new BinaryReader(stream));
            var reloaded = new PersonaCompanion(companion.Serial);
            reloaded.Deserialize(reader);

            var reloadedBotAi = (BotAI)reloaded.AIObject;

            Assert.Equal(CompanionPresence.Dismissed, reloadedBotAi.Presence);
            Assert.Equal(botAi.BotId, reloadedBotAi.BotId);
            Assert.Equal(botAi.PersonaId, reloadedBotAi.PersonaId);
        }
        finally
        {
            companion.Delete();
        }
    }

    [Fact]
    public void Active_PresenceRoundTripsThroughSerialization()
    {
        var companion = _fixture.CreateCompanion(new Point3D(60, 100, 0), MeleeSkills);
        var botAi = (BotAI)companion.AIObject;

        try
        {
            using var stream = new MemoryStream();

            var writer = new BinaryFileWriter(stream, true);
            companion.Serialize(writer);
            writer.Flush();

            stream.Position = 0;

            var reader = new BinaryFileReader(new BinaryReader(stream));
            var reloaded = new PersonaCompanion(companion.Serial);
            reloaded.Deserialize(reader);

            var reloadedBotAi = (BotAI)reloaded.AIObject;

            Assert.Equal(CompanionPresence.Active, reloadedBotAi.Presence);
        }
        finally
        {
            companion.Delete();
        }
    }
}
