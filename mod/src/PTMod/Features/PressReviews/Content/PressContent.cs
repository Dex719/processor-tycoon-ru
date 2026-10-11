using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The loaded content of press-reviews (design.md, Component 2): the formulas' numbers (<c>review-config.json</c>), the outlets (<c>outlets.json</c>)
    /// and the notes (<c>notes.json</c>), read through <c>ModContent</c> (the built-in files plus the player's overrides) and checked by the pure
    /// <see cref="PressContentValidator"/>. Nothing here throws into the game: a broken piece falls back (defaults for the config, a stub outlet
    /// when no outlet is usable, no notes) and the reason goes to the log. Loaded once per session; the files are not read again.
    /// </summary>
    public sealed class PressContent
    {
        /// <summary>The <c>schema</c> of the content files this version reads.</summary>
        public const int Schema = 1;

        private static PressContent current;

        private PressContent(ReviewConfig config, IReadOnlyList<Outlet> outlets, IReadOnlyList<NoteTemplate> notes, ValidationReport report)
        {
            Config = config;
            Outlets = outlets;
            Notes = notes;
            Report = report;
        }

        public ReviewConfig Config { get; }

        /// <summary>The outlets in file order (at least one: the stub when the file yields none).</summary>
        public IReadOnlyList<Outlet> Outlets { get; }

        public IReadOnlyList<NoteTemplate> Notes { get; }

        /// <summary>The problems found while loading: errors, warnings and the years and segments without an outlet.</summary>
        public ValidationReport Report { get; }

        /// <summary>The content of the session, loaded on first use.</summary>
        public static PressContent Current
        {
            get
            {
                PressContent content = current;
                if (content == null)
                {
                    content = Load();
                    current = content;
                }
                return content;
            }
        }

        /// <summary>Reads the three files, checks them, logs the problems and the summary. Never throws.</summary>
        public static PressContent Load()
        {
            const string id = PressReviewsFeature.FeatureId;
            var report = new ValidationReport();
            ReviewConfig config;
            IReadOnlyList<Outlet> outlets;
            IReadOnlyList<NoteTemplate> notes;

            try
            {
                JsonObject json = ModContent.LoadConfig(id, "review-config.json", Schema);
                config = PressContentValidator.ParseConfig(json, report);
            }
            catch (Exception ex)
            {
                ModLog.Error(id, "content: review-config.json could not be read, the built-in defaults are used", ex);
                config = ReviewConfig.CreateDefault();
            }

            try
            {
                outlets = ModContent.LoadRecords<Outlet>(id, "outlets.json", PressContentValidator.ParseOutlet, null, Schema);
            }
            catch (Exception ex)
            {
                ModLog.Error(id, "content: outlets.json could not be read", ex);
                outlets = new Outlet[0];
            }
            if (outlets.Count == 0)
            {
                ModLog.Error(id, "content: no usable outlet in outlets.json, a stub outlet reviews everything");
                outlets = new[] { PressContentValidator.StubOutlet() };
            }

            try
            {
                notes = ModContent.LoadRecords<NoteTemplate>(id, "notes.json", PressContentValidator.ParseNote, null, Schema);
            }
            catch (Exception ex)
            {
                ModLog.Error(id, "content: notes.json could not be read, there will be no quarterly notes", ex);
                notes = new NoteTemplate[0];
            }

            ValidationReport whole = PressContentValidator.Validate(outlets, config, notes);
            report.Errors.AddRange(whole.Errors);
            report.Warnings.AddRange(whole.Warnings);
            report.CoverageHoles.AddRange(whole.CoverageHoles);
            LogReport(id, report, outlets.Count, notes.Count);

            return new PressContent(config, outlets, notes, report);
        }

        private static void LogReport(string id, ValidationReport report, int outlets, int notes)
        {
            foreach (string error in report.Errors) ModLog.Error(id, "content: " + error);
            foreach (string warning in report.Warnings) ModLog.Warn(id, "content: " + warning);
            if (report.CoverageHoles.Count > 0)
            {
                ModLog.Warn(id, "content: " + report.CoverageHoles.Count + " year/segment pair(s) have no outlet, the first is " + report.CoverageHoles[0]);
            }
            ModLog.Info(id, "content: " + outlets + " outlet(s), " + notes + " note(s), " + report.Errors.Count + " error(s), "
                + report.Warnings.Count + " warning(s), " + report.CoverageHoles.Count + " coverage hole(s)");
        }
    }
}
