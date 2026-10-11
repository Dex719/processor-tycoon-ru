using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PressReviews
{
    /// <summary>One patch of the feature as the registry of the core reports it: the game method it targets and whether it is in the game.</summary>
    public struct PatchLine
    {
        /// <summary><c>Class.Method</c> of the target, as <c>ModHooks.Report</c> names it.</summary>
        public string Target;

        /// <summary>The patch is applied (not declared only, not failed, not rejected).</summary>
        public bool Applied;

        /// <summary>The feature starts without it when it cannot be applied.</summary>
        public bool Optional;
    }

    /// <summary>
    /// The result of the self-check of the feature at start (design.md "Самопроверка на старте", NFR-1, NFR-2, TSK-019): what was counted, whether the
    /// parts the feature stands on are there, and the problems found. <see cref="Summary"/> is the line of the log:
    /// <c>self-check OK (0 patches, 6 outlets, 12 notes, channels OK, reputation OK)</c>. A problem is a part that is wrong (a patch on a method the feature must
    /// not touch, a channel that is not declared); a warning is a part that is missing but optional or only reduces the feature (an optional patch that is
    /// not in the game). The feature runs either way: the self-check only says what is the matter.
    /// </summary>
    public sealed class SelfCheckReport
    {
        /// <summary>How many patches of the feature are applied (1 with the market column on, otherwise 0).</summary>
        public int Patches;

        public int Outlets;
        public int Notes;

        /// <summary>The channel <c>popularity.gain</c> is declared and has the provider, and the core's events and patches it needs are in.</summary>
        public bool ChannelsOk = true;

        /// <summary><c>ModReputation</c> answers with a reputation in range.</summary>
        public bool ReputationOk = true;

        /// <summary>What is wrong, in English, one line each.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>What is missing but does not stop anything, in English, one line each.</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Nothing is wrong (warnings do not count).</summary>
        public bool IsOk
        {
            get { return Problems.Count == 0 && ChannelsOk && ReputationOk; }
        }

        public void Fail(string problem)
        {
            if (!string.IsNullOrEmpty(problem)) Problems.Add(problem);
        }

        public void Warn(string warning)
        {
            if (!string.IsNullOrEmpty(warning)) Warnings.Add(warning);
        }

        /// <summary><c>self-check OK (1 patch, 6 outlets, 12 notes, channels OK, reputation OK)</c>; <c>FAILED</c> and <c>channels FAILED</c> where it is not.</summary>
        public string Summary()
        {
            var sb = new StringBuilder("self-check ");
            sb.Append(IsOk ? "OK" : "FAILED").Append(" (");
            sb.Append(Count(Patches, "patch", "patches")).Append(", ");
            sb.Append(Count(Outlets, "outlet", "outlets")).Append(", ");
            sb.Append(Count(Notes, "note", "notes")).Append(", ");
            sb.Append("channels ").Append(ChannelsOk ? "OK" : "FAILED").Append(", ");
            sb.Append("reputation ").Append(ReputationOk ? "OK" : "FAILED").Append(')');
            return sb.ToString();
        }

        private static string Count(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }
    }

    /// <summary>
    /// The rules of the self-check that need no game: which patches the feature may have, whether the content is usable, whether the identifiers follow the
    /// naming rules. The game side (<c>Diagnostics/PressSelfCheck</c>) reads the registries of the core and the game and hands the plain facts in.
    /// </summary>
    public static class SelfCheckRules
    {
        /// <summary>
        /// The methods of the game that the feature must never patch (design.md "Патчи Harmony"): the releases come from <c>ModEvents.CpuReleased</c> (patches of
        /// the core on <c>AddCpu</c>) and the formulas of the market from the channels of the core (<c>popularity.gain</c> on <c>UpdatePopularities</c>).
        /// </summary>
        public static readonly string[] ForbiddenTargets =
        {
            "Company.AddCpu", "AICompany.AddCpu", "Market.UpdatePopularities", "Market.CalculateRatings",
        };

        /// <summary>The only method the feature may patch, and only for the optional market column (FR-13).</summary>
        public const string MarketColumnTarget = "HardwareWindow.ShowMarket";

        /// <summary>The target is a method the feature must not patch.</summary>
        public static bool IsForbidden(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            for (int i = 0; i < ForbiddenTargets.Length; i++)
            {
                if (string.Equals(ForbiddenTargets[i], target, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Counts the applied patches and judges them: none on a forbidden method; the market column's patch only when the column was switched on at start
        /// (<paramref name="columnRegistered"/>) and then it is expected - an optional patch that did not apply is a warning (the column does not show), any
        /// other missing patch is a problem.
        /// </summary>
        public static void CheckPatches(IList<PatchLine> own, bool columnRegistered, SelfCheckReport report)
        {
            if (report == null) throw new ArgumentNullException("report");

            int declared = own == null ? 0 : own.Count;
            bool column = false;
            for (int i = 0; i < declared; i++)
            {
                PatchLine line = own[i];
                if (IsForbidden(line.Target))
                {
                    report.Fail("the feature patches " + line.Target + ", which belongs to the core (use the events and the channels of the core)");
                    continue;
                }

                bool isColumn = string.Equals(line.Target, MarketColumnTarget, StringComparison.Ordinal);
                if (isColumn) column = true;
                if (!isColumn && !line.Optional) report.Fail("the patch on " + line.Target + " is not part of the design");
                if (line.Applied) report.Patches++;
                else if (line.Optional) report.Warn("the optional patch on " + line.Target + " is not applied (the market column will not show)");
                else report.Fail("the patch on " + line.Target + " is not applied");
            }

            if (columnRegistered && !column) report.Fail("the market column is on but its patch on " + MarketColumnTarget + " is not declared");
            if (!columnRegistered && column) report.Fail("the patch on " + MarketColumnTarget + " is declared though the market column is off");
        }

        /// <summary>The content loaded: at least one outlet, no errors in the files, every year and segment covered by an outlet (design.md, FR-3).</summary>
        public static void CheckContent(int outlets, int notes, int errors, int coverageHoles, SelfCheckReport report)
        {
            if (report == null) throw new ArgumentNullException("report");

            report.Outlets = outlets;
            report.Notes = notes;
            if (outlets < 1) report.Fail("no outlet is loaded");
            if (errors > 0) report.Fail("the content files have " + errors + " error(s), see the log of the start");
            if (coverageHoles > 0) report.Fail(coverageHoles + " year/segment pair(s) 1971-2030 have no outlet");
            if (notes == 0) report.Warn("no note is loaded, there will be no quarterly notes");
        }

        /// <summary>Every tag of a letter of the feature follows the rule <c>press:&lt;kind&gt;:&lt;key&gt;</c> of the table of identifiers.</summary>
        public static void CheckTags(IEnumerable<string> tags, Func<string, bool> allowed, SelfCheckReport report)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (tags == null || allowed == null) return;

            foreach (string tag in tags)
            {
                if (!allowed(tag)) report.Fail("the tag '" + tag + "' of a letter does not follow the table of identifiers");
            }
        }
    }
}
