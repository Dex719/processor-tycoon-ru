using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameReleaseWindow = ProcessorTycoon.ProjectSystem.ProjectReleaseWindow;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The warning in the release window (FR-7; design, Component 7): when the window of releasing a CPU opens, the player is told what the release
    /// will cost or give - the penalty for changing the socket ("Смена сокета: −6.7 % ... Основной сокет Socket A поддерживается 12 мес. из 36") or the
    /// early transition ("Ранний переход на ...: +6 % на 60 мес.") - before the button "Выпустить" is pressed, which cannot be undone. The answer is
    /// <see cref="PlatformTimeline.Preview"/>, the very code that books the release, so the warning and the result agree. For a CPU that changes
    /// nothing, a variant, a CPU outside the generations or the switch of notifications off, nothing is shown. Nothing is written: the state of the
    /// feature is the same before and after.
    /// <para>
    /// The only patch of the feature, a postfix on <c>ProjectReleaseWindow.Open(Cpu)</c>; <c>Optional</c>, so a game in which the method is gone
    /// costs the feature only this warning. Release itself comes from the core's event (<c>ModEvents.CpuReleased</c>), not from a patch of
    /// <c>AddCpu</c>.
    /// </para>
    /// </summary>
    internal static class ReleaseWarning
    {
        private const string Site = "ProjectReleaseWindow.Open/postfix";

        [ModPatch(typeof(GameReleaseWindow), "Open", PatchKind.Postfix, Optional = true)]
        internal static void OpenPostfix(GameCpu cpu)
        {
            if (ModTicks.IsFastForwarding) return;

            string id = PlatformGenerationsFeature.FeatureId;
            if (!ModFeatures.IsActive(id) || !ModHooks.CanRun(id, Site)) return;

            try
            {
                Warn(PlatformGenerationsFeature.Instance, cpu);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(id, Site, e);
            }
        }

        /// <summary>
        /// Shows the warnings for releasing <paramref name="cpu"/> now; returns how many were shown. The body of the postfix, apart so that it can be
        /// asked for in the self-check and so that the patch method stays a guard and a call.
        /// </summary>
        internal static int Warn(PlatformGenerationsFeature feature, GameCpu cpu)
        {
            if (feature == null || !feature.CatalogReady || ReferenceEquals(cpu, null) || !PlatformSettings.Notifications) return 0;
            if (GameAdapter.IsVariant(cpu)) return 0;   // a variant changes neither the socket nor the generation

            string companyKey = GameAdapter.CompanyKeyOf(cpu);
            SocketId socket;
            if (companyKey == null || !GameAdapter.TryGetSocket(cpu, out socket)) return 0;

            GenerationDef gen = feature.Catalog.BySocket(socket);
            GameDate today = GameAdapter.Today();
            if (gen == null || !today.IsSet) return 0;

            ReleaseOutcome outcome = feature.Timeline.Preview(companyKey, socket, gen, today, feature.InstalledBase, feature.Catalog, feature.Balance);

            ViewText text = PlatformViewText.Instance;
            string mainName = string.Empty;
            if (!string.IsNullOrEmpty(outcome.MainSocketKey))
            {
                SocketId main = feature.InstalledBase.SocketOf(outcome.MainSocketKey);
                mainName = text.Socket(main.IsValid ? main : new SocketId { Key = outcome.MainSocketKey });
            }

            List<Notice> notices = PlatformNotices.ReleaseWarnings(outcome, gen, text.Socket(socket), mainName, feature.Balance, text);
            for (int i = 0; i < notices.Count; i++) ModNotify.ShowKey(PlatformGenerationsFeature.FeatureId, notices[i].Key, notices[i].Args);
            return notices.Count;
        }
    }
}
