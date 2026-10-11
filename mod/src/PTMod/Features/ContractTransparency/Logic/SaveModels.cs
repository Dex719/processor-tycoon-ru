using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    // The data of the section "contract-transparency" of the side save file (design.md, "DTO состояния"). Plain classes with public fields: ModJson.Bind turns the
    // section into them and ModJson.From turns them back. There is no field for a version: the core writes and reads "_schema" itself. Numbers are int and double
    // (never float: a float written as a double prints twenty digits). Every string is at most TextSafe.MaxLength characters after Sanitize; the sizes of the
    // lists are held by the model, which also drops what is not usable.

    /// <summary>The whole section: the log, the tender results and the two counters of FR-14.</summary>
    [Serializable]
    public sealed class ContractTransparencySave
    {
        /// <summary>The entries of the log, the oldest first.</summary>
        public List<LogEntry> log = new List<LogEntry>();

        /// <summary>The results of the tenders the player took part in, the oldest first.</summary>
        public List<TenderResultDto> tenders = new List<TenderResultDto>();

        /// <summary>Offers the player has sent in this game (FR-14, the predicate <c>ct.offersSent</c>).</summary>
        public int offersSent;

        /// <summary>Tenders the player has won in this game (FR-14, the predicate <c>ct.tendersWon</c>).</summary>
        public int tendersWon;
    }

    /// <summary>One line of the log (FR-8). The window builds its text from <see cref="reasonKey"/> and <see cref="data"/>.</summary>
    [Serializable]
    public sealed class LogEntry
    {
        /// <summary>The longest key of <see cref="data"/>.</summary>
        public const int MaxKeyLength = 32;

        /// <summary>The most numbers one entry keeps.</summary>
        public const int MaxData = 16;

        /// <summary>The game date of the entry, <c>yyyy-MM-dd</c>.</summary>
        public string date;

        /// <summary>One of <see cref="LogTypes"/>.</summary>
        public string type;

        /// <summary>One of <see cref="LogSources"/>.</summary>
        public string source;

        /// <summary>The key of the contract (<see cref="ContractKeyBuilder"/>); empty when the entry is about no contract.</summary>
        public string contractKey;

        /// <summary>The contract for people: "Apple - Computer, 52 000 units/month".</summary>
        public string contractLabel;

        /// <summary><c>Cpu.SaveID</c>, 0 when the entry names no CPU.</summary>
        public int cpuId;

        /// <summary>The name of the CPU as it was then.</summary>
        public string cpuName;

        /// <summary>The key in <c>ModL10n</c> of the reason or the sentence of the entry, for example <c>ct.reason.margin</c>.</summary>
        public string reasonKey;

        /// <summary>The numbers behind the entry, by <see cref="LogDataKeys"/>; the values are text so a number keeps the digits it was written with.</summary>
        public Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>A new entry of this kind.</summary>
        public static LogEntry Create(string date, string type, string source)
        {
            return new LogEntry { date = date ?? string.Empty, type = type ?? string.Empty, source = source ?? string.Empty };
        }

        /// <summary>Adds (or replaces) a number as text; the key and the value are cut to the limits. Returns the entry for chaining.</summary>
        public LogEntry Put(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            if (data == null) data = new Dictionary<string, string>(StringComparer.Ordinal);

            string cleanKey = TextSafe.Clean(key, MaxKeyLength);
            if (data.Count >= MaxData && !data.ContainsKey(cleanKey)) return this;
            data[cleanKey] = TextSafe.Clean(value);
            return this;
        }

        /// <summary>Adds a whole number.</summary>
        public LogEntry Put(string key, int value)
        {
            return Put(key, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Adds a number with up to four decimals (<c>0.35</c>, <c>194.4</c>); a value that is not a finite number is written as 0.</summary>
        public LogEntry Put(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return Put(key, value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        /// <summary>The number stored under the key, or null.</summary>
        public string Get(string key)
        {
            string value;
            return data != null && key != null && data.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>The stored number as an integer; <paramref name="fallback"/> when it is missing or not a whole number.</summary>
        public int GetInt(string key, int fallback)
        {
            int value;
            return int.TryParse(Get(key), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        /// <summary>The stored number as a double; <paramref name="fallback"/> when it is missing or not a number.</summary>
        public double GetDouble(string key, double fallback)
        {
            double value;
            return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value)
                ? value
                : fallback;
        }
    }

    /// <summary>The result of one tender the player took part in (FR-5, FR-7).</summary>
    [Serializable]
    public sealed class TenderResultDto
    {
        /// <summary>The most rows of offers and of withdrawn offers a result keeps (a contract has a dozen offers at most).</summary>
        public const int MaxRows = 32;

        public string contractKey;
        public string contractLabel;

        /// <summary>The day of the draw, <c>yyyy-MM-dd</c>.</summary>
        public string date;

        /// <summary><c>Won</c>, <c>Lost</c> or <c>Void</c> (<see cref="TenderOutcomeClassifier"/>).</summary>
        public string outcome;

        /// <summary>The reason, a key of <c>ModL10n</c>: <c>ct.tender.won</c>, <c>ct.tender.outbid</c>, <c>ct.tender.autoReject</c>, <c>ct.tender.shadowed</c>, <c>ct.tender.retired</c>, <c>ct.tender.noOffers</c>.</summary>
        public string reasonKey;

        /// <summary>The CPU that played for the player (the best of the player's offers that reached the draw), or the strongest one that was withdrawn.</summary>
        public int playerCpuId;
        public string playerCpuName;

        /// <summary>The chance the game gave that CPU, in percent (0 when it did not reach the draw).</summary>
        public int playerGameChance;

        /// <summary>The probability the draw picks it, 0..1 (<see cref="OddsMath"/>).</summary>
        public double playerActual;

        /// <summary>The CPU the draw picked (0 and empty names for nobody).</summary>
        public int winnerCpuId;
        public string winnerCompany;
        public string winnerCpuName;

        /// <summary>True when the player's automatic cancellation of offers was off and the offer needed more lines than were free: the game did not check, fines are possible (FR-7 item 2).</summary>
        public bool capacityNotChecked;

        /// <summary>The offers that reached the draw, in the order of the draw.</summary>
        public List<OfferRowDto> offers = new List<OfferRowDto>();

        /// <summary>The offers the game withdrew for lack of lines, the player's and the competitors' (FR-7 item 3).</summary>
        public List<PrunedRowDto> pruned = new List<PrunedRowDto>();
    }

    /// <summary>One offer in the draw of a tender.</summary>
    [Serializable]
    public sealed class OfferRowDto
    {
        public string company;
        public string cpu;
        public double ips;

        /// <summary>The chance the game gave, percent.</summary>
        public int chance;

        /// <summary>The probability the draw picks this offer, 0..1.</summary>
        public double actual;

        /// <summary>The offer is the player's.</summary>
        public bool player;
    }

    /// <summary>One offer withdrawn before the draw because its producer had no free lines.</summary>
    [Serializable]
    public sealed class PrunedRowDto
    {
        public string company;
        public string cpu;

        /// <summary>The lines the contract needs.</summary>
        public int required;

        /// <summary>The lines the producer has already given to active contracts.</summary>
        public int contracted;

        /// <summary>The lines the producer's factory has.</summary>
        public int capacity;

        /// <summary>The factory that would build the CPU when it is not the company's own (a foundry); empty for the company's own factory.</summary>
        public string maker;

        /// <summary>The withdrawn offer is the player's.</summary>
        public bool player;
    }
}
