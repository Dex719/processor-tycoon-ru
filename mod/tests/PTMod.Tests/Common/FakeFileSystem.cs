using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.Save;

namespace PTMod.Tests.Common
{
    /// <summary>The operations of <see cref="IFileSystem"/>, for failure injection and for the operation log.</summary>
    public enum FsOp
    {
        FileExists,
        DirectoryExists,
        CreateDirectory,
        ReadAllText,
        WriteAllText,
        Move,
        Replace,
        Delete,
    }

    /// <summary>
    /// In-memory <see cref="IFileSystem"/> for the persistence tests. Behaves like the real disk where the logic depends on it: a parent
    /// directory must exist before a file is written, <c>Move</c> refuses an existing destination, <c>Replace</c> needs an existing
    /// destination and keeps the old content in the backup, a missing file is a <see cref="FileNotFoundException"/>. Paths are compared
    /// case-insensitively after <see cref="Path.GetFullPath(string)"/>.
    /// <para>
    /// <see cref="FailOn"/> makes the next matching operation throw an <see cref="IOException"/> BEFORE it changes anything, which is what an atomic
    /// <c>File.Replace</c> failure looks like to the caller. <see cref="Log"/> lists every mutating operation in order;
    /// <see cref="FinalWrites"/> counts how often a file received new content by Replace or Move.
    /// </para>
    /// </summary>
    public sealed class FakeFileSystem : IFileSystem
    {
        private sealed class FailRule
        {
            public FsOp Op;
            public string PathContains;
            public int Remaining;
            public string Message;
        }

        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FailRule> _rules = new List<FailRule>();
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<string, int> _finalWrites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A path-like root that exists on every Windows machine without being touched: tests build their paths under it.</summary>
        public const string Root = @"C:\PTFake";

        public FakeFileSystem()
        {
            _directories.Add(Full(Root));
        }

        /// <summary>Mutating operations in order: "WriteAllText p", "Replace src -> dst (backup b)", "Move src -> dst", "Delete p", "CreateDirectory p".</summary>
        public IReadOnlyList<string> Log
        {
            get { return _log; }
        }

        public IEnumerable<string> Files
        {
            get { return _files.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        // ---- test helpers ----

        /// <summary>Puts a file in place (creating its directories) without logging.</summary>
        public void Seed(string path, string text)
        {
            string full = Full(path);
            AddDirectories(Path.GetDirectoryName(full));
            _files[full] = text;
        }

        public void AddDirectory(string path)
        {
            AddDirectories(Full(path));
        }

        public bool Exists(string path)
        {
            return _files.ContainsKey(Full(path));
        }

        /// <summary>The content of a file; fails the test with a clear message when it does not exist.</summary>
        public string Text(string path)
        {
            string text;
            if (!_files.TryGetValue(Full(path), out text)) throw new AssertFailedException("the fake file does not exist: " + path);
            return text;
        }

        /// <summary>Cuts a file to its first <paramref name="length"/> characters (a write interrupted half way).</summary>
        public void Truncate(string path, int length)
        {
            string full = Full(path);
            _files[full] = _files[full].Substring(0, Math.Min(length, _files[full].Length));
        }

        /// <summary>Files whose name contains <paramref name="fragment"/> (e.g. ".corrupt-"), sorted.</summary>
        public List<string> FilesContaining(string fragment)
        {
            return _files.Keys.Where(k => k.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                              .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The next <paramref name="times"/> operations of this kind (on a path containing the text, if given) throw an IOException.</summary>
        public void FailOn(FsOp op, string pathContains = null, int times = 1, string message = "injected failure")
        {
            _rules.Add(new FailRule { Op = op, PathContains = pathContains, Remaining = times, Message = message });
        }

        public void ClearFailures()
        {
            _rules.Clear();
        }

        /// <summary>How many times the file got new content through Replace or Move since the start (a profile write = 1).</summary>
        public int FinalWrites(string path)
        {
            int count;
            return _finalWrites.TryGetValue(Full(path), out count) ? count : 0;
        }

        public int CountLog(string prefix)
        {
            return _log.Count(line => line.StartsWith(prefix, StringComparison.Ordinal));
        }

        // ---- IFileSystem ----

        public bool FileExists(string path)
        {
            Check(FsOp.FileExists, path);
            return _files.ContainsKey(Full(path));
        }

        public bool DirectoryExists(string path)
        {
            Check(FsOp.DirectoryExists, path);
            return _directories.Contains(Full(path));
        }

        public void CreateDirectory(string path)
        {
            Check(FsOp.CreateDirectory, path);
            _log.Add("CreateDirectory " + path);
            AddDirectories(Full(path));
        }

        public string ReadAllText(string path)
        {
            Check(FsOp.ReadAllText, path);
            string text;
            if (!_files.TryGetValue(Full(path), out text)) throw new FileNotFoundException("no such file: " + path);
            return text;
        }

        public void WriteAllText(string path, string contents)
        {
            Check(FsOp.WriteAllText, path);
            string full = Full(path);
            RequireDirectory(full);
            _log.Add("WriteAllText " + path);
            _files[full] = contents;
        }

        public void Move(string source, string destination)
        {
            Check(FsOp.Move, source, destination);
            string from = Full(source);
            string to = Full(destination);
            if (!_files.ContainsKey(from)) throw new FileNotFoundException("no such file: " + source);
            if (_files.ContainsKey(to)) throw new IOException("the destination exists: " + destination);
            RequireDirectory(to);
            _log.Add("Move " + source + " -> " + destination);
            _files[to] = _files[from];
            _files.Remove(from);
            CountFinalWrite(to);
        }

        public void Replace(string source, string destination, string backup)
        {
            Check(FsOp.Replace, source, destination);
            string from = Full(source);
            string to = Full(destination);
            if (!_files.ContainsKey(from)) throw new FileNotFoundException("no such file: " + source);
            if (!_files.ContainsKey(to)) throw new FileNotFoundException("no such file: " + destination);
            if (backup != null) RequireDirectory(Full(backup));

            _log.Add("Replace " + source + " -> " + destination + (backup != null ? " (backup " + backup + ")" : string.Empty));
            if (backup != null) _files[Full(backup)] = _files[to];
            _files[to] = _files[from];
            _files.Remove(from);
            CountFinalWrite(to);
        }

        public void Delete(string path)
        {
            Check(FsOp.Delete, path);
            _log.Add("Delete " + path);
            _files.Remove(Full(path));
        }

        public string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        // ---- internals ----

        private static string Full(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }

        private void AddDirectories(string full)
        {
            while (!string.IsNullOrEmpty(full) && _directories.Add(full)) full = Path.GetDirectoryName(full);
        }

        private void RequireDirectory(string fullFilePath)
        {
            string directory = Path.GetDirectoryName(fullFilePath);
            if (!string.IsNullOrEmpty(directory) && !_directories.Contains(directory))
                throw new DirectoryNotFoundException("the directory does not exist: " + directory);
        }

        private void CountFinalWrite(string fullPath)
        {
            int count;
            _finalWrites.TryGetValue(fullPath, out count);
            _finalWrites[fullPath] = count + 1;
        }

        private void Check(FsOp op, params string[] paths)
        {
            foreach (FailRule rule in _rules)
            {
                if (rule.Op != op || rule.Remaining <= 0) continue;
                if (rule.PathContains != null && !paths.Any(p => p != null && p.IndexOf(rule.PathContains, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                rule.Remaining--;
                throw new IOException(rule.Message + " (" + op + " " + string.Join(" -> ", paths.Where(p => p != null)) + ")");
            }
        }
    }
}
