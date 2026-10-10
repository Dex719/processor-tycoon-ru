using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Reputation;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// The brand reputation of every company (FR-13): one number in [0, 100] per company, 50 for a company that never had a change, shared by the
    /// features (the press reviews write it, the conditions and the channels read it). The rules - the clamp, the history, the drift, what is
    /// ignored - are the pure <see cref="ReputationStore"/>; this class is its one instance and its place in the game:
    /// <list type="bullet">
    /// <item><b>Keys.</b> A company is addressed by its <see cref="ModCompanies.KeyOf"/> key (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>). A new AI company needs no
    /// registration: it reads <see cref="Default"/> until its first change. Reputation does not change the game's formulas by itself (FR-13 item 6);
    /// the effect and the display belong to <c>press-reviews</c>.</item>
    /// <item><b>Add and Set</b> clamp, write one history entry (date, delta, result, reason) and raise <see cref="Changed"/>. A null company, a company
    /// without a key and a NaN or infinite number change nothing and leave a warning in the log.</item>
    /// <item><b>The monthly drift</b> belongs to the core (FR-13 items 7-9): on <c>TickPhase.LateMonthPassed</c>, not during the fast-forward, every company
    /// of <see cref="ModCompanies.All"/> that is not bankrupt moves towards <c>reputation.driftTarget</c> (50) by the share <c>reputation.driftMonthlyRate</c>
    /// (0.03) of the distance - without a history entry and without <see cref="Changed"/>, so months of drift never push the meaningful entries out of the
    /// history. Then <see cref="Drifted"/> is raised once. Features do not drift reputation themselves. The channel cache of <c>ModModifiers</c> is dropped by
    /// <c>ModModifiers</c> itself, which listens to <see cref="Drifted"/>.</item>
    /// <item><b>The save.</b> The values and the last <c>reputation.historyPerCompany</c> (50) history entries of every company are the block
    /// <c>reputation</c> of the section <c>mod-framework</c> of the side file, <c>{ "player": { "value": 57.5, "history": [ ... ] }, "ai:0": ... }</c>.
    /// A new game and the main menu empty it; an entry that cannot be read (a bad key, a missing or non-numeric value) is dropped with a warning, a
    /// bad history entry alone is dropped, and the rest is kept.</item>
    /// </list>
    /// The numbers come from <c>core.json</c> and are read on first use. Handlers of <see cref="Changed"/> and <see cref="Drifted"/> are called through
    /// <see cref="ModHooks"/>: only for active features, each isolated. Main thread only. Never throws.
    /// </summary>
    public static class ModReputation
    {
        /// <summary>Lowest reputation.</summary>
        public const float Min = ReputationStore.Min;

        /// <summary>Highest reputation.</summary>
        public const float Max = ReputationStore.Max;

        /// <summary>The reputation of a company that never had a change, a new AI company included.</summary>
        public const float Default = ReputationStore.Default;

        private const string CoreId = ModLog.CoreFeatureId;
        private const string DateFormat = "yyyy-MM-dd";
        private const int DriftPriority = 1000;            // before the features' handlers of LateMonthPassed
        private const int MaxReportedProblems = 10;
        private const double DefaultDriftRate = 0.03;

        private static readonly ReputationStore Store = CreateStore();
        private static readonly List<string> DriftKeys = new List<string>(32);
        private static readonly ICoreSectionPart SavePart = new ReputationPart();

        private static bool _configured;
        private static float _driftRate = (float)DefaultDriftRate;
        private static float _driftTarget = Default;

        // ---- reading ----

        /// <summary>The reputation of the company: <see cref="Default"/> for a company that has none, for null and for a company without a key.</summary>
        public static float Get(ICompany company)
        {
            string key = ModCompanies.KeyOf(company);
            return key == null ? Default : Store.Get(key);
        }

        /// <summary>The reputation of the company with this key (<c>player</c>, <c>ai:3</c>); <see cref="Default"/> for an unknown or invalid key.</summary>
        public static float Get(string companyKey)
        {
            return Store.Get(companyKey);
        }

        /// <summary>
        /// The changes made by <see cref="Add"/> and <see cref="Set"/> (never the drift), oldest first, at most <c>reputation.historyPerCompany</c> of
        /// them; a copy. Empty for an unknown company and an invalid key.
        /// </summary>
        public static IReadOnlyList<ReputationChange> History(string companyKey)
        {
            return Store.History(companyKey);
        }

        // ---- changing ----

        /// <summary>
        /// Changes the reputation of the company by <paramref name="delta"/>, keeps it in [0, 100], writes a history entry (the requested delta, the
        /// result, <paramref name="reason"/> - an L10n key of the caller's feature or free text) and raises <see cref="Changed"/>. A null company, a
        /// company without a key and a delta that is NaN or infinite change nothing and are logged as a warning.
        /// </summary>
        public static void Add(ICompany company, float delta, string reason)
        {
            try
            {
                string key = RequireKey(company, "Add");
                if (key == null) return;

                EnsureConfig();
                Store.Add(key, delta, reason, ModTicks.Today.Date);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "reputation: Add failed", ex);
            }
        }

        /// <summary>
        /// Sets the reputation of the company to <paramref name="value"/> (kept in [0, 100]), writes a history entry with the effective change and
        /// raises <see cref="Changed"/>. The same refusals as <see cref="Add"/>.
        /// </summary>
        public static void Set(ICompany company, float value, string reason)
        {
            try
            {
                string key = RequireKey(company, "Set");
                if (key == null) return;

                EnsureConfig();
                Store.Set(key, value, reason, ModTicks.Today.Date);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "reputation: Set failed", ex);
            }
        }

        /// <summary>
        /// Moves the reputation of one company towards <paramref name="target"/> by the share <paramref name="rate"/> of the distance
        /// (<c>v += (target - v) * rate</c>; closer than 0.05 to the target it becomes the target). No history entry, no <see cref="Changed"/> and no
        /// <see cref="Drifted"/>: the monthly drift of all companies is done by the core, features do not need this.
        /// </summary>
        public static void Drift(ICompany company, float rate, float target = Default)
        {
            try
            {
                string key = RequireKey(company, "Drift");
                if (key == null) return;

                EnsureConfig();
                Store.Drift(key, rate, target);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "reputation: Drift failed", ex);
            }
        }

        // ---- events ----

        /// <summary>A reputation was changed by <see cref="Add"/> or <see cref="Set"/> (never by the drift): company key, old value, new value, reason.</summary>
        public static event Action<string, float, float, string> Changed;

        /// <summary>
        /// The monthly drift of all companies is done (once per month, after the last company moved). The values already differ from the month before, so
        /// anything cached from the reputation (the daily cache of the channels) is stale.
        /// </summary>
        public static event Action Drifted;

        // ---- start ----

        /// <summary>
        /// Declare stage: the block <c>reputation</c> joins the section of the core and the monthly drift is subscribed. With the default tick options
        /// the drift does not run during the new game's fast-forward (FR-13 item 8): everybody starts from 50 there anyway.
        /// </summary>
        [CoreInit(CoreInitStage.Declare)]
        internal static void Declare()
        {
            CoreSection.RegisterPart(SavePart);
            ModTicks.Subscribe(CoreId, TickPhase.LateMonthPassed, OnLateMonthPassed, DriftPriority);
        }

        /// <summary>
        /// <c>DateController.OnLateMonthPassed</c> (day 1 of every month, after <c>OnMonthPassed</c>): the drift of every company that is not bankrupt,
        /// then <see cref="Drifted"/>.
        /// </summary>
        private static void OnLateMonthPassed()
        {
            // The default options of the subscription already skip the fast-forward; the rule stays visible (and true) here as well.
            if (ModTicks.IsFastForwarding) return;

            EnsureConfig();

            IReadOnlyList<ICompany> companies = ModCompanies.All;
            DriftKeys.Clear();
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ModCompanies.IsBankrupt(company)) continue;

                string key = ModCompanies.KeyOf(company);
                if (key != null) DriftKeys.Add(key);
            }

            int listed = DriftKeys.Count;
            int moved = Store.DriftAll(DriftKeys, _driftRate, _driftTarget);   // raises Drifted once, after the last company
            DriftKeys.Clear();
            ModLog.Debug(CoreId, "reputation: monthly drift towards " + _driftTarget.ToString("0.##", CultureInfo.InvariantCulture) + " by "
                                 + _driftRate.ToString("0.###", CultureInfo.InvariantCulture) + ": " + moved + " of " + listed + " company(ies) moved");
        }

        // ---- internals ----

        private static ReputationStore CreateStore()
        {
            var store = new ReputationStore(ReputationStore.DefaultHistoryLimit, WarnFromStore);
            store.Changed += RelayChanged;
            store.Drifted += RelayDrifted;
            return store;
        }

        private static void WarnFromStore(string message)
        {
            ModLog.Warn(CoreId, message);   // the store prefixes its messages with "reputation: "
        }

        private static void RelayChanged(string key, float oldValue, float newValue, string reason)
        {
            ModHooks.Raise("ModReputation.Changed", Changed, key, oldValue, newValue, reason);
        }

        private static void RelayDrifted()
        {
            ModHooks.Raise("ModReputation.Drifted", Drifted);
        }

        private static string RequireKey(ICompany company, string operation)
        {
            if (ReferenceEquals(company, null))
            {
                ModLog.Warn(CoreId, "reputation: " + operation + " ignored: the company is null");
                return null;
            }

            string key = ModCompanies.KeyOf(company);
            if (key == null)
                ModLog.Warn(CoreId, "reputation: " + operation + " ignored: the company has no key (it is neither the player's nor an AI company)");
            return key;
        }

        /// <summary>Reads <c>reputation.*</c> of <c>core.json</c> once, before the first change or load; a bad number gives the default and a warning.</summary>
        private static void EnsureConfig()
        {
            if (_configured) return;
            _configured = true;

            double limit = ReputationStore.DefaultHistoryLimit;
            double rate = DefaultDriftRate;
            double target = Default;
            try
            {
                limit = ModContent.GetCoreNumber("reputation", "historyPerCompany", ReputationStore.DefaultHistoryLimit);
                rate = ModContent.GetCoreNumber("reputation", "driftMonthlyRate", DefaultDriftRate);
                target = ModContent.GetCoreNumber("reputation", "driftTarget", Default);
            }
            catch (Exception)
            {
                // ModContent logs its own problems and does not throw; the defaults stand.
            }

            if (limit >= 1 && limit <= 1000)
            {
                Store.SetHistoryLimit((int)Math.Round(limit));
            }
            else
            {
                ModLog.Warn(CoreId, "reputation: core.json reputation.historyPerCompany = " + limit.ToString("R", CultureInfo.InvariantCulture)
                                    + " is not a number in [1, 1000]; " + ReputationStore.DefaultHistoryLimit + " is used");
            }

            if (rate >= 0 && rate <= 1)
            {
                _driftRate = (float)rate;
            }
            else
            {
                ModLog.Warn(CoreId, "reputation: core.json reputation.driftMonthlyRate = " + rate.ToString("R", CultureInfo.InvariantCulture)
                                    + " is not a number in [0, 1]; " + DefaultDriftRate.ToString(CultureInfo.InvariantCulture) + " is used");
            }

            if (target >= Min && target <= Max)
            {
                _driftTarget = (float)target;
            }
            else
            {
                ModLog.Warn(CoreId, "reputation: core.json reputation.driftTarget = " + target.ToString("R", CultureInfo.InvariantCulture)
                                    + " is not a number in [0, 100]; " + Default.ToString(CultureInfo.InvariantCulture) + " is used");
            }
        }

        // ---- the save ----

        /// <summary>
        /// The block <c>reputation</c> (<c>ReputationDto</c> by company key): <c>{ "value": 57.5, "history": [ { "date": "1985-02-14", "delta": 7.5,
        /// "value": 57.5, "reason": "key" } ] }</c>. Written with the player first, then the AI companies by id; left out when nobody has a reputation.
        /// </summary>
        private sealed class ReputationPart : ICoreSectionPart
        {
            public string Key
            {
                get { return CoreSection.ReputationKey; }
            }

            public void ResetToDefaults()
            {
                Store.Clear();
            }

            public void Read(JsonNode block, int schema)
            {
                EnsureConfig();
                Store.Clear();
                if (block == null || block.IsNull) return;

                var root = block as JsonObject;
                if (root == null)
                {
                    ModLog.Warn(CoreId, "save: the block 'reputation' is " + block.Kind.ToString().ToLowerInvariant() + ", not an object; the reputations start from the default");
                    return;
                }

                var problems = new List<string>();
                int loaded = 0;
                foreach (KeyValuePair<string, JsonNode> member in root)
                {
                    if (ReadCompany(member.Key, member.Value, problems)) loaded++;
                }

                for (int i = 0; i < problems.Count && i < MaxReportedProblems; i++) ModLog.Warn(CoreId, "save: reputation" + problems[i]);
                if (problems.Count > MaxReportedProblems)
                    ModLog.Warn(CoreId, "save: reputation: " + (problems.Count - MaxReportedProblems) + " more problem(s) of the same kind are not listed");
                ModLog.Debug(CoreId, "save: reputation of " + loaded + " company(ies) restored");
            }

            public JsonNode Write()
            {
                IReadOnlyList<string> keys = Store.Keys();
                if (keys.Count == 0) return null;

                var ordered = new List<string>(keys);
                ordered.Sort(CompareKeys);

                var root = new JsonObject();
                foreach (string key in ordered)
                {
                    var entry = new JsonObject();
                    entry.Set("value", Store.Get(key));

                    var history = new JsonArray();
                    foreach (ReputationChange change in Store.History(key))
                    {
                        var item = new JsonObject();
                        item.Set("date", change.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
                        item.Set("delta", change.Delta);
                        item.Set("value", change.Value);
                        if (change.Reason.Length > 0) item.Set("reason", change.Reason);
                        history.Add(item);
                    }
                    entry.Set("history", history);
                    root.Set(key, entry);
                }
                return root;
            }

            /// <summary>Player first, then <c>ai:N</c> by N, then anything else (the store only holds valid keys) in ordinal order.</summary>
            private static int CompareKeys(string a, string b)
            {
                int rankA = RankOf(a);
                int rankB = RankOf(b);
                if (rankA != rankB) return rankA.CompareTo(rankB);

                if (rankA == 1)
                {
                    int idA, idB;
                    if (CompanyKey.TryGetAiId(a, out idA) && CompanyKey.TryGetAiId(b, out idB) && idA != idB) return idA.CompareTo(idB);
                }
                return string.CompareOrdinal(a, b);
            }

            private static int RankOf(string key)
            {
                if (CompanyKey.IsPlayer(key)) return 0;
                int id;
                return CompanyKey.TryGetAiId(key, out id) ? 1 : 2;
            }

            /// <summary>One company of the block; false (the reason is added to <paramref name="problems"/>) when it is dropped.</summary>
            private static bool ReadCompany(string key, JsonNode node, List<string> problems)
            {
                string where = "[" + key + "]";
                if (!CompanyKey.IsValid(key))
                {
                    problems.Add(": the key '" + key + "' is not a company key (player or ai:<number>); the entry is dropped");
                    return false;
                }

                var entry = node as JsonObject;
                if (entry == null)
                {
                    problems.Add(where + ": expected an object, got " + node.Kind.ToString().ToLowerInvariant() + "; the entry is dropped");
                    return false;
                }

                double value;
                if (!entry.TryGetDouble("value", out value) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    problems.Add(where + ": 'value' is missing or not a number; the entry is dropped");
                    return false;
                }

                var history = new List<ReputationChange>();
                JsonNode historyNode;
                if (entry.TryGetValue("history", out historyNode) && !historyNode.IsNull)
                {
                    var array = historyNode as JsonArray;
                    if (array == null)
                    {
                        problems.Add(where + ".history: expected an array, got " + historyNode.Kind.ToString().ToLowerInvariant() + "; the history is dropped");
                    }
                    else
                    {
                        for (int i = 0; i < array.Count; i++)
                        {
                            ReputationChange change;
                            string problem;
                            if (ReadChange(array[i], out change, out problem)) history.Add(change);
                            else problems.Add(where + ".history[" + i + "]: " + problem + "; the entry is dropped");
                        }
                    }
                }

                return Store.Load(key, (float)value, history);
            }

            private static bool ReadChange(JsonNode node, out ReputationChange change, out string problem)
            {
                change = default(ReputationChange);
                var item = node as JsonObject;
                if (item == null)
                {
                    problem = "expected an object, got " + node.Kind.ToString().ToLowerInvariant();
                    return false;
                }

                string text;
                DateTime date;
                if (!item.TryGetString("date", out text)
                    || !DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    problem = "'date' is missing or is not " + DateFormat;
                    return false;
                }

                double delta, value;
                if (!item.TryGetDouble("delta", out delta) || double.IsNaN(delta) || double.IsInfinity(delta)
                    || !item.TryGetDouble("value", out value) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    problem = "'delta' or 'value' is missing or not a number";
                    return false;
                }

                string reason;
                if (!item.TryGetString("reason", out reason)) reason = string.Empty;

                change = new ReputationChange(date, (float)delta, (float)value, reason);
                problem = null;
                return true;
            }
        }
    }
}
