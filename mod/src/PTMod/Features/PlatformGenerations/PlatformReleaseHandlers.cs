using System;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The handlers of the core's events (design, Component 7; FR-3, FR-4 item 6, FR-6): <c>ModEvents.CpuReleased</c> and
    /// <c>ModEvents.CpuTransferred</c>. The feature has no patch on <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c> - the core's one postfix (P20)
    /// raises the release once per CPU, never while a save loads, and those methods are reserved. The core calls the handlers through
    /// <c>ModHooks.Raise</c>: only while the feature is Enabled, each in a try/catch that counts a failure against the feature, so nothing here
    /// needs a guard of its own and an exception cannot reach the game. The handlers are subscribed in <c>Register</c>, once.
    /// </summary>
    internal sealed class PlatformReleaseHandlers
    {
        private readonly PlatformGenerationsFeature _feature;

        public PlatformReleaseHandlers(PlatformGenerationsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>
        /// A CPU was released (FR-3, FR-6). The fast-forward of a new game and a variant of an earlier design leave at once (the policy of the
        /// owner: neutral in the fast-forward, reconstructed at its end; a variant changes neither socket nor generation). Otherwise the release
        /// is booked in the chronology (a launch, an early adopter, the first CPU of the company on the socket and its penalty), the cache of the
        /// multipliers follows - the whole cache when the chronology changed, the CPU alone when it did not - and the player is told through the notifier.
        /// A CPU outside the generations changes nothing. The same code for the player and the AI.
        /// </summary>
        public void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            if (duringFastForward || isVariant) return;

            PlatformGenerationsFeature feature = _feature;
            if (!feature.CatalogReady || ReferenceEquals(cpu, null)) return;

            string companyKey = ModCompanies.KeyOf(owner);
            if (companyKey == null) return;

            SocketId socket;
            if (!GameAdapter.TryGetSocket(cpu, out socket)) return;

            GenerationDef gen = feature.Catalog.BySocket(socket);
            if (gen == null) return;

            GameDate today = GameAdapter.Today();
            if (!today.IsSet) return;

            ReleaseOutcome outcome = feature.Timeline.OnCpuReleased(companyKey, socket, gen, today, feature.InstalledBase, feature.Catalog, feature.Balance);

            // A launch, a new early adopter or a new socket changes the multipliers of other CPUs too (the adoption of the market, the bonus of
            // every CPU of the company in the generation): rebuild all, then make the entry of the new CPU.
            if (outcome.Announced || outcome.Launched || outcome.EarlyAdopter || outcome.NewSocket) feature.Provider.RebuildCache();
            feature.Provider.UpdateCpu(cpu);

            Tell(outcome, gen, socket, companyKey, feature);
        }

        /// <summary>
        /// A CPU moved to another company (FR-4 item 6; for example a takeover): its entry in the cache is made again for the new owner at once and
        /// the cached values of both companies are dropped. The installed base follows the CPU at the next monthly pass (D3). No chronology is
        /// written: the new owner has not released the CPU.
        /// </summary>
        public void OnCpuTransferred(ICompany from, ICompany to, GameCpu cpu)
        {
            PlatformGenerationsFeature feature = _feature;
            if (!feature.CatalogReady || ReferenceEquals(cpu, null)) return;

            feature.Provider.UpdateCpu(cpu, ModCompanies.KeyOf(from));
        }

        /// <summary>
        /// What a release tells (FR-2 item 3, FR-3, FR-6, FR-11): the player is told through the notifier - an announcement, a launch (by any
        /// company), and, for the player's own release only, the early adopter slot and the penalty - and the others through
        /// <see cref="PlatformEvents"/>, for every company: each fact once, in the order it happened.
        /// </summary>
        private static void Tell(ReleaseOutcome outcome, GenerationDef gen, SocketId socket, string companyKey, PlatformGenerationsFeature feature)
        {
            IPlatformNotifier notifier = feature.Notifier;
            bool player = string.Equals(companyKey, ModCompanies.PlayerKey, StringComparison.Ordinal);

            if (outcome.Announced)
            {
                notifier.Announced(gen);
                PlatformEvents.RaiseGenerationAnnounced(gen.id);
            }

            if (outcome.Launched)
            {
                notifier.Launched(gen, companyKey, player);
                PlatformEvents.RaiseGenerationLaunched(gen.id);
            }

            if (outcome.EarlyAdopter)
            {
                if (player) notifier.EarlyAdopter(gen, outcome.EarlySlot);
                PlatformEvents.RaiseEarlyAdopterGranted(companyKey, gen.id);
            }

            if (outcome.ChurnSeverity > 0)
            {
                if (player)
                {
                    PlatformBalance.RatingBalance rating = feature.Balance.Rating;
                    notifier.Churn(socket, rating.ChurnPenalty * outcome.ChurnSeverity, rating.ChurnDurationMonths, outcome.MainSocketKey, outcome.MainAgeMonths);
                }
                PlatformEvents.RaiseSocketChurnApplied(companyKey, socket.Key, outcome.ChurnSeverity);
            }
        }
    }
}
