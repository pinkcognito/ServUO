using System;

using Server.Mobiles;

namespace Server.Misc
{
    public class RenameRequests
    {
        public static void Initialize()
        {
            EventSink.RenameRequest += new RenameRequestEventHandler(EventSink_RenameRequest);
        }

        private static void EventSink_RenameRequest(RenameRequestEventArgs e)
        {
            Mobile from = e.From;
            Mobile targ = e.Target;
            string name = e.Name;

            if (from.CanSee(targ) && from.InRange(targ, 12) && targ.CanBeRenamedBy(from))
            {
                name = name.Trim();

                if (name.Length == 0)
                {
                    from.SendMessage("That name is unacceptable.");
                    return;
                }

                if (Core.ML)
                {
                    from.SendLocalizedMessage(1072623, String.Format("{0}\t{1}", targ.Name, name)); // Pet ~1_OLDPETNAME~ renamed to ~2_NEWPETNAME~.
                }

                targ.Name = name;
            }
        }
    }
}
