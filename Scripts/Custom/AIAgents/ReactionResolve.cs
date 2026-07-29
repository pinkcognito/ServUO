using System;

namespace Server.Custom.AIAgents
{
    // Issue #63 (epic #35): the numeric core of "Reaction & Resolve" - whether
    // a companion obeys an order, decided from grounded game state + dice
    // BEFORE any model call, never by asking an LLM to weigh it. Two prior
    // tests (docs/model test results/) showed the model can't be trusted with
    // this: asked to weigh bond*risk it collapsed to bond-only, and a malign
    // NPC ordered to harm an Innocent refused on its own alignment training
    // (willingness-refusal) even in character. A follow-up
    // (FINDINGS-narrator-compliance.md) showed the same NPC complies 6/6 once
    // the outcome is pre-resolved and it's demoted to narrator - so the
    // decision has to happen here, in plain arithmetic, with the model never
    // given the chance to think it through.
    //
    // Pure and Mobile/World-free by design, like ActionValidator (#59),
    // CombatStanceSelector (#68), and DispositionSystem (#61) - callers
    // (BotAI) do the live Mobile/Notoriety/Karma reads and hand plain values
    // in here, so the three checks are directly unit-testable without a live
    // game world, and reproducible from a seeded IDiceRoller.
    public enum OrderKind
    {
        // Follow / come - low stakes, decided by the OD&D reaction roll.
        Routine,

        // Attack / defend a target that isn't a hard morally-loaded case -
        // decided by the B/X morale check (loyalty vs. how outmatched the
        // companion is).
        RiskyCombat,

        // An order whose target reads as Notoriety.Innocent - decided by the
        // Pendragon-style trait check against the companion's own
        // Merciful/Cruel alignment. The model is never consulted either way
        // (see the class doc comment).
        MorallyLoaded,
    }

    // Which of the three source mechanics actually fired for a given
    // Resolve() call - kept on the result so BotAI can log "why" (issue
    // acceptance: "every roll + modifier is logged").
    public enum CheckKind
    {
        Reaction,
        Morale,
        Trait,
    }

    public enum Outcome
    {
        ComplyEager,
        Comply,
        ComplyGrudging,
        Hesitate,
        Refuse,
        Flee,
    }

    // Plain-value inputs to Resolve - every field is something BotAI reads
    // off live game state (Loyalty, AffinityScore, Karma, Intent, hp%,
    // skill, target toughness) and reduces to a number before calling in
    // here; nothing in this struct is invented.
    public struct ReactionInputs
    {
        public int LoyaltyMod;
        public int BaseMorale;
        public int ThreatPenalty;
        public int CruelTrait;
        public bool NearDeath;
    }

    public struct ReactionResult
    {
        public Outcome Outcome;
        public OrderKind OrderKind;
        public CheckKind CheckKind;
        public int Roll;
        public int Modifier;
        public string Reason;

        public override string ToString()
        {
            return string.Format(
                "{0} via {1} (order={2}, roll={3}, mod={4}): {5}",
                Outcome, CheckKind, OrderKind, Roll, Modifier, Reason);
        }
    }

    // Rolls dice. UtilityDiceRoller is the production implementation
    // (Utility.Dice, ServUO's shared RNG); SeededDiceRoller lets tests (and
    // the acceptance criterion "reproducible from a seeded RNG") get a fixed
    // sequence without touching ServUO's global random state.
    public interface IDiceRoller
    {
        int Roll(int numDice, int sides);
    }

    public sealed class UtilityDiceRoller : IDiceRoller
    {
        public int Roll(int numDice, int sides)
        {
            return Utility.Dice(numDice, sides, 0);
        }
    }

    public sealed class SeededDiceRoller : IDiceRoller
    {
        private readonly Random _random;

        public SeededDiceRoller(int seed)
        {
            _random = new Random(seed);
        }

        public int Roll(int numDice, int sides)
        {
            var total = 0;
            for (var i = 0; i < numDice; i++)
            {
                total += _random.Next(1, sides + 1);
            }

            return total;
        }
    }

    // A roller that always returns the same value - for tests that want an
    // exact, named roll rather than a seeded sequence.
    public sealed class FixedDiceRoller : IDiceRoller
    {
        private readonly int _value;

        public FixedDiceRoller(int value)
        {
            _value = value;
        }

        public int Roll(int numDice, int sides)
        {
            return _value;
        }
    }

    public static class ReactionResolve
    {
        // B/X-style default: a recruited companion starts reasonably brave
        // (it was vetted through CompanionRecruitmentQuest, #65) rather than
        // at the tabletop default of "average NPC."
        public const int DefaultBaseMorale = 9;

        // -- Reaction roll bucket edges (2d6 + loyaltyMod). Chosen so the
        // 6-8 "uncertain middle" band covers exactly the classic OD&D 2d6
        // probability mass (~44%), matching the issue's own framing. --
        private const int ReactionRefuseMax = 2;
        private const int ReactionHesitateMax = 5;
        private const int ReactionGrudgingMax = 8;
        private const int ReactionComplyMax = 11;

