using System;
using System.Globalization;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The numbers and names of a decision as the player reads them: the game's formatter of money, a count, a percent. A test brings a plain one.</summary>
    public interface IFlawFormat
    {
        /// <summary>A sum of money as the game writes it (<c>StringFormatter.FloatToMoney</c>).</summary>
        string Money(float amount);

        /// <summary>A count of units.</summary>
        string Count(long units);

        /// <summary>A share as a percent without the sign: <c>0.3</c> gives <c>30</c>.</summary>
        string Percent(float fraction);
    }

    /// <summary>Which of the three situations a decision window opens in (design.md Component 8).</summary>
    public enum FlawDecisionKind
    {
        /// <summary>The flaw is public (found by customers, the press, the model itself or another feature): four buttons, ignore among them.</summary>
        Public,

        /// <summary>Only the company's engineers know: four buttons; a quiet fix is cheaper, a leak hurts more.</summary>
        Internal,

        /// <summary>A cover-up leaked: three buttons, the way of ignoring is closed.</summary>
        Leaked,
    }

    /// <summary>
    /// Which event of the pack opens for a flaw and with what (FR-5, design.md Component 8): the six events <c>dfr.decide.*</c> (public, internal, leaked, each
    /// with and without the patch button), the choice that the timer applies, the arguments of the texts. Pure; <c>events.json</c> must say the same (a test
    /// compares them).
    /// <para>
    /// The arguments of the texts (<c>EventFireContext.Args</c>, <c>{0}</c>..<c>{10}</c>): the id of the record, the name of the CPU, the type of the flaw, the
    /// severity, the affected units, then the recall, the revision, the days of the revision, the patch, the loss in percent, the months of the patch.
    /// The first one is how the effects find the record again.
    /// </para>
    /// </summary>
    public static class FlawDecisionPlanner
    {
        /// <summary>The prefix of the ids of the events of the pack.</summary>
        public const string EventPrefix = "dfr.decide.";

        public const string PublicEvent = "dfr.decide.public";
        public const string PublicNoPatchEvent = "dfr.decide.public.nopatch";
        public const string InternalEvent = "dfr.decide.internal";
        public const string InternalNoPatchEvent = "dfr.decide.internal.nopatch";
        public const string LeakedEvent = "dfr.decide.leaked";
        public const string LeakedNoPatchEvent = "dfr.decide.leaked.nopatch";

        /// <summary>The ids of the choices of the events (the button ids).</summary>
        public const string RecallChoice = "recall";

        public const string SteppingChoice = "stepping";
        public const string PatchChoice = "patch";
        public const string IgnoreChoice = "ignore";

        /// <summary>The number of positional arguments of the texts.</summary>
        public const int ArgCount = 11;

        /// <summary>The place of the id of the record in the arguments.</summary>
        public const int RecordIdArg = 0;

        /// <summary>All six event ids.</summary>
        public static readonly string[] EventIds =
        {
            PublicEvent, PublicNoPatchEvent, InternalEvent, InternalNoPatchEvent, LeakedEvent, LeakedNoPatchEvent,
        };

        /// <summary>The event belongs to this pack (its id starts with <c>dfr.decide.</c>).</summary>
        public static bool IsDecisionEvent(string eventId)
        {
            return !string.IsNullOrEmpty(eventId) && eventId.StartsWith(EventPrefix, StringComparison.Ordinal);
        }

        /// <summary>The situation of a record that waits for a decision: a leaked cover-up, a public flaw, or a flaw only the company knows.</summary>
        public static FlawDecisionKind KindOf(FlawRecord record)
        {
            if (record == null) return FlawDecisionKind.Internal;
            if (record.Data.coverUp && record.Source == DiscoverySource.Leak) return FlawDecisionKind.Leaked;
            return record.State == FlawState.Public ? FlawDecisionKind.Public : FlawDecisionKind.Internal;
        }

        /// <summary>The event for the situation: without the patch button when the patch is not available (AC-5.2).</summary>
        public static string EventIdFor(FlawDecisionKind kind, bool patchAvailable)
        {
            switch (kind)
            {
                case FlawDecisionKind.Public: return patchAvailable ? PublicEvent : PublicNoPatchEvent;
                case FlawDecisionKind.Leaked: return patchAvailable ? LeakedEvent : LeakedNoPatchEvent;
                default: return patchAvailable ? InternalEvent : InternalNoPatchEvent;
            }
        }

        /// <summary>
        /// The choice the timer applies and the engine applies when it cannot show the window (FR-5 items 4 and 7): the patch if there is one, otherwise ignore;
        /// after a leak, where ignoring is closed, the patch if there is one, otherwise the revision.
        /// </summary>
        public static string DefaultChoiceFor(FlawDecisionKind kind, bool patchAvailable)
        {
            if (patchAvailable) return PatchChoice;
            return kind == FlawDecisionKind.Leaked ? SteppingChoice : IgnoreChoice;
        }

        /// <summary>The response behind a button id; <see cref="FlawResponse.None"/> for an id that is not one of the four.</summary>
        public static FlawResponse ResponseOfChoice(string choiceId)
        {
            switch (choiceId)
            {
                case RecallChoice: return FlawResponse.Recall;
                case SteppingChoice: return FlawResponse.Stepping;
                case PatchChoice: return FlawResponse.Patch;
                case IgnoreChoice: return FlawResponse.Ignore;
                default: return FlawResponse.None;
            }
        }

        /// <summary>The button id of a response; <c>null</c> for <see cref="FlawResponse.None"/>.</summary>
        public static string ChoiceOfResponse(FlawResponse response)
        {
            switch (response)
            {
                case FlawResponse.Recall: return RecallChoice;
                case FlawResponse.Stepping: return SteppingChoice;
                case FlawResponse.Patch: return PatchChoice;
                case FlawResponse.Ignore: return IgnoreChoice;
                default: return null;
            }
        }

        /// <summary>
        /// The arguments of the texts of the window (design.md Component 8). The money and the units are formatted by <paramref name="format"/>, the names are
        /// the player's language already. A missing quote gives zeros.
        /// </summary>
        public static string[] BuildArgs(FlawRecord record, FlawQuote quote, string cpuName, string typeName, string severityName, IFlawFormat format)
        {
            if (record == null) throw new ArgumentNullException("record");

            FlawQuote q = quote ?? new FlawQuote();
            IFlawFormat f = format ?? new PlainFlawFormat();
            return new[]
            {
                record.Id.ToString(CultureInfo.InvariantCulture),
                cpuName ?? string.Empty,
                typeName ?? string.Empty,
                severityName ?? string.Empty,
                f.Count(q.AffectedUnits),
                f.Money(q.RecallCost),
                f.Money(q.SteppingCost),
                q.SteppingDays.ToString(CultureInfo.InvariantCulture),
                f.Money(q.PatchCost),
                f.Percent(q.PatchIpsLoss),
                q.PatchMonths.ToString(CultureInfo.InvariantCulture),
            };
        }

        /// <summary>The id of the record in the arguments of a firing; <c>false</c> when they do not start with a number.</summary>
        public static bool TryRecordId(string[] args, out int recordId)
        {
            recordId = 0;
            if (args == null || args.Length <= RecordIdArg) return false;
            return int.TryParse(args[RecordIdArg], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordId) && recordId > 0;
        }
    }

    /// <summary>A plain formatter: invariant numbers (the default when no formatter is given, and the one the tests use).</summary>
    internal sealed class PlainFlawFormat : IFlawFormat
    {
        public string Money(float amount)
        {
            return amount.ToString("0", CultureInfo.InvariantCulture);
        }

        public string Count(long units)
        {
            return units.ToString(CultureInfo.InvariantCulture);
        }

        public string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
