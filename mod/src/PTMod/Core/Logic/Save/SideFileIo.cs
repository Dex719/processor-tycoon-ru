using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PTMod.Core.Logic.Save
{
    /// <summary>Outcome of <see cref="SideFileIo.Write"/>.</summary>
    public sealed class SideFileWriteReport
    {
        private readonly List<string> _warnings = new List<string>();

        internal SideFileWriteReport(string path)
        {
            Path = path;
        }

        /// <summary>True when the side file was written. On false the previous side file (if any) is untouched and the game's own save is not affected.</summary>
        public bool Success { get; internal set; }

        /// <summary>The side file path, or null when the save name was rejected.</summary>
        public string Path { get; internal set; }

        /// <summary>Why nothing was written; null on success. For the log and the "Mod state not saved" notification.</summary>
        public string Error { get; internal set; }

        /// <summary>The save name could not be turned into a path inside Saves/.</summary>
        public bool PathRejected { get; internal set; }

        /// <summary>Problems met while collecting the sections (a participant that threw, a returned <c>_schema</c>, ...).</summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        internal void AddWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings != null) _warnings.AddRange(warnings);
        }
    }

    /// <summary>
    /// Disk side of the side save file (FR-5): where the file lives, reading it into a <see cref="SideFileModel"/>, writing it atomically,
    /// deleting it. Works through <see cref="IFileSystem"/> and an injected clock, so everything is testable without a disk. Never throws.
    /// <list type="bullet">
    /// <item>Path: <c>&lt;Saves&gt;/&lt;name&gt;.ptmod.json</c>, built by plain concatenation like the game does for <c>&lt;name&gt;.txt</c>. A name that
    /// makes the full path leave <c>Saves/</c> (<c>..</c>, a drive, invalid characters) is rejected: nothing is read or written (design: Security).</item>
    /// <item>Write: <c>name.ptmod.json.tmp</c>, then Replace (or Move when there is no file yet). Any failure removes the temporary file and leaves the
    /// previous side file as it was.</item>
    /// <item>Read: a missing file gives defaults; an unreadable or invalid one is renamed to <c>name.ptmod.json.corrupt-yyyyMMddHHmmss</c> (UTC).</item>
    /// </list>
    /// </summary>
    public sealed class SideFileIo
    {
        /// <summary>Extension that follows the save name.</summary>
        public const string Extension = ".ptmod.json";

        private static readonly char[] InvalidNameCharacters = Path.GetInvalidFileNameChars();

        private readonly IFileSystem _fs;
        private readonly string _savesDirectory;   // normalized, no trailing separator; null when it could not be normalized
        private readonly Func<DateTime> _utcNow;

        /// <param name="savesDirectory"><c>Application.persistentDataPath + "/Saves"</c>.</param>
        /// <param name="utcNow">Clock for the quarantine timestamp and <c>writtenUtc</c>; null = <see cref="DateTime.UtcNow"/>.</param>
        public SideFileIo(IFileSystem fs, string savesDirectory, Func<DateTime> utcNow = null)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            _fs = fs;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            try
            {
                string full = fs.GetFullPath(savesDirectory ?? string.Empty);
                _savesDirectory = string.IsNullOrEmpty(savesDirectory) ? null : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                _savesDirectory = null;
            }
        }

        /// <summary>The normalized Saves folder, or null when the configured one is not a valid path.</summary>
        public string SavesDirectory
        {
            get { return _savesDirectory; }
        }

        /// <summary>
        /// Full path of the side file for a save name (the name the game used, without extension, including the <c>Y_M_D</c> and
        /// <c>autosave</c> fallbacks). False with the reason when the name is empty or the path would leave the Saves folder.
        /// </summary>
        public bool TryGetPath(string saveName, out string path, out string reason)
        {
            path = null;
            reason = null;
            if (_savesDirectory == null)
            {
                reason = "the Saves folder is not a valid path";
                return false;
            }
            if (string.IsNullOrWhiteSpace(saveName))
            {
                reason = "the save name is empty";
                return false;
            }
            if (HasInvalidNameCharacter(saveName))
            {
                reason = "the save name contains characters that a file name cannot have";
                return false;
            }

            string full;
            try
            {
                full = _fs.GetFullPath(_savesDirectory + Path.DirectorySeparatorChar + saveName + Extension);
            }
            catch (Exception)
            {
                reason = "the save name is not valid in a file path";
                return false;
            }

            string prefix = _savesDirectory + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "the path leaves the Saves folder";
                return false;
            }

            path = full;
            return true;
        }

        /// <summary>Characters the game could not have written into a file name either (separators are allowed: the path check decides about those).</summary>
        private static bool HasInvalidNameCharacter(string saveName)
        {
            foreach (char c in saveName)
            {
                if (c == '/' || c == '\\') continue;
                if (Array.IndexOf(InvalidNameCharacters, c) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Loads the side file of a save into the model. No file: defaults (<see cref="SideFileStatus.Missing"/>). Broken file: defaults,
        /// <see cref="SideFileStatus.Corrupt"/>, and the file is set aside (<see cref="SideFileReadReport.QuarantinedTo"/>). A rejected save
        /// name is treated as "no file" with a warning.
        /// </summary>
        public SideFileReadReport Read(SideFileModel model, string saveName)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string path;
            string reason;
            if (!TryGetPath(saveName, out path, out reason))
            {
                SideFileReadReport rejected = model.ApplyMissing();
                rejected.PathRejected = true;
                rejected.Warn("side file: " + reason + " ('" + saveName + "'), no mod state is loaded");
                return rejected;
            }

            bool exists = false;
            string text = null;
            string readError = null;
            try
            {
                exists = _fs.FileExists(path);
                if (exists) text = _fs.ReadAllText(path);
            }
            catch (Exception ex)
            {
                readError = AtomicFile.Describe(ex);
            }

            SideFileReadReport report;
            if (readError != null) report = model.ApplyCorrupt("the file cannot be read: " + readError);
            else if (!exists) report = model.ApplyMissing();
            else report = model.Apply(text);

            report.Path = path;
            if (report.Status == SideFileStatus.Corrupt && (exists || readError != null))
            {
                string error;
                report.QuarantinedTo = AtomicFile.Quarantine(_fs, path, _utcNow(), out error);
                if (report.QuarantinedTo == null) report.Warn("side file: the broken file could not be set aside: " + error);
            }
            return report;
        }

        /// <summary>
        /// Collects the document from the model and writes it atomically. <paramref name="header"/> supplies modVersion, gameVersion and gameDate;
        /// <c>schema</c>, <c>saveName</c> and (unless given) <c>writtenUtc</c> are filled here. The header object is not modified.
        /// </summary>
        public SideFileWriteReport Write(SideFileModel model, string saveName, SideFileHeader header)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string path;
            string reason;
            if (!TryGetPath(saveName, out path, out reason))
            {
                return new SideFileWriteReport(null)
                {
                    Success = false,
                    PathRejected = true,
                    Error = "the side file was not written: " + reason + " ('" + saveName + "')",
                };
            }

            var fields = new SideFileHeader
            {
                schema = SideFileModel.FileSchema,
                modVersion = header != null ? header.modVersion : null,
                gameVersion = header != null ? header.gameVersion : null,
                gameDate = header != null ? header.gameDate : null,
                saveName = saveName,
                writtenUtc = header != null && header.writtenUtc != null
                    ? header.writtenUtc
                    : _utcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            };

            SideFileBuildReport build = model.Build(fields);
            FileWriteResult written = AtomicFile.WriteJson(_fs, path, build.Document, null);

            var report = new SideFileWriteReport(path) { Success = written.Success, Error = written.Error };
            report.AddWarnings(build.Warnings);
            return report;
        }

        /// <summary>
        /// Deletes the side file (the player deleted the save in the load or save window) and a temporary file left by an interrupted write.
        /// Corrupt copies are kept. True when a side file existed and is gone.
        /// </summary>
        public bool Delete(string saveName, out string error)
        {
            error = null;
            string path;
            if (!TryGetPath(saveName, out path, out error)) return false;

            try
            {
                bool existed = _fs.FileExists(path);
                if (existed) _fs.Delete(path);
                AtomicFile.TryDelete(_fs, path + AtomicFile.TempSuffix);
                return existed;
            }
            catch (Exception ex)
            {
                error = AtomicFile.Describe(ex);
                return false;
            }
        }
    }
}
