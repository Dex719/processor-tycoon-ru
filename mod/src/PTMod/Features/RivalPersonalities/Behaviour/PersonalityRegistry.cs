using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameController = ProcessorTycoon.AISystem.AIBehaviourController;
using GameCreation = ProcessorTycoon.AISystem.AIBehaviourCreation;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GameDesignFocus = ProcessorTycoon.AISystem.CpuDesignFocus;
using GamePowerFocus = ProcessorTycoon.AISystem.PowerConsumptionFocus;
using GameSimulation = ProcessorTycoon.AISystem.SimulationPriority;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The characters of the AI companies in the game (design.md, Component 3; FR-2, FR-3 items 4-7): which archetype each company has, the snapshot
    /// of the priorities of its designers, applying the character and reverting it.
    /// <list type="bullet">
    /// <item><b>When</b> (<see cref="PersonalityLifecycle"/>): nothing during the fast-forward of a new game; at <c>TickPhase.FastForwardEnded</c> every
    /// existing company once (assignment, baseline attitude, start of the grace period); a company initialised while a save loads waits for
    /// <c>ModSave.AfterRead</c>, which applies every company again (the game reads the AI's configuration from the prefab at every load); a company
    /// that appears in a running game at once; Cyrix again after <c>AIBehaviourController.ReformCyrux</c>.</item>
    /// <item><b>Designers.</b> The private list <c>AIBehaviourController.creators</c> (reflection; it has the designer that <c>ReformCyrux</c> adds,
    /// <c>behaviourCreations</c> does not) gives the designers; their private <c>designFocus</c>, <c>powerConsumptionFocus</c> and
    /// <c>simulationPriority</c> are the snapshot (<see cref="CreatorRecord"/>). <c>ChangePriorities</c> is called only when the archetype's values
    /// differ from the current ones; a designer whose own focus is Inlet or EMD keeps its design focus; a foundry keeps its designers.</item>
    /// <item><b>Multipliers.</b> Applying the character puts its numbers into the <see cref="BehaviourModel"/> the patches read
    /// (<see cref="BehaviourModifiers"/>); before that, and after <see cref="Revert"/>, they are 1.</item>
    /// <item><b>Log.</b> One line <c>applied &lt;archetype&gt; to &lt;company&gt;</c> per company and application (never during the fast-forward).</item>
    /// </list>
    /// The snapshot is a runtime cache: a new scene starts without it. Main thread only; the callers guard the calls (patch bodies, handlers).
    /// </summary>
    internal sealed class PersonalityRegistry
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo CreatorsField = typeof(GameController).GetField("creators", AnyInstance);
        private static readonly FieldInfo DesignFocusField = typeof(GameCreation).GetField("designFocus", AnyInstance);
        private static readonly FieldInfo PowerFocusField = typeof(GameCreation).GetField("powerConsumptionFocus", AnyInstance);
        private static readonly FieldInfo SimulationField = typeof(GameCreation).GetField("simulationPriority", AnyInstance);

        private readonly PersonalityBook _book;
        private readonly BehaviourModel _model;
        private readonly PersonalityLifecycle _lifecycle = new PersonalityLifecycle();
        private readonly Dictionary<GameCreation, CreatorRecord> _creators = new Dictionary<GameCreation, CreatorRecord>();
        private readonly Dictionary<int, AICompany> _companies = new Dictionary<int, AICompany>();
        private readonly List<GameCreation> _scratch = new List<GameCreation>();

        private PropertyInfo _cpuCreatorProperty;   // AIBehaviourController+Creator.CpuCreator (a private nested class)
        private bool _reflectionWarned;

        public PersonalityRegistry(PersonalityBook book, BehaviourModel model)
        {
            _book = book;
            _model = model;
        }

        /// <summary>The moments of the scene (for the tests of the order and the diagnostics).</summary>
        public PersonalityLifecycle Lifecycle
        {
            get { return _lifecycle; }
        }

        // ---- reading ----

        /// <summary>
        /// The live AI companies by <c>UniqueID</c>, for the triggers and the monthly pass (TSK-011, TSK-012). The list of the core is used; only when it
        /// has no AI company (the game is not ready) the scene is scanned. A new list at every call.
        /// </summary>
        public List<AICompany> Rivals()
        {
            var result = new List<AICompany>();
            IReadOnlyList<ICompany> known = ModCompanies.All;
            for (int i = 0; i < known.Count; i++) AddAi(result, known[i] as AICompany);
            if (result.Count == 0) return AiCompanies();

            result.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
            return result;
        }

        /// <summary>The live AI company with this <c>UniqueID</c> (the core's list, then the scene), or null.</summary>
        public AICompany FindRival(int uniqueId)
        {
            string key = CompanyKey.ForAi(uniqueId);
            var company = key != null ? ModCompanies.Resolve(key) as AICompany : null;
            if (!ReferenceEquals(company, null) && company != null) return company;

            foreach (AICompany rival in Rivals())
            {
                if (rival.UniqueID == uniqueId) return rival;
            }
            return null;
        }

        /// <summary>The id of the archetype saved for the company, or null.</summary>
        public string GetArchetypeId(int uniqueId)
        {
            return _book.ArchetypeIdOf(uniqueId);
        }

        /// <summary>The character of the company is in effect (the patches return the vanilla values until then).</summary>
        public bool IsApplied(int uniqueId)
        {
            return _model.IsApplied(uniqueId);
        }

        // ---- the visibility of the characters (TSK-028; FR-11 items 2-4) ----

        /// <summary>
        /// A character was revealed for the first time: <c>UniqueID</c> of the rival. Raised after the record and the notification; the feature writes the
        /// line <c>revealed</c> to the history. A handler that throws is the caller's to guard (<c>Reveal</c> is called from a guarded handler).
        /// </summary>
        public event Action<int> Revealed;

        /// <summary>
        /// The character of the rival is shown to the player: its <c>UniqueID</c> is in <c>revealed</c> of the section (the rival has started a reaction).
        /// This is the record; whether the character is visible now also depends on the mode and the attitude (<see cref="CharacterVisibility"/>).
        /// </summary>
        public bool IsRevealed(int uniqueId)
        {
            return _book.IsRevealed(uniqueId);
        }

        /// <summary>
        /// The first reaction of a rival started (<c>ReactionExecutor.OnStarted</c>): saves the <c>UniqueID</c> in <c>revealed</c> (no duplicates) and, while
        /// the mode "hidden characters" is on, shows the notification "&lt;company&gt;: character revealed - &lt;archetype&gt;" once. True the first
        /// time, false when the rival was revealed before. Switching the mode off does not clear the record, switching it on does not hide a revealed rival.
        /// </summary>
        public bool Reveal(int uniqueId)
        {
            if (!_book.Reveal(uniqueId)) return false;

            Archetype archetype = _book.ArchetypeOf(uniqueId);
            string archetypeName = archetype != null ? ModL10n.Resolve(archetype.Name) : string.Empty;
            string name = AttitudeNotifier.NameOf(uniqueId);
            ModLog.Info(Feature, "character of " + name + " (ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) + ") revealed"
                                 + (archetype != null ? ": " + archetype.id : string.Empty));
            if (RivalSettings.HiddenCharacters) ModNotify.ShowKey(Feature, "rival.notify.revealed", name, archetypeName);

            Action<int> handlers = Revealed;
            if (handlers != null) handlers(uniqueId);
            return true;
        }

        // ---- the moments of the game ----

        /// <summary>Postfix of <c>AICompany.Initialize(int)</c>: every way a company appears (a new game, a spawn in a running game, a load).</summary>
        public void OnCompanyInitialized(AICompany company)
        {
            if (ReferenceEquals(company, null)) return;
            SpawnDecision decision = _lifecycle.OnSpawn(ModTicks.IsFastForwarding, ModTicks.IsLoading);
            if (decision == SpawnDecision.Apply)
            {
                EnsureAssigned(company);
                Apply(company, "spawn");
            }
            else if (decision == SpawnDecision.DeferLoading)
            {
                ModLog.Debug(Feature, "character of " + Describe(company) + " waits for the section of the save (ModSave.AfterRead)");
            }
        }

        /// <summary>Postfix of <c>AIBehaviourController.ReformCyrux(bool)</c>: the reform rewrote the designers of Cyrix, the character goes on top again.</summary>
        public void OnCyrixReformed(AICompany company)
        {
            if (ReferenceEquals(company, null)) return;
            if (_lifecycle.OnSpawn(ModTicks.IsFastForwarding, ModTicks.IsLoading) != SpawnDecision.Apply) return;
            EnsureAssigned(company);
            Apply(company, "after ReformCyrux");
        }

        /// <summary>
        /// <c>TickPhase.FastForwardEnded</c> (D10): once per scene, every existing AI company gets its archetype and its character, its attitude is set to
        /// the baseline and the grace period starts today.
        /// </summary>
        public void OnFastForwardEnded()
        {
            if (!_lifecycle.BeginNewGame()) return;
            StartNewGame();
        }

        /// <summary><c>ModSave.AfterRead</c>: the section of the load is there; every existing company gets its character (the configuration is the prefab's again).</summary>
        public void OnAfterRead()
        {
            ClearRuntime();
            _lifecycle.StateRead();
            _book.StartGrace(ModTicks.Today, false);
            ApplyAll("load");
        }

        /// <summary><c>ModTicks.GameReady</c>: the safety net when the fast-forward end or the reading of the section did not come.</summary>
        public void OnGameReady(GameReadyReason reason)
        {
            ReadyAction action = _lifecycle.GameReady(reason == GameReadyReason.NewGame);
            if (action == ReadyAction.StartNewGame)
            {
                ModLog.Warn(Feature, "the game is ready without TickPhase.FastForwardEnded; the characters are given now");
                StartNewGame();
            }
            else if (action == ReadyAction.ApplyAll)
            {
                ModLog.Warn(Feature, "the game is ready without ModSave.AfterRead; the characters are applied now");
                ApplyAll("game ready");
            }
            _book.StartGrace(ModTicks.Today, false);
        }

        /// <summary>The scene goes: the objects of the game go with it, so does the runtime cache.</summary>
        public void OnSceneLeaving()
        {
            _lifecycle.Reset();
            ClearRuntime();
            _book.ResetWarnings();
        }

        /// <summary>The feature was switched on: in a ready game every existing company gets its character at once (a save without the section starts the grace period today).</summary>
        public void OnEnabled(bool gameReady)
        {
            ClearRuntime();
            if (!_lifecycle.EnabledInGame(gameReady)) return;
            _book.StartGrace(ModTicks.Today, false);
            ApplyAll("feature on");
        }

        /// <summary>The feature was switched off (FR-3 item 6): every designer gets its snapshot back and the multipliers stop.</summary>
        public void OnDisabled()
        {
            int reverted = RevertAll();
            _lifecycle.Reset();
            ModLog.Info(Feature, "OnDisabled: reverted the character of " + reverted.ToString(CultureInfo.InvariantCulture) + " AI company(ies)");
        }

        // ---- assigning and applying ----

        /// <summary>Gives the company its archetype the first time it is seen (FR-2 items 1-3). Nothing during the fast-forward.</summary>
        public void EnsureAssigned(AICompany company)
        {
            if (ModTicks.IsFastForwarding || ReferenceEquals(company, null) || company == null || company.UniqueID < 0) return;

            AssignOutcome outcome = _book.EnsureAssigned(company.UniqueID);
            if (outcome == AssignOutcome.Assigned || outcome == AssignOutcome.AssignedFallback)
            {
                ModLog.Debug(Feature, "assigned " + _book.ArchetypeIdOf(company.UniqueID) + " to " + Describe(company));
            }
            else if (outcome == AssignOutcome.NoArchetype)
            {
                ModLog.Warn(Feature, "no archetype for " + Describe(company) + ": the catalog is empty");
            }
        }

        /// <summary>
        /// Applies the character of the company (idempotent): the designers' priorities when they differ, the multipliers of the patches. Nothing during
        /// the fast-forward, for a bankrupt or dormant company, or for one without an archetype. True when the character is in effect after the call.
        /// </summary>
        public bool Apply(AICompany company, string moment)
        {
            if (ModTicks.IsFastForwarding || ReferenceEquals(company, null) || company == null) return false;
            int uniqueId = company.UniqueID;
            if (uniqueId < 0 || company.IsBankrupt) return false;
            if (_book.IsDormant(uniqueId))
            {
                if (_model.IsApplied(uniqueId)) Revert(company);
                return false;
            }

            Archetype archetype = _book.ArchetypeOf(uniqueId);
            if (archetype == null) return false;

            bool foundry = company.IsFoundry;
            int changed = foundry ? 0 : ApplyDesigners(company, archetype.behaviour);
            _model.SetApplied(uniqueId, archetype.behaviour);
            _companies[uniqueId] = company;

            string detail = foundry ? "; foundry: attitude and negotiations only"
                : changed > 0 ? "; " + changed.ToString(CultureInfo.InvariantCulture) + " designer(s) re-prioritised" : string.Empty;
            ModLog.Info(Feature, "applied " + archetype.id + " to " + Describe(company) + " (" + (moment ?? "?") + detail + ")");
            return true;
        }

        /// <summary>Applies the characters of every existing AI company (assigning those that have none). Returns how many are in effect.</summary>
        public int ApplyAll(string moment)
        {
            if (ModTicks.IsFastForwarding) return 0;
            int applied = 0;
            foreach (AICompany company in AiCompanies())
            {
                EnsureAssigned(company);
                if (Apply(company, moment)) applied++;
            }
            ModLog.Debug(Feature, "characters in effect for " + applied.ToString(CultureInfo.InvariantCulture) + " AI company(ies) (" + moment + ")");
            return applied;
        }

        /// <summary>
        /// Takes the character off the company (FR-3 item 6, FR-13 item 3): every designer that still has the priorities the character set gets its
        /// snapshot back, and the multipliers become 1.
        /// </summary>
        public void Revert(AICompany company)
        {
            if (ReferenceEquals(company, null)) return;
            int uniqueId = company.UniqueID;
            _model.ClearApplied(uniqueId);
            _companies.Remove(uniqueId);
            if (company == null) return;   // destroyed with its scene: nothing to give back

            int reverted = 0;
            if (CollectCreators(company, _scratch))
            {
                for (int i = 0; i < _scratch.Count; i++)
                {
                    GameCreation creator = _scratch[i];
                    CreatorRecord record;
                    if (!_creators.TryGetValue(creator, out record)) continue;
                    _creators.Remove(creator);

                    CreatorPriorities target;
                    if (record.PlanRevert(Read(creator), out target))
                    {
                        Change(creator, target);
                        reverted++;
                    }
                    record.MarkReverted();
                }
            }
            _scratch.Clear();
            ModLog.Debug(Feature, "reverted the character of " + Describe(company) + (reverted > 0 ? " (" + reverted.ToString(CultureInfo.InvariantCulture) + " designer(s))" : string.Empty));
        }

        /// <summary>
        /// Takes the character off the company that was given by its <c>UniqueID</c> (<c>SetDormant</c>): the company this registry applied it to, or only
        /// the multipliers when it did not keep the company (a new scene).
        /// </summary>
        public void Revert(int uniqueId)
        {
            AICompany company;
            if (_companies.TryGetValue(uniqueId, out company) && !ReferenceEquals(company, null))
            {
                Revert(company);
                return;
            }
            _model.ClearApplied(uniqueId);
            _companies.Remove(uniqueId);
        }

        /// <summary>Takes every character off (the feature is switched off). Returns how many companies had one in effect. A failure with one company does not stop the others.</summary>
        public int RevertAll()
        {
            var companies = new List<AICompany>(_companies.Values);
            int reverted = 0;
            foreach (AICompany company in companies)
            {
                try
                {
                    Revert(company);
                    reverted++;
                }
                catch (Exception e)
                {
                    ModHooks.ReportFault(Feature, "PersonalityRegistry.RevertAll", e);
                }
            }
            ClearRuntime();
            return reverted;
        }

        // ---- internals ----

        private void StartNewGame()
        {
            DateTime today = ModTicks.Today;
            _book.StartGrace(today, true);

            int applied = 0;
            List<AICompany> companies = AiCompanies();
            DealRandomCharacters(companies);
            foreach (AICompany company in companies)
            {
                EnsureAssigned(company);
                _book.SetAttitudeToBaseline(company.UniqueID);
                if (Apply(company, "new game")) applied++;
            }
            ModLog.Info(Feature, "new game: characters given to " + applied.ToString(CultureInfo.InvariantCulture) + " of "
                                 + companies.Count.ToString(CultureInfo.InvariantCulture) + " AI company(ies), grace period from " + RivalDates.ToText(today));
        }

        /// <summary>
        /// The random mode (FR-11 item 1, TSK-021): when the player switched "Random characters" on, the archetypes are dealt now, at the start of the
        /// new game, from the stream <see cref="RandomAssignment.StreamId"/>; <c>EnsureAssigned</c> then keeps what was dealt. Later a change of the mode
        /// deals nothing again.
        /// </summary>
        private void DealRandomCharacters(List<AICompany> companies)
        {
            if (!RivalSettings.RandomCharacters || companies.Count == 0) return;

            var candidates = new List<AssignmentCandidate>(companies.Count);
            foreach (AICompany company in companies) candidates.Add(new AssignmentCandidate(company.UniqueID, company.IsFoundry));

            List<AssignmentEntry> dealt = _book.DealRandom(candidates, ModRandom.Stream(RandomAssignment.StreamId));
            var line = new System.Text.StringBuilder("random characters dealt to " + dealt.Count.ToString(CultureInfo.InvariantCulture) + " company(ies):");
            foreach (AssignmentEntry entry in dealt) line.Append(" ai:").Append(entry.uid.ToString(CultureInfo.InvariantCulture)).Append('=').Append(entry.archetype);
            ModLog.Info(Feature, line.ToString());
        }

        private void ClearRuntime()
        {
            _model.ClearAllApplied();
            _creators.Clear();
            _companies.Clear();
            _scratch.Clear();
        }

        /// <summary>The designers get the priorities of the archetype where they differ; returns how many were changed.</summary>
        private int ApplyDesigners(AICompany company, ArchetypeBehaviour behaviour)
        {
            if (behaviour == null) return 0;
            int changed = 0;
            if (!CollectCreators(company, _scratch)) return 0;
            try
            {
                for (int i = 0; i < _scratch.Count; i++)
                {
                    GameCreation creator = _scratch[i];
                    CreatorRecord record;
                    if (!_creators.TryGetValue(creator, out record))
                    {
                        record = new CreatorRecord();
                        _creators[creator] = record;
                    }

                    CreatorPriorities target;
                    if (record.Plan(Read(creator), behaviour.designFocusOverride, behaviour.powerFocusOverride, out target))
                    {
                        Change(creator, target);
                        changed++;
                    }
                    record.MarkApplied(target);
                }
            }
            finally
            {
                _scratch.Clear();
            }
            return changed;
        }

        /// <summary>The designers of the company from <c>AIBehaviourController.creators</c>. False (with one warning per session) when the game does not have them as expected.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool CollectCreators(AICompany company, List<GameCreation> into)
        {
            into.Clear();
            if (CreatorsField == null || DesignFocusField == null || PowerFocusField == null || SimulationField == null)
            {
                WarnReflection("a private member of AIBehaviourController/AIBehaviourCreation is missing");
                return false;
            }

            GameController controller = company.GetController();
            if (controller == null) return false;

            var creators = CreatorsField.GetValue(controller) as IList;
            if (creators == null) return false;

            for (int i = 0; i < creators.Count; i++)
            {
                object item = creators[i];
                if (item == null) continue;
                if (_cpuCreatorProperty == null || _cpuCreatorProperty.DeclaringType != item.GetType())
                {
                    _cpuCreatorProperty = item.GetType().GetProperty("CpuCreator", AnyInstance);
                    if (_cpuCreatorProperty == null)
                    {
                        WarnReflection("AIBehaviourController.Creator.CpuCreator is missing");
                        into.Clear();
                        return false;
                    }
                }

                var creator = _cpuCreatorProperty.GetValue(item, null) as GameCreation;
                if (creator != null) into.Add(creator);
            }
            return true;
        }

        private static CreatorPriorities Read(GameCreation creator)
        {
            return new CreatorPriorities(
                Convert.ToInt32(DesignFocusField.GetValue(creator), CultureInfo.InvariantCulture),
                Convert.ToInt32(PowerFocusField.GetValue(creator), CultureInfo.InvariantCulture),
                Convert.ToInt32(SimulationField.GetValue(creator), CultureInfo.InvariantCulture));
        }

        private static void Change(GameCreation creator, CreatorPriorities target)
        {
            creator.ChangePriorities((GameDesignFocus)target.DesignFocus, (GamePowerFocus)target.PowerFocus, (GameSimulation)target.SimulationPriority);
        }

        private void WarnReflection(string what)
        {
            if (_reflectionWarned) return;
            _reflectionWarned = true;
            ModLog.Warn(Feature, "designers: " + what + " (another game version?); the characters change no designer, the other effects stay");
        }

        /// <summary>The live AI companies: the core's list and a scan of the scene (the core's list is empty until the game is ready), by <c>UniqueID</c>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<AICompany> AiCompanies()
        {
            var result = new List<AICompany>();
            IReadOnlyList<ICompany> known = ModCompanies.All;
            for (int i = 0; i < known.Count; i++) AddAi(result, known[i] as AICompany);

            try
            {
                List<ICompany> found = GameDataFinder.FindAllAICompanies(true);
                if (found != null)
                {
                    for (int i = 0; i < found.Count; i++) AddAi(result, found[i] as AICompany);
                }
            }
            catch (Exception e)
            {
                ModLog.Warn(Feature, "could not scan the scene for AI companies (" + e.GetType().Name + ": " + e.Message + "); the core's list is used");
            }

            result.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
            return result;
        }

        private static void AddAi(List<AICompany> into, AICompany company)
        {
            if (ReferenceEquals(company, null) || company == null || company.UniqueID < 0) return;
            for (int i = 0; i < into.Count; i++)
            {
                if (ReferenceEquals(into[i], company)) return;
            }
            into.Add(company);
        }

        private static string Describe(AICompany company)
        {
            string name;
            try
            {
                name = company.Name;
            }
            catch (Exception)
            {
                name = null;
            }
            return (string.IsNullOrEmpty(name) ? "?" : name) + " (ai:" + company.UniqueID.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
