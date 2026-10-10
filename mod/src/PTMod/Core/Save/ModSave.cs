using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using GameSaves = ProcessorTycoon.Save.SaveHandler;

namespace PTMod.Core
{
    /// <summary>
    /// A holder of one section of the side save file (FR-5): what a feature registers with <c>FeatureContext.SaveParticipant</c>. The core calls it
    /// when a save is loaded or written and when the game is reset; the feature never opens the file or the game's save.
    /// <list type="bullet">
    /// <item><see cref="ISideFileParticipant.SectionId"/> is the feature's id (a participant of a feature with another id is refused).</item>
    /// <item><see cref="ISideFileParticipant.SchemaVersion"/> is the version of the section. The core writes it as <c>_schema</c> and reads it back, the
    /// feature keeps no version field of its own (D20); a section without <c>_schema</c> is read as version 1, and a section newer than
    /// <c>SchemaVersion</c> is not read at all: the feature gets <see cref="ISideFileParticipant.ResetToDefaults"/> and the stored section is written
    /// back unchanged.</item>
    /// <item><see cref="ISideFileParticipant.ResetToDefaults"/>: a new game, leaving to the menu, a save without the section, a newer or unreadable section.</item>
    /// <item><see cref="ISideFileParticipant.Read"/>: the stored section without <c>_schema</c>, on <c>SaveHandler.OnLoaded</c>, before <c>GameReady</c>;
    /// <c>schema</c> below <c>SchemaVersion</c> means "migrate inside". Must not modify the object.</item>
    /// <item><see cref="ISideFileParticipant.Write"/>: the data of the feature without <c>_schema</c> (a returned <c>_schema</c> is overwritten with a
    /// warning), after every successful write of the game's own save. A fresh object each time; null leaves the section out.</item>
    /// </list>
    /// An exception from any of these is caught, counted against the feature (FR-2) and logged; a section whose <c>Read</c> or <c>Write</c> failed keeps
    /// its previous text in the file. The members are declared on <see cref="ISideFileParticipant"/>, the logic-side interface the unit tests use.
    /// </summary>
    public interface IModSaveParticipant : ISideFileParticipant
    {
    }

