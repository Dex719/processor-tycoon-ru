namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The level of a rival's attitude to the player (FR-4 item 4), the label of the number that
    /// <see cref="IRivalPersonalitiesApi.GetAttitude"/> returns. The numbers are fixed by the spec and must not change.
    /// <list type="bullet">
    /// <item><see cref="Hostile"/>: attitude at most -60.</item>
    /// <item><see cref="Rivalry"/>: -59 to -20.</item>
    /// <item><see cref="Neutral"/>: -19 to +19.</item>
    /// <item><see cref="Respect"/>: +20 to +59.</item>
    /// <item><see cref="Partnership"/>: +60 and more.</item>
    /// </list>
    /// The exact borders come from <c>config.json</c> (<c>attitude.thresholds</c>); the list above is the default.
    /// <para>
    /// Build note: the file is pure (no <c>PTMod.Core</c>, Unity or game types) and lies in <c>Api/Contracts</c> because other features compile
    /// only the <c>Api</c> folder of <c>rival-personalities</c> (<c>mod/build_feature.py</c>). The unit-test builds compile <c>Api/Contracts</c>
    /// together with <c>Logic</c>, so <c>Logic/</c> (for example <c>AttitudeMath.LevelOf</c>) uses this enum directly: do not declare a second
    /// <c>AttitudeLevel</c> in the feature.
    /// </para>
    /// </summary>
    public enum AttitudeLevel
    {
        Hostile = 0,
        Rivalry = 1,
        Neutral = 2,
        Respect = 3,
        Partnership = 4
    }
}
