using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The registry of content packs and of the events they define. A feature registers its pack in <c>IModFeature.Register</c>; the engine reads
    /// the files of every pack once, on the first <c>ModTicks.GameReady</c> of the session, after all features have registered (so the order of
    /// registration does not matter). Works while the event system is off too: the registration is kept.
    /// </summary>
    public static class EventRegistry
    {
        /// <summary>
        /// Registers a content pack. <paramref name="contentFolder"/> is the folder of the pack in <c>Content/</c> (usually the pack id; a leading
        /// <c>content/</c> is allowed and dropped). The events file is <c>PackOptions.EventsFile</c> (<c>events</c> by default), the optional
        /// <c>config.json</c> holds the categories and the channel limits. The ids of the pack's events must carry a prefix of the registering feature.
        /// A null <paramref name="options"/> means the defaults. The engine keeps the <paramref name="options"/> reference (see <see cref="PackOptions"/>).
        /// </summary>
        public static void RegisterPack(string packId, string contentFolder, PackOptions options)
        {
            if (string.IsNullOrEmpty(packId))
            {
                ModLog.Warn(EventSystemApi.FeatureId, "api: RegisterPack ignored, the pack id is empty");
                return;
            }

            string folder = string.IsNullOrEmpty(contentFolder) ? packId : contentFolder;
            PackOptions pack = options ?? new PackOptions();
            EventSystemApi.Register(api => api.RegisterPack(packId, folder, pack));
        }

        /// <summary>The definition of an event from any pack, or null (unknown id, or the packs are not loaded yet).</summary>
        public static EventDefinition Get(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            return EventSystemApi.Query(EventSystemSite.Get, api => api.GetDefinition(eventId), (EventDefinition)null);
        }

        /// <summary>All loaded definitions; empty before the packs are loaded. A snapshot to read, not to change.</summary>
        public static IList<EventDefinition> All
        {
            get
            {
                IList<EventDefinition> all = EventSystemApi.Query(EventSystemSite.All, api => api.GetDefinitions(), (IList<EventDefinition>)null);
                return all ?? new EventDefinition[0];
            }
        }
    }
}
