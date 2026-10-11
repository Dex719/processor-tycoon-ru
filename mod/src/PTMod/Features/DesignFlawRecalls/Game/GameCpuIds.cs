using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The CPUs that exist in the game right now, as <c>SaveID</c>s: what <see cref="FlawSaveSection"/> enters as "already rolled" when it starts
    /// from nothing (FR-16 item 3). The thin game side of the section; the rule is the section's.
    /// </summary>
    internal static class GameCpuIds
    {
        /// <summary>
        /// The <c>SaveID</c> of every CPU of every company (retired ones too). Empty outside the game scene (the main menu, between scenes), where
        /// the game's objects do not exist. It scans the owners of CPUs itself instead of reading the cache of <c>CpuDataProvider</c>, which a save that
        /// is just being loaded may not have refreshed yet. Never throws.
        /// </summary>
        public static IEnumerable<int> All()
        {
            var ids = new List<int>();
            try
            {
                if (PTModHost.CurrentScene != PTModHost.GameScene) return ids;

                List<GameCpu> cpus = GameDataFinder.FindAllCpus();
                if (cpus == null) return ids;
                foreach (GameCpu cpu in cpus)
                {
                    if (cpu != null) ids.Add(cpu.SaveID);
                }
            }
            catch (Exception e)
            {
                ModLog.Debug(DesignFlawRecallsApi.FeatureId, "save: the CPUs of the game could not be listed (" + e.GetType().Name + ": " + e.Message + ")");
                ids.Clear();
            }
            return ids;
        }
    }
}
