using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What the planner of the reactions needs to know about the game and the settings (design.md, Component 8). The game side implements it once
    /// (<c>ReactionPlanner</c>); the tests implement it on a plain class. Reads only, and cheap: the planner asks every day.
    /// </summary>
    public interface IPlannerEnvironment
    {
        /// <summary>The game date (<c>ModTicks.Today</c>); <c>default(DateTime)</c> outside a game.</summary>
        DateTime Today { get; }

        /// <summary><c>Player.DifficultyLevel</c>, 0..5.</summary>
        int Difficulty { get; }

        /// <summary>The event system works now (FR-7 item 8): it is on, and its engine runs.</summary>
        bool EventSystemAvailable { get; }

        /// <summary>The debug flag <c>forceReactions</c>: chance 1.0, a warning of one day, no grace period. Always false in a release build.</summary>
        bool ForceReactions { get; }

        /// <summary>A save loads, the new-game fast-forward runs or the game is not ready: no reaction is planned (FR-6 item 7).</summary>
        bool Blocked { get; }

        /// <summary>The research of the player is underfunded (<c>ResearchSector.Funding &lt; 1.0</c>): <c>Poach</c> is more likely (formula 6).</summary>
        bool PlayerResearchUnderfunded { get; }

        /// <summary>The AI company with this <c>UniqueID</c> is bankrupt.</summary>
        bool IsBankrupt(int uniqueId);

        /// <summary>The rival sells at least one CPU on the market in the segment (a <c>PriceCut</c> needs one).</summary>
        bool SellsCpuIn(int uniqueId, Segment segment);

        /// <summary>The segment the rival is strongest in (its best CPU on the market); <see cref="Segment.Any"/> when it has none.</summary>
        Segment MainSegmentOf(int uniqueId);

        /// <summary>
        /// The day the intelligence mail reached the inbox, or null while it waits in the queue (or is unknown). The planner uses it when the
        /// event <c>ModMail.Delivered</c> was missed (the feature was off, a load).
        /// </summary>
        DateTime? MailDeliveredOn(int mailId, string tag);
    }

    /// <summary>What goes into the intelligence mail of a planned reaction (FR-7 item 1): who, what, where and the range of weeks.</summary>
    public struct IntelNotice
    {
        public IntelNotice(string reactionId, int uniqueId, ReactionType type, Segment segment, int weeksFrom, int weeksTo)
        {
            ReactionId = reactionId;
            UniqueId = uniqueId;
            Type = type;
            Segment = segment;
            WeeksFrom = weeksFrom;
            WeeksTo = weeksTo;
        }

        public string ReactionId;
        public int UniqueId;
        public ReactionType Type;

        /// <summary><see cref="PTMod.Core.Segment.Any"/> for a reaction that has no segment (<c>OemPush</c>, <c>Poach</c>, <c>Smear</c>).</summary>
        public Segment Segment;

        /// <summary>The warning as a range of weeks: from the delivery of the mail to the carrying out, 2..7 with the default 14..45 days.</summary>
        public int WeeksFrom;

        public int WeeksTo;
    }

    /// <summary>Sends the intelligence mail (<c>ModMail.Send</c> in the game).</summary>
    public interface IIntelOutput
    {
        /// <summary>Sends the mail and returns its id (<c>ModMail.Send</c>), or 0 or less when the mail was not accepted.</summary>
        int SendIntel(IntelNotice notice);
    }

    /// <summary>What happened when the executor was asked to carry a reaction out.</summary>
    public enum LaunchOutcome
    {
        /// <summary>The event fired: the reaction is in effect (or waits for the choice of the player) and the planned record can go.</summary>
        Started = 0,

        /// <summary>The event system is not ready (switched off): ask again tomorrow.</summary>
        Retry = 1,

        /// <summary>The event could not be fired (unknown event, no target): the reaction is dropped.</summary>
        Cancelled = 2,
    }

    /// <summary>The executor of the reactions as the planner sees it (<c>ReactionExecutorCore</c>).</summary>
    public interface IReactionLauncher
    {
        /// <summary>Carries the planned reaction out (fires its event). <paramref name="reason"/> tells why it was not (for the log); null when it was.</summary>
        LaunchOutcome Launch(PendingReaction pending, out string reason);

        /// <summary>Takes a reaction in effect back without waiting for its end (the rival went bankrupt or was put aside): gives back what it holds.</summary>
        void Abort(ActiveReaction active, string reason);
    }

    /// <summary>What <see cref="ReactionPlannerCore.OnTrigger"/> did.</summary>
    public enum PlanStatus
    {
        /// <summary>A reaction was planned: see <see cref="PlanResult.Reaction"/>.</summary>
        Planned = 0,

        /// <summary>A save loads, the fast-forward runs or the game is not ready.</summary>
        Blocked = 1,

        /// <summary>The rival has no character.</summary>
        NoArchetype = 2,

        /// <summary>The archetype has no reaction for the trigger.</summary>
        NoRules = 3,

        /// <summary>A limit of FR-6 forbids it: see <see cref="PlanResult.Gate"/>.</summary>
        Refused = 4,

        /// <summary>No row of the table could be tried (no segment, no CPU to cut the price of).</summary>
        NoCandidate = 5,

        /// <summary>Every roll missed.</summary>
        Missed = 6,

        /// <summary>The roll succeeded, but the intelligence mail was not accepted, so the reaction was not planned (the warning is not optional, D12).</summary>
        MailRefused = 7,
    }

    /// <summary>The answer of the planner to a trigger.</summary>
    public sealed class PlanResult
    {
        public PlanStatus Status;

        /// <summary>The reason of a refusal; <see cref="GateResult.Allowed"/> otherwise.</summary>
        public GateResult Gate;

        /// <summary>The planned reaction (the record of the section) when <see cref="Status"/> is <see cref="PlanStatus.Planned"/>.</summary>
        public PendingReaction Reaction;

        /// <summary>How many rolls were made.</summary>
        public int Rolls;

        public bool IsPlanned
        {
            get { return Status == PlanStatus.Planned; }
        }
    }

    /// <summary>What one game day of the planner did (for the log and the tests).</summary>
    public struct TickReport
    {
        /// <summary>Reactions carried out.</summary>
        public int Launched;

        /// <summary>Reactions dropped (mail not delivered in time, the rival is gone, the event could not be fired, stale).</summary>
        public int Cancelled;

        /// <summary>Reactions whose date of carrying out was set from a delivery that the event did not announce.</summary>
        public int Synced;

        /// <summary>Reactions that are due but wait (the event system is not ready).</summary>
        public int Waiting;
    }

    /// <summary>
    /// The planner of the reactions (design.md, Component 8; FR-6, FR-7 items 1, 8, 9): when a trigger of the player is recorded, decides whether the
    /// rival answers, and plans the answer with a warning. Pure: the game is behind <see cref="IPlannerEnvironment"/>, the intelligence mail behind
    /// <see cref="IIntelOutput"/>, the carrying out behind <see cref="IReactionLauncher"/>, the dice are a <see cref="IModRng"/>.
    /// <list type="number">
    /// <item><b>Rows in the order of the table</b> of the archetype for the trigger. A row is tried only if it may happen at all: it has a segment
    /// (a price cut also a CPU to cut), and the limits of FR-6 let it through (<see cref="ReactionMath.Gate"/>: the grace period, the cooldown of
    /// the rival, two at a time, four a year, the attitude gate unless the row says <c>anyAttitude</c>, no second reaction of the same type). A limit
    /// that holds for every row ends the search, one that is about this row only moves on to the next row.</item>
    /// <item><b>The roll</b> (<see cref="ReactionMath.Chance"/>, one draw per row tried): the first row that succeeds is planned, the rest are not
    /// considered.</item>
    /// <item><b>The warning.</b> The reaction gets an id, the number of days of the warning is rolled now (14..45) and the intelligence mail is
    /// sent (<c>Normal</c>, tag <c>rival:intel:&lt;id&gt;</c>). The day it is carried out is <b>not</b> known yet: the delivery of the mail starts the
    /// count (<see cref="OnMailDelivered"/>, D12), so a busy inbox never shortens the warning. A mail that is not delivered in
    /// <c>mailWaitDays</c> (30) days cancels the reaction.</item>
    /// <item><b>Every day</b> (<see cref="DailyTick"/>): a due reaction is handed to the launcher; the reactions of a bankrupt or put-aside rival are
    /// dropped; a reaction that waited more than <c>mailWaitDays</c> past its day (the event system was off) is dropped as stale.</item>
    /// </list>
    /// Without the event system nothing is planned and one line goes to the log per session (<c>reactions disabled: event-system unavailable</c>,
    /// FR-7 item 8). Main thread only.
    /// </summary>
    public sealed class ReactionPlannerCore
    {
        /// <summary>The line written once per session when a trigger comes and the event system cannot carry a reaction out (AC-7.4).</summary>
        public const string NoEventSystemLine = "reactions disabled: event-system unavailable";

        private readonly Func<ArchetypeCatalog> _catalog;
        private readonly PersonalityBook _book;
        private readonly AttitudeLedger _ledger;
        private readonly ReactionBook _reactions;
        private readonly IPlannerEnvironment _env;
        private readonly IIntelOutput _intel;
        private readonly IReactionLauncher _launcher;
        private readonly Func<IModRng> _rng;
        private readonly Action<string> _log;
        private readonly Action<string> _debug;
        private bool _noEventSystemLogged;

        /// <param name="catalog">The archetypes and the numbers; null or a null result gives an empty catalog.</param>
        /// <param name="book">The characters (the archetype of a rival, who is put aside).</param>
        /// <param name="ledger">The attitudes.</param>
        /// <param name="reactions">The lists and counters of the section.</param>
        /// <param name="env">The game.</param>
        /// <param name="intel">The intelligence mail.</param>
        /// <param name="launcher">The executor.</param>
        /// <param name="rng">The random stream of the feature (asked at every roll: a load replaces the generator).</param>
        /// <param name="log">One English line per planned, carried out and dropped reaction and per cap that held one back; may be null.</param>
        /// <param name="debug">One English line per refusal for a lesser reason (the grace period, the cooldown, the attitude); may be null.</param>
        public ReactionPlannerCore(Func<ArchetypeCatalog> catalog, PersonalityBook book, AttitudeLedger ledger, ReactionBook reactions,
                                   IPlannerEnvironment env, IIntelOutput intel, IReactionLauncher launcher, Func<IModRng> rng,
                                   Action<string> log = null, Action<string> debug = null)
        {
            if (book == null) throw new ArgumentNullException("book");
            if (ledger == null) throw new ArgumentNullException("ledger");
            if (reactions == null) throw new ArgumentNullException("reactions");
            if (env == null) throw new ArgumentNullException("env");
            if (intel == null) throw new ArgumentNullException("intel");
            if (launcher == null) throw new ArgumentNullException("launcher");
            if (rng == null) throw new ArgumentNullException("rng");

            _catalog = catalog;
            _book = book;
            _ledger = ledger;
            _reactions = reactions;
            _env = env;
            _intel = intel;
            _launcher = launcher;
            _rng = rng;
            _log = log;
            _debug = debug;
        }

        private RivalConfig Config
        {
            get
            {
                ArchetypeCatalog catalog = _catalog != null ? _catalog() : null;
                RivalConfig config = catalog != null ? catalog.Config : null;
                return config ?? RivalConfig.Defaults();
            }
        }

        /// <summary>The reactions that are planned and not carried out yet, a copy.</summary>
        public IReadOnlyList<PendingReaction> Pending
        {
            get { return _reactions.PendingSnapshot(); }
        }

        // ---- the trigger ----

        /// <summary>
        /// A trigger was recorded for a rival (the attitude has been changed already): rolls its reaction table and plans the first success. See
        /// the class for the order and the limits.
        /// </summary>
        public PlanResult OnTrigger(PlayerTrigger trigger)
        {
            var result = new PlanResult { Gate = GateResult.Allowed };
            if (_env.Blocked)
            {
                result.Status = PlanStatus.Blocked;
                return result;
            }

            int uid = trigger.UniqueId;
            Archetype archetype = _book.ArchetypeOf(uid);
            if (archetype == null)
            {
                result.Status = PlanStatus.NoArchetype;
                return result;
            }

            IReadOnlyList<ReactionRule> rules = archetype.RulesFor(trigger.Type);
            if (rules.Count == 0)
            {
                result.Status = PlanStatus.NoRules;
                return result;
            }

            DateTime today = _env.Today;
            RivalConfig config = Config;
            bool force = _env.ForceReactions;
            result.Status = PlanStatus.NoCandidate;

            for (int i = 0; i < rules.Count; i++)
            {
                ReactionRule rule = rules[i];
                ReactionType kind = rule.Kind;
                Segment segment = SegmentFor(kind, trigger.Segment, uid);

                if (NeedsSegment(kind) && segment == Segment.Any)
                {
                    Debug("row " + rule.type + " of " + archetype.id + " for ai:" + Text(uid) + " skipped: no segment to act in");
                    continue;
                }
                if (kind == ReactionType.PriceCut && !_env.SellsCpuIn(uid, segment))
                {
                    Debug("row PriceCut of ai:" + Text(uid) + " skipped: it sells no CPU in segment " + Text((int)segment));
                    continue;
                }

                GateResult gate = GateOf(uid, archetype, rule, kind, today, config, force);
                if (gate != GateResult.Allowed)
                {
                    NoteRefusal(gate, uid, kind, trigger.Type);
                    result.Status = PlanStatus.Refused;
                    result.Gate = gate;
                    if (IsAboutThisRowOnly(gate)) continue;
                    return result;
                }

                float chance = ChanceOf(rule, kind, uid, config, force);
                result.Rolls++;
                if (!_rng().Chance(chance))
                {
                    result.Status = PlanStatus.Missed;
                    result.Gate = GateResult.Allowed;
                    continue;
                }

                // the first row that succeeds is the answer; the rest are not considered (FR-6 item 1)
                PendingReaction pending = Plan(uid, kind, segment, trigger.Type, today, config, force, force ? 1 : 0, result);
                if (pending == null) return result;
                result.Status = PlanStatus.Planned;
                result.Gate = GateResult.Allowed;
                result.Reaction = pending;
                return result;
            }
            return result;
        }

        /// <summary>
        /// Plans a reaction at once, for the debug page of the hub (design.md, "Отладочная страница"): no roll, no limit but the three that cannot
        /// be set aside (the event system is available, the rival is not bankrupt and not put aside, no reaction of the same type is on its way),
        /// the warning is one day. <see cref="Segment.Any"/> is replaced by the main segment of the rival for the reactions that need one.
        /// </summary>
        public PlanResult PlanManual(int uniqueId, ReactionType type, Segment segment)
        {
            var result = new PlanResult { Gate = GateResult.Allowed, Status = PlanStatus.NoCandidate };
            if (!ReactionKinds.IsTableReaction(type)) return result;

            Archetype archetype = _book.ArchetypeOf(uniqueId);
            if (archetype == null)
            {
                result.Status = PlanStatus.NoArchetype;
                return result;
            }

            DateTime today = _env.Today;
            RivalConfig config = Config;
            Segment resolved = SegmentFor(type, segment, uniqueId);
            if (NeedsSegment(type) && resolved == Segment.Any) return result;

            // a friendly gate: the limits of a hostile reaction are the ones the debug page exists to skip
            var input = new GateInput
            {
                Hostile = false,
                EventSystemAvailable = _env.EventSystemAvailable,
                RivalBankrupt = _env.IsBankrupt(uniqueId),
                RivalDormant = _book.IsDormant(uniqueId),
                SameTypeActiveOrPending = _reactions.HasInFlight(uniqueId, type),
            };
            GateResult gate = ReactionMath.Gate(in input, config);
            if (gate != GateResult.Allowed)
            {
                NoteRefusal(gate, uniqueId, type, null);
                result.Status = PlanStatus.Refused;
                result.Gate = gate;
                return result;
            }

            PendingReaction pending = Plan(uniqueId, type, resolved, null, today, config, true, 1, result);
            if (pending == null) return result;
            result.Status = PlanStatus.Planned;
            result.Reaction = pending;
            return result;
        }

        // ---- the mail ----

        /// <summary>
        /// An intelligence mail reached the inbox (<c>ModMail.Delivered</c>): the day the reaction is carried out is the day of the delivery plus
        /// the days of the warning rolled at the planning (FR-7 item 1, AC-7.6). True when a planned reaction got its day; false for another mail,
        /// for a reaction that is gone and for one that has its day already.
        /// </summary>
        public bool OnMailDelivered(string tag, DateTime? deliveredOn)
        {
            string reactionId;
            if (!deliveredOn.HasValue || !ReactionTags.TryParseIntel(tag, out reactionId)) return false;

            PendingReaction pending = _reactions.FindPending(reactionId);
            if (pending == null || !string.IsNullOrEmpty(pending.fireOn)) return false;

            SetFireOn(pending, deliveredOn.Value);
            Debug("intel mail of " + pending.id + " delivered on " + RivalDates.ToText(deliveredOn.Value) + "; it is carried out on " + pending.fireOn);
            return true;
        }

        // ---- every day ----

        /// <summary>
        /// One game day (<c>TickPhase.DayPassed</c>): catches up with a delivery the event did not announce, carries out the reactions that are due,
        /// drops the ones that cannot go on (the mail was not delivered in <c>mailWaitDays</c> days, the rival is bankrupt or put aside, the day
        /// of carrying out is more than <c>mailWaitDays</c> days past). Cheap: it walks the planned reactions (at most 20).
        /// </summary>
        public TickReport DailyTick()
        {
            var report = new TickReport();
            DateTime today = _env.Today;
            if (today == default(DateTime)) return report;

            RivalConfig config = Config;
            _reactions.RollYear(today);

            foreach (PendingReaction pending in _reactions.PendingSnapshot())
            {
                if (_env.IsBankrupt(pending.uid))
                {
                    Cancel(pending, "the rival is bankrupt");
                    report.Cancelled++;
                    continue;
                }
                if (_book.IsDormant(pending.uid))
                {
                    Cancel(pending, "the rival is put aside");
                    report.Cancelled++;
                    continue;
                }

                if (string.IsNullOrEmpty(pending.fireOn))
                {
                    DateTime planned;
                    if (!RivalDates.TryParse(pending.plannedOn, out planned))
                    {
                        Cancel(pending, "the date of the planning cannot be read");
                        report.Cancelled++;
                        continue;
                    }

                    DateTime? delivered = _env.MailDeliveredOn(pending.mailId, ReactionTags.Intel(pending.id));
                    if (delivered.HasValue)
                    {
                        SetFireOn(pending, delivered.Value);
                        report.Synced++;
                    }
                    else if (RivalDates.DaysBetween(planned, today) >= config.mailWaitDays)
                    {
                        Cancel(pending, "the intelligence mail was not delivered in " + Text(config.mailWaitDays) + " days");
                        report.Cancelled++;
                        continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                DateTime fireDate;
                if (!RivalDates.TryParse(pending.fireOn, out fireDate))
                {
                    Cancel(pending, "the day of carrying out cannot be read");
                    report.Cancelled++;
                    continue;
                }
                if (today.Date < fireDate.Date) continue;

                if (RivalDates.DaysBetween(fireDate, today) > config.mailWaitDays)
                {
                    Cancel(pending, "overdue: the day of carrying out passed " + Text(RivalDates.DaysBetween(fireDate, today)) + " days ago");
                    report.Cancelled++;
                    continue;
                }
                if (!_env.EventSystemAvailable)
                {
                    report.Waiting++;
                    NoteNoEventSystem();
                    continue;
                }

                string reason;
                LaunchOutcome outcome = _launcher.Launch(pending, out reason);
                if (outcome == LaunchOutcome.Started)
                {
                    _reactions.RemovePending(pending.id);
                    report.Launched++;
                }
                else if (outcome == LaunchOutcome.Retry)
                {
                    report.Waiting++;
                }
                else
                {
                    Cancel(pending, reason ?? "the event could not be fired");
                    report.Cancelled++;
                }
            }
            return report;
        }

        // ---- dropping ----

        /// <summary>
        /// Drops everything of the rival that is planned and takes back what is in effect (bankruptcy, <c>SetDormant</c>; design.md, Component 8
        /// <c>CancelFor</c>). Returns how many reactions were dropped.
        /// </summary>
        public int CancelFor(int uniqueId, string reason)
        {
            int count = 0;
            foreach (PendingReaction pending in _reactions.PendingSnapshot())
            {
                if (pending.uid != uniqueId) continue;
                Cancel(pending, reason);
                count++;
            }
            foreach (ActiveReaction active in _reactions.ActiveOf(uniqueId))
            {
                _launcher.Abort(active, reason);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Drops the reactions of every rival the environment calls bankrupt (a company that went bankrupt while its reaction was planned or in effect).
        /// Returns how many.
        /// </summary>
        public int CancelBankrupt()
        {
            var uids = new List<int>();
            foreach (PendingReaction pending in _reactions.PendingSnapshot())
            {
                if (!uids.Contains(pending.uid) && _env.IsBankrupt(pending.uid)) uids.Add(pending.uid);
            }
            foreach (ActiveReaction active in _reactions.ActiveSnapshot())
            {
                if (!uids.Contains(active.uid) && _env.IsBankrupt(active.uid)) uids.Add(active.uid);
            }

            int count = 0;
            foreach (int uid in uids) count += CancelFor(uid, "the rival is bankrupt");
            return count;
        }

        // ---- the warning in weeks ----

        /// <summary>
        /// The range of weeks the intelligence mail names (AC-7.1: 14..45 days is "2-7 weeks"): the lowest warning rounded down, the highest up,
        /// at least one week. A forced (debug) reaction has a warning of one day: 1..1.
        /// </summary>
        public static void WeekRange(RivalConfig config, bool forced, out int from, out int to)
        {
            if (forced)
            {
                from = 1;
                to = 1;
                return;
            }

            int[] days = (config ?? RivalConfig.Defaults()).telegraphDays;
            int low = days != null && days.Length > 0 ? days[0] : 14;
            int high = days != null && days.Length > 1 ? days[1] : 45;
            from = Math.Max(1, low / 7);
            to = Math.Max(from, (high + 6) / 7);
        }

        // ---- internals ----

        private PendingReaction Plan(int uid, ReactionType kind, Segment segment, TriggerType? trigger, DateTime today, RivalConfig config, bool force,
                                     int telegraphOverride, PlanResult result)
        {
            int telegraph = telegraphOverride > 0 ? telegraphOverride : RollTelegraph(config);
            var pending = new PendingReaction
            {
                id = _reactions.NextId(),
                uid = uid,
                type = ReactionKinds.NameOf(kind),
                segment = (int)segment,
                trigger = trigger.HasValue ? ReactionKinds.NameOf(trigger.Value) : "Manual",
                plannedOn = RivalDates.ToText(today),
                mailId = 0,
                telegraphDays = telegraph,
                fireOn = string.Empty,
            };
            if (!_reactions.AddPending(pending))
            {
                Say("reaction " + pending.type + " of ai:" + Text(uid) + " not planned: " + Text(ReactionBook.MaxPending) + " reactions wait already");
                result.Status = PlanStatus.Refused;
                result.Gate = GateResult.ConcurrentCap;
                return null;
            }

            int weeksFrom, weeksTo;
            WeekRange(config, force, out weeksFrom, out weeksTo);
            int mailId = _intel.SendIntel(new IntelNotice(pending.id, uid, kind, segment, weeksFrom, weeksTo));
            if (mailId <= 0)
            {
                _reactions.RemovePending(pending.id);
                Say("reaction " + pending.type + " of ai:" + Text(uid) + " not planned: the intelligence mail was not accepted");
                result.Status = PlanStatus.MailRefused;
                return null;
            }
            pending.mailId = mailId;

            _reactions.AddHistory(uid, today, pending.type, pending.segment, HistoryOutcomes.Planned);
            Say("planned " + pending.type + " of ai:" + Text(uid) + " (" + pending.id + ", segment " + Text(pending.segment) + ", trigger " + pending.trigger
                + ", warning " + Text(telegraph) + " days after the mail #" + Text(mailId) + " is delivered)");
            return pending;
        }

        private int RollTelegraph(RivalConfig config)
        {
            int low = config.telegraphDays[0];
            int high = config.telegraphDays[1];
            return _rng().Range(low, high + 1);
        }

        private void SetFireOn(PendingReaction pending, DateTime deliveredOn)
        {
            int days = pending.telegraphDays < 1 ? 1 : pending.telegraphDays;
            pending.fireOn = RivalDates.ToText(deliveredOn.Date.AddDays(days));
        }

        private void Cancel(PendingReaction pending, string reason)
        {
            _reactions.RemovePending(pending.id);
            ReactionType kind;
            string type = ReactionKinds.TryParseReaction(pending.type, out kind) ? ReactionKinds.NameOf(kind) : (pending.type ?? string.Empty);
            _reactions.AddHistory(pending.uid, _env.Today, type, pending.segment, HistoryOutcomes.Cancelled);
            Say("cancelled " + type + " of ai:" + Text(pending.uid) + " (" + pending.id + "): " + reason);
        }

        private GateResult GateOf(int uid, Archetype archetype, ReactionRule rule, ReactionType kind, DateTime today, RivalConfig config, bool force)
        {
            var input = new GateInput
            {
                Hostile = ReactionKinds.IsHostile(kind),
                AnyAttitude = rule != null && rule.anyAttitude,
                Attitude = _ledger.Get(uid),
                DaysSinceGraceStart = force ? int.MaxValue : DaysSinceGraceStart(today),
                // a rival with a reaction on its way is in its cooldown already: one answer at a time
                DaysSinceLastReactionOfRival = _reactions.HasPending(uid) ? 0 : _reactions.DaysSinceLastReaction(uid, today),
                CooldownDays = archetype.cooldownDays,
                ActiveOrPendingHostile = _reactions.HostileInFlight(),
                HostileExecutedThisYear = _reactions.HostileExecutedThisYear(today),
                SameTypeActiveOrPending = _reactions.HasInFlight(uid, kind),
                RivalBankrupt = _env.IsBankrupt(uid),
                RivalDormant = _book.IsDormant(uid),
                EventSystemAvailable = _env.EventSystemAvailable,
            };
            return ReactionMath.Gate(in input, config);
        }

        private float ChanceOf(ReactionRule rule, ReactionType kind, int uid, RivalConfig config, bool force)
        {
            if (force) return 1f;
            float extra = kind == ReactionType.Poach && _env.PlayerResearchUnderfunded ? config.reactions.Poach.underfundedChanceMult : 1f;
            return ReactionMath.Chance(rule.@base, _ledger.Get(uid), _env.Difficulty, config, extra);
        }

        private int DaysSinceGraceStart(DateTime today)
        {
            // the grace period starts on the first day of the game (or the first load of an old save); a start that is not known holds everything back
            DateTime start;
            if (!RivalDates.TryParse(_reactions.GraceStart, out start)) return 0;
            int days = RivalDates.DaysBetween(start, today);
            return days < 0 ? 0 : days;
        }

        private Segment SegmentFor(ReactionType kind, Segment fromTrigger, int uid)
        {
            if (!NeedsSegment(kind) || fromTrigger != Segment.Any) return fromTrigger;
            return _env.MainSegmentOf(uid);
        }

        private static bool NeedsSegment(ReactionType kind)
        {
            return kind == ReactionType.PriceCut || kind == ReactionType.CounterLaunch;
        }

        /// <summary>The attitude gate and the same-type rule belong to one row of the table; every other limit holds for all of them.</summary>
        private static bool IsAboutThisRowOnly(GateResult gate)
        {
            return gate == GateResult.AttitudeTooHigh || gate == GateResult.SameTypeActive;
        }

        private void NoteRefusal(GateResult gate, int uid, ReactionType kind, TriggerType? trigger)
        {
            string what = ReactionKinds.NameOf(kind) + " of ai:" + Text(uid) + (trigger.HasValue ? " on " + ReactionKinds.NameOf(trigger.Value) : string.Empty);
            switch (gate)
            {
                case GateResult.NoEventSystem:
                    NoteNoEventSystem();
                    break;
                case GateResult.ConcurrentCap:
                case GateResult.YearlyCap:
                    Say(what + " not planned: cap reached (" + gate + ")");
                    break;
                default:
                    Debug(what + " not planned: " + gate);
                    break;
            }
        }

        private void NoteNoEventSystem()
        {
            if (_noEventSystemLogged) return;
            _noEventSystemLogged = true;
            Say(NoEventSystemLine);
        }

        private void Say(string line)
        {
            if (_log != null) _log(line);
        }

        private void Debug(string line)
        {
            if (_debug != null) _debug(line);
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
