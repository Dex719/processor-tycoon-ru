using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Which side of a numeric row of the compare table is the better one (FR-6 item 4). The same three values as the public
    /// <c>Better</c> of the API; this copy lives with the pure logic so the tests can reach it, and the window turns one into the other at the border.
    /// </summary>
    public enum CompareDirection
    {
        /// <summary>No direction: the row is never highlighted (price, die size, year, text rows).</summary>
        None = 0,

        /// <summary>A larger number is better (IPS, frequency, cores).</summary>
        Higher = 1,

        /// <summary>A smaller number is better (power, temperature, unit cost).</summary>
        Lower = 2,
    }

    /// <summary>The setting of one built-in row of the compare table: which direction is better and where the row stands (<c>compare.rows</c> of <c>ui.json</c>).</summary>
    public sealed class CompareRowSpec
    {
        public readonly string Id;
        public readonly CompareDirection Better;

        /// <summary>Sort key, ascending (the built-in rows use 1...200; the rows of other features come after, from 205).</summary>
        public readonly int Order;

        public CompareRowSpec(string id, CompareDirection better, int order)
        {
            Id = id;
            Better = better;
            Order = order;
        }
    }

    /// <summary>
    /// The numbers of the compare table that <c>ui.json</c> holds (design: "Контент", the section <c>compare</c>): the threshold of the
    /// highlight (<see cref="DeltaHighlightPercent"/>, 2.5 % by default) and, for each built-in row, the direction and the position. The
    /// rows themselves (what each shows) are in <see cref="CompareModel"/>; a file can move a row and change the direction, not invent one.
    /// <para>
    /// Every value is checked on reading: a missing one keeps the default silently, a wrong one keeps the default and adds a line to
    /// <c>problems</c> (the adapter writes them to the log, decision 0006). Nothing throws; at most <see cref="MaxRecords"/> records are read.
    /// </para>
    /// </summary>
    public sealed class CompareConfig
    {
        public const float DefaultHighlightPercent = 2.5f;
        public const float MinHighlightPercent = 0f;
        public const float MaxHighlightPercent = 50f;

        /// <summary>At most this many records of <c>compare.rows</c> are read (design: "Security", 64 rows of the compare table).</summary>
        public const int MaxRecords = 64;

        private readonly Dictionary<string, CompareRowSpec> _rows = new Dictionary<string, CompareRowSpec>(StringComparer.Ordinal);

        /// <summary>The smallest difference, in percent, that highlights the better side.</summary>
        public float DeltaHighlightPercent = DefaultHighlightPercent;

        /// <summary>The setting of a built-in row, or null for an id this version does not know.</summary>
        public CompareRowSpec SpecOf(string id)
        {
            CompareRowSpec spec;
            return id != null && _rows.TryGetValue(id, out spec) ? spec : null;
        }

        /// <summary>The ids of the rows that have a setting.</summary>
        public IEnumerable<string> RowIds
        {
            get { return _rows.Keys; }
        }

        /// <summary>The values of the spec (the same as the shipped <c>ui.json</c>).</summary>
        public static CompareConfig Defaults()
        {
            var config = new CompareConfig();
            Put(config, "company", CompareDirection.None, 1);
            Put(config, "manufacturer", CompareDirection.None, 2);
            Put(config, "package", CompareDirection.None, 3);
            Put(config, "lithography", CompareDirection.None, 4);
            Put(config, "ips", CompareDirection.Higher, 10);
            Put(config, "ipc", CompareDirection.Higher, 20);
            Put(config, "frequency", CompareDirection.Higher, 30);
            Put(config, "cores", CompareDirection.Higher, 40);
            Put(config, "smt", CompareDirection.None, 45);
            Put(config, "power", CompareDirection.Lower, 50);
            Put(config, "temperature", CompareDirection.Lower, 60);
            Put(config, "yield", CompareDirection.Higher, 70);
            Put(config, "unitCost", CompareDirection.Lower, 80);
            Put(config, "price", CompareDirection.None, 90);
            Put(config, "margin", CompareDirection.Higher, 100);
            Put(config, "ipsPerWatt", CompareDirection.Higher, 110);
            Put(config, "ipsPerDollar", CompareDirection.Higher, 120);
            Put(config, "popularity", CompareDirection.Higher, 130);
            Put(config, "soldLastMonth", CompareDirection.Higher, 140);
            Put(config, "dieSize", CompareDirection.None, 150);
            Put(config, "transistors", CompareDirection.None, 160);
            Put(config, "memory", CompareDirection.None, 165);
            Put(config, "l1", CompareDirection.Higher, 170);
            Put(config, "l2", CompareDirection.Higher, 180);
            Put(config, "l3", CompareDirection.Higher, 190);
            Put(config, "year", CompareDirection.None, 200);
            return config;
        }

        /// <summary>Reads the section <c>compare</c> of the merged <c>ui.json</c> over the defaults. A null file or a missing section gives the defaults.</summary>
        public static CompareConfig FromConfig(JsonObject config, List<string> problems)
        {
            CompareConfig result = Defaults();
            if (config == null) return result;

            JsonNode node;
            if (!config.TryGetValue("compare", out node)) return result;

            var section = node as JsonObject;
            if (section == null)
            {
                Report(problems, "compare: expected an object, the defaults are used");
                return result;
            }

            ReadThreshold(section, result, problems);

            JsonNode rowsNode;
            if (!section.TryGetValue("rows", out rowsNode)) return result;

            var rows = rowsNode as JsonArray;
            if (rows == null)
            {
                Report(problems, "compare.rows: expected an array, the defaults are used");
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonNode item in rows)
            {
                string path = "compare.rows[" + index + "]";
                index++;

                if (index > MaxRecords)
                {
                    Report(problems, "compare.rows: more than " + MaxRecords + " records, the rest are skipped");
                    break;
                }

                ReadRow(item as JsonObject, path, result, seen, problems);
            }
            return result;
        }

        /// <summary>The direction written in <c>ui.json</c> ("higher", "lower", "none", any case); false for anything else.</summary>
        public static bool TryParseDirection(string text, out CompareDirection direction)
        {
            direction = CompareDirection.None;
            if (text == null) return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "higher":
                    direction = CompareDirection.Higher;
                    return true;
                case "lower":
                    direction = CompareDirection.Lower;
                    return true;
                case "none":
                    direction = CompareDirection.None;
                    return true;
                default:
                    return false;
            }
        }

        private static void ReadThreshold(JsonObject section, CompareConfig result, List<string> problems)
        {
            double value;
            if (section.TryGetDouble("deltaHighlightPercent", out value))
            {
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= MinHighlightPercent && value <= MaxHighlightPercent)
                    result.DeltaHighlightPercent = (float)value;
                else
                    Report(problems, "compare.deltaHighlightPercent: expected a number from " + MinHighlightPercent + " to " + MaxHighlightPercent
                        + ", " + result.DeltaHighlightPercent + " is used");
            }
            else if (section.ContainsKey("deltaHighlightPercent"))
            {
                Report(problems, "compare.deltaHighlightPercent: not a number, " + result.DeltaHighlightPercent + " is used");
            }
        }

        private static void ReadRow(JsonObject record, string path, CompareConfig result, HashSet<string> seen, List<string> problems)
        {
            if (record == null)
            {
                Report(problems, path + ": expected an object, the record is skipped");
                return;
            }

            string id = record.GetString("id");
            if (string.IsNullOrEmpty(id))
            {
                Report(problems, path + ": 'id' is missing or empty, the record is skipped");
                return;
            }

            CompareRowSpec known = result.SpecOf(id);
            if (known == null)
            {
                Report(problems, path + " (" + id + "): this version has no such row, the record is skipped");
                return;
            }
            if (!seen.Add(id))
            {
                Report(problems, path + " (" + id + "): the id is used already, the record is skipped");
                return;
            }

            CompareDirection direction = known.Better;
            string text;
            if (record.TryGetString("better", out text))
            {
                if (!TryParseDirection(text, out direction))
                {
                    Report(problems, path + " (" + id + "): 'better' must be higher, lower or none, the record is skipped");
                    return;
                }
            }
            else if (record.ContainsKey("better"))
            {
                Report(problems, path + " (" + id + "): 'better' must be a string, the record is skipped");
                return;
            }

            int order = known.Order;
            if (record.ContainsKey("order") && !record.TryGetInt32("order", out order))
            {
                Report(problems, path + " (" + id + "): 'order' must be a whole number, the record is skipped");
                return;
            }

            Put(result, id, direction, order);
        }

        private static void Put(CompareConfig config, string id, CompareDirection better, int order)
        {
            config._rows[id] = new CompareRowSpec(id, better, order);
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
