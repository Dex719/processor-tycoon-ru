using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The link with <c>rival-acquisitions</c> (FR-15, AC-15.1, TSK-027): the price multiplier of the channel <c>acquisition.price</c>
    /// (<see cref="AcquisitionRules"/>), what a finished deal does to the rivals (<see cref="AcquisitionLinkCore"/>) and, read as text, the registration of the
    /// provider (once, in <c>Register</c>) and the subscription to the deals (only while <c>rival-acquisitions</c> is active).
    /// </summary>
    [Covers(typeof(AcquisitionRules), typeof(AcquisitionLinkCore), typeof(DealEffect))]
    public class AcquisitionLinkTests
    {
        private const int TexasInstruments = 3;   // conservative: baseline +10, sensitivity 0.6

        private sealed class FakeEffects : IRivalApiEffects
        {
            public readonly List<string> Calls = new List<string>();

            public int ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin)
            {
                return 0;
            }

            public int RestoreCut(string reactionId)
            {
                return 0;
            }

            public int RestoreRivalCuts(int uniqueId)
            {
                Calls.Add("restore-rival ai:" + uniqueId);
                return 0;
            }

            public int CancelReactions(int uniqueId, string reason)
            {
                Calls.Add("cancel ai:" + uniqueId);
                return 0;
            }

            public void RevertCharacter(int uniqueId)
            {
                Calls.Add("revert ai:" + uniqueId);
            }

            public void ApplyCharacter(int uniqueId)
            {
                Calls.Add("apply ai:" + uniqueId);
            }
        }

        private sealed class Rig
        {
            public readonly AttitudeRig Base = new AttitudeRig().AssignAll();
            public readonly FakeEffects Effects = new FakeEffects();
            public readonly AcquisitionLinkCore Link;

            public Rig(RivalConfig config = null)
            {
                RivalConfig used = config ?? Base.Catalog.Config;
                Link = new AcquisitionLinkCore(Base.Book, Base.Ledger, new RivalApiCore(Base.Book, Base.Ledger, Effects), () => used);
            }
        }

        // ---- the price ----

        [Test]
        public void TheMultiplier_OfAHostileRival_IsAboveOne_AC_15_1()
        {
            // AC-15.1: the attitude of Texas Instruments is -60, the price of a hostile takeover x 1.3
            Assert.Near(1.3, AcquisitionRules.PriceMultiplier(-60f, 200), 1e-6);
            Assert.Near(1.0, AcquisitionRules.PriceMultiplier(40f, 200), 1e-9);
        }

        [Test]
        public void TheMultiplier_RunsFromOneToOneAndAHalf()
        {
            Assert.Equal(1f, AcquisitionRules.PriceMultiplier(0f, 200));
            Assert.Equal(1f, AcquisitionRules.PriceMultiplier(100f, 200), "a friend costs no more");
            Assert.Near(1.1, AcquisitionRules.PriceMultiplier(-20f, 200), 1e-6);
            Assert.Near(1.5, AcquisitionRules.PriceMultiplier(-100f, 200), 1e-6, "the greatest contribution of the feature");
            Assert.Near(1.5, AcquisitionRules.PriceMultiplier(-250f, 200), 1e-6, "the attitude is held to the scale first");
        }

        [Test]
        public void TheMultiplier_ToleratesBadNumbers()
        {
            Assert.Equal(1f, AcquisitionRules.PriceMultiplier(float.NaN, 200));
            Assert.Near(1.3, AcquisitionRules.PriceMultiplier(-60f, 0), 1e-6, "a divisor below one is the default 200");
            Assert.Near(1.3, AcquisitionRules.PriceMultiplier(-60f, -5), 1e-6);
            Assert.Near(1.6, AcquisitionRules.PriceMultiplier(-60f, 100), 1e-6, "the divisor of the config");
        }

        [Test]
        public void TheMultiplier_FollowsTheAttitudeOfTheLedger_WithTheDivisorOfTheConfig()
        {
            var rig = new Rig();
            rig.Base.SetAttitude(TexasInstruments, -60f);

            float multiplier = AcquisitionRules.PriceMultiplier(rig.Base.Ledger.Get(TexasInstruments), rig.Base.Catalog.Config.acquisition.hostilePriceDivisor);

            Assert.Near(1.3, multiplier, 1e-6);
        }

        [Test]
        public void TheShippedConfig_HasTheNumbersOfTheSpec()
        {
            RivalConfig.AcquisitionSettings settings = new Rig().Base.Catalog.Config.acquisition;

            Assert.Equal(200, settings.hostilePriceDivisor);
            Assert.Equal(-10, settings.hostileDealAttitude);
            Assert.Equal(200, AcquisitionRules.DefaultDivisor);
        }

        // ---- what the deal types do ----

        [Test]
        public void OnlyAHostileDeal_AlarmsTheOtherRivals()
        {
            Assert.True(AcquisitionRules.IsHostile("Hostile"));
            foreach (string type in new[] { "BankruptAssets", "Distressed", "Stake", "RescueOrder", "", null, "hostile" })
            {
                Assert.False(AcquisitionRules.IsHostile(type), type ?? "null");
            }
        }

        [Test]
        public void ADealThatBuysTheWholeCompany_TakesItOutOfTheGame_AStakeDoesNot()
        {
            Assert.True(AcquisitionRules.RemovesTarget("Hostile", false));
            Assert.True(AcquisitionRules.RemovesTarget("Distressed", false));
            Assert.True(AcquisitionRules.RemovesTarget("BankruptAssets", false));
            Assert.False(AcquisitionRules.RemovesTarget("Stake", false));
            Assert.False(AcquisitionRules.RemovesTarget("RescueOrder", false));
            Assert.True(AcquisitionRules.RemovesTarget("Stake", true), "the API says the company was bought: it is out");
        }

        // ---- the deals ----

        [Test]
        public void AHostileDeal_LowersTheAttitudeOfTheOtherRivals_By10TimesTheirSensitivity()
        {
            var rig = new Rig();

            DealEffect effect = rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.Equal(12, effect.Alarmed, "13 rivals, the target is not one of the others");
            Assert.Near(-22f, rig.Base.Ledger.Get(AttitudeRig.Intel), 1e-4, "hegemon: -10 + (-10 x 1.2)");
            Assert.Near(-28f, rig.Base.Ledger.Get(AttitudeRig.Amd), 1e-4, "price_warrior: -15 + (-10 x 1.3)");
            Assert.Near(-8f, rig.Base.Ledger.Get(AttitudeRig.Ibm), 1e-4, "engineer: 0 + (-10 x 0.8)");
            Assert.Near(15f, rig.Base.Ledger.Get(AttitudeRig.Tsmc), 1e-4, "foundry_partner: +20 + (-10 x 0.5)");
            Assert.Near(10f, rig.Base.Ledger.Get(TexasInstruments), 1e-4, "the target itself is not touched");
        }

        [Test]
        public void TheLineInTheHistory_SaysWhy()
        {
            var rig = new Rig();

            rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.Equal("hostile acquisition", rig.Base.Ledger.History(AttitudeRig.Intel).Single().reason);
            Assert.Equal(0, rig.Base.Ledger.History(TexasInstruments).Count);
        }

        [Test]
        public void TheTarget_IsPutAside_WithItsReactionsAndPrices()
        {
            var rig = new Rig();

            DealEffect effect = rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.True(effect.TargetDormant);
            Assert.True(rig.Base.Book.IsDormant(TexasInstruments));
            Assert.Equal(new List<string> { "cancel ai:3", "restore-rival ai:3", "revert ai:3" }, rig.Effects.Calls);
        }

        [Test]
        public void ADealIsNotAnnouncedTwice_ButItsTargetIsPutAsideOnce()
        {
            var rig = new Rig();
            rig.Link.OnDeal("Hostile", TexasInstruments, true);
            rig.Effects.Calls.Clear();

            DealEffect again = rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.False(again.TargetDormant, "it was dormant already");
            Assert.Equal(0, rig.Effects.Calls.Count);
        }

        [Test]
        public void ADormantRival_AndABankruptOne_AreNotAlarmed()
        {
            var rig = new Rig();
            rig.Base.Book.SetDormant(AttitudeRig.Intel, true);
            rig.Base.Bankrupt.Add(AttitudeRig.Amd);

            DealEffect effect = rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.Equal(10, effect.Alarmed);
            Assert.Near(-10f, rig.Base.Ledger.Get(AttitudeRig.Intel), 1e-4, "a dormant rival is out of the game");
            Assert.Near(-15f, rig.Base.Ledger.Get(AttitudeRig.Amd), 1e-4, "a bankrupt one is frozen");
        }

        [Test]
        public void ADistressedPurchase_PutsTheTargetAside_WithoutAlarmingAnybody()
        {
            var rig = new Rig();

            DealEffect effect = rig.Link.OnDeal("Distressed", TexasInstruments, true);

            Assert.True(effect.TargetDormant);
            Assert.Equal(0, effect.Alarmed);
            Assert.Near(-10f, rig.Base.Ledger.Get(AttitudeRig.Intel), 1e-4);
        }

        [Test]
        public void AStake_ChangesNothing()
        {
            var rig = new Rig();

            DealEffect effect = rig.Link.OnDeal("Stake", TexasInstruments, false);

            Assert.False(effect.TargetDormant);
            Assert.Equal(0, effect.Alarmed);
            Assert.False(rig.Base.Book.IsDormant(TexasInstruments));
            Assert.Equal(0, rig.Effects.Calls.Count);
        }

        [Test]
        public void TheAttitudeOfADeal_IsTakenFromTheConfig()
        {
            RivalConfig config = RivalConfig.Defaults();
            config.acquisition.hostileDealAttitude = -20;
            var rig = new Rig(config);

            rig.Link.OnDeal("Hostile", TexasInstruments, true);

            Assert.Near(-34f, rig.Base.Ledger.Get(AttitudeRig.Intel), 1e-4, "-10 + (-20 x 1.2)");
        }

        [Test]
        public void AfterALoad_TheTargetsOfEarlierDealsThatLeftTheGame_ArePutAside()
        {
            var rig = new Rig();
            var deals = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("Hostile", 3), new KeyValuePair<string, int>("Stake", 5), new KeyValuePair<string, int>("Distressed", 6),
                new KeyValuePair<string, int>("RescueOrder", 7),
            };

            int first = rig.Link.SyncTargets(deals, uid => uid == 7);

            Assert.Equal(3, first, "3 and 6 by their type, 7 because the API says it was bought");
            Assert.True(rig.Base.Book.IsDormant(3) && rig.Base.Book.IsDormant(6) && rig.Base.Book.IsDormant(7));
            Assert.False(rig.Base.Book.IsDormant(5), "a stake leaves the company in the game");
            Assert.Equal(0, rig.Link.SyncTargets(deals, uid => uid == 7), "the second look finds nothing to do");
            Assert.Near(-10f, rig.Base.Ledger.Get(AttitudeRig.Intel), 1e-4, "no deal is announced again");
        }

        [Test]
        public void SyncTargets_WithoutDeals_DoesNothing()
        {
            var rig = new Rig();

            Assert.Equal(0, rig.Link.SyncTargets(null, null));
            Assert.Equal(0, rig.Link.SyncTargets(new List<KeyValuePair<string, int>>(), uid => true));
        }

        // ---- the registration (read as text) ----

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities" }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheProvider_IsRegisteredOnce_InRegister_NotInTheSwitches()
        {
            string link = Code("Integration", "AcquisitionLink.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.Equal(1, Regex.Matches(link, @"ModModifiers\.RegisterProvider\(Feature, ModChannels\.AcquisitionPrice, PriceMultiplier, PriceReasonKey\)").Count);
            Assert.True(Regex.IsMatch(link, @"if \(_providerRegistered\) return false;"), "a second call does nothing");
            Assert.True(Regex.IsMatch(link, @"PriceReasonKey = ""rival\.reason\.acquisitionPrice"""));
            Assert.Equal(1, Regex.Matches(feature, @"AcquisitionLink\.RegisterProvider\(\);").Count);

            // the provider is not made again when the feature is switched on or off
            string enabled = Between(feature, "public void OnEnabled()", "public void OnDisabled()");
            string disabled = Between(feature, "public void OnDisabled()", "private void OnFastForwardEnded()");
            Assert.False(enabled.Contains("RegisterProvider"), "OnEnabled");
            Assert.False(disabled.Contains("RegisterProvider"), "OnDisabled");
        }

        [Test]
        public void TheMultiplier_IsAskedOnlyOfACompanyWithACharacter_AndUsesTheDivisorOfTheConfig()
        {
            string link = Code("Integration", "AcquisitionLink.cs");

            Assert.True(Regex.IsMatch(link, @"if \(!uid\.HasValue \|\| !feature\.Attitudes\.IsKnown\(uid\.Value\)\) return 1f;"), "the player and unknown companies cost x1");
            Assert.True(Regex.IsMatch(link, @"AcquisitionRules\.PriceMultiplier\(feature\.Attitudes\.Get\(uid\.Value\), feature\.Catalog\.Config\.acquisition\.hostilePriceDivisor\)"));
            Assert.True(Regex.IsMatch(link, @"ModModifiers\.Invalidate\(ModChannels\.AcquisitionPrice, key\)"), "a cached price follows the attitude");
            Assert.True(Regex.IsMatch(link, @"feature\.Attitudes\.Changed \+= OnAttitudeChanged;"));
        }

        [Test]
        public void TheSubscriptionToTheDeals_IsMadeOnlyWhileRivalAcquisitionsIsActive_OnceAndLetGo()
        {
            string link = Code("Integration", "AcquisitionLink.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(link, @"if \(!ModFeatures\.IsActive\(Feature\) \|\| !ModFeatures\.IsActive\(RivalAcquisitionsApi\.FeatureId\)\) return;"), "without rival-acquisitions the module is not connected");
            Assert.Equal(1, Regex.Matches(link, @"RivalAcquisitionsApi\.DealCompleted \+= OnDeal;").Count);
            Assert.Equal(1, Regex.Matches(link, @"RivalAcquisitionsApi\.DealCompleted -= OnDeal;").Count);
            Assert.True(Regex.IsMatch(link, @"if \(!_attached\)\s*\{\s*RivalAcquisitionsApi\.DealCompleted \+= OnDeal;") && Regex.IsMatch(link, @"if \(!_attached\) return;"), "idempotent");

            Assert.True(Regex.IsMatch(feature, @"""ModTicks\.GameReady\.links"", AttachLinks"), "on GameReady");
            Assert.True(Regex.IsMatch(feature, @"""feature/disabled\.links"", DetachLinks"), "dropped when the feature is switched off");
            Assert.True(Regex.IsMatch(link, @"RivalAcquisitionsApi\.IsAbsorbedByPlayer\(deal\.targetUniqueId\)"), "the API tells whether the target is out");
            Assert.True(Regex.IsMatch(link, @"_core\.OnDeal\(deal\.type, deal\.targetUniqueId, absorbed\)"));
            Assert.True(Regex.IsMatch(link, @"RivalAcquisitionsApi\.Deals"), "after a load the earlier deals are looked at");
        }

        [Test]
        public void TheHostileDealReason_IsToldInWordsInTheHistoryOfTheWindow()
        {
            var rig = new IntelRig();
            rig.Base.Ledger.Change(AttitudeRig.Ibm, -10f, AcquisitionRules.HostileDealReason);

            List<string> texts = IntelRig.TextsOf(rig.Builder.BuildDetails(IntelRig.Facts(AttitudeRig.Ibm, "IBM"), rig.Settings(false, true)).Find("history"));

            Assert.Equal(1, texts.Count);
            Assert.True(texts[0].Contains("вы поглотили компанию против её воли"), texts[0]);
            Assert.Equal(0, rig.Text.Missing.Count, string.Join(", ", rig.Text.Missing));
        }

        [Test]
        public void TheReason_IsInBothTables()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                Assert.True(RivalTestKit.ReadL10n(language).ContainsKey("rival.reason.acquisitionPrice"), language);
            }
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "'");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "no '" + to + "' after '" + from + "'");
            return text.Substring(start, end - start);
        }
    }
}
