using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using UnityRectTransform = UnityEngine.RectTransform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The real implementation behind <see cref="ResearchTreeUxApi"/> (FR-7, decisions D7, D10, D11): the books of the providers other features
    /// register, the guarded calls into them, and the answers the parts of this feature ask for (the tabs of the strip, the rows of the compare table,
    /// the blocks of the modifiers panel, the lines of the tooltip, the state of the lab). The feature installs it in <c>Register</c>
    /// (<see cref="ResearchTreeUxApi.Install"/>), which hands over every registration made before.
    /// <list type="bullet">
    /// <item><b>Registrations are kept whatever the state of the feature is</b> (item 6): <see cref="IsActive"/> is the state of the feature, the books are
    /// not touched by it, and a provider registered while the feature is off works as soon as it is switched on. A provider is registered before or after
    /// the window of the tree is built; the strip learns of a new tab by <see cref="TabsChanged"/>.</item>
    /// <item><b>Every call into a provider goes through <see cref="ProviderCalls"/>:</b> an exception is counted with <c>ModHooks.ReportFault</c> against the
    /// feature that owns the code of the provider (<c>ModFeatures.FeatureOfType</c> of its type, not this feature), only that provider is switched off until the
    /// game restarts, and the rest go on (AC-7.2).</item>
    /// <item><b>A provider of a feature that is not active is not asked</b> (item 5): its tab is not in the strip and its rows are not in the tables; it is asked
    /// again the moment its feature is switched on (AC-7.3).</item>
    /// <item><b>A duplicate tab id is refused</b> with a warning in the log, a built-in id included.</item>
    /// </list>
    /// Main thread; the books are guarded by a lock for the registrations that arrive from the start of another feature.
    /// </summary>
    internal sealed class ResearchTreeUxRegistry : IResearchTreeUxApi
    {
        private const string FeatureId = ResearchTreeUxFeature.FeatureId;

        // The most that one provider can add (a defence against a provider that never stops).
        private const int MaxCompareRowsPerProvider = 64;
        private const int MaxTooltipLinesPerProvider = 8;

        private static readonly Func<string, bool> OwnerActive = owner => ModFeatures.IsActive(owner);
        private static readonly Action<string, string, Exception> Report = ReportFault;

        private readonly object _sync = new object();
        private readonly ResearchTreeUxFeature _feature;
        private readonly ProviderBook<IResearchTabProvider> _tabs;
        private readonly ProviderBook<ICompareRowProvider> _compareRows = new ProviderBook<ICompareRowProvider>();
        private readonly ProviderBook<IModifierLineSource> _modifierLines = new ProviderBook<IModifierLineSource>();
        private readonly ProviderBook<ITechTooltipLineSource> _tooltipLines = new ProviderBook<ITechTooltipLineSource>();
        private readonly ProviderBook<ILabOccupancySource> _labSources = new ProviderBook<ILabOccupancySource>();

        public ResearchTreeUxRegistry(ResearchTreeUxFeature feature)
        {
            _feature = feature;

            var builtIn = new List<string>();
            foreach (TabDefinition tab in feature.Tabs) builtIn.Add(tab.Id);
            _tabs = new ProviderBook<IResearchTabProvider>(builtIn);
        }

        /// <summary>A tab was added (a feature registered it, before or after the window was built): the strip is to be made again.</summary>
        public event Action TabsChanged;

        public bool IsActive
        {
            get { return ModFeatures.IsActive(FeatureId); }
        }

        /// <summary>How many tabs of other features are registered (the switched-off ones too); for the log and the tests of the source.</summary>
        public int TabCount
        {
            get
            {
                lock (_sync) return _tabs.Count;
            }
        }

        // ---- registering (IResearchTreeUxApi) ----

        public void RegisterTab(IResearchTabProvider provider)
        {
            if (provider == null) return;

            string owner = OwnerOf(provider);
            string name = provider.GetType().Name;
            string id;
            string titleKey;
            int order;
            try
            {
                id = provider.TabId;
                titleKey = provider.TitleKey;
                order = provider.Order;
            }
            catch (Exception e)
            {
                ReportFault(owner, "IResearchTabProvider.TabId " + name, e);
                ModLog.Warn(FeatureId, "api: the tab of " + name + " cannot be read, it is not added");
                return;
            }

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(titleKey))
            {
                ModLog.Warn(FeatureId, "api: the tab of " + name + " has no TabId or no TitleKey, it is not added");
                return;
            }

            AddOutcome outcome;
            lock (_sync) outcome = _tabs.Add(provider, id, titleKey, order, owner);

            switch (outcome)
            {
                case AddOutcome.Added:
                    ModLog.Debug(FeatureId, "api: tab '" + id + "' of " + owner + " registered (order " + order + ")");
                    RaiseTabsChanged();
                    break;
                case AddOutcome.KeyTaken:
                    ModLog.Warn(FeatureId, "api: the tab id '" + id + "' of " + name + " is already taken by a built-in tab or another feature, the tab is not added");
                    break;
                case AddOutcome.AlreadyRegistered:
                    ModLog.Debug(FeatureId, "api: tab '" + id + "' is registered already");
                    break;
                case AddOutcome.Full:
                    ModLog.Warn(FeatureId, "api: too many tabs are registered, the tab '" + id + "' is not added");
                    break;
                default:
                    ModLog.Warn(FeatureId, "api: the tab of " + name + " is not valid, it is not added");
                    break;
            }
        }

        public void RegisterCompareRows(ICompareRowProvider provider)
        {
            AddKeyless(_compareRows, provider, "RegisterCompareRows");
        }

        public void RegisterModifierLines(IModifierLineSource source)
        {
            AddKeyless(_modifierLines, source, "RegisterModifierLines");
        }

        public void RegisterTechTooltipLines(ITechTooltipLineSource source)
        {
            AddKeyless(_tooltipLines, source, "RegisterTechTooltipLines");
        }

        public void RegisterLabOccupancy(ILabOccupancySource source)
        {
            AddKeyless(_labSources, source, "RegisterLabOccupancy");
        }

        public void OpenTab(string tabId)
        {
            if (string.IsNullOrEmpty(tabId) || !IsActive) return;
            _feature.OpenTab(tabId);
        }

        private void AddKeyless<T>(ProviderBook<T> book, T provider, string call) where T : class
        {
            if (provider == null) return;

            string owner = OwnerOf(provider);
            AddOutcome outcome;
            lock (_sync) outcome = book.Add(provider, null, null, 0, owner);

            if (outcome == AddOutcome.Added) ModLog.Debug(FeatureId, "api: " + call + " of " + owner + " (" + provider.GetType().Name + ")");
            else if (outcome == AddOutcome.Full) ModLog.Warn(FeatureId, "api: too many sources are registered, " + provider.GetType().Name + " is not added");
        }

        // ---- the tabs ----

        /// <summary>
        /// The tabs of other features that are shown now: owned by an active feature, not switched off, and available (<c>IsAvailable()</c> is asked,
        /// guarded). As definitions without types, in the order of the book; <see cref="TabMerge"/> puts them among the built-in ones.
        /// </summary>
        public List<TabDefinition> ExternalTabs()
        {
            var result = new List<TabDefinition>();
            var entries = new List<ProviderEntry<IResearchTabProvider>>();
            lock (_sync) _tabs.Collect(OwnerActive, entries);

            foreach (ProviderEntry<IResearchTabProvider> entry in entries)
            {
                bool available = ProviderCalls.Run(_tabs, entry, "IResearchTabProvider.IsAvailable " + NameOf(entry), p => p.IsAvailable(), false, Report);
                if (available) result.Add(new TabDefinition(entry.Key, entry.Caption, null, entry.Order));
            }
            return result;
        }

        /// <summary>The entry of a tab by its id, or null (unknown id, a built-in tab, a tab that was switched off).</summary>
        public ProviderEntry<IResearchTabProvider> TabEntry(string tabId)
        {
            ProviderEntry<IResearchTabProvider> entry;
            lock (_sync) entry = _tabs.EntryByKey(tabId);
            return entry == null || entry.Disabled ? null : entry;
        }

        /// <summary>
        /// <c>BuildContent</c> of the tab, once per scene (the owner keeps the result). Null when the provider threw (it is switched off and reported)
        /// or returned nothing (the same).
        /// </summary>
        public UnityRectTransform BuildTabContent(ProviderEntry<IResearchTabProvider> entry, UnityRectTransform parent)
        {
            string site = "IResearchTabProvider.BuildContent " + NameOf(entry);
            UnityRectTransform root = ProviderCalls.Run(_tabs, entry, site, p => p.BuildContent(parent), (UnityRectTransform)null, Report);
            if (root == null && entry != null && !entry.Disabled)
            {
                ProviderCalls.Fail(_tabs, entry, site, new InvalidOperationException("BuildContent returned no root"), Report);
            }
            return root;
        }

        /// <summary><c>OnShown()</c> of the tab. False when the provider threw (it is switched off and reported).</summary>
        public bool TabShown(ProviderEntry<IResearchTabProvider> entry)
        {
            return ProviderCalls.Run(_tabs, entry, "IResearchTabProvider.OnShown " + NameOf(entry), p => p.OnShown(), Report);
        }

        /// <summary><c>OnHidden()</c> of the tab. False when the provider threw (it is switched off and reported).</summary>
        public bool TabHidden(ProviderEntry<IResearchTabProvider> entry)
        {
            return ProviderCalls.Run(_tabs, entry, "IResearchTabProvider.OnHidden " + NameOf(entry), p => p.OnHidden(), Report);
        }

        /// <summary>The counter of the tab (<c>BadgeCount()</c>), zero for a provider that threw, is off, or answers below zero.</summary>
        public int TabBadge(ProviderEntry<IResearchTabProvider> entry)
        {
            int count = ProviderCalls.Run(_tabs, entry, "IResearchTabProvider.BadgeCount " + NameOf(entry), p => p.BadgeCount(), 0, Report);
            return Math.Max(0, count);
        }

        // ---- the seams of the parts ----

        /// <summary>
        /// The blocks of other features for the modifiers panel and its tooltip (FR-7 item 4): one <c>GetGroup(company)</c> per source, in the order of
        /// registration; null groups are left out. An empty list when the feature is off or nobody has anything to show.
        /// </summary>
        public IReadOnlyList<ModifierLineGroup> ModifierGroups(GameCompany company)
        {
            var groups = new List<ModifierLineGroup>();
            if (!IsActive) return groups;

            var entries = new List<ProviderEntry<IModifierLineSource>>();
            lock (_sync) _modifierLines.Collect(OwnerActive, entries);
            foreach (ProviderEntry<IModifierLineSource> entry in entries)
            {
                ModifierLineGroup group = ProviderCalls.Run(_modifierLines, entry, "IModifierLineSource.GetGroup " + NameOf(entry),
                    p => p.GetGroup(company), (ModifierLineGroup)null, Report);
                if (group != null) groups.Add(group);
            }
            return groups;
        }

        /// <summary>
        /// The rows of other features for the compare table (FR-7 item 4), for the pair of processors (either may be null): the rows of every source, each
        /// copied inside the guarded call so an iterator that throws is caught there. The sorting by <c>Order</c> and the refusal of a used id are the
        /// model's.
        /// </summary>
        public IReadOnlyList<CompareRow> CompareRows(GameCpu a, GameCpu b)
        {
            var rows = new List<CompareRow>();
            if (!IsActive) return rows;

            var entries = new List<ProviderEntry<ICompareRowProvider>>();
            lock (_sync) _compareRows.Collect(OwnerActive, entries);
            foreach (ProviderEntry<ICompareRowProvider> entry in entries)
            {
                List<CompareRow> own = ProviderCalls.Run(_compareRows, entry, "ICompareRowProvider.GetRows " + NameOf(entry),
                    p => ProviderCalls.Materialize(p.GetRows(a, b), MaxCompareRowsPerProvider), (List<CompareRow>)null, Report);
                if (own != null) rows.AddRange(own);
            }
            return rows;
        }

        /// <summary>The extra lines of the tooltip of a technology (FR-7 item 4), all sources together in the order of registration; empty for none.</summary>
        public IReadOnlyList<string> TooltipLines(GameTechnology technology)
        {
            var lines = new List<string>();
            if (!IsActive || technology == null) return lines;

            var entries = new List<ProviderEntry<ITechTooltipLineSource>>();
            lock (_sync) _tooltipLines.Collect(OwnerActive, entries);
            foreach (ProviderEntry<ITechTooltipLineSource> entry in entries)
            {
                List<string> own = ProviderCalls.Run(_tooltipLines, entry, "ITechTooltipLineSource.GetLines " + NameOf(entry),
                    p => ProviderCalls.Materialize(p.GetLines(technology), MaxTooltipLinesPerProvider), (List<string>)null, Report);
                if (own == null) continue;

                foreach (string line in own)
                {
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                }
            }
            return lines;
        }

        /// <summary>
        /// The text of the line "Lab busy: ..." (FR-2 item 9): what the first busy source says; null when the lab is free or the feature is off. A busy
        /// source that gives no text gets a general one.
        /// </summary>
        public string LabBusyText()
        {
            if (!IsActive) return null;

            LabState state = PollLab(true);
            if (!state.Busy) return null;
            return string.IsNullOrEmpty(state.Text) ? ModL10n.L("rtux.lab.busy.other") : state.Text;
        }

        /// <summary>True when a source says the lab is busy with research of its own (the idle-lab reminder does not count such a lab as idle, AC-9.2).</summary>
        public bool LabIsBusy()
        {
            return IsActive && PollLab(false).Busy;
        }

        private LabState PollLab(bool withText)
        {
            return LabOccupancy.Poll(_labSources, OwnerActive, s => s.IsLabBusy(), withText ? (Func<ILabOccupancySource, string>)(s => s.BusyText()) : null, Report);
        }

        // ---- helpers ----

        private void RaiseTabsChanged()
        {
            Action handler = TabsChanged;
            if (handler == null) return;

            // A registration comes from the code of another feature: whatever the listener does must not turn into its failure.
            ModHooks.Guard(FeatureId, "api/tabsChanged", handler);
        }

        private static string OwnerOf(object provider)
        {
            string owner = ModFeatures.FeatureOfType(provider.GetType());
            return string.IsNullOrEmpty(owner) ? ModLog.CoreFeatureId : owner;
        }

        private static string NameOf<T>(ProviderEntry<T> entry) where T : class
        {
            return entry == null ? "?" : (entry.Key ?? entry.Provider.GetType().Name) + " (" + entry.Owner + ")";
        }

        private static void ReportFault(string owner, string site, Exception error)
        {
            ModHooks.ReportFault(owner, site, error);
        }
    }
}
