using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The discount of the co-development (TSK-042; FR-17, F27): a card on a process node costs <c>coDevFactor</c> (0.85) times as much to develop when the company has released a CPU on the
    /// same node in the last <c>coDevYears</c> (2) years. AC-17.1 - a 65 nm CPU of 2006, a 65 nm card in 2007: the cost x0.85 and the line "Совместная разработка: −15 %" - on the designer, on the project
    /// and on the window; the line is hidden when the condition does not hold; the CPUs are read when the designer asks, there is no subscription to the release of a CPU.
    /// </summary>
    [Covers(typeof(GpuDesigner))]
    public class GpuCoDevTests
    {
        private static readonly DateTime Spring2007 = new DateTime(2007, 3, 10);

        private static GpuWorld World2007()
        {
            return UiWorlds.Ac31(Spring2007);
        }

        private static GpuDesignInput On(string node)
        {
            GpuDesignInput input = GpuWorld.Ac31Input();
            input.NodeName = node;
            return input;
        }

        // ---- AC-17.1 ----

        [Test]
        public void Ac171_ACpuOn65nmOf2006_MakesA65nmCardOf2007CostEightyFivePercent()
        {
            GpuWorld world = World2007();
            float plain = world.Designer.Evaluate(On("65nm")).ProjectCost;
            Assert.True(plain > 0f);

            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "65nm"));
            GpuStats stats = world.Designer.Evaluate(On("65nm"));
            Assert.Near(0.85 * plain, stats.ProjectCost, plain * 1e-6, "x0.85");
            Assert.Near(0.85, world.Designer.ContextFor(On("65nm")).CoDevFactor, 1e-6);
            Assert.Equal(stats.ProjectDays, world.Designer.Evaluate(On("90nm")).ProjectDays, "only the cost changes, not the time");
        }

        [Test]
        public void TheLine_IsShownInTheWindow_OnlyWhenTheConditionHolds()
        {
            GpuWorld world = World2007();
            var text = new TestGpuText();
            GpuDesignerModel model = UiWorlds.Designer(world, text);
            UiWorlds.SetAc31(model);
            model.SelectNode(UiWorlds.IndexOf(model.NodeLabels(), "65nm"));
            Assert.Equal(string.Empty, model.CoDevText, "no CPU on 65 nm: no line");
            float plain = model.Stats.ProjectCost;

            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "65nm"));
            model.Recompute();
            Assert.Equal("Совместная разработка: −15 %", model.CoDevText, "the line of AC-17.1");
            Assert.Near(0.85 * plain, model.Stats.ProjectCost, plain * 1e-6, "the cost the window shows has the discount in it");

            model.SelectNode(UiWorlds.IndexOf(model.NodeLabels(), "90nm"));
            Assert.Equal(string.Empty, model.CoDevText, "another node: no discount, no line");
            model.SelectNode(UiWorlds.IndexOf(model.NodeLabels(), "65nm"));
            Assert.Equal("Совместная разработка: −15 %", model.CoDevText, "back on 65 nm");

            var english = new TestGpuText("en");
            GpuDesignerModel en = UiWorlds.Designer(world, english);
            en.SelectNode(UiWorlds.IndexOf(en.NodeLabels(), "65nm"));
            Assert.Equal("Co-development: −15 %", en.CoDevText);
        }

        [Test]
        public void TheProject_Pays_TheDiscountedPrice_AndDailyCostFollows()
        {
            GpuWorld world = World2007();
            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "65nm"));
            float undiscounted = world.Designer.Evaluate(On("90nm")).ProjectCost;   // another node: the same parts, no discount

            GpuDesignInput input = On("65nm");
            float expected = world.Designer.Evaluate(input).ProjectCost;
            string reason;
            string id = world.Projects.Start(input, out reason);
            Assert.NotNull(id, reason);
            GpuProjectSave project = world.Projects.Find(id);
            Assert.Near(expected, project.totalCost, expected * 1e-6, "the project is started with the price of the window");
            Assert.Near(0.85, world.Designer.ContextFor(input).CoDevFactor, 1e-6);
            Assert.True(undiscounted > 0f);
            Assert.Near(expected / project.days, world.Projects.DailyCost(project), expected / project.days * 1e-4, "F14 on the discounted total");
        }

        // ---- the edges of the rule ----

        [Test]
        public void TheRule_IsTheCpuOfTheLastTwoYears_OnTheSameNodeByItsName()
        {
            GpuWorld world = World2007();
            Func<float> factor = () => world.Designer.ContextFor(On("65nm")).CoDevFactor;
            Assert.Equal(1f, factor(), "no CPUs at all");

            world.Game.PlayerCpus.Add(new GpuCpuRef(2004, "65nm"));
            Assert.Equal(1f, factor(), "2004 is three years back");
            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "90nm"));
            world.Game.PlayerCpus.Add(new GpuCpuRef(2007, "45nm"));
            Assert.Equal(1f, factor(), "recent CPUs, but on other nodes");
            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "65NM"));
            Assert.Equal(1f, factor(), "the name of the node is compared exactly");
            world.Game.PlayerCpus.Add(new GpuCpuRef(2005, "65nm"));
            Assert.Near(0.85, factor(), 1e-6, "2005 = 2007 - 2: the last year that counts");
            world.Game.PlayerCpus.Clear();
            world.Game.PlayerCpus.Add(new GpuCpuRef(2007, "65nm"));
            Assert.Near(0.85, factor(), 1e-6, "a CPU of this very year");
            world.Game.PlayerCpus.Clear();
            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, null));
            Assert.Equal(1f, factor(), "a CPU with no node is no match");
        }

        [Test]
        public void TheDiscount_FollowsTheYear_AndTheNumbersOfTheBalance()
        {
            GpuWorld world = World2007();
            world.Game.PlayerCpus.Add(new GpuCpuRef(2005, "65nm"));
            Assert.Near(0.85, world.Designer.ContextFor(On("65nm")).CoDevFactor, 1e-6);

            world.Game.Date = new DateTime(2008, 1, 2);
            Assert.Equal(1f, world.Designer.ContextFor(On("65nm")).CoDevFactor, "in 2008 the CPU of 2005 is three years old");

            world.Game.Date = Spring2007;
            GpuBalanceDto.SynergyBlock rules = world.Content.Balance.synergy;
            float factor = rules.coDevFactor;
            int years = rules.coDevYears;
            try
            {
                rules.coDevFactor = 0.5f;
                Assert.Near(0.5, world.Designer.ContextFor(On("65nm")).CoDevFactor, 1e-6, "coDevFactor is a number of the balance");
                rules.coDevYears = 1;
                Assert.Equal(1f, world.Designer.ContextFor(On("65nm")).CoDevFactor, "coDevYears is too");
                rules.coDevYears = years;
                rules.coDevFactor = 1f;
                Assert.Equal(1f, world.Designer.ContextFor(On("65nm")).CoDevFactor, "a factor of 1 is no discount");
            }
            finally
            {
                rules.coDevFactor = factor;
                rules.coDevYears = years;
            }
        }

        [Test]
        public void ADesignWithNoNode_HasNoDiscount_AndTheOtherCompaniesCpusAreNotRead()
        {
            GpuWorld world = World2007();
            world.Game.PlayerCpus.Add(new GpuCpuRef(2006, "65nm"));
            Assert.Equal(1f, world.Designer.CoDevFor(FakeGpuGame.Player, string.Empty, 2007));
            Assert.Equal(1f, world.Designer.CoDevFor(FakeGpuGame.Player, null, 2007));
            Assert.Near(0.85, world.Designer.CoDevFor(FakeGpuGame.Player, "65nm", 2007), 1e-6);

            world.Game.AddCompany("ai:1", "AMD").Cpus.Add(new GpuCpuRef(2006, "45nm"));
            Assert.Equal(1f, world.Designer.CoDevFor(FakeGpuGame.Player, "45nm", 2007), "the CPUs of an AI company do not give the player a discount (FR-17: the company's own CPUs)");
        }

        // ---- where the CPUs are read ----

        [Test]
        public void TheCpus_AreReadOnRequest_WithNoSubscriptionToTheirRelease()
        {
            string src = GpuFiles.FeatureSourceDir;
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"CpuReleased|OnNewCpu|AddCpu")) offenders.Add(SourceScan.Relative(src, path));
            }
            Assert.Equal(0, offenders.Count, "FR-17: no subscription to the release of a CPU: " + string.Join(", ", offenders.ToArray()));

            string designer = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Logic", "Services", "GpuDesigner.cs")), true);
            Assert.True(designer.Contains("_game.CpusOf(companyKey)"), "the designer asks the game for the CPUs when it needs the factor");

            string adapter = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Bridges", "GpuGameAdapter.cs")), true);
            Assert.True(adapter.Contains("owner.GetCpus()"), "the CPUs come from ICompanyOwner.GetCpus() of the company");
            Assert.True(Regex.IsMatch(adapter, @"if \(cpu == null \|\| !cpu\.IsReleased\) continue;"), "only released CPUs count");
        }
    }
}
