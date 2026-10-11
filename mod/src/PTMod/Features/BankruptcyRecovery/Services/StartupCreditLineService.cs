using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The startup credit line in the game (design.md, <c>StartupCreditLineService</c>; FR-7). The rules are <see cref="CreditLineEngine"/>'s (pure logic with
    /// the tests); this class adds what belongs to the game: the notifications of the auto draw and of the end of the term, and the view of the API.
    /// <list type="bullet">
    /// <item><see cref="EnsureOpened"/>: <c>ModTicks.GameReady</c> (a new game after the fast-forward, a loaded save), when the feature is switched on and when
    /// the switch of the part is turned on.</item>
    /// <item><see cref="OnDay"/>: <c>TickPhase.LateTick</c>, the auto draw, before the game's bankruptcy check on <c>DayPassed</c>.</item>
    /// <item><see cref="OnMonth"/>: <c>TickPhase.LateMonthPassed</c>, the interest, the repayment out of a positive balance, the end of the term.</item>
    /// </list>
    /// The core runs the tick handlers only while the feature is active and never during the new-game fast-forward or the loading screen
    /// (<c>TickOptions.None</c>), under the guard of the feature.
    /// </summary>
    internal sealed class StartupCreditLineService
    {
        private readonly RecoveryEnvironment _env;
        private readonly CreditLineEngine _engine;

        public StartupCreditLineService(RecoveryEnvironment env, LoanService loans)
        {
            _env = env;
            _engine = new CreditLineEngine(env, loans.Book);
            _engine.AutoDrawn += OnAutoDrawn;
            _engine.Converted += OnConverted;
        }

        /// <summary>The rules of the line (the window reads the limit and draws through the service's methods).</summary>
        public CreditLineEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>The limit today.</summary>
        public double Limit
        {
            get { return _engine.Limit(); }
        }

        /// <summary>What can be drawn now.</summary>
        public double Available
        {
            get { return _engine.Available(); }
        }

        /// <summary>The line is open.</summary>
        public bool IsOpen
        {
            get { return _engine.IsOpen; }
        }

        /// <summary>Opens the line when it was never opened and the first years of the company are not over. True when it opened now.</summary>
        public bool EnsureOpened()
        {
            return _engine.EnsureOpened();
        }

        /// <summary>The auto draw, once a game day.</summary>
        public void OnDay()
        {
            _engine.OnDay();
        }

        /// <summary>The first of the month: the interest, the repayment, the end of the term.</summary>
        public void OnMonth()
        {
            _engine.OnMonth();
        }

        /// <summary>Draws money by hand; returns what was drawn (0 when nothing: <see cref="CreditLineEngine.DrawBlock"/> says why).</summary>
        public double Draw(double amount)
        {
            return _engine.Draw(amount);
        }

        /// <summary>Pays the drawn part back by hand; returns what was repaid (0 when nothing).</summary>
        public double Repay(double amount)
        {
            return _engine.Repay(amount);
        }

        /// <summary>The line as the API shows it; <c>Open</c> is false when there is no open line.</summary>
        public CreditLineView View()
        {
            CreditLineData line = _env.State.creditLine;
            if (line.state != CreditLineData.StateOpen) return default(CreditLineView);

            var view = new CreditLineView();
            view.Open = true;
            view.Limit = _engine.Limit();
            view.Drawn = line.drawn;
            view.Available = _engine.Available();
            view.ClosesOn = line.closesOn;
            view.AutoDraw = line.autoDraw;
            return view;
        }

        // ---- the engine's events ----

        private static void OnAutoDrawn(double amount)
        {
            ModNotify.ShowKey(RecoveryApi.FeatureId, "rec.notify.autoDraw", RecoveryViews.Money(amount));
        }

        private static void OnConverted(LoanData loan)
        {
            ModNotify.ShowKey(RecoveryApi.FeatureId, "rec.notify.lineConverted", RecoveryViews.Money(loan.principal));
        }
    }
}
