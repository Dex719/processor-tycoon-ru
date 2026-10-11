using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The game side of <see cref="IChronicleWorld"/> (design.md, "Страница «Хроника отрасли»"; TSK-022): the calendar and the section of the save of the running feature, the
    /// settings, the journal and the definitions of the event system, the words of <c>ModL10n</c> and the figures in the look of the game. The rules of the lines are
    /// <see cref="ChronicleModel"/>'s; this only answers its questions and never changes anything. A question that fails answers the empty thing (the page guards the whole build).
    /// </summary>
    internal sealed class ChronicleWorld : IChronicleWorld
    {
        private static readonly string[] NoArgs = new string[0];

        private readonly HistoricalEventsFeature _feature;

        internal ChronicleWorld(HistoricalEventsFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        public int Today
        {
            get
            {
                DateTime today = ModTicks.Today;
                return today == default(DateTime) ? 0 : SimpleDate.FromDateTime(today).Days;
            }
        }

        public IReadOnlyList<CalendarEntry> Calendar
        {
            get { return _feature.Calendar; }
        }

        public HistSaveData Data
        {
            get { return _feature.Save == null ? new HistSaveData() : _feature.Save.Data; }
        }

        public float Intensity
        {
            get { return HistSettings.Intensity; }
        }

        public bool Spoilers
        {
            get { return HistSettings.Spoilers; }
        }

        public bool IsCategoryEnabled(string category)
        {
            return HistSettings.IsCategoryEnabled(category);
        }

        public IReadOnlyList<CatchUpEffect> EffectsOf(string eventId)
        {
            return PackEffects.Of(eventId);
        }

        /// <summary>
        /// The choice the player made in an event of the entry: the latest <c>Resolved</c> or <c>AutoResolved</c> line of the journal for an event the entry fires, for the player, from the
        /// day the entry fired (the events of the pack are shared by several entries, so the day tells them apart).
        /// </summary>
        public string ChoiceMadeFor(CalendarEntry entry)
        {
            EntryStateDto state = HistSaveCodec.FindEntry(Data, entry.Id);
            if (state == null || state.firedDay <= 0) return null;

            var events = new HashSet<string>(StringComparer.Ordinal);
            foreach (StageSpec stage in entry.Stages)
            {
                if (stage == null || stage.Fires == null) continue;
                foreach (FireSpec fire in stage.Fires)
                {
                    if (fire != null && !string.IsNullOrEmpty(fire.EventId)) events.Add(fire.EventId);
                }
            }

            EventLogEntry latest = null;
            foreach (EventLogEntry line in EventLog.Entries)
            {
                if (line == null || string.IsNullOrEmpty(line.choiceId) || !events.Contains(line.eventId) || !CompanyKey.IsPlayer(line.companyKey)) continue;
                if (line.kind != EventLogKinds.Resolved && line.kind != EventLogKinds.AutoResolved) continue;
                if (line.day < state.firedDay || line.day > state.firedDay + 400) continue;
                latest = line;
            }
            if (latest == null) return null;

            EventDefinition definition = EventRegistry.Get(latest.eventId);
            if (definition != null && definition.choices != null)
            {
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && choice.id == latest.choiceId && !string.IsNullOrEmpty(choice.textKey)) return ModL10n.L(choice.textKey);
                }
            }
            return latest.choiceId;
        }

        // ---- words ----

        public string Text(string key, params string[] args)
        {
            return ModL10n.L(key, (object[])(args ?? NoArgs));
        }

        public bool HasText(string key)
        {
            return !string.IsNullOrEmpty(key) && ModL10n.Has(key);
        }

        public string Plural(string key, long n, params string[] args)
        {
            return ModL10n.Plural(key, n, (object[])(args ?? NoArgs));
        }

        public string CompanyName(string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey)) return string.Empty;

            ICompany company = ModCompanies.Resolve(companyKey);
            return company == null ? string.Empty : company.Name ?? string.Empty;
        }

        public string EntryTitle(CalendarEntry entry, string subjectName, string secondName)
        {
            if (entry.History == null || string.IsNullOrEmpty(entry.History.TitleKey)) return entry.Id;
            return ModL10n.L(entry.History.TitleKey, entry.Id, subjectName ?? string.Empty, secondName ?? string.Empty);
        }

        // ---- figures ----

        public string Date(int day)
        {
            try
            {
                return ProcessorTycoon.StringFormatter.DateToString(SimpleDate.FromDays(day).ToDateTime());
            }
            catch (Exception)
            {
                return SimpleDate.FromDays(day).ToString();
            }
        }

        public string MonthYear(int day)
        {
            SimpleDate date = SimpleDate.FromDays(day);
            return ModL10n.L("hist.month." + date.Month.ToString(CultureInfo.InvariantCulture)) + " " + date.Year.ToString(CultureInfo.InvariantCulture);
        }

        public string Money(float amount)
        {
            return HistText.Money(amount);
        }

        public string Ips(float ips)
        {
            return HistText.Ips(ips);
        }

        public string ChannelName(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return string.Empty;

            string key = "es.channel." + channel;
            return ModL10n.Has(key) ? ModL10n.L(key) : channel;
        }

        public string SegmentName(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return string.Empty;

            string key = "hist.segment." + segment.ToLowerInvariant();
            return ModL10n.Has(key) ? ModL10n.L(key) : segment;
        }

        /// <summary>
        /// The name of a case, war, rule or dispute: a regulator by the id of the case (<c>hist.chronicle.case.&lt;id&gt;</c>), otherwise the title of the entry whose fire carries
        /// the id as its first extra argument (the wars and the rules are named that way in the calendar).
        /// </summary>
        public string NameOfId(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (kind == "case")
            {
                // A regulator is named by the id of its case; a case without a name here is "the regulator".
                string key = "hist.chronicle.case." + id;
                return ModL10n.Has(key) ? ModL10n.L(key) : null;
            }

            foreach (CalendarEntry entry in _feature.Calendar)
            {
                if (EntryFiresId(entry, id)) return EntryTitle(entry, string.Empty, string.Empty);
            }
            return null;
        }

        private static bool EntryFiresId(CalendarEntry entry, string id)
        {
            foreach (StageSpec stage in entry.Stages)
            {
                if (stage == null || stage.Fires == null) continue;
                foreach (FireSpec fire in stage.Fires)
                {
                    if (fire != null && fire.ExtraArgs != null && fire.ExtraArgs.Length > 0 && string.Equals(fire.ExtraArgs[0], id, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }
    }
}
