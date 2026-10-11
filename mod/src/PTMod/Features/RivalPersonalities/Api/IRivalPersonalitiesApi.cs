using System;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The public API of <c>rival-personalities</c> for other features (FR-13): the attitude and the character of the rivals, the dormant
    /// mode that <c>rival-acquisitions</c> uses, and the price wars that <c>historical-events</c> and others start. Get it from
    /// <see cref="RivalPersonalities.Api"/> (or <see cref="RivalPersonalitiesApi.Instance"/>); the object never changes and never is null.
    /// <para>
    /// While the feature is off (hub toggle, Faulted, Unavailable, not started yet) the object is a null-object (FR-13 item 7):
    /// <see cref="IsEnabled"/> is false, every read gives null, <see cref="ApplyPriceCut"/> gives 0 and the other calls do nothing. A feature that
    /// may touch AI prices without this API (the fallback of <c>historical-events</c>) does it only while <see cref="IsEnabled"/> is false.
    /// </para>
    /// <para>
    /// Rules for every member: main thread only; a company is the game's <c>ICompany</c>, and the player's company or an object that is no AI
    /// company counts as unknown; no member throws (a failure inside the feature is counted against <c>rival-personalities</c>, the caller gets
    /// the neutral answer).
    /// </para>
    /// </summary>
    public interface IRivalPersonalitiesApi
    {
        /// <summary>
        /// True while the feature runs (FR-13 item 7). False means "this is the null-object": the AI keeps its own behaviour and prices, and a
        /// caller that needs an effect on them uses its own fallback.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// The attitude of the rival to the player, from -100 (hostile) to +100 (partner) (FR-13 item 1). Null for the player, for a null or
        /// unknown company and while the feature is off.
        /// </summary>
        float? GetAttitude(ICompany company);

        /// <summary>
        /// The level of the attitude of the rival (<see cref="AttitudeLevel"/>). Null in the same cases as <see cref="GetAttitude"/>.
        /// </summary>
        AttitudeLevel? GetAttitudeLevel(ICompany company);

        /// <summary>
        /// The id of the archetype of the rival (the id in <c>archetypes.json</c>, for example <c>conservative</c>) (FR-13 item 1). Null for the
        /// player, for a null or unknown company and while the feature is off.
        /// </summary>
        string GetArchetypeId(ICompany company);

        /// <summary>
        /// Changes the attitude of the rival by <paramref name="delta"/> by the rules of FR-4 (the sensitivity of the archetype, the clamp to
        /// -100..+100, the entry in the history of 20 with <paramref name="reason"/>, the notification when the level changes) (FR-13 item 2).
        /// Ignored for the player, for a null or unknown company and while the feature is off.
        /// </summary>
        /// <param name="reason">A short English text for the history and the log; the player does not see it as is.</param>
        void ChangeAttitude(ICompany company, float delta, string reason);

        /// <summary>
        /// <c>rival-acquisitions</c> puts a rival that the player took over (or that left the game) aside, or brings it back (FR-13 item 3).
        /// With <paramref name="dormant"/> true the feature takes its changes of the behaviour off the company, cancels its planned reactions,
        /// gives the prices under price cuts back and leaves the company out of the triggers until the call with false. Ignored for the player,
        /// for a null or unknown company and while the feature is off.
        /// </summary>
        void SetDormant(ICompany company, bool dormant);

        /// <summary>
        /// A reaction of a rival to the player started (the mail that announced it came, the effect began): <c>(uniqueId, reaction type)</c>,
        /// where the type is one of <c>PriceCut</c>, <c>CounterLaunch</c>, <c>OemPush</c>, <c>Poach</c>, <c>Smear</c>, <c>Partnership</c>
        /// and <c>uniqueId</c> is the <c>UniqueID</c> of the rival (the part of its <c>ai:&lt;UniqueID&gt;</c> key). The subscription made on the
        /// object of <see cref="RivalPersonalities.Api"/> stays valid when the feature is switched off and on. Handlers of a feature that is not
        /// active are skipped, an exception in a handler is counted against the feature of the handler and the next handler still runs.
        /// </summary>
        event Action<int, string> ReactionStarted;

        /// <summary>
        /// Price war: cuts the prices of the CPUs that the rival sells in <paramref name="segment"/> by <paramref name="depth"/> and holds
        /// their target price down, through the same price book as the <c>PriceCut</c> reaction (FR-13 item 6). Returns the number of CPUs
        /// affected; 0 when the feature is off, when the rival is the player, unknown, bankrupt or dormant, or when it has no suitable CPU.
        /// <para>
        /// <paramref name="reactionId"/> is the caller's own id, <c>&lt;prefix&gt;:&lt;name&gt;</c> (for example <c>hist:price-war-1985</c>; the
        /// ids of the feature itself look like <c>r-&lt;n&gt;</c>): a non-empty string of at most 64 characters, the same id is given to
        /// <see cref="RestorePriceCut"/>. <paramref name="depth"/> is the share of the price taken off (0.10 is 10 %), clipped to 0..0.5;
        /// <paramref name="floorMargin"/> keeps the new price at least at <c>RealUnitCost * (1 + floorMargin)</c>, clipped to 0..0.5. At most
        /// 20 external cuts are active at once. A cut that the caller did not take back is taken off by the feature after
        /// <c>priceCut.externalMaxDays</c> (400) days.
        /// </para>
        /// <para>
        /// <paramref name="segment"/> is one of <see cref="Segment.HighEnd"/>, <see cref="Segment.MidRange"/>, <see cref="Segment.LowEnd"/>
        /// and <see cref="Segment.Industries"/> (the index is the one of the game's market segments).
        /// </para>
        /// </summary>
        int ApplyPriceCut(string reactionId, ICompany rival, Segment segment, float depth, float floorMargin);

        /// <summary>
        /// Ends the price war <paramref name="reactionId"/>: the captured base target prices are given back (FR-13 item 6), the prices
        /// themselves then move by the usual logic of the AI. An id the feature does not know (never applied, already restored, expired) is
        /// ignored. While the feature is off the call does nothing: the cuts were already taken back when the feature was switched off.
        /// </summary>
        void RestorePriceCut(string reactionId);
    }
}
