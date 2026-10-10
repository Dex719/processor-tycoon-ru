using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>
    /// One block of the section <c>mod-framework</c> of the side file (<see cref="CoreSection"/>): <c>startDate</c>, <c>random</c>, <c>flags</c>,
    /// <c>newGameOptions</c>, <c>modifiers</c>, <c>reputation</c>, <c>mail</c>. The core services own their blocks and plug them in with
    /// <see cref="CoreSection.RegisterPart(ICoreSectionPart)"/> from a <c>[CoreInit(CoreInitStage.Declare)]</c> method, so the section is assembled
    /// from parts and no package edits another's file. The contract of <see cref="IModSaveParticipant"/>, per block and without a version: the
    /// version <c>_schema</c> belongs to the whole section (the model reads and writes it) and arrives as <c>schema</c> in <see cref="Read"/>.
    /// Every call is made by <see cref="CoreSection"/> inside try/catch; an exception only costs the block (see there).
    /// </summary>
    internal interface ICoreSectionPart
    {
        /// <summary>The member of the section the block is stored under (<c>random</c>, <c>modifiers</c>, ...). Unique, not empty, not <c>_schema</c>.</summary>
        string Key { get; }

        /// <summary>New game, leaving to the menu, a save without the block: back to defaults. Also called after a <see cref="Read"/> that threw.</summary>
        void ResetToDefaults();

        /// <summary>
        /// The stored block, never null (an absent block is <see cref="ResetToDefaults"/>). <paramref name="schema"/> is the stored version of the
        /// whole section (1 when the field was missing): a value below <see cref="CoreSection.Version"/> means "migrate inside". A block that
        /// is malformed is the part's to repair, with a message to <c>ModLog</c> for what it dropped; throwing keeps the stored text for the next
        /// save and resets the part. Must not modify the node.
        /// </summary>
        void Read(JsonNode block, int schema);

        /// <summary>The block to store: a tree the caller may keep (a fresh one every time). Null, or JSON null, leaves the block out.</summary>
        JsonNode Write();
    }

    /// <summary>
    /// Migrations of the section <c>mod-framework</c> from an older <c>_schema</c> to the current one (design.md, "Схемы"). Only version 1 exists:
    /// the step is here so that the first change of the section format has its place and <see cref="CoreSection"/> already calls it.
    /// </summary>
    internal static class CoreSectionMigrations
    {
        /// <summary>The section brought to <see cref="CoreSection.Version"/>; <paramref name="section"/> itself is never modified (copy before changing).</summary>
        internal static JsonObject Apply(JsonObject section, int fromSchema)
        {
            return section;   // nothing older than version 1 exists
        }
    }

    /// <summary>
    /// The participant of the core in the side file: the section <c>mod-framework</c> (FR-5, design.md "DTO бокового файла"), built from
    /// <see cref="ICoreSectionPart"/> blocks. <see cref="ModSave"/> registers it first, so it leads the <c>features</c> object.
    /// <list type="bullet">
    /// <item><b>Blocks.</b> <c>startDate</c> and <c>flags</c> belong to <see cref="ModSave"/>, <c>random</c> to <see cref="ModRandom"/>;
    /// <c>newGameOptions</c> (ModNewGame), <c>modifiers</c>, <c>reputation</c> and <c>mail</c> come from the packages that own them and register in
    /// their own start methods. The written order is the one above, then any other block in the order of registration, so the file does not
    /// reshuffle between saves.</item>
    /// <item><b>Unclaimed blocks are kept.</b> A member of the stored section that no registered part owns (a block of a newer build, or of a
    /// service that is not wired yet) is written back unchanged on every save, like the section of an unknown feature (FR-5 item 5). A part that
    /// registers later is handed the block that waited for it.</item>
    /// <item><b>Isolation.</b> A block whose <c>Read</c> throws keeps its stored text for the next save and its part is reset; a <c>Write</c> that
    /// throws leaves the block out, or writes the previous stored text when there is one. The other blocks are not affected. Each failure is
    /// counted by <c>ModHooks.ReportFault</c> under the site <c>save.&lt;key&gt;/Read</c> (a unit of its own, so a broken block cannot switch off
    /// the rest of the save).</item>
    /// </list>
    /// Main thread only; nothing here throws.
    /// </summary>
    internal sealed class CoreSection : IModSaveParticipant
    {
        /// <summary>The section id: the framework's own id.</summary>
        internal const string Id = ModLog.CoreFeatureId;

        /// <summary>The section version this build writes. 1: the first format.</summary>
        internal const int Version = 1;

        // Names of the blocks of design.md ("CoreSectionDto"); the owners of the blocks refer to them instead of retyping the strings.
        internal const string StartDateKey = "startDate";
        internal const string RandomKey = "random";
        internal const string FlagsKey = "flags";
        internal const string NewGameOptionsKey = "newGameOptions";
        internal const string ModifiersKey = "modifiers";
        internal const string ReputationKey = "reputation";
        internal const string MailKey = "mail";

        private static readonly string[] WrittenOrder =
        {
            StartDateKey, RandomKey, FlagsKey, NewGameOptionsKey, ModifiersKey, ReputationKey, MailKey,
        };

        /// <summary>The one section of the core.</summary>
        internal static readonly CoreSection Instance = new CoreSection();

        private readonly List<ICoreSectionPart> _parts = new List<ICoreSectionPart>();
        private readonly Dictionary<string, ICoreSectionPart> _byKey = new Dictionary<string, ICoreSectionPart>(StringComparer.Ordinal);
        private readonly JsonObject _kept = new JsonObject();                                     // blocks held as they were read: unclaimed ones and those whose Read threw
        private readonly HashSet<string> _locked = new HashSet<string>(StringComparer.Ordinal);   // blocks written back as read, never from Write()
        private ICoreSectionPart[] _ordered;                                                      // _parts in written order; null = rebuild
        private bool _hasState;                                                                   // the section was read or reset at least once

        private CoreSection()
        {
        }

        string ISideFileParticipant.SectionId
        {
            get { return Id; }
        }

        int ISideFileParticipant.SchemaVersion
        {
            get { return Version; }
        }

        /// <summary>Keys of the registered blocks, in written order (diagnostics and tests).</summary>
        internal static IReadOnlyList<string> PartKeys
        {
            get
            {
                var keys = new List<string>();
                foreach (ICoreSectionPart part in Instance.Ordered()) keys.Add(part.Key);
                return keys;
            }
        }

        // ---- registration ----

        /// <summary>
        /// Plugs a block into the section. False, with a message in the log, for a null part, an empty or reserved key, or a key that is
        /// taken. A block registered after the section was read is given the stored block that waited for it, or defaults. Never throws.
        /// </summary>
        internal static bool RegisterPart(ICoreSectionPart part)
        {
            return Instance.Add(part);
        }

        /// <summary>
        /// <see cref="RegisterPart(ICoreSectionPart)"/> from three delegates, for a service that has no class for the block: <paramref name="reset"/>
        /// (new game, absent or unreadable block), <paramref name="read"/> (the stored block), <paramref name="write"/> (the block to store, or null).
        /// </summary>
        internal static bool RegisterPart(string key, Action reset, Action<JsonNode> read, Func<JsonNode> write)
        {
            return Instance.Add(new DelegatePart(key, reset, read, write));
        }

        private bool Add(ICoreSectionPart part)
        {
            try
            {
                if (part == null)
                {
                    ModLog.Warn(Id, "save: RegisterPart(null) is ignored");
                    return false;
                }

                string key = part.Key;
                if (string.IsNullOrEmpty(key) || key == SideFileModel.SchemaKey)
                {
                    ModLog.Error(Id, "save: the block of " + part.GetType().Name + " has " + (string.IsNullOrEmpty(key) ? "an empty key" : "the reserved key '" + key + "'")
                                     + " and is not registered");
                    return false;
                }
                if (_byKey.ContainsKey(key))
                {
                    ModLog.Error(Id, "save: the block '" + key + "' of the section " + Id + " is already registered; " + part.GetType().Name + " is ignored");
                    return false;
                }

                _parts.Add(part);
                _byKey[key] = part;
                _ordered = null;

                if (_hasState) Hand(part);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Id, "save: cannot register a block of the section " + Id, ex);
                return false;
            }
        }

        // ---- IModSaveParticipant ----

        void ISideFileParticipant.ResetToDefaults()
        {
            foreach (ICoreSectionPart part in _parts.ToArray()) SafeReset(part);
            _kept.Clear();
            _locked.Clear();
            _hasState = true;
        }

        void ISideFileParticipant.Read(JsonObject section, int schema)
        {
            JsonObject data = CoreSectionMigrations.Apply(section, schema) ?? section;

            _kept.Clear();
            _locked.Clear();
            _hasState = true;

            foreach (ICoreSectionPart part in _parts.ToArray())
            {
                JsonNode block = data[SafeKey(part)];
                if (block == null) SafeReset(part);
                else SafeRead(part, block, schema);
            }

            // Blocks nobody owns right now are not ours to drop.
            foreach (KeyValuePair<string, JsonNode> member in data)
            {
                if (!_byKey.ContainsKey(member.Key)) _kept.Set(member.Key, member.Value);
            }
            if (_kept.Count > 0)
                ModLog.Debug(Id, "save: blocks of " + Id + " without an owner are kept as they are: " + string.Join(", ", new List<string>(_kept.Keys).ToArray()));
        }

        JsonObject ISideFileParticipant.Write()
        {
            var section = new JsonObject();
            foreach (ICoreSectionPart part in Ordered())
            {
                string key = SafeKey(part);
                JsonNode node;
                if (_locked.Contains(key))
                {
                    node = _kept[key];   // its Read failed: the text from the file goes back untouched
                }
                else
                {
                    try
                    {
                        node = part.Write();
                    }
                    catch (Exception ex)
                    {
                        node = _kept[key];
                        Fault(key, "Write", ex);
                        ModLog.Warn(Id, "save: the block '" + key + "' could not be written (" + AtomicFile.Describe(ex) + "); "
                                        + (node == null ? "it is left out" : "the previous text is kept"));
                    }
                }
                if (node != null && !node.IsNull) section.Set(key, node);
            }

            // Unclaimed blocks go last, in the order they had.
            foreach (KeyValuePair<string, JsonNode> member in _kept)
            {
                if (!_byKey.ContainsKey(member.Key) && !section.ContainsKey(member.Key)) section.Set(member.Key, member.Value);
            }
            return section;
        }

        // ---- internals ----

        /// <summary>The parts in the order of the file: the blocks of design.md first, the rest as they registered.</summary>
        private ICoreSectionPart[] Ordered()
        {
            ICoreSectionPart[] ordered = _ordered;
            if (ordered != null) return ordered;

            var indexed = new List<KeyValuePair<int, ICoreSectionPart>>();
            for (int i = 0; i < _parts.Count; i++)
            {
                int rank = Array.IndexOf(WrittenOrder, SafeKey(_parts[i]));
                indexed.Add(new KeyValuePair<int, ICoreSectionPart>((rank < 0 ? WrittenOrder.Length : rank) * 100000 + i, _parts[i]));
            }
            indexed.Sort((a, b) => a.Key.CompareTo(b.Key));

            ordered = new ICoreSectionPart[indexed.Count];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = indexed[i].Value;
            _ordered = ordered;
            return ordered;
        }

        /// <summary>A block registered after the section was read: the stored block that waited for it, or defaults.</summary>
        private void Hand(ICoreSectionPart part)
        {
            string key = SafeKey(part);
            JsonNode waiting = _kept[key];
            if (waiting == null)
            {
                SafeReset(part);
                return;
            }

            _kept.Remove(key);
            SafeRead(part, waiting, Version);
        }

        private void SafeReset(ICoreSectionPart part)
        {
            try
            {
                part.ResetToDefaults();
            }
            catch (Exception ex)
            {
                Fault(SafeKey(part), "ResetToDefaults", ex);
                ModLog.Warn(Id, "save: the block '" + SafeKey(part) + "' could not be reset (" + AtomicFile.Describe(ex) + ")");
            }
        }

        private void SafeRead(ICoreSectionPart part, JsonNode block, int schema)
        {
            string key = SafeKey(part);
            try
            {
                part.Read(block, schema);
                _kept.Remove(key);
                _locked.Remove(key);
            }
            catch (Exception ex)
            {
                Fault(key, "Read", ex);
                ModLog.Warn(Id, "save: the block '" + key + "' could not be read (" + AtomicFile.Describe(ex) + "); defaults are used and the stored text is kept as it is");
                _kept.Set(key, block);
                _locked.Add(key);
                SafeReset(part);
            }
        }

        private static void Fault(string key, string what, Exception ex)
        {
            ModHooks.ReportFault(Id, "save." + key + "/" + what, ex);
        }

        private static string SafeKey(ICoreSectionPart part)
        {
            try
            {
                return part.Key;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>A block made of delegates (<see cref="RegisterPart(string, Action, Action{JsonNode}, Func{JsonNode})"/>).</summary>
        private sealed class DelegatePart : ICoreSectionPart
        {
            private readonly Action _reset;
            private readonly Action<JsonNode> _read;
            private readonly Func<JsonNode> _write;

            public DelegatePart(string key, Action reset, Action<JsonNode> read, Func<JsonNode> write)
            {
                Key = key;
                _reset = reset;
                _read = read;
                _write = write;
            }

            public string Key { get; }

            public void ResetToDefaults()
            {
                if (_reset != null) _reset();
            }

            public void Read(JsonNode block, int schema)
            {
                if (_read != null) _read(block);
            }

            public JsonNode Write()
            {
                return _write == null ? null : _write();
            }
        }
    }
}
