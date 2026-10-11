using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>How serious a finding of the self-check of the feature is.</summary>
    public enum HistCheckLevel
    {
        /// <summary>A part of the feature does not work (a letter about the game's own event, a string in one language); the rest does.</summary>
        Warning = 0,

        /// <summary>Something the feature stands on is wrong: it will not work as designed.</summary>
        Error = 1,
    }

    /// <summary>One finding of the self-check: where it was found and what is wrong, in English, for the log.</summary>
    public sealed class HistCheckProblem
    {
        public HistCheckProblem(HistCheckLevel level, string area, string message)
        {
            Level = level;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public HistCheckLevel Level { get; private set; }

        /// <summary><c>calendar</c>, <c>pack</c>, <c>types</c>, <c>predicates</c>, <c>channels</c>, <c>targets</c>, <c>patches</c>, <c>names</c> or <c>strings</c>.</summary>
        public string Area { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return Area + ": " + Message;
        }
    }

    /// <summary>What the registry of the core says about one channel of <c>ModModifiers</c>.</summary>
    public sealed class HistChannelFact
    {
        public HistChannelFact(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }

        /// <summary><c>ModModifiers.IsDeclared</c>.</summary>
        public bool Declared { get; set; }

        /// <summary>The declaration says the value depends on the CPU (<c>ChannelSpec.PerCpu</c>).</summary>
        public bool PerCpu { get; set; }

        /// <summary>The declaration says the value depends on the segment (<c>ChannelSpec.PerSegment</c>).</summary>
        public bool PerSegment { get; set; }
    }

    /// <summary>
    /// Everything the self-check of the feature looks at, as plain data (TSK-024; design.md, "Самопроверка на старте"): the targets in the game, the pack of the event
    /// system, the effect types and predicates the feature registered, the channels of the core, the size of the calendar and the patches of the feature. Collected from the
    /// game by <c>HistSelfCheck</c>; the tests make it up. A fact that could not be asked is left at its default and the rules report the gap.
    /// </summary>
    public sealed class HistCheckFacts
    {
        /// <summary><c>CompanyEventHandler.TriggerSolarFoundriesSpinOff</c> exists (the target of the optional patch of FR-16).</summary>
        public bool SpinOffMethodFound;

        /// <summary>The game's event <c>LicenseHelper.OnX86LicenseRevoked</c> exists.</summary>
        public bool LicenseEventFound;

        /// <summary>The game's event <c>BankruptcyHandler.OnCompanyWentBankrupt</c> exists.</summary>
        public bool BankruptcyEventFound;

        /// <summary>The ids of the events of the pack that the engine accepted (<c>EventRegistry.All</c> with the pack of the feature).</summary>
        public readonly List<string> PackEventIds = new List<string>();

        /// <summary>The ids of the events the entries of the calendar fire (stages and fallbacks).</summary>
        public readonly List<string> CalendarEventIds = new List<string>();

        /// <summary>The effect types the engine has handlers for (<c>EventEffects.RegisteredTypes</c>).</summary>
        public readonly List<string> EffectTypes = new List<string>();

        /// <summary>The predicate names <c>ModConditions</c> knows.</summary>
        public readonly List<string> Predicates = new List<string>();

        /// <summary>The channels to look for in the registry of the core.</summary>
        public readonly List<HistChannelFact> Channels = new List<HistChannelFact>();

        /// <summary>How many entries of the calendar passed the validator.</summary>
        public int ValidEntries;

        /// <summary>The calendar has been read (false: the self-check ran too early to judge it).</summary>
        public bool CalendarLoaded;

        /// <summary>The patches the feature declared, as <c>Class.Method</c> (<c>ModHooks.Report</c>).</summary>
        public readonly List<string> OwnPatchTargets = new List<string>();

        /// <summary>Whether a name of an event of the feature is allowed by <c>ModIds</c> (prefix <c>hist.</c>); null skips the rule.</summary>
        public Func<string, bool> EventIdAllowed;

        /// <summary>Whether a name of a predicate of the feature is allowed by <c>ModIds</c>; null skips the rule.</summary>
        public Func<string, bool> PredicateNameAllowed;

        /// <summary>The keys of the texts of the pack that the table of Russian lacks.</summary>
        public readonly List<string> MissingRu = new List<string>();

        /// <summary>The keys of the texts of the pack that the table of English lacks.</summary>
        public readonly List<string> MissingEn = new List<string>();
    }

    /// <summary>What the self-check came to: the findings, and what the feature does about them.</summary>
    public sealed class HistCheckVerdict
    {
        public readonly List<HistCheckProblem> Problems = new List<HistCheckProblem>();

        /// <summary>
        /// The categories of the calendar that get <c>Disabled</c> because the channel they act through is not usable: <c>export</c> and <c>race</c> act only through
        /// <c>market.rating.cpu</c>, so a failed channel switches them off (their records are not fired, the services do not contribute).
        /// </summary>
        public readonly List<string> DisabledCategories = new List<string>();

        public bool Ok
        {
            get { return Problems.Count == 0; }
        }

        public bool HasErrors
        {
            get { return Problems.Exists(p => p.Level == HistCheckLevel.Error); }
        }

        public int Errors
        {
            get { return Problems.FindAll(p => p.Level == HistCheckLevel.Error).Count; }
        }

        public int Warnings
        {
            get { return Problems.FindAll(p => p.Level == HistCheckLevel.Warning).Count; }
        }

        public bool Has(string area, string fragment)
        {
            return Problems.Exists(p => p.Area == area && p.Message.IndexOf(fragment ?? string.Empty, StringComparison.Ordinal) >= 0);
        }

        /// <summary>The summary line of the log: <c>selfcheck OK: ...</c> or the count of errors and warnings.</summary>
        public string Line { get; set; }
    }

    /// <summary>
    /// The rules of the self-check of the feature (TSK-024; FR-17, NFR-2; design.md, "Самопроверка на старте"). Pure: <see cref="Evaluate"/> judges the facts. The items:
    /// the targets of the game (the spin-off method and the events of the game, which the optional news and the services use); the pack and the events the calendar fires;
    /// the eight effect types and the five predicates; the channels <c>market.rating.cpu</c> (declared for a CPU and a segment), <c>acquisition.price</c> and
    /// <c>gpu.demand</c>; the size of the calendar (25 or more); the prefix <c>hist.</c> of the names; and that the feature patches neither <c>AddCpu</c> nor
    /// <c>NextMonth</c> (the core would have refused it). A failed <c>market.rating.cpu</c> disables the categories <c>export</c> and <c>race</c>.
    /// </summary>
    public static class HistSelfCheckRules
    {
        /// <summary>The channel the export rules and the races act through.</summary>
        public const string RatingChannel = "market.rating.cpu";

        /// <summary>The channel the antitrust surcharge of the acquisitions acts through (FR-19).</summary>
        public const string AcquisitionChannel = "acquisition.price";

        /// <summary>The channel the crypto booms act through (<c>gpu-product-line</c>).</summary>
        public const string GpuChannel = "gpu.demand";

        /// <summary>The methods the feature has no patch of its own on (design.md, "Патчи Harmony": the core owns them).</summary>
        public static readonly string[] ForbiddenPatchTargets =
        {
            "Company.AddCpu", "AICompany.AddCpu", "DateController.NextMonth", "CentralBank.DebtLimit",
        };

        /// <summary>The categories that act only through <c>market.rating.cpu</c>.</summary>
        public static readonly string[] RatingCategories = { HistCategories.Export, HistCategories.Race };

        /// <summary>The channels a collector has to fill in <see cref="HistCheckFacts.Channels"/>.</summary>
        public static readonly string[] RequiredChannels = { RatingChannel, AcquisitionChannel, GpuChannel };

        /// <summary>The names of the effect types the feature registers (the facts must contain them).</summary>
        public static readonly string[] RequiredEffectTypes = HistEffectTypes.Own;

        /// <summary>Judges the facts.</summary>
        public static HistCheckVerdict Evaluate(HistCheckFacts facts)
        {
            var verdict = new HistCheckVerdict();
            if (facts == null)
            {
                verdict.Problems.Add(new HistCheckProblem(HistCheckLevel.Error, "calendar", "there are no facts to judge"));
                verdict.Line = Summarize(verdict, facts);
                return verdict;
            }

            CheckTargets(facts, verdict);
            CheckCalendar(facts, verdict);
            CheckPack(facts, verdict);
            CheckTypes(facts, verdict);
            CheckPredicates(facts, verdict);
            CheckChannels(facts, verdict);
            CheckPatches(facts, verdict);
            CheckNames(facts, verdict);
            CheckStrings(facts, verdict);

            verdict.Line = Summarize(verdict, facts);
            return verdict;
        }

        // ---- the groups ----

        private static void CheckTargets(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            if (!facts.SpinOffMethodFound)
                Add(verdict, HistCheckLevel.Warning, "targets", "CompanyEventHandler.TriggerSolarFoundriesSpinOff is not in the game: the letter about the spin-off of the foundries (FR-16) is not sent");
            if (!facts.LicenseEventFound)
                Add(verdict, HistCheckLevel.Warning, "targets", "LicenseHelper.OnX86LicenseRevoked is not in the game: the letter about the x86 licence (FR-16) is not sent");
            if (!facts.BankruptcyEventFound)
                Add(verdict, HistCheckLevel.Warning, "targets", "BankruptcyHandler.OnCompanyWentBankrupt is not in the game: the cases and wars of a bankrupt company are closed on the next change of the companies only");
        }

        private static void CheckCalendar(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            if (!facts.CalendarLoaded)
            {
                Add(verdict, HistCheckLevel.Warning, "calendar", "the calendar has not been read yet");
                return;
            }

            if (!CalendarValidator.IsUsable(facts.ValidEntries))
                Add(verdict, HistCheckLevel.Error, "calendar", "calendar.json has " + facts.ValidEntries + " valid entries, at least " + CalendarValidator.MinValidEntries + " are needed");
        }

        private static void CheckPack(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            if (facts.PackEventIds.Count == 0)
            {
                Add(verdict, HistCheckLevel.Error, "pack", "the event system accepted none of the events of the pack (check the content report of events.json)");
                return;
            }

            var pack = new HashSet<string>(facts.PackEventIds, StringComparer.Ordinal);
            var absent = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string id in facts.CalendarEventIds)
            {
                if (!string.IsNullOrEmpty(id) && !pack.Contains(id)) absent.Add(id);
            }
            if (absent.Count > 0) Add(verdict, HistCheckLevel.Error, "pack", "the calendar fires events the pack does not have: " + Join(absent));

            var news = new List<string>();
            foreach (string id in BuiltInNews.EventIds)
            {
                if (!pack.Contains(id)) news.Add(id);
            }
            if (news.Count > 0) Add(verdict, HistCheckLevel.Warning, "pack", "the pack lacks the events of the letters about the game's own events: " + Join(news));
        }

        private static void CheckTypes(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            var known = new HashSet<string>(facts.EffectTypes, StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string type in RequiredEffectTypes)
            {
                if (!known.Contains(type)) missing.Add(type);
            }
            if (missing.Count > 0) Add(verdict, HistCheckLevel.Error, "types", "effect types of the feature are not registered with the event system: " + Join(missing));
        }

        private static void CheckPredicates(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            var known = new HashSet<string>(facts.Predicates, StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string name in HistPredicates.Names)
            {
                if (!known.Contains(name)) missing.Add(name);
            }
            if (missing.Count > 0) Add(verdict, HistCheckLevel.Error, "predicates", "predicates of the feature are not registered: " + Join(missing));
        }

        private static void CheckChannels(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            HistChannelFact rating = FindChannel(facts, RatingChannel);
            string why = null;
            if (rating == null || !rating.Declared) why = RatingChannel + " is not declared in the registry of the core";
            else if (!rating.PerCpu || !rating.PerSegment) why = RatingChannel + " is not declared for a CPU and a segment (PerCpu: " + rating.PerCpu + ", PerSegment: " + rating.PerSegment + ")";

            if (why != null)
            {
                Add(verdict, HistCheckLevel.Error, "channels", why + "; the categories " + Join(RatingCategories) + " are disabled");
                foreach (string category in RatingCategories) verdict.DisabledCategories.Add(category);
            }

            HistChannelFact acquisition = FindChannel(facts, AcquisitionChannel);
            if (acquisition == null || !acquisition.Declared)
                Add(verdict, HistCheckLevel.Warning, "channels", AcquisitionChannel + " is not declared in the registry of the core: the antitrust surcharge of the acquisitions has no effect");

            HistChannelFact gpu = FindChannel(facts, GpuChannel);
            if (gpu == null || !gpu.Declared)
                Add(verdict, HistCheckLevel.Warning, "channels", GpuChannel + " is not declared in the registry of the core: the crypto booms have no effect");
        }

        private static void CheckPatches(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            var found = new List<string>();
            foreach (string target in facts.OwnPatchTargets)
            {
                if (Array.IndexOf(ForbiddenPatchTargets, target) >= 0 && !found.Contains(target)) found.Add(target);
            }
            if (found.Count > 0) Add(verdict, HistCheckLevel.Error, "patches", "the feature patches " + Join(found) + ", which the services of the core own (design.md, \"Патчи Harmony\")");
        }

        private static void CheckNames(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            if (facts.EventIdAllowed != null)
            {
                var wrong = new List<string>();
                foreach (string id in facts.PackEventIds)
                {
                    if (!facts.EventIdAllowed(id)) wrong.Add(id);
                }
                if (wrong.Count > 0) Add(verdict, HistCheckLevel.Error, "names", "events of the pack without the prefix of the feature: " + Join(wrong));
            }

            if (facts.PredicateNameAllowed != null)
            {
                var wrong = new List<string>();
                foreach (string name in HistPredicates.Names)
                {
                    if (!facts.PredicateNameAllowed(name)) wrong.Add(name);
                }
                if (wrong.Count > 0) Add(verdict, HistCheckLevel.Error, "names", "predicates of the feature without the prefix of the feature: " + Join(wrong));
            }
        }

        private static void CheckStrings(HistCheckFacts facts, HistCheckVerdict verdict)
        {
            if (facts.MissingRu.Count > 0) Add(verdict, HistCheckLevel.Warning, "strings", "keys of the pack without a Russian text: " + Join(facts.MissingRu));
            if (facts.MissingEn.Count > 0) Add(verdict, HistCheckLevel.Warning, "strings", "keys of the pack without an English text: " + Join(facts.MissingEn));
        }

        // ---- small helpers ----

        private static HistChannelFact FindChannel(HistCheckFacts facts, string name)
        {
            foreach (HistChannelFact channel in facts.Channels)
            {
                if (string.Equals(channel.Name, name, StringComparison.Ordinal)) return channel;
            }
            return null;
        }

        private static void Add(HistCheckVerdict verdict, HistCheckLevel level, string area, string message)
        {
            verdict.Problems.Add(new HistCheckProblem(level, area, message));
        }

        private static string Join(IEnumerable<string> values)
        {
            var text = new StringBuilder();
            int count = 0;
            foreach (string value in values)
            {
                if (count == 8)
                {
                    text.Append(", ...");
                    break;
                }
                if (count > 0) text.Append(", ");
                text.Append(value);
                count++;
            }
            return text.ToString();
        }

        private static string Summarize(HistCheckVerdict verdict, HistCheckFacts facts)
        {
            if (verdict.Ok)
            {
                int events = facts == null ? 0 : facts.PackEventIds.Count;
                int entries = facts == null ? 0 : facts.ValidEntries;
                return "selfcheck OK: " + entries + " entries, " + events + " events, " + HistEffectTypes.Own.Length + " effect types, " + HistPredicates.Names.Count + " predicates";
            }
            return "selfcheck: " + verdict.Errors + " error(s), " + verdict.Warnings + " warning(s)";
        }
    }

    /// <summary>
    /// The categories of the calendar that the feature itself has switched off for this session because what they stand on failed the self-check (a channel of the core
    /// that is missing). They are off whatever the player's settings say; the settings themselves are not touched. Not saved.
    /// </summary>
    public sealed class CategoryBlocks
    {
        private readonly Dictionary<string, string> _reasons = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Switches a category off for the session. A repeated call keeps the first reason.</summary>
        public void Block(string category, string reason)
        {
            if (string.IsNullOrEmpty(category) || _reasons.ContainsKey(category)) return;
            _reasons[category] = reason ?? string.Empty;
        }

        public bool IsBlocked(string category)
        {
            return category != null && _reasons.ContainsKey(category);
        }

        /// <summary>Why the category is off; null when it is not.</summary>
        public string ReasonOf(string category)
        {
            string reason;
            return category != null && _reasons.TryGetValue(category, out reason) ? reason : null;
        }

        public int Count
        {
            get { return _reasons.Count; }
        }

        public void Clear()
        {
            _reasons.Clear();
        }
    }
}
