using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The names of the effect types the feature registers with the event system (the <c>type</c> of an effect in <c>events.json</c>).</summary>
    public static class HistEffectTypes
    {
        public const string AntitrustCase = "AntitrustCase";
        public const string AntitrustSettlement = "AntitrustSettlement";
        public const string ExportControl = "ExportControl";
        public const string PriceWar = "PriceWar";
        public const string PatentDispute = "PatentDispute";
        public const string AttributeRace = "AttributeRace";

        /// <summary>The bridge to design-flaw-recalls (FR-12): forces a flaw on a CPU of the company, or fires the fallback event.</summary>
        public const string ForceDesignFlaw = "ForceDesignFlaw";

        /// <summary>The service effect that puts a flag on a calendar entry, which a <c>skipIfFlag</c> of a stage reads.</summary>
        public const string HistFlag = "HistFlag";

        /// <summary>The six types of the services with a process of their own (cases, suits, rules, wars, disputes, races), in the order of registration.</summary>
        public static readonly string[] Services = { AntitrustCase, AntitrustSettlement, ExportControl, PriceWar, PatentDispute, AttributeRace };

        /// <summary>Every effect type the feature registers: the six services, then the bridge to the flaw mechanic and the flag.</summary>
        public static readonly string[] Own = { AntitrustCase, AntitrustSettlement, ExportControl, PriceWar, PatentDispute, AttributeRace, ForceDesignFlaw, HistFlag };
    }

    /// <summary>
    /// The positional arguments of an event of the pack as a service reads them (<see cref="DispatcherCore"/> writes them): <c>[0]</c> the id of the calendar entry,
    /// <c>[1]</c> the name of the company the event is for, <c>[2]</c> the name and <c>[3]</c> the key of the second company of the entry (the plaintiff, the beneficiary),
    /// <c>[4]</c> the first extra argument of the fire (the case, the rule, the war). Missing arguments read as empty.
    /// </summary>
    public static class HistArgs
    {
        public const int EntryIndex = 0;
        public const int TargetNameIndex = 1;
        public const int SecondNameIndex = 2;
        public const int SecondKeyIndex = 3;
        public const int FirstExtraIndex = 4;
        public const int SecondExtraIndex = 5;

        public static string At(string[] args, int index)
        {
            return args != null && index >= 0 && index < args.Length && args[index] != null ? args[index] : string.Empty;
        }

        /// <summary>
        /// The id of the case, the rule, the war, the dispute or the race: the member of the effect when it names one, else the first extra argument of the fire, else the
        /// calendar entry (one process per entry).
        /// </summary>
        public static string IdOf(string fromParams, string[] args)
        {
            if (!string.IsNullOrEmpty(fromParams)) return fromParams;
            string extra = At(args, FirstExtraIndex);
            return extra.Length > 0 ? extra : At(args, EntryIndex);
        }
    }

    /// <summary>
    /// The check of the <c>params</c> of the feature's effect types, the same one for every caller (design.md, "Security": "<c>IEventEffectHandler.Validate</c> of the types of
    /// the feature checks the ranges of its parameters"): the handlers call it when the content is loaded, and the tests call it on the shipped <c>events.json</c>. A line in
    /// <paramref name="errors"/> rejects the event.
    /// </summary>
    public static class HistEffectRules
    {
        /// <summary>The type is one of the six services handled here.</summary>
        public static bool IsServiceType(string type)
        {
            return Array.IndexOf(HistEffectTypes.Services, type) >= 0;
        }

        /// <summary>The type is one of the eight the feature registers (the six services, <c>ForceDesignFlaw</c> and <c>HistFlag</c>).</summary>
        public static bool IsOwnType(string type)
        {
            return Array.IndexOf(HistEffectTypes.Own, type) >= 0;
        }

        /// <summary>Checks the parameters of an effect of the type. True when there is nothing to reject; unknown types are not this class's business and pass.</summary>
        public static bool Validate(string type, JsonObject raw, List<string> errors)
        {
            var problems = new List<string>();
            switch (type)
            {
                case HistEffectTypes.AntitrustCase:
                    CaseParams.Read(raw, problems);
                    break;
                case HistEffectTypes.AntitrustSettlement:
                    SettlementParams.Read(raw, null, problems);
                    break;
                case HistEffectTypes.ExportControl:
                    ExportParams.Read(raw, problems);
                    break;
                case HistEffectTypes.PriceWar:
                    WarParams.Read(raw, null, problems);
                    break;
                case HistEffectTypes.PatentDispute:
                    DisputeSpec.Read(raw, problems);
                    break;
                case HistEffectTypes.AttributeRace:
                    RaceParams.Read(raw, problems);
                    break;
                case HistEffectTypes.ForceDesignFlaw:
                    ForceFlawParams.Read(raw, problems);
                    break;
                case HistEffectTypes.HistFlag:
                    HistFlagParams.Read(raw, problems);
                    break;
                default:
                    return true;
            }

            // An unknown member is a warning of the reader ("unknown member is ignored"), not a reason to reject.
            bool ok = true;
            foreach (string line in problems)
            {
                if (line.EndsWith("unknown member is ignored", StringComparison.Ordinal)) continue;
                ok = false;
                if (errors != null) errors.Add(line);
            }
            return ok;
        }
    }
}
