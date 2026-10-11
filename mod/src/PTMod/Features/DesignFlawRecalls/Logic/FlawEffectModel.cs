using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The effect types the pack registers with <c>EventEffects.Register</c> (design.md Component 9), the names written in <c>events.json</c>.</summary>
    public static class FlawEffectTypes
    {
        public const string Recall = "FlawRecall";
        public const string Stepping = "FlawStepping";
        public const string SoftwarePatch = "FlawSoftwarePatch";
        public const string Ignore = "FlawIgnore";

        /// <summary>
        /// The fifth type, not a button: the line about a public incident of an AI company in the event journal (<c>EventEffects.ApplyDirect</c>, FR-11 item 4). It changes
        /// nothing in the game and is not in <see cref="All"/> (no event of the pack uses it).
        /// </summary>
        public const string News = "FlawNews";

        /// <summary>All four, in the order Recall, Stepping, Patch, Ignore.</summary>
        public static readonly string[] All = { Recall, Stepping, SoftwarePatch, Ignore };

        /// <summary>The effect type of a response; <c>null</c> for <see cref="FlawResponse.None"/>.</summary>
        public static string TypeOf(FlawResponse response)
        {
            switch (response)
            {
                case FlawResponse.Recall: return Recall;
                case FlawResponse.Stepping: return Stepping;
                case FlawResponse.Patch: return SoftwarePatch;
                case FlawResponse.Ignore: return Ignore;
                default: return null;
            }
        }

        /// <summary>The response an effect type stands for; <c>false</c> for any other type.</summary>
        public static bool TryResponseOf(string type, out FlawResponse response)
        {
            switch (type)
            {
                case Recall: response = FlawResponse.Recall; return true;
                case Stepping: response = FlawResponse.Stepping; return true;
                case SoftwarePatch: response = FlawResponse.Patch; return true;
                case Ignore: response = FlawResponse.Ignore; return true;
                default: response = FlawResponse.None; return false;
            }
        }
    }

    /// <summary>
    /// The numbers of a resolved effect of the pack, in the shape the event system keeps them (<c>AppliedEffect.value</c>, <c>curve</c>, <c>textKey</c>,
    /// <c>textArgs</c>): a plain value, so the formulas are tested without the engine.
    /// </summary>
    public sealed class FlawEffectNumbers
    {
        /// <summary>The id of the record the effect belongs to (also <c>curve[0]</c>).</summary>
        public int RecordId;

        /// <summary>The money in dollars, negative when it is spent; 0 for an ignore.</summary>
        public float Value;

        /// <summary>
        /// What the line under the button is made of: <c>[recordId]</c> for a recall, <c>[recordId, days]</c> for a revision, <c>[recordId, ipsLoss, months]</c> for
        /// a patch, <c>[recordId, oemCancel, lawsuit]</c> for an ignore.
        /// </summary>
        public float[] Curve = new float[0];

        /// <summary>The key of the line (<c>dfr.fx.recall</c>, <c>dfr.fx.stepping</c>, <c>dfr.fx.patch</c>, <c>dfr.fx.ignore</c>).</summary>
        public string TextKey = string.Empty;

        public string[] TextArgs = new string[0];
    }

    /// <summary>
    /// The numbers behind the four effect types (FR-5 item 2, FR-6..FR-9, D1/D9): <c>Resolve</c> takes the amounts from the saved quote of the record
    /// (the same numbers the window shows and the application takes), and the line under the button is made from those numbers alone (<c>Describe</c>).
    /// Pure: the formatting of money is <see cref="IFlawFormat"/>.
    /// </summary>
    public static class FlawEffectModel
    {
        public const string RecallKey = "dfr.fx.recall";
        public const string SteppingKey = "dfr.fx.stepping";
        public const string PatchKey = "dfr.fx.patch";
        public const string IgnoreKey = "dfr.fx.ignore";

        /// <summary>The line of the event journal about an incident of an AI company (arguments: the company, the CPU, the type, the severity, the reaction).</summary>
        public const string NewsKey = "dfr.fx.news";

        /// <summary>
        /// The numbers of the effect of <paramref name="response"/> for the record <paramref name="recordId"/> with the quote <paramref name="quote"/> (<c>null</c> - no
        /// quote: everything is 0). Never throws.
        /// </summary>
        public static FlawEffectNumbers Resolve(FlawResponse response, int recordId, FlawQuote quote, IFlawFormat format)
        {
            FlawQuote q = quote ?? new FlawQuote();
            var numbers = new FlawEffectNumbers { RecordId = recordId };

            switch (response)
            {
                case FlawResponse.Recall:
                    numbers.Value = -Money(q.RecallCost);
                    numbers.Curve = new[] { (float)recordId };
                    break;

                case FlawResponse.Stepping:
                    numbers.Value = -Money(q.SteppingCost);
                    numbers.Curve = new[] { (float)recordId, q.SteppingDays };
                    break;

                case FlawResponse.Patch:
                    numbers.Value = -Money(q.PatchCost);
                    numbers.Curve = new[] { (float)recordId, q.PatchIpsLoss, q.PatchMonths };
                    break;

                case FlawResponse.Ignore:
                    numbers.Value = 0f;
                    numbers.Curve = new[] { (float)recordId, q.OemCancelChance, Money(q.ClassActionCost) };
                    break;

                default:
                    numbers.Curve = new[] { (float)recordId };
                    break;
            }

            Describe(response, numbers, format);
            return numbers;
        }

        /// <summary>Fills the line of the effect (<see cref="FlawEffectNumbers.TextKey"/>, <see cref="FlawEffectNumbers.TextArgs"/>) from <see cref="FlawEffectNumbers.Value"/> and <see cref="FlawEffectNumbers.Curve"/> only.</summary>
        public static void Describe(FlawResponse response, FlawEffectNumbers numbers, IFlawFormat format)
        {
            DescribeCore(response, numbers.Value, numbers.Curve, format, out numbers.TextKey, out numbers.TextArgs);
        }

        /// <summary>
        /// The key and the arguments of the line of a saved effect: <paramref name="value"/> and <paramref name="curve"/> are what <see cref="Resolve"/> made (the
        /// event system keeps them in the save), so the line is the same before and after a load.
        /// </summary>
        public static void DescribeCore(FlawResponse response, float value, float[] curve, IFlawFormat format, out string key, out string[] args)
        {
            IFlawFormat f = format ?? new PlainFlawFormat();
            float spent = -value;
            switch (response)
            {
                case FlawResponse.Recall:
                    key = RecallKey;
                    args = new[] { f.Money(spent) };
                    return;

                case FlawResponse.Stepping:
                    key = SteppingKey;
                    args = new[] { f.Money(spent), Whole(At(curve, 1)) };
                    return;

                case FlawResponse.Patch:
                    key = PatchKey;
                    args = new[] { f.Money(spent), f.Percent(At(curve, 1)), Whole(At(curve, 2)) };
                    return;

                case FlawResponse.Ignore:
                    key = IgnoreKey;
                    args = new[] { f.Percent(At(curve, 1)), f.Money(At(curve, 2)) };
                    return;

                default:
                    key = RecallKey;
                    args = new[] { f.Money(0f) };
                    return;
            }
        }

        /// <summary>The id of the record an effect belongs to: the first number of the curve it was resolved with; <c>false</c> when there is none.</summary>
        public static bool TryRecordId(float[] curve, out int recordId)
        {
            recordId = 0;
            if (curve == null || curve.Length == 0) return false;

            float first = curve[0];
            if (float.IsNaN(first) || float.IsInfinity(first) || first < 1f || first > int.MaxValue) return false;
            recordId = (int)first;
            return true;
        }

        private static float Money(float amount)
        {
            return amount > 0f && !float.IsInfinity(amount) ? amount : 0f;
        }

        private static float At(float[] curve, int index)
        {
            return curve != null && index >= 0 && index < curve.Length ? curve[index] : 0f;
        }

        private static string Whole(float value)
        {
            return ((int)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
