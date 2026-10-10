using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Mail;
using GameEmail = ProcessorTycoon.Desktop.Email.Email;
using GameEmailButton = ProcessorTycoon.Desktop.Email.EmailButton;
using GameEmailWindow = ProcessorTycoon.Desktop.Email.EmailWindow;

namespace PTMod.Core
{
    /// <summary>
    /// Puts the game's mail list in date order after the letters of the mod came back from a save (FR-14 item 11, AC-14.10, D9). The game's
    /// <c>EmailWindow.InstantiateButton</c> makes every new row the first child of <c>emailButtonsParent</c>, so after
    /// <see cref="ModMail.RestoreInbox"/> put the saved mod letters in (oldest first) they sit above everything, the game's own "Year Summary" rows
    /// included. The sorter reads every row's <c>EmailButton.Email</c>, takes <c>Email.Date</c> and <c>Email.EmailID</c>, lets the pure
    /// <see cref="InboxOrder"/> say what order is right (newest date first; for equal dates the larger id above, so a mod letter is above a vanilla one
    /// of its day) and sets the sibling indices (<see cref="InboxSortPlan"/> turns the order into the moves).
    /// <list type="bullet">
    /// <item><b>When.</b> On <see cref="ModMail.InboxRestored"/>, which is raised only when letters were put back. A mail delivered in the middle of a game
    /// needs no sorting: it is dated with today and the game puts it first. A game without mod mail is left as the game made it.</item>
    /// <item><b>What it touches.</b> Only the order of the rows (<c>Transform.SetSiblingIndex</c>); the game's own list of letters (the one saved in
    /// the game's file, in order of arrival) is not changed, so the order is made again after every load. A list that is in order already is not
    /// touched. A row without a readable date goes to the end of the list.</item>
    /// <item><b>The private field.</b> <c>emailButtonsParent</c> is found with Harmony's <c>AccessTools</c>; the self-check of the core checks that
    /// it exists. When it does not (another game version) the error is logged once and the list stays as the game made it for the rest of the
    /// session.</item>
    /// </list>
    /// The game's classes are touched only in the small methods at the end (no inlining), so a mismatch fails inside them, in a caller's <c>try</c>.
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    internal static class InboxSorter
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string SortSite = "InboxSorter/sort";
        private const string ParentField = "emailButtonsParent";

        private static FieldInfo _parentField;   // EmailWindow.emailButtonsParent
        private static bool _broken;             // the game's classes do not match: no sorting for the session
        private static bool _orderWarned;        // the list was not in order after the sort: written once

        /// <summary>One row of the game's mail list as the sorter reads it.</summary>
        private struct Row
        {
            public UnityEngine.Transform Transform;
            public int Sibling;      // index among the children of the parent
            public DateTime Date;
            public int Id;
        }

        /// <summary>Rows moved by the last sort (the Diagnostics tab and the harness read it); -1 before the first one.</summary>
        internal static int LastMoved { get; private set; } = -1;

        /// <summary>The sorter gave up for this session: the game's mail window does not match the mod.</summary>
        internal static bool IsBroken
        {
            get { return _broken; }
        }

        /// <summary>Declare stage: the list is put in order whenever the letters of a save come back.</summary>
        [CoreInit(CoreInitStage.Declare, 120, Optional = true)]
        internal static void DeclareSorter()
        {
            ModMail.InboxRestored += OnInboxRestored;
        }

        private static void OnInboxRestored()
        {
            ModHooks.Guard(CoreId, SortSite, () => Sort());
        }

        /// <summary>
        /// Puts the rows of the game's mail list in order. Returns the number of rows that changed their place; 0 when the list was in order or
        /// is not there (the mail window is missing, the field is not found). Never throws.
        /// </summary>
        internal static int Sort()
        {
            if (_broken) return 0;

            int moved = 0;
            ModHooks.Guard(CoreId, SortSite, () => { moved = SortCore(); });
            return moved;
        }

