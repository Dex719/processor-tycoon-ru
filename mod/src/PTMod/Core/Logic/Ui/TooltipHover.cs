using System;

namespace PTMod.Core.Logic.Ui
{
    /// <summary>The game's tooltip as the hover rules see it.</summary>
    internal interface ITooltipHost
    {
        /// <summary>Asks the game to show the tooltip (after its own delay). False when it could not be asked: no tooltip in this scene, the bridge is off, the call failed.</summary>
        bool Start(string header, string content);

        /// <summary>Cancels the tooltip that was asked for. Called only after a <see cref="Start"/> that returned true.</summary>
        void Cancel();
    }

    /// <summary>Reads the text of one hover; false when there is nothing to show (a provider that failed, for one).</summary>
    internal delegate bool TooltipTextSource(out string header, out string content);

    /// <summary>
    /// What the pointer does to one element's tooltip (FR-16 item 9, AC-16.4): entering reads the text (the provider runs on every enter, not before) and asks the game for the tooltip once;
    /// leaving cancels it, but only if it was asked for, so a hidden element, a second leave or a scene without tooltips never reaches the game's <c>CancelTrigger</c>.
    /// Exceptions of the source or the host propagate to the caller (the Unity handler counts them); the state stays consistent.
    /// </summary>
    internal sealed class TooltipHover
    {
        private readonly ITooltipHost _host;
        private readonly TooltipTextSource _source;

        public TooltipHover(ITooltipHost host, TooltipTextSource source)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (source == null) throw new ArgumentNullException("source");

            _host = host;
            _source = source;
        }

        /// <summary>The tooltip was asked for and not yet cancelled.</summary>
        public bool Showing { get; private set; }

        /// <summary>The pointer entered the element.</summary>
        public void PointerEntered()
        {
            if (Showing) return;   // already asked for; the provider is not called again until the pointer has left

            string header;
            string content;
            if (!_source(out header, out content)) return;

            header = header ?? string.Empty;
            content = content ?? string.Empty;
            if (header.Length == 0 && content.Length == 0) return;   // nothing to say, nothing to show

            Showing = _host.Start(header, content);
        }

        /// <summary>The pointer left the element, or the element was hidden or destroyed.</summary>
        public void PointerExited()
        {
            if (!Showing) return;

            Showing = false;   // first: a failing Cancel must not leave the element believing a tooltip is up
            _host.Cancel();
        }
    }
}
