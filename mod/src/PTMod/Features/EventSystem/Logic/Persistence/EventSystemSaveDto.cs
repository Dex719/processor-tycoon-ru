using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The section <c>event-system</c> (design.md, "Секция ModSave"): the keys of the example in the design (<c>nextInstanceId</c>, <c>lastProcessedDay</c>,
    /// <c>instances</c>, <c>scheduled</c>, <c>cooldowns</c>, <c>categories</c>, <c>firedOnce</c>, <c>flags</c>, <c>edges</c>, <c>log</c>) plus <c>deferred</c>
    /// (firings waiting for a free choice slot), <c>fired</c> and <c>choices</c> (the memory of <c>es.eventFired</c> / <c>es.choiceMade</c>). No version field:
    /// the core writes <c>_schema</c>. The active effects are not a list of their own: they live in their instances (<c>instances</c> keeps an instance while one
    /// of its effects runs), so a contribution is saved once.
    /// </summary>
    public sealed class EventSystemSaveDto
    {
        /// <summary>The id the next instance gets.</summary>
        public int nextInstanceId;

        public int lastProcessedDay;

        /// <summary>The instances that wait for a choice or have an effect running (<c>EffectRuntime</c>).</summary>
        public List<EventInstanceDto> instances = new List<EventInstanceDto>();

        public List<ScheduledFire> scheduled = new List<ScheduledFire>();
        public List<DeferredFire> deferred = new List<DeferredFire>();
        public List<CooldownRecord> cooldowns = new List<CooldownRecord>();
        public List<CategoryRecord> categories = new List<CategoryRecord>();
        public List<string> firedOnce = new List<string>();
        public List<string> edges = new List<string>();
        public List<FiredRecord> fired = new List<FiredRecord>();
        public List<ChoiceRecord> choices = new List<ChoiceRecord>();
        public List<FlagRecord> flags = new List<FlagRecord>();

        /// <summary>The journal, oldest first (<c>EventLogStore</c>).</summary>
        public List<EventLogEntry> log = new List<EventLogEntry>();
    }

    /// <summary>A part of the engine's state that the section saves besides the scheduler and the history (the instances and the journal of <c>EffectRuntime</c>).</summary>
    public interface IEventSavePart
    {
        /// <summary>Forgets everything (a new game, a save without the section, before a save is read).</summary>
        void Clear();

        void Export(EventSystemSaveDto dto);

        /// <summary>Takes the saved records; a record that does not make sense is dropped with a line in <paramref name="problems"/> (NFR-7).</summary>
        void Import(EventSystemSaveDto dto, List<string> problems);
    }
}
