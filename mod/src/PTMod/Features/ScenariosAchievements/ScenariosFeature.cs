using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Features.ScenariosAchievements.UI;

namespace PTMod.Features.ScenariosAchievements
{
    /// <summary>
    /// The entry point of <c>scenarios-achievements</c> (I18; design.md, "ScenariosFeature"): scenarios with goals, challenges and achievements
    /// kept in a global profile. The core finds the class by <c>[ModFeature]</c> and the namespace
    /// <c>PTMod.Features.ScenariosAchievements</c>, which is how it knows which feature a handler or an exception belongs to.
    /// <para>
    /// There are no hard dependencies: <c>first-year-onboarding</c> and <c>rival-acquisitions</c> are soft links the later tasks reach through
    /// their <c>Api</c> folders and only while those features are active. The feature switches on and off in a running game
    /// (<see cref="SupportsHotToggle"/>): its patches and handlers go with the core's state, and the state that must stop by itself (windows,
    /// checks) listens to <see cref="Disabled"/>.
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class ScenariosFeature : IModFeature
    {
        /// <summary>The slug of the spec; the key of the feature's sections in the save and the profile and the owner of its content folder.</summary>
        public const string FeatureId = "scenarios-achievements";

        /// <summary>The feature is Enabled (between <see cref="OnEnabled"/> and <see cref="OnDisabled"/>).</summary>
        internal static bool IsActive { get; private set; }

        /// <summary>
        /// Raised at the end of <see cref="OnEnabled"/>: reopen what belongs to a run that is going (the tracker); the modifier entries of the run
        /// are not created again, the core kept them. A throwing subscriber does not stop the others.
        /// </summary>
        internal static event Action Enabled;

        /// <summary>
        /// Raised in <see cref="OnDisabled"/>: close the windows, stop the checks, mark a run that is going as assisted. Not raised through
        /// <c>ModHooks.Raise</c>, which skips the handlers of a feature that is no longer Enabled.
        /// </summary>
        internal static event Action Disabled;

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "scn.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "scn.feature.desc"; }
        }

        /// <summary>None: the links to the other features are soft.</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        public bool SupportsHotToggle
        {
            get { return true; }
        }

        /// <summary>
        /// Declares the parameters, the settings page, the holder of the save section (the run in play; the profile is read through <c>ModProfile</c> when it is needed),
        /// the two patches of the feature and everything that listens to the game: the director of the run (ticks, <c>GameReady</c>, <c>BatchEnded</c>), the events of the
        /// game, the start of a new game, the section of the new-game window, the watch over the features that are switched off, the windows and the pop-ups (challenges,
        /// achievements, goal tracker, briefing and letters, results, career summary, notifications). The links are made one by one: one that fails is
        /// logged and counted against the feature, and the others are still made.
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            foreach (ParamSpec spec in ScenarioParamSpecs.All()) ctx.Param(spec);
            ctx.HubPage("scn.hub.title", ScenariosHubPage.Build);
            ctx.SaveParticipant(RunSaveParticipant.Instance);

            // the two patches of the feature (design.md, "Патчи Harmony"); everything else the game offers comes through the services of the core
            Guarded("patch: OEM contracts", () => ctx.Patch(typeof(ScenarioPatches)));
            Guarded("patch: difficulty", () => ctx.Patch(typeof(DifficultyGuard)));

            Guarded("director", () => RunDirector.Install(ctx));
            Guarded("game events", GameEventHooks.Install);
            Guarded("start", ScenarioStarter.Install);
            Guarded("setup section", ScenarioSetupSection.Install);
            Guarded("feature watch", FeatureWatch.Install);

            // the windows and the pop-ups (TSK-022 .. TSK-027); the tracker is installed before the briefing so that the briefing, which is modal, opens last and stays on top
            Guarded("challenges window", ChallengesWindow.Install);
            Guarded("achievements window", AchievementsWindow.Install);
            Guarded("goal tracker", () => GoalTrackerPanel.Install(ctx));
            Guarded("briefing and letters", ScenarioMailer.Install);
            Guarded("results window", ScorecardWindow.Install);
            Guarded("career summary", CareerSummaryWindow.Install);
            Guarded("pop-ups", AchievementToastQueue.Install);
        }

        public void OnEnabled()
        {
            try
            {
                IsActive = true;
                ScenarioCatalog.EnsureLoaded();
                ModLog.Info(FeatureId, "enabled: " + ScenarioCatalog.Scenarios.Count + " scenario(s), "
                                       + ScenarioCatalog.Challenges.Count + " challenge(s), " + ScenarioCatalog.Achievements.Count + " achievement(s)");

                // TSK-029: the patches are applied, the catalog is loaded and the tables are complete; one line in the log, "self-check: OK" or the list of problems
                ScenariosSelfCheck.RunOnce();
                Raise(Enabled, "OnEnabled");
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, "ScenariosFeature.OnEnabled", ex);
            }
        }

        public void OnDisabled()
        {
            try
            {
                IsActive = false;
                ModLog.Info(FeatureId, "disabled: windows closed, checks stopped");
                Raise(Disabled, "OnDisabled");
            }
            catch (Exception ex)
            {
                // The state is already off; whatever else went wrong is logged, never thrown into the core.
                ModLog.Error(FeatureId, "disabling failed", ex);
            }
        }

        /// <summary>One link of <see cref="Register"/>: a failure is logged and counted against the feature, and the next link is still made.</summary>
        private static void Guarded(string what, Action link)
        {
            try
            {
                link();
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "register: " + what + " failed", ex);
                ModHooks.ReportFault(FeatureId, "ScenariosFeature.Register/" + what, ex);
            }
        }

        /// <summary>Calls the subscribers one by one: a failing one is reported and the rest still run.</summary>
        private static void Raise(Action handlers, string site)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    ModLog.Error(FeatureId, "a subscriber of " + site + " failed", ex);
                }
            }
        }
    }
}
