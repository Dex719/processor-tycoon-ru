using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>What kind of member of a game class a <see cref="RecoveryTarget"/> is.</summary>
    internal enum TargetKind
    {
        Method,
        Field,
        Property,
    }

    /// <summary>
    /// A member of the game that the feature depends on, named by the full name of its class (design.md, "Самопроверка на старте"): a method it patches or
    /// calls, a private field it reads. The classes are named by text on purpose: a class that was renamed or removed in a game update is a missing
    /// target in the report, not a crash of the check.
    /// </summary>
    internal sealed class RecoveryTarget
    {
        public RecoveryTarget(string typeName, string member, TargetKind kind, string[] parameterTypes, string valueType, string role, bool optional = false)
        {
            TypeName = typeName;
            Member = member;
            Kind = kind;
            ParameterTypes = parameterTypes;
            ValueType = valueType;
            Role = role;
            IsOptional = optional;
        }

        /// <summary>The full name of the class, <c>Namespace.Class</c>.</summary>
        public string TypeName { get; private set; }

        /// <summary>The name of the method, field or property.</summary>
        public string Member { get; private set; }

        public TargetKind Kind { get; private set; }

        /// <summary>The full names of the parameter types of a method (empty for none); null for a field or property.</summary>
        public string[] ParameterTypes { get; private set; }

        /// <summary>The full name of the type of a field or property when it matters; null when the existence is enough.</summary>
        public string ValueType { get; private set; }

        /// <summary>Why the feature needs it: <c>P1</c>..<c>P4</c> for a patch, <c>reflection</c> for a call by name, <c>call</c> for a plain call.</summary>
        public string Role { get; private set; }

        /// <summary>The feature works without it (a patch declared optional): a miss is a warning, not a reason to switch the feature off.</summary>
        public bool IsOptional { get; private set; }

        /// <summary>The short text for the log: <c>BankruptcyHandler.CancelDefault(ICompany)</c>.</summary>
        public string Label
        {
            get
            {
                var text = new StringBuilder(ShortName(TypeName)).Append('.').Append(Member);
                if (Kind == TargetKind.Method)
                {
                    text.Append('(');
                    string[] parameters = ParameterTypes ?? new string[0];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (i > 0) text.Append(", ");
                        text.Append(ShortName(parameters[i]));
                    }
                    text.Append(')');
                }
                return text.ToString();
            }
        }

        private static string ShortName(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            return dot < 0 ? fullName : fullName.Substring(dot + 1);
        }
    }

    /// <summary>The members of the game the feature depends on (the list of the design's self-check plus what the services call).</summary>
    internal static class RecoveryTargets
    {
        private const string Handler = "ProcessorTycoon.Bank.BankruptcyHandler";
        private const string Company = "ProcessorTycoon.CompanySystem.ICompany";
        private const string Owner = "ProcessorTycoon.CompanySystem.ICompanyOwner";
        private const string Cpu = "ProcessorTycoon.Hardware.Cpu";
        private const string Wafer = "ProcessorTycoon.Hardware.WaferSize";
        private const string Int = "System.Int32";
        private const string Float = "System.Single";
        private const string Storage = "ProcessorTycoon.Production.Math.StorageMath";
        private const string Production = "ProcessorTycoon.Production.Math.ProductionMath";
        private const string Contracts = "ProcessorTycoon.ContractSystem.ContractHelper";
        private const string PlayerCompany = "ProcessorTycoon.CompanySystem.Company";

        private static readonly IReadOnlyList<RecoveryTarget> AllTargets = new RecoveryTarget[]
        {
            // the patches P1-P4
            new RecoveryTarget(Handler, "TickBankruptcies", TargetKind.Method, new string[0], null, "P1, P2"),
            new RecoveryTarget(Handler, "TriggerDefault", TargetKind.Method, new[] { Company }, null, "P3"),
            new RecoveryTarget("ProcessorTycoon.Bank.BankruptcyWarningWindow", "Open", TargetKind.Method, new string[0], null, "P4", true),

            // the private members called by name
            new RecoveryTarget(Handler, "CancelDefault", TargetKind.Method, new[] { Company }, null, "reflection"),
            new RecoveryTarget(Handler, "defaultTimeLimit", TargetKind.Field, null, Int, "reflection"),

            // the money of the player
            new RecoveryTarget(PlayerCompany, "AddMoney", TargetKind.Method, new[] { Float }, null, "call"),
            new RecoveryTarget(Company, "SpendMoney", TargetKind.Method, new[] { Float, "ProcessorTycoon.ExpenseOrigin" }, null, "call"),
            new RecoveryTarget(Company, "EarnMoney", TargetKind.Method, new[] { Float, "ProcessorTycoon.RevenueOrigin" }, null, "call"),
            new RecoveryTarget("ProcessorTycoon.Bank.Inflation.InflationHandler", "Inflation", TargetKind.Property, null, Float, "call"),
            new RecoveryTarget("ProcessorTycoon.CompanySystem.CompanyFinance", "Reports", TargetKind.Property, null, null, "call"),

            // the asset sale
            new RecoveryTarget(Production, "SaleCost", TargetKind.Method, new[] { Int, Wafer }, null, "call"),
            new RecoveryTarget(Production, "ManufacturingDivisionValue", TargetKind.Method, new[] { Company }, null, "call"),
            new RecoveryTarget(Company, "RemoveProductionLines", TargetKind.Method, new[] { Int, Wafer }, null, "call"),
            new RecoveryTarget("ProcessorTycoon.Production.Factory", "AmountOfLines", TargetKind.Method, new[] { Wafer }, null, "call"),
            new RecoveryTarget("ProcessorTycoon.Production.UI.ManageLinesWindow", "UpdateWafers", TargetKind.Method, new string[0], null, "call"),
            new RecoveryTarget(Storage, "SellStock", TargetKind.Method, new[] { Cpu }, null, "call"),
            new RecoveryTarget(Storage, "Income", TargetKind.Method, new[] { Cpu }, null, "call"),
            new RecoveryTarget(Contracts, "FinesFromContracts", TargetKind.Method, new[] { Cpu }, null, "call"),
            new RecoveryTarget(Contracts, "ContractedProductionLines", TargetKind.Method, new[] { Owner }, null, "call"),
            new RecoveryTarget(Cpu, "TriggerRetirement", TargetKind.Method, new string[0], null, "call"),
        };

        /// <summary>Every member the feature depends on. A missing one means the game changed and the feature cannot run (design.md, "Error Handling").</summary>
        public static IReadOnlyList<RecoveryTarget> All
        {
            get { return AllTargets; }
        }
    }

    /// <summary>Looks for one target in the game. The game side uses reflection; a test uses a table.</summary>
    internal interface ITargetResolver
    {
        /// <summary>Null when the target is there, otherwise one English sentence about what is missing or different.</summary>
        string Probe(RecoveryTarget target);
    }

    /// <summary>
    /// What the self-check found out about one target: the line of the log (<c>[P1, P2] BankruptcyHandler.TickBankruptcies(): found</c>) and whether the target is
    /// one of those the log always names (a patch or a member called by name).
    /// </summary>
    internal sealed class TargetEntry
    {
        public TargetEntry(RecoveryTarget target, string problem)
        {
            Target = target;
            Problem = problem;
        }

        public RecoveryTarget Target { get; private set; }

        /// <summary>Null when the target is there, otherwise the sentence about what is missing.</summary>
        public string Problem { get; private set; }

        public bool Found
        {
            get { return Problem == null; }
        }

        /// <summary>A patch (<c>P1</c> to <c>P4</c>) or a member called by name (<c>reflection</c>): the targets the design's self-check names in the log.</summary>
        public bool IsPatchOrReflection
        {
            get
            {
                string role = Target.Role;
                return role == "reflection" || (role.Length >= 2 && role[0] == 'P' && char.IsDigit(role[1]));
            }
        }

        /// <summary>One line for the log: the role, the target and what was found.</summary>
        public string Line
        {
            get { return "[" + Target.Role + "] " + Target.Label + (Found ? ": found" : ": missing (" + Problem + ")"); }
        }
    }

    /// <summary>What a run of the self-check found.</summary>
    internal sealed class TargetReport
    {
        private readonly List<string> _missing = new List<string>();
        private readonly List<string> _optionalMissing = new List<string>();
        private readonly List<TargetEntry> _entries = new List<TargetEntry>();

        public TargetReport(int total)
        {
            Total = total;
        }

        /// <summary>How many targets were looked for.</summary>
        public int Total { get; private set; }

        /// <summary>One line per missing target the feature cannot do without: <c>BankruptcyHandler.CancelDefault(ICompany): no such method</c>.</summary>
        public IReadOnlyList<string> Missing
        {
            get { return _missing; }
        }

        /// <summary>One line per missing target the feature can do without (an optional patch).</summary>
        public IReadOnlyList<string> OptionalMissing
        {
            get { return _optionalMissing; }
        }

        public int Found
        {
            get { return Total - _missing.Count - _optionalMissing.Count; }
        }

        /// <summary>Nothing the feature needs is missing (an optional target may be).</summary>
        public bool AllFound
        {
            get { return _missing.Count == 0; }
        }

        /// <summary>Every target that was looked for, in the order of the list, found or not (for the log that names them all).</summary>
        public IReadOnlyList<TargetEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>
        /// Why the feature must not run, as the sentence of the exception that tells the core so (<c>Register</c> throws it and the core marks the feature
        /// unavailable); null when nothing the feature needs is missing.
        /// </summary>
        public string FailureText
        {
            get { return AllFound ? null : "the game does not match the feature, missing: " + string.Join("; ", _missing); }
        }

        internal void AddMissing(string line, bool optional)
        {
            if (optional) _optionalMissing.Add(line);
            else _missing.Add(line);
        }

        internal void AddEntry(RecoveryTarget target, string problem)
        {
            _entries.Add(new TargetEntry(target, problem));
        }

        /// <summary>The result line for the log: <c>self-check: 21 of 21 game members found</c>.</summary>
        public string Summary
        {
            get
            {
                return "self-check: " + Found.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of "
                       + Total.ToString(System.Globalization.CultureInfo.InvariantCulture) + " game members found";
            }
        }
    }

    /// <summary>The self-check of the game members (design.md, "Самопроверка на старте"): runs a resolver over a list of targets.</summary>
    internal static class TargetCheck
    {
        /// <summary>Probes every target. A probe that throws counts as a missing target with the exception as the reason; the check itself never throws.</summary>
        public static TargetReport Run(IEnumerable<RecoveryTarget> targets, ITargetResolver resolver)
        {
            var list = new List<RecoveryTarget>(targets ?? new RecoveryTarget[0]);
            var report = new TargetReport(list.Count);
            if (resolver == null)
            {
                foreach (RecoveryTarget target in list)
                {
                    report.AddMissing(target.Label + ": no resolver", target.IsOptional);
                    report.AddEntry(target, "no resolver");
                }
                return report;
            }

            foreach (RecoveryTarget target in list)
            {
                string problem;
                try
                {
                    problem = resolver.Probe(target);
                }
                catch (Exception e)
                {
                    problem = "the check failed: " + e.GetType().Name + ": " + e.Message;
                }
                if (problem != null) report.AddMissing(target.Label + " [" + target.Role + "]: " + problem, target.IsOptional);
                report.AddEntry(target, problem);
            }
            return report;
        }
    }
}