        private static int SortCore()
        {
            var rows = new List<Row>();
            try
            {
                if (!ReadRows(rows))
                {
                    ModLog.Debug(CoreId, "inbox: the mail list is not there, nothing to sort");
                    return 0;
                }
            }
            catch (Exception ex) when (IsMismatch(ex))
            {
                Broken(ex);
                return 0;
            }

            if (rows.Count < 2) return 0;

            IReadOnlyList<int> siblings, ids;
            IReadOnlyList<DateTime> dates;
            Split(rows, out siblings, out dates, out ids);

            InboxMove[] moves = InboxSortPlan.Compute(siblings, dates, ids);
            if (moves.Length == 0)
            {
                LastMoved = 0;
                ModLog.Debug(CoreId, "inbox: " + rows.Count.ToString(CultureInfo.InvariantCulture) + " row(s), already in date order");
                return 0;
            }

            Apply(rows, moves);
            int changed = CountChanged(rows.Count, moves);
            LastMoved = changed;
            ModLog.Debug(CoreId, "inbox: " + rows.Count.ToString(CultureInfo.InvariantCulture) + " row(s) put in date order, " + changed.ToString(CultureInfo.InvariantCulture) + " moved");

            try
            {
                VerifyOrder();
            }
            catch (Exception ex)
            {
                // The sort is done; a check that cannot read the list again says nothing against it.
                ModLog.Debug(CoreId, "inbox: the order could not be checked after sorting (" + ex.GetType().Name + ")");
            }
            return changed;
        }

        /// <summary>Reads the list again after the moves: it must be in order now; if it is not, that is a bug of the sorter or a foreign child in the list, said once.</summary>
        private static void VerifyOrder()
        {
            if (_orderWarned) return;

            var rows = new List<Row>();
            if (!ReadRows(rows)) return;

            IReadOnlyList<int> siblings, ids;
            IReadOnlyList<DateTime> dates;
            Split(rows, out siblings, out dates, out ids);
            if (InboxOrder.IsOrdered(dates, ids)) return;

            _orderWarned = true;
            ModLog.Warn(CoreId, "inbox: the mail list is still not in date order after sorting (" + rows.Count.ToString(CultureInfo.InvariantCulture) + " rows)");
        }

        private static void Split(List<Row> rows, out IReadOnlyList<int> siblings, out IReadOnlyList<DateTime> dates, out IReadOnlyList<int> ids)
        {
            var s = new int[rows.Count];
            var d = new DateTime[rows.Count];
            var i = new int[rows.Count];
            for (int n = 0; n < rows.Count; n++)
            {
                s[n] = rows[n].Sibling;
                d[n] = rows[n].Date;
                i[n] = rows[n].Id;
            }
            siblings = s;
            dates = d;
            ids = i;
        }

        /// <summary>
        /// Rows that end up at another place among the rows than they had. The moves are the places from <c>rows.Count - moves.Length</c> on
        /// (the rows in front are in order and not touched); the k-th move fills place <c>start + k</c> with the row of that name, so a row whose number
        /// is not its place has moved.
        /// </summary>
        private static int CountChanged(int rowCount, InboxMove[] moves)
        {
            int changed = 0;
            int start = rowCount - moves.Length;
            for (int k = 0; k < moves.Length; k++)
            {
                if (moves[k].Row != start + k) changed++;
            }
            return changed;
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT or the lookup fails in the method and the caller catches it ----

        /// <summary>Reads the rows of the list in the order of its children; false when the mail window or its list is not there.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadRows(List<Row> rows)
        {
            GameEmailWindow window = GameEmailWindow.Instance;
            if (window == null) return false;

            var parent = (UnityEngine.GameObject)ParentFieldInfo().GetValue(window);
            if (parent == null) return false;

            UnityEngine.Transform root = parent.transform;
            int count = root.childCount;
            for (int index = 0; index < count; index++)
            {
                UnityEngine.Transform child = root.GetChild(index);
                GameEmailButton button = child.GetComponent<GameEmailButton>();
                if (button == null) continue;   // not a letter row (a Unity null check)

                GameEmail email = button.Email;
                if (email == null) continue;

                rows.Add(new Row { Transform = child, Sibling = index, Date = DateOf(email), Id = email.EmailID });
            }
            return true;
        }

        /// <summary>The date of the letter; <c>DateTime.MinValue</c> (the end of the list) for a date the game itself could not show.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static DateTime DateOf(GameEmail email)
        {
            GameEmail.EventDate date = email.Date;
            if (date == null) return DateTime.MinValue;

            try
            {
                return new DateTime(date.Year, date.Month, date.Day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Apply(List<Row> rows, InboxMove[] moves)
        {
            for (int k = 0; k < moves.Length; k++)
            {
                rows[moves[k].Row].Transform.SetSiblingIndex(moves[k].Slot);
            }
        }

        private static FieldInfo ParentFieldInfo()
        {
            FieldInfo field = _parentField;
            if (field != null) return field;

            field = HarmonyLib.AccessTools.Field(typeof(GameEmailWindow), ParentField);
            if (field == null) throw new MissingFieldException(typeof(GameEmailWindow).FullName, ParentField);
            _parentField = field;
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
            ModLog.Error(CoreId, "inbox: the game's EmailWindow, EmailButton or Email does not match the mod (another game version?); the mail list stays as the game made it", ex);
        }
    }
}
