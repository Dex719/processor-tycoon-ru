using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Save;
using PTMod.Core.Logic.Settings;

namespace PTMod.Core
{
    /// <summary>
    /// The player's global settings (FR-6 items 1-4), kept in <c>Application.persistentDataPath/ptmod_settings.json</c>: the language of the
    /// mod, verbose logging, which features are switched on, the values of feature parameters, and (debug build only) the <c>debug</c> flags.
    /// A thin Unity adapter over <see cref="SettingsModel"/>, which does the reading, validation and atomic writing.
    /// <list type="bullet">
    /// <item><b>Start.</b> The file is read on first use (the bootstrap calls <see cref="Init"/> early); it is created with the defaults when
    /// missing and renamed to <c>*.corrupt-yyyyMMddHHmmss</c> when it is not valid JSON. Every problem found is one <c>ModLog.Warn</c>
    /// line with the JSON path (AC-6.1).</item>
    /// <item><b>Language</b> is the mod's own setting, <c>ru</c> by default; the game's language is never consulted (FR-8 item 7).</item>
    /// <item><b>Changes</b> from the hub are written at once (the file is tiny) and raise <see cref="Changed"/> after the write.
    /// Parameter values are clamped to the <see cref="ParamSpec"/>.</item>
    /// <item><b>Debug flags</b> (<see cref="Debug"/>) exist only in the debug build (<c>PTMOD_DEBUG</c>); in a release build every flag is false.</item>
    /// <item>Nothing here throws into the game. If the settings folder cannot be resolved (Unity refuses on a worker thread) the class
    /// behaves as if the file held the defaults and stores nothing.</item>
    /// </list>
    /// Main thread only, like the rest of the mod's state.
    /// </summary>
    public static class ModSettings
    {
        /// <summary>File name inside <c>persistentDataPath</c>.</summary>
        public const string FileName = "ptmod_settings.json";

        /// <summary>The <c>key</c> of <see cref="Changed"/> when a feature is switched on or off (the feature's parameters have their own keys).</summary>
        public const string EnabledKey = "enabled";

        private static readonly object Sync = new object();

        /// <summary>How often a start may fail (the folder cannot be resolved) before the class gives up and serves the defaults without trying again.</summary>
        private const int MaxStartFailures = 3;

        private static SettingsModel _model;
        private static bool _loading;
        private static int _failures;

        /// <summary>Where <see cref="IsFeatureEnabled(string)"/> gets the feature's own default (<c>IModFeature.EnabledByDefault</c>); set by the feature registry.</summary>
        internal static Func<string, bool> FeatureDefaultProvider { get; set; }

        /// <summary>Raised after a setting changed and was saved: (featureId, key) - the parameter key, <see cref="EnabledKey"/>, or ("mod-framework", "language" / "verboseLog").</summary>
        public static event Action<string, string> Changed;

        /// <summary>"ru" or "en"; <c>ru</c> by default and independent of the game's language. Setting it saves the file and switches <see cref="ModL10n"/>.</summary>
        public static string Language
        {
            get { return EnsureLoaded() ? _model.Language : SettingsModel.DefaultLanguage; }
            set
            {
                try
                {
                    if (!EnsureLoaded()) return;

                    if (_model.SetLanguage(value))
                    {
                        SaveIfDirty();
                        ModL10n.OnSettingsLanguageChanged(_model.Language);
                        RaiseChanged(ModLog.CoreFeatureId, "language");
                    }
                    else if (!IsSupportedLanguage(value))
                    {
                        ModLog.Warn(ModLog.CoreFeatureId, "settings: language '" + value + "' is not supported (\"ru\" or \"en\"), it stays '" + _model.Language + "'");
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "settings: cannot change the language", ex);
                }
            }
        }

        /// <summary>Whether <c>ModLog.Debug</c> writes. Off by default.</summary>
        public static bool VerboseLog
        {
            get { return EnsureLoaded() && _model.VerboseLog; }
            set
            {
                try
                {
                    if (!EnsureLoaded()) return;

                    if (_model.SetVerboseLog(value))
                    {
                        SaveIfDirty();
                        RaiseChanged(ModLog.CoreFeatureId, "verboseLog");
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "settings: cannot change verboseLog", ex);
                }
            }
        }

