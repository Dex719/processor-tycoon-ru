using PTMod.Core;
using GameStringFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The <see cref="ViewText"/> of the game: the string tables of the mod (<c>ModL10n</c>, in the language of the moment), the names of the
    /// generations from the content, the names of companies and sockets as the game spells them (<see cref="GameAdapter"/>) and the units written
    /// by the game's own <c>StringFormatter.FloatToIntString</c> (NFR-6). One instance serves the notifier, the release warning and the window -
    /// nothing in it is cached, so a new language or a renamed company shows at once.
    /// </summary>
    internal static class PlatformViewText
    {
        public static readonly ViewText Instance = new ViewText
        {
            Text = ModL10n.L,
            GenerationName = gen => gen.name == null ? null : ModL10n.Resolve(gen.name),
            CompanyName = GameAdapter.CompanyNameOf,
            SocketName = GameAdapter.SocketNameOf,
            CountText = Units,
        };

        /// <summary>Pieces the way the game writes them (no unit, no abbreviation); empty on a failure, so the view falls back to its own spelling.</summary>
        private static string Units(double units)
        {
            try
            {
                return GameStringFormatter.FloatToIntString((float)units, string.Empty, false);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
