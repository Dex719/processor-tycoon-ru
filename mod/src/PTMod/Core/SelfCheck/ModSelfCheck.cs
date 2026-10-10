using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>The three moments at which the mod checks itself (FR-19, design.md "Самопроверка на старте").</summary>
    public enum SelfCheckStage
    {
        /// <summary>The end of <c>ModBootstrap.Init</c>: every feature has registered and started. Harmony, game version, patches, content, strings, settings, profile, registries.</summary>
        Boot,

        /// <summary>The main menu (scene 0) is ready: its UI templates, the new-game window and the private field the core reads from it.</summary>
        MainMenu,

        /// <summary>The game scene (scene 1) is ready: its UI templates, the singletons, the private members, the saves folder, the credit patterns.</summary>
        GameScene
    }

    public enum CheckSeverity
    {
        /// <summary>Nothing to report. <see cref="ModSelfCheck"/> lists problems only: a stage without items is a stage that passed.</summary>
        Ok,

        /// <summary>The mod works, but something is off (a game template moved, a string is missing in one language, an optional patch is not in the game).</summary>
        Warning,

        /// <summary>A part of the mod does not work (a patch target is missing, a reserved method is taken, a singleton is absent). Counts for the notification.</summary>
        Error
    }

    /// <summary>One finding of the self-check. <see cref="Area"/>, <see cref="Message"/> and <see cref="Hint"/> are in the current language and follow a language switch.</summary>
    public sealed class SelfCheckItem
    {
        public SelfCheckStage Stage;
        public CheckSeverity Severity;

        /// <summary>The part of the mod the finding is about ("Патчи", "Строки"), for grouping.</summary>
        public string Area;

        /// <summary>What is wrong, for the player (the Diagnostics tab).</summary>
        public string Message;

        /// <summary>What to do about it; null when there is nothing to suggest.</summary>
        public string Hint;

        /// <summary>The same finding in English with every detail the log has (names of tables, counts); not translated.</summary>
        public string Detail;

        internal ModSelfCheck.CheckArea AreaCode;
        internal string Key;
        internal string English;
        internal object[] Args;
        internal ModSelfCheck.CheckHint HintCode;

        /// <summary>A hint that comes from the finding itself (the API to use instead of a reserved method) and is asked for again on a language switch; wins over <see cref="HintCode"/>.</summary>
        internal Func<string> HintSource;

        /// <summary>Identity of the finding across stages: a later stage does not report what an earlier one already did (null: always reported).</summary>
        internal string DedupeKey;

        public override string ToString()
        {
            return Stage + " " + Severity + " [" + Area + "] " + Message;
        }
    }

    /// <summary>What one run of a stage came to; <see cref="ModSelfCheck.Stages"/> has one per stage, also for a stage that has not run.</summary>
    public sealed class SelfCheckStageResult
    {
        internal SelfCheckStageResult(SelfCheckStage stage, bool ran, IReadOnlyList<SelfCheckItem> items, long elapsedMs)
        {
            Stage = stage;
            Ran = ran;
            Items = items;
            ElapsedMs = elapsedMs;
            foreach (SelfCheckItem item in items)
            {
                if (item.Severity == CheckSeverity.Error) Errors++;
                else if (item.Severity == CheckSeverity.Warning) Warnings++;
            }
        }

        public SelfCheckStage Stage { get; }

        /// <summary>The stage has run in this session (false: the hub shows "not run yet").</summary>
        public bool Ran { get; }

        public int Errors { get; }

        public int Warnings { get; }

        /// <summary>The stage ran and found nothing.</summary>
        public bool IsOk
        {
            get { return Ran && Errors == 0 && Warnings == 0; }
        }

        /// <summary>How long the checks took, in milliseconds (the budget is <see cref="ModSelfCheck.StageBudgetMs"/>).</summary>
        public long ElapsedMs { get; }

        /// <summary>The findings of the run, errors first; empty for a stage that passed or has not run.</summary>
        public IReadOnlyList<SelfCheckItem> Items { get; }

        /// <summary>The line of the log, in English: <c>self-check Boot: OK</c> or <c>self-check Boot: 2 errors, 1 warnings</c>.</summary>
        public string Summary
        {
            get
            {
                if (!Ran) return "self-check " + Stage + ": not run";
                return IsOk
                    ? "self-check " + Stage + ": OK"
                    : "self-check " + Stage + ": " + Errors.ToString(CultureInfo.InvariantCulture) + " errors, " + Warnings.ToString(CultureInfo.InvariantCulture) + " warnings";
            }
        }
    }

    /// <summary>
    /// The mod's check of itself (FR-19): after every start it looks at what the mod stands on - Harmony, the game's version, the methods the patches
    /// aim at, the private members the core reads, the UI templates, the singletons, the content, the string tables, the settings - and says in the log
    /// and in the hub's Diagnostics tab whether all is well. The point is to learn after a game update, or a bad install, in one glance at
    /// <c>Player.log</c>, not an hour into a game.
    /// <list type="bullet">
    /// <item><b>Boot</b> (<see cref="CoreInitStage.SelfCheckBoot"/>, after the features registered and started): Harmony is loaded and 2.3.x; the game's
    /// <c>GameVersion.Current</c> (read by reflection: a <c>const</c> is copied into the caller by the compiler) is 0.2.3; every patch the registry knows
    /// (<see cref="ModHooks.Report"/>) is applied or has a reason, no feature patches a reserved method, no two features share a transpiler; every target of
    /// P1-P23 exists in the game's assembly, whatever the registry says; the content files, the string tables (both languages complete, the keys carry the prefix
    /// of their feature, FR-25), the settings and the profile are readable; and, once all the features registered, channels with entries or providers but no
    /// declaration (FR-12 item 16) and predicates refused for want of a prefix (FR-11 item 6, AC-19.3).</item>
    /// <item><b>MainMenu</b> (<see cref="PTModHost.SceneReady"/> of scene 0, after <see cref="ModUITemplates"/> looked the templates up): the templates of the menu
    /// (<c>Window.MenuPanel</c>, <c>Button.NewGame</c> and the rest), <c>MenuGameSetup</c> and its private field <c>playerInitialData</c>.</item>
    /// <item><b>GameScene</b> (scene 1): the templates of the game scene, the seven singletons, the private members the core uses (<c>EmailWindow.emailButtonsParent</c>
    /// and the others of the table below), a probe file in <c>Saves/</c> (<c>.ptmod.probe</c>, deleted at once), the patterns of the transpilers P10/P11
    /// (<see cref="ModCredit.IsPerCompanyActive"/> is the outcome).</item>
    /// </list>
    /// Every stage writes one line to the log - <c>self-check Boot: OK (12 ms)</c> or <c>self-check Boot: 2 errors, 1 warnings (12 ms)</c> followed by one line per
    /// finding - and measures itself: more than <see cref="StageBudgetMs"/> is a warning with the time of each group. With errors, the player gets one notification
    /// per session ("Моды → Диагностика"); it waits for a running game like every notification of <see cref="ModNotify"/>.
    /// <para>
    /// The game's classes are reached by name through reflection (never by a type the compiler resolves), so a class that is gone or renamed is a finding, not a
    /// crash. Only problems are listed (<see cref="Last"/>); whether a stage ran, and how long it took, is in <see cref="Stages"/>. A check that itself throws is
    /// logged and becomes a warning; nothing here reaches the game. Main thread only.
    /// </para>
    /// <para>
    /// <b>Check in the game</b> (needs the player; the game is never started from the tools): on a clean game 0.2.3 <c>Player.log</c> has, in this order,
    /// <c>[PTMod] [mod-framework] INFO self-check Boot: OK (N ms)</c> (before the main menu), <c>self-check MainMenu: OK</c> (main menu) and <c>self-check GameScene: OK</c>
    /// (after a new game or a load); N stays well below <see cref="StageBudgetMs"/>. With findings the line reads <c>self-check Boot: 2 errors, 1 warnings (N ms)</c> and a
    /// line per finding follows: <c>self-check Boot [patches]: patch target … not found (feature "…")</c>. The offline harness cannot show what only the running game has:
    /// the UI templates of a scene, the singletons, <c>Resources.FindObjectsOfTypeAll</c> for <c>MenuGameSetup</c>, the probe in the real <c>Saves/</c>, the notification.
    /// </para>
    /// </summary>
    public static class ModSelfCheck
    {
        /// <summary>FR-19 item 6: a stage takes no longer than this; more is logged as a warning with the time of every group.</summary>
        public const int StageBudgetMs = 300;

        /// <summary>The game version the mod was made for (FR-19 item 1). Another version is a warning: the mod goes on, the features whose targets are gone are Unavailable.</summary>
        internal const string SupportedGameVersion = "0.2.3";

        private const string CoreId = ModLog.CoreFeatureId;
        private const string GameAssemblyName = "Assembly-CSharp";
        private const string HarmonyAssemblyName = "0Harmony";
        private const string ProbeFileName = ".ptmod.probe";
        private const string ProbeText = "ptmod probe";
        private const int MaxItemsPerKind = 10;

        private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // The handlers of the credit transpilers P10 and P11 (Optional): ModCredit knows why a pattern did not match, so the GameScene stage speaks for them.
        private const string ThresholdHandler = "BankruptcyThresholdTranspiler.Transpiler";
        private const string DisplayHandler = "CreditDisplayTranspiler.Transpiler";

        private static readonly IReadOnlyList<SelfCheckItem> NoItems = new SelfCheckItem[0];

        /// <summary>Patches that no feature owns and the table of reserved targets leaves out (design.md "Патчи Harmony": P12-P14 are free).</summary>
        private static readonly string[][] FreePatches =
        {
            new[] { "P12", "ProcessorTycoon.Save.SaveSystem", "Save" },
            new[] { "P13", "ProcessorTycoon.Save.SaveHandler", "Load" },
            new[] { "P14", "ProcessorTycoon.Save.SaveSystem", "Delete" },
        };

        /// <summary>The singletons of the game scene the core and the features subscribe to (FR-19 item 2): the type, whose static <c>Instance</c> must be set.</summary>
        private static readonly string[] Singletons =
        {
            "ProcessorTycoon.TimeSystem.DateController",
            "ProcessorTycoon.Save.SaveHandler",
            "ProcessorTycoon.Desktop.Email.EmailWindow",
            "ProcessorTycoon.PopupSystem.PopupManager",
            "ProcessorTycoon.UI.Themes.ThemeManager",
            "ProcessorTycoon.MarketSystem.Market",
            "ProcessorTycoon.Player",
        };

        /// <summary>
        /// The members of the game's classes the core reaches by reflection or through an injected private field - the ones a game update may rename.
        /// Each row names the code that relies on it. Type names are as reflection spells them (a nested type after '+').
        /// </summary>
        private static readonly MemberRow[] Members =
        {
            // MainMenu: P22 injects the private field as ___playerInitialData, ModNewGame.Preview hands it out.
            new MemberRow(SelfCheckStage.MainMenu, "ProcessorTycoon.Menu.MenuGameSetup", MemberKind.Field, "playerInitialData", "ProcessorTycoon.InitialData.PlayerInitialData"),

            // GameScene: ModMail (MailFactory.GameInbox, MailView) builds, redraws and finds letters through these.
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Desktop.Email.EmailWindow", MemberKind.Field, "emailButtonsParent", "UnityEngine.GameObject"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Desktop.Email.EmailWindow", MemberKind.Field, "lastEmail", "ProcessorTycoon.Desktop.Email.Email"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Desktop.Email.EmailMessage", MemberKind.Field, "<Title>k__BackingField", "System.String"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Desktop.Email.EmailMessage", MemberKind.Field, "<Text>k__BackingField", "System.String"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Desktop.Email.Email", MemberKind.Field, "<Date>k__BackingField", "ProcessorTycoon.Desktop.Email.Email+EventDate"),

            // GameScene: P8 and P23 work on the private nested offer of a contract; P3 finds the segment of a population.
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.ContractSystem.Contract", MemberKind.Method, "GetBestOffers", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.ContractSystem.Contract+CpuOffer", MemberKind.Property, "Cpu", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.ContractSystem.Contract+CpuOffer", MemberKind.Property, "Chance", "System.Int32"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.MarketSystem.Market", MemberKind.Field, "populations", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.MarketSystem.Market+Population", MemberKind.Field, "populationData", null),

            // GameScene: ModCredit reads the debt limit after P9; ModUI's tooltips fill the private setters of the tooltip data.
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.Bank.CentralBank", MemberKind.Property, "DebtLimit", "System.Single"),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.TooltipSystem.TooltipData", MemberKind.PropertyWithSetter, "Header", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.TooltipSystem.TooltipData", MemberKind.PropertyWithSetter, "Content", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.TooltipSystem.TooltipData", MemberKind.PropertyWithSetter, "Delay", null),
            new MemberRow(SelfCheckStage.GameScene, "ProcessorTycoon.TooltipSystem.TooltipData", MemberKind.PropertyWithSetter, "PreferedWidth", null),   // sic, the game's spelling
        };

        // One slot per stage; a new run of a stage replaces its slot.
        private static readonly SelfCheckStageResult[] Results = new SelfCheckStageResult[3];

        private static Assembly _gameAssembly;
        private static bool _started;
        private static bool _subscribed;
        private static bool _notified;

        // ---- public API ----

        /// <summary>
        /// Runs the checks of a stage now, replaces the stored result of the stage, writes the summary and the findings to the log and, with errors, shows the
        /// notification (once per session). Returns the findings of this run, errors first; an empty list is a stage that passed. <see cref="SelfCheckStage.MainMenu"/>
        /// and <see cref="SelfCheckStage.GameScene"/> only run while that scene is ready (the answer is empty otherwise: the checks would blame the wrong scene).
        /// The mod itself runs each stage once per scene load; calling it again (a "check again" button) is safe. Never throws.
        /// </summary>
        public static IReadOnlyList<SelfCheckItem> Run(SelfCheckStage stage)
        {
            try
            {
                if (!SceneMatches(stage))
                {
                    ModLog.Debug(CoreId, "self-check " + stage + " is not run: the scene is " + PTModHost.CurrentScene + (PTModHost.IsSceneReady ? "" : " (not ready)"));
                    return NoItems;
                }
                return Execute(stage);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "self-check " + stage + " failed", ex);
                return NoItems;
            }
        }

        /// <summary>
        /// The findings of the latest run of every stage that has run - Boot, then MainMenu, then GameScene, errors first in each - so the Diagnostics tab can list
        /// them by <see cref="SelfCheckItem.Stage"/>. Problems only: a stage that passed has no items here (see <see cref="Stages"/> for "did it run"). A copy.
        /// </summary>
        public static IReadOnlyList<SelfCheckItem> Last
        {
            get
            {
                var all = new List<SelfCheckItem>();
                foreach (SelfCheckStageResult result in Results)
                {
                    if (result != null) all.AddRange(result.Items);
                }
                return all;
            }
        }

        /// <summary>One result per stage in the order of <see cref="SelfCheckStage"/>; <see cref="SelfCheckStageResult.Ran"/> is false for a stage that has not run yet. A copy.</summary>
        public static IReadOnlyList<SelfCheckStageResult> Stages
        {
            get
            {
                var list = new List<SelfCheckStageResult>(Results.Length);
                for (int i = 0; i < Results.Length; i++) list.Add(Results[i] ?? new SelfCheckStageResult((SelfCheckStage)i, false, NoItems, 0));
                return list;
            }
        }

        // ---- the start (ModBootstrap) ----

        /// <summary>SelfCheckBoot stage: every feature has registered and started. Optional: a defect of the check must not stop the mod.</summary>
        [CoreInit(CoreInitStage.SelfCheckBoot, Optional = true)]
        internal static void RunBoot()
        {
            Run(SelfCheckStage.Boot);
        }

        /// <summary>
        /// HostReady stage, late in it: after <see cref="ModUITemplates"/> and the other core services subscribed to <see cref="PTModHost.SceneReady"/>, so the
        /// templates of the scene are looked up when this handler runs. A start inside an already ready scene runs its stage at once.
        /// </summary>
        [CoreInit(CoreInitStage.HostReady, 9000, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            EnsureSubscribed();
            PTModHost.SceneReady += OnSceneReady;

            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        private static void OnSceneReady(int scene)
        {
            try
            {
                if (scene == PTModHost.MainMenuScene) Execute(SelfCheckStage.MainMenu);
                else if (scene == PTModHost.GameScene) Execute(SelfCheckStage.GameScene);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "self-check of scene " + scene + " failed", ex);
            }
        }

        // ---- running a stage ----

        private static bool SceneMatches(SelfCheckStage stage)
        {
            switch (stage)
            {
                case SelfCheckStage.Boot:
                    return true;
                case SelfCheckStage.MainMenu:
                    return PTModHost.IsSceneReady && PTModHost.CurrentScene == PTModHost.MainMenuScene;
                default:
                    return PTModHost.IsSceneReady && PTModHost.CurrentScene == PTModHost.GameScene;
            }
        }

        private static IReadOnlyList<SelfCheckItem> Execute(SelfCheckStage stage)
        {
            EnsureSubscribed();

            var run = new StageRun(stage);
            Stopwatch clock = Stopwatch.StartNew();
            switch (stage)
            {
                case SelfCheckStage.Boot:
                    run.Group(CheckArea.Harmony, "harmony", () => CheckHarmony(run));
                    run.Group(CheckArea.Game, "game version", () => CheckGameVersion(run));
                    run.Group(CheckArea.Patches, "patch registry", () => CheckPatchRegistry(run));
                    run.Group(CheckArea.Patches, "patch targets", () => CheckPatchTargets(run));
                    run.Group(CheckArea.Content, "content", () => CheckContent(run));
                    run.Group(CheckArea.L10n, "string tables", () => CheckL10n(run));
                    run.Group(CheckArea.Settings, "settings", () => CheckSettings(run));
                    run.Group(CheckArea.Profile, "profile", () => CheckProfile(run));
                    run.Group(CheckArea.Content, "registries", () => CheckRegistries(run));
                    break;

                case SelfCheckStage.MainMenu:
                    run.Group(CheckArea.Ui, "templates", () => CheckTemplates(run, PTModHost.MainMenuScene));
                    run.Group(CheckArea.Singletons, "new-game window", () => CheckMenuObjects(run));
                    run.Group(CheckArea.Members, "members", () => CheckMembers(run));
                    run.Group(CheckArea.Content, "registries", () => CheckRegistries(run));
                    break;

                default:
                    run.Group(CheckArea.Ui, "templates", () => CheckTemplates(run, PTModHost.GameScene));
                    run.Group(CheckArea.Singletons, "singletons", () => CheckSingletons(run));
                    run.Group(CheckArea.Members, "members", () => CheckMembers(run));
                    run.Group(CheckArea.Save, "saves folder", () => CheckSavesFolder(run));
                    run.Group(CheckArea.Credit, "credit patterns", () => CheckCredit(run));
                    run.Group(CheckArea.Content, "registries", () => CheckRegistries(run));
                    break;
            }
            clock.Stop();

            SelfCheckStageResult result = Seal(run, clock.ElapsedMilliseconds);
            Results[(int)stage] = result;
            Publish(result, run);
            return result.Items;
        }

        /// <summary>Orders the findings (errors first, detection order inside a severity), renders their texts and closes the run.</summary>
        private static SelfCheckStageResult Seal(StageRun run, long elapsedMs)
        {
            var ordered = new List<SelfCheckItem>(run.Items.Count);
            foreach (CheckSeverity severity in new[] { CheckSeverity.Error, CheckSeverity.Warning, CheckSeverity.Ok })
            {
                foreach (SelfCheckItem item in run.Items)
                {
                    if (item.Severity == severity) ordered.Add(item);
                }
            }
            foreach (SelfCheckItem item in ordered) Render(item);
            return new SelfCheckStageResult(run.Stage, true, ordered, elapsedMs);
        }

        /// <summary>The log lines (summary, one per finding, the timing), the notification.</summary>
        private static void Publish(SelfCheckStageResult result, StageRun run)
        {
            string line = result.Summary + " (" + result.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms)";
            if (result.Errors > 0) ModLog.Error(CoreId, line);
            else if (result.Warnings > 0) ModLog.Warn(CoreId, line);
            else ModLog.Info(CoreId, line);

            foreach (SelfCheckItem item in result.Items)
            {
                string text = "self-check " + result.Stage + " [" + AreaId(item.AreaCode) + "]: " + item.Detail;
                if (item.Severity == CheckSeverity.Error) ModLog.Error(CoreId, text);
                else ModLog.Warn(CoreId, text);
            }

            string timings = run.TimingsText();
            if (result.ElapsedMs > StageBudgetMs)
                ModLog.Warn(CoreId, "self-check " + result.Stage + " took " + result.ElapsedMs + " ms, over the budget of " + StageBudgetMs + " ms: " + timings);
            else
                ModLog.Debug(CoreId, "self-check " + result.Stage + " timings: " + timings);

            if (result.Errors > 0 && !_notified)
            {
                _notified = true;
                ModNotify.ShowKey(CoreId, "core.selfcheck.failed", result.Errors);
            }
        }

        // ---- texts ----

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            ModL10n.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>The player switched the language: the findings of the stages that ran speak the new one.</summary>
        private static void OnLanguageChanged()
        {
            try
            {
                foreach (SelfCheckStageResult result in Results)
                {
                    if (result == null) continue;
                    foreach (SelfCheckItem item in result.Items) Render(item);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "self-check: the texts could not follow the language", ex);
            }
        }

        private static void Render(SelfCheckItem item)
        {
            item.Area = Localize(AreaKey(item.AreaCode), AreaId(item.AreaCode), null);
            item.Message = Localize(item.Key, item.English, item.Args);
            item.Hint = item.HintSource != null ? item.HintSource() : item.HintCode == CheckHint.None ? null : Localize(HintKey(item.HintCode), null, null);
            item.Detail = FormatEnglish(item.English, item.Args);
        }

        /// <summary>The text of the key in the current language; the English format while the tables lack it (or the finding has no key).</summary>
        private static string Localize(string key, string english, object[] args)
        {
            try
            {
                if (key != null && ModL10n.Has(key)) return ModL10n.L(key, args ?? new object[0]);
            }
            catch (Exception)
            {
                // ModL10n does not throw; the English text is the answer either way.
            }
            return english == null ? key : FormatEnglish(english, args);
        }

        private static string FormatEnglish(string format, object[] args)
        {
            if (format == null) return string.Empty;
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        internal enum CheckArea
        {
            Harmony,
            Game,
            Patches,
            Content,
            L10n,
            Settings,
            Profile,
            Ui,
            Singletons,
            Members,
            Save,
            Credit
        }

        internal enum CheckHint
        {
            None,
            Reinstall,
            GameVersion,
            Log
        }

        private static string AreaKey(CheckArea area)
        {
            switch (area)
            {
                case CheckArea.Harmony: return "core.selfcheck.area.harmony";
                case CheckArea.Game: return "core.selfcheck.area.game";
                case CheckArea.Patches: return "core.selfcheck.area.patches";
                case CheckArea.Content: return "core.selfcheck.area.content";
                case CheckArea.L10n: return "core.selfcheck.area.l10n";
                case CheckArea.Settings: return "core.selfcheck.area.settings";
                case CheckArea.Profile: return "core.selfcheck.area.profile";
                case CheckArea.Ui: return "core.selfcheck.area.ui";
                case CheckArea.Singletons: return "core.selfcheck.area.singletons";
                case CheckArea.Members: return "core.selfcheck.area.members";
                case CheckArea.Save: return "core.selfcheck.area.save";
                default: return "core.selfcheck.area.credit";
            }
        }

        /// <summary>The area in the log (<c>[patches]</c>) and the English text while the tables lack the area's name.</summary>
        private static string AreaId(CheckArea area)
        {
            return area.ToString().ToLowerInvariant();
        }

        private static string HintKey(CheckHint hint)
        {
            switch (hint)
            {
                case CheckHint.Reinstall: return "core.selfcheck.hint.reinstall";
                case CheckHint.GameVersion: return "core.selfcheck.hint.gameVersion";
                default: return "core.selfcheck.hint.log";
            }
        }

        // ================================ Boot ================================

        private static void CheckHarmony(StageRun run)
        {
            // The registry made its backend in the Declare stage and knows whether Harmony could be loaded.
            string problem = ModHooks.BackendProblem;
            if (problem != null)
            {
                run.BackendDown = true;
                run.Add(CheckSeverity.Error, CheckArea.Harmony, "core.selfcheck.harmony.missing", "the Harmony library is not loaded: {0}", CheckHint.Reinstall, problem);
                return;
            }

            Assembly harmony = FindAssembly(HarmonyAssemblyName);
            Version version = harmony == null ? null : harmony.GetName().Version;
            if (version == null)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Harmony, "core.selfcheck.harmony.version", "Harmony version {0}, 2.3.x expected", CheckHint.Reinstall, "?");
                return;
            }
            if (version.Major != 2 || version.Minor != 3)
                run.Add(CheckSeverity.Warning, CheckArea.Harmony, "core.selfcheck.harmony.version", "Harmony version {0}, 2.3.x expected", CheckHint.Reinstall, version.ToString());
        }

        private static void CheckGameVersion(StageRun run)
        {
            string version = ReadGameVersion();
            if (version == null)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Game, "core.selfcheck.game.versionUnknown", "could not determine the game version (GameVersion.Current was not found by reflection)", CheckHint.GameVersion);
                return;
            }
            if (!string.Equals(version, SupportedGameVersion, StringComparison.Ordinal))
                run.Add(CheckSeverity.Warning, CheckArea.Game, "core.selfcheck.game.version", "game version {0}, the mod was checked on {1}", CheckHint.GameVersion, version, SupportedGameVersion);
        }

        /// <summary><c>GameVersion.Current</c> of the game's assembly; a <c>const</c> is copied into the caller at compile time, so it is read by reflection (FR-19 item 1). Null when it cannot be told.</summary>
        private static string ReadGameVersion()
        {
            Type type = GameType("ProcessorTycoon.GameVersion");
            FieldInfo field = type == null ? null : type.GetField("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) return null;
            return (field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null)) as string;
        }

        /// <summary>
        /// What the registry knows: every patch declared by the core and the features. A required patch that failed, a reserved target taken by a feature and a
        /// second transpiler on a method are errors (AC-19.2: the missing target is named); an optional patch that is not in the game is a warning. Without
        /// Harmony nothing can be applied - that is one finding (the Harmony check), not one per patch.
        /// </summary>
        private static void CheckPatchRegistry(StageRun run)
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            Dictionary<string, string> labels = PatchLabels();
            int withoutBackend = 0;

            foreach (PatchReport report in reports)
            {
                if (report.FeatureId == CoreId) run.DeclaredByCore.Add(report.Target);

                if (run.BackendDown)
                {
                    if (report.Status != PatchStatus.Applied) withoutBackend++;
                    continue;
                }
                if (report.FeatureId == CoreId && (report.Handler == ThresholdHandler || report.Handler == DisplayHandler)) continue;   // the GameScene stage

                string label = report.FeatureId == CoreId && labels.ContainsKey(report.Target) ? labels[report.Target] + " " + report.Target : report.Target;
                bool problem = report.IsProblem;
                bool optionalGap = !problem && (report.Status == PatchStatus.Failed || report.Status == PatchStatus.Rejected);
                bool latentConflict = !problem && report.Status == PatchStatus.Declared && report.ConflictWith != null;
                if (!problem && !optionalGap && !latentConflict) continue;

                if (problem || optionalGap) run.ReportedTargets.Add(report.Target);

                CheckSeverity severity = problem ? CheckSeverity.Error : CheckSeverity.Warning;
                SelfCheckItem item;
                if (report.Reserved != null)
                {
                    // The hint says what to use instead of the reserved method ("the channel market.rating.cpu reads this formula, use ModModifiers").
                    ReservedTarget reserved = report.Reserved;
                    item = run.Add(severity, CheckArea.Patches, "core.selfcheck.patch.reservedTaken", "the feature \"{0}\" tries to patch the reserved target {1}: {2}", CheckHint.Log,
                        report.FeatureId, label, reserved.EnglishHint);
                    item.HintSource = reserved.Hint;
                }
                else if (report.ConflictWith != null)
                {
                    item = run.Add(severity, CheckArea.Patches, "core.hooks.patchFailed", latentConflict
                            ? "the features \"{1}\" and \"{2}\" both declare a transpiler on {0}; only one of them can run (could not patch {0})"
                            : "the feature \"{1}\": the transpiler on {0} is refused, the feature \"{2}\" has one there already (could not patch {0})", CheckHint.Log,
                        label, report.FeatureId, report.ConflictWith);
                }
                else if (report.Detail != null && report.Detail.StartsWith("target ", StringComparison.Ordinal) && report.Detail.IndexOf(" not found", StringComparison.Ordinal) > 0)
                {
                    int cut = report.Detail.IndexOf(": ", StringComparison.Ordinal);   // "target X not found: no method 'X' in Y or its base types"
                    item = run.Add(severity, CheckArea.Patches, "core.selfcheck.patch.targetMissing", "patch target {0} not found (feature \"{1}\"): {2}", CheckHint.GameVersion,
                        label, report.FeatureId, cut > 0 ? report.Detail.Substring(cut + 2) : report.Detail);
                }
                else
                {
                    item = run.Add(severity, CheckArea.Patches, "core.hooks.patchFailed", "could not patch {0} (feature \"{1}\"{2}): {3}", CheckHint.Log,
                        label, report.FeatureId, report.Optional ? ", optional patch" : "", report.Detail ?? report.Status.ToString().ToLowerInvariant());
                }
                item.DedupeKey = "target:" + report.Target;
            }

            if (withoutBackend > 0)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Patches, null, "{0} declared patch(es) could not be applied because there is no patch backend; every feature that patches the game is unavailable", CheckHint.Reinstall,
                    withoutBackend);
            }
        }

        /// <summary>
        /// FR-19 item 1, "all the targets resolve": every method of the table of reserved targets (P1-P11, P15-P23, F1, F2) and the three free patches (P12-P14)
        /// exists in the game's assembly, independently of the registry - a defect in the registry that lets a core patch go unnoticed is caught here. A target the
        /// registry reported already is not named twice. Also: every core patch of the table is declared by the core (the patch class was found and read).
        /// </summary>
        private static void CheckPatchTargets(StageRun run)
        {
            Assembly game = GameAssembly();
            if (game == null)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Patches, null, "the game's assembly ({0}) is not loaded; the patch targets cannot be checked against it", CheckHint.Log, GameAssemblyName);
                return;
            }

            foreach (TargetRow row in ExpectedTargets())
            {
                string problem = ResolveMethod(game, row.TypeName, row.Method);
                if (problem != null)
                {
                    if (run.ReportedTargets.Contains(row.Target)) continue;

                    // A formula of a feature (F1, F2) matters only when that feature is in the build; while it is switched off the target is a warning, not a break.
                    if (!row.IsCore && ModFeatures.Find(row.Owner) == null) continue;
                    CheckSeverity severity = row.IsCore || ModFeatures.IsActive(row.Owner) ? CheckSeverity.Error : CheckSeverity.Warning;

                    SelfCheckItem item = run.Add(severity, CheckArea.Patches, "core.selfcheck.patch.targetMissing", "patch target {0} not found (feature \"{1}\"): {2}", CheckHint.GameVersion,
                        row.Patch + " " + row.Target, row.Owner, problem);
                    item.DedupeKey = "target:" + row.Target;
                    run.ReportedTargets.Add(row.Target);
                    continue;
                }

                // The target is in the game; the core's patch must be in the registry (applied or with a reason). Without a backend nothing is declared at all.
                if (row.IsCore && !run.BackendDown && !run.DeclaredByCore.Contains(row.Target))
                {
                    run.Add(CheckSeverity.Error, CheckArea.Patches, null, "the core patch {0} on {1} is not declared: its patch class was not found or could not be read", CheckHint.Reinstall,
                        row.Patch, row.Target);
                }
            }
        }

        private static void CheckContent(StageRun run)
        {
            IReadOnlyList<string> content = ModContent.ResourceNames("content/");
            if (!ContainsName(content, "content/" + ModContent.CoreFeatureId + "/" + ModContent.CoreFile))
            {
                run.Add(CheckSeverity.Error, CheckArea.Content, "core.selfcheck.content.invalid", "content {0}: {1}", CheckHint.Reinstall,
                    ModContent.CoreFile, "the built-in file is not embedded in PTMod.dll");
            }

            foreach (ContentReport report in ModContent.AllReports())
            {
                if (report.Errors.Count == 0) continue;

                // The player's override is the player's to fix; a broken built-in file is the mod's.
                CheckSeverity severity = report.OverrideFound ? CheckSeverity.Warning : CheckSeverity.Error;
                string more = report.Errors.Count > 1 ? " (and " + (report.Errors.Count - 1).ToString(CultureInfo.InvariantCulture) + " more)" : "";
                run.Add(severity, CheckArea.Content, "core.selfcheck.content.invalid", "content {0}: {1}", CheckHint.Log,
                    report.FeatureId + "/" + report.File, report.Errors[0] + more);
            }

            foreach (string line in ModIdsAdapter.Problems)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Content, "core.selfcheck.content.invalid", "content {0}: {1}", CheckHint.Log, ModContent.CoreFile, line);
            }
            if (ModIdsAdapter.Features.Count == 0)
            {
                run.Add(CheckSeverity.Error, CheckArea.Content, "core.selfcheck.content.invalid", "content {0}: {1}", CheckHint.Reinstall,
                    ModContent.CoreFile, "the table of identifier prefixes is empty; no feature owns a string key, a predicate, an event id or a mail tag");
            }
        }

        /// <summary>FR-19 item 1 and FR-8 item 6: both languages complete (plural forms too), every key carries the prefix of its feature (FR-25).</summary>
        private static void CheckL10n(StageRun run)
        {
            IReadOnlyList<string> tables = ModContent.ResourceNames("l10n/");
            foreach (string required in new[] { "l10n/ru.json", "l10n/en.json" })
            {
                if (!ContainsName(tables, required))
                    run.Add(CheckSeverity.Error, CheckArea.L10n, null, "the string table {0} is not embedded in PTMod.dll", CheckHint.Reinstall, required);
            }

            L10nMismatchReport report = ModL10n.CompareLanguages();
            if (report == null)
            {
                run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "the string tables of the two languages could not be compared", CheckHint.Log);
            }
            else
            {
                // A is ru, B is en: MissingInA are the keys only en has.
                AddMissingKeys(run, report.MissingInA, report.LanguageB);
                AddMissingKeys(run, report.MissingInB, report.LanguageA);

                int shown = 0;
                foreach (string line in report.IncompletePlurals)
                {
                    if (++shown > MaxItemsPerKind)
                    {
                        run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "... and {0} more plural groups with a missing form", CheckHint.Log, report.IncompletePlurals.Count - MaxItemsPerKind);
                        break;
                    }
                    int cut = line.IndexOf(": ", StringComparison.Ordinal);   // "ru: core.mail.daysLeft.few"
                    string language = cut > 0 ? line.Substring(0, cut) : "?";
                    string form = cut > 0 ? line.Substring(cut + 2) : line;
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, "core.selfcheck.l10n.plural", "the table {0} lacks a plural form: {1}", CheckHint.Log, language, form);
                }
            }

            CheckKeyPrefixes(run);
        }

        private static void AddMissingKeys(StageRun run, IReadOnlyList<string> keys, string languageThatHasThem)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (i == MaxItemsPerKind)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "... and {0} more keys that exist only in {1}", CheckHint.Log, keys.Count - MaxItemsPerKind, languageThatHasThem);
                    return;
                }
                run.Add(CheckSeverity.Warning, CheckArea.L10n, "core.selfcheck.l10n.missing", "the key \"{0}\" exists only in the language {1}", CheckHint.Log, keys[i], languageThatHasThem);
            }
        }

        /// <summary>
        /// A key of <c>l10n/&lt;lang&gt;/&lt;feature&gt;.json</c> carries a prefix of that feature; a key of the shared <c>l10n/&lt;lang&gt;.json</c> (and of the
        /// player's overrides) any prefix of the table - the same rule as the build-time test and <c>install.py --check</c>. One finding per table file.
        /// </summary>
        private static void CheckKeyPrefixes(StageRun run)
        {
            if (ModIdsAdapter.Features.Count == 0) return;   // reported with the content: nothing to compare with

            var featureOfSource = new Dictionary<string, string>(StringComparer.Ordinal);
            var failures = new Dictionary<string, PrefixFailure>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in ModL10n.Keys(language))
                {
                    string source = ModL10n.SourceOf(language, key) ?? "?";
                    string feature;
                    if (!featureOfSource.TryGetValue(source, out feature))
                    {
                        feature = FeatureOfTable(source);
                        featureOfSource[source] = feature;
                    }

                    bool allowed = feature != null
                        ? ModIdsAdapter.IsL10nKeyAllowed(feature, key)
                        : ModIdsAdapter.FeatureOfId(IdKind.L10nKey, key) != null;
                    if (allowed) continue;

                    PrefixFailure failure;
                    if (!failures.TryGetValue(source, out failure))
                    {
                        failure = new PrefixFailure { Source = source, Feature = feature, FirstKey = key };
                        failures[source] = failure;
                        order.Add(source);
                    }
                    failure.Count++;
                }
            }

            int shown = 0;
            foreach (string source in order)
            {
                if (++shown > MaxItemsPerKind)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "... and {0} more string tables with keys outside their prefixes", CheckHint.Log, order.Count - MaxItemsPerKind);
                    break;
                }

                PrefixFailure failure = failures[source];
                IReadOnlyList<string> prefixes = failure.Feature == null ? new string[0] : ModIdsAdapter.PrefixesOf(failure.Feature);
                if (failure.Feature != null && prefixes.Count > 0)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, "core.selfcheck.l10n.prefix", "the key \"{0}\" does not belong to the feature \"{1}\": the prefix \"{2}.\" is required ({3} key(s) in {4})", CheckHint.Log,
                        failure.FirstKey, failure.Feature, prefixes[0], failure.Count, failure.Source);
                }
                else if (failure.Feature != null)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "the table {4} belongs to the feature \"{1}\", which has no row in the prefixes table of core.json ({3} key(s), the first is \"{0}\")", CheckHint.Log,
                        failure.FirstKey, failure.Feature, null, failure.Count, failure.Source);
                }
                else
                {
                    run.Add(CheckSeverity.Warning, CheckArea.L10n, null, "the key \"{0}\" in {4} starts with no prefix of the prefixes table of core.json ({3} key(s) in this table)", CheckHint.Log,
                        failure.FirstKey, null, null, failure.Count, failure.Source);
                }
            }
        }

        /// <summary>The feature a table file belongs to (<c>l10n/ru/event-system.json</c> gives <c>event-system</c>); null for a shared file or an override.</summary>
        private static string FeatureOfTable(string source)
        {
            string path = source.Replace('\\', '/');
            if (path.StartsWith("PTMod/", StringComparison.Ordinal)) path = path.Substring("PTMod/".Length);
            string language, feature;
            return L10nTables.TryParseTablePath(path, out language, out feature) ? feature : null;
        }

        private static void CheckSettings(StageRun run)
        {
            string folder = RealFileSystem.PersistentDataPath;
            if (folder == null)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Settings, null, "Application.persistentDataPath is not available: {0} and {1} are not used, nothing is stored", CheckHint.Log,
                    ModSettings.FileName, ModProfile.FileName);
                return;
            }

            string path = Path.Combine(folder, ModSettings.FileName);
            IFileSystem fs = RealFileSystem.Instance;
            if (!fs.FileExists(path))
            {
                run.Add(CheckSeverity.Warning, CheckArea.Settings, null, "{0} does not exist and could not be created in {1}; the settings are not stored", CheckHint.Log, ModSettings.FileName, folder);
                return;
            }

            JsonNode node;
            JsonError error;
            string text = fs.ReadAllText(path);
            if (!ModJson.TryParse(text, out node, out error))
                run.Add(CheckSeverity.Warning, CheckArea.Settings, null, "{0} is not valid JSON ({1}); the defaults are in use", CheckHint.Log, ModSettings.FileName, error);
            else if (!(node is JsonObject))
                run.Add(CheckSeverity.Warning, CheckArea.Settings, null, "{0} is not a JSON object; the defaults are in use", CheckHint.Log, ModSettings.FileName);
        }

        private static void CheckProfile(StageRun run)
        {
            switch (ModProfile.Status)
            {
                case ProfileStatus.RestoredFromBackup:
                    run.Add(CheckSeverity.Warning, CheckArea.Profile, null, "{0} was damaged and has been restored from its copy", CheckHint.Log, ModProfile.FileName);
                    break;
                case ProfileStatus.Corrupt:
                    run.Add(CheckSeverity.Error, CheckArea.Profile, null, "{0} and its copy are both damaged: an empty profile is in use", CheckHint.Log, ModProfile.FileName);
                    break;
            }
        }

        /// <summary>
        /// Once every feature has registered (and again in the later stages, where a feature may have registered more): channels with entries or providers
        /// and no declaration (FR-12 item 16) and predicates refused for want of the feature's prefix (FR-11 item 6, AC-19.3). A finding of an earlier stage is
        /// not repeated.
        /// </summary>
        private static void CheckRegistries(StageRun run)
        {
            foreach (string channel in ModModifiers.UndeclaredChannels())
            {
                run.AddOnce(CheckSeverity.Warning, CheckArea.Content, "core.selfcheck.channelUndeclared", "the channel \"{0}\" is used but not declared in the registry (core.json)", CheckHint.Log,
                    "channel:" + channel, channel);
            }

            foreach (RejectedPredicate rejected in ModConditions.Rejected)
            {
                string owner = rejected.OwnerFeature ?? CoreId;
                IReadOnlyList<string> prefixes = owner == CoreId ? new string[0] : ModIdsAdapter.PrefixesOf(owner);
                bool prefixProblem = prefixes.Count > 0 && !ModIdsAdapter.IsPredicateNameAllowed(owner, rejected.Type);
                string dedupe = "predicate:" + owner + "/" + rejected.Type;

                if (prefixProblem)
                {
                    string joined = string.Join("./", new List<string>(prefixes).ToArray());   // "scn./chl./ach" for a feature with three prefixes
                    run.AddOnce(CheckSeverity.Error, CheckArea.Content, "core.selfcheck.predicateRejected", "the predicate \"{0}\" was rejected: it lacks the prefix \"{1}.\" ({2})", CheckHint.Log,
                        dedupe, rejected.Type, joined, rejected.Reason);
                }
                else
                {
                    run.AddOnce(CheckSeverity.Error, CheckArea.Content, null, "the predicate \"{0}\" of the feature \"{1}\" was rejected: {2}", CheckHint.Log,
                        dedupe, rejected.Type, owner, rejected.Reason);
                }
            }
        }

        // ============================ MainMenu and GameScene ============================

        /// <summary>The templates of the scene (looked up by <see cref="ModUITemplates"/>): a required one that is missing is an error, one found away from its path or lacking components a warning.</summary>
        private static void CheckTemplates(StageRun run, int scene)
        {
            if (!ModUITemplates.IsReady || ModUITemplates.SceneIndex != scene)
            {
                run.Add(CheckSeverity.Warning, CheckArea.Ui, null, "the UI templates of scene {0} were not looked up; windows and buttons of the mod are unavailable in it", CheckHint.Log, scene);
                return;
            }

            foreach (UiTemplateResult result in ModUITemplates.Results)
            {
                UiTemplate row = result.Template;
                if (!result.Found)
                {
                    if (row.Required)
                        run.Add(CheckSeverity.Error, CheckArea.Ui, "core.selfcheck.ui.templateMissing", "UI template {0} not found (expected {1})", CheckHint.GameVersion, row.Key, row.Path);
                    continue;
                }
                if (result.How != TemplateHow.Path)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.Ui, null, "UI template {0} is not at its path {1}; found by name at {2}: update the template table (tools/dump_ui_templates.py)", CheckHint.GameVersion,
                        row.Key, row.Path, result.ActualPath);
                }
                if (result.MissingComponents.Count > 0)
                {
                    run.Add(CheckSeverity.Warning, CheckArea.Ui, null, "UI template {0} lacks the expected component(s) {1}", CheckHint.GameVersion, row.Key, string.Join(", ", new List<string>(result.MissingComponents).ToArray()));
                }
            }
        }

        /// <summary>The new-game window: <c>MenuGameSetup</c> is in the menu scene (inactive is fine); its private field is checked with the members table.</summary>
        private static void CheckMenuObjects(StageRun run)
        {
            const string type = "ProcessorTycoon.Menu.MenuGameSetup";
            Type setup = GameType(type);
            if (setup == null)
            {
                run.Add(CheckSeverity.Error, CheckArea.Members, "core.selfcheck.member.missing", "the game class has no member {0}", CheckHint.GameVersion, "MenuGameSetup");
                return;
            }
            if (!AnyInScene(setup))
                run.Add(CheckSeverity.Error, CheckArea.Singletons, "core.selfcheck.singleton.missing", "the game object {0} does not exist yet", CheckHint.Log, "MenuGameSetup");
        }

        private static void CheckSingletons(StageRun run)
        {
            foreach (string typeName in Singletons)
            {
                string display = ShortName(typeName) + ".Instance";
                Type type = GameType(typeName);
                PropertyInfo property = type == null ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property == null)
                {
                    run.Add(CheckSeverity.Error, CheckArea.Members, "core.selfcheck.member.missing", "the game class has no member {0}", CheckHint.GameVersion, display);
                    continue;
                }

                // A destroyed Unity object is not C# null; Unity's own comparison tells (the cast makes the operator of UnityEngine.Object apply).
                object value = property.GetValue(null, null);
                bool present = value != null && (!(value is UnityEngine.Object) || (UnityEngine.Object)value != null);
                if (!present) run.Add(CheckSeverity.Error, CheckArea.Singletons, "core.selfcheck.singleton.missing", "the game object {0} does not exist yet", CheckHint.Log, display);
            }
        }

        /// <summary>The rows of <see cref="Members"/> for this stage: the member exists, with the type the core expects. A target the Boot stage named is not named again.</summary>
        private static void CheckMembers(StageRun run)
        {
            Assembly game = GameAssembly();
            if (game == null) return;   // the Boot stage said so

            foreach (MemberRow row in Members)
            {
                if (row.Stage != run.Stage) continue;

                string problem = ResolveMember(game, row);
                if (problem == null) continue;

                string dedupe = row.Kind == MemberKind.Method ? "target:" + ShortName(row.TypeName).Replace('+', '.') + "." + row.Name : null;
                run.AddOnce(CheckSeverity.Error, CheckArea.Members, "core.selfcheck.member.missing", "the game class has no member {0}: {1}", CheckHint.GameVersion,
                    dedupe, row.Display, problem);
            }
        }

        /// <summary>FR-19 item 2: a file can be written to <c>Saves/</c>, where the side files of the saves go. The probe is deleted at once.</summary>
        private static void CheckSavesFolder(StageRun run)
        {
            string root = RealFileSystem.PersistentDataPath;
            if (root == null)
            {
                run.Add(CheckSeverity.Error, CheckArea.Save, "core.selfcheck.save.probeFailed", "cannot write a file to the saves folder: {0}", CheckHint.Log, "Application.persistentDataPath is not available");
                return;
            }

            string folder = Path.Combine(root, "Saves");
            string probe = Path.Combine(folder, ProbeFileName);
            IFileSystem fs = RealFileSystem.Instance;
            string error = null;
            try
            {
                if (!fs.DirectoryExists(folder)) fs.CreateDirectory(folder);   // the game does the same in SaveHandler.Awake
                fs.WriteAllText(probe, ProbeText);
                if (fs.ReadAllText(probe) != ProbeText) error = "the probe file reads back differently";
            }
            catch (Exception ex)
            {
                error = AtomicFile.Describe(ex);
            }
            finally
            {
                try
                {
                    fs.Delete(probe);
                }
                catch (Exception ex)
                {
                    if (error == null) error = "the probe file cannot be deleted: " + AtomicFile.Describe(ex);
                }
            }

            if (error != null)
                run.Add(CheckSeverity.Error, CheckArea.Save, "core.selfcheck.save.probeFailed", "cannot write a file to the saves folder: {0}", CheckHint.Log, folder + ": " + error);
        }

        /// <summary>The patterns of the transpilers P10/P11 (FR-24): a missing one is a warning; the outcome, <see cref="ModCredit.IsPerCompanyActive"/>, goes to the log.</summary>
        private static void CheckCredit(StageRun run)
        {
            if (ModHooks.BackendProblem != null) return;   // the Harmony finding says why

            foreach (string target in ModCredit.MissingPatterns())
            {
                run.Add(CheckSeverity.Warning, CheckArea.Credit, "core.selfcheck.credit.patternMissing", "the code pattern for {0} was not found: per-company credit limits are off", CheckHint.Log, target);
            }
            ModLog.Info(CoreId, "self-check " + run.Stage + ": credit thresholds " + (ModCredit.IsPerCompanyActive ? "per company" : "global") + " (" + ModCredit.DescribeMode() + ")");
        }

        // ================================ the game, by reflection ================================

        /// <summary>The game's assembly, found among the loaded ones once; null in a process that is not the game.</summary>
        private static Assembly GameAssembly()
        {
            Assembly cached = _gameAssembly;
            if (cached != null) return cached;

            cached = FindAssembly(GameAssemblyName);
            _gameAssembly = cached;
            return cached;
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

        private static Type GameType(string fullName)
        {
            Assembly game = GameAssembly();
            return game == null ? null : game.GetType(fullName, false);
        }

        /// <summary>Null when the type has a method of the name (declared on it: the table of reserved targets keys by the declaring type); else why not.</summary>
        private static string ResolveMethod(Assembly game, string typeName, string method)
        {
            Type type = game.GetType(typeName, false);
            if (type == null) return "type " + typeName + " not found";
            foreach (MethodInfo candidate in type.GetMethods(AnyMember))
            {
                if (candidate.Name == method) return null;
            }
            return "method " + method + " not found on " + typeName;
        }

        private static string ResolveMember(Assembly game, MemberRow row)
        {
            Type type = game.GetType(row.TypeName, false);
            if (type == null) return "type " + row.TypeName + " not found";

            Type actual;
            switch (row.Kind)
            {
                case MemberKind.Field:
                    FieldInfo field = type.GetField(row.Name, AnyMember);
                    if (field == null) return "field " + row.Name + " not found";
                    actual = field.FieldType;
                    break;

                case MemberKind.Property:
                case MemberKind.PropertyWithSetter:
                    PropertyInfo property = type.GetProperty(row.Name, AnyMember);
                    if (property == null) return "property " + row.Name + " not found";
                    if (row.Kind == MemberKind.PropertyWithSetter && property.GetSetMethod(true) == null) return "property " + row.Name + " has no setter";
                    actual = property.PropertyType;
                    break;

                default:
                    foreach (MethodInfo candidate in type.GetMethods(AnyMember))
                    {
                        if (candidate.Name == row.Name) return null;
                    }
                    return "method " + row.Name + " not found";
            }

            if (row.ExpectedType != null && !string.Equals(actual.FullName, row.ExpectedType, StringComparison.Ordinal))
                return row.Name + " is a " + actual.FullName + ", the core expects " + row.ExpectedType;
            return null;
        }

        /// <summary>An object of the type in a loaded scene (also an inactive one; prefabs and assets do not count).</summary>
        private static bool AnyInScene(Type type)
        {
            foreach (UnityEngine.Object candidate in UnityEngine.Resources.FindObjectsOfTypeAll(type))
            {
                var component = candidate as UnityEngine.Component;
                if (component != null && component.gameObject.scene.IsValid()) return true;
            }
            return false;
        }

        // ================================ tables ================================

        /// <summary>The target of every patch the core owns or reserves: the reserved table of <see cref="ModHooks"/> (P1-P11, P15-P23, F1, F2) and P12-P14.</summary>
        private static List<TargetRow> ExpectedTargets()
        {
            var rows = new List<TargetRow>();
            foreach (ReservedTarget reserved in ModHooks.ReservedTargetList)
                rows.Add(new TargetRow(reserved.Patch, reserved.TypeName, reserved.Method, reserved.Target, reserved.OwnerFeature, reserved.Patch.StartsWith("P", StringComparison.Ordinal)));

            foreach (string[] free in FreePatches)
                rows.Add(new TargetRow(free[0], free[1], free[2], ShortName(free[1]).Replace('+', '.') + "." + free[2], CoreId, true));
            return rows;
        }

        /// <summary><c>Market.CalculateRatings</c> to <c>P4</c>, for naming a core patch in a finding.</summary>
        private static Dictionary<string, string> PatchLabels()
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TargetRow row in ExpectedTargets())
            {
                if (row.IsCore && !labels.ContainsKey(row.Target)) labels[row.Target] = row.Patch;
            }
            return labels;
        }

        /// <summary>The class name without its namespace; a nested type keeps its '+'.</summary>
        private static string ShortName(string typeName)
        {
            int dot = typeName.LastIndexOf('.');
            return dot < 0 ? typeName : typeName.Substring(dot + 1);
        }

        private static bool ContainsName(IReadOnlyList<string> names, string wanted)
        {
            foreach (string name in names)
            {
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>A finding of an earlier stage with this key: the later stages do not repeat it.</summary>
        private static bool ReportedBefore(SelfCheckStage stage, string dedupeKey)
        {
            for (int i = 0; i < (int)stage; i++)
            {
                SelfCheckStageResult result = Results[i];
                if (result == null) continue;
                foreach (SelfCheckItem item in result.Items)
                {
                    if (item.DedupeKey == dedupeKey) return true;
                }
            }
            return false;
        }

        private enum MemberKind
        {
            Field,
            Property,
            PropertyWithSetter,
            Method
        }

        private sealed class MemberRow
        {
            public MemberRow(SelfCheckStage stage, string typeName, MemberKind kind, string name, string expectedType)
            {
                Stage = stage;
                TypeName = typeName;
                Kind = kind;
                Name = name;
                ExpectedType = expectedType;
            }

            public SelfCheckStage Stage { get; }

            public string TypeName { get; }

            public MemberKind Kind { get; }

            public string Name { get; }

            /// <summary>The full name of the type the core expects the member to have, or null when only its existence matters.</summary>
            public string ExpectedType { get; }

            /// <summary><c>EmailWindow.emailButtonsParent</c>, <c>Contract.CpuOffer.Chance</c>.</summary>
            public string Display
            {
                get { return ShortName(TypeName).Replace('+', '.') + "." + Name; }
            }
        }

        private sealed class TargetRow
        {
            public TargetRow(string patch, string typeName, string method, string target, string owner, bool isCore)
            {
                Patch = patch;
                TypeName = typeName;
                Method = method;
                Target = target;
                Owner = owner;
                IsCore = isCore;
            }

            public string Patch { get; }

            public string TypeName { get; }

            public string Method { get; }

            /// <summary><c>Class.Method</c> as the patch registry names it.</summary>
            public string Target { get; }

            /// <summary>The feature the target belongs to: the core, or the owner of a feature formula.</summary>
            public string Owner { get; }

            /// <summary>The core declares a patch on it (P1-P23); F1 and F2 are the patches of <c>market-shocks</c>.</summary>
            public bool IsCore { get; }
        }

        private sealed class PrefixFailure
        {
            public string Source;
            public string Feature;
            public string FirstKey;
            public int Count;
        }

        // ================================ one run ================================

        /// <summary>The findings of one run of a stage and what the groups of checks share; thrown away after <see cref="Seal"/>.</summary>
        private sealed class StageRun
        {
            private readonly List<string> _timings = new List<string>();

            public StageRun(SelfCheckStage stage)
            {
                Stage = stage;
                Items = new List<SelfCheckItem>();
                ReportedTargets = new HashSet<string>(StringComparer.Ordinal);
                DeclaredByCore = new HashSet<string>(StringComparer.Ordinal);
            }

            public SelfCheckStage Stage { get; }

            public List<SelfCheckItem> Items { get; }

            /// <summary>Targets (<c>Class.Method</c>) the registry check named already, so the table check does not name them again.</summary>
            public HashSet<string> ReportedTargets { get; }

            /// <summary>Targets of the patches the core declared.</summary>
            public HashSet<string> DeclaredByCore { get; }

            /// <summary>There is no patch backend (the Harmony finding stands for every patch).</summary>
            public bool BackendDown { get; set; }

            public SelfCheckItem Add(CheckSeverity severity, CheckArea area, string key, string english, CheckHint hint, params object[] args)
            {
                var item = new SelfCheckItem
                {
                    Stage = Stage,
                    Severity = severity,
                    AreaCode = area,
                    Key = key,
                    English = english,
                    Args = args,
                    HintCode = hint,
                };
                Items.Add(item);
                return item;
            }

            /// <summary><see cref="Add"/> unless an earlier stage reported the same thing (<paramref name="dedupeKey"/>) or this run did; null then.</summary>
            public SelfCheckItem AddOnce(CheckSeverity severity, CheckArea area, string key, string english, CheckHint hint, string dedupeKey, params object[] args)
            {
                if (dedupeKey != null)
                {
                    if (ReportedBefore(Stage, dedupeKey)) return null;
                    foreach (SelfCheckItem existing in Items)
                    {
                        if (existing.DedupeKey == dedupeKey) return null;
                    }
                }

                SelfCheckItem item = Add(severity, area, key, english, hint, args);
                item.DedupeKey = dedupeKey;
                return item;
            }

            /// <summary>Runs one group of checks: a failure inside is logged and becomes a warning, the other groups still run; the time is kept for the log.</summary>
            public void Group(CheckArea area, string name, Action body)
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "self-check " + Stage + ": the check '" + name + "' failed", ex);
                    Add(CheckSeverity.Warning, area, null, "the check '{0}' could not be completed ({1}: {2})", CheckHint.Log, name, ex.GetType().Name, ex.Message);
                }
                finally
                {
                    _timings.Add(name + " " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
                }
            }

            public string TimingsText()
            {
                return string.Join(", ", _timings.ToArray());
            }
        }
    }
}
