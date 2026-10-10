using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Log;
using PTMod.Core.Logic.Modifiers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Core
{
    /// <summary>One block of a hub tab: a heading (may be empty) and its lines of plain text. Made by the tabs from the state of the mod, drawn by <see cref="HubText"/>.</summary>
    internal sealed class DiagBlock
    {
        internal DiagBlock(string title, List<string> lines)
        {
            Title = title ?? string.Empty;
            Lines = lines ?? new List<string>();
        }

        /// <summary>The heading in the current language; empty for a block without one.</summary>
        internal string Title { get; }

        /// <summary>One entry per line; an entry is a single line (no line breaks inside), a leading run of spaces is the indent.</summary>
        internal List<string> Lines { get; }
    }

    /// <summary>
    /// The text helpers the Diagnostics and About tabs share. The first half is plain functions of strings and numbers (no Unity, no game), so they can be tried offline; the second half
    /// draws a <see cref="DiagBlock"/> with the widgets of <see cref="ModUI"/>.
    /// <list type="bullet">
    /// <item><b>One text per chunk.</b> A list of 50 problems or 45 patches is not one widget per line: the lines are joined into texts of at most <see cref="MaxLinesPerText"/> lines and
    /// <see cref="MaxCharsPerText"/> characters, so a text never gets near the vertex limit of one TextMeshPro mesh (65 535 vertices, about 16 000 glyphs) and the layout has a dozen
    /// objects to measure instead of a hundred.</item>
    /// <item><b>Plain text.</b> Every text has <c>richText</c> off: messages of the log and names from the game may hold "&lt;", which TextMeshPro would take for a tag.</item>
    /// <item><b>Only characters the game's font surely has</b> are used by the tabs themselves: letters, digits, " | ", "-", brackets, the quotes of the strings; no arrows, no ellipsis sign, no symbols.</item>
    /// </list>
    /// </summary>
    internal static class HubText
    {
        /// <summary>Lines per drawn text.</summary>
        internal const int MaxLinesPerText = 16;

        /// <summary>Characters per drawn text (a line is kept whole, so one text can be longer by at most <see cref="MaxLineChars"/>).</summary>
        internal const int MaxCharsPerText = 3000;

        /// <summary>Longest line a tab writes (a log message or a patch note is cut to it).</summary>
        internal const int MaxLineChars = 300;

        private const float SectionGap = 10f;
        private const string Cut = "...";

        // ---- plain text (no Unity) ----

        /// <summary>
        /// The text as one line of at most <paramref name="maxChars"/> characters: line breaks, tabs and other control characters become single spaces, runs of spaces
        /// shrink to one, the ends are trimmed, and what does not fit is cut and ends with "...". A cut never splits a surrogate pair. Null gives an empty string.
        /// </summary>
        internal static string OneLine(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return string.Empty;

            var sb = new StringBuilder(Math.Min(text.Length, maxChars + Cut.Length));
            bool space = true;   // true at the start: leading blanks are dropped
            foreach (char c in text)
            {
                bool blank = c == ' ' || char.IsControl(c);
                if (blank)
                {
                    if (!space) sb.Append(' ');
                    space = true;
                    continue;
                }
                sb.Append(c);
                space = false;
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            if (sb.Length <= maxChars) return sb.ToString();

            int keep = Math.Max(0, maxChars - Cut.Length);
            if (keep > 0 && char.IsHighSurrogate(sb[keep - 1])) keep--;
            sb.Length = keep;
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.Append(Cut).ToString();
        }

        /// <summary>
        /// Joins lines into texts: a new text starts when adding a line would pass <paramref name="maxLines"/> lines or <paramref name="maxChars"/> characters (a line is never split;
        /// a text always has at least one line). Null entries are empty lines. No lines give no texts.
        /// </summary>
        internal static List<string> Chunk(IReadOnlyList<string> lines, int maxLines, int maxChars)
        {
            var chunks = new List<string>();
            if (lines == null || lines.Count == 0) return chunks;
            if (maxLines < 1) maxLines = 1;

            var current = new StringBuilder();
            int inCurrent = 0;
            foreach (string raw in lines)
            {
                string line = raw ?? string.Empty;
                if (inCurrent > 0 && (inCurrent >= maxLines || current.Length + 1 + line.Length > maxChars))
                {
                    chunks.Add(current.ToString());
                    current.Length = 0;
                    inCurrent = 0;
                }
                if (inCurrent > 0) current.Append('\n');
                current.Append(line);
                inCurrent++;
            }
            if (inCurrent > 0) chunks.Add(current.ToString());
            return chunks;
        }

        /// <summary>A time in seconds as "m:ss" or, from one hour on, "h:mm:ss" (the clock of the log entries: real time since the game started). A negative or non-finite number is 0:00.</summary>
        internal static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) seconds = 0d;

            long total = (long)Math.Floor(seconds);
            long hours = total / 3600;
            long minutes = total % 3600 / 60;
            long secs = total % 60;
            return hours > 0
                ? hours.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + secs.ToString("00", CultureInfo.InvariantCulture)
                : minutes.ToString(CultureInfo.InvariantCulture) + ":" + secs.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>A number in the decimal mark of the current language: a comma in Russian, a point otherwise. No digit grouping. A non-finite number is "?".</summary>
        internal static string Num(double value, string format)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "?";

            string text = value.ToString(format, CultureInfo.InvariantCulture);
            return UsesComma() ? text.Replace('.', ',') : text;
        }

        /// <summary>Milliseconds without the unit: three decimals below 10 ("0.034", "1.25"), one from 10 on; trailing zeros are dropped.</summary>
        internal static string Ms(double ms)
        {
            return Num(ms, ms < 10d ? "0.###" : "0.#");
        }

        private static bool UsesComma()
        {
            try
            {
                return string.Equals(ModL10n.Language, "ru", StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;   // the number is shown with a point rather than not at all
            }
        }

        /// <summary>The level of a log entry or a finding as a word in the current language (core.diag.severity.*); a level that is no problem gets the log's own upper-case name.</summary>
        internal static string LevelWord(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return ModL10n.L("core.diag.severity.error");
                case LogLevel.Warn: return ModL10n.L("core.diag.severity.warning");
                default: return LogFormatter.LevelName(level);
            }
        }

        // ---- drawing (Unity) ----

        /// <summary>
        /// Draws a block into the body of the hub: a vertical list with the heading in bold and the lines below it in smaller type, with a gap after. A block without lines draws only its
        /// heading. Never throws; a widget that cannot be made is a log line from <see cref="ModUI"/> and the rest is drawn.
        /// </summary>
        internal static void Draw(RectTransform body, DiagBlock block)
        {
            if (body == null || block == null) return;

            RectTransform box = ModUI.VerticalList(body, 2f) ?? body;
            if (box != body)
            {
                VerticalLayoutGroup layout = box.GetComponent<VerticalLayoutGroup>();
                if (layout != null) layout.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(SectionGap));
            }

            if (block.Title.Length > 0)
            {
                TMP_Text title = ModUI.Text(box, block.Title, true);
                if (title != null) title.richText = false;
            }

            foreach (string chunk in Chunk(block.Lines, MaxLinesPerText, MaxCharsPerText))
            {
                TMP_Text text = ModUI.Text(box, chunk);
                if (text == null) continue;

                text.richText = false;
                text.fontSize = Mathf.Max(11f, text.fontSize - 2f);
            }
        }

        /// <summary>
        /// Draws the blocks of a tab one after another. The maker of a block runs under its own try/catch: a block that fails is a log line and the line "section not loaded"
        /// ("tab caption/block name") in its place, the other blocks are drawn.
        /// </summary>
        internal static void DrawAll(RectTransform body, string tabKey, IReadOnlyList<KeyValuePair<string, Func<DiagBlock>>> makers)
        {
            foreach (KeyValuePair<string, Func<DiagBlock>> maker in makers)
            {
                try
                {
                    Draw(body, maker.Value());
                }
                catch (Exception ex)
                {
                    ModLog.Error(ModLog.CoreFeatureId, "hub: the block '" + maker.Key + "' of the tab " + tabKey + " failed", ex);
                    ModUI.Text(body, ModL10n.L("core.newgame.sectionError", ModL10n.L(tabKey) + "/" + maker.Key));
                }
            }
        }
    }

    /// <summary>
    /// The Diagnostics tab of the hub (FR-17 item 4, FR-19 item 4, AC-19.2, AC-19.3): everything the mod knows about its own health in one scrolling list, newest facts at the time the tab
    /// is shown (the tab is rebuilt every time it is opened, so it reads the state of that moment). Seven blocks, in this order:
    /// <list type="bullet">
    /// <item><b>Self-check</b> (<see cref="ModSelfCheck.Stages"/>): per stage (mod start, main menu, game scene) "all good" or the numbers of errors and warnings, how long the stage took
    /// (the budget is 300 ms), then each finding with its severity, area, text and what to do; a stage that has not run yet says so. A hub button that fell back to the pause menu
    /// (<see cref="HubEntryButton.UsesFallback"/>) is a warning at the top (FR-17 item 7).</item>
    /// <item><b>Patches</b> (<see cref="ModHooks.Report"/>): one line per declared patch - target, kind, feature, status, reserve. The patches that failed or were rejected come first (with the reason
    /// in English, as the log has it), then the others in the order of declaration. "Reserve" is the row of the patch table that holds the target ("P4 ModModifiers", "P17 ModNewGame"); for a
    /// patch that was rejected because of it, the hint at the API to use instead.</item>
    /// <item><b>Errors</b> (<see cref="ModLog.RecentProblems"/>): the last 50 warnings and errors, newest first, with the time since the game started, the feature and the text (an exception
    /// follows the text; a repeat summary says how often). Below them the core units the circuit breaker switched off, if any.</item>
    /// <item><b>Time</b> (<see cref="ModTicks.StatsOf"/>, <see cref="ModTicks.StatsOfDay"/>): the whole game day first (the mean per day over the last 30 days: the figure of the 0,5 ms budget; the
    /// peak), then each phase in which a handler ran (mean per dispatch, peak), in ms; and a warning line when the day passes <c>performance.warnMsPerDay</c>.</item>
    /// <item><b>Ticks</b>: whether the batch run of a month is going on and how long the last one took (<see cref="ModTicks.IsBatchRun"/>, <see cref="ModTicks.LastBatchDays"/>; the hub is modal, so the first is
    /// always "no" here, the second is what scenario M20 looks at).</item>
    /// <item><b>Channels</b> (<see cref="ModModifiers.Describe"/>): the registry of <c>ModModifiers</c> - one line per declared channel and per undeclared name in use, with the number of entries and providers,
    /// the balance range for the channels that have one (the "capped" ones) and "not declared" for a name nobody declared; then the mode of the credit threshold (<see cref="ModCredit"/>).</item>
    /// <item><b>Mail</b>: letters waiting for delivery, how many of them are <c>Critical</c>, letters delivered today, and why delivery would wait now, if it would.</item>
    /// </list>
    /// The tab joins the hub from its own <c>[CoreInit]</c> method (<see cref="ModHub.RegisterTab"/>), so <c>ModHub.cs</c> does not know it. A block that throws is a log line and a
    /// "section not loaded" line in its place. Main thread only. Nothing here throws into the game.
    /// <para>
    /// <b>Check in the game</b> (owner; the game is never started from the tools): "Mods" opens the hub; the second tab is "Diagnostics" ("Диагностика"). Self-check shows three stages, each "all good"
    /// with a time well below 300 ms on a clean game 0.2.3; with <c>debug.brokenPatch</c> the missing target is named (AC-19.2), with <c>debug.badIds</c> the undeclared channel and the rejected predicate
    /// are listed (AC-19.3). Patches list the core's patches (P1-P23) as "applied", each with its reserve. Errors lists what <c>Player.log</c> has as WARN or ERROR (<c>"debug": {"throwOnTick": true}</c> adds a
    /// line per failing handler). Time fills in after some game days (M15: the day mean stays below 0,5 ms, the peak below 2 ms). Channels shows the 27 channels of the core (M18: with
    /// <c>debug.stress</c> the two capped channels show their balance range [0,5 - 2,0]). Switch the language: the whole tab is redrawn.
    /// </para>
    /// </summary>
    internal static class DiagnosticsTab
    {
        private const string CoreId = ModLog.CoreFeatureId;

        /// <summary>Caption key of the tab (the hub shows it and sorts by <see cref="TabOrder"/>).</summary>
        internal const string TabKey = "core.hub.tab.diagnostics";

        /// <summary>After the Features tab (0) and before the About tab (20).</summary>
        internal const int TabOrder = 10;

        /// <summary>How many problems the block lists (the buffer of <see cref="ModLog"/> holds as many by default, <c>log.recentProblems</c>).</summary>
        internal const int MaxProblems = 50;

        private const int MaxNoteChars = 140;
        private const double DefaultWarnMsPerDay = 2.0;

        private static readonly KeyValuePair<string, Func<DiagBlock>>[] Blocks =
        {
            new KeyValuePair<string, Func<DiagBlock>>("selfcheck", SelfCheckBlock),
            new KeyValuePair<string, Func<DiagBlock>>("patches", PatchesBlock),
            new KeyValuePair<string, Func<DiagBlock>>("errors", ErrorsBlock),
            new KeyValuePair<string, Func<DiagBlock>>("time", TimeBlock),
            new KeyValuePair<string, Func<DiagBlock>>("ticks", TicksBlock),
            new KeyValuePair<string, Func<DiagBlock>>("channels", ChannelsBlock),
            new KeyValuePair<string, Func<DiagBlock>>("mail", MailBlock),
        };

        /// <summary>HostReady stage: the tab joins the registry of the hub (the Features tab is built into the hub, this one is not).</summary>
        [CoreInit(CoreInitStage.HostReady, 30, Optional = true)]
        internal static void Register()
        {
            ModHub.RegisterTab(TabKey, TabOrder, Build);
        }

        /// <summary>The builder of the tab: fills the emptied body of the hub with the blocks.</summary>
        internal static void Build(RectTransform body)
        {
            HubText.DrawAll(body, TabKey, Blocks);
        }

        // ================================ self-check ================================

        internal static DiagBlock SelfCheckBlock()
        {
            var lines = new List<string>();
            if (HubEntryButton.UsesFallback) lines.Add(HubText.LevelWord(LogLevel.Warn) + ": " + HubText.OneLine(ModL10n.L("core.diag.buttonFallback"), HubText.MaxLineChars));

            foreach (SelfCheckStageResult stage in ModSelfCheck.Stages) AddStage(lines, stage);
            return new DiagBlock(ModL10n.L("core.diag.selfcheck.title"), lines);
        }

        private static void AddStage(List<string> lines, SelfCheckStageResult stage)
        {
            string name = StageName(stage.Stage);
            if (!stage.Ran)
            {
                lines.Add(name + ": " + ModL10n.L("core.diag.selfcheck.notRun"));
                return;
            }

            string took = " (" + ModL10n.L("core.diag.ms", HubText.Ms(stage.ElapsedMs)) + ")";
            lines.Add(stage.IsOk
                ? ModL10n.L("core.diag.selfcheck.ok", name) + took
                : ModL10n.L("core.diag.selfcheck.summary", name, stage.Errors, stage.Warnings) + took);

            foreach (SelfCheckItem item in stage.Items)
            {
                if (item == null) continue;

                string head = SeverityWord(item.Severity) + " [" + (item.Area ?? string.Empty) + "] " + (item.Message ?? string.Empty);
                lines.Add("  " + HubText.OneLine(head, HubText.MaxLineChars));
                if (!string.IsNullOrEmpty(item.Hint)) lines.Add("      " + HubText.OneLine(ModL10n.L("core.diag.selfcheck.hint", item.Hint), HubText.MaxLineChars));
            }
        }

        private static string StageName(SelfCheckStage stage)
        {
            switch (stage)
            {
                case SelfCheckStage.Boot: return ModL10n.L("core.diag.selfcheck.stage.boot");
                case SelfCheckStage.MainMenu: return ModL10n.L("core.diag.selfcheck.stage.mainMenu");
                default: return ModL10n.L("core.diag.selfcheck.stage.gameScene");
            }
        }

        private static string SeverityWord(CheckSeverity severity)
        {
            switch (severity)
            {
                case CheckSeverity.Error: return HubText.LevelWord(LogLevel.Error);
                case CheckSeverity.Warning: return HubText.LevelWord(LogLevel.Warn);
                default: return ModL10n.L("core.common.ok");
            }
        }

        // ================================ patches ================================

        internal static DiagBlock PatchesBlock()
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            Dictionary<string, ReservedTarget> reserved = ReservedByTarget();

            var lines = new List<string>(reports.Count + 1);
            if (reports.Count > 0) lines.Add(PatchHeader());

            // What failed or was turned down is what the reader looks for: those lines come first, the rest keep the order of declaration.
            foreach (PatchReport report in reports) if (IsTroubled(report)) lines.Add(PatchLine(report, reserved));
            foreach (PatchReport report in reports) if (!IsTroubled(report)) lines.Add(PatchLine(report, reserved));

            string title = ModL10n.L("core.diag.patches.title") + " (" + reports.Count.ToString(CultureInfo.InvariantCulture) + ")";
            return new DiagBlock(title, lines);
        }

        private static bool IsTroubled(PatchReport report)
        {
            return report.Status == PatchStatus.Failed || report.Status == PatchStatus.Rejected;
        }

        private static string PatchHeader()
        {
            return FormatPatch(ModL10n.L("core.diag.patches.target"), ModL10n.L("core.diag.patches.kind"), ModL10n.L("core.diag.patches.feature"),
                ModL10n.L("core.diag.patches.status"), ModL10n.L("core.diag.patches.reserved"));
        }

        /// <summary>The reserved methods by <c>Class.Method</c> as the patch report spells its target (<see cref="ReservedTarget.Target"/>); the first row of a method wins.</summary>
        private static Dictionary<string, ReservedTarget> ReservedByTarget()
        {
            var map = new Dictionary<string, ReservedTarget>(StringComparer.Ordinal);
            foreach (ReservedTarget row in ModHooks.ReservedTargetList)
            {
                if (!map.ContainsKey(row.Target)) map[row.Target] = row;
            }
            return map;
        }

        internal static string PatchLine(PatchReport report, IDictionary<string, ReservedTarget> reserved)
        {
            string status = PatchStatusText(report.Status);
            if (!string.IsNullOrEmpty(report.Detail) && IsTroubled(report)) status += " (" + HubText.OneLine(report.Detail, MaxNoteChars) + ")";

            string reserve = "-";
            ReservedTarget row;
            if (report.Reserved != null) reserve = report.Reserved.Patch + " " + HubText.OneLine(report.Reserved.Hint(), MaxNoteChars);
            else if (reserved != null && report.Target != null && reserved.TryGetValue(report.Target, out row)) reserve = row.Patch + " " + row.Holder;

            return FormatPatch(report.Target, report.Kind.ToString(), report.FeatureId, status, reserve);
        }

        /// <summary>A patch line: the five columns joined with " | " (no cell may hold a line break).</summary>
        internal static string FormatPatch(string target, string kind, string feature, string status, string reserve)
        {
            return HubText.OneLine((target ?? "?") + " | " + (kind ?? "?") + " | " + (feature ?? "?") + " | " + (status ?? "?") + " | " + (reserve ?? "-"), HubText.MaxLineChars);
        }

        private static string PatchStatusText(PatchStatus status)
        {
            switch (status)
            {
                case PatchStatus.Applied: return ModL10n.L("core.diag.patches.status.applied");
                case PatchStatus.Failed: return ModL10n.L("core.diag.patches.status.failed");
                case PatchStatus.Rejected: return ModL10n.L("core.diag.patches.status.rejected");
                case PatchStatus.Removed: return ModL10n.L("core.diag.patches.status.removed");
                default: return ModL10n.L("core.diag.patches.status.declared");
            }
        }

        // ================================ errors ================================

        internal static DiagBlock ErrorsBlock()
        {
            IReadOnlyList<LogEntry> problems = ModLog.RecentProblems;   // oldest first
            var lines = new List<string>();
            int shown = 0;
            for (int i = problems.Count - 1; i >= 0 && shown < MaxProblems; i--, shown++) lines.Add(ProblemLine(problems[i]));
            if (shown == 0) lines.Add(ModL10n.L("core.diag.errors.empty"));

            IReadOnlyList<string> tripped = ModHooks.TrippedCoreUnits();
            if (tripped.Count > 0) lines.Add(ModL10n.L("core.diag.tripped", string.Join(", ", new List<string>(tripped).ToArray())));

            // "Errors (last 0)" reads oddly: an empty list names the size of the list instead.
            return new DiagBlock(ModL10n.L("core.diag.errors.title", shown > 0 ? shown : MaxProblems), lines);
        }

        internal static string ProblemLine(LogEntry entry)
        {
            string text = entry.Message;
            if (entry.IsSummary) text += " (" + LogFormatter.RepeatedPhrase(entry.SuppressedCount) + ")";
            else if (!string.IsNullOrEmpty(entry.Details)) text += ": " + entry.Details;

            return FormatProblem(HubText.Clock(entry.TimeSeconds), HubText.LevelWord(entry.Level), entry.FeatureId, text);
        }

        /// <summary><c>[3:07] Warning feature-id: text</c> on one line, the text cut to fit.</summary>
        internal static string FormatProblem(string clock, string levelWord, string featureId, string text)
        {
            return HubText.OneLine("[" + clock + "] " + levelWord + " " + featureId + ": " + text, HubText.MaxLineChars);
        }

        // ================================ time and ticks ================================

        internal static DiagBlock TimeBlock()
        {
            var lines = new List<string>();

            TickStats day = ModTicks.StatsOfDay();
            double perDay = day.AveragePerDayMs;
            if (day.Samples > 0)
            {
                lines.Add(ModL10n.L("core.diag.timing.row", ModL10n.L("core.diag.timing.day"), HubText.Ms(perDay), HubText.Ms(day.MaxMs)));
                double warn = WarnMsPerDay();
                if (perDay > warn) lines.Add(ModL10n.L("core.diag.timing.slow", HubText.Ms(perDay), HubText.Ms(warn)));
            }

            foreach (TickPhase phase in Enum.GetValues(typeof(TickPhase)))
            {
                TickStats stats = ModTicks.StatsOf(phase);
                if (stats.Samples == 0) continue;
                lines.Add("  " + ModL10n.L("core.diag.timing.row", phase.ToString(), HubText.Ms(stats.AverageMs), HubText.Ms(stats.MaxMs)));
            }

            if (lines.Count == 0) lines.Add(ModL10n.L("core.diag.timing.empty"));
            return new DiagBlock(ModL10n.L("core.diag.timing.title"), lines);
        }

        private static double WarnMsPerDay()
        {
            double value = ModContent.GetCoreNumber("performance", "warnMsPerDay", DefaultWarnMsPerDay);
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value) ? value : DefaultWarnMsPerDay;
        }

        internal static DiagBlock TicksBlock()
        {
            var lines = new List<string>
            {
                ModL10n.L("core.diag.ticks.batch", ModL10n.L(ModTicks.IsBatchRun ? "core.common.yes" : "core.common.no")),
            };
            if (ModTicks.LastBatchDays > 0 || ModTicks.LastBatchMs > 0d)
            {
                lines.Add(ModL10n.L("core.diag.ticks.lastBatch", ModTicks.LastBatchDays, ModL10n.L("core.diag.ms", HubText.Ms(ModTicks.LastBatchMs))));
            }
            return new DiagBlock(ModL10n.L("core.diag.ticks.title"), lines);
        }

        // ================================ channels ================================

        internal static DiagBlock ChannelsBlock()
        {
            var lines = new List<string>();
            foreach (ChannelInfo info in ModModifiers.Describe()) lines.Add(ChannelLine(info));

            AddCreditMode(lines);
            return new DiagBlock(ModL10n.L("core.diag.channels.title"), lines);
        }

        internal static string ChannelLine(ChannelInfo info)
        {
            string row = ModL10n.L("core.diag.channels.row", info.Name, info.EntryCount, info.ProviderCount);
            if (!info.IsDeclared) return HubText.OneLine(row + " | " + ModL10n.L("core.diag.channels.undeclared"), HubText.MaxLineChars);
            if (!info.HasBalanceRange) return HubText.OneLine(row, HubText.MaxLineChars);

            return HubText.OneLine(row + " | " + ModL10n.L("core.diag.channels.balance") + " " + HubText.Num(info.Spec.BalanceMin, "0.##") + " - " + HubText.Num(info.Spec.BalanceMax, "0.##"),
                HubText.MaxLineChars);
        }

        /// <summary>
        /// The mode of the default threshold for companies (AC-24.2): per company when the transpiler P10 is in the game; otherwise one global threshold, and under it the reason as the
        /// log has it (English, with the target and why the patch is not in the game).
        /// </summary>
        private static void AddCreditMode(List<string> lines)
        {
            if (ModCredit.IsPerCompanyActive)
            {
                lines.Add(ModL10n.L("core.diag.credit.perCompany"));
                return;
            }

            lines.Add(ModL10n.L("core.diag.credit.global"));
            lines.Add("  " + HubText.OneLine(ModCredit.DescribeMode(), HubText.MaxLineChars));
        }

        // ================================ mail ================================

        internal static DiagBlock MailBlock()
        {
            var lines = new List<string>
            {
                ModL10n.L("core.diag.mail.queue", ModMail.QueuedCount, ModMail.QueuedCriticalCount),
                ModL10n.L("core.diag.mail.deliveredToday", ModMail.DeliveredTodayCount),
            };

            string hold = MailScheduler.HoldText;
            if (!string.IsNullOrEmpty(hold)) lines.Add(ModL10n.L("core.diag.mail.hold", hold));
            return new DiagBlock(ModL10n.L("core.diag.mail.title"), lines);
        }
    }
}
