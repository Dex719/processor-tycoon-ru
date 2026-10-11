using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The planner and the executor of the reactions on the shipped content, a plain state and fakes of the game, for the tests of TSK-013..016 and TSK-028.
    /// Internal, so the runner never discovers it. The company numbers are the <c>UniqueID</c> of <c>assignments.json</c> (see <see cref="AttitudeRig"/>).
    /// </summary>
    internal sealed class ReactionRig
    {
        /// <summary>The first day of the game: the grace period starts here.</summary>
        public static readonly DateTime GameStart = new DateTime(1980, 1, 1);

        public readonly AttitudeRig Base;
        public readonly ReactionBook Reactions;
        public readonly FakeEnv Env = new FakeEnv();
        public readonly FakeIntel Intel = new FakeIntel();
        public readonly FakeRuntime Runtime;
        public readonly ScriptedRng Rng = new ScriptedRng();
        public readonly ReactionExecutorCore Executor;
        public readonly ReactionPlannerCore Planner;
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Debug = new List<string>();

        public ReactionRig()
        {
            Base = new AttitudeRig().AssignAll();
            Reactions = new ReactionBook(() => Base.State);
            Runtime = new FakeRuntime(this);
            Executor = new ReactionExecutorCore(Reactions, Runtime, () => Env.Today, Log.Add);
            Planner = new ReactionPlannerCore(() => Base.Catalog, Base.Book, Base.Ledger, Reactions, Env, Intel, Executor, () => Rng, Log.Add, Debug.Add);

            Base.State.graceStart = RivalDates.ToText(GameStart);
            SetToday(GameStart.AddDays(400));   // well past the grace period of 180 days
        }

        public RivalState State
        {
            get { return Base.State; }
        }

        public void SetToday(DateTime today)
        {
            Env.Today = today;
            Base.Today = today;
        }

        public void AddDays(int days)
        {
            SetToday(Env.Today.AddDays(days));
        }

        /// <summary>A trigger of the player for a rival, as the recorder raises it.</summary>
        public PlayerTrigger Trigger(TriggerType type, int uid, Segment segment)
        {
            return new PlayerTrigger(type, uid, segment, null);
        }

        /// <summary>Plans a reaction of the rival: the next roll succeeds. Fails the test if it was not planned.</summary>
        public PendingReaction PlanOne(TriggerType trigger, int uid, Segment segment)
        {
            Rng.Chances.Enqueue(true);
            PlanResult result = Planner.OnTrigger(Trigger(trigger, uid, segment));
            Assert.True(result.IsPlanned, "not planned: " + result.Status + " " + result.Gate);
            return result.Reaction;
        }

        /// <summary>The mail of the reaction reached the inbox on <paramref name="day"/>.</summary>
        public bool Deliver(PendingReaction pending, DateTime day)
        {
            return Planner.OnMailDelivered(ReactionTags.Intel(pending.id), day);
        }

        public static string Key(int uid)
        {
            return "ai:" + uid.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The game and the settings as the planner sees them.</summary>
    internal sealed class FakeEnv : IPlannerEnvironment
    {
        public DateTime Today { get; set; }
        public int Difficulty { get; set; }
        public bool EventSystemAvailable { get; set; }
        public bool ForceReactions { get; set; }
        public bool Blocked { get; set; }
        public bool PlayerResearchUnderfunded { get; set; }

        public readonly HashSet<int> Bankrupt = new HashSet<int>();

        /// <summary>The rivals that sell no CPU in a segment: <c>uid * 10 + segment</c>.</summary>
        public readonly HashSet<int> NoCpu = new HashSet<int>();

        public readonly Dictionary<int, Segment> MainSegments = new Dictionary<int, Segment>();

        /// <summary>The day each mail (by id) was delivered.</summary>
        public readonly Dictionary<int, DateTime> Deliveries = new Dictionary<int, DateTime>();

        public FakeEnv()
        {
            Difficulty = 2;
            EventSystemAvailable = true;
        }

        public bool IsBankrupt(int uniqueId)
        {
            return Bankrupt.Contains(uniqueId);
        }

        public bool SellsCpuIn(int uniqueId, Segment segment)
        {
            return !NoCpu.Contains(uniqueId * 10 + (int)segment);
        }

        public Segment MainSegmentOf(int uniqueId)
        {
            Segment segment;
            return MainSegments.TryGetValue(uniqueId, out segment) ? segment : Segment.MidRange;
        }

        public DateTime? MailDeliveredOn(int mailId, string tag)
        {
            DateTime day;
            return Deliveries.TryGetValue(mailId, out day) ? day : (DateTime?)null;
        }
    }

    /// <summary>The intelligence mail: records what would be sent and gives out ids from 100.</summary>
    internal sealed class FakeIntel : IIntelOutput
    {
        public readonly List<IntelNotice> Sent = new List<IntelNotice>();
        public int NextId = 100;

        /// <summary>The mail is not accepted (<c>ModMail.Send</c> answers -1).</summary>
        public bool Refuse;

        public int SendIntel(IntelNotice notice)
        {
            if (Refuse) return -1;
            Sent.Add(notice);
            return NextId++;
        }
    }

    /// <summary>A random stream whose answers the test scripts; it records what it was asked.</summary>
    internal sealed class ScriptedRng : IModRng
    {
        public readonly Queue<bool> Chances = new Queue<bool>();

        /// <summary>The answer when the script is empty.</summary>
        public bool DefaultChance;

        public readonly List<float> Probabilities = new List<float>();
        public int RangeDraws;

        /// <summary>The number <see cref="Range"/> gives (held to the range asked); -1 gives the lowest.</summary>
        public int RangeValue = -1;

        public int Range(int minInclusive, int maxExclusive)
        {
            RangeDraws++;
            if (maxExclusive <= minInclusive) return minInclusive;
            if (RangeValue < 0) return minInclusive;
            return Math.Max(minInclusive, Math.Min(maxExclusive - 1, RangeValue));
        }

        public float Value()
        {
            return 0.5f;
        }

        public bool Chance(float probability)
        {
            Probabilities.Add(probability);
            return Chances.Count > 0 ? Chances.Dequeue() : DefaultChance;
        }

        public int WeightedIndex(IList<float> weights)
        {
            return -1;
        }

        public T Pick<T>(IList<T> items)
        {
            return items != null && items.Count > 0 ? items[0] : default(T);
        }
    }

    /// <summary>One call of <see cref="IReactionRuntime.Fire"/>.</summary>
    internal sealed class FireCall
    {
        public string EventId;
        public string CompanyKey;
        public string[] Args;
    }

    /// <summary>The game and the event system as the executor sees them.</summary>
    internal sealed class FakeRuntime : IReactionRuntime
    {
        private readonly ReactionRig _rig;

        public readonly List<FireCall> Fires = new List<FireCall>();
        public readonly List<string> StartedNotes = new List<string>();
        public readonly List<string> EndedNotes = new List<string>();
        public readonly List<string> Cleanups = new List<string>();
        public readonly List<int> RevealCalls = new List<int>();

        /// <summary>The answer of the event system.</summary>
        public FireResult Result = FireResult.Fired;

        /// <summary>Runs while the event fires (the handlers of the effects report here in the game).</summary>
        public Action<FireCall> WhileFiring;

        public int RevealedForTheFirstTime;

        public FakeRuntime(ReactionRig rig)
        {
            _rig = rig;
        }

        public FireResult Fire(string eventId, string companyKey, string[] args)
        {
            var call = new FireCall { EventId = eventId, CompanyKey = companyKey, Args = args };
            Fires.Add(call);
            if (WhileFiring != null) WhileFiring(call);
            return Result;
        }

        public string NameOf(int uniqueId)
        {
            return "Rival" + uniqueId.ToString(CultureInfo.InvariantCulture);
        }

        public string DetailFor(ReactionType type, int uniqueId, int segment)
        {
            return type == ReactionType.Poach ? "$250K" : segment.ToString(CultureInfo.InvariantCulture);
        }

        public int DurationDays(ReactionType type)
        {
            switch (type)
            {
                case ReactionType.PriceCut: return 180;
                case ReactionType.CounterLaunch: return 150;
                case ReactionType.OemPush: return 365;
                case ReactionType.Poach: return 10;
                default: return 0;
            }
        }

        public void Reveal(int uniqueId)
        {
            RevealCalls.Add(uniqueId);
            if (_rig.Base.Book.Reveal(uniqueId)) RevealedForTheFirstTime++;
        }

        public void NotifyStarted(ActiveReaction reaction, string[] details)
        {
            StartedNotes.Add(reaction.type + ":" + reaction.uid + ":" + string.Join(",", details ?? new string[0]));
        }

        public void NotifyEnded(ActiveReaction reaction)
        {
            EndedNotes.Add(reaction.type + ":" + reaction.uid);
        }

        public void Cleanup(ActiveReaction reaction)
        {
            Cleanups.Add(reaction.id);
        }
    }
}
