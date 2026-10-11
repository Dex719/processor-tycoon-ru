using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;

namespace PTMod.Tests.Architecture
{
    // Build and architecture checks (TSK-046, NFR-7, NFR-8) and the checks of the shipped data files (TSK-043):
    //   - ArchitectureTests: the pure logic stays free of Unity, nothing talks to the network, every public type of
    //     Core/Logic/** is listed in [Covers], PTMod.dll (when it is built and fresh) carries the resources and the version.
    //   - CoreContentTests: Content/mod-framework/core.json is the design.md table ("Контент ядра").
    //   - L10nContentTests: L10n/ru.json and L10n/en.json are complete, consistent and cover every core.* key the code uses.
    // The files are read from the source tree (TestEnv.SrcDir); PTMod.dll from mod/build. A test that needs the DLL says "skip"
    // on the console and passes when the DLL is missing or older than the sources (rebuild with python mod/build.py --game-dir ...).

    /// <summary>Source scanning shared by the tests of this file. Internal, so the runner never discovers it.</summary>
    internal static class SourceScan
    {
        public static string SrcDirOrFail()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            Assert.True(Directory.Exists(TestEnv.SrcDir), "no source folder: " + TestEnv.SrcDir);
            return TestEnv.SrcDir;
        }

        /// <summary>Full paths of the files with the extension under <paramref name="root"/>, sorted; empty when the folder is missing.</summary>
        public static List<string> Files(string root, string extension)
        {
            var result = new List<string>();
            if (root == null || !Directory.Exists(root)) return result;
            foreach (string path in Directory.GetFiles(root, "*" + extension, SearchOption.AllDirectories))
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) result.Add(path);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static string Relative(string root, string path)
        {
            string full = Path.GetFullPath(path);
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relative = full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full.Substring(rootFull.Length + 1) : full;
            return relative.Replace('\\', '/');
        }

