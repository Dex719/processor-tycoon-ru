using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-006 (FR-1, FR-16): the store of the records and the marks over the saved data: creation, the variants of a base CPU, "rolled" and "rush" marks,
    /// the lookups by CPU and by id, the records the monthly monitor looks at, and that everything is the saved data (a round trip through the side
    /// file gives the same store).
    /// </summary>
    [Covers(typeof(FlawStore))]
    public class FlawStoreTests
    {
        private static FlawTypeDef Arith()
        {
            return FlawBalance.Defaults().Types.First(t => t.Id == FlawTypeIds.Arithmetic);
        }

        private static FlawStore NewStore(out FlawSaveData data)
        {
            FlawSaveData local = new FlawSaveData();
            data = local;
            return new FlawStore(() => local);
        }

        // ---- creation ----

        [Test]
        public void Create_MakesAHiddenRecord_WithTheNextId_AndTheFieldsOfTheRoll()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);

            FlawRecord a = store.Create("player", 2044, Arith(), FlawSeverity.Major, 0.167f, 16860, 6.6f);
            FlawRecord b = store.Create("ai:3", 2051, Arith(), FlawSeverity.Minor, 0.07f, 16870, 6.7f);

            Assert.Equal(1, a.Id);
            Assert.Equal(2, b.Id);
            Assert.Equal(3, data.nextRecordId);
            Assert.Equal(2, data.records.Count);
            Assert.Equal(FlawState.Hidden, a.State);
            Assert.Equal(DiscoverySource.None, a.Source);
            Assert.Equal(FlawResponse.None, a.Response);
            Assert.Equal("player", a.CompanyKey);
            Assert.Equal(2044, a.BaseCpuId);
            Assert.Equal("arith", a.TypeId);
            Assert.Equal(FlawSeverity.Major, a.Severity);
            Assert.Equal(0.167f, a.Data.riskAtRelease);
            Assert.Equal(6.6f, a.Data.inflationAtRelease);
            Assert.Equal(16860, a.Data.releaseDay);
            Assert.Equal(new List<int> { 2044 }, a.Data.cpuIds);
            Assert.True(ReferenceEquals(a.Data, data.records[0]), "the record is a face over the saved DTO");
        }

        [Test]
        public void Create_ForACpuThatAlreadyHasARecord_ReturnsNull()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            store.Create("player", 10, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);

            Assert.Null(store.Create("player", 10, Arith(), FlawSeverity.Major, 0.2f, 100, 1f));
            Assert.Equal(1, data.records.Count);
        }

        [Test]
        public void Create_WithoutAType_ReturnsNull()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);

            Assert.Null(store.Create("player", 10, null, FlawSeverity.Minor, 0.1f, 100, 1f));
            Assert.Equal(0, data.records.Count);
        }

        [Test]
        public void Create_NeverReusesAnId_EvenWhenTheCounterIsBehind()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            data.records.Add(new FlawRecordDto { id = 9, companyKey = "player", baseCpuId = 5, cpuIds = new List<int> { 5 }, typeId = "arith" });
            data.nextRecordId = 2;   // a hand-edited counter

            FlawRecord created = store.Create("player", 6, Arith(), FlawSeverity.Minor, 0.1f, 1, 1f);

            Assert.Equal(10, created.Id);
            Assert.Equal(11, data.nextRecordId);
            Assert.Equal(new[] { 9, 10 }, store.All.Select(r => r.Id).ToArray());
        }

        // ---- variants ----

        [Test]
        public void AttachVariant_AddsTheCpuToTheRecord_AndTheLookupFindsIt()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord record = store.Create("player", 2044, Arith(), FlawSeverity.Major, 0.1f, 100, 1f);

            Assert.True(store.AttachVariant(record, 2059));

            Assert.Equal(new List<int> { 2044, 2059 }, record.Data.cpuIds);
            Assert.True(ReferenceEquals(record, store.FindByCpu(2059)));
            Assert.True(record.CoversCpu(2059));
            Assert.True(store.AttachVariant(record, 2059), "the same variant twice is not an error");
            Assert.Equal(2, record.Data.cpuIds.Count);
        }

        [Test]
        public void AttachVariant_ToARecordThatIsOver_IsRefused()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord record = store.Create("player", 2044, Arith(), FlawSeverity.Major, 0.1f, 100, 1f);
            FlawStateMachine.Close(record);

            Assert.False(store.AttachVariant(record, 2059));
            Assert.Null(store.FindByCpu(2059));
        }

        [Test]
        public void AttachVariant_OfACpuThatBelongsToAnotherRecord_IsRefused()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord first = store.Create("player", 1, Arith(), FlawSeverity.Major, 0.1f, 100, 1f);
            FlawRecord second = store.Create("player", 2, Arith(), FlawSeverity.Major, 0.1f, 100, 1f);

            Assert.False(store.AttachVariant(second, 1));
            Assert.True(ReferenceEquals(first, store.FindByCpu(1)));
        }

        // ---- the marks ----

        [Test]
        public void Rolled_MarkIsRemembered_OnceInTheSavedList()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);

            Assert.False(store.WasRolled(77));
            store.MarkRolled(77);
            store.MarkRolled(77);

            Assert.True(store.WasRolled(77));
            Assert.Equal(new List<int> { 77 }, data.rolledCpuIds);
        }

        [Test]
        public void Rolled_SeesTheCpusThatTheSectionEnteredAtTheStart()
        {
            var data = new FlawSaveData();
            data.rolledCpuIds.AddRange(new[] { 1, 2, 3 });
            var store = new FlawStore(() => data);

            Assert.True(store.WasRolled(2));
            Assert.False(store.WasRolled(4));
            store.MarkRolled(4);
            Assert.Equal(new List<int> { 1, 2, 3, 4 }, data.rolledCpuIds);
        }

        [Test]
        public void Rush_MarkIsRemembered_InTheSavedList()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);

            Assert.False(store.IsRush(2051));
            store.MarkRush(2051);
            store.MarkRush(2051);

            Assert.True(store.IsRush(2051));
            Assert.Equal(new List<int> { 2051 }, data.rushCpuIds);
        }

        // ---- following the data ----

        [Test]
        public void Store_FollowsAReplacedDataObject_LikeALoadOfASave()
        {
            FlawSaveData current = new FlawSaveData();
            var store = new FlawStore(() => current);
            store.Create("player", 1, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            store.MarkRolled(1);
            Assert.Equal(1, store.All.Count);

            current = new FlawSaveData();   // FlawSaveSection.Read / ResetToDefaults replace the object
            current.rolledCpuIds.Add(500);

            Assert.Equal(0, store.All.Count, "the old records are gone");
            Assert.False(store.WasRolled(1));
            Assert.True(store.WasRolled(500));
            Assert.Null(store.FindByCpu(1));
            Assert.Equal(1, store.Create("player", 2, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f).Id, "the new data starts its own ids");
        }

        [Test]
        public void Store_KeepsTheRuntimeFieldsOfTheRecords_BetweenCalls()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord record = store.Create("player", 1, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            record.FailStreak = 2;
            record.CachedUnits = 123;

            store.MarkRolled(1);
            store.MarkRush(2);
            FlawRecord again = store.Find(record.Id);

            Assert.True(ReferenceEquals(record, again), "the same wrapper, not a rebuilt one");
            Assert.Equal(2, again.FailStreak);
            Assert.Equal(123L, again.CachedUnits);
        }

        [Test]
        public void Store_WithDuplicateMarks_DoesNotRebuildOnEveryCall()
        {
            // A hand-edited save may list a CPU twice; the wrappers must still be stable.
            var data = new FlawSaveData();
            data.rolledCpuIds.AddRange(new[] { 5, 5, 6 });
            data.records.Add(new FlawRecordDto { id = 1, companyKey = "player", baseCpuId = 9, cpuIds = new List<int> { 9 }, typeId = "arith" });
            var store = new FlawStore(() => data);

            FlawRecord first = store.Find(1);
            store.WasRolled(5);
            FlawRecord second = store.Find(1);

            Assert.True(ReferenceEquals(first, second));
        }

        // ---- the lists the monitor and the API use ----

        [Test]
        public void Active_IsTheRecordsThatAreNotOver_InIdOrder()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord a = store.Create("player", 1, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            FlawRecord b = store.Create("player", 2, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            FlawRecord c = store.Create("player", 3, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            FlawStateMachine.Expire(b);

            Assert.Equal(new[] { a.Id, c.Id }, store.Active().Select(r => r.Id).ToArray());
            Assert.Equal(3, store.All.Count);
        }

        [Test]
        public void Monitored_AddsAResolvedRecordWhileItsLeakTailRuns()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord quiet = store.Create("player", 1, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            quiet.Data.state = (int)FlawState.Resolved;
            quiet.Data.quietStepping = true;
            quiet.Data.leakTailUntilDay = 500;
            FlawRecord loud = store.Create("player", 2, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);
            loud.Data.state = (int)FlawState.Resolved;
            loud.Data.leakTailUntilDay = 500;   // not a quiet revision: no tail to watch
            FlawRecord open = store.Create("player", 3, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);

            Assert.Equal(new[] { quiet.Id, open.Id }, store.Monitored(500).Select(r => r.Id).ToArray());
            Assert.Equal(new[] { open.Id }, store.Monitored(501).Select(r => r.Id).ToArray());
        }

        [Test]
        public void MoveOwner_ChangesOnlyTheKey()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord record = store.Create("ai:3", 1, Arith(), FlawSeverity.Minor, 0.1f, 100, 1f);

            store.MoveOwner(record, "player");
            Assert.Equal("player", record.CompanyKey);
            Assert.Equal("player", data.records[0].companyKey);

            store.MoveOwner(record, "");
            store.MoveOwner(null, "ai:1");
            Assert.Equal("player", record.CompanyKey, "an empty key is ignored");
        }

        [Test]
        public void Find_UnknownId_IsNull()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);

            Assert.Null(store.Find(5));
            Assert.Null(store.FindByCpu(5));
        }

        // ---- the save ----

        [Test]
        public void Store_RoundTripsThroughTheSideFile()
        {
            FlawSaveData data;
            FlawStore store = NewStore(out data);
            FlawRecord a = store.Create("player", 2044, Arith(), FlawSeverity.Major, 0.167f, 16860, 6.6f);
            store.AttachVariant(a, 2059);
            store.MarkRolled(2044);
            store.MarkRolled(2059);
            store.MarkRush(2044);
            FlawStateMachine.Discover(a, DiscoverySource.External, 17000);
            FlawStateMachine.BeginMitigation(a, 17001, 18, 0.12f);
            store.Create("ai:4", 3000, Arith(), FlawSeverity.Minor, 0.05f, 16900, 6.6f);
            store.LastActiveDay = 17005;

            JsonObject written = FlawSaveCodec.Write(data);
            FlawSaveData loaded = FlawSaveCodec.Read(FlawTestKit.ParseObject(ModJson.Write(written, false)), new List<string>());
            var reloaded = new FlawStore(() => loaded);

            Assert.Equal(2, reloaded.All.Count);
            FlawRecord b = reloaded.Find(1);
            Assert.Equal(FlawState.Mitigated, b.State);
            Assert.Equal(FlawResponse.Patch, b.Response);
            Assert.Equal(new List<int> { 2044, 2059 }, b.Data.cpuIds);
            Assert.True(ReferenceEquals(b, reloaded.FindByCpu(2059)));
            Assert.True(reloaded.WasRolled(2059));
            Assert.True(reloaded.IsRush(2044));
            Assert.Equal(17005, reloaded.LastActiveDay);
            Assert.Equal(3, loaded.nextRecordId);
        }
    }
}
