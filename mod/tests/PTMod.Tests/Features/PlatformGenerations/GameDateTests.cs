using System;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>The plain date of the models: validity, the text of the save (<c>yyyy-MM-dd</c>), the order, the months between two dates.</summary>
    [Covers(typeof(GameDate))]
    public class GameDateTests
    {
        [Test]
        public void ADate_KeepsItsParts_AndIsSet()
        {
            var date = new GameDate(1995, 3, 17);
            Assert.Equal(1995, date.Year);
            Assert.Equal(3, date.Month);
            Assert.Equal(17, date.Day);
            Assert.True(date.IsSet);
        }

        [Test]
        public void ADateThatDoesNotExist_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(1995, 13, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(1995, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(1995, 2, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(1995, 2, 29));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(10000, 1, 1));
            Assert.True(GameDate.IsValid(1996, 2, 29), "1996 is a leap year");
            Assert.False(GameDate.IsValid(1900, 2, 29), "1900 is not");
        }

        [Test]
        public void None_IsNotSet_AndIsTheDefault()
        {
            Assert.False(GameDate.None.IsSet);
            Assert.False(default(GameDate).IsSet);
            Assert.Equal(GameDate.None, default(GameDate));
            Assert.Equal(string.Empty, GameDate.None.ToString());
        }

        [Test]
        public void ToString_IsTheTextOfTheSave_AndTryParseReadsItBack()
        {
            var date = new GameDate(1990, 2, 14);
            Assert.Equal("1990-02-14", date.ToString());

            GameDate back;
            Assert.True(GameDate.TryParse("1990-02-14", out back));
            Assert.Equal(date, back);
            Assert.True(GameDate.TryParse("2030-12-31", out back));
            Assert.Equal(new GameDate(2030, 12, 31), back);
        }

        [Test]
        public void TryParse_RefusesEverythingButTheExactFormat()
        {
            foreach (string text in new[] { null, string.Empty, "1990-2-14", "14.02.1990", "1990-02-30", "1990-13-01", "1990/02/14", " 1990-02-14", "1990-02-14T00:00", "abcd-ef-gh", "0000-01-01" })
            {
                GameDate date;
                Assert.False(GameDate.TryParse(text, out date), "'" + text + "' is not a date of the save");
                Assert.False(date.IsSet);
            }
        }

        [Test]
        public void FromDateTime_AndBack_DropTheTimeOfDay()
        {
            GameDate date = GameDate.FromDateTime(new DateTime(1985, 7, 4, 13, 45, 0));
            Assert.Equal(new GameDate(1985, 7, 4), date);
            Assert.Equal(new DateTime(1985, 7, 4), date.ToDateTime());
            Assert.False(GameDate.FromDateTime(default(DateTime)).IsSet);
            Assert.Equal(default(DateTime), GameDate.None.ToDateTime());
        }

        [Test]
        public void Dates_AreOrderedInTime_AndNoneIsTheEarliest()
        {
            var a = new GameDate(1990, 1, 1);
            var b = new GameDate(1990, 1, 2);
            var c = new GameDate(1990, 2, 1);
            var d = new GameDate(1991, 1, 1);
            Assert.True(a < b && b < c && c < d);
            Assert.True(d > c && c > b && b > a);
            var same = new GameDate(1990, 1, 1);
            Assert.True(a <= same && a >= same);
            Assert.True(a != b && a == new GameDate(1990, 1, 1));
            Assert.True(GameDate.None < a);
            Assert.Equal(0, a.CompareTo(new GameDate(1990, 1, 1)));
            Assert.Equal(-1, a.CompareTo(d));
            Assert.Equal(1, d.CompareTo(a));
            Assert.Equal(a.GetHashCode(), new GameDate(1990, 1, 1).GetHashCode());
            Assert.True(a.Equals((object)new GameDate(1990, 1, 1)));
            Assert.False(a.Equals((object)"1990-01-01"));
        }

        [Test]
        public void MonthsTo_CountsCalendarMonths_AndIgnoresDays()
        {
            Assert.Equal(0, new GameDate(2000, 1, 1).MonthsTo(new GameDate(2000, 1, 31)));
            Assert.Equal(1, new GameDate(2000, 1, 31).MonthsTo(new GameDate(2000, 2, 1)));
            Assert.Equal(12, new GameDate(2000, 3, 15).MonthsTo(new GameDate(2001, 3, 1)));
            Assert.Equal(23, new GameDate(2000, 3, 15).MonthsTo(new GameDate(2002, 2, 28)));
            Assert.Equal(-5, new GameDate(2000, 6, 1).MonthsTo(new GameDate(2000, 1, 1)));
        }

        [Test]
        public void AddMonths_MovesTheDate_AndCutsTheDayToTheMonth()
        {
            Assert.Equal(new GameDate(2001, 3, 15), new GameDate(2000, 3, 15).AddMonths(12));
            Assert.Equal(new GameDate(1999, 3, 15), new GameDate(2000, 3, 15).AddMonths(-12));
            Assert.Equal(new GameDate(2000, 2, 29), new GameDate(2000, 1, 31).AddMonths(1), "2000 is a leap year");
            Assert.Equal(new GameDate(2001, 2, 28), new GameDate(2000, 12, 31).AddMonths(2));
            Assert.Equal(new GameDate(2000, 3, 15), new GameDate(2000, 3, 15).AddMonths(0));
            Assert.False(GameDate.None.AddMonths(5).IsSet);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(2000, 1, 1).AddMonths(-30000));
        }

        [Test]
        public void MonthIndex_IsMonotonic()
        {
            Assert.Equal(0, GameDate.None.MonthIndex);
            Assert.Equal(new GameDate(2000, 1, 1).MonthIndex + 1, new GameDate(2000, 2, 28).MonthIndex);
            Assert.Equal(new GameDate(2000, 12, 1).MonthIndex + 1, new GameDate(2001, 1, 1).MonthIndex);
        }
    }
}
