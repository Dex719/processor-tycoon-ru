using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Log;
using PTMod.Core.Logic.Mail;
using GameEmail = ProcessorTycoon.Desktop.Email.Email;
using GameEmailButton = ProcessorTycoon.Desktop.Email.EmailButton;
using GameEmailMessage = ProcessorTycoon.Desktop.Email.EmailMessage;
using GameEmailWindow = ProcessorTycoon.Desktop.Email.EmailWindow;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Core
{
    /// <summary>
    /// The game's inbox as <see cref="MailFactory"/> uses it: put a letter in, read whether it was opened, redraw its text, let it go. The real
    /// one is <see cref="GameInbox"/>; an offline harness gives a stand-in that needs no Unity. A letter is an opaque handle that only the inbox
    /// that made it understands.
    /// </summary>
    internal interface IMailInbox
    {
        /// <summary>The window and the player's company exist, so a letter can be put in; quiet false while the scene is not up and after a mismatch with the game.</summary>
        bool IsReady { get; }

        /// <summary>Makes a letter with this id, date, reading state and text and loads it into the window. Throws when it cannot.</summary>
        object Add(int mailId, DateTime date, bool read, string title, string text);

        /// <summary>Whether the letter was opened; <paramref name="fallback"/> when the letter cannot tell.</summary>
        bool IsRead(object handle, bool fallback);

        /// <summary>New title and text for a letter that is in the window (a change of language), including the list row and the open letter.</summary>
        void Retext(object handle, string title, string text);

        /// <summary>The letter is no longer used (its scene is being left): its helper objects are released.</summary>
        void Release(object handle);
    }

    /// <summary>
    /// Makes the letters of the mod mail (FR-14 item 2, 11, 14): the text of a record in the current language, the game's <c>Email</c> with the
    /// date of delivery, and the list of the letters that are in the window now. A letter is made like this, as design.md "Реализация письма"
    /// says: a <c>ScriptableObject.CreateInstance&lt;EmailMessage&gt;()</c> whose title and text are set through the compiler's backing fields (their
    /// setters are private), then <c>new Email().Initialize(message, id, false)</c>, then the date (<c>Email.Date</c> has a private setter, so its
    /// backing field), the reading state and <c>EmailWindow.LoadEmail</c>. The game formats only the letter with id 0, so the text of a mod mail is
    /// shown as it is, and the game's own theme change (<c>FormatTextAgain</c>) takes it back from the message.
    /// <list type="bullet">
    /// <item>The letters are tied to a scene: the table lives until the scene is left, then the messages are destroyed and the table is dropped;
    /// the next scene gets its letters from the store (<see cref="ModMail.RestoreInbox"/>).</item>
    /// <item>The game's classes are touched only in <see cref="GameInbox"/>'s methods. When they do not match the game (a member is missing) the
    /// error is logged once and the inbox is not ready for the rest of the session: mail stays in the queue instead of failing every day.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class MailFactory
    {
        private const string CoreId = ModLog.CoreFeatureId;

        private static readonly Dictionary<int, object> Live = new Dictionary<int, object>();

        /// <summary>The inbox in use: the game's; the offline harness replaces it.</summary>
        internal static IMailInbox Inbox = GameInbox.Instance;

        /// <summary>The inbox can take a letter now.</summary>
        internal static bool IsReady
        {
            get { return Inbox.IsReady; }
        }

        /// <summary>Letters of the mod that are in the window now.</summary>
        internal static int ShownCount
        {
            get { return Live.Count; }
        }

        /// <summary>The mail is a letter of the window now.</summary>
        internal static bool IsShown(int mailId)
        {
            return Live.ContainsKey(mailId);
        }

        // ---- text ----

        /// <summary>The title of the mail in the current language: <c>TitleKey</c> with the arguments, else the inline text.</summary>
        internal static string TitleOf(MailRecordModel record)
        {
            return TextOf(record.TitleKey, record.Title, record.Args);
        }

        /// <summary>The body of the mail in the current language.</summary>
        internal static string BodyOf(MailRecordModel record)
        {
            return TextOf(record.BodyKey, record.Body, record.Args);
        }

        /// <summary>
        /// A key wins when the tables have it (or when there is no inline text to fall back on, which shows <c>[key]</c> and a warning, the
        /// mod's usual way to say a key is missing); otherwise the inline text in the current language. Arguments are already formatted strings.
        /// </summary>
        internal static string TextOf(string key, LocText inline, string[] args)
        {
            bool hasInline = inline != null && !inline.IsEmpty;
            bool hasArgs = args != null && args.Length > 0;

            if (!string.IsNullOrEmpty(key) && (!hasInline || ModL10n.Has(key)))
                return hasArgs ? ModL10n.L(key, (object[])args) : ModL10n.L(key);
            if (hasInline) return hasArgs ? ModL10n.Resolve(inline, (object[])args) : ModL10n.Resolve(inline);
            return string.Empty;
        }

        // ---- letters ----

        /// <summary>
        /// Makes the delivered mail a letter of the window, dated with the day of delivery (the date of sending for a record that has none), with
        /// its reading state. Does nothing for a mail that already is one. Throws what the inbox throws; the caller logs it.
        /// </summary>
        internal static void Show(MailRecordModel record)
        {
            if (Live.ContainsKey(record.MailId)) return;

            DateTime date = record.DeliveredOn ?? record.SentOn;
            object handle = Inbox.Add(record.MailId, date, record.Read, TitleOf(record), BodyOf(record));
            Live[record.MailId] = handle;
        }

        /// <summary>Whether the player opened the letter of this mail (the record's own flag when the mail is not a letter now).</summary>
        internal static bool IsRead(MailRecordModel record)
        {
            object handle;
            if (!Live.TryGetValue(record.MailId, out handle)) return record.Read;
            return ReadOf(handle, record.Read);
        }

        /// <summary>Copies the reading state of every letter into its record, so that the side file keeps what the player has read.</summary>
        internal static void SyncRead(MailQueue queue)
        {
            if (Live.Count == 0) return;

            foreach (KeyValuePair<int, object> pair in Live)
            {
                MailRecordModel record = queue.Get(pair.Key);
                if (record != null) record.Read = ReadOf(pair.Value, record.Read);
            }
        }

        /// <summary>Draws the text of every letter again in the current language (title, list row, the open letter). Returns the number redrawn.</summary>
        internal static int RetextAll(MailQueue queue)
        {
            if (Live.Count == 0) return 0;

            int redrawn = 0;
            foreach (KeyValuePair<int, object> pair in new List<KeyValuePair<int, object>>(Live))
            {
                MailRecordModel record = queue.Get(pair.Key);
                if (record == null) continue;

                try
                {
                    Inbox.Retext(pair.Value, TitleOf(record), BodyOf(record));
                    redrawn++;
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "mail: cannot redraw the text of mail #" + record.MailId.ToString(CultureInfo.InvariantCulture) + " of \"" + record.FeatureId + "\"", ex);
                }
            }
            return redrawn;
        }

        /// <summary>The scene is being left: the letters' helper objects go and the table is dropped.</summary>
        internal static void ReleaseAll()
        {
            foreach (object handle in Live.Values)
            {
                try
                {
                    Inbox.Release(handle);
                }
                catch (Exception ex)
                {
                    ModLog.Debug(CoreId, "mail: a letter could not be released: " + LogFormatter.DescribeException(ex));
                }
            }
            Live.Clear();
        }

        /// <summary>The table is dropped without releasing anything (the store was replaced; the old letters belong to a scene that is gone or going).</summary>
        internal static void Forget()
        {
            Live.Clear();
        }

        /// <summary>The game's <c>Email</c> behind the mail, for the parts of the service that work on the open letter; null when the mail is not a letter now.</summary>
        internal static GameEmail EmailOf(int mailId)
        {
            object handle;
            return Live.TryGetValue(mailId, out handle) ? GameInbox.EmailOf(handle) : null;
        }

        private static bool ReadOf(object handle, bool fallback)
        {
            try
            {
                return Inbox.IsRead(handle, fallback);
            }
            catch (Exception ex)
            {
                ModLog.Debug(CoreId, "mail: the reading state of a letter is unknown: " + LogFormatter.DescribeException(ex));
                return fallback;
            }
        }
    }

    /// <summary>
    /// The real inbox: the game's <c>EmailWindow</c>. The game's types are touched in small methods that are never inlined, so that when they do
    /// not match the game the failure happens inside them, in a caller's <c>try</c>. A mismatch (a missing member or type) is logged once and the
    /// inbox stays not ready for the session.
    /// </summary>
    internal sealed class GameInbox : IMailInbox
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static readonly GameInbox Instance = new GameInbox();

        // Private members of the game's classes, found on first use (design.md: the self-check of TSK-042 checks them too).
        private static FieldInfo _messageTitle;
        private static FieldInfo _messageText;
        private static FieldInfo _emailDate;
        private static FieldInfo _windowButtonsParent;
        private static FieldInfo _windowLastEmail;
        private static MethodInfo _windowUpdateEmailText;

        private static bool _broken;

        /// <summary>A letter in the window: the game's <c>Email</c> and the <c>EmailMessage</c> it was made from.</summary>
        private sealed class Letter
        {
            public GameEmail Email;
            public GameEmailMessage Message;
        }

        private GameInbox()
        {
        }

        public bool IsReady
        {
            get
            {
                if (_broken) return false;
                try
                {
                    return WindowAndPlayerExist();
                }
                catch (Exception ex)
                {
                    if (IsMismatch(ex)) Broken(ex);
                    return false;
                }
            }
        }

        public object Add(int mailId, DateTime date, bool read, string title, string text)
        {
            try
            {
                return AddLetter(mailId, date, read, title, text);
            }
            catch (Exception ex)
            {
                if (IsMismatch(ex)) Broken(ex);
                throw;
            }
        }

        public bool IsRead(object handle, bool fallback)
        {
            var letter = handle as Letter;
            return letter == null ? fallback : HasBeenRead(letter);
        }

        public void Retext(object handle, string title, string text)
        {
            var letter = handle as Letter;
            if (letter == null) return;
            RedrawLetter(letter, title, text);
        }

        public void Release(object handle)
        {
            var letter = handle as Letter;
            if (letter == null) return;
            DestroyMessage(letter);
        }

        /// <summary>The game's <c>Email</c> of a handle made by <see cref="Add"/>; null for anything else.</summary>
        internal static GameEmail EmailOf(object handle)
        {
            var letter = handle as Letter;
            return letter == null ? null : letter.Email;
        }

        // ---- the game's classes: touched only below ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool WindowAndPlayerExist()
        {
            GameEmailWindow window = GameEmailWindow.Instance;
            if (window == null) return false;

            GamePlayer player = GamePlayer.Instance;
            return player != null && !ReferenceEquals(player.Company, null);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object AddLetter(int mailId, DateTime date, bool read, string title, string text)
        {
            GameEmailWindow window = GameEmailWindow.Instance;
            if (window == null) throw new InvalidOperationException("EmailWindow is not in the scene");

            FieldInfo titleField = MessageTitleField();
            FieldInfo textField = MessageTextField();
            FieldInfo dateField = EmailDateField();

            GameEmailMessage message = UnityEngine.ScriptableObject.CreateInstance<GameEmailMessage>();
            try
            {
                titleField.SetValue(message, title ?? string.Empty);
                textField.SetValue(message, text ?? string.Empty);

                var email = new GameEmail();
                email.Initialize(message, mailId, false);

                // Dated with the day of delivery whatever the clock says now (a restored letter keeps its old date).
                var eventDate = new GameEmail.EventDate { Year = date.Year, Month = date.Month, Day = date.Day };
                dateField.SetValue(email, eventDate);
                email.HasBeenRead = read;

                window.LoadEmail(email);
                return new Letter { Email = email, Message = message };
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(message);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasBeenRead(Letter letter)
        {
            return letter.Email.HasBeenRead;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RedrawLetter(Letter letter, string title, string text)
        {
            MessageTitleField().SetValue(letter.Message, title ?? string.Empty);
            MessageTextField().SetValue(letter.Message, text ?? string.Empty);
            letter.Email.FormatTextAgain();   // takes the title and text from the message, as the game does on a theme change

            GameEmailWindow window = GameEmailWindow.Instance;
            if (window == null) return;

            // The row in the list shows the title and the date: ask it to read the letter again.
            var parent = (UnityEngine.GameObject)WindowField(ref _windowButtonsParent, "emailButtonsParent").GetValue(window);
            if (parent != null)
            {
                foreach (GameEmailButton button in parent.GetComponentsInChildren<GameEmailButton>(true))
                {
                    if (ReferenceEquals(button.Email, letter.Email)) button.Initialize(letter.Email);
                }
            }

            // The open letter is drawn again too (the game's own handler of a theme change does the same).
            if (ReferenceEquals(WindowField(ref _windowLastEmail, "lastEmail").GetValue(window), letter.Email))
            {
                try
                {
                    WindowUpdateEmailText().Invoke(window, new object[] { letter.Email });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DestroyMessage(Letter letter)
        {
            if (letter.Message != null) UnityEngine.Object.Destroy(letter.Message);
        }

        // ---- reflection on the private members ----

        private static FieldInfo MessageTitleField()
        {
            return _messageTitle ?? (_messageTitle = Field(typeof(GameEmailMessage), "<Title>k__BackingField"));
        }

        private static FieldInfo MessageTextField()
        {
            return _messageText ?? (_messageText = Field(typeof(GameEmailMessage), "<Text>k__BackingField"));
        }

        private static FieldInfo EmailDateField()
        {
            return _emailDate ?? (_emailDate = Field(typeof(GameEmail), "<Date>k__BackingField"));
        }

        private static FieldInfo WindowField(ref FieldInfo cache, string name)
        {
            return cache ?? (cache = Field(typeof(GameEmailWindow), name));
        }

        private static MethodInfo WindowUpdateEmailText()
        {
            if (_windowUpdateEmailText != null) return _windowUpdateEmailText;

            MethodInfo method = typeof(GameEmailWindow).GetMethod("UpdateEmailText", AnyInstance, null, new[] { typeof(GameEmail) }, null);
            if (method == null) throw new MissingMethodException(typeof(GameEmailWindow).FullName, "UpdateEmailText");
            _windowUpdateEmailText = method;
            return method;
        }

        private static FieldInfo Field(Type type, string name)
        {
            FieldInfo field = type.GetField(name, AnyInstance);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        // ---- a mismatch with the game ----

        /// <summary>The game's classes are not what the mod was built against (a member or type is missing or not reachable).</summary>
        private static bool IsMismatch(Exception ex)
        {
            return ex is MissingMemberException || ex is TypeLoadException || ex is MemberAccessException || ex is BadImageFormatException;
        }

        private static void Broken(Exception ex)
        {
            if (_broken) return;
            _broken = true;
            ModLog.Error(CoreId, "mail: the game's EmailWindow, Email or EmailMessage does not match the mod (another game version?); mod mail stays in the queue for this session", ex);
        }
    }
}
