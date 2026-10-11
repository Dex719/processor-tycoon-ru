using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Features.ScenariosAchievements.UI;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The briefing and the result of a scenario as letters of the game's mail and as windows (TSK-023; FR-4 item 2, FR-9 item 5, AC-4.1).
    /// <list type="bullet">
    /// <item><b>The briefing</b>: when a new game starts with a scenario (<see cref="RunDirector.RunReady"/> with <c>NewGame</c>), a letter goes out through <c>ModMail.Send</c> -
    /// <c>Critical</c>, tag <c>scn:briefing:&lt;runId&gt;</c>, the title "Briefing: &lt;scenario&gt;" - and the window of the briefing opens (<see cref="BriefingWindow"/>). The id the core
    /// returned is kept in the run (<see cref="RunState.BriefingMailId"/>), which is part of the save section; the feature reserves no block of letter ids. A loaded game gets no
    /// briefing again.</item>
    /// <item><b>The result</b>: when the scenario is over and its window may open (<see cref="RunDirector.ResultReady"/>; after <c>BatchEnded</c> when it was decided inside a
    /// month's batch run), a <c>Normal</c> letter with the tag <c>scn:result:&lt;runId&gt;</c> carries the outcome, the reason, the score, the medal and the goals, so the
    /// player can read them again; its id is <see cref="RunState.ResultMailId"/>. One letter per run: a second call finds the first by its tag.</item>
    /// <item>The words are made by the pure <see cref="BriefingBuilder"/> and <see cref="ResultBuilder"/>; the numbers are written by the formats of the game
    /// (<c>StringFormatter.FloatToMoney</c> and the like, through <see cref="GameUiTexts"/>). The texts are table keys with already formatted arguments, so the core draws
    /// the frame of a letter again in the language of the moment; the arguments keep the language of the day the letter was sent.</item>
    /// </list>
    /// Every entry runs in <c>ModHooks.Guard</c>; a letter the core refuses (<c>-1</c>) is a line in the log, never an exception. Main thread only.
    /// </summary>
    internal static class ScenarioMailer
    {
        private const string SitePrefix = "ScenarioMailer.";

        /// <summary>The tag of the briefing letter of a run.</summary>
        internal const string BriefingTagPrefix = "scn:briefing:";

        /// <summary>The tag of the result letter of a run.</summary>
        internal const string ResultTagPrefix = "scn:result:";

        private static bool _installed;

        /// <summary>Listens to the start of a run and to the end of a scenario (once per session).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RunDirector.RunReady += OnRunReady;
            RunDirector.ResultReady += OnResultReady;
        }

        private static void OnRunReady(RunState run, GameReadyReason reason)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "RunReady", () =>
            {
                // only a game that has just started gets a briefing; a loaded one has had it
                if (reason != GameReadyReason.NewGame || run == null || !run.HasScenario) return;

                SendBriefing(run);
                BriefingWindow.Open(run);
            });
        }

        private static void OnResultReady(ScenarioResult result)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "ResultReady", () =>
            {
                RunDirector director = RunDirector.Instance;
                RunState run = director == null ? null : director.Run;
                if (result == null || run == null || !run.HasScenario) return;

                SendResult(run, result);
            });
        }

        // ---- the letters ----

        /// <summary>Sends the letter with the briefing of the run's scenario; false when it was sent already or the core did not take it.</summary>
        internal static bool SendBriefing(RunState run)
        {
            if (run == null || !run.HasScenario || run.BriefingMailId > 0) return false;

            string tag = BriefingTagPrefix + run.RunId;
            if (ModMail.Find(ScenariosFeature.FeatureId, tag) != null) return false;

            BriefingView view = BriefingBuilder.Build(run.Scenario, run.Challenges, ScenarioCatalog.FindChallenge, run, GameUiTexts.Instance, GameUiTexts.FeatureName);
            int id = ModMail.Send(new ModMailMessage
            {
                FeatureId = ScenariosFeature.FeatureId,
                Tag = tag,
                TitleKey = "scn.briefing.title",
                BodyKey = "scn.mail.briefing.body",
                Args = BriefingBuilder.MailArgs(view),
                Priority = MailPriority.Critical,
            });

            if (id < 0)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "mail: the core did not take the briefing letter of the run " + run.RunId);
                return false;
            }
            run.BriefingMailId = id;
            ModLog.Debug(ScenariosFeature.FeatureId, "mail: the briefing of '" + run.ScenarioId + "' is letter " + id);
            return true;
        }

        /// <summary>Sends the letter with the result of the run's scenario; false when it was sent already or the core did not take it.</summary>
        internal static bool SendResult(RunState run, ScenarioResult result)
        {
            if (run == null || result == null || !run.HasScenario || run.ResultMailId > 0) return false;

            string tag = ResultTagPrefix + run.RunId;
            if (ModMail.Find(ScenariosFeature.FeatureId, tag) != null) return false;

            IUiTexts texts = GameUiTexts.Instance;
            ResultView view = ResultBuilder.Build(run, result, ScenarioCatalog.FindAchievement, ScenarioCatalog.FindChallenge, texts, GameUiTexts.FeatureName);
            int id = ModMail.Send(new ModMailMessage
            {
                FeatureId = ScenariosFeature.FeatureId,
                Tag = tag,
                TitleKey = "scn.result.mailSubject",
                BodyKey = "scn.mail.result.body",
                Args = ResultBuilder.MailArgs(view, texts),
                Priority = MailPriority.Normal,
            });

            if (id < 0)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "mail: the core did not take the result letter of the run " + run.RunId);
                return false;
            }
            run.ResultMailId = id;
            ModLog.Debug(ScenariosFeature.FeatureId, "mail: the result of '" + run.ScenarioId + "' is letter " + id);
            return true;
        }
    }
}
