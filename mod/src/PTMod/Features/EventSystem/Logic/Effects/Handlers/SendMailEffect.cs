using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>SendMail</c>: an information mail to the player through <c>ModMail.Send</c> - no choices, priority <c>Normal</c>, the tag <c>es:fx:&lt;effect id&gt;</c>
    /// (the mail of an instance itself is <c>es:&lt;instanceId&gt;</c>), the texts <c>title</c> / <c>text</c> (keys) with the arguments of the instance. Only the
    /// player reads mail: for an AI company the effect is <c>Skipped</c>. A mail the core refuses makes the effect <c>Failed</c>.
    /// </summary>
    public sealed class SendMailEffect : StandardEffect
    {
        public const string TextKey = "es.fx.mail";

        /// <summary>The prefix of the tag of an effect's mail.</summary>
        public const string TagPrefix = "es:fx:";

        private readonly IEffectWorld _world;

        public SendMailEffect(IEffectWorld world)
            : base(StandardEffectRules.SendMail)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
        }

        /// <summary>The tag of the mail of an effect.</summary>
        public static string TagOf(AppliedEffect effect)
        {
            return TagPrefix + (effect != null ? effect.id : string.Empty);
        }

        public override AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            AppliedEffect effect = NewEffect(spec, call);
            effect.durationDays = 0;
            return effect;
        }

        public override void Apply(AppliedEffect effect, EffectCall call)
        {
            bool toPlayer = PTMod.Core.Logic.Companies.CompanyKey.IsPlayer(effect.companyKey) || IsGlobal(effect.companyKey);
            if (!toPlayer)
            {
                Skip(effect);
                return;
            }

            IEffectParams p = ParamsOf(call != null ? call.Spec : null);
            string title = p.GetString("title", null);
            string text = p.GetString("text", null);
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(text)) throw new InvalidOperationException("SendMail needs 'title' and 'text'");

            string[] args = call != null && call.Instance != null && call.Instance.args != null ? call.Instance.args : new string[0];
            int id = _world.SendMail(title, text, TagOf(effect), args);
            if (id < 0) throw new InvalidOperationException("ModMail refused the mail of " + effect.id);
        }

        public override EffectText Describe(AppliedEffect effect)
        {
            return new EffectText(TextKey);
        }
    }
}
