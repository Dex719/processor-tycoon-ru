using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The seed of a new game and of an old save (FR-8; TSK-014): the player gets <c>proprietary</c> and the families of <c>seed.playerPath</c> up to the unlock
    /// year U (AC-8.1: U = 1993 gives <c>proprietary</c> and x86), an AI company gets the families of its preference record that existed for it (AC-8.2: in 2008
    /// Intel designs on x86-64, Texas Instruments on ARM), requirements come along, the starting support follows <c>seed</c>, and a CPU without a binding gets the
    /// family of its owner and year (a CPU of the player from 2001 is x86). Everything is deterministic and uses the real content files.
    /// </summary>
    [Covers(typeof(ArchSeedPolicy), typeof(SeedResult))]
    public class ArchSeedPolicyTests
    {
        private const int Intel = 0;
        private const int Amd = 1;
        private const int TexasInstruments = 3;
        private const int Motorola = 7;

        private static ArchCatalog Catalog()
        {
            return ArchTestKit.BuiltInCatalog();
        }

        private static AiPreference PreferenceOf(ArchCatalog catalog, int uniqueId)
        {
            return ArchTestKit.BuiltInPreferences(catalog).Match(uniqueId);
        }

        private static string Joined(IEnumerable<string> ids)
        {
            return string.Join(",", ids);
        }

        // ---- the player ----

        [Test]
        public void SeedPlayer_Unlocked1993_ProprietaryAndX86_AC81()
        {
            ArchCatalog catalog = Catalog();
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 1993, ArchTestKit.BuiltInBalance().seed);

            Assert.Equal("proprietary,x86", Joined(seed.Researched), "x86-64 is from 2003");
            Assert.Equal("x86", seed.Active, "the designer opens on the newest family");
        }

        [Test]
        public void SeedPlayer_FollowsTheUnlockYear_AndKeepsTheCatalogOrder()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;

            Assert.Equal("proprietary", Joined(ArchSeedPolicy.SeedPlayer(catalog, 1975, cfg).Researched), "x86 appears in 1978");
            Assert.Equal("proprietary,x86", Joined(ArchSeedPolicy.SeedPlayer(catalog, 1978, cfg).Researched), "the year itself counts");
            Assert.Equal("proprietary,x86", Joined(ArchSeedPolicy.SeedPlayer(catalog, 2002, cfg).Researched));
            SeedResult late = ArchSeedPolicy.SeedPlayer(catalog, 2003, cfg);
            Assert.Equal("proprietary,x86,x86_64", Joined(late.Researched));
            Assert.Equal("x86_64", late.Active);
            Assert.Equal("proprietary", ArchSeedPolicy.SeedPlayer(catalog, 0, cfg).Active, "no technology unlocked: only the default family");
            Assert.Equal("proprietary", Joined(ArchSeedPolicy.SeedPlayer(catalog, -1, cfg).Researched));
        }

        [Test]
        public void SeedPlayer_OnlyThePathIsGiven_RiscAndArmAreNotFree()
        {
            ArchCatalog catalog = Catalog();
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 2020, ArchTestKit.BuiltInBalance().seed);
            Assert.Equal("proprietary,x86,x86_64", Joined(seed.Researched), "playerPath is proprietary, x86, x86_64: the rest the player researches");
        }

        [Test]
        public void SeedPlayer_StartingSupport_NewestIs50Normal_OthersAre30Off()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 2005, cfg);

            Assert.Equal(50f, seed.Support["x86_64"]);
            Assert.Equal(SupportTier.Normal, seed.Tiers["x86_64"]);
            Assert.Equal(30f, seed.Support["x86"]);
            Assert.Equal(SupportTier.Off, seed.Tiers["x86"]);
            Assert.Equal(30f, seed.Support["proprietary"]);
            Assert.Equal(SupportTier.Off, seed.Tiers["proprietary"]);
            Assert.Equal(seed.Researched.Count, seed.Support.Count);
            Assert.Equal(seed.Researched.Count, seed.Tiers.Count);
        }

        [Test]
        public void SeedPlayer_ARequirementComesAlong_EvenWhenItsYearIsNotReached()
        {
            ArchCatalog catalog = Catalog();
            var cfg = new SeedConfig { playerPath = new[] { "proprietary", "x86_64" } };   // x86 is not on the path, x86-64 requires it

            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 2004, cfg);
            Assert.Equal("proprietary,x86,x86_64", Joined(seed.Researched));
            Assert.Equal("x86_64", seed.Active);

            Assert.Equal("proprietary", Joined(ArchSeedPolicy.SeedPlayer(catalog, 2002, cfg).Researched), "x86-64 does not exist yet: nothing comes along");
        }

        [Test]
        public void SeedPlayer_UnknownIdsOnThePath_AreIgnored()
        {
            ArchCatalog catalog = Catalog();
            var cfg = new SeedConfig { playerPath = new[] { "vliw", null, "x86", "x86" } };
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 1990, cfg);
            Assert.Equal("proprietary,x86", Joined(seed.Researched));
        }

        // ---- the AI ----

        [Test]
        public void SeedAi_In2008_IntelDesignsOnX8664_TexasInstrumentsOnArm_AC82()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;

            SeedResult intel = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, Intel), 2008, cfg);
            Assert.Equal("x86_64", intel.Active, "x86 and x86-64 both weigh 1.0: the newer one");
            Assert.Equal("proprietary,x86,arm,x86_64", Joined(intel.Researched), "ARM64 is from 2011; the others have weights");

            SeedResult ti = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, TexasInstruments), 2008, cfg);
            Assert.Equal("arm", ti.Active, "proprietary and ARM weigh 1.0: the newer one");
            Assert.Equal("proprietary,arm", Joined(ti.Researched), "ARM64 and RISC-V come two years after their appearance for this company");
        }

        [Test]
        public void SeedAi_TheLagOfTheCompanyDelaysAFamily()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            AiPreference amd = PreferenceOf(catalog, Amd);   // lag 1 year, x86 weighs 1.0
            Assert.Equal(1, amd.LagYears);

            Assert.Equal("proprietary", Joined(ArchSeedPolicy.SeedAi(catalog, amd, 1978, cfg).Researched), "x86 is from 1978, AMD takes it up in 1979");
            Assert.Equal("proprietary,x86", Joined(ArchSeedPolicy.SeedAi(catalog, amd, 1979, cfg).Researched));
            Assert.Equal("x86", ArchSeedPolicy.SeedAi(catalog, amd, 1979, cfg).Active);
            Assert.Equal("proprietary", ArchSeedPolicy.SeedAi(catalog, amd, 1979, cfg).Researched[0]);
        }

        [Test]
        public void SeedAi_BeforeAnyFamilyExists_OnlyProprietary_AndItIsActive()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            SeedResult seed = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, Intel), 1975, cfg);

            Assert.Equal("proprietary", Joined(seed.Researched));
            Assert.Equal("proprietary", seed.Active);
        }

        [Test]
        public void SeedAi_StartingSupport_ActiveIs60Normal_OthersAre35Off()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            SeedResult seed = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, Intel), 2008, cfg);

            Assert.Equal(60f, seed.Support["x86_64"]);
            Assert.Equal(SupportTier.Normal, seed.Tiers["x86_64"]);
            foreach (string id in new[] { "proprietary", "x86", "arm" })
            {
                Assert.Equal(35f, seed.Support[id], id);
                Assert.Equal(SupportTier.Off, seed.Tiers[id], id);
            }
        }

        [Test]
        public void SeedAi_RequirementsComeAlong_EvenWithoutAWeight()
        {
            ArchCatalog catalog = Catalog();
            var pref = new AiPreference { Id = "test", UniqueId = 50, LagYears = 0 };
            pref.Weights["x86_64"] = 1f;   // x86 has no weight, but x86-64 requires it

            SeedResult seed = ArchSeedPolicy.SeedAi(catalog, pref, 2010, ArchTestKit.BuiltInBalance().seed);
            Assert.Equal("proprietary,x86,x86_64", Joined(seed.Researched));
            Assert.Equal("x86_64", seed.Active, "only a family with a weight is chosen as the active one");
        }

        [Test]
        public void SeedAi_TheDefaultRecord_KeepsProprietaryActive()
        {
            ArchCatalog catalog = Catalog();
            AiPreference other = PreferenceOf(catalog, 99);   // no record of its own: the default one
            Assert.True(other.IsDefault);

            SeedResult seed = ArchSeedPolicy.SeedAi(catalog, other, 2008, ArchTestKit.BuiltInBalance().seed);
            Assert.Equal("proprietary", seed.Active, "proprietary weighs 1.0, the rest 0.6 or 0.4");
            Assert.True(seed.Researched.Contains("x86"));
            Assert.True(seed.Researched.Contains("arm"));
        }

        [Test]
        public void SeedAi_ANullRecord_FallsBackToTheBuiltInOne()
        {
            ArchCatalog catalog = Catalog();
            SeedResult seed = ArchSeedPolicy.SeedAi(catalog, null, 2008, null);
            Assert.Equal("proprietary", Joined(seed.Researched));
            Assert.Equal("proprietary", seed.Active);
        }

        [Test]
        public void Seeds_AreDeterministic()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            for (int unique = 0; unique <= 12; unique++)
            {
                SeedResult a = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, unique), 1995, cfg);
                SeedResult b = ArchSeedPolicy.SeedAi(catalog, PreferenceOf(catalog, unique), 1995, cfg);
                Assert.Equal(Joined(a.Researched), Joined(b.Researched), "company " + unique);
                Assert.Equal(a.Active, b.Active, "company " + unique);
            }
            Assert.Equal(Joined(ArchSeedPolicy.SeedPlayer(catalog, 1999, cfg).Researched), Joined(ArchSeedPolicy.SeedPlayer(catalog, 1999, cfg).Researched));
        }

        // ---- a CPU without a binding ----

        [Test]
        public void DefaultForCpu_Player_TheNewestFamilyOfThePath_AtTheYearOfTheCpu()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;

            Assert.Equal("x86", ArchSeedPolicy.DefaultForCpu(catalog, null, true, 2001, cfg), "AC-8.2: a CPU of the player from 2001");
            Assert.Equal("x86_64", ArchSeedPolicy.DefaultForCpu(catalog, null, true, 2003, cfg));
            Assert.Equal("x86", ArchSeedPolicy.DefaultForCpu(catalog, null, true, 1978, cfg));
            Assert.Equal("proprietary", ArchSeedPolicy.DefaultForCpu(catalog, null, true, 1977, cfg));
            Assert.Equal("proprietary", ArchSeedPolicy.DefaultForCpu(catalog, null, true, 0, cfg));
        }

        [Test]
        public void DefaultForCpu_Ai_TheHeaviestFamilyThatExistedForTheCompany()
        {
            ArchCatalog catalog = Catalog();
            SeedConfig cfg = ArchTestKit.BuiltInBalance().seed;
            AiPreference intel = PreferenceOf(catalog, Intel);
            AiPreference motorola = PreferenceOf(catalog, Motorola);   // lag 1, proprietary 1.0, RISC 1.0

            Assert.Equal("proprietary", ArchSeedPolicy.DefaultForCpu(catalog, intel, false, 1975, cfg));
            Assert.Equal("x86", ArchSeedPolicy.DefaultForCpu(catalog, intel, false, 1985, cfg));
            Assert.Equal("x86_64", ArchSeedPolicy.DefaultForCpu(catalog, intel, false, 2008, cfg));
            Assert.Equal("proprietary", ArchSeedPolicy.DefaultForCpu(catalog, motorola, false, 1986, cfg), "RISC is from 1986, Motorola takes it up in 1987");
            Assert.Equal("risc", ArchSeedPolicy.DefaultForCpu(catalog, motorola, false, 1987, cfg), "a tie of weights goes to the newer family");
        }

        [Test]
        public void DefaultForCpu_AiWithoutARecord_IsProprietary()
        {
            Assert.Equal("proprietary", ArchSeedPolicy.DefaultForCpu(Catalog(), null, false, 2010, new SeedConfig()));
        }

        // ---- the registry ----

        [Test]
        public void Registry_DefaultFamilyFor_UsesThePolicy_ForThePlayerAndByUniqueId()
        {
            ArchRegistry registry = ArchTestKit.BuiltInRegistry();

            Assert.Equal("x86", registry.DefaultFamilyFor(true, -1, 2001), "the player's path");
            Assert.Equal("x86_64", registry.DefaultFamilyFor(false, Intel, 2008), "Intel by its UniqueID");
            Assert.Equal("arm", registry.DefaultFamilyFor(false, TexasInstruments, 2008));
            Assert.Equal("proprietary", registry.DefaultFamilyFor(false, 99, 2008), "an unknown company takes the default record: proprietary weighs most");
            Assert.Equal("proprietary", registry.DefaultFamilyFor(false, -1, 2008), "no UniqueID: the default record too");
        }

        [Test]
        public void Registry_WithoutPreferences_UsesTheBuiltInFallback()
        {
            var registry = new ArchRegistry(Catalog());
            Assert.Equal("x86", registry.DefaultFamilyFor(true, -1, 2001), "the numbers of the seed default to the built-in path");
            Assert.Equal("proprietary", registry.DefaultFamilyFor(false, Intel, 2008), "no table: every company is the fallback record (proprietary only)");
            Assert.True(registry.PreferenceOf(Intel).IsDefault);
        }

        // ---- applying a seed ----

        [Test]
        public void ApplyTo_MakesTheFamiliesResearched_WithTheSupportOfTheSeed()
        {
            ArchCatalog catalog = Catalog();
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 1993, ArchTestKit.BuiltInBalance().seed);
            var state = new CompanyArchState(7, true);

            int added = seed.ApplyTo(state);
            Assert.Equal(2, added);
            Assert.True(state.IsResearched("proprietary"));
            Assert.True(state.IsResearched("x86"));
            Assert.False(state.IsResearched("x86_64"));
            Assert.Equal("x86", state.Active);
            Assert.Equal(1f, state.GetFamily("x86").Progress);
            Assert.Equal(50f, state.GetFamily("x86").Support);
            Assert.Equal(SupportTier.Normal, state.GetFamily("x86").Tier);
            Assert.Equal(0, state.GetFamily("x86").ResearchedYear, "given at the start, not researched on a date");
        }

        [Test]
        public void ApplyTo_Twice_ChangesNothing_AndKeepsWhatTheCompanyChose()
        {
            ArchCatalog catalog = Catalog();
            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 1993, ArchTestKit.BuiltInBalance().seed);
            var state = new CompanyArchState(7, true);
            seed.ApplyTo(state);

            state.Active = "proprietary";                    // the player picked another family in the designer
            state.GetFamily("x86").Support = 77f;            // and paid for more support
            Assert.Equal(0, seed.ApplyTo(state), "nothing is added a second time");
            Assert.Equal("proprietary", state.Active);
            Assert.Equal(77f, state.GetFamily("x86").Support);
        }

        [Test]
        public void ApplyTo_DoesNotTouchAFamilyTheCompanyAlreadyResearched()
        {
            ArchCatalog catalog = Catalog();
            var state = new CompanyArchState(7, true);
            FamilyState x86 = state.GetOrAddFamily("x86");
            x86.Researched = true;
            x86.Progress = 1f;
            x86.ResearchedYear = 1980;
            x86.Support = 90f;

            SeedResult seed = ArchSeedPolicy.SeedPlayer(catalog, 1993, ArchTestKit.BuiltInBalance().seed);
            Assert.Equal(1, seed.ApplyTo(state), "only proprietary is new");
            Assert.Equal(90f, x86.Support);
            Assert.Equal(1980, x86.ResearchedYear);
        }

        [Test]
        public void ApplyTo_ANullState_Throws()
        {
            SeedResult seed = ArchSeedPolicy.SeedPlayer(Catalog(), 1993, null);
            Assert.Throws<System.ArgumentNullException>(() => seed.ApplyTo(null));
        }

        // ---- helpers ----

        [Test]
        public void Closure_AddsRequirementsTransitively_AndDropsUnknownIds()
        {
            ArchCatalog catalog = Catalog();
            HashSet<string> set = ArchSeedPolicy.Closure(catalog, new[] { "arm64", "vliw", null });
            Assert.Equal(2, set.Count);
            Assert.True(set.Contains("arm64"));
            Assert.True(set.Contains("arm"));

            Assert.Equal(0, ArchSeedPolicy.Closure(catalog, null).Count);
            Assert.Equal(0, ArchSeedPolicy.Closure(null, new[] { "x86" }).Count);
        }

        [Test]
        public void PathOf_StartsWithTheDefaultFamily_AndKeepsTheOrder()
        {
            ArchCatalog catalog = Catalog();
            Assert.Equal("proprietary,x86,x86_64", Joined(ArchSeedPolicy.PathOf(catalog, ArchTestKit.BuiltInBalance().seed)));
            Assert.Equal("proprietary,arm", Joined(ArchSeedPolicy.PathOf(catalog, new SeedConfig { playerPath = new[] { "arm", "arm", "nothing" } })));
            Assert.Equal("proprietary", Joined(ArchSeedPolicy.PathOf(catalog, new SeedConfig { playerPath = null })));
        }
    }
}
