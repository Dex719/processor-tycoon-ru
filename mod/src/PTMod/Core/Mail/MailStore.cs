using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Mail;
using PTMod.Core.Logic.Save;

namespace PTMod.Core
{
    /// <summary>
    /// What <see cref="ModMail"/> asks the game and the rest of the core, as delegates that an offline harness can replace: the game date, the
    /// moments in which mail is held back, the feature registry and the patch that keeps the letters out of the game's save. The fields start
    /// with the real readers (<see cref="ModTicks"/>, <see cref="ModFeatures"/>, <see cref="MailPatches"/>).
    /// </summary>
    internal sealed class MailEnv
    {
        /// <summary><c>DateController.CurrentDate</c>; <c>default(DateTime)</c> outside the game scene.</summary>
        internal Func<DateTime> Today = ReadToday;

        /// <summary><see cref="ModTicks.IsGameReady"/>.</summary>
        internal Func<bool> IsGameReady = ReadGameReady;

        /// <summary><see cref="ModTicks.IsFastForwarding"/>: the new-game fast-forward runs.</summary>
        internal Func<bool> IsFastForwarding = ReadFastForwarding;

        /// <summary><see cref="ModTicks.IsLoading"/>: the loading screen is up.</summary>
        internal Func<bool> IsLoading = ReadLoading;

        /// <summary><see cref="ModTicks.IsBatchRun"/>: <c>DateController.NextMonth</c> plays up to 31 days in one frame.</summary>
        internal Func<bool> IsBatchRun = ReadBatchRun;

        /// <summary>A feature of the registry, or the framework itself.</summary>
        internal Func<string, bool> IsKnownFeature = ReadKnownFeature;

        /// <summary>The patch P15 is in force: a delivered letter will not be written into the game's save.</summary>
        internal Func<bool> IsSaveGuardActive = ReadSaveGuard;

        private static DateTime ReadToday()
        {
            return ModTicks.Today;
        }

        private static bool ReadGameReady()
        {
            return ModTicks.IsGameReady;
        }

        private static bool ReadFastForwarding()
        {
            return ModTicks.IsFastForwarding;
        }

        private static bool ReadLoading()
        {
            return ModTicks.IsLoading;
        }

        private static bool ReadBatchRun()
        {
            return ModTicks.IsBatchRun;
        }

        private static bool ReadKnownFeature(string featureId)
        {
            return featureId == ModLog.CoreFeatureId || ModFeatures.Find(featureId) != null;
        }

        private static bool ReadSaveGuard()
        {
            return MailPatches.IsInEffect;
        }
    }

