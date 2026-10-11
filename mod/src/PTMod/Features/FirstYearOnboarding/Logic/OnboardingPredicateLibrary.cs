using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The facts about the player's game that the predicates <c>onb.*</c> need and the core's predicates do not give (design.md, table of
    /// predicates). The game layer answers them from the public state of the game (<c>GameOnboardingFacts</c>); the tests answer from a fake.
    /// All of them are about the player's company and are cheap enough for the 2 Hz poll. A fact that cannot be read now is false.
    /// </summary>
    public interface IOnboardingFacts
    {
        /// <summary>The player has a CPU design project in progress (<c>TimerScheduler</c>: a project of the player with <c>GetCpuProject() != null</c>).</summary>
        bool CpuProjectActive();

        /// <summary>The player has a factory expansion project in progress (<c>GetFactoryExpansionProject() != null</c>).</summary>
        bool FactoryProjectActive();

        /// <summary>A CPU of the player was produced on the last game day (<c>ProducedLastDay &gt; 0</c>).</summary>
        bool ProducedToday();

        /// <summary>A CPU of the player was sold on the last game day (<c>SoldLastDay &gt; 0</c>).</summary>
        bool SoldToday();

        /// <summary>On the last game day the player's CPUs missed more sales than they made (<c>sum MissedSalesLastDay &gt; sum SoldLastDay</c>).</summary>
        bool MissedOverSold();

        /// <summary>The player has a released CPU that is not retired and is old by the game's own measure (<c>Cpu.IsOld(-1, -1)</c>).</summary>
        bool AnyOldCpu();

        /// <summary>At least one OEM contract is open for offers (<c>ContractManager.GetAvailableContracts().Count &gt; 0</c>).</summary>
        bool OemAvailable();

        /// <summary>The player switched on the automatic sending of offers (<c>Player.AutomateContracts</c>).</summary>
        bool Automate();

        /// <summary>The player's company offers foundry services (<c>Divisions.FoundryServices</c>).</summary>
        bool FoundryServices();

        /// <summary>
        /// The step with this id is closed in the tutorial of this game (the state of the machine): done, or skipped by the player, so that a skipped
        /// step does not hold back the steps that wait for it. False for an unknown step and when there is no tutorial.
        /// </summary>
        bool IsStepDone(string stepId);
    }

    /// <summary>
    /// The twelve predicates <c>onb.*</c> of the tutorial (design.md, table of predicates; FR-4): names, parameters and meaning, over
    /// <see cref="IOnboardingFacts"/>. The game layer registers them with <c>ModConditions.Register</c>; the tests register them in a
    /// <see cref="PredicateRegistry"/> to parse the shipped <c>steps.json</c>. The date predicates read the date of the condition context
    /// (<c>ModTicks.Today</c> in the game), not a fact. The other predicates come from the core as they are (<c>researchActive</c>,
    /// <c>cpuCountAtLeast</c>, <c>moneyBelow</c>...).
    /// </summary>
    public static class OnboardingPredicateLibrary
    {
        public const string CpuProjectActive = "onb.cpuProjectActive";
        public const string FactoryProjectActive = "onb.factoryProjectActive";
        public const string ProducedToday = "onb.producedToday";
        public const string SoldToday = "onb.soldToday";
        public const string MissedOverSold = "onb.missedOverSold";
        public const string AnyOldCpu = "onb.anyOldCpu";
        public const string OemAvailable = "onb.oemAvailable";
        public const string Automate = "onb.automate";
        public const string FoundryServices = "onb.foundryServices";
        public const string IsJan1 = "onb.isJan1";
        public const string DayOfMonth = "onb.dayOfMonth";
        public const string StepDone = "onb.stepDone";

        /// <summary>The names of the twelve predicates, in the order of the table of design.md.</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            CpuProjectActive, FactoryProjectActive, ProducedToday, SoldToday, MissedOverSold, AnyOldCpu,
            OemAvailable, Automate, FoundryServices, IsJan1, DayOfMonth, StepDone
        };

        /// <summary>
        /// The factories of the predicates, name first, in the order of <see cref="Names"/>: what <c>ModConditions.Register(name, owner, factory)</c>
        /// and <see cref="PredicateRegistry.Register"/> take. A factory reads the parameters of the node (<c>day</c>, <c>id</c>), reports mistakes
        /// (a missing or out-of-range parameter, an unknown one) in the error list and returns null then.
        /// </summary>
        public static IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> CreateFactories(IOnboardingFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));

            var list = new List<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>>();
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add = (name, factory) =>
                list.Add(new KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>(name, factory));

            add(CpuProjectActive, NoParameters(CpuProjectActive, ctx => facts.CpuProjectActive()));
            add(FactoryProjectActive, NoParameters(FactoryProjectActive, ctx => facts.FactoryProjectActive()));
            add(ProducedToday, NoParameters(ProducedToday, ctx => facts.ProducedToday()));
            add(SoldToday, NoParameters(SoldToday, ctx => facts.SoldToday()));
            add(MissedOverSold, NoParameters(MissedOverSold, ctx => facts.MissedOverSold()));
            add(AnyOldCpu, NoParameters(AnyOldCpu, ctx => facts.AnyOldCpu()));
            add(OemAvailable, NoParameters(OemAvailable, ctx => facts.OemAvailable()));
            add(Automate, NoParameters(Automate, ctx => facts.Automate()));
            add(FoundryServices, NoParameters(FoundryServices, ctx => facts.FoundryServices()));
            add(IsJan1, NoParameters(IsJan1, ctx => ctx.HasDate && ctx.Today.Month == 1 && ctx.Today.Day == 1));
            add(DayOfMonth, DayOfMonthFactory);
            add(StepDone, (node, errors, path) => StepDoneFactory(node, errors, path, facts));
            return list;
        }

        // ---- factories ----

        private static Func<JsonObject, List<string>, string, ICondition> NoParameters(string name, Func<ConditionContext, bool> test)
        {
            return (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                return args.Finish() ? new DelegateCondition(name, test) : null;
            };
        }

        private static ICondition DayOfMonthFactory(JsonObject node, List<string> errors, string path)
        {
            var args = new ConditionArgs(node, errors, path);
            int day = args.RequireInt("day", 1, 31);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(DayOfMonth), ctx => ctx.HasDate && ctx.Today.Day == day);
        }

        private static ICondition StepDoneFactory(JsonObject node, List<string> errors, string path, IOnboardingFacts facts)
        {
            var args = new ConditionArgs(node, errors, path);
            string id = args.RequireString("id");
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(StepDone), ctx => facts.IsStepDone(id));
        }
    }
}
