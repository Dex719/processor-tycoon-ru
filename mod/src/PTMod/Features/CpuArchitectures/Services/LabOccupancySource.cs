using System;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// Tells <c>research-tree-ux</c> that the player's lab is busy with an architecture (FR-2 item 10, AC-9.2 there; design.md Component 4): while an
    /// architecture is the current research of the player and is not paused, <c>ResearchSector.CurrentResearch()</c> is null, so without this source the
    /// research list would show an idle lab and the daily reminder would nag. The list shows "Лаборатория занята: Архитектура «x86» — 43 %" and the
    /// reminder keeps quiet. A paused architecture does not occupy the lab (the reminder is right to speak then). Registered once in
    /// <c>ArchitecturesFeature.Register</c> with <c>ResearchTreeUxApi.RegisterLabOccupancy</c>; <c>research-tree-ux</c> keeps the registration while it is off
    /// and does not ask a source of a feature that is switched off. Neither member throws (an exception would switch the source off for the session).
    /// </summary>
    internal sealed class LabOccupancySource : ILabOccupancySource
    {
        private readonly ArchitecturesFeature _feature;

        public LabOccupancySource(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>True while an architecture of the player runs in the lab.</summary>
        public bool IsLabBusy()
        {
            return ModHooks.Guard(ArchitecturesFeature.FeatureId, "lab/occupancy.busy", () =>
            {
                ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
                return _feature.Lab != null && !ReferenceEquals(player, null) && _feature.Lab.IsOccupiedByArchitecture(player);
            }, false);
        }

        /// <summary>"Архитектура «x86» — 43 %": the family and the share of the research done.</summary>
        public string BusyText()
        {
            return ModHooks.Guard(ArchitecturesFeature.FeatureId, "lab/occupancy.text", () =>
            {
                ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
                CompanyArchState state = ReferenceEquals(player, null) || _feature.Registry == null ? null : _feature.Registry.GetOrNull(player.SaveID);
                if (state == null || state.CurrentResearch == null) return string.Empty;

                FamilyState progress = state.GetFamily(state.CurrentResearch);
                string family = ArchDisplay.FamilyName(_feature.Catalog, state.CurrentResearch, ModL10n.L);
                return ModL10n.L("arch.lab.busyText", family, FamilyCardModel.Percent(progress == null ? 0f : progress.Progress));
            }, string.Empty);
        }
    }
}
