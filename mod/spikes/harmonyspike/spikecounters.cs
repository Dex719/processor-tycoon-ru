// Harmony spike (spike 0002, TSK-006): counters, log helper and performance history. Nothing here touches Harmony or
// the game, so the arithmetic the owner relies on (the S1 verdict, the baseline-vs-patched comparison) can be checked
// outside the game. Built together with spikepatches.cs and spikebootstrap.cs as PTMod.dll (assembly name PTMod, as in
// the real mod, so the JSON registration is the real one):
//   python mod/build.py --game-dir "<game dir>" --src mod/spikes/harmonyspike --out spikes/harmonyspike/PTMod.dll
// Installed and removed by tools/spike_install.py (--dll mod/build/spikes/harmonyspike/PTMod.dll); the procedure for
// the owner is in readme.txt. Every log line of the spike starts with "[PTMod] spike ", lines of the S1 table with
// "[PTMod] spike S1 ".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace PTMod.Spikes.HarmonySpike
{
    /// <summary>The ten S1 patches in summary order (design.md, table of patches, row S1).</summary>
    internal enum S1
    {
        GetModifier = 0,        // P1  Modifiers.GetModifier, postfix
        UpdatePopularities,     // P5  Market.UpdatePopularities (private), prefix
        CalculateRatings,       // P4  Market.CalculateRatings (private nested type in the signature), postfix
        ProduceWafers,          // P2  Factory.ProduceWafers, prefix + postfix + finalizer with a __state struct
        TickBankruptcies,       // P10 BankruptcyHandler.TickBankruptcies (private), transpiler
        DefineChance,           // P8  Contract.CpuOffer.DefineChance (private nested type), postfix
        GetBestOffers,          // P23 Contract.GetBestOffers (private), prefix + finalizer
        NextMonth,              // P21 DateController.NextMonth, prefix + finalizer
        CompanyAddCpu,          // P20 Company.AddCpu (virtual), postfix
        AiCompanyAddCpu,        // P20 AICompany.AddCpu (virtual), postfix
    }

    /// <summary>Static description of one S1 row: what is patched and what the extra counters mean.</summary>
    internal sealed class PatchRow
    {
        public readonly S1 Id;
        public readonly string Short;       // key in the compact progress line
        public readonly string Design;      // P-number of the design.md table
        public readonly string Target;
        public readonly string Kinds;
        public readonly string[] Extras;    // labels of the extra counters; a label ending in "Mask" prints as hex

        public PatchRow(S1 id, string shortName, string design, string target, string kinds, params string[] extras)
        {
            Id = id; Short = shortName; Design = design; Target = target; Kinds = kinds; Extras = extras;
        }
    }

    /// <summary>
    /// Thread-safe counters of the ten S1 patches plus the formatting of the table and the verdict. An instance
    /// (not statics) so that a test can build one from synthetic data.
    /// </summary>
    internal sealed class CounterSet
    {
        public const int PatchCount = 10;
        public const int ExtraSlots = 4;

        /// <summary>Calls the transpiler of P10 must count in the loop of TickBankruptcies (acceptance of TSK-006).</summary>
        public const int ExpectedLdloc0InLoop = 3;

        public static readonly PatchRow[] Rows =
        {
            new PatchRow(S1.GetModifier, "P1", "P1", "Modifiers.GetModifier", "postfix",
                "nullCompany", "typesMask", "oddResult"),
            new PatchRow(S1.UpdatePopularities, "P5", "P5", "Market.UpdatePopularities", "prefix",
                "cpuSum", "cpuMax", "noField"),
            new PatchRow(S1.CalculateRatings, "P4", "P4", "Market.CalculateRatings", "postfix",
                "segmentMask", "nullInstance"),
            new PatchRow(S1.ProduceWafers, "P2", "P2", "Factory.ProduceWafers", "prefix+postfix+finalizer",
                "postfix", "finalizer", "producedDelta", "stateLost"),
            new PatchRow(S1.TickBankruptcies, "P10", "P10", "BankruptcyHandler.TickBankruptcies", "transpiler",
                "ldloc0", "ldloc0InLoop", "stloc0", "instructions"),
            new PatchRow(S1.DefineChance, "P8", "P8", "Contract.CpuOffer.DefineChance", "postfix",
                "inScope", "outOfScope"),
            new PatchRow(S1.GetBestOffers, "P23", "P23", "Contract.GetBestOffers", "prefix+finalizer",
                "finalizer", "maxDepth", "withException"),
            new PatchRow(S1.NextMonth, "P21", "P21", "DateController.NextMonth", "prefix+finalizer",
                "finalizer", "days", "ms"),
            new PatchRow(S1.CompanyAddCpu, "P20", "P20", "Company.AddCpu", "postfix",
                "liveRelease", "loading", "fastForward", "distinctCpus"),
            new PatchRow(S1.AiCompanyAddCpu, "P20ai", "P20", "AICompany.AddCpu", "postfix",
                "liveRelease", "loading", "fastForward", "distinctCpus"),
        };

        private readonly long[] _calls = new long[PatchCount];
        private readonly long[] _liveBase = new long[PatchCount];
        private readonly long[] _extra = new long[PatchCount * ExtraSlots];
        private readonly int[] _faults = new int[PatchCount];
        private readonly bool[] _applied = new bool[PatchCount];
        private readonly string[] _error = new string[PatchCount];

        /// <summary>Game days that passed outside fast-forward (set by the date watcher).</summary>
        public int LiveDays;

        public void Hit(S1 id)
        {
            Interlocked.Increment(ref _calls[(int)id]);
        }

        public void Add(S1 id, int slot, long n)
        {
            Interlocked.Add(ref _extra[(int)id * ExtraSlots + slot], n);
        }

        /// <summary>Sets bits in an extra counter (the hot paths run on the main thread; a lost bit would only show up on the next call).</summary>
        public void SetBits(S1 id, int slot, long bits)
        {
            _extra[(int)id * ExtraSlots + slot] |= bits;
        }

        public void Max(S1 id, int slot, long value)
        {
            var index = (int)id * ExtraSlots + slot;
            if (value > _extra[index]) _extra[index] = value;
        }

        public void SetExtra(S1 id, int slot, long value)
        {
            _extra[(int)id * ExtraSlots + slot] = value;
        }

        public long Calls(S1 id) { return Interlocked.Read(ref _calls[(int)id]); }

        public long Extra(S1 id, int slot) { return Interlocked.Read(ref _extra[(int)id * ExtraSlots + slot]); }

        /// <summary>Calls since live play began (see SnapshotLiveBase).</summary>
        public long Live(S1 id) { return Calls(id) - Interlocked.Read(ref _liveBase[(int)id]); }

        public int Faults(S1 id) { return Volatile.Read(ref _faults[(int)id]); }

        public int NoteFault(S1 id) { return Interlocked.Increment(ref _faults[(int)id]); }

        public bool Applied(S1 id) { return _applied[(int)id]; }

        public string Error(S1 id) { return _error[(int)id]; }

        public void MarkApplied(S1 id, bool ok, string error)
        {
            _applied[(int)id] = ok;
            _error[(int)id] = ok ? null : (error ?? "unknown error");
        }

        /// <summary>Remembers the current call counts as the start of "live" play (the end of the new-game fast-forward).</summary>
        public void SnapshotLiveBase()
        {
            for (var i = 0; i < PatchCount; i++) Interlocked.Exchange(ref _liveBase[i], Interlocked.Read(ref _calls[i]));
        }

        public bool AllApplied()
        {
            for (var i = 0; i < PatchCount; i++) if (!_applied[i]) return false;
            return true;
        }

        public bool AllCalled()
        {
            for (var i = 0; i < PatchCount; i++) if (Calls((S1)i) == 0) return false;
            return true;
        }

        public long TotalCalls()
        {
            long sum = 0;
            for (var i = 0; i < PatchCount; i++) sum += Calls((S1)i);
            return sum;
        }

        // ---- formatting ------------------------------------------------------------------------------------------

        public string FormatRow(PatchRow row, int number)
        {
            var id = row.Id;
            var sb = new StringBuilder(160);
            sb.Append(number.ToString("00", CultureInfo.InvariantCulture)).Append('/').Append(PatchCount).Append(' ');
            sb.Append(Pad(row.Design, 3)).Append(' ').Append(Pad(row.Target, 35)).Append(' ').Append(Pad(row.Kinds, 24));
            sb.Append(" applied=").Append(Applied(id) ? "yes" : "NO");
            sb.Append(" calls=").Append(Calls(id).ToString(CultureInfo.InvariantCulture));
            sb.Append(" live=").Append(Live(id).ToString(CultureInfo.InvariantCulture));
            for (var slot = 0; slot < row.Extras.Length; slot++)
            {
                var label = row.Extras[slot];
                var value = Extra(id, slot);
                sb.Append(' ').Append(label).Append('=');
                if (label.EndsWith("Mask", StringComparison.Ordinal)) sb.Append("0x").Append(value.ToString("X", CultureInfo.InvariantCulture));
                else sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(" faults=").Append(Faults(id).ToString(CultureInfo.InvariantCulture));
            if (!Applied(id) && _error[(int)id] != null) sb.Append(" error=\"").Append(_error[(int)id]).Append('"');
            return sb.ToString();
        }

        /// <summary>The whole table: header, one line per patch, verdict. Each string is one log line without the "[PTMod] spike " tag.</summary>
        public List<string> FormatTable(string tag, string reason, string mode, string harmonyVersion)
        {
            var lines = new List<string>(PatchCount + 3);
            lines.Add(tag + " summary (reason=" + reason + ", mode=" + mode + ", liveDays=" + LiveDays.ToString(CultureInfo.InvariantCulture)
                + ", harmony=" + harmonyVersion + "):");
            for (var i = 0; i < Rows.Length; i++) lines.Add(tag + " " + FormatRow(Rows[i], i + 1));
            lines.Add(tag + " result: " + Evaluate().Text);
            return lines;
        }

        /// <summary>One short line for periodic progress: "S1 progress: liveDays=12 P1=100 P5=30 ...".</summary>
        public string FormatCompact()
        {
            var sb = new StringBuilder(160);
            sb.Append("S1 progress: liveDays=").Append(LiveDays.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < Rows.Length; i++)
                sb.Append(' ').Append(Rows[i].Short).Append('=').Append(Calls(Rows[i].Id).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Pad(string text, int width)
        {
            return text.Length >= width ? text : text + new string(' ', width - text.Length);
        }

        // ---- verdict ---------------------------------------------------------------------------------------------

        /// <summary>
        /// PASS: every patch applied and called, no faults, the transpiler counted ExpectedLdloc0InLoop loads of local 0 in
        /// the loop. FAIL: something was not applied, faulted or counted wrongly. INCOMPLETE: nothing wrong yet, but some
        /// patch has not been called (play on: see readme.txt for what triggers each patch).
        /// </summary>
        public Verdict Evaluate()
        {
            var failures = new List<string>();
            var pending = new List<string>();
            for (var i = 0; i < Rows.Length; i++)
            {
                var row = Rows[i];
                var id = row.Id;
                var name = row.Design + (row.Id == S1.AiCompanyAddCpu ? "ai" : "") + " " + row.Target;
                if (!Applied(id)) failures.Add(name + " not applied");
                else if (Calls(id) == 0) pending.Add(name);
                if (Faults(id) > 0) failures.Add(name + " faults=" + Faults(id).ToString(CultureInfo.InvariantCulture));
            }

            if (Extra(S1.ProduceWafers, 3) > 0) failures.Add("P2 __state lost between prefix and postfix/finalizer " + Extra(S1.ProduceWafers, 3).ToString(CultureInfo.InvariantCulture) + " times");

            var inLoop = Extra(S1.TickBankruptcies, 1);
            var total = Extra(S1.TickBankruptcies, 0);
            string transpiler;
            if (!Applied(S1.TickBankruptcies)) transpiler = "transpiler not applied";
            else if (inLoop == ExpectedLdloc0InLoop) transpiler = "ldloc.0 in loop = " + inLoop.ToString(CultureInfo.InvariantCulture) + " (total " + total.ToString(CultureInfo.InvariantCulture) + ", expected " + ExpectedLdloc0InLoop + ")";
            else
            {
                transpiler = "ldloc.0 in loop = " + inLoop.ToString(CultureInfo.InvariantCulture) + " (total " + total.ToString(CultureInfo.InvariantCulture) + ", expected " + ExpectedLdloc0InLoop + ")";
                failures.Add("P10 " + transpiler);
            }

            var applied = 0;
            var called = 0;
            for (var i = 0; i < PatchCount; i++)
            {
                if (_applied[i]) applied++;
                if (Calls((S1)i) > 0) called++;
            }

            var counts = "applied " + applied + "/" + PatchCount + ", called " + called + "/" + PatchCount + ", " + transpiler;
            var verdict = new Verdict();
            if (failures.Count > 0)
            {
                verdict.Result = VerdictResult.Fail;
                verdict.Text = "FAIL (" + counts + "; problems: " + string.Join("; ", failures.ToArray()) + ")";
            }
            else if (pending.Count > 0)
            {
                verdict.Result = VerdictResult.Incomplete;
                verdict.Text = "INCOMPLETE (" + counts + "; not called yet: " + string.Join("; ", pending.ToArray()) + ")";
            }
            else
            {
                verdict.Result = VerdictResult.Pass;
                verdict.Text = "PASS (" + counts + ", faults 0)";
            }
            return verdict;
        }
    }

    internal enum VerdictResult { Pass, Incomplete, Fail }

    internal sealed class Verdict
    {
        public VerdictResult Result;
        public string Text;
    }

    /// <summary>Process-wide state of the spike: the counter set the patches write to, the mode, the log helper.</summary>
    internal static class SpikeCounters
    {
        public const string Tag = "[PTMod] spike ";

        public static readonly CounterSet Set = new CounterSet();

        /// <summary>"patched" (S1 patches applied) or "baseline" (marker file present: nothing patched, only the timer runs).</summary>
        public static string Mode = "patched";

        public static string HarmonyVersion = "not loaded";

        /// <summary>Counts a failure inside a patch body and logs the first three with the full exception. Never throws.</summary>
        public static void Fault(S1 id, Exception e)
        {
            try
            {
                var count = Set.NoteFault(id);
                if (count <= 3)
                    SpikeLog.Error("S1 fault #" + count + " in " + CounterSet.Rows[(int)id].Target + ": " + e);
            }
            catch (Exception)
            {
                // The log itself failed; nothing left to do and nothing may reach the game.
            }
        }

        public static void DumpTable(string reason, bool final)
        {
            try
            {
                var tag = final ? "S1 FINAL" : "S1 table";
                if (Mode != "patched")
                {
                    SpikeLog.Info(tag + " summary (reason=" + reason + "): mode=" + Mode + ", no S1 patch was applied on purpose");
                    return;
                }
                foreach (var line in Set.FormatTable(tag, reason, Mode, HarmonyVersion)) SpikeLog.Info(line);
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 table failed: " + e);
            }
        }
    }

    /// <summary>The one place that writes to Player.log; the sink can be replaced (tests, offline checks).</summary>
    internal static class SpikeLog
    {
        /// <summary>Replaces Unity's Debug.Log / Debug.LogError. Arguments: isError, text without the tag.</summary>
        public static Action<bool, string> Sink { get; set; }

        public static void Info(string text) { Write(false, text, true); }

        public static void Error(string text) { Write(true, text, true); }

        /// <summary>A line that already carries its own "[PTMod] spike ..." prefix (the Init line the installer script looks for).</summary>
        public static void Raw(string line) { Write(false, line, false); }

        private static void Write(bool isError, string text, bool addTag)
        {
            try
            {
                var sink = Sink;
                if (sink != null) sink(isError, text);
                else UnityWrite(isError, addTag ? SpikeCounters.Tag + text : text);
            }
            catch (Exception)
            {
                // Logging must never break the game.
            }
        }

        // Separate method: the call to UnityEngine is only compiled when the Unity sink is really used.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UnityWrite(bool isError, string line)
        {
            if (isError) UnityEngine.Debug.LogError(line);
            else UnityEngine.Debug.Log(line);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Performance of the new-game fast-forward: one run per start, kept in a small history file so that a patched
    // run is compared with baseline runs without hand arithmetic.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>One measured fast-forward (the game simulates 1971 up to the chosen start year in a single call).</summary>
    internal sealed class PerfRun
    {
        public string Mode = "patched";     // "patched" or "baseline"
        public int StartYear;
        public int Days;
        public double TotalMs;
        public double WindowMs = -1;        // 1975-01-01 .. 1985-01-01 inside the run; -1 when the run did not cover it
        public string Stamp = "";

        public string ToLine()
        {
            var inv = CultureInfo.InvariantCulture;
            return Mode + ";" + StartYear.ToString(inv) + ";" + Days.ToString(inv) + ";" + TotalMs.ToString("F1", inv)
                + ";" + WindowMs.ToString("F1", inv) + ";" + Stamp;
        }

        public static bool TryParse(string line, out PerfRun run)
        {
            run = null;
            if (string.IsNullOrEmpty(line)) return false;
            var parts = line.Trim().Split(';');
            if (parts.Length < 5) return false;
            var inv = CultureInfo.InvariantCulture;
            var result = new PerfRun();
            result.Mode = parts[0];
            if (result.Mode != "patched" && result.Mode != "baseline") return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, inv, out result.StartYear)) return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, inv, out result.Days)) return false;
            if (!double.TryParse(parts[3], NumberStyles.Float, inv, out result.TotalMs)) return false;
            if (!double.TryParse(parts[4], NumberStyles.Float, inv, out result.WindowMs)) return false;
            result.Stamp = parts.Length > 5 ? parts[5] : "";
            run = result;
            return true;
        }
    }

    internal sealed class PerfCompare
    {
        /// <summary>OK, OVER_LIMIT, PROVISIONAL (fewer than the wanted runs), NO_BASELINE or NO_PATCHED.</summary>
        public string Status;
        public double Percent = double.NaN;
        public string Text;
    }

    internal static class PerfMath
    {
        public const double LimitPercent = 5.0;
        public const int WantedRuns = 3;
        public const int WindowFromYear = 1975;
        public const int WindowToYear = 1985;

        public static double Median(IList<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            var sorted = new List<double>(values);
            sorted.Sort();
            var n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        /// <summary>
        /// Milliseconds between the first mark of fromYear and the first mark of toYear; -1 if either is missing.
        /// A mark is (year, Stopwatch ticks) taken when the game raises OnYearPassed (Jan 1 of that year).
        /// </summary>
        public static double WindowMs(IList<KeyValuePair<int, long>> marks, long frequency, int fromYear, int toYear)
        {
            if (marks == null || frequency <= 0) return -1;
            long from = -1, to = -1;
            for (var i = 0; i < marks.Count; i++)
            {
                if (marks[i].Key == fromYear && from < 0) from = marks[i].Value;
                if (marks[i].Key == toYear && to < 0) to = marks[i].Value;
            }
            if (from < 0 || to < 0 || to < from) return -1;
            return (to - from) * 1000.0 / frequency;
        }

        /// <summary>"1972:950 1973:1010 ..." milliseconds the game needed for each simulated year (from the second mark on).</summary>
        public static string PerYear(IList<KeyValuePair<int, long>> marks, long frequency)
        {
            var sb = new StringBuilder();
            if (marks == null || frequency <= 0) return "";
            for (var i = 1; i < marks.Count; i++)
            {
                var ms = (marks[i].Value - marks[i - 1].Value) * 1000.0 / frequency;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(marks[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':').Append(ms.ToString("F0", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Compares the median of the last (up to five) patched runs with the median of the last baseline runs of the same
        /// start year. The metric is the 1975-1985 window when every selected run has it, otherwise the whole fast-forward.
        /// </summary>
        public static PerfCompare Compare(IList<PerfRun> runs, int startYear)
        {
            var inv = CultureInfo.InvariantCulture;
            var baseline = new List<PerfRun>();
            var patched = new List<PerfRun>();
            if (runs != null)
            {
                foreach (var run in runs)
                {
                    if (run.StartYear != startYear) continue;
                    if (run.Mode == "baseline") baseline.Add(run);
                    else if (run.Mode == "patched") patched.Add(run);
                }
            }
            Trim(baseline);
            Trim(patched);

            var result = new PerfCompare();
            var head = "perf compare (start year " + startYear.ToString(inv) + "): ";
            if (baseline.Count == 0)
            {
                result.Status = "NO_BASELINE";
                result.Text = head + "no baseline run yet; create the marker file ptmod_spike_baseline.txt (readme.txt, step 6) and start a new game with the same start year";
                return result;
            }
            if (patched.Count == 0)
            {
                result.Status = "NO_PATCHED";
                result.Text = head + "no patched run yet (" + baseline.Count + " baseline run(s) recorded); remove the marker file and start a new game with the same start year";
                return result;
            }

            var useWindow = AllHaveWindow(baseline) && AllHaveWindow(patched);
            var b = Metric(baseline, useWindow);
            var p = Metric(patched, useWindow);
            var mb = Median(b);
            var mp = Median(p);
            var percent = mb > 0 ? (mp / mb - 1.0) * 100.0 : double.NaN;
            result.Percent = percent;
            var enough = baseline.Count >= WantedRuns && patched.Count >= WantedRuns;
            if (double.IsNaN(percent)) result.Status = "NO_BASELINE";
            else if (!enough) result.Status = "PROVISIONAL";
            else result.Status = percent <= LimitPercent ? "OK" : "OVER_LIMIT";

            result.Text = head + "metric=" + (useWindow ? "window 1975-1985" : "whole fast-forward")
                + ", baseline median " + mb.ToString("F0", inv) + " ms (n=" + baseline.Count + "), patched median "
                + mp.ToString("F0", inv) + " ms (n=" + patched.Count + "): "
                + (double.IsNaN(percent) ? "n/a" : (percent >= 0 ? "+" : "") + percent.ToString("F1", inv) + " %")
                + " (limit +" + LimitPercent.ToString("F0", inv) + " %) -> " + result.Status
                + (enough ? "" : "; need " + WantedRuns + " runs of each kind for a final answer");
            return result;
        }

        private static void Trim(List<PerfRun> runs)
        {
            while (runs.Count > 5) runs.RemoveAt(0);
        }

        private static bool AllHaveWindow(List<PerfRun> runs)
        {
            foreach (var run in runs) if (run.WindowMs < 0) return false;
            return true;
        }

        private static List<double> Metric(List<PerfRun> runs, bool useWindow)
        {
            var values = new List<double>(runs.Count);
            foreach (var run in runs) values.Add(useWindow ? run.WindowMs : run.TotalMs);
            return values;
        }
    }

    /// <summary>The history file (one PerfRun per line) in the folder of Player.log. Never throws.</summary>
    internal static class PerfHistory
    {
        public static List<PerfRun> Load(string path)
        {
            var runs = new List<PerfRun>();
            try
            {
                if (!File.Exists(path)) return runs;
                foreach (var line in File.ReadAllLines(path))
                {
                    PerfRun run;
                    if (PerfRun.TryParse(line, out run)) runs.Add(run);
                }
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 perf history could not be read from " + path + ": " + e.Message);
            }
            return runs;
        }

        public static void Append(string path, PerfRun run)
        {
            try
            {
                File.AppendAllText(path, run.ToLine() + Environment.NewLine);
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 perf history could not be written to " + path + ": " + e.Message);
            }
        }
    }
}