    /// <summary>
    /// The side save file of the mod (FR-5, decision 0004): <c>&lt;name&gt;.ptmod.json</c> beside the game's <c>&lt;name&gt;.txt</c> in
    /// <c>Saves/</c>. The game's save is never touched. The format, the sections and their versions are the pure model
    /// <see cref="SideFileModel"/>, the disk is <see cref="SideFileIo"/>; this class is the game side of them.
    /// <list type="bullet">
    /// <item><b>Write (P12).</b> After <c>SaveSystem.Save</c> wrote the <c>.txt</c> (a manual save, the autosave, an empty name that became
    /// <c>Y_M_D</c>) the file is written atomically with the section of every active participant; sections of features that are off or unknown are
    /// copied as they were. A failed write never touches the <c>.txt</c>: it is a log record and the notice <c>core.save.writeFailed</c>.</item>
    /// <item><b>Read (P13 + OnLoaded).</b> <c>SaveHandler.Load</c> tells the name (<see cref="PrepareLoad"/>); the file is read when the game raises
    /// <c>SaveHandler.OnLoaded</c>, before <see cref="ModTicks.GameReady"/>. No file: defaults for everybody (an old save); a broken file is set aside as
    /// <c>*.corrupt-&lt;time&gt;</c> with defaults and the notice <c>core.save.corrupt</c>; a newer file or section keeps its data and gives the feature
    /// defaults (<c>core.save.newer</c>). <see cref="AfterRead"/> follows.</item>
    /// <item><b>Delete (P14).</b> Deleting a save in the game's window deletes its side file.</item>
    /// <item><b>New game and menu.</b> <see cref="PrepareNewGame"/> (called by <c>ModNewGame</c>'s P17 before the fast-forward) resets every participant, so
    /// the new game starts from defaults and a new world seed; arriving at the main menu does the same (nothing of the last game is left behind).</item>
    /// <item><b>The section of the core</b> (<c>mod-framework</c>) is <see cref="CoreSection"/>, built from blocks: the start date, <see cref="Flags"/>,
    /// <see cref="ModRandom"/>, and the blocks of the other core services.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    public static class ModSave
    {
        /// <summary>Format version of the file itself (the top-level <c>schema</c>).</summary>
        public const int FileSchema = SideFileModel.FileSchema;

        private const string CoreId = ModLog.CoreFeatureId;
        private const string ReadSite = "save/read";
        private const string WriteSite = "save/write";
        private const string LoadSite = "save/load";
        private const string NewGameSite = "save/newgame";
        private const string ClearSite = "save/clear";
        private const string DeleteSite = "save/delete";
        private const string EventSite = "save/events";
        private const int AttachRetryFrames = 600;
        private const int MaxPendingNotices = 8;
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly ModFlags FlagsTable = new ModFlags();
        private static readonly Action LoadedHandler = OnSaveLoaded;
        private static readonly HashSet<string> Registered = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Handed = new HashSet<string>(StringComparer.Ordinal);   // registered sections whose participant holds its data now
        private static readonly HashSet<string> Unmanaged = new HashSet<string>(StringComparer.Ordinal); // sections of code that is no feature: nothing switches them off
        private static readonly List<PendingNotice> PendingNotices = new List<PendingNotice>();

        private static SideFileModel _model;
        private static SideFileIo _io;
        private static bool _pathWarned;
        private static SideFileStatus _status = SideFileStatus.None;
        private static string _currentName;      // without .txt: the save loaded last or written last
        private static string _pendingLoad;      // announced by P13, not read yet
        private static string _sideFilePath;
        private static DateTime? _startDate;
        private static bool _newGameWarned;
        private static bool _gameBroken;
        private static bool _retrying;
        private static int _retryFrames;
        private static Action<string, object[]> _notice;

        private struct PendingNotice
        {
            public string Key;
            public object[] Args;
        }

        // ---- public API ----

        /// <summary>
        /// Registers a participant for the section <see cref="ISideFileParticipant.SectionId"/>. Features use <c>ctx.SaveParticipant(...)</c> in
        /// <c>Register</c>, which does the same with the feature's id as the owner. The section id of a feature's participant must be its feature's id
        /// (the id of the namespace the participant's type lives in) and <c>mod-framework</c> belongs to the core; anything else is refused with a log
        /// record. Registering while a game is in play gives the participant its data at once when its feature is active, otherwise when it starts.
        /// A null or a duplicate is logged and ignored. Never throws.
        /// </summary>
        public static void Register(IModSaveParticipant participant)
        {
            RegisterFor(null, participant);
        }

        /// <summary>
        /// The name of the save in play without <c>.txt</c>: the save that was loaded or written last, as the game named it (<c>1985_3_1</c>,
        /// <c>autosave</c>); null in the menu and in a new game that was not saved yet.
        /// </summary>
        public static string CurrentSaveName
        {
            get { return _currentName; }
        }

        /// <summary>What happened to the side file of the save in play (the "About" tab shows it); <see cref="SideFileStatus.None"/> before a save was loaded or a game started.</summary>
        public static SideFileStatus Status
        {
            get { return _status; }
        }

        /// <summary>
        /// The date the game's first day was played on: set when a new game becomes ready (<c>GameReady(NewGame)</c>), stored in the save. Null for a
        /// save made before the mod (the condition <c>daysSinceStartAtLeast</c> then takes the foundation date of the player's company).
        /// </summary>
        public static DateTime? GameStartDate
        {
            get { return _startDate; }
        }

        /// <summary>The flags of the features, saved with the game (the block <c>flags</c> of the core's section).</summary>
        public static ModFlags Flags
        {
            get { return FlagsTable; }
        }

        /// <summary>Every participant has been given its data after a load (or, with no file, its defaults). The place to wire what depends on several features.</summary>
        public static event Action AfterRead;

        /// <summary>The side file is about to be collected (a save is being written): the last moment to hand state over to a participant.</summary>
        public static event Action BeforeWrite;

        /// <summary>
        /// The player has to be told something about the side file: (L10n key, format arguments) - <c>core.save.corrupt</c> (the file name, the name of
        /// the copy or a dash), <c>core.save.newer</c> (the mod version that wrote it, or a question mark), <c>core.save.writeFailed</c> (the reason).
        /// <c>ModNotify</c> subscribes and shows them. A notice raised while nobody listens is kept (up to eight) and handed to the first subscriber.
        /// </summary>
        public static event Action<string, object[]> Notice
        {
            add
            {
                if (value == null) return;
                List<PendingNotice> replay = null;
                if (_notice == null && PendingNotices.Count > 0)
                {
                    replay = new List<PendingNotice>(PendingNotices);
                    PendingNotices.Clear();
                }
                _notice += value;
                if (replay == null) return;

                foreach (PendingNotice pending in replay) InvokeNotice(value, pending.Key, pending.Args);
            }
            remove { _notice -= value; }
        }

        /// <summary>The path of the side file of the save in play, or null when none is known yet (a new game, the menu).</summary>
        public static string SideFilePath
        {
            get { return _sideFilePath; }
        }

        /// <summary>The top-level fields of the side file that was read (mod and game versions, the date saved); null when none was read.</summary>
        public static SideFileHeader FileHeader
        {
            get { return _model == null ? null : _model.Header; }
        }

        // ---- start (the core's start methods) ----

        /// <summary>
        /// Declare stage: the section of the core gets its blocks (the start date and the flags are ours; <c>random</c> is registered by
        /// <c>ModRandom</c>), the model is created with the core's section first, and the events of the other services are followed. Early in the stage,
        /// so that on <c>BeforeGameReady</c> the side file is read before the other services look at their data.
        /// </summary>
        [CoreInit(CoreInitStage.Declare, -1000)]
        internal static void Declare()
        {
            CoreSection.RegisterPart(new StartDatePart());
            CoreSection.RegisterPart(FlagsTable.Part);
            EnsureModel();

            ModTicks.BeforeGameReady += OnBeforeGameReady;
            ModFeatures.StateChanged += OnFeatureStateChanged;
        }

        /// <summary>HostReady stage: follow the scenes - the menu clears the state, the game scene gets the <c>OnLoaded</c> subscription.</summary>
        [CoreInit(CoreInitStage.HostReady, 900, Optional = true)]
        internal static void Start()
        {
            PTModHost.SceneReady += OnSceneReady;

            // A late start inside a loaded scene: the host has raised SceneReady already.
            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        // ---- the game's moments (SavePatches, ModNewGame, the host) ----

        /// <summary>
        /// A new game is about to start (P17 on <c>GameManager.StartNewGame</c>, after <c>ModNewGame.BeforeStart</c>): every participant goes back to
        /// defaults before the fast-forward begins (FR-5 item 8) and the world seed of <see cref="ModRandom"/> is chosen (the scenario's, or the clock's).
        /// </summary>
        internal static void PrepareNewGame()
        {
            ModHooks.Guard(CoreId, NewGameSite, () =>
            {
                SideFileModel model = EnsureModel();
                _pendingLoad = null;
                _currentName = null;
                _sideFilePath = null;

                SideFileReadReport report;
                ModRandom.BeginReset(ModRandom.ResetCause.NewGame);
                try
                {
                    report = model.StartNewGame();
                }
                finally
                {
                    ModRandom.EndReset();
                }

                _status = SideFileStatus.NewGame;
                _newGameWarned = false;
                MarkHanded();
                LogWarnings(report.Warnings);
                ModLog.Info(CoreId, "save: new game, " + model.ParticipantCount + " participant(s) reset");
            });
        }

        /// <summary>The main menu is up (or the mod starts there): nothing of the last game stays - every participant is reset, the save in play is forgotten.</summary>
        internal static void Clear()
        {
            ModHooks.Guard(CoreId, ClearSite, () =>
            {
                SideFileModel model = EnsureModel();
                _pendingLoad = null;
                _currentName = null;
                _sideFilePath = null;

                SideFileReadReport report;
                ModRandom.BeginReset(ModRandom.ResetCause.Menu);
                try
                {
                    report = model.StartNewGame();
                }
                finally
                {
                    ModRandom.EndReset();
                }

                _status = SideFileStatus.None;
                MarkHanded();
                LogWarnings(report.Warnings);
                ModLog.Debug(CoreId, "save: the state of the last game is cleared");
            });
        }

        /// <summary>P13: the player loads <paramref name="saveName"/> (without <c>.txt</c>); the side file is read when the game says the save is loaded.</summary>
        internal static void PrepareLoad(string saveName)
        {
            ModHooks.Guard(CoreId, LoadSite, () =>
            {
                _pendingLoad = saveName;
                _currentName = saveName;
                ModLog.Debug(CoreId, "save: loading '" + saveName + "'; its side file is read on SaveHandler.OnLoaded");
            });
        }

        /// <summary>
        /// <c>SaveHandler.OnLoaded</c>: reads the side file of the save announced by <see cref="PrepareLoad"/> and hands the sections out, then raises
        /// <see cref="AfterRead"/>. Without an announced name (P13 is not applied) nothing can be read and the participants are reset, with a warning.
        /// </summary>
        internal static void ReadSideFile()
        {
            ModHooks.Guard(CoreId, ReadSite, ReadCore);
        }

        /// <summary>
        /// P12: the game wrote <paramref name="saveName"/>.txt; writes the side file beside it. <paramref name="gameDate"/> is the date of the
        /// game (the third argument of <c>SaveSystem.Save</c>). A failure is a log record and the notice <c>core.save.writeFailed</c>; the game goes on.
        /// </summary>
        internal static void WriteSideFile(string saveName, DateTime gameDate)
        {
            ModHooks.Guard(CoreId, WriteSite, () => WriteCore(saveName, gameDate));
        }

        /// <summary>P14: the game deleted <paramref name="fileName"/> (with <c>.txt</c>); its side file goes too. Corrupt copies are kept.</summary>
        internal static void DeleteSideFile(string fileName)
        {
            ModHooks.Guard(CoreId, DeleteSite, () =>
            {
                string name = SaveNames.FromFileName(fileName);
                SideFileIo io = EnsureIo();
                if (io == null) return;

                string error;
                bool deleted = io.Delete(name, out error);
                if (error != null) ModLog.Warn(CoreId, "save: the side file of '" + name + "' was not deleted: " + error);
                else if (deleted) ModLog.Info(CoreId, "save: deleted the side file of '" + name + "' together with the save");
            });
        }

        /// <summary>
        /// Registers a participant for <paramref name="featureId"/> (<c>ctx.SaveParticipant</c>), or for whoever owns its type when the id is null
        /// (<see cref="Register"/>). See <see cref="Register"/> for the rules. Returns true when it was registered.
        /// </summary>
        internal static bool RegisterFor(string featureId, IModSaveParticipant participant)
        {
            string logId = featureId ?? CoreId;
            try
            {
                if (participant == null)
                {
                    ModLog.Warn(logId, "save: Register(null) is ignored");
                    return false;
                }

                string type = participant.GetType().Name;
                string section = participant.SectionId;
                if (string.IsNullOrEmpty(section))
                {
                    ModLog.Error(logId, "save: " + type + " has an empty SectionId and is not registered");
                    return false;
                }

                bool isCore = ReferenceEquals(participant, CoreSection.Instance);
                string typeOwner = ModFeatures.FeatureOfType(participant.GetType());
                string owner = isCore ? CoreId : featureId ?? (typeOwner != null && typeOwner != CoreId ? typeOwner : null);
                if (owner != null && owner != CoreId)
                {
                    if (section != owner)
                    {
                        ModLog.Error(logId, "save: the section id '" + section + "' of " + type + " is not the id of its feature '" + owner
                                            + "'; a feature stores its data in the section of its own id, the participant is not registered");
                        return false;
                    }
                }
                else if (!isCore && section == CoreSection.Id)
                {
                    ModLog.Error(logId, "save: the section id '" + section + "' belongs to the core; " + type + " is not registered");
                    return false;
                }
                else if (!isCore && ModFeatures.Find(section) != null)
                {
                    ModLog.Error(logId, "save: the section id '" + section + "' is the id of a feature, only that feature may register it; " + type + " is not registered");
                    return false;
                }

                SideFileModel model = EnsureModel();
                ISideFileParticipant entry = isCore ? (ISideFileParticipant)participant : new GuardedParticipant(owner ?? CoreId, section, participant);
                string problem;
                if (!model.TryRegister(entry, out problem))
                {
                    ModLog.Error(logId, "save: " + type + " is not registered: " + problem);
                    return false;
                }

                Registered.Add(section);
                if (owner == null || owner == CoreId) Unmanaged.Add(section);
                ModLog.Debug(logId, "save: the participant " + type + " is registered for the section '" + section + "'");

                // Registered in the middle of a game: it has not been given its data yet.
                if (_status != SideFileStatus.None && IsSectionActive(section)) Restore(section);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(logId, "save: cannot register the save participant", ex);
                return false;
            }
        }

        /// <summary>The number of registered sections, the core's included (diagnostics).</summary>
        internal static int ParticipantCount
        {
            get { return _model == null ? 0 : _model.ParticipantCount; }
        }

        /// <summary>
        /// The disk and the clock for the offline harness: a fake file system and a Saves folder. The game uses the real ones, found on first use.
        /// </summary>
        internal static void UseForTests(IFileSystem fileSystem, string savesDirectory, Func<DateTime> utcNow)
        {
            _io = new SideFileIo(fileSystem, savesDirectory, utcNow);
        }

        // ---- reading ----

        private static void ReadCore()
        {
            SideFileModel model = EnsureModel();
            string name = _pendingLoad;
            _pendingLoad = null;

            SideFileReadReport report;
            if (name == null)
            {
                // OnLoaded is raised by SaveHandler.Load only, and P13 runs before it: this is a game version or a patch failure that
                // we cannot repair. Stale state of the last game must not stay, so everybody starts from defaults.
                ModLog.Warn(CoreId, "save: SaveHandler.OnLoaded came without a save name (the P13 patch on SaveHandler.Load did not run?); the mod state starts from defaults");
                report = model.ApplyMissing();
            }
            else
            {
                SideFileIo io = EnsureIo();
                if (io == null)
                {
                    report = model.ApplyMissing();
                    report.Warn("side file: the Saves folder is not known, no mod state is loaded");
                }
                else
                {
                    report = io.Read(model, name);
                }
            }

            _status = report.Status;
            _sideFilePath = report.Path;
            MarkHanded();
            Describe(report, name, model);
            ModHooks.Raise("ModSave.AfterRead", AfterRead);
        }

        /// <summary>Writes the result of a read to the log and raises the notices the player has to see.</summary>
        private static void Describe(SideFileReadReport report, string name, SideFileModel model)
        {
            string label = name == null ? "(no save name)" : "'" + name + "'";
            string file = report.Path == null ? label : Path.GetFileName(report.Path);
            SideFileHeader header = model.Header;

            switch (report.Status)
            {
                case SideFileStatus.Loaded:
                    ModLog.Info(CoreId, "save: read " + file + " (mod " + (header == null ? "?" : header.modVersion ?? "?") + ", game "
                                        + (header == null ? "?" : header.gameVersion ?? "?") + ", game date " + (header == null ? "?" : header.gameDate ?? "?") + ")");
                    break;

                case SideFileStatus.Corrupt:
                    ModLog.Error(CoreId, "save: " + file + " is not usable (" + report.Error + "); "
                                         + (report.QuarantinedTo == null ? "it could not be set aside" : "it was moved to " + Path.GetFileName(report.QuarantinedTo))
                                         + ", the mod state starts from defaults");
                    RaiseNotice("core.save.corrupt", file, report.QuarantinedTo == null ? "-" : Path.GetFileName(report.QuarantinedTo));
                    break;

                case SideFileStatus.Newer:
                    ModLog.Warn(CoreId, "save: " + file + " was written by a newer mod (" + (header == null ? "?" : header.modVersion ?? "?") + ", this is "
                                        + BuildInfo.Version + "); " + (report.FileIsNewer ? "the whole file" : string.Join(", ", ToArray(report.NewerSections)))
                                        + " is kept unchanged and the features get defaults");
                    RaiseNotice("core.save.newer", header == null || string.IsNullOrEmpty(header.modVersion) ? "?" : header.modVersion);
                    break;

                default:
                    if (!report.PathRejected)
                        ModLog.Info(CoreId, "save: no side file for " + label + " (an old or vanilla save); the mod state starts from defaults");
                    break;
            }
            LogWarnings(report.Warnings);
        }

        // ---- writing ----

        private static void WriteCore(string saveName, DateTime gameDate)
        {
            SideFileModel model = EnsureModel();
            ModHooks.Raise("ModSave.BeforeWrite", BeforeWrite);

            SideFileIo io = EnsureIo();
            if (io == null)
            {
                Failed("the Saves folder is not known", saveName);
                return;
            }

            var header = new SideFileHeader
            {
                modVersion = BuildInfo.Version,
                gameVersion = GameVersionText(),
                gameDate = gameDate == default(DateTime) ? null : gameDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            };
            SideFileWriteReport report = io.Write(model, saveName, header);
            LogWarnings(report.Warnings);

            if (!report.Success)
            {
                Failed(report.Error, saveName);
                return;
            }

            _currentName = saveName;
            _sideFilePath = report.Path;
            ModLog.Debug(CoreId, "save: wrote " + Path.GetFileName(report.Path));
        }

        private static void Failed(string error, string saveName)
        {
            ModLog.Error(CoreId, "save: the side file of '" + saveName + "' was not written; the game's own save is intact: " + error);
            RaiseNotice("core.save.writeFailed", error);
        }

        /// <summary>The game's version as the game writes it into its own saves. Read through reflection: a direct
        /// reference to the const would be baked into PTMod.dll at compile time and never follow a game update.</summary>
        private static string GameVersionText()
        {
            try
            {
                var field = typeof(ProcessorTycoon.GameVersion).GetField("Current",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as string ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        // ---- participants ----

        private static SideFileModel EnsureModel()
        {
            SideFileModel model = _model;
            if (model != null) return model;

            model = new SideFileModel(IsSectionActive);
            string problem;
            if (!model.TryRegister(CoreSection.Instance, out problem))
                ModLog.Error(CoreId, "save: the section of the core is not registered: " + problem);
            _model = model;
            return model;
        }

        /// <summary>The disk side, made on first use (the Saves folder comes from Unity and is not known before the main thread asks).</summary>
        private static SideFileIo EnsureIo()
        {
            SideFileIo io = _io;
            if (io != null) return io;

            string folder = RealFileSystem.PersistentDataPath;
            if (folder == null)
            {
                if (!_pathWarned)
                {
                    _pathWarned = true;
                    ModLog.Error(CoreId, "save: Application.persistentDataPath is not available; the side file cannot be read or written");
                }
                return null;
            }

            io = new SideFileIo(RealFileSystem.Instance, Path.Combine(folder, "Saves"));
            _io = io;
            return io;
        }

        /// <summary>
        /// Is the holder of the section on: the feature of that id is Enabled (not switched off, Unavailable or Faulted); the core's section and
        /// a section registered by code that is no feature (<see cref="Register"/> from outside the features) always are.
        /// </summary>
        private static bool IsSectionActive(string sectionId)
        {
            return Unmanaged.Contains(sectionId) || ModFeatures.IsActive(sectionId);
        }

        /// <summary>
        /// After a read, a new game or the menu: every registered participant that is active now has its data (the model read it, or reset it).
        /// Those that are not will get theirs when they become active (<see cref="OnFeatureStateChanged"/>).
        /// </summary>
        private static void MarkHanded()
        {
            Handed.Clear();
            foreach (string sectionId in Registered)
            {
                if (IsSectionActive(sectionId)) Handed.Add(sectionId);
            }
        }

        /// <summary>Gives a participant that was not active (or not registered) at load time its data now.</summary>
        private static void Restore(string sectionId)
        {
            try
            {
                Handed.Add(sectionId);
                SideFileReadReport report = EnsureModel().RestoreParticipant(sectionId);
                LogWarnings(report.Warnings);
                ModLog.Debug(CoreId, "save: the section '" + sectionId + "' was handed to its participant");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "save: cannot hand the section '" + sectionId + "' to its participant", ex);
            }
        }

        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (state != FeatureState.Enabled)
            {
                Handed.Remove(featureId);   // a feature that stops keeps nothing: its last written state waits in the model
                return;
            }

            // A feature switched on in the middle of a game (or after its Register): the data that waited for it in the file. Not twice:
            // a participant registered while its feature was already active got its data in Register.
            if (_status == SideFileStatus.None || !Registered.Contains(featureId) || Handed.Contains(featureId)) return;
            Restore(featureId);
        }

        // ---- scenes and events ----

        private static void OnSceneReady(int scene)
        {
            StopRetry();
            if (scene == PTModHost.MainMenuScene)
            {
                Clear();
                return;
            }
            if (scene != PTModHost.GameScene) return;

            if (!TryAttach(false)) StartRetry();
        }

        /// <summary>
        /// Before <c>GameReady</c>: remembers the day a new game starts on, and reads the side file of a load whose <c>OnLoaded</c> did not arrive
        /// (a safety net - the core services that restore their data on this event come after this handler).
        /// </summary>
        private static void OnBeforeGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(CoreId, EventSite, () =>
            {
                if (reason == GameReadyReason.NewGame)
                {
                    if (_status != SideFileStatus.NewGame)
                    {
                        if (!_newGameWarned)
                        {
                            _newGameWarned = true;
                            ModLog.Warn(CoreId, "save: a new game became ready without ModSave.PrepareNewGame (P17 did not run?); the state of the menu is used as the start state");
                        }
                        _status = SideFileStatus.NewGame;
                    }

                    DateTime today = ModTicks.Today;
                    if (today != default(DateTime)) _startDate = today.Date;
                    return;
                }

                if (_pendingLoad != null)
                {
                    ModLog.Warn(CoreId, "save: SaveHandler.OnLoaded did not read '" + _pendingLoad + "'; reading the side file now, before GameReady");
                    ReadSideFile();
                }
            });
        }

        private static void OnSaveLoaded()
        {
            ReadSideFile();
        }

        private static bool TryAttach(bool last)
        {
            if (_gameBroken) return true;   // nothing to wait for
            try
            {
                if (SubscribeLoaded()) return true;

                if (last)
                    ModLog.Error(CoreId, "save: SaveHandler not found in the game scene after " + AttachRetryFrames + " frames; the side file is read just before GameReady");
                return false;
            }
            catch (Exception ex) when (IsMismatch(ex))
            {
                _gameBroken = true;
                ModLog.Error(CoreId, "save: the game's SaveHandler does not match the mod (another game version?); the side file is read just before GameReady", ex);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "save: subscribing to SaveHandler.OnLoaded failed" + (last ? "; the side file is read just before GameReady" : ", trying again"), ex);
                return last;
            }
        }

        private static void StartRetry()
        {
            if (_retrying) return;
            _retrying = true;
            _retryFrames = 0;
            PTModHost.FrameUpdate += RetryAttach;
        }

        private static void StopRetry()
        {
            if (!_retrying) return;
            _retrying = false;
            PTModHost.FrameUpdate -= RetryAttach;
        }

        private static void RetryAttach()
        {
            bool last = ++_retryFrames >= AttachRetryFrames;
            if (TryAttach(last) || last) StopRetry();
        }

        private static bool IsMismatch(Exception ex)
        {
            return ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException;
        }

        /// <summary>
        /// <c>SaveHandler.OnLoaded</c>: the handler is removed and added again on every game scene, so it is there exactly once (<c>SaveHandler.Load</c>
        /// clears the event before a load). The game's class is touched only here: if it does not match the game, the JIT fails in this method and
        /// the caller catches it. False when there is no <c>SaveHandler</c> yet.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SubscribeLoaded()
        {
            GameSaves saves = GameSaves.Instance;
            if (saves == null) return false;

            saves.OnLoaded -= LoadedHandler;
            saves.OnLoaded += LoadedHandler;
            return true;
        }

        // ---- notices and log ----

        private static void RaiseNotice(string key, params object[] args)
        {
            Action<string, object[]> handlers = _notice;
            if (handlers == null)
            {
                if (PendingNotices.Count >= MaxPendingNotices) PendingNotices.RemoveAt(0);
                PendingNotices.Add(new PendingNotice { Key = key, Args = args });
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList()) InvokeNotice((Action<string, object[]>)handler, key, args);
        }

        private static void InvokeNotice(Action<string, object[]> handler, string key, object[] args)
        {
            try
            {
                handler(key, args);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "save: a Notice subscriber failed", ex);
            }
        }

