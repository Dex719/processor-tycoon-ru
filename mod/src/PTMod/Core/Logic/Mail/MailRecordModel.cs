using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Save;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>
    /// A one-shot answer button of a mail (the logic side of <c>ModMailChoice</c>): the first click resolves the mail.
    /// Text is a table key, an inline <see cref="LocText"/> or both (key first), exactly as in the side file.
    /// </summary>
    public sealed class MailChoiceModel
    {
        private string[] disabledReasonArgs = new string[0];

        public string Id { get; set; }

        public string LabelKey { get; set; }

        public LocText Label { get; set; }

        public string TooltipKey { get; set; }

        /// <summary>False: the button is inactive and shows <see cref="DisabledReasonKey"/> as a hint and under the button.</summary>
        public bool Enabled { get; set; } = true;

        public string DisabledReasonKey { get; set; }

        /// <summary>Arguments of <see cref="DisabledReasonKey"/>; never null.</summary>
        public string[] DisabledReasonArgs
        {
            get { return disabledReasonArgs; }
            set { disabledReasonArgs = value ?? new string[0]; }
        }

        /// <summary>True when the button has some text (a key or an inline text).</summary>
        public bool HasLabel
        {
            get { return !string.IsNullOrEmpty(LabelKey) || (Label != null && !Label.IsEmpty); }
        }
    }

    /// <summary>A button of a mail that calls the feature and does not close the mail (the logic side of <c>ModMailAction</c>).</summary>
    public sealed class MailActionModel
    {
        private string[] disabledReasonArgs = new string[0];

        public string Id { get; set; }

        public string LabelKey { get; set; }

        public LocText Label { get; set; }

        public string TooltipKey { get; set; }

        public bool Enabled { get; set; } = true;

        public string DisabledReasonKey { get; set; }

        /// <summary>Arguments of <see cref="DisabledReasonKey"/>; never null.</summary>
        public string[] DisabledReasonArgs
        {
            get { return disabledReasonArgs; }
            set { disabledReasonArgs = value ?? new string[0]; }
        }

        /// <summary>False: the action becomes inactive after the first click (<see cref="Used"/>).</summary>
        public bool Repeatable { get; set; } = true;

        /// <summary>The action was clicked at least once.</summary>
        public bool Used { get; set; }

        public bool HasLabel
        {
            get { return !string.IsNullOrEmpty(LabelKey) || (Label != null && !Label.IsEmpty); }
        }
    }

    /// <summary>
    /// One mod mail without Unity or game objects (FR-14): who sent it, the text as keys and arguments, priority, choices and actions,
    /// the dates of the life cycle and the player's answer. This is the data that <see cref="MailQueue"/> orders, delivers and saves;
    /// the runtime <c>ModMailRecord</c> (TSK-037) is a view of it. The queue owns the life-cycle fields: <see cref="MailId"/>
    /// (assigned by <see cref="MailQueue.Enqueue"/>), <see cref="DeliveredOn"/> and <see cref="Deadline"/> (set at delivery); everything
    /// else is plain state that the integration changes directly (<see cref="Read"/>, <see cref="ChoiceId"/>, <see cref="Resolution"/>,
    /// <see cref="StatusLine"/>, the buttons' state).
    /// <para>
    /// Side file mapping (<see cref="MailRecordDto"/>): a delivered record is written to <c>mails</c> with <c>delivered</c> and <c>deadline</c>
    /// (<see cref="DeliveredOn"/> + <see cref="TimeoutDays"/>). A queued record has no <c>delivered</c>, and the DTO has no field for the
    /// timeout, so <c>deadline</c> carries a provisional value there, <see cref="SentOn"/> + <see cref="TimeoutDays"/>, from which the
    /// timeout is restored on load (the real deadline is computed at delivery, AC-14.8).
    /// </para>
    /// </summary>
    public sealed class MailRecordModel
    {
        /// <summary>Lowest mail id; mirrors <c>ModMail.FirstModMailId</c>. ID 0 is the vanilla Year Summary.</summary>
        public const int FirstId = 100000;

        /// <summary>Longest answer period accepted (100 years); keeps date arithmetic far from <see cref="DateTime.MaxValue"/>.</summary>
        public const int MaxTimeoutDays = 36500;

        public const string ResolutionPending = "Pending";
        public const string ResolutionPlayer = "Player";
        public const string ResolutionTimeout = "Timeout";
        public const string ResolutionProgrammatic = "Programmatic";

        private const string DateFormat = "yyyy-MM-dd";

        private string[] args = new string[0];

        /// <summary>Mail id (>= <see cref="FirstId"/>), assigned by the queue; 0 while the record is not in a queue.</summary>
        public int MailId { get; internal set; }

        /// <summary>Id of the sending feature.</summary>
        public string FeatureId { get; set; }

        /// <summary><c>&lt;prefix&gt;:&lt;...&gt;</c>; tells the mails of one feature apart and is what <c>Find</c> looks for.</summary>
        public string Tag { get; set; }

        public MailPriority Priority { get; set; } = MailPriority.Normal;

        public string TitleKey { get; set; }

        public string BodyKey { get; set; }

        public LocText Title { get; set; }

        public LocText Body { get; set; }

        /// <summary>Already formatted arguments of the texts; never null.</summary>
        public string[] Args
        {
            get { return args; }
            set { args = value ?? new string[0]; }
        }

        public List<MailChoiceModel> Choices { get; } = new List<MailChoiceModel>();

        public List<MailActionModel> Actions { get; } = new List<MailActionModel>();

        /// <summary>Answer period in days counted from the delivery date; 0 = no deadline.</summary>
        public int TimeoutDays { get; set; }

        /// <summary>The choice applied when the deadline passes; required when <see cref="TimeoutDays"/> is above 0.</summary>
        public string DefaultChoiceId { get; set; }

        /// <summary>The day the feature sent the mail (the date of the <c>Enqueue</c> call).</summary>
        public DateTime SentOn { get; set; }

        /// <summary>The day the mail was delivered; null while it waits in the queue.</summary>
        public DateTime? DeliveredOn { get; internal set; }

        /// <summary><see cref="DeliveredOn"/> + <see cref="TimeoutDays"/>; null without a deadline or before delivery.</summary>
        public DateTime? Deadline { get; internal set; }

        public bool Read { get; set; }

        /// <summary>The id of the chosen answer; null while the mail is unanswered.</summary>
        public string ChoiceId { get; set; }

        /// <summary>One of the <c>Resolution*</c> constants: who resolved the mail.</summary>
        public string Resolution { get; set; } = ResolutionPending;

        /// <summary>The line under the text (a feature timer); null = none.</summary>
        public string StatusLine { get; set; }

        public bool IsDelivered
        {
            get { return DeliveredOn.HasValue; }
        }

        public bool IsPending
        {
            get { return Resolution == ResolutionPending; }
        }

        /// <summary>True for a delivered, unanswered mail whose deadline is <paramref name="today"/> or earlier (the default choice is due).</summary>
        public bool IsExpired(DateTime today)
        {
            return IsDelivered && IsPending && Deadline.HasValue && today.Date >= Deadline.Value;
        }

        public MailChoiceModel FindChoice(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Choices.Count; i++)
            {
                if (Choices[i] != null && string.Equals(Choices[i].Id, id, StringComparison.Ordinal)) return Choices[i];
            }
            return null;
        }

        public MailActionModel FindAction(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i] != null && string.Equals(Actions[i].Id, id, StringComparison.Ordinal)) return Actions[i];
            }
            return null;
        }

        /// <summary>
        /// Checks a mail that a feature wants to send. Returns null when it is acceptable, otherwise the first problem (English, one line):
        /// no feature, undefined priority, no title or body text, a timeout out of range, a choice or action without a unique id or a label,
        /// a default choice that is not one of the choices, or a timeout without a default choice.
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrEmpty(FeatureId)) return "the mail has no feature id";
            if (!Enum.IsDefined(typeof(MailPriority), Priority)) return "priority " + (int)Priority + " is not Low, Normal or Critical";
            if (!HasText(TitleKey, Title)) return "the mail has no title (TitleKey or Title)";
            if (!HasText(BodyKey, Body)) return "the mail has no body (BodyKey or Body)";
            if (TimeoutDays < 0 || TimeoutDays > MaxTimeoutDays)
                return "TimeoutDays " + TimeoutDays.ToString(CultureInfo.InvariantCulture) + " is not in [0, " + MaxTimeoutDays.ToString(CultureInfo.InvariantCulture) + "]";

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Choices.Count; i++)
            {
                MailChoiceModel choice = Choices[i];
                if (choice == null) return "choice #" + i.ToString(CultureInfo.InvariantCulture) + " is null";
                if (string.IsNullOrEmpty(choice.Id)) return "choice #" + i.ToString(CultureInfo.InvariantCulture) + " has no id";
                if (!seen.Add(choice.Id)) return "choice id \"" + choice.Id + "\" is used twice";
                if (!choice.HasLabel) return "choice \"" + choice.Id + "\" has no label";
            }

            seen.Clear();
            for (int i = 0; i < Actions.Count; i++)
            {
                MailActionModel action = Actions[i];
                if (action == null) return "action #" + i.ToString(CultureInfo.InvariantCulture) + " is null";
                if (string.IsNullOrEmpty(action.Id)) return "action #" + i.ToString(CultureInfo.InvariantCulture) + " has no id";
                if (!seen.Add(action.Id)) return "action id \"" + action.Id + "\" is used twice";
                if (!action.HasLabel) return "action \"" + action.Id + "\" has no label";
            }

            if (!string.IsNullOrEmpty(DefaultChoiceId) && FindChoice(DefaultChoiceId) == null)
                return "DefaultChoiceId \"" + DefaultChoiceId + "\" is not one of the choices";
            if (TimeoutDays > 0 && string.IsNullOrEmpty(DefaultChoiceId))
                return "TimeoutDays is above 0 but there is no DefaultChoiceId";
            return null;
        }

        // ---- side file ----

        /// <summary>
        /// The side file form of the record (see the class remarks for the queued case). Empty strings and empty lists are written as null,
        /// the priority as its name.
        /// </summary>
        public MailRecordDto ToDto()
        {
            var dto = new MailRecordDto
            {
                id = MailId,
                feature = NullIfEmpty(FeatureId),
                tag = NullIfEmpty(Tag),
                titleKey = NullIfEmpty(TitleKey),
                bodyKey = NullIfEmpty(BodyKey),
                title = LocTextToDto(Title),
                body = LocTextToDto(Body),
                args = Args.Length == 0 ? null : (string[])Args.Clone(),
                sent = FormatDate(SentOn),
                read = Read,
                choice = NullIfEmpty(ChoiceId),
                resolution = NullIfEmpty(Resolution),
                defaultChoice = NullIfEmpty(DefaultChoiceId),
                priority = Priority.ToString(),
                status = NullIfEmpty(StatusLine),
            };

            if (DeliveredOn.HasValue)
            {
                dto.delivered = FormatDate(DeliveredOn.Value);
                dto.deadline = Deadline.HasValue ? FormatDate(Deadline.Value) : null;
            }
            else
            {
                dto.deadline = TimeoutDays > 0 ? FormatDate(SentOn.Date.AddDays(TimeoutDays)) : null;
            }

            if (Choices.Count > 0)
            {
                dto.choices = new List<MailChoiceDto>(Choices.Count);
                foreach (MailChoiceModel choice in Choices)
                {
                    if (choice == null) continue;
                    dto.choices.Add(new MailChoiceDto
                    {
                        id = choice.Id,
                        labelKey = NullIfEmpty(choice.LabelKey),
                        tooltipKey = NullIfEmpty(choice.TooltipKey),
                        disabledReasonKey = NullIfEmpty(choice.DisabledReasonKey),
                        label = LocTextToDto(choice.Label),
                        disabledReasonArgs = choice.DisabledReasonArgs.Length == 0 ? null : (string[])choice.DisabledReasonArgs.Clone(),
                        enabled = choice.Enabled,
                    });
                }
            }

            if (Actions.Count > 0)
            {
                dto.actions = new List<MailActionDto>(Actions.Count);
                foreach (MailActionModel action in Actions)
                {
                    if (action == null) continue;
                    dto.actions.Add(new MailActionDto
                    {
                        id = action.Id,
                        labelKey = NullIfEmpty(action.LabelKey),
                        tooltipKey = NullIfEmpty(action.TooltipKey),
                        disabledReasonKey = NullIfEmpty(action.DisabledReasonKey),
                        label = LocTextToDto(action.Label),
                        disabledReasonArgs = action.DisabledReasonArgs.Length == 0 ? null : (string[])action.DisabledReasonArgs.Clone(),
                        enabled = action.Enabled,
                        repeatable = action.Repeatable,
                        used = action.Used,
                    });
                }
            }

            return dto;
        }

        /// <summary>
        /// Reads one record of the side file. <paramref name="queued"/> tells which list it came from (<c>queue</c> or <c>mails</c>).
        /// Returns null, after a warning, for a record that violates the value rules: id below <see cref="FirstId"/>, no feature, a missing or
        /// malformed date (<c>yyyy-MM-dd</c>), no <c>delivered</c> in <c>mails</c>, a <c>delivered</c> in <c>queue</c>. Softer problems are
        /// repaired with a warning: an unknown priority becomes Normal, an unknown resolution Pending, a choice or action without an id (or
        /// with the id of an earlier one) is skipped.
        /// </summary>
        public static MailRecordModel FromDto(MailRecordDto dto, bool queued, Action<string> warn)
        {
            if (dto == null)
            {
                Warn(warn, "a null record was dropped");
                return null;
            }

            string who = "record " + dto.id.ToString(CultureInfo.InvariantCulture);
            if (dto.id < FirstId)
            {
                Warn(warn, who + " dropped: id is below " + FirstId.ToString(CultureInfo.InvariantCulture));
                return null;
            }
            if (string.IsNullOrEmpty(dto.feature))
            {
                Warn(warn, who + " dropped: no feature");
                return null;
            }

            DateTime sent;
            if (!TryParseDate(dto.sent, out sent))
            {
                Warn(warn, who + " dropped: 'sent' is not a date yyyy-MM-dd");
                return null;
            }

            DateTime? delivered = null;
            if (queued)
            {
                if (!string.IsNullOrEmpty(dto.delivered))
                {
                    Warn(warn, who + " dropped: a queued mail has a 'delivered' date");
                    return null;
                }
            }
            else
            {
                DateTime when;
                if (!TryParseDate(dto.delivered, out when))
                {
                    Warn(warn, who + " dropped: 'delivered' is missing or not a date yyyy-MM-dd");
                    return null;
                }
                delivered = when;
            }

            DateTime? deadline = null;
            if (!string.IsNullOrEmpty(dto.deadline))
            {
                DateTime when;
                if (!TryParseDate(dto.deadline, out when))
                {
                    Warn(warn, who + " dropped: 'deadline' is not a date yyyy-MM-dd");
                    return null;
                }
                deadline = when;
            }

            var model = new MailRecordModel
            {
                MailId = dto.id,
                FeatureId = dto.feature,
                Tag = NullIfEmpty(dto.tag),
                TitleKey = NullIfEmpty(dto.titleKey),
                BodyKey = NullIfEmpty(dto.bodyKey),
                Title = LocTextFromDto(dto.title),
                Body = LocTextFromDto(dto.body),
                Args = dto.args == null ? null : (string[])dto.args.Clone(),
                SentOn = sent,
                DeliveredOn = delivered,
                Read = dto.read,
                ChoiceId = NullIfEmpty(dto.choice),
                DefaultChoiceId = NullIfEmpty(dto.defaultChoice),
                StatusLine = NullIfEmpty(dto.status),
            };

            model.Priority = ReadPriority(dto.priority, who, warn);
            model.Resolution = ReadResolution(dto.resolution, who, warn);

            // Delivered: the deadline is stored as is, the timeout is its distance from the delivery day.
            // Queued: the deadline is the provisional sent + timeout; the real one is computed at delivery.
            if (deadline.HasValue)
            {
                DateTime origin = delivered.HasValue ? delivered.Value : sent;
                double days = (deadline.Value - origin).TotalDays;
                if (days < 0)
                {
                    Warn(warn, who + ": 'deadline' is before the " + (delivered.HasValue ? "delivery" : "sending") + " day, ignored");
                }
                else
                {
                    model.TimeoutDays = days > MaxTimeoutDays ? MaxTimeoutDays : (int)days;
                    if (delivered.HasValue) model.Deadline = deadline;
                }
            }

            ReadChoices(dto.choices, model, who, warn);
            ReadActions(dto.actions, model, who, warn);
            return model;
        }

        // ---- internals ----

        private static void ReadChoices(List<MailChoiceDto> source, MailRecordModel model, string who, Action<string> warn)
        {
            if (source == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                MailChoiceDto item = source[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    Warn(warn, who + ": choice #" + i.ToString(CultureInfo.InvariantCulture) + " has no id, skipped");
                    continue;
                }
                if (!seen.Add(item.id))
                {
                    Warn(warn, who + ": choice id \"" + item.id + "\" is used twice, the second one skipped");
                    continue;
                }
                model.Choices.Add(new MailChoiceModel
                {
                    Id = item.id,
                    LabelKey = NullIfEmpty(item.labelKey),
                    Label = LocTextFromDto(item.label),
                    TooltipKey = NullIfEmpty(item.tooltipKey),
                    Enabled = item.enabled,
                    DisabledReasonKey = NullIfEmpty(item.disabledReasonKey),
                    DisabledReasonArgs = item.disabledReasonArgs == null ? null : (string[])item.disabledReasonArgs.Clone(),
                });
            }
        }

        private static void ReadActions(List<MailActionDto> source, MailRecordModel model, string who, Action<string> warn)
        {
            if (source == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                MailActionDto item = source[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    Warn(warn, who + ": action #" + i.ToString(CultureInfo.InvariantCulture) + " has no id, skipped");
                    continue;
                }
                if (!seen.Add(item.id))
                {
                    Warn(warn, who + ": action id \"" + item.id + "\" is used twice, the second one skipped");
                    continue;
                }
                model.Actions.Add(new MailActionModel
                {
                    Id = item.id,
                    LabelKey = NullIfEmpty(item.labelKey),
                    Label = LocTextFromDto(item.label),
                    TooltipKey = NullIfEmpty(item.tooltipKey),
                    Enabled = item.enabled,
                    DisabledReasonKey = NullIfEmpty(item.disabledReasonKey),
                    DisabledReasonArgs = item.disabledReasonArgs == null ? null : (string[])item.disabledReasonArgs.Clone(),
                    Repeatable = item.repeatable,
                    Used = item.used,
                });
            }
        }

        private static MailPriority ReadPriority(string text, string who, Action<string> warn)
        {
            if (string.IsNullOrEmpty(text)) return MailPriority.Normal;
            if (string.Equals(text, "Low", StringComparison.OrdinalIgnoreCase)) return MailPriority.Low;
            if (string.Equals(text, "Normal", StringComparison.OrdinalIgnoreCase)) return MailPriority.Normal;
            if (string.Equals(text, "Critical", StringComparison.OrdinalIgnoreCase)) return MailPriority.Critical;
            Warn(warn, who + ": priority \"" + text + "\" is not Low, Normal or Critical, Normal used");
            return MailPriority.Normal;
        }

        private static string ReadResolution(string text, string who, Action<string> warn)
        {
            if (string.IsNullOrEmpty(text)) return ResolutionPending;
            if (string.Equals(text, ResolutionPending, StringComparison.OrdinalIgnoreCase)) return ResolutionPending;
            if (string.Equals(text, ResolutionPlayer, StringComparison.OrdinalIgnoreCase)) return ResolutionPlayer;
            if (string.Equals(text, ResolutionTimeout, StringComparison.OrdinalIgnoreCase)) return ResolutionTimeout;
            if (string.Equals(text, ResolutionProgrammatic, StringComparison.OrdinalIgnoreCase)) return ResolutionProgrammatic;
            Warn(warn, who + ": resolution \"" + text + "\" is unknown, Pending used");
            return ResolutionPending;
        }

        private static bool HasText(string key, LocText text)
        {
            return !string.IsNullOrEmpty(key) || (text != null && !text.IsEmpty);
        }

        private static LocTextDto LocTextToDto(LocText text)
        {
            if (text == null || text.IsEmpty) return null;
            return new LocTextDto { key = NullIfEmpty(text.Key), ru = NullIfEmpty(text.Ru), en = NullIfEmpty(text.En) };
        }

        private static LocText LocTextFromDto(LocTextDto dto)
        {
            if (dto == null) return null;
            var text = new LocText(NullIfEmpty(dto.key), NullIfEmpty(dto.ru), NullIfEmpty(dto.en));
            return text.IsEmpty ? null : text;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string FormatDate(DateTime date)
        {
            return date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            date = default(DateTime);
            return !string.IsNullOrEmpty(text)
                   && DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static void Warn(Action<string> sink, string message)
        {
            if (sink != null) sink("mail: " + message);
        }
    }
}
