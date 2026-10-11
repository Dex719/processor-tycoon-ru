using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Reads the <c>params</c> of an effect of the feature's own types (<c>AntitrustCase</c>, <c>ExportControl</c>, ...) with the ranges of the design:
    /// a member that is absent reads as its default (first the effect's own, then the optional <c>defaults</c> object of <c>params.json</c>), a member of
    /// the wrong type or outside its range is one English line in <c>errors</c> and also reads as the default. The handler's <c>Validate</c> turns any line
    /// into a rejection of the event, so a bad number in <c>events.json</c> is found when the content is loaded, not when a ruling is made.
    /// Common members of every effect (<c>scope</c>, <c>chance</c>, <c>scalable</c>, <c>exclusiveGroup</c>) are the engine's and never reported as unknown.
    /// </summary>
    internal sealed class ParamReader
    {
        private static readonly string[] CommonMembers = { "scope", "chance", "scalable", "exclusiveGroup" };

        private readonly JsonObject _raw;
        private readonly JsonObject _defaults;
        private readonly HistJsonReader _reader;
        private readonly List<string> _known = new List<string>(CommonMembers);

        /// <param name="raw">The <c>params</c> object of the effect (null reads as an empty one).</param>
        /// <param name="defaults">The block of <c>params.json</c> that supplies what <paramref name="raw"/> leaves out (may be null).</param>
        /// <param name="subject">What the lines are about (<c>AntitrustCase</c>).</param>
        /// <param name="errors">Where the lines go (may be null).</param>
        public ParamReader(JsonObject raw, JsonObject defaults, string subject, List<string> errors)
        {
            _raw = raw ?? new JsonObject();
            _defaults = defaults;
            _reader = new HistJsonReader(subject, errors);
        }

        /// <summary>How many problems were found.</summary>
        public int Problems
        {
            get { return _reader.Problems; }
        }

        public JsonObject Raw
        {
            get { return _raw; }
        }

        /// <summary>The member is present (in the effect or in the defaults).</summary>
        public bool Has(string name)
        {
            return HistJsonReader.Get(_raw, name) != null || HistJsonReader.Get(_defaults, name) != null;
        }

        public void Fail(string name, string message)
        {
            _reader.Fail(name, message);
        }

        /// <summary>A number from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public float Num(string name, float fallback, float min, float max)
        {
            Known(name);
            JsonObject source = Source(name);
            double value = _reader.Num(source, name, string.Empty, fallback);
            if (value < min || value > max)
            {
                _reader.Fail(name, value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " is outside " + Show(min) + ".." + Show(max));
                return fallback;
            }
            return (float)value;
        }

        /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public int Int(string name, int fallback, int min, int max)
        {
            Known(name);
            JsonObject source = Source(name);
            int value = _reader.Int(source, name, string.Empty, fallback);
            if (value < min || value > max)
            {
                _reader.Fail(name, value + " is outside " + min + ".." + max);
                return fallback;
            }
            return value;
        }

        public bool Bool(string name, bool fallback)
        {
            Known(name);
            return _reader.Bool(Source(name), name, string.Empty, fallback);
        }

        /// <summary>A string; <paramref name="fallback"/> when absent.</summary>
        public string Str(string name, string fallback)
        {
            Known(name);
            return _reader.Str(Source(name), name, string.Empty) ?? fallback;
        }

        /// <summary>A string out of a fixed set (case-sensitive, like the ids of the content); <paramref name="fallback"/> when absent.</summary>
        public string Choice(string name, string fallback, params string[] allowed)
        {
            string value = Str(name, fallback);
            if (value == null || Array.IndexOf(allowed, value) >= 0) return value;

            _reader.Fail(name, "\"" + value + "\" is not one of " + string.Join(", ", allowed));
            return fallback;
        }

        /// <summary>
        /// A list of segment names (<c>highend</c>, <c>midrange</c>, <c>lowend</c>, <c>industries</c>, or <c>all</c> for the four), in the order given and without
        /// repeats. Absent reads as <paramref name="fallback"/>; an unknown name is a problem.
        /// </summary>
        public List<Segment> Segments(string name, IEnumerable<Segment> fallback)
        {
            Known(name);
            var result = new List<Segment>();
            JsonObject source = Source(name);
            if (HistJsonReader.Get(source, name) == null)
            {
                if (fallback != null) result.AddRange(fallback);
                return result;
            }

            List<string> names = _reader.Strings(source, name, string.Empty);
            foreach (string text in names)
            {
                Segment segment;
                if (!HistSegments.TryParse(text, out segment))
                {
                    _reader.Fail(name, "unknown segment \"" + text + "\"");
                    continue;
                }

                if (segment == Segment.Any)
                {
                    foreach (Segment each in AllSegments) if (!result.Contains(each)) result.Add(each);
                }
                else if (!result.Contains(segment))
                {
                    result.Add(segment);
                }
            }

            if (result.Count == 0) _reader.Fail(name, "no segment is named");
            return result;
        }

        /// <summary>Warns about members nobody asked for (a typo in an optional member would be ignored silently).</summary>
        public void WarnUnknown()
        {
            _reader.WarnUnknown(_raw, string.Empty, _known.ToArray());
        }

        /// <summary>The four concrete segments.</summary>
        public static readonly Segment[] AllSegments = { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries };

        private void Known(string name)
        {
            if (!_known.Contains(name)) _known.Add(name);
        }

        /// <summary>The object that holds the member: the effect's own params when it has it, else the defaults (which may not have it either).</summary>
        private JsonObject Source(string name)
        {
            if (HistJsonReader.Get(_raw, name) != null) return _raw;
            if (_defaults != null && HistJsonReader.Get(_defaults, name) != null) return _defaults;
            return _raw;
        }

        private static string Show(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
