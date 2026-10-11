using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The antitrust cases of the game in progress (design.md, Component 8: <c>CaseService</c> without the glue to the event system; FR-5, FR-19): opens a case,
    /// carries out a choice, and once a month charges the running costs, rules, imposes a new fine and closes the cases whose target went bankrupt. The state
    /// is the list <c>cases</c> of the save section (taken afresh from <c>data()</c> whenever the core works: the section replaces its object on a load). The
    /// rules are <see cref="AntitrustCaseMachine"/>; this class carries out its requests through the host interfaces, so the same rules apply to the
    /// player and to an AI company (FR-5 item 9) and the tests drive them with fakes.
    /// <list type="bullet">
    /// <item><b>Rolls.</b> One number from the generator per ruling and per new fine, drawn when the decision is made.</item>
    /// <item><b>Active.</b> A case is "active" for <c>hist.CaseActive</c> from its opening until it ends without liability; an open one (something can still be decided,
    /// ruled or imposed) raises the price of acquisitions by the buyer who is its target (<see cref="HasOpenCase"/>). Opening and ending an open case calls
    /// <c>ModModifiers.Invalidate("acquisition.price")</c>.</item>
    /// <item><b>Failures.</b> A case that throws three months in a row is closed with a line in the log (design.md, "Error Handling").</item>
    /// </list>
    /// </summary>
    public sealed class CaseCore
    {
        /// <summary>The event the regulator's new fine fires for the target (its choices are <c>reimposePay</c> and <c>reimposeAppeal</c>).</summary>
        public const string ReimposeEventId = "hist.decide.antitrust.reimpose";

        /// <summary>The channel the buyer's open case raises (FR-19, read by <c>rival-acquisitions</c>).</summary>
        public const string AcquisitionChannel = "acquisition.price";

        /// <summary>The reason shown for the behavioural remedies this class adds to <c>ModModifiers</c>.</summary>
        public const string RemedyReasonKey = "hist.mod.remedy";

        private const int MaxFailures = 3;
        private const int MaxFinished = 24;

        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly IHistModifiers _modifiers;
        private readonly IEventFirer _firer;
        private readonly Func<IModRng> _rng;
        private readonly Func<string, float> _shareOf;
        private readonly Func<float, string> _money;
        private readonly Action<string> _log;
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <param name="data">The section of the save (never null; asked afresh at every use).</param>
        /// <param name="shareOf">The share of a company over all segments for <c>dominanceLostShare</c> (the share cache of the roles).</param>
        /// <param name="money">Formats an amount for a note to the player; null prints the number.</param>
        /// <param name="log">Receives a line per problem; may be null.</param>
        public CaseCore(Func<HistSaveData> data, IHistEconomy economy, IHistModifiers modifiers, IEventFirer firer, Func<IModRng> rng, Func<string, float> shareOf,
            Func<float, string> money = null, Action<string> log = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (economy == null) throw new ArgumentNullException("economy");
            if (modifiers == null) throw new ArgumentNullException("modifiers");
            if (firer == null) throw new ArgumentNullException("firer");
            if (rng == null) throw new ArgumentNullException("rng");
            _data = data;
            _economy = economy;
            _modifiers = modifiers;
            _firer = firer;
            _rng = rng;
            _shareOf = shareOf ?? (key => 1f);
            _money = money ?? (amount => amount.ToString("0"));
            _log = log;
        }

        /// <summary>The cases, oldest first (a view; do not change). Open ones, then the finished ones that are kept for the chronicle.</summary>
        public IReadOnlyList<CaseDto> Cases
        {
            get { return _data().cases; }
        }

        /// <summary>The case by its id, or null.</summary>
        public CaseDto Find(string caseId)
        {
            if (string.IsNullOrEmpty(caseId)) return null;
            foreach (CaseDto c in _data().cases)
            {
                if (c != null && string.Equals(c.caseId, caseId, StringComparison.Ordinal)) return c;
            }
            return null;
        }

        /// <summary>The numbers of a case, read from the parameters it was opened with.</summary>
        public static CaseParams ParamsOf(CaseDto c)
        {
            return CaseParams.Read(c == null ? null : c.@params, null);
        }

        // ---- the questions of the calendar and the acquisition surcharge ----

        /// <summary>
        /// The condition <c>hist.CaseActive</c>: a case with this id exists and has not ended without liability; with <paramref name="targetNotPlayer"/> its
        /// target must also be a company other than the player.
        /// </summary>
        public bool IsActive(string caseId, bool targetNotPlayer)
        {
            CaseDto c = Find(caseId);
            if (c == null || !CaseStages.CountsAsActive(c.stage)) return false;
            return !targetNotPlayer || !CompanyKey.IsPlayer(c.targetKey);
        }

        /// <summary>This company is the target of a case that is not over (FR-19: the surcharge on what the player pays for an acquisition).</summary>
        public bool HasOpenCase(string targetKey)
        {
            if (string.IsNullOrEmpty(targetKey)) return false;
            foreach (CaseDto c in _data().cases)
            {
                if (c != null && string.Equals(c.targetKey, targetKey, StringComparison.Ordinal) && CaseStages.IsOpen(c.stage)) return true;
            }
            return false;
        }

        // ---- opening and choosing ----

        /// <summary>
        /// Opens a case of the effect's numbers against <paramref name="targetKey"/>. False (and nothing happens) when a case with this id is already there or the
        /// target is not a company of the game.
        /// </summary>
        /// <param name="entryId">The calendar entry that fired the event (kept in the case for the new fine's event).</param>
        /// <param name="beneficiaryKey">The company a warning favours; may be empty.</param>
        /// <param name="raw">The <c>params</c> of the effect, kept in the case.</param>
        /// <param name="fine">The fine worked out when the event was resolved (the number the player saw).</param>
        public bool Open(string caseId, string entryId, string targetKey, string beneficiaryKey, CaseParams p, JsonObject raw, float fine)
        {
            if (string.IsNullOrEmpty(caseId) || p == null) return false;
            if (!CompanyKey.IsValid(targetKey) || !_economy.IsAlive(targetKey)) return false;
            if (Find(caseId) != null)
            {
                Log("case " + caseId + " is already there; the second opening is ignored");
                return false;
            }

            string beneficiary = !string.IsNullOrEmpty(beneficiaryKey) && CompanyKey.IsValid(beneficiaryKey) && _economy.IsAlive(beneficiaryKey) ? beneficiaryKey : null;
            CaseStep step = AntitrustCaseMachine.Open(caseId, entryId, targetKey, beneficiary, p, raw, fine, _economy.Today);
            _data().cases.Add(step.Case);
            Run(step.Case, step);

            if (CaseStages.IsOpen(step.Case.stage)) _modifiers.Invalidate(AcquisitionChannel);
            return true;
        }

        /// <summary>The target's choice (or the default choice the timer applied). False when there is no such case or the action does not fit its stage.</summary>
        /// <param name="legalPerMonth">The monthly legal cost the player was shown, for a fight.</param>
        public bool Choose(string caseId, string action, float legalPerMonth)
        {
            CaseDto c = Find(caseId);
            if (c == null)
            {
                Log("no case " + caseId + " for the choice " + action);
                return false;
            }

            bool wasOpen = CaseStages.IsOpen(c.stage);
            CaseStep step = AntitrustCaseMachine.Choose(c, action, CaseParams.Read(c.@params, null), legalPerMonth, _economy.Today);
            if (!step.Accepted)
            {
                Log("case " + caseId + " is at stage " + c.stage + ": the action " + action + " does not fit");
                return false;
            }

            Run(c, step);
            if (wasOpen != CaseStages.IsOpen(c.stage)) _modifiers.Invalidate(AcquisitionChannel);
            return true;
        }

        // ---- the month ----

        /// <summary>
        /// The monthly pass (priority <see cref="HistTickPriority.Cases"/>): the running costs, the rulings and new fines that are due, the defaults for cases
        /// whose target never chose, the cases whose target is gone. A case that fails is skipped; three failures in a row close it.
        /// </summary>
        public void OnMonth()
        {
            HistSaveData data = _data();
            int today = _economy.Today;

            foreach (CaseDto c in new List<CaseDto>(data.cases))
            {
                if (c == null) continue;
                try
                {
                    Process(c, today);
                    _failures.Remove(c.caseId);
                }
                catch (Exception ex)
                {
                    int count;
                    _failures.TryGetValue(c.caseId, out count);
                    _failures[c.caseId] = ++count;
                    Log("case " + c.caseId + " failed this month (" + count + "): " + ex.GetType().Name + ": " + ex.Message);

                    if (count >= MaxFailures)
                    {
                        Log("case " + c.caseId + " failed " + MaxFailures + " months in a row and is closed");
                        bool wasOpen = CaseStages.IsOpen(c.stage);
                        AntitrustCaseMachine.Close(c);
                        if (wasOpen) _modifiers.Invalidate(AcquisitionChannel);
                        _failures.Remove(c.caseId);
                    }
                }
            }
            Prune(data);
        }

        /// <summary>A company left the game (bankruptcy): the cases against it are closed without payments.</summary>
        public void OnCompanyGone(string companyKey)
        {
            foreach (CaseDto c in _data().cases)
            {
                if (c == null || !string.Equals(c.targetKey, companyKey, StringComparison.Ordinal) || !CaseStages.IsOpen(c.stage)) continue;
                AntitrustCaseMachine.Close(c);
                _modifiers.Invalidate(AcquisitionChannel);
            }
        }

        private void Process(CaseDto c, int today)
        {
            if (!CaseStages.IsOpen(c.stage)) return;

            if (!_economy.IsAlive(c.targetKey))
            {
                AntitrustCaseMachine.Close(c);
                _modifiers.Invalidate(AcquisitionChannel);
                return;
            }

            CaseParams p = CaseParams.Read(c.@params, null);

            if (CaseStages.IsAwaitingChoice(c.stage))
            {
                if (today >= c.rulingDay) ApplyDefault(c);
                return;
            }

            if (c.stage == CaseStages.Reimposition)
            {
                if (today < c.reimposeDay) return;
                CaseStep again = AntitrustCaseMachine.Reimpose(c, p, _rng().Value(), EntryOf(c), _economy.NameOf(c.targetKey), today);
                bool wasOpen = true;
                Run(c, again);
                if (wasOpen != CaseStages.IsOpen(c.stage)) _modifiers.Invalidate(AcquisitionChannel);
                return;
            }

            if (c.stage == CaseStages.Appeal || c.stage == CaseStages.ConsentFight) Run(c, AntitrustCaseMachine.Monthly(c));

            if (today >= c.rulingDay)
            {
                CaseStep ruling = AntitrustCaseMachine.Rule(c, p, _shareOf(c.targetKey), _rng().Value(), _economy.Revenue12m(c.targetKey), today);
                Run(c, ruling);
                if (!CaseStages.IsOpen(c.stage)) _modifiers.Invalidate(AcquisitionChannel);
            }
        }

        /// <summary>The timer of the event ran out and nobody applied the default choice (the instance was cancelled): the case takes it.</summary>
        private void ApplyDefault(CaseDto c)
        {
            string action = CaseStages.DefaultAction(c.stage);
            if (action == null) return;

            Log("case " + c.caseId + " waited for its target; the default choice " + action + " is applied");
            Choose(c.caseId, action, 0f);
        }

        // ---- carrying out the requests ----

        private void Run(CaseDto c, CaseStep step)
        {
            bool fireFailed = false;
            foreach (CaseOp op in step.Ops)
            {
                switch (op.Kind)
                {
                    case CaseOpKind.Spend:
                        if (_economy.IsAlive(op.CompanyKey)) _economy.Spend(op.CompanyKey, op.Amount, op.Expense);
                        break;

                    case CaseOpKind.Earn:
                        if (_economy.IsAlive(op.CompanyKey)) _economy.Earn(op.CompanyKey, op.Amount, op.Income);
                        break;

                    case CaseOpKind.Reputation:
                        if (_economy.IsAlive(op.CompanyKey)) _economy.AddReputation(op.CompanyKey, op.Amount, op.Key);
                        break;

                    case CaseOpKind.Remedy:
                        if (_economy.IsAlive(op.CompanyKey)) _modifiers.Add(op.Channel, op.CompanyKey, op.Multiplier, op.Days, RemedyReasonKey, null);
                        break;

                    case CaseOpKind.Notify:
                        if (op.Amount > 0f) _economy.Notify(op.CompanyKey, op.Key, _money(op.Amount));
                        else _economy.Notify(op.CompanyKey, op.Key);
                        break;

                    case CaseOpKind.Fire:
                        FireResult result = _firer.Fire(op.Key, op.CompanyKey, op.Args);
                        if (result == FireResult.UnknownEvent || result == FireResult.Disabled) fireFailed = true;
                        break;
                }
            }

            // The new fine could not be announced (the pack is off or lacks the event): the target takes the default choice, as the design says.
            if (fireFailed) ApplyDefault(c);
        }

        private static string EntryOf(CaseDto c)
        {
            return c.@params == null ? string.Empty : c.@params.GetString("entryId", string.Empty);
        }

        /// <summary>Keeps the chronicle short: the oldest finished cases beyond the limit are forgotten.</summary>
        private static void Prune(HistSaveData data)
        {
            int finished = 0;
            foreach (CaseDto c in data.cases) if (c != null && !CaseStages.IsOpen(c.stage)) finished++;

            while (finished > MaxFinished)
            {
                CaseDto oldest = null;
                foreach (CaseDto c in data.cases)
                {
                    if (c == null || CaseStages.IsOpen(c.stage)) continue;
                    if (oldest == null || c.openedDay < oldest.openedDay) oldest = c;
                }
                if (oldest == null) break;
                data.cases.Remove(oldest);
                finished--;
            }
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
