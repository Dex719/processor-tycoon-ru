using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-020 (FR-13, AC-13.1): the public API of the feature. The rules behind <c>FlawRegistry</c> are the pure <see cref="FlawApiEngine"/>: <c>TryReveal</c> (a CPU without a flaw gives
    /// <c>false</c> and nothing happens), <c>ForceFlaw</c> (creates the record and starts the discovery, raises the severity of a hidden one), the lists of known flaws (never a
    /// hidden one), <c>FindByCpu</c>, and the snapshot <see cref="FlawRecordView"/> with <c>PublicSinceDay</c>. The facade in <c>Api/</c> (a null object while the feature is not
    /// enabled, XML comments on the members) is checked on its source: the unit build cannot compile it.
    /// </summary>
    [Covers(typeof(FlawApiEngine), typeof(IFlawApiHost), typeof(FlawCpuInfo), typeof(FlawViews), typeof(FlawRecordView))]
    public class FlawRegistryApiTests
    {
        // ---- TryReveal (FR-13 item 1) ----

        [Test]
        public void TryReveal_ForACpuWithoutAFlaw_IsFalse_WithoutSideEffects()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            world.Store.MarkRolled(100);   // rolled, no flaw

            Assert.False(world.Engine.TryReveal(100, DiscoverySource.External), "the CPU has no record");
            Assert.False(world.Engine.TryReveal(999, DiscoverySource.External), "an unknown CPU");

            Assert.Equal(0, world.Store.All.Count, "no record was made");
            Assert.Equal(0, world.CycleHost.Events.Count, "nothing happened in the cycle: no state change, no decision");
            Assert.Equal(0, world.Host.Lines.Count, "nothing was logged either");
            Assert.True(world.Store.WasRolled(100), "the mark of the roll is as it was");
        }

        [Test]
        public void TryReveal_MakesAHiddenFlawKnown_BySource()
        {
            foreach (DiscoverySource source in new[] { DiscoverySource.External, DiscoverySource.Api, DiscoverySource.Forced, DiscoverySource.Internal })
            {
                var world = new ApiWorld();
                world.AddCpu(100);
                FlawRecord record = world.AddRecord(FlawState.Hidden, 1, 100);

                Assert.True(world.Engine.TryReveal(100, source), "revealed by " + source);

                FlawState expected = source == DiscoverySource.Internal ? FlawState.InternalKnown : FlawState.Public;
                Assert.Equal(expected, record.State, "the state after " + source);
                Assert.Equal(source, record.Source, "the source is kept");
                Assert.Equal(world.Day, record.Data.discoveredDay, "the day of the discovery");
                Assert.NotNull(record.Data.quote, "the quote is saved at the discovery (D9)");
                Assert.Equal(1, world.CycleHost.Count("discovered#1"), "the game reacts once: the window of the player, the choice of an AI company");
            }
        }

        [Test]
        public void TryReveal_AVariantOfTheCpu_RevealsTheRecordOfItsBase()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            FlawRecord record = world.AddRecord(FlawState.Hidden, 1, 100);
            record.Data.cpuIds.Add(101);

            Assert.True(world.Engine.TryReveal(101, DiscoverySource.External), "the variant is covered by the record");
            Assert.Equal(FlawState.Public, record.State);
        }

        [Test]
        public void TryReveal_RefusesWhatIsNotADiscovery()
        {
            foreach (DiscoverySource source in new[] { DiscoverySource.None, DiscoverySource.Leak, (DiscoverySource)99, (DiscoverySource)(-1) })
            {
                var world = new ApiWorld();
                world.AddCpu(100);
                FlawRecord record = world.AddRecord(FlawState.Hidden, 1, 100);

                Assert.False(world.Engine.TryReveal(100, source), "refused: " + source);
                Assert.Equal(FlawState.Hidden, record.State, "the record is as it was");
                Assert.Equal(0, world.CycleHost.Events.Count, "no side effect");
            }
        }

        [Test]
        public void TryReveal_AFlawThatIsKnownAlready_IsFalse()
        {
            foreach (FlawState state in new[] { FlawState.InternalKnown, FlawState.Public, FlawState.Stepping, FlawState.Mitigated, FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                var world = new ApiWorld();
                world.AddCpu(100);
                FlawRecord record = world.AddRecord(state, 1, 100);

                Assert.False(world.Engine.TryReveal(100, DiscoverySource.External), "a record in " + state);
                Assert.Equal(state, record.State);
                Assert.Equal(0, world.CycleHost.Events.Count, "no side effect for " + state);
            }
        }

        [Test]
        public void TryReveal_WhenTheOwnerOrTheCpuIsGone_OrTheSaveIsLocked_IsFalse_AndTheRecordStaysHidden()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            FlawRecord record = world.AddRecord(FlawState.Hidden, 1, 100);

            world.Host.DeadOwners.Add("player");
            Assert.False(world.Engine.TryReveal(100, DiscoverySource.External), "the owner is bankrupt");
            world.Host.DeadOwners.Clear();

            world.Host.Cpus.Clear();
            Assert.False(world.Engine.TryReveal(100, DiscoverySource.External), "the game has no such CPU any more");
            world.AddCpu(100);

            world.Host.Locked = true;
            Assert.False(world.Engine.TryReveal(100, DiscoverySource.External), "the save is held back by a newer mod");
            world.Host.Locked = false;

            Assert.Equal(FlawState.Hidden, record.State, "the monthly pass closes a record whose owner or CPU is gone; the API does not");
            Assert.True(world.Engine.TryReveal(100, DiscoverySource.External), "and with everything in place it works");
        }

        // ---- ForceFlaw (FR-13 item 2, AC-13.1) ----

        [Test]
        public void ForceFlaw_CreatesTheRecord_AndStartsTheDiscovery()
        {
            var world = new ApiWorld();
            world.AddCpu(100, "player", -1, true, false, 101, 102);

            bool forced = world.Engine.ForceFlaw(100, FlawTypeIds.Security, FlawSeverity.Major, DiscoverySource.External);

            Assert.True(forced, "AC-13.1: the call is done");
            FlawRecord record = world.Store.FindByCpu(100);
            Assert.NotNull(record, "a record exists");
            Assert.Equal("player", record.CompanyKey);
            Assert.Equal(FlawTypeIds.Security, record.TypeId);
            Assert.Equal(FlawSeverity.Major, record.Severity);
            Assert.Equal(FlawState.Public, record.State, "External is a public discovery");
            Assert.Equal(DiscoverySource.External, record.Source);
            Assert.Equal(new[] { 100, 101, 102 }, record.Data.cpuIds.ToArray(), "the base CPU and the variants it has now");
            Assert.True(world.Store.WasRolled(100), "the CPU counts as rolled: the roll never runs for it");
            Assert.Equal(1.5f, record.Data.inflationAtRelease, "the inflation of the day is stored");
            Assert.Equal(world.Day - 400, record.Data.releaseDay, "the release day is the game's");
            Assert.True(record.Data.riskAtRelease > 0f, "the probability of the design is stored");
            Assert.NotNull(record.Data.quote, "the quote is made at once");
            Assert.Equal(1, world.CycleHost.Count("discovered#" + record.Id), "AC-13.1: the owner is asked (the window for the player, the policy for an AI company)");
        }

        [Test]
        public void ForceFlaw_InternalDiscovery_LeavesTheFlawWithTheCompany()
        {
            var world = new ApiWorld();
            world.AddCpu(100);

            Assert.True(world.Engine.ForceFlaw(100, FlawTypeIds.Arithmetic, FlawSeverity.Minor, DiscoverySource.Internal));

            Assert.Equal(FlawState.InternalKnown, world.Store.FindByCpu(100).State);
        }

        [Test]
        public void ForceFlaw_ForAnAiCpu_NeedsTheSettingAiFlaws()
        {
            var world = new ApiWorld();
            world.AddCpu(200, "ai:7");

            world.Host.AiFlaws = false;
            Assert.False(world.Engine.ForceFlaw(200, FlawTypeIds.Thermal, FlawSeverity.Major, DiscoverySource.External), "aiFlaws is off: no flaws for rivals");
            Assert.Equal(0, world.Store.All.Count);

            world.Host.AiFlaws = true;
            Assert.True(world.Engine.ForceFlaw(200, FlawTypeIds.Thermal, FlawSeverity.Major, DiscoverySource.External));
            Assert.Equal("ai:7", world.Store.FindByCpu(200).CompanyKey, "the record belongs to the AI company");
        }

        [Test]
        public void ForceFlaw_RefusesWhatCannotGetAFlaw_AndMakesNoRecord()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            world.AddCpu(101, "player", -1, released: false);
            world.AddCpu(102, "player", -1, true, retired: true);
            world.AddCpu(103, null);
            world.AddCpu(104, "ai:3");
            world.Host.DeadOwners.Add("ai:3");

            Assert.False(world.Engine.ForceFlaw(999, null, FlawSeverity.Major, DiscoverySource.External), "an unknown CPU");
            Assert.False(world.Engine.ForceFlaw(101, null, FlawSeverity.Major, DiscoverySource.External), "a CPU that is not released");
            Assert.False(world.Engine.ForceFlaw(102, null, FlawSeverity.Major, DiscoverySource.External), "a CPU that left the market");
            Assert.False(world.Engine.ForceFlaw(103, null, FlawSeverity.Major, DiscoverySource.External), "a CPU whose owner is not in the world");
            Assert.False(world.Engine.ForceFlaw(104, null, FlawSeverity.Major, DiscoverySource.External), "a bankrupt owner");
            Assert.False(world.Engine.ForceFlaw(100, "no-such-type", FlawSeverity.Major, DiscoverySource.External), "a type the content does not know");
            Assert.False(world.Engine.ForceFlaw(100, null, (FlawSeverity)7, DiscoverySource.External), "a severity outside the enum");
            Assert.False(world.Engine.ForceFlaw(100, null, FlawSeverity.Major, DiscoverySource.None), "no discovery");
            Assert.False(world.Engine.ForceFlaw(100, null, FlawSeverity.Major, DiscoverySource.Leak), "a leak is not a discovery");

            Assert.Equal(0, world.Store.All.Count, "no record was made");
            Assert.False(world.Store.WasRolled(100), "and no mark either");
            Assert.Equal(0, world.CycleHost.Events.Count, "no side effect");

            world.Host.Locked = true;
            Assert.False(world.Engine.ForceFlaw(100, null, FlawSeverity.Major, DiscoverySource.External), "the save is held back");
        }

        [Test]
        public void ForceFlaw_WithoutAType_ChoosesByTheUsualWeights_ForTheYearOfTheCpu()
        {
            var world = new ApiWorld();
            world.AddCpu(100).Risk = FlawApiKit.Design(1998);
            world.Host.Rng_ = new FakeRng(0.999f);   // the last type that is allowed in 1998
            Assert.True(world.Engine.ForceFlaw(100, "", FlawSeverity.Minor, DiscoverySource.External));
            Assert.NotEqual(FlawTypeIds.Security, world.Store.FindByCpu(100).TypeId, "no security holes before 2015");

            var late = new ApiWorld();
            late.AddCpu(100).Risk = FlawApiKit.Design(2018);
            late.Host.Rng_ = new FakeRng(0.999f);
            Assert.True(late.Engine.ForceFlaw(100, null, FlawSeverity.Minor, DiscoverySource.External));
            Assert.Equal(FlawTypeIds.Security, late.Store.FindByCpu(100).TypeId, "the last type of the table is open in 2018");
        }

        [Test]
        public void ForceFlaw_ACpuTheDesignOfWhichCannotBeRead_StillGetsTheFlaw()
        {
            var world = new ApiWorld();
            FlawCpuInfo info = world.AddCpu(100);
            info.HasRisk = false;

            Assert.True(world.Engine.ForceFlaw(100, null, FlawSeverity.Major, DiscoverySource.External));
            Assert.Equal(0f, world.Store.FindByCpu(100).Data.riskAtRelease, "no design, no probability");
        }

        [Test]
        public void ForceFlaw_OnAHiddenRecord_RaisesTheSeverity_AndKeepsTheType()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            FlawRecord record = world.AddRecord(FlawState.Hidden, 1, 100, "player", FlawSeverity.Minor);

            Assert.True(world.Engine.ForceFlaw(100, FlawTypeIds.Security, FlawSeverity.Critical, DiscoverySource.External));

            Assert.Equal(FlawSeverity.Critical, record.Severity, "the severity was raised");
            Assert.Equal("arith", record.TypeId, "the type of the roll stays");
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(1, world.Store.All.Count, "still one record");

            var other = new ApiWorld();
            other.AddCpu(100);
            FlawRecord heavy = other.AddRecord(FlawState.Hidden, 1, 100, "player", FlawSeverity.Critical);
            Assert.True(other.Engine.ForceFlaw(100, null, FlawSeverity.Minor, DiscoverySource.External));
            Assert.Equal(FlawSeverity.Critical, heavy.Severity, "a lower severity never lowers it");
        }

        [Test]
        public void ForceFlaw_OnAFlawThatIsKnownOrOver_IsFalse_AndChangesNothing()
        {
            foreach (FlawState state in new[] { FlawState.InternalKnown, FlawState.Public, FlawState.Stepping, FlawState.Mitigated, FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                var world = new ApiWorld();
                world.AddCpu(100);
                FlawRecord record = world.AddRecord(state, 1, 100, "player", FlawSeverity.Minor);

                Assert.False(world.Engine.ForceFlaw(100, null, FlawSeverity.Critical, DiscoverySource.External), "a record in " + state);
                Assert.Equal(FlawSeverity.Minor, record.Severity, "the severity of a " + state + " record stays");
                Assert.Equal(0, world.CycleHost.Events.Count, "no side effect for " + state);
            }
        }

        [Test]
        public void ForceFlaw_TwiceOnTheSameCpu_TheSecondIsFalse()
        {
            var world = new ApiWorld();
            world.AddCpu(100);

            Assert.True(world.Engine.ForceFlaw(100, null, FlawSeverity.Major, DiscoverySource.External));
            Assert.False(world.Engine.ForceFlaw(100, null, FlawSeverity.Critical, DiscoverySource.External), "the flaw is known now");
            Assert.Equal(1, world.Store.All.Count);
            Assert.Equal(FlawSeverity.Major, world.Store.FindByCpu(100).Severity);
        }

        [Test]
        public void ForceFlaw_AVariant_IsAskedThroughItsBase()
        {
            var world = new ApiWorld();
            world.AddCpu(100, "player", -1, true, false, 101);
            world.AddCpu(101, "player", 100);

            Assert.True(world.Engine.ForceFlaw(101, null, FlawSeverity.Major, DiscoverySource.External));

            FlawRecord record = world.Store.FindByCpu(100);
            Assert.NotNull(record, "the record is the base CPU's");
            Assert.Equal(100, record.BaseCpuId);
            Assert.True(record.CoversCpu(101), "and covers the variant");
            Assert.Equal(1, world.Store.All.Count);
        }

        // ---- the lists of known flaws (FR-13 item 3) ----

        private static ApiWorld WorldWithRecords()
        {
            var world = new ApiWorld();
            world.AddRecord(FlawState.Hidden, 1, 100);
            world.AddRecord(FlawState.InternalKnown, 2, 101);
            world.AddRecord(FlawState.Public, 3, 102, "ai:5", FlawSeverity.Critical, FlawResponse.None, 16000);
            world.AddRecord(FlawState.Resolved, 4, 103, "player", FlawSeverity.Minor, FlawResponse.Recall);
            world.AddRecord(FlawState.Hidden, 5, 104, "ai:5");
            return world;
        }

        [Test]
        public void Known_NeverReturnsAHiddenRecord()
        {
            ApiWorld world = WorldWithRecords();

            IReadOnlyList<FlawRecordView> mine = world.Engine.Known("player");
            Assert.Equal(new[] { 2, 4 }, mine.Select(v => v.Id).ToArray(), "the known records of the player, final ones too, no hidden one");

            IReadOnlyList<FlawRecordView> rival = world.Engine.Known("ai:5");
            Assert.Equal(new[] { 3 }, rival.Select(v => v.Id).ToArray(), "the hidden record of the rival is not there");

            Assert.Equal(new[] { 2, 3, 4 }, world.Engine.AllKnown().Select(v => v.Id).ToArray(), "all known records");
            Assert.True(world.Engine.AllKnown().All(v => v.State != FlawState.Hidden), "no hidden state in any snapshot");
        }

        [Test]
        public void Known_ForANullKeyOrACompanyWithoutRecords_IsEmpty()
        {
            ApiWorld world = WorldWithRecords();

            Assert.Equal(0, world.Engine.Known(null).Count);
            Assert.Equal(0, world.Engine.Known("ai:99").Count);
            Assert.Equal(0, new ApiWorld().Engine.AllKnown().Count, "an empty store");
        }

        [Test]
        public void FindByCpu_ShowsKnownRecords_AndAnswersNullForAHiddenOneLikeForNoRecord()
        {
            ApiWorld world = WorldWithRecords();

            Assert.Null(world.Engine.FindByCpu(100), "a hidden flaw is never revealed by a lookup");
            Assert.Null(world.Engine.FindByCpu(555), "an unknown CPU gives the same answer: no hint at a hidden flaw");
            Assert.Equal(2, world.Engine.FindByCpu(101).Id);
            Assert.Equal(FlawState.Resolved, world.Engine.FindByCpu(103).State, "a final record is shown too");

            Assert.True(world.Store.AttachVariant(world.Store.FindByCpu(101), 150));
            Assert.Equal(2, world.Engine.FindByCpu(150).Id, "a CPU of the record other than the base one");
        }

        // ---- the snapshot (FR-13, FR-17 item 2) ----

        [Test]
        public void View_IsACopy_AndCarriesTheFieldsOfTheRecord()
        {
            var world = new ApiWorld();
            FlawRecord record = world.AddRecord(FlawState.Mitigated, 7, 100, "player", FlawSeverity.Major, FlawResponse.Patch, publicSince: 16100);
            record.Data.cpuIds.Add(101);
            record.Data.mitigationEndDay = world.Day + 200;
            record.Data.ipsLoss = 0.12f;
            record.Data.spentTotal = 7250000f;
            record.Data.coverUp = true;
            record.Source = DiscoverySource.External;

            FlawRecordView view = FlawViews.Of(record, world.Day);

            Assert.Equal(7, view.Id);
            Assert.Equal("player", view.CompanyKey);
            Assert.Equal(100, view.BaseCpuId);
            Assert.Equal(new[] { 100, 101 }, view.CpuIds.ToArray());
            Assert.Equal("arith", view.TypeId);
            Assert.Equal(FlawSeverity.Major, view.Severity);
            Assert.Equal(FlawState.Mitigated, view.State);
            Assert.Equal(FlawResponse.Patch, view.Response);
            Assert.Equal(DiscoverySource.External, view.Source);
            Assert.True(view.CoverUp);
            Assert.Equal(200, view.DaysLeft, "the days left of the patch");
            Assert.Equal(7250000f, view.SpentTotal);
            Assert.Equal(0.12f, view.IpsLoss);
            Assert.Equal(16100, view.PublicSinceDay);

            record.Data.cpuIds.Add(102);
            record.State = FlawState.Resolved;
            Assert.Equal(2, view.CpuIds.Count, "the snapshot does not follow the record");
            Assert.Equal(FlawState.Mitigated, view.State);
            Assert.True(view.ToString().Contains("#7"), "ToString names the record");
        }

        [Test]
        public void View_DaysLeft_IsZeroWhenNoTermRuns_AndNeverNegative()
        {
            var world = new ApiWorld();
            FlawRecord stepping = world.AddRecord(FlawState.Stepping, 1, 100);
            stepping.Data.steppingEndDay = world.Day - 5;
            FlawRecord waiting = world.AddRecord(FlawState.Public, 2, 101);

            Assert.Equal(0, FlawViews.Of(stepping, world.Day).DaysLeft, "a revision that is over by the calendar");
            Assert.Equal(0, FlawViews.Of(waiting, world.Day).DaysLeft, "no term");
        }

        [Test]
        public void View_PublicSinceDay_IsFilledWhenTheFlawBecomesPublic()
        {
            var world = new ApiWorld();
            world.AddCpu(100);
            world.AddRecord(FlawState.Hidden, 1, 100);

            Assert.Equal(0, FlawViews.Of(world.Store.Find(1), world.Day).PublicSinceDay, "never public yet");
            Assert.True(world.Engine.TryReveal(100, DiscoverySource.External));

            FlawRecordView view = world.Engine.FindByCpu(100);
            Assert.Equal(FlawState.Public, view.State);
            Assert.Equal(world.Day, view.PublicSinceDay, "the day it became public (rival-personalities starts the caution from it)");
        }

        [Test]
        public void View_PublicSinceDay_IsFilledOnALeak_AndKeptWhenTheFlawChangesLater()
        {
            var world = new ApiWorld();
            FlawRecord record = world.AddRecord(FlawState.InternalKnown, 1, 100, "ai:4", FlawSeverity.Major, FlawResponse.Ignore);
            world.CycleHost.Rng_ = FakeRng.Always(0f);   // every monthly roll succeeds: the cover-up leaks

            world.CycleHost.Day = FlawCycleKit.MonthStart(20);
            world.CycleHost.Facts[100] = FakeCycleHost.Selling(5000);
            world.Cycle.OnMonth();

            Assert.Equal(FlawState.Public, record.State, "the ignored flaw leaked");
            int leakDay = world.CycleHost.Day;
            Assert.Equal(leakDay, FlawViews.Of(record, leakDay).PublicSinceDay);

            Assert.True(FlawStateMachine.BeginStepping(record, leakDay + 10, 90, 1000f));
            Assert.Equal(leakDay, FlawViews.Of(record, leakDay + 10).PublicSinceDay, "it is the first day it was public, not the last");
        }

        [Test]
        public void AI17_2_APublicCritical_OfAnAiCompany_IsAnnouncedWithTheDataOfRivalPersonalities()
        {
            var world = new ApiWorld();
            world.AddCpu(300, "ai:8");
            world.AddRecord(FlawState.Hidden, 1, 300, "ai:8", FlawSeverity.Critical);

            Assert.True(world.Engine.TryReveal(300, DiscoverySource.External));

            FlawRecordView view = FlawViews.Of(world.Store.Find(1), world.Day);   // what FlawViews.Announce hands to OnFlawStateChanged
            Assert.Equal("ai:8", view.CompanyKey);
            Assert.Equal(FlawSeverity.Critical, view.Severity);
            Assert.Equal(FlawState.Public, view.State);
            Assert.Equal(world.Day, view.PublicSinceDay, "AC-17.2: PublicSinceDay is today");
            Assert.Equal(1, world.CycleHost.Count("state#1:Hidden>Public"), "the cycle reported the move to the host, which announces it");
        }

        // ---- the facade in Api/ (null object, XML comments), checked on its source ----

        [Test]
        public void Facade_EveryMethodIsANullObjectWhileTheFeatureIsNotEnabled()
        {
            string code = FlawApiKit.Code("Api/FlawRegistry.cs");
            string raw = FlawApiKit.Raw("Api/FlawRegistry.cs");

            string active = FlawApiKit.Between(code, "private IFlawRegistryBackend ActiveBackend()", "private static float[] SanitizeMultipliers");
            Assert.True(active.Contains("ModFeatures.IsActive(DesignFlawRecallsApi.FeatureId) ? backend : null"), "the backend answers only while the feature is enabled");

            foreach (string method in new[] { "TryReveal", "ForceFlaw", "GetKnownFlaws", "GetAllKnownFlaws", "FindByCpu" })
            {
                int start = code.IndexOf(" " + method + "(", StringComparison.Ordinal);
                Assert.True(start > 0, method + " exists");
                string body = code.Substring(start, Math.Min(900, code.Length - start));
                Assert.True(body.Contains("ActiveBackend()"), method + " asks for the active backend");
                Assert.True(body.Contains("try") && body.Contains("catch (Exception e)") && body.Contains("Fault("), method + " never throws: the failure is counted against the feature");
            }

            Assert.True(Regex.IsMatch(code, @"return backend != null && backend\.TryReveal\("), "TryReveal: false without a backend");
            Assert.True(Regex.IsMatch(code, @"return backend != null && backend\.ForceFlaw\("), "ForceFlaw: false without a backend");
            Assert.True(code.Contains("if (backend == null) return NoFlaws;"), "the lists are empty without a backend");
            Assert.True(code.Contains("return backend == null ? null : backend.FindByCpu("), "FindByCpu: null without a backend");
            Assert.True(code.Contains("if (ReferenceEquals(company, null)) return NoFlaws;"), "a null company gives an empty list");
            Assert.True(raw.Contains("never <c>null</c>") || raw.Contains("Never <c>null</c>"), "the documentation promises non-null lists");
        }

        [Test]
        public void Facade_EveryPublicMemberHasAnXmlComment()
        {
            foreach (string file in new[] { "Api/FlawRegistry.cs", "Api/IFlawRegistryBackend.cs", "Api/IFlawResponseBias.cs", "Api/DesignFlawRecallsApi.cs", "Api/Model/FlawRecordView.cs", "Api/Model/FlawEnums.cs" })
            {
                string[] lines = FlawApiKit.Raw(file).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (!line.StartsWith("public ", StringComparison.Ordinal)) continue;

                    int back = i - 1;
                    while (back >= 0 && lines[back].TrimStart().StartsWith("[", StringComparison.Ordinal)) back--;
                    bool documented = back >= 0 && lines[back].TrimStart().StartsWith("///", StringComparison.Ordinal);
                    Assert.True(documented, file + ":" + (i + 1) + ": the public member has no XML comment: " + line);
                }
            }
        }

        [Test]
        public void Feature_AttachesTheBackendWhileEnabled_AndDetachesIt()
        {
            string feature = FlawApiKit.Code("DesignFlawRecallsFeature.cs");

            Assert.True(feature.Contains("FlawRegistry.Shared.AttachBackend(Api);"), "OnEnabled attaches the store of the API");
            Assert.True(feature.Contains("FlawRegistry.Shared.DetachBackend(Api)"), "OnDisabled detaches it");
            Assert.True(FlawApiKit.Between(feature, "public void OnEnabled()", "public void OnDisabled()").Contains("AttachBackend"), "attached in OnEnabled");
            Assert.True(FlawApiKit.Code("FlawApiBackend.cs").Contains("internal sealed class FlawApiBackend : IFlawRegistryBackend, IFlawApiHost"), "the backend implements the contract of the Api");
        }
    }
}
