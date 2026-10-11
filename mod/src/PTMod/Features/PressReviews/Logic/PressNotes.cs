using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// What the conditions of the quarterly notes are judged on (FR-10, design.md Component 9): the growth of each segment's leader and the change of its median
    /// price over the last full year, whether the market has just moved to PGA, the player's reputation over 12 months and the company that has just entered the
    /// field of view of the press. Plain values; a number that is not known is <see cref="float.NaN"/> (a condition on it does not hold). The per-segment arrays
    /// are indexed by <c>(int)Segment</c> 0…3.
    /// </summary>
    public sealed class NoteFacts
    {
        public float[] LeaderGrowth = Unknown();
        public float[] MedianPriceChange = Unknown();

        /// <summary>A PGA CPU came to the market within the last year (for the first time).</summary>
        public bool PgaShift;

        /// <summary>The change of the player's reputation over 12 months, in points; NaN when the player has no history.</summary>
        public float ReputationChange = float.NaN;

        /// <summary>The name of the player's company (the note about its reputation).</summary>
        public string PlayerCompanyName = string.Empty;

        /// <summary>The name of the company that published its first review in the last quarter; empty for none.</summary>
        public string NewEntrantName = string.Empty;

        private static float[] Unknown()
        {
            return new[] { float.NaN, float.NaN, float.NaN, float.NaN };
        }
    }

    /// <summary>Judges the condition of a note against the facts.</summary>
    public static class NoteEvaluator
    {
        /// <summary>
        /// Does the condition hold? <c>leaderGrowth</c> and <c>medianPriceChange</c> need the value of their segment to be known and between <c>min</c> and
        /// <c>max</c> (the bounds that the file has); <c>reputationTrend</c> the same for the change of the player's reputation; <c>pgaShift</c> the flag;
        /// <c>newEntrant</c> a name; <c>segmentFocus</c> always holds (a static note, its rate is the cooldown).
        /// </summary>
        public static bool Holds(NoteCondition condition, NoteFacts facts)
        {
            if (condition == null) throw new ArgumentNullException("condition");
            if (facts == null) throw new ArgumentNullException("facts");

            switch (condition.Type)
            {
                case NoteConditionType.LeaderGrowth: return InRange(SegmentValue(facts.LeaderGrowth, condition.Segment), condition);
                case NoteConditionType.MedianPriceChange: return InRange(SegmentValue(facts.MedianPriceChange, condition.Segment), condition);
                case NoteConditionType.PgaShift: return facts.PgaShift;
                case NoteConditionType.SegmentFocus: return true;
                case NoteConditionType.ReputationTrend: return InRange(facts.ReputationChange, condition);
                case NoteConditionType.NewEntrant: return !string.IsNullOrEmpty(facts.NewEntrantName);
                default: return false;
            }
        }

        /// <summary>The value of the segment from a per-segment array; NaN for a segment outside 0…3 (<c>Any</c>) or a short array.</summary>
        public static float SegmentValue(float[] values, PTMod.Core.Segment segment)
        {
            int index = (int)segment;
            if (values == null || index < 0 || index > 3 || index >= values.Length) return float.NaN;
            return values[index];
        }

        private static bool InRange(float value, NoteCondition condition)
        {
            if (!MarketTrends.HasValue(value)) return false;
            if (condition.Min.HasValue && value < condition.Min.Value) return false;
            if (condition.Max.HasValue && value > condition.Max.Value) return false;
            return true;
        }
    }

    /// <summary>The rests of the notes (<see cref="PressState.noteCooldowns"/>): a note that was used rests for its number of quarters.</summary>
    public static class NoteCooldowns
    {
        /// <summary>The months of a quarter.</summary>
        public const int MonthsPerQuarter = 3;

        /// <summary>Is the note resting on <paramref name="today"/>? It rests until the day in its entry, and may be chosen again on that day.</summary>
        public static bool IsResting(IList<NoteCooldown> cooldowns, string noteId, DateTime today)
        {
            if (cooldowns == null || string.IsNullOrEmpty(noteId)) return false;

            for (int i = 0; i < cooldowns.Count; i++)
            {
                NoteCooldown entry = cooldowns[i];
                DateTime until;
                if (entry == null || !string.Equals(entry.noteId, noteId, StringComparison.Ordinal) || !PressDates.TryParse(entry.until, out until)) continue;
                if (today.Date < until.Date) return true;
            }
            return false;
        }

        /// <summary>The day a note used on <paramref name="today"/> may be chosen again: <paramref name="quarters"/> quarters later (at least one).</summary>
        public static DateTime UntilOf(DateTime today, int quarters)
        {
            int count = quarters < 1 ? 1 : quarters > 400 ? 400 : quarters;
            return today.Date.AddMonths(MonthsPerQuarter * count);
        }

        /// <summary>Sets the rest of a note (the entry of the note is replaced, there is one per note) and keeps the list within its limit.</summary>
        public static void Rest(IList<NoteCooldown> cooldowns, string noteId, DateTime until)
        {
            if (cooldowns == null) throw new ArgumentNullException("cooldowns");
            if (string.IsNullOrEmpty(noteId)) return;

            for (int i = cooldowns.Count - 1; i >= 0; i--)
            {
                if (cooldowns[i] != null && string.Equals(cooldowns[i].noteId, noteId, StringComparison.Ordinal)) cooldowns.RemoveAt(i);
            }
            cooldowns.Add(new NoteCooldown { noteId = noteId, until = PressDates.ToText(until) });
            while (cooldowns.Count > PressStateCodec.MaxNoteCooldowns) cooldowns.RemoveAt(0);
        }

        /// <summary>Forgets the rests that are over. Returns how many entries went.</summary>
        public static int Prune(IList<NoteCooldown> cooldowns, DateTime today)
        {
            if (cooldowns == null) return 0;

            int removed = 0;
            for (int i = cooldowns.Count - 1; i >= 0; i--)
            {
                DateTime until;
                if (cooldowns[i] == null || !PressDates.TryParse(cooldowns[i].until, out until) || today.Date >= until.Date)
                {
                    cooldowns.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
    }

    /// <summary>The choice of the notes of a quarter (FR-10 item 1, 3): up to two of those whose condition holds and which are not resting, by weight.</summary>
    public static class NoteSelection
    {
        /// <summary>The most notes in one letter.</summary>
        public const int MaxPerQuarter = 2;

        private const int Resolution = 1000000;

        /// <summary>The months in which the notes come: January, April, July and October.</summary>
        public static bool IsQuarterMonth(int month)
        {
            return month == 1 || month == 4 || month == 7 || month == 10;
        }

        /// <summary>The notes whose condition holds and which are not resting on <paramref name="today"/>, in the order of the file.</summary>
        public static List<NoteTemplate> Candidates(IReadOnlyList<NoteTemplate> notes, NoteFacts facts, IList<NoteCooldown> cooldowns, DateTime today)
        {
            var found = new List<NoteTemplate>();
            if (notes == null) return found;

            for (int i = 0; i < notes.Count; i++)
            {
                NoteTemplate note = notes[i];
                if (note == null || !(note.Weight > 0f) || float.IsInfinity(note.Weight)) continue;
                if (NoteCooldowns.IsResting(cooldowns, note.Id, today)) continue;
                if (NoteEvaluator.Holds(note.Condition, facts)) found.Add(note);
            }
            return found;
        }

        /// <summary>
        /// Up to <paramref name="max"/> candidates, drawn one after the other by weight without putting back (the heavier a note, the likelier it comes first).
        /// The same candidates and the same dice give the same notes. Draws one number per note chosen.
        /// </summary>
        public static List<NoteTemplate> Pick(IReadOnlyList<NoteTemplate> notes, NoteFacts facts, IList<NoteCooldown> cooldowns, DateTime today, int max, IRandomSource rng)
        {
            if (rng == null) throw new ArgumentNullException("rng");

            List<NoteTemplate> pool = Candidates(notes, facts, cooldowns, today);
            var picked = new List<NoteTemplate>();
            int limit = max < 0 ? 0 : max;

            while (picked.Count < limit && pool.Count > 0)
            {
                double total = 0.0;
                for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;

                double target = (double)rng.Range(0, Resolution) / Resolution * total;
                int chosen = pool.Count - 1;
                double sum = 0.0;
                for (int i = 0; i < pool.Count; i++)
                {
                    sum += pool[i].Weight;
                    if (target < sum)
                    {
                        chosen = i;
                        break;
                    }
                }
                picked.Add(pool[chosen]);
                pool.RemoveAt(chosen);
            }
            return picked;
        }
    }

    /// <summary>Turns a note into its text: the names of its arguments become the segment, the qualitative growth or change, or a company (FR-10 item 2).</summary>
    public static class NoteRenderer
    {
        /// <summary>The text of the note. Only the words and the tens of percent the player may read go in; an argument it does not know is empty.</summary>
        public static string Render(NoteTemplate note, NoteFacts facts, PressText text)
        {
            if (note == null) throw new ArgumentNullException("note");
            if (facts == null) throw new ArgumentNullException("facts");
            if (text == null) throw new ArgumentNullException("text");

            var args = new object[note.Args == null ? 0 : note.Args.Count];
            for (int i = 0; i < args.Length; i++) args[i] = ArgumentOf(note.Args[i], note, facts, text);
            return text(note.TextKey, args);
        }

        private static string ArgumentOf(string name, NoteTemplate note, NoteFacts facts, PressText text)
        {
            int segment = (int)note.Condition.Segment;
            switch (name)
            {
                case "segment":
                    return segment >= 0 && segment <= 3 ? text(PressWords.SegmentKey(segment)) : string.Empty;
                case "qualitativeGrowth":
                    return MarketTrends.Qualitative(NoteEvaluator.SegmentValue(facts.LeaderGrowth, note.Condition.Segment), text);
                case "qualitativeChange":
                    return MarketTrends.QualitativeChange(NoteEvaluator.SegmentValue(facts.MedianPriceChange, note.Condition.Segment), text);
                case "companyName":
                    return PressWords.Plain(note.Condition.Type == NoteConditionType.NewEntrant ? facts.NewEntrantName : facts.PlayerCompanyName);
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>What goes into the quarterly letter "Press: market review": its tag and the arguments of its title and body.</summary>
    public sealed class NotesMailContent
    {
        /// <summary><c>press:notes:&lt;yyyy-MM&gt;</c>.</summary>
        public string Tag = string.Empty;

        /// <summary>The arguments (the core fills the title and the body with the same list): 0 the notes, a paragraph each.</summary>
        public string[] Args = new string[0];
    }

    /// <summary>The letter of the notes of a quarter (FR-10 item 1, design.md "Письма"): one letter, one paragraph a note. Pure.</summary>
    public static class NotesMailText
    {
        /// <summary>The prefix of the tag of the letters with notes; the month follows.</summary>
        public const string TagPrefix = "press:notes:";

        /// <summary>The tag of the letter of the month of <paramref name="today"/>.</summary>
        public static string TagOf(DateTime today)
        {
            return TagPrefix + PressDates.ToMonthText(today);
        }

        public static NotesMailContent Build(DateTime today, IList<string> lines)
        {
            var paragraphs = new List<string>();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i])) paragraphs.Add(lines[i]);
                }
            }
            return new NotesMailContent { Tag = TagOf(today), Args = new[] { string.Join("\n\n", paragraphs.ToArray()) } };
        }
    }
}
