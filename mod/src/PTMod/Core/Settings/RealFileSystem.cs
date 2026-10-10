using System;
using System.IO;
using System.Text;
using PTMod.Core.Logic.Save;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// The real disk (<c>System.IO</c>) behind <see cref="IFileSystem"/>: what <c>ModSettings</c>, <c>ModProfile</c>, <c>ModL10n</c>,
    /// <c>ModContent</c> and <c>ModSave</c> pass to the pure logic. Thin on purpose: every decision (atomic write order, quarantine,
    /// size limits) is made by the logic above it. Text is UTF-8: writes produce no BOM, reads accept one.
    /// <para>
    /// The one thing it adds to <c>File.Replace</c> is a fallback for runtimes that do not implement it (a Mono class library may throw
    /// <see cref="PlatformNotSupportedException"/> / <see cref="NotImplementedException"/>): the backup is copied, the old file is moved
    /// aside and put back if the new one cannot take its place. Any other failure of <c>File.Replace</c> goes to the caller untouched,
    /// because the contract is "a failure leaves the destination and the backup as they were".
    /// </para>
    /// It also resolves the two roots the mod works under (cached after the first success; the Unity getters are main-thread API, a
    /// failed lookup returns null and is retried on the next call):
    /// <see cref="PersistentDataPath"/> (<c>ptmod_settings.json</c>, <c>ptmod_profile.json</c>, <c>Saves/</c>) and <see cref="GameFolder"/>
    /// (<c>PTMod/content</c>, <c>PTMod/l10n</c>).
    /// </summary>
    public sealed class RealFileSystem : IFileSystem
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private static string _persistentDataPath;
        private static string _gameFolder;

        /// <summary>The shared instance; the class is stateless, <c>new RealFileSystem()</c> is equivalent.</summary>
        public static readonly RealFileSystem Instance = new RealFileSystem();

        /// <summary>
        /// <c>Application.persistentDataPath</c> as a full path with the platform's separators, or null when Unity cannot say (the call
        /// is not on the main thread). The folder that already holds <c>Saves/</c> and <c>GameSettings/</c>.
        /// </summary>
        internal static string PersistentDataPath
        {
            get
            {
                string cached = _persistentDataPath;
                if (cached != null) return cached;

                string resolved = TryFullPath(() => Application.persistentDataPath, false);
                if (resolved != null) _persistentDataPath = resolved;
                return resolved;
            }
        }

        /// <summary>The folder with <c>Processor Tycoon.exe</c>: the parent of <c>Application.dataPath</c>. Null when Unity cannot say.</summary>
        internal static string GameFolder
        {
            get
            {
                string cached = _gameFolder;
                if (cached != null) return cached;

                string resolved = TryFullPath(() => Application.dataPath, true);
                if (resolved != null) _gameFolder = resolved;
                return resolved;
            }
        }

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
            try
            {
                File.Replace(source, destination, backup);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByHand(source, destination, backup);
            }
            catch (NotImplementedException)
            {
                ReplaceByHand(source, destination, backup);
            }
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// <c>File.Replace</c> for runtimes without it. Nothing is changed before the backup copy succeeded; the destination is moved aside
        /// (not deleted) so that it can be put back if the source cannot be moved into place.
        /// </summary>
        private static void ReplaceByHand(string source, string destination, string backup)
        {
            if (!File.Exists(destination)) throw new FileNotFoundException("The file to replace does not exist", destination);

            if (!string.IsNullOrEmpty(backup)) File.Copy(destination, backup, true);

            string aside = destination + ".replacing";
            if (File.Exists(aside)) File.Delete(aside);
            File.Move(destination, aside);
            try
            {
                File.Move(source, destination);
            }
            catch (Exception)
            {
                File.Move(aside, destination);
                throw;
            }

            try
            {
                File.Delete(aside);
            }
            catch (Exception)
            {
                // The new file is in place; a leftover ".replacing" is harmless.
            }
        }

        /// <summary>Runs a Unity path getter and normalizes the result; null on an empty value or any failure.</summary>
        private static string TryFullPath(Func<string> getter, bool parent)
        {
            try
            {
                string raw = getter();
                if (string.IsNullOrEmpty(raw)) return null;

                string full = Path.GetFullPath(raw);
                if (!parent) return full;

                string up = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.IsNullOrEmpty(up) ? null : up;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
