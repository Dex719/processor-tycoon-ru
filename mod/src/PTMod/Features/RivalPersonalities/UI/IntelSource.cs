using System;
using System.Collections.Generic;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GameMarketData = ProcessorTycoon.MarketSystem.MarketDataProvider;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>The language and the formatters of the game for <see cref="IntelBuilder"/>: <c>ModL10n</c> and <c>StringFormatter</c>.</summary>
    internal sealed class GameIntelText : IIntelText
    {
        public string L(string key, params object[] args)
        {
            return ModL10n.L(key, args);
        }

        public string Resolve(LocText text)
        {
            return ModL10n.Resolve(text);
        }

        public string Date(DateTime date)
        {
            return IntelUi.DateText(date);
        }

        public string Ips(float ips)
        {
            try
            {
                return GameFormatter.FloatToIps(ips);
            }
            catch (Exception)
            {
                return Math.Round(ips).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Reads the public facts about the AI companies for the intelligence window (FR-9 item 2): the name, the bankruptcy, the segments with a CPU on sale, the
    /// share of the market, the last CPU, the reputation of the brand while <c>press-reviews</c> is on. Nothing else: the money and the projects of the rivals
    /// are not read at all (FR-9 item 6, FR-16 item 5). One walk over the CPUs of the world per call. Main thread only; a failure of one company leaves
    /// the facts of the others.
    /// </summary>
    internal sealed class IntelSource
    {
        private const string PressFeatureId = "press-reviews";

        private readonly List<GameCpu> _cpus = new List<GameCpu>(512);

        /// <summary>The facts of every company of <paramref name="rivals"/>, in the same order.</summary>
        public List<IntelCompanyFacts> Collect(List<AICompany> rivals)
        {
            var result = new List<IntelCompanyFacts>(rivals != null ? rivals.Count : 0);
            if (rivals == null) return result;

            var byCompany = new Dictionary<ICompany, IntelCompanyFacts>(rivals.Count);
            var lastSaveId = new Dictionary<int, int>();
            GameMarketData market = GameMarketData.Instance;
            bool reputation = ModFeatures.IsActive(PressFeatureId);

            foreach (AICompany rival in rivals)
            {
                if (ReferenceEquals(rival, null) || rival == null) continue;

                IntelCompanyFacts facts = new IntelCompanyFacts { UniqueId = rival.UniqueID, Name = NameOf(rival), IsBankrupt = rival.IsBankrupt };
                if (market != null)
                {
                    facts.Share = ShareOf(market, rival);
                    facts.HasShare = true;
                }
                if (reputation)
                {
                    facts.Reputation = ModReputation.Get(rival);
                    facts.HasReputation = true;
                }
                byCompany[rival] = facts;
                result.Add(facts);
            }

            try
            {
                Walk(byCompany, lastSaveId);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(RivalPersonalitiesFeature.FeatureId, "IntelSource/walk", e);
            }
            finally
            {
                _cpus.Clear();
            }
            return result;
        }

        private void Walk(Dictionary<ICompany, IntelCompanyFacts> byCompany, Dictionary<int, int> lastSaveId)
        {
            GameCpuProvider provider = GameCpuProvider.Instance;
            if (provider == null) return;
            provider.GetAllCpus(_cpus);

            for (int i = 0; i < _cpus.Count; i++)
            {
                GameCpu cpu = _cpus[i];
                if (cpu == null) continue;

                ICompany owner = cpu.Company;
                IntelCompanyFacts facts;
                if (ReferenceEquals(owner, null) || !byCompany.TryGetValue(owner, out facts)) continue;

                if (!cpu.IsRetired && cpu.SellOnMarket && cpu.Strategy != null)
                {
                    int target = cpu.Strategy.TargetMarket;
                    if (target >= 0 && target < facts.Segments.Length) facts.Segments[target] = true;
                }

                if (!cpu.IsReleased) continue;
                int previousId;
                bool later = !facts.HasLastCpu || cpu.Year > facts.LastCpuYear
                             || (cpu.Year == facts.LastCpuYear && lastSaveId.TryGetValue(facts.UniqueId, out previousId) && cpu.SaveID > previousId);
                if (!later) continue;

                facts.HasLastCpu = true;
                facts.LastCpuName = cpu.Name ?? string.Empty;
                facts.LastCpuYear = cpu.Year;
                facts.LastCpuIps = cpu.Ips;
                lastSaveId[facts.UniqueId] = cpu.SaveID;
            }
        }

        private static float ShareOf(GameMarketData market, AICompany rival)
        {
            try
            {
                float share = market.MarketShare(rival);
                return float.IsNaN(share) || share < 0f ? 0f : (share > 1f ? 1f : share);
            }
            catch (Exception)
            {
                return 0f;   // the cache of the market is not ready: the company has no share to show yet
            }
        }

        private static string NameOf(AICompany rival)
        {
            try
            {
                string name = rival.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
                // fall through to the key
            }
            return "ai:" + rival.UniqueID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
