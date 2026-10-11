namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What to do with an AI company that has just been initialised (<c>AICompany.Initialize</c>) or reformed (<c>ReformCyrux</c>).</summary>
    public enum SpawnDecision
    {
        /// <summary>Give the company its character now.</summary>
        Apply = 0,

        /// <summary>The fast-forward of a new game runs: the feature is neutral, <c>TickPhase.FastForwardEnded</c> gives every character at once.</summary>
        SkipFastForward = 1,

        /// <summary>A save is loading and its section has not been read: <c>ModSave.AfterRead</c> gives every character.</summary>
        DeferLoading = 2,

        /// <summary>The state of the game is not ready yet (no fast-forward end, no load in this scene): the moment that makes it ready gives every character.</summary>
        DeferNotReady = 3,
    }

    /// <summary>What <see cref="PersonalityLifecycle.GameReady"/> asks for.</summary>
    public enum ReadyAction
    {
        /// <summary>Everything was done at the right moment already.</summary>
        None = 0,

        /// <summary>The start of a new game was not handled (no <c>FastForwardEnded</c> came): give the characters, the baseline attitudes and the grace period now.</summary>
        StartNewGame = 1,

        /// <summary>The section of a load was not announced (no <c>ModSave.AfterRead</c> came): give the characters of the existing companies now.</summary>
        ApplyAll = 2,
    }

    /// <summary>
    /// When the characters may be given (FR-2 items 1, 4 and 6; design.md D1, D10): the moments of one game scene as a small state machine, pure so
    /// that the order of the game's moments can be tested.
    /// <list type="bullet">
    /// <item>During the fast-forward of a new game nothing is given (<see cref="SpawnDecision.SkipFastForward"/>).</item>
    /// <item><c>TickPhase.FastForwardEnded</c> starts the game once per scene: every existing company gets its character, the baseline attitude and the
    /// grace period start (<see cref="BeginNewGame"/>).</item>
    /// <item>While a save loads, a company waits for <c>ModSave.AfterRead</c> (<see cref="StateRead"/>), because its archetype is in the section.</item>
    /// <item><c>ModTicks.GameReady</c> is the safety net when one of those moments did not come (<see cref="GameReady"/>).</item>
    /// <item>A new scene and switching the feature on start from nothing (<see cref="Reset"/>, <see cref="EnabledInGame"/>).</item>
    /// </list>
    /// </summary>
    public sealed class PersonalityLifecycle
    {
        /// <summary>The section of the scene can be used: the characters were given after a load, at the start of a new game, or when the feature was switched on in a ready game.</summary>
        public bool StateReady { get; private set; }

        /// <summary>The start of a new game (<c>FastForwardEnded</c>) was handled in this scene.</summary>
        public bool NewGameStarted { get; private set; }

        /// <summary>How many companies waited for the state since the last <see cref="Reset"/> (for the log).</summary>
        public int Deferred { get; private set; }

        /// <summary>A scene goes, or the feature is switched on before the game is ready: nothing is ready.</summary>
        public void Reset()
        {
            StateReady = false;
            NewGameStarted = false;
            Deferred = 0;
        }

        /// <summary>A company was initialised or reformed: give it its character now, or leave it to a later moment.</summary>
        public SpawnDecision OnSpawn(bool fastForwarding, bool loading)
        {
            if (fastForwarding) return SpawnDecision.SkipFastForward;
            if (StateReady) return SpawnDecision.Apply;

            Deferred++;
            return loading ? SpawnDecision.DeferLoading : SpawnDecision.DeferNotReady;
        }

        /// <summary>
        /// <c>TickPhase.FastForwardEnded</c>: true the first time in a scene, and then the caller assigns and applies every character, sets the
        /// attitudes to the baseline and starts the grace period. False when the start was handled already.
        /// </summary>
        public bool BeginNewGame()
        {
            if (NewGameStarted) return false;
            NewGameStarted = true;
            StateReady = true;
            return true;
        }

        /// <summary><c>ModSave.AfterRead</c>: the section of the load is there; true, and the caller gives every existing company its character.</summary>
        public bool StateRead()
        {
            StateReady = true;
            return true;
        }

        /// <summary><c>ModTicks.GameReady</c>: what is left to do when the moment that should have done it did not come.</summary>
        public ReadyAction GameReady(bool newGame)
        {
            if (newGame)
            {
                return BeginNewGame() ? ReadyAction.StartNewGame : ReadyAction.None;
            }
            if (StateReady) return ReadyAction.None;
            StateReady = true;
            return ReadyAction.ApplyAll;
        }

        /// <summary>
        /// The feature was switched on (<c>OnEnabled</c>): it starts from nothing; in a ready game the state can be used at once (true: the caller gives
        /// every existing company its character).
        /// </summary>
        public bool EnabledInGame(bool gameReady)
        {
            Reset();
            if (!gameReady) return false;
            StateReady = true;
            return true;
        }
    }
}
