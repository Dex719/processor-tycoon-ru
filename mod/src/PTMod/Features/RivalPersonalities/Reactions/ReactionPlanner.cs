using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameAI = ProcessorTycoon.CompanySystem.AI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The planner of the reactions in the game (design.md, Component 8; FR-6, FR-7 items 1, 8, 9): the game side of <see cref="ReactionPlannerCore"/>.
    /// It reads the game for the core (the date, the difficulty, whether the event system works, the bankruptcy of a rival, its CPUs, the delivery of
    /// a mail) and gives it the intelligence mail (<see cref="IntelMail"/>), the executor and the random stream <c>ModRandom.Stream("rival-personalities")</c>.
    /// The feature calls it from the trigger recorder (<see cref="OnTrigger"/>), the mail event (<see cref="OnMailDelivered"/>) and the day tick
    /// (<see cref="DailyTick"/>). Main thread only; the callers guard the calls.
    /// </summary>
    internal sealed class ReactionPlanner : IPlannerEnvironment
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly RivalPersonalitiesFeature _feature;
        private readonly ReactionPlannerCore _core;

        public ReactionPlanner(RivalPersonalitiesFeature feature, PersonalityBook book, AttitudeLedger ledger, ReactionBook reactions,
                               IReactionLauncher launcher, IIntelOutput intel)
        {
            _feature = feature;
            _core = new ReactionPlannerCore(() => feature.Catalog, book, ledger, reactions, this, intel, launcher,
                () => ModRandom.Stream(Feature),
                line => ModLog.Info(Feature, "reactions: " + line),
                line => ModLog.Debug(Feature, "reactions: " + line));
        }

        /// <summary>The rules (shared with the executor and the tests).</summary>
        public ReactionPlannerCore Core
        {
            get { return _core; }
        }

        // ---- what the feature calls ----

        /// <summary><c>TriggerRecorder.TriggerRaised</c>: a move of the player was recorded for a rival.</summary>
        public PlanResult OnTrigger(PlayerTrigger trigger)
        {
            return _core.OnTrigger(trigger);
        }

        /// <summary>The debug page: plans a reaction at once, without a roll (a warning of one day).</summary>
        public PlanResult PlanManual(int uniqueId, ReactionType type)
        {
            return _core.PlanManual(uniqueId, type, Segment.Any);
        }

        /// <summary><c>ModMail.Delivered</c>: sets the day a planned reaction is carried out. Mails of other features are not ours.</summary>
        public bool OnMailDelivered(ModMailRecord record)
        {
            if (record == null || !string.Equals(record.FeatureId, Feature, StringComparison.Ordinal)) return false;
            return _core.OnMailDelivered(record.Tag, record.DeliveredOn);
        }

        /// <summary><c>TickPhase.DayPassed</c>: carries out the reactions that are due, drops the ones that cannot go on.</summary>
        public TickReport DailyTick()
        {
            TickReport report = _core.DailyTick();
            if (report.Launched > 0 || report.Cancelled > 0 || report.Synced > 0)
            {
                ModLog.Debug(Feature, "reactions: day tick - " + report.Launched.ToString(CultureInfo.InvariantCulture) + " carried out, "
                                      + report.Cancelled.ToString(CultureInfo.InvariantCulture) + " dropped, "
                                      + report.Synced.ToString(CultureInfo.InvariantCulture) + " dated from a delivery");
            }
            return report;
        }

        /// <summary><c>TickPhase.YearPassed</c>: the count of the hostile reactions of the year starts again.</summary>
        public void OnYearPassed()
        {
            // the book rolls the year by itself on every call; this makes it happen on the first day of the year, as the design says
            DateTime today = ModTicks.Today;
            if (today != default(DateTime) && _feature.Reactions.RollYear(today)) ModLog.Debug(Feature, "reactions: the count of the year " + today.Year.ToString(CultureInfo.InvariantCulture) + " starts at 0");
        }

        public int CancelFor(int uniqueId, string reason)
        {
            return _core.CancelFor(uniqueId, reason);
        }

        public int CancelBankrupt()
        {
            return _core.CancelBankrupt();
        }

        // ---- IPlannerEnvironment ----

        public DateTime Today
        {
            get { return ModTicks.Today; }
        }

        public int Difficulty
        {
            get
            {
                GamePlayer player = GamePlayer.Instance;
                return player != null ? player.DifficultyLevel : 2;
            }
        }

        public bool EventSystemAvailable
        {
            get { return EventSystemApi.IsActive; }
        }

        public bool ForceReactions
        {
            get { return RivalSettings.ForceReactions; }
        }

        public bool Blocked
        {
            get { return ModTicks.IsLoading || ModTicks.IsFastForwarding || !ModTicks.IsGameReady; }
        }

        public bool PlayerResearchUnderfunded
        {
            get { return IsResearchUnderfunded(); }
        }

        public bool IsBankrupt(int uniqueId)
        {
            return AttitudeNotifier.IsBankrupt(uniqueId);
        }

        public bool SellsCpuIn(int uniqueId, Segment segment)
        {
            if (segment < Segment.HighEnd || segment > Segment.Industries) return false;
            List<GameCpu> cpus = CpusOf(uniqueId);
            if (cpus == null) return false;

            int market = (int)segment;
            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (IsOnMarket(cpu) && cpu.Strategy.TargetMarket == market) return true;
            }
            return false;
        }

        public Segment MainSegmentOf(int uniqueId)
        {
            List<GameCpu> cpus = CpusOf(uniqueId);
            if (cpus == null) return Segment.Any;

            Segment best = Segment.Any;
            float bestIps = float.MinValue;
            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (!IsOnMarket(cpu)) continue;
                int market = cpu.Strategy.TargetMarket;
                if (market < 0 || market > 3 || cpu.Ips <= bestIps) continue;
                best = (Segment)market;
                bestIps = cpu.Ips;
            }
            return best;
        }

        public DateTime? MailDeliveredOn(int mailId, string tag)
        {
            ModMailRecord record = mailId > 0 ? ModMail.Get(mailId) : null;
            if (record == null || !string.Equals(record.Tag, tag, StringComparison.Ordinal)) record = ModMail.Find(Feature, tag);
            return record != null ? record.DeliveredOn : null;
        }

        // ---- the game ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsResearchUnderfunded()
        {
            try
            {
                GamePlayer player = GamePlayer.Instance;
                ICompany company = player != null ? player.Company : null;
                var sector = company != null ? company.ResearchSector : null;
                return sector != null && sector.Funding < 1f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsOnMarket(GameCpu cpu)
        {
            return cpu != null && cpu.Strategy != null && !cpu.IsRetired && cpu.SellOnMarket;
        }

        /// <summary>The CPUs of the rival (<c>AI.GetCpus()</c>), or null when the company or its AI component is not there. The list belongs to the game: read only.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<GameCpu> CpusOf(int uniqueId)
        {
            AICompany rival = RivalOf(uniqueId);
            if (ReferenceEquals(rival, null) || rival == null) return null;
            GameAI ai = rival.GetComponent<GameAI>();
            return ai != null ? ai.GetCpus() : null;
        }

        private AICompany RivalOf(int uniqueId)
        {
            string key = CompanyKey.ForAi(uniqueId);
            var company = key != null ? ModCompanies.Resolve(key) as AICompany : null;
            if (!ReferenceEquals(company, null) && company != null) return company;

            foreach (AICompany rival in _feature.Registry.Rivals())
            {
                if (rival.UniqueID == uniqueId) return rival;
            }
            return null;
        }
    }
}
