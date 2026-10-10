using System;
using System.Globalization;
using MenuGameSetupType = ProcessorTycoon.Menu.MenuGameSetup;
using PlayerInitialData = ProcessorTycoon.InitialData.PlayerInitialData;

namespace PTMod.Core
{
    /// <summary>
    /// P22: <c>MenuGameSetup.UpdateInitialData</c>, postfix. The game calls this private method to recompute the new-game settings from its dropdowns and
    /// the cheats toggle (from <c>InitializeUI</c>, from the handler of every one of them, from <c>OnEnable</c> of the window and on a theme change), and
    /// fills <c>playerInitialData</c> in it. The postfix hands that object (read through the injected private field <c>___playerInitialData</c>) to
    /// <see cref="ModNewGame"/>, which raises <see cref="ModNewGame.PreviewChanged"/> - a class of its own, so that if another game version changes this
    /// method only this patch is off (the core's unit of rollback). The method is reserved for the core (<c>ReservedTargets</c>, P22): a feature's patch on it
    /// is rejected with the hint to use <c>PreviewChanged</c>.
    /// </summary>
    internal static class PreviewPatches
    {
        private const string Site = "P22/postfix";

        [ModPatch(typeof(MenuGameSetupType), "UpdateInitialData", PatchKind.Postfix)]
        private static void UpdateInitialDataPostfix(PlayerInitialData ___playerInitialData)
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                ModNewGame.PreviewUpdated(___playerInitialData);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }

    public static partial class ModNewGame
    {
        private const string PreviewEvent = "ModNewGame.PreviewChanged";
        private const string PreviewSite = "P22/raise";

        private static PlayerInitialData _preview;
        private static string _lastRaised;       // the settings the handlers have been told about
        private static bool _previewDirty;       // the game recomputed the settings since the handlers were told
        private static bool _flushHooked;

        /// <summary>
        /// The settings of the new-game window as the game computed them (a read-only snapshot, null outside the window).
        /// They are filled by <c>MenuGameSetup.UpdateInitialData</c>: <c>Year</c>, <c>CompanyType</c>,
        /// <c>DifficultyLevel</c>, <c>StartingFunds</c>, <c>FactorySize</c>, <c>StartingTechnology</c>, <c>EnableCheats</c>. Read-only: this is a snapshot taken
        /// after the last recomputation, so nothing a handler writes into it reaches the game - the object that goes into <c>Player.InitializeAsNewGame</c> is
        /// the one <see cref="BeforeStart"/> gets. <c>Name</c>, <c>Founder</c> and <c>Color</c> are filled by the game only when "Start" is pressed. Null before the
        /// first recomputation and outside the new-game window (the game scene), and when the patch P22 is not applied.
        /// </summary>
        public static PlayerInitialData Preview
        {
            get { return _preview; }
        }

        /// <summary>
        /// The settings of the new-game window changed (start year, company type, difficulty, cheats): refresh your texts from <see cref="Preview"/>.
        /// Raised once per change, a frame after the game recomputed the settings (P22): the game recomputes them several times for one click on the difficulty list
        /// (it resets the company type and the cheats itself), and a recomputation that changes nothing - the window being shown again - raises nothing. The
        /// first recomputation of a menu scene always raises it. Handlers only update their own texts and never change <c>PlayerInitialData</c>
        /// (<see cref="BeforeStart"/> is the place for that). Called handler by handler: only for Enabled features, an exception is counted against the feature of
        /// the handler and the next handler still runs.
        /// </summary>
        public static event Action PreviewChanged;

        /// <summary>P22: the game recomputed the settings in <paramref name="live"/>; take a snapshot and tell the handlers on the next frame.</summary>
        internal static void PreviewUpdated(PlayerInitialData live)
        {
            if (live == null) return;

            _preview = Snapshot(live);
            _previewDirty = true;
            ScheduleFlush();
        }

        /// <summary>The scene is being left: the window and its settings are gone, so is the snapshot.</summary>
        private static void PreviewSceneLeaving()
        {
            if (_flushHooked)
            {
                _flushHooked = false;
                PTModHost.FrameUpdate -= FlushPreview;
            }
            _preview = null;
            _lastRaised = null;
            _previewDirty = false;
        }

        // The tell is deferred to the next frame so that the several recomputations of one click are one change with the final settings. Without a host
        // (nothing to wait for) it is done at once.
        private static void ScheduleFlush()
        {
            if (_flushHooked) return;
            if (ReferenceEquals(PTModHost.Instance, null))
            {
                FlushPreview();
                return;
            }

            _flushHooked = true;
            PTModHost.FrameUpdate += FlushPreview;
        }

        private static void FlushPreview()
        {
            if (_flushHooked)
            {
                _flushHooked = false;
                PTModHost.FrameUpdate -= FlushPreview;
            }

            try
            {
                PlayerInitialData snapshot = _preview;
                if (!_previewDirty || snapshot == null) return;
                _previewDirty = false;

                string stamp = Fingerprint(snapshot);
                if (string.Equals(stamp, _lastRaised, StringComparison.Ordinal)) return;   // recomputed, but nothing changed
                _lastRaised = stamp;

                ModHooks.Raise(PreviewEvent, PreviewChanged);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PreviewSite, ex);
            }
        }

        /// <summary>The settings that <c>UpdateInitialData</c> computes, as one text: equal texts mean nothing a handler could show has changed.</summary>
        private static string Fingerprint(PlayerInitialData data)
        {
            return string.Join("|", new[]
            {
                data.Year.ToString(CultureInfo.InvariantCulture),
                data.StartingFunds.ToString("R", CultureInfo.InvariantCulture),
                data.FactorySize.ToString(CultureInfo.InvariantCulture),
                ((int)data.StartingTechnology).ToString(CultureInfo.InvariantCulture),
                ((int)data.CompanyType).ToString(CultureInfo.InvariantCulture),
                data.DifficultyLevel.ToString(CultureInfo.InvariantCulture),
                data.EnableCheats ? "1" : "0",
            });
        }

        private static PlayerInitialData Snapshot(PlayerInitialData live)
        {
            return new PlayerInitialData
            {
                Name = live.Name,
                Founder = live.Founder,
                Color = live.Color,
                StartingFunds = live.StartingFunds,
                FactorySize = live.FactorySize,
                StartingTechnology = live.StartingTechnology,
                CompanyType = live.CompanyType,
                DifficultyLevel = live.DifficultyLevel,
                Year = live.Year,
                EnableCheats = live.EnableCheats,
            };
        }
    }
}
