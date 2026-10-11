using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>How bad a finding of the self-check is: a part of the feature does not work (<see cref="Error"/>) or it works in a weaker way (<see cref="Warning"/>).</summary>
    public enum FlawCheckSeverity
    {
        Warning = 0,
        Error = 1,
    }

    /// <summary>
    /// The three optional abilities of the feature that stand on private parts of the game (design.md, "Самопроверка на старте"): when the patch target or the field one of
    /// them needs is gone, that ability is off and the rest of the feature goes on.
    /// </summary>
    public enum FlawCapability
    {
        /// <summary>The mark "Flaw: …" on the card of a CPU (<c>CpuSalesUI.UpdateDailyTexts</c>).</summary>
        CardMark = 0,

        /// <summary>The line "Hidden flaw risk" in the CPU designer (<c>CreatorCpu.UpdateInfoTexts</c>).</summary>
        RiskHint = 1,

        /// <summary>The switch "Rush project" in the CPU designer and the mark of a rushed CPU.</summary>
        RushProject = 2,
    }

    /// <summary>One finding of the self-check: where, what is wrong and what it costs. A <see cref="Fatal"/> one makes the feature Unavailable.</summary>
    public sealed class FlawCheckFinding
    {
        public FlawCheckFinding(FlawCheckSeverity severity, string area, string message, bool fatal)
        {
            Severity = severity;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
            Fatal = fatal;
        }

        public FlawCheckSeverity Severity { get; private set; }

        /// <summary>The part of the feature the finding is about (<see cref="FlawSelfCheckRules.AreaPatches"/> and the other <c>Area…</c> names).</summary>
        public string Area { get; private set; }

        /// <summary>English, for <c>Player.log</c>.</summary>
        public string Message { get; private set; }

        /// <summary>A required part (the decision events, the effect types, the channels, the pack) is missing: the feature cannot work.</summary>
        public bool Fatal { get; private set; }

        public override string ToString()
        {
            return "[" + Area + "] " + Message;
        }
    }

    /// <summary>What the self-check found. Pure data with the lines for <c>Player.log</c>; the verdict on the feature is <see cref="IsFatal"/>.</summary>
    public sealed class FlawSelfCheckReport
    {
        /// <summary>The line of a clean check (the prefix <c>[PTMod] [design-flaw-recalls] INFO </c> is the log's).</summary>
        public const string OkLine = "self-check: OK";

        private readonly List<FlawCheckFinding> _findings = new List<FlawCheckFinding>();
        private readonly List<string> _notes = new List<string>();
        private readonly bool[] _off = new bool[3];

        public IList<FlawCheckFinding> Findings
        {
            get { return new ReadOnlyCollection<FlawCheckFinding>(_findings); }
        }

        /// <summary>Things worth a line of debugging that are not problems (a part that could not be looked at).</summary>
        public IList<string> Notes
        {
            get { return new ReadOnlyCollection<string>(_notes); }
        }

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        /// <summary>The six decision events were looked for (the event definitions were loaded). False: the check of them waits for the first ready game.</summary>
        public bool EventsChecked { get; internal set; }

        /// <summary>Nothing was found: no error and no warning.</summary>
        public bool IsOk
        {
            get { return _findings.Count == 0; }
        }

        public bool HasErrors
        {
            get { return Errors > 0; }
        }

        /// <summary>A required part is missing: the feature becomes Unavailable (the game goes on without it).</summary>
        public bool IsFatal
        {
            get
            {
                foreach (FlawCheckFinding finding in _findings)
                {
                    if (finding.Fatal) return true;
                }
                return false;
            }
        }

        /// <summary>The fatal findings in one line, for <c>ModFeatures.MarkUnavailable</c> and the log; empty when the feature is not fatal.</summary>
        public string FatalReason
        {
            get
            {
                var text = new StringBuilder();
                foreach (FlawCheckFinding finding in _findings)
                {
                    if (!finding.Fatal) continue;
                    if (text.Length > 0) text.Append("; ");
                    text.Append(finding.Message);
                }
                return text.ToString();
            }
        }

        /// <summary>The ability is on (its targets and fields were found, or the check could not tell).</summary>
        public bool IsOn(FlawCapability capability)
        {
            return !_off[(int)capability];
        }

        /// <summary>The first line: <see cref="OkLine"/> or the counts.</summary>
        public string Summary
        {
            get
            {
                if (IsOk) return OkLine;
                return "self-check: " + Errors.ToString(CultureInfo.InvariantCulture) + " error(s), " + Warnings.ToString(CultureInfo.InvariantCulture) + " warning(s)"
                       + (IsFatal ? ", the feature is unavailable" : string.Empty);
            }
        }

        /// <summary>One line per finding, <c>self-check: [area] message</c>, errors first (each group in the order they were found).</summary>
        public IList<string> Lines()
        {
            var lines = new List<string>(_findings.Count);
            foreach (FlawCheckFinding finding in _findings)
            {
                if (finding.Severity == FlawCheckSeverity.Error) lines.Add("self-check: " + finding);
            }
            foreach (FlawCheckFinding finding in _findings)
            {
                if (finding.Severity == FlawCheckSeverity.Warning) lines.Add("self-check: " + finding);
            }
            return lines;
        }

        public bool Has(FlawCheckSeverity severity, string area)
        {
            foreach (FlawCheckFinding finding in _findings)
            {
                if (finding.Severity == severity && string.Equals(finding.Area, area, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal FlawCheckFinding Add(FlawCheckSeverity severity, string area, string message, bool fatal)
        {
            var finding = new FlawCheckFinding(severity, area, message, fatal);
            _findings.Add(finding);
            if (severity == FlawCheckSeverity.Error) Errors++;
            else Warnings++;
            return finding;
        }

        internal void Note(string text)
        {
            _notes.Add(text);
        }

        internal void SwitchOff(FlawCapability capability)
        {
            _off[(int)capability] = true;
        }
    }

    /// <summary>One patch of the feature as the core reports it (<c>ModHooks.Report</c>): the target as <c>Class.Method</c> and where the patch stands.</summary>
    public sealed class FlawPatchFact
    {
        public FlawPatchFact(string target, bool applied, bool rejected, string status, string detail)
        {
            Target = target ?? string.Empty;
            Applied = applied;
            Rejected = rejected;
            Status = status ?? string.Empty;
            Detail = detail;
        }

        /// <summary><c>Class.Method</c> of the game method, as the patch registry names it.</summary>
        public string Target { get; private set; }

        /// <summary>The patch is in the game.</summary>
        public bool Applied { get; private set; }

        /// <summary>The core refused the patch by its rules (a reserved target, a second transpiler).</summary>
        public bool Rejected { get; private set; }

        /// <summary>The status in a word (<c>Applied</c>, <c>Failed</c>, <c>Rejected</c>, <c>Declared</c>, <c>Removed</c>).</summary>
        public string Status { get; private set; }

        /// <summary>English: why the patch failed; null when there is nothing to say.</summary>
        public string Detail { get; private set; }
    }

    /// <summary>A private field of the game the feature reads or injects, and the ability that needs it.</summary>
    public sealed class FlawMemberSpec
    {
        public FlawMemberSpec(string typeName, string member, FlawCapability capability, bool required, string role)
        {
            TypeName = typeName;
            Member = member;
            Capability = capability;
            Required = required;
            Role = role;
        }

        /// <summary>The short name of the game class (<c>CreatorCpu</c>).</summary>
        public string TypeName { get; private set; }

        /// <summary>The name of the field.</summary>
        public string Member { get; private set; }

        public FlawCapability Capability { get; private set; }

        /// <summary>True: the ability is off without it. False: the ability works in a weaker way (<see cref="Role"/> says how).</summary>
        public bool Required { get; private set; }

        /// <summary>What the feature does with the field, or what it does without it.</summary>
        public string Role { get; private set; }

        public string Label
        {
            get { return TypeName + "." + Member; }
        }
    }

    /// <summary>A target of a patch of the feature and the ability that stands on it.</summary>
    public sealed class FlawPatchTarget
    {
        public FlawPatchTarget(string target, FlawCapability capability)
        {
            Target = target;
            Capability = capability;
        }

        /// <summary><c>Class.Method</c>.</summary>
        public string Target { get; private set; }

        public FlawCapability Capability { get; private set; }
    }

    /// <summary>A channel of <c>ModModifiers</c> as the core declared it.</summary>
    public sealed class FlawChannelFact
    {
        public FlawChannelFact(string name, bool declared, bool perCpu)
        {
            Name = name;
            Declared = declared;
            PerCpu = perCpu;
        }

        public string Name { get; private set; }

        public bool Declared { get; private set; }

        /// <summary>The declaration says the value depends on the CPU (<c>ChannelSpec.PerCpu</c>).</summary>
        public bool PerCpu { get; private set; }
    }

    /// <summary>What the self-check looks at. The game layer fills it; the tests fill it with fakes. A member left <c>null</c> is a part that could not be looked at (a note, not a finding).</summary>
    public sealed class FlawSelfCheckInput
    {
        /// <summary>The patches of the feature in the core's report (only the rows of this feature).</summary>
        public IList<FlawPatchFact> Patches;

        /// <summary>Looks for a field of the game; returns null when it is there, otherwise why not (English). May throw: that counts as a missing field.</summary>
        public Func<FlawMemberSpec, string> MemberProblem;

        /// <summary>The handler of the feature is on <c>ModEvents.CpuReleased</c>: <c>true</c>, <c>false</c>, or <c>null</c> when the event cannot be looked into.</summary>
        public bool? ReleaseSubscribed;

        /// <summary>The handler of the feature is on <c>ModEvents.CpuTransferred</c>.</summary>
        public bool? TransferSubscribed;

        /// <summary>The core raises <c>CpuReleased</c> (its patches on <c>AddCpu</c> are applied); <c>null</c> when unknown.</summary>
        public bool? ReleaseTracked;

        /// <summary>The definitions of the events are loaded (the packs of <c>event-system</c> were read), so that <see cref="HasEvent"/> can be believed.</summary>
        public bool EventsLoaded;

        /// <summary>The event with this id is defined.</summary>
        public Func<string, bool> HasEvent;

        /// <summary>The registered effect types; <c>null</c> when the table cannot be read.</summary>
        public IList<string> EffectTypes;

        /// <summary>The two channels the feature writes to, as declared; <c>null</c> when the registry cannot be asked.</summary>
        public IList<FlawChannelFact> Channels;

        /// <summary>The problem of an event id under the identifier rules of the feature (null: it passes); <c>null</c> skips the check.</summary>
        public Func<string, string> EventIdProblem;

        /// <summary>The keys of the string tables the feature needs at once (name, description, parameters, flaw types).</summary>
        public IList<string> Keys;

        /// <summary>The problem of a key (it lacks the prefix of the feature, or no table has it); null: it passes. <c>null</c> skips the check.</summary>
        public Func<string, string> KeyProblem;

        /// <summary>How many values of <c>flaws.json</c> were rejected (the validator put the built-in ones in their place); 0 when clean.</summary>
        public int ContentErrors;

        /// <summary>The first rejected value, for the line of the log; null when none.</summary>
        public string FirstContentError;
    }

    /// <summary>
    /// The rules of the self-check at start (TSK-023, NFR-2, NFR-3, NFR-8, FR-15; design.md, "Самопроверка на старте" and "Error Handling"). Pure logic over the facts the game layer
    /// collects:
    /// <list type="bullet">
    /// <item><b>Patches.</b> Every target of <see cref="PatchTargets"/> is declared and applied, no target is refused by the core, and the feature does not patch the
    /// <c>AddCpu</c> methods the core owns. A missing target or field switches off only its own ability (<see cref="FlawCapability"/>): one warning per ability.</item>
    /// <item><b>Subscriptions.</b> The handlers are on <c>ModEvents.CpuReleased</c> and <c>CpuTransferred</c>, and the core raises the release event.</item>
    /// <item><b>Required parts</b> (a miss is <see cref="FlawCheckFinding.Fatal"/>: the feature becomes Unavailable, the game goes on): the six decision events of the pack,
    /// the four effect types, the channel <c>market.rating.cpu</c> (declared per CPU) and the channel <c>contract.signChance</c>.</item>
    /// <item><b>Identifiers.</b> The event ids and the string keys carry the prefix of the feature and exist.</item>
    /// <item><b>Content.</b> A rejected value of <c>flaws.json</c> is a warning (the built-in value is used).</item>
    /// </list>
    /// A check never throws: a part that fails is a warning and the next part runs.
    /// </summary>
    public static class FlawSelfCheckRules
    {
        public const string AreaPatches = "patches";
        public const string AreaMembers = "members";
        public const string AreaAbilities = "abilities";
        public const string AreaSubscriptions = "subscriptions";
        public const string AreaEvents = "events";
        public const string AreaEffects = "effects";
        public const string AreaChannels = "channels";
        public const string AreaIds = "ids";
        public const string AreaContent = "content";
        public const string AreaCheck = "check";

        /// <summary>The patches the feature declares, all <c>Optional</c>, and the ability each one stands for. Patches on <c>Company.AddCpu</c> and <c>AICompany.AddCpu</c> are not here: the core owns them.</summary>
        public static readonly IList<FlawPatchTarget> PatchTargets = new ReadOnlyCollection<FlawPatchTarget>(new[]
        {
            new FlawPatchTarget("CpuSalesUI.UpdateDailyTexts", FlawCapability.CardMark),
            new FlawPatchTarget("CreatorCpu.UpdateInfoTexts", FlawCapability.RiskHint),
            new FlawPatchTarget("CreatorCpu.Project", FlawCapability.RushProject),
            new FlawPatchTarget("CreatorCpu.CreateCpu", FlawCapability.RushProject),
            new FlawPatchTarget("CreatorCpu.UpdateUI", FlawCapability.RushProject),
            new FlawPatchTarget("ProjectCompletedHandler.AddCpu", FlawCapability.RushProject),
        });

        /// <summary>The methods whose release and transfer events the core raises; the feature must not patch them (the core would refuse).</summary>
        public static readonly IList<string> ForbiddenTargets = new ReadOnlyCollection<string>(new[] { "Company.AddCpu", "AICompany.AddCpu" });

        /// <summary>The private fields of the game the feature relies on (found by name with reflection, never by the shape the code was compiled against).</summary>
        public static readonly IList<FlawMemberSpec> Members = new ReadOnlyCollection<FlawMemberSpec>(new[]
        {
            new FlawMemberSpec("CpuSalesUI", "cpu", FlawCapability.CardMark, true, "the CPU of the card, injected into the patch"),
            new FlawMemberSpec("CpuSalesUI", "popularityText", FlawCapability.CardMark, true, "the text the mark is appended to"),

            new FlawMemberSpec("CreatorCpu", "failureRate", FlawCapability.RiskHint, true, "the failure rate of the design"),
            new FlawMemberSpec("CreatorCpu", "frequency", FlawCapability.RiskHint, true, "the frequency of the design"),
            new FlawMemberSpec("CreatorCpu", "maxFrequency", FlawCapability.RiskHint, true, "the ceiling of the frequency (the overclock is measured against it)"),
            new FlawMemberSpec("CreatorCpu", "temperature", FlawCapability.RiskHint, true, "the temperature of the design"),
            new FlawMemberSpec("CreatorCpu", "currentPackage", FlawCapability.RiskHint, true, "the package (the safe temperature)"),
            new FlawMemberSpec("CreatorCpu", "currentLithography", FlawCapability.RiskHint, true, "the process node (its maturity)"),
            new FlawMemberSpec("CreatorCpu", "nodeManufacturer", FlawCapability.RiskHint, true, "the manufacturer of the node"),
            new FlawMemberSpec("CreatorCpu", "failureRateText", FlawCapability.RiskHint, true, "the line the hint is put under"),
            new FlawMemberSpec("CreatorCpu", "projectTime", FlawCapability.RiskHint, false, "the term of the project (without it the release year is this year)"),

            new FlawMemberSpec("CreatorCpu", "projectTime", FlawCapability.RushProject, true, "the term of the project, shortened by the rush"),
            new FlawMemberSpec("CreatorCpu", "projectCost", FlawCapability.RushProject, true, "the cost of the project, raised by the rush"),
            new FlawMemberSpec("CreatorCpu", "nameInput", FlawCapability.RushProject, true, "the name of the project (the key of the pending rush project)"),
            new FlawMemberSpec("CreatorCpu", "createCpuButton", FlawCapability.RushProject, true, "the button the switch is put before"),
        });

        /// <summary>The effect types without which no decision can be applied (a miss is fatal).</summary>
        public static IList<string> RequiredEffectTypes
        {
            get { return FlawEffectTypes.All; }
        }

        /// <summary>The words for an ability in a line of the log.</summary>
        public static string NameOf(FlawCapability capability)
        {
            switch (capability)
            {
                case FlawCapability.CardMark:
                    return "the mark on the CPU card";
                case FlawCapability.RiskHint:
                    return "the risk hint in the CPU designer";
                default:
                    return "the 'Rush project' switch";
            }
        }

        /// <summary>Runs every check and returns the report. A null input gives a report with one warning.</summary>
        public static FlawSelfCheckReport Run(FlawSelfCheckInput input)
        {
            var report = new FlawSelfCheckReport();
            if (input == null)
            {
                report.Add(FlawCheckSeverity.Warning, AreaCheck, "nothing to check: the input is missing", false);
                return report;
            }

            var reasons = new Dictionary<FlawCapability, List<string>>();
            Guarded(report, AreaPatches, () => CheckPatches(report, input.Patches, reasons));
            Guarded(report, AreaMembers, () => CheckMembers(report, input.MemberProblem, reasons));
            Guarded(report, AreaAbilities, () => ReportAbilities(report, reasons));
            Guarded(report, AreaSubscriptions, () => CheckSubscriptions(report, input));
            Guarded(report, AreaEvents, () => CheckEvents(report, input));
            Guarded(report, AreaEffects, () => CheckEffects(report, input.EffectTypes));
            Guarded(report, AreaChannels, () => CheckChannels(report, input.Channels));
            Guarded(report, AreaIds, () => CheckIds(report, input));
            Guarded(report, AreaContent, () => CheckContent(report, input));
            return report;
        }

        // ---- the patches and the fields ----

        /// <summary>Every target is declared and applied; a missing one gives a reason for its ability. A rejected patch and a patch on an <c>AddCpu</c> of the core are errors.</summary>
        public static void CheckPatches(FlawSelfCheckReport report, IList<FlawPatchFact> patches, IDictionary<FlawCapability, List<string>> reasons)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (patches == null)
            {
                report.Note("the report of the patches is not available; the targets were not checked");
                return;
            }

            foreach (FlawPatchTarget target in PatchTargets)
            {
                bool declared = false;
                foreach (FlawPatchFact row in patches)
                {
                    if (!string.Equals(row.Target, target.Target, StringComparison.Ordinal)) continue;

                    declared = true;
                    if (row.Applied) continue;

                    AddReason(reasons, target.Capability, "the patch of " + target.Target + " is " + row.Status.ToLowerInvariant() + (string.IsNullOrEmpty(row.Detail) ? string.Empty : " (" + row.Detail + ")"));
                    if (row.Rejected)
                    {
                        report.Add(FlawCheckSeverity.Error, AreaPatches, "the core refused the patch of " + target.Target + (string.IsNullOrEmpty(row.Detail) ? string.Empty : ": " + row.Detail), false);
                    }
                }

                if (!declared) AddReason(reasons, target.Capability, "the patch of " + target.Target + " is not declared");
            }

            foreach (FlawPatchFact row in patches)
            {
                foreach (string forbidden in ForbiddenTargets)
                {
                    if (string.Equals(row.Target, forbidden, StringComparison.Ordinal))
                    {
                        report.Add(FlawCheckSeverity.Error, AreaPatches, "the feature patches " + forbidden + ", which the core owns (ModEvents.CpuReleased and CpuTransferred replace it)", false);
                    }
                }
            }
        }

        /// <summary>Every field is found; a missing required one gives a reason for its ability, a missing optional one a warning of its own.</summary>
        public static void CheckMembers(FlawSelfCheckReport report, Func<FlawMemberSpec, string> probe, IDictionary<FlawCapability, List<string>> reasons)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (probe == null)
            {
                report.Note("the fields of the game were not checked: there is no way to look at them");
                return;
            }

            foreach (FlawMemberSpec member in Members)
            {
                string problem;
                try
                {
                    problem = probe(member);
                }
                catch (Exception ex)
                {
                    problem = "the check failed (" + ex.GetType().Name + ": " + ex.Message + ")";
                }
                if (problem == null) continue;

                if (member.Required)
                {
                    AddReason(reasons, member.Capability, member.Label + ": " + problem);
                }
                else
                {
                    report.Add(FlawCheckSeverity.Warning, AreaMembers, member.Label + ": " + problem + "; it is used for " + member.Role, false);
                }
            }
        }

        /// <summary>One warning per ability that has a reason to be off; the ability is switched off in the report. The rest of the feature is not touched.</summary>
        private static void ReportAbilities(FlawSelfCheckReport report, IDictionary<FlawCapability, List<string>> reasons)
        {
            foreach (FlawCapability capability in new[] { FlawCapability.CardMark, FlawCapability.RiskHint, FlawCapability.RushProject })
            {
                List<string> list;
                if (!reasons.TryGetValue(capability, out list) || list.Count == 0) continue;

                report.SwitchOff(capability);
                report.Add(FlawCheckSeverity.Warning, AreaAbilities, NameOf(capability) + " is off: " + string.Join("; ", list.ToArray()) + "; the rest of the feature goes on (another version of the game?)", false);
            }
        }

        private static void AddReason(IDictionary<FlawCapability, List<string>> reasons, FlawCapability capability, string reason)
        {
            List<string> list;
            if (!reasons.TryGetValue(capability, out list))
            {
                list = new List<string>();
                reasons[capability] = list;
            }
            list.Add(reason);
        }

        // ---- the subscriptions ----

        private static void CheckSubscriptions(FlawSelfCheckReport report, FlawSelfCheckInput input)
        {
            Subscription(report, "ModEvents.CpuReleased", input.ReleaseSubscribed, "no CPU gets a hidden flaw");
            Subscription(report, "ModEvents.CpuTransferred", input.TransferSubscribed, "a flaw does not follow its CPU to the company that takes it over");

            if (input.ReleaseTracked == false)
            {
                report.Add(FlawCheckSeverity.Error, AreaSubscriptions, "the core does not raise ModEvents.CpuReleased (its patches on AddCpu are not applied): no CPU gets a hidden flaw", false);
            }
        }

        private static void Subscription(FlawSelfCheckReport report, string name, bool? subscribed, string consequence)
        {
            if (subscribed == null)
            {
                report.Note(name + " cannot be looked into; the subscription was not verified");
                return;
            }
            if (!subscribed.Value) report.Add(FlawCheckSeverity.Error, AreaSubscriptions, "the handler of the feature is not subscribed to " + name + ": " + consequence, false);
        }

        // ---- the required parts ----

        /// <summary>The six decision events are in the pack. Not loaded yet: a warning and <see cref="FlawSelfCheckReport.EventsChecked"/> stays false. Missing ones are fatal.</summary>
        private static void CheckEvents(FlawSelfCheckReport report, FlawSelfCheckInput input)
        {
            string[] expected = FlawDecisionPlanner.EventIds;
            if (!input.EventsLoaded || input.HasEvent == null)
            {
                report.Add(FlawCheckSeverity.Warning, AreaEvents, "the event definitions are not loaded yet; the " + expected.Length.ToString(CultureInfo.InvariantCulture) + " decision events are checked on the first ready game", false);
                return;
            }

            report.EventsChecked = true;
            var missing = new List<string>();
            foreach (string id in expected)
            {
                if (!input.HasEvent(id)) missing.Add(id);
            }
            if (missing.Count == 0) return;

            string text = missing.Count == expected.Length
                ? "the event pack has none of the " + expected.Length.ToString(CultureInfo.InvariantCulture) + " decision events (the pack is not registered, is empty or failed to load): the window of a decision cannot be shown"
                : "decision event(s) missing in the pack: " + string.Join(", ", missing.ToArray()) + ": the window of such a decision cannot be shown";
            report.Add(FlawCheckSeverity.Error, AreaEvents, text, true);
        }

        /// <summary>The four effect types are registered (a miss is fatal); the fifth, the line of the rivals' news, is a warning.</summary>
        public static void CheckEffects(FlawSelfCheckReport report, IList<string> registered)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (registered == null)
            {
                report.Note("the table of effect types cannot be read; the effect types were not checked");
                return;
            }

            var missing = new List<string>();
            foreach (string type in RequiredEffectTypes)
            {
                if (!Contains(registered, type)) missing.Add(type);
            }
            if (missing.Count > 0)
            {
                report.Add(FlawCheckSeverity.Error, AreaEffects, "effect type(s) not registered: " + string.Join(", ", missing.ToArray()) + ": the buttons of the decision window do nothing", true);
            }

            if (!Contains(registered, FlawEffectTypes.News))
            {
                report.Add(FlawCheckSeverity.Warning, AreaEffects, "the effect type " + FlawEffectTypes.News + " is not registered: the flaws of the rivals are not written to the event journal", false);
            }
        }

        /// <summary><c>market.rating.cpu</c> is declared for a single CPU and <c>contract.signChance</c> is declared; otherwise the consequences cannot be put into the game (fatal).</summary>
        public static void CheckChannels(FlawSelfCheckReport report, IList<FlawChannelFact> channels)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (channels == null)
            {
                report.Note("the registry of channels cannot be asked; the channels were not checked");
                return;
            }

            CheckChannel(report, channels, FlawChannelNames.MarketRatingCpu, true);
            CheckChannel(report, channels, FlawChannelNames.ContractSignChance, false);
        }

        private static void CheckChannel(FlawSelfCheckReport report, IList<FlawChannelFact> channels, string name, bool needsPerCpu)
        {
            FlawChannelFact found = null;
            foreach (FlawChannelFact channel in channels)
            {
                if (string.Equals(channel.Name, name, StringComparison.Ordinal)) found = channel;
            }

            if (found == null || !found.Declared)
            {
                report.Add(FlawCheckSeverity.Error, AreaChannels, "the channel " + name + " is not declared by the core: the effects of the decisions cannot reach the game", true);
            }
            else if (needsPerCpu && !found.PerCpu)
            {
                report.Add(FlawCheckSeverity.Error, AreaChannels, "the channel " + name + " is not declared per CPU (PerCpu = false): a patch cannot be limited to the CPU it is for", true);
            }
        }

        // ---- the identifiers and the content ----

        private static void CheckIds(FlawSelfCheckReport report, FlawSelfCheckInput input)
        {
            if (input.EventIdProblem == null)
            {
                report.Note("the table of identifiers is not available; the event ids were not checked");
            }
            else
            {
                foreach (string id in FlawDecisionPlanner.EventIds)
                {
                    string problem = Ask(input.EventIdProblem, id);
                    if (problem != null) report.Add(FlawCheckSeverity.Error, AreaIds, "the event id " + id + ": " + problem, false);
                }
            }

            if (input.KeyProblem == null || input.Keys == null)
            {
                report.Note("the string tables are not available; the keys were not checked");
                return;
            }

            var bad = new List<string>();
            string first = null;
            foreach (string key in input.Keys)
            {
                string problem = Ask(input.KeyProblem, key);
                if (problem == null) continue;
                bad.Add(key);
                if (first == null) first = key + ": " + problem;
            }
            if (bad.Count > 0)
            {
                report.Add(FlawCheckSeverity.Error, AreaIds, bad.Count.ToString(CultureInfo.InvariantCulture) + " string key(s) of the feature fail the check, the first is " + first, false);
            }
        }

        private static void CheckContent(FlawSelfCheckReport report, FlawSelfCheckInput input)
        {
            if (input.ContentErrors <= 0) return;

            report.Add(FlawCheckSeverity.Warning, AreaContent, input.ContentErrors.ToString(CultureInfo.InvariantCulture) + " value(s) of flaws.json were rejected and the built-in ones are used"
                + (string.IsNullOrEmpty(input.FirstContentError) ? string.Empty : ", the first: " + input.FirstContentError), false);
        }

        // ---- helpers ----

        private static string Ask(Func<string, string> problem, string id)
        {
            try
            {
                return problem(id);
            }
            catch (Exception ex)
            {
                return "the check failed (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
        }

        private static bool Contains(IList<string> list, string value)
        {
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Guarded(FlawSelfCheckReport report, string area, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                report.Add(FlawCheckSeverity.Warning, AreaCheck, "the check of '" + area + "' failed (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
        }
    }
}
