using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The translation between the section of the side save file and <see cref="ContractLogModel"/> (FR-8 item 5, NFR-3): <c>ModJson.Bind</c> in, <c>ModJson.From</c>
    /// out. The thin adapter <c>ContractLog</c> hands the core's calls to it, which keeps everything that can be tested away from the game: that a log goes through
    /// the JSON and comes back whole, that a missing or half-broken section gives a usable log, that the section carries no version field of its own.
    /// </summary>
    public static class ContractLogCodec
    {
        /// <summary>The key of the section in the side file: the id of the feature.</summary>
        public const string SectionId = ContractParamSpecs.FeatureId;

        /// <summary>The most the section may take in the side file, bytes (NFR-6): 256 KB.</summary>
        public const int MaxSectionBytes = 256 * 1024;

        /// <summary>The words by which the line about a log cut to the limit can be recognised in the problems <see cref="Write"/> returns.</summary>
        public const string SizeLimitMarker = "size limit";

        /// <summary>What the file adds around the section and the imprecision of the estimate (the braces and the keys of the wrapper), bytes.</summary>
        private const int WrapperSlack = 2 * 1024;

        /// <summary>Each step of the trimming takes off the oldest tenth of the log and of the results; this many steps leave 1.5 % of them.</summary>
        private const int MaxTrimSteps = 40;

        /// <summary>
        /// The version of the section (decision 0004). The core writes it as <c>_schema</c> and hands it to <see cref="Read"/>; the data has no field for it.
        /// A change of the shape of <see cref="ContractTransparencySave"/> raises it, and <see cref="ContractLogModel.FromSave(ContractTransparencySave, int, ContractRules, DateTime, List{string})"/> migrates by it.
        /// </summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// Fills <paramref name="model"/> from the section. A null section gives an empty log; fields of the wrong kind keep their defaults and each is a line in
        /// <paramref name="problems"/>, as is every record the model refuses. Never throws for any content of the section.
        /// </summary>
        public static void Read(ContractLogModel model, JsonObject section, int schema, ContractRules rules, DateTime today, List<string> problems)
        {
            if (model == null) throw new ArgumentNullException("model");

            ContractTransparencySave save = null;
            if (section != null)
            {
                var errors = new List<string>();
                save = ModJson.Bind<ContractTransparencySave>(section, errors);
                if (problems != null)
                {
                    foreach (string error in errors) problems.Add("section: " + error);
                }
            }
            model.FromSave(save, schema, rules, today, problems);
        }

        /// <summary>
        /// The section for the side file: the model through <c>ModJson.From</c>, without <c>_schema</c>. Never null. The section is held to <see cref="MaxSectionBytes"/> as
        /// the core writes it (indented, two levels deep): when a log is over that, the oldest records of the log and of the tender results are left out of the
        /// returned section, a tenth at a time, until it fits (NFR-6), and a line in <paramref name="problems"/> says how many. The model itself is not changed.
        /// </summary>
        public static JsonObject Write(ContractLogModel model, List<string> problems)
        {
            if (model == null) throw new ArgumentNullException("model");

            ContractTransparencySave save = model.ToSave();
            var errors = new List<string>();
            JsonObject section = ToObject(save, errors);
            if (problems != null)
            {
                foreach (string error in errors) problems.Add("section: " + error);
            }

            long bytes = SizeInFile(section);
            if (bytes <= MaxSectionBytes - WrapperSlack) return section;

            int droppedLog = 0;
            int droppedTenders = 0;
            for (int step = 0; step < MaxTrimSteps && bytes > MaxSectionBytes - WrapperSlack && (save.log.Count > 0 || save.tenders.Count > 0); step++)
            {
                droppedLog += DropOldest(save.log);
                droppedTenders += DropOldest(save.tenders);
                section = ToObject(save, null);
                bytes = SizeInFile(section);
            }

            if (problems != null)
            {
                problems.Add("the log is over the " + SizeLimitMarker + " of " + (MaxSectionBytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB: the oldest " +
                             droppedLog.ToString(CultureInfo.InvariantCulture) + " log record(s) and " + droppedTenders.ToString(CultureInfo.InvariantCulture) +
                             " tender result(s) are left out of the file");
            }
            return section;
        }

        /// <summary>
        /// The size of the section in the side file, bytes: the indented text (<c>IFileSystem</c> writes the file indented) in UTF-8, plus the two more levels of
        /// indentation the section has inside <c>features</c> (four spaces on every line).
        /// </summary>
        public static long SizeInFile(JsonObject section)
        {
            if (section == null) return 0;

            string text = ModJson.Write(section, true);
            long lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lines++;
            }
            return Encoding.UTF8.GetByteCount(text) + lines * 4;
        }

        private static JsonObject ToObject(ContractTransparencySave save, List<string> errors)
        {
            JsonNode node = ModJson.From(save, errors);
            return node as JsonObject ?? new JsonObject();
        }

        private static int DropOldest<T>(List<T> list)
        {
            if (list.Count == 0) return 0;
            int count = Math.Max(1, list.Count / 10);
            list.RemoveRange(0, count);
            return count;
        }
    }
}
