using System.Collections.Generic;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-013, TSK-014, FR-2, FR-3, FR-6, FR-7: the notifications and the letters of the feature, as the string tables of the feature word them in ru and en -
    /// the announcement (one letter tagged <c>pg:announce:id</c>, one notification), the launch, the early adopter (<c>pg:early:id</c>, +6 % for 60 months),
    /// the penalty for changing the socket (AC-6.1: 6.7 %, 24 months, "supported 12 of 36"), and the warnings of the release window (AC-7.1; nothing for a CPU that
    /// changes nothing; the answer of <c>Preview</c> writes nothing).
    /// </summary>
    [Covers(typeof(PlatformNotices), typeof(Notice), typeof(MailPlan), typeof(ViewText))]
    public class PlatformNoticesTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private static GenerationDef Gen(string id)
        {
            GenerationDef gen = ViewTestData.Catalog().ById(id);
            Assert.NotNull(gen, id);
            return gen;
        }

        private static string Raw(string language, string key)
        {
            var tables = new L10nTables();
            tables.AddJson(language, language, PlatformTestData.ReadStrings(language), null);
            string text;
            Assert.True(tables.TryGet(language, key, out text), language + " lacks " + key);
            return text;
        }

        /// <summary>The key exists in both languages, every {n} of the text has an argument, and the result has no {n} and no [key] left.</summary>
        private static void AssertSaid(string key, string[] args)
        {
            foreach (string language in new[] { "ru", "en" })
            {
                string raw = Raw(language, key);
                foreach (Match m in Placeholder.Matches(raw))
                {
                    int index = int.Parse(m.Groups[1].Value);
                    Assert.True(index < args.Length, language + ":" + key + " uses {" + index + "} but there are " + args.Length + " arguments");
                }

                string said = ViewTestData.Text(language).L(key, args);
                Assert.False(said.StartsWith("["), language + ":" + key + " is missing");
                Assert.False(Placeholder.IsMatch(said), language + ":" + key + " has a placeholder left: " + said);
            }
        }

        private static string Snapshot(PlatformTimeline timeline)
        {
            var save = new PlatformGenerationsSave();
            timeline.WriteTo(save);
            return PTMod.Core.Logic.Json.ModJson.Write(PlatformSaveCodec.Write(save), false);
        }

        // ---- the announcement (FR-2) ----

        [Test]
        public void Announced_NamesThePlatform_InBothLanguages()
        {
            GenerationDef gen = Gen("gen-1990-pga");
            Notice notice = PlatformNotices.Announced(gen, ViewTestData.Text("ru"));

            Assert.Equal(PlatformNotices.KeyAnnounced, notice.Key);
            Assert.Equal("Анонс платформы: Socket 1–3", ViewTestData.Say(ViewTestData.Text("ru"), notice));
            Assert.Equal("Platform announced: Socket 1–3", ViewTestData.Say(ViewTestData.Text("en"), notice));
        }

        [Test]
        public void AnnounceMail_AC21_HasTheTagThePriorityKeysAndTheArguments()
        {
            GenerationDef gen = Gen("gen-1990-pga");   // obsolete from 2000
            PlatformBalance balance = ViewTestData.Balance();
            MailPlan mail = PlatformNotices.AnnounceMail(gen, balance, ViewTestData.Text("ru"));

            Assert.Equal("pg:announce:gen-1990-pga", mail.Tag);
            Assert.Equal(PlatformNotices.KeyAnnounceTitle, mail.TitleKey);
            Assert.Equal(PlatformNotices.KeyAnnounceBodyObsolete, mail.BodyKey, "this generation has an obsolete year");
            Assert.Equal(new[] { "Socket 1–3", "PGA 1990", "SDR", "3", "24", "6", "60", "2000" }, mail.Args);

            AssertSaid(mail.TitleKey, mail.Args);
            AssertSaid(mail.BodyKey, mail.Args);
            ViewText ru = ViewTestData.Text("ru");
            Assert.Equal("Анонс платформы: Socket 1–3", ru.L(mail.TitleKey, mail.Args));
            string body = ru.L(mail.BodyKey, mail.Args);
            Assert.True(body.Contains("Сокеты: PGA 1990."), body);
            Assert.True(body.Contains("Память: SDR."), body);
            Assert.True(body.Contains("в течение 24 мес. после запуска"), body);
            Assert.True(body.Contains("+6 %"), body);
            Assert.True(body.Contains("затухает за 60 мес."), body);
            Assert.True(body.Contains("устареет с 2000 года"), body);
        }

        [Test]
        public void AnnounceMail_OfAGenerationThatNeverBecomesObsolete_HasNoSentenceAboutIt()
        {
            GenerationDef gen = Gen("gen-2030");
            Assert.Equal(0, gen.obsoleteYear);
            MailPlan mail = PlatformNotices.AnnounceMail(gen, ViewTestData.Balance(), ViewTestData.Text("en"));

            Assert.Equal(PlatformNotices.KeyAnnounceBody, mail.BodyKey);
            Assert.Equal(7, mail.Args.Length);
            Assert.Equal("pg:announce:gen-2030", mail.Tag);
            Assert.Equal("LGA 2030 A, LGA 2030 B", mail.Args[1]);
            AssertSaid(mail.TitleKey, mail.Args);
            AssertSaid(mail.BodyKey, mail.Args);
            Assert.False(ViewTestData.Text("en").L(mail.BodyKey, mail.Args).Contains("obsolete"));
        }

        [Test]
        public void AnnounceMail_UsesTheNumbersOfTheBalance()
        {
            PlatformBalance balance = ViewTestData.Balance();
            balance.Rating.EarlyAdopterSlots = 5;
            balance.Rating.EarlyBonus = 0.075f;
            balance.Rating.EarlyDurationMonths = 48;

            MailPlan mail = PlatformNotices.AnnounceMail(Gen("gen-2000"), balance, ViewTestData.Text("ru"));

            Assert.Equal("5", mail.Args[3]);
            Assert.Equal("24", mail.Args[4]);
            Assert.Equal("7.5", mail.Args[5]);
            Assert.Equal("48", mail.Args[6]);
        }

        // ---- the launch and the early adopter (FR-3) ----

        [Test]
        public void Launched_NamesTheCompanyAndThePlatform()
        {
            GenerationDef gen = Gen("gen-2000");
            Notice notice = PlatformNotices.Launched(gen, "Intelligent", ViewTestData.Text("ru"));

            Assert.Equal(PlatformNotices.KeyLaunched, notice.Key);
            Assert.Equal("Intelligent первой выпустила CPU на платформе LGA 775 / Socket 939", ViewTestData.Say(ViewTestData.Text("ru"), notice));
            Assert.Equal("Intelligent shipped the first CPU on LGA 775 / Socket 939", ViewTestData.Say(ViewTestData.Text("en"), notice));
        }

        [Test]
        public void EarlyAdopter_AC31_SixPercentForSixtyMonths()
        {
            GenerationDef gen = Gen("gen-2000");
            Notice notice = PlatformNotices.EarlyAdopter(gen, ViewTestData.Balance(), ViewTestData.Text("ru"));

            Assert.Equal("Вы — ранний последователь LGA 775 / Socket 939: +6 % на 60 мес.", ViewTestData.Say(ViewTestData.Text("ru"), notice));
            Assert.Equal("You are an early adopter of LGA 775 / Socket 939: +6% for 60 mo", ViewTestData.Say(ViewTestData.Text("en"), notice));
        }

        [Test]
        public void EarlyMail_HasTheTagAndThePioneerHasABodyOfItsOwn()
        {
            GenerationDef gen = Gen("gen-2000");
            PlatformBalance balance = ViewTestData.Balance();

            MailPlan pioneer = PlatformNotices.EarlyMail(gen, 1, balance, ViewTestData.Text("ru"));
            MailPlan second = PlatformNotices.EarlyMail(gen, 2, balance, ViewTestData.Text("ru"));

            Assert.Equal("pg:early:gen-2000", pioneer.Tag);
            Assert.Equal(PlatformNotices.KeyEarlyTitle, pioneer.TitleKey);
            Assert.Equal(PlatformNotices.KeyEarlyBodyPioneer, pioneer.BodyKey);
            Assert.Equal(PlatformNotices.KeyEarlyBody, second.BodyKey);
            Assert.Equal(new[] { "LGA 775 / Socket 939", "6", "60", "1", "3" }, pioneer.Args);
            Assert.Equal(new[] { "LGA 775 / Socket 939", "6", "60", "2", "3" }, second.Args);

            foreach (MailPlan mail in new[] { pioneer, second })
            {
                AssertSaid(mail.TitleKey, mail.Args);
                AssertSaid(mail.BodyKey, mail.Args);
            }

            ViewText ru = ViewTestData.Text("ru");
            Assert.Equal("Вы — ранний последователь: LGA 775 / Socket 939", ru.L(pioneer.TitleKey, pioneer.Args));
            string body = ru.L(pioneer.BodyKey, pioneer.Args);
            Assert.True(body.Contains("+6 %") && body.Contains("60 мес.") && body.Contains("место 1 из 3"), body);
            string secondBody = ru.L(second.BodyKey, second.Args);
            Assert.True(secondBody.Contains("место 2 из 3"), secondBody);
        }

        // ---- the socket change (FR-6, FR-7) ----

        [Test]
        public void Churn_AC61_SixPointSevenPercentForTwentyFourMonths()
        {
            Notice notice = PlatformNotices.Churn("PGA 1995", 0.0667f, 24, "PGA 1990", 12, 36);

            Assert.Equal(PlatformNotices.KeyChurn, notice.Key);
            Assert.Equal("Смена сокета: −6.7 % к рейтингу CPU на PGA 1995 на 24 мес. Основной сокет PGA 1990 поддерживается 12 мес. из 36",
                ViewTestData.Say(ViewTestData.Text("ru"), notice));
            Assert.Equal("Socket switch: −6.7% rating for CPUs on PGA 1995 for 24 mo. Main socket PGA 1990 supported 12 of 36 mo",
                ViewTestData.Say(ViewTestData.Text("en"), notice));
        }

        [Test]
        public void ReleaseWarnings_AC71_TheSocketChangeIsAnnouncedBeforeTheRelease()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            SocketId next = ViewTestData.Std("PGA 1995");
            GenerationDef gen = s.Catalog.BySocket(next);
            ReleaseOutcome outcome = s.Timeline.Preview(ViewTestData.Player, next, gen, s.Today, s.InstalledBase, s.Catalog, s.Balance);

            ViewText ru = ViewTestData.Text("ru");
            List<Notice> warnings = PlatformNotices.ReleaseWarnings(outcome, gen, "PGA 1995", "PGA 1990", s.Balance, ru);

            Assert.Equal(1, warnings.Count, "a penalty and no early transition: the generation is long launched");
            Assert.Equal(PlatformNotices.KeyChurn, warnings[0].Key);
            Assert.Equal("Смена сокета: −6.7 % к рейтингу CPU на PGA 1995 на 24 мес. Основной сокет PGA 1990 поддерживается 12 мес. из 36",
                ViewTestData.Say(ru, warnings[0]));
        }

        [Test]
        public void ReleaseWarnings_AnEarlyTransition_IsAnnouncedWithTheBonus()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            s.InstalledBase = new InstalledBaseLedger();   // the player has no main socket: no penalty to talk about
            SocketId next = ViewTestData.Std("LGA 2000");
            GenerationDef gen = s.Catalog.BySocket(next);
            ReleaseOutcome outcome = s.Timeline.Preview(ViewTestData.Player, next, gen, ViewTestData.D(2000, 3, 14), s.InstalledBase, s.Catalog, s.Balance);

            List<Notice> warnings = PlatformNotices.ReleaseWarnings(outcome, gen, "LGA 2000", string.Empty, s.Balance, ViewTestData.Text("ru"));

            Assert.Equal(1, warnings.Count);
            Assert.Equal(PlatformNotices.KeyWarnEarly, warnings[0].Key);
            Assert.Equal("Ранний переход на LGA 775 / Socket 939: +6 % на 60 мес.", ViewTestData.Say(ViewTestData.Text("ru"), warnings[0]));
            Assert.Equal("Early transition to LGA 775 / Socket 939: +6% for 60 mo", ViewTestData.Say(ViewTestData.Text("en"), warnings[0]));
        }

        [Test]
        public void ReleaseWarnings_AnEarlyTransitionThatAlsoChangesTheSocket_GivesBoth_ThePenaltyFirst()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            SocketId next = ViewTestData.Std("LGA 2000");
            GenerationDef gen = s.Catalog.BySocket(next);
            ReleaseOutcome outcome = s.Timeline.Preview(ViewTestData.Player, next, gen, ViewTestData.D(2000, 3, 14), s.InstalledBase, s.Catalog, s.Balance);

            List<Notice> warnings = PlatformNotices.ReleaseWarnings(outcome, gen, "LGA 2000", "PGA 1990", s.Balance, ViewTestData.Text("ru"));

            Assert.Equal(2, warnings.Count);
            Assert.Equal(PlatformNotices.KeyChurn, warnings[0].Key);
            Assert.Equal(PlatformNotices.KeyWarnEarly, warnings[1].Key);
            Assert.True(ViewTestData.Say(ViewTestData.Text("ru"), warnings[0]).Contains("поддерживается 14 мес. из 36"), "14 months from 1 January 1999 to 14 March 2000");
        }

        [Test]
        public void ReleaseWarnings_ForACpuThatChangesNothing_ShowNothing()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            SocketId known = ViewTestData.Std("PGA 1990");   // the player's own socket, long launched
            GenerationDef gen = s.Catalog.BySocket(known);
            ReleaseOutcome outcome = s.Timeline.Preview(ViewTestData.Player, known, gen, s.Today, s.InstalledBase, s.Catalog, s.Balance);

            Assert.Equal(0, PlatformNotices.ReleaseWarnings(outcome, gen, "PGA 1990", string.Empty, s.Balance, ViewTestData.Text("ru")).Count);
            Assert.Equal(0, PlatformNotices.ReleaseWarnings(default(ReleaseOutcome), null, string.Empty, string.Empty, s.Balance, ViewTestData.Text("ru")).Count);
        }

        [Test]
        public void ReleaseWarnings_AreMadeFromThePreview_WhichChangesNothing()
        {
            PlatformSnapshot s = ViewTestData.Ac61();
            SocketId next = ViewTestData.Std("LGA 2000");
            GenerationDef gen = s.Catalog.BySocket(next);
            string before = Snapshot(s.Timeline);

            ReleaseOutcome outcome = s.Timeline.Preview(ViewTestData.Player, next, gen, ViewTestData.D(2000, 3, 14), s.InstalledBase, s.Catalog, s.Balance);
            PlatformNotices.ReleaseWarnings(outcome, gen, "LGA 2000", "PGA 1990", s.Balance, ViewTestData.Text("ru"));

            Assert.Equal(before, Snapshot(s.Timeline), "the state is the same after the warning");
            GameDate none;
            string nobody;
            Assert.False(s.Timeline.IsLaunched("gen-2000", out none, out nobody));
        }

        // ---- every text names its arguments ----

        [Test]
        public void EveryNotice_HasAnArgumentForEveryPlaceholderOfItsText_InBothLanguages()
        {
            GenerationDef gen = Gen("gen-2000");
            PlatformBalance balance = ViewTestData.Balance();
            ViewText ru = ViewTestData.Text("ru");

            var notices = new List<Notice>
            {
                PlatformNotices.Announced(gen, ru),
                PlatformNotices.Launched(gen, "Intelligent", ru),
                PlatformNotices.EarlyAdopter(gen, balance, ru),
                PlatformNotices.Churn("PGA 1995", 0.0667f, 24, "PGA 1990", 12, 36),
            };
            var early = new ReleaseOutcome { EarlyAdopter = true, EarlySlot = 1 };
            notices.AddRange(PlatformNotices.ReleaseWarnings(early, gen, "LGA 2000", string.Empty, balance, ru));
            Assert.Equal(PlatformNotices.KeyWarnEarly, notices[notices.Count - 1].Key);

            foreach (Notice notice in notices) AssertSaid(notice.Key, notice.Args);
        }

        [Test]
        public void NullArguments_GiveReadableTexts_NotExceptions()
        {
            Notice launched = PlatformNotices.Launched(null, null, null);
            Assert.Equal(2, launched.Args.Length);
            Assert.Equal(string.Empty, launched.Args[0]);

            MailPlan announce = PlatformNotices.AnnounceMail(null, null, null);
            Assert.Equal("pg:announce:", announce.Tag);
            Assert.True(announce.Args.Length >= 7);

            MailPlan early = PlatformNotices.EarlyMail(null, 1, null, null);
            Assert.Equal("pg:early:", early.Tag);
            Assert.Equal(5, early.Args.Length);
        }
    }
}
