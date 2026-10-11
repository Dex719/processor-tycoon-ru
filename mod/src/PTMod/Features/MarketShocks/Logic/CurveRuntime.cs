namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// Where a curve effect writes its multiplier: the contribution of one effect to one channel for one company. In the game it is the
    /// <c>IEffectModifiers</c> of <c>event-system</c> bound to the effect, its channel and its company key; in the tests, a recorder.
    /// </summary>
    public interface IContributionSink
    {
        /// <summary>Sets the multiplier of the effect (replacing the one it set yesterday).</summary>
        void Set(float multiplier);

        /// <summary>Takes the contribution of the effect out of the channel.</summary>
        void Clear();
    }

    /// <summary>
    /// What <c>ModifierRamp</c> and <c>ModifierWave</c> do every day and after a load (design.md, "Типы эффектов", "Подписки на события"): read the saved
    /// numbers of the effect, work out today's multiplier from them and put it into the channel. <c>Apply</c>, <c>Tick</c> and <c>Restore</c> are all
    /// one call here, with the age of the effect as the only input, so an effect restored in the middle of its curve gets exactly the multiplier an
    /// effect that has been ticking every day would have (FR-7 item 4). Numbers that do not pass the checks of <see cref="RampCurve.TryRead"/> and
    /// <see cref="WaveCurve.TryRead"/> drop the effect: the contribution is taken out and nothing is set (the engine's effect stays in the list and
    /// does nothing). Pure.
    /// </summary>
    public static class CurveRuntime
    {
        /// <summary>
        /// The age of an effect that is being applied: the days since <paramref name="startDay"/> when the engine has already given the effect its end
        /// day (<paramref name="endDay"/> above 0), otherwise 0 - the effect starts today and the first multiplier of every curve is 1.
        /// </summary>
        public static int AgeAtApply(int startDay, int endDay, int today)
        {
            if (endDay <= 0) return 0;
            int age = today - startDay;
            return age < 0 ? 0 : age;
        }

        /// <summary>
        /// Puts the multiplier of a ramp on day <paramref name="age"/> into the channel. False, with the contribution taken out, when the saved
        /// <paramref name="curve"/> is not usable (the reason is in <paramref name="problem"/>).
        /// </summary>
        public static bool PushRamp(float[] curve, int age, IContributionSink sink, out string problem)
        {
            RampCurve ramp;
            if (!RampCurve.TryRead(curve, out ramp, out problem))
            {
                if (sink != null) sink.Clear();
                return false;
            }

            if (sink != null) sink.Set(ramp.ValueAt(age));
            return true;
        }

        /// <summary>
        /// Puts the multiplier of a wave on day <paramref name="age"/> into the channel. A wave that has lived its <paramref name="totalDays"/> (the
        /// <c>durationDays</c> of the effect, <c>period · cycles</c>; 0 or less means "unknown") puts 1: the engine closes the effect at its end day, but
        /// a call on that very day must not leave a stray half-wave. False, with the contribution taken out, when the saved <paramref name="curve"/>
        /// is not usable.
        /// </summary>
        public static bool PushWave(float[] curve, int totalDays, int age, IContributionSink sink, out string problem)
        {
            WaveCurve wave;
            if (!WaveCurve.TryRead(curve, out wave, out problem))
            {
                if (sink != null) sink.Clear();
                return false;
            }

            float value = totalDays > 0 && age >= totalDays ? 1f : wave.ValueAt(age);
            if (sink != null) sink.Set(value);
            return true;
        }

        /// <summary>
        /// The current multiplier of a saved ramp for the list of active effects (<c>ICurveEffectHandler.ValueAt</c>); <paramref name="fallback"/> when the
        /// curve is not usable.
        /// </summary>
        public static float RampValue(float[] curve, int age, float fallback)
        {
            RampCurve ramp;
            string problem;
            return RampCurve.TryRead(curve, out ramp, out problem) ? ramp.ValueAt(age) : fallback;
        }

        /// <summary>The current multiplier of a saved wave; see <see cref="RampValue"/>.</summary>
        public static float WaveValue(float[] curve, int totalDays, int age, float fallback)
        {
            WaveCurve wave;
            string problem;
            if (!WaveCurve.TryRead(curve, out wave, out problem)) return fallback;
            return totalDays > 0 && age >= totalDays ? 1f : wave.ValueAt(age);
        }
    }
}
