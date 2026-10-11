using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The model of the calendar (TSK-002, FR-1): the date type, the vocabularies and the mapper from the JSON of <c>calendar.json</c>. The mapper checks the
    /// shape (types, unreadable values); what an entry may contain is the validator's business (<see cref="CalendarValidatorTests"/>).
    /// </summary>
    [Covers(typeof(SimpleDate), typeof(CalendarEntry), typeof(StageSpec), typeof(FireSpec), typeof(RoleSpec), typeof(HistoryInfo), typeof(ConditionUse))]
    [Covers(typeof(HistCategories), typeof(RoleKinds), typeof(FallbackModes), typeof(EntryStatus))]
    public class CalendarModelTests
    {
        // ---- SimpleDate ----

        [Test]
        public void SimpleDate_ParsesOnlyTheExactFormat()
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse("1995-08-24", out date));
            Assert.Equal(1995, date.Year);
            Assert.Equal(8, date.Month);
            Assert.Equal(24, date.Day);
            Assert.True(date.IsSet);
            Assert.Equal("1995-08-24", date.ToString());

            foreach (string bad in new[] { null, "", "1995-8-24", "95-08-24", "1995/08/24", "1995-08-24 ", " 1995-08-24", "1995-13-01", "1995-02-30", "1995-00-10", "1995-08-00", "abcd-ef-gh", "1995-08-2x", "+995-08-24", "1995-08-24T00:00" })
            {
                Assert.False(SimpleDate.TryParse(bad, out date), "'" + bad + "' must not parse");
                Assert.False(date.IsSet);
            }
        }

        [Test]
        public void SimpleDate_KnowsLeapDays()
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse("2000-02-29", out date));
            Assert.True(SimpleDate.TryParse("2024-02-29", out date));
            Assert.False(SimpleDate.TryParse("1900-02-29", out date));
            Assert.False(SimpleDate.TryParse("2023-02-29", out date));
            Assert.False(SimpleDate.TryCreate(2023, 4, 31, out date));
        }

        [Test]
        public void SimpleDate_CountsDaysFrom1971_01_01()
        {
            Assert.Equal(0, new SimpleDate(1971, 1, 1).Days);
            Assert.Equal(1, new SimpleDate(1971, 1, 2).Days);
            Assert.Equal(365, new SimpleDate(1972, 1, 1).Days);
            Assert.Equal(-1, new SimpleDate(1970, 12, 31).Days);
            Assert.Equal(1462, new SimpleDate(1975, 1, 2).Days, "1972 is a leap year: 1971-01-01 to 1975-01-02 is 365 + 366 + 365 + 365 + 1 days");
            Assert.Equal(0, default(SimpleDate).Days, "a date that is not set counts as 0, check IsSet");
        }

        [Test]
        public void SimpleDate_RoundTripsThroughDayNumbers()
        {
            foreach (string text in new[] { "1971-01-01", "1980-03-14", "1995-08-24", "2000-02-29", "2024-12-31", "2030-12-31", "1970-12-31" })
            {
                SimpleDate date;
                Assert.True(SimpleDate.TryParse(text, out date));
                Assert.Equal(date, SimpleDate.FromDays(date.Days), text);
                Assert.Equal(date.ToDateTime(), new DateTime(date.Year, date.Month, date.Day));
                Assert.Equal(date, SimpleDate.FromDateTime(date.ToDateTime().AddHours(13)), "the time of day is dropped");
            }
        }

        [Test]
        public void SimpleDate_AddsDaysAcrossMonthsAndYears()
        {
            Assert.Equal(new SimpleDate(1995, 9, 3), new SimpleDate(1995, 8, 24).AddDays(10));
            Assert.Equal(new SimpleDate(1996, 1, 5), new SimpleDate(1995, 12, 31).AddDays(5));
            Assert.Equal(new SimpleDate(1995, 8, 14), new SimpleDate(1995, 8, 24).AddDays(-10));
            Assert.Equal(new SimpleDate(2000, 2, 29), new SimpleDate(2000, 2, 28).AddDays(1));
            Assert.False(default(SimpleDate).AddDays(5).IsSet, "an unset date stays unset");
        }

        [Test]
        public void SimpleDate_IsComparable()
        {
            var a = new SimpleDate(1995, 8, 24);
            var b = new SimpleDate(1995, 8, 25);
            var c = new SimpleDate(1995, 8, 24);
            Assert.True(a < b && b > a && a <= c && a >= c && a == c && a != b);
            Assert.True(a.Equals(c) && a.Equals((object)c) && !a.Equals((object)"x"));
            Assert.Equal(a.GetHashCode(), c.GetHashCode());
            Assert.True(a.CompareTo(b) < 0 && b.CompareTo(a) > 0 && a.CompareTo(c) == 0);
            Assert.True(new SimpleDate(1996, 1, 1) > new SimpleDate(1995, 12, 31));
            Assert.True(new SimpleDate(1995, 9, 1) > new SimpleDate(1995, 8, 31));
            Assert.Equal("(not set)", default(SimpleDate).ToString());
        }

        [Test]
        public void SimpleDate_RefusesADayThatDoesNotExist()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleDate(1995, 2, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleDate(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimpleDate.FromDays(int.MaxValue));
            SimpleDate date;
            Assert.False(SimpleDate.TryFromDays(int.MinValue, out date));
            Assert.True(SimpleDate.TryFromDays(0, out date));
        }

        // ---- the vocabularies ----

        [Test]
        public void Categories_AreTheTwelveOfTheSettings_OnlyDisasterIsOffByDefault()
        {
            Assert.Equal(
                new[] { "macro", "boom", "shock", "disaster", "antitrust", "export", "priceWar", "patent", "flaw", "race", "brand", "oem" },
                HistCategories.All.ToArray());
            foreach (string category in HistCategories.All)
            {
                Assert.True(HistCategories.IsKnown(category), category);
                Assert.Equal(category != "disaster", HistCategories.DefaultEnabled(category), category);
            }
            Assert.False(HistCategories.IsKnown(null));
            Assert.False(HistCategories.IsKnown("Boom"));
        }

        [Test]
        public void RoleKinds_AreThoseOfTheDesign()
        {
            Assert.Equal(
                new[] { "leader", "challenger", "named", "foundryLeader", "bestPerfPerWatt", "newestEntrant", "randomManufacturer", "player", "global", "allCpuCompanies", "allManufacturers", "allFabless" },
                RoleKinds.All.ToArray());
            Assert.True(RoleKinds.IsKnown("leader"));
            Assert.False(RoleKinds.IsKnown("Leader"));
            Assert.False(RoleKinds.IsKnown(null));
            Assert.True(RoleKinds.IsImplicitRole("global") && RoleKinds.IsImplicitRole("player") && RoleKinds.IsImplicitRole("allFabless"));
            Assert.False(RoleKinds.IsImplicitRole("leader") || RoleKinds.IsImplicitRole("target") || RoleKinds.IsImplicitRole(null));
            Assert.True(RoleKinds.IsGroup("allCpuCompanies") && RoleKinds.IsGroup("allManufacturers") && RoleKinds.IsGroup("allFabless"));
            Assert.False(RoleKinds.IsGroup("global") || RoleKinds.IsGroup("leader"));
        }

        [Test]
        public void EntryStatus_NumbersAreFixed_BecauseTheyAreSaved()
        {
            // The design's example section writes "status": 2 for a fired entry.
            Assert.Equal(0, (int)EntryStatus.Pending);
            Assert.Equal(1, (int)EntryStatus.Waiting);
            Assert.Equal(2, (int)EntryStatus.Fired);
            Assert.Equal(3, (int)EntryStatus.Expired);
            Assert.Equal(4, (int)EntryStatus.SkippedBeforeStart);
            Assert.Equal(5, (int)EntryStatus.Disabled);
            Assert.Equal(6, (int)EntryStatus.Error);
            Assert.Equal(7, Enum.GetValues(typeof(EntryStatus)).Length);
        }

        [Test]
        public void FallbackModes_AreNewsAndSkip()
        {
            Assert.True(FallbackModes.IsKnown("news") && FallbackModes.IsKnown("skip"));
            Assert.False(FallbackModes.IsKnown("News") || FallbackModes.IsKnown(null) || FallbackModes.IsKnown(""));
        }

        // ---- the mapper ----

        [Test]
        public void TheMapper_ReadsTheExampleOfTheDesign()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Fine);

            Assert.Equal("hist.2009.eufine", e.Id);
            Assert.Equal("antitrust", e.Category);
            Assert.True(e.Major);
            Assert.False(e.CatchUp);
            Assert.Equal(new SimpleDate(2009, 5, 13), e.Anchor);
            Assert.Equal(new SimpleDate(2009, 5, 1), e.WindowFrom);
            Assert.Equal(new SimpleDate(2010, 12, 31), e.WindowTo);
            Assert.Equal(15, e.JitterDays);
            Assert.Equal("news", e.Fallback);

            Assert.Equal(1, e.Roles.Count);
            RoleSpec role = e.Roles[0];
            Assert.Equal("target", role.Name);
            Assert.Equal("leader", role.Kind);
            Assert.Equal(new[] { Segment.Any }, role.Segments);
            Assert.Equal(36, role.Months);
            Assert.Near(0.55, role.MinShare, 1e-6);
            Assert.Equal(-1, role.UniqueId);
            Assert.False(role.RequireFoundry);
            Assert.Equal(0, role.Fallback.Count);

            Assert.Equal(1, e.Conditions.Count);
            Assert.NotNull(e.Conditions[0]);
            Assert.Equal(new[] { "hist.ShareAtLeast" }, e.ConditionUse.Predicates.ToArray());
            Assert.Equal(new[] { "target" }, e.ConditionUse.Roles.ToArray());

            Assert.Equal(1, e.Stages.Count);
            Assert.Equal(0, e.Stages[0].OffsetDays);
            Assert.Null(e.Stages[0].SkipIfFlag);
            FireSpec fire = e.Stages[0].Fires[0];
            Assert.Equal("hist.decide.antitrust.fine", fire.EventId);
            Assert.Equal("target", fire.Role);
            Assert.Equal(new[] { "eu2009" }, fire.ExtraArgs);
            Assert.Null(fire.OnlyIf);

            Assert.Equal("hist.2009.eufine.title", e.History.TitleKey);
            Assert.Equal("hist.2009.eufine.fact", e.History.FactKey);
            Assert.Equal(2, e.History.Sources.Count);
            Assert.Equal("hist.2009.eufine", e.ToString());
        }

        [Test]
        public void TheMapper_ReadsNamedRolesWithAFallbackChain_AndTheNameIsInherited()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Jiji);

            RoleSpec role = e.Roles[0];
            Assert.Equal("named", role.Kind);
            Assert.Equal(9, role.UniqueId);
            Assert.True(role.RequireFoundry);
            Assert.False(role.RequireCapacity);
            Assert.Equal(1, role.Fallback.Count);
            Assert.Equal("target", role.Fallback[0].Name, "a fallback role stands in for the role it is written under");
            Assert.Equal("foundryLeader", role.Fallback[0].Kind);

            Assert.Equal(2, e.Stages.Count);
            Assert.Equal(30, e.Stages[1].OffsetDays);
            Assert.Equal("recovered", e.Stages[1].SkipIfFlag);
            Assert.Equal(2, e.Stages[0].Fires.Count);
        }

        [Test]
        public void TheMapper_ReadsOnlyIf_AndCollectsWhatItNames()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Export);

            FireSpec fire = e.Stages[0].Fires[1];
            Assert.Equal("allCpuCompanies", fire.Role);
            Assert.NotNull(fire.OnlyIf);
            Assert.Equal(new[] { "hist.PlayerHasCpuAbove" }, fire.OnlyIfUse.Predicates.ToArray());
            Assert.Equal(0, fire.OnlyIfUse.Roles.Count);
            Assert.Null(e.Stages[0].Fires[0].OnlyIf);
            Assert.Equal(375, e.Stages[1].OffsetDays);
        }

        [Test]
        public void TheMapper_FillsDefaultsForWhatIsAbsent()
        {
            CalendarEntry e = HistFixture.Entry("{ 'id': 'hist.bare' }");

            Assert.Equal("hist.bare", e.Id);
            Assert.Null(e.Category);
            Assert.False(e.Major);
            Assert.False(e.CatchUp);
            Assert.False(e.Anchor.IsSet || e.WindowFrom.IsSet || e.WindowTo.IsSet);
            Assert.Equal(0, e.JitterDays);
            Assert.Equal(0, e.Roles.Count + e.Conditions.Count + e.Stages.Count + e.FallbackFires.Count);
            Assert.Null(e.Fallback);
            Assert.Null(e.History);
        }

        [Test]
        public void TheMapper_AFireWithoutARole_IsAGlobalOne_AndARoleGetsItsDefaults()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Fine, o =>
            {
                ((JsonObject)o.GetArray("stages")[0]).GetArray("fires").Add(HistFixture.Node("{'eventId':'hist.x'}"));
                o.GetArray("roles").Add(HistFixture.Node("{'name':'second','kind':'challenger'}"));
            });

            Assert.Equal("global", e.Stages[0].Fires[1].Role);
            Assert.Equal(0, e.Stages[0].Fires[1].ExtraArgs.Length);

            RoleSpec second = e.Roles[1];
            Assert.Equal(new[] { Segment.Any }, second.Segments);
            Assert.Equal(RoleSpec.DefaultMonths, second.Months);
            Assert.Equal(36, RoleSpec.DefaultMonths);
            Assert.Equal(0.0f, second.MinShare);
            Assert.Equal(-1, second.UniqueId);
        }

        [Test]
        public void TheMapper_ReadsTheSegmentWords_InAnyCase()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Fine, o =>
                ((JsonObject)o.GetArray("roles")[0]).Set("segments", HistFixture.Node("['HighEnd','midrange','LOWEND','industries']")));

            Assert.Equal(new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries }, e.Roles[0].Segments);
        }

        [Test]
        public void TheMapper_AOneStringIsAListOfOne()
        {
            CalendarEntry e = HistFixture.Entry(HistFixture.Fine, o =>
            {
                ((JsonObject)o.GetArray("roles")[0]).Set("segments", "lowend");
                o.GetObject("history").Set("sources", "https://example.com/one");
            });

            Assert.Equal(new[] { Segment.LowEnd }, e.Roles[0].Segments);
            Assert.Equal(new[] { "https://example.com/one" }, e.History.Sources.ToArray());
        }

        [Test]
        public void TheMapper_ComplainsAboutWrongTypes_WithAPath()
        {
            var cases = new Dictionary<string, string>
            {
                { "{'id':'hist.x','category':5}", "category: expected a string, got a number" },
                { "{'id':'hist.x','major':'yes'}", "major: expected true or false, got a string" },
                { "{'id':'hist.x','jitterDays':1.5}", "jitterDays: expected a whole number, got a number" },
                { "{'id':'hist.x','anchor':'24.08.1995'}", "anchor: expected a date as yyyy-MM-dd, got \"24.08.1995\"" },
                { "{'id':'hist.x','anchor':19950824}", "anchor: expected a string, got a number" },
                { "{'id':'hist.x','window':{'from':'1995-02-30'}}", "window.from: expected a date as yyyy-MM-dd" },
                { "{'id':'hist.x','window':[]}", "window: expected an object, got an array" },
                { "{'id':'hist.x','roles':{}}", "roles: expected an array, got an object" },
                { "{'id':'hist.x','roles':[5]}", "roles[0]: expected an object, got a number" },
                { "{'id':'hist.x','roles':[{'name':'a','kind':'leader','segments':['everything']}]}", "roles[0].segments: unknown segment \"everything\"" },
                { "{'id':'hist.x','roles':[{'name':'a','kind':'leader','minShare':'high'}]}", "roles[0].minShare: expected a number, got a string" },
                { "{'id':'hist.x','stages':[1]}", "stages[0]: expected an object, got a number" },
                { "{'id':'hist.x','stages':[{'fires':[{'role':7}]}]}", "stages[0].fires[0].role: expected a string, got a number" },
                { "{'id':'hist.x','stages':[{'fires':[{'extraArgs':[1]}]}]}", "stages[0].fires[0].extraArgs[0]: expected a string, got a number" },
                { "{'id':'hist.x','fallbackFires':[null]}", "fallbackFires[0]: expected an object, got null" },
                { "{'id':'hist.x','history':{'sources':[7]}}", "history.sources[0]: expected a string, got a number" },
            };

            foreach (KeyValuePair<string, string> pair in cases)
            {
                var messages = new List<string>();
                CalendarEntry e = HistFixture.Map(HistFixture.Obj(pair.Key), messages);
                Assert.Null(e, "must not map: " + pair.Key);
                Assert.True(messages.Any(m => m.StartsWith("entry hist.x ") && m.Contains(pair.Value)), pair.Key + " -> " + string.Join(" | ", messages));
            }
        }

        [Test]
        public void TheMapper_NeedsAnId()
        {
            var messages = new List<string>();
            Assert.Null(HistFixture.Map(HistFixture.Obj("{'category':'boom'}"), messages));
            Assert.True(messages.Any(m => m.Contains("id: is required")), string.Join(" | ", messages));
            Assert.Null(CalendarEntry.FromJson(null, messages, HistFixture.Parser()));
        }

        [Test]
        public void TheMapper_WarnsAboutUnknownMembers_ButNotAboutComments()
        {
            var messages = new List<string>();
            CalendarEntry e = HistFixture.Map(HistFixture.Obj("{'id':'hist.x','_note':'a comment','catchup':true,'window':{'frm':'1990-01-01'},'roles':[{'name':'a','kind':'leader','shares':1}]}"), messages);

            Assert.NotNull(e, "unknown members do not stop the entry: " + string.Join(" | ", messages));
            Assert.True(messages.Any(m => m.Contains("catchup: unknown member is ignored")), string.Join(" | ", messages));
            Assert.True(messages.Any(m => m.Contains("window.frm: unknown member is ignored")), string.Join(" | ", messages));
            Assert.True(messages.Any(m => m.Contains("roles[0].shares: unknown member is ignored")), string.Join(" | ", messages));
            Assert.False(messages.Any(m => m.Contains("_note")), "a name that starts with an underscore is a comment");
        }

        [Test]
        public void TheMapper_AcceptsTheFrameworksDisabledSwitch()
        {
            var messages = new List<string>();
            Assert.NotNull(HistFixture.Map(HistFixture.Obj("{'id':'hist.x','disabled':true}"), messages));
            Assert.False(messages.Any(m => m.Contains("disabled")), string.Join(" | ", messages));
        }

        [Test]
        public void TheMapper_StopsADeepFallbackChain()
        {
            string chain = "{'kind':'leader'}";
            for (int i = 0; i < CalendarEntry.MaxRoleDepth + 2; i++) chain = "{'kind':'leader','fallback':[" + chain + "]}";
            var messages = new List<string>();
            CalendarEntry e = HistFixture.Map(HistFixture.Obj("{'id':'hist.x','roles':[{'name':'a'," + chain.Substring(1) + "]}"), messages);

            Assert.Null(e);
            Assert.True(messages.Any(m => m.Contains("fallback roles are nested deeper than")), string.Join(" | ", messages));
        }

        [Test]
        public void TheMapper_ConditionsNeedAParser()
        {
            var messages = new List<string>();
            CalendarEntry e = CalendarEntry.FromJson(HistFixture.Obj("{'id':'hist.x','conditions':[{'type':'dateAtLeast','year':1980}]}"), messages, null);
            Assert.Null(e);
            Assert.True(messages.Any(m => m.Contains("no condition parser")), string.Join(" | ", messages));

            messages.Clear();
            e = CalendarEntry.FromJson(HistFixture.Obj("{'id':'hist.x'}"), messages, null);
            Assert.NotNull(e, "an entry without conditions needs no parser");
        }

        [Test]
        public void TheMapper_ReportsTheParametersOfABadCondition()
        {
            var messages = new List<string>();
            CalendarEntry e = HistFixture.Map(HistFixture.Obj("{'id':'hist.x','conditions':[{'type':'dateAtLeast','year':0}]}"), messages);
            Assert.Null(e);
            Assert.True(messages.Any(m => m.StartsWith("entry hist.x $.conditions[0]") && m.Contains("year")), string.Join(" | ", messages));
        }

        [Test]
        public void TheMapperFunction_IsWhatLoadRecordsWants()
        {
            Func<JsonObject, List<string>, CalendarEntry> map = CalendarEntry.Mapper(HistFixture.Parser());
            var messages = new List<string>();
            CalendarEntry e = map(HistFixture.Obj(HistFixture.Plain), messages);
            Assert.NotNull(e);
            Assert.Equal("hist.1995.win95", e.Id);
        }

        [Test]
        public void ConditionUse_FindsEveryPredicateAndRoleInATree()
        {
            var use = new ConditionUse();
            use.Collect(HistFixture.Node(
                "{'all':[ {'type':'a','role':'r1'}, {'any':[ {'type':'b'}, {'not':{'type':'c','role':'r2'}} ]}, {'type':'d','role':5} ]}"));

            Assert.Equal(new[] { "a", "b", "c", "d" }, use.Predicates.ToArray());
            Assert.Equal(new[] { "r1", "r2" }, use.Roles.ToArray(), "a role that is not a string is not a role name");

            var empty = new ConditionUse();
            empty.Collect(null);
            empty.Collect(HistFixture.Node("5"));
            Assert.Equal(0, empty.Predicates.Count + empty.Roles.Count);
        }

        [Test]
        public void ConditionUse_IgnoresATreeThatIsTooDeep()
        {
            // Built in code: the JSON parser itself refuses a document nested deeper than 64 levels.
            JsonNode tree = new JsonObject().Set("type", "deep");
            for (int i = 0; i < 100; i++) tree = new JsonObject().Set("not", tree);
            var use = new ConditionUse();
            use.Collect(tree);
            Assert.Equal(0, use.Predicates.Count, "the walk stops at 64 levels instead of recursing without end");
        }
    }
}
