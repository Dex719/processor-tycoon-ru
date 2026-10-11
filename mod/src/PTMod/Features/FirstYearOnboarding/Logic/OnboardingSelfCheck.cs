using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>How bad a finding of the self-check is: a part of the tutorial does not work (<see cref="Error"/>) or it works in a weaker way (<see cref="Warning"/>).</summary>
    public enum SelfCheckSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>One finding of the self-check: the area it belongs to (<c>game</c>, <c>predicates</c>, <c>content</c>, <c>texts</c>) and a line of English for the log.</summary>
    public sealed class SelfCheckProblem
    {
        public SelfCheckProblem(SelfCheckSeverity severity, string area, string message)
        {
            Severity = severity;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public SelfCheckSeverity Severity { get; private set; }

        public string Area { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return "[" + Area + "] " + Message;
        }
    }

    /// <summary>
    /// What the self-check at start found (TSK-016, NFR-2, NFR-4): a list of findings and the lines for <c>Player.log</c>. Nothing found is the line
    /// <c>self-check: OK</c>; otherwise <c>self-check: 2 error(s), 1 warning(s)</c> and one line per finding, errors first.
    /// </summary>
    public sealed class SelfCheckReport
    {
        /// <summary>The line of a clean check (the prefix <c>[PTMod] [first-year-onboarding] INFO </c> is the log's).</summary>
        public const string OkLine = "self-check: OK";

        private readonly List<SelfCheckProblem> _problems = new List<SelfCheckProblem>();

        public IList<SelfCheckProblem> Problems
        {
            get { return new ReadOnlyCollection<SelfCheckProblem>(_problems); }
        }

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        /// <summary>Nothing was found: no error and no warning.</summary>
        public bool IsOk
        {
            get { return _problems.Count == 0; }
        }

        public bool HasErrors
        {
            get { return Errors > 0; }
        }

        /// <summary>The first line: <see cref="OkLine"/> or the counts.</summary>
        public string Summary
        {
            get
            {
                if (IsOk) return OkLine;
                return "self-check: " + Errors.ToString(CultureInfo.InvariantCulture) + " error(s), " + Warnings.ToString(CultureInfo.InvariantCulture) + " warning(s)";
            }
        }

        public SelfCheckProblem Add(SelfCheckSeverity severity, string area, string message)
        {
            var problem = new SelfCheckProblem(severity, area, message);
            _problems.Add(problem);
            if (severity == SelfCheckSeverity.Error) Errors++;
            else Warnings++;
            return problem;
        }

        public bool Has(SelfCheckSeverity severity, string area)
        {
            foreach (SelfCheckProblem problem in _problems)
            {
                if (problem.Severity == severity && string.Equals(problem.Area, area, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>One line per finding, <c>self-check: [area] message</c>, errors first (each group in the order they were found).</summary>
        public IList<string> Lines()
        {
            var lines = new List<string>(_problems.Count);
            foreach (SelfCheckProblem problem in _problems)
            {
                if (problem.Severity == SelfCheckSeverity.Error) lines.Add("self-check: " + problem);
            }
            foreach (SelfCheckProblem problem in _problems)
            {
                if (problem.Severity == SelfCheckSeverity.Warning) lines.Add("self-check: " + problem);
            }
            return lines;
        }
    }

    public enum GameMemberKind
    {
        Method = 0,
        Field = 1,
        Event = 2
    }

    /// <summary>
    /// A member of the game's classes the tutorial relies on (design.md, "Самопроверка на старте"): where it is, what the tutorial does with it and what
    /// happens without it. A member with a <see cref="Fallback"/> costs a warning (the tutorial goes on in a weaker way), one without it an error.
    /// </summary>
    public sealed class GameMemberSpec
    {
        public GameMemberSpec(string typeName, GameMemberKind kind, string name, string role, string fallback, string valueType, params string[] parameterTypes)
        {
            TypeName = typeName;
            Kind = kind;
            Name = name;
            Role = role;
            Fallback = fallback;
            ValueType = valueType;
            ParameterTypes = parameterTypes ?? new string[0];
        }

        /// <summary>Full name of the class as reflection spells it (a nested class after <c>+</c>).</summary>
        public string TypeName { get; private set; }

        public GameMemberKind Kind { get; private set; }

        public string Name { get; private set; }

        /// <summary>Full names of the parameter types of a method (empty for none); ignored for a field and an event.</summary>
        public string[] ParameterTypes { get; private set; }

        /// <summary>Full name of the type of a field, or the return type of a method; null when the type does not matter.</summary>
        public string ValueType { get; private set; }

        /// <summary>What the tutorial does with the member.</summary>
        public string Role { get; private set; }

        /// <summary>What the tutorial does when the member is gone; null when nothing takes its place.</summary>
        public string Fallback { get; private set; }

        /// <summary><c>Class.Member</c>, the way the log names it.</summary>
        public string Label
        {
            get
            {
                int dot = TypeName == null ? -1 : TypeName.LastIndexOf('.');
                string shortName = dot < 0 ? TypeName : TypeName.Substring(dot + 1);
                return shortName + "." + Name;
            }
        }

        public SelfCheckSeverity Severity
        {
            get { return string.IsNullOrEmpty(Fallback) ? SelfCheckSeverity.Error : SelfCheckSeverity.Warning; }
        }
    }

    /// <summary>The game layer's reflection behind <see cref="GameMemberSpec"/>: the tests use a fake.</summary>
    public interface IGameMemberProbe
    {
        /// <summary>Looks for the member. Null when it is there; otherwise the reason in English (<c>no such method</c>). May throw: the check counts that as a missing member.</summary>
        string Probe(GameMemberSpec member);
    }

    /// <summary>The members of the game the tutorial uses: the list of the self-check (TSK-016).</summary>
    public static class GameMembers
    {
        private const string Creator = "ProcessorTycoon.Hardware.Creation.CreatorManager";
        private const string CreatorWindowType = "ProcessorTycoon.Hardware.Creation.CreatorWindow+Type";
        private const string Date = "ProcessorTycoon.TimeSystem.DateController";
        private const string Market = "ProcessorTycoon.MarketSystem.Market";
        private const string Bankruptcy = "ProcessorTycoon.Bank.BankruptcyHandler";
        private const string Formatter = "ProcessorTycoon.StringFormatter";
        private const string Single = "System.Single";
        private const string Int32 = "System.Int32";

        public static readonly IReadOnlyList<GameMemberSpec> All = new[]
        {
            new GameMemberSpec(Creator, GameMemberKind.Method, "OpenCreatorWindowOfType",
                "the buttons \"Open the CPU designer\" and \"Open the factory expansion\" of the mails", null, null, CreatorWindowType),

            new GameMemberSpec(Date, GameMemberKind.Method, "ManualPause",
                "the pause when the mail of a new step arrives", null, null, "System.Boolean"),
            new GameMemberSpec(Date, GameMemberKind.Field, "currentTimeSpeed",
                "tells whether the time runs, so that the pause never stops a game that stands already",
                "the tutorial does not pause the game on a new step", Int32),

            new GameMemberSpec(Market, GameMemberKind.Method, "get_HighEndBudget", "the High End budget quoted in the mails", null, Single),
            new GameMemberSpec(Market, GameMemberKind.Method, "get_MidRangeBudget", "the Mid Range budget quoted in the mails", null, Single),
            new GameMemberSpec(Market, GameMemberKind.Method, "get_LowEndBudget", "the Low End budget quoted in the mails", null, Single),
            new GameMemberSpec(Market, GameMemberKind.Method, "get_IndustriesBudget", "the Industries budget quoted in the mails", null, Single),

            new GameMemberSpec(Bankruptcy, GameMemberKind.Field, "defaultTimeLimit",
                "the days a company may stay in default, quoted in the mails",
                "the mails quote defaultDaysFallback of config.json (120 by default)", Int32),
            new GameMemberSpec(Bankruptcy, GameMemberKind.Event, "OnCompanyWentBankrupt",
                "ends the tutorial when the player's company goes bankrupt",
                "the tutorial ends only with its time limit", null),

            new GameMemberSpec(Formatter, GameMemberKind.Method, "FloatToMoney",
                "writes the money of the mails the way the game does", null, "System.String",
                Single, "System.String", "System.Boolean", "System.Boolean", "System.Boolean")
        };
    }

    /// <summary>What the self-check looks at. The game layer fills it; the tests fill it with fakes.</summary>
    public sealed class OnboardingSelfCheckInput
    {
        /// <summary>Looks for a member of the game; null skips the check of the members (and says so).</summary>
        public IGameMemberProbe Members;

        /// <summary>The members to look for; the default is <see cref="GameMembers.All"/>.</summary>
        public IEnumerable<GameMemberSpec> MemberList = GameMembers.All;

        /// <summary>Why the core did not accept the predicate (a name); null when it did. Null skips the check of the predicates (and says so).</summary>
        public Func<string, string> PredicateRefusal;

        /// <summary>The checked steps.</summary>
        public StepCatalog Catalog;

        /// <summary>The key exists in the string table of the Russian language. Null skips the check of the texts (and says so).</summary>
        public Func<string, bool> HasRussianText;

        /// <summary>
        /// <see cref="HasRussianText"/> looks at the Russian table itself. False when the game layer could only ask the language in use: the check is
        /// then approximate and says so.
        /// </summary>
        public bool RussianIsExact = true;
    }

    /// <summary>
    /// The rules of the self-check at start (TSK-016; design.md, "Самопроверка на старте"): the members of the game, the acceptance of the predicates
    /// <c>onb.*</c> by the core, the catalog of the steps, the Russian texts. A check never throws: a part that fails is a warning and the next part runs.
    /// Pure logic; the game's classes, the core and the tables are reached through <see cref="OnboardingSelfCheckInput"/>.
    /// </summary>
    public static class OnboardingSelfCheckRules
    {
        public const string AreaGame = "game";
        public const string AreaPredicates = "predicates";
        public const string AreaContent = "content";
        public const string AreaTexts = "texts";
        public const string AreaCheck = "check";

        /// <summary>The key of the line the hub page shows for broken content (the finding names it, so the log and the page agree).</summary>
        public const string HubErrorKey = "onb.error.content";

        private const int MaxKeysListed = 8;

        /// <summary>Runs every check and returns the report. A null input gives a report with one warning.</summary>
        public static SelfCheckReport Run(OnboardingSelfCheckInput input)
        {
            var report = new SelfCheckReport();
            if (input == null)
            {
                report.Add(SelfCheckSeverity.Warning, AreaCheck, "nothing to check: the input is missing");
                return report;
            }

            Guarded(report, AreaGame, () => CheckMembers(report, input.Members, input.MemberList));
            Guarded(report, AreaPredicates, () => CheckPredicates(report, input.PredicateRefusal));
            Guarded(report, AreaContent, () => CheckCatalog(report, input.Catalog));
            Guarded(report, AreaTexts, () => CheckTexts(report, input.Catalog, input.HasRussianText, input.RussianIsExact));
            return report;
        }

        // ---- the game ----

        /// <summary>Looks for every member. A member that is gone is an error, or a warning when the tutorial has a fallback for it.</summary>
        public static void CheckMembers(SelfCheckReport report, IGameMemberProbe probe, IEnumerable<GameMemberSpec> members)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (probe == null)
            {
                report.Add(SelfCheckSeverity.Warning, AreaGame, "the members of the game were not checked: there is no way to look at them");
                return;
            }

            foreach (GameMemberSpec member in members ?? GameMembers.All)
            {
                string problem;
                try
                {
                    problem = probe.Probe(member);
                }
                catch (Exception ex)
                {
                    problem = "the check failed (" + ex.GetType().Name + ": " + ex.Message + ")";
                }
                if (problem == null) continue;

                var text = new StringBuilder();
                text.Append(member.Label).Append(": ").Append(problem).Append(". It is used for ").Append(member.Role);
                text.Append(string.IsNullOrEmpty(member.Fallback)
                    ? "; the tutorial does without it only in part"
                    : "; without it " + member.Fallback);
                report.Add(member.Severity, AreaGame, text.ToString());
            }
        }

        // ---- the predicates ----

        /// <summary>Every predicate of <see cref="OnboardingPredicateLibrary.Names"/> has to be known to the core as the feature's own.</summary>
        public static void CheckPredicates(SelfCheckReport report, Func<string, string> refusal)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (refusal == null)
            {
                report.Add(SelfCheckSeverity.Warning, AreaPredicates, "the predicates were not checked: there is no way to ask the core");
                return;
            }

            foreach (string name in OnboardingPredicateLibrary.Names)
            {
                string reason;
                try
                {
                    reason = refusal(name);
                }
                catch (Exception ex)
                {
                    reason = "the check failed (" + ex.GetType().Name + ": " + ex.Message + ")";
                }
                if (reason == null) continue;

                report.Add(SelfCheckSeverity.Error, AreaPredicates,
                    "the core did not accept the predicate '" + name + "': " + reason + "; the steps that use it are dropped");
            }
        }

        // ---- the catalog ----

        /// <summary>
        /// The steps: no usable catalog and a track without steps are errors (the hub page shows <see cref="HubErrorKey"/>), a step the checks dropped and the
        /// soft findings of the checks are warnings.
        /// </summary>
        public static void CheckCatalog(SelfCheckReport report, StepCatalog catalog)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (catalog == null || catalog.IsEmpty)
            {
                report.Add(SelfCheckSeverity.Error, AreaContent,
                    "no track has a usable step, the tutorial cannot start; the hub page shows '" + HubErrorKey + "'");
            }
            else
            {
                foreach (string track in catalog.MissingTracks)
                {
                    report.Add(SelfCheckSeverity.Error, AreaContent,
                        "the track '" + track + "' has no sequential step, the tutorial is off for it; the hub page shows '" + HubErrorKey + "'");
                }
            }

            if (catalog == null) return;

            foreach (string error in catalog.Errors)
            {
                if (error != null && error.StartsWith("track '", StringComparison.Ordinal)) continue;   // told above, once per track
                report.Add(SelfCheckSeverity.Warning, AreaContent, error);
            }

            if (catalog.Warnings.Count > 0)
            {
                report.Add(SelfCheckSeverity.Warning, AreaContent,
                    catalog.Warnings.Count.ToString(CultureInfo.InvariantCulture) + " soft finding(s) in the steps, the first: " + catalog.Warnings[0]);
            }
        }

        // ---- the texts ----

        /// <summary>
        /// Every key of <see cref="OnboardingKeys.All"/> has to be in the Russian table, the main language of the tutorial (NFR-4). A key that is only
        /// in English shows the player English text, a key in neither shows <c>[key]</c>.
        /// </summary>
        public static void CheckTexts(SelfCheckReport report, StepCatalog catalog, Func<string, bool> hasRussianText, bool russianIsExact)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (hasRussianText == null)
            {
                report.Add(SelfCheckSeverity.Warning, AreaTexts, "the texts were not checked: there is no way to read the string tables");
                return;
            }

            var missing = new List<string>();
            foreach (string key in OnboardingKeys.All(catalog))
            {
                bool found;
                try
                {
                    found = hasRussianText(key);
                }
                catch (Exception)
                {
                    found = false;
                }
                if (!found) missing.Add(key);
            }

            if (missing.Count > 0)
            {
                var listed = new StringBuilder();
                for (int i = 0; i < missing.Count && i < MaxKeysListed; i++)
                {
                    if (i > 0) listed.Append(", ");
                    listed.Append(missing[i]);
                }
                if (missing.Count > MaxKeysListed) listed.Append(" and ").Append((missing.Count - MaxKeysListed).ToString(CultureInfo.InvariantCulture)).Append(" more");

                report.Add(SelfCheckSeverity.Error, AreaTexts,
                    missing.Count.ToString(CultureInfo.InvariantCulture) + " key(s) are missing in the Russian table: " + listed);
            }

            if (!russianIsExact)
            {
                report.Add(SelfCheckSeverity.Warning, AreaTexts,
                    "the Russian table could not be read by itself, the keys were looked up in the language in use (English or Russian): a key that exists only in English was not noticed");
            }
        }

        // ---- helpers ----

        private static void Guarded(SelfCheckReport report, string area, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                report.Add(SelfCheckSeverity.Warning, AreaCheck, "the check of '" + area + "' failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }
    }
}
