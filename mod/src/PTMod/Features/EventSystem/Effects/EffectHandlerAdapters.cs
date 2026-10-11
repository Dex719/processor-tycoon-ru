using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// A standard effect type (<c>Logic/Effects/Handlers</c>) as the API shows it: an <c>IEventEffectHandler</c> registered with <c>EventEffects.Register</c> the
    /// same way a pack registers its own types (design.md, "Обработчики эффектов"). The parameters were checked by <c>StandardEffectRules</c> when the content was
    /// loaded, so <see cref="Validate"/> has nothing to add. The engine itself calls <see cref="Port"/> directly.
    /// </summary>
    internal class StandardEffectHandler : IEventEffectHandler
    {
        public StandardEffectHandler(IEffectHandlerPort port)
        {
            if (port == null) throw new ArgumentNullException("port");
            Port = port;
        }

        /// <summary>The pure handler behind this one.</summary>
        public IEffectHandlerPort Port { get; private set; }

        public string Type
        {
            get { return Port.Type; }
        }

        public bool Validate(EventEffect spec, IList<string> errors)
        {
            return true;
        }

        public AppliedEffect Resolve(EventEffect spec, EventContext ctx)
        {
            return Port.Resolve(spec, EffectContexts.ToCall(ctx, spec));
        }

        public void Apply(AppliedEffect effect, EventContext ctx)
        {
            Port.Apply(effect, EffectContexts.ToCall(ctx, null));
        }

        public EffectText Describe(AppliedEffect effect)
        {
            return Port.Describe(effect);
        }

        /// <summary>The adapter that fits the port: a timed one for a timed port.</summary>
        public static IEventEffectHandler For(IEffectHandlerPort port)
        {
            return port.IsTimed ? new StandardTimedEffectHandler(port) : new StandardEffectHandler(port);
        }
    }

    /// <summary>A timed standard effect type (<c>ModifierTimed</c>, <c>FactoryOutput</c>, <c>MarketDemand</c>) as the API shows it.</summary>
    internal sealed class StandardTimedEffectHandler : StandardEffectHandler, ITimedEffectHandler
    {
        public StandardTimedEffectHandler(IEffectHandlerPort port)
            : base(port)
        {
        }

        public void Tick(AppliedEffect effect, EventContext ctx)
        {
            Port.Tick(effect, EffectContexts.ToCall(ctx, null));
        }

        public void Remove(AppliedEffect effect, EventContext ctx)
        {
            Port.Remove(effect, EffectContexts.ToCall(ctx, null));
        }

        public void Restore(AppliedEffect effect, EventContext ctx)
        {
            Port.Restore(effect, EffectContexts.ToCall(ctx, null));
        }
    }

    /// <summary>
    /// A pack's <c>IEventEffectHandler</c> as the runtime calls it: every <see cref="EffectCall"/> becomes an <c>EventContext</c> with the company of the game
    /// (null for <c>global</c>), the game facade, the aggregator, the intensity and the effect's definition.
    /// </summary>
    internal sealed class ForeignEffectPort : IEffectHandlerPort
    {
        private readonly IEventEffectHandler _handler;
        private readonly ITimedEffectHandler _timed;
        private readonly CompanyDirectory _directory;

        public ForeignEffectPort(string type, IEventEffectHandler handler, CompanyDirectory directory)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            Type = type;
            _handler = handler;
            _timed = handler as ITimedEffectHandler;
            _directory = directory;
        }

        public string Type { get; private set; }

        public bool IsTimed
        {
            get { return _timed != null; }
        }

        public AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            return _handler.Resolve(spec, Context(call));
        }

        public void Apply(AppliedEffect effect, EffectCall call)
        {
            _handler.Apply(effect, Context(call));
        }

        public EffectText Describe(AppliedEffect effect)
        {
            return _handler.Describe(effect);
        }

        public void Tick(AppliedEffect effect, EffectCall call)
        {
            if (_timed != null) _timed.Tick(effect, Context(call));
        }

        public void Remove(AppliedEffect effect, EffectCall call)
        {
            if (_timed != null) _timed.Remove(effect, Context(call));
        }

        public void Restore(AppliedEffect effect, EffectCall call)
        {
            if (_timed != null) _timed.Restore(effect, Context(call));
        }

        private EventContext Context(EffectCall call)
        {
            return EffectContexts.ToContext(call, _directory);
        }
    }

    /// <summary>The translation between the pure <see cref="EffectCall"/> and the game-bound <c>EventContext</c>.</summary>
    internal static class EffectContexts
    {
        public static EventContext ToContext(EffectCall call, CompanyDirectory directory)
        {
            if (call == null) return new EventContext();
            ICompany company = null;
            if (directory != null && CompanyKey.IsValid(call.CompanyKey)) company = directory.Resolve(call.CompanyKey);

            return new EventContext
            {
                Definition = call.Definition,
                Instance = call.Instance != null ? new EventInstance(call.Instance, call.Definition) : null,
                Company = company,
                CompanyKey = call.CompanyKey,
                Today = call.Today,
                Rng = call.Rng,
                Game = directory != null ? directory.Game : null,
                Contributions = call.Contributions,
                Severity = call.Severity,
                Spec = call.Spec,
            };
        }

        public static EffectCall ToCall(EventContext ctx, EventEffect spec)
        {
            if (ctx == null) return new EffectCall { Spec = spec };
            return new EffectCall
            {
                Definition = ctx.Definition,
                Instance = ctx.Instance != null ? ctx.Instance.Data : null,
                Spec = spec ?? ctx.Spec,
                CompanyKey = ctx.CompanyKey,
                Today = ctx.Today,
                Rng = ctx.Rng,
                Severity = ctx.Severity,
                Contributions = ctx.Contributions,
            };
        }
    }

    /// <summary>
    /// The engine's table of effect types: the registrations of <c>EventEffects.Register</c> (the first one of a type wins, AC-6.1, FR-6 item 5) and, for the
    /// runtime, the port of each type - the standard handlers directly, a pack's handler through <see cref="ForeignEffectPort"/>.
    /// </summary>
    internal sealed class EngineEffectTable : IEffectTable
    {
        private readonly TypeTable<IEventEffectHandler> _handlers = new TypeTable<IEventEffectHandler>();
        private readonly Dictionary<string, IEffectHandlerPort> _ports = new Dictionary<string, IEffectHandlerPort>(StringComparer.Ordinal);
        private readonly CompanyDirectory _directory;

        public EngineEffectTable(CompanyDirectory directory)
        {
            _directory = directory;
        }

        public bool Register(string type, IEventEffectHandler handler)
        {
            if (!_handlers.Register(type, handler, line => ModLog.Warn(EventSystemApi.FeatureId, line))) return false;
            var standard = handler as StandardEffectHandler;
            _ports[type] = standard != null ? standard.Port : new ForeignEffectPort(type, handler, _directory);
            return true;
        }

        public IEventEffectHandler Handler(string type)
        {
            return _handlers.Get(type);
        }

        public IList<string> Types
        {
            get { return _handlers.Types; }
        }

        public IEffectHandlerPort Get(string type)
        {
            IEffectHandlerPort port;
            return type != null && _ports.TryGetValue(type, out port) ? port : null;
        }
    }
}
