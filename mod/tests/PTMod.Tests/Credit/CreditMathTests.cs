using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Credit;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Credit
{
    /// <summary>
    /// The default threshold of FR-24 (TSK-031, TSK-058): <see cref="CreditMath"/> is the arithmetic of P9 (global part in <c>CentralBank.DebtLimit</c>),
    /// P10 (company part in the game's comparisons) and <c>ModCredit</c> (what features read). <see cref="World"/> plays the three on top of the
    /// channel's semantics (a company question also sees the global entries), so the acceptance criteria AC-24.1-24.3 are checked the way the game
    /// and <c>ModCredit</c> compute them. The last tests read the sources: the hooks and <c>ModCredit</c> share the function, nobody else reads <c>DebtLimit</c>.
    /// </summary>
    [Covers(typeof(CreditMath))]
    public class CreditMathTests
    {
        private const float Million = 1e6f;

        /// <summary>
        /// The game and the mod around one bank: the game's own limit, the product of the global entries of <c>bank.creditLimit</c>, the products of
        /// each company's own entries, and whether P10 is in the game. The registry answers a company question with the global entries times the
        /// company's own (scope rules of FR-12), which is what <see cref="CreditMath.CompanyMultiplier"/> has to undo.
        /// </summary>
        private sealed class World
        {
            public float GameLimit = 100 * Million;   // CentralBank.UpdateCreditLimit before P9
            public float Global = 1f;
            public bool PerCompanyActive = true;
            private readonly Dictionary<string, float> own = new Dictionary<string, float>();

            public World Own(string company, float product)
            {
                own[company] = product;
                return this;
            }

            /// <summary><c>CentralBank.DebtLimit</c> after P9.</summary>
            public float DebtLimit
            {
                get { return CreditMath.GlobalDebtLimit(GameLimit, Global); }
            }

            /// <summary><c>M(bank.creditLimit, company)</c> as the registry gives it: the global entries and the company's own.</summary>
            public float Product(string company)
            {
                float product;
                return own.TryGetValue(company, out product) ? Global * product : Global * 1f;
            }

            /// <summary><c>ModCredit.ThresholdOf(company)</c>.</summary>
            public float ThresholdOf(string company)
            {
                return CreditMath.Threshold(DebtLimit, CreditMath.CompanyMultiplier(Product(company), Global), PerCompanyActive);
            }

            /// <summary><c>ModCredit.PlayerDebtLimit()</c> (and what P11 shows).</summary>
            public float PlayerDebtLimit()
            {
                return CreditMath.LimitOf(ThresholdOf("player"));
            }

            /// <summary>
            /// What <c>BankruptcyHandler.TickBankruptcies</c> compares with: <c>V_0 = −DebtLimit</c>, and with P10 every load of <c>V_0</c> goes through
            /// <c>ModCreditHooks.Threshold(V_0, company)</c>.
            /// </summary>
            public float GameThreshold(string company)
            {
                float v0 = -DebtLimit;
                return PerCompanyActive ? Hook(v0, company) : v0;
            }

            /// <summary><c>ModCreditHooks.Threshold(V_0, company)</c>.</summary>
            public float Hook(float v0, string company)
            {
                return CreditMath.Threshold(-v0, CreditMath.CompanyMultiplier(Product(company), Global), true);
            }
        }

        private static void AssertPositiveZero(float value, string message)
        {
            Assert.True(value == 0f && float.IsPositiveInfinity(1f / value), message + ": expected +0, got " + value.ToString("R"));
        }

        private static int Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        // ---- the acceptance criteria ----

        [Test]
        public void Ac241_P10Applied_PlayerHalf_AiFull()
        {
            World world = new World().Own("player", 0.5f).Own("ai:3", 1f);

            Assert.Near(-50 * Million, world.ThresholdOf("player"), 0, "ThresholdOf(player)");
            Assert.Near(50 * Million, world.PlayerDebtLimit(), 0, "PlayerDebtLimit()");
            Assert.Near(-100 * Million, world.ThresholdOf("ai:3"), 0, "ThresholdOf(ai)");
            Assert.Near(-50 * Million, world.GameThreshold("player"), 0, "the game applies the same to the player");
            Assert.Near(-100 * Million, world.GameThreshold("ai:3"), 0, "and to the AI");
        }

        [Test]
        public void Ac242_P10NotApplied_EveryoneHasTheGlobalThreshold()
        {
            World world = new World { PerCompanyActive = false }.Own("player", 0.5f).Own("ai:3", 1.5f);

            Assert.Near(-100 * Million, world.ThresholdOf("player"), 0, "player");
            Assert.Near(-100 * Million, world.ThresholdOf("ai:3"), 0, "ai");
            Assert.Near(100 * Million, world.PlayerDebtLimit(), 0, "the interface shows the global limit");
            Assert.Near(-100 * Million, world.GameThreshold("player"), 0, "the game compares with -DebtLimit");
        }

        [Test]
        public void Ac243_GlobalMultiplierOnly_BothModesGiveTheLimitAfterP9()
        {
            foreach (bool active in new[] { true, false })
            {
                World world = new World { Global = 0.8f, PerCompanyActive = active };
                string mode = active ? "P10 applied" : "P10 not applied";

                Assert.Near(80 * Million, world.DebtLimit, 0, mode + ": DebtLimit after P9");
                Assert.Near(-80 * Million, world.ThresholdOf("player"), 0, mode + ": player");
                Assert.Near(-80 * Million, world.ThresholdOf("ai:0"), 0, mode + ": ai");
                Assert.Near(80 * Million, world.PlayerDebtLimit(), 0, mode + ": PlayerDebtLimit");
                Assert.Near(-80 * Million, world.GameThreshold("ai:0"), 0, mode + ": the game, without a second x0.8");
            }
        }

        [Test]
        public void Multiplier15_RaisesTheCompanyLimit()
        {
            World world = new World().Own("player", 1.5f);

            Assert.Near(-150 * Million, world.ThresholdOf("player"), 0);
            Assert.Near(150 * Million, world.PlayerDebtLimit(), 0);
            Assert.Near(-100 * Million, world.ThresholdOf("ai:1"), 0, "a company without entries keeps the global threshold");
        }

        [Test]
        public void GlobalAndCompanyEntries_EachAppliedOnce()
        {
            World world = new World { Global = 0.8f }.Own("player", 0.5f);

            Assert.Near(80 * Million, world.DebtLimit, 0, "global part in DebtLimit");
            Assert.Near(0.5, CreditMath.CompanyMultiplier(world.Product("player"), world.Global), 1e-6, "company part = 0.4 / 0.8");
            Assert.Near(-40 * Million, world.ThresholdOf("player"), 10, "0.8 x 0.5 of 100 million");
            Assert.Near(-80 * Million, world.ThresholdOf("ai:2"), 0, "the AI: global part only");

            world.PerCompanyActive = false;
            Assert.Near(-80 * Million, world.ThresholdOf("player"), 0, "without P10 the player is back to the global threshold");
        }

        [Test]
        public void ZeroLimit_GivesZero_NeverMinusZero()
        {
            AssertPositiveZero(CreditMath.Threshold(0f, 0.5f, true), "per company, x0.5");
            AssertPositiveZero(CreditMath.Threshold(0f, 1.5f, true), "per company, x1.5");
            AssertPositiveZero(CreditMath.Threshold(0f, 0.5f, false), "global");
            AssertPositiveZero(CreditMath.Threshold(100 * Million, 0f, true), "a zero company part");
            AssertPositiveZero(CreditMath.LimitOf(0f), "LimitOf(0)");
            AssertPositiveZero(CreditMath.LimitOf(-0f), "LimitOf(-0)");
        }

        // ---- Threshold ----

        [Test]
        public void Threshold_PerCompany_IsMinusLimitTimesMultiplier()
        {
            Assert.Near(-50 * Million, CreditMath.Threshold(100 * Million, 0.5f, true), 0);
            Assert.Near(-150 * Million, CreditMath.Threshold(100 * Million, 1.5f, true), 0);
            Assert.Near(-100 * Million, CreditMath.Threshold(100 * Million, 1f, true), 0);
            Assert.Near(-1 * Million, CreditMath.Threshold(1 * Million, 1f, true), 0, "the game's floor");
        }

        [Test]
        public void Threshold_Global_IgnoresTheMultiplier()
        {
            foreach (float multiplier in new[] { 0f, 0.5f, 1.5f, 10f, float.NaN, float.PositiveInfinity, -2f })
            {
                Assert.Near(-100 * Million, CreditMath.Threshold(100 * Million, multiplier, false), 0, "x" + multiplier.ToString("R"));
            }
        }

        [Test]
        public void Threshold_UnusableMultiplier_CountsAsOne()
        {
            foreach (float multiplier in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f })
            {
                Assert.Near(-100 * Million, CreditMath.Threshold(100 * Million, multiplier, true), 0, "x" + multiplier.ToString("R"));
            }
        }

        [Test]
        public void Threshold_DecidesTheDefaultLikeTheGame()
        {
            // The game: default when MoneyAmount < threshold (bge.un over V_0 in TickBankruptcies).
            World world = new World().Own("player", 0.5f);
            float player = world.GameThreshold("player");
            float ai = world.GameThreshold("ai:0");

            Assert.True(-60 * Million < player, "player at -60 million is in default with a half limit");
            Assert.False(-40 * Million < player, "player at -40 million is not");
            Assert.False(-60 * Million < ai, "an AI at -60 million is not: its limit is 100 million");
        }

        [Test]
        public void HookAndModCredit_AgreeBitForBit()
        {
            // ModCreditHooks.Threshold gets V_0 = -DebtLimit, ModCredit reads DebtLimit: the two must be the same float, not just close.
            float[] limits = { 1 * Million, 123456.7f * 10f, 33333333.3f, 100 * Million, 2.5e9f, 0f };
            float[] multipliers = { 0.1f, 1f / 3f, 0.5f, 0.8f, 1f, 1.25f, 1.5f, 7.77f, 10f };
            foreach (float limit in limits)
            {
                foreach (float multiplier in multipliers)
                {
                    float fromModCredit = CreditMath.Threshold(limit, multiplier, true);
                    float fromHook = CreditMath.Threshold(-(-limit), multiplier, true);
                    Assert.Equal(Bits(fromModCredit), Bits(fromHook), "limit " + limit.ToString("R") + " x" + multiplier.ToString("R"));
                    Assert.Equal(Bits(-fromModCredit == 0f ? 0f : -fromModCredit), Bits(CreditMath.LimitOf(fromModCredit)), "LimitOf is the negation");
                }
            }
        }

        // ---- GlobalDebtLimit (P9) ----

        [Test]
        public void GlobalDebtLimit_MultipliesWithTheFloorOfTheGame()
        {
            Assert.Near(80 * Million, CreditMath.GlobalDebtLimit(100 * Million, 0.8f), 0, "x0.8");
            Assert.Near(30 * Million, CreditMath.GlobalDebtLimit(20 * Million, 1.5f), 0, "x1.5");
            Assert.Near(1 * Million, CreditMath.GlobalDebtLimit(5 * Million, 0.1f), 0, "0.5 million is below the floor");
            Assert.Near(1 * Million, CreditMath.GlobalDebtLimit(1 * Million, 0.5f), 0, "the floor stays the floor");
            Assert.Near(1 * Million, CreditMath.GlobalDebtLimit(5 * Million, 0f), 0, "x0 gives the floor");
            Assert.Near(1e6, CreditMath.MinDebtLimit, 0, "the floor of CentralBank.UpdateCreditLimit");
        }

        [Test]
        public void GlobalDebtLimit_OneOrUnusable_LeavesTheGamesValue()
        {
            Assert.Near(20 * Million, CreditMath.GlobalDebtLimit(20 * Million, 1f), 0, "x1");
            Assert.Near(0.5 * Million, CreditMath.GlobalDebtLimit(0.5f * Million, 1f), 0, "x1 never adds a floor of its own");
            foreach (float multiplier in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            {
                Assert.Near(20 * Million, CreditMath.GlobalDebtLimit(20 * Million, multiplier), 0, "x" + multiplier.ToString("R"));
            }
        }

        // ---- CompanyMultiplier ----

        [Test]
        public void CompanyMultiplier_IsTheCompanyOverTheGlobalProduct()
        {
            Assert.Near(0.5, CreditMath.CompanyMultiplier(0.4f, 0.8f), 1e-6, "0.4 / 0.8");
            Assert.Near(1.5, CreditMath.CompanyMultiplier(1.5f, 1f), 0, "no global entries");
            Assert.Equal(1f, CreditMath.CompanyMultiplier(0.8f, 0.8f), "only global entries: exactly 1");
            Assert.Equal(1f, CreditMath.CompanyMultiplier(0f, 0f), "0 and 0");
        }

        [Test]
        public void CompanyMultiplier_UnusableInputs()
        {
            Assert.Near(0.5, CreditMath.CompanyMultiplier(0.5f, 0f), 0, "a zero global product is no divisor");
            Assert.Near(0.5, CreditMath.CompanyMultiplier(0.5f, float.NaN), 0, "nor is NaN");
            Assert.Near(0.5, CreditMath.CompanyMultiplier(0.5f, -1f), 0, "nor a negative one");
            Assert.Near(1.25, CreditMath.CompanyMultiplier(float.NaN, 0.8f), 1e-6, "an unusable company product counts as 1");
            Assert.Equal(1f, CreditMath.CompanyMultiplier(5f, 1e-40f), "an overflowing quotient counts as 1");
        }

        [Test]
        public void IsUsable_FiniteAndNotNegative()
        {
            Assert.True(CreditMath.IsUsable(0f));
            Assert.True(CreditMath.IsUsable(1.5f));
            Assert.False(CreditMath.IsUsable(-0.1f));
            Assert.False(CreditMath.IsUsable(float.NaN));
            Assert.False(CreditMath.IsUsable(float.PositiveInfinity));
            Assert.False(CreditMath.IsUsable(float.NegativeInfinity));
        }

        // ---- the sources (the game-side classes cannot run here) ----

        private static readonly Regex DebtLimitRead = new Regex(@"\.\s*DebtLimit\b", RegexOptions.Compiled);

        private static readonly string[] DebtLimitReaders =
        {
            "Core/Credit/ModCredit.cs", "Core/Modifiers/Patches/ModCreditHooks.cs", "Core/Modifiers/Patches/CreditLimitPatch.cs",
        };

        private static string Source(string relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing source " + relative);
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }

        [Test]
        public void Sources_HooksAndModCredit_ComputeTheThresholdWithCreditMath()
        {
            string credit = Source("Core/Credit/ModCredit.cs");
            string hooks = Source("Core/Modifiers/Patches/ModCreditHooks.cs");

            Assert.True(credit.Contains("CreditMath.Threshold("), "ModCredit computes through CreditMath.Threshold");
            Assert.True(credit.Contains("CreditMath.CompanyMultiplier("), "ModCredit takes the company part from CreditMath");
            Assert.True(hooks.Contains("CreditMath.Threshold("), "the P10 hook computes through CreditMath.Threshold");
            Assert.True(hooks.Contains("ModCredit.CompanyMultiplier("), "the P10 hook takes the company part from ModCredit");
            Assert.True(hooks.Contains("ModCredit.ThresholdFor("), "the P11 hook shows ModCredit's threshold");
        }

        [Test]
        public void Sources_OnlyTheCreditClasses_ReadCentralBankDebtLimit()
        {
            // FR-24 item 4: features (and the rest of the core) read ModCredit, never CentralBank.DebtLimit; the AI's reserves are not patched.
            string src = SourceScan.SrcDirOrFail();
            var offenders = new List<string>();
            int allowedReads = 0;
            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                string relative = SourceScan.Relative(src, path);
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match m in DebtLimitRead.Matches(code))
                {
                    if (Array.IndexOf(DebtLimitReaders, relative) >= 0) allowedReads++;
                    else offenders.Add(relative + ":" + SourceScan.LineOf(code, m.Index));
                }
            }
            Assert.True(offenders.Count == 0, "CentralBank.DebtLimit is read outside ModCredit and the patches P9-P11 (use ModCredit.ThresholdOf / "
                                              + "PlayerDebtLimit):\n  " + string.Join("\n  ", offenders));
            Assert.True(allowedReads >= 3, "the scan did not find the reads of ModCredit, the hooks and P9: is the scanner broken?");
        }
    }
}
