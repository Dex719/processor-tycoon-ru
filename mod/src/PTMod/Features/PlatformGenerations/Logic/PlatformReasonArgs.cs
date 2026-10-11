using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The arguments of the reasons of <c>contract.signChance</c>, made ahead of time (FR-10 item 3, D12): the provider answers a question with
    /// a <see cref="ProviderResult"/> whose <c>ReasonArgs</c> is one of the arrays built in <see cref="Rebuild"/> (once a month, when the
    /// language changes), so the call itself builds no strings and no arrays for the usual answers. The reasons and their placeholders are in the
    /// string tables: <c>pg.reason.oemPrefers</c> {0} the name of the platform, <c>pg.reason.oemLag</c> {0} the number of generations behind,
    /// <c>pg.reason.oemAdoption</c> {0} the name of the platform {1} the adoption in percent, <c>pg.reason.oemLoyalty</c> none.
    /// </summary>
    public sealed class PlatformReasonArgs
    {
        private static readonly string[] NoArgs = new string[0];

        private readonly Dictionary<string, string[]> _name = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _adoption = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private string[][] _lag = new string[0][];

        /// <summary>How many generations have prepared arguments.</summary>
        public int Count
        {
            get { return _name.Count; }
        }

        /// <summary>
        /// Prepares the arguments for every generation of the catalog: the name (<paramref name="nameOf"/>, the name in the current language),
        /// the adoption of the generation on the day (<paramref name="newestFirst"/> are the inputs of the day) and the lag counts 1..n. Replaces
        /// what was prepared before. Never throws on a missing name (an empty one is used).
        /// </summary>
        public void Rebuild(GenerationCatalog catalog, IReadOnlyList<GenAdoptionInput> newestFirst, Func<GenerationDef, string> nameOf)
        {
            _name.Clear();
            _adoption.Clear();

            int count = 0;
            if (catalog != null)
            {
                IReadOnlyList<GenerationDef> generations = catalog.Generations;
                count = generations.Count;
                for (int i = 0; i < generations.Count; i++)
                {
                    GenerationDef gen = generations[i];
                    if (gen == null || string.IsNullOrEmpty(gen.id)) continue;
                    _name[gen.id] = new[] { NameOf(gen, nameOf) };
                }
            }

            if (newestFirst != null)
            {
                for (int i = 0; i < newestFirst.Count; i++)
                {
                    GenerationDef gen = newestFirst[i].Gen;
                    if (gen == null || string.IsNullOrEmpty(gen.id)) continue;
                    _adoption[gen.id] = new[] { NameOf(gen, nameOf), Percent(OemAdoptionModel.Adoption(newestFirst[i]) * 100.0) };
                }
            }

            var lag = new string[count + 1][];
            for (int i = 1; i < lag.Length; i++) lag[i] = new[] { i.ToString(CultureInfo.InvariantCulture) };
            _lag = lag;
        }

        /// <summary>Forgets everything that was prepared.</summary>
        public void Clear()
        {
            _name.Clear();
            _adoption.Clear();
            _lag = new string[0][];
        }

        /// <summary>
        /// The answer for the provider: the multiplier with its reason and the prepared arguments. A result without a reason gives just the
        /// multiplier. A generation or a lag that was not prepared (the catalog changed since) gets an array built on the spot.
        /// </summary>
        public ProviderResult Of(ChanceResult result)
        {
            if (string.IsNullOrEmpty(result.ReasonKey)) return new ProviderResult(result.Multiplier);
            return new ProviderResult(result.Multiplier, result.ReasonKey, ArgsOf(result));
        }

        private string[] ArgsOf(ChanceResult result)
        {
            string[] args;
            switch (result.ReasonKey)
            {
                case OemAdoptionModel.ReasonPrefers:
                    if (result.GenId != null && _name.TryGetValue(result.GenId, out args)) return args;
                    return new[] { result.GenId ?? string.Empty };

                case OemAdoptionModel.ReasonLag:
                    if (result.LagGenerations > 0 && result.LagGenerations < _lag.Length) return _lag[result.LagGenerations];
                    return new[] { result.LagGenerations.ToString(CultureInfo.InvariantCulture) };

                case OemAdoptionModel.ReasonAdoption:
                    if (result.GenId != null && _adoption.TryGetValue(result.GenId, out args)) return args;
                    return new[] { result.GenId ?? string.Empty, Percent(result.AdoptionPercent) };

                default:
                    return NoArgs;
            }
        }

        private static string NameOf(GenerationDef gen, Func<GenerationDef, string> nameOf)
        {
            string name = nameOf == null ? null : nameOf(gen);
            return name ?? string.Empty;
        }

        /// <summary>A percentage with one decimal place and a dot, whatever the culture of the machine: "47.5".</summary>
        public static string Percent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
