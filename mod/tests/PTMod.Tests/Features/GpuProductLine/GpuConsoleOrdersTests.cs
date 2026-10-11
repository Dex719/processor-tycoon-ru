using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>The mail service of the core in memory, with the buttons of the letters: what was sent, which button got which state, which letter was redrawn or answered.</summary>
    internal sealed class FakeOrderMail : IGpuOrderMail
    {
        public sealed class Sent
        {
            public GpuMailLetter Letter;
            public DateTime On;
            public int Id;

            public string Tag
            {
                get { return Letter.Tag; }
            }
        }

        public struct Button
        {
            public int MailId;
            public string ChoiceId, ReasonKey;
            public bool Enabled;
        }

        private readonly Func<DateTime> _now;
        public readonly List<Sent> Letters = new List<Sent>();
        public readonly List<Button> Buttons = new List<Button>();
        public readonly List<int> Redrawn = new List<int>();
        public readonly List<KeyValuePair<int, string>> Resolved = new List<KeyValuePair<int, string>>();
        public bool Refuse;

        public FakeOrderMail(Func<DateTime> now)
        {
            _now = now;
        }

        public int Send(GpuMailLetter letter)
        {
            if (Refuse) return -1;
            int id = 100000 + Letters.Count;
            Letters.Add(new Sent { Letter = letter, On = _now(), Id = id });
            return id;
        }

        public bool Exists(string tag)
        {
            return Letters.Any(l => l.Tag == tag);
        }

        public void SetChoiceState(int mailId, string choiceId, bool enabled, string disabledReasonKey)
        {
            Buttons.Add(new Button { MailId = mailId, ChoiceId = choiceId, Enabled = enabled, ReasonKey = disabledReasonKey });
        }

        public void Refresh(int mailId)
        {
            Redrawn.Add(mailId);
        }

        public bool Resolve(int mailId, string choiceId)
        {
            Resolved.Add(new KeyValuePair<int, string>(mailId, choiceId));
            return true;
        }

        public List<Sent> WithPrefix(string prefix)
        {
            return Letters.Where(l => l.Tag.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        public Sent Offer(string orderId)
        {
            return Letters.FirstOrDefault(l => l.Tag == GpuConsoleOrders.TagPrefix + orderId);
        }

        /// <summary>The last state given to the button "Принять" of a letter; null when it was never touched.</summary>
        public Button? LastAccept(int mailId)
        {
            for (int i = Buttons.Count - 1; i >= 0; i--)
            {
                if (Buttons[i].MailId == mailId && Buttons[i].ChoiceId == GpuConsoleOrders.ChoiceAccept) return Buttons[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Console orders (TSK-049; FR-19, F35, AC-19.1 to AC-19.5) on the shipped content, the services of the feature, the fake game and a mail service in memory: the offer of the year (its
    /// letter, its buttons, its answer), the order that is made on "Принять", the deliveries before the shop, the breach and the other ways an order ends, the question before the feature is
    /// switched off, the save and the load.
    /// </summary>
    [Covers(typeof(GpuConsoleOrders), typeof(IGpuOrderMail), typeof(GpuDisableConfirmation), typeof(GpuConsoleOrderView), typeof(GpuConsoleRow), typeof(GpuMailChoice), typeof(GpuMailAction))]
    public class GpuConsoleOrdersTests
    {
        private sealed class Rig
        {
            public readonly GpuWorld World;
            public readonly FakeOrderMail Mail;
            public readonly GpuConsoleOrders Orders;
            public readonly TestGpuText Text;
            public bool SwitchOn = true;
            public bool CanSend = true;

            /// <param name="date">The day of the test.</param>
            /// <param name="founded">The division is founded.</param>
            /// <param name="content">The content to run on; the shipped content with the market of the eras when null.</param>
            public Rig(DateTime date, bool founded = true, string language = "ru", GpuContentDb content = null)
            {
                World = new GpuWorld(date, 42, content ?? GpuWorld.ShippedEras());
                World.Game.AddTimelineNodes();
                World.Game.FrontierKHz = 250000f;   // the CPUs of 1997-1998: the background cards are the cards of the time, not of 2005
                if (founded) World.Founded();
                World.Market.SeedForYear(date.Year);
                Text = new TestGpuText(language);
                Mail = new FakeOrderMail(() => World.Game.Date);
                Orders = new GpuConsoleOrders(World.Content, World.State, World.Game, World.Production, World.Market, World.Lineup, Mail, Text, () => SwitchOn, () => CanSend, World.Warnings.Add);
                World.Lineup.Retired += Orders.OnProductRetired;
            }

            public FakeGpuGame Game
            {
                get { return World.Game; }
            }

            public DateTime Today
            {
                get { return World.Game.Date; }
            }

            /// <summary>A card of the player on the market (the design of AC-3.1: arch.shader, 90 nm, 300 mm2, 256 MB), with the given lines.</summary>
            public GpuProductSave Card(string id = "g-1", int lines = 0, float perf = -1f)
            {
                GpuProductSave card = World.AddCard(id, lines);
                card.price = 300f;
                if (perf >= 0f) card.stats = Clone(card.stats, perf);
                return card;
            }

            // The cards of the helper share the snapshot of one design: a card with a performance of its own gets a copy of its own.
            private static GpuStatsSave Clone(GpuStatsSave stats, double perf)
            {
                var copy = new GpuStatsSave();
                foreach (System.Reflection.FieldInfo field in typeof(GpuStatsSave).GetFields())
                {
                    object value = field.GetValue(stats);
                    var list = value as List<string>;
                    field.SetValue(copy, list != null ? new List<string>(list) : value);
                }
                copy.effectivePerf = perf;
                return copy;
            }

            public GpuConsoleOrderSave Order(string id)
            {
                return World.State.Data.consoleOrders.Find(o => o.id == id);
            }

            /// <summary>The offer "sony.2" on the first day of 1998, the letter in the fake mail.</summary>
            public GpuConsoleOrderSave Offered(string id = "sony.2")
            {
                Orders.DayTick();
                GpuConsoleOrderSave order = Order(id);
                Assert.NotNull(order, "the offer " + id + " was made (" + string.Join("; ", World.Warnings.ToArray()) + ")");
                return order;
            }

            /// <summary>An order that runs: made by hand in the section, started <paramref name="daysAgo"/> days ago.</summary>
            public GpuConsoleOrderSave Active(string id, string productId, int units, float price, int daysAgo = 10, int termYears = 6, int[] delivered = null, int[] missed = null)
            {
                DateTime started = Today.AddDays(-daysAgo);
                var order = new GpuConsoleOrderSave
                {
                    id = id,
                    status = GpuState.OrderActive,
                    productId = productId,
                    mailId = 100000 + World.State.Data.consoleOrders.Count + 50,
                    offeredOn = Date(started),
                    startedOn = Date(started),
                    endsOn = Date(started.AddYears(termYears)),
                    unitsPerDay = units,
                    price = price,
                    deliveredWindow = delivered != null ? new List<int>(delivered) : new List<int>(),
                    missedWindow = missed != null ? new List<int>(missed) : new List<int>(),
                };
                World.State.Data.consoleOrders.Add(order);
                return order;
            }

            public void RunDay()
            {
                World.Production.ProduceDay();
                Orders.DeliverDay();
                World.Market.SellDay();
                World.Market.DayTick();
                World.Lineup.DayTick();
                Orders.DayTick();
                World.Game.Factory.ResetDay();
                World.NextDay();
                if (World.Game.Date.Day == 1) World.MonthTicks();
            }

            public string[] Reasons()
            {
                return World.Game.Reputation.Select(r => r.Reason).ToArray();
            }
        }

        private static GpuDate Date(DateTime date)
        {
            return new GpuDate { y = date.Year, m = date.Month, d = date.Day };
        }

        private static DateTime Jan1(int year)
        {
            return new DateTime(year, 1, 1);
        }

        // ---- AC-19.1: the offer of the year ----

        [Test]
        public void Ac191_InTheYearOfTheOffer_TheLetterOfSonyComes_WithAnActiveAcceptButton()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            rig.Orders.DayTick();

            Assert.Equal(1, rig.Mail.Letters.Count, "one letter");
            FakeOrderMail.Sent sent = rig.Mail.Offer("sony.2");
            Assert.NotNull(sent, "the tag gpu:console:sony.2");
            GpuMailLetter letter = sent.Letter;
            Assert.Equal(MailPriority.Critical, letter.Priority);
            Assert.Equal(60, letter.TimeoutDays, "the player has 60 days to answer");
            Assert.Equal("decline", letter.DefaultChoiceId, "a letter nobody answers is declined");
            Assert.Equal("gpu.console.offer.subject", letter.TitleKey);
            Assert.Equal("gpu.console.offer.body", letter.BodyKey);
            Assert.Equal("Sony", letter.Args[0], "the real maker");

            Assert.Equal(new[] { "accept", "decline" }, letter.Choices.Select(c => c.Id).ToArray());
            GpuMailChoice accept = letter.Choices[0];
            Assert.True(accept.Enabled, "AC-19.1: the button is active - a card of the player is fast enough");
            Assert.Equal(string.Empty, accept.DisabledReasonKey);
            Assert.True(letter.Choices[1].Enabled);
            Assert.Equal(1, letter.Actions.Count);
            Assert.Equal("openLineup", letter.Actions[0].Id);
            Assert.True(letter.Actions[0].Repeatable, "it never closes the letter");

            GpuConsoleOrderSave order = rig.Order("sony.2");
            Assert.Equal(GpuState.OrderOffered, order.status);
            Assert.Equal(sent.Id, order.mailId, "the id the core gave is kept in the section (mailId)");
            Assert.Equal(1998, order.offeredOn.y);
            Assert.Equal(0, rig.World.Game.Notes.Count);
            Assert.Equal(0, rig.World.Game.Reputation.Count);
        }

        [Test]
        public void TheLetter_IsInBothLanguages_AndEveryPlaceholderGetsAnArgument()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var rig = new Rig(Jan1(1998), true, language);
                rig.Card();
                rig.Orders.DayTick();
                GpuMailLetter letter = rig.Mail.Offer("sony.2").Letter;

                Assert.Equal(8, letter.Args.Length, language);
                foreach (string key in new[] { letter.TitleKey, letter.BodyKey, "gpu.console.accept", "gpu.console.decline", "gpu.console.openLineup", "gpu.console.noproduct" })
                    Assert.True(rig.Text.Has(key), language + ": no text for " + key);

                string title, body;
                Assert.True(Raw(language, letter.TitleKey, out title), language + " title");
                Assert.True(Raw(language, letter.BodyKey, out body), language + " body");
                Assert.True(Highest(title) < letter.Args.Length && Highest(body) < letter.Args.Length, language + ": a placeholder without an argument");
                Assert.Equal(letter.Args.Length - 1, Highest(body), language + ": every argument is in the text");
                Assert.False(letter.Args.Any(a => string.IsNullOrWhiteSpace(a)), language + ": an argument is empty");
                Assert.Equal("Sony", letter.Args[0]);
                Assert.Equal("90", letter.Args[5], "the days of the window");
                Assert.Equal("3", letter.Args[7], "the penalty");
            }
        }

        private static bool Raw(string language, string key, out string text)
        {
            return TestGpuText.Tables().TryGet(language, key, out text);
        }

        private static int Highest(string text)
        {
            int highest = -1;
            foreach (Match match in Regex.Matches(text, @"\{(\d+)\}")) highest = Math.Max(highest, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            return highest;
        }

        [Test]
        public void TheYearOfTheOffer_Counts_NotTheDay()
        {
            var rig = new Rig(new DateTime(1997, 12, 31));
            rig.Card();
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count, "a year too early");

            rig.World.Game.Date = new DateTime(1998, 7, 15);
            rig.Orders.DayTick();
            Assert.NotNull(rig.Mail.Offer("sony.2"), "the division, the card and the day are there, the year is the year of the offer");

            rig.World.Game.Date = new DateTime(1999, 1, 1);
            rig.Orders.DayTick();
            Assert.NotNull(rig.Mail.Offer("microsoft.1"), "the next year, the next maker");
            Assert.Equal(2, rig.Mail.Letters.Count, "and the offer of 1998 is not made again");
        }

        [Test]
        public void AnOffer_IsMadeOnce_ForEachRecord()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            for (int i = 0; i < 5; i++)
            {
                rig.Orders.DayTick();
                rig.World.NextDay();
            }
            Assert.Equal(1, rig.Mail.Letters.Count);
            Assert.Equal(1, rig.World.State.Data.consoleOrders.Count);

            rig.Order("sony.2").status = GpuState.OrderDeclined;
            rig.Orders.DayTick();
            Assert.Equal(1, rig.Mail.Letters.Count, "a declined offer does not come back in its year");
        }

        [Test]
        public void NoOffer_WithoutTheDivision_WithTheSwitchOff_OrInAQuietTime()
        {
            var rig = new Rig(Jan1(1998), false);
            rig.Card();
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count, "the division is not founded");

            rig.World.Founded();
            rig.SwitchOn = false;
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count, "the switch consoleOrders is off");

            rig.SwitchOn = true;
            rig.CanSend = false;
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count, "the game is fast-forwarding or loading: no order is made");
            Assert.Equal(0, rig.World.State.Data.consoleOrders.Count);

            rig.CanSend = true;
            rig.Orders.DayTick();
            Assert.Equal(1, rig.Mail.Letters.Count, "and when the game goes on, the offer of the year goes out");
        }

        [Test]
        public void TheSwitchOff_StopsTheOffers_NotTheRunningOrders()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 5000;
            rig.Active("sony.2", card.id, 1000, 100f);
            rig.SwitchOn = false;

            rig.Orders.DeliverDay();
            Assert.Equal(4000, card.stock, "an order that runs is delivered whatever the switch says");
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count);
        }

        [Test]
        public void WithoutACard_TheOfferIsStillMade_WithAGrayButton()
        {
            var rig = new Rig(Jan1(1998));
            rig.Orders.DayTick();
            GpuMailChoice accept = rig.Mail.Offer("sony.2").Letter.Choices[0];
            Assert.False(accept.Enabled);
            Assert.Equal("gpu.console.noproduct", accept.DisabledReasonKey, "AC-19.2: Нет подходящей карты");
            Assert.True(rig.Mail.Offer("sony.2").Letter.Choices[1].Enabled, "declining is always possible");
        }

        [Test]
        public void TheOffer_FallsBackToTheConsumerSegment_InTheSingleMode()
        {
            var rig = new Rig(Jan1(1998), true, "ru", GpuWorld.Shipped());
            rig.Card();
            rig.Orders.DayTick();
            Assert.Equal("consumer", rig.Orders.TargetSegment(rig.Today).id, "gaming.mid is not in this mode");
            Assert.True(rig.Mail.Offer("sony.2").Letter.Choices[0].Enabled);
        }

        // ---- AC-19.2: the button follows the cards ----

        [Test]
        public void Ac192_TheButtonTurnsOn_AWeekAfterAFastEnoughCardAppears_WithoutClosingTheLetter()
        {
            var rig = new Rig(Jan1(1998));
            rig.Orders.DayTick();
            GpuConsoleOrderSave order = rig.Order("sony.2");
            Assert.False(rig.Mail.Offer("sony.2").Letter.Choices[0].Enabled);

            // a card that is too slow does not help
            GpuProductSave slow = rig.Card("g-1", 0, 1f);
            for (int day = 0; day < 8; day++)
            {
                rig.World.NextDay();
                rig.Orders.DayTick();
            }
            Assert.False(rig.Mail.LastAccept(order.mailId).HasValue && rig.Mail.LastAccept(order.mailId).Value.Enabled, "1 is nowhere near 60 % of the best card of the time");

            // the fast one
            rig.Card("g-2");
            int redrawn = rig.Mail.Redrawn.Count;
            for (int day = 0; day < 8; day++)
            {
                rig.World.NextDay();
                rig.Orders.DayTick();
            }
            FakeOrderMail.Button? button = rig.Mail.LastAccept(order.mailId);
            Assert.True(button.HasValue && button.Value.Enabled, "the button is active again");
            Assert.True(rig.Mail.Redrawn.Count > redrawn && rig.Mail.Redrawn.Contains(order.mailId), "and the open letter is drawn again");
            Assert.Equal(1, rig.Mail.Letters.Count, "no second letter");
            Assert.Equal(GpuState.OrderOffered, order.status, "the offer is open all along");
            Assert.Equal(0, rig.Mail.Resolved.Count, "the letter is not closed");
            Assert.True(slow != null);
        }

        [Test]
        public void TheButtonIsLookedAtOnceInRevalidateDays_NotEveryDay()
        {
            var rig = new Rig(Jan1(1998));
            rig.Orders.DayTick();   // the first look, the offer
            int calls = rig.Mail.Buttons.Count;
            rig.Card();
            for (int day = 1; day <= 6; day++)
            {
                rig.World.NextDay();
                rig.Orders.DayTick();
            }
            Assert.Equal(calls, rig.Mail.Buttons.Count, "six days after: not yet");
            rig.World.NextDay();
            rig.Orders.DayTick();
            Assert.Equal(calls + 1, rig.Mail.Buttons.Count, "the seventh day");
            Assert.True(rig.Mail.Buttons.Last().Enabled);
        }

        [Test]
        public void AnUnchangedButton_IsNotGivenToTheCoreAgain()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            rig.Orders.DayTick();
            for (int day = 0; day < 40; day++)
            {
                rig.World.NextDay();
                rig.Orders.DayTick();
            }
            Assert.Equal(0, rig.Mail.Buttons.Count, "the state of the letter at the sending was right and stayed right");
            Assert.Equal(0, rig.Mail.Redrawn.Count);
        }

        [Test]
        public void ACardThatLacksAFunctionOfTheSegment_DoesNotQualify()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stats.features = new List<string> { "2d", "gui" };   // gaming.mid asks for 3d from 1996
            rig.Orders.DayTick();
            Assert.False(rig.Mail.Offer("sony.2").Letter.Choices[0].Enabled, "a 2D card fast as it is is no card for a 3D console");
        }

        [Test]
        public void OnlyTheCardsOnSale_Qualify()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave ready = rig.Card("g-1");
            ready.status = GpuState.StatusReady;
            GpuProductSave paused = rig.Card("g-2");
            paused.sell = false;
            GpuProductSave retired = rig.Card("g-3");
            retired.status = GpuState.StatusRetired;
            Assert.Null(rig.Orders.BestCandidate(rig.Today), "a card that waits for its release, one that is not offered and one in the archive");

            paused.sell = true;
            Assert.Equal("g-2", rig.Orders.BestCandidate(rig.Today).id);
        }

        [Test]
        public void TheBar_IsTheBestCardOnSaleInTheSegment_TheVendorsCountOnlyWhileTheyTakePart()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card("g-1", 0, 50000f);
            GpuSegmentDto seg = rig.World.Content.Segment("gaming.mid");
            Func<string, bool, GpuProductSave> vendorCard = (id, listed) => new GpuProductSave
            {
                id = id, owner = GpuState.OwnerVendor, vendorId = "nvidia", status = GpuState.StatusSelling, sell = true, price = 100f,
                stats = new GpuStatsSave { effectivePerf = 1000000d, features = new List<string> { "2d", "gui", "3d" } },
                segIds = listed ? new List<string> { "gaming.mid" } : new List<string> { "office2d" }, pop = new List<float> { 0.5f },
            };
            rig.World.State.Data.products.Add(vendorCard("v-1", true));

            double alone = rig.World.Market.BestPerfIn(seg, 1998);
            Assert.True(alone >= 50000d && alone < 1000000d, "the switch 'rivals' is off: the card of the maker does not sell, the bar is mine or the background's: " + alone);
            Assert.Equal("g-1", rig.Orders.BestCandidate(rig.Today).id);

            rig.World.RivalsOn = true;
            Assert.Equal(1000000d, rig.World.Market.BestPerfIn(seg, 1998), "the maker takes part: its card sets the bar");
            Assert.Null(rig.Orders.BestCandidate(rig.Today), "and 50 000 is far under 60 % of a million");

            rig.World.State.Data.products.RemoveAll(p => p.id == "v-1");
            rig.World.State.Data.products.Add(vendorCard("v-2", false));
            Assert.True(rig.World.Market.BestPerfIn(seg, 1998) < 1000000d, "a card that is not listed in the segment does not sell there, it sets no bar");
            Assert.Equal("g-1", rig.Orders.BestCandidate(rig.Today).id);

            rig.World.State.Data.products.Find(p => p.id == "v-2").status = GpuState.StatusRetired;
            rig.World.State.Data.products.Find(p => p.id == "g-1").sell = false;
            Assert.True(rig.World.Market.BestPerfIn(seg, 1998) < 50000d, "a card taken off the shelf sets no bar");
        }

        [Test]
        public void TheBestCard_IsTheOneTiedToTheOrder()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave good = rig.Card("g-1", 0, 90000f);
            GpuProductSave best = rig.Card("g-2", 0, 120000f);
            GpuProductSave tie = rig.Card("g-3", 0, 120000f);
            Assert.Equal("g-2", rig.Orders.BestCandidate(rig.Today).id, "the faster one; the earlier on a tie");
            Assert.True(good != null && best != null && tie != null);
        }

        [Test]
        public void TheButton_IsComputedAgainWhenTheLetterIsDelivered_AndAfterALoad()
        {
            var rig = new Rig(Jan1(1998));
            rig.Orders.DayTick();
            GpuConsoleOrderSave order = rig.Order("sony.2");
            Assert.False(rig.Mail.Offer("sony.2").Letter.Choices[0].Enabled);

            // a card came before the letter reached the inbox
            rig.Card();
            rig.Orders.OnMailDelivered(order.mailId);
            Assert.True(rig.Mail.LastAccept(order.mailId).Value.Enabled, "the delivery looks again");
            rig.Orders.OnMailDelivered(order.mailId);
            int count = rig.Mail.Buttons.Count;
            rig.Orders.OnMailDelivered(999);   // a letter that is no order of ours
            Assert.Equal(count, rig.Mail.Buttons.Count);

            // a load: the core's record of the letter is old, the cards are the cards of the save
            rig.Mail.Buttons.Clear();
            rig.Mail.Redrawn.Clear();
            rig.Orders.SessionStart();
            Assert.Equal(1, rig.Mail.Buttons.Count, "GameReady: the state of the button is given again");
            Assert.True(rig.Mail.Buttons[0].Enabled);
            Assert.Equal(order.mailId, rig.Mail.Buttons[0].MailId);
            Assert.True(rig.Mail.Redrawn.Contains(order.mailId));
        }

        [Test]
        public void AfterALoad_TheOrdersAreReadFromTheSave_AndTheButtonsComeBackRight()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            rig.Orders.DayTick();
            GpuConsoleOrderSave offer = rig.Order("sony.2");
            rig.Active("nintendo.4", card.id, 700, 99.5f, 20, 5, new[] { 700, 700, 500 }, new[] { 0, 0, 200 });
            JsonObject written = rig.World.State.Write();

            // another session: a world with an empty state that reads the section, and a service on it
            var world = new GpuWorld(Jan1(1998), 42, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.State.Read(written, 1);
            var mail = new FakeOrderMail(() => world.Game.Date);
            var orders = new GpuConsoleOrders(world.Content, world.State, world.Game, world.Production, world.Market, world.Lineup, mail, rig.Text, () => true, () => true, null);

            Assert.Equal(2, orders.Orders.Count);
            GpuConsoleOrderSave loadedOffer = orders.Find("sony.2");
            Assert.Equal(offer.mailId, loadedOffer.mailId, "the id of the letter is in the save (mailId)");
            Assert.Equal(GpuState.OrderOffered, loadedOffer.status);
            GpuConsoleOrderSave loadedActive = orders.Find("nintendo.4");
            Assert.Equal(GpuState.OrderActive, loadedActive.status);
            Assert.Equal(card.id, loadedActive.productId);
            Assert.Equal(new[] { 700, 700, 500 }, loadedActive.deliveredWindow.ToArray());
            Assert.Equal(new[] { 0, 0, 200 }, loadedActive.missedWindow.ToArray());
            Assert.Equal(99.5f, loadedActive.price);
            Assert.Equal(700, loadedActive.unitsPerDay);
            Assert.Equal(1, orders.ActiveCount);
            Assert.Equal(0, world.Warnings.Count, string.Join("; ", world.Warnings.ToArray()));

            // GameReady(Loaded): the core's record of the letter says what the cards were when the letter was sent; now the button is computed from the cards of the save
            Assert.Equal(0, mail.Buttons.Count);
            orders.SessionStart();
            Assert.Equal(1, mail.Buttons.Count, "one open offer, one button");
            Assert.Equal(offer.mailId, mail.Buttons[0].MailId);
            Assert.Equal("accept", mail.Buttons[0].ChoiceId);
            Assert.True(mail.Buttons[0].Enabled, "the card of the save is fast enough");
            Assert.Equal(new[] { offer.mailId }, mail.Redrawn.ToArray());
            Assert.Equal(700, world.Production.ContractDemandOf(card.id), "and the running order asks its card for its volume again");

            // the card of the save is gone from the shelf: the button is gray after the load
            world.State.Data.products.Find(p => p.id == card.id).sell = false;
            var again = new FakeOrderMail(() => world.Game.Date);
            var orders2 = new GpuConsoleOrders(world.Content, world.State, world.Game, world.Production, world.Market, world.Lineup, again, rig.Text, () => true, () => true, null);
            orders2.SessionStart();
            Assert.False(again.Buttons.Single().Enabled);
            Assert.Equal("gpu.console.noproduct", again.Buttons.Single().ReasonKey);
        }

        // ---- the answer ----

        [Test]
        public void Accepting_TiesTheBestCard_AndFixesThePriceTheVolumeAndTheTerm()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card("g-1", 2);
            rig.World.Production.ProduceDay();
            float unitCost = rig.World.Production.UnitCostOf("g-1");
            Assert.True(unitCost > 0f, "the card has been produced: it has a cost");

            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "accept");

            GpuSegmentDto seg = rig.World.Content.Segment("gaming.mid");
            float expectedPrice = Math.Max(1.15f * unitCost, 0.55f * rig.World.Market.BudgetOf(seg, 1998));
            int expectedUnits = GpuConsoleMath.DailyUnits(rig.World.Market.DailyBuyersOf(seg, 1998), rig.World.Content.Balance.console.unitsShare);
            Assert.Equal(GpuState.OrderActive, order.status);
            Assert.Equal("g-1", order.productId);
            Assert.Near(expectedPrice, order.price, 1e-3, "max(1.15 x U, 0.55 x Budget)");
            Assert.Equal(expectedUnits, order.unitsPerDay, "floor(0.15 x N_s)");
            Assert.True(order.unitsPerDay > 100, "a volume worth the name: " + order.unitsPerDay);
            Assert.Equal(1998, order.startedOn.y);
            Assert.Equal(2004, order.endsOn.y, "6 years (sony.2)");
            Assert.Equal(1, order.endsOn.m);
            Assert.Equal(0, order.deliveredWindow.Count + order.missedWindow.Count);
            Assert.Equal(1, rig.Orders.ActiveCount);
            Assert.Equal(1, rig.Orders.ActiveFor("g-1"));
            Assert.Equal(1, rig.World.Game.Notes.Count, "the player is told");
            Assert.Equal("gpu.console.accepted", rig.World.Game.Notes[0].Key);
            Assert.Equal("Sony", rig.World.Game.Notes[0].Argument);
            Assert.Equal(expectedUnits, rig.World.Production.ContractDemandOf("g-1"), "the automatic mode of the card sees the order");
            Assert.True(card != null);
        }

        [Test]
        public void ThePrice_IsNeverBelowTheCostWithTheMarkup()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card("g-1", 2);
            rig.World.Production.ProduceDay();
            rig.World.Game.UnitCostMul = 40f;   // the cards became expensive to make
            rig.World.Production.ProduceDay();
            float unitCost = rig.World.Production.UnitCostOf("g-1");
            GpuSegmentDto seg = rig.World.Content.Segment("gaming.mid");
            Assert.True(1.15f * unitCost > 0.55f * rig.World.Market.BudgetOf(seg, 1998), "the test: the cost wins over the budget (" + unitCost + ")");

            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "accept");
            Assert.Near(1.15f * unitCost, order.price, 1e-2);
        }

        [Test]
        public void ACardNotYetProduced_IsPricedByTheBudgetAlone()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card("g-1", 0);
            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "accept");
            GpuSegmentDto seg = rig.World.Content.Segment("gaming.mid");
            Assert.Near(0.55f * rig.World.Market.BudgetOf(seg, 1998), order.price, 1e-3);
        }

        [Test]
        public void Declining_ClosesTheOffer_WithoutAPenalty()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "decline");
            Assert.Equal(GpuState.OrderDeclined, order.status);
            Assert.Equal(0, rig.World.Game.Reputation.Count);
            Assert.Equal(0, rig.Orders.ActiveCount);
            Assert.Equal(string.Empty, order.productId ?? string.Empty);
        }

        [Test]
        public void WhenNoOneAnswers_TheCoreDeclines_AndTheHandlerClosesTheOffer()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "decline");   // what the core does at the end of the 60 days: the default choice
            rig.Orders.OnChoice(order.mailId, "accept");     // and a late click on an answered letter changes nothing
            Assert.Equal(GpuState.OrderDeclined, order.status);
        }

        [Test]
        public void Accepting_WhenNoCardIsLeft_CancelsTheOffer_WithoutAPenalty_AndTellsThePlayer()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            GpuConsoleOrderSave order = rig.Offered();
            Assert.True(rig.Mail.Offer("sony.2").Letter.Choices[0].Enabled);
            card.status = GpuState.StatusRetired;   // the card went between the last look and the click

            rig.Orders.OnChoice(order.mailId, "accept");
            Assert.Equal(GpuState.OrderCancelled, order.status);
            Assert.Equal(0, rig.World.Game.Reputation.Count, "no penalty for an order that never was");
            Assert.Equal("gpu.console.accept.failed", rig.World.Game.Notes.Single().Key);
            Assert.Equal(0, rig.Orders.ActiveCount);
        }

        [Test]
        public void AnAnswerToALetterThatIsNoOrder_OrToAnOrderThatIsOver_ChangesNothing()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(424242, "accept");
            Assert.Equal(GpuState.OrderOffered, order.status);
            Assert.Equal(1, rig.World.Warnings.Count(w => w.Contains("424242")), "told once");
            rig.Orders.OnChoice(424242, "accept");
            Assert.Equal(1, rig.World.Warnings.Count(w => w.Contains("424242")), "and only once");

            rig.Orders.OnChoice(order.mailId, "maybe");
            Assert.Equal(GpuState.OrderOffered, order.status, "an answer the feature does not know");

            order.status = GpuState.OrderCancelled;
            rig.Orders.OnChoice(order.mailId, "accept");
            Assert.Equal(GpuState.OrderCancelled, order.status);
            Assert.Equal(0, rig.Orders.ActiveCount);
        }

        // ---- AC-19.3: the delivery ----

        [Test]
        public void Ac193_AStockOf1500_AgainstAnOrderOf2000_DeliversAll_PaysContracts_AndWritesTheMissedHundreds()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 1500;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 2000, 100f);

            rig.Orders.DeliverDay();

            Assert.Equal(0, card.stock, "1500 delivered");
            Assert.Near(150000.0, rig.Game.EarnedOn(GpuRevenue.Contracts), 0.5, "1500 x 100 under Contracts");
            Assert.Equal(0f, rig.Game.EarnedOn(GpuRevenue.Sales), "and nothing under Sales");
            Assert.Equal(new[] { 1500 }, order.deliveredWindow.ToArray());
            Assert.Equal(new[] { 500 }, order.missedWindow.ToArray(), "the shortfall of 500 is written to the window of 90 days");
            Assert.Equal(GpuState.OrderActive, order.status, "one bad day is no breach");
            Assert.Equal(1500, card.ledger.sold[0], "the books of the card");
            Assert.Near(150000.0, card.ledger.income[0], 0.5);
        }

        [Test]
        public void ADayWithEnoughStock_DeliversTheWholeVolume()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 5000;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 2000, 100f);
            rig.Orders.DeliverDay();
            Assert.Equal(3000, card.stock);
            Assert.Near(200000.0, rig.Game.EarnedOn(GpuRevenue.Contracts), 0.5);
            Assert.Equal(new[] { 2000 }, order.deliveredWindow.ToArray());
            Assert.Equal(new[] { 0 }, order.missedWindow.ToArray());
        }

        [Test]
        public void TheDeliveryWindow_KeepsTheLastNinetyDays()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 1000000;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 100, 10f);
            for (int day = 0; day < 130; day++) rig.Orders.DeliverDay();
            Assert.Equal(90, order.deliveredWindow.Count);
            Assert.Equal(90, order.missedWindow.Count);
            Assert.Equal(1000000 - 13000, card.stock);
        }

        [Test]
        public void WithoutARunningOrder_TheDeliveryDoesNothing()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 1000;
            rig.Orders.DeliverDay();
            Assert.Equal(1000, card.stock);
            Assert.Equal(0, rig.Game.Earned.Count);

            rig.Active("sony.2", card.id, 100, 10f).status = GpuState.OrderOffered;
            rig.Orders.DeliverDay();
            Assert.Equal(1000, card.stock, "an offer delivers nothing");
        }

        [Test]
        public void TheCardsOfAnOrder_ComeOutOfTheStock_BeforeTheShop()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card("g-1", 1);
            rig.World.Production.ProduceDay();
            GpuProductSave card = rig.World.State.Data.products.Find(p => p.id == "g-1");
            card.stock = 1200;
            card.price = 100f;   // under the budget: it sells
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 80f);

            rig.Orders.DeliverDay();
            Assert.Equal(200, card.stock, "the order took its 1000 first");
            int sold = rig.World.Market.SoldLast(card.id, 1);
            rig.World.Market.SellDay();
            rig.World.Market.DayTick();
            int shop = rig.World.Market.SoldLast(card.id, 1);
            Assert.True(shop <= 200, "the shop sees what the order left: " + shop);
            Assert.True(shop >= 0 && sold == 0 && order != null);
        }

        [Test]
        public void TheRevenue_DoesNotGoThroughTheChannelOfTheContracts()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 100;
            rig.Active("sony.2", card.id, 100, 10f);
            rig.Game.RatingMul = 7f;
            rig.Game.UnitCostMul = 7f;
            rig.Game.OutputMul = 7f;
            rig.Orders.DeliverDay();
            Assert.Near(1000.0, rig.Game.EarnedOn(GpuRevenue.Contracts), 1e-3, "price x delivered, with no multiplier of any channel");
        }

        // ---- AC-19.4: the breach ----

        private static int[] Days(int count, int value)
        {
            return Enumerable.Repeat(value, count).ToArray();
        }

        [Test]
        public void Ac194_AThirtyPercentMiss_OverNinetyDays_CancelsTheOrder_LowersTheReputation_AndSendsALetter()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 700;
            // 89 days behind: 700 delivered and 300 missed each day; today is the 90th, the same
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f, 89, 6, Days(89, 700), Days(89, 300));

            rig.Orders.DeliverDay();

            Assert.Equal(GpuState.OrderCancelled, order.status);
            Assert.Equal(-3f, rig.Game.ReputationDelta(), "reputationPenalty");
            Assert.Equal(new[] { "gpu.console.failed" }, rig.Reasons(), "the reason is the key of the feature");
            FakeOrderMail.Sent letter = rig.Mail.WithPrefix("gpu:console:sony.2:").Single();
            Assert.Equal("gpu:console:sony.2:failed", letter.Tag);
            Assert.Equal(MailPriority.Normal, letter.Letter.Priority);
            Assert.Equal("gpu.console.mail.failed.subject", letter.Letter.TitleKey);
            Assert.Equal("Sony", letter.Letter.Args[0]);
            Assert.Equal("30%", letter.Letter.Args[2]);
            Assert.Equal("3", letter.Letter.Args[3]);
            Assert.Equal(0, rig.Orders.ActiveCount);
            Assert.Equal(0, rig.World.Production.ContractDemandOf(card.id), "the card is free of the demand of the order");
        }

        [Test]
        public void TheOrder_IsNotCancelledBeforeNinetyDays_WhateverWasMissed()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 0;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f, 87, 6, Days(87, 0), Days(87, 1000));
            rig.Orders.DeliverDay();   // the 88th day: everything missed so far, but the window is not full
            Assert.Equal(GpuState.OrderActive, order.status);
            rig.World.NextDay();
            rig.Orders.DeliverDay();   // the 89th
            Assert.Equal(GpuState.OrderActive, order.status);
            rig.World.NextDay();
            rig.Orders.DeliverDay();   // the 90th
            Assert.Equal(GpuState.OrderCancelled, order.status);
            Assert.Equal(-3f, rig.Game.ReputationDelta());
        }

        [Test]
        public void TheOrder_IsNotCancelledAtExactlyTheLimit_NorBelow()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 750;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f, 120, 6, Days(89, 750), Days(89, 250));
            rig.Orders.DeliverDay();   // 750 of 1000 again: 25 % over the window, exactly the limit
            Assert.Equal(GpuState.OrderActive, order.status, "over 25 %, not at 25 %");
            Assert.Equal(0, rig.Game.Reputation.Count);

            card.stock = 800;
            rig.World.NextDay();
            rig.Orders.DeliverDay();
            Assert.Equal(GpuState.OrderActive, order.status, "and less than the limit");
        }

        [Test]
        public void ABadSpell_IsForgiven_WhenItFallsOutOfTheWindow()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 1000000;
            // the first 20 days: nothing delivered (every day missed, 22 % of the 90 days of the window); then it recovers
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f, 20, 6, Days(20, 0), Days(20, 1000));
            for (int day = 0; day < 200; day++)
            {
                rig.World.NextDay();
                rig.Orders.DeliverDay();
                Assert.Equal(GpuState.OrderActive, order.status, "day " + day);
            }
            Assert.Near(0d, GpuConsoleMath.MissedShare(order.deliveredWindow, order.missedWindow), 1e-9, "the window forgot");
        }

        [Test]
        public void ADamagedOrder_WithoutItsCard_IsCancelledQuietly()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            GpuConsoleOrderSave order = rig.Active("sony.2", "g-404", 100, 10f);
            rig.Orders.DeliverDay();
            Assert.Equal(GpuState.OrderCancelled, order.status);
            Assert.Equal(0, rig.Game.Reputation.Count, "nobody to blame");
            Assert.Equal(0, rig.Mail.Letters.Count);
        }

        // ---- the card is taken off production ----

        [Test]
        public void RetiringTheTiedCard_CancelsTheOrder_WithThePenaltyAndALetter()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card("g-1");
            GpuProductSave other = rig.Card("g-2");
            rig.Active("sony.2", "g-1", 500, 100f);
            rig.Active("nintendo.4", "g-2", 500, 100f);

            rig.World.Lineup.Retire("g-1");

            Assert.Equal(GpuState.OrderCancelled, rig.Order("sony.2").status);
            Assert.Equal(GpuState.OrderActive, rig.Order("nintendo.4").status, "the order of the other card goes on");
            Assert.Equal(-3f, rig.Game.ReputationDelta());
            FakeOrderMail.Sent letter = rig.Mail.WithPrefix("gpu:console:sony.2:").Single();
            Assert.Equal("gpu:console:sony.2:retired", letter.Tag);
            Assert.Equal("gpu.console.mail.retired.body", letter.Letter.BodyKey);
            Assert.Equal(card.name, letter.Letter.Args[1]);
            Assert.True(other != null);
        }

        [Test]
        public void RetiringACardWithNoOrder_CostsNothing()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card("g-1");
            rig.Card("g-2");
            rig.Active("sony.2", "g-2", 500, 100f);
            rig.World.Lineup.Retire("g-1");
            Assert.Equal(0, rig.Game.Reputation.Count);
            Assert.Equal(0, rig.Mail.Letters.Count);
            Assert.Equal(1, rig.Orders.ActiveCount);
        }

        // ---- the term ----

        [Test]
        public void WhenTheTermIsOver_TheOrderEnds_WithALowLetter_AndNoPenalty()
        {
            var rig = new Rig(Jan1(2004));
            GpuProductSave card = rig.Card();
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 500, 100f, 365 * 6 + 2, 6);
            rig.Orders.DayTick();
            Assert.Equal(GpuState.OrderEnded, order.status);
            Assert.Equal(0, rig.Game.Reputation.Count);
            FakeOrderMail.Sent letter = rig.Mail.WithPrefix("gpu:console:sony.2:").Single();
            Assert.Equal("gpu:console:sony.2:ended", letter.Tag);
            Assert.Equal(MailPriority.Low, letter.Letter.Priority);
            Assert.Equal("gpu.console.mail.ended.subject", letter.Letter.TitleKey);

            card.stock = 1000;
            rig.Orders.DeliverDay();
            Assert.Equal(1000, card.stock, "an ended order delivers nothing");
        }

        [Test]
        public void AnOrder_RunsExactlyItsTerm()
        {
            var rig = new Rig(new DateTime(1998, 1, 20));
            GpuProductSave card = rig.Card();
            card.stock = 1000000;
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 10, 10f, 0, 6);
            Assert.Equal(2004, order.endsOn.y);
            rig.World.Game.Date = new DateTime(2004, 1, 19);
            rig.Orders.DayTick();
            Assert.Equal(GpuState.OrderActive, order.status, "the day before");
            rig.World.Game.Date = new DateTime(2004, 1, 20);
            rig.Orders.DayTick();
            Assert.Equal(GpuState.OrderEnded, order.status, "the day of the end");
        }

        // ---- AC-19.5: switching the feature off ----

        [Test]
        public void Ac195_TheQuestionBeforeSwitchingOff_SaysHowManyOrdersRun_AndWhatTheyCost()
        {
            var rig = new Rig(Jan1(1998));
            Assert.Null(rig.Orders.BuildDisableConfirmation(), "no order, nothing to ask");

            GpuProductSave card = rig.Card();
            rig.Active("sony.2", card.id, 500, 100f);
            rig.Active("nintendo.4", card.id, 500, 100f);
            GpuConsoleOrderSave offer = rig.Active("microsoft.1", card.id, 500, 100f);
            offer.status = GpuState.OrderOffered;   // an offer is no order that runs

            GpuDisableConfirmation ask = rig.Orders.BuildDisableConfirmation();
            Assert.NotNull(ask);
            Assert.Equal("gpu.console.disableConfirm", ask.MessageKey);
            Assert.Equal(new[] { "2", "6" }, ask.Args, "two orders, 2 x 3 points of the reputation");
            Assert.Equal("gpu.console.disableConfirm.yes", ask.ConfirmLabelKey);
            Assert.True(rig.Text.Has(ask.MessageKey) && rig.Text.Has(ask.ConfirmLabelKey));

            // asking changes nothing: "Отмена" leaves the feature on and the orders running
            Assert.Equal(2, rig.Orders.ActiveCount);
            Assert.Equal(0, rig.Game.Reputation.Count);
            Assert.Equal(GpuState.OrderOffered, offer.status);
        }

        [Test]
        public void SwitchingOff_CancelsTheRunningOrdersWithThePenalty_AndClosesTheOffers_WithoutLetters()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.stock = 100;
            GpuConsoleOrderSave a = rig.Active("sony.2", card.id, 500, 100f);
            GpuConsoleOrderSave b = rig.Active("nintendo.4", card.id, 500, 100f);
            GpuConsoleOrderSave offer = rig.Active("microsoft.1", card.id, 500, 100f);
            offer.status = GpuState.OrderOffered;
            offer.productId = string.Empty;
            GpuConsoleOrderSave ended = rig.Active("sony.3", card.id, 500, 100f);
            ended.status = GpuState.OrderEnded;

            int running = rig.Orders.CancelAll();

            Assert.Equal(2, running);
            Assert.Equal(GpuState.OrderCancelled, a.status);
            Assert.Equal(GpuState.OrderCancelled, b.status);
            Assert.Equal(GpuState.OrderCancelled, offer.status);
            Assert.Equal(GpuState.OrderEnded, ended.status, "history stays history");
            Assert.Equal(-6f, rig.Game.ReputationDelta(), "3 for each order that ran");
            Assert.Equal(new[] { new KeyValuePair<int, string>(offer.mailId, "decline") }, rig.Mail.Resolved.ToArray(), "the letter of the open offer is closed");
            Assert.Equal(0, rig.Mail.Letters.Count, "the player was asked, or the code did it on purpose: no letters");
            Assert.Equal(0, rig.Orders.ActiveCount);
            Assert.Equal(0, rig.Orders.CancelAll(), "a second time there is nothing to cancel");
            Assert.Equal(-6f, rig.Game.ReputationDelta());
        }

        [Test]
        public void ACancelledOffer_IsNotMadeAgain_WhenTheFeatureIsSwitchedOnLater()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            rig.Orders.DayTick();
            rig.Orders.CancelAll();
            Assert.Equal(GpuState.OrderCancelled, rig.Order("sony.2").status);
            rig.World.NextDay();
            rig.Orders.DayTick();
            Assert.Equal(1, rig.Mail.Letters.Count, "the year's offer was made; the cancelled one is history");
        }

        // ---- limits and the log ----

        [Test]
        public void TheOrdersOfTheSection_AreCappedByTheLimitOfTheBalance()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            for (int i = 0; i < 32; i++) rig.World.State.Data.consoleOrders.Add(new GpuConsoleOrderSave { id = "k." + i, status = GpuState.OrderEnded });
            rig.Orders.DayTick();
            Assert.Equal(0, rig.Mail.Letters.Count, "no room for one more");
            Assert.True(rig.World.Warnings.Any(w => w.Contains("no more offers")), "and the log says so");
            rig.Orders.DayTick();
            Assert.Equal(1, rig.World.Warnings.Count(w => w.Contains("no more offers")), "once");
        }

        [Test]
        public void AMailServiceThatRefuses_MakesNoOrder_AndTheOfferIsTriedTomorrow()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            rig.Mail.Refuse = true;
            rig.Orders.DayTick();
            Assert.Equal(0, rig.World.State.Data.consoleOrders.Count, "no letter, no order");
            Assert.Equal(1, rig.World.Warnings.Count(w => w.Contains("sony.2")));

            rig.Mail.Refuse = false;
            rig.World.NextDay();
            rig.Orders.DayTick();
            Assert.Equal(1, rig.World.State.Data.consoleOrders.Count);
            Assert.Equal(1, rig.Mail.Letters.Count);
        }

        [Test]
        public void ThePrice_IsAMoneyText_InTheLetter()
        {
            var rig = new Rig(Jan1(1998));
            rig.Card();
            rig.Orders.DayTick();
            string[] args = rig.Mail.Offer("sony.2").Letter.Args;
            Assert.True(args[1].Length > 0 && char.IsDigit(args[1][0]), "the volume: " + args[1]);
            Assert.True(args[2].StartsWith("$", StringComparison.Ordinal), "the price in the money format of the game: " + args[2]);
            Assert.True(args[3].Contains("6"), "the term: " + args[3]);
        }

        // ---- the demand of the automatic mode ----

        [Test]
        public void TheDemandOfAnOrder_IsPartOfTheDemandOfItsCard()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            rig.World.Production.RecordDemand(card.id, 100, 50);
            Assert.Equal(150f, rig.World.Production.AverageDemand(card.id));

            rig.Active("sony.2", card.id, 800, 10f);
            rig.Active("nintendo.4", card.id, 200, 10f);
            card.stock = 1000000;
            rig.Orders.DeliverDay();
            Assert.Equal(1000, rig.World.Production.ContractDemandOf(card.id), "the two orders of the card");
            rig.World.Production.Reset();
            rig.World.Production.RecordDemand(card.id, 100, 50);
            Assert.Equal(150f, rig.World.Production.AverageDemand(card.id), "a new session forgets it until the next delivery");
            rig.Orders.DeliverDay();
            rig.World.Production.RecordDemand(card.id, 100, 50);
            Assert.Equal((150f + 1150f) / 2f, rig.World.Production.AverageDemand(card.id), "the day that was recorded with the orders asks for 1150 in all");
        }

        [Test]
        public void AnAutomaticCard_FollowsTheOrder_AndTheOrderIsNotBroken()
        {
            // one line of the card is about 300 cards a day, the order asks for ~800: only the automatic mode that sees the order keeps it
            long kept = RunLife(true);
            long broken = RunLife(false);
            Assert.True(kept > 0, "the automatic card stays on the order");
            Assert.True(broken < 0, "the same card with a line of its own breaks it");
        }

        // The order of sony.2 on a card with one line, 400 days; returns the days the order ran (negative: the day it was cancelled).
        private static long RunLife(bool auto)
        {
            var rig = new Rig(Jan1(1998));
            rig.Game.Factory.CapacityLines = 40;
            GpuProductSave card = rig.Card("g-1", 1);
            card.auto = auto;
            card.price = 150f;
            rig.World.Production.ProduceDay();
            GpuConsoleOrderSave order = rig.Offered();
            rig.Orders.OnChoice(order.mailId, "accept");
            Assert.Equal(GpuState.OrderActive, order.status);
            rig.World.Game.Factory.ResetDay();

            for (int day = 1; day <= 400; day++)
            {
                rig.RunDay();
                if (order.status == GpuState.OrderCancelled) return -day;
            }
            Assert.Equal(GpuState.OrderActive, order.status);
            Assert.True(GpuConsoleMath.MissedShare(order.deliveredWindow, order.missedWindow) <= 0.25f);
            return 400;
        }

        // ---- the long life of an order ----

        [Test]
        public void ALongRun_OfferAcceptedDeliveredAndEnded_KeepsItsInvariants()
        {
            var rig = new Rig(new DateTime(1997, 12, 20));
            rig.Game.Factory.CapacityLines = 60;
            GpuProductSave card = rig.Card("g-1", 4);
            card.auto = true;
            card.price = 150f;
            var seen = new HashSet<string>();
            long contracts = 0;
            for (int day = 0; day < 365 * 7 + 30; day++)
            {
                rig.RunDay();
                foreach (GpuConsoleOrderSave o in rig.World.State.Data.consoleOrders)
                {
                    seen.Add(o.id + ":" + o.status);
                    Assert.True(o.deliveredWindow.Count <= 90 && o.missedWindow.Count <= 90, o.id + " keeps 90 days at most");
                    Assert.True(o.unitsPerDay >= 0 && o.price >= 0f);
                }
                Assert.True(card.stock >= 0, "no negative stock");
                var offered = rig.World.State.Data.consoleOrders.Find(o => o.id == "sony.2");
                if (offered != null && offered.status == GpuState.OrderOffered) rig.Orders.OnChoice(offered.mailId, "accept");
            }
            contracts = (long)rig.Game.EarnedOn(GpuRevenue.Contracts);
            Assert.True(seen.Contains("sony.2:offered") && seen.Contains("sony.2:active"), "sony.2 was offered and accepted: " + string.Join(", ", seen.ToArray()));
            Assert.True(seen.Contains("sony.2:ended") || seen.Contains("sony.2:cancelled"), "and it is over after 6 years: " + string.Join(", ", seen.ToArray()));
            Assert.True(contracts > 0, "and it paid");
            Assert.True(rig.Mail.Letters.Count(l => l.Tag.StartsWith("gpu:console:", StringComparison.Ordinal) && l.Letter.Priority == MailPriority.Critical) >= 2, "the later offers came too");
            Assert.True(rig.World.Warnings.Count == 0 || rig.World.Warnings.All(w => !w.Contains("console")), "no warning of the orders: " + string.Join("; ", rig.World.Warnings.ToArray()));
        }

        // ---- the views and the tab ----

        [Test]
        public void TheViews_SayWhatTheTabShows()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.name = "Vortex";
            rig.Orders.DayTick();
            GpuConsoleOrderSave active = rig.Active("nintendo.4", card.id, 700, 99.5f, 40, 5, new[] { 700, 700, 500 }, new[] { 0, 0, 200 });
            rig.Active("microsoft.1", card.id, 1, 1f).status = GpuState.OrderDeclined;

            IReadOnlyList<GpuConsoleOrderView> views = rig.Orders.Views();
            Assert.Equal(new[] { "sony.2", "nintendo.4", "microsoft.1" }, views.Select(v => v.Id).ToArray());
            GpuConsoleOrderView offer = views[0];
            Assert.Equal(GpuState.OrderOffered, offer.Status);
            Assert.Equal("Sony", offer.Client);
            Assert.Equal(6, offer.TermYears);
            Assert.Equal(new DateTime(1998, 3, 2), offer.AnswerBy.Value, "60 days from the offer");
            Assert.True(offer.UnitsPerDay > 0 && offer.Price > 0f, "the estimate of the terms");

            GpuConsoleOrderView running = views[1];
            Assert.Equal("Nintendo", running.Client);
            Assert.Equal("Vortex", running.ProductName);
            Assert.Equal(700, running.UnitsPerDay);
            Assert.Equal(99.5f, running.Price);
            Assert.Equal(3, running.WindowDays);
            Assert.Near(200d / 2100d, running.MissedShare, 1e-5);
            Assert.Equal(0.25f, running.FailShare);
            Assert.Equal(90, running.GraceDays);
            Assert.Equal(active.endsOn.y, running.EndsOn.Value.Year);
            Assert.Null(running.AnswerBy);
        }

        [Test]
        public void TheTab_ShowsNoBlock_UntilTheFirstOffer_ThenTheOffersTheOrdersAndTheHistory()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            card.name = "Vortex";

            GpuLineupModel empty = BuildModel(rig);
            Assert.Equal(0, empty.Console.Count, "nothing to show");
            string before = empty.Signature;

            rig.Orders.DayTick();
            rig.Active("nintendo.4", card.id, 700, 99.5f, 40, 5, new[] { 700, 700, 500 }, new[] { 0, 0, 200 });
            rig.Active("microsoft.1", card.id, 1, 1f).status = GpuState.OrderCancelled;
            rig.Active("sony.3", card.id, 1, 1f).status = GpuState.OrderEnded;
            GpuLineupModel model = BuildModel(rig);

            Assert.Equal(new[] { "sony.2", "nintendo.4", "microsoft.1", "sony.3" }, model.Console.Select(r => r.Id).ToArray(), "the offers first, then the orders that run, then the history");
            GpuConsoleRow offer = model.Console[0];
            Assert.True(offer.IsLive);
            Assert.Equal(GpuTone.Warn, offer.TitleTone);
            Assert.True(offer.TitleText.Contains("Sony") && offer.TitleText.Contains("6 лет"), offer.TitleText);

            GpuConsoleRow running = model.Console[1];
            Assert.True(running.IsLive);
            Assert.Equal(GpuTone.Good, running.TitleTone);
            Assert.True(running.TitleText.Contains("Nintendo") && running.TitleText.Contains("Vortex") && running.TitleText.Contains("700"), running.TitleText);
            Assert.True(running.DetailText.Contains("9,5%") || running.DetailText.Contains("9.5%"), "the missed share (200 of 2100): " + running.DetailText);
            Assert.Equal(GpuTone.Muted, running.DetailTone, "9.5 % is under half of the 25 % limit");

            Assert.False(model.Console[2].IsLive);
            Assert.Equal(GpuTone.Muted, model.Console[2].TitleTone);
            Assert.True(model.Console[2].TitleText.Contains("расторгнут") && model.Console[2].TitleText.Contains("1999"), model.Console[2].TitleText);
            Assert.True(model.Console[3].TitleText.Contains("выполнен"), model.Console[3].TitleText);
            Assert.NotEqual(before, model.Signature, "the tab is built again when an order appears");
        }

        [Test]
        public void TheTab_PaintsTheMissedShare_ByHowCloseTheLimitIs()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f, 40, 6, new[] { 800 }, new[] { 200 });   // 20 %
            Assert.Equal(GpuTone.Warn, BuildModel(rig).Console[0].DetailTone, "20 % is over half of the 25 % limit");
            order.deliveredWindow = new List<int> { 600 };
            order.missedWindow = new List<int> { 400 };   // 40 %
            Assert.Equal(GpuTone.Bad, BuildModel(rig).Console[0].DetailTone, "40 % is over the limit");
        }

        [Test]
        public void TheSignature_ChangesWithTheStatusOfAnOrder()
        {
            var rig = new Rig(Jan1(1998));
            GpuProductSave card = rig.Card();
            GpuConsoleOrderSave order = rig.Active("sony.2", card.id, 1000, 100f);
            string running = BuildModel(rig).Signature;
            order.status = GpuState.OrderEnded;
            Assert.NotEqual(running, BuildModel(rig).Signature);
        }

        [Test]
        public void TheOldBuild_WithoutTheOrders_StillBuilds()
        {
            var rig = new Rig(Jan1(1998));
            GpuLineupModel model = GpuLineupModel.Build(rig.World.Lineup, rig.World.Projects, rig.World.Production, rig.World.Market, rig.World.Content, rig.Game, false, rig.Text);
            Assert.Equal(0, model.Console.Count);
        }

        private static GpuLineupModel BuildModel(Rig rig)
        {
            return GpuLineupModel.Build(rig.World.Lineup, rig.World.Projects, rig.World.Production, rig.World.Market, rig.World.Content, rig.Game, false, rig.Text, rig.Orders);
        }

        // ---- the strings ----

        [Test]
        public void EveryTextOfTheOrders_IsInBothLanguages()
        {
            var keys = new List<string>
            {
                "gpu.console.title", "gpu.console.title.tip", "gpu.console.offer.subject", "gpu.console.offer.body", "gpu.console.accept", "gpu.console.accept.tip", "gpu.console.decline",
                "gpu.console.decline.tip", "gpu.console.openLineup", "gpu.console.openLineup.tip", "gpu.console.noproduct", "gpu.console.accept.failed", "gpu.console.accepted",
                "gpu.console.failed", "gpu.console.mail.failed.subject", "gpu.console.mail.failed.body", "gpu.console.mail.retired.subject", "gpu.console.mail.retired.body",
                "gpu.console.mail.ended.subject", "gpu.console.mail.ended.body", "gpu.console.disableConfirm", "gpu.console.disableConfirm.yes", "gpu.console.row.offered",
                "gpu.console.row.active", "gpu.console.row.missed", "gpu.console.row.history", "gpu.console.row.noCard", "gpu.lineup.retire.console",
                "gpu.console.client.sony", "gpu.console.client.nintendo", "gpu.console.client.microsoft",
            };
            foreach (string status in new[] { GpuState.OrderOffered, GpuState.OrderActive, GpuState.OrderEnded, GpuState.OrderCancelled, GpuState.OrderDeclined })
                keys.Add("gpu.console.status." + status);

            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in keys)
                {
                    string text;
                    Assert.True(Raw(language, key, out text) && text.Length > 0, language + " lacks " + key);
                }
            }
        }

        [Test]
        public void ThePlaceholdersOfTheOrderTexts_AreFilledByTheCode()
        {
            // the tab: row.offered 5 arguments, row.active 5, row.missed 3, row.history 3
            Assert.Equal(new[] { 4, 4, 2, 2 }, new[]
            {
                HighestOf("gpu.console.row.offered"), HighestOf("gpu.console.row.active"), HighestOf("gpu.console.row.missed"), HighestOf("gpu.console.row.history"),
            });
            Assert.Equal(1, HighestOf("gpu.lineup.retire.console"), "the count of the orders and the penalty");
            Assert.Equal(1, HighestOf("gpu.console.disableConfirm"), "the count and the penalty (the arguments of the question)");
            Assert.Equal(3, HighestOf("gpu.console.mail.failed.body"));
            Assert.Equal(2, HighestOf("gpu.console.mail.retired.body"));
            Assert.Equal(0, HighestOf("gpu.console.accepted"), "ModNotify gives one argument");
            Assert.Equal(0, HighestOf("gpu.console.accept.failed"), "ModNotify gives one argument");
        }

        private static int HighestOf(string key)
        {
            int result = -2;
            foreach (string language in new[] { "ru", "en" })
            {
                string text;
                Assert.True(Raw(language, key, out text), language + " " + key);
                int highest = Highest(text);
                if (result != -2) Assert.Equal(result, highest, key + ": the two languages ask for the same arguments");
                result = highest;
            }
            return result;
        }

        // ---- the wiring, by the text of the code ----

        private static string Code(params string[] parts)
        {
            return Source(true, parts);
        }

        private static string CodeWithStrings(params string[] parts)
        {
            return Source(false, parts);
        }

        private static string Source(bool blankStrings, string[] parts)
        {
            string path = GpuFiles.FeatureSourceDir;
            foreach (string part in parts) path = System.IO.Path.Combine(path, part);
            return SourceScan.Strip(SourceScan.ReadText(path), blankStrings);
        }

        [Test]
        public void TheFeature_WiresTheOrders_ToTheDeliveryTheDayTheQuestionAndTheMail()
        {
            string feature = CodeWithStrings("GpuFeature.cs");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.ContractSellTick, ConsoleDeliveryTick);"), "the delivery before the shop (MarketSellTick)");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, ConsoleDayTick, ConsoleDayPriority);"));
            Assert.True(feature.Contains("ctx.ConfirmBeforeDisable(BuildDisableConfirmation);"), "AC-19.5");
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"console/delivery\", ConsoleOrders.DeliverDay);"));
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"console/day\", ConsoleOrders.DayTick);"));
            Assert.True(feature.Contains("Lineup.Retired += OnCardRetired;"), "taking the card off production breaks the order");
            Assert.True(Regex.IsMatch(feature, @"Guard\(""gameReady/console"", ConsoleSessionStart\);"), "GameReady: the buttons of the letters are computed again after a load");
            Assert.True(Regex.IsMatch(feature, @"Guard\(""console/disabled"", CancelConsoleOrders\);"), "OnDisabled cancels the orders");
            Assert.True(feature.Contains("() => ConsoleOrdersEnabled"), "the switch is read when the offers are made");
            Assert.True(Regex.IsMatch(feature, @"new GpuConsoleOrders\(Content, State, Game, Production, Market, Lineup, new GpuMailAdapter\(\), GpuUi\.Text, \(\) => ConsoleOrdersEnabled, MailAllowed, LogProductionWarning\);"),
                "no order in the fast-forward and while a save loads (MailAllowed)");
            Assert.True(feature.Contains("GpuMailAdapter.BindConsoleHandlers("), "the answers of the letters");
            Assert.True(feature.Contains("GpuWindow.Open(GpuTab.Lineup)"), "the button 'Открыть линейку' opens the tab with the orders");

            int contract = feature.IndexOf("TickPhase.ContractSellTick", StringComparison.Ordinal);
            int market = feature.IndexOf("TickPhase.MarketSellTick, MarketTick", StringComparison.Ordinal);
            Assert.True(contract > 0 && market > 0, "both ticks are subscribed");
        }

        [Test]
        public void TheOrders_AreTheOwnModel_NotTheContractsOfTheGame_AndRevenueIsContracts()
        {
            string orders = Code("Logic", "Services", "GpuConsoleOrders.cs");
            Assert.True(orders.Contains("GpuRevenue.Contracts"), "RevenueOrigin.Contracts");
            Assert.False(orders.Contains("GpuRevenue.Sales"));
            foreach (string forbidden in new[] { "ContractType", "BusinessContract", "ContractManager", "ContractSeller", "contract.reward", "ModChannels", "Modifier(" })
                Assert.False(orders.Contains(forbidden), "FR-19 item 4: " + forbidden + " is not used (the channel contract.reward does not act here)");
            Assert.False(Regex.IsMatch(orders, @"\bModMail\b|\bModReputation\b|\bModUI\b|\bModLog\b"), "the game side is the adapter and the feature");

            Assert.True(Code("Bridges", "GpuGameAdapter.cs").Contains("ModReputation.Add(company, delta, reasonKey)"), "the penalty goes through ModReputation");
            string adapter = Code("Bridges", "GpuMailAdapter.cs");
            foreach (string call in new[] { "ModMail.Send(", "ModMail.SetChoiceState(", "ModMail.Refresh(", "ModMail.Resolve(", "ModMail.RegisterChoiceHandler(", "ModMail.RegisterActionHandler(", "ModMail.Delivered" })
                Assert.True(adapter.Contains(call), "the adapter calls " + call);
        }

        [Test]
        public void TheOrdersHaveNoCounterOfTheirOwn_AndPickNoMailId()
        {
            string orders = Code("Logic", "Services", "GpuConsoleOrders.cs");
            foreach (string forbidden in new[] { "NextId", "FirstModMailId", "maxDeliveredPerDay", "maxPerFeaturePerDay" })
                Assert.False(orders.Contains(forbidden), forbidden);
            Assert.False(Regex.IsMatch(orders, @"\bmailId\s*=\s*\d"), "the id is the core's");
        }
    }
}
