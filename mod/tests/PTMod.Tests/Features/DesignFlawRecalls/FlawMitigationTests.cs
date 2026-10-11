using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-012 (FR-8, FR-9; AC-8.1, AC-8.2, AC-9.1, AC-9.2): the patch puts an entry on <c>market.rating.cpu</c> for every CPU of the record, ends by its term or
    /// early when a revision replaces it, and never lowers the rating by more than a quarter with the shipped content; ignoring a public flaw ends contracts, lowers the
    /// chance of signing, drains the reputation and may bring one lawsuit; ignoring an internal flaw costs nothing until it leaks, and a leak asks for a new choice.
    /// </summary>
    [Covers(typeof(FlawResponseEngine), typeof(FlawChannelNames))]
    public class FlawMitigationTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        private static ResponseWorld WorldOf(params FlawRecord[] records)
        {
            var world = new ResponseWorld(records);
            world.SetDay(Day);
            world.Gateway.CpuNames[100] = "Nova 3";
            world.Gateway.CpuNames[101] = "Nova 3 Lite";
            return world;
        }

        /// <summary>A public Major flaw of the type <c>security</c> in 2018: a patch costs 12 % of the performance for 18 months (AC-8.1).</summary>
        private static FlawRecord SecurityMajor(FlawState state = FlawState.Public, int id = 1)
        {
            return ResponseWorld.Known(state, id, FlawSeverity.Major, "security", year: 2018);
        }

        // ---- the patch (AC-8.1) ----

        [Test]
        public void Patch_AC81_AddsOneEntryPerCpu_With088For18Months()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);

            bool applied = world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.True(applied);
            Assert.Equal(FlawState.Mitigated, record.State, "FR-8 item 2: the record is mitigated while the patch runs");
            Assert.Equal(FlawResponse.Patch, record.Response);
            Assert.Equal(FlawDays.AddMonths(Day, 18), record.Data.mitigationEndDay, "mitigationMonths of a Major flaw is 18");
            Assert.Equal(0.12f, record.Data.ipsLoss);

            List<FakeModifierGateway.Entry> entries = world.Gateway.On(FlawChannelNames.MarketRatingCpu);
            Assert.Equal(2, entries.Count, "the base CPU and its variant");
            var cpus = new HashSet<int>();
            foreach (FakeModifierGateway.Entry entry in entries)
            {
                Assert.Equal(ScopeKind.Cpu, entry.Scope.Kind, "ModifierScope.Cpu");
                cpus.Add(entry.Scope.CpuSaveId);
                Assert.Near(0.88, entry.Multiplier, 1e-6, "1 - ipsLoss of a Major security flaw");
                Assert.Equal("dfr.mod.patch", entry.ReasonKey);
                Assert.Equal(record.Data.mitigationEndDay - Day, entry.DurationDays, "the term in days");
                Assert.Equal(1, entry.ReasonArgs.Length);
            }
            Assert.True(cpus.Contains(100) && cpus.Contains(101));
            Assert.Equal("Nova 3", world.Gateway.Entries[record.Data.modifierEntryIds[0]].ReasonArgs[0], "the reason names the CPU");
            Assert.Equal(2, record.Data.modifierEntryIds.Count, "the ids are kept in the record");
        }

        [Test]
        public void Patch_PaysThePatch_FromDevelopment_AndChangesTheReputationAndThePopularity()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            float price = record.Quote.PatchCost;

            world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.Equal(1, world.Host.Spent.Count);
            Assert.Equal(FlawSpendKind.Development, world.Host.Spent[0].Kind);
            Assert.Equal(price, world.Host.Spent[0].Amount, "exactly the price under the button");
            Assert.Equal(price, record.Data.spentTotal);
            Assert.Equal(-2, world.Host.ReputationSum("dfr.rep.patch"));
            Assert.Near(0.02, world.Host.PopularityHits[0], 1e-6, "popHit.patch of Major");
            Assert.Equal(0, world.Host.CancelChances.Count, "a patch ends no contract");
        }

        [Test]
        public void Patch_RemovesTheWaitingEntry_OfAPublicFlaw()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            world.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            Assert.Equal(1, world.Gateway.CountReason(FlawChannelNames.ReasonPending));

            world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.Equal(0, world.Gateway.CountReason(FlawChannelNames.ReasonPending), "FR-5 item 5: the waiting entry is taken away at the answer");
            Assert.Equal(2, record.Data.modifierEntryIds.Count, "only the two entries of the patch are left");
        }

        [Test]
        public void Patch_WhenThereIsNone_IsRefused_AndChangesNothing()
        {
            // arith has a patch from 1995 only (AC-5.2).
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "arith", year: 1990);
            ResponseWorld world = WorldOf(record);
            Assert.False(record.Quote.PatchAvailable);

            bool applied = world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.False(applied);
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(0, world.Host.Spent.Count);
            Assert.Equal(0, world.Gateway.AddCount);
            Assert.Equal(1, world.Host.Warnings.Count);
        }

        [Test]
        public void Patch_EndsByItsTerm_AndTheRecordIsResolved()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Patch, false);
            int end = record.Data.mitigationEndDay;

            world.SetDay(end - 1);
            cycle.OnDay();
            Assert.Equal(FlawState.Mitigated, record.State, "the day before the end");
            Assert.Equal(2, world.Gateway.LiveEntries(FlawChannelNames.MarketRatingCpu, "player", 100).Count + world.Gateway.LiveEntries(FlawChannelNames.MarketRatingCpu, "player", 101).Count,
                "both entries still apply");

            world.SetDay(end);
            cycle.OnDay();

            Assert.Equal(FlawState.Resolved, record.State, "FR-8 item 3");
            Assert.True(host.Events.Contains("mitigationExpired#1"));
            Assert.Equal(0, record.Data.modifierEntryIds.Count, "the ids are forgotten");
            Assert.Equal(0f, record.Data.ipsLoss);
            Assert.Equal(0, world.Gateway.LiveEntries(FlawChannelNames.MarketRatingCpu, "player", 100).Count, "the core ended the entry on the same day");
        }

        [Test]
        public void Patch_KeepsTheRecordMitigated_AndItIsNotFoundAgain()
        {
            // FR-8 item 2: while the patch runs the record is Mitigated, which the monthly roll of the discovery does not touch.
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world);
            host.Facts[100] = WiredCycleHost.ExampleFacts();
            host.Rng_ = FakeRng.Always(0.0f);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Patch, false);

            world.SetDay(FlawDays.AddMonths(Day, 3));
            cycle.OnMonth();

            Assert.Equal(FlawState.Mitigated, record.State);
            Assert.Equal(0, host.Events.FindAll(e => e.StartsWith("discovered")).Count);
        }

        // ---- the limit of the patch ----

        [Test]
        public void Patch_Contribution_LiesBetween075And1_ForEveryTypeAndSeverity()
        {
            JsonObject core = CoreContentTests.LoadCore();
            double balanceMin = 0, balanceMax = 0;
            foreach (JsonNode node in core.GetArray("channels"))
            {
                var channel = node as JsonObject;
                if (channel != null && channel.GetString("name") == FlawChannelNames.MarketRatingCpu)
                {
                    balanceMin = channel.GetDouble("balanceMin", -1);
                    balanceMax = channel.GetDouble("balanceMax", -1);
                }
            }
            Assert.Near(0.5, balanceMin, 1e-9, "the balance range of market.rating.cpu in core.json: lower end");
            Assert.Near(2.0, balanceMax, 1e-9, "the balance range of market.rating.cpu in core.json: upper end");

            FlawBalance b = FlawBalance.Defaults();
            int checkedPatches = 0;
            foreach (FlawTypeDef type in b.Types)
            {
                foreach (FlawSeverity severity in new[] { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical })
                {
                    FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, severity, type.Id, year: 2020);
                    ResponseWorld world = WorldOf(record);
                    Assert.True(world.Engine.Apply(record, FlawResponse.Patch, false), type.Id + "/" + severity + ": the patch is possible in 2020");

                    foreach (FakeModifierGateway.Entry entry in world.Gateway.On(FlawChannelNames.MarketRatingCpu))
                    {
                        Assert.InRange(entry.Multiplier, 0.75f, 1.0f, type.Id + "/" + severity + ": the contribution of the patch");
                        Assert.True(entry.Multiplier >= balanceMin && entry.Multiplier <= balanceMax, type.Id + "/" + severity + ": inside the balance range of the core, so the core never cuts the patch");
                        checkedPatches++;
                    }
                }
            }
            Assert.Equal(b.Types.Count * 3 * 2, checkedPatches, "every type and severity, two CPUs each");
        }

        [Test]
        public void Patch_NeverLowersTheRatingBelowHalf_WhateverTheContentSays()
        {
            FlawRecord record = SecurityMajor();
            record.Data.quote.PatchIpsLoss = 0.9f;   // a content override that the validator would have refused
            ResponseWorld world = WorldOf(record);

            world.Engine.Apply(record, FlawResponse.Patch, false);

            foreach (FakeModifierGateway.Entry entry in world.Gateway.On(FlawChannelNames.MarketRatingCpu))
            {
                Assert.Equal(0.5f, entry.Multiplier, "the book holds the line of the validator once more");
            }
        }

        // ---- a revision instead of the patch (FR-8 item 5) ----

        [Test]
        public void FundStepping_AfterAPatch_StartsTheRevision_AndTakesThePatchAwayWhenItIsDone()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Patch, false);
            world.Host.Spent.Clear();

            world.SetDay(Day + 30);
            bool funded = world.Engine.FundSteppingForMitigated(record);

            Assert.True(funded);
            Assert.Equal(FlawState.Stepping, record.State);
            Assert.Equal(Day + 120, record.Data.steppingEndDay, "90 days from the day it was paid for");
            Assert.False(record.Data.quietStepping, "the flaw was public");
            Assert.Equal(2, world.Gateway.On(FlawChannelNames.MarketRatingCpu).Count, "the patch stays while the revision is made");
            Assert.Equal(0, world.Gateway.RemoveCount);

            world.SetDay(Day + 120);
            cycle.OnDay();

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(0, world.Gateway.On(FlawChannelNames.MarketRatingCpu).Count, "FR-7 item 6: the multiplier is taken away when the revision is finished");
            Assert.Equal(2, world.Gateway.RemoveCount);
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
            Assert.Equal(0f, record.Data.ipsLoss);
            Assert.Equal(0, record.Data.mitigationEndDay);
            Assert.Equal(18000000f, world.Host.Total(FlawSpendKind.Fines), "a public revision owes the residual claims");
        }

        [Test]
        public void FundStepping_OfAQuietPatch_IsAQuietRevision()
        {
            FlawRecord record = SecurityMajor(FlawState.InternalKnown);
            ResponseWorld world = WorldOf(record);
            world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.True(world.Engine.FundSteppingForMitigated(record));

            Assert.True(record.Data.quietStepping, "nobody outside knows: the revision is quiet and may leak");
        }

        [Test]
        public void FundStepping_NeedsAPatchedRecord_AndAQuote()
        {
            FlawRecord waiting = SecurityMajor(FlawState.Public, 1);
            FlawRecord noQuote = SecurityMajor(FlawState.Mitigated, 2);
            noQuote.Data.quote = null;
            ResponseWorld world = WorldOf(waiting, noQuote);

            Assert.False(world.Engine.FundSteppingForMitigated(waiting), "a flaw that waits for its first answer uses the window, not this");
            Assert.False(world.Engine.FundSteppingForMitigated(noQuote));
            Assert.False(world.Engine.FundSteppingForMitigated(null));
            Assert.Equal(2, world.Host.Warnings.Count);
        }

        // ---- ignoring a public flaw (AC-9.1) ----

        [Test]
        public void Ignore_Public_AC91_EndsContractsWithTheChance_AndChargesNoFine()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Critical);
            ResponseWorld world = WorldOf(record);
            world.Host.ContractsEnded = 2;

            bool applied = world.Engine.Apply(record, FlawResponse.Ignore, false);

            Assert.True(applied);
            Assert.Equal(1, world.Host.CancelChances.Count);
            Assert.Near(0.6, world.Host.CancelChances[0], 1e-6, "oemCancel of a Critical flaw: every contract ends with the chance 0.6");
            Assert.Equal(0, world.Host.Spent.Count, "AC-9.1: no fine is charged");
            Assert.Equal(FlawState.Public, record.State, "an ignored public flaw stays public");
            Assert.Equal(FlawResponse.Ignore, record.Response);
            Assert.True(world.Host.Logs.Exists(l => l.Contains("2 OEM contract(s) ended")), "the number of ended contracts is in the log");
        }

        [Test]
        public void Ignore_Public_LowersTheChanceOfSigning_For12Months()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Critical);
            ResponseWorld world = WorldOf(record);

            world.Engine.Apply(record, FlawResponse.Ignore, false);

            List<FakeModifierGateway.Entry> entries = world.Gateway.On(FlawChannelNames.ContractSignChance);
            Assert.Equal(1, entries.Count);
            Assert.Equal(ScopeKind.Company, entries[0].Scope.Kind, "ModifierScope.Company");
            Assert.Equal("player", entries[0].Scope.CompanyKey);
            Assert.Near(0.70, entries[0].Multiplier, 1e-6, "ignore.signChance of a Critical flaw");
            Assert.Equal("dfr.mod.ignore", entries[0].ReasonKey);
            Assert.Equal(FlawDays.AddMonths(Day, 12) - Day, entries[0].DurationDays, "signChanceMonths = 12");
        }

        [Test]
        public void Ignore_Public_CostsReputation_AndPopularity_AndStartsTheDrain()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Critical);
            ResponseWorld world = WorldOf(record);

            world.Engine.Apply(record, FlawResponse.Ignore, false);

            Assert.Equal(-15, world.Host.ReputationSum("dfr.rep.ignore"));
            Assert.Near(0.20, world.Host.PopularityHits[0], 1e-6, "popHit.ignore of Critical");
            Assert.Equal(FlawDays.AddMonths(Day, 12), record.Data.ignoreDrainUntilDay, "drainMonths = 12");
        }

        [Test]
        public void Ignore_Internal_HasNoImmediateConsequence()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown, 1, FlawSeverity.Critical);
            ResponseWorld world = WorldOf(record);

            bool applied = world.Engine.Apply(record, FlawResponse.Ignore, false);

            Assert.True(applied);
            Assert.Equal(FlawState.InternalKnown, record.State);
            Assert.Equal(FlawResponse.Ignore, record.Response);
            Assert.Equal(0, world.Host.Spent.Count);
            Assert.Equal(0, world.Host.Reputation.Count, "FR-9 item 4");
            Assert.Equal(0, world.Host.PopularityHits.Count);
            Assert.Equal(0, world.Host.CancelChances.Count);
            Assert.Equal(0, world.Gateway.AddCount);
            Assert.Equal(0, record.Data.ignoreDrainUntilDay, "no drain for a flaw nobody knows");
        }

        // ---- the months after an ignore ----

        [Test]
        public void IgnoredPublicFlaw_DrainsOnePointAMonth_For12Months()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major);
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world) { Rng_ = FakeRng.Always(0.99f) };
            host.Facts[100] = WiredCycleHost.ExampleFacts();
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Ignore, false);
            world.Host.Reputation.Clear();

            for (int month = 1; month <= 14; month++)
            {
                world.SetDay(FlawDays.AddMonths(Day, month));
                cycle.OnMonth();
            }

            Assert.Equal(12, host.Events.FindAll(e => e.StartsWith("drain")).Count, "AC: up to 12 months of drain");
            Assert.Equal(-12, world.Host.ReputationSum("dfr.rep.drain"), "Major: -8 for the ignore and up to -12 of drain");
            Assert.Equal(0, world.Host.Spent.Count, "no lawsuit with these dice");
        }

        [Test]
        public void IgnoredPublicFlaw_IsSuedAtMostOnce_ForTheQuotedAmount()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major);
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world) { Rng_ = FakeRng.After(0.99f, 0.99f, 0.99f, 0.0f, 0.0f, 0.0f) };
            host.Facts[100] = WiredCycleHost.ExampleFacts();
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Ignore, false);

            for (int month = 1; month <= 8; month++)
            {
                world.SetDay(FlawDays.AddMonths(Day, month));
                cycle.OnMonth();
            }

            Assert.Equal(1, host.Events.FindAll(e => e.StartsWith("suit")).Count, "one lawsuit per record");
            Assert.Equal(1, world.Host.Spent.Count);
            Assert.Equal(FlawSpendKind.Fines, world.Host.Spent[0].Kind);
            Assert.Equal(24000000f, world.Host.Spent[0].Amount, "0.03 * 2 000 000 * 400");
            Assert.Equal(24000000f, record.Data.spentTotal);
        }

        // ---- the leak of an ignored internal flaw (AC-9.2) ----

        [Test]
        public void Leak_OfAnIgnoredInternalFlaw_AppliesTheConsequences_WithTheCoverUpMultiplier()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown, 1, FlawSeverity.Major);
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world);
            host.Facts[100] = WiredCycleHost.ExampleFacts();
            var cycle = new FlawCycle(world.Store, host);
            world.Host.ContractsEnded = 1;
            world.Engine.Apply(record, FlawResponse.Ignore, false);

            // Four months later the leak comes (the dice of the monthly roll).
            host.Rng_ = FakeRng.Always(0.0f);
            world.SetDay(FlawDays.AddMonths(Day, 4));
            cycle.OnMonth();

            Assert.Equal(FlawState.Public, record.State, "AC-9.2");
            Assert.True(record.Data.coverUp);
            Assert.Equal(DiscoverySource.Leak, record.Source);
            Assert.Equal(FlawResponse.None, record.Response, "the owner chooses again");
            Assert.Equal(FlawDecisionKind.Leaked, FlawDecisionPlanner.KindOf(record), "the window without the way of ignoring");
            Assert.True(FlawResponseEngine.AwaitsDecision(record));

            Assert.Equal(-26, world.Host.ReputationSum("dfr.rep.leak"), "(-5 + -8) * 2");
            Assert.Equal(2, world.Host.PopularityHits.Count, "the ignore and the flaw becoming public");
            Assert.Near(0.12, world.Host.PopularityHits[0], 1e-6, "popHit.ignore of Major");
            Assert.Near(0.04, world.Host.PopularityHits[1], 1e-6, "popHit.public of Major");
            Assert.Near(0.3, world.Host.CancelChances[0], 1e-6, "the OEM contracts are asked once more");
            Assert.Equal(0, world.Host.Total(FlawSpendKind.Fines), "an ignored flaw that leaks owes no recall");

            List<FakeModifierGateway.Entry> signs = world.Gateway.On(FlawChannelNames.ContractSignChance);
            Assert.Equal(1, signs.Count, "the chance of signing is lowered");
            Assert.Equal("dfr.mod.ignore", signs[0].ReasonKey);
        }

        [Test]
        public void Leak_MakesTheQuoteAnewFromTheFactsOfToday_AndKeepsTheUnitsOfTheDiscovery()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown, 1, FlawSeverity.Major);
            record.Data.unitsAtDiscovery = 2000000L;
            ResponseWorld world = WorldOf(record);
            var host = new WiredCycleHost(world);
            FlawFacts facts = WiredCycleHost.ExampleFacts();
            host.Facts[100] = facts;
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Ignore, false);

            facts.Units = 3000000L;
            host.Rng_ = FakeRng.Always(0.0f);
            world.SetDay(FlawDays.AddMonths(Day, 2));
            cycle.OnMonth();

            Assert.Equal(3000000L, record.Quote.AffectedUnits, "the window after the leak shows what the flaw costs now");
            Assert.Equal(2000000L, record.Data.unitsAtDiscovery, "the discovery stays as it was");
            Assert.Equal(-1, record.Quote.ReputationStepping, "the flaw is public now: a revision costs reputation");
        }

        // ---- the guards of the engine ----

        [Test]
        public void Patch_WhenTheCoreRefusesTheEntries_TheAnswerStands_AndTheFailureIsWarnedAbout()
        {
            FlawRecord record = SecurityMajor();
            ResponseWorld world = WorldOf(record);
            world.Gateway.RefuseAdd = true;

            bool applied = world.Engine.Apply(record, FlawResponse.Patch, false);

            Assert.True(applied, "the money is paid and the record is mitigated");
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
            Assert.Equal(2, world.ChannelWarnings.Count, "one warning per refused entry");
        }

        [Test]
        public void Engine_RequiresItsParts()
        {
            ResponseWorld world = WorldOf();
            Assert.Throws<ArgumentNullException>(() => new FlawResponseEngine(null, world.Host));
            Assert.Throws<ArgumentNullException>(() => new FlawResponseEngine(world.Book, null));
        }
    }
}
