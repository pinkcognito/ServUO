using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Server.Accounting;
using Server.Network;

namespace Server.Misc
{
    /// <summary>
    /// Bans any account that hasn't been linked to a Discord user (via the
    /// bot's /register command) within a grace period after creation - and
    /// automatically un-bans it once it does get linked, even if that
    /// happens after the ban. Staff accounts (AccessLevel > Player) are
    /// never touched.
    ///
    /// The bot writes linked account names to LinkedAccounts.txt (one per
    /// line) via SSM whenever /register runs - this is the only interface
    /// between the two systems, deliberately kept as simple as a shared
    /// text file rather than anything requiring the AWS SDK in ServUO.
    /// </summary>
    public class UnregisteredAccountBan
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(4);
        private static readonly string LinkedAccountsPath = Path.Combine(Core.BaseDirectory, "LinkedAccounts.txt");

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.Zero, CheckInterval, CheckAccounts);
        }

        private static void CheckAccounts()
        {
            var linked = LoadLinkedAccounts();

            foreach (IAccount acc in Accounts.GetAccounts())
            {
                if (acc.AccessLevel > AccessLevel.Player)
                {
                    continue;
                }

                bool isLinked = linked.Contains(acc.Username);
                bool pastGrace = (DateTime.UtcNow - acc.Created) > GracePeriod;

                if (!isLinked && pastGrace && !acc.Banned)
                {
                    acc.Banned = true;
                    Console.WriteLine(
                        "UnregisteredAccountBan: Banned unregistered account '{0}' (created {1:g} UTC)",
                        acc.Username,
                        acc.Created);
                    KickIfOnline(acc);
                }
                else if (isLinked && acc.Banned)
                {
                    acc.Banned = false;
                    Console.WriteLine("UnregisteredAccountBan: Un-banned newly-registered account '{0}'", acc.Username);
                }
            }
        }

        private static void KickIfOnline(IAccount acc)
        {
            foreach (var ns in NetState.Instances.Where(n => n.Account == acc).ToArray())
            {
                ns.Dispose();
            }
        }

        private static HashSet<string> LoadLinkedAccounts()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(LinkedAccountsPath))
                {
                    foreach (var line in File.ReadAllLines(LinkedAccountsPath))
                    {
                        var trimmed = line.Trim();

                        if (trimmed.Length > 0)
                        {
                            result.Add(trimmed);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("UnregisteredAccountBan: Failed to read linked accounts file: {0}", e.Message);
            }

            return result;
        }
    }
}