        public static Outcome ReactionOutcome(int roll2d6, int loyaltyMod)
        {
            var total = roll2d6 + loyaltyMod;

            if (total <= ReactionRefuseMax)
            {
                return Outcome.Refuse;
            }

            if (total <= ReactionHesitateMax)
            {
                return Outcome.Hesitate;
            }

            if (total <= ReactionGrudgingMax)
            {
                return Outcome.ComplyGrudging;
            }

            if (total <= ReactionComplyMax)
            {
                return Outcome.Comply;
            }

            return Outcome.ComplyEager;
        }

        // -- Morale check (2d6 vs. effectiveMorale). B/X: roll over morale =
        // fail. Graded here (ComplyEager/Comply/ComplyGrudging on a hold,
        // Flee on a fail) rather than the tabletop's flat pass/fail, so the
        // same check can flavor a clean narration line. --
        private const int MoraleGrudgingMargin = 0;
        private const int MoraleComplyMaxMargin = 3;

        public static Outcome MoraleOutcome(int roll2d6, int effectiveMorale)
        {
            var margin = effectiveMorale - roll2d6;

            if (margin < 0)
            {
                return Outcome.Flee;
            }

            if (margin == MoraleGrudgingMargin)
            {
                return Outcome.ComplyGrudging;
            }

            if (margin <= MoraleComplyMaxMargin)
            {
                return Outcome.Comply;
            }

            return Outcome.ComplyEager;
        }

        // -- Trait check (d20 under cruelTrait, Pendragon-style). Binary by
        // design (no Hesitate band) - an opposed-trait check at either
        // extreme (cruelTrait 0 or 20, see CruelTrait below) must resolve
        // the same way on every roll, which a fixed-width "near the
        // boundary" band would break for a min/max roll. Comply is graded
        // by how far under the trait the roll landed. --
        public static Outcome TraitOutcome(int rollD20, int cruelTrait)
        {
            if (rollD20 > cruelTrait)
            {
                return Outcome.Refuse;
            }

            var margin = cruelTrait - rollD20;
            return margin >= cruelTrait / 2 ? Outcome.ComplyEager : Outcome.ComplyGrudging;
        }

        // Loyalty (BaseCreature, 0-100) and bond (CompanionBond, 0-1000)
        // each contribute roughly -2..+2 around their midpoint, combined
        // into one -4..+4 reaction-roll modifier. LoyaltyScaleMax mirrors
        // BaseCreature.MaxLoyalty (not referenced directly - this class
        // stays Mobile-free); BondMidpoint/BondScaleMax reuse CompanionBond's
        // own constants rather than re-guessing its range.
        private const int LoyaltyScaleMax = 100;
        private const int LoyaltyDivisor = 25;
        private const int BondDivisor = 250;
        private const int MaxLoyaltyModifier = 4;

        // Clamped so the "-4..+4" contract in the comment above holds even
        // if a caller ever hands in an out-of-band Loyalty/bond value (e.g.
        // content that pokes BaseCreature.Loyalty directly) rather than
        // relying on both inputs always staying in their documented range.
        public static int LoyaltyModifier(int loyalty, int bondScore)
        {
            var loyaltyPart = (loyalty - (LoyaltyScaleMax / 2)) / LoyaltyDivisor;
            var bondMidpoint = (CompanionBond.MinScore + CompanionBond.MaxScore) / 2;
            var bondPart = (bondScore - bondMidpoint) / BondDivisor;

            return Math.Min(Math.Max(loyaltyPart + bondPart, -MaxLoyaltyModifier), MaxLoyaltyModifier);
        }

        // How outmatched the companion reads against a target: self
        // capability is hp% (weighted lower - a skilled bot at half health
        // is still dangerous) plus its best trained combat skill, compared
        // against the target's toughness (caller-supplied, e.g. HitsMax).
        // Every 10 points outmatched costs 1 point of effective morale.
        private const double HpWeight = 60.0;
        private const double SkillWeight = 0.5;
        private const double PenaltyPerPoint = 10.0;

        public static int ThreatPenalty(double selfHpPercent, double selfBestCombatSkill, double targetPower)
        {
            var selfCapability = (selfHpPercent * HpWeight) + (selfBestCombatSkill * SkillWeight);
            var diff = targetPower - selfCapability;

            return diff <= 0 ? 0 : (int)(diff / PenaltyPerPoint);
        }

