using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-012 (FR-2): the text of the breakdown of the chance. The cases are the scenarios of the spec: AC-2.1 (60 against 40: hint "IPS at least 60"), AC-2.3 (1000 against 1: the first offer with 100 %
    /// always wins), AC-2.4 (only the strongest own CPU counts), AC-2.5 (the multiplier 1.25 of another feature with its reason) and the case of a contract without offers (FR-2 item 7).
    /// </summary>
    [Covers(typeof(OddsLines), typeof(OddsPanelInput), typeof(ModSourceText))]
    public class OddsLinesTests
    {
        private static List<OfferOdds> Rows(params OfferInput[] offers)
        {
            return OddsMath.DrawOrder(OddsMath.BestPerCompany(offers), null);
        }

        private static OddsPanelInput InputFor(List<OfferOdds> rows, int candidateCpu)
        {
            OfferOdds own = rows.First(r => r.CpuId == candidateCpu);
            float rivals = rows.Where(r => r.CpuId != candidateCpu).Sum(r => r.Ips);
            float strongest = rows.Where(r => r.CpuId != candidateCpu).Select(r => r.Ips).DefaultIfEmpty(0f).Max();
            return new OddsPanelInput
            {
                Rows = rows,
                CandidateCpuId = candidateCpu,
                VanillaChance = own.VanillaChance,
                GameChance = own.GameChance,
                Actual = own.ActualProbability,
                IpsFor50 = rivals,
                IpsForFirstInQueue = strongest,
            };
        }

        private static List<string> Keys(List<PanelLine> lines)
        {
            return lines.Select(l => l.Key).ToList();
        }

        [Test]
        public void Ac21_SixtyAgainstForty_ShowsTheSummaryAndTheHints()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            List<PanelLine> lines = OddsLines.Build(InputFor(rows, 20));

            Assert.Equal(new[] { "ct.odds.summary", "ct.odds.hint50", "ct.odds.hintFirst" }, Keys(lines).ToArray());
            Assert.Equal(40.0, lines[0].Args[0].Number, "the chance of the game");
            Assert.Near(21.1, lines[0].Args[1].Number * 100, 0.05, "the actual probability");
            Assert.Equal(60.0, lines[1].Args[0].Number, "for 50 % the IPS must reach the sum of the others");
            Assert.Equal(60.0, lines[2].Args[0].Number);
            Assert.True(lines[0].Bold);
        }

        [Test]
        public void Ac23_AnOfferWithAHundredPercentInFront_ShutsTheCandidateOut()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 1000f, 0), new OfferInput("player", 20, 1f, 1));
            List<PanelLine> lines = OddsLines.Build(InputFor(rows, 20));

            Assert.True(OddsLines.IsShutOut(rows, 20));
            Assert.True(Keys(lines).Contains("ct.odds.blocked"));
            Assert.Equal(LineTone.Bad, lines[0].Tone, "an actual probability under the limit is red");
            Assert.Equal(1.0, lines[0].Args[0].Number, "the game's minimum chance is 1 %");
            Assert.Near(0.0, lines[0].Args[1].Number, 1e-9);
        }

        [Test]
        public void TheCandidateAhead_IsNotShutOut_AndIsToldItIsFirst()
        {
            List<OfferOdds> rows = Rows(new OfferInput("player", 20, 60f, 0), new OfferInput("ai:1", 10, 40f, 1));

            Assert.False(OddsLines.IsShutOut(rows, 20));
            Assert.Equal(0, OddsLines.CandidatePosition(rows, 20));
            List<PanelLine> lines = OddsLines.Build(InputFor(rows, 20));
            Assert.Equal(new[] { "ct.odds.summary", "ct.odds.hint50", "ct.odds.first" }, Keys(lines).ToArray());
        }

        [Test]
        public void ACandidateThatIsNotInTheRows_HasNoPosition()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0));

            Assert.Equal(-1, OddsLines.CandidatePosition(rows, 99));
            Assert.Equal(-1, OddsLines.CandidatePosition(null, 99));
            Assert.False(OddsLines.IsShutOut(rows, 99));
            Assert.False(OddsLines.IsShutOut(null, 1));
        }

        [Test]
        public void Ac25_AMultiplierOfAnotherFeature_IsShownWithItsSourceAndReason()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);
            input.ChanceDiffersFromVanilla = true;
            input.ModMultiplier = 1.25f;
            input.Sources = new List<ModSourceText>
            {
                new ModSourceText { Feature = "platform-generations", Reason = "OEM prefers the platform Z80", Multiplier = 1.25f },
                new ModSourceText { Feature = "press-reviews", Reason = null, Multiplier = 1.0f },
            };

            List<PanelLine> lines = OddsLines.Build(input);

            Assert.Equal(new[] { "ct.odds.summary", "ct.odds.mods", "ct.odds.source", "ct.odds.source.noReason", "ct.odds.hint50", "ct.odds.hintFirst" }, Keys(lines).ToArray());
            Assert.Near(1.25, lines[1].Args[0].Number, 1e-6);
            Assert.Equal(1, lines[2].Indent);
            Assert.Equal("platform-generations", lines[2].Args[0].Text);
            Assert.Equal("OEM prefers the platform Z80", lines[2].Args[1].Text);
            Assert.Near(1.25, lines[2].Args[2].Number, 1e-6);
            Assert.Equal("press-reviews", lines[3].Args[0].Text, "a provider without a reason still shows its feature and factor (E13)");
            Assert.Equal(2, lines[3].Args.Length);
        }

        [Test]
        public void TheCappedProduct_GetsTheCappedText()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);
            input.ChanceDiffersFromVanilla = true;
            input.ModsCapped = true;
            input.ModMultiplier = 5f;

            List<PanelLine> lines = OddsLines.Build(input);

            Assert.True(Keys(lines).Contains("ct.odds.mods.capped"));
            Assert.False(Keys(lines).Contains("ct.odds.mods"));
            PanelLine capped = lines.First(l => l.Key == "ct.odds.mods.capped");
            Assert.Equal(ArgKind.Key, capped.Args[1].Kind);
            Assert.Equal("core.explain.capped", capped.Args[1].Text, "the text of the core, as in every breakdown");
        }

        [Test]
        public void WithoutADifference_TheModsAreNotMentioned_EvenWithSources()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);
            input.Sources = new List<ModSourceText> { new ModSourceText { Feature = "x", Reason = "y", Multiplier = 1f } };

            Assert.False(Keys(OddsLines.Build(input)).Any(k => k.StartsWith("ct.odds.mods") || k.StartsWith("ct.odds.source")));
        }

        [Test]
        public void ANoOffersContract_SaysTheAiStillBids_AndHasNoHints()
        {
            List<OfferOdds> rows = Rows(new OfferInput("player", 20, 40f, 0));
            OddsPanelInput input = InputFor(rows, 20);
            input.NoOffersYet = true;
            input.ReviewDateText = "January 1987";

            List<PanelLine> lines = OddsLines.Build(input);

            Assert.Equal(new[] { "ct.odds.summary", "ct.odds.noOffers" }, Keys(lines).ToArray());
            Assert.Equal(100.0, lines[0].Args[0].Number);
            Assert.Equal("January 1987", lines[1].Args[0].Text);
            Assert.Equal(LineTone.Muted, lines[1].Tone);
        }

        [Test]
        public void Ac24_OnlyTheStrongestOwnCpuCounts_IsSaidWithItsName()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);
            input.ShadowedCount = 1;
            input.StrongestOwnName = "Z80-A";

            List<PanelLine> lines = OddsLines.Build(input);

            PanelLine shadowed = lines.First(l => l.Key == "ct.odds.shadowed");
            Assert.Equal("Z80-A", shadowed.Args[0].Text);
            Assert.Equal(LineTone.Warn, shadowed.Tone);
        }

        [Test]
        public void TheNotesOfTheGame_AreAddedWhenTheyApply()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);
            input.GameChancesAvailable = false;
            input.Mismatch = true;

            List<string> keys = Keys(OddsLines.Build(input));

            Assert.True(keys.Contains("ct.odds.noGameData"));
            Assert.Equal("ct.odds.mismatch", keys[keys.Count - 1]);
        }

        [Test]
        public void ANullInput_GivesNoLines()
        {
            Assert.Equal(0, OddsLines.Build(null).Count);
        }

        [Test]
        public void TheLowProbabilityLimit_ComesFromTheInput()
        {
            List<OfferOdds> rows = Rows(new OfferInput("ai:1", 10, 60f, 0), new OfferInput("player", 20, 40f, 1));
            OddsPanelInput input = InputFor(rows, 20);   // actual probability 21 %

            input.LowActualChance = 0.10;
            Assert.Equal(LineTone.Normal, OddsLines.Build(input)[0].Tone);
            input.LowActualChance = 0.30;
            Assert.Equal(LineTone.Bad, OddsLines.Build(input)[0].Tone);
        }
    }
}
