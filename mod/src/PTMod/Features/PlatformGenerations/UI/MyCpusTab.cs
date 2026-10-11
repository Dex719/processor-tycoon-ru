using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The tab "Мои CPU" (FR-9 items 4 and 5, FR-5): for every released CPU of the player that is not retired - its platform and socket, whether it is a
    /// "full platform" (the memory of the CPU is one of the memories of its generation), the three parts of the rating multiplier (the installed base,
    /// the early transition, the penalty for changing the socket), the totals for the consumer segments and for "Промышленность", and what the OEM clients make
    /// of it: in the "per contract" mode how many of the offered contracts prefer a newer platform and the mean multiplier over them, and the "market"
    /// multiplier. Hovering the platform line shows the risk of changing the socket (<see cref="SocketRiskView"/>): how long the main socket has been
    /// supported, how many months are left until the minimum, and the penalty for leaving it today. The numbers are <see cref="MyCpusView"/>'s (pure,
    /// tested); this class only lays them out.
    /// </summary>
    internal static class MyCpusTab
    {
        /// <summary>Fills the emptied body of the window.</summary>
        public static void Build(RectTransform body, PlatformGenerationsFeature feature)
        {
            if (!feature.CatalogReady)
            {
                ModUI.Text(body, ModL10n.L("pg.window.notReady"));
                return;
            }

            PlatformSnapshot snapshot = PlatformWindowData.Snapshot(feature);
            ViewText text = PlatformViewText.Instance;

            var cpus = new List<MyCpuInput>();
            var contracts = new List<OemContractInput>();
            CollectCpus(cpus);
            GameAdapter.CollectOemContracts(contracts);

            if (cpus.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("pg.cpu.empty"));
                return;
            }

            List<MyCpuRow> rows = MyCpusView.Build(cpus, ModCompanies.PlayerKey, snapshot, contracts, text);
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) PlatformUi.Rule(body);
                AddRow(body, rows[i], feature, text);
            }
        }

        private static void CollectCpus(List<MyCpuInput> into)
        {
            var all = new List<GameCpu>();
            if (GameAdapter.CollectCpus(all)) GameAdapter.CollectPlayerCpus(all, into);
            all.Clear();
        }

        private static void AddRow(RectTransform body, MyCpuRow row, PlatformGenerationsFeature feature, ViewText text)
        {
            RectTransform list = ModUI.VerticalList(body, 2f);
            if (list == null) return;

            ModUI.Text(list, row.Title, true);

            TMP_Text platform = ModUI.Text(list, row.PlatformLine);
            if (platform != null && !string.IsNullOrEmpty(row.SocketKey))
            {
                string socketKey = row.SocketKey;
                platform.raycastTarget = true;   // the tooltip needs something under the pointer
                ModUI.Tooltip(platform.gameObject, () =>
                {
                    SocketRisk risk = SocketRiskView.Build(ModCompanies.PlayerKey, socketKey, PlatformWindowData.Snapshot(feature), text);
                    return new ModUI.TooltipText(risk.Header, risk.Content);
                });
            }

            if (row.Lines.Count > 0) ModUI.Text(list, string.Join("\n", row.Lines.ToArray()));
        }
    }
}
