using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>Why the division cannot be founded now (the first reason in the order of the checks).</summary>
    public enum FoundingBlock
    {
        None,
        AlreadyFounded,
        MarketNotOpen,
        NotEnoughCredit,
        FoundryNotAllowed,
    }

    /// <summary>What the founding screen shows: the price (F30), the first reason it is blocked with its text key and arguments, the nodes that open for free.</summary>
    public struct GpuFoundingQuote
    {
        public FoundingBlock Block;
        public string ReasonKey;
        public string[] ReasonArgs;
        public float Cost;
        public List<string> FreeNodes;
        public int CatchUpYear;
    }

    /// <summary>
    /// The founding of the GPU division of the player (design.md, "GpuDivision"; FR-1). The checks go in this order and the first one that fails is the reason:
    /// already founded; the year is before the market opens (<c>market.opensYear</c>, 1981); a pure foundry (<c>ICompany.IsCpuCompany</c> is false); the money
    /// after paying would fall below the default threshold of the player (<c>ModCredit.ThresholdOf</c> - the bank's limit is never read directly). Founding
    /// costs <c>founding.cost x inflation</c> (F30) under <c>ExpenseOrigin.Development</c>, marks the division with its date and opens every node of the tree
    /// of a year up to <c>year - catchUpLagYears</c> (<see cref="GpuResearch.UnlockUpToYear"/>). <see cref="FoundFree"/> does the same without paying (the
    /// line of an acquired company passes to the player, FR-18 item 5).
    /// </summary>
    public sealed class GpuDivision
    {
        public const string ReasonAlready = "gpu.found.block.already";
        public const string ReasonYear = "gpu.found.block.year";
        public const string ReasonFoundry = "gpu.found.block.foundry";
        public const string ReasonCredit = "gpu.found.block.credit";

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuResearch _research;

        public GpuDivision(GpuContentDb content, GpuState state, IGpuGame game, GpuResearch research)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (research == null) throw new ArgumentNullException("research");
            _content = content;
            _state = state;
            _game = game;
            _research = research;
        }

        /// <summary>The division of the player is founded.</summary>
        public bool IsFounded
        {
            get { return _state.Data.divisionFounded; }
        }

        /// <summary>The date of the founding, or null.</summary>
        public GpuDate FoundedOn
        {
            get { return _state.Data.foundedOn; }
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        /// <summary>F30: <c>founding.cost x InflationHandler.Instance.Inflation</c>.</summary>
        public float Cost
        {
            get
            {
                float inflation = _game.Inflation;
                if (!(inflation > 0f) || float.IsInfinity(inflation)) inflation = 1f;
                return Math.Max(0f, Balance.founding.cost) * inflation;
            }
        }

        /// <summary>The year up to which the founding opens the nodes: the current year minus <c>catchUpLagYears</c>.</summary>
        public int CatchUpYear
        {
            get { return _game.Today.Year - Math.Max(0, Balance.founding.catchUpLagYears); }
        }

        /// <summary>The first reason the player cannot found the division, with its text key (<c>null</c> for <see cref="FoundingBlock.None"/>), and the price.</summary>
        public FoundingBlock Check(out string reasonKey, out float cost)
        {
            GpuFoundingQuote quote = Quote();
            reasonKey = quote.ReasonKey;
            cost = quote.Cost;
            return quote.Block;
        }

        /// <summary>Everything the founding screen needs (FR-1 items 3-5, 9; AC-1.3, AC-1.4, AC-1.5).</summary>
        public GpuFoundingQuote Quote()
        {
            var quote = new GpuFoundingQuote { Cost = Cost, CatchUpYear = CatchUpYear };
            quote.FreeNodes = _research.NodesUpToYear(quote.CatchUpYear);
            string player = _game.PlayerKey;
            int opens = Balance.market.opensYear;

            if (IsFounded)
            {
                quote.Block = FoundingBlock.AlreadyFounded;
                quote.ReasonKey = ReasonAlready;
            }
            else if (_game.Today.Year < opens)
            {
                quote.Block = FoundingBlock.MarketNotOpen;
                quote.ReasonKey = ReasonYear;
                quote.ReasonArgs = new[] { opens.ToString(CultureInfo.InvariantCulture) };
            }
            else if (!_game.IsCpuCompany(player))
            {
                quote.Block = FoundingBlock.FoundryNotAllowed;
                quote.ReasonKey = ReasonFoundry;
            }
            else if (_game.Money(player) - quote.Cost < _game.DebtThreshold(player))
            {
                quote.Block = FoundingBlock.NotEnoughCredit;
                quote.ReasonKey = ReasonCredit;
            }
            return quote;
        }

        /// <summary>FR-1 item 6: pays the founding, marks the division founded today and opens the nodes of the catch-up. False (nothing changes) when a check fails.</summary>
        public bool Found()
        {
            string reasonKey;
            float cost;
            if (Check(out reasonKey, out cost) != FoundingBlock.None) return false;
            _game.Spend(_game.PlayerKey, cost, GpuExpense.Development);
            Mark();
            return true;
        }

        /// <summary>FR-18 item 5: founds the division without paying and without the checks (an acquired line passes to the player); false when it is founded already.</summary>
        public bool FoundFree()
        {
            if (IsFounded) return false;
            Mark();
            return true;
        }

        private void Mark()
        {
            DateTime today = _game.Today;
            GpuSaveSection section = _state.Data;
            section.divisionFounded = true;
            section.foundedOn = new GpuDate { y = today.Year, m = today.Month, d = today.Day };
            _research.UnlockUpToYear(CatchUpYear);
        }
    }
}
