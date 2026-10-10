using System;

namespace PTMod.Core
{
    /// <summary>
    /// What a feature uses in <see cref="IModFeature.Register"/> to declare its needs (FR-3). One object per feature for the whole session:
    /// a feature may keep it and declare more later (in <c>OnEnabled</c>, on <c>GameReady</c>). Every member is bound to
    /// <see cref="FeatureId"/>, so a feature cannot declare anything under another feature's id.
    /// <para>
    /// The class is partial: a core service may add its own declaration method next to its code;
    /// <see cref="Patch"/> (<c>ModHooks</c>), <see cref="OnTick"/> (<c>ModTicks</c>), <see cref="SaveParticipant"/> (<c>ModSave</c>),
    /// <see cref="Param"/> (<c>ModSettings</c>), <see cref="HubPage"/> (<c>ModHub</c>) and <see cref="ConfirmBeforeDisable"/> (the registry and the
    /// hub) live here. Nothing here throws into the feature: a bad declaration is logged under the feature's id and ignored.
    /// </para>
    /// </summary>
    public sealed partial class FeatureContext
    {
        internal FeatureContext(FeatureInfo info)
        {
            Info = info;
        }

        /// <summary>The id of the feature this context belongs to.</summary>
        public string FeatureId
        {
            get { return Info.Id; }
        }

        /// <summary>The registry entry of the feature.</summary>
        internal FeatureInfo Info { get; }

        /// <summary>
        /// Declares the patches of the feature: the <see cref="ModPatchAttribute"/> methods of <paramref name="patchClass"/>, a static class in
        /// the feature's namespace (FR-2). Call it in <see cref="IModFeature.Register"/>; the patches go into the game when the feature starts,
        /// all or nothing for the required ones, and come out when it stops. A required patch whose target is missing, reserved for the core or
        /// another feature (<c>ModHooks.IsReserved</c>), or already holds another feature's transpiler makes the feature Unavailable with the
        /// reason; the log names the target. A class declared while the feature runs (in <c>OnEnabled</c>) is applied at once.
        /// </summary>
        public void Patch(Type patchClass)
        {
            try
            {
                ModHooks.Declare(FeatureId, patchClass);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "hooks: cannot declare the patches of " + (patchClass == null ? "null" : patchClass.FullName), ex);
            }
        }

