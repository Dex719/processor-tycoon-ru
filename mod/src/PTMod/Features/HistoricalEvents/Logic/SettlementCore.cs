using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The actions of the effect <c>AntitrustSettlement</c>.</summary>
    public static class SettlementActions
    {
        public const string Sue = "sue";
        public const string Decline = "decline";

        public static readonly string[] All = { Sue, Decline };
    }

    /// <summary>The keys of the notes about a suit (all in the string tables of the feature).</summary>
    public static class SettlementKeys
    {
        public const string NoteWon = "hist.note.settlement.won";
        public const string NoteLost = "hist.note.settlement.lost";
        public const string NotePaid = "hist.note.settlement.paid";
    }

    /// <summary>
    /// The numbers of a victim's suit (the members of the effect <c>AntitrustSettlement</c>, FR-6, design.md formula 5): the monthly legal costs as a share of the
    /// plaintiff's revenue, how long the suit lasts, the chance to win and the award as a share of the defendant's revenue. What the effect leaves out comes from the
    /// block <c>settlementDefaults</c> of <c>params.json</c>, and what that leaves out from the code (the numbers of the design).
    /// </summary>
    public sealed class SettlementParams
    {
        /// <summary>The monthly legal costs of the plaintiff as a share of its revenue of 12 months (AMD v Intel: 0.4 %).</summary>
        public float LegalCostPct = 0.004f;

        /// <summary>The months until the verdict.</summary>
        public int Months = 12;

        public float PWin = 0.7f;

        /// <summary>The award as a share of the defendant's revenue of 12 months (AMD v Intel 2009: USD 1.25 bn of about USD 35 bn, 3 %).</summary>
        public float SettlementPct = 0.03f;

        /// <summary>The id of the case the suit refers to when the effect names one (the event's fourth argument is used otherwise).</summary>
        public string CaseRef = string.Empty;

        /// <summary>The id of the suit; empty: the id of the calendar entry.</summary>
        public string SettlementId = string.Empty;

        /// <summary>Reads the numbers; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static SettlementParams Read(JsonObject raw, JsonObject defaults, List<string> problems)
        {
            var r = new ParamReader(raw, defaults, "AntitrustSettlement", problems);
            var p = new SettlementParams();

            r.Choice("action", SettlementActions.Sue, SettlementActions.All);
            p.LegalCostPct = r.Num("legalCostPct", p.LegalCostPct, 0f, 0.05f);
            p.Months = r.Int("months", p.Months, 1, 120);
            p.PWin = r.Num("pWin", p.PWin, 0f, 1f);
            p.SettlementPct = r.Num("settlementPct", p.SettlementPct, 0f, 0.3f);
            p.CaseRef = r.Str("caseRef", string.Empty);
            p.SettlementId = r.Str("settlementId", string.Empty);
            r.WarnUnknown();
            return p;
        }

        public static string ActionOf(JsonObject raw)
        {
            return raw == null ? SettlementActions.Sue : raw.GetString("action", SettlementActions.Sue) ?? SettlementActions.Sue;
        }
    }

    /// <summary>What a suit costs and may bring, worked out when the event is resolved (the numbers under the button).</summary>
    public sealed class SettlementQuote
    {
        public float LegalPerMonth;
        public int Months;

        /// <summary>The legal costs over the whole suit.</summary>
        public float TotalLegal;

        /// <summary>The award if the suit is won, by the defendant's revenue now.</summary>
        public float Award;
    }

    /// <summary>
    /// The suits of the victims of an antitrust case (design.md, Component 8: <c>SettlementService</c> without the glue; FR-6). A plaintiff (the player, or an AI
    /// challenger when the player does not qualify) pays legal costs every month; when the term is over the generator decides: with <c>pWin</c> the defendant pays
    /// <c>settlementPct × its revenue of 12 months</c> to the plaintiff (<c>Fines</c> for the payer, <c>Royalties</c> for the receiver), otherwise nothing more
    /// happens. A suit whose plaintiff or defendant has gone bankrupt ends without a payment. The rules are the same for the player and for an AI company.
    /// </summary>
    public sealed class SettlementCore
    {
        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly Func<IModRng> _rng;
        private readonly Func<float, string> _money;
        private readonly Action<string> _log;

        public SettlementCore(Func<HistSaveData> data, IHistEconomy economy, Func<IModRng> rng, Func<float, string> money = null, Action<string> log = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (economy == null) throw new ArgumentNullException("economy");
            if (rng == null) throw new ArgumentNullException("rng");
            _data = data;
            _economy = economy;
            _rng = rng;
            _money = money ?? (amount => amount.ToString("0"));
            _log = log;
        }

        /// <summary>The suits in progress (a view; do not change).</summary>
        public IReadOnlyList<SettlementDto> Suits
        {
            get { return _data().settlements; }
        }

        public SettlementDto Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (SettlementDto s in _data().settlements)
            {
                if (s != null && string.Equals(s.id, id, StringComparison.Ordinal)) return s;
            }
            return null;
        }

        /// <summary>The costs and the award for the plaintiff and defendant as they are now (the numbers under the button and in the letter).</summary>
        public static SettlementQuote Quote(SettlementParams p, float plaintiffRevenue12m, float defendantRevenue12m)
        {
            float perMonth = HistMoney.Of(p.LegalCostPct, plaintiffRevenue12m);
            return new SettlementQuote
            {
                LegalPerMonth = perMonth,
                Months = p.Months,
                TotalLegal = perMonth * p.Months,
                Award = HistMoney.Of(p.SettlementPct, defendantRevenue12m),
            };
        }

        /// <summary>
        /// Files a suit; the numbers are those the plaintiff was shown and are kept with it. False when the id is taken or a company is not in the game.
        /// </summary>
        public bool File(string id, string plaintiffKey, string defendantKey, SettlementParams p)
        {
            if (string.IsNullOrEmpty(id) || p == null) return false;
            if (!CompanyKey.IsValid(plaintiffKey) || !CompanyKey.IsValid(defendantKey) || string.Equals(plaintiffKey, defendantKey, StringComparison.Ordinal)) return false;
            if (!_economy.IsAlive(plaintiffKey) || !_economy.IsAlive(defendantKey)) return false;
            if (Find(id) != null)
            {
                Log("suit " + id + " is already filed; the second filing is ignored");
                return false;
            }

            _data().settlements.Add(new SettlementDto
            {
                id = id,
                plaintiffKey = plaintiffKey,
                defendantKey = defendantKey,
                resolveDay = _economy.Today + HistTime.MonthsToDays(p.Months),
                legalPerMonth = HistMoney.Of(p.LegalCostPct, _economy.Revenue12m(plaintiffKey)),
                pWin = p.PWin,
                settlementPct = p.SettlementPct,
            });
            return true;
        }

        /// <summary>The monthly pass (priority <see cref="HistTickPriority.Settlements"/>): the legal costs, and the verdict of the suits whose term is over.</summary>
        public void OnMonth()
        {
            HistSaveData data = _data();
            int today = _economy.Today;

            foreach (SettlementDto suit in new List<SettlementDto>(data.settlements))
            {
                if (suit == null) continue;
                try
                {
                    if (!_economy.IsAlive(suit.plaintiffKey) || !_economy.IsAlive(suit.defendantKey))
                    {
                        data.settlements.Remove(suit);
                        continue;
                    }

                    if (today < suit.resolveDay)
                    {
                        _economy.Spend(suit.plaintiffKey, suit.legalPerMonth, HistExpense.Fines);
                        continue;
                    }

                    Decide(suit);
                    data.settlements.Remove(suit);
                }
                catch (Exception ex)
                {
                    // One suit that fails ends here: it would repeat every month, and the money it moved is not known.
                    Log("suit " + suit.id + " failed and is dropped: " + ex.GetType().Name + ": " + ex.Message);
                    data.settlements.Remove(suit);
                }
            }
        }

        /// <summary>The verdict: one number from the generator; a win moves the award from the defendant to the plaintiff.</summary>
        private void Decide(SettlementDto suit)
        {
            bool won = _rng().Value() < suit.pWin;
            if (!won)
            {
                _economy.Notify(suit.plaintiffKey, SettlementKeys.NoteLost, _economy.NameOf(suit.defendantKey));
                return;
            }

            float award = HistMoney.Of(suit.settlementPct, _economy.Revenue12m(suit.defendantKey));
            if (award > 0f)
            {
                _economy.Spend(suit.defendantKey, award, HistExpense.Fines);
                _economy.Earn(suit.plaintiffKey, award, HistIncome.Royalties);
            }

            string text = _money(award);
            _economy.Notify(suit.plaintiffKey, SettlementKeys.NoteWon, text, _economy.NameOf(suit.defendantKey));
            _economy.Notify(suit.defendantKey, SettlementKeys.NotePaid, text, _economy.NameOf(suit.plaintiffKey));
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
