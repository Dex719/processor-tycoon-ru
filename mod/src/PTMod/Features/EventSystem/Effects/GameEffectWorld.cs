using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <see cref="IEffectWorld"/> over the running game: the money of <c>ICompany</c> (through <see cref="GameFacade"/>), the popularity of the CPUs on the market
    /// (<c>CpuBase.Popularities</c>), the current research (<c>ResearchSector.Research.Progress</c>), <c>ModReputation</c> and <c>ModMail</c>. A company key
    /// that resolves to nothing reads as "not there"; <c>global</c> means every company that is not bankrupt. Main thread only.
    /// </summary>
    internal sealed class GameEffectWorld : IEffectWorld
    {
        private const string FeatureId = EventSystemApi.FeatureId;

        /// <summary>The order of the segments in <c>CpuBase.Popularities</c>.</summary>
        private static readonly string[] Segments = { "highend", "midrange", "lowend", "industries" };

        private readonly GameFacade _game;
        private readonly CompanyDirectory _directory;

        public GameEffectWorld(GameFacade game, CompanyDirectory directory)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (directory == null) throw new ArgumentNullException("directory");
            _game = game;
            _directory = directory;
        }

        public float Inflation
        {
            get { return _game.Inflation; }
        }

        public float QuarterlyRevenue(string companyKey)
        {
            ICompany company = Company(companyKey);
            return company == null ? 0f : _game.QuarterlyRevenue(company);
        }

        public bool Spend(string companyKey, float amount, string origin)
        {
            ICompany company = Company(companyKey);
            if (company == null) return false;

            ExpenseOrigin value;
            if (string.IsNullOrEmpty(origin) || !Enum.TryParse(origin, false, out value)) value = ExpenseOrigin.Fines;
            _game.Spend(company, amount, value);
            return true;
        }

        public bool Earn(string companyKey, float amount, string origin)
        {
            ICompany company = Company(companyKey);
            if (company == null) return false;

            RevenueOrigin value;
            if (string.IsNullOrEmpty(origin) || !Enum.TryParse(origin, false, out value)) value = RevenueOrigin.Interest;
            _game.Earn(company, amount, value);
            return true;
        }

        public int AddPopularity(string companyKey, float delta, string cpuSelector, string segment)
        {
            int segmentIndex = Array.IndexOf(Segments, (segment ?? "all").ToLowerInvariant());
            bool newestOnly = string.Equals(cpuSelector, "Newest", StringComparison.OrdinalIgnoreCase);
            int changed = 0;

            foreach (ICompany company in Companies(companyKey))
            {
                IList<Cpu> cpus = _game.MarketCpus(company);
                if (newestOnly) cpus = Newest(cpus);
                foreach (Cpu cpu in cpus)
                {
                    List<float> popularities = cpu != null ? cpu.Popularities : null;
                    if (popularities == null || popularities.Count == 0) continue;
                    for (int k = 0; k < popularities.Count; k++)
                    {
                        if (segmentIndex >= 0 && k != segmentIndex) continue;
                        popularities[k] = EffectMath.Popularity(popularities[k], delta);
                    }
                    changed++;
                }
            }
            return changed;
        }

        public bool AddResearchProgress(string companyKey, float delta)
        {
            ICompany company = Company(companyKey);
            if (company == null) return false;

            ResearchSector.Research research = _game.CurrentResearch(company);
            if (research == null) return false;
            research.Progress = EffectMath.ResearchProgress(research.Progress, delta);
            return true;
        }

        public bool AddReputation(string companyKey, float delta, string reasonKey)
        {
            bool any = false;
            foreach (ICompany company in Companies(companyKey))
            {
                ModReputation.Add(company, delta, reasonKey);
                any = true;
            }
            return any;
        }

        public int SendMail(string titleKey, string textKey, string tag, string[] args)
        {
            return ModMail.Send(new ModMailMessage
            {
                FeatureId = FeatureId,
                Tag = tag,
                TitleKey = titleKey,
                BodyKey = textKey,
                Args = args ?? new string[0],
                Priority = MailPriority.Normal,
                TimeoutDays = 0,
            });
        }

        // ---- helpers ----

        private ICompany Company(string companyKey)
        {
            return CompanyKey.IsValid(companyKey) && _directory.IsAlive(companyKey) ? _directory.Resolve(companyKey) : null;
        }

        private IEnumerable<ICompany> Companies(string companyKey)
        {
            if (!string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal))
            {
                ICompany company = Company(companyKey);
                if (company != null) yield return company;
                yield break;
            }

            foreach (CompanyRef entry in _directory.All)
            {
                if (entry.Company != null) yield return entry.Company;
            }
        }

        private static IList<Cpu> Newest(IList<Cpu> cpus)
        {
            Cpu newest = null;
            foreach (Cpu cpu in cpus)
            {
                if (cpu != null && (newest == null || cpu.Year > newest.Year)) newest = cpu;
            }
            return newest == null ? (IList<Cpu>)new Cpu[0] : new[] { newest };
        }
    }

    /// <summary>The aggregator's sink over <c>ModModifiers</c>: one reasoned provider of the feature per channel, invalidation by company or for all.</summary>
    internal sealed class ModModifiersSink : IModifierChannelSink
    {
        private readonly Dictionary<string, int> _registrations = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>How many times a provider was registered per channel (the self-check wants one each); a copy.</summary>
        public IDictionary<string, int> Registrations
        {
            get { return new Dictionary<string, int>(_registrations, StringComparer.Ordinal); }
        }

        public void RegisterProvider(string channel, Func<string, ProviderResult> provider)
        {
            int count;
            _registrations.TryGetValue(channel, out count);
            _registrations[channel] = count + 1;
            ModModifiers.RegisterReasonedProvider(EventSystemApi.FeatureId, channel, ctx => provider(ctx.CompanyKey));
        }

        public void Invalidate(string channel, string companyKey)
        {
            if (companyKey == null) ModModifiers.Invalidate(channel);
            else ModModifiers.Invalidate(channel, companyKey);
        }
    }

    /// <summary>The nine standard effect types of the engine, registered through the API like the types of any pack.</summary>
    internal static class StandardEffects
    {
        public static IEffectHandlerPort[] Create(IEffectWorld world, IChainHost chain)
        {
            return new IEffectHandlerPort[]
            {
                new MoneyEffect(world),
                new ModifierTimedEffect(),
                new FactoryOutputEffect(),
                new MarketDemandEffect(),
                new PopularityEffect(world),
                new ReputationEffect(world),
                new ResearchProgressEffect(world),
                new SendMailEffect(world),
                new FireEventEffect(chain),
            };
        }

        public static void Register(IEffectWorld world, IChainHost chain)
        {
            foreach (IEffectHandlerPort port in Create(world, chain)) EventEffects.Register(port.Type, StandardEffectHandler.For(port));
        }
    }
}
