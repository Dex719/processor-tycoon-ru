using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The arguments the feature gives to <c>EventBus.Fire</c> (<c>EventFireContext.Args</c>, design.md "Контент: events.json": <c>[имя соперника,
    /// сегмент, reactionId, ключ соперника]</c>) and the layout of the <c>textArgs</c> its effect handlers save in the <c>AppliedEffect</c>. A handler
    /// reads the rival, the segment and the id of the reaction from what <c>Resolve</c> saved, never from the live event, so a load or a
    /// switch-on gives the same numbers. Pure.
    /// </summary>
    public sealed class EffectArgs
    {
        /// <summary>The number of fixed arguments both layouts start with.</summary>
        public const int FixedCount = 4;

        /// <summary>The id of the reaction (<c>r-&lt;n&gt;</c>).</summary>
        public string ReactionId = string.Empty;

        /// <summary>The segment (<c>(int)Segment</c>, -1 for none).</summary>
        public int Segment = -1;

        /// <summary>The company key of the rival (<c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string RivalKey = string.Empty;

        /// <summary>The name of the rival for the texts.</summary>
        public string RivalName = string.Empty;

        /// <summary>The <c>UniqueID</c> of the rival, -1 when <see cref="RivalKey"/> is not an AI key.</summary>
        public int UniqueId
        {
            get
            {
                int id;
                return PTMod.Core.Logic.Companies.CompanyKey.TryGetAiId(RivalKey, out id) ? id : -1;
            }
        }

        /// <summary>The rival and the reaction are known: the effect can be applied.</summary>
        public bool IsComplete
        {
            get { return !string.IsNullOrEmpty(ReactionId) && UniqueId >= 0; }
        }

        /// <summary>
        /// The arguments of <c>EventFireContext.Args</c>: <c>[rival name, detail, reaction id, rival key]</c>. The detail is what the texts of the
        /// event put in <c>{1}</c>: the segment (the number of <c>(int)Segment</c>, -1 for none) for most reactions, the price of keeping the engineers
        /// for <c>Poach</c> (its text says "keeping them will cost {1}").
        /// </summary>
        public static string[] ForFire(string rivalName, string detail, string reactionId, string rivalKey)
        {
            return new[] { rivalName ?? string.Empty, detail ?? string.Empty, reactionId ?? string.Empty, rivalKey ?? string.Empty };
        }

        /// <summary>The arguments of <c>EventFireContext.Args</c> with a segment as the detail.</summary>
        public static string[] ForFire(string rivalName, int segment, string reactionId, string rivalKey)
        {
            return ForFire(rivalName, segment.ToString(CultureInfo.InvariantCulture), reactionId, rivalKey);
        }

        /// <summary>
        /// Reads the arguments of a fired event (<see cref="ForFire"/>). Missing or damaged members stay at their defaults; <see cref="IsComplete"/>
        /// says whether the rival and the reaction came through.
        /// </summary>
        public static EffectArgs FromFire(string[] args)
        {
            var result = new EffectArgs();
            if (args == null) return result;
            if (args.Length > 0) result.RivalName = args[0] ?? string.Empty;
            if (args.Length > 1) result.Segment = ParseSegment(args[1]);
            if (args.Length > 2) result.ReactionId = args[2] ?? string.Empty;
            if (args.Length > 3) result.RivalKey = args[3] ?? string.Empty;
            return result;
        }

        /// <summary>
        /// The <c>textArgs</c> of an effect: the four fixed members in the order <c>[reaction id, segment, rival key, rival name]</c> and then
        /// <paramref name="extra"/> (the formatted numbers of the effect's line, in the order its text takes them).
        /// </summary>
        public string[] ToSaved(params string[] extra)
        {
            int count = extra != null ? extra.Length : 0;
            var saved = new string[FixedCount + count];
            saved[0] = ReactionId ?? string.Empty;
            saved[1] = Segment.ToString(CultureInfo.InvariantCulture);
            saved[2] = RivalKey ?? string.Empty;
            saved[3] = RivalName ?? string.Empty;
            for (int i = 0; i < count; i++) saved[FixedCount + i] = extra[i] ?? string.Empty;
            return saved;
        }

        /// <summary>Reads the <c>textArgs</c> a handler saved (<see cref="ToSaved"/>). Damaged or short arrays give an object that is not <see cref="IsComplete"/>.</summary>
        public static EffectArgs FromSaved(string[] saved)
        {
            var result = new EffectArgs();
            if (saved == null) return result;
            if (saved.Length > 0) result.ReactionId = saved[0] ?? string.Empty;
            if (saved.Length > 1) result.Segment = ParseSegment(saved[1]);
            if (saved.Length > 2) result.RivalKey = saved[2] ?? string.Empty;
            if (saved.Length > 3) result.RivalName = saved[3] ?? string.Empty;
            return result;
        }

        /// <summary>The member <paramref name="index"/> after the four fixed ones, or an empty text when the array is shorter.</summary>
        public static string ExtraAt(string[] saved, int index)
        {
            int position = FixedCount + index;
            return saved != null && index >= 0 && position < saved.Length ? saved[position] ?? string.Empty : string.Empty;
        }

        /// <summary>A number the way the texts show it: the invariant culture, at most one decimal (<c>15</c>, <c>0.5</c>).</summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>A share as a percentage the way the texts show it: 0.15 is <c>15</c>.</summary>
        public static string Percent(double share)
        {
            return Number(share * 100.0);
        }

        /// <summary>A multiplier with two decimals at most (<c>1.35</c>, <c>1.1</c>).</summary>
        public static string Multiplier(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "1";
            return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static int ParseSegment(string text)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return -1;
            return value >= -1 && value <= 3 ? value : -1;
        }
    }
}
