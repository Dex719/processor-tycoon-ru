using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The news of the architectures for the player (FR-15 items 2-4, stage 2; design.md Component 13): a pop-up when an AI company changes the architecture it designs on,
    /// and a letter when the ecosystem of a family passes 25 %, 50 % or 75 % for the first time.
    /// <list type="bullet">
    /// <item><b>A switch</b> - <see cref="OnActiveChanged"/> is told by <see cref="ArchitectureAi"/>; "{company} переходит на {architecture}" is shown through
    /// <see cref="ModNotify"/> (the core holds the pop-ups back during the fast-forward and the batch run of a month, FR-15 item 2). The first family a company is given by the
    /// seed is not a switch.</item>
    /// <item><b>A milestone</b> - <see cref="OnEcosystem"/> is told by <see cref="EcosystemService"/> after each monthly measurement. The letter has the priority <c>Normal</c> and the
    /// tag <c>arch:eco:&lt;familyId&gt;:&lt;threshold&gt;</c>; every threshold is announced once per game: it is recorded in the save section (<c>milestones</c>), so it survives a
    /// load, and the mail service is asked for the tag as well. Two thresholds passed in one month give one letter, for the higher. The default (proprietary) family has no
    /// letters: it is every company's own ISA, not an ecosystem to join. The first measurement of a game (<see cref="OnEcosystemBaseline"/>) records what is long past without a letter.</item>
    /// <item>The switch <c>notifications</c> of the hub silences both (the milestones are still recorded, so that turning it on later does not bring a pile of old letters).</item>
    /// </list>
    /// Everything is for stage 2 of <c>balance.json</c> only. A failure is counted against the feature and never reaches the game.
    /// </summary>
    internal sealed class ArchNotifier
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string SwitchSite = "notify/switch";
        private const string MilestoneSite = "notify/milestone";

        /// <summary>How many families the line "the market now" of a letter names.</summary>
        private const int TopCount = 3;

        private readonly ArchitecturesFeature _feature;

        public ArchNotifier(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        private bool Works
        {
            get { return _feature.Registry != null && _feature.Catalog != null && !_feature.IsNeutralNow && _feature.Balance.IsFullStage; }
        }

        // ---- a switch of an AI company ----

        /// <summary>An AI company changed the architecture it designs on (<see cref="ArchitectureAi.OnActiveChanged"/>).</summary>
        public void OnActiveChanged(ICompany company, string previous, string current)
        {
            ModHooks.Guard(FeatureId, SwitchSite, () => ShowSwitch(company, previous, current));
        }

        private void ShowSwitch(ICompany company, string previous, string current)
        {
            if (!Works || !_feature.NotificationsEnabled) return;
            if (ReferenceEquals(company, null) || company.IsPlayer || company.IsFoundry || company.IsBankrupt) return;
            if (previous == null || current == null || string.Equals(previous, current, StringComparison.Ordinal)) return;   // the seed gives a family, it is no switch

            string family = ArchDisplay.FamilyName(_feature.Catalog, current, ModL10n.L);
            ModNotify.ShowKey(FeatureId, "arch.notify.switch", company.Name, family);
        }

        // ---- the milestones of the ecosystem ----

        /// <summary>
        /// A monthly measurement of the ecosystem. <paramref name="previous"/> is the measurement before it (null: unknown, treated as a first look). Sends the letters of
        /// the thresholds that were crossed this month.
        /// </summary>
        public void OnEcosystem(IReadOnlyDictionary<string, float> previous, IReadOnlyDictionary<string, float> current, bool hasSales)
        {
            ModHooks.Guard(FeatureId, MilestoneSite, () => CheckMilestones(previous, current, hasSales));
        }

        /// <summary>The first measurement of a game: the thresholds the shares already stand above are recorded as announced, without a letter.</summary>
        public void OnEcosystemBaseline(IReadOnlyDictionary<string, float> current, bool hasSales)
        {
            ModHooks.Guard(FeatureId, MilestoneSite, () => CheckMilestones(null, current, hasSales));
        }

        private void CheckMilestones(IReadOnlyDictionary<string, float> previous, IReadOnlyDictionary<string, float> current, bool hasSales)
        {
            if (!Works || current == null || !hasSales) return;   // without sales the shares are the fallback of the early game, not a market

            EcosystemConfig config = _feature.Balance.ecosystem;
            List<MilestoneSave> recorded = _feature.Registry.Milestones;
            foreach (ArchFamily family in _feature.Catalog.Families)
            {
                string id = family.Id;
                if (string.Equals(id, config.fallbackFamily, StringComparison.Ordinal)) continue;

                float now = EcosystemMath.ShareOf(current, id);
                Func<int, bool> announced = threshold => IsAnnounced(recorded, id, threshold);
                List<int> thresholds;
                bool silent = previous == null;
                if (silent) thresholds = EcosystemMilestones.Passed(now, config.milestones, announced);
                else thresholds = EcosystemMilestones.Crossed(Math.Max(0f, EcosystemMath.ShareOf(previous, id)), now, config.milestones, announced);
                if (thresholds.Count == 0) continue;

                foreach (int threshold in thresholds) Record(recorded, id, threshold);
                if (silent) continue;

                if (_feature.NotificationsEnabled) SendLetter(family, thresholds[thresholds.Count - 1], current);
            }
        }

        private static bool IsAnnounced(List<MilestoneSave> recorded, string familyId, int threshold)
        {
            for (int i = 0; i < recorded.Count; i++)
            {
                MilestoneSave m = recorded[i];
                if (m != null && m.threshold == threshold && string.Equals(m.arch, familyId, StringComparison.Ordinal)) return true;
            }
            return ModMail.Find(FeatureId, EcosystemMilestones.Tag(familyId, threshold)) != null;
        }

        private static void Record(List<MilestoneSave> recorded, string familyId, int threshold)
        {
            if (recorded.Count >= ArchRegistry.MaxMilestones) return;
            recorded.Add(new MilestoneSave { arch = familyId, threshold = threshold });
        }

        private void SendLetter(ArchFamily family, int threshold, IReadOnlyDictionary<string, float> shares)
        {
            string name = ModL10n.L(family.NameKey);
            var message = new ModMailMessage
            {
                FeatureId = FeatureId,
                Tag = EcosystemMilestones.Tag(family.Id, threshold),
                TitleKey = "arch.mail.eco.subject",
                BodyKey = "arch.mail.eco.body",
                Args = new[] { name, threshold.ToString(CultureInfo.InvariantCulture), MarketNow(shares) },
                Priority = MailPriority.Normal,
            };

            if (ModMail.Send(message) < 0)
            {
                ModLog.Warn(FeatureId, "notify: the mail service did not accept the letter " + message.Tag);
            }
        }

        /// <summary>"x86 62 %, ARM 20 %, RISC 18 %": the largest shares, named in the language of the game.</summary>
        private string MarketNow(IReadOnlyDictionary<string, float> shares)
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<string, int> entry in EcosystemMilestones.Top(shares, TopCount))
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(ArchDisplay.FamilyName(_feature.Catalog, entry.Key, ModL10n.L)).Append(' ').Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append(" %");
            }
            return text.Length == 0 ? ArchDisplay.Empty : text.ToString();
        }
    }
}
