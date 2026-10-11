using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>One AI company as the overview reads it from the game: its name and its state of architectures.</summary>
    public sealed class RivalInput
    {
        public int CompanyId;
        public string Name;
        public bool IsFoundry;
        public bool IsBankrupt;

        /// <summary>The state of architectures of the company; null for a company the seed has not reached.</summary>
        public CompanyArchState State;
    }

    /// <summary>One line of the table "Соперники" (FR-15 item 1): the company, the family it designs on, the family it researches and how far it is.</summary>
    public sealed class RivalRow
    {
        public int CompanyId;
        public string Name;

        /// <summary>The family the company designs on; null when it has none yet.</summary>
        public string ActiveId;

        /// <summary>The family in its lab; null when it researches none.</summary>
        public string ResearchId;

        /// <summary>The progress of that research, whole percent 0..100.</summary>
        public int ResearchPercent;

        /// <summary>The research stands still (no money, a technology was chosen).</summary>
        public bool Paused;
    }

    /// <summary>
    /// The table "Соперники" of the panel without the game (FR-15 item 1; design.md Component 13): the AI companies that design CPUs, sorted by name, each with its
    /// active architecture and its current research. What it shows is what the game's own inspector shows for any company, nothing more (the assumption of FR-15).
    /// Foundries and bankrupt companies are not listed (they design nothing); a company the seed has not reached is listed with dashes.
    /// </summary>
    public static class RivalsModel
    {
        /// <summary>The rows of the table, sorted by name (ignoring case), then by company id.</summary>
        public static List<RivalRow> Rows(IEnumerable<RivalInput> companies)
        {
            var rows = new List<RivalRow>();
            if (companies == null) return rows;

            foreach (RivalInput company in companies)
            {
                if (company == null || company.IsFoundry || company.IsBankrupt) continue;

                var row = new RivalRow { CompanyId = company.CompanyId, Name = company.Name ?? string.Empty };
                CompanyArchState state = company.State;
                if (state != null)
                {
                    row.ActiveId = state.Active;
                    row.ResearchId = state.CurrentResearch;
                    row.Paused = state.Paused;
                    if (state.CurrentResearch != null)
                    {
                        FamilyState family = state.GetFamily(state.CurrentResearch);
                        row.ResearchPercent = family == null ? 0 : Percent(family.Progress);
                    }
                }
                rows.Add(row);
            }

            rows.Sort((a, b) =>
            {
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.CompanyId.CompareTo(b.CompanyId);
            });
            return rows;
        }

        /// <summary>A progress 0..1 as a whole percent that is 100 only for a finished research.</summary>
        public static int Percent(float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f) return 0;
            if (progress >= 1f) return 100;
            return (int)Math.Floor(progress * 100f);
        }
    }
}
