using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    // Read-only pictures of the state for the windows of the feature (the tabs "Линейка" and "Рынок", TSK-025/026): built on request from the section, never saved.

    /// <summary>An open segment of the GPU market today (FR-8 item 1).</summary>
    public sealed class GpuSegmentView
    {
        public string Id, NameKey;

        /// <summary>The buyers of a day (F19) and of a month (x 30).</summary>
        public float DailyBuyers, MonthlyBuyers;

        /// <summary>The budget of a buyer (F19).</summary>
        public float Budget;

        /// <summary>The video memory the segment asks for today, MB (F33); 0 - none.</summary>
        public float VramRequirementMB;

        /// <summary>The features of the architecture the segment asks for this year (F34).</summary>
        public string[] RequiredFeatures = new string[0];
    }

    /// <summary>A card on the market as the tab "Рынок" lists it (the top 5 of a segment).</summary>
    public sealed class GpuMarketEntry
    {
        public string ProductId, Name, OwnerKey, VendorId, Node;
        public float Price, ClockMHz, Popularity;
        public double Perf;

        /// <summary>The cards sold over the last 30 days of this session (the window is not saved).</summary>
        public int Sold30;
    }

    /// <summary>The share of an owner (<c>player</c>, <c>vendor:&lt;id&gt;</c>, <c>background</c>) in the cards sold in a segment over the last 30 days.</summary>
    public sealed class GpuOwnerShare
    {
        public string OwnerKey = string.Empty;
        public int Sold;
        public float Share;
    }

    /// <summary>
    /// A card as the lineup shows it (FR-7 item 1) and as <c>GpuApi.ProductsOf</c> gives it to the other features (FR-18 item 3). The lineup fills the fields of the player's
    /// card; the API also fills <see cref="OwnerKey"/>, <see cref="EffectivePerf"/>, <see cref="PowerW"/> and <see cref="SoldLastMonth"/>.
    /// </summary>
    public sealed class GpuProductView
    {
        public string Id, Name, Status, Node;

        /// <summary>The key of the company the card belongs to (<c>player</c>, <c>ai:&lt;id&gt;</c>); empty for the card of a virtual vendor and of the background (the API only).</summary>
        public string OwnerKey = string.Empty;

        /// <summary>The performance of the card after the memory bottleneck, the figure of the market (the API only).</summary>
        public double EffectivePerf;

        /// <summary>The power the card draws, watts (the API only).</summary>
        public float PowerW;

        /// <summary>The cards sold over the last 30 days of this session (the API only).</summary>
        public int SoldLastMonth;

        public float Price, UnitCost;
        public bool Sell, Auto, IsOld, Fabless;

        /// <summary>The features of the architecture of the card (the marks "Нет: ..." in the lineup compare them with what the open segments ask for).</summary>
        public string[] Features = new string[0];
        public int Lines, UnitsPerDay, Stock;
        public DateTime? Released;

        /// <summary>The card got fewer lines than it asked for today ("Не хватает линий: <see cref="GrantedLines"/> из <see cref="RequestedLines"/>").</summary>
        public bool Short;

        public int GrantedLines, RequestedLines;

        /// <summary>The books of the previous month: sold, produced, failed, income, cost and the profit.</summary>
        public int LastMonthSold, LastMonthProduced, LastMonthFailed;

        public float LastMonthIncome, LastMonthCost, LastMonthProfit;

        /// <summary>The popularity by segment (the ids and the values side by side).</summary>
        public List<string> SegmentIds = new List<string>();

        public List<float> Popularity = new List<float>();
    }
}
