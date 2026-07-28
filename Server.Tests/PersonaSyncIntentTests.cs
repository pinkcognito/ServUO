using Server.Custom.AIAgents;

namespace Server.Tests
{
    // Issue #61: NpcIntent is sourced from the persona record (like
    // skills/stats/spawn_mode), never a new serialized field on
    // PersonaCompanion - see that class's Intent property doc comment.
    // These tests cover the string->enum parsing rule in isolation;
    // PersonaSync's _known dict itself is only ever populated via the
    // live sidecar-polling path (ApplyDiff), so it isn't exercised here.
    public class PersonaSyncIntentTests
    {
        [Fact]
        public void ParseIntent_Benign_CaseInsensitive()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Benign, PersonaSync.ParseIntent("benign"));
            Assert.Equal(DispositionSystem.NpcIntent.Benign, PersonaSync.ParseIntent("BENIGN"));
        }

        [Fact]
        public void ParseIntent_Malign_CaseInsensitive()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Malign, PersonaSync.ParseIntent("malign"));
            Assert.Equal(DispositionSystem.NpcIntent.Malign, PersonaSync.ParseIntent("Malign"));
        }

        [Fact]
        public void ParseIntent_Neutral_Explicit()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.ParseIntent("neutral"));
        }

        [Fact]
        public void ParseIntent_NullOrUnrecognized_DefaultsToNeutral()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.ParseIntent(null));
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.ParseIntent(""));
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.ParseIntent("a typo'd value"));
        }

        [Fact]
        public void GetIntent_UnknownPersonaId_DefaultsToNeutral()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.GetIntent("no-such-persona-id"));
        }

        [Fact]
        public void GetIntent_NullPersonaId_DefaultsToNeutral()
        {
            Assert.Equal(DispositionSystem.NpcIntent.Neutral, PersonaSync.GetIntent(null));
        }
    }
}
