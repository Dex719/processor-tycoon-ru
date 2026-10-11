using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The decisions of the AI companies about architectures, stage 1 (FR-7; TSK-016): the record of a company by its <c>UniqueId</c> or the default one, the
    /// choice by weight with the lag of the company (AC-7.1), ties by year and id, the reserve of 6 months × cost × M4, the technology that is too close to its
    /// end to be interrupted (AC-7.3), the pause at negative money, the change of the active family when a research ends (AC-7.2), foundries and bankrupt
    /// companies skipped, and the same answer for the same snapshot.
    /// </summary>
    [Covers(typeof(ArchAiPolicy), typeof(AiView), typeof(AiDecision))]
    public class ArchAiPolicyTests
    {
        private const int Intel = 0;
        private const int Amd = 1;
        private const int TexasInstruments = 3;
        private const int Ibm = 4;
        private const int Cyrix = 10;

        private const float RichMoney = 1e12f;

        private static ArchCatalog Catalog()
        {
            return ArchTestKit.BuiltInCatalog();
        }

        private static AiPreference PreferenceOf(ArchCatalog catalog, int uniqueId)
        {
            return ArchTestKit.BuiltInPreferences(catalog).Match(uniqueId);
        }

        private static AiConfig Config()
        {
            return ArchTestKit.BuiltInBalance().ai;
        }

        /// <summary>A company whose state holds the given researched families (proprietary is always there).</summary>
        private static CompanyArchState StateWith(string active, params string[] researched)
        {
            var state = new CompanyArchState(1, false) { Active = active };
            foreach (string id in researched)
            {
                FamilyState family = state.GetOrAddFamily(id);
                family.Researched = true;
                family.Progress = 1f;
            }
            return state;
        }

        private static AiView View(CompanyArchState state, int year, float money = RichMoney, int technologyDaysLeft = -1, float costModifier = 1f, int difficulty = 2)
        {
            return new AiView
            {
                CompanyId = 1,
                UniqueId = 0,
                Year = year,
                Money = money,
                CostModifier = costModifier,
                Difficulty = difficulty,
                TechnologyDaysLeft = technologyDaysLeft,
                State = state,
            };
        }

        private static AiDecision Decide(AiView view, int uniqueId)
        {
            ArchCatalog catalog = Catalog();
            return ArchAiPolicy.DecideMvp(view, PreferenceOf(catalog, uniqueId), catalog, Config());
        }

        // ---- the record of a company ----

        [Test]
        public void Match_ByUniqueId_ElseTheDefaultRecord()
        {
            ArchCatalog catalog = Catalog();
            AiPreferenceTable table = ArchTestKit.BuiltInPreferences(catalog);

            Assert.Equal("intel", ArchAiPolicy.Match(table.Preferences, Intel).Id);
            Assert.Equal("texas-instruments", ArchAiPolicy.Match(table.Preferences, TexasInstruments).Id);

            // The list of the historical companies has no default record; the one of the table is not in it.
            var all = new List<AiPreference>(table.Preferences) { table.Default };
            Assert.Equal("intel", ArchAiPolicy.Match(all, Intel).Id);
            Assert.True(ArchAiPolicy.Match(all, 99).IsDefault, "a company without a record of its own");
            Assert.Equal("default", ArchAiPolicy.Match(all, -1).Id);
        }

        [Test]
        public void Match_WithoutADefaultRecord_ThereIsTheBuiltInFallback()
        {
            ArchCatalog catalog = Catalog();
            AiPreferenceTable table = ArchTestKit.BuiltInPreferences(catalog);

            AiPreference fallback = ArchAiPolicy.Match(table.Preferences, 99);
            Assert.NotNull(fallback);
            Assert.True(fallback.IsDefault);
            Assert.Equal(1f, fallback.WeightOf(ArchCatalog.DefaultId), "only the default family");
            Assert.NotNull(ArchAiPolicy.Match(null, 5));

            var withNulls = new List<AiPreference> { null, table.ByUniqueId(Intel), null };
            Assert.Equal("intel", ArchAiPolicy.Match(withNulls, Intel).Id);
        }

        // ---- the choice (AC-7.1) ----

        [Test]
        public void Intel_In1979_StartsX86_AC71()
        {
            CompanyArchState state = StateWith("proprietary");
            AiDecision decision = Decide(View(state, 1979), Intel);

            Assert.Equal("x86", decision.StartResearch);
            Assert.Equal(1.0f, decision.Funding, "difficulty 2");
            Assert.False(decision.Pause);
            Assert.False(decision.DeferInterrupt);
            Assert.Null(decision.NewActive, "proprietary stays until x86 is researched");
        }

        [Test]
        public void TheLagOfTheCompany_DelaysTheStart()
        {
            CompanyArchState state = StateWith("proprietary");

            Assert.Null(Decide(View(state, 1977), Intel).StartResearch, "x86 is from 1978");
            Assert.Equal("x86", Decide(View(state, 1978), Intel).StartResearch, "Intel has no lag");

            Assert.Null(Decide(View(state, 1978), Amd).StartResearch, "AMD takes a family up a year late");
            Assert.Equal("x86", Decide(View(state, 1979), Amd).StartResearch);
        }

        [Test]
        public void TheHeaviestFamilyIsChosen_NotTheNewest()
        {
            // IBM likes RISC (1.2) more than x86 (0.3): in 1990 both exist, RISC is chosen.
            CompanyArchState state = StateWith("proprietary");
            Assert.Equal("risc", Decide(View(state, 1990), Ibm).StartResearch);
        }

        [Test]
        public void ARequirement_MustBeResearchedFirst()
        {
            // x86-64 (2003) needs x86: an AMD without x86 starts x86 even in 2005, and x86-64 once x86 is researched.
            CompanyArchState state = StateWith("proprietary");
            Assert.Equal("x86", Decide(View(state, 2005), Amd).StartResearch);

            CompanyArchState withX86 = StateWith("x86", "x86");
            Assert.Equal("x86_64", Decide(View(withX86, 2005), Amd).StartResearch);
        }

        [Test]
        public void NothingToResearch_WhenEverythingTheCompanyLikesIsDone()
        {
            CompanyArchState state = StateWith("x86_64", "x86", "x86_64", "arm");
            AiDecision decision = Decide(View(state, 2008), Intel);
            Assert.True(decision.IsEmpty, "arm64 is from 2011");
            Assert.Null(decision.StartResearch);
        }

        [Test]
        public void ATieOfWeights_GoesToTheOlderFamily_ThenToTheSmallerId()
        {
            // Intel weighs x86 and x86-64 at 1.0 each: the older one is researched first.
            ArchCatalog catalog = Catalog();
            AiPreference intel = PreferenceOf(catalog, Intel);
            Assert.Equal(intel.WeightOf("x86"), intel.WeightOf("x86_64"));
            ArchFamily first = ArchAiPolicy.PickCandidate(catalog, intel, new Dictionary<string, FamilyState>(), 2010);
            Assert.Equal("x86", first.Id);

            // Equal weights of x86 (1978) and RISC (1986): the older family is researched first.
            var equal = new AiPreference { Id = "e", UniqueId = 2 };
            equal.Weights["x86"] = 1f;
            equal.Weights["risc"] = 1f;
            Assert.Equal("x86", ArchAiPolicy.PickCandidate(catalog, equal, new Dictionary<string, FamilyState>(), 1990).Id);

            // Two families of the same year and the same weight: the smaller id.
            var custom = new ArchCatalog(new[] { ArchTestKit.Proprietary(), ArchTestKit.Family("beta", 1980), ArchTestKit.Family("alpha", 1980) });
            var pref = new AiPreference { Id = "p", UniqueId = 1 };
            pref.Weights["beta"] = 1f;
            pref.Weights["alpha"] = 1f;
            Assert.Equal("alpha", ArchAiPolicy.PickCandidate(custom, pref, new Dictionary<string, FamilyState>(), 1990).Id);

            // The same tie for the active family goes to the NEWER one.
            var researched = new HashSet<string> { "proprietary", "x86", "x86_64" };
            Assert.Equal("x86_64", ArchAiPolicy.PickActive(catalog, intel, researched));
        }

        [Test]
        public void ACandidate_NeedsAWeight()
        {
            ArchCatalog catalog = Catalog();
            var pref = new AiPreference { Id = "p", UniqueId = 1 };
            pref.Weights["arm"] = 0f;
            Assert.Null(ArchAiPolicy.PickCandidate(catalog, pref, new Dictionary<string, FamilyState>(), 2020), "a weight of 0 is never");
            Assert.Null(ArchAiPolicy.PickCandidate(catalog, new AiPreference { Id = "q", UniqueId = 2 }, null, 2020), "no weights at all");
        }

        // ---- the money (the reserve) ----

        [Test]
        public void TheReserve_IsSixMonthsOfTheCostTimesM4()
        {
            // x86 costs 5 000 000 a month: 6 × 5M = 30M.
            CompanyArchState state = StateWith("proprietary");
            Assert.Equal(30000000f, ArchAiPolicy.Reserve(6, Catalog().Get("x86").Research, 1f));

            Assert.Null(Decide(View(state, 1980, 29999000f), Intel).StartResearch, "just below the reserve");
            Assert.Equal("x86", Decide(View(state, 1980, 30000000f), Intel).StartResearch, "the reserve itself is enough");
        }

        [Test]
        public void TheReserve_GrowsWithTheCostModifier_M4Of6ForTheUnluckyCompany()
        {
            CompanyArchState state = StateWith("proprietary");
            Assert.Equal(180000000f, ArchAiPolicy.Reserve(6, Catalog().Get("x86").Research, 6f), "6 × 5M × 6");

            Assert.Null(Decide(View(state, 1990, 100000000f, costModifier: 6f), Cyrix).StartResearch, "100M is less than 180M");
            Assert.Equal("x86", Decide(View(state, 1990, 180000000f, costModifier: 6f), Cyrix).StartResearch);
        }

        [Test]
        public void TheReserve_UsesTheMonthsOfTheConfig_AndToleratesBadNumbers()
        {
            ResearchDef research = Catalog().Get("x86").Research;
            Assert.Equal(60000000f, ArchAiPolicy.Reserve(12, research, 1f));
            Assert.Equal(0f, ArchAiPolicy.Reserve(-3, research, 1f), "no negative months");
            Assert.Equal(30000000f, ArchAiPolicy.Reserve(6, research, float.NaN), "an unusable modifier counts as 1");
            Assert.Equal(0f, ArchAiPolicy.Reserve(6, null, 1f));
        }

        [Test]
        public void NegativeMoney_StartsNothing_AndPausesARunningResearch()
        {
            CompanyArchState idle = StateWith("proprietary");
            AiDecision none = Decide(View(idle, 1980, -1f), Intel);
            Assert.Null(none.StartResearch);
            Assert.False(none.Pause, "nothing is running, nothing to pause");

            CompanyArchState running = StateWith("proprietary");
            running.CurrentResearch = "x86";
            AiDecision pause = Decide(View(running, 1980, -1f), Intel);
            Assert.True(pause.Pause, "FR-7 item 4");
            Assert.Null(pause.StartResearch);

            Assert.False(Decide(View(running, 1980, 0f), Intel).Pause, "zero is not negative");
            Assert.False(Decide(View(running, 1980, RichMoney), Intel).Pause);
        }

        [Test]
        public void ARunningResearch_BlocksANewOne()
        {
            CompanyArchState running = StateWith("proprietary");
            running.CurrentResearch = "x86";
            Assert.True(Decide(View(running, 1990), Ibm).IsEmpty, "IBM would like RISC, but the lab holds x86");

            running.Paused = true;
            Assert.Null(Decide(View(running, 1990), Ibm).StartResearch, "a paused research still holds the slot");
        }

        // ---- the technology in the lab (AC-7.3) ----

        [Test]
        public void ATechnologyWith20DaysLeft_IsNotInterrupted_AC73()
        {
            CompanyArchState state = StateWith("proprietary");
            AiDecision decision = Decide(View(state, 1980, technologyDaysLeft: 20), Intel);

            Assert.True(decision.DeferInterrupt, "the decision waits for the next month");
            Assert.Null(decision.StartResearch);
            Assert.False(decision.IsEmpty);
        }

        [Test]
        public void TheBoundaryOf30Days()
        {
            CompanyArchState state = StateWith("proprietary");

            Assert.True(Decide(View(state, 1980, technologyDaysLeft: 30), Intel).DeferInterrupt, "30 days left is not more than 30");
            AiDecision at31 = Decide(View(state, 1980, technologyDaysLeft: 31), Intel);
            Assert.Equal("x86", at31.StartResearch);
            Assert.False(at31.DeferInterrupt);
            Assert.Equal("x86", Decide(View(state, 1980, technologyDaysLeft: -1), Intel).StartResearch, "a free lab");
            Assert.Equal("x86", Decide(View(state, 1980, technologyDaysLeft: 400), Intel).StartResearch, "a long technology is interrupted, without asking");
        }

        [Test]
        public void WithoutTheMoney_ThereIsNothingToWaitFor()
        {
            CompanyArchState state = StateWith("proprietary");
            AiDecision decision = Decide(View(state, 1980, 1000f, technologyDaysLeft: 20), Intel);
            Assert.False(decision.DeferInterrupt, "it could not start anyway");
            Assert.True(decision.IsEmpty);
        }

        [Test]
        public void TheDaysOfTheConfig_AreUsed()
        {
            ArchCatalog catalog = Catalog();
            AiConfig config = Config();
            config.noInterruptTimeLeftDays = 60;
            CompanyArchState state = StateWith("proprietary");

            AiDecision decision = ArchAiPolicy.DecideMvp(View(state, 1980, technologyDaysLeft: 45), PreferenceOf(catalog, Intel), catalog, config);
            Assert.True(decision.DeferInterrupt);
        }

        // ---- the funding ----

        [Test]
        public void TheFunding_FollowsTheDifficultyTable()
        {
            float[] expected = { 0.6f, 0.8f, 1.0f, 1.3f, 1.6f };
            CompanyArchState state = StateWith("proprietary");
            for (int level = 0; level < expected.Length; level++)
            {
                Assert.Equal(expected[level], Decide(View(state, 1980, difficulty: level), Intel).Funding, "difficulty " + level);
            }
        }

        // ---- the active family (AC-7.2) ----

        [Test]
        public void AfterTheResearch_TheNewerFamilyOfTheSameWeightBecomesActive_AC72()
        {
            // Texas Instruments: proprietary and ARM both weigh 1.0; once ARM is researched the company moves to it.
            CompanyArchState state = StateWith("proprietary", "arm");
            AiDecision decision = Decide(View(state, 1990), TexasInstruments);
            Assert.Equal("arm", decision.NewActive);

            state.Active = "arm";
            Assert.Null(Decide(View(state, 1990), TexasInstruments).NewActive, "already active");
        }

        [Test]
        public void Intel_MovesToX86_AndLaterToX8664()
        {
            CompanyArchState state = StateWith("proprietary", "x86");
            Assert.Equal("x86", Decide(View(state, 1982), Intel).NewActive, "x86 weighs 1.0, proprietary 0.2");

            state = StateWith("x86", "x86", "x86_64");
            Assert.Equal("x86_64", Decide(View(state, 2004), Intel).NewActive, "equal weights: the newer family");
        }

        [Test]
        public void ALighterFamily_DoesNotTakeTheActiveRole()
        {
            // Intel researched ARM (0.1): x86 (1.0) stays.
            CompanyArchState state = StateWith("x86", "x86", "arm");
            Assert.Null(Decide(View(state, 1995), Intel).NewActive);
        }

        [Test]
        public void ACompanyWithoutAnActiveFamily_GetsOne()
        {
            CompanyArchState state = StateWith(null);
            Assert.Equal("proprietary", Decide(View(state, 1975), Intel).NewActive);

            Assert.Equal("proprietary", Decide(View(StateWith("vliw"), 1975), Intel).NewActive, "a family the catalog does not know is replaced");
        }

        [Test]
        public void PickActive_CountsTheFamilyResearchedAtOnce()
        {
            ArchCatalog catalog = Catalog();
            // The state holds no entry for proprietary: it counts as researched anyway (baseDays 0).
            var families = new Dictionary<string, FamilyState>();
            Assert.Equal("proprietary", ArchAiPolicy.PickActive(catalog, PreferenceOf(catalog, Intel), families));
            Assert.Equal("proprietary", ArchAiPolicy.PickActive(catalog, PreferenceOf(catalog, Intel), (IReadOnlyDictionary<string, FamilyState>)null));
            Assert.Equal("proprietary", ArchAiPolicy.PickActive(catalog, PreferenceOf(catalog, Intel), (ISet<string>)null), "nothing researched: the default family");
        }

        // ---- who is skipped ----

        [Test]
        public void AFoundry_AndABankruptCompany_AreSkipped()
        {
            CompanyArchState state = StateWith("proprietary");

            AiView foundry = View(state, 1980);
            foundry.IsFoundry = true;
            Assert.True(Decide(foundry, Intel).IsEmpty, "FR-7 item 6");

            AiView bankrupt = View(state, 1980);
            bankrupt.IsBankrupt = true;
            Assert.True(Decide(bankrupt, Intel).IsEmpty);
        }

        [Test]
        public void ANullView_OrCatalog_DecidesNothing()
        {
            ArchCatalog catalog = Catalog();
            Assert.True(ArchAiPolicy.DecideMvp(null, PreferenceOf(catalog, Intel), catalog, Config()).IsEmpty);
            Assert.True(ArchAiPolicy.DecideMvp(View(StateWith("proprietary"), 1980), PreferenceOf(catalog, Intel), null, Config()).IsEmpty);
        }

        [Test]
        public void ACompanyWithoutAState_GetsAnActiveFamilyAndCanStart()
        {
            AiView view = View(null, 1980);
            AiDecision decision = Decide(view, Intel);
            Assert.Equal("x86", decision.StartResearch);
            Assert.Equal("proprietary", decision.NewActive);
        }

        // ---- determinism ----

        [Test]
        public void TheSameSnapshot_GivesTheSameDecision_AndChangesNothing()
        {
            ArchCatalog catalog = Catalog();
            AiPreferenceTable table = ArchTestKit.BuiltInPreferences(catalog);
            AiConfig config = Config();

            for (int unique = 0; unique <= 12; unique++)
            {
                AiPreference pref = table.Match(unique);
                for (int year = 1975; year <= 2020; year += 5)
                {
                    CompanyArchState state = StateWith("proprietary");
                    AiDecision a = ArchAiPolicy.DecideMvp(View(state, year), pref, catalog, config);
                    AiDecision b = ArchAiPolicy.DecideMvp(View(state, year), pref, catalog, config);

                    string label = "company " + unique + ", year " + year;
                    Assert.Equal(a.StartResearch, b.StartResearch, label);
                    Assert.Equal(a.NewActive, b.NewActive, label);
                    Assert.Equal(a.Funding, b.Funding, label);
                    Assert.Equal(a.Pause, b.Pause, label);
                    Assert.Equal(a.DeferInterrupt, b.DeferInterrupt, label);
                    Assert.Null(state.CurrentResearch, "the policy only reads the state: " + label);
                    Assert.Equal("proprietary", state.Active, label);
                }
            }
        }

        [Test]
        public void AFullHistory_Intel_ResearchesX86ThenX8664_AndTexasInstrumentsArm()
        {
            // The monthly loop the service runs, on the pure policy: start, finish, move to the family, ask again.
            ArchCatalog catalog = Catalog();
            AiConfig config = Config();

            CompanyArchState intel = StateWith("proprietary");
            AiPreference intelPref = PreferenceOf(catalog, Intel);
            AiDecision d = ArchAiPolicy.DecideMvp(View(intel, 1979), intelPref, catalog, config);
            Assert.Equal("x86", d.StartResearch);
            intel.CurrentResearch = "x86";   // started
            intel.GetOrAddFamily("x86").Researched = true;   // finished 8 months later
            intel.CurrentResearch = null;
            d = ArchAiPolicy.DecideMvp(View(intel, 1980), intelPref, catalog, config);
            Assert.Equal("x86", d.NewActive);
            intel.Active = d.NewActive;
            Assert.Equal("arm", ArchAiPolicy.DecideMvp(View(intel, 1990), intelPref, catalog, config).StartResearch, "Intel weighs ARM at 0.1: it is the only thing left to research");
            intel.GetOrAddFamily("arm").Researched = true;
            Assert.Null(ArchAiPolicy.DecideMvp(View(intel, 1990), intelPref, catalog, config).StartResearch, "x86-64 is from 2003");
            Assert.Equal("x86_64", ArchAiPolicy.DecideMvp(View(intel, 2003), intelPref, catalog, config).StartResearch);

            CompanyArchState ti = StateWith("proprietary");
            AiPreference tiPref = PreferenceOf(catalog, TexasInstruments);
            Assert.Null(ArchAiPolicy.DecideMvp(View(ti, 1988), tiPref, catalog, config).StartResearch, "ARM is from 1987, Texas Instruments takes it up two years late");
            Assert.Equal("arm", ArchAiPolicy.DecideMvp(View(ti, 1989), tiPref, catalog, config).StartResearch);
        }

        // ---- stage 2: the adaptive policy (FR-14, TSK-027) ----

        private const int Motorola = 7;

        private static RatingConfig Rating()
        {
            return ArchTestKit.BuiltInBalance().rating;
        }

        private static SupportConfig SupportCfg()
        {
            return ArchTestKit.BuiltInBalance().support;
        }

        /// <summary>A company that has researched the given families with their support level and tier; proprietary is there too (60, off) unless named.</summary>
        private static CompanyArchState Full(string active, params object[] idSupportTier)
        {
            var state = new CompanyArchState(1, false) { Active = active };
            if (Array.IndexOf(idSupportTier, "proprietary") < 0)
            {
                state.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 60f, Tier = SupportTier.Off };
            }
            for (int i = 0; i + 2 < idSupportTier.Length + 0; i += 3)
            {
                string id = (string)idSupportTier[i];
                state.Families[id] = new FamilyState(id)
                {
                    Researched = true,
                    Progress = 1f,
                    Support = Convert.ToSingle(idSupportTier[i + 1]),
                    Tier = (SupportTier)idSupportTier[i + 2],
                };
            }
            return state;
        }

        private static AiDecision DecideFull(AiView view, int uniqueId, Dictionary<string, float> ecosystem = null, AiPreference preference = null)
        {
            ArchCatalog catalog = Catalog();
            return ArchAiPolicy.DecideFull(view, preference ?? PreferenceOf(catalog, uniqueId), catalog, Config(), ecosystem, Rating(), SupportCfg());
        }

        private static Dictionary<string, float> Eco(params object[] pairs)
        {
            var map = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) map[(string)pairs[i]] = Convert.ToSingle(pairs[i + 1]);
            return map;
        }

        private static float Att(string familyId, AiPreference pref, float[] shares, int year, Dictionary<string, float> eco, CompanyArchState state)
        {
            ArchFamily family = Catalog().Get(familyId);
            return ArchAiPolicy.Attractiveness(family, pref.WeightOf(familyId), shares, year, EcosystemMath.ShareOf(eco, familyId), state.GetFamily(familyId).Support, Rating());
        }

        [Test]
        public void AC14_1_MotorolaOnTheProprietaryIsa_In1996_MovesToRisc_WhichIsTenPercentMoreAttractive()
        {
            ArchCatalog catalog = Catalog();
            AiPreference pref = PreferenceOf(catalog, Motorola);
            CompanyArchState state = Full("proprietary", "risc", 40f, SupportTier.Off);
            AiView view = View(state, 1996);
            view.UniqueId = Motorola;
            view.SegmentShare = new[] { 0.8f, 0f, 0f, 0.2f };   // a company that sells to the top of the market and to industry
            Dictionary<string, float> eco = Eco("proprietary", 0.20f, "risc", 0.10f);

            float proprietary = Att("proprietary", pref, view.SegmentShare, 1996, eco, state);
            float risc = Att("risc", pref, view.SegmentShare, 1996, eco, state);
            Assert.True(risc >= 1.10f * proprietary, "the attractiveness of RISC (" + risc + ") is at least 10 % above the proprietary ISA (" + proprietary + ")");

            AiDecision decision = DecideFull(view, Motorola, eco);
            Assert.Equal("risc", decision.NewActive, "Motorola moves to RISC");
        }

        [Test]
        public void ACompany_DoesNotMove_WhenTheBestIsLessThanTenPercentBetter()
        {
            ArchCatalog catalog = Catalog();
            AiPreference pref = PreferenceOf(catalog, Motorola);
            CompanyArchState state = Full("proprietary", "risc", 60f, SupportTier.Off);
            AiView view = View(state, 1996);
            view.UniqueId = Motorola;
            view.SegmentShare = new[] { 0.25f, 0.25f, 0.25f, 0.25f };
            Dictionary<string, float> eco = Eco("proprietary", 0.30f, "risc", 0.30f);

            float proprietary = Att("proprietary", pref, view.SegmentShare, 1996, eco, state);
            float risc = Att("risc", pref, view.SegmentShare, 1996, eco, state);
            Assert.True(risc > proprietary && risc < 1.10f * proprietary, "RISC is better, but not by the margin: " + risc + " against " + proprietary);

            AiDecision decision = DecideFull(view, Motorola, eco);
            Assert.Null(decision.NewActive, "the margin of 10 % is not reached: the company stays");
        }

        [Test]
        public void ACompany_DoesNotMove_WhenTheSupportOfTheBestIsUnder35_AndTheCandidateWithAnEcosystemGetsTheLowTier()
        {
            // A company that likes RISC a little less than its own family (0.97 against 1.0): RISC passes the margin, but its support 34 is under 35, so the company stays.
            // RISC has a real ecosystem (0.30, from 0.2 on): FR-14 item 3 funds such a candidate at 'low'.
            var pref = new AiPreference { Id = "custom", UniqueId = 50, LagYears = 0 };
            pref.Weights["proprietary"] = 1.0f;
            pref.Weights["risc"] = 0.97f;
            CompanyArchState state = Full("proprietary", "risc", 34f, SupportTier.Off);
            AiView view = View(state, 1996);
            view.UniqueId = 50;
            view.SegmentShare = new[] { 1f, 0f, 0f, 0f };
            Dictionary<string, float> eco = Eco("proprietary", 0.20f, "risc", 0.30f);

            float proprietary = Att("proprietary", pref, view.SegmentShare, 1996, eco, state);
            float risc = Att("risc", pref, view.SegmentShare, 1996, eco, state);
            Assert.True(risc >= 1.10f * proprietary, "RISC passes the margin (" + risc + " against " + proprietary + ")");

            AiDecision poor = DecideFull(view, 50, eco, pref);
            Assert.Null(poor.NewActive, "FR-14 item 2: the support of the best must be at least 35");
            Assert.NotNull(poor.Tiers);
            Assert.Equal(SupportTier.Low, poor.Tiers["risc"], "FR-14 item 3: a candidate whose ecosystem is at least 0.2 is funded at 'low'");
            Assert.Equal(SupportTier.Normal, poor.Tiers["proprietary"], "the family it designs on is funded at 'normal'");
        }

        [Test]
        public void ANicheCandidateThatTheCompanyLikesLess_IsNotFunded_AndALikedOneIs()
        {
            // Both families weigh 1.0 for Motorola: a candidate it likes as much as its own family is funded even without an ecosystem.
            CompanyArchState liked = Full("proprietary", "risc", 30f, SupportTier.Off);
            AiView likedView = View(liked, 1996);
            likedView.UniqueId = Motorola;
            likedView.SegmentShare = new[] { 0.8f, 0f, 0f, 0.2f };
            AiDecision committed = DecideFull(likedView, Motorola, Eco("proprietary", 0.20f, "risc", 0.10f));
            Assert.Null(committed.NewActive);
            Assert.Equal(SupportTier.Normal, committed.Tiers["risc"], "a family researched to move to it is funded like the active one until its support allows the move");

            // A company that likes the candidate a little less than what it designs on, in a niche (E under 0.2): no money for it.
            var pref = new AiPreference { Id = "custom", UniqueId = 50, LagYears = 0 };
            pref.Weights["proprietary"] = 1.0f;
            pref.Weights["risc"] = 0.99f;
            CompanyArchState lesser = Full("proprietary", "risc", 34f, SupportTier.Off);
            AiView lesserView = View(lesser, 2000);
            lesserView.UniqueId = 50;
            lesserView.SegmentShare = new[] { 1f, 0f, 0f, 0f };
            Dictionary<string, float> eco = Eco("proprietary", 0.10f, "risc", 0.10f);

            float proprietary = Att("proprietary", pref, lesserView.SegmentShare, 2000, eco, lesser);
            float risc = Att("risc", pref, lesserView.SegmentShare, 2000, eco, lesser);
            Assert.True(risc >= 1.10f * proprietary, "RISC passes the margin (" + risc + " against " + proprietary + ") but its support 34 is under 35");

            AiDecision niche = DecideFull(lesserView, 50, eco, pref);
            Assert.Null(niche.NewActive);
            Assert.False(niche.Tiers != null && niche.Tiers.ContainsKey("risc"), "a niche ISA that is liked less than the own family stays 'off'");
        }

        [Test]
        public void AFamilyResearchedFromScratch_IsFundedUntilItsSupportAllowsTheMove_IntelOnX86()
        {
            // The loop of the service, on the pure pieces: Intel has the proprietary ISA at 60 and has just finished x86 (support 30, no ecosystem at all yet).
            // Without funding x86 would decay below 35 and Intel would never move; with it the support climbs and Intel moves within a year.
            ArchCatalog catalog = Catalog();
            AiPreference intel = PreferenceOf(catalog, Intel);
            SupportConfig support = SupportCfg();
            CompanyArchState state = Full("proprietary", "x86", 30f, SupportTier.Off);
            state.Families["proprietary"].Tier = SupportTier.Normal;

            Dictionary<string, float> eco = Eco("proprietary", 1f);   // nobody sells an x86 CPU yet
            int month = 0;
            for (; month < 24 && state.Active == "proprietary"; month++)
            {
                AiView view = View(state, 1981);
                view.UniqueId = Intel;
                view.SegmentShare = new[] { 0.25f, 0.25f, 0.25f, 0.25f };
                AiDecision decision = ArchAiPolicy.DecideFull(view, intel, catalog, Config(), eco, Rating(), support);

                if (decision.Tiers != null)
                {
                    foreach (KeyValuePair<string, SupportTier> pair in decision.Tiers) SupportCycle.SetTier(state, catalog, pair.Key, pair.Value, false);
                }
                if (decision.NewActive != null) state.Active = decision.NewActive;
                SupportCycle.RunMonth(state, catalog, support, 1981, 1f, float.PositiveInfinity);
            }

            Assert.Equal("x86", state.Active, "Intel moved to x86 (after " + month + " months)");
            Assert.True(month <= 12, "within a year of finishing the research: " + month);
            Assert.True(state.Families["x86"].Support >= 35f, "the move needed the 35");
        }

        // ---- a family the company researched to move to (TSK-029: found by the balance simulation) ----

        [Test]
        public void AFamilyTheCompanyLikesAsMuchAsItsOwn_IsBroughtUpToTheLevelOfTheActiveOne_EvenWhenItIsNotTheBest()
        {
            // Texas Instruments weighs ARM and its own ISA the same (1.0). ARM has just been researched: support 30, a thin ecosystem. It is not the most attractive
            // family yet, so it is not "the candidate" of FR-14 item 2 - but without money it would decay to nothing and the research would be wasted.
            CompanyArchState state = Full("proprietary", "arm", 30f, SupportTier.Off);
            state.Families["proprietary"].Tier = SupportTier.Normal;
            AiView view = View(state, 1993);
            view.UniqueId = TexasInstruments;
            view.SegmentShare = new[] { 0f, 0.05f, 0.30f, 0.65f };
            Dictionary<string, float> eco = Eco("proprietary", 0.30f, "arm", 0.05f);

            AiDecision rich = DecideFull(view, TexasInstruments, eco);
            Assert.Equal(SupportTier.Normal, rich.Tiers["arm"], "it is funded like the family the company designs on");
            Assert.Null(rich.NewActive, "support 30 is under the 35 the move needs");

            view.Money = 1f;   // no money for twelve months of the 'normal' upkeep
            view.State.Families["proprietary"].Tier = SupportTier.Low;
            AiDecision poor = DecideFull(view, TexasInstruments, eco);
            Assert.Equal(SupportTier.Low, poor.Tiers["arm"], "a poor company funds it at 'low'");
        }

        [Test]
        public void AFamilyTheCompanyLikesLess_IsNotPursued_ItDecays()
        {
            // Intel weighs ARM at 0.1 against 1.0 for x86: researched because there was nothing else, never funded.
            CompanyArchState state = Full("x86", "x86", 70f, SupportTier.Normal, "arm", 40f, SupportTier.Normal);
            state.Families["proprietary"].Tier = SupportTier.Normal;   // a left-over from the time it was active
            AiView view = View(state, 1990);
            view.UniqueId = Intel;
            AiDecision decision = DecideFull(view, Intel, Eco("x86", 0.57f, "proprietary", 0.30f, "arm", 0.05f));
            Assert.Equal(SupportTier.Off, decision.Tiers["arm"]);
            Assert.True(!decision.Tiers.ContainsKey("x86"), "the active family already has its tier");
            Assert.Equal(SupportTier.Off, decision.Tiers["proprietary"], "the family the company left is not pursued either (Intel weighs it 0.2)");
        }

        [Test]
        public void TexasInstruments_MovesToArm_OnceItsSupportHasCaughtUp_AndNotBefore35()
        {
            // The loop of the service on the pure pieces: ARM was researched with support 30 next to a proprietary ISA at 60; the company funds both and moves within months.
            ArchCatalog catalog = Catalog();
            AiPreference ti = PreferenceOf(catalog, TexasInstruments);
            SupportConfig support = SupportCfg();
            CompanyArchState state = Full("proprietary", "arm", 30f, SupportTier.Off);
            state.Families["proprietary"].Tier = SupportTier.Normal;

            Dictionary<string, float> eco = Eco("proprietary", 0.20f, "arm", 0.25f, "x86", 0.5f);   // other embedded companies sell ARM already
            int month = 0;
            for (; month < 36 && state.Active == "proprietary"; month++)
            {
                AiView view = View(state, 1994);
                view.UniqueId = TexasInstruments;
                view.SegmentShare = new[] { 0f, 0.05f, 0.30f, 0.65f };
                AiDecision decision = ArchAiPolicy.DecideFull(view, ti, catalog, Config(), eco, Rating(), support);

                if (decision.Tiers != null)
                {
                    foreach (KeyValuePair<string, SupportTier> pair in decision.Tiers) SupportCycle.SetTier(state, catalog, pair.Key, pair.Value, false);
                }
                if (decision.NewActive != null)
                {
                    Assert.True(state.Families["arm"].Support >= 35f, "FR-14 item 2: the move needs the support of the family at 35 (it is " + state.Families["arm"].Support + ")");
                    state.Active = decision.NewActive;
                }
                SupportCycle.RunMonth(state, catalog, support, 1994, 1f, float.PositiveInfinity);
            }

            Assert.Equal("arm", state.Active, "Texas Instruments moved to ARM (after " + month + " months)");
            Assert.True(month <= 12, "within a year: " + month);
        }

        [Test]
        public void TheTiers_TheActiveFamilyIsNormalWhenTwelveMonthsOfUpkeepAreInTheBank_ElseLow_TheRestAreOff()
        {
            ArchCatalog catalog = Catalog();
            ArchFamily x86 = catalog.Get("x86");
            float upkeep = SupportMath.MonthlyUpkeep(x86.Support, SupportTier.Normal, 1990, 1.2f, SupportCfg());
            Assert.True(upkeep > 0f);

            Func<float, AiDecision> decide = money =>
            {
                CompanyArchState state = Full("x86", "x86", 60f, SupportTier.Off);
                state.Families["proprietary"].Tier = SupportTier.Normal;   // a left-over from the time it was active
                AiView view = View(state, 1990, money);
                view.UniqueId = Intel;
                view.Inflation = 1.2f;
                return DecideFull(view, Intel, Eco("x86", 0.6f, "proprietary", 0.4f));
            };

            AiDecision rich = decide(12f * upkeep + 1000f);
            Assert.Equal(SupportTier.Normal, rich.Tiers["x86"], "the money covers twelve months of the 'normal' upkeep");
            Assert.Equal(SupportTier.Off, rich.Tiers["proprietary"], "the family it left is switched off");

            AiDecision poor = decide(12f * upkeep - 1000f);
            Assert.Equal(SupportTier.Low, poor.Tiers["x86"], "a little short: 'low'");

            // Only changes are listed: a company that already has the tiers is asked for nothing.
            CompanyArchState settled = Full("x86", "x86", 60f, SupportTier.Normal);
            AiView view2 = View(settled, 1990, 1e12f);
            view2.UniqueId = Intel;
            view2.Inflation = 1.2f;
            AiDecision same = DecideFull(view2, Intel, Eco("x86", 0.6f));
            Assert.True(same.Tiers == null || same.Tiers.Count == 0, "the tiers are already as they should be");
        }

        [Test]
        public void ANotResearchedFamily_GetsNoTier_AndAFamilyOfNoWeightIsNeverTheBest()
        {
            CompanyArchState state = Full("proprietary");
            AiView view = View(state, 1996);
            view.UniqueId = Motorola;
            AiDecision decision = DecideFull(view, Motorola, Eco("proprietary", 1f));
            Assert.False(decision.Tiers != null && decision.Tiers.ContainsKey("risc"), "RISC is not researched: there is nothing to fund");
            Assert.Null(decision.NewActive);
        }

        [Test]
        public void AnActiveFamilyThatIsNotResearched_IsReplacedAtOnce_WithoutTheMargin()
        {
            CompanyArchState state = Full("x86", "arm", 50f, SupportTier.Off);   // the active family x86 is not in the state
            AiView view = View(state, 1996);
            view.UniqueId = Intel;
            AiDecision decision = DecideFull(view, Intel, Eco("proprietary", 0.5f, "arm", 0.5f));
            Assert.NotNull(decision.NewActive, "a company designs on what it has");
            Assert.True(decision.NewActive == "proprietary" || decision.NewActive == "arm");

            state.Active = null;   // before the seed
            Assert.NotNull(DecideFull(view, Intel, null).NewActive);
        }

        [Test]
        public void WithoutAnEcosystemMap_TheFactorsAreOne_AndTheSegmentSharesFallBackToAQuarter()
        {
            ArchCatalog catalog = Catalog();
            float noMap = ArchAiPolicy.Attractiveness(catalog.Get("x86"), 1f, null, 1996, -1f, 50f, Rating());
            float quarters = ArchAiPolicy.Attractiveness(catalog.Get("x86"), 1f, SegmentShares.Equal(), 1996, -1f, 50f, Rating());
            Assert.Near(quarters, noMap, 1e-6, "no shares: a quarter each");
            Assert.Near((1.05 + 1.08 + 1.05 + 0.95) / 4, noMap, 1e-5, "the mean affinity of x86, fade and the factors x1 at support 50");

            Assert.Equal(0f, ArchAiPolicy.Attractiveness(null, 1f, null, 1996, 0f, 50f, Rating()));
            Assert.Equal(0f, ArchAiPolicy.Attractiveness(catalog.Get("x86"), 0f, null, 1996, 0f, 50f, Rating()), "a weight of 0 is no wish");
            Assert.Equal(0f, ArchAiPolicy.Attractiveness(catalog.Get("x86"), -1f, null, 1996, 0f, 50f, Rating()));
            Assert.Near(noMap, ArchAiPolicy.Attractiveness(catalog.Get("x86"), 1f, new[] { 1f, 1f }, 1996, -1f, 50f, Rating()), 1e-6, "an unusable array counts as a quarter each");
        }

        [Test]
        public void TheAttractiveness_FollowsTheShares_TheAgeing_TheEcosystemAndTheSupport()
        {
            ArchCatalog catalog = Catalog();
            ArchFamily proprietary = catalog.Get("proprietary");
            RatingConfig rating = Rating();

            // All sales in the top segment in 1996: the proprietary ISA is at 0.9 there, in industry at 1.0.
            Assert.Near(0.9, ArchAiPolicy.Attractiveness(proprietary, 1f, new[] { 1f, 0f, 0f, 0f }, 1996, -1f, 50f, rating), 1e-5);
            Assert.Near(1.0, ArchAiPolicy.Attractiveness(proprietary, 1f, new[] { 0f, 0f, 0f, 1f }, 1996, -1f, 50f, rating), 1e-5);
            // The weight is a plain factor.
            Assert.Near(2.0, ArchAiPolicy.Attractiveness(proprietary, 2f, new[] { 0f, 0f, 0f, 1f }, 1996, -1f, 50f, rating), 1e-5);
            // Support 100 and a full ecosystem: 1.10 × 1.05.
            Assert.Near(1.10 * 1.05, ArchAiPolicy.Attractiveness(proprietary, 1f, new[] { 0f, 0f, 0f, 1f }, 1996, 1f, 100f, rating), 1e-5);
            // The shares are normalised: 2:2 is the same as 0.5:0.5.
            Assert.Near(
                ArchAiPolicy.Attractiveness(proprietary, 1f, new[] { 0.5f, 0f, 0f, 0.5f }, 1996, -1f, 50f, rating),
                ArchAiPolicy.Attractiveness(proprietary, 1f, new[] { 2f, 0f, 0f, 2f }, 1996, -1f, 50f, rating), 1e-5);
        }

        [Test]
        public void TheResearchOfStageTwo_WeighsTheWeightByOnePlusTheEcosystem()
        {
            ArchCatalog catalog = Catalog();
            var pref = new AiPreference { Id = "custom", UniqueId = 50, LagYears = 0 };
            pref.Weights["arm"] = 1.0f;
            pref.Weights["riscv"] = 0.8f;

            CompanyArchState state = Full("proprietary");
            AiView view = View(state, 2016);
            view.UniqueId = 50;

            Assert.Equal("arm", DecideFull(view, 50, null, pref).StartResearch, "no ecosystem: the larger weight wins");
            Assert.Equal("arm", DecideFull(view, 50, Eco("riscv", 0.1f), pref).StartResearch, "0.8 × 1.1 = 0.88 is still below 1.0");
            Assert.Equal("riscv", DecideFull(view, 50, Eco("riscv", 0.5f), pref).StartResearch, "FR-14 item 4: 0.8 × 1.5 = 1.2 beats 1.0");
            Assert.Equal("arm", ArchAiPolicy.PickCandidate(catalog, pref, state.Families, 2016).Id, "the four-argument form is the stage 1 rule");
        }

        [Test]
        public void TheResearchPart_IsTheSameAsAtStageOne_WhenThereIsNoEcosystem()
        {
            ArchCatalog catalog = Catalog();
            int[] companies = { Intel, Amd, TexasInstruments, Ibm, Cyrix, Motorola, 5, 99 };
            int[] years = { 1975, 1979, 1985, 1990, 2003, 2011, 2016 };
            foreach (int uniqueId in companies)
            {
                foreach (int year in years)
                {
                    foreach (float money in new[] { -5f, 1e6f, RichMoney })
                    {
                        foreach (int left in new[] { -1, 10, 90 })
                        {
                            CompanyArchState a = StateWith("proprietary");
                            CompanyArchState b = StateWith("proprietary");
                            AiView va = View(a, year, money, left);
                            AiView vb = View(b, year, money, left);
                            va.UniqueId = vb.UniqueId = uniqueId;

                            AiDecision mvp = ArchAiPolicy.DecideMvp(va, PreferenceOf(catalog, uniqueId), catalog, Config());
                            AiDecision full = ArchAiPolicy.DecideFull(vb, PreferenceOf(catalog, uniqueId), catalog, Config(), null, Rating(), SupportCfg());
                            string label = uniqueId + "/" + year + "/" + money + "/" + left;
                            Assert.Equal(mvp.StartResearch, full.StartResearch, label);
                            Assert.Equal(mvp.Funding, full.Funding, label);
                            Assert.Equal(mvp.Pause, full.Pause, label);
                            Assert.Equal(mvp.DeferInterrupt, full.DeferInterrupt, label);
                        }
                    }
                }
            }
        }

        [Test]
        public void TheFullDecision_PausesAndWaitsLikeTheFirst()
        {
            CompanyArchState running = Full("proprietary");
            running.CurrentResearch = "x86";
            AiView broke = View(running, 1980, -10f);
            broke.UniqueId = Intel;
            Assert.True(DecideFull(broke, Intel).Pause, "FR-7 item 4");

            CompanyArchState idle = Full("proprietary");
            AiView busy = View(idle, 1979, RichMoney, 12);
            busy.UniqueId = Intel;
            AiDecision wait = DecideFull(busy, Intel);
            Assert.True(wait.DeferInterrupt, "AC-7.3: 12 days left, the technology is not interrupted");
            Assert.Null(wait.StartResearch);
        }

        [Test]
        public void TheFullDecision_IsDeterministic_NoMatterHowTheFamiliesWereAdded()
        {
            Func<bool, AiDecision> decide = reverse =>
            {
                var state = new CompanyArchState(1, false) { Active = "proprietary" };
                var entries = new List<FamilyState>
                {
                    new FamilyState("proprietary") { Researched = true, Support = 60f },
                    new FamilyState("risc") { Researched = true, Support = 40f },
                    new FamilyState("x86") { Researched = true, Support = 45f },
                    new FamilyState("arm") { Researched = true, Support = 45f },
                };
                if (reverse) entries.Reverse();
                foreach (FamilyState entry in entries) state.Families[entry.Id] = entry;

                AiView view = View(state, 1996);
                view.UniqueId = Motorola;
                view.SegmentShare = new[] { 0.5f, 0.2f, 0.1f, 0.2f };
                return DecideFull(view, Motorola, Eco("proprietary", 0.2f, "risc", 0.3f, "x86", 0.4f, "arm", 0.1f));
            };

            AiDecision a = decide(false);
            AiDecision b = decide(true);
            AiDecision again = decide(false);
            Assert.Equal(a.NewActive, b.NewActive);
            Assert.Equal(a.NewActive, again.NewActive);
            Assert.Equal(a.StartResearch, b.StartResearch);
            Assert.Equal(a.Tiers == null ? 0 : a.Tiers.Count, b.Tiers == null ? 0 : b.Tiers.Count);
            if (a.Tiers != null)
            {
                foreach (KeyValuePair<string, SupportTier> pair in a.Tiers) Assert.Equal(pair.Value, b.Tiers[pair.Key], pair.Key);
            }
        }

        [Test]
        public void TheFullDecision_SkipsFoundriesAndBankruptCompanies_AndNeverThrowsOnNothing()
        {
            ArchCatalog catalog = Catalog();
            CompanyArchState state = Full("proprietary");
            AiView foundry = View(state, 1996);
            foundry.IsFoundry = true;
            AiView bankrupt = View(state, 1996);
            bankrupt.IsBankrupt = true;
            Assert.True(DecideFull(foundry, 8).IsEmpty);
            Assert.True(DecideFull(bankrupt, 5).IsEmpty);
            Assert.True(ArchAiPolicy.DecideFull(null, null, catalog, Config(), null, null).IsEmpty);
            Assert.True(ArchAiPolicy.DecideFull(View(state, 1996), null, null, Config(), null, null).IsEmpty);

            // A view without a state (a company the seed has not reached) gets no tiers and no switch.
            AiView unsown = View(null, 1996);
            AiDecision decision = ArchAiPolicy.DecideFull(unsown, PreferenceOf(catalog, Intel), catalog, null, null, null);
            Assert.Null(decision.Tiers);
        }

        [Test]
        public void AnOverrideOfTheWeight_ChangesWhatTheCompanyChoosesAndWhereItMoves()
        {
            ArchCatalog catalog = Catalog();
            AiPreference motorola = PreferenceOf(catalog, Motorola);
            CompanyArchState state = Full("proprietary", "risc", 45f, SupportTier.Off, "arm", 45f, SupportTier.Off);
            AiView view = View(state, 1996);
            view.UniqueId = Motorola;
            view.SegmentShare = new[] { 0.25f, 0.25f, 0.25f, 0.25f };
            Dictionary<string, float> eco = Eco("proprietary", 0.3f, "risc", 0.3f, "arm", 0.1f);

            // The record gives RISC no advantage that clears the margin; an override that makes it the favourite does.
            Assert.Null(DecideFull(view, Motorola, eco).NewActive);

            var overrides = new List<AiOverrideSave>();
            Assert.Equal(AiOverrideResult.Set, AiOverrides.Set(overrides, 1, "risc", 3f, ArchRegistry.MaxOverrides));
            AiPreference effective = AiOverrides.Apply(motorola, 1, overrides);
            Assert.Equal("risc", DecideFull(view, Motorola, eco, effective).NewActive, "SetAiWeightOverride: RISC at 3");

            // And back: taking the override away restores the record.
            Assert.Equal(AiOverrideResult.Removed, AiOverrides.Set(overrides, 1, "risc", null, ArchRegistry.MaxOverrides));
            Assert.Null(DecideFull(view, Motorola, eco, AiOverrides.Apply(motorola, 1, overrides)).NewActive);

            // A weight of 0 on the active family takes the company away from it.
            AiOverrides.Set(overrides, 1, "proprietary", 0f, ArchRegistry.MaxOverrides);
            Assert.Equal("risc", DecideFull(view, Motorola, eco, AiOverrides.Apply(motorola, 1, overrides)).NewActive, "it no longer likes what it designs on");
            Assert.Equal(1f, motorola.WeightOf("proprietary"), "the record of the table is untouched");
        }
    }
}
