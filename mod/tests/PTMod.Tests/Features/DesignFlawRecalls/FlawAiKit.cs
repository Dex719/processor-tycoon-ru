using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// A scripted <see cref="IFlawAiHost"/>: the money and the default threshold of the company, the bias of the other feature and the state of the world are set by the test;
    /// the news that reached the player is kept. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeAiHost : IFlawAiHost
    {
        public sealed class Published
        {
            public int RecordId;
            public FlawResponse Response;
            public FlawNewsPlan Plan;
        }

        public FlawBalance Balance_ = FlawBalance.Defaults();
        public IModRng Rng_ = FakeRng.Always(0.5f);

        /// <summary>The money of the company, and its default threshold (negative: the debt limit the game applies to it) - the fake of <c>ModCredit.ThresholdOf</c>.</summary>
        public float Money = 1e10f;

        public float Threshold = -1e7f;

        /// <summary>The owner is not in the world: the headroom is NaN.</summary>
        public bool OwnerMissing;

        public bool FastForward;
        public float[] Bias;
        public bool ThrowOnBias;
        public bool ThrowOnPublish;
        public bool ThrowOnHeadroom;

        public readonly List<Published> News = new List<Published>();
        public readonly List<bool> BiasAskedForPublic = new List<bool>();
        public readonly List<string> Logs = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public FlawBalance Balance
        {
            get { return Balance_; }
        }

        public IModRng Rng
        {
            get { return Rng_; }
        }

        public bool DuringFastForward
        {
            get { return FastForward; }
        }

        public float CashHeadroom(FlawRecord record)
        {
            if (ThrowOnHeadroom) throw new InvalidOperationException("the company is gone");
            return OwnerMissing ? float.NaN : AiFlawPolicy.CashHeadroom(Money, Threshold);
        }

        public float[] ResponseBias(FlawRecord record, bool isPublic)
        {
            BiasAskedForPublic.Add(isPublic);
            if (ThrowOnBias) throw new InvalidOperationException("the bias is broken");
            return Bias;
        }

        public void PublishNews(FlawRecord record, FlawResponse response, FlawNewsPlan plan)
        {
            if (ThrowOnPublish) throw new InvalidOperationException("the mail is broken");
            News.Add(new Published { RecordId = record.Id, Response = response, Plan = plan });
        }

        public void Log(string line)
        {
            Logs.Add(line);
        }

        public void Warn(string line)
        {
            Warnings.Add(line);
        }

        public void Error(string line, Exception exception)
        {
            Errors.Add(line);
        }
    }

    /// <summary>
    /// The answers of the AI companies wired together over a store, as the feature wires them: the pieces of <see cref="ResponseWorld"/> (the fake game, the channel book, the
    /// response engine), the scripted AI host and the engine of the AI. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class AiWorld
    {
        public readonly ResponseWorld World;
        public readonly FakeAiHost Host = new FakeAiHost();
        public readonly FlawAiEngine Engine;

        public AiWorld(params FlawRecord[] records)
        {
            World = new ResponseWorld(records);
            Engine = new FlawAiEngine(World.Engine, Host);
        }

        public FlawRecord Record(int id)
        {
            return World.Store.Find(id);
        }

        /// <summary>
        /// A record of the AI company <c>ai:3</c> in <paramref name="state"/> with the quote of the 1994 example (<c>arith</c> by default; the patch is available for <c>security</c>
        /// from 2015).
        /// </summary>
        public static FlawRecord Ai(FlawState state, int id = 1, FlawSeverity severity = FlawSeverity.Major, string typeId = "security", int year = 2018, bool coverUp = false)
        {
            return ResponseWorld.Known(state, id, severity, typeId, "ai:3", year, coverUp);
        }
    }

    /// <summary>
    /// The decision host the feature has (<c>FlawDecisionDispatcher</c>), with the player's window replaced by <see cref="FakeDecisionHost"/> and the AI branch wired to a real
    /// <see cref="FlawAiEngine"/>: what <c>DispatchAi</c> does in the game. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class ForwardingDecisionHost : IFlawDecisionHost
    {
        public readonly FakeDecisionHost Inner = new FakeDecisionHost();
        public FlawAiEngine Ai;
        public readonly List<int> AiDispatched = new List<int>();

        public int Today
        {
            get { return Inner.Today; }
        }

        public FlawBalance Balance
        {
            get { return Inner.Balance; }
        }

        public string[] BuildArgs(FlawRecord record, FlawQuote quote)
        {
            return Inner.BuildArgs(record, quote);
        }

        public FlawFireOutcome Fire(string eventId, string companyKey, string[] args)
        {
            return Inner.Fire(eventId, companyKey, args);
        }

        public void SendAutoMail(FlawRecord record, FlawResponse response, string eventId)
        {
            Inner.SendAutoMail(record, response, eventId);
        }

        public bool DispatchAi(FlawRecord record)
        {
            AiDispatched.Add(record.Id);
            return Ai.Decide(record);
        }

        public void Log(string line)
        {
            Inner.Log(line);
        }

        public void Warn(string line)
        {
            Inner.Warn(line);
        }

        public void Error(string line, Exception exception)
        {
            Inner.Error(line, exception);
        }
    }
}
