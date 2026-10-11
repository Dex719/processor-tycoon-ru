using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A line "label: value" of the release dialog.</summary>
    public sealed class GpuFact
    {
        public string Label = string.Empty;
        public string Value = string.Empty;
        public string Tip = string.Empty;

        public GpuFact(string label, string value, string tip = "")
        {
            Label = label;
            Value = value;
            Tip = tip ?? string.Empty;
        }
    }

    /// <summary>
    /// The dialog "Выпуск GPU" as data (design.md, "Диалог «Выпуск GPU»"; FR-4 items 6-8, AC-4.3): the name and the characteristics of the finished card, the price the
    /// dialog proposes (<c>0.9 x</c> the budget of the first open segment) and lets the player change, "sell on the market", the lines of the own factory (or the cards a
    /// day when the company is fabless). <see cref="Release"/> puts the card on the market; closing the dialog without it leaves the card ready for the button "Выпустить"
    /// of the lineup.
    /// </summary>
    public sealed class GpuReleaseModel
    {
        private readonly GpuLineup _lineup;
        private readonly IGpuText _text;
        private float _budget, _unitCost;

        public string ProductId = string.Empty;
        public string Name = string.Empty;
        public string Title = string.Empty;
        public List<GpuFact> Facts = new List<GpuFact>();

        /// <summary>The proposed price (0.9 x the budget).</summary>
        public float SuggestedPrice;

        public float Price;
        public bool Sell = true;
        public int Lines;
        public bool Fabless;

        /// <summary>The slider of the lines (or of the cards a day).</summary>
        public GpuSliderRange LinesRange;

        /// <summary>The tooltips and the labels of the controls, written for the player.</summary>
        public string LinesLabel = string.Empty;

        private GpuReleaseModel(GpuLineup lineup, IGpuText text)
        {
            _lineup = lineup;
            _text = text;
        }

        /// <summary>The budget of the first open segment (the price is compared with it), 0 with no segment.</summary>
        public float Budget
        {
            get { return _budget; }
        }

        /// <summary>The cost of one card today (F12), 0 when the design cannot be evaluated.</summary>
        public float UnitCost
        {
            get { return _unitCost; }
        }

        /// <summary>One step of the price buttons: 5 % of the budget, at least 1.</summary>
        public float PriceStep
        {
            get { return Math.Max(1f, (float)Math.Round(_budget * 0.05f)); }
        }

        /// <summary>Builds the dialog for a card that is ready; null when there is no such card of the player.</summary>
        public static GpuReleaseModel Build(string productId, GpuLineup lineup, GpuProjects projects, GpuDesigner designer, GpuMarketService market, GpuContentDb content, IGpuGame game,
            IGpuText text)
        {
            GpuProductSave product = lineup.Find(productId);
            if (product == null || product.status != GpuState.StatusReady) return null;

            var model = new GpuReleaseModel(lineup, text)
            {
                ProductId = product.id,
                Name = product.name ?? string.Empty,
                Fabless = game.IsFabless(game.PlayerKey),
            };
            model.Title = text.L("gpu.release.title", model.Name);

            DateTime today = game.Today;
            GpuSegmentDto fit = lineup.FitSegment(product.id);   // the segment the card is meant for: with several segments the price follows it
            model.SuggestedPrice = lineup.SuggestedPrice(product.id);
            model._budget = fit != null ? market.BudgetOf(fit, today.Year) : 0f;
            if (!(model.SuggestedPrice > 0f)) model.SuggestedPrice = 100f;
            model.Price = model.SuggestedPrice;

            GpuDesignSave design = projects.Design(product.designId);
            if (design != null)
            {
                var input = new GpuDesignInput
                {
                    ArchId = design.archId, MemId = design.memId, CoolerId = design.coolerId, NodeName = design.node, Name = design.name,
                    DieMm2 = design.die, ClockMHz = design.clockMHz, VramMB = design.vramMB, BusWidthBits = design.busWidth,
                };
                GpuStats now = designer.Evaluate(input);
                model._unitCost = now.UnitCost;
            }

            model.FillFacts(content, product, design);
            model.PrepareLines(game);
            return model;
        }

        private void FillFacts(GpuContentDb content, GpuProductSave product, GpuDesignSave design)
        {
            GpuStatsSave s = product.stats ?? new GpuStatsSave();
            if (design != null)
            {
                Facts.Add(new GpuFact(_text.L("gpu.release.fact.arch"), GpuNames.Part(content, _text, design.archId)));
                Facts.Add(new GpuFact(_text.L("gpu.release.fact.node"), _text.L("gpu.release.fact.nodeValue", design.node, GpuUiMath.Plain(design.die))));
                Facts.Add(new GpuFact(_text.L("gpu.release.fact.memory"),
                    _text.L("gpu.release.fact.memoryValue", GpuNames.Part(content, _text, design.memId), GpuUiMath.Plain(design.vramMB), design.busWidth)));
                Facts.Add(new GpuFact(_text.L("gpu.release.fact.cooler"), GpuNames.Part(content, _text, design.coolerId)));
                Facts.Add(new GpuFact(_text.L("gpu.release.fact.clock"), _text.Frequency(design.clockMHz)));
            }
            Facts.Add(new GpuFact(_text.L("gpu.design.stat.perf"), _text.L("gpu.design.val.mops", _text.Integer(s.effectivePerf))));
            Facts.Add(new GpuFact(_text.L("gpu.design.stat.power"), _text.L("gpu.design.val.watts", _text.Number(s.powerW, string.Empty))));
            Facts.Add(new GpuFact(_text.L("gpu.design.stat.failure"), _text.Percent(s.failureRate)));
            if (_unitCost > 0f) Facts.Add(new GpuFact(_text.L("gpu.design.stat.unitCost"), _text.Money(_unitCost)));
        }

        private void PrepareLines(IGpuGame game)
        {
            if (Fabless)
            {
                LinesLabel = _text.L("gpu.release.unitsPerDay");
                LinesRange = GpuSliderRange.Nice(0f, 20000f, 100);
                Lines = (int)LinesRange.Snap(1000f);
                return;
            }

            LinesLabel = _text.L("gpu.release.lines");
            IGpuFactory factory = game.FactoryOf(game.PlayerKey);
            int capacity = factory != null ? Math.Max(0, factory.Capacity) : 0;
            LinesRange = new GpuSliderRange { Min = 0f, Max = capacity, Step = 1f };
            Lines = capacity > 0 ? Math.Max(1, capacity / 5) : 0;
        }

        // ---------------------------------------------------------------- the price

        /// <summary>The price moves by one step up (+1) or down (-1); it never goes below the step.</summary>
        public void StepPrice(int direction)
        {
            float step = PriceStep;
            Price = Math.Max(step, Price + (direction >= 0 ? step : -step));
        }

        /// <summary>The price the player typed; false (the price stays) for text that is no number or a number below zero.</summary>
        public bool SetPriceText(string text)
        {
            float value;
            if (!GpuUiMath.TryParseNumber(text, out value) || value < 0f) return false;
            Price = value;
            return true;
        }

        /// <summary>The price as it goes into the input field.</summary>
        public string PriceText
        {
            get { return GpuUiMath.Plain(Price); }
        }

        /// <summary>A word about the price: it will not sell (from 3 budgets up, or zero), or it is below the cost. Empty when the price is fine.</summary>
        public string PriceNote
        {
            get
            {
                if (!(Price > 0f)) return _text.L("gpu.release.note.zero");
                if (_budget > 0f && Price >= _budget * GpuMarketMath.PriceCeilingBudgets) return _text.L("gpu.release.note.high", _text.Money(_budget * GpuMarketMath.PriceCeilingBudgets));
                if (_unitCost > 0f && Price < _unitCost) return _text.L("gpu.release.note.belowCost", _text.Money(_unitCost));
                return string.Empty;
            }
        }

        /// <summary>The line under the price: the cost of a card and what is left of the price.</summary>
        public string MarginText
        {
            get
            {
                if (!(_unitCost > 0f) || !(Price > 0f)) return string.Empty;
                float margin = (Price - _unitCost) / Price;
                return _text.L("gpu.release.margin", _text.Money(_unitCost), Math.Round(margin * 100f).ToString("0", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>The text of the budget line: "Бюджет сегмента: ... Предложено: ...".</summary>
        public string BudgetText
        {
            get { return _text.L("gpu.release.budget", _text.Money(_budget), _text.Money(SuggestedPrice)); }
        }

        // ---------------------------------------------------------------- the release

        /// <summary>
        /// FR-4 item 7: puts the card on the market at the price, with "sell on the market" and the lines (or the cards a day). False, with the reason in words, when
        /// the card is not ready any more.
        /// </summary>
        public bool Release(out string reasonText)
        {
            string reasonKey;
            bool released = _lineup.Release(ProductId, Price, Sell, Lines, out reasonKey);
            reasonText = released || string.IsNullOrEmpty(reasonKey) ? string.Empty : _text.L(reasonKey);
            return released;
        }
    }
}
