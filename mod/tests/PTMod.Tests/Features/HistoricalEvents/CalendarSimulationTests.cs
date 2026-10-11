using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The simulation of the calendar (TSK-026; FR-2, FR-3, NFR-1, NFR-6; design.md, "CalendarSimulationTests"): whole games of 1975-2030 on synthetic worlds
    /// (<see cref="SyntheticWorld"/>), played through the real scheduler, role resolver, conditions, dispatcher and share cache with the calendar and the pack that ship.
    /// <list type="bullet">
    /// <item>100 ordinary worlds: 27 to 35 entries fire by default, 30 to 38 with the category <c>disaster</c> on; the player sees 7 to 13 decisions in an average world
    /// (8 to 14 with <c>disaster</c>); two <c>major</c> entries are never closer than 45 days.</item>
    /// <item>A world where the player dominates (more than 0.6 of the market from 1995 on): he is the target of at least two of the three antitrust cases. A world where
    /// the player is weak (never 5 percent): of none.</item>
    /// <item>The same seed gives the same game, event for event.</item>
    /// </list>
    /// The counts are those of a game without the line of GPUs (<c>gpu-product-line</c>): the two crypto booms need it, and with it on they add two entries (a separate test).
    /// The worlds are drawn from <see cref="WorldProfile"/>; what they assume is written there.
    /// </summary>
    [Covers(typeof(SchedulerCore), typeof(DispatcherCore), typeof(RoleResolver), typeof(MarketShareCache), typeof(HistPredicates))]
    public class CalendarSimulationTests
    {
        private const int Worlds = 100;

        private static readonly string[] DisasterEntries = { "hist.1999.jiji", "hist.2011.tohoku", "hist.2020.covid" };
        private static readonly string[] CryptoEntries = { "hist.2017.cryptoboom", "hist.2021.cryptoboom" };

        // ---- what a run leaves behind ----

        private sealed class Summary
        {
            public WorldProfile Profile;
            public string Digest;
            public int Fired;
            public int Decisions;
            public int Errors;
            public int Warnings;
            public int DoubleWindows;
            public List<int> MajorDays;
            public List<string> AntitrustAgainstPlayer;
            public Dictionary<string, EntryStatus> Status;
            public HashSet<string> FiredIds;

            public string Name
            {
                get { return Profile.Describe(); }
            }
        }

        private static Summary Summarize(CalendarRun run)
        {
            var status = new Dictionary<string, EntryStatus>(StringComparer.Ordinal);
            foreach (CalendarEntry entry in ShippedContent.Entries) status[entry.Id] = run.StatusOf(entry.Id);

            // The player is never asked two things about one entry on one day (the follower and the aggressor, the partner and the leader).
            int doubles = run.Fires
                .Where(f => f.Company == CompanyKey.Player && ShippedContent.DecisionEvents.Contains(f.EventId))
                .GroupBy(f => f.Day + "|" + f.EntryId)
                .Count(g => g.Count() > 1);

            return new Summary
            {
                Profile = run.Profile,
                Digest = run.Digest(),
                Fired = run.FiredEntries.Count,
                Decisions = run.PlayerDecisions,
                Errors = run.Errors.Count,
                Warnings = run.Warnings.Count,
                DoubleWindows = doubles,
                MajorDays = run.MajorDays.ToList(),
                AntitrustAgainstPlayer = run.AntitrustAgainstPlayer.ToList(),
                Status = status,
                FiredIds = new HashSet<string>(run.FiredEntries.Select(e => e.EntryId), StringComparer.Ordinal),
            };
        }

        private static Summary Play(WorldKind kind, uint seed, bool disaster, Action<WorldProfile> tweak = null)
        {
            WorldProfile profile = WorldProfile.Draw(seed, kind);
            if (tweak != null) tweak(profile);
            return Summarize(new CalendarRun(profile, disaster).Execute());
        }

        private static List<Summary> _byDefault;
        private static List<Summary> _withDisaster;

        /// <summary>The hundred ordinary worlds (seeds 1 to 100), played once with the default categories.</summary>
        private static List<Summary> ByDefault()
        {
            return _byDefault ?? (_byDefault = Hundred(false));
        }

        /// <summary>The same hundred worlds with the category <c>disaster</c> on.</summary>
        private static List<Summary> WithDisaster()
        {
            return _withDisaster ?? (_withDisaster = Hundred(true));
        }

        private static List<Summary> Hundred(bool disaster)
        {
            var all = new List<Summary>();
            for (uint seed = 1; seed <= Worlds; seed++) all.Add(Play(WorldKind.Typical, seed, disaster));
            return all;
        }

        private static string Distribution(IEnumerable<int> values)
        {
            List<int> list = values.ToList();
            return "min " + list.Min() + ", max " + list.Max() + ", mean " + list.Average().ToString("0.0") + " (" + string.Join(" ", list.GroupBy(v => v).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count())) + ")";
        }

        // ---- the counts ----

        [Test]
        public void HundredWorlds_FireBetween27And35Entries_ByDefault()
        {
            List<Summary> worlds = ByDefault();
            Assert.Equal(Worlds, worlds.Count);

            var outside = worlds.Where(w => w.Fired < 27 || w.Fired > 35).Select(w => w.Name + " fired " + w.Fired).ToList();
            Assert.True(outside.Count == 0, "27 to 35 entries by default, but: " + string.Join(" | ", outside) + " -- " + Distribution(worlds.Select(w => w.Fired)));
        }

        [Test]
        public void HundredWorlds_FireBetween30And38Entries_WithDisaster()
        {
            List<Summary> worlds = WithDisaster();

            var outside = worlds.Where(w => w.Fired < 30 || w.Fired > 38).Select(w => w.Name + " fired " + w.Fired).ToList();
            Assert.True(outside.Count == 0, "30 to 38 entries with disaster, but: " + string.Join(" | ", outside) + " -- " + Distribution(worlds.Select(w => w.Fired)));
        }

        [Test]
        public void TheHundredWorlds_AreNotAllTheSame_TheCountsSpreadOverTheRange()
        {
            // A guard against a generator that makes one world a hundred times: the counts move, and some worlds have antitrust cases and some have none.
            List<Summary> worlds = ByDefault();

            Assert.True(worlds.Select(w => w.Fired).Distinct().Count() >= 5, "the counts: " + Distribution(worlds.Select(w => w.Fired)));
            Assert.True(worlds.Select(w => w.Digest).Distinct().Count() >= 90, "at least 90 different games out of 100");
            Assert.True(worlds.Count(w => w.FiredIds.Contains("hist.2009.eufine")) >= 10, "the dominating giants of the 2000s exist");
            Assert.True(worlds.Count(w => !w.FiredIds.Contains("hist.2009.eufine")) >= 40, "and so do the games without one");
        }

        [Test]
        public void TheDisasterCategory_AddsExactlyTheDisasterEntries_AndNothingElseChanges()
        {
            List<Summary> plain = ByDefault();
            List<Summary> with = WithDisaster();

            for (int i = 0; i < Worlds; i++)
            {
                Summary a = plain[i];
                Summary b = with[i];

                foreach (string id in DisasterEntries)
                {
                    Assert.Equal(EntryStatus.Disabled, a.Status[id], a.Name + ": " + id + " is off by default (AC-17.1)");
                    Assert.NotEqual(EntryStatus.Disabled, b.Status[id], b.Name + ": " + id + " is judged when the category is on");
                }
                Assert.True(b.FiredIds.Contains("hist.2020.covid"), b.Name + ": the pandemic needs no company and always happens");
                Assert.True(a.FiredIds.IsSubsetOf(b.FiredIds), a.Name + ": nothing that fired by default is lost: " + string.Join(", ", a.FiredIds.Except(b.FiredIds)));

                var added = b.FiredIds.Except(a.FiredIds).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                Assert.True(added.All(id => DisasterEntries.Contains(id)), a.Name + ": only disaster entries are added, got " + string.Join(", ", added));
            }
        }

        [Test]
        public void TheCryptoBooms_NeedTheLineOfGpus_AndOnlyThen_AddTwoEntries()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                Summary without = Play(WorldKind.Typical, seed, false);
                Summary with = Play(WorldKind.Typical, seed, false, p => p.GpuLineActive = true);

                foreach (string id in CryptoEntries)
                {
                    Assert.Equal(EntryStatus.Expired, without.Status[id], without.Name + ": " + id + " has no line of GPUs to boom (fallback skip, AC-10.3)");
                    Assert.Equal(EntryStatus.Fired, with.Status[id], with.Name + ": " + id + " fires with the line of GPUs");
                }
                Assert.Equal(without.Fired + 2, with.Fired, with.Name + ": exactly the two booms are added");
                Assert.Equal(0, without.Errors + with.Errors, "no error either way");
            }
        }

        // ---- the decisions of the player ----

        [Test]
        public void TheAverageWorld_GivesThePlayerSevenToThirteenDecisions_EightToFourteenWithDisaster()
        {
            Summary plain = Play(WorldKind.Average, 1, false);
            Summary with = Play(WorldKind.Average, 1, true);

            Assert.InRange(plain.Decisions, 7, 13, plain.Name + ": decisions by default");
            Assert.InRange(with.Decisions, 8, 14, with.Name + ": decisions with disaster");
        }

        [Test]
        public void AcrossTheHundredWorlds_ThePlayerSeesAboutNineDecisions()
        {
            List<Summary> plain = ByDefault();
            List<Summary> with = WithDisaster();

            Assert.InRange(plain.Average(w => w.Decisions), 7.0, 13.0, "the mean by default: " + Distribution(plain.Select(w => w.Decisions)));
            Assert.InRange(with.Average(w => w.Decisions), 8.0, 14.0, "the mean with disaster: " + Distribution(with.Select(w => w.Decisions)));

            // A world with a fabless player who has no OEM contract has fewer, one with every era's price war to join has more: never none, never a flood.
            Assert.True(plain.All(w => w.Decisions >= 5 && w.Decisions <= 14), "per world: " + Distribution(plain.Select(w => w.Decisions)));
        }

        [Test]
        public void ThePlayer_IsNeverAskedTwoThingsAboutOneEntryOnOneDay()
        {
            var worlds = new List<Summary>(ByDefault());
            for (uint seed = 1; seed <= 5; seed++) worlds.Add(Play(WorldKind.Dominating, seed, true));

            var doubled = worlds.Where(w => w.DoubleWindows > 0).Select(w => w.Name).ToList();
            Assert.True(doubled.Count == 0, "a second window for the same entry on the same day (the follower of a war the player starts, the partner of a brand he leads): " + string.Join(" | ", doubled));
        }

        [Test]
        public void ThePlayerWhoLeads_GetsTheLeadersEvent_AndTheEntriesStayPossible()
        {
            // The follower of a price war and the partner of a brand programme are fires at "player". A player who is the leader of the entry used to make the entry
            // impossible (the role "player" was taken); now it fires, and he gets the leader's window only.
            foreach (uint seed in new uint[] { 1, 2, 3 })
            {
                var run = new CalendarRun(WorldProfile.Draw(seed, WorldKind.Dominating), false).Execute();

                Assert.Equal(EntryStatus.Fired, run.StatusOf("hist.1991.brandprogram"), "seed " + seed + ": the brand programme of a player who leads");
                Assert.True(run.Fires.Any(f => f.EventId == "hist.decide.brand.leader" && f.Company == CompanyKey.Player), "seed " + seed + ": he is asked to launch it");
                Assert.False(run.Fires.Any(f => f.EventId == "hist.decide.brand.coop" && f.Company == CompanyKey.Player), "seed " + seed + ": and not to join it as a partner");

                foreach (string id in new[] { "hist.1998.sub1000", "hist.2006.core2war", "hist.2017.corewar" })
                {
                    Assert.NotEqual(EntryStatus.Error, run.StatusOf(id), id);
                    CalendarRun.Fired war = run.Fires.FirstOrDefault(f => f.EntryId == id && f.Company == CompanyKey.Player && f.EventId.StartsWith("hist.decide.pricewar.", StringComparison.Ordinal));
                    Assert.NotNull(war, "seed " + seed + ": the player is asked about " + id + " (as the aggressor or as a follower)");
                }
            }
        }

        // ---- the spacing of the major entries ----

        [Test]
        public void NoTwoMajorEntries_FireCloserThan45Days()
        {
            var runs = new List<Summary>(ByDefault());
            runs.AddRange(WithDisaster());
            runs.Add(Play(WorldKind.Dominating, 1, true));

            int pairs = 0;
            foreach (Summary world in runs)
            {
                for (int i = 1; i < world.MajorDays.Count; i++)
                {
                    pairs++;
                    int gap = world.MajorDays[i] - world.MajorDays[i - 1];
                    Assert.True(gap >= 45, world.Name + ": two major entries " + gap + " days apart (days " + world.MajorDays[i - 1] + " and " + world.MajorDays[i] + ")");
                }
            }
            Assert.True(pairs >= 2000, "the pairs of major entries compared: " + pairs);
        }

        // ---- the antitrust cases ----

        [Test]
        public void TheDominatingWorld_MakesThePlayerTheTargetOfAtLeastTwoOfTheThreeCases()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                Summary world = Play(WorldKind.Dominating, seed, false);

                Assert.True(world.AntitrustAgainstPlayer.Count >= 2, world.Name + ": cases against the player: " + string.Join(", ", world.AntitrustAgainstPlayer));
                Assert.Equal(new[] { "hist.1998.ftc", "hist.2005.jftc", "hist.2009.eufine" }, world.AntitrustAgainstPlayer.OrderBy(id => id, StringComparer.Ordinal).ToArray(), world.Name);
                Assert.Equal(EntryStatus.Expired, world.Status["hist.2009.settlement"], world.Name + ": the suit of the victims needs a case against somebody else");
            }
        }

        [Test]
        public void TheWeakPlayerWorld_NeverMakesThePlayerTheTargetOfACase()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                Summary world = Play(WorldKind.Weak, seed, true);

                Assert.Equal(0, world.AntitrustAgainstPlayer.Count, world.Name + ": cases against the player: " + string.Join(", ", world.AntitrustAgainstPlayer));
            }
        }

        [Test]
        public void InTheOrdinaryWorlds_NobodyDominatesTheMarketAsThePlayer_SoNoCaseIsAgainstHim()
        {
            foreach (Summary world in ByDefault())
            {
                Assert.Equal(0, world.AntitrustAgainstPlayer.Count, world.Name + ": the leader is an AI company, the player has under 0.7 of the leader's share");
            }
        }

        [Test]
        public void WhenTheGiantIsAnAiCompany_TheCasesAreAgainstIt_AndTheVictimsSueOnlyAfterTheFine()
        {
            // Every ordinary world in which the EU fines somebody also sees the suit of the victims (when there is a plaintiff), and never the other way round.
            foreach (Summary world in ByDefault())
            {
                if (world.FiredIds.Contains("hist.2009.settlement")) Assert.True(world.FiredIds.Contains("hist.2009.eufine"), world.Name + ": a suit without a fine");
                if (world.FiredIds.Contains("hist.2009.eufine")) Assert.True(world.FiredIds.Contains("hist.2009.settlement"), world.Name + ": a fine whose victims never sue");
            }
        }

        // ---- the games end well ----

        [Test]
        public void NoGame_HasAnError_AndEveryEntryEndsInAFinalStatus()
        {
            var worlds = new List<Summary>(ByDefault());
            worlds.AddRange(WithDisaster());
            worlds.Add(Play(WorldKind.Average, 1, true));
            worlds.Add(Play(WorldKind.Dominating, 1, true));
            worlds.Add(Play(WorldKind.Weak, 1, true));

            foreach (Summary world in worlds)
            {
                Assert.Equal(0, world.Errors, world.Name + ": errors in the log");
                Assert.Equal(0, world.Warnings, world.Name + ": warnings in the log (a condition that threw, a bad parameter)");
                foreach (KeyValuePair<string, EntryStatus> entry in world.Status)
                {
                    Assert.True(
                        entry.Value == EntryStatus.Fired || entry.Value == EntryStatus.Expired || entry.Value == EntryStatus.Disabled || entry.Value == EntryStatus.SkippedBeforeStart,
                        world.Name + ": " + entry.Key + " ended as " + entry.Value);
                }
                Assert.Equal(2, world.Status.Count(s => s.Value == EntryStatus.SkippedBeforeStart), world.Name + ": a game that starts in 1975 sets aside the two entries before it");
            }
        }

        [Test]
        public void EveryEntryThatFires_FiresInsideItsWindow_AndItsStagesFollowTheirOffsets()
        {
            var run = new CalendarRun(WorldProfile.Draw(7, WorldKind.Typical), true).Execute();
            var byId = ShippedContent.Entries.ToDictionary(e => e.Id, e => e, StringComparer.Ordinal);

            foreach (FiredEntryView fired in run.FiredEntries)
            {
                CalendarEntry entry = byId[fired.EntryId];
                Assert.True(fired.FiredDay >= entry.WindowFrom.Days && fired.FiredDay <= entry.WindowTo.Days, fired.EntryId + " fired on day " + fired.FiredDay + ", outside its window");

                // The events of stage k are fired on firedDay + offset (the loop visits the day on which something is due).
                for (int k = 0; k < entry.Stages.Count; k++)
                {
                    int day = fired.FiredDay + entry.Stages[k].OffsetDays;
                    foreach (FireSpec spec in entry.Stages[k].Fires)
                    {
                        bool any = run.Fires.Any(f => f.EventId == spec.EventId && f.EntryId == fired.EntryId && f.Day == day);
                        bool dropped = spec.OnlyIf != null || spec.Role == RoleKinds.Player;
                        Assert.True(any || dropped, fired.EntryId + " stage " + k + ": " + spec.EventId + " was not fired on day " + day);
                    }
                }
            }
        }

        // ---- determinism ----

        [Test]
        public void TheSameSeed_GivesTheSameGame_EventForEvent()
        {
            foreach (uint seed in new uint[] { 1, 7, 42, 100 })
            {
                Summary first = Play(WorldKind.Typical, seed, true);
                Summary second = Play(WorldKind.Typical, seed, true);

                Assert.Equal(first.Digest, second.Digest, "seed " + seed);
                Assert.Equal(first.Fired, second.Fired);
                Assert.Equal(first.Decisions, second.Decisions);
                Assert.Equal(first.MajorDays.ToArray(), second.MajorDays.ToArray());
            }
        }

        [Test]
        public void AWorld_DoesNotDependOnTheWorldsPlayedBeforeIt()
        {
            // The hundred were played in order; world 42 played alone, after a different world, is the same game.
            Summary inTheSet = ByDefault()[41];
            Play(WorldKind.Dominating, 3, true);
            Summary alone = Play(WorldKind.Typical, 42, false);

            Assert.Equal(inTheSet.Profile.Seed, alone.Profile.Seed);
            Assert.Equal(inTheSet.Digest, alone.Digest);
        }

        [Test]
        public void DifferentSeeds_GiveDifferentGames()
        {
            Assert.NotEqual(Play(WorldKind.Typical, 1, false).Digest, Play(WorldKind.Typical, 2, false).Digest);
            Assert.NotEqual(Play(WorldKind.Typical, 1, false).Digest, Play(WorldKind.Typical, 1, true).Digest, "the same world with another category set is another game");
        }

        // ---- the cost of a day (NFR-1) ----

        [Test]
        public void ADayWithNothingDue_CostsLessThanAFiftiethOfAMillisecond_NFR_1()
        {
            var run = new CalendarRun(WorldProfile.Draw(1, WorldKind.Typical), false);
            int start = CalendarRun.Day("1975-01-01");
            run.World.AdvanceTo(start);
            run.Core.Initialize(start);

            // Play the first year, then ask again and again for a day on which nothing is due.
            int quiet = CalendarRun.Day("1976-02-01");
            run.World.AdvanceTo(quiet);
            run.Core.OnDay(quiet);
            Assert.True(run.Core.NextDueDay > quiet, "a quiet stretch of the calendar: the next item is on day " + run.Core.NextDueDay);

            const int calls = 200000;
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < calls; i++) run.Core.OnDay(quiet);
            clock.Stop();

            double millisecondsPerCall = clock.Elapsed.TotalMilliseconds / calls;
            Assert.True(millisecondsPerCall < 0.05, "one quiet day took " + millisecondsPerCall.ToString("0.000000") + " ms");
        }

        [Test]
        public void TheRoles_AreLookedFor_OnlyWhenAnEntryIsDue_NotOnEveryDay()
        {
            var run = new CalendarRun(WorldProfile.Draw(5, WorldKind.Typical), true).Execute();

            Assert.True(run.ChecksOfRoles >= 40, "the roles were looked for " + run.ChecksOfRoles + " times");
            Assert.True(run.ChecksOfRoles <= 1500, "and not on every day of the game");
        }
    }

    /// <summary>The synthetic worlds themselves: they are what their profiles say, and the same seed makes the same world.</summary>
    public class SyntheticWorldTests
    {
        private static SyntheticWorld WorldAt(WorldProfile profile, string iso)
        {
            var world = new SyntheticWorld(profile);
            world.AdvanceTo(CalendarRun.Day(iso));
            return world;
        }

        private static float Share(SyntheticWorld world, string key, int months, SegmentMask mask = SegmentMask.All)
        {
            var cache = new MarketShareCache();
            cache.Rebuild(world, 36);
            return cache.Share(key, mask, months);
        }

        [Test]
        public void TheDominatingPlayer_HasMoreThanSixtyPercent_FromTheMiddleOfTheNineties()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                WorldProfile profile = WorldProfile.Draw(seed, WorldKind.Dominating);
                var world = new SyntheticWorld(profile);
                var cache = new MarketShareCache();

                for (int year = 1995; year <= 2030; year++)
                {
                    foreach (int month in new[] { 4, 10 })
                    {
                        world.AdvanceTo(CalendarRun.Day(year + "-" + month.ToString("00") + "-15"));
                        cache.Rebuild(world, 36);
                        Assert.True(cache.Share("player", SegmentMask.All, 1) > 0.60f, "seed " + seed + " " + year + "-" + month + ": the share of the player is " + cache.Share("player", SegmentMask.All, 1));
                    }
                }
            }
        }

        [Test]
        public void TheWeakPlayer_NeverHasFivePercent()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                WorldProfile profile = WorldProfile.Draw(seed, WorldKind.Weak);
                var world = new SyntheticWorld(profile);
                var cache = new MarketShareCache();

                for (int year = 1975; year <= 2030; year++)
                {
                    world.AdvanceTo(CalendarRun.Day(year + "-07-15"));
                    cache.Rebuild(world, 36);
                    Assert.True(cache.Share("player", SegmentMask.All, 1) < 0.05f, "seed " + seed + " " + year + ": the share of the player is " + cache.Share("player", SegmentMask.All, 1));
                }
            }
        }

        [Test]
        public void TheLeaderOfAnOrdinaryWorld_IsAnAiCompany_AndTheShareOfTheLeaderFollowsItsProfile()
        {
            WorldProfile profile = WorldProfile.Draw(2, WorldKind.Typical);
            SyntheticWorld world = WorldAt(profile, "2005-03-15");
            var cache = new MarketShareCache();
            cache.Rebuild(world, 36);

            string leader = cache.Ranking(SegmentMask.All, 1)[0];
            Assert.Equal(profile.LeaderUid == 0 ? "ai:0" : "ai:1", leader, "the leader of the profile leads");
            Assert.Near(WorldProfile.At(profile.LeaderShare, 2005.2f), cache.Share(leader, SegmentMask.All, 1), 0.04, "and holds the share the profile gives it");
        }

        [Test]
        public void TheView_ShowsOnlyWhatHasHappened_AndTheJournalsEndInTheCurrentMonth()
        {
            WorldProfile profile = WorldProfile.Draw(3, WorldKind.Typical);
            SyntheticWorld early = WorldAt(profile, "1980-06-10");

            Assert.Equal(5 * 12 + 5, early.MonthsSinceStart);
            Assert.True(early.Companies.All(c => c.FoundedYear <= 1980), "nobody is shown before its foundation");
            Assert.False(early.Companies.Any(c => c.Key == "ai:13"), "Apple does not exist in 1980");
            Assert.True(early.Cpus.All(c => c.Released && c.Year <= 1980), "only released CPUs");
            Assert.True(early.Cpus.Count >= 5, "CPUs of the first years: " + early.Cpus.Count);

            foreach (CpuInfo cpu in early.Cpus)
            {
                int months = cpu.Sold.Count;
                Assert.True(months >= 1, "a journal starts with the month of the release");
                Assert.Equal(months, cpu.SoldByHighEnd.Count, "the journals of the segments are as long as the total");
                Assert.True(cpu.Sold.All(v => v >= 0));
            }
        }

        [Test]
        public void ACompanyThatFails_IsMarkedBankrupt_AndStopsSelling()
        {
            // Cyrix fails in the late nineties in a world where it is drawn to fail.
            WorldProfile profile = null;
            for (uint seed = 1; seed <= 200 && profile == null; seed++)
            {
                WorldProfile candidate = WorldProfile.Draw(seed, WorldKind.Typical);
                if (candidate.HasCyrix && candidate.CyrixFails) profile = candidate;
            }
            Assert.NotNull(profile);

            SyntheticWorld before = WorldAt(profile, "1996-06-15");
            SyntheticWorld after = WorldAt(profile, "2002-06-15");

            Assert.True(before.Companies.Any(c => c.Key == "ai:10"));
            CompanyInfo cyrix = after.Companies.First(c => c.Key == "ai:10");
            Assert.True(cyrix.IsBankrupt, "Cyrix is gone by 2002");
            Assert.Equal(0f, Share(after, "ai:10", 12), "and sold nothing in the last year");
        }

        [Test]
        public void TheSameProfile_MakesTheSameWorld_AndASeedMakesItsOwn()
        {
            WorldProfile a = WorldProfile.Draw(9, WorldKind.Typical);
            WorldProfile b = WorldProfile.Draw(9, WorldKind.Typical);
            WorldProfile c = WorldProfile.Draw(10, WorldKind.Typical);

            Assert.Equal(a.Describe(), b.Describe());
            Assert.NotEqual(a.Describe(), c.Describe());

            SyntheticWorld one = WorldAt(a, "2000-01-15");
            SyntheticWorld two = WorldAt(b, "2000-01-15");
            Assert.Equal(one.Cpus.Count, two.Cpus.Count);
            Assert.Equal(Share(one, "ai:0", 24), Share(two, "ai:0", 24));
        }

        [Test]
        public void TheAverageWorld_HasEveryNamedCompanyOfTheCalendar()
        {
            SyntheticWorld world = WorldAt(WorldProfile.Draw(1, WorldKind.Average), "1995-06-15");

            foreach (int uid in new[] { 0, 1, 6, 9, 10 })
            {
                Assert.True(world.Companies.Any(c => c.UniqueId == uid), "the calendar names company " + uid);
            }
            Assert.True(world.Companies.First(c => c.UniqueId == 9).IsFoundry, "9 is a foundry");
            Assert.True(world.Companies.First(c => c.UniqueId == 10).IsFabless, "10 has no factory");
        }
    }
}
