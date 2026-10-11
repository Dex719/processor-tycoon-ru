using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The holder of the section <c>event-system</c> of the side save file (<c>ISideFileParticipant</c>; the feature hands it to <c>ctx.SaveParticipant</c>
    /// through a thin <c>IModSaveParticipant</c>). Reads and writes <see cref="EventSystemSaveDto"/> with <c>ModJson</c>; a record that does not make sense is
    /// dropped with a warning (NFR-7), the rest is kept. Main thread only.
    /// </summary>
    public sealed class EventSaveSection : ISideFileParticipant
    {
        /// <summary>The key of the section: the feature id.</summary>
        public const string Id = "event-system";

        /// <summary>The version of the section the core writes as <c>_schema</c>.</summary>
        public const int Schema = 1;

        private readonly SchedulerState _state;
        private readonly EventHistory _history;
        private readonly Action<string> _warn;
        private readonly IEventSavePart _runtime;

        public EventSaveSection(SchedulerState state, EventHistory history, Action<string> warn)
            : this(state, history, warn, null)
        {
        }

        /// <param name="runtime">The instances and the journal (<c>EffectRuntime</c>); null - the section keeps the scheduler and the history only.</param>
        public EventSaveSection(SchedulerState state, EventHistory history, Action<string> warn, IEventSavePart runtime)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (history == null) throw new ArgumentNullException("history");
            _state = state;
            _history = history;
            _warn = warn ?? (line => { });
            _runtime = runtime;
        }

        public string SectionId
        {
            get { return Id; }
        }

        public int SchemaVersion
        {
            get { return Schema; }
        }

        /// <summary>A new game, an old save without the section, a newer or unreadable section: an empty state.</summary>
        public void ResetToDefaults()
        {
            _state.Clear();
            _history.Clear();
            if (_runtime != null) _runtime.Clear();
        }

        public void Read(JsonObject section, int schema)
        {
            ResetToDefaults();
            if (section == null) return;

            var errors = new List<string>();
            EventSystemSaveDto dto = ModJson.Bind<EventSystemSaveDto>(section, errors);
            foreach (string error in errors) _warn("save: " + error);

            var problems = new List<string>();
            _state.Import(dto, problems);
            _history.Import(dto, problems);
            if (_runtime != null) _runtime.Import(dto, problems);
            foreach (string problem in problems) _warn("save: " + problem);
        }

        public JsonObject Write()
        {
            var dto = new EventSystemSaveDto();
            _state.Export(dto);
            _history.Export(dto);
            if (_runtime != null) _runtime.Export(dto);
            return ModJson.From(dto) as JsonObject;
        }
    }
}
