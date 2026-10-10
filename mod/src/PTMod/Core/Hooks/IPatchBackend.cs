namespace PTMod.Core
{
    /// <summary>
    /// What actually changes the game's methods (D2): Harmony (<see cref="HarmonyBackend"/>) or a fallback if spike 0002 fails (TSK-008). The
    /// features never see it: they declare <see cref="ModPatchAttribute"/> and <c>ModHooks</c> decides what to apply, so the backend can change
    /// without touching them. A backend never throws: failures come back as <c>false</c> and an English error.
    /// </summary>
    public interface IPatchBackend
    {
        /// <summary>For the log and the About tab: <c>"Harmony 2.3.6.0"</c>.</summary>
        string Name { get; }

        /// <summary>False for a kind the backend cannot apply; a feature that needs it is Unavailable (FR-2 item 7).</summary>
        bool Supports(PatchKind kind);

        /// <summary>Applies one patch under the feature's own owner id. On failure nothing of this patch stays in the game.</summary>
        bool TryApply(PatchSpec spec, out string error);

        /// <summary>Removes one applied patch (rolling back a core patch class); a patch that is not applied is ignored.</summary>
        void Remove(PatchSpec spec);

        /// <summary>Removes every patch of the feature and nothing else (another feature's patches on the same methods stay).</summary>
        void RemoveFeature(string featureId);
    }

    /// <summary>
    /// The backend when no real one could be loaded (0Harmony.dll missing or broken): nothing is applied, every feature that declares a
    /// patch is Unavailable with the reason, the rest of the mod works.
    /// </summary>
    internal sealed class UnavailableBackend : IPatchBackend
    {
        internal UnavailableBackend(string reason)
        {
            Reason = string.IsNullOrEmpty(reason) ? "no patch backend" : reason;
        }

        /// <summary>Why there is no backend (English, for the log).</summary>
        internal string Reason { get; }

        public string Name
        {
            get { return "none"; }
        }

        public bool Supports(PatchKind kind)
        {
            return false;
        }

        public bool TryApply(PatchSpec spec, out string error)
        {
            error = "no patch backend: " + Reason;
            return false;
        }

        public void Remove(PatchSpec spec)
        {
        }

        public void RemoveFeature(string featureId)
        {
        }

        public override string ToString()
        {
            return "none (" + Reason + ")";
        }
    }
}
