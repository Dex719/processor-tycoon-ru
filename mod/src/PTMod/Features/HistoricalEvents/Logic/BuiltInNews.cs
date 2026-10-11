using System;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The letters of reference about the game's own events (FR-16; design.md, Component 10): the spin-off of the foundries of the company that plays AMD, and the end of
    /// the x86 licence. They are events of the pack without effects, fired for the player when the game does the thing; they belong to no calendar entry, so the table here
    /// gives each its history (title, fact, source) the way an entry's <c>history</c> block does. The words are in the string tables (<c>hist.news.*</c>).
    /// </summary>
    public static class BuiltInNews
    {
        /// <summary>The spin-off of SolarFoundries (<c>CompanyEventHandler.TriggerSolarFoundriesSpinOff</c>, 2009-03-02 in the game).</summary>
        public const string SolarFoundriesEventId = "hist.news.solarfoundries";

        /// <summary>The licence of x86 was revoked (<c>LicenseHelper.OnX86LicenseRevoked</c>).</summary>
        public const string X86LicenseEventId = "hist.news.x86license";

        /// <summary>Both events of the news.</summary>
        public static readonly string[] EventIds = { SolarFoundriesEventId, X86LicenseEventId };

        /// <summary>The history of a news event, or null for any other id.</summary>
        public static HistoryInfo Find(string eventId)
        {
            if (string.Equals(eventId, SolarFoundriesEventId, StringComparison.Ordinal))
            {
                return Make("hist.news.solarfoundries.title", "hist.news.solarfoundries.fact", "https://en.wikipedia.org/wiki/GlobalFoundries");
            }
            if (string.Equals(eventId, X86LicenseEventId, StringComparison.Ordinal))
            {
                return Make("hist.news.x86license.title", "hist.news.x86license.fact", "https://en.wikipedia.org/wiki/Advanced_Micro_Devices");
            }
            return null;
        }

        /// <summary>The id is one of the news events.</summary>
        public static bool IsNews(string eventId)
        {
            return Find(eventId) != null;
        }

        private static HistoryInfo Make(string titleKey, string factKey, string source)
        {
            var info = new HistoryInfo { TitleKey = titleKey, FactKey = factKey };
            info.Sources.Add(source);
            return info;
        }
    }

    /// <summary>
    /// When the news about the game's own events are sent (FR-16, AC-16.1): the decisions, without the game. The spin-off is noticed after the game's method ran, by the
    /// foundry that was not there before and is now; nothing is sent while a save loads or the new game fast-forwards (the same events are raised again then), or when the
    /// setting <c>historyNotes</c> is off (these letters are nothing but reference), and each news goes out once per game. The game's logic is never changed.
    /// </summary>
    public sealed class BuiltInNewsCore
    {
        private bool _known;
        private bool _foundryExisted;
        private bool _spinOffAnnounced;
        private bool _licenseAnnounced;

        /// <summary>Forgets everything (a new scene: a game is loaded or started).</summary>
        public void Reset()
        {
            _known = false;
            _foundryExisted = false;
            _spinOffAnnounced = false;
            _licenseAnnounced = false;
        }

        /// <summary>Takes the state of the world as it is now as the state "before": the foundry is there or it is not. Nothing is announced.</summary>
        public void Baseline(bool foundryExists)
        {
            _known = true;
            _foundryExisted = foundryExists;
            if (foundryExists) _spinOffAnnounced = true;
        }

        /// <summary>The state before is known (<see cref="Baseline"/> or an earlier question).</summary>
        public bool IsKnown
        {
            get { return _known; }
        }

        /// <summary>
        /// The game's method for the spin-off has just run and the foundry is (<paramref name="foundryExists"/>) or is not there. True when the news goes out now.
        /// The state is updated whatever the answer, so a spin-off during a load or a fast-forward is not announced later.
        /// </summary>
        /// <param name="busy">A save is loading or the new game fast-forwards (<c>ModTicks.IsLoading</c> / <c>IsFastForwarding</c>).</param>
        /// <param name="notesOn">The setting <c>historyNotes</c>.</param>
        public bool ShouldAnnounceSpinOff(bool foundryExists, bool busy, bool notesOn)
        {
            if (!_known)
            {
                // Nothing is known of what was before, so nothing can be said to have just happened.
                Baseline(foundryExists);
                return false;
            }

            bool appeared = foundryExists && !_foundryExisted;
            _foundryExisted = foundryExists;
            if (!appeared) return false;

            bool announce = !busy && notesOn && !_spinOffAnnounced;
            _spinOffAnnounced = true;
            return announce;
        }

        /// <summary>The game raised <c>OnX86LicenseRevoked</c>. True when the news goes out now (once per game, not while loading, only with the notes on).</summary>
        public bool ShouldAnnounceLicense(bool busy, bool notesOn)
        {
            // Raised while loading or fast-forwarding it is the replay of something that already happened: never announced later either.
            if (busy)
            {
                _licenseAnnounced = true;
                return false;
            }
            if (!notesOn || _licenseAnnounced) return false;

            _licenseAnnounced = true;
            return true;
        }
    }
}
