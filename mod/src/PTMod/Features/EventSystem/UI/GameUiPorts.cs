using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary><see cref="IChoiceWorld"/> over the running game: the companies of <see cref="CompanyDirectory"/>, their money and default threshold through <see cref="GameFacade"/>.</summary>
    internal sealed class GameChoiceWorld : IChoiceWorld
    {
        private readonly GameFacade _game;
        private readonly CompanyDirectory _directory;

        public GameChoiceWorld(GameFacade game, CompanyDirectory directory)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (directory == null) throw new ArgumentNullException("directory");
            _game = game;
            _directory = directory;
        }

        public bool InBatch
        {
            get { return _game.InBatch; }
        }

        /// <summary>The company is not bankrupt. While the game has no companies at all (a scene that is still loading) nobody is called gone: a waiting choice is not cancelled by an empty list.</summary>
        public bool IsAlive(string companyKey)
        {
            return _directory.All.Count == 0 || _directory.IsAlive(companyKey);
        }

        public float Money(string companyKey)
        {
            ICompany company = _directory.Resolve(companyKey);
            return company == null ? 0f : _game.Money(company);
        }

        public float DefaultThreshold(string companyKey)
        {
            ICompany company = _directory.Resolve(companyKey);
            return company == null ? 0f : _game.DefaultThreshold(company);
        }

        public bool Check(ICondition condition, string companyKey)
        {
            return _directory.Check(condition, companyKey);
        }
    }

    /// <summary><see cref="ITextSource"/> over <c>ModL10n</c>, the date format of the game and the names of the companies.</summary>
    internal sealed class GameTextSource : ITextSource
    {
        private static readonly string[] NoArgs = new string[0];

        private readonly CompanyDirectory _directory;

        public GameTextSource(CompanyDirectory directory)
        {
            _directory = directory;
        }

        public string L(string key, params string[] args)
        {
            return ModL10n.L(key, (object[])(args ?? NoArgs));
        }

        public string Find(string key)
        {
            return !string.IsNullOrEmpty(key) && ModL10n.Has(key) ? ModL10n.L(key) : null;
        }

        public string Resolve(LocText text, string[] args)
        {
            return text == null ? string.Empty : ModL10n.Resolve(text, (object[])(args ?? NoArgs));
        }

        public string Date(int day)
        {
            try
            {
                return ProcessorTycoon.StringFormatter.DateToString(EventDays.ToDate(day));
            }
            catch (Exception)
            {
                return EventDays.ToDate(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public string Company(string companyKey)
        {
            try
            {
                ICompany company = _directory.Resolve(companyKey);
                string name = company != null ? company.Name : null;
                return string.IsNullOrEmpty(name) ? companyKey : name;
            }
            catch (Exception)
            {
                return companyKey;
            }
        }
    }

    /// <summary><see cref="IMailPort"/> over <c>ModMail</c>: the letters of the engine are the mails of the feature <c>event-system</c> with the tag <c>es:&lt;instanceId&gt;</c>.</summary>
    internal sealed class GameMailPort : IMailPort
    {
        private const string FeatureId = EventSystemApi.FeatureId;

        public int Send(EventMailPlan plan)
        {
            if (plan == null) return -1;

            var message = new ModMailMessage
            {
                FeatureId = FeatureId,
                Tag = plan.Tag,
                TitleKey = plan.TitleKey,
                BodyKey = plan.BodyKey,
                Args = plan.Args,
                Priority = plan.Priority,
                TimeoutDays = 0,
            };
            if (plan.Choices != null && plan.Choices.Count > 0)
            {
                message.Choices = new List<ModMailChoice>();
                foreach (MailChoicePlan choice in plan.Choices)
                {
                    var button = new ModMailChoice
                    {
                        Id = choice.Id,
                        Enabled = choice.Enabled,
                        DisabledReasonKey = choice.Enabled ? null : choice.ReasonKey,
                        DisabledReasonArgs = choice.Enabled ? null : choice.ReasonArgs,
                    };
                    if (!string.IsNullOrEmpty(choice.LabelKey)) button.LabelKey = choice.LabelKey;
                    else button.Label = LocText.Of(choice.Id, choice.Id);
                    message.Choices.Add(button);
                }
            }
            return ModMail.Send(message);
        }

        public bool Resolve(int mailId, string choiceId)
        {
            return ModMail.Resolve(mailId, choiceId);
        }

        public void SetStatusLine(int mailId, string text)
        {
            ModMail.SetStatusLine(mailId, text);
        }

        public void SetChoiceState(int mailId, string choiceId, bool enabled, string reasonKey, string[] reasonArgs)
        {
            ModMail.SetChoiceState(mailId, choiceId, enabled, enabled ? null : reasonKey, reasonArgs ?? new string[0]);
        }

        public void Refresh(int mailId)
        {
            ModMail.Refresh(mailId);
        }

        public MailInfo Find(string tag)
        {
            ModMailRecord record = ModMail.Find(FeatureId, tag);
            if (record == null) return null;
            return new MailInfo
            {
                MailId = record.MailId,
                Delivered = record.IsDelivered,
                DeliveredDay = record.DeliveredOn.HasValue ? EventDays.FromDate(record.DeliveredOn.Value) : 0,
            };
        }
    }

    /// <summary><see cref="INotifyPort"/> over <c>ModNotify</c>.</summary>
    internal sealed class GameNotifyPort : INotifyPort
    {
        public void Show(string text, string secondary)
        {
            ModNotify.Show(EventSystemApi.FeatureId, text, string.IsNullOrEmpty(secondary) ? null : secondary);
        }
    }

    /// <summary><see cref="IUiSettings"/> over the parameters of the feature in the hub.</summary>
    internal sealed class GameUiSettings : IUiSettings
    {
        public bool PauseAllChoices
        {
            get { return ModSettings.GetBool(EventSystemApi.FeatureId, EventParams.PauseAllChoices); }
        }

        public bool RivalNews
        {
            get { return ModSettings.GetBool(EventSystemApi.FeatureId, EventParams.RivalNews); }
        }
    }
}
