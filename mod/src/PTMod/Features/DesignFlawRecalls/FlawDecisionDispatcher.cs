using System;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The delivery of a decision to the player (FR-5, design.md Component 8): the game's side of <see cref="FlawDecisionEngine"/>. A flaw of the player that is
    /// known and waits for an answer gets one of the six events of the pack (<c>dfr.decide.*</c>) through <c>EventBus.Fire</c>; the letter, the window with the pause and
    /// the timer (counted from the day the letter is delivered) are the event system's. The record is found again by <c>Args[0]</c>. When the answer is made
    /// (<c>EventBus.OnChoiceResolved</c>) the next decision of the queue is opened. When the event system cannot show the window, the default choice is applied and an
    /// information letter (<c>ModMail</c>, <c>Normal</c>, tag <c>dfr:auto:&lt;recordId&gt;</c>) tells the player; the letter is not kept in the record.
    /// <para>
    /// The flaws of AI companies have no window: <see cref="DispatchAi"/> hands them to <see cref="FlawAiEngine"/> (FR-11, D8), which chooses by <see cref="AiFlawPolicy"/> at once, applies the
    /// answer with the rules of the player's and, when the incident is public, tells the player through <see cref="FlawNews"/>. This class is the game's side of both engines
    /// (<see cref="IFlawDecisionHost"/> for the player, <see cref="IFlawAiHost"/> for the AI).
    /// </para>
    /// </summary>
    internal sealed class FlawDecisionDispatcher : IFlawDecisionHost, IFlawAiHost
    {
        private const string FeatureId = DesignFlawRecallsFeature.FeatureId;

        private readonly DesignFlawRecallsFeature _feature;
        private readonly FlawDecisionEngine _engine;
        private readonly FlawAiEngine _ai;

        public FlawDecisionDispatcher(DesignFlawRecallsFeature feature, FlawResponseApplier applier, FlawChannels channels)
        {
            _feature = feature;
            _engine = new FlawDecisionEngine(feature.Store, applier.Engine, channels.Book, this);
            _ai = new FlawAiEngine(applier.Engine, this);
        }

        /// <summary>The pure engine.</summary>
        internal FlawDecisionEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>The pure engine of the AI companies' answers.</summary>
        internal FlawAiEngine Ai
        {
            get { return _ai; }
        }

        // ---- the entries ----

        /// <summary>
        /// A flaw became known and waits for the answer of its owner: the player gets the window (or a place in the queue). The body is guarded: an exception is
        /// counted against the feature and does not count as a failure of the record.
        /// </summary>
        internal FlawDispatchResult Dispatch(FlawRecord record)
        {
            return ModHooks.Guard<FlawDispatchResult>(FeatureId, "decision/dispatch", () => _engine.Dispatch(record), FlawDispatchResult.Failed);
        }

        /// <summary>
        /// A flaw that waits for a decision has a new owner (a takeover of its CPU, FR-19 item 3): the old owner's place in the queue is given up and the new owner is asked,
        /// the player by the window <c>dfr.decide.public*</c> (or a place in the queue), an AI company by its policy. The body is guarded.
        /// </summary>
        internal FlawDispatchResult Reoffer(FlawRecord record)
        {
            return ModHooks.Guard<FlawDispatchResult>(FeatureId, "decision/reoffer", () => _engine.Reoffer(record), FlawDispatchResult.Failed);
        }

        /// <summary><c>EventBus.OnChoiceResolved</c>: an answer was given (only the events of this pack matter).</summary>
        internal void OnChoiceResolved(EventInstance instance, string choiceId, bool auto)
        {
            if (instance == null || instance.Data == null) return;

            EventInstanceDto data = instance.Data;
            ModHooks.Guard(FeatureId, "decision/resolved", () => _engine.OnChoiceResolved(data.eventId, data.args, choiceId, auto));
        }

        /// <summary>Opens the next window of the queue, if none is open (after a load, after a record was closed).</summary>
        internal void Pump()
        {
            ModHooks.Guard(FeatureId, "decision/pump", _engine.Pump);
        }

        /// <summary>The record is over without an answer: it leaves the queue and the next one may open.</summary>
        internal void Forget(FlawRecord record)
        {
            ModHooks.Guard(FeatureId, "decision/forget", () =>
            {
                _engine.Forget(record);
                _engine.Pump();
            });
        }

        /// <summary>The monthly pass: a window that was never answered gets the default choice so that it does not block the queue.</summary>
        internal void Sweep()
        {
            ModHooks.Guard(FeatureId, "decision/sweep", () => _engine.Sweep());
        }

        // ---- IFlawDecisionHost ----

        public int Today
        {
            get { return FlawDays.FromDate(ModTicks.Today); }
        }

        public FlawBalance Balance
        {
            get { return _feature.Balance; }
        }

        public string[] BuildArgs(FlawRecord record, FlawQuote quote)
        {
            return FlawDecisionPlanner.BuildArgs(record, quote, FlawText.CpuName(record.BaseCpuId), FlawText.TypeName(Balance, record.TypeId),
                FlawText.SeverityName(record.Severity), FlawFormat.Instance);
        }

        public FlawFireOutcome Fire(string eventId, string companyKey, string[] args)
        {
            EventFireResult result = EventBus.Fire(eventId, new EventFireContext { CompanyKey = companyKey, Args = args, IgnoreConditions = true });
            switch (result)
            {
                case EventFireResult.Fired: return FlawFireOutcome.Fired;
                case EventFireResult.Scheduled: return FlawFireOutcome.Scheduled;
                case EventFireResult.Skipped: return FlawFireOutcome.Skipped;
                case EventFireResult.UnknownEvent: return FlawFireOutcome.UnknownEvent;
                default: return FlawFireOutcome.Disabled;
            }
        }

        public void SendAutoMail(FlawRecord record, FlawResponse response, string eventId)
        {
            FlawEffectNumbers numbers = FlawEffectModel.Resolve(response, record.Id, record.Quote, FlawFormat.Instance);
            string cost = FlawFormat.Instance.Money(numbers.Value < 0f ? -numbers.Value : 0f);

            ModMail.Send(new ModMailMessage
            {
                FeatureId = FeatureId,
                Tag = "dfr:auto:" + record.Id,
                TitleKey = "dfr.mail.auto.title",
                BodyKey = "dfr.mail.auto.body",
                Args = new[] { FlawText.CpuName(record.BaseCpuId), FlawText.ChoiceName(response), cost },
                Priority = MailPriority.Normal,
            });

            _feature.News.OnPlayerAutoChoice(record, response);
        }

        /// <summary>The flaw of an AI company: its policy answers at once and the answer is applied; the player hears about a public incident.</summary>
        public bool DispatchAi(FlawRecord record)
        {
            return _ai.Decide(record);
        }

        // ---- IFlawAiHost ----

        public IModRng Rng
        {
            get { return ModRandom.Stream(FeatureId + "/ai"); }
        }

        public bool DuringFastForward
        {
            get { return ModTicks.IsFastForwarding; }
        }

        /// <summary>
        /// The money of the owner plus the debt the game lets it have: <c>MoneyAmount - ModCredit.ThresholdOf(owner)</c> (the threshold is negative). The threshold comes from
        /// <c>ModCredit</c> only, never from <c>CentralBank.DebtLimit</c>. NaN when the owner is not in the world.
        /// </summary>
        public float CashHeadroom(FlawRecord record)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            if (owner == null) return float.NaN;

            return AiFlawPolicy.CashHeadroom(owner.MoneyAmount, ModCredit.ThresholdOf(owner));
        }

        /// <summary>The bias of <c>rival-personalities</c> for the owner, if the feature has registered one and is on; <c>null</c> for no bias. Never throws.</summary>
        public float[] ResponseBias(FlawRecord record, bool isPublic)
        {
            GameCompany owner = ModCompanies.Resolve(record.CompanyKey);
            return owner == null ? null : FlawRegistry.Shared.GetResponseBiasMultipliers(owner, isPublic);
        }

        public void PublishNews(FlawRecord record, FlawResponse response, FlawNewsPlan plan)
        {
            _feature.News.OnAiPublic(record, response, plan);
        }

        public void Log(string line)
        {
            ModLog.Info(FeatureId, line);
        }

        public void Warn(string line)
        {
            ModLog.Warn(FeatureId, line);
        }

        public void Error(string line, Exception exception)
        {
            ModLog.Error(FeatureId, line, exception);
        }
    }
}
