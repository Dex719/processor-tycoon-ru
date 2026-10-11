using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PTMod.Core.Logic.Json;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// Helpers shared by the tests of design-flaw-recalls. Internal, so the runner never discovers it. The files are read from the source tree
    /// (<c>TestEnv.SrcDir</c>), like the architecture tests do.
    /// </summary>
    internal static class FlawTestKit
    {
        public const string Feature = "design-flaw-recalls";

        /// <summary>Text of a file of the source tree, relative to <c>mod/src/PTMod</c>.</summary>
        public static string ReadSource(string relative)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The shipped <c>flaws.json</c>, as text.</summary>
        public static string ShippedFlawsText()
        {
            return ReadSource("Content/design-flaw-recalls/flaws.json");
        }

        /// <summary>The shipped <c>flaws.json</c>, parsed; a fresh tree each call, so a test may change it.</summary>
        public static JsonObject ShippedFlaws()
        {
            return ParseObject(ShippedFlawsText());
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node = ParseNode(text);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "a JSON object was expected: " + text);
            return obj;
        }

        public static JsonNode ParseNode(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error + " in " + text);
            return node;
        }

        /// <summary>Sets the member at a dotted path (<c>risk.pMax</c>; an index is allowed for arrays: <c>risk.difficultyMultiplier.2</c>).</summary>
        public static void Set(JsonObject root, string path, JsonNode value)
        {
            string[] parts = path.Split('.');
            JsonNode current = root;
            for (int i = 0; i < parts.Length - 1; i++) current = Child(current, parts[i]);

            string last = parts[parts.Length - 1];
            var obj = current as JsonObject;
            if (obj != null)
            {
                obj.Set(last, value);
                return;
            }

            var array = (JsonArray)current;
            int index = int.Parse(last);
            array.RemoveAt(index);
            array.Insert(index, value);
        }

        /// <summary>Removes the member at a dotted path.</summary>
        public static void Remove(JsonObject root, string path)
        {
            string[] parts = path.Split('.');
            JsonNode current = root;
            for (int i = 0; i < parts.Length - 1; i++) current = Child(current, parts[i]);
            Assert.True(((JsonObject)current).Remove(parts[parts.Length - 1]), "nothing to remove at " + path);
        }

        private static JsonNode Child(JsonNode node, string key)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                JsonNode child = obj[key];
                Assert.NotNull(child, "no member '" + key + "'");
                return child;
            }

            JsonNode item;
            Assert.True(((JsonArray)node).TryGetValue(int.Parse(key), out item), "no element " + key);
            return item;
        }

        /// <summary>
        /// Where two objects differ, as lines "path: expected X, actual Y". Compares the public fields recursively; floats and integers exactly,
        /// arrays and lists element by element. Empty when equal.
        /// </summary>
        public static List<string> Diff(object expected, object actual)
        {
            var lines = new List<string>();
            Compare(expected, actual, "$", lines);
            return lines;
        }

        private static void Compare(object expected, object actual, string path, List<string> lines)
        {
            if (expected == null || actual == null)
            {
                if (expected != actual) lines.Add(path + ": expected " + (expected ?? "null") + ", actual " + (actual ?? "null"));
                return;
            }

            Type type = expected.GetType();
            if (type != actual.GetType())
            {
                lines.Add(path + ": expected type " + type.Name + ", actual " + actual.GetType().Name);
                return;
            }

            if (type.IsPrimitive || type.IsEnum || expected is string || expected is decimal)
            {
                if (!expected.Equals(actual)) lines.Add(path + ": expected " + expected + ", actual " + actual);
                return;
            }

            var listA = expected as IList;
            if (listA != null)
            {
                var listB = (IList)actual;
                if (listA.Count != listB.Count)
                {
                    lines.Add(path + ": expected " + listA.Count + " element(s), actual " + listB.Count);
                    return;
                }
                for (int i = 0; i < listA.Count; i++) Compare(listA[i], listB[i], path + "[" + i + "]", lines);
                return;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Compare(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name, lines);
            }
        }

        /// <summary>The first <c>```json</c> block after the line that contains <paramref name="headingPart"/> in the spec, or null when the spec is not there.</summary>
        public static string DesignJsonBlock(string headingPart)
        {
            if (TestEnv.ModDir == null) return null;
            string path = Path.GetFullPath(Path.Combine(TestEnv.ModDir, "..", ".kiro", "specs", Feature, "design.md"));
            if (!File.Exists(path)) return null;

            string[] lines = SourceScan.ReadText(path).Replace("\r\n", "\n").Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length && start < 0; i++)
            {
                if (lines[i].StartsWith("#", StringComparison.Ordinal) && lines[i].Contains(headingPart)) start = i;
            }
            if (start < 0) return null;

            int open = -1;
            for (int i = start + 1; i < lines.Length && open < 0; i++)
            {
                if (lines[i].StartsWith("```json", StringComparison.Ordinal)) open = i;
            }
            if (open < 0) return null;

            var block = new List<string>();
            for (int i = open + 1; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++) block.Add(lines[i]);
            return string.Join("\n", block);
        }

        /// <summary>The example section of the side save file from design.md (save section), without a version field.</summary>
        public static string DesignSaveExample()
        {
            return DesignSaveExampleText;
        }

        // The example of design.md (save section) (a copy: the test of the design example compares it with the spec when the spec is at hand).
        private const string DesignSaveExampleText = @"{
  ""nextRecordId"": 4,
  ""lastActiveDay"": 17010,
  ""rolledCpuIds"": [1012, 1013, 2044, 2051],
  ""rushCpuIds"": [2051],
  ""rushProjects"": [ { ""companyKey"": ""player"", ""cpuName"": ""Nova 3"", ""plannedEndDay"": 17240 } ],
  ""records"": [
    {
      ""id"": 3, ""companyKey"": ""player"", ""baseCpuId"": 2044, ""cpuIds"": [2044, 2059],
      ""typeId"": ""security"", ""severity"": 1, ""state"": 4, ""source"": 2, ""response"": 3,
      ""coverUp"": false, ""automaticChoice"": false, ""suitFiled"": false, ""quietStepping"": false,
      ""riskAtRelease"": 0.167, ""inflationAtRelease"": 6.6,
      ""releaseDay"": 16860, ""discoveredDay"": 17167, ""publicSinceDay"": 17167,
      ""unitsAtDiscovery"": 3200000,
      ""quote"": {
        ""RecallCost"": 254600000, ""RecallCapped"": false,
        ""SteppingCost"": 30000000, ""SteppingDays"": 90, ""SteppingDailyCost"": 333333.3,
        ""ResidualClaimsCost"": 72000000,
        ""PatchAvailable"": true, ""PatchCost"": 7250000, ""PatchIpsLoss"": 0.12, ""PatchMonths"": 18,
        ""ClassActionCost"": 57600000, ""OemCancelChance"": 0.3, ""IgnoreSignChanceMultiplier"": 0.85,
        ""ReputationRecall"": 2, ""ReputationStepping"": -1, ""ReputationPatch"": -2, ""ReputationIgnore"": -8,
        ""AffectedUnits"": 3200000
      },
      ""steppingEndDay"": 0, ""lastChargedDay"": 0, ""steppingDailyCost"": 0,
      ""mitigationEndDay"": 17707, ""ipsLoss"": 0.12,
      ""ignoreDrainUntilDay"": 0, ""leakTailUntilDay"": 0,
      ""modifierEntryIds"": [""m-000017""],
      ""spentTotal"": 7250000
    }
  ],
  ""pendingDecisionQueue"": []
}";
    }
}
