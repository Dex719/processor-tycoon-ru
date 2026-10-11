using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The checks every record goes through before the model keeps it (NFR-7): the strings are cut to 128 characters, the numbers are held in their range, the
    /// lists in their size, and a record that cannot be right (an unknown type, a date that is not a date) is refused with a line for the log. Applied to what a
    /// save holds and to what the recorders add, so both are held to the same rules. Works on the record in place.
    /// </summary>
    internal static class SaveSanitizer
    {
        /// <summary>Cleans a log entry. False (and a line in <paramref name="problems"/>) when the entry cannot be kept.</summary>
        internal static bool Entry(LogEntry entry, List<string> problems)
        {
            if (entry == null)
            {
                Add(problems, "an empty log entry");
                return false;
            }

            entry.type = TextSafe.Clean(entry.type);
            if (!LogTypes.IsKnown(entry.type))
            {
                Add(problems, "log entry of the unknown type '" + entry.type + "'");
                return false;
            }

            entry.date = TextSafe.Clean(entry.date);
            DateTime date;
            if (!LogDates.TryParse(entry.date, out date))
            {
                Add(problems, "log entry " + entry.type + " with the date '" + entry.date + "' which is not yyyy-MM-dd");
                return false;
            }

            entry.source = TextSafe.Clean(entry.source);
            entry.contractKey = TextSafe.Clean(entry.contractKey);
            entry.contractLabel = TextSafe.Clean(entry.contractLabel);
            entry.cpuName = TextSafe.Clean(entry.cpuName);
            entry.reasonKey = TextSafe.Clean(entry.reasonKey);
            if (entry.cpuId < 0) entry.cpuId = 0;

            var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entry.data != null)
            {
                foreach (KeyValuePair<string, string> pair in entry.data)
                {
                    if (string.IsNullOrEmpty(pair.Key)) continue;
                    if (cleaned.Count >= LogEntry.MaxData)
                    {
                        Add(problems, "log entry " + entry.type + " has more than " + LogEntry.MaxData.ToString(CultureInfo.InvariantCulture) + " numbers; the rest are dropped");
                        break;
                    }
                    cleaned[TextSafe.Clean(pair.Key, LogEntry.MaxKeyLength)] = TextSafe.Clean(pair.Value);
                }
            }
            entry.data = cleaned;
            return true;
        }

        /// <summary>Cleans a tender result. False (and a line in <paramref name="problems"/>) when the result cannot be kept.</summary>
        internal static bool Tender(TenderResultDto tender, List<string> problems)
        {
            if (tender == null)
            {
                Add(problems, "an empty tender result");
                return false;
            }

            tender.outcome = TextSafe.Clean(tender.outcome);
            if (!TenderOutcomeClassifier.IsKnownOutcome(tender.outcome))
            {
                Add(problems, "tender result with the unknown outcome '" + tender.outcome + "'");
                return false;
            }

            tender.date = TextSafe.Clean(tender.date);
            DateTime date;
            if (!LogDates.TryParse(tender.date, out date))
            {
                Add(problems, "tender result with the date '" + tender.date + "' which is not yyyy-MM-dd");
                return false;
            }

            tender.contractKey = TextSafe.Clean(tender.contractKey);
            tender.contractLabel = TextSafe.Clean(tender.contractLabel);
            tender.reasonKey = TextSafe.Clean(tender.reasonKey);
            tender.playerCpuName = TextSafe.Clean(tender.playerCpuName);
            tender.winnerCompany = TextSafe.Clean(tender.winnerCompany);
            tender.winnerCpuName = TextSafe.Clean(tender.winnerCpuName);
            if (tender.playerCpuId < 0) tender.playerCpuId = 0;
            if (tender.winnerCpuId < 0) tender.winnerCpuId = 0;
            tender.playerGameChance = Percent(tender.playerGameChance);
            tender.playerActual = Fraction(tender.playerActual);

            var offers = new List<OfferRowDto>();
            if (tender.offers != null)
            {
                foreach (OfferRowDto row in tender.offers)
                {
                    if (row == null) continue;
                    if (offers.Count >= TenderResultDto.MaxRows)
                    {
                        Add(problems, "tender result with more than " + TenderResultDto.MaxRows.ToString(CultureInfo.InvariantCulture) + " offers; the rest are dropped");
                        break;
                    }
                    row.company = TextSafe.Clean(row.company);
                    row.cpu = TextSafe.Clean(row.cpu);
                    row.ips = double.IsNaN(row.ips) || double.IsInfinity(row.ips) || row.ips < 0 ? 0 : row.ips;
                    row.chance = Percent(row.chance);
                    row.actual = Fraction(row.actual);
                    offers.Add(row);
                }
            }
            tender.offers = offers;

            var pruned = new List<PrunedRowDto>();
            if (tender.pruned != null)
            {
                foreach (PrunedRowDto row in tender.pruned)
                {
                    if (row == null) continue;
                    if (pruned.Count >= TenderResultDto.MaxRows)
                    {
                        Add(problems, "tender result with more than " + TenderResultDto.MaxRows.ToString(CultureInfo.InvariantCulture) + " withdrawn offers; the rest are dropped");
                        break;
                    }
                    row.company = TextSafe.Clean(row.company);
                    row.cpu = TextSafe.Clean(row.cpu);
                    row.maker = TextSafe.Clean(row.maker);
                    if (row.required < 0) row.required = 0;
                    if (row.contracted < 0) row.contracted = 0;
                    if (row.capacity < 0) row.capacity = 0;
                    pruned.Add(row);
                }
            }
            tender.pruned = pruned;
            return true;
        }

        private static int Percent(int value)
        {
            return value < 0 ? 0 : (value > 100 ? 100 : value);
        }

        private static double Fraction(double value)
        {
            if (double.IsNaN(value) || value < 0) return 0;
            return value > 1 ? 1 : value;
        }

        private static void Add(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }
    }
}
