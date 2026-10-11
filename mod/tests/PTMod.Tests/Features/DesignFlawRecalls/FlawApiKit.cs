using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// Helpers for the tests of the public API, the journal, the badge, the risk hint, the bias and the press link of design-flaw-recalls (TSK-017..021, TSK-026):
    /// reading the sources and the string tables, and a world for the pure <see cref="FlawApiEngine"/>. Internal, so the runner never discovers it.
    /// </summary>
    internal static class FlawApiKit
    {
        public static string FeatureFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls");
        }

        /// <summary>The code of a file of the feature folder without comments (strings kept), line ends as "\n".</summary>
        public static string Code(string relative)
        {
            string path = Path.GetFullPath(Path.Combine(FeatureFolder(), relative));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false).Replace("\r\n", "\n");
        }

        /// <summary>The text of a file of the feature folder as it is (comments included), line ends as "\n".</summary>
        public static string Raw(string relative)
        {
            string path = Path.GetFullPath(Path.Combine(FeatureFolder(), relative));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.ReadText(path).Replace("\r\n", "\n");
        }

        /// <summary>The code of every file of the feature folder, with the path relative to the folder (comments removed, strings kept).</summary>
        public static Dictionary<string, string> AllCode()
        {
            var all = new Dictionary<string, string>();
            string root = FeatureFolder();
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                string relative = path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                all[relative] = SourceScan.Strip(SourceScan.ReadText(path), false).Replace("\r\n", "\n");
            }
            return all;
        }

        public static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>The text from the first <paramref name="from"/> up to the next <paramref name="to"/> after it (the method body, as a rule).</summary>
        public static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "not found after '" + from + "': " + to);
            return text.Substring(start, end - start);
        }

        /// <summary>The text of a string table of a feature (<c>L10n/&lt;lang&gt;/&lt;feature&gt;.json</c>) as a map from key to text.</summary>
        public static Dictionary<string, string> Strings(string language, string feature)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, feature + ".json");
            Assert.True(File.Exists(path), "no such table: " + path);

            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in FlawTestKit.ParseObject(SourceScan.ReadText(path)))
            {
                var value = pair.Value as JsonValue;
                Assert.NotNull(value, "the text of " + pair.Key + " is a string");
                table[pair.Key] = value.StringValue;
            }
            return table;
        }

        public static Dictionary<string, string> Own(string language)
        {
            return Strings(language, "design-flaw-recalls");
        }

        /// <summary>The literals <c>"dfr.…"</c> of a code text that name a key of the string tables (a literal that ends with a dot is a prefix and is left out).</summary>
        public static List<string> KeyLiterals(string code)
        {
            var keys = new List<string>();
            foreach (Match match in Regex.Matches(code, "\"(dfr(?:\\.[A-Za-z0-9_]+)+)\""))
            {
                if (!keys.Contains(match.Groups[1].Value)) keys.Add(match.Groups[1].Value);
            }
            return keys;
        }

        // ---- a world for the engine of the API ----

        /// <summary>The numbers of the design of AC-1.1 (p = 0.233 on difficulty 2), a CPU of 1998.</summary>
        public static FlawRiskInput Design(int year = 1998)
        {
            return new FlawRiskInput
            {
                FailureRate = 0.30f,
                Frequency = 125f,
                MaxFrequency = 100f,
                Temperature = 84f,
                SafeTemperature = 80f,
                HasNodeRecord = true,
                NodeProgress = 0.4f,
                Year = year,
                Difficulty = 2,
            };
        }
    }

    /// <summary>The host of the engine of the API: the CPUs of the game, the settings and the clock are set by the test.</summary>
    internal sealed class FakeApiHost : IFlawApiHost
    {
        public int Day;
        public FlawBalance Balance_ = FlawBalance.Defaults();
        public IModRng Rng_ = new FakeRng(0.0f);
        public float FlawFrequency_ = 1f;
        public bool AiFlaws = true;
        public float Inflation_ = 1.5f;
        public bool Locked;
        public readonly Dictionary<int, FlawCpuInfo> Cpus = new Dictionary<int, FlawCpuInfo>();
        public readonly HashSet<string> DeadOwners = new HashSet<string>();
        public readonly List<string> Lines = new List<string>();

        public int Today { get { return Day; } }
        public FlawBalance Balance { get { return Balance_; } }
        public IModRng Rng { get { return Rng_; } }
        public float FlawFrequency { get { return FlawFrequency_; } }
        public bool AiFlawsEnabled { get { return AiFlaws; } }
        public float Inflation { get { return Inflation_; } }
        public bool SaveLocked { get { return Locked; } }

        public FlawCpuInfo FindCpu(int cpuSaveId)
        {
            FlawCpuInfo info;
            return Cpus.TryGetValue(cpuSaveId, out info) ? info : null;
        }

        public bool CpuExists(int cpuSaveId)
        {
            return Cpus.ContainsKey(cpuSaveId);
        }

        public bool OwnerIsAlive(string companyKey)
        {
            return !DeadOwners.Contains(companyKey);
        }

        public void Log(string line)
        {
            Lines.Add(line);
        }
    }

    /// <summary>The store, the cycle with a scripted host, the engine and its host, wired together.</summary>
    internal sealed class ApiWorld
    {
        public readonly FlawSaveData Data = new FlawSaveData();
        public readonly FlawStore Store;
        public readonly FakeCycleHost CycleHost;
        public readonly FlawCycle Cycle;
        public readonly FakeApiHost Host;
        public readonly FlawApiEngine Engine;

        public ApiWorld()
        {
            int day = FlawCycleKit.MonthStart(14);   // 1991-03-01
            Store = new FlawStore(() => Data);
            CycleHost = new FakeCycleHost { Day = day };
            Cycle = new FlawCycle(Store, CycleHost);
            Host = new FakeApiHost { Day = day };
            Engine = new FlawApiEngine(Store, Cycle, Host);
        }

        public int Day
        {
            get { return Host.Day; }
        }

        /// <summary>A released CPU of a company, known to the game (and to the facts of the cycle, so the quote can be made).</summary>
        public FlawCpuInfo AddCpu(int id, string owner = "player", int baseId = -1, bool released = true, bool retired = false, params int[] variants)
        {
            int baseSaveId = baseId < 0 ? id : baseId;
            var info = new FlawCpuInfo
            {
                SaveId = id,
                BaseSaveId = baseSaveId,
                IsVariant = baseSaveId != id,
                OwnerKey = owner,
                IsReleased = released,
                IsRetired = retired,
                ReleaseDay = Day - 400,
                Risk = FlawApiKit.Design(),
                HasRisk = true,
                VariantSaveIds = new List<int>(variants),
            };
            Host.Cpus[id] = info;
            if (!CycleHost.Facts.ContainsKey(baseSaveId)) CycleHost.Facts[baseSaveId] = FakeCycleHost.Selling(5000);
            return info;
        }

        /// <summary>A record already in the store, in the given state.</summary>
        public FlawRecord AddRecord(FlawState state, int id, int cpu, string company = "player", FlawSeverity severity = FlawSeverity.Major,
            FlawResponse response = FlawResponse.None, int publicSince = 0)
        {
            FlawRecord record = FlawCycleKit.Record(state, id, cpu, company, severity, response);
            record.Data.publicSinceDay = publicSince;
            Data.records.Add(record.Data);
            Data.nextRecordId = Math.Max(Data.nextRecordId, id + 1);
            if (!CycleHost.Facts.ContainsKey(cpu)) CycleHost.Facts[cpu] = FakeCycleHost.Selling(5000);
            return record;
        }
    }
}
