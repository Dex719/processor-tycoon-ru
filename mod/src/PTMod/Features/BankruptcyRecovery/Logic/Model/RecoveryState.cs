using System;
using System.Collections.Generic;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    // The state of the feature: the section "bankruptcy-recovery" of the side save file (FR-12, NFR-3; design.md, "Состояние (секция ModSave)").
    // Plain DTOs of ModJson.From / ModJson.Bind: the public fields are the keys of the JSON. They hold money and dates only, no references to the game.
    // There is no version field: the core writes and reads "_schema" of the section (RecoverySaveSection.SchemaVersion). The initializers are
    // the state of a game that never met the feature (a new game, an old save without the section).

    /// <summary>The state of the feature, the whole section. A fresh object is the state of a new game.</summary>
    [Serializable]
    public sealed class RecoveryState
    {
        /// <summary>The startup credit line (FR-7).</summary>
        public CreditLineData creditLine = new CreditLineData();

        /// <summary>The loans of the feature (bridge, term, rescue), oldest first.</summary>
        public List<LoanData> loans = new List<LoanData>();

        /// <summary>How many times the player accepted the "last chance" offer in this game (FR-8); survives switching the feature off and on.</summary>
        public int bailoutsUsed;

        /// <summary>The investor share contract of the "last chance" offer (FR-8).</summary>
        public InvestorShareData investorShare = new InvestorShareData();

        /// <summary>The episode of the player's default: what was already sent and done (FR-1, FR-8, FR-9).</summary>
        public EpisodeData episode = new EpisodeData();

        /// <summary>The date the last bridge loan was closed, <c>yyyy-MM-dd</c>, or empty; the cooldown counts from it (FR-6, survives switching the feature off).</summary>
        public string lastBridgeClosedOn = "";

        /// <summary>The number the next loan id gets (<c>bridge-1</c>, <c>bridge-2</c>...).</summary>
        public int nextLoanSeq = 1;

        /// <summary>The journal of the money operations (FR-10), oldest first, at most <see cref="RecoveryStateCodec.MaxJournalEntries"/>.</summary>
        public List<JournalEntryData> journal = new List<JournalEntryData>();
    }

    /// <summary>The startup credit line (FR-7).</summary>
    [Serializable]
    public sealed class CreditLineData
    {
        /// <summary>The line was never opened (also the state after a reset).</summary>
        public const string StateNone = "none";

        /// <summary>The line is open.</summary>
        public const string StateOpen = "open";

        /// <summary>The line is closed for good; <see cref="closedReason"/> says why. It is never opened again.</summary>
        public const string StateClosed = "closed";

        /// <summary>The line ran out of time and the drawn part became a term loan.</summary>
        public const string ReasonExpired = "expired";

        /// <summary>The feature was switched off and the drawn part became ordinary debt (FR-12).</summary>
        public const string ReasonDisabled = "disabled";

        /// <summary><see cref="StateNone"/>, <see cref="StateOpen"/> or <see cref="StateClosed"/>.</summary>
        public string state = StateNone;

        /// <summary>The first day of the line, <c>yyyy-MM-dd</c>; empty while the line was not opened.</summary>
        public string opensOn = "";

        /// <summary>The day the line closes, <c>yyyy-MM-dd</c>; empty while the line was not opened.</summary>
        public string closesOn = "";

        /// <summary>The drawn part, the debt on the line; 0 or more.</summary>
        public double drawn;

        /// <summary>The line draws money by itself near the default threshold (the switch of the menu).</summary>
        public bool autoDraw = true;

        /// <summary>Empty, <see cref="ReasonExpired"/> or <see cref="ReasonDisabled"/>.</summary>
        public string closedReason = "";
    }

    /// <summary>One loan of the feature (bridge, term or rescue): an annuity.</summary>
    [Serializable]
    public sealed class LoanData
    {
        /// <summary>The kinds, spelled as the members of <c>LoanKind</c> (the save stores the name).</summary>
        public const string KindBridge = "Bridge";
        public const string KindTerm = "Term";
        public const string KindRescueLoan = "RescueLoan";

        /// <summary>The id, unique in the save: <c>bridge-1</c>.</summary>
        public string id = "";

        /// <summary><see cref="KindBridge"/>, <see cref="KindTerm"/> or <see cref="KindRescueLoan"/>.</summary>
        public string kind = "";

        /// <summary>The amount the loan started with.</summary>
        public double principal;

        /// <summary>What the player still owes on the principal.</summary>
        public double outstanding;

        /// <summary>The annual rate before the <c>bank.interest</c> multiplier, as a fraction (0.30 is 30 percent).</summary>
        public double annualRate;

        /// <summary>The term in months.</summary>
        public int monthsTotal;

        /// <summary>The payments left.</summary>
        public int monthsLeft;

        /// <summary>The monthly payment (interest and principal) as it was set when the loan was opened.</summary>
        public double payment;

        /// <summary>The day the loan was opened, <c>yyyy-MM-dd</c>.</summary>
        public string openedOn = "";
    }

    /// <summary>The investor share contract (FR-8): a part of the monthly profit for a fixed time, capped.</summary>
    [Serializable]
    public sealed class InvestorShareData
    {
        /// <summary>The contract is running.</summary>
        public bool active;

        /// <summary>The amount the investor gave.</summary>
        public double amount;

        /// <summary>The part of the positive profit of the last month the investor takes, 0..1.</summary>
        public double share;

        /// <summary>The payments left.</summary>
        public int monthsLeft;

        /// <summary>Everything paid so far.</summary>
        public double paid;

        /// <summary>The ceiling of all payments.</summary>
        public double cap;
    }

    /// <summary>The episode of the player's default (the flag "the offer is open" is not saved: it belongs to the session).</summary>
    [Serializable]
    public sealed class EpisodeData
    {
        /// <summary>The "default risk" letter was sent in this episode (FR-1).</summary>
        public bool preWarningSent;

        /// <summary>The "company in default" letter was sent in this episode (FR-2).</summary>
        public bool defaultMailSent;

        /// <summary>Days in a row above the threshold while in default (FR-9).</summary>
        public int recoveryStreak;

        /// <summary>The player declined the "last chance" offer in this episode (FR-8): the next tick is the game's own.</summary>
        public bool bailoutDeclined;
    }
}
