using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-021, NFR-3, NFR-4, NFR-8: the section of the side save file against the real <see cref="SideFileModel"/> of the core and three saves
    /// (<c>Fixtures/*.ptmod.json</c>): one without the section (a save from before the feature), one with <c>_schema</c> 1 (this build) and one with
    /// <c>_schema</c> 99 (a newer build). The first and the last start from nothing and reconstruct (the core keeps the newer section as it was
    /// read), the middle one is read and written back as it was. Writing never touches the sections of other features. The preferences of contracts
    /// come out the same for the same contracts and the same seed.
    /// </summary>
    public class SaveModelTests
    {
        private const string Id = PlatformTestData.FeatureId;
        private const string NoSection = "no-section.ptmod.json";
        private const string Schema1 = "schema-1.ptmod.json";
        private const string Schema99 = "schema-99.ptmod.json";

        // ---- the fixtures and the holder ----

        private static string Fixture(string name)
        {
            Assert.NotNull(TestEnv.ModDir, "mod folder not found; run the tests through python mod/build_feature.py platform-generations --tests");
            string path = Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "PlatformGenerations", "Fixtures", name);
            Assert.True(File.Exists(path), "no fixture " + path);
            return SourceScan.ReadText(path);
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the fixture is not valid JSON: " + error);
            Assert.True(node is JsonObject, "the root of a side file is an object");
            return (JsonObject)node;
        }

        private static JsonObject Features(JsonObject document)
        {
            JsonObject features = document.GetObject("features");
            Assert.NotNull(features, "the document has 'features'");
            return features;
        }

        private static JsonNode Section(JsonObject document, string id)
        {
            return Features(document)[id];
        }

        /// <summary>
        /// The feature's side of the save the way <c>PlatformGenerationsFeature</c> composes it from the pure parts (the source of the feature is
        /// checked for the same calls in <c>PlatformIntegrationTests</c>): the holder of the DTO, the chronology and the preferences. The installed
        /// base and the cache of the multipliers are derived from the game and are not part of the section.
        /// </summary>
        private sealed class FeatureState : ISideFileParticipant
        {
            public readonly PlatformSaveSection Holder;
            public readonly PlatformTimeline Timeline = new PlatformTimeline();
            public readonly OemPreferences Preferences = new OemPreferences();
            public readonly List<string> Problems = new List<string>();
            public int Resets;
            public int Reads;
            public GameDate Today = new GameDate(1996, 6, 1);

            public FeatureState()
            {
                Holder = new PlatformSaveSection(Id, line => Problems.Add(line));
            }

            public string SectionId
            {
                get { return Holder.SectionId; }
            }

            public int SchemaVersion
            {
                get { return Holder.SchemaVersion; }
            }

            public bool NeedsReconstruction
            {
                get { return Holder.NeedsReconstruction; }
            }

            public void ResetToDefaults()
            {
                Resets++;
                Holder.ResetToDefaults();
                Timeline.Clear();
                Preferences.Clear();
            }

            public void Read(JsonObject section, int schema)
            {
                Reads++;
                Holder.Read(section, schema);
                Timeline.ReadFrom(Holder.Data, Today, Problems);
                Preferences.ReadFrom(Holder.Data, Problems);
                if (Holder.Data.generations.Count == 0) Holder.NeedsReconstruction = true;
            }

            public JsonObject Write()
            {
                Timeline.WriteTo(Holder.Data);
                Preferences.WriteTo(Holder.Data);
                return Holder.Write();
            }
        }

        private static SideFileModel Model(ISideFileParticipant participant, params ISideFileParticipant[] others)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(participant, out problem), problem);
            foreach (ISideFileParticipant other in others) Assert.True(model.TryRegister(other, out problem), problem);
            return model;
        }

        private static SideFileReadReport Load(SideFileModel model, string fixture)
        {
            SideFileReadReport report = model.Apply(Fixture(fixture));
            Assert.NotNull(report);
            return report;
        }

        private static JsonObject Save(SideFileModel model)
        {
            SideFileBuildReport build = model.Build(null);
            Assert.Equal(0, build.Warnings.Count, string.Join("; ", build.Warnings));
            return build.Document;
        }

        // ---- the market the fixtures were made from ----

        private static GenerationCatalog Catalog()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return catalog;
        }

        private static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        private static ReconCpu C(int id, int year, string company, string socketKey, string genId)
        {
            return new ReconCpu { CpuId = id, Year = year, CompanyKey = company, SocketKey = socketKey, GenId = genId };
        }

        /// <summary>The CPUs of 1996 that the chronology of <c>schema-1.ptmod.json</c> was reconstructed from (the fixture is exactly what <see cref="Reconstruction"/> makes of them).</summary>
        private static List<ReconCpu> Market1996()
        {
            return new List<ReconCpu>
            {
                C(1, 1986, "ai:1", "pkg:PGA 1985", "gen-1985-pga"),
                C(3, 1990, "ai:2", "pkg:PGA 1990", "gen-1990-pga"),
                C(5, 1990, "ai:1", "pkg:PGA 1990", "gen-1990-pga"),
                C(9, 1990, "player", "pkg:PGA 1990", "gen-1990-pga"),
                C(11, 1991, "ai:3", "pkg:PGA 1990", "gen-1990-pga"),
                C(20, 1995, "ai:1", "pkg:PGA 1995", "gen-1995-pga"),
                C(22, 1995, "ai:3", "pkg:PGA 1995", "gen-1995-pga"),
                C(21, 1996, "ai:2", "pkg:PGA 1995", "gen-1995-pga"),
            };
        }

        private static ReconstructionReport Reconstruct(FeatureState state)
        {
            ReconstructionReport report = Reconstruction.Run(state.Timeline, Catalog(), Balance(), Market1996(), 1996);
            state.Holder.NeedsReconstruction = false;
            return report;
        }

        /// <summary>The chronology of the state as the JSON of its section (the preferences left out: a reconstruction does not make them).</summary>
        private static JsonObject ChronologyOf(FeatureState state)
        {
            var save = new PlatformGenerationsSave();
            state.Timeline.WriteTo(save);
            JsonObject written = PlatformSaveCodec.Write(save);
            written.Set("contractPrefs", new JsonArray());
            return written;
        }

        // ---- the fixtures themselves ----

        [Test]
        public void TheThreeFixtures_AreValidSideFiles_OfTheShapesTheyClaim()
        {
            JsonObject none = Parse(Fixture(NoSection));
            JsonObject one = Parse(Fixture(Schema1));
            JsonObject future = Parse(Fixture(Schema99));

            Assert.False(Features(none).ContainsKey(Id), "no section of the feature");
            Assert.Equal(1, ((JsonObject)Section(one, Id)).GetInt32("_schema", -1));
            Assert.Equal(99, ((JsonObject)Section(future, Id)).GetInt32("_schema", -1));
            foreach (JsonObject document in new[] { none, one, future })
            {
                Assert.Equal(1, document.GetInt32("schema", -1), "the format of the file itself");
                Assert.True(Features(document).ContainsKey("mod-framework"), "the section of the core");
            }
            Assert.True(Features(none).ContainsKey("some-future-feature"), "a section nobody here owns");
        }

        // ---- a save without the section (a save from before the feature) ----

        [Test]
        public void ASaveWithoutTheSection_Loads_TheFeatureStartsFromNothing_AndAsksForReconstruction()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);

            SideFileReadReport report = Load(model, NoSection);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(1, state.Resets, "the core gave the feature its defaults");
            Assert.Equal(0, state.Reads, "and nothing to read");
            Assert.True(state.NeedsReconstruction);
            Assert.Equal(0, state.Timeline.GenerationCount);
            Assert.Equal(0, state.Timeline.IntroCount);
            Assert.Equal(0, state.Preferences.Count);
        }

        [Test]
        public void NoSideFileAtAll_IsTheSameAsNoSection()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);

            SideFileReadReport report = model.ApplyMissing();

            Assert.Equal(SideFileStatus.Missing, report.Status);
            Assert.True(state.NeedsReconstruction);
            Assert.Equal(0, state.Timeline.GenerationCount);
        }

        [Test]
        public void ASaveWithoutTheSection_IsReconstructed_ToExactlyTheChronologyOfTheSchema1Fixture()
        {
            // NFR-3: an old save and a save made by the mod end in the same chronology.
            var old = new FeatureState();
            Load(Model(old), NoSection);
            ReconstructionReport report = Reconstruct(old);
            Assert.True(report.Changed);
            Assert.Equal(8, report.CpusUsed);

            var made = new FeatureState();
            Load(Model(made), Schema1);

            JsonObject fromOld = ChronologyOf(old);
            JsonObject fromFile = ChronologyOf(made);
            Assert.True(fromOld.Equals(fromFile), "reconstructed:\n" + ModJson.Write(fromOld, true) + "\nthe fixture:\n" + ModJson.Write(fromFile, true));
        }

        // ---- a save of this build (_schema 1) ----

        [Test]
        public void ASchema1Save_IsRead_IntoTheChronologyAndThePreferences_WithoutAProblem()
        {
            var state = new FeatureState();
            SideFileReadReport report = Load(Model(state), Schema1);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(0, state.Problems.Count, string.Join("; ", state.Problems));
            Assert.Equal(0, state.Resets);
            Assert.Equal(1, state.Reads);
            Assert.False(state.NeedsReconstruction, "a section was read");

            Assert.Equal(5, state.Timeline.GenerationCount);
            Assert.Equal(8, state.Timeline.IntroCount);
            GameDate launched;
            string by;
            Assert.True(state.Timeline.IsLaunched("gen-1990-pga", out launched, out by));
            Assert.Equal(new GameDate(1990, 1, 1), launched);
            Assert.Equal("ai:2", by);
            Assert.Equal(3, state.Timeline.EarlyAdoptersOf("gen-1990-pga").Count);
            Assert.Equal(3, state.Timeline.EarlyAdoptersOf("gen-1995-pga").Count);
            GameDate since;
            Assert.True(state.Timeline.IsEarlyAdopter("player", "gen-1990-pga", out since));
            Assert.Equal(new GameDate(1990, 1, 1), since);
            Assert.False(state.Timeline.IsEarlyAdopter("player", "gen-1995-pga", out since));
            Assert.False(state.Timeline.IsLaunched("gen-1980-plcc", out launched, out by), "announced, never launched");
            Assert.True(state.Timeline.IsAnnounced("gen-1980-plcc"));

            Assert.Equal(3, state.Preferences.Count);
            Assert.Equal("gen-1995-pga", state.Preferences.PreferenceOf("Dell|1996-3|60000|180|1"));
            Assert.Equal(string.Empty, state.Preferences.PreferenceOf("Sony|1996-3|30000|240|2"), "'' is an answer: the contract prefers none");
            Assert.Null(state.Preferences.PreferenceOf("Nobody|1996-3|1|1|1"), "no answer yet");
        }

        [Test]
        public void ASchema1Save_IsWrittenBack_AsItWasRead()
        {
            JsonObject original = Parse(Fixture(Schema1));
            var state = new FeatureState();
            SideFileModel model = Model(state);
            Load(model, Schema1);

            JsonObject saved = Save(model);

            JsonNode before = Section(original, Id);
            JsonNode after = Section(saved, Id);
            Assert.True(before.Equals(after), "the section of the feature:\n" + ModJson.Write(after, true) + "\nwas:\n" + ModJson.Write(before, true));
        }

        [Test]
        public void SavingAndLoadingAgain_GivesTheSameFileEveryTime()
        {
            var first = new FeatureState();
            SideFileModel firstModel = Model(first);
            Load(firstModel, Schema1);
            string text = ModJson.Write(Save(firstModel), false);

            var second = new FeatureState();
            SideFileModel secondModel = Model(second);
            Assert.Equal(SideFileStatus.Loaded, secondModel.Apply(text).Status);
            string again = ModJson.Write(Save(secondModel), false);

            Assert.Equal(text, again, "load, save, load, save: the same text");
            Assert.True(ChronologyOf(first).Equals(ChronologyOf(second)));
        }

        [Test]
        public void AReconstruction_AfterASchema1Read_AddsNothing()
        {
            // The section is complete and the market is the one it was made from: nothing is missing, so a reconstruction (the feature switched on again) leaves it alone.
            var state = new FeatureState();
            Load(Model(state), Schema1);
            JsonObject before = ChronologyOf(state);

            ReconstructionReport report = Reconstruction.Run(state.Timeline, Catalog(), Balance(), Market1996(), 1996);

            Assert.False(report.Changed, "added " + report.Announced + "/" + report.Launched + "/" + report.EarlyAdopters + "/" + report.SocketIntros);
            Assert.True(before.Equals(ChronologyOf(state)));
        }

        // ---- a save of a newer build (_schema 99) ----

        [Test]
        public void ASchema99Section_IsNotRead_TheFeatureStartsFromNothing_AndWorksByReconstruction()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);

            SideFileReadReport report = Load(model, Schema99);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.True(report.NewerSections.Contains(Id), "the core names the section that is newer");
            Assert.Equal(0, state.Reads, "the feature never sees a section it cannot understand");
            Assert.Equal(1, state.Resets);
            Assert.True(state.NeedsReconstruction);
            Assert.Equal(0, state.Timeline.GenerationCount);
            Assert.Equal(0, state.Preferences.Count);

            // It works: the chronology is reconstructed, the same as for a save from before the feature.
            Reconstruct(state);
            var old = new FeatureState();
            Load(Model(old), NoSection);
            Reconstruct(old);
            Assert.True(ChronologyOf(state).Equals(ChronologyOf(old)), "the unreadable section changes nothing in what the feature builds");
        }

        [Test]
        public void ASchema99Section_IsKeptByTheCore_EvenAfterTheFeatureBuiltItsOwnState()
        {
            JsonObject original = Parse(Fixture(Schema99));
            var state = new FeatureState();
            SideFileModel model = Model(state);
            Load(model, Schema99);
            Reconstruct(state);
            state.Preferences.Assign("Dell|1996-3|60000|180|1", "gen-1995-pga");

            JsonObject saved = Save(model);

            JsonNode kept = Section(saved, Id);
            Assert.True(kept.Equals(Section(original, Id)), "the newer section is written back as it was read:\n" + ModJson.Write(kept, true));
            Assert.Equal(99, ((JsonObject)kept).GetInt32("_schema", -1));
            Assert.True(((JsonObject)kept).ContainsKey("futureFlag"), "with its members this build does not know");
        }

        // ---- other sections ----

        private static readonly string[] OthersOf = { "mod-framework", "market-shocks", "some-future-feature" };

        [Test]
        public void WritingTheSection_NeverTouchesTheSectionsOfOtherFeatures()
        {
            foreach (string fixture in new[] { NoSection, Schema1, Schema99 })
            {
                JsonObject original = Parse(Fixture(fixture));
                var state = new FeatureState();
                SideFileModel model = Model(state);
                Load(model, fixture);

                // The feature changes its own state before the save.
                if (state.NeedsReconstruction) Reconstruct(state);
                state.Timeline.Announce("gen-2000", new GameDate(1996, 1, 1));
                state.Preferences.Assign("Added|1996-5|1000|100|1", "gen-1995-pga");

                JsonObject saved = Save(model);
                foreach (string other in OthersOf)
                {
                    JsonNode was = Section(original, other);
                    if (was == null) continue;
                    JsonNode now = Section(saved, other);
                    Assert.NotNull(now, fixture + ": the section '" + other + "' vanished");
                    Assert.True(was.Equals(now), fixture + ": the section '" + other + "' changed:\n" + ModJson.Write(now, true));
                }
            }
        }

        private sealed class OtherFeature : ISideFileParticipant
        {
            public JsonObject Stored = new JsonObject();
            public int Resets;

            public string SectionId
            {
                get { return "market-shocks"; }
            }

            public int SchemaVersion
            {
                get { return 1; }
            }

            public void ResetToDefaults()
            {
                Resets++;
                Stored = new JsonObject();
            }

            public void Read(JsonObject section, int schema)
            {
                Stored = section;
            }

            public JsonObject Write()
            {
                return Stored;
            }
        }

        [Test]
        public void TwoFeaturesInOneFile_KeepToTheirOwnSections()
        {
            JsonObject original = Parse(Fixture(Schema1));
            var state = new FeatureState();
            var other = new OtherFeature();
            SideFileModel model = Model(state, other);
            Load(model, Schema1);

            Assert.Equal("1995-03-01", other.Stored.GetString("lastShock"), "the other feature got its own section");
            Assert.Equal(1, state.Reads);
            Assert.Equal(0, other.Resets);

            // Ours changes; theirs does not.
            state.Timeline.Announce("gen-2000", new GameDate(1996, 6, 1));
            JsonObject saved = Save(model);

            JsonObject theirs = (JsonObject)Section(saved, "market-shocks");
            JsonObject theirsBefore = (JsonObject)Section(original, "market-shocks");
            Assert.Equal(1, theirs.GetInt32("_schema", -1));
            Assert.Equal("1995-03-01", theirs.GetString("lastShock"));
            Assert.Equal(theirsBefore.GetArray("queued").Count, theirs.GetArray("queued").Count);

            JsonObject ours = (JsonObject)Section(saved, Id);
            Assert.Equal(6, ours.GetArray("generations").Count, "the new announcement is ours alone");
            Assert.False(ours.ContainsKey("lastShock"), "nothing of theirs leaked into ours");
        }

        [Test]
        public void ANewGame_DropsEverything_TheSectionIncluded()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);
            Load(model, Schema1);
            Assert.Equal(5, state.Timeline.GenerationCount);

            SideFileReadReport report = model.StartNewGame();

            Assert.Equal(SideFileStatus.NewGame, report.Status);
            Assert.Equal(0, state.Timeline.GenerationCount);
            Assert.Equal(0, state.Preferences.Count);
            Assert.True(state.NeedsReconstruction, "rebuilt at the end of the fast-forward");
            JsonObject saved = Save(model);
            Assert.Equal(0, ((JsonObject)Section(saved, Id)).GetArray("generations").Count, "a new game starts clean; the old file's other sections are gone too");
            Assert.False(Features(saved).ContainsKey("some-future-feature"));
        }

        // ---- a section that is broken inside ----

        [Test]
        public void ADamagedSection_LoadsWithWarningsOfTheFeature_AndTheRestIsUsed()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);
            string text = "{\"schema\":1,\"features\":{\"platform-generations\":{\"_schema\":1,"
                          + "\"generations\":[{\"id\":\"gen-1990-pga\",\"announced\":\"1990-01-01\",\"launched\":\"2999-01-01\",\"launchedBy\":\"ai:2\",\"earlyAdopters\":[]},"
                          + "{\"id\":\"gen-1995-pga\",\"announced\":\"not a date\",\"launched\":\"\",\"launchedBy\":\"\",\"earlyAdopters\":[]}],"
                          + "\"socketIntros\":[{\"company\":\"player\",\"socket\":\"pkg:PGA 1990\",\"date\":\"1990-01-01\",\"main\":\"\",\"mainAgeMonths\":0,\"severity\":7.5}],"
                          + "\"contractPrefs\":[{\"key\":\"\",\"gen\":\"x\"},{\"key\":\"Dell|1996-3|60000|180|1\",\"gen\":\"gen-1995-pga\"}]}}}";

            SideFileReadReport report = model.Apply(text);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.True(state.Problems.Count >= 3, "the date in the future, the date that is no date, the empty key: " + string.Join("; ", state.Problems));
            GameDate launched;
            string by;
            Assert.False(state.Timeline.IsLaunched("gen-1990-pga", out launched, out by), "a launch in the future is dropped");
            Assert.True(state.Timeline.IsAnnounced("gen-1990-pga"), "the rest of the record stays");
            Assert.Equal(1, state.Timeline.IntroCount);
            Assert.Equal(1, state.Preferences.Count, "the record with an empty key is dropped");
            Assert.Near(1.0, state.Timeline.IntroOf("player", "pkg:PGA 1990").Severity, 0, "a severity above 1 is cut to 1");
        }

        // ---- determinism of the preferences (NFR-4) ----

        private const uint Seed = 123456789u;

        /// <summary>400 contracts of two years: clients, review months, units, payments, the two types of the balance.</summary>
        private static List<string> Contracts()
        {
            string[] clients = { "Dell", "Sony", "Acer", "Apple", "Compaq", "Packard Bell", "A|B", "C\\D" };
            var keys = new List<string>();
            for (int i = 0; i < 400; i++)
            {
                string client = clients[i % clients.Length];
                int year = 1996 + (i / 12) % 2;
                int month = 1 + i % 12;
                int units = 10000 + (i * 137) % 90000;
                float payment = 80f + (i * 7) % 220 + (i % 3) * 0.5f;
                int type = 1 + i % 2;
                keys.Add(ContractKeyBuilder.Build(client, year, month, units, payment, type));
            }
            return keys;
        }

        private static List<GenAdoptionInput> InputsFrom(FeatureState state, GameDate at)
        {
            return OemAdoptionModel.BuildInputs(Catalog(), state.Timeline, new InstalledBaseLedger(), at, new List<GenAdoptionInput>());
        }

        private static OemPreferences Assign(List<string> contracts, uint seed, IReadOnlyList<GenAdoptionInput> inputs)
        {
            var prefs = new OemPreferences();
            foreach (string key in contracts) prefs.Assign(key, OemAdoptionModel.AssignPreference(key, seed, inputs));
            return prefs;
        }

        [Test]
        public void TheContractKeys_AreDistinct_ForTheTestToMeanSomething()
        {
            List<string> contracts = Contracts();
            Assert.Equal(400, new HashSet<string>(contracts, StringComparer.Ordinal).Count);
        }

        [Test]
        public void AssigningTheSameContractsAgain_WithTheSameSeed_GivesTheSamePreferencesEveryTime()
        {
            var state = new FeatureState();
            Load(Model(state), Schema1);
            List<string> contracts = Contracts();
            GameDate at = new GameDate(1996, 3, 1);

            OemPreferences first = Assign(contracts, Seed, InputsFrom(state, at));
            OemPreferences again = Assign(contracts, Seed, InputsFrom(state, at));   // the inputs are built anew, as the next session would

            int same = 0;
            foreach (string key in contracts)
            {
                if (first.PreferenceOf(key) == again.PreferenceOf(key)) same++;
            }
            Assert.Equal(contracts.Count, same, "100 % of the contracts get the same generation");

            // The answers are not all alike: the test would pass for a constant, too.
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in contracts) kinds.Add(first.PreferenceOf(key));
            Assert.True(kinds.Count >= 2, "more than one answer among the contracts: " + string.Join(", ", kinds));
            Assert.True(kinds.Contains(string.Empty) || kinds.Count >= 3, "some contracts prefer no generation, or three generations share them");
        }

        [Test]
        public void AnotherSeed_GivesAnotherSetOfPreferences()
        {
            var state = new FeatureState();
            Load(Model(state), Schema1);
            List<string> contracts = Contracts();
            IReadOnlyList<GenAdoptionInput> inputs = InputsFrom(state, new GameDate(1996, 3, 1));

            OemPreferences one = Assign(contracts, Seed, inputs);
            OemPreferences other = Assign(contracts, Seed + 1, inputs);

            int different = 0;
            foreach (string key in contracts)
            {
                if (one.PreferenceOf(key) != other.PreferenceOf(key)) different++;
            }
            Assert.True(different >= 20, "the seed is part of the dice: " + different + " of " + contracts.Count + " differ");
        }

        [Test]
        public void ThePreferences_SurviveASaveAndALoad_AndAreNotDecidedAgain()
        {
            var state = new FeatureState();
            SideFileModel model = Model(state);
            Load(model, Schema1);
            List<string> contracts = Contracts();
            IReadOnlyList<GenAdoptionInput> inputs = InputsFrom(state, new GameDate(1996, 3, 1));

            var assigned = Assign(contracts, Seed, inputs);
            state.Preferences.Clear();
            foreach (string key in contracts) state.Preferences.Assign(key, assigned.PreferenceOf(key));
            string text = ModJson.Write(Save(model), false);

            var loaded = new FeatureState();
            SideFileModel loadedModel = Model(loaded);
            Assert.Equal(SideFileStatus.Loaded, loadedModel.Apply(text).Status);
            Assert.Equal(contracts.Count, loaded.Preferences.Count);

            int same = 0;
            foreach (string key in contracts)
            {
                if (loaded.Preferences.PreferenceOf(key) == assigned.PreferenceOf(key)) same++;
            }
            Assert.Equal(contracts.Count, same, "100 % after a save and a load");

            // Even a different answer for the same contract is not taken: the first answer stands (D6), so a changed installed base moves nothing.
            int taken = 0;
            foreach (string key in contracts)
            {
                if (loaded.Preferences.Assign(key, "gen-2030")) taken++;
            }
            Assert.Equal(0, taken);
        }

        [Test]
        public void TheDice_DoNotDependOnTheOrderOfTheContracts_OrOnWhatWasAssignedBefore()
        {
            var state = new FeatureState();
            Load(Model(state), Schema1);
            List<string> contracts = Contracts();
            IReadOnlyList<GenAdoptionInput> inputs = InputsFrom(state, new GameDate(1996, 3, 1));

            OemPreferences forward = Assign(contracts, Seed, inputs);
            var reversed = new List<string>(contracts);
            reversed.Reverse();
            OemPreferences backward = Assign(reversed, Seed, inputs);

            foreach (string key in contracts) Assert.Equal(forward.PreferenceOf(key), backward.PreferenceOf(key), key);
        }

        // ---- the same run twice (NFR-3, NFR-8) ----

        [Test]
        public void TheWholeFlow_GivesTheSameFileTwice_FromTheSameSave()
        {
            string[] texts = new string[2];
            for (int run = 0; run < 2; run++)
            {
                var state = new FeatureState();
                SideFileModel model = Model(state);
                Load(model, NoSection);
                Reconstruct(state);
                List<string> contracts = Contracts();
                IReadOnlyList<GenAdoptionInput> inputs = InputsFrom(state, new GameDate(1996, 3, 1));
                foreach (string key in contracts) state.Preferences.Assign(key, OemAdoptionModel.AssignPreference(key, Seed, inputs));
                texts[run] = ModJson.Write(Save(model), false);
            }
            Assert.Equal(texts[0], texts[1]);
            Assert.True(texts[0].Length > 5000, "a real section was written: " + texts[0].Length);
        }
    }
}
