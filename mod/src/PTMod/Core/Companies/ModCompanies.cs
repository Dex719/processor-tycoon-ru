using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Companies;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameBankruptcy = ProcessorTycoon.Bank.BankruptcyHandler;
using GameDataFinder = ProcessorTycoon.DataFinder;
using GamePlayer = ProcessorTycoon.Player;
using GameProvider = ProcessorTycoon.CompanySystem.CompanyDataProvider;
using GameSpawner = ProcessorTycoon.CompanySystem.CompanySpawner;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using UnityObject = UnityEngine.Object;

namespace PTMod.Core
{
    /// <summary>
    /// The companies of the world for the mod (FR-13, D7): the stable string key of a company (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>), the way back from
    /// the key to the game's object, and the cached list of every company. Everything the mod stores about a company (reputation, modifier scopes,
    /// conditions, the state of the features) is stored by key; the game's <c>ICompany</c> object never goes to a save.
    /// <list type="bullet">
    /// <item><b>Keys.</b> <see cref="KeyOf"/> is O(1) and does not allocate: the player is <c>player</c>, an <c>AICompany</c> is <c>ai:</c> plus its
    /// <c>UniqueID</c> (the index in <c>CompanySpawner.historicalCompanies</c>, the same after a load). Anything else (null, an object that is no
    /// company of the game) has no key.</item>
    /// <item><b>The cache.</b> <see cref="All"/> is the player first, then the AI companies ordered by <c>UniqueID</c>; bankrupt AI companies stay in it
    /// (their <c>IsBankrupt</c> is true), because conditions and features ask "did this rival go bankrupt". It is a snapshot: a new list is made
    /// only when the set of companies changes, so a caller may keep iterating the one it got. The AI part is built from
    /// <c>CompanyDataProvider.GetAICompanies</c> (no scan of the scene), which lists the AI companies that are not bankrupt; the bankrupt ones are kept
    /// from the earlier snapshots. The provider's own list is only refreshed by the game at <c>OnLateMonthPassed</c> and <c>OnLastLoadedTick</c>, so
    /// on the events that change the set (a spawn, a bankruptcy, the moment the game is ready) the AI companies are also taken from
    /// <c>DataFinder.FindAllAICompanies</c> - one typed scan of at most a few dozen objects, a few times per game. Refreshed on
    /// <c>TickPhase.MonthCacheUpdate</c>, <c>CompanySpawner.OnCompanySpawned</c>, <c>BankruptcyHandler.OnCompanyWentBankrupt</c> and just before
    /// <see cref="ModTicks.GameReady"/>; emptied when a scene goes.</item>
    /// <item><b>Changes.</b> <see cref="Changed"/> is raised, and <see cref="Version"/> grows, whenever the set of companies or a company's bankruptcy
    /// changed: a cache of anything derived from the companies (the lists of <c>ModConditions</c>) keeps the version it was built for.</item>
    /// </list>
    /// The game's classes are touched only in the small methods of <see cref="GameAccess"/> (no inlining); when they do not match the game the error is
    /// logged once and the cache stays as it was. Main thread only. Never throws.
    /// </summary>
    public static class ModCompanies
    {
        /// <summary>Key of the player's company.</summary>
        public const string PlayerKey = CompanyKey.Player;

        private const string CoreId = ModLog.CoreFeatureId;
        private const string RefreshSite = "ModCompanies/refresh";
        private const string SpawnSite = "ModCompanies/spawned";
        private const string BankruptSite = "ModCompanies/bankrupt";
        private const string ReadySite = "ModCompanies/ready";
        private const string IdentitySite = "ModCompanies/identity";
        private const int RefreshPriority = 1000;   // before the features' handlers of the same phase
        private const int AiKeyCacheSize = 256;     // AICompany.UniqueID is the index in a list of 13 historical companies

        private static readonly ICompany[] NoCompanies = new ICompany[0];
        private static readonly IReadOnlyList<ICompany> EmptyView = new ReadOnlyCollection<ICompany>(NoCompanies);
        private static readonly Dictionary<string, ICompany> ByKey = new Dictionary<string, ICompany>(StringComparer.Ordinal);
        private static readonly List<ICompany> Found = new List<ICompany>(32);
        private static readonly List<Candidate> Pool = new List<Candidate>(32);
        private static readonly string[] AiKeys = new string[AiKeyCacheSize];
        private static readonly Comparison<Candidate> ByUniqueId = (a, b) => a.Id.CompareTo(b.Id);
        private static readonly Action SpawnedHandler = OnSpawned;
        private static readonly Action<ICompany> BankruptHandler = OnBankrupt;

