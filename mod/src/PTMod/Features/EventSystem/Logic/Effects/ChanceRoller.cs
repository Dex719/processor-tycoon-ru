using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The chance of the effects of one list (a choice, or the event's own effects) and its exclusive groups (FR-5 items 4-5, design.md "Шанс и эксклюзивные
    /// группы"), rolled once at <c>Resolve</c>:
    /// <list type="bullet">
    /// <item>an effect without a group: <c>hit = u &lt; chance</c>;</item>
    /// <item>a group <c>G</c>: one <c>u</c> for the group (the number of its first member); with the running sums <c>c1, c1 + c2, ...</c> in the order of the list
    /// the first member with <c>u &lt; Σ c</c> hits and the others miss; none hits when <c>u</c> is past the total (Σ &lt; 1).</item>
    /// </list>
    /// Exactly one number is taken per effect, whatever the chances and the groups (D5): a change of content never shifts the numbers of later effects.
    /// </summary>
    public static class ChanceRoller
    {
        /// <summary>The <c>chance</c> of an effect, 0..1 (1 when missing; a value outside is brought into the range, NaN is 0).</summary>
        public static float ChanceOf(EventEffect effect)
        {
            if (effect == null || effect.@params == null) return 1f;
            float chance = effect.@params.GetFloat(EffectParamKeys.Chance, 1f);
            if (float.IsNaN(chance) || chance <= 0f) return 0f;
            return chance >= 1f ? 1f : chance;
        }

        /// <summary>The <c>exclusiveGroup</c> of an effect, or null for none.</summary>
        public static string GroupOf(EventEffect effect)
        {
            if (effect == null || effect.@params == null) return null;
            string group = effect.@params.GetString(EffectParamKeys.ExclusiveGroup, null);
            return string.IsNullOrEmpty(group) ? null : group;
        }

        /// <summary>Rolls the effects of one list with <paramref name="rng"/>: one number per effect, in order.</summary>
        public static bool[] Roll(IList<EventEffect> effects, IModRng rng)
        {
            if (effects == null || effects.Count == 0) return new bool[0];
            if (rng == null) throw new ArgumentNullException("rng");

            var chances = new float[effects.Count];
            var groups = new string[effects.Count];
            var draws = new float[effects.Count];
            for (int i = 0; i < effects.Count; i++)
            {
                chances[i] = ChanceOf(effects[i]);
                groups[i] = GroupOf(effects[i]);
                draws[i] = rng.Value();
            }
            return Roll(chances, groups, draws);
        }

        /// <summary>The same rule over numbers already drawn (one per effect): <paramref name="groups"/> null or empty means no group.</summary>
        public static bool[] Roll(IList<float> chances, IList<string> groups, IList<float> draws)
        {
            if (chances == null) throw new ArgumentNullException("chances");
            if (draws == null || draws.Count < chances.Count) throw new ArgumentException("one number per effect is needed", "draws");

            var hits = new bool[chances.Count];
            var groupDraw = new Dictionary<string, float>(StringComparer.Ordinal);
            var groupSum = new Dictionary<string, float>(StringComparer.Ordinal);
            var groupDone = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < chances.Count; i++)
            {
                float chance = Clamp01(chances[i]);
                string group = groups != null && i < groups.Count && !string.IsNullOrEmpty(groups[i]) ? groups[i] : null;
                if (group == null)
                {
                    hits[i] = draws[i] < chance;
                    continue;
                }

                float u;
                if (!groupDraw.TryGetValue(group, out u))
                {
                    u = draws[i];
                    groupDraw[group] = u;
                    groupSum[group] = 0f;
                }

                float sum = groupSum[group] + chance;
                groupSum[group] = sum;
                if (groupDone.Contains(group)) continue;
                if (u < sum)
                {
                    hits[i] = true;
                    groupDone.Add(group);
                }
            }
            return hits;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }
    }
}
