using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The three tabs of the page "Хроника отрасли" (FR-15; design.md, Component 10).</summary>
    public enum ChronicleTab
    {
        /// <summary>What happened: the date, the event, who was in the roles, the outcome.</summary>
        Past = 0,

        /// <summary>What is in force now: the lasting effects with the days left, the export rules, the price wars, the races.</summary>
        Active = 1,

        /// <summary>The cases of the regulators, the suits of the victims and the patent disputes: the stage, the sums, the nearest date.</summary>
        Cases = 2,
    }

    /// <summary>One line of the page: the left side and the right side, as the widget "title: value" shows them.</summary>
    public sealed class ChronicleRow
    {
        public ChronicleRow(string title, string text)
        {
            Title = title ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string Title { get; private set; }

        public string Text { get; private set; }

        /// <summary>The line wants the player's attention (a fine to decide, a case that waits for a ruling): the page paints it.</summary>
        public bool Attention { get; set; }

        /// <summary>The line as one text, <c>Title: Text</c>.</summary>
        public string Full
        {
            get { return Text.Length == 0 ? Title : Title + ": " + Text; }
        }

        /// <summary>For the order of the lines (a day number, or the days left); not shown.</summary>
        internal int Order { get; set; }

        /// <summary>The place where the line was made, to keep the order of the calendar among lines with the same <see cref="Order"/>; not shown.</summary>
        internal int Sequence { get; set; }
    }

    /// <summary>The entries of <c>flags</c> that tell the chronicle which entries the catch-up of a late start made effects of (FR-4).</summary>
    public static class CatchUpMarks
    {
        /// <summary>The start of the name of the flag; the day of the start follows it.</summary>
        public const string Prefix = "caughtUp-";

        /// <summary>The flag a caught-up entry gets: the day the game started, which is where its lasting effects were counted from.</summary>
        public static string FlagFor(int startDay)
        {
            return Prefix + startDay.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The start day in the flags of an entry; false when the entry was not caught up.</summary>
        public static bool TryRead(IEnumerable<string> flags, out int startDay)
        {
            startDay = 0;
            if (flags == null) return false;

            foreach (string flag in flags)
            {
                if (flag == null || !flag.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (int.TryParse(flag.Substring(Prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out startDay) && startDay >= 0) return true;
            }
            startDay = 0;
            return false;
        }
    }

    /// <summary>
    /// What the page of the chronicle asks of the game (design.md, "Страница «Хроника отрасли»"): the state of the calendar and of the services in the section of the
    /// save, the settings, and the words and figures in the language and the look of the game. The game gives the real one (<c>ChronicleWorld</c>), the tests a fake.
    /// </summary>
    public interface IChronicleWorld
    {
        /// <summary>The day of the game, days from 1971-01-01.</summary>
        int Today { get; }

        /// <summary>The valid entries of the calendar.</summary>
        IReadOnlyList<CalendarEntry> Calendar { get; }

        /// <summary>The section of the save: the states of the entries and of the services.</summary>
        HistSaveData Data { get; }

        /// <summary>The intensity setting (0.5 to 1.5): the lasting effects are shown the size they have.</summary>
        float Intensity { get; }

        /// <summary>The setting <c>spoilers</c>: the year of the three nearest entries is shown.</summary>
        bool Spoilers { get; }

        /// <summary>The switch of a category.</summary>
        bool IsCategoryEnabled(string category);

        /// <summary>The standard timed effects of an event of the pack (null when there is no such event).</summary>
        IReadOnlyList<CatchUpEffect> EffectsOf(string eventId);

        /// <summary>The label of the choice the player made in an event of the entry; null when there was none.</summary>
        string ChoiceMadeFor(CalendarEntry entry);

        // ---- words ----

        /// <summary>The text of a string key with its arguments (<c>ModL10n.L</c>).</summary>
        string Text(string key, params string[] args);

        /// <summary>The key has a text.</summary>
        bool HasText(string key);

        /// <summary>The form of a key that fits <paramref name="n"/> (<c>ModL10n.Plural</c>); <c>{0}</c> is the number.</summary>
        string Plural(string key, long n, params string[] args);

        /// <summary>The name of a company of the game; the player's too. Empty for one that is not in the game.</summary>
        string CompanyName(string companyKey);

        /// <summary>
        /// The title of an entry in the current language. The titles of some entries name the companies (<c>{1}</c> the one the event was for, <c>{2}</c> the other one), so the
        /// names of the game are given; both may be empty.
        /// </summary>
        string EntryTitle(CalendarEntry entry, string subjectName, string secondName);

        // ---- figures ----

        /// <summary>A date as the game writes it (<c>DateFormat.Current</c>).</summary>
        string Date(int day);

        /// <summary>The month and the year: <c>май 2014</c>.</summary>
        string MonthYear(int day);

        /// <summary>A sum of money as the game writes it.</summary>
        string Money(float amount);

        /// <summary>The performance of a CPU as the game writes it.</summary>
        string Ips(float ips);

        /// <summary>The name of a channel of <c>ModModifiers</c> in the current language.</summary>
        string ChannelName(string channel);

        /// <summary>The name of a segment (<c>highend</c>, <c>midrange</c>, <c>lowend</c>, <c>industries</c>).</summary>
        string SegmentName(string segment);

        /// <summary>The name of a war, a case or a dispute by the id its calendar entry gave; null when none is known.</summary>
        string NameOfId(string kind, string id);
    }

    /// <summary>
    /// The lines of the three tabs of the page "Хроника отрасли" (FR-15; design.md, Component 10), made from the section of the save and the calendar. Pure: the page only
    /// draws them. Nothing here changes the game, the section or the settings.
    /// <list type="bullet">
    /// <item><b>Past.</b> The entries that fired, expired, were switched off or failed, the latest first: the date, the title, who was in the roles ("вы", the companies by the
    /// names of the game) and the outcome (and the choice the player made). With <c>spoilers</c> on, a last line gives the year of the three nearest entries - the year only.
    /// Entries of the future are never listed otherwise.</item>
    /// <item><b>Active.</b> The standard timed effects of the stages that have played, with the days left (the day of the stage plus the duration of the effect, counted from
    /// the definitions of the pack; the intensity applied), and the same for an entry that was caught up at the start of the game; then the export rules (the threshold, the
    /// share, the exceptions), the price wars and the races.</item>
    /// <item><b>Cases.</b> The open antitrust cases ("Еврокомиссия против вас: 1 млрд, апелляция, решение ~ май 2014, уплачено 0, гарантия 10 млн в год"), the suits of the
    /// victims and the patent disputes: the stage, the sums, the nearest date.</item>
    /// </list>
    /// </summary>
    public static class ChronicleModel
    {
        /// <summary>How many entries of the future the setting <c>spoilers</c> names.</summary>
        public const int SpoilerCount = 3;

        /// <summary>How many companies a line names before it says "и другие".</summary>
        public const int MaxNamedCompanies = 4;

        /// <summary>The lines of a tab. Never null; an empty list means there is nothing to show (the page says so).</summary>
        public static List<ChronicleRow> Rows(ChronicleTab tab, IChronicleWorld world)
        {
            if (world == null) return new List<ChronicleRow>();

            switch (tab)
            {
                case ChronicleTab.Active: return Active(world);
                case ChronicleTab.Cases: return Cases(world);
                default: return Past(world);
            }
        }

        /// <summary>The key of the words that stand for an empty tab.</summary>
        public static string EmptyKey(ChronicleTab tab)
        {
            switch (tab)
            {
                case ChronicleTab.Active: return "hist.chronicle.empty.active";
                case ChronicleTab.Cases: return "hist.chronicle.empty.cases";
                default: return "hist.chronicle.empty.past";
            }
        }

        // ---- the past ----

        public static List<ChronicleRow> Past(IChronicleWorld world)
        {
            var rows = new List<ChronicleRow>();
            HistSaveData data = world.Data ?? new HistSaveData();

            foreach (CalendarEntry entry in world.Calendar)
            {
                EntryStateDto state = HistSaveCodec.FindEntry(data, entry.Id);
                if (state == null) continue;

                var status = (EntryStatus)state.status;
                if (status != EntryStatus.Fired && status != EntryStatus.Expired && status != EntryStatus.Disabled && status != EntryStatus.Error) continue;

                int day = status == EntryStatus.Fired ? state.firedDay : PlannedDay(entry, state);
                string title = world.Text("hist.chronicle.past.title", world.Date(day), TitleOf(entry, state, world));
                string who = Who(state, world);
                string outcome = Outcome(entry, status, world);
                string text = who.Length == 0 ? outcome : world.Text("hist.chronicle.past.text", who, outcome);

                rows.Add(new ChronicleRow(title, text) { Order = day, Attention = status == EntryStatus.Error });
            }

            // The latest first; the same day by id.
            rows.Sort((a, b) =>
            {
                int byDay = b.Order.CompareTo(a.Order);
                return byDay != 0 ? byDay : string.CompareOrdinal(a.Title, b.Title);
            });

            if (world.Spoilers)
            {
                string years = NearestYears(world, data);
                if (years.Length > 0) rows.Insert(0, new ChronicleRow(world.Text("hist.chronicle.soon"), years));
            }
            return rows;
        }

        /// <summary>The day an entry that did not fire was meant for: the anchor moved by its jitter, held to the window.</summary>
        internal static int PlannedDay(CalendarEntry entry, EntryStateDto state)
        {
            int day = entry.Anchor.Days + (state == null ? 0 : state.jitter);
            if (entry.WindowFrom.IsSet && day < entry.WindowFrom.Days) day = entry.WindowFrom.Days;
            if (entry.WindowTo.IsSet && day > entry.WindowTo.Days) day = entry.WindowTo.Days;
            return day;
        }

        /// <summary>
        /// The title of an entry with the names of its companies: the company the first fire was for is <c>{1}</c>, the first company of another role of the entry <c>{2}</c>
        /// (the same two the dispatcher gives the event, see <see cref="DispatcherCore.BuildArgs"/>). The names are the game's, the player's company included.
        /// </summary>
        public static string TitleOf(CalendarEntry entry, EntryStateDto state, IChronicleWorld world)
        {
            string subjectKey = null;
            string role = FirstFireRole(entry);
            if (string.IsNullOrEmpty(role) || role == RoleKinds.Global || role == RoleKinds.Player) subjectKey = CompanyKey.Player;
            else subjectKey = FirstKeyOf(state, role);

            if (subjectKey == null && entry.Roles != null && entry.Roles.Count > 0 && entry.Roles[0] != null) subjectKey = FirstKeyOf(state, entry.Roles[0].Name);

            string secondKey = null;
            if (entry.Roles != null)
            {
                foreach (RoleSpec spec in entry.Roles)
                {
                    string key = spec == null ? null : FirstKeyOf(state, spec.Name);
                    if (key != null && !string.Equals(key, subjectKey, StringComparison.Ordinal))
                    {
                        secondKey = key;
                        break;
                    }
                }
            }

            string subject = subjectKey == null ? string.Empty : world.CompanyName(subjectKey) ?? string.Empty;
            string second = secondKey == null ? string.Empty : world.CompanyName(secondKey) ?? string.Empty;
            return world.EntryTitle(entry, subject, second);
        }

        private static string FirstFireRole(CalendarEntry entry)
        {
            if (entry.Stages == null || entry.Stages.Count == 0 || entry.Stages[0] == null || entry.Stages[0].Fires == null) return null;
            foreach (FireSpec fire in entry.Stages[0].Fires)
            {
                if (fire != null && !string.IsNullOrEmpty(fire.Role)) return fire.Role;
            }
            return null;
        }

        private static string FirstKeyOf(EntryStateDto state, string role)
        {
            if (state == null || state.targets == null || string.IsNullOrEmpty(role)) return null;
            foreach (RoleTargetsDto targets in state.targets)
            {
                if (targets != null && targets.role == role && targets.companyKeys != null && targets.companyKeys.Count > 0) return targets.companyKeys[0];
            }
            return null;
        }

        private static string Who(EntryStateDto state, IChronicleWorld world)
        {
            var names = new List<string>();
            if (state.targets != null)
            {
                foreach (RoleTargetsDto role in state.targets)
                {
                    if (role == null || role.companyKeys == null) continue;
                    foreach (string key in role.companyKeys)
                    {
                        string name = NameOf(key, world);
                        if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                    }
                }
            }
            if (names.Count == 0) return string.Empty;

            if (names.Count > MaxNamedCompanies)
            {
                int others = names.Count - MaxNamedCompanies;
                names.RemoveRange(MaxNamedCompanies, others);
                names.Add(world.Text("hist.chronicle.others", others.ToString(CultureInfo.InvariantCulture)));
            }
            return string.Join(", ", names.ToArray());
        }

        private static string Outcome(CalendarEntry entry, EntryStatus status, IChronicleWorld world)
        {
            switch (status)
            {
                case EntryStatus.Fired:
                    string choice = world.ChoiceMadeFor(entry);
                    string fired = world.Text("hist.chronicle.outcome.fired");
                    return string.IsNullOrEmpty(choice) ? fired : world.Text("hist.chronicle.outcome.choice", fired, choice);
                case EntryStatus.Expired:
                    return world.Text("hist.chronicle.outcome.expired");
                case EntryStatus.Disabled:
                    return world.Text("hist.chronicle.outcome.disabled");
                default:
                    return world.Text("hist.chronicle.outcome.error");
            }
        }

        /// <summary>The years of the three nearest entries that have not come (spoilers on): <c>1995, 1995, 1997</c>. Only the year; categories that are off do not count.</summary>
        private static string NearestYears(IChronicleWorld world, HistSaveData data)
        {
            var coming = new List<KeyValuePair<int, CalendarEntry>>();
            foreach (CalendarEntry entry in world.Calendar)
            {
                EntryStateDto state = HistSaveCodec.FindEntry(data, entry.Id);
                if (state == null || state.status != (int)EntryStatus.Pending || !entry.Anchor.IsSet) continue;
                if (!world.IsCategoryEnabled(entry.Category)) continue;

                int day = PlannedDay(entry, state);
                if (day <= world.Today) continue;
                coming.Add(new KeyValuePair<int, CalendarEntry>(day, entry));
            }

            coming.Sort((a, b) =>
            {
                int byDay = a.Key.CompareTo(b.Key);
                return byDay != 0 ? byDay : string.CompareOrdinal(a.Value.Id, b.Value.Id);
            });

            var years = new List<string>();
            for (int i = 0; i < coming.Count && i < SpoilerCount; i++) years.Add(coming[i].Value.Anchor.Year.ToString(CultureInfo.InvariantCulture));
            return string.Join(", ", years.ToArray());
        }

        // ---- what is in force ----

        public static List<ChronicleRow> Active(IChronicleWorld world)
        {
            var rows = new List<ChronicleRow>();
            HistSaveData data = world.Data ?? new HistSaveData();

            foreach (CalendarEntry entry in world.Calendar)
            {
                EntryStateDto state = HistSaveCodec.FindEntry(data, entry.Id);
                if (state == null) continue;

                var status = (EntryStatus)state.status;
                if (status == EntryStatus.Fired)
                {
                    for (int k = 0; k < entry.Stages.Count && k < state.stagesFired; k++)
                    {
                        StageSpec stage = entry.Stages[k];
                        if (stage == null) continue;
                        AddEffects(rows, world, entry, state, stage, state.firedDay + stage.OffsetDays, int.MaxValue);
                    }
                }
                else if (status == EntryStatus.SkippedBeforeStart)
                {
                    int startDay;
                    if (!CatchUpMarks.TryRead(state.flags, out startDay)) continue;

                    for (int k = 0; k < entry.Stages.Count; k++)
                    {
                        StageSpec stage = entry.Stages[k];
                        if (stage == null || !entry.Anchor.IsSet) continue;
                        AddEffects(rows, world, entry, state, stage, entry.Anchor.Days + stage.OffsetDays, startDay);
                    }
                }
            }

            // The effects that end first come first; the same end in the order of the calendar.
            for (int i = 0; i < rows.Count; i++) rows[i].Sequence = i;
            rows.Sort((a, b) =>
            {
                int byDays = a.Order.CompareTo(b.Order);
                return byDays != 0 ? byDays : a.Sequence.CompareTo(b.Sequence);
            });

            AddExportRules(rows, world, data);
            AddPriceWars(rows, world, data);
            AddRaces(rows, world, data);
            return rows;
        }

        /// <summary>
        /// The lines for the timed effects of a stage: one per effect with days left. <paramref name="begins"/> is the day the stage played; a stage caught up at the start of the game
        /// counts only if it began by <paramref name="startedBy"/> (the planner's rule: a stage that begins later was never applied).
        /// </summary>
        private static void AddEffects(List<ChronicleRow> rows, IChronicleWorld world, CalendarEntry entry, EntryStateDto state, StageSpec stage, int begins, int startedBy)
        {
            if (stage.Fires == null || begins > startedBy) return;

            string title = null;

            foreach (FireSpec fire in stage.Fires)
            {
                if (fire == null || string.IsNullOrEmpty(fire.EventId)) continue;

                IReadOnlyList<CatchUpEffect> effects = world.EffectsOf(fire.EventId);
                if (effects == null) continue;

                foreach (CatchUpEffect effect in effects)
                {
                    if (effect == null || !CatchUpEffectTypes.IsCaughtUp(effect.Type) || effect.DurationDays <= 0) continue;

                    int remaining = begins + effect.DurationDays - world.Today;
                    if (remaining <= 0) continue;

                    if (title == null) title = TitleOf(entry, state, world);
                    string line = EffectLine(effect, world);
                    string days = world.Plural("hist.fx.days", remaining);
                    rows.Add(new ChronicleRow(title, world.Text("hist.chronicle.active.text", line, days)) { Order = remaining });
                }
            }
        }

        private static string EffectLine(CatchUpEffect effect, IChronicleWorld world)
        {
            float multiplier = IntensityMath.Scale(effect.Multiplier, world.Intensity, effect.Scalable);
            string change = SignedPercent(multiplier);

            if (effect.Type == CatchUpEffectTypes.MarketDemand)
            {
                string segment = string.IsNullOrEmpty(effect.Segment) || string.Equals(effect.Segment, "all", StringComparison.OrdinalIgnoreCase)
                    ? world.Text("hist.chronicle.segment.all")
                    : world.SegmentName(effect.Segment);
                return world.Text("hist.chronicle.fx.demand", segment, change);
            }

            string channel = effect.Type == CatchUpEffectTypes.FactoryOutput ? CatchUpEffectTypes.FactoryOutputChannel : effect.Channel;
            return world.Text("hist.chronicle.fx.channel", world.ChannelName(channel), change);
        }

        /// <summary>A multiplier as a change in percent with its sign: 1.25 gives <c>+25 %</c>, 0.9 gives <c>-10 %</c>, 1 gives <c>0 %</c>.</summary>
        public static string SignedPercent(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) return "0 %";

            double percent = Math.Round((multiplier - 1.0) * 100.0, MidpointRounding.AwayFromZero);
            string number = Math.Abs(percent).ToString("0", CultureInfo.InvariantCulture);
            if (percent > 0) return "+" + number + " %";
            if (percent < 0) return "-" + number + " %";
            return "0 %";
        }

        private static void AddExportRules(List<ChronicleRow> rows, IChronicleWorld world, HistSaveData data)
        {
            foreach (ExportRuleDto rule in data.exportRules)
            {
                if (rule == null) continue;

                string until = Until(world, rule.endDay);
                string text = world.Text("hist.chronicle.export.text", world.Ips(rule.threshold), Percent(rule.blockedShare), until);

                var exempt = new List<string>();
                if (rule.exemptions != null)
                {
                    foreach (ExemptionDto exemption in rule.exemptions)
                    {
                        if (exemption == null) continue;
                        string name = NameOf(exemption.companyKey, world);
                        if (name.Length == 0) continue;
                        string kind = world.HasText("hist.chronicle.exempt." + exemption.kind) ? world.Text("hist.chronicle.exempt." + exemption.kind) : exemption.kind;
                        exempt.Add(world.Text("hist.chronicle.exempt.line", name, kind));
                    }
                }
                if (exempt.Count > 0) text += world.Text("hist.chronicle.export.exceptions", string.Join(", ", exempt.ToArray()));

                string title = world.NameOfId("export", rule.ruleId) ?? world.Text("hist.chronicle.export.title");
                rows.Add(new ChronicleRow(title, text));
            }
        }

        private static void AddPriceWars(List<ChronicleRow> rows, IChronicleWorld world, HistSaveData data)
        {
            foreach (PriceWarDto war in data.priceWars)
            {
                if (war == null) continue;

                string title = world.NameOfId("war", war.warId) ?? world.Text("hist.chronicle.war.title");
                rows.Add(new ChronicleRow(title, world.Text("hist.chronicle.war.text", Until(world, war.endDay))));
            }
        }

        private static void AddRaces(List<ChronicleRow> rows, IChronicleWorld world, HistSaveData data)
        {
            foreach (RaceDto race in data.races)
            {
                if (race == null) continue;

                string key = "hist.chronicle.race." + race.attribute;
                string title = world.HasText(key) ? world.Text(key) : world.Text("hist.chronicle.race.other");
                float bonus = race.bonus;
                rows.Add(new ChronicleRow(title, world.Text("hist.chronicle.race.text", race.topN.ToString(CultureInfo.InvariantCulture), SignedPercent(bonus), Until(world, race.endDay))));
            }
        }

        // ---- cases and disputes ----

        public static List<ChronicleRow> Cases(IChronicleWorld world)
        {
            var rows = new List<ChronicleRow>();
            HistSaveData data = world.Data ?? new HistSaveData();

            foreach (CaseDto c in data.cases)
            {
                if (c == null || !CaseStages.IsOpen(c.stage)) continue;
                rows.Add(CaseRow(c, world));
            }

            foreach (SettlementDto suit in data.settlements)
            {
                if (suit == null) continue;

                string title = world.Text("hist.chronicle.settlement.title", NameOf(suit.plaintiffKey, world), NameOf(suit.defendantKey, world, true));
                string text = world.Text("hist.chronicle.settlement.text", world.Money(suit.legalPerMonth), suit.resolveDay > 0 ? world.MonthYear(suit.resolveDay) : "-");
                rows.Add(new ChronicleRow(title, text));
            }

            foreach (DisputeDto dispute in data.disputes)
            {
                if (dispute == null) continue;
                rows.Add(DisputeRow(dispute, world));
            }
            return rows;
        }

        /// <summary>
        /// A case as one line: <c>Еврокомиссия против вас: 1 млрд, апелляция, решение ~ май 2014, уплачено 0, гарантия 10 млн в год</c>. The sum, the stage, the date of the ruling
        /// (or of the new fine), what was paid (and put into the reserve) and the yearly fee of the bank guarantee, each where it applies.
        /// </summary>
        private static ChronicleRow CaseRow(CaseDto c, IChronicleWorld world)
        {
            string regulator = world.NameOfId("case", c.caseId) ?? world.Text("hist.chronicle.case.regulator");
            string title = CompanyKey.IsPlayer(c.targetKey)
                ? world.Text("hist.chronicle.case.vsPlayer", regulator)
                : world.Text("hist.chronicle.case.vsCompany", regulator, NameOf(c.targetKey, world));

            var parts = new List<string>();
            if (c.fine > 0f) parts.Add(world.Money(c.fine));
            parts.Add(world.Text("hist.chronicle.case.stage." + c.stage.ToString(CultureInfo.InvariantCulture)));

            switch (c.stage)
            {
                case CaseStages.Appeal:
                case CaseStages.Provision:
                case CaseStages.SecondAppeal:
                case CaseStages.ConsentFight:
                    if (c.rulingDay > 0) parts.Add(world.Text("hist.chronicle.case.ruling", world.MonthYear(c.rulingDay)));
                    break;
                case CaseStages.Reimposition:
                    if (c.reimposeDay > 0) parts.Add(world.Text("hist.chronicle.case.reimpose", world.MonthYear(c.reimposeDay)));
                    break;
            }

            if (c.fine > 0f && !CaseStages.IsAwaitingChoice(c.stage)) parts.Add(world.Text("hist.chronicle.case.paid", world.Money(c.paid + c.provisioned)));
            if (c.guaranteeFeePerMonth > 0f && (c.stage == CaseStages.Appeal || c.stage == CaseStages.SecondAppeal))
                parts.Add(world.Text("hist.chronicle.case.guarantee", world.Money(c.guaranteeFeePerMonth * 12f)));

            return new ChronicleRow(title, string.Join(", ", parts.ToArray())) { Attention = CaseStages.IsAwaitingChoice(c.stage) && CompanyKey.IsPlayer(c.targetKey) };
        }

        private static ChronicleRow DisputeRow(DisputeDto d, IChronicleWorld world)
        {
            string title = world.Text("hist.chronicle.dispute.title", NameOf(d.plaintiffKey, world), NameOf(d.defendantKey, world, true));

            var parts = new List<string>();
            string modeKey = "hist.chronicle.dispute.mode." + d.mode;
            parts.Add(world.HasText(modeKey) ? world.Text(modeKey) : world.Text("hist.chronicle.dispute.mode.other"));

            string choiceKey = "hist.chronicle.dispute.choice." + d.choice;
            if (!string.IsNullOrEmpty(d.choice) && world.HasText(choiceKey)) parts.Add(world.Text(choiceKey));
            else parts.Add(world.Text("hist.chronicle.dispute.choice.waiting"));

            if (d.resolveDay > 0) parts.Add(world.Text("hist.chronicle.case.ruling", world.MonthYear(d.resolveDay)));
            else if (d.endDay > 0) parts.Add(world.Text("hist.chronicle.dispute.until", world.MonthYear(d.endDay)));

            return new ChronicleRow(title, string.Join(", ", parts.ToArray()));
        }

        // ---- small helpers ----

        /// <summary>The name of a company as a line names it: "вы" for the player, or "вас" (<paramref name="afterAgainst"/>) where the language wants the other case.</summary>
        private static string NameOf(string companyKey, IChronicleWorld world, bool afterAgainst = false)
        {
            if (string.IsNullOrEmpty(companyKey)) return string.Empty;
            if (CompanyKey.IsPlayer(companyKey)) return world.Text(afterAgainst ? "hist.chronicle.you.against" : "hist.chronicle.you");
            return world.CompanyName(companyKey) ?? string.Empty;
        }

        private static string Until(IChronicleWorld world, int endDay)
        {
            return endDay > 0 ? world.Text("hist.chronicle.until", world.Date(endDay)) : world.Text("hist.chronicle.open");
        }

        private static string Percent(float share)
        {
            return (share * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        }
    }
}
