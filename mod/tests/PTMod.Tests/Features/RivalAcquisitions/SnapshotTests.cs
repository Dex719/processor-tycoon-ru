using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The snapshots and the status rule (TSK-005, FR-1, FR-2): what the data classes derive, how a company's status comes from the bankruptcy flags, the
    /// auctions and the journal, and that every key the logic hands to the window is defined in both languages.
    /// </summary>
    [Covers(typeof(CompanySnapshot), typeof(PlayerSnapshot), typeof(WaferLinesSnapshot), typeof(TechSnapshot), typeof(TechProgressSnapshot), typeof(NodeSnapshot),
        typeof(CpuSnapshot), typeof(ContractSnapshot), typeof(CompanyStatus), typeof(CompanyStatusRules), typeof(AcqKeys))]
    public class SnapshotTests
    {
        private static readonly DateTime Today = RivalTestData.Today;

        // ---- derived values of the data classes ----

        [Test]
        public void Technology_ValueIsCostTimesTimeOverThirty()
        {
            TechSnapshot tech = RivalTestData.Tech(1, 400000.0, 600.0);

            Assert.Near(8000000.0, tech.ResearchValue, 1e-6);
            Assert.False(tech.IsWafer);
            Assert.True(RivalTestData.Tech(2, type: TechSnapshot.WaferType).IsWafer);
        }

        [Test]
        public void Node_GapIsWhatTheTargetKnowsMore()
        {
            Assert.Near(0.6, new NodeSnapshot { TargetExperience = 0.8, PlayerExperience = 0.2 }.Gap, 1e-12);
            Assert.Near(0.0, new NodeSnapshot { TargetExperience = 0.2, PlayerExperience = 0.8 }.Gap, 1e-12, "a player who is ahead needs nothing");
        }

        [Test]
        public void Cpu_IsABaseWithoutABaseId()
        {
            Assert.True(RivalTestData.Cpu(1).IsBase);
            Assert.False(RivalTestData.Cpu(2, 1).IsBase);
        }

        [Test]
        public void Company_CountsItsLinesAndTheCpusOfThePlayerItMakes()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Lines.Add(RivalTestData.Wafer(1, 150, 5));
            t.Lines.Add(RivalTestData.Wafer(2, 200, 0));
            t.Lines.Add(null);
            t.PlayerCpuIdsMadeByTarget.Add(9);
            t.PlayerCpuIdsMadeByTarget.Add(10);

            Assert.Equal(45, t.TotalLines);
            Assert.Equal(2, t.PlayerCpusMadeByTarget);

            t.PlayerCpuIdsMadeByTarget = null;
            Assert.Equal(0, t.PlayerCpusMadeByTarget, "a missing list is an empty one");
        }

        [Test]
        public void Company_IsBankruptForBothBankruptStatuses()
        {
            var t = new CompanySnapshot();
            foreach (CompanyStatus status in new[] { CompanyStatus.BankruptAuction, CompanyStatus.Bankrupt })
            {
                t.Status = status;
                Assert.True(t.IsBankrupt, status.ToString());
            }
            foreach (CompanyStatus status in new[] { CompanyStatus.Healthy, CompanyStatus.InDefault, CompanyStatus.Absorbed })
            {
                t.Status = status;
                Assert.False(t.IsBankrupt, status.ToString());
            }
        }

        [Test]
        public void Player_FindsItsWafers()
        {
            PlayerSnapshot p = RivalTestData.Player();

            Assert.True(p.KnowsWafer(RivalTestData.WaferOld));
            Assert.False(p.KnowsWafer(12345));
            Assert.Equal(100.0, p.FindWafer(RivalTestData.WaferOld).Value);
            Assert.Null(p.FindWafer(12345));

            p.Wafers = null;
            Assert.False(p.KnowsWafer(RivalTestData.WaferOld), "no list, no wafers");
        }

        // ---- the status ----

        [Test]
        public void Status_IsHealthyOrInDefaultForALivingCompany()
        {
            Assert.Equal(CompanyStatus.Healthy, CompanyStatusRules.Resolve(false, false, false, null, 1, Today));
            Assert.Equal(CompanyStatus.InDefault, CompanyStatusRules.Resolve(false, true, false, null, 1, Today));
        }

        [Test]
        public void Status_ABankruptCompanyWithAnOpenAuction_IsOnAuction()
        {
            var auction = new AuctionData { targetUniqueId = 3, targetSaveId = 412, bankruptOn = "1984-06-01", until = "1985-06-01" };

            Assert.Equal(CompanyStatus.BankruptAuction, CompanyStatusRules.Resolve(true, false, false, auction, 412, Today));
        }

        [Test]
        public void Status_ABankruptCompanyWithoutAUsableAuction_IsPlainBankrupt()
        {
            var expired = new AuctionData { targetUniqueId = 3, targetSaveId = 412, bankruptOn = "1983-01-01", until = "1984-01-01" };
            var closed = new AuctionData { targetUniqueId = 3, targetSaveId = 412, bankruptOn = "1984-06-01", until = "1985-06-01", closed = true };
            var stale = new AuctionData { targetUniqueId = 3, targetSaveId = 999, bankruptOn = "1984-06-01", until = "1985-06-01" };
            var broken = new AuctionData { targetUniqueId = 3, targetSaveId = 412, bankruptOn = "1984-06-01", until = "not a date" };

            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, null, 412, Today), "no record: AC-3.3, the bankrupt without a deal");
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, expired, 412, Today));
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, closed, 412, Today));
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, stale, 412, Today), "a record of another company is ignored");
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, broken, 412, Today));
        }

        [Test]
        public void Status_TheLastDayOfTheAuction_IsStillOpen()
        {
            var auction = new AuctionData { targetUniqueId = 3, targetSaveId = 412, until = "1985-03-01" };

            Assert.Equal(CompanyStatus.BankruptAuction, CompanyStatusRules.Resolve(true, false, false, auction, 412, new DateTime(1985, 3, 1, 15, 0, 0)));
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, auction, 412, new DateTime(1985, 3, 2)));
        }

        [Test]
        public void Status_AnUnknownDate_DoesNotHideTheAuction()
        {
            var auction = new AuctionData { targetUniqueId = 3, targetSaveId = 412, until = "1985-03-01" };

            Assert.Equal(CompanyStatus.BankruptAuction, CompanyStatusRules.Resolve(true, false, false, auction, 412, default(DateTime)));
        }

        [Test]
        public void Status_ACompanyOfTheJournal_IsAbsorbedWhateverElseItIs()
        {
            Assert.Equal(CompanyStatus.Absorbed, CompanyStatusRules.Resolve(true, true, true, null, 412, Today));
            Assert.Equal(CompanyStatus.Absorbed, CompanyStatusRules.Resolve(false, false, true, null, 412, Today));
        }

        [Test]
        public void Auctions_AreFoundByUniqueIdAndSaveId()
        {
            var s = new AcquisitionsState();
            s.auctions.Add(new AuctionData { targetUniqueId = 3, targetSaveId = 412, until = "1985-06-01" });
            s.auctions.Add(new AuctionData { targetUniqueId = 4, targetSaveId = 413, until = "1985-06-01", closed = true });

            Assert.NotNull(CompanyStatusRules.OpenAuction(s, 3, 412, Today));
            Assert.Null(CompanyStatusRules.OpenAuction(s, 3, 999, Today), "the SaveID differs");
            Assert.Null(CompanyStatusRules.OpenAuction(s, 4, 413, Today), "closed");
            Assert.Null(CompanyStatusRules.OpenAuction(s, 5, 414, Today), "none");
            Assert.Null(CompanyStatusRules.OpenAuction(null, 3, 412, Today), "no books");
        }

        [Test]
        public void OwnedShare_ComesFromTheStakeOfTheVeryCompany()
        {
            var s = new AcquisitionsState();
            s.stakes.Add(new StakeData { targetUniqueId = 3, targetSaveId = 412, share = 0.25 });
            s.stakes.Add(new StakeData { targetUniqueId = 4, targetSaveId = 413, share = 0.6 });   // not a valid share

            Assert.Equal(0.25, CompanyStatusRules.OwnedShare(s, 3, 412));
            Assert.Equal(0.0, CompanyStatusRules.OwnedShare(s, 3, 999), "a stale record");
            Assert.Equal(0.0, CompanyStatusRules.OwnedShare(s, 4, 413), "a share of 0.5 or more is not read");
            Assert.Equal(0.0, CompanyStatusRules.OwnedShare(s, 5, 414));
            Assert.Equal(0.0, CompanyStatusRules.OwnedShare(null, 3, 412));
        }

        // ---- the keys ----

        [Test]
        public void EveryKeyOfTheLogic_IsDefinedInBothLanguages()
        {
            var keys = new List<string>();
            foreach (FieldInfo field in typeof(AcqKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string)) keys.Add((string)field.GetRawConstantValue());
            }
            Assert.True(keys.Count >= 30, "the reflection found the keys: " + keys.Count);

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                var missing = new List<string>();
                foreach (string key in keys)
                {
                    if (!key.StartsWith("acq.", StringComparison.Ordinal)) missing.Add(key + " (no acq. prefix)");
                    else if (table.GetString(key) == null) missing.Add(key);
                }
                Assert.True(missing.Count == 0, language + ": keys without a text: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void TheKeysWithAnArgument_HaveItsPlaceholder_InBothLanguages()
        {
            string[] withArgument =
            {
                AcqKeys.ReasonCooldown, AcqKeys.ReasonNoCash, AcqKeys.ReasonAuctionExpired, AcqKeys.RescueOverLimit, AcqKeys.RescueNoCash,
                AcqKeys.PlanFamilyBase, AcqKeys.ConsequenceGfAmdContract,
            };
            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                foreach (string key in withArgument) Assert.True(table.GetString(key).Contains("{0}"), language + ": " + key + " lacks {0}");
            }
        }

        private static JsonObject Table(string language)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "rival-acquisitions.json");
            Assert.True(File.Exists(path), "missing " + path);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(File.ReadAllText(path), out node, out error), path + ": " + error);
            return (JsonObject)node;
        }
    }
}
