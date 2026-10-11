using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The words and the numbers of the windows as the game writes them (TSK-023, TSK-024, TSK-025, TSK-027; NFR-6): the texts are <c>ModL10n</c>'s, the money, the shares
    /// and the frequencies are <c>StringFormatter.FloatToMoney</c>, <c>FloatToPercentageString</c> and <c>FloatToFrequency</c>, so a number reads in a window of the
    /// feature the way it reads in the windows of the game. The game's class is touched only in the small methods that carry <c>NoInlining</c>; when it does not match the
    /// game (or is not ready) the number is written plainly instead of throwing into a window. Main thread only.
    /// </summary>
    internal sealed class GameUiTexts : IUiTexts
    {
        /// <summary>The one instance: the class keeps no state.</summary>
        internal static readonly GameUiTexts Instance = new GameUiTexts();

        private GameUiTexts()
        {
        }

        public string Text(string key, params object[] args)
        {
            return ModL10n.L(key, args ?? new object[0]);
        }

        public string Plural(string key, long n, params object[] args)
        {
            return ModL10n.Plural(key, n, args ?? new object[0]);
        }

        public string Money(double amount)
        {
            try
            {
                return FormatMoney((float)amount);
            }
            catch (Exception)
            {
                return amount.ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        public string Percent(double fraction)
        {
            try
            {
                return FormatPercent((float)fraction);
            }
            catch (Exception)
            {
                return (fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }
        }

        public string Number(double value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        public string Frequency(double kilohertz)
        {
            try
            {
                return FormatFrequency((float)kilohertz);
            }
            catch (Exception)
            {
                return (kilohertz / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " MHz";
            }
        }

        public string Date(DateTime date)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>The display name of a feature by id (its <c>NameKey</c> in the current language), or null for a feature the registry does not know.</summary>
        internal static string FeatureName(string featureId)
        {
            FeatureInfo info = ModFeatures.Find(featureId);
            return info == null || string.IsNullOrEmpty(info.NameKey) ? null : ModL10n.L(info.NameKey);
        }

        // ---- the game's StringFormatter: touched only below ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string FormatMoney(float amount)
        {
            return GameFormatter.FloatToMoney(amount, string.Empty, true, true, true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string FormatPercent(float fraction)
        {
            return GameFormatter.FloatToPercentageString(fraction, true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string FormatFrequency(float kilohertz)
        {
            return GameFormatter.FloatToFrequency(kilohertz);
        }
    }
}
