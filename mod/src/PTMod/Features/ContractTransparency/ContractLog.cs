using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The holder of the section <c>contract-transparency</c> of the side save file (design.md, Component 10; FR-8 item 5, NFR-3): the thin adapter between the core's
    /// calls and <see cref="ContractLogModel"/>. All the work is in <see cref="ContractLogCodec"/> and the model, which the tests reach; this class adds the log of the
    /// problems and the game's date.
    /// <list type="bullet">
    /// <item>The version of the section is the core's business: <see cref="SchemaVersion"/> is written as <c>_schema</c> and handed back to <see cref="Read"/>; the data
    /// has no field for it and <see cref="Write"/> does not return it.</item>
    /// <item>A save without the section, a new game and a section from a newer version of the mod reach <see cref="ResetToDefaults"/>: an empty log. The core keeps a newer
    /// section as it was and does not overwrite it.</item>
    /// <item>A section that is damaged field by field is read as far as it goes; each problem is a line of <c>ModLog</c>.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ContractLog : IModSaveParticipant
    {
        private const string Feature = ContractParamSpecs.FeatureId;

        /// <summary>Years before this are not a game date: the date of the game is not read yet (a load is in progress) and the age of old results is measured from the data.</summary>
        private const int FirstPlausibleYear = 1900;

        private readonly ContractLogModel _model;
        private bool _sizeWarned;

        public ContractLog(ContractRules rules)
        {
            _model = new ContractLogModel(rules);
        }

        /// <summary>The log itself.</summary>
        public ContractLogModel Model
        {
            get { return _model; }
        }

        public string SectionId
        {
            get { return ContractLogCodec.SectionId; }
        }

        public int SchemaVersion
        {
            get { return ContractLogCodec.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            _model.Reset();
        }

        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            using (PerfTimer.Start(TimingSites.LogRead))
            {
                ContractLogCodec.Read(_model, section, schema, _model.Rules, GameToday(), problems);
            }
            foreach (string line in problems) ModLog.Warn(Feature, "save: " + line);
        }

        public JsonObject Write()
        {
            var problems = new List<string>();
            JsonObject section;
            using (PerfTimer.Start(TimingSites.LogWrite))
            {
                section = ContractLogCodec.Write(_model, problems);
            }
            foreach (string line in problems)
            {
                // A log that sits at the size limit is cut at every save: the first time is a warning, the next ones are not worth a line each.
                bool cut = line.IndexOf(ContractLogCodec.SizeLimitMarker, StringComparison.Ordinal) >= 0;
                if (cut && _sizeWarned) ModLog.Debug(Feature, "save: " + line);
                else ModLog.Warn(Feature, "save: " + line);
                if (cut) _sizeWarned = true;
            }
            return section;
        }

        /// <summary>The game date, or <c>default(DateTime)</c> when there is none yet.</summary>
        private static DateTime GameToday()
        {
            DateTime today = ModTicks.Today;
            return today.Year < FirstPlausibleYear ? default(DateTime) : today;
        }
    }
}
