using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>The mail service of the core in memory: what was sent and when, whether it refuses or fails.</summary>
    internal sealed class FakeMail : IGpuMail
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

        private readonly Func<DateTime> _now;
        public readonly List<Sent> Letters = new List<Sent>();

        /// <summary>The service takes no letter (a feature that is Faulted, a queue full of critical mail): <c>Send</c> answers -1.</summary>
        public bool Refuse;

        /// <summary>The service throws, as a broken service might.</summary>
        public bool Throw;

        public FakeMail(Func<DateTime> now)
        {
            _now = now;
        }

        public int Send(GpuMailLetter letter)
        {
            if (Throw) throw new InvalidOperationException("the mail service is broken");
            if (Refuse) return -1;
            int id = 100000 + Letters.Count;
            Letters.Add(new Sent { Letter = letter, On = _now(), Id = id });
            return id;
        }

        public bool Exists(string tag)
        {
            return Letters.Any(l => l.Tag == tag);
        }

        public List<Sent> WithPrefix(string prefix)
        {
            return Letters.Where(l => l.Tag.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
    }

    /// <summary>
    /// The news of the GPU market (TSK-036; FR-14, AC-11.1, AC-12.1, AC-12.3, AC-14.1, AC-14.2): the letters of the eras, the segments, the makers and the new leaders - their tags,
    /// their priorities, their texts in both languages - on the shipped content, the services of the feature and a mail service in memory. No letter is made in a quiet time
    /// (the fast-forward, a load), none for an event of the past, none twice; the switch <c>news</c> silences the leaders and only them; the feature picks no id and keeps no limit.
    /// </summary>
    [Covers(typeof(GpuNews), typeof(IGpuMail), typeof(GpuMailLetter))]
    public class GpuNewsTests
    {
        private const string FeatureId = "gpu-product-line";

        private sealed class Rig
        {
            public readonly GpuWorld World;
            public readonly FakeMail Mail;
            public readonly GpuNews News;
            public readonly TestGpuText Text;
            public bool Leaders = true;
            public bool CanSend = true;
            public int Caught;

            /// <param name="guardCatches">The guard of the feature: an exception of a handler is caught and counted, as <c>ModHooks.Guard</c> does; otherwise it reaches the test.</param>
            public Rig(DateTime start, string language = "ru", bool guardCatches = false, uint seed = 42, bool seed1 = true)
            {
                World = new GpuWorld(start, seed, GpuWorld.ShippedEras());
                World.Game.AddTimelineNodes();
                World.Game.FrontierKHz = 250000f;
                World.RivalsOn = true;
                World.Game.AddCompany("ai:1", "AMD");
                World.Game.AddCompany("ai:0", "Intel");
                Text = new TestGpuText(language);
                Mail = new FakeMail(() => World.Game.Date);
                Action<string, Action> guard = null;
                if (guardCatches)
                {
                    guard = (site, body) =>
                    {
                        try
                        {
                            body();
                        }
                        catch (Exception)
                        {
                            Caught++;
                        }
                    };
                }
                News = new GpuNews(World.Content, World.State, World.Game, World.Eras, World.Rivals, Mail, Text, () => Leaders, () => CanSend, guard, World.Warnings.Add);
                News.Bind();
                if (seed1) World.Market.SeedForYear(start.Year);
            }

            /// <summary>The 1st of every month after the current date up to <paramref name="last"/>, with the monthly handlers of the feature.</summary>
            public void MonthsUntil(DateTime last)
            {
                DateTime d = new DateTime(World.Game.Date.Year, World.Game.Date.Month, 1);
                while (d < last)
                {
                    d = d.AddMonths(1);
                    World.Game.Date = d;
                    World.MonthTicks();
                }
            }
        }

        private static string[] Tags(IEnumerable<FakeMail.Sent> letters)
        {
            return letters.Select(l => l.Tag).ToArray();
        }

        // The run of 1990-2035 in months, once: several tests read the same letters.
        private static Rig _long;

        private static Rig LongRun()
        {
            if (_long != null) return _long;
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.MonthsUntil(new DateTime(2035, 12, 1));
            _long = rig;
            return rig;
        }

        // ---- the eras and the segments (FR-14 item 1, AC-11.1) ----

        [Test]
        public void Ac111_The3DEra_BringsALetter_WithTheNameOfTheEraAndItsText()
        {
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.MonthsUntil(new DateTime(1995, 12, 1));
            Assert.Equal(0, rig.Mail.WithPrefix(GpuNews.TagEra).Count, "the eras of the past (2D, 1981) are not news; the next one is 1996");

            rig.MonthsUntil(new DateTime(1996, 1, 1));
            List<FakeMail.Sent> eras = rig.Mail.WithPrefix(GpuNews.TagEra);
            Assert.Equal(1, eras.Count);
            GpuMailLetter letter = eras[0].Letter;
            Assert.Equal("gpu:era:era.3d", letter.Tag);
            Assert.Equal("gpu.era.3d", letter.TitleKey);
            Assert.Equal("gpu.mail.era.3d", letter.BodyKey);
            Assert.Equal(MailPriority.Normal, letter.Priority);
            Assert.Equal(new DateTime(1996, 1, 1), eras[0].On);
            Assert.Equal("Эпоха 3D-ускорителей", rig.Text.L(letter.TitleKey), "AC-11.1: the letter \"Эпоха 3D-ускорителей\"");
            Assert.True(rig.Text.L(letter.BodyKey, letter.Args).Contains("SGRAM"), "and the nodes of the tree it needs");
            Assert.Equal(0, rig.Mail.WithPrefix(GpuNews.TagSegment).Count, "the segments that open with the era are in its letter");
        }

        [Test]
        public void ASegmentThatOpensWithoutAnEra_HasALetterOfItsOwn_WithTheRequirementsAndTheNodeToResearch()
        {
            var rig = new Rig(new DateTime(1996, 6, 1));
            rig.MonthsUntil(new DateTime(1998, 1, 1));
            List<FakeMail.Sent> segments = rig.Mail.WithPrefix(GpuNews.TagSegment);
            Assert.Equal(new[] { "gpu:segment:gaming.low" }, Tags(segments), "the budget segment opens in 1998, no era begins then");

            GpuMailLetter letter = segments[0].Letter;
            Assert.Equal(MailPriority.Normal, letter.Priority);
            Assert.Equal("gpu.mail.segment.subject", letter.TitleKey);
            Assert.Equal("Игровые: бюджетные", letter.Args[0]);
            Assert.Equal("Открылся сегмент: Игровые: бюджетные", rig.Text.L(letter.TitleKey, letter.Args));
            string body = rig.Text.L(letter.BodyKey, letter.Args);
            Assert.True(body.Contains("Требования года: 3D;"), "what the segment asks for: " + body);

            // the node of the tree that gives the 3D architecture
            GpuResearchNodeDto node = rig.World.Content.ResearchNodes.First(n => n.unlocks == "arch.3d");
            Assert.True(body.Contains("Исследуйте: " + GpuNames.Node(rig.World.Content, rig.Text, node.id)), "the hint names the node " + node.id + ": " + body);
        }

        [Test]
        public void TheLettersOfTheErasAndTheSegments_ComeWhateverTheSwitchSays()
        {
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.Leaders = false;   // news off
            rig.MonthsUntil(new DateTime(2008, 1, 1));
            Assert.Equal(new[] { "gpu:era:era.3d", "gpu:era:era.shader", "gpu:era:era.compute" }, Tags(rig.Mail.WithPrefix(GpuNews.TagEra)), "FR-14 item 5: the eras stay");
            Assert.Equal(new[] { "gpu:segment:gaming.low" }, Tags(rig.Mail.WithPrefix(GpuNews.TagSegment)));
            Assert.True(rig.Mail.WithPrefix(GpuNews.TagVendor).Count >= 4, "so do the makers: only the letters about leaders are the switch's");
            Assert.Equal(0, rig.Mail.WithPrefix(GpuNews.TagLeader).Count, "no letter about a leader");
            Assert.Equal(0, rig.World.State.Data.news.lastLeaderMail.Count, "and nothing recorded, so the first leader after the switch is turned on gets its letter");
        }

        // ---- the makers (FR-14 item 2, AC-12.1, AC-12.3) ----

        [Test]
        public void Ac121_3dfxEntersIn1996_WithALetter()
        {
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.MonthsUntil(new DateTime(1996, 3, 1));
            FakeMail.Sent enter = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:3dfx:enter");
            Assert.Equal(MailPriority.Normal, enter.Letter.Priority);
            Assert.Equal(new DateTime(1996, 1, 1), enter.On);
            Assert.Equal("3dfx выходит на рынок видеокарт", rig.Text.L(enter.Letter.TitleKey, enter.Letter.Args));
            string body = rig.Text.L(enter.Letter.BodyKey, enter.Letter.Args);
            Assert.True(body.Contains("«Voodoo»") && body.Contains("Игровые: энтузиасты") && body.Contains("Игровые: массовые"), body);
        }

        [Test]
        public void TheMakersOfTheShippedHistory_HaveTheirLetters_ExactlyOneEach()
        {
            Rig rig = LongRun();
            string[] vendor = Tags(rig.Mail.WithPrefix(GpuNews.TagVendor)).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[]
            {
                "gpu:vendor:3dfx:enter", "gpu:vendor:3dfx:merge", "gpu:vendor:amd.gpu:enter", "gpu:vendor:ati:merge", "gpu:vendor:intel.gpu:enter",
                "gpu:vendor:matrox:exit", "gpu:vendor:nvidia:enter", "gpu:vendor:s3:exit",
            }, vendor, "enter, leave, merge: each once, by the dates of vendors.json");

            Assert.Equal(new DateTime(2003, 1, 1), rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:s3:exit").On);
            Assert.Equal(new DateTime(2005, 1, 1), rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:matrox:exit").On);
            Assert.Equal(new DateTime(2001, 1, 1), rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:3dfx:merge").On);
            Assert.Equal(new DateTime(2006, 1, 1), rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:ati:merge").On);
            Assert.Equal(new DateTime(2022, 1, 1), rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:intel.gpu:enter").On);
        }

        [Test]
        public void TheMerger_NamesTheOneWhoTakesAndTheOneTaken_AndTheNameOfABoundMakerIsTheGames()
        {
            Rig rig = LongRun();
            GpuMailLetter ati = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:ati:merge").Letter;
            Assert.Equal(new[] { "AMD", "ATI" }, ati.Args, "AMD takes ATI; AMD is the company of the game");
            Assert.Equal("AMD поглощает ATI", rig.Text.L(ati.TitleKey, ati.Args));
            Assert.True(rig.Text.L(ati.BodyKey, ati.Args).Contains("Карты ATI переходят к AMD"));

            GpuMailLetter dfx = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:3dfx:merge").Letter;
            Assert.Equal(new[] { "NVIDIA", "3dfx" }, dfx.Args);
            Assert.Equal(MailPriority.Normal, dfx.Priority);
        }

        [Test]
        public void Ac123_AMakerWhoseCompanyWentBankrupt_LeavesWithALetterThatSaysSo()
        {
            var rig = new Rig(new DateTime(2021, 6, 1));
            rig.MonthsUntil(new DateTime(2022, 2, 1));
            Assert.True(rig.Mail.Exists("gpu:vendor:intel.gpu:enter"), "Intel entered");
            Assert.Equal(GpuState.VendorActive, rig.World.Rivals.Find("intel.gpu").State);

            rig.World.Game.Companies["ai:0"].Bankrupt = true;
            rig.World.RunDay();   // the day: CheckBoundCompanies sees IsBankrupt
            FakeMail.Sent exit = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:intel.gpu:exit");
            Assert.Equal(MailPriority.Normal, exit.Letter.Priority);
            Assert.Equal("gpu.mail.vendor.exit.bankrupt", exit.Letter.BodyKey);
            Assert.Equal("Intel уходит с рынка видеокарт", rig.Text.L(exit.Letter.TitleKey, exit.Letter.Args), "AC-12.3");
            Assert.True(rig.Text.L(exit.Letter.BodyKey, exit.Letter.Args).Contains("обанкротилась"));
        }

        [Test]
        public void AnExitAtTheEndOfTheYears_HasTheOrdinaryText()
        {
            Rig rig = LongRun();
            GpuMailLetter matrox = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:matrox:exit").Letter;
            Assert.Equal("gpu.mail.vendor.exit.body", matrox.BodyKey);
            Assert.Equal("Matrox уходит с рынка видеокарт", rig.Text.L(matrox.TitleKey, matrox.Args));
        }

        // ---- the leaders (FR-14 item 3, AC-14.1) ----

        // The vendor launches a card in a segment that has no other card on sale, so it is the fastest.
        private static GpuProductSave LaunchAlone(Rig rig, string vendorId, string segmentId, DateTime date)
        {
            rig.World.Game.Date = date;
            rig.World.State.Data.products.RemoveAll(p => p.segIds != null && p.segIds.Contains(segmentId));
            GpuVendorRuntime vendor = rig.World.Rivals.Find(vendorId);
            Assert.NotNull(vendor, vendorId);
            GpuProductSave card = rig.World.Rivals.AI.Launch(vendor, rig.World.Content.Segment(segmentId), date, date, false);
            Assert.NotNull(card, "the vendor can make a card on " + date.ToString("yyyy-MM-dd"));
            return card;
        }

        [Test]
        public void Ac141_AVendorsFastestCard_BringsALetterOfLowPriority_AndTheSegmentIsQuietForThreeMonths()
        {
            var rig = new Rig(new DateTime(2006, 3, 1));
            rig.Mail.Letters.Clear();

            GpuProductSave card = LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 3, 1));
            List<FakeMail.Sent> leaders = rig.Mail.WithPrefix(GpuNews.TagLeader);
            Assert.Equal(new[] { "gpu:leader:gaming.high" }, Tags(leaders));
            GpuMailLetter letter = leaders[0].Letter;
            Assert.Equal(MailPriority.Low, letter.Priority, "a new leader is Low");
            Assert.Equal("Новый лидер в сегменте «Игровые: энтузиасты»", rig.Text.L(letter.TitleKey, letter.Args), "AC-14.1");
            string body = rig.Text.L(letter.BodyKey, letter.Args);
            Assert.True(body.Contains("NVIDIA") && body.Contains(card.name) && body.Contains("сильных конкурентов нет"), body);
            Assert.Equal(1, rig.World.State.Data.news.lastLeaderMail.Count);
            Assert.Equal("gaming.high", rig.World.State.Data.news.lastLeaderMail[0].seg);

            // another fastest card within three months: no second letter
            LaunchAlone(rig, "amd.gpu", "gaming.high", new DateTime(2006, 4, 1));
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 5, 31));
            Assert.Equal(1, rig.Mail.WithPrefix(GpuNews.TagLeader).Count, "no repeat for the segment in the next three months");

            // another segment is not held back by it
            LaunchAlone(rig, "nvidia", "gaming.mid", new DateTime(2006, 4, 1));
            Assert.Equal(new[] { "gpu:leader:gaming.high", "gpu:leader:gaming.mid" }, Tags(rig.Mail.WithPrefix(GpuNews.TagLeader)));

            // and after three months the segment may be told again
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 6, 1));
            Assert.Equal(3, rig.Mail.WithPrefix(GpuNews.TagLeader).Count);
            Assert.Equal(new DateTime(2006, 6, 1), rig.Mail.WithPrefix(GpuNews.TagLeader).Last().On);
            GpuSegLaunch kept = rig.World.State.Data.news.lastLeaderMail.Find(l => l.seg == "gaming.high");
            Assert.Equal(6, kept.date.m, "the date of the last letter is kept for the segment");
        }

        [Test]
        public void ATieOrNoSpeedAtAll_LeadsNobody()
        {
            Assert.True(GpuNews.IsNewLeader(101d, 100d));
            Assert.False(GpuNews.IsNewLeader(100d, 100d), "a tie is not a new leader");
            Assert.False(GpuNews.IsNewLeader(99d, 100d));
            Assert.True(GpuNews.IsNewLeader(1d, 0d), "the only card with a speed leads");
            Assert.False(GpuNews.IsNewLeader(0d, 0d), "a card of no speed does not");
            Assert.False(GpuNews.IsNewLeader(double.NaN, 0d));
        }

        [Test]
        public void ACardThatIsNotTheFastest_BringsNoLetter()
        {
            var rig = new Rig(new DateTime(2006, 3, 1));
            rig.Mail.Letters.Clear();

            // a card of the player that no vendor's card beats
            rig.World.State.Data.products.RemoveAll(p => p.segIds != null && p.segIds.Contains("gaming.high"));
            GpuProductSave mine = rig.World.AddCard("g-fast", 0);
            mine.segIds = new List<string> { "gaming.high" };
            mine.pop = new List<float> { 0.5f };
            mine.stats.effectivePerf = 1e12;
            mine.stats.features = new List<string> { "3d", "tnl", "shader" };

            GpuVendorRuntime vendor = rig.World.Rivals.Find("nvidia");
            GpuProductSave card = rig.World.Rivals.AI.Launch(vendor, rig.World.Content.Segment("gaming.high"), rig.World.Game.Date, rig.World.Game.Date, false);
            Assert.NotNull(card);
            Assert.Equal(0, rig.Mail.WithPrefix(GpuNews.TagLeader).Count, "the player's card is faster");
            Assert.Equal(0, rig.World.State.Data.news.lastLeaderMail.Count);
        }

        [Test]
        public void ACardThatCannotSellInTheSegment_IsNotItsLeader()
        {
            var rig = new Rig(new DateTime(2012, 3, 1));
            rig.Mail.Letters.Clear();

            // compute asks for the function "compute", without it a card sells nothing there (factor 0): a faster card of the player without it does not count
            rig.World.State.Data.products.RemoveAll(p => p.segIds != null && p.segIds.Contains("compute"));
            GpuProductSave nofunction = rig.World.AddCard("g-noc", 0);
            nofunction.segIds = new List<string> { "compute" };
            nofunction.pop = new List<float> { 0.5f };
            nofunction.stats.effectivePerf = 1e12;
            nofunction.stats.features = new List<string> { "3d" };

            GpuVendorRuntime vendor = rig.World.Rivals.Find("nvidia");
            GpuProductSave card = rig.World.Rivals.AI.Launch(vendor, rig.World.Content.Segment("compute"), rig.World.Game.Date, rig.World.Game.Date, false);
            Assert.NotNull(card);
            Assert.Equal(new[] { "gpu:leader:compute" }, Tags(rig.Mail.WithPrefix(GpuNews.TagLeader)), "the card without the function is not a rival for the leadership");
        }

        [Test]
        public void TheSwitchNews_SilencesTheLeaders_AndOnlyThem()
        {
            var rig = new Rig(new DateTime(2006, 3, 1));
            rig.Mail.Letters.Clear();
            rig.Leaders = false;
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 3, 1));
            Assert.Equal(0, rig.Mail.Letters.Count, "FR-14 item 5: no letter about a leader");
            Assert.Equal(0, rig.World.State.Data.news.lastLeaderMail.Count);

            rig.Leaders = true;
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 3, 2));
            Assert.Equal(1, rig.Mail.WithPrefix(GpuNews.TagLeader).Count, "on again: the next leader is told at once");
        }

        [Test]
        public void TheLeadersOfALongRun_AreThreeMonthsApartInASegment_AndNeverBeforeTheirSegmentOpened()
        {
            Rig rig = LongRun();
            List<FakeMail.Sent> leaders = rig.Mail.WithPrefix(GpuNews.TagLeader);
            Assert.True(leaders.Count >= 10, "a market of forty-five years has leaders: " + leaders.Count);
            Console.WriteLine("  news  letters about a new leader over the long run: " + leaders.Count + " (" + (leaders.Count / 46.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                              + " a year, the pause is 3 months a segment)");

            foreach (IGrouping<string, FakeMail.Sent> segment in leaders.GroupBy(l => l.Tag))
            {
                DateTime? previous = null;
                foreach (FakeMail.Sent letter in segment)
                {
                    Assert.Equal(MailPriority.Low, letter.Letter.Priority);
                    if (previous.HasValue) Assert.True(letter.On >= previous.Value.AddMonths(3), segment.Key + ": " + previous.Value.ToString("yyyy-MM") + " then " + letter.On.ToString("yyyy-MM"));
                    previous = letter.On;
                }
            }
            string segmentId = leaders[0].Tag.Substring(GpuNews.TagLeader.Length);
            GpuSegmentDto first = rig.World.Content.Segment(segmentId);
            Assert.True(leaders[0].On.Year >= first.opensYear, "a letter about a segment that is open");
        }

        // ---- quiet times (FR-14 items 4 and 6, AC-14.2) ----

        [Test]
        public void WhileTheGameIsNotInItsNormalCourse_NoLetterIsMade()
        {
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.CanSend = false;   // the new game is fast-forwarded, or a save loads
            rig.MonthsUntil(new DateTime(1997, 1, 1));
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(1997, 1, 1));
            Assert.Equal(0, rig.Mail.Letters.Count, "FR-14 item 4: nothing is made, nothing is queued");
            Assert.True(rig.News.IsSilent);
        }

        [Test]
        public void TheStartOfASession_BringsTheMakersUpToDateWithoutAWord()
        {
            var rig = new Rig(new DateTime(2024, 1, 1));
            rig.MonthsUntil(new DateTime(2024, 3, 1));
            rig.Mail.Letters.Clear();

            // Intel's company is not in this game (it was removed from the save): the vendor leaves when the session starts
            rig.World.Game.Companies.Remove("ai:0");
            var heard = new List<string>();
            rig.World.Rivals.Exited += v => heard.Add(v.Id);
            rig.News.Silently(() => rig.World.Rivals.StartSession());
            Assert.Equal(new[] { "intel.gpu" }, heard.ToArray(), "the vendor left");
            Assert.Equal(0, rig.Mail.Letters.Count, "the quiet migration of a load: no letter");
            Assert.False(rig.News.IsSilent, "and the silence is over with the call");

            // the same exit seen on an ordinary day has its letter: it is the call that was quiet, not the event
            var other = new Rig(new DateTime(2024, 1, 1));
            other.MonthsUntil(new DateTime(2024, 3, 1));
            other.Mail.Letters.Clear();
            other.World.Game.Companies["ai:0"].Bankrupt = true;
            other.World.Rivals.CheckBoundCompanies();
            Assert.Equal(new[] { "gpu:vendor:intel.gpu:exit" }, Tags(other.Mail.Letters));
        }

        [Test]
        public void Silently_Nests_AndEndsEvenWhenTheBodyThrows()
        {
            var rig = new Rig(new DateTime(2000, 1, 1));
            rig.News.Silently(() =>
            {
                Assert.True(rig.News.IsSilent);
                rig.News.Silently(() => Assert.True(rig.News.IsSilent));
                Assert.True(rig.News.IsSilent, "the inner call did not end the outer one");
            });
            Assert.False(rig.News.IsSilent);

            try
            {
                rig.News.Silently(() => { throw new InvalidOperationException("boom"); });
            }
            catch (InvalidOperationException)
            {
            }
            Assert.False(rig.News.IsSilent, "an exception does not leave the news silent for good");
            rig.News.Silently(null);   // nothing to run: nothing happens
        }

        [Test]
        public void Ac142_AFeatureThatWasOffForYears_CatchesUpWithoutLettersAboutTheYearsItMissed()
        {
            // the feature was off from 2001 to 2005: the market is of 2001, the calendar says June 2005
            var rig = new Rig(new DateTime(2001, 1, 1));
            rig.Mail.Letters.Clear();
            rig.World.Game.Date = new DateTime(2005, 6, 1);
            rig.World.MonthTicks();

            Assert.Equal(0, rig.Mail.Letters.Count(l => !l.Tag.StartsWith(GpuNews.TagLeader, StringComparison.Ordinal)),
                "no letter about 2001-2004 (the shader era, S3 and Matrox leaving); a card the makers launch in the month itself may be a leader, that is today's news");
            Assert.Equal(GpuState.VendorExited, rig.World.Rivals.Find("s3").State, "but the state caught up");
            Assert.Equal(GpuState.VendorExited, rig.World.Rivals.Find("matrox").State);
            Assert.Equal(GpuState.VendorActive, rig.World.Rivals.Find("ati").State);
            Assert.Equal(GpuState.VendorMerged, rig.World.Rivals.Find("3dfx").State);

            // what happens after the catch-up is news as usual
            rig.MonthsUntil(new DateTime(2006, 1, 1));
            Assert.Equal(new[] { "gpu:vendor:amd.gpu:enter", "gpu:vendor:ati:merge" }, Tags(rig.Mail.WithPrefix(GpuNews.TagVendor)).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        }

        [Test]
        public void AnEventOfTheLastMonth_IsStillNews_AnEventOfTheMonthBeforeIsNot()
        {
            var feb = new DateTime(2005, 2, 1);
            Assert.True(GpuNews.IsFresh(new DateTime(2005, 1, 1), feb), "the 1st of January, seen on the 1st of February: the previous month");
            Assert.True(GpuNews.IsFresh(new DateTime(2005, 2, 1), feb));
            Assert.False(GpuNews.IsFresh(new DateTime(2004, 12, 31), feb), "before the previous calendar month");
            Assert.True(GpuNews.IsFresh(new DateTime(2005, 1, 1), new DateTime(2005, 2, 28)), "the whole of February");
            Assert.False(GpuNews.IsFresh(new DateTime(2005, 1, 1), new DateTime(2005, 3, 1)), "and not in March");
            Assert.True(GpuNews.IsFresh(new DateTime(2004, 12, 1), new DateTime(2005, 1, 1)), "across the new year");
            Assert.False(GpuNews.IsFresh(new DateTime(2004, 11, 30), new DateTime(2005, 1, 1)));
        }

        [Test]
        public void ALetterSentBefore_IsNotSentAgain()
        {
            var rig = new Rig(new DateTime(1990, 1, 1));
            rig.Mail.Letters.Add(new FakeMail.Sent { Letter = new GpuMailLetter { Tag = "gpu:era:era.3d" }, On = new DateTime(1996, 1, 1) });   // sent by a session before
            rig.MonthsUntil(new DateTime(1996, 6, 1));
            Assert.Equal(1, rig.Mail.Letters.Count(l => l.Tag == "gpu:era:era.3d"), "the tag is in the mail: it is not sent twice");
            Assert.True(rig.Mail.Exists("gpu:vendor:3dfx:enter"), "the other letters of the year came");
        }

        // ---- the core's business, not the feature's (FR-14 item 7) ----

        [Test]
        public void AMailServiceThatRefuses_CostsTheNewsNothingButAWarning()
        {
            var rig = new Rig(new DateTime(2006, 3, 1));
            rig.Mail.Letters.Clear();
            rig.Mail.Refuse = true;
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 3, 1));
            Assert.Equal(0, rig.World.State.Data.news.lastLeaderMail.Count, "a letter that was not taken is not recorded: the segment is not quiet for it");
            Assert.True(rig.World.Warnings.Any(w => w.Contains("gpu:leader:gaming.high")), "one line in the log says which letter");

            rig.Mail.Refuse = false;
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(2006, 3, 2));
            Assert.Equal(1, rig.Mail.WithPrefix(GpuNews.TagLeader).Count);
        }

        [Test]
        public void AFailingHandler_IsCaughtByTheGuard_AndTheMonthGoesOn()
        {
            var rig = new Rig(new DateTime(1995, 6, 1), "ru", true);
            rig.Mail.Throw = true;
            rig.MonthsUntil(new DateTime(1996, 3, 1));   // the month of 3dfx and of the 3D era: both letters throw
            Assert.True(rig.Caught >= 2, "the guard of the feature caught " + rig.Caught);
            Assert.Equal(GpuState.VendorActive, rig.World.Rivals.Find("3dfx").State, "and the month went on: the vendor entered all the same");
            Assert.True(rig.World.Rivals.CardsOnSale > 0);

            rig.Mail.Throw = false;
            LaunchAlone(rig, "nvidia", "gaming.high", new DateTime(1996, 3, 2));
            Assert.Equal(1, rig.Mail.WithPrefix(GpuNews.TagLeader).Count, "later letters are not lost to it");
        }

        [Test]
        public void TheNews_PickNoIdAndKeepNoDailyLimit_TheCoreDoesBoth()
        {
            string news = SourceScan.Strip(SourceScan.ReadText(System.IO.Path.Combine(GpuFiles.FeatureSourceDir, "Logic", "Services", "GpuNews.cs")), true);
            foreach (string forbidden in new[] { "NextId", "MailId", "maxDeliveredPerDay", "maxPerFeaturePerDay", "PerDay", "DeliveredToday", "FirstModMailId" })
                Assert.False(news.Contains(forbidden), "FR-14 item 7: the news have no " + forbidden);
            Assert.False(Regex.IsMatch(news, @"\bModMail\b"), "and no door to the mail service of the core but the interface");

            // the whole feature reaches the mail service in one place
            var users = new List<string>();
            foreach (string path in SourceScan.Files(GpuFiles.FeatureSourceDir, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"\bModMail\s*\.")) users.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path));
            }
            Assert.Equal(new[] { "Bridges/GpuMailAdapter.cs" }, users.Select(u => u.Replace('\\', '/')).ToArray(), "ModMail is used by the adapter only");

            string adapter = SourceScan.Strip(SourceScan.ReadText(System.IO.Path.Combine(GpuFiles.FeatureSourceDir, "Bridges", "GpuMailAdapter.cs")), true);
            Assert.True(adapter.Contains("ModMail.Send(") && adapter.Contains("ModMail.Find("), "Send and Find");
            Assert.False(Regex.IsMatch(adapter, @"\bMailId\s*="), "the adapter gives no id to a message");
        }

        [Test]
        public void TheFeature_WiresTheNews_QuietAtTheStartOfASession()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(System.IO.Path.Combine(GpuFiles.FeatureSourceDir, "GpuFeature.cs")), false);
            Assert.True(Regex.IsMatch(feature, @"News = new GpuNews\(Content, State, Game, Eras, Rivals, new GpuMailAdapter\(\), GpuUi\.Text, \(\) => NewsEnabled, MailAllowed, Guard, LogProductionWarning\);"), "the news");
            Assert.True(feature.Contains("News.Bind();"));
            Assert.True(Regex.IsMatch(feature, @"private void StartMarket\(\)\s*\{[^}]*News\.Silently\(\(\) =>"), "the session of the market starts quietly");
            Assert.True(feature.Contains("News.Silently(() => Rivals.SeedForYear(year))"), "so does the seed of the vendors");
            Assert.True(Regex.IsMatch(feature, @"MailAllowed\(\)\s*\{\s*return ModTicks\.IsGameReady && !ModTicks\.IsFastForwarding && !ModTicks\.IsLoading;"), "no letter in the fast-forward and while a save loads");
        }

        // ---- the texts and the tags of every letter of the shipped history ----

        [Test]
        public void EveryTagOfTheLongRun_BelongsToTheFeature()
        {
            Rig rig = LongRun();
            Assert.True(rig.Mail.Letters.Count > 20);
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            var problems = new List<string>();
            try
            {
                Assert.True(ModIds.Load(prefixes, problems) > 0, string.Join("; ", problems));
                foreach (FakeMail.Sent sent in rig.Mail.Letters)
                    Assert.True(ModIds.IsTagAllowed(FeatureId, sent.Tag), ModIds.Explain(IdKind.Tag, FeatureId, sent.Tag));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void EveryLetterOfTheLongRun_ReadsWellInBothLanguages()
        {
            Rig rig = LongRun();
            var unfilled = new Regex(@"\{\d+\}|\[gpu\.");
            foreach (string language in new[] { "ru", "en" })
            {
                var text = new TestGpuText(language);
                foreach (FakeMail.Sent sent in rig.Mail.Letters)
                {
                    GpuMailLetter letter = sent.Letter;
                    Assert.True(text.Has(letter.TitleKey), language + ": no title " + letter.TitleKey);
                    Assert.True(text.Has(letter.BodyKey), language + ": no body " + letter.BodyKey);
                    string title = text.L(letter.TitleKey, letter.Args);
                    string body = text.L(letter.BodyKey, letter.Args);
                    Assert.True(title.Length > 3 && body.Length > 20, language + " " + letter.Tag + ": " + title + " / " + body);
                    Assert.False(unfilled.IsMatch(title) || unfilled.IsMatch(body), language + " " + letter.Tag + " has an argument left open: " + title + " / " + body);
                }
            }
        }

        [Test]
        public void TheLettersInEnglish_UseTheEnglishNames()
        {
            var rig = new Rig(new DateTime(1990, 1, 1), "en");
            rig.MonthsUntil(new DateTime(1996, 2, 1));
            FakeMail.Sent enter = rig.Mail.Letters.Single(l => l.Tag == "gpu:vendor:3dfx:enter");
            Assert.Equal("3dfx enters the graphics card market", rig.Text.L(enter.Letter.TitleKey, enter.Letter.Args));
            FakeMail.Sent era = rig.Mail.Letters.Single(l => l.Tag == "gpu:era:era.3d");
            Assert.Equal("The era of 3D accelerators", rig.Text.L(era.Letter.TitleKey, era.Letter.Args));
        }

        [Test]
        public void TheEraLetters_AreTheEras_OfTheFiveThatBeganAfterTheStart()
        {
            Rig rig = LongRun();
            Assert.Equal(new[] { "gpu:era:era.3d", "gpu:era:era.shader", "gpu:era:era.compute", "gpu:era:era.ai", "gpu:era:era.rt" }, Tags(rig.Mail.WithPrefix(GpuNews.TagEra)),
                "1996, 2003, 2007, 2016, 2020: the era of 1981 began before the game");
            Assert.Equal(new[] { 1996, 2003, 2007, 2016, 2020 }, rig.Mail.WithPrefix(GpuNews.TagEra).Select(l => l.On.Year).ToArray());
            Assert.Equal(new[] { "gpu:segment:gaming.low" }, Tags(rig.Mail.WithPrefix(GpuNews.TagSegment)), "the only segment that opens without an era");
        }
    }
}
