using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameFactory = ProcessorTycoon.Production.Factory;
using GameInfoText = ProcessorTycoon.UI.InfoText;
using GameProductionUI = ProcessorTycoon.Production.UI.ProductionUI;
using GameProductionWindow = ProcessorTycoon.Production.UI.ProductionWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// GP1 (design.md, "Патчи Harmony"; FR-5 item 8, TSK-021): a postfix on <c>ProductionWindow.UpdateAvailableProductionLines</c> (private; the game calls it every
    /// frame the production screen is up and when a day ends). Only the screen changes, never the factory. When the player's cards hold <c>N &gt; 0</c> lines today,
    /// the label "Использовано" gets " (+N GPU)" and, in the default mode, every row of the CPUs (<c>productionUIs</c>) gets
    /// <c>ProductionUI.UpdateAvailableProductionLines(max(0, free - N))</c>, where <c>free</c> is counted from the same lists as the game counts it (<c>cpus</c> without
    /// the outsourced ones: production and contract lines; <c>clientCpus</c>: both). With "CPU first" the rows keep the game's number and only the label is added.
    /// With no reserve the method is the game's. The fields come by Harmony's names (<c>___linesUsedText</c> and the others): a game without one of them does not
    /// take the patch, which is optional - the feature stays on and the self-check switches off <see cref="GpuCapability.ProductionUi"/> only (the cards are produced
    /// all the same). The body runs under <c>GpuFeature.Guard</c>: an exception is counted against the feature and leaves the screen as the game drew it.
    /// </summary>
    internal static class ProductionWindowPatch
    {
        private const string Site = "gp1/postfix";

        [ModPatch(typeof(GameProductionWindow), "UpdateAvailableProductionLines", PatchKind.Postfix, Optional = true)]
        private static void PostfixUpdateAvailableProductionLines(GameInfoText ___linesUsedText, List<GameProductionUI> ___productionUIs, List<GameCpu> ___cpus,
            List<GameCpu> ___clientCpus)
        {
            GpuFeature feature = GpuFeature.Instance;
            if (feature == null) return;
            int lines = feature.ProductionScreenLines;
            if (!GpuProductionScreen.ShowsCards(lines)) return;   // nothing reserved: the screen is the game's
            feature.Guard(Site, () => Apply(feature, lines, ___linesUsedText, ___productionUIs, ___cpus, ___clientCpus));
        }

        private static void Apply(GpuFeature feature, int lines, GameInfoText used, List<GameProductionUI> rows, List<GameCpu> cpus, List<GameCpu> clientCpus)
        {
            if (used != null) used.Text = used.Text + ModL10n.L(GpuProductionScreen.ReservedKey, lines);
            if (feature.CpuPriority || rows == null || rows.Count == 0) return;

            ICompany player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            GameFactory factory = player != null ? player.Factory : null;
            if (factory == null) return;
            int free = GpuProductionScreen.FreeForCpus(factory.ProductionCapacity, UsedLines(cpus, clientCpus), lines, false);
            foreach (GameProductionUI row in rows)
            {
                if (row != null) row.UpdateAvailableProductionLines(free);
            }
        }

        /// <summary>The lines in use as the screen counts them: the player's CPUs that are not outsourced (production and contracts) and the foundry clients' CPUs (both).</summary>
        private static int UsedLines(List<GameCpu> cpus, List<GameCpu> clientCpus)
        {
            long used = 0;
            if (cpus != null)
            {
                foreach (GameCpu cpu in cpus)
                {
                    if (cpu == null || cpu.IsOutsourced) continue;
                    used += cpu.CurrentProductionLines + (long)cpu.CurrentContractedProductionLines;
                }
            }
            if (clientCpus != null)
            {
                foreach (GameCpu cpu in clientCpus)
                {
                    if (cpu != null) used += cpu.CurrentProductionLines + (long)cpu.CurrentContractedProductionLines;
                }
            }
            return used > int.MaxValue ? int.MaxValue : (int)used;
        }
    }
}
