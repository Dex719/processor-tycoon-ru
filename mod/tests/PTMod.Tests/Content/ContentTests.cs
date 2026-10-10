using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;

namespace PTMod.Tests.Content
{
    /// <summary>A record of the test content file: the wrapper a feature would write around its definition.</summary>
    public sealed class SampleRecord : IContentRecord
    {
        public string Id { get; set; }
        public double Weight { get; set; }
        public LocText Title { get; set; }
    }

    /// <summary>
    /// ModContent logic (FR-10, TSK-015): the override is merged into the built-in file by <c>id</c> (replace, add, remove with
    /// <c>"disabled": true</c>), a broken record never drops the others, an override with an unknown schema or a syntax error is ignored
    /// whole, a duplicate id is reported. AC-10.1: one record replaced, one removed, no errors.
    /// </summary>
    [Covers(typeof(ContentMerger), typeof(ContentLoad<>), typeof(ConfigLoad), typeof(ContentReport), typeof(IContentRecord))]
    public class ContentTests
    {
        private const string Feature = "framework-sample";
        private const string File = "samples";

        private const string Builtin3 = @"{ ""schema"": 1, ""records"": [
            { ""id"": ""s1"", ""weight"": 1 },
            { ""id"": ""s2"", ""weight"": 2 },
            { ""id"": ""s3"", ""weight"": 3 } ] }";

        private static SampleRecord Map(JsonObject json, List<string> errors)
        {
            double weight;
            if (!json.TryGetDouble("weight", out weight))
            {
                errors.Add("weight: expected a number");
                return null;
            }

            LocText title = null;
            JsonNode node = json["title"];
            if (node != null)
            {
                title = LocText.FromJson(node, "title", errors);
                if (title == null) return null;
            }
            return new SampleRecord { Id = json.GetString("id"), Weight = weight, Title = title };
        }

        private static bool Validate(SampleRecord record, List<string> errors)
        {
            if (record.Weight < 0)
            {
                errors.Add("weight: must not be negative");
                return false;
            }
            return true;
        }

        private static ContentLoad<SampleRecord> Load(string builtin, string over)
        {
            return ContentMerger.MergeRecords<SampleRecord>(Feature, File, builtin, over, Map, Validate, 1);
        }

        private static string[] Ids(ContentLoad<SampleRecord> load)
        {
            return load.Records.Select(r => r.Id).ToArray();
        }

        private static string Records(params string[] records)
        {
            return "{ \"schema\": 1, \"records\": [ " + string.Join(", ", records) + " ] }";
        }

        // ---- AC-10.1 and the basic merge ----

        [Test]
        public void Ac101_OverrideReplacesOneAndDisablesOne()
        {
            string over = Records("{ \"id\": \"s1\", \"weight\": 5 }", "{ \"id\": \"s2\", \"disabled\": true }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);
            ContentReport report = load.Report;

            Assert.Equal(1, report.Replaced);
            Assert.Equal(1, report.Removed);
            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(0, report.Added);
            Assert.Equal(3, report.Builtin);
            Assert.Equal(0, report.Skipped);
            Assert.Equal(new[] { "s1", "s3" }, Ids(load));
            Assert.Equal(5.0, load.Records[0].Weight);
            Assert.Equal(3.0, load.Records[1].Weight);
            Assert.Equal(new[] { "s1" }, report.ReplacedIds.ToArray());
            Assert.Equal(new[] { "s2" }, report.RemovedIds.ToArray());
            Assert.True(report.OverrideFound);
            Assert.True(report.OverrideApplied);
            Assert.False(report.OverrideIgnored);
            Assert.False(report.HasErrors);
        }

        [Test]
        public void NoOverride_BuiltinRecordsOnly()
        {
            ContentLoad<SampleRecord> load = Load(Builtin3, null);

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.Equal(3, load.Report.Builtin);
            Assert.Equal(0, load.Report.Replaced + load.Report.Added + load.Report.Removed + load.Report.Skipped);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.False(load.Report.OverrideFound);
            Assert.False(load.Report.OverrideApplied);
            Assert.False(load.Report.OverrideIgnored);
            Assert.Equal(Feature, load.Report.FeatureId);
            Assert.Equal("samples.json", load.Report.File);
        }

        [Test]
        public void Override_ReplacedKeepsPlaceAndNewIdsGoToTheEnd()
        {
            string over = Records("{ \"id\": \"s9\", \"weight\": 9 }", "{ \"id\": \"s2\", \"weight\": 20 }", "{ \"id\": \"s0\", \"weight\": 10 }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s1", "s2", "s3", "s9", "s0" }, Ids(load));
            Assert.Equal(20.0, load.Records[1].Weight);
            Assert.Equal(1, load.Report.Replaced);
            Assert.Equal(2, load.Report.Added);
            Assert.Equal(new[] { "s9", "s0" }, load.Report.AddedIds.ToArray());
            Assert.Equal(0, load.Report.Errors.Count);
        }

        [Test]
        public void Override_ReplacementIsWholeNotFieldByField()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1, \"title\": { \"ru\": \"Имя\", \"en\": \"Name\" } }");
            string over = Records("{ \"id\": \"a\", \"weight\": 2 }");

            ContentLoad<SampleRecord> load = Load(builtin, over);

            Assert.Equal(2.0, load.Records[0].Weight);
            Assert.Null(load.Records[0].Title, "the override record replaces the built-in one as written");
        }

        [Test]
        public void Override_RemovesSeveralRecordsAndKeepsTheOrderOfTheRest()
        {
            string over = Records("{ \"id\": \"s1\", \"disabled\": true }", "{ \"id\": \"s3\", \"disabled\": true }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s2" }, Ids(load));
            Assert.Equal(2, load.Report.Removed);
            Assert.Equal(new[] { "s1", "s3" }, load.Report.RemovedIds.ToArray());
        }

        [Test]
        public void Override_EmptyRecordsArrayChangesNothing()
        {
            ContentLoad<SampleRecord> load = Load(Builtin3, Records());

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.True(load.Report.OverrideApplied);
            Assert.Equal(0, load.Report.Errors.Count);
        }

        // ---- override ignored as a whole (FR-10 item 5) ----

        [Test]
        public void Override_UnknownSchemaIsIgnoredAndBuiltinIsUsed()
        {
            string over = "{ \"schema\": 2, \"records\": [ { \"id\": \"s1\", \"weight\": 99 }, { \"id\": \"s2\", \"disabled\": true } ] }";

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.Equal(1.0, load.Records[0].Weight);
            Assert.Equal(0, load.Report.Replaced + load.Report.Removed + load.Report.Added);
            Assert.True(load.Report.OverrideFound);
            Assert.False(load.Report.OverrideApplied);
            Assert.True(load.Report.OverrideIgnored);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("unknown schema 2"), load.Report.Errors[0]);
            Assert.True(load.Report.Errors[0].Contains("framework-sample/samples.json (override)"), load.Report.Errors[0]);
            Assert.True(load.Report.Errors[0].Contains("ignored"), load.Report.Errors[0]);
        }

        [Test]
        public void Override_MissingOrOddSchemaIsIgnored()
        {
            foreach (string over in new[]
            {
                "{ \"records\": [ { \"id\": \"s1\", \"weight\": 99 } ] }",
                "{ \"schema\": \"1\", \"records\": [ { \"id\": \"s1\", \"weight\": 99 } ] }",
                "{ \"schema\": 1.5, \"records\": [ { \"id\": \"s1\", \"weight\": 99 } ] }",
                "{ \"schema\": null, \"records\": [ { \"id\": \"s1\", \"weight\": 99 } ] }",
                "{ \"schema\": 0, \"records\": [ { \"id\": \"s1\", \"weight\": 99 } ] }",
            })
            {
                ContentLoad<SampleRecord> load = Load(Builtin3, over);
                Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load), over);
                Assert.Equal(1.0, load.Records[0].Weight, over);
                Assert.True(load.Report.OverrideIgnored, over);
                Assert.Equal(1, load.Report.Errors.Count, over);
            }
        }

        [Test]
        public void Override_SyntaxErrorIsIgnoredWithLineAndColumn()
        {
            ContentLoad<SampleRecord> load = Load(Builtin3, "{\n  \"schema\": 1,\n  \"records\": [ { \"id\": \"s1\", \"weight\": } ]\n}");

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.True(load.Report.OverrideIgnored);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("invalid JSON at line 3, column"), load.Report.Errors[0]);
        }

        [Test]
        public void Override_WrongShapeIsIgnored()
        {
            foreach (string over in new[] { "", "   ", "[]", "5", "null", "\"text\"", "{ \"schema\": 1 }", "{ \"schema\": 1, \"records\": {} }", "{ \"schema\": 1, \"records\": 5 }" })
            {
                ContentLoad<SampleRecord> load = Load(Builtin3, over);
                Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load), "override: " + over);
                Assert.True(load.Report.OverrideIgnored, "override: " + over);
                Assert.Equal(1, load.Report.Errors.Count, "override: " + over);
            }
        }

        [Test]
        public void Override_DuplicateKeyInObjectIsASyntaxError()
        {
            // The parser rejects a repeated key, so a half-read file cannot happen.
            ContentLoad<SampleRecord> load = Load(Builtin3, "{ \"schema\": 1, \"schema\": 1, \"records\": [] }");

            Assert.True(load.Report.OverrideIgnored);
            Assert.Equal(1, load.Report.Errors.Count);
        }

        // ---- one broken record never drops the others (FR-10 item 4) ----

        [Test]
        public void BrokenBuiltinRecordIsSkippedTheRestLoads()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1 }", "{ \"id\": \"b\", \"weight\": \"heavy\" }", "{ \"id\": \"c\", \"weight\": 3 }");

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(new[] { "a", "c" }, Ids(load));
            Assert.Equal(2, load.Report.Builtin);
            Assert.Equal(1, load.Report.Skipped);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.Equal("framework-sample/samples.json (built-in) record 'b': weight: expected a number", load.Report.Errors[0]);
        }

        [Test]
        public void ValidationFailureNamesFileIdAndField()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": -4 }", "{ \"id\": \"b\", \"weight\": 1 }");

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(new[] { "b" }, Ids(load));
            Assert.Equal(1, load.Report.Skipped);
            Assert.Equal(new[] { "framework-sample/samples.json (built-in) record 'a': weight: must not be negative" }, load.Report.Errors.ToArray());
        }

        [Test]
        public void RejectionWithoutAMessageStillExplainsItself()
        {
            ContentLoad<SampleRecord> mapperNull = ContentMerger.MergeRecords<SampleRecord>(Feature, File,
                Records("{ \"id\": \"a\", \"weight\": 1 }"), null, (json, errors) => null, null, 1);
            ContentLoad<SampleRecord> validateFalse = ContentMerger.MergeRecords<SampleRecord>(Feature, File,
                Records("{ \"id\": \"a\", \"weight\": 1 }"), null, Map, (record, errors) => false, 1);

            Assert.Equal(0, mapperNull.Records.Count);
            Assert.Equal(1, mapperNull.Report.Errors.Count);
            Assert.True(mapperNull.Report.Errors[0].Contains("record 'a': the record was rejected by the mapper"), mapperNull.Report.Errors[0]);
            Assert.Equal(0, validateFalse.Records.Count);
            Assert.True(validateFalse.Report.Errors[0].Contains("record 'a': the record failed validation"), validateFalse.Report.Errors[0]);
        }

        [Test]
        public void ThrowingMapperOrValidationIsContained()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1 }", "{ \"id\": \"boom\", \"weight\": 2 }", "{ \"id\": \"c\", \"weight\": 3 }");

            ContentLoad<SampleRecord> mapperThrows = ContentMerger.MergeRecords<SampleRecord>(Feature, File, builtin, null,
                (json, errors) =>
                {
                    if (json.GetString("id") == "boom") throw new InvalidOperationException("mapper exploded");
                    return Map(json, errors);
                }, Validate, 1);
            ContentLoad<SampleRecord> validateThrows = ContentMerger.MergeRecords<SampleRecord>(Feature, File, builtin, null,
                Map, (record, errors) =>
                {
                    if (record.Id == "boom") throw new NullReferenceException("validator exploded");
                    return true;
                }, 1);

            Assert.Equal(new[] { "a", "c" }, Ids(mapperThrows));
            Assert.Equal(1, mapperThrows.Report.Errors.Count);
            Assert.True(mapperThrows.Report.Errors[0].Contains("record 'boom'") && mapperThrows.Report.Errors[0].Contains("mapper exploded"), mapperThrows.Report.Errors[0]);
            Assert.Equal(new[] { "a", "c" }, Ids(validateThrows));
            Assert.True(validateThrows.Report.Errors[0].Contains("validator exploded"), validateThrows.Report.Errors[0]);
        }

        [Test]
        public void MappedIdMustMatchTheJsonId()
        {
            ContentLoad<SampleRecord> load = ContentMerger.MergeRecords<SampleRecord>(Feature, File,
                Records("{ \"id\": \"a\", \"weight\": 1 }", "{ \"id\": \"b\", \"weight\": 2 }"), null,
                (json, errors) => new SampleRecord { Id = "other", Weight = 1 }, null, 1);

            Assert.Equal(0, load.Records.Count);
            Assert.Equal(2, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("has id 'other' but the JSON record has id 'a'"), load.Report.Errors[0]);
        }

        [Test]
        public void AcceptedRecordWithMessagesGivesWarningsNotErrors()
        {
            ContentLoad<SampleRecord> load = ContentMerger.MergeRecords<SampleRecord>(Feature, File,
                Records("{ \"id\": \"a\", \"weight\": 1 }"), null,
                (json, errors) =>
                {
                    errors.Add("unknown field 'color' ignored");
                    return new SampleRecord { Id = "a", Weight = 1 };
                }, null, 1);

            Assert.Equal(1, load.Records.Count);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.Equal(new[] { "framework-sample/samples.json (built-in) record 'a': unknown field 'color' ignored" }, load.Report.Warnings.ToArray());
        }

        [Test]
        public void BrokenOverrideRecordLeavesTheBuiltinOneInPlace()
        {
            string over = Records("{ \"id\": \"s1\", \"weight\": -1 }", "{ \"id\": \"s2\", \"weight\": 20 }", "{ \"id\": \"new\" }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.Equal(1.0, load.Records[0].Weight, "the invalid override of s1 is dropped");
            Assert.Equal(20.0, load.Records[1].Weight);
            Assert.Equal(1, load.Report.Replaced);
            Assert.Equal(0, load.Report.Added);
            Assert.Equal(2, load.Report.Skipped);
            Assert.Equal(2, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("(override) record 's1'"), load.Report.Errors[0]);
            Assert.True(load.Report.Errors[1].Contains("(override) record 'new'"), load.Report.Errors[1]);
            Assert.True(load.Report.OverrideApplied);
        }

        [Test]
        public void OverrideOfARejectedBuiltinRecordCountsAsAdded()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": -1 }", "{ \"id\": \"b\", \"weight\": 1 }");
            string over = Records("{ \"id\": \"a\", \"weight\": 7 }");

            ContentLoad<SampleRecord> load = Load(builtin, over);

            Assert.Equal(new[] { "b", "a" }, Ids(load));
            Assert.Equal(1, load.Report.Added);
            Assert.Equal(0, load.Report.Replaced);
            Assert.Equal(1, load.Report.Errors.Count, "the broken built-in record is still reported");
        }

        [Test]
        public void ManyRecordsWithOneBrokenOneLoadTheRest()
        {
            var sb = new StringBuilder("{ \"schema\": 1, \"records\": [");
            for (int i = 0; i < 1000; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(i == 437 ? "{ \"id\": \"r437\", \"weight\": \"x\" }" : "{ \"id\": \"r" + i + "\", \"weight\": " + i + " }");
            }
            sb.Append("] }");

            ContentLoad<SampleRecord> load = Load(sb.ToString(), null);

            Assert.Equal(999, load.Records.Count);
            Assert.Equal(999, load.Report.Builtin);
            Assert.Equal(1, load.Report.Skipped);
            Assert.Equal("r0", load.Records[0].Id);
            Assert.Equal("r999", load.Records[998].Id);
        }

        // ---- ids, duplicates, the disabled flag ----

        [Test]
        public void DuplicateIdInBuiltinIsAnErrorAndTheFirstStays()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1 }", "{ \"id\": \"b\", \"weight\": 2 }", "{ \"id\": \"a\", \"weight\": 9 }");

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(new[] { "a", "b" }, Ids(load));
            Assert.Equal(1.0, load.Records[0].Weight);
            Assert.Equal(2, load.Report.Builtin);
            Assert.Equal(1, load.Report.Skipped);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("(built-in) record 'a'") && load.Report.Errors[0].Contains("duplicate id"), load.Report.Errors[0]);
        }

        [Test]
        public void DuplicateIdInOverrideIsAnErrorAndTheFirstStays()
        {
            string over = Records("{ \"id\": \"s1\", \"weight\": 5 }", "{ \"id\": \"s1\", \"weight\": 6 }", "{ \"id\": \"s2\", \"disabled\": true }", "{ \"id\": \"s2\", \"weight\": 8 }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s1", "s3" }, Ids(load));
            Assert.Equal(5.0, load.Records[0].Weight);
            Assert.Equal(1, load.Report.Replaced);
            Assert.Equal(1, load.Report.Removed);
            Assert.Equal(2, load.Report.Errors.Count);
            Assert.True(load.Report.Errors.All(e => e.Contains("(override)") && e.Contains("duplicate id")));
        }

        [Test]
        public void RecordWithoutAUsableIdOrNotAnObjectIsSkipped()
        {
            string builtin = "{ \"schema\": 1, \"records\": [ { \"weight\": 1 }, { \"id\": \"\", \"weight\": 1 }, { \"id\": \"  \", \"weight\": 1 }, "
                             + "{ \"id\": 5, \"weight\": 1 }, 7, \"text\", null, [], { \"id\": \"ok\", \"weight\": 1 } ] }";

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(new[] { "ok" }, Ids(load));
            Assert.Equal(8, load.Report.Skipped);
            Assert.Equal(8, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("record #1") && load.Report.Errors[0].Contains("'id'"), load.Report.Errors[0]);
            Assert.True(load.Report.Errors[4].Contains("record #5") && load.Report.Errors[4].Contains("must be a JSON object"), load.Report.Errors[4]);
        }

        [Test]
        public void DisabledFlagMustBeABoolean()
        {
            string over = Records("{ \"id\": \"s1\", \"disabled\": \"yes\" }", "{ \"id\": \"s2\", \"disabled\": false, \"weight\": 22 }", "{ \"id\": \"s3\", \"disabled\": null, \"weight\": 33 }");

            ContentLoad<SampleRecord> load = Load(Builtin3, over);

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.Equal(1.0, load.Records[0].Weight, "a record with a bad flag is not applied");
            Assert.Equal(22.0, load.Records[1].Weight);
            Assert.Equal(33.0, load.Records[2].Weight);
            Assert.Equal(2, load.Report.Replaced);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("record 's1'") && load.Report.Errors[0].Contains("'disabled' must be true or false"), load.Report.Errors[0]);
        }

        [Test]
        public void DisablingAnUnknownIdIsAWarningNotAnError()
        {
            ContentLoad<SampleRecord> load = Load(Builtin3, Records("{ \"id\": \"nope\", \"disabled\": true }"));

            Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(load));
            Assert.Equal(0, load.Report.Removed);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.Equal(1, load.Report.Warnings.Count);
            Assert.True(load.Report.Warnings[0].Contains("record 'nope'") && load.Report.Warnings[0].Contains("no such built-in record"), load.Report.Warnings[0]);
        }

        [Test]
        public void DisabledBuiltinRecordIsSkippedWithAWarning()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1 }", "{ \"id\": \"b\", \"disabled\": true }", "{ \"id\": \"c\", \"weight\": 3 }");

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(new[] { "a", "c" }, Ids(load));
            Assert.Equal(2, load.Report.Builtin);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.Equal(1, load.Report.Warnings.Count);
        }

        [Test]
        public void DisabledRecordNeedsNoOtherFields()
        {
            // A removal is just the id and the flag: the mapper (which needs "weight") is not asked.
            ContentLoad<SampleRecord> load = Load(Builtin3, Records("{ \"id\": \"s2\", \"disabled\": true }"));

            Assert.Equal(0, load.Report.Errors.Count);
            Assert.Equal(1, load.Report.Removed);
        }

        // ---- problems with the built-in file itself ----

        [Test]
        public void MissingBuiltinFileGivesAnErrorAndNoRecords()
        {
            ContentLoad<SampleRecord> load = Load(null, Records("{ \"id\": \"x\", \"weight\": 1 }"));

            Assert.Equal(0, load.Records.Count);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("(built-in)") && load.Report.Errors[0].Contains("not found"), load.Report.Errors[0]);
            Assert.True(load.Report.OverrideFound);
            Assert.True(load.Report.OverrideIgnored, "no baseline to merge into");
            Assert.Equal(0, load.Report.Added);
            Assert.Equal(1, load.Report.Warnings.Count);
        }

        [Test]
        public void BuiltinWithAnotherSchemaOrBadJsonLoadsNothing()
        {
            ContentLoad<SampleRecord> schema2 = Load("{ \"schema\": 2, \"records\": [ { \"id\": \"a\", \"weight\": 1 } ] }", null);
            ContentLoad<SampleRecord> broken = Load("{ \"schema\": 1, \"records\": [ ", null);
            ContentLoad<SampleRecord> noRecords = Load("{ \"schema\": 1 }", null);

            foreach (ContentLoad<SampleRecord> load in new[] { schema2, broken, noRecords })
            {
                Assert.Equal(0, load.Records.Count);
                Assert.Equal(1, load.Report.Errors.Count);
                Assert.True(load.Report.Errors[0].Contains("(built-in)"), load.Report.Errors[0]);
            }
            Assert.True(schema2.Report.Errors[0].Contains("unknown schema 2"));
            Assert.True(broken.Report.Errors[0].Contains("invalid JSON at line 1"));
        }

        [Test]
        public void MissingMapperIsReportedNotThrown()
        {
            ContentLoad<SampleRecord> load = ContentMerger.MergeRecords<SampleRecord>(Feature, File, Builtin3, null, null, null, 1);

            Assert.Equal(0, load.Records.Count);
            Assert.Equal(1, load.Report.Errors.Count);
            Assert.True(load.Report.Errors[0].Contains("no record mapper"));
        }

        [Test]
        public void ValidationIsOptional()
        {
            ContentLoad<SampleRecord> load = ContentMerger.MergeRecords<SampleRecord>(Feature, "samples.json", Records("{ \"id\": \"a\", \"weight\": -1 }"), null, Map, null, 1);

            Assert.Equal(1, load.Records.Count);
            Assert.Equal("samples.json", load.Report.File, "the extension is not doubled");
        }

        // ---- text of the records ----

        [Test]
        public void LocTextAndCyrillicSurviveTheMerge()
        {
            string builtin = Records("{ \"id\": \"mail\", \"weight\": 1, \"title\": { \"ru\": \"Проверка модов\", \"en\": \"Mod check\" } }");
            string over = Records("{ \"id\": \"mail\", \"weight\": 2, \"title\": { \"ru\": \"Новый заголовок\", \"key\": \"smp.mail.title\" } }");

            ContentLoad<SampleRecord> merged = Load(builtin, over);
            ContentLoad<SampleRecord> plain = Load(builtin, null);

            Assert.Equal("Проверка модов", plain.Records[0].Title.Ru);
            Assert.Equal("Mod check", plain.Records[0].Title.En);
            Assert.Equal("Новый заголовок", merged.Records[0].Title.Ru);
            Assert.Equal("smp.mail.title", merged.Records[0].Title.Key);
            Assert.Null(merged.Records[0].Title.En);
        }

        [Test]
        public void BadLocTextNamesThePathInsideTheRecord()
        {
            string builtin = Records("{ \"id\": \"a\", \"weight\": 1, \"title\": { \"ru\": 5 } }");

            ContentLoad<SampleRecord> load = Load(builtin, null);

            Assert.Equal(0, load.Records.Count);
            Assert.Equal(new[] { "framework-sample/samples.json (built-in) record 'a': title.ru: expected a string, got a number" }, load.Report.Errors.ToArray());
        }

        // ---- names and paths ----

        [Test]
        public void Names_NormalizeFileAddsTheExtensionOnce()
        {
            Assert.Equal("events.json", ContentMerger.NormalizeFile("events"));
            Assert.Equal("rules.json", ContentMerger.NormalizeFile("rules.json"));
            Assert.Equal("rules.JSON", ContentMerger.NormalizeFile("rules.JSON"));
            Assert.Equal("shocks.json", ContentMerger.NormalizeFile("  shocks "));
            Assert.Equal(string.Empty, ContentMerger.NormalizeFile(null));
            Assert.Equal(string.Empty, ContentMerger.NormalizeFile("  "));
        }

        [Test]
        public void Names_SafeNamesOnly()
        {
            foreach (string ok in new[] { "framework-sample", "scenarios-achievements", "events.json", "a_b.c", "X1" })
                Assert.True(ContentMerger.IsSafeName(ok), ok);

            foreach (string bad in new[] { null, "", "..", "a/b", "a\\b", "../x", "x/..", ".hidden", "a..b", "C:\\x", "/abs", "a b", "тест", new string('a', 101) })
                Assert.False(ContentMerger.IsSafeName(bad), bad);
        }

        [Test]
        public void Names_ResourceAndOverridePaths()
        {
            Assert.Equal("content/framework-sample/samples.json", ContentMerger.BuiltinResourceName("framework-sample", "samples"));
            Assert.Equal("content/event-system/events.json", ContentMerger.BuiltinResourceName("event-system", "events.json"));
            Assert.Equal("framework-sample/samples.json", ContentMerger.OverrideRelativePath("framework-sample", "samples"));
            Assert.Equal("framework-sample/samples.json", ContentMerger.ReportKey("framework-sample", "samples.json"));

            Assert.Null(ContentMerger.BuiltinResourceName("../evil", "samples"));
            Assert.Null(ContentMerger.BuiltinResourceName("framework-sample", "../x"));
            Assert.Null(ContentMerger.BuiltinResourceName("framework-sample", "a/b"));
            Assert.Null(ContentMerger.BuiltinResourceName("framework-sample", ""));
            Assert.Null(ContentMerger.OverrideRelativePath("a/b", "samples"));
            Assert.Null(ContentMerger.OverrideRelativePath(null, "samples"));
        }

        // ---- config files ----

        private const string BuiltinConfig = @"{ ""schema"": 1,
            ""limits"": { ""floorBase"": 2000000, ""perGameByDifficulty"": [2, 1, 1, 1, 1, 0] },
            ""expenseOrigin"": ""Royalties"",
            ""enabled"": true }";

        [Test]
        public void Config_NoOverrideGivesTheBuiltinObject()
        {
            ConfigLoad load = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, null, 1);

            Assert.Equal(1, load.Config.GetInt32("schema"));
            Assert.Equal(2000000, load.Config.GetObject("limits").GetInt32("floorBase"));
            Assert.Equal("Royalties", load.Config.GetString("expenseOrigin"));
            Assert.Equal(3, load.Report.Builtin, "top-level fields without schema");
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.False(load.Report.OverrideFound);
            Assert.Equal("config.json", load.Report.File);
        }

        [Test]
        public void Config_OverrideMergesFieldByField()
        {
            string over = @"{ ""schema"": 1,
                ""limits"": { ""floorBase"": 3000000 },
                ""enabled"": false,
                ""extra"": { ""note"": ""ok"" },
                ""expenseOrigin"": ""Royalties"" }";

            ConfigLoad load = ContentMerger.MergeConfig(Feature, "config.json", BuiltinConfig, over, 1);
            JsonObject limits = load.Config.GetObject("limits");

            Assert.Equal(3000000, limits.GetInt32("floorBase"));
            Assert.Equal(6, limits.GetArray("perGameByDifficulty").Count, "fields the override does not mention stay");
            Assert.False(load.Config.GetBool("enabled", true));
            Assert.Equal("ok", load.Config.GetObject("extra").GetString("note"));
            Assert.Equal(new[] { "limits", "enabled" }, load.Report.ReplacedIds.ToArray(), "expenseOrigin is unchanged, so it is not counted");
            Assert.Equal(new[] { "extra" }, load.Report.AddedIds.ToArray());
            Assert.Equal(2, load.Report.Replaced);
            Assert.Equal(1, load.Report.Added);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.True(load.Report.OverrideApplied);
        }

        [Test]
        public void Config_ArraysAndScalarsAreReplacedWhole()
        {
            string over = @"{ ""schema"": 1, ""limits"": { ""perGameByDifficulty"": [9, 9] }, ""expenseOrigin"": { ""now"": ""an object"" } }";

            ConfigLoad load = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, over, 1);

            Assert.Equal(2, load.Config.GetObject("limits").GetArray("perGameByDifficulty").Count);
            Assert.Equal("an object", load.Config.GetObject("expenseOrigin").GetString("now"));
            Assert.Equal(2000000, load.Config.GetObject("limits").GetInt32("floorBase"));
        }

        [Test]
        public void Config_OverrideWithUnknownSchemaOrBadJsonIsIgnored()
        {
            foreach (string over in new[] { "{ \"schema\": 3, \"enabled\": false }", "{ \"enabled\": false }", "{ \"schema\": 1, \"enabled\": ", "[]", "" })
            {
                ConfigLoad load = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, over, 1);

                Assert.True(load.Config.GetBool("enabled"), over);
                Assert.True(load.Report.OverrideIgnored, over);
                Assert.Equal(1, load.Report.Errors.Count, over);
                Assert.Equal(0, load.Report.Replaced + load.Report.Added, over);
            }
        }

        [Test]
        public void Config_BrokenBuiltinGivesAnEmptyObjectNotNull()
        {
            foreach (string builtin in new[] { null, "", "{ \"schema\": 2 }", "{ \"limits\": {} }", "[1]", "{ \"schema\": 1, " })
            {
                ConfigLoad load = ContentMerger.MergeConfig(Feature, "config", builtin, "{ \"schema\": 1, \"a\": 1 }", 1);

                Assert.NotNull(load.Config, builtin);
                Assert.Equal(0, load.Config.Count, builtin);
                Assert.Equal(1, load.Report.Errors.Count, builtin);
                Assert.True(load.Report.Errors[0].Contains("(built-in)"), load.Report.Errors[0]);
                Assert.True(load.Report.OverrideIgnored, builtin);
            }
        }

        [Test]
        public void Config_ResultsDoNotShareObjects()
        {
            ConfigLoad first = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, "{ \"schema\": 1, \"limits\": { \"floorBase\": 1 } }", 1);
            ConfigLoad second = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, null, 1);

            first.Config.GetObject("limits").Set("floorBase", 12345);

            Assert.Equal(2000000, second.Config.GetObject("limits").GetInt32("floorBase"));
        }

        [Test]
        public void Config_NullInTheOverrideIsKept()
        {
            ConfigLoad load = ContentMerger.MergeConfig(Feature, "config", BuiltinConfig, "{ \"schema\": 1, \"expenseOrigin\": null }", 1);

            Assert.True(load.Config["expenseOrigin"].IsNull);
            Assert.Equal(new[] { "expenseOrigin" }, load.Report.ReplacedIds.ToArray());
        }

        // ---- ContentReport ----

        [Test]
        public void Report_DefaultsAndSummaryLine()
        {
            var empty = new ContentReport();
            Assert.NotNull(empty.Errors);
            Assert.NotNull(empty.Warnings);
            Assert.NotNull(empty.ReplacedIds);
            Assert.NotNull(empty.AddedIds);
            Assert.NotNull(empty.RemovedIds);
            Assert.False(empty.HasErrors);
            Assert.False(empty.OverrideIgnored);

            var named = new ContentReport(null, null);
            Assert.Equal(string.Empty, named.FeatureId);
            Assert.Equal(string.Empty, named.File);

            ContentLoad<SampleRecord> load = Load(Builtin3, Records("{ \"id\": \"s1\", \"weight\": 5 }", "{ \"id\": \"s2\", \"disabled\": true }"));
            Assert.Equal("framework-sample/samples.json: built-in 3, replaced 1, added 0, removed 1, skipped 0, errors 0", load.Report.ToString());

            ContentLoad<SampleRecord> bad = Load(Builtin3, Records("{ \"id\": \"s1\", \"weight\": -5 }"));
            Assert.True(bad.Report.HasErrors);
            Assert.Equal("framework-sample/samples.json: built-in 3, replaced 0, added 0, removed 0, skipped 1, errors 1", bad.Report.ToString());
        }
    }
}
