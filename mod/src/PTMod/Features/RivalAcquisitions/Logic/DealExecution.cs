using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>The steps of a purchase of a whole company, numbered as in design.md ("AcquisitionExecutor", steps 1-14).</summary>
    public enum DealStep
    {
        None = 0,

        /// <summary>1. The target is still the one of the request; nothing is changed before this passes.</summary>
        Recheck = 1,

        /// <summary>3. The technologies of the gap are unlocked for the player (no pop-ups), the partial progress is raised.</summary>
        Technologies = 3,

        /// <summary>4. The process-node experience of the player is raised to the target's.</summary>
        NodeExperience = 4,

        /// <summary>5. The CPU families leave the lists of the AI (before the target goes bankrupt); the CPUs to retire are retired.</summary>
        DetachCpus = 5,

        /// <summary>6. The lines leave the target and go to the player's factory, or are sold.</summary>
        Lines = 6,

        /// <summary>7. The supply contracts, the makers of the clients' CPUs, the foundry-services flag, the target's client contracts.</summary>
        Contracts = 7,

        /// <summary>8. The CPU families join the player, the base first; <c>CpuTransferred</c> for each.</summary>
        AttachCpus = 8,

        /// <summary>9. The state of related features passes to the player, before the target leaves (FR-16; <see cref="RelatedStateTransfer"/>).</summary>
        RelatedFeatures = 9,

        /// <summary>10. The record of the deal goes into the books: from now on the target counts as absorbed.</summary>
        Record = 10,

        /// <summary>11. The target leaves the game: balance 0, <c>TriggerBankruptcy</c>, the bankruptcy event of the game.</summary>
        TakeOut = 11,

        /// <summary>12. The money: the price, then the cash or the debt of the target. Last of all the changes.</summary>
        Payment = 12,

        /// <summary>13. The books, the reputation, <c>DealCompleted</c>, the mail.</summary>
        Finish = 13,
    }

    /// <summary>
    /// What the steps of a deal report besides throwing: notes for the record and the log, and steps that did not do all they should (the deal
    /// is then "partial"). A step that throws is marked failed by <see cref="DealExecution"/>; a step that finds a problem without throwing
    /// (fewer lines than planned, a CPU that is gone) calls <see cref="Fail"/>.
    /// </summary>
    public sealed class DealLog
    {
        private readonly List<string> _notes = new List<string>();
        private readonly List<DealStep> _failed = new List<DealStep>();

        /// <summary>The step that runs now; notes are labelled with it.</summary>
        public DealStep Current { get; set; }

        public IReadOnlyList<string> Notes
        {
            get { return _notes; }
        }

        public IReadOnlyList<DealStep> FailedSteps
        {
            get { return _failed; }
        }

        /// <summary>Some step did not do all it should.</summary>
        public bool Partial
        {
            get { return _failed.Count > 0; }
        }

        /// <summary>The assets moved without a problem: none of the steps 3 to 8 failed. Only then is the price charged (design.md, "Error Handling").</summary>
        public bool AssetsComplete
        {
            get
            {
                for (int i = 0; i < _failed.Count; i++)
                {
                    if (_failed[i] >= DealStep.Technologies && _failed[i] <= DealStep.AttachCpus) return false;
                }
                return true;
            }
        }

        /// <summary>A note of the current step (English, for the log and the record).</summary>
        public void Note(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notes.Add(Label(Current) + text);
        }

        /// <summary>The current step did not do all it should: the deal becomes partial, <paramref name="text"/> says what.</summary>
        public void Fail(string text)
        {
            MarkFailed(Current);
            Note(text);
        }

        public void MarkFailed(DealStep step)
        {
            if (!_failed.Contains(step)) _failed.Add(step);
        }

        public bool HasFailed(DealStep step)
        {
            return _failed.Contains(step);
        }

        /// <summary>All notes in one line, for <c>DealRecord.notes</c>.</summary>
        public string Joined()
        {
            return string.Join("; ", _notes.ToArray());
        }

        private static string Label(DealStep step)
        {
            return step == DealStep.None ? "" : "step " + ((int)step).ToString(CultureInfo.InvariantCulture) + ": ";
        }
    }

    /// <summary>
    /// A purchase of a whole company the player confirmed: the quote and the plan built from fresh snapshots right before the execution, and what
    /// the window showed (so a deal whose terms changed since is not carried out).
    /// </summary>
    public sealed class DealRequest
    {
        public DealType Type;
        public DealQuote Quote;
        public AcquisitionPlan Plan;
        public CompanySnapshot Target;
        public PlayerSnapshot Player;

        /// <summary>The <c>UniqueID</c> the player chose (-1: not checked).</summary>
        public int ExpectedUniqueId = -1;

        /// <summary>The <c>SaveID</c> the player chose (-1: not checked).</summary>
        public int ExpectedSaveId = -1;

        /// <summary>The status the window showed; null: not checked.</summary>
        public CompanyStatus? ExpectedStatus;

        /// <summary>The price the player agreed to; a higher price now cancels the deal. 0 or less: not checked.</summary>
        public double ShownPrice;

        /// <summary>The game date of the deal.</summary>
        public DateTime Today;
    }

    /// <summary>What came of a request: carried out (with its record) or cancelled before any change (with the reason).</summary>
    public sealed class DealOutcome
    {
        /// <summary>The deal went ahead (it may still be partial: see <see cref="DealRecord.partial"/>).</summary>
        public bool Executed;

        /// <summary>A key of <see cref="AcqKeys"/> when the deal was cancelled; null when it went ahead.</summary>
        public string CancelReasonKey;

        /// <summary>The argument of the reason ({0}); null when it has none.</summary>
        public string CancelReasonArg;

        /// <summary>The record kept in the books; null when cancelled.</summary>
        public DealRecord Record;

        /// <summary>The price and the cash of the target moved (false when the payment was skipped or failed).</summary>
        public bool Paid;

        public DealLog Log = new DealLog();
    }

    /// <summary>
    /// The game side of a deal: what <see cref="DealExecution"/> asks the game to do, step by step. The game implementation is
    /// <c>AcquisitionExecutor</c>; the tests use a fake. A member may throw: the pipeline catches it through its guard and marks the step failed.
    /// Every member that changes the game gets the plan built from fresh snapshots (contract ids are those of that snapshot).
    /// </summary>
    public interface IDealWorld
    {
        /// <summary>Step 1 on the live objects: null when the target is still the one of the request, otherwise a key of <see cref="AcqKeys"/>.</summary>
        string Recheck(DealRequest request);

        /// <summary>Step 2: the deal starts (the feature's own hooks ignore the target from now on).</summary>
        void Begin(DealRequest request);

        void UnlockTechnologies(AcquisitionPlan plan, DealLog log);
        void RaiseNodeExperience(AcquisitionPlan plan, DealLog log);
        void DetachCpus(AcquisitionPlan plan, DealLog log);
        void MoveLines(AcquisitionPlan plan, DealLog log);
        void MoveContracts(AcquisitionPlan plan, DealLog log);
        void AttachCpus(AcquisitionPlan plan, DealLog log);
        void TransferRelatedState(DealRequest request, DealLog log);

        /// <summary>Step 11: balance 0, <c>AICompany.TriggerBankruptcy</c>, the game's <c>OnCompanyWentBankrupt</c>. Never for the assets of a bankrupt company.</summary>
        void TakeOutTarget(DealLog log);

        /// <summary>Capital out of the player's account (<c>CapitalLedger.CapitalOut</c>).</summary>
        void PayOut(double amount, string op, string details);

        /// <summary>Capital into the player's account (<c>CapitalLedger.CapitalIn</c>).</summary>
        void PayIn(double amount, string op, string details);

        void AddReputation(int delta, string reasonKey);
        void RaiseStakeChanged(int uniqueId, double share);
        void RaiseDealCompleted(DealRecord record);
        void SendDealMail(DealRecord record);

        /// <summary>The deal is over (or stopped): always called after <see cref="Begin"/>.</summary>
        void End();
    }

    /// <summary>
    /// <c>DealExecution</c> (FR-3…FR-5, FR-7…FR-10, NFR-2; design.md "AcquisitionExecutor" and D8): the order of the steps of a purchase of a
    /// whole company, independent of the game. "Plan, then execution; the payment last".
    /// <list type="number">
    /// <item>Every check comes before the first change: the request itself (<see cref="Validate"/>) and the live objects (<see cref="IDealWorld.Recheck"/>).
    /// A failed check cancels the deal and nothing is changed.</item>
    /// <item>The steps 3 to 9 run one by one, each under the guard: an exception marks the step failed (the deal is partial) and the next step
    /// still runs.</item>
    /// <item>The record goes into the books BEFORE the target leaves the game, so <c>IsAbsorbedByPlayer</c> is already true inside the game's
    /// bankruptcy event that the takeover itself raises.</item>
    /// <item>The payment is the last change: the price, then the cash of the target (positive: in; a debt: out). When one of the steps 3 to 8
    /// failed, nothing is charged and nothing is taken over (the record says so).</item>
    /// <item>Then the books (the auction closed, the stake and the rescue order of the target ended, the cooldown of a hostile takeover), the
    /// reputation, <c>DealCompleted</c> and the mail.</item>
    /// </list>
    /// The target object of the game is never renamed or replaced: the pipeline only asks for the steps above.
    /// </summary>
    public static class DealExecution
    {
        /// <summary>The operation codes of the payment (the log of <c>CapitalLedger</c>).</summary>
        public const string OpPrice = "acq.price";
        public const string OpCash = "acq.cash";
        public const string OpDebt = "acq.debt";

        /// <summary>
        /// Carries out <paramref name="request"/> on <paramref name="world"/>, keeping the books in <paramref name="state"/>. <paramref name="guard"/>
        /// runs one step: <c>(site, body) =&gt; true</c> when the body ran to the end, false when it threw (the game passes <c>ModHooks.Guard</c>).
        /// Never throws for a failing step; a failure of the pipeline itself is the caller's to catch.
        /// </summary>
        public static DealOutcome Run(IDealWorld world, DealRequest request, AcquisitionsState state, Func<string, Action, bool> guard)
        {
            var outcome = new DealOutcome();
            if (world == null || request == null || state == null || guard == null) return Cancel(outcome, AcqKeys.ReasonUnsupported, null);

            string arg;
            string reason = Validate(request, out arg);
            if (reason != null) return Cancel(outcome, reason, arg);

            DealLog log = outcome.Log;
            log.Current = DealStep.Recheck;
            string live = null;
            if (!guard(SiteOf(DealStep.Recheck), () => live = world.Recheck(request))) return Cancel(outcome, AcqKeys.ReasonUnsupported, null);
            if (live != null) return Cancel(outcome, live, null);

            if (!guard("Begin", () => world.Begin(request))) return Cancel(outcome, AcqKeys.ReasonUnsupported, null);

            try
            {
                AcquisitionPlan plan = request.Plan;
                Step(log, guard, DealStep.Technologies, () => world.UnlockTechnologies(plan, log));
                Step(log, guard, DealStep.NodeExperience, () => world.RaiseNodeExperience(plan, log));
                Step(log, guard, DealStep.DetachCpus, () => world.DetachCpus(plan, log));
                Step(log, guard, DealStep.Lines, () => world.MoveLines(plan, log));
                Step(log, guard, DealStep.Contracts, () => world.MoveContracts(plan, log));
                Step(log, guard, DealStep.AttachCpus, () => world.AttachCpus(plan, log));
                Step(log, guard, DealStep.RelatedFeatures, () => world.TransferRelatedState(request, log));

                // 10: the record first, so the target counts as absorbed inside the bankruptcy event of step 11.
                log.Current = DealStep.Record;
                DealRecord record = Draft(request);
                state.AddDeal(record);
                outcome.Record = record;

                if (TakesTargetOut(request.Type)) Step(log, guard, DealStep.TakeOut, () => world.TakeOutTarget(log));

                outcome.Paid = Pay(world, request, record, log, guard);
                Finish(world, request, state, record, log, guard);
                outcome.Executed = true;
                return outcome;
            }
            finally
            {
                guard("End", world.End);
            }
        }

        /// <summary>
        /// The checks of the request that need no game: the kind of deal, the target the player chose and its status, the quote and the plan, the
        /// price the player agreed to, and the invariant <c>NetCash &lt; 0</c> of a purchase that takes the target's cash over. Null when it can go
        /// ahead; otherwise the key of the reason (<paramref name="arg"/> is its argument).
        /// </summary>
        public static string Validate(DealRequest r, out string arg)
        {
            arg = null;
            if (r == null || r.Target == null || r.Player == null || r.Plan == null) return AcqKeys.ReasonUnsupported;
            if (!IsPurchase(r.Type)) return AcqKeys.PlanNotPurchase;
            if (r.Plan.Type != r.Type || r.Quote.Type != r.Type) return AcqKeys.ReasonUnsupported;

            if ((r.ExpectedUniqueId >= 0 && r.Target.UniqueId != r.ExpectedUniqueId) || (r.ExpectedSaveId >= 0 && r.Target.SaveId != r.ExpectedSaveId))
                return AcqKeys.ReasonTargetChanged;
            if (r.ExpectedStatus.HasValue && r.Target.Status != r.ExpectedStatus.Value) return AcqKeys.ReasonStatusChanged;

            if (!r.Quote.Available)
            {
                arg = r.Quote.ReasonArg;
                return r.Quote.ReasonKey ?? AcqKeys.ReasonUnsupported;
            }
            if (!r.Plan.CanExecute)
            {
                arg = r.Plan.BlockingArgs.Count > 0 ? r.Plan.BlockingArgs[0] : null;
                return r.Plan.BlockingErrors[0];
            }
            if (r.ShownPrice > 0.0 && r.Quote.Price > r.ShownPrice + 0.5)
            {
                arg = r.Quote.Price.ToString("F0", CultureInfo.InvariantCulture);
                return AcqKeys.ReasonPriceChanged;
            }
            // The invariant of design.md: a purchase that takes the cash over never brings money. DealRules guarantees it; checked again here.
            if (TakesTargetOut(r.Type) && !(r.Quote.NetCash < 0.0)) return AcqKeys.ReasonUnsupported;
            return null;
        }

        /// <summary>
        /// The record of the deal as it goes into the books (without an id; <c>AcquisitionsState.AddDeal</c> gives it one): what the plan moves.
        /// Moved lines are listed on the wafer size the player gets them on, sold lines on their own.
        /// </summary>
        public static DealRecord Draft(DealRequest r)
        {
            AcquisitionPlan plan = r.Plan;
            var record = new DealRecord
            {
                type = r.Type.ToString(),
                targetUniqueId = r.Target.UniqueId,
                targetSaveId = r.Target.SaveId,
                targetName = r.Target.Name ?? "",
                date = r.Today.ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture),
                price = r.Quote.Price,
                assumedCash = r.Quote.AssumedCash,
                share = 0.0,
                keptLines = plan.KeepLines,
                techCount = plan.TechUnlockIds.Count,
                cpuSaveIds = plan.TransferredCpuIds(),
                retiredCpuSaveIds = plan.TransferredRetiredCpuIds(),
                providerContracts = plan.KeepLines ? plan.RepointProviderContractIds.Count : 0,
                clientContracts = plan.RepointClientContractIds.Count,
            };

            if (plan.KeepLines)
            {
                for (int i = 0; i < plan.LineMoves.Count; i++) AddLines(record.lines, plan.LineMoves[i].ToWaferId, plan.LineMoves[i].Lines);
            }
            else
            {
                for (int i = 0; i < plan.LineSales.Count; i++) AddLines(record.lines, plan.LineSales[i].WaferId, plan.LineSales[i].Lines);
            }
            return record;
        }

        /// <summary>The reason key of the reputation change of a deal (<c>acq.reputation.*</c>).</summary>
        public static string ReputationKey(DealType type)
        {
            switch (type)
            {
                case DealType.Hostile: return AcqKeys.ReputationHostile;
                case DealType.Distressed: return AcqKeys.ReputationDistressed;
                case DealType.BankruptAssets: return AcqKeys.ReputationBankrupt;
                default: return AcqKeys.ReputationRescue;
            }
        }

        /// <summary>The purchases of a whole company (the only requests this pipeline carries out).</summary>
        public static bool IsPurchase(DealType type)
        {
            return type == DealType.BankruptAssets || type == DealType.Distressed || type == DealType.Hostile;
        }

        /// <summary>The deal takes the target out of the game (and takes its cash over): in default and hostile. The assets of a bankrupt are bought from a company that is out already.</summary>
        public static bool TakesTargetOut(DealType type)
        {
            return type == DealType.Distressed || type == DealType.Hostile;
        }

        /// <summary>The guard site of a step: <c>AcquisitionExecutor.step&lt;N&gt;</c>.</summary>
        public static string SiteOf(DealStep step)
        {
            return "AcquisitionExecutor.step" + ((int)step).ToString(CultureInfo.InvariantCulture);
        }

        // ---- the steps ----

        private static void Step(DealLog log, Func<string, Action, bool> guard, DealStep step, Action body)
        {
            log.Current = step;
            if (!guard(SiteOf(step), body))
            {
                log.MarkFailed(step);
                log.Note("failed, see the log");
            }
        }

        /// <summary>Step 12. True when the money moved.</summary>
        private static bool Pay(IDealWorld world, DealRequest r, DealRecord record, DealLog log, Func<string, Action, bool> guard)
        {
            log.Current = DealStep.Payment;
            if (!log.AssetsComplete)
            {
                // design.md, "Error Handling": the price is charged only when the assets moved; the cash of the target is not taken over either,
                // so a failed step never brings the player money.
                record.price = 0.0;
                record.assumedCash = 0.0;
                log.MarkFailed(DealStep.Payment);
                log.Note("nothing was charged: an asset step failed");
                return false;
            }

            double price = r.Quote.Price;
            double cash = r.Quote.AssumedCash;
            string details = (r.Target.Name ?? "") + " (" + r.Type + ")";
            bool ok = guard(SiteOf(DealStep.Payment), () =>
            {
                if (price > 0.0) world.PayOut(price, OpPrice, details);
                if (cash > 0.0) world.PayIn(cash, OpCash, details);
                else if (cash < 0.0) world.PayOut(-cash, OpDebt, details);
            });
            if (!ok)
            {
                log.MarkFailed(DealStep.Payment);
                log.Note("the payment failed, see the log");
            }
            return ok;
        }

        /// <summary>Step 13: the books, then the reputation, the event and the mail.</summary>
        private static void Finish(IDealWorld world, DealRequest r, AcquisitionsState state, DealRecord record, DealLog log, Func<string, Action, bool> guard)
        {
            log.Current = DealStep.Finish;
            int uid = r.Target.UniqueId;
            int saveId = r.Target.SaveId;

            DistressRules.CloseAuction(state, uid, saveId);
            double hadShare = StakeBook.RemoveStake(state, uid);
            StakeBook.RemoveOrder(state, uid);
            if (r.Type == DealType.Hostile) state.lastHostileOn = record.date;

            int delta = r.Quote.ReputationDelta;
            if (delta != 0 && !guard(SiteOf(DealStep.Finish) + ".reputation", () => world.AddReputation(delta, ReputationKey(r.Type))))
            {
                log.Fail("the reputation was not changed");
            }

            record.partial = log.Partial;
            record.notes = log.Joined();

            if (hadShare > 0.0) guard(SiteOf(DealStep.Finish) + ".stake", () => world.RaiseStakeChanged(uid, 0.0));
            guard(SiteOf(DealStep.Finish) + ".event", () => world.RaiseDealCompleted(record));
            guard(SiteOf(DealStep.Finish) + ".mail", () => world.SendDealMail(record));
        }

        // ---- helpers ----

        private static DealOutcome Cancel(DealOutcome outcome, string reason, string arg)
        {
            outcome.Executed = false;
            outcome.CancelReasonKey = reason ?? AcqKeys.ReasonUnsupported;
            outcome.CancelReasonArg = arg;
            return outcome;
        }

        private static void AddLines(List<WaferLines> lines, int waferId, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].waferId == waferId)
                {
                    lines[i].lines += count;
                    return;
                }
            }
            lines.Add(new WaferLines { waferId = waferId, lines = count });
        }
    }
}
