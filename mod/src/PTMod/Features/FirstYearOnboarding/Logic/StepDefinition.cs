using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>How a step reaches the player: in a fixed order, one after another, or when its own situation comes up.</summary>
    public enum StepKind
    {
        /// <summary>A step of the track: shown strictly by <see cref="StepDefinition.Order"/>, one current step at a time.</summary>
        Sequential = 0,

        /// <summary>A tip: shown once, when its <see cref="StepDefinition.ShowWhen"/> comes true, next to the sequential steps.</summary>
        Situational = 1
    }

    /// <summary>
    /// A condition of a step in the notation of <c>ModConditions</c> (<c>{ "type": "...", ... }</c>, <c>all</c> / <c>any</c> / <c>not</c>), kept as the
    /// JSON tree. Pure logic never evaluates it: the world (<see cref="IOnboardingWorld"/>) does, and may park its parsed condition in
    /// <see cref="Compiled"/> so the JSON is parsed once.
    /// </summary>
    public sealed class ConditionRef
    {
        public ConditionRef()
        {
        }

        public ConditionRef(JsonNode node)
        {
            Node = node;
        }

        /// <summary>The node in the notation of <c>ModConditions</c>.</summary>
        public JsonNode Node;

        /// <summary>The condition object of the core built from <see cref="Node"/> by the game layer (a cache; tests leave it null).</summary>
        public object Compiled;

        /// <summary>The game layer tried to parse <see cref="Node"/> and failed: the condition is false from then on, and the parse is not repeated.</summary>
        public bool CompileFailed;

        public override string ToString()
        {
            return Node == null ? "(none)" : ModJson.Write(Node, false);
        }
    }

    /// <summary>A text for a mail that replaces the main body when its condition is true at the moment of sending (for example when <c>bankruptcy-recovery</c> is on).</summary>
    public sealed class BodyVariant
    {
        /// <summary>The condition that selects the variant.</summary>
        public ConditionRef When;

        /// <summary>The string key of the text that replaces the body.</summary>
        public string BodyKey;
    }

    /// <summary>A reminder hint of a step: shown <see cref="AfterDays"/> game days after the mail of the step was delivered, if the step is still open.</summary>
    public sealed class ReminderSpec
    {
        /// <summary>Game days since the delivery of the mail, 1..3650.</summary>
        public int AfterDays;

        /// <summary>The string key of the hint text.</summary>
        public string HintKey;

        /// <summary>Names of the placeholders that fill <c>{0}</c>, <c>{1}</c>... of the hint; empty for none.</summary>
        public string[] Args = new string[0];
    }

    /// <summary>What the mail of a step says and offers.</summary>
    public sealed class MailSpec
    {
        /// <summary>String key of the title (the string itself starts with the mentor prefix).</summary>
        public string TitleKey;

        /// <summary>String key of the body; the variant of a track is looked up as <c>&lt;BodyKey&gt;.&lt;track&gt;</c>.</summary>
        public string BodyKey;

        /// <summary>Names of the placeholders that fill <c>{0}</c>, <c>{1}</c>... of the title and the body, in this order.</summary>
        public string[] Args = new string[0];

        /// <summary>Ids of the buttons of the mail: <c>skipAll</c>, <c>skipStep</c>, <c>hint</c>, <c>openCpuDesigner</c>, <c>openFactoryExpansion</c>.</summary>
        public string[] Buttons = new string[0];

        /// <summary>Bodies chosen by a condition at the moment of sending; the first variant whose condition is true wins.</summary>
        public BodyVariant[] Variants = new BodyVariant[0];
    }

    /// <summary>
    /// One step of the tutorial as <c>steps.json</c> describes it (design.md, "StepDefinition"). Plain fields filled by <see cref="StepMapper"/>
    /// and checked by <see cref="StepValidator"/>; the catalog only hands out steps that passed the check.
    /// </summary>
    public sealed class StepDefinition
    {
        /// <summary>Prefix of the string keys of the feature; the keys of a step are <c>onb.&lt;id&gt;.name</c> and <c>onb.&lt;id&gt;.done</c> next to the ones in <see cref="Mail"/>.</summary>
        public const string KeyPrefix = "onb.";

        /// <summary>Unique, <c>[a-z0-9_]{2,40}</c>.</summary>
        public string Id;

        /// <summary>The place among the sequential steps of a track (unique inside the track); ignored for a situational step.</summary>
        public int Order;

        public StepKind Kind;

        /// <summary>The tracks the step belongs to: a non-empty subset of <c>idm</c>, <c>fabless</c>, <c>foundry</c>.</summary>
        public string[] Tracks = new string[0];

        /// <summary>The step becomes current (a tip is sent) only when this is true; null means at once.</summary>
        public ConditionRef ShowWhen;

        /// <summary>The step is done when this is true (latched); null when <see cref="CompleteOnShow"/> is set.</summary>
        public ConditionRef CompleteWhen;

        /// <summary>An information step: done at the moment its mail is sent.</summary>
        public bool CompleteOnShow;

        /// <summary>Situational: the condition has to hold on this many game days in a row; 0 = at once.</summary>
        public int StreakDays;

        /// <summary><c>critical</c>, <c>normal</c> (default) or <c>low</c>: the priority of the mail in the queue of the core.</summary>
        public string Priority = "normal";

        public MailSpec Mail = new MailSpec();

        public ReminderSpec[] Reminders = new ReminderSpec[0];

        /// <summary>Pause the game when the mail of the step is delivered (if the player's setting allows).</summary>
        public bool PauseOnShow;

        /// <summary>String key of the short name of the step ("Step 4 of 10: Waiting for the development"): the hub page and the summary mail.</summary>
        public string NameKey
        {
            get { return KeyPrefix + Id + ".name"; }
        }

        /// <summary>String key of the phrase for the line "Already done: ..." of the mail that follows a step the player did before it came.</summary>
        public string DoneKey
        {
            get { return KeyPrefix + Id + ".done"; }
        }

        /// <summary>True for a step that is in the given track.</summary>
        public bool InTrack(string track)
        {
            if (Tracks == null || track == null) return false;
            for (int i = 0; i < Tracks.Length; i++)
            {
                if (string.Equals(Tracks[i], track, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return (Id ?? "?") + " (" + Kind + ", order " + Order + ")";
        }
    }

    /// <summary>
    /// The rhythm of the tutorial from <c>config.json</c> (design.md, "OnboardingConfig"). Out-of-range values are brought into
    /// range by <see cref="StepMapper.MapConfig"/>, so the fields always hold usable numbers.
    /// </summary>
    public sealed class OnboardingConfig
    {
        public const int DefaultTutorialWindowDays = 730;
        public const int DefaultMinDaysBetweenMails = 1;
        public const int DefaultDefaultDaysFallback = 120;

        /// <summary>Game days from the start after which the tutorial ends with the summary mail, 30..3650.</summary>
        public int TutorialWindowDays = DefaultTutorialWindowDays;

        /// <summary>The smallest number of game days between two mails of the tutorial, 1..365.</summary>
        public int MinDaysBetweenMails = DefaultMinDaysBetweenMails;

        /// <summary>The days to bankruptcy in default that the texts quote when the game's own value cannot be read, 1..3650.</summary>
        public int DefaultDaysFallback = DefaultDefaultDaysFallback;
    }
}
