using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameAI = ProcessorTycoon.CompanySystem.AI;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Turns the moves of the player into triggers (design.md, Component 7; FR-5 items 1, 4, 5 and 6): the game side of <see cref="TriggerEvaluator"/>. It
    /// reads the game, hands the facts over and keeps nothing but two small memories; the rules are the evaluator's (pure, tested) and the changes of
    /// the attitude are <see cref="TriggerRecorder"/>'s. The moves it hears:
    /// <list type="bullet">
    /// <item><c>ModEvents.CpuReleased</c> (<see cref="OnCpuReleased"/>, subscribed once in <c>Register</c>): <c>StrongLaunch</c> by the segment of
    /// <c>ModSegments.Classify</c>. The core does not raise it while a save loads; this class also drops a CPU released in the fast-forward and a
    /// variant. <c>CpuTransferred</c> is not a release and is not heard.</item>
    /// <item><c>Company.AddTechnology</c> (patch): <c>TechLead</c>, once for a technology, for the rivals that lack it.</item>
    /// <item><c>ContractManager.PromoteContractToActive</c> (patch): <c>OemLoss</c> for the rivals that offered a CPU for a contract that went to the
    /// player.</item>
    /// <item><c>BusinessContractManager.OnPlayerSignedContract</c> (event of the scene's manager): +15. It is subscribed again for every scene
    /// (<see cref="OnSceneReady"/>), never twice (<see cref="SingleSubscription{TSource}"/>), and the log tells how many subscriptions there are.</item>
    /// <item><c>BusinessContractManager.BreakContract</c> (patch): -25 when the player breaks a contract.</item>
    /// </list>
    /// Nothing is recorded while a save loads or the fast-forward runs. A failure goes to the caller (the patch or the handler reports it).
    /// </summary>
    internal sealed class PlayerActionMonitor
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly PersonalityRegistry _registry;
        private readonly TriggerEvaluator _evaluator;
        private readonly SingleSubscription<GameBusinessContracts> _signed;
        private readonly Action<GameBusinessContract> _onSigned;
        private readonly HashSet<int> _reportedTechnologies = new HashSet<int>();

        public PlayerActionMonitor(PersonalityRegistry registry, TriggerEvaluator evaluator)
        {
            _registry = registry;
            _evaluator = evaluator;
            _onSigned = OnPlayerSignedBusinessContract;
            _signed = new SingleSubscription<GameBusinessContracts>(Subscribe, Unsubscribe);
        }

        /// <summary>The subscriptions to <c>BusinessContractManager</c> that exist now (0 or 1) and how many were made (the log shows both).</summary>
        public int ContractSubscriptions
        {
            get { return _signed.Active; }
        }

        // ---- the scene ----

        /// <summary>
        /// The game is ready (<c>ModTicks.GameReady</c>), or the feature was switched on in a ready game: subscribes to the contract manager of this
        /// scene. Safe to call again: the same manager is not subscribed twice, a new one replaces the old.
        /// </summary>
        public void OnSceneReady()
        {
            GameBusinessContracts manager = GameBusinessContracts.Instance;
            bool made = _signed.Attach(manager != null ? manager : null);
            ModLog.Info(Feature, "triggers: BusinessContractManager.OnPlayerSignedContract " + (made ? "subscribed" : "kept")
                                 + " (active subscriptions: " + _signed.Active.ToString(CultureInfo.InvariantCulture)
                                 + ", made in total: " + _signed.Made.ToString(CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>The scene goes (or the feature is switched off): the handler leaves the manager, the memory of the technologies is dropped.</summary>
        public void Detach()
        {
            _signed.Detach();
            _reportedTechnologies.Clear();
        }

        // ---- the moves ----

        /// <summary><c>ModEvents.CpuReleased</c>: a CPU was released. Only the player's own new CPUs matter (FR-5 item 1).</summary>
        public void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            if (cpu == null || isVariant || duringFastForward || !ModCompanies.IsPlayer(owner)) return;
            if (_evaluator.IsBlocked) return;

            Segment segment = ModSegments.Classify(cpu);
            var launch = new LaunchFacts
            {
                ByPlayer = true,
                IsVariant = isVariant,
                DuringFastForward = duringFastForward,
                Segment = segment,
                Ips = cpu.Ips,
                CpuName = cpu.Name,
            };

            var facts = new List<RivalFacts>();
            if (segment != Segment.Any)
            {
                foreach (AICompany rival in _registry.Rivals())
                {
                    if (rival.IsFoundry || rival.IsBankrupt) continue;
                    facts.Add(LaunchFactsOf(rival, segment));
                }
            }
            _evaluator.OnLaunch(launch, facts);
        }

        /// <summary><c>Company.AddTechnology</c> of the player (a postfix): <c>TechLead</c> once for each technology (FR-5 item 5).</summary>
        public void OnPlayerTechnology(GameTechnology technology)
        {
            if (technology == null || _evaluator.IsBlocked) return;

            string type = technology.Type.ToString();
            if (!TriggerRules.IsTechLeadType(type, _evaluator.TechLeadTypes)) return;
            if (!_reportedTechnologies.Add(technology.ID)) return;

            var facts = new List<RivalFacts>();
            foreach (AICompany rival in _registry.Rivals())
            {
                if (rival.IsFoundry || rival.IsBankrupt) continue;

                bool lacks;
                try
                {
                    lacks = !rival.ResearchSector.IsUnlocked(technology);
                }
                catch (Exception e)
                {
                    ModLog.Debug(Feature, "triggers: cannot tell whether ai:" + rival.UniqueID.ToString(CultureInfo.InvariantCulture)
                                          + " has researched " + type + " (" + e.GetType().Name + "); no TechLead for it");
                    continue;
                }
                facts.Add(new RivalFacts { UniqueId = rival.UniqueID, LacksTechnology = lacks });
            }
            _evaluator.OnTechnology(type, NameOf(technology), facts);
        }

        /// <summary><c>ContractManager.PromoteContractToActive</c> (a postfix): an OEM contract went active (FR-5 item 4).</summary>
        public void OnOemContractActivated(GameContract contract)
        {
            if (contract == null || contract.OffersIDs == null || _evaluator.IsBlocked) return;

            GameCpuProvider provider = GameCpuProvider.Instance;
            if (provider == null) return;

            GameCpu chosen = provider.GetCpu(contract.ChosenCpuID);
            if (chosen == null || !ModCompanies.IsPlayer(chosen.Company)) return;

            var offerers = new List<RivalFacts>();
            var seen = new HashSet<int>();
            for (int i = 0; i < contract.OffersIDs.Count; i++)
            {
                GameCpu offer = provider.GetCpu(contract.OffersIDs[i]);
                if (offer == null || ReferenceEquals(offer, chosen)) continue;

                var rival = offer.Company as AICompany;
                if (rival == null || !seen.Add(rival.UniqueID)) continue;

                int market = offer.Strategy != null ? offer.Strategy.TargetMarket : -1;
                offerers.Add(new RivalFacts
                {
                    UniqueId = rival.UniqueID,
                    IsFoundry = rival.IsFoundry,
                    IsBankrupt = rival.IsBankrupt,
                    OfferSegment = market >= 0 && market < ShareCalculator.SegmentCount ? (Segment)market : Segment.Any,
                });
            }
            _evaluator.OnOemContractWon(offerers, contract.Company);
        }

        /// <summary><c>BusinessContractManager.OnPlayerSignedContract</c>: +15 for the other side (FR-5 item 6). Not while a save loads (<c>AddContract</c> raises it too).</summary>
        private void OnPlayerSignedBusinessContract(GameBusinessContract contract)
        {
            ModHooks.Guard(Feature, "triggers/businessSigned", () =>
            {
                if (contract == null || !ModFeatures.IsActive(Feature) || _evaluator.IsBlocked) return;

                AICompany other = OtherSide(contract);
                if (other != null) _evaluator.OnBusinessContractSigned(other.UniqueID);
            });
        }

        /// <summary><c>BusinessContractManager.BreakContract</c> (a postfix): -25 when the breaker is the player and the other side an AI company.</summary>
        public void OnBusinessContractBroken(ICompany breaker, ICompany other)
        {
            if (_evaluator.IsBlocked || !ModCompanies.IsPlayer(breaker)) return;

            var rival = other as AICompany;
            if (rival == null) return;
            _evaluator.OnBusinessContractBroken(rival.UniqueID);
        }

        // ---- internals ----

        /// <summary>The facts about a rival for a launch in <paramref name="segment"/>: is it active there and how good is the best CPU it sells there.</summary>
        private static RivalFacts LaunchFactsOf(AICompany rival, Segment segment)
        {
            var facts = new RivalFacts { UniqueId = rival.UniqueID, IsFoundry = rival.IsFoundry, IsBankrupt = rival.IsBankrupt };
            List<GameCpu> cpus = CpusOf(rival);
            if (cpus == null) return facts;

            int target = (int)segment;
            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (cpu == null || cpu.Strategy == null || cpu.Strategy.TargetMarket != target) continue;

                facts.ActiveInSegment = true;
                if (cpu.IsRetired || !cpu.SellOnMarket) continue;
                if (!facts.SellsCpuInSegment || cpu.Ips > facts.BestIps)
                {
                    facts.SellsCpuInSegment = true;
                    facts.BestIps = cpu.Ips;
                }
            }
            return facts;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<GameCpu> CpusOf(AICompany rival)
        {
            GameAI ai = rival.GetComponent<GameAI>();
            return ai != null ? ai.GetCpus() : null;
        }

        /// <summary>The AI company on the other side of a business contract of the player; null when there is none.</summary>
        private static AICompany OtherSide(GameBusinessContract contract)
        {
            ICompany provider = contract.Provider;
            ICompany client = contract.Client;
            if (ModCompanies.IsPlayer(provider)) return client as AICompany;
            if (ModCompanies.IsPlayer(client)) return provider as AICompany;
            return null;
        }

        private static string NameOf(GameTechnology technology)
        {
            try
            {
                return technology.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Subscribe(GameBusinessContracts manager)
        {
            manager.OnPlayerSignedContract += _onSigned;
        }

        private void Unsubscribe(GameBusinessContracts manager)
        {
            manager.OnPlayerSignedContract -= _onSigned;
        }
    }
}
