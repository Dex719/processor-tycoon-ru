using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>A pop-up notification: a key of the string tables and its arguments, already written (<c>ModNotify.ShowKey(feature, Key, Args)</c>).</summary>
    public sealed class Notice
    {
        public string Key;
        public string[] Args = new string[0];
    }

    /// <summary>A letter: the tag that tells it from the others, the keys of its title and body and their arguments, already written (<c>ModMailMessage</c>).</summary>
    public sealed class MailPlan
    {
        public string Tag;
        public string TitleKey;
        public string BodyKey;
        public string[] Args = new string[0];
    }

    /// <summary>
    /// What the feature tells the player and how it words it (FR-2, FR-3, FR-6, FR-7; design, Component 7): the notification and the letter of an
    /// announcement, the notification of a launch, of an early adopter, of a socket-change penalty, and the two warnings the release window shows.
    /// Only the arguments are made here (names, percents, months, from the balance); the words are in the string tables. Pure logic, so the
    /// numbers of the texts are checked without the game.
    /// </summary>
    public static class PlatformNotices
    {
        /// <summary>The tag of the letter about an announcement: <c>pg:announce:&lt;genId&gt;</c>.</summary>
        public const string AnnounceTagPrefix = "pg:announce:";

        /// <summary>The tag of the letter to an early adopter: <c>pg:early:&lt;genId&gt;</c>.</summary>
        public const string EarlyTagPrefix = "pg:early:";

        public const string KeyAnnounced = "pg.notify.announced";
        public const string KeyLaunched = "pg.notify.launched";
        public const string KeyEarly = "pg.notify.early";
        public const string KeyChurn = "pg.notify.churn";
        public const string KeyWarnEarly = "pg.warn.early";
        public const string KeyAnnounceTitle = "pg.mail.announce.title";
        public const string KeyAnnounceBody = "pg.mail.announce.body";
        public const string KeyAnnounceBodyObsolete = "pg.mail.announce.bodyObsolete";
        public const string KeyEarlyTitle = "pg.mail.early.title";
        public const string KeyEarlyBody = "pg.mail.early.body";
        public const string KeyEarlyBodyPioneer = "pg.mail.early.bodyPioneer";

        // ---- the announcement (FR-2) ----

        /// <summary>The notification of an announcement: the name of the platform.</summary>
        public static Notice Announced(GenerationDef gen, ViewText text)
        {
            return new Notice { Key = KeyAnnounced, Args = new[] { NameOf(gen, text) } };
        }

        /// <summary>
        /// The letter of an announcement (FR-2 item 2): tag <c>pg:announce:&lt;id&gt;</c>; the sockets, the memory standards, the number of places and
        /// the window of the early adopters, the size and the length of their bonus, and - when the generation has an obsolete year - the year
        /// it goes out of date (a second body key, so the sentence is either there or not). The letter has one list of arguments for its title and
        /// its body: {0} the name of the platform, {1} sockets, {2} memory, {3} places, {4} window in months, {5} bonus in percent, {6} length of the
        /// bonus in months, {7} the obsolete year.
        /// </summary>
        public static MailPlan AnnounceMail(GenerationDef gen, PlatformBalance b, ViewText text)
        {
            PlatformBalance.RatingBalance rating = (b ?? PlatformBalance.Defaults()).Rating;
            bool obsolete = gen != null && gen.obsoleteYear > 0;

            var args = new List<string>
            {
                NameOf(gen, text),
                PlatformNumbers.Join(gen == null ? null : gen.sockets),
                PlatformNumbers.Join(gen == null ? null : gen.memories),
                PlatformNumbers.Int(rating.EarlyAdopterSlots),
                PlatformNumbers.Int(gen == null ? 0 : gen.earlyWindowMonths),
                PlatformNumbers.Percent(rating.EarlyBonus),
                PlatformNumbers.Int(rating.EarlyDurationMonths),
            };
            if (obsolete) args.Add(PlatformNumbers.Int(gen.obsoleteYear));

            return new MailPlan
            {
                Tag = AnnounceTagPrefix + (gen == null ? string.Empty : gen.id),
                TitleKey = KeyAnnounceTitle,
                BodyKey = obsolete ? KeyAnnounceBodyObsolete : KeyAnnounceBody,
                Args = args.ToArray(),
            };
        }

        // ---- the launch (FR-3 item 4) ----

        /// <summary>The notification of a launch: {0} the name of the company that shipped the first CPU, {1} the name of the platform.</summary>
        public static Notice Launched(GenerationDef gen, string companyName, ViewText text)
        {
            return new Notice { Key = KeyLaunched, Args = new[] { companyName ?? string.Empty, NameOf(gen, text) } };
        }

        // ---- the early adopter (FR-3 item 3) ----

        /// <summary>The notification to an early adopter: {0} the name of the platform, {1} the bonus in percent, {2} how many months it lasts.</summary>
        public static Notice EarlyAdopter(GenerationDef gen, PlatformBalance b, ViewText text)
        {
            PlatformBalance.RatingBalance rating = (b ?? PlatformBalance.Defaults()).Rating;
            return new Notice
            {
                Key = KeyEarly,
                Args = new[] { NameOf(gen, text), PlatformNumbers.Percent(rating.EarlyBonus), PlatformNumbers.Int(rating.EarlyDurationMonths) },
            };
        }

        /// <summary>
        /// The letter to an early adopter (FR-3 item 3): tag <c>pg:early:&lt;id&gt;</c>. The pioneer (slot 1) gets a body of its own. One list of
        /// arguments for the title and the body: {0} the name of the platform, {1} the bonus in percent, {2} months it lasts, {3} the place taken,
        /// {4} the places.
        /// </summary>
        public static MailPlan EarlyMail(GenerationDef gen, int slot, PlatformBalance b, ViewText text)
        {
            PlatformBalance.RatingBalance rating = (b ?? PlatformBalance.Defaults()).Rating;
            var args = new List<string>
            {
                NameOf(gen, text),
                PlatformNumbers.Percent(rating.EarlyBonus),
                PlatformNumbers.Int(rating.EarlyDurationMonths),
                PlatformNumbers.Int(slot),
                PlatformNumbers.Int(rating.EarlyAdopterSlots),
            };

            return new MailPlan
            {
                Tag = EarlyTagPrefix + (gen == null ? string.Empty : gen.id),
                TitleKey = KeyEarlyTitle,
                BodyKey = slot == 1 ? KeyEarlyBodyPioneer : KeyEarlyBody,
                Args = args.ToArray(),
            };
        }

        // ---- the socket change (FR-6 item 5, FR-7) ----

        /// <summary>
        /// The notification of the penalty for changing a socket (FR-6 item 5, FR-7 item 1): {0} the penalty in percent, {1} the new socket, {2} the months
        /// it lasts, {3} the main socket that is left, {4} how long the company supported it, {5} how long it should have (<c>minSocketLifeMonths</c>).
        /// <paramref name="penalty"/> is a fraction of the rating (0.067 = 6.7 %).
        /// </summary>
        public static Notice Churn(string socketName, float penalty, int months, string mainSocketName, int mainAgeMonths, int minSocketLifeMonths)
        {
            return new Notice
            {
                Key = KeyChurn,
                Args = new[]
                {
                    PlatformNumbers.Percent(penalty),
                    socketName ?? string.Empty,
                    PlatformNumbers.Int(months),
                    mainSocketName ?? string.Empty,
                    PlatformNumbers.Int(mainAgeMonths),
                    PlatformNumbers.Int(minSocketLifeMonths),
                },
            };
        }

        // ---- the release window (FR-7) ----

        /// <summary>
        /// What the release window warns about when the CPU is about to be released: the penalty for changing the socket (FR-7 item 1) and the early
        /// transition (item 2); none of them - nothing (item 3). <paramref name="outcome"/> is the answer of <see cref="PlatformTimeline.Preview"/>.
        /// </summary>
        public static List<Notice> ReleaseWarnings(ReleaseOutcome outcome, GenerationDef gen, string socketName, string mainSocketName, PlatformBalance b, ViewText text)
        {
            var list = new List<Notice>();
            PlatformBalance.RatingBalance rating = (b ?? PlatformBalance.Defaults()).Rating;

            if (outcome.ChurnSeverity > 0)
            {
                list.Add(Churn(socketName, rating.ChurnPenalty * outcome.ChurnSeverity, rating.ChurnDurationMonths,
                    mainSocketName, outcome.MainAgeMonths, rating.MinSocketLifeMonths));
            }

            if (outcome.EarlyAdopter && gen != null)
            {
                list.Add(new Notice
                {
                    Key = KeyWarnEarly,
                    Args = new[] { NameOf(gen, text), PlatformNumbers.Percent(rating.EarlyBonus), PlatformNumbers.Int(rating.EarlyDurationMonths) },
                });
            }
            return list;
        }

        private static string NameOf(GenerationDef gen, ViewText text)
        {
            if (text != null) return text.Gen(gen);
            return gen == null ? string.Empty : gen.id;
        }
    }
}
