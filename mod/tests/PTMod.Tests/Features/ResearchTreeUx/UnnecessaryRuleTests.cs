using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The grey-out rule of FR-2/FR-4 against the game: <c>ResearchButton.TechnologyIsUnnecessary</c>. The oracle below is the decompiled body
    /// (code/ProcessorTycoon/ResearchSystem/ResearchButton.cs, labels IL_002f...IL_0064) written out branch by branch, so the table of the
    /// rule is compared with the game's own control flow for every company kind and all seven technology types. TSK-002.
    /// </summary>
    [Covers(typeof(UnnecessaryRule))]
    public class UnnecessaryRuleTests
    {
        /// <summary>The game's method, transcribed from the decompiled gotos (<c>type</c> is the value of <c>Technology.Type</c>).</summary>
        private static bool GameTechnologyIsUnnecessary(int type, bool isFabless, bool isFoundry)
        {
            bool v0 = false;                       // V_0 = 0
            if (isFabless)                         // if (!Player.Instance.Company.IsFabless) goto IL_0033
            {
                if (type == 1) { v0 = true; }      // if (Type == 1) goto IL_002f  ->  V_0 = 1
                else if (type == 5) { v0 = true; } // if (Type != 5) goto IL_0064; (falls to IL_002f)
                return v0;                         // IL_0064
            }
            if (!isFoundry) return v0;             // IL_0033: if (!IsFoundry) goto IL_0064
            v0 = true;                             // V_0 = 1
            if (type == 1) return false;           // if (Type == 1) goto IL_0062  ->  V_0 = 0
            if (type != 5) return v0;              // if (Type != 5) goto IL_0064
            return false;                          // IL_0062: V_0 = 0
        }

        [Test]
        public void IsUnnecessary_MatchesTheGame_ForEveryCompanyKindAndType()
        {
            bool[] flags = { false, true };
            foreach (bool fabless in flags)
            {
                foreach (bool foundry in flags)
                {
                    for (int type = 0; type <= 6; type++)
                    {
                        Assert.Equal(GameTechnologyIsUnnecessary(type, fabless, foundry),
                            UnnecessaryRule.IsUnnecessary(type, fabless, foundry),
                            "fabless=" + fabless + " foundry=" + foundry + " type=" + type);
                    }
                }
            }
        }

        [Test]
        public void IsUnnecessary_Idm_NeedsEverything()
        {
            for (int type = 0; type <= 6; type++) Assert.False(UnnecessaryRule.IsUnnecessary(type, false, false), "type " + type);
        }

        [Test]
        public void IsUnnecessary_Fabless_DoesNotNeedLithographyAndWafers()
        {
            // 0 package, 1 lithography, 2 memory, 3 frequency, 4 cache, 5 wafer size, 6 multicore
            bool[] expected = { false, true, false, false, false, true, false };
            for (int type = 0; type <= 6; type++) Assert.Equal(expected[type], UnnecessaryRule.IsUnnecessary(type, true, false), "type " + type);
        }

        [Test]
        public void IsUnnecessary_Foundry_NeedsOnlyLithographyAndWafers()
        {
            bool[] expected = { true, false, true, true, true, false, true };
            for (int type = 0; type <= 6; type++) Assert.Equal(expected[type], UnnecessaryRule.IsUnnecessary(type, false, true), "type " + type);
        }

        [Test]
        public void IsUnnecessary_BothFlags_IsTreatedAsFabless()
        {
            // The game checks IsFabless first.
            Assert.True(UnnecessaryRule.IsUnnecessary(UnnecessaryRule.Lithography, true, true));
            Assert.False(UnnecessaryRule.IsUnnecessary(0, true, true));
        }

        [Test]
        public void IsUnnecessary_AnUnknownType_FollowsTheNonManufacturingBranch()
        {
            Assert.False(UnnecessaryRule.IsUnnecessary(7, true, false), "fabless: not lithography or wafer");
            Assert.True(UnnecessaryRule.IsUnnecessary(7, false, true), "foundry: not lithography or wafer");
            Assert.Equal(1, UnnecessaryRule.Lithography);
            Assert.Equal(5, UnnecessaryRule.WaferSize);
        }
    }
}
