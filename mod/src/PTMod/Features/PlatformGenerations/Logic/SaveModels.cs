using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    // The section "platform-generations" of the side save file (design.md, "Секция ModSave"). Plain DTOs for ModJson.Bind / ModJson.From.
    // There is NO version field: the core writes and reads "_schema" itself (PlatformSaveCodec.SchemaVersion is what the participant reports).
    // Dates are the text yyyy-MM-dd (GameDate), a company is a ModCompanies key ("player", "ai:<UniqueID>"). The fields are public and
    // lower-case because they are the JSON member names.

    /// <summary>The whole section: the chronology of generations, the appearances of sockets and the OEM preferences of contracts.</summary>
    [Serializable]
    public sealed class PlatformGenerationsSave
    {
        public List<GenerationStateDto> generations = new List<GenerationStateDto>();
        public List<SocketIntroDto> socketIntros = new List<SocketIntroDto>();

        /// <summary>The "per contract" mode: the generation each contract prefers (<see cref="ContractPrefDto.gen"/> "" = none).</summary>
        public List<ContractPrefDto> contractPrefs = new List<ContractPrefDto>();
    }

    /// <summary>The history of one generation: announced, launched (when and by whom), the early adopters.</summary>
    [Serializable]
    public sealed class GenerationStateDto
    {
        /// <summary>The generation id; an id that the current content does not have is kept as it is and ignored in calculations (FR-12 item 6).</summary>
        public string id;

        /// <summary>The announce date, or "" when not announced.</summary>
        public string announced;

        /// <summary>The launch date (the first CPU on the generation), or "" when not launched.</summary>
        public string launched;

        /// <summary>The company key that shipped the first CPU, or "".</summary>
        public string launchedBy;

        public List<AdopterDto> earlyAdopters = new List<AdopterDto>();
    }

    /// <summary>An early adopter of a generation.</summary>
    [Serializable]
    public sealed class AdopterDto
    {
        /// <summary>The company key (<c>ModCompanies.KeyOf</c>).</summary>
        public string company;

        public string date;
    }

    /// <summary>The first CPU of a company on a socket, with the main socket of the company at that moment (FR-6 item 1).</summary>
    [Serializable]
    public sealed class SocketIntroDto
    {
        public string company;
        public string socket;
        public string date;

        /// <summary>The key of the company's main socket at that moment, or "" when it had none.</summary>
        public string main;

        public int mainAgeMonths;

        /// <summary>The penalty strength 0..1 (FR-6 item 2).</summary>
        public float severity;
    }

    /// <summary>The generation a contract prefers in the "per contract" mode.</summary>
    [Serializable]
    public sealed class ContractPrefDto
    {
        /// <summary>The contract key (<c>ContractKey</c>).</summary>
        public string key;

        /// <summary>The generation id; "" = the contract prefers no generation.</summary>
        public string gen;
    }
}
