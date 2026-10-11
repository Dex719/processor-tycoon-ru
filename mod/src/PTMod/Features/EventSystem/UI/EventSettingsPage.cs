using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The settings page of the feature in the hub "Моды" (<c>FeatureContext.HubPage</c>; FR-11). The hub draws the three parameters itself (the frequency slider, the
    /// switches "Пауза на всех событиях с выбором" and "Новости о конкурентах") from <see cref="EventParams"/>; the builder is called under them, only while the feature is on,
    /// and adds:
    /// <list type="bullet">
    /// <item>the line that says what each number of the frequency slider means;</item>
    /// <item>the button "Журнал событий", which closes the hub and opens the journal window;</item>
    /// <item>with the debug flag <c>debug.events</c> (only a debug build ever has it on, AC-11.2): the list of the events of the registry with the button "Запустить сейчас" that
    /// fires the event for the player today, conditions skipped, through <see cref="EventBus.Fire"/>.</item>
    /// </list>
    /// The builder runs under the guard of the hub: an exception is counted against the feature and the page shows a line instead. Main thread only.
    /// </summary>
    internal static class EventSettingsPage
    {
        private const float DebugListHeight = 320f;
        private const float DebugListMinHeight = 120f;
        private const float FireButtonWidth = 200f;

        /// <summary>Fills the vertical list the hub gave to the page.</summary>
        internal static void Build(RectTransform list)
        {
            EventEngine engine = EventSystemFeature.Instance != null ? EventSystemFeature.Instance.Engine : null;
            if (list == null) return;
            if (engine == null)
            {
                EventUiKit.Line(list, ModL10n.L("es.hub.unavailable"));
                return;
            }

            EventUiKit.Line(list, EventUiKit.Muted(EventSettings.FrequencyLegend(engine.Text)));
            ModUI.Button(list, EventUiKit.Safe(ModL10n.L("es.hub.open_log")), OpenLog);

            if (ModSettings.Debug(EventSettings.DebugFlag)) BuildDebugList(list, engine);
        }

        private static void OpenLog()
        {
            ModHooks.Guard(EventSystemApi.FeatureId, "hub/open-log", () =>
            {
                ModHub.Close();          // the hub is modal; the journal is a window of its own
                EventLog.OpenWindow();
            });
        }

        // ---- the debug list ----

        private static void BuildDebugList(RectTransform list, EventEngine engine)
        {
            EventUiKit.Line(list, ModL10n.L("es.hub.debug.title"), true);
            EventUiKit.Line(list, EventUiKit.Muted(ModL10n.L("es.hub.debug.hint")));
            TMP_Text answer = EventUiKit.Line(list, string.Empty);   // what the last press did

            IList<DebugEventRow> rows = EventSettings.DebugRows(engine.Catalog.All, engine.IsPackActive, engine.Text);
            if (rows.Count == 0)
            {
                EventUiKit.Line(list, ModL10n.L("es.hub.debug.empty"));
                return;
            }

            RectTransform content = EventUiKit.Scroll(list, DebugListMinHeight, DebugListHeight, 1f);
            if (content == null) return;

            string pack = null;
            foreach (DebugEventRow row in rows)
            {
                if (!string.Equals(row.PackId, pack, StringComparison.Ordinal))
                {
                    pack = row.PackId;
                    EventUiKit.Line(content, ModL10n.L("es.hub.debug.pack", string.IsNullOrEmpty(pack) ? "?" : pack), true);
                }
                AddRow(content, row, answer);
            }
        }

        private static void AddRow(RectTransform content, DebugEventRow row, TMP_Text answer)
        {
            RectTransform line = EventUiKit.Row(content, 0f, 8f);
            string detail = string.IsNullOrEmpty(row.Detail) ? row.EventId : row.EventId + " · " + row.Detail;
            TMP_Text label = ModUI.Text(line, EventUiKit.Safe(row.Title) + "\n" + EventUiKit.Muted(EventUiKit.Safe(detail)));
            EventUiKit.SetWidth(label, 0f, 1f);

            DebugEventRow captured = row;
            ButtonUI button = ModUI.Button(line, EventUiKit.Safe(ModL10n.L("es.hub.debug.fire")), () => Fire(captured, answer));
            if (button == null) return;
            EventUiKit.SetWidth(button, FireButtonWidth, 0f);
            if (!row.CanFire)
            {
                button.Interactable = false;
                ModUI.Tooltip(button.gameObject, string.Empty, EventUiKit.Safe(ModL10n.L(row.BlockedKey)));
            }
        }

        /// <summary>"Запустить сейчас": the event fires for the player today, its conditions skipped; the letter comes at once (AC-11.2).</summary>
        private static void Fire(DebugEventRow row, TMP_Text answer)
        {
            ModHooks.Guard(EventSystemApi.FeatureId, "hub/debug-fire", () =>
            {
                EventFireResult result = EventBus.Fire(row.EventId, EventSettings.DebugFire());
                string text = ModL10n.L("es.hub.debug.result", row.EventId, ModL10n.L("es.hub.debug.result." + result.ToString().ToLowerInvariant()));
                if (answer != null) answer.text = EventUiKit.Safe(text);
            });
        }
    }
}
