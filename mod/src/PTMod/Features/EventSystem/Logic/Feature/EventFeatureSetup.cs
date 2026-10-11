using System;
using PTMod.Core;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What <c>EventSystemFeature.Register</c> declares, as the feature context offers it (<c>FeatureContext</c> in the game, a recorder in the tests). There is
    /// no way to declare a patch: the engine has no Harmony patches of its own (design.md, "Патчи Harmony").
    /// </summary>
    public interface IEventFeatureHost
    {
        /// <summary><c>ctx.SaveParticipant</c>: the section <c>event-system</c>.</summary>
        void SaveParticipant(ISideFileParticipant participant);

        /// <summary><c>ctx.OnTick(TickPhase.DayPassed, handler)</c>: the day of the scheduler.</summary>
        void OnDayPassed(Action handler);

        /// <summary><c>ctx.Param</c>: a parameter of the hub.</summary>
        void Param(ParamSpec spec);

        /// <summary><c>ctx.HubPage</c>: the settings page of the feature in the hub (the journal button, the debug list).</summary>
        void HubPage();

        /// <summary><c>EventSystemApi.Install</c>: the engine behind the static API for the other features.</summary>
        void InstallApi();

        /// <summary>The engine's own registrations through the API: effect types, the predicates <c>es.*</c>, the pack <c>event-system</c>.</summary>
        void RegisterOwnContent();

        /// <summary>The subscriptions to the events of the core (<c>ModTicks.GameReady</c> and the others).</summary>
        void SubscribeCoreEvents();
    }

    /// <summary>The order of <c>Register</c> from design.md ("Точка входа фичи"): save participant, day tick, parameters and the hub page, the API and its registrations, the subscriptions.</summary>
    public static class EventFeatureSetup
    {
        public static void Register(IEventFeatureHost host, ISideFileParticipant participant, Action dayHandler, string featureId)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (participant == null) throw new ArgumentNullException("participant");
            if (dayHandler == null) throw new ArgumentNullException("dayHandler");

            host.SaveParticipant(participant);
            host.OnDayPassed(dayHandler);
            foreach (ParamSpec spec in EventParams.Create(featureId)) host.Param(spec);
            host.HubPage();
            host.InstallApi();
            host.RegisterOwnContent();
            host.SubscribeCoreEvents();
        }
    }
}
