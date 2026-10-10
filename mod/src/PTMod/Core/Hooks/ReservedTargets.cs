using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace PTMod.Core
{
    /// <summary>Who holds a reserved game method (the "Резерв" column of the patch table in design.md).</summary>
    public enum ReservedKind
    {
        /// <summary>A channel of <c>ModModifiers</c> reads the formula (P1-P11, P19, P23): features use records and providers.</summary>
        Channel,

        /// <summary>A core service sits there (P15-P18, P20-P22): features use its events and API.</summary>
        Service,

        /// <summary>The formula belongs to the feature that owns the channel (F1, F2): only that feature may patch it.</summary>
        FeatureFormula
    }

    /// <summary>
    /// One reserved game method (FR-2 item 6): a feature's patch on it is rejected with a hint at the API to use instead. Immutable; the table
    /// is <see cref="ReservedTargets.All"/>.
    /// </summary>
    public sealed class ReservedTarget
    {
        private const string HintKeyPrefix = "core.hooks.reserved.";

        private readonly PatchKind[] _kinds;

        internal ReservedTarget(string patch, string typeName, string method, ReservedKind kind, string name, string ownerFeature, string api,
            params PatchKind[] kinds)
        {
            Patch = patch;
            TypeName = typeName;
            Method = method;
            Kind = kind;
            Name = name;
            OwnerFeature = ownerFeature;
            Api = api;
            _kinds = kinds == null || kinds.Length == 0
                ? new[] { PatchKind.Prefix, PatchKind.Postfix, PatchKind.Transpiler, PatchKind.Finalizer }
                : (PatchKind[])kinds.Clone();
            Kinds = new ReadOnlyCollection<PatchKind>(_kinds);
        }

        /// <summary>The row of the patch table: <c>P4</c>, <c>P20</c>, <c>F1</c>.</summary>
        public string Patch { get; }

        /// <summary>Full name of the game type as reflection spells it (<c>ProcessorTycoon.ContractSystem.Contract+CpuOffer</c>).</summary>
        public string TypeName { get; }

        public string Method { get; }

        /// <summary><c>Class.Method</c> for messages: <c>Market.CalculateRatings</c>, <c>Contract.CpuOffer.DefineChance</c>.</summary>
        public string Target
        {
            get
            {
                string type = TypeName;
                int dot = type.LastIndexOf('.');
                if (dot >= 0) type = type.Substring(dot + 1);
                return type.Replace('+', '.') + "." + Method;
            }
        }

        public ReservedKind Kind { get; }

        /// <summary>The channel (<c>market.rating.cpu</c>), the service (<c>ModEvents</c>) or, for a feature formula, the owner's channel.</summary>
        public string Name { get; }

        /// <summary><c>mod-framework</c>, or the feature that owns the formula (<c>market-shocks</c>).</summary>
        public string OwnerFeature { get; }

        /// <summary>The API to use instead, as code: <c>ModEvents.CpuReleased</c>, <c>ModModifiers</c>; null for a feature formula.</summary>
        public string Api { get; }

        /// <summary>
        /// The kinds of patch the reservation covers; the others stay open to every feature (P7: postfixes on <c>EarnMoney</c> for accounting,
        /// P10/P11: only the transpiler is the core's, prefixes and postfixes are free).
        /// </summary>
        public IReadOnlyList<PatchKind> Kinds { get; }

        /// <summary>
        /// ModL10n key of the hint: <c>core.hooks.reserved.channel</c>, <c>.service</c> or <c>.featureFormula</c>. Arguments: {0} =
        /// <see cref="Name"/>, {1} = <see cref="Api"/> (the owner feature for a feature formula).
        /// </summary>
        public string UseInstead
        {
            get { return HintKeyPrefix + KindSlug(Kind); }
        }

        /// <summary>The reservation covers this kind of patch.</summary>
        public bool Covers(PatchKind kind)
        {
            return Array.IndexOf(_kinds, kind) >= 0;
        }

        /// <summary>
        /// The patch is refused: the kind is covered and the feature is neither the core nor, for a feature formula, the owner. The owner's
        /// patch on its own formula is accepted (FR-2 item 6).
        /// </summary>
        public bool Rejects(string featureId, PatchKind kind)
        {
            if (string.IsNullOrEmpty(featureId) || featureId == ModHooks.CoreId) return false;
            if (!Covers(kind)) return false;
            return !(Kind == ReservedKind.FeatureFormula && featureId == OwnerFeature);
        }

        /// <summary>
        /// The hint for the player in the current language (<see cref="UseInstead"/> with <see cref="Name"/> and <see cref="Api"/>):
        /// "формулу читает канал market.rating.cpu, используйте ModModifiers", "используйте ModEvents.CpuReleased (служба ModEvents)". The English
        /// <see cref="EnglishHint"/> while the tables do not have the key.
        /// </summary>
        public string Hint()
        {
            try
            {
                string key = UseInstead;
                if (ModL10n.Has(key)) return ModL10n.L(key, Name, Api ?? OwnerFeature);
            }
            catch (Exception)
            {
                // ModL10n never throws; the English text is the answer either way.
            }
            return EnglishHint;
        }

        /// <summary>The hint in English, for the log.</summary>
        public string EnglishHint
        {
            get
            {
                switch (Kind)
                {
                    case ReservedKind.Channel: return "the channel " + Name + " reads this formula, use " + (Api ?? "ModModifiers");
                    case ReservedKind.Service: return "use " + Api;
                    default: return "the feature " + OwnerFeature + " owns this formula (channel " + Name + ")";
                }
            }
        }

        /// <summary>Who holds the target, for <c>core.hooks.targetReserved</c> ("цель {0} закреплена за {1}"): <c>ModModifiers</c>, <c>ModEvents</c>, <c>market-shocks</c>.</summary>
        public string Holder
        {
            get
            {
                switch (Kind)
                {
                    case ReservedKind.Channel: return "ModModifiers";
                    case ReservedKind.Service: return Name;
                    default: return OwnerFeature;
                }
            }
        }

        /// <summary>One English line for the log: why the target is closed and what to use.</summary>
        public string Describe()
        {
            string kinds = Kinds.Count == 4 ? "" : " (" + KindsText() + " only)";
            switch (Kind)
            {
                case ReservedKind.Channel:
                    return Target + " is reserved" + kinds + " by the core channel " + Name + " (" + Patch + "): " + EnglishHint;
                case ReservedKind.Service:
                    return Target + " is reserved" + kinds + " by the core service " + Name + " (" + Patch + "): " + EnglishHint;
                default:
                    return Target + " is reserved" + kinds + " for the feature " + OwnerFeature + " (" + Patch + ", channel " + Name + ")";
            }
        }

        public override string ToString()
        {
            return Patch + " " + Target + " -> " + Kind + " " + Name;
        }

        internal string Key
        {
            get { return ReservedTargets.KeyOf(TypeName, Method); }
        }

        private string KindsText()
        {
            var names = new string[_kinds.Length];
            for (int i = 0; i < _kinds.Length; i++) names[i] = _kinds[i].ToString().ToLowerInvariant();
            return string.Join(", ", names);
        }

        private static string KindSlug(ReservedKind kind)
        {
            switch (kind)
            {
                case ReservedKind.Channel: return "channel";
                case ReservedKind.Service: return "service";
                default: return "featureFormula";
            }
        }
    }

    /// <summary>
    /// The table of reserved game methods: every row of the "Резерв" column of the patch table in design.md (P1-P11, P19, P23 - channel;
    /// P15-P18, P20-P22 - service; F1, F2 - formula of <c>market-shocks</c>; P12-P14 are free). Keyed by the declaring type's full name and the
    /// method name, so every overload is covered and the table needs no game types. <see cref="Verify"/> checks the rows against the game.
    /// </summary>
    internal static class ReservedTargets
    {
        private const string Core = ModHooks.CoreId;
        private const string MarketShocks = "market-shocks";
        private const string Modifiers = "ModModifiers";

        private static readonly ReservedTarget[] Rows =
        {
            // ---- channel: ModModifiers reads the formula ----
            Channel("P1", "ProcessorTycoon.CompanySystem.Modifiers", "GetModifier",
                "research.speed, research.cost, factory.operatingCost, construction.cost, development.cost, taxes, bank.interest"),
            Channel("P2", "ProcessorTycoon.Production.Factory", "ProduceWafers", "production.unitCost, production.output, cpu.failureRate"),
            Channel("P3", "ProcessorTycoon.MarketSystem.PopulationData", "GetCurrentSize", "market.demand.*"),
            Channel("P4", "ProcessorTycoon.MarketSystem.Market", "CalculateRatings", "market.rating.cpu"),
            Channel("P5", "ProcessorTycoon.MarketSystem.Market", "UpdatePopularities", "popularity.gain"),
            Channel("P6", "ProcessorTycoon.ContractSystem.ContractSeller", "SellContracts", "contract.reward"),
            // P7: other features may add postfixes (and finalizers) for accounting, but must not change the amount.
            Channel("P7", "ProcessorTycoon.CompanySystem.Company", "EarnMoney", "contract.reward", Modifiers, PatchKind.Prefix, PatchKind.Transpiler),
            Channel("P7", "ProcessorTycoon.CompanySystem.AICompany", "EarnMoney", "contract.reward", Modifiers, PatchKind.Prefix, PatchKind.Transpiler),
            Channel("P8", "ProcessorTycoon.ContractSystem.Contract+CpuOffer", "DefineChance", "contract.signChance"),
            Channel("P9", "ProcessorTycoon.Bank.CentralBank", "UpdateCreditLimit", "bank.creditLimit"),
            // P10/P11: only the transpiler is the core's; bankruptcy-recovery uses prefixes and postfixes on these methods.
            Channel("P10", "ProcessorTycoon.Bank.BankruptcyHandler", "TickBankruptcies", "bank.creditLimit", "ModCredit.ThresholdOf", PatchKind.Transpiler),
            Channel("P11", "ProcessorTycoon.CompanySystem.PlayerUI.MoneyBalance", "UpdateUI", "bank.creditLimit", "ModCredit.PlayerDebtLimit", PatchKind.Transpiler),
            Channel("P11", "ProcessorTycoon.CompanySystem.Money", "UpdateMoneyText", "bank.creditLimit", "ModCredit.PlayerDebtLimit", PatchKind.Transpiler),
            Channel("P11", "ProcessorTycoon.Bank.BankruptcyHandler", "UpdateUI", "bank.creditLimit", "ModCredit.PlayerDebtLimit", PatchKind.Transpiler),
            Channel("P19", "ProcessorTycoon.Hardware.Math.CpuMath", "ProjectCost", "development.cost.design"),
            Channel("P23", "ProcessorTycoon.ContractSystem.Contract", "GetBestOffers", "contract.signChance"),

            // ---- service: a core service sits there ----
            Service("P15", "ProcessorTycoon.Desktop.Email.EmailWindow", "GetEmails", "ModMail", "ModMail.Send"),
            Service("P16", "ProcessorTycoon.Desktop.Email.EmailWindow", "UpdateEmailText", "ModMail", "ModMail.Send (choices and actions of a letter)"),
            Service("P17", "ProcessorTycoon.GameManager", "StartNewGame", "ModNewGame", "ModNewGame.BeforeStart"),
            Service("P18", "ProcessorTycoon.Menu.MenuGameSetup", "InitializeUI", "ModNewGame", "ModNewGame.RegisterOption / ModNewGame.RegisterSection"),
            Service("P20", "ProcessorTycoon.CompanySystem.Company", "AddCpu", "ModEvents", "ModEvents.CpuReleased"),
            Service("P20", "ProcessorTycoon.CompanySystem.AICompany", "AddCpu", "ModEvents", "ModEvents.CpuReleased"),
            Service("P21", "ProcessorTycoon.TimeSystem.DateController", "NextMonth", "ModTicks", "ModTicks.IsBatchRun / ModTicks.BatchEnded"),
            Service("P22", "ProcessorTycoon.Menu.MenuGameSetup", "UpdateInitialData", "ModNewGame", "ModNewGame.PreviewChanged"),

            // ---- feature formula: the channel's owner patches it, nobody else ----
            Formula("F1", "ProcessorTycoon.Hardware.Math.CpuMath", "MaterialCost", "production.memoryCost", MarketShocks),
            Formula("F1", "ProcessorTycoon.Production.Math.ProductionMath", "WaferMaterialCost", "production.waferCost", MarketShocks),
            Formula("F2", "ProcessorTycoon.Production.Math.ProductionMath", "WaferCost", "production.waferCost", MarketShocks),
            Formula("F2", "ProcessorTycoon.Hardware.Memory", "GetDescriptions", "production.memoryCost", MarketShocks),
        };

        private static readonly Dictionary<string, ReservedTarget> ByKey = Index();

        private static readonly ReadOnlyCollection<ReservedTarget> AllRows = new ReadOnlyCollection<ReservedTarget>(Rows);

        /// <summary>Every row, in the order of the patch table.</summary>
        internal static IReadOnlyList<ReservedTarget> All
        {
            get { return AllRows; }
        }

        /// <summary>The reservation of a method (any overload of a reserved name on the type that declares it), or null.</summary>
        internal static ReservedTarget Find(MethodBase target)
        {
            if (target == null || target.DeclaringType == null) return null;

            ReservedTarget row;
            return ByKey.TryGetValue(KeyOf(target.DeclaringType.FullName ?? target.DeclaringType.Name, target.Name), out row) ? row : null;
        }

        internal static string KeyOf(string typeName, string method)
        {
            return typeName + "::" + method;
        }

        /// <summary>
        /// The rows that do not match the loaded game (the type or the method name is missing): an empty list on the supported version. For the
        /// self-check (FR-19: "зарезервированные цели не заняты фичами" needs the table to be right). Never throws.
        /// </summary>
        internal static IReadOnlyList<string> Verify()
        {
            var problems = new List<string>();
            Assembly game = GameAssembly();
            if (game == null)
            {
                problems.Add("Assembly-CSharp is not loaded");
                return problems;
            }

            foreach (ReservedTarget row in Rows)
            {
                try
                {
                    Type type = game.GetType(row.TypeName, false);
                    if (type == null)
                    {
                        problems.Add(row.Patch + " " + row.Target + ": type " + row.TypeName + " not found");
                        continue;
                    }

                    bool found = false;
                    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (MethodInfo method in type.GetMethods(all))
                    {
                        if (method.Name != row.Method) continue;
                        found = true;
                        break;
                    }
                    if (!found) problems.Add(row.Patch + " " + row.Target + ": method " + row.Method + " not found on " + row.TypeName);
                }
                catch (Exception ex)
                {
                    problems.Add(row.Patch + " " + row.Target + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return problems;
        }

        private static Assembly GameAssembly()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name == "Assembly-CSharp") return assembly;
                }
                catch (Exception)
                {
                    // A dynamic or broken assembly: not the game.
                }
            }
            return null;
        }

        private static Dictionary<string, ReservedTarget> Index()
        {
            var map = new Dictionary<string, ReservedTarget>(StringComparer.Ordinal);
            foreach (ReservedTarget row in Rows) map[row.Key] = row;
            return map;
        }

        private static ReservedTarget Channel(string patch, string type, string method, string channel, string api = Modifiers, params PatchKind[] kinds)
        {
            return new ReservedTarget(patch, type, method, ReservedKind.Channel, channel, Core, api, kinds);
        }

        private static ReservedTarget Service(string patch, string type, string method, string service, string api)
        {
            return new ReservedTarget(patch, type, method, ReservedKind.Service, service, Core, api);
        }

        private static ReservedTarget Formula(string patch, string type, string method, string channel, string owner)
        {
            return new ReservedTarget(patch, type, method, ReservedKind.FeatureFormula, channel, owner, null);
        }
    }
}