    /// <summary>
    /// The stored mail of the game in play and its place in the side file (FR-14 item 10): the pure <see cref="MailQueue"/> (every mail ever
    /// sent, delivered or still queued, with the daily counters), the block <c>mail</c> of the section <c>mod-framework</c> that saves and
    /// restores it, and the views the features get. The limits come from <c>core.json</c> (<c>mail.maxDeliveredPerDay</c>, <c>mail.maxPerFeaturePerDay</c>,
    /// <c>mail.queueLimit</c>) when the queue is first used.
    /// <list type="bullet">
    /// <item><b>Save.</b> <see cref="Part"/> writes <c>nextId</c>, the delivered mails and the queue (a mail waiting for delivery is saved too and
    /// goes on after the load); before it writes, the reading state of every letter in the inbox is copied into its record, because the player
    /// reads in the game's window, not here. A game without any mail writes no block.</item>
    /// <item><b>Load.</b> A block that is not an object, or records that break the rules, are dropped with a warning and the rest is read; a save
    /// without the block, a new game and the main menu empty the store. The letters themselves come back on <c>BeforeGameReady</c>
    /// (<see cref="ModMail.RestoreInbox"/>), not here.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class MailStore
    {
        private const string CoreId = ModLog.CoreFeatureId;

        internal static readonly IReadOnlyList<ModMailRecord> NoViews = new ModMailRecord[0];

        private static readonly Dictionary<int, ModMailRecord> Views = new Dictionary<int, ModMailRecord>();
        private static readonly ICoreSectionPart MailPart = new MailBlock();

        private static MailQueue _queue;

        /// <summary>What the service reads from the game; the offline harness replaces the delegates.</summary>
        internal static MailEnv Env = new MailEnv();

        /// <summary>The block <c>mail</c> of the section of the core (registered by <see cref="ModMail"/> at the Declare stage).</summary>
        internal static ICoreSectionPart Part
        {
            get { return MailPart; }
        }

        /// <summary>
        /// The store and the queue. Made on first use from <c>core.json</c>; a problem of the queue (a rejected record, an overflow) is a
        /// warning in the log under the framework's id, English, with the id of the feature in the text.
        /// </summary>
        internal static MailQueue Queue
        {
            get
            {
                MailQueue queue = _queue;
                if (queue != null) return queue;

                double perDay = ModContent.GetCoreNumber("mail", "maxDeliveredPerDay", MailQuota.DefaultMaxPerDay);
                double perFeature = ModContent.GetCoreNumber("mail", "maxPerFeaturePerDay", MailQuota.DefaultMaxPerFeature);
                double limit = ModContent.GetCoreNumber("mail", "queueLimit", MailQueue.DefaultQueueLimit);

                queue = new MailQueue(ToLimit(limit, MailQueue.DefaultQueueLimit), new MailQuota(ToLimit(perDay, MailQuota.DefaultMaxPerDay),
                    ToLimit(perFeature, MailQuota.DefaultMaxPerFeature)), WarnFromQueue);
                _queue = queue;
                return queue;
            }
        }

        /// <summary>For the harness: another queue (another limit, a collecting warning sink) in place of the one made from <c>core.json</c>.</summary>
        internal static void UseQueue(MailQueue queue)
        {
            _queue = queue;
            ForgetViews();
        }

        // ---- the message and the views ----

        /// <summary>
        /// The record of a message, copied so that the feature's later changes to its objects change nothing. Nothing is checked here
        /// (<see cref="MailRecordModel.Validate"/> does that); a null choice or action stays null so that the check names it.
        /// </summary>
        internal static MailRecordModel ToModel(ModMailMessage message)
        {
            var record = new MailRecordModel
            {
                FeatureId = message.FeatureId,
                Tag = message.Tag,
                Priority = message.Priority,
                TitleKey = message.TitleKey,
                BodyKey = message.BodyKey,
                Title = Copy(message.Title),
                Body = Copy(message.Body),
                Args = CopyOf(message.Args),
                TimeoutDays = message.TimeoutDays,
                DefaultChoiceId = message.DefaultChoiceId,
            };

            if (message.Choices != null)
            {
                foreach (ModMailChoice choice in message.Choices)
                {
                    record.Choices.Add(choice == null ? null : new MailChoiceModel
                    {
                        Id = choice.Id,
                        LabelKey = choice.LabelKey,
                        Label = Copy(choice.Label),
                        TooltipKey = choice.TooltipKey,
                        Enabled = choice.Enabled,
                        DisabledReasonKey = choice.DisabledReasonKey,
                        DisabledReasonArgs = CopyOf(choice.DisabledReasonArgs),
                    });
                }
            }

            if (message.Actions != null)
            {
                foreach (ModMailAction action in message.Actions)
                {
                    record.Actions.Add(action == null ? null : new MailActionModel
                    {
                        Id = action.Id,
                        LabelKey = action.LabelKey,
                        Label = Copy(action.Label),
                        TooltipKey = action.TooltipKey,
                        Enabled = action.Enabled,
                        Repeatable = action.Repeatable,
                        DisabledReasonKey = action.DisabledReasonKey,
                        DisabledReasonArgs = CopyOf(action.DisabledReasonArgs),
                    });
                }
            }
            return record;
        }

        /// <summary>The view of a stored record the features hold; the same object every time for the same record, null for null.</summary>
        internal static ModMailRecord ViewOf(MailRecordModel model)
        {
            if (model == null) return null;

            ModMailRecord view;
            if (Views.TryGetValue(model.MailId, out view) && ReferenceEquals(view.Model, model)) return view;

            view = new ModMailRecord(model);
            Views[model.MailId] = view;
            return view;
        }

        internal static IReadOnlyList<ModMailRecord> ViewsOf(IReadOnlyList<MailRecordModel> models)
        {
            if (models == null || models.Count == 0) return NoViews;

            var views = new List<ModMailRecord>(models.Count);
            for (int i = 0; i < models.Count; i++) views.Add(ViewOf(models[i]));
            return views;
        }

        /// <summary>The views belong to the records of the store that was replaced or emptied.</summary>
        internal static void ForgetViews()
        {
            Views.Clear();
        }

        /// <summary>The <see cref="MailResolution"/> of the stored text (<see cref="MailRecordModel.Resolution"/>); Pending for anything unknown.</summary>
        internal static MailResolution ResolutionOf(string stored)
        {
            if (stored == MailRecordModel.ResolutionPlayer) return MailResolution.Player;
            if (stored == MailRecordModel.ResolutionTimeout) return MailResolution.Timeout;
            if (stored == MailRecordModel.ResolutionProgrammatic) return MailResolution.Programmatic;
            return MailResolution.Pending;
        }

        // ---- internals ----

        private static LocText Copy(LocText text)
        {
            return text == null ? null : new LocText(text.Key, text.Ru, text.En);
        }

        private static string[] CopyOf(string[] values)
        {
            return values == null ? null : (string[])values.Clone();
        }

        private static int ToLimit(double value, int fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return value > int.MaxValue ? int.MaxValue : (int)Math.Round(value);
        }

        private static void WarnFromQueue(string message)
        {
            ModLog.Warn(CoreId, message);
        }

        // ---- the block of the section ----

        /// <summary>The block <c>mail</c>: <c>{ "nextId": 100004, "mails": [...], "queue": [...] }</c> (<c>MailStoreDto</c>).</summary>
        private sealed class MailBlock : ICoreSectionPart
        {
            public string Key
            {
                get { return CoreSection.MailKey; }
            }

            public void ResetToDefaults()
            {
                Queue.Clear();
                ModMail.StoreReplaced();
            }

            public void Read(JsonNode block, int schema)
            {
                var node = block as JsonObject;
                if (node == null)
                {
                    if (block != null && !block.IsNull) ModLog.Warn(CoreId, "save: the block 'mail' is not an object, the mail starts empty");
                    ResetToDefaults();
                    return;
                }

                var errors = new List<string>();
                MailStoreDto dto = ModJson.Bind<MailStoreDto>(node, errors);
                foreach (string error in errors) ModLog.Warn(CoreId, "save: mail" + error.TrimStart('$'));

                Queue.Load(dto);
                ModMail.StoreReplaced();
                ModLog.Debug(CoreId, "save: mail read, " + Queue.DeliveredCount.ToString(CultureInfo.InvariantCulture) + " delivered, "
                                     + Queue.QueuedCount.ToString(CultureInfo.InvariantCulture) + " in the queue, next id " + Queue.NextId.ToString(CultureInfo.InvariantCulture));
            }

            public JsonNode Write()
            {
                MailQueue queue = Queue;
                if (queue.Count == 0 && queue.NextId <= MailRecordModel.FirstId) return null;   // a game without any mail leaves the file as it was

                MailFactory.SyncRead(queue);
                var errors = new List<string>();
                JsonNode node = ModJson.From(queue.ToDto(), errors);
                foreach (string error in errors) ModLog.Warn(CoreId, "save: mail" + error.TrimStart('$'));
                return node;
            }
        }
    }
}
