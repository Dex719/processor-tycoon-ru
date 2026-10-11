using System.Globalization;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>How the player learns about a flaw of an AI company (FR-11 item 4).</summary>
    public enum FlawNewsKind
    {
        /// <summary>The player is not told (an internal flaw handled quietly, a flaw of the player, the fast-forward of a new game).</summary>
        None,

        /// <summary>A letter in the inbox (<c>ModMail</c>, <c>Normal</c>, tag <c>dfr:news:&lt;recordId&gt;</c>): a <c>Major</c> or <c>Critical</c> flaw.</summary>
        Mail,

        /// <summary>A short notification of two lines (<c>ModNotify.Show</c>): a <c>Minor</c> flaw.</summary>
        Notify,
    }

    /// <summary>What <see cref="FlawNewsPlanner"/> decided: whether to tell the player, how, with which keys, and whether the event journal gets a line.</summary>
    public sealed class FlawNewsPlan
    {
        /// <summary>Nothing to say and nothing to write.</summary>
        public static readonly FlawNewsPlan Nothing = new FlawNewsPlan();

        /// <summary>A letter, a notification, or nothing.</summary>
        public FlawNewsKind Kind = FlawNewsKind.None;

        /// <summary>A line goes to the event journal (<c>EventEffects.ApplyDirect</c>): every public incident of an AI company, whatever its severity.</summary>
        public bool Journal;

        /// <summary>The tag of the letter: <c>dfr:news:&lt;recordId&gt;</c>; <c>null</c> when there is no letter.</summary>
        public string MailTag;

        /// <summary>The key of the title of the letter; <c>null</c> when there is no letter.</summary>
        public string TitleKey;

        /// <summary>The key of the body of the letter; <c>null</c> when there is no letter.</summary>
        public string BodyKey;

        /// <summary>The key of the first line (the title) of the notification; <c>null</c> when there is none.</summary>
        public string NotifyKey;

        /// <summary>The key of the second line of the notification; <c>null</c> when there is none.</summary>
        public string NotifyBodyKey;

        /// <summary>The key of the words for the reaction (<c>dfr.news.reaction.recall</c> and so on); <c>null</c> when nothing is said.</summary>
        public string ReactionKey;

        /// <summary>The player is told in some way (a letter or a notification).</summary>
        public bool TellsPlayer
        {
            get { return Kind != FlawNewsKind.None; }
        }

        /// <summary>Something has to be done at all.</summary>
        public bool IsEmpty
        {
            get { return Kind == FlawNewsKind.None && !Journal; }
        }
    }

    /// <summary>
    /// What the player hears about a flaw of an AI company (FR-11 items 4 and 5, design.md Component 13). Pure: the rules only; <c>FlawNews</c> sends the letter and the
    /// notification and writes the journal.
    /// <list type="bullet">
    /// <item>A flaw that was <b>public</b> when the company answered is news, whatever the answer: <c>Major</c> and <c>Critical</c> by a letter, <c>Minor</c> by a notification;
    /// the event journal gets a line in every case.</item>
    /// <item>A flaw only the company knew about stays a secret when the answer is quiet - a revision, a patch or ignoring (no rumours until a leak, FR-11 item 5). A <b>recall</b>
    /// of such a flaw is news: the units are taken back in the open.</item>
    /// <item>Nothing is said during the fast-forward of a new game (no record is made then either), and nothing about the flaws of the player.</item>
    /// </list>
    /// The core holds a letter or a notification back during a fast-forward, a load or a batch run; this class does not look at them.
    /// </summary>
    public static class FlawNewsPlanner
    {
        /// <summary>The prefix of the tag of a news letter: <c>dfr:news:</c>.</summary>
        public const string MailTagPrefix = "dfr:news:";

        public const string TitleKey = "dfr.news.title";
        public const string BodyKey = "dfr.news.body";
        public const string LeakedTitleKey = "dfr.news.leaked.title";
        public const string LeakedBodyKey = "dfr.news.leaked.body";
        public const string NotifyKey = "dfr.news.rival.title";
        public const string NotifyBodyKey = "dfr.news.rival.body";

        /// <summary>The source id of the line in the event journal (the <c>eventId</c> of the service instance); the prefix of the feature, so the ids stay inside its range.</summary>
        public const string JournalSourceId = "dfr.news.ai";

        /// <summary>The number of positional arguments of the texts: the company, the CPU, the type, the severity, the reaction.</summary>
        public const int ArgCount = 5;

        /// <summary>The tag of the letter about a record.</summary>
        public static string MailTagOf(int recordId)
        {
            return MailTagPrefix + recordId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The key of the words for a reaction ("recalls the units"); <c>null</c> for <see cref="FlawResponse.None"/>.</summary>
        public static string ReactionKeyOf(FlawResponse response)
        {
            string choice = FlawDecisionPlanner.ChoiceOfResponse(response);
            return choice == null ? null : "dfr.news.reaction." + choice;
        }

        /// <summary>
        /// The flaw is known to the public once the company has answered: it was public when it was found, or the answer is a recall (which is never done in secret).
        /// <paramref name="stateBefore"/> is the state of the record before the answer was applied.
        /// </summary>
        public static bool IsPublicIncident(FlawState stateBefore, FlawResponse response)
        {
            if (stateBefore == FlawState.Public) return true;
            return stateBefore == FlawState.InternalKnown && response == FlawResponse.Recall;
        }

        /// <summary>
        /// The plan for a flaw that an AI company has just answered. <paramref name="stateBefore"/> is the state of the record before the answer; <paramref name="response"/> the
        /// answer; <paramref name="fastForward"/> - the fast-forward of a new game is running.
        /// </summary>
        public static FlawNewsPlan Plan(FlawRecord record, FlawResponse response, FlawState stateBefore, bool fastForward)
        {
            if (record == null || fastForward || response == FlawResponse.None) return FlawNewsPlan.Nothing;
            if (CompanyKey.IsPlayer(record.CompanyKey)) return FlawNewsPlan.Nothing;
            if (!IsPublicIncident(stateBefore, response)) return FlawNewsPlan.Nothing;

            bool leaked = record.Data.coverUp && record.Source == DiscoverySource.Leak;
            var plan = new FlawNewsPlan { Journal = true, ReactionKey = ReactionKeyOf(response) };

            if (record.Severity == FlawSeverity.Minor)
            {
                plan.Kind = FlawNewsKind.Notify;
                plan.NotifyKey = NotifyKey;
                plan.NotifyBodyKey = NotifyBodyKey;
                return plan;
            }

            plan.Kind = FlawNewsKind.Mail;
            plan.MailTag = MailTagOf(record.Id);
            plan.TitleKey = leaked ? LeakedTitleKey : TitleKey;
            plan.BodyKey = leaked ? LeakedBodyKey : BodyKey;
            return plan;
        }
    }
}
