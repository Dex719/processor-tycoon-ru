using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>One line of the preview: a plain text, or a title with a value; with an optional tooltip.</summary>
    public sealed class PreviewLine
    {
        /// <summary>The title of a "title: value" pair; null for a plain text line.</summary>
        public string Title;

        /// <summary>The value, or the whole text of a plain line.</summary>
        public string Text = "";

        public ViewTone Tone;

        /// <summary>The tooltip (<c>ModUI.Tooltip</c>); null for none.</summary>
        public string TipHeader;

        public string TipBody;

        public bool IsPair
        {
            get { return Title != null; }
        }

        public bool HasTip
        {
            get { return !string.IsNullOrEmpty(TipBody); }
        }
    }

    /// <summary>A block of the preview: a heading and its lines.</summary>
    public sealed class PreviewSection
    {
        /// <summary>The key of the heading (<see cref="UiKeys"/>).</summary>
        public string TitleKey = "";

        public string Title = "";
        public List<PreviewLine> Lines = new List<PreviewLine>();
    }

    /// <summary>
    /// What the preview of a purchase shows (TSK-016; FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-12, NFR-5; design.md "Превью сделки"): the blocks "Valuation" (with the
    /// note when the cash floor set the price), "Money", "What passes", "What is retired or terminated", "Warnings" and "Consequences", the reasons that
    /// block the deal, and the question of the confirmation. Built from the quote and the plan by this pure class, so the window only draws it; the plan
    /// is built again each time the choice of lines or the tick of the consequences changes.
    /// </summary>
    public sealed class DealPreviewModel
    {
        /// <summary>The most names listed in a tooltip; the rest are counted.</summary>
        public const int TipNames = 25;

        public DealType Type;
        public string TargetName = "";
        public string Title = "";

        /// <summary>The deal is open (the quote); the plan may still be blocked by the unticked consequences.</summary>
        public bool Available;

        /// <summary>The plan has no blocking error: the button "Buy" may be pressed (unless the gate says a deal runs).</summary>
        public bool CanBuy;

        /// <summary>Some consequence needs the tick "I understand" (FR-12 item 5).</summary>
        public bool NeedsAck;

        /// <summary>The target has lines, so the player can choose to keep or sell them.</summary>
        public bool HasLines;

        /// <summary>The lines have to be kept (the target makes CPUs of the player): the choice is locked.</summary>
        public bool KeepLinesRequired;

        public bool KeepLines;

        /// <summary>What the sale of the lines brings in this plan (0 while the lines are kept).</summary>
        public double SaleIncome;

        /// <summary>What the sale of all the lines would bring: the number on the button "Sell the lines".</summary>
        public double LinesSaleValue;

        public double Price;

        /// <summary>The player's balance after the deal, with the money from selling lines.</summary>
        public double MoneyAfter;

        public string ConfirmTitle = "";
        public string ConfirmMessage = "";

        /// <summary>The reasons the deal cannot go ahead, except the missing tick (that one is the toggle).</summary>
        public List<PreviewLine> Problems = new List<PreviewLine>();

        public List<PreviewSection> Sections = new List<PreviewSection>();

        /// <summary>
        /// Builds the preview. <paramref name="related"/> is what step 9 will pass on from the related features (FR-16: architectures, the GPU lineup); null
        /// when nothing is known of them, which shows no such lines.
        /// </summary>
        public static DealPreviewModel Build(DealQuote q, AcquisitionPlan plan, CompanySnapshot t, PlayerSnapshot p, ViewFormat f, RelatedStatePlan related = null)
        {
            if (f == null) f = new ViewFormat();
            if (plan == null) plan = new AcquisitionPlan { Type = q.Type };
            if (t == null) t = new CompanySnapshot();
            if (p == null) p = new PlayerSnapshot();

            var m = new DealPreviewModel
            {
                Type = q.Type,
                TargetName = t.Name ?? "",
                Available = q.Available,
                CanBuy = plan.CanExecute,
                NeedsAck = plan.HasConsequencesToAcknowledge(),
                HasLines = t.TotalLines > 0,
                KeepLinesRequired = plan.KeepLinesRequired,
                KeepLines = plan.KeepLines,
                SaleIncome = plan.SaleIncome,
                LinesSaleValue = SaleValueOf(t),
                Price = q.Price,
            };
            m.MoneyAfter = q.MoneyAfter + m.SaleIncome;
            m.Title = f.T(TitleKey(q.Type), m.TargetName);
            m.ConfirmTitle = f.T(UiKeys.ConfirmTitle);
            m.ConfirmMessage = f.T(ConfirmKey(q.Type), m.TargetName, f.M(q.Price), f.M(m.MoneyAfter));

            AddProblems(m, plan, f);
            AddValuation(m, q, t, plan, f);
            AddMoney(m, q, plan, f);
            AddTransfers(m, plan, t, f, related);
            AddRemoved(m, plan, t, f);
            AddWarnings(m, plan, f);
            AddConsequences(m, plan, f);
            return m;
        }

        /// <summary>The game's sale cost of all the target's lines.</summary>
        public static double SaleValueOf(CompanySnapshot t)
        {
            double sum = 0.0;
            if (t == null || t.Lines == null) return sum;
            for (int i = 0; i < t.Lines.Count; i++)
            {
                WaferLinesSnapshot w = t.Lines[i];
                if (w != null && w.Lines > 0 && w.SaleCost > 0.0 && !double.IsInfinity(w.SaleCost)) sum += w.SaleCost;
            }
            return sum;
        }

        public static string TitleKey(DealType type)
        {
            switch (type)
            {
                case DealType.BankruptAssets: return UiKeys.PreviewTitleBankrupt;
                case DealType.Distressed: return UiKeys.PreviewTitleDistressed;
                default: return UiKeys.PreviewTitleHostile;
            }
        }

        public static string ConfirmKey(DealType type)
        {
            switch (type)
            {
                case DealType.BankruptAssets: return UiKeys.ConfirmBankrupt;
                case DealType.Distressed: return UiKeys.ConfirmDistressed;
                default: return UiKeys.ConfirmHostile;
            }
        }

        // ---- the reasons ----

        private static void AddProblems(DealPreviewModel m, AcquisitionPlan plan, ViewFormat f)
        {
            for (int i = 0; i < plan.BlockingErrors.Count; i++)
            {
                string key = plan.BlockingErrors[i];
                if (key == AcqKeys.PlanAckRequired) continue;   // the toggle of the consequences says it
                string arg = i < plan.BlockingArgs.Count ? plan.BlockingArgs[i] : null;
                m.Problems.Add(new PreviewLine { Text = ReasonText.Of(f, key, arg), Tone = ViewTone.Negative });
            }
        }

        // ---- the valuation ----

        private static void AddValuation(DealPreviewModel m, DealQuote q, CompanySnapshot t, AcquisitionPlan plan, ViewFormat f)
        {
            PreviewSection s = Section(m, UiKeys.SectionValuation, f);
            Valuation v = q.Valuation;

            var parts = new PreviewLine { Text = f.T(UiKeys.PreviewValuation, f.M(v.AFab), f.M(v.ATech), f.M(v.ACpu), f.M(v.SharePremium)) };
            string tip = ValuationTip(t, plan, f);
            if (tip.Length > 0)
            {
                parts.TipHeader = f.T(UiKeys.TipValuationHeader);
                parts.TipBody = tip;
            }
            s.Lines.Add(parts);

            s.Lines.Add(Pair(f, UiKeys.LabelValuation, f.M(v.E)));
            s.Lines.Add(Pair(f, UiKeys.LabelMultiplier, f.N(q.Multiplier)));
            if (Math.Abs(q.Channel - 1.0) > 1e-9) s.Lines.Add(Pair(f, UiKeys.LabelChannel, f.N(q.Channel)));
            if (q.OwnedShare > 0.0) s.Lines.Add(Pair(f, UiKeys.LabelOwnedShare, f.P(q.OwnedShare)));

            PreviewLine price = Pair(f, UiKeys.LabelPrice, f.M(q.Price));
            s.Lines.Add(price);
            if (q.CashFloorApplied) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewCashFloor, f.M(q.CashFloor)), Tone = ViewTone.Warning });
        }

        private static string ValuationTip(CompanySnapshot t, AcquisitionPlan plan, ViewFormat f)
        {
            var text = new StringBuilder();

            if (plan.TechUnlocks.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < plan.TechUnlocks.Count; i++) names.Add(plan.TechUnlocks[i].Name);
                text.Append(f.T(UiKeys.TipTechs, f.I(names.Count), NameList(names, f)));
            }

            var cpus = new List<string>();
            for (int i = 0; i < plan.TransferFamilies.Count; i++)
            {
                for (int j = 0; j < plan.TransferFamilies[i].Members.Count; j++)
                {
                    FamilyMember member = plan.TransferFamilies[i].Members[j];
                    if (member.Fate == CpuFate.Live || member.Fate == CpuFate.Revive) cpus.Add(member.Name);
                }
            }
            if (cpus.Count > 0)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(f.T(UiKeys.TipCpus, f.I(cpus.Count), NameList(cpus, f)));
            }
            return text.ToString();
        }

        // ---- the money ----

        private static void AddMoney(DealPreviewModel m, DealQuote q, AcquisitionPlan plan, ViewFormat f)
        {
            PreviewSection s = Section(m, UiKeys.SectionMoney, f);

            s.Lines.Add(Pair(f, UiKeys.LabelPrice, f.M(q.Price)));

            if (q.Type == DealType.BankruptAssets)
            {
                s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewCashStays) });
            }
            else if (q.AssumedCash >= 0.0)
            {
                s.Lines.Add(Pair(f, UiKeys.LabelCash, f.M(q.AssumedCash), q.AssumedCash > 0.0 ? ViewTone.Positive : ViewTone.Neutral));
            }
            else
            {
                s.Lines.Add(Pair(f, UiKeys.LabelDebt, f.M(-q.AssumedCash), ViewTone.Negative));
            }

            s.Lines.Add(Pair(f, UiKeys.LabelNet, f.M(q.NetCash), q.NetCash < 0.0 ? ViewTone.Negative : ViewTone.Neutral));

            if (plan.SaleIncome > 0.0) s.Lines.Add(Pair(f, UiKeys.LabelSaleIncome, f.M(plan.SaleIncome), ViewTone.Positive));

            s.Lines.Add(Pair(f, UiKeys.LabelBalanceAfter, f.M(m.MoneyAfter), m.MoneyAfter < 0.0 ? ViewTone.Negative : ViewTone.Neutral));

            if (q.ReputationDelta != 0)
            {
                string delta = (q.ReputationDelta > 0 ? "+" : "") + f.I(q.ReputationDelta);
                s.Lines.Add(Pair(f, UiKeys.LabelReputation, delta, q.ReputationDelta > 0 ? ViewTone.Positive : ViewTone.Negative));
            }
        }

        // ---- what passes ----

        private static void AddTransfers(DealPreviewModel m, AcquisitionPlan plan, CompanySnapshot t, ViewFormat f, RelatedStatePlan related)
        {
            PreviewSection s = Section(m, UiKeys.SectionTransfers, f);

            // lines
            if (plan.KeepLines && plan.LineMoves.Count > 0)
            {
                var items = new List<string>();
                for (int i = 0; i < plan.LineMoves.Count; i++)
                {
                    LineMove move = plan.LineMoves[i];
                    items.Add(move.Remapped
                        ? f.T(UiKeys.PreviewLineItemMoved, f.I(move.Lines), f.N(move.FromValue), f.N(move.ToValue))
                        : f.T(UiKeys.PreviewLineItem, f.I(move.Lines), f.N(move.FromValue)));
                }
                s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewLines, string.Join(", ", items.ToArray())) });
                if (plan.OpexDeltaPerMonth > 0.0) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewOpex, f.M(plan.OpexDeltaPerMonth)), Tone = ViewTone.Warning });
            }
            else if (!plan.KeepLines && plan.LineSales.Count > 0)
            {
                var items = new List<string>();
                for (int i = 0; i < plan.LineSales.Count; i++) items.Add(f.T(UiKeys.PreviewLineItem, f.I(plan.LineSales[i].Lines), f.N(plan.LineSales[i].Value)));
                s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewLinesSold, string.Join(", ", items.ToArray())) });
            }

            // technologies and experience
            if (plan.TechUnlocks.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < plan.TechUnlocks.Count; i++) names.Add(plan.TechUnlocks[i].Name);
                s.Lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewTechs, f.I(plan.TechUnlocks.Count)) }, f.T(UiKeys.TipTechsHeader), names, f));
            }
            if (plan.TechProgress.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < plan.TechProgress.Count; i++) names.Add(plan.TechProgress[i].Name);
                s.Lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewTechsPartial, f.I(plan.TechProgress.Count)) }, f.T(UiKeys.TipTechsHeader), names, f));
            }
            if (plan.NodeRaises.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < plan.NodeRaises.Count; i++) names.Add(plan.NodeRaises[i].Name);
                s.Lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewNodes, f.I(plan.NodeRaises.Count)) }, f.T(UiKeys.TipNodesHeader), names, f));
            }

            // CPUs and the contracts that follow them
            int cpuCount = 0;
            int retiredCount = 0;
            int oem = 0;
            var cpuNames = new List<string>();
            for (int i = 0; i < plan.TransferFamilies.Count; i++)
            {
                for (int j = 0; j < plan.TransferFamilies[i].Members.Count; j++)
                {
                    FamilyMember member = plan.TransferFamilies[i].Members[j];
                    cpuCount++;
                    oem += member.OemContracts;
                    cpuNames.Add(member.Name);
                    if (member.Fate == CpuFate.StaysRetired || member.Fate == CpuFate.Retire) retiredCount++;
                }
            }
            if (cpuCount > 0)
            {
                s.Lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewCpus, f.I(cpuCount)) }, f.T(UiKeys.TipCpusHeader), cpuNames, f));
                if (retiredCount > 0) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewCpusRetired, f.I(retiredCount)) });
                if (oem > 0) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewOem, f.I(oem)) });
            }
            if (plan.PlayerCpusToInHouse.Count > 0) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewInHouse, f.I(plan.PlayerCpusToInHouse.Count)) });

            // contracts
            if (plan.RepointProviderContractIds.Count > 0)
            {
                var clients = new List<string>();
                for (int i = 0; i < plan.RepointProviderContractIds.Count; i++)
                {
                    ContractSnapshot c = ContractOf(t.ProviderContracts, plan.RepointProviderContractIds[i]);
                    if (c != null) clients.Add(c.ClientName);
                }
                s.Lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewProviderContracts, f.I(plan.RepointProviderContractIds.Count)) }, f.T(UiKeys.TipContractsHeader), clients, f));
            }
            for (int i = 0; i < plan.RepointClientContractIds.Count; i++)
            {
                ContractSnapshot c = ContractOf(t.ClientContracts, plan.RepointClientContractIds[i]);
                if (c != null) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewClientContract, c.ClientName, c.ProviderName) });
            }
            if (plan.EnableFoundryServices) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewFoundryFlag) });

            // the state of the related features (FR-16, step 9)
            if (related != null)
            {
                if (related.Architectures) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewRelatedArchitectures) });
                if (related.GpuLineup) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewRelatedGpu) });
                if (related.GpuNotPassed) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewGpuNotPassed), Tone = ViewTone.Warning });
            }

            if (s.Lines.Count == 0) s.Lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewNothing) });
        }

        // ---- what is retired or terminated ----

        private static void AddRemoved(DealPreviewModel m, AcquisitionPlan plan, CompanySnapshot t, ViewFormat f)
        {
            var lines = new List<PreviewLine>();

            // CPUs retired by the deal: the plan lists their ids; the names come from the snapshot.
            if (plan.RetireCpuIds.Count > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < plan.RetireCpuIds.Count; i++) names.Add(CpuName(t, plan.RetireCpuIds[i]));
                lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewRetireCpus, f.I(names.Count)), Tone = ViewTone.Warning }, f.T(UiKeys.TipCpusHeader), names, f));
            }

            for (int i = 0; i < plan.DroppedClientContractIds.Count; i++)
            {
                ContractSnapshot c = ContractOf(t.ClientContracts, plan.DroppedClientContractIds[i]);
                if (c != null) lines.Add(new PreviewLine { Text = f.T(UiKeys.PreviewContractDropped, c.ProviderName + " - " + c.ClientName), Tone = ViewTone.Warning });
            }

            if (plan.DroppedProviderContractIds.Count > 0)
            {
                var clients = new List<string>();
                for (int i = 0; i < plan.DroppedProviderContractIds.Count; i++)
                {
                    ContractSnapshot c = ContractOf(t.ProviderContracts, plan.DroppedProviderContractIds[i]);
                    if (c != null) clients.Add(c.ClientName);
                }
                lines.Add(WithNames(new PreviewLine { Text = f.T(UiKeys.PreviewProviderDropped, f.I(plan.DroppedProviderContractIds.Count)), Tone = ViewTone.Warning },
                    f.T(UiKeys.TipContractsHeader), clients, f));
            }

            if (lines.Count == 0) return;
            PreviewSection s = Section(m, UiKeys.SectionRemoved, f);
            s.Lines.AddRange(lines);
        }

        // ---- warnings and consequences ----

        private static void AddWarnings(DealPreviewModel m, AcquisitionPlan plan, ViewFormat f)
        {
            if (plan.Warnings.Count == 0) return;
            PreviewSection s = Section(m, UiKeys.SectionWarnings, f);
            for (int i = 0; i < plan.Warnings.Count; i++)
            {
                s.Lines.Add(new PreviewLine { Text = f.T(plan.Warnings[i].Key, plan.Warnings[i].Args), Tone = ViewTone.Warning });
            }
        }

        private static void AddConsequences(DealPreviewModel m, AcquisitionPlan plan, ViewFormat f)
        {
            if (plan.Consequences.Count == 0) return;
            PreviewSection s = Section(m, UiKeys.SectionConsequences, f);
            for (int i = 0; i < plan.Consequences.Count; i++)
            {
                PlanNote note = plan.Consequences[i];
                s.Lines.Add(new PreviewLine { Text = f.T(note.Key, note.Args), Tone = note.RequiresAck ? ViewTone.Warning : ViewTone.Neutral });
            }
        }

        // ---- helpers ----

        private static PreviewSection Section(DealPreviewModel m, string titleKey, ViewFormat f)
        {
            var s = new PreviewSection { TitleKey = titleKey, Title = f.T(titleKey) };
            m.Sections.Add(s);
            return s;
        }

        private static PreviewLine Pair(ViewFormat f, string titleKey, string value, ViewTone tone = ViewTone.Neutral)
        {
            return new PreviewLine { Title = f.T(titleKey), Text = value, Tone = tone };
        }

        private static PreviewLine WithNames(PreviewLine line, string header, List<string> names, ViewFormat f)
        {
            if (names != null && names.Count > 0)
            {
                line.TipHeader = header;
                line.TipBody = NameList(names, f);
            }
            return line;
        }

        /// <summary>The names joined with commas; past <see cref="TipNames"/> the rest are counted.</summary>
        public static string NameList(IList<string> names, ViewFormat f)
        {
            if (names == null || names.Count == 0) return "";
            var text = new StringBuilder();
            int shown = Math.Min(names.Count, TipNames);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(names[i]);
            }
            if (names.Count > shown) text.Append(", ").Append(f.T(UiKeys.TipMore, f.I(names.Count - shown)));
            return text.ToString();
        }

        private static ContractSnapshot ContractOf(List<ContractSnapshot> list, int id)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].Id == id) return list[i];
            }
            return null;
        }

        private static string CpuName(CompanySnapshot t, int saveId)
        {
            if (t.Cpus != null)
            {
                for (int i = 0; i < t.Cpus.Count; i++)
                {
                    if (t.Cpus[i] != null && t.Cpus[i].SaveId == saveId) return t.Cpus[i].Name ?? "";
                }
            }
            return saveId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
