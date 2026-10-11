using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using Multicore = ProcessorTycoon.Hardware.Multicore;
using Technology = ProcessorTycoon.ResearchSystem.Technology;
using TechnologyType = ProcessorTycoon.ResearchSystem.TechnologyType;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The lines the tooltip of a technology of the kind "multicore" gets in the research tree (FR-13 item 3, stage 2; design.md Component 14), registered once with
    /// <c>ResearchTreeUxApi.RegisterTechTooltipLines</c>: the technologies of more cores (Dual Core … Octa Core) and the technology of SMT give their full effect on the market only
    /// with enough software support of the architecture, so the tooltip says how much support the extension needs, what the rating of a CPU with it is multiplied by without any,
    /// and the support the player has now for the architecture he designs on. The extensions that belong to a technology are <see cref="ExtensionWarnings.ForTechnology"/>
    /// (cores or SMT of <c>balance.json</c>); other technologies get no line.
    /// <para>
    /// Asked every time the cursor stops over a technology, so it is cheap: a check of the kind, one short list. No lines at stage 1, while the feature is off and for a technology
    /// the feature knows nothing about. An exception is counted against this feature by <c>research-tree-ux</c> (it asks inside a guard) and only these lines go away.
    /// </para>
    /// </summary>
    internal sealed class ArchTechTooltipLines : ITechTooltipLineSource
    {
        public const string LineKey = "arch.techtip.ext";

        private readonly ArchitecturesFeature _feature;

        public ArchTechTooltipLines(ArchitecturesFeature feature)
        {
            _feature = feature;
        }

        public IEnumerable<string> GetLines(Technology technology)
        {
            if (_feature == null || technology == null || _feature.IsNeutralNow || _feature.Registry == null || _feature.Catalog == null) return null;

            BalanceConfig balance = _feature.Balance;
            if (!balance.IsFullStage || technology.Type != TechnologyType.Multicore) return null;

            var hardware = technology.Hardware as Multicore;
            int cores = hardware == null ? 0 : hardware.CoreCount;
            List<ExtensionDef> extensions = ExtensionWarnings.ForTechnology(balance.extensions, cores);
            if (extensions.Count == 0) return null;

            string family;
            string support;
            SupportOfPlayer(out family, out support);

            string language = ModL10n.Language;
            var lines = new List<string>(extensions.Count);
            foreach (ExtensionDef extension in extensions)
            {
                string key = ExtensionWarnings.NameKey(extension.id);
                string name = key == null ? extension.id : ModL10n.L(key);
                lines.Add(ModL10n.L(LineKey, name, SupportMath.Score(extension.requiredSupport), RatingForecast.Format(extension.penalty, language), family, support));
            }
            return lines;
        }

        /// <summary>The architecture the player designs on (the one he picked last) and his support for it, as texts; dashes when he has none yet.</summary>
        private void SupportOfPlayer(out string family, out string support)
        {
            family = ArchDisplay.Empty;
            support = ArchDisplay.Empty;

            CompanyArchState player = _feature.Registry.Player;
            if (player == null || player.Active == null) return;

            family = ArchDisplay.FamilyName(_feature.Catalog, player.Active, ModL10n.L);
            FamilyState state = player.GetFamily(player.Active);
            if (state != null) support = SupportMath.Score(state.Support).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
