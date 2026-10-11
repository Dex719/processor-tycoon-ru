using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    // The data of the section "historical-events" of the side save file (FR-18; design.md, "Секция ModSave"). Days are whole days from 1971-01-01 (SimpleDate.Days).
    // Companies are written by their keys (CompanyKey: "player", "ai:<UniqueID>"), CPUs by the game's SaveID. The version of the section (_schema) is written and read by the
    // core: the data of the feature has no field for it (D20, NFR-3). The fields are public and lower-case on purpose: they mirror the JSON.
    //
    // These DTOs live in Logic/Save, not in Save/, because they are pure (they are built by ModJson, no game types) and the build of the tests compiles only Logic/.

    /// <summary>The whole section.</summary>
    public sealed class HistSaveData
    {
        /// <summary>The jitter of the entries was rolled and the start of the game was processed (a new game after its fast-forward, or a save without a section).</summary>
        public bool initialized;

        public List<EntryStateDto> entries = new List<EntryStateDto>();
        public List<CaseDto> cases = new List<CaseDto>();
        public List<SettlementDto> settlements = new List<SettlementDto>();
        public List<ExportRuleDto> exportRules = new List<ExportRuleDto>();
        public List<PriceWarDto> priceWars = new List<PriceWarDto>();
        public List<DisputeDto> disputes = new List<DisputeDto>();
        public List<RaceDto> races = new List<RaceDto>();

        /// <summary>The day of the last <c>major</c> firing (0 when there was none).</summary>
        public int lastMajorDay;

        /// <summary>Ids of the entries whose lasting effects were caught up as entries of <c>ModModifiers</c>.</summary>
        public List<string> catchUpEntryIds = new List<string>();
    }

    /// <summary>The state of one entry of the calendar.</summary>
    public sealed class EntryStateDto
    {
        public string id;

        /// <summary>An <see cref="EntryStatus"/> number.</summary>
        public int status;

        /// <summary>The rolled shift of the firing, in days (-60 to 60).</summary>
        public int jitter;

        /// <summary>The day the entry fired (0 while it has not).</summary>
        public int firedDay;

        /// <summary>How many stages were played.</summary>
        public int stagesFired;

        /// <summary>The companies that were found for each role at the firing; the later stages fire for the same companies.</summary>
        public List<RoleTargetsDto> targets = new List<RoleTargetsDto>();

        /// <summary>Flags set on the entry (the <c>HistFlag</c> effect), read by <c>skipIfFlag</c>.</summary>
        public List<string> flags = new List<string>();
    }

    public sealed class RoleTargetsDto
    {
        public string role;
        public List<string> companyKeys = new List<string>();
    }

    /// <summary>An antitrust case (<c>CaseService</c>).</summary>
    public sealed class CaseDto
    {
        public string caseId;
        public string targetKey;
        public string mode;
        public int stage;
        public float fine;
        public float paid;
        public float provisioned;
        public int rulingDay;
        public int reimposeDay;
        public float guaranteeFeePerMonth;
        public int openedDay;

        /// <summary>The parameters of the case as the event gave them (the appeal months, the odds, the remedy); free-form, owned by the service.</summary>
        public JsonObject @params;
    }

    /// <summary>A suit of a victim of an antitrust case (<c>SettlementService</c>).</summary>
    public sealed class SettlementDto
    {
        public string id;
        public string plaintiffKey, defendantKey;
        public int resolveDay;
        public float legalPerMonth;

        /// <summary>The chance to win and the award as a share of the defendant's revenue, fixed when the suit was filed (the numbers the plaintiff was shown).</summary>
        public float pWin, settlementPct;
    }

    /// <summary>An export rule (<c>ExportRuleService</c>).</summary>
    public sealed class ExportRuleDto
    {
        public string ruleId;
        public float threshold0, avgIps0, threshold;
        public float percentile, blockedShare, indexing;
        public List<int> segments = new List<int>();
        public int endDay;
        public List<ExemptionDto> exemptions = new List<ExemptionDto>();
    }

    public sealed class ExemptionDto
    {
        public string companyKey;
        public string kind;
        public float factor;
        public int endDay;
    }

    /// <summary>A price war (<c>PriceWarService</c>): what to give back when it ends.</summary>
    public sealed class PriceWarDto
    {
        public string warId;
        public int endDay;
        public bool viaRivalApi;
        public List<PriceRestoreDto> restore = new List<PriceRestoreDto>();
    }

    public sealed class PriceRestoreDto
    {
        public int cpuSaveId;
        public float originalTargetPrice;
    }

    /// <summary>A patent dispute (<c>DisputeService</c>).</summary>
    public sealed class DisputeDto
    {
        public string disputeId;
        public string mode;
        public string plaintiffKey, defendantKey;
        public string choice;
        public int endDay;
        public int resolveDay;
        public float monthlyPaymentPct;
    }

    /// <summary>An attribute race (<c>RaceService</c>).</summary>
    public sealed class RaceDto
    {
        public string raceId;
        public string attribute;
        public int topN;
        public float bonus;
        public List<int> segments = new List<int>();
        public int endDay;
        public List<int> topCpuIds = new List<int>();
    }
}
