using System;
using System.Globalization;
using ProcessorTycoon.Save;

namespace PTMod.Core
{
    /// <summary>
    /// The names of saves as the game makes them, for the side file (FR-5 item 1): <c>Y_M_D</c> for an empty name, the name without
    /// <c>.txt</c> for a file. Pure, no game classes.
    /// </summary>
    internal static class SaveNames
    {
        private const string TextExtension = ".txt";

        /// <summary>
        /// The name the game gives the save: <paramref name="saveName"/>, or, when it is empty or blank, <c>Year_Month_Day</c> of
        /// <paramref name="currentTime"/> with no padding (<c>1985_3_1</c>) - the same as <c>SaveSystem.Save</c> (<c>autosave</c> arrives as it is).
        /// </summary>
        internal static string Resolve(string saveName, DateTime currentTime)
        {
            if (!string.IsNullOrWhiteSpace(saveName)) return saveName;

            return currentTime.Year.ToString(CultureInfo.InvariantCulture) + "_"
                   + currentTime.Month.ToString(CultureInfo.InvariantCulture) + "_"
                   + currentTime.Day.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The save name behind a file name of the save window (<c>1985_3_1.txt</c> gives <c>1985_3_1</c>): what <c>SaveSystem.RemoveTxt</c> does, but only
        /// for a name that ends in <c>.txt</c>; any other name is returned as it is. Null stays null.
        /// </summary>
        internal static string FromFileName(string fileName)
        {
            if (fileName == null) return null;
            return fileName.EndsWith(TextExtension, StringComparison.OrdinalIgnoreCase) ? fileName.Substring(0, fileName.Length - TextExtension.Length) : fileName;
        }
    }

    /// <summary>
    /// P12: <c>SaveSystem.Save</c>, postfix. The game has just written <c>&lt;name&gt;.txt</c> (a failed write throws and a postfix does not run),
    /// so the side file is written now, under the same name: the name the player typed, <c>autosave</c>, or <c>Y_M_D</c> of the game date when the
    /// name was blank. A class of its own, so that if another game version changes this method only this patch is off (the core's unit of rollback).
    /// </summary>
    internal static class SaveWritePatches
    {
        private const string Site = "P12/postfix";

        [ModPatch(typeof(SaveSystem), "Save", PatchKind.Postfix, ArgTypes = new[] { typeof(string), typeof(string), typeof(DateTime) })]
        private static void SavePostfix(string saveName, DateTime currentTime)
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                // The postfix may see the name before or after the game filled in the blank one; Resolve gives the same answer for both.
                ModSave.WriteSideFile(SaveNames.Resolve(saveName, currentTime), currentTime);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }

    /// <summary>
    /// P13: <c>SaveHandler.Load</c>, prefix. Tells <c>ModSave</c> which save is being loaded; the side file itself is read later, on
    /// <c>SaveHandler.OnLoaded</c> (the game clears its load events inside <c>Load</c> and the mod subscribes again once the scene is up). Skipped when
    /// the game has no such file, as the game itself then loads nothing.
    /// </summary>
    internal static class SaveLoadPatches
    {
        private const string Site = "P13/prefix";

        [ModPatch(typeof(SaveHandler), "Load", PatchKind.Prefix, ArgTypes = new[] { typeof(string) })]
        private static void LoadPrefix(string fileName)
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                if (SaveSystem.FileExists(fileName)) ModSave.PrepareLoad(SaveNames.FromFileName(fileName));
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }

    /// <summary>P14: <c>SaveSystem.Delete</c>, postfix. The player deleted a save in the load or save window: its side file goes with it.</summary>
    internal static class SaveDeletePatches
    {
        private const string Site = "P14/postfix";

        [ModPatch(typeof(SaveSystem), "Delete", PatchKind.Postfix, ArgTypes = new[] { typeof(string) })]
        private static void DeletePostfix(string fileName)
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                ModSave.DeleteSideFile(fileName);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }
}
