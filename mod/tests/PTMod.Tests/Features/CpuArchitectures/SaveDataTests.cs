using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The state of the feature and its section of the side save file (FR-5, FR-8, NFR-3, TSK-003): the round trip without loss, the missing section
    /// (the old save, the new game), the values out of range and the limits of the lists, the families the catalog does not know, a DTO without a version
    /// field, and the section inside the core's side-file model (no <c>_schema</c> read as version 1, a newer section kept as it is). TSK-031 adds the compatibility of the
    /// saves between the stages and with a changed catalog: a save of stage 1 opens in stage 2 without a migration or a warning and the steps of stage 2 run on it, the lists of
    /// stage 2 with broken records, a family the catalog has lost going through every step, a <c>resumeTech</c> out of range, the state of an absorbed company going to its buyer.
    /// </summary>
    [Covers(typeof(ArchRegistry), typeof(CompanyArchState), typeof(FamilyState), typeof(SupportTier), typeof(PauseReason),
            typeof(ArchSaveData), typeof(CompanyArchSave), typeof(FamilySave), typeof(CpuArchSave), typeof(PendingSave), typeof(AiOverrideSave),
            typeof(MilestoneSave), typeof(ArchSaveSection))]
    public class SaveDataTests
    {
        private static ArchRegistry NewRegistry()
        {
            return new ArchRegistry(ArchTestKit.BuiltInCatalog());
        }

        private static ArchSaveSection Section(ArchRegistry registry, List<string> warnings = null)
        {
            return new ArchSaveSection(registry, warnings == null ? (Action<string>)null : warnings.Add);
        }

        /// <summary>A registry with a little of everything: the player and an AI company, bindings, a waiting project, the stage-2 lists.</summary>
        private static ArchRegistry Populated()
        {
            ArchRegistry registry = NewRegistry();

            CompanyArchState player = registry.GetOrCreate(3, true);
            player.Active = "x86";
            player.CurrentResearch = "risc";
            player.Paused = true;
            player.PauseReason = PauseReason.TechnologyChosen;
            player.ResumeTechnologyId = 41;
            player.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, ResearchedYear = 1971, ResearchedMonth = 1, Support = 31.5f };
            player.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, ResearchedYear = 1981, ResearchedMonth = 6, Support = 58.2f, Tier = SupportTier.Normal };
            player.Families["risc"] = new FamilyState("risc") { Progress = 0.4213f };

            CompanyArchState ai = registry.GetOrCreate(17, false);
            ai.Active = "x86";
            ai.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, ResearchedYear = 1971, ResearchedMonth = 1, Support = 12f };
            ai.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, ResearchedYear = 1978, ResearchedMonth = 9, Support = 68.9f, Tier = SupportTier.High };

            registry.Bind(1043, "x86");
            registry.Bind(1057, "proprietary");
            registry.AddPending(new PendingSave { company = 3, name = "PT-386", year = 1987, ips = 5600000f, arch = "x86" });
            registry.AiOverrides.Add(new AiOverrideSave { company = 17, arch = "arm", weight = 2.5f });
            registry.Milestones.Add(new MilestoneSave { arch = "x86", threshold = 25 });
            registry.Milestones.Add(new MilestoneSave { arch = "x86", threshold = 50 });
            registry.MarkSeeded();
            return registry;
        }

        private static string Text(JsonNode node)
        {
            return ModJson.Write(node, false);
        }

        private static JsonObject Parse(string text)
        {
            return ArchTestKit.ParseObject(text);
        }

        // ---- round trip ----

        [Test]
        public void RoundTrip_KeepsEveryFieldOfTheState()
        {
            ArchRegistry original = Populated();
            JsonObject written = Section(original).Write();
            Assert.NotNull(written);

            // Through text, like the side file: the second registry sees only what was written.
            ArchRegistry copy = NewRegistry();
            var warnings = new List<string>();
            Section(copy, warnings).Read(Parse(Text(written)), ArchSaveSection.Version);

            Assert.Equal(0, warnings.Count, ArchTestKit.Joined(warnings));
            Assert.Equal(Text(written), Text(Section(copy).Write()), "the second write must equal the first");

            Assert.True(copy.Seeded);
            Assert.False(copy.MigrationNeeded);
            Assert.Equal(2, copy.Companies.Count);

            CompanyArchState player = copy.GetOrNull(3);
            Assert.True(player.IsPlayer);
            Assert.Equal("x86", player.Active);
            Assert.Equal("risc", player.CurrentResearch);
            Assert.True(player.Paused);
            Assert.Equal(PauseReason.TechnologyChosen, player.PauseReason);
            Assert.Equal(41, player.ResumeTechnologyId);
            Assert.Equal(3, player.Families.Count);
            FamilyState x86 = player.GetFamily("x86");
            Assert.True(x86.Researched);
            Assert.Near(1, x86.Progress);
            Assert.Equal(1981, x86.ResearchedYear);
            Assert.Equal(6, x86.ResearchedMonth);
            Assert.Near(58.2, x86.Support, 1e-4);
            Assert.Equal(SupportTier.Normal, x86.Tier);
            Assert.Near(0.4213, player.GetFamily("risc").Progress, 1e-6);
            Assert.False(player.GetFamily("risc").Researched);

            CompanyArchState ai = copy.GetOrNull(17);
            Assert.False(ai.IsPlayer);
            Assert.Null(ai.CurrentResearch);
            Assert.False(ai.Paused);
            Assert.Equal(PauseReason.None, ai.PauseReason);
            Assert.Equal(-1, ai.ResumeTechnologyId);
            Assert.Equal(SupportTier.High, ai.GetFamily("x86").Tier);

            Assert.Equal(2, copy.CpuBindingCount);
            Assert.Equal("x86", copy.GetCpuArch(1043));
            Assert.Equal("proprietary", copy.GetCpuArch(1057));
            Assert.Null(copy.GetCpuArch(1));

            Assert.Equal(1, copy.Pending.Count);
            Assert.Equal("PT-386", copy.Pending[0].name);
            Assert.Equal(1987, copy.Pending[0].year);
            Assert.Near(5600000, copy.Pending[0].ips, 1);
            Assert.Equal("x86", copy.Pending[0].arch);

            Assert.Equal(1, copy.AiOverrides.Count);
            Assert.Near(2.5, copy.AiOverrides[0].weight, 1e-6);
            Assert.Equal(2, copy.Milestones.Count);
        }

        [Test]
        public void RoundTrip_OfAnEmptyRegistry_IsAnEmptySection()
        {
            ArchRegistry empty = NewRegistry();
            JsonObject written = Section(empty).Write();

            Assert.Equal("{\"seeded\":false,\"companies\":[],\"cpuArch\":[],\"pending\":[],\"aiOverrides\":[],\"milestones\":[]}", Text(written));

            ArchRegistry copy = NewRegistry();
            Section(copy).Read(written, 1);
            Assert.True(copy.MigrationNeeded);
            Assert.Equal(0, copy.Companies.Count);
        }

        [Test]
        public void Write_HasADocumentedShape_AndNoVersionField()
        {
            JsonObject written = Section(Populated()).Write();

            Assert.False(written.ContainsKey("_schema"), "the core writes _schema, not the feature");
            Assert.False(written.ContainsKey("schema"));
            Assert.False(written.ContainsKey("version"));
            Assert.True(written.GetBool("seeded"));
            JsonObject firstCompany = (JsonObject)written.GetArray("companies")[0];
            Assert.Equal(3, firstCompany.GetInt32("companyId"));
            Assert.True(firstCompany.GetBool("isPlayer"));
            Assert.Equal("technology", firstCompany.GetString("pauseReason"));
            Assert.Equal(41, firstCompany.GetInt32("resumeTech"));
            var families = firstCompany.GetArray("families");
            Assert.Equal("proprietary", ((JsonObject)families[0]).GetString("id"), "families follow the order of the catalog");
            Assert.Equal("x86", ((JsonObject)families[1]).GetString("id"));
            Assert.Equal("risc", ((JsonObject)families[2]).GetString("id"));
            Assert.Equal("normal", ((JsonObject)families[1]).GetString("tier"));
            Assert.Equal(1043, ((JsonObject)written.GetArray("cpuArch")[0]).GetInt32("cpu"));
            Assert.Equal("PT-386", ((JsonObject)written.GetArray("pending")[0]).GetString("name"));
        }

        [Test]
        public void TheDtos_HaveNoVersionField()
        {
            var types = new[]
            {
                typeof(ArchSaveData), typeof(CompanyArchSave), typeof(FamilySave), typeof(CpuArchSave), typeof(PendingSave), typeof(AiOverrideSave), typeof(MilestoneSave),
            };
            foreach (Type type in types)
            {
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public).Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property))
                {
                    string name = member.Name.ToLowerInvariant();
                    Assert.False(name.Contains("schema") || name.Contains("version"), type.Name + "." + member.Name + ": the version of the section belongs to the core");
                }
            }
        }

        [Test]
        public void TheSection_ReportsItsIdAndVersion()
        {
            ArchSaveSection section = Section(NewRegistry());

            Assert.Equal("cpu-architectures", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(ArchSaveSection.Section, section.SectionId);
            Assert.Equal(ArchSaveSection.Version, section.SchemaVersion);
        }

        [Test]
        public void ToSave_ReturnsFreshObjects()
        {
            ArchRegistry registry = Populated();
            ArchSaveData data = registry.ToSave();
            data.companies[0].families[0].progress = 0.5f;
            data.cpuArch.Clear();
            data.pending[0].name = "changed";
            data.milestones.Clear();

            Assert.Near(1, registry.GetOrNull(3).GetFamily("proprietary").Progress);
            Assert.Equal(2, registry.CpuBindingCount);
            Assert.Equal("PT-386", registry.Pending[0].name);
            Assert.Equal(2, registry.Milestones.Count);
        }

        [Test]
        public void ToSave_PutsKnownFamiliesInCatalogOrder_ThenTheUnknownByName()
        {
            ArchRegistry registry = NewRegistry();
            CompanyArchState company = registry.GetOrCreate(1, false);
            foreach (string id in new[] { "zz_unknown", "riscv", "aa_unknown", "x86", "proprietary" }) company.GetOrAddFamily(id);

            List<string> ids = registry.ToSave().companies[0].families.Select(f => f.id).ToList();

            Assert.True(ids.SequenceEqual(new[] { "proprietary", "x86", "riscv", "aa_unknown", "zz_unknown" }), string.Join(",", ids));
        }

        // ---- the missing section, the new game ----

        [Test]
        public void ResetToDefaults_EmptiesTheState_AndAsksForTheMigration()
        {
            ArchRegistry registry = Populated();
            Assert.False(registry.MigrationNeeded);

            Section(registry).ResetToDefaults();

            Assert.True(registry.MigrationNeeded);
            Assert.False(registry.Seeded);
            Assert.Equal(0, registry.Companies.Count);
            Assert.Equal(0, registry.CpuBindingCount);
            Assert.Equal(0, registry.Pending.Count);
            Assert.Equal(0, registry.AiOverrides.Count);
            Assert.Equal(0, registry.Milestones.Count);
        }

        [Test]
        public void ASaveWithoutTheSection_ResetsTheState_AndNeedsTheMigration()
        {
            ArchRegistry registry = Populated();
            SideFileModel model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(Section(registry), out problem), problem);

            model.ApplyMissing();
            Assert.True(registry.MigrationNeeded);
            Assert.Equal(0, registry.Companies.Count);

            // A side file that has other features' sections but not this one.
            ArchRegistry second = Populated();
            SideFileModel other = new SideFileModel();
            Assert.True(other.TryRegister(Section(second), out problem), problem);
            other.Apply("{\"schema\":1,\"features\":{\"mod-framework\":{\"_schema\":1}}}");
            Assert.True(second.MigrationNeeded);
            Assert.Equal(0, second.Companies.Count);

            // A new game starts from defaults too.
            ArchRegistry third = Populated();
            SideFileModel fresh = new SideFileModel();
            Assert.True(fresh.TryRegister(Section(third), out problem), problem);
            fresh.StartNewGame();
            Assert.True(third.MigrationNeeded);
        }

        [Test]
        public void ASectionWithoutSchema_IsReadAsVersion1_AndTheCoreWritesTheVersion()
        {
            ArchRegistry source = Populated();
            string section = Text(Section(source).Write());

            ArchRegistry registry = NewRegistry();
            SideFileModel model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(Section(registry), out problem), problem);
            SideFileReadReport report = model.Apply("{\"schema\":1,\"features\":{\"cpu-architectures\":" + section + "}}");

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.False(registry.MigrationNeeded);
            Assert.Equal(2, registry.Companies.Count);

            JsonObject features = model.Build(null).Document.GetObject("features");
            JsonObject written = features.GetObject("cpu-architectures");
            Assert.Equal(1, written.GetInt32("_schema"), "the core writes the version of the section");
            Assert.True(written.GetBool("seeded"));
            Assert.Equal(2, written.GetArray("companies").Count);
        }

        [Test]
        public void ASectionNewerThanTheMod_IsNotRead_AndIsKeptAsItWas()
        {
            ArchRegistry registry = Populated();
            SideFileModel model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(Section(registry), out problem), problem);

            SideFileReadReport report = model.Apply("{\"schema\":1,\"features\":{\"cpu-architectures\":{\"_schema\":2,\"seeded\":true,\"future\":[1,2,3]}}}");

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.True(report.NewerSections.Contains("cpu-architectures"));
            Assert.True(registry.MigrationNeeded, "the feature starts from defaults and migrates in memory");
            Assert.Equal(0, registry.Companies.Count);

            JsonObject written = model.Build(null).Document.GetObject("features").GetObject("cpu-architectures");
            Assert.Equal(2, written.GetInt32("_schema"), "the stored section is written back as it was");
            Assert.Equal(3, written.GetArray("future").Count);
        }

        [Test]
        public void ReadingANullSection_ClearsTheState()
        {
            ArchRegistry registry = Populated();

            Section(registry).Read(null, 1);

            Assert.True(registry.MigrationNeeded);
            Assert.Equal(0, registry.Companies.Count);
        }

        [Test]
        public void ASectionThatIsNotSeeded_StillNeedsTheMigration()
        {
            ArchRegistry registry = NewRegistry();

            Section(registry).Read(Parse("{\"seeded\":false,\"companies\":[{\"companyId\":3,\"isPlayer\":true,\"families\":[]}]}"), 1);

            Assert.True(registry.MigrationNeeded, "nobody sowed it");
            Assert.Equal(1, registry.Companies.Count);
            registry.MarkSeeded();
            Assert.False(registry.MigrationNeeded);
            Assert.True(Section(registry).Write().GetBool("seeded"));
        }

        // ---- values out of range, limits, broken records ----

        [Test]
        public void Load_BringsValuesBackIntoTheirRanges_WithWarnings()
        {
            var data = new ArchSaveData { seeded = true };
            var company = new CompanyArchSave { companyId = 5, isPlayer = true };
            company.families.Add(new FamilySave { id = "x86", progress = 1.5f, support = 250f });
            company.families.Add(new FamilySave { id = "arm", progress = -0.5f, support = -3f });
            company.families.Add(new FamilySave { id = "risc", progress = float.NaN, support = float.PositiveInfinity });
            company.families.Add(new FamilySave { id = "riscv", progress = 1f, support = 100f });
            data.companies.Add(company);
            var warnings = new List<string>();

            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);
            CompanyArchState state = registry.GetOrNull(5);

            Assert.Near(1, state.GetFamily("x86").Progress);
            Assert.Near(100, state.GetFamily("x86").Support);
            Assert.Near(0, state.GetFamily("arm").Progress);
            Assert.Near(0, state.GetFamily("arm").Support);
            Assert.Near(0, state.GetFamily("risc").Progress);
            Assert.Near(0, state.GetFamily("risc").Support);
            Assert.Near(1, state.GetFamily("riscv").Progress);
            Assert.Near(100, state.GetFamily("riscv").Support);
            Assert.Equal(6, warnings.Count, ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "companies[0].families[0].progress", "1.5"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "families[0].support", "250"), ArchTestKit.Joined(warnings));
        }

        [Test]
        public void Load_UnknownWords_BecomeTheDefaults_WithWarnings()
        {
            var data = new ArchSaveData();
            var company = new CompanyArchSave { companyId = 5, pauseReason = "banana", paused = true };
            company.families.Add(new FamilySave { id = "x86", tier = "ultra", researchedYear = 5, researchedMonth = 13 });
            company.families.Add(new FamilySave { id = "arm", tier = null, researchedYear = 1990, researchedMonth = 12 });
            data.companies.Add(company);
            var warnings = new List<string>();

            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);
            CompanyArchState state = registry.GetOrNull(5);

            Assert.Equal(PauseReason.None, state.PauseReason);
            Assert.True(state.Paused, "the flag itself is kept");
            Assert.Equal(SupportTier.Off, state.GetFamily("x86").Tier);
            Assert.Equal(0, state.GetFamily("x86").ResearchedYear);
            Assert.Equal(0, state.GetFamily("x86").ResearchedMonth);
            Assert.Equal(SupportTier.Off, state.GetFamily("arm").Tier);
            Assert.Equal(1990, state.GetFamily("arm").ResearchedYear);
            Assert.Equal(12, state.GetFamily("arm").ResearchedMonth);
            Assert.True(ArchTestKit.AnyContains(warnings, "pauseReason", "banana"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "tier", "ultra"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "researchedYear"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "researchedMonth"), ArchTestKit.Joined(warnings));
        }

        [Test]
        public void Load_ResumeTech_IsHeldToTheNumberOfTechnologies()
        {
            Func<int, int> load = value =>
            {
                var data = new ArchSaveData();
                data.companies.Add(new CompanyArchSave { companyId = 1, resumeTech = value });
                var registry = new ArchRegistry(ArchTestKit.BuiltInCatalog());
                registry.Load(data, new List<string>(), 99);
                return registry.GetOrNull(1).ResumeTechnologyId;
            };

            Assert.Equal(-1, load(-1));
            Assert.Equal(0, load(0));
            Assert.Equal(41, load(41));
            Assert.Equal(99, load(99));
            Assert.Equal(-1, load(100), "above the last technology");
            Assert.Equal(-1, load(-5), "below -1");
            Assert.Equal(-1, load(int.MinValue));
        }

        [Test]
        public void Load_DropsTheRecordsThatCannotBeUsed_AndKeepsTheOthers()
        {
            var data = new ArchSaveData();
            data.companies.Add(null);
            data.companies.Add(new CompanyArchSave { companyId = -1 });
            data.companies.Add(new CompanyArchSave { companyId = 4, isPlayer = true, active = "Bad Id", current = "x86" });
            data.companies.Add(new CompanyArchSave { companyId = 4, isPlayer = false });        // a second company 4
            var good = new CompanyArchSave { companyId = 9 };
            good.families.Add(null);
            good.families.Add(new FamilySave { id = "Bad Id" });
            good.families.Add(new FamilySave { id = "x86", researched = true });
            good.families.Add(new FamilySave { id = "x86", researched = false });                // a second x86
            data.companies.Add(good);
            data.companies.Add(new CompanyArchSave { companyId = 12, families = null });

            data.cpuArch.Add(null);
            data.cpuArch.Add(new CpuArchSave { cpu = -1, arch = "x86" });
            data.cpuArch.Add(new CpuArchSave { cpu = 5, arch = "BAD" });
            data.cpuArch.Add(new CpuArchSave { cpu = 6, arch = null });
            data.cpuArch.Add(new CpuArchSave { cpu = 7, arch = "x86" });
            data.cpuArch.Add(new CpuArchSave { cpu = 7, arch = "arm" });                          // a second binding of cpu 7

            data.pending.Add(null);
            data.pending.Add(new PendingSave { company = -1, name = "a", arch = "x86" });
            data.pending.Add(new PendingSave { company = 1, name = null, arch = "x86" });
            data.pending.Add(new PendingSave { company = 1, name = "b", arch = "BAD" });
            data.pending.Add(new PendingSave { company = 1, name = "c", arch = "x86", ips = float.NaN });
            data.pending.Add(new PendingSave { company = 1, name = "d", arch = "x86", ips = -1f });
            data.pending.Add(new PendingSave { company = 1, name = "ok", year = 1990, ips = 10f, arch = "x86" });

            data.aiOverrides = null;
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 0 });
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 101 });
            data.milestones.Add(new MilestoneSave { arch = "BAD", threshold = 25 });
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 25 });
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 25 });             // twice

            var warnings = new List<string>();
            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.True(registry.Companies.Select(c => c.CompanyId).SequenceEqual(new[] { 4, 9, 12 }), "ordered by id, the broken ones dropped");
            Assert.True(registry.GetOrNull(4).IsPlayer, "the first company 4 stays");
            Assert.Null(registry.GetOrNull(4).Active, "a malformed family id is ignored");
            Assert.Equal("x86", registry.GetOrNull(4).CurrentResearch);
            Assert.Equal(1, registry.GetOrNull(9).Families.Count);
            Assert.True(registry.GetOrNull(9).GetFamily("x86").Researched, "the first x86 stays");
            Assert.Equal(0, registry.GetOrNull(12).Families.Count);

            Assert.Equal(1, registry.CpuBindingCount);
            Assert.Equal("x86", registry.GetCpuArch(7), "the first binding stays");

            Assert.Equal(1, registry.Pending.Count);
            Assert.Equal("ok", registry.Pending[0].name);

            Assert.Equal(0, registry.AiOverrides.Count);
            Assert.Equal(1, registry.Milestones.Count);
            Assert.True(warnings.Count >= 15, "every drop is reported: " + warnings.Count);
        }

        [Test]
        public void Load_CutsTheListsAtTheirLimits_WithAWarning()
        {
            var data = new ArchSaveData { seeded = true };
            for (int i = 0; i < ArchRegistry.MaxCpuBindings + 7; i++) data.cpuArch.Add(new CpuArchSave { cpu = i, arch = "x86" });
            for (int i = 0; i < ArchRegistry.MaxPending + 3; i++) data.pending.Add(new PendingSave { company = 1, name = "cpu" + i, year = 1990, ips = 1f, arch = "x86" });
            var warnings = new List<string>();

            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.Equal(5000, ArchRegistry.MaxCpuBindings);
            Assert.Equal(200, ArchRegistry.MaxPending);
            Assert.Equal(ArchRegistry.MaxCpuBindings, registry.CpuBindingCount);
            Assert.Equal("x86", registry.GetCpuArch(0));
            Assert.Equal("x86", registry.GetCpuArch(ArchRegistry.MaxCpuBindings - 1));
            Assert.Null(registry.GetCpuArch(ArchRegistry.MaxCpuBindings), "the part over the limit is dropped");
            Assert.Equal(ArchRegistry.MaxPending, registry.Pending.Count);
            Assert.Equal("cpu0", registry.Pending[0].name, "the oldest waiting projects are kept");
            Assert.True(ArchTestKit.AnyContains(warnings, "more than 5000"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "more than 200"), ArchTestKit.Joined(warnings));
            Assert.True(warnings.Count <= 4, "one warning per list, not one per record: " + warnings.Count);
        }

        [Test]
        public void Load_KeepsTheFamiliesTheCatalogDoesNotKnow_AsTheyAre()
        {
            var data = new ArchSaveData();
            var company = new CompanyArchSave { companyId = 2, active = "mips", current = "mips" };
            company.families.Add(new FamilySave { id = "mips", researched = true, progress = 1f, support = 40f, tier = "low" });
            data.companies.Add(company);
            data.cpuArch.Add(new CpuArchSave { cpu = 10, arch = "mips" });
            data.cpuArch.Add(new CpuArchSave { cpu = 11, arch = "mips" });
            data.pending.Add(new PendingSave { company = 2, name = "R4000", year = 1991, ips = 5f, arch = "mips" });
            var warnings = new List<string>();

            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.Equal("mips", registry.GetOrNull(2).Active);
            Assert.True(registry.GetOrNull(2).GetFamily("mips").Researched);
            Assert.Equal("mips", registry.GetCpuArch(10));
            Assert.Equal("mips", registry.GetCpuArch(11));
            Assert.Equal("mips", registry.Pending[0].arch);
            Assert.False(registry.Catalog.Contains("mips"));
            Assert.Equal(1, warnings.Count(w => w.Contains("'mips'")), "the family is noted once: " + ArchTestKit.Joined(warnings));

            // And it survives the next write.
            ArchSaveData again = registry.ToSave();
            Assert.Equal("mips", again.cpuArch[0].arch);
            Assert.Equal("mips", again.companies[0].families[0].id);
            Assert.Equal("low", again.companies[0].families[0].tier);
        }

        [Test]
        public void Read_ReportsAMemberOfTheWrongKind_AndDropsTheRecordWithoutAnId()
        {
            ArchRegistry registry = NewRegistry();
            var warnings = new List<string>();
            JsonObject section = Parse("{\"seeded\":true,\"companies\":[" +
                                       "{\"companyId\":\"three\",\"isPlayer\":true}," +
                                       "{\"companyId\":4,\"isPlayer\":true,\"families\":[{\"id\":\"x86\",\"progress\":\"high\",\"researched\":true}]}]," +
                                       "\"cpuArch\":[{\"cpu\":\"x\",\"arch\":\"x86\"},{\"cpu\":8,\"arch\":\"arm\"}]}");

            Section(registry, warnings).Read(section, 1);

            Assert.Equal(1, registry.Companies.Count, "company 'three' has no usable id");
            Assert.Equal(4, registry.Companies[0].CompanyId);
            Assert.Near(0, registry.Companies[0].GetFamily("x86").Progress, 0, "a progress of the wrong kind keeps the default");
            Assert.True(registry.Companies[0].GetFamily("x86").Researched);
            Assert.Equal(1, registry.CpuBindingCount);
            Assert.Equal("arm", registry.GetCpuArch(8));
            Assert.True(ArchTestKit.AnyContains(warnings, "companyId"), ArchTestKit.Joined(warnings));
            Assert.True(ArchTestKit.AnyContains(warnings, "progress"), ArchTestKit.Joined(warnings));
        }

        [Test]
        public void Read_ASectionOfTheWrongShape_GivesAnEmptyState_NotAnException()
        {
            ArchRegistry registry = Populated();
            var warnings = new List<string>();

            Section(registry, warnings).Read(Parse("{\"companies\":\"none\",\"cpuArch\":5,\"pending\":{}}"), 1);

            Assert.Equal(0, registry.Companies.Count);
            Assert.Equal(0, registry.CpuBindingCount);
            Assert.True(registry.MigrationNeeded);
            Assert.True(warnings.Count >= 3, ArchTestKit.Joined(warnings));
        }

        [Test]
        public void Read_ANullListsInTheSection_AreEmptyLists()
        {
            ArchRegistry registry = NewRegistry();

            Section(registry).Read(Parse("{\"seeded\":true,\"companies\":null,\"cpuArch\":null,\"pending\":null,\"aiOverrides\":null,\"milestones\":null}"), 1);

            Assert.True(registry.Seeded);
            Assert.Equal(0, registry.Companies.Count);
            Assert.Equal(0, registry.CpuBindingCount);
        }

        [Test]
        public void Warnings_LongHostileIds_AreShortened()
        {
            var data = new ArchSaveData();
            data.cpuArch.Add(new CpuArchSave { cpu = 1, arch = new string('x', 5000) + "!" });
            var warnings = new List<string>();

            ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Length < 200, "a hostile id must not flood the log: " + warnings[0].Length);
        }

        [Test]
        public void ASectionWarningCallback_ThatThrows_DoesNotBreakTheRead()
        {
            ArchRegistry registry = NewRegistry();
            var section = new ArchSaveSection(registry, line => { throw new InvalidOperationException("log is broken"); });

            section.Read(Parse("{\"cpuArch\":[{\"cpu\":-1,\"arch\":\"x86\"},{\"cpu\":2,\"arch\":\"x86\"}]}"), 1);

            Assert.Equal("x86", registry.GetCpuArch(2));
        }

        // ---- the registry ----

        [Test]
        public void Registry_KeepsCompaniesOrderedById_AndFindsThePlayer()
        {
            ArchRegistry registry = NewRegistry();
            registry.GetOrCreate(30, false);
            registry.GetOrCreate(7, true);
            registry.GetOrCreate(12, false);

            Assert.True(registry.Companies.Select(c => c.CompanyId).SequenceEqual(new[] { 7, 12, 30 }));
            Assert.Equal(7, registry.Player.CompanyId);
            Assert.True(ReferenceEquals(registry.GetOrNull(12), registry.GetOrCreate(12, true)), "an existing state is returned as it is");
            Assert.False(registry.GetOrNull(12).IsPlayer);
            Assert.Null(registry.GetOrNull(99));

            Assert.True(registry.Remove(12));
            Assert.False(registry.Remove(12));
            Assert.True(registry.Companies.Select(c => c.CompanyId).SequenceEqual(new[] { 7, 30 }));
            Assert.Null(NewRegistry().Player);
        }

        [Test]
        public void Registry_Bind_ReplacesTheBinding_AndRefusesBadIds()
        {
            ArchRegistry registry = NewRegistry();

            Assert.True(registry.Bind(5, "x86"));
            Assert.True(registry.Bind(5, "arm"));
            Assert.Equal("arm", registry.GetCpuArch(5));
            Assert.Equal(1, registry.CpuBindingCount);
            string family;
            Assert.True(registry.TryGetCpuArch(5, out family));
            Assert.Equal("arm", family);
            Assert.False(registry.TryGetCpuArch(6, out family));

            Assert.False(registry.Bind(-1, "x86"));
            Assert.False(registry.Bind(7, null));
            Assert.False(registry.Bind(7, "X86"));
            Assert.False(registry.Bind(7, ""));
            Assert.True(registry.Bind(7, "mips"), "a family the catalog does not know is bound as it is");
            Assert.Equal(2, registry.CpuBindingCount);
        }

        [Test]
        public void Registry_Pending_IsFifo_AndTheOldestMatchIsTaken()
        {
            ArchRegistry registry = NewRegistry();
            registry.AddPending(new PendingSave { company = 1, name = "PT-1", year = 1990, ips = 100f, arch = "x86" });
            registry.AddPending(new PendingSave { company = 1, name = "PT-1", year = 1990, ips = 100f, arch = "arm" });
            registry.AddPending(new PendingSave { company = 2, name = "PT-1", year = 1990, ips = 100f, arch = "risc" });

            PendingSave first = registry.TakePending(p => p.company == 1 && p.name == "PT-1");
            PendingSave second = registry.TakePending(p => p.company == 1 && p.name == "PT-1");
            PendingSave third = registry.TakePending(p => p.company == 1 && p.name == "PT-1");

            Assert.Equal("x86", first.arch, "the oldest project with the same fingerprint goes first");
            Assert.Equal("arm", second.arch);
            Assert.Null(third);
            Assert.Equal(1, registry.Pending.Count);
            Assert.Equal("risc", registry.Pending[0].arch);
            Assert.Null(registry.TakePending(null));
            Assert.Null(registry.TakePending(p => false));
        }

        [Test]
        public void Registry_Pending_WhenFull_DropsTheOldest()
        {
            ArchRegistry registry = NewRegistry();
            for (int i = 0; i < ArchRegistry.MaxPending + 5; i++)
                Assert.True(registry.AddPending(new PendingSave { company = 1, name = "cpu" + i, year = 1990, ips = 1f, arch = "x86" }));

            Assert.Equal(ArchRegistry.MaxPending, registry.Pending.Count);
            Assert.Equal("cpu5", registry.Pending[0].name);
            Assert.Equal("cpu" + (ArchRegistry.MaxPending + 4), registry.Pending[ArchRegistry.MaxPending - 1].name);

            Assert.False(registry.AddPending(null));
            Assert.False(registry.AddPending(new PendingSave { company = -1, name = "a", arch = "x86" }));
            Assert.False(registry.AddPending(new PendingSave { company = 1, name = null, arch = "x86" }));
            Assert.False(registry.AddPending(new PendingSave { company = 1, name = "a", arch = "BAD" }));
        }

        [Test]
        public void Registry_Clear_ForgetsEverything_IncludingTheSeededFlag()
        {
            ArchRegistry registry = Populated();
            Assert.True(registry.Seeded);

            registry.Clear();

            Assert.False(registry.Seeded);
            Assert.True(registry.MigrationNeeded);
            Assert.Equal(0, registry.Companies.Count);
            Assert.Null(registry.GetOrNull(3));
        }

        [Test]
        public void Registry_NotifiesTheSubscribers_AndOneThatThrowsDoesNotStopTheOthers()
        {
            ArchRegistry registry = NewRegistry();
            var seen = new List<int>();
            registry.OnCompanyStateChanged += id => { throw new InvalidOperationException("broken subscriber"); };
            registry.OnCompanyStateChanged += id => seen.Add(id);

            registry.NotifyCompanyChanged(7);
            registry.NotifyCompanyChanged(8);

            Assert.True(seen.SequenceEqual(new[] { 7, 8 }));
            NewRegistry().NotifyCompanyChanged(1);   // nobody listens: nothing happens
        }

        [Test]
        public void Registry_NeedsACatalog()
        {
            Assert.Throws<ArgumentNullException>(() => new ArchRegistry(null));
            Assert.Throws<ArgumentNullException>(() => new ArchSaveSection(null));
        }

        [Test]
        public void CompanyState_FindsAndAddsFamilies()
        {
            var company = new CompanyArchState(3, true);

            Assert.Equal(3, company.CompanyId);
            Assert.True(company.IsPlayer);
            Assert.Equal(-1, company.ResumeTechnologyId);
            Assert.Equal(PauseReason.None, company.PauseReason);
            Assert.Null(company.GetFamily("x86"));
            Assert.Null(company.GetFamily(null));
            Assert.False(company.IsResearched("x86"));

            FamilyState added = company.GetOrAddFamily("x86");
            Assert.Equal("x86", added.Id);
            Assert.False(added.Researched);
            Assert.Equal(SupportTier.Off, added.Tier);
            Assert.True(ReferenceEquals(added, company.GetOrAddFamily("x86")));
            added.Researched = true;
            Assert.True(company.IsResearched("x86"));
            Assert.Throws<ArgumentNullException>(() => company.GetOrAddFamily(null));
        }

        // ---- TSK-031: the compatibility of the saves (NFR-3, FR-8, FR-9, TSK-018) ----

        /// <summary>
        /// A save of stage 1 as the stage-1 game writes it: the player and two AI companies sown by the seed (so they carry the support levels and tiers of the seed, no date of
        /// research), a research of the player under way, bindings of CPUs and a waiting project; nothing of the lists of stage 2 has an entry.
        /// </summary>
        private static ArchRegistry StageOneGame()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            AiPreferenceTable preferences = ArchTestKit.BuiltInPreferences(catalog);
            Assert.Equal(BalanceConfig.Stage1, balance.stage, "the built-in balance is the one of stage 1");

            var registry = new ArchRegistry(catalog, preferences, balance.seed);
            CompanyArchState player = registry.GetOrCreate(3, true);
            ArchSeedPolicy.SeedPlayer(catalog, 1993, balance.seed).ApplyTo(player);
            player.CurrentResearch = "risc";
            player.GetOrAddFamily("risc").Progress = 0.4f;
            player.ResumeTechnologyId = 41;

            ArchSeedPolicy.SeedAi(catalog, preferences.Match(0), 1993, balance.seed).ApplyTo(registry.GetOrCreate(17, false));
            ArchSeedPolicy.SeedAi(catalog, preferences.Match(3), 1993, balance.seed).ApplyTo(registry.GetOrCreate(21, false));

            registry.Bind(1043, "x86");
            registry.Bind(1057, "proprietary");
            registry.AddPending(new PendingSave { company = 3, name = "PT-486", year = 1993, ips = 9100000f, arch = "x86" });
            registry.MarkSeeded();
            return registry;
        }

        private static ArchRegistry Reopened(ArchRegistry source, List<string> warnings)
        {
            string section = Text(Section(source).Write());
            ArchRegistry registry = NewRegistry();
            SideFileModel model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(Section(registry, warnings), out problem), problem);
            SideFileReadReport report = model.Apply("{\"schema\":1,\"features\":{\"cpu-architectures\":" + section + "}}");
            Assert.Equal(SideFileStatus.Loaded, report.Status);
            return registry;
        }

        [Test]
        public void AStageOneSave_OpensInStageTwo_WithoutAMigration_AndWithoutAWarning()
        {
            ArchRegistry game = StageOneGame();
            JsonObject written = Section(game).Write();

            // The fields of stage 2 are written from stage 1 on: the support level and the tier of every family, the two lists (empty).
            var firstCompany = (JsonObject)written.GetArray("companies").First();
            var firstFamily = (JsonObject)firstCompany.GetArray("families").First();
            Assert.True(firstFamily.ContainsKey("support") && firstFamily.ContainsKey("tier") && firstFamily.ContainsKey("researchedYear") && firstFamily.ContainsKey("researchedMonth"),
                        "a family carries the fields of stage 2");
            Assert.Equal(0, written.GetArray("aiOverrides").Count);
            Assert.Equal(0, written.GetArray("milestones").Count);
            Assert.True(written.GetBool("seeded"));

            var warnings = new List<string>();
            ArchRegistry reopened = Reopened(game, warnings);
            Assert.Equal(0, warnings.Count, "nothing to complain about: " + ArchTestKit.Joined(warnings));
            Assert.False(reopened.MigrationNeeded, "FR-8 item 3: a save of the mod is not migrated again");
            Assert.True(reopened.Seeded);
            Assert.Equal(3, reopened.Companies.Count);
            Assert.Equal(Text(written), Text(Section(reopened).Write()), "the state is the same after the trip");
        }

        [Test]
        public void TheStageTwoSteps_RunOnAStageOneSave_FromTheSeedValues()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;
            ArchRegistry registry = Reopened(StageOneGame(), new List<string>());
            CompanyArchState player = registry.GetOrNull(3);

            // The support: the player got the proprietary ISA and x86 with the seed (U = 1993), the newest (x86) at 50 on 'normal', the other at 30 on 'off'; the month moves the
            // levels towards the 70 of the tier and towards 0.
            FamilyState newest = player.GetFamily("x86");
            Assert.True(newest != null && newest.Researched && newest.Tier == SupportTier.Normal && Math.Abs(newest.Support - 50f) < 1e-3f, "the seed of the player");
            SupportMonthResult month = SupportCycle.RunMonth(player, catalog, balance.support, 1993, 1.1f, float.PositiveInfinity);
            Assert.True(month.Upkeep > 0f, "the upkeep of the family on 'normal'");
            Assert.True(month.Changed);
            Assert.Near(52.0, player.GetFamily("x86").Support, 1e-3, "50 + (70 - 50) x 0.10");
            Assert.True(player.GetFamily("proprietary").Support < 30f, "the family on 'off' decays from the 30 of the seed");

            // The immature tools: the families the seed gave have no date of research, so they carry no surcharge.
            var today = new DateTime(1993, 6, 1);
            foreach (FamilyState family in player.Families.Values) Assert.Near(0.0, ImmatureTools.Surcharge(family, today, balance.migration), 1e-9, family.Id);

            // The milestones: the list is empty, so the first look at the market records what has been passed long ago and sends no letter for it.
            Func<int, bool> announced = threshold => registry.Milestones.Exists(m => m.arch == "x86" && m.threshold == threshold);
            List<int> passed = EcosystemMilestones.Passed(0.62f, balance.ecosystem.milestones, announced);
            Assert.True(passed.SequenceEqual(new[] { 25, 50 }), string.Join(",", passed.Select(p => p.ToString()).ToArray()));
            foreach (int threshold in passed) registry.Milestones.Add(new MilestoneSave { arch = "x86", threshold = threshold });
            Assert.Equal(0, EcosystemMilestones.Crossed(0.60f, 0.63f, balance.ecosystem.milestones, announced).Count, "and the next month announces nothing again");

            // The decision of an AI company reads its state and the weights; the overrides are none.
            CompanyArchState intel = registry.GetOrNull(17);
            AiPreference preference = ArchTestKit.BuiltInPreferences(catalog).Match(0);
            Assert.True(ReferenceEquals(preference, AiOverrides.Apply(preference, 17, registry.AiOverrides)), "no override: the record itself");
            var view = new AiView { CompanyId = 17, UniqueId = 0, Money = 1e10f, Year = 1993, Difficulty = 2, State = intel, SegmentShare = new[] { 0.3f, 0.4f, 0.3f, 0f } };
            AiDecision decision = ArchAiPolicy.DecideFull(view, preference, catalog, balance.ai, null, balance.rating, balance.support);
            Assert.NotNull(decision);

            // And the state written afterwards is a valid section of the same version.
            ArchRegistry again = Reopened(registry, new List<string>());
            Assert.Equal(registry.Milestones.Count, again.Milestones.Count);
            Assert.Near(52.0, again.GetOrNull(3).GetFamily("x86").Support, 1e-3);
        }

        [Test]
        public void ASectionOfAnOlderShape_WithoutTheListsOfLaterStages_IsRead_NothingMissingIsAnError()
        {
            const string old = "{\"seeded\":true,\"companies\":[{\"companyId\":3,\"isPlayer\":true,\"active\":\"x86\",\"families\":[{\"id\":\"proprietary\",\"researched\":true,\"progress\":1},"
                               + "{\"id\":\"x86\",\"researched\":true,\"progress\":1}]}]}";
            var warnings = new List<string>();
            ArchRegistry registry = NewRegistry();
            Section(registry, warnings).Read(Parse(old), 1);

            Assert.Equal(0, warnings.Count, ArchTestKit.Joined(warnings));
            Assert.False(registry.MigrationNeeded);
            CompanyArchState player = registry.Player;
            Assert.Equal("x86", player.Active);
            Assert.Near(0.0, player.GetFamily("x86").Support, 1e-9, "a family without a support level has none");
            Assert.Equal(SupportTier.Off, player.GetFamily("x86").Tier);
            Assert.Equal(-1, player.ResumeTechnologyId);
            Assert.Equal(PauseReason.None, player.PauseReason);
            Assert.Equal(0, registry.CpuBindingCount + registry.Pending.Count + registry.AiOverrides.Count + registry.Milestones.Count);
        }

        [Test]
        public void TheListsOfStageTwo_BrokenRecords_AreDropped_TheRestIsKept_AndTheBoundariesHold()
        {
            var data = new ArchSaveData { seeded = true };
            data.aiOverrides.Add(new AiOverrideSave { company = 17, arch = "arm", weight = 2f });
            data.aiOverrides.Add(new AiOverrideSave { company = 17, arch = "arm", weight = 3f });         // the same company and family twice
            data.aiOverrides.Add(new AiOverrideSave { company = -4, arch = "arm", weight = 1f });
            data.aiOverrides.Add(new AiOverrideSave { company = 18, arch = "Bad Id", weight = 1f });
            data.aiOverrides.Add(new AiOverrideSave { company = 19, arch = "risc", weight = float.NaN });  // held to the lowest weight
            data.aiOverrides.Add(new AiOverrideSave { company = 20, arch = "risc", weight = 99f });         // held to the highest
            data.aiOverrides.Add(null);
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 1 });                       // the bounds of 1..100 are in
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = 100 });
            data.milestones.Add(new MilestoneSave { arch = "x86", threshold = -25 });
            data.milestones.Add(new MilestoneSave { arch = null, threshold = 50 });
            data.milestones.Add(null);

            var warnings = new List<string>();
            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.Equal(3, registry.AiOverrides.Count, "17/arm (the first), 19/risc and 20/risc");
            Assert.Near(2.0, AiOverrides.Get(registry.AiOverrides, 17, "arm").Value, 1e-9, "the first record stays");
            Assert.Near(0.0, AiOverrides.Get(registry.AiOverrides, 19, "risc").Value, 1e-9);
            Assert.Near(AiPreference.MaxWeight, AiOverrides.Get(registry.AiOverrides, 20, "risc").Value, 1e-9);
            Assert.Equal(2, registry.Milestones.Count);
            Assert.True(registry.Milestones.Any(m => m.threshold == 1) && registry.Milestones.Any(m => m.threshold == 100));
            Assert.True(warnings.Count >= 6, "every drop and every correction is reported: " + warnings.Count);

            // And what is left is written again as a valid section.
            ArchSaveData again = registry.ToSave();
            Assert.Equal(3, again.aiOverrides.Count);
            Assert.Equal(2, again.milestones.Count);
        }

        [Test]
        public void AFamilyTheCatalogHasLost_StaysInTheSave_AndEveryStageTwoStepGoesPastIt()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;

            // The user's content had a family 'mips'; the file is gone now. A company researched it, a CPU is built on it, a project waits for it.
            var data = new ArchSaveData { seeded = true };
            var company = new CompanyArchSave { companyId = 2, active = "mips", current = "mips" };
            company.families.Add(new FamilySave { id = "proprietary", researched = true, progress = 1f, support = 55f, tier = "normal" });
            company.families.Add(new FamilySave { id = "mips", researched = true, progress = 1f, support = 80f, tier = "high", researchedYear = 1990, researchedMonth = 2 });
            data.companies.Add(company);
            data.cpuArch.Add(new CpuArchSave { cpu = 77, arch = "mips" });
            data.pending.Add(new PendingSave { company = 2, name = "R4000", year = 1991, ips = 5f, arch = "mips" });
            data.aiOverrides.Add(new AiOverrideSave { company = 2, arch = "mips", weight = 1.5f });
            data.milestones.Add(new MilestoneSave { arch = "mips", threshold = 25 });

            var warnings = new List<string>();
            ArchRegistry registry = ArchRegistry.FromSave(data, catalog, warnings);
            CompanyArchState state = registry.GetOrNull(2);
            Assert.False(catalog.Contains("mips"));

            // Rated as ×1: the provider asks the catalog for the family and gets nothing.
            Assert.Near(1.0, RatingForecast.One(new RatingInputs(), catalog.Get("mips"), Segment.HighEnd, 1995, balance, 80f, 0.3f, 4, true, 1f), 1e-9);

            // The support goes on for the families the catalog knows and does not touch the lost one (its level and tier stay as they were, nothing is charged for it).
            SupportMonthResult month = SupportCycle.RunMonth(state, catalog, balance.support, 1995, 1.2f, float.PositiveInfinity);
            Assert.Equal(1, month.Families, "only proprietary takes part");
            Assert.Near(80.0, state.GetFamily("mips").Support, 1e-9);
            Assert.Equal(SupportTier.High, state.GetFamily("mips").Tier);
            Assert.Equal(SupportSetResult.UnknownFamily, SupportCycle.SetTier(state, catalog, "mips", SupportTier.Off, false));

            // The decision of the company designs on what the catalog has: the lost family is not 'active' any more, the company moves to a known one.
            AiPreference pref = ArchTestKit.BuiltInPreferences(catalog).Match(7);
            var view = new AiView { CompanyId = 2, UniqueId = 7, Money = 1e10f, Year = 1995, State = state, SegmentShare = new[] { 0.5f, 0.2f, 0.1f, 0.2f } };
            AiDecision decision = ArchAiPolicy.DecideFull(view, pref, catalog, balance.ai, null, balance.rating, balance.support);
            Assert.Equal("proprietary", decision.NewActive, "a company designs on what it has");
            Assert.False(decision.Tiers != null && decision.Tiers.ContainsKey("mips"), "no tier is asked for a family the catalog lost");

            // The ecosystem and the royalty see no such family; the merge of an absorbed company carries it over as it is.
            var sales = new Dictionary<string, long>(StringComparer.Ordinal) { { "proprietary", 600 } };
            Assert.Near(1.0, EcosystemMath.Shares(sales, "proprietary")["proprietary"], 1e-9);
            var buyer = new CompanyArchState(5, true);
            StateTransfer.Merge(state, buyer, true);
            Assert.True(buyer.IsResearched("mips"));

            // Everything is still in the save, byte for byte.
            ArchSaveData again = registry.ToSave();
            Assert.Equal("mips", again.companies[0].active);
            Assert.Equal("mips", again.cpuArch[0].arch);
            Assert.Equal("mips", again.pending[0].arch);
            Assert.Equal("mips", again.aiOverrides[0].arch);
            Assert.Equal("mips", again.milestones[0].arch);
            Assert.Equal("high", again.companies[0].families.First(f => f.id == "mips").tier);
        }

        [Test]
        public void ResumeTech_OutOfRange_IsForgotten_AndTheLabGivesBackOnlyWhatExists()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Func<int, bool, int> loadAndFree = (value, slotEmpty) =>
            {
                var data = new ArchSaveData { seeded = true };
                data.companies.Add(new CompanyArchSave { companyId = 3, isPlayer = true, current = "x86", resumeTech = value });
                var registry = new ArchRegistry(catalog);
                registry.Load(data, new List<string>(), 120);
                return LabRules.TakeReturnTarget(registry.GetOrNull(3), slotEmpty);
            };

            Assert.Equal(41, loadAndFree(41, true), "a technology of the game goes back to the empty slot");
            Assert.Equal(120, loadAndFree(120, true), "the last technology of the game");
            Assert.Equal(-1, loadAndFree(121, true), "a number the game does not have is forgotten at the load");
            Assert.Equal(-1, loadAndFree(int.MaxValue, true));
            Assert.Equal(-1, loadAndFree(-7, true));
            Assert.Equal(-1, loadAndFree(41, false), "the player chose another technology meanwhile: nothing goes back");

            // The tidy-up of the lab forgets the remembered technology in every case.
            var state = new CompanyArchState(3, true) { ResumeTechnologyId = 41 };
            LabRules.TakeReturnTarget(state, false);
            Assert.Equal(-1, state.ResumeTechnologyId);
        }

        [Test]
        public void TheStateOfAnAbsorbedCompany_GoesToTheBuyer_AndTheMergedStateSurvivesASave()
        {
            ArchRegistry registry = NewRegistry();
            CompanyArchState buyer = registry.GetOrCreate(3, true);
            buyer.Active = "x86";
            buyer.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 20f, Tier = SupportTier.Normal };
            buyer.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, Support = 40f, Tier = SupportTier.Normal };

            CompanyArchState target = registry.GetOrCreate(17, false);
            target.Active = "risc";
            target.CurrentResearch = "arm";
            target.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 60f, Tier = SupportTier.Low };
            target.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, Support = 15f, Tier = SupportTier.Off };
            target.Families["risc"] = new FamilyState("risc") { Researched = true, Progress = 1f, ResearchedYear = 1988, ResearchedMonth = 3, Support = 72f, Tier = SupportTier.High };
            target.Families["arm"] = new FamilyState("arm") { Progress = 0.6f };
            registry.Bind(900, "risc");   // a CPU of the absorbed company: bound by its SaveID, the transfer does not touch it

            int changed = StateTransfer.Merge(target, buyer, true);
            registry.Remove(target.CompanyId);

            Assert.Equal(2, changed, "RISC is new to the buyer, the support of proprietary is raised");
            Assert.True(buyer.IsResearched("risc"), "the union of what both researched");
            Assert.Near(72.0, buyer.GetFamily("risc").Support, 1e-9, "the support of the absorbed company");
            Assert.Equal(1988, buyer.GetFamily("risc").ResearchedYear);
            Assert.Near(60.0, buyer.GetFamily("proprietary").Support, 1e-9, "the larger of the two");
            Assert.Near(40.0, buyer.GetFamily("x86").Support, 1e-9, "the buyer's own, the larger");
            Assert.Equal(SupportTier.Off, buyer.GetFamily("risc").Tier, "the tier is the buyer's to choose");
            Assert.Equal(SupportTier.Normal, buyer.GetFamily("proprietary").Tier, "his own tiers stay");
            Assert.Equal("x86", buyer.Active, "he keeps designing on his own family");
            Assert.False(buyer.IsResearched("arm") && buyer.GetFamily("arm").Progress > 0f, "the unfinished research of the absorbed company is not carried over");
            Assert.Null(buyer.CurrentResearch);
            Assert.Null(registry.GetOrNull(17));
            Assert.Equal("risc", registry.GetCpuArch(900), "the CPU stays bound to its family");

            // The merged state is saved and read like any other; merging the same company again changes nothing.
            ArchRegistry reopened = Reopened(registry, new List<string>());
            CompanyArchState again = reopened.GetOrNull(3);
            Assert.Near(72.0, again.GetFamily("risc").Support, 1e-3);
            Assert.Near(60.0, again.GetFamily("proprietary").Support, 1e-3);
            Assert.Equal("risc", reopened.GetCpuArch(900));
            Assert.Equal(0, StateTransfer.Merge(target, again, true), "the second merge finds nothing to add");

            // Without 'keep the best' the support of a family the buyer has stays his; the families he lacks still come over.
            var keepOwn = new CompanyArchState(4, true);
            keepOwn.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 20f };
            StateTransfer.Merge(target, keepOwn, false);
            Assert.Near(20.0, keepOwn.GetFamily("proprietary").Support, 1e-9);
            Assert.True(keepOwn.IsResearched("risc"));
        }

        [Test]
        public void ASaveWithTwoPlayers_OrNone_DoesNotBreakTheRegistry()
        {
            var data = new ArchSaveData { seeded = true };
            data.companies.Add(new CompanyArchSave { companyId = 8, isPlayer = true, active = "x86" });
            data.companies.Add(new CompanyArchSave { companyId = 2, isPlayer = true, active = "arm" });
            var warnings = new List<string>();
            ArchRegistry registry = ArchRegistry.FromSave(data, ArchTestKit.BuiltInCatalog(), warnings);

            Assert.Equal(2, registry.Companies.Count);
            Assert.Equal(2, registry.Player.CompanyId, "the first by id: no exception, the game decides which company is the player's by ModCompanies");
            Assert.Equal("arm", registry.Player.Active);

            var none = new ArchSaveData { seeded = true };
            none.companies.Add(new CompanyArchSave { companyId = 8, isPlayer = false });
            Assert.Null(ArchRegistry.FromSave(none, ArchTestKit.BuiltInCatalog()).Player, "no player yet: the seed of the player waits for his research sector");
        }
    }
}
