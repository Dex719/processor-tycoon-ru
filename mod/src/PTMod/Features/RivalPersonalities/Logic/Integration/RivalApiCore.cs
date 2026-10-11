using System;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What the public API changes in the game, by <c>UniqueID</c> (the game side implements it once, <c>RivalApiService</c>; the tests use a fake). Every
    /// method may be called for a rival that has nothing to give back.
    /// </summary>
    public interface IRivalApiEffects
    {
        /// <summary>Cuts the prices of the rival's CPUs in the segment and holds them (the price book); returns how many CPUs the cut holds.</summary>
        int ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin);

        /// <summary>Ends the cut and gives the captured target prices back; returns how many CPUs got theirs back.</summary>
        int RestoreCut(string reactionId);

        /// <summary>Ends every cut on the rival (dormant); returns how many CPUs got their target price back.</summary>
        int RestoreRivalCuts(int uniqueId);

        /// <summary>Drops the planned reactions of the rival and takes back the ones in effect; returns how many.</summary>
        int CancelReactions(int uniqueId, string reason);

        /// <summary>Takes the character off the rival: the priorities of its designers and the multipliers of the patches go back to the game's.</summary>
        void RevertCharacter(int uniqueId);

        /// <summary>Puts the character on the rival again (it was dormant).</summary>
        void ApplyCharacter(int uniqueId);
    }

    /// <summary>
    /// The decisions behind the public API of the feature (design.md, Component 12; FR-13, TSK-020), in terms of the <c>UniqueID</c> of an AI company.
    /// The facade <c>RivalApiService</c> turns the game's <c>ICompany</c> into a <c>UniqueID</c> (<c>null</c> for the player, a missing company and
    /// anything that is not an AI company) and calls this. Pure; the books and the effects come from the constructor. Nothing here throws.
    /// <list type="bullet">
    /// <item><b>Reads</b> give null for a company without a character (the player never has one).</item>
    /// <item><b>ChangeAttitude</b> is the ledger's change (sensitivity, clamp, history, notification).</item>
    /// <item><b>SetDormant(true)</b> writes the record, drops the planned and active reactions of the rival, gives the prices under cuts back and takes
    /// the character off; <c>SetDormant(false)</c> removes the record and puts the character on again. A repeated call changes nothing.</item>
    /// <item><b>ApplyPriceCut / RestorePriceCut</b> go to the price book of the feature. An id of the feature's own reactions (<c>r-&lt;n&gt;</c>) is
    /// refused: the callers' ids look like <c>&lt;prefix&gt;:&lt;name&gt;</c>.</item>
    /// </list>
    /// </summary>
    public sealed class RivalApiCore
    {
        private readonly PersonalityBook _book;
        private readonly AttitudeLedger _attitudes;
        private readonly IRivalApiEffects _effects;
        private readonly Action<string> _warn;

        public RivalApiCore(PersonalityBook book, AttitudeLedger attitudes, IRivalApiEffects effects, Action<string> warn = null)
        {
            _book = book;
            _attitudes = attitudes;
            _effects = effects;
            _warn = warn;
        }

        // ---- reads ----

        /// <summary>The attitude of the rival, -100..+100; null for no company and for a company without a character.</summary>
        public float? GetAttitude(int? uniqueId)
        {
            if (!IsKnown(uniqueId)) return null;
            return _attitudes.Get(uniqueId.Value);
        }

        /// <summary>The level of the attitude; null in the same cases as <see cref="GetAttitude"/>.</summary>
        public AttitudeLevel? GetAttitudeLevel(int? uniqueId)
        {
            if (!IsKnown(uniqueId)) return null;
            return _attitudes.GetLevel(uniqueId.Value);
        }

        /// <summary>The id of the archetype of the rival (<c>conservative</c>...); null in the same cases as <see cref="GetAttitude"/>.</summary>
        public string GetArchetypeId(int? uniqueId)
        {
            if (!IsKnown(uniqueId)) return null;
            return _book.ArchetypeIdOf(uniqueId.Value);
        }

        // ---- the attitude ----

        /// <summary>
        /// Changes the attitude by <paramref name="delta"/> × the sensitivity of the archetype (FR-4). True when the ledger took the change; false for no
        /// company, a company without a character, a bankrupt one and a delta that is not a number. An empty reason is written as <c>api</c>.
        /// </summary>
        public bool ChangeAttitude(int? uniqueId, float delta, string reason)
        {
            if (!IsKnown(uniqueId)) return false;
            return _attitudes.Change(uniqueId.Value, delta, string.IsNullOrWhiteSpace(reason) ? "api" : reason);
        }

        // ---- dormant ----

        /// <summary>
        /// Puts the rival aside (<c>true</c>) or brings it back (<c>false</c>), FR-13 item 3. True when the state changed; false for no company, a negative
        /// id and a rival that was in that state already. A step of the effects that fails is logged and the others still run.
        /// </summary>
        public bool SetDormant(int? uniqueId, bool dormant)
        {
            if (!uniqueId.HasValue || uniqueId.Value < 0) return false;
            int uid = uniqueId.Value;
            if (!_book.SetDormant(uid, dormant)) return false;

            if (dormant)
            {
                Step("cancel the reactions of " + uid, () => _effects.CancelReactions(uid, "the rival is dormant"));
                Step("give the prices back for " + uid, () => _effects.RestoreRivalCuts(uid));
                Step("take the character off " + uid, () => _effects.RevertCharacter(uid));
            }
            else
            {
                Step("put the character on " + uid, () => _effects.ApplyCharacter(uid));
            }
            return true;
        }

        // ---- price wars ----

        /// <summary>
        /// A price war of another feature (FR-13 item 6): the same price book as the <c>PriceCut</c> reaction. Returns the number of CPUs the cut holds;
        /// 0 for no company, an id the feature does not accept, a segment that is not one of the four and everything the price book refuses (a bankrupt or
        /// dormant rival, no suitable CPU, too many external cuts).
        /// </summary>
        public int ApplyPriceCut(string reactionId, int? uniqueId, Segment segment, float depth, float floorMargin)
        {
            if (!uniqueId.HasValue || uniqueId.Value < 0) return 0;
            if (!IsExternalId(reactionId, "ApplyPriceCut")) return 0;
            if (segment < Segment.HighEnd || segment > Segment.Industries) return 0;

            try
            {
                return _effects.ApplyCut(reactionId, uniqueId.Value, segment, depth, floorMargin);
            }
            catch (Exception e)
            {
                Say("ApplyPriceCut('" + reactionId + "') failed (" + e.GetType().Name + ": " + e.Message + ")");
                return 0;
            }
        }

        /// <summary>Ends the price war <paramref name="reactionId"/>; an id the feature does not know (or an id of its own reactions) is ignored.</summary>
        public void RestorePriceCut(string reactionId)
        {
            if (!IsExternalId(reactionId, "RestorePriceCut")) return;
            Step("restore the price cut '" + reactionId + "'", () => _effects.RestoreCut(reactionId));
        }

        // ---- internals ----

        private bool IsKnown(int? uniqueId)
        {
            return uniqueId.HasValue && uniqueId.Value >= 0 && _attitudes.IsKnown(uniqueId.Value);
        }

        /// <summary>The id is usable by a caller from outside: not empty, at most 64 characters, not one of the feature's own <c>r-&lt;n&gt;</c>.</summary>
        private bool IsExternalId(string reactionId, string member)
        {
            if (!PriceCutMath.IsValidReactionId(reactionId))
            {
                Say(member + ": the id of a price cut must be 1.." + PriceCutMath.MaxReactionIdLength + " characters; the call was ignored");
                return false;
            }
            if (PriceCutMath.IsInternalId(reactionId))
            {
                Say(member + ": '" + reactionId + "' is an id of the feature's own reactions; use <prefix>:<name>; the call was ignored");
                return false;
            }
            return true;
        }

        private void Step(string what, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                Say("could not " + what + " (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private void Say(string line)
        {
            if (_warn != null) _warn("api: " + line);
        }
    }
}
