using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractHelper = ProcessorTycoon.ContractSystem.ContractHelper;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameDateController = ProcessorTycoon.TimeSystem.DateController;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GameOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GamePlayer = ProcessorTycoon.Player;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using GameSaveId = ProcessorTycoon.Save.ISaveID;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The adapter between the contracts of the game and the pure maths of the feature (design.md, Component 6; FR-1, FR-2, FR-3, FR-12). It is the only
    /// place that reads the private members of <c>Contract</c>; everything else works on <see cref="OfferInput"/>, <see cref="RequirementReport"/> and
    /// <see cref="OddsReport"/>.
    /// <list type="bullet">
    /// <item><b>The game is the source of truth.</b> The chance of each offer comes from a call of the private <c>Contract.GetBestOffers</c> (found through
    /// <c>GameMembers</c>, never patched: the target is reserved by the core, P23). The call runs under the core's prefix, so the contract is in
    /// <c>ModifierContext.Contract</c> and the contract-dependent multipliers of the other features count as they do in the draw. The tolerance of the
    /// IPS requirement is read from the field <c>mipsToleranceMultiplier</c> of the contract.</item>
    /// <item><b>Every result is checked.</b> The verdict on the requirements against <c>Contract.FailsAtAnyRequirement</c>, the chance of the candidate
    /// against <c>Contract.ChanceToSign</c> (tolerance half a percent point); a difference is a flag on the report and a line in the log, once per
    /// kind of difference and contract (FR-12 items 1 and 2).</item>
    /// <item><b>A missing member</b> is found when the adapter is created: <see cref="IsAvailable"/> is false and <see cref="UnavailableReason"/> says which
    /// one (FR-12 item 3). The callers keep the dependent parts of the feature off then; the methods here still degrade without throwing.</item>
    /// <item><b>Keys.</b> Companies are the keys of <c>ModCompanies.KeyOf</c>; a company without one is <c>sid:&lt;SaveID&gt;</c>, read through the interface
    /// <c>ISaveID</c> (the company interface does not declare it).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ContractInspector
    {
        /// <summary>The tolerance the game's constructor sets (<c>mipsToleranceMultiplier = 0.75f</c>), used when the field cannot be read.</summary>
        public const float DefaultTolerance = 0.75f;

        /// <summary>The widest difference between the chance of the mod and the game's <c>ChanceToSign</c> that still counts as equal, in percent points.</summary>
        public const float ChanceTolerance = 0.5f;

        /// <summary><c>Player.DifficultyLevel</c> of the hardest game: the AI leaders take every kind of contract (E11).</summary>
        public const int HardestDifficulty = 5;

        private const string Feature = ContractTransparencyFeature.FeatureId;
        private const int MaxReportedKeys = 500;

        private readonly FieldInfo _toleranceField;
        private readonly MethodInfo _getBestOffers;
        private readonly MethodInfo _offerCpu;
        private readonly MethodInfo _offerChance;
        private readonly string _unavailableReason;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        public ContractInspector()
        {
            var missing = new List<string>();

            // Every member goes through GameMembers (ContractSelfCheck checks the same rows, the debug flag of TSK-020 can hide any of them).
            _toleranceField = GameMembers.Field(MemberKeys.Tolerance, typeof(GameContract), "mipsToleranceMultiplier", typeof(float));
            if (_toleranceField == null) missing.Add("field Contract.mipsToleranceMultiplier (float)" + GameMembers.HiddenNote(MemberKeys.Tolerance));

            Type offerType = GameMembers.Inner(MemberKeys.OfferType, typeof(GameContract), "CpuOffer");
            if (offerType == null)
            {
                missing.Add("nested type Contract.CpuOffer" + GameMembers.HiddenNote(MemberKeys.OfferType));
            }
            else
            {
                _offerCpu = GameMembers.Getter(MemberKeys.OfferCpu, offerType, "Cpu", typeof(GameCpu));
                if (_offerCpu == null) missing.Add("property Contract.CpuOffer.Cpu (Cpu)" + GameMembers.HiddenNote(MemberKeys.OfferCpu));

                _offerChance = GameMembers.Getter(MemberKeys.OfferChance, offerType, "Chance", typeof(int));
                if (_offerChance == null) missing.Add("property Contract.CpuOffer.Chance (int)" + GameMembers.HiddenNote(MemberKeys.OfferChance));
            }

            _getBestOffers = GameMembers.Method(MemberKeys.GetBestOffers, typeof(GameContract), "GetBestOffers", new[] { typeof(List<int>) });
            if (_getBestOffers != null && (_getBestOffers.IsStatic || !typeof(IEnumerable).IsAssignableFrom(_getBestOffers.ReturnType))) _getBestOffers = null;
            if (_getBestOffers == null) missing.Add("method Contract.GetBestOffers(List<int>)" + GameMembers.HiddenNote(MemberKeys.GetBestOffers));

            if (missing.Count > 0)
            {
                _unavailableReason = "not found in the game: " + string.Join("; ", missing.ToArray());
                ModLog.Warn(Feature, "inspector: " + _unavailableReason + "; the breakdown of chances, the review screen and the actual probability stay off");
            }
        }

        /// <summary>Every private member of the game the adapter needs was found (FR-12 item 3).</summary>
        public bool IsAvailable
        {
            get { return _unavailableReason == null; }
        }

        /// <summary>English text for the log naming the missing members; null when <see cref="IsAvailable"/>.</summary>
        public string UnavailableReason
        {
            get { return _unavailableReason; }
        }

        // ---- reading the contract ----

        /// <summary>
        /// The tolerance of the IPS requirement of this contract: the value of its field <c>mipsToleranceMultiplier</c> (0.75 in the game; another mod may
        /// change it, AC-12.1). <see cref="DefaultTolerance"/> when the field is missing (the check against <c>FailsAtAnyRequirement</c> then catches a difference).
        /// </summary>
        public float ToleranceMultiplier(GameContract contract)
        {
            if (contract == null || _toleranceField == null) return DefaultTolerance;
            return (float)_toleranceField.GetValue(contract);
        }

        /// <summary>
        /// The offers of the contract as maths input, in the order of <c>OffersIDs</c> (that order decides ties). A CPU that cannot be found gets no row and one warning:
        /// the game itself skips such an id in <c>GetBestOffers</c>.
        /// </summary>
        public List<OfferInput> CurrentOffers(GameContract contract)
        {
            var result = new List<OfferInput>();
            if (contract == null || contract.OffersIDs == null || GameCpuProvider.Instance == null) return result;

            List<int> ids = contract.OffersIDs;
            for (int i = 0; i < ids.Count; i++)
            {
                GameCpu cpu = GameCpuProvider.Instance.GetCpu(ids[i]);
                if (cpu == null)
                {
                    ModLog.Warn(Feature, "inspector: the offer " + ids[i].ToString(CultureInfo.InvariantCulture) + " of a contract names a CPU that does not exist; it is skipped");
                    continue;
                }
                result.Add(ToInput(cpu, i));
            }
            return result;
        }

        /// <summary>
        /// The chances the game itself gives a set of offers: calls the private <c>Contract.GetBestOffers</c> for <paramref name="offerIds"/> and reads
        /// <c>CpuOffer.Cpu</c> and <c>CpuOffer.Chance</c> of every row. The result is in the game's order (the draw order), as (CPU id, chance). The method
        /// does not change the contract. Null when the method is not available or the call failed (the failure is logged).
        /// </summary>
        public List<KeyValuePair<int, int>> GameChances(GameContract contract, List<int> offerIds)
        {
            if (contract == null || offerIds == null || _getBestOffers == null || _offerCpu == null || _offerChance == null) return null;

            try
            {
                var rows = _getBestOffers.Invoke(contract, new object[] { offerIds }) as IEnumerable;
                if (rows == null) return null;

                var result = new List<KeyValuePair<int, int>>();
                foreach (object offer in rows)
                {
                    var cpu = _offerCpu.Invoke(offer, null) as GameCpu;
                    if (cpu == null) continue;
                    result.Add(new KeyValuePair<int, int>(cpu.SaveID, (int)_offerChance.Invoke(offer, null)));
                }
                return result;
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                ModLog.Warn(Feature, "inspector: Contract.GetBestOffers failed: " + cause.GetType().Name + ": " + cause.Message);
                return null;
            }
        }

        // ---- the reports ----

        /// <summary>
        /// The requirements of <paramref name="contract"/> against <paramref name="cpu"/> (FR-1), checked against <c>Contract.FailsAtAnyRequirement</c>
        /// (FR-12 item 1): a difference sets <see cref="RequirementReport.MismatchWithGame"/> and writes the numbers to the log.
        /// </summary>
        public RequirementReport BuildRequirements(GameContract contract, GameCpu cpu)
        {
            if (contract == null) throw new ArgumentNullException("contract");
            if (cpu == null) throw new ArgumentNullException("cpu");

            float tolerance = ToleranceMultiplier(contract);
            GamePackage package = cpu.Package;

            var report = new RequirementReport
            {
                Tolerance = tolerance,
                Ips = RequirementCheck.Ips(cpu.Ips, contract.MinIps, tolerance),
                Power = RequirementCheck.Power(cpu.PowerConsumption, contract.MaxPowerConsumption),
                Package = RequirementCheck.Package(
                    contract.PackageType,
                    package != null && package.IsDip,
                    package != null && package.IsPlcc,
                    package != null && package.IsPgaOrLga),
                RequiredPackageText = contract.PackageTypeString(),
                CpuPackageText = package == null ? string.Empty : package.PackageTypeString(),
            };
            report.FailsByMod = RequirementCheck.AnyFailed(report.Results);
            report.FailsByGame = contract.FailsAtAnyRequirement(cpu);

            if (report.MismatchWithGame)
            {
                ReportOnce(
                    "requirements|" + ContractTag(contract) + "|" + cpu.SaveID.ToString(CultureInfo.InvariantCulture),
                    "requirements differ from the game for " + ContractTag(contract) + " and CPU " + cpu.SaveID.ToString(CultureInfo.InvariantCulture) +
                    ": the mod says " + (report.FailsByMod ? "fails" : "passes") + ", the game says " + (report.FailsByGame ? "fails" : "passes") +
                    " (IPS " + Num(cpu.Ips) + " of limit " + Num(report.Ips.Limit) + ", tolerance " + Num(tolerance) +
                    "; power " + Num(cpu.PowerConsumption) + " of " + Num(contract.MaxPowerConsumption) + "; package type " +
                    contract.PackageType.ToString(CultureInfo.InvariantCulture) + ")");
            }
            return report;
        }

        /// <summary>
        /// The analysis of the chance of <paramref name="candidate"/> in <paramref name="contract"/> (FR-2, FR-12 item 2). The offers are those of the
        /// contract with the offers of the candidate's company replaced by the candidate (the rule of <c>Contract.ChanceToSign</c>); the chance of each
        /// comes from the game; the probability of the draw and the hints are computed by <see cref="OddsMath"/>; the multipliers of the mods come
        /// from <c>ModModifiers.Explain("contract.signChance", ...)</c> with the contract in the context.
        /// </summary>
        public OddsReport BuildOdds(GameContract contract, GameCpu candidate)
        {
            if (contract == null) throw new ArgumentNullException("contract");
            if (candidate == null) throw new ArgumentNullException("candidate");

            using (PerfTimer.Start(TimingSites.Odds))
            {
                return BuildOddsCore(contract, candidate);
            }
        }

        private OddsReport BuildOddsCore(GameContract contract, GameCpu candidate)
        {
            var report = new OddsReport();
            List<OfferInput> offers = CurrentOffers(contract);
            OfferInput self = ToInput(candidate, offers.Count);
            report.CandidateCompanyKey = self.CompanyKey;
            report.CandidateCpuId = self.CpuId;
            report.NoOffersYet = contract.OffersIDs == null || contract.OffersIDs.Count == 0;

            List<OfferInput> inPlay = OddsMath.WithCandidate(offers, self);
            List<OfferInput> best = OddsMath.BestPerCompany(inPlay);
            report.BestOffers = best;

            // The chances of the game for the offers in play; vanilla chances stand in when the game cannot be asked or leaves an offer out.
            var ids = new List<int>(inPlay.Count);
            for (int i = 0; i < inPlay.Count; i++) ids.Add(inPlay[i].CpuId);
            List<KeyValuePair<int, int>> fromGame = GameChances(contract, ids);
            report.GameChancesAvailable = fromGame != null;

            int[] chances = null;
            if (fromGame != null)
            {
                float sum = OddsMath.SumIps(best);
                chances = new int[best.Count];
                for (int i = 0; i < best.Count; i++)
                {
                    int chance;
                    chances[i] = TryFind(fromGame, best[i].CpuId, out chance) ? chance : OddsMath.VanillaChance(best[i].Ips, sum);
                }
            }
            report.Rows = OddsMath.DrawOrder(best, chances);

            OfferOdds? own = report.CandidateRow;
            if (own.HasValue)
            {
                report.CandidateVanillaChance = own.Value.VanillaChance;
                report.CandidateGameChance = own.Value.GameChance;
                report.CandidateActual = own.Value.ActualProbability;
            }

            CheckAgainstChanceToSign(contract, candidate, report);
            ExplainMods(contract, candidate, report);

            // Hints: what the candidate has to beat.
            List<OfferInput> rivals = OddsMath.WithoutCompany(best, self.CompanyKey);
            report.IpsFor50 = OddsMath.IpsForChance(0.5, OddsMath.SumIps(rivals));
            report.IpsForFirstInQueue = OddsMath.MaxIps(rivals);
            report.ShadowedOwnCpuIds = OddsMath.ShadowedOwn(offers, self);
            report.StrongestOwnCpuId = OddsMath.StrongestOwnCpuId(offers, self);
            return report;
        }

        /// <summary>
        /// The obligations of the offer of <paramref name="cpu"/> to <paramref name="contract"/> and their risks (FR-3): the figures are read from the
        /// game and given to <see cref="CommitmentMath"/>. <paramref name="actualProbability"/> is the probability of winning (<see cref="OddsReport.CandidateActual"/>).
        /// </summary>
        public CommitmentReport BuildCommitments(GameContract contract, GameCpu cpu, double actualProbability)
        {
            if (contract == null) throw new ArgumentNullException("contract");
            if (cpu == null) throw new ArgumentNullException("cpu");

            ICompany manufacturer = cpu.Manufacturer;
            GameOwner manufacturerOwner = manufacturer == null ? null : manufacturer.Owner;

            var input = new CommitmentInput
            {
                Units = contract.Units,
                PaymentPerUnit = contract.PaymentPerUnit,
                FinePerUnit = contract.FinePerUnit,
                RealUnitCostAtPayment = cpu.RealUnitCostWithNewPrice(contract.PaymentPerUnit),
                RequiredLines = GameProductionMath.RequiredProductionLines(cpu, contract.Units),
                CapacityLines = manufacturer == null || manufacturer.Factory == null ? 0 : manufacturer.Factory.ProductionCapacity,
                ContractedLines = manufacturerOwner == null ? 0 : GameContractHelper.ContractedProductionLines(manufacturerOwner),
                PendingOfferLines = PendingOfferLines(contract, cpu),
                AutomateRejection = GamePlayer.Instance != null && GamePlayer.Instance.AutomateRejection,
                AutomationMarginPercent = GamePlayer.Instance == null ? 0 : GamePlayer.Instance.ContractProfitMargin,
                CompanyCash = cpu.Company == null ? 0f : cpu.Company.MoneyAmount,
                ActualProbability = actualProbability,
            };

            if (contract.AcceptanceDate != null && contract.TerminationDate != null && GameDateController.Instance != null)
            {
                DateTime accept = (DateTime)contract.AcceptanceDate;
                DateTime end = (DateTime)contract.TerminationDate;
                input.TermDays = GameDateController.Instance.DaysUntil(accept, end);
                input.TermMonths = Math.Max(0, (end.Year - accept.Year) * 12 + (end.Month - accept.Month));
            }

            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            return CommitmentMath.Evaluate(input, feature == null ? ContractRules.Default : feature.Rules);
        }

        // ---- possible participants (FR-11) ----

        /// <summary>
        /// The AI offers that may still reach the contract (FR-11 item 1): the best CPU of every AI company that has one which passes the requirements of the contract
        /// (<c>FailsAtAnyRequirement</c> is false), is not withdrawn, is for sale on contracts and whose margin at the contract's price is not below the AI's threshold
        /// (<c>ai.contractMarginThreshold</c> of <c>rules.json</c>, the literal of <c>AIBehaviourContract.OnTick</c>). One row per company, the companies in the order of the world's list; bankrupt
        /// companies and the player's are left out. It is an estimate: the AI's focus on kinds of contracts, the one CPU per market the AI picks in a month and the lines the AI may lack
        /// when the contract is drawn are not looked at. Never throws; a CPU the game cannot answer for is skipped (one line in the log).
        /// </summary>
        public List<OfferInput> PotentialAiBidders(GameContract contract)
        {
            var result = new List<OfferInput>();
            if (contract == null) return result;

            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            double threshold = feature == null ? ContractRules.DefaultAiContractMarginThreshold : feature.Rules.AiContractMarginThreshold;
            float payment = contract.PaymentPerUnit;

            try
            {
                var facts = new List<AiCpuFact>();
                IReadOnlyList<ICompany> companies = ModCompanies.All;
                for (int i = 0; i < companies.Count; i++)
                {
                    ICompany company = companies[i];
                    if (company == null || company.IsPlayer || company.IsBankrupt) continue;

                    string key = CompanyKeyOf(company);
                    GameOwner owner = company.Owner;
                    List<GameCpu> cpus = owner == null ? null : owner.GetCpus();
                    if (key == null || cpus == null) continue;

                    for (int c = 0; c < cpus.Count; c++)
                    {
                        GameCpu cpu = cpus[c];
                        if (cpu == null) continue;
                        facts.Add(new AiCpuFact
                        {
                            CompanyKey = key,
                            CpuId = cpu.SaveID,
                            Ips = cpu.Ips,
                            IsRetired = cpu.IsRetired,
                            SellOnContracts = cpu.SellOnContracts,
                            FailsRequirements = () => contract.FailsAtAnyRequirement(cpu),
                            PaymentPerUnit = payment,
                            UnitCostAtPayment = () => cpu.RealUnitCostWithNewPrice(payment),
                        });
                    }
                }

                result = AiBidRules.BestQualifying(facts, threshold, (fact, ex) =>
                    ReportOnce("aibidder|" + fact.CpuId.ToString(CultureInfo.InvariantCulture),
                        "possible participants: CPU " + fact.CpuId.ToString(CultureInfo.InvariantCulture) + " of " + fact.CompanyKey + " is skipped: " + ex.GetType().Name + ": " + ex.Message));
            }
            catch (Exception ex)
            {
                ModLog.Warn(Feature, "inspector: the possible participants could not be listed: " + ex.GetType().Name + ": " + ex.Message);
                return new List<OfferInput>();
            }
            return result;
        }

        /// <summary>
        /// The estimate of the possible participants for the analysed CPU (FR-11): the monthly moves of the AI left before the review date, the AI offers that may still come
        /// (<see cref="PotentialAiBidders"/> without those that change nothing), the probability of the CPU if all of them come, and the note for difficulty 5. With no move left
        /// (<see cref="PotentialReport.IsShown"/> is false) the AI is not asked at all. <paramref name="odds"/> is the report <see cref="BuildOdds"/> made for the same contract and CPU.
        /// </summary>
        public PotentialReport BuildPotential(GameContract contract, GameCpu candidate, OddsReport odds)
        {
            if (contract == null) throw new ArgumentNullException("contract");
            if (candidate == null) throw new ArgumentNullException("candidate");
            if (odds == null) throw new ArgumentNullException("odds");

            var report = new PotentialReport { CurrentActual = odds.CandidateActual, PessimisticActual = odds.CandidateActual };

            DateTime today = ModTicks.Today;
            if (today == default(DateTime) || contract.AcceptanceDate == null) return report;

            report.MovesLeft = AiMoveCalendar.MovesBefore(today, contract.AcceptanceDate.Year, contract.AcceptanceDate.Month);
            if (report.MovesLeft <= 0) return report;

            report.HardDifficulty = GamePlayer.Instance != null && GamePlayer.Instance.DifficultyLevel == HardestDifficulty;
            try
            {
                PotentialEstimate estimate = PotentialBidders.Estimate(
                    odds.BestOffers, PotentialAiBidders(contract), odds.CandidateCompanyKey, odds.CandidateCpuId, odds.CandidateActual, odds.ModMultiplier);
                report.NewBidders = estimate.NewBidders;
                report.PessimisticActual = estimate.PessimisticActual;
            }
            catch (Exception ex)
            {
                // The block is left out rather than shown with numbers nobody can trust.
                ModLog.Warn(Feature, "inspector: the estimate of the possible participants failed: " + ex.GetType().Name + ": " + ex.Message);
                report.MovesLeft = 0;
                report.NewBidders = new List<OfferInput>();
            }
            return report;
        }

        // ---- keys ----

        /// <summary>
        /// The key of a company in the logic and the log: <c>ModCompanies.KeyOf</c> (<c>player</c>, <c>ai:&lt;id&gt;</c>), otherwise <c>sid:&lt;SaveID&gt;</c> read through
        /// <c>ISaveID</c>; null for no company.
        /// </summary>
        public static string CompanyKeyOf(ICompany company)
        {
            if (company == null) return null;

            string key = ModCompanies.KeyOf(company);
            if (key != null) return key;

            var withId = company as GameSaveId;
            return withId == null ? null : "sid:" + withId.SaveID.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The key of a contract in the log (D7): client, the review month, units, payment and type (<see cref="ContractKeyBuilder"/>). A contract of the game has
        /// no id of its own that survives a load. An empty string for no contract.
        /// </summary>
        public static string ContractKey(GameContract contract)
        {
            if (contract == null) return string.Empty;

            int year = contract.AcceptanceDate == null ? 0 : contract.AcceptanceDate.Year;
            int month = contract.AcceptanceDate == null ? 0 : contract.AcceptanceDate.Month;
            return ContractKeyBuilder.Build(contract.Company, year, month, contract.Units, contract.PaymentPerUnit, (int)contract.ContractType);
        }

        /// <summary>
        /// The contract for people: "Apple - Computer, 52 000 units a month" (the template is <c>ct.contract.label</c>). The name of the client is the game's own,
        /// not translated (NFR-4); it is not escaped here, the text that goes to the screen is (<see cref="TextSafe.EscapeMarkup"/>).
        /// </summary>
        public static string ContractLabel(GameContract contract)
        {
            if (contract == null) return string.Empty;

            string type;
            switch (contract.ContractType)
            {
                case ProcessorTycoon.ContractSystem.ContractType.Calculator: type = ModL10n.L("ct.contract.type.calculator"); break;
                case ProcessorTycoon.ContractSystem.ContractType.Computer: type = ModL10n.L("ct.contract.type.computer"); break;
                case ProcessorTycoon.ContractSystem.ContractType.Console: type = ModL10n.L("ct.contract.type.console"); break;
                default: type = contract.ContractType.ToString(); break;
            }
            string units = GameFormatter.FloatToIntString(contract.Units, string.Empty, false);
            return TextSafe.Clean(ModL10n.L("ct.contract.label", contract.Company ?? string.Empty, type, units));
        }

        /// <summary>The name of a company as the game shows it, or an empty string.</summary>
        public static string CompanyNameOf(ICompany company)
        {
            return company == null ? string.Empty : (company.Name ?? string.Empty);
        }

        // ---- private ----

        private static OfferInput ToInput(GameCpu cpu, int firstSeenIndex)
        {
            return new OfferInput(CompanyKeyOf(cpu.Company), cpu.SaveID, cpu.Ips, firstSeenIndex);
        }

        private static bool TryFind(List<KeyValuePair<int, int>> chances, int cpuId, out int chance)
        {
            for (int i = 0; i < chances.Count; i++)
            {
                if (chances[i].Key == cpuId)
                {
                    chance = chances[i].Value;
                    return true;
                }
            }
            chance = 0;
            return false;
        }

        /// <summary>
        /// FR-12 item 2: the chance of the candidate against <c>Contract.ChanceToSign</c>. A contract without offers is not compared: the game answers 100
        /// before it looks at any chance there, and the analysis shows that answer.
        /// </summary>
        private void CheckAgainstChanceToSign(GameContract contract, GameCpu candidate, OddsReport report)
        {
            float official;
            try
            {
                official = contract.ChanceToSign(candidate);
            }
            catch (Exception ex)
            {
                ModLog.Warn(Feature, "inspector: Contract.ChanceToSign failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            report.GameChanceToSign = official;
            if (float.IsNaN(official) || float.IsInfinity(official)) return;

            int shown = (int)Math.Round(official);
            if (report.NoOffersYet)
            {
                report.CandidateGameChance = shown;
                return;
            }

            if (Math.Abs(official - report.CandidateGameChance) <= ChanceTolerance) return;

            report.MismatchWithGame = true;
            ReportOnce(
                "chance|" + ContractTag(contract) + "|" + candidate.SaveID.ToString(CultureInfo.InvariantCulture),
                "chance differs from the game for " + ContractTag(contract) + " and CPU " + candidate.SaveID.ToString(CultureInfo.InvariantCulture) +
                ": the mod computed " + report.CandidateGameChance.ToString(CultureInfo.InvariantCulture) + " %, Contract.ChanceToSign says " +
                Num(official) + " %; the game's value is shown");
            report.CandidateGameChance = shown;
        }

        /// <summary>FR-2 item 3: the multipliers behind the chance, from the core's breakdown of the channel <c>contract.signChance</c> with the contract in the context.</summary>
        private void ExplainMods(GameContract contract, GameCpu candidate, OddsReport report)
        {
            ModifierBreakdown breakdown = null;
            try
            {
                var context = new ModifierContext
                {
                    Company = candidate.Company,
                    CompanyKey = ModCompanies.KeyOf(candidate.Company),
                    Cpu = candidate,
                    Contract = contract,
                };
                breakdown = ModModifiers.Explain(ModChannels.ContractSignChance, context);
            }
            catch (Exception ex)
            {
                ModLog.Warn(Feature, "inspector: the breakdown of contract.signChance failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            report.ChanceDiffersFromVanilla = report.CandidateGameChance != report.CandidateVanillaChance;
            if (breakdown == null)
            {
                report.ModMultiplier = report.CandidateVanillaChance > 0 ? (float)report.CandidateGameChance / report.CandidateVanillaChance : 1f;
                return;
            }

            foreach (AppliedModifier applied in breakdown)
            {
                report.ModSources.Add(new ModifierSourceRow
                {
                    FeatureId = applied.FeatureId,
                    ReasonKey = applied.ReasonKey,
                    ReasonArgs = applied.ReasonArgs ?? new string[0],
                    Multiplier = applied.Multiplier,
                });
            }
            report.ModsCapped = breakdown.IsBalanceCapped || breakdown.IsFuseCapped;
            report.ModMultiplier = breakdown.Total;
        }

        /// <summary>The lines promised in the pending offers of the CPU's company, not counting this contract (the figure the review screen adds the new offer to).</summary>
        private static int PendingOfferLines(GameContract contract, GameCpu cpu)
        {
            GameOwner owner = cpu.Company == null ? null : cpu.Company.Owner;
            if (owner == null) return 0;

            int all = GameContractHelper.FutureProductionLinesDemand(owner);
            int thisContract = 0;
            List<GameCpu> cpus = owner.GetCpus();
            if (cpus != null)
            {
                for (int i = 0; i < cpus.Count; i++)
                {
                    if (contract.HasReceivedOffer(cpus[i].SaveID)) thisContract += GameProductionMath.RequiredProductionLines(cpus[i], contract.Units);
                }
            }
            return Math.Max(0, all - thisContract);
        }

        /// <summary>A contract has no id of its own (D7); this is enough to tell two apart in a log line: client, template id, review date.</summary>
        private static string ContractTag(GameContract contract)
        {
            string review = contract.AcceptanceDate == null
                ? "?"
                : contract.AcceptanceDate.Year.ToString(CultureInfo.InvariantCulture) + "-" + contract.AcceptanceDate.Month.ToString(CultureInfo.InvariantCulture);
            return "contract " + (contract.Company ?? "?") + " #" + contract.ContractID.ToString(CultureInfo.InvariantCulture) + " (" + review + ")";
        }

        private void ReportOnce(string key, string message)
        {
            if (_reported.Count >= MaxReportedKeys) _reported.Clear();   // a long game must not grow the set without end
            if (_reported.Add(key)) ModLog.Warn(Feature, "inspector: " + message);
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
