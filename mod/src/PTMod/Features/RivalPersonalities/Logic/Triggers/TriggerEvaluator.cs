using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What the game side knows about one rival at the moment of a move of the player (the facts <see cref="TriggerEvaluator"/> judges). The game side
    /// fills only the fields that the move needs; the rest keep their defaults.
    /// </summary>
    public sealed class RivalFacts
    {
        /// <summary>The <c>UniqueID</c> of the rival.</summary>
        public int UniqueId;

        /// <summary>The rival makes products for others and not for the market: it has no part in the triggers of the products (FR-5 item 1).</summary>
        public bool IsFoundry;

        public bool IsBankrupt;

        /// <summary>
        /// <c>StrongLaunch</c>: the rival is active in the segment of the launch (it has, or had, a CPU aimed at it). A rival that is not active there
        /// does not mind.
        /// </summary>
        public bool ActiveInSegment;

        /// <summary><c>StrongLaunch</c>: the rival sells a CPU of that segment on the market now.</summary>
        public bool SellsCpuInSegment;

        /// <summary><c>StrongLaunch</c>: the performance of the best of those CPUs.</summary>
        public float BestIps;

        /// <summary><c>TechLead</c>: the rival has not researched the technology of the player.</summary>
        public bool LacksTechnology;

        /// <summary><c>OemLoss</c>: the segment of the rival's CPU that took part in the lost tender; <see cref="Segment.Any"/> when it is not known.</summary>
        public Segment OfferSegment = Segment.Any;
    }

    /// <summary>A CPU that the player has released (the facts of <c>ModEvents.CpuReleased</c> for <see cref="TriggerEvaluator.OnLaunch"/>).</summary>
    public sealed class LaunchFacts
    {
        /// <summary>The CPU belongs to the player's company.</summary>
        public bool ByPlayer;

        /// <summary>The CPU is a variant of an earlier design (<c>cpu.Base != null</c>): not a new product.</summary>
        public bool IsVariant;

        /// <summary>The CPU was released during the fast-forward of a new game.</summary>
        public bool DuringFastForward;

        /// <summary>The segment of the CPU (<c>ModSegments.Classify</c>); <see cref="Segment.Any"/> when the market cannot say.</summary>
        public Segment Segment = Segment.Any;

        public float Ips;
        public string CpuName;
    }

    /// <summary>
    /// The decisions about the moves of the player (FR-5 items 1, 4, 5, 6; design.md Component 7): which rivals notice a launch, a technology, a lost
    /// tender, a contract. Pure: the game side (<c>PlayerActionMonitor</c>) reads the game and hands the facts over; the decisions go through
    /// <see cref="TriggerRecorder"/>, which changes the attitudes and tells the planner.
    /// <list type="bullet">
    /// <item><b>Launch.</b> Only the player's own new CPUs, no variants, nothing during the fast-forward, no segment <see cref="Segment.Any"/> (a warning
    /// is logged). A rival that is no foundry, not bankrupt and active in the segment gets <c>StrongLaunch</c> when the CPU is at least
    /// <c>strongLaunchIpsRatio</c> (1.15) times its best.</item>
    /// <item><b>Technology.</b> <c>TechLead</c> for the rivals that lack it, when its type is in <c>techLeadTypes</c>.</item>
    /// <item><b>Tender.</b> <c>OemLoss</c> for each rival that offered a CPU.</item>
    /// <item><b>Contracts.</b> +15 for a signed one, -25 for one that the player broke.</item>
    /// </list>
    /// Every method returns how many rivals were affected and does nothing while the recorder is blocked.
    /// </summary>
    public sealed class TriggerEvaluator
    {
        private readonly TriggerRecorder _recorder;
        private readonly Func<RivalConfig> _config;
        private readonly Action<string> _warn;

        public TriggerEvaluator(TriggerRecorder recorder, Func<RivalConfig> config, Action<string> warn = null)
        {
            _recorder = recorder;
            _config = config;
            _warn = warn;
        }

        /// <summary>No trigger may be recorded now (a save loads, the fast-forward runs, the game is not ready): the game side need not collect facts.</summary>
        public bool IsBlocked
        {
            get { return _recorder.IsBlocked; }
        }

        /// <summary>The technology types the rivals care about (<c>triggerThresholds.techLeadTypes</c>).</summary>
        public IList<string> TechLeadTypes
        {
            get { return Thresholds.techLeadTypes; }
        }

        private RivalConfig.TriggerThresholdSettings Thresholds
        {
            get
            {
                RivalConfig config = _config != null ? _config() : null;
                return (config ?? RivalConfig.Defaults()).triggerThresholds;
            }
        }

        /// <summary><c>ModEvents.CpuReleased</c> (FR-5 item 1): <c>StrongLaunch</c> for the rivals that the new CPU of the player clearly beats.</summary>
        public int OnLaunch(LaunchFacts launch, IList<RivalFacts> rivals)
        {
            if (launch == null || rivals == null) return 0;
            if (!launch.ByPlayer || launch.IsVariant || launch.DuringFastForward || _recorder.IsBlocked) return 0;
            if (launch.Segment == Segment.Any)
            {
                Warn("StrongLaunch: the segment of '" + (launch.CpuName ?? "?") + "' is not known (the market has no budgets yet), no trigger");
                return 0;
            }

            float ratio = Thresholds.strongLaunchIpsRatio;
            int count = 0;
            for (int i = 0; i < rivals.Count; i++)
            {
                RivalFacts rival = rivals[i];
                if (rival == null || rival.IsFoundry || rival.IsBankrupt || !rival.ActiveInSegment) continue;
                if (!TriggerRules.IsStrongLaunch(launch.Ips, rival.SellsCpuInSegment, rival.BestIps, ratio)) continue;
                if (_recorder.Record(TriggerType.StrongLaunch, rival.UniqueId, launch.Segment, launch.CpuName)) count++;
            }
            return count;
        }

        /// <summary>
        /// <c>Company.AddTechnology</c> of the player (FR-5 item 5): <c>TechLead</c> for each rival that has not researched it, when the type is
        /// one of <c>techLeadTypes</c>. A foundry is not a rival in technology (it makes the chips of others) and is left out.
        /// </summary>
        public int OnTechnology(string technologyType, string technologyName, IList<RivalFacts> rivals)
        {
            if (rivals == null || _recorder.IsBlocked) return 0;
            if (!TriggerRules.IsTechLeadType(technologyType, Thresholds.techLeadTypes)) return 0;

            int count = 0;
            for (int i = 0; i < rivals.Count; i++)
            {
                RivalFacts rival = rivals[i];
                if (rival == null || rival.IsFoundry || rival.IsBankrupt || !rival.LacksTechnology) continue;
                if (_recorder.Record(TriggerType.TechLead, rival.UniqueId, Segment.Any, technologyName)) count++;
            }
            return count;
        }

        /// <summary>
        /// An OEM contract went active with a CPU of the player (FR-5 item 4): <c>OemLoss</c> for each rival that offered a CPU for it
        /// (<paramref name="offerers"/> holds each rival once). <paramref name="customer"/> is the name of the customer for the log.
        /// </summary>
        public int OnOemContractWon(IList<RivalFacts> offerers, string customer)
        {
            if (offerers == null || _recorder.IsBlocked) return 0;

            int count = 0;
            for (int i = 0; i < offerers.Count; i++)
            {
                RivalFacts rival = offerers[i];
                if (rival == null || rival.IsFoundry || rival.IsBankrupt) continue;
                if (_recorder.Record(TriggerType.OemLoss, rival.UniqueId, rival.OfferSegment, customer)) count++;
            }
            return count;
        }

        /// <summary>The player signed a business contract with the rival (FR-5 item 6): +15.</summary>
        public bool OnBusinessContractSigned(int uniqueId)
        {
            return _recorder.RecordContract(ContractMove.Signed, uniqueId);
        }

        /// <summary>The player broke a business contract with the rival (FR-5 item 6): -25.</summary>
        public bool OnBusinessContractBroken(int uniqueId)
        {
            return _recorder.RecordContract(ContractMove.BrokenByPlayer, uniqueId);
        }

        private void Warn(string line)
        {
            if (_warn != null) _warn(line);
        }
    }
}
