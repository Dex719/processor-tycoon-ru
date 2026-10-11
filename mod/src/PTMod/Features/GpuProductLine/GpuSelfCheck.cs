using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameCompanyHelper = ProcessorTycoon.CompanySystem.CompanyHelper;
using GameContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameCpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameDateHelper = ProcessorTycoon.TimeSystem.DateHelper;
using GameFactory = ProcessorTycoon.Production.Factory;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameProductionWindow = ProcessorTycoon.Production.UI.ProductionWindow;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The self-check of the feature (design.md, "Поля для рефлексии", "Самопроверка на старте"; TSK-011, NFR-2). It finds the two optional patches of the feature in
    /// <c>ModHooks.Report</c> (GP1 on <c>ProductionWindow.UpdateAvailableProductionLines</c>, GP2 on <c>Company.TotalLinesAllocated</c>), the six private fields
    /// (<c>Factory.totalAllocatedLines</c>, <c>Factory.totalDemandedLines</c> and four of <c>ProductionWindow</c>), the twelve members of the game the feature
    /// calls, and compares the ports of F1, F10 and F11 with the game on three sets of numbers; it checks that <c>CompanyHelper.IsEMD(1)</c> and
    /// <c>CompanyHelper.IsInlet(0)</c> are true (the vendors bind to AMD and Intel by these ids). What is missing switches off only the ability it serves
    /// (<see cref="GpuCapability"/>): without <c>Factory.totalAllocatedLines</c> the cards are not produced, the rest of the feature works. The line
    /// <c>self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)</c> goes to <c>Player.log</c> and to the page of the feature in the hub.
    /// </summary>
    internal static class GpuSelfCheck
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The target of GP1 as <c>ModHooks.Report</c> names it.</summary>
        public const string ProductionUiTarget = "ProductionWindow.UpdateAvailableProductionLines";

        /// <summary>The target of GP2.</summary>
        public const string SharedLearningTarget = "Company.TotalLinesAllocated";

        /// <summary>The last full run (with the ports), or null before the game was ready.</summary>
        public static GpuSelfCheckBook Last { get; private set; }

        /// <summary>
        /// Runs the checks. <paramref name="withPorts"/> compares the ports too (it needs the player's company: the game scene, after <c>GameReady</c>); without it
        /// only the patches, fields and members are looked for (at <c>Register</c>, so the abilities are right from the first day). The abilities of
        /// <paramref name="feature"/> follow the result.
        /// </summary>
        public static GpuSelfCheckBook Run(GpuFeature feature, bool withPorts)
        {
            var book = new GpuSelfCheckBook();
            CheckPatches(book);
            CheckFields(book);
            CheckCalls(book);
            if (withPorts)
            {
                try
                {
                    CheckPorts(book);
                }
                catch (Exception ex)
                {
                    // a member of the game the ports call is gone (the JIT fails on the method): the abilities found above still apply
                    book.Problem("the ports could not be compared: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            else
            {
                book.PortSkipped("F1/F10/F11", "the game is not ready yet");
            }

            if (feature != null)
            {
                foreach (GpuCapability capability in (GpuCapability[])Enum.GetValues(typeof(GpuCapability)))
                    feature.SetAvailable(capability, book.IsAvailable(capability));
            }
            if (withPorts) Last = book;
            return book;
        }

        // ---- patches ----

        private static void CheckPatches(GpuSelfCheckBook book)
        {
            bool gp1Declared = false, gp1 = false, gp2Declared = false, gp2 = false;
            foreach (PatchReport report in ModHooks.Report)
            {
                if (report == null || report.FeatureId != GpuFeature.FeatureId) continue;
                bool applied = report.Status == PatchStatus.Applied;
                if (report.Target == ProductionUiTarget)
                {
                    gp1Declared = true;
                    gp1 |= applied;
                }
                if (report.Target == SharedLearningTarget)
                {
                    gp2Declared = true;
                    gp2 |= applied;
                }
            }
            book.Patch("GP1 " + ProductionUiTarget, gp1Declared, gp1, GpuCapability.ProductionUi);
            book.Patch("GP2 " + SharedLearningTarget, gp2Declared, gp2, GpuCapability.SharedLearning);
        }

        // ---- fields ----

        private static void CheckFields(GpuSelfCheckBook book)
        {
            book.Field("Factory." + FactoryAccess.AllocatedField, FactoryAccess.HasIntField(FactoryAccess.AllocatedField), GpuCapability.Production);
            book.Field("Factory." + FactoryAccess.DemandedField, FactoryAccess.HasIntField(FactoryAccess.DemandedField), GpuCapability.Production);
            book.Field("ProductionWindow.linesUsedText", HasField(typeof(GameProductionWindow), "linesUsedText", "InfoText"), GpuCapability.ProductionUi);
            book.Field("ProductionWindow.productionUIs", HasField(typeof(GameProductionWindow), "productionUIs", "List`1"), GpuCapability.ProductionUi);
            book.Field("ProductionWindow.cpus", HasField(typeof(GameProductionWindow), "cpus", "List`1"), GpuCapability.ProductionUi);
            book.Field("ProductionWindow.clientCpus", HasField(typeof(GameProductionWindow), "clientCpus", "List`1"), GpuCapability.ProductionUi);
        }

        private static bool HasField(Type type, string name, string typeName)
        {
            FieldInfo field = type.GetField(name, AnyInstance);
            return field != null && field.FieldType.Name == typeName;
        }

        // ---- members of the game ----

        private static void CheckCalls(GpuSelfCheckBook book)
        {
            Type f = typeof(float), i = typeof(int), company = typeof(ICompany), node = typeof(GameProcessNode);
            GpuCapability production = GpuCapability.Production;
            Call(book, typeof(GameCpuMath), "TransistorCount", new[] { company, node, f }, null);
            Call(book, typeof(GameCpuMath), "RealUnitCost", new[] { company, f, f, f, f, typeof(bool), f, f }, production);
            Call(book, typeof(GameProductionMath), "DiesPerWafer", new[] { f, f, f }, production);
            Call(book, typeof(GameProductionMath), "WaferMaterialCost", new[] { f }, production);
            Call(book, typeof(GameModifiers), "GetModifier", new[] { typeof(GameModifierType), company }, null);
            Call(book, typeof(GameResearchMath), "ResearchSpeed", new[] { f, company }, null);
            Call(book, typeof(GameDateHelper), "DecadeTransitionValue", new[] { i, i }, null);
            Call(book, typeof(GameDataFinder), "FindLithography", new[] { typeof(string) }, null);
            Call(book, typeof(GameFactory), "MaintainanceCostPerLine", Type.EmptyTypes, production);
            Call(book, company, "CurrentNodeYield", new[] { node }, production);
            Call(book, typeof(GameContracts), "GetFoundryServicesContract", new[] { company, company }, production);
            Call(book, typeof(GameBusinessContract), "ContractedProductionCapacity", Type.EmptyTypes, production);
        }

        private static void Call(GpuSelfCheckBook book, Type type, string name, Type[] parameters, GpuCapability? serves)
        {
            MethodInfo method = type.GetMethod(name, AnyMember, null, parameters, null);
            book.Call(type.Name + "." + name, method != null, serves);
        }

        // ---- ports and ids ----

        private static void CheckPorts(GpuSelfCheckBook book)
        {
            if (!GameCompanyHelper.IsEMD(1) || !GameCompanyHelper.IsInlet(0))
                book.Problem("CompanyHelper.IsEMD(1) / IsInlet(0) are not true: the vendors bound to ai:1 and ai:0 may sit on the wrong companies");

            ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            List<GameProcessNode> nodes = player != null ? player.ProcessNodes : null;
            GameProcessNode node = nodes != null && nodes.Count > 0 ? nodes[0] : null;
            if (player == null || node == null)
            {
                book.PortSkipped("F1/F10/F11", "the player's company or its process node is not there");
                return;
            }

            var game = new List<double>();
            var port = new List<double>();
            float increase = player.ResearchModifiers != null ? player.ResearchModifiers.DensityIncrease : 1f;
            foreach (GpuSelfCheckSamples.Sample s in GpuSelfCheckSamples.Sets)
            {
                game.Add(GameCpuMath.TransistorCount(player, node, s.Die));
                port.Add(GpuDesignMath.TransistorsPort(node.TransistorDensity, increase, s.Die));
            }
            book.Port("F1 CpuMath.TransistorCount", game, port);

            game.Clear();
            port.Clear();
            foreach (GpuSelfCheckSamples.Sample s in GpuSelfCheckSamples.Sets)
            {
                game.Add(GameProductionMath.DiesPerWafer(s.Die, s.Wafer, s.Yield));
                port.Add(GpuDesignMath.DiesPerWaferPort(s.Die, s.Wafer, s.Yield));
            }
            book.Port("F10 ProductionMath.DiesPerWafer", game, port);

            game.Clear();
            port.Clear();
            GameFactory factory = player.Factory;
            if (factory == null)
            {
                book.PortSkipped("F11 CpuMath.RealUnitCost", "the player's company has no factory");
                return;
            }
            float wafer = player.CurrentWaferSize;
            float maintenance = factory.MaintainanceCostPerLine();
            float material = GameProductionMath.WaferMaterialCost(1f);
            foreach (GpuSelfCheckSamples.Sample s in GpuSelfCheckSamples.Sets)
            {
                int dpwYield = GameProductionMath.DiesPerWafer(s.Die, wafer, s.Yield);
                int dpwFull = GameProductionMath.DiesPerWafer(s.Die, wafer, 1f);
                game.Add(GameCpuMath.RealUnitCost(player, s.Die, s.Material, dpwYield, s.FailureRate, false, 1f, 0f));
                port.Add(GpuDesignMath.RealUnitCostPort(maintenance, dpwFull, s.Material, dpwYield, s.FailureRate, 1f, 0f, material));
            }
            book.Port("F11 CpuMath.RealUnitCost", game, port);
        }
    }
}
