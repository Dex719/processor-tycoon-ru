using System.Collections.Generic;

namespace PTMod.Features.RivalAcquisitions
{
    // The plan of a deal (TSK-008, design.md "AcquisitionPlanBuilder"): what moves to the player, what is retired or broken, what the player has to
    // be told. A plain description built by a pure function from the snapshots; the executor only carries it out. Public fields, English comments.

    /// <summary>What the player chose in the preview (design.md, "PlanOptions").</summary>
    public sealed class PlanOptions
    {
        /// <summary>"Keep the lines" (true) or "Sell the lines" (false).</summary>
        public bool KeepLines = true;

        /// <summary>The "I understand the consequences" tick (FR-12 item 5).</summary>
        public bool ConsequencesAcknowledged;
    }

    /// <summary>Lines of one wafer size that go from the target to the player (FR-7 item 3).</summary>
    public sealed class LineMove
    {
        /// <summary>The wafer size the target has the lines on.</summary>
        public int FromWaferId;

        /// <summary>The wafer size the player gets them on: the same one, or a smaller one the player knows when the player cannot have the first.</summary>
        public int ToWaferId;

        public int Lines;
        public double FromValue;
        public double ToValue;

        /// <summary>The lines move to a different wafer size.</summary>
        public bool Remapped
        {
            get { return FromWaferId != ToWaferId; }
        }
    }

    /// <summary>Lines of one wafer size that are sold for the game's sale cost (FR-7 item 4).</summary>
    public sealed class LineSale
    {
        public int WaferId;
        public double Value;
        public int Lines;

        /// <summary><c>ProductionMath.SaleCost(Lines, wafer)</c>: what the player is paid.</summary>
        public double SaleCost;
    }

    /// <summary>What becomes of a CPU of the target in the deal.</summary>
    public enum CpuFate
    {
        /// <summary>It is on sale now and stays on sale under the player.</summary>
        Live,

        /// <summary>It was on sale when the company went bankrupt and is brought back (<c>IsRetired = false</c>) under the player.</summary>
        Revive,

        /// <summary>It is retired already and passes to the player retired (a base that a living variant needs, or a CPU that cannot be made).</summary>
        StaysRetired,

        /// <summary>It is on sale now but cannot be made after the deal: it is retired (<c>TriggerRetirement</c>) and, if its family passes, goes along retired.</summary>
        Retire,
    }

    /// <summary>One CPU of a family that passes to the player.</summary>
    public sealed class FamilyMember
    {
        public int SaveId;
        public string Name = "";
        public CpuFate Fate;

        /// <summary>The player makes this CPU today (its <c>OutsourcedID</c> is the player's): after the deal it is made in the player's own factory, <c>OutsourcedID = -1</c>.</summary>
        public bool ToInHouse;

        /// <summary>The OEM contracts that chose it (they follow it).</summary>
        public int OemContracts;
    }

    /// <summary>A base CPU and its variants: they pass together, the base first (D5), so a variant never loads without its base.</summary>
    public sealed class CpuFamily
    {
        /// <summary><c>SaveID</c> of the base CPU.</summary>
        public int BaseSaveId;

        /// <summary>The base first, then the variants by <c>SaveID</c>. The base is missing only when the snapshot lacks it (a blocking error then).</summary>
        public List<FamilyMember> Members = new List<FamilyMember>();
    }

    /// <summary>A consequence or a warning of the plan: a <see cref="AcqKeys"/> key and the arguments of its text.</summary>
    public sealed class PlanNote
    {
        public string Key = "";
        public string[] Args = new string[0];

        /// <summary>The player has to tick "I understand" before the deal (FR-12 item 5).</summary>
        public bool RequiresAck;
    }

    /// <summary>
    /// What a deal does, step by step, for the preview and the executor. Built by <see cref="AcquisitionPlanBuilder"/>. Contract ids are the
    /// <see cref="ContractSnapshot.Id"/> of the snapshot the plan was built from. The deal can go ahead only when <see cref="BlockingErrors"/> is empty.
    /// </summary>
    public sealed class AcquisitionPlan
    {
        public DealType Type;

        /// <summary>The player keeps the lines (true) or sells them (false); what the options said.</summary>
        public bool KeepLines = true;

        /// <summary>The target makes CPUs of the player: the lines must be kept (FR-7 item 5).</summary>
        public bool KeepLinesRequired;

        /// <summary>Technologies to unlock for the player, shallowest first (the order of the unlocking).</summary>
        public List<TechSnapshot> TechUnlocks = new List<TechSnapshot>();

