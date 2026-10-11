using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// One event of a content pack (<c>events.json</c> of a pack, <c>ModContent.LoadRecords</c>). Built from JSON by the engine's mapper
    /// (the fields <c>conditions</c>, <c>filter</c> and <c>requires</c> are parsed with <c>ModConditions.Parse</c>); read-only for every other feature.
    /// The field names are the JSON keys.
    /// </summary>
    public sealed class EventDefinition : IContentRecord
    {
        /// <summary>Unique id, <c>&lt;prefix&gt;.&lt;name&gt;</c> with a prefix of the pack's owner feature (<c>ModIds.IsEventIdAllowed</c>).</summary>
        public string id;

        /// <summary>Category of the lottery (pacing: <c>dailyChance</c>, <c>minGapDays</c>, <c>maxActive</c> in a pack's <c>config.json</c>).</summary>
        public string category;

        public LocText title;
        public LocText text;

        /// <summary>"Why did this happen" line of the mail, the window and the journal.</summary>
        public LocText cause;

        public EventTrigger trigger;

        /// <summary>A <c>ModConditions</c> node evaluated for the target company; null - always true.</summary>
        public ICondition conditions;

        public float weight = 1f;
        public int cooldownDays;
        public bool oncePerGame;
        public EventTargetSpec targets;

        /// <summary>The choices offered to the target; empty - an event without a choice.</summary>
        public EventChoice[] choices = new EventChoice[0];

        /// <summary>Days to decide, counted from the day the mail is delivered (D2); 0 - no choice to time.</summary>
        public int timerDays;

        /// <summary>The choice applied when the timer runs out and for AI companies that cannot decide.</summary>
        public string defaultChoice;

        /// <summary>Effects applied whenever the event fires, whatever is chosen.</summary>
        public EventEffect[] effects = new EventEffect[0];

        /// <summary>A modal window with a pause (otherwise a mail and a notification only).</summary>
        public bool important;

        /// <summary>A news item for the player when the target is an AI company.</summary>
        public bool notifyPlayer;

        /// <summary>The pack that registered the event; filled by the registry, not by the JSON.</summary>
        public string packId;

        public string Id { get { return id; } }
    }

    /// <summary>When an event starts.</summary>
    public sealed class EventTrigger
    {
        public EventTriggerType type;

        // Date
        public int year;
        public int month;
        public int day;

        /// <summary>Date: how many days after the date the event may still start (a late start after a load or a long fast-forward).</summary>
        public int graceDays = 31;

        /// <summary>Condition: the chance to fire when the conditions are true.</summary>
        public float chance = 1f;

        /// <summary>Condition: <c>"Day"</c> or <c>"Month"</c> (the 1st).</summary>
        public string checkEvery = "Month";

        /// <summary>Condition: fire only on the change false to true.</summary>
        public bool edge;
    }

    /// <summary>Whom an event hits.</summary>
    public sealed class EventTargetSpec
    {
        public EventTargetType type;

        /// <summary>Specific: <c>"player"</c> or <c>"ai:&lt;UniqueID&gt;"</c>.</summary>
        public string companyKey;

        /// <summary>A condition checked for every candidate company; null - every company fits.</summary>
        public ICondition filter;

        public TargetWeighting weighting;
    }

    /// <summary>One button of an event: its text, its effects and how an AI company rates it.</summary>
    public sealed class EventChoice
    {
        public string id;

        /// <summary>Key of the button text in the string tables of the pack's owner feature.</summary>
        public string textKey;

        public EventEffect[] effects = new EventEffect[0];

        /// <summary>A condition for the choice to be available; null - always available. A false condition greys the button, it does not remove it.</summary>
        public ICondition requires;

        /// <summary>Weight of the choice in the default AI policy.</summary>
        public float aiWeight = 1f;

        /// <summary>An AI company takes the choice only if its money is at least this multiple of the choice's cost; 0 - no such rule.</summary>
        public float aiMinCashMultiple;
    }

    /// <summary>One effect of an event or of a choice: a registered type and its parameters.</summary>
    public sealed class EventEffect
    {
        /// <summary>The type name given to <c>EventEffects.Register</c> (<c>Money</c>, <c>ModifierTimed</c>, <c>ModifierRamp</c>...).</summary>
        public string type;

        /// <summary>The JSON object <c>params</c> of the effect; never null for a loaded definition.</summary>
        public IEffectParams @params;

        /// <summary>How long a timed effect lasts, in game days; 0 for a one-off effect.</summary>
        public int durationDays;
    }
}
