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
    /// never touched. While still within the grace period, online
    /// unregistered players get a periodic system message with their
    /// remaining time.
    ///
    /// The bot writes linked account names to LinkedAccounts.txt (one per
    /// line) via SSM whenever /register runs - this is the only interface
    /// between the two systems, deliberately kept as simple as a shared
    /// text file rather than anything requiring the AWS SDK in ServUO. A
    /// FileSystemWatcher on that file triggers an immediate re-check
    /// (debounced, and marshaled onto ServUO's own timer thread rather than
    /// acting directly from the watcher's background thread) so un-banning
    /// after /register doesn't wait for the next scheduled 5-minute tick -
    /// banning itself still only happens on that periodic schedule, since
    /// it depends on elapsed time, not a file change.
    /// </summary>
    public class UnregisteredAccountBan
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(1);
        private static readonly string LinkedAccountsPath = Path.Combine(Core.BaseDirectory, "LinkedAccounts.txt");

        private static Timer m_DebounceTimer;

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.Zero, CheckInterval, CheckAccounts);

            try
            {
                var watcher = new FileSystemWatcher(Core.BaseDirectory, "LinkedAccounts.txt")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += (sender, args) => ScheduleImmediateCheck();
                watcher.Created += (sender, args) => ScheduleImmediateCheck();
            }
            catch (Exception e)
            {
                Console.WriteLine("UnregisteredAccountBan: Failed to start file watcher: {0}", e.Message);
            }
        }

        private static void ScheduleImmediateCheck()
        {
            // FileSystemWatcher fires on a background thread pool thread and
            // can fire multiple times for a single logical write - debounce
            // by restarting a short one-shot timer, which itself runs on
            // ServUO's own timer thread (safe to touch Account/NetState from
            // there, unlike directly from the watcher callback).
            m_DebounceTimer?.Stop();
            m_DebounceTimer = Timer.DelayCall(TimeSpan.FromSeconds(2), CheckAccounts);
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
                var remaining = GracePeriod - (DateTime.UtcNow - acc.Created);

                if (isLinked)
                {
                    if (acc.Banned)
                    {
                        acc.Banned = false;
                        Console.WriteLine("UnregisteredAccountBan: Un-banned newly-registered account '{0}'", acc.Username);
                    }

                    continue;
                }

                if (remaining <= TimeSpan.Zero)
                {
                    if (!acc.Banned)
                    {
                        acc.Banned = true;
                        Console.WriteLine(
                            "UnregisteredAccountBan: Banned unregistered account '{0}' (created {1:g} UTC)",
                            acc.Username,
                            acc.Created);
                        KickIfOnline(acc);
                    }
                }
                else
                {
                    WarnIfOnline(acc, remaining);
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

        private static void WarnIfOnline(IAccount acc, TimeSpan remaining)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));

            foreach (var ns in NetState.Instances.Where(n => n.Account == acc && n.Mobile != null).ToArray())
            {
                ns.Mobile.SendMessage(
                    0x35,
                    "Your account isn't linked to Discord yet - run /register in Discord within {0} minute{1} or you'll be banned.",
                    minutes,
                    minutes == 1 ? "" : "s");
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