        /// <summary>The same technologies as ids, in the same order.</summary>
        public List<int> TechUnlockIds = new List<int>();

        /// <summary>Technologies neither has finished: raise the player's progress to the target's.</summary>
        public List<TechProgressSnapshot> TechProgress = new List<TechProgressSnapshot>();

        /// <summary>Process nodes where the player's experience is raised to the target's.</summary>
        public List<NodeSnapshot> NodeRaises = new List<NodeSnapshot>();

        public List<LineMove> LineMoves = new List<LineMove>();
        public List<LineSale> LineSales = new List<LineSale>();

        /// <summary>The families of CPUs that pass to the player, bases first.</summary>
        public List<CpuFamily> TransferFamilies = new List<CpuFamily>();

        /// <summary><c>SaveID</c> of the CPUs that are on sale now and get retired in the deal (they do not live on under the player).</summary>
        public List<int> RetireCpuIds = new List<int>();

        /// <summary>Contracts of the target as the provider that pass to the player (the player becomes the provider).</summary>
        public List<int> RepointProviderContractIds = new List<int>();

        /// <summary>Contracts of the target as a client of a third party that pass to the player (the player becomes the client).</summary>
        public List<int> RepointClientContractIds = new List<int>();

        /// <summary>Contracts of the target as a client that are not taken over (exclusivity conflict, or no CPU of the target is made there any more).</summary>
        public List<int> DroppedClientContractIds = new List<int>();

        /// <summary>Contracts of the target as the provider that end with the target (the lines are sold).</summary>
        public List<int> DroppedProviderContractIds = new List<int>();

        /// <summary>Switch on the player's foundry services (<c>Divisions.FoundryServices</c>): the lines are kept and the target had them on.</summary>
        public bool EnableFoundryServices;

        /// <summary><c>SaveID</c> of the player's CPUs that the target makes: they become the player's own production.</summary>
        public List<int> PlayerCpusToInHouse = new List<int>();

        /// <summary>What the player must acknowledge (FR-12), and the information that comes with it.</summary>
        public List<PlanNote> Consequences = new List<PlanNote>();

        /// <summary>Things that do not block but the player should read.</summary>
        public List<PlanNote> Warnings = new List<PlanNote>();

        /// <summary>The rise of the monthly upkeep of the player's factory when the lines are kept (before the upkeep rate changes, which the game does).</summary>
        public double OpexDeltaPerMonth;

        /// <summary>Keys of <see cref="AcqKeys"/> (or of the quote's reason) that forbid the deal; empty means the plan can be executed.</summary>
        public List<string> BlockingErrors = new List<string>();

        /// <summary>The argument of each blocking error, in the same order (null when it has none).</summary>
        public List<string> BlockingArgs = new List<string>();

        public bool CanExecute
        {
            get { return BlockingErrors.Count == 0; }
        }

        public int LinesMoved
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < LineMoves.Count; i++) sum += LineMoves[i].Lines;
                return sum;
            }
        }

        public int LinesSold
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < LineSales.Count; i++) sum += LineSales[i].Lines;
                return sum;
            }
        }

        /// <summary>The money the sale of the lines brings.</summary>
        public double SaleIncome
        {
            get
            {
                double sum = 0.0;
                for (int i = 0; i < LineSales.Count; i++) sum += LineSales[i].SaleCost;
                return sum;
            }
        }

        /// <summary>All the CPUs that pass to the player, bases first (what <c>DealRecord.cpuSaveIds</c> keeps).</summary>
        public List<int> TransferredCpuIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < TransferFamilies.Count; i++)
            {
                for (int j = 0; j < TransferFamilies[i].Members.Count; j++) ids.Add(TransferFamilies[i].Members[j].SaveId);
            }
            return ids;
        }

        /// <summary>The CPUs that pass to the player retired (what <c>DealRecord.retiredCpuSaveIds</c> keeps): already retired or retired by the deal.</summary>
        public List<int> TransferredRetiredCpuIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < TransferFamilies.Count; i++)
            {
                for (int j = 0; j < TransferFamilies[i].Members.Count; j++)
                {
                    FamilyMember member = TransferFamilies[i].Members[j];
                    if (member.Fate == CpuFate.StaysRetired || member.Fate == CpuFate.Retire) ids.Add(member.SaveId);
                }
            }
            return ids;
        }

        public bool HasConsequencesToAcknowledge()
        {
            for (int i = 0; i < Consequences.Count; i++)
            {
                if (Consequences[i].RequiresAck) return true;
            }
            return false;
        }
    }
}
