using PTMod.Core;
using GameStringFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The words and the formatters of the game for the texts the pure builders write (<see cref="TipFormat"/>): <c>ModL10n</c> for the words and
    /// <c>StringFormatter</c> for times, sums and frequencies, so a number in a tooltip, a row of the list or a line of the modifiers panel reads
    /// exactly as on the buttons of the tree and in the windows of the game (NFR-6). One shared instance; it holds no state.
    /// </summary>
    internal static class GameTipFormat
    {
        public static readonly TipFormat Instance = new TipFormat
        {
            L = (key, args) => ModL10n.L(key, args),
            Duration = days => GameStringFormatter.DaysToDate(days),
            Money = amount => GameStringFormatter.FloatToMoney(amount, string.Empty, true, true, true),
            Frequency = frequency => GameStringFormatter.FloatToFrequency(frequency),
            Has = key => ModL10n.Has(key),
        };
    }
}
