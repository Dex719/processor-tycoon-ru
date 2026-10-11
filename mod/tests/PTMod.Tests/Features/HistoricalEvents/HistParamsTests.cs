using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The settings and the content numbers of the feature (TSK-001, TSK-002; FR-17, NFR-4): the declarations of the parameters, <c>params.json</c> as
    /// the design gives it, the strings of the hub, and the skeleton of the entry class (checked on its source, because the class needs the game).
    /// </summary>
    [Covers(typeof(HistParamSpecs), typeof(HistMode), typeof(HistContentParams), typeof(AcquisitionAntitrustParams))]
    public class HistParamsTests
    {
        private const string Feature = "historical-events";

        /// <summary>The example of "Параметры: Content/historical-events/params.json" in design.md.</summary>
        private const string DesignParams =
            "{ 'schema': 1, 'majorSpacingDays': 45, 'shareCacheMonths': 36, 'aiChooserPriority': 10," +
            "  'settlementDefaults': { 'legalCostPct': 0.004, 'months': 12, 'pWin': 0.7, 'settlementPct': 0.03 }," +
            "  'disputeDefaults': { 'legalCostPct': 0.002, 'arbitrationMonths': 48, 'pWinArbitration': 0.6," +
            "                       'licenseFeePct': 0.0125, 'licenseMonths': 36, 'researchCostMultiplier': 1.3," +
            "                       'pWinInfringement': 0.55, 'settlePct': 0.03, 'redesignMultiplier': 1.2, 'redesignMonths': 24," +
            "                       'plaintiffSettlementPct': 0.03 }," +
            "  'priceWarDefaults': { 'floorMargin': 0.05, 'marketingCostPct': 0.01, 'marketingPopularityGain': 1.10, 'marketingDays': 180 }," +
            "  'acquisitionAntitrust': { 'softShare': 0.40, 'slope': 2.5, 'caseSurcharge': 1.25, 'maxMultiplier': 3.0, 'shareMonths': 36 }," +
            "  'aiWeightOverrides': {} }";

        // ---- the parameters of the hub ----

        [Test]
        public void Params_AreTheSeventeenOfFR17_InTheOrderOfThePage()
        {
            ParamSpec[] specs = HistParamSpecs.Create(Feature);

            Assert.Equal(
                new[]
                {
                    "mode", "intensity",
                    "macro", "boom", "shock", "disaster", "antitrust", "export", "priceWar", "patent", "flaw", "race", "brand", "oem",
                    "historyNotes", "spoilers", "debug",
                },
                specs.Select(s => s.Key).ToArray());
            Assert.Equal(17, specs.Length);
            foreach (ParamSpec spec in specs) Assert.Equal(Feature, spec.FeatureId, spec.Key);
        }

        [Test]
        public void ModeParam_IsZeroOrOne_AdaptiveByDefault()
        {
            ParamSpec mode = Find("mode");
            Assert.Equal(ParamType.Int, mode.Type);
            Assert.Equal(0, mode.Default);
            Assert.Equal(0, mode.Min);
            Assert.Equal(1, mode.Max);
            Assert.Equal(1, mode.Step);
            Assert.Equal((int)HistMode.Adaptive, 0);
            Assert.Equal((int)HistMode.Strict, 1);
            Assert.Equal(1, mode.Clamp(5), "a value above the range is brought into it");
        }

        [Test]
        public void IntensityParam_IsHalfToOneAndAHalf_OneByDefault()
        {
            ParamSpec intensity = Find("intensity");
            Assert.Equal(ParamType.Float, intensity.Type);
            Assert.Near(1.0, intensity.Default);
            Assert.Near(0.5, intensity.Min);
            Assert.Near(1.5, intensity.Max);
            Assert.Near(0.5, intensity.Clamp(0.1));
            Assert.Near(1.5, intensity.Clamp(9));
            Assert.Near(1.0, HistParamSpecs.IntensityDefault);
            Assert.Near(0.5, HistParamSpecs.IntensityMin);
            Assert.Near(1.5, HistParamSpecs.IntensityMax);
        }

        [Test]
        public void CategorySwitches_AreOn_ExceptDisaster()
        {
            foreach (string category in HistCategories.All)
            {
                ParamSpec spec = Find(category);
                Assert.Equal(ParamType.Bool, spec.Type, category);
                Assert.Equal(category == "disaster" ? 0.0 : 1.0, spec.Default, category + " default");
            }
        }

        [Test]
        public void TheOtherSwitches_HaveTheirDefaults()
        {
            Assert.Equal(1.0, Find("historyNotes").Default);
            Assert.Equal(0.0, Find("spoilers").Default);
            Assert.Equal(0.0, Find("debug").Default);
            foreach (string key in new[] { "historyNotes", "spoilers", "debug" }) Assert.Equal(ParamType.Bool, Find(key).Type, key);
        }

        [Test]
        public void EverySpec_IsAcceptedByTheCore_AndNeedsNoRestart()
        {
            foreach (ParamSpec spec in HistParamSpecs.Create(Feature))
            {
                string problem;
                Assert.True(spec.Normalize(out problem), spec.Key + ": " + problem);
                Assert.True(spec.InRange(spec.Default), spec.Key + ": the default is inside the range");
                Assert.False(spec.RequiresReload, spec.Key);
                Assert.Equal("hist.param." + spec.Key + ".label", spec.LabelKey);
                Assert.Equal("hist.param." + spec.Key + ".tooltip", spec.TooltipKey);
            }
        }

        [Test]
        public void Create_GivesFreshObjectsEveryTime()
        {
            ParamSpec[] first = HistParamSpecs.Create(Feature);
            ParamSpec[] second = HistParamSpecs.Create(Feature);
            Assert.False(ReferenceEquals(first[0], second[0]), "a ParamSpec is a mutable object the core copies");
            first[0].Default = 1;
            Assert.Equal(0.0, second[0].Default);
        }

        // ---- the strings of the hub ----

        [Test]
        public void TheStringTables_HaveEverythingTheHubAsksFor_InBothLanguages()
        {
            Dictionary<string, string> ru = LoadTable("ru");
            Dictionary<string, string> en = LoadTable("en");

            Assert.Equal(LogicalKeys(ru), LogicalKeys(en), "ru and en have the same keys (a plural group is one key: ru has one, few, many, en has one, other)");

            var needed = new List<string> { "hist.feature.name", "hist.feature.desc" };
            foreach (ParamSpec spec in HistParamSpecs.Create(Feature))
            {
                needed.Add(spec.LabelKey);
                needed.Add(spec.TooltipKey);
            }

            foreach (string key in needed)
            {
                Assert.True(ru.ContainsKey(key) && ru[key].Trim().Length > 0, "ru lacks " + key);
                Assert.True(en.ContainsKey(key) && en[key].Trim().Length > 0, "en lacks " + key);
            }

            foreach (string key in ru.Keys) Assert.True(key.StartsWith("hist.", StringComparison.Ordinal), "every key carries the prefix of the feature: " + key);
        }

        // ---- params.json ----

        [Test]
        public void ParamsJson_IsTheExampleOfTheDesign()
        {
            JsonObject shipped = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", "params.json")));
            JsonObject design = HistFixture.Obj(DesignParams);

            Assert.True(design.Equals(shipped), "params.json must equal the example of design.md, including acquisitionAntitrust:\n  " + shipped);
        }

        [Test]
        public void ParamsJson_ReadsIntoTheNumbersOfTheCode()
        {
            var problems = new List<string>();
            HistContentParams p = HistContentParams.FromJson(HistFixture.Obj(DesignParams), problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Equal(45, p.MajorSpacingDays);
            Assert.Equal(36, p.ShareCacheMonths);
            Assert.Equal(10, p.AiChooserPriority);
            Assert.Near(0.40, p.AcquisitionAntitrust.SoftShare, 1e-6);
            Assert.Near(2.5, p.AcquisitionAntitrust.Slope, 1e-6);
            Assert.Near(1.25, p.AcquisitionAntitrust.CaseSurcharge, 1e-6);
            Assert.Near(3.0, p.AcquisitionAntitrust.MaxMultiplier, 1e-6);
            Assert.Equal(36, p.AcquisitionAntitrust.ShareMonths);
            Assert.Near(0.7, p.SettlementDefaults.GetDouble("pWin"), 1e-9);
            Assert.Equal(48, p.DisputeDefaults.GetInt32("arbitrationMonths"));
            Assert.Equal(180, p.PriceWarDefaults.GetInt32("marketingDays"));
            Assert.Equal(0, p.AiWeightOverrides.Count);

            var errors = new List<string>();
            Assert.True(CalendarValidator.ValidateParams(p, errors), string.Join(" | ", errors));
        }

        [Test]
        public void TheDefaultsOfTheCode_AreTheNumbersOfParamsJson()
        {
            HistContentParams defaults = HistContentParams.Defaults();
            HistContentParams read = HistContentParams.FromJson(HistFixture.Obj(DesignParams), null);

            Assert.Equal(read.MajorSpacingDays, defaults.MajorSpacingDays);
            Assert.Equal(read.ShareCacheMonths, defaults.ShareCacheMonths);
            Assert.Equal(read.AiChooserPriority, defaults.AiChooserPriority);
            Assert.Equal(read.AcquisitionAntitrust.SoftShare, defaults.AcquisitionAntitrust.SoftShare);
            Assert.Equal(read.AcquisitionAntitrust.Slope, defaults.AcquisitionAntitrust.Slope);
            Assert.Equal(read.AcquisitionAntitrust.CaseSurcharge, defaults.AcquisitionAntitrust.CaseSurcharge);
            Assert.Equal(read.AcquisitionAntitrust.MaxMultiplier, defaults.AcquisitionAntitrust.MaxMultiplier);
            Assert.Equal(read.AcquisitionAntitrust.ShareMonths, defaults.AcquisitionAntitrust.ShareMonths);
            Assert.NotNull(defaults.SettlementDefaults);
            Assert.NotNull(defaults.DisputeDefaults);
            Assert.NotNull(defaults.PriceWarDefaults);
            Assert.NotNull(defaults.AiWeightOverrides);
        }

        [Test]
        public void ParamsOfTheWrongType_KeepTheirDefaultAndSayWhere()
        {
            var problems = new List<string>();
            HistContentParams p = HistContentParams.FromJson(
                HistFixture.Obj("{'majorSpacingDays':'many','shareCacheMonths':1.5,'acquisitionAntitrust':{'slope':'steep','shareMonths':true},'settlementDefaults':[]}"), problems);

            Assert.Equal(45, p.MajorSpacingDays);
            Assert.Equal(36, p.ShareCacheMonths);
            Assert.Near(2.5, p.AcquisitionAntitrust.Slope, 1e-6);
            Assert.Equal(36, p.AcquisitionAntitrust.ShareMonths);
            Assert.NotNull(p.SettlementDefaults);
            Assert.True(problems.Any(m => m.StartsWith("params.json majorSpacingDays: expected a whole number")), string.Join(" | ", problems));
            Assert.True(problems.Any(m => m.StartsWith("params.json shareCacheMonths: expected a whole number")), string.Join(" | ", problems));
            Assert.True(problems.Any(m => m.StartsWith("params.json acquisitionAntitrust.slope: expected a number")), string.Join(" | ", problems));
            Assert.True(problems.Any(m => m.StartsWith("params.json acquisitionAntitrust.shareMonths: expected a whole number")), string.Join(" | ", problems));
            Assert.True(problems.Any(m => m.StartsWith("params.json settlementDefaults: expected an object")), string.Join(" | ", problems));
        }

        [Test]
        public void ParamsJson_WarnsAboutUnknownMembers_AndAnEmptyFileGivesTheDefaults()
        {
            var problems = new List<string>();
            HistContentParams.FromJson(HistFixture.Obj("{'schema':1,'majorSpacing':45,'acquisitionAntitrust':{'soft':1}}"), problems);
            Assert.True(problems.Any(m => m.Contains("majorSpacing: unknown member is ignored")), string.Join(" | ", problems));
            Assert.True(problems.Any(m => m.Contains("acquisitionAntitrust.soft: unknown member is ignored")), string.Join(" | ", problems));

            HistContentParams empty = HistContentParams.FromJson(new JsonObject(), null);
            Assert.Equal(45, empty.MajorSpacingDays);
            HistContentParams none = HistContentParams.FromJson(null, null);
            Assert.Equal(45, none.MajorSpacingDays);
        }

        [Test]
        public void ARangeIsNotJudgedWhenReading_ThatIsTheValidatorsJob()
        {
            var problems = new List<string>();
            HistContentParams p = HistContentParams.FromJson(HistFixture.Obj("{'acquisitionAntitrust':{'maxMultiplier':9}}"), problems);
            Assert.Equal(0, problems.Count);
            Assert.Near(9, p.AcquisitionAntitrust.MaxMultiplier, 1e-6);
            Assert.False(CalendarValidator.ValidateParams(p, new List<string>()));
        }

        // ---- the entry class (TSK-001) ----

        [Test]
        public void TheEntryClass_IsDeclaredAsTheSpecSays()
        {
            string code = FeatureCode("HistoricalEventsFeature.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+HistoricalEventsFeature\s*:\s*IModFeature"), "[ModFeature] public sealed class ... : IModFeature");
            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.HistoricalEvents\b"), "the namespace names the feature (ModFeatures.FeatureOfType)");
            Assert.True(Regex.IsMatch(code, @"const\s+string\s+FeatureId\s*=\s*HistIds\.Feature"), "Id = historical-events");
            Assert.Equal("historical-events", HistIdsFeature());
            Assert.True(Regex.IsMatch(code, @"NameKey\s*\{\s*get\s*\{\s*return\s+""hist\.feature\.name"""), "NameKey");
            Assert.True(Regex.IsMatch(code, @"DescriptionKey\s*\{\s*get\s*\{\s*return\s+""hist\.feature\.desc"""), "DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\[\]\s*\{\s*HistIds\.EventSystem\s*\}"), "DependsOn = { event-system } and nothing else");
            Assert.Equal("event-system", HistIdsEventSystem());
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true"), "EnabledByDefault");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true"), "SupportsHotToggle");
        }

        [Test]
        public void TheEntryClass_RegistersThePackWithTheIntensityAsSeverity()
        {
            string code = FeatureCode("HistoricalEventsFeature.cs");

            Assert.True(Regex.IsMatch(code, @"EventRegistry\.RegisterPack\(\s*HistIds\.PackId\s*,\s*HistIds\.PackFolder\s*,\s*_pack\s*\)"), "RegisterPack(\"historical-events\", folder, options)");
            Assert.True(Regex.IsMatch(code, @"new\s+PackOptions\s*\{[^}]*SettingsFeatureId\s*=\s*FeatureId[^}]*Severity\s*=\s*HistSettings\.Intensity"), "PackOptions { SettingsFeatureId, Severity = intensity }");
            Assert.True(Regex.IsMatch(code, @"_pack\.Severity\s*=\s*HistSettings\.Intensity"), "a change of the intensity is made in the options in place");
            Assert.Equal("historical-events", HistIdsPack());
            Assert.Equal("content/historical-events", HistIdsPackFolder());
        }

        [Test]
        public void TheEntryClass_NeitherRemovesNorRecreatesTheModifiers()
        {
            // FR-17 item 3: OnDisabled does not call ModModifiers.RemoveAll; OnEnabled does not recreate the entries of the feature. Entries are added by the catch-up of a late
            // start (TSK-007, FR-4), which runs for a new game only, and by the game-side adapter of the services (GameModifiers in GameEconomy.cs: a remedy of a case, the
            // research cost of a dispute, the marketing of a war), which acts when the player or the AI chooses.
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(code.Contains("RemoveAll"), Path.GetFileName(path) + " must not call ModModifiers.RemoveAll");
                string name = Path.GetFileName(path);
                if (name == "CatchUpService.cs" || name == "GameEconomy.cs") continue;
                Assert.False(Regex.IsMatch(code, @"ModModifiers\s*\.\s*Add\b"), name + " creates no entries of ModModifiers: only the catch-up and the services' adapter do");
            }

            string feature = FeatureCode("HistoricalEventsFeature.cs");
            Match enabled = Regex.Match(feature, @"public\s+void\s+OnEnabled\s*\(\s*\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(enabled.Success, "OnEnabled is there");
            Assert.False(enabled.Groups["body"].Value.Contains("ModModifiers"), "OnEnabled does not recreate entries");

            Match disabled = Regex.Match(feature, @"public\s+void\s+OnDisabled\s*\(\s*\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(disabled.Success, "OnDisabled is there");
            string body = disabled.Groups["body"].Value;
            Assert.False(body.Contains("ModModifiers"), "OnDisabled touches no entries of ModModifiers");
            Assert.True(body.Contains("OnFeatureDisabled"), "OnDisabled gives the prices of the AI back (TSK-012)");
        }

        [Test]
        public void TheEntryClass_MakesTheFeatureUnavailable_WhenTheCalendarIsTooSmall()
        {
            // TSK-002: fewer than 25 valid entries -> Unavailable (a broken install or override). The class needs the game, so its source is checked.
            string code = FeatureCode("HistoricalEventsFeature.cs");

            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*!CalendarValidator\.IsUsable\(\s*_calendar\.Count\s*\)\s*\)"), "the count of valid entries is judged by CalendarValidator.IsUsable");
            Assert.True(Regex.IsMatch(code, @"ModFeatures\.MarkUnavailable\(\s*FeatureId\s*,"), "and the feature marks itself Unavailable");
        }

        [Test]
        public void TheEntryClass_RegistersTheSaveSectionOfTheCore()
        {
            // TSK-003: HistSaveSection : IModSaveParticipant, registered with ctx.SaveParticipant.
            string section = FeatureCode(Path.Combine("Save", "HistSaveSection.cs"));
            Assert.True(Regex.IsMatch(section, @"class\s+HistSaveSection\s*:\s*HistSaveSectionBase\s*,\s*IModSaveParticipant"), "HistSaveSection is an IModSaveParticipant");

            string feature = FeatureCode("HistoricalEventsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\.SaveParticipant\(\s*_save\s*\)"), "Register hands the section to the core");
            Assert.Equal("historical-events", HistIds.SaveSection);
        }

        [Test]
        public void TheCalendar_IsLoadedThroughModContent_WithTheConditionsOfTheCore()
        {
            string content = FeatureCode("CalendarContent.cs");

            Assert.True(Regex.IsMatch(content, @"ModContent\.LoadRecords<CalendarEntry>\("), "calendar.json goes through ModContent.LoadRecords");
            Assert.True(Regex.IsMatch(content, @"CalendarEntry\.Mapper\(\s*ModConditions\.Parse\s*\)"), "conditions are parsed by ModConditions.Parse");
            Assert.True(content.Contains("CalendarValidator.Validate("), "every record is judged by the validator inside the loader, so a broken one is skipped with its reasons");
            Assert.True(Regex.IsMatch(content, @"ModContent\.LoadConfig\("), "params.json goes through ModContent.LoadConfig");
            Assert.Equal("calendar.json", HistIds.CalendarFile);
            Assert.Equal("params.json", HistIds.ParamsFile);
        }

        [Test]
        public void TheFeature_HasNoPatchOnTheMethodsTheDesignForbids()
        {
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                // The one patch of the feature is the optional postfix of FR-16 (TSK-023), in BuiltInEventNews.cs.
                if (Path.GetFileName(path) == "BuiltInEventNews.cs") Assert.Equal(1, Regex.Matches(code, @"\[ModPatch\(").Count, "BuiltInEventNews.cs: one patch");
                else Assert.False(code.Contains("[ModPatch"), Path.GetFileName(path) + ": the only patch of the feature is in BuiltInEventNews.cs");
                foreach (string target in new[] { "AddCpu", "NextMonth", "DebtLimit" })
                    Assert.False(Regex.IsMatch(code, @"\b" + target + @"\b"), Path.GetFileName(path) + " must not touch " + target);
            }
        }

        [Test]
        public void EveryFileOfTheFeature_LivesInTheNamespaceOfTheFeature()
        {
            // The core finds the owner of a handler, a patch or an exception by the namespace (PTMod.Features.<Name>).
            int files = 0;
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                MatchCollection namespaces = Regex.Matches(code, @"\bnamespace\s+([\w.]+)");
                Assert.True(namespaces.Count >= 1, Path.GetFileName(path) + " declares a namespace");
                foreach (Match ns in namespaces) Assert.Equal("PTMod.Features.HistoricalEvents", ns.Groups[1].Value, Path.GetFileName(path));
            }
            Assert.True(files >= 10, "the sources of the feature were found: " + files);
        }

        [Test]
        public void EveryStringKeyInTheCode_HasATextInBothLanguages()
        {
            Dictionary<string, string> ru = LoadTable("ru");
            Dictionary<string, string> en = LoadTable("en");
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match literal in Regex.Matches(code, "\"(hist\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                {
                    string key = literal.Groups[1].Value;
                    if (!seen.Add(key)) continue;
                    if (IsIdentifierNotAKey(key)) continue;
                    Assert.True(HasText(ru, key), Path.GetFileName(path) + " uses \"" + key + "\", which ru lacks");
                    Assert.True(HasText(en, key), Path.GetFileName(path) + " uses \"" + key + "\", which en lacks");
                }
            }
            Assert.True(seen.Contains("hist.feature.name") && seen.Contains("hist.feature.desc"), "the scan found the keys of the entry class");
        }

        // ---- helpers ----

        /// <summary>
        /// Some literals that start with the prefix are identifiers, not keys of the string tables: the names of the predicates (<c>hist.ShareAtLeast</c>,
        /// the first letter after the prefix is upper-case) and the ids of the events of the pack (<c>hist.fizzle</c>, <c>hist.decide.antitrust.reimpose</c>: the code fires
        /// them; their texts are other keys, checked with the pack).
        /// </summary>
        private static bool IsIdentifierNotAKey(string literal)
        {
            return Regex.IsMatch(literal, @"^hist\.[A-Z]") || literal == "hist.fizzle" || PackEventIds().Contains(literal);
        }

        private static HashSet<string> _packEventIds;

        /// <summary>The ids of the records of <c>Content/historical-events/events.json</c>.</summary>
        private static HashSet<string> PackEventIds()
        {
            if (_packEventIds != null) return _packEventIds;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", "events.json"));
            JsonNode root;
            JsonError error;
            if (ModJson.TryParse(text, out root, out error) && root is JsonObject)
            {
                JsonArray records = ((JsonObject)root).GetArray("records");
                if (records != null)
                {
                    foreach (JsonNode record in records)
                    {
                        string id;
                        if (record is JsonObject && ((JsonObject)record).TryGetString("id", out id)) ids.Add(id);
                    }
                }
            }
            _packEventIds = ids;
            return ids;
        }

        /// <summary>The keys of a table, a plural group (<c>key.one</c>, <c>key.few</c>, <c>key.many</c>, <c>key.other</c>) counted as its base key; sorted.</summary>
        private static string[] LogicalKeys(Dictionary<string, string> table)
        {
            return table.Keys
                .Select(k => Regex.Replace(k, @"\.(one|few|many|other)$", string.Empty))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>The key has a text, or is the base of a plural group (<c>key.one</c>, <c>key.other</c>).</summary>
        private static bool HasText(Dictionary<string, string> table, string key)
        {
            return table.ContainsKey(key) || table.ContainsKey(key + ".one") || table.ContainsKey(key + ".other");
        }

        private static ParamSpec Find(string key)
        {
            ParamSpec spec = HistParamSpecs.Create(Feature).FirstOrDefault(s => s.Key == key);
            Assert.NotNull(spec, "no parameter " + key);
            return spec;
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }

        private static string FeatureCode(string file)
        {
            string path = Path.Combine(FeatureDir(), file);
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "no source: " + path);
            return SourceScan.Strip(text, false);
        }

        private static Dictionary<string, string> LoadTable(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, Feature + ".json");
            JsonObject table = HistFixture.ObjFromFile(SourceScan.ReadText(path));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in table)
            {
                var text = pair.Value as JsonValue;
                Assert.True(text != null && text.Kind == JsonKind.String, language + ": the value of " + pair.Key + " is a string");
                result[pair.Key] = text.StringValue;
            }
            return result;
        }

        // The constants of HistIds are internal to the sources compiled into the tests; reading them here keeps the checks above honest.
        private static string HistIdsFeature()
        {
            return HistIds.Feature;
        }

        private static string HistIdsEventSystem()
        {
            return HistIds.EventSystem;
        }

        private static string HistIdsPack()
        {
            return HistIds.PackId;
        }

        private static string HistIdsPackFolder()
        {
            return HistIds.PackFolder;
        }
    }
}