        public static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n') line++;
            }
            return line;
        }

        /// <summary>UTF-8 text of a file. Other tools may be writing into the tree, so a file in use is read shared and one that vanished reads as empty.</summary>
        public static string ReadText(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Removes the comments of C# source and, when <paramref name="blankStrings"/> is set, the contents of string and char
        /// literals; everything removed becomes spaces, so offsets and line numbers of the result match the original.
        /// Handles //, /* */, "..." with escapes, @"..." (and $@, @$), raw """...""" strings and char literals.
        /// </summary>
        public static string Strip(string text, bool blankStrings)
        {
            var sb = new StringBuilder(text.Length);
            int n = text.Length;
            int i = 0;
            while (i < n)
            {
                char c = text[i];
                char next = i + 1 < n ? text[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    while (i < n && text[i] != '\n' && text[i] != '\r')
                    {
                        sb.Append(' ');
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    sb.Append("  ");
                    i += 2;
                    while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/'))
                    {
                        sb.Append(IsNewline(text[i]) ? text[i] : ' ');
                        i++;
                    }
                    if (i < n)
                    {
                        sb.Append("  ");
                        i += 2;
                    }
                }
                else if (c == '"')
                {
                    i = ReadString(text, i, blankStrings, sb);
                }
                else if (c == '\'')
                {
                    i = ReadChar(text, i, blankStrings, sb);
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static bool IsNewline(char c)
        {
            return c == '\n' || c == '\r';
        }

        private static void Emit(StringBuilder sb, char c, bool blank)
        {
            sb.Append(blank && !IsNewline(c) ? ' ' : c);
        }

        /// <summary>Copies the literal that starts at the opening quote at <paramref name="start"/>; returns the index after it.</summary>
        private static int ReadString(string text, int start, bool blank, StringBuilder sb)
        {
            int n = text.Length;
            int i = start;

            int quotes = 0;
            while (i + quotes < n && text[i + quotes] == '"') quotes++;

            if (quotes >= 3)
            {
                // Raw string literal: ends at a run of at least as many quotes.
                sb.Append('"', quotes);
                i += quotes;
                while (i < n)
                {
                    int run = 0;
                    while (i + run < n && text[i + run] == '"') run++;
                    if (run >= quotes)
                    {
                        sb.Append('"', quotes);
                        return i + quotes;
                    }
                    Emit(sb, text[i], blank);
                    i++;
                }
                return i;
            }

            bool verbatim = (start >= 1 && text[start - 1] == '@') || (start >= 2 && text[start - 1] == '$' && text[start - 2] == '@');
            sb.Append('"');
            i++;
            while (i < n)
            {
                char c = text[i];
                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"')
                        {
                            Emit(sb, '"', blank);
                            Emit(sb, '"', blank);
                            i += 2;
                            continue;
                        }
                        sb.Append('"');
                        return i + 1;
                    }
                }
                else
                {
                    if (c == '\\' && i + 1 < n)
                    {
                        Emit(sb, c, blank);
                        Emit(sb, text[i + 1], blank);
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        sb.Append('"');
                        return i + 1;
                    }
                    if (IsNewline(c))
                    {
                        return i;   // unterminated: stop at the line end instead of swallowing the file
                    }
                }
                Emit(sb, c, blank);
                i++;
            }
            return i;
        }

        private static int ReadChar(string text, int start, bool blank, StringBuilder sb)
        {
            int n = text.Length;
            int i = start;
            sb.Append('\'');
            i++;
            while (i < n)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < n)
                {
                    Emit(sb, c, blank);
                    Emit(sb, text[i + 1], blank);
                    i += 2;
                    continue;
                }
                if (c == '\'')
                {
                    sb.Append('\'');
                    return i + 1;
                }
                if (IsNewline(c)) return i;
                Emit(sb, c, blank);
                i++;
            }
            return i;
        }
    }

    /// <summary>The built PTMod.dll for the tests that need it: loaded reflection-only from bytes (no file lock, no Unity needed).</summary>
    internal static class BuiltDll
    {
        private static bool loaded;
        private static Assembly assembly;
        private static string skipReason;
        private static string failure;

        public static string FilePath
        {
            get { return TestEnv.BuildDir == null ? null : Path.Combine(TestEnv.BuildDir, "PTMod.dll"); }
        }

        /// <summary>
        /// True when the DLL is built, newer than all sources and loaded. False with <paramref name="skipReason"/> when there is nothing
        /// to check (not built, or stale); fails the test when the DLL exists and is fresh but cannot be read.
        /// </summary>
        public static bool TryGet(out Assembly result, out string reason)
        {
            if (!loaded)
            {
                loaded = true;
                Load();
            }
            if (failure != null) Assert.Fail(failure);
            result = assembly;
            reason = skipReason;
            return assembly != null;
        }

        private static void Load()
        {
            string path = FilePath;
            if (path == null || !File.Exists(path))
            {
                skipReason = "PTMod.dll is not built (python mod/build.py --game-dir <game folder>)";
                return;
            }

            string newer = NewestInputNewerThan(File.GetLastWriteTimeUtc(path));
            if (newer != null)
            {
                skipReason = "PTMod.dll is older than " + newer + " (rebuild: python mod/build.py --game-dir <game folder>)";
                return;
            }

            try
            {
                ResolveHandler.Install();
                assembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                failure = "PTMod.dll cannot be loaded for inspection: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>Relative path of a source file (or VERSION) written after the DLL, or null when the DLL is up to date.</summary>
        private static string NewestInputNewerThan(DateTime builtUtc)
        {
            string src = TestEnv.SrcDir;
            var inputs = new List<string>();
            if (src != null)
            {
                inputs.AddRange(SourceScan.Files(src, ".cs"));
                inputs.AddRange(SourceScan.Files(src, ".json"));
            }
            if (TestEnv.ModDir != null) inputs.Add(Path.Combine(TestEnv.ModDir, "VERSION"));

            foreach (string input in inputs)
            {
                if (File.Exists(input) && File.GetLastWriteTimeUtc(input) > builtUtc)
                    return TestEnv.ModDir == null ? input.Replace('\\', '/') : SourceScan.Relative(TestEnv.ModDir, input);
            }
            return null;
        }

        /// <summary>Reflection-only loading does not resolve references by itself: send the framework ones to the runtime folder.</summary>
        private static class ResolveHandler
        {
            private static bool installed;

            public static void Install()
            {
                if (installed) return;
                installed = true;
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender, args) =>
                {
                    try
                    {
                        string simple = new AssemblyName(args.Name).Name;
                        string candidate = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), simple + ".dll");
                        return File.Exists(candidate) ? Assembly.ReflectionOnlyLoadFrom(candidate) : null;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                };
            }
        }
    }

    /// <summary>Notes for the console: the runner prints failures only, a skip must still be visible.</summary>
    internal static class Note
    {
        public static void Skip(string test, string reason)
        {
            Console.WriteLine("  skip  PTMod.Tests.Architecture." + test + ": " + reason);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Architecture
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>TSK-046 / NFR-7 / NFR-8: layering of the sources, no network, [Covers] completeness, resources and version of PTMod.dll.</summary>
    public class ArchitectureTests
    {
        // Namespaces the pure logic must not touch: the engine, its UI/text libraries, Harmony and the game's own assembly.
        private static readonly Regex EngineUsing = new Regex(
            @"\busing\s+(?:static\s+)?(?:global::)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b", RegexOptions.Compiled);

        private static readonly Regex EngineQualified = new Regex(
            @"(?<![\w.])(?:global::)?(?:UnityEngine|UnityEditor|TMPro|HarmonyLib|ProcessorTycoon)(?=\s*\.\s*\w)", RegexOptions.Compiled);

        // Network access in any form (NFR-7): System.Net.* (also Http and Sockets), UnityEngine.Networking, the usual client types.
        private static readonly Regex NetworkUse = new Regex(
            @"\bSystem\s*\.\s*Net\b|\bUnityEngine\s*\.\s*Networking\b|\bUnityWebRequest\b|\bWebClient\b|\bHttpClient\b|\bHttpWebRequest\b|\bWebRequest\b|\bTcpClient\b|\bUdpClient\b",
            RegexOptions.Compiled);

        private static readonly Regex VersionText = new Regex(@"^(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.+-]+)?$", RegexOptions.Compiled);

        [Test]
        public void LogicSources_DoNotReferenceUnityOrTheGame()
        {
            string src = SourceScan.SrcDirOrFail();
            var violations = new List<string>();
            int files = 0;

            foreach (string folder in new[] { Path.Combine(src, "Core", "Logic"), Path.Combine(src, "Core", "Compat") })
            {
                foreach (string path in SourceScan.Files(folder, ".cs"))
                {
                    files++;
                    string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                    var reportedLines = new HashSet<int>();
                    foreach (Regex rule in new[] { EngineUsing, EngineQualified })
                    {
                        foreach (Match match in rule.Matches(code))
                        {
                            int line = SourceScan.LineOf(code, match.Index);
                            if (reportedLines.Add(line)) violations.Add(SourceScan.Relative(src, path) + ":" + line + ": " + match.Value.Trim());
                        }
                    }
                }
            }

            Assert.True(files >= 20, "too few files under Core/Logic and Core/Compat (" + files + "): wrong source folder? " + src);
            Assert.True(violations.Count == 0,
                "Core/Logic/** and Core/Compat/** must compile without Unity, Harmony and the game (FR-21 item 4):\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void Sources_DoNotUseTheNetwork()
        {
            string src = SourceScan.SrcDirOrFail();
            var violations = new List<string>();
            int files = 0;

            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in NetworkUse.Matches(code))
                {
                    violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
                }
            }

            Assert.True(files > 0, "no sources under " + src);
            Assert.True(violations.Count == 0, "the mod must not touch the network (NFR-7):\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void StripScanner_IgnoresCommentsAndStrings()
        {
            // The two scans above rely on this: mentions in comments and messages are not violations, real code is.
            string text = "// using UnityEngine;\n" +
                          "/* UnityEngine.Mathf\n using TMPro; */\n" +
                          "var s = \"using UnityEngine; System.Net\";\n" +
                          "var v = @\"a \"\"UnityEngine.X\"\" b\";\n" +
                          "var r = \"\"\"raw UnityEngine.Y\"\"\";\n" +
                          "var c = '\"';\n" +
                          "using UnityEngine.UI;\n";

            string code = SourceScan.Strip(text, true);
            Assert.Equal(text.Length, code.Length, "stripping keeps offsets");
            Assert.Equal(text.Count(ch => ch == '\n'), code.Count(ch => ch == '\n'), "stripping keeps line numbers");

            MatchCollection usings = EngineUsing.Matches(code);
            Assert.Equal(1, usings.Count, "only the real using remains: " + code);
            Assert.Equal(8, SourceScan.LineOf(code, usings[0].Index));
            Assert.False(NetworkUse.IsMatch(code), "System.Net inside a string is not a use");

            string kept = SourceScan.Strip("var k = \"core.hub.title\"; // \"core.hidden.key\"", false);
            Assert.True(kept.Contains("\"core.hub.title\""), "strings are kept when asked");
            Assert.False(kept.Contains("core.hidden.key"), "comments are always removed");
        }

        [Test]
        public void EveryPublicLogicType_IsListedInCovers()
        {
            // The replacement of a line-coverage percentage (NFR-8): the same check as `build.py --tests --coverage-types`, run with the tests.
            Type[] types = typeof(ArchitectureTests).Assembly.GetExportedTypes();
            int needing = types.Count(TestRunner.NeedsCoverage);
            Assert.True(needing >= 20, "too few logic types found (" + needing + "): Core/Logic/** is not part of the test build?");

            List<Type> uncovered = TestRunner.FindUncovered(types);
            Assert.True(uncovered.Count == 0,
                "public types of Core/Logic/** without [Covers] on a test class that has [Test] methods:\n  " +
                string.Join("\n  ", uncovered.Select(t => t.FullName)));
        }

        [Test]
        public void ModVersionFile_HasOneVersionLine()
        {
            Assert.NotNull(TestEnv.ModDir, "mod folder not found");
            string path = Path.Combine(TestEnv.ModDir, "VERSION");
            Assert.True(File.Exists(path), "mod/VERSION is the single place of the version (FR-21 item 5)");

            string text = SourceScan.ReadText(path).TrimStart('﻿').Trim();
            Assert.True(VersionText.IsMatch(text), "mod/VERSION must look like 1.2.3 or 1.2.3-rc1, got '" + text + "'");
            Assert.False(text.Contains("\n"), "mod/VERSION must have one line");
        }

        [Test]
        public void ModSave_TakesItsVersionFromBuildInfo_WhenTheRuntimeClassExists()
        {
            // TSK-025 writes ModSave; this keeps the promise "mod/VERSION reaches ModSave.modVersion" from drifting once it exists.
            string src = SourceScan.SrcDirOrFail();
            List<string> files = SourceScan.Files(Path.Combine(src, "Core"), ".cs")
                .Where(p => !p.Replace('\\', '/').Contains("/Core/Logic/") && Path.GetFileName(p).Equals("ModSave.cs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.True(code.Contains("BuildInfo"),
                    SourceScan.Relative(src, path) + " must take modVersion from BuildInfo.Version (generated from mod/VERSION)");
            }
        }

        [Test]
        public void ResourceNames_FollowTheNamingRules()
        {
            // What build.py embeds, derived from the file tree: content/<feature>/<file>.json, l10n/<lang>.json, l10n/<lang>/<feature>.json.
            string src = SourceScan.SrcDirOrFail();
            List<string> names = ExpectedResourceNames(src);

            var problems = new List<string>();
            foreach (string name in names)
            {
                if (name.StartsWith("content/", StringComparison.Ordinal))
                {
                    string[] parts = name.Split('/');
                    bool ok = parts.Length == 3
                              && ContentMerger.BuiltinResourceName(parts[1], parts[2]) == name;
                    if (!ok) problems.Add(name + ": expected content/<feature>/<file>.json with plain names");
                }
                else
                {
                    string language, feature;
                    if (!L10nTables.TryParseTablePath(name, out language, out feature))
                        problems.Add(name + ": expected l10n/<lang>.json or l10n/<lang>/<feature>.json");
                }
                if (name.Contains(",")) problems.Add(name + ": a comma breaks the csc -resource argument");
            }
            Assert.True(problems.Count == 0, "bad resource names:\n  " + string.Join("\n  ", problems));

            foreach (string required in new[] { "content/mod-framework/core.json", "l10n/ru.json", "l10n/en.json" })
            {
                Assert.True(names.Contains(required), "the core resource is missing from the source tree: " + required);
            }
        }

        [Test]
        public void BuiltDll_HasNoNetworkReferences()
        {
            Assembly assembly;
            string reason;
            if (!BuiltDll.TryGet(out assembly, out reason))
            {
                Note.Skip("BuiltDll_HasNoNetworkReferences", reason);
                return;
            }

            AssemblyName[] references = assembly.GetReferencedAssemblies();
            Assert.True(references.Length > 0, "the references of PTMod.dll could not be read");

            List<string> bad = references
                .Select(r => r.Name)
                .Where(n => n.StartsWith("System.Net", StringComparison.OrdinalIgnoreCase)
                            || n.Equals("UnityEngine.Networking", StringComparison.OrdinalIgnoreCase)
                            || n.StartsWith("UnityEngine.UnityWebRequest", StringComparison.OrdinalIgnoreCase)
                            || n.StartsWith("UnityEngine.Multiplayer", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(bad.Count == 0, "PTMod.dll references network assemblies (NFR-7): " + string.Join(", ", bad));
        }

        [Test]
        public void BuiltDll_EmbedsContentAndL10nResources()
        {
            Assembly assembly;
            string reason;
            if (!BuiltDll.TryGet(out assembly, out reason))
            {
                Note.Skip("BuiltDll_EmbedsContentAndL10nResources", reason);
                return;
            }

            string src = SourceScan.SrcDirOrFail();
            string[] actual = assembly.GetManifestResourceNames();
            var actualJson = new HashSet<string>(
                actual.Where(n => n.StartsWith("content/", StringComparison.Ordinal) || n.StartsWith("l10n/", StringComparison.Ordinal)),
                StringComparer.Ordinal);

            foreach (string required in new[] { "content/mod-framework/core.json", "l10n/ru.json", "l10n/en.json" })
            {
                Assert.True(actualJson.Contains(required), "PTMod.dll does not embed " + required + "; embedded: " + string.Join(", ", actual));
            }

            // The whole tree, minus what a release build leaves out (framework-sample, decision D27).
            bool release = IsReleaseBuild(assembly);
            IEnumerable<string> expected = ExpectedResourceNames(src);
            if (release) expected = expected.Where(n => !IsReleaseExcluded(n));
            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);

            List<string> missing = expectedSet.Where(n => !actualJson.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            List<string> extra = actualJson.Where(n => !expectedSet.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, "files of Content/ and L10n/ that PTMod.dll does not embed: " + string.Join(", ", missing));
            Assert.True(extra.Count == 0, "resources of PTMod.dll without a file in Content/ or L10n/: " + string.Join(", ", extra));
        }

        [Test]
        public void BuiltDll_RegistersEveryFeatureOfTheTree_OnceAndOnlyThem()
        {
            // The final integration check (the owner of the build asks "is every feature in the DLL?"): each folder of Features/ has exactly one [ModFeature]
            // class in its namespace, no [ModFeature] class lives elsewhere, and the DLL embeds the strings (both languages) and the content of every feature.
            Assembly assembly;
            string reason;
            if (!BuiltDll.TryGet(out assembly, out reason))
            {
                Note.Skip("BuiltDll_RegistersEveryFeatureOfTheTree_OnceAndOnlyThem", reason);
                return;
            }

            string src = SourceScan.SrcDirOrFail();
            string featuresDir = Path.Combine(src, "Features");
            Assert.True(Directory.Exists(featuresDir), "no folder " + featuresDir);
            bool release = IsReleaseBuild(assembly);

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();   // the classes of the engine and of the game cannot be loaded for inspection; the features can
            }

            var featuresByNamespace = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Type type in types)
            {
                bool marked = CustomAttributeData.GetCustomAttributes(type).Any(a => a.AttributeType.FullName == "PTMod.Core.ModFeatureAttribute");
                if (!marked) continue;

                List<string> list;
                if (!featuresByNamespace.TryGetValue(type.Namespace ?? string.Empty, out list)) featuresByNamespace[type.Namespace ?? string.Empty] = list = new List<string>();
                list.Add(type.FullName);
            }

            var resources = new HashSet<string>(assembly.GetManifestResourceNames(), StringComparer.Ordinal);
            var problems = new List<string>();
            int features = 0;
            foreach (string directory in Directory.GetDirectories(featuresDir))
            {
                string pascal = Path.GetFileName(directory);
                if (release && pascal == "FrameworkSample") continue;   // left out of a release build (decision D27)

                features++;
                string slug = FeatureGraph.PascalToSlug(pascal);
                List<string> classes;
                if (!featuresByNamespace.TryGetValue("PTMod.Features." + pascal, out classes)) problems.Add(pascal + ": no [ModFeature] class in the namespace PTMod.Features." + pascal);
                else if (classes.Count != 1) problems.Add(pascal + ": " + classes.Count + " [ModFeature] classes (" + string.Join(", ", classes) + "), the core registers one per feature");

                foreach (string language in new[] { "ru", "en" })
                {
                    if (!resources.Contains("l10n/" + language + "/" + slug + ".json")) problems.Add(slug + ": the " + language + " strings (l10n/" + language + "/" + slug + ".json) are not embedded");
                }

                if (Directory.Exists(Path.Combine(src, "Content", slug)) && !resources.Any(n => n.StartsWith("content/" + slug + "/", StringComparison.Ordinal)))
                    problems.Add(slug + ": its content (content/" + slug + "/...) is not embedded");
            }

            foreach (string ns in featuresByNamespace.Keys)
            {
                string pascal = ns.StartsWith("PTMod.Features.", StringComparison.Ordinal) ? ns.Substring("PTMod.Features.".Length) : null;
                if (pascal == null || pascal.Contains(".") || !Directory.Exists(Path.Combine(featuresDir, pascal)))
                    problems.Add("a [ModFeature] class outside Features/<Name>/: " + string.Join(", ", featuresByNamespace[ns]));
            }

            Assert.True(features >= 15, "too few feature folders found (" + features + "): is the source folder right? " + featuresDir);
            Assert.True(problems.Count == 0, "features of the tree against PTMod.dll:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void BuiltDll_CarriesTheVersionOfTheVersionFile()
        {
            Assembly assembly;
            string reason;
            if (!BuiltDll.TryGet(out assembly, out reason))
            {
                Note.Skip("BuiltDll_CarriesTheVersionOfTheVersionFile", reason);
                return;
            }

            Assert.NotNull(TestEnv.ModDir, "mod folder not found");
            string version = SourceScan.ReadText(Path.Combine(TestEnv.ModDir, "VERSION")).TrimStart('﻿').Trim();
            Match m = VersionText.Match(version);
            Assert.True(m.Success, "mod/VERSION is not a version: '" + version + "'");
            string assemblyVersion = m.Groups[1].Value + "." + m.Groups[2].Value + "." + m.Groups[3].Value + ".0";

            Assert.Equal(assemblyVersion, assembly.GetName().Version.ToString(), "AssemblyVersion of PTMod.dll");

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(BuiltDll.FilePath);
            Assert.Equal(assemblyVersion, info.FileVersion, "AssemblyFileVersion of PTMod.dll");
            Assert.True(info.ProductVersion != null && info.ProductVersion.StartsWith(version + "+", StringComparison.Ordinal),
                "AssemblyInformationalVersion must start with '" + version + "+', got '" + info.ProductVersion + "'");

            // BuildInfo.Version is the constant ModSave and the log use.
            Type buildInfo = assembly.GetType("PTMod.Core.BuildInfo", false);
            Assert.NotNull(buildInfo, "PTMod.Core.BuildInfo (generated by build.py) is missing");
            FieldInfo field = buildInfo.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field, "BuildInfo.Version is missing");
            Assert.Equal(version, (string)field.GetRawConstantValue(), "BuildInfo.Version");
        }

        // ---- helpers ----

        private static bool IsReleaseBuild(Assembly assembly)
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(BuiltDll.FilePath);
            return info.ProductVersion != null && info.ProductVersion.EndsWith("+release", StringComparison.Ordinal);
        }

        /// <summary>The same rule as RELEASE_EXCLUDED in build.py: the service feature has no place in a release.</summary>
        private static bool IsReleaseExcluded(string resourceName)
        {
            string[] parts = resourceName.Split('/');
            if (parts[0] == "content" && parts.Length > 1 && parts[1].Equals("framework-sample", StringComparison.OrdinalIgnoreCase)) return true;
            return parts[0] == "l10n" && parts.Length == 3 && parts[2].Equals("framework-sample.json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Logical resource names of every JSON file in Content/ and L10n/, like build.py's collect_resources.</summary>
        internal static List<string> ExpectedResourceNames(string src)
        {
            var names = new List<string>();
            foreach (string folder in new[] { "Content", "L10n" })
            {
                string root = Path.Combine(src, folder);
                foreach (string path in SourceScan.Files(root, ".json"))
                {
                    names.Add(folder.ToLowerInvariant() + "/" + SourceScan.Relative(root, path));
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // core.json
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>TSK-043: Content/mod-framework/core.json is the table of design.md "Контент ядра" and holds every number the code reads from it.</summary>
    public class CoreContentTests
    {
        private static readonly string[] AllowedChannelMembers =
        {
            "name", "min", "max", "balanceMin", "balanceMax", "global", "perCompany", "perCpu", "perSegment", "cacheable", "owner",
        };

        // FR-25 item 4: the domains a channel name may start with.
        private static readonly string[] ChannelDomains =
        {
            "research", "production", "factory", "market", "popularity", "cpu", "contract", "bank", "taxes", "construction", "development",
            "gpu", "acquisition",
        };

        private sealed class Expected
        {
            public string Name;
            public double Min, Max, BalanceMin, BalanceMax;
            public string Dimensions;   // G global, C per company, P per CPU, S per segment
            public string Owner;
            public bool Cacheable = true;
        }

        private static Expected Ch(string name, double min, double max, string dimensions, string owner = null,
            double balanceMin = 0, double balanceMax = 0, bool cacheable = true)
        {
            return new Expected
            {
                Name = name, Min = min, Max = max, Dimensions = dimensions, Owner = owner,
                BalanceMin = balanceMin, BalanceMax = balanceMax, Cacheable = cacheable,
            };
        }

        /// <summary>The 27 registry entries of design.md, in the order of the table.</summary>
        private static List<Expected> DesignChannels()
        {
            return new List<Expected>
            {
                Ch("research.speed", 0.25, 4.0, "C"),
                Ch("research.cost", 0.25, 4.0, "C"),
                Ch("factory.operatingCost", 0.25, 4.0, "C"),
                Ch("construction.cost", 0.25, 4.0, "C"),
                Ch("development.cost", 0.25, 4.0, "C"),
                Ch("development.cost.design", 0.25, 4.0, "C", cacheable: false),
                Ch("taxes", 0.0, 4.0, "C"),
                Ch("bank.interest", 0.0, 4.0, "C"),
                Ch("bank.creditLimit", 0.1, 10.0, "GC"),
                Ch("production.unitCost", 0.25, 4.0, "CP"),
                Ch("production.output", 0.0, 3.0, "CP"),
                Ch("production.memoryCost", 0.5, 2.0, "G", "market-shocks"),
                Ch("production.waferCost", 0.6, 2.0, "G", "market-shocks"),
                Ch("cpu.failureRate", 0.0, 10.0, "CP"),
                Ch("market.demand.highend", 0.1, 3.0, "G"),
                Ch("market.demand.midrange", 0.1, 3.0, "G"),
                Ch("market.demand.lowend", 0.1, 3.0, "G"),
                Ch("market.demand.industries", 0.1, 3.0, "G"),
                Ch("market.rating.cpu", 0.0, 5.0, "CPS", balanceMin: 0.5, balanceMax: 2.0),
                Ch("popularity.gain", 0.0, 5.0, "CPS", balanceMin: 0.5, balanceMax: 2.0),
                Ch("contract.reward", 0.25, 4.0, "C"),
                Ch("contract.signChance", 0.0, 5.0, "CP", cacheable: false),
                Ch("gpu.demand", 0.1, 3.0, "G", "gpu-product-line"),
                Ch("gpu.demand.*", 0.1, 3.0, "G", "gpu-product-line"),
                Ch("gpu.rating", 0.25, 4.0, "C", "gpu-product-line"),
                Ch("gpu.vramCost", 0.5, 2.0, "GC", "gpu-product-line"),
                Ch("acquisition.price", 0.5, 3.0, "C", "rival-acquisitions"),
            };
        }

        /// <summary>The table "Соглашение об идентификаторах" (17 rows).</summary>
        private static Dictionary<string, string[]> DesignPrefixes()
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "mod-framework", new[] { "core" } },
                { "framework-sample", new[] { "smp" } },
                { "first-year-onboarding", new[] { "onb" } },
                { "contract-transparency", new[] { "ct" } },
                { "research-tree-ux", new[] { "rtux" } },
                { "bankruptcy-recovery", new[] { "rec" } },
                { "event-system", new[] { "es" } },
                { "market-shocks", new[] { "ms" } },
                { "design-flaw-recalls", new[] { "dfr" } },
                { "historical-events", new[] { "hist" } },
                { "press-reviews", new[] { "press" } },
                { "rival-personalities", new[] { "rival" } },
                { "rival-acquisitions", new[] { "acq" } },
                { "scenarios-achievements", new[] { "scn", "chl", "ach" } },
                { "platform-generations", new[] { "pg" } },
                { "cpu-architectures", new[] { "arch" } },
                { "gpu-product-line", new[] { "gpu" } },
            };
        }

        internal static JsonObject LoadCore()
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Content", "mod-framework", "core.json");
            Assert.True(File.Exists(path), "missing " + path);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), "core.json is not valid JSON: " + error);
            Assert.True(node is JsonObject, "core.json must be a JSON object");
            return (JsonObject)node;
        }

        [Test]
        public void CoreJson_LoadsThroughTheContentMerger_WithoutProblems()
        {
            string src = SourceScan.SrcDirOrFail();
            string text = SourceScan.ReadText(Path.Combine(src, "Content", "mod-framework", "core.json"));

            ConfigLoad load = ContentMerger.MergeConfig("mod-framework", "core.json", text, null, 1);
            Assert.True(load.Report.Errors.Count == 0, "core.json report: " + string.Join("; ", load.Report.Errors));
            Assert.True(load.Config.Count > 0, "core.json produced an empty config");
            Assert.Equal(1, load.Config.GetInt32("schema", -1), "schema");
            Assert.Equal("content/mod-framework/core.json", ContentMerger.BuiltinResourceName("mod-framework", "core.json"));
        }

        [Test]
        public void CoreJson_HasTheDesignSections()
        {
            JsonObject core = LoadCore();
            string[] sections = { "hooks", "log", "mail", "notify", "reputation", "profile", "segments", "performance", "prefixes", "channels" };
            foreach (string section in sections) Assert.True(core.ContainsKey(section), "core.json lacks the section '" + section + "'");

            Assert.Near(5, core.GetObject("hooks").GetDouble("faultThreshold", -1));

            JsonObject log = core.GetObject("log");
            Assert.Near(3, log.GetDouble("repeatLimit", -1));
            Assert.Near(60, log.GetDouble("repeatWindowSeconds", -1));
            Assert.Near(50, log.GetDouble("recentProblems", -1));

            JsonObject mail = core.GetObject("mail");
            Assert.Near(3, mail.GetDouble("maxDeliveredPerDay", -1));
            Assert.Near(2, mail.GetDouble("maxPerFeaturePerDay", -1));
            Assert.Near(200, mail.GetDouble("queueLimit", -1));
            Assert.Near(300, mail.GetDouble("maxStoredResolved", -1));

            JsonObject notify = core.GetObject("notify");
            Assert.Near(5, notify.GetDouble("maxPerFeaturePerDay", -1));
            Assert.Near(20, notify.GetDouble("queueLimit", -1));

            JsonObject reputation = core.GetObject("reputation");
            Assert.Near(50, reputation.GetDouble("historyPerCompany", -1));
            Assert.Near(0.03, reputation.GetDouble("driftMonthlyRate", -1));
            Assert.Near(50, reputation.GetDouble("driftTarget", -1));

            Assert.Near(2.0, core.GetObject("profile").GetDouble("saveThrottleSeconds", -1));
            Assert.Near(1.25, core.GetObject("segments").GetDouble("priceBand", -1));
            Assert.Near(2.0, core.GetObject("performance").GetDouble("warnMsPerDay", -1));
        }

        [Test]
        public void CoreJson_EveryNumberTheCodeReadsFromCore_Exists()
        {
            // ModContent.GetCoreNumber("section", "key", fallback): the names are literals in the code, so they can be checked.
            string src = SourceScan.SrcDirOrFail();
            JsonObject core = LoadCore();
            var call = new Regex("GetCoreNumber\\(\\s*\"(?<s>[A-Za-z0-9_]+)\"\\s*,\\s*\"(?<k>[A-Za-z0-9_]+)\"", RegexOptions.Compiled);

            var wanted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in call.Matches(code))
                {
                    string name = m.Groups["s"].Value + "." + m.Groups["k"].Value;
                    if (!wanted.ContainsKey(name)) wanted[name] = SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, m.Index);
                }
            }

            // The names TSK-021 depends on, whether or not the scan finds them.
            foreach (string required in new[] { "log.repeatLimit", "log.repeatWindowSeconds", "log.recentProblems", "profile.saveThrottleSeconds" })
            {
                if (!wanted.ContainsKey(required)) wanted[required] = "TSK-043";
            }

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> pair in wanted)
            {
                string[] parts = pair.Key.Split('.');
                JsonObject section = core.GetObject(parts[0]);
                double value;
                bool ok = section != null && section.TryGetDouble(parts[1], out value) && !double.IsNaN(value) && !double.IsInfinity(value);
                if (!ok) missing.Add(pair.Key + " (read at " + pair.Value + ")");
            }
            Assert.True(missing.Count == 0, "numbers the code reads with GetCoreNumber are missing in core.json:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void CoreJson_Channels_AreTheDesignRegistry()
        {
            JsonObject core = LoadCore();
            JsonArray channels = core.GetArray("channels");
            Assert.NotNull(channels, "core.json lacks the 'channels' array");

            List<Expected> expected = DesignChannels();
            Assert.Equal(27, expected.Count, "the design table has 27 entries");
            Assert.Equal(27, channels.Count, "the registry of the core has 27 entries (FR-12 item 15)");

            for (int i = 0; i < expected.Count; i++)
            {
                var entry = channels[i] as JsonObject;
                Assert.NotNull(entry, "channels[" + i + "] must be an object");
                Expected want = expected[i];
                string where = "channels[" + i + "] (" + want.Name + ")";

                Assert.Equal(want.Name, entry.GetString("name"), where + ": name / order");
                foreach (string member in entry.Keys)
                    Assert.True(AllowedChannelMembers.Contains(member), where + ": unknown member '" + member + "'");

                Assert.Near(want.Min, entry.GetDouble("min", double.NaN), 1e-9, where + ": min");
                Assert.Near(want.Max, entry.GetDouble("max", double.NaN), 1e-9, where + ": max");
                Assert.Near(want.BalanceMin, entry.GetDouble("balanceMin", 0), 1e-9, where + ": balanceMin");
                Assert.Near(want.BalanceMax, entry.GetDouble("balanceMax", 0), 1e-9, where + ": balanceMax");
                Assert.Equal(want.Dimensions.Contains("G"), entry.GetBool("global"), where + ": global");
                Assert.Equal(want.Dimensions.Contains("C"), entry.GetBool("perCompany"), where + ": perCompany");
                Assert.Equal(want.Dimensions.Contains("P"), entry.GetBool("perCpu"), where + ": perCpu");
                Assert.Equal(want.Dimensions.Contains("S"), entry.GetBool("perSegment"), where + ": perSegment");
                Assert.Equal(want.Cacheable, entry.GetBool("cacheable", true), where + ": cacheable");
                Assert.Equal(want.Owner, entry.GetString("owner"), where + ": owner");
            }
        }

        [Test]
        public void CoreJson_Channels_PassTheRegistryValidation()
        {
            JsonArray channels = LoadCore().GetArray("channels");
            var names = new HashSet<string>(StringComparer.Ordinal);
            int standard = 0, owned = 0, demand = 0, balanced = 0, uncached = 0;

            foreach (JsonNode node in channels)
            {
                var entry = (JsonObject)node;
                var spec = new ChannelSpec
                {
                    Name = entry.GetString("name"),
                    Min = (float)entry.GetDouble("min"),
                    Max = (float)entry.GetDouble("max"),
                    BalanceMin = (float)entry.GetDouble("balanceMin", 0),
                    BalanceMax = (float)entry.GetDouble("balanceMax", 0),
                    Cacheable = entry.GetBool("cacheable", true),
                    OwnerFeature = entry.GetString("owner"),
                };

                string error;
                Assert.True(spec.Validate(out error), error);
                Assert.True(names.Add(spec.Name), "duplicate channel '" + spec.Name + "'");
                Assert.True(ChannelDomains.Contains(spec.Name.Split('.')[0]), "channel '" + spec.Name + "' has a domain outside FR-25 item 4");

                if (spec.OwnerFeature == null) standard++; else owned++;
                if (spec.Name.StartsWith("market.demand.", StringComparison.Ordinal)) demand++;
                if (spec.HasBalanceRange) balanced++;
                if (!spec.Cacheable) uncached++;
            }

            Assert.Equal(20, standard, "19 standard channels + development.cost.design");
            Assert.Equal(7, owned, "7 channels of features declared ahead (D21)");
            Assert.Equal(4, demand, "market.demand.<segment> for the four segments");
            Assert.Equal(2, balanced, "the balance range exists only on market.rating.cpu and popularity.gain");
            Assert.Equal(2, uncached, "development.cost.design and contract.signChance are not cached");
        }

        [Test]
        public void CoreJson_Prefixes_AreTheIdentifierTable()
        {
            JsonObject prefixes = LoadCore().GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");

            Dictionary<string, string[]> expected = DesignPrefixes();
            Assert.Equal(17, expected.Count, "the design table has 17 rows");
            Assert.Equal(17, prefixes.Count, "prefixes has 17 rows (FR-25 item 1)");

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> row in expected)
            {
                var list = prefixes.GetArray(row.Key);
                Assert.NotNull(list, "prefixes lacks the feature '" + row.Key + "'");
                var actual = new List<string>();
                foreach (JsonNode item in list)
                {
                    var value = item as JsonValue;
                    Assert.True(value != null && value.Kind == JsonKind.String, "prefixes['" + row.Key + "'] must hold strings");
                    actual.Add(value.StringValue);
                }
                Assert.Equal(row.Value, actual.ToArray(), "prefixes['" + row.Key + "']");

                foreach (string prefix in actual)
                {
                    Assert.True(prefix.Length > 0 && prefix.All(ch => ch >= 'a' && ch <= 'z'), "prefix '" + prefix + "' must be lower-case letters");
                    string owner;
                    Assert.False(seen.TryGetValue(prefix, out owner), "prefix '" + prefix + "' belongs to both '" + owner + "' and '" + row.Key + "'");
                    seen[prefix] = row.Key;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // L10n
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>TSK-043: the built-in string tables are complete in both languages and cover every core.* key the code uses.</summary>
    public class L10nContentTests
    {
        private sealed class TableFile
        {
            public string Relative;   // l10n/ru.json
            public string Language;
            public string Feature;    // null for l10n/<lang>.json
            public string Text;
        }

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

        private static readonly Regex CoreKeyLiteral = new Regex("\"(?<k>core(?:\\.[A-Za-z0-9_\\-]+){2,})\"", RegexOptions.Compiled);

        private static readonly string[] PluralSuffixes = { ".one", ".few", ".many", ".other" };

        private static List<TableFile> LoadFiles(List<string> problems)
        {
            string src = SourceScan.SrcDirOrFail();
            string root = Path.Combine(src, "L10n");
            var result = new List<TableFile>();
            foreach (string path in SourceScan.Files(root, ".json"))
            {
                string relative = "l10n/" + SourceScan.Relative(root, path);
                string language, feature;
                if (!L10nTables.TryParseTablePath(relative, out language, out feature))
                {
                    problems.Add(relative + ": not a table name (l10n/<lang>.json or l10n/<lang>/<feature>.json)");
                    continue;
                }
                result.Add(new TableFile { Relative = relative, Language = language, Feature = feature, Text = SourceScan.ReadText(path) });
            }
            // Same order as ModL10n: language files first, then the feature files.
            return result.OrderBy(f => f.Feature == null ? 0 : 1).ThenBy(f => f.Relative, StringComparer.Ordinal).ToList();
        }

        /// <summary>All built-in tables loaded like ModL10n loads them; the problems of the files go to <paramref name="problems"/>.</summary>
        private static L10nTables LoadTables(List<string> problems)
        {
            var tables = new L10nTables();
            foreach (TableFile file in LoadFiles(problems))
            {
                tables.AddJson(file.Language, file.Relative, file.Text, problems);
            }
            return tables;
        }

        private static L10nTables LoadTablesOrFail()
        {
            var problems = new List<string>();
            L10nTables tables = LoadTables(problems);
            Assert.True(problems.Count == 0, "L10n files have problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }

        [Test]
        public void L10n_Files_ExistAndAreValidTables()
        {
            var problems = new List<string>();
            List<TableFile> files = LoadFiles(problems);
            Assert.True(problems.Count == 0, "L10n files have bad names:\n  " + string.Join("\n  ", problems));

            foreach (string required in new[] { "l10n/ru.json", "l10n/en.json" })
            {
                Assert.True(files.Any(f => f.Relative == required), "missing " + required);
            }

            L10nTables tables = LoadTables(problems);
            Assert.True(problems.Count == 0, "L10n files do not load cleanly:\n  " + string.Join("\n  ", problems));
            Assert.True(tables.Count("ru") >= 50, "ru table is too small: " + tables.Count("ru"));
            Assert.True(tables.Count("en") >= 50, "en table is too small: " + tables.Count("en"));
        }

        [Test]
        public void L10n_RuAndEn_HaveTheSameKeys_AndCompletePluralForms()
        {
            L10nTables tables = LoadTablesOrFail();
            L10nMismatchReport report = tables.Compare("ru", "en");

            var lines = new List<string>(report.ToLines());
            Assert.True(lines.Count == 0, "ru and en differ (NFR-5):\n  " + string.Join("\n  ", lines));
        }

        [Test]
        public void L10n_Keys_CarryThePrefixOfTheirFeature()
        {
            L10nTables tables = LoadTablesOrFail();
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");

            var problems = new List<string>();
            foreach (TableFile file in LoadFiles(new List<string>()))
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(file.Text, out node, out error), file.Relative + ": " + error);

                // l10n/<lang>/<feature>.json belongs to the feature (FR-25 item 2). The shared l10n/<lang>.json holds the core's strings
                // and, by D15, the strings the feature specs put there too, so it takes any prefix of the prefixes table - the same rule
                // as `install.py --check` (ptru/mods.py), which also names the file the key belongs in.
                var allowedPrefixes = new List<string>();
                if (file.Feature == null)
                {
                    foreach (KeyValuePair<string, JsonNode> row in prefixes)
                    {
                        var rowPrefixes = row.Value as JsonArray;
                        if (rowPrefixes == null) continue;
                        foreach (JsonNode item in rowPrefixes) allowedPrefixes.Add(((JsonValue)item).StringValue + ".");
                    }
                }
                else
                {
                    JsonArray allowed = prefixes.GetArray(file.Feature);
                    if (allowed == null)
                    {
                        problems.Add(file.Relative + ": the feature '" + file.Feature + "' is not in the prefixes table of core.json");
                        continue;
                    }
                    foreach (JsonNode item in allowed) allowedPrefixes.Add(((JsonValue)item).StringValue + ".");
                }

                foreach (KeyValuePair<string, JsonNode> pair in (JsonObject)node)
                {
                    if (!allowedPrefixes.Any(p => pair.Key.StartsWith(p, StringComparison.Ordinal)))
                        problems.Add(file.Relative + ": key '" + pair.Key + "' lacks the prefix " + string.Join(" / ", allowedPrefixes));
                }
            }
            Assert.True(problems.Count == 0, "keys outside the prefixes of their feature:\n  " + string.Join("\n  ", problems));
            Assert.True(tables.Contains("ru", "core.hub.title"), "sanity: core.hub.title");
        }

        [Test]
        public void L10n_Placeholders_MatchBetweenLanguages_AndFormatCleanly()
        {
            L10nTables tables = LoadTablesOrFail();
            IReadOnlyList<string> ruKeys = tables.Keys("ru");
            IReadOnlyList<string> enKeys = tables.Keys("en");

            var problems = new List<string>();
            var ruSets = PlaceholderSets(tables, "ru", ruKeys);
            var enSets = PlaceholderSets(tables, "en", enKeys);

            foreach (KeyValuePair<string, string> pair in ruSets)
            {
                string other;
                if (enSets.TryGetValue(pair.Key, out other) && other != pair.Value)
                    problems.Add(pair.Key + ": ru has placeholders [" + pair.Value + "], en has [" + other + "]");
            }

            // Every text must survive string.Format with arguments (a stray brace would throw), like ModL10n does it.
            object[] args = { "a", "b", "c", "d", "e", "f", "g", "h" };
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in tables.Keys(language))
                {
                    string text;
                    tables.TryGet(language, key, out text);
                    if (string.IsNullOrWhiteSpace(text)) problems.Add(language + ":" + key + ": empty text");
                    try
                    {
                        string.Format(CultureInfo.InvariantCulture, text, args);
                    }
                    catch (FormatException ex)
                    {
                        problems.Add(language + ":" + key + ": bad format: " + ex.Message);
                    }
                    if (key.StartsWith("core.", StringComparison.Ordinal) && text.IndexOf('|') >= 0)
                        problems.Add(language + ":" + key + ": '|' is the inline plural separator; use key.one/.few/.many");
                }
            }
            Assert.True(problems.Count == 0, "placeholder problems:\n  " + string.Join("\n  ", problems));
        }

        private static Dictionary<string, string> PlaceholderSets(L10nTables tables, string language, IReadOnlyList<string> keys)
        {
            var union = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            var keyList = keys.ToList();
            foreach (string key in keyList)
            {
                string text;
                tables.TryGet(language, key, out text);
                string logical = PluralBase(keyList, key);
                SortedSet<int> set;
                if (!union.TryGetValue(logical, out set))
                {
                    set = new SortedSet<int>();
                    union[logical] = set;
                }
                foreach (Match m in Placeholder.Matches(text)) set.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return union.ToDictionary(p => p.Key, p => string.Join(",", p.Value), StringComparer.Ordinal);
        }

        /// <summary>The plural base of a key that is one of several forms of a group, otherwise the key itself.</summary>
        private static string PluralBase(List<string> keys, string key)
        {
            string baseKey;
            PluralForm form;
            if (!L10nTables.TrySplitPluralKey(key, out baseKey, out form)) return key;
            int forms = PluralSuffixes.Count(s => keys.Contains(baseKey + s));
            return forms > 1 || form == PluralForm.Few || form == PluralForm.Many ? baseKey : key;
        }

        [Test]
        public void L10n_DesignFragment_KeysAndTexts_ArePresent()
        {
            // The fragment of design.md "Строки L10n/ru.json" and the en lines under it.
            var ru = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "core.hub.title", "Моды" },
                { "core.hub.tab.features", "Фичи" },
                { "core.hub.tab.diagnostics", "Диагностика" },
                { "core.hub.tab.about", "О моде" },
                { "core.feature.state.enabled", "Включена" },
                { "core.feature.state.disabled", "Выключена" },
                { "core.feature.state.faulted", "Ошибка — отключена до перезапуска" },
                { "core.feature.state.unavailable", "Недоступна: {0}" },
                { "core.feature.reloadRequired", "Изменение вступит в силу после загрузки сейва" },
                { "core.save.status.loaded", "Загружен" },
                { "core.save.status.missing", "Нет файла модов (старый сейв)" },
                { "core.save.corrupt", "Файл состояния модов «{0}» повреждён. Использованы значения по умолчанию, копия: {1}" },
                { "core.save.newer", "Сейв создан более новой версией мода ({0}). Часть данных модов недоступна, но сохранится без изменений." },
                { "core.save.writeFailed", "Состояние модов не сохранено: {0}" },
                { "core.mail.answer", "Ваш ответ: {0}" },
                { "core.mail.answerTimeout", "Ответ по истечении срока: {0}" },
                { "core.mail.featureOff", "Фича «{0}» выключена — ответить нельзя" },
                { "core.mail.daysLeft.one", "Остался {0} день" },
                { "core.mail.daysLeft.few", "Осталось {0} дня" },
                { "core.mail.daysLeft.many", "Осталось {0} дней" },
                { "core.selfcheck.failed", "Моды: самопроверка нашла ошибок — {0}. Подробности: «Моды → Диагностика»" },
                { "core.mail.statusQueued", "Письмо ожидает доставки" },
                { "core.newgame.button", "Моды…" },
                { "core.newgame.title", "Параметры модов" },
                { "core.newgame.sectionError", "Раздел «{0}» не загружен — ошибка в логе" },
                { "core.explain.capped", "(ограничено)" },
                { "core.hub.confirm.title", "Выключить фичу?" },
                { "core.hub.confirm.default", "Выключить «{0}»?" },
                { "core.selfcheck.channelUndeclared", "Канал «{0}» используется, но не объявлен в реестре" },
                { "core.selfcheck.predicateRejected", "Предикат «{0}» отклонён: нет префикса «{1}.»" },
                { "core.profile.corrupt", "Файл профиля модов повреждён. Данные восстановлены из копии: {0}" },
                { "core.profile.reset", "Файл профиля модов повреждён и не восстановлен: начат пустой профиль. Повреждённый файл сохранён как {0}" },
                { "core.common.yes", "Да" },
                { "core.common.no", "Нет" },
                { "core.common.cancel", "Отмена" },
            };
            var en = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "core.hub.title", "Mods" },
                { "core.feature.state.faulted", "Error — disabled until restart" },
                { "core.mail.answer", "Your answer: {0}" },
                { "core.mail.daysLeft.one", "{0} day left" },
                { "core.mail.daysLeft.other", "{0} days left" },
                { "core.explain.capped", "(capped)" },
                { "core.newgame.button", "Mods…" },
                { "core.profile.reset", "The mod profile was damaged and could not be restored: a new empty profile was started. The damaged file was saved as {0}" },
            };

            L10nTables tables = LoadTablesOrFail();
            var problems = new List<string>();
            CheckTexts(tables, "ru", ru, problems);
            CheckTexts(tables, "en", en, problems);

            // Every ru key of the fragment has an en counterpart (plural groups by base).
            foreach (string key in ru.Keys)
            {
                string baseKey;
                PluralForm form;
                bool plural = L10nTables.TrySplitPluralKey(key, out baseKey, out form) && ru.ContainsKey(baseKey + ".few");
                bool present = plural ? PluralSuffixes.Any(s => tables.Contains("en", baseKey + s)) : tables.Contains("en", key);
                if (!present) problems.Add("en lacks " + key);
            }
            Assert.True(problems.Count == 0, "the design fragment and the tables differ:\n  " + string.Join("\n  ", problems));
        }

        private static void CheckTexts(L10nTables tables, string language, Dictionary<string, string> expected, List<string> problems)
        {
            foreach (KeyValuePair<string, string> pair in expected)
            {
                string actual;
                if (!tables.TryGet(language, pair.Key, out actual)) problems.Add(language + " lacks " + pair.Key);
                else if (actual != pair.Value) problems.Add(language + ":" + pair.Key + " is '" + actual + "', the design says '" + pair.Value + "'");
            }
        }

        [Test]
        public void L10n_Plural_MailDaysLeft_PicksTheRightForm()
        {
            L10nTables tables = LoadTablesOrFail();
            var resolver = new LocTextResolver(tables);

            resolver.Language = "ru";
            Assert.Equal("Остался 1 день", resolver.Plural("core.mail.daysLeft", 1));
            Assert.Equal("Осталось 2 дня", resolver.Plural("core.mail.daysLeft", 2));
            Assert.Equal("Осталось 5 дней", resolver.Plural("core.mail.daysLeft", 5));
            Assert.Equal("Осталось 11 дней", resolver.Plural("core.mail.daysLeft", 11));
            Assert.Equal("Остался 21 день", resolver.Plural("core.mail.daysLeft", 21));
            Assert.Equal("Осталось 22 дня", resolver.Plural("core.mail.daysLeft", 22));
            Assert.Equal("Осталось 111 дней", resolver.Plural("core.mail.daysLeft", 111));

            resolver.Language = "en";
            Assert.Equal("1 day left", resolver.Plural("core.mail.daysLeft", 1));
            Assert.Equal("2 days left", resolver.Plural("core.mail.daysLeft", 2));
            Assert.Equal("0 days left", resolver.Plural("core.mail.daysLeft", 0));
        }

        [Test]
        public void L10n_EveryCoreKeyUsedInTheCode_IsDefinedInBothLanguages()
        {
            // The check ModSelfCheck makes at runtime, made at build time: a literal "core.x.y" in the code that no table defines
            // would show as [core.x.y] in the game. Keys built from pieces ("core.hub.tab." + name) cannot be seen here.
            string src = SourceScan.SrcDirOrFail();
            L10nTables tables = LoadTablesOrFail();

            var used = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in CoreKeyLiteral.Matches(code))
                {
                    string key = m.Groups["k"].Value;
                    if (!used.ContainsKey(key)) used[key] = SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, m.Index);
                }
            }

            // Sanity: the code already uses these (TSK-021 adapters, ModifierBreakdown); a scan that misses them is broken.
            foreach (string known in new[] { ModifierBreakdown.CappedKey, "core.profile.corrupt", "core.profile.reset" })
            {
                Assert.True(used.ContainsKey(known), "the scan did not find '" + known + "' in the sources: is the scanner broken?");
            }

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> pair in used)
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    bool defined = tables.Contains(language, pair.Key) || PluralSuffixes.Any(s => tables.Contains(language, pair.Key + s));
                    if (!defined) missing.Add(language + ": " + pair.Key + " (used at " + pair.Value + ")");
                }
            }
            Assert.True(missing.Count == 0,
                "core.* keys used by the code are missing in the tables (add them to L10n/ru.json and L10n/en.json):\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void ContentFiles_AreValidJson()
        {
            string src = SourceScan.SrcDirOrFail();
            string root = Path.Combine(src, "Content");
            List<string> files = SourceScan.Files(root, ".json");
            Assert.True(files.Count >= 1, "no content files under " + root);

            var problems = new List<string>();
            foreach (string path in files)
            {
                JsonNode node;
                JsonError error;
                string relative = SourceScan.Relative(root, path);
                if (!ModJson.TryParse(SourceScan.ReadText(path), out node, out error)) problems.Add(relative + ": " + error);
                else if (!(node is JsonObject)) problems.Add(relative + ": the root must be a JSON object");
                else if (((JsonObject)node).GetInt32("schema", 0) < 1) problems.Add(relative + ": 'schema' is missing (FR-10)");
            }
            Assert.True(problems.Count == 0, "content files with problems:\n  " + string.Join("\n  ", problems));
        }
    }
}