        /// <summary>
        /// Declares a parameter of the feature (a hub slider, toggle or number; FR-6). <see cref="ParamSpec.FeatureId"/> is always this
        /// feature's id (another id is overridden with a warning). The value comes from <c>ptmod_settings.json</c>, or the default when the file
        /// has none or a bad one; read it with <see cref="ModSettings.GetFloat"/> and the like. The spec is copied, later changes to it are not seen.
        /// </summary>
        public void Param(ParamSpec spec)
        {
            try
            {
                if (spec == null)
                {
                    ModLog.Warn(FeatureId, "settings: Param(null) is ignored");
                    return;
                }

                ParamSpec copy = spec.Clone();
                if (!string.IsNullOrEmpty(copy.FeatureId) && !string.Equals(copy.FeatureId, FeatureId, StringComparison.Ordinal))
                {
                    ModLog.Warn(FeatureId, "settings: parameter '" + copy.Key + "' names feature '" + copy.FeatureId + "', it is declared for '" + FeatureId + "'");
                }
                copy.FeatureId = FeatureId;
                ModSettings.Declare(copy);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "settings: cannot declare a parameter", ex);
            }
        }

        /// <summary>
        /// Gives the feature a settings page in the hub (FR-17): the "Settings" button of the feature's row opens it under a "Back" button and the heading
        /// <paramref name="titleKey"/> (a <see cref="ModL10n"/> key; empty gives "Settings: &lt;feature name&gt;"). The parameters declared with <see cref="Param"/> get their toggles
        /// and sliders on the page by themselves; <paramref name="build"/> adds what they cannot express. It is called every time the page is opened, with the emptied
        /// container of the page, while the feature is Enabled: fill it with the widgets of <see cref="ModUI"/> (<c>ModUI.Button</c>, <c>Toggle</c>, <c>Slider</c>, <c>Text</c>, ...).
        /// An exception in it is counted against the feature and the page shows a line instead. One page per feature, a second call replaces the first; a feature that only
        /// declares parameters needs no page. Never throws.
        /// </summary>
        public void HubPage(string titleKey, Action<UnityEngine.RectTransform> build)
        {
            try
            {
                ModHub.RegisterPage(FeatureId, titleKey, build);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "hub: cannot register the settings page", ex);
            }
        }

        /// <summary>
        /// Asks the hub to question the player before it switches the feature off (FR-3 item 8, FR-17 item 3): <c>ctx.ConfirmBeforeDisable(BuildConfirmation)</c>.
        /// When the player flips the feature's toggle to "off", the hub calls <paramref name="provider"/> (while the feature is Enabled, on the main thread, under the
        /// guard of this feature). A <see cref="DisableConfirmation"/> makes the hub open <c>ModUI.Confirm</c> with its text, time standing: "Yes" switches the feature
        /// off (<c>OnDisabled</c> runs once), "Cancel" - or any other way out of the window - leaves it on and <c>OnDisabled</c> is not called. <c>null</c> switches it off
        /// without a question (the usual answer when there is nothing to lose). A provider that throws is counted against the feature and the hub asks its own plain
        /// "Turn off &lt;name&gt;?". Only the hub asks: <c>ModFeatures.SetEnabled(id, false)</c> from code, a dependency that goes away and the circuit breaker never do.
        /// One provider per feature, a second call replaces the first, <c>null</c> removes it; the provider stays for the session. Never throws.
        /// </summary>
        public void ConfirmBeforeDisable(Func<DisableConfirmation> provider)
        {
            try
            {
                ModFeatures.SetDisableConfirmationProvider(FeatureId, provider);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "hub: cannot register the disable confirmation", ex);
            }
        }

        /// <summary>
        /// Subscribes a handler of the feature to a phase of the game day (FR-4): <c>ctx.OnTick(TickPhase.DayPassed, OnDay)</c>, not a
        /// subscription to <c>DateController</c> of its own. Handlers of a phase run after the game's, by <paramref name="priority"/> (higher
        /// first), then in the order of subscription; each in try/catch, a failure counted against the feature (FR-2). They are skipped while
        /// the feature is not active, during the new-game fast-forward and while the loading screen is up, unless <paramref name="options"/>
        /// say otherwise; <see cref="TickPhase.FastForwardEnded"/> reaches every subscriber. During the batch run of a month
        /// (<see cref="ModTicks.IsBatchRun"/>) they are called for every day as usual. The subscription lasts the session (it survives scene
        /// loads; <c>Register</c> runs once); dispose the token to end it earlier. Never throws.
        /// </summary>
        public IDisposable OnTick(TickPhase phase, Action handler, int priority = 0, TickOptions options = TickOptions.None)
        {
            try
            {
                return ModTicks.Subscribe(FeatureId, phase, handler, priority, options);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "ticks: cannot subscribe to " + phase, ex);
                return ModTicks.NoSubscription;
            }
        }

        /// <summary>
        /// Registers the feature's holder of a section of the side save file (FR-5): <c>ctx.SaveParticipant(new MySaveParticipant())</c>. The
        /// core calls it - <c>Read</c> when a save is loaded, <c>Write</c> after each save, <c>ResetToDefaults</c> for a new game, the menu, a
        /// save without the section - so the feature never touches the game's save or the file. <c>SectionId</c> must be
        /// the feature's id (otherwise the participant is refused with an error in the log). The version of the section (<c>_schema</c>) is
        /// the core's business: the participant only reports its <c>SchemaVersion</c>. Call it in <see cref="IModFeature.Register"/>;
        /// called later, the participant gets its data when the feature is active. One participant per feature; never throws.
        /// </summary>
        public void SaveParticipant(IModSaveParticipant participant)
        {
            try
            {
                ModSave.RegisterFor(FeatureId, participant);
            }
            catch (Exception ex)
            {
                ModLog.Error(FeatureId, "save: cannot register the save participant", ex);
            }
        }

        public override string ToString()
        {
            return "FeatureContext(" + FeatureId + ")";
        }
    }
}
