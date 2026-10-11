using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalPersonalities
{
    // The section "rival-personalities" of the side save file (design.md, "DTO состояния (ModSave)"). Plain DTOs for ModJson.Bind / ModJson.From.
    // There is NO version field: the core writes and reads "_schema" itself (RivalSaveCodec.SchemaVersion is what the participant reports).
    // Dates are the text yyyy-MM-dd (an empty text is "not set"), a company is its UniqueID (uid) or a ModCompanies key ("ai:<UniqueID>"),
    // a segment is the number (int)Segment: 0 HighEnd, 1 MidRange, 2 LowEnd, 3 Industries. No dictionaries: JsonUtility cannot read them
    // (decision 0006), a "map" is a list of records. The fields are public and lower-case because they are the JSON member names.

    /// <summary>The whole section. See the comment above for the rules of the file.</summary>
    [Serializable]
    public sealed class RivalState
    {
        /// <summary>The first day of the grace period (<c>yyyy-MM-dd</c>); empty until the characters were given (the end of the fast-forward, or the first load of a save without a section).</summary>
        public string graceStart = string.Empty;

        /// <summary>The character of each company that has one (FR-2).</summary>
        public List<AssignmentEntry> assignments = new List<AssignmentEntry>();

        /// <summary>The <c>UniqueID</c> of the rivals whose archetype is shown to the player (FR-11).</summary>
        public List<int> revealed = new List<int>();

        /// <summary>The attitude of each rival to the player and its history (FR-4).</summary>
        public List<AttitudeRecord> attitudes = new List<AttitudeRecord>();

        /// <summary>The market shares of the last full month, to see who gained (FR-5).</summary>
        public ShareSnapshot shareSnapshot = new ShareSnapshot();

        /// <summary>Until when a rival does not count the same segment as a price undercut again.</summary>
        public List<UndercutCooldown> undercutCooldowns = new List<UndercutCooldown>();

        /// <summary>The reactions that are planned and not carried out yet (FR-7).</summary>
        public List<PendingReaction> pending = new List<PendingReaction>();

        /// <summary>The reactions that are in effect now (FR-7).</summary>
        public List<ActiveReaction> active = new List<ActiveReaction>();

        /// <summary>The target prices that the price cuts replaced (FR-10): the way back to them.</summary>
        public List<PriceBookEntry> priceBook = new List<PriceBookEntry>();

        /// <summary>The caution of AI companies after a public design flaw (FR-17).</summary>
        public List<CautionEntry> cautions = new List<CautionEntry>();

        /// <summary>The counters of the honesty limits (FR-6).</summary>
        public Counters counters = new Counters();

        /// <summary>The last reactions and what became of them, newest last (the panel shows ten).</summary>
        public List<HistoryEntry> history = new List<HistoryEntry>();

        /// <summary>The <c>UniqueID</c> of the rivals that are put aside (<c>SetDormant</c>).</summary>
        public List<int> dormant = new List<int>();

        /// <summary>The number of the next reaction: its id is <c>r-&lt;n&gt;</c>.</summary>
        public int nextReactionSeq = 1;
    }

    /// <summary>The archetype of one company.</summary>
    [Serializable]
    public sealed class AssignmentEntry
    {
        public int uid;
        public string archetype;
    }

    /// <summary>The attitude of one rival. <see cref="value"/> is NaN until it was set (the reader then uses the baseline of the archetype).</summary>
    [Serializable]
    public sealed class AttitudeRecord
    {
        public int uid;
        public float value = float.NaN;
        public List<AttitudeEntry> history = new List<AttitudeEntry>();
    }

    /// <summary>One change of an attitude.</summary>
    [Serializable]
    public sealed class AttitudeEntry
    {
        public string date;
        public float delta;
        public string reason;
    }

    /// <summary>The shares of the companies in one month.</summary>
    [Serializable]
    public sealed class ShareSnapshot
    {
        /// <summary>The month the shares are of (a running number of months, so the next month is easy to tell).</summary>
        public int monthIndex;

        public List<ShareRow> rows = new List<ShareRow>();
    }

    /// <summary>The shares of one company, one number per segment (index = <c>(int)Segment</c>).</summary>
    [Serializable]
    public sealed class ShareRow
    {
        /// <summary><c>player</c> or <c>ai:&lt;UniqueID&gt;</c>.</summary>
        public string key;

        public float[] s = new float[4];
    }

    /// <summary>A segment in which a rival does not count a price undercut of the player until <see cref="until"/>.</summary>
    [Serializable]
    public sealed class UndercutCooldown
    {
        public int uid;
        public int segment;
        public string until;
    }

    /// <summary>A reaction that is planned: the rival, the type, the mail that warns about it and the day it will be carried out.</summary>
    [Serializable]
    public sealed class PendingReaction
    {
        /// <summary>The id of the reaction: <c>r-&lt;n&gt;</c>.</summary>
        public string id;

        public int uid;

        /// <summary>The name of a <see cref="ReactionType"/>.</summary>
        public string type;

        public int segment;

        /// <summary>The name of the <see cref="TriggerType"/> that caused it.</summary>
        public string trigger;

        public string plannedOn;

        /// <summary>The id of the intelligence mail in <c>ModMail</c> (the day of the delivery starts the warning).</summary>
        public int mailId;

        /// <summary>The warning, in days after the delivery of the mail, rolled when the reaction was planned.</summary>
        public int telegraphDays;

        /// <summary>The day it will be carried out; empty until the mail is delivered.</summary>
        public string fireOn;
    }

    /// <summary>A reaction that is in effect.</summary>
    [Serializable]
    public sealed class ActiveReaction
    {
        public string id;
        public int uid;
        public string type;
        public int segment;
        public string start;
        public string end;
    }

    /// <summary>The target price of a CPU before a price cut took it down.</summary>
    [Serializable]
    public sealed class PriceBookEntry
    {
        /// <summary>The save id of the CPU.</summary>
        public int cpuSaveId;

        public int uid;

        /// <summary><c>r-&lt;n&gt;</c> for a reaction of the feature, otherwise the id of the feature that asked for the cut (<c>&lt;prefix&gt;:&lt;name&gt;</c>).</summary>
        public string reactionId;

        public float baseTargetPrice;
        public float cutTarget;
        public string startedOn;
    }

    /// <summary>An AI company that is careful after a public design flaw.</summary>
    [Serializable]
    public sealed class CautionEntry
    {
        public int uid;
        public int flawId;
        public string until;
    }

    /// <summary>The counters of the limits.</summary>
    [Serializable]
    public sealed class Counters
    {
        /// <summary>The game year the counter of the year belongs to.</summary>
        public int year;

        public int hostileExecutedThisYear;

        /// <summary>The day of the last reaction of each rival.</summary>
        public List<LastReaction> lastByUid = new List<LastReaction>();

        /// <summary>How many reactions each rival has carried out so far, all game years together (the predicate <c>rival.reactionsExecuted</c>, FR-13 item 4).</summary>
        public List<ExecutedCount> executed = new List<ExecutedCount>();

        /// <summary>The day of the last friendly offer of each foundry (FR-12 item 2: not more often than once in <c>repeatMonths</c>).</summary>
        public List<LastReaction> lastPartnerByUid = new List<LastReaction>();
    }

    /// <summary>The number of reactions one rival has carried out (<see cref="Counters.executed"/>).</summary>
    [Serializable]
    public sealed class ExecutedCount
    {
        public int uid;
        public int count;
    }

    /// <summary>The day of the last reaction of one rival.</summary>
    [Serializable]
    public sealed class LastReaction
    {
        public int uid;
        public string date;
    }

    /// <summary>One line of the history of reactions.</summary>
    [Serializable]
    public sealed class HistoryEntry
    {
        public int uid;
        public string date;
        public string type;
        public int segment;

        /// <summary><c>planned</c>, <c>started</c>, <c>ended</c>, <c>cancelled</c>, <c>counterLaunchDone</c> or <c>revealed</c>.</summary>
        public string outcome;
    }
}
