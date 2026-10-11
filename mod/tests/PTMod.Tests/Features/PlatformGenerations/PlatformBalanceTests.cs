using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-002, NFR-5: <c>balance.json</c> holds every number of design.md with its default (also <c>oem.fullPlatformBonus</c> 0.05, <c>oem.mode</c> auto,
    /// no Calculator in <c>contractTypes</c>), the typed form <see cref="PlatformBalance"/> reads it without a problem, the defaults of the code are
    /// the numbers of the file, and a missing, wrong or out-of-range member keeps its default with a line in the list.
    /// </summary>
    [Covers(typeof(PlatformBalance), typeof(OemMode))]
    public class PlatformBalanceTests
    {
        // Every member of balance.json (design.md, "Контент balance.json"): section, name.
        private static readonly string[] InstalledBaseMembers = { "lifetimeMonths", "contractUnitWeight" };

        private static readonly string[] RatingMembers =
        {
            "ibMaxBonus", "ibSaturationShare", "sharedWeight", "earlyBonus", "earlyDurationMonths", "earlyAdopterSlots", "churnPenalty",
            "minSocketLifeMonths", "churnDurationMonths", "churnMinBaseShare", "customOnSameBaseIsCompatible", "industriesWeight", "minMult", "maxMult",
        };

        private static readonly string[] OemMembers =
        {
            "mode", "contractTypes", "marketAverageMinGenOrder", "adoptBonus", "lagPenalty1", "lagPenalty2", "fullPlatformBonus", "oemLoyaltyBonus",
            "minChanceMult", "maxChanceMult",
        };

        private static JsonObject ShippedJson()
        {
            return PlatformTestData.ParseObject(PlatformTestData.ReadContent("balance.json"));
        }

        private static PlatformBalance Read(string json, List<string> problems)
        {
            return PlatformBalance.FromJson(PlatformTestData.ParseObject(json), problems);
        }

        // ---- the shipped file ----

        [Test]
        public void ShippedFile_HasEveryMemberOfTheDesign()
        {
            JsonObject root = ShippedJson();
            Assert.Equal(1, root.GetInt32("schema", -1), "schema");
            AssertMembers(root, "installedBase", InstalledBaseMembers);
            AssertMembers(root, "rating", RatingMembers);
            AssertMembers(root, "oem", OemMembers);
            Assert.Equal(4, root.Count, "schema + three sections and nothing else");
        }

        private static void AssertMembers(JsonObject root, string section, string[] members)
        {
            JsonObject obj = root.GetObject(section);
            Assert.NotNull(obj, "section '" + section + "'");
            foreach (string member in members) Assert.True(obj.ContainsKey(member), section + "." + member + " is missing");
            Assert.Equal(members.Length, obj.Count, section + " has no extra members");
        }

        [Test]
        public void ShippedFile_IsReadWithoutAProblem_AndEqualsTheDefaultsOfTheCode()
        {
            var problems = new List<string>();
            PlatformBalance shipped = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, "problems: " + string.Join("; ", problems));

            PlatformBalance code = PlatformBalance.Defaults();
            Assert.Equal(code.InstalledBase.LifetimeMonths, shipped.InstalledBase.LifetimeMonths);
            Assert.Near(code.InstalledBase.ContractUnitWeight, shipped.InstalledBase.ContractUnitWeight, 1e-9);

            PlatformBalance.RatingBalance c = code.Rating;
            PlatformBalance.RatingBalance s = shipped.Rating;
            Assert.Near(c.IbMaxBonus, s.IbMaxBonus, 1e-9, "ibMaxBonus");
            Assert.Near(c.IbSaturationShare, s.IbSaturationShare, 1e-9, "ibSaturationShare");
            Assert.Near(c.SharedWeight, s.SharedWeight, 1e-9, "sharedWeight");
            Assert.Near(c.EarlyBonus, s.EarlyBonus, 1e-9, "earlyBonus");
            Assert.Equal(c.EarlyDurationMonths, s.EarlyDurationMonths, "earlyDurationMonths");
            Assert.Equal(c.EarlyAdopterSlots, s.EarlyAdopterSlots, "earlyAdopterSlots");
            Assert.Near(c.ChurnPenalty, s.ChurnPenalty, 1e-9, "churnPenalty");
            Assert.Equal(c.MinSocketLifeMonths, s.MinSocketLifeMonths, "minSocketLifeMonths");
            Assert.Equal(c.ChurnDurationMonths, s.ChurnDurationMonths, "churnDurationMonths");
            Assert.Near(c.ChurnMinBaseShare, s.ChurnMinBaseShare, 1e-9, "churnMinBaseShare");
            Assert.Equal(c.CustomOnSameBaseIsCompatible, s.CustomOnSameBaseIsCompatible, "customOnSameBaseIsCompatible");
            Assert.Near(c.IndustriesWeight, s.IndustriesWeight, 1e-9, "industriesWeight");
            Assert.Near(c.MinMult, s.MinMult, 1e-9, "minMult");
            Assert.Near(c.MaxMult, s.MaxMult, 1e-9, "maxMult");

            PlatformBalance.OemBalance co = code.Oem;
            PlatformBalance.OemBalance so = shipped.Oem;
            Assert.Equal(co.Mode, so.Mode, "mode");
            Assert.Equal(co.ContractTypes, so.ContractTypes, "contractTypes");
            Assert.Equal(co.MarketAverageMinGenOrder, so.MarketAverageMinGenOrder, "marketAverageMinGenOrder");
            Assert.Near(co.AdoptBonus, so.AdoptBonus, 1e-9, "adoptBonus");
            Assert.Near(co.LagPenalty1, so.LagPenalty1, 1e-9, "lagPenalty1");
            Assert.Near(co.LagPenalty2, so.LagPenalty2, 1e-9, "lagPenalty2");
            Assert.Near(co.FullPlatformBonus, so.FullPlatformBonus, 1e-9, "fullPlatformBonus");
            Assert.Near(co.OemLoyaltyBonus, so.OemLoyaltyBonus, 1e-9, "oemLoyaltyBonus");
            Assert.Near(co.MinChanceMult, so.MinChanceMult, 1e-9, "minChanceMult");
            Assert.Near(co.MaxChanceMult, so.MaxChanceMult, 1e-9, "maxChanceMult");
        }

        [Test]
        public void ShippedFile_HasTheNumbersOfTheDesign()
        {
            var problems = new List<string>();
            PlatformBalance b = PlatformTestData.BuiltInBalance(problems);

            Assert.Equal(60, b.InstalledBase.LifetimeMonths);
            Assert.Near(0.5, b.InstalledBase.ContractUnitWeight, 1e-6);

            Assert.Near(0.10, b.Rating.IbMaxBonus, 1e-6);
            Assert.Near(0.40, b.Rating.IbSaturationShare, 1e-6);
            Assert.Near(0.25, b.Rating.SharedWeight, 1e-6, "sharedWeight is the owner's 0.25");
            Assert.Near(0.06, b.Rating.EarlyBonus, 1e-6);
            Assert.Equal(60, b.Rating.EarlyDurationMonths);
            Assert.Equal(3, b.Rating.EarlyAdopterSlots);
            Assert.Near(0.10, b.Rating.ChurnPenalty, 1e-6);
            Assert.Equal(36, b.Rating.MinSocketLifeMonths);
            Assert.Equal(24, b.Rating.ChurnDurationMonths);
            Assert.Near(0.02, b.Rating.ChurnMinBaseShare, 1e-6);
            Assert.True(b.Rating.CustomOnSameBaseIsCompatible);
            Assert.Near(0.5, b.Rating.IndustriesWeight, 1e-6, "industriesWeight is the owner's 0.5");
            Assert.Near(0.85, b.Rating.MinMult, 1e-6);
            Assert.Near(1.20, b.Rating.MaxMult, 1e-6);

            Assert.Equal(OemMode.Auto, b.Oem.Mode, "oem.mode is auto");
            Assert.Equal(2, b.Oem.MarketAverageMinGenOrder);
            Assert.Near(0.20, b.Oem.AdoptBonus, 1e-6);
            Assert.Near(0.25, b.Oem.LagPenalty1, 1e-6);
            Assert.Near(0.50, b.Oem.LagPenalty2, 1e-6);
            Assert.Near(0.05, b.Oem.FullPlatformBonus, 1e-6, "oem.fullPlatformBonus is 0.05");
            Assert.Near(0.10, b.Oem.OemLoyaltyBonus, 1e-6);
            Assert.Near(0.40, b.Oem.MinChanceMult, 1e-6);
            Assert.Near(1.40, b.Oem.MaxChanceMult, 1e-6);
        }

        [Test]
        public void ShippedFile_ContractTypes_AreComputerAndConsole_WithoutCalculator()
        {
            PlatformBalance b = PlatformTestData.BuiltInBalance(null);
            Assert.Equal(new[] { 1, 2 }, b.Oem.ContractTypes);
            Assert.False(b.Oem.IncludesContractType(0), "Calculator is excluded (owner decision)");
            Assert.True(b.Oem.IncludesContractType(1), "Computer");
            Assert.True(b.Oem.IncludesContractType(2), "Console");
            Assert.False(b.Oem.IncludesContractType(3));

            JsonArray raw = ShippedJson().GetObject("oem").GetArray("contractTypes");
            Assert.Equal(2, raw.Count);
            foreach (JsonNode item in raw) Assert.True(((JsonValue)item).NumberValue != 0, "no 0 in the file");
        }

        [Test]
        public void ShippedFile_StaysInsideTheDesignBounds_OfTheContributionToTheChannels()
        {
            // The contribution to the shared channels (design.md "Максимальный вклад фичи"): rating 0.90 .. 1.16, chance 0.50 .. 1.375.
            PlatformBalance b = PlatformTestData.BuiltInBalance(null);
            Assert.Near(0.90, 1 - b.Rating.ChurnPenalty, 1e-6, "the lowest rating multiplier");
            Assert.Near(1.16, 1 + b.Rating.IbMaxBonus + b.Rating.EarlyBonus, 1e-6, "the highest rating multiplier");
            Assert.True(1 - b.Rating.ChurnPenalty >= b.Rating.MinMult, "the clamp does not cut the lowest value");
            Assert.True(1 + b.Rating.IbMaxBonus + b.Rating.EarlyBonus <= b.Rating.MaxMult, "the clamp does not cut the highest value");

            Assert.Near(0.50, 1 - b.Oem.LagPenalty2, 1e-6, "the lowest chance multiplier");
            Assert.Near(1.375, (1 + b.Oem.AdoptBonus + b.Oem.FullPlatformBonus) * (1 + b.Oem.OemLoyaltyBonus), 1e-6, "the highest chance multiplier");
            Assert.True(1 - b.Oem.LagPenalty2 >= b.Oem.MinChanceMult);
            Assert.True((1 + b.Oem.AdoptBonus + b.Oem.FullPlatformBonus) * (1 + b.Oem.OemLoyaltyBonus) <= b.Oem.MaxChanceMult);
        }

        // ---- the typed form ----

        [Test]
        public void Decay_IsOneMinusOneOverTheLifetime()
        {
            var b = PlatformBalance.Defaults();
            Assert.Near(1.0 - 1.0 / 60.0, b.InstalledBase.Decay, 1e-12);
            b.InstalledBase.LifetimeMonths = 12;
            Assert.Near(1.0 - 1.0 / 12.0, b.InstalledBase.Decay, 1e-12);
            b.InstalledBase.LifetimeMonths = 0;
            Assert.Near(0.0, b.InstalledBase.Decay, 1e-12, "a broken lifetime does not divide by zero");
        }

        [Test]
        public void FromJson_NullOrEmptyConfig_GivesTheDefaults_WithoutProblems()
        {
            var problems = new List<string>();
            PlatformBalance fromNull = PlatformBalance.FromJson(null, problems);
            PlatformBalance fromEmpty = PlatformBalance.FromJson(new JsonObject(), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(60, fromNull.InstalledBase.LifetimeMonths);
            Assert.Near(0.05, fromEmpty.Oem.FullPlatformBonus, 1e-6);
            Assert.Equal(new[] { 1, 2 }, fromEmpty.Oem.ContractTypes);
            Assert.Equal(OemMode.Auto, fromEmpty.Oem.Mode);
        }

        [Test]
        public void FromJson_AValidOverride_ChangesJustThoseMembers()
        {
            var problems = new List<string>();
            PlatformBalance b = Read(
                "{ 'schema': 1, 'installedBase': { 'lifetimeMonths': 120 }, 'rating': { 'ibMaxBonus': 0.2, 'customOnSameBaseIsCompatible': false }," +
                " 'oem': { 'mode': 'marketAverage', 'contractTypes': [ 0, 1, 2 ], 'fullPlatformBonus': 0.1 } }", problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            Assert.Equal(120, b.InstalledBase.LifetimeMonths);
            Assert.Near(0.5, b.InstalledBase.ContractUnitWeight, 1e-6, "an untouched member keeps its default");
            Assert.Near(0.2, b.Rating.IbMaxBonus, 1e-6);
            Assert.False(b.Rating.CustomOnSameBaseIsCompatible);
            Assert.Near(0.25, b.Rating.SharedWeight, 1e-6);
            Assert.Equal(OemMode.MarketAverage, b.Oem.Mode);
            Assert.Equal(new[] { 0, 1, 2 }, b.Oem.ContractTypes);
            Assert.True(b.Oem.IncludesContractType(0));
            Assert.Near(0.1, b.Oem.FullPlatformBonus, 1e-6);
        }

        [Test]
        public void FromJson_AnOverrideMergedByTheContentLoader_IsReadLikeTheFile()
        {
            ConfigLoad load = ContentMerger.MergeConfig(PlatformTestData.FeatureId, "balance.json", PlatformTestData.ReadContent("balance.json"),
                "{ \"schema\": 1, \"rating\": { \"earlyBonus\": 0.09 }, \"oem\": { \"contractTypes\": [ 2 ] } }", 1);
            var problems = new List<string>();
            PlatformBalance b = PlatformBalance.FromJson(load.Config, problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Near(0.09, b.Rating.EarlyBonus, 1e-6);
            Assert.Near(0.10, b.Rating.IbMaxBonus, 1e-6, "the rest comes from the shipped file");
            Assert.Equal(new[] { 2 }, b.Oem.ContractTypes);
        }

        [Test]
        public void FromJson_ValuesOutsideTheirRange_KeepTheDefault_AndAreReported()
        {
            var cases = new[]
            {
                new[] { "installedBase", "lifetimeMonths", "11" },
                new[] { "installedBase", "lifetimeMonths", "241" },
                new[] { "rating", "ibMaxBonus", "0.51" },
                new[] { "rating", "ibMaxBonus", "-0.1" },
                new[] { "rating", "ibSaturationShare", "0" },
                new[] { "rating", "sharedWeight", "1.5" },
                new[] { "rating", "earlyAdopterSlots", "0" },
                new[] { "rating", "minSocketLifeMonths", "0" },
                new[] { "rating", "churnDurationMonths", "0" },
                new[] { "rating", "minMult", "0.4" },
                new[] { "rating", "minMult", "1.1" },
                new[] { "rating", "maxMult", "0.9" },
                new[] { "rating", "maxMult", "2.5" },
                new[] { "oem", "marketAverageMinGenOrder", "10" },
                new[] { "oem", "adoptBonus", "0.6" },
                new[] { "oem", "lagPenalty2", "0.9" },
                new[] { "oem", "fullPlatformBonus", "0.21" },
                new[] { "oem", "minChanceMult", "1.5" },
                new[] { "oem", "maxChanceMult", "5.5" },
            };

            foreach (string[] c in cases)
            {
                var problems = new List<string>();
                PlatformBalance changed = Read("{ 'schema': 1, '" + c[0] + "': { '" + c[1] + "': " + c[2] + " } }", problems);
                Assert.Equal(1, problems.Count, c[0] + "." + c[1] + "=" + c[2] + ": " + string.Join("; ", problems));
                Assert.True(problems[0].Contains(c[0] + "." + c[1]), "the line names the member: " + problems[0]);

                // The member keeps its default: the whole balance equals the defaults.
                AssertSameAsDefaults(changed, c[0] + "." + c[1]);
            }
        }

        [Test]
        public void FromJson_AMemberOfTheWrongType_KeepsTheDefault_AndIsReported()
        {
            var cases = new[]
            {
                "{ 'installedBase': { 'lifetimeMonths': 'sixty' } }",
                "{ 'installedBase': { 'lifetimeMonths': 60.5 } }",
                "{ 'installedBase': { 'contractUnitWeight': true } }",
                "{ 'rating': { 'ibMaxBonus': '0.1' } }",
                "{ 'rating': { 'customOnSameBaseIsCompatible': 1 } }",
                "{ 'rating': { 'earlyAdopterSlots': 2.5 } }",
                "{ 'oem': { 'mode': 3 } }",
                "{ 'oem': { 'mode': 'sometimes' } }",
                "{ 'oem': { 'contractTypes': 1 } }",
                "{ 'oem': { 'contractTypes': [ 1, 3 ] } }",
                "{ 'oem': { 'contractTypes': [ 1, 'two' ] } }",
                "{ 'oem': { 'contractTypes': [ -1 ] } }",
                "{ 'rating': 5 }",
                "{ 'oem': [] }",
            };
            foreach (string json in cases)
            {
                var problems = new List<string>();
                PlatformBalance b = Read(json, problems);
                Assert.True(problems.Count >= 1, "reported: " + json);
                AssertSameAsDefaults(b, json);
            }
        }

        [Test]
        public void FromJson_AnUnknownMember_IsReported_AndIgnored()
        {
            var problems = new List<string>();
            PlatformBalance b = Read("{ 'schema': 1, 'mystery': 1, 'rating': { 'ibMaxBonuss': 0.3 }, 'oem': { 'extra': true } }", problems);
            Assert.Equal(3, problems.Count, string.Join("; ", problems));
            Assert.True(PlatformTestData.AnyContains(problems, "mystery"));
            Assert.True(PlatformTestData.AnyContains(problems, "ibMaxBonuss"));
            Assert.True(PlatformTestData.AnyContains(problems, "extra"));
            AssertSameAsDefaults(b, "unknown members");
        }

        [Test]
        public void FromJson_ModeIsReadWithoutRegardToCase()
        {
            Assert.Equal(OemMode.MarketAverage, Read("{ 'oem': { 'mode': 'MARKETAVERAGE' } }", null).Oem.Mode);
            Assert.Equal(OemMode.Auto, Read("{ 'oem': { 'mode': 'Auto' } }", null).Oem.Mode);
        }

        [Test]
        public void FromJson_ContractTypes_DropRepeats_AndMayBeEmpty()
        {
            var problems = new List<string>();
            Assert.Equal(new[] { 1, 2 }, Read("{ 'oem': { 'contractTypes': [ 1, 1, 2, 2, 1 ] } }", problems).Oem.ContractTypes);
            Assert.Equal(0, problems.Count);

            PlatformBalance none = Read("{ 'oem': { 'contractTypes': [] } }", problems);
            Assert.Equal(0, none.Oem.ContractTypes.Length, "an empty list switches the OEM effect off for every type");
            Assert.False(none.Oem.IncludesContractType(1));
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void IncludesContractType_WithANullList_IsFalse()
        {
            var b = PlatformBalance.Defaults();
            b.Oem.ContractTypes = null;
            Assert.False(b.Oem.IncludesContractType(1));
        }

        [Test]
        public void FromJson_WithoutAProblemList_DoesNotThrow()
        {
            PlatformBalance b = Read("{ 'rating': { 'ibMaxBonus': 9, 'minMult': 'x' }, 'oem': { 'mode': 7, 'contractTypes': 5 }, 'zzz': 1 }", null);
            AssertSameAsDefaults(b, "broken values without a list");
        }

        private static void AssertSameAsDefaults(PlatformBalance b, string what)
        {
            PlatformBalance d = PlatformBalance.Defaults();
            Assert.Equal(d.InstalledBase.LifetimeMonths, b.InstalledBase.LifetimeMonths, what);
            Assert.Near(d.InstalledBase.ContractUnitWeight, b.InstalledBase.ContractUnitWeight, 1e-9, what);
            Assert.Near(d.Rating.IbMaxBonus, b.Rating.IbMaxBonus, 1e-9, what);
            Assert.Near(d.Rating.IbSaturationShare, b.Rating.IbSaturationShare, 1e-9, what);
            Assert.Near(d.Rating.SharedWeight, b.Rating.SharedWeight, 1e-9, what);
            Assert.Near(d.Rating.EarlyBonus, b.Rating.EarlyBonus, 1e-9, what);
            Assert.Equal(d.Rating.EarlyDurationMonths, b.Rating.EarlyDurationMonths, what);
            Assert.Equal(d.Rating.EarlyAdopterSlots, b.Rating.EarlyAdopterSlots, what);
            Assert.Near(d.Rating.ChurnPenalty, b.Rating.ChurnPenalty, 1e-9, what);
            Assert.Equal(d.Rating.MinSocketLifeMonths, b.Rating.MinSocketLifeMonths, what);
            Assert.Equal(d.Rating.ChurnDurationMonths, b.Rating.ChurnDurationMonths, what);
            Assert.Near(d.Rating.ChurnMinBaseShare, b.Rating.ChurnMinBaseShare, 1e-9, what);
            Assert.Equal(d.Rating.CustomOnSameBaseIsCompatible, b.Rating.CustomOnSameBaseIsCompatible, what);
            Assert.Near(d.Rating.IndustriesWeight, b.Rating.IndustriesWeight, 1e-9, what);
            Assert.Near(d.Rating.MinMult, b.Rating.MinMult, 1e-9, what);
            Assert.Near(d.Rating.MaxMult, b.Rating.MaxMult, 1e-9, what);
            Assert.Equal(d.Oem.Mode, b.Oem.Mode, what);
            Assert.Equal(d.Oem.ContractTypes, b.Oem.ContractTypes, what);
            Assert.Equal(d.Oem.MarketAverageMinGenOrder, b.Oem.MarketAverageMinGenOrder, what);
            Assert.Near(d.Oem.AdoptBonus, b.Oem.AdoptBonus, 1e-9, what);
            Assert.Near(d.Oem.LagPenalty1, b.Oem.LagPenalty1, 1e-9, what);
            Assert.Near(d.Oem.LagPenalty2, b.Oem.LagPenalty2, 1e-9, what);
            Assert.Near(d.Oem.FullPlatformBonus, b.Oem.FullPlatformBonus, 1e-9, what);
            Assert.Near(d.Oem.OemLoyaltyBonus, b.Oem.OemLoyaltyBonus, 1e-9, what);
            Assert.Near(d.Oem.MinChanceMult, b.Oem.MinChanceMult, 1e-9, what);
            Assert.Near(d.Oem.MaxChanceMult, b.Oem.MaxChanceMult, 1e-9, what);
        }
    }
}
