using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>One entry of the log as a row of the window: the date, the subject (the CPU and the contract), the sentence and its colour.</summary>
    public sealed class LogRowText
    {
        /// <summary>The date as the log stores it, <c>yyyy-MM-dd</c>; the window writes it the way the game writes dates.</summary>
        public string Date = string.Empty;

        /// <summary>The CPU the entry is about; empty for none.</summary>
        public string Cpu = string.Empty;

        /// <summary>The contract for people; empty for none.</summary>
        public string Contract = string.Empty;

        /// <summary>The sentence: why the entry is there (<c>ct.reason.*</c>, with its numbers).</summary>
        public PanelLine Sentence;

        public LineTone Tone;
    }

    /// <summary>
    /// What the window of the log says about an entry (FR-8 items 1 and 3): the row of the list, its colour, and the lines of the details panel with every number the entry carries. The entry keeps a key of
    /// the string table and the numbers as text; this class turns them into <see cref="PanelLine"/> with the right kind of argument (money, a share, a count, a key), so the window prints a margin as
    /// a margin and a payment as money. The reasons the other parts of the feature write are the ones understood here (<c>ct.reason.*</c>, <c>ct.tender.*</c>); an unknown key is shown as a plain line.
    /// </summary>
    public static class LogLines
    {
        public const string TypeKeyPrefix = "ct.log.type.";
        public const string SourceKeyPrefix = "ct.log.source.";
        public const string OutcomeKeyPrefix = "ct.log.outcome.";
        public const string DataKeyPrefix = "ct.log.data.";
        public const string DecisionKeyPrefix = "ct.log.decision.";
        public const string RequirementNameKeyPrefix = "ct.req.name.";

        // ---- the row ----

        /// <summary>The row of the list for an entry.</summary>
        public static LogRowText Row(LogEntry entry)
        {
            var row = new LogRowText();
            if (entry == null)
            {
                row.Sentence = new PanelLine(string.Empty, LineTone.Muted);
                return row;
            }

            row.Date = entry.date ?? string.Empty;
            row.Cpu = entry.cpuName ?? string.Empty;
            row.Contract = entry.contractLabel ?? string.Empty;
            row.Sentence = Sentence(entry);
            row.Tone = ToneOf(entry);
            return row;
        }

        /// <summary>The sentence of an entry: its reason key with the arguments that key takes.</summary>
        public static PanelLine Sentence(LogEntry entry)
        {
            LineTone tone = ToneOf(entry);
            if (entry == null) return new PanelLine(string.Empty, tone);

            if (entry.type == LogTypes.TenderResult) return TenderSentence(entry, tone);
            if (entry.type == LogTypes.BusinessSigned) return BusinessSentence(entry, tone);

            switch (entry.reasonKey)
            {
                case "ct.reason.margin":
                    return new PanelLine(entry.reasonKey, tone,
                        PanelArg.Fraction(entry.GetDouble(LogDataKeys.Margin, 0)),
                        PanelArg.Fraction(entry.GetDouble(LogDataKeys.Threshold, 0)),
                        PanelArg.Money(entry.GetDouble(LogDataKeys.Payment, 0)),
                        PanelArg.Money(entry.GetDouble(LogDataKeys.UnitCost, 0)));

                case "ct.reason.requirements":
                    return new PanelLine(entry.reasonKey, tone, PanelArg.OfKeys(FailedKeys(entry.Get(LogDataKeys.Failed))));

                case "ct.reason.automationSummary":
                    return new PanelLine(entry.reasonKey, tone,
                        PanelArg.Whole(entry.GetInt(LogDataKeys.Sent, 0)),
                        PanelArg.Whole(entry.GetInt(LogDataKeys.Skipped, 0)),
                        PanelArg.Whole(entry.GetInt(LogDataKeys.SkippedMargin, 0)),
                        PanelArg.Whole(entry.GetInt(LogDataKeys.SkippedRequirements, 0)));

                case "ct.reason.mismatch":
                    return new PanelLine(entry.reasonKey, tone,
                        PanelArg.OfKey(PredictedKey(entry.Get(LogDataKeys.Predicted))),
                        PanelArg.OfKey(ObservedKey(entry.Get(LogDataKeys.Observed))));

                case "ct.reason.autoReject":
                    return new PanelLine(entry.reasonKey, tone,
                        PanelArg.Whole(entry.GetInt(LogDataKeys.Required, 0)),
                        PanelArg.Whole(entry.GetInt(LogDataKeys.Contracted, 0)),
                        PanelArg.Whole(entry.GetInt(LogDataKeys.Capacity, 0)));

                default:
                    return new PanelLine(string.IsNullOrEmpty(entry.reasonKey) ? TypeKey(entry.type) : entry.reasonKey, tone);
            }
        }

        private static PanelLine TenderSentence(LogEntry entry, LineTone tone)
        {
            string outcome = entry.Get(LogDataKeys.Outcome);
            string key = outcome == TenderOutcomeClassifier.OutcomeWon ? "ct.log.row.tender.won"
                : outcome == TenderOutcomeClassifier.OutcomeVoid ? "ct.log.row.tender.void"
                : "ct.log.row.tender.lost";
            return new PanelLine(key, tone,
                PanelArg.Whole(entry.GetInt(LogDataKeys.Chance, 0)),
                PanelArg.Actual(entry.GetDouble(LogDataKeys.Actual, 0)),
                PanelArg.OfText(entry.Get(LogDataKeys.Winner)),
                PanelArg.OfText(entry.Get(LogDataKeys.WinnerCpu)),
                PanelArg.OfKey(entry.reasonKey));
        }

        private static PanelLine BusinessSentence(LogEntry entry, LineTone tone)
        {
            return new PanelLine("ct.reason.businessSigned", tone,
                PanelArg.OfText(entry.Get(LogDataKeys.Provider)),
                PanelArg.OfText(entry.Get(LogDataKeys.Client)),
                PanelArg.Whole(entry.GetInt(LogDataKeys.Years, 0)),
                PanelArg.Percent(entry.GetDouble(LogDataKeys.Share, 0)),
                PanelArg.Percent(entry.GetDouble(LogDataKeys.Markup, 0)));
        }

        /// <summary>The colour of an entry: green for what went out or was won, red for what was turned down or lost, yellow for a skip by margin and for disagreements, dimmed for the rest.</summary>
        public static LineTone ToneOf(LogEntry entry)
        {
            if (entry == null) return LineTone.Muted;

            switch (entry.type)
            {
                case LogTypes.OfferSent:
                case LogTypes.OfferAutoConfirmed:
                case LogTypes.BusinessSigned:
                    return LineTone.Good;
                case LogTypes.OfferCancelledOnReview:
                    return LineTone.Muted;
                case LogTypes.AutoRejected:
                    return LineTone.Bad;
                case LogTypes.Mismatch:
                    return LineTone.Warn;
                case LogTypes.AutomationSummary:
                    return LineTone.Normal;
                case LogTypes.TenderResult:
                    string outcome = entry.Get(LogDataKeys.Outcome);
                    return outcome == TenderOutcomeClassifier.OutcomeWon ? LineTone.Good
                        : outcome == TenderOutcomeClassifier.OutcomeVoid ? LineTone.Muted
                        : LineTone.Bad;
                case LogTypes.AutomationDecision:
                    switch (entry.reasonKey)
                    {
                        case "ct.reason.sent": return LineTone.Good;
                        case "ct.reason.margin": return LineTone.Warn;
                        case "ct.reason.requirements": return LineTone.Bad;
                        case "ct.reason.mismatch": return LineTone.Warn;
                        default: return LineTone.Muted;
                    }
                default:
                    return LineTone.Normal;
            }
        }

        // ---- the details ----

        /// <summary>The lines of the details panel for an entry: when, what, who, the sentence, then every number the entry carries with its name.</summary>
        public static List<PanelLine> Details(LogEntry entry)
        {
            var lines = new List<PanelLine>();
            if (entry == null) return lines;

            lines.Add(new PanelLine("ct.log.d.date", LineTone.Normal, PanelArg.OfText(entry.date)));
            lines.Add(new PanelLine("ct.log.d.type", LineTone.Normal, PanelArg.OfKey(TypeKey(entry.type))));
            if (!string.IsNullOrEmpty(entry.source)) lines.Add(new PanelLine("ct.log.d.source", LineTone.Normal, PanelArg.OfKey(SourceKeyPrefix + entry.source)));
            if (!string.IsNullOrEmpty(entry.contractLabel)) lines.Add(new PanelLine("ct.log.d.contract", LineTone.Normal, PanelArg.OfText(entry.contractLabel)));
            if (!string.IsNullOrEmpty(entry.cpuName)) lines.Add(new PanelLine("ct.log.d.cpu", LineTone.Normal, PanelArg.OfText(entry.cpuName)));

            PanelLine sentence = Sentence(entry);
            if (!string.IsNullOrEmpty(sentence.Key)) lines.Add(sentence.AsBold());

            if (entry.data != null)
            {
                foreach (KeyValuePair<string, string> pair in entry.data)
                {
                    lines.Add(new PanelLine("ct.log.d.data", LineTone.Muted, PanelArg.OfKey(DataKeyPrefix + pair.Key), ValueOf(pair.Key, pair.Value)).Indented(1));
                }
            }
            return lines;
        }

        /// <summary>
        /// The lines of the details panel for the result of a tender: the outcome and its reason, the player's CPU and its chances, the winner, the table of the offers in the order of the draw and the
        /// offers the game withdrew before it (FR-5, FR-7).
        /// </summary>
        public static List<PanelLine> TenderDetails(TenderResultDto tender)
        {
            var lines = new List<PanelLine>();
            if (tender == null) return lines;

            lines.Add(new PanelLine("ct.log.t.result", ToneOfOutcome(tender.outcome),
                PanelArg.OfKey(OutcomeKeyPrefix + tender.outcome), PanelArg.OfKey(tender.reasonKey)).AsBold());
            if (!string.IsNullOrEmpty(tender.playerCpuName))
            {
                lines.Add(new PanelLine("ct.log.t.player", LineTone.Normal,
                    PanelArg.OfText(tender.playerCpuName), PanelArg.Whole(tender.playerGameChance), PanelArg.Actual(tender.playerActual)));
            }
            if (!string.IsNullOrEmpty(tender.winnerCompany) || !string.IsNullOrEmpty(tender.winnerCpuName))
            {
                lines.Add(new PanelLine("ct.log.t.winner", LineTone.Normal, PanelArg.OfText(tender.winnerCompany), PanelArg.OfText(tender.winnerCpuName)));
            }
            if (tender.capacityNotChecked) lines.Add(new PanelLine("ct.log.t.capacityNotChecked", LineTone.Warn));

            if (tender.offers != null && tender.offers.Count > 0)
            {
                lines.Add(new PanelLine("ct.log.t.offers", LineTone.Normal).AsBold());
                foreach (OfferRowDto offer in tender.offers)
                {
                    if (offer == null) continue;
                    bool winner = tender.winnerCpuName != null && offer.cpu == tender.winnerCpuName && offer.company == tender.winnerCompany;
                    PanelLine row = new PanelLine("ct.log.t.offer", winner ? LineTone.Good : offer.player ? LineTone.Normal : LineTone.Muted,
                        PanelArg.OfText(offer.company), PanelArg.OfText(offer.cpu), PanelArg.Ips(offer.ips), PanelArg.Whole(offer.chance), PanelArg.Actual(offer.actual)).Indented(1);
                    row.Bold = offer.player;
                    lines.Add(row);
                }
            }

            if (tender.pruned != null && tender.pruned.Count > 0)
            {
                lines.Add(new PanelLine("ct.log.t.pruned", LineTone.Normal).AsBold());
                foreach (PrunedRowDto pruned in tender.pruned)
                {
                    if (pruned == null) continue;
                    string maker = string.IsNullOrEmpty(pruned.maker) ? string.Empty : pruned.maker;
                    PanelLine row = new PanelLine(string.IsNullOrEmpty(maker) ? "ct.log.t.prunedOwn" : "ct.log.t.prunedMaker", pruned.player ? LineTone.Bad : LineTone.Muted,
                        PanelArg.OfText(pruned.company), PanelArg.OfText(pruned.cpu),
                        PanelArg.Whole(pruned.required), PanelArg.Whole(pruned.contracted), PanelArg.Whole(pruned.capacity), PanelArg.OfText(maker)).Indented(1);
                    row.Bold = pruned.player;
                    lines.Add(row);
                }
            }
            return lines;
        }

        private static LineTone ToneOfOutcome(string outcome)
        {
            return outcome == TenderOutcomeClassifier.OutcomeWon ? LineTone.Good
                : outcome == TenderOutcomeClassifier.OutcomeVoid ? LineTone.Muted
                : LineTone.Bad;
        }

        // ---- keys and values ----

        /// <summary>The key of the name of an entry type (<c>ct.log.type.OfferSent</c>).</summary>
        public static string TypeKey(string type)
        {
            return TypeKeyPrefix + (type ?? string.Empty);
        }

        /// <summary>The keys of the names of the filters of the window, in the order of <see cref="LogFilter"/>.</summary>
        public static string FilterKey(LogFilter filter)
        {
            switch (filter)
            {
                case LogFilter.Manual: return "ct.log.filter.manual";
                case LogFilter.Automation: return "ct.log.filter.automation";
                case LogFilter.AutoReject: return "ct.log.filter.autoReject";
                case LogFilter.Tenders: return "ct.log.filter.tenders";
                case LogFilter.Business: return "ct.log.filter.business";
                default: return "ct.log.filter.all";
            }
        }

        /// <summary>The decision of the automation that <see cref="LogDataKeys.Predicted"/> holds, as the key of its name.</summary>
        public static string PredictedKey(string predicted)
        {
            return DecisionKeyPrefix + (predicted ?? string.Empty);
        }

        /// <summary>What the game did (<see cref="LogDataKeys.Observed"/>: <c>sent</c> or <c>notSent</c>), as the key of its sentence.</summary>
        public static string ObservedKey(string observed)
        {
            return observed == "sent" ? "ct.reason.sent" : "ct.reason.notSent";
        }

        /// <summary><c>ips,power,package</c> as the keys of the names of the requirements, comma separated; empty for nothing.</summary>
        public static string FailedKeys(string failed)
        {
            if (string.IsNullOrEmpty(failed)) return string.Empty;
            var keys = new List<string>();
            foreach (string part in failed.Split(','))
            {
                string name = part.Trim();
                if (name == "ips" || name == "power" || name == "package") keys.Add(RequirementNameKeyPrefix + name);
            }
            return string.Join(",", keys.ToArray());
        }

        /// <summary>The argument for the value of a number of an entry: money for payments, a share for margins, a count for lines, keys for flags; anything not known is text.</summary>
        public static PanelArg ValueOf(string key, string value)
        {
            double number;
            switch (key)
            {
                case LogDataKeys.Units:
                    return TryNumber(value, out number) ? PanelArg.Count(number) : PanelArg.OfText(value);

                case LogDataKeys.Payment:
                case LogDataKeys.UnitCost:
                case LogDataKeys.ProviderFines:
                case LogDataKeys.ClientFines:
                    return TryNumber(value, out number) ? PanelArg.Money(number) : PanelArg.OfText(value);

                case LogDataKeys.Margin:
                case LogDataKeys.Threshold:
                    return TryNumber(value, out number) ? PanelArg.Fraction(number) : PanelArg.OfText(value);

                case LogDataKeys.Actual:
                    return TryNumber(value, out number) ? PanelArg.Actual(number) : PanelArg.OfText(value);

                case LogDataKeys.Share:
                case LogDataKeys.Markup:
                    return TryNumber(value, out number) ? PanelArg.Percent(number) : PanelArg.OfText(value);

                case LogDataKeys.Chance:
                case LogDataKeys.Required:
                case LogDataKeys.Contracted:
                case LogDataKeys.Capacity:
                case LogDataKeys.Sent:
                case LogDataKeys.Skipped:
                case LogDataKeys.SkippedMargin:
                case LogDataKeys.SkippedRequirements:
                case LogDataKeys.SkippedOther:
                case LogDataKeys.AlreadyOffered:
                case LogDataKeys.Mismatches:
                case LogDataKeys.Years:
                case LogDataKeys.Score:
                    return TryNumber(value, out number) ? PanelArg.Whole(number) : PanelArg.OfText(value);

                case LogDataKeys.Risks:
                    int flags;
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags) && flags != 0
                        ? PanelArg.OfKeys(RiskLines.NameKeys(flags))
                        : PanelArg.OfKey("ct.log.none");

                case LogDataKeys.Failed:
                    return PanelArg.OfKeys(FailedKeys(value));

                case LogDataKeys.Predicted:
                    return PanelArg.OfKey(PredictedKey(value));

                case LogDataKeys.Observed:
                    return PanelArg.OfKey(ObservedKey(value));

                case LogDataKeys.Outcome:
                    return PanelArg.OfKey(OutcomeKeyPrefix + value);

                case LogDataKeys.Own:
                case LogDataKeys.Exclusive:
                    return PanelArg.OfKey(value == "1" ? "ct.log.yes" : "ct.log.no");

                default:
                    return PanelArg.OfText(value);
            }
        }

        private static bool TryNumber(string text, out double number)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                   && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        /// <summary>The names of every key the lines above can ask the string table for, for the check that both languages have them.</summary>
        public static IEnumerable<string> AllNameKeys()
        {
            foreach (string type in new[]
            {
                LogTypes.OfferSent, LogTypes.OfferCancelledOnReview, LogTypes.OfferAutoConfirmed, LogTypes.AutomationDecision, LogTypes.AutomationSummary,
                LogTypes.AutoRejected, LogTypes.TenderResult, LogTypes.BusinessSigned, LogTypes.Mismatch,
            })
            {
                yield return TypeKey(type);
            }
            foreach (string source in new[] { LogSources.Manual, LogSources.Automation, LogSources.Api, LogSources.Game }) yield return SourceKeyPrefix + source;
            foreach (string outcome in new[] { TenderOutcomeClassifier.OutcomeWon, TenderOutcomeClassifier.OutcomeLost, TenderOutcomeClassifier.OutcomeVoid }) yield return OutcomeKeyPrefix + outcome;
            foreach (AutomationDecision decision in (AutomationDecision[])Enum.GetValues(typeof(AutomationDecision))) yield return PredictedKey(decision.ToString());
            foreach (string name in new[] { "ips", "power", "package" }) yield return RequirementNameKeyPrefix + name;
            foreach (LogFilter filter in (LogFilter[])Enum.GetValues(typeof(LogFilter))) yield return FilterKey(filter);
            foreach (RiskFlags flag in RiskLines.Order) yield return RiskLines.NameKey(flag);
            foreach (string data in DataKeys()) yield return DataKeyPrefix + data;
        }

        /// <summary>Every name of a number of an entry that <see cref="LogDataKeys"/> defines.</summary>
        public static IEnumerable<string> DataKeys()
        {
            return new[]
            {
                LogDataKeys.Units, LogDataKeys.Payment, LogDataKeys.UnitCost, LogDataKeys.Margin, LogDataKeys.Threshold, LogDataKeys.Chance, LogDataKeys.Actual,
                LogDataKeys.Risks, LogDataKeys.Required, LogDataKeys.Contracted, LogDataKeys.Capacity, LogDataKeys.Maker, LogDataKeys.Own, LogDataKeys.Outcome,
                LogDataKeys.Winner, LogDataKeys.WinnerCpu, LogDataKeys.Failed, LogDataKeys.Predicted, LogDataKeys.Observed, LogDataKeys.Sent, LogDataKeys.Skipped,
                LogDataKeys.SkippedMargin, LogDataKeys.SkippedRequirements, LogDataKeys.SkippedOther, LogDataKeys.AlreadyOffered, LogDataKeys.Mismatches,
                LogDataKeys.Provider, LogDataKeys.Client, LogDataKeys.Years, LogDataKeys.Share, LogDataKeys.Markup, LogDataKeys.Exclusive, LogDataKeys.ProviderFines,
                LogDataKeys.ClientFines, LogDataKeys.Score,
            };
        }
    }
}
