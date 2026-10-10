using System;
using System.Globalization;
using System.IO;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Save
{
    /// <summary>
    /// The disk as seen by the persistence logic (side save file, settings, profile). The logic never touches
    /// <c>System.IO</c> directly: the game side passes <see cref="SystemFileSystem"/>, the unit tests pass a fake that can
    /// fail on demand. Semantics follow <c>System.IO.File</c>: members throw <see cref="IOException"/> and its relatives
    /// on failure; callers (<see cref="AtomicFile"/>, the models) catch and report, nothing may reach the game.
    /// Text is UTF-8; writes produce no BOM, reads accept one.
    /// </summary>
    public interface IFileSystem
    {
        bool FileExists(string path);

        bool DirectoryExists(string path);

        /// <summary>Creates the directory and its parents; no error when it already exists.</summary>
        void CreateDirectory(string path);

        string ReadAllText(string path);

        /// <summary>Creates or overwrites the file. The parent directory must exist.</summary>
        void WriteAllText(string path, string contents);

        /// <summary>Like <c>File.Move</c>: fails when the source is missing or the destination exists.</summary>
        void Move(string source, string destination);

        /// <summary>
        /// Like <c>File.Replace</c>: the destination must exist; it receives the content of the source and the source
        /// disappears. When <paramref name="backup"/> is not null, the previous destination content is kept there
        /// (an existing backup is overwritten). Atomic: a failure leaves the destination (and the backup) as they were.
        /// </summary>
        void Replace(string source, string destination, string backup);

        /// <summary>Deletes the file; no error when it does not exist.</summary>
        void Delete(string path);

        /// <summary>Normalized absolute path (used for the "inside Saves/" check). May throw for an invalid path.</summary>
        string GetFullPath(string path);
    }

    /// <summary>The real disk (<c>System.IO</c>). Thin on purpose: all decisions are made by the logic above it.</summary>
    public sealed class SystemFileSystem : IFileSystem
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path, Utf8);   // a BOM, if present, is detected and skipped
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents, Utf8);
        }

        public void Move(string source, string destination)
        {
            File.Move(source, destination);
        }

        public void Replace(string source, string destination, string backup)
        {
            File.Replace(source, destination, backup);
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }
    }

    /// <summary>Outcome of an atomic write: success, or the reason in plain English (for the log).</summary>
    public sealed class FileWriteResult
    {
        private FileWriteResult(bool success, string path, string error)
        {
            Success = success;
            Path = path;
            Error = error;
        }

        public bool Success { get; }

        /// <summary>The file that was (or should have been) written.</summary>
        public string Path { get; }

        /// <summary>Null on success.</summary>
        public string Error { get; }

        public static FileWriteResult Ok(string path)
        {
            return new FileWriteResult(true, path, null);
        }

        public static FileWriteResult Fail(string path, string error)
        {
            return new FileWriteResult(false, path, error ?? "unknown error");
        }

        public override string ToString()
        {
            return Success ? "written " + Path : "not written " + Path + ": " + Error;
        }
    }

    /// <summary>
    /// The three disk operations shared by the side save file, the settings and the profile:
    /// atomic write (temporary file, then replace or move), the size guard, and quarantine of a broken file.
    /// None of them throws.
    /// </summary>
    public static class AtomicFile
    {
        public const string TempSuffix = ".tmp";
        public const string CorruptInfix = ".corrupt-";

        /// <summary>
        /// Writes <paramref name="document"/> as JSON to <paramref name="path"/> atomically: <c>path.tmp</c> first, then
        /// <c>Replace</c> (target exists; the old content goes to <paramref name="backupPath"/> when it is not null) or
        /// <c>Move</c> (target absent). On any failure the temporary file is removed and the previous target (and backup)
        /// stay as they were. The text is indented; when that would exceed <see cref="ModJson.MaxBytes"/> the compact form is
        /// tried, and a document that still does not fit is refused (the next load would reject the file).
        /// </summary>
        public static FileWriteResult WriteJson(IFileSystem fs, string path, JsonNode document, string backupPath)
        {
            string text;
            try
            {
                text = Serialize(document);
            }
            catch (Exception ex)
            {
                return FileWriteResult.Fail(path, "cannot serialize the document: " + Describe(ex));
            }
            if (text == null)
                return FileWriteResult.Fail(path, "the document is larger than " + ModJson.MaxBytes.ToString(CultureInfo.InvariantCulture) + " bytes, the file would not be readable back");
            return WriteText(fs, path, text, backupPath);
        }

        /// <summary>The same atomic sequence for ready text (no size guard here: <see cref="WriteJson"/> applies it).</summary>
        public static FileWriteResult WriteText(IFileSystem fs, string path, string text, string backupPath)
        {
            if (fs == null || string.IsNullOrEmpty(path) || text == null)
                return FileWriteResult.Fail(path, "invalid arguments");

            string temp = path + TempSuffix;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !fs.DirectoryExists(directory)) fs.CreateDirectory(directory);

                fs.WriteAllText(temp, text);
                if (fs.FileExists(path)) fs.Replace(temp, path, backupPath);
                else fs.Move(temp, path);
                return FileWriteResult.Ok(path);
            }
            catch (Exception ex)
            {
                TryDelete(fs, temp);
                return FileWriteResult.Fail(path, Describe(ex));
            }
        }

        /// <summary>
        /// Renames a broken file to <c>path.corrupt-yyyyMMddHHmmss</c> (a numeric suffix <c>-2</c>, <c>-3</c>... is added when that
        /// name is taken) and returns the new path, or null with the reason in <paramref name="error"/>.
        /// </summary>
        public static string Quarantine(IFileSystem fs, string path, DateTime timestamp, out string error)
        {
            error = null;
            try
            {
                string stamp = timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                string target = path + CorruptInfix + stamp;
                for (int n = 2; fs.FileExists(target) && n < 1000; n++)
                    target = path + CorruptInfix + stamp + "-" + n.ToString(CultureInfo.InvariantCulture);
                fs.Move(path, target);
                return target;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return null;
            }
        }

        /// <summary>Best-effort delete; false when it failed (the reason is not interesting to anybody).</summary>
        public static bool TryDelete(IFileSystem fs, string path)
        {
            try
            {
                fs.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>"IOException: message" - the form used in warnings and errors.</summary>
        public static string Describe(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>Indented text, or the compact text when only that fits, or null when nothing fits.</summary>
        private static string Serialize(JsonNode document)
        {
            string pretty = ModJson.Write(document, true);
            if (FitsInLimit(pretty)) return pretty;

            string compact = ModJson.Write(document, false);
            return FitsInLimit(compact) ? compact : null;
        }

        private static bool FitsInLimit(string text)
        {
            if (text.Length > ModJson.MaxBytes) return false;
            return Encoding.UTF8.GetByteCount(text) <= ModJson.MaxBytes;
        }
    }
}
