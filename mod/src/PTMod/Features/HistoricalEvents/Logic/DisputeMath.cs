using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The two kinds of patent dispute (<c>mode</c> of the effect <c>PatentDispute</c>, FR-9).</summary>
    public static class DisputeModes
    {
        /// <summary>The plaintiff withdraws a licence of the defendant: arbitration, a new licence, or an independent development.</summary>
        public const string LicenseWithdrawal = "licenseWithdrawal";

        /// <summary>The plaintiff claims the defendant copied its design: fight, settle or redesign.</summary>
        public const string Infringement = "infringement";

        public static readonly string[] All = { LicenseWithdrawal, Infringement };
    }

    /// <summary>The actions of the effect <c>PatentDispute</c> (design.md, Component 8).</summary>
    public static class DisputeActions
    {
        /// <summary>The dispute begins (the common effect of the defendant's event).</summary>
        public const string Open = "open";

        public const string Arbitrate = "arbitrate";
        public const string License = "license";
        public const string Independent = "independent";
        public const string Fight = "fight";
        public const string Settle = "settle";
        public const string Redesign = "redesign";

        /// <summary>The plaintiff files the claim: the defendant gets its decision event.</summary>
        public const string Sue = "sue";

        public const string Decline = "decline";

        public static readonly string[] All = { Open, Arbitrate, License, Independent, Fight, Settle, Redesign, Sue, Decline };

        /// <summary>The action is one of the defendant's answers in the mode.</summary>
        public static bool IsAnswerIn(string mode, string action)
        {
            if (mode == DisputeModes.LicenseWithdrawal) return action == Arbitrate || action == License || action == Independent;
            if (mode == DisputeModes.Infringement) return action == Fight || action == Settle || action == Redesign;
            return false;
        }
    }

    /// <summary>The keys the disputes use (all in the string tables of the feature).</summary>
    public static class DisputeKeys
    {
        public const string ResearchReasonKey = "hist.mod.dispute";
        public const string NoteWon = "hist.note.dispute.won";
        public const string NoteFightWon = "hist.note.dispute.fightWon";
        public const string NoteLost = "hist.note.dispute.lost";

        /// <summary>The event the plaintiff's claim fires for the defendant, by the mode of the dispute.</summary>
        public static string DecisionEvent(string mode)
        {
            return mode == DisputeModes.Infringement ? "hist.decide.patent.infringement" : "hist.decide.patent.withdrawal";
        }
    }

    /// <summary>
    /// The numbers of the patent disputes (the block <c>disputeDefaults</c> of <c>params.json</c>, design.md formula 8; FR-9): the effect <c>PatentDispute</c> itself carries
    /// only the id, the mode and the action. The names are those of the file; the members that the design file does not list have defaults.
    /// </summary>
    public sealed class DisputeParams
    {
        /// <summary>The monthly legal costs of the defendant in a fight, as a share of its revenue of 12 months.</summary>
        public float LegalCostPct = 0.002f;

        /// <summary>The months of an arbitration.</summary>
        public int ArbitrationMonths = 48;

        public float PWinArbitration = 0.6f;

        /// <summary>The monthly licence fee as a share of the defendant's revenue of the last month.</summary>
        public float LicenseFeePct = 0.0125f;

        public int LicenseMonths = 36;

        /// <summary>The multiplier of the research cost of a defendant who loses the arbitration or develops its own design.</summary>
        public float ResearchCostMultiplier = 1.3f;

        public float PWinInfringement = 0.55f;

        /// <summary>The one-time payment of a settlement, and the base of the payment after a lost fight, as a share of the defendant's revenue of 12 months.</summary>
        public float SettlePct = 0.03f;

        public float RedesignMultiplier = 1.2f;
        public int RedesignMonths = 24;

        /// <summary>What the plaintiff pays the defendant when it loses the arbitration, as a share of its own revenue of 12 months.</summary>
        public float PlaintiffSettlementPct = 0.03f;

        // Not in the design file: the length of the fight over a copied design, the factor of a lost fight (formula 8: "settle amount × 1.5"), the length of the
        // independent development (FR-9 item 1: 36 months) and of the research penalty after a lost arbitration (formula 8: 730 days).
        public int FightMonths = 24;
        public float FightLossFactor = 1.5f;
        public int IndependentMonths = 36;
        public int LostArbitrationDays = 730;

        /// <summary>Reads the block; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static DisputeParams Read(JsonObject block, List<string> problems)
        {
            var r = new ParamReader(block, null, "disputeDefaults", problems);
            var p = new DisputeParams();

            p.LegalCostPct = r.Num("legalCostPct", p.LegalCostPct, 0f, 0.05f);
            p.ArbitrationMonths = r.Int("arbitrationMonths", p.ArbitrationMonths, 1, 240);
            p.PWinArbitration = r.Num("pWinArbitration", p.PWinArbitration, 0f, 1f);
            p.LicenseFeePct = r.Num("licenseFeePct", p.LicenseFeePct, 0f, 0.2f);
            p.LicenseMonths = r.Int("licenseMonths", p.LicenseMonths, 1, 240);
            p.ResearchCostMultiplier = r.Num("researchCostMultiplier", p.ResearchCostMultiplier, 1f, 3f);
            p.PWinInfringement = r.Num("pWinInfringement", p.PWinInfringement, 0f, 1f);
            p.SettlePct = r.Num("settlePct", p.SettlePct, 0f, 0.3f);
            p.RedesignMultiplier = r.Num("redesignMultiplier", p.RedesignMultiplier, 1f, 3f);
            p.RedesignMonths = r.Int("redesignMonths", p.RedesignMonths, 1, 240);
            p.PlaintiffSettlementPct = r.Num("plaintiffSettlementPct", p.PlaintiffSettlementPct, 0f, 0.3f);
            p.FightMonths = r.Int("fightMonths", p.FightMonths, 1, 240);
            p.FightLossFactor = r.Num("fightLossFactor", p.FightLossFactor, 1f, 5f);
            p.IndependentMonths = r.Int("independentMonths", p.IndependentMonths, 1, 240);
            p.LostArbitrationDays = r.Int("lostArbitrationDays", p.LostArbitrationDays, 1, 3650);
            r.WarnUnknown();
            return p;
        }
    }

    /// <summary>The identity of a dispute effect: the id, the mode and the action (what the effect itself carries).</summary>
    public sealed class DisputeSpec
    {
        public string DisputeId = string.Empty;
        public string Mode = DisputeModes.LicenseWithdrawal;
        public string Action = DisputeActions.Open;

        /// <summary>Reads the three members; a bad one is a line in <paramref name="problems"/> (may be null). Never null.</summary>
        public static DisputeSpec Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "PatentDispute", problems);
            var spec = new DisputeSpec();
            spec.DisputeId = r.Str("disputeId", string.Empty);
            spec.Mode = r.Choice("mode", DisputeModes.LicenseWithdrawal, DisputeModes.All);
            spec.Action = r.Choice("action", DisputeActions.Open, DisputeActions.All);

            if (!DisputeActions.IsAnswerIn(spec.Mode, spec.Action) && spec.Action != DisputeActions.Open && spec.Action != DisputeActions.Sue && spec.Action != DisputeActions.Decline)
                r.Fail("action", "\"" + spec.Action + "\" is not an answer in the mode " + spec.Mode);
            r.WarnUnknown();
            return spec;
        }
    }

    /// <summary>What a choice costs, worked out when the event is resolved (the numbers under the buttons).</summary>
    public sealed class DisputeQuote
    {
        public string Action;
        public string TextKey;

        /// <summary>The money paid when the action is applied (a settlement); legal costs and fees come monthly and are in <see cref="PerMonth"/>.</summary>
        public float Upfront;

        public float PerMonth;
        public int Months;

        /// <summary>The research-cost multiplier, if the action brings one (0 for none).</summary>
        public float ResearchMultiplier;

        /// <summary>The chance of winning, if the action is decided in court (0 for none).</summary>
        public float WinChance;
    }

    /// <summary>The arithmetic of the patent disputes without the game (design.md formula 8; FR-9).</summary>
    public static class DisputeMath
    {
        /// <summary>What an answer costs the defendant now (the numbers under the buttons).</summary>
        public static DisputeQuote Quote(string action, DisputeParams p, float defendantRevenue12m, float defendantMonthlyRevenue)
        {
            var q = new DisputeQuote { Action = action };
            switch (action)
            {
                case DisputeActions.Arbitrate:
                    q.PerMonth = HistMoney.Of(p.LegalCostPct, defendantRevenue12m);
                    q.Months = p.ArbitrationMonths;
                    q.WinChance = p.PWinArbitration;
                    q.ResearchMultiplier = p.ResearchCostMultiplier;
                    q.TextKey = "hist.fx.dispute.arbitrate";
                    break;
                case DisputeActions.License:
                    q.PerMonth = HistMoney.Of(p.LicenseFeePct, defendantMonthlyRevenue);
                    q.Months = p.LicenseMonths;
                    q.TextKey = "hist.fx.dispute.license";
                    break;
                case DisputeActions.Independent:
                    q.Months = p.IndependentMonths;
                    q.ResearchMultiplier = p.ResearchCostMultiplier;
                    q.TextKey = "hist.fx.dispute.independent";
                    break;
                case DisputeActions.Fight:
                    q.PerMonth = HistMoney.Of(p.LegalCostPct, defendantRevenue12m);
                    q.Months = p.FightMonths;
                    q.WinChance = p.PWinInfringement;
                    q.Upfront = 0f;
                    q.TextKey = "hist.fx.dispute.fight";
                    break;
                case DisputeActions.Settle:
                    q.Upfront = HistMoney.Of(p.SettlePct, defendantRevenue12m);
                    q.TextKey = "hist.fx.dispute.settle";
                    break;
                case DisputeActions.Redesign:
                    q.Months = p.RedesignMonths;
                    q.ResearchMultiplier = p.RedesignMultiplier;
                    q.TextKey = "hist.fx.dispute.redesign";
                    break;
                case DisputeActions.Sue:
                    q.TextKey = "hist.fx.dispute.sue";
                    break;
                case DisputeActions.Decline:
                    q.TextKey = "hist.fx.dispute.decline";
                    break;
                default:
                    q.TextKey = "hist.fx.dispute.open";
                    break;
            }
            return q;
        }

        /// <summary>The payment after a lost fight over a copied design: the settlement amount times the factor (formula 8: 1.5).</summary>
        public static float LostFightPayment(DisputeParams p, float defendantRevenue12m)
        {
            return HistMoney.Of(p.SettlePct * p.FightLossFactor, defendantRevenue12m);
        }

        /// <summary>The licence fee of a month: <c>licenseFeePct</c> of the defendant's revenue of the month (AC-9.1: 400 million × 0.0125 = 5 million).</summary>
        public static float LicenseFee(DisputeParams p, float defendantMonthlyRevenue)
        {
            return HistMoney.Of(p.LicenseFeePct, defendantMonthlyRevenue);
        }
    }

    /// <summary>
    /// The patent disputes of the game in progress (design.md, Component 8: <c>DisputeService</c> without the glue; FR-9). A dispute begins when the defendant's event
    /// fires (<see cref="Open"/>); the defendant answers (<see cref="Choose"/>); every month the costs of the answer are paid and the verdicts are given.
    /// <list type="bullet">
    /// <item><b>Arbitration / fight</b>: legal costs every month of the term; at the end one roll decides. Arbitration won: the plaintiff pays the defendant a share of its
    /// own revenue (<c>Fines</c> / <c>Royalties</c>); lost: the research cost of the defendant goes up for 730 days. Fight won: nothing; lost: the settlement amount × 1.5 to the plaintiff.</item>
    /// <item><b>Licence</b>: every month <c>licenseFeePct</c> of the defendant's revenue goes to the plaintiff for the term (<c>Royalties</c> both ways).</item>
    /// <item><b>Independent development / redesign</b>: the research cost of the defendant goes up for the term; <b>settle</b>: one payment to the plaintiff.</item>
    /// <item>The multiplier of the research cost is skipped for a company the game already penalizes (<see cref="IHistEconomy.IsUnlucky"/>, Cyrix): only the payments act.</item>
    /// <item>A dispute whose plaintiff or defendant has gone bankrupt ends without payments; one that no choice ever reached is dropped after a while.</item>
    /// </list>
    /// </summary>
    public sealed class DisputeCore
    {
        /// <summary>The days a dispute may wait for the defendant's answer before it is dropped (the timer of the event has long applied its default by then).</summary>
        public const int AwaitingDays = 180;

        public const string ResearchChannel = "research.cost";

        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly IHistModifiers _modifiers;
        private readonly IDelayedEventFirer _firer;
        private readonly Func<IModRng> _rng;
        private readonly Func<DisputeParams> _params;
        private readonly Func<float, string> _money;
        private readonly Action<string> _log;

        /// <param name="parameters">The numbers of the disputes now (<c>disputeDefaults</c> of <c>params.json</c>); read at every use.</param>
        public DisputeCore(Func<HistSaveData> data, IHistEconomy economy, IHistModifiers modifiers, IDelayedEventFirer firer, Func<IModRng> rng, Func<DisputeParams> parameters,
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
            _params = parameters ?? (() => new DisputeParams());
            _money = money ?? (amount => amount.ToString("0"));
            _log = log;
        }

        /// <summary>The disputes in progress (a view; do not change).</summary>
        public IReadOnlyList<DisputeDto> Disputes
        {
            get { return _data().disputes; }
        }

        public DisputeDto Find(string disputeId)
        {
            if (string.IsNullOrEmpty(disputeId)) return null;
            foreach (DisputeDto d in _data().disputes)
            {
                if (d != null && string.Equals(d.disputeId, disputeId, StringComparison.Ordinal)) return d;
            }
            return null;
        }

        /// <summary>
        /// The dispute begins: the defendant has not answered yet. Returns the dispute that is there (a second opening of the same id changes nothing), or null when a
        /// company is not in the game or the mode is unknown.
        /// </summary>
        public DisputeDto Open(string disputeId, string mode, string plaintiffKey, string defendantKey)
        {
            if (string.IsNullOrEmpty(disputeId) || Array.IndexOf(DisputeModes.All, mode) < 0) return null;
            if (!CompanyKey.IsValid(plaintiffKey) || !CompanyKey.IsValid(defendantKey) || string.Equals(plaintiffKey, defendantKey, StringComparison.Ordinal)) return null;
            if (!_economy.IsAlive(plaintiffKey) || !_economy.IsAlive(defendantKey)) return null;

            DisputeDto existing = Find(disputeId);
            if (existing != null) return existing;

            var dispute = new DisputeDto
            {
                disputeId = disputeId,
                mode = mode,
                plaintiffKey = plaintiffKey,
                defendantKey = defendantKey,
                choice = string.Empty,
                resolveDay = _economy.Today + AwaitingDays,
            };
            _data().disputes.Add(dispute);
            return dispute;
        }

        /// <summary>
        /// The defendant answers. The dispute is opened first when the event's common effect has not done it. False when the action is not an answer in the dispute's mode,
        /// the dispute already has an answer, or a company is gone.
        /// </summary>
        public bool Choose(string disputeId, string mode, string plaintiffKey, string defendantKey, string action)
        {
            DisputeDto dispute = Open(disputeId, mode, plaintiffKey, defendantKey);
            if (dispute == null) return false;
            if (dispute.choice.Length > 0 || !DisputeActions.IsAnswerIn(dispute.mode, action))
            {
                Log("dispute " + disputeId + ": the answer " + action + " does not fit (mode " + dispute.mode + ", answered " + dispute.choice + ")");
                return false;
            }

            DisputeParams p = _params();
            int today = _economy.Today;
            string defendant = dispute.defendantKey;
            dispute.choice = action;

            switch (action)
            {
                case DisputeActions.Arbitrate:
                    dispute.monthlyPaymentPct = p.LegalCostPct;
                    dispute.endDay = dispute.resolveDay = today + HistTime.MonthsToDays(p.ArbitrationMonths);
                    break;

                case DisputeActions.Fight:
                    dispute.monthlyPaymentPct = p.LegalCostPct;
                    dispute.endDay = dispute.resolveDay = today + HistTime.MonthsToDays(p.FightMonths);
                    break;

                case DisputeActions.License:
                    dispute.monthlyPaymentPct = p.LicenseFeePct;
                    dispute.resolveDay = 0;
                    dispute.endDay = today + HistTime.MonthsToDays(p.LicenseMonths);
                    break;

                case DisputeActions.Independent:
                    dispute.resolveDay = 0;
                    dispute.endDay = today + HistTime.MonthsToDays(p.IndependentMonths);
                    AddResearchCost(defendant, p.ResearchCostMultiplier, dispute.endDay - today);
                    break;

                case DisputeActions.Redesign:
                    dispute.resolveDay = 0;
                    dispute.endDay = today + HistTime.MonthsToDays(p.RedesignMonths);
                    AddResearchCost(defendant, p.RedesignMultiplier, dispute.endDay - today);
                    break;

                case DisputeActions.Settle:
                    Pay(dispute, HistMoney.Of(p.SettlePct, _economy.Revenue12m(defendant)));
                    _data().disputes.Remove(dispute);
                    break;
            }
            return true;
        }

        /// <summary>
        /// The plaintiff files the claim: the defendant gets its decision event (the next day, so that it is not made inside the plaintiff's own choice). The dispute itself
        /// begins in that event. False when the companies are not in the game or the event could not be fired.
        /// </summary>
        public bool Sue(string disputeId, string mode, string entryId, string plaintiffKey, string defendantKey)
        {
            if (string.IsNullOrEmpty(disputeId) || Array.IndexOf(DisputeModes.All, mode) < 0) return false;
            if (!_economy.IsAlive(plaintiffKey) || !_economy.IsAlive(defendantKey)) return false;

            FireResult result = _firer.FireLater(DisputeKeys.DecisionEvent(mode), defendantKey, 1,
                entryId ?? string.Empty, _economy.NameOf(defendantKey), _economy.NameOf(plaintiffKey), plaintiffKey, disputeId);
            if (result == FireResult.UnknownEvent || result == FireResult.Disabled)
            {
                Log("dispute " + disputeId + ": the decision event " + DisputeKeys.DecisionEvent(mode) + " could not be fired (" + result + ")");
                return false;
            }
            return true;
        }

        /// <summary>The monthly pass (priority <see cref="HistTickPriority.Disputes"/>): the payments of the answers, the verdicts, the end of the terms.</summary>
        public void OnMonth()
        {
            HistSaveData data = _data();
            int today = _economy.Today;

            foreach (DisputeDto d in new List<DisputeDto>(data.disputes))
            {
                if (d == null) continue;
                try
                {
                    if (!_economy.IsAlive(d.plaintiffKey) || !_economy.IsAlive(d.defendantKey))
                    {
                        data.disputes.Remove(d);
                        continue;
                    }
                    Month(d, today);
                }
                catch (Exception ex)
                {
                    Log("dispute " + d.disputeId + " failed and is dropped: " + ex.GetType().Name + ": " + ex.Message);
                    data.disputes.Remove(d);
                }
            }
        }

        private void Month(DisputeDto d, int today)
        {
            switch (d.choice)
            {
                case "":
                    if (today >= d.resolveDay) _data().disputes.Remove(d);
                    break;

                case DisputeActions.Arbitrate:
                case DisputeActions.Fight:
                    if (today < d.resolveDay)
                    {
                        _economy.Spend(d.defendantKey, HistMoney.Of(d.monthlyPaymentPct, _economy.Revenue12m(d.defendantKey)), HistExpense.Fines);
                        break;
                    }
                    Verdict(d);
                    _data().disputes.Remove(d);
                    break;

                case DisputeActions.License:
                    {
                        float fee = HistMoney.Of(d.monthlyPaymentPct, _economy.MonthlyRevenue(d.defendantKey));
                        if (fee > 0f)
                        {
                            _economy.Spend(d.defendantKey, fee, HistExpense.Royalties);
                            _economy.Earn(d.plaintiffKey, fee, HistIncome.Royalties);
                        }
                        if (today >= d.endDay) _data().disputes.Remove(d);
                        break;
                    }

                default:
                    if (today >= d.endDay) _data().disputes.Remove(d);
                    break;
            }
        }

        /// <summary>The verdict of an arbitration or a fight: one roll.</summary>
        private void Verdict(DisputeDto d)
        {
            DisputeParams p = _params();
            float roll = _rng().Value();

            if (d.choice == DisputeActions.Arbitrate)
            {
                if (roll < p.PWinArbitration)
                {
                    float payment = HistMoney.Of(p.PlaintiffSettlementPct, _economy.Revenue12m(d.plaintiffKey));
                    if (payment > 0f)
                    {
                        _economy.Spend(d.plaintiffKey, payment, HistExpense.Fines);
                        _economy.Earn(d.defendantKey, payment, HistIncome.Royalties);
                    }
                    _economy.Notify(d.defendantKey, DisputeKeys.NoteWon, _money(payment), _economy.NameOf(d.plaintiffKey));
                }
                else
                {
                    AddResearchCost(d.defendantKey, p.ResearchCostMultiplier, p.LostArbitrationDays);
                    _economy.Notify(d.defendantKey, DisputeKeys.NoteLost, _economy.NameOf(d.plaintiffKey));
                }
                return;
            }

            if (roll < p.PWinInfringement)
            {
                _economy.Notify(d.defendantKey, DisputeKeys.NoteFightWon, _economy.NameOf(d.plaintiffKey));
                return;
            }

            float lost = DisputeMath.LostFightPayment(p, _economy.Revenue12m(d.defendantKey));
            Pay(d, lost);
            _economy.Notify(d.defendantKey, DisputeKeys.NoteLost, _economy.NameOf(d.plaintiffKey));
        }

        /// <summary>A payment from the defendant to the plaintiff (<c>Fines</c> for the payer, <c>Royalties</c> for the receiver).</summary>
        private void Pay(DisputeDto d, float amount)
        {
            if (amount <= 0f) return;
            _economy.Spend(d.defendantKey, amount, HistExpense.Fines);
            _economy.Earn(d.plaintiffKey, amount, HistIncome.Royalties);
        }

        /// <summary>The research cost of a company goes up; not for a company the game already penalizes.</summary>
        private void AddResearchCost(string companyKey, float multiplier, int days)
        {
            if (_economy.IsUnlucky(companyKey))
            {
                Log("the research cost of " + companyKey + " is not raised: the game already penalizes it");
                return;
            }
            _modifiers.Add(ResearchChannel, companyKey, multiplier, days, DisputeKeys.ResearchReasonKey, null);
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
