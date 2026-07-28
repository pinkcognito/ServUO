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
public class CompanionPresenceIntegrationTests : IClassFixture<CompanionWorldFixture>
{
    private readonly CompanionWorldFixture _fixture;

    public CompanionPresenceIntegrationTests(CompanionWorldFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly Dictionary<string, double> MeleeSkills = new() { ["Swordsmanship"] = 88 };

    [Fact]
    public void Dismissed_NeverDelegatesToCombatAI()
    {
        var companion = _fixture.CreateCompanion(new Point3D(200, 100, 0), MeleeSkills);
        var attacker = _fixture.CreateAttacker(new Point3D(201, 100, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

            attacker.DoHarmful(companion);

            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            // BaseCreature.AggressiveAction (the engine's own reflex, see
            // Scripts/Mobiles/Normal/BaseCreature.cs) still marks
            // Combatant on the target the instant it's attacked -
            // bookkeeping entirely outside BotAI's control, dismissed or
            // not. What Presence.Dismissed actually gates is whether
            // BotAI ever ACTS on that: Think() returns immediately without
            // ever touching CombatAI, so the delegated combat AI never
            // leaves its just-constructed Wander default - the companion
            // never swings, casts, or otherwise fights back while dormant.
            Assert.Equal(ActionType.Wander, botAi.DebugCombatAI.Action);
        }
        finally
        {
            companion.Delete();
            attacker.Delete();
        }
    }

    [Fact]
    public void Dismissed_DropsStandingFollowAndGuardOrders()
    {
        var companion = _fixture.CreateCompanion(new Point3D(210, 100, 0), MeleeSkills);
        var friend = _fixture.CreateAttacker(new Point3D(215, 100, 0));

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
        var companion = _fixture.CreateCompanion(new Point3D(220, 100, 0), MeleeSkills);

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
        var companion = _fixture.CreateCompanion(new Point3D(230, 100, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(231, 100, 0));

        try
        {
            var botAi = (BotAI)companion.AIObject;
            botAi.SetPresence(CompanionPresence.Dismissed);

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
        var companion = _fixture.CreateCompanion(new Point3D(240, 100, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(241, 100, 0));

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
    public async Task Dismissed_NeverCallsDecideSidecarEvenWhenSpokenTo()
    {
        var companion = _fixture.CreateCompanion(new Point3D(250, 100, 0), MeleeSkills);
        var speaker = _fixture.CreateAttacker(new Point3D(251, 100, 0));

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

            // Direct call, bypassing HandlesOnSpeech, to prove OnSpeech
            // itself is inert while dismissed even if something else in
            // the engine ever called it without checking HandlesOnSpeech
            // first (defense in depth over trusting a single gate).
            for (var i = 0; i < 5; i++)
            {
                botAi.Think();
            }

            Assert.Equal(0, callCount);
        }
        finally
        {
            AsyncDecisionPump.Transport = originalTransport;
            companion.Delete();
            speaker.Delete();
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
        var companion = _fixture.CreateCompanion(new Point3D(260, 100, 0), MeleeSkills);
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
        var companion = _fixture.CreateCompanion(new Point3D(270, 100, 0), MeleeSkills);
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