        private static ICompany[] _all = NoCompanies;       // the player (if any), then the AI companies by UniqueID
        private static IReadOnlyList<ICompany> _view = EmptyView;
        private static ICompany _player;
        private static int _version;
        private static bool _started;
        private static bool _gameBroken;                    // the game's classes do not match the mod: no refreshes until restart
        private static bool _duplicateWarned;
        private static bool _eventsMissingWarned;
        private static bool _eventsAttachedLogged;          // once per scene

        private enum Kind
        {
            Other,
            Player,
            Ai
        }

        private struct Candidate
        {
            public int Id;
            public ICompany Company;
        }

        // ---- public API ----

        /// <summary>
        /// The key of a company: <c>player</c> for the player's company, <c>ai:&lt;UniqueID&gt;</c> for an AI company, null for null and for anything
        /// that is not a company of the game. Does not look at the cache, does not allocate.
        /// </summary>
        public static string KeyOf(ICompany company)
        {
            if (ReferenceEquals(company, null)) return null;

            Kind kind = Kind.Other;
            int id = -1;
            try
            {
                kind = GameAccess.KindOf(company, out id);
            }
            catch (Exception ex)
            {
                GameFailed("reading the identity of a company", IdentitySite, ex);
            }

            if (kind == Kind.Player) return PlayerKey;
            return kind == Kind.Ai ? AiKey(id) : null;
        }

        /// <summary>
        /// The company with this key, or null when there is none in the world (the key is not valid, the company has not been spawned yet, the
        /// scene has gone). A bankrupt company is found (check <c>IsBankrupt</c>). A key is compared exactly: <c>ai:7</c>, not <c>ai:07</c>.
        /// </summary>
        public static ICompany Resolve(string key)
        {
            if (!CompanyKey.IsValid(key)) return null;
            if (CompanyKey.IsPlayer(key)) return PlayerCompany();

            ICompany company;
            if (!ByKey.TryGetValue(key, out company)) return null;
            return IsAlive(company) ? company : null;
        }

        /// <summary>
        /// Every company the mod knows: the player first, then the AI companies by <c>UniqueID</c>, bankrupt ones included. A snapshot, never null,
        /// empty in the main menu and until the game is ready. See the class description for when it is refreshed.
        /// </summary>
        public static IReadOnlyList<ICompany> All
        {
            get { return _view; }
        }

        /// <summary>True for the player's company; false for null and for every other company.</summary>
        public static bool IsPlayer(ICompany company)
        {
            if (ReferenceEquals(company, null)) return false;
            try
            {
                int id;
                return GameAccess.KindOf(company, out id) == Kind.Player;
            }
            catch (Exception ex)
            {
                GameFailed("reading the identity of a company", IdentitySite, ex);
                return false;
            }
        }

        /// <summary>
        /// Grows whenever the set of companies or the bankruptcy of one of them changed (and when a scene went), together with <see cref="Changed"/>.
        /// A cache built from the companies remembers the version it was built at and is stale when this differs.
        /// </summary>
        public static int Version
        {
            get { return _version; }
        }

        /// <summary>
        /// The set of companies or a company's bankruptcy changed (a company spawned, a company went bankrupt, the game became ready, the scene
        /// went). Raised after <see cref="All"/> and <see cref="Version"/> were updated, through <see cref="ModHooks"/> (a handler of a feature that
        /// is off is skipped, a failing handler does not stop the others).
        /// </summary>
        public static event Action Changed;

        // ---- core services ----

        /// <summary>
        /// Rebuilds the cache from <c>CompanyDataProvider.GetAICompanies</c> and the AI companies already known (the monthly refresh,
        /// <c>MonthCacheUpdate</c>). Raises <see cref="Changed"/> only when the set of companies changed.
        /// </summary>
        internal static void Refresh()
        {
            if (Rebuild(false)) NotifyChanged();
        }

        /// <summary>
        /// A company's bankruptcy status changed without the set of companies changing (<c>BankruptcyHandler.OnCompanyWentBankrupt</c>): the version
        /// grows and <see cref="Changed"/> is raised, so caches of anything that depends on who is bankrupt are rebuilt.
        /// </summary>
        internal static void MarkChanged()
        {
            NotifyChanged();
        }

