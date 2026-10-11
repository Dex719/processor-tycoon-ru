using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    // The saved shape of the engine's data (design.md, "Data Models"). Plain public fields: the names are the keys of the section "event-system"
    // of the side save file (ModJson.From / ModJson.Bind). The section has no version field - the core writes and reads "_schema".

    /// <summary>One firing of an event for one company (or one global instance): what was rolled, what it costs, what was chosen.</summary>
    public sealed class EventInstanceDto
    {
        public int id;

        /// <summary>Shared by the instances of one firing of an <c>AllCompanies</c> event.</summary>
        public int groupId;

        /// <summary><see cref="EventInstanceKinds.Event"/> or <see cref="EventInstanceKinds.Direct"/> (<c>EventEffects.ApplyDirect</c>; <c>eventId</c> is then the source id).</summary>
        public string kind = EventInstanceKinds.Event;

        public string eventId;

        /// <summary><c>player</c>, <c>ai:&lt;UniqueID&gt;</c> or <c>global</c>.</summary>
        public string companyKey;

        /// <summary>Game day of the firing (days from 1971-01-01).</summary>
        public int firedDay;

        /// <summary>Day the mail was delivered (<c>ModMail.Delivered</c>); 0 - the mail is still queued, or there is no mail.</summary>
        public int deliveredDay;

        /// <summary>0 - nothing to decide; otherwise <c>max(firedDay, deliveredDay) + timerDays</c> (before delivery: <c>firedDay + timerDays</c>).</summary>
        public int deadlineDay;

        /// <summary>One of <see cref="EventInstanceStates"/>.</summary>
        public string state;

        /// <summary>Id of the chosen choice, empty while none was chosen.</summary>
        public string chosen = string.Empty;

        /// <summary>The choice was made by the timer or by an AI company, not by a player's click.</summary>
        public bool auto;

        /// <summary>The effects of every choice, resolved once at firing (D1).</summary>
        public List<ChoiceOptionDto> options = new List<ChoiceOptionDto>();

        /// <summary>The effects of the event itself (<c>effects</c> of the definition).</summary>
        public List<AppliedEffect> common = new List<AppliedEffect>();

        /// <summary>Id returned by <c>ModMail.Send</c>; a hint only - the mail is found by the tag <c>es:&lt;id&gt;</c>.</summary>
        public int mailId;

        /// <summary>The instance whose <c>FireEvent</c> effect started this one; 0 for none.</summary>
        public int parentId;

        /// <summary>Positional arguments of the texts (<c>{0}</c>, <c>{1}</c>...), as given in <see cref="EventFireContext.Args"/>.</summary>
        public string[] args = new string[0];

        /// <summary>
        /// How many <c>FireEvent</c> steps led to this instance: 0 for an event that was not started by a chain, the parent's depth + 1 otherwise. A
        /// <c>FireEvent</c> of an instance at depth 8 is not applied (NFR-7: a chain is at most 8 levels deep).
        /// </summary>
        public int chainDepth;
    }

    /// <summary>The effects of one choice of an instance.</summary>
    public sealed class ChoiceOptionDto
    {
        public string choiceId;
        public List<AppliedEffect> effects = new List<AppliedEffect>();
    }

    /// <summary>
    /// An effect with its numbers already computed (D1): the mail, the window, the journal and the application all read the numbers from here and
    /// never recompute them.
    /// </summary>
    public sealed class AppliedEffect
    {
        /// <summary><c>&lt;instanceId&gt;/&lt;choiceId|common&gt;/&lt;index&gt;</c>.</summary>
        public string id;

        public int instanceId;

        /// <summary>The registered effect type.</summary>
        public string type;

        /// <summary>The company the effect acts on; <c>global</c> for a global-scope effect.</summary>
        public string companyKey;

        /// <summary>The modifier channel for modifier effects; empty for the others.</summary>
        public string channel = string.Empty;

        /// <summary>The final number: money in dollars (negative - spend), a multiplier, a delta.</summary>
        public float value;

        /// <summary>Parameters of a curve effect (<c>peak, rampIn, hold, rampOut</c> / <c>amplitude, period, phase</c>); the meaning belongs to the effect type.</summary>
        public float[] curve;

        public int startDay;

        /// <summary>0 - a one-off effect.</summary>
        public int endDay;

        /// <summary>For a variant not applied yet: how long it will last once applied.</summary>
        public int durationDays;

        public float chance = 1f;

        /// <summary>The result of the chance roll (hidden from the player until the decision).</summary>
        public bool hit = true;

        /// <summary>The <c>exclusiveGroup</c> of the effect inside its choice; empty or null for none.</summary>
        public string group;

        /// <summary>One of <see cref="AppliedEffectStates"/>.</summary>
        public string state;

        /// <summary>
        /// The value was cut by the limits of its channel when it was resolved (D15, AC-5.5): <see cref="value"/> is already the cut number, the one the
        /// mail shows and the channel gets; the mark "ограничено" of the texts and <see cref="EffectRecord.clamped"/> of the journal come from here.
        /// </summary>
        public bool clamped;

        /// <summary>The L10n key of the effect's line and its arguments, built from the numbers above.</summary>
        public string textKey;
        public string[] textArgs = new string[0];
    }
}
