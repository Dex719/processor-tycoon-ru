using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// A record of <c>steps.json</c> as <c>ModContent</c> sees it (TSK-002): the identity that an override replaces or disables the built-in
    /// step by, around the <see cref="StepDefinition"/> that <see cref="StepMapper"/> read. The wrapper is the game layer's; the definition is pure logic.
    /// </summary>
    internal sealed class StepRecord : IContentRecord
    {
        internal StepRecord(StepDefinition definition)
        {
            Definition = definition;
        }

        /// <summary>The step; never null.</summary>
        internal StepDefinition Definition { get; private set; }

        /// <summary>The <c>id</c> of the JSON record, which is the id of the step.</summary>
        public string Id
        {
            get { return Definition.Id; }
        }
    }
}
