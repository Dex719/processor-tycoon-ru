using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace PTMod.Tests
{
    /// <summary>Thrown by <see cref="Assert"/>; the runner reports it as a failed test (not as a crash).</summary>
    public sealed class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Assertions of the mini runner. Every method takes an optional trailing message that is put
    /// in front of the generated "expected / actual" text. Numbers are formatted with the invariant culture.
    /// </summary>
    public static class Assert
    {
        public static void True(bool condition, string message = null)
        {
            if (!condition) throw Failure("expected: true, actual: false", message);
        }

        public static void False(bool condition, string message = null)
        {
            if (condition) throw Failure("expected: false, actual: true", message);
        }

        /// <summary>
        /// Value equality via Equals; sequences (arrays, lists, any IEnumerable except string) are compared
        /// element by element in order, non-generic dictionaries by key regardless of order.
        /// </summary>
        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!AreEqual(expected, actual))
                throw Failure("expected: " + Format(expected) + ", actual: " + Format(actual), message);
        }

        public static void NotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (AreEqual(notExpected, actual))
                throw Failure("expected a value different from " + Format(notExpected), message);
        }

        public static void Null(object value, string message = null)
        {
            if (value != null) throw Failure("expected: null, actual: " + Format(value), message);
        }

        public static void NotNull(object value, string message = null)
        {
            if (value == null) throw Failure("expected: not null, actual: null", message);
        }

        /// <summary>
        /// |expected - actual| &lt;= tolerance. Two NaN are "near"; infinities are near only when equal.
        /// </summary>
        public static void Near(double expected, double actual, double tolerance = 1e-9, string message = null)
        {
            if (double.IsNaN(tolerance) || tolerance < 0)
                throw new ArgumentException("tolerance must be a non-negative number", nameof(tolerance));

            bool ok;
            if (double.IsNaN(expected) || double.IsNaN(actual))
                ok = double.IsNaN(expected) && double.IsNaN(actual);
            else if (double.IsInfinity(expected) || double.IsInfinity(actual))
                ok = expected == actual;
            else
                ok = Math.Abs(expected - actual) <= tolerance;

            if (!ok)
                throw Failure("expected: " + Format(expected) + " +/- " + Format(tolerance)
                              + ", actual: " + Format(actual), message);
        }

        /// <summary>min &lt;= value &lt;= max (both ends inclusive). NaN is never in range.</summary>
        public static void InRange<T>(T value, T min, T max, string message = null) where T : IComparable<T>
        {
            bool ok = value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
            // double.NaN.CompareTo(x) < 0 for any x, so NaN already fails the lower bound; this keeps the intent explicit.
            if (value is double d && double.IsNaN(d)) ok = false;
            if (!ok)
                throw Failure("expected a value in [" + Format(min) + ", " + Format(max) + "], actual: " + Format(value), message);
        }

        /// <summary>
        /// The action must throw <typeparamref name="T"/> or a subclass; returns the exception for further checks.
        /// A failed <see cref="Assert"/> inside the action is not swallowed unless T is <see cref="AssertFailedException"/>.
        /// </summary>
        public static T Throws<T>(Action action, string message = null) where T : Exception
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Exception thrown = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            if (thrown == null)
                throw Failure("expected exception " + typeof(T).Name + ", but nothing was thrown", message);

            if (thrown is AssertFailedException && !typeof(AssertFailedException).IsAssignableFrom(typeof(T)))
                throw thrown;

            var typed = thrown as T;
            if (typed == null)
                throw Failure("expected exception " + typeof(T).Name + ", actual: "
                              + thrown.GetType().Name + ": " + thrown.Message, message);
            return typed;
        }

        public static void Fail(string message = null)
        {
            throw new AssertFailedException(message ?? "Assert.Fail");
        }

        private static AssertFailedException Failure(string detail, string message)
        {
            return new AssertFailedException(string.IsNullOrEmpty(message) ? detail : message + " (" + detail + ")");
        }

        private static bool AreEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a is string || b is string) return a.Equals(b);

            var da = a as IDictionary;
            var db = b as IDictionary;
            if (da != null && db != null)
            {
                if (da.Count != db.Count) return false;
                foreach (DictionaryEntry entry in da)
                {
                    if (!db.Contains(entry.Key) || !AreEqual(entry.Value, db[entry.Key])) return false;
                }
                return true;
            }

            var ea = a as IEnumerable;
            var eb = b as IEnumerable;
            if (ea != null && eb != null)
            {
                IEnumerator ia = ea.GetEnumerator();
                IEnumerator ib = eb.GetEnumerator();
                while (true)
                {
                    bool hasA = ia.MoveNext();
                    bool hasB = ib.MoveNext();
                    if (hasA != hasB) return false;
                    if (!hasA) return true;
                    if (!AreEqual(ia.Current, ib.Current)) return false;
                }
            }

            return a.Equals(b);
        }

        private const int MaxShown = 300;

        private static string Format(object value)
        {
            string text = FormatCore(value);
            return text.Length > MaxShown ? text.Substring(0, MaxShown) + "..." : text;
        }

        private static string FormatCore(object value)
        {
            if (value == null) return "null";
            if (value is string s) return "\"" + Escape(s) + "\"";
            if (value is char c) return "'" + Escape(c.ToString()) + "'";
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is IDictionary dict)
            {
                var sbd = new StringBuilder("{");
                int n = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    if (n++ > 0) sbd.Append(", ");
                    if (sbd.Length > MaxShown) { sbd.Append("..."); break; }
                    sbd.Append(Format(entry.Key)).Append(": ").Append(Format(entry.Value));
                }
                return sbd.Append("}").ToString();
            }
            if (value is IEnumerable seq)
            {
                var sb = new StringBuilder("[");
                int n = 0;
                foreach (object item in seq)
                {
                    if (n++ > 0) sb.Append(", ");
                    if (sb.Length > MaxShown) { sb.Append("..."); break; }
                    sb.Append(Format(item));
                }
                return sb.Append("]").ToString();
            }
            if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
