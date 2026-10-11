using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// Loads the steps and the rhythm of the tutorial (TSK-002, FR-3): <c>steps.json</c> through <c>ModContent.LoadRecords</c> (the built-in file
    /// with the player's override merged in by step id, a broken record skipped and reported), <c>config.json</c> through
    /// <c>ModContent.LoadConfig</c>, then <see cref="StepCatalog.Build"/> for what only the whole set shows. Every dropped step is a line in the log
    /// (<c>[first-year-onboarding] WARN ...</c>); the rest of the tutorial works. Never throws.
    /// </summary>
    internal static class StepContentLoader
    {
        internal const string StepsFile = "steps.json";
        internal const string ConfigFile = "config.json";

        /// <summary>The <c>schema</c> of the two files this version reads.</summary>
        internal const int Schema = 1;

        /// <summary>The checked catalog; an empty one (<see cref="StepCatalog.IsEmpty"/>) when the content cannot be used at all.</summary>
        internal static StepCatalog Load(IStepValidationContext context)
        {
            string feature = OnboardingFeature.FeatureId;
            try
            {
                var configNotes = new List<string>();
                OnboardingConfig config = StepMapper.MapConfig(ModContent.LoadConfig(feature, ConfigFile, Schema), configNotes);
                foreach (string note in configNotes) ModLog.Warn(feature, "content: " + note);

                IReadOnlyList<StepRecord> records = ModContent.LoadRecords<StepRecord>(
                    feature, StepsFile,
                    (json, errors) =>
                    {
                        StepDefinition definition = StepMapper.Map(json, errors);
                        return definition == null ? null : new StepRecord(definition);
                    },
                    // Warnings are left to Build below, which runs the same checks once more and reports each finding once.
                    (record, errors) => StepValidator.Validate(record.Definition, context, errors, null),
                    Schema);

                var steps = new List<StepDefinition>(records.Count);
                foreach (StepRecord record in records) steps.Add(record.Definition);

                StepCatalog catalog = StepCatalog.Build(steps, config, context);
                foreach (string error in catalog.Errors) ModLog.Warn(feature, "content: " + error);
                foreach (string warning in catalog.Warnings) ModLog.Warn(feature, "content: " + warning);

                ModLog.Info(feature, "content: " + catalog.All.Count + " step(s) loaded, sequential steps per track: " + DescribeTracks(catalog)
                                      + (catalog.IsEmpty ? " - the tutorial cannot start" : string.Empty));
                return catalog;
            }
            catch (Exception ex)
            {
                ModLog.Error(feature, "content: the steps could not be loaded, the tutorial cannot start", ex);
                return StepCatalog.Build(null, null, null);
            }
        }

        private static string DescribeTracks(StepCatalog catalog)
        {
            var parts = new List<string>();
            foreach (string track in StepValidator.Tracks) parts.Add(track + "=" + catalog.SequentialCount(track));
            return string.Join(", ", parts.ToArray());
        }
    }
}
