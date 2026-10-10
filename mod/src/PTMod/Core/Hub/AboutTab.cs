using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PTMod.Core.Logic.Save;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// The About tab of the hub (FR-17 item 5, AC-5.2): what is installed and where it keeps its files, in three groups of plain lines.
    /// <list type="bullet">
    /// <item><b>Versions.</b> The mod (<see cref="BuildInfo.Version"/>, with "debug" in the debug build), the game (<c>GameVersion.Current</c> read by reflection, as the self-check does: a <c>const</c> is copied into the
    /// mod at build time and would always show the version the mod was built against; a version the mod was not checked on is followed by the self-check's own sentence), Harmony (the version of the
    /// loaded <c>0Harmony</c> assembly, or "not loaded" when the patch backend is down) and the language of the mod's texts.</item>
    /// <item><b>The side file of the save in play.</b> Its status (<see cref="ModSave.Status"/>): "Loaded", "No mod file (old save)" (AC-5.2), "Damaged, defaults are used", "Created by a newer mod version"
    /// (with that version when the file says it), "No save loaded" in the menu and before the first load, "New game" until the first save; the path of the file when it is known; and the hint that copying a save means
    /// copying the <c>.ptmod.json</c> beside it.</item>
    /// <item><b>Files.</b> The settings file (<c>ptmod_settings.json</c>), the profile (<c>ptmod_profile.json</c>) with how it was obtained at start (<see cref="ModProfile.Status"/>: read, new, restored from the
    /// backup, damaged), and the two folders of overrides, <c>&lt;game&gt;/PTMod/content</c> and <c>&lt;game&gt;/PTMod/l10n</c> (the player's files that replace the built-in content and texts).</item>
    /// </list>
    /// The tab joins the hub from its own <c>[CoreInit]</c> method (<see cref="ModHub.RegisterTab"/>). A group that throws is a log line and a "section not loaded" line in its place. Main thread only.
    /// Nothing here throws into the game.
    /// <para>
    /// <b>Check in the game</b> (owner; the game is never started from the tools): "Mods" opens the hub; the third tab is "About" ("О моде"). The first group names the mod version of <c>mod/VERSION</c>, the game
    /// version 0.2.3 (no extra line), Harmony 2.3.x and the language. After a new game the side file says "New game"; after saving as <c>test1</c> and loading it, "Loaded" and the path
    /// <c>...\Saves\test1.ptmod.json</c> (M9); a vanilla save (M10) shows "No mod file (old save)"; a damaged <c>test1.ptmod.json</c> (M11) shows "Damaged"; a file written by a later build of the mod shows "Created by a
    /// newer mod version". The paths match the real files: <c>Player.log</c> and <c>ptmod_settings.json</c> are in the same <c>persistentDataPath</c>. Switch the language: the tab is redrawn.
    /// </para>
    /// </summary>
    internal static class AboutTab
    {
        /// <summary>Caption key of the tab (the hub shows it and sorts by <see cref="TabOrder"/>).</summary>
        internal const string TabKey = "core.hub.tab.about";

        /// <summary>After the Diagnostics tab (10).</summary>
        internal const int TabOrder = 20;

        private const string GameAssemblyName = "Assembly-CSharp";
        private const string HarmonyAssemblyName = "0Harmony";
        private const int MaxPathChars = 400;

        private static readonly KeyValuePair<string, Func<DiagBlock>>[] Blocks =
        {
            new KeyValuePair<string, Func<DiagBlock>>("versions", VersionsBlock),
            new KeyValuePair<string, Func<DiagBlock>>("save", SaveBlock),
            new KeyValuePair<string, Func<DiagBlock>>("files", FilesBlock),
        };

        /// <summary>HostReady stage: the tab joins the registry of the hub.</summary>
        [CoreInit(CoreInitStage.HostReady, 31, Optional = true)]
        internal static void Register()
        {
            ModHub.RegisterTab(TabKey, TabOrder, Build);
        }

        /// <summary>The builder of the tab: fills the emptied body of the hub with the groups.</summary>
        internal static void Build(RectTransform body)
        {
            HubText.DrawAll(body, TabKey, Blocks);
        }

        // ================================ versions ================================

        internal static DiagBlock VersionsBlock()
        {
            string game = ReadGameVersion();
            var lines = new List<string>
            {
                ModL10n.L("core.about.modVersion", ModVersionText()),
                ModL10n.L("core.about.gameVersion", game ?? "?"),
            };

            // A game the mod was not checked on: the sentence of the self-check ("game version X, the mod was checked on 0.2.3").
            if (game != null && !string.Equals(game, ModSelfCheck.SupportedGameVersion, StringComparison.Ordinal))
            {
                lines.Add("  " + HubText.OneLine(ModL10n.L("core.selfcheck.game.version", game, ModSelfCheck.SupportedGameVersion), HubText.MaxLineChars));
            }

            lines.Add(ModL10n.L("core.about.harmonyVersion", HarmonyText()));
            lines.Add(ModL10n.L("core.about.language", ModL10n.Language));
            return new DiagBlock(string.Empty, lines);
        }

        /// <summary>The version of <c>mod/VERSION</c>; the debug build says so (it has <c>framework-sample</c> and the <c>debug.*</c> flags).</summary>
        internal static string ModVersionText()
        {
            return BuildInfo.IsDebug ? BuildInfo.Version + " (debug)" : BuildInfo.Version;
        }

        /// <summary>
        /// <c>ProcessorTycoon.GameVersion.Current</c>, read by reflection from the game's assembly so that the compiler cannot have copied it into the mod
        /// (it is a <c>const</c>); null when the class or the field is not there.
        /// </summary>
        internal static string ReadGameVersion()
        {
            try
            {
                Assembly game = FindAssembly(GameAssemblyName);
                Type type = game == null ? null : game.GetType("ProcessorTycoon.GameVersion", false);
                FieldInfo field = type == null ? null : type.GetField("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null) return null;

                string version = (field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null)) as string;
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch (Exception ex)
            {
                ModLog.Debug(ModLog.CoreFeatureId, "hub: the game version could not be read: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>The version of the loaded Harmony ("2.3.6"), "?" when the assembly is not found, and the self-check's "not loaded" when the patch backend is down.</summary>
        internal static string HarmonyText()
        {
            try
            {
                if (ModHooks.BackendProblem != null) return ModL10n.L("core.selfcheck.harmony.missing");

                Assembly harmony = FindAssembly(HarmonyAssemblyName);
                Version version = harmony == null ? null : harmony.GetName().Version;
                if (version == null) return "?";
                return version.Build >= 0 ? version.ToString(3) : version.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name == name) return assembly;
                }
                catch (Exception)
                {
                    // A dynamic or broken assembly is not the one we look for.
                }
            }
            return null;
        }

        // ================================ the side file ================================

        internal static DiagBlock SaveBlock()
        {
            SideFileStatus status = ModSave.Status;
            string text = SaveStatusText(status);

            // A file written by a later mod says which one: "Created by a newer mod version (0.3.0)".
            SideFileHeader header = ModSave.FileHeader;
            if (status == SideFileStatus.Newer && header != null && !string.IsNullOrEmpty(header.modVersion)) text += " (" + header.modVersion + ")";

            var lines = new List<string> { ModL10n.L("core.about.saveFile", text) };
            string path = ModSave.SideFilePath;
            if (!string.IsNullOrEmpty(path)) lines.Add("  " + HubText.OneLine(path, MaxPathChars));
            lines.Add(ModL10n.L("core.about.saveHint"));
            return new DiagBlock(string.Empty, lines);
        }

        /// <summary>The status of the side file as a short phrase (core.save.status.*): the four of AC-5.2 and FR-17 item 5, the menu and a new game.</summary>
        internal static string SaveStatusText(SideFileStatus status)
        {
            switch (status)
            {
                case SideFileStatus.Loaded: return ModL10n.L("core.save.status.loaded");
                case SideFileStatus.Missing: return ModL10n.L("core.save.status.missing");
                case SideFileStatus.Corrupt: return ModL10n.L("core.save.status.corrupt");
                case SideFileStatus.Newer: return ModL10n.L("core.save.status.newer");
                case SideFileStatus.NewGame: return ModL10n.L("core.save.status.newGame");
                default: return ModL10n.L("core.save.status.none");
            }
        }

        // ================================ files ================================

        internal static DiagBlock FilesBlock()
        {
            string data = RealFileSystem.PersistentDataPath;
            var lines = new List<string>
            {
                ModL10n.L("core.about.settingsFile", FileIn(data, ModSettings.FileName)),
                ModL10n.L("core.about.profileFile", FileIn(data, ModProfile.FileName) + " (" + ProfileStatusText(ModProfile.Status) + ")"),
                ModL10n.L("core.about.overrides", ModContent.OverrideRoot ?? "?"),
                ModL10n.L("core.about.overrides", ModContent.L10nOverrideRoot ?? "?"),
            };
            for (int i = 0; i < lines.Count; i++) lines[i] = HubText.OneLine(lines[i], MaxPathChars);
            return new DiagBlock(string.Empty, lines);
        }

        /// <summary>The path of a file in a folder; "?" when the folder is not known (Unity could not say).</summary>
        internal static string FileIn(string folder, string fileName)
        {
            return string.IsNullOrEmpty(folder) ? "?" : Path.Combine(folder, fileName);
        }

        /// <summary>How the profile was obtained at start as a short phrase (core.about.profile.*).</summary>
        internal static string ProfileStatusText(ProfileStatus status)
        {
            switch (status)
            {
                case ProfileStatus.Loaded: return ModL10n.L("core.about.profile.loaded");
                case ProfileStatus.RestoredFromBackup: return ModL10n.L("core.about.profile.restored");
                case ProfileStatus.Corrupt: return ModL10n.L("core.about.profile.corrupt");
                default: return ModL10n.L("core.about.profile.new");
            }
        }
    }
}
