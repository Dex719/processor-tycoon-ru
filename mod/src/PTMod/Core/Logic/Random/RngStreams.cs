using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Core.Logic.Rng
{
    /// <summary>
    /// The named random streams of one game (FR-9): a world seed plus a table stream id -> <see cref="Xoshiro128"/>.
    /// This is the Unity-free half of <c>ModRandom</c>; the static facade, the world seed choice and the save
    /// section (<c>{ worldSeed, streams: { id: [4 words] } }</c>) belong to the save integration (TSK-025).
    /// <para>
    /// Seed of a stream = <c>worldSeed XOR FNV-1a(UTF-8 of the id)</c>, expanded to the 128-bit state by
    /// <see cref="SplitMix32"/> (<see cref="Xoshiro128"/> constructor). So a stream depends only on the world seed and its
    /// own id: the order in which features ask for streams, and how much the other streams were used, change nothing.
    /// Ids are compared exactly (ordinal, case-sensitive); use the feature's full slug or <c>slug/name</c> as the spec asks.
    /// </para>
    /// <para>
    /// Stream objects live as long as the table. <see cref="Reset"/> and <see cref="Restore"/> rewrite them in place, so
    /// a reference a feature cached (a static field, say) stays valid across a new game and a load. Not thread-safe.
    /// </para>
    /// </summary>
    public sealed class RngStreams
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        private readonly Dictionary<string, Xoshiro128> streams = new Dictionary<string, Xoshiro128>(StringComparer.Ordinal);
        private uint worldSeed;

        public RngStreams(uint worldSeed)
        {
            this.worldSeed = worldSeed;
        }

        /// <summary>The seed all stream seeds are derived from; it is saved with the streams.</summary>
        public uint WorldSeed
        {
            get { return worldSeed; }
        }

        /// <summary>Number of streams created so far (asked for with <see cref="Get"/> or loaded by <see cref="Restore"/>).</summary>
        public int Count
        {
            get { return streams.Count; }
        }

        /// <summary>Ids of the existing streams, ordinal order.</summary>
        public IReadOnlyList<string> StreamIds
        {
            get { return SortedIds(); }
        }

        public bool Contains(string streamId)
        {
            return streamId != null && streams.ContainsKey(streamId);
        }

        /// <summary>
        /// The stream with this id; created at its initial state (from the world seed) on first use, the same object after.
        /// Throws <see cref="ArgumentNullException"/> for null and <see cref="ArgumentException"/> for an empty id: both
        /// are programming errors, and silently sharing one stream between such callers would hide them.
        /// </summary>
        public IModRng Get(string streamId)
        {
            if (streamId == null) throw new ArgumentNullException(nameof(streamId));
            if (streamId.Length == 0) throw new ArgumentException("A stream id must not be empty.", nameof(streamId));

            Xoshiro128 rng;
            if (!streams.TryGetValue(streamId, out rng))
            {
                rng = new Xoshiro128(DeriveStreamSeed(worldSeed, streamId));
                streams.Add(streamId, rng);
            }
            return rng;
        }

        /// <summary>
        /// New game: switches to a new world seed and puts every existing stream back to its initial state for that seed
        /// (streams asked for later are created from it as usual).
        /// </summary>
        public void Reset(uint newWorldSeed)
        {
            worldSeed = newWorldSeed;
            ReseedAll();
        }

        // ---- save / load ----

        /// <summary>
        /// The state of every stream, ready for a save section: id -> four words. Keys are in ordinal order (the dictionary
        /// is filled in that order, so the written JSON is stable between saves). The arrays are copies. Streams that were
        /// created but never drawn from are included with their initial state.
        /// </summary>
        public Dictionary<string, uint[]> ExportStates()
        {
            IReadOnlyList<string> ids = SortedIds();
            var result = new Dictionary<string, uint[]>(ids.Count, StringComparer.Ordinal);
            foreach (string id in ids) result.Add(id, streams[id].GetState());
            return result;
        }

        /// <summary>
        /// Loads a save: takes <paramref name="savedWorldSeed"/> and the saved stream states. After the call every stream
        /// continues exactly where it was when the game was saved. Never throws.
        /// <list type="bullet">
        /// <item>A stream that exists here but is not in the save returns to its initial state for the saved world seed: that
        /// is what it was at save time (it had not been used yet), and it keeps the stream object a feature may hold.</item>
        /// <item>A saved stream that does not exist here yet is created.</item>
        /// <item>A saved entry with an empty id, or a state that is null, not four words or all zeros, is skipped. The stream
        /// then starts from the world seed (its initial state) when asked for. The returned list holds one English
        /// message per skipped entry, for <c>ModLog</c>; it is empty when everything was loaded.</item>
        /// </list>
        /// A null <paramref name="savedStates"/> counts as "no stream was saved". A save without any random section at all
        /// (FR-9, item 5) is handled by the caller: it picks a new world seed, calls <see cref="Reset"/> and logs that.
        /// </summary>
        public IReadOnlyList<string> Restore(uint savedWorldSeed, IEnumerable<KeyValuePair<string, uint[]>> savedStates)
        {
            var problems = new List<string>();

            worldSeed = savedWorldSeed;
            ReseedAll();

            if (savedStates == null) return problems;

            foreach (KeyValuePair<string, uint[]> saved in savedStates)
            {
                string id = saved.Key;
                if (string.IsNullOrEmpty(id))
                {
                    problems.Add("saved random stream with an empty id skipped");
                    continue;
                }

                uint[] state = saved.Value;
                if (!Xoshiro128.IsValidState(state))
                {
                    problems.Add("saved random stream '" + id + "' skipped: " + DescribeInvalidState(state)
                                 + "; it restarts from the world seed");
                    continue;
                }

                Xoshiro128 rng;
                if (!streams.TryGetValue(id, out rng))
                {
                    rng = new Xoshiro128(0u); // any valid state; overwritten on the next line
                    streams.Add(id, rng);
                }
                rng.TrySetState(state);
            }

            return problems;
        }

        // ---- seed derivation ----

        /// <summary>Seed of the stream <paramref name="streamId"/> in the world <paramref name="worldSeed"/>: the seed XOR <see cref="Fnv1a"/> of the id.</summary>
        public static uint DeriveStreamSeed(uint worldSeed, string streamId)
        {
            return worldSeed ^ Fnv1a(streamId);
        }

        /// <summary>
        /// FNV-1a, 32 bit, over the UTF-8 bytes of the text (offset basis 2166136261, prime 16777619). UTF-8 and not the
        /// UTF-16 chars, so the value is the textbook one for ASCII ids and does not depend on byte order or platform.
        /// Throws <see cref="ArgumentNullException"/> for null.
        /// </summary>
        public static uint Fnv1a(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            unchecked
            {
                uint hash = FnvOffsetBasis;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        /// <summary>
        /// A world seed from a clock reading (for a new game without a seed set by a scenario): the 64-bit tick count
        /// folded to 32 bits and mixed by <see cref="SplitMix32"/>, so readings a few ticks apart give unrelated seeds.
        /// The caller supplies the clock (<c>DateTime.UtcNow.Ticks</c>); the same ticks always give the same seed.
        /// </summary>
        public static uint SeedFromClock(long ticks)
        {
            unchecked
            {
                uint folded = (uint)ticks ^ (uint)(ticks >> 32);
                return new SplitMix32(folded).Next();
            }
        }

        // ---- helpers ----

        private void ReseedAll()
        {
            foreach (KeyValuePair<string, Xoshiro128> pair in streams)
            {
                pair.Value.Reseed(DeriveStreamSeed(worldSeed, pair.Key));
            }
        }

        private List<string> SortedIds()
        {
            var ids = new List<string>(streams.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static string DescribeInvalidState(uint[] state)
        {
            if (state == null) return "the state is null";
            if (state.Length != Xoshiro128.StateWords) return "expected 4 words, found " + state.Length;
            return "the state is all zeros";
        }
    }
}
