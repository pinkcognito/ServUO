namespace Server.Tests
{
    using Server.Custom.AIAgents;

    public class ReactionResolveTests
    {
        // -- ReactionOutcome (2d6 + loyaltyMod) bucket edges --

        [Fact]
        public void ReactionOutcome_AtEveryEdge()
        {
            Assert.Equal(Outcome.Refuse, ReactionResolve.ReactionOutcome(2, 0));
            Assert.Equal(Outcome.Hesitate, ReactionResolve.ReactionOutcome(5, 0));
            Assert.Equal(Outcome.ComplyGrudging, ReactionResolve.ReactionOutcome(8, 0));
            Assert.Equal(Outcome.Comply, ReactionResolve.ReactionOutcome(11, 0));
            Assert.Equal(Outcome.ComplyEager, ReactionResolve.ReactionOutcome(12, 0));
        }

        [Fact]
        public void ReactionOutcome_LoyaltyModShiftsBucketUpward()
        {
            // A flat roll of 5 (Hesitate unmodified) becomes ComplyGrudging
            // once a +3 loyalty mod is applied - issue acceptance: "vary
            // Loyalty/bond -> reaction bucket shifts as expected."
            Assert.Equal(Outcome.Hesitate, ReactionResolve.ReactionOutcome(5, 0));
            Assert.Equal(Outcome.ComplyGrudging, ReactionResolve.ReactionOutcome(5, 3));
        }

        [Fact]
        public void ReactionOutcome_LoyaltyModShiftsBucketDownward()
        {
            Assert.Equal(Outcome.ComplyGrudging, ReactionResolve.ReactionOutcome(8, 0));
            Assert.Equal(Outcome.Hesitate, ReactionResolve.ReactionOutcome(8, -3));
        }

        // -- MoraleOutcome (2d6 vs. effectiveMorale) --

        [Fact]
        public void MoraleOutcome_RollAboveMorale_Flees()
        {
            Assert.Equal(Outcome.Flee, ReactionResolve.MoraleOutcome(9, 8));
        }

        [Fact]
        public void MoraleOutcome_RollEqualsMorale_ComplyGrudging()
        {
            Assert.Equal(Outcome.ComplyGrudging, ReactionResolve.MoraleOutcome(9, 9));
        }

        [Fact]
        public void MoraleOutcome_RollWellUnderMorale_ComplyEager()
        {
            Assert.Equal(Outcome.ComplyEager, ReactionResolve.MoraleOutcome(2, 12));
        }

        [Fact]
        public void MoraleOutcome_RollModeratelyUnderMorale_Comply()
        {
            Assert.Equal(Outcome.Comply, ReactionResolve.MoraleOutcome(7, 9));
        }

        // -- TraitOutcome (d20 under cruelTrait) --

        [Fact]
        public void TraitOutcome_RollAboveTrait_Refuses()
        {
            Assert.Equal(Outcome.Refuse, ReactionResolve.TraitOutcome(15, 10));
        }

        [Fact]
        public void TraitOutcome_RollWellUnderTrait_ComplyEager()
        {
            Assert.Equal(Outcome.ComplyEager, ReactionResolve.TraitOutcome(1, 20));
        }

        [Fact]
        public void TraitOutcome_RollJustUnderTrait_ComplyGrudging()
        {
            Assert.Equal(Outcome.ComplyGrudging, ReactionResolve.TraitOutcome(19, 20));
        }

        [Fact]
        public void TraitOutcome_ZeroTrait_AlwaysRefusesRegardlessOfRoll()
        {
            for (var roll = 1; roll <= 20; roll++)
            {
                Assert.Equal(Outcome.Refuse, ReactionResolve.TraitOutcome(roll, 0));
            }
        }

        [Fact]
        public void TraitOutcome_MaxTrait_NeverRefusesRegardlessOfRoll()
        {
            for (var roll = 1; roll <= 20; roll++)
            {
                Assert.NotEqual(Outcome.Refuse, ReactionResolve.TraitOutcome(roll, 20));
            }
        }

        // -- LoyaltyModifier --

        [Fact]
        public void LoyaltyModifier_MidpointLoyaltyAndBond_IsZero()
        {
            Assert.Equal(0, ReactionResolve.LoyaltyModifier(50, 500));
        }

        [Fact]
        public void LoyaltyModifier_MaxLoyaltyAndBond_IsPositive()
        {
            Assert.True(ReactionResolve.LoyaltyModifier(100, CompanionBond.MaxScore) > 0);
        }

        [Fact]
        public void LoyaltyModifier_MinLoyaltyAndBond_IsNegative()
        {
            Assert.True(ReactionResolve.LoyaltyModifier(0, CompanionBond.MinScore) < 0);
        }

        // -- ThreatPenalty --

        [Fact]
        public void ThreatPenalty_SelfOutmatchesTarget_IsZero()
        {
            Assert.Equal(0, ReactionResolve.ThreatPenalty(selfHpPercent: 1.0, selfBestCombatSkill: 100, targetPower: 50));
        }

        [Fact]
        public void ThreatPenalty_LowHpAgainstToughTarget_IsPositiveAndGrows()
        {
            var lowHpPenalty = ReactionResolve.ThreatPenalty(selfHpPercent: 0.1, selfBestCombatSkill: 0, targetPower: 100);
            var fullHpPenalty = ReactionResolve.ThreatPenalty(selfHpPercent: 1.0, selfBestCombatSkill: 100, targetPower: 100);

            Assert.True(lowHpPenalty > 0);
            Assert.True(lowHpPenalty > fullHpPenalty);
        }

        // -- CruelTrait (the morally-loaded check's alignment input) --

        [Fact]
        public void CruelTrait_BenignIntentWithVeryGoodKarma_PinsToZero()
        {
            var trait = ReactionResolve.CruelTrait(DispositionSystem.NpcIntent.Benign, DispositionSystem.KarmaVeryGoodThreshold);

            Assert.Equal(0, trait);
        }

        [Fact]
        public void CruelTrait_MalignIntentWithVeryEvilKarma_PinsToMax()
        {
            var trait = ReactionResolve.CruelTrait(DispositionSystem.NpcIntent.Malign, DispositionSystem.KarmaVeryEvilThreshold);

            Assert.Equal(20, trait);
        }

        [Fact]
        public void CruelTrait_NeutralIntentNeutralKarma_IsMidpoint()
        {
            Assert.Equal(10, ReactionResolve.CruelTrait(DispositionSystem.NpcIntent.Neutral, 0));
        }

        // -- IsDarkFlavor --

        [Fact]
        public void IsDarkFlavor_MorallyLoadedCompliance_IsDark()
        {
            Assert.True(ReactionResolve.IsDarkFlavor(OrderKind.MorallyLoaded, Outcome.ComplyEager));
            Assert.True(ReactionResolve.IsDarkFlavor(OrderKind.MorallyLoaded, Outcome.Comply));
            Assert.True(ReactionResolve.IsDarkFlavor(OrderKind.MorallyLoaded, Outcome.ComplyGrudging));
        }

        [Fact]
        public void IsDarkFlavor_MorallyLoadedRefuse_IsClean_BenignRefuseCase()
        {
            Assert.False(ReactionResolve.IsDarkFlavor(OrderKind.MorallyLoaded, Outcome.Refuse));
        }

        [Fact]
        public void IsDarkFlavor_NonMorallyLoadedOrders_AreNeverDark()
        {
            foreach (Outcome outcome in System.Enum.GetValues(typeof(Outcome)))
            {
                Assert.False(ReactionResolve.IsDarkFlavor(OrderKind.Routine, outcome));
                Assert.False(ReactionResolve.IsDarkFlavor(OrderKind.RiskyCombat, outcome));
            }
        }

        // -- Resolve: full acceptance scenarios --

        [Fact]
        public void Resolve_AttackToughFoeAtLowHp_Flees()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 0,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = ReactionResolve.ThreatPenalty(selfHpPercent: 0.1, selfBestCombatSkill: 0, targetPower: 100),
                CruelTrait = 10,
                NearDeath = false,
            };

            var result = ReactionResolve.Resolve(OrderKind.RiskyCombat, inputs, new FixedDiceRoller(7));

            Assert.Equal(Outcome.Flee, result.Outcome);
            Assert.Equal(CheckKind.Morale, result.CheckKind);
        }

        [Fact]
        public void Resolve_AttackToughFoeAtFullHpHighSkill_Complies()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 0,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = ReactionResolve.ThreatPenalty(selfHpPercent: 1.0, selfBestCombatSkill: 100, targetPower: 100),
                CruelTrait = 10,
                NearDeath = false,
            };

            var result = ReactionResolve.Resolve(OrderKind.RiskyCombat, inputs, new FixedDiceRoller(7));

            Assert.NotEqual(Outcome.Flee, result.Outcome);
            Assert.NotEqual(Outcome.Refuse, result.Outcome);
        }

        [Fact]
        public void Resolve_HarmInnocent_BenignKarmaNpc_Refuses_NoModelConsulted()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 4,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = 0,
                CruelTrait = ReactionResolve.CruelTrait(DispositionSystem.NpcIntent.Benign, DispositionSystem.KarmaVeryGoodThreshold),
                NearDeath = false,
            };

            // Seeded, not hand-picked - any roll must refuse (CruelTrait is
            // pinned to 0), demonstrating the outcome doesn't depend on
            // getting a "lucky" die.
            var result = ReactionResolve.Resolve(OrderKind.MorallyLoaded, inputs, new SeededDiceRoller(1234));

            Assert.Equal(Outcome.Refuse, result.Outcome);
            Assert.Equal(CheckKind.Trait, result.CheckKind);
        }

        [Fact]
        public void Resolve_HarmInnocent_MalignKarmaNpc_Complies_NoModelConsulted()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = -4,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = 0,
                CruelTrait = ReactionResolve.CruelTrait(DispositionSystem.NpcIntent.Malign, DispositionSystem.KarmaVeryEvilThreshold),
                NearDeath = false,
            };

            var result = ReactionResolve.Resolve(OrderKind.MorallyLoaded, inputs, new SeededDiceRoller(5678));

            Assert.True(
                result.Outcome == Outcome.ComplyEager || result.Outcome == Outcome.ComplyGrudging || result.Outcome == Outcome.Comply);
            Assert.Equal(CheckKind.Trait, result.CheckKind);
        }

        [Fact]
        public void Resolve_NearDeath_MoraleFailure_OverridesRoutineOrder()
        {
            // "Any order, near death: morale first, flee overrides" - even
            // a Routine (follow) order must yield Flee, never reach the
            // reaction-roll check, when morale fails.
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 4, // would otherwise comply easily on a reaction roll
                BaseMorale = 2, // minimum possible - fails almost any roll
                ThreatPenalty = 0,
                CruelTrait = 10,
                NearDeath = true,
            };

            var result = ReactionResolve.Resolve(OrderKind.Routine, inputs, new FixedDiceRoller(7));

            Assert.Equal(Outcome.Flee, result.Outcome);
            Assert.Equal(CheckKind.Morale, result.CheckKind);
        }

        [Fact]
        public void Resolve_NearDeath_MoraleHolds_FallsThroughToRoutineCheck()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 4,
                BaseMorale = 12, // maximum - holds against nearly any roll
                ThreatPenalty = 0,
                CruelTrait = 10,
                NearDeath = true,
            };

            var result = ReactionResolve.Resolve(OrderKind.Routine, inputs, new FixedDiceRoller(7));

            Assert.NotEqual(Outcome.Flee, result.Outcome);
            Assert.Equal(CheckKind.Reaction, result.CheckKind);
        }

        [Fact]
        public void Resolve_RiskyCombat_UsesMoraleCheck_EvenWithoutNearDeath()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 0,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = 0,
                CruelTrait = 10,
                NearDeath = false,
            };

            var result = ReactionResolve.Resolve(OrderKind.RiskyCombat, inputs, new FixedDiceRoller(7));

            Assert.Equal(CheckKind.Morale, result.CheckKind);
        }

        [Fact]
        public void Resolve_IsReproducible_FromASeededRoller()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 1,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = 2,
                CruelTrait = 10,
                NearDeath = false,
            };

            var first = ReactionResolve.Resolve(OrderKind.Routine, inputs, new SeededDiceRoller(42));
            var second = ReactionResolve.Resolve(OrderKind.Routine, inputs, new SeededDiceRoller(42));

            Assert.Equal(first.Outcome, second.Outcome);
            Assert.Equal(first.Roll, second.Roll);
        }

        [Fact]
        public void Resolve_LogsTheRollAndModifier()
        {
            var inputs = new ReactionInputs
            {
                LoyaltyMod = 2,
                BaseMorale = ReactionResolve.DefaultBaseMorale,
                ThreatPenalty = 0,
                CruelTrait = 10,
                NearDeath = false,
            };

            var result = ReactionResolve.Resolve(OrderKind.Routine, inputs, new FixedDiceRoller(6));

            Assert.Equal(6, result.Roll);
            Assert.Equal(2, result.Modifier);
            Assert.False(string.IsNullOrEmpty(result.Reason));
        }
    }

    public class ReactionBarkTableTests
    {
        [Fact]
        public void Pick_ReturnsAuthoredLine_ForDarkOutcomes()
        {
            Assert.False(string.IsNullOrEmpty(ReactionBarkTable.Pick(Outcome.ComplyEager, 0)));
            Assert.False(string.IsNullOrEmpty(ReactionBarkTable.Pick(Outcome.Comply, 0)));
            Assert.False(string.IsNullOrEmpty(ReactionBarkTable.Pick(Outcome.ComplyGrudging, 0)));
        }

        [Fact]
        public void Pick_ReturnsNull_ForNonComplyOutcomes()
        {
            Assert.Null(ReactionBarkTable.Pick(Outcome.Refuse, 0));
            Assert.Null(ReactionBarkTable.Pick(Outcome.Hesitate, 0));
            Assert.Null(ReactionBarkTable.Pick(Outcome.Flee, 0));
        }

        [Fact]
        public void Pick_IsDeterministic_ForTheSameIndex()
        {
            Assert.Equal(ReactionBarkTable.Pick(Outcome.ComplyEager, 3), ReactionBarkTable.Pick(Outcome.ComplyEager, 3));
        }
    }
}
