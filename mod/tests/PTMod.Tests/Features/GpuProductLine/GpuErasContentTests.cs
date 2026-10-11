using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The content of stage 2 as it is shipped (TSK-030, TSK-032; FR-11, FR-12): the six segments of the eras with their weights, years and requirements, the six eras, the seven
    /// vendors with the real names of the makers, the binding of AMD and Intel to the AI companies by <c>UniqueID</c>. The tables below are the tables of design.md, typed in again.
    /// </summary>
    public class GpuErasContentTests
    {
        private static GpuContentDb Shipped()
        {
            return GpuWorld.ShippedEras();
        }

        private static float[] F(params float[] values)
        {
            return values;
        }

        // ---- the segments ----

        // id | opens | closes | baseSize | sizeByDecade | budgetByDecade | wPerf | wUnder | wOver | wObs | wCons | consumptionTarget | missingFeatureFactor
        // The table of design.md with the calibration of TSK-045 (balance-report.md): the gaming segments are 2.5 times as big in the 1990s and 2.2 times in the 2000s and after (design: 1/4/6, 1/5/7, 1/4/3/2).
        private static readonly object[][] SegmentTable =
        {
            new object[] { "office2d", 1981, 2010, 3000000f, F(0, 0.6f, 3, 1, 0, 0, 0), F(399, 399, 149, 59, 59, 59, 59), 60f, 3.5f, 2.0f, 1.0f, 0f, 99f, 0.35f },
            new object[] { "gaming.high", 1996, 0, 1000000f, F(0, 0, 2.5f, 8.8f, 13.2f, 13.2f, 13.2f), F(399, 399, 399, 499, 699, 999, 1199), 100f, 2.5f, 1.0f, 4.0f, 0f, 99f, 0.35f },
            new object[] { "gaming.mid", 1996, 0, 2000000f, F(0, 0, 2.5f, 11, 15.4f, 15.4f, 15.4f), F(199, 199, 199, 249, 299, 449, 499), 100f, 3.0f, 1.0f, 3.0f, 0f, 99f, 0.35f },
            new object[] { "gaming.low", 1998, 0, 3000000f, F(0, 0, 2.5f, 8.8f, 6.6f, 4.4f, 4.4f), F(99, 99, 99, 119, 149, 199, 199), 30f, 3.5f, 2.0f, 1.5f, 0f, 99f, 0.35f },
            new object[] { "compute", 2007, 0, 200000f, F(0, 0, 0, 0.5f, 2, 3, 3), F(2999, 2999, 2999, 2999, 4999, 7999, 7999), 200f, 3.5f, 2.0f, 2.0f, 30f, 250f, 0f },
            new object[] { "ai", 2016, 0, 100000f, F(0, 0, 0, 0, 0.5f, 8, 12), F(9999, 9999, 9999, 9999, 9999, 24999, 24999), 300f, 3.0f, 1.5f, 3.0f, 20f, 400f, 0f },
        };

        [Test]
        public void TheSixSegmentsOfTheEras_AreTheTableOfTheDesign()
        {
            GpuContentDb db = Shipped();
            Assert.Equal(SegmentTable.Select(r => (string)r[0]).ToArray(), db.Segments.Select(s => s.id).ToArray(), "the six segments, in this order");
            foreach (object[] row in SegmentTable)
            {
                string id = (string)row[0];
                GpuSegmentDto s = db.Segment(id);
                Assert.NotNull(s, id);
                Assert.Equal("eras", s.mode, id + ": mode");
                Assert.Equal("gpu.seg." + id, s.nameKey, id + ": nameKey");
                Assert.Equal((int)row[1], s.opensYear, id + ": opensYear");
                Assert.Equal((int)row[2], s.closesYear, id + ": closesYear");
                Assert.Equal((float)row[3], s.baseSize, id + ": baseSize");
                Assert.Equal((float[])row[4], s.sizeByDecade.ToArray(), id + ": sizeByDecade");
                Assert.Equal((float[])row[5], s.budgetByDecade.ToArray(), id + ": budgetByDecade");
                Assert.Equal(new[] { (float)row[6], (float)row[7], (float)row[8], (float)row[9], (float)row[10] }, new[] { s.wPerf, s.wUnder, s.wOver, s.wObs, s.wCons }, id + ": the weights");
                Assert.Equal((float)row[11], s.consumptionTarget, id + ": consumptionTarget");
                Assert.Equal((float)row[12], s.missingFeatureFactor, id + ": missingFeatureFactor");
            }
        }

        [Test]
        public void TheSegmentsOfTheEras_OpenAsFR111Says()
        {
            GpuContentDb db = Shipped();
            Assert.Equal("eras", db.Balance.marketMode);
            Assert.Equal(1981, db.Segment("office2d").opensYear);
            Assert.Equal(2010, db.Segment("office2d").closesYear, "office2d: 1981-2009");
            Assert.Equal(1996, db.Segment("gaming.high").opensYear);
            Assert.Equal(1996, db.Segment("gaming.mid").opensYear);
            Assert.Equal(1998, db.Segment("gaming.low").opensYear);
            Assert.Equal(2007, db.Segment("compute").opensYear);
            Assert.Equal(2016, db.Segment("ai").opensYear);
            foreach (GpuSegmentDto seg in db.Segments.Where(s => s.id != "office2d")) Assert.Equal(0, seg.closesYear, seg.id + " never closes");
        }

        [Test]
        public void TheRequirements_AreSetByControlYears_AndGrowWithTheYears()
        {
            GpuContentDb db = Shipped();
            foreach (GpuSegmentDto seg in db.Segments)
            {
                Assert.True(seg.featureReqs.Count >= 1, seg.id + ": the features are asked for from a year");
                Assert.True(seg.vramReq.Count >= 3, seg.id + ": the video memory has control years");
                for (int i = 1; i < seg.vramReq.Count; i++)
                {
                    Assert.True(seg.vramReq[i].year > seg.vramReq[i - 1].year, seg.id + ": control years go up");
                    Assert.True(seg.vramReq[i].v >= seg.vramReq[i - 1].v, seg.id + ": the memory asked for does not fall: " + seg.vramReq[i - 1].v + " -> " + seg.vramReq[i].v);
                }
                for (int i = 1; i < seg.featureReqs.Count; i++)
                {
                    Assert.True(seg.featureReqs[i].f.Count >= seg.featureReqs[i - 1].f.Count, seg.id + ": a segment asks for more features, never fewer");
                    foreach (string feature in seg.featureReqs[i - 1].f) Assert.True(seg.featureReqs[i].f.Contains(feature), seg.id + ": " + feature + " stays required");
                }
                Assert.True(seg.vramReq[0].year <= seg.opensYear + 1, seg.id + ": the memory is asked for from the opening of the segment");
                Assert.True(seg.featureReqs[0].year <= seg.opensYear, seg.id + ": and so are the features");
                foreach (GpuYearFeats reqs in seg.featureReqs)
                {
                    foreach (string feature in reqs.f)
                        Assert.True(db.Architectures.Any(a => a.features.Contains(feature)), seg.id + ": no architecture has the feature " + feature);
                }
            }

            // the gaming segments ask for the features the history gave: 3D, then T&L, shaders, unified shaders, ray tracing
            GpuSegmentDto high = db.Segment("gaming.high");
            Assert.Equal(new[] { 1996, 2000, 2003, 2008, 2020 }, high.featureReqs.Select(r => r.year).ToArray());
            Assert.Equal(new[] { "3d", "tnl", "shader", "unified", "rt" }, high.featureReqs[4].f.ToArray());
            Assert.Equal(new[] { "3d" }, db.Segment("gaming.low").featureReqs[0].f.ToArray(), "the budget segment asks for the 3D");
            Assert.Equal(new[] { "3d", "shader" }, db.Segment("gaming.low").featureReqs[1].f.ToArray(), "and from 2003 for the shader");
            Assert.Equal(new[] { "2d" }, db.Segment("office2d").featureReqs[0].f.ToArray());
            Assert.Equal(new[] { "compute" }, db.Segment("compute").featureReqs[0].f.ToArray(), "FR-11 item 2: no compute, no sales (factor 0)");
            Assert.Equal(new[] { "tensor" }, db.Segment("ai").featureReqs[0].f.ToArray());
        }

        [Test]
        public void TheMemoryOfTheSegments_FollowsTheDesign()
        {
            GpuContentDb db = Shipped();
            GpuSegmentDto high = db.Segment("gaming.high"), mid = db.Segment("gaming.mid"), low = db.Segment("gaming.low");
            Assert.Equal(new[] { 4f, 64f, 256f, 512f, 2048f, 8192f, 16384f, 24576f }, high.vramReq.Select(p => p.v).ToArray(), "the enthusiasts: the table of the design, and 256 MB in 2004");
            Assert.Equal(new[] { 1996, 2000, 2004, 2005, 2010, 2015, 2020, 2025 }, high.vramReq.Select(p => p.year).ToArray());
            // the mainstream asks for half of what the enthusiasts do (AC-11.3: 1024 MB in 2010), the budget segment for a quarter
            for (int i = 0; i < mid.vramReq.Count; i++)
            {
                Assert.Equal(high.vramReq[i].year, mid.vramReq[i].year);
                Assert.Near(high.vramReq[i].v / 2f, mid.vramReq[i].v, 1e-3, "gaming.mid at " + mid.vramReq[i].year);
            }
            Assert.Near(1024f, mid.vramReq.First(p => p.year == 2010).v, 1e-3, "AC-11.3");
            Assert.Near(128f, mid.vramReq.First(p => p.year == 2004).v, 1e-3, "AC-8.1");
            Assert.Near(64f, low.vramReq.First(p => p.year == 2004).v, 1e-3, "AC-8.1");
            Assert.Equal(new[] { 1024f, 16384f, 49152f }, db.Segment("compute").vramReq.Select(p => p.v).ToArray(), "the compute segment: the table of the design");
            Assert.True(db.Segment("ai").vramReq.Last().v <= db.Memories.Max(m => m.maxVramMB), "and the AI segment never asks for more than the best memory can carry");
        }

        [Test]
        public void Ac81_In2004_TheContentOpensFourSegments()
        {
            GpuContentDb db = Shipped();
            var date = new DateTime(2004, 6, 1);
            string[] open = db.Segments.Where(s => GpuEras.IsOpen(s, date)).Select(s => s.id).ToArray();
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, open);
            foreach (string id in new[] { "gaming.high", "gaming.mid", "gaming.low" })
                Assert.True(GpuMarketMath.RequiredFeatures(db.Segment(id).featureReqs, 2004).Contains("shader"), id + " asks for the shader in 2004");
        }

        [Test]
        public void TheBudgetsAndTheSizes_GiveEverySegmentBuyersInItsYears()
        {
            GpuContentDb db = Shipped();
            foreach (GpuSegmentDto seg in db.Segments)
            {
                int last = seg.closesYear > 0 ? seg.closesYear - 1 : 2035;
                for (int year = seg.opensYear; year <= last; year += 3)
                {
                    float size = GpuMarketMath.YearlySize(seg.baseSize, seg.sizeByDecade, year, GpuMarketMath.DecadeTransitionPort(new DateTime(year, 7, 1), 2030), 1f);
                    float budget = GpuMarketMath.DecadeLerp(seg.budgetByDecade, year, GpuMarketMath.DecadeTransitionPort(new DateTime(year, 7, 1), 2030));
                    Assert.True(GpuMarketMath.DailyBuyers(size) >= 1f, seg.id + " has buyers in " + year + ": " + size);
                    Assert.True(budget >= 59f, seg.id + " has a budget in " + year);
                }
            }
        }

        // ---- the eras ----

        [Test]
        public void TheSixEras_AreTheTableOfTheDesign()
        {
            GpuContentDb db = Shipped();
            string[] expected =
            {
                "era.2d|1981|arch.2d,vram.dram", "era.3d|1996|arch.3d,vram.sgram,cool.fan", "era.shader|2003|arch.shader,vram.gddr3",
                "era.compute|2007|arch.gpgpu,vram.gddr5", "era.ai|2016|arch.tensor,vram.hbm", "era.rt|2020|arch.rt,vram.gddr6",
            };
            Assert.Equal(expected.Select(r => r.Split('|')[0]).ToArray(), db.Eras.Select(e => e.id).ToArray());
            foreach (string row in expected)
            {
                string[] c = row.Split('|');
                GpuEraDto era = db.Era(c[0]);
                Assert.Equal(int.Parse(c[1]), era.startYear, c[0] + ": startYear");
                Assert.Equal(c[2].Split(','), era.hintNodes.ToArray(), c[0] + ": hintNodes");
                Assert.Equal("gpu.era." + c[0].Substring("era.".Length), era.nameKey);
                Assert.Equal("gpu.mail.era." + c[0].Substring("era.".Length), era.mailKey);
                foreach (string node in era.hintNodes) Assert.NotNull(db.Node(node), c[0] + ": the hint " + node + " is a research node");
            }
        }

        // ---- the vendors ----

        // id | name | series | bindTo | fallbackVirtual | entry | exit | mergeInto | mergeYear | techLag | priceFactor | capacityShare | interval | strength | segments | color
        // The table of design.md with the calibration of TSK-045: capacityShare is 0.6 of the design (0.35, 0.5, 0.4, 0.6, 0.5, 0.5, 0.3), so the sum of the makers of a segment leaves room for the others.
        private static readonly string[] VendorTable =
        {
            "matrox|Matrox|Millennium||false|1983|2005||0|1.5|0.95|0.21|24|0.6|consumer,office2d,gaming.low|#2E7D6B",
            "ati|ATI|Radeon||false|1985|0|amd.gpu|2006|0.75|0.9|0.3|15|0.8|consumer,office2d,gaming.high,gaming.mid,gaming.low|#C62828",
            "s3|S3|Savage||false|1989|2003||0|1.5|0.8|0.24|18|0.5|consumer,office2d,gaming.low|#6A1B9A",
            "nvidia|NVIDIA|GeForce||false|1995|0||0|0.5|1.05|0.36|12|1.0|consumer,gaming.high,gaming.mid,gaming.low,compute,ai|#76B900",
            "3dfx|3dfx|Voodoo||false|1996|0|nvidia|2001|0.25|1.1|0.3|12|0.9|consumer,gaming.high,gaming.mid|#F9A825",
            "amd.gpu|AMD|Radeon|ai:1|true|2006|0||0|0.75|0.9|0.3|15|0.8|consumer,gaming.high,gaming.mid,gaming.low,compute,ai|#D32F2F",
            "intel.gpu|Intel|Arc|ai:0|false|2022|0||0|1.5|0.85|0.18|18|0.5|consumer,gaming.mid,gaming.low,compute|#1565C0",
        };

        [Test]
        public void TheSevenVendors_AreTheTableOfTheDesign()
        {
            GpuContentDb db = Shipped();
            Assert.Equal(VendorTable.Select(r => r.Split('|')[0]).ToArray(), db.Vendors.Select(v => v.id).ToArray(), "Matrox, ATI, S3, NVIDIA, 3dfx, AMD, Intel");
            foreach (string row in VendorTable)
            {
                string[] c = row.Split('|');
                GpuVendorDto v = db.Vendor(c[0]);
                Assert.NotNull(v, c[0]);
                Assert.Equal(c[1], v.name.ru, c[0] + ": name.ru");
                Assert.Equal(c[1], v.name.en, c[0] + ": name.en");
                Assert.Equal(c[2], v.series, c[0] + ": series");
                Assert.Equal(c[3], v.bindTo ?? string.Empty, c[0] + ": bindTo");
                Assert.Equal(c[4] == "true", v.fallbackVirtual, c[0] + ": fallbackVirtual");
                Assert.Equal(int.Parse(c[5]), v.entryYear, c[0] + ": entryYear");
                Assert.Equal(int.Parse(c[6]), v.exitYear, c[0] + ": exitYear");
                Assert.Equal(c[7], v.mergeInto ?? string.Empty, c[0] + ": mergeInto");
                Assert.Equal(int.Parse(c[8]), v.mergeYear, c[0] + ": mergeYear");
                Assert.Equal(float.Parse(c[9], CultureInfo.InvariantCulture), v.techLag, c[0] + ": techLag");
                Assert.Equal(float.Parse(c[10], CultureInfo.InvariantCulture), v.priceFactor, c[0] + ": priceFactor");
                Assert.Equal(float.Parse(c[11], CultureInfo.InvariantCulture), v.capacityShare, c[0] + ": capacityShare");
                Assert.Equal(int.Parse(c[12]), v.designIntervalMonths, c[0] + ": designIntervalMonths");
                Assert.Equal(float.Parse(c[13], CultureInfo.InvariantCulture), v.strength, c[0] + ": strength");
                Assert.Equal(c[14].Split(','), v.segments.ToArray(), c[0] + ": segments");
                Assert.Equal(c[15], v.color, c[0] + ": color");
            }
        }

        [Test]
        public void TheReferences_OfTheVendors_AreSegmentsAndVendorsThatExist_AndTheBindingsAreAmdAndIntel()
        {
            GpuContentDb db = Shipped();
            var ids = new HashSet<string>(db.Vendors.Select(v => v.id));
            foreach (GpuVendorDto v in db.Vendors)
            {
                foreach (string segment in v.segments) Assert.NotNull(db.AnySegment(segment), v.id + ": the segment " + segment + " exists");
                Assert.True(string.IsNullOrEmpty(v.mergeInto) || ids.Contains(v.mergeInto), v.id + ": mergeInto is a vendor");
                Assert.True(new[] { string.Empty, "ai:0", "ai:1" }.Contains(v.bindTo ?? string.Empty), v.id + ": bindTo is empty, ai:0 (Intel) or ai:1 (AMD): " + v.bindTo);
                Assert.True(v.entryYear > 1970 && (v.exitYear == 0 || v.exitYear > v.entryYear), v.id + ": years");
                Assert.True(v.mergeYear == 0 || v.mergeYear > v.entryYear, v.id + ": it merges after it entered");
                Assert.True(v.exitYear == 0 || v.mergeYear == 0, v.id + ": it leaves or it merges, not both");
            }
            Assert.Equal("ai:1", db.Vendor("amd.gpu").bindTo, "AMD is UniqueID 1 (CompanyHelper.IsEMD(1))");
            Assert.Equal("ai:0", db.Vendor("intel.gpu").bindTo, "Intel is UniqueID 0 (CompanyHelper.IsInlet(0))");
            Assert.Equal(2, db.Vendors.Count(v => !string.IsNullOrEmpty(v.bindTo)), "NVIDIA, ATI, 3dfx, Matrox and S3 are virtual");
            Assert.True(db.Vendor("amd.gpu").fallbackVirtual, "AMD is a vendor even in a game without the company");
            Assert.False(db.Vendor("intel.gpu").fallbackVirtual);
            Assert.Equal("amd.gpu", db.Vendor("ati").mergeInto, "AC-12.2");
            Assert.Equal("nvidia", db.Vendor("3dfx").mergeInto);
        }

        [Test]
        public void EverySegmentOfTheEras_HasVendors_ThroughTheYears()
        {
            GpuContentDb db = Shipped();
            foreach (GpuSegmentDto seg in db.Segments)
            {
                // from 1983, when the first vendor (Matrox) enters; before it the background "other makers" is the whole market. The office segment loses its last
                // vendor in 2005 (Matrox leaves, ATI has merged into AMD, which does not sell office cards in the design): until it closes in 2010 it is the background's
                // and the cards ATI left.
                int lastYear = seg.id == "office2d" ? 2005 : seg.closesYear > 0 ? seg.closesYear - 1 : 2035;
                for (int year = Math.Max(seg.opensYear, 1983); year <= lastYear; year++)
                {
                    int vendors = db.Vendors.Count(v => db.ActiveSegmentIds(v).Contains(seg.id) && v.entryYear <= year && (v.exitYear == 0 || year < v.exitYear)
                                                         && (string.IsNullOrEmpty(v.mergeInto) || year < v.mergeYear));
                    Assert.True(vendors >= 1, seg.id + " has a vendor in " + year + ": " + vendors);
                }
            }
        }

        [Test]
        public void TheNames_AreTheRealOnes_NoParodiesAnywhereInTheContentOrTheTexts()
        {
            string[] parodies = { "Envidia", "ATY", "3Dfox", "Matrax", "S4", "EMD", "Inlet" };
            var texts = new List<KeyValuePair<string, string>>();
            foreach (string file in GpuFiles.ContentFiles)
                texts.Add(new KeyValuePair<string, string>(file + ".json", GpuFiles.Read(Path.Combine(GpuFiles.ShippedContentDir, file + ".json"))));
            foreach (string language in new[] { "ru", "en" })
                texts.Add(new KeyValuePair<string, string>("l10n/" + language, GpuFiles.Read(Path.Combine(TestEnv.SrcDir, "L10n", language, "gpu-product-line.json"))));

            foreach (KeyValuePair<string, string> text in texts)
            {
                foreach (string parody in parodies)
                {
                    int at = text.Value.IndexOf(parody, StringComparison.Ordinal);
                    // "EMD" and "Inlet" are the names of the game's own parodies of AMD and Intel; none of the words may appear as a word of the feature's text
                    while (at >= 0)
                    {
                        bool wordStart = at == 0 || !char.IsLetterOrDigit(text.Value[at - 1]);
                        bool wordEnd = at + parody.Length >= text.Value.Length || !char.IsLetterOrDigit(text.Value[at + parody.Length]);
                        Assert.False(wordStart && wordEnd, text.Key + " contains the parody name " + parody);
                        at = text.Value.IndexOf(parody, at + 1, StringComparison.Ordinal);
                    }
                }
            }

            GpuContentDb db = Shipped();
            Assert.Equal(new[] { "Matrox", "ATI", "S3", "NVIDIA", "3dfx", "AMD", "Intel" }, db.Vendors.Select(v => v.name.en).ToArray());
            Assert.Equal(new[] { "Millennium", "Radeon", "Savage", "GeForce", "Voodoo", "Radeon", "Arc" }, db.Vendors.Select(v => v.series).ToArray());
        }

        [Test]
        public void TheShippedSet_PassesTheValidation_WithoutWarnings()
        {
            var source = GpuFiles.Shipped();
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(source, problems);
            Assert.Equal(0, problems.Count, string.Join("\n  ", problems.ToArray()));
            Assert.Equal(0, source.ReportLines().Count, string.Join("\n  ", source.ReportLines().ToArray()));
            Assert.True(db.IsUsable);
            Assert.Equal(7, db.Vendors.Count);
            Assert.Equal(6, db.Eras.Count);
            Assert.Equal(6, db.Segments.Count);
        }
    }
}
