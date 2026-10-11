namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The order of the monthly handlers of the feature (design.md, "Подписки на события"): the core calls the handlers of a phase by priority, the larger
    /// first. The market shares come first, because the scheduler and the services read the cache; then the scheduler; then the services in the order
    /// of the design (cases, settlements, export rules, disputes, races). The later tasks take their numbers from here.
    /// </summary>
    internal static class HistTickPriority
    {
        public const int ShareCache = 100;
        public const int Scheduler = 90;
        public const int Cases = 80;
        public const int Settlements = 70;
        public const int ExportRules = 60;
        public const int Disputes = 50;
        public const int Races = 40;
    }
}
