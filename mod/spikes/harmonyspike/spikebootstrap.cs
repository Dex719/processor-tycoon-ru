// Harmony spike (spike 0002, TSK-006): entry point, host object and the fast-forward timer. Same identity as the real
// mod and as the load spike (assembly PTMod, PTMod.Core.ModBootstrap.Init), so tools/spike_install.py registers it with
// the entries the installer will use. Init applies the ten S1 patches (spikepatches.cs) and creates PTModHost, which
// logs the counters while the owner plays (spikecounters.cs). Nothing is written but Player.log lines and, in
// Application.persistentDataPath, the small file ptmod_spike_perf.csv.
//
// Two modes, chosen at start by a marker file in the folder of Player.log:
//   patched   (default)                                  - S1 patches applied, counters run, fast-forward is timed;
//   baseline  (file ptmod_spike_baseline.txt exists)     - NOTHING is patched, only the fast-forward is timed;
// the owner compares the two for the "fast-forward is not slower than +5 %" criterion (readme.txt, step 6).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using PTMod.Spikes.HarmonySpike;
using ProcessorTycoon.TimeSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    public static class ModBootstrap
    {
        /// <summary>String the installer script looks for in the DLL (tools/spike_install.py, MARKER); also the prefix of the Init line.</summary>
        internal const string InitMarker = "[PTMod] spike Init";

        internal const string BaselineMarkerFile = "ptmod_spike_baseline.txt";
        internal const string PerfHistoryFile = "ptmod_spike_perf.csv";

        private static int _calls;

        // The attribute documents the intent; the call itself comes from RuntimeInitializeOnLoads.json (see the load spike).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            try
            {
                _calls++;
                if (_calls > 1)
                {
                    // Not an "Init" line on purpose: a launch must produce exactly one "spike Init" line.
                    SpikeLog.Info("repeat: Init called again (call #" + _calls + "), ignored");
                    return;
                }

                SpikeLog.Raw(InitMarker + ": harmony spike, unity=" + Application.unityVersion + ", mono=" + Environment.Version);
                SpikeLog.Info("info: runtime=" + RuntimeName()
                    + ", assembly=" + typeof(ModBootstrap).Assembly.GetName().FullName
                    + ", frame=" + Time.frameCount
                    + ", sceneCount=" + SceneManager.sceneCount
                    + ", platform=" + Application.platform);

                var markerPath = BaselineMarkerPath();
                var baseline = File.Exists(markerPath);
                SpikeCounters.Mode = baseline ? "baseline" : "patched";
                SpikeLog.Info("S1 mode: " + SpikeCounters.Mode + " (baseline marker " + (baseline ? "FOUND" : "not present") + ": " + markerPath
                    + "; perf history: " + PerfHistoryPath() + ")");

                var harmonyLoaded = ProbeHarmony();
                if (!harmonyLoaded) SpikeLog.Info("S1 apply: skipped, Harmony is not loaded (see the line above; readme.txt, step 4)");
                else if (baseline) SpikeLog.Info("S1 apply: skipped on purpose, baseline run");
                else SpikePatcher.ApplyAll();

                PTModHost.Create();
            }
            catch (Exception e)
            {
                try { SpikeLog.Error("Init failed: " + e); }
                catch (Exception) { /* nothing left to do */ }
            }
        }

        internal static string BaselineMarkerPath()
        {
            return Path.Combine(Application.persistentDataPath, BaselineMarkerFile);
        }

        internal static string PerfHistoryPath()
        {
            return Path.Combine(Application.persistentDataPath, PerfHistoryFile);
        }

        /// <summary>
        /// Can Mono find 0Harmony.dll next to PTMod.dll by itself? Loaded by name through reflection, so Init runs even
        /// when it cannot. Line format as in the load spike ("harmony: loaded ..." / "harmony: NOT loaded (...)").
        /// </summary>
        private static bool ProbeHarmony()
        {
            try
            {
                var assembly = Assembly.Load("0Harmony");
                var type = assembly.GetType("HarmonyLib.Harmony", true);
                SpikeCounters.HarmonyVersion = assembly.GetName().Version.ToString();
                SpikeLog.Info("harmony: loaded " + SpikeCounters.HarmonyVersion + " from \"" + assembly.Location + "\", type " + type.FullName);
                return true;
            }
            catch (Exception e)
            {
                SpikeLog.Info("harmony: NOT loaded (" + e.GetType().Name + ": " + FirstLine(e.Message) + ")");
                return false;
            }
        }

        private static string RuntimeName()
        {
            try
            {
                var mono = Type.GetType("Mono.Runtime");
                var method = mono == null ? null : mono.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                var name = method == null ? null : method.Invoke(null, null) as string;
                return name ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd();
        }
    }

    /// <summary>
    /// Persistent host: scene log (first lines only), the once-a-second poll for progress lines, the final table at quit.
    /// Same shape as the real PTModHost (design.md), reduced to what the spike measures.
    /// </summary>
    public sealed class PTModHost : MonoBehaviour
    {
        private const int MaxSceneLines = 8;
        private const float ProgressEverySeconds = 60f;
        private const int MaxProgressLines = 20;

        private static PTModHost _instance;
        private static int _awakeCalls;

        private readonly DateWatch _watch = new DateWatch();
        private int _frames;
        private int _sceneLines;
        private int _scenesSeen;
        private float _nextPoll;
        private float _lastProgress;
        private long _lastProgressTotal;
        private int _progressLines;
        private bool _allCalledLogged;

        public static PTModHost Instance { get { return _instance; } }

        /// <summary>Creates the single host object. Called from ModBootstrap.Init, never throws.</summary>
        internal static void Create()
        {
            try
            {
                if (_instance != null)
                {
                    SpikeLog.Info("host: already exists, not created again");
                    return;
                }

                var go = new GameObject("PTModHost");
                DontDestroyOnLoad(go);
                var host = go.AddComponent<PTModHost>();     // Awake runs inside this call
                SpikeLog.Info("host: created=" + (host != null) + ", awakeCalls=" + _awakeCalls);
            }
            catch (Exception e)
            {
                SpikeLog.Error("host: creation failed: " + e);
            }
        }

        private void Awake()
        {
            try
            {
                _awakeCalls++;
                if (_instance != null && _instance != this)
                {
                    SpikeLog.Info("host: duplicate Awake, destroying the extra object");
                    Destroy(gameObject);
                    return;
                }

                _instance = this;
                SceneManager.sceneLoaded += OnSceneLoaded;
                SpikeLog.Info("host: Awake, frame=" + Time.frameCount + ", sceneCount=" + SceneManager.sceneCount);
            }
            catch (Exception e)
            {
                SpikeLog.Error("host: Awake failed: " + e);
            }
        }

        private void Update()
        {
            _frames++;
            try
            {
                if (_frames == 1) SpikeLog.Info("host: first Update, frame=" + Time.frameCount);

                var now = Time.realtimeSinceStartup;
                if (now < _nextPoll) return;
                _nextPoll = now + 1f;
                Poll(now);
            }
            catch (Exception e)
            {
                SpikeLog.Error("host: Update failed: " + e);
            }
        }

        // Once a second: the "all ten patches were called" milestone and a short progress line every minute while counts change.
        private void Poll(float now)
        {
            if (SpikeCounters.Mode != "patched") return;
            var counters = SpikeCounters.Set;

            if (!_allCalledLogged && counters.AllApplied() && counters.AllCalled())
            {
                _allCalledLogged = true;
                SpikeLog.Info("S1 milestone: all " + CounterSet.PatchCount + " patches were called at least once (liveDays=" + counters.LiveDays + ")");
                SpikeCounters.DumpTable("all patches called", false);
            }

            if (_progressLines < MaxProgressLines && now - _lastProgress >= ProgressEverySeconds)
            {
                var total = counters.TotalCalls();
                if (total != _lastProgressTotal)
                {
                    _lastProgress = now;
                    _lastProgressTotal = total;
                    _progressLines++;
                    SpikeLog.Info(counters.FormatCompact());
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                _scenesSeen++;
                if (++_sceneLines <= MaxSceneLines)
                {
                    SpikeLog.Info("scene loaded: buildIndex=" + scene.buildIndex + ", name=" + scene.name
                        + ", mode=" + mode + ", hostFrames=" + _frames);
                }
                // Scene 1 is the game; its DateController is new on every start of a game, so subscribe again each time.
                if (scene.buildIndex == 1) _watch.Attach();
            }
            catch (Exception e)
            {
                SpikeLog.Error("host: sceneLoaded failed: " + e);
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                SpikeLog.Info("host: quit, frames=" + _frames + ", sceneLoadsSeen=" + _scenesSeen);
                SpikeCounters.DumpTable("quit", true);
            }
            catch (Exception e)
            {
                SpikeLog.Error("host: quit failed: " + e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_instance == this)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    _instance = null;
                }
                // At application quit this is normal; any earlier one means DontDestroyOnLoad did not hold.
                SpikeLog.Info("host: destroyed, frames=" + _frames);
            }
            catch (Exception)
            {
                // The game may be shutting down; do not throw from OnDestroy.
            }
        }
    }

    /// <summary>
    /// Subscribes to the DateController of the game scene: counts live game days (the "30 days" of the acceptance
    /// criterion), times the new-game fast-forward and logs it with the 1975-1985 window. All handlers catch everything.
    /// </summary>
    internal sealed class DateWatch
    {
        private readonly List<KeyValuePair<int, long>> _marks = new List<KeyValuePair<int, long>>();
        private DateController _dates;
        private Stopwatch _clock;
        private bool _running;
        private DateTime _startDate;
        private int _startYear;

        public void Attach()
        {
            try
            {
                var dates = DateController.Instance;
                if (dates == null)
                {
                    SpikeLog.Info("S1 watch: DateController.Instance is null on scene load, nothing to watch");
                    return;
                }
                if (ReferenceEquals(dates, _dates)) return;     // the same object, already subscribed

                _dates = dates;
                _running = false;
                dates.OnDayPassed += OnDayPassed;
                dates.OnYearPassed += OnYearPassed;
                dates.FirstEverTick += OnFirstEverTick;
                dates.OnFastFowardEnded += OnFastForwardEnded;
                SpikeLog.Info("S1 watch: subscribed to DateController, date " + dates.CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 watch: subscribe failed: " + e);
            }
        }

        private void OnDayPassed()
        {
            try
            {
                var dates = _dates;
                if (dates == null || dates.IsFastFowardSimulating) return;
                var counters = SpikeCounters.Set;
                counters.LiveDays++;
                if (counters.LiveDays == 30)
                {
                    SpikeLog.Info("S1 milestone: 30 live game days have passed (date " + dates.CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")");
                    SpikeCounters.DumpTable("30 live days", false);
                }
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 watch: OnDayPassed failed: " + e);
            }
        }

        private void OnFirstEverTick()
        {
            try
            {
                var dates = _dates;
                _marks.Clear();
                _startDate = dates.CurrentDate;
                _startYear = dates.StartYear;       // DateController.SimulateUntilYear sets it before raising FirstEverTick
                _clock = Stopwatch.StartNew();
                _running = true;
                SpikeLog.Info("S1 perf: fast-forward starts at " + _startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + ", start year " + _startYear + ", mode " + SpikeCounters.Mode);
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 watch: FirstEverTick failed: " + e);
            }
        }

        private void OnYearPassed()
        {
            try
            {
                if (_running) _marks.Add(new KeyValuePair<int, long>(_dates.CurrentDate.Year, _clock.ElapsedTicks));
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 watch: OnYearPassed failed: " + e);
            }
        }

        private void OnFastForwardEnded()
        {
            try
            {
                SpikeCounters.Set.SnapshotLiveBase();       // "live" counts start here
                if (!_running) return;
                _running = false;
                _clock.Stop();

                var frequency = Stopwatch.Frequency;
                var totalMs = _clock.ElapsedTicks * 1000.0 / frequency;
                var days = (_dates.CurrentDate - _startDate).Days;
                var window = PerfMath.WindowMs(_marks, frequency, PerfMath.WindowFromYear, PerfMath.WindowToYear);
                var inv = CultureInfo.InvariantCulture;

                SpikeLog.Info("S1 perf: fast-forward done, start year " + _startYear + ", " + days + " game days, total " + totalMs.ToString("F0", inv) + " ms ("
                    + (days > 0 ? (totalMs / days).ToString("F3", inv) : "n/a") + " ms/day), window 1975-1985 "
                    + (window >= 0 ? window.ToString("F0", inv) + " ms" : "n/a (needs start year 1985 or later)") + ", mode " + SpikeCounters.Mode);
                SpikeLog.Info("S1 perf per-year ms: " + PerfMath.PerYear(_marks, frequency));

                var run = new PerfRun
                {
                    Mode = SpikeCounters.Mode,
                    StartYear = _startYear,
                    Days = days,
                    TotalMs = totalMs,
                    WindowMs = window,
                    Stamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", inv),
                };
                var path = ModBootstrap.PerfHistoryPath();
                PerfHistory.Append(path, run);
                SpikeLog.Info("S1 " + PerfMath.Compare(PerfHistory.Load(path), _startYear).Text);

                SpikeCounters.DumpTable("fast-forward ended", false);
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 watch: OnFastFowardEnded failed: " + e);
            }
        }
    }
}