        private static void LogWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings == null) return;
            foreach (string warning in warnings) ModLog.Warn(CoreId, "save: " + warning);
        }

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        // ---- the start date ----

        /// <summary>The block <c>startDate</c> of the section of the core: <c>"1975-01-01"</c>.</summary>
        private sealed class StartDatePart : ICoreSectionPart
        {
            public string Key
            {
                get { return CoreSection.StartDateKey; }
            }

            public void ResetToDefaults()
            {
                _startDate = null;
            }

            public void Read(JsonNode block, int schema)
            {
                var value = block as JsonValue;
                DateTime date;
                if (value != null && value.Kind == JsonKind.String
                    && DateTime.TryParseExact(value.StringValue, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    _startDate = date;
                    return;
                }

                _startDate = null;
                if (block != null && !block.IsNull) ModLog.Warn(CoreId, "save: startDate " + block + " is not a date yyyy-MM-dd, the start date is unknown");
            }

            public JsonNode Write()
            {
                return _startDate.HasValue ? JsonValue.Of(_startDate.Value.ToString(DateFormat, CultureInfo.InvariantCulture)) : null;
            }
        }

        // ---- a feature's participant behind the fault counter ----

        /// <summary>
        /// A feature's participant seen by the model: every call is counted against the feature when it throws (FR-2 item 3) and then passed on, so
        /// the model keeps the previous section of the feature. A feature that cannot run (Faulted, Unavailable) is not asked to reset.
        /// </summary>
        private sealed class GuardedParticipant : IModSaveParticipant
        {
            private readonly string _owner;
            private readonly string _section;
            private readonly IModSaveParticipant _inner;
            private readonly string _type;

            public GuardedParticipant(string owner, string section, IModSaveParticipant inner)
            {
                _owner = owner;
                _section = section;
                _inner = inner;
                _type = inner.GetType().Name;
            }

            public string SectionId
            {
                get { return _section; }
            }

            public int SchemaVersion
            {
                get
                {
                    try
                    {
                        return _inner.SchemaVersion;
                    }
                    catch (Exception ex)
                    {
                        ModHooks.ReportFault(_owner, _type + ".SchemaVersion", ex);
                        throw;
                    }
                }
            }

            public void ResetToDefaults()
            {
                if (!ModHooks.CanRun(_owner, _type + ".ResetToDefaults")) return;
                try
                {
                    _inner.ResetToDefaults();
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(_owner, _type + ".ResetToDefaults", ex);
                    throw;
                }
            }

            public void Read(JsonObject section, int schema)
            {
                try
                {
                    _inner.Read(section, schema);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(_owner, _type + ".Read", ex);
                    throw;
                }
            }

            public JsonObject Write()
            {
                try
                {
                    return _inner.Write();
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(_owner, _type + ".Write", ex);
                    throw;
                }
            }
        }
    }
}
