using System;
using PTMod.Core;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The five parts of the feature the player can switch separately (FR-12 item 2). A part off means: no new operations of it and no meddling
    /// with the matching part of the game's cycle; the loans already taken keep being served as long as the feature itself is on.
    /// </summary>
    public enum SubFeature
    {
        /// <summary>The "default risk" letter (FR-1).</summary>
        PreWarning,

        /// <summary>The startup credit line (FR-7).</summary>
        StartupCredit,

        /// <summary>The bridge loan (FR-6).</summary>
        BridgeLoan,

        /// <summary>The "last chance" offer (FR-8).</summary>
        Bailout,

        /// <summary>The early exit from default (FR-9).</summary>
        EarlyExit,
    }

    /// <summary>
    /// The parameters of the feature in the hub (FR-12 item 2): one switch per <see cref="SubFeature"/>, all on by default. The feature itself is
    /// switched by the hub's own toggle, so "enabled" is not here. Only the declarations live in this pure file (so that a test can check them);
    /// reading the values is <c>RecoverySettings</c>. The label of a switch is <c>rec.param.&lt;key&gt;</c>, its tooltip <c>rec.param.&lt;key&gt;.tooltip</c>.
    /// </summary>
    public static class RecoveryParams
    {
        public const string PreWarning = "preWarning";
        public const string StartupCredit = "startupCredit";
        public const string BridgeLoan = "bridgeLoan";
        public const string Bailout = "bailout";
        public const string EarlyExit = "earlyExit";

        /// <summary>The name of the parameter of a part in <c>ptmod_settings.json</c> (<c>features.bankruptcy-recovery.params.&lt;key&gt;</c>).</summary>
        public static string KeyOf(SubFeature part)
        {
            switch (part)
            {
                case SubFeature.PreWarning: return PreWarning;
                case SubFeature.StartupCredit: return StartupCredit;
                case SubFeature.BridgeLoan: return BridgeLoan;
                case SubFeature.Bailout: return Bailout;
                case SubFeature.EarlyExit: return EarlyExit;
                default: throw new ArgumentOutOfRangeException("part");
            }
        }

        /// <summary>The five declarations, in the order of the hub page. <paramref name="featureId"/> is the owner's id.</summary>
        public static ParamSpec[] Create(string featureId)
        {
            var specs = new ParamSpec[5];
            specs[0] = Switch(featureId, PreWarning);
            specs[1] = Switch(featureId, StartupCredit);
            specs[2] = Switch(featureId, BridgeLoan);
            specs[3] = Switch(featureId, Bailout);
            specs[4] = Switch(featureId, EarlyExit);
            return specs;
        }

        private static ParamSpec Switch(string featureId, string key)
        {
            return new ParamSpec
            {
                FeatureId = featureId,
                Key = key,
                LabelKey = "rec.param." + key,
                TooltipKey = "rec.param." + key + ".tooltip",
                Type = ParamType.Bool,
                Default = 1,
                Min = 0,
                Max = 1,
                Step = 1,
                RequiresReload = false,
            };
        }
    }
}
