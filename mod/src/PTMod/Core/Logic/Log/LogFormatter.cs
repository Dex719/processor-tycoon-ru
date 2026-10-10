using System;
using System.Globalization;
using System.Text;

namespace PTMod.Core
{
    /// <summary>
    /// Severity of a ModLog record. <see cref="Debug"/> records are written only when verbose logging
    /// is on (decided by the ModLog adapter, not by the logic classes).
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }
}

namespace PTMod.Core.Logic.Log
{
    /// <summary>
    /// Pure text rules of ModLog: the line format <c>[PTMod] [&lt;feature-id&gt;] LEVEL message</c>,
    /// the repeat key (feature + text with digit runs collapsed to <c>#</c>) and the repeat summary line.
    /// No Unity and no clock: the ModLog adapter hands the finished line to <c>Debug.Log*</c>.
    /// </summary>
    public static class LogFormatter
    {
        /// <summary>Fixed prefix of every line the mod writes.</summary>
        public const string Prefix = "[PTMod]";

        /// <summary>Feature id used for records of the framework itself and for records without a feature.</summary>
        public const string CoreFeatureId = "mod-framework";

        /// <summary>Longest inner-exception chain written into a line.</summary>
        private const int MaxExceptionDepth = 4;

        /// <summary>Upper-case level name as it appears in the line.</summary>
        public static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO";
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERROR";
                default: return "UNKNOWN";
            }
        }

        /// <summary>
        /// Feature id as written in the line and used in repeat keys: trimmed; null or blank becomes the
        /// framework id; brackets and control characters (which would break the line format) become spaces.
        /// </summary>
        public static string NormalizeFeatureId(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId)) return CoreFeatureId;

            string trimmed = featureId.Trim();
            StringBuilder sb = null;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool bad = c == '[' || c == ']' || char.IsControl(c);
                if (bad && sb == null) sb = new StringBuilder(trimmed, 0, i, trimmed.Length);
                if (sb != null) sb.Append(bad ? ' ' : c);
            }
            return sb == null ? trimmed : sb.ToString();
        }

        /// <summary><c>[PTMod] [id] LEVEL message</c>; a null or empty message leaves just the header.</summary>
        public static string Format(string featureId, LogLevel level, string message)
        {
            return Format(featureId, level, message, null, false);
        }

        /// <summary>
        /// Line with an exception: <c>[PTMod] [id] ERROR message: Type: text &lt;- InnerType: text</c>.
        /// With <paramref name="includeStackTrace"/> the stack trace of the exception follows on the next lines.
        /// </summary>
        public static string Format(string featureId, LogLevel level, string message, Exception exception, bool includeStackTrace)
        {
            var sb = new StringBuilder(64 + (message == null ? 0 : message.Length));
            sb.Append(Prefix).Append(" [").Append(NormalizeFeatureId(featureId)).Append("] ").Append(LevelName(level));

            bool hasMessage = !string.IsNullOrEmpty(message);
            if (hasMessage) sb.Append(' ').Append(message);

            if (exception != null)
            {
                sb.Append(hasMessage ? ": " : " ").Append(DescribeException(exception));
                string trace = includeStackTrace ? exception.StackTrace : null;
                if (!string.IsNullOrEmpty(trace)) sb.Append('\n').Append(trace);
            }
            return sb.ToString();
        }

        /// <summary>"Type: message" of the exception followed by up to three inner exceptions, joined with " &lt;- ".</summary>
        public static string DescribeException(Exception exception)
        {
            if (exception == null) return string.Empty;

            var sb = new StringBuilder();
            Exception current = exception;
            for (int depth = 0; current != null && depth < MaxExceptionDepth; depth++)
            {
                if (depth > 0) sb.Append(" <- ");
                sb.Append(current.GetType().FullName).Append(": ").Append(current.Message);
                current = current.InnerException;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Message text with every run of digits replaced by one <c>#</c>, so "tick 5 failed" and
        /// "tick 1042 failed" are the same message. Text without digits is returned as is.
        /// </summary>
        public static string CollapseDigits(string message)
        {
            if (string.IsNullOrEmpty(message)) return message ?? string.Empty;

            int first = -1;
            for (int i = 0; i < message.Length; i++)
            {
                if (char.IsDigit(message[i]))
                {
                    first = i;
                    break;
                }
            }
            if (first < 0) return message;

            var sb = new StringBuilder(message.Length);
            sb.Append(message, 0, first);
            bool inDigits = false;
            for (int i = first; i < message.Length; i++)
            {
                char c = message[i];
                if (char.IsDigit(c))
                {
                    if (!inDigits) sb.Append('#');
                    inDigits = true;
                }
                else
                {
                    inDigits = false;
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Key under which the repeat limiter counts a message: normalized feature id + text without numbers.</summary>
        public static string RepeatKey(string featureId, string message)
        {
            return NormalizeFeatureId(featureId) + "\n" + CollapseDigits(message);
        }

        /// <summary>
        /// The wording of every repeat summary: "повторено 597 раз", "повторено 4 раза" (AC-2.2, AC-7.1). Russian plural of "раз":
        /// "раза" after 2-4 except 12-14, "раз" otherwise.
        /// </summary>
        public static string RepeatedPhrase(int count)
        {
            int mod10 = Math.Abs(count % 10), mod100 = Math.Abs(count % 100);
            bool few = mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14);
            return "повторено " + count.ToString(CultureInfo.InvariantCulture) + (few ? " раза" : " раз");
        }

        /// <summary>Summary line written after a repeat window: the message with "(повторено N раз)" appended; N = suppressed repeats.</summary>
        public static string FormatSummary(string featureId, LogLevel level, string message, int suppressed)
        {
            string text = (message ?? string.Empty) + " (" + RepeatedPhrase(suppressed) + ")";
            return Format(featureId, level, text, null, false);
        }

        /// <summary>Summary line for a closed repeat window of <see cref="RepeatLimiter"/>.</summary>
        public static string FormatSummary(RepeatSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            return FormatSummary(summary.FeatureId, summary.Level, summary.Message, summary.Suppressed);
        }
    }
}
