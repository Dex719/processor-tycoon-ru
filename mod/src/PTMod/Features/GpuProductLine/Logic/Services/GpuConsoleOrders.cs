using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The mail service as the console orders see it: the letters of the news (<see cref="IGpuMail"/>) plus the buttons of a letter. The game side (<c>Bridges/GpuMailAdapter</c>) is
    /// <c>ModMail.SetChoiceState</c>, <c>ModMail.Refresh</c> and <c>ModMail.Resolve</c>; the answers of the player come back through <see cref="GpuConsoleOrders.OnChoice"/>.
    /// </summary>
    public interface IGpuOrderMail : IGpuMail
    {
        /// <summary>Switches an answer button of the letter on or off; a gray button shows <paramref name="disabledReasonKey"/> as the reason. Unknown letters are ignored.</summary>
        void SetChoiceState(int mailId, string choiceId, bool enabled, string disabledReasonKey);

        /// <summary>Draws the open letter again.</summary>
        void Refresh(int mailId);

        /// <summary>The feature answers the letter itself (the offer is gone): the buttons give way to the answer and the handler of the choices is not called. False when it could not.</summary>
        bool Resolve(int mailId, string choiceId);
    }

    /// <summary>
    /// The question the hub asks before it switches the feature off when console orders run (<c>FeatureContext.ConfirmBeforeDisable</c>, FR-19 item 7, AC-19.5): the key of the text
    /// with its arguments already put into words, and the key of the caption of the "Yes" button. The game side makes a <c>DisableConfirmation</c> of it.
    /// </summary>
    public sealed class GpuDisableConfirmation
    {
        public string MessageKey = string.Empty;
        public string[] Args = new string[0];
        public string ConfirmLabelKey = string.Empty;
    }

    /// <summary>A console order as the tab "Линейка" shows it (FR-19; the block "Консольные заказы").</summary>
    public sealed class GpuConsoleOrderView
    {
        /// <summary>The id of the offer of <c>consoles.json</c> (an order is made once per offer).</summary>
        public string Id = string.Empty;

        /// <summary><c>offered</c>, <c>active</c>, <c>ended</c>, <c>cancelled</c> or <c>declined</c> (<see cref="GpuState.OrderOffered"/> and the others).</summary>
        public string Status = string.Empty;

        /// <summary>The maker in the language of the player.</summary>
        public string Client = string.Empty;

        public int OfferYear, TermYears;

        /// <summary>The card the order is tied to; empty for an offer.</summary>
        public string ProductId = string.Empty, ProductName = string.Empty;

        /// <summary>The cards a day and the price of one: the terms of an active order, the estimate of an offer.</summary>
        public int UnitsPerDay;

        public float Price;
        public DateTime? OfferedOn, StartedOn, EndsOn;

        /// <summary>The share of the order that was not delivered over the days kept in the window, and the number of those days.</summary>
        public float MissedShare;

        public int WindowDays;

        /// <summary>The missed share at which the order is cancelled (<c>console.failCancelShare</c>) and the days of the window (<c>console.graceDays</c>).</summary>
        public float FailShare;

        public int GraceDays;

        /// <summary>The offer can still be answered (the letter is open for <c>console.offerTimeoutDays</c> from its delivery); the last day to answer, an estimate from the day it was made.</summary>
        public DateTime? AnswerBy;
    }

    /// <summary>
    /// Console orders (design.md, "GpuConsoleOrders и GpuConsoleMath"; FR-19, Could, D20): a console maker offers a long delivery of cards at a fixed price, the player accepts it in the
    /// letter, and the cards are shipped from the stock of one of the player's cards every day until the term ends.
    /// <list type="bullet">
    /// <item><b>The offer</b> (<see cref="DayTick"/>): in the year <c>offerYear</c> of a record of <c>consoles.json</c>, with the switch <c>consoleOrders</c> on and the division founded, a letter
    /// goes out (<c>Critical</c>, tag <c>gpu:console:&lt;orderId&gt;</c>, 60 days to answer, "Отказаться" by default, the button "Открыть линейку"). The button "Принять" is gray with the reason "Нет подходящей
    /// карты" while no card of the player is fast enough (F35: at least <c>minPerfShare</c> of the best card on sale in the target segment <c>console.segment</c>, else <c>fallbackSegment</c>, and
    /// every function the segment demands); it is looked at again every <c>revalidateDays</c> days, when the letter arrives and when a game is loaded.</item>
    /// <item><b>The order</b> (<see cref="OnChoice"/>): on "Принять" the best fitting card is tied to the order, the price is fixed at <c>max(priceFloorMarkup x U, priceFactor x Budget)</c> and the
    /// volume at <c>floor(unitsShare x N_s)</c> cards a day, for <c>termYears</c>. The card's own sales go on; the order only takes cards from its stock.</item>
    /// <item><b>The delivery</b> (<see cref="DeliverDay"/>, <c>ContractSellTick</c>, before the shop): <c>min(stock, volume)</c> cards, paid <c>price x delivered</c> as <c>RevenueOrigin.Contracts</c>
    /// (the channel <c>contract.reward</c> belongs to the contracts of the game and does not act here); the rest of the day's volume is written to the window of the last <c>graceDays</c> days.</item>
    /// <item><b>The breach</b>: from <c>graceDays</c> days after the start, a missed share over <c>failCancelShare</c> cancels the order, lowers the reputation by <c>reputationPenalty</c> and sends a
    /// letter. Taking the tied card off production (<see cref="OnProductRetired"/>) and switching the feature off (<see cref="CancelAll"/>, after the question of <see cref="BuildDisableConfirmation"/>)
    /// cancel it the same way. The term running out ends the order with a letter and no penalty.</item>
    /// </list>
    /// Only the player gets orders (FR-19 item 8). No order is made in the fast-forward or while a save loads: the offers wait for <c>canSend</c>, the delivery is a tick without those options.
    /// </summary>
    public sealed class GpuConsoleOrders
    {
        public const string TagPrefix = "gpu:console:";
        public const string ChoiceAccept = "accept";
        public const string ChoiceDecline = "decline";
        public const string ActionOpenLineup = "openLineup";

        /// <summary>The reason a reputation change is written with (<c>ModReputation.Add</c>) and the name of the hub's question.</summary>
        public const string FailedKey = "gpu.console.failed";

        /// <summary>The reason of the gray button "Принять".</summary>
        public const string NoProductKey = "gpu.console.noproduct";

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuProduction _production;
        private readonly GpuMarketService _market;
        private readonly GpuLineup _lineup;
        private readonly IGpuOrderMail _mail;
        private readonly IGpuText _text;
        private readonly Func<bool> _enabled;
        private readonly Func<bool> _canSend;
        private readonly Action<string> _warn;

        private readonly Dictionary<int, bool> _buttons = new Dictionary<int, bool>();   // the state of the button "Принять" last given to the core, by letter
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _lastRevalidate = DateTime.MinValue;

        /// <param name="production">The production: the unit cost of the tied card and the demand its automatic mode follows.</param>
        /// <param name="market">The market: the target segment, its buyers and budget, the best card on sale there.</param>
        /// <param name="lineup">The lineup: the player's cards.</param>
        /// <param name="mail">The mail service of the core.</param>
        /// <param name="text">The words and the numbers in the language of the player.</param>
        /// <param name="enabled">The switch <c>consoleOrders</c> of the hub (new offers only); null means on.</param>
        /// <param name="canSend">False while the game is not in its normal course (the fast-forward, a load); null means yes.</param>
        /// <param name="warn">English lines for the log; may be null.</param>
        public GpuConsoleOrders(GpuContentDb content, GpuState state, IGpuGame game, GpuProduction production, GpuMarketService market, GpuLineup lineup, IGpuOrderMail mail,
            IGpuText text, Func<bool> enabled, Func<bool> canSend, Action<string> warn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (production == null) throw new ArgumentNullException("production");
            if (market == null) throw new ArgumentNullException("market");
            if (lineup == null) throw new ArgumentNullException("lineup");
            if (mail == null) throw new ArgumentNullException("mail");
            if (text == null) throw new ArgumentNullException("text");
            _content = content;
            _state = state;
            _game = game;
            _production = production;
            _market = market;
            _lineup = lineup;
            _mail = mail;
            _text = text;
            _enabled = enabled;
            _canSend = canSend;
            _warn = warn;
        }

        private GpuBalanceDto.ConsoleBlock Rules
        {
            get { return (_content.Balance ?? new GpuBalanceDto()).console ?? new GpuBalanceDto.ConsoleBlock(); }
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private List<GpuConsoleOrderSave> OrderList
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.consoleOrders == null) data.consoleOrders = new List<GpuConsoleOrderSave>();
                return data.consoleOrders;
            }
        }

        /// <summary>The orders of the section, in the order they were made (offers, active, history).</summary>
        public IReadOnlyList<GpuConsoleOrderSave> Orders
        {
            get { return OrderList; }
        }

        /// <summary>The switch <c>consoleOrders</c> is on: new offers are made (running orders go on whatever it says).</summary>
        public bool Enabled
        {
            get { return _enabled == null || _enabled(); }
        }

        /// <summary>No letter is made now: the game is not in its normal course.</summary>
        public bool IsSilent
        {
            get { return _canSend != null && !_canSend(); }
        }

        /// <summary>The orders that are running.</summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (GpuConsoleOrderSave o in OrderList)
                {
                    if (o != null && o.status == GpuState.OrderActive) count++;
                }
                return count;
            }
        }

        /// <summary>The running orders tied to this card.</summary>
        public int ActiveFor(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return 0;
            int count = 0;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o != null && o.status == GpuState.OrderActive && o.productId == productId) count++;
            }
            return count;
        }

        /// <summary>The fall of the reputation that breaking <paramref name="orders"/> orders costs (<c>console.reputationPenalty</c> each).</summary>
        public float PenaltyFor(int orders)
        {
            return Math.Max(0, orders) * Math.Max(0f, Rules.reputationPenalty);
        }

        /// <summary>The order made from an offer of the content, or null.</summary>
        public GpuConsoleOrderSave Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return OrderList.Find(o => o != null && o.id == id);
        }

        // ---- the target and the candidates (F35) ----

        /// <summary>The segment the offers are measured in: <c>console.segment</c> if it is open at the date, else <c>console.fallbackSegment</c>; null when neither is.</summary>
        public GpuSegmentDto TargetSegment(DateTime date)
        {
            GpuBalanceDto.ConsoleBlock rules = Rules;
            List<GpuSegmentDto> open = _market.OpenSegmentsAt(date);
            GpuSegmentDto target = OpenSegment(open, rules.segment);
            return target ?? OpenSegment(open, rules.fallbackSegment);
        }

        private static GpuSegmentDto OpenSegment(List<GpuSegmentDto> open, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (GpuSegmentDto seg in open)
            {
                if (seg.id == id) return seg;
            }
            return null;
        }

        /// <summary>
        /// The card of the player an offer would be tied to: of the cards on sale that have every function the target segment demands this year, the one with the best performance that
        /// is at least <c>minPerfShare</c> of the best card on sale there (F35); the earlier one in the section on a tie. Null when there is none (or no target segment).
        /// </summary>
        public GpuProductSave BestCandidate(DateTime date)
        {
            GpuSegmentDto seg = TargetSegment(date);
            return seg == null ? null : BestCandidate(seg, date);
        }

        private GpuProductSave BestCandidate(GpuSegmentDto seg, DateTime date)
        {
            double bar = _market.BestPerfIn(seg, date.Year);
            string[] required = GpuMarketMath.RequiredFeatures(seg.featureReqs, date.Year);
            float minShare = Rules.minPerfShare;
            GpuProductSave best = null;
            foreach (GpuProductSave p in Products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling || !p.sell || !(p.price > 0f) || p.stats == null) continue;
                if (GpuMarketMath.FeatureFactor(p.stats.features, required, 0f) <= 0f) continue;
                if (!GpuConsoleMath.Qualifies(p.stats.effectivePerf, bar, minShare)) continue;
                if (best == null || p.stats.effectivePerf > best.stats.effectivePerf) best = p;
            }
            return best;
        }

        private List<GpuProductSave> Products
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.products == null) data.products = new List<GpuProductSave>();
                return data.products;
            }
        }

        // The terms an order would have today for a candidate (the price needs the unit cost of the card; without a card the floor of the share of the budget).
        private void Terms(GpuSegmentDto seg, GpuProductSave card, DateTime date, out float price, out int units)
        {
            GpuBalanceDto.ConsoleBlock rules = Rules;
            float unitCost = card != null ? _production.UnitCostOf(card.id) : 0f;
            price = GpuConsoleMath.OrderPrice(unitCost, _market.BudgetOf(seg, date.Year), rules.priceFactor, rules.priceFloorMarkup);
            units = GpuConsoleMath.DailyUnits(_market.DailyBuyersOf(seg, date.Year), rules.unitsShare);
        }

        // ---- the days ----

        /// <summary>
        /// A day passed (<c>TickPhase.DayPassed</c>): orders whose term is over end, the buttons of the offers are looked at again once in <c>revalidateDays</c> days, and the offers of the
        /// year go out (FR-19 items 1-2, 6).
        /// </summary>
        public void DayTick()
        {
            DateTime today = _game.Today;
            EndExpired(today);
            OfferNew(today);
            if (_lastRevalidate == DateTime.MinValue || (today - _lastRevalidate).TotalDays >= Math.Max(1, Rules.revalidateDays) || today < _lastRevalidate)
            {
                _lastRevalidate = today;
                RevalidateAll();
            }
        }

        private void OfferNew(DateTime today)
        {
            if (!Enabled || IsSilent || !_state.Data.divisionFounded) return;
            foreach (GpuConsoleDto record in _content.Consoles)
            {
                if (record.offerYear != today.Year || Find(record.id) != null) continue;
                if (OrderList.Count >= Balance.limits.consoleOrders)
                {
                    WarnOnce("limit", "console: " + OrderList.Count.ToString(CultureInfo.InvariantCulture) + " orders are kept, no more offers are made");
                    return;
                }
                Offer(record, today);
            }
        }

        private void Offer(GpuConsoleDto record, DateTime today)
        {
            GpuSegmentDto seg = TargetSegment(today);
            if (seg == null) return;   // no segment to measure in: try again tomorrow, the year is not over

            GpuBalanceDto.ConsoleBlock rules = Rules;
            GpuProductSave card = BestCandidate(seg, today);
            float price;
            int units;
            Terms(seg, card, today, out price, out units);
            bool canAccept = card != null && units > 0;

            var letter = new GpuMailLetter
            {
                Tag = TagPrefix + record.id,
                TitleKey = "gpu.console.offer.subject",
                BodyKey = "gpu.console.offer.body",
                Args = new[]
                {
                    ClientName(record),
                    _text.Integer(units),
                    _text.Money(price),
                    _text.Plural("gpu.fmt.years", record.termYears),
                    _text.Date(today.AddDays(rules.offerTimeoutDays)),
                    rules.graceDays.ToString(CultureInfo.InvariantCulture),
                    _text.Percent(rules.failCancelShare),
                    Points(rules.reputationPenalty),
                },
                Priority = MailPriority.Critical,
                TimeoutDays = rules.offerTimeoutDays,
                DefaultChoiceId = ChoiceDecline,
                Choices = new List<GpuMailChoice>
                {
                    new GpuMailChoice
                    {
                        Id = ChoiceAccept, LabelKey = "gpu.console.accept", TooltipKey = "gpu.console.accept.tip",
                        Enabled = canAccept, DisabledReasonKey = canAccept ? string.Empty : NoProductKey,
                    },
                    new GpuMailChoice { Id = ChoiceDecline, LabelKey = "gpu.console.decline", TooltipKey = "gpu.console.decline.tip" },
                },
                Actions = new List<GpuMailAction>
                {
                    new GpuMailAction { Id = ActionOpenLineup, LabelKey = "gpu.console.openLineup", TooltipKey = "gpu.console.openLineup.tip", Repeatable = true },
                },
            };

            int mailId = _mail.Send(letter);
            if (mailId < 0)
            {
                WarnOnce("send:" + record.id, "console: the mail service did not take the offer " + record.id);
                return;
            }

            OrderList.Add(new GpuConsoleOrderSave
            {
                id = record.id,
                status = GpuState.OrderOffered,
                mailId = mailId,
                offeredOn = DateOf(today),
                unitsPerDay = units,
                price = price,
            });
            _buttons[mailId] = canAccept;
        }

        // An order whose term is over ends with a letter and no penalty (FR-19 item 6).
        private void EndExpired(DateTime today)
        {
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o == null || o.status != GpuState.OrderActive) continue;
                DateTime end = EndOf(o, today);
                if (end > today) continue;

                o.status = GpuState.OrderEnded;
                ReleaseDemand(o.productId);
                GpuConsoleDto record = _content.ConsoleEntry(o.id);
                Send(new GpuMailLetter
                {
                    Tag = TagPrefix + o.id + ":ended",
                    TitleKey = "gpu.console.mail.ended.subject",
                    BodyKey = "gpu.console.mail.ended.body",
                    Args = new[] { record != null ? ClientName(record) : o.id },
                    Priority = MailPriority.Low,
                });
            }
        }

        private DateTime EndOf(GpuConsoleOrderSave o, DateTime fallback)
        {
            DateTime? end = DateFrom(o.endsOn);
            if (end.HasValue) return end.Value;
            DateTime? start = DateFrom(o.startedOn);
            GpuConsoleDto record = _content.ConsoleEntry(o.id);
            if (start.HasValue && record != null) return start.Value.AddYears(record.termYears);
            return fallback;
        }

        // ---- the buttons of the offers (FR-19 item 2) ----

        /// <summary>The state of the button "Принять" of every open offer is given to the core again: after a load (<c>GameReady</c>, AC-19.2) and once in <c>revalidateDays</c> days.</summary>
        private void RevalidateAll()
        {
            DateTime today = _game.Today;
            GpuSegmentDto seg = null;
            GpuProductSave card = null;
            bool computed = false;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o == null || o.status != GpuState.OrderOffered || o.mailId <= 0) continue;
                if (!computed)
                {
                    seg = TargetSegment(today);
                    card = seg == null ? null : BestCandidate(seg, today);
                    computed = true;
                }
                ApplyButton(o, card != null);
            }
        }

        private void ApplyButton(GpuConsoleOrderSave o, bool canAccept)
        {
            bool known;
            bool last;
            known = _buttons.TryGetValue(o.mailId, out last);
            if (known && last == canAccept) return;
            _buttons[o.mailId] = canAccept;
            _mail.SetChoiceState(o.mailId, ChoiceAccept, canAccept, canAccept ? null : NoProductKey);
            _mail.Refresh(o.mailId);
        }

        /// <summary>
        /// A game is ready (a save loaded, a new game started, or the feature switched on): the state of every button of the offers is computed again and given to the core, because the
        /// core's record of the letter may be older than the cards of the player (AC-19.2), and the demand of the running orders goes back to the automatic mode of their cards.
        /// </summary>
        public void SessionStart()
        {
            _buttons.Clear();
            _lastRevalidate = _game.Today;
            RevalidateAll();
            RefreshDemand();
        }

        /// <summary>A letter of an offer reached the inbox (<c>ModMail.Delivered</c>): its button is looked at once more, so it shows the state of the day it arrives.</summary>
        public void OnMailDelivered(int mailId)
        {
            GpuConsoleOrderSave o = ByMail(mailId);
            if (o == null || o.status != GpuState.OrderOffered) return;
            DateTime today = _game.Today;
            GpuProductSave card = BestCandidate(today);
            ApplyButton(o, card != null);
        }

        // ---- the answer (FR-19 item 3) ----

        /// <summary>
        /// The player pressed a button of an offer, or the answer period passed (the core answers "Отказаться" then). "Отказаться" closes the offer. "Принять" ties the best card to the
        /// order and fixes the price and the volume; when no card fits any more (the state of the button was old) the order is cancelled without a penalty and the player is told.
        /// </summary>
        public void OnChoice(int mailId, string choiceId)
        {
            GpuConsoleOrderSave o = ByMail(mailId);
            if (o == null)
            {
                WarnOnce("choice:" + mailId.ToString(CultureInfo.InvariantCulture), "console: an answer to a letter that is no order of ours (mail " + mailId.ToString(CultureInfo.InvariantCulture) + ")");
                return;
            }
            if (o.status != GpuState.OrderOffered) return;   // the order was cancelled or answered some other way

            _buttons.Remove(mailId);
            if (choiceId == ChoiceDecline)
            {
                o.status = GpuState.OrderDeclined;
                return;
            }
            if (choiceId != ChoiceAccept)
            {
                WarnOnce("choiceId:" + choiceId, "console: unknown answer '" + choiceId + "'");
                return;
            }
            Accept(o);
        }

        private void Accept(GpuConsoleOrderSave o)
        {
            DateTime today = _game.Today;
            GpuConsoleDto record = _content.ConsoleEntry(o.id);
            string client = record != null ? ClientName(record) : o.id;
            GpuSegmentDto seg = TargetSegment(today);
            GpuProductSave card = seg == null ? null : BestCandidate(seg, today);
            float price = 0f;
            int units = 0;
            if (card != null) Terms(seg, card, today, out price, out units);

            if (record == null || card == null || units <= 0 || !(price > 0f))
            {
                o.status = GpuState.OrderCancelled;
                _game.Notify("gpu.console.accept.failed", client, false);
                return;
            }

            o.status = GpuState.OrderActive;
            o.productId = card.id;
            o.unitsPerDay = units;
            o.price = price;
            o.startedOn = DateOf(today);
            o.endsOn = DateOf(today.AddYears(record.termYears));
            o.deliveredWindow = new List<int>();
            o.missedWindow = new List<int>();
            _production.SetContractDemand(card.id, DemandOf(card.id));
            _game.Notify("gpu.console.accepted", client, false);
        }

        // ---- the delivery (FR-19 items 4-5) ----

        /// <summary>
        /// One day of deliveries (<c>TickPhase.ContractSellTick</c>, before the shop): each running order takes <c>min(stock, volume)</c> cards from its card, pays
        /// <c>price x delivered</c> as <c>RevenueOrigin.Contracts</c> (into the books of the card too), writes the day to the window of <c>graceDays</c> days and is cancelled
        /// when it missed too much (F35). Nothing happens without a running order.
        /// </summary>
        public void DeliverDay()
        {
            if (ActiveCount == 0) return;

            DateTime today = _game.Today;
            GpuBalanceDto.ConsoleBlock rules = Rules;
            int grace = Math.Max(1, rules.graceDays);
            int month = GpuLedgerBook.MonthIndex(today);
            int ledgerMonths = Balance.limits.ledgerMonths;
            string player = _game.PlayerKey;

            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o == null || o.status != GpuState.OrderActive) continue;

                GpuProductSave card = _lineup.Find(o.productId);
                if (card == null || card.status == GpuState.StatusRetired)
                {
                    // the card is gone without the lineup telling (a section that was edited): there is nothing to deliver and nobody to blame
                    WarnOnce("card:" + o.id, "console: the card '" + o.productId + "' of the order " + o.id + " is gone, the order is cancelled");
                    o.status = GpuState.OrderCancelled;
                    ReleaseDemand(o.productId);
                    continue;
                }

                int units = Math.Max(0, o.unitsPerDay);
                int delivered = GpuConsoleMath.Delivered(card.stock, units);
                int missed = units - delivered;
                if (delivered > 0)
                {
                    card.stock -= delivered;
                    float income = o.price * delivered;
                    if (income > 0f && !float.IsInfinity(income))
                    {
                        _game.Earn(player, income, GpuRevenue.Contracts);
                        GpuLedgerBook.AddSales(card.ledger ?? (card.ledger = new GpuLedgerSave()), month, delivered, income, ledgerMonths);
                    }
                }
                if (o.deliveredWindow == null) o.deliveredWindow = new List<int>();
                if (o.missedWindow == null) o.missedWindow = new List<int>();
                GpuConsoleMath.Push(o.deliveredWindow, delivered, grace);
                GpuConsoleMath.Push(o.missedWindow, missed, grace);

                DateTime? started = DateFrom(o.startedOn);
                int daysActive = started.HasValue ? GpuConsoleMath.DaysActive(started.Value, today) : grace;
                float share = GpuConsoleMath.MissedShare(o.deliveredWindow, o.missedWindow);
                if (GpuConsoleMath.ShouldCancel(share, rules.failCancelShare, daysActive, grace)) Cancel(o, Breach, share, true);
            }
            RefreshDemand();
        }

        // ---- cancelling (FR-19 item 5, 7) ----

        private const int Breach = 0, CardRetired = 1, Switched = 2;

        // The order is cancelled: the reputation falls by the penalty, the demand of the card is released and (unless the player switched the feature off) a letter says so.
        private void Cancel(GpuConsoleOrderSave o, int cause, float missedShare, bool letter)
        {
            o.status = GpuState.OrderCancelled;
            ReleaseDemand(o.productId);

            float penalty = Math.Max(0f, Rules.reputationPenalty);
            if (penalty > 0f) _game.AddReputation(_game.PlayerKey, -penalty, FailedKey);
            if (!letter) return;

            GpuConsoleDto record = _content.ConsoleEntry(o.id);
            string client = record != null ? ClientName(record) : o.id;
            if (cause == Breach)
            {
                Send(new GpuMailLetter
                {
                    Tag = TagPrefix + o.id + ":failed",
                    TitleKey = "gpu.console.mail.failed.subject",
                    BodyKey = "gpu.console.mail.failed.body",
                    Args = new[] { client, Math.Max(1, Rules.graceDays).ToString(CultureInfo.InvariantCulture), _text.Percent(missedShare), Points(penalty) },
                    Priority = MailPriority.Normal,
                });
            }
            else
            {
                GpuProductSave card = _lineup.Find(o.productId);
                Send(new GpuMailLetter
                {
                    Tag = TagPrefix + o.id + ":retired",
                    TitleKey = "gpu.console.mail.retired.subject",
                    BodyKey = "gpu.console.mail.retired.body",
                    Args = new[] { client, card != null ? card.name ?? o.productId : o.productId, Points(penalty) },
                    Priority = MailPriority.Normal,
                });
            }
        }

        /// <summary>FR-19 item 7: a card of the player was taken off production (<see cref="GpuLineup.Retired"/>); the running orders tied to it are cancelled with the penalty and a letter.</summary>
        public void OnProductRetired(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o != null && o.status == GpuState.OrderActive && o.productId == productId) Cancel(o, CardRetired, 0f, true);
            }
        }

        /// <summary>
        /// FR-19 item 7: the feature is switched off. Running orders are cancelled with the penalty and without a letter (the player was asked, or the code did it on purpose); the open
        /// offers are cancelled and their letters closed with "Отказаться". Returns the number of orders that were running.
        /// </summary>
        public int CancelAll()
        {
            int running = 0;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o == null) continue;
                if (o.status == GpuState.OrderActive)
                {
                    running++;
                    Cancel(o, Switched, 0f, false);
                }
                else if (o.status == GpuState.OrderOffered)
                {
                    o.status = GpuState.OrderCancelled;
                    if (o.mailId > 0) _mail.Resolve(o.mailId, ChoiceDecline);
                    _buttons.Remove(o.mailId);
                }
            }
            return running;
        }

        /// <summary>
        /// AC-19.5: the question the hub asks before the feature is switched off; null when no order is running (the hub then switches off at once). It says how many orders are running and
        /// how far the reputation falls.
        /// </summary>
        public GpuDisableConfirmation BuildDisableConfirmation()
        {
            int running = ActiveCount;
            if (running <= 0) return null;
            return new GpuDisableConfirmation
            {
                MessageKey = "gpu.console.disableConfirm",
                Args = new[] { running.ToString(CultureInfo.InvariantCulture), Points(PenaltyFor(running)) },
                ConfirmLabelKey = "gpu.console.disableConfirm.yes",
            };
        }

        // ---- the demand of the cards (F29) ----

        // What the running orders ask of a card in a day: the sum of their volumes.
        private int DemandOf(string productId)
        {
            long sum = 0L;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o != null && o.status == GpuState.OrderActive && o.productId == productId) sum += Math.Max(0, o.unitsPerDay);
            }
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        private void ReleaseDemand(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            _production.SetContractDemand(productId, DemandOf(productId));
        }

        private void RefreshDemand()
        {
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o != null && o.status == GpuState.OrderActive) _production.SetContractDemand(o.productId, DemandOf(o.productId));
            }
        }

        // ---- the view for the tab ----

        /// <summary>The orders as the tab shows them, in the order of the section.</summary>
        public IReadOnlyList<GpuConsoleOrderView> Views()
        {
            var views = new List<GpuConsoleOrderView>();
            GpuBalanceDto.ConsoleBlock rules = Rules;
            foreach (GpuConsoleOrderSave o in OrderList)
            {
                if (o == null) continue;
                GpuConsoleDto record = _content.ConsoleEntry(o.id);
                GpuProductSave card = string.IsNullOrEmpty(o.productId) ? null : _lineup.Find(o.productId);
                DateTime? offered = DateFrom(o.offeredOn);
                var view = new GpuConsoleOrderView
                {
                    Id = o.id,
                    Status = o.status,
                    Client = record != null ? ClientName(record) : o.id,
                    OfferYear = record != null ? record.offerYear : (offered.HasValue ? offered.Value.Year : 0),
                    TermYears = record != null ? record.termYears : 0,
                    ProductId = o.productId ?? string.Empty,
                    ProductName = card != null ? card.name ?? card.id : string.Empty,
                    UnitsPerDay = o.unitsPerDay,
                    Price = o.price,
                    OfferedOn = offered,
                    StartedOn = DateFrom(o.startedOn),
                    EndsOn = DateFrom(o.endsOn),
                    MissedShare = GpuConsoleMath.MissedShare(o.deliveredWindow, o.missedWindow),
                    WindowDays = Math.Max(o.deliveredWindow != null ? o.deliveredWindow.Count : 0, o.missedWindow != null ? o.missedWindow.Count : 0),
                    FailShare = rules.failCancelShare,
                    GraceDays = rules.graceDays,
                };
                if (offered.HasValue && o.status == GpuState.OrderOffered) view.AnswerBy = offered.Value.AddDays(rules.offerTimeoutDays);
                views.Add(view);
            }
            return views;
        }

        // ---- helpers ----

        private GpuConsoleOrderSave ByMail(int mailId)
        {
            if (mailId <= 0) return null;
            return OrderList.Find(o => o != null && o.mailId == mailId);
        }

        private string ClientName(GpuConsoleDto record)
        {
            return GpuNames.Localized(_text, record.clientKey, record.id);
        }

        private void Send(GpuMailLetter letter)
        {
            int id = _mail.Send(letter);
            if (id < 0) WarnOnce("send:" + letter.Tag, "console: the mail service did not take the letter " + letter.Tag);
        }

        private void WarnOnce(string what, string message)
        {
            if (_warn != null && _warned.Add(what)) _warn(message);
        }

        // A number of reputation points: no more than two decimals, no trailing zeros, the invariant culture.
        private static string Points(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static GpuDate DateOf(DateTime date)
        {
            return new GpuDate { y = date.Year, m = date.Month, d = date.Day };
        }

        private static DateTime? DateFrom(GpuDate date)
        {
            if (date == null || date.y < 1 || date.y > 9999 || date.m < 1 || date.m > 12 || date.d < 1 || date.d > DateTime.DaysInMonth(date.y, date.m)) return null;
            return new DateTime(date.y, date.m, date.d);
        }
    }
}
