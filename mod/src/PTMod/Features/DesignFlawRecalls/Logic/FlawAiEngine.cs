using System;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the AI branch needs from the game (design.md Components 12 and 13): the clock, the numbers, the random stream, the money of the company, the bias of
    /// <c>rival-personalities</c> and the way to tell the player. <c>FlawDecisionDispatcher</c> is the game's implementation; a test brings a fake.
    /// </summary>
    public interface IFlawAiHost
    {
        /// <summary>The numbers of the feature (<c>aiPolicy</c> among them).</summary>
        FlawBalance Balance { get; }

        /// <summary>The random stream of the choices of AI companies, <c>ModRandom.Stream("design-flaw-recalls/ai")</c>.</summary>
        IModRng Rng { get; }

        /// <summary>The new game is being fast-forwarded: nothing is told to the player (FR-11, design.md "Error Handling").</summary>
        bool DuringFastForward { get; }

        /// <summary>
        /// The cash headroom of the owner of the record: <c>MoneyAmount - ModCredit.ThresholdOf(owner)</c> (<see cref="AiFlawPolicy.CashHeadroom"/>). NaN when the
        /// owner is not in the world.
        /// </summary>
        float CashHeadroom(FlawRecord record);

        /// <summary>
        /// The bias of the reaction of the owner (<c>FlawRegistry.GetResponseBiasMultipliers</c>, called under <c>ModHooks.Guard</c>): [Recall, Stepping, Patch, Ignore],
        /// or <c>null</c> for "all 1.0" (no bias, a bias that is off, a bias that failed).
        /// </summary>
        float[] ResponseBias(FlawRecord record, bool isPublic);

        /// <summary>Tells the player about the incident as <paramref name="plan"/> says (a letter or a notification, and a line in the event journal).</summary>
        void PublishNews(FlawRecord record, FlawResponse response, FlawNewsPlan plan);

        void Log(string line);

        void Warn(string line);

        void Error(string line, Exception exception);
    }

    /// <summary>
    /// The decision of an AI company about its flaw (FR-11, D8): at once, without a window, by <see cref="AiFlawPolicy"/>. The answer is applied with the same rules as the
    /// player's (<see cref="FlawResponseEngine"/>: the money through <c>SpendMoney</c> of the company, the reputation, the popularity, the channels, the contracts), marked as an
    /// automatic choice. The player hears about a public incident (<see cref="FlawNewsPlanner"/>); a quiet answer to an internal flaw is not told to anybody until a leak.
    /// <para>
    /// A flaw that leaked (<c>CoverUp</c> after <see cref="DiscoverySource.Leak"/>) is decided again, now as a public one and without the way of ignoring. Pure: the game is
    /// behind <see cref="IFlawAiHost"/>.
    /// </para>
    /// </summary>
    public sealed class FlawAiEngine
    {
        private readonly FlawResponseEngine _responses;
        private readonly IFlawAiHost _host;

        public FlawAiEngine(FlawResponseEngine responses, IFlawAiHost host)
        {
            if (responses == null) throw new ArgumentNullException("responses");
            if (host == null) throw new ArgumentNullException("host");
            _responses = responses;
            _host = host;
        }

        /// <summary>
        /// The company chooses its answer to the flaw and it is applied. <c>true</c> when the answer was applied now. <c>false</c> (nothing changed) for a record of the player, a
        /// record that does not wait for an answer, a record without a quote, and when the answer could not be applied; the record then waits and is offered again by the monthly
        /// pass (<see cref="FlawDecisionEngine.Sweep"/>). An exception of the policy does not leave the method: it is reported to the host and the answer is <c>false</c>.
        /// </summary>
        public bool Decide(FlawRecord record)
        {
            if (record == null) return false;
            if (CompanyKey.IsPlayer(record.CompanyKey))
            {
                _host.Warn(record + " is the player's: the policy of the AI does not answer for it");
                return false;
            }
            if (!FlawResponseEngine.AwaitsDecision(record)) return false;

            FlawQuote quote = record.Quote;
            if (quote == null)
            {
                _host.Warn("decision for " + record + " not made: the record has no quote");
                return false;
            }

            FlawState before = record.State;
            FlawResponse response;
            try
            {
                response = Choose(record, quote, before);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the policy of the AI failed", e);
                return false;
            }

            bool applied;
            try
            {
                applied = _responses.Apply(record, response, true);
            }
            catch (Exception e)
            {
                _host.Error(record + ": the answer " + response + " of the AI could not be applied", e);
                return false;
            }
            if (!applied) return false;

            _host.Log("ai decision " + record + ": " + response + (before == FlawState.Public ? " (public)" : " (internal)"));
            Publish(record, response, before);
            return true;
        }

        /// <summary>The answer of the policy for the record: the weights of the content, the cuts, the bias of the other feature and one draw.</summary>
        private FlawResponse Choose(FlawRecord record, FlawQuote quote, FlawState state)
        {
            bool isPublic = state == FlawState.Public;
            var input = new AiDecisionInput
            {
                IsPublic = isPublic,
                PatchAvailable = quote.PatchAvailable,
                IgnoreClosed = FlawDecisionPlanner.KindOf(record) == FlawDecisionKind.Leaked,
                CashHeadroom = _host.CashHeadroom(record),
                Quote = quote,
                BiasMultipliers = _host.ResponseBias(record, isPublic),
            };
            return AiFlawPolicy.Choose(input, _host.Balance.AiPolicy, _host.Rng);
        }

        private void Publish(FlawRecord record, FlawResponse response, FlawState before)
        {
            try
            {
                FlawNewsPlan plan = FlawNewsPlanner.Plan(record, response, before, _host.DuringFastForward);
                if (!plan.IsEmpty) _host.PublishNews(record, response, plan);
            }
            catch (Exception e)
            {
                // The answer stands; only the message to the player is lost.
                _host.Error(record + ": the news about the answer of the AI failed", e);
            }
        }
    }
}
