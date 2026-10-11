using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The texts the player reads, as the UI asks for them: the string tables in the current language (<c>ModL10n</c> in the game, tables loaded from the files
    /// in the tests) and the game's own way of writing dates and company names. Every member answers a printable string and never throws.
    /// </summary>
    public interface ITextSource
    {
        /// <summary>The text of a key with <c>{0}..{n}</c> filled in; <c>[key]</c> when no language has it.</summary>
        string L(string key, params string[] args);

        /// <summary>The text of a key in the current language, or null when no table has it (the words of a requirement).</summary>
        string Find(string key);

        /// <summary>A text of the content (<c>title</c>, <c>text</c>, <c>cause</c> of an event) with the positional arguments of the instance filled in; empty for a null text.</summary>
        string Resolve(LocText text, string[] args);

        /// <summary>A game day (days from 1971-01-01) as the game writes dates.</summary>
        string Date(int day);

        /// <summary>The name of a company for the player; the key itself for one that is gone.</summary>
        string Company(string companyKey);
    }

    /// <summary>One choice button of a letter.</summary>
    public sealed class MailChoicePlan
    {
        public string Id;
        public string LabelKey;
        public bool Enabled = true;
        public string ReasonKey;
        public string[] ReasonArgs = new string[0];
    }

    /// <summary>What goes into <c>ModMail.Send</c>: the tag <c>es:&lt;instanceId&gt;</c>, the priority, the keys of the title and the body with their shared arguments and the buttons.</summary>
    public sealed class EventMailPlan
    {
        public string Tag;
        public MailPriority Priority = MailPriority.Normal;
        public string TitleKey;
        public string BodyKey;

        /// <summary>The arguments of both texts, already formatted: <c>{0}</c> is the title of the event, <c>{1}</c> the whole body.</summary>
        public string[] Args = new string[0];

        /// <summary>The buttons; empty - a letter that only informs.</summary>
        public List<MailChoicePlan> Choices = new List<MailChoicePlan>();
    }

    /// <summary>What the mail service knows about a letter of the engine.</summary>
    public sealed class MailInfo
    {
        public int MailId;

        /// <summary>The letter reached the inbox.</summary>
        public bool Delivered;

        /// <summary>The game day it was delivered (valid when <see cref="Delivered"/>).</summary>
        public int DeliveredDay;
    }

    /// <summary><c>ModMail</c> as the engine uses it (FR-7; the game side calls the core, the tests record).</summary>
    public interface IMailPort
    {
        /// <summary>Sends the letter; its id (100000 and above), or -1 when the mail service did not accept it.</summary>
        int Send(EventMailPlan plan);

        /// <summary>Closes the letter with the answer the engine applied (<c>ModMail.Resolve</c>, no handler is called); false when it is unknown or answered already.</summary>
        bool Resolve(int mailId, string choiceId);

        /// <summary>The line under the text of the letter (<c>ModMail.SetStatusLine</c>); null removes it.</summary>
        void SetStatusLine(int mailId, string text);

        /// <summary>A button on or off with the reason (<c>ModMail.SetChoiceState</c>).</summary>
        void SetChoiceState(int mailId, string choiceId, bool enabled, string reasonKey, string[] reasonArgs);

        /// <summary>Draws the open letter again (<c>ModMail.Refresh</c>).</summary>
        void Refresh(int mailId);

        /// <summary>The letter with this tag, or null when there is none.</summary>
        MailInfo Find(string tag);
    }

    /// <summary><c>ModNotify</c>: a short pop-up of the game's style.</summary>
    public interface INotifyPort
    {
        void Show(string text, string secondary);
    }

    /// <summary>The modal window of one waiting choice (design.md, <c>EventChoiceWindow</c>). The game side draws it; the engine decides when it shows and what it says.</summary>
    public interface IChoiceWindowPort
    {
        /// <summary>The window is on the screen.</summary>
        bool IsOpen { get; }

        /// <summary>Opens the window with this content (or draws a window that is open anew).</summary>
        void Show(ChoiceWindowModel model);

        /// <summary>Closes the window without telling the engine (the instance was answered elsewhere).</summary>
        void Hide();
    }

    /// <summary>The parameters of the feature the UI depends on (FR-11 item 1).</summary>
    public interface IUiSettings
    {
        /// <summary>A window with a pause for every event with a choice, not only for the important ones.</summary>
        bool PauseAllChoices { get; }

        /// <summary>News about the events of rival companies.</summary>
        bool RivalNews { get; }
    }
}
