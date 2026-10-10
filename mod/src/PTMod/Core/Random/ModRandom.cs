using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>
    /// The deterministic random numbers of the mod (FR-9): named streams derived from one world seed of the game, saved with the game. The
    /// pure half (the generator, the stream table, the save/restore of the states) is <see cref="RngStreams"/>; this class is its one instance
    /// and its place in the save - the block <c>random</c> of the section <c>mod-framework</c>.
    /// <list type="bullet">
    /// <item><b>Streams.</b> <see cref="Stream"/> gives the same object for the same id for the whole session; the sequence depends only on the
    /// world seed and the id, never on the order in which features ask or on how much other streams were used. Use the feature id or
    /// <c>feature-id/name</c> as the id. The game's own random generators are not touched (FR-9 item 4).</item>
    /// <item><b>World seed.</b> A new game takes the seed set by <see cref="SetSeedForNewGame"/> (a scenario's, from <c>ModNewGame.BeforeStart</c>) or
    /// one made from the clock; every stream starts from it. A save written by this mod holds the seed and the state of every stream, so a game
    /// that is saved and loaded goes on exactly as it would have without (AC-9.1). A save without the block (an old save, a file that could not
    /// be used) gets a new seed and one line in the log (FR-9 item 5).</item>
    /// <item><b>Streams survive new games and loads.</b> A load or a new game rewrites the streams in place, so a stream a feature kept in a field
    /// stays valid. The streams of features that are off or gone stay in the table and in the save.</item>
    /// </list>
    /// Main thread only (a stream's numbers depend on the order of calls).
    /// </summary>
    public static class ModRandom
    {
        private const string CoreId = ModLog.CoreFeatureId;

        private static readonly RngStreams Streams = new RngStreams(NewClockSeed());
        private static readonly ICoreSectionPart SavePart = new RandomPart();

        private static long _seedCounter;
        private static uint? _pendingSeed;
        private static ResetCause _cause = ResetCause.NoState;

        /// <summary>Why the part is being reset: decides the new world seed and what the log says (see <see cref="BeginReset"/>).</summary>
        internal enum ResetCause
        {
            /// <summary>A save without random state (a vanilla save, an unusable or newer file, a section without the block): a new seed from the clock, logged.</summary>
            NoState,

            /// <summary>A new game: the seed set by <see cref="SetSeedForNewGame"/>, else one from the clock.</summary>
            NewGame,

            /// <summary>Leaving to the menu: nothing is played, a fresh seed from the clock and no log line; a pending scenario seed is dropped.</summary>
            Menu,
        }

        /// <summary>The seed all streams of this game derive from. Saved with the game.</summary>
        public static uint WorldSeed
        {
            get { return Streams.WorldSeed; }
        }

        /// <summary>
        /// The stream with this id: created on first use, the same object afterwards (it keeps its place in the sequence across saving and loading).
        /// An id is the feature's slug or <c>slug/name</c>; ids are compared exactly. A null or empty id is a bug of the caller and throws
        /// (<see cref="ArgumentNullException"/>, <see cref="ArgumentException"/>) - the failure shows at the call, in the feature's own log, and
        /// two callers never share a stream by accident.
        /// </summary>
        public static IModRng Stream(string streamId)
        {
            return Streams.Get(streamId);
        }

        /// <summary>
        /// A scenario's own world seed for the new game that is being started (<c>ModNewGame.BeforeStart</c>): the same scenario then plays the same
        /// world. Called before the new game is prepared, it waits for it; called after <c>ModSave.PrepareNewGame</c> and before the game is ready, it
        /// applies at once; in a game that is already running it has no effect (a warning) - go through the menu for a new world.
        /// </summary>
        public static void SetSeedForNewGame(uint seed)
        {
            if (ModTicks.IsGameReady)
            {
                ModLog.Warn(CoreId, "random: SetSeedForNewGame(" + seed.ToString(CultureInfo.InvariantCulture) + ") is ignored: the game is already running");
                return;
            }

            if (ModSave.Status == SideFileStatus.NewGame)
            {
                _pendingSeed = null;
                Streams.Reset(seed);
                ModLog.Info(CoreId, "random: new game, world seed " + seed.ToString(CultureInfo.InvariantCulture) + " (set by the scenario, after the game was prepared)");
                return;
            }

            _pendingSeed = seed;
        }

        // ---- the save (ModSave) ----

        /// <summary>The block <c>random</c> of the section of the core; registered at the Declare stage.</summary>
        [CoreInit(CoreInitStage.Declare, -900)]
        internal static void DeclareSavePart()
        {
            CoreSection.RegisterPart(SavePart);
        }

        /// <summary>
        /// Tells the block why it is about to be reset (<c>ModSave</c> sets it around a new game and around leaving to the menu; every other reset
        /// is "the save has no random state"). Pair with <see cref="EndReset"/>.
        /// </summary>
        internal static void BeginReset(ResetCause cause)
        {
            _cause = cause;
        }

        internal static void EndReset()
        {
            _cause = ResetCause.NoState;
        }

        /// <summary>The seed a scenario asked for and nobody has used yet (diagnostics and tests).</summary>
        internal static uint? PendingSeed
        {
            get { return _pendingSeed; }
        }

        private static uint NewClockSeed()
        {
            // The counter keeps two resets within one clock tick apart.
            return RngStreams.SeedFromClock(DateTime.UtcNow.Ticks + ++_seedCounter);
        }

        private static void ApplyReset()
        {
            uint seed;
            switch (_cause)
            {
                case ResetCause.NewGame:
                {
                    string source;
                    if (_pendingSeed.HasValue)
                    {
                        seed = _pendingSeed.Value;
                        source = "set by the scenario";
                        _pendingSeed = null;
                    }
                    else
                    {
                        seed = NewClockSeed();
                        source = "from the clock";
                    }
                    Streams.Reset(seed);
                    ModLog.Info(CoreId, "random: new game, world seed " + seed.ToString(CultureInfo.InvariantCulture) + " (" + source + ")");
                    break;
                }

                case ResetCause.Menu:
                    _pendingSeed = null;
                    Streams.Reset(NewClockSeed());
                    break;

                default:
                    seed = NewClockSeed();
                    Streams.Reset(seed);
                    ModLog.Info(CoreId, "random: the save has no random state; a new world seed " + seed.ToString(CultureInfo.InvariantCulture) + " is used");
                    break;
            }
        }

        /// <summary>The block <c>random</c>: <c>{ "worldSeed": 2730914561, "streams": { "id": [w0, w1, w2, w3] } }</c> (<c>RandomDto</c>).</summary>
        private sealed class RandomPart : ICoreSectionPart
        {
            public string Key
            {
                get { return CoreSection.RandomKey; }
            }

            public void ResetToDefaults()
            {
                ApplyReset();
            }

            public void Read(JsonNode block, int schema)
            {
                var node = block as JsonObject;
                long seed;
                if (node == null || !node.TryGetInt64("worldSeed", out seed) || seed < 0 || seed > uint.MaxValue)
                {
                    // Nothing to continue from (a hand-edited or damaged block): the same as a save without the block.
                    ModLog.Warn(CoreId, "save: the block 'random' has no usable worldSeed; a new world seed is chosen");
                    ApplyReset();
                    return;
                }

                var errors = new List<string>();
                RandomDto dto = ModJson.Bind<RandomDto>(node, errors);
                foreach (string error in errors) ModLog.Warn(CoreId, "save: random" + error.TrimStart('$'));

                IReadOnlyList<string> problems = Streams.Restore(dto.worldSeed, dto.streams);
                foreach (string problem in problems) ModLog.Warn(CoreId, "save: " + problem);
            }

            public JsonNode Write()
            {
                var dto = new RandomDto { worldSeed = Streams.WorldSeed, streams = Streams.ExportStates() };
                return ModJson.From(dto);
            }
        }
    }
}
