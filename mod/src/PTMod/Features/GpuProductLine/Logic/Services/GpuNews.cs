using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A letter the news hand to the mail service of the core: the keys of the texts with their arguments, the tag and the priority (the game side is <c>ModMail.Send</c>).</summary>
    public sealed class GpuMailLetter
    {
        /// <summary><c>gpu:era:&lt;eraId&gt;</c>, <c>gpu:segment:&lt;segmentId&gt;</c>, <c>gpu:vendor:&lt;vendorId&gt;:&lt;enter|exit|merge&gt;</c>, <c>gpu:leader:&lt;segmentId&gt;</c>.</summary>
        public string Tag = string.Empty;

        public string TitleKey = string.Empty;
        public string BodyKey = string.Empty;

        /// <summary>The arguments of both texts, already put into words ({0}, {1}, ...).</summary>
        public string[] Args = new string[0];

        public MailPriority Priority = MailPriority.Normal;

        /// <summary>The answer period in days counted from the delivery; 0 - none (the letters of the news have none; an offer of a console order has <c>offerTimeoutDays</c>).</summary>
        public int TimeoutDays;

        /// <summary>The choice applied when the answer period passes; required with <see cref="TimeoutDays"/>.</summary>
        public string DefaultChoiceId = string.Empty;

        /// <summary>The one-shot answer buttons of the letter; null or empty - the letter has none.</summary>
        public List<GpuMailChoice> Choices;

        /// <summary>The reusable buttons that never close the letter; null or empty - none.</summary>
        public List<GpuMailAction> Actions;
    }

    /// <summary>An answer button of a letter (<c>ModMailChoice</c> on the game side): its text, its hint and whether it can be pressed now.</summary>
    public sealed class GpuMailChoice
    {
        public string Id = string.Empty;
        public string LabelKey = string.Empty;
        public string TooltipKey = string.Empty;
        public bool Enabled = true;

        /// <summary>The text key of why the button is gray; empty - no reason.</summary>
        public string DisabledReasonKey = string.Empty;
    }

    /// <summary>A button of a letter that calls the feature and leaves the letter open (<c>ModMailAction</c> on the game side).</summary>
    public sealed class GpuMailAction
    {
        public string Id = string.Empty;
        public string LabelKey = string.Empty;
        public string TooltipKey = string.Empty;
        public bool Repeatable = true;
    }

    /// <summary>
    /// The mail service as the news see it. The game side (<c>Bridges/GpuMailAdapter</c>) is <c>ModMail.Send</c> and <c>ModMail.Find</c>: the core gives out the ids of the letters, delivers them
    /// by priority and keeps the daily limits (FR-14 item 7); the feature chooses neither an id nor a limit.
    /// </summary>
    public interface IGpuMail
    {
        /// <summary>Sends the letter; a negative number when the service did not take it.</summary>
        int Send(GpuMailLetter letter);

        /// <summary>A letter of the feature with exactly this tag was sent before (delivered or still waiting).</summary>
        bool Exists(string tag);
    }

    /// <summary>
    /// The news of the GPU market (design.md, "GpuEras, GpuRivals, GpuRivalGenerator, GpuNews"; FR-14): letters in the inbox of the game about what the calendar and the makers do.
    /// <list type="bullet">
    /// <item><b>Eras and segments</b> (<c>Normal</c>): an era begins (<c>gpu:era:&lt;eraId&gt;</c>; the title is the name of the era, the text is its own, with the nodes of the tree to research), or a
    /// segment opens in a year no era begins in (<c>gpu:segment:&lt;segmentId&gt;</c>: the requirements of the year and the node that gives the features). They come whatever the switch <c>news</c> says.</item>
    /// <item><b>Makers</b> (<c>Normal</c>, <c>gpu:vendor:&lt;vendorId&gt;:&lt;enter|exit|merge&gt;</c>): a maker enters the market, leaves it (its year, or the bankruptcy of its company) or is absorbed.
    /// FR-14 item 5 names only the letters about leaders as the ones the switch turns off, so these come too.</item>
    /// <item><b>A new leader</b> (<c>Low</c>, <c>gpu:leader:&lt;segmentId&gt;</c>): a card a maker has just launched is the fastest on sale in its segment; no more than one letter per
    /// <c>news.leaderCooldownMonths</c> (3) months for a segment, the date of the last one is kept in the save (<c>news.lastLeaderMail</c>). Only with the switch <c>news</c> on.</item>
    /// </list>
    /// <para>
    /// <b>Quiet times.</b> No letter is made while the new game is fast-forwarded or a save loads, and none is put in a queue then (<see cref="Silently"/> and the <c>canSend</c> test): the
    /// start of a session catches the makers up with the calendar without a word. The same goes for the events of the past (FR-14 item 6): the events carry the 1st of January of the year of
    /// the content, and a letter is made only when that day is not earlier than the 1st of the previous calendar month (<see cref="IsFresh"/>) - a feature that was off for years brings its
    /// state up to date on the first month and sends nothing about the years in between. A tag that was sent once is not sent again (<see cref="IGpuMail.Exists"/>), except the tag of a leader.
    /// </para>
    /// </summary>
    public sealed class GpuNews
    {
        public const string TagEra = "gpu:era:";
        public const string TagSegment = "gpu:segment:";
        public const string TagVendor = "gpu:vendor:";
        public const string TagLeader = "gpu:leader:";
        public const string Enter = "enter";
        public const string Exit = "exit";
        public const string Merge = "merge";

        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;
        private readonly GpuEras _eras;
        private readonly GpuRivals _rivals;
        private readonly IGpuMail _mail;
        private readonly IGpuText _text;
        private readonly Func<bool> _leadersEnabled;
        private readonly Func<bool> _canSend;
        private readonly Action<string, Action> _guard;
        private readonly Action<string> _warn;
        private int _silent;
        private bool _bound;

        /// <param name="eras">The calendar of the segments and the eras whose events become letters.</param>
        /// <param name="rivals">The makers whose entries, exits, mergers and launches become letters.</param>
        /// <param name="mail">The mail service of the core.</param>
        /// <param name="text">The words and the numbers in the language of the player.</param>
        /// <param name="leadersEnabled">The switch <c>news</c> of the hub (the letters about leaders); null means on.</param>
        /// <param name="canSend">False while the game is not in its normal course (the new game is fast-forwarded, a save loads, the game is not ready); null means yes.</param>
        /// <param name="guard">Runs a handler of an event as <c>(site, body)</c> under the guard of the feature: an exception reaches the log and the count of the feature and goes no further, so
        /// the month that raised the event goes on. Null runs the body as it is.</param>
        /// <param name="warn">English lines for the log; may be null.</param>
        public GpuNews(GpuContentDb content, GpuState state, IGpuGame game, GpuEras eras, GpuRivals rivals, IGpuMail mail, IGpuText text,
            Func<bool> leadersEnabled, Func<bool> canSend, Action<string, Action> guard, Action<string> warn)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            if (eras == null) throw new ArgumentNullException("eras");
            if (rivals == null) throw new ArgumentNullException("rivals");
            if (mail == null) throw new ArgumentNullException("mail");
            if (text == null) throw new ArgumentNullException("text");
            _content = content;
            _state = state;
            _game = game;
            _eras = eras;
            _rivals = rivals;
            _mail = mail;
            _text = text;
            _leadersEnabled = leadersEnabled;
            _canSend = canSend;
            _guard = guard;
            _warn = warn;
        }

        /// <summary>The letters about leaders are on (the switch <c>news</c>).</summary>
        public bool LeadersEnabled
        {
            get { return _leadersEnabled == null || _leadersEnabled(); }
        }

        /// <summary>No letter is made now: the session starts, or the core says the game is not in its normal course.</summary>
        public bool IsSilent
        {
            get { return _silent > 0 || (_canSend != null && !_canSend()); }
        }

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        // ---- the subscriptions ----

        /// <summary>Listens to the eras and to the makers; once (a second call does nothing).</summary>
        public void Bind()
        {
            if (_bound) return;
            _bound = true;
            _eras.EraStarted += OnEraStarted;
            _eras.SegmentOpened += OnSegmentOpened;
            _rivals.Entered += OnEntered;
            _rivals.Exited += OnExited;
            _rivals.Merged += OnMerged;
            _rivals.AI.Launched += OnLaunched;
        }

        /// <summary>Stops listening.</summary>
        public void Unbind()
        {
            if (!_bound) return;
            _bound = false;
            _eras.EraStarted -= OnEraStarted;
            _eras.SegmentOpened -= OnSegmentOpened;
            _rivals.Entered -= OnEntered;
            _rivals.Exited -= OnExited;
            _rivals.Merged -= OnMerged;
            _rivals.AI.Launched -= OnLaunched;
        }

        /// <summary>
        /// Runs <paramref name="body"/> without letters (FR-14 item 4): the start of a session (a load, a new game, the feature switched on in the middle of a game) brings the makers up to
        /// date, and what that raises - a maker whose company is gone - is not worth a letter. Nests.
        /// </summary>
        public void Silently(Action body)
        {
            if (body == null) return;
            _silent++;
            try
            {
                body();
            }
            finally
            {
                _silent--;
            }
        }

        /// <summary>
        /// FR-14 item 6: an event is fresh when it is not earlier than the 1st of the previous calendar month. The catch-up of a feature that was off raises the events of the years it
        /// missed; their dates are old, so there is no letter for them.
        /// </summary>
        public static bool IsFresh(DateTime eventDate, DateTime today)
        {
            DateTime thisMonth = new DateTime(today.Year, today.Month, 1);
            DateTime limit = thisMonth > DateTime.MinValue.AddMonths(1) ? thisMonth.AddMonths(-1) : thisMonth;
            return eventDate >= limit;
        }

        // ---- the eras and the segments ----

        private void OnEraStarted(GpuEraDto era)
        {
            Run("news/era", () => SendEra(era));
        }

        private void SendEra(GpuEraDto era)
        {
            if (era == null || !CanSendAbout(FirstOfYear(era.startYear))) return;
            Offer(new GpuMailLetter
            {
                Tag = TagEra + era.id,
                TitleKey = era.nameKey,
                BodyKey = era.mailKey,
                Priority = MailPriority.Normal,
            }, true);
        }

        private void OnSegmentOpened(GpuSegmentDto segment)
        {
            Run("news/segment", () => SendSegment(segment));
        }

        private void SendSegment(GpuSegmentDto segment)
        {
            if (segment == null || !CanSendAbout(FirstOfYear(segment.opensYear))) return;
            if (EraBeginsIn(segment.opensYear)) return;   // the letter of the era speaks for the segments that open with it

            DateTime today = _game.Today;
            string[] required = _eras.RequiredFeatures(segment, today.Year);
            float vram = _eras.VramRequirement(segment, today.Year + (today.Month - 1) / 12f);
            string details = GpuNames.Requirements(_text, required, vram) + ".";
            string hint = HintFor(required);
            if (hint.Length > 0) details += " " + hint;

            Offer(new GpuMailLetter
            {
                Tag = TagSegment + segment.id,
                TitleKey = "gpu.mail.segment.subject",
                BodyKey = "gpu.mail.segment.body",
                Args = new[] { GpuNames.Localized(_text, segment.nameKey, segment.id), details },
                Priority = MailPriority.Normal,
            }, true);
        }

        private bool EraBeginsIn(int year)
        {
            if (!_eras.IsErasMode) return false;
            foreach (GpuEraDto era in _content.Eras)
            {
                if (era.startYear == year) return true;
            }
            return false;
        }

        // "Исследуйте: <node>.": the earliest architecture that has every feature the segment asks for, and the node of the tree that gives it; failing that, a node for each feature.
        private string HintFor(string[] required)
        {
            if (required == null || required.Length == 0) return string.Empty;

            var nodeIds = new List<string>();
            GpuArchitectureDto all = null;
            foreach (GpuArchitectureDto arch in _content.Architectures)
            {
                if (HasAll(arch.features, required) && (all == null || arch.year < all.year)) all = arch;
            }
            if (all != null)
            {
                AddNodeOf(all, nodeIds);
            }
            else
            {
                foreach (string feature in required)
                {
                    GpuArchitectureDto first = null;
                    foreach (GpuArchitectureDto arch in _content.Architectures)
                    {
                        if (arch.features != null && arch.features.Contains(feature) && (first == null || arch.year < first.year)) first = arch;
                    }
                    if (first != null) AddNodeOf(first, nodeIds);
                }
            }
            return nodeIds.Count == 0 ? string.Empty : _text.L("gpu.mail.hint", GpuNames.Nodes(_content, _text, nodeIds));
        }

        private void AddNodeOf(GpuArchitectureDto arch, List<string> nodeIds)
        {
            foreach (GpuResearchNodeDto node in _content.ResearchNodes)
            {
                if (string.Equals(node.unlocks, arch.id, StringComparison.Ordinal))
                {
                    if (!nodeIds.Contains(node.id)) nodeIds.Add(node.id);
                    return;
                }
            }
        }

        private static bool HasAll(List<string> have, string[] required)
        {
            if (have == null) return false;
            foreach (string need in required)
            {
                if (!have.Contains(need)) return false;
            }
            return true;
        }

        // ---- the makers ----

        private void OnEntered(GpuVendorRuntime vendor)
        {
            Run("news/vendor/enter", () => SendEntered(vendor));
        }

        private void SendEntered(GpuVendorRuntime vendor)
        {
            if (vendor == null || !CanSendAbout(FirstOfYear(vendor.Def.entryYear))) return;

            DateTime today = _game.Today;
            var segments = new List<string>();
            foreach (string id in _content.ActiveSegmentIds(vendor.Def))
            {
                GpuSegmentDto segment = _content.Segment(id);
                if (segment != null && GpuEras.IsOpen(segment, today)) segments.Add(GpuNames.Localized(_text, segment.nameKey, segment.id));
            }
            string series = GpuNames.Series(_text, vendor.Def);
            if (string.IsNullOrWhiteSpace(series)) series = _rivals.NameOf(vendor, _text.IsRussian);

            Offer(new GpuMailLetter
            {
                Tag = TagVendor + vendor.Id + ":" + Enter,
                TitleKey = "gpu.mail.vendor.enter.subject",
                BodyKey = "gpu.mail.vendor.enter.body",
                Args = new[] { _rivals.NameOf(vendor, _text.IsRussian), series, segments.Count > 0 ? string.Join(", ", segments.ToArray()) : "-" },
                Priority = MailPriority.Normal,
            }, true);
        }

        private void OnExited(GpuVendorRuntime vendor)
        {
            Run("news/vendor/exit", () => SendExited(vendor));
        }

        private void SendExited(GpuVendorRuntime vendor)
        {
            if (vendor == null) return;
            DateTime today = _game.Today;
            if (!CanSendAbout(ExitDate(vendor.Def, today))) return;

            // a vendor bound to a company that went bankrupt: the company is the one in trouble, and the letter says so
            bool bankrupt = vendor.IsBound && _game.IsBankrupt(vendor.CompanyKey);
            Offer(new GpuMailLetter
            {
                Tag = TagVendor + vendor.Id + ":" + Exit,
                TitleKey = "gpu.mail.vendor.exit.subject",
                BodyKey = bankrupt ? "gpu.mail.vendor.exit.bankrupt" : "gpu.mail.vendor.exit.body",
                Args = new[] { _rivals.NameOf(vendor, _text.IsRussian) },
                Priority = MailPriority.Normal,
            }, true);
        }

        private void OnMerged(GpuVendorRuntime absorbed, GpuVendorRuntime absorber)
        {
            Run("news/vendor/merge", () => SendMerged(absorbed, absorber));
        }

        private void SendMerged(GpuVendorRuntime absorbed, GpuVendorRuntime absorber)
        {
            if (absorbed == null || absorber == null || !CanSendAbout(FirstOfYear(absorbed.Def.mergeYear))) return;
            Offer(new GpuMailLetter
            {
                Tag = TagVendor + absorbed.Id + ":" + Merge,
                TitleKey = "gpu.mail.vendor.merge.subject",
                BodyKey = "gpu.mail.vendor.merge.body",
                Args = new[] { _rivals.NameOf(absorber, _text.IsRussian), _rivals.NameOf(absorbed, _text.IsRussian) },
                Priority = MailPriority.Normal,
            }, true);
        }

        // The day of an exit: the end of the maker's years when they have ended, the year of the merger that could not be carried out, otherwise today (the bankruptcy of its company).
        private static DateTime ExitDate(GpuVendorDto def, DateTime today)
        {
            if (def.exitYear > 0 && def.exitYear <= today.Year) return FirstOfYear(def.exitYear);
            if (!string.IsNullOrEmpty(def.mergeInto) && def.mergeYear > 0 && def.mergeYear <= today.Year) return FirstOfYear(def.mergeYear);
            return today;
        }

        // ---- the leaders ----

        private void OnLaunched(GpuVendorRuntime vendor, GpuProductSave product)
        {
            Run("news/leader", () => SendLeader(vendor, product));
        }

        private void SendLeader(GpuVendorRuntime vendor, GpuProductSave product)
        {
            if (vendor == null || product == null || !LeadersEnabled || IsSilent) return;
            if (product.segIds == null || product.segIds.Count == 0 || product.stats == null) return;

            DateTime today = _game.Today;
            string segmentId = product.segIds[0];
            GpuSegmentDto segment = _content.Segment(segmentId);
            if (segment == null || !GpuEras.IsOpen(segment, today)) return;

            // "не чаще одного раза в 3 месяца на сегмент"
            GpuNewsSave news = NewsSave;
            GpuSegLaunch last = news.lastLeaderMail.Find(l => l != null && l.seg == segmentId);
            int cooldown = Math.Max(0, Balance.news.leaderCooldownMonths);
            if (last != null && last.date != null && today < DateOf(last.date).AddMonths(cooldown)) return;

            // the fastest on sale in the segment: the card has to be one that can sell there (a segment that wants a function does not buy a card without it for nothing)
            string[] required = _eras.RequiredFeatures(segment, today.Year);
            double mine = product.stats.effectivePerf;
            if (!(mine > 0d) || !CanSellIn(product, required, segment)) return;
            double bestOther = 0d;
            foreach (GpuProductSave other in _state.Data.products)
            {
                if (other == null || ReferenceEquals(other, product) || other.status != GpuState.StatusSelling || other.stats == null) continue;
                if (other.segIds == null || !other.segIds.Contains(segmentId) || !CanSellIn(other, required, segment)) continue;
                if (other.stats.effectivePerf > bestOther) bestOther = other.stats.effectivePerf;
            }
            if (!IsNewLeader(mine, bestOther)) return;

            string segmentName = GpuNames.Localized(_text, segment.nameKey, segment.id);
            string before = bestOther > 0d
                ? _text.L("gpu.mail.leader.vs", _text.L("gpu.design.val.mops", _text.Integer(bestOther)))
                : _text.L("gpu.mail.leader.alone");
            bool sent = Offer(new GpuMailLetter
            {
                Tag = TagLeader + segmentId,
                TitleKey = "gpu.mail.leader.subject",
                BodyKey = "gpu.mail.leader.body",
                Args = new[]
                {
                    segmentName,
                    _rivals.NameOf(vendor, _text.IsRussian),
                    product.name ?? string.Empty,
                    _text.L("gpu.design.val.mops", _text.Integer(mine)),
                    before,
                    _text.Money(product.price),
                },
                Priority = MailPriority.Low,
            }, false);
            if (!sent) return;

            if (last == null)
            {
                last = new GpuSegLaunch { seg = segmentId };
                news.lastLeaderMail.Add(last);
            }
            last.date = DateOf(today);
            last.extraMonths = 0;
        }

        /// <summary>A card is the fastest of its segment when it is strictly faster than every other card that can sell there: a tie leads nobody.</summary>
        public static bool IsNewLeader(double performance, double bestOther)
        {
            return performance > 0d && performance > bestOther;
        }

        private static bool CanSellIn(GpuProductSave card, string[] required, GpuSegmentDto segment)
        {
            return GpuMarketMath.FeatureFactor(card.stats.features, required, segment.missingFeatureFactor) > 0f;
        }

        private GpuNewsSave NewsSave
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.news == null) data.news = new GpuNewsSave();
                if (data.news.lastLeaderMail == null) data.news.lastLeaderMail = new List<GpuSegLaunch>();
                return data.news;
            }
        }

        // ---- sending ----

        // The common tests of a letter: not in a quiet time, and the event is not of the past.
        private bool CanSendAbout(DateTime eventDate)
        {
            if (IsSilent) return false;
            return IsFresh(eventDate, _game.Today);
        }

        /// <summary>Hands the letter to the mail service; false when it was not sent (it was sent before and <paramref name="once"/> is set, or the service did not take it).</summary>
        private bool Offer(GpuMailLetter letter, bool once)
        {
            if (once && _mail.Exists(letter.Tag)) return false;
            int id = _mail.Send(letter);
            if (id >= 0) return true;
            if (_warn != null) _warn("news: the mail service did not accept the letter " + letter.Tag);
            return false;
        }

        private void Run(string site, Action body)
        {
            if (_guard != null) _guard(site, body);
            else body();
        }

        // ---- dates ----

        private static DateTime FirstOfYear(int year)
        {
            return new DateTime(Math.Min(9999, Math.Max(1, year)), 1, 1);
        }

        private static GpuDate DateOf(DateTime date)
        {
            return new GpuDate { y = date.Year, m = date.Month, d = date.Day };
        }

        private static DateTime DateOf(GpuDate date)
        {
            return new DateTime(Math.Min(9999, Math.Max(1, date.y)), Math.Min(12, Math.Max(1, date.m)), 1).AddDays(Math.Max(1, date.d) - 1);
        }
    }
}
