using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using PTMod.Core.Logic.Json;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// The mod's entry point (FR-1, decision 0001). Unity calls <see cref="Init"/> before the first scene loads: the installer lists it in
    /// <c>RuntimeInitializeOnLoads.json</c> (<c>loadTypes: 1</c>, BeforeSceneLoad); the fallback is a call from <c>GameManager.Awake</c>
    /// inserted by the patcher, which reaches the same method. The start, in order:
    /// <list type="number">
    /// <item>the line <c>[PTMod] Init &lt;version&gt;</c> (AC-1.1), then the infrastructure: <c>ModSettings</c> (creates
    /// <c>ptmod_settings.json</c>), the <c>log.*</c> numbers of <c>core.json</c>, <c>ModProfile</c> (reads and recovers the profile),
    /// <c>ModL10n</c>; <c>core.json</c> itself is read and its channel list and prefix table are counted;</item>
    /// <item>one reflection pass over <c>PTMod.dll</c>: the features (<c>[ModFeature]</c>) and the start methods of the core services
    /// (<see cref="CoreInitAttribute"/>);</item>
    /// <item><see cref="CoreInitStage.Declare"/> - the 27 channels and the prefix table are declared here, before any feature exists (D21);</item>
    /// <item>the feature registry (<see cref="ModFeatures"/>: order, the player's choices), <see cref="CoreInitStage.Patch"/>,
    /// <c>Register</c> of every enabled feature, <see cref="CoreInitStage.AfterRegister"/>;</item>
    /// <item><see cref="PTModHost"/> (<c>DontDestroyOnLoad</c>), <c>OnEnabled</c> of the enabled features,
    /// <see cref="CoreInitStage.SelfCheckBoot"/>, <see cref="CoreInitStage.HostReady"/>, one summary line.</item>
    /// </list>
    /// <see cref="Init"/> runs once per process; a second call is ignored. Nothing escapes into the game: an exception anywhere in the start
    /// is one <c>[PTMod] [core] ERROR Init failed</c> record (AC-1.3), the features are switched off, the core's
    /// <see cref="CoreInitStage.Rollback"/> methods run, the host is removed, and the game goes on as vanilla.
    /// </summary>
    public static class ModBootstrap
    {
        /// <summary>Mod version. Single source: mod/VERSION, injected by build.py through BuildInfo.g.cs.</summary>
        public const string ModVersion = BuildInfo.Version;

        /// <summary>The feature id of the "Init failed" record; AC-1.3 names it <c>[core]</c>.</summary>
        internal const string InitFailedLogId = "core";

        private const string CoreId = ModLog.CoreFeatureId;

        private static readonly List<CoreInitEntry> Entries = new List<CoreInitEntry>();

        private static bool _started;
        private static string _step = "start";
        private static string _coreConfigSummary;

        /// <summary>True after a successful start.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>The start failed: the mod is off for this session (the hub's About tab and the self-check read it).</summary>
        internal static bool HasFailed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            // Idempotent (FR-1 item 3): the fallback loader may call it from both GameManager objects.
            if (_started)
            {
                ModLog.Debug(CoreId, "Init called again, ignored");
                return;
            }
            _started = true;

            try
            {
                Start();
            }
            catch (Exception ex)
            {
                try
                {
                    Fail(ex);
                }
                catch (Exception)
                {
                    // Fail guards every step itself; this only keeps a bug there from reaching Unity.
                }
            }
        }

        // ---- the start ----

        private static void Start()
        {
            Stopwatch clock = Stopwatch.StartNew();
            UnityEngine.Debug.Log("[PTMod] Init " + ModVersion + (BuildInfo.IsDebug ? " (debug build)" : ""));

            // The adapters log their own problems and never throw; the explicit calls fix the moment their problems appear in the log.
            _step = "infrastructure";
            ModSettings.Init();
            ModLog.ApplyCoreConfig();
            ModProfile.Init();
            ModL10n.Init();

#if PTMOD_DEBUG
            // AC-1.3: a start failure on demand. The flag exists only in the debug build (D27).
            _step = "debug.failInit";
            if (ModSettings.Debug("debug.failInit"))
                throw new InvalidOperationException("debug.failInit is set in " + ModSettings.FileName + " (simulated start failure)");
#endif

            _step = "core.json";
            ReadCoreConfig();

            _step = "discovery";
            List<Type> featureTypes = Scan(typeof(ModBootstrap).Assembly);

            RunStage(CoreInitStage.Declare);

            _step = "feature registry";
            ModFeatures.Build(featureTypes);

            RunStage(CoreInitStage.Patch);

            _step = "feature registration";
            ModFeatures.RegisterActive();

            RunStage(CoreInitStage.AfterRegister);

            _step = "host";
            if (PTModHost.Create() == null) throw new InvalidOperationException("PTModHost could not be created");

            _step = "feature activation";
            ModFeatures.OpenActivation();

            RunStage(CoreInitStage.SelfCheckBoot);
            RunStage(CoreInitStage.HostReady);

            _step = "done";
            IsInitialized = true;
            ModLog.Info(CoreId, "started in " + clock.ElapsedMilliseconds + " ms; " + _coreConfigSummary + "; " + ModFeatures.Summary());
        }

        /// <summary>
        /// Everything that ran is undone as far as possible: the features are switched off (their patches go with them), the core services
        /// roll back, the host goes away. Each step is guarded: the failure is reported once and nothing reaches the game.
        /// </summary>
        private static void Fail(Exception ex)
        {
            HasFailed = true;
            IsInitialized = false;
            ModLog.Error(InitFailedLogId, "Init failed (" + _step + ")", ex);

            try
            {
                ModFeatures.Halt("the mod failed to start (" + _step + ")");
            }
            catch (Exception haltError)
            {
                ModLog.Error(CoreId, "Init failed: the features could not be switched off", haltError);
            }

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                CoreInitEntry entry = Entries[i];
                if (entry.Stage != CoreInitStage.Rollback) continue;
                try
                {
                    entry.Run();
                }
                catch (Exception rollbackError)
                {
                    ModLog.Error(CoreId, "Init failed: rollback " + entry.Name + " failed", rollbackError);
                }
            }

            try
            {
                PTModHost.Shutdown();
            }
            catch (Exception hostError)
            {
                ModLog.Error(CoreId, "Init failed: the host could not be removed", hostError);
            }
        }

        /// <summary>
        /// Loads <c>core.json</c> (built-in plus the player's override; <c>ModContent</c> keeps it for the services that read it in the Declare
        /// stage) and counts what the registries will get. Missing parts are a warning: the services fall back to their code defaults.
        /// </summary>
        private static void ReadCoreConfig()
        {
            JsonObject core = ModContent.LoadConfig(ModContent.CoreFeatureId, ModContent.CoreFile, ModContent.CoreSchema);
            JsonArray channels = core.GetArray("channels");
            JsonObject prefixes = core.GetObject("prefixes");
            int channelCount = channels == null ? 0 : channels.Count;
            int prefixRows = prefixes == null ? 0 : prefixes.Count;

            _coreConfigSummary = ModContent.CoreFile + ": " + channelCount + " channels, " + prefixRows + " prefix rows";
            if (channelCount == 0 || prefixRows == 0)
                ModLog.Warn(CoreId, _coreConfigSummary + "; the channel registry or the id prefix table starts empty");
        }

        // ---- reflection pass ----

        /// <summary>
        /// One pass over the types of the mod (the start budget is one second): returns the <c>[ModFeature]</c> classes and fills
        /// <see cref="Entries"/> with the <see cref="CoreInitAttribute"/> methods. Types that cannot be loaded (a missing 0Harmony.dll makes
        /// the types that use it unloadable) are skipped with one warning; their services' start methods are then not found.
        /// </summary>
        private static List<Type> Scan(Assembly assembly)
        {
            Type[] loaded;
            try
            {
                loaded = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                loaded = ex.Types ?? new Type[0];
                int missing = 0;
                foreach (Type type in loaded) if (type == null) missing++;
                Exception first = null;
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception loaderError in ex.LoaderExceptions)
                    {
                        if (loaderError == null) continue;
                        first = loaderError;
                        break;
                    }
                }
                ModLog.Warn(CoreId, "discovery: " + missing + " type(s) of PTMod.dll could not be loaded and are skipped"
                                    + (first == null ? "" : " (" + first.GetType().Name + ": " + first.Message + ")"));
            }

            var types = new List<Type>(loaded.Length);
            foreach (Type type in loaded) if (type != null) types.Add(type);
            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            var features = new List<Type>();
            var entries = new List<CoreInitEntry>();
            foreach (Type type in types)
            {
                try
                {
                    if (type.IsInterface || type.IsEnum) continue;

                    if (type.IsDefined(typeof(ModFeatureAttribute), false)) features.Add(type);
                    else if (!type.IsAbstract && typeof(IModFeature).IsAssignableFrom(type))
                        ModLog.Warn(ModFeatures.FeatureOfType(type) ?? CoreId, "discovery: " + type.FullName + " implements IModFeature but has no [ModFeature]; it is not registered");

                    // Lambdas and iterators cannot carry the attribute; open generic types cannot be called.
                    if (type.Name.IndexOf('<') >= 0 || type.ContainsGenericParameters) continue;

                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!method.IsDefined(typeof(CoreInitAttribute), false)) continue;
                        CoreInitEntry entry = MakeEntry(type, method);
                        if (entry != null) entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn(CoreId, "discovery: type " + type.FullName + " is skipped: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            entries.Sort(CoreInitEntry.Compare);
            Entries.Clear();
            Entries.AddRange(entries);
            ModLog.Debug(CoreId, "discovery: " + types.Count + " types, " + features.Count + " feature class(es), " + entries.Count + " core init method(s)");
            return features;
        }

        private static CoreInitEntry MakeEntry(Type type, MethodInfo method)
        {
            string name = type.Name + "." + method.Name;
            var attribute = (CoreInitAttribute)Attribute.GetCustomAttribute(method, typeof(CoreInitAttribute), false);

            if (ModFeatures.FeatureOfType(type) != CoreId)
            {
                ModLog.Warn(ModFeatures.FeatureOfType(type) ?? CoreId, "discovery: [CoreInit] on " + name + " is ignored: only the core (PTMod.Core) uses it, features start in Register");
                return null;
            }
            if (!Enum.IsDefined(typeof(CoreInitStage), attribute.Stage))
            {
                ModLog.Warn(CoreId, "discovery: [CoreInit] on " + name + " has an unknown stage " + (int)attribute.Stage + " and is ignored");
                return null;
            }
            if (method.GetParameters().Length != 0 || method.ContainsGenericParameters || method.ReturnType != typeof(void))
            {
                ModLog.Warn(CoreId, "discovery: [CoreInit] on " + name + " is ignored: the method must be static void and take no parameters");
                return null;
            }

            var run = (Action)Delegate.CreateDelegate(typeof(Action), method, false);
            if (run == null)
            {
                ModLog.Warn(CoreId, "discovery: [CoreInit] on " + name + " is ignored: it cannot be called as Action");
                return null;
            }
            return new CoreInitEntry(attribute.Stage, attribute.Order, attribute.Optional, name, type.FullName + "." + method.Name, run);
        }

        // ---- stages ----

        /// <summary>
        /// Calls the methods of one stage in order. A required method that throws stops the start (the exception goes to <see cref="Fail"/>
        /// unchanged, with its stack); an optional one is logged and skipped.
        /// </summary>
        private static void RunStage(CoreInitStage stage)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int count = 0;
            foreach (CoreInitEntry entry in Entries)
            {
                if (entry.Stage != stage) continue;

                count++;
                _step = stage + " " + entry.Name;
                try
                {
                    entry.Run();
                }
                catch (Exception ex)
                {
                    if (!entry.Optional) throw;
                    ModLog.Error(CoreId, "core init " + entry.Name + " (" + stage + ") failed; the start goes on without it", ex);
                }
            }

            if (count > 0) ModLog.Debug(CoreId, "stage " + stage + ": " + count + " method(s), " + watch.ElapsedMilliseconds + " ms");
        }

        /// <summary>One <see cref="CoreInitAttribute"/> method.</summary>
        private sealed class CoreInitEntry
        {
            public CoreInitEntry(CoreInitStage stage, int order, bool optional, string name, string sortKey, Action run)
            {
                Stage = stage;
                Order = order;
                Optional = optional;
                Name = name;
                SortKey = sortKey;
                Run = run;
            }

            public CoreInitStage Stage { get; }

            public int Order { get; }

            public bool Optional { get; }

            /// <summary><c>Type.Method</c> for the log.</summary>
            public string Name { get; }

            public string SortKey { get; }

            public Action Run { get; }

            public static int Compare(CoreInitEntry a, CoreInitEntry b)
            {
                int byStage = ((int)a.Stage).CompareTo((int)b.Stage);
                if (byStage != 0) return byStage;
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.SortKey, b.SortKey);
            }
        }
    }
}
