using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// One finished deal, as <c>RivalAcquisitionsApi.DealCompleted</c> hands it to other features and as the feature keeps it in its
    /// section of the side save (<c>deals</c>). A plain DTO of public fields: the core's <c>ModJson.From</c> / <c>ModJson.Bind</c> read and write
    /// it as is, so the field names are the JSON keys and must not change. Readers treat it as read-only.
    /// <para>
    /// Pure contract file: it must not reference PTMod.Core, UnityEngine or the game, so the tests build can compile it with the feature's Logic.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class DealRecord
    {
        /// <summary>Number of the deal in this save (<c>AcquisitionsState.nextDealId</c> at the time), from 1.</summary>
        public int id;

        /// <summary>The name of a <see cref="DealType"/>: <c>"BankruptAssets"</c>, <c>"Distressed"</c>, <c>"Hostile"</c>, <c>"Stake"</c> or <c>"RescueOrder"</c>.</summary>
        public string type = "";

        /// <summary><c>UniqueID</c> of the target AI company (stable across saves, the key other features use).</summary>
        public int targetUniqueId;

        /// <summary><c>SaveID</c> of the target AI company at the time of the deal.</summary>
        public int targetSaveId;

        /// <summary>The name of the target as the game spelled it at the time of the deal.</summary>
        public string targetName = "";

        /// <summary>The game date of the deal, <c>yyyy-MM-dd</c>.</summary>
        public string date = "";

        /// <summary>The price paid for the deal (the cash that left the player's account for the purchase itself), at least 0.</summary>
        public double price;

        /// <summary>The cash of the target that came with the deal (positive) or the debt that was taken over (negative): <c>DealQuote.AssumedCash</c>. The only amount that may be below 0.</summary>
        public double assumedCash;

        /// <summary>For a deal about a stake (<see cref="DealType.Stake"/>, <see cref="DealType.RescueOrder"/>): the share of the target involved, 0 to 0.49; 0 for a purchase of the whole company.</summary>
        public double share;

        /// <summary>The production lines were moved to the player's factory (true) and not sold (false).</summary>
        public bool keptLines;

        /// <summary>The production lines that were moved or sold, by wafer size.</summary>
        public List<WaferLines> lines = new List<WaferLines>();

        /// <summary>How many technologies the player got from the target.</summary>
        public int techCount;

        /// <summary><c>SaveID</c> of the CPUs that passed to the player (the base of a family first).</summary>
        public List<int> cpuSaveIds = new List<int>();

        /// <summary><c>SaveID</c> of the CPUs that passed to the player already retired.</summary>
        public List<int> retiredCpuSaveIds = new List<int>();

        /// <summary>How many contracts of the target as a supplier were taken over by the player.</summary>
        public int providerContracts;

        /// <summary>How many contracts of the target as a client of a third party were taken over by the player.</summary>
        public int clientContracts;

        /// <summary>Some step of the deal failed and was skipped (details in <see cref="notes"/> and the log); the deal itself stands.</summary>
        public bool partial;

        /// <summary>Free-form notes of the deal log (for example, a related feature that did not transfer its state). Never null.</summary>
        public string notes = "";
    }

    /// <summary>Production lines of one wafer size: part of <see cref="DealRecord.lines"/>.</summary>
    [Serializable]
    public sealed class WaferLines
    {
        /// <summary><c>WaferSize.ID</c> of the game.</summary>
        public int waferId;

        /// <summary>The number of lines of that wafer size.</summary>
        public int lines;
    }
}
