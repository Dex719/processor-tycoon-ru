using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// JSON record to definition (design.md, "Контент"): the first half of loading a record, the part that is about the shape of the file.
    /// A missing required member, a member of the wrong type and a member the schema does not know are errors; the record is then rejected
    /// (null) and the reasons go to the list. What the values mean (ranges, white lists, cross-references) is <see cref="ContentValidator"/>'s
    /// business. The mapper also fills what is computed on load: <c>Source</c> (a copy of the record, the snapshot of the run) and
    /// <c>ContentHash</c>. Pure: no game, no Unity.
    /// </summary>
    public static class ContentMapper
    {
        private const string Disabled = "disabled";   // the member the loader of the core reads itself (FR-10 item 3)
        private const int MaxCheckDepth = 8;          // the validator allows less; this only keeps a hand-made file from nesting without end

        /// <summary>Maps a record of <c>scenarios.json</c>; null (with the reasons in <paramref name="errors"/>) when the record is unusable.</summary>
        public static ScenarioDefinition MapScenario(JsonObject json, List<string> errors)
        {
            if (!Begin(json, errors)) return null;
            int before = errors.Count;

            var r = new FieldReader(json, string.Empty, errors);
            var s = new ScenarioDefinition();
            s.Id = r.Str("id", true);
            s.Order = r.Int("order", false, 0);
            s.TitleKey = r.Str("titleKey", true);
            s.DescKey = r.Str("descKey", true);
            s.BriefingKey = r.Str("briefingKey", true);
            s.Start = MapStart(r.Obj("start", true), "start", errors);
            s.Onboarding = r.Str("onboarding", false) ?? "allowed";
            s.Modifiers = MapModifiers(r.Arr("modifiers", false), "modifiers", errors);
            s.Deadline = r.Str("deadline", true);
            s.TimeBonusMax = r.Int("timeBonusMax", false, 0);
            s.Goals = MapGoals(r.Arr("goals", true), "goals", errors);
            s.Fail = MapFails(r.Arr("fail", false), "fail", errors);
            s.Medals = MapMedals(r.Obj("medals", true), "medals", errors);
            s.AllowedChallenges = MapStrings(r.Arr("allowedChallenges", false), "allowedChallenges", errors);
            r.Finish(Disabled);

            if (errors.Count > before) return null;
            s.Source = (JsonObject)json.DeepClone();
            s.ContentHash = ContentHasher.Hash(json);
            return s;
        }

        /// <summary>Maps a record of <c>challenges.json</c>; null (with the reasons in <paramref name="errors"/>) when the record is unusable.</summary>
        public static ChallengeDefinition MapChallenge(JsonObject json, List<string> errors)
        {
            if (!Begin(json, errors)) return null;
            int before = errors.Count;

            var r = new FieldReader(json, string.Empty, errors);
            var c = new ChallengeDefinition();
            c.Id = r.Str("id", true);
            c.TitleKey = r.Str("titleKey", true);
            c.DescKey = r.Str("descKey", true);
            c.ScoreBonus = r.Float("scoreBonus", true, 0f);
            c.Modifiers = MapModifiers(r.Arr("modifiers", false), "modifiers", errors);
            c.Fail = MapFails(r.Arr("fail", false), "fail", errors);
            c.RequiresCompanyType = MapStrings(r.Arr("requiresCompanyType", false), "requiresCompanyType", errors);
            c.RequiresPerCompanyCredit = r.Bool("requiresPerCompanyCredit", false);
            r.Finish(Disabled);

            if (errors.Count > before) return null;
            c.Source = (JsonObject)json.DeepClone();
            c.ContentHash = ContentHasher.Hash(json);
            return c;
        }

        /// <summary>Maps a record of <c>achievements.json</c>; null (with the reasons in <paramref name="errors"/>) when the record is unusable.</summary>
        public static AchievementDefinition MapAchievement(JsonObject json, List<string> errors)
        {
            if (!Begin(json, errors)) return null;
            int before = errors.Count;

            var r = new FieldReader(json, string.Empty, errors);
            var a = new AchievementDefinition();
            a.Id = r.Str("id", true);
            a.Category = r.Str("category", true);
            a.Hidden = r.Bool("hidden", false);
            a.Points = r.Int("points", true, 0);
            a.TitleKey = r.Str("titleKey", true);
            a.DescKey = r.Str("descKey", true);
            a.Check = MapCheckAt(r.Obj("check", true), "check", errors, 0);
            a.CountsOnlyNewGames = r.Bool("countsOnlyNewGames", false);
            r.Finish(Disabled);

            if (errors.Count > before) return null;
            a.Source = (JsonObject)json.DeepClone();
            a.ContentHash = ContentHasher.Hash(json);
            return a;
        }

        /// <summary>Maps one check (a goal's, a fail rule's or an achievement's); null (with the reasons in <paramref name="errors"/>) when it is unusable.</summary>
        public static CheckSpec MapCheck(JsonObject json, string path, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            int before = errors.Count;
            CheckSpec check = MapCheckAt(json, path, errors, 0);
            return errors.Count > before ? null : check;
        }

        private static bool Begin(JsonObject json, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (json != null) return true;
            errors.Add("the record is not a JSON object");
            return false;
        }

        // ---- scenario parts ----

        private static ScenarioStart MapStart(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var start = new ScenarioStart();
            start.Year = r.Int("year", true, 0);
            start.CompanyType = r.Str("companyType", true);
            start.DifficultyLevel = r.Int("difficultyLevel", true, 0);
            start.StartingTechnology = r.Int("startingTechnology", true, 0);
            start.StartingFunds = r.Float("startingFunds", true, 0f);
            start.FactorySize = r.Int("factorySize", true, 0);
            start.AllowCheats = r.Bool("allowCheats", false);
            start.TechYear = r.Int("techYear", false, 0);
            r.Finish();
            return start;
        }

        private static GoalSpec[] MapGoals(JsonArray array, string path, List<string> errors)
        {
            if (array == null) return new GoalSpec[0];
            var list = new List<GoalSpec>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = path + "[" + i + "]";
                var json = array[i] as JsonObject;
                if (json == null)
                {
                    errors.Add(itemPath + ": expected an object");
                    continue;
                }

                var r = new FieldReader(json, itemPath, errors);
                var goal = new GoalSpec();
                goal.Id = r.Str("id", true);
                goal.Required = r.Bool("required", false);
                goal.Points = r.Int("points", false, 0);
                goal.TitleKey = r.Str("titleKey", true);
                goal.Check = MapCheckAt(r.Obj("check", true), itemPath + ".check", errors, 0);
                goal.By = r.Str("by", false);
                goal.Progress = MapProgress(r.Obj("progress", false), itemPath + ".progress", errors);
                r.Finish();
                list.Add(goal);
            }
            return list.ToArray();
        }

        private static ProgressSpec MapProgress(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var progress = new ProgressSpec();
            progress.Metric = r.Str("metric", true);
            progress.Target = r.Num("target", true, 0);
            progress.Format = r.Str("format", true);
            r.Finish();
            return progress;
        }

        private static FailSpec[] MapFails(JsonArray array, string path, List<string> errors)
        {
            if (array == null) return new FailSpec[0];
            var list = new List<FailSpec>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = path + "[" + i + "]";
                var json = array[i] as JsonObject;
                if (json == null)
                {
                    errors.Add(itemPath + ": expected an object");
                    continue;
                }

                var r = new FieldReader(json, itemPath, errors);
                var fail = new FailSpec();
                fail.Id = r.Str("id", true);
                fail.TextKey = r.Str("textKey", true);
                fail.Check = MapCheckAt(r.Obj("check", true), itemPath + ".check", errors, 0);
                r.Finish();
                list.Add(fail);
            }
            return list.ToArray();
        }

        private static ModifierSpec[] MapModifiers(JsonArray array, string path, List<string> errors)
        {
            if (array == null) return new ModifierSpec[0];
            var list = new List<ModifierSpec>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = path + "[" + i + "]";
                var json = array[i] as JsonObject;
                if (json == null)
                {
                    errors.Add(itemPath + ": expected an object");
                    continue;
                }

                var r = new FieldReader(json, itemPath, errors);
                var modifier = new ModifierSpec();
                modifier.Channel = r.Str("channel", true);
                modifier.Multiplier = r.Float("multiplier", true, 1f);
                modifier.AppliesTo = r.Str("appliesTo", false) ?? "player";
                r.Finish();
                list.Add(modifier);
            }
            return list.ToArray();
        }

        private static MedalThresholds MapMedals(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var medals = new MedalThresholds();
            medals.Bronze = r.Int("bronze", true, 0);
            medals.Silver = r.Int("silver", true, 0);
            medals.Gold = r.Int("gold", true, 0);
            r.Finish();
            return medals;
        }

        private static string[] MapStrings(JsonArray array, string path, List<string> errors)
        {
            if (array == null) return new string[0];
            var list = new List<string>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var value = array[i] as JsonValue;
                if (value == null || value.Kind != JsonKind.String)
                {
                    errors.Add(path + "[" + i + "]: expected a string");
                    continue;
                }
                list.Add(value.StringValue);
            }
            return list.ToArray();
        }

        // ---- checks ----

        private static CheckSpec MapCheckAt(JsonObject json, string path, List<string> errors, int depth)
        {
            if (json == null) return null;
            if (depth > MaxCheckDepth)
            {
                errors.Add(path + ": checks are nested too deep");
                return null;
            }

            var r = new FieldReader(json, path, errors);
            var check = new CheckSpec();
            check.Kind = r.Str("kind", true);

            bool isMetric = check.Kind == CheckSpec.KindMetric;
            check.Metric = r.Str("metric", isMetric);
            check.Op = r.Str("op", isMetric);
            check.Value = r.Num("value", isMetric, 0);
            check.HoldDays = r.Int("holdDays", false, 0);
            check.Cpu = MapCpu(r.Obj("cpu", check.Kind == CheckSpec.KindCpuReleased || check.Kind == CheckSpec.KindRace), path + ".cpu", errors);
            check.FailOnLose = r.Bool("failOnLose", false);
            check.Event = r.Str("event", check.Kind == CheckSpec.KindEvent);
            check.Count = r.Int("count", false, 1);

            JsonObject condition = r.Obj("condition", check.Kind == CheckSpec.KindCondition);
            if (condition != null) check.Condition = new ConditionRef { Node = condition.DeepClone() };

            JsonArray all = r.Arr("all", check.Kind == CheckSpec.KindAll);
            if (all != null)
            {
                var children = new List<CheckSpec>(all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    string childPath = path + ".all[" + i + "]";
                    var child = all[i] as JsonObject;
                    if (child == null)
                    {
                        errors.Add(childPath + ": expected an object");
                        continue;
                    }
                    CheckSpec mapped = MapCheckAt(child, childPath, errors, depth + 1);
                    if (mapped != null) children.Add(mapped);
                }
                check.All = children.ToArray();
            }

            check.Require = MapRequire(r.Obj("require", false), path + ".require", errors);
            check.Cadence = r.Str("cadence", false);
            check.Profile = MapProfile(r.Obj("profile", check.Kind == CheckSpec.KindProfile), path + ".profile", errors);
            r.Finish();

            if (check.Cadence == null) check.Cadence = DefaultCadence(check);
            return check;
        }

        private static CpuFilter MapCpu(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var cpu = new CpuFilter();
            cpu.MinFrequency = r.Float("minFrequency", false, 0f);
            cpu.MinCores = r.Int("minCores", false, 0);
            cpu.MaxPower = r.Float("maxPower", false, 0f);
            cpu.RequireSmt = r.Bool("requireSmt", false);
            cpu.CompanyTypeAtRelease = r.Str("companyTypeAtRelease", false);
            cpu.ReleasedBefore = r.Str("releasedBefore", false);
            r.Finish();
            return cpu;
        }

        private static RequireSpec MapRequire(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var require = new RequireSpec();
            require.DifficultyLevel = r.Int("difficultyLevel", false, -1);
            require.NotBankrupt = r.Bool("notBankrupt", false);
            require.CompanyType = r.Str("companyType", false);
            require.MinChallenges = r.Int("minChallenges", false, 0);
            r.Finish();
            return require;
        }

        private static ProfileSpec MapProfile(JsonObject json, string path, List<string> errors)
        {
            if (json == null) return null;
            var r = new FieldReader(json, path, errors);
            var profile = new ProfileSpec();
            profile.Rule = r.Str("rule", true);
            profile.Medal = r.Str("medal", true);
            r.Finish();
            return profile;
        }

        /// <summary>
        /// The cadence of a check whose file does not name one: a metric check follows its metric (monthly for the financial ones), the checks
        /// that wait for something to happen are evaluated on the event, the rest daily; a group is as frequent as its most frequent child.
        /// </summary>
        internal static string DefaultCadence(CheckSpec check)
        {
            switch (check.Kind)
            {
                case CheckSpec.KindMetric:
                    MetricInfo metric;
                    return ContentVocabulary.TryGetMetric(check.Metric, out metric) ? metric.DefaultCadence : CheckSpec.CadenceDaily;
                case CheckSpec.KindCpuReleased:
                case CheckSpec.KindRace:
                case CheckSpec.KindEvent:
                case CheckSpec.KindProfile:
                    return CheckSpec.CadenceEvent;
                case CheckSpec.KindAll:
                    return DefaultCadenceOfGroup(check.All);
                default:
                    return CheckSpec.CadenceDaily;
            }
        }

        private static string DefaultCadenceOfGroup(CheckSpec[] children)
        {
            if (children == null || children.Length == 0) return CheckSpec.CadenceDaily;
            bool anyDaily = false, anyMonthly = false;
            foreach (CheckSpec child in children)
            {
                string cadence = child.Cadence ?? DefaultCadence(child);
                if (cadence == CheckSpec.CadenceDaily) anyDaily = true;
                else if (cadence == CheckSpec.CadenceMonthly) anyMonthly = true;
            }
            if (anyDaily) return CheckSpec.CadenceDaily;
            return anyMonthly ? CheckSpec.CadenceMonthly : CheckSpec.CadenceEvent;
        }
    }
}
