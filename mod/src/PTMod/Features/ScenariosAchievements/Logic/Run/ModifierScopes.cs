using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The scope of a modifier of the content (<see cref="ModifierSpec.AppliesTo"/>: <c>player</c>, <c>ai</c>, <c>all</c>) as the scope of an entry of
    /// <c>ModModifiers</c> (design.md, "Испытания и формулы игры"; D15): the player's company, every AI company, every company. Kept apart from
    /// <c>ModifierBinder</c> so the mapping is tested without the game. An unknown word means the player (the validator refuses such a record, so this
    /// is only a safe default).
    /// </summary>
    public static class ModifierScopes
    {
        public static ModifierScope Of(string appliesTo)
        {
            switch (appliesTo)
            {
                case "ai": return ModifierScope.AIOnly();
                case "all": return ModifierScope.AllCompanies();
                default: return ModifierScope.Player();
            }
        }
    }
}