        /// <summary>The company is bankrupt; false for null and when it cannot be read.</summary>
        internal static bool IsBankrupt(ICompany company)
        {
            if (ReferenceEquals(company, null)) return false;
            try
            {
                return GameAccess.BankruptOf(company);
            }
            catch (Exception ex)
            {
                GameFailed("reading the bankruptcy of a company", IdentitySite, ex);
                return false;
            }
        }

        // ---- start ----

        /// <summary>Declare stage: the cache is refreshed every month and just before <see cref="ModTicks.GameReady"/> (the features hear it after us).</summary>
        [CoreInit(CoreInitStage.Declare)]
        internal static void Declare()
        {
            ModTicks.BeforeGameReady += OnBeforeGameReady;
            ModTicks.Subscribe(CoreId, TickPhase.MonthCacheUpdate, OnMonthCacheUpdate, RefreshPriority);
        }

        /// <summary>HostReady stage: follow the scenes - the cache of a scene is dropped, the game scene gets the subscriptions to its events.</summary>
        [CoreInit(CoreInitStage.HostReady, 800, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;

            // A late start inside a loaded scene: the host has raised SceneReady already.
            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        // ---- scenes ----

        private static void OnSceneReady(int scene)
        {
            Reset();
            if (scene != PTModHost.GameScene) return;

            AttachGameEvents();
        }

        private static void OnSceneLeaving()
        {
            Reset();
        }

        /// <summary>Forgets the companies of the scene that goes; the objects are destroyed with it.</summary>
        private static void Reset()
        {
            bool had = _all.Length > 0;
            _eventsAttachedLogged = false;
            _all = NoCompanies;
            _view = EmptyView;
            _player = null;
            ByKey.Clear();
            Found.Clear();
            Pool.Clear();

            if (had) NotifyChanged();
            else _version++;   // a cache built in the scene that went must not look current
        }

        // ---- the game's moments (must not throw) ----

        private static void OnBeforeGameReady(GameReadyReason reason)
        {
            if (!ModHooks.CanRun(CoreId, ReadySite)) return;
            try
            {
                AttachGameEvents();   // a second chance if a singleton of the scene was missing at SceneReady
                Rebuild(true);
                NotifyChanged();
                ModLog.Info(CoreId, "companies: GameReady(" + reason + "): " + Describe());
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, ReadySite, ex);
            }
        }

        private static void OnMonthCacheUpdate()
        {
            Refresh();
        }

        /// <summary><c>CompanySpawner.OnCompanySpawned</c>: a new AI company exists. The game's own list does not know it before the next month.</summary>
        private static void OnSpawned()
        {
            if (!ModHooks.CanRun(CoreId, SpawnSite)) return;
            try
            {
                bool changed = Rebuild(true);
                ModLog.Debug(CoreId, "companies: a company was spawned; " + Describe() + (changed ? "" : " (no change in the cache)"));
                if (changed) NotifyChanged();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, SpawnSite, ex);
            }
        }

        /// <summary><c>BankruptcyHandler.OnCompanyWentBankrupt</c>: the company stays in the cache with <c>IsBankrupt</c> true.</summary>
        private static void OnBankrupt(ICompany company)
        {
            if (!ModHooks.CanRun(CoreId, BankruptSite)) return;
            try
            {
                Rebuild(true);   // the set may have changed too (a company the cache had not seen)
                ModLog.Debug(CoreId, "companies: " + (KeyOf(company) ?? "a company") + " went bankrupt");
                MarkChanged();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, BankruptSite, ex);
            }
        }

        private static void AttachGameEvents()
        {
            try
            {
                bool spawner = GameAccess.AttachSpawner(SpawnedHandler);
                bool bankruptcy = GameAccess.AttachBankruptcy(BankruptHandler);
                if (spawner && bankruptcy)
                {
                    if (!_eventsAttachedLogged)
                    {
                        _eventsAttachedLogged = true;
                        ModLog.Debug(CoreId, "companies: subscribed to CompanySpawner.OnCompanySpawned and BankruptcyHandler.OnCompanyWentBankrupt");
                    }
                    return;
                }
                if (_eventsMissingWarned) return;

                _eventsMissingWarned = true;
                ModLog.Warn(CoreId, "companies: " + (spawner ? "BankruptcyHandler" : bankruptcy ? "CompanySpawner" : "CompanySpawner and BankruptcyHandler")
                                    + " not found in the scene; the cache of companies is refreshed every month only");
            }
            catch (Exception ex)
            {
                GameFailed("subscribing to CompanySpawner and BankruptcyHandler", RefreshSite, ex);
            }
        }

