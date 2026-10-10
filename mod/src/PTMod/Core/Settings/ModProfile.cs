using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Core.Logic.Settings;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// The global, save-independent data of the features (achievements, "seen the tutorial", best results; FR-6 items 5-9), kept in
    /// <c>Application.persistentDataPath/ptmod_profile.json</c> in one section per feature. The only writer of that file (D22): features ask for
    /// their section with <see cref="Section"/> and never open the file. A thin Unity adapter over <see cref="ProfileModel"/>, which does the
    /// reading, the recovery and the atomic writing.
    /// <list type="bullet">
    /// <item><b>Sections.</b> <see cref="Section"/> returns the live object of the feature; after changing it the feature calls
    /// <see cref="MarkDirty"/>. The core writes and reads <c>_schema</c>; sections of unknown or switched-off features and sections with a newer
    /// <c>_schema</c> are written back unchanged (AC-6.4).</item>
    /// <item><b>Writing.</b> Throttled: not earlier than <c>profile.saveThrottleSeconds</c> (2 s, from <c>core.json</c>) after the first change
    /// and at most once per window; the host's <see cref="Pump()"/> does the timed write. Every write is atomic: <c>.tmp</c>, then
    /// <c>File.Replace</c> with the previous file kept as <c>ptmod_profile.json.bak</c>. <see cref="Save"/> writes at once; the host calls it on
    /// <c>OnApplicationQuit</c>, <c>OnApplicationPause</c> and <c>SceneLeaving</c>, so the throttle never loses a change (<see cref="Flush"/>
    /// does the same but only when something is unsaved).</item>
    /// <item><b>Recovery.</b> A broken file is renamed to <c>*.corrupt-yyyyMMddHHmmss</c> and the <c>.bak</c> is used
    /// (<see cref="ProfileStatus.RestoredFromBackup"/>); if that is broken too the profile starts empty (<see cref="ProfileStatus.Corrupt"/>).
    /// Both cases raise one <see cref="Notice"/> for the player (key <see cref="BackupNoticeKey"/> or <see cref="ResetNoticeKey"/>).</item>
    /// <item>The profile does not depend on any save: loading a game or starting a new one leaves it alone. Nothing here throws into the game.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static class ModProfile
    {
        /// <summary>File name inside <c>persistentDataPath</c>; the previous version is <c>ptmod_profile.json.bak</c>.</summary>
        public const string FileName = "ptmod_profile.json";

        /// <summary>Notice key: the profile was damaged and restored from the backup; the argument is the backup file name.</summary>
        public const string BackupNoticeKey = "core.profile.corrupt";

        /// <summary>
        /// Notice key: the profile and its backup were both unusable, an empty profile is in use; the argument is the file name the damaged
        /// profile was moved to (or a dash when it could not be moved). Needs its own text: "restored from the copy" would be untrue here.
        /// </summary>
        public const string ResetNoticeKey = "core.profile.reset";

        private static readonly object Sync = new object();
        private static readonly List<PendingNotice> PendingNotices = new List<PendingNotice>();
        private static readonly System.Diagnostics.Stopwatch FallbackClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>How often a start may fail (the folder cannot be resolved) before the class gives up and serves an empty detached profile.</summary>
        private const int MaxStartFailures = 3;

        private static ProfileModel _model;
        private static bool _loading;
        private static int _failures;
        private static bool _loadedRaised;
        private static Action _loaded;
        private static Action<string, object[]> _notice;

        private struct PendingNotice
        {
            public string Key;
            public object[] Args;
        }

        /// <summary>How the profile was obtained at start. <see cref="ProfileStatus.New"/> when it has not been loaded.</summary>
        public static ProfileStatus Status
        {
            get { return EnsureLoaded() ? _model.Status : ProfileStatus.New; }
        }

        /// <summary>
        /// The profile was read (mod start). Sticky: a subscriber that arrives after the start is called at once, because the profile is
        /// loaded before any feature registers and a plain event would never reach it.
        /// </summary>
        public static event Action Loaded
        {
            add
            {
                if (value == null) return;
                bool already;
                lock (Sync)
                {
                    _loaded += value;
                    already = _loadedRaised;
                }
                if (already) InvokeSafely(value, "Loaded");
            }
            remove
            {
                lock (Sync) _loaded -= value;
            }
        }

        /// <summary>
        /// The player has to be told something about the profile: (L10n key, format arguments), see <see cref="BackupNoticeKey"/> and
        /// <see cref="ResetNoticeKey"/>. <c>ModNotify</c> subscribes later than the profile is read, so a notice raised while nobody listens
        /// is kept and handed to the first subscriber when it arrives.
        /// </summary>
        public static event Action<string, object[]> Notice
        {
            add
            {
                if (value == null) return;
                List<PendingNotice> replay = null;
                lock (Sync)
                {
                    bool first = _notice == null;
                    _notice += value;
                    if (first && PendingNotices.Count > 0)
                    {
                        replay = new List<PendingNotice>(PendingNotices);
                        PendingNotices.Clear();
                    }
                }
                if (replay == null) return;

                foreach (PendingNotice pending in replay)
                {
                    try
                    {
                        value(pending.Key, pending.Args);
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error(ModLog.CoreFeatureId, "profile: a Notice subscriber failed", ex);
                    }
                }
            }
            remove
            {
                lock (Sync) _notice -= value;
            }
        }

        /// <summary>
        /// The live object of the feature's section (created empty on the first call, the same object afterwards). It does not contain
        /// <c>_schema</c>: the core writes <paramref name="schemaVersion"/> there. A stored section newer than the requested version comes back
        /// empty and is never overwritten. If the profile cannot be used at all the object is detached (changes are not stored).
        /// </summary>
        public static JsonObject Section(string featureId, int schemaVersion = 1)
        {
            try
            {
                if (EnsureLoaded()) return _model.Section(featureId, schemaVersion);
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "profile: cannot open the section", ex);
            }
            return new JsonObject();
        }

        /// <summary>The feature changed its section. Writes not earlier than the throttle window from now (2 s) and not more often than once per window.</summary>
        public static void MarkDirty(string featureId)
        {
            try
            {
                if (EnsureLoaded()) _model.MarkDirty(featureId, Now());
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "profile: cannot mark the section as changed", ex);
            }
        }

        /// <summary>Writes the profile now, atomically, keeping the previous file as <c>.bak</c>.</summary>
        public static void Save()
        {
            try
            {
                if (EnsureLoaded()) Report(_model.Save(Now()));
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "profile: cannot save", ex);
            }
        }

        // ---- host and bootstrap ----

        /// <summary>
        /// Reads the profile (and recovers it if needed). Idempotent; every public member calls it on first use, the bootstrap calls it once
        /// so that the recovery is logged at a known moment. Raises <see cref="Loaded"/>, and <see cref="Notice"/> after a recovery.
        /// </summary>
        internal static void Init()
        {
            if (_model != null) return;

            var notices = new List<PendingNotice>();
            bool loaded = false;
            lock (Sync)
            {
                if (_model != null || _loading || _failures >= MaxStartFailures) return;
                _loading = true;
                try
                {
                    string folder = RealFileSystem.PersistentDataPath;
                    if (folder == null)
                    {
                        _failures++;
                        ModLog.Error(ModLog.CoreFeatureId, "profile: Application.persistentDataPath is not available, the profile stays empty and nothing is saved");
                        return;
                    }

                    double throttle = ModContent.GetCoreNumber("profile", "saveThrottleSeconds", WriteThrottle.DefaultWindowSeconds);
                    var model = new ProfileModel(RealFileSystem.Instance, Path.Combine(folder, FileName), null, throttle, WarnFromModel);
                    ProfileLoadReport report = model.Load(Now());
                    _model = model;
                    Describe(model, report, notices);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    _failures++;
                    ModLog.Error(ModLog.CoreFeatureId, "profile: cannot start, the profile stays empty and nothing is saved", ex);
                }
                finally
                {
                    _loading = false;
                }
            }

            // Subscribers run outside the lock.
            foreach (PendingNotice notice in notices) Raise(notice.Key, notice.Args);
            if (loaded) RaiseLoaded();
        }

        /// <summary>
        /// The timed write: call every frame (<c>PTModHost.Update</c>); it writes only when the window of a marked change has passed.
        /// It never loads the profile, so a frame before the start costs nothing. The profile keeps its own real-time clock, the argument of the
        /// overloads is ignored (a host may pass its frame time).
        /// </summary>
        internal static void Pump()
        {
            ProfileModel model = _model;
            if (model == null) return;

            try
            {
                Report(model.Tick(Now()));
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "profile: the timed write failed", ex);
            }
        }

        internal static void Pump(float ignoredTime)
        {
            Pump();
        }

        internal static void Pump(double ignoredTime)
        {
            Pump();
        }

        /// <summary>Writes only if something is unsaved; for events that fire often (pause, scene change) where a rewrite of an unchanged file would only churn the <c>.bak</c>.</summary>
        internal static void Flush()
        {
            ProfileModel model = _model;
            if (model == null) return;

            try
            {
                Report(model.FlushIfDirty(Now()));
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "profile: the forced write failed", ex);
            }
        }

        // ---- internals ----

        private static bool EnsureLoaded()
        {
            if (_model == null) Init();
            return _model != null;
        }

        /// <summary>A write that did not happen is logged (the model keeps the data dirty and tries again one window later).</summary>
        private static void Report(FileWriteResult result)
        {
            if (result != null && !result.Success)
                ModLog.Warn(ModLog.CoreFeatureId, "profile: not saved, the data stay in memory and the write is retried: " + result.Error);
        }

        /// <summary>Logs how the profile came up and collects the notices for the player (raised by the caller, outside the lock).</summary>
        private static void Describe(ProfileModel model, ProfileLoadReport report, List<PendingNotice> notices)
        {
            string backupName = Path.GetFileName(model.BackupPath);
            switch (report.Status)
            {
                case ProfileStatus.New:
                    ModLog.Info(ModLog.CoreFeatureId, "profile: no file yet, an empty profile is used (" + model.FilePath + ")");
                    break;

                case ProfileStatus.RestoredFromBackup:
                    ModLog.Warn(ModLog.CoreFeatureId, "profile: " + model.FilePath + " is unusable (" + report.Error + "); "
                                + (report.QuarantinedTo == null ? "it could not be set aside" : "it was moved to " + report.QuarantinedTo)
                                + ", the data were restored from " + backupName);
                    notices.Add(new PendingNotice { Key = BackupNoticeKey, Args = new object[] { backupName } });
                    break;

                case ProfileStatus.Corrupt:
                    ModLog.Error(ModLog.CoreFeatureId, "profile: " + model.FilePath + " is unusable (" + report.Error + ") and the backup is no help; "
                                 + (report.QuarantinedTo == null ? "the file could not be set aside" : "the file was moved to " + report.QuarantinedTo)
                                 + ", an empty profile is used");
                    notices.Add(new PendingNotice
                    {
                        Key = ResetNoticeKey,
                        Args = new object[] { report.QuarantinedTo == null ? "-" : Path.GetFileName(report.QuarantinedTo) },
                    });
                    break;
            }

            foreach (string warning in report.Warnings) ModLog.Warn(ModLog.CoreFeatureId, "profile: " + warning);
        }

        /// <summary>Delivers a notice to the subscribers, or keeps it for the first one (see <see cref="Notice"/>).</summary>
        private static void Raise(string key, object[] args)
        {
            Action<string, object[]> handlers;
            lock (Sync)
            {
                handlers = _notice;
                if (handlers == null)
                {
                    PendingNotices.Add(new PendingNotice { Key = key, Args = args });
                    return;
                }
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string, object[]>)handler)(key, args);
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "profile: a Notice subscriber failed", ex);
                }
            }
        }

        private static void RaiseLoaded()
        {
            Action handlers;
            lock (Sync)
            {
                _loadedRaised = true;
                handlers = _loaded;
            }
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList()) InvokeSafely((Action)handler, "Loaded");
        }

        private static void InvokeSafely(Action handler, string eventName)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "profile: a " + eventName + " subscriber failed", ex);
            }
        }

        private static void WarnFromModel(string message)
        {
            ModLog.Warn(ModLog.CoreFeatureId, message);
        }

        /// <summary>Real time in seconds since the game started (the clock of the write throttle); a stopwatch when Unity refuses (a worker thread).</summary>
        private static double Now()
        {
            try
            {
                return Time.realtimeSinceStartupAsDouble;
            }
            catch (Exception)
            {
                return FallbackClock.Elapsed.TotalSeconds;
            }
        }
    }
}
