using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The random characters (FR-11 item 1, TSK-021): the deal of the archetypes among the companies at the start of a new game. The rules are
    /// <see cref="RandomAssignment"/>'s, the saving is <see cref="PersonalityBook"/>'s (<c>Assign</c>, <c>DealRandom</c>); the moment (the end of the
    /// fast-forward, once) is read from the source of the registry.
    /// </summary>
    [Covers(typeof(RandomAssignment), typeof(AssignmentCandidate))]
    public class RandomAssignmentTests
    {
        private static readonly string[] Pool = { "hegemon", "price_warrior", "fast_follower", "engineer", "conservative", "niche_specialist" };
        private static readonly string[] AllIds = Pool.Concat(new[] { "foundry_partner" }).ToArray();

        /// <summary>The 13 historical companies; 8 and 9 are the foundries.</summary>
        private static List<AssignmentCandidate> Thirteen()
        {
            var list = new List<AssignmentCandidate>();
            for (int uid = 0; uid <= 12; uid++) list.Add(new AssignmentCandidate(uid, uid == 8 || uid == 9));
            return list;
        }

        private static IModRng Stream(uint seed)
        {
            return new RngStreams(seed).Get(RandomAssignment.StreamId);
        }

        private static string Show(List<AssignmentEntry> plan)
        {
            return string.Join(",", plan.Select(e => e.uid + "=" + e.archetype));
        }

        /// <summary>Counts the draws; otherwise the real generator.</summary>
        private sealed class CountingRng : IModRng
        {
            private readonly IModRng _inner;
            public int Draws;

            public CountingRng(IModRng inner)
            {
                _inner = inner;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                Draws++;
                return _inner.Range(minInclusive, maxExclusive);
            }

            public float Value()
            {
                Draws++;
                return _inner.Value();
            }

            public bool Chance(float probability)
            {
                Draws++;
                return _inner.Chance(probability);
            }

            public int WeightedIndex(IList<float> weights)
            {
                Draws++;
                return _inner.WeightedIndex(weights);
            }

            public T Pick<T>(IList<T> items)
            {
                Draws++;
                return _inner.Pick(items);
            }
        }

        // ---- the rules ----

        [Test]
        public void TheFoundries_AlwaysGetTheFoundryPartner_AndNobodyElseDoes()
        {
            for (uint seed = 1; seed <= 100; seed++)
            {
                List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), AllIds, Stream(seed));

                Assert.Equal(13, plan.Count, "seed " + seed);
                foreach (AssignmentEntry entry in plan)
                {
                    bool foundry = entry.uid == 8 || entry.uid == 9;
                    Assert.Equal(foundry, entry.archetype == "foundry_partner", "seed " + seed + " ai:" + entry.uid + " -> " + entry.archetype);
                }
            }
        }

        [Test]
        public void NoOtherArchetype_GoesToMoreThanThreeCompanies()
        {
            for (uint seed = 1; seed <= 300; seed++)
            {
                List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), AllIds, Stream(seed));

                foreach (IGrouping<string, AssignmentEntry> group in plan.Where(e => e.archetype != "foundry_partner").GroupBy(e => e.archetype))
                {
                    Assert.True(group.Count() <= RandomAssignment.MaxPerArchetype, "seed " + seed + ": " + group.Key + " x" + group.Count() + " in " + Show(plan));
                }
            }
            Assert.Equal(3, RandomAssignment.MaxPerArchetype);
        }

        [Test]
        public void TheDeal_IsTheSameForTheSameSeed_AndNotForEverySeed()
        {
            string first = Show(RandomAssignment.Plan(Thirteen(), AllIds, Stream(1234)));
            string again = Show(RandomAssignment.Plan(Thirteen(), AllIds, Stream(1234)));
            Assert.Equal(first, again, "the same world seed deals the same characters");

            var seen = new HashSet<string>();
            for (uint seed = 1; seed <= 30; seed++) seen.Add(Show(RandomAssignment.Plan(Thirteen(), AllIds, Stream(seed))));
            Assert.True(seen.Count >= 25, "30 seeds gave only " + seen.Count + " different deals");
        }

        [Test]
        public void TheDeal_DiffersFromTheHistoricalOne()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();
            for (uint seed = 1; seed <= 20; seed++)
            {
                List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), AllIds, Stream(seed));
                int same = plan.Count(e => e.archetype == catalog.DefaultArchetypeFor(e.uid));

                Assert.True(same < 13, "seed " + seed + " dealt the historical characters");
                Assert.True(plan.Where(e => e.uid != 8 && e.uid != 9).Any(e => e.archetype != catalog.DefaultArchetypeFor(e.uid)), "seed " + seed);
            }
        }

        [Test]
        public void EachCompany_CostsExactlyOneDraw_AndAFoundryCostsNone()
        {
            var rng = new CountingRng(Stream(7));

            RandomAssignment.Plan(Thirteen(), AllIds, rng);

            Assert.Equal(11, rng.Draws, "13 companies, 2 foundries");
        }

        [Test]
        public void TheOrderOfTheInput_DoesNotChangeTheDeal()
        {
            List<AssignmentCandidate> shuffled = Thirteen();
            shuffled.Reverse();

            string normal = Show(RandomAssignment.Plan(Thirteen(), AllIds, Stream(99)));
            string reversed = Show(RandomAssignment.Plan(shuffled, AllIds, Stream(99)));

            Assert.Equal(normal, reversed, "the companies are dealt in the order of the UniqueID");
            Assert.Equal(Enumerable.Range(0, 13).ToList(), RandomAssignment.Plan(shuffled, AllIds, Stream(99)).Select(e => e.uid).ToList());
        }

        [Test]
        public void ADuplicatedOrNegativeUniqueId_IsIgnored()
        {
            var companies = new List<AssignmentCandidate>
            {
                new AssignmentCandidate(3, false), new AssignmentCandidate(3, false), new AssignmentCandidate(-1, false), new AssignmentCandidate(5, false),
            };

            List<AssignmentEntry> plan = RandomAssignment.Plan(companies, AllIds, Stream(5));

            Assert.Equal(new List<int> { 3, 5 }, plan.Select(e => e.uid).ToList());
        }

        [Test]
        public void MoreCompaniesThanRoom_StillEveryoneGetsACharacter_AndTheLeastUsedOnesAreOpenedAgain()
        {
            var companies = new List<AssignmentCandidate>();
            for (int uid = 0; uid < 40; uid++) companies.Add(new AssignmentCandidate(uid, false));   // 6 archetypes x 3 = 18 places

            List<AssignmentEntry> plan = RandomAssignment.Plan(companies, AllIds, Stream(11));

            Assert.Equal(40, plan.Count);
            int[] counts = Pool.Select(id => plan.Count(e => e.archetype == id)).ToArray();
            Assert.True(counts.Max() - counts.Min() <= 1, "the load is even: " + string.Join("/", counts));
            Assert.True(plan.All(e => e.archetype != "foundry_partner"));
        }

        [Test]
        public void WithoutTheFoundryArchetype_TheFoundriesAreLeftToTheDefaults()
        {
            List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), Pool, Stream(3));

            Assert.Equal(11, plan.Count);
            Assert.False(plan.Any(e => e.uid == 8 || e.uid == 9), "the default of assignments.json decides for them");
        }

        [Test]
        public void WithOnlyTheFoundryArchetype_NobodyButTheFoundriesIsDealt()
        {
            List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), new[] { "foundry_partner" }, Stream(3));

            Assert.Equal(new List<int> { 8, 9 }, plan.Select(e => e.uid).ToList());
        }

        [Test]
        public void NothingToDeal_GivesAnEmptyPlan()
        {
            Assert.Equal(0, RandomAssignment.Plan(null, AllIds, Stream(1)).Count);
            Assert.Equal(0, RandomAssignment.Plan(new List<AssignmentCandidate>(), AllIds, Stream(1)).Count);
            Assert.Equal(0, RandomAssignment.Plan(Thirteen(), null, Stream(1)).Count);
            Assert.Equal(0, RandomAssignment.Plan(Thirteen(), new string[0], Stream(1)).Count);
        }

        [Test]
        public void WithoutAGenerator_TheFirstArchetypeWithRoomIsDealt()
        {
            List<AssignmentEntry> plan = RandomAssignment.Plan(Thirteen(), AllIds, null);

            Assert.Equal(13, plan.Count);
            Assert.Equal("hegemon", plan[0].archetype);
            Assert.True(plan.Where(e => e.archetype != "foundry_partner").GroupBy(e => e.archetype).All(g => g.Count() <= 3));
        }

        [Test]
        public void ALimitBelowOne_IsTheDefaultLimit()
        {
            var companies = new List<AssignmentCandidate>();
            for (int uid = 0; uid < 12; uid++) companies.Add(new AssignmentCandidate(uid, false));

            List<AssignmentEntry> plan = RandomAssignment.Plan(companies, AllIds, Stream(2), 0);

            Assert.True(plan.GroupBy(e => e.archetype).All(g => g.Count() <= 3));
        }

        [Test]
        public void TheStream_IsTheOneOfTheSpec()
        {
            Assert.Equal("rival-personalities/assign", RandomAssignment.StreamId);
            Assert.Equal("foundry_partner", RandomAssignment.FoundryArchetypeId);
        }

        // ---- saving ----

        [Test]
        public void DealRandom_SavesTheDeal_WithTheBaselineAttitudeOfEachCharacter()
        {
            var rig = new AttitudeRig();

            List<AssignmentEntry> dealt = rig.Book.DealRandom(Thirteen(), Stream(21));

            Assert.Equal(13, dealt.Count);
            foreach (AssignmentEntry entry in dealt)
            {
                Assert.Equal(entry.archetype, rig.Book.ArchetypeIdOf(entry.uid), "saved ai:" + entry.uid);
                Archetype archetype = rig.Catalog.All.First(a => a.id == entry.archetype);
                Assert.Equal(archetype.attitude.baseline, rig.Book.AttitudeOf(entry.uid).Value, "baseline of " + entry.archetype);
            }
            Assert.Equal("foundry_partner", rig.Book.ArchetypeIdOf(9));
            Assert.Equal(20f, rig.Book.AttitudeOf(9).Value, "TSMC starts at +20 and is revealed at once");
        }

        [Test]
        public void ADeal_IsNotChangedByALaterDeal_OrByTheDefaults()
        {
            var rig = new AttitudeRig();
            List<AssignmentEntry> first = rig.Book.DealRandom(Thirteen(), Stream(21));
            string before = Show(first);

            List<AssignmentEntry> second = rig.Book.DealRandom(Thirteen(), Stream(22));   // the mode was switched again in the middle of the game
            foreach (int uid in Enumerable.Range(0, 13)) rig.Book.EnsureAssigned(uid);

            Assert.Equal(0, second.Count, "everybody has a character: nothing is dealt again");
            Assert.Equal(before, string.Join(",", Enumerable.Range(0, 13).Select(uid => uid + "=" + rig.Book.ArchetypeIdOf(uid))), "the characters stayed");
        }

        [Test]
        public void ACompanyWithACharacter_IsLeftOutOfTheDeal()
        {
            var rig = new AttitudeRig();
            rig.Book.EnsureAssigned(0);   // Intel: hegemon from assignments.json

            List<AssignmentEntry> dealt = rig.Book.DealRandom(Thirteen(), Stream(21));

            Assert.Equal(12, dealt.Count);
            Assert.False(dealt.Any(e => e.uid == 0));
            Assert.Equal("hegemon", rig.Book.ArchetypeIdOf(0));
        }

        [Test]
        public void Assign_RefusesWhatItCannotSave()
        {
            var rig = new AttitudeRig();

            Assert.False(rig.Book.Assign(-1, "hegemon"), "not a company");
            Assert.False(rig.Book.Assign(1, null), "no id");
            Assert.False(rig.Book.Assign(1, "wizard"), "not in the catalog");
            Assert.Null(rig.Book.ArchetypeIdOf(1));

            Assert.True(rig.Book.Assign(1, "engineer"));
            Assert.False(rig.Book.Assign(1, "hegemon"), "a character is never re-dealt");
            Assert.Equal("engineer", rig.Book.ArchetypeIdOf(1));
        }

        [Test]
        public void ADealWithAnEmptyCatalog_GivesNothing()
        {
            var book = new PersonalityBook(() => new RivalState(), () => ArchetypeCatalog.Empty);

            Assert.Equal(0, book.DealRandom(Thirteen(), Stream(1)).Count);
            Assert.Equal(0, book.DealRandom(null, Stream(1)).Count);
        }

        [Test]
        public void TheDeal_SurvivesTheSaveRoundTrip()
        {
            var rig = new AttitudeRig();
            rig.Book.DealRandom(Thirteen(), Stream(8));
            string before = Show(Enumerable.Range(0, 13).Select(uid => new AssignmentEntry { uid = uid, archetype = rig.Book.ArchetypeIdOf(uid) }).ToList());

            RivalState back = RivalSaveCodec.Read(RivalSaveCodec.Write(rig.State), null);
            var loaded = new PersonalityBook(() => back, () => rig.Catalog);

            Assert.Equal(before, Show(Enumerable.Range(0, 13).Select(uid => new AssignmentEntry { uid = uid, archetype = loaded.ArchetypeIdOf(uid) }).ToList()));
        }

        // ---- the moment ----

        [Test]
        public void TheRegistry_DealsOnlyAtTheStartOfANewGame_FromTheStreamOfTheSpec()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities", "Behaviour", "PersonalityRegistry.cs")), false);

            Assert.Equal(1, Regex.Matches(code, @"DealRandomCharacters\(companies\)").Count, "one call");
            Assert.True(Regex.IsMatch(code, @"private void StartNewGame\(\)[^}]*?DealRandomCharacters\(companies\)", RegexOptions.Singleline), "the call is in StartNewGame (TickPhase.FastForwardEnded)");
            Assert.True(Regex.IsMatch(code, @"if \(!RivalSettings\.RandomCharacters"), "the setting is read at that moment");
            Assert.True(Regex.IsMatch(code, @"ModRandom\.Stream\(RandomAssignment\.StreamId\)"), "the stream rival-personalities/assign");
            Assert.True(Regex.IsMatch(code, @"new AssignmentCandidate\(company\.UniqueID, company\.IsFoundry\)"), "the foundries are the game's");
            Assert.False(Regex.IsMatch(code, @"System\.Random|UnityEngine\.Random|new Random\("), "no random numbers of the game");
        }
    }
}
