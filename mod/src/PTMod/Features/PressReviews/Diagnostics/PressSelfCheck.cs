using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Ids;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The self-check of the feature at the start of a game (design.md "Самопроверка на старте", NFR-2, TSK-019): it looks at what the feature stands on and writes
    /// one line to the log, <c>[PTMod] [press-reviews] INFO self-check OK (0 patches, 6 outlets, 12 notes, channels OK, reputation OK)</c>, with a warning line
    /// for each problem. The facts it checks:
    /// <list type="bullet">
    /// <item><b>Patches.</b> The feature has none, except the optional <c>HardwareWindow.ShowMarket</c> for the market column when that was switched on at the start
    /// (<see cref="SelfCheckRules.CheckPatches"/>); in particular none on <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c>, whose releases come from the core's event.</item>
    /// <item><b>Content.</b> At least one outlet, no errors in the content files, every year 1971-2030 and every segment covered by an outlet (the stand-in counted).</item>
    /// <item><b>Channels.</b> The channel <c>popularity.gain</c> is declared and holds the provider of the feature, the core's patch on <c>Market.UpdatePopularities</c>
    /// is applied (without it the reputation and the hype change nothing) and so are the core's patches on <c>AddCpu</c> that raise <c>ModEvents.CpuReleased</c>.</item>
    /// <item><b>Reputation.</b> <c>ModReputation</c> answers with a number in range.</item>
    /// <item><b>Identifiers.</b> The tags of the feature's letters follow the table of identifiers of the core (<c>press:…</c>), its strings are in the tables.</item>
    /// </list>
    /// The feature does not stop on a problem: the part that is wrong is named in the log and the rest of the feature goes on (reviews, letters and the archive work
    /// without the channel). It runs on <c>ModTicks.GameReady</c> (when the registries of the core are complete) and when the feature is switched on later in the scene.
    /// The decisions are <see cref="SelfCheckRules"/>' (pure, with the tests); this class reads the registries.
    /// </summary>
    internal static class PressSelfCheck
    {
        private const string Feature = PressReviewsFeature.FeatureId;
        private static readonly string[] CoreAddCpuTargets = { "Company.AddCpu", "AICompany.AddCpu" };
        private static readonly string[] SampleKeys = { "press.feature.name", "press.mail.review.subject", "press.awards.subject", "press.mail.digest.subject" };

        /// <summary>The result of the last run; null before the first one.</summary>
        internal static SelfCheckReport Last { get; private set; }

        /// <summary><c>ModTicks.GameReady</c>: the game is ready, the registries are complete.</summary>
        internal static void OnGameReady(GameReadyReason reason)
        {
            Run();
        }

        /// <summary>Runs the check, writes the result to the log and returns it. Never throws.</summary>
        internal static SelfCheckReport Run()
        {
            var report = new SelfCheckReport();
            try
            {
                CheckPatches(report);
                CheckContent(report);
                CheckChannels(report);
                CheckReputation(report);
                CheckIdentifiers(report);
            }
            catch (Exception ex)
            {
                report.Fail("the check itself failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            Last = report;
            Write(report);
            return report;
        }

        private static void Write(SelfCheckReport report)
        {
            if (report.IsOk) ModLog.Info(Feature, report.Summary());
            else ModLog.Warn(Feature, report.Summary());

            foreach (string problem in report.Problems) ModLog.Warn(Feature, "self-check: " + problem);
            foreach (string warning in report.Warnings) ModLog.Warn(Feature, "self-check: " + warning);
        }

        // ---- the parts ----

        private static void CheckPatches(SelfCheckReport report)
        {
            var own = new List<PatchLine>();
            foreach (PatchReport patch in ModHooks.Report)
            {
                if (patch.FeatureId != Feature) continue;
                own.Add(new PatchLine { Target = patch.Target, Applied = patch.Status == PatchStatus.Applied, Optional = patch.Optional });
            }
            SelfCheckRules.CheckPatches(own, PressReviewsFeature.MarketColumnRegistered, report);
        }

        private static void CheckContent(SelfCheckReport report)
        {
            PressContent content = PressContent.Current;
            SelfCheckRules.CheckContent(content.Outlets.Count, content.Notes.Count, content.Report.Errors.Count, content.Report.CoverageHoles.Count, report);
        }

        private static void CheckChannels(SelfCheckReport report)
        {
            bool ok = true;

            if (!ModModifiers.IsDeclared(ModChannels.PopularityGain))
            {
                report.Fail("the channel " + ModChannels.PopularityGain + " is not declared by the core, the reputation and the hype change nothing");
                ok = false;
            }
            else if (!ModModifiers.HasAny(ModChannels.PopularityGain))
            {
                report.Fail("nothing is registered on the channel " + ModChannels.PopularityGain + ", the provider of the feature is missing");
                ok = false;
            }

            if (!CorePatchApplied("Market.UpdatePopularities"))
            {
                report.Fail("the patch of the core on Market.UpdatePopularities is not applied, the reputation and the hype do not change popularity");
                ok = false;
            }

            foreach (string target in CoreAddCpuTargets)
            {
                if (CorePatchApplied(target)) continue;

                report.Fail("the patch of the core on " + target + " is not applied, ModEvents.CpuReleased will not be raised for its CPUs and they get no review");
                ok = false;
            }

            report.ChannelsOk = ok;
        }

        private static void CheckReputation(SelfCheckReport report)
        {
            try
            {
                float value = ModReputation.Get(ModCompanies.PlayerKey);
                if (float.IsNaN(value) || value < ModReputation.Min || value > ModReputation.Max)
                {
                    report.Fail("ModReputation gave " + value + " for the player, which is outside " + ModReputation.Min + "..." + ModReputation.Max);
                    report.ReputationOk = false;
                }
            }
            catch (Exception ex)
            {
                report.Fail("ModReputation is not available: " + ex.GetType().Name + ": " + ex.Message);
                report.ReputationOk = false;
            }
        }

        private static void CheckIdentifiers(SelfCheckReport report)
        {
            var tags = new List<string>
            {
                ReviewMailText.TagOf(1),
                AwardsMailText.TagOf(1990),
                NotesMailText.TagOf(new DateTime(1990, 1, 1)),
                DigestMailText.TagOf(new DateTime(1990, 1, 1)),
                ArticleLog.TagOf(1),
            };
            SelfCheckRules.CheckTags(tags, tag => ModIds.IsTagAllowed(Feature, tag), report);

            foreach (string key in SampleKeys)
            {
                if (!ModL10n.Has(key)) report.Warn("the string '" + key + "' is not in the tables of the language (the strings of the feature are not loaded?)");
            }
        }

        /// <summary>The core declared the patch on <paramref name="target"/> (<c>Class.Method</c>) and it is in the game.</summary>
        private static bool CorePatchApplied(string target)
        {
            foreach (PatchReport patch in ModHooks.Report)
            {
                if (patch.FeatureId == ModHooks.CoreId && patch.Target == target && patch.Status == PatchStatus.Applied) return true;
            }
            return false;
        }
    }
}
