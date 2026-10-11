using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>What the event system answered to one firing (the pure mirror of <c>EventFireResult</c>, so that the logic needs no types of the engine).</summary>
    public enum FireResult
    {
        /// <summary>The event fired and has an instance.</summary>
        Fired,

        /// <summary>The firing was queued for a later day.</summary>
        Scheduled,

        /// <summary>Nothing happened: <c>oncePerGame</c> used, the target does not exist or is bankrupt.</summary>
        Skipped,

        /// <summary>The pack has no such event.</summary>
        UnknownEvent,

        /// <summary>The event system, or the pack, is off.</summary>
        Disabled,
    }

    /// <summary>
    /// What happened to a whole stage (or a fallback) of an entry: the worst answer of its firings. What the scheduler needs to set the status:
    /// <see cref="UnknownEvent"/> makes the entry <c>Error</c>, <see cref="Disabled"/> makes it <c>Disabled</c>.
    /// </summary>
    public enum FireOutcome
    {
        /// <summary>At least one event fired or was queued.</summary>
        Fired,

        /// <summary>No event fired (nobody to fire for, a condition said no, or every firing was skipped by the engine).</summary>
        Skipped,

        /// <summary>The engine, or the pack, is off.</summary>
        Disabled,

        /// <summary>A fire names an event the pack does not have.</summary>
        UnknownEvent,
    }

    /// <summary>The door to <c>EventBus.Fire</c>: in the game <c>EventBusFirer</c> (with <c>IgnoreConditions = true</c>), in the tests a recording fake.</summary>
    public interface IEventFirer
    {
        /// <summary>Fires the event for the company <paramref name="companyKey"/> (<c>player</c> or <c>ai:&lt;id&gt;</c>) with the positional arguments of its texts. Never throws.</summary>
        FireResult Fire(string eventId, string companyKey, string[] args);
    }

    /// <summary>What <see cref="DispatcherCore"/> did for one stage: the counts and the worst answer.</summary>
    public sealed class FireReport
    {
        /// <summary>How many firings were asked of the engine.</summary>
        public int Attempts { get; set; }

        /// <summary>How many fired or were queued.</summary>
        public int Fired { get; set; }

        /// <summary>How many the engine skipped.</summary>
        public int Skipped { get; set; }

        /// <summary>How many were left out before the engine was asked (a false <c>onlyIf</c>, a role with no company).</summary>
        public int Dropped { get; set; }

        public bool UnknownEvent { get; set; }
        public bool Disabled { get; set; }

        /// <summary>The ids that the pack does not have.</summary>
        public List<string> UnknownEvents { get; } = new List<string>();

        /// <summary>The worst answer: unknown event, then disabled, then fired, then skipped.</summary>
        public FireOutcome Outcome
        {
            get
            {
                if (UnknownEvent) return FireOutcome.UnknownEvent;
                if (Disabled) return FireOutcome.Disabled;
                return Fired > 0 ? FireOutcome.Fired : FireOutcome.Skipped;
            }
        }
    }

    /// <summary>
    /// Fires the events of a stage (design.md, Component 6; FR-2 item 1, FR-10). For every fire of the stage and every company of its role it asks the
    /// engine (<see cref="IEventFirer"/>) for the event with the arguments of the table below; the engine is told to ignore the conditions of the
    /// event (the calendar has already decided that it happens) and applies the intensity itself (<c>PackOptions.Severity</c>: this class never
    /// scales anything).
    /// <list type="bullet">
    /// <item><b>Who.</b> <c>global</c> fires once for the player (the effects of such an event are global); <c>player</c> is the player; any other role
    /// fires once per company of the role in the order it was found. A role that has no companies in the result fires nothing.</item>
    /// <item><b>onlyIf.</b> Checked for each company: the firing for a company whose condition is false is dropped.</item>
    /// <item><b>One window for one company.</b> A fire that names the role <c>player</c> without the entry declaring one of that name is for the player when no other
    /// fire of the same stage already asks him something through a role the entry declares: the follower of a price war, the partner of a brand programme. A player who is
    /// the aggressor or the leader got the event of that role, so the follower's one is dropped for him (the design: "the aggressor: start or not; otherwise: cut, hold or
    /// market"); a player who is only the challenger of the brand programme still gets the partner's offer.</item>
    /// <item><b>Arguments.</b> <c>[0]</c> the id of the entry; <c>[1]</c> the game's name of the company fired at; <c>[2]</c> the game's name and
    /// <c>[3]</c> the key of the second company of the entry (the first company of the first declared role that is not the one fired at: plaintiff,
    /// beneficiary), empty when there is none; <c>[4..]</c> the <c>ExtraArgs</c> of the fire. The positions never move.</item>
    /// <item><b>Answers.</b> An unknown event or a disabled engine is reported in the <see cref="FireReport"/>; the rest of the fires still go.</item>
    /// </list>
    /// </summary>
    public sealed class DispatcherCore
    {
        /// <summary>The id of the event that tells the player an entry did not happen (<c>fallback: news</c> without fallback fires).</summary>
        public const string FizzleEventId = "hist.fizzle";

        private readonly IEventFirer _firer;
        private readonly IWorldView _world;
        private readonly MarketShareCache _shares;
        private readonly ConditionEvaluator _evaluator;
        private readonly HistState _state;
        private readonly Func<string, string> _text;
        private readonly Action<string> _log;

        /// <param name="text">The text of a string key in the current language (<c>ModL10n.L</c>); null shows the key.</param>
        /// <param name="log">Receives a line for the log per problem; may be null.</param>
        public DispatcherCore(IEventFirer firer, IWorldView world, MarketShareCache shares, ConditionEvaluator evaluator, HistState state,
            Func<string, string> text = null, Action<string> log = null)
        {
            if (firer == null) throw new ArgumentNullException("firer");
            _firer = firer;
            _world = world;
            _shares = shares ?? new MarketShareCache();
            _evaluator = evaluator;
            _state = state ?? new HistState();
            _text = text;
            _log = log;
        }

        /// <summary>Fires stage <paramref name="stageIndex"/> of <paramref name="entry"/> for the companies of <paramref name="roles"/>.</summary>
        public FireReport FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
        {
            var report = new FireReport();
            if (entry == null || entry.Stages == null || stageIndex < 0 || stageIndex >= entry.Stages.Count) return report;

            StageSpec stage = entry.Stages[stageIndex];
            if (stage == null || stage.Fires == null) return report;

            FireAll(entry, stage.Fires, roles ?? new RoleResult(), report);
            return report;
        }

        /// <summary>
        /// What happens when the window of an entry ends (FR-2 item 4): for <c>news</c> the fallback fires of the entry, or without them the event
        /// <c>hist.fizzle</c> for the player with the arguments <c>[title, id]</c> (the title of the entry's history, which the text of the event
        /// shows); for <c>skip</c> nothing. <paramref name="roles"/> may be null or partial: a fire whose role is not in it is left out.
        /// </summary>
        public FireReport FireFallback(CalendarEntry entry, RoleResult roles)
        {
            var report = new FireReport();
            if (entry == null || !string.Equals(entry.Fallback, FallbackModes.News, StringComparison.Ordinal)) return report;

            if (entry.FallbackFires != null && entry.FallbackFires.Count > 0)
            {
                FireAll(entry, entry.FallbackFires, roles ?? new RoleResult(), report);
                return report;
            }

            string title = entry.History != null && !string.IsNullOrEmpty(entry.History.TitleKey) ? Text(entry.History.TitleKey) : entry.Id;
            Ask(FizzleEventId, CompanyKey.Player, new[] { title ?? string.Empty, entry.Id ?? string.Empty }, report);
            return report;
        }

        /// <summary>
        /// The arguments of a firing (see the class summary). <paramref name="companyKey"/> is the company fired at; the roles say who the second
        /// company is. Public for the tests of the table.
        /// </summary>
        public string[] BuildArgs(CalendarEntry entry, RoleResult roles, FireSpec spec, string companyKey)
        {
            string[] extra = spec == null || spec.ExtraArgs == null ? new string[0] : spec.ExtraArgs;
            var args = new string[4 + extra.Length];

            string secondKey = SecondCompany(entry, roles, companyKey);
            args[0] = entry == null ? string.Empty : entry.Id ?? string.Empty;
            args[1] = NameOf(companyKey);
            args[2] = secondKey == null ? string.Empty : NameOf(secondKey);
            args[3] = secondKey ?? string.Empty;
            for (int i = 0; i < extra.Length; i++) args[4 + i] = extra[i] ?? string.Empty;
            return args;
        }

        // ---- internals ----

        private void FireAll(CalendarEntry entry, List<FireSpec> fires, RoleResult roles, FireReport report)
        {
            foreach (FireSpec spec in fires)
            {
                if (spec == null || string.IsNullOrEmpty(spec.EventId)) continue;

                foreach (string key in CompaniesOf(spec.Role, roles))
                {
                    if (IsSecondWindow(entry, fires, spec, roles, key))
                    {
                        report.Dropped++;
                        continue;
                    }

                    if (spec.OnlyIf != null && !OnlyIf(spec.OnlyIf, roles, key))
                    {
                        report.Dropped++;
                        continue;
                    }

                    Ask(spec.EventId, key, BuildArgs(entry, roles, spec, key), report);
                    if (report.Disabled) return;   // the engine (or the pack) is off: the rest would be told the same
                }
            }
        }

        /// <summary>
        /// The fire names the built-in role <c>player</c> and another fire of the same stage already goes to the player through a single-company role the entry declares
        /// (the leader, the aggressor, the plaintiff ...): that event is his window, a second one for the same entry would ask him two things about one event.
        /// </summary>
        private static bool IsSecondWindow(CalendarEntry entry, List<FireSpec> stage, FireSpec spec, RoleResult roles, string companyKey)
        {
            if (entry == null || entry.Roles == null || spec.Role != RoleKinds.Player || !CompanyKey.IsPlayer(companyKey)) return false;
            if (Declared(entry, RoleKinds.Player) != null) return false;   // the entry declares its own role of that name: the fire is for that role

            foreach (FireSpec other in stage)
            {
                if (other == null || ReferenceEquals(other, spec) || string.IsNullOrEmpty(other.Role)) continue;

                RoleSpec declared = Declared(entry, other.Role);
                if (declared == null || declared.Kind == RoleKinds.Global || RoleKinds.IsGroup(declared.Kind)) continue;
                if (roles.Contains(other.Role, companyKey)) return true;
            }
            return false;
        }

        private static RoleSpec Declared(CalendarEntry entry, string name)
        {
            foreach (RoleSpec role in entry.Roles)
            {
                if (role != null && role.Name == name) return role;
            }
            return null;
        }

        /// <summary>The companies a fire is for: the player for <c>global</c> and <c>player</c>, else the companies of the role.</summary>
        internal static IReadOnlyList<string> CompaniesOf(string role, RoleResult roles)
        {
            if (string.IsNullOrEmpty(role) || role == RoleKinds.Global || role == RoleKinds.Player) return new[] { CompanyKey.Player };
            return roles.KeysOf(role);
        }

        private bool OnlyIf(PTMod.Core.ICondition condition, RoleResult roles, string subjectKey)
        {
            if (_evaluator == null) return true;
            return _evaluator.Evaluate(condition, roles, _world, _shares, _state, subjectKey);
        }

        private void Ask(string eventId, string companyKey, string[] args, FireReport report)
        {
            report.Attempts++;
            FireResult result;
            try
            {
                result = _firer.Fire(eventId, companyKey, args);
            }
            catch (Exception ex)
            {
                Log("firing " + eventId + " for " + companyKey + " threw " + ex.GetType().Name + ": " + ex.Message);
                report.Skipped++;
                return;
            }

            switch (result)
            {
                case FireResult.Fired:
                case FireResult.Scheduled:
                    report.Fired++;
                    break;
                case FireResult.UnknownEvent:
                    report.UnknownEvent = true;
                    if (!report.UnknownEvents.Contains(eventId)) report.UnknownEvents.Add(eventId);
                    Log("the pack has no event " + eventId);
                    break;
                case FireResult.Disabled:
                    report.Disabled = true;
                    break;
                default:
                    report.Skipped++;
                    break;
            }
        }

        private static string SecondCompany(CalendarEntry entry, RoleResult roles, string firedAt)
        {
            if (entry == null || entry.Roles == null || roles == null) return null;

            foreach (RoleSpec role in entry.Roles)
            {
                if (role == null) continue;
                string key = roles.FirstKey(role.Name);
                if (key != null && !string.Equals(key, firedAt, StringComparison.Ordinal)) return key;
            }
            return null;
        }

        private string NameOf(string companyKey)
        {
            if (companyKey == null) return string.Empty;
            IReadOnlyList<CompanyInfo> companies = _world == null ? null : _world.Companies;
            for (int i = 0; companies != null && i < companies.Count; i++)
            {
                CompanyInfo company = companies[i];
                if (company != null && string.Equals(company.Key, companyKey, StringComparison.Ordinal)) return company.Name ?? string.Empty;
            }
            return string.Empty;
        }

        private string Text(string key)
        {
            return _text == null ? key : _text(key);
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
