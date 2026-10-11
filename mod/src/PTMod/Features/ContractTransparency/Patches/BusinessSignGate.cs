using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using GameNegotiationWindow = ProcessorTycoon.CompanySystem.Business.ContractNegotiationWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// P: <c>ContractNegotiationWindow.SignContract()</c>, prefix and postfix (design.md, Component 11; FR-10): the question before a deal between companies (the foundry services contract) is signed.
    /// <list type="bullet">
    /// <item><b>Prefix.</b> Unless the mode is "never ask", the signature is put off and <c>ModUI.Confirm</c> (the core's modal question, one window for the whole mod, no window of this feature) lists the
    /// terms: the provider, the client, the length in years, the share of the lines, the mark-up, the exclusivity, the fines of both sides and the verdict of the other side (<see cref="DealLines"/>).
    /// "Sign" calls the original under <see cref="BypassScope"/>; "No" and the cross do nothing and leave the negotiation window as it is. A second click on "Sign contract" while the question is up
    /// is swallowed, so one question cannot become two deals. If the question cannot be shown, or the fields of the window are not as expected, the signature goes ahead as in the vanilla game.</item>
    /// <item><b>Postfix.</b> After the original has run, the line "deal signed" with the terms goes into the log (<see cref="LogTypes.BusinessSigned"/>), also in the mode "never ask".</item>
    /// <item><b>The terms</b> are read from the private fields of the window (found once through <c>GameMembers</c>); the window refreshes them every frame, so they are what the player sees (FR-10 item 2).</item>
    /// </list>
    /// "Only risky" asks as "always": a deal has no risk model in this spec (the assumption of FR-10). To check in the game (the owner, M8): "Sign contract" then "No" leaves the window and the deal alone;
    /// "Sign contract" then "Sign" creates the deal and the log has "deal signed".
    /// </summary>
    internal static class BusinessSignGate
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string PrefixSite = "BusinessSignGate/SignContract.prefix";
        private const string PostfixSite = "BusinessSignGate/SignContract.postfix";
        private const string AnswerSite = "BusinessSignGate/Signed";

        /// <summary>The mod's own call of the original after the player said "Sign": the prefix lets it through.</summary>
        [ThreadStatic]
        internal static bool BypassScope;

        /// <summary>A question about a deal is open (or waits): another click on "Sign contract" is not asked about again.</summary>
        private static bool _asking;

        private static bool _resolved;
        private static string _missing;
        private static FieldInfo _provider;
        private static FieldInfo _client;
        private static FieldInfo _duration;
        private static FieldInfo _capacity;
        private static FieldInfo _markup;
        private static FieldInfo _providerFines;
        private static FieldInfo _clientFines;
        private static FieldInfo _exclusivity;
        private static FieldInfo _score;
        private static MethodInfo _sign;

        // ---- the patches ----

        [ModPatch(typeof(GameNegotiationWindow), "SignContract", PatchKind.Prefix, ArgTypes = new Type[0], Optional = true)]
        private static bool Prefix(GameNegotiationWindow __instance, out bool __state)
        {
            bool run = true;
            bool track = true;
            bool ok = ContractTransparencyFeature.Guarded(PrefixSite, () => { run = Decide(__instance, out track); });
            if (!ok)
            {
                // The vanilla way, as if the patch were not there (NFR-2); the deal is still logged.
                run = true;
                track = true;
            }
            __state = track;
            return run;
        }

        [ModPatch(typeof(GameNegotiationWindow), "SignContract", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void Postfix(GameNegotiationWindow __instance, bool __state)
        {
            if (!__state) return;   // the original did not run: the question is still open, or the click was swallowed
            ContractTransparencyFeature.Guarded(PostfixSite, () => Record(__instance));
        }

        // ---- the decision ----

        /// <summary>The body of the prefix: true lets the original run now. <paramref name="track"/> is true when the original is going to run (the postfix records it).</summary>
        private static bool Decide(GameNegotiationWindow window, out bool track)
        {
            track = true;
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || window == null) return true;

            SignAction action = SignGatePolicy.Decide(new SignInput
            {
                Bypass = BypassScope,
                Mode = ContractTransparencySettings.ConfirmMode,
                FieldsAvailable = Resolve(),   // a field of the window is missing: the question stays off (FR-12 item 3)
                QuestionOpen = _asking,
            });
            if (action == SignAction.PassThrough) return true;
            if (action == SignAction.Swallow)
            {
                track = false;   // the question is up already
                return false;
            }

            DealInput deal = Read(window);
            string message = Message(deal);

            bool answeredInside = true;   // the core answers "no" inside Confirm when it cannot show the question
            bool shown = true;
            _asking = true;
            ModUI.Confirm(
                "ct.business.title",
                message,
                () =>
                {
                    _asking = false;
                    Sign(window);
                },
                () =>
                {
                    _asking = false;
                    if (answeredInside) shown = false;
                },
                "ct.business.sign");
            answeredInside = false;

            if (!shown)
            {
                // The question cannot be shown: the player must still be able to sign, as in the vanilla game.
                ModLog.Info(Feature, "business: the question cannot be shown; the deal is signed without it");
                return true;
            }

            track = false;   // the signature waits for the answer
            return false;
        }

        /// <summary>"Sign": the original, once, under the bypass. A window that went with its scene is not signed.</summary>
        private static void Sign(GameNegotiationWindow window)
        {
            ContractTransparencyFeature.Guarded(AnswerSite, () =>
            {
                if (window == null) return;   // Unity's null: the scene was left while the question was open

                BypassScope = true;
                try
                {
                    _sign.Invoke(window, null);
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null) throw ex.InnerException;
                    throw;
                }
                finally
                {
                    BypassScope = false;
                }
            });
        }

        private static string Message(DealInput deal)
        {
            GameArgFormatter formatter = GameArgFormatter.Current();
            var lines = new List<string>();
            foreach (PanelLine line in DealLines.Build(deal)) lines.Add(UiKit.PlainText(line, formatter));
            return string.Join("\n", lines.ToArray());
        }

        // ---- the record ----

        private static void Record(GameNegotiationWindow window)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || feature.Log == null || window == null || !Resolve()) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            feature.Log.Model.Add(DealLines.LogEntryOf(Read(window), LogDates.Write(today)));
        }

        // ---- the window's fields ----

        /// <summary>The terms the window holds now.</summary>
        private static DealInput Read(GameNegotiationWindow window)
        {
            var provider = _provider.GetValue(window) as ICompany;
            var client = _client.GetValue(window) as ICompany;
            return new DealInput
            {
                Provider = provider == null ? string.Empty : (provider.Name ?? string.Empty),
                Client = client == null ? string.Empty : (client.Name ?? string.Empty),
                Years = (int)_duration.GetValue(window),
                CapacityPercent = (int)_capacity.GetValue(window),
                MarkupPercent = (int)_markup.GetValue(window),
                Exclusive = (bool)_exclusivity.GetValue(window),
                ProviderFines = (float)_providerFines.GetValue(window),
                ClientFines = (float)_clientFines.GetValue(window),
                AcceptanceScore = (int)_score.GetValue(window),
            };
        }

        private static bool Resolve()
        {
            if (_resolved) return _missing == null;
            _resolved = true;

            // The members go through GameMembers: ContractSelfCheck checks the same rows, and the debug flag of TSK-020 can hide any of them.
            var missing = new List<string>();
            _provider = Member(MemberKeys.DealProvider, "currentProvider", typeof(ICompany), missing);
            _client = Member(MemberKeys.DealClient, "currentClient", typeof(ICompany), missing);
            _duration = Member(MemberKeys.DealDuration, "duration", typeof(int), missing);
            _capacity = Member(MemberKeys.DealCapacity, "productionCapacity", typeof(int), missing);
            _markup = Member(MemberKeys.DealMarkup, "markupPrice", typeof(int), missing);
            _providerFines = Member(MemberKeys.DealProviderFines, "providerFines", typeof(float), missing);
            _clientFines = Member(MemberKeys.DealClientFines, "clientFines", typeof(float), missing);
            _exclusivity = Member(MemberKeys.DealExclusivity, "exclusivity", typeof(bool), missing);
            _score = Member(MemberKeys.DealScore, "acceptanceScore", typeof(int), missing);
            _sign = GameMembers.Method(MemberKeys.SignContract, typeof(GameNegotiationWindow), "SignContract", new Type[0]);
            if (_sign == null) missing.Add("method SignContract()" + GameMembers.HiddenNote(MemberKeys.SignContract));

            if (missing.Count > 0)
            {
                _missing = string.Join("; ", missing.ToArray());
                ModLog.Warn(Feature, "business: ContractNegotiationWindow lacks " + _missing + "; the question before a deal is signed stays off");
            }
            return _missing == null;
        }

        private static FieldInfo Member(string key, string name, Type type, List<string> missing)
        {
            FieldInfo field = GameMembers.Field(key, typeof(GameNegotiationWindow), name, type);
            if (field == null) missing.Add("field " + name + " (" + type.Name + ")" + GameMembers.HiddenNote(key));
            return field;
        }
    }
}
