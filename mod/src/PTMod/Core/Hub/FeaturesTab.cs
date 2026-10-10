using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Core
{
    /// <summary>
    /// What one feature row of the hub shows, worked out from the registry (FR-17 item 2). The rules are plain functions of the state, so they stay testable without Unity:
    /// <list type="bullet">
    /// <item>the toggle shows the player's choice (<see cref="ModFeatures.WantsEnabled"/>, a switch that waits for the scene to change included);</item>
    /// <item>the toggle can be flipped only when the feature is Enabled or Disabled; Unavailable and Faulted are shown with their reason and cannot be switched;</item>
    /// <item>the line under the toggle is the state, the note "takes effect after loading a save" when a switch waits (AC-3.2, FR-3 item 5), then the description.</item>
    /// </list>
    /// </summary>
    internal sealed class FeatureRowModel
    {
        /// <summary>The toggle position: what the player asked for.</summary>
        internal bool ToggleOn;

        /// <summary>The toggle can be flipped.</summary>
        internal bool Interactable;

        /// <summary>A switch of a feature without hot toggle waits for the scene to change.</summary>
        internal bool Pending;

        /// <summary>"Enabled", "Disabled", "Faulted - off until restart" or "Unavailable: reason".</summary>
        internal string StateText;

        /// <summary>State, note and description as the text under the toggle.</summary>
        internal string InfoLine;

        internal static FeatureRowModel Of(FeatureInfo info)
        {
            FeatureState state = ModFeatures.StateOf(info.Id);
            bool pending = ModFeatures.HasPendingChange(info.Id);

            var row = new FeatureRowModel();
            row.ToggleOn = ModFeatures.WantsEnabled(info.Id);
            row.Interactable = IsSwitchable(state);
            row.Pending = pending;
            row.StateText = StateTextOf(state, ModFeatures.ReasonOf(info.Id));
            row.InfoLine = Compose(row.StateText, pending ? ModL10n.L("core.feature.reloadRequired") : null, FeaturesTab.DescriptionOf(info));
            return row;
        }

        /// <summary>Only a feature that is Enabled or Disabled has a toggle to flip.</summary>
        internal static bool IsSwitchable(FeatureState state)
        {
            return state == FeatureState.Enabled || state == FeatureState.Disabled;
        }

        internal static string StateTextOf(FeatureState state, string reason)
        {
            switch (state)
            {
                case FeatureState.Enabled: return ModL10n.L("core.feature.state.enabled");
                case FeatureState.Faulted: return ModL10n.L("core.feature.state.faulted");
                case FeatureState.Unavailable: return ModL10n.L("core.feature.state.unavailable", reason ?? string.Empty);
                default: return ModL10n.L("core.feature.state.disabled");
            }
        }

        /// <summary>"State \u2014 note" (an em dash), then the description on its own line; empty parts are left out.</summary>
        internal static string Compose(string stateText, string note, string description)
        {
            string first = string.IsNullOrEmpty(note) ? stateText : stateText + " \u2014 " + note;
            return string.IsNullOrEmpty(description) ? first : first + "\n" + description;
        }
    }

    /// <summary>
    /// The Features tab of the hub (FR-17 items 2 and 8): one row per feature of the build, in dependency order. A row is the name with a toggle, the "Settings" button when the feature
    /// has a page or parameters, and a small text with the state (and the reason when the feature cannot run) and the description.
    /// <list type="bullet">
    /// <item><b>Switching.</b> The toggle calls <see cref="ModFeatures.SetEnabled"/> (the choice is saved at once). A feature with hot toggle changes state now (its patches and handlers go on or off), one
    /// without it, switched in the game scene, keeps its state and the row says that the change takes effect after loading a save (AC-3.2). A toggle of an Unavailable or Faulted feature is inactive; the reason is in the row and in its tooltip.</item>
    /// <item><b>Confirmation.</b> Switching off goes through <see cref="AskBeforeDisable"/> (FR-3 item 8, AC-3.3, AC-17.3): the feature's <c>ConfirmBeforeDisable</c> provider is asked
    /// (<see cref="ModFeatures.GetDisableConfirmation"/>); nothing to ask - the feature is switched off at once; otherwise <see cref="ModUI.Confirm"/> opens (time stands), the toggle stays on while
    /// the question is open, "Yes" switches the feature off (<c>OnDisabled</c> once), "Cancel" leaves it on. Only this tab asks; a switch from code never does.</item>
    /// <item><b>Refresh.</b> A change of any feature (a dependency cascade, the breaker, a switch from code) updates all rows in place with <see cref="ModUI.SetWithoutNotify(ToggleUI, bool)"/>; a feature that got a settings page since the
    /// rows were made (it registered when it was first switched on) rebuilds the tab.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class FeaturesTab
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string ConfirmTitleKey = "core.hub.confirm.title";
        private const string ConfirmDefaultKey = "core.hub.confirm.default";
        private const string YesKey = "core.common.yes";
        private const string CancelKey = "core.common.cancel";
        private const float SettingsButtonWidth = 150f;
        private const float RowGap = 8f;

        private sealed class Row
        {
            public FeatureInfo Info;
            public ToggleUI Toggle;
            public TMP_Text Line;
            public bool HasSettings;
        }

        private static readonly List<Row> Rows = new List<Row>();

        // Features whose switch-off question is open (or waits in the queue of ModUI.Confirm): another flip of the same toggle meanwhile asks nothing new.
        private static readonly HashSet<string> Asking = new HashSet<string>(StringComparer.Ordinal);

        // ---- names ----

        /// <summary>The name of the feature in the current language; the id when the feature gave no (known) key.</summary>
        internal static string NameOf(FeatureInfo info)
        {
            return !string.IsNullOrEmpty(info.NameKey) && ModL10n.Has(info.NameKey) ? ModL10n.L(info.NameKey) : info.Id;
        }

        /// <summary>The description of the feature in the current language; empty when there is none.</summary>
        internal static string DescriptionOf(FeatureInfo info)
        {
            return !string.IsNullOrEmpty(info.DescriptionKey) && ModL10n.Has(info.DescriptionKey) ? ModL10n.L(info.DescriptionKey) : string.Empty;
        }

        // ---- building ----

        /// <summary>The builder of the tab: fills the emptied body with the feature rows. A row that cannot be made is a log line and the others are made.</summary>
        internal static void Build(RectTransform body)
        {
            Rows.Clear();

            IReadOnlyList<FeatureInfo> features = ModFeatures.All;
            if (features.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("core.hub.features.empty"));
                return;
            }

            foreach (FeatureInfo info in features)
            {
                try
                {
                    BuildRow(body, info);
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "hub: cannot make the row of the feature '" + info.Id + "'", ex);
                }
            }
        }

        /// <summary>
        /// The rows are gone (the body was emptied, the hub closed, the scene left): nothing is refreshed. The open questions are forgotten too, so a question
        /// that never got its answer cannot lock a feature's toggle for good; answered later, they still act (a "Yes" switches the feature off, whatever the rows).
        /// </summary>
        internal static void Forget()
        {
            Rows.Clear();
            Asking.Clear();
        }

        private static void BuildRow(RectTransform body, FeatureInfo info)
        {
            FeatureRowModel model = FeatureRowModel.Of(info);
            bool settings = ModHub.HasSettings(info.Id);

            RectTransform box = ModUI.VerticalList(body, 2f);
            if (box == null) return;   // the log has said why
            VerticalLayoutGroup boxLayout = box.GetComponent<VerticalLayoutGroup>();
            if (boxLayout != null) boxLayout.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(RowGap));

            RectTransform line = HubWidgets.Row(box, "PTModFeatureLine", 0f, RowGap);
            ToggleUI toggle = ModUI.Toggle(line, NameOf(info), model.ToggleOn, on => OnToggled(info, on));
            if (toggle != null)
            {
                toggle.Interactable = model.Interactable;
                ModUI.Tooltip(toggle.gameObject, () => TipOf(info));
            }

            if (settings)
            {
                string featureId = info.Id;
                ButtonUI open = ModUI.Button(line, ModL10n.L("core.hub.feature.settings"), () => ModHub.ShowFeaturePage(featureId));
                HubWidgets.SetWidth(open, SettingsButtonWidth, 0f);
            }

            TMP_Text text = ModUI.Text(box, model.InfoLine);
            if (text != null)
            {
                text.richText = false;   // reasons and descriptions are plain text: a "<" in them is not a tag
                text.fontSize = Mathf.Max(11f, text.fontSize - 2f);
            }

            Rows.Add(new Row { Info = info, Toggle = toggle, Line = text, HasSettings = settings });
        }

        private static ModUI.TooltipText TipOf(FeatureInfo info)
        {
            string reason = ModFeatures.ReasonOf(info.Id);
            string description = DescriptionOf(info);
            string content = string.IsNullOrEmpty(reason) ? description : string.IsNullOrEmpty(description) ? reason : reason + "\n" + description;
            return new ModUI.TooltipText(NameOf(info), content);
        }

        // ---- switching ----

        private static void OnToggled(FeatureInfo info, bool on)
        {
            if (on)
            {
                Apply(info, true);
                return;
            }

            AskBeforeDisable(info, () => Apply(info, false), Refresh);
        }

        private static void Apply(FeatureInfo info, bool enabled)
        {
            ModFeatures.SetEnabled(info.Id, enabled);
            Refresh();   // a switch that waits for the scene raises no StateChanged, but the row has to say so
        }

        /// <summary>
        /// The confirmation of a switch-off (FR-3 item 8, AC-3.3, AC-17.3). Asks <c>ModFeatures.GetDisableConfirmation(info.Id)</c>: null - <paramref name="disable"/> runs at
        /// once; otherwise the toggle goes back to "on" (<paramref name="keep"/>; it moves only after the answer) and <see cref="ModUI.Confirm"/> asks with the feature's
        /// text and "Cancel" as the other button. "Yes" calls <paramref name="disable"/>, "Cancel", the cross, the end of the scene and a question that cannot be shown call
        /// <paramref name="keep"/>: the feature stays on and its <c>OnDisabled</c> does not run. While a question about the feature is open, flipping its toggle again only
        /// puts the toggle back. Only the hub asks; a switch from code never does.
        /// </summary>
        private static void AskBeforeDisable(FeatureInfo info, Action disable, Action keep)
        {
            string featureId = info.Id;
            if (Asking.Contains(featureId))
            {
                keep();
                return;
            }

            DisableConfirmation confirmation = ModFeatures.GetDisableConfirmation(featureId);
            if (confirmation == null)
            {
                disable();
                return;
            }

            keep();   // the toggle has flipped by itself; it goes back to "on" and moves only after the answer
            Asking.Add(featureId);

            try
            {
                ModUI.Confirm(
                    ModL10n.L(ConfirmTitleKey),
                    MessageOf(info, confirmation),
                    () =>
                    {
                        Asking.Remove(featureId);
                        disable();
                    },
                    () =>
                    {
                        Asking.Remove(featureId);
                        keep();
                    },
                    string.IsNullOrEmpty(confirmation.ConfirmLabelKey) ? YesKey : confirmation.ConfirmLabelKey,
                    CancelKey);
            }
            catch (Exception ex)
            {
                Asking.Remove(featureId);
                ModLog.Error(CoreId, "hub: cannot ask whether to turn off '" + featureId + "'; the feature stays on", ex);
            }
        }

        /// <summary>
        /// The body of the question: the feature's <c>MessageKey</c> with its <c>Args</c>; the hub's own "Turn off &lt;name&gt;?" when the key is empty (the provider failed
        /// or named none) or no table has it - the switch is never blocked by a slip in the text.
        /// </summary>
        internal static string MessageOf(FeatureInfo info, DisableConfirmation confirmation)
        {
            string key = confirmation == null ? null : confirmation.MessageKey;
            if (!string.IsNullOrEmpty(key))
            {
                if (ModL10n.Has(key)) return ModL10n.L(key, ArgsOf(confirmation.Args));
                ModLog.Warn(info.Id, "hub: the disable confirmation names the L10n key '" + key + "', which no table has; the hub asks its own question");
            }
            return ModL10n.L(ConfirmDefaultKey, NameOf(info));
        }

        private static object[] ArgsOf(string[] args)
        {
            if (args == null || args.Length == 0) return new object[0];

            var result = new object[args.Length];
            for (int i = 0; i < args.Length; i++) result[i] = args[i] ?? string.Empty;
            return result;
        }

        // ---- refresh ----

        /// <summary>Brings the rows in line with the registry without rebuilding them: toggle position, whether it can be flipped, the state line.</summary>
        internal static void Refresh()
        {
            if (Rows.Count == 0 || !ModHub.IsOpen) return;

            foreach (Row row in Rows)
            {
                if (row.Info == null || (row.Toggle == null && row.Line == null)) continue;

                if (ModHub.HasSettings(row.Info.Id) != row.HasSettings)
                {
                    ModHub.RefreshBody();   // the feature got (or lost) its page: the row needs a different shape
                    return;
                }

                FeatureRowModel model = FeatureRowModel.Of(row.Info);
                if (row.Toggle != null)
                {
                    ModUI.SetWithoutNotify(row.Toggle, model.ToggleOn);
                    row.Toggle.Interactable = model.Interactable;
                }
                if (row.Line != null) row.Line.text = model.InfoLine;
            }
        }
    }
}
