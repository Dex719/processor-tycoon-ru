using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Features.EventSystem;
using CpuSalesUI = ProcessorTycoon.MarketSystem.UI.CpuSalesUI;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The self-check of the feature at start (TSK-023; NFR-2, NFR-3, NFR-8, FR-15; design.md, "Самопроверка на старте" and "Error Handling"). The core's <c>ModSelfCheck</c> has no
    /// point where a feature adds findings of its own (see <c>mod/requests/design-flaw-recalls.md</c>), so the feature looks for itself and writes to <c>Player.log</c>:
    /// <c>self-check: OK</c> when all is well, otherwise <c>self-check: 1 error(s), 2 warning(s)</c> and a line per finding. The rules and the report are
    /// <see cref="FlawSelfCheckRules"/>'s (pure logic with the tests); this class collects the facts from the game and the core and acts on the verdict:
    /// <list type="bullet">
    /// <item><b>the patches</b> of the feature in the core's report (<c>ModHooks.Report</c>): the five targets of the design, <c>CpuSalesUI.UpdateDailyTexts</c>,
    /// <c>CreatorCpu.UpdateInfoTexts</c>, <c>CreatorCpu.Project</c>, <c>CreatorCpu.CreateCpu</c>, <c>ProjectCompletedHandler.AddCpu</c> (and <c>CreatorCpu.UpdateUI</c> of the switch),
    /// all <c>Optional</c>: a miss costs only its own ability (the mark, the hint, the rush switch), with one warning for it;</item>
    /// <item><b>the private fields</b> of <c>CpuSalesUI</c> and <c>CreatorCpu</c> by name with <c>AccessTools</c>, never by the shape the code was compiled against;</item>
    /// <item><b>the subscriptions</b> of the feature on <c>ModEvents.CpuReleased</c> and <c>ModEvents.CpuTransferred</c> (the backing field of the event is read);</item>
    /// <item><b>the required parts</b>: the six events <c>dfr.decide.*</c> of the pack, the four effect types, the channels <c>market.rating.cpu</c> (declared per CPU) and
    /// <c>contract.signChance</c>: a miss makes the feature Unavailable (<c>ModFeatures.MarkUnavailable</c>) with a line in the log, and the game goes on without it;</item>
    /// <item><b>the identifiers</b>: the event ids and the string keys of the feature carry the prefix <c>dfr.</c> (<c>ModIds</c>) and the keys exist;</item>
    /// <item><b>the content</b>: how many values of <c>flaws.json</c> were rejected.</item>
    /// </list>
    /// It only LOOKS, with one exception, the verdict on a missing required part. It never throws: a fact that cannot be collected is left out (and noted), a failing part of the
    /// check is a warning of its own.
    /// </summary>
    internal static class FlawSelfCheck
    {
        private const string FeatureId = DesignFlawRecallsFeature.FeatureId;
        private const BindingFlags AnyEvent = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static string[] _lastLines;

        /// <summary>The report of the last run; null before the first run.</summary>
        internal static FlawSelfCheckReport Last { get; private set; }

        /// <summary>
        /// Runs the check, writes the result to the log and, when a required part is missing, makes the feature Unavailable. Returns the report. Never throws.
        /// <paramref name="stage"/> is only a word for the log (<c>start</c>, <c>ready</c>).
        /// </summary>
        internal static FlawSelfCheckReport Run(DesignFlawRecallsFeature feature, string stage)
        {
            FlawSelfCheckReport report;
            try
            {
                report = FlawSelfCheckRules.Run(BuildInput(feature));
            }
            catch (Exception ex)
            {
                report = new FlawSelfCheckReport();
                report.Add(FlawCheckSeverity.Warning, FlawSelfCheckRules.AreaCheck, "the self-check itself failed (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }

            Last = report;
            try
            {
                Write(report, stage);
            }
            catch (Exception)
            {
                // the log must never be the reason the feature fails to start
            }

            if (report.IsFatal) MakeUnavailable(report);
            return report;
        }

        /// <summary>
        /// <c>ModTicks.GameReady</c>: the six decision events are looked for again when the definitions were not loaded yet at the start. Does nothing when the start already
        /// looked at them.
        /// </summary>
        internal static void OnGameReady(DesignFlawRecallsFeature feature)
        {
            FlawSelfCheckReport last = Last;
            if (last == null || last.EventsChecked) return;
            Run(feature, "ready");
        }

        // ---- the verdict ----

        private static void MakeUnavailable(FlawSelfCheckReport report)
        {
            string reason = report.FatalReason;
            ModLog.Error(FeatureId, "self-check: a required part is missing, the feature is unavailable until the game restarts (the game itself goes on): " + reason);
            ModFeatures.MarkUnavailable(FeatureId, reason);
        }

        private static void Write(FlawSelfCheckReport report, string stage)
        {
            string[] lines = Join(report);
            if (_lastLines != null && SameLines(_lastLines, lines))
            {
                ModLog.Debug(FeatureId, "self-check (" + stage + "): the same result as before: " + report.Summary);
                return;
            }
            _lastLines = lines;

            string summary = report.Summary + (stage == "start" ? string.Empty : " (" + stage + ")");
            if (report.IsOk) ModLog.Info(FeatureId, summary);
            else if (report.HasErrors) ModLog.Error(FeatureId, summary);
            else ModLog.Warn(FeatureId, summary);

            foreach (FlawCheckFinding finding in report.Findings)
            {
                if (finding.Severity == FlawCheckSeverity.Error) ModLog.Error(FeatureId, "self-check: " + finding);
            }
            foreach (FlawCheckFinding finding in report.Findings)
            {
                if (finding.Severity == FlawCheckSeverity.Warning) ModLog.Warn(FeatureId, "self-check: " + finding);
            }
            foreach (string note in report.Notes) ModLog.Debug(FeatureId, "self-check: note: " + note);
        }

        private static string[] Join(FlawSelfCheckReport report)
        {
            IList<string> lines = report.Lines();
            var all = new string[lines.Count + 1];
            all[0] = report.Summary;
            lines.CopyTo(all, 1);
            return all;
        }

        private static bool SameLines(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // ---- the facts ----

        /// <summary>Collects the facts. A fact that cannot be collected stays <c>null</c> (the rules note it); one broken source does not spoil the others.</summary>
        private static FlawSelfCheckInput BuildInput(DesignFlawRecallsFeature feature)
        {
            var input = new FlawSelfCheckInput();
            input.Patches = Collect("patches", PatchFacts);
            input.MemberProblem = ProbeMember;
            input.ReleaseSubscribed = CollectFlag("CpuReleased", () => IsSubscribed("CpuReleased", typeof(FlawRoller)));
            input.TransferSubscribed = CollectFlag("CpuTransferred", () => IsSubscribed("CpuTransferred", typeof(FlawTransferHandler)));
            input.ReleaseTracked = CollectFlag("release tracking", () => (bool?)ModEvents.IsReleaseTracked);

            IList<EventDefinition> events = Collect("events", () => EventRegistry.All);
            input.EventsLoaded = events != null && events.Count > 0;
            if (input.EventsLoaded) input.HasEvent = id => HasEvent(events, id);

            input.EffectTypes = Collect("effect types", () =>
            {
                IList<string> types = EventEffects.RegisteredTypes;
                return types == null || types.Count == 0 ? null : types;   // an empty table is "cannot tell" (the event system answers so when it is off), not "nothing registered"
            });

            input.Channels = Collect("channels", ChannelFacts);

            bool tableKnown = ModIds.PrefixesOf(FeatureId).Count > 0;
            input.EventIdProblem = id => EventIdProblem(tableKnown, id);
            input.KeyProblem = key => KeyProblem(tableKnown, key);
            input.Keys = Collect("keys", () => KeysOf(feature));

            FlawContentReport content = feature == null ? null : Collect("content", () => feature.ContentReport);
            if (content != null)
            {
                input.ContentErrors = content.Errors.Count;
                input.FirstContentError = content.Errors.Count > 0 ? content.Errors[0] : null;
            }
            return input;
        }

        private static T Collect<T>(string what, Func<T> source) where T : class
        {
            try
            {
                return source();
            }
            catch (Exception ex)
            {
                ModLog.Warn(FeatureId, "self-check: " + what + " could not be collected (" + ex.GetType().Name + ": " + ex.Message + ")");
                return null;
            }
        }

        private static bool? CollectFlag(string what, Func<bool?> source)
        {
            try
            {
                return source();
            }
            catch (Exception ex)
            {
                ModLog.Warn(FeatureId, "self-check: " + what + " could not be looked at (" + ex.GetType().Name + ": " + ex.Message + ")");
                return null;
            }
        }

        private static IList<FlawPatchFact> PatchFacts()
        {
            IReadOnlyList<PatchReport> report = ModHooks.Report;
            var facts = new List<FlawPatchFact>();
            for (int i = 0; i < report.Count; i++)
            {
                PatchReport row = report[i];
                if (!string.Equals(row.FeatureId, FeatureId, StringComparison.Ordinal)) continue;
                facts.Add(new FlawPatchFact(row.Target, row.Status == PatchStatus.Applied, row.Status == PatchStatus.Rejected, row.Status.ToString(), row.Detail));
            }
            return facts;
        }

        /// <summary>Null when the field is in the game class; otherwise why not.</summary>
        private static string ProbeMember(FlawMemberSpec member)
        {
            Type type = TypeOf(member.TypeName);
            if (type == null) return "the class is not in the game";
            return AccessTools.Field(type, member.Member) == null ? "the class has no such field" : null;
        }

        private static Type TypeOf(string name)
        {
            switch (name)
            {
                case "CpuSalesUI":
                    return typeof(CpuSalesUI);
                case "CreatorCpu":
                    return typeof(CreatorCpu);
                default:
                    return null;
            }
        }

        /// <summary>
        /// The handler of the feature (an object of <paramref name="handlerType"/>) is on the event of the core. A C# event cannot be looked into from outside, so the backing
        /// field of the event is read; a core that keeps it some other way gives <c>null</c> (not verified) and no problem.
        /// </summary>
        private static bool? IsSubscribed(string eventName, Type handlerType)
        {
            FieldInfo field = typeof(ModEvents).GetField(eventName, AnyEvent);
            if (field == null) return null;

            var handlers = field.GetValue(null) as Delegate;
            if (handlers == null) return false;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                if (handler.Target != null && handlerType.IsInstanceOfType(handler.Target)) return true;
            }
            return false;
        }

        private static bool HasEvent(IList<EventDefinition> events, string id)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != null && string.Equals(events[i].id, id, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static IList<FlawChannelFact> ChannelFacts()
        {
            var facts = new List<FlawChannelFact>();
            foreach (string name in new[] { FlawChannelNames.MarketRatingCpu, FlawChannelNames.ContractSignChance })
            {
                ChannelSpec spec = ModModifiers.SpecOf(name);
                facts.Add(new FlawChannelFact(name, ModModifiers.IsDeclared(name), spec != null && spec.PerCpu));
            }
            return facts;
        }

        // ---- the identifiers ----

        /// <summary>Null when the id passes the rules of the feature's prefix (<c>ModIds</c>; the literal prefix when the table has no row for the feature).</summary>
        private static string EventIdProblem(bool tableKnown, string id)
        {
            if (tableKnown) return ModIds.IsEventIdAllowed(FeatureId, id) ? null : ModIds.Explain(IdKind.EventId, FeatureId, id);
            return id != null && id.StartsWith(FlawContentValidator.KeyPrefix, StringComparison.Ordinal) ? null : "the id does not start with '" + FlawContentValidator.KeyPrefix + "'";
        }

        /// <summary>Null when the key passes the prefix rules and some string table has it.</summary>
        private static string KeyProblem(bool tableKnown, string key)
        {
            if (tableKnown)
            {
                if (!ModIds.IsL10nKeyAllowed(FeatureId, key)) return ModIds.Explain(IdKind.L10nKey, FeatureId, key);
            }
            else if (key == null || !key.StartsWith(FlawContentValidator.KeyPrefix, StringComparison.Ordinal))
            {
                return "the key does not start with '" + FlawContentValidator.KeyPrefix + "'";
            }
            return ModL10n.Has(key) ? null : "no string table has the key";
        }

        /// <summary>The keys the feature needs the moment the game starts: its name and description, its parameters, the page and the names of the flaw types.</summary>
        private static IList<string> KeysOf(DesignFlawRecallsFeature feature)
        {
            var keys = new List<string> { "dfr.feature.name", "dfr.feature.desc", FlawJournalPage.TitleKey, "dfr.rush.toggle", "dfr.rush.tooltip", FlawBadgeModel.BadgeKey, "dfr.hint.title" };
            foreach (ParamSpec spec in FlawParams.Create(FeatureId))
            {
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }

            if (feature != null)
            {
                foreach (FlawTypeDef type in feature.Balance.Types)
                {
                    if (!string.IsNullOrEmpty(type.NameKey) && !keys.Contains(type.NameKey)) keys.Add(type.NameKey);
                }
            }
            return keys;
        }
    }
}
