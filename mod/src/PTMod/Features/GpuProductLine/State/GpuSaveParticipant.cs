using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The holder of the section <c>gpu-product-line</c> of the side save file, registered with <c>ctx.SaveParticipant</c> (FR-9). All the work is in
    /// <see cref="GpuSectionParticipant"/> and <see cref="GpuState"/>, which the unit tests can reach; this class only carries the core's interface
    /// <see cref="IModSaveParticipant"/>.
    /// </summary>
    internal sealed class GpuSaveParticipant : GpuSectionParticipant, IModSaveParticipant
    {
        public GpuSaveParticipant(GpuState state) : base(state)
        {
        }
    }
}
