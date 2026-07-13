using System;
using System.Collections.Generic;

using Server.Mobiles;

namespace Server.Custom.AIAgents
{
    // Thin adapter over the frozen /decide contract (§2.2). Reflexive
    // behavior (wander/flee) stays on the inherited BaseAI FSM; only
    // conversation hits the cognition sidecar.
    public class BotAI : BaseAI
    {
        private const int NearbyRange = 10;

        private bool _awaitingDecision;

        public string BotId { get; set; }
        public string PersonaId { get; set; }
        public Mobile FollowTarget { get; set; }

        public BotAI(BaseCreature m)
            : base(m)
        {
        }

        public override bool HandlesOnSpeech(Mobile from)
        {
            return from.Alive && from.InRange(m_Mobile.Location, m_Mobile.RangePerception);
        }

        public override void OnSpeech(SpeechEventArgs e)
        {
            if (_awaitingDecision || string.IsNullOrWhiteSpace(e.Speech))
            {
                return;
            }

            _awaitingDecision = true;

            var request = BuildDecisionRequest(e.Mobile, e.Speech);

            AsyncDecisionPump.Enqueue(this, request);
        }

        public override bool Think()
        {
            if (FollowTarget != null && !FollowTarget.Deleted && FollowTarget.Map == m_Mobile.Map &&
                FollowTarget.Alive)
            {
                if (!m_Mobile.InRange(FollowTarget, 1))
                {
                    var d = m_Mobile.GetDirectionTo(FollowTarget);
                    m_Mobile.Direction = d;
                    m_Mobile.Move(d);
                }

                return true;
            }

            return base.Think();
        }

        // Applied only on the game thread, via AsyncDecisionPump.DrainOnGameThread.
        public void ApplyActions(List<DecisionAction> actions)
        {
            _awaitingDecision = false;

            if (actions == null)
            {
                return;
            }

            foreach (var action in actions)
            {
                switch (action.Type)
                {
                    case "say":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Say(action.Text);
                        }
                        break;

                    case "emote":
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            m_Mobile.Emote(action.Text);
                        }
                        break;

                    case "follow":
                        FollowTarget = FindNearbyMobileByName(action.Target);
                        break;

                    case "stop":
                        FollowTarget = null;
                        break;

                    case "move_to":
                    case "none":
                        break;

                    default:
                        // Unknown action types are ignored (forward-compatible, §2.2).
                        break;
                }
            }
        }

        private DecisionRequest BuildDecisionRequest(Mobile speaker, string speech)
        {
            return new DecisionRequest
            {
                BotId = BotId,
                PersonaId = PersonaId,
                Trigger = "speech",
                ConversationId = $"{BotId}:{speaker.Name}",
                Observation = new DecisionObservation
                {
                    Speaker = speaker.Name,
                    Text = speech,
                    Self = new DecisionSelfState
                    {
                        Loc = new double[] { m_Mobile.X, m_Mobile.Y, m_Mobile.Z },
                        HpPct = m_Mobile.HitsMax > 0 ? (double)m_Mobile.Hits / m_Mobile.HitsMax : 1.0,
                        State = m_Mobile.Combatant != null ? "combat" : "idle",
                    },
                    Nearby = BuildNearby(),
                },
            };
        }

        private List<DecisionNearby> BuildNearby()
        {
            var nearby = new List<DecisionNearby>();
            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m == m_Mobile)
                    {
                        continue;
                    }

                    nearby.Add(new DecisionNearby
                    {
                        Name = m.Name,
                        Kind = m.Player ? "player" : "npc",
                        Distance = m_Mobile.GetDistanceToSqrt(m),
                    });
                }
            }
            finally
            {
                eable.Free();
            }

            return nearby;
        }

        private Mobile FindNearbyMobileByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var eable = m_Mobile.GetMobilesInRange(NearbyRange);

            try
            {
                foreach (var m in eable)
                {
                    if (m != m_Mobile && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            finally
            {
                eable.Free();
            }

            return null;
        }
    }
}
