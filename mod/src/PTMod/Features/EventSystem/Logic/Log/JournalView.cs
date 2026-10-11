using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>What the journal window shows of the entries (FR-8 item 3).</summary>
    public sealed class JournalFilter
    {
        /// <summary>"Только моя компания": the player's entries and the global effects.</summary>
        public bool OnlyMine;

        /// <summary>The category of the event; null or empty - all of them.</summary>
        public string Category;
    }

    /// <summary>A line of the tab "История".</summary>
    public sealed class JournalRow
    {
        public int Day;
        public string CompanyKey;
        public string Kind;

        /// <summary>The date, the company and the name of the event.</summary>
        public string Header;

        /// <summary>What happened: fired, chosen, chosen automatically, cancelled, an effect ended or failed.</summary>
        public string Outcome;

        /// <summary>The effects with the numbers that were shown and applied, one line each.</summary>
        public string[] Lines = new string[0];

        /// <summary>The row as one block of text.</summary>
        public string Text
        {
            get { return Lines.Length == 0 ? Header + " — " + Outcome : Header + " — " + Outcome + "\n" + string.Join("\n", Lines); }
        }
    }

    /// <summary>A line of the tab "Активные эффекты".</summary>
    public sealed class ActiveEffectRow
    {
        /// <summary>What the effect does and until when: <c>Скорость исследований +12 % — до 01.06.1986 (182 дн.)</c> (AC-8.1).</summary>
        public string Text;

        /// <summary>The company the effect acts on, or "весь рынок".</summary>
        public string Company;

        /// <summary>The event the effect belongs to (the source id of a <c>Direct</c> effect).</summary>
        public string Source;

        public string CompanyKey;
        public int EndDay;
        public int DaysLeft;

        /// <summary>The effect is frozen with its pack: its end date stays, nothing changes meanwhile.</summary>
        public bool Frozen;
    }

    /// <summary>
    /// The text of the journal window (design.md "UI", FR-8): the rows of the history, newest first, and the active effects. Everything is built from the saved
    /// keys, arguments and numbers at the moment of drawing, so a change of language redraws the same rows with the same numbers (FR-8 item 5, AC-8.2 keeps the
    /// store to its limit). Pure: the window only draws what this returns.
    /// </summary>
    public sealed class JournalView
    {
        private readonly ITextSource _text;
        private readonly Func<string, EventDefinition> _definitionOf;
        private readonly Func<AppliedEffect, int, float> _valueAt;
        private readonly Func<AppliedEffect, string> _sourceOf;

        /// <param name="text">The tables and the way the game writes dates.</param>
        /// <param name="definitionOf">The event by id (names, categories, the captions of choices); null - none is known.</param>
        /// <param name="valueAt">The current value of an effect on a day: an effect that changes (a curve) says so, any other gives its saved value; null - the saved value.</param>
        /// <param name="sourceOf">The name of the event an effect belongs to; null - none.</param>
        public JournalView(ITextSource text, Func<string, EventDefinition> definitionOf, Func<AppliedEffect, int, float> valueAt, Func<AppliedEffect, string> sourceOf)
        {
            if (text == null) throw new ArgumentNullException("text");
            _text = text;
            _definitionOf = definitionOf ?? (id => null);
            _valueAt = valueAt ?? ((effect, day) => effect.value);
            _sourceOf = sourceOf ?? (effect => string.Empty);
        }

        // ---- history ----

        /// <summary>The rows of the history, the newest first.</summary>
        public IList<JournalRow> History(IList<EventLogEntry> entries, JournalFilter filter)
        {
            var rows = new List<JournalRow>();
            if (entries == null) return rows;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                EventLogEntry entry = entries[i];
                if (entry == null || !Passes(entry, filter)) continue;
                rows.Add(Row(entry));
            }
            return rows;
        }

        /// <summary>The categories of the events in the entries, sorted; the choices of the filter.</summary>
        public IList<string> Categories(IList<EventLogEntry> entries)
        {
            var set = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            if (entries != null)
            {
                foreach (EventLogEntry entry in entries)
                {
                    EventDefinition definition = entry != null ? _definitionOf(entry.eventId) : null;
                    if (definition != null && !string.IsNullOrEmpty(definition.category)) set[definition.category] = true;
                }
            }
            return new List<string>(set.Keys);
        }

        private bool Passes(EventLogEntry entry, JournalFilter filter)
        {
            if (filter == null) return true;
            if (filter.OnlyMine && !IsMine(entry.companyKey)) return false;
            if (!string.IsNullOrEmpty(filter.Category))
            {
                EventDefinition definition = _definitionOf(entry.eventId);
                if (definition == null || !string.Equals(definition.category, filter.Category, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private JournalRow Row(EventLogEntry entry)
        {
            EventDefinition definition = _definitionOf(entry.eventId);
            string title = definition != null ? EventMailFormatter.TitleOf(new EventInstanceDto { eventId = entry.eventId }, definition, _text) : entry.eventId;
            if (string.IsNullOrEmpty(title)) title = entry.eventId;

            var lines = new List<string>();
            if (entry.effects != null)
            {
                foreach (EffectRecord record in entry.effects)
                {
                    if (record != null) lines.Add(RecordLine(record));
                }
            }

            return new JournalRow
            {
                Day = entry.day,
                CompanyKey = entry.companyKey,
                Kind = entry.kind,
                Header = _text.Date(entry.day) + "  " + CompanyName(entry.companyKey) + ": " + title,
                Outcome = Outcome(entry, definition),
                Lines = lines.ToArray(),
            };
        }

        private string Outcome(EventLogEntry entry, EventDefinition definition)
        {
            switch (entry.kind)
            {
                case EventLogKinds.Fired: return _text.L("es.ui.journal.fired");
                case EventLogKinds.Resolved: return _text.L("es.ui.journal.resolved", EventMailFormatter.ChoiceLabel(definition, entry.choiceId, _text));
                case EventLogKinds.AutoResolved: return _text.L("es.ui.auto_chosen", EventMailFormatter.ChoiceLabel(definition, entry.choiceId, _text));
                case EventLogKinds.Cancelled: return _text.L("es.ui.journal.cancelled");
                case EventLogKinds.EffectEnded: return _text.L("es.ui.journal.ended");
                case EventLogKinds.EffectFailed: return _text.L("es.ui.journal.failed");
                default: return entry.kind ?? string.Empty;
            }
        }

        /// <summary>The line of a journal record: its saved text, the mark of a cut number, and the note that a chance did not come up.</summary>
        public string RecordLine(EffectRecord record)
        {
            string line;
            if (!string.IsNullOrEmpty(record.textKey)) line = _text.L(record.textKey, EventMailFormatter.WithChannelName(record.channel, record.textArgs, _text));
            else line = _text.L("es.ui.journal.raw", record.type ?? string.Empty, string.IsNullOrEmpty(record.channel) ? EffectFormat.Signed(record.value, 2) : Percent(record.value));

            if (record.clamped) line += " " + _text.L("es.fx.clamped");
            if (!record.hit) line += " " + _text.L("es.ui.journal.missed");
            return line;
        }

        // ---- active effects ----

        /// <summary>The effects that run now (active or frozen), soonest to end first; the filter "Только моя компания" keeps the player's and the global ones.</summary>
        public IList<ActiveEffectRow> Active(IList<AppliedEffect> effects, int today, JournalFilter filter)
        {
            var rows = new List<ActiveEffectRow>();
            if (effects == null) return rows;

            foreach (AppliedEffect effect in effects)
            {
                if (effect == null || effect.endDay <= 0) continue;
                bool frozen = string.Equals(effect.state, AppliedEffectStates.Suspended, StringComparison.Ordinal);
                if (!frozen && !string.Equals(effect.state, AppliedEffectStates.Active, StringComparison.Ordinal)) continue;
                if (filter != null && filter.OnlyMine && !IsMine(effect.companyKey)) continue;
                rows.Add(ActiveRow(effect, today, frozen));
            }
            rows.Sort((a, b) => a.EndDay != b.EndDay ? a.EndDay.CompareTo(b.EndDay) : string.CompareOrdinal(a.Text, b.Text));
            return rows;
        }

        private ActiveEffectRow ActiveRow(AppliedEffect effect, int today, bool frozen)
        {
            int left = Math.Max(0, effect.endDay - today);
            string what;
            if (!string.IsNullOrEmpty(effect.channel))
            {
                float value = _valueAt(effect, today);
                what = _text.L("es.ui.journal.channel", ChannelName(effect.channel), Percent(value));
            }
            else
            {
                what = EventMailFormatter.EffectLine(effect, _text);
            }

            string until = _text.L("es.ui.journal.until", _text.Date(effect.endDay), left.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string text = _text.L("es.ui.journal.active.row", what, until);
            if (frozen) text += " " + _text.L("es.ui.journal.frozen");

            return new ActiveEffectRow
            {
                Text = text,
                Company = string.Equals(effect.companyKey, EventCompanyKeys.Global, StringComparison.Ordinal) ? _text.L("es.ui.whole_market") : CompanyName(effect.companyKey),
                Source = _sourceOf(effect) ?? string.Empty,
                CompanyKey = effect.companyKey,
                EndDay = effect.endDay,
                DaysLeft = left,
                Frozen = frozen,
            };
        }

        // ---- words ----

        /// <summary>A multiplier as the journal writes a change: <c>+12 %</c>, <c>-40 %</c>.</summary>
        public static string Percent(float multiplier)
        {
            string text = EffectFormat.Percent(multiplier);
            return text.Substring(0, text.Length - 1) + " %";
        }

        private string ChannelName(string channel)
        {
            string name = _text.Find("es.channel." + channel);
            return string.IsNullOrEmpty(name) ? channel : name;
        }

        private string CompanyName(string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey)) return string.Empty;
            if (string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return _text.L("es.ui.whole_market");
            return _text.Company(companyKey);
        }

        private static bool IsMine(string companyKey)
        {
            return string.IsNullOrEmpty(companyKey) || CompanyKey.IsPlayer(companyKey) || string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal);
        }
    }
}
