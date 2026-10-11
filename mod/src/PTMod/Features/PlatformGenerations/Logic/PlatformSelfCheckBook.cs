using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>What kind of member of a game class the feature stands on.</summary>
    public enum GameMemberKind
    {
        Property,
        Field,
        Method,
        Event,
    }

    /// <summary>
    /// One thing of the game that the feature reads, calls or patches (design.md, "Самопроверка на старте"): the full name of the class, the kind of
    /// member, its name and, when it matters, the type of its value or the types of its parameters. A plain description: the game side looks it up
    /// by reflection, so a game version that moved or renamed it makes a line in the log, never an exception on the way.
    /// </summary>
    public sealed class GameMemberSpec
    {
        public GameMemberSpec(string typeName, GameMemberKind kind, string name, string valueType, string[] parameterTypes, string use, bool optional)
        {
            TypeName = typeName;
            Kind = kind;
            Name = name;
            ValueType = valueType;
            ParameterTypes = parameterTypes;
            Use = use;
            Optional = optional;
        }

        /// <summary>The full name of the class as reflection spells it (<c>ProcessorTycoon.Hardware.CustomSocket</c>, a nested class after a plus).</summary>
        public string TypeName { get; private set; }

        public GameMemberKind Kind { get; private set; }

        public string Name { get; private set; }

        /// <summary>
        /// The beginning of the full name of the type the member must have (the property, the field, the event's handler type or the method's
        /// result): <c>System.Int32</c>; null when only its existence matters.
        /// </summary>
        public string ValueType { get; private set; }

        /// <summary>The full names of the parameters of a method, to tell overloads apart; null = any parameters (the one method of the name).</summary>
        public string[] ParameterTypes { get; private set; }

        /// <summary>What the feature does with it, in a few English words, for the line of the log.</summary>
        public string Use { get; private set; }

        /// <summary>A missing optional member is a note, not a problem (the feature has a fallback for it).</summary>
        public bool Optional { get; private set; }

        /// <summary>The class without its namespace; a nested class after its outer one (<c>CpuBase.MarketData</c>).</summary>
        public string ShortType
        {
            get
            {
                int dot = TypeName.LastIndexOf('.');
                return (dot < 0 ? TypeName : TypeName.Substring(dot + 1)).Replace('+', '.');
            }
        }

        /// <summary><c>CustomSocket.CustomHardwareID</c>.</summary>
        public string Display
        {
            get { return ShortType + "." + Name; }
        }
    }

    /// <summary>
    /// What the self-check of the feature looks for (TSK-020; design.md, "Самопроверка на старте"): the members of the game that the adapter, the
    /// content loader, the release warning and the contract subscription use, and the names of the hooks of the core the feature depends on. The
    /// data only; <c>PlatformSelfCheck</c> (the game side) does the looking. Held here, away from the game, so the list can be checked against the
    /// acceptance of the task and against the code that uses the members.
    /// </summary>
    public static class PlatformSelfCheckTable
    {
        private const string Hardware = "ProcessorTycoon.Hardware.";
        private const string Int32 = "System.Int32";
        private const string ListOfInt = "System.Collections.Generic.List`1[[System.Int32";

        /// <summary>The target of the one patch of the feature, as <c>ModHooks.Report</c> names it.</summary>
        public const string ReleaseWindowTarget = "ProjectReleaseWindow.Open";

        /// <summary>The classes whose <c>AddCpu</c> the core reserves (P20) and delivers as <c>ModEvents.CpuReleased</c>; the feature must not patch them.</summary>
        public static readonly string[] ReservedAddCpu =
        {
            "ProcessorTycoon.CompanySystem.Company",
            "ProcessorTycoon.CompanySystem.AICompany",
        };

        /// <summary>The channels of the core that the two providers write to.</summary>
        public static readonly string[] Channels = { "market.rating.cpu", "contract.signChance" };

        /// <summary>The members of the game, in the order of the log.</summary>
        public static readonly IReadOnlyList<GameMemberSpec> Members = new List<GameMemberSpec>
        {
            // The socket of a CPU (FR-4 item 4, NFR-2): a standard package, or a customized one with its base.
            Property(Hardware + "CustomSocket", "CustomHardwareID", Int32, "the id of a custom socket"),
            Property(Hardware + "CustomSocket", "BasePackage", Hardware + "Package", "the base package of a custom socket (throws when its CustomHardware is gone)"),
            Property(Hardware + "IPackage", "IsCustomized", "System.Boolean", "standard or custom socket"),
            Property(Hardware + "IPackage", "Name", "System.String", "the name of a standard socket"),
            Property(Hardware + "Cpu", "Package", Hardware + "IPackage", "the socket of a CPU"),

            // The memory of a CPU, the base design, the owner, the year, the ledger.
            Property(Hardware + "Cpu", "Memory", Hardware + "Memory", "the memory standard of a CPU (the full platform)"),
            Property(Hardware + "Memory", "Name", "System.String", "the name of the memory standard"),
            Property(Hardware + "Cpu", "Base", Hardware + "Cpu", "the base design of a variant"),
            Property(Hardware + "Cpu", "Company", "ProcessorTycoon.CompanySystem.ICompany", "the owner of a CPU"),
            Property(Hardware + "CpuBase", "Year", Int32, "the year of a CPU (the reconstruction)"),
            Property(Hardware + "CpuBase", "SaveID", Int32, "the identity of a CPU"),
            Property(Hardware + "CpuBase", "Market", Hardware + "CpuBase+MarketData", "the market data of a CPU (the ledger)"),
            Property(Hardware + "CpuBase+MarketData", "Sold", ListOfInt, "units sold per month"),
            Property(Hardware + "CpuBase+MarketData", "SoldByContracts", ListOfInt, "units sold through contracts per month"),
            Property(Hardware + "CpuBase", "Name", "System.String", "the name of a CPU of the player (the window)"),
            Property(Hardware + "CpuBase", "IsReleased", "System.Boolean", "a CPU of the player is on the market"),
            Property(Hardware + "CpuBase", "IsRetired", "System.Boolean", "a CPU of the player is off the market"),
            Method(Hardware + "CpuDataProvider", "GetAllCpus", null, "every CPU of the game"),
            Property(Hardware + "CpuDataProvider", "Instance", Hardware + "CpuDataProvider", "the provider of CPUs"),

            // The clock.
            Property("ProcessorTycoon.TimeSystem.DateController", "MonthsSinceStart", Int32, "the last full month of the ledgers"),
            Property("ProcessorTycoon.TimeSystem.DateController", "Instance", "ProcessorTycoon.TimeSystem.DateController", "the date controller"),

            // The contracts (FR-8): the event of new contracts, the lists, the fields of the key.
            Event("ProcessorTycoon.ContractSystem.ContractManager", "OnNewContracts", "the preferences of new contracts"),
            Method("ProcessorTycoon.ContractSystem.ContractManager", "GetAvailableContracts", null, "the contracts on offer"),
            Method("ProcessorTycoon.ContractSystem.ContractManager", "GetActiveContracts", null, "the contracts in progress"),
            Property("ProcessorTycoon.ContractSystem.ContractManager", "Instance", "ProcessorTycoon.ContractSystem.ContractManager", "the contract manager"),
            Property("ProcessorTycoon.ContractSystem.Contract", "Company", "System.String", "the client in the key of a contract"),
            Property("ProcessorTycoon.ContractSystem.Contract", "Units", Int32, "the units in the key of a contract"),
            Property("ProcessorTycoon.ContractSystem.Contract", "PaymentPerUnit", "System.Single", "the payment in the key of a contract"),
            Property("ProcessorTycoon.ContractSystem.Contract", "AcceptanceDate", null, "the review month of a contract"),
            Property("ProcessorTycoon.ContractSystem.Contract", "ContractType", null, "the type of a contract (the calculator is left out)"),

            // The patch of the release window (FR-7) and the loader of the content.
            Method("ProcessorTycoon.ProjectSystem.ProjectReleaseWindow", "Open", new[] { Hardware + "Cpu" }, "the release window (the one patch)"),
            Method("ProcessorTycoon.DataFinder", "FindPackage", new[] { "System.String" }, "the sockets of the content are checked against the game's"),
            Method("ProcessorTycoon.DataFinder", "FindMemory", new[] { "System.String" }, "the memories of the content are checked against the game's"),

            // Names for the window and the letters: the feature has fallbacks (the key of the company, the base package of a socket).
            Property("ProcessorTycoon.CompanySystem.ICompany", "Name", "System.String", "the name of a company", true),
            Method(Hardware + "CustomHardwareHelper", "GetCustomHardware", new[] { Int32 }, "the name of a custom socket", true),
            Property(Hardware + "CustomHardwareHelper", "Instance", Hardware + "CustomHardwareHelper", "the helper of custom hardware", true),
        };

        private static GameMemberSpec Property(string type, string name, string valueType, string use, bool optional = false)
        {
            return new GameMemberSpec(type, GameMemberKind.Property, name, valueType, null, use, optional);
        }

        private static GameMemberSpec Method(string type, string name, string[] parameters, string use, bool optional = false)
        {
            return new GameMemberSpec(type, GameMemberKind.Method, name, null, parameters, use, optional);
        }

        private static GameMemberSpec Event(string type, string name, string use)
        {
            return new GameMemberSpec(type, GameMemberKind.Event, name, null, null, use, false);
        }

        /// <summary>The rows of the members the feature cannot do without.</summary>
        public static int RequiredCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Members.Count; i++)
                {
                    if (!Members[i].Optional) count++;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// The result of the self-check (TSK-020): every member of the game found or missing, every hook of the core in place or not. Turns the findings
    /// into the lines of the log - one summary line when all is well, one line per problem otherwise. A missing required thing is a problem (the part
    /// of the feature that stands on it goes without it, the feature itself runs on); a missing optional one is a note. Pure: the game side fills it.
    /// </summary>
    public sealed class PlatformSelfCheckBook
    {
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <summary>The members the table lists (required and optional).</summary>
        public int MembersExpected { get; private set; }

        /// <summary>The members that were found as described.</summary>
        public int MembersFound { get; private set; }

        /// <summary>The hooks of the core that were looked at.</summary>
        public int HooksExpected { get; private set; }

        /// <summary>The hooks that are in place.</summary>
        public int HooksOk { get; private set; }

        /// <summary>English lines of what is wrong, for the log; empty when everything required was found.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>English lines of what is missing but has a fallback.</summary>
        public IReadOnlyList<string> Notes
        {
            get { return _notes; }
        }

        /// <summary>Nothing required is missing.</summary>
        public bool Ok
        {
            get { return _problems.Count == 0; }
        }

        /// <summary>Books a member: <paramref name="problem"/> null means found as described, otherwise what is wrong with it.</summary>
        public void Member(GameMemberSpec spec, string problem)
        {
            if (spec == null) return;

            MembersExpected++;
            if (problem == null)
            {
                MembersFound++;
                return;
            }

            string line = spec.Display + " (" + spec.Use + "): " + problem;
            if (spec.Optional) _notes.Add(line + "; the feature has a fallback");
            else _problems.Add(line);
        }

        /// <summary>Books a hook of the core: <paramref name="problem"/> null means it is in place. An optional hook is a note when it is not.</summary>
        public void Hook(string name, string problem, bool optional = false)
        {
            HooksExpected++;
            if (problem == null)
            {
                HooksOk++;
                return;
            }

            string line = name + ": " + problem;
            if (optional) _notes.Add(line);
            else _problems.Add(line);
        }

        /// <summary>
        /// The line for the log: <c>self-check OK (members 36/36, hooks 8/8)</c>, or the count of problems and the counts. The problems follow as
        /// their own lines (<see cref="Problems"/>).
        /// </summary>
        public string Summary()
        {
            string counts = "members " + MembersFound.ToString(CultureInfo.InvariantCulture) + "/" + MembersExpected.ToString(CultureInfo.InvariantCulture)
                            + ", hooks " + HooksOk.ToString(CultureInfo.InvariantCulture) + "/" + HooksExpected.ToString(CultureInfo.InvariantCulture);
            if (_problems.Count == 0)
            {
                return "self-check OK (" + counts + (_notes.Count == 0 ? string.Empty : ", " + _notes.Count.ToString(CultureInfo.InvariantCulture) + " note(s)") + ")";
            }
            return "self-check found " + _problems.Count.ToString(CultureInfo.InvariantCulture) + " problem(s) (" + counts + ")";
        }

        /// <summary>The summary and every problem and note, one per line, for tests and for a single message.</summary>
        public string Report()
        {
            var text = new StringBuilder(Summary());
            foreach (string line in _problems) text.Append("\n  problem: ").Append(line);
            foreach (string line in _notes) text.Append("\n  note: ").Append(line);
            return text.ToString();
        }
    }
}
