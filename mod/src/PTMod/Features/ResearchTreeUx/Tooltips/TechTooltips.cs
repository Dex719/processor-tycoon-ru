using System;
using PTMod.Core;
using UnityGameObject = UnityEngine.GameObject;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The adapter between the texts of the tooltip (<see cref="TipText"/>) and the tooltips of the core (FR-4 item 1, decision D4):
    /// <see cref="Attach"/> puts <c>ModUI.Tooltip</c> on an object with a provider that makes the text when the cursor comes onto it. The bridge
    /// to the game's <c>TooltipHandler</c> (the native delay and look, the position, no error without a handler) is one for all features and
    /// lives in <c>ModUI</c>; this class has no tooltip data of its own and starts no trigger of its own. The provider runs under the guard of
    /// <c>research-tree-ux</c> (the lambda below belongs to its namespace), so an exception counts against the feature and shows nothing.
    /// </summary>
    internal static class TechTooltips
    {
        /// <summary>
        /// Puts a tooltip on <paramref name="target"/>: <paramref name="provider"/> is called on every pointer enter and its text is shown after
        /// the native delay. A second call on the same object replaces the first. A null object or provider does nothing (the core logs it).
        /// The object needs a graphic that takes the pointer (a button has one).
        /// </summary>
        public static void Attach(UnityGameObject target, Func<TipText> provider)
        {
            if (target == null || provider == null) return;

            ModUI.Tooltip(target, () =>
            {
                TipText text = provider();
                return new ModUI.TooltipText(text.Header, text.Content);
            });
        }

        /// <summary>
        /// Takes the tooltip off <paramref name="target"/> (the feature is switched off, FR-8 item 3). The core has no call that removes a hover
        /// handler, so the text is replaced by an empty one, which shows nothing; the handler itself goes with the object.
        /// </summary>
        public static void Detach(UnityGameObject target)
        {
            if (target == null) return;
            ModUI.Tooltip(target, string.Empty, string.Empty);
        }
    }
}
