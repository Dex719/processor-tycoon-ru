using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The default <see cref="IResearchTreeUxApi"/> (a null object that remembers): used until the feature <c>research-tree-ux</c> installs its
    /// own implementation, and again if that implementation is removed. It is never active, opens nothing, and never throws, so a feature that
    /// calls <see cref="ResearchTreeUxApi"/> works the same whether <c>research-tree-ux</c> is on, off, or has not registered yet (the order in
    /// which features register is not fixed, and a feature that is off at start registers only when the player switches it on).
    /// <para>
    /// Registrations made while this object is current are kept in order and handed to the real implementation by
    /// <see cref="ReplayTo"/> when it is installed; calls that still reach this object afterwards are forwarded to that implementation.
    /// </para>
    /// </summary>
    internal sealed class DeferredResearchTreeUxApi : IResearchTreeUxApi
    {
        private const string FeatureId = "research-tree-ux";

        private readonly object _sync = new object();
        private readonly List<IResearchTabProvider> _tabs = new List<IResearchTabProvider>();
        private readonly List<ICompareRowProvider> _compareRows = new List<ICompareRowProvider>();
        private readonly List<IModifierLineSource> _modifierLines = new List<IModifierLineSource>();
        private readonly List<ITechTooltipLineSource> _tooltipLines = new List<ITechTooltipLineSource>();
        private readonly List<ILabOccupancySource> _labSources = new List<ILabOccupancySource>();
        private volatile IResearchTreeUxApi _forward;   // set once the real implementation took over

        public bool IsActive
        {
            get
            {
                IResearchTreeUxApi target = _forward;
                return target != null && target.IsActive;
            }
        }

        /// <summary>How many registrations wait for an implementation (for the log and for tests).</summary>
        public int PendingCount
        {
            get
            {
                lock (_sync)
                {
                    return _tabs.Count + _compareRows.Count + _modifierLines.Count + _tooltipLines.Count + _labSources.Count;
                }
            }
        }

        public void RegisterTab(IResearchTabProvider provider)
        {
            IResearchTreeUxApi target;
            lock (_sync)
            {
                target = _forward;
                if (target == null) _tabs.Add(provider);
            }
            if (target != null) target.RegisterTab(provider);
        }

        public void RegisterCompareRows(ICompareRowProvider provider)
        {
            IResearchTreeUxApi target;
            lock (_sync)
            {
                target = _forward;
                if (target == null) _compareRows.Add(provider);
            }
            if (target != null) target.RegisterCompareRows(provider);
        }

        public void RegisterModifierLines(IModifierLineSource source)
        {
            IResearchTreeUxApi target;
            lock (_sync)
            {
                target = _forward;
                if (target == null) _modifierLines.Add(source);
            }
            if (target != null) target.RegisterModifierLines(source);
        }

        public void RegisterTechTooltipLines(ITechTooltipLineSource source)
        {
            IResearchTreeUxApi target;
            lock (_sync)
            {
                target = _forward;
                if (target == null) _tooltipLines.Add(source);
            }
            if (target != null) target.RegisterTechTooltipLines(source);
        }

        public void RegisterLabOccupancy(ILabOccupancySource source)
        {
            IResearchTreeUxApi target;
            lock (_sync)
            {
                target = _forward;
                if (target == null) _labSources.Add(source);
            }
            if (target != null) target.RegisterLabOccupancy(source);
        }

        public void OpenTab(string tabId)
        {
            IResearchTreeUxApi target = _forward;
            if (target != null) target.OpenTab(tabId);
        }

        /// <summary>
        /// Hands every remembered registration to <paramref name="target"/> in the order of the calls (tabs, compare rows, modifier lines,
        /// tooltip lines, lab sources) and makes this object forward later calls to it. A failure of one registration is logged and does not
        /// stop the others.
        /// </summary>
        public void ReplayTo(IResearchTreeUxApi target)
        {
            if (target == null) return;

            IResearchTabProvider[] tabs;
            ICompareRowProvider[] compareRows;
            IModifierLineSource[] modifierLines;
            ITechTooltipLineSource[] tooltipLines;
            ILabOccupancySource[] labSources;
            lock (_sync)
            {
                _forward = target;
                tabs = _tabs.ToArray();
                compareRows = _compareRows.ToArray();
                modifierLines = _modifierLines.ToArray();
                tooltipLines = _tooltipLines.ToArray();
                labSources = _labSources.ToArray();
                _tabs.Clear();
                _compareRows.Clear();
                _modifierLines.Clear();
                _tooltipLines.Clear();
                _labSources.Clear();
            }

            foreach (IResearchTabProvider item in tabs) Replay("RegisterTab", () => target.RegisterTab(item));
            foreach (ICompareRowProvider item in compareRows) Replay("RegisterCompareRows", () => target.RegisterCompareRows(item));
            foreach (IModifierLineSource item in modifierLines) Replay("RegisterModifierLines", () => target.RegisterModifierLines(item));
            foreach (ITechTooltipLineSource item in tooltipLines) Replay("RegisterTechTooltipLines", () => target.RegisterTechTooltipLines(item));
            foreach (ILabOccupancySource item in labSources) Replay("RegisterLabOccupancy", () => target.RegisterLabOccupancy(item));
        }

        private static void Replay(string call, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ModLog.Error(FeatureId, "ResearchTreeUxApi." + call + " failed while handing over a registration made before the feature started", e);
            }
        }
    }
}
