using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using PTMod.Core.Logic.Mail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameEmail = ProcessorTycoon.Desktop.Email.Email;
using GameEmailWindow = ProcessorTycoon.Desktop.Email.EmailWindow;

namespace PTMod.Core
{
    /// <summary>
    /// P16: <c>EmailWindow.UpdateEmailText</c>, postfix. The game calls it when the player opens a letter (and for the open letter when the theme or the
    /// language changes): the postfix shows the panel of the buttons under the text for a letter of the mod (<c>EmailID</c> of 100000 and above) and hides
    /// it for any other. The work is <see cref="MailView"/>'s.
    /// The method is reserved for the core (<c>ReservedTargets</c>: service <c>ModMail</c>): a feature's patch on it is rejected. Found by <c>ModHooks</c>'
    /// scan of the core at the Patch stage; if the patch cannot be applied the letters of the mod are still delivered and readable, only their buttons
    /// are missing (the deadlines still answer).
    /// </summary>
    internal static class MailViewPatches
    {
        private const string Site = "P16/postfix";

        [ModPatch(typeof(GameEmailWindow), "UpdateEmailText", PatchKind.Postfix)]
        private static void UpdateEmailTextPostfix(GameEmailWindow __instance, GameEmail email)
        {
            try
            {
                if (email == null || !ModHooks.CanRun(ModLog.CoreFeatureId, Site)) return;
                MailView.OnLetterShown(__instance, email.EmailID);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModLog.CoreFeatureId, Site, ex);
            }
        }
    }

    /// <summary>
    /// The panel of buttons under the text of an open letter of the mod (FR-14 items 3-6, 12; design.md "Панель кнопок в письме"): a vertical container
    /// <c>PTModMailPanel</c> in the parent of the letter's text, drawn from the stored record by the pure <see cref="MailPanelModel"/>. Top to bottom:
    /// <list type="number">
    /// <item><b>Status lines</b>: the core's deadline ("Осталось 3 дня", the right form of the number) while the mail is unanswered and has a period,
    /// then the line the feature set with <c>SetStatusLine</c>.</item>
    /// <item><b>Actions</b>: a <c>Widget.Button</c> each. A click calls the handler of the feature and the mail stays open; a used non-repeatable action and
    /// a disabled one are gray, with the reason as a hint and as a line under the button.</item>
    /// <item><b>Choices</b> while the mail is unanswered: a <c>Widget.Button</c> each, gray with the reason when disabled. After the answer the buttons are
    /// replaced by "Ваш ответ: ..." (or "Ответ по истечении срока: ..." when the deadline answered); the actions stay.</item>
    /// </list>
    /// When the feature is not running every button is gray, with one line "Фича «X» выключена — ответить нельзя" above them.
    /// <list type="bullet">
    /// <item><b>Redrawing.</b> The panel is drawn again when the letter is opened and when something it shows changes (<c>SetChoiceState</c>,
    /// <c>SetActionState</c>, <c>SetStatusLine</c>, <c>Refresh</c>, an answer, a click on an action, a feature switched on or off, a new game day, a
    /// change of the language) - only if the letter is the one that is open, and only if the picture would differ: the signature of what is drawn is
    /// compared first, so the game calling <c>UpdateEmailText</c> once per letter on a theme change costs nothing. The buttons are rebuilt, not edited
    /// (a handful of clones); the old ones are switched off and destroyed.</item>
    /// <item><b>Place.</b> The panel is a child of the parent of the letter's text (<c>Mail.Host</c>, <c>Texts</c>). If that parent lays its children out
    /// (a layout group) the panel is the next sibling of the text and the group places it; otherwise the panel is pinned under the text, as high as the text
    /// is, across its width. The look of both is for the owner to check in the game.</item>
    /// <item><b>Safe.</b> A failure is counted against the unit <c>P16</c> (five switch the view off for the session); a missing member of the game's
    /// window is logged once and the view stays off. Without the button template of the scene the lines are drawn and the buttons are not (one warning).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class MailView
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string RedrawSite = "P16/redraw";
        private const string PanelName = "PTModMailPanel";
        private const float PanelSpacing = 6f;
        private const float PanelGap = 8f;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly PanelTexts Texts = new PanelTexts();

        private static FieldInfo _contentTextField;
        private static bool _broken;            // the window of the game does not match: no panel this session
        private static bool _noButtonsWarned;

        private static int _openMailId;         // the mail whose letter is open; 0 = none (a letter of the game, or nothing)
        private static TMP_Text _contentText;   // the text of the open letter: the panel goes under it
        private static GameObject _panel;
        private static bool _hostHasLayout;     // the parent of the text lays out its children: the panel is one of them
        private static string _signature;       // what the panel shows now: the mail id and the picture

        // ---- start ----

        /// <summary>HostReady stage: the panel belongs to a scene, a feature's state and the language change what it says.</summary>
        [CoreInit(CoreInitStage.HostReady, 510, Optional = true)]
        internal static void StartMailView()
        {
            PTModHost.SceneLeaving += Forget;
            ModFeatures.StateChanged += OnFeatureStateChanged;
        }

        // ---- the letter is opened (P16) ----

        /// <summary>
        /// A letter is shown in the window of the game (the postfix of <c>UpdateEmailText</c>): the panel for a letter of the mod, nothing for the game's own.
        /// </summary>
        /// <param name="window">The <c>EmailWindow</c>; its <c>contentText</c> is the text the panel goes under.</param>
        /// <param name="emailId">The <c>EmailID</c> of the letter.</param>
        internal static void OnLetterShown(Component window, int emailId)
        {
            if (_broken) return;

            MailRecordModel mail = emailId < ModMail.FirstModMailId ? null : MailStore.Queue.Get(emailId);
            TMP_Text text = mail == null ? null : ContentTextOf(window);
            if (text == null)
            {
                _openMailId = 0;
                Hide();
                return;
            }

            if (!ReferenceEquals(text, _contentText))
            {
                _contentText = text;   // another window (a new scene): the panel of the old one went with it
                _panel = null;
                _signature = null;
            }
            _openMailId = emailId;
            Render(mail, false);
        }

        // ---- redrawing ----

        /// <summary>Something the open letter shows may have changed: draws it again if the letter is the open one and the picture differs.</summary>
        internal static void Invalidate(int mailId)
        {
            Redraw(mailId, false);
        }

        /// <summary>Draws the open letter's panel again if <paramref name="mailId"/> is the open one; <paramref name="force"/> rebuilds it even if the picture is the same.</summary>
        internal static void Redraw(int mailId, bool force)
        {
            try
            {
                if (_broken || mailId == 0 || mailId != _openMailId || _contentText == null) return;
                if (!ModHooks.CanRun(CoreId, RedrawSite)) return;

                MailRecordModel mail = MailStore.Queue.Get(mailId);
                if (mail == null)
                {
                    Hide();
                    return;
                }
                Render(mail, force);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, RedrawSite, ex);
            }
        }

        /// <summary>A new game day: the deadline line of the open letter counts down.</summary>
        internal static void OnDayPassed()
        {
            if (_openMailId != 0) Redraw(_openMailId, false);
        }

        /// <summary>The language changed: the captions and lines of the open letter are said again (nothing is rebuilt if the game's redraw of the letter already did it).</summary>
        internal static void OnLanguageChanged()
        {
            if (_openMailId != 0) Redraw(_openMailId, false);
        }

        /// <summary>The stored mail was replaced (a load, a new game): the letter that was open is not there any more.</summary>
        internal static void OnStoreReplaced()
        {
            _openMailId = 0;
            try
            {
                Hide();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, RedrawSite, ex);
            }
        }

        private static void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (_openMailId == 0) return;

            MailRecordModel mail = MailStore.Queue.Get(_openMailId);
            if (mail != null && string.Equals(mail.FeatureId, featureId, StringComparison.Ordinal)) Redraw(_openMailId, false);
        }

        /// <summary>The scene is being left: the window, the text and the panel go with it.</summary>
        internal static void Forget()
        {
            _openMailId = 0;
            _contentText = null;
            _panel = null;
            _signature = null;
            _hostHasLayout = false;
        }

        // ---- drawing ----

        private static void Render(MailRecordModel mail, bool force)
        {
            bool running = ModMail.IsFeatureActive(mail.FeatureId);
            List<MailPanelItem> items = MailPanelModel.Build(mail, MailStore.Env.Today(), running, Texts);
            string signature = mail.MailId.ToString(CultureInfo.InvariantCulture) + "\u001d" + MailPanelModel.Signature(items);

            if (items.Count == 0)
            {
                Hide();
                _signature = signature;
                return;
            }

            if (!force && _panel != null && signature == _signature)
            {
                _panel.SetActive(true);
                PlaceBelowText();
                return;
            }

            if (Rebuild(mail.MailId, items)) _signature = signature;
        }

        private static bool Rebuild(int mailId, List<MailPanelItem> items)
        {
            GameObject panel = EnsurePanel();
            if (panel == null) return false;

            ClearPanel(panel);
            Transform parent = panel.transform;
            bool buttonsReady = ModUI.IsReady && ModUITemplates.Has(ModUITemplates.WidgetButton);

            foreach (MailPanelItem item in items)
            {
                if (item.Kind == MailPanelKind.Action || item.Kind == MailPanelKind.Choice)
                {
                    if (buttonsReady) AddButton(parent, mailId, item);
                    else WarnNoButtons();
                }
                else
                {
                    ModUI.Text(parent, item.Text, item.Kind == MailPanelKind.Answer);
                }
            }

            panel.SetActive(true);
            PlaceBelowText();

            var hostRect = panel.transform.parent as RectTransform;
            if (hostRect != null) LayoutRebuilder.MarkLayoutForRebuild(hostRect);
            return true;
        }

        private static void AddButton(Transform parent, int mailId, MailPanelItem item)
        {
            string id = item.Id;
            Action onClick;
            if (item.Kind == MailPanelKind.Action) onClick = () => ModMail.ClickAction(mailId, id);
            else onClick = () => ModMail.ClickChoice(mailId, id);

            ButtonUI button = ModUI.Button(parent, item.Text, onClick);
            if (button == null) return;   // ModUI has said why

            if (!item.Enabled) button.Interactable = false;
            if (!string.IsNullOrEmpty(item.Tip)) ModUI.Tooltip(button.gameObject, string.Empty, item.Tip);
        }

        private static void WarnNoButtons()
        {
            if (_noButtonsWarned) return;
            _noButtonsWarned = true;
            ModLog.Warn(CoreId, "mail: the button template (Widget.Button) is not in this scene; the choices and actions of a mod mail are not shown");
        }

        /// <summary>The next picture replaces the old one: the old buttons and lines are switched off now (the layout stops counting them) and destroyed at the end of the frame.</summary>
        private static void ClearPanel(GameObject panel)
        {
            Transform root = panel.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        private static void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        // ---- the panel object ----

        private static GameObject EnsurePanel()
        {
            if (_panel != null) return _panel;
            if (_contentText == null) return null;

            Transform host = _contentText.transform.parent;
            if (host == null)
            {
                ModLog.Warn(CoreId, "mail: the text of the letter has no parent to put the panel of buttons in; the buttons are not shown");
                return null;
            }

            var panel = new GameObject(PanelName, typeof(RectTransform));
            panel.layer = host.gameObject.layer;
            var rect = (RectTransform)panel.transform;
            rect.SetParent(host, false);

            var flow = panel.AddComponent<VerticalLayoutGroup>();
            flow.spacing = PanelSpacing;
            flow.childAlignment = TextAnchor.UpperLeft;
            flow.childControlWidth = true;
            flow.childControlHeight = true;
            flow.childForceExpandWidth = true;
            flow.childForceExpandHeight = false;

            _hostHasLayout = host.GetComponent<LayoutGroup>() != null;
            if (_hostHasLayout)
            {
                panel.AddComponent<LayoutElement>().flexibleWidth = 1f;
                rect.SetSiblingIndex(_contentText.transform.GetSiblingIndex() + 1);   // right under the text
            }
            else
            {
                panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                rect.SetAsLastSibling();
            }

            _panel = panel;
            DescribeHost(host);
            return panel;
        }

        // The pin under the text for a host that lays nothing out: the panel takes the width of the text's box and starts a gap below the last line.
        private static void PlaceBelowText()
        {
            if (_hostHasLayout || _panel == null || _contentText == null) return;

            var rect = (RectTransform)_panel.transform;
            var hostRect = rect.parent as RectTransform;
            if (hostRect == null) return;

            var corners = new Vector3[4];
            _contentText.rectTransform.GetWorldCorners(corners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            Vector3 topLeft = hostRect.InverseTransformPoint(corners[1]);
            Vector3 topRight = hostRect.InverseTransformPoint(corners[2]);

            float textBottom = topLeft.y - Mathf.Max(0f, _contentText.preferredHeight);   // the text hangs from the top of its box
            Rect host = hostRect.rect;
            float leftInset = topLeft.x - host.xMin;
            float rightInset = host.xMax - topRight.x;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-(leftInset + rightInset), rect.sizeDelta.y);   // the height is the fitter's
            rect.anchoredPosition = new Vector2((leftInset - rightInset) * 0.5f, textBottom - PanelGap - host.yMax);
        }

        // One line for the owner's check of the look: where the panel went and how it is placed.
        private static void DescribeHost(Transform host)
        {
            try
            {
                if (!ModSettings.VerboseLog) return;

                GameObject template = ModUITemplates.Get(ModUITemplates.MailHost);
                string note = template == null ? "the Mail.Host template is not in the scene"
                    : ReferenceEquals(template.transform, host) ? "this is the Mail.Host template" : "NOT the Mail.Host template '" + template.name + "'";
                ModLog.Debug(CoreId, "mail: panel of buttons put in '" + host.name + "' (" + note + "), " + (_hostHasLayout ? "placed by its layout group" : "pinned under the text"));
            }
            catch (Exception)
            {
                // A line of the log is not worth a failure.
            }
        }

        // The window's private text box, found by name on the window's own type or a base type; a missing one switches the view off for the session.
        private static TMP_Text ContentTextOf(Component window)
        {
            if (window == null) return null;

            try
            {
                FieldInfo field = _contentTextField;
                if (field == null)
                {
                    for (Type type = window.GetType(); type != null && field == null; type = type.BaseType)
                        field = type.GetField("contentText", AnyInstance);
                    if (field == null) throw new MissingFieldException(window.GetType().FullName, "contentText");
                    _contentTextField = field;
                }
                return field.GetValue(window) as TMP_Text;
            }
            catch (Exception ex)
            {
                _broken = true;
                ModLog.Error(CoreId, "mail: the game's EmailWindow has no readable contentText (another game version?); the buttons of a mod mail are not shown for this session", ex);
                return null;
            }
        }

        /// <summary>The words of the panel: the tables of the mod in the current language, through the same rules as the letter's own text.</summary>
        private sealed class PanelTexts : IMailPanelTexts
        {
            public string Label(string key, LocText inline)
            {
                return MailFactory.TextOf(key, inline, null);
            }

            public string Tip(string key)
            {
                return ModUI.TextOfKey(key);
            }

            public string Reason(string key, string[] args)
            {
                return MailFactory.TextOf(key, null, args);
            }

            public string DaysLeft(int days)
            {
                return ModL10n.Plural("core.mail.daysLeft", days);
            }

            public string Answer(string label, bool afterDeadline)
            {
                return ModL10n.L(afterDeadline ? "core.mail.answerTimeout" : "core.mail.answer", label);
            }

            public string FeatureOff(string featureId)
            {
                return ModL10n.L("core.mail.featureOff", ModMail.FeatureNameOf(featureId));
            }
        }
    }
}

