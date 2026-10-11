using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Builds what the intelligence window shows (design.md, Component 11; FR-9, FR-16, FR-17): one <see cref="IntelRow"/> per AI company for the list and
    /// the <see cref="IntelDetails"/> of the chosen one. Pure: the facts of the game come as <see cref="IntelCompanyFacts"/>, the state of the feature is read
    /// from the same books the rest of the feature uses (<see cref="PersonalityBook"/>, <see cref="AttitudeLedger"/>, <see cref="ReactionBook"/>, the
    /// section), the language and the game's formatters come through <see cref="IIntelText"/>.
    /// <list type="bullet">
    /// <item><b>Character.</b> The archetype is shown when <see cref="CharacterVisibility"/> says so, otherwise the text "Character unknown"; the archetype id and
    /// the description do not leave the builder while it is hidden (nor does the discount of a foundry partner, which would give it away).</item>
    /// <item><b>Fog.</b> Only the share and the <c>Ips</c> depend on <see cref="IntelSettings.Fog"/> (<see cref="IntelFog"/>); the attitude, the threats, the
    /// effects in force and the history are exact.</item>
    /// <item><b>Threats.</b> A planned reaction is a threat for the player once its intelligence mail was delivered (the date of carrying out is set then);
    /// the panel shows the range of weeks, never the day (<see cref="ThreatMath"/>).</item>
    /// <item><b>History.</b> The last ten lines of the changes of the attitude and of the reactions of the rival, newest first.</item>
    /// <item><b>No money, no projects</b> of the rivals anywhere (FR-9 item 6): the facts do not carry them.</item>
    /// </list>
    /// </summary>
    public sealed class IntelBuilder
    {
        /// <summary>The panel shows this many lines of history.</summary>
        public const int HistoryLines = 10;

        private const string Dash = "—";

        private readonly PersonalityBook _book;
        private readonly AttitudeLedger _ledger;
        private readonly ReactionBook _reactions;
        private readonly Func<RivalState> _state;
        private readonly Func<ArchetypeCatalog> _catalog;
        private readonly IIntelText _text;

        public IntelBuilder(PersonalityBook book, AttitudeLedger ledger, ReactionBook reactions, Func<RivalState> state, Func<ArchetypeCatalog> catalog,
                            IIntelText text)
        {
            if (book == null) throw new ArgumentNullException("book");
            if (ledger == null) throw new ArgumentNullException("ledger");
            if (reactions == null) throw new ArgumentNullException("reactions");
            if (text == null) throw new ArgumentNullException("text");

            _book = book;
            _ledger = ledger;
            _reactions = reactions;
            _state = state;
            _catalog = catalog;
            _text = text;
        }

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        private RivalConfig Config
        {
            get
            {
                ArchetypeCatalog catalog = _catalog != null ? _catalog() : null;
                RivalConfig config = catalog != null ? catalog.Config : null;
                return config ?? RivalConfig.Defaults();
            }
        }

        /// <summary>The order of the list: companies in the game first, by name; the bankrupt ones below them (FR-9 item 2).</summary>
        public static int Compare(IntelRow a, IntelRow b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            if (a.IsBankrupt != b.IsBankrupt) return a.IsBankrupt ? 1 : -1;
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.UniqueId.CompareTo(b.UniqueId);
        }

        // ---- the list ----

        /// <summary>The row of one company: the character (or its absence), the attitude, the segments, the share, the last CPU.</summary>
        public IntelRow BuildRow(IntelCompanyFacts facts, IntelSettings settings)
        {
            if (facts == null) throw new ArgumentNullException("facts");

            RivalConfig config = Config;
            int uid = facts.UniqueId;
            Archetype archetype = _book.ArchetypeOf(uid);
            float attitude = _ledger.Get(uid);
            bool visible = archetype != null
                           && CharacterVisibility.IsVisible(settings.HiddenMode, _book.IsRevealed(uid), attitude, config.attitude.revealAttitude);
            AttitudeLevel level = _ledger.LevelOf(attitude);

            var row = new IntelRow
            {
                UniqueId = uid,
                Name = facts.Name ?? string.Empty,
                IsBankrupt = facts.IsBankrupt,
                IsDormant = _book.IsDormant(uid),
                CharacterVisible = visible,
                ArchetypeId = visible ? archetype.id : string.Empty,
                CharacterText = visible ? _text.Resolve(archetype.Name) : T("rival.window.unknownCharacter"),
                Description = visible ? _text.Resolve(archetype.Description) : string.Empty,
                Attitude = attitude,
                AttitudeText = SignedWhole(attitude),
                Level = level,
                LevelText = T("rival.level." + AttitudeLedger.LevelKeyName(level)),
                AttitudeTone = ToneOf(level),
                SegmentsText = SegmentsOf(facts),
                ShareText = ShareOf(facts, settings.Fog, config),
                LastCpuText = LastCpuOf(facts, settings.Fog, config),
            };
            row.Tone = facts.IsBankrupt ? IntelTone.Muted : IntelTone.Normal;
            row.SummaryText = facts.IsBankrupt
                ? T("rival.window.bankrupt")
                : T("rival.window.rowSummary", row.CharacterText, row.SegmentsText, row.ShareText, row.LastCpuText);
            return row;
        }

        // ---- the details ----

        /// <summary>The right side of the panel for one company (FR-9 item 3).</summary>
        public IntelDetails BuildDetails(IntelCompanyFacts facts, IntelSettings settings)
        {
            IntelRow row = BuildRow(facts, settings);
            var details = new IntelDetails(row);
            details.Sections.Add(HeaderSection(row));
            details.Sections.Add(AttitudeSection(row));
            details.Sections.Add(MarketSection(facts, row));
            details.Sections.Add(ThreatSection(row, settings));
            details.Sections.Add(ActiveSection(row, settings));
            details.Sections.Add(HistorySection(row, settings));
            return details;
        }

        private IntelSection HeaderSection(IntelRow row)
        {
            var section = new IntelSection("header", null);
            section.Add(new IntelLine(null, row.Name, IntelTone.Normal, true));
            if (row.IsBankrupt) section.Add(new IntelLine(null, T("rival.window.bankrupt"), IntelTone.Bad));
            else if (row.IsDormant) section.Add(new IntelLine(null, T("rival.window.dormant"), IntelTone.Muted));

            if (row.CharacterVisible)
            {
                section.Add(new IntelLine(null, row.CharacterText, IntelTone.Normal, true));
                if (row.Description.Length > 0) section.Add(new IntelLine(null, row.Description, IntelTone.Muted));
            }
            else
            {
                section.Add(new IntelLine(null, row.CharacterText, IntelTone.Muted));
            }
            return section;
        }

        private IntelSection AttitudeSection(IntelRow row)
        {
            var section = new IntelSection("attitude", T("rival.window.attitude"));
            Archetype archetype = _book.ArchetypeOf(row.UniqueId);
            bool acts = archetype != null && !row.IsDormant;
            bool discount = acts && archetype.behaviour != null && archetype.behaviour.negotiationDiscount;

            NegotiationBonus bonus = acts
                ? NegotiationRules.Bonus(true, row.Attitude, discount, NegotiationNumbers.From(Config))
                : default(NegotiationBonus);
            bool showDiscount = discount && row.CharacterVisible;   // the discount gives a foundry partner away

            // the explanation on the hover tip: what the number is and what it does in a negotiation
            var tip = new StringBuilder(T("rival.window.attitudeTip"));
            if (acts)
            {
                tip.Append('\n').Append(T("rival.window.negotiationHint", NegotiationRules.Signed(bonus.Interest)));
                if (showDiscount) tip.Append('\n').Append(DiscountText(bonus));
            }

            section.Add(new IntelLine(T("rival.window.attitude.value"), row.AttitudeText + "  ·  " + row.LevelText, row.AttitudeTone, false, tip.ToString()));
            if (acts)
            {
                section.Add(new IntelLine(null, T("rival.window.negotiationHint", NegotiationRules.Signed(bonus.Interest)),
                    bonus.Interest > 0 ? IntelTone.Good : (bonus.Interest < 0 ? IntelTone.Bad : IntelTone.Normal)));
                if (showDiscount) section.Add(new IntelLine(null, DiscountText(bonus), bonus.Price > 0 ? IntelTone.Good : IntelTone.Muted));
            }
            return section;
        }

        private string DiscountText(NegotiationBonus bonus)
        {
            return bonus.Price > 0
                ? T("rival.window.discountHint", bonus.Price.ToString(CultureInfo.InvariantCulture))
                : T("rival.window.discountNone");
        }

        private IntelSection MarketSection(IntelCompanyFacts facts, IntelRow row)
        {
            var section = new IntelSection("market", T("rival.window.market"));
            section.Add(new IntelLine(T("rival.window.segments"), row.SegmentsText));
            section.Add(new IntelLine(T("rival.window.share"), row.ShareText));
            section.Add(new IntelLine(T("rival.window.lastCpu"), row.LastCpuText));
            if (facts.HasReputation)
            {
                section.Add(new IntelLine(T("rival.window.reputation"), WholeText(facts.Reputation)));
            }
            return section;
        }

        private IntelSection ThreatSection(IntelRow row, IntelSettings settings)
        {
            var section = new IntelSection("threats", T("rival.window.threats"));
            RivalConfig config = Config;
            int low = config.telegraphDays != null && config.telegraphDays.Length > 0 ? config.telegraphDays[0] : 14;
            int high = config.telegraphDays != null && config.telegraphDays.Length > 1 ? config.telegraphDays[1] : 45;

            var threats = new List<KeyValuePair<DateTime, PendingReaction>>();
            foreach (PendingReaction pending in _reactions.PendingSnapshot())
            {
                DateTime fireOn;
                // not delivered yet (no day set): the player has not been told, so it is not a threat he can see
                if (pending.uid != row.UniqueId || !RivalDates.TryParse(pending.fireOn, out fireOn)) continue;
                threats.Add(new KeyValuePair<DateTime, PendingReaction>(fireOn, pending));
            }
            threats.Sort((a, b) =>
            {
                int byDay = a.Key.CompareTo(b.Key);
                return byDay != 0 ? byDay : string.CompareOrdinal(a.Value.id, b.Value.id);
            });

            foreach (KeyValuePair<DateTime, PendingReaction> threat in threats)
            {
                int from, to;
                ThreatMath.WeeksLeft(settings.Today, threat.Key, threat.Value.telegraphDays, low, high, out from, out to);
                string name = ReactionLabel(threat.Value.type, threat.Value.segment);
                string text;
                if (to <= 1) text = T("rival.window.threatSoon", name);
                else if (from == 0) text = T("rival.window.threatWithin", name, to.ToString(CultureInfo.InvariantCulture));
                else text = T("rival.window.threat", name, ThreatMath.RangeText(from, to));
                section.Add(new IntelLine(null, text, IntelTone.Warn));
            }

            if (section.Lines.Count == 0) section.Add(new IntelLine(null, T("rival.window.none"), IntelTone.Muted));
            return section;
        }

        private IntelSection ActiveSection(IntelRow row, IntelSettings settings)
        {
            var section = new IntelSection("active", T("rival.window.active"));

            var active = _reactions.ActiveOf(row.UniqueId);
            active.Sort((a, b) =>
            {
                DateTime x, y;
                RivalDates.TryParse(a.end, out x);
                RivalDates.TryParse(b.end, out y);
                int byEnd = x.CompareTo(y);
                return byEnd != 0 ? byEnd : string.CompareOrdinal(a.id, b.id);
            });
            Archetype archetype = _book.ArchetypeOf(row.UniqueId);
            foreach (ActiveReaction reaction in active)
            {
                DateTime end;
                string until = RivalDates.TryParse(reaction.end, out end) ? _text.Date(end) : Dash;
                string label = ReactionLabel(reaction.type, reaction.segment);
                string size = MagnitudeOf(reaction.type, archetype);
                string text = size.Length > 0 ? T("rival.window.activeLineSize", label, size, until) : T("rival.window.activeLine", label, until);
                section.Add(new IntelLine(null, text, IntelTone.Warn));
            }

            DateTime cautionUntil;
            if (CautionUntil(row, settings.Today, out cautionUntil))
            {
                section.Add(new IntelLine(null, T("rival.window.caution", _text.Date(cautionUntil)), IntelTone.Warn));
            }

            if (section.Lines.Count == 0) section.Add(new IntelLine(null, T("rival.window.none"), IntelTone.Muted));
            return section;
        }

        /// <summary>
        /// The size of an effect in force (NFR-5: its term and size are on the panel): the cut of the prices, the boosts of a counter-launch, the factor of
        /// the OEM push. The numbers are the ones the handlers of the effects take (<see cref="ReactionParams"/>: the archetype's own, else the config's).
        /// Empty for a reaction that has no size to show.
        /// </summary>
        private string MagnitudeOf(string typeName, Archetype archetype)
        {
            ReactionType type;
            if (!ReactionKinds.TryParseReaction(typeName, out type)) return string.Empty;

            RivalConfig config = Config;
            switch (type)
            {
                case ReactionType.PriceCut:
                    return T("rival.window.size.priceCut", EffectArgs.Percent(ReactionParams.PriceCutDepth(archetype, config)));
                case ReactionType.CounterLaunch:
                    return T("rival.window.size.counterLaunch", EffectArgs.Multiplier(ReactionParams.ApprovalBoost(archetype, config)),
                        EffectArgs.Multiplier(ReactionParams.FundingBoost(archetype, config)));
                case ReactionType.OemPush:
                    return T("rival.window.size.oemPush", EffectArgs.Multiplier(ReactionParams.OemPushSignChance));
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// The caution of the company after a public design flaw is in force (FR-17): a record in the section whose day has not come, for a company that
        /// is in the game and not put aside (FR-17 item 4). The latest end wins when there are several records.
        /// </summary>
        private bool CautionUntil(IntelRow row, DateTime today, out DateTime until)
        {
            until = default(DateTime);
            if (row.IsBankrupt || row.IsDormant) return false;

            List<CautionEntry> cautions = State.cautions;
            if (cautions == null) return false;

            bool found = false;
            foreach (CautionEntry entry in cautions)
            {
                DateTime end;
                if (entry == null || entry.uid != row.UniqueId || !RivalDates.TryParse(entry.until, out end)) continue;
                if (today.Date >= end.Date) continue;   // the caution lasts while today < until (CautionCleaner)
                if (!found || end > until)
                {
                    until = end;
                    found = true;
                }
            }
            return found;
        }

        // ---- the history ----

        private sealed class HistoryItem
        {
            public DateTime Date;
            public int Order;
            public string Text;
            public string Delta;
            public IntelTone Tone;
        }

        private IntelSection HistorySection(IntelRow row, IntelSettings settings)
        {
            var section = new IntelSection("history", T("rival.window.history"));
            var items = new List<HistoryItem>();
            int order = 0;

            foreach (AttitudeEntry entry in _ledger.History(row.UniqueId))
            {
                if (entry == null) continue;
                DateTime date;
                RivalDates.TryParse(entry.date, out date);
                items.Add(new HistoryItem
                {
                    Date = date,
                    Order = order++,
                    Text = ReasonText(entry.reason),
                    Delta = DeltaText(entry.delta),
                    Tone = entry.delta > 0f ? IntelTone.Good : (entry.delta < 0f ? IntelTone.Bad : IntelTone.Normal),
                });
            }

            List<PendingReaction> pending = _reactions.PendingSnapshot();
            foreach (HistoryEntry entry in _reactions.HistoryOf(row.UniqueId, -1))
            {
                if (IsHiddenPlan(entry, pending)) continue;
                DateTime date;
                RivalDates.TryParse(entry.date, out date);
                items.Add(new HistoryItem { Date = date, Order = order++, Text = OutcomeText(entry), Delta = null, Tone = IntelTone.Normal });
            }

            // oldest first by the day (the order they were written in breaks ties), the last ten, newest first
            items.Sort((a, b) =>
            {
                int byDay = a.Date.CompareTo(b.Date);
                return byDay != 0 ? byDay : a.Order.CompareTo(b.Order);
            });
            int start = Math.Max(0, items.Count - HistoryLines);
            for (int i = items.Count - 1; i >= start; i--)
            {
                HistoryItem item = items[i];
                string date = item.Date == default(DateTime) ? Dash : _text.Date(item.Date);
                string text = item.Delta != null ? T("rival.window.historyDelta", date, item.Text, item.Delta) : T("rival.window.historyLine", date, item.Text);
                section.Add(new IntelLine(null, text, item.Tone));
            }

            if (section.Lines.Count == 0) section.Add(new IntelLine(null, T("rival.window.none"), IntelTone.Muted));
            return section;
        }

        /// <summary>
        /// The line "planned" of a reaction whose mail has not come yet: the player has not been told, so the history does not tell him either.
        /// </summary>
        private static bool IsHiddenPlan(HistoryEntry entry, List<PendingReaction> pending)
        {
            if (entry == null || !string.Equals(entry.outcome, HistoryOutcomes.Planned, StringComparison.Ordinal)) return false;
            foreach (PendingReaction item in pending)
            {
                if (item.uid == entry.uid && item.segment == entry.segment && string.Equals(item.type, entry.type, StringComparison.Ordinal)
                    && string.Equals(item.plannedOn, entry.date, StringComparison.Ordinal) && !string.IsNullOrEmpty(item.id) && string.IsNullOrEmpty(item.fireOn))
                {
                    return true;
                }
            }
            return false;
        }

        private string OutcomeText(HistoryEntry entry)
        {
            string outcome = entry.outcome ?? string.Empty;
            bool auto = outcome.EndsWith(HistoryOutcomes.AutoSuffix, StringComparison.Ordinal);
            if (auto) outcome = outcome.Substring(0, outcome.Length - HistoryOutcomes.AutoSuffix.Length);

            string name = ReactionLabel(entry.type, entry.segment);
            string text;
            switch (outcome)
            {
                case HistoryOutcomes.Planned:
                case HistoryOutcomes.Started:
                case HistoryOutcomes.Ended:
                case HistoryOutcomes.Cancelled:
                case HistoryOutcomes.CounterLaunchDone:
                case HistoryOutcomes.Revealed:
                case HistoryOutcomes.CounterOffer:
                case HistoryOutcomes.LetGo:
                    text = T("rival.history." + outcome, name);
                    break;
                default:
                    text = T("rival.history.other", outcome.Length == 0 ? Dash : outcome);
                    break;
            }
            return auto ? text + " " + T("rival.history.auto") : text;
        }

        /// <summary>The words for the reason written with a change of the attitude (<see cref="TriggerRecorder.ReasonOf"/>, the contract moves, an event, another feature).</summary>
        private string ReasonText(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return T("rival.history.other", Dash);

            int colon = reason.IndexOf(':');
            string head = colon < 0 ? reason : reason.Substring(0, colon);
            string tail = colon < 0 ? string.Empty : reason.Substring(colon + 1);

            TriggerType trigger;
            if (ReactionKinds.TryParseTrigger(head, out trigger))
            {
                int segment;
                string segmentName = int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out segment) ? SegmentName(segment) : string.Empty;
                return T("rival.trigger." + head, segmentName.Length == 0 ? Dash : segmentName);
            }

            switch (head)
            {
                case "BusinessSigned": return T("rival.history.businessSigned");
                case "BusinessBrokenByPlayer": return T("rival.history.businessBroken");
                case "BusinessActiveMonthly": return T("rival.history.businessActive");
                case "event": return T("rival.history.event");
                case AcquisitionRules.HostileDealReason: return T("rival.history.hostileDeal");
                default: return T("rival.history.other", reason);
            }
        }

        // ---- small helpers ----

        private string T(string key, params object[] args)
        {
            return _text.L(key, args);
        }

        private string SegmentName(int segment)
        {
            return segment >= 0 && segment < ShareCalculator.SegmentCount ? T("rival.segment." + segment.ToString(CultureInfo.InvariantCulture)) : string.Empty;
        }

        private string SegmentsOf(IntelCompanyFacts facts)
        {
            var names = new List<string>();
            for (int i = 0; i < facts.Segments.Length && i < ShareCalculator.SegmentCount; i++)
            {
                if (facts.Segments[i]) names.Add(SegmentName(i));
            }
            return names.Count == 0 ? Dash : string.Join(", ", names.ToArray());
        }

        private string ShareOf(IntelCompanyFacts facts, bool fog, RivalConfig config)
        {
            if (!facts.HasShare) return Dash;
            return IntelFog.FormatShare(facts.Share, fog, config.intelFog.shareStepPp, Approx);
        }

        private string LastCpuOf(IntelCompanyFacts facts, bool fog, RivalConfig config)
        {
            if (!facts.HasLastCpu) return Dash;
            string ips = IntelFog.FormatIps(facts.LastCpuIps, fog, config.intelFog.ipsSignificantDigits, _text.Ips, Approx);
            return T("rival.window.lastCpuValue", facts.LastCpuName ?? string.Empty, facts.LastCpuYear.ToString(CultureInfo.InvariantCulture), ips);
        }

        private string Approx(string number)
        {
            return T("rival.window.fog", number);
        }

        /// <summary>The name of a reaction with its segment, as the player reads it: <c>Price cut, Mid Range</c>. Empty for a type that is not known.</summary>
        private string ReactionLabel(string typeName, int segment)
        {
            ReactionType type;
            if (!ReactionKinds.TryParseReaction(typeName, out type)) return string.Empty;

            string name = T("rival.reaction." + KeyNameOf(type));
            string segmentName = SegmentName(segment);
            return segmentName.Length == 0 ? name : name + ", " + segmentName;
        }

        private static string KeyNameOf(ReactionType type)
        {
            string name = ReactionKinds.NameOf(type);
            return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static IntelTone ToneOf(AttitudeLevel level)
        {
            switch (level)
            {
                case AttitudeLevel.Hostile:
                case AttitudeLevel.Rivalry:
                    return IntelTone.Bad;
                case AttitudeLevel.Respect:
                case AttitudeLevel.Partnership:
                    return IntelTone.Good;
                default:
                    return IntelTone.Normal;
            }
        }

        /// <summary>The attitude as a whole number with its sign: <c>+12</c>, <c>-15</c>, <c>0</c>.</summary>
        public static string SignedWhole(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            double rounded = Math.Round((double)value, MidpointRounding.AwayFromZero);
            if (rounded == 0.0) return "0";
            string text = rounded.ToString("0", CultureInfo.InvariantCulture);
            return rounded > 0.0 ? "+" + text : text;
        }

        /// <summary>A change of the attitude as the history writes it: <c>-9.6</c>, <c>+4</c>.</summary>
        public static string DeltaText(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return "0";
            return ((double)delta).ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
        }

        private static string WholeText(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Dash;
            return Math.Round((double)value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
