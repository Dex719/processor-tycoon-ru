using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The gates of the CPU extensions (FR-13, TSK-026): multicore and SMT give their full rating effect only with enough software support. AC-13.1 (four cores at a
    /// support of 20 are held to 0.96, through the same function the provider of <c>market.rating.cpu</c> uses), the warning of the designer ("раскроется при поддержке
    /// ≥ 40 (сейчас 20)") that names exactly the extensions the rating punishes, and the extensions the tooltip of a multicore technology talks about.
    /// </summary>
    [Covers(typeof(ExtensionWarnings), typeof(ExtensionWarning))]
    public class ExtensionWarningsTests
    {
        private static List<ExtensionDef> Extensions()
        {
            return ArchTestKit.BuiltInBalance().extensions;
        }

        private static BalanceConfig FullStage()
        {
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;
            return balance;
        }

        // ---- the warnings ----

        [Test]
        public void AC13_1_FourCoresAtSupport20_AreWarnedAbout_AndHeldTo0_96()
        {
            List<ExtensionWarning> warnings = ExtensionWarnings.For(Extensions(), 20f, 4, false);

            Assert.Equal(1, warnings.Count);
            Assert.Equal("multicore", warnings[0].Id);
            Assert.Near(40.0, warnings[0].Required, 1e-6, "'раскроется при поддержке ≥ 40'");
            Assert.Near(20.0, warnings[0].Support, 1e-6, "'(сейчас 20)'");
            Assert.Near(0.96, warnings[0].Factor, 1e-6, "half of the way from 0.92 to 1");
        }

        [Test]
        public void NoWarning_WhenTheGateIsOpen_OrTheCpuCarriesNothing()
        {
            Assert.Equal(0, ExtensionWarnings.For(Extensions(), 40f, 4, false).Count, "the required support is reached");
            Assert.Equal(0, ExtensionWarnings.For(Extensions(), 95f, 8, true).Count);
            Assert.Equal(0, ExtensionWarnings.For(Extensions(), 0f, 1, false).Count, "a single core without SMT carries no extension");
            Assert.Equal(0, ExtensionWarnings.For(null, 0f, 4, true).Count);
            Assert.Equal(0, ExtensionWarnings.For(new List<ExtensionDef>(), 0f, 4, true).Count);
            Assert.Equal(0, ExtensionWarnings.For(new List<ExtensionDef> { new ExtensionDef { id = "none", trigger = ExtensionDef.TriggerCoreCount, minCores = 2, requiredSupport = 0f, penalty = 0.5f } }, 0f, 4, false).Count,
                "a required support of 0 is no gate");
        }

        [Test]
        public void CoresAndSmt_BothAreNamed_InTheOrderOfTheConfig()
        {
            List<ExtensionWarning> warnings = ExtensionWarnings.For(Extensions(), 30f, 2, true);
            Assert.Equal(2, warnings.Count);
            Assert.Equal("multicore", warnings[0].Id);
            Assert.Equal("smt", warnings[1].Id);
            Assert.Near(50.0, warnings[1].Required, 1e-6);

            List<ExtensionWarning> smtOnly = ExtensionWarnings.For(Extensions(), 30f, 1, true);
            Assert.Equal(1, smtOnly.Count);
            Assert.Equal("smt", smtOnly[0].Id);

            // Between the two requirements: multicore (40) is open at 45, SMT (50) is not.
            List<ExtensionWarning> between = ExtensionWarnings.For(Extensions(), 45f, 4, true);
            Assert.Equal(1, between.Count);
            Assert.Equal("smt", between[0].Id);
        }

        [Test]
        public void TheSupportIsHeldToTheScale_AndABadNumberIsZero()
        {
            Assert.Near(0.0, ExtensionWarnings.For(Extensions(), -20f, 4, false)[0].Support, 1e-6);
            Assert.Near(0.0, ExtensionWarnings.For(Extensions(), float.NaN, 4, false)[0].Support, 1e-6);
            Assert.Equal(0, ExtensionWarnings.For(Extensions(), 500f, 4, false).Count);
        }

        [Test]
        public void TheWarning_NamesExactlyTheExtensionsTheRatingPunishes()
        {
            // The same function the provider uses: a warning exists if and only if f_ext < 1.
            for (int cores = 1; cores <= 8; cores++)
            {
                foreach (bool smt in new[] { false, true })
                {
                    for (float support = 0f; support <= 100f; support += 5f)
                    {
                        float factor = ArchRatingMath.ExtensionsFactor(support, cores, smt, Extensions());
                        List<ExtensionWarning> warnings = ExtensionWarnings.For(Extensions(), support, cores, smt);

                        double product = 1.0;
                        foreach (ExtensionWarning warning in warnings) product *= warning.Factor;
                        Assert.Near(factor, product, 1e-5, "cores " + cores + " smt " + smt + " support " + support);
                        Assert.Equal(factor < 0.999999f, warnings.Count > 0, "cores " + cores + " smt " + smt + " support " + support);
                    }
                }
            }
        }

        [Test]
        public void TheNameKeys_AreForTheTwoBuiltInExtensions()
        {
            Assert.Equal("arch.ext.multicore", ExtensionWarnings.NameKey("multicore"));
            Assert.Equal("arch.ext.smt", ExtensionWarnings.NameKey("smt"));
            Assert.Null(ExtensionWarnings.NameKey("other"), "an extension of a user file is shown by its id");
            Assert.Null(ExtensionWarnings.NameKey(null));
        }

        // ---- the rating through the shared function ----

        [Test]
        public void TheRatingOfAFourCoreCpu_AtSupport20_IsTheDualityOfAC13_1()
        {
            BalanceConfig balance = FullStage();
            ArchFamily x86 = ArchTestKit.BuiltInCatalog().Get("x86");
            var inputs = new RatingInputs();

            float single = RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, balance, 20f, -1f, 1, false, 1f);
            float quad = RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, balance, 20f, -1f, 4, false, 1f);
            Assert.Near(0.96, quad / single, 1e-5, "AC-13.1 end to end: the same CPU with four cores is 4 % lower");

            float quadWithSupport = RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, balance, 40f, -1f, 4, false, 1f);
            float singleWithSupport = RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, balance, 40f, -1f, 1, false, 1f);
            Assert.Near(1.0, quadWithSupport / singleWithSupport, 1e-5, "support 40: the gate is open");

            // At stage 1 the extensions are not played at all.
            BalanceConfig mvp = ArchTestKit.BuiltInBalance();
            Assert.Near(
                RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, mvp, 20f, -1f, 1, false, 1f),
                RatingForecast.One(inputs, x86, Segment.HighEnd, 1990, mvp, 20f, -1f, 4, true, 1f), 1e-6, "stage 1: x1");
        }

        [Test]
        public void TheEcosystem_EntersTheSameFunction_AtStageTwoOnly()
        {
            BalanceConfig full = FullStage();
            BalanceConfig mvp = ArchTestKit.BuiltInBalance();
            ArchFamily x86 = ArchTestKit.BuiltInCatalog().Get("x86");
            var inputs = new RatingInputs();

            float withoutEco = RatingForecast.One(inputs, x86, Segment.MidRange, 1996, full, 50f, -1f, 1, false, 1f);
            float busy = RatingForecast.One(inputs, x86, Segment.MidRange, 1996, full, 50f, 0.9f, 1, false, 1f);
            float empty = RatingForecast.One(inputs, x86, Segment.MidRange, 1996, full, 50f, 0f, 1, false, 1f);
            Assert.Near(1.045, busy / withoutEco, 1e-3, "AC-11.1: E 0.9 is x1.045");
            Assert.Near(0.95, empty / withoutEco, 1e-3, "no ecosystem at all: x0.95");

            Assert.Near(
                RatingForecast.One(inputs, x86, Segment.MidRange, 1996, mvp, 50f, -1f, 1, false, 1f),
                RatingForecast.One(inputs, x86, Segment.MidRange, 1996, mvp, 50f, 0.9f, 1, false, 1f), 1e-6, "stage 1 ignores E");
        }

        // ---- the tooltip of a technology ----

        [Test]
        public void TheTooltipOfATechnology_ListsTheExtensionsItBrings()
        {
            List<ExtensionDef> extensions = Extensions();

            List<ExtensionDef> dual = ExtensionWarnings.ForTechnology(extensions, 2);
            Assert.Equal(1, dual.Count);
            Assert.Equal("multicore", dual[0].id, "Dual Core brings multicore");
            Assert.Equal("multicore", ExtensionWarnings.ForTechnology(extensions, 8)[0].id, "Octa Core too");

            List<ExtensionDef> smt = ExtensionWarnings.ForTechnology(extensions, 0);
            Assert.Equal(1, smt.Count);
            Assert.Equal("smt", smt[0].id, "the SMT technology has no cores");

            Assert.Equal(0, ExtensionWarnings.ForTechnology(extensions, 1).Count, "one core is no extension");
            Assert.Equal(0, ExtensionWarnings.ForTechnology(null, 4).Count);

            var fourOnly = new List<ExtensionDef> { new ExtensionDef { id = "many", trigger = ExtensionDef.TriggerCoreCount, minCores = 4, requiredSupport = 60f, penalty = 0.9f } };
            Assert.Equal(0, ExtensionWarnings.ForTechnology(fourOnly, 2).Count, "Dual Core is below the 4 cores of this extension");
            Assert.Equal(1, ExtensionWarnings.ForTechnology(fourOnly, 6).Count);
        }
    }
}