        // ---- the cache ----

        /// <summary>
        /// Brings the cache up to date. <paramref name="deep"/> also scans the scene for AI companies (spawns, bankruptcies, the moment the game is
        /// ready). True when the set of companies (or their order) changed. A failure is counted and logged, the cache stays as it was.
        /// </summary>
        private static bool Rebuild(bool deep)
        {
            if (_gameBroken || !ModHooks.CanRun(CoreId, RefreshSite)) return false;
            try
            {
                ICompany player = PlayerCompany();

                // The AI companies the game lists now, plus those known before and still alive: the provider no longer lists the bankrupt ones.
                Found.Clear();
                GameAccess.ListFromProvider(Found);
                if (deep) GameAccess.ListFromScan(Found);
                for (int i = 0; i < _all.Length; i++)
                {
                    ICompany known = _all[i];
                    if (!ReferenceEquals(known, player) && IsAlive(known)) Found.Add(known);
                }

                Pool.Clear();
                for (int i = 0; i < Found.Count; i++)
                {
                    int id;
                    if (GameAccess.KindOf(Found[i], out id) != Kind.Ai || id < 0) continue;
                    Pool.Add(new Candidate { Id = id, Company = Found[i] });
                }
                Found.Clear();
                Pool.Sort(ByUniqueId);

                // One company per id: the same object arrives from several sources.
                int count = 0;
                for (int i = 0; i < Pool.Count; i++)
                {
                    if (count > 0 && Pool[count - 1].Id == Pool[i].Id)
                    {
                        if (!ReferenceEquals(Pool[count - 1].Company, Pool[i].Company)) WarnDuplicate(Pool[i].Id);
                        continue;
                    }
                    Pool[count++] = Pool[i];
                }
                Pool.RemoveRange(count, Pool.Count - count);

                bool changed = !SameAsCache(player, count);
                if (changed) Publish(player, count);
                Pool.Clear();
                return changed;
            }
            catch (Exception ex)
            {
                Pool.Clear();
                Found.Clear();
                GameFailed("refreshing the cache of companies", RefreshSite, ex);
                return false;
            }
        }

        private static bool SameAsCache(ICompany player, int aiCount)
        {
            int offset = ReferenceEquals(player, null) ? 0 : 1;
            if (_all.Length != offset + aiCount) return false;
            if (offset == 1 && !ReferenceEquals(_all[0], player)) return false;

            for (int i = 0; i < aiCount; i++)
            {
                if (!ReferenceEquals(_all[offset + i], Pool[i].Company)) return false;
            }
            return true;
        }

        private static void Publish(ICompany player, int aiCount)
        {
            int offset = ReferenceEquals(player, null) ? 0 : 1;
            var all = new ICompany[offset + aiCount];
            ByKey.Clear();
            if (offset == 1) all[0] = player;
            for (int i = 0; i < aiCount; i++)
            {
                all[offset + i] = Pool[i].Company;
                ByKey[AiKey(Pool[i].Id)] = Pool[i].Company;
            }

            _player = player;
            _all = all;
            _view = new ReadOnlyCollection<ICompany>(all);
        }

        private static void NotifyChanged()
        {
            _version++;
            ModHooks.Raise("ModCompanies.Changed", Changed);
        }

        private static ICompany PlayerCompany()
        {
            ICompany player = _player;
            if (!ReferenceEquals(player, null) && IsAlive(player)) return player;

            try
            {
                player = GameAccess.ReadPlayer();
            }
            catch (Exception ex)
            {
                GameFailed("reading the player's company", IdentitySite, ex);
                player = null;
            }
            _player = player;
            return player;
        }

        /// <summary>A Unity object that was destroyed (its scene went) is not a company any more, though the reference to it is not null.</summary>
        private static bool IsAlive(ICompany company)
        {
            if (ReferenceEquals(company, null)) return false;
            var unityObject = company as UnityObject;
            return ReferenceEquals(unityObject, null) || unityObject != null;
        }

        private static string AiKey(int id)
        {
            if (id < 0) return null;
            if (id >= AiKeys.Length) return CompanyKey.ForAi(id);

            string key = AiKeys[id];
            if (key == null)
            {
                key = CompanyKey.ForAi(id);
                AiKeys[id] = key;
            }
            return key;
        }

