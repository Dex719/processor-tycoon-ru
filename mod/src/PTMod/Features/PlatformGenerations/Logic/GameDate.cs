using System;
using System.Globalization;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// A plain calendar date of the game (year, month, day) without <c>UnityEngine</c> and without a time of day: what the models of
    /// platform-generations store and compare (announce and launch dates, the day a socket appeared). The adapter builds it from
    /// <c>DateController.CurrentDate</c> with <see cref="FromDateTime"/>. <c>default(GameDate)</c> (<see cref="None"/>) means "no date";
    /// every other value is a real date. In the save a date is the text <c>yyyy-MM-dd</c> (<see cref="ToString"/>, <see cref="TryParse"/>).
    /// </summary>
    public struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        /// <summary>The text format of a date in the side save file.</summary>
        public const string TextFormat = "yyyy-MM-dd";

        /// <summary>"No date": the unset value.</summary>
        public static readonly GameDate None = default(GameDate);

        public readonly int Year;
        public readonly int Month;
        public readonly int Day;

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> for a date that does not exist (month 13, 30 February, year 0).</summary>
        public GameDate(int year, int month, int day)
        {
            if (!IsValid(year, month, day))
                throw new ArgumentOutOfRangeException(nameof(day), "not a calendar date: " + year + "-" + month + "-" + day);

            Year = year;
            Month = month;
            Day = day;
        }

        /// <summary>True when the date is set (not <see cref="None"/>).</summary>
        public bool IsSet
        {
            get { return Year > 0; }
        }

        /// <summary>Months counted from year 0: the index that <see cref="MonthsTo"/> subtracts. 0 for <see cref="None"/>.</summary>
        public int MonthIndex
        {
            get { return IsSet ? Year * 12 + (Month - 1) : 0; }
        }

        /// <summary>True for a real calendar date (year 1..9999, month 1..12, a day that the month has).</summary>
        public static bool IsValid(int year, int month, int day)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1) return false;
            return day <= DateTime.DaysInMonth(year, month);
        }

        /// <summary>The date of a <see cref="DateTime"/> (the time of day is dropped). <c>default(DateTime)</c> gives <see cref="None"/>.</summary>
        public static GameDate FromDateTime(DateTime value)
        {
            if (value == default(DateTime)) return None;
            return new GameDate(value.Year, value.Month, value.Day);
        }

        /// <summary>The same day as a <see cref="DateTime"/> (midnight, unspecified kind); <c>default(DateTime)</c> for <see cref="None"/>.</summary>
        public DateTime ToDateTime()
        {
            return IsSet ? new DateTime(Year, Month, Day) : default(DateTime);
        }

        /// <summary>
        /// Reads <c>yyyy-MM-dd</c> exactly (the format of the save). False for null, an empty text, another format or a day that does not
        /// exist; <paramref name="date"/> is then <see cref="None"/>.
        /// </summary>
        public static bool TryParse(string text, out GameDate date)
        {
            date = None;
            if (string.IsNullOrEmpty(text) || text.Length != TextFormat.Length) return false;

            DateTime parsed;
            if (!DateTime.TryParseExact(text, TextFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return false;

            date = new GameDate(parsed.Year, parsed.Month, parsed.Day);
            return true;
        }

        /// <summary>
        /// Calendar months from this date to <paramref name="other"/>: the difference of the month numbers, days are ignored
        /// (1 January to 31 January is 0, 31 January to 1 February is 1). Negative when <paramref name="other"/> is earlier.
        /// A date that is not set counts as 0 months from year 0, so check <see cref="IsSet"/> first.
        /// </summary>
        public int MonthsTo(GameDate other)
        {
            return other.MonthIndex - MonthIndex;
        }

        /// <summary>The date <paramref name="months"/> later (earlier for a negative number); the day is cut to the length of the month. <see cref="None"/> stays <see cref="None"/>.</summary>
        public GameDate AddMonths(int months)
        {
            if (!IsSet) return None;

            int index = Year * 12 + (Month - 1) + months;
            if (index < 12 || index >= 10000 * 12) throw new ArgumentOutOfRangeException(nameof(months), "the result is outside the years 1..9999");

            int year = index / 12;
            int month = index % 12 + 1;
            int day = Math.Min(Day, DateTime.DaysInMonth(year, month));
            return new GameDate(year, month, day);
        }

        /// <summary><c>yyyy-MM-dd</c>; an empty text for <see cref="None"/>.</summary>
        public override string ToString()
        {
            if (!IsSet) return string.Empty;
            return Year.ToString("D4", CultureInfo.InvariantCulture) + "-" + Month.ToString("D2", CultureInfo.InvariantCulture)
                   + "-" + Day.ToString("D2", CultureInfo.InvariantCulture);
        }

        public bool Equals(GameDate other)
        {
            return Year == other.Year && Month == other.Month && Day == other.Day;
        }

        public override bool Equals(object obj)
        {
            return obj is GameDate && Equals((GameDate)obj);
        }

        public override int GetHashCode()
        {
            return (Year * 13 + Month) * 32 + Day;
        }

        /// <summary>Chronological order; <see cref="None"/> is earlier than every real date.</summary>
        public int CompareTo(GameDate other)
        {
            if (Year != other.Year) return Year < other.Year ? -1 : 1;
            if (Month != other.Month) return Month < other.Month ? -1 : 1;
            if (Day != other.Day) return Day < other.Day ? -1 : 1;
            return 0;
        }

        public static bool operator ==(GameDate left, GameDate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameDate left, GameDate right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(GameDate left, GameDate right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(GameDate left, GameDate right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(GameDate left, GameDate right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(GameDate left, GameDate right)
        {
            return left.CompareTo(right) >= 0;
        }
    }
}
