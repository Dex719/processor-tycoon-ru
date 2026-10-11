using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The migrations of the section "rival-personalities" of the side save file (FR-10, NFR-3, TSK-023): when a section written by an older version of the
    /// mod is read, it is brought up to <see cref="RivalSaveCodec.SchemaVersion"/> step by step, <i>inside</i> <c>Read(section, schema)</c> (the core hands over
    /// the stored version in <c>schema</c>; the data of the feature carries no version field of its own). A step turns the section of version <c>N</c> into the
    /// section of version <c>N + 1</c>; it gets its own deep copy of the data, so the object the core holds is never changed (the contract of <c>Read</c>).
    /// <para>
    /// There is nothing to migrate yet: the section is at version 1 and <see cref="Steps"/> is empty. Fields added to version 1 after it was first shipped
    /// (<c>counters.executed</c>, <c>counters.lastPartnerByUid</c>) are additive, so an older section without them reads with empty lists and needs no step.
    /// The day <see cref="RivalSaveCodec.SchemaVersion"/> becomes 2, the step <c>1 → 2</c> is added to <see cref="Steps"/> and the test
    /// <c>EveryVersionOfTheSection_HasItsMigrationStep</c> stays red until it is. A section that is <i>newer</i> than the mod never gets here: the core gives the
    /// feature its defaults (<c>ResetToDefaults</c>) and writes the stored section back unchanged; <see cref="Upgrade(JsonObject, int, int, List{string})"/> reads such a
    /// section "as it is" anyway, should it ever be asked to.
    /// </para>
    /// Pure; never throws.
    /// </summary>
    public static class RivalStateMigrations
    {
        /// <summary>One step: the section of version <c>N</c> in, the section of version <c>N + 1</c> out (the input is a private copy and may be changed).</summary>
        public delegate JsonObject Step(JsonObject section);

        /// <summary>
        /// The steps of this version of the mod: <c>Steps[0]</c> is <c>1 → 2</c>, <c>Steps[1]</c> is <c>2 → 3</c> and so on, so there are
        /// <c>SchemaVersion − 1</c> of them (none while the section is at version 1).
        /// </summary>
        public static readonly IList<Step> Steps = new Step[0];

        /// <summary>Brings <paramref name="section"/>, stored as version <paramref name="from"/>, to version <paramref name="to"/> with the steps of this mod.</summary>
        public static JsonObject Upgrade(JsonObject section, int from, int to, List<string> problems)
        {
            return Upgrade(section, from, to, Steps, problems);
        }

        /// <summary>
        /// The same with another table of steps (the tests use it to check the mechanism before the first real step exists). The result is a new object when
        /// anything was migrated and the argument itself when nothing had to be (equal versions, a null section, a stored version above the target). A missing
        /// step or a step that throws stops the chain: the data migrated so far is returned and the reason is added to <paramref name="problems"/> (may be null),
        /// so the section is still read as far as it can be.
        /// </summary>
        public static JsonObject Upgrade(JsonObject section, int from, int to, IList<Step> steps, List<string> problems)
        {
            if (section == null) return null;

            if (from < 1)
            {
                Report(problems, "migration: the stored version " + Text(from) + " is below 1, the section is read as version 1");
                from = 1;
            }
            if (from == to) return section;
            if (from > to)
            {
                Report(problems, "migration: the stored version " + Text(from) + " is newer than " + Text(to) + ", the section is read as it is");
                return section;
            }

            var current = (JsonObject)section.DeepClone();
            for (int version = from; version < to; version++)
            {
                Step step = StepFor(steps, version);
                if (step == null)
                {
                    Report(problems, "migration: no step from version " + Text(version) + " to " + Text(version + 1) + ", the section is read as version " + Text(version));
                    return current;
                }

                try
                {
                    JsonObject next = step(current);
                    if (next != null) current = next;
                }
                catch (Exception ex)
                {
                    Report(problems, "migration: the step from version " + Text(version) + " to " + Text(version + 1) + " failed (" + ex.GetType().Name + ": "
                                     + ex.Message + "), the section is read as version " + Text(version));
                    return current;
                }
            }
            return current;
        }

        private static Step StepFor(IList<Step> steps, int fromVersion)
        {
            int index = fromVersion - 1;
            if (steps == null || index < 0 || index >= steps.Count) return null;
            return steps[index];
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
