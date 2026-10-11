using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// What the intelligence window shows (TSK-018, TSK-029; FR-9, FR-16, FR-17; design.md Component 11), checked on the shipped content and the shipped
    /// strings: the list (character or "Character unknown", attitude, segments, share, last CPU), the details (threats with the range of weeks, effects with their
    /// end, ten lines of history, the part of the attitude in a negotiation and the discount of the foundry partner, the line about the caution), the fog that
    /// touches the share and the <c>Ips</c> only, and that no money and no project of a rival is anywhere in it.
    /// </summary>
    [Covers(typeof(IntelBuilder), typeof(IntelRow), typeof(IntelLine), typeof(IntelSection), typeof(IntelDetails), typeof(IntelCompanyFacts),
            typeof(IntelSettings), typeof(IntelTone), typeof(IIntelText), typeof(ThreatMath))]
    public class IntelBuilderTests
    {
        private static readonly DateTime Day = new DateTime(1990, 5, 1);

        // ---- the list ----

        [Test]
        public void AC_9_1_WithTheModeOn_EveryCharacterIsUnknown_AtTheStart_ButTheFoundryPartner()
        {
            var rig = new IntelRig();
            IntelSettings settings = rig.Settings(true, true);

            IntelRow amd = rig.Builder.BuildRow(IntelRig.Amd(), settings);
            Assert.False(amd.CharacterVisible);
            Assert.Equal("Характер неизвестен", amd.CharacterText);
            Assert.Equal(string.Empty, amd.ArchetypeId, "the id does not leave the builder while the character is hidden");
            Assert.Equal(string.Empty, amd.Description);
            Assert.Equal("-15", amd.AttitudeText, "the attitude is the baseline of the archetype");
            Assert.Equal(AttitudeLevel.Neutral, amd.Level, "-15 is above the border of -20");
            Assert.Equal("Нейтралитет", amd.LevelText);
            Assert.Equal(IntelTone.Normal, amd.AttitudeTone);

            IntelRow tsmc = rig.Builder.BuildRow(IntelRig.Facts(AttitudeRig.Tsmc, "TSMC"), settings);
            Assert.True(tsmc.CharacterVisible, "AC-11.2: +20 is enough");
            Assert.Equal("Литейный партнёр", tsmc.CharacterText);
            Assert.Equal("foundry_partner", tsmc.ArchetypeId);
            Assert.Equal("+20", tsmc.AttitudeText);
            Assert.Equal(AttitudeLevel.Respect, tsmc.Level);
            Assert.Equal(IntelTone.Good, tsmc.AttitudeTone);

            Assert.Equal(0, rig.Text.Missing.Count, "no string is missing: " + string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void AC_9_3_AfterTheFirstReaction_TheCharacterIsShown_WithItsDescription()
        {
            var rig = new IntelRig();
            IntelSettings settings = rig.Settings(true, true);
            Assert.False(rig.Builder.BuildRow(IntelRig.Amd(), settings).CharacterVisible);

            Assert.True(rig.Base.Book.Reveal(AttitudeRig.Amd));
            IntelRow row = rig.Builder.BuildRow(IntelRig.Amd(), settings);
            Assert.True(row.CharacterVisible);
            Assert.Equal("Ценовой боец", row.CharacterText);
            Assert.Equal("price_warrior", row.ArchetypeId);
            Assert.Equal("Дешёвые запуски. На угрозы отвечает снижением цен.", row.Description);

            IntelDetails details = rig.Builder.BuildDetails(IntelRig.Amd(), settings);
            List<string> header = IntelRig.TextsOf(details.Find("header"));
            Assert.True(header.Contains("Ценовой боец") && header.Contains("Дешёвые запуски. На угрозы отвечает снижением цен."));
        }

        [Test]
        public void WithTheModeOff_EveryCharacterIsShown_AndTheModeBackOn_DoesNotHideTheRevealedOnes()
        {
            var rig = new IntelRig();

            Assert.True(rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(false, true)).CharacterVisible);
            Assert.Equal("Ценовой боец", rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(false, true)).CharacterText);
            Assert.False(rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(true, true)).CharacterVisible, "back on: hidden again");

            rig.Base.Book.Reveal(AttitudeRig.Amd);
            Assert.True(rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(true, true)).CharacterVisible, "a revealed one stays shown");
        }

        [Test]
        public void AnAttitudeOfTwenty_ShowsTheCharacter_WithoutAReaction()
        {
            var rig = new IntelRig();
            rig.Base.SetAttitude(AttitudeRig.Amd, 19.9f);
            Assert.False(rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(true, true)).CharacterVisible);
            rig.Base.SetAttitude(AttitudeRig.Amd, 20f);
            Assert.True(rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(true, true)).CharacterVisible);
        }

        [Test]
        public void TheRow_HasTheSegmentsTheShareAndTheLastCpu_AndTheFogRoundsTwoOfThem()
        {
            var rig = new IntelRig();

            IntelRow fog = rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(false, true));
            Assert.Equal("Mid Range, Low End", fog.SegmentsText);
            Assert.Equal("≈ 15 %", fog.ShareText, "AC-16.1");
            Assert.Equal("Am386, 1991 г., IPS ≈ 120", fog.LastCpuText, "AC-16.2");
            Assert.Equal("Ценовой боец · Mid Range, Low End · доля ≈ 15 % · Am386, 1991 г., IPS ≈ 120", rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(false, true)).SummaryText);

            IntelRow exact = rig.Builder.BuildRow(IntelRig.Amd(), rig.Settings(false, false));
            Assert.Equal("17 %", exact.ShareText);
            Assert.Equal("Am386, 1991 г., IPS 123", exact.LastCpuText);
            Assert.Equal(0, rig.Text.Missing.Count, string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void ACompanyWithoutSalesAndCpu_ShowsDashes()
        {
            var rig = new IntelRig();
            IntelRow row = rig.Builder.BuildRow(IntelRig.Facts(AttitudeRig.Tsmc, "TSMC"), rig.Settings(false, true));
            Assert.Equal("—", row.SegmentsText);
            Assert.Equal("—", row.ShareText);
            Assert.Equal("—", row.LastCpuText);
        }

        [Test]
        public void ABankruptCompany_IsGrey_AndSaysSo_AndTheListPutsItLast()
        {
            var rig = new IntelRig();
            IntelRow gone = rig.Builder.BuildRow(IntelRig.Facts(AttitudeRig.Ibm, "IBM", true), rig.Settings(false, true));
            IntelRow alive = rig.Builder.BuildRow(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true));
            IntelRow other = rig.Builder.BuildRow(IntelRig.Facts(AttitudeRig.Amd, "AMD"), rig.Settings(false, true));

            Assert.True(gone.IsBankrupt);
            Assert.Equal(IntelTone.Muted, gone.Tone);
            Assert.Equal("Банкрот", gone.SummaryText);

            var rows = new List<IntelRow> { gone, alive, other };
            rows.Sort(IntelBuilder.Compare);
            Assert.Equal("AMD", rows[0].Name, "by name");
            Assert.Equal("Intel", rows[1].Name);
            Assert.Equal("IBM", rows[2].Name, "the bankrupt below");
            Assert.Equal(0, IntelBuilder.Compare(alive, alive));
            Assert.True(IntelBuilder.Compare(null, alive) > 0 && IntelBuilder.Compare(alive, null) < 0);

            IntelDetails details = rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Ibm, "IBM", true), rig.Settings(false, true));
            Assert.True(IntelRig.TextsOf(details.Find("header")).Contains("Банкрот"));
        }

        [Test]
        public void ACompanyWithoutACharacter_IsUnknown_AtZero_AndHasNoNegotiationLines()
        {
            var rig = new IntelRig();
            IntelCompanyFacts newcomer = IntelRig.Facts(40, "Newcomer");

            IntelRow row = rig.Builder.BuildRow(newcomer, rig.Settings(false, true));
            Assert.False(row.CharacterVisible, "nothing to show");
            Assert.Equal("0", row.AttitudeText);
            Assert.Equal(AttitudeLevel.Neutral, row.Level);

            IntelSection attitude = rig.Builder.BuildDetails(newcomer, rig.Settings(false, true)).Find("attitude");
            Assert.Equal(1, attitude.Lines.Count, "only the number");
        }

        [Test]
        public void ADormantRival_IsMarked_AndHasNoNegotiationBonus()
        {
            var rig = new IntelRig();
            rig.State.dormant.Add(AttitudeRig.Amd);

            IntelDetails details = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true));
            Assert.True(details.Row.IsDormant);
            Assert.True(IntelRig.TextsOf(details.Find("header")).Contains("Вне игры"));
            Assert.Equal(1, details.Find("attitude").Lines.Count);
        }

        // ---- the attitude and the negotiations ----

        [Test]
        public void TheAttitude_ShowsTheValueTheLevel_AndWhatItDoesInANegotiation()
        {
            var rig = new IntelRig();
            rig.Base.SetAttitude(AttitudeRig.Intel, 12f);

            IntelSection section = rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("attitude");
            Assert.Equal("Отношение", section.Title);
            Assert.Equal("К вам: +12  ·  Нейтралитет", IntelRig.TextsOf(section)[0]);
            Assert.Equal("Отношение даёт +2 к интересу в переговорах", IntelRig.TextsOf(section)[1]);
            Assert.Equal(2, section.Lines.Count, "Intel is no foundry partner: no discount line");

            string tip = section.Lines[0].Tooltip;
            Assert.NotNull(tip, "FR-8 item 4: a tip on the number explains it");
            Assert.True(tip.Contains("Отношение соперника к вам") && tip.Contains("Отношение даёт +2 к интересу в переговорах"), tip);
        }

        [Test]
        public void TheFoundryPartner_ShowsItsDiscount_OnlyWhenTheCharacterIsShown()
        {
            var rig = new IntelRig();
            rig.Base.SetAttitude(AttitudeRig.Tsmc, 40f);
            IntelCompanyFacts tsmc = IntelRig.Facts(AttitudeRig.Tsmc, "TSMC");

            IntelSection shown = rig.Builder.BuildDetails(tsmc, rig.Settings(true, true)).Find("attitude");
            List<string> lines = IntelRig.TextsOf(shown);
            Assert.True(lines.Contains("Отношение даёт +8 к интересу в переговорах"), "AC-8.1: the interest");
            Assert.True(lines.Contains("Литейный партнёр даёт скидку на наценку: до 4 п. п."), "AC-8.1: the discount");
            Assert.True(shown.Lines[0].Tooltip.Contains("до 4 п. п."), "the tip tells it too");

            // hidden mode, not revealed, below +20: the discount would give the archetype away
            rig.Base.SetAttitude(AttitudeRig.Tsmc, 10f);
            IntelSection hidden = rig.Builder.BuildDetails(tsmc, rig.Settings(true, true)).Find("attitude");
            Assert.False(hidden.Lines.Any(l => l.Text.Contains("Литейный партнёр")), "no hint of the discount while the character is hidden");
            Assert.True(IntelRig.TextsOf(hidden).Contains("Отношение даёт +2 к интересу в переговорах"));
        }

        [Test]
        public void AC_8_3_AHostileFoundryPartner_ShowsNoDiscount_AndALowerInterest()
        {
            var rig = new IntelRig();
            rig.Base.SetAttitude(AttitudeRig.Tsmc, -30f);

            IntelSection section = rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Tsmc, "TSMC"), rig.Settings(false, true)).Find("attitude");
            List<string> lines = IntelRig.TextsOf(section);
            Assert.True(lines.Contains("Отношение даёт -6 к интересу в переговорах"), string.Join(" | ", lines));
            Assert.True(lines.Any(l => l.StartsWith("Литейный партнёр пока не даёт скидки")), string.Join(" | ", lines));
            Assert.Equal(IntelTone.Bad, section.Lines[1].Tone);
        }

        // ---- the threats ----

        [Test]
        public void AC_9_2_APlannedPriceCut_Shows_TwoToSevenWeeks_AfterTheMailWasDelivered()
        {
            var rig = new IntelRig();
            rig.Pend(AttitudeRig.Amd, ReactionType.PriceCut, Segment.MidRange, Day, 30);

            IntelSection threats = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("threats");
            Assert.Equal("Угрозы", threats.Title);
            Assert.Equal(1, threats.Lines.Count);
            Assert.Equal("Снижение цен, Mid Range, ожидается через 2–7 нед.", threats.Lines[0].Text);
            Assert.Equal(IntelTone.Warn, threats.Lines[0].Tone);
            Assert.False(threats.Lines[0].Text.Contains("31.05") || threats.Lines[0].Text.Contains("1990"), "the exact day is not given");
        }

        [Test]
        public void AReactionWhoseMailHasNotComeYet_IsNotAThreatTheOfPlayerKnows()
        {
            var rig = new IntelRig();
            PendingReaction pending = rig.Pend(AttitudeRig.Amd, ReactionType.PriceCut, Segment.MidRange, Day, 30);
            pending.fireOn = string.Empty;   // planned, the mail is not delivered
            rig.Reactions.AddHistory(AttitudeRig.Amd, Day, pending.type, pending.segment, HistoryOutcomes.Planned);

            IntelDetails details = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true));
            Assert.Equal("Нет", details.Find("threats").Lines[0].Text);
            Assert.Equal("Нет", details.Find("history").Lines[0].Text, "the line 'planned' waits for the mail too");

            pending.fireOn = RivalDates.ToText(Day.AddDays(30));   // the mail came
            IntelDetails later = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true));
            Assert.Equal(1, later.Find("threats").Lines.Count);
            Assert.True(later.Find("history").Lines[0].Text.Contains("Снижение цен, Mid Range: запланирована"), later.Find("history").Lines[0].Text);
        }

        [Test]
        public void TheRangeOfWeeks_NarrowsWithTheDays_AndNeverRevealsTheDay()
        {
            var rig = new IntelRig();
            rig.Pend(AttitudeRig.Amd, ReactionType.CounterLaunch, Segment.HighEnd, Day, 30);

            rig.Today = Day.AddDays(20);
            Assert.Equal("Ответная разработка, High End, ожидается в течение 4 нед.",
                rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("threats").Lines[0].Text, "20 days later: 14..45 minus 20 days");

            rig.Today = Day.AddDays(30);
            Assert.Equal("Ответная разработка, High End, ожидается в ближайшие дни",
                rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("threats").Lines[0].Text, "the day itself");

            rig.Today = Day.AddDays(3);
            Assert.True(rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("threats").Lines[0].Text.Contains("через 1–6 нед."),
                "three days in: the range of the mail (14..45 days) less the three days");
        }

        [Test]
        public void TheThreats_OfOtherRivals_AreNotShown_AndTheyComeInTheOrderOfTheDays()
        {
            var rig = new IntelRig();
            rig.Pend(AttitudeRig.Intel, ReactionType.Smear, Segment.Any, Day, 20);
            rig.Pend(AttitudeRig.Amd, ReactionType.OemPush, Segment.Any, Day, 40);
            rig.Pend(AttitudeRig.Amd, ReactionType.PriceCut, Segment.LowEnd, Day, 16);

            IntelSection amd = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("threats");
            Assert.Equal(2, amd.Lines.Count);
            Assert.True(amd.Lines[0].Text.StartsWith("Снижение цен, Low End"), amd.Lines[0].Text);
            Assert.True(amd.Lines[1].Text.StartsWith("OEM-натиск, ожидается"), "a reaction without a segment has no segment in the line: " + amd.Lines[1].Text);
        }

        [Test]
        public void ThreatMath_KeepsTheTrueRemainingTimeInsideTheRange()
        {
            for (int warning = 1; warning <= 60; warning++)
            {
                for (int elapsed = 0; elapsed <= warning + 3; elapsed++)
                {
                    DateTime delivered = Day;
                    DateTime fireOn = delivered.AddDays(warning);
                    DateTime today = delivered.AddDays(elapsed);
                    int remaining = Math.Max(0, warning - elapsed);

                    int from, to;
                    ThreatMath.WeeksLeft(today, fireOn, warning, 14, 45, out from, out to);
                    Assert.True(from >= 0 && to >= 1 && from <= to, "ordered " + from + ".." + to);
                    Assert.True(from * 7 <= remaining, "from " + from + " weeks is not above the true " + remaining + " days (warning " + warning + ", elapsed " + elapsed + ")");
                    Assert.True(to * 7 >= remaining, "to " + to + " weeks is not below the true " + remaining + " days (warning " + warning + ", elapsed " + elapsed + ")");
                }
            }
        }

        [Test]
        public void ThreatMath_RightAfterTheMail_IsTheRangeOfTheMail()
        {
            int from, to;
            ThreatMath.WeeksLeft(Day, Day.AddDays(14), 14, 14, 45, out from, out to);
            Assert.Equal(2, from);
            Assert.Equal(7, to);
            ThreatMath.WeeksLeft(Day, Day.AddDays(45), 45, 14, 45, out from, out to);
            Assert.Equal(2, from);
            Assert.Equal(7, to);

            ThreatMath.WeeksLeft(Day, Day.AddDays(30), 30, 14, 45, out from, out to);
            Assert.Equal("2–7", ThreatMath.RangeText(from, to));
            Assert.Equal("3", ThreatMath.RangeText(3, 3));

            ThreatMath.WeeksLeft(Day, Day, 30, 14, 45, out from, out to);
            Assert.True(from == 0 && to == 1, "due today: within days");
            ThreatMath.WeeksLeft(Day.AddDays(5), Day, 30, 14, 45, out from, out to);
            Assert.True(from == 0 && to == 1, "overdue: within days");
        }

        // ---- the effects, the caution, the history ----

        [Test]
        public void TheEffectsInForce_ShowTheirEnd()
        {
            var rig = new IntelRig();
            rig.Reactions.BeginActive("r-3", AttitudeRig.Amd, ReactionType.PriceCut, (int)Segment.MidRange, Day, new DateTime(1990, 10, 28));

            IntelSection active = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("active");
            Assert.Equal("Действует", active.Title);
            Assert.Equal(1, active.Lines.Count);
            Assert.Equal("Снижение цен, Mid Range (цены −15 %) — до 28.10.1990", active.Lines[0].Text, "NFR-5: the end and the size; AMD cuts by 15 % (price_warrior)");

            IntelSection nothing = rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("active");
            Assert.Equal("Нет", nothing.Lines[0].Text, "the effect of another rival is not shown");
        }

        [Test]
        public void TheSizeOfAnEffect_IsTheOneTheHandlersTake_FromTheArchetypeOrTheConfig()
        {
            var rig = new IntelRig();
            rig.Reactions.BeginActive("r-1", AttitudeRig.Intel, ReactionType.PriceCut, 0, Day, Day.AddDays(180));
            rig.Reactions.BeginActive("r-2", AttitudeRig.Intel, ReactionType.CounterLaunch, 0, Day, Day.AddDays(150));
            rig.Reactions.BeginActive("r-3", AttitudeRig.Intel, ReactionType.OemPush, -1, Day, Day.AddDays(365));
            rig.Reactions.BeginActive("r-4", AttitudeRig.Intel, ReactionType.Smear, -1, Day, Day.AddDays(1));

            List<string> lines = IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("active"));
            Assert.Equal(4, lines.Count);
            // by the end of the effect: the smear, the counter-launch, the price cut, the OEM push
            Assert.Equal("Кампания против репутации — до 02.05.1990", lines[0], "a reaction without a size has only the end");
            Assert.Equal("Ответная разработка, High End (рейтинг дизайна ×1.35, наука ×1.25) — до 28.09.1990", lines[1]);
            Assert.Equal("Снижение цен, High End (цены −8 %) — до 28.10.1990", lines[2], "the hegemon uses the 8 % of the config");
            Assert.Equal("OEM-натиск (шансы в тендерах ×1.5) — до 01.05.1991", lines[3]);

            var english = new IntelRig("en");
            english.Reactions.BeginActive("r-1", AttitudeRig.Amd, ReactionType.PriceCut, 1, Day, Day.AddDays(180));
            Assert.Equal("Price cut, Mid Range (prices -15 %) — until 28.10.1990",
                IntelRig.TextsOf(english.Builder.BuildDetails(IntelRig.Amd(), english.Settings(false, true)).Find("active"))[0]);
            Assert.Equal(0, rig.Text.Missing.Count + english.Text.Missing.Count, string.Join(", ", rig.Text.Missing.Concat(english.Text.Missing)));
        }

        [Test]
        public void TheOemPushFactor_OfThePanel_IsTheOneOfTheEventContent()
        {
            PTMod.Core.Logic.Json.JsonObject root = RivalTestKit.ParseObject(RivalTestKit.ReadContent("events.json"));
            double found = 0;
            foreach (PTMod.Core.Logic.Json.JsonNode record in root.GetArray("records"))
            {
                var json = (PTMod.Core.Logic.Json.JsonObject)record;
                if (json.GetString("id", null) != ReactionTags.EventOemPush) continue;
                var effect = (PTMod.Core.Logic.Json.JsonObject)json.GetArray("effects")[0];
                found = effect.GetObject("params").GetDouble("multiplier", 0);
            }
            Assert.Near(ReactionParams.OemPushSignChance, found, 1e-9, "the panel shows the factor the event gives");
        }

        [Test]
        public void TheCautionLine_IsShown_OnlyForARecordInForce()
        {
            var rig = new IntelRig();
            IntelCompanyFacts intel = IntelRig.Facts(AttitudeRig.Intel, "Intel");

            Assert.Equal("Нет", rig.Builder.BuildDetails(intel, rig.Settings(false, true)).Find("active").Lines[0].Text, "no record, no line");

            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Intel, flawId = 7, until = "1992-05-01" });
            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Intel, flawId = 9, until = "1991-12-01" });
            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Amd, flawId = 8, until = "1999-01-01" });
            IntelSection active = rig.Builder.BuildDetails(intel, rig.Settings(false, true)).Find("active");
            Assert.Equal(1, active.Lines.Count);
            Assert.Equal("Осторожен после брака до 01.05.1992", active.Lines[0].Text, "the later end of the two, and not the record of another rival");
            Assert.Equal(IntelTone.Warn, active.Lines[0].Tone);

            rig.Today = new DateTime(1992, 5, 1);
            Assert.Equal("Нет", rig.Builder.BuildDetails(intel, rig.Settings(false, true)).Find("active").Lines[0].Text, "it lasts while today < until");
        }

        [Test]
        public void TheCautionLine_IsNotShown_ForABankruptOrADormantCompany_OrAnUnreadableRecord()
        {
            var rig = new IntelRig();
            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Intel, flawId = 7, until = "1992-05-01" });
            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Amd, flawId = 8, until = "next year" });
            rig.State.cautions.Add(null);

            Assert.True(IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel", true), rig.Settings(false, true)).Find("active")).Contains("Нет"), "bankrupt");
            Assert.Equal("Нет", rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)).Find("active").Lines[0].Text, "a date that cannot be read");

            rig.State.dormant.Add(AttitudeRig.Intel);
            Assert.Equal("Нет", rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("active").Lines[0].Text, "dormant (FR-17 item 4)");
        }

        [Test]
        public void TheHistory_HasTheLastTenLines_NewestFirst_WithTheChangeOfTheAttitude()
        {
            var rig = new IntelRig();
            for (int i = 0; i < 8; i++)
            {
                rig.Today = Day.AddDays(i * 10);
                rig.Base.Ledger.Change(AttitudeRig.Intel, -8f, "StrongLaunch:" + (i % 4));
            }
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day.AddDays(75), "PriceCut", 1, HistoryOutcomes.Started);
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day.AddDays(90), "PriceCut", 1, HistoryOutcomes.Ended);
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day.AddDays(95), string.Empty, -1, HistoryOutcomes.Revealed);
            rig.Today = Day.AddDays(100);

            IntelSection history = rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("history");
            Assert.Equal("История", history.Title);
            Assert.Equal(IntelBuilder.HistoryLines, history.Lines.Count, "ten of the eleven");

            Assert.True(history.Lines[0].Text.Contains("04.08.1990") && history.Lines[0].Text.Contains("Характер раскрыт"), history.Lines[0].Text);
            Assert.True(history.Lines[1].Text.Contains("Снижение цен, Mid Range: закончилась"), history.Lines[1].Text);
            Assert.True(history.Lines[2].Text.Contains("Снижение цен, Mid Range: началась"), history.Lines[2].Text);
            Assert.True(history.Lines[3].Text.Contains("ваш сильный запуск (Industries)") && history.Lines[3].Text.EndsWith("(-9.6)"),
                "the change is the 8 points times the sensitivity of 1.2: " + history.Lines[3].Text);
            Assert.Equal(IntelTone.Bad, history.Lines[3].Tone);
            Assert.False(history.Lines.Any(l => l.Text.StartsWith("01.05.1990")), "the oldest line went out");
            Assert.Equal(0, rig.Text.Missing.Count, string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void TheHistory_TellsTheReasonsOfTheAttitudeInWords()
        {
            var rig = new IntelRig();
            string[] reasons = { "ShareGain:0", "PriceUndercut:2", "OemLoss", "TechLead", "BusinessSigned", "BusinessBrokenByPlayer", "BusinessActiveMonthly", "event:rival.poach", "acquisition" };
            foreach (string reason in reasons) rig.Base.Ledger.Change(AttitudeRig.Ibm, 3f, reason);

            List<string> texts = IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Ibm, "IBM"), rig.Settings(false, true)).Find("history"));
            Assert.Equal(9, texts.Count);
            string all = string.Join("\n", texts);
            foreach (string part in new[]
            {
                "рост вашей доли рынка (High End)", "ваши низкие цены (Low End)", "ваша победа в OEM-тендере", "ваше технологическое лидерство", "подписан бизнес-контракт",
                "вы расторгли бизнес-контракт", "действует бизнес-контракт", "событие", "acquisition",
            })
            {
                Assert.True(all.Contains(part), "'" + part + "' is not in:\n" + all);
            }
            Assert.False(all.Contains("[rival."), "no string is missing");
            Assert.Equal(0, rig.Text.Missing.Count, string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void TheHistory_MarksWhatTheTimerOrTheAiDecided()
        {
            var rig = new IntelRig();
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day, "Poach", -1, HistoryOutcomes.LetGo + HistoryOutcomes.AutoSuffix);
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day, "Poach", -1, HistoryOutcomes.CounterOffer);
            rig.Reactions.AddHistory(AttitudeRig.Intel, Day, "PriceCut", 2, "something new");

            List<string> texts = IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Intel, "Intel"), rig.Settings(false, true)).Find("history"));
            Assert.True(texts[2].Contains("Переманивание инженеров: инженеры отпущены (автоматически)"), texts[2]);
            Assert.True(texts[1].Contains("Переманивание инженеров: вы сделали контрпредложение"), texts[1]);
            Assert.True(texts[0].EndsWith("something new"), "an outcome the panel does not know is shown as it is: " + texts[0]);
        }

        // ---- the fog touches the share and the Ips only ----

        [Test]
        public void TheFog_ChangesOnlyTheShareAndTheIps_NotTheAttitudeTheThreatsTheEffectsOrTheHistory()
        {
            var rig = new IntelRig();
            rig.Pend(AttitudeRig.Amd, ReactionType.PriceCut, Segment.MidRange, Day, 30);
            rig.Reactions.BeginActive("r-9", AttitudeRig.Amd, ReactionType.CounterLaunch, (int)Segment.HighEnd, Day, Day.AddDays(150));
            rig.Reactions.AddHistory(AttitudeRig.Amd, Day, "CounterLaunch", 0, HistoryOutcomes.Started);
            rig.Base.Ledger.Change(AttitudeRig.Amd, -8f, "StrongLaunch:0");
            rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Amd, flawId = 1, until = "1992-01-01" });

            IntelDetails foggy = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(true, true));
            IntelDetails exact = rig.Builder.BuildDetails(IntelRig.Amd(), rig.Settings(true, false));

            Assert.Equal(foggy.Row.AttitudeText, exact.Row.AttitudeText);
            Assert.Equal(foggy.Row.Attitude, exact.Row.Attitude);
            Assert.Equal(foggy.Row.LevelText, exact.Row.LevelText);
            foreach (string id in new[] { "header", "attitude", "threats", "active", "history" })
            {
                Assert.True(IntelRig.TextsOf(foggy.Find(id)).SequenceEqual(IntelRig.TextsOf(exact.Find(id))), "the section '" + id + "' does not depend on the fog");
            }

            List<string> foggyMarket = IntelRig.TextsOf(foggy.Find("market"));
            List<string> exactMarket = IntelRig.TextsOf(exact.Find("market"));
            Assert.False(foggyMarket.SequenceEqual(exactMarket));
            Assert.Equal("Сегменты: Mid Range, Low End", foggyMarket[0]);
            Assert.Equal(foggyMarket[0], exactMarket[0], "the segments are exact");
            Assert.Equal("Доля рынка: ≈ 15 %", foggyMarket[1]);
            Assert.Equal("Доля рынка: 17 %", exactMarket[1]);
            Assert.Equal("Последний CPU: Am386, 1991 г., IPS ≈ 120", foggyMarket[2]);
            Assert.Equal("Последний CPU: Am386, 1991 г., IPS 123", exactMarket[2]);
        }

        [Test]
        public void TheMarketSection_ShowsTheReputation_OnlyWhenTheFactsHaveIt()
        {
            var rig = new IntelRig();
            IntelCompanyFacts amd = IntelRig.Amd();
            Assert.Equal(3, rig.Builder.BuildDetails(amd, rig.Settings(false, true)).Find("market").Lines.Count);

            amd.HasReputation = true;
            amd.Reputation = 67.6f;
            List<string> texts = IntelRig.TextsOf(rig.Builder.BuildDetails(amd, rig.Settings(false, true)).Find("market"));
            Assert.Equal(4, texts.Count);
            Assert.Equal("Репутация бренда: 68", texts[3]);
        }

        // ---- no money, no projects ----

        [Test]
        public void ThePanel_CannotShowTheMoneyOrTheProjectsOfARival()
        {
            string[] forbidden = { "money", "cash", "balance", "debt", "income", "profit", "revenue", "project", "funding", "budget", "loan" };
            foreach (Type type in new[] { typeof(IntelCompanyFacts), typeof(IntelRow), typeof(IntelLine), typeof(IntelSection), typeof(IntelDetails) })
            {
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (member is ConstructorInfo || member.Name.Contains("<")) continue;
                    string name = member.Name.ToLowerInvariant();
                    foreach (string word in forbidden) Assert.False(name.Contains(word), type.Name + "." + member.Name + " names " + word);
                }
            }
        }

        [Test]
        public void EveryTextOfThePanel_IsInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var rig = new IntelRig(language);
                rig.Pend(AttitudeRig.Amd, ReactionType.PriceCut, Segment.MidRange, Day, 30);
                rig.Pend(AttitudeRig.Amd, ReactionType.Poach, Segment.Any, Day, 20);
                rig.Reactions.BeginActive("r-1", AttitudeRig.Amd, ReactionType.CounterLaunch, 0, Day, Day.AddDays(150));
                foreach (string outcome in new[]
                {
                    HistoryOutcomes.Planned, HistoryOutcomes.Started, HistoryOutcomes.Ended, HistoryOutcomes.Cancelled, HistoryOutcomes.CounterLaunchDone,
                    HistoryOutcomes.Revealed, HistoryOutcomes.CounterOffer, HistoryOutcomes.LetGo + HistoryOutcomes.AutoSuffix,
                })
                {
                    rig.Reactions.AddHistory(AttitudeRig.Amd, Day, "Poach", -1, outcome);
                }
                foreach (string reason in new[] { "StrongLaunch:0", "ShareGain:1", "PriceUndercut:2", "OemLoss", "TechLead", "BusinessSigned", "BusinessBrokenByPlayer", "BusinessActiveMonthly", "event:x", "other" })
                {
                    rig.Base.Ledger.Change(AttitudeRig.Amd, 1f, reason);
                }
                rig.State.cautions.Add(new CautionEntry { uid = AttitudeRig.Amd, flawId = 1, until = "1992-01-01" });
                IntelCompanyFacts amd = IntelRig.Amd();
                amd.HasReputation = true;

                foreach (bool hidden in new[] { true, false })
                {
                    foreach (bool fog in new[] { true, false })
                    {
                        IntelDetails details = rig.Builder.BuildDetails(amd, rig.Settings(hidden, fog));
                        foreach (IntelSection section in details.Sections)
                        {
                            foreach (IntelLine line in section.Lines) Assert.False(line.Text.Contains("[rival."), language + ": " + line.Text);
                        }
                    }
                }
                rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Tsmc, "TSMC"), rig.Settings(false, true));
                rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Ibm, "IBM", true), rig.Settings(false, true));
                rig.State.dormant.Add(AttitudeRig.Tsmc);
                rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Tsmc, "TSMC"), rig.Settings(false, true));

                Assert.Equal(0, rig.Text.Missing.Count, language + ": " + string.Join(", ", rig.Text.Missing));
            }
        }

        // ---- the numbers as the panel writes them ----

        [Test]
        public void TheNumbers_AreWrittenWithTheirSign()
        {
            Assert.Equal("+12", IntelBuilder.SignedWhole(12f));
            Assert.Equal("+13", IntelBuilder.SignedWhole(12.5f), "halves go away from zero");
            Assert.Equal("-15", IntelBuilder.SignedWhole(-15f));
            Assert.Equal("-13", IntelBuilder.SignedWhole(-12.5f));
            Assert.Equal("0", IntelBuilder.SignedWhole(0.2f));
            Assert.Equal("0", IntelBuilder.SignedWhole(-0.2f));
            Assert.Equal("0", IntelBuilder.SignedWhole(float.NaN));

            Assert.Equal("-9.6", IntelBuilder.DeltaText(-9.6f));
            Assert.Equal("+4", IntelBuilder.DeltaText(4f));
            Assert.Equal("+0.5", IntelBuilder.DeltaText(0.5f));
            Assert.Equal("0", IntelBuilder.DeltaText(0f));
            Assert.Equal("0", IntelBuilder.DeltaText(float.PositiveInfinity));
        }

        [Test]
        public void TheBuilder_RefusesNothingItNeeds_AndSaysSoForNull()
        {
            var rig = new IntelRig();
            Assert.Throws<ArgumentNullException>(() => rig.Builder.BuildRow(null, rig.Settings(false, true)));
            Assert.Throws<ArgumentNullException>(() => new IntelBuilder(null, rig.Base.Ledger, rig.Reactions, () => rig.State, () => rig.Base.Catalog, rig.Text));
            Assert.Throws<ArgumentNullException>(() => new IntelBuilder(rig.Base.Book, rig.Base.Ledger, rig.Reactions, () => rig.State, () => rig.Base.Catalog, null));

            // a missing state or catalog is an empty one, not a crash
            var bare = new IntelBuilder(rig.Base.Book, rig.Base.Ledger, rig.Reactions, null, null, rig.Text);
            Assert.NotNull(bare.BuildDetails(IntelRig.Amd(), rig.Settings(false, true)));
        }
    }
}
