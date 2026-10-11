using PTMod.Core;
using GameStringFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The formatters of the game behind the compare table (<see cref="IFormatter"/>, design: Component 8): the same calls <c>CpuUI.SetCpu</c> and
    /// the market windows make, so a value reads exactly as in the inspector (NFR-6), and <c>ModL10n</c> for the words. Holds no state.
    /// </summary>
    internal sealed class GameFormatter : IFormatter
    {
        // The size of a step of each level of cache in kilobytes, as CpuUI.SetCpu passes it to StringFormatter.CacheString.
        private const float L1StepKb = 4f;
        private const float L2StepKb = 16f;
        private const float L3StepKb = 64f;

        public static readonly GameFormatter Instance = new GameFormatter();

        public string Ips(float ips)
        {
            return GameStringFormatter.FloatToIps(ips);
        }

        public string Frequency(float frequency)
        {
            return GameStringFormatter.FloatToFrequency(frequency);
        }

        public string Money(float amount)
        {
            return GameStringFormatter.FloatToMoney(amount, string.Empty, true, true, true);
        }

        public string Percent(float fraction)
        {
            return GameStringFormatter.FloatToPercentageString(fraction, true);
        }

        public string Cores(int coreCount)
        {
            return GameStringFormatter.CoreCountString(coreCount);
        }

        public string Cache(int level, int steps)
        {
            float step = level == 1 ? L1StepKb : level == 2 ? L2StepKb : L3StepKb;
            return GameStringFormatter.CacheString(steps, step);
        }

        public string Number(float value, string unit)
        {
            return GameStringFormatter.FloatToString(value, unit ?? string.Empty, false, false);
        }

        public string Integer(float value)
        {
            return GameStringFormatter.FloatToIntString(value, string.Empty, false);
        }

        public string Text(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }
    }
}
