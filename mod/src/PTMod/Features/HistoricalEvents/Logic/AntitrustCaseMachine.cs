using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The three kinds of antitrust case (<c>mode</c> of the effect <c>AntitrustCase</c>, design.md formulas 3 and 4).</summary>
    public static class CaseModes
    {
        /// <summary>A fine, with the choice to pay, to pay into a reserve or to appeal.</summary>
        public const string Fine = "fine";

        /// <summary>A consent decree: settle (a behavioural remedy) or fight in court.</summary>
        public const string Consent = "consent";

        /// <summary>A warning: a behavioural remedy for the target and a benefit for the beneficiary, no choice.</summary>
        public const string Warning = "warning";

        public static readonly string[] All = { Fine, Consent, Warning };
    }

    /// <summary>The actions of the effect <c>AntitrustCase</c> (design.md, Component 8). <c>open</c> belongs to the event itself, the others to its choices.</summary>
    public static class CaseActions
    {
        public const string Open = "open";
        public const string Pay = "pay";
        public const string Provision = "provision";
        public const string Appeal = "appeal";
        public const string Settle = "settle";
        public const string Fight = "fight";
        public const string ReimposePay = "reimposePay";
        public const string ReimposeAppeal = "reimposeAppeal";

        public static readonly string[] All = { Open, Pay, Provision, Appeal, Settle, Fight, ReimposePay, ReimposeAppeal };
    }

    /// <summary>
    /// The stage of a case as the save keeps it (<c>CaseDto.stage</c>). The numbers are written to the save and do not change. A stage outside this set
    /// is treated as <see cref="Closed"/>.
    /// </summary>
    public static class CaseStages
    {
        /// <summary>The fine was announced; its target has not chosen yet.</summary>
        public const int Awaiting = 0;

        /// <summary>The fine was paid, in full or after a ruling: nothing is left to decide. The company was found liable.</summary>
        public const int Paid = 1;

        /// <summary>Appealed: a monthly fee for the bank guarantee, the ruling on <c>rulingDay</c>.</summary>
        public const int Appeal = 2;

        /// <summary>Paid into a reserve: refunded in whole or in part if the court annuls or reduces the fine, the ruling on <c>rulingDay</c>.</summary>
        public const int Provision = 3;

        /// <summary>The fine was annulled; on <c>reimposeDay</c> the regulator may impose a new one.</summary>
        public const int Reimposition = 4;

        /// <summary>A second, smaller fine was announced; its target has not chosen yet.</summary>
        public const int SecondAwaiting = 5;

        /// <summary>The second fine was appealed; the ruling on <c>rulingDay</c>.</summary>
        public const int SecondAppeal = 6;

        /// <summary>A consent decree was offered; its target has not chosen yet.</summary>
        public const int ConsentAwaiting = 7;

        /// <summary>The target fights the decree in court: monthly legal costs, the ruling on <c>rulingDay</c>.</summary>
        public const int ConsentFight = 8;

        /// <summary>The case ended without liability (annulled for good, won, settled, a warning) or was closed by the bankruptcy of its target.</summary>
        public const int Closed = 9;

        /// <summary>The case is not over: something can still be decided, ruled or imposed. It is what the acquisition surcharge and the chronicle call "active".</summary>
        public static bool IsOpen(int stage)
        {
            switch (stage)
            {
                case Awaiting:
                case Appeal:
                case Provision:
                case Reimposition:
                case SecondAwaiting:
                case SecondAppeal:
                case ConsentAwaiting:
                case ConsentFight:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The target has to choose (the event waits for the player or the AI; the timer of the event applies its default choice).</summary>
        public static bool IsAwaitingChoice(int stage)
        {
            return stage == Awaiting || stage == SecondAwaiting || stage == ConsentAwaiting;
        }

        /// <summary>The case is known to the calendar's condition <c>hist.CaseActive</c>: it has not ended without liability.</summary>
        public static bool CountsAsActive(int stage)
        {
            return stage != Closed && stage >= Awaiting && stage <= ConsentFight;
        }

        /// <summary>The action the default choice of the event takes for a case that waits for its target (the timer ran out and nobody applied it).</summary>
        public static string DefaultAction(int stage)
        {
            switch (stage)
            {
                case Awaiting: return CaseActions.Appeal;
                case SecondAwaiting: return CaseActions.ReimposeAppeal;
                case ConsentAwaiting: return CaseActions.Settle;
                default: return null;
            }
        }
    }

    /// <summary>The keys of the reputation changes and of the notes that a case makes (all in the string tables of the feature).</summary>
    public static class CaseKeys
    {
        public const string ReputationFine = "hist.rep.antitrust.fine";
        public const string ReputationPaid = "hist.rep.antitrust.paid";
        public const string ReputationConsent = "hist.rep.antitrust.consent";
        public const string ReputationCleared = "hist.rep.antitrust.cleared";
        public const string ReputationWarning = "hist.rep.antitrust.warning";

        public const string NoteAnnulled = "hist.note.case.annulled";
        public const string NoteAnnulledRefund = "hist.note.case.annulledRefund";
        public const string NoteReduced = "hist.note.case.reduced";
        public const string NoteUpheld = "hist.note.case.upheld";
        public const string NoteSecondReduced = "hist.note.case.secondReduced";
        public const string NoteSecondUpheld = "hist.note.case.secondUpheld";
        public const string NoteConsentWon = "hist.note.case.consentWon";
        public const string NoteConsentLost = "hist.note.case.consentLost";
    }

    /// <summary>
    /// The numbers of a case (the members of the effect <c>AntitrustCase</c>, design.md Component 8, formulas 3 and 4). Every member has the default of
    /// the design example, so an event states only what differs. The ranges are those of the validator: a number outside them is a problem and the event
    /// is rejected when the content is loaded.
    /// </summary>
    public sealed class CaseParams
    {
        /// <summary>How much more likely the annulment is when the target has lost its dominance (design.md formula 3, FR-5 item 3).</summary>
        public const float DominanceBonus = 0.10f;

        private static readonly string[] Members =
        {
            "action", "mode", "finePct", "remedyChannel", "remedyMultiplier", "remedyDays", "appealMonths", "pAnnul", "pReduce", "reduceTo",
            "interestPerYear", "guaranteeFeePerYear", "dominanceLostShare", "reimpose", "reimposeDelayMonths", "reimposeP", "reimposeShare",
            "secondAppealMonths", "secondPReduce", "secondReduceTo", "legalCostPct", "legalMonths", "winP", "lossFinePct",
            "beneficiaryChannel", "beneficiaryMultiplier", "beneficiaryDays",
        };

        public string Mode = CaseModes.Fine;

        /// <summary>The fine as a share of the revenue of the 12 full months before the ruling (EU 2009: about 4 % of Intel's 2008 revenue).</summary>
        public float FinePct = 0.04f;

        /// <summary>The behavioural remedy: a multiplier of this channel for the target (empty: none).</summary>
        public string RemedyChannel = string.Empty;

        public float RemedyMultiplier = 1f;
        public int RemedyDays;

        /// <summary>The months from the appeal to the ruling of the court.</summary>
        public int AppealMonths = 60;

        public float PAnnul = 0.35f;
        public float PReduce = 0.35f;

        /// <summary>A reduced fine is this share of the original (EU 2024: 237 of 376 million is the second stage's 0.63, the first stage's default is 0.35).</summary>
        public float ReduceTo = 0.35f;

        public float InterestPerYear = 0.04f;
        public float GuaranteeFeePerYear = 0.01f;
        public float DominanceLostShare = 0.40f;

        /// <summary>After an annulment the regulator may impose a new, smaller fine.</summary>
        public bool Reimpose;

        public int ReimposeDelayMonths = 18;
        public float ReimposeP = 0.5f;

        /// <summary>The new fine as a share of the original (EU 2023: 376 of 1060 million).</summary>
        public float ReimposeShare = 0.355f;

        public int SecondAppealMonths = 15;
        public float SecondPReduce = 0.6f;
        public float SecondReduceTo = 0.63f;

        // Consent decree (formula 4): the legal costs of the fight, the chance to win it, the fine when it is lost.
        public float LegalCostPct = 0.003f;
        public int LegalMonths = 9;
        public float WinP = 0.5f;
        public float LossFinePct = 0.01f;

        // Warning (formula 4): the benefit of the beneficiary.
        public string BeneficiaryChannel = string.Empty;
        public float BeneficiaryMultiplier = 1f;
        public int BeneficiaryDays;

        /// <summary>The action named by the effect; <c>open</c> when it names none. Not validated here.</summary>
        public static string ActionOf(JsonObject raw)
        {
            return raw == null ? CaseActions.Open : raw.GetString("action", CaseActions.Open) ?? CaseActions.Open;
        }

        /// <summary>
        /// Reads the numbers of an effect (or of a case saved with its parameters). Absent members keep the defaults; a member of the wrong kind or outside its
        /// range is a line in <paramref name="problems"/> (may be null) and keeps the default. Never throws and never returns null.
        /// </summary>
        public static CaseParams Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "AntitrustCase", problems);
            var p = new CaseParams();

            r.Choice("action", CaseActions.Open, CaseActions.All);
            p.Mode = r.Choice("mode", CaseModes.Fine, CaseModes.All);
            p.FinePct = r.Num("finePct", p.FinePct, 0f, 0.5f);
            p.RemedyChannel = r.Str("remedyChannel", string.Empty);
            p.RemedyMultiplier = r.Num("remedyMultiplier", p.RemedyMultiplier, 0.1f, 3f);
            p.RemedyDays = r.Int("remedyDays", p.RemedyDays, 0, 3650);
            p.AppealMonths = r.Int("appealMonths", p.AppealMonths, 1, 240);
            p.PAnnul = r.Num("pAnnul", p.PAnnul, 0f, 1f);
            p.PReduce = r.Num("pReduce", p.PReduce, 0f, 1f);
            p.ReduceTo = r.Num("reduceTo", p.ReduceTo, 0f, 1f);
            p.InterestPerYear = r.Num("interestPerYear", p.InterestPerYear, 0f, 0.5f);
            p.GuaranteeFeePerYear = r.Num("guaranteeFeePerYear", p.GuaranteeFeePerYear, 0f, 0.2f);
            p.DominanceLostShare = r.Num("dominanceLostShare", p.DominanceLostShare, 0f, 1f);
            p.Reimpose = r.Bool("reimpose", p.Reimpose);
            p.ReimposeDelayMonths = r.Int("reimposeDelayMonths", p.ReimposeDelayMonths, 1, 240);
            p.ReimposeP = r.Num("reimposeP", p.ReimposeP, 0f, 1f);
            p.ReimposeShare = r.Num("reimposeShare", p.ReimposeShare, 0f, 1f);
            p.SecondAppealMonths = r.Int("secondAppealMonths", p.SecondAppealMonths, 1, 240);
            p.SecondPReduce = r.Num("secondPReduce", p.SecondPReduce, 0f, 1f);
            p.SecondReduceTo = r.Num("secondReduceTo", p.SecondReduceTo, 0f, 1f);
            p.LegalCostPct = r.Num("legalCostPct", p.LegalCostPct, 0f, 0.05f);
            p.LegalMonths = r.Int("legalMonths", p.LegalMonths, 1, 120);
            p.WinP = r.Num("winP", p.WinP, 0f, 1f);
            p.LossFinePct = r.Num("lossFinePct", p.LossFinePct, 0f, 0.5f);
            p.BeneficiaryChannel = r.Str("beneficiaryChannel", string.Empty);
            p.BeneficiaryMultiplier = r.Num("beneficiaryMultiplier", p.BeneficiaryMultiplier, 0.1f, 3f);
            p.BeneficiaryDays = r.Int("beneficiaryDays", p.BeneficiaryDays, 0, 3650);

            if (p.PAnnul + p.PReduce > 1f + 1e-6f) r.Fail("pAnnul", "pAnnul + pReduce is above 1");
            if (!string.IsNullOrEmpty(p.RemedyChannel) && p.RemedyDays < 1) r.Fail("remedyDays", "a remedy needs remedyDays of at least 1");
            if (!string.IsNullOrEmpty(p.BeneficiaryChannel) && p.BeneficiaryDays < 1) r.Fail("beneficiaryDays", "a benefit needs beneficiaryDays of at least 1");

            r.WarnUnknown();
            return p;
        }

        /// <summary>The member names this class reads (for the tests that compare the content with the code).</summary>
        public static IReadOnlyList<string> MemberNames
        {
            get { return Members; }
        }
    }

    /// <summary>One thing a step of the case machine asks the host to do.</summary>
    public enum CaseOpKind
    {
        Spend,
        Earn,
        Reputation,

        /// <summary>A behavioural remedy or a benefit: an entry of the channel for the company.</summary>
        Remedy,

        /// <summary>Fire the event <see cref="CaseOp.Key"/> for the company with <see cref="CaseOp.Args"/>.</summary>
        Fire,

        /// <summary>A note for the player (the ruling): the key and, when <see cref="CaseOp.Amount"/> is above 0, the amount as its first argument.</summary>
        Notify,
    }

    /// <summary>One request of a step: who, what and how much.</summary>
    public sealed class CaseOp
    {
        public CaseOpKind Kind;
        public string CompanyKey;
        public float Amount;
        public HistExpense Expense;
        public HistIncome Income;

        /// <summary>The reason of a reputation change, the id of the event to fire or the key of a note.</summary>
        public string Key;

        public string Channel;
        public float Multiplier;
        public int Days;
        public string[] Args;

        public override string ToString()
        {
            return Kind + " " + CompanyKey + " " + Amount + " " + Key;
        }
    }

    /// <summary>The result of a step of the machine: the case after it, whether the action fitted, and the requests to carry out in order.</summary>
    public sealed class CaseStep
    {
        public CaseStep(CaseDto c)
        {
            Case = c;
            Accepted = true;
        }

        /// <summary>The case after the step (the same object that was passed in, changed in place).</summary>
        public CaseDto Case { get; private set; }

        /// <summary>False when the action does not fit the stage of the case (nothing was changed).</summary>
        public bool Accepted { get; set; }

        public List<CaseOp> Ops { get; } = new List<CaseOp>();

        /// <summary>The money the target has paid in this step, net of refunds (what the books of the target lose).</summary>
        public float NetCost
        {
            get
            {
                float net = 0f;
                foreach (CaseOp op in Ops)
                {
                    if (op.Kind == CaseOpKind.Spend) net += op.Amount;
                    else if (op.Kind == CaseOpKind.Earn) net -= op.Amount;
                }
                return net;
            }
        }

        public CaseStep Spend(string company, float amount, HistExpense origin)
        {
            float value = HistMoney.Positive(amount);
            if (value > 0f) Ops.Add(new CaseOp { Kind = CaseOpKind.Spend, CompanyKey = company, Amount = value, Expense = origin });
            return this;
        }

        public CaseStep Earn(string company, float amount, HistIncome origin)
        {
            float value = HistMoney.Positive(amount);
            if (value > 0f) Ops.Add(new CaseOp { Kind = CaseOpKind.Earn, CompanyKey = company, Amount = value, Income = origin });
            return this;
        }

        public CaseStep Reputation(string company, float delta, string reasonKey)
        {
            Ops.Add(new CaseOp { Kind = CaseOpKind.Reputation, CompanyKey = company, Amount = delta, Key = reasonKey });
            return this;
        }

        public CaseStep Remedy(string company, string channel, float multiplier, int days)
        {
            if (!string.IsNullOrEmpty(channel) && days > 0 && Math.Abs(multiplier - 1f) > 1e-6f)
                Ops.Add(new CaseOp { Kind = CaseOpKind.Remedy, CompanyKey = company, Channel = channel, Multiplier = multiplier, Days = days });
            return this;
        }

        public CaseStep Fire(string company, string eventId, params string[] args)
        {
            Ops.Add(new CaseOp { Kind = CaseOpKind.Fire, CompanyKey = company, Key = eventId, Args = args });
            return this;
        }

        public CaseStep Notify(string company, string key, float amount)
        {
            Ops.Add(new CaseOp { Kind = CaseOpKind.Notify, CompanyKey = company, Key = key, Amount = amount });
            return this;
        }
    }

    /// <summary>
    /// What an action of the effect costs and does, worked out once when the event is resolved (D1 of the event system): the same numbers are shown under
    /// the buttons and applied. The amounts are positive; the handler stores the up-front cost as a negative <c>value</c> of the applied effect.
    /// </summary>
    public sealed class CaseQuote
    {
        public string Action;

        /// <summary>The key of the line of the effect (<c>hist.fx.case.*</c>).</summary>
        public string TextKey;

        /// <summary>The fine in question (the announced one, or the second one for the reimposition choices).</summary>
        public float Fine;

        /// <summary>The money paid when the action is applied; for a fight the whole of the legal costs, which are paid over the months.</summary>
        public float Upfront;

        /// <summary>The guarantee fee of an appeal per year.</summary>
        public float AnnualFee;

        /// <summary>The monthly legal costs of a fight.</summary>
        public float LegalPerMonth;

        /// <summary>The months to the ruling (or of the legal costs).</summary>
        public int Months;
    }

    /// <summary>
    /// The rules of an antitrust case without the game (design.md, Component 8 and formulas 3 and 4; FR-5): the fine, the three ways to meet it, the ruling of the
    /// court, the new fine, the consent decree and the warning. Every method works on a <see cref="CaseDto"/> (the saved state) and returns a
    /// <see cref="CaseStep"/>: the requests to carry out (money, reputation, remedies, an event). Nothing here reads the clock or a generator: the day and the
    /// roll are passed in, which is why the tests can check the amounts of the design and the expected loss over a hundred thousand rolls.
    /// <list type="bullet">
    /// <item><b>Fine</b> <c>F = finePct × revenue of 12 months</c>. Pay: <c>F</c> now, reputation −5 and +2. Reserve: <c>F</c> now, refunded if the court annuls or reduces it.
    /// Appeal: nothing now, <c>F × guaranteeFeePerYear / 12</c> a month (<see cref="HistExpense.Interest"/>), the ruling after <c>appealMonths</c>.</item>
    /// <item><b>Ruling</b> with <c>pA = pAnnul (+0.10 if the target lost its dominance)</c>, <c>pReduce</c>, the rest upheld with interest for the years that passed.</item>
    /// <item><b>New fine</b> after an annulment, with <c>reimposeP</c>, of <c>F × reimposeShare</c>; pay it or appeal (one more ruling, <c>secondPReduce</c>).</item>
    /// </list>
    /// The expected loss of an appealing target is about 0.58 F (design.md); the tests check it.
    /// </summary>
    public static class AntitrustCaseMachine
    {
        /// <summary>The days a case may wait for its target to choose before the service applies the default choice for it.</summary>
        public const int AwaitingDays = 180;

        /// <summary>The fine for the revenue of the 12 full months: <c>finePct × revenue</c>, 0 for a bad number.</summary>
        public static float Fine(CaseParams p, float revenue12m)
        {
            return HistMoney.Of(p.FinePct, revenue12m);
        }

        /// <summary>What an action costs and shows (the numbers under the buttons). <paramref name="fine"/> is the fine the action is about.</summary>
        public static CaseQuote Quote(string action, CaseParams p, float fine, float revenue12m)
        {
            float f = HistMoney.Positive(fine);
            var q = new CaseQuote { Action = action, Fine = f };
            switch (action)
            {
                case CaseActions.Open:
                    q.TextKey = p.Mode == CaseModes.Consent ? "hist.fx.case.consent" : p.Mode == CaseModes.Warning ? "hist.fx.case.warning" : "hist.fx.case.fine";
                    break;
                case CaseActions.Pay:
                    q.Upfront = f;
                    q.TextKey = "hist.fx.case.pay";
                    break;
                case CaseActions.Provision:
                    q.Upfront = f;
                    q.TextKey = "hist.fx.case.provision";
                    break;
                case CaseActions.Appeal:
                    q.AnnualFee = f * p.GuaranteeFeePerYear;
                    q.Months = p.AppealMonths;
                    q.TextKey = "hist.fx.case.appeal";
                    break;
                case CaseActions.Settle:
                    q.TextKey = "hist.fx.case.settle";
                    break;
                case CaseActions.Fight:
                    q.LegalPerMonth = HistMoney.Of(p.LegalCostPct, revenue12m);
                    q.Months = p.LegalMonths;
                    q.Upfront = q.LegalPerMonth * p.LegalMonths;
                    q.TextKey = "hist.fx.case.fight";
                    break;
                case CaseActions.ReimposePay:
                    q.Upfront = f;
                    q.TextKey = "hist.fx.case.reimposePay";
                    break;
                case CaseActions.ReimposeAppeal:
                    q.Months = p.SecondAppealMonths;
                    q.TextKey = "hist.fx.case.reimposeAppeal";
                    break;
                default:
                    q.TextKey = "hist.fx.case.fine";
                    break;
            }
            return q;
        }

        /// <summary>
        /// Opens a case (the common effect of the event). <paramref name="raw"/> is the <c>params</c> of the effect, kept in the case so that later steps read the
        /// same numbers; <paramref name="entryId"/> is the calendar entry that fired it (the first argument of the event). A fine announces the fine
        /// (reputation −5) and imposes the remedy at once; a consent decree waits for the choice; a warning imposes its remedy and benefit and is over.
        /// </summary>
        public static CaseStep Open(string caseId, string entryId, string targetKey, string beneficiaryKey, CaseParams p, JsonObject raw, float fine, int today)
        {
            JsonObject saved = raw == null ? new JsonObject() : (JsonObject)raw.DeepClone();
            saved.Set("entryId", entryId ?? string.Empty);
            if (!string.IsNullOrEmpty(beneficiaryKey)) saved.Set("beneficiary", beneficiaryKey);

            var c = new CaseDto
            {
                caseId = caseId,
                targetKey = targetKey,
                mode = p.Mode,
                openedDay = today,
                rulingDay = today + AwaitingDays,
                @params = saved,
            };
            var step = new CaseStep(c);

            switch (p.Mode)
            {
                case CaseModes.Consent:
                    c.stage = CaseStages.ConsentAwaiting;
                    break;

                case CaseModes.Warning:
                    c.stage = CaseStages.Closed;
                    step.Remedy(targetKey, p.RemedyChannel, p.RemedyMultiplier, p.RemedyDays);
                    if (!string.IsNullOrEmpty(beneficiaryKey)) step.Remedy(beneficiaryKey, p.BeneficiaryChannel, p.BeneficiaryMultiplier, p.BeneficiaryDays);
                    step.Reputation(targetKey, -2f, CaseKeys.ReputationWarning);
                    break;

                default:
                    c.stage = CaseStages.Awaiting;
                    c.fine = HistMoney.Positive(fine);
                    step.Reputation(targetKey, -5f, CaseKeys.ReputationFine);
                    step.Remedy(targetKey, p.RemedyChannel, p.RemedyMultiplier, p.RemedyDays);
                    break;
            }
            return step;
        }

        /// <summary>
        /// The target's choice. <paramref name="legalPerMonth"/> is the monthly legal cost the player was shown (a fight only). An action that does not fit
        /// the stage is not accepted and changes nothing.
        /// </summary>
        public static CaseStep Choose(CaseDto c, string action, CaseParams p, float legalPerMonth, int today)
        {
            var step = new CaseStep(c);
            string target = c.targetKey;

            switch (action)
            {
                case CaseActions.Pay when c.stage == CaseStages.Awaiting:
                    step.Spend(target, c.fine, HistExpense.Fines);
                    step.Reputation(target, 2f, CaseKeys.ReputationPaid);
                    c.paid += HistMoney.Positive(c.fine);
                    c.stage = CaseStages.Paid;
                    break;

                case CaseActions.Provision when c.stage == CaseStages.Awaiting:
                    step.Spend(target, c.fine, HistExpense.Fines);
                    c.paid += HistMoney.Positive(c.fine);
                    c.provisioned = HistMoney.Positive(c.fine);
                    c.stage = CaseStages.Provision;
                    c.rulingDay = today + HistTime.MonthsToDays(p.AppealMonths);
                    break;

                case CaseActions.Appeal when c.stage == CaseStages.Awaiting:
                    c.guaranteeFeePerMonth = HistMoney.Positive(c.fine * p.GuaranteeFeePerYear / 12f);
                    c.stage = CaseStages.Appeal;
                    c.rulingDay = today + HistTime.MonthsToDays(p.AppealMonths);
                    break;

                case CaseActions.Settle when c.stage == CaseStages.ConsentAwaiting:
                    step.Remedy(target, p.RemedyChannel, p.RemedyMultiplier, p.RemedyDays);
                    step.Reputation(target, -3f, CaseKeys.ReputationConsent);
                    c.stage = CaseStages.Closed;
                    break;

                case CaseActions.Fight when c.stage == CaseStages.ConsentAwaiting:
                    c.guaranteeFeePerMonth = HistMoney.Positive(legalPerMonth);
                    c.stage = CaseStages.ConsentFight;
                    c.rulingDay = today + HistTime.MonthsToDays(p.LegalMonths);
                    break;

                case CaseActions.ReimposePay when c.stage == CaseStages.SecondAwaiting:
                    step.Spend(target, c.fine, HistExpense.Fines);
                    step.Reputation(target, 2f, CaseKeys.ReputationPaid);
                    c.paid += HistMoney.Positive(c.fine);
                    c.stage = CaseStages.Paid;
                    break;

                case CaseActions.ReimposeAppeal when c.stage == CaseStages.SecondAwaiting:
                    c.guaranteeFeePerMonth = 0f;
                    c.stage = CaseStages.SecondAppeal;
                    c.rulingDay = today + HistTime.MonthsToDays(p.SecondAppealMonths);
                    break;

                default:
                    step.Accepted = false;
                    break;
            }
            return step;
        }

        /// <summary>
        /// The running costs of one month: the guarantee fee of an appeal (<see cref="HistExpense.Interest"/>) or the legal costs of a fight
        /// (<see cref="HistExpense.Fines"/>). Other stages cost nothing.
        /// </summary>
        public static CaseStep Monthly(CaseDto c)
        {
            var step = new CaseStep(c);
            if (c.stage == CaseStages.Appeal)
            {
                step.Spend(c.targetKey, c.guaranteeFeePerMonth, HistExpense.Interest);
                c.paid += HistMoney.Positive(c.guaranteeFeePerMonth);
            }
            else if (c.stage == CaseStages.ConsentFight)
            {
                step.Spend(c.targetKey, c.guaranteeFeePerMonth, HistExpense.Fines);
                c.paid += HistMoney.Positive(c.guaranteeFeePerMonth);
            }
            else
            {
                step.Accepted = false;
            }
            return step;
        }

        /// <summary>
        /// The ruling of the court on <c>rulingDay</c>. <paramref name="shareNow"/> is the target's share of the market now (a target that has lost its dominance
        /// is annulled more often), <paramref name="roll"/> a number in [0, 1), <paramref name="revenue12m"/> the target's revenue (the fine of a lost consent fight).
        /// An appeal and a reserve have three outcomes: annulled, reduced to <c>F × reduceTo</c>, upheld with interest for the years that passed.
        /// </summary>
        public static CaseStep Rule(CaseDto c, CaseParams p, float shareNow, float roll, float revenue12m, int today)
        {
            var step = new CaseStep(c);
            string target = c.targetKey;
            float f = HistMoney.Positive(c.fine);

            if (c.stage == CaseStages.Appeal || c.stage == CaseStages.Provision)
            {
                float pAnnul = Math.Min(1f, p.PAnnul + (shareNow < p.DominanceLostShare ? CaseParams.DominanceBonus : 0f));
                float pReduce = Math.Min(p.PReduce, 1f - pAnnul);
                float provisioned = HistMoney.Positive(c.provisioned);
                float years = p.AppealMonths / 12f;

                if (roll < pAnnul)
                {
                    if (provisioned > 0f)
                    {
                        step.Earn(target, provisioned, HistIncome.Interest);
                        c.paid = Math.Max(0f, c.paid - provisioned);
                    }
                    c.provisioned = 0f;
                    step.Notify(target, provisioned > 0f ? CaseKeys.NoteAnnulledRefund : CaseKeys.NoteAnnulled, provisioned);

                    if (p.Reimpose)
                    {
                        c.stage = CaseStages.Reimposition;
                        c.reimposeDay = today + HistTime.MonthsToDays(p.ReimposeDelayMonths);
                    }
                    else
                    {
                        c.stage = CaseStages.Closed;
                    }
                }
                else if (roll < pAnnul + pReduce)
                {
                    float due = f * p.ReduceTo;
                    if (provisioned > 0f)
                    {
                        step.Earn(target, Math.Max(0f, f - due), HistIncome.Interest);
                        c.paid = Math.Max(0f, c.paid - Math.Max(0f, f - due));
                    }
                    else
                    {
                        step.Spend(target, due, HistExpense.Fines);
                        c.paid += due;
                    }
                    c.provisioned = 0f;
                    step.Notify(target, CaseKeys.NoteReduced, due);
                    c.stage = CaseStages.Paid;
                }
                else
                {
                    float due = f * (1f + p.InterestPerYear * years);
                    float pay = provisioned > 0f ? Math.Max(0f, due - f) : due;
                    step.Spend(target, pay, HistExpense.Fines);
                    c.paid += pay;
                    c.provisioned = 0f;
                    step.Notify(target, CaseKeys.NoteUpheld, due);
                    c.stage = CaseStages.Paid;
                }
            }
            else if (c.stage == CaseStages.SecondAppeal)
            {
                float due = roll < p.SecondPReduce ? f * p.SecondReduceTo : f;
                step.Spend(target, due, HistExpense.Fines);
                c.paid += due;
                step.Notify(target, roll < p.SecondPReduce ? CaseKeys.NoteSecondReduced : CaseKeys.NoteSecondUpheld, due);
                c.stage = CaseStages.Paid;
            }
            else if (c.stage == CaseStages.ConsentFight)
            {
                if (roll < p.WinP)
                {
                    step.Reputation(target, 1f, CaseKeys.ReputationCleared);
                    step.Notify(target, CaseKeys.NoteConsentWon, 0f);
                    c.stage = CaseStages.Closed;
                }
                else
                {
                    float due = HistMoney.Of(p.LossFinePct, revenue12m);
                    step.Remedy(target, p.RemedyChannel, p.RemedyMultiplier, p.RemedyDays);
                    step.Spend(target, due, HistExpense.Fines);
                    step.Reputation(target, -5f, CaseKeys.ReputationFine);
                    step.Notify(target, CaseKeys.NoteConsentLost, due);
                    c.paid += due;
                    c.stage = CaseStages.Paid;
                }
            }
            else
            {
                step.Accepted = false;
            }
            return step;
        }

        /// <summary>
        /// The day the regulator may impose a new fine after an annulment: with <c>reimposeP</c> the fine becomes <c>F × reimposeShare</c>, its target chooses
        /// again (the event <see cref="CaseCore.ReimposeEventId"/> is fired for it); otherwise the case is closed.
        /// </summary>
        public static CaseStep Reimpose(CaseDto c, CaseParams p, float roll, string entryId, string targetName, int today)
        {
            var step = new CaseStep(c);
            if (c.stage != CaseStages.Reimposition)
            {
                step.Accepted = false;
                return step;
            }

            if (roll < p.ReimposeP)
            {
                c.fine = HistMoney.Positive(c.fine * p.ReimposeShare);
                c.stage = CaseStages.SecondAwaiting;
                c.rulingDay = today + AwaitingDays;
                step.Reputation(c.targetKey, -5f, CaseKeys.ReputationFine);
                step.Fire(c.targetKey, CaseCore.ReimposeEventId, entryId ?? string.Empty, targetName ?? string.Empty, string.Empty, string.Empty, c.caseId);
            }
            else
            {
                c.stage = CaseStages.Closed;
            }
            return step;
        }

        /// <summary>Closes a case without payments (its target went bankrupt, or the case was broken).</summary>
        public static CaseStep Close(CaseDto c)
        {
            c.stage = CaseStages.Closed;
            c.provisioned = 0f;
            return new CaseStep(c);
        }
    }
}
