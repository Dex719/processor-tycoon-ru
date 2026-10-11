using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCompanyHelper = ProcessorTycoon.CompanySystem.CompanyHelper;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using GameProject = ProcessorTycoon.ProjectSystem.Project;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using GameResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameTimerScheduler = ProcessorTycoon.TimeSystem.TimerScheduler;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Reads the company of the player into a <see cref="ModifierSnapshot"/> for the modifiers panel and its tooltip (design: Component 7,
    /// FR-5): the perks and the funding, the totals of <c>Modifiers.GetModifier</c> and <c>ResearchMath.ResearchSpeed</c>, the accumulated bonuses
    /// of the projects (<c>Company.ResearchModifiers</c>), the research projects in progress (<c>TimerScheduler.GetTimedObjectsOfType</c>),
    /// the technology in the lab and, per type, the breakdown of the mods from <c>ModModifiers.Explain</c> asked for the player's company. Only
    /// reads. Each part is read apart under its own try/catch: what cannot be read keeps its neutral value and the rest of the report is still
    /// built (a missing breakdown becomes the line "Mods: ×k", FR-5 item 7).
    /// </summary>
    internal static class ModifierSnapshotReader
    {
        // The channel behind each value of the game's ModifierType (the core's table of patch P1; design: "тип → канал").
        private static readonly string[] Channels =
        {
            ModChannels.ResearchSpeed, ModChannels.FactoryOperatingCost, ModChannels.ConstructionCost, ModChannels.DevelopmentCost,
            ModChannels.ResearchCost, ModChannels.Taxes, ModChannels.BankInterest,
        };

        /// <summary>The channel of a modifier type (0...6), or null for a number outside it.</summary>
        public static string ChannelOf(int type)
        {
            return type >= 0 && type < Channels.Length ? Channels[type] : null;
        }

        /// <summary>
        /// Reads the snapshot of <paramref name="company"/> (the player's). Null only for a null company; otherwise a snapshot with whatever could
        /// be read. Costs a few dozen cheap calls and one <c>Explain</c> per type; nothing is kept between calls.
        /// </summary>
        public static ModifierSnapshot Read(GameCompany company)
        {
            if (ReferenceEquals(company, null)) return null;

            var snapshot = new ModifierSnapshot();
            Guard("facts", () => snapshot.Facts = ReadFacts(company));
            Guard("research", () => ReadResearch(company, snapshot));
            Guard("totals", () => ReadTotals(company, snapshot));
            Guard("bonuses", () => ReadBonuses(company, snapshot));
            Guard("projects", () => ReadProjects(company, snapshot));
            ReadExplain(company, snapshot);
            return snapshot;
        }

        // ---- the parts ----

        private static CompanyFacts ReadFacts(GameCompany company)
        {
            bool tcms = false;
            bool unlucky = false;
            try
            {
                tcms = GameCompanyHelper.IsTCMS(company);
                unlucky = GameCompanyHelper.IsUnluckyCompany(company);
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "modifiers: TCMS and the lack of patents cannot be read (" + e.GetType().Name + ")");
            }
            return new CompanyFacts(company.IsIdmCompany, company.IsFoundry, company.IsFabless, tcms, unlucky);
        }

        private static void ReadResearch(GameCompany company, ModifierSnapshot snapshot)
        {
            GameResearchSector sector = company.ResearchSector;
            if (sector == null) return;

            snapshot.Funding = sector.Funding;
            snapshot.SpeedTotal = GameResearchMath.ResearchSpeed(sector.Funding, company);

            GameTechnology current = sector.CurrentResearch();
            if (current == null) return;

            snapshot.CurrentTechName = NameOf(current);
            snapshot.CurrentTechMonthlyCost = current.Cost;
        }

        private static void ReadTotals(GameCompany company, ModifierSnapshot snapshot)
        {
            for (int type = 0; type < ModifierSnapshot.TypeCount; type++)
                snapshot.TotalByType[type] = GameModifiers.GetModifier((GameModifierType)type, company);
        }

        private static void ReadBonuses(GameCompany company, ModifierSnapshot snapshot)
        {
            var modifiers = company.ResearchModifiers;
            if (modifiers == null) return;

            snapshot.FrequencyIncrease = modifiers.FrequencyIncrease;
            snapshot.ConsumptionReduction = modifiers.ConsumptionReduction;
            snapshot.DensityIncrease = modifiers.DensityIncrease;
        }

        // The research projects of this company in progress: a Project with a research payload (Project.GetResearchProject() is null for the others).
        private static void ReadProjects(GameCompany company, ModifierSnapshot snapshot)
        {
            GameTimerScheduler scheduler = GameTimerScheduler.Instance;
            if (scheduler == null) return;

            List<GameProject> projects = scheduler.GetTimedObjectsOfType<GameProject>();
            foreach (GameProject project in projects)
            {
                if (project == null || !ReferenceEquals(project.Company, company)) continue;

                var payload = project.GetResearchProject();
                if (payload == null) continue;

                // Project.EstimatedTime is worked out on the project's own tick; the days left are Time * (1 - Progress), the same thing for a loaded project too.
                int eta = project.IsPaused ? -1 : (int)Math.Ceiling(project.Time * (1.0 - project.Progress));
                snapshot.Pending.Add(new PendingProject
                {
                    Frequency = payload.FrequencyIncrease,
                    Consumption = payload.ConsumptionReduction,
                    Density = payload.DensityIncrease,
                    EtaDays = eta,
                    IsPaused = project.IsPaused,
                });
            }
        }

        // ModModifiers.Explain for the seven channels with the player as the company (FR-5 item 7); a channel that throws, or is not declared, stays null.
        private static void ReadExplain(GameCompany company, ModifierSnapshot snapshot)
        {
            DateTime today = ModTicks.Today;
            for (int type = 0; type < Channels.Length; type++)
            {
                try
                {
                    ModifierBreakdown breakdown = ModModifiers.Explain(Channels[type], ModifierContext.For(company));
                    snapshot.ExplainByType[type] = breakdown == null || !breakdown.IsDeclared ? null : Copy(breakdown, today);
                }
                catch (Exception e)
                {
                    snapshot.ExplainByType[type] = null;
                    ModLog.Debug(ResearchTreeUxFeature.FeatureId, "modifiers: Explain failed on " + Channels[type] + " (" + e.GetType().Name + ")");
                }
            }
        }

        private static ExplainData Copy(ModifierBreakdown breakdown, DateTime today)
        {
            var data = new ExplainData
            {
                Raw = breakdown.Raw,
                Total = breakdown.Total,
                IsBalanceCapped = breakdown.IsBalanceCapped,
                IsFuseCapped = breakdown.IsFuseCapped,
            };

            foreach (AppliedModifier applied in breakdown)
            {
                int? days = null;
                if (applied.ExpiresOn.HasValue) days = Math.Max(0, (int)Math.Ceiling((applied.ExpiresOn.Value - today).TotalDays));

                data.Rows.Add(new ExplainRow
                {
                    FeatureId = applied.FeatureId,
                    ReasonKey = applied.ReasonKey,
                    ReasonArgs = applied.ReasonArgs,
                    Multiplier = applied.Multiplier,
                    ExpiresInDays = days,
                });
            }
            return data;
        }

        // ---- small helpers ----

        private static void Guard(string part, Action read)
        {
            try
            {
                read();
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "modifiers: cannot read " + part + " (" + e.GetType().Name + ")");
            }
        }

        private static string NameOf(GameTechnology technology)
        {
            try
            {
                string name = technology.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
                // The hardware of the technology is set in its Start; before that the name cannot be read.
            }
            return "#" + technology.ID;
        }
    }
}
