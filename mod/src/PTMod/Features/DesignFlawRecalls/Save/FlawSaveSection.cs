using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The game-build half of <see cref="FlawSaveSection"/> (design.md, Component 17: <c>FlawSaveSection : IModSaveParticipant</c>). The whole logic
    /// of the section is in <c>Logic/Save/FlawSaveSection.cs</c>, which the unit tests compile (they cannot see <c>IModSaveParticipant</c>, a type of
    /// the core outside <c>Core/Logic</c>); this file only adds the game-side interface that <c>ctx.SaveParticipant</c> takes. The interface
    /// extends the logic-side one without adding a member.
    /// </summary>
    public sealed partial class FlawSaveSection : IModSaveParticipant
    {
    }
}
