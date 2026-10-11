using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    // The DTOs of the section "cpu-architectures" of the side save file (design.md, "ModSave: секция cpu-architectures"), written and read by
    // ModJson.From / ModJson.Bind. The fields are lower-case on purpose: they are the JSON keys. There is NO version field: the section version
    // (_schema) is written and read by the core (ModSave), the feature only reports SchemaVersion. The fields of stage 2 (support, aiOverrides,
    // milestones) exist from stage 1, so that a save of stage 1 opens in stage 2 without a migration. A number that stands for an id starts at -1,
    // so that a record whose id is missing or unreadable can be told from company 0 and dropped.

    /// <summary>The whole section: the companies, the CPU-to-family bindings, the projects waiting for their CPU, the stage-2 lists.</summary>
    public sealed class ArchSaveData
    {
        /// <summary>The state has been sown (new game, migration of an old save); false means "sow on GameReady".</summary>
        public bool seeded;

        public List<CompanyArchSave> companies = new List<CompanyArchSave>();
        public List<CpuArchSave> cpuArch = new List<CpuArchSave>();
        public List<PendingSave> pending = new List<PendingSave>();

        /// <summary><c>ArchitectureApi.SetAiWeightOverride</c> (rival-personalities).</summary>
        public List<AiOverrideSave> aiOverrides = new List<AiOverrideSave>();

        /// <summary>The ecosystem milestones already announced (FR-15 item 3).</summary>
        public List<MilestoneSave> milestones = new List<MilestoneSave>();
    }

    public sealed class CompanyArchSave
    {
        /// <summary><c>ICompany.SaveID</c>.</summary>
        public int companyId = -1;

        public bool isPlayer;
        public string active;
        public string current;
        public bool paused;

        /// <summary>none, technology, default, funds or disabled.</summary>
        public string pauseReason = "none";

        public int resumeTech = -1;
        public List<FamilySave> families = new List<FamilySave>();
    }

    public sealed class FamilySave
    {
        public string id;
        public bool researched;
        public float progress;
        public int researchedYear;
        public int researchedMonth;
        public float support;

        /// <summary>off, low, normal or high.</summary>
        public string tier = "off";
    }

    /// <summary>A CPU bound to a family: <c>Cpu.SaveID</c> and the family id.</summary>
    public sealed class CpuArchSave
    {
        public int cpu = -1;
        public string arch;
    }

    /// <summary>A CPU project that was started on a family and has no CPU yet (the "fingerprint" of FR-5 item 1): company, name, year, IPS, family.</summary>
    public sealed class PendingSave
    {
        public int company = -1;
        public string name;
        public int year;
        public float ips;
        public string arch;
    }

    public sealed class AiOverrideSave
    {
        public int company = -1;
        public string arch;
        public float weight;
    }

    public sealed class MilestoneSave
    {
        public string arch;
        public int threshold;
    }
}
