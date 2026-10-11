using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GamePlayer = ProcessorTycoon.Player;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The estimate of the risk of a hidden flaw in the CPU designer (FR-14, Could; design.md Component 15). A postfix of <c>CreatorCpu.UpdateInfoTexts</c> keeps a line
    /// "Hidden flaw risk: ~N %" under the failure rate: the probability of the roll at the release (<see cref="FlawRiskHint"/> over <see cref="FlawRiskModel.Probability"/>,
    /// the same function and the same <c>flawFrequency</c>) from the numbers of the design on the screen, rounded to 5 percent. It follows the sliders, the overclock and the
    /// switch "Rush project" (<see cref="RushSource"/>), shows while <c>showRiskHint</c> is on whatever the difficulty, and draws no random number (a source test checks it).
    /// <para>
    /// <c>UpdateInfoTexts</c> runs every frame while the designer is open, so the postfix does its work at most ten times a second and writes the text only when it changed.
    /// The private numbers of the designer (<c>failureRate</c>, <c>frequency</c>, <c>maxFrequency</c>, <c>temperature</c>, <c>currentPackage</c>, <c>currentLithography</c>,
    /// <c>nodeManufacturer</c>, <c>projectTime</c>, and <c>failureRateText</c> as the place of the line) are read through <c>AccessTools</c>, found once. If one of the
    /// required ones is missing (another game version) the hint turns itself off for the session with a line in the log, and the designer is left as the game made it.
    /// </para>
    /// The patch is <c>Optional</c> with a low priority (it runs after the other mods' postfixes of the same method).
    /// </summary>
    internal static class DesignerRiskHint
    {
        private const string Feature = DesignFlawRecallsFeature.FeatureId;
        private const string Site = "DesignerRiskHint/UpdateInfoTexts.postfix";
        private const float Interval = 0.1f;
        private const float PlacementGap = 4f;

        /// <summary>
        /// The state of the switch "Rush project" (FR-20): <c>true</c> when it is on. The feature sets it to <c>RushProjectOption.IsRushOn</c> in <c>Register</c> (TSK-029);
        /// without a source, or while the switch is hidden, the estimate is made for an ordinary project. A throwing source counts as "off".
        /// </summary>
        internal static Func<bool> RushSource { get; set; }

        private static readonly string[] RequiredFields =
        {
            "failureRate", "frequency", "maxFrequency", "temperature", "currentPackage", "currentLithography", "nodeManufacturer", "failureRateText",
        };

        private static FieldInfo _failureRate, _frequency, _maxFrequency, _temperature, _package, _lithography, _manufacturer, _projectTime, _anchor;
        private static bool _resolved;
        private static bool _off;

        private static CreatorCpu _creator;
        private static GameObject _root;
        private static InfoText _line;
        private static string _shown;
        private static float _nextTime;

        /// <summary>The designer updated its texts (every frame while it is open).</summary>
        [ModPatch(typeof(CreatorCpu), "UpdateInfoTexts", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true, Priority = 200)]
        private static void PostfixUpdateInfoTexts(CreatorCpu __instance)
        {
            if (_off || __instance == null) return;

            float now = Time.unscaledTime;
            if (now < _nextTime && ReferenceEquals(_creator, __instance)) return;
            _nextTime = now + Interval;

            // The guard is spelled out: the postfix runs often, and a closure per call is not wanted.
            if (!ModHooks.CanRun(Feature, Site)) return;
            try
            {
                Refresh(__instance);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, Site, ex);
            }
        }

        private static void Refresh(CreatorCpu creator)
        {
            bool wanted = ModFeatures.IsActive(Feature) && FlawSettings.ShowRiskHint;
            if (!wanted)
            {
                SetVisible(false);
                return;
            }

            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            if (feature == null || !Resolve()) return;
            if (!EnsureLine(creator)) return;   // no template yet: tried again in a tenth of a second

            FlawRiskInput input = Read(creator);
            float probability = FlawRiskHint.Probability(input, feature.Balance, FlawSettings.FlawFrequency, RushIsOn());
            string text = ValueText(probability);
            if (!string.Equals(text, _shown, StringComparison.Ordinal))
            {
                _shown = text;
                _line.SetTexts(ModL10n.L("dfr.hint.title"), text);
            }
            SetVisible(true);
        }

        // ---- the numbers of the design ----

        private static FlawRiskInput Read(CreatorCpu creator)
        {
            var input = new FlawRiskInput();
            input.FailureRate = (float)_failureRate.GetValue(creator);
            input.Frequency = (float)_frequency.GetValue(creator);
            input.MaxFrequency = (float)_maxFrequency.GetValue(creator);   // the designer's own ceiling: the base one plus the research, the way the release reads it
            input.Temperature = (float)_temperature.GetValue(creator);

            var package = _package.GetValue(creator) as GamePackage;
            input.SafeTemperature = package != null ? package.SafeTemperature : 0f;

            object node = _lithography.GetValue(creator);
            var manufacturer = _manufacturer.GetValue(creator) as GameCompany;
            input.HasNodeRecord = node != null && GameCpuView.HasNodeRecord(manufacturer, node);
            input.NodeProgress = input.HasNodeRecord ? manufacturer.CurrentNodeLearningProgress((GameProcessNode)node) : 0f;

            // The release year: the project ends projectTime days from today.
            int projectDays = _projectTime == null ? 0 : (int)_projectTime.GetValue(creator);
            DateTime today = ModTicks.Today;
            input.Year = today == default(DateTime) ? 0 : today.AddDays(Math.Max(0, projectDays)).Year;
            input.Difficulty = GamePlayer.Instance != null ? GamePlayer.Instance.DifficultyLevel : 2;
            return input;
        }

        private static bool RushIsOn()
        {
            Func<bool> source = RushSource;
            if (source == null) return false;

            try
            {
                return source();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, Site + "/rush", ex);
                return false;
            }
        }

        private static string ValueText(float probability)
        {
            if (FlawRiskHint.IsBelowStep(probability)) return ModL10n.L("dfr.hint.low");
            return ModL10n.L("dfr.hint.value", FlawRiskHint.Percent(probability));
        }

        // ---- the fields of the designer ----

        /// <summary>Finds the private fields once. False (and the hint is off for the session, with a line in the log) when a required one is missing.</summary>
        private static bool Resolve()
        {
            if (_resolved) return !_off;
            _resolved = true;

            _failureRate = AccessTools.Field(typeof(CreatorCpu), "failureRate");
            _frequency = AccessTools.Field(typeof(CreatorCpu), "frequency");
            _maxFrequency = AccessTools.Field(typeof(CreatorCpu), "maxFrequency");
            _temperature = AccessTools.Field(typeof(CreatorCpu), "temperature");
            _package = AccessTools.Field(typeof(CreatorCpu), "currentPackage");
            _lithography = AccessTools.Field(typeof(CreatorCpu), "currentLithography");
            _manufacturer = AccessTools.Field(typeof(CreatorCpu), "nodeManufacturer");
            _anchor = AccessTools.Field(typeof(CreatorCpu), "failureRateText");
            _projectTime = AccessTools.Field(typeof(CreatorCpu), "projectTime");   // optional: without it the release year is this year

            FieldInfo[] required = { _failureRate, _frequency, _maxFrequency, _temperature, _package, _lithography, _manufacturer, _anchor };
            for (int i = 0; i < required.Length; i++)
            {
                if (required[i] != null) continue;

                _off = true;
                ModLog.Warn(Feature, "designer: the risk hint is off for this session: CreatorCpu has no field '" + RequiredFields[i] + "' (another version of the game?)");
                return false;
            }
            return true;
        }

        // ---- the line ----

        private static bool EnsureLine(CreatorCpu creator)
        {
            if (ReferenceEquals(_creator, creator) && _line != null && _root != null) return true;

            _creator = null;
            _line = null;
            _root = null;
            _shown = null;

            var anchorText = _anchor.GetValue(creator) as InfoText;
            if (anchorText == null) return false;

            Transform anchor = anchorText.transform;
            Transform parent = anchor.parent;
            if (parent == null) return false;
            if (parent.GetComponent<HorizontalLayoutGroup>() != null && parent.parent != null)
            {
                // The failure rate sits in a row with its caption: the new line goes after that row, not into it.
                anchor = parent;
                parent = parent.parent;
            }

            InfoText line = ModUI.Info(parent, ModL10n.L("dfr.hint.title"), string.Empty, ModL10n.L("dfr.hint.tip"));
            if (line == null) return false;

            Place(line.transform, anchor, parent);
            _creator = creator;
            _line = line;
            _root = line.gameObject;
            ModLog.Info(Feature, "designer: the risk hint is in the CPU designer (under " + anchor.name + ")");
            return true;
        }

        /// <summary>Puts the new line right after the anchor: in a layout group by its order among the children, otherwise by position (one row lower).</summary>
        private static void Place(Transform root, Transform anchor, Transform parent)
        {
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                root.SetSiblingIndex(Math.Min(anchor.GetSiblingIndex() + 1, parent.childCount - 1));
                return;
            }

            var rootRect = root as RectTransform;
            var anchorRect = anchor as RectTransform;
            if (rootRect == null || anchorRect == null) return;

            float height = anchorRect.rect.height > 0f ? anchorRect.rect.height : rootRect.rect.height;
            rootRect.anchorMin = anchorRect.anchorMin;
            rootRect.anchorMax = anchorRect.anchorMax;
            rootRect.pivot = anchorRect.pivot;
            rootRect.sizeDelta = new Vector2(anchorRect.sizeDelta.x, height);
            rootRect.anchoredPosition = anchorRect.anchoredPosition - new Vector2(0f, height + PlacementGap);
        }

        private static void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }
    }
}
