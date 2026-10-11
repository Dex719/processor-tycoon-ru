using System;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The holder of the section <c>event-system</c> of the side save file (<c>IModSaveParticipant</c>, <c>SchemaVersion = 1</c>, no version field of its own:
    /// the core writes <c>_schema</c>). A thin adapter: the reading, the checks and the writing are the pure <see cref="EventSaveSection"/> (its saved shape is
    /// <see cref="EventSystemSaveDto"/>), behind the <see cref="SessionGate"/> that keeps the state of the game in play from being replaced by a hand-over of the
    /// core at the wrong moment. What follows a load - cancelling the events that are gone, <c>Restore</c>, linking to the letters by the tag, recalculating the
    /// buttons - is <see cref="EventRuntime.OnGameReady"/>, called on <c>ModTicks.GameReady</c>.
    /// </summary>
    internal sealed class EventPersistence : IModSaveParticipant
    {
        private readonly ISideFileParticipant _section;

        public EventPersistence(ISideFileParticipant section)
        {
            if (section == null) throw new ArgumentNullException("section");
            _section = section;
        }

        public string SectionId
        {
            get { return _section.SectionId; }
        }

        public int SchemaVersion
        {
            get { return _section.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
        }

        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
        }

        public JsonObject Write()
        {
            return _section.Write();
        }
    }
}
