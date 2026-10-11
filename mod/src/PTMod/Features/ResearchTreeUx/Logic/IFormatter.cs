namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// How the compare table writes a value (FR-6, design: Component 8). In the game it is a thin cover over <c>StringFormatter</c>
    /// (<c>GameFormatter</c>: <c>FloatToIps</c>, <c>FloatToFrequency</c>, <c>FloatToMoney</c>, <c>CacheString</c>, <c>CoreCountString</c>,
    /// <c>FloatToPercentageString</c>, <c>FloatToString</c>, <c>FloatToIntString</c>) and of <c>ModL10n</c>, so a number reads exactly as in the
    /// inspector and the market windows (NFR-6); a test passes a simple one. The model never formats a game value on its own.
    /// </summary>
    public interface IFormatter
    {
        /// <summary>Instructions per second (<c>StringFormatter.FloatToIps</c>).</summary>
        string Ips(float ips);

        /// <summary>A frequency (<c>StringFormatter.FloatToFrequency</c>).</summary>
        string Frequency(float frequency);

        /// <summary>An amount of money (<c>StringFormatter.FloatToMoney</c>).</summary>
        string Money(float amount);

        /// <summary>A fraction as a percentage: 0.985 is "98,5 %" (<c>StringFormatter.FloatToPercentageString</c>).</summary>
        string Percent(float fraction);

        /// <summary>The count of cores (<c>StringFormatter.CoreCountString</c>: "Single Core", "4 Cores").</summary>
        string Cores(int coreCount);

        /// <summary>A cache of the given level (1, 2 or 3) of this many steps (<c>StringFormatter.CacheString(steps, 4 / 16 / 64)</c>).</summary>
        string Cache(int level, int steps);

        /// <summary>A number with a unit (<c>StringFormatter.FloatToString(value, unit, ...)</c>): "65 W", "81 °C", "2,3".</summary>
        string Number(float value, string unit);

        /// <summary>A whole number (<c>StringFormatter.FloatToIntString</c>).</summary>
        string Integer(float value);

        /// <summary>The text of a key of the string tables with its arguments (<c>ModL10n.L</c>).</summary>
        string Text(string key, params object[] args);
    }
}
