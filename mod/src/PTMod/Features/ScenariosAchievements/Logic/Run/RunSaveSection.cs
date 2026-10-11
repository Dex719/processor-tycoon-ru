using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The holder of the section <c>scenarios-achievements</c> of the side save file as the core's save model sees it
    /// (<see cref="ISideFileParticipant"/>, TSK-014): what the feature's <c>IModSaveParticipant</c> delegates to. It owns the run in play
    /// (<see cref="Run"/>) and has no game dependencies, so the real <c>SideFileModel</c> can drive it in a test: no section, a section from a newer
    /// build (the core resets the participant and keeps the stored text), a stored section. The core keeps the version of the section
    /// (<c>_schema</c>): it strips it before <see cref="Read"/> and adds it to what <see cref="Write"/> returns (FR-5, NFR-3).
    /// <para>
    /// A new game, a save without the section, a section the build cannot read and a section newer than this build all mean "no run": the director
    /// makes a <c>legacy</c> run on <c>GameReady(Loaded)</c> (FR-16 item 3) and sets it here, after which it is written with the next save; a
    /// newer section is kept by the core as it was, whatever this participant writes.
    /// </para>
    /// </summary>
    public sealed class RunSaveSection : ISideFileParticipant
    {
        private readonly Action<string> _warn;

        /// <param name="warn">Receives one English line per problem met while reading (the feature logs them); may be null.</param>
        public RunSaveSection(Action<string> warn = null)
        {
            _warn = warn;
        }

        public string SectionId
        {
            get { return RunSaveCodec.SectionId; }
        }

        public int SchemaVersion
        {
            get { return RunSaveCodec.SchemaVersion; }
        }

        /// <summary>
        /// The run in play, or null when there is none (a new game before the start, a save without the section). The director sets it when a
        /// run starts or a legacy run is born; <see cref="Read"/> and <see cref="ResetToDefaults"/> replace it.
        /// </summary>
        public RunState Run { get; set; }

        /// <summary>Raised after <see cref="Run"/> was replaced by a load or a reset: whatever was built over the old run (the evaluators, the tracker) is out of date.</summary>
        public event Action RunReplaced;

        /// <summary>A new game, a save without the section, a section newer than this build, leaving to the menu: no run.</summary>
        public void ResetToDefaults()
        {
            Run = null;
            RaiseReplaced();
        }

        /// <summary>The stored section (without <c>_schema</c>). <paramref name="schema"/> below <see cref="SchemaVersion"/> would mean "migrate"; there is only version 1.</summary>
        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            Run = RunSaveCodec.Read(section, problems);

            if (_warn != null)
            {
                foreach (string problem in problems) _warn(problem);
            }
            RaiseReplaced();
        }

        /// <summary>The data to store, without <c>_schema</c> (the core adds it): a fresh object each time; null (the section is left out) when there is no run.</summary>
        public JsonObject Write()
        {
            return RunSaveCodec.Write(Run);
        }

        private void RaiseReplaced()
        {
            Action handlers = RunReplaced;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception e)
                {
                    // A subscriber's fault must not fail the load of the whole side file.
                    if (_warn != null) _warn("run replaced: a subscriber failed (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
        }
    }
}
