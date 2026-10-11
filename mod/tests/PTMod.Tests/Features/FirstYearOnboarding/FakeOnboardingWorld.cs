using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The world of the machine of the tutorial for the scenario tests (TSK-010): a date the test moves, the blocking states as switches, the mail
    /// that reached the inbox as a set of ids, and the conditions of the steps parsed by the real parser of the core over the fake game state of the
    /// core and the fake facts, so the shipped <c>steps.json</c> and a hand-made catalog behave as in the game. Internal: the runner never discovers it.
    /// </summary>
    internal sealed class FakeOnboardingWorld : IOnboardingWorld
    {
        private readonly ConditionParser _parser;

        public readonly FakeGameState State = new FakeGameState();
        public readonly FakeFacts Facts = new FakeFacts();

        /// <summary>The game date; the test moves it (<see cref="OnbSim.Day"/>).</summary>
        public DateTime Date;

        public bool Blocked;
        public bool Batch;

        /// <summary>What <see cref="DetectTrack"/> answers; null for "the company cannot be read".</summary>
        public string Track = "idm";

        /// <summary>The ids of the mails that reached the inbox.</summary>
        public readonly HashSet<int> Delivered = new HashSet<int>();

        /// <summary>The answer of <c>onb.stepDone</c>; the sim points it at the state of the machine.</summary>
        public Func<string, bool> StepDone = id => false;

        public int EvaluateCalls;
        public int DetectTrackCalls;
        public int DeliveredChecks;
        public readonly List<string> ParseErrors = new List<string>();

        public FakeOnboardingWorld(DateTime start)
        {
            Date = start;
            State.Start = start;

            PredicateRegistry registry = Predicates.CreateRegistry();
            var facts = new LiveFacts(this);
            foreach (var pair in OnboardingPredicateLibrary.CreateFactories(facts))
                Assert.True(registry.Register(pair.Key, "first-year-onboarding", pair.Value), "predicate refused: " + pair.Key);
            _parser = new ConditionParser(registry);
        }

        public bool IsBlocked { get { return Blocked; } }

        public bool IsBatchRun { get { return Batch; } }

        public DateTime GameDate { get { return Date; } }

        public string DetectTrack()
        {
            DetectTrackCalls++;
            return Track;
        }

        public bool Evaluate(ConditionRef condition)
        {
            EvaluateCalls++;
            if (condition == null || condition.Node == null || condition.CompileFailed) return false;

            var compiled = condition.Compiled as ICondition;
            if (compiled == null)
            {
                var errors = new List<string>();
                compiled = _parser.Parse(condition.Node, errors, "$");
                if (compiled == null)
                {
                    condition.CompileFailed = true;
                    ParseErrors.AddRange(errors);
                    return false;
                }
                condition.Compiled = compiled;
            }
            return ConditionEvaluator.Evaluate(compiled, State.Context(Date));
        }

        public bool IsMailDelivered(int mailId)
        {
            DeliveredChecks++;
            return Delivered.Contains(mailId);
        }

        /// <summary>The facts of the game: the switches of <see cref="FakeFacts"/>, except that <c>onb.stepDone</c> reads the state of the machine.</summary>
        private sealed class LiveFacts : IOnboardingFacts
        {
            private readonly FakeOnboardingWorld _world;

            public LiveFacts(FakeOnboardingWorld world)
            {
                _world = world;
            }

            public bool CpuProjectActive() { return _world.Facts.CpuProjectActive(); }
            public bool FactoryProjectActive() { return _world.Facts.FactoryProjectActive(); }
            public bool ProducedToday() { return _world.Facts.ProducedToday(); }
            public bool SoldToday() { return _world.Facts.SoldToday(); }
            public bool MissedOverSold() { return _world.Facts.MissedOverSold(); }
            public bool AnyOldCpu() { return _world.Facts.AnyOldCpu(); }
            public bool OemAvailable() { return _world.Facts.OemAvailable(); }
            public bool Automate() { return _world.Facts.Automate(); }
            public bool FoundryServices() { return _world.Facts.FoundryServices(); }
            public bool IsStepDone(string stepId) { return _world.StepDone(stepId); }
        }
    }

    /// <summary>A mail the machine asked the output to send.</summary>
    internal sealed class SentMail
    {
        public int Id;
        public string StepId;
        public string Track;
        public DateTime Date;
        public string[] CaughtUp = new string[0];
        public bool IsRepeat;
        public bool IsSummary;
        public bool Delivered;

        public string Tag
        {
            get { return "onb:" + StepId + (IsRepeat ? ":repeat" : string.Empty); }
        }

        public override string ToString()
        {
            return Tag + " #" + Id + " on " + Date.ToString("yyyy-MM-dd") + (CaughtUp.Length > 0 ? " (already: " + string.Join(",", CaughtUp) + ")" : string.Empty);
        }
    }

    /// <summary>The output of the machine that records what it was asked to do (what <c>OnboardingMailer</c> would send and show).</summary>
    internal sealed class FakeOnboardingOutput : IOnboardingOutput
    {
        private readonly FakeOnboardingWorld _world;

        public readonly List<SentMail> Mails = new List<SentMail>();

        /// <summary>The hints shown, as <c>stepId#index</c>.</summary>
        public readonly List<string> Hints = new List<string>();

        /// <summary>The calls of <c>SetHintAvailable</c>: mail id and the new state.</summary>
        public readonly List<KeyValuePair<int, bool>> HintSwitches = new List<KeyValuePair<int, bool>>();

        public readonly List<OnboardingResult> Completed = new List<OnboardingResult>();
        public int Started;

        /// <summary>Every call of the output, whatever it was.</summary>
        public int Calls;

        /// <summary>The next sends fail (return 0) while this is above 0; each failed send counts it down.</summary>
        public int FailSends;

        /// <summary>The summary mails fail while this is true.</summary>
        public bool FailSummary;

        public int SendAttempts;
        public int NextId = 100000;

        public FakeOnboardingOutput(FakeOnboardingWorld world)
        {
            _world = world;
        }

        public int SendStepMail(StepDefinition step, string track, IList<string> caughtUp, bool isRepeat)
        {
            Calls++;
            SendAttempts++;
            if (FailSends > 0)
            {
                FailSends--;
                return 0;
            }

            var mail = new SentMail
            {
                Id = NextId++,
                StepId = step.Id,
                Track = track,
                Date = _world.Date,
                CaughtUp = caughtUp.ToArray(),
                IsRepeat = isRepeat
            };
            Mails.Add(mail);
            return mail.Id;
        }

        public int SendSummaryMail(OnboardingState state, StepCatalog catalog)
        {
            Calls++;
            SendAttempts++;
            if (FailSummary) return 0;

            var mail = new SentMail { Id = NextId++, StepId = OnboardingMachine.GraduationStepId, Track = state.Track, Date = _world.Date, IsSummary = true };
            Mails.Add(mail);
            return mail.Id;
        }

        public void ShowHint(StepDefinition step, int hintIndex)
        {
            Calls++;
            Hints.Add(step.Id + "#" + hintIndex);
        }

        public void SetHintAvailable(int mailId, bool available)
        {
            Calls++;
            HintSwitches.Add(new KeyValuePair<int, bool>(mailId, available));
        }

        public void NotifyStarted()
        {
            Calls++;
            Started++;
        }

        public void RaiseCompleted(OnboardingResult result)
        {
            Calls++;
            Completed.Add(result);
        }

        /// <summary>The ids of the steps whose mail went out, in order (repeats included).</summary>
        public string[] StepIds
        {
            get { return Mails.Select(m => m.StepId).ToArray(); }
        }

        /// <summary>The mails of the steps and the tips, not the repeats.</summary>
        public SentMail[] Originals
        {
            get { return Mails.Where(m => !m.IsRepeat).ToArray(); }
        }

        public SentMail MailOf(string stepId)
        {
            return Mails.LastOrDefault(m => m.StepId == stepId && !m.IsRepeat);
        }
    }

    /// <summary>
    /// A machine over a fake world and a recording output, with a clock: <see cref="Day"/> moves the date by one, lets the core deliver the mail that was
    /// queued (as <c>ModMail</c> does on <c>DayPassed</c>) and calls the machine like the director does. The test arranges the state of the game through
    /// <see cref="World"/> and reads the result from <see cref="Out"/> and <see cref="State"/>.
    /// </summary>
    internal sealed class OnbSim
    {
        public static readonly DateTime Start1975 = new DateTime(1975, 1, 1);

        public readonly FakeOnboardingWorld World;
        public readonly FakeOnboardingOutput Out;
        public readonly StepCatalog Catalog;
        public readonly List<string> Log = new List<string>();

        public OnboardingMachine Machine;

        /// <summary>The mail queued on one day reaches the inbox when the next day begins (the usual case of the core).</summary>
        public bool AutoDeliver = true;

        public OnbSim(StepCatalog catalog, string track = "idm", OnboardingState state = null, DateTime? start = null)
        {
            Catalog = catalog;
            World = new FakeOnboardingWorld(start ?? Start1975) { Track = track };
            Out = new FakeOnboardingOutput(World);
            Machine = NewMachine(state ?? new OnboardingState());
        }

        public OnboardingState State
        {
            get { return Machine.State; }
        }

        public FakeGameState.FakeCompany Player
        {
            get { return World.State.Player; }
        }

        public DateTime Today
        {
            get { return World.Date; }
        }

        public OnboardingMachine NewMachine(OnboardingState state)
        {
            var machine = new OnboardingMachine(Catalog, state) { Log = Log.Add };
            World.StepDone = id => Machine.State.IsStepClosed(id);
            return machine;
        }

        /// <summary>Begins a run (<c>GameReady(NewGame)</c>).</summary>
        public void Start(string source = "menu")
        {
            Machine.Start(World, Out, source);
        }

        public void Poll()
        {
            Machine.Poll(World, Out);
        }

        /// <summary>One game day: the date moves, the queued mail is delivered, the machine hears of the day.</summary>
        public void Day()
        {
            World.Date = World.Date.AddDays(1);
            if (AutoDeliver) DeliverQueued();
            Machine.OnGameDay(World, Out);
        }

        public void Days(int count)
        {
            for (int i = 0; i < count; i++) Day();
        }

        /// <summary>A day as the director sees it, with a poll after the tick, the way the real poll runs between ticks.</summary>
        public void DayAndPoll()
        {
            Day();
            Poll();
        }

        /// <summary>Delivers the mail that was sent before today (not the repeats): the id enters the inbox and the machine is told.</summary>
        public void DeliverQueued()
        {
            foreach (SentMail mail in Out.Mails)
            {
                if (mail.Delivered || mail.IsRepeat || mail.Date >= World.Date) continue;
                Deliver(mail);
            }
        }

        public void Deliver(SentMail mail)
        {
            mail.Delivered = true;
            World.Delivered.Add(mail.Id);
            Machine.OnMailDelivered(mail.StepId, mail.Id, World.Date);
        }

        /// <summary>Delivers every mail sent so far, today's included.</summary>
        public void DeliverAll()
        {
            foreach (SentMail mail in Out.Mails.Where(m => !m.Delivered && !m.IsRepeat).ToArray()) Deliver(mail);
        }

        public StepStatus Status(string stepId)
        {
            return State.StatusOf(stepId);
        }
    }

    /// <summary>Builders of hand-made steps and catalogs for the machine tests. Internal: the runner never discovers it.</summary>
    internal static class OnbSteps
    {
        public static readonly string[] Idm = { "idm" };

        /// <summary>A sequential step done by a condition (a JSON with single quotes).</summary>
        public static StepDefinition Seq(string id, int order, string completeWhen, params string[] tracks)
        {
            return new StepDefinition
            {
                Id = id,
                Order = order,
                Kind = StepKind.Sequential,
                Tracks = tracks.Length == 0 ? Idm : tracks,
                CompleteWhen = OnbJson.Cond(completeWhen),
                Mail = new MailSpec { TitleKey = "onb." + id + ".title", BodyKey = "onb." + id + ".body" }
            };
        }

        /// <summary>An information step: done when its mail is shown.</summary>
        public static StepDefinition Info(string id, int order, params string[] tracks)
        {
            StepDefinition step = Seq(id, order, "{'type':'researchActive'}", tracks);
            step.CompleteWhen = null;
            step.CompleteOnShow = true;
            return step;
        }

        /// <summary>A situational tip shown once when its condition holds (for <paramref name="streakDays"/> game days in a row, if above 0).</summary>
        public static StepDefinition Tip(string id, string showWhen, int streakDays = 0, params string[] tracks)
        {
            StepDefinition step = Seq(id, 0, "{'type':'researchActive'}", tracks);
            step.Kind = StepKind.Situational;
            step.ShowWhen = OnbJson.Cond(showWhen);
            step.CompleteWhen = null;
            step.CompleteOnShow = true;
            step.StreakDays = streakDays;
            return step;
        }

        public static StepDefinition WithReminders(this StepDefinition step, params int[] afterDays)
        {
            step.Reminders = afterDays
                .Select((days, i) => new ReminderSpec { AfterDays = days, HintKey = "onb." + step.Id + ".hint" + (i + 1) })
                .ToArray();
            return step;
        }

        public static StepDefinition ShownWhen(this StepDefinition step, string showWhen)
        {
            step.ShowWhen = OnbJson.Cond(showWhen);
            return step;
        }

        /// <summary>Builds the catalog without the game layer's checks of strings and conditions (the machine tests need the structure only).</summary>
        public static StepCatalog Catalog(params StepDefinition[] steps)
        {
            return Catalog(null, steps);
        }

        public static StepCatalog Catalog(OnboardingConfig config, params StepDefinition[] steps)
        {
            StepCatalog catalog = StepCatalog.Build(new List<StepDefinition>(steps), config ?? new OnboardingConfig(), null);
            // A track with no step at all is reported too (the hand-made catalogs rarely cover all three); a step that was dropped is a mistake of the test.
            Assert.False(catalog.Errors.Any(e => e.StartsWith("step", StringComparison.Ordinal)),
                "the test catalog is not valid: " + string.Join(" | ", catalog.Errors));
            return catalog;
        }

        /// <summary>
        /// A short track used by most machine tests: welcome (information), then four steps done by a condition, then the closing step.
        /// <c>research</c> has two reminders (15 and 40 days).
        /// </summary>
        public static StepCatalog ShortTrack()
        {
            return Catalog(
                Info("welcome", 10),
                Seq("research", 20, "{'type':'researchActive'}").WithReminders(15, 40),
                Seq("design_cpu", 30, "{'type':'onb.cpuProjectActive'}").WithReminders(20),
                Seq("release", 40, "{'type':'cpuCountAtLeast','count':1}"),
                Seq("sales", 50, "{'type':'onb.soldToday'}").WithReminders(10, 30),
                Info("graduation", 100));
        }
    }
}
