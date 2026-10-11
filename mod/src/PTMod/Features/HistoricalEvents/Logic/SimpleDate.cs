using System;
using System.Globalization;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// A calendar date without a time of day: how the calendar writes its dates (<c>"1995-08-24"</c>) and how the save counts them (whole days from
    /// 1971-01-01, the same day numbers as in event-system). A value type; <c>default(SimpleDate)</c> is "not set" (<see cref="IsSet"/> is false).
    /// The logic of this folder never touches <c>DateTime.Now</c>: the current date always comes in from the caller.
    /// </summary>
    public struct SimpleDate : IEquatable<SimpleDate>, IComparable<SimpleDate>
    {
        /// <summary>The year of day 0 (1 January).</summary>
        public const int EpochYear = 1971;

        private const string Format = "yyyy-MM-dd";
        private static readonly DateTime Epoch = new DateTime(EpochYear, 1, 1);

        private readonly int _year;
        private readonly int _month;
        private readonly int _day;

        /// <summary>A date; throws <see cref="ArgumentOutOfRangeException"/> for a day that does not exist (use <see cref="TryCreate"/> for data).</summary>
        public SimpleDate(int year, int month, int day)
        {
            if (!Exists(year, month, day)) throw new ArgumentOutOfRangeException("day", "no such date: " + year + "-" + month + "-" + day);
            _year = year;
            _month = month;
            _day = day;
        }

        public int Year
        {
            get { return _year; }
        }

        public int Month
        {
            get { return _month; }
        }

        public int Day
        {
            get { return _day; }
        }

        /// <summary>The date holds a real day (a default value does not).</summary>
        public bool IsSet
        {
            get { return _year != 0; }
        }

        /// <summary>Whole days from 1971-01-01 (negative before it); 0 for a date that is not set - check <see cref="IsSet"/> first.</summary>
        public int Days
        {
            get { return IsSet ? (int)(new DateTime(_year, _month, _day) - Epoch).TotalDays : 0; }
        }

        public static bool TryCreate(int year, int month, int day, out SimpleDate date)
        {
            if (!Exists(year, month, day))
            {
                date = default(SimpleDate);
                return false;
            }

            date = new SimpleDate(year, month, day);
            return true;
        }

        /// <summary>Reads exactly <c>yyyy-MM-dd</c> (four digits, two, two) naming a real day; anything else is false.</summary>
        public static bool TryParse(string text, out SimpleDate date)
        {
            date = default(SimpleDate);
            if (text == null || text.Length != 10 || text[4] != '-' || text[7] != '-') return false;
            for (int i = 0; i < 10; i++)
            {
                if (i == 4 || i == 7) continue;
                if (text[i] < '0' || text[i] > '9') return false;
            }

            int year = int.Parse(text.Substring(0, 4), CultureInfo.InvariantCulture);
            int month = int.Parse(text.Substring(5, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(text.Substring(8, 2), CultureInfo.InvariantCulture);
            return TryCreate(year, month, day, out date);
        }

        /// <summary>The date of a day number (days from 1971-01-01); false when it falls outside the years 1..9999.</summary>
        public static bool TryFromDays(int days, out SimpleDate date)
        {
            date = default(SimpleDate);
            double target = Epoch.Ticks / (double)TimeSpan.TicksPerDay + days;
            if (target < 0 || target >= DateTime.MaxValue.Ticks / (double)TimeSpan.TicksPerDay) return false;

            DateTime value = Epoch.AddDays(days);
            date = new SimpleDate(value.Year, value.Month, value.Day);
            return true;
        }

        public static SimpleDate FromDays(int days)
        {
            SimpleDate date;
            if (!TryFromDays(days, out date)) throw new ArgumentOutOfRangeException("days", "day number out of range: " + days);
            return date;
        }

        /// <summary>The calendar day of a date-time (the time of day is dropped). <c>default(DateTime)</c> is 0001-01-01, which callers treat as "unknown".</summary>
        public static SimpleDate FromDateTime(DateTime value)
        {
            return new SimpleDate(value.Year, value.Month, value.Day);
        }

        public DateTime ToDateTime()
        {
            return IsSet ? new DateTime(_year, _month, _day) : default(DateTime);
        }

        /// <summary>The date a number of days later (earlier for a negative number). A date that is not set stays unset.</summary>
        public SimpleDate AddDays(int days)
        {
            if (!IsSet) return this;
            SimpleDate result;
            return TryFromDays(Days + days, out result) ? result : this;
        }

        public bool Equals(SimpleDate other)
        {
            return _year == other._year && _month == other._month && _day == other._day;
        }

        public override bool Equals(object obj)
        {
            return obj is SimpleDate && Equals((SimpleDate)obj);
        }

        public override int GetHashCode()
        {
            return (_year * 372) + (_month * 31) + _day;
        }

        public int CompareTo(SimpleDate other)
        {
            if (_year != other._year) return _year < other._year ? -1 : 1;
            if (_month != other._month) return _month < other._month ? -1 : 1;
            if (_day != other._day) return _day < other._day ? -1 : 1;
            return 0;
        }

        /// <summary><c>yyyy-MM-dd</c>, or <c>(not set)</c>.</summary>
        public override string ToString()
        {
            return IsSet ? new DateTime(_year, _month, _day).ToString(Format, CultureInfo.InvariantCulture) : "(not set)";
        }

        public static bool operator ==(SimpleDate a, SimpleDate b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SimpleDate a, SimpleDate b)
        {
            return !a.Equals(b);
        }

        public static bool operator <(SimpleDate a, SimpleDate b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(SimpleDate a, SimpleDate b)
        {
            return a.CompareTo(b) > 0;
        }

        public static bool operator <=(SimpleDate a, SimpleDate b)
        {
            return a.CompareTo(b) <= 0;
        }

        public static bool operator >=(SimpleDate a, SimpleDate b)
        {
            return a.CompareTo(b) >= 0;
        }

        private static bool Exists(int year, int month, int day)
        {
            return year >= 1 && year <= 9999 && month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);
        }
    }
}