        // The companion's own Merciful<->Cruel trait (Pendragon: an opposed
        // pair summing to 20), derived from its own Intent (#61) and Karma -
        // NOT from how it perceives a player (DispositionSystem.Compute is
        // that, a different question). Reuses DispositionSystem.IntentSign
        // and KarmaTerm per the issue's explicit instruction rather than
        // re-deriving the same alignment math twice.
        //
        // Intent sets the baseline (a Malign-flagged NPC starts crueler than
        // a Benign one); Karma is the gradient within it, same soft-gradient
        // idea DispositionSystem uses for notoriety/karma/honor. At the
        // extremes (Benign + very good karma, or Malign + very evil karma)
        // this pins to 0 or 20 - a fully committed alignment always resolves
        // the same way regardless of the die roll, matching the issue's own
        // acceptance ("benign-Karma NPC's trait roll -> Refuse; malign-Karma
        // NPC's -> Comply", stated as a certainty, not a probability).
        private const int TraitMidpoint = 10;
        private const int TraitMax = 20;
        private const int IntentTraitSwing = 4;
        private const int KarmaTraitSwing = 3;

        public static int CruelTrait(DispositionSystem.NpcIntent intent, int karma)
        {
            var baseline = TraitMidpoint - (DispositionSystem.IntentSign(intent) * IntentTraitSwing);
            var karmaAdjusted = baseline - (DispositionSystem.KarmaTerm(karma) * KarmaTraitSwing);

            return Math.Min(Math.Max(karmaAdjusted, 0), TraitMax);
        }

        // Dark/malign outcomes (a companion agreeing to a morally-loaded
        // order) must never be voiced by the model - Bedrock's own content
        // filter blocked ~1 in 7 malign generations non-deterministically
        // (FINDINGS-narrator-compliance.md finding 2). BotAI routes these to
        // ReactionBarkTable (authored, filter-proof) instead; everything
        // else is left for the model to narrate later (#40), never blocked
        // from executing either way.
        public static bool IsDarkFlavor(OrderKind kind, Outcome outcome)
        {
            return kind == OrderKind.MorallyLoaded &&
                   (outcome == Outcome.ComplyEager || outcome == Outcome.Comply || outcome == Outcome.ComplyGrudging);
        }

        // The single entry point: picks the check by order kind, per the
        // issue's table, with one cross-cutting rule layered on top -
        // "any order, near death -> morale first; flee overrides" - checked
        // before, not instead of, the kind-specific check. RiskyCombat's own
        // primary check already *is* morale, so near-death there costs no
        // extra roll; Routine/MorallyLoaded get an extra morale roll first
        // only when NearDeath is set, and only fall through to their own
        // check if that roll didn't produce Flee.
        public static ReactionResult Resolve(OrderKind kind, ReactionInputs inputs, IDiceRoller dice)
        {
            if (dice == null)
            {
                throw new ArgumentNullException(nameof(dice));
            }

            // Carries the near-death morale roll's own log line forward when
            // it holds and execution falls through to the kind-specific
            // check below - otherwise that roll (the first thing that
            // actually fired) would silently vanish from the logged Reason,
            // even though it's what let the order proceed at all.
            string nearDeathPreamble = null;

            if (inputs.NearDeath || kind == OrderKind.RiskyCombat)
            {
                var effectiveMorale = inputs.BaseMorale + inputs.LoyaltyMod - inputs.ThreatPenalty;
                var moraleRoll = dice.Roll(2, 6);
                var moraleOutcome = MoraleOutcome(moraleRoll, effectiveMorale);

                if (moraleOutcome == Outcome.Flee || kind == OrderKind.RiskyCombat)
                {
                    return new ReactionResult
                    {
                        Outcome = moraleOutcome,
                        OrderKind = kind,
                        CheckKind = CheckKind.Morale,
                        Roll = moraleRoll,
                        Modifier = effectiveMorale,
                        Reason = string.Format("morale {0} vs roll {1} (effective morale {2})", moraleOutcome, moraleRoll, effectiveMorale),
                    };
                }

                // Near death but not a combat order, and morale held - fall
                // through to the order's own check below, carrying this
                // roll's line forward instead of dropping it.
                nearDeathPreamble = string.Format(
                    "near-death morale held (roll {0} vs effective morale {1}); ", moraleRoll, effectiveMorale);
            }

            switch (kind)
            {
                case OrderKind.Routine:
                {
                    var roll = dice.Roll(2, 6);
                    var outcome = ReactionOutcome(roll, inputs.LoyaltyMod);
                    return new ReactionResult
                    {
                        Outcome = outcome,
                        OrderKind = kind,
                        CheckKind = CheckKind.Reaction,
                        Roll = roll,
                        Modifier = inputs.LoyaltyMod,
                        Reason = nearDeathPreamble + string.Format(
                            "reaction {0} vs roll {1} (loyalty mod {2})", outcome, roll, inputs.LoyaltyMod),
                    };
                }

                case OrderKind.MorallyLoaded:
                {
                    var roll = dice.Roll(1, 20);
                    var outcome = TraitOutcome(roll, inputs.CruelTrait);
                    return new ReactionResult
                    {
                        Outcome = outcome,
                        OrderKind = kind,
                        CheckKind = CheckKind.Trait,
                        Roll = roll,
                        Modifier = inputs.CruelTrait,
                        Reason = nearDeathPreamble + string.Format(
                            "trait {0} vs roll {1} (cruel trait {2})", outcome, roll, inputs.CruelTrait),
                    };
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
