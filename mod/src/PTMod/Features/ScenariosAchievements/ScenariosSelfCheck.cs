using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements
{
    /// <summary>
    /// The feature's contribution to the self-check of the mod at the start (TSK-029; NFR-2, NFR-5; design.md "Самопроверка на старте"). The core's <c>ModSelfCheck</c> has no point where
    /// a feature adds its own findings (see <c>mod/requests/scenarios-achievements.md</c>), so the feature checks itself and writes to <c>Player.log</c> under its own id:
    /// <c>[PTMod] [scenarios-achievements] INFO self-check: OK</c> when all is well, otherwise <c>self-check: 1 error(s), 2 warning(s)</c> and a line per finding. What is looked at:
    /// <list type="bullet">
    /// <item>the two patches of the feature (<c>ContractManager.PromoteContractToActive</c>, <c>GameplaySettingsWindow.ChangeDifficulty</c>) in <c>ModHooks.Report</c>: declared and in the game, and no
    /// patch on a method the services of the core took;</item>
    /// <item>the members of the game the feature reaches by name (<see cref="ScenariosSelfCheckRules.Members"/>): the four fields of <c>MenuGameSetup</c>, <c>GameplaySettingsWindow.difficultySettings</c>,
    /// <c>GameManager.QuitToMenu</c> and the two events of the scene, by reflection (<see cref="GameMemberProbe"/>);</item>
    /// <item>the content: the catalog is loaded and holds records, nothing of the built-in files was rejected (<c>ModContent.ReportOf</c>);</item>
    /// <item>the modifier channels the records write to (<c>ModModifiers.IsDeclared</c>);</item>
    /// <item>the keys <c>scn.*</c>, <c>chl.*</c>, <c>ach.*</c> in the Russian table (an error) and the English one (a warning): <see cref="ScenarioTextKeys.Required"/>.</item>
    /// </list>
    /// The rules and the wording are <see cref="ScenariosSelfCheckRules"/>' (pure, with the tests); this class collects the facts and writes the log. It runs once per session, in
    /// <c>OnEnabled</c>: the patches of the feature are applied by then, the catalog is loaded and the tables of the core are complete. A feature whose patches could not be applied is made
    /// Unavailable by the core and never gets <c>OnEnabled</c>; for that case <c>FeatureWatch</c> runs the check when the core announces the state, so the log names the patch all the same. A
    /// finding changes nothing in the feature: every missing part already has its way out. Every group of questions is asked inside its own try/catch, so a question that cannot be asked is a warning in the log and not an exception.
    /// </summary>
    internal static class ScenariosSelfCheck
    {
        private const string FeatureId = ScenariosFeature.FeatureId;

        private static bool _ran;

        /// <summary>The verdict of the last run; null before the first one.</summary>
        internal static SelfCheckVerdict Last { get; private set; }

        /// <summary>Runs the check the first time it is asked for and never again in the session (a feature that is switched off and on does not repeat it). Never throws.</summary>
        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;
            Run();
        }

        /// <summary>Runs the check now, writes the result to the log and returns it. Never throws.</summary>
        internal static SelfCheckVerdict Run()
        {
            SelfCheckVerdict verdict;
            try
            {
                verdict = ScenariosSelfCheckRules.Evaluate(Collect());
            }
            catch (Exception ex)
            {
                var finding = new SelfCheckFinding
                {
                    Level = SelfCheckLevel.Error,
                    Area = ScenariosSelfCheckRules.AreaCheck,
                    Message = "the self-check itself failed (" + ex.GetType().Name + ": " + ex.Message + ")",
                };
                verdict = new SelfCheckVerdict(new[] { finding }, string.Empty);
            }

            Last = verdict;
            try
            {
                Write(verdict);
            }
            catch (Exception)
            {
                // the log must never be the reason the feature fails to start
            }
            return verdict;
        }

        private static void Write(SelfCheckVerdict verdict)
        {
            if (verdict.IsOk)
            {
                ModLog.Info(FeatureId, verdict.Summary);
                ModLog.Debug(FeatureId, "self-check: " + verdict.Counts);
                return;
            }

            if (verdict.Errors > 0) ModLog.Error(FeatureId, verdict.Summary);
            else ModLog.Warn(FeatureId, verdict.Summary);

            foreach (SelfCheckFinding finding in verdict.Findings)
            {
                if (finding.Level == SelfCheckLevel.Error) ModLog.Error(FeatureId, "self-check: " + finding);
                else ModLog.Warn(FeatureId, "self-check: " + finding);
            }
            ModLog.Debug(FeatureId, "self-check: " + verdict.Counts);
        }

        // ---- the facts ----

        private static SelfCheckFacts Collect()
        {
            var facts = new SelfCheckFacts();
            Ask(facts, "the patches", () => CollectPatches(facts));
            Ask(facts, "the members of the game", () => CollectMembers(facts));
            Ask(facts, "the content", () => CollectContent(facts));
            Ask(facts, "the modifier channels", () => CollectChannels(facts));
            Ask(facts, "the strings", () => CollectStrings(facts));
            return facts;
        }

        private static void Ask(SelfCheckFacts facts, string area, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                facts.NotLooked.Add(area + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void CollectPatches(SelfCheckFacts facts)
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            if (reports == null) return;

            foreach (PatchReport report in reports)
            {
                if (report == null || !string.Equals(report.FeatureId, FeatureId, StringComparison.Ordinal)) continue;
                facts.Patches.Add(new PatchFact
                {
                    Target = report.Target,
                    Applied = report.Status == PatchStatus.Applied,
                    Optional = report.Optional,
                    Status = report.Status.ToString(),
                    Detail = report.Detail,
                });
            }
        }

        private static void CollectMembers(SelfCheckFacts facts)
        {
            var probe = new GameMemberProbe();
            foreach (GameMemberSpec spec in ScenariosSelfCheckRules.Members)
            {
                facts.Members.Add(new MemberFact { Spec = spec, Problem = probe.Probe(spec) });
            }
        }

        private static void CollectContent(SelfCheckFacts facts)
        {
            ScenarioCatalog.EnsureLoaded();
            facts.ContentLoaded = ScenarioCatalog.IsLoaded;
            facts.Scenarios = ScenarioCatalog.Scenarios.Count;
            facts.Challenges = ScenarioCatalog.Challenges.Count;
            facts.Achievements = ScenarioCatalog.Achievements.Count;

            AddFile(facts, ScenarioCatalog.ScenariosFile, facts.Scenarios);
            AddFile(facts, ScenarioCatalog.ChallengesFile, facts.Challenges);
            AddFile(facts, ScenarioCatalog.AchievementsFile, facts.Achievements);
        }

        private static void AddFile(SelfCheckFacts facts, string file, int loaded)
        {
            ContentReport report = ModContent.ReportOf(FeatureId, file);
            var fact = new ContentFileFact { File = file + ".json", Loaded = loaded };
            if (report != null)
            {
                fact.Skipped = report.Skipped;
                fact.OverrideFound = report.OverrideFound;
                fact.FirstError = report.Errors.Count > 0 ? report.Errors[0] : null;
            }
            facts.ContentFiles.Add(fact);
        }

        private static void CollectChannels(SelfCheckFacts facts)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioDefinition scenario in ScenarioCatalog.Scenarios)
            {
                AddChannels(facts, seen, scenario.Modifiers, "scenario '" + scenario.Id + "'");
            }
            foreach (ChallengeDefinition challenge in ScenarioCatalog.Challenges)
            {
                AddChannels(facts, seen, challenge.Modifiers, "challenge '" + challenge.Id + "'");
            }
        }

        private static void AddChannels(SelfCheckFacts facts, HashSet<string> seen, ModifierSpec[] modifiers, string owner)
        {
            if (modifiers == null) return;
            foreach (ModifierSpec modifier in modifiers)
            {
                if (modifier == null || string.IsNullOrEmpty(modifier.Channel) || !seen.Add(owner + "|" + modifier.Channel)) continue;
                facts.Channels.Add(new ChannelFact { Channel = modifier.Channel, UsedBy = owner, Declared = ModModifiers.IsDeclared(modifier.Channel) });
            }
        }

        private static void CollectStrings(SelfCheckFacts facts)
        {
            facts.RequiredKeys.AddRange(ScenarioTextKeys.Required(ScenarioCatalog.Scenarios, ScenarioCatalog.Challenges, ScenarioCatalog.Achievements));
            AddOwnKeys(facts.RussianKeys, ModL10n.Keys("ru"));
            AddOwnKeys(facts.EnglishKeys, ModL10n.Keys("en"));
        }

        private static void AddOwnKeys(HashSet<string> into, IReadOnlyList<string> keys)
        {
            if (keys == null) return;
            foreach (string key in keys)
            {
                if (ScenarioTextKeys.IsOwnKey(key)) into.Add(key);
            }
        }
    }
}