        // ---- features ----

        /// <summary>The player's choice for the feature, or its own default (<c>EnabledByDefault</c>) when the player has not chosen yet.</summary>
        public static bool IsFeatureEnabled(string featureId)
        {
            bool fallback = false;
            Func<string, bool> provider = FeatureDefaultProvider;
            if (provider != null)
            {
                try
                {
                    fallback = provider(featureId);
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "settings: the default of feature '" + featureId + "' cannot be determined", ex);
                }
            }
            return IsFeatureEnabled(featureId, fallback);
        }

        /// <summary>The player's choice for the feature, or <paramref name="defaultEnabled"/> when the file has none.</summary>
        public static bool IsFeatureEnabled(string featureId, bool defaultEnabled)
        {
            try
            {
                return EnsureLoaded() ? _model.IsFeatureEnabled(featureId, defaultEnabled) : defaultEnabled;
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "settings: cannot read the switch of feature '" + featureId + "'", ex);
                return defaultEnabled;
            }
        }

        /// <summary>The explicit choice of the player, or null when there is none (the hub offers "default" then).</summary>
        internal static bool? GetFeatureChoice(string featureId)
        {
            return EnsureLoaded() ? _model.GetFeatureEnabled(featureId) : null;
        }

        /// <summary>Records the player's choice and saves. Raises <see cref="Changed"/> with <see cref="EnabledKey"/> when the stored choice changed.</summary>
        public static void SetFeatureEnabled(string featureId, bool enabled)
        {
            try
            {
                if (!EnsureLoaded()) return;

                bool changed = _model.SetFeatureEnabled(featureId, enabled);
                SaveIfDirty();
                if (changed) RaiseChanged(featureId, EnabledKey);
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "settings: cannot save the switch of feature '" + featureId + "'", ex);
            }
        }

        // ---- parameters ----

        /// <summary>
        /// Registers a feature parameter and takes its value from the file (the default when the file has none or a bad one - with a warning
        /// naming the JSON path). A repeated identical declaration is harmless; a conflicting one is refused and the first stays.
        /// </summary>
        public static void Declare(ParamSpec spec)
        {
            try
            {
                if (!EnsureLoaded()) return;

                var problems = new List<string>();
                _model.Declare(spec, problems);
                string owner = spec == null ? ModLog.CoreFeatureId : spec.FeatureId;
                foreach (string problem in problems) ModLog.Warn(owner, "settings: " + problem);
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "settings: cannot declare a parameter", ex);
            }
        }

        public static bool GetBool(string featureId, string key)
        {
            return EnsureLoaded() && _model.GetBool(featureId, key);
        }

        public static int GetInt(string featureId, string key)
        {
            return EnsureLoaded() ? _model.GetInt(featureId, key) : 0;
        }

        public static float GetFloat(string featureId, string key)
        {
            return EnsureLoaded() ? _model.GetFloat(featureId, key) : 0f;
        }

        /// <summary>
        /// Sets a declared parameter, clamped by its <see cref="ParamSpec"/> (a Bool is non-zero = true, an Int is rounded, NaN is ignored),
        /// saves the file and raises <see cref="Changed"/> when the value changed. An undeclared parameter changes nothing.
        /// </summary>
        public static void Set(string featureId, string key, double value)
        {
            try
            {
                if (!EnsureLoaded()) return;

                double stored;
                bool changed = _model.Set(featureId, key, value, out stored);
                SaveIfDirty();
                if (changed) RaiseChanged(featureId, key);
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "settings: cannot change parameter '" + key + "'", ex);
            }
        }

        /// <summary>Copies of the declarations of one feature, in declaration order (the feature's page of the hub).</summary>
        internal static IReadOnlyList<ParamSpec> SpecsOf(string featureId)
        {
            return EnsureLoaded() ? _model.SpecsOf(featureId) : new ParamSpec[0];
        }

        /// <summary>A copy of the declaration, or null.</summary>
        internal static ParamSpec GetSpec(string featureId, string key)
        {
            return EnsureLoaded() ? _model.GetSpec(featureId, key) : null;
        }

        // ---- debug flags ----

        /// <summary>
        /// A flag of the <c>debug</c> section (<c>"debug.failInit"</c>, <c>"debug.brokenPatch"</c>, <c>"debug.throwOnTick"</c>, ...). Only the debug
        /// build (<c>PTMOD_DEBUG</c>) reads the section: in a release build the answer is always false.
        /// </summary>
        public static bool Debug(string flag)
        {
            // BuildInfo.IsDebug is a constant: the release build never reaches the file (the model checks it too).
            return BuildInfo.IsDebug && EnsureLoaded() && _model.Debug(flag);
        }

        // ---- start and stop ----

        /// <summary>
        /// Reads (or creates) the file. Idempotent; every public member calls it on first use, the bootstrap calls it explicitly so the
        /// problems of the file are logged at a known moment.
        /// </summary>
        internal static void Init()
        {
            if (_model != null) return;

            lock (Sync)
            {
                if (_model != null || _loading || _failures >= MaxStartFailures) return;   // _loading: the load itself logs, and the log asks for VerboseLog
                _loading = true;
                try
                {
                    string folder = RealFileSystem.PersistentDataPath;
                    if (folder == null)
                    {
                        _failures++;
                        ModLog.Error(ModLog.CoreFeatureId, "settings: Application.persistentDataPath is not available, the defaults are used and nothing is saved");
                        return;
                    }

                    var model = new SettingsModel(RealFileSystem.Instance, Path.Combine(folder, FileName), BuildInfo.IsDebug);
                    Report(model, model.Load());
                    _model = model;
                }
                catch (Exception ex)
                {
                    _failures++;
                    ModLog.Error(ModLog.CoreFeatureId, "settings: cannot start, the defaults are used and nothing is saved", ex);
                }
                finally
                {
                    _loading = false;
                }
            }
        }

        /// <summary>Writes the file if something is waiting. Changes are saved at once, so this matters only after a failed write (the host calls it on quit).</summary>
        internal static void Flush()
        {
            try
            {
                SaveIfDirty();
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "settings: cannot flush", ex);
            }
        }

        // ---- internals ----

        private static bool EnsureLoaded()
        {
            if (_model == null) Init();
            return _model != null;
        }

        private static void SaveIfDirty()
        {
            if (_model == null || !_model.IsDirty) return;

            FileWriteResult result = _model.Save();
            if (!result.Success) ModLog.Warn(ModLog.CoreFeatureId, "settings: not saved, the change lives until the game closes: " + result.Error);
        }

        private static void RaiseChanged(string featureId, string key)
        {
            Action<string, string> handlers = Changed;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string, string>)handler)(featureId, key);
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "settings: a Changed subscriber failed for " + featureId + "." + key, ex);
                }
            }
        }

        private static void Report(SettingsModel model, SettingsLoadReport report)
        {
            switch (report.Status)
            {
                case SettingsFileStatus.Created:
                    ModLog.Info(ModLog.CoreFeatureId, "settings: created " + model.FilePath);
                    break;

                case SettingsFileStatus.Corrupt:
                    ModLog.Warn(ModLog.CoreFeatureId, "settings: " + model.FilePath + " is unusable (" + report.Error + "); "
                                + (report.QuarantinedTo == null ? "it could not be set aside" : "it was moved to " + report.QuarantinedTo)
                                + ", the defaults are used and a new file is written");
                    break;
            }

            if (report.WriteResult != null && !report.WriteResult.Success)
                ModLog.Warn(ModLog.CoreFeatureId, "settings: the file could not be written: " + report.WriteResult.Error);

            foreach (string warning in report.Warnings) ModLog.Warn(ModLog.CoreFeatureId, "settings: " + warning);
        }

        private static bool IsSupportedLanguage(string language)
        {
            if (language == null) return false;
            string value = language.Trim().ToLowerInvariant();
            return value == "ru" || value == "en";
        }
    }
}
