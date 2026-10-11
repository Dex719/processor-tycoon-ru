using System;
using System.Runtime.CompilerServices;
using UnityTransform = UnityEngine.Transform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Brings a window of the game to the front of the others, the way the game does when the player clicks it (<c>WindowHierarchyBehaviour</c> makes the
    /// window the last child of its parent). Used when <c>ResearchTreeUxApi.OpenTab</c> opens the research window that may be open already behind another
    /// one. A window without the component, or a game without the class, is left where it is. Never throws.
    /// </summary>
    internal static class WindowFront
    {
        public static void BringToFront(UnityTransform window)
        {
            if (window == null) return;

            try
            {
                Raise(window);
            }
            catch (Exception)
            {
                // The class of the game is not the one the mod was built for: the window stays in its place, which is harmless.
            }
        }

        // The game's type is touched only here: when it does not match, the JIT fails in this method and BringToFront catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Raise(UnityTransform window)
        {
            ProcessorTycoon.UI.WindowHierarchyBehaviour behaviour = window.GetComponentInParent<ProcessorTycoon.UI.WindowHierarchyBehaviour>(true);
            if (behaviour != null) behaviour.UpdateHierarchy();
        }
    }
}
