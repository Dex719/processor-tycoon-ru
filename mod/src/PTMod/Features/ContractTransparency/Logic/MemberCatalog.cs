using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The kind of a member of the game's classes the feature relies on.</summary>
    public enum GameMemberKind
    {
        /// <summary>A method (a patch target, or a private method the feature only calls).</summary>
        Method = 0,

        /// <summary>A field, usually private (a serialized field of a window).</summary>
        Field = 1,

        /// <summary>A property (read through its getter).</summary>
        Property = 2,

        /// <summary>A nested type (<c>Contract.CpuOffer</c>).</summary>
        NestedType = 3,
    }

    /// <summary>
    /// The parts of the feature a missing member of the game turns off (design.md, "Самопроверка на старте": "отсутствие члена отключает только зависящую часть"). A part is something the player sees or
    /// the log keeps; the rest of the feature goes on when one part is off.
    /// </summary>
    public enum FeaturePart
    {
        /// <summary>The review screen before a manual offer (patch of <c>Contract.AddOffer</c>; the screen also needs <see cref="Analysis"/>).</summary>
        OfferGate = 0,

        /// <summary>The results of the tenders in the log, the letter and the notification (patch of <c>Contract.ChooseCpu</c>).</summary>
        TenderLog = 1,

        /// <summary>The log of the contract automation and its preview (patch of <c>ContractAutomation.SendOffers</c>).</summary>
        AutomationLog = 2,

        /// <summary>The "Why?" and "Log" buttons and the actual probability in the contract window.</summary>
        WindowUi = 3,

        /// <summary>The question before a deal between companies is signed and the line in the log.</summary>
        SignQuestion = 4,

        /// <summary>The breakdown of the chance, the figures of the review screen, the actual probability and the possible participants: everything built on the game's own chances.</summary>
        Analysis = 5,

        /// <summary>The game's own chances of the offers in a tender result (the result itself is still recorded; the plain formula of the chance stands in for the game's answer).</summary>
        TenderChances = 6,
    }

    /// <summary>How a member of the game was found by the check.</summary>
    public enum MemberState
    {
        /// <summary>The member exists as expected.</summary>
        Found = 0,

        /// <summary>The member (or its class) is not in the game.</summary>
        Missing = 1,

        /// <summary>The member exists, but its type is not the expected one (a field that became a double, a method with another number of parameters).</summary>
        WrongType = 2,

        /// <summary>The core holds the method for itself (<c>ModHooks.IsReserved</c>): a patch of the feature would be refused.</summary>
        Reserved = 3,

        /// <summary>A patch of this feature names a method that the feature may only call.</summary>
        Patched = 4,
    }

    /// <summary>What kind of reliance a row of <see cref="MemberCatalog"/> is.</summary>
    [Flags]
    internal enum MemberFlags
    {
        None = 0,
        PatchTarget = 1,
        RequiredPatch = 2,
        Advisory = 4,
        MustNotBePatched = 8,
        Reflected = 16,
    }

    /// <summary>One member of the game that the feature relies on (a patch target or a private member read by reflection), with what depends on it.</summary>
    public sealed class GameMember
    {
        internal GameMember(string key, string typeName, string name, GameMemberKind kind, string expectedType, int parameterCount, MemberFlags flags, FeaturePart[] parts)
        {
            Key = key;
            TypeName = typeName;
            Name = name;
            Kind = kind;
            ExpectedType = expectedType;
            ParameterCount = parameterCount;
            IsPatchTarget = (flags & MemberFlags.PatchTarget) != 0;
            RequiredPatch = (flags & MemberFlags.RequiredPatch) != 0;
            Advisory = (flags & MemberFlags.Advisory) != 0;
            MustNotBePatched = (flags & MemberFlags.MustNotBePatched) != 0;
            Reflected = (flags & MemberFlags.Reflected) != 0;
            Parts = parts;
        }

        /// <summary><c>Class.member</c>, a nested class after a dot (<c>Contract.CpuOffer.Chance</c>): the name in the log and in the debug flag that hides the member (<see cref="MissingMemberFlags"/>).</summary>
        public string Key { get; private set; }

        /// <summary>The full name of the declaring type as reflection spells it (<c>ProcessorTycoon.ContractSystem.Contract+CpuOffer</c>; a nested type after a plus).</summary>
        public string TypeName { get; private set; }

        /// <summary>The name of the member (for a nested type: its own name).</summary>
        public string Name { get; private set; }

        public GameMemberKind Kind { get; private set; }

        /// <summary>The full name of the type of a field or a property, or null when only the existence counts.</summary>
        public string ExpectedType { get; private set; }

        /// <summary>The number of parameters of a method (the check is by name and this number), -1 for any.</summary>
        public int ParameterCount { get; private set; }

        /// <summary>A method one of the patches of the feature is on (<c>[ModPatch]</c>).</summary>
        public bool IsPatchTarget { get; private set; }

        /// <summary>The patch is not <c>Optional</c>: when its target is gone the core makes the whole feature Unavailable.</summary>
        public bool RequiredPatch { get; private set; }

        /// <summary>The member is not read by the code yet and only watched (the design names it): when it is gone the log says so, nothing is turned off.</summary>
        public bool Advisory { get; private set; }

        /// <summary>The feature calls the method and never patches it (<c>Contract.GetBestOffers</c> is the core's P23).</summary>
        public bool MustNotBePatched { get; private set; }

        /// <summary>The code looks the member up by reflection (through <c>GameMembers</c>), so the debug flag of <see cref="MissingMemberFlags"/> can hide it. A patch target that only the core resolves is not.</summary>
        public bool Reflected { get; private set; }

        /// <summary>What stops working when the member is not usable.</summary>
        public FeaturePart[] Parts { get; private set; }

        public override string ToString()
        {
            return Key;
        }
    }

    /// <summary>The keys of <see cref="MemberCatalog"/>, one place: the code that reaches the member by reflection names it by this key (<c>GameMembers</c>) and so can be hidden by the debug flag.</summary>
    public static class MemberKeys
    {
        // The six patch targets (design.md, "Патчи Harmony").
        public const string AddOffer = "Contract.AddOffer";
        public const string ChooseCpu = "Contract.ChooseCpu";
        public const string SendOffers = "ContractAutomation.SendOffers";
        public const string WindowStart = "ContractWindow.Start";
        public const string WindowUpdateProbability = "ContractWindow.UpdateProbabilityText";
        public const string SignContract = "ContractNegotiationWindow.SignContract";

        // The private members of Contract.
        public const string Tolerance = "Contract.mipsToleranceMultiplier";
        public const string GetBestOffers = "Contract.GetBestOffers";
        public const string OfferType = "Contract.CpuOffer";
        public const string OfferCpu = "Contract.CpuOffer.Cpu";
        public const string OfferChance = "Contract.CpuOffer.Chance";

        // The private fields of ContractWindow.
        public const string WindowContract = "ContractWindow.currentContract";
        public const string WindowCpu = "ContractWindow.currentCpu";
        public const string WindowProbabilityText = "ContractWindow.probabilityText";
        public const string WindowCpuFailed = "ContractWindow.cpuFailedAtAnyRequirement";
        public const string WindowSendButton = "ContractWindow.sendOfferButton";

        // The private fields of ContractNegotiationWindow.
        public const string DealProvider = "ContractNegotiationWindow.currentProvider";
        public const string DealClient = "ContractNegotiationWindow.currentClient";
        public const string DealDuration = "ContractNegotiationWindow.duration";
        public const string DealCapacity = "ContractNegotiationWindow.productionCapacity";
        public const string DealMarkup = "ContractNegotiationWindow.markupPrice";
        public const string DealProviderFines = "ContractNegotiationWindow.providerFines";
        public const string DealClientFines = "ContractNegotiationWindow.clientFines";
        public const string DealExclusivity = "ContractNegotiationWindow.exclusivity";
        public const string DealScore = "ContractNegotiationWindow.acceptanceScore";
    }

    /// <summary>
    /// The table of what the feature expects of the game (design.md, "Самопроверка на старте"; FR-12 item 3): the six methods the patches are on and the private members read by reflection, each with the part of
    /// the feature that stops when it is gone. Pure data: the check that looks the members up in the game is <c>ContractSelfCheck</c>, the evaluation of its answers is <see cref="SelfCheckEvaluator"/>.
    /// </summary>
    public static class MemberCatalog
    {
        private const string ContractType = "ProcessorTycoon.ContractSystem.Contract";
        private const string OfferType = "ProcessorTycoon.ContractSystem.Contract+CpuOffer";
        private const string AutomationType = "ProcessorTycoon.ContractSystem.Automation.ContractAutomation";
        private const string WindowType = "ProcessorTycoon.ContractSystem.UI.ContractWindow";
        private const string DealType = "ProcessorTycoon.CompanySystem.Business.ContractNegotiationWindow";

        private const string Cpu = "ProcessorTycoon.Hardware.Cpu";
        private const string Company = "ProcessorTycoon.CompanySystem.ICompany";
        private const string Float = "System.Single";
        private const string Int = "System.Int32";
        private const string Bool = "System.Boolean";

        private static readonly FeaturePart[] ChancesParts = { FeaturePart.Analysis, FeaturePart.TenderChances };
        private static readonly FeaturePart[] AnalysisParts = { FeaturePart.Analysis };
        private static readonly FeaturePart[] WindowParts = { FeaturePart.WindowUi };
        private static readonly FeaturePart[] DealParts = { FeaturePart.SignQuestion };

        private static readonly GameMember[] Rows =
        {
            // ---- the patch targets: the three of the flow are required (the core makes the feature Unavailable without them), the three of the screens are Optional ----
            Target(MemberKeys.AddOffer, ContractType, "AddOffer", 1, MemberFlags.RequiredPatch, FeaturePart.OfferGate),
            Target(MemberKeys.ChooseCpu, ContractType, "ChooseCpu", 0, MemberFlags.RequiredPatch, FeaturePart.TenderLog),
            Target(MemberKeys.SendOffers, AutomationType, "SendOffers", 0, MemberFlags.RequiredPatch, FeaturePart.AutomationLog),
            Target(MemberKeys.WindowStart, WindowType, "Start", 0, MemberFlags.None, FeaturePart.WindowUi),
            Target(MemberKeys.WindowUpdateProbability, WindowType, "UpdateProbabilityText", 0, MemberFlags.Reflected, FeaturePart.WindowUi),   // also called by the mod to refresh the text
            Target(MemberKeys.SignContract, DealType, "SignContract", 0, MemberFlags.Reflected, FeaturePart.SignQuestion),                     // also called by the mod after "Sign"

            // ---- Contract: the tolerance of the IPS requirement, the chances of the draw (called, never patched: the core's P23), the offer type ----
            Member(MemberKeys.Tolerance, ContractType, "mipsToleranceMultiplier", GameMemberKind.Field, Float, -1, AnalysisParts),
            new GameMember(MemberKeys.GetBestOffers, ContractType, "GetBestOffers", GameMemberKind.Method, null, 1, MemberFlags.MustNotBePatched | MemberFlags.Reflected, ChancesParts),
            Member(MemberKeys.OfferType, ContractType, "CpuOffer", GameMemberKind.NestedType, null, -1, ChancesParts),
            Member(MemberKeys.OfferCpu, OfferType, "Cpu", GameMemberKind.Property, Cpu, -1, ChancesParts),
            Member(MemberKeys.OfferChance, OfferType, "Chance", GameMemberKind.Property, Int, -1, ChancesParts),

            // ---- ContractWindow: the buttons and the actual probability. sendOfferButton is named by the design and not read yet: watched only ----
            Member(MemberKeys.WindowContract, WindowType, "currentContract", GameMemberKind.Field, ContractType, -1, WindowParts),
            Member(MemberKeys.WindowCpu, WindowType, "currentCpu", GameMemberKind.Field, Cpu, -1, WindowParts),
            Member(MemberKeys.WindowProbabilityText, WindowType, "probabilityText", GameMemberKind.Field, "ProcessorTycoon.UI.InfoText", -1, WindowParts),
            Member(MemberKeys.WindowCpuFailed, WindowType, "cpuFailedAtAnyRequirement", GameMemberKind.Field, Bool, -1, WindowParts),
            new GameMember(MemberKeys.WindowSendButton, WindowType, "sendOfferButton", GameMemberKind.Field, "ProcessorTycoon.UI.ButtonUI", -1, MemberFlags.Advisory, WindowParts),

            // ---- ContractNegotiationWindow: the terms of a deal ----
            Member(MemberKeys.DealProvider, DealType, "currentProvider", GameMemberKind.Field, Company, -1, DealParts),
            Member(MemberKeys.DealClient, DealType, "currentClient", GameMemberKind.Field, Company, -1, DealParts),
            Member(MemberKeys.DealDuration, DealType, "duration", GameMemberKind.Field, Int, -1, DealParts),
            Member(MemberKeys.DealCapacity, DealType, "productionCapacity", GameMemberKind.Field, Int, -1, DealParts),
            Member(MemberKeys.DealMarkup, DealType, "markupPrice", GameMemberKind.Field, Int, -1, DealParts),
            Member(MemberKeys.DealProviderFines, DealType, "providerFines", GameMemberKind.Field, Float, -1, DealParts),
            Member(MemberKeys.DealClientFines, DealType, "clientFines", GameMemberKind.Field, Float, -1, DealParts),
            Member(MemberKeys.DealExclusivity, DealType, "exclusivity", GameMemberKind.Field, Bool, -1, DealParts),
            Member(MemberKeys.DealScore, DealType, "acceptanceScore", GameMemberKind.Field, Int, -1, DealParts),
        };

        private static GameMember Target(string key, string typeName, string name, int parameters, MemberFlags flags, FeaturePart part)
        {
            return new GameMember(key, typeName, name, GameMemberKind.Method, null, parameters, flags | MemberFlags.PatchTarget, new[] { part });
        }

        private static GameMember Member(string key, string typeName, string name, GameMemberKind kind, string expectedType, int parameters, FeaturePart[] parts)
        {
            return new GameMember(key, typeName, name, kind, expectedType, parameters, MemberFlags.Reflected, parts);
        }

        /// <summary>Every row: the six patch targets first, then the private members.</summary>
        public static IReadOnlyList<GameMember> All
        {
            get { return Rows; }
        }

        /// <summary>The six methods the patches of the feature are on.</summary>
        public static IReadOnlyList<GameMember> PatchTargets
        {
            get
            {
                var list = new List<GameMember>();
                foreach (GameMember row in Rows)
                {
                    if (row.IsPatchTarget) list.Add(row);
                }
                return list;
            }
        }

        /// <summary>The members that are not patch targets (fields, properties, the nested type, the private method that is called).</summary>
        public static IReadOnlyList<GameMember> PrivateMembers
        {
            get
            {
                var list = new List<GameMember>();
                foreach (GameMember row in Rows)
                {
                    if (!row.IsPatchTarget) list.Add(row);
                }
                return list;
            }
        }

        /// <summary>The row with this key, or null.</summary>
        public static GameMember Find(string key)
        {
            foreach (GameMember row in Rows)
            {
                if (string.Equals(row.Key, key, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        /// <summary>A line for the log about what a part is (English, short).</summary>
        public static string Describe(FeaturePart part)
        {
            switch (part)
            {
                case FeaturePart.OfferGate: return "the review screen before an offer";
                case FeaturePart.TenderLog: return "the results of the tenders";
                case FeaturePart.AutomationLog: return "the log and the preview of the automation";
                case FeaturePart.WindowUi: return "the buttons and the actual probability in the contract window";
                case FeaturePart.SignQuestion: return "the question before a deal is signed";
                case FeaturePart.Analysis: return "the breakdown of the chance, the review screen figures and the actual probability";
                case FeaturePart.TenderChances: return "the game's chances in the tender results (the plain formula stands in)";
                default: return part.ToString();
            }
        }
    }

    /// <summary>
    /// The debug flag that makes the feature believe a member of the game is gone (the test flag of TSK-020, AC "эмуляция отсутствия члена"). In the <c>debug</c> section of <c>ptmod_settings.json</c> of a debug build:
    /// <c>"ct.missing.Contract.GetBestOffers": true</c>. Every lookup of a member by reflection goes through <c>GameMembers</c> (the rows with <see cref="GameMember.Reflected"/>), which asks <see cref="IsHidden"/> first, so the part of the feature that depends on the
    /// member behaves as it would in a game that lacks it, and the rest goes on. A release build has no debug flags: the flag never reads true there.
    /// </summary>
    public static class MissingMemberFlags
    {
        /// <summary>The prefix of the flag names.</summary>
        public const string Prefix = "ct.missing.";

        /// <summary>The name of the flag (the key in the <c>debug</c> section) that hides the member with this key.</summary>
        public static string FlagOf(string memberKey)
        {
            return Prefix + (memberKey ?? string.Empty);
        }

        /// <summary>True when the flag source says the member is hidden. <paramref name="flagSource"/> answers for a flag name (<c>ModSettings.Debug</c> in the game); null means no flags.</summary>
        public static bool IsHidden(string memberKey, Func<string, bool> flagSource)
        {
            if (string.IsNullOrEmpty(memberKey) || flagSource == null) return false;
            return flagSource(FlagOf(memberKey));
        }
    }
}
