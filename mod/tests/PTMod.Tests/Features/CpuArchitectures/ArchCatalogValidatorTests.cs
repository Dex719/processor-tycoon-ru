using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The catalog of families (FR-1, TSK-002): the rules of one record and the rules across records (duplicate ids, unknown dependencies, cycles),
    /// the reading of the JSON record, the lookup of the catalog, and the override of the built-in file by the player's file (AC-1.2).
    /// </summary>
    [Covers(typeof(ArchFamily), typeof(ResearchDef), typeof(DesignDef), typeof(FadeDef), typeof(SupportDef),
            typeof(ArchCatalog), typeof(ArchCatalogValidator), typeof(ValidationResult))]
    public class ArchCatalogValidatorTests
    {
        private static ValidationResult Validate(params ArchFamily[] families)
        {
            return ArchCatalogValidator.Validate(families);
        }

        private static List<string> Messages(ArchFamily family, out bool ok)
        {
            var messages = new List<string>();
            ok = ArchCatalogValidator.ValidateRecord(family, messages);
            return messages;
        }

        // ---- across records ----

        [Test]
        public void Validate_AcceptsAValidList_InTheOrderItCameIn()
        {
            ValidationResult result = Validate(ArchTestKit.Proprietary(), ArchTestKit.Family("x86", 1978), ArchTestKit.Family("x86_64", 2003, "x86"));

            Assert.Equal(0, result.Errors.Count, ArchTestKit.Joined(result.Errors));
            Assert.True(result.HasDefault);
            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary", "x86", "x86_64" }));
        }

        [Test]
        public void Validate_RejectsARepeatedId_AndKeepsTheFirstRecord()
        {
            ArchFamily first = ArchTestKit.Family("x86", 1978);
            ArchFamily second = ArchTestKit.Family("x86", 1990);
            ValidationResult result = Validate(ArchTestKit.Proprietary(), first, second);

            Assert.Equal(2, result.Families.Count);
            Assert.True(ReferenceEquals(first, result.Families[1]), "the first record with the id stays");
            Assert.True(ArchTestKit.AnyContains(result.Errors, "x86", "duplicate"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_SkipsAFamilyWithAnUnknownDependency_AndKeepsTheOthers()
        {
            // AC-1.2: arm64 requires 'armv7', which does not exist: arm64 is skipped, the rest works.
            ValidationResult result = Validate(ArchTestKit.Proprietary(), ArchTestKit.Family("arm", 1987), ArchTestKit.Family("arm64", 2011, "armv7"),
                ArchTestKit.Family("x86", 1978));

            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary", "arm", "x86" }), string.Join(",", ArchTestKit.Ids(result.Families)));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "arm64", "armv7", "unknown"), ArchTestKit.Joined(result.Errors));
            Assert.True(result.HasDefault);
        }

        [Test]
        public void Validate_SkipsTheDependentsOfASkippedFamily()
        {
            ArchFamily broken = ArchTestKit.Family("risc", 1986);
            broken.Design.Ipc = 3f;   // out of range: risc is skipped
            ValidationResult result = Validate(ArchTestKit.Proprietary(), broken, ArchTestKit.Family("risc2", 1990, "risc"),
                ArchTestKit.Family("risc3", 1995, "risc2"));

            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary" }), string.Join(",", ArchTestKit.Ids(result.Families)));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "risc", "design.ipc"), ArchTestKit.Joined(result.Errors));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "risc2", "risc", "skipped"), ArchTestKit.Joined(result.Errors));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "risc3", "risc2", "skipped"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_SkipsTheMembersOfACycle_AndWhatRequiresThem()
        {
            ValidationResult result = Validate(ArchTestKit.Proprietary(),
                ArchTestKit.Family("a", 1980, "b"), ArchTestKit.Family("b", 1980, "a"),
                ArchTestKit.Family("c", 1990, "a"), ArchTestKit.Family("d", 1990, "proprietary"));

            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary", "d" }), string.Join(",", ArchTestKit.Ids(result.Families)));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "cycle", "a", "b"), ArchTestKit.Joined(result.Errors));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "c:", "skipped"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_FindsALongCycle()
        {
            ValidationResult result = Validate(ArchTestKit.Proprietary(),
                ArchTestKit.Family("a", 1980, "c"), ArchTestKit.Family("b", 1980, "a"), ArchTestKit.Family("c", 1980, "b"));

            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary" }));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "cycle"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_RejectsAFamilyThatRequiresItself()
        {
            ValidationResult result = Validate(ArchTestKit.Proprietary(), ArchTestKit.Family("x86", 1978, "x86"));

            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "proprietary" }));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "x86", "itself"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_AcceptsADiamondOfDependencies()
        {
            ValidationResult result = Validate(ArchTestKit.Proprietary(), ArchTestKit.Family("base", 1980),
                ArchTestKit.Family("left", 1985, "base"), ArchTestKit.Family("right", 1985, "base"), ArchTestKit.Family("top", 1990, "left", "right"));

            Assert.Equal(5, result.Families.Count, ArchTestKit.Joined(result.Errors));
            Assert.Equal(0, result.Errors.Count);
        }

        [Test]
        public void Validate_WithoutTheDefaultFamily_HasNoDefault_AndCreatesNoCatalog()
        {
            ValidationResult result = Validate(ArchTestKit.Family("x86", 1978));
            ArchCatalog catalog;
            ValidationResult again;

            Assert.False(result.HasDefault);
            Assert.True(ArchTestKit.AnyContains(result.Errors, ArchCatalog.DefaultId, "default"), ArchTestKit.Joined(result.Errors));
            Assert.False(ArchCatalog.TryCreate(new[] { ArchTestKit.Family("x86", 1978) }, out catalog, out again));
            Assert.Null(catalog);
            Assert.False(again.HasDefault);
        }

        [Test]
        public void Validate_WithAnInvalidDefaultFamily_HasNoDefault()
        {
            ArchFamily proprietary = ArchTestKit.Proprietary();
            proprietary.Design.DevCost = 0.1f;
            ValidationResult result = Validate(proprietary, ArchTestKit.Family("x86", 1978));

            Assert.False(result.HasDefault);
            Assert.True(ArchTestKit.Ids(result.Families).SequenceEqual(new[] { "x86" }), "the valid families are still listed");
            Assert.True(ArchTestKit.AnyContains(result.Errors, "proprietary", "design.devCost"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_RejectsADefaultFamilyThatRequiresAnother()
        {
            ValidationResult result = Validate(ArchTestKit.Family("x86", 1978), ArchTestKit.Family(ArchCatalog.DefaultId, 1971, "x86"));

            Assert.False(result.HasDefault);
            Assert.True(ArchTestKit.AnyContains(result.Errors, ArchCatalog.DefaultId, "must not require"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_AcceptsAtMost32Families()
        {
            var list = new List<ArchFamily> { ArchTestKit.Proprietary() };
            for (int i = 1; i < 34; i++) list.Add(ArchTestKit.Family("f" + i, 1980));

            ValidationResult result = ArchCatalogValidator.Validate(list);

            Assert.Equal(ArchCatalogValidator.MaxFamilies, result.Families.Count);
            Assert.Equal(2, result.Errors.Count);
            Assert.True(ArchTestKit.AnyContains(result.Errors, "f32", "more than 32"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Validate_ToleratesNullsAndAnEmptyList()
        {
            ValidationResult withNull = ArchCatalogValidator.Validate(new ArchFamily[] { null, ArchTestKit.Proprietary() });
            ValidationResult nothing = ArchCatalogValidator.Validate(null);

            Assert.Equal(1, withNull.Families.Count);
            Assert.True(ArchTestKit.AnyContains(withNull.Errors, "null"));
            Assert.Equal(0, nothing.Families.Count);
            Assert.False(nothing.HasDefault);
        }

        // ---- one record: the ranges ----

        [Test]
        public void Record_ValidFamily_PassesWithoutMessages()
        {
            bool ok;
            List<string> messages = Messages(ArchTestKit.Family("x86", 1978), out ok);

            Assert.True(ok, ArchTestKit.Joined(messages));
            Assert.Equal(0, messages.Count);
        }

        [Test]
        public void Record_DesignMultipliers_AreHeldTo_0_5_And_2_0()
        {
            foreach (string field in new[] { "ipc", "power", "devCost" })
            {
                foreach (float good in new[] { 0.5f, 1f, 2f })
                {
                    ArchFamily family = ArchTestKit.Family("x86");
                    SetDesign(family, field, good);
                    bool ok;
                    Messages(family, out ok);
                    Assert.True(ok, field + " " + good + " must pass");
                }
                foreach (float bad in new[] { 0.49f, 2.01f, 0f, -1f, float.NaN, float.PositiveInfinity })
                {
                    ArchFamily family = ArchTestKit.Family("x86");
                    SetDesign(family, field, bad);
                    bool ok;
                    List<string> messages = Messages(family, out ok);
                    Assert.False(ok, field + " " + bad + " must be rejected");
                    Assert.True(ArchTestKit.AnyContains(messages, "design." + field), ArchTestKit.Joined(messages));
                }
            }
        }

        private static void SetDesign(ArchFamily family, string field, float value)
        {
            if (field == "ipc") family.Design.Ipc = value;
            else if (field == "power") family.Design.Power = value;
            else family.Design.DevCost = value;
        }

        [Test]
        public void Record_Affinity_IsHeldTo_0_7_And_1_3_AndNeedsFourNumbers()
        {
            for (int segment = 0; segment < 4; segment++)
            {
                foreach (float good in new[] { 0.7f, 1.3f })
                {
                    ArchFamily family = ArchTestKit.Family("x86");
                    family.Affinity[segment] = good;
                    bool ok;
                    Messages(family, out ok);
                    Assert.True(ok, "affinity[" + segment + "] " + good + " must pass");
                }
                foreach (float bad in new[] { 0.69f, 1.31f, float.NaN })
                {
                    ArchFamily family = ArchTestKit.Family("x86");
                    family.Affinity[segment] = bad;
                    bool ok;
                    List<string> messages = Messages(family, out ok);
                    Assert.False(ok, "affinity[" + segment + "] " + bad + " must be rejected");
                    Assert.True(ArchTestKit.AnyContains(messages, "affinity[" + segment + "]"), ArchTestKit.Joined(messages));
                }
            }

            foreach (float[] wrong in new[] { new float[0], new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f, 1f } })
            {
                ArchFamily family = ArchTestKit.Family("x86");
                family.Affinity = wrong;
                bool ok;
                List<string> messages = Messages(family, out ok);
                Assert.False(ok, wrong.Length + " numbers must be rejected");
                Assert.True(ArchTestKit.AnyContains(messages, "affinity", "exactly 4"), ArchTestKit.Joined(messages));
            }

            ArchFamily none = ArchTestKit.Family("x86");
            none.Affinity = null;
            bool noneOk;
            Messages(none, out noneOk);
            Assert.False(noneOk, "null affinity");
        }

        [Test]
        public void Record_Year_BaseDays_AndCost_AreHeldToTheirRanges()
        {
            foreach (int year in new[] { 1971, 2050 })
            {
                bool ok;
                Messages(ArchTestKit.Family("x86", year), out ok);
                Assert.True(ok, "year " + year);
            }
            foreach (int year in new[] { 1970, 2051, 0, -5 })
            {
                bool ok;
                List<string> messages = Messages(ArchTestKit.Family("x86", year), out ok);
                Assert.False(ok, "year " + year);
                Assert.True(ArchTestKit.AnyContains(messages, "year"), ArchTestKit.Joined(messages));
            }

            foreach (float days in new[] { 0f, 3650f })
            {
                ArchFamily family = ArchTestKit.Family("x86");
                family.Research.BaseDays = days;
                bool ok;
                Messages(family, out ok);
                Assert.True(ok, "baseDays " + days);
            }
            foreach (float days in new[] { 3651f, -1f, float.NaN })
            {
                ArchFamily family = ArchTestKit.Family("x86");
                family.Research.BaseDays = days;
                bool ok;
                List<string> messages = Messages(family, out ok);
                Assert.False(ok, "baseDays " + days);
                Assert.True(ArchTestKit.AnyContains(messages, "research.baseDays"), ArchTestKit.Joined(messages));
            }

            foreach (float cost in new[] { 0f, 1e12f })
            {
                ArchFamily family = ArchTestKit.Family("x86");
                family.Research.MonthlyCost = cost;
                bool ok;
                Messages(family, out ok);
                Assert.True(ok, "monthlyCost " + cost);
            }
            foreach (float cost in new[] { -1f, 1.1e12f, float.PositiveInfinity })
            {
                ArchFamily family = ArchTestKit.Family("x86");
                family.Research.MonthlyCost = cost;
                bool ok;
                List<string> messages = Messages(family, out ok);
                Assert.False(ok, "monthlyCost " + cost);
                Assert.True(ArchTestKit.AnyContains(messages, "research.monthlyCost"), ArchTestKit.Joined(messages));
            }
        }

        [Test]
        public void Record_Ids_KeysAndRequires_AreChecked()
        {
            foreach (string bad in new[] { "X86", "x 86", "x-86", "", null, new string('a', 33), "x86;drop" })
            {
                bool ok;
                List<string> messages = Messages(ArchTestKit.Family(bad), out ok);
                Assert.False(ok, "id '" + bad + "' must be rejected");
                Assert.True(ArchTestKit.AnyContains(messages, "id:"), ArchTestKit.Joined(messages));
            }
            foreach (string good in new[] { "x86", "x86_64", "a", new string('a', 32), "r1sc_v" })
            {
                bool ok;
                Messages(ArchTestKit.Family(good), out ok);
                Assert.True(ok, "id '" + good + "' must pass");
            }

            ArchFamily noKey = ArchTestKit.Family("x86");
            noKey.NameKey = "";
            noKey.DescKey = null;
            bool keyOk;
            List<string> keyMessages = Messages(noKey, out keyOk);
            Assert.False(keyOk);
            Assert.True(ArchTestKit.AnyContains(keyMessages, "nameKey") && ArchTestKit.AnyContains(keyMessages, "descKey"), ArchTestKit.Joined(keyMessages));

            ArchFamily badIsa = ArchTestKit.Family("x86");
            badIsa.Isa = "X86!";
            bool isaOk;
            Messages(badIsa, out isaOk);
            Assert.False(isaOk, "isa");

            ArchFamily badRequires = ArchTestKit.Family("x86", 1978, "ok_id", "BAD", "ok_id");
            bool requiresOk;
            List<string> requiresMessages = Messages(badRequires, out requiresOk);
            Assert.False(requiresOk);
            Assert.True(ArchTestKit.AnyContains(requiresMessages, "BAD"), ArchTestKit.Joined(requiresMessages));
            Assert.True(ArchTestKit.AnyContains(requiresMessages, "twice"), ArchTestKit.Joined(requiresMessages));

            ArchFamily tooMany = ArchTestKit.Family("x86", 1978, Enumerable.Range(0, 9).Select(i => "r" + i).ToArray());
            bool manyOk;
            Messages(tooMany, out manyOk);
            Assert.False(manyOk, "more than 8 requirements");
        }

        [Test]
        public void Record_Fade_IsChecked()
        {
            ArchFamily ok = ArchTestKit.Family("own2");
            ok.Fade = new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = new[] { 0, 1, 2 } };
            bool passes;
            Messages(ok, out passes);
            Assert.True(passes, "a valid fade");

            var broken = new List<FadeDef>
            {
                new FadeDef { FromYear = 1995, ToYear = 1985, Min = 0.9f, Segments = new[] { 0 } },        // reversed years
                new FadeDef { FromYear = 1960, ToYear = 1985, Min = 0.9f, Segments = new[] { 0 } },        // year out of range
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.4f, Segments = new[] { 0 } },        // min too low
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 1.1f, Segments = new[] { 0 } },        // min above 1
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = new int[0] },         // no segments
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = new[] { 4 } },        // not a segment
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = new[] { 1, 1 } },     // twice
                new FadeDef { FromYear = 1985, ToYear = 1995, Min = 0.9f, Segments = new[] { 0, 1, 2, 3, 3 } },
            };
            foreach (FadeDef fade in broken)
            {
                ArchFamily family = ArchTestKit.Family("own2");
                family.Fade = fade;
                bool valid;
                List<string> messages = Messages(family, out valid);
                Assert.False(valid, "fade " + fade.FromYear + "-" + fade.ToYear + " min " + fade.Min + " must be rejected");
                Assert.True(ArchTestKit.AnyContains(messages, "fade"), ArchTestKit.Joined(messages));
            }
        }

        [Test]
        public void Record_Support_IsChecked()
        {
            ArchFamily family = ArchTestKit.Family("x86");
            family.Support = new SupportDef { UpkeepPerMonth = 1500000f, DecayRate = 0.06f, TargetCap = 60f, EraCost = new[] { new[] { 1987f, 1.3f }, new[] { 2000f, 1f } } };
            bool ok;
            Messages(family, out ok);
            Assert.True(ok, "a valid support");

            var broken = new List<SupportDef>
            {
                new SupportDef { UpkeepPerMonth = -1f },
                new SupportDef { DecayRate = 1.5f },
                new SupportDef { DecayRate = -0.1f },
                new SupportDef { TargetCap = 101f },
                new SupportDef { EraCost = new[] { new[] { 2000f, 1f }, new[] { 1990f, 1f } } },      // years must ascend
                new SupportDef { EraCost = new[] { new[] { 1990f, 0.05f } } },                        // factor out of range
                new SupportDef { EraCost = new[] { new[] { 1990f } } },                               // not a pair
                new SupportDef { EraCost = new[] { new[] { 1900f, 1f } } },                           // year out of range
            };
            foreach (SupportDef support in broken)
            {
                ArchFamily bad = ArchTestKit.Family("x86");
                bad.Support = support;
                bool valid;
                List<string> messages = Messages(bad, out valid);
                Assert.False(valid, "support must be rejected: " + ArchTestKit.Joined(messages));
                Assert.True(ArchTestKit.AnyContains(messages, "support"), ArchTestKit.Joined(messages));
            }
        }

        [Test]
        public void Record_Null_IsRejected()
        {
            bool ok;
            List<string> messages = Messages(null, out ok);
            Assert.False(ok);
            Assert.Equal(1, messages.Count);
        }

        // ---- reading the JSON record ----

        [Test]
        public void Map_ReadsEveryFieldOfARecord()
        {
            var errors = new List<string>();
            ArchFamily family = ArchFamily.Map(ArchTestKit.RecordJson("arm"), errors);

            Assert.NotNull(family, ArchTestKit.Joined(errors));
            Assert.Equal("arm", family.Id);
            Assert.Equal("arm", family.Isa);
            Assert.Equal("arch.family.arm.name", family.NameKey);
            Assert.Equal("arch.family.arm.desc", family.DescKey);
            Assert.Equal(1987, family.Year);
            Assert.Equal(0, family.Requires.Length);
            Assert.Near(240, family.Research.BaseDays);
            Assert.Near(8000000, family.Research.MonthlyCost);
            Assert.Near(0.95, family.Design.Ipc, 1e-6);
            Assert.Near(0.65, family.Design.Power, 1e-6);
            Assert.Near(0.85, family.Design.DevCost, 1e-6);
            Assert.True(family.Affinity.SequenceEqual(new[] { 0.90f, 0.95f, 1.05f, 1.10f }));
            Assert.Null(family.Fade);
            Assert.Near(600000, family.Support.UpkeepPerMonth);
            Assert.Near(0.04, family.Support.DecayRate, 1e-6);
            Assert.Near(100, family.Support.TargetCap);
            Assert.Equal(3, family.Support.EraCost.Length);
            Assert.True(family.Support.EraCost[1].SequenceEqual(new[] { 2000f, 1f }));
        }

        [Test]
        public void Map_ReadsTheFadeAndTheDependencies()
        {
            var errors = new List<string>();
            ArchFamily own = ArchFamily.Map(ArchTestKit.RecordJson("proprietary"), errors);
            ArchFamily x64 = ArchFamily.Map(ArchTestKit.RecordJson("x86_64"), errors);

            Assert.NotNull(own, ArchTestKit.Joined(errors));
            Assert.NotNull(x64, ArchTestKit.Joined(errors));
            Assert.NotNull(own.Fade);
            Assert.Equal(1985, own.Fade.FromYear);
            Assert.Equal(1995, own.Fade.ToYear);
            Assert.Near(0.90, own.Fade.Min, 1e-6);
            Assert.True(own.Fade.Segments.SequenceEqual(new[] { 0, 1, 2 }));
            Assert.Near(60, own.Support.TargetCap);
            Assert.True(x64.Requires.SequenceEqual(new[] { "x86" }));
        }

        [Test]
        public void Map_AMissingOptionalBlock_GetsTheNeutralDefaults()
        {
            JsonObject json = ArchTestKit.RecordJson("x86");
            json.Remove("design");
            json.Remove("support");
            json.Remove("requires");

            var errors = new List<string>();
            ArchFamily family = ArchFamily.Map(json, errors);

            Assert.NotNull(family, ArchTestKit.Joined(errors));
            Assert.Near(1, family.Design.Ipc);
            Assert.Near(1, family.Design.Power);
            Assert.Near(1, family.Design.DevCost);
            Assert.Near(0, family.Support.UpkeepPerMonth);
            Assert.Near(100, family.Support.TargetCap);
            Assert.Equal(0, family.Requires.Length);
        }

        [Test]
        public void Map_AMemberOfTheWrongKind_RejectsTheRecordWithTheName()
        {
            var cases = new List<KeyValuePair<string, Action<JsonObject>>>
            {
                new KeyValuePair<string, Action<JsonObject>>("year", j => j.Set("year", "1978")),
                new KeyValuePair<string, Action<JsonObject>>("year", j => j.Set("year", 1978.5)),
                new KeyValuePair<string, Action<JsonObject>>("affinity", j => j.Set("affinity", "all")),
                new KeyValuePair<string, Action<JsonObject>>("affinity", j => j.Set("affinity", new JsonArray().Add(1).Add("x").Add(1).Add(1))),
                new KeyValuePair<string, Action<JsonObject>>("requires", j => j.Set("requires", new JsonArray().Add(5))),
                new KeyValuePair<string, Action<JsonObject>>("isa", j => j.Set("isa", 5)),
                new KeyValuePair<string, Action<JsonObject>>("research", j => j.Set("research", 5)),
                new KeyValuePair<string, Action<JsonObject>>("design", j => j.Set("design", new JsonArray())),
                new KeyValuePair<string, Action<JsonObject>>("fade", j => j.Set("fade", "no")),
                new KeyValuePair<string, Action<JsonObject>>("eraCost", j => j.GetObject("support").Set("eraCost", new JsonArray().Add(new JsonArray().Add(1990)))),
            };

            foreach (KeyValuePair<string, Action<JsonObject>> pair in cases)
            {
                JsonObject json = ArchTestKit.RecordJson("arm");
                pair.Value(json);
                var errors = new List<string>();
                ArchFamily family = ArchFamily.Map(json, errors);

                Assert.Null(family, pair.Key + " of the wrong kind must reject the record");
                Assert.True(ArchTestKit.AnyContains(errors, pair.Key), pair.Key + ": " + ArchTestKit.Joined(errors));
            }
        }

        [Test]
        public void Map_ARequiredMemberMissing_RejectsTheRecord()
        {
            foreach (string required in new[] { "id", "isa", "nameKey", "descKey", "year", "research", "affinity" })
            {
                JsonObject json = ArchTestKit.RecordJson("x86");
                json.Remove(required);
                var errors = new List<string>();

                Assert.Null(ArchFamily.Map(json, errors), required + " is required");
                Assert.True(ArchTestKit.AnyContains(errors, required, "required"), required + ": " + ArchTestKit.Joined(errors));
            }

            JsonObject research = ArchTestKit.RecordJson("x86");
            research.GetObject("research").Remove("baseDays");
            var researchErrors = new List<string>();
            Assert.Null(ArchFamily.Map(research, researchErrors));
            Assert.True(ArchTestKit.AnyContains(researchErrors, "baseDays"), ArchTestKit.Joined(researchErrors));
        }

        [Test]
        public void Map_AnUnknownMember_IsIgnored()
        {
            JsonObject json = ArchTestKit.RecordJson("x86");
            json.Set("comment", "added by a newer version");
            var errors = new List<string>();

            Assert.NotNull(ArchFamily.Map(json, errors), ArchTestKit.Joined(errors));
        }

        // ---- the catalog ----

        [Test]
        public void Catalog_FindsFamiliesById_AndKeepsTheOrder()
        {
            ArchCatalog catalog = new ArchCatalog(new[] { ArchTestKit.Proprietary(), ArchTestKit.Family("x86", 1978), ArchTestKit.Family("arm", 1987) });

            Assert.Equal(3, catalog.Count);
            Assert.Equal(3, catalog.Families.Count);
            Assert.Equal("x86", catalog.Get("x86").Id);
            Assert.Null(catalog.Get("riscv"));
            Assert.Null(catalog.Get(null));
            Assert.True(catalog.Contains("arm"));
            Assert.False(catalog.Contains("ARM"));
            Assert.False(catalog.Contains(null));
            Assert.Equal(0, catalog.IndexOf("proprietary"));
            Assert.Equal(2, catalog.IndexOf("arm"));
            Assert.Equal(-1, catalog.IndexOf("missing"));
            Assert.Equal(-1, catalog.IndexOf(null));
            Assert.Equal(ArchCatalog.DefaultId, catalog.Default.Id);
        }

        [Test]
        public void Catalog_IgnoresNullsAndRepeatedIds_AndHasNoDefaultWithoutIt()
        {
            ArchFamily first = ArchTestKit.Family("x86", 1978);
            var catalog = new ArchCatalog(new[] { null, first, ArchTestKit.Family("x86", 1990) });

            Assert.Equal(1, catalog.Count);
            Assert.True(ReferenceEquals(first, catalog.Get("x86")));
            Assert.Null(catalog.Default);
            Assert.Equal(0, new ArchCatalog(null).Count);
        }

        [Test]
        public void Catalog_FromTheBuiltInFile_IsUsable()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();

            Assert.Equal(7, catalog.Count);
            Assert.Equal("proprietary", catalog.Default.Id);
            Assert.True(catalog.Get("x86_64").Requires.SequenceEqual(new[] { "x86" }));
        }

        [Test]
        public void FamiliesInRussian_AgreesWithTheNumber()
        {
            // The log line of AC-1.1: "7 семейств загружено".
            Assert.Equal("семейство", ArchCatalog.FamiliesInRussian(1));
            Assert.Equal("семейства", ArchCatalog.FamiliesInRussian(2));
            Assert.Equal("семейства", ArchCatalog.FamiliesInRussian(4));
            Assert.Equal("семейств", ArchCatalog.FamiliesInRussian(5));
            Assert.Equal("семейств", ArchCatalog.FamiliesInRussian(7));
            Assert.Equal("семейств", ArchCatalog.FamiliesInRussian(11));
            Assert.Equal("семейств", ArchCatalog.FamiliesInRussian(14));
            Assert.Equal("семейство", ArchCatalog.FamiliesInRussian(21));
            Assert.Equal("семейств", ArchCatalog.FamiliesInRussian(0));
        }

        // ---- the player's file over the built-in one (FR-1 item 1, AC-1.2) ----

        [Test]
        public void Override_ReplacesARecordWhole_AddsOne_AndRemovesOne()
        {
            JsonObject x86 = ArchTestKit.RecordJson("x86");
            x86.GetObject("design").Set("ipc", 1.2);
            JsonObject mips = ArchTestKit.RecordJson("risc");
            mips.Set("id", "mips");
            mips.Set("year", 1985);
            var removeRiscv = new JsonObject();
            removeRiscv.Set("id", "riscv");
            removeRiscv.Set("disabled", true);

            ContentLoad<ArchFamily> load = ArchTestKit.LoadFamilies(ArchTestKit.OverrideFile(x86, mips, removeRiscv));
            ArchCatalog catalog;
            ValidationResult result;
            bool usable = ArchCatalog.TryCreate(load.Records, out catalog, out result);

            Assert.Equal(0, load.Report.Errors.Count, ArchTestKit.Joined(load.Report.Errors));
            Assert.True(usable);
            Assert.Equal(0, result.Errors.Count, ArchTestKit.Joined(result.Errors));
            Assert.True(load.Report.OverrideApplied);
            Assert.Equal(7, catalog.Count, "7 built-in - riscv + mips");
            Assert.Near(1.2, catalog.Get("x86").Design.Ipc, 1e-6);
            Assert.NotNull(catalog.Get("mips"));
            Assert.Null(catalog.Get("riscv"));
            Assert.Equal("mips", catalog.Families[catalog.Count - 1].Id, "an added family goes to the end");
        }

        [Test]
        public void Override_WithAnUnknownDependency_SkipsOnlyThatFamily()
        {
            // AC-1.2: the player's file gives arm64 the dependency 'armv7', which does not exist.
            JsonObject arm64 = ArchTestKit.RecordJson("arm64");
            arm64.Set("requires", new JsonArray().Add("armv7"));

            ContentLoad<ArchFamily> load = ArchTestKit.LoadFamilies(ArchTestKit.OverrideFile(arm64));
            ArchCatalog catalog;
            ValidationResult result;
            bool usable = ArchCatalog.TryCreate(load.Records, out catalog, out result);

            Assert.True(usable);
            Assert.Equal(6, catalog.Count);
            Assert.Null(catalog.Get("arm64"));
            Assert.NotNull(catalog.Get("arm"));
            Assert.NotNull(catalog.Get("x86_64"));
            Assert.True(ArchTestKit.AnyContains(result.Errors, "arm64", "armv7"), ArchTestKit.Joined(result.Errors));
        }

        [Test]
        public void Override_WithABrokenRecord_KeepsTheBuiltInOne()
        {
            JsonObject x86 = ArchTestKit.RecordJson("x86");
            x86.Set("year", "soon");

            ContentLoad<ArchFamily> load = ArchTestKit.LoadFamilies(ArchTestKit.OverrideFile(x86));

            Assert.Equal(1, load.Report.Skipped);
            Assert.True(ArchTestKit.AnyContains(load.Report.Errors, "x86", "year"), ArchTestKit.Joined(load.Report.Errors));
            Assert.Equal(7, load.Records.Count);
            Assert.Equal(1978, load.Records.First(f => f.Id == "x86").Year, "the built-in x86 stays");
        }

        [Test]
        public void Override_DisablingTheDefaultFamily_MakesTheCatalogUnusable()
        {
            var removeDefault = new JsonObject();
            removeDefault.Set("id", "proprietary");
            removeDefault.Set("disabled", true);

            ContentLoad<ArchFamily> load = ArchTestKit.LoadFamilies(ArchTestKit.OverrideFile(removeDefault));
            ArchCatalog catalog;
            ValidationResult result;

            Assert.False(ArchCatalog.TryCreate(load.Records, out catalog, out result), "FR-1 item 4: without proprietary the feature is off");
            Assert.Null(catalog);
            Assert.False(result.HasDefault);
        }
    }
}
