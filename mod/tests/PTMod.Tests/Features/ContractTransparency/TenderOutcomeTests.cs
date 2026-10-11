using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-008 and TSK-010 (the pure parts): the verdict on a tender in its seven cases (FR-5 items 2 and 3), the result built from what the recorder saw (FR-5 item 1, FR-7) and the
    /// sentence of the letter for each result.
    /// </summary>
    [Covers(typeof(TenderOutcomeClassifier), typeof(TenderFacts), typeof(TenderOutcome), typeof(TenderKind), typeof(TenderReason),
        typeof(TenderAssembler), typeof(TenderOffer), typeof(TenderSnapshot), typeof(TenderRecord), typeof(TenderLines))]
    public class TenderOutcomeTests
    {
        private const int IntelId = 3977;
        private const int ZStrongId = 4110;   // Z-386, IPS 40
        private const int ZWeakId = 4108;     // Z-286, IPS 20

        // ---- the classifier: seven cases ----

        private static TenderFacts Facts(int offersAfter = 2, int chosen = IntelId, bool chosenIsPlayer = false, bool withdrawn = false, bool retired = false, bool shadowed = false)
        {
            return new TenderFacts
            {
                OffersAfter = offersAfter,
                ChosenCpuId = chosen,
                ChosenIsPlayer = chosenIsPlayer,
                PlayerStrongestWithdrawn = withdrawn,
                PlayerCpuRetired = retired,
                PlayerCpuShadowed = shadowed,
            };
        }

        [Test]
        public void Case1_Won_WhenTheChosenCpuIsThePlayers()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(chosen: ZStrongId, chosenIsPlayer: true));

            Assert.Equal(TenderKind.Won, outcome.Kind);
            Assert.Equal(TenderReason.None, outcome.Reason);
            Assert.Equal("Won", outcome.Name);
            Assert.Equal("ct.tender.won", outcome.ReasonKey);
            Assert.Equal("ct.tender.line.won", outcome.LineKey);
        }

        [Test]
        public void Case2_LostOutbid_WhenACompetitorIsDrawn()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts());

            Assert.Equal(TenderKind.Lost, outcome.Kind);
            Assert.Equal(TenderReason.Outbid, outcome.Reason);
            Assert.Equal("Lost", outcome.Name);
            Assert.Equal("ct.tender.outbid", outcome.ReasonKey);
            Assert.Equal("ct.tender.lost.outbid", outcome.LineKey);
        }

        [Test]
        public void Case3_LostAutoReject_WhenThePlayersBestOfferWasWithdrawn()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(offersAfter: 1, withdrawn: true));

            Assert.Equal(TenderKind.Lost, outcome.Kind);
            Assert.Equal(TenderReason.AutoReject, outcome.Reason);
            Assert.Equal("ct.tender.autoReject", outcome.ReasonKey);
            Assert.Equal("ct.tender.lost.autoReject", outcome.LineKey);
        }

        [Test]
        public void Case4_LostShadowed_WhenAStrongerOwnCpuTookItsPlace()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(shadowed: true));

            Assert.Equal(TenderKind.Lost, outcome.Kind);
            Assert.Equal(TenderReason.Shadowed, outcome.Reason);
            Assert.Equal("ct.tender.shadowed", outcome.ReasonKey);
            Assert.Equal("ct.tender.lost.shadowed", outcome.LineKey);
        }

        [Test]
        public void Case5_LostRetired_WhenThePlayersCpuWasRetired()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(retired: true));

            Assert.Equal(TenderKind.Lost, outcome.Kind);
            Assert.Equal(TenderReason.Retired, outcome.Reason);
            Assert.Equal("ct.tender.retired", outcome.ReasonKey);
            Assert.Equal("ct.tender.lost.retired", outcome.LineKey);
        }

        [Test]
        public void Case6_VoidNoOffers_WhenNothingIsLeftAfterTheCheckOfLines()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(offersAfter: 0, chosen: -1, withdrawn: true));

            Assert.Equal(TenderKind.Void, outcome.Kind);
            Assert.Equal(TenderReason.NoOffers, outcome.Reason);
            Assert.Equal("Void", outcome.Name);
            Assert.Equal("ct.tender.noOffers", outcome.ReasonKey);
            Assert.Equal("ct.tender.void.noOffers", outcome.LineKey);
        }

        [Test]
        public void Case7_VoidRetired_WhenTheDrawPickedARetiredCpu()
        {
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(Facts(offersAfter: 2, chosen: -1));

            Assert.Equal(TenderKind.Void, outcome.Kind);
            Assert.Equal(TenderReason.Retired, outcome.Reason);
            Assert.Equal("ct.tender.retired", outcome.ReasonKey);
            Assert.Equal("ct.tender.void.retired", outcome.LineKey);
        }

        [Test]
        public void TheOrderOfTheChecks_IsTheOneOfTheTable()
        {
            // Nothing left beats everything; a void beats a win flag; a win beats the reasons for a defeat; then auto-reject, retired, shadowed, outbid.
            Assert.Equal(TenderReason.NoOffers, TenderOutcomeClassifier.Classify(Facts(offersAfter: 0, chosen: ZStrongId, chosenIsPlayer: true, withdrawn: true)).Reason);
            Assert.Equal(TenderReason.Retired, TenderOutcomeClassifier.Classify(Facts(chosen: -1, chosenIsPlayer: true)).Reason);
            Assert.Equal(TenderKind.Won, TenderOutcomeClassifier.Classify(Facts(chosen: ZStrongId, chosenIsPlayer: true, withdrawn: true, retired: true, shadowed: true)).Kind);
            Assert.Equal(TenderReason.AutoReject, TenderOutcomeClassifier.Classify(Facts(withdrawn: true, retired: true, shadowed: true)).Reason);
            Assert.Equal(TenderReason.Retired, TenderOutcomeClassifier.Classify(Facts(retired: true, shadowed: true)).Reason);
            Assert.Equal(TenderReason.Shadowed, TenderOutcomeClassifier.Classify(Facts(shadowed: true)).Reason);
            Assert.Equal(TenderReason.Outbid, TenderOutcomeClassifier.Classify(Facts()).Reason);
        }

        [Test]
        public void TheNamesOfTheOutcomes_AreTheOnesTheSaveHolds()
        {
            Assert.True(TenderOutcomeClassifier.IsKnownOutcome("Won"));
            Assert.True(TenderOutcomeClassifier.IsKnownOutcome("Lost"));
            Assert.True(TenderOutcomeClassifier.IsKnownOutcome("Void"));
            Assert.False(TenderOutcomeClassifier.IsKnownOutcome("won"));
            Assert.False(TenderOutcomeClassifier.IsKnownOutcome(null));
            Assert.Equal("Won", TenderOutcomeClassifier.OutcomeWon);
            Assert.Equal("Lost", TenderOutcomeClassifier.OutcomeLost);
            Assert.Equal("Void", TenderOutcomeClassifier.OutcomeVoid);
        }

        // ---- the assembler ----

        private static TenderOffer Offer(int id, string company, string cpu, float ips, bool player = false, bool retired = false,
            int required = 0, int contracted = 0, int capacity = 0, string maker = "")
        {
            return new TenderOffer
            {
                CpuId = id,
                CpuName = cpu,
                CompanyKey = player ? "player" : "ai:" + company,
                CompanyName = company,
                IsPlayer = player,
                Ips = ips,
                IsRetired = retired,
                RequiredLines = required,
                ContractedLines = contracted,
                CapacityLines = capacity,
                MakerName = maker,
            };
        }

        private static TenderSnapshot Snapshot(int chosen, bool automateRejection, params TenderOffer[] before)
        {
            var snapshot = new TenderSnapshot
            {
                ContractKey = "Apple|1986-1|52000|299|1",
                ContractLabel = "Apple — Computer, 52 000 шт./мес.",
                Date = new DateTime(1986, 1, 1),
                ChosenCpuId = chosen,
                AutomateRejection = automateRejection,
            };
            snapshot.Before.AddRange(before);
            snapshot.AfterIds.AddRange(before.Select(o => o.CpuId));
            return snapshot;
        }

        private static void Without(TenderSnapshot snapshot, params int[] ids)
        {
            snapshot.AfterIds.RemoveAll(id => ids.Contains(id));
        }

        private static List<KeyValuePair<int, int>> Chances(params int[] idAndChance)
        {
            var list = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < idAndChance.Length; i += 2) list.Add(new KeyValuePair<int, int>(idAndChance[i], idAndChance[i + 1]));
            return list;
        }

        [Test]
        public void Ac51_ALostDraw_HasWinnerChanceAndActualProbability()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));
            snapshot.DrawChances = Chances(IntelId, 60, ZStrongId, 40);

            TenderRecord record = TenderAssembler.Build(snapshot);
            TenderResultDto result = record.Result;

            Assert.Equal(TenderKind.Lost, record.Outcome.Kind);
            Assert.Equal("Lost", result.outcome);
            Assert.Equal("ct.tender.outbid", result.reasonKey);
            Assert.Equal("1986-01-01", result.date);
            Assert.Equal("Apple|1986-1|52000|299|1", result.contractKey);
            Assert.Equal(ZStrongId, result.playerCpuId);
            Assert.Equal("Z-386", result.playerCpuName);
            Assert.Equal(40, result.playerGameChance);
            Assert.Near(0.2105, result.playerActual, 1e-9);
            Assert.Equal(IntelId, result.winnerCpuId);
            Assert.Equal("Intel", result.winnerCompany);
            Assert.Equal("i386", result.winnerCpuName);
            Assert.False(result.capacityNotChecked);

            Assert.Equal(2, result.offers.Count);
            Assert.Equal("Intel", result.offers[0].company);
            Assert.Equal(60, result.offers[0].chance);
            Assert.Near(0.7895, result.offers[0].actual, 1e-9);
            Assert.False(result.offers[0].player);
            Assert.Equal("Z-386", result.offers[1].cpu);
            Assert.Equal(40, result.offers[1].chance);
            Assert.Near(0.2105, result.offers[1].actual, 1e-9);
            Assert.True(result.offers[1].player);
            Assert.Near(40.0, result.offers[1].ips);
            Assert.Equal(0, result.pruned.Count);
            Assert.Equal(0, record.Withdrawn.Count);
        }

        [Test]
        public void AWonDraw_NamesThePlayersCpuAsTheWinner()
        {
            TenderSnapshot snapshot = Snapshot(ZStrongId, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));
            snapshot.DrawChances = Chances(IntelId, 60, ZStrongId, 40);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderKind.Won, record.Outcome.Kind);
            Assert.Equal("Won", record.Result.outcome);
            Assert.Equal("ct.tender.won", record.Result.reasonKey);
            Assert.Equal(ZStrongId, record.Result.winnerCpuId);
            Assert.Equal("Игрок", record.Result.winnerCompany);
        }

        [Test]
        public void TheOrderOfTheDraw_FollowsTheIps_AndEqualIpsKeepTheEarlierOffer()
        {
            // 50 against 50: the player offered second, so the other company is drawn first and has 66.7 %.
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(IntelId, "Intel", "i386", 50f),
                Offer(ZStrongId, "Игрок", "Z-386", 50f, player: true));
            snapshot.DrawChances = Chances(IntelId, 50, ZStrongId, 50);

            TenderResultDto result = TenderAssembler.Build(snapshot).Result;

            Assert.Equal("Intel", result.offers[0].company);
            Assert.Near(0.6667, result.offers[0].actual, 1e-4);
            Assert.Near(0.3333, result.offers[1].actual, 1e-4);
            Assert.Near(0.3333, result.playerActual, 1e-4);
        }

        [Test]
        public void WithoutChancesFromTheGame_TheChancesWithoutModsStandIn()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));
            snapshot.DrawChances = null;

            TenderResultDto result = TenderAssembler.Build(snapshot).Result;

            Assert.Equal(60, result.offers[0].chance);
            Assert.Equal(40, result.playerGameChance);

            snapshot.DrawChances = Chances(IntelId, 60);   // the game left the player's offer out of its answer
            result = TenderAssembler.Build(snapshot).Result;
            Assert.Equal(40, result.playerGameChance, "the missing chance is the chance without mods");
        }

        [Test]
        public void OnlyTheBestCpuOfACompanyPlays_TheOthersAreNotInTheTable()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true),
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZWeakId, "Игрок", "Z-286", 20f, player: true));
            snapshot.DrawChances = Chances(IntelId, 60, ZStrongId, 40);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(2, record.Result.offers.Count);
            Assert.Equal(ZStrongId, record.Result.playerCpuId, "the stronger CPU played for the player");
            Assert.Equal(TenderReason.Shadowed, record.Outcome.Reason, "the CPU offered last (Z-286) stayed out of the draw");
            Assert.Equal("ct.tender.shadowed", record.Result.reasonKey);
        }

        [Test]
        public void WhenTheCpuOfferedLastIsTheStrongest_ADefeatIsPlainOutbid()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(ZWeakId, "Игрок", "Z-286", 20f, player: true),
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));

            Assert.Equal(TenderReason.Outbid, TenderAssembler.Build(snapshot).Outcome.Reason);
        }

        [Test]
        public void Ac52_AnOfferWithdrawnForLackOfLines_IsALossByAutoCancel_WithTheNumbers()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, true,
                Offer(IntelId, "Intel", "i386", 60f, required: 2, contracted: 1, capacity: 10),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5));
            Without(snapshot, ZStrongId);
            snapshot.DrawChances = Chances(IntelId, 100);

            TenderRecord record = TenderAssembler.Build(snapshot);
            TenderResultDto result = record.Result;

            Assert.Equal(TenderKind.Lost, record.Outcome.Kind);
            Assert.Equal(TenderReason.AutoReject, record.Outcome.Reason);
            Assert.Equal(ZStrongId, result.playerCpuId, "the strongest withdrawn offer is the player's CPU of the record");
            Assert.Equal(0, result.playerGameChance);
            Assert.Near(0.0, result.playerActual);
            Assert.False(result.capacityNotChecked, "the game checked: the switch was on");

            Assert.Equal(1, result.offers.Count);
            Assert.Equal(1, result.pruned.Count);
            Assert.Equal(3, result.pruned[0].required);
            Assert.Equal(4, result.pruned[0].contracted);
            Assert.Equal(5, result.pruned[0].capacity);
            Assert.True(result.pruned[0].player);

            Assert.Equal(1, record.Withdrawn.Count);
            LogEntry line = record.Withdrawn[0];
            Assert.Equal(LogTypes.AutoRejected, line.type);
            Assert.Equal(LogSources.Game, line.source);
            Assert.Equal("1986-01-01", line.date);
            Assert.Equal(ZStrongId, line.cpuId);
            Assert.Equal("ct.reason.autoReject", line.reasonKey);
            Assert.Equal(3, line.GetInt(LogDataKeys.Required, -1));
            Assert.Equal(4, line.GetInt(LogDataKeys.Contracted, -1));
            Assert.Equal(5, line.GetInt(LogDataKeys.Capacity, -1));
            Assert.Equal(1, line.GetInt(LogDataKeys.Own, -1), "the company's own factory");
        }

        [Test]
        public void AWithdrawnOfferBuiltByAFoundry_NamesTheFoundry()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, true,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5, maker: "TSMC"));
            Without(snapshot, ZStrongId);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal("TSMC", record.Result.pruned[0].maker);
            Assert.Equal("TSMC", record.Withdrawn[0].Get(LogDataKeys.Maker));
            Assert.Equal(0, record.Withdrawn[0].GetInt(LogDataKeys.Own, -1));
        }

        [Test]
        public void ACompetitorWithdrawnForLackOfLines_IsInTheResult_ButHasNoLineOfItsOwn()
        {
            TenderSnapshot snapshot = Snapshot(ZStrongId, true,
                Offer(IntelId, "Intel", "i386", 60f, required: 9, contracted: 9, capacity: 10),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));
            Without(snapshot, IntelId);
            snapshot.DrawChances = Chances(ZStrongId, 100);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderKind.Won, record.Outcome.Kind);
            Assert.Equal(1, record.Result.pruned.Count);
            Assert.Equal("Intel", record.Result.pruned[0].company);
            Assert.False(record.Result.pruned[0].player);
            Assert.Equal(0, record.Withdrawn.Count, "FR-7 item 3: the competitor leaves the table, only the player's offers have a line");
            Assert.Equal(1, record.Result.offers.Count);
        }

        [Test]
        public void WhenTheStrongestIsWithdrawnButAWeakerOwnOfferPlays_ALossIsStillAnAutoCancelLoss()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, true,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5),
                Offer(ZWeakId, "Игрок", "Z-286", 20f, player: true));
            Without(snapshot, ZStrongId);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderReason.AutoReject, record.Outcome.Reason);
            Assert.Equal(ZWeakId, record.Result.playerCpuId, "the weaker CPU is the one that played");
            Assert.Equal(1, record.Withdrawn.Count);
        }

        [Test]
        public void NothingLeft_IsAVoidTender_AndTheDefeatOfThePlayersOffersIsInTheLog()
        {
            TenderSnapshot snapshot = Snapshot(-1, true,
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5));
            Without(snapshot, ZStrongId);

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderKind.Void, record.Outcome.Kind);
            Assert.Equal(TenderReason.NoOffers, record.Outcome.Reason);
            Assert.Equal("Void", record.Result.outcome);
            Assert.Equal(0, record.Result.offers.Count);
            Assert.True(string.IsNullOrEmpty(record.Result.winnerCompany), "nobody won");
            Assert.Equal(1, record.Withdrawn.Count);
        }

        [Test]
        public void ARetiredCpuDrawn_IsAVoidTender()
        {
            TenderSnapshot snapshot = Snapshot(-1, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderKind.Void, record.Outcome.Kind);
            Assert.Equal(TenderReason.Retired, record.Outcome.Reason);
            Assert.Equal(2, record.Result.offers.Count, "the draw had both offers");
        }

        [Test]
        public void ALosingRetiredCpu_IsALossByRetirement()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, retired: true));

            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.Equal(TenderKind.Lost, record.Outcome.Kind);
            Assert.Equal(TenderReason.Retired, record.Outcome.Reason);
            Assert.Equal("ct.tender.retired", record.Result.reasonKey);
        }

        [Test]
        public void WithTheSwitchOff_AnOfferThatNeedsMoreLinesThanAreFree_IsMarked()
        {
            TenderOffer needy = Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5);
            TenderOffer fits = Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 1, contracted: 4, capacity: 5);
            TenderOffer unknown = Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 0);
            TenderOffer rival = Offer(IntelId, "Intel", "i386", 60f);

            Assert.True(TenderAssembler.Build(Snapshot(IntelId, false, rival, needy)).Result.capacityNotChecked, "the game did not check and the lines are short: fines are possible");
            Assert.False(TenderAssembler.Build(Snapshot(IntelId, true, rival, needy)).Result.capacityNotChecked, "the game checked");
            Assert.False(TenderAssembler.Build(Snapshot(IntelId, false, rival, fits)).Result.capacityNotChecked, "the lines are enough");
            Assert.False(TenderAssembler.Build(Snapshot(IntelId, false, rival, unknown)).Result.capacityNotChecked, "the capacity is not known");
        }

        [Test]
        public void ADrawWithoutAnOfferOfThePlayer_HasNoRecord()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, true,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(3978, "AMD", "Am386", 50f));

            Assert.Null(TenderAssembler.Build(snapshot));
            Assert.Null(TenderAssembler.Build(null));
            Assert.Null(TenderAssembler.Build(new TenderSnapshot()));
        }

        [Test]
        public void TheResult_IsReadyForTheLog()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, false,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true));

            var model = new ContractLogModel();
            TenderRecord record = TenderAssembler.Build(snapshot);

            Assert.True(model.AddTender(record.Result), "the sanitizer accepts what the assembler builds");
            Assert.Equal(1, model.Query(LogFilter.Tenders).Count);
            Assert.Equal(0, model.TendersWon);
        }

        // ---- the sentences of the letter ----

        [Test]
        public void TheLineOfAResult_IsChosenByItsOutcomeAndReason()
        {
            Func<string, string, string> key = (outcome, reason) => TenderLines.KeyOf(new TenderResultDto { outcome = outcome, reasonKey = reason });

            Assert.Equal("ct.tender.line.won", key("Won", "ct.tender.won"));
            Assert.Equal("ct.tender.lost.outbid", key("Lost", "ct.tender.outbid"));
            Assert.Equal("ct.tender.lost.autoReject", key("Lost", "ct.tender.autoReject"));
            Assert.Equal("ct.tender.lost.shadowed", key("Lost", "ct.tender.shadowed"));
            Assert.Equal("ct.tender.lost.retired", key("Lost", "ct.tender.retired"));
            Assert.Equal("ct.tender.void.noOffers", key("Void", "ct.tender.noOffers"));
            Assert.Equal("ct.tender.void.retired", key("Void", "ct.tender.retired"));
            Assert.Equal("ct.tender.lost.outbid", key("Lost", "something unknown"), "a reason that is not understood is a plain defeat");
            Assert.Equal("ct.tender.lost.outbid", TenderLines.KeyOf(null));
        }

        [Test]
        public void TheArguments_FillTheEightSlots()
        {
            TenderSnapshot snapshot = Snapshot(IntelId, true,
                Offer(IntelId, "Intel", "i386", 60f),
                Offer(ZStrongId, "Игрок", "Z-386", 40f, player: true, required: 3, contracted: 4, capacity: 5));
            snapshot.DrawChances = Chances(IntelId, 60, ZStrongId, 40);
            TenderResultDto outbid = TenderAssembler.Build(snapshot).Result;

            string[] args = TenderLines.Args(outbid, ContractRules.Default, false);

            Assert.Equal(TenderLines.ArgCount, args.Length);
            Assert.Equal(8, TenderLines.ArgCount, "the string-table test formats every text with eight arguments");
            Assert.Equal("Apple — Computer, 52 000 шт./мес.", args[0]);
            Assert.Equal("Intel", args[1]);
            Assert.Equal("i386", args[2]);
            Assert.Equal("40", args[3]);
            Assert.Equal("21", args[4]);
            Assert.Equal("0", args[5], "the offer was not withdrawn: no lines to report");

            Without(snapshot, ZStrongId);
            TenderResultDto rejected = TenderAssembler.Build(snapshot).Result;
            args = TenderLines.Args(rejected, ContractRules.Default, false);
            Assert.Equal("3", args[5]);
            Assert.Equal("4", args[6]);
            Assert.Equal("5", args[7]);
        }

        [Test]
        public void TheArguments_UseTheDecimalsOfTheRules_AndTheSeparatorOfTheLanguage()
        {
            var tender = new TenderResultDto { outcome = "Lost", reasonKey = "ct.tender.outbid", playerActual = 0.2111, playerGameChance = 40 };
            ContractRules oneDecimal = ContractRules.FromJson(
                (PTMod.Core.Logic.Json.JsonObject)Parse("{ \"schema\": 1, \"display\": { \"actualPercentDecimals\": 1 } }"), null);

            Assert.Equal("21.1", TenderLines.Args(tender, oneDecimal, false)[4]);
            Assert.Equal("21,1", TenderLines.Args(tender, oneDecimal, true)[4]);
            Assert.Equal("21", TenderLines.Args(tender, null, false)[4], "no rules: the defaults");
        }

        [Test]
        public void TheArguments_TakeTheMarkupOutOfNamesFromTheGame()
        {
            var tender = new TenderResultDto { outcome = "Lost", contractLabel = "<b>Apple</b>", playerCpuName = "<size=900>Z", winnerCompany = "A>B", winnerCpuName = "<i>x" };

            string[] args = TenderLines.Args(tender, ContractRules.Default, false);

            for (int i = 0; i < 3; i++)
            {
                Assert.False(args[i].Contains("<") || args[i].Contains(">"), "argument " + i + " is clean: " + args[i]);
            }
            Assert.Equal(TenderLines.ArgCount, TenderLines.Args(null, null, false).Length);
        }

        private static PTMod.Core.Logic.Json.JsonNode Parse(string text)
        {
            PTMod.Core.Logic.Json.JsonNode node;
            PTMod.Core.Logic.Json.JsonError error;
            Assert.True(PTMod.Core.Logic.Json.ModJson.TryParse(text, out node, out error), "test JSON: " + error);
            return node;
        }
    }
}
