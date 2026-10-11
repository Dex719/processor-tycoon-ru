using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The kind of an expense of the feature (the game's <c>ExpenseOrigin</c>: the report of the company shows the lines by it).</summary>
    public enum FlawSpendKind
    {
        /// <summary><c>ExpenseOrigin.Fines</c>: a recall, claims after a revision, a lawsuit.</summary>
        Fines,

        /// <summary><c>ExpenseOrigin.Development</c>: a patch, the days of a revision.</summary>
        Development,
    }

    /// <summary>
    /// What the response engine needs from the game (design.md Component 10). <c>FlawResponseApplier</c> is the game's implementation (money, reputation,
    /// popularity, contracts); a test brings a fake. Every method acts on the owner of <paramref name="record"/>.
    /// </summary>
    public interface IFlawResponseHost
    {
        /// <summary>Today as a day number (days from 1971-01-01).</summary>
        int Today { get; }

        /// <summary>The numbers of the feature.</summary>
        FlawBalance Balance { get; }

        /// <summary>The random stream of the reactions (the cancellation of the contracts), <c>ModRandom.Stream("design-flaw-recalls/response")</c>.</summary>
        IModRng Rng { get; }

        /// <summary><c>ICompany.SpendMoney(amount, origin)</c> of the owner.</summary>
        void Spend(FlawRecord record, float amount, FlawSpendKind kind);

        /// <summary><c>ModReputation.Add(owner, delta, reasonKey)</c>; the id of the record goes to the log.</summary>
        void AddReputation(FlawRecord record, int delta, string reasonKey);

        /// <summary>Multiplies the popularity of every CPU of the record by <c>(1 - hit)</c>.</summary>
        void HitPopularity(FlawRecord record, float hit);

        /// <summary>
        /// Ends each active OEM contract on the CPUs of the record with the probability <paramref name="chance"/> (<c>ContractManager.RemoveContract</c>, no fine
        /// for the supplier). Returns how many were ended.
        /// </summary>
        int CancelContracts(FlawRecord record, float chance);

        /// <summary>The record moved to another state (<paramref name="previous"/> is the old one) or answered: the public API tells its subscribers.</summary>
        void StateChanged(FlawRecord record, FlawState previous);

        void Log(string line);

        void Warn(string line);

        void Error(string line, Exception exception);
    }

    /// <summary>
    /// The reactions to a flaw (FR-6..FR-10, design.md Component 10): what a recall, a revision, a patch and an ignore do to the money, the reputation, the
    /// popularity, the contracts and the channels, and what the end of a revision, the end of a patch, a leak, the monthly drain and the lawsuit do. Pure:
    /// the game is behind <see cref="IFlawResponseHost"/> and the core's channels behind the <see cref="FlawChannelBook"/>. The amounts are the saved quote
    /// of the record (the numbers the player saw), never recomputed here (D9).
    /// <para>
    /// A response is applied once: it needs a record that waits for the decision (known, and nothing chosen yet), otherwise it only warns and returns
    /// <c>false</c> (idempotent, FR-5 / design.md Component 9). The money is taken before the record moves, so a failure of the payment leaves the record
    /// waiting; a failure of the later steps (reputation, popularity, channels, contracts) is logged and does not undo the response.
    /// </para>
    /// </summary>
    public sealed class FlawResponseEngine
    {
        private readonly FlawChannelBook _channels;
        private readonly IFlawResponseHost _host;

        public FlawResponseEngine(FlawChannelBook channels, IFlawResponseHost host)
        {
            if (channels == null) throw new ArgumentNullException("channels");
            if (host == null) throw new ArgumentNullException("host");
            _channels = channels;
            _host = host;
        }

        /// <summary>The record is known (internally or publicly) and its owner has not answered: a response may be applied.</summary>
        public static bool AwaitsDecision(FlawRecord record)
        {
            if (record == null) return false;
            return (record.State == FlawState.InternalKnown || record.State == FlawState.Public) && record.Response == FlawResponse.None;
        }

        // ---- the answer ----

        /// <summary>
        /// Applies the owner's answer to a flaw that waits for it. <paramref name="automatic"/> - the choice was made by the timer or by an AI company, not by a
        /// click (it is kept in the record). Returns <c>true</c> when the answer was applied; <c>false</c> (with a warning) when the record does not wait for
        /// a decision, has no quote, or the answer is not possible (no patch for the type in this year).
        /// </summary>
        public bool Apply(FlawRecord record, FlawResponse response, bool automatic)
        {
            if (record == null) return false;
            if (!AwaitsDecision(record))
            {
                _host.Warn("response " + response + " for " + record + " ignored: the record does not wait for a decision (state " + record.State
                           + ", response " + record.Response + ")");
                return false;
            }

            FlawQuote quote = record.Quote;
            if (quote == null)
            {
                _host.Warn("response " + response + " for " + record + " ignored: the record has no quote");
                return false;
            }

            switch (response)
            {
                case FlawResponse.Recall: return Recall(record, quote, automatic);
                case FlawResponse.Stepping: return Stepping(record, quote, automatic);
                case FlawResponse.Patch: return Patch(record, quote, automatic);
                case FlawResponse.Ignore: return Ignore(record, quote, automatic);
                default:
                    _host.Warn("response " + response + " for " + record + " ignored: not a response");
                    return false;
            }
        }

        private bool Recall(FlawRecord record, FlawQuote quote, bool automatic)
        {
            FlawBalance b = _host.Balance;
            FlawSeverity sev = record.Severity;
            FlawState previous = record.State;

            Pay(record, quote.RecallCost, FlawSpendKind.Fines);
            if (!FlawStateMachine.Recall(record, _host.Today)) return false;
            record.Data.automaticChoice = automatic;

            Reputation(record, FlawReputationTable.Recall(b, sev), FlawReputationKeys.Recall);
            Popularity(record, FlawPopularity.HitOf(b, sev, FlawPopularityHit.Recall));
            Guarded(record, "pending entry", () => _channels.RemovePending(record));
            Announce(record, previous);
            _host.Log("recall " + record + " cost=" + quote.RecallCost + (automatic ? " (automatic)" : string.Empty));
            return true;
        }

        private bool Stepping(FlawRecord record, FlawQuote quote, bool automatic)
        {
            return BeginStepping(record, quote, automatic, "stepping");
        }

        /// <summary>The start of a revision: from a known flaw, or from a patch that is paid off by a revision (<see cref="FundSteppingForMitigated"/>).</summary>
        private bool BeginStepping(FlawRecord record, FlawQuote quote, bool automatic, string what)
        {
            FlawBalance b = _host.Balance;
            FlawSeverity sev = record.Severity;
            FlawState previous = record.State;

            if (!FlawStateMachine.BeginStepping(record, _host.Today, quote.SteppingDays, quote.SteppingDailyCost)) return false;
            record.Data.automaticChoice = automatic;

            bool quiet = record.Data.quietStepping;
            Reputation(record, FlawReputationTable.Stepping(b, sev, quiet), FlawReputationKeys.Stepping);
            if (!quiet) Popularity(record, FlawPopularity.HitOf(b, sev, FlawPopularityHit.Stepping));
            Guarded(record, "pending entry", () => _channels.RemovePending(record));
            Announce(record, previous);
            _host.Log(what + " " + record + " cost=" + quote.SteppingCost + " days=" + quote.SteppingDays + " until day " + record.Data.steppingEndDay
                      + (quiet ? " (quiet)" : string.Empty) + (automatic ? " (automatic)" : string.Empty));
            return true;
        }

        private bool Patch(FlawRecord record, FlawQuote quote, bool automatic)
        {
            if (!quote.PatchAvailable)
            {
                _host.Warn("patch for " + record + " ignored: there is no patch for this type of flaw in this year");
                return false;
            }

            FlawBalance b = _host.Balance;
            FlawSeverity sev = record.Severity;
            FlawState previous = record.State;
            int today = _host.Today;

            Pay(record, quote.PatchCost, FlawSpendKind.Development);
            if (!FlawStateMachine.BeginMitigation(record, today, quote.PatchMonths, quote.PatchIpsLoss)) return false;
            record.Data.automaticChoice = automatic;

            Guarded(record, "patch entries", () => AddPatchEntries(record, today));
            Reputation(record, FlawReputationTable.Patch(b, sev), FlawReputationKeys.Patch);
            Popularity(record, FlawPopularity.HitOf(b, sev, FlawPopularityHit.Patch));
            Guarded(record, "pending entry", () => _channels.RemovePending(record));
            Announce(record, previous);
            _host.Log("patch " + record + " cost=" + quote.PatchCost + " ipsLoss=" + record.Data.ipsLoss + " until day " + record.Data.mitigationEndDay
                      + (automatic ? " (automatic)" : string.Empty));
            return true;
        }

        /// <summary>One <c>market.rating.cpu</c> entry per CPU of the record, for the term of the patch: the rating is multiplied by <c>1 - ipsLoss</c>.</summary>
        private void AddPatchEntries(FlawRecord record, int today)
        {
            int days = record.Data.mitigationEndDay - today;
            float multiplier = 1f - record.Data.ipsLoss;
            if (record.Data.cpuIds == null) return;

            foreach (int cpuId in new List<int>(record.Data.cpuIds))
            {
                _channels.AddCpuRating(cpuId, multiplier, days, record.Id);
            }
        }

        private bool Ignore(FlawRecord record, FlawQuote quote, bool automatic)
        {
            FlawState previous = record.State;
            int today = _host.Today;

            if (!FlawStateMachine.Ignore(record, today, _host.Balance)) return false;
            record.Data.automaticChoice = automatic;

            bool isPublic = record.State == FlawState.Public;
            if (isPublic)
            {
                FlawBalance b = _host.Balance;
                Reputation(record, FlawReputationTable.IgnorePublic(b, record.Severity), FlawReputationKeys.Ignore);
                IgnoreConsequences(record, quote, today);
            }

            Guarded(record, "pending entry", () => _channels.RemovePending(record));
            Announce(record, previous);
            _host.Log("ignore " + record + (isPublic ? " (public: drain until day " + record.Data.ignoreDrainUntilDay + ")" : " (internal: it may leak)")
                      + (automatic ? " (automatic)" : string.Empty));
            return true;
        }

        /// <summary>
        /// What ignoring a public flaw does besides the reputation (FR-9 item 1): the popularity of the CPUs, the clients' chance of signing for the next
        /// <c>signChanceMonths</c> months, and the OEM contracts on the CPUs, each ended with the chance <c>oemCancel</c>.
        /// </summary>
        private void IgnoreConsequences(FlawRecord record, FlawQuote quote, int today)
        {
            FlawBalance b = _host.Balance;
            Popularity(record, FlawPopularity.HitOf(b, record.Severity, FlawPopularityHit.Ignore));

            int months = b.Responses.Ignore.SignChanceMonths < 1 ? 1 : b.Responses.Ignore.SignChanceMonths;
            int days = FlawDays.AddMonths(today, months) - today;
            Guarded(record, "sign-chance entry", () =>
                _channels.AddSignChance(record.CompanyKey, quote.IgnoreSignChanceMultiplier, days, record.Id, FlawChannelNames.ReasonIgnore));

            Guarded(record, "contract cancellation", () =>
            {
                int ended = _host.CancelContracts(record, quote.OemCancelChance);
                if (ended > 0) _host.Log("ignore " + record + ": " + ended + " OEM contract(s) ended");
            });
        }

        // ---- the life after the answer ----

        /// <summary>
        /// A hidden flaw was found (<see cref="FlawCycle.Reveal"/>, the record is already moved): when it became public the brand pays at once
        /// (<c>publicBase</c>, <c>dfr.rep.public</c>) and the CPUs lose popularity (<c>popHit.public</c>). A flaw only the company knows about costs nothing yet.
        /// </summary>
        public void OnDiscovered(FlawRecord record)
        {
            if (record == null || record.State != FlawState.Public) return;

            FlawBalance b = _host.Balance;
            Reputation(record, FlawReputationTable.PublicBase(b, record.Severity), FlawReputationKeys.Public);
            Popularity(record, FlawPopularity.HitOf(b, record.Severity, FlawPopularityHit.Public));
        }

        /// <summary>
        /// A cover-up leaked (FR-7 item 5, FR-9 item 5): the record is public with <c>coverUp</c> and has no answer. The brand pays
        /// <c>(publicBase + reaction) * coverUpMultiplier</c> (<c>dfr.rep.leak</c>). A quiet revision that leaked costs the full recall (the claims for the units
        /// sold before the fix, <c>Fines</c>), its reaction is the quiet revision (0); an ignored flaw that leaked brings the consequences of the ignore
        /// (the popularity, the clients, the OEM contracts), its reaction is the ignore. In both cases the CPUs lose the popularity of a public flaw.
        /// </summary>
        public void OnLeak(FlawRecord record)
        {
            if (record == null || record.State != FlawState.Public) return;

            FlawBalance b = _host.Balance;
            FlawSeverity sev = record.Severity;
            FlawQuote quote = record.Quote;
            bool quiet = record.Data.quietStepping;

            int reaction;
            if (quiet)
            {
                reaction = FlawReputationTable.Stepping(b, sev, true);
                if (quote != null && quote.RecallCost > 0f) Guarded(record, "claims", () => Pay(record, quote.RecallCost, FlawSpendKind.Fines));
            }
            else
            {
                reaction = FlawReputationTable.IgnorePublic(b, sev);
                if (quote != null) IgnoreConsequences(record, quote, _host.Today);
            }

            int delta = FlawReputationTable.CoverUp(b, FlawReputationTable.PublicBase(b, sev), reaction);
            Reputation(record, delta, FlawReputationKeys.Leak);
            Popularity(record, FlawPopularity.HitOf(b, sev, FlawPopularityHit.Public));
            _host.Log("leak " + record + (quiet ? " (quiet revision)" : " (ignored)") + ": reputation " + delta);
        }

        /// <summary>
        /// The revision is finished (the record is already <see cref="FlawState.Resolved"/>): a public one still owes the residual claims
        /// (<c>ResidualClaimsCost</c>, <c>Fines</c>, FR-7 item 3); the entries of a patch that waited for it are taken away (FR-7 item 6, FR-8 item 5).
        /// </summary>
        public void CompleteStepping(FlawRecord record)
        {
            if (record == null) return;

            FlawQuote quote = record.Quote;
            if (!record.Data.quietStepping && quote != null && quote.ResidualClaimsCost > 0f)
            {
                Guarded(record, "residual claims", () => Pay(record, quote.ResidualClaimsCost, FlawSpendKind.Fines));
            }
            Guarded(record, "patch entries", () => _channels.RemoveRatings(record));
            _host.Log("stepping finished " + record + (record.Data.quietStepping ? " (quiet: the leak tail runs until day " + record.Data.leakTailUntilDay + ")" : string.Empty));
        }

        /// <summary>The term of the patch is over (the record is already <see cref="FlawState.Resolved"/>): the entries have ended in the core; the ids are forgotten.</summary>
        public void ExpireMitigation(FlawRecord record)
        {
            if (record == null) return;

            Guarded(record, "patch entries", () => _channels.RemoveRatings(record));
            _host.Log("patch expired " + record);
        }

        /// <summary>
        /// The owner pays for a revision to replace the patch (the journal's button, FR-8 item 5): from <see cref="FlawState.Mitigated"/> the revision starts with
        /// the saved quote; the patch stays until the revision is done. <c>false</c> for a record that is not patched or has no quote.
        /// </summary>
        public bool FundSteppingForMitigated(FlawRecord record)
        {
            if (record == null) return false;
            if (record.State != FlawState.Mitigated)
            {
                _host.Warn("fund stepping for " + record + " ignored: the flaw is not patched");
                return false;
            }

            FlawQuote quote = record.Quote;
            if (quote == null)
            {
                _host.Warn("fund stepping for " + record + " ignored: the record has no quote");
                return false;
            }
            return BeginStepping(record, quote, false, "funded stepping");
        }

        /// <summary>One month of the drain of a publicly ignored flaw (FR-9 item 2): <c>drainPerMonth</c> points, <c>dfr.rep.drain</c>.</summary>
        public void IgnoreDrain(FlawRecord record)
        {
            if (record == null) return;
            Reputation(record, FlawReputationTable.Drain(_host.Balance), FlawReputationKeys.Drain);
        }

        /// <summary>The class action of a publicly ignored flaw (FR-9 item 3): <c>ClassActionCost</c>, <c>Fines</c>; the cycle files at most one.</summary>
        public void ClassActionFiled(FlawRecord record)
        {
            if (record == null) return;

            FlawQuote quote = record.Quote;
            if (quote == null || quote.ClassActionCost <= 0f) return;

            Guarded(record, "lawsuit", () => Pay(record, quote.ClassActionCost, FlawSpendKind.Fines));
            _host.Log("class action " + record + " cost=" + quote.ClassActionCost);
        }

        // ---- helpers ----

        private void Pay(FlawRecord record, float amount, FlawSpendKind kind)
        {
            if (!(amount > 0f) || float.IsInfinity(amount)) return;

            _host.Spend(record, amount, kind);
            record.Data.spentTotal += amount;
        }

        private void Reputation(FlawRecord record, int delta, string reasonKey)
        {
            if (delta == 0) return;
            Guarded(record, "reputation", () => _host.AddReputation(record, delta, reasonKey));
        }

        private void Popularity(FlawRecord record, float hit)
        {
            if (!(hit > 0f)) return;
            Guarded(record, "popularity", () => _host.HitPopularity(record, hit));
        }

        private void Announce(FlawRecord record, FlawState previous)
        {
            Guarded(record, "announcement", () => _host.StateChanged(record, previous));
        }

        /// <summary>Runs a step that follows the commitment of the answer: a failure is logged and the answer stands.</summary>
        private void Guarded(FlawRecord record, string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                try
                {
                    _host.Error(record + ": " + step + " failed", e);
                }
                catch (Exception)
                {
                    // The log must not stop the answer.
                }
            }
        }
    }
}
