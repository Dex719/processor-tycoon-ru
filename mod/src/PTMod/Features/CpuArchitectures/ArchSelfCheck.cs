using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using PTMod.Core;
using AIBehaviourResearch = ProcessorTycoon.AISystem.AIBehaviourResearch;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using CpuBase = ProcessorTycoon.Hardware.CpuBase;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using CpuDesign = ProcessorTycoon.Hardware.CpuDesign;
using CpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using CpuUI = ProcessorTycoon.Desktop.Inspector.CpuUI;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using CurrentResearchUI = ProcessorTycoon.ResearchSystem.CurrentResearchUI;
using DataConverter = ProcessorTycoon.Save.DataConverter;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameResearchMath = ProcessorTycoon.ResearchSystem.Math.ResearchMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using InfoText = ProcessorTycoon.UI.InfoText;
using LicenseHelper = ProcessorTycoon.LicenseHelper;
using IPackage = ProcessorTycoon.Hardware.IPackage;
using ModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using Project = ProcessorTycoon.ProjectSystem.Project;
using ProjectCompletedHandler = ProcessorTycoon.ProjectSystem.ProjectCompletedHandler;
using ResearchDataProvider = ProcessorTycoon.ResearchSystem.ResearchDataProvider;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using ResearchTreeWindow = ProcessorTycoon.ResearchSystem.ResearchTreeWindow;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;
using SaveObject = ProcessorTycoon.Save.SaveObject;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The self-check of the feature (design.md, "Самопроверка на старте"; NFR-2): looks for every method the feature patches or calls and every
    /// private field it reads, writes one line per part to the log and returns the parts that have all they stand on (<see cref="ArchParts"/>).
    /// A part with a missing member is off, the others go on. It only LOOKS: no patch is applied here (the patches arrive with the tasks of the
    /// parts), so the first run shows what the patches will find. <c>ModHooks</c> repeats the check of the targets when it applies the patches and
    /// rejects a reserved one; a reserved target is reported here too.
    /// <para>
    /// The core's self-check has no slot for the members of a single feature, so this class writes its own lines under the id of the feature.
    /// Members are found by name with reflection, never by the compile-time shape, so a game version without a member costs one part, not the start.
    /// </para>
    /// </summary>
    internal static class ArchSelfCheck
    {
        private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private sealed class Row
        {
            public ArchParts Part;
            public Type Owner;
            public string Name;
            public bool IsField;
            public Type[] Args;        // methods: the exact parameter types
            public Type FieldType;     // fields: the type the code expects (null: any)
            public PatchKind[] Kinds;  // the kinds of patch the feature will put on the method (null: it is only called): the core must not reserve them
            public bool Optional;      // a missing optional member is reported but does not switch the part off
        }

        /// <summary>
        /// Checks everything, logs one line per part and a summary, returns the parts that work. Never throws. In the debug build a member can be hidden by its flag
        /// (<see cref="PartCheck.HideFlag"/>): the check then finds it missing and only the part that stands on it goes off (NFR-2).
        /// </summary>
        public static ArchParts Run()
        {
            string feature = ArchitecturesFeature.FeatureId;
            var check = new PartCheck();

            try
            {
                foreach (Row row in Rows())
                {
                    check.Add(row.Part.ToString(), Display(row), Check(row), row.Optional);
                }

                CheckChannels(check);
            }
            catch (Exception ex)
            {
                ModLog.Error(feature, "self-check: failed unexpectedly; the parts that depend on the game are off", ex);
                return ArchParts.None;
            }

            ArchParts working = ArchParts.None;
            var off = new List<string>();
            foreach (ArchParts part in new[] { ArchParts.Research, ArchParts.Designer, ArchParts.Binder, ArchParts.Market, ArchParts.Inspector, ArchParts.Ai, ArchParts.Ecosystem, ArchParts.Royalty, ArchParts.Extensions, ArchParts.LabUi, ArchParts.Support })
            {
                string name = part.ToString();
                string line = check.Line(name);
                bool ok = !check.IsOff(name);
                if (ok) working |= part;
                else off.Add(name);

                string text = "self-check " + part + ": " + (ok ? "ok" : "OFF") + " [" + (line.Length == 0 ? "nothing to check" : line) + "]";
                if (ok) ModLog.Info(feature, text);
                else ModLog.Warn(feature, text);
            }

            // The panel, the tab and the window of the hub stand on the templates of ModUI, which the UI tasks check themselves; on until they say otherwise.
            working |= ArchParts.Ui;

            ModLog.Info(feature, "self-check: " + check.Members + " members, parts " + working + (off.Count == 0 ? "" : "; off: " + string.Join(", ", off.ToArray())));
            return working;
        }

        // ---- the table ----

        private static IEnumerable<Row> Rows()
        {
            // The shared lab: the public API the research calls (D2). Nothing is patched there.
            yield return Method(ArchParts.Research, typeof(ResearchSector), "StopResearching");
            yield return Method(ArchParts.Research, typeof(ResearchSector), "SetCurrentResearch", typeof(Technology));
            yield return Method(ArchParts.Research, typeof(ResearchSector), "CurrentResearch");
            yield return Method(ArchParts.Research, typeof(ResearchSector), "TimeLeftOf", typeof(Technology));
            yield return Method(ArchParts.Research, typeof(ResearchSector), "IsUnlocked", typeof(Technology));
            yield return Method(ArchParts.Research, typeof(ResearchSector), "IsLocked", typeof(Technology));
            yield return Method(ArchParts.Research, typeof(GameResearchMath), "ResearchSpeed", typeof(float), typeof(ICompany));
            yield return Method(ArchParts.Research, typeof(GameModifiers), "GetModifier", typeof(ModifierType), typeof(ICompany));
            yield return Method(ArchParts.Research, typeof(ResearchDataProvider), "GetTechnology", typeof(int));
            yield return Method(ArchParts.Research, typeof(ResearchDataProvider), "GetAllTechnologies");
            // The seed of the player (U is the latest year of a technology he has) and the slider of the lab block.
            yield return Method(ArchParts.Research, typeof(Technology), "get_Year");
            yield return Method(ArchParts.Research, typeof(ResearchSector), "get_Funding");
            yield return Method(ArchParts.Research, typeof(ResearchSector), "set_Funding", typeof(float));
            yield return Method(ArchParts.Research, typeof(ResearchSector), "get_InnovationEffort");

            // The designer of the player and the design of the AI (FR-4).
            yield return Patched(ArchParts.Designer, typeof(CreatorCpu), "Initialize", Postfix);
            yield return Patched(ArchParts.Designer, typeof(CreatorCpu), "UpdateUI", Postfix, typeof(bool));
            yield return Patched(ArchParts.Designer, typeof(CreatorCpu), "ConsumptionAndTemperature", Postfix);
            yield return Patched(ArchParts.Designer, typeof(CreatorCpu), "Performance", Postfix);
            yield return Patched(ArchParts.Designer, typeof(CreatorCpu), "CreateCpu", PrefixAndPostfix);
            yield return Patched(ArchParts.Designer, typeof(DataConverter), "CpuDesignToCpu", Postfix, typeof(CpuDesign));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "ipc", typeof(float));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "ips", typeof(float));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "ipsWithoutBottleneck", typeof(float));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "consumption", typeof(float));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "temperature", typeof(float));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "currentPackage", typeof(IPackage));
            yield return Field(ArchParts.Designer, typeof(CreatorCpu), "coreCountDropdown", typeof(DropdownUI));
            // The game's own temperature of a power (the designer and the CPUs of the AI), called after the power of the family is put on.
            yield return Method(ArchParts.Designer, typeof(CpuMath), "Temperature", typeof(float), typeof(IPackage));

            // The binding of a CPU to a family (FR-5).
            yield return Patched(ArchParts.Binder, typeof(Project), "SetProjectType", Postfix, typeof(SaveObject.CpuProject));
            yield return Patched(ArchParts.Binder, typeof(ProjectCompletedHandler), "AddCpu", Postfix, typeof(GameCpu));
            // The migration of an old save gives every existing CPU its family (FR-8 item 3); without it the CPUs are bound lazily, as they are asked for.
            Row allCpus = Method(ArchParts.Binder, typeof(CpuDataProvider), "GetAllCpus");
            allCpus.Optional = true;
            yield return allCpus;

            // The architecture in the inspector (FR-5 item 4).
            yield return Patched(ArchParts.Inspector, typeof(CpuUI), "SetCpu", Postfix, typeof(GameCpu));
            yield return Field(ArchParts.Inspector, typeof(CpuUI), "architectureText", typeof(InfoText));

            // The AI does not pick a technology while an architecture holds the lab (D2).
            yield return Patched(ArchParts.Ai, typeof(AIBehaviourResearch), "ChooseANewResearch", Prefix);
            yield return Field(ArchParts.Ai, typeof(AIBehaviourResearch), "company", typeof(AICompany));

            // Interrupting the technology of the player: the panel of the current research (D2).
            yield return Field(ArchParts.LabUi, typeof(CurrentResearchUI), "targetTechnology", typeof(Technology));
            yield return Method(ArchParts.LabUi, typeof(CurrentResearchUI), "Hide");
            // The lab goes back to a technology: the panel shows it again (the buttons of the tree call the same public method).
            yield return Method(ArchParts.LabUi, typeof(CurrentResearchUI), "SetTechnology", typeof(Technology));
            Row loadFunding = Method(ArchParts.LabUi, typeof(CurrentResearchUI), "LoadFunding");
            loadFunding.Optional = true;
            yield return loadFunding;
            Row forceUpdate = Method(ArchParts.LabUi, typeof(ResearchTreeWindow), "ForceUIUpdate");
            forceUpdate.Optional = true;
            yield return forceUpdate;

            // The monthly upkeep of the software support (FR-10): the company's money, the default of the player and the inflation of the game.
            yield return Method(ArchParts.Support, typeof(ICompany), "SpendMoney", typeof(float), typeof(ExpenseOrigin));
            yield return Method(ArchParts.Support, typeof(ICompany), "get_IsInDefault");
            yield return Method(ArchParts.Support, typeof(ICompany), "get_MoneyAmount");
            yield return Method(ArchParts.Support, typeof(GameInflation), "get_Instance");
            yield return Method(ArchParts.Support, typeof(GameInflation), "get_Inflation");

            // The ecosystem (FR-11) and the shares of the segments of the AI (FR-14): the monthly series of every CPU, summed over the last full months.
            yield return Method(ArchParts.Ecosystem, typeof(CpuDataProvider), "get_Instance");
            yield return Method(ArchParts.Ecosystem, typeof(CpuDataProvider), "GetAllCpus", typeof(List<GameCpu>));
            yield return Method(ArchParts.Ecosystem, typeof(CpuBase), "get_Market");
            foreach (string series in new[] { "Sold", "SoldByContracts", "SoldByHighEnd", "SoldByMidRange", "SoldByLowEnd", "SoldByIndustries", "Income" })
            {
                yield return Method(ArchParts.Ecosystem, typeof(CpuBase.MarketData), "get_" + series);
            }
            yield return Method(ArchParts.Ecosystem, typeof(GameCpu), "get_Company");

            // The royalty for x86 without a licence (FR-16): the money of the payer and of the holders, the flag of the game, the income of a CPU.
            yield return Method(ArchParts.Royalty, typeof(ICompany), "SpendMoney", typeof(float), typeof(ExpenseOrigin));
            yield return Method(ArchParts.Royalty, typeof(ICompany), "EarnMoney", typeof(float), typeof(RevenueOrigin));
            yield return Method(ArchParts.Royalty, typeof(LicenseHelper), "get_Instance");
            yield return Method(ArchParts.Royalty, typeof(LicenseHelper), "get_X86LicenseHasBeenRevoked");
            yield return Method(ArchParts.Royalty, typeof(CpuBase.MarketData), "get_Income");
            yield return Method(ArchParts.Royalty, typeof(GameCpu), "get_Company");

            // The warning of the extension gates in the designer (FR-13 item 2): the cores and SMT of the design that is being made.
            yield return Patched(ArchParts.Extensions, typeof(CreatorCpu), "UpdateNumbers", Postfix);
            yield return Field(ArchParts.Extensions, typeof(CreatorCpu), "currentCoreCount", typeof(int));
            yield return Field(ArchParts.Extensions, typeof(CreatorCpu), "smtEnabled", typeof(bool));
        }

        private static Row Method(ArchParts part, Type owner, string name, params Type[] args)
        {
            return new Row { Part = part, Owner = owner, Name = name, Args = args };
        }

        private static readonly PatchKind[] Prefix = { PatchKind.Prefix };
        private static readonly PatchKind[] Postfix = { PatchKind.Postfix };
        private static readonly PatchKind[] PrefixAndPostfix = { PatchKind.Prefix, PatchKind.Postfix };

        private static Row Patched(ArchParts part, Type owner, string name, PatchKind[] kinds, params Type[] args)
        {
            return new Row { Part = part, Owner = owner, Name = name, Args = args, Kinds = kinds };
        }

        private static Row Field(ArchParts part, Type owner, string name, Type fieldType)
        {
            return new Row { Part = part, Owner = owner, Name = name, IsField = true, FieldType = fieldType };
        }

        // ---- the checks ----

        /// <summary>"ok", or why the member is not usable: "missing", "has type X", "reserved by ...", "error: ...", "hidden by the debug flag".</summary>
        private static string Check(Row row)
        {
            try
            {
                // The debug build can hide a member on purpose to show the feature degrade (TSK-030); a release build never reads the flag.
                if (BuildInfo.IsDebug && ModSettings.Debug(PartCheck.HideFlag(row.Owner.Name, row.Name))) return PartCheck.HiddenState;

                if (row.IsField)
                {
                    FieldInfo field = row.Owner.GetField(row.Name, AnyMember);
                    if (field == null) return "missing";
                    if (row.FieldType != null && !row.FieldType.IsAssignableFrom(field.FieldType)) return "has type " + field.FieldType.Name;
                    return "ok";
                }

                MethodInfo method = row.Owner.GetMethod(row.Name, AnyMember, null, row.Args, null);
                if (method == null) return "missing";

                ReservedTarget reserved;
                if (row.Kinds != null && ModHooks.IsReserved(method, out reserved))
                {
                    foreach (PatchKind kind in row.Kinds)
                    {
                        if (reserved.Rejects(ArchitecturesFeature.FeatureId, kind)) return "reserved: " + reserved.Describe();
                    }
                }
                return "ok";
            }
            catch (Exception ex)
            {
                return "error: " + ex.GetType().Name;
            }
        }

        /// <summary>The channels of the modifiers the feature writes to must be declared by the core (the Market part).</summary>
        private static void CheckChannels(PartCheck check)
        {
            foreach (string channel in new[] { ModChannels.MarketRatingCpu, ModChannels.DevelopmentCostDesign })
            {
                bool declared = ModModifiers.IsDeclared(channel);
                check.Add(ArchParts.Market.ToString(), "channel " + channel, declared ? PartCheck.Ok : "not declared", false);
            }
        }

        private static string Display(Row row)
        {
            if (row.IsField) return row.Owner.Name + "." + row.Name;

            var text = new StringBuilder(row.Owner.Name).Append('.').Append(row.Name).Append('(');
            for (int i = 0; i < row.Args.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(row.Args[i].Name);
            }
            return text.Append(')').ToString();
        }
    }
}
