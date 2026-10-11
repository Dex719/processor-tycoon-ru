using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>One side of a negotiation as the scope sees it: the player, an AI company (its <c>UniqueID</c>), or neither.</summary>
    public struct NegotiationParty
    {
        public NegotiationParty(bool isPlayer, int aiUniqueId)
        {
            IsPlayer = isPlayer;
            AiUniqueId = aiUniqueId;
        }

        public bool IsPlayer;

        /// <summary>The <c>UniqueID</c> of the AI company, or -1 when the side is not an AI company.</summary>
        public int AiUniqueId;

        /// <summary>The side is the player's company.</summary>
        public static NegotiationParty Player
        {
            get { return new NegotiationParty(true, -1); }
        }

        /// <summary>The side is an AI company.</summary>
        public static NegotiationParty Ai(int uniqueId)
        {
            return new NegotiationParty(false, uniqueId);
        }

        /// <summary>The side is neither the player nor an AI company (a null company).</summary>
        public static NegotiationParty Unknown
        {
            get { return new NegotiationParty(false, -1); }
        }
    }

    /// <summary>
    /// Where the scores of a negotiation with the player are counted (FR-8 item 3, design.md Component 10): between the prefix and the finalizer of
    /// <c>ContractNegotiationWindow.UpdateScores</c> the scope is open for the AI side of that window; everywhere else (the AI-AI deals of
    /// <c>AIBehaviourBusiness</c>) it is closed and the scores of the game stay as they are. Pure; the game side (<c>NegotiationScope</c>) fills it from
    /// the companies and a stamp that tells one frame from another.
    /// <list type="bullet">
    /// <item><b>Open.</b> <see cref="Enter"/> opens the scope when exactly one side is the player and the other is an AI company; two AI companies, two
    /// players or an unknown company leave it closed.</item>
    /// <item><b>Close.</b> <see cref="Exit"/> closes it. The finalizer calls it in every case, so an exception inside the window does not leave the scope open.</item>
    /// <item><b>Stale.</b> A scope the finalizer never closed (a patch of another mod, a failure of the host) is dropped as soon as it is asked about in
    /// a later frame: <c>UpdateScores</c> is one synchronous call, so a scope older than the frame it was opened in is wrong.</item>
    /// </list>
    /// </summary>
    public sealed class NegotiationScopeCore
    {
        private long _stamp;

        /// <summary>The scope is open: the scores of the AI side are counted for a negotiation with the player.</summary>
        public bool Active { get; private set; }

        /// <summary>The <c>UniqueID</c> of the AI company of the open scope; -1 when the scope is closed.</summary>
        public int AiUniqueId { get; private set; } = -1;

        /// <summary>The AI side is the provider (the foundry) of the open scope; false when it is the client or the scope is closed.</summary>
        public bool AiIsProvider { get; private set; }

        /// <summary>How many times a scope was opened (for the diagnostics and the tests).</summary>
        public int Entered { get; private set; }

        /// <summary>How many times a scope was closed by <see cref="Exit"/>.</summary>
        public int Exited { get; private set; }

        /// <summary>How many scopes were found open in a later frame and dropped.</summary>
        public int StaleDropped { get; private set; }

        /// <summary>
        /// Opens the scope for a window that counts the scores of <paramref name="provider"/> and <paramref name="client"/>. True when it is open
        /// now (one side is the player and the other an AI company). A scope that was open is replaced.
        /// </summary>
        /// <param name="stamp">A number that stays the same inside one <c>UpdateScores</c> and changes with the frame (<c>Time.frameCount</c>).</param>
        public bool Enter(NegotiationParty provider, NegotiationParty client, long stamp)
        {
            Close();

            bool providerIsAi = !provider.IsPlayer && provider.AiUniqueId >= 0;
            bool clientIsAi = !client.IsPlayer && client.AiUniqueId >= 0;
            if (provider.IsPlayer == client.IsPlayer) return false;   // two players, or no player at all
            if (!(providerIsAi || clientIsAi)) return false;

            Active = true;
            AiIsProvider = providerIsAi;
            AiUniqueId = providerIsAi ? provider.AiUniqueId : client.AiUniqueId;
            _stamp = stamp;
            Entered++;
            return true;
        }

        /// <summary>Closes the scope. Does nothing when it is closed.</summary>
        public void Exit()
        {
            if (!Active) return;
            Close();
            Exited++;
        }

        /// <summary>
        /// The company with this <c>UniqueID</c> is the AI side of the open scope in the given role. A scope opened in an earlier frame is dropped
        /// (and counted in <see cref="StaleDropped"/>) and the answer is false.
        /// </summary>
        public bool IsAiSide(bool asProvider, int uniqueId, long stamp)
        {
            if (!Active) return false;
            if (stamp != _stamp)
            {
                Close();
                StaleDropped++;
                return false;
            }
            return AiIsProvider == asProvider && AiUniqueId == uniqueId && uniqueId >= 0;
        }

        /// <summary>
        /// The postfix of a score function in one call: when the company is the AI side of the open scope in this role, asks <paramref name="bonusOf"/>
        /// for what its attitude gives and adds it to the scores (<see cref="NegotiationRules.Apply"/>: a veto stays). True when the scores were
        /// treated; false (and nothing changed) when the scope is closed, stale, or the company is another one.
        /// </summary>
        /// <param name="bonusOf">Gets the <c>UniqueID</c> and the role (true: provider) and gives the bonus; null means no bonus.</param>
        public bool Apply(bool asProvider, int uniqueId, long stamp, Func<int, bool, NegotiationBonus> bonusOf, ref int interest, ref int price)
        {
            if (!IsAiSide(asProvider, uniqueId, stamp)) return false;

            NegotiationBonus bonus = bonusOf != null ? bonusOf(uniqueId, asProvider) : default(NegotiationBonus);
            NegotiationRules.Apply(bonus, ref interest, ref price);
            return true;
        }

        private void Close()
        {
            Active = false;
            AiIsProvider = false;
            AiUniqueId = -1;
        }
    }
}
