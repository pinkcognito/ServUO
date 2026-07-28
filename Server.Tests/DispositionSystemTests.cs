using Server.Custom.AIAgents;
using Server.Services.Virtues;

namespace Server.Tests
{
    public class DispositionSystemTests
    {
        // -- IntentSign --

        [Fact]
        public void IntentSign_Benign_IsPositive()
        {
            Assert.Equal(1, DispositionSystem.IntentSign(DispositionSystem.NpcIntent.Benign));
        }

        [Fact]
        public void IntentSign_Malign_IsNegative()
        {
            Assert.Equal(-1, DispositionSystem.IntentSign(DispositionSystem.NpcIntent.Malign));
        }

        [Fact]
        public void IntentSign_Neutral_IsZero()
        {
            Assert.Equal(0, DispositionSystem.IntentSign(DispositionSystem.NpcIntent.Neutral));
        }

        // -- IsHardHostile (the notoriety gate) --

        [Fact]
        public void IsHardHostile_MurdererForcesHostile_ForBenignNpc()
        {
            Assert.True(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Benign, Notoriety.Murderer));
        }

        [Fact]
        public void IsHardHostile_MurdererForcesHostile_ForNeutralNpc()
        {
            Assert.True(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Neutral, Notoriety.Murderer));
        }

        [Fact]
        public void IsHardHostile_MurdererDoesNotGate_ForMalignNpc()
        {
            // A brigand respects a murderer - the hard gate is skipped, not
            // forced Hostile, leaving the soft score to decide.
            Assert.False(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Malign, Notoriety.Murderer));
        }

        [Fact]
        public void IsHardHostile_NonMurdererNotoriety_NeverGates()
        {
            Assert.False(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Benign, Notoriety.Innocent));
            Assert.False(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Benign, Notoriety.CanBeAttacked));
            Assert.False(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Benign, Notoriety.Criminal));
            Assert.False(DispositionSystem.IsHardHostile(DispositionSystem.NpcIntent.Benign, Notoriety.Enemy));
        }

        // -- Term functions --

        [Fact]
        public void KarmaTerm_VeryGoodAtThreshold_IsTwo()
        {
            Assert.Equal(2, DispositionSystem.KarmaTerm(DispositionSystem.KarmaVeryGoodThreshold));
        }

        [Fact]
        public void KarmaTerm_GoodAtThreshold_IsOne()
        {
            Assert.Equal(1, DispositionSystem.KarmaTerm(DispositionSystem.KarmaGoodThreshold));
        }

        [Fact]
        public void KarmaTerm_NeutralBand_IsZero()
        {
            Assert.Equal(0, DispositionSystem.KarmaTerm(0));
        }

        [Fact]
        public void KarmaTerm_EvilAtThreshold_IsNegativeOne()
        {
            Assert.Equal(-1, DispositionSystem.KarmaTerm(DispositionSystem.KarmaEvilThreshold));
        }

        [Fact]
        public void KarmaTerm_VeryEvilAtThreshold_IsNegativeTwo()
        {
            Assert.Equal(-2, DispositionSystem.KarmaTerm(DispositionSystem.KarmaVeryEvilThreshold));
        }

        [Fact]
        public void FameTerm_BelowNotable_IsZero()
        {
            Assert.Equal(0, DispositionSystem.FameTerm(DispositionSystem.FameNotableThreshold - 1));
        }

        [Fact]
        public void FameTerm_AtNotableThreshold_IsOne()
        {
            Assert.Equal(1, DispositionSystem.FameTerm(DispositionSystem.FameNotableThreshold));
        }

        [Fact]
        public void FameTerm_AtRenownedThreshold_IsTwo()
        {
            Assert.Equal(2, DispositionSystem.FameTerm(DispositionSystem.FameRenownedThreshold));
        }

        [Fact]
        public void HonorTerm_MatchesVirtueLevelOrdinal()
        {
            Assert.Equal(0, DispositionSystem.HonorTerm(VirtueLevel.None));
            Assert.Equal(1, DispositionSystem.HonorTerm(VirtueLevel.Seeker));
            Assert.Equal(2, DispositionSystem.HonorTerm(VirtueLevel.Follower));
            Assert.Equal(3, DispositionSystem.HonorTerm(VirtueLevel.Knight));
        }

        // -- Bucket / Clamp --

        [Fact]
        public void Bucket_AtEveryEdge()
        {
            Assert.Equal(DispositionSystem.Disposition.Hostile, DispositionSystem.Bucket(DispositionSystem.HostileMaxScore));
            Assert.Equal(DispositionSystem.Disposition.Wary, DispositionSystem.Bucket(DispositionSystem.WaryMaxScore));
            Assert.Equal(DispositionSystem.Disposition.Neutral, DispositionSystem.Bucket(DispositionSystem.NeutralMaxScore));
            Assert.Equal(DispositionSystem.Disposition.Warm, DispositionSystem.Bucket(DispositionSystem.WarmMaxScore));
            Assert.Equal(DispositionSystem.Disposition.Reverent, DispositionSystem.Bucket(DispositionSystem.WarmMaxScore + 1));
        }

        [Fact]
        public void Clamp_BoundsAboveMax()
        {
            Assert.Equal(DispositionSystem.MaxScore, DispositionSystem.Clamp(DispositionSystem.MaxScore + 100));
        }

        [Fact]
        public void Clamp_BoundsBelowMin()
        {
            Assert.Equal(DispositionSystem.MinScore, DispositionSystem.Clamp(DispositionSystem.MinScore - 100));
        }

        // -- Compute: the acceptance scenarios --

        [Fact]
        public void Compute_FamousHonorablePaladin_IsReverent_ToBenignNpc()
        {
            var result = DispositionSystem.Compute(
                DispositionSystem.NpcIntent.Benign,
                Notoriety.Innocent,
                fame: DispositionSystem.FameRenownedThreshold,
                karma: DispositionSystem.KarmaVeryGoodThreshold,
                honor: VirtueLevel.Knight);

            Assert.Equal(DispositionSystem.Disposition.Reverent, result);
        }

        [Fact]
        public void Compute_Murderer_IsHostile_ToBenignNpc_RegardlessOfFame()
        {
            // Issue #61 acceptance: "Notoriety Murderer forces Hostile from
            // a benign NPC no matter how high the fame."
            var result = DispositionSystem.Compute(
                DispositionSystem.NpcIntent.Benign,
                Notoriety.Murderer,
                fame: DispositionSystem.FameRenownedThreshold,
                karma: DispositionSystem.KarmaVeryGoodThreshold, // even with implausibly good karma
                honor: VirtueLevel.Knight);

            Assert.Equal(DispositionSystem.Disposition.Hostile, result);
        }

        [Fact]
        public void Compute_SamePlayer_OppositeIntents_YieldOppositeBuckets()
        {
            var benign = DispositionSystem.Compute(
                DispositionSystem.NpcIntent.Benign,
                Notoriety.Innocent,
                fame: DispositionSystem.FameNotableThreshold,
                karma: DispositionSystem.KarmaVeryGoodThreshold,
                honor: VirtueLevel.Knight);

            var malign = DispositionSystem.Compute(
                DispositionSystem.NpcIntent.Malign,
                Notoriety.Innocent,
                fame: DispositionSystem.FameNotableThreshold,
                karma: DispositionSystem.KarmaVeryGoodThreshold,
                honor: VirtueLevel.Knight);

            Assert.NotEqual(benign, malign);
            Assert.Equal(DispositionSystem.Disposition.Reverent, benign);
            Assert.Equal(DispositionSystem.Disposition.Hostile, malign);
        }

        [Fact]
        public void Compute_ReceptionOrderFlips_BetweenBenignAndMalignNpc()
        {
            // A paladin and a murderer walking past a benign blacksmith get
            // one ordering; the same two past a malign brigand get the
            // opposite ordering (issue #61 acceptance).
            const int paladinFame = DispositionSystem.FameRenownedThreshold;
            const int paladinKarma = DispositionSystem.KarmaVeryGoodThreshold;
            const VirtueLevel paladinHonor = VirtueLevel.Knight;

            const int murdererFame = 0;
            const int murdererKarma = DispositionSystem.KarmaVeryEvilThreshold;
            const VirtueLevel murdererHonor = VirtueLevel.None;

            var benignPaladin = DispositionSystem.Compute(DispositionSystem.NpcIntent.Benign, Notoriety.Innocent, paladinFame, paladinKarma, paladinHonor);
            var benignMurderer = DispositionSystem.Compute(DispositionSystem.NpcIntent.Benign, Notoriety.Murderer, murdererFame, murdererKarma, murdererHonor);

            var malignPaladin = DispositionSystem.Compute(DispositionSystem.NpcIntent.Malign, Notoriety.Innocent, paladinFame, paladinKarma, paladinHonor);
            var malignMurderer = DispositionSystem.Compute(DispositionSystem.NpcIntent.Malign, Notoriety.Murderer, murdererFame, murdererKarma, murdererHonor);

            // Benign NPC: paladin ranks above murderer.
            Assert.True(benignPaladin > benignMurderer);

            // Malign NPC: murderer ranks above paladin - the ordering flips.
            Assert.True(malignMurderer > malignPaladin);
        }

        [Fact]
        public void Compute_HonorLevel_MovesBucketUpward_ForBenignNpc()
        {
            var noHonor = DispositionSystem.Compute(DispositionSystem.NpcIntent.Benign, Notoriety.Innocent, fame: 0, karma: 0, honor: VirtueLevel.None);
            var knightHonor = DispositionSystem.Compute(DispositionSystem.NpcIntent.Benign, Notoriety.Innocent, fame: 0, karma: 0, honor: VirtueLevel.Knight);

            Assert.True(knightHonor > noHonor);
        }

        [Fact]
        public void Compute_HonorLevel_MovesBucketDownward_ForMalignNpc()
        {
            // Honor inverts for a malign NPC - a more honorable player reads
            // as LESS favorably disposed, not more.
            var noHonor = DispositionSystem.Compute(DispositionSystem.NpcIntent.Malign, Notoriety.Innocent, fame: 0, karma: 0, honor: VirtueLevel.None);
            var knightHonor = DispositionSystem.Compute(DispositionSystem.NpcIntent.Malign, Notoriety.Innocent, fame: 0, karma: 0, honor: VirtueLevel.Knight);

            Assert.True(knightHonor < noHonor);
        }
    }
}
