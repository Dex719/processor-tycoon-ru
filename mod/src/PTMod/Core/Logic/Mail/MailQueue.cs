using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using PTMod.Core.Logic.Save;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>
    /// The store and the delivery queue of mod mail (FR-14), without Unity: every mod mail ever sent, in the order it was sent (which is the
    /// order of the ids), where the records that are not delivered yet form the queue. The runtime facade <c>ModMail</c> (TSK-037/062)
    /// builds a <see cref="MailRecordModel"/> from the feature's message, calls <see cref="Enqueue"/>, calls <see cref="DeliverDue"/> on the daily tick
    /// and at the end of a batch run, shows every returned record in the mail window and raises <c>Delivered</c> for it. The queue itself does
    /// not know the game: the date, <c>GameReady</c> and <c>IsBatchRun</c> come from the caller (which also skips the call during a fast-forward),
    /// the limits from the settings.
    /// <list type="bullet">
    /// <item>Delivery order: all <see cref="MailPriority.Critical"/>, then <see cref="MailPriority.Normal"/>, then <see cref="MailPriority.Low"/>; inside a
    /// priority in sending order. Only Normal and Low obey <see cref="MailQuota"/> (3 a day, 2 per feature); a mail held back by its feature's
    /// quota is skipped, the others behind it still go; held mails stay in the queue for the next day. Critical bypasses both limits.</item>
    /// <item>Nothing is delivered, whatever the priority, while the game is not ready or a batch run is in progress.</item>
    /// <item>The deadline is the delivery day + the timeout, not the sending day (AC-14.8).</item>
    /// <item>The queue holds at most <see cref="QueueLimit"/> mails: on overflow the oldest Low are dropped, then the oldest Normal, never Critical
    /// (so a queue of Critical mails alone may exceed the limit). A drop is reported through the warning sink with the feature id.</item>
    /// <item><see cref="Find"/>, <see cref="FindAll"/>, <see cref="Get"/> and <see cref="All"/> see delivered and queued mails alike.</item>
    /// <item><see cref="ToDto"/> and <see cref="Load"/> convert to and from the <c>mail</c> section of the side file (round trip).</item>
    /// </list>
    /// Not thread-safe: main thread only.
    /// </summary>
    public sealed class MailQueue
    {
        /// <summary>The setting <c>mail.queueLimit</c> by default.</summary>
        public const int DefaultQueueLimit = 200;

        /// <summary>Returned by <see cref="Enqueue"/> when the mail was not stored.</summary>
        public const int InvalidId = -1;

        private const int MaxQueueLimit = 10000;

        private static readonly IReadOnlyList<MailRecordModel> NoMails = new MailRecordModel[0];

        private readonly List<MailRecordModel> records = new List<MailRecordModel>();   // ascending MailId
        private readonly ReadOnlyCollection<MailRecordModel> view;
        private readonly Dictionary<int, MailRecordModel> byId = new Dictionary<int, MailRecordModel>();
        private readonly Action<string> warn;
        private int nextId = MailRecordModel.FirstId;
        private int queuedCount;
        private int queueLimit;

        /// <param name="queueLimit">Mails waiting for delivery; clamped to [1, 10000].</param>
        /// <param name="quota">The daily limits; null means the defaults (3 a day, 2 per feature).</param>
        /// <param name="warn">Receives the text of every rejected call, dropped record and overflow (English, one line); null drops the warnings.</param>
        public MailQueue(int queueLimit = DefaultQueueLimit, MailQuota quota = null, Action<string> warn = null)
        {
            this.queueLimit = ClampLimit(queueLimit);
            this.warn = warn;
            Quota = quota ?? new MailQuota();
            view = new ReadOnlyCollection<MailRecordModel>(records);
        }

        /// <summary>The daily limits and counters; change the limits with <see cref="MailQuota.Configure"/>.</summary>
        public MailQuota Quota { get; }

        public int QueueLimit
        {
            get { return queueLimit; }
        }

        /// <summary>The id the next mail gets (what the side file stores as <c>nextId</c>).</summary>
        public int NextId
        {
            get { return nextId; }
        }

        /// <summary>All mails, delivered and queued.</summary>
        public int Count
        {
            get { return records.Count; }
        }

        /// <summary>Mails waiting for delivery.</summary>
        public int QueuedCount
        {
            get { return queuedCount; }
        }

        public int DeliveredCount
        {
            get { return records.Count - queuedCount; }
        }

        /// <summary>Every mail, delivered or queued, in sending order (ascending id). A live read-only view: do not keep it across calls that change the queue.</summary>
        public IReadOnlyList<MailRecordModel> All
        {
            get { return view; }
        }

        /// <summary>Changes the limit of the queue; a queue that is already longer is cut at once (see the class remarks).</summary>
        public void SetQueueLimit(int limit)
        {
            queueLimit = ClampLimit(limit);
            EnforceLimit();
        }

        /// <summary>
        /// Puts a mail into the queue: checks it (<see cref="MailRecordModel.Validate"/>), gives it the next id, stamps <see cref="MailRecordModel.SentOn"/>
        /// with <paramref name="today"/> and drops the surplus if the queue is over its limit. The queue takes over the record: the caller must
        /// not change it afterwards except through the facade. Returns the id, or <see cref="InvalidId"/> when the record was rejected
        /// (a warning says why) or when the overflow rule dropped this very mail (nothing but Critical mails are older than it).
        /// </summary>
        public int Enqueue(MailRecordModel record, DateTime today)
        {
            if (record == null)
            {
                Warn("Enqueue ignored: the mail is null");
                return InvalidId;
            }
            if (record.MailId != 0)
            {
                Warn("Enqueue ignored: the mail of \"" + record.FeatureId + "\" already has id " + record.MailId.ToString(CultureInfo.InvariantCulture));
                return InvalidId;
            }
            string problem = record.Validate();
            if (problem != null)
            {
                Warn("mail of \"" + record.FeatureId + "\" rejected: " + problem);
                return InvalidId;
            }
            if (nextId == int.MaxValue)
            {
                Warn("Enqueue ignored: the mail ids are exhausted");
                return InvalidId;
            }

            int id = nextId++;
            record.MailId = id;
            record.SentOn = today.Date;
            record.DeliveredOn = null;
            record.Deadline = null;
            records.Add(record);
            byId[id] = record;
            queuedCount++;

            EnforceLimit();
            return byId.ContainsKey(id) ? id : InvalidId;
        }

        /// <summary>
        /// The delivery pass of one game day. Returns the mails that are delivered now, in delivery order, with
        /// <see cref="MailRecordModel.DeliveredOn"/> = <paramref name="today"/> and <see cref="MailRecordModel.Deadline"/> = <paramref name="today"/> +
        /// <see cref="MailRecordModel.TimeoutDays"/> (none without a timeout). Returns an empty list and changes nothing while
        /// <paramref name="gameReady"/> is false or <paramref name="isBatchRun"/> is true. May be called several times a day: the quota of the day is kept.
        /// </summary>
        public IReadOnlyList<MailRecordModel> DeliverDue(DateTime today, bool gameReady, bool isBatchRun)
        {
            if (!gameReady || isBatchRun || queuedCount == 0) return NoMails;

            Quota.StartDay(today);
            DateTime day = today.Date;
            List<MailRecordModel> delivered = null;

            for (int level = (int)MailPriority.Critical; level >= (int)MailPriority.Low; level--)
            {
                var priority = (MailPriority)level;
                bool limited = priority != MailPriority.Critical;
                for (int i = 0; i < records.Count; i++)
                {
                    if (limited && Quota.IsDayFull) break;   // nothing more may go today in this and the lower priorities

                    MailRecordModel mail = records[i];
                    if (mail.IsDelivered || mail.Priority != priority) continue;
                    if (!Quota.Allows(priority, mail.FeatureId)) continue;   // this feature is at its limit; others behind it may still go

                    mail.DeliveredOn = day;
                    mail.Deadline = mail.TimeoutDays > 0 ? (DateTime?)day.AddDays(mail.TimeoutDays) : null;
                    queuedCount--;
                    Quota.Record(priority, mail.FeatureId);
                    if (delivered == null) delivered = new List<MailRecordModel>();
                    delivered.Add(mail);
                }
                if (limited && Quota.IsDayFull) break;
            }

            return delivered != null ? (IReadOnlyList<MailRecordModel>)delivered : NoMails;
        }

        // ---- lookup ----

        /// <summary>The mail with this id, delivered or queued; null for an unknown id.</summary>
        public MailRecordModel Get(int mailId)
        {
            MailRecordModel mail;
            return byId.TryGetValue(mailId, out mail) ? mail : null;
        }

        /// <summary>
        /// The latest mail (the highest id, delivered or queued) of the feature with exactly this tag; null when there is none or the
        /// feature or the tag is empty.
        /// </summary>
        public MailRecordModel Find(string featureId, string tag)
        {
            if (string.IsNullOrEmpty(featureId) || string.IsNullOrEmpty(tag)) return null;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                MailRecordModel mail = records[i];
                if (string.Equals(mail.FeatureId, featureId, StringComparison.Ordinal) && string.Equals(mail.Tag, tag, StringComparison.Ordinal))
                    return mail;
            }
            return null;
        }

        /// <summary>
        /// The mails of the feature, delivered and queued, in sending order; with a prefix only those whose tag starts with it (ordinal,
        /// case-sensitive), without one (null or empty) all of them, tagged or not. A snapshot, never null.
        /// </summary>
        public IReadOnlyList<MailRecordModel> FindAll(string featureId, string tagPrefix = null)
        {
            if (string.IsNullOrEmpty(featureId)) return NoMails;
            bool anyTag = string.IsNullOrEmpty(tagPrefix);
            List<MailRecordModel> found = null;
            for (int i = 0; i < records.Count; i++)
            {
                MailRecordModel mail = records[i];
                if (!string.Equals(mail.FeatureId, featureId, StringComparison.Ordinal)) continue;
                if (!anyTag && (mail.Tag == null || !mail.Tag.StartsWith(tagPrefix, StringComparison.Ordinal))) continue;
                if (found == null) found = new List<MailRecordModel>();
                found.Add(mail);
            }
            return found != null ? (IReadOnlyList<MailRecordModel>)found : NoMails;
        }

        /// <summary>The mails waiting for delivery in sending order (a snapshot).</summary>
        public IReadOnlyList<MailRecordModel> GetQueued()
        {
            return Snapshot(false);
        }

        /// <summary>The delivered mails in sending order (a snapshot).</summary>
        public IReadOnlyList<MailRecordModel> GetDelivered()
        {
            return Snapshot(true);
        }

        // ---- side file ----

        /// <summary>
        /// The <c>mail</c> section: <c>nextId</c>, the delivered mails in <c>mails</c> and the queued ones in <c>queue</c>, each in sending
        /// order. The lists are never null.
        /// </summary>
        public MailStoreDto ToDto()
        {
            var dto = new MailStoreDto
            {
                nextId = nextId,
                mails = new List<MailRecordDto>(records.Count - queuedCount),
                queue = new List<MailRecordDto>(queuedCount),
            };
            for (int i = 0; i < records.Count; i++)
            {
                MailRecordModel mail = records[i];
                (mail.IsDelivered ? dto.mails : dto.queue).Add(mail.ToDto());
            }
            return dto;
        }

        /// <summary>
        /// Replaces everything with the content of the <c>mail</c> section (a null section means an empty store). A record that breaks
        /// the value rules (see <see cref="MailRecordModel.FromDto"/>) or repeats an id is dropped with a warning and the rest is
        /// read. <c>nextId</c> is raised above the highest id read, never lowered below 100000. A queue longer than the limit is cut
        /// by the overflow rule. The daily counters are restored from the mails delivered on the latest delivery day, so loading a save
        /// made after the day's deliveries does not give the day a second allowance.
        /// </summary>
        public void Load(MailStoreDto dto)
        {
            Clear();
            if (dto == null) return;

            var loaded = new List<MailRecordModel>();
            var seen = new HashSet<int>();
            long maxId = 0;
            ReadList(dto.mails, false, loaded, seen, ref maxId);
            ReadList(dto.queue, true, loaded, seen, ref maxId);
            loaded.Sort(delegate (MailRecordModel a, MailRecordModel b) { return a.MailId.CompareTo(b.MailId); });

            foreach (MailRecordModel mail in loaded)
            {
                records.Add(mail);
                byId[mail.MailId] = mail;
                if (!mail.IsDelivered) queuedCount++;
            }

            long next = Math.Max((long)dto.nextId, Math.Max(maxId + 1, MailRecordModel.FirstId));
            nextId = next > int.MaxValue ? int.MaxValue : (int)next;

            RestoreQuota();
            EnforceLimit();
        }

        /// <summary>Forgets every mail and the daily counters (new game, exit to the menu, before a save is loaded). The limits stay.</summary>
        public void Clear()
        {
            records.Clear();
            byId.Clear();
            queuedCount = 0;
            nextId = MailRecordModel.FirstId;
            Quota.Reset();
        }

        // ---- internals ----

        private void ReadList(List<MailRecordDto> source, bool queued, List<MailRecordModel> into, HashSet<int> seen, ref long maxId)
        {
            if (source == null) return;
            foreach (MailRecordDto item in source)
            {
                MailRecordModel mail = MailRecordModel.FromDto(item, queued, warn);
                if (mail == null) continue;
                if (!seen.Add(mail.MailId))
                {
                    Warn("record " + mail.MailId.ToString(CultureInfo.InvariantCulture) + " dropped: id is used twice");
                    continue;
                }
                into.Add(mail);
                if (mail.MailId > maxId) maxId = mail.MailId;
            }
        }

        private void RestoreQuota()
        {
            DateTime? last = null;
            foreach (MailRecordModel mail in records)
            {
                if (mail.DeliveredOn.HasValue && (!last.HasValue || mail.DeliveredOn.Value > last.Value)) last = mail.DeliveredOn.Value;
            }
            if (!last.HasValue) return;

            Quota.StartDay(last.Value);
            foreach (MailRecordModel mail in records)
            {
                if (mail.DeliveredOn.HasValue && mail.DeliveredOn.Value == last.Value) Quota.Record(mail.Priority, mail.FeatureId);
            }
        }

        /// <summary>Drops the oldest Low, then the oldest Normal mails of the queue until it fits the limit; Critical mails stay.</summary>
        private void EnforceLimit()
        {
            int excess = queuedCount - queueLimit;
            if (excess <= 0) return;

            List<MailRecordModel> lows = null;
            List<MailRecordModel> normals = null;
            for (int i = 0; i < records.Count; i++)
            {
                MailRecordModel mail = records[i];
                if (mail.IsDelivered) continue;
                if (mail.Priority == MailPriority.Low)
                {
                    if (lows == null) lows = new List<MailRecordModel>();
                    lows.Add(mail);
                }
                else if (mail.Priority == MailPriority.Normal)
                {
                    if (normals == null) normals = new List<MailRecordModel>();
                    normals.Add(mail);
                }
            }

            var victims = new List<MailRecordModel>();   // oldest Low first, then oldest Normal: also the order of the warnings
            Take(lows, ref excess, victims);
            Take(normals, ref excess, victims);
            if (victims.Count == 0) return;

            foreach (MailRecordModel victim in victims)
            {
                byId.Remove(victim.MailId);
                Warn("queue is over its limit of " + queueLimit.ToString(CultureInfo.InvariantCulture) + ": dropped the "
                     + victim.Priority + " mail " + victim.MailId.ToString(CultureInfo.InvariantCulture) + " of feature \""
                     + victim.FeatureId + "\"" + (string.IsNullOrEmpty(victim.Tag) ? string.Empty : " (tag \"" + victim.Tag + "\")"));
            }
            var gone = new HashSet<MailRecordModel>(victims);
            records.RemoveAll(gone.Contains);
            queuedCount -= victims.Count;
        }

        private static void Take(List<MailRecordModel> oldestFirst, ref int excess, List<MailRecordModel> victims)
        {
            if (oldestFirst == null) return;
            for (int i = 0; i < oldestFirst.Count && excess > 0; i++)
            {
                victims.Add(oldestFirst[i]);
                excess--;
            }
        }

        private IReadOnlyList<MailRecordModel> Snapshot(bool delivered)
        {
            var list = new List<MailRecordModel>(delivered ? records.Count - queuedCount : queuedCount);
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsDelivered == delivered) list.Add(records[i]);
            }
            return list;
        }

        private void Warn(string message)
        {
            Action<string> sink = warn;
            if (sink != null) sink("mail: " + message);
        }

        private static int ClampLimit(int limit)
        {
            if (limit < 1) return 1;
            return limit > MaxQueueLimit ? MaxQueueLimit : limit;
        }
    }
}
