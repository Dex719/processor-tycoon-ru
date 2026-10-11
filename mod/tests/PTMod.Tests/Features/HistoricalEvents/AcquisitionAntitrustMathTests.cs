using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The antitrust surcharge on the price of an acquisition (TSK-028; FR-19, design.md formula 11): the table of the design (0.30 to 1.00, 0.50 to 1.25, 0.65 with a case to about 2.03,
    /// 0.97 with a case cut at 3.00), AC-19.1 to AC-19.3 over a real share cache and a real case, the switches (feature, category), the reason that appears only when the multiplier is not
    /// 1, the range [1.00, 3.00], the numbers read from <c>params.json</c>, and the wiring of the provider in the feature (one registration, the invalidations).
    /// </summary>
    [Covers(typeof(AcquisitionAntitrustMath), typeof(AcquisitionAntitrustCore), typeof(AcquisitionAntitrustParams))]
    public class AcquisitionAntitrustMathTests
    {
        private const string Player = "player";
        private const string Target = "ai:1";
        private const string Case = "eu2009";

        private static readonly AcquisitionAntitrustParams Design = new AcquisitionAntitrustParams();

        // ---- the table of the design ----

        [Test]
        public void TheTable_OfFormulaEleven_GivesTheDesignValues()
        {
            Assert.Equal(1f, AcquisitionAntitrustMath.Multiplier(0.20f, 0.10f, false, Design), "S' 0.30, no case");
            Assert.Equal(1f, AcquisitionAntitrustMath.Multiplier(0.25f, 0.15f, false, Design), "S' 0.40, no case: the soft limit itself is not charged");
            Assert.Near(1.25, AcquisitionAntitrustMath.Multiplier(0.35f, 0.15f, false, Design), 1e-5, "S' 0.50, no case");
            Assert.Near(1.50, AcquisitionAntitrustMath.Multiplier(0.40f, 0.20f, false, Design), 1e-5, "S' 0.60, no case");
            Assert.Near(2.03125, AcquisitionAntitrustMath.Multiplier(0.50f, 0.15f, true, Design), 1e-5, "S' 0.65 with a case: (1 + 2.5 x 0.25) x 1.25");
            Assert.Near(2.8125, AcquisitionAntitrustMath.Multiplier(0.70f, 0.20f, true, Design), 1e-5, "S' 0.90 with a case: (1 + 2.5 x 0.5) x 1.25");
            Assert.Equal(3f, AcquisitionAntitrustMath.Multiplier(0.80f, 0.17f, true, Design), "S' 0.97 with a case: 3.03 is cut at 3.00");
        }

        [Test]
        public void ACaseAddsTheSurcharge_EvenBelowTheSoftLimit()
        {
            Assert.Near(1.25, AcquisitionAntitrustMath.Multiplier(0.10f, 0.05f, true, Design), 1e-5, "formula 11: the surcharge multiplies 1 + slope x 0");
            Assert.Equal(1f, AcquisitionAntitrustMath.Multiplier(0.10f, 0.05f, false, Design));
        }

        [Test]
        public void WithoutACase_TheMultiplierGrowsLinearlyAboveTheSoftLimit_AndNeverPastTheCeiling()
        {
            float previous = 1f;
            for (int percent = 0; percent <= 100; percent++)
            {
                float m = AcquisitionAntitrustMath.Multiplier(percent / 100f, 0f, false, Design);
                Assert.True(m >= previous - 1e-6f, "monotonic at " + percent + "%");
                Assert.InRange(m, 1f, 3f, "range at " + percent + "%");
                previous = m;
            }
            Assert.Near(1 + 2.5 * 0.60, AcquisitionAntitrustMath.Multiplier(1f, 0f, false, Design), 1e-5, "the whole market: 2.50, below the ceiling");
        }

        [Test]
        public void PostDealShare_IsTheSumOfTheTwoShares_HeldInZeroToOne()
        {
            Assert.Near(0.65, AcquisitionAntitrustMath.PostDealShare(0.50f, 0.15f), 1e-6);
            Assert.Equal(1f, AcquisitionAntitrustMath.PostDealShare(0.80f, 0.60f), "two shares that add up to more than the market are one market");
            Assert.Equal(0.30f, AcquisitionAntitrustMath.PostDealShare(-1f, 0.30f), "a negative share counts as none");
            Assert.Equal(0f, AcquisitionAntitrustMath.PostDealShare(float.NaN, float.NaN), "a number that is not a number counts as none");
        }

        [Test]
        public void ThePercentArgument_IsAWholeNumber_RoundedHalfUp()
        {
            Assert.Equal("65", AcquisitionAntitrustMath.PercentText(0.65f));
            Assert.Equal("63", AcquisitionAntitrustMath.PercentText(0.625f));
            Assert.Equal("100", AcquisitionAntitrustMath.PercentText(1.7f));
            Assert.Equal("0", AcquisitionAntitrustMath.PercentText(-0.2f));
        }

        // ---- the parameters ----

        [Test]
        public void TheParameters_AreUsed_NotTheConstants()
        {
            var steep = new AcquisitionAntitrustParams { SoftShare = 0.20f, Slope = 4f, CaseSurcharge = 2f, MaxMultiplier = 2.5f, ShareMonths = 12 };
            Assert.Near(1 + 4 * 0.10, AcquisitionAntitrustMath.Multiplier(0.20f, 0.10f, false, steep), 1e-5, "the soft limit and the slope");
            Assert.Equal(2.5f, AcquisitionAntitrustMath.Multiplier(0.50f, 0.10f, true, steep), "the ceiling of the file, below the channel's");
            Assert.Near(2.0, AcquisitionAntitrustMath.Multiplier(0.05f, 0.05f, true, steep), 1e-5, "the surcharge of the file");
        }

        [Test]
        public void NullParameters_AreTheDefaultsOfTheDesign()
        {
            Assert.Near(2.03125, AcquisitionAntitrustMath.Multiplier(0.50f, 0.15f, true, null), 1e-5);
        }

        [Test]
        public void ParametersThatCouldLowerThePrice_NeverDo()
        {
            var bad = new AcquisitionAntitrustParams { SoftShare = 0f, Slope = -3f, CaseSurcharge = 0.2f, MaxMultiplier = 0.1f };
            Assert.Equal(1f, AcquisitionAntitrustMath.Multiplier(0.9f, 0.05f, true, bad), "a surcharge never lowers a price");

            var nan = new AcquisitionAntitrustParams { Slope = float.NaN };
            Assert.Equal(1f, AcquisitionAntitrustMath.Multiplier(0.9f, 0.05f, false, nan), "a number that is not a number gives 1");

            var huge = new AcquisitionAntitrustParams { Slope = 1000f, CaseSurcharge = 1000f, MaxMultiplier = 1000f };
            Assert.Equal(3f, AcquisitionAntitrustMath.Multiplier(0.9f, 0.05f, true, huge), "the channel's own limit of 3.00 holds whatever the file says");
        }

        [Test]
        public void TheShippedParams_AreTheNumbersOfTheDesign_AndTheDefaultsOfTheCode()
        {
            string json = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", "params.json"));
            var problems = new List<string>();
            HistContentParams shipped = HistContentParams.FromJson(HistFixture.ObjFromFile(json), problems);
            AcquisitionAntitrustParams p = shipped.AcquisitionAntitrust;

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Near(0.40, p.SoftShare, 1e-6);
            Assert.Near(2.5, p.Slope, 1e-6);
            Assert.Near(1.25, p.CaseSurcharge, 1e-6);
            Assert.Near(3.0, p.MaxMultiplier, 1e-6);
            Assert.Equal(36, p.ShareMonths);
            Assert.Equal(Design.SoftShare, p.SoftShare, "the defaults of the code are the shipped numbers");
            Assert.Equal(Design.Slope, p.Slope);
            Assert.Equal(Design.CaseSurcharge, p.CaseSurcharge);
            Assert.Equal(Design.MaxMultiplier, p.MaxMultiplier);
            Assert.Equal(Design.ShareMonths, p.ShareMonths);
        }

        [Test]
        public void AnOverrideOfTheFile_ChangesTheAnswer()
        {
            HistContentParams overridden = HistContentParams.FromJson(HistFixture.Obj("{'acquisitionAntitrust':{'softShare':0.10,'slope':5,'caseSurcharge':1.0,'maxMultiplier':2.0,'shareMonths':12}}"), null);
            var world = new Market(0.30f, 0.10f);
            AcquisitionAntitrustCore core = world.Core(() => overridden.AcquisitionAntitrust);

            ProviderResult result = core.Provide(Target);
            Assert.Equal(2f, result.Multiplier, "1 + 5 x 0.30 = 2.5, cut at the 2.0 of the file");
            Assert.Equal(12, world.LastMonths, "the shares are asked for the months of the file");
        }

        // ---- the provider over the real share cache and the real case ----

        /// <summary>A market of three companies over 40 months (the player, the target, the rest) and the cache of the roles built on it, like the feature does.</summary>
        private sealed class Market
        {
            public readonly FakeWorld World = new FakeWorld();
            public readonly MarketShareCache Shares;
            public readonly HistSaveData Data = new HistSaveData();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly CaseCore Cases;
            public bool Enabled = true;
            public int LastMonths;
            public int Asked;

            public Market(float player, float target)
            {
                World.AddPlayer("Player Co");
                World.AddAi(0, "Intel");
                World.AddAi(1, "AMD");
                CompanyInfo rest = World.Find("ai:0");
                World.AddSteadyCpu(World.Find(Player), Segment.HighEnd, (int)Math.Round(player * 1000));
                World.AddSteadyCpu(World.Find(Target), Segment.MidRange, (int)Math.Round(target * 1000));
                World.AddSteadyCpu(rest, Segment.LowEnd, 1000 - (int)Math.Round(player * 1000) - (int)Math.Round(target * 1000));
                Shares = World.Shares();

                Economy.Revenue[Player] = 25e9f;
                Cases = new CaseCore(() => Data, Economy, Mods, new FakeFirer(), () => new FixedRng(0.9f), key => 0.6f, a => a.ToString("0"), null);
            }

            public AcquisitionAntitrustCore Core(Func<AcquisitionAntitrustParams> parameters = null)
            {
                return new AcquisitionAntitrustCore(
                    () => Enabled,
                    parameters ?? (() => Design),
                    (key, months) =>
                    {
                        Asked++;
                        LastMonths = months;
                        return Shares.Share(key, SegmentMask.All, months);
                    },
                    Cases.HasOpenCase);
            }

            /// <summary>The fine of the EU against the player, opened like the effect opens it.</summary>
            public bool OpenCaseAgainst(string key)
            {
                JsonObject raw = HistFixture.Obj("{'mode':'fine','finePct':0.04,'appealMonths':60,'pAnnul':0.35,'pReduce':0.35,'reduceTo':0.35}");
                CaseParams p = CaseParams.Read(raw, null);
                return Cases.Open(Case, "hist.2009.eufine", key, null, p, raw, AntitrustCaseMachine.Fine(p, 25e9f));
            }
        }

        [Test]
        public void Ac191_PlayerHalf_TargetFifteen_AndACaseAgainstThePlayer_CostsAboutTwoTimes_WithTheReason()
        {
            var world = new Market(0.50f, 0.15f);
            Assert.True(world.OpenCaseAgainst(Player));

            ProviderResult result = world.Core().Provide(Target);

            Assert.Near(2.03125, result.Multiplier, 1e-4, "(1 + 2.5 x 0.25) x 1.25");
            Assert.Equal("hist.mod.acqAntitrust", result.ReasonKey, "visible in Explain");
            Assert.Equal(new[] { "65" }, result.ReasonArgs, "the share after the deal, in percent");
        }

        [Test]
        public void Ac192_ThirtyPercentAndNoCase_IsOne_WithoutAReason()
        {
            var world = new Market(0.20f, 0.10f);

            ProviderResult result = world.Core().Provide(Target);

            Assert.Equal(1f, result.Multiplier);
            Assert.Null(result.ReasonKey, "no entry in Explain");
            Assert.Null(result.ReasonArgs);
        }

        [Test]
        public void Ac193_NinetySevenPercentWithACase_IsCutAtThree()
        {
            var world = new Market(0.80f, 0.17f);
            world.OpenCaseAgainst(Player);

            ProviderResult result = world.Core().Provide(Target);

            Assert.Equal(3f, result.Multiplier, "3.03 is cut at 3.0");
            Assert.Equal(new[] { "97" }, result.ReasonArgs);
        }

        [Test]
        public void WithoutACase_HalfTheMarketCostsAQuarterMore_AndTheReasonSaysSo()
        {
            var world = new Market(0.35f, 0.15f);

            ProviderResult result = world.Core().Provide(Target);

            Assert.Near(1.25, result.Multiplier, 1e-4);
            Assert.Equal("hist.mod.acqAntitrust", result.ReasonKey);
            Assert.Equal(new[] { "50" }, result.ReasonArgs);
        }

        [Test]
        public void TheReason_AppearsOnlyWhenTheMultiplierIsNotOne()
        {
            foreach (var shares in new[] { new[] { 0.10f, 0.05f }, new[] { 0.25f, 0.15f }, new[] { 0.35f, 0.15f }, new[] { 0.60f, 0.30f } })
            {
                var world = new Market(shares[0], shares[1]);
                ProviderResult result = world.Core().Provide(Target);
                Assert.Equal(result.Multiplier > 1f, result.ReasonKey != null, "shares " + shares[0] + "+" + shares[1] + ": the reason goes with a multiplier above 1");
            }
        }

        [Test]
        public void ACaseAgainstARival_DoesNotCount_OnlyOneAgainstThePlayer()
        {
            var world = new Market(0.20f, 0.10f);
            Assert.True(world.OpenCaseAgainst("ai:0"));

            Assert.Equal(1f, world.Core().Provide(Target).Multiplier, "the buyer is the player: a case against Intel is not his");
            Assert.False(world.OpenCaseAgainst(Player), "(the same case id is already there)");
        }

        [Test]
        public void WhenTheCaseEnds_TheSurchargeGoes_AndTheChannelIsInvalidatedAtBothEnds()
        {
            var world = new Market(0.50f, 0.15f);
            AcquisitionAntitrustCore core = world.Core();
            float before = core.Provide(Target).Multiplier;

            world.OpenCaseAgainst(Player);
            float during = core.Provide(Target).Multiplier;
            Assert.Equal(1, world.Mods.InvalidationsOf("acquisition.price"), "opening the case drops the cached prices");

            world.Cases.Choose(Case, CaseActions.Pay, 0f);
            float after = core.Provide(Target).Multiplier;

            Assert.Near(1 + 2.5 * 0.25, before, 1e-4, "no case: 1.625");
            Assert.Near(before * 1.25, during, 1e-4, "an open case multiplies by 1.25");
            Assert.Equal(before, after, "a paid fine is not an open case");
            Assert.Equal(2, world.Mods.InvalidationsOf("acquisition.price"), "closing it drops them again");
        }

        [Test]
        public void ARefusedDeal_NeverHappens_TheSurchargeOnlyRaisesThePrice()
        {
            // D13: no ban. Every combination of shares and case gives a number in [1.00, 3.00]: the deal stays possible, it just costs more.
            for (int player = 0; player <= 100; player += 5)
            {
                for (int target = 0; target <= 100 - player; target += 5)
                {
                    foreach (bool open in new[] { false, true })
                    {
                        float m = AcquisitionAntitrustMath.Multiplier(player / 100f, target / 100f, open, Design);
                        Assert.InRange(m, 1f, 3f, player + "+" + target + " case " + open);
                        Assert.False(float.IsNaN(m) || float.IsInfinity(m));
                    }
                }
            }
        }

        // ---- the switches ----

        [Test]
        public void FeatureOrCategoryOff_IsOne_WithoutAReason_WhateverTheShares()
        {
            var world = new Market(0.80f, 0.17f);
            world.OpenCaseAgainst(Player);
            world.Enabled = false;

            ProviderResult off = world.Core().Provide(Target);
            Assert.Equal(1f, off.Multiplier, "the feature or the category antitrust is off");
            Assert.Null(off.ReasonKey);

            world.Enabled = true;
            Assert.Equal(3f, world.Core().Provide(Target).Multiplier, "and it comes back when it is switched on");
            Assert.Equal(2, world.Asked, "(only the two questions of the answer that was on: the shares are not read while it is off)");
        }

        [Test]
        public void ASwitchedOffProvider_DoesNotEvenReadTheShares()
        {
            var world = new Market(0.80f, 0.17f);
            world.Enabled = false;
            world.Core().Provide(Target);
            Assert.Equal(0, world.Asked, "nothing is read while the feature or the category is off");
        }

        [Test]
        public void TheQuestionsThatAreNotAboutARival_AreOne()
        {
            var world = new Market(0.80f, 0.17f);
            world.OpenCaseAgainst(Player);
            AcquisitionAntitrustCore core = world.Core();

            Assert.Equal(1f, core.Provide(Player).Multiplier, "the player does not buy himself");
            Assert.Equal(1f, core.Provide(null).Multiplier, "no company");
            Assert.Equal(1f, core.Provide(string.Empty).Multiplier);
            Assert.Equal(1f, core.Provide("ai:").Multiplier, "not a key");
            Assert.Equal(1f, core.Provide("someone").Multiplier);
            Assert.Equal(0, world.Asked, "none of them reads a share");
        }

        [Test]
        public void ACompanyWithoutSales_AddsNothingToTheShare()
        {
            var world = new Market(0.45f, 0.10f);
            world.World.AddAi(7, "Newcomer");
            world.Shares.Rebuild(world.World, 36);

            ProviderResult result = world.Core().Provide("ai:7");

            Assert.Near(1 + 2.5 * 0.05, result.Multiplier, 1e-4, "S' is the player's 0.45 alone");
            Assert.Equal(new[] { "45" }, result.ReasonArgs);
        }

        // ---- cost ----

        [Test]
        public void AnAnswer_IsCheap_TheSharesComeFromTheCacheAndAreRememberedForTheMonth()
        {
            var world = new Market(0.50f, 0.15f);
            world.OpenCaseAgainst(Player);
            AcquisitionAntitrustCore core = world.Core();
            int rebuilds = world.Shares.Rebuilds;

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) core.Provide(Target);
            clock.Stop();

            Assert.Equal(rebuilds, world.Shares.Rebuilds, "asking never rebuilds the cache");
            Assert.True(clock.ElapsedMilliseconds < 500, "20 000 answers took " + clock.ElapsedMilliseconds + " ms");
        }

        // ---- the wiring in the feature (read from the source) ----

        private static string Code(string relative)
        {
            return SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", relative)), true);
        }

        private static string Body(string code, string signature)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            int brace = code.IndexOf('{', start);
            int depth = 0;
            for (int i = brace; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(brace, i - brace + 1);
            }
            return code.Substring(brace);
        }

        [Test]
        public void TheProvider_IsRegisteredOnce_InRegister_OnTheChannelOfRivalAcquisitions()
        {
            string feature = Code("HistoricalEventsFeature.cs");

            Assert.Equal(1, Occurrences(feature, "RegisterReasonedProvider(FeatureId, ModChannels.AcquisitionPrice, _services.Acquisition.Multiplier)"), "one registration, a reasoned provider");
            Assert.Equal(2, Occurrences(feature, "RegisterReasonedProvider("), "the two providers of the feature: market.rating.cpu and acquisition.price");

            string register = Body(feature, "public void Register(FeatureContext ctx)");
            string services = Body(feature, "private void RegisterServices(FeatureContext ctx)");
            Assert.True(register.Contains("RegisterServices(ctx)"), "Register runs the registration of the services");
            Assert.True(services.Contains("_services.Acquisition.Multiplier"), "and the provider is one of them: registered in Register, not in a tick or a hook");
            Assert.False(feature.Contains("ModModifiers.RemoveAll("), "the entries of the feature are never removed");
        }

        [Test]
        public void TheShares_AreRebuiltMonthly_AndTheChannelIsInvalidatedAfterTheRebuild()
        {
            string feature = Code("HistoricalEventsFeature.cs");
            string rebuild = Body(feature, "private void OnShareRebuild()");

            int rebuilt = rebuild.IndexOf("Shares.Rebuild(", StringComparison.Ordinal);
            int invalidated = rebuild.IndexOf("ModModifiers.Invalidate(ModChannels.AcquisitionPrice)", StringComparison.Ordinal);
            Assert.True(rebuilt >= 0 && invalidated > rebuilt, "the invalidation comes after the rebuild (FR-19 item 4)");
            Assert.True(rebuild.Contains("ModHooks.Guard("), "NFR-2: no exception leaves the tick");
        }

        [Test]
        public void WhenTheGameIsReady_ThePricesAskedBeforeAreDropped()
        {
            string feature = Code("HistoricalEventsFeature.cs");
            string ready = Body(feature, "private void OnGameReady(GameReadyReason reason)");

            Assert.True(ready.Contains("ModModifiers.Invalidate(ModChannels.AcquisitionPrice)"));
        }

        [Test]
        public void ASwitchedCategory_DropsTheCachedPrices()
        {
            string feature = Code("HistoricalEventsFeature.cs");
            Assert.True(Body(feature, "private void OnSettingChanged(string featureId, string key)").Contains("AcquisitionAntitrust.OnSettingChanged(key)"));

            string provider = Code("AcquisitionAntitrust.cs");
            string changed = Body(provider, "internal static void OnSettingChanged(string key)");
            Assert.True(changed.Contains("HistCategories.Antitrust") && changed.Contains("ModModifiers.Invalidate(ModChannels.AcquisitionPrice)"));
        }

        [Test]
        public void TheProviderGlue_ReadsTheSwitchesTheParamsAndTheCache_AndBlocksNothing()
        {
            string provider = Code("AcquisitionAntitrust.cs");

            Assert.True(provider.Contains("ModFeatures.IsActive(HistIds.Feature)") && provider.Contains("HistSettings.IsCategoryEnabled(HistCategories.Antitrust)"), "the feature and the category");
            Assert.True(provider.Contains("ModTicks.IsGameReady"), "outside a running game the world is not read");
            Assert.True(provider.Contains("feature.Params.AcquisitionAntitrust"), "the numbers are those of params.json");
            Assert.True(provider.Contains("runtime.Shares.Share(companyKey, SegmentMask.All, months)") && provider.Contains("EnsureFresh("), "the shares come from the cache of the roles");
            Assert.True(provider.Contains("cases.HasOpenCase"), "the case comes from the case service");
            Assert.True(provider.Contains("return _core.Provide(ctx.CompanyKey)"), "the company in the question is the target");
            Assert.False(provider.Contains("[HarmonyPatch") || provider.Contains("Harmony"), "no patch: the price channel is read by rival-acquisitions");
            Assert.False(provider.Contains("RemoveAll(") || provider.Contains("ModModifiers.Add("), "no entry of ModModifiers is made: the provider is not saved");
        }

        [Test]
        public void TheProviderIsMadeByTheServices_FromTheCaseCoreAndTheRuntime()
        {
            string services = Code("HistServices.cs");
            Assert.True(services.Contains("Acquisition = new AcquisitionAntitrust(feature, runtime, Cases)"));
        }

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
            return count;
        }
    }
}
