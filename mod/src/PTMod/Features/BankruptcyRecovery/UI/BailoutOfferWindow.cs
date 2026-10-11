using System;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The window of the "last chance" offer (design.md, "UI", <c>BailoutOfferWindow</c>; FR-8): a modal window, the game stands still while it is open, that offers the sum
    /// <c>B</c>, the balance after it, and two ways: "Rescue loan" and "Investor share", each with its terms under the heading; and "Decline - declare bankruptcy".
    /// The window is the <see cref="IBailoutPresenter"/> of <see cref="BailoutService"/>: the engine asks it to open (patch P1, or after the batch run) and it reports back.
    /// <list type="bullet">
    /// <item><b>Accepting</b> a way calls <c>BailoutService.Accept</c> (the money, the loan or the contract, the counter, the reputation and the game's own
    /// <c>CancelDefault</c> are the service's), then the window closes.</item>
    /// <item><b>Declining</b> asks once more (the core's <c>ModUI.Confirm</c>: the bankruptcy ends the game), then calls <c>Decline</c>: the next tick is the game's own and
    /// the bankruptcy follows (AC-8.3).</item>
    /// <item><b>The cross</b> is not a refusal (FR-8, design.md): the window reports <c>Dismissed</c> and the offer comes back on the next tick that holds the counter.</item>
    /// <item>The investor way is inactive while an investor contract runs (a second one is not offered), with the reason under the button.</item>
    /// </list>
    /// A window that cannot be built makes <see cref="Open"/> return false: the engine then lets the bankruptcy go the game's way (NFR-2). Main thread only.
    /// </summary>
    internal sealed class BailoutOfferWindow : IBailoutPresenter
    {
        private const string Feature = RecoveryApi.FeatureId;
        private const string WindowId = Feature + ".offer";

        private static readonly Vector2 WindowSize = new Vector2(620f, 620f);

        private readonly BailoutService _service;
        private ModWindow _window;
        private ModWindow _hooked;
        private bool _decided;

        public BailoutOfferWindow(BailoutService service)
        {
            if (service == null) throw new ArgumentNullException("service");
            _service = service;
        }

        /// <summary>The window is on the screen.</summary>
        public bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>
        /// Shows the offer. True when the window is up; false when it could not be built (no template in the scene, an error): the offer is not made then.
        /// </summary>
        public bool Open(BailoutOffer offer)
        {
            if (offer == null) return false;

            try
            {
                if (!EnsureWindow()) return false;

                _decided = false;
                Build(offer);
                _window.Open();
                return _window.IsOpen;
            }
            catch (Exception e)
            {
                ModLog.Error(Feature, "ui: the window of the last-chance offer could not be opened", e);
                return false;
            }
        }

        /// <summary>Closes the window without a decision (the feature was switched off): it counts as the cross, nothing is refused.</summary>
        public void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        // ---- the window ----

        private bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive) return true;
            _window = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the offer window cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "rec.bailout.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            if (!ReferenceEquals(_hooked, window))
            {
                window.Closed += OnWindowClosed;
                _hooked = window;
            }
            _window = window;
            return true;
        }

        // The window went away without a decision: the cross, the end of the scene, the feature switched off. Not a refusal.
        private void OnWindowClosed()
        {
            if (_decided) return;
            _decided = true;
            _service.Dismissed();
        }

        // ---- the contents ----

        private void Build(BailoutOffer offer)
        {
            RectTransform body = _window.Content;
            RecoveryUi.Clear(body);

            BankruptcyRecoveryFeature feature = BankruptcyRecoveryFeature.Instance;
            double reputation = feature == null ? 0.0 : feature.Balance.bailout.reputationDelta;
            OfferTexts texts = OfferTerms.Describe(GameMenuText.Instance, offer, reputation);

            RecoveryUi.Line(body, ModL10n.L("rec.bailout.intro"));
            RecoveryUi.Heading(body, texts.Amount);
            RecoveryUi.Line(body, texts.BalanceAfter);
            RecoveryUi.Line(body, texts.Used, MenuTone.Muted);

            // the first way: a loan
            RecoveryUi.Heading(body, ModL10n.L("rec.bailout.loan.title"));
            RecoveryUi.Line(body, texts.LoanTerms);
            RecoveryUi.Line(body, texts.LoanPayment, MenuTone.Muted);
            RecoveryUi.Button(body, ModL10n.L("rec.bailout.loan.button"), OnAcceptLoan, null, ButtonStyle.CallToAction);

            // the second way: a share of the profit; closed while an investor contract runs
            RecoveryUi.Heading(body, ModL10n.L("rec.bailout.investor.title"));
            RecoveryUi.Line(body, texts.InvestorTerms);
            RecoveryUi.Button(body, ModL10n.L("rec.bailout.investor.button"), OnAcceptInvestor, texts.InvestorUnavailable, ButtonStyle.CallToAction);

            RecoveryUi.Line(body, texts.Reputation, MenuTone.Warn);
            RecoveryUi.Button(body, ModL10n.L("rec.bailout.decline"), OnDecline, null);
        }

        // ---- the buttons ----

        private void OnAcceptLoan()
        {
            Accept(BailoutKind.RescueLoan);
        }

        private void OnAcceptInvestor()
        {
            Accept(BailoutKind.InvestorShare);
        }

        private void Accept(BailoutKind kind)
        {
            _decided = true;
            bool done = false;
            try
            {
                done = _service.Accept(kind);
            }
            finally
            {
                Close();
            }

            // nothing was done (the offer is gone, the company is not there): the window closes, the offer may come back on the next tick
            if (!done) _service.Dismissed();
        }

        private void OnDecline()
        {
            // the bankruptcy ends the game: one more question, and "No" leaves the offer on the screen
            ModUI.Confirm("rec.bailout.decline.title", "rec.bailout.decline.confirm", ConfirmedDecline, null, "rec.bailout.decline.yes", "rec.bailout.decline.no");
        }

        private void ConfirmedDecline()
        {
            if (!IsOpen) return;   // the window went away while the question was open
            _decided = true;
            _service.Decline();
            Close();
        }
    }
}
