using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The <c>params</c> object of an effect as an effect handler reads it. An adapter over <see cref="JsonObject"/>: a missing key, or a key of the
    /// wrong kind, gives the fallback. The engine itself reads the common keys (see <see cref="EffectParamKeys"/>), a handler reads its own.
    /// </summary>
    public interface IEffectParams
    {
        bool Has(string key);
        float GetFloat(string key, float fallback);
        int GetInt(string key, int fallback);
        bool GetBool(string key, bool fallback);
        string GetString(string key, string fallback);

        /// <summary>A number (lo = hi) or a pair <c>[lo, hi]</c>.</summary>
        FloatRange GetRange(string key, FloatRange fallback);

        /// <summary>The underlying JSON object, for parameters this interface cannot express (lists, nested objects).</summary>
        JsonObject Raw { get; }
    }

    /// <summary>An interval <c>[Lo, Hi]</c>; a single number in JSON is the interval of one point.</summary>
    public struct FloatRange
    {
        public float Lo;
        public float Hi;

        public FloatRange(float lo, float hi)
        {
            Lo = lo;
            Hi = hi;
        }
    }
}
