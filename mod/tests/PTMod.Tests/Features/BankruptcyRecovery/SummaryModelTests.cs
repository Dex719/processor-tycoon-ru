using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-013 (FR-2 item 3, AC-2.2): the six rows of the "Summary" tab. The model picks the key and the number of every row; the fake text prints them
    /// (<c>rec.summary.daysLeft(90)</c>), so the tests see what the window would write without the game.
    /// </summary>
    [Covers(typeof(SummaryModel), typeof(SummaryRow), typeof(SummaryInput), typeof(MenuTone))]
    public class SummaryModelTests
    {
        private static readonly IMenuText Text = new FakeMenuText();

        private static SummaryInput InDefault30Days()
        {
            // AC-2.2: 30 days in default, 90 left, the balance 2 million below a threshold of 10 million
            return new SummaryInput
            {
                Money = -12000000.0,
                Limit = 10000000.0,
                Deficit = 2000000.0,
                LoansOutstanding = 8400000.0,
                NextPayment = 818892.0,
                NextPaymentDate = new DateTime(1976, 2, 1),
                InDefault = true,
                DaysToBankruptcy = 90,
            };
        }

        private static SummaryRow Row(IReadOnlyList<SummaryRow> rows, string id)
        {
            foreach (SummaryRow row in rows)
            {
                if (row.Id == id) return row;
            }
            Assert.Fail("no row " + id);
            return null;
        }

        [Test]
        public void Rows_AreTheSixOfTheDesign_InOrder()
        {
            IReadOnlyList<SummaryRow> rows = SummaryModel.Rows(Text, InDefault30Days());

            Assert.Equal(SummaryModel.RowCount, rows.Count);
            Assert.Equal(6, rows.Count);
            string[] ids = { "balance", "threshold", "deficit", "bankruptcy", "debt", "payment" };
            for (int i = 0; i < ids.Length; i++) Assert.Equal(ids[i], rows[i].Id, "row " + i);

            Assert.Equal("rec.summary.label.balance", rows[0].Title);
            Assert.Equal("rec.summary.label.threshold", rows[1].Title);
            Assert.Equal("rec.summary.label.bankruptcy", rows[3].Title);
            Assert.Equal("rec.summary.label.debt", rows[4].Title);
            Assert.Equal("rec.summary.label.payment", rows[5].Title);
        }

        [Test]
        public void ThirtyDaysInDefault_ShowsNinetyDaysLeft_AndTheDeficitInDollars_AC_2_2()
        {
            IReadOnlyList<SummaryRow> rows = SummaryModel.Rows(Text, InDefault30Days());

            Assert.Equal("-$12000000", Row(rows, "balance").Value);
            Assert.Equal(MenuTone.Bad, Row(rows, "balance").Tone, "a negative balance is painted as a danger");
            Assert.Equal("-$10000000", Row(rows, "threshold").Value, "the threshold is -D");

            SummaryRow deficit = Row(rows, "deficit");
            Assert.Equal("rec.summary.label.deficit", deficit.Title);
            Assert.Equal("$2000000", deficit.Value);
            Assert.Equal(MenuTone.Bad, deficit.Tone);

            SummaryRow days = Row(rows, "bankruptcy");
            Assert.Equal("rec.summary.daysLeft(90)", days.Value);
            Assert.Equal(MenuTone.Warn, days.Tone, "90 days left is a warning, not yet a danger");
        }

        [Test]
        public void TheDaysToBankruptcy_BecomeADanger_FromThirtyDaysDown()
        {
            SummaryInput input = InDefault30Days();

            input.DaysToBankruptcy = SummaryModel.UrgentDays;
            Assert.Equal(MenuTone.Bad, Row(SummaryModel.Rows(Text, input), "bankruptcy").Tone);

            input.DaysToBankruptcy = SummaryModel.UrgentDays + 1;
            Assert.Equal(MenuTone.Warn, Row(SummaryModel.Rows(Text, input), "bankruptcy").Tone);

            input.DaysToBankruptcy = -5;
            Assert.Equal("rec.summary.daysLeft(0)", Row(SummaryModel.Rows(Text, input), "bankruptcy").Value, "a negative count is shown as 0");
        }

        [Test]
        public void NotInDefault_SaysSo_AndTheRowShowsTheRoomAboveTheThreshold()
        {
            var input = new SummaryInput { Money = 2000000.0, Limit = 10000000.0, Deficit = 0.0, InDefault = false };

            IReadOnlyList<SummaryRow> rows = SummaryModel.Rows(Text, input);

            SummaryRow state = Row(rows, "bankruptcy");
            Assert.Equal("rec.summary.notInDefault", state.Value);
            Assert.Equal(MenuTone.Good, state.Tone);

            SummaryRow room = Row(rows, "deficit");
            Assert.Equal("rec.summary.label.headroom", room.Title, "above the threshold the row is the room left, not a deficit");
            Assert.Equal("$12000000", room.Value, "balance + D");
            Assert.Equal(MenuTone.Good, room.Tone);
            Assert.Equal(MenuTone.Normal, Row(rows, "balance").Tone);
        }

        [Test]
        public void TheNextPayment_HasItsSumAndDate_OrSaysThereIsNone()
        {
            SummaryRow with = Row(SummaryModel.Rows(Text, InDefault30Days()), "payment");
            Assert.Equal("rec.summary.payment($818892|d:1976-02-01)", with.Value);

            SummaryInput none = InDefault30Days();
            none.NextPayment = 0.0;
            none.NextPaymentDate = null;
            SummaryRow without = Row(SummaryModel.Rows(Text, none), "payment");
            Assert.Equal("rec.summary.noPayment", without.Value);
            Assert.Equal(MenuTone.Muted, without.Tone);

            SummaryInput noDate = InDefault30Days();
            noDate.NextPaymentDate = null;
            Assert.Equal("rec.summary.payment($818892|-)", Row(SummaryModel.Rows(Text, noDate), "payment").Value, "a payment with no known date shows a dash");
        }

        [Test]
        public void TheDebtRow_IsMuted_WhenThereIsNoDebt()
        {
            SummaryInput input = InDefault30Days();
            Assert.Equal("$8400000", Row(SummaryModel.Rows(Text, input), "debt").Value);
            Assert.Equal(MenuTone.Normal, Row(SummaryModel.Rows(Text, input), "debt").Tone);

            input.LoansOutstanding = 0.0;
            Assert.Equal("$0", Row(SummaryModel.Rows(Text, input), "debt").Value);
            Assert.Equal(MenuTone.Muted, Row(SummaryModel.Rows(Text, input), "debt").Tone);
        }

        [Test]
        public void AnUnknownThreshold_ShowsDashes_InsteadOfInventingOne()
        {
            var input = new SummaryInput { Money = 500000.0, Limit = 0.0 };

            IReadOnlyList<SummaryRow> rows = SummaryModel.Rows(Text, input);

            Assert.Equal("-", Row(rows, "threshold").Value);
            Assert.Equal("-", Row(rows, "deficit").Value);
            Assert.Equal(MenuTone.Normal, Row(rows, "deficit").Tone);
            Assert.Equal(6, rows.Count, "the table has its six rows all the same");
        }
    }
}
