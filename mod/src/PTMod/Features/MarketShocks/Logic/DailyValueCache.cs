using System;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The value of one channel for one game day (FR-8 item 6, NFR-1, design decision D9): the patches ask for the multiplier many times a day
    /// (<c>CpuMath.MaterialCost</c> for every CPU every day and again and again in the AI's search for a design), so the multiplier is read from
    /// the channel at most once per game day and the rest of the day's calls get the stored number.
    /// <list type="bullet">
    /// <item><b>When it reads.</b> The first question of a new day (the <c>day</c> function gives another number than the stored day), and the
    /// first question after <see cref="Invalidate"/> - which is what the change of a feature's state, the end of a load
    /// (<c>ModTicks.GameReady</c>) and <see cref="OnChannelChanged"/> for the channel's own name (a contribution changed in the middle of a day:
    /// the player's choice in a mail, <c>ModModifiers.Invalidate</c>) call.</item>
    /// <item><b>Fast path.</b> Nothing registered on the channel (<c>hasAny</c> is false) is the multiplier 1 and the channel is not asked at all.</item>
    /// <item><b>Unknown day.</b> A negative day (the date cannot be read) is never stored: such a question always asks again, so a stale
    /// number can never outlive the day it was read on.</item>
    /// <item><b>Unusable answers.</b> A multiplier that is not finite or is negative is replaced by 1 (the game's own value stands).</item>
    /// </list>
    /// The class does not know the game: the day, the "something is registered" test and the reading come as functions (the adapter is
    /// <c>ShockChannels</c>), so the rules above are tested without Unity. It is for the main thread, like the game's formulas.
    /// </summary>
    public sealed class DailyValueCache
    {
        private readonly string _channel;
        private readonly Func<long> _day;
        private readonly Func<bool> _hasAny;
        private readonly Func<float> _read;

        private bool _valid;
        private long _cachedDay;
        private float _value = 1f;
        private int _reads;
        private int _refreshes;

        /// <param name="channel">The channel's name, for <see cref="OnChannelChanged"/>.</param>
        /// <param name="day">The current game day as a number that changes when the day does (the date's day number); negative when unknown.</param>
        /// <param name="hasAny">Something is registered on the channel (<c>ModModifiers.HasAny</c>). False is the multiplier 1 without a reading.</param>
        /// <param name="read">The reading of the channel (<c>ModModifiers.Get</c> with the empty context: the channels are global).</param>
        public DailyValueCache(string channel, Func<long> day, Func<bool> hasAny, Func<float> read)
        {
            if (day == null) throw new ArgumentNullException("day");
            if (hasAny == null) throw new ArgumentNullException("hasAny");
            if (read == null) throw new ArgumentNullException("read");

            _channel = channel;
            _day = day;
            _hasAny = hasAny;
            _read = read;
        }

        /// <summary>The name of the channel this cache holds.</summary>
        public string Channel
        {
            get { return _channel; }
        }

        /// <summary>The multiplier of the channel for today: 1 when nothing is registered on it, otherwise the stored reading of the day.</summary>
        public float Value
        {
            get
            {
                long day = _day();
                if (day >= 0 && _valid && day == _cachedDay) return _value;

                float value = Compute();
                _refreshes++;
                _value = value;
                if (day >= 0)
                {
                    _cachedDay = day;
                    _valid = true;
                }
                else
                {
                    _valid = false;
                }
                return value;
            }
        }

        /// <summary>How many times the channel was actually read (<c>read</c> was called) since the cache was made.</summary>
        public int Reads
        {
            get { return _reads; }
        }

        /// <summary>How many times the value was worked out anew (a new day, <see cref="Invalidate"/>, an unknown day), with or without a reading.</summary>
        public int Refreshes
        {
            get { return _refreshes; }
        }

        /// <summary>Drops the stored value: the next question works it out again (a feature switched, a save loaded, the channel changed).</summary>
        public void Invalidate()
        {
            _valid = false;
        }

        /// <summary>
        /// The report <c>ModModifiers.ChannelChanged(channel)</c>: drops the stored value when it is this cache's channel (an exact name) or when
        /// the report names no channel (null: everything may have changed). The other channels are not this cache's business.
        /// </summary>
        public void OnChannelChanged(string channel)
        {
            if (channel == null || string.Equals(channel, _channel, StringComparison.Ordinal)) Invalidate();
        }

        private float Compute()
        {
            if (!_hasAny()) return 1f;

            _reads++;
            float value = _read();
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 1f;
            return value;
        }
    }
}
