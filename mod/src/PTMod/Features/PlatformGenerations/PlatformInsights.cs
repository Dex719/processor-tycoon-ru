using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// What the feature knows about one CPU, for the window "Платформы" and for the features next to it (FR-10 item 2; design, Component 6): the
    /// parts of the rating multiplier, what the OEM clients think of the CPU's platform, the generation of its socket. The multiplier itself and the
    /// reasons for it - "Платформа: база, ранний переход, смена сокета", "OEM предпочитает платформу ..." - are the core's: the providers of the
    /// feature are registered with <c>ModModifiers</c> with their reasons, and <c>ModModifiers.Explain</c> lists them. The feature has no
    /// <c>Explain</c> of its own; this class only breaks the multiplier into <c>B_ib</c>, <c>B_early</c> and <c>P_churn</c>, and it does so lazily, when
    /// asked (the window opens, a tooltip is built), never in the market tick - the hot path of the provider is a lookup in a cache.
    /// <para>
    /// The numbers are made by the same engine with the same arguments as the provider's cache (<see cref="PlatformEffectEngine"/>), so they agree
    /// with what the game uses. A CPU outside the generations, a feature that is off or not ready, give the neutral answer (no parts, ×1.00).
    /// The switches "эффекты на рейтинг" and "эффекты на контракты" do not change these answers: they say what the CPU would get with the
    /// effect on; whether it is on is <see cref="PlatformSettings"/>'s.
    /// </para>
    /// </summary>
    public static class PlatformInsights
    {
        private static readonly PlatformEffectEngine Engine = new PlatformEffectEngine();

        /// <summary>
        /// The components of the rating multiplier of the CPU: <c>Bib</c>, <c>Bearly</c>, <c>Pchurn</c>, the totals for the consumer segments and for
        /// "Промышленность", the share of the installed base behind them. Neutral (parts 0, totals 1) when the CPU is outside the generations.
        /// </summary>
        public static RatingBreakdown RatingOf(GameCpu cpu)
        {
            PlatformGenerationsFeature feature = Ready();
            if (feature == null) return Neutral();

            SocketId socket;
            string company;
            GenerationDef gen = Locate(feature, cpu, out socket, out company);
            if (gen == null) return Neutral();

            return Engine.RatingFor(company, socket, gen.id, feature.InstalledBase, feature.Timeline, GameAdapter.Today(), feature.Balance);
        }

        /// <summary>
        /// What the OEM clients make of the CPU's platform: in the "per contract" mode how many of the contracts on offer prefer a newer platform and
        /// the mean multiplier over them, and the "market" multiplier. Neutral when the CPU is outside the generations. Reads the contracts on offer
        /// now, so ask when the window is built, not every frame.
        /// </summary>
        public static OemView OemOf(GameCpu cpu)
        {
            PlatformGenerationsFeature feature = Ready();
            if (feature == null) return new OemView();

            SocketId socket;
            string company;
            GenerationDef gen = Locate(feature, cpu, out socket, out company);
            if (gen == null) return new OemView { PerContract = feature.Balance.Oem.Mode == OemMode.Auto };

            RatingBreakdown rating = Engine.RatingFor(company, socket, gen.id, feature.InstalledBase, feature.Timeline, GameAdapter.Today(), feature.Balance);
            var effect = new EffectCpu
            {
                GenOrder = gen.order,
                GenId = gen.id,
                FullPlatform = feature.Catalog.IsFullPlatform(gen, GameAdapter.MemoryNameOf(cpu)),
                EffShare = rating.EffShare,
            };

            var contracts = new List<OemContractInput>();
            GameAdapter.CollectOemContracts(contracts);
            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(feature.Catalog, feature.Timeline, feature.InstalledBase, GameAdapter.Today(), null);
            return OemViewBuilder.Build(effect, contracts, feature.Preferences, inputs, feature.Balance);
        }

        /// <summary>The generation of the CPU's socket (for <c>press-reviews</c> and the windows); null when the CPU is outside the generations or the feature is not ready.</summary>
        public static GenerationDef GenerationOf(GameCpu cpu)
        {
            PlatformGenerationsFeature feature = Ready();
            if (feature == null) return null;

            SocketId socket;
            string company;
            return Locate(feature, cpu, out socket, out company);
        }

        // ---- internals ----

        /// <summary>The running feature with its generations loaded; null otherwise.</summary>
        private static PlatformGenerationsFeature Ready()
        {
            PlatformGenerationsFeature feature = PlatformGenerationsFeature.Instance;
            if (feature == null || !feature.CatalogReady || !ModFeatures.IsActive(PlatformGenerationsFeature.FeatureId)) return null;
            return feature;
        }

        /// <summary>The generation, the socket and the owner of a CPU; null generation when any of them cannot be told. Never throws.</summary>
        private static GenerationDef Locate(PlatformGenerationsFeature feature, GameCpu cpu, out SocketId socket, out string company)
        {
            socket = default(SocketId);
            company = null;
            if (ReferenceEquals(cpu, null)) return null;

            try
            {
                company = GameAdapter.CompanyKeyOf(cpu);
                if (company == null || !GameAdapter.TryGetSocket(cpu, out socket)) return null;
                return feature.Catalog.BySocket(socket);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(PlatformGenerationsFeature.FeatureId, "insights/locate", ex);
                return null;
            }
        }

        private static RatingBreakdown Neutral()
        {
            return new RatingBreakdown { ConsumerMult = 1f, IndustriesMult = 1f };
        }
    }
}
