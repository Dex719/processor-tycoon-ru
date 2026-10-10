using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>
    /// The daily delivery limits of mod mail (FR-14 item 8): at most <see cref="MaxPerDay"/> mails of <see cref="MailPriority.Normal"/> and
    /// <see cref="MailPriority.Low"/> per game day and at most <see cref="MaxPerFeature"/> of them from one feature. <see cref="MailPriority.Critical"/>
    /// mails bypass both limits and are not counted. The counters belong to one game day: <see cref="StartDay"/> with another date clears them,
    /// with the same date keeps them, so two delivery passes on one day (the daily tick and the end of a batch run) share the allowance.
    /// Defaults are the settings <c>mail.maxDeliveredPerDay</c> (3) and <c>mail.maxPerFeaturePerDay</c> (2); the limits can change at any time.
    /// Not thread-safe: main thread only.
    /// </summary>
    public sealed class MailQuota
    {
        public const int DefaultMaxPerDay = 3;
        public const int DefaultMaxPerFeature = 2;

        /// <summary>Limits are clamped to [1, <see cref="LimitCeiling"/>]; a zero limit would hold every non-critical mail forever.</summary>
        public const int LimitCeiling = 1000;

        private readonly Dictionary<string, int> perFeature = new Dictionary<string, int>(StringComparer.Ordinal);
        private DateTime? day;
        private int total;

        public MailQuota(int maxPerDay = DefaultMaxPerDay, int maxPerFeature = DefaultMaxPerFeature)
        {
            MaxPerDay = ClampLimit(maxPerDay);
            MaxPerFeature = ClampLimit(maxPerFeature);
        }

        /// <summary>Non-critical mails per game day, all features together.</summary>
        public int MaxPerDay { get; private set; }

        /// <summary>Non-critical mails per game day from one feature.</summary>
        public int MaxPerFeature { get; private set; }

        /// <summary>The game day the counters belong to; null before the first <see cref="StartDay"/>.</summary>
        public DateTime? Day
        {
            get { return day; }
        }

        /// <summary>Non-critical mails counted for <see cref="Day"/>.</summary>
        public int DeliveredToday
        {
            get { return total; }
        }

        /// <summary>Changes both limits (settings changed in the hub); the counters stay.</summary>
        public void Configure(int maxPerDay, int maxPerFeature)
        {
            MaxPerDay = ClampLimit(maxPerDay);
            MaxPerFeature = ClampLimit(maxPerFeature);
        }

        /// <summary>Non-critical mails of the feature counted for <see cref="Day"/>.</summary>
        public int DeliveredTodayBy(string featureId)
        {
            int count;
            return perFeature.TryGetValue(featureId ?? string.Empty, out count) ? count : 0;
        }

        /// <summary>
        /// Makes <paramref name="today"/> (the time part is ignored) the day of the counters: another date than <see cref="Day"/> clears
        /// them, the same date leaves them alone.
        /// </summary>
        public void StartDay(DateTime today)
        {
            DateTime date = today.Date;
            if (day.HasValue && day.Value == date) return;
            day = date;
            total = 0;
            perFeature.Clear();
        }

        /// <summary>True when one more mail of this priority from this feature may be delivered today.</summary>
        public bool Allows(MailPriority priority, string featureId)
        {
            if (priority == MailPriority.Critical) return true;
            return total < MaxPerDay && DeliveredTodayBy(featureId) < MaxPerFeature;
        }

        /// <summary>Counts a delivered mail (nothing for <see cref="MailPriority.Critical"/>).</summary>
        public void Record(MailPriority priority, string featureId)
        {
            if (priority == MailPriority.Critical) return;
            string key = featureId ?? string.Empty;
            int count;
            perFeature.TryGetValue(key, out count);
            perFeature[key] = count + 1;
            total++;
        }

        /// <summary>True when no non-critical mail can be delivered today, whichever feature sends it (lets the delivery pass stop early).</summary>
        public bool IsDayFull
        {
            get { return total >= MaxPerDay; }
        }

        /// <summary>Forgets the day and all counters (new game, exit to the menu, before a save is loaded).</summary>
        public void Reset()
        {
            day = null;
            total = 0;
            perFeature.Clear();
        }

        private static int ClampLimit(int limit)
        {
            if (limit < 1) return 1;
            return limit > LimitCeiling ? LimitCeiling : limit;
        }
    }
}
