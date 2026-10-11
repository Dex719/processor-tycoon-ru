namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// What the handlers of the feature tell the player (design, Component 7): an announcement, a launch, an early adopter, a penalty for changing
    /// the socket. The handlers (<see cref="PlatformReleaseHandlers"/>, <see cref="PlatformSubscriptions"/>) only say that it happened; whether
    /// anything is shown (not in the fast-forward, not while a save loads, not with the switch "notifications" off, only for the player where
    /// the rule says so) is the implementation's business (<see cref="PlatformNotifier"/>, TSK-013). <see cref="NullNotifier"/> shows nothing (tests, a notifier taken away).
    /// </summary>
    internal interface IPlatformNotifier
    {
        /// <summary>The generation was announced (1 January of its announce year, or the day of a release that came before it).</summary>
        void Announced(GenerationDef generation);

        /// <summary>The first CPU of the generation was released by the company (<c>player</c> says whether it is the player's).</summary>
        void Launched(GenerationDef generation, string byCompanyKey, bool player);

        /// <summary>The player became an early adopter of the generation; <paramref name="slot"/> is 1 for the pioneer.</summary>
        void EarlyAdopter(GenerationDef generation, int slot);

        /// <summary>
        /// The player's first CPU on <paramref name="socket"/> costs <paramref name="penalty"/> (a fraction of the rating, 0.067 = 6.7 %) for
        /// <paramref name="months"/> months, fading. <paramref name="mainSocketKey"/> is the main socket the player leaves (null when unknown) and
        /// <paramref name="mainAgeMonths"/> the months it was supported; the notification says both (FR-6 item 5).
        /// </summary>
        void Churn(SocketId socket, float penalty, int months, string mainSocketKey, int mainAgeMonths);
    }

    /// <summary>The notifier that shows nothing: what <c>PlatformGenerationsFeature.Notifier</c> falls back to when it is set to null.</summary>
    internal sealed class NullNotifier : IPlatformNotifier
    {
        public static readonly NullNotifier Instance = new NullNotifier();

        public void Announced(GenerationDef generation)
        {
        }

        public void Launched(GenerationDef generation, string byCompanyKey, bool player)
        {
        }

        public void EarlyAdopter(GenerationDef generation, int slot)
        {
        }

        public void Churn(SocketId socket, float penalty, int months, string mainSocketKey, int mainAgeMonths)
        {
        }
    }
}
