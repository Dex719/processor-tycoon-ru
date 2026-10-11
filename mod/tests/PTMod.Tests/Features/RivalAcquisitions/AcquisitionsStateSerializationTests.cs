using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The holder of the section as the game has it (<c>AcquisitionsSaveParticipant</c>), without the game: the same <see cref="AcquisitionsSection"/> calls, handed to
    /// the core's own <see cref="SideFileModel"/>. Internal, so the runner never takes it for a test class.
    /// </summary>
    internal sealed class SectionHolder : ISideFileParticipant
    {
        public AcquisitionsState State = new AcquisitionsState();
        public readonly List<string> Problems = new List<string>();
        public int Reads;
        public int Resets;

        public string SectionId
        {
            get { return AcquisitionsSection.SectionId; }
        }

        public int SchemaVersion
        {
            get { return AcquisitionsSection.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            Resets++;
            State = new AcquisitionsState();
        }

        public void Read(JsonObject section, int schema)
        {
            Reads++;
            State = AcquisitionsSection.Read(section, Problems);
        }

        public JsonObject Write()
        {
            return AcquisitionsSection.Write(State, Problems);
        }
    }

    /// <summary>
    /// The save of the feature (TSK-020; FR-14, NFR-2, NFR-3, NFR-7): the section <c>rival-acquisitions</c> of <c>sample.ptmod.json</c> (a whole side file with the
    /// section of the feature, one of the core and two of features this build does not know) goes through the core's <c>SideFileModel</c> the way the game's
    /// <c>ModSave</c> does it. Checked: the round trip of the DTO without a loss and without a version field of its own, the records that name a company the game no
    /// longer has (ignored, kept), the sections of others (kept as they were), the feature switched off between a deal and the save, a damaged section, and a save of
    /// a newer mod. That the core handles <c>_schema</c> (missing, newer) is the core's own test (mod-framework TSK-016); here only what the feature adds to it.
    /// </summary>
    [Covers(typeof(AcquisitionsSection))]
    public class AcquisitionsStateSerializationTests
    {
        private static readonly string[] DtoKeys =
        {
            "deals", "stakes", "auctions", "rescueOrders", "defaultMailSentForSaveIds", "lastHostileOn", "backfillDone", "nextDealId",
        };

        // ---- helpers ----

        private static string SampleText()
        {
            Assert.NotNull(TestEnv.ModDir, "mod folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "RivalAcquisitions", "Data", "sample.ptmod.json");
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error);
            return (JsonObject)node;
        }

        private static JsonObject Features(JsonObject file)
        {
            return (JsonObject)file["features"];
        }

        private static JsonObject OurSection(JsonObject file)
        {
            return (JsonObject)Features(file)[AcquisitionsSection.SectionId];
        }

        /// <summary>The section without its <c>_schema</c>: what the participant sees.</summary>
        private static JsonObject WithoutVersion(JsonObject section)
        {
            var copy = new JsonObject();
            foreach (KeyValuePair<string, JsonNode> member in section)
            {
                if (member.Key != SideFileModel.SchemaKey) copy.Set(member.Key, member.Value);
            }
            return copy;
        }

        private static SideFileModel NewModel(SectionHolder holder, Func<string, bool> isActive = null)
        {
            var model = new SideFileModel(isActive);
            string problem;
            Assert.True(model.TryRegister(holder, out problem), "the holder registers: " + problem);
            return model;
        }

        private static SideFileHeader Header()
        {
            return new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", saveName = "1987_6_1", gameDate = "1987-06-01", writtenUtc = "2026-10-10T18:42:07Z" };
        }

        private static JsonObject Build(SideFileModel model)
        {
            SideFileBuildReport report = model.Build(Header());
            Assert.Equal(0, report.Warnings.Count, "the build has no warnings: " + string.Join(" | ", report.Warnings.ToArray()));
            return report.Document;
        }

        private static void AssertSameJson(JsonNode expected, JsonNode actual, string what)
        {
            Assert.True(expected != null && expected.Equals(actual), what + ": expected " + (expected == null ? "null" : ModJson.Write(expected, false)) + " but got "
                                                                   + (actual == null ? "null" : ModJson.Write(actual, false)));
        }

        // ---- the sample ----

        [Test]
        public void TheSample_IsAWholeSideFile_WithTheSectionOfTheFeatureAndOthers()
        {
            JsonObject file = Parse(SampleText());

            Assert.Equal(SideFileModel.FileSchema, file.GetInt32("schema"));
            JsonObject features = Features(file);
            foreach (string id in new[] { "mod-framework", "rival-acquisitions", "gpu-product-line", "feature-that-was-removed" }) Assert.True(features.ContainsKey(id), id);

            JsonObject ours = OurSection(file);
            Assert.True(ours.ContainsKey(SideFileModel.SchemaKey), "the core's version field is in the file");
            foreach (string key in DtoKeys) Assert.True(ours.ContainsKey(key), "the section has " + key);
            Assert.Equal(DtoKeys.Length + 1, ours.Count, "the section holds the eight fields of the books and the core's _schema, nothing else");
        }

        [Test]
        public void TheSample_IsReadByTheCore_IntoTheBooks_WithoutAProblem()
        {
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);

            SideFileReadReport report = model.Apply(SampleText());

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join(" | ", report.Warnings.ToArray()));
            Assert.Equal(1, holder.Reads);
            Assert.Equal(0, holder.Problems.Count, string.Join(" | ", holder.Problems.ToArray()));

            AcquisitionsState s = holder.State;
            Assert.Equal(new[] { "Hostile", "BankruptAssets", "Distressed", "Stake", "RescueOrder" }, s.deals.Select(d => d.type).ToArray());
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, s.deals.Select(d => d.id).ToArray());
            Assert.True(s.deals[2].partial);
            Assert.True(s.deals[2].notes.Contains("step 9"));
            Assert.Equal(new[] { 45, 46 }, s.deals[1].lines.Select(l => l.waferId).ToArray());
            Assert.Equal(new[] { 1015 }, s.deals[1].retiredCpuSaveIds.ToArray());
            Assert.Equal(2, s.stakes.Count);
            Assert.Near(0.35, s.ShareOf(10));
            Assert.Near(1000000.0, s.stakes[0].lastDividend);
            Assert.Equal(3, s.auctions.Count);
            Assert.True(s.auctions[1].closed);
            Assert.Equal(2, s.rescueOrders.Count);
            Assert.Equal(new[] { 388, 401 }, s.defaultMailSentForSaveIds.ToArray());
            Assert.Equal("1985-03-01", s.lastHostileOn);
            Assert.True(s.backfillDone);
            Assert.Equal(6, s.nextDealId);
            Assert.True(s.IsAbsorbed(3) && s.IsAbsorbed(7) && s.IsAbsorbed(5), "Hostile, BankruptAssets and Distressed deals absorb their targets");
            Assert.False(s.IsAbsorbed(10), "a stake and a rescue order do not");

            var problems = new List<string>();
            Assert.Equal(0, s.Sanitize(problems), "the sample is valid: " + string.Join(" | ", problems.ToArray()));
            Assert.Equal(0, problems.Count);
        }

        // ---- the round trip ----

        [Test]
        public void TheDto_RoundTripsTheSampleSection_WithoutALoss_AndWithoutAVersionField()
        {
            JsonObject section = WithoutVersion(OurSection(Parse(SampleText())));
            var problems = new List<string>();

            AcquisitionsState state = AcquisitionsSection.Read(section, problems);
            JsonObject written = AcquisitionsSection.Write(state, problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems.ToArray()));
            AssertSameJson(section, written, "the section read and written");
            foreach (string name in new[] { "schema", "_schema", "version", "Version" }) Assert.False(written.ContainsKey(name), "no '" + name + "' in what the participant writes");
            Assert.Equal(DtoKeys.OrderBy(k => k).ToArray(), written.Keys.OrderBy(k => k).ToArray());

            // and once more: reading what was written gives the same books
            AcquisitionsState again = AcquisitionsSection.Read(written, problems);
            AssertSameJson(written, AcquisitionsSection.Write(again, problems), "the second round");
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void TheEmptyBooks_WriteTheEightFields_AndReadBackEmpty()
        {
            var problems = new List<string>();
            JsonObject written = AcquisitionsSection.Write(new AcquisitionsState(), problems);

            Assert.Equal(0, problems.Count);
            Assert.Equal(DtoKeys.OrderBy(k => k).ToArray(), written.Keys.OrderBy(k => k).ToArray());

            AcquisitionsState back = AcquisitionsSection.Read(written, problems);
            Assert.Equal(0, back.deals.Count + back.stakes.Count + back.auctions.Count + back.rescueOrders.Count);
            Assert.Equal(1, back.nextDealId);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void ANullSection_OrNullBooks_AreHandledWithoutThrowing()
        {
            var problems = new List<string>();

            AcquisitionsState state = AcquisitionsSection.Read(null, problems);
            JsonObject written = AcquisitionsSection.Write(null, problems);

            Assert.NotNull(state);
            Assert.Equal(1, state.nextDealId);
            Assert.NotNull(written);
            Assert.True(written.ContainsKey("deals"));
            Assert.NotNull(AcquisitionsSection.Read(new JsonObject(), null));
            Assert.NotNull(AcquisitionsSection.Write(new AcquisitionsState(), null));
        }

        [Test]
        public void OffersClosed_IsNotAFieldOfTheSection()
        {
            var state = new AcquisitionsState();
            state.CloseOffers();
            Assert.True(state.OffersClosed);

            JsonObject written = AcquisitionsSection.Write(state, null);
            Assert.False(written.Keys.Any(k => string.Equals(k, "offersClosed", StringComparison.OrdinalIgnoreCase)), "the lock is not saved");

            JsonObject section = Parse("{ \"OffersClosed\": true, \"offersClosed\": true, \"nextDealId\": 4 }");
            AcquisitionsState read = AcquisitionsSection.Read(section, null);
            Assert.False(read.OffersClosed, "a file cannot close the offers");
            Assert.Equal(4, read.nextDealId);
        }

        // ---- through the core's model ----

        [Test]
        public void ThroughTheCoreModel_EverySectionAndTheHeaderOfTheSampleSurvive()
        {
            JsonObject original = Parse(SampleText());
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            model.Apply(SampleText());

            JsonObject built = Build(model);

            Assert.Equal(SideFileModel.FileSchema, built.GetInt32("schema"));
            foreach (string name in new[] { "modVersion", "gameVersion", "saveName", "gameDate", "writtenUtc" })
            {
                AssertSameJson(original[name], built[name], "header field " + name);
            }
            foreach (KeyValuePair<string, JsonNode> member in Features(original))
            {
                AssertSameJson(member.Value, Features(built)[member.Key], "the section " + member.Key);
            }
            Assert.Equal(Features(original).Count, Features(built).Count);
        }

        [Test]
        public void TheSectionsOfOthers_AreNotLost_WhenTheFeatureChangesItsBooks()
        {
            JsonObject original = Parse(SampleText());
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            model.Apply(SampleText());

            holder.State.AddDeal(new DealRecord { type = "Hostile", targetUniqueId = 0, targetSaveId = 400, targetName = "Intel", date = "1987-06-01", price = 5e8 });
            holder.State.lastHostileOn = "1987-06-01";
            JsonObject built = Build(model);

            foreach (string id in new[] { "mod-framework", "gpu-product-line", "feature-that-was-removed" })
            {
                AssertSameJson(Features(original)[id], Features(built)[id], "the section of others, " + id);
            }
            JsonObject ours = OurSection(built);
            Assert.Equal(AcquisitionsSection.SchemaVersion, ours.GetInt32(SideFileModel.SchemaKey));
            AcquisitionsState back = AcquisitionsSection.Read(WithoutVersion(ours), null);
            Assert.Equal(6, back.deals.Count);
            Assert.Equal(6, back.deals[5].id, "the new deal got the next number");
            Assert.Equal(7, back.nextDealId);
            Assert.Equal("1987-06-01", back.lastHostileOn);
        }

        [Test]
        public void ASaveWithoutOurSection_StartsEmpty_AndGetsTheSectionOnTheNextSave()
        {
            // NFR-3: no section - the defaults; the auctions of the existing bankrupts come from the backfill (backfillDone is false).
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            string withoutOurs = "{ \"schema\": 1, \"features\": { \"gpu-product-line\": { \"_schema\": 2, \"x\": 1 } } }";

            SideFileReadReport report = model.Apply(withoutOurs);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, holder.Reads);
            Assert.True(holder.Resets >= 1, "the holder was given its defaults");
            Assert.False(holder.State.backfillDone, "the backfill of the auctions is still due");
            Assert.Equal(1, holder.State.nextDealId);

            JsonObject built = Build(model);
            Assert.True(Features(built).ContainsKey(AcquisitionsSection.SectionId), "the section appears on the next save");
            Assert.Equal(2, Features(built).Count);
        }

        [Test]
        public void ASaveWithoutAnySideFile_GivesDefaults()
        {
            var holder = new SectionHolder();
            holder.State.AddDeal(new DealRecord { type = "Hostile", targetUniqueId = 3, targetSaveId = 412, date = "1985-03-01" });
            SideFileModel model = NewModel(holder);

            model.ApplyMissing();

            Assert.Equal(0, holder.State.deals.Count, "a vanilla or pre-mod save starts with empty books");
            Assert.False(holder.State.OffersClosed);
        }

        // ---- records that name a company the game no longer has ----

        [Test]
        public void StaleRecords_AreIgnoredByTheReads_ButKeptInTheSection()
        {
            // The pair (UniqueID, SaveID) names a company. A record whose SaveID is not the one of the company with that UniqueID now (a new game with the same ids, a
            // company that is gone) belongs to nobody: the reads pass it over, Sanitize does not drop it, the save writes it back.
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            model.Apply(SampleText());
            AcquisitionsState s = holder.State;
            DateTime today = new DateTime(1987, 6, 1);

            Assert.Near(0.35, CompanyStatusRules.OwnedShare(s, 10, 388), 1e-12, "the stake of the company it was bought in");
            Assert.Near(0.0, CompanyStatusRules.OwnedShare(s, 10, 999), 1e-12, "another company with the same UniqueID is not owned by the player");
            Assert.Near(0.0, CompanyStatusRules.OwnedShare(s, 11, 388), 1e-12, "another UniqueID");

            AuctionData auction = s.auctions[0];
            Assert.True(CompanyStatusRules.IsAuctionOpen(auction, auction.targetSaveId, today));
            Assert.False(CompanyStatusRules.IsAuctionOpen(auction, auction.targetSaveId + 1, today), "the auction of another company is stale");
            Assert.Null(CompanyStatusRules.OpenAuction(s, auction.targetUniqueId, auction.targetSaveId + 1, today));
            Assert.Equal(CompanyStatus.Bankrupt, CompanyStatusRules.Resolve(true, false, false, auction, auction.targetSaveId + 1, today), "no open auction for a stale record");
            Assert.Equal(CompanyStatus.BankruptAuction, CompanyStatusRules.Resolve(true, false, false, auction, auction.targetSaveId, today));

            var problems = new List<string>();
            Assert.Equal(0, s.Sanitize(problems), "nothing is dropped for a missing company");

            JsonObject built = Build(model);
            AcquisitionsState back = AcquisitionsSection.Read(WithoutVersion(OurSection(built)), null);
            Assert.Equal(2, back.stakes.Count, "both stakes are in the saved section");
            Assert.Equal(3, back.auctions.Count);
            Assert.Equal(2, back.rescueOrders.Count);
        }

        [Test]
        public void ARecordOfAGoneCompany_IsReplacedByAPurchaseOfTheNewOneWithTheSameUniqueId_NotMerged()
        {
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            model.Apply(SampleText());

            double now = StakeBook.AddShare(holder.State, 10, 999, 0.1, 1000.0, new DateTime(1987, 6, 1));

            Assert.Near(0.1, now, 1e-12, "the 35% of the old company does not add to the new one");
            Assert.Near(0.1, CompanyStatusRules.OwnedShare(holder.State, 10, 999), 1e-12);
            Assert.Equal(1, holder.State.stakes.Count(x => x.targetUniqueId == 10));
        }

        // ---- the feature is off between a deal and the save (edge case 13) ----

        [Test]
        public void AFeatureSwitchedOffAfterADeal_KeepsItsLastSavedSection()
        {
            bool active = true;
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder, id => id != AcquisitionsSection.SectionId || active);
            model.Apply(SampleText());
            holder.State.AddDeal(new DealRecord { type = "Hostile", targetUniqueId = 0, targetSaveId = 400, targetName = "Intel", date = "1987-06-01", price = 5e8 });
            JsonObject withDeal = Build(model);   // a save while the feature is on

            active = false;                       // the player switches it off in the hub
            holder.State = new AcquisitionsState();   // whatever the holder has now, the file is not taken from it
            JsonObject whileOff = Build(model);

            AssertSameJson(OurSection(withDeal), OurSection(whileOff), "the section while the feature is off");
            Assert.Equal(6, AcquisitionsSection.Read(WithoutVersion(OurSection(whileOff)), null).deals.Count, "the deal is still in the file");
        }

        [Test]
        public void AFeatureSwitchedOnInTheMiddleOfASession_GetsItsSectionBack()
        {
            bool active = false;
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder, id => id != AcquisitionsSection.SectionId || active);
            model.Apply(SampleText());
            Assert.Equal(0, holder.Reads, "the section of a feature that is off is not read");
            JsonObject whileOff = Build(model);
            AssertSameJson(OurSection(Parse(SampleText())), OurSection(whileOff), "the section passes through while the feature is off");

            active = true;
            model.RestoreParticipant(AcquisitionsSection.SectionId);

            Assert.Equal(1, holder.Reads);
            Assert.Equal(5, holder.State.deals.Count);
            Assert.Near(0.35, holder.State.ShareOf(10), 1e-12, "the stakes work again");
        }

        // ---- a damaged section ----

        [Test]
        public void ADamagedSection_IsReadAsFarAsItGoes_AndTheOtherSectionsAreUntouched()
        {
            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            string text = "{ \"schema\": 1, \"features\": { \"rival-acquisitions\": { \"_schema\": 1, \"deals\": \"none\","
                          + " \"stakes\": [ { \"targetUniqueId\": 10, \"targetSaveId\": 388, \"share\": 0.9 }, { \"targetUniqueId\": 11, \"targetSaveId\": 389, \"share\": 0.2, \"since\": \"1986-01-01\" } ],"
                          + " \"nextDealId\": \"x\", \"backfillDone\": true },"
                          + " \"gpu-product-line\": { \"_schema\": 2, \"x\": [ 1, 2 ] } } }";

            SideFileReadReport report = model.Apply(text);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.NotNull(holder.State);
            Assert.True(holder.Problems.Count > 0, "every problem is a line for the log");
            Assert.True(holder.Problems.All(p => p.StartsWith("section:")), "the lines say where they come from");
            Assert.True(holder.State.backfillDone, "the field that binds is kept");
            Assert.Equal(1, holder.State.stakes.Count, "the stake above 49% is dropped, the good one stays");
            Assert.Equal(11, holder.State.stakes[0].targetUniqueId);
            Assert.Equal(1, holder.State.nextDealId);

            JsonObject built = Build(model);
            AssertSameJson(Features(Parse(text))["gpu-product-line"], Features(built)["gpu-product-line"], "the section of others");
        }

        // ---- a save of a newer mod ----

        [Test]
        public void ASectionOfANewerMod_IsKeptUnchanged_AndTheFeatureGetsDefaults()
        {
            JsonObject newer = Parse(SampleText());
            OurSection(newer).Set(SideFileModel.SchemaKey, 2);
            OurSection(newer).Set("somethingNew", Parse("{ \"a\": [ 1, 2, 3 ] }"));
            string text = ModJson.Write(newer, true);

            var holder = new SectionHolder();
            SideFileModel model = NewModel(holder);
            SideFileReadReport report = model.Apply(text);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(new[] { AcquisitionsSection.SectionId }, report.NewerSections.ToArray());
            Assert.Equal(0, holder.Reads, "a section of a newer version is not read");
            Assert.Equal(0, holder.State.deals.Count, "the feature has defaults");
            Assert.True(AcquisitionsSection.IsNewerThanSupported(text), "and knows the save is of a newer mod");

            JsonObject built = Build(model);
            AssertSameJson(OurSection(newer), OurSection(built), "the section as it was in the file");
        }

        [Test]
        public void IsNewerThanSupported_ChecksTheFileAndOurSectionOnly()
        {
            Assert.False(AcquisitionsSection.IsNewerThanSupported(SampleText()), "the sample is of this version");
            Assert.False(AcquisitionsSection.IsNewerThanSupported(null));
            Assert.False(AcquisitionsSection.IsNewerThanSupported(""));
            Assert.False(AcquisitionsSection.IsNewerThanSupported("not json at all"));
            Assert.False(AcquisitionsSection.IsNewerThanSupported("[1, 2]"));
            Assert.False(AcquisitionsSection.IsNewerThanSupported("{ \"schema\": 1, \"features\": { } }"), "no section of ours");
            Assert.False(AcquisitionsSection.IsNewerThanSupported("{ \"schema\": 1, \"features\": { \"rival-acquisitions\": { } } }"), "a section without _schema is version 1");
            Assert.False(AcquisitionsSection.IsNewerThanSupported("{ \"schema\": 1, \"features\": { \"rival-acquisitions\": { \"_schema\": \"2\" } } }"), "not a number: the core's business");

            // another feature's newer section is not ours
            JsonObject otherNewer = Parse(SampleText());
            ((JsonObject)Features(otherNewer)["gpu-product-line"]).Set(SideFileModel.SchemaKey, 9);
            Assert.False(AcquisitionsSection.IsNewerThanSupported(ModJson.Write(otherNewer, false)), "the newer section of another feature does not close our offers");

            Assert.True(AcquisitionsSection.IsNewerThanSupported("{ \"schema\": 1, \"features\": { \"rival-acquisitions\": { \"_schema\": 2 } } }"));
            Assert.True(AcquisitionsSection.IsNewerThanSupported("{ \"schema\": 2, \"features\": { } }"), "a file of a newer format");
        }

        [Test]
        public void TheGameSide_UsesTheSameSectionLogic_AndClosesTheOffersOfANewerSave()
        {
            string participant = System.IO.File.ReadAllText(SourcePath("AcquisitionsSaveParticipant.cs"));
            Assert.True(participant.Contains("AcquisitionsSection.Read(section, problems)"), "the participant reads through the pure section logic");
            Assert.True(participant.Contains("AcquisitionsSection.Write(_feature.State, problems)"), "and writes through it");
            Assert.False(participant.Contains("ModJson."), "no JSON handling of its own");

            string feature = System.IO.File.ReadAllText(SourcePath("RivalAcquisitionsFeature.cs"));
            Assert.True(feature.Contains("ModSave.AfterRead += OnSaveRead"), "the check runs once the save is read");
            Assert.True(feature.Contains("ModSave.Status != SideFileStatus.Newer"), "only for a save of a newer mod");
            Assert.True(feature.Contains("AcquisitionsSection.IsNewerThanSupported(text)"), "the version of OUR section decides");
            Assert.True(feature.Contains("State.CloseOffers()"), "NFR-3: no deal for that save");
        }

        private static string SourcePath(string file)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions", file);
            Assert.True(File.Exists(path), "missing " + path);
            return path;
        }

        [Test]
        public void ASaveOfANewerMod_OffersNoDeal_AndNothingElseChanges()
        {
            // NFR-3: "the feature offers no deal for this save". Every kind of deal says why.
            var state = new AcquisitionsState();
            state.CloseOffers();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();

            foreach (DealType type in new[] { DealType.BankruptAssets, DealType.Distressed, DealType.Hostile, DealType.Stake, DealType.RescueOrder })
            {
                DealQuote quote = RivalTestData.Quote(type, t, p, state);
                Assert.False(quote.Available, type + " is not offered");
                Assert.Equal(AcqKeys.ReasonSaveNewer, quote.ReasonKey, type.ToString());
            }

            // the same books, not closed, offer the hostile takeover of a healthy company
            Assert.True(RivalTestData.Quote(DealType.Hostile, t, p, new AcquisitionsState()).Available);
        }
    }
}
