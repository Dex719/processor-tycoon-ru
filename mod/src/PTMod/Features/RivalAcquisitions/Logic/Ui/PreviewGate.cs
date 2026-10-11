namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The choices and the lock of the preview of a purchase (TSK-016, FR-7 item 5, FR-12 item 5, NFR-5): keep or sell the lines, the tick "I understand the
    /// consequences", and the guard against pressing "Buy" twice. The window asks it before it enables a button; the plan is built from <see cref="Options"/>
    /// every time a choice changes. Pure.
    /// <list type="bullet">
    /// <item><b>Buy</b> is possible only when the plan has no blocking error (the quote is open, the lines may be sold, the consequences are ticked),
    /// no deal of this window is running, the window did not finish a deal already, and the executor is not busy with another deal.</item>
    /// <item><b>Lines</b>: "sell" is refused when the plan says the lines are required; a change of the choice clears the tick (the consequences may differ).</item>
    /// <item><b>Begin / End</b>: <see cref="TryBegin"/> answers true once; a second press before <see cref="End"/> gets false.</item>
    /// </list>
    /// </summary>
    public sealed class PreviewGate
    {
        /// <summary>"Keep the lines" (true, the default) or "Sell the lines".</summary>
        public bool KeepLines { get; private set; } = true;

        /// <summary>The tick "I understand the consequences".</summary>
        public bool Acknowledged { get; private set; }

        /// <summary>A deal of this window is being carried out.</summary>
        public bool Executing { get; private set; }

        /// <summary>The deal of this window was carried out; the window closes.</summary>
        public bool Done { get; private set; }

        /// <summary>What the player chose, for <c>AcquisitionPlanBuilder.Build</c> and the executor.</summary>
        public PlanOptions Options()
        {
            return new PlanOptions { KeepLines = KeepLines, ConsequencesAcknowledged = Acknowledged };
        }

        /// <summary>
        /// The player picks keep or sell. Selling is refused (false) when <paramref name="linesRequired"/>, and while a deal runs. A real change clears
        /// the tick of the consequences.
        /// </summary>
        public bool SetKeepLines(bool keep, bool linesRequired)
        {
            if (Executing || Done) return false;
            if (!keep && linesRequired) return false;
            if (keep == KeepLines) return true;

            KeepLines = keep;
            Acknowledged = false;
            return true;
        }

        /// <summary>The player ticks or unticks "I understand". Ignored while a deal runs.</summary>
        public void SetAcknowledged(bool value)
        {
            if (Executing || Done) return;
            Acknowledged = value;
        }

        /// <summary>The choice of the lines can be changed now.</summary>
        public bool CanChooseLines(bool hasLines)
        {
            return hasLines && !Executing && !Done;
        }

        /// <summary>The button "Buy" is enabled.</summary>
        public bool CanBuy(AcquisitionPlan plan, bool executorBusy)
        {
            return plan != null && plan.CanExecute && !Executing && !Done && !executorBusy;
        }

        /// <summary>
        /// "Buy" was confirmed: true once, and from now on <see cref="Executing"/> is set so that nothing else can start. False when the deal cannot start
        /// (blocked plan, a deal is running, done already, the executor is busy).
        /// </summary>
        public bool TryBegin(AcquisitionPlan plan, bool executorBusy)
        {
            if (!CanBuy(plan, executorBusy)) return false;
            Executing = true;
            return true;
        }

        /// <summary>The deal ended: <paramref name="executed"/> true when it went ahead (the window then closes), false when it was cancelled before any change.</summary>
        public void End(bool executed)
        {
            Executing = false;
            if (executed) Done = true;
        }
    }
}
