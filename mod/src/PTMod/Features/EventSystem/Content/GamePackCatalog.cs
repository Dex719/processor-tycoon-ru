using System;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>Builds the <see cref="PackCatalog"/> of the running mod: <c>ModContent</c> for the files, <c>ModLog</c> for the lines, the engine's handler table for the types.</summary>
    internal static class GamePackCatalog
    {
        /// <param name="handlerOf">The engine's table of effect handlers; null - <c>EventEffects.Get</c>.</param>
        public static PackCatalog Create(Func<string, IEventEffectHandler> handlerOf = null)
        {
            return new PackCatalog(
                new CoreContentSource(),
                new CoreContentEnvironment(handlerOf),
                line => ModLog.Warn(EventSystemApi.FeatureId, line),
                line => ModLog.Info(EventSystemApi.FeatureId, line));
        }
    }
}
