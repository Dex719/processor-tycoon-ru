using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The "Loans" tab of the rescue menu (FR-6, FR-7, FR-10; design.md "UI"): the table of the loans of the feature with "Pay back early", the startup credit line
    /// (the limit, the drawn part, the free part, the closing date, a slider for the sum, "Take", "Pay back" and the switch "Auto draw"), and the bridge loan (a slider from
    /// 25 to 100 percent of the most the player may take, the terms, "Take"). Every operation asks first (<see cref="ConfirmPlanner"/>, <c>ModUI.Confirm</c>) and goes through
    /// the services; a button that cannot work is inactive and says why.
    /// <para>
    /// A slider of the core shows its plain number, so the sum is picked as a share of the limit (the whole limit is 100) and written beside it, on the button
    /// (<see cref="AmountSteps"/>). The share of the credit line is one for both its buttons.
    /// </para>
    /// </summary>
    internal static partial class RecoveryWindow
    {
        private const float KindWidth = 150f;
        private const float MoneyWidth = 100f;
        private const float RateWidth = 60f;
        private const float MonthsWidth = 70f;
        private const float PrepayWidth = 190f;

        private static int _linePercent = AmountSteps.MaxPercent;
        private static int _bridgePercent = AmountSteps.MaxPercent;

        private static ButtonUI _drawButton;
        private static ButtonUI _repayButton;
        private static ButtonUI _bridgeButton;
        private static TMP_Text _lineReasons;
        private static TMP_Text _bridgeTerms;

        // The widgets of a tab die with its rebuild; the references must not outlive them.
        private static void ResetLoansTab()
        {
            _drawButton = null;
            _repayButton = null;
            _bridgeButton = null;
            _lineReasons = null;
            _bridgeTerms = null;
        }

        private static void BuildLoans(BankruptcyRecoveryFeature feature)
        {
            BuildLoanTable(feature);
            BuildCreditLine(feature);
            BuildBridge(feature);
        }

        // ---- the loans ----

        private static void BuildLoanTable(BankruptcyRecoveryFeature feature)
        {
            RecoveryUi.Heading(_body, L("rec.loans.title"));

            IReadOnlyList<LoanView> loans = feature.Loans.Views();
            if (loans.Count == 0)
            {
                RecoveryUi.Line(_body, L("rec.loans.none"), MenuTone.Muted);
            }
            else
            {
                RectTransform head = RecoveryUi.Row(_body, 22f, 6f);
                LoanCell(head, "rec.loans.col.kind", KindWidth, 1f, TextAlignmentOptions.MidlineLeft);
                LoanCell(head, "rec.loans.col.outstanding", MoneyWidth, 0f, TextAlignmentOptions.MidlineRight);
                LoanCell(head, "rec.loans.col.rate", RateWidth, 0f, TextAlignmentOptions.MidlineRight);
                LoanCell(head, "rec.loans.col.payment", MoneyWidth, 0f, TextAlignmentOptions.MidlineRight);
                LoanCell(head, "rec.loans.col.months", MonthsWidth, 0f, TextAlignmentOptions.MidlineRight);
                LoanCell(head, "rec.loans.col.action", PrepayWidth, 0f, TextAlignmentOptions.MidlineRight);

                foreach (LoanView loan in loans) AddLoanRow(feature, loan);
            }

            InvestorShareData investor = feature.State.investorShare;
            if (investor.active)
            {
                RecoveryUi.Line(_body, L("rec.loans.investor", RecoveryViews.Percent(investor.share), RecoveryUi.Num(investor.monthsLeft),
                    RecoveryViews.Money(investor.paid), RecoveryViews.Money(investor.cap)));
            }
        }

        private static void LoanCell(Transform row, string titleKey, float width, float flexible, TextAlignmentOptions align)
        {
            RecoveryUi.Cell(row, RecoveryUi.Paint(L(titleKey), MenuTone.Muted), width, flexible, align);
        }

        private static void AddLoanRow(BankruptcyRecoveryFeature feature, LoanView loan)
        {
            double effective = LoanMath.EffectiveRate(loan.AnnualRate, feature.Finance.InterestMultiplier);
            string kindName = RecoveryViews.KindName(loan.Kind.ToString());

            RectTransform row = RecoveryUi.Row(_body, 34f, 6f);
            RecoveryUi.Cell(row, kindName, KindWidth, 1f, TextAlignmentOptions.MidlineLeft);
            RecoveryUi.Cell(row, RecoveryViews.Money(loan.Outstanding), MoneyWidth, 0f, TextAlignmentOptions.MidlineRight);
            RecoveryUi.Cell(row, L("rec.loans.rate", RecoveryViews.Percent(effective)), RateWidth, 0f, TextAlignmentOptions.MidlineRight);
            RecoveryUi.Cell(row, RecoveryViews.Money(loan.Payment), MoneyWidth, 0f, TextAlignmentOptions.MidlineRight);
            RecoveryUi.Cell(row, RecoveryUi.Num(loan.MonthsLeft), MonthsWidth, 0f, TextAlignmentOptions.MidlineRight);

            // FR-10 item 3: the balance has to cover the repayment
            double balance = feature.Finance.Money;
            string reason = balance >= loan.Outstanding ? null : L("rec.loans.prepay.noMoney", RecoveryViews.Money(balance), RecoveryViews.Money(loan.Outstanding));
            string id = loan.Id;
            ButtonUI button = RecoveryUi.Button(row, L("rec.loans.prepay"), () => OnPrepay(id), reason, ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(button, PrepayWidth, 0f);
        }

        private static void OnPrepay(string loanId)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            LoanView loan;
            if (!FindLoan(feature, loanId, out loan)) return;
            double amount = loan.Outstanding;
            if (!(amount > 0.0) || feature.Finance.Money < amount) return;

            Confirmation plan = Planner(feature).Prepay(RecoveryViews.KindName(loan.Kind.ToString()), amount, 0.0);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                if (now != null && now.Loans.Prepay(loanId, amount) != PrepayResult.Done) ModNotify.ShowKey(Feature, "rec.notify.failed");
                Refresh();   // the notification "loan paid off" is the service's
            });
        }

        private static bool FindLoan(BankruptcyRecoveryFeature feature, string loanId, out LoanView found)
        {
            foreach (LoanView loan in feature.Loans.Views())
            {
                if (string.Equals(loan.Id, loanId, StringComparison.Ordinal))
                {
                    found = loan;
                    return true;
                }
            }
            found = default(LoanView);
            return false;
        }

        // ---- the credit line ----

        private static void BuildCreditLine(BankruptcyRecoveryFeature feature)
        {
            RecoveryUi.Heading(_body, L("rec.credit.title"));

            CreditLineView line = feature.CreditLine.View();
            if (!line.Open)
            {
                RecoveryUi.Line(_body, L(ClosedLineKey(feature)), MenuTone.Muted);
                return;
            }

            DateTime? closes = DateOf(line.ClosesOn);
            string until = closes.HasValue ? RecoveryViews.GameDate(closes.Value) : "-";
            RecoveryUi.Line(_body, L("rec.credit.state", RecoveryViews.Money(line.Limit), RecoveryViews.Money(line.Drawn), RecoveryViews.Money(line.Available), until));

            double rate = LoanMath.EffectiveRate(feature.Balance.startupCredit.annualRate, feature.Finance.InterestMultiplier);
            RecoveryUi.Line(_body, L("rec.credit.rate", RecoveryViews.Percent(rate)), MenuTone.Muted);

            _linePercent = AmountSteps.ClampPercent(_linePercent, AmountSteps.LineMinPercent);
            ModUI.Slider(_body, L("rec.credit.share"), AmountSteps.LineMinPercent, AmountSteps.MaxPercent, AmountSteps.PercentStep, _linePercent, OnLineShare);

            RectTransform row = RecoveryUi.Row(_body, 34f, 6f);
            _drawButton = RecoveryUi.Button(row, string.Empty, OnDraw, null, ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(_drawButton, 300f, 1f);
            _repayButton = RecoveryUi.Button(row, string.Empty, OnRepay, null, ButtonStyle.Standard, false);
            RecoveryUi.SetWidth(_repayButton, 300f, 1f);
            _lineReasons = ModUI.Text(_body, string.Empty);

            ModUI.Toggle(_body, L("rec.credit.autodraw"), line.AutoDraw, OnAutoDraw, L("rec.credit.autodraw.tooltip"));
            UpdateLineControls(feature);
        }

        // Why there is no line to use: it ran out of time, was closed by the switch-off, was never opened or the part is off.
        private static string ClosedLineKey(BankruptcyRecoveryFeature feature)
        {
            CreditLineData data = feature.State.creditLine;
            if (data.state == CreditLineData.StateClosed)
                return data.closedReason == CreditLineData.ReasonDisabled ? "rec.credit.closed.disabled" : "rec.credit.closed.expired";
            return feature.CreditLine.Engine.DrawBlock() ?? CreditLineEngine.ReasonNoLine;
        }

        private static void OnLineShare(float value)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;
            _linePercent = AmountSteps.ClampPercent((int)Math.Round(value), AmountSteps.LineMinPercent);
            UpdateLineControls(feature);
        }

        private static void OnAutoDraw(bool on)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature != null) feature.CreditLine.Engine.SetAutoDraw(on);
        }

        // The sums under the share: what "Take" would draw and what "Pay back" would pay, the captions, and the reasons for what cannot be done.
        private static void UpdateLineControls(BankruptcyRecoveryFeature feature)
        {
            CreditLineEngine engine = feature.CreditLine.Engine;
            string drawReason = DrawReason(feature);
            double drawAmount = AmountSteps.OfMax(engine.Available(), _linePercent);
            if (drawReason == null && !(drawAmount > 0.0)) drawReason = L(CreditLineEngine.ReasonFull);

            double drawn = engine.Drawn;
            double repayAmount = AmountSteps.OfMax(AmountSteps.RepayLimit(drawn, feature.Finance.Money), _linePercent);
            string repayReason = null;
            if (!(drawn > 0.0)) repayReason = L("rec.credit.repay.nothing");
            else if (!(repayAmount > 0.0)) repayReason = L("rec.credit.repay.noMoney");

            RecoveryUi.SetCaption(_drawButton, L("rec.credit.draw", RecoveryViews.Money(drawAmount)));
            RecoveryUi.SetEnabled(_drawButton, drawReason);
            RecoveryUi.SetCaption(_repayButton, L("rec.credit.repay", RecoveryViews.Money(repayAmount)));
            RecoveryUi.SetEnabled(_repayButton, repayReason);

            if (_lineReasons != null)
            {
                string text = string.Empty;
                if (drawReason != null) text = L("rec.credit.reason.draw", drawReason);
                if (repayReason != null) text += (text.Length > 0 ? "\n" : string.Empty) + L("rec.credit.reason.repay", repayReason);
                _lineReasons.gameObject.SetActive(text.Length > 0);
                _lineReasons.text = RecoveryUi.Paint(text, MenuTone.Muted);
            }
        }

        private static void OnDraw()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            CreditLineEngine engine = feature.CreditLine.Engine;
            double amount = AmountSteps.OfMax(engine.Available(), _linePercent);
            if (engine.DrawBlock() != null || !(amount > 0.0)) return;

            double rate = LoanMath.EffectiveRate(feature.Balance.startupCredit.annualRate, feature.Finance.InterestMultiplier);
            Confirmation plan = Planner(feature).Draw(amount, rate);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                double drawn = now == null ? 0.0 : now.CreditLine.Draw(amount);
                if (drawn > 0.0) ModNotify.ShowKey(Feature, "rec.notify.drawn", RecoveryViews.Money(drawn));
                else ModNotify.ShowKey(Feature, "rec.notify.failed");
                Refresh();
            });
        }

        private static void OnRepay()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;

            CreditLineEngine engine = feature.CreditLine.Engine;
            double amount = AmountSteps.OfMax(AmountSteps.RepayLimit(engine.Drawn, feature.Finance.Money), _linePercent);
            if (!(amount > 0.0)) return;

            Confirmation plan = Planner(feature).RepayLine(amount);
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                double paid = now == null ? 0.0 : now.CreditLine.Repay(amount);
                if (paid > 0.0) ModNotify.ShowKey(Feature, "rec.notify.repaid", RecoveryViews.Money(paid));
                else ModNotify.ShowKey(Feature, "rec.notify.failed");
                Refresh();
            });
        }

        // ---- the bridge loan ----

        private static void BuildBridge(BankruptcyRecoveryFeature feature)
        {
            RecoveryUi.Heading(_body, L("rec.bridge.title"));

            string reason = BridgeReason(feature);
            if (reason != null)
            {
                // AC-2.3: the button is there, inactive, with the reason as its tooltip and under it
                RecoveryUi.Button(_body, L("rec.bridge.take", RecoveryViews.Money(feature.BridgeLoans.MaxAmount())), OnTakeBridge, reason);
                return;
            }

            _bridgePercent = AmountSteps.ClampPercent(_bridgePercent, AmountSteps.BridgeMinPercent);
            ModUI.Slider(_body, L("rec.bridge.share"), AmountSteps.BridgeMinPercent, AmountSteps.MaxPercent, AmountSteps.PercentStep, _bridgePercent, OnBridgeShare);
            _bridgeTerms = ModUI.Text(_body, string.Empty);
            _bridgeButton = RecoveryUi.Button(_body, string.Empty, OnTakeBridge, null, ButtonStyle.Standard, false);
            UpdateBridge(feature);
        }

        private static void OnBridgeShare(float value)
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null) return;
            _bridgePercent = AmountSteps.ClampPercent((int)Math.Round(value), AmountSteps.BridgeMinPercent);
            UpdateBridge(feature);
        }

        // The terms of the loan for the picked share: the sum, the months, the rate, the payment, the fee and what stays in hand.
        private static void UpdateBridge(BankruptcyRecoveryFeature feature)
        {
            if (_bridgeButton == null) return;

            BridgeLoanService bridge = feature.BridgeLoans;
            double amount = AmountSteps.OfMax(bridge.MaxAmount(), _bridgePercent);
            double fee = bridge.Fee(amount);

            if (_bridgeTerms != null)
            {
                _bridgeTerms.text = L("rec.bridge.terms", RecoveryViews.Money(amount), RecoveryUi.Num(feature.Balance.bridgeLoan.months),
                                      RecoveryViews.Percent(bridge.EffectiveRate()), RecoveryViews.Money(bridge.Payment(amount)))
                                    + "\n" + L("rec.bridge.fee", RecoveryViews.Money(fee), RecoveryViews.Money(amount - fee));
            }
            RecoveryUi.SetCaption(_bridgeButton, L("rec.bridge.take", RecoveryViews.Money(amount)));
            RecoveryUi.SetEnabled(_bridgeButton, amount > 0.0 ? null : L("rec.bridge.unavailable.noLimit"));
        }

        private static void OnTakeBridge()
        {
            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            if (feature == null || BridgeReason(feature) != null) return;

            BridgeLoanService bridge = feature.BridgeLoans;
            double amount = AmountSteps.OfMax(bridge.MaxAmount(), _bridgePercent);
            if (!(amount > 0.0)) return;

            Confirmation plan = Planner(feature).Bridge(amount, bridge.Fee(amount), feature.Balance.bridgeLoan.months, bridge.EffectiveRate(), bridge.Payment(amount));
            Ask(plan, () =>
            {
                BankruptcyRecoveryFeature now = BankruptcyRecoveryFeature.Instance;
                BridgeTakeResult result = now == null ? BridgeTakeResult.Unavailable : now.BridgeLoans.Take(amount);
                if (result == BridgeTakeResult.Done) ModNotify.ShowKey(Feature, "rec.notify.bridgeTaken", RecoveryViews.Money(amount));
                else ModNotify.ShowKey(Feature, "rec.notify.failed");
                Refresh();
            });
        }
    }
}
