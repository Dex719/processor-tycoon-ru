using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Save
{
    // Data shapes of the side save file <name>.ptmod.json (design.md, "DTO бокового файла"). Public lowercase fields on purpose:
    // the field name is the JSON key, ModJson.Bind / ModJson.From map them without attributes. Defaults in initializers are what
    // a missing key leaves. Value rules (dates yyyy-MM-dd, mult finite and >= 0, mail id >= 100000 and unique, company key
    // ^(player|ai:\d+)$, priority Low/Normal/Critical) are checked by the package that owns the data; a violating record is
    // dropped with a warning and the rest of the section is still read.

    /// <summary>Top-level fields of the file (everything except the <c>features</c> map).</summary>
    public sealed class SideFileHeader
    {
        public int schema;            // format of the file itself (SideFileModel.FileSchema)
        public string modVersion;
        public string gameVersion;
        public string saveName;       // without extension, as the game named the save
        public string gameDate;       // yyyy-MM-dd
        public string writtenUtc;     // yyyy-MM-ddTHH:mm:ssZ
    }

    /// <summary>
    /// Section "mod-framework". Note: the section version <c>_schema</c> is read and written by <see cref="SideFileModel"/>, not by the
    /// owner of the data, so a participant that serializes this DTO with <c>ModJson.From</c> must remove the <c>_schema</c> member from the
    /// result (otherwise the model overwrites it and logs a warning on every save).
    /// </summary>
    public sealed class CoreSectionDto
    {
        public int _schema = 1;
        public string startDate;                                   // "1975-01-01"
        public RandomDto random;
        public Dictionary<string, Dictionary<string, int>> flags;  // featureId -> key -> int (bool = 0/1)
        public Dictionary<string, JsonNode> newGameOptions;
        public List<ModifierEntryDto> modifiers;
        public Dictionary<string, ReputationDto> reputation;       // companyKey -> ...
        public MailStoreDto mail;
    }

    public sealed class RandomDto
    {
        public uint worldSeed;
        public Dictionary<string, uint[]> streams;   // stream id -> 4 words of xoshiro128**
    }

    public sealed class ModifierEntryDto
    {
        public string id, feature, channel, scope, company, reason, expires;
        public string[] reasonArgs;
        public int cpu = -1;
        public int segment = -1;
        public float mult;
    }

    public sealed class ReputationDto
    {
        public float value;
        public List<ReputationChangeDto> history;   // drift is not written to the history
    }

    public sealed class ReputationChangeDto
    {
        public string date;
        public float delta, value;
        public string reason;
    }

    public sealed class MailStoreDto
    {
        public int nextId;
        public List<MailRecordDto> mails;
        public List<MailRecordDto> queue;   // not delivered yet
    }

    public sealed class MailRecordDto
    {
        public int id;
        public string feature, tag, titleKey, bodyKey, sent, delivered, deadline, choice, resolution, defaultChoice, priority, status;
        public LocTextDto title, body;
        public string[] args;
        public bool read;
        public List<MailChoiceDto> choices;
        public List<MailActionDto> actions;
    }

    public sealed class MailChoiceDto
    {
        public string id, labelKey, tooltipKey, disabledReasonKey;
        public LocTextDto label;
        public string[] disabledReasonArgs;
        public bool enabled = true;
    }

    public sealed class MailActionDto
    {
        public string id, labelKey, tooltipKey, disabledReasonKey;
        public LocTextDto label;
        public string[] disabledReasonArgs;
        public bool enabled = true;
        public bool repeatable = true;
        public bool used;
    }

    /// <summary>
    /// A text given inline instead of by L10n key (same three parts as <c>LocText</c>: <c>key</c>, <c>ru</c>, <c>en</c>).
    /// design.md names this type in the mail DTOs but does not define it; this is the minimal shape.
    /// </summary>
    public sealed class LocTextDto
    {
        public string key;
        public string ru;
        public string en;
    }
}