        private static string Describe()
        {
            int ai = 0;
            int bankrupt = 0;
            for (int i = 0; i < _all.Length; i++)
            {
                if (!ReferenceEquals(_all[i], _player)) ai++;
                if (IsBankrupt(_all[i])) bankrupt++;
            }
            return (ReferenceEquals(_player, null) ? "no player company" : "the player's company") + " and " + ai + " AI compan" + (ai == 1 ? "y" : "ies")
                   + " known (" + bankrupt + " bankrupt)";
        }

        private static void WarnDuplicate(int id)
        {
            if (_duplicateWarned) return;
            _duplicateWarned = true;
            ModLog.Warn(CoreId, "companies: two AI companies have the UniqueID " + id + "; ai:" + id + " is the first of them");
        }

        /// <summary>A failure of the game's classes: a mismatch switches the refreshes off for the session (logged once), anything else is counted against the core.</summary>
        private static void GameFailed(string what, string site, Exception ex)
        {
            if (ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException)
            {
                if (_gameBroken) return;
                _gameBroken = true;
                ModLog.Error(CoreId, "companies: " + what + " failed, the game's classes do not match the mod (another game version?); the cache of companies "
                                     + "is not refreshed until the game restarts", ex);
                return;
            }
            ModHooks.ReportFault(CoreId, site, ex);
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        private static class GameAccess
        {
            // Not a field of ModCompanies: a mismatch of AICompany fails the first use of this class, not the whole service.
            private static readonly List<AICompany> Buffer = new List<AICompany>(32);

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static Kind KindOf(ICompany company, out int uniqueId)
            {
                uniqueId = -1;
                if (company.IsPlayer) return Kind.Player;

                // A destroyed AICompany still answers: UniqueID is a managed field.
                var ai = company as AICompany;
                if (ReferenceEquals(ai, null)) return Kind.Other;
                uniqueId = ai.UniqueID;
                return Kind.Ai;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool BankruptOf(ICompany company)
            {
                return company.IsBankrupt;
            }

            /// <summary><c>Player.Instance.Company</c>; null in the main menu, where the player is gone.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static ICompany ReadPlayer()
            {
                GamePlayer player = GamePlayer.Instance;
                if (player == null) return null;

                ICompany company = player.Company;
                return IsAliveCompany(company) ? company : null;
            }

            /// <summary>The AI companies that are not bankrupt, from the game's cached list. False when the provider does not exist (the scene is not up).</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool ListFromProvider(List<ICompany> into)
            {
                GameProvider provider = GameProvider.Instance;
                if (provider == null) return false;

                provider.GetAICompanies(Buffer);
                for (int i = 0; i < Buffer.Count; i++)
                {
                    AICompany ai = Buffer[i];
                    if (ai != null) into.Add(ai);
                }
                Buffer.Clear();   // no references to the scene's objects between calls
                return true;
            }

            /// <summary>Every AI company of the scene, bankrupt ones included (one scan of <c>AICompany</c> objects).</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void ListFromScan(List<ICompany> into)
            {
                List<ICompany> all = GameDataFinder.FindAllAICompanies(false);
                if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null) into.Add(all[i]);
                }
            }

            /// <summary>Subscribes <paramref name="handler"/> to <c>CompanySpawner.OnCompanySpawned</c> exactly once. False when there is no spawner.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool AttachSpawner(Action handler)
            {
                GameSpawner spawner = GameSpawner.Instance;
                if (spawner == null) return false;

                spawner.OnCompanySpawned -= handler;
                spawner.OnCompanySpawned += handler;
                return true;
            }

            /// <summary>Subscribes <paramref name="handler"/> to <c>BankruptcyHandler.OnCompanyWentBankrupt</c> exactly once. False when there is no handler.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool AttachBankruptcy(Action<ICompany> handler)
            {
                GameBankruptcy bankruptcy = GameBankruptcy.Instance;
                if (bankruptcy == null) return false;

                bankruptcy.OnCompanyWentBankrupt -= handler;
                bankruptcy.OnCompanyWentBankrupt += handler;
                return true;
            }

            private static bool IsAliveCompany(ICompany company)
            {
                var unityObject = company as UnityObject;
                return !ReferenceEquals(company, null) && (ReferenceEquals(unityObject, null) || unityObject != null);
            }
        }
    }
}
