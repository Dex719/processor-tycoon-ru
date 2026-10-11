using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-014 (FR-5 item 5, FR-8, FR-9, NFR-2, NFR-8; AC-8.2, AC-8.3): the entries of <c>ModModifiers</c> the feature makes - <c>market.rating.cpu</c> with the scope of a
    /// CPU, <c>contract.signChance</c> with the scope of a company, a term in days, a reason that is a key - their ids in the record, their removal by id, the check against
    /// the core after a load, and the rule that switching the feature off removes nothing and switching it on makes nothing again.
    /// </summary>
    [Covers(typeof(FlawChannelBook), typeof(FlawChannelNames), typeof(FlawLiveEntry), typeof(IFlawModifierGateway), typeof(FlawChannelReport))]
    public class FlawChannelsTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        private sealed class Env
        {
            public readonly FlawStore Store;
            public readonly FakeModifierGateway Gateway = new FakeModifierGateway();
            public readonly List<string> Warnings = new List<string>();
            public readonly FlawChannelBook Book;

            public Env(params FlawRecord[] records)
            {
                Store = FlawCycleKit.Store(records);
                Gateway.Today = Day;
                Gateway.CpuNames[100] = "Nova 3";
                Gateway.CpuNames[101] = "Nova 3 Lite";
                Book = new FlawChannelBook(Store, Gateway, Warnings.Add);
            }
        }

        private static FlawRecord Waiting(int id = 1, string company = "player")
        {
            return ResponseWorld.Known(FlawState.Public, id, company: company);
        }

        // ---- AddCpuRating ----

        [Test]
        public void AddCpuRating_UsesTheScopeOfTheCpu_TheTermInDays_AndAReasonKey()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            string id = env.Book.AddCpuRating(100, 0.88f, 548, record.Id);

            Assert.NotNull(id);
            Assert.Equal(1, env.Gateway.Entries.Count);
            FakeModifierGateway.Entry entry = env.Gateway.Entries[id];
            Assert.Equal("market.rating.cpu", entry.Channel);
            Assert.Equal(ScopeKind.Cpu, entry.Scope.Kind, "ModifierScope.Cpu");
            Assert.Equal(100, entry.Scope.CpuSaveId);
            Assert.Equal(Segment.Any, entry.Scope.Segment, "the segment is Any");
            Assert.Equal(0.88f, entry.Multiplier);
            Assert.Equal(548, entry.DurationDays, "the term in days");
            Assert.Equal("dfr.mod.patch", entry.ReasonKey);
            Assert.Equal("Nova 3", entry.ReasonArgs[0], "the name of the CPU is the argument of the reason");
        }

        [Test]
        public void AddCpuRating_KeepsTheIdInTheRecord()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            string first = env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            string second = env.Book.AddCpuRating(101, 0.9f, 100, record.Id);

            Assert.Equal(2, record.Data.modifierEntryIds.Count);
            Assert.Equal(first, record.Data.modifierEntryIds[0]);
            Assert.Equal(second, record.Data.modifierEntryIds[1]);
            Assert.True(first.StartsWith("m-", StringComparison.Ordinal), "an id of the core");
        }

        [Test]
        public void AddCpuRating_KeepsTheMultiplierBetweenHalfAndOne()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            Assert.Equal(0.5f, env.Gateway.Entries[env.Book.AddCpuRating(100, 0.2f, 10, record.Id)].Multiplier, "never below the line of the validator");
            Assert.Equal(1.0f, env.Gateway.Entries[env.Book.AddCpuRating(100, 1.7f, 10, record.Id)].Multiplier, "a patch does not raise a rating");
            Assert.Equal(1.0f, env.Gateway.Entries[env.Book.AddCpuRating(100, float.NaN, 10, record.Id)].Multiplier, "a broken number is no effect");
            Assert.Equal(0.5f, env.Gateway.Entries[env.Book.AddCpuRating(100, 0.5f, 10, record.Id)].Multiplier, "the value the validator accepts as the lowest passes unchanged");
        }

        [Test]
        public void AddCpuRating_NeverMakesAnEndlessEntry()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            string id = env.Book.AddCpuRating(100, 0.9f, 0, record.Id);

            Assert.Equal(1, env.Gateway.Entries[id].DurationDays, "0 days would mean an entry without an end");
            Assert.Equal(1, env.Gateway.Entries[env.Book.AddCpuRating(100, 0.9f, -5, record.Id)].DurationDays);
        }

        [Test]
        public void AddCpuRating_WithoutARecord_OrWhenTheCoreRefuses_ReturnsNull_WithAWarning()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            Assert.Null(env.Book.AddCpuRating(100, 0.9f, 10, 999), "no record #999");
            Assert.Equal(0, env.Gateway.AddCount);
            Assert.Equal(1, env.Warnings.Count);

            env.Gateway.RefuseAdd = true;
            Assert.Null(env.Book.AddCpuRating(100, 0.9f, 10, record.Id));
            Assert.Equal(0, record.Data.modifierEntryIds.Count, "nothing is kept for an entry that does not exist");
            Assert.Equal(2, env.Warnings.Count);
        }

        // ---- AddSignChance ----

        [Test]
        public void AddSignChance_UsesTheScopeOfTheCompany_AndTheReasonItIsGiven()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            string pending = env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            string ignore = env.Book.AddSignChance("player", 0.85f, 365, record.Id, FlawChannelNames.ReasonIgnore);

            FakeModifierGateway.Entry p = env.Gateway.Entries[pending];
            Assert.Equal("contract.signChance", p.Channel);
            Assert.Equal(ScopeKind.Company, p.Scope.Kind, "ModifierScope.Company");
            Assert.Equal("player", p.Scope.CompanyKey);
            Assert.Equal(0.8f, p.Multiplier);
            Assert.Equal(44, p.DurationDays, "timerDays + pendingGraceDays = 30 + 14");
            Assert.Equal("dfr.mod.pending", p.ReasonKey);

            FakeModifierGateway.Entry i = env.Gateway.Entries[ignore];
            Assert.Equal(0.85f, i.Multiplier);
            Assert.Equal(365, i.DurationDays);
            Assert.Equal("dfr.mod.ignore", i.ReasonKey);

            Assert.Equal(2, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void AddSignChance_KeepsTheMultiplierBetweenHalfAndOne_AndRefusesNoCompany()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);

            Assert.Equal(0.5f, env.Gateway.Entries[env.Book.AddSignChance("player", 0.1f, 10, record.Id, FlawChannelNames.ReasonIgnore)].Multiplier, "NFR-8");
            Assert.Equal(1.0f, env.Gateway.Entries[env.Book.AddSignChance("player", 3f, 10, record.Id, FlawChannelNames.ReasonIgnore)].Multiplier);
            Assert.Null(env.Book.AddSignChance(null, 0.8f, 10, record.Id, FlawChannelNames.ReasonPending));
            Assert.Null(env.Book.AddSignChance("", 0.8f, 10, record.Id, FlawChannelNames.ReasonPending));
            Assert.Null(env.Book.AddSignChance("player", 0.8f, 10, 999, FlawChannelNames.ReasonPending), "no record #999");
        }

        [Test]
        public void TheWorstCase_OfThePendingAndTheIgnoreTogether_IsFiftySixPercent()
        {
            // design.md "Максимальный вклад фичи": 0.80 * 0.70 = 0.56 on contract.signChance; the channel has a fuse of [0, 5] and no balance range.
            FlawBalance b = FlawBalance.Defaults();
            float worst = b.Responses.PendingSignChanceMultiplier * b.Responses.Ignore.SignChance.Min();
            Assert.Near(0.56, worst, 1e-6);
            Assert.True(worst >= FlawChannelNames.MinMultiplier);
        }

        // ---- removing ----

        [Test]
        public void Remove_TakesTheEntryAwayFromTheCore_AndFromTheRecord()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            string id = env.Book.AddCpuRating(100, 0.9f, 100, record.Id);

            bool removed = env.Book.Remove(id);

            Assert.True(removed);
            Assert.Equal(0, env.Gateway.Entries.Count);
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
            Assert.False(env.Book.Remove(id), "a second removal finds nothing");
            Assert.False(env.Book.Remove(null));
            Assert.False(env.Book.Remove(""));
        }

        [Test]
        public void RemoveAllOfRecord_TakesAwayEveryEntryOfTheRecord_AndNoOthers()
        {
            FlawRecord mine = Waiting(1);
            FlawRecord other = Waiting(2);
            other.Data.baseCpuId = 200;
            other.Data.cpuIds = new List<int> { 200 };
            var env = new Env(mine, other);
            env.Book.AddCpuRating(100, 0.9f, 100, 1);
            env.Book.AddCpuRating(101, 0.9f, 100, 1);
            env.Book.AddSignChance("player", 0.8f, 44, 1, FlawChannelNames.ReasonPending);
            string kept = env.Book.AddCpuRating(200, 0.9f, 100, 2);

            int removed = env.Book.RemoveAllOfRecord(mine);

            Assert.Equal(3, removed);
            Assert.Equal(1, env.Gateway.Entries.Count);
            Assert.True(env.Gateway.Entries.ContainsKey(kept), "the entry of the other record stays");
            Assert.Equal(0, mine.Data.modifierEntryIds.Count);
            Assert.Equal(1, other.Data.modifierEntryIds.Count);
        }

        [Test]
        public void RemoveCompanyEntries_KeepsThePatchOfTheCpu()
        {
            // FR-19: the CPU changes its owner; the consequences for the old company go, the patch of the CPU stays with it.
            FlawRecord record = Waiting();
            var env = new Env(record);
            string rating = env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            env.Book.AddSignChance("player", 0.85f, 365, record.Id, FlawChannelNames.ReasonIgnore);

            int removed = env.Book.RemoveCompanyEntries(record);

            Assert.Equal(2, removed);
            Assert.Equal(1, env.Gateway.Entries.Count);
            Assert.True(env.Gateway.Entries.ContainsKey(rating));
            Assert.Equal(1, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void RemovePending_TakesAwayTheWaitingEntryOnly()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            string rating = env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            env.Book.AddSignChance("player", 0.85f, 365, record.Id, FlawChannelNames.ReasonIgnore);
            env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            Assert.True(env.Book.HasPending(record));

            int removed = env.Book.RemovePending(record);

            Assert.Equal(1, removed);
            Assert.False(env.Book.HasPending(record));
            Assert.Equal(2, env.Gateway.Entries.Count);
            Assert.True(env.Gateway.Entries.ContainsKey(rating));
            Assert.Equal(0, env.Gateway.CountReason(FlawChannelNames.ReasonPending));
        }

        [Test]
        public void RemoveRatings_TakesAwayThePatchOnly()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            env.Book.AddCpuRating(101, 0.9f, 100, record.Id);
            env.Book.AddSignChance("player", 0.85f, 365, record.Id, FlawChannelNames.ReasonIgnore);

            Assert.Equal(2, env.Book.RemoveRatings(record));

            Assert.Equal(1, env.Gateway.Entries.Count);
            Assert.Equal(1, env.Gateway.CountReason(FlawChannelNames.ReasonIgnore));
        }

        [Test]
        public void Removal_WorksAfterALoad_WhereTheBookDoesNotKnowTheReasonsOfTheIds()
        {
            // A new book over the same saved data stands for the book after a load: the ids are in the record, the reasons are only in the core.
            FlawRecord record = Waiting();
            var env = new Env(record);
            string rating = env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            var reloaded = new FlawChannelBook(env.Store, env.Gateway, env.Warnings.Add);

            Assert.True(reloaded.HasPending(record), "it learns the reasons from the core");
            Assert.Equal(1, reloaded.RemovePending(record));

            Assert.Equal(1, env.Gateway.Entries.Count);
            Assert.True(env.Gateway.Entries.ContainsKey(rating));
        }

        [Test]
        public void Removal_SurvivesACoreThatThrows()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            env.Book.AddCpuRating(100, 0.9f, 100, record.Id);
            env.Gateway.ThrowOnRemove = true;

            int removed = env.Book.RemoveAllOfRecord(record);

            Assert.Equal(1, removed, "the id leaves the record even if the core could not remove it");
            Assert.Equal(1, env.Warnings.Count);
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
        }

        // ---- Reconcile (AC-8.2) ----

        [Test]
        public void Reconcile_AC82_APatchThatRunsIsKept_WithTheRemainingTerm()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "security", year: 2018);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            world.Engine.Apply(record, FlawResponse.Patch, false);
            int end = record.Data.mitigationEndDay;
            string[] ids = record.Data.modifierEntryIds.ToArray();

            // Six months later the save is loaded.
            int loadDay = FlawDays.AddMonths(Day, 6);
            world.SetDay(loadDay);
            var reloaded = new FlawChannelBook(world.Store, world.Gateway, world.ChannelWarnings.Add);
            FlawChannelReport report = reloaded.Reconcile(loadDay, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(2, report.Alive);
            Assert.Equal(0, report.Lost);
            Assert.Equal(0, report.Ended);
            Assert.Equal(0, world.ChannelWarnings.Count, "nothing to warn about");
            Assert.Equal(string.Join(",", ids), string.Join(",", record.Data.modifierEntryIds), "the same ids");
            foreach (string id in ids)
            {
                Assert.Equal(end, world.Gateway.Entries[id].EndDay, "the entry kept its end date");
            }
            Assert.Equal(end - loadDay, world.Gateway.Entries[ids[0]].EndDay - loadDay, "the remaining term of the entry is the one the record shows");
            Assert.True(end - loadDay >= 365 && end - loadDay <= 366 + 1, "about 12 months are left of the 18");
        }

        [Test]
        public void Reconcile_ALostPatch_IsWarnedAbout_AndTheIdsAreDropped()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "security", year: 2018);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            world.Engine.Apply(record, FlawResponse.Patch, false);
            world.Gateway.Entries.Clear();   // the side file lost the entries

            FlawChannelReport report = world.Book.Reconcile(Day + 30, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(1, report.Lost);
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].Contains("record #1") && report.Warnings[0].Contains("lost"), report.Warnings[0]);
            Assert.Equal(1, world.ChannelWarnings.Count, "the warning goes to the log");
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void Reconcile_EntriesThatEndedByTheirTerm_AreForgottenWithoutAWarning()
        {
            FlawRecord record = Waiting();
            record.Data.response = (int)FlawResponse.Ignore;
            var env = new Env(record);
            env.Book.AddSignChance("player", 0.85f, 365, record.Id, FlawChannelNames.ReasonIgnore);
            env.Gateway.Today = Day + 400;   // a year has passed

            FlawChannelReport report = env.Book.Reconcile(Day + 400, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(1, report.Ended);
            Assert.Equal(0, report.Lost);
            Assert.Equal(0, env.Warnings.Count);
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void Reconcile_ThePatchOfARecordWithAnEndedTerm_IsNotLost()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Mitigated, 1, FlawSeverity.Major, "security", year: 2018);
            record.Data.ipsLoss = 0.12f;
            record.Data.mitigationEndDay = Day - 1;
            record.Data.modifierEntryIds = new List<string> { "m-000005" };
            var env = new Env(record);

            FlawChannelReport report = env.Book.Reconcile(Day, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(0, report.Lost, "the term is over: the entries are gone as they should be");
            Assert.Equal(1, report.Ended);
            Assert.Equal(0, env.Warnings.Count);
        }

        [Test]
        public void Reconcile_AWaitingEntry_OfARecordThatNoLongerWaits_IsRemoved()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            string pending = env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);
            FlawStateMachine.Recall(record, Day);   // the answer was given; the removal of the entry was lost (a crash)

            FlawChannelReport report = env.Book.Reconcile(Day, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(1, report.StaleRemoved);
            Assert.False(env.Gateway.Entries.ContainsKey(pending));
            Assert.Equal(0, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void Reconcile_AWaitingEntry_OfARecordThatStillWaits_Stays()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            string pending = env.Book.AddSignChance("player", 0.8f, 44, record.Id, FlawChannelNames.ReasonPending);

            FlawChannelReport report = env.Book.Reconcile(Day + 10, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(0, report.StaleRemoved);
            Assert.True(env.Gateway.Entries.ContainsKey(pending));
            Assert.Equal(1, record.Data.modifierEntryIds.Count);
        }

        [Test]
        public void Reconcile_AWaitingEntryThatNoRecordHolds_IsRemoved_ButTheOtherEntriesOfTheFeatureAreNot()
        {
            FlawRecord record = Waiting();
            var env = new Env(record);
            string orphan = env.Gateway.Add("contract.signChance", ModifierScope.Company("player"), 0.8f, 44, FlawChannelNames.ReasonPending, new string[0]);
            string ignore = env.Gateway.Add("contract.signChance", ModifierScope.Company("player"), 0.85f, 365, FlawChannelNames.ReasonIgnore, new string[0]);

            FlawChannelReport report = env.Book.Reconcile(Day, FlawResponseEngine.AwaitsDecision);

            Assert.Equal(1, report.StaleRemoved);
            Assert.False(env.Gateway.Entries.ContainsKey(orphan));
            Assert.True(env.Gateway.Entries.ContainsKey(ignore), "an entry of another reason is not ours to judge");
        }

        // ---- NFR-2, D13, AC-8.3: switching the feature off and on ----

        [Test]
        public void SwitchingOffAndOn_RemovesNothing_AndMakesNothingAgain_AC83()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "security", year: 2018);
            var world = new ResponseWorld(record);
            world.SetDay(Day);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            world.Engine.Apply(record, FlawResponse.Patch, false);
            string[] ids = record.Data.modifierEntryIds.ToArray();
            int[] endDays = ids.Select(id => world.Gateway.Entries[id].EndDay).ToArray();
            int added = world.Gateway.AddCount;
            world.Store.LastActiveDay = Day;

            // OnDisabled remembers the day and does nothing else. Ten days pass, the core keeps the entries and does not apply them (the fake just lets them stand).
            int on = Day + 10;
            world.SetDay(on);

            // OnEnabled: the pause is counted for the revisions; the entries are not made again, the book is not asked to remove anything.
            cycle.OnResume();

            Assert.Equal(0, world.Gateway.RemoveCount, "nothing was removed while the feature was off");
            Assert.Equal(added, world.Gateway.AddCount, "no entry was made again");
            Assert.Equal(string.Join(",", ids), string.Join(",", record.Data.modifierEntryIds), "the same ids");
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.Equal(endDays[i], world.Gateway.Entries[ids[i]].EndDay, "the end date is the old one: the term of a patch is the calendar's");
            }
            Assert.Equal(FlawDays.AddMonths(Day, 18), record.Data.mitigationEndDay, "FR-7 item 7: the term of a patch does not move with the pause");
        }

        [Test]
        public void FeatureSources_NeverCallRemoveAll_AndOnEnabledMakesNoEntry()
        {
            string src = SourceScan.SrcDirOrFail();
            string folder = Path.Combine(src, "Features", "DesignFlawRecalls");
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(Regex.IsMatch(code, @"ModModifiers\s*\.\s*RemoveAll"), "no call of ModModifiers.RemoveAll in " + Path.GetFileName(path) + " (D13)");
            }

            foreach (string name in new[] { "FlawChannels.cs", Path.Combine("Logic", "FlawChannelBook.cs") })
            {
                string channelCode = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, name)), true);
                Assert.False(channelCode.Contains("RemoveAll("), "the channel code does not even have a RemoveAll to call (" + name + ")");
            }

            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "DesignFlawRecallsFeature.cs")), true);
            int enabled = feature.IndexOf("public void OnEnabled()", StringComparison.Ordinal);
            int disabled = feature.IndexOf("public void OnDisabled()", StringComparison.Ordinal);
            Assert.True(enabled >= 0 && disabled > enabled);
            string onEnabled = feature.Substring(enabled, disabled - enabled);
            Assert.False(onEnabled.Contains("AddCpuRating") || onEnabled.Contains("AddSignChance") || onEnabled.Contains("Channels."), "OnEnabled makes no entries");
        }

        [Test]
        public void TheGateway_OffersNoRemoveAll()
        {
            Assert.Null(typeof(IFlawModifierGateway).GetMethod("RemoveAll"), "the gateway has no way to remove everything");
            string[] names = typeof(IFlawModifierGateway).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal("Add,CpuName,LiveEntries,Remove", string.Join(",", names));
        }

        // ---- the names ----

        [Test]
        public void ChannelNames_AreChannelsOfTheCore_WithTheScopesTheFeatureNeeds()
        {
            JsonObject core = CoreContentTests.LoadCore();
            JsonObject rating = null, sign = null;
            foreach (JsonNode node in core.GetArray("channels"))
            {
                var channel = node as JsonObject;
                if (channel == null) continue;
                if (channel.GetString("name") == FlawChannelNames.MarketRatingCpu) rating = channel;
                if (channel.GetString("name") == FlawChannelNames.ContractSignChance) sign = channel;
            }

            Assert.NotNull(rating, "market.rating.cpu is declared by the core");
            Assert.True(rating.GetBool("perCpu"), "the scope of a CPU is allowed on market.rating.cpu");
            Assert.NotNull(sign, "contract.signChance is declared by the core");
            Assert.True(sign.GetBool("perCompany"), "the scope of a company is allowed on contract.signChance");
        }

        [Test]
        public void ChannelNames_AreTheOnesOfModChannels()
        {
            string modChannels = SourceScan.Strip(FlawTestKit.ReadSource("Core/Modifiers/ModChannels.cs"), false);
            Assert.True(modChannels.Contains("MarketRatingCpu = \"" + FlawChannelNames.MarketRatingCpu + "\""));
            Assert.True(modChannels.Contains("ContractSignChance = \"" + FlawChannelNames.ContractSignChance + "\""));
        }

        [Test]
        public void Reasons_AreKeysOfTheFeature_InBothTables()
        {
            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            foreach (string key in new[] { FlawChannelNames.ReasonPatch, FlawChannelNames.ReasonPending, FlawChannelNames.ReasonIgnore })
            {
                Assert.True(key.StartsWith("dfr.mod.", StringComparison.Ordinal), key);
                Assert.True(ru.ContainsKey(key), key + " in ru");
                Assert.True(en.ContainsKey(key), key + " in en");
            }
            Assert.True(ru.GetString("dfr.mod.patch").Contains("{0}") && en.GetString("dfr.mod.patch").Contains("{0}"), "the reason of the patch names the CPU");
        }

        [Test]
        public void Book_RequiresItsParts()
        {
            var env = new Env();
            Assert.Throws<ArgumentNullException>(() => new FlawChannelBook(null, env.Gateway, null));
            Assert.Throws<ArgumentNullException>(() => new FlawChannelBook(env.Store, null, null));
            Assert.NotNull(new FlawChannelBook(env.Store, env.Gateway, null), "the warning sink is optional");
        }
    }
}
