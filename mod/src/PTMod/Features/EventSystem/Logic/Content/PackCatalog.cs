using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>One content pack as the catalog keeps it: what the feature registered and how the load went.</summary>
    public sealed class PackInfo
    {
        internal PackInfo(string packId, string folder, PackOptions options)
        {
            PackId = packId;
            Folder = folder;
            Options = options;
        }

        public string PackId { get; private set; }

        /// <summary>The folder of the pack in <c>Content/</c> (no leading <c>content/</c>).</summary>
        public string Folder { get; private set; }

        /// <summary>The options the feature gave, by reference: the engine reads the fields whenever it uses them.</summary>
        public PackOptions Options { get; private set; }

        /// <summary>The events file read for the pack, without the extension; set when the pack is loaded.</summary>
        public string EventsFile { get; internal set; }

        /// <summary>The feature whose prefixes the pack's ids carry; set when the pack is loaded.</summary>
        public string Owner { get; internal set; }

        /// <summary>The files of the pack have been read.</summary>
        public bool Loaded { get; internal set; }

        /// <summary>Definitions of the pack that are in the catalog now.</summary>
        public int EventCount { get; internal set; }

        /// <summary>Records of the pack that were rejected (bad content, a duplicate id, a dangling <c>FireEvent</c>).</summary>
        public int SkippedCount { get; internal set; }
    }

    /// <summary>
    /// The registry of content packs and of the events they define (FR-1, NFR-2; the store behind <c>EventRegistry</c> and <c>IEventSystemApi.RegisterPack</c>).
    /// <list type="number">
    /// <item><b>Register.</b> <see cref="RegisterPack"/> only notes the pack; nothing is read. The order of registration does not matter.</item>
    /// <item><b>Load.</b> <see cref="LoadPending"/> reads the packs that are not loaded, once, on the first <c>ModTicks.GameReady</c> of the session (when every
    /// feature has registered). Packs are taken in ordinal order of their ids. First every pending pack's <c>config.json</c> (optional; categories, limits), merged
    /// with the configs already known into <see cref="Config"/>; then each pack's events file through the source (<c>ModContent.LoadRecords</c>: built-in
    /// resource plus the player's override from <c>PTMod/content/&lt;folder&gt;/</c>), each record through <see cref="DefinitionMapper"/> and
    /// <see cref="ContentValidator"/>. A bad record is skipped alone with the log line <c>skip '&lt;id&gt;': &lt;reason&gt;</c> (AC-1.2); an id that another pack has
    /// is skipped as a duplicate; afterwards the events whose <c>FireEvent</c> names an event nobody defines are skipped too, until nothing more falls out.</item>
    /// <item><b>Read.</b> <see cref="Get"/>, <see cref="All"/> and <see cref="InCategory"/> answer from the loaded definitions, in a fixed order: packs by id,
    /// then the order of the file (a record the player's override replaced keeps its place). They are empty before the load.</item>
    /// </list>
    /// A pack registered after the load is read by the next <see cref="LoadPending"/>. Nothing here throws for content. Main thread only.
    /// </summary>
    public sealed class PackCatalog : ISchedulerCatalog
    {
        /// <summary>The <c>schema</c> of the events files this version reads.</summary>
        public const int ContentSchema = 1;

        /// <summary>The events file of a pack that names none (<see cref="PackOptions.EventsFile"/>).</summary>
        public const string DefaultEventsFile = "events";

        private const string ContentFolderPrefix = "content/";
        private static readonly EventDefinition[] NoDefinitions = new EventDefinition[0];

        private readonly IPackContentSource _source;
        private readonly IContentEnvironment _env;
        private readonly Action<string> _warn;
        private readonly Action<string> _info;

        private readonly List<PackInfo> _packs = new List<PackInfo>();
        private readonly Dictionary<string, PackConfig> _configs = new Dictionary<string, PackConfig>(StringComparer.Ordinal);
        private readonly List<EventDefinition> _definitions = new List<EventDefinition>();
        private readonly Dictionary<string, EventDefinition> _byId = new Dictionary<string, EventDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<EventDefinition>> _byCategory = new Dictionary<string, List<EventDefinition>>(StringComparer.Ordinal);

        private ReadOnlyCollection<EventDefinition> _snapshot;
        private EventConfig _config = EventConfig.Defaults;

        /// <param name="source">Where the files come from (<c>ModContent</c> in the game).</param>
        /// <param name="env">What the running mod knows about conditions, effect types and channels.</param>
        /// <param name="warn">Receives the lines for the log at warning level (<c>ModLog.Warn</c>).</param>
        /// <param name="info">Receives the lines at information level; null - none.</param>
        public PackCatalog(IPackContentSource source, IContentEnvironment env, Action<string> warn, Action<string> info = null)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (env == null) throw new ArgumentNullException("env");
            _source = source;
            _env = env;
            _warn = warn ?? (line => { });
            _info = info ?? (line => { });
        }

        /// <summary>The registered packs, in the order of registration.</summary>
        public IReadOnlyList<PackInfo> Packs
        {
            get { return _packs; }
        }

        /// <summary>The settings of all loaded packs together: categories, channel limits, <c>maxPendingChoices</c>. The defaults before the first load.</summary>
        public EventConfig Config
        {
            get { return _config; }
        }

        /// <summary>Some registered pack has not been read yet.</summary>
        public bool HasPending
        {
            get
            {
                foreach (PackInfo pack in _packs)
                {
                    if (!pack.Loaded) return true;
                }
                return false;
            }
        }

        /// <summary>The pack with this id, or null.</summary>
        public PackInfo PackOf(string packId)
        {
            foreach (PackInfo pack in _packs)
            {
                if (string.Equals(pack.PackId, packId, StringComparison.Ordinal)) return pack;
            }
            return null;
        }

        /// <summary>The options the pack was registered with (by reference), or null for an unknown pack.</summary>
        public PackOptions OptionsOf(string packId)
        {
            PackInfo pack = PackOf(packId);
            return pack != null ? pack.Options : null;
        }

        // ---- registering ----

        /// <summary>
        /// Notes a pack for the load. <paramref name="contentFolder"/> is the folder in <c>Content/</c> (the pack id when empty; a leading <c>content/</c> is
        /// dropped); <paramref name="options"/> is kept by reference (null - the defaults). False, with a warning, for an empty pack id, a folder that is not a
        /// plain name, and a pack id that is registered already (the first registration stays).
        /// </summary>
        public bool RegisterPack(string packId, string contentFolder, PackOptions options)
        {
            if (string.IsNullOrWhiteSpace(packId))
            {
                _warn("RegisterPack ignored: the pack id is empty");
                return false;
            }

            string id = packId.Trim();
            string folder = NormalizeFolder(string.IsNullOrWhiteSpace(contentFolder) ? id : contentFolder);
            if (!ContentMerger.IsSafeName(folder))
            {
                _warn("RegisterPack ignored for pack '" + id + "': '" + contentFolder + "' is not a plain folder name of Content/ (letters, digits, '-', '_', '.')");
                return false;
            }

            if (PackOf(id) != null)
            {
                _warn("RegisterPack ignored: pack '" + id + "' is registered already, the first registration stays");
                return false;
            }

            _packs.Add(new PackInfo(id, folder, options ?? new PackOptions()));
            return true;
        }

        /// <summary><c>content/design-flaw-recalls</c> and <c>design-flaw-recalls</c> name the same folder.</summary>
        internal static string NormalizeFolder(string folder)
        {
            string text = folder.Trim().Replace('\\', '/');
            if (text.StartsWith(ContentFolderPrefix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(ContentFolderPrefix.Length);
            return text.Trim('/');
        }

        // ---- reading ----

        /// <summary>The definition with this id from any loaded pack, or null.</summary>
        public EventDefinition Get(string eventId)
        {
            EventDefinition definition;
            return eventId != null && _byId.TryGetValue(eventId, out definition) ? definition : null;
        }

        /// <summary>Every loaded definition (a snapshot to read, not to change); empty before the load.</summary>
        public IList<EventDefinition> All
        {
            get
            {
                if (_snapshot == null) _snapshot = new ReadOnlyCollection<EventDefinition>(new List<EventDefinition>(_definitions));
                return _snapshot;
            }
        }

        /// <summary>The definitions of a category in the fixed order; empty for a category nobody uses.</summary>
        public IReadOnlyList<EventDefinition> InCategory(string category)
        {
            List<EventDefinition> list;
            return category != null && _byCategory.TryGetValue(category, out list) ? (IReadOnlyList<EventDefinition>)list : NoDefinitions;
        }

        // ---- loading ----

        /// <summary>
        /// Reads every pack that has not been read (see the class summary). Returns how many definitions this call added. Safe to call again: a pack is read
        /// once, and a call with nothing pending does nothing.
        /// </summary>
        public int LoadPending()
        {
            var pending = new List<PackInfo>();
            foreach (PackInfo pack in _packs)
            {
                if (!pack.Loaded) pending.Add(pack);
            }
            if (pending.Count == 0) return 0;
            pending.Sort((a, b) => string.CompareOrdinal(a.PackId, b.PackId));

            foreach (PackInfo pack in pending) LoadConfig(pack);
            _config = EventConfig.Merge(_configs.Values, _warn);

            int before = _definitions.Count;
            foreach (PackInfo pack in pending) LoadEvents(pack);
            RemoveDanglingFireEvents();
            return _definitions.Count - before;
        }

        private void LoadConfig(PackInfo pack)
        {
            string file = string.IsNullOrWhiteSpace(pack.Options.ConfigFile) ? PackConfigReader.FileName : pack.Options.ConfigFile.Trim();
            JsonObject json = null;
            try
            {
                json = _source.LoadConfig(pack.Folder, file, PackConfigReader.Schema);
            }
            catch (Exception ex)
            {
                _warn("pack '" + pack.PackId + "': " + file + ".json could not be read: " + ex.GetType().Name + ": " + ex.Message);
            }

            var problems = new List<string>();
            _configs[pack.PackId] = PackConfigReader.Read(pack.PackId, json, problems);
            foreach (string problem in problems) _warn("pack '" + pack.PackId + "': " + file + ".json: " + problem);
        }

        private void LoadEvents(PackInfo pack)
        {
            pack.Loaded = true;
            pack.EventsFile = string.IsNullOrWhiteSpace(pack.Options.EventsFile) ? DefaultEventsFile : pack.Options.EventsFile.Trim();
            pack.Owner = OwnerOf(pack);

            var mapper = new DefinitionMapper(_env.ParseCondition);
            var validator = new ContentValidator(_env, pack.PackId, pack.Owner, _config.HasCategory);

            IReadOnlyList<EventDefinition> records = NoDefinitions;
            try
            {
                records = _source.LoadRecords<EventDefinition>(pack.Folder, pack.EventsFile,
                    (json, messages) => MapRecord(pack, mapper, json, messages),
                    (definition, messages) => ValidateRecord(pack, validator, definition, messages),
                    ContentSchema);
            }
            catch (Exception ex)
            {
                _warn("pack '" + pack.PackId + "': " + pack.EventsFile + ".json could not be loaded: " + ex.GetType().Name + ": " + ex.Message);
            }

            foreach (EventDefinition definition in records)
            {
                if (definition == null) continue;
                definition.packId = pack.PackId;
                Add(definition);
                pack.EventCount++;
            }

            _info("pack '" + pack.PackId + "': " + pack.EventCount + " events loaded from " + pack.Folder + "/" + pack.EventsFile + ".json"
                  + (pack.SkippedCount > 0 ? ", " + pack.SkippedCount + " skipped" : string.Empty));
        }

        /// <summary>
        /// The feature that owns the ids of the pack: the pack id when it is a feature with prefixes, else the feature of
        /// <see cref="PackOptions.SettingsFeatureId"/>, else the pack id (every id is then refused, and the message says why).
        /// </summary>
        private static string OwnerOf(PackInfo pack)
        {
            if (ModIds.PrefixesOf(pack.PackId).Count > 0) return pack.PackId;

            string settings = pack.Options.SettingsFeatureId;
            if (!string.IsNullOrEmpty(settings) && ModIds.PrefixesOf(settings).Count > 0) return settings;
            return pack.PackId;
        }

        // ---- the two functions given to the source ----

        private EventDefinition MapRecord(PackInfo pack, DefinitionMapper mapper, JsonObject json, List<string> messages)
        {
            var own = new List<string>();
            EventDefinition definition = mapper.Map(json, own);
            if (definition == null)
            {
                pack.SkippedCount++;
                Skip(json == null ? null : json.GetString("id"), own);
            }
            else
            {
                definition.packId = pack.PackId;
            }

            messages.AddRange(own);
            return definition;
        }

        private bool ValidateRecord(PackInfo pack, ContentValidator validator, EventDefinition definition, List<string> messages)
        {
            var own = new List<string>();
            bool accepted;

            EventDefinition other;
            if (_byId.TryGetValue(definition.id, out other))
            {
                own.Add("duplicate id: pack '" + other.packId + "' defines the event already (id)");
                accepted = false;
            }
            else
            {
                accepted = validator.Validate(definition, own);
            }

            if (!accepted)
            {
                pack.SkippedCount++;
                Skip(definition.id, own);
            }
            messages.AddRange(own);
            return accepted;
        }

        private void Skip(string id, List<string> reasons)
        {
            _warn("skip '" + (id ?? "?") + "': " + (reasons.Count == 0 ? "the record was rejected" : string.Join("; ", reasons)));
        }

        // ---- the store ----

        private void Add(EventDefinition definition)
        {
            _definitions.Add(definition);
            _byId[definition.id] = definition;
            _snapshot = null;

            if (string.IsNullOrEmpty(definition.category)) return;
            List<EventDefinition> list;
            if (!_byCategory.TryGetValue(definition.category, out list))
            {
                list = new List<EventDefinition>();
                _byCategory[definition.category] = list;
            }
            list.Add(definition);
        }

        private void Remove(EventDefinition definition)
        {
            _definitions.Remove(definition);
            _byId.Remove(definition.id);
            _snapshot = null;

            List<EventDefinition> list;
            if (!string.IsNullOrEmpty(definition.category) && _byCategory.TryGetValue(definition.category, out list)) list.Remove(definition);

            PackInfo pack = PackOf(definition.packId);
            if (pack != null)
            {
                pack.EventCount--;
                pack.SkippedCount++;
            }
        }

        // ---- references between events ----

        /// <summary>
        /// Skips the events whose <c>FireEvent</c> names an event that no loaded pack defines (design.md, "Security": <c>FireEvent</c> only to existing ids).
        /// Removing one can orphan another, so it repeats until nothing falls out.
        /// </summary>
        private void RemoveDanglingFireEvents()
        {
            bool removed;
            do
            {
                removed = false;
                foreach (EventDefinition definition in _definitions.ToArray())
                {
                    string missing = FirstMissingFireTarget(definition);
                    if (missing == null) continue;

                    Remove(definition);
                    _warn("skip '" + definition.id + "': FireEvent refers to the unknown event '" + missing + "'");
                    removed = true;
                }
            }
            while (removed);
        }

        private string FirstMissingFireTarget(EventDefinition definition)
        {
            string missing = FirstMissing(definition.effects);
            if (missing != null || definition.choices == null) return missing;

            foreach (EventChoice choice in definition.choices)
            {
                if (choice == null) continue;
                missing = FirstMissing(choice.effects);
                if (missing != null) return missing;
            }
            return null;
        }

        private string FirstMissing(EventEffect[] effects)
        {
            if (effects == null) return null;
            foreach (EventEffect effect in effects)
            {
                if (effect == null || !string.Equals(effect.type, StandardEffectRules.FireEvent, StringComparison.Ordinal) || effect.@params == null) continue;

                string target = effect.@params.GetString("eventId", null);
                if (!string.IsNullOrEmpty(target) && !_byId.ContainsKey(target)) return target;
            }
            return null;
        }
    }
}
