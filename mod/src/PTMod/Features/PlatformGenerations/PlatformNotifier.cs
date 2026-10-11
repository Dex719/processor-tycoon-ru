using PTMod.Core;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// What the player is told about platforms (FR-2 item 2, FR-3 items 3 and 4, FR-6 item 5; design, Component 7): the announcement of a generation
    /// (a letter and a notification), the launch of a generation (a notification), the player becoming an early adopter (a letter and a notification)
    /// and the penalty for changing the socket (a notification). The handlers say that a thing happened (<see cref="IPlatformNotifier"/>); this class
    /// decides whether it is shown and shows it through <c>ModNotify</c> and <c>ModMail</c>:
    /// <list type="bullet">
    /// <item>nothing during the new game's fast-forward, while a save loads, with the feature off, or with the switch "Уведомления и письма" off
    /// (the chronology goes on in every case); the rest of the holding back - a batch run of a month - is the core's, which delays what is sent
    /// until the run is over;</item>
    /// <item>the words and the numbers are <see cref="PlatformNotices"/>'s, the string tables hold the sentences (<c>pg.notify.*</c>, <c>pg.mail.*</c>);</item>
    /// <item>a letter has a tag (<c>pg:announce:&lt;genId&gt;</c>, <c>pg:early:&lt;genId&gt;</c>) and the priority Normal; a letter whose tag is in the
    /// mail already is not sent again (the feature switched off and on does not repeat itself);</item>
    /// <item>a failure inside is caught, counted against the feature and never reaches the game.</item>
    /// </list>
    /// </summary>
    internal sealed class PlatformNotifier : IPlatformNotifier
    {
        private const string Feature = PlatformGenerationsFeature.FeatureId;

        private readonly PlatformGenerationsFeature _feature;
        private readonly ViewText _text;

        public PlatformNotifier(PlatformGenerationsFeature feature)
            : this(feature, PlatformViewText.Instance)
        {
        }

        public PlatformNotifier(PlatformGenerationsFeature feature, ViewText text)
        {
            _feature = feature;
            _text = text;
        }

        public void Announced(GenerationDef generation)
        {
            if (generation == null || !IsAllowed()) return;

            ModHooks.Guard(Feature, "notifier/announced", () =>
            {
                Send(PlatformNotices.AnnounceMail(generation, _feature.Balance, _text));
                Show(PlatformNotices.Announced(generation, _text));
            });
        }

        public void Launched(GenerationDef generation, string byCompanyKey, bool player)
        {
            if (generation == null || !IsAllowed()) return;

            ModHooks.Guard(Feature, "notifier/launched", () =>
                Show(PlatformNotices.Launched(generation, _text.Company(byCompanyKey), _text)));
        }

        public void EarlyAdopter(GenerationDef generation, int slot)
        {
            if (generation == null || !IsAllowed()) return;

            ModHooks.Guard(Feature, "notifier/early", () =>
            {
                Send(PlatformNotices.EarlyMail(generation, slot, _feature.Balance, _text));
                Show(PlatformNotices.EarlyAdopter(generation, _feature.Balance, _text));
            });
        }

        public void Churn(SocketId socket, float penalty, int months, string mainSocketKey, int mainAgeMonths)
        {
            if (!socket.IsValid || !IsAllowed()) return;

            ModHooks.Guard(Feature, "notifier/churn", () =>
            {
                string mainName = string.Empty;
                if (!string.IsNullOrEmpty(mainSocketKey))
                {
                    SocketId main = _feature.InstalledBase.SocketOf(mainSocketKey);
                    mainName = _text.Socket(main.IsValid ? main : new SocketId { Key = mainSocketKey });
                }

                Show(PlatformNotices.Churn(_text.Socket(socket), penalty, months, mainName, mainAgeMonths, _feature.Balance.Rating.MinSocketLifeMonths));
            });
        }

        /// <summary>The feature is on, the switch is on, and the game is in its ordinary course (no fast-forward, no loading).</summary>
        private static bool IsAllowed()
        {
            return ModFeatures.IsActive(Feature) && PlatformSettings.Notifications && !ModTicks.IsFastForwarding && !ModTicks.IsLoading;
        }

        private static void Show(Notice notice)
        {
            ModNotify.ShowKey(Feature, notice.Key, notice.Args);
        }

        private static void Send(MailPlan plan)
        {
            if (ModMail.Find(Feature, plan.Tag) != null) return;   // this letter is in the mail already

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = plan.Tag,
                TitleKey = plan.TitleKey,
                BodyKey = plan.BodyKey,
                Args = plan.Args,
                Priority = MailPriority.Normal,
            };
            if (ModMail.Send(message) < 0) ModLog.Warn(Feature, "notifier: the letter '" + plan.Tag + "' was not accepted by the mail service");
        }
    }
}
