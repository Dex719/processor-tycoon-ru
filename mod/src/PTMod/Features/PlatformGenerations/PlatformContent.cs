using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// Loads the content of the feature through <c>ModContent</c> (FR-1, NFR-5): <c>generations.json</c> (records, the built-in file with the
    /// player's override on top) and <c>balance.json</c> (a config). The thin game side of <see cref="GenerationCatalog"/> and
    /// <see cref="PlatformBalance"/>: the parsing and the checks are theirs. Every problem is one line in the log under the feature's id.
    /// </summary>
    internal static class PlatformContent
    {
        public const string GenerationsFile = "generations.json";
        public const string BalanceFile = "balance.json";

        /// <summary>The <c>schema</c> of both files this version reads.</summary>
        public const int Schema = 1;

        /// <summary>The numbers of the feature. Needs no scene; a broken file gives the defaults of the code.</summary>
        public static PlatformBalance LoadBalance()
        {
            JsonObject config = ModContent.LoadConfig(PlatformGenerationsFeature.FeatureId, BalanceFile, Schema);
            var problems = new List<string>();
            PlatformBalance balance = PlatformBalance.FromJson(config, problems);
            foreach (string problem in problems) ModLog.Warn(PlatformGenerationsFeature.FeatureId, "content: " + problem);
            return balance;
        }

        /// <summary>
        /// The generations. The sockets and memories are checked against the game's <c>Package</c> and <c>Memory</c> objects, which live in the
        /// game scene: call it only when the game scene is ready (<c>PTModHost.IsSceneReady</c> and <c>CurrentScene == GameScene</c>).
        /// </summary>
        public static GenerationCatalog LoadCatalog()
        {
            const string feature = PlatformGenerationsFeature.FeatureId;
            Func<string, bool> packageExists = Cached(name => GameDataFinder.FindPackage(name) != null);
            Func<string, bool> memoryExists = Cached(name => GameDataFinder.FindMemory(name) != null);

            // The validation runs inside the loader as well, so that a broken record of an override leaves the built-in one in place.
            IReadOnlyList<GenerationDef> records = ModContent.LoadRecords<GenerationDef>(
                feature, GenerationsFile,
                GenerationDef.FromJson,
                (record, errors) => GenerationCatalog.Validate(record, packageExists, memoryExists, errors),
                Schema);

            var problems = new List<string>();
            GenerationCatalog catalog = GenerationCatalog.Build(records, packageExists, memoryExists, problems);
            foreach (string problem in problems) ModLog.Warn(feature, "content: " + problem);

            ModLog.Debug(feature, "content: " + catalog.Count + " generation(s) in the catalog");
            return catalog;
        }

        /// <summary><c>DataFinder.FindPackage</c> scans the scene on every call: remember the answers.</summary>
        private static Func<string, bool> Cached(Func<string, bool> exists)
        {
            var answers = new Dictionary<string, bool>(StringComparer.Ordinal);
            return name =>
            {
                if (name == null) return false;
                bool known;
                if (!answers.TryGetValue(name, out known))
                {
                    known = exists(name);
                    answers[name] = known;
                }
                return known;
            };
        }
    }
}
