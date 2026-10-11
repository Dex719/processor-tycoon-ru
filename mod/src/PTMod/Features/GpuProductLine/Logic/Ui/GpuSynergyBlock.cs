using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>The two channels the synergy writes into (FR-15 item 2): the rating of the company's CPUs and the rating of its cards.</summary>
    public enum GpuSynergyChannel
    {
        /// <summary><c>market.rating.cpu</c>.</summary>
        Cpu = 0,

        /// <summary><c>gpu.rating</c>.</summary>
        Gpu = 1,
    }

    /// <summary>One multiplier of a channel as <c>ModModifiers.Explain</c> reports it (a copy of the core's <c>AppliedModifier</c>, free of the core's types).</summary>
    public sealed class GpuExplainRow
    {
        /// <summary>The feature that registered the multiplier.</summary>
        public string FeatureId = string.Empty;

        /// <summary>The key of the reason text, or empty (the source is then all there is to say).</summary>
        public string ReasonKey = string.Empty;

        public string[] ReasonArgs = new string[0];
        public float Multiplier = 1f;
    }

    /// <summary>The breakdown of a channel for the player's company (a copy of the core's <c>ModifierBreakdown</c>): the multipliers, the value the game uses, whether the balance range cut it.</summary>
    public sealed class GpuExplainView
    {
        public List<GpuExplainRow> Rows = new List<GpuExplainRow>();

        /// <summary>The value after the limits - what <c>ModModifiers.Get</c> answers.</summary>
        public float Total = 1f;

        /// <summary>The product of the multipliers was cut by the balance range of the channel (the core shows "(capped)" then, AC-15.3).</summary>
        public bool Capped;
    }

    /// <summary>
    /// The block "Синергия процессоров и видеокарт" of the tab "Рынок" (FR-15 item 5, AC-15.1): the state of the player's synergy in words - "Действует: +8 % к рейтингу процессоров,
    /// +5 % к рейтингу видеокарт", or why there is none (the switch is off, there is no CPU or no card on sale, the shares are not counted yet) -, the two bonuses with the formula and
    /// the shares they come from, and for each channel the breakdown of the multipliers (<c>ModModifiers.Explain</c>) for a tooltip: the synergy stays x1.08 in it even when the
    /// product of everyone's multipliers is cut by the range of the channel (AC-15.3). It is built from the services on request, like the rest of the tab.
    /// </summary>
    public sealed class GpuSynergyBlock
    {
        public bool Enabled, Eligible, Active;
        public float CpuBonus, GpuBonus, CpuShare, GpuShare;

        public string Title = string.Empty;

        /// <summary>"Действует: +8% к рейтингу процессоров, +5% к рейтингу видеокарт", or the reason there is nothing.</summary>
        public string StatusText = string.Empty;

        /// <summary>"Рейтинг процессоров: +8% (доля рынка видеокарт 40% × 0.2, не больше 8%)".</summary>
        public string CpuLine = string.Empty;

        /// <summary>"Рейтинг видеокарт: +5% (доля рынка процессоров 20% × 0.25, не больше 10%)".</summary>
        public string GpuLine = string.Empty;

        /// <summary>How the bonus is counted, for the tooltip of the title.</summary>
        public string FormulaText = string.Empty;

        /// <summary>The multipliers of <c>market.rating.cpu</c> and of <c>gpu.rating</c> of the player's company, one per line; for the tooltips of the two lines.</summary>
        public string CpuExplain = string.Empty, GpuExplain = string.Empty;

        /// <summary>
        /// Builds the block for the player. <paramref name="explain"/> gives the breakdown of a channel (null: no breakdown); it may throw or return null, the block then says there
        /// is none. Never null.
        /// </summary>
        public static GpuSynergyBlock Build(GpuContentDb content, GpuSynergy synergy, IGpuGame game, IGpuText text, Func<GpuSynergyChannel, GpuExplainView> explain)
        {
            var block = new GpuSynergyBlock { Title = text.L("gpu.market.synergy.title") };
            GpuBalanceDto.SynergyBlock rules = (content.Balance ?? new GpuBalanceDto()).synergy ?? new GpuBalanceDto.SynergyBlock();
            string player = game.PlayerKey;

            GpuSynergyView view = synergy.ViewFor(player);
            block.Enabled = synergy.IsEnabled;
            block.Eligible = view.Eligible;
            block.Active = view.Active;
            block.CpuBonus = view.CpuBonus;
            block.GpuBonus = view.GpuBonus;
            block.CpuShare = view.CpuShare30;
            block.GpuShare = view.GpuShare30;

            block.StatusText = Status(block, synergy, game, text, player);
            block.CpuLine = text.L("gpu.market.synergy.cpu", Plus(text, block.CpuBonus), text.Percent(block.GpuShare), GpuUiMath.Plain(rules.cpuK), text.Percent(rules.cpuMax));
            block.GpuLine = text.L("gpu.market.synergy.gpu", Plus(text, block.GpuBonus), text.Percent(block.CpuShare), GpuUiMath.Plain(rules.gpuK), text.Percent(rules.gpuMax));
            block.FormulaText = text.L("gpu.market.synergy.formula", text.Percent(rules.cpuMax), text.Percent(rules.gpuMax));
            block.CpuExplain = ExplainText(text, explain, GpuSynergyChannel.Cpu);
            block.GpuExplain = ExplainText(text, explain, GpuSynergyChannel.Gpu);
            return block;
        }

        private static string Status(GpuSynergyBlock block, GpuSynergy synergy, IGpuGame game, IGpuText text, string player)
        {
            if (!block.Enabled) return text.L("gpu.market.synergy.status.off");
            if (!game.HasCpuOnMarket(player)) return text.L("gpu.market.synergy.status.needCpu");
            if (!synergy.HasCardOnSale(player)) return text.L("gpu.market.synergy.status.needCard");
            if (!block.Active) return text.L("gpu.market.synergy.status.zero");
            return text.L("gpu.market.synergy.status.active", Plus(text, block.CpuBonus), Plus(text, block.GpuBonus));
        }

        private static string Plus(IGpuText text, float bonus)
        {
            return "+" + text.Percent(bonus);
        }

        // The multipliers of a channel, the biggest picture first: the total, then one line for each (x1.08 - the reason, or the feature).
        private static string ExplainText(IGpuText text, Func<GpuSynergyChannel, GpuExplainView> explain, GpuSynergyChannel channel)
        {
            GpuExplainView view = null;
            if (explain != null)
            {
                try
                {
                    view = explain(channel);
                }
                catch (Exception)
                {
                    view = null;   // a provider of another feature failed: the block says there is no breakdown, the core has counted the failure
                }
            }
            if (view == null) return text.L("gpu.market.synergy.explain.none");

            var lines = new List<string>
            {
                text.L("gpu.market.synergy.explain.head", Factor(view.Total), view.Capped ? text.L("gpu.market.synergy.explain.capped") : string.Empty),
            };
            if (view.Rows.Count == 0)
            {
                lines.Add(text.L("gpu.market.synergy.explain.empty"));
            }
            else
            {
                foreach (GpuExplainRow row in view.Rows)
                {
                    string reason = !string.IsNullOrEmpty(row.ReasonKey) && text.Has(row.ReasonKey) ? text.L(row.ReasonKey, (object[])(row.ReasonArgs ?? new string[0])) : row.FeatureId;
                    lines.Add(text.L("gpu.market.synergy.explain.row", Factor(row.Multiplier), reason));
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string Factor(float multiplier)
        {
            return GpuUiMath.Plain((float)Math.Round(multiplier, 2));
        }
    }
}
