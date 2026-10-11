using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The friendly offer of a foundry (FR-12, AC-12.1, TSK-022): <see cref="PartnershipOfferCore"/> on the shipped content, the real books of the feature,
    /// a fake mail service and scripted dice - who rolls, how often, what is recorded - and its place in the monthly pass. The mail, the action that opens
    /// the negotiations and the stream are read as text (they need the game).
    /// </summary>
    [Covers(typeof(PartnershipOfferCore), typeof(IPartnershipMail), typeof(PartnershipNotice), typeof(PartnershipResult))]
    public class PartnershipTests
    {
        private const int Gf = 8;      // GlobalFoundries: foundry_partner
        private const int Tsmc = 9;    // TSMC: foundry_partner, baseline +20
        private const int Amd = AttitudeRig.Amd;

        private static readonly DateTime May = new DateTime(1990, 5, 1);

        private sealed class FakeMail : IPartnershipMail
        {
            public readonly List<PartnershipNotice> Sent = new List<PartnershipNotice>();
            public int NextId = 500;
            public bool Refuse;
            public bool Throw;

            public int SendOffer(PartnershipNotice notice)
            {
                if (Throw) throw new InvalidOperationException("boom");
                if (Refuse) return -1;
                Sent.Add(notice);
                return NextId++;
            }
        }

        private sealed class Rig
        {
            public readonly AttitudeRig Base = new AttitudeRig().AssignAll();
            public readonly ReactionBook Reactions;
            public readonly FakeMail Mail = new FakeMail();
            public readonly ScriptedRng Rng = new ScriptedRng();
            public readonly PartnershipOfferCore Core;
            public readonly List<int> Offered = new List<int>();
            public readonly List<string> Log = new List<string>();

            public Rig(RivalConfig config = null)
            {
                Reactions = new ReactionBook(() => Base.State);
                RivalConfig used = config ?? Base.Catalog.Config;
                Core = new PartnershipOfferCore(Base.Book, Base.Ledger, Reactions, () => used, Mail, () => Rng, Log.Add);
                Core.Offered += Offered.Add;
            }

            /// <summary>The facts of a monthly pass: both foundries, Intel and AMD; the given rivals have a contract with the player.</summary>
            public MonthlyInput Input(DateTime today, params int[] contractRivals)
            {
                var input = new MonthlyInput { Today = today };
                input.Rivals.Add(new RivalMarketView { UniqueId = AttitudeRig.Intel });
                input.Rivals.Add(new RivalMarketView { UniqueId = Amd });
                input.Rivals.Add(new RivalMarketView { UniqueId = Gf, IsFoundry = true });
                input.Rivals.Add(new RivalMarketView { UniqueId = Tsmc, IsFoundry = true });
                input.ContractRivals.AddRange(contractRivals);
                return input;
            }
        }

        // ---- who rolls ----

        [Test]
        public void ATsmcWithAnAttitudeOf45_Rolls_AndAWinningRollSendsTheMail()
        {
            // AC-12.1
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 45f);
            rig.Rng.Chances.Enqueue(true);

            PartnershipResult result = rig.Core.Run(rig.Input(May));

            Assert.Equal(1, result.Rolled);
            Assert.Equal(1, result.Sent);
            Assert.Equal(1, rig.Mail.Sent.Count);
            Assert.Equal(Tsmc, rig.Mail.Sent[0].UniqueId);
            Assert.Equal(May, rig.Mail.Sent[0].Month);
            Assert.Equal(new List<int> { Tsmc }, rig.Offered, "the event for the API");
            Assert.Equal(0.10f, rig.Rng.Probabilities[0], "the chance is 0.10");
        }

        [Test]
        public void TheAttitude_MustReach40()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 39.9f);
            rig.Base.SetAttitude(Gf, 39.9f);
            rig.Rng.DefaultChance = true;

            PartnershipResult below = rig.Core.Run(rig.Input(May));

            Assert.Equal(0, below.Rolled, "not even a roll");
            Assert.Equal(0, rig.Rng.Probabilities.Count, "no draw of the stream");

            rig.Base.SetAttitude(Tsmc, 40f);
            PartnershipResult at = rig.Core.Run(rig.Input(May));
            Assert.Equal(1, at.Rolled, "40 is enough");
            Assert.Equal(1, at.Sent);
        }

        [Test]
        public void AFoundryWithABaselineOf20_DoesNotOfferAnything_UntilTheFriendshipIsEarned()
        {
            var rig = new Rig();
            rig.Rng.DefaultChance = true;

            PartnershipResult result = rig.Core.Run(rig.Input(May));

            Assert.Equal(0, result.Rolled);
            Assert.Equal(0, rig.Mail.Sent.Count);
        }

        [Test]
        public void WithAContractWithThePlayer_ThereIsNoOffer()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Rng.DefaultChance = true;

            PartnershipResult result = rig.Core.Run(rig.Input(May, Tsmc));

            Assert.Equal(0, result.Rolled);
            Assert.Equal(0, rig.Mail.Sent.Count);
            Assert.Equal(0, rig.Rng.Probabilities.Count);
        }

        [Test]
        public void ARivalThatIsNoFoundry_NeverOffers_WhateverItsAttitude()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(AttitudeRig.Intel, 100f);
            rig.Base.SetAttitude(Amd, 100f);
            rig.Rng.DefaultChance = true;

            Assert.Equal(0, rig.Core.Run(rig.Input(May)).Rolled);
        }

        [Test]
        public void ABankruptFoundry_ADormantOne_AndOneWithoutACharacter_DoNotRoll()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 90f);
            rig.Base.SetAttitude(Gf, 90f);
            rig.Rng.DefaultChance = true;

            MonthlyInput input = rig.Input(May);
            input.Rivals.First(r => r.UniqueId == Tsmc).IsBankrupt = true;
            rig.Base.Book.SetDormant(Gf, true);
            Assert.Equal(0, rig.Core.Run(input).Rolled, "bankrupt and dormant");

            rig.Base.Book.SetDormant(Gf, false);
            input.Rivals.Add(new RivalMarketView { UniqueId = 40, IsFoundry = true });   // a foundry that has no character
            PartnershipResult result = rig.Core.Run(input);
            Assert.Equal(1, result.Rolled, "only GlobalFoundries");
            Assert.Equal(Gf, rig.Mail.Sent[0].UniqueId);
        }

        [Test]
        public void EachEligibleFoundry_RollsOnce_InTheOrderOfTheList()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 70f);
            rig.Base.SetAttitude(Gf, 70f);
            rig.Rng.Chances.Enqueue(false);
            rig.Rng.Chances.Enqueue(true);

            PartnershipResult result = rig.Core.Run(rig.Input(May));

            Assert.Equal(2, result.Rolled);
            Assert.Equal(1, result.Sent);
            Assert.Equal(Tsmc, rig.Mail.Sent[0].UniqueId, "GlobalFoundries missed, TSMC won");
            Assert.Equal(2, rig.Rng.Probabilities.Count, "one draw each");
        }

        // ---- not more often than once in six months ----

        [Test]
        public void AfterAnOffer_TheFoundryWaitsSixMonths()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Rng.DefaultChance = true;
            Assert.Equal(1, rig.Core.Run(rig.Input(May)).Sent);

            Assert.Equal(0, rig.Core.Run(rig.Input(new DateTime(1990, 6, 1))).Rolled, "1 month");
            Assert.Equal(0, rig.Core.Run(rig.Input(new DateTime(1990, 10, 1))).Rolled, "5 months");
            PartnershipResult sixth = rig.Core.Run(rig.Input(new DateTime(1990, 11, 1)));
            Assert.Equal(1, sixth.Rolled, "6 months");
            Assert.Equal(1, sixth.Sent);
            Assert.Equal(2, rig.Mail.Sent.Count);
        }

        [Test]
        public void TheCooldown_IsPerFoundry()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Base.SetAttitude(Gf, 80f);
            rig.Rng.Chances.Enqueue(false);   // GlobalFoundries misses
            rig.Rng.Chances.Enqueue(true);    // TSMC offers
            rig.Core.Run(rig.Input(May));

            rig.Rng.DefaultChance = true;
            PartnershipResult next = rig.Core.Run(rig.Input(new DateTime(1990, 6, 1)));

            Assert.Equal(1, next.Rolled, "TSMC is still waiting");
            Assert.Equal(Gf, rig.Mail.Sent[1].UniqueId);
        }

        [Test]
        public void ALosingRoll_RecordsNothing_SoTheNextMonthRollsAgain()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);

            Assert.Equal(1, rig.Core.Run(rig.Input(May)).Rolled);
            Assert.Equal(0, rig.Mail.Sent.Count);
            Assert.Equal(int.MaxValue, rig.Reactions.MonthsSincePartnerOffer(Tsmc, May));

            Assert.Equal(1, rig.Core.Run(rig.Input(May.AddMonths(1))).Rolled);
        }

        [Test]
        public void AMailTheServiceRefuses_IsNotAnOffer_AndRollsAgainNextMonth()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Rng.DefaultChance = true;
            rig.Mail.Refuse = true;

            PartnershipResult first = rig.Core.Run(rig.Input(May));

            Assert.Equal(1, first.Refused);
            Assert.Equal(0, first.Sent);
            Assert.Equal(0, rig.Offered.Count, "no event for the API");
            Assert.Equal(int.MaxValue, rig.Reactions.MonthsSincePartnerOffer(Tsmc, May), "the cooldown did not start");

            rig.Mail.Refuse = false;
            Assert.Equal(1, rig.Core.Run(rig.Input(May.AddMonths(1))).Sent);
        }

        [Test]
        public void TheDayOfTheLastOffer_IsSavedInTheSection()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Rng.DefaultChance = true;
            rig.Core.Run(rig.Input(May));

            RivalState back = RivalSaveCodec.Read(RivalSaveCodec.Write(rig.Base.State), null);
            var loaded = new ReactionBook(() => back);

            Assert.Equal(0, loaded.MonthsSincePartnerOffer(Tsmc, May));
            Assert.Equal(5, loaded.MonthsSincePartnerOffer(Tsmc, new DateTime(1990, 10, 1)));
            Assert.Equal("1990-05-01", back.counters.lastPartnerByUid.Single(l => l.uid == Tsmc).date);
        }

        [Test]
        public void MonthsSinceTheLastOffer_CountCalendarMonths_AndNeverGoBelowZero()
        {
            var rig = new Rig();
            rig.Reactions.MarkPartnerOffer(Tsmc, new DateTime(1990, 3, 31));

            Assert.Equal(6, rig.Reactions.MonthsSincePartnerOffer(Tsmc, new DateTime(1990, 9, 1)));
            Assert.Equal(0, rig.Reactions.MonthsSincePartnerOffer(Tsmc, new DateTime(1990, 3, 1)));
            Assert.Equal(0, rig.Reactions.MonthsSincePartnerOffer(Tsmc, new DateTime(1989, 1, 1)), "a date before the offer");
            Assert.Equal(int.MaxValue, rig.Reactions.MonthsSincePartnerOffer(Gf, new DateTime(1990, 9, 1)), "never offered");
            Assert.Equal(7, rig.Reactions.MonthsSincePartnerOffer(Tsmc, new DateTime(1990, 10, 15)));
        }

        // ---- the numbers of the config ----

        [Test]
        public void TheShippedConfig_HasTheNumbersOfTheSpec()
        {
            RivalConfig.PartnershipReaction rules = new Rig().Base.Catalog.Config.reactions.Partnership;

            Assert.Equal(40, rules.minAttitude);
            Assert.Equal(0.10f, rules.monthlyChance);
            Assert.Equal(6, rules.repeatMonths);
        }

        [Test]
        public void TheConfig_SetsTheAttitudeTheChanceAndTheCooldown()
        {
            RivalConfig config = RivalConfig.Defaults();
            config.reactions.Partnership.minAttitude = 10;
            config.reactions.Partnership.monthlyChance = 0.25f;
            config.reactions.Partnership.repeatMonths = 2;
            var rig = new Rig(config);
            rig.Base.SetAttitude(Tsmc, 12f);
            rig.Base.SetAttitude(Gf, 5f);   // below the lowered border
            rig.Rng.DefaultChance = true;

            Assert.Equal(1, rig.Core.Run(rig.Input(May)).Sent);
            Assert.Equal(0.25f, rig.Rng.Probabilities[0]);
            Assert.Equal(0, rig.Core.Run(rig.Input(May.AddMonths(1))).Rolled);
            Assert.Equal(1, rig.Core.Run(rig.Input(May.AddMonths(2))).Rolled);
        }

        [Test]
        public void ARollerWithoutAMailServiceOrAnInput_DoesNothing()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 80f);
            var noMail = new PartnershipOfferCore(rig.Base.Book, rig.Base.Ledger, rig.Reactions, () => rig.Base.Catalog.Config, null, () => rig.Rng);

            Assert.Equal(0, noMail.Run(rig.Input(May)).Rolled);
            Assert.Equal(0, rig.Core.Run(null).Rolled);
            Assert.Equal(0, rig.Core.Run(new MonthlyInput()).Rolled, "no date");
        }

        // ---- the tag ----

        [Test]
        public void TheTag_IsRivalPartnerMonthAndId()
        {
            Assert.Equal("rival:partner:1990-05:9", ReactionTags.Partner(new DateTime(1990, 5, 17), 9));
            Assert.Equal("rival:partner:1990-05:", ReactionTags.PartnerPrefix + "1990-05:");
            Assert.Equal("openNegotiation", ReactionTags.ActionOpenNegotiation);
        }

        [Test]
        public void TheTag_IsReadBack()
        {
            int uid;

            Assert.True(ReactionTags.TryParsePartner("rival:partner:1990-05:9", out uid));
            Assert.Equal(9, uid);
            Assert.True(ReactionTags.TryParsePartner(ReactionTags.Partner(new DateTime(2001, 12, 3), 12), out uid));
            Assert.Equal(12, uid);

            foreach (string tag in new[] { null, "", "rival:partner:", "rival:intel:r-3", "rival:partner:1990-05", "rival:partner:1990-5:9", "rival:partner:1990-13:9", "rival:partner:1990-05:x", "rival:partner:1990-05:-3", "rival:partner:1990-05:" })
            {
                Assert.False(ReactionTags.TryParsePartner(tag, out uid), tag ?? "null");
            }
        }

        // ---- the place in the monthly pass ----

        private static MonthlyEvaluatorCore Monthly(Rig rig)
        {
            return new MonthlyEvaluatorCore(() => rig.Base.State, () => rig.Base.Catalog.Config, rig.Base.Recorder, rig.Base.Ledger, rig.Log.Add, rig.Core);
        }

        [Test]
        public void TheMonthlyPass_RollsTheOffers_AndCountsThem()
        {
            var rig = new Rig();
            rig.Base.Today = May;
            rig.Base.SetAttitude(Tsmc, 80f);
            rig.Rng.DefaultChance = true;

            MonthlyResult result = Monthly(rig).Run(rig.Input(May));

            Assert.Equal(1, result.Partnerships);
            Assert.Equal(0, result.Failures);
            Assert.Equal(1, rig.Mail.Sent.Count);
        }

        [Test]
        public void TheRoll_ComesAfterTheDriftOfTheMonth()
        {
            // TSMC at 41 drifts 10 % of the way to its baseline of 20: 38.9 - below 40, so there is no offer this month
            var rig = new Rig();
            rig.Base.Today = May;
            rig.Base.SetAttitude(Tsmc, 41f);
            rig.Rng.DefaultChance = true;

            MonthlyResult result = Monthly(rig).Run(rig.Input(May));

            Assert.Equal(0, result.Partnerships);
            Assert.Equal(1, result.Drifted);
            Assert.Equal(0, rig.Mail.Sent.Count);
            Assert.True(rig.Base.Ledger.Get(Tsmc) < 40f);
        }

        [Test]
        public void AMailServiceThatThrows_IsARefusedOffer_AndTheOtherFoundryStillRolls()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 90f);
            rig.Base.SetAttitude(Gf, 90f);
            rig.Rng.DefaultChance = true;
            rig.Mail.Throw = true;

            PartnershipResult result = rig.Core.Run(rig.Input(May));

            Assert.Equal(2, result.Rolled, "both foundries rolled");
            Assert.Equal(2, result.Refused);
            Assert.Equal(0, result.Sent);
            Assert.Equal(int.MaxValue, rig.Reactions.MonthsSincePartnerOffer(Tsmc, May), "nothing was recorded");
            Assert.True(rig.Log.Count(l => l.Contains("mail service failed")) == 2, string.Join("; ", rig.Log));
        }

        [Test]
        public void AHandlerThatThrows_DoesNotStopTheOthers_OrTheRoll()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 90f);
            rig.Base.SetAttitude(Gf, 90f);
            rig.Rng.DefaultChance = true;
            var late = new List<int>();
            rig.Core.Offered += uid => { throw new InvalidOperationException("boom"); };
            rig.Core.Offered += late.Add;

            PartnershipResult result = rig.Core.Run(rig.Input(May));

            Assert.Equal(2, result.Sent, "both offers went out");
            Assert.Equal(new List<int> { Gf, Tsmc }, late, "the handler after the one that throws heard both");
            Assert.Equal(new List<int> { Gf, Tsmc }, rig.Offered, "and the one before it");
            Assert.Equal(2, rig.Log.Count(l => l.Contains("handler of Offered failed")));
        }

        [Test]
        public void AFailingStepOfThePass_DoesNotStopTheRestOfIt()
        {
            var rig = new Rig();
            rig.Base.Today = May;
            rig.Base.State.cautions.Add(new CautionEntry { uid = 4, flawId = 1, until = "1990-01-01" });
            var broken = new PartnershipOfferCore(rig.Base.Book, rig.Base.Ledger, rig.Reactions, () => rig.Base.Catalog.Config, rig.Mail,
                () => { throw new InvalidOperationException("no dice"); }, rig.Log.Add);
            rig.Base.SetAttitude(Tsmc, 90f);
            var pass = new MonthlyEvaluatorCore(() => rig.Base.State, () => rig.Base.Catalog.Config, rig.Base.Recorder, rig.Base.Ledger, rig.Log.Add, broken);

            MonthlyResult result = pass.Run(rig.Input(May));

            Assert.Equal(1, result.Failures);
            Assert.Equal(1, result.Expired, "the housekeeping after it still ran");
            Assert.True(rig.Log.Any(l => l.Contains("partnership")), string.Join("; ", rig.Log));
        }

        [Test]
        public void WithoutTheRoll_ThePassIsAsBefore()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(Tsmc, 90f);
            rig.Rng.DefaultChance = true;

            MonthlyResult result = rig.Base.Monthly.Run(rig.Input(May));

            Assert.Equal(0, result.Partnerships);
            Assert.Equal(0, rig.Mail.Sent.Count);
        }

        // ---- the game side (read as text) ----

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities" }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheMail_IsLowPriority_WithTheTagAndTheActionOfTheSpec()
        {
            string offer = Code("Reactions", "PartnershipOffer.cs");

            Assert.True(Regex.IsMatch(offer, @"Priority = MailPriority\.Low,"), "Low");
            Assert.True(Regex.IsMatch(offer, @"Tag = ReactionTags\.Partner\(notice\.Month, notice\.UniqueId\),"), "rival:partner:<yyyy-MM>:<uid>");
            Assert.True(Regex.IsMatch(offer, @"TitleKey = ""rival\.mail\.partner\.subject""") && Regex.IsMatch(offer, @"BodyKey = ""rival\.mail\.partner\.body"""));
            Assert.True(Regex.IsMatch(offer, @"new ModMailAction \{ Id = ReactionTags\.ActionOpenNegotiation, LabelKey = ""rival\.mail\.partner\.open"", Repeatable = true \}"),
                "the action Open negotiations, the mail stays open");
            Assert.True(Regex.IsMatch(offer, @"ModMail\.Send\(message\)"));
        }

        [Test]
        public void TheAction_OpensTheNegotiationsOfTheGame_WithTheFoundryAsTheProvider()
        {
            string offer = Code("Reactions", "PartnershipOffer.cs");
            string intel = Code("Reactions", "IntelMail.cs");

            Assert.True(Regex.IsMatch(offer, @"window\.Open\(foundry, playerCompany, false\);"), "the foundry provides, the player is the client");
            Assert.True(Regex.IsMatch(offer, @"FindFirstObjectByType<GameNegotiationWindow>\(UnityEngine\.FindObjectsInactive\.Include\)"), "the window of the scene");
            Assert.True(Regex.IsMatch(offer, @"using GameNegotiationWindow = ProcessorTycoon\.CompanySystem\.Business\.ContractNegotiationWindow;"));
            Assert.True(Regex.IsMatch(offer, @"ModHooks\.Guard\(Feature, ""PartnershipOffer/openNegotiation"", \(\) => OpenNegotiation\(record\)\)"), "a failure stays here");
            Assert.True(Regex.IsMatch(intel, @"ReactionTags\.ActionOpenNegotiation[\s\S]*?PartnershipOffer\.OnAction\(record\);"), "the one action handler of the feature dispatches");
        }

        [Test]
        public void TheRoll_UsesItsOwnStream_AndTheOfferIsInTheHistoryAndTheApi()
        {
            string offer = Code("Reactions", "PartnershipOffer.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(offer, @"StreamId = ""rival-personalities/partner"""), "the dice of the hostile reactions do not move");
            Assert.True(Regex.IsMatch(offer, @"ModRandom\.Stream\(StreamId\)"));
            Assert.True(Regex.IsMatch(offer, @"AddHistory\(uniqueId, ModTicks\.Today, TypeName, -1, HistoryOutcomes\.Started\)"));
            Assert.True(Regex.IsMatch(offer, @"_feature\.Api\.OnReactionStarted\(uniqueId, TypeName\)"), "ReactionStarted(uid, \"Partnership\")");
            Assert.True(Regex.IsMatch(feature, @"new MonthlyEvaluatorCore\([^;]*_partnership\.Core\)"), "the roll is a step of the monthly pass");
            Assert.False(Regex.IsMatch(offer, @"System\.Random|UnityEngine\.Random|new Random\("), "no random numbers of the game");
        }

        [Test]
        public void TheOffer_IsALineInTheHistoryOfTheWindow()
        {
            var rig = new IntelRig();
            rig.Reactions.AddHistory(Tsmc, new DateTime(1990, 5, 1), ReactionKinds.NameOf(ReactionType.Partnership), -1, HistoryOutcomes.Started);

            List<string> texts = IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(Tsmc, "TSMC"), rig.Settings(false, true)).Find("history"));

            Assert.Equal(1, texts.Count);
            Assert.True(texts[0].Contains("Предложение партнёрства"), texts[0]);
            Assert.Equal(0, rig.Text.Missing.Count, string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void ThePartnershipPackOfStrings_IsThere()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                PTMod.Core.Logic.Json.JsonObject table = RivalTestKit.ReadL10n(language);
                foreach (string key in new[] { "rival.mail.partner.subject", "rival.mail.partner.body", "rival.mail.partner.open", "rival.reaction.partnership" })
                {
                    Assert.True(table.ContainsKey(key), language + " lacks " + key);
                }
            }
        }
    }
}
