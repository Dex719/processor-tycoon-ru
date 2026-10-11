using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The rules of the section "Security" of design.md for <c>calendar.json</c> and <c>params.json</c> (TSK-002, FR-1, NFR-7, NFR-8): one test per rule,
    /// on entries written the way the design writes them (the four examples of "Calendar: calendar.json"). The identifier table is the real one
    /// (<c>core.json</c>); the pack, the string tables and the predicates are fakes.
    /// </summary>
    [Covers(typeof(CalendarValidator), typeof(IEventCatalog), typeof(IL10nKeys))]
    public class CalendarValidatorTests
    {
        // ---- the examples of the design are valid ----

        [Test]
        public void ExamplesOfTheDesign_AreValid()
        {
            foreach (string json in new[] { HistFixture.Plain, HistFixture.Fine, HistFixture.Jiji, HistFixture.Export })
            {
                Verdict verdict = HistFixture.Judge(json);
                Assert.True(verdict.Ok, verdict.Text);
                Assert.Equal(0, verdict.Errors.Count, verdict.Text);
                Assert.Equal(0, verdict.Warnings.Count, verdict.Text);
            }
        }

        [Test]
        public void TheCryptoBoomCondition_FromTheDesign_IsValid()
        {
            // Example 5 of the design: a core predicate in the conditions of the entry.
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("conditions", HistFixture.Node("[{'type':'featureActive','id':'gpu-product-line'}]")));
            Assert.True(verdict.Ok, verdict.Text);
        }

        [Test]
        public void EveryEntryOfTheBuiltInCalendar_IsValid()
        {
            // calendar.json grows with the content tasks (42 entries in the end); whatever is in it must pass every rule, fire events of events.json
            // and have no id twice. The count itself (25 to 42) is checked by the content tasks when the file is complete.
            string src = SourceScan.SrcDirOrFail();
            JsonObject calendar = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(src, "Content", "historical-events", "calendar.json")));
            JsonObject pack = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(src, "Content", "historical-events", "events.json")));
            Assert.Equal(1, calendar.GetInt32("schema"), "calendar.json schema");
            Assert.Equal(1, pack.GetInt32("schema"), "events.json schema");

            var eventIds = new List<string>();
            JsonArray events = pack.GetArray("records");
            Assert.NotNull(events, "events.json must have 'records'");
            for (int i = 0; i < events.Count; i++) eventIds.Add(((JsonObject)events[i]).GetString("id"));

            JsonArray records = calendar.GetArray("records");
            Assert.NotNull(records, "calendar.json must have 'records'");
            var entries = new List<CalendarEntry>();
            var messages = new List<string>();
            for (int i = 0; i < records.Count; i++)
            {
                CalendarEntry entry = HistFixture.Map((JsonObject)records[i], messages);
                Assert.NotNull(entry, "record " + i + " must map: " + string.Join(" | ", messages));
                entries.Add(entry);
            }

            var errors = new List<string>();
            List<CalendarEntry> valid = null;
            HistFixture.WithIds(() => { valid = CalendarValidator.ValidateAll(entries, new FakeEvents(eventIds.ToArray()), null, errors); });
            Assert.Equal(0, errors.Count, "the built-in calendar must be valid: " + string.Join(" | ", errors));
            Assert.Equal(entries.Count, valid.Count);
            Assert.True(entries.Count <= 200, "the calendar is far larger than the 42 entries of the design: " + entries.Count);
        }

        // ---- identity: id, category ----

        [Test]
        public void Id_WithoutTheHistPrefix_IsRejected()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("id", "ms.1995.win95"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("entry ms.1995.win95 id:"), verdict.Text);
        }

        [Test]
        public void Id_IsRequired()
        {
            CalendarEntry entry = HistFixture.Entry(HistFixture.Plain);
            entry.Id = null;
            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), null);
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("id: is required"), verdict.Text);
        }

        [Test]
        public void Ids_AreUnique_TheSecondEntryWithTheSameIdIsDropped()
        {
            var first = HistFixture.Entry(HistFixture.Plain);
            var second = HistFixture.Entry(HistFixture.Plain, o => o.Set("anchor", "1995-09-01"));
            var other = HistFixture.Entry(HistFixture.Fine);

            var errors = new List<string>();
            List<CalendarEntry> valid = null;
            HistFixture.WithIds(() => { valid = CalendarValidator.ValidateAll(new[] { first, second, other }, HistFixture.AllEvents(), null, errors); });

            Assert.Equal(2, valid.Count);
            Assert.True(ReferenceEquals(first, valid[0]) && ReferenceEquals(other, valid[1]), "the first entry with an id stays, the order is kept");
            Assert.True(errors.Any(e => e.Contains("entry hist.1995.win95: duplicate id")), string.Join(" | ", errors));
        }

        [Test]
        public void Category_MustBeOneOfTheTwelve()
        {
            Assert.Equal(12, HistCategories.All.Count);
            foreach (string category in HistCategories.All)
            {
                Verdict ok = HistFixture.Judge(HistFixture.Plain, o => o.Set("category", category));
                Assert.True(ok.Ok, category + ": " + ok.Text);
            }

            foreach (string wrong in new[] { "weird", "Boom", "" })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("category", wrong));
                Assert.False(verdict.Ok, "category '" + wrong + "' must be rejected");
                Assert.True(verdict.HasError("category"), verdict.Text);
            }
        }

        // ---- dates and ranges ----

        [Test]
        public void Dates_BeforeTheFirstDay_AreRejected()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("anchor", "1970-12-31");
                o.GetObject("window").Set("from", "1970-12-01");
            });
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("anchor: 1970-12-31 is outside 1971-01-01 .. 2030-12-31"), verdict.Text);
            Assert.True(verdict.HasError("window.from: 1970-12-01 is outside"), verdict.Text);
        }

        [Test]
        public void Dates_AfterTheLastDay_AreRejected()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("anchor", "2031-01-01");
                o.GetObject("window").Set("to", "2031-06-30");
            });
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("anchor: 2031-01-01 is outside"), verdict.Text);
            Assert.True(verdict.HasError("window.to: 2031-06-30 is outside"), verdict.Text);
        }

        [Test]
        public void Dates_TheFirstAndTheLastDayThemselves_AreAccepted()
        {
            Verdict first = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("anchor", "1971-01-01");
                o.Set("window", HistFixture.Node("{'from':'1971-01-01','to':'1971-06-30'}"));
            });
            Assert.True(first.Ok, first.Text);

            Verdict last = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("anchor", "2030-12-31");
                o.Set("window", HistFixture.Node("{'from':'2030-06-01','to':'2030-12-31'}"));
            });
            Assert.True(last.Ok, last.Text);
        }

        [Test]
        public void Window_StartingAfterItsEnd_IsRejected()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("window", HistFixture.Node("{'from':'1996-03-31','to':'1995-08-01'}")));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("window: from 1996-03-31 is after to 1995-08-01"), verdict.Text);
        }

        [Test]
        public void Anchor_OutsideTheWindow_IsRejected()
        {
            Verdict before = HistFixture.Judge(HistFixture.Plain, o => o.Set("anchor", "1995-07-31"));
            Assert.False(before.Ok, before.Text);
            Assert.True(before.HasError("anchor: 1995-07-31 is before the window starts"), before.Text);

            Verdict after = HistFixture.Judge(HistFixture.Plain, o => o.Set("anchor", "1996-04-01"));
            Assert.False(after.Ok, after.Text);
            Assert.True(after.HasError("anchor: 1996-04-01 is after the window ends"), after.Text);

            Verdict edges = HistFixture.Judge(HistFixture.Plain, o => o.Set("anchor", "1995-08-01"));
            Assert.True(edges.Ok, "the anchor may sit on the first day of the window: " + edges.Text);
            Verdict lastDay = HistFixture.Judge(HistFixture.Plain, o => o.Set("anchor", "1996-03-31"));
            Assert.True(lastDay.Ok, "and on the last: " + lastDay.Text);
        }

        [Test]
        public void Dates_ThatAreNotSet_AreRejected()
        {
            CalendarEntry entry = HistFixture.Entry(HistFixture.Plain);
            entry.Anchor = default(SimpleDate);
            entry.WindowFrom = default(SimpleDate);
            entry.WindowTo = default(SimpleDate);

            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), null);
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("anchor: is required"), verdict.Text);
            Assert.True(verdict.HasError("window.from: is required"), verdict.Text);
            Assert.True(verdict.HasError("window.to: is required"), verdict.Text);
        }

        [Test]
        public void JitterDays_MustBeBetweenZeroAndSixty()
        {
            foreach (int bad in new[] { -1, 61, 1000 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("jitterDays", bad));
                Assert.False(verdict.Ok, "jitterDays " + bad);
                Assert.True(verdict.HasError("jitterDays: " + bad + " is outside 0..60"), verdict.Text);
            }

            foreach (int good in new[] { 0, 1, 60 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("jitterDays", good));
                Assert.True(verdict.Ok, "jitterDays " + good + ": " + verdict.Text);
            }
        }

        [Test]
        public void OffsetDays_MustBeBetweenZeroAnd1825()
        {
            Verdict negative = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("offsetDays", -1));
            Assert.False(negative.Ok, negative.Text);
            Assert.True(negative.HasError("stages[1].offsetDays: -1 is outside 0..1825"), negative.Text);

            Verdict tooFar = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("offsetDays", 1826));
            Assert.False(tooFar.Ok, tooFar.Text);
            Assert.True(tooFar.HasError("stages[1].offsetDays: 1826 is outside 0..1825"), tooFar.Text);

            Verdict edge = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("offsetDays", 1825));
            Assert.True(edge.Ok, "five years is the longest stage offset: " + edge.Text);
        }

        [Test]
        public void Stages_GoInOrder_AndTheFirstOnePlaysAtTheFiring()
        {
            Verdict first = HistFixture.Judge(HistFixture.Plain, o => StageAt(o, 0).Set("offsetDays", 5));
            Assert.False(first.Ok, first.Text);
            Assert.True(first.HasError("stages[0].offsetDays: stage 0 plays at the firing"), first.Text);

            Verdict unordered = HistFixture.Judge(HistFixture.Jiji, o =>
            {
                JsonArray stages = o.GetArray("stages");
                stages.Add(HistFixture.Node("{'offsetDays': 10, 'fires': [ { 'eventId': 'hist.1999.jiji.market', 'role': 'global' } ] }"));
            });
            Assert.False(unordered.Ok, unordered.Text);
            Assert.True(unordered.HasError("stages[2].offsetDays: 10 is before the previous stage (30)"), unordered.Text);

            Verdict sameDay = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("offsetDays", 0));
            Assert.True(sameDay.Ok, "two stages may play on the same day: " + sameDay.Text);
        }

        [Test]
        public void Stages_NeedAtLeastOneStage_AndEveryStageOneFire()
        {
            Verdict none = HistFixture.Judge(HistFixture.Plain, o => o.Set("stages", HistFixture.Node("[]")));
            Assert.False(none.Ok, none.Text);
            Assert.True(none.HasError("stages: needs at least one stage"), none.Text);

            Verdict empty = HistFixture.Judge(HistFixture.Plain, o => StageAt(o, 0).Set("fires", HistFixture.Node("[]")));
            Assert.False(empty.Ok, empty.Text);
            Assert.True(empty.HasError("stages[0].fires: a stage needs at least one fire"), empty.Text);
        }

        [Test]
        public void SkipIfFlag_MustBeAPlainName()
        {
            Verdict bad = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("skipIfFlag", "not a flag!"));
            Assert.False(bad.Ok, bad.Text);
            Assert.True(bad.HasError("stages[1].skipIfFlag"), bad.Text);

            Verdict good = HistFixture.Judge(HistFixture.Jiji, o => StageAt(o, 1).Set("skipIfFlag", "recovered"));
            Assert.True(good.Ok, good.Text);
        }

        [Test]
        public void Fallback_MustBeNewsOrSkip()
        {
            foreach (string mode in new[] { "news", "skip" })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("fallback", mode));
                Assert.True(verdict.Ok, mode + ": " + verdict.Text);
            }

            Verdict wrong = HistFixture.Judge(HistFixture.Plain, o => o.Set("fallback", "retry"));
            Assert.False(wrong.Ok, wrong.Text);
            Assert.True(wrong.HasError("fallback: \"retry\" is not news or skip"), wrong.Text);

            Verdict missing = HistFixture.Judge(HistFixture.Plain, o => o.Remove("fallback"));
            Assert.False(missing.Ok, missing.Text);
            Assert.True(missing.HasError("fallback: is required"), missing.Text);
        }

        // ---- roles: shares, probabilities, uniqueId ----

        [Test]
        public void MinShare_MustBeBetweenZeroAndOne()
        {
            foreach (double bad in new[] { -0.01, 1.01, 55 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("minShare", bad));
                Assert.False(verdict.Ok, "minShare " + bad);
                Assert.True(verdict.HasError("roles[0].minShare"), verdict.Text);
            }

            foreach (double good in new[] { 0.0, 0.55, 1.0 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("minShare", good));
                Assert.True(verdict.Ok, "minShare " + good + ": " + verdict.Text);
            }
        }

        [Test]
        public void MinShare_ThatIsNotANumber_IsRejected()
        {
            CalendarEntry entry = HistFixture.Entry(HistFixture.Fine);
            entry.Roles[0].MinShare = float.NaN;
            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), null);
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("roles[0].minShare"), verdict.Text);
        }

        [Test]
        public void UniqueId_MustBeBetweenZeroAndTwelve()
        {
            foreach (int bad in new[] { 13, -2, 100 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Jiji, o => RoleAt(o, 0).Set("uniqueId", bad));
                Assert.False(verdict.Ok, "uniqueId " + bad);
                Assert.True(verdict.HasError("roles[0].uniqueId: " + bad + " is outside 0..12"), verdict.Text);
            }

            foreach (int good in new[] { 0, 9, 12 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Jiji, o => RoleAt(o, 0).Set("uniqueId", good));
                Assert.True(verdict.Ok, "uniqueId " + good + ": " + verdict.Text);
            }
        }

        [Test]
        public void ANamedRole_NeedsItsCompany()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Jiji, o => RoleAt(o, 0).Remove("uniqueId"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("roles[0].uniqueId: a named role needs the uniqueId of its company"), verdict.Text);
        }

        [Test]
        public void RoleKind_MustBeKnown()
        {
            Assert.Equal(12, RoleKinds.All.Count);
            foreach (string kind in RoleKinds.All)
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("kind", kind).Set("uniqueId", 3));
                Assert.True(verdict.Ok, kind + ": " + verdict.Text);
            }

            Verdict wrong = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("kind", "boss"));
            Assert.False(wrong.Ok, wrong.Text);
            Assert.True(wrong.HasError("roles[0].kind: \"boss\" is not a kind of role"), wrong.Text);
        }

        [Test]
        public void Roles_NamesAreUniqueAndNotBuiltIn()
        {
            Verdict twice = HistFixture.Judge(HistFixture.Fine, o => o.GetArray("roles").Add(HistFixture.Node("{'name':'target','kind':'challenger'}")));
            Assert.False(twice.Ok, twice.Text);
            Assert.True(twice.HasError("roles[1].name: \"target\" is declared twice"), twice.Text);

            Verdict reserved = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("name", "global"));
            Assert.False(reserved.Ok, reserved.Text);
            Assert.True(reserved.HasError("\"global\" is a built-in role name"), reserved.Text);
        }

        [Test]
        public void RoleMonths_MustBeBetween1And120()
        {
            foreach (int bad in new[] { 0, -5, 121 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("months", bad));
                Assert.False(verdict.Ok, "months " + bad);
                Assert.True(verdict.HasError("roles[0].months: " + bad + " is outside 1..120"), verdict.Text);
            }

            foreach (int good in new[] { 1, 36, 120 })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("months", good));
                Assert.True(verdict.Ok, "months " + good + ": " + verdict.Text);
            }
        }

        [Test]
        public void RoleSegments_NeedOneSegment_AndAllStandsAlone()
        {
            Verdict none = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("segments", HistFixture.Node("[]")));
            Assert.False(none.Ok, none.Text);
            Assert.True(none.HasError("roles[0].segments: needs at least one segment"), none.Text);

            Verdict mixed = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("segments", HistFixture.Node("['all','highend']")));
            Assert.False(mixed.Ok, mixed.Text);
            Assert.True(mixed.HasError("\"all\" cannot be combined"), mixed.Text);

            Verdict several = HistFixture.Judge(HistFixture.Fine, o => RoleAt(o, 0).Set("segments", HistFixture.Node("['lowend','midrange']")));
            Assert.True(several.Ok, "two segments are fine: " + several.Text);
        }

        [Test]
        public void FallbackRoles_AreJudgedLikeRoles()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Jiji, o =>
                RoleAt(o, 0).Set("fallback", HistFixture.Node("[ { 'kind': 'leader', 'minShare': 2 }, { 'kind': 'nobody' } ]")));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("roles[0].fallback[0].minShare"), verdict.Text);
            Assert.True(verdict.HasError("roles[0].fallback[1].kind: \"nobody\" is not a kind of role"), verdict.Text);
        }

        [Test]
        public void FallbackRoles_NestedTooDeep_AreRejectedByTheValidatorToo()
        {
            // The mapper stops a deep JSON chain; a calendar built in code (or a cycle) is stopped here.
            CalendarEntry entry = HistFixture.Entry(HistFixture.Fine);
            RoleSpec role = entry.Roles[0];
            for (int i = 0; i <= CalendarEntry.MaxRoleDepth; i++)
            {
                var inner = new RoleSpec { Name = role.Name, Kind = RoleKinds.Leader };
                role.Fallback.Add(inner);
                role = inner;
            }
            role.Fallback.Add(new RoleSpec { Name = "x", Kind = RoleKinds.Leader });

            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), null);
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("nested deeper than"), verdict.Text);
        }

        // ---- the events of the pack ----

        [Test]
        public void EveryFiredEvent_MustExistInThePack()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => FireAt(o, 0, 0).Set("eventId", "hist.1995.nothing"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("stages[0].fires[0].eventId: the event \"hist.1995.nothing\" is not in the pack"), verdict.Text);

            Verdict later = HistFixture.Judge(HistFixture.Jiji, o => FireAt(o, 1, 0).Set("eventId", "hist.1999.jiji.gone"));
            Assert.False(later.Ok, later.Text);
            Assert.True(later.HasError("stages[1].fires[0].eventId"), later.Text);
        }

        [Test]
        public void TheFallbackFires_AreCheckedAgainstThePackToo()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o =>
                o.Set("fallbackFires", HistFixture.Node("[ { 'eventId': 'hist.fizzle.missing', 'role': 'global' } ]")));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("fallbackFires[0].eventId: the event \"hist.fizzle.missing\" is not in the pack"), verdict.Text);
        }

        [Test]
        public void WithoutACatalog_TheExistenceOfEventsIsNotChecked_ButTheSecondPhaseDoesIt()
        {
            CalendarEntry entry = HistFixture.Entry(HistFixture.Plain, o => FireAt(o, 0, 0).Set("eventId", "hist.1995.nothing"));

            Verdict early = HistFixture.Judge(entry, null, null);
            Assert.True(early.Ok, "the packs are not loaded yet: " + early.Text);

            var errors = new List<string>();
            Assert.False(CalendarValidator.ValidateEventsExist(entry, HistFixture.AllEvents(), errors), "the second phase finds the missing event");
            Assert.True(errors.Any(e => e.Contains("entry hist.1995.win95 stages[0].fires[0].eventId")), string.Join(" | ", errors));

            errors.Clear();
            Assert.True(CalendarValidator.ValidateEventsExist(HistFixture.Entry(HistFixture.Plain), HistFixture.AllEvents(), errors), string.Join(" | ", errors));
            Assert.True(CalendarValidator.ValidateEventsExist(entry, null, errors), "no catalog, nothing to check");
        }

        [Test]
        public void AFiredEvent_MustCarryTheHistPrefix()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => FireAt(o, 0, 0).Set("eventId", "es.starter.boom"), events: new FakeEvents("es.starter.boom"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("stages[0].fires[0].eventId"), verdict.Text);
        }

        [Test]
        public void AFire_NeedsAKnownRole()
        {
            Verdict ghost = HistFixture.Judge(HistFixture.Fine, o => FireAt(o, 0, 0).Set("role", "ghost"));
            Assert.False(ghost.Ok, ghost.Text);
            Assert.True(ghost.HasError("stages[0].fires[0].role: \"ghost\" is not a role of the entry"), ghost.Text);

            foreach (string implicitRole in new[] { "global", "player", "allCpuCompanies", "allManufacturers", "allFabless" })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => FireAt(o, 0, 0).Set("role", implicitRole));
                Assert.True(verdict.Ok, implicitRole + ": " + verdict.Text);
            }

            Verdict missing = HistFixture.Judge(HistFixture.Fine, o => FireAt(o, 0, 0).Remove("role"));
            Assert.True(missing.Ok, "a fire without a role is a global one: " + missing.Text);
        }

        // ---- the history block: sources, texts ----

        [Test]
        public void ASource_IsRequired_AC12()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Remove("sources"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("entry hist.1995.win95 missing sources"), "the line of AC-1.2: " + verdict.Text);

            Verdict empty = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Set("sources", HistFixture.Node("[]")));
            Assert.False(empty.Ok, empty.Text);
            Assert.True(empty.HasError("entry hist.1995.win95 missing sources"), empty.Text);

            Verdict noHistory = HistFixture.Judge(HistFixture.Plain, o => o.Remove("history"));
            Assert.False(noHistory.Ok, noHistory.Text);
            Assert.True(noHistory.HasError("entry hist.1995.win95 history: is required"), noHistory.Text);
            Assert.True(noHistory.HasError("missing sources"), noHistory.Text);
        }

        [Test]
        public void Sources_MustBeHttpsUrls()
        {
            foreach (string bad in new[] { "http://example.com/a", "example.com", "https://", "https://exa mple.com", "ftp://example.com", "" })
            {
                Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Set("sources", HistFixture.Node("['" + bad + "']")));
                Assert.False(verdict.Ok, "source '" + bad + "' must be rejected");
                Assert.True(verdict.HasError("history.sources[0]"), verdict.Text);
            }

            Verdict mixed = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Set("sources", HistFixture.Node("['https://a.example/x','http://b.example/y']")));
            Assert.False(mixed.Ok, "one https source does not excuse a bad one: " + mixed.Text);
        }

        [Test]
        public void TextKeys_MustCarryTheHistPrefix()
        {
            Verdict title = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Set("titleKey", "core.title"));
            Assert.False(title.Ok, title.Text);
            Assert.True(title.HasError("history.titleKey"), title.Text);

            Verdict fact = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Set("factKey", "ms.fact"));
            Assert.False(fact.Ok, fact.Text);
            Assert.True(fact.HasError("history.factKey"), fact.Text);

            Verdict missing = HistFixture.Judge(HistFixture.Plain, o => o.GetObject("history").Remove("factKey"));
            Assert.False(missing.Ok, missing.Text);
            Assert.True(missing.HasError("history.factKey: is required"), missing.Text);
        }

        [Test]
        public void TextKeys_WithoutAText_AreOnlyAWarning()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, null, HistFixture.AllEvents(), new FakeKeys("hist.1995.win95.title"));
            Assert.True(verdict.Ok, "a missing text is a warning, the entry stays: " + verdict.Text);
            Assert.Equal(0, verdict.Errors.Count, verdict.Text);
            Assert.True(verdict.HasWarning("history.factKey: the key \"hist.1995.win95.fact\" has no text"), verdict.Text);
            Assert.False(verdict.HasWarning("titleKey"), "the title has its text: " + verdict.Text);

            Verdict complete = HistFixture.Judge(HistFixture.Plain, null, HistFixture.AllEvents(), new FakeKeys("hist.1995.win95.title", "hist.1995.win95.fact"));
            Assert.Equal(0, complete.Warnings.Count, complete.Text);
        }

        // ---- conditions ----

        [Test]
        public void Predicates_MayBeCoreEngineOrTheFeaturesOwn()
        {
            string conditions = "[ {'type':'dateAtLeast','year':1980}, {'type':'hist.IsFabless','role':'target'}, {'type':'es.hasFab'}," +
                                "  {'all':[ {'type':'featureActive','id':'gpu-product-line'}, {'not':{'type':'es.activeInCategory'}} ]}," +
                                "  {'any':[ {'type':'hist.CaseActive'}, {'type':'hist.HasCpuSince','role':'target'} ]} ]";
            Verdict verdict = HistFixture.Judge(HistFixture.Fine, o => o.Set("conditions", HistFixture.Node(conditions)));
            Assert.True(verdict.Ok, verdict.Text);
        }

        [Test]
        public void APredicateOfAnotherFeature_IsRejected()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o => o.Set("conditions", HistFixture.Node("[ {'all':[ {'type':'ms.quake'} ]} ]")));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("predicate \"ms.quake\" belongs to the feature market-shocks"), verdict.Text);
        }

        [Test]
        public void APredicateWithAnUnknownPrefix_IsRejected()
        {
            // A registry that knows the name (another mod, a typo that happens to exist) is not enough: the prefix has to belong to a feature.
            CalendarEntry entry = HistFixture.Entry(HistFixture.Plain);
            entry.ConditionUse.Predicates.Add("zz.thing");
            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), null);
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("predicate \"zz.thing\" has an unknown prefix"), verdict.Text);
        }

        [Test]
        public void AnUnknownPredicate_StopsTheEntryAtTheParser()
        {
            var messages = new List<string>();
            CalendarEntry entry = HistFixture.Map(HistFixture.Obj(HistFixture.Plain).Also(o => o.Set("conditions", HistFixture.Node("[ {'type':'hist.NoSuchThing'} ]"))), messages);
            Assert.Null(entry, "an entry with a predicate nobody registered is not built");
            Assert.True(messages.Any(m => m.Contains("entry hist.1995.win95") && m.Contains("unknown predicate \"hist.NoSuchThing\"")), string.Join(" | ", messages));
        }

        [Test]
        public void AConditionRole_MustBeDeclared()
        {
            Verdict ghost = HistFixture.Judge(HistFixture.Fine, o =>
                o.Set("conditions", HistFixture.Node("[ {'type':'hist.ShareAtLeast','role':'ghost','value':0.5,'months':36} ]")));
            Assert.False(ghost.Ok, ghost.Text);
            Assert.True(ghost.HasError("conditions: refers to the role \"ghost\" that the entry does not declare"), ghost.Text);

            Verdict onlyIf = HistFixture.Judge(HistFixture.Export, o =>
                FireAt(o, 0, 1).Set("onlyIf", HistFixture.Node("{'type':'hist.IsFabless','role':'ghost'}")));
            Assert.False(onlyIf.Ok, onlyIf.Text);
            Assert.True(onlyIf.HasError("stages[0].fires[1].onlyIf: refers to the role \"ghost\""), onlyIf.Text);
        }

        [Test]
        public void ThePredicateOfAnOnlyIf_FollowsTheSameRule()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Export, o =>
                FireAt(o, 0, 1).Set("onlyIf", HistFixture.Node("{'type':'ms.quake'}")));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.HasError("stages[0].fires[1].onlyIf: predicate \"ms.quake\" belongs to the feature market-shocks"), verdict.Text);
        }

        // ---- the validator itself ----

        [Test]
        public void EveryMessage_NamesTheEntry()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("jitterDays", 99);
                o.Set("category", "weird");
                o.Set("fallback", "retry");
                o.GetObject("history").Set("sources", HistFixture.Node("[]"));
            });
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.Errors.Count >= 4, verdict.Text);
            foreach (string error in verdict.Errors) Assert.True(error.StartsWith("entry hist.1995.win95 "), "the line must name the entry: " + error);
        }

        [Test]
        public void ARunThatFindsSeveralProblems_ReportsAllOfThem()
        {
            Verdict verdict = HistFixture.Judge(HistFixture.Plain, o =>
            {
                o.Set("jitterDays", 99);
                o.Set("category", "weird");
            });
            Assert.True(verdict.HasError("jitterDays"), verdict.Text);
            Assert.True(verdict.HasError("category"), verdict.Text);
        }

        [Test]
        public void ANullEntry_IsRejected()
        {
            var errors = new List<string>();
            Assert.False(CalendarValidator.Validate(null, null, null, errors));
            Assert.Equal(1, errors.Count);
        }

        [Test]
        public void AHalfBuiltEntry_NeverThrows()
        {
            var entry = new CalendarEntry
            {
                Id = "hist.x",
                Roles = null,
                Conditions = null,
                ConditionUse = null,
                Stages = new List<StageSpec> { null, new StageSpec { Fires = null }, new StageSpec { Fires = new List<FireSpec> { null, new FireSpec { ExtraArgs = null, OnlyIfUse = null } } } },
                FallbackFires = new List<FireSpec> { null },
                History = new HistoryInfo { Sources = null },
            };

            Verdict verdict = HistFixture.Judge(entry, HistFixture.AllEvents(), new FakeKeys("x"));
            Assert.False(verdict.Ok, verdict.Text);
            Assert.True(verdict.Errors.Count > 5, verdict.Text);

            var errors = new List<string>();
            Assert.False(CalendarValidator.Validate(entry, null, null, null, null), "null lists of messages are fine");
            Assert.NotNull(errors);
        }

        [Test]
        public void Validate_WithOneList_PutsTheWarningsOfAGoodEntryInIt()
        {
            // The form ModContent.LoadRecords wants: one list; messages of an accepted record become warnings, of a rejected one errors.
            CalendarEntry entry = HistFixture.Entry(HistFixture.Plain);
            var messages = new List<string>();
            bool ok = false;
            HistFixture.WithIds(() => { ok = CalendarValidator.Validate(entry, HistFixture.AllEvents(), new FakeKeys("hist.1995.win95.title"), messages); });
            Assert.True(ok);
            Assert.Equal(1, messages.Count, string.Join(" | ", messages));
            Assert.True(messages[0].Contains("has no text"), messages[0]);
        }

        [Test]
        public void TheCalendar_NeedsAtLeast25ValidEntries()
        {
            Assert.Equal(25, CalendarValidator.MinValidEntries);
            Assert.False(CalendarValidator.IsUsable(0));
            Assert.False(CalendarValidator.IsUsable(24));
            Assert.True(CalendarValidator.IsUsable(25));
            Assert.True(CalendarValidator.IsUsable(42));
        }

        [Test]
        public void TheLimits_AreThoseOfTheDesign()
        {
            Assert.Equal("1971-01-01", CalendarValidator.MinDate.ToString());
            Assert.Equal("2030-12-31", CalendarValidator.MaxDate.ToString());
            Assert.Equal(60, CalendarValidator.MaxJitterDays);
            Assert.Equal(1825, CalendarValidator.MaxOffsetDays);
            Assert.Equal(12, CalendarValidator.MaxUniqueId);
            Assert.Equal(120, CalendarValidator.MaxMonths);
        }

        // ---- the block acquisitionAntitrust of params.json ----

        [Test]
        public void SoftShare_MustBeBetweenZeroAndOne()
        {
            AssertRange(p => p.SoftShare = 0.0f, p => p.SoftShare = 1.0f, p => p.SoftShare = -0.01f, p => p.SoftShare = 1.01f, "acquisitionAntitrust.softShare");
        }

        [Test]
        public void Slope_MustBeBetweenZeroAndTen()
        {
            AssertRange(p => p.Slope = 0f, p => p.Slope = 10f, p => p.Slope = -0.1f, p => p.Slope = 10.1f, "acquisitionAntitrust.slope");
        }

        [Test]
        public void CaseSurcharge_MustBeBetweenOneAndThree()
        {
            AssertRange(p => p.CaseSurcharge = 1f, p => p.CaseSurcharge = 3f, p => p.CaseSurcharge = 0.99f, p => p.CaseSurcharge = 3.01f, "acquisitionAntitrust.caseSurcharge");
        }

        [Test]
        public void MaxMultiplier_MustBeBetweenOneAndThree()
        {
            AssertRange(p => p.MaxMultiplier = 1f, p => p.MaxMultiplier = 3f, p => p.MaxMultiplier = 0.5f, p => p.MaxMultiplier = 4f, "acquisitionAntitrust.maxMultiplier");
        }

        [Test]
        public void ShareMonths_MustBeBetween1And120()
        {
            AssertRange(p => p.ShareMonths = 1, p => p.ShareMonths = 120, p => p.ShareMonths = 0, p => p.ShareMonths = 121, "acquisitionAntitrust.shareMonths");
        }

        [Test]
        public void TheOtherNumbersOfParams_HaveRanges()
        {
            foreach (int bad in new[] { -1, 366 })
            {
                var p = new HistContentParams { MajorSpacingDays = bad };
                var errors = new List<string>();
                Assert.False(CalendarValidator.ValidateParams(p, errors), "majorSpacingDays " + bad);
                Assert.True(errors.Any(e => e.Contains("majorSpacingDays")), string.Join(" | ", errors));
            }

            foreach (int bad in new[] { 0, 121 })
            {
                var p = new HistContentParams { ShareCacheMonths = bad };
                var errors = new List<string>();
                Assert.False(CalendarValidator.ValidateParams(p, errors), "shareCacheMonths " + bad);
                Assert.True(errors.Any(e => e.Contains("shareCacheMonths")), string.Join(" | ", errors));
            }

            var priority = new HistContentParams { AiChooserPriority = 5000 };
            var priorityErrors = new List<string>();
            Assert.False(CalendarValidator.ValidateParams(priority, priorityErrors));
            Assert.True(priorityErrors.Any(e => e.Contains("aiChooserPriority")), string.Join(" | ", priorityErrors));
        }

        [Test]
        public void TheDefaultParams_AreValid_AndAMissingBlockIsNot()
        {
            var errors = new List<string>();
            Assert.True(CalendarValidator.ValidateParams(HistContentParams.Defaults(), errors), string.Join(" | ", errors));
            Assert.Equal(0, errors.Count);

            var broken = new HistContentParams { AcquisitionAntitrust = null };
            Assert.False(CalendarValidator.ValidateParams(broken, errors));
            Assert.True(errors.Any(e => e.Contains("acquisitionAntitrust: the block is missing")), string.Join(" | ", errors));
            Assert.False(CalendarValidator.ValidateParams(null, errors));
        }

        [Test]
        public void ParamsThatAreNotNumbers_AreRejected()
        {
            var p = new HistContentParams();
            p.AcquisitionAntitrust.Slope = float.NaN;
            p.AcquisitionAntitrust.SoftShare = float.PositiveInfinity;
            var errors = new List<string>();
            Assert.False(CalendarValidator.ValidateParams(p, errors));
            Assert.True(errors.Any(e => e.Contains("acquisitionAntitrust.slope")), string.Join(" | ", errors));
            Assert.True(errors.Any(e => e.Contains("acquisitionAntitrust.softShare")), string.Join(" | ", errors));
        }

        // ---- helpers ----

        private static void AssertRange(
            Action<AcquisitionAntitrustParams> lowEdge, Action<AcquisitionAntitrustParams> highEdge,
            Action<AcquisitionAntitrustParams> below, Action<AcquisitionAntitrustParams> above, string member)
        {
            foreach (Action<AcquisitionAntitrustParams> good in new[] { lowEdge, highEdge })
            {
                var p = new HistContentParams();
                good(p.AcquisitionAntitrust);
                var errors = new List<string>();
                Assert.True(CalendarValidator.ValidateParams(p, errors), member + " edge: " + string.Join(" | ", errors));
            }

            foreach (Action<AcquisitionAntitrustParams> bad in new[] { below, above })
            {
                var p = new HistContentParams();
                bad(p.AcquisitionAntitrust);
                var errors = new List<string>();
                Assert.False(CalendarValidator.ValidateParams(p, errors), member + " outside the range");
                Assert.True(errors.Any(e => e.StartsWith("params.json " + member + ":")), string.Join(" | ", errors));
            }
        }

        private static JsonObject StageAt(JsonObject entry, int index)
        {
            return (JsonObject)entry.GetArray("stages")[index];
        }

        private static JsonObject FireAt(JsonObject entry, int stage, int fire)
        {
            return (JsonObject)StageAt(entry, stage).GetArray("fires")[fire];
        }

        private static JsonObject RoleAt(JsonObject entry, int index)
        {
            return (JsonObject)entry.GetArray("roles")[index];
        }
    }

    internal static class JsonObjectTestExtensions
    {
        /// <summary>Runs the change and returns the object, to tweak a parsed record inside an expression.</summary>
        public static JsonObject Also(this JsonObject obj, Action<JsonObject> change)
        {
            change(obj);
            return obj;
        }
    }
}
