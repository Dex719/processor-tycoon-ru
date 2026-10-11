using System;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The player-facing words and figures of the effect lines (<c>ms.fx.*</c>, design.md, "Отображение в журнале"): the name of a channel, an end date and a sum
    /// of money, in the language of the mod and in the look of the game. Every method answers something printable and never throws: a line of the journal
    /// must not fail because a name is missing.
    /// </summary>
    internal static class ShockText
    {
        /// <summary>The mod's language is Russian (the decimal comma, the groups of digits separated by a space).</summary>
        public static bool IsRussian
        {
            get { return string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// The name of a channel for the player: the text of <c>ms.channel.&lt;channel&gt;</c> when the tables have it, otherwise the channel's own name
        /// (<c>production.memoryCost</c>), which is still readable. Null or empty gives an empty text.
        /// </summary>
        public static string ChannelName(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return string.Empty;

            string key = "ms.channel." + channel;
            return ModL10n.Has(key) ? ModL10n.L(key) : channel;
        }

        /// <summary>The mark that the limits of a channel cut a value: "(ограничено)" / "(capped)".</summary>
        public static string ClampedMark()
        {
            return ModL10n.L("ms.fx.clamped");
        }

        /// <summary>
        /// The date a timed effect ends on. <paramref name="endDay"/> is the end day the engine gave the effect; with none yet (0: the effect is only resolved,
        /// the player is still deciding) the end is counted from today, <paramref name="totalDays"/> ahead. Formatted by the game's
        /// <c>StringFormatter.DateToString</c> (the date format the player chose); empty when nothing is known.
        /// </summary>
        public static string EndDate(int endDay, int totalDays)
        {
            int day = endDay;
            if (day <= 0)
            {
                DateTime today = ModTicks.Today;
                if (today.Year <= 1) return string.Empty;
                day = ShockFormat.DayOf(today) + Math.Max(totalDays, 0);
            }

            DateTime date = ShockFormat.DateOfDay(day);
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return date.ToString("yyyy-MM-dd");
            }
        }

        /// <summary>A sum of money as the game writes it ("$1.20M"); empty when the game's formatter is not there.</summary>
        public static string Money(float amount)
        {
            try
            {
                return GameFormatter.FloatToMoney(amount, "", true, true, true);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