namespace PTMod.Core.Logic.Mail
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // What the panel shows, without Unity (nothing below this line may touch the engine, the game, the log or ModL10n): at integration this
    // block moves to Core/Logic/Mail/MailPanelModel.cs unchanged, where the test runner reaches it.
    // ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>What a row of the panel is.</summary>
    internal enum MailPanelKind
    {
        /// <summary>A line under the text of the letter: the deadline of the core, the line of the feature.</summary>
        Status = 0,

        /// <summary>The line above the buttons that says the feature is not running.</summary>
        Note,

        /// <summary>A button of an action.</summary>
        Action,

        /// <summary>A button of a choice (the mail is unanswered).</summary>
        Choice,

        /// <summary>The reason a button above is disabled, under that button.</summary>
        Reason,

        /// <summary>"Ваш ответ: ..." in place of the choices.</summary>
        Answer
    }

    /// <summary>One row of the panel: a line of text or a button with its state and hint.</summary>
    internal sealed class MailPanelItem
    {
        public MailPanelKind Kind;

        /// <summary>The id of the choice or action; null for a line.</summary>
        public string Id;

        /// <summary>The text of the line or the caption of the button.</summary>
        public string Text = string.Empty;

        /// <summary>A button can be pressed (always true for a line).</summary>
        public bool Enabled = true;

        /// <summary>The hint of a button: its own tooltip text and, when it is gray, the reason; empty for none.</summary>
        public string Tip = string.Empty;
    }

    /// <summary>The words of the panel in the current language; the runtime gives the mod's tables, a test gives its own.</summary>
    internal interface IMailPanelTexts
    {
        /// <summary>A button caption or the name of the chosen answer: the table key, else the inline text.</summary>
        string Label(string key, LocText inline);

        /// <summary>The text of a <c>TooltipKey</c>.</summary>
        string Tip(string key);

        /// <summary>The text of a <c>DisabledReasonKey</c> filled with its arguments.</summary>
        string Reason(string key, string[] args);

        /// <summary>"Осталось N дней" with the right form of the number.</summary>
        string DaysLeft(int days);

        /// <summary>"Ваш ответ: X", or "Ответ по истечении срока: X" when the deadline answered.</summary>
        string Answer(string label, bool afterDeadline);

        /// <summary>"Фича «X» выключена — ответить нельзя".</summary>
        string FeatureOff(string featureId);
    }

    /// <summary>
    /// Makes the rows of the panel from a stored mail (design.md "Панель кнопок в письме"): status lines, then the feature-off line, the actions, then the
    /// choices - or, when the mail is answered, the answer in place of the choices. A button is active only if the feature is running and the record says so
    /// (<see cref="MailAnswerRules"/>); a gray button carries its reason in the hint and, if it is the button's own reason, in a line under it.
    /// </summary>
    internal static class MailPanelModel
    {
        public static List<MailPanelItem> Build(MailRecordModel mail, DateTime today, bool featureActive, IMailPanelTexts texts)
        {
            var items = new List<MailPanelItem>();
            if (mail == null || texts == null) return items;

            bool pending = mail.IsPending;

            // 1. The status lines: the period of the core while the mail waits for an answer, then the feature's own line.
            if (pending && mail.TimeoutDays > 0 && mail.Deadline.HasValue)
                items.Add(Line(MailPanelKind.Status, texts.DaysLeft(MailTimeoutRules.DaysLeft(mail.Deadline.Value, today))));
            if (!string.IsNullOrEmpty(mail.StatusLine))
                items.Add(Line(MailPanelKind.Status, mail.StatusLine));

            // 2. A feature that is not running answers nothing: one line says so, every button is gray.
            string off = featureActive ? null : texts.FeatureOff(mail.FeatureId);
            bool anyButton = mail.Actions.Count > 0 || (pending && mail.Choices.Count > 0);
            if (off != null && anyButton) items.Add(Line(MailPanelKind.Note, off));

            // 3. The actions.
            foreach (MailActionModel action in mail.Actions)
            {
                if (action == null) continue;

                bool enabled = MailAnswerRules.IsActionActive(action, featureActive);
                bool usedUp = action.Used && !action.Repeatable;
                string reason = enabled || usedUp ? null : ReasonOf(action.DisabledReasonKey, action.DisabledReasonArgs, texts);
                AddButton(items, MailPanelKind.Action, action.Id, LabelOf(action.LabelKey, action.Label, action.Id, texts), enabled, action.TooltipKey, reason, off, texts);
            }

            // 4. The choices, or the answer.
            if (pending)
            {
                foreach (MailChoiceModel choice in mail.Choices)
                {
                    if (choice == null) continue;

                    bool enabled = MailAnswerRules.IsChoiceActive(mail, choice, featureActive);
                    string reason = enabled ? null : ReasonOf(choice.DisabledReasonKey, choice.DisabledReasonArgs, texts);
                    AddButton(items, MailPanelKind.Choice, choice.Id, LabelOf(choice.LabelKey, choice.Label, choice.Id, texts), enabled, choice.TooltipKey, reason, off, texts);
                }
            }
            else if (mail.Choices.Count > 0 && !string.IsNullOrEmpty(mail.ChoiceId))
            {
                MailChoiceModel chosen = mail.FindChoice(mail.ChoiceId);
                string name = chosen == null ? mail.ChoiceId : LabelOf(chosen.LabelKey, chosen.Label, chosen.Id, texts);
                bool afterDeadline = mail.Resolution == MailRecordModel.ResolutionTimeout;
                items.Add(Line(MailPanelKind.Answer, texts.Answer(name, afterDeadline)));
            }

            return items;
        }

        /// <summary>
        /// A text that is equal for two panels exactly when they look and behave the same (the kind, id, state, text and hint of every row); the runtime
        /// compares it to skip a redraw. The id of the mail is added by the caller: the same rows of another mail click on another mail.
        /// </summary>
        public static string Signature(IList<MailPanelItem> items)
        {
            var sb = new StringBuilder();
            if (items == null) return string.Empty;

            for (int i = 0; i < items.Count; i++)
            {
                MailPanelItem item = items[i];
                sb.Append((int)item.Kind).Append('\u001f').Append(item.Id).Append('\u001f').Append(item.Enabled ? '1' : '0').Append('\u001f')
                  .Append(item.Text).Append('\u001f').Append(item.Tip).Append('\u001e');
            }
            return sb.ToString();
        }

        // ---- internals ----

        private static void AddButton(List<MailPanelItem> items, MailPanelKind kind, string id, string label, bool enabled, string tooltipKey,
            string reason, string featureOff, IMailPanelTexts texts)
        {
            string tip = string.IsNullOrEmpty(tooltipKey) ? string.Empty : texts.Tip(tooltipKey) ?? string.Empty;
            if (!enabled)
            {
                string why = featureOff ?? reason;   // a feature that is off explains every button the same way, once, above them
                if (!string.IsNullOrEmpty(why)) tip = tip.Length == 0 ? why : tip + "\n" + why;
            }

            items.Add(new MailPanelItem { Kind = kind, Id = id, Text = label, Enabled = enabled, Tip = tip });
            if (!enabled && featureOff == null && !string.IsNullOrEmpty(reason)) items.Add(Line(MailPanelKind.Reason, reason));
        }

        private static string LabelOf(string key, LocText inline, string id, IMailPanelTexts texts)
        {
            string label = texts.Label(key, inline);
            return string.IsNullOrEmpty(label) ? id : label;
        }

        private static string ReasonOf(string key, string[] args, IMailPanelTexts texts)
        {
            return string.IsNullOrEmpty(key) ? null : texts.Reason(key, args);
        }

        private static MailPanelItem Line(MailPanelKind kind, string text)
        {
            return new MailPanelItem { Kind = kind, Text = text ?? string.Empty };
        }
    }
}
