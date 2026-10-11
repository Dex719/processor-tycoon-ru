using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the validator asks the world: the vocabulary of the content and the registries of the game (design.md, "ContentValidator"). The real
    /// implementation lives in the game-side layer (<c>ModModifiers.IsDeclared</c>, <c>ModConditions.Parse</c>, <c>ModL10n.Has</c>); the tests
    /// bring a fake, so the validation runs without Unity.
    /// </summary>
    public interface IValidationContext
    {
        /// <summary>The id is a metric of the feature (<see cref="ContentVocabulary.Metrics"/>).</summary>
        bool IsKnownMetric(string id);

        /// <summary>The id is an internal event of the feature (<see cref="ContentVocabulary.Events"/>).</summary>
        bool IsKnownEvent(string id);

        /// <summary>The channel is declared in the registry of the core (<c>ModModifiers.IsDeclared</c>).</summary>
        bool IsKnownChannel(string channel);

        /// <summary>Parses the expression with <c>ModConditions.Parse</c>; false (with the reasons in <paramref name="errors"/>) when it does not parse, for instance for an unknown predicate.</summary>
        bool ValidateCondition(JsonNode node, IList<string> errors, string path);

        /// <summary>The L10n key has a text in some language (<c>ModL10n.Has</c>).</summary>
        bool HasString(string key);

        /// <summary>The id is a challenge that was loaded.</summary>
        bool IsKnownChallenge(string id);
    }

    /// <summary>The outcome of validating one definition: the reasons it must be rejected and the remarks that do not reject it.</summary>
    public sealed class ValidationResult
    {
        /// <summary>Why the record is rejected. English, one line each, in the form <c>path value reason</c> (<c>start.year 1998 not allowed</c>).</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>Remarks about a record that is accepted (a text missing from the tables, a challenge that was removed from the list).</summary>
        public readonly List<string> Warnings = new List<string>();

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }

        /// <summary>Appends the errors, then the warnings, to <paramref name="messages"/> (what the loader of the core expects from a validator).</summary>
        public void AddTo(List<string> messages)
        {
            if (messages == null) return;
            messages.AddRange(Errors);
            messages.AddRange(Warnings);
        }
    }

    /// <summary>
    /// The rules of the content (design.md, "Validation Rules (сценарий)"; FR-1, FR-11, FR-12; NFR-7): ranges, white lists, cross-references. A
    /// record that breaks one is rejected, the loader of the core drops it and the log says why (AC-1.1). Pure: the registries of the game come
    /// through <see cref="IValidationContext"/>. The one thing a validator changes is <see cref="ScenarioDefinition.AllowedChallenges"/>: a
    /// challenge that does not exist is removed from the list with a warning (design.md, "Error Handling").
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>Longest text of a content string, in characters (NFR-7).</summary>
        public const int MaxTextLength = 2000;

        /// <summary>Deepest nesting of <c>all</c> groups inside a check.</summary>
        public const int MaxCheckDepth = 4;

        private const int MaxGoals = 32;
        private const int MaxFails = 16;
        private const int MaxModifiers = 16;
        private const int MaxChildren = 16;
        private const double MaxFunds = 1e11, MinFunds = -1e9;
        private const double MinMultiplier = 0.1, MaxMultiplier = 10;

        public static ValidationResult ValidateScenario(ScenarioDefinition s, IValidationContext ctx)
        {
            var run = new Run(ctx);
            run.Guarded(() => run.Scenario(s));
            return run.Result;
        }

        public static ValidationResult ValidateChallenge(ChallengeDefinition c, IValidationContext ctx)
        {
            var run = new Run(ctx);
            run.Guarded(() => run.Challenge(c));
            return run.Result;
        }

        public static ValidationResult ValidateAchievement(AchievementDefinition a, IValidationContext ctx)
        {
            var run = new Run(ctx);
            run.Guarded(() => run.Achievement(a));
            return run.Result;
        }

        private enum CheckUse
        {
            Goal,
            Fail,
            Achievement,
        }

        /// <summary>One validation: the context, the result and the rules. A class of its own so the rules can share them without passing them around.</summary>
        private sealed class Run
        {
            private readonly IValidationContext _ctx;

            public Run(IValidationContext ctx)
            {
                _ctx = ctx;
                Result = new ValidationResult();
            }

            public ValidationResult Result { get; }

            public void Guarded(Action body)
            {
                if (_ctx == null)
                {
                    Error("no validation context was given");
                    return;
                }

                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    Error("validation failed unexpectedly: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            // ---- scenario ----

            public void Scenario(ScenarioDefinition s)
            {
                if (s == null)
                {
                    Error("the scenario is missing");
                    return;
                }

                Id(s.Id, "id", 3);
                Range(s.Order, 0, 10000, "order");
                Text(s.TitleKey, "titleKey");
                Text(s.DescKey, "descKey");
                Text(s.BriefingKey, "briefingKey");

                DateTime startDate = default(DateTime);
                bool haveStart = false;
                if (s.Start == null) Error("start is missing");
                else haveStart = Start(s.Start, out startDate);

                if (!ContentVocabulary.Contains(ContentVocabulary.OnboardingModes, s.Onboarding))
                {
                    Error("onboarding " + Quote(s.Onboarding) + " not allowed (allowed, forcedOn, forcedOff)");
                }

                Modifiers(s.Modifiers, "modifiers");

                DateTime deadline;
                bool haveDeadline = ContentVocabulary.TryParseDate(s.Deadline, out deadline);
                if (!haveDeadline) Error("deadline " + Quote(s.Deadline) + " is not a date (yyyy-MM-dd)");
                else if (haveStart && deadline <= startDate)
                {
                    Error("deadline " + s.Deadline + " must be after the start " + ContentVocabulary.FormatDate(startDate));
                }

                Range(s.TimeBonusMax, 0, 2000, "timeBonusMax");
                Goals(s, haveStart, startDate, haveDeadline, deadline);
                Fails(s.Fail, "fail");
                Medals(s);
                AllowedChallenges(s);
            }

            private bool Start(ScenarioStart start, out DateTime startDate)
            {
                startDate = default(DateTime);
                bool yearOk = ContentVocabulary.IsStartYear(start.Year);
                if (!yearOk) Error("start.year " + Number(start.Year) + " not allowed");
                else startDate = new DateTime(start.Year, 1, 1);

                bool typeOk = ContentVocabulary.IsCompanyType(start.CompanyType);
                if (!typeOk) Error("start.companyType " + Quote(start.CompanyType) + " not allowed (idm, fabless, foundry)");

                Range(start.DifficultyLevel, 0, 5, "start.difficultyLevel");
                if (start.DifficultyLevel == 5 && typeOk && start.CompanyType != "idm")
                {
                    Error("start.difficultyLevel 5 (Impossible) is only allowed for companyType idm");
                }

                if (start.StartingTechnology < 0 || start.StartingTechnology > 4)
                {
                    Error("start.startingTechnology " + Number(start.StartingTechnology) + " not allowed (0..4)");
                }

                Range(start.StartingFunds, MinFunds, MaxFunds, "start.startingFunds");
                Range(start.FactorySize, 0, 1000, "start.factorySize");
                if (typeOk && start.CompanyType == "fabless" && start.FactorySize != 0)
                {
                    Error("start.factorySize " + Number(start.FactorySize) + " must be 0 for a fabless company");
                }

                if (start.TechYear != 0 && (start.TechYear < 1971 || start.TechYear > 2030))
                {
                    Error("start.techYear " + Number(start.TechYear) + " is out of range (0 for none, or 1971..2030)");
                }
                return yearOk;
            }

            private void Goals(ScenarioDefinition s, bool haveStart, DateTime start, bool haveDeadline, DateTime deadline)
            {
                if (s.Goals == null || s.Goals.Length == 0)
                {
                    Error("goals: a scenario needs at least one goal");
                    return;
                }
                if (s.Goals.Length > MaxGoals) Error("goals: " + Number(s.Goals.Length) + " goals are too many (at most " + Number(MaxGoals) + ")");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                bool anyRequired = false;
                for (int i = 0; i < s.Goals.Length; i++)
                {
                    GoalSpec goal = s.Goals[i];
                    if (goal == null)
                    {
                        Error("goals[" + Number(i) + "] is missing");
                        continue;
                    }

                    string path = "goals[" + (string.IsNullOrEmpty(goal.Id) ? Number(i) : goal.Id) + "]";
                    Id(goal.Id, path + ".id", 1);
                    if (!string.IsNullOrEmpty(goal.Id) && !ids.Add(goal.Id)) Error(path + " duplicate goal id " + Quote(goal.Id));
                    if (goal.Required) anyRequired = true;

                    Range(goal.Points, 0, 10000, path + ".points");
                    Text(goal.TitleKey, path + ".titleKey");

                    if (goal.Check == null) Error(path + ".check is missing");
                    else Check(goal.Check, path + ".check", CheckUse.Goal, 0);

                    if (goal.By != null) By(goal.By, path + ".by", haveStart, start, haveDeadline, deadline);
                    if (goal.Progress != null) Progress(goal.Progress, path + ".progress");
                }

                if (!anyRequired) Error("goals: a scenario needs at least one required goal");
            }

            private void By(string by, string path, bool haveStart, DateTime start, bool haveDeadline, DateTime deadline)
            {
                DateTime date;
                if (!ContentVocabulary.TryParseDate(by, out date))
                {
                    Error(path + " " + Quote(by) + " is not a date (yyyy-MM-dd)");
                    return;
                }

                if ((haveStart && date <= start) || (haveDeadline && date > deadline))
                {
                    Error(path + " " + by + " is outside the scenario (after the start, not after the deadline)");
                }
            }

            private void Progress(ProgressSpec progress, string path)
            {
                if (!_ctx.IsKnownMetric(progress.Metric)) Error(path + ".metric " + Quote(progress.Metric) + " is not a known metric");
                Finite(progress.Target, path + ".target");
                if (!ContentVocabulary.Contains(ContentVocabulary.ProgressFormats, progress.Format))
                {
                    Error(path + ".format " + Quote(progress.Format) + " not allowed (percent, money, count)");
                }
            }

            private void Fails(FailSpec[] fails, string path)
            {
                if (fails == null || fails.Length == 0) return;
                if (fails.Length > MaxFails) Error(path + ": " + Number(fails.Length) + " rules are too many (at most " + Number(MaxFails) + ")");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < fails.Length; i++)
                {
                    FailSpec fail = fails[i];
                    if (fail == null)
                    {
                        Error(path + "[" + Number(i) + "] is missing");
                        continue;
                    }

                    string itemPath = path + "[" + (string.IsNullOrEmpty(fail.Id) ? Number(i) : fail.Id) + "]";
                    Id(fail.Id, itemPath + ".id", 1);
                    if (!string.IsNullOrEmpty(fail.Id) && !ids.Add(fail.Id)) Error(itemPath + " duplicate fail id " + Quote(fail.Id));
                    Text(fail.TextKey, itemPath + ".textKey");

                    if (fail.Check == null) Error(itemPath + ".check is missing");
                    else Check(fail.Check, itemPath + ".check", CheckUse.Fail, 0);
                }
            }

            private void Modifiers(ModifierSpec[] modifiers, string path)
            {
                if (modifiers == null || modifiers.Length == 0) return;
                if (modifiers.Length > MaxModifiers)
                {
                    Error(path + ": " + Number(modifiers.Length) + " modifiers are too many (at most " + Number(MaxModifiers) + ")");
                }

                for (int i = 0; i < modifiers.Length; i++)
                {
                    ModifierSpec modifier = modifiers[i];
                    string itemPath = path + "[" + Number(i) + "]";
                    if (modifier == null)
                    {
                        Error(itemPath + " is missing");
                        continue;
                    }

                    if (string.IsNullOrEmpty(modifier.Channel) || !_ctx.IsKnownChannel(modifier.Channel))
                    {
                        Error(itemPath + ".channel " + Quote(modifier.Channel) + " is not declared in the modifier registry");
                    }
                    Range(modifier.Multiplier, MinMultiplier, MaxMultiplier, itemPath + ".multiplier");
                    if (!ContentVocabulary.Contains(ContentVocabulary.Scopes, modifier.AppliesTo))
                    {
                        Error(itemPath + ".appliesTo " + Quote(modifier.AppliesTo) + " not allowed (player, ai, all)");
                    }
                }
            }

            private void Medals(ScenarioDefinition s)
            {
                MedalThresholds medals = s.Medals;
                if (medals == null)
                {
                    Error("medals are missing");
                    return;
                }

                if (!(medals.Bronze < medals.Silver && medals.Silver < medals.Gold))
                {
                    Error("medals: bronze < silver < gold is required (got " + Number(medals.Bronze) + ", " + Number(medals.Silver) + ", " + Number(medals.Gold) + ")");
                }
                if (medals.Bronze < 0) Error("medals.bronze " + Number(medals.Bronze) + " must not be negative");

                int max = s.MaxScoreWithoutChallenges();
                if (medals.Bronze > max)
                {
                    Error("medals.bronze " + Number(medals.Bronze) + " is above the most the scenario can give (" + Number(max) + ")");
                }
                else if (medals.Gold > max)
                {
                    Warn("medals.gold " + Number(medals.Gold) + " is above the most the scenario gives without challenges (" + Number(max) + ")");
                }
            }

            private void AllowedChallenges(ScenarioDefinition s)
            {
                if (s.AllowedChallenges == null || s.AllowedChallenges.Length == 0) return;

                var kept = new List<string>(s.AllowedChallenges.Length);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                bool changed = false;
                foreach (string id in s.AllowedChallenges)
                {
                    if (string.IsNullOrEmpty(id) || !_ctx.IsKnownChallenge(id))
                    {
                        Warn("allowedChallenges: unknown challenge " + Quote(id) + " removed from the list");
                        changed = true;
                    }
                    else if (!seen.Add(id))
                    {
                        Warn("allowedChallenges: challenge " + Quote(id) + " is listed twice");
                        changed = true;
                    }
                    else
                    {
                        kept.Add(id);
                    }
                }
                if (changed) s.AllowedChallenges = kept.ToArray();
            }

            // ---- challenge ----

            public void Challenge(ChallengeDefinition c)
            {
                if (c == null)
                {
                    Error("the challenge is missing");
                    return;
                }

                Id(c.Id, "id", 3);
                Text(c.TitleKey, "titleKey");
                Text(c.DescKey, "descKey");
                Range(c.ScoreBonus, 0, 1, "scoreBonus");
                Modifiers(c.Modifiers, "modifiers");
                Fails(c.Fail, "fail");

                bool noModifiers = c.Modifiers == null || c.Modifiers.Length == 0;
                bool noFail = c.Fail == null || c.Fail.Length == 0;
                if (noModifiers && noFail) Error("a challenge needs at least one modifier or one fail rule");

                if (c.RequiresCompanyType != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < c.RequiresCompanyType.Length; i++)
                    {
                        string type = c.RequiresCompanyType[i];
                        if (!ContentVocabulary.IsCompanyType(type)) Error("requiresCompanyType[" + Number(i) + "] " + Quote(type) + " not allowed (idm, fabless, foundry)");
                        else if (!seen.Add(type)) Warn("requiresCompanyType: " + Quote(type) + " is listed twice");
                    }
                }

                if (c.RequiresPerCompanyCredit && !string.IsNullOrEmpty(c.Id) && !_ctx.HasString(c.UnavailableKey))
                {
                    Warn("unavailableKey " + Quote(c.UnavailableKey) + " has no text in the string tables");
                }
            }

            // ---- achievement ----

            public void Achievement(AchievementDefinition a)
            {
                if (a == null)
                {
                    Error("the achievement is missing");
                    return;
                }

                Id(a.Id, "id", 3);
                if (!ContentVocabulary.Contains(ContentVocabulary.Categories, a.Category))
                {
                    Error("category " + Quote(a.Category) + " not allowed (" + string.Join(", ", ContentVocabulary.Categories) + ")");
                }
                Range(a.Points, 0, 10000, "points");
                Text(a.TitleKey, "titleKey");
                Text(a.DescKey, "descKey");

                if (a.Check == null) Error("check is missing");
                else Check(a.Check, "check", CheckUse.Achievement, 0);
            }

            // ---- checks ----

            private void Check(CheckSpec check, string path, CheckUse use, int depth)
            {
                if (depth > MaxCheckDepth)
                {
                    Error(path + " is nested too deep (at most " + Number(MaxCheckDepth) + " levels of 'all')");
                    return;
                }
                if (!ContentVocabulary.Contains(ContentVocabulary.CheckKinds, check.Kind))
                {
                    Error(path + ".kind " + Quote(check.Kind) + " is not a known kind of check (" + string.Join(", ", ContentVocabulary.CheckKinds) + ")");
                    return;
                }
                if (!IsKindAllowed(check.Kind, use, depth))
                {
                    Error(path + ".kind " + check.Kind + " is not allowed " + Where(use, depth));
                    return;
                }

                switch (check.Kind)
                {
                    case CheckSpec.KindMetric:
                        MetricCheck(check, path);
                        break;
                    case CheckSpec.KindCpuReleased:
                    case CheckSpec.KindRace:
                        Cpu(check.Cpu, path + ".cpu");
                        break;
                    case CheckSpec.KindEvent:
                        if (!_ctx.IsKnownEvent(check.Event)) Error(path + ".event " + Quote(check.Event) + " is not a known event");
                        Range(check.Count, 1, 1000000, path + ".count");
                        break;
                    case CheckSpec.KindCondition:
                        ConditionCheck(check, path);
                        break;
                    case CheckSpec.KindProfile:
                        ProfileCheck(check.Profile, path + ".profile");
                        break;
                    case CheckSpec.KindAll:
                        GroupCheck(check, path, use, depth);
                        break;
                }

                if (check.Kind != CheckSpec.KindMetric && check.Kind != CheckSpec.KindCondition && check.HoldDays != 0)
                {
                    Error(path + ".holdDays " + Number(check.HoldDays) + " is only supported by metric and condition checks");
                }
                if (check.Cadence != null && !ContentVocabulary.Contains(ContentVocabulary.Cadences, check.Cadence))
                {
                    Error(path + ".cadence " + Quote(check.Cadence) + " not allowed (daily, monthly, event)");
                }
                if (check.Require != null) Require(check.Require, path + ".require");
            }

            private static bool IsKindAllowed(string kind, CheckUse use, int depth)
            {
                switch (kind)
                {
                    case CheckSpec.KindMetric:
                    case CheckSpec.KindEvent:
                    case CheckSpec.KindCondition:
                    case CheckSpec.KindAll:
                        return true;
                    case CheckSpec.KindCpuReleased:
                        return use != CheckUse.Fail;
                    case CheckSpec.KindRace:
                        return use == CheckUse.Goal && depth == 0;
                    case CheckSpec.KindProfile:
                        return use == CheckUse.Achievement;
                    default:
                        return false;
                }
            }

            private static string Where(CheckUse use, int depth)
            {
                switch (use)
                {
                    case CheckUse.Fail: return "in a fail rule";
                    case CheckUse.Achievement: return "in an achievement";
                    default: return depth > 0 ? "inside 'all'" : "in a goal";
                }
            }

            private void MetricCheck(CheckSpec check, string path)
            {
                if (!_ctx.IsKnownMetric(check.Metric))
                {
                    Error(path + ".metric " + Quote(check.Metric) + " is not a known metric");
                }
                else
                {
                    MetricInfo metric;
                    if (ContentVocabulary.TryGetMetric(check.Metric, out metric) && IsFinite(check.Value)
                        && (check.Value < metric.Min || check.Value > metric.Max))
                    {
                        Warn(path + ".value " + Number(check.Value) + " is outside the range of " + check.Metric + " (" + Number(metric.Min) + ".." + Number(metric.Max) + ")");
                    }
                }

                if (!ContentVocabulary.Contains(ContentVocabulary.Operators, check.Op))
                {
                    Error(path + ".op " + Quote(check.Op) + " is not a comparison (" + string.Join(", ", ContentVocabulary.Operators) + ")");
                }
                Finite(check.Value, path + ".value");
                Range(check.HoldDays, 0, 3650, path + ".holdDays");
            }

            private void ConditionCheck(CheckSpec check, string path)
            {
                Range(check.HoldDays, 0, 3650, path + ".holdDays");
                if (check.Condition == null || check.Condition.Node == null)
                {
                    Error(path + ".condition is missing");
                    return;
                }

                var reasons = new List<string>();
                bool ok = _ctx.ValidateCondition(check.Condition.Node, reasons, path + ".condition");
                if (ok) return;

                if (reasons.Count == 0) Error(path + ".condition does not parse");
                foreach (string reason in reasons) Error(reason);
            }

            private void ProfileCheck(ProfileSpec profile, string path)
            {
                if (profile == null)
                {
                    Error(path + " is missing");
                    return;
                }
                if (!ContentVocabulary.Contains(ContentVocabulary.ProfileRules, profile.Rule))
                {
                    Error(path + ".rule " + Quote(profile.Rule) + " not allowed (" + string.Join(", ", ContentVocabulary.ProfileRules) + ")");
                }
                if (!ContentVocabulary.Contains(ContentVocabulary.Medals, profile.Medal))
                {
                    Error(path + ".medal " + Quote(profile.Medal) + " not allowed (" + string.Join(", ", ContentVocabulary.Medals) + ")");
                }
            }

            private void GroupCheck(CheckSpec check, string path, CheckUse use, int depth)
            {
                if (check.All == null || check.All.Length == 0)
                {
                    Error(path + ".all needs at least one check");
                    return;
                }
                if (check.All.Length > MaxChildren)
                {
                    Error(path + ".all: " + Number(check.All.Length) + " checks are too many (at most " + Number(MaxChildren) + ")");
                }

                for (int i = 0; i < check.All.Length; i++)
                {
                    CheckSpec child = check.All[i];
                    string childPath = path + ".all[" + Number(i) + "]";
                    if (child == null) Error(childPath + " is missing");
                    else Check(child, childPath, use, depth + 1);
                }
            }

            private void Cpu(CpuFilter cpu, string path)
            {
                if (cpu == null)
                {
                    Error(path + " is missing");
                    return;
                }

                if (!cpu.HasCriteria) Error(path + " needs at least one criterion (minFrequency, minCores, maxPower, requireSmt, companyTypeAtRelease, releasedBefore)");
                Range(cpu.MinFrequency, 0, 1e9, path + ".minFrequency");
                Range(cpu.MinCores, 0, 256, path + ".minCores");
                Range(cpu.MaxPower, 0, 10000, path + ".maxPower");

                if (cpu.CompanyTypeAtRelease != null && !ContentVocabulary.IsCompanyType(cpu.CompanyTypeAtRelease))
                {
                    Error(path + ".companyTypeAtRelease " + Quote(cpu.CompanyTypeAtRelease) + " not allowed (idm, fabless, foundry)");
                }

                DateTime date;
                if (cpu.ReleasedBefore != null && !ContentVocabulary.TryParseDate(cpu.ReleasedBefore, out date))
                {
                    Error(path + ".releasedBefore " + Quote(cpu.ReleasedBefore) + " is not a date (yyyy-MM-dd)");
                }
            }

            private void Require(RequireSpec require, string path)
            {
                if (require.DifficultyLevel != -1) Range(require.DifficultyLevel, 0, 5, path + ".difficultyLevel");
                if (require.CompanyType != null && !ContentVocabulary.IsCompanyType(require.CompanyType))
                {
                    Error(path + ".companyType " + Quote(require.CompanyType) + " not allowed (idm, fabless, foundry)");
                }
                Range(require.MinChallenges, 0, 100, path + ".minChallenges");
            }

            // ---- small rules ----

            private void Id(string id, string path, int minLength)
            {
                bool ok = !string.IsNullOrEmpty(id) && id.Length >= minLength && id.Length <= 40;
                if (ok)
                {
                    foreach (char ch in id)
                    {
                        if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-'))
                        {
                            ok = false;
                            break;
                        }
                    }
                }
                if (!ok) Error(path + " " + Quote(id) + " is not valid (" + Number(minLength) + "-40 characters of a-z, 0-9 and '-')");
            }

            private void Text(string key, string path)
            {
                if (string.IsNullOrEmpty(key))
                {
                    Error(path + " is missing");
                    return;
                }
                if (key.Length > MaxTextLength)
                {
                    Error(path + " is longer than " + Number(MaxTextLength) + " characters");
                    return;
                }

                bool prefixed = false;
                foreach (string prefix in ContentVocabulary.KeyPrefixes)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length)
                    {
                        prefixed = true;
                        break;
                    }
                }
                if (!prefixed)
                {
                    Error(path + " " + Quote(key) + " must be a key starting with " + string.Join(" or ", ContentVocabulary.KeyPrefixes));
                    return;
                }

                if (!_ctx.HasString(key)) Warn(path + " " + Quote(key) + " has no text in the string tables");
            }

            private void Range(int value, int min, int max, string path)
            {
                if (value < min || value > max) Error(path + " " + Number(value) + " is out of range " + Number(min) + ".." + Number(max));
            }

            private void Range(double value, double min, double max, string path)
            {
                if (!IsFinite(value) || value < min || value > max)
                {
                    Error(path + " " + Number(value) + " is out of range " + Number(min) + ".." + Number(max));
                }
            }

            private void Finite(double value, string path)
            {
                if (!IsFinite(value)) Error(path + " " + Number(value) + " is not a finite number");
            }

            private static bool IsFinite(double value)
            {
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }

            private void Error(string message)
            {
                Result.Errors.Add(message);
            }

            private void Warn(string message)
            {
                Result.Warnings.Add(message);
            }

            private static string Quote(string value)
            {
                return value == null ? "(none)" : "'" + value + "'";
            }

            private static string Number(int value)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            private static string Number(double value)
            {
                if (double.IsNaN(value)) return "NaN";
                if (double.IsPositiveInfinity(value)) return "Infinity";
                if (double.IsNegativeInfinity(value)) return "-Infinity";

                // Seven decimals are enough for a content value, and keep a float such as 0.05f from showing its binary tail (0.0500000007...).
                return Math.Abs(value) >= 1e7
                    ? value.ToString("0", CultureInfo.InvariantCulture)
                    : value.ToString("0.#######", CultureInfo.InvariantCulture);
            }
        }
    }
}
