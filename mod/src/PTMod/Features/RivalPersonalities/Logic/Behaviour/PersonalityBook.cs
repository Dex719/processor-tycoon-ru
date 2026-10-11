using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What <see cref="PersonalityBook.EnsureAssigned"/> found or did.</summary>
    public enum AssignOutcome
    {
        /// <summary>The company had its archetype already (saved in the section); nothing changed.</summary>
        Existing = 0,

        /// <summary>The archetype of <c>assignments.json</c> was given and saved.</summary>
        Assigned = 1,

        /// <summary>The company has no record in <c>assignments.json</c> (or its archetype is not in the catalog): the fallback was given and saved, with a warning.</summary>
        AssignedFallback = 2,

        /// <summary>The catalog has no archetype at all: nothing was given.</summary>
        NoArchetype = 3,
    }

    /// <summary>
    /// The characters of the companies in the section of the side save file (FR-2 items 2-3 and 6, FR-4 item 1, FR-6 item 2): which archetype each
    /// <c>UniqueID</c> has, its starting attitude and the start of the grace period. The pure half of <c>PersonalityRegistry</c>: it works on the
    /// state the provider gives (re-read at every call, because a load replaces the object) and the catalog of the content.
    /// <list type="bullet">
    /// <item>The first time a company is seen it gets the archetype of <c>assignments.json</c> (<see cref="ArchetypeCatalog.DefaultArchetypeFor"/>),
    /// which is saved and used from then on; a company without a record there gets <c>conservative</c> and a warning (FR-2 item 3).</item>
    /// <item>A new character starts at the baseline attitude of its archetype (FR-4 item 1).</item>
    /// <item>A saved archetype that the catalog no longer has (an override removed it) stays saved; the company behaves as the fallback until the
    /// content has it again (one warning per company).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class PersonalityBook
    {
        private readonly Func<RivalState> _state;
        private readonly Func<ArchetypeCatalog> _catalog;
        private readonly Action<string> _warn;
        private readonly HashSet<int> _warnedMissing = new HashSet<int>();

        /// <param name="state">The section as it is now (never keep the object: a load replaces it); null gives an empty state.</param>
        /// <param name="catalog">The archetypes and the numbers; null gives an empty catalog.</param>
        /// <param name="warn">One English line per problem (the feature logs it); may be null.</param>
        public PersonalityBook(Func<RivalState> state, Func<ArchetypeCatalog> catalog, Action<string> warn = null)
        {
            _state = state;
            _catalog = catalog;
            _warn = warn;
        }

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        private ArchetypeCatalog Catalog
        {
            get
            {
                ArchetypeCatalog catalog = _catalog != null ? _catalog() : null;
                return catalog ?? ArchetypeCatalog.Empty;
            }
        }

        // ---- the archetypes ----

        /// <summary>The id of the archetype saved for the company, or null when it has none yet.</summary>
        public string ArchetypeIdOf(int uniqueId)
        {
            AssignmentEntry entry = Find(State.assignments, uniqueId);
            return entry != null && !string.IsNullOrEmpty(entry.archetype) ? entry.archetype : null;
        }

        /// <summary>
        /// The archetype that is in effect for the company: the saved one; the fallback of the catalog when the catalog does not have the saved
        /// one (one warning per company); null when the company has no saved archetype or the catalog is empty.
        /// </summary>
        public Archetype ArchetypeOf(int uniqueId)
        {
            string id = ArchetypeIdOf(uniqueId);
            if (id == null) return null;

            ArchetypeCatalog catalog = Catalog;
            Archetype archetype;
            if (catalog.TryGet(id, out archetype)) return archetype;

            string fallback = catalog.DefaultArchetypeFor(-1);
            if (!catalog.TryGet(fallback, out archetype)) return null;
            if (_warnedMissing.Add(uniqueId))
            {
                Warn("the saved archetype '" + id + "' of UniqueID " + Text(uniqueId) + " is not in the catalog; it behaves as '" + archetype.id + "'");
            }
            return archetype;
        }

        /// <summary>
        /// Gives the company its archetype when it has none (see the class) and saves it, with the baseline attitude when the company has no attitude
        /// yet. Idempotent: a company with a saved archetype keeps it.
        /// </summary>
        public AssignOutcome EnsureAssigned(int uniqueId)
        {
            RivalState state = State;
            if (Find(state.assignments, uniqueId) is AssignmentEntry existing && !string.IsNullOrEmpty(existing.archetype))
            {
                return AssignOutcome.Existing;
            }

            ArchetypeCatalog catalog = Catalog;
            string id = catalog.DefaultArchetypeFor(uniqueId);
            Archetype archetype;
            if (!catalog.TryGet(id, out archetype)) return AssignOutcome.NoArchetype;

            AssignmentRecord record = catalog.AssignmentFor(uniqueId);
            AssignOutcome outcome = AssignOutcome.Assigned;
            if (record == null)
            {
                Warn("UniqueID " + Text(uniqueId) + " has no record in assignments.json; it gets '" + id + "'");
                outcome = AssignOutcome.AssignedFallback;
            }
            else if (!string.Equals(record.archetype, id, StringComparison.Ordinal))
            {
                Warn("the archetype '" + (record.archetype ?? "") + "' of UniqueID " + Text(uniqueId) + " is not in the catalog; it gets '" + id + "'");
                outcome = AssignOutcome.AssignedFallback;
            }

            Write(state, uniqueId, archetype);
            return outcome;
        }

        /// <summary>
        /// Gives the company the archetype <paramref name="archetypeId"/> (the random mode of TSK-021 decides it, not <c>assignments.json</c>) and saves
        /// it, with the baseline attitude when the company has none yet. Only a company that has no saved archetype takes it: a character is never
        /// re-dealt (FR-11 item 4). False when the company has an archetype already, when the id is empty or not in the catalog, or when the company is
        /// not a company (a negative id).
        /// </summary>
        public bool Assign(int uniqueId, string archetypeId)
        {
            if (uniqueId < 0 || string.IsNullOrEmpty(archetypeId)) return false;

            RivalState state = State;
            AssignmentEntry existing = Find(state.assignments, uniqueId);
            if (existing != null && !string.IsNullOrEmpty(existing.archetype)) return false;

            Archetype archetype;
            if (!Catalog.TryGet(archetypeId, out archetype)) return false;

            Write(state, uniqueId, archetype);
            return true;
        }

        /// <summary>
        /// The random mode (TSK-021): deals the archetypes of the catalog among the companies that have no character yet (<see cref="RandomAssignment.Plan"/>)
        /// and saves them. Returns what was dealt, in the order of the <c>UniqueID</c>. Companies that already have a character are left alone and not
        /// counted.
        /// </summary>
        public List<AssignmentEntry> DealRandom(IList<AssignmentCandidate> companies, IModRng rng)
        {
            var dealt = new List<AssignmentEntry>();
            if (companies == null) return dealt;

            var open = new List<AssignmentCandidate>();
            foreach (AssignmentCandidate company in companies)
            {
                if (ArchetypeIdOf(company.UniqueId) == null) open.Add(company);
            }

            var ids = new List<string>();
            foreach (Archetype archetype in Catalog.All) ids.Add(archetype.id);

            foreach (AssignmentEntry entry in RandomAssignment.Plan(open, ids, rng))
            {
                if (Assign(entry.uid, entry.archetype)) dealt.Add(entry);
            }
            return dealt;
        }

        /// <summary>The <c>UniqueID</c> of every company that has a saved archetype, in the order of the section. A new list.</summary>
        public List<int> AssignedUids()
        {
            var uids = new List<int>();
            List<AssignmentEntry> list = State.assignments;
            if (list == null) return uids;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && !string.IsNullOrEmpty(list[i].archetype) && !uids.Contains(list[i].uid)) uids.Add(list[i].uid);
            }
            return uids;
        }

        // ---- the attitude and the grace period ----

        /// <summary>The saved attitude of the company, or null when it has none (TSK-010 owns the scale; this is only the start value).</summary>
        public float? AttitudeOf(int uniqueId)
        {
            AttitudeRecord record = FindAttitude(State, uniqueId);
            if (record == null || float.IsNaN(record.value)) return null;
            return record.value;
        }

        /// <summary>
        /// Sets the attitude of the company to the baseline of its archetype (FR-2 item 6: the start of a new game). False when the company has no
        /// archetype in effect.
        /// </summary>
        public bool SetAttitudeToBaseline(int uniqueId)
        {
            Archetype archetype = ArchetypeOf(uniqueId);
            if (archetype == null) return false;
            SetAttitude(State, uniqueId, archetype.attitude != null ? archetype.attitude.baseline : 0f);
            return true;
        }

        /// <summary>
        /// Starts the grace period (FR-6 item 2) on <paramref name="today"/>: always when <paramref name="restart"/> (a new game), otherwise only when
        /// it has not started yet (a save without the section starts it on the day of the load, FR-10 item 3). False for an unknown date and when
        /// nothing changed.
        /// </summary>
        public bool StartGrace(DateTime today, bool restart)
        {
            if (today == default(DateTime)) return false;

            RivalState state = State;
            if (!restart && !string.IsNullOrEmpty(state.graceStart)) return false;

            string text = RivalDates.ToText(today);
            if (string.Equals(state.graceStart, text, StringComparison.Ordinal)) return false;
            state.graceStart = text;
            return true;
        }

        /// <summary>The company is put aside (<c>SetDormant</c>, FR-13 item 3): no character, no price cut, no trigger.</summary>
        public bool IsDormant(int uniqueId)
        {
            List<int> dormant = State.dormant;
            return dormant != null && dormant.Contains(uniqueId);
        }

        /// <summary>
        /// Puts the company aside or brings it back (the record <see cref="RivalState.dormant"/>; the effects on the game are the caller's). True when
        /// the record changed, false when the company was in that state already or is not a company (a negative id).
        /// </summary>
        public bool SetDormant(int uniqueId, bool dormant)
        {
            if (uniqueId < 0) return false;

            RivalState state = State;
            if (state.dormant == null) state.dormant = new List<int>();
            bool present = state.dormant.Contains(uniqueId);
            if (dormant == present) return false;

            if (dormant) state.dormant.Add(uniqueId);
            else state.dormant.RemoveAll(item => item == uniqueId);
            return true;
        }

        /// <summary>
        /// The character of the company is shown to the player: it has started a reaction (<see cref="RivalState.revealed"/>, FR-11 item 3). This is
        /// the record only; whether the character is visible now also depends on the mode and the attitude (<see cref="CharacterVisibility"/>).
        /// </summary>
        public bool IsRevealed(int uniqueId)
        {
            List<int> revealed = State.revealed;
            return revealed != null && revealed.Contains(uniqueId);
        }

        /// <summary>
        /// Writes the company into <see cref="RivalState.revealed"/> (TSK-028; the first reaction of a rival starts it). True the first time, false
        /// when it was revealed already or when the company is not a company (a negative id): the caller shows the notification on true only, so
        /// it is shown once. The record stays when the mode "hidden characters" is switched off and on again.
        /// </summary>
        public bool Reveal(int uniqueId)
        {
            if (uniqueId < 0) return false;

            RivalState state = State;
            if (state.revealed == null) state.revealed = new List<int>();
            if (state.revealed.Contains(uniqueId)) return false;
            state.revealed.Add(uniqueId);
            return true;
        }

        /// <summary>Forgets which companies were warned about (a new scene).</summary>
        public void ResetWarnings()
        {
            _warnedMissing.Clear();
        }

        // ---- internals ----

        private static void Write(RivalState state, int uniqueId, Archetype archetype)
        {
            if (state.assignments == null) state.assignments = new List<AssignmentEntry>();
            AssignmentEntry entry = Find(state.assignments, uniqueId);
            if (entry == null)
            {
                entry = new AssignmentEntry { uid = uniqueId };
                state.assignments.Add(entry);
            }
            entry.archetype = archetype.id;

            AttitudeRecord attitude = FindAttitude(state, uniqueId);
            if (attitude == null || float.IsNaN(attitude.value)) SetAttitude(state, uniqueId, archetype.attitude != null ? archetype.attitude.baseline : 0f);
        }

        private static AssignmentEntry Find(List<AssignmentEntry> list, int uniqueId)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].uid == uniqueId) return list[i];
            }
            return null;
        }

        private static AttitudeRecord FindAttitude(RivalState state, int uniqueId)
        {
            List<AttitudeRecord> list = state.attitudes;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].uid == uniqueId) return list[i];
            }
            return null;
        }

        private static void SetAttitude(RivalState state, int uniqueId, float value)
        {
            if (state.attitudes == null) state.attitudes = new List<AttitudeRecord>();
            AttitudeRecord record = FindAttitude(state, uniqueId);
            if (record == null)
            {
                record = new AttitudeRecord { uid = uniqueId };
                state.attitudes.Add(record);
            }
            record.value = AttitudeMath.Clamp(value);
        }

        private void Warn(string line)
        {
            if (_warn != null) _warn(line);
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
