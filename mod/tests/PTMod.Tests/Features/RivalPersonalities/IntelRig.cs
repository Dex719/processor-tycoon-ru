using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The texts of the panel from the shipped string tables of the feature, in one language, the way <c>ModL10n</c> reads them: a key that no table has comes
    /// out as <c>[key]</c> and is written to <see cref="Missing"/>. The date is <c>dd.MM.yyyy</c>, the <c>Ips</c> a whole number. Internal, so the runner never
    /// discovers it.
    /// </summary>
    internal sealed class TableIntelText : IIntelText
    {
        private readonly LocTextResolver _resolver;

        public readonly List<string> Missing = new List<string>();

        public TableIntelText(string language)
        {
            var tables = new L10nTables();
            var errors = new List<string>();
            tables.AddTable(language, "rival-personalities", RivalTestKit.ReadL10n(language), errors);
            Assert.Equal(0, errors.Count, "string table " + language + ": " + string.Join("; ", errors));

            _resolver = new LocTextResolver(tables, Missing.Add);
            _resolver.Language = language;
        }

        public string L(string key, params object[] args)
        {
            return _resolver.L(key, args);
        }

        public string Resolve(LocText text)
        {
            return _resolver.Resolve(text);
        }

        public string Date(DateTime date)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        public string Ips(float ips)
        {
            return Math.Round(ips).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The intelligence panel on the shipped content and a plain state: the thirteen companies with their characters (<see cref="AttitudeRig"/>), the books of
    /// the reactions on the same section, and a builder that speaks one language. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class IntelRig
    {
        public readonly AttitudeRig Base;
        public readonly ReactionBook Reactions;
        public readonly TableIntelText Text;
        public readonly IntelBuilder Builder;

        public IntelRig(string language = "ru")
        {
            Base = new AttitudeRig().AssignAll();
            Reactions = new ReactionBook(() => Base.State);
            Text = new TableIntelText(language);
            Builder = new IntelBuilder(Base.Book, Base.Ledger, Reactions, () => Base.State, () => Base.Catalog, Text);
        }

        public DateTime Today
        {
            get { return Base.Today; }
            set { Base.Today = value; }
        }

        public RivalState State
        {
            get { return Base.State; }
        }

        public IntelSettings Settings(bool hidden, bool fog)
        {
            return new IntelSettings { Today = Base.Today, HiddenMode = hidden, Fog = fog };
        }

        /// <summary>The facts of a company with a name; the segments, the share and the last CPU are set by the caller.</summary>
        public static IntelCompanyFacts Facts(int uniqueId, string name, bool bankrupt = false)
        {
            return new IntelCompanyFacts { UniqueId = uniqueId, Name = name, IsBankrupt = bankrupt };
        }

        /// <summary>AMD as the game could tell it: Mid Range and Low End, 17.3 % of the market, a last CPU of 123 Ips.</summary>
        public static IntelCompanyFacts Amd()
        {
            IntelCompanyFacts facts = Facts(AttitudeRig.Amd, "AMD");
            facts.Segments[1] = true;
            facts.Segments[2] = true;
            facts.HasShare = true;
            facts.Share = 0.173f;
            facts.HasLastCpu = true;
            facts.LastCpuName = "Am386";
            facts.LastCpuYear = 1991;
            facts.LastCpuIps = 123f;
            return facts;
        }

        /// <summary>A reaction that was planned, whose mail was delivered on <paramref name="deliveredOn"/>, to be carried out <paramref name="warningDays"/> later.</summary>
        public PendingReaction Pend(int uniqueId, ReactionType type, Segment segment, DateTime deliveredOn, int warningDays)
        {
            string id = Reactions.NextId();
            var pending = new PendingReaction
            {
                id = id,
                uid = uniqueId,
                type = ReactionKinds.NameOf(type),
                segment = (int)segment,
                trigger = "StrongLaunch",
                plannedOn = RivalDates.ToText(deliveredOn),
                mailId = 100000 + State.pending.Count,
                telegraphDays = warningDays,
                fireOn = RivalDates.ToText(deliveredOn.AddDays(warningDays)),
            };
            Assert.True(Reactions.AddPending(pending));
            return pending;
        }

        /// <summary>The lines of a section as <c>label: text</c> (or the text alone), for a short check.</summary>
        public static List<string> TextsOf(IntelSection section)
        {
            var texts = new List<string>();
            foreach (IntelLine line in section.Lines) texts.Add(line.Label != null ? line.Label + ": " + line.Text : line.Text);
            return texts;
        }
    }
}
