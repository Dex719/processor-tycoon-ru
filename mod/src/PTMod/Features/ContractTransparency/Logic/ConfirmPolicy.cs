namespace PTMod.Features.ContractTransparency
{
    /// <summary>When the player is asked to confirm an offer (FR-4). The numbers are the values of the <c>confirmMode</c> setting.</summary>
    public enum ConfirmMode
    {
        /// <summary>0: every manual offer opens the review screen.</summary>
        Always = 0,

        /// <summary>1 (the default, a decision of the owner): the screen opens only when a risk was found.</summary>
        Risky = 1,

        /// <summary>2: manual offers go through without a question (they are still logged).</summary>
        Never = 2,
    }

    /// <summary>The rule behind the review screen (FR-4 items 1-3) and the translation of the stored setting.</summary>
    public static class ConfirmPolicy
    {
        /// <summary>The mode used when the stored value is not one of the three.</summary>
        public const ConfirmMode DefaultMode = ConfirmMode.Risky;

        /// <summary>
        /// Whether a manual offer needs the player's confirmation: <see cref="ConfirmMode.Always"/> yes; <see cref="ConfirmMode.Risky"/> yes when
        /// <paramref name="risks"/> is not <see cref="RiskFlags.None"/>; <see cref="ConfirmMode.Never"/> no. A mode that is not one of the three
        /// counts as <see cref="DefaultMode"/>.
        /// </summary>
        public static bool NeedsConfirmation(ConfirmMode mode, RiskFlags risks)
        {
            switch (mode)
            {
                case ConfirmMode.Always:
                    return true;
                case ConfirmMode.Never:
                    return false;
                default:
                    return risks != RiskFlags.None;
            }
        }

        /// <summary>The mode for the stored setting (0, 1, 2); <see cref="DefaultMode"/> for anything else.</summary>
        public static ConfirmMode FromSetting(int value)
        {
            switch (value)
            {
                case (int)ConfirmMode.Always:
                    return ConfirmMode.Always;
                case (int)ConfirmMode.Never:
                    return ConfirmMode.Never;
                default:
                    return DefaultMode;
            }
        }

        /// <summary>The value of the setting for a mode (the number of the mode).</summary>
        public static int ToSetting(ConfirmMode mode)
        {
            return (int)mode;
        }

        /// <summary>
        /// Whether the switch "don't ask again for safe offers" is shown on the review screen (FR-4 item 4): only where every offer is asked about. In the other modes the screen opens for a risk alone, so a
        /// safe offer goes through already.
        /// </summary>
        public static bool DontAskIsShown(ConfirmMode current)
        {
            return FromSetting(ToSetting(current)) == ConfirmMode.Always;
        }

        /// <summary>
        /// The mode after the switch was flipped: on, from "always" to "only risky" (<paramref name="changedHere"/> becomes true so the switch can take it back); off, back to "always" if the switch itself made
        /// the change. Any other case leaves the mode alone.
        /// </summary>
        public static ConfirmMode AfterDontAsk(ConfirmMode current, bool on, bool changedHere, out bool changedHereNow)
        {
            ConfirmMode mode = FromSetting(ToSetting(current));
            changedHereNow = changedHere;

            if (on)
            {
                if (mode != ConfirmMode.Always) return mode;
                changedHereNow = true;
                return ConfirmMode.Risky;
            }

            if (!changedHere) return mode;
            changedHereNow = false;
            return mode == ConfirmMode.Risky ? ConfirmMode.Always : mode;
        }
    }
}
