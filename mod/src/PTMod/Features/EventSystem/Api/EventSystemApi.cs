using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the engine of <c>event-system</c> implements behind the static entry points (<see cref="EventBus"/>, <see cref="EventEffects"/>,
    /// <see cref="EventAi"/>, <see cref="EventRegistry"/>, <see cref="EventLog"/>). Other features never call it directly: they use the static classes,
    /// which forward here (and do nothing, or answer "not available", while no implementation is installed or the feature is off).
    /// The implementation is installed once per session by <c>EventSystemFeature.Register</c> with <see cref="EventSystemApi.Install"/>.
    /// Members are called on the Unity main thread, each inside a try/catch of the caller (<c>ModHooks.Guard</c>), so an exception costs the
    /// engine a fault count but never reaches the calling feature or the game.
    /// </summary>
    public interface IEventSystemApi
    {
        // ---- EventRegistry ----

        /// <summary>Registers a content pack; its files are read later, on the first <c>GameReady</c> after every feature has registered.</summary>
        void RegisterPack(string packId, string contentFolder, PackOptions options);

        /// <summary>The definition with this id from any loaded pack, or null.</summary>
        EventDefinition GetDefinition(string eventId);

        /// <summary>Every loaded definition; empty before the packs are loaded.</summary>
        IList<EventDefinition> GetDefinitions();

        // ---- EventEffects ----

        /// <summary>Registers a handler for an effect type; a second registration of a type keeps the first handler and logs a warning.</summary>
        void RegisterEffectHandler(string type, IEventEffectHandler handler);

        /// <summary>The handler of an effect type, or null.</summary>
        IEventEffectHandler GetEffectHandler(string type);

        /// <summary>The registered effect types, in the order of registration.</summary>
        IList<string> GetEffectTypes();

        /// <summary>
        /// Resolves and applies an effect outside an event: a service instance of kind <c>Direct</c> is created, with <paramref name="sourceId"/> as its
        /// <c>eventId</c>, so saving, expiry and the journal work as for any effect. Null when the effect could not be resolved.
        /// </summary>
        AppliedEffect ApplyDirect(EventEffect spec, string companyKey, string sourceId, string[] args);

        // ---- EventBus ----

        /// <summary>Fires an event now or after a delay; the answer says what happened.</summary>
        EventFireResult Fire(string eventId, EventFireContext context);

        // ---- EventAi ----

        /// <summary>Adds an AI chooser to the chain, ordered by descending <paramref name="priority"/> (equal priorities keep the order of adding).</summary>
        void AddChooser(IEventAiChooser chooser, int priority);

        /// <summary>Removes a chooser from the chain; an unknown chooser is ignored.</summary>
        void RemoveChooser(IEventAiChooser chooser);

        // ---- EventLog ----

        /// <summary>The journal, oldest first; empty when nothing was logged.</summary>
        IList<EventLogEntry> GetLogEntries();

        /// <summary>Opens the "Event journal" window.</summary>
        void OpenLogWindow();
    }

    /// <summary>
    /// The one place where the implementation of the event system is plugged in, and the answer to "can I use it now". The static classes of the
    /// API read it; a feature that needs to know whether the event system works asks <see cref="IsActive"/>.
    /// <list type="bullet">
    /// <item><b>Null object.</b> Until <see cref="Install"/> is called, <see cref="Instance"/> is <see cref="NullEventSystemApi"/>: queries answer
    /// "nothing" (null, empty lists), <c>EventBus.Fire</c> answers <see cref="EventFireResult.Disabled"/>, nothing throws. The same holds while the
    /// feature is switched off in the hub: the runtime calls (<c>Fire</c>, <c>ApplyDirect</c>, <c>OpenWindow</c>) do not reach the implementation.</item>
    /// <item><b>Early registrations.</b> A registration made before <see cref="Install"/> (<c>RegisterPack</c>, <c>EventEffects.Register</c>,
    /// <c>EventAi.AddChooser</c>/<c>RemoveChooser</c>) is kept and replayed, in order, when the implementation arrives, so the order in which the
    /// features register does not matter. Registrations made while the feature is switched off go to the implementation as usual (it keeps them).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static class EventSystemApi
    {
        /// <summary>The id of the feature that provides the API.</summary>
        public const string FeatureId = "event-system";

        private static IEventSystemApi _implementation = NullEventSystemApi.Instance;
        private static readonly List<Action<IEventSystemApi>> Deferred = new List<Action<IEventSystemApi>>();

        /// <summary>The installed implementation, or <see cref="NullEventSystemApi.Instance"/> before <see cref="Install"/>. Never null.</summary>
        public static IEventSystemApi Instance
        {
            get { return _implementation; }
        }

        /// <summary>An implementation has been installed.</summary>
        public static bool IsInstalled
        {
            get { return !ReferenceEquals(_implementation, NullEventSystemApi.Instance); }
        }

        /// <summary>
        /// The event system works now: an implementation is installed and the feature is Enabled (<c>ModFeatures.IsActive</c>). A dependent feature
        /// that wants to degrade quietly without the event system checks this (or its own dependency state).
        /// </summary>
        public static bool IsActive
        {
            get { return IsInstalled && ModFeatures.IsActive(FeatureId); }
        }

        /// <summary>
        /// Plugs in the implementation (called by the feature, once per session) and replays the registrations made before. A null argument
        /// does the same as <see cref="Uninstall"/>.
        /// </summary>
        public static void Install(IEventSystemApi implementation)
        {
            if (implementation == null || ReferenceEquals(implementation, NullEventSystemApi.Instance))
            {
                Uninstall();
                return;
            }

            _implementation = implementation;
            if (Deferred.Count == 0) return;

            Action<IEventSystemApi>[] pending = Deferred.ToArray();
            Deferred.Clear();
            foreach (Action<IEventSystemApi> operation in pending) RunGuarded("api/replay", operation);
        }

        /// <summary>Puts the null object back (tests; a failed start). Registrations kept by the old implementation are not carried over.</summary>
        public static void Uninstall()
        {
            _implementation = NullEventSystemApi.Instance;
        }

        // ---- helpers of the static classes (same assembly) ----

        /// <summary>Runs a registration on the implementation now, or keeps it until <see cref="Install"/>. An exception is counted against the feature.</summary>
        internal static void Register(Action<IEventSystemApi> operation)
        {
            if (operation == null) return;
            if (!IsInstalled)
            {
                Deferred.Add(operation);
                return;
            }

            RunGuarded("api/register", operation);
        }

        /// <summary>A query that is answered even while the feature is off (the registry, the handler table, the journal); <paramref name="fallback"/> on a failure.</summary>
        internal static T Query<T>(string site, Func<IEventSystemApi, T> query, T fallback)
        {
            IEventSystemApi api = _implementation;
            return ModHooks.Guard(FeatureId, site, () => query(api), fallback);
        }

        /// <summary>A call that needs the working event system: <paramref name="fallback"/> when it is not <see cref="IsActive"/> and when the call fails.</summary>
        internal static T Run<T>(string site, Func<IEventSystemApi, T> call, T fallback)
        {
            if (!IsActive) return fallback;
            IEventSystemApi api = _implementation;
            return ModHooks.Guard(FeatureId, site, () => call(api), fallback);
        }

        private static void RunGuarded(string site, Action<IEventSystemApi> operation)
        {
            IEventSystemApi api = _implementation;
            ModHooks.Guard(FeatureId, site, () => operation(api));
        }
    }

    /// <summary>The names of the guarded call sites of the API (the fault counter and the log use them).</summary>
    internal static class EventSystemSite
    {
        public const string Get = "api/EventRegistry.Get";
        public const string All = "api/EventRegistry.All";
        public const string Fire = "api/EventBus.Fire";
        public const string Handler = "api/EventEffects.Get";
        public const string Types = "api/EventEffects.RegisteredTypes";
        public const string ApplyDirect = "api/EventEffects.ApplyDirect";
        public const string Entries = "api/EventLog.Entries";
        public const string OpenWindow = "api/EventLog.OpenWindow";
    }

    /// <summary>
    /// The implementation used while the event system is not there: every query answers "nothing", every registration is ignored, and
    /// <see cref="Fire"/> says <see cref="EventFireResult.Disabled"/>. It lets a dependent feature call the API without checking anything first.
    /// </summary>
    public sealed class NullEventSystemApi : IEventSystemApi
    {
        public static readonly NullEventSystemApi Instance = new NullEventSystemApi();

        private static readonly IList<EventDefinition> NoDefinitions = new ReadOnlyCollection<EventDefinition>(new EventDefinition[0]);
        private static readonly IList<string> NoTypes = new ReadOnlyCollection<string>(new string[0]);
        private static readonly IList<EventLogEntry> NoEntries = new ReadOnlyCollection<EventLogEntry>(new EventLogEntry[0]);

        private NullEventSystemApi()
        {
        }

        public void RegisterPack(string packId, string contentFolder, PackOptions options)
        {
        }

        public EventDefinition GetDefinition(string eventId)
        {
            return null;
        }

        public IList<EventDefinition> GetDefinitions()
        {
            return NoDefinitions;
        }

        public void RegisterEffectHandler(string type, IEventEffectHandler handler)
        {
        }

        public IEventEffectHandler GetEffectHandler(string type)
        {
            return null;
        }

        public IList<string> GetEffectTypes()
        {
            return NoTypes;
        }

        public AppliedEffect ApplyDirect(EventEffect spec, string companyKey, string sourceId, string[] args)
        {
            return null;
        }

        public EventFireResult Fire(string eventId, EventFireContext context)
        {
            return EventFireResult.Disabled;
        }

        public void AddChooser(IEventAiChooser chooser, int priority)
        {
        }

        public void RemoveChooser(IEventAiChooser chooser)
        {
        }

        public IList<EventLogEntry> GetLogEntries()
        {
            return NoEntries;
        }

        public void OpenLogWindow()
        {
        }
    }
}
