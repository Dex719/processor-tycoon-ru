using System;
using System.Globalization;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The words and figures of the windows, letters and lines of the effects, in the player's language and in the look of the game: the money as
    /// <c>StringFormatter.FloatToMoney</c> writes it, the names of the severity, the type and the CPU. Every method answers something printable and never
    /// throws: a line must not fail because a name is missing.
    /// </summary>
    internal sealed class FlawFormat : IFlawFormat
    {
        /// <summary>The one formatter of the feature.</summary>
        public static readonly FlawFormat Instance = new FlawFormat();

        private FlawFormat()
        {
        }

        /// <summary>A sum of money as the game writes it (abbreviated, with the currency): <c>$66.00M</c>.</summary>
        public string Money(float amount)
        {
            try
            {
                return GameFormatter.FloatToMoney(amount, string.Empty, true, true, true);
            }
            catch (Exception)
            {
                return amount.ToString("0", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>A count of units with the groups of digits separated (a space in Russian, a comma in English).</summary>
        public string Count(long units)
        {
            string text = units.ToString("N0", CultureInfo.InvariantCulture);
            return FlawText.IsRussian ? text.Replace(',', ' ') : text;
        }

        /// <summary>A share as a percent without the sign, up to one decimal: <c>0.125</c> gives <c>12.5</c>.</summary>
        public string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The names the texts of the feature are made of.</summary>
    internal static class FlawText
    {
        /// <summary>The mod's language is Russian.</summary>
        public static bool IsRussian
        {
            get { return string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>The severity in words: <c>dfr.severity.0</c> (minor), <c>.1</c> (major), <c>.2</c> (critical).</summary>
        public static string SeverityName(FlawSeverity severity)
        {
            return ModL10n.L("dfr.severity." + FlawMath.SeverityIndex(severity).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The kind of the flaw in words (<c>nameKey</c> of its type in the content); the id of the type when the content has no such type.</summary>
        public static string TypeName(FlawBalance balance, string typeId)
        {
            FlawTypeDef type = balance == null ? null : balance.FindType(typeId);
            if (type == null || string.IsNullOrEmpty(type.NameKey)) return typeId ?? string.Empty;
            return ModL10n.Has(type.NameKey) ? ModL10n.L(type.NameKey) : typeId ?? string.Empty;
        }

        /// <summary>The name of the CPU as the player knows it; empty when the game has no CPU with this <c>SaveID</c>.</summary>
        public static string CpuName(int cpuSaveId)
        {
            try
            {
                GameCpu cpu = GameDataFinder.FindCpu(cpuSaveId);
                return cpu == null || cpu.Name == null ? string.Empty : cpu.Name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>The name of a response as on its button: <c>dfr.choice.recall</c> and so on.</summary>
        public static string ChoiceName(FlawResponse response)
        {
            string choice = FlawDecisionPlanner.ChoiceOfResponse(response);
            return choice == null ? string.Empty : ModL10n.L("dfr.choice." + choice);
        }
    }
}
