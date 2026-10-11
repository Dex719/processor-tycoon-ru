using System;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// What patch GP1 changes on the production screen of the game (design.md, "Патчи Harmony", GP1; FR-5 item 8), without the game: whether the screen shows the
    /// cards at all and how many lines the rows of the CPUs may still take. The game counts the free lines as <c>capacity - used</c> (0 at the least), where
    /// <c>used</c> is the lines of the player's own CPUs (production and contracts, the outsourced ones left out) and of the foundry clients' CPUs. With a reserve
    /// of <c>N</c> lines of cards the label "Использовано" gets " (+N GPU)"; in the default mode the rows get <c>max(0, free - N)</c>, so the CPUs cannot be given
    /// the lines the cards take first; with "CPU first" the rows keep the game's number (the cards only take what the CPUs leave). Without a reserve the screen
    /// stays the game's.
    /// </summary>
    public static class GpuProductionScreen
    {
        /// <summary>The text added to "Использовано" (<c>{0}</c> is the lines of the cards).</summary>
        public const string ReservedKey = "gpu.productionUi.reserved";

        /// <summary>The screen shows the cards (a reserve of more than 0 lines today); otherwise it is left to the game.</summary>
        public static bool ShowsCards(int gpuLines)
        {
            return gpuLines > 0;
        }

        /// <summary>The free lines as the game counts them: <c>max(0, capacity - used)</c>.</summary>
        public static int GameFree(int capacity, int usedByCpus)
        {
            long free = (long)capacity - Math.Max(0, usedByCpus);
            return free <= 0 ? 0 : (int)Math.Min(free, int.MaxValue);
        }

        /// <summary>
        /// The lines the rows of the CPUs may still take: the game's free lines minus the cards' reserve in the default mode, the game's number with
        /// <paramref name="cpuPriority"/> or without a reserve. Never below 0.
        /// </summary>
        public static int FreeForCpus(int capacity, int usedByCpus, int gpuLines, bool cpuPriority)
        {
            int free = GameFree(capacity, usedByCpus);
            if (!ShowsCards(gpuLines) || cpuPriority) return free;
            return Math.Max(0, free - gpuLines);
        }
    }
}
