using System;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using GameFormatter = ProcessorTycoon.StringFormatter;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The block "Архитектуры" of the panel "Все модификаторы" and of its tooltip in <c>research-tree-ux</c> (FR-5 item 8 of that spec; design.md
    /// Component 14), registered once with <c>ResearchTreeUxApi.RegisterModifierLines</c>. For the player's company it lists:
    /// <list type="bullet">
    /// <item>the architecture the lab is busy with: its name, the progress, the time left at the current funding (or that it is paused and why), and
    /// the cost per month of that research (<see cref="ArchResearchService.Forecast"/>: <c>monthlyCost × the research cost modifier × funding</c>);</item>
    /// <item>the multipliers of the family the player designs on now, each as a factor of the panel: IPC and IPS, power, the cost of a project, with the immature tools of a young architecture at stage 2
    /// (<see cref="DesignContext.FamilyFor"/> - what the designer really applies);</item>
    /// </list>
    /// and nothing (null) when there is nothing to tell: the feature is off, the player has no state yet, no research and a neutral family. The lines
    /// are plain data for the window, which translates their keys with <c>ModL10n</c>; the arguments are already formatted.
    /// </summary>
    internal sealed class ArchModifierLines : IModifierLineSource
    {
        public const string TitleKey = "arch.mods.title";

        private readonly ArchitecturesFeature _feature;

        public ArchModifierLines(ArchitecturesFeature feature)
        {
            _feature = feature;
        }

        public ModifierLineGroup GetGroup(ICompany company)
        {
            if (_feature == null || ReferenceEquals(company, null) || _feature.IsNeutralNow || _feature.Registry == null || _feature.Catalog == null) return null;

            CompanyArchState state = _feature.Registry.GetOrNull(company.SaveID);
            var group = new ModifierLineGroup { TitleKey = TitleKey };

            if (state != null && state.CurrentResearch != null) AddResearch(group, company, state);
            AddDesign(group, company);

            return group.Lines.Count == 0 ? null : group;
        }

        // ---- the research in the lab ----

        private void AddResearch(ModifierLineGroup group, ICompany company, CompanyArchState state)
        {
            ArchFamily family = _feature.Catalog.Get(state.CurrentResearch);
            string name = ArchDisplay.FamilyName(_feature.Catalog, state.CurrentResearch, ModL10n.L);
            FamilyState progress = state.GetFamily(state.CurrentResearch);
            string percent = ((int)Math.Floor((progress == null ? 0f : progress.Progress) * 100f)).ToString(CultureInfo.InvariantCulture);

            if (state.Paused)
            {
                group.Lines.Add(Line("arch.mods.paused", name, percent, PauseText(state.PauseReason)));
                return;
            }
            if (family == null || _feature.Research == null)
            {
                group.Lines.Add(Line("arch.mods.research", name, percent, ArchDisplay.Empty));
                return;
            }

            ResearchForecast forecast = _feature.Research.Forecast(company, family.Id);
            string left = forecast.EtaDays == ArchResearchMath.InfiniteEta ? "∞" : GameFormatter.DaysToDate(forecast.EtaDays);
            group.Lines.Add(Line("arch.mods.research", name, percent, left));
            group.Lines.Add(Line("arch.mods.cost", name, GameFormatter.FloatToMoney(forecast.MonthlyCost, string.Empty, true, true, true)));
        }

        private static string PauseText(PauseReason reason)
        {
            switch (reason)
            {
                case PauseReason.TechnologyChosen: return ModL10n.L("arch.pause.technology");
                case PauseReason.Default: return ModL10n.L("arch.pause.default");
                case PauseReason.NoFunds: return ModL10n.L("arch.pause.noFunds");
                case PauseReason.FeatureDisabled: return ModL10n.L("arch.pause.disabled");
                default: return ArchDisplay.Empty;
            }
        }

        // ---- the family designed on ----

        private void AddDesign(ModifierLineGroup group, ICompany company)
        {
            string familyId = DesignContext.FamilyFor(company);
            ArchFamily family = familyId == null ? null : _feature.Catalog.Get(familyId);
            if (family == null) return;

            // The price of a project includes the immature tools of a young architecture (FR-12 item 3, stage 2): the line shows what the provider of the channel gives.
            float surcharge = DesignContext.ImmatureSurcharge(company, familyId);
            if (DesignMath.IsNeutral(family.Design) && surcharge <= 0f) return;

            string name = ModL10n.L(family.NameKey);
            AddFactor(group, "arch.mods.ipc", name, family.Design.Ipc);
            AddFactor(group, "arch.mods.power", name, family.Design.Power);
            AddFactor(group, "arch.mods.devCost", name, DesignMath.CostFactor(family.Design, surcharge));
        }

        private static void AddFactor(ModifierLineGroup group, string key, string familyName, float factor)
        {
            if (factor == 1f) return;
            ModifierLine line = Line(key, familyName);
            line.Multiplier = factor;
            group.Lines.Add(line);
        }

        private static ModifierLine Line(string key, params object[] args)
        {
            return new ModifierLine { LabelKey = key, Args = args, SourceFeatureId = ArchitecturesFeature.FeatureId };
        }
    }
}
