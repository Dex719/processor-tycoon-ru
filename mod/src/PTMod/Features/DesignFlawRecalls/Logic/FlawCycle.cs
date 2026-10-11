using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the monitor learns about the CPUs of a record from the game, in one reading (design.md Component 6). A plain value filled by
    /// <c>GameCpuView</c>; the pure cycle never touches the game. The first group is read every month, the second only when a quote is made.
    /// </summary>
    public sealed class FlawFacts
    {
        /// <summary>The base CPU of the record exists (<c>false</c>: <c>FindCpu</c> returned <c>null</c>, FR-4 item 5).</summary>
        public bool Found = true;

        /// <summary>Units of the affected CPUs sold since the start: <c>MarketData.Sold</c> plus <c>SoldByContracts</c>, all months (<c>U</c>).</summary>
        public long Units;

        /// <summary>Units sold in the last <c>expireNoSalesMonths</c> closed months (the window of the "no sales" rule).</summary>
        public long UnitsInNoSalesWindow;

        /// <summary>How many closed months in a row, counting back from the last one, nothing was sold.</summary>
        public int EmptyMonths;

        /// <summary>Every affected CPU is off the market (<c>IsRetired</c>).</summary>
        public bool Retired;

        // ---- only for a quote ----

        /// <summary>Units of the affected CPUs in stock.</summary>
        public int Stock;

        public float RealUnitCost;
        public float Price;
        public float MaxVariantPrice;
        public float DevelopmentCost;

        /// <summary>The inflation now.</summary>
        public float Inflation;

        /// <summary>The revenue of the owner over the 12 closed months before the current one.</summary>
        public float Revenue12m;

        /// <summary>The current year of the game.</summary>
        public int Year;
    }

    /// <summary>
    /// The game side of the monthly and daily cycle (design.md Component 6): the clock, the numbers, the facts about the CPUs, and the effects that
    /// need the game (money, reputation, mail, channels). <see cref="FlawCycle"/> decides what happens and moves the records; the host does the
    /// rest. The implementation of the feature is <c>FlawMonitor</c>; a test brings a fake. Every effect method is called after the record has
    /// been moved, and an exception in it counts as a failure of that record only.
    /// </summary>
    public interface IFlawCycleHost
    {
        /// <summary>Today as a day number (days from 1971-01-01); 0 outside the game (the cycle does nothing then).</summary>
        int Today { get; }

        /// <summary>The numbers of the feature.</summary>
        FlawBalance Balance { get; }

        /// <summary>The random stream of the monitor (<c>ModRandom.Stream("design-flaw-recalls/monitor")</c>).</summary>
        IModRng Rng { get; }

        /// <summary>A pass over the records begins (the host may build an index of the CPUs of the game for it).</summary>
        void BeginPass();

        /// <summary>The pass is over (the host drops the index).</summary>
        void EndPass();

        /// <summary>The owner of the record still exists and has not gone bankrupt (<c>false</c> closes the record, FR-4 item 5).</summary>
        bool OwnerIsAlive(FlawRecord record);

        /// <summary>The facts about the CPUs of the record. <see cref="FlawFacts.Found"/> is <c>false</c> when the base CPU is gone.</summary>
        FlawFacts ReadFacts(FlawRecord record, bool forQuote);

        /// <summary>Charges the owner for the days of a revision: <c>SpendMoney(amount, ExpenseOrigin.Development)</c>.</summary>
        void ChargeStepping(FlawRecord record, float amount);

        /// <summary>A hidden flaw was found; the quote is in the record. The base reputation and popularity hit, the decision window or the AI's choice (TSK-010, TSK-013, TSK-015).</summary>
        void Discovered(FlawRecord record);

        /// <summary>A cover-up leaked: the record is now public with <c>coverUp</c> and no response. The full recall cost, the reputation, a new choice (TSK-011).</summary>
        void Leaked(FlawRecord record);

        /// <summary>A revision is finished: the record is resolved. The residual claims of a public revision, the end of a patch that waited for it (TSK-011).</summary>
        void SteppingCompleted(FlawRecord record);

        /// <summary>The term of a patch is over: the record is resolved. The entries of the channel expire by themselves (TSK-012).</summary>
        void MitigationExpired(FlawRecord record);

        /// <summary>A month of the drain of the reputation of a publicly ignored flaw: <c>ModReputation.Add(company, -1, "dfr.rep.drain")</c> (TSK-012).</summary>
        void IgnoreDrain(FlawRecord record);

        /// <summary>The class action came (<c>suitFiled</c> is set): the lawsuit is charged with <c>ExpenseOrigin.Fines</c> (TSK-012).</summary>
        void ClassActionFiled(FlawRecord record);

        /// <summary>The record moved to another state (<paramref name="previous"/> is the old one): the public API tells its subscribers; a closed record gives up its channel entries.</summary>
        void StateChanged(FlawRecord record, FlawState previous);

        /// <summary>A line for the log of the feature.</summary>
        void Log(string message);

        /// <summary>An exception while processing a record (the cycle goes on with the next record).</summary>
        void Error(string message, Exception exception);
    }

    /// <summary>
    /// The monthly and daily life of the records (FR-4, FR-7, FR-8, FR-9; design.md Component 6): who is found, who expires, who leaks, what a revision
    /// costs each day, when a revision and a patch end, and how the terms are moved after a pause. Pure: the game is behind <see cref="IFlawCycleHost"/>.
    /// <list type="bullet">
    /// <item><b>Dates.</b> A term is an end day (days from 1971-01-01), so a save, a load and a pause do not change it; a revision pays for the days that
    /// were missed (<c>lastChargedDay</c>).</item>
    /// <item><b>Errors.</b> A record that throws is skipped, the others go on; three failures in a row close it (design.md "Error Handling").</item>
    /// <item><b>Batch run.</b> The cycle does not look at the batch flag: <c>OnDay</c> and <c>OnMonth</c> run for every day as usual, and the core holds
    /// the mail back (FR-4 item 7).</item>
    /// </list>
    /// </summary>
    public sealed class FlawCycle
    {
        /// <summary>A record that fails this many times in a row is closed.</summary>
        public const int MaxFailStreak = 3;

        private readonly FlawStore _store;
        private readonly IFlawCycleHost _host;

        public FlawCycle(FlawStore store, IFlawCycleHost host)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (host == null) throw new ArgumentNullException("host");
            _store = store;
            _host = host;
        }

        // ---- the month (TickPhase.MonthPassed) ----

        /// <summary>
        /// The first of a month: for every record in the order of the ids, close it if its owner or CPU is gone; roll the discovery of a hidden flaw
        /// (expiry, forced, chance); roll the leak of an ignored internal flaw and of a quiet revision and its tail; drain the reputation of a publicly
        /// ignored flaw and roll its lawsuit.
        /// </summary>
        public void OnMonth()
        {
            int today = _host.Today;
            if (today <= 0) return;

            List<FlawRecord> records = _store.Monitored(today);
            if (records.Count == 0) return;

            SafeBegin();
            try
            {
                foreach (FlawRecord record in records)
                {
                    try
                    {
                        MonthStep(record, today);
                        record.FailStreak = 0;
                    }
                    catch (Exception e)
                    {
                        Failed(record, "month", e);
                    }
                }
            }
            finally
            {
                SafeEnd();
            }
        }

        private void MonthStep(FlawRecord record, int today)
        {
            if (record.State == FlawState.Resolved)
            {
                TailStep(record, today);
                return;
            }

            if (!_host.OwnerIsAlive(record))
            {
                Close(record, "the owner went bankrupt or is gone");
                return;
            }

            FlawFacts facts = _host.ReadFacts(record, false);
            if (facts == null || !facts.Found)
            {
                Close(record, "the CPU was not found");
                return;
            }

            record.CachedUnits = facts.Units;
            record.CachedUnitsMonth = FlawDays.MonthKey(today);

            FlawBalance b = _host.Balance;
            FlawSeverity sev = record.Severity;
            int monthsSinceRelease = FlawDays.FullMonthsBetween(record.Data.releaseDay, today);

            switch (record.State)
            {
                case FlawState.Hidden:
                    HiddenStep(record, facts, monthsSinceRelease, today, b);
                    break;

                case FlawState.InternalKnown:
                    if (record.Response != FlawResponse.Ignore) break;
                    if (ExpireIgnored(record, facts, monthsSinceRelease, b)) break;
                    if (_host.Rng.Chance(DiscoveryModel.LeakRate(FlawResponse.Ignore, false, b))) Leak(record, today);
                    break;

                case FlawState.Public:
                    if (record.Response != FlawResponse.Ignore) break;
                    if (ExpireIgnored(record, facts, monthsSinceRelease, b)) break;
                    IgnoredPublicStep(record, sev, today, b);
                    break;

                case FlawState.Stepping:
                    if (record.Data.quietStepping && _host.Rng.Chance(DiscoveryModel.LeakRate(FlawResponse.Stepping, false, b))) Leak(record, today);
                    break;
            }
        }

        private void HiddenStep(FlawRecord record, FlawFacts facts, int monthsSinceRelease, int today, FlawBalance b)
        {
            FlawSeverity sev = record.Severity;
            int monthsSinceRetired = facts.Retired ? facts.EmptyMonths : 0;

            if (DiscoveryModel.IsExpired(facts.Retired, monthsSinceRetired, monthsSinceRelease, facts.UnitsInNoSalesWindow, b))
            {
                Expire(record);
                return;
            }

            if (DiscoveryModel.IsForced(sev, monthsSinceRelease, facts.Units, b))
            {
                Reveal(record, DiscoverySource.Forced);
                return;
            }

            IModRng rng = _host.Rng;
            if (!rng.Chance(DiscoveryModel.MonthlyHazard(sev, facts.Units, b))) return;

            Reveal(record, DiscoveryModel.PickSource(sev, rng, b));
        }

        /// <summary>An ignored flaw that nobody can meet any more ends without consequences (FR-9 item 6). True when it did.</summary>
        private bool ExpireIgnored(FlawRecord record, FlawFacts facts, int monthsSinceRelease, FlawBalance b)
        {
            int monthsSinceRetired = facts.Retired ? facts.EmptyMonths : 0;
            if (!DiscoveryModel.IsExpired(facts.Retired, monthsSinceRetired, monthsSinceRelease, facts.UnitsInNoSalesWindow, b)) return false;

            return Expire(record);
        }

        private void IgnoredPublicStep(FlawRecord record, FlawSeverity sev, int today, FlawBalance b)
        {
            if (record.Data.ignoreDrainUntilDay >= today) _host.IgnoreDrain(record);

            if (record.Data.suitFiled) return;
            float chance = FlawMath.At(b.Responses.Ignore.ClassAction, FlawMath.SeverityIndex(sev), 0f);
            if (!_host.Rng.Chance(chance)) return;

            record.Data.suitFiled = true;
            _host.ClassActionFiled(record);
        }

        /// <summary>The tail of a quiet revision: a flaw that was fixed in silence may still leak for <c>leakTailMonths</c> months (FR-7 item 4).</summary>
        private void TailStep(FlawRecord record, int today)
        {
            if (!record.Data.quietStepping || record.Data.leakTailUntilDay < today) return;

            if (!_host.OwnerIsAlive(record))
            {
                record.Data.leakTailUntilDay = 0;
                return;
            }

            if (_host.Rng.Chance(DiscoveryModel.LeakRate(FlawResponse.Stepping, true, _host.Balance))) Leak(record, today);
        }

        // ---- the day (TickPhase.DayPassed) ----

        /// <summary>
        /// A day: every revision pays for the days since <c>lastChargedDay</c> (up to its end) and ends when its term has come; every patch ends when its
        /// term has come. At the end <c>lastActiveDay</c> becomes today (the start of a pause the terms are moved by, D12).
        /// </summary>
        public void OnDay()
        {
            int today = _host.Today;
            if (today <= 0) return;

            foreach (FlawRecord record in _store.Active())
            {
                if (record.State != FlawState.Stepping && record.State != FlawState.Mitigated) continue;

                try
                {
                    DayStep(record, today);
                    record.FailStreak = 0;
                }
                catch (Exception e)
                {
                    Failed(record, "day", e);
                }
            }

            if (today > _store.LastActiveDay) _store.LastActiveDay = today;
        }

        private void DayStep(FlawRecord record, int today)
        {
            FlawRecordDto d = record.Data;

            if (record.State == FlawState.Mitigated)
            {
                if (today < d.mitigationEndDay) return;
                FlawState previous = record.State;
                if (!FlawStateMachine.ExpireMitigation(record)) return;

                _host.StateChanged(record, previous);
                _host.MitigationExpired(record);
                return;
            }

            if (!_host.OwnerIsAlive(record))
            {
                Close(record, "the owner went bankrupt or is gone");
                return;
            }

            int paidUntil = Math.Min(today, d.steppingEndDay);
            int days = paidUntil - d.lastChargedDay;
            if (days > 0)
            {
                // Charge first: if the host fails, the days stay unpaid and are paid with the next day (the dates, not the calls, are counted).
                float amount = days * d.steppingDailyCost;
                _host.ChargeStepping(record, amount);
                d.lastChargedDay = paidUntil;
                d.spentTotal += amount;
            }

            if (today < d.steppingEndDay) return;

            FlawState before = record.State;
            if (!FlawStateMachine.CompleteStepping(record, today, _host.Balance)) return;

            _host.StateChanged(record, before);
            _host.SteppingCompleted(record);
        }

        // ---- the pause (D12, NFR-2) ----

        /// <summary>
        /// The feature runs again after it was off (<c>OnEnabled</c>) or the game was loaded (<c>GameReady(Loaded)</c>): the revisions did not go
        /// while it was off, so <c>steppingEndDay</c> and <c>lastChargedDay</c> of every running revision move by <c>today - lastActiveDay - 1</c> days
        /// (the day the feature is switched on is paid as usual). The term of a patch, the drain and the leak tail run on the calendar and stay.
        /// <c>lastActiveDay</c> becomes today, so a second call does not move anything again.
        /// </summary>
        public void OnResume()
        {
            int today = _host.Today;
            if (today <= 0) return;

            int last = _store.LastActiveDay;
            int pause = today - last - 1;
            if (last > 0 && pause > 0)
            {
                foreach (FlawRecord record in _store.Active())
                {
                    if (record.State != FlawState.Stepping) continue;
                    record.Data.steppingEndDay += pause;
                    record.Data.lastChargedDay += pause;
                }
                _host.Log("resume: the revisions were moved by " + pause + " day(s) (the feature was off since day " + last + ")");
            }

            if (today > last) _store.LastActiveDay = today;
        }

        // ---- changes of the state ----

        /// <summary>
        /// Makes the hidden flaw of the record known by <paramref name="source"/> (FR-4, FR-13 item 1): moves the record, computes and saves the quote and
        /// the units sold, and lets the host react. False for a record that is not hidden or a source that is not a discovery.
        /// </summary>
        public bool Reveal(FlawRecord record, DiscoverySource source)
        {
            if (record == null) return false;

            int today = _host.Today;
            FlawState previous = record.State;
            if (!FlawStateMachine.Discover(record, source, today)) return false;

            SaveQuote(record, today, true);
            _host.Log("discovered " + record + " by " + source);
            _host.StateChanged(record, previous);
            _host.Discovered(record);
            return true;
        }

        /// <summary>
        /// Makes the prices of the four reactions anew from the facts of today (the owner's revenue caps the recall, the units sold since count), the way a leak does
        /// (the discovery stays as it was). For a record that is offered to a new owner (FR-19 item 3): the new owner pays by its own books. False, and the old quote stays,
        /// when the record is over, has no quote yet, or the game cannot tell the facts (the CPU is not found).
        /// </summary>
        public bool Requote(FlawRecord record)
        {
            if (record == null || record.IsFinal || record.Data.quote == null) return false;

            int today = _host.Today;
            return today > 0 && SaveQuote(record, today, false);
        }

        /// <summary>
        /// Closes one record that cannot go on (its owner is gone, FR-4 item 5, FR-19 item 6): the state becomes <see cref="FlawState.Closed"/>, the host is told (the channel
        /// entries and the place in the queue are given up there). False for a record that is over already.
        /// </summary>
        public bool CloseRecord(FlawRecord record, string reason)
        {
            if (record == null || record.IsFinal) return false;

            Close(record, reason);
            return record.State == FlawState.Closed;
        }

        /// <summary>
        /// Computes the quote of the record from the facts of today and saves it (D9). <paramref name="atDiscovery"/> - the units are the ones at the discovery
        /// (<c>unitsAtDiscovery</c> is written); after a leak the prices are made anew from today's facts and the discovery stays as it was.
        /// </summary>
        private bool SaveQuote(FlawRecord record, int today, bool atDiscovery)
        {
            FlawFacts facts = _host.ReadFacts(record, true);
            if (facts == null || !facts.Found) return false;

            FlawBalance b = _host.Balance;
            var input = new QuoteInput
            {
                Severity = record.Severity,
                Type = b.FindType(record.TypeId) ?? FlawTypeDef.FallbackArithmetic(),
                AffectedUnits = facts.Units,
                StockUnits = facts.Stock,
                RealUnitCost = facts.RealUnitCost,
                Price = facts.Price,
                MaxVariantPrice = facts.MaxVariantPrice,
                DevelopmentCost = facts.DevelopmentCost,
                InflationNow = facts.Inflation,
                InflationAtRelease = record.Data.inflationAtRelease,
                Revenue12m = facts.Revenue12m,
                Year = facts.Year > 0 ? facts.Year : FlawDays.YearOf(today),
                Quiet = record.State == FlawState.InternalKnown,
            };

            record.Data.quote = ResponseCostCalculator.Quote(input, b).ToDto();
            if (atDiscovery) record.Data.unitsAtDiscovery = facts.Units;
            record.CachedUnits = facts.Units;
            record.CachedUnitsMonth = FlawDays.MonthKey(today);
            return true;
        }

        private void Leak(FlawRecord record, int today)
        {
            FlawState previous = record.State;
            if (!FlawStateMachine.Leak(record, today)) return;

            // The owner chooses again, now with a public flaw and with the units sold since: the window shows the prices of today.
            SaveQuote(record, today, false);
            _host.Log("leak " + record);
            _host.StateChanged(record, previous);
            _host.Leaked(record);
        }

        private bool Expire(FlawRecord record)
        {
            FlawState previous = record.State;
            if (!FlawStateMachine.Expire(record)) return false;

            _host.Log("expired " + record);
            _host.StateChanged(record, previous);
            return true;
        }

        private void Close(FlawRecord record, string reason)
        {
            FlawState previous = record.State;
            if (!FlawStateMachine.Close(record)) return;

            _host.Log("closed " + record + ": " + reason);
            _host.StateChanged(record, previous);
        }

        /// <summary>
        /// Closes every record of a company that is not over (the company went bankrupt, <c>BankruptcyHandler.OnCompanyWentBankrupt</c>, FR-4 item 5)
        /// and ends the leak tails of its resolved ones. Returns how many records were closed.
        /// </summary>
        public int CloseCompany(string companyKey, string reason)
        {
            if (string.IsNullOrEmpty(companyKey)) return 0;

            int closed = 0;
            foreach (FlawRecord record in new List<FlawRecord>(_store.All))
            {
                if (record.CompanyKey != companyKey) continue;

                try
                {
                    if (record.IsFinal)
                    {
                        record.Data.leakTailUntilDay = 0;
                        continue;
                    }

                    Close(record, reason);
                    if (record.State == FlawState.Closed) closed++;
                }
                catch (Exception e)
                {
                    Failed(record, "close company", e);
                }
            }
            return closed;
        }

        // ---- failures ----

        private void Failed(FlawRecord record, string phase, Exception e)
        {
            record.FailStreak++;
            try
            {
                _host.Error(record + ": " + phase + " failed (" + record.FailStreak + " in a row)", e);
            }
            catch (Exception)
            {
                // The log must not stop the cycle.
            }

            if (record.FailStreak < MaxFailStreak || record.IsFinal) return;

            try
            {
                Close(record, MaxFailStreak + " failures in a row");
            }
            catch (Exception closing)
            {
                record.Data.state = (int)FlawState.Closed;
                SafeError("the record could not be closed properly: " + record, closing);
            }
        }

        private void SafeBegin()
        {
            try
            {
                _host.BeginPass();
            }
            catch (Exception e)
            {
                SafeError("BeginPass failed", e);
            }
        }

        private void SafeEnd()
        {
            try
            {
                _host.EndPass();
            }
            catch (Exception e)
            {
                SafeError("EndPass failed", e);
            }
        }

        private void SafeError(string message, Exception e)
        {
            try
            {
                _host.Error(message, e);
            }
            catch (Exception)
            {
                // Nothing more can be done.
            }
        }
    }
}
