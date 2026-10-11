namespace PTMod.Features.EventSystem
{
    // Public contract of event-system (design.md, "Контентная модель"). Pure data: no Unity and no game types in this folder (Api/Model).

    /// <summary>How an event starts (<c>trigger.type</c> of a definition).</summary>
    public enum EventTriggerType
    {
        /// <summary>On a calendar date (<c>year</c>, <c>month</c>, <c>day</c>, with <c>graceDays</c> for a late start).</summary>
        Date,

        /// <summary>By the daily lottery of the event's category (D6).</summary>
        DailyChance,

        /// <summary>When the <c>conditions</c> are true (checked every day or on the 1st, with <c>chance</c> and optional <c>edge</c>).</summary>
        Condition,

        /// <summary>Only from code: <c>EventBus.Fire</c>, a <c>FireEvent</c> effect or a debug command.</summary>
        Manual,
    }

    /// <summary>Whom an event hits (<c>targets.type</c> of a definition).</summary>
    public enum EventTargetType
    {
        /// <summary>The player's company.</summary>
        Player,

        /// <summary>One AI company picked by weight.</summary>
        RandomAI,

        /// <summary>Every company: one group of instances, global effects applied once (D8).</summary>
        AllCompanies,

        /// <summary>The company of <c>targets.companyKey</c> (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>).</summary>
        Specific,

        /// <summary>One company out of all, the player included (extension of the original list).</summary>
        RandomCompany,
    }

    /// <summary>How the candidates of a random target are weighted.</summary>
    public enum TargetWeighting
    {
        Uniform,
        FactoryCapacity,
        MarketShare,
        QuarterlyRevenue,
    }
}
