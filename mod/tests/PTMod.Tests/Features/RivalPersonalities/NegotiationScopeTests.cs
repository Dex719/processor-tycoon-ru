using System;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The attitude in the negotiations of business contracts (TSK-017; FR-8; design.md Component 10, formulas 5 and 8): the scope that tells the window of the
    /// player from the AI-AI deals, the arithmetic of the bonus (<c>round(a / 5)</c> for the interest, <c>clamp(round(a / 10), 0, 10)</c> for the markup of a
    /// foundry partner), the veto of the game that is never changed, and the closing of the scope in every case. The patches themselves touch the game, so their
    /// wiring is read from the source.
    /// </summary>
    [Covers(typeof(NegotiationScopeCore), typeof(NegotiationParty), typeof(NegotiationRules), typeof(NegotiationBonus), typeof(NegotiationNumbers))]
    public class NegotiationScopeTests
    {
        private const int Stamp = 1000;

        private static NegotiationBonus Bonus(bool asProvider, float attitude, bool discount)
        {
            return NegotiationRules.Bonus(asProvider, attitude, discount, NegotiationNumbers.Default);
        }

        // ---- the arithmetic ----

        [Test]
        public void TheAttitude_AddsRoundOfAFifth_ToTheInterest()
        {
            Assert.Equal(8, Bonus(true, 40f, false).Interest, "+40 -> +8");
            Assert.Equal(-6, Bonus(true, -30f, false).Interest, "-30 -> -6");
            Assert.Equal(0, Bonus(true, 0f, false).Interest);
            Assert.Equal(20, Bonus(false, 100f, false).Interest, "+100 -> +20, the client too");
            Assert.Equal(-20, Bonus(false, -100f, false).Interest);
            Assert.Equal(1, Bonus(true, 2.5f, false).Interest, "halves go away from zero");
            Assert.Equal(-1, Bonus(true, -2.5f, false).Interest);
        }

        [Test]
        public void AFoundryPartner_TakesPointsOffTheMarkup_ButOnlyAsAProvider_AndOnlyForAGoodAttitude()
        {
            Assert.Equal(4, Bonus(true, 40f, true).Price, "AC-8.1: +40 -> 4 points");
            Assert.Equal(10, Bonus(true, 100f, true).Price, "the most is 10");
            Assert.Equal(0, Bonus(true, -30f, true).Price, "AC-8.3: a bad attitude gives no discount, and no surcharge");
            Assert.Equal(0, Bonus(true, 4f, true).Price);
            Assert.Equal(1, Bonus(true, 5f, true).Price);
            Assert.Equal(0, Bonus(false, 80f, true).Price, "as a client it takes no points off the markup");
            Assert.Equal(0, Bonus(true, 80f, false).Price, "an archetype without negotiationDiscount gives none");
        }

        [Test]
        public void TheNumbers_AreTheOnesOfTheConfig_AndTheDesignWithoutOne()
        {
            var rig = new AttitudeRig();
            NegotiationNumbers numbers = NegotiationNumbers.From(rig.Catalog.Config);
            Assert.Equal(5, numbers.InterestDivisor);
            Assert.Equal(10, numbers.DiscountDivisor);
            Assert.Equal(10, numbers.DiscountMaxPoints);

            NegotiationNumbers none = NegotiationNumbers.From(null);
            Assert.Equal(5, none.InterestDivisor);
            Assert.Equal(10, NegotiationNumbers.Default.DiscountMaxPoints);

            NegotiationBonus wide = NegotiationRules.Bonus(true, 40f, true, new NegotiationNumbers(10, 20, 1));
            Assert.Equal(4, wide.Interest, "a divisor of 10");
            Assert.Equal(1, wide.Price, "a discount of 2 points, cut to the largest of 1");
            Assert.True(NegotiationRules.Bonus(true, 40f, true, null).Interest == 8, "no numbers: the design's");
        }

        [Test]
        public void TheVeto_IsNeverChanged()
        {
            Assert.True(NegotiationRules.IsVeto(-1000));
            Assert.False(NegotiationRules.IsVeto(-999));
            Assert.Equal(-1000, NegotiationRules.InterestAfter(-1000, 20), "FR-8 item 2");
            Assert.Equal(-1000, NegotiationRules.InterestAfter(-1000, -20));
            Assert.Equal(8, NegotiationRules.InterestAfter(0, 8));
            Assert.Equal(-14, NegotiationRules.InterestAfter(-20, 6));

            int interest = -1000;
            int price = 7;
            NegotiationRules.Apply(new NegotiationBonus(8, 4), ref interest, ref price);
            Assert.Equal(-1000, interest, "the veto stays");
            Assert.Equal(11, price, "the price discount is added all the same");
        }

        [Test]
        public void TheSign_IsWrittenForThePanel()
        {
            Assert.Equal("+8", NegotiationRules.Signed(8));
            Assert.Equal("-6", NegotiationRules.Signed(-6));
            Assert.Equal("0", NegotiationRules.Signed(0));
            Assert.True(new NegotiationBonus(0, 0).IsZero);
            Assert.False(new NegotiationBonus(0, 1).IsZero);
        }

        [Test]
        public void OnTheShippedContent_OnlyTheFoundryPartnerGivesTheDiscount()
        {
            var rig = new AttitudeRig().AssignAll();
            int withDiscount = 0;
            foreach (Archetype archetype in rig.Catalog.All)
            {
                if (archetype.behaviour.negotiationDiscount)
                {
                    withDiscount++;
                    Assert.Equal("foundry_partner", archetype.id);
                }
            }
            Assert.Equal(1, withDiscount);
        }

        [Test]
        public void AC_8_1_AndAC_8_3_OnTsmc_OnTheShippedContent()
        {
            var rig = new AttitudeRig().AssignAll();
            Archetype tsmc = rig.Book.ArchetypeOf(AttitudeRig.Tsmc);
            Assert.True(tsmc.behaviour.negotiationDiscount);
            NegotiationNumbers numbers = NegotiationNumbers.From(rig.Catalog.Config);

            rig.SetAttitude(AttitudeRig.Tsmc, 40f);
            NegotiationBonus warm = NegotiationRules.Bonus(true, rig.Ledger.Get(AttitudeRig.Tsmc), tsmc.behaviour.negotiationDiscount, numbers);
            Assert.Equal(8 + 4, warm.Interest + warm.Price, "the sum of the scores is 12 higher than the game's");

            rig.SetAttitude(AttitudeRig.Tsmc, -30f);
            NegotiationBonus cold = NegotiationRules.Bonus(true, rig.Ledger.Get(AttitudeRig.Tsmc), tsmc.behaviour.negotiationDiscount, numbers);
            Assert.Equal(-6, cold.Interest, "the interest is 6 lower than the game's");
            Assert.Equal(0, cold.Price, "no discount");
        }

        // ---- the scope ----

        [Test]
        public void TheScope_OpensForThePlayerAndOneAiCompany_InEitherRole()
        {
            var scope = new NegotiationScopeCore();
            Assert.False(scope.Active);
            Assert.Equal(-1, scope.AiUniqueId);

            Assert.True(scope.Enter(NegotiationParty.Ai(9), NegotiationParty.Player, Stamp), "the player asks a foundry for lines");
            Assert.True(scope.Active);
            Assert.Equal(9, scope.AiUniqueId);
            Assert.True(scope.AiIsProvider);

            Assert.True(scope.Enter(NegotiationParty.Player, NegotiationParty.Ai(3), Stamp), "the player offers lines to an AI client");
            Assert.Equal(3, scope.AiUniqueId);
            Assert.False(scope.AiIsProvider);

            scope.Exit();
            Assert.False(scope.Active);
            Assert.Equal(-1, scope.AiUniqueId);
            Assert.Equal(2, scope.Entered);
            Assert.Equal(1, scope.Exited, "the second Enter replaced the first scope, only the last was closed by Exit");
        }

        [Test]
        public void TheScope_StaysClosed_ForADealWithoutThePlayer()
        {
            var scope = new NegotiationScopeCore();

            Assert.False(scope.Enter(NegotiationParty.Ai(1), NegotiationParty.Ai(9), Stamp), "AI-AI: AMD and GlobalFoundries");
            Assert.False(scope.Active);
            Assert.False(scope.Enter(NegotiationParty.Player, NegotiationParty.Player, Stamp));
            Assert.False(scope.Enter(NegotiationParty.Unknown, NegotiationParty.Player, Stamp), "a company the mod cannot name");
            Assert.False(scope.Enter(NegotiationParty.Player, NegotiationParty.Unknown, Stamp));
            Assert.False(scope.Enter(NegotiationParty.Unknown, NegotiationParty.Unknown, Stamp));
            Assert.False(scope.Active);
            Assert.Equal(0, scope.Entered);
        }

        [Test]
        public void OutsideTheScope_TheScoresOfTheGame_AreNotChanged()
        {
            var scope = new NegotiationScopeCore();
            int interest = 3;
            int price = -12;

            // an AI-AI deal: the scope is closed and the bonus is not even asked for
            bool asked = false;
            Func<int, bool, NegotiationBonus> bonusOf = (uid, provider) =>
            {
                asked = true;
                return new NegotiationBonus(8, 4);
            };
            Assert.False(scope.Apply(true, 9, Stamp, bonusOf, ref interest, ref price));
            Assert.False(scope.Apply(false, 1, Stamp, bonusOf, ref interest, ref price));
            Assert.False(asked);
            Assert.Equal(3, interest, "AC-8.2: the same as the game's");
            Assert.Equal(-12, price);

            // the window of the player is open for TSMC, but the call is for AMD: another company
            scope.Enter(NegotiationParty.Ai(AttitudeRig.Tsmc), NegotiationParty.Player, Stamp);
            Assert.False(scope.Apply(true, AttitudeRig.Amd, Stamp, bonusOf, ref interest, ref price));
            Assert.False(asked);
            Assert.Equal(3, interest);
            Assert.Equal(-12, price);
        }

        [Test]
        public void InsideTheScope_OnlyTheAiSide_InItsRole_GetsTheBonus()
        {
            var scope = new NegotiationScopeCore();
            scope.Enter(NegotiationParty.Ai(AttitudeRig.Tsmc), NegotiationParty.Player, Stamp);
            Func<int, bool, NegotiationBonus> bonusOf = (uid, provider) => new NegotiationBonus(8, provider ? 4 : 0);

            int interest = -5;
            int price = 10;
            Assert.True(scope.Apply(true, AttitudeRig.Tsmc, Stamp, bonusOf, ref interest, ref price));
            Assert.Equal(3, interest, "FR-8 item 1");
            Assert.Equal(14, price, "FR-8 item 5");

            // the same company in the other role (the score of the client in the same window): not the AI side
            int clientInterest = 0;
            int clientPrice = 0;
            Assert.False(scope.Apply(false, AttitudeRig.Tsmc, Stamp, bonusOf, ref clientInterest, ref clientPrice), "the player is the client in this window");
            Assert.Equal(0, clientInterest);

            // the veto
            int vetoed = -1000;
            int vetoPrice = 0;
            Assert.True(scope.Apply(true, AttitudeRig.Tsmc, Stamp, bonusOf, ref vetoed, ref vetoPrice));
            Assert.Equal(-1000, vetoed);

            // no bonus function: the call is treated and nothing is added
            int plain = 2;
            int plainPrice = 2;
            Assert.True(scope.Apply(true, AttitudeRig.Tsmc, Stamp, null, ref plain, ref plainPrice));
            Assert.Equal(2, plain);
            Assert.Equal(2, plainPrice);
        }

        [Test]
        public void TheScope_IsClosedByTheFinalizer_EvenWhenTheWindowThrows()
        {
            var scope = new NegotiationScopeCore();

            // the prefix, the original method that throws, the finalizer: the order Harmony gives them
            Exception rethrown = null;
            try
            {
                scope.Enter(NegotiationParty.Ai(AttitudeRig.Tsmc), NegotiationParty.Player, Stamp);
                Assert.True(scope.Active);
                throw new InvalidOperationException("UpdateScores failed");
            }
            catch (InvalidOperationException e)
            {
                rethrown = e;
            }
            finally
            {
                scope.Exit();
            }

            Assert.False(scope.Active, "the scope is closed");
            Assert.NotNull(rethrown, "the exception goes on to the game");
            Assert.Equal(1, scope.Exited);

            int interest = 1;
            int price = 1;
            Assert.False(scope.Apply(true, AttitudeRig.Tsmc, Stamp, (u, p) => new NegotiationBonus(8, 4), ref interest, ref price), "an AI-AI call after it is vanilla");
            Assert.Equal(1, interest);
        }

        [Test]
        public void AScopeNobodyClosed_IsDroppedInALaterFrame()
        {
            var scope = new NegotiationScopeCore();
            scope.Enter(NegotiationParty.Ai(AttitudeRig.Amd), NegotiationParty.Player, Stamp);

            int interest = 0;
            int price = 0;
            Assert.False(scope.Apply(true, AttitudeRig.Amd, Stamp + 1, (u, p) => new NegotiationBonus(8, 0), ref interest, ref price),
                "UpdateScores is one call: a scope from an earlier frame is stale");
            Assert.False(scope.Active, "it was dropped");
            Assert.Equal(1, scope.StaleDropped);
            Assert.Equal(0, interest);

            scope.Exit();
            Assert.Equal(0, scope.Exited, "closing a closed scope is no event");
        }

        // ---- the wiring ----

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            foreach (string part in relative) path = Path.Combine(path, part);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void ThePatches_AreThePrefixAndFinalizerOfUpdateScores_AndThePostfixesOfTheTwoScoreFunctions()
        {
            string code = Code("Negotiation", "NegotiationPatches.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameNegotiationWindow\),\s*""UpdateScores"",\s*PatchKind\.Prefix\)\]"), "prefix on UpdateScores");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameNegotiationWindow\),\s*""UpdateScores"",\s*PatchKind\.Finalizer\)\]"), "finalizer on UpdateScores");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameAIMath\),\s*""GetProviderScores"",\s*PatchKind\.Postfix\)\]"), "postfix on GetProviderScores");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameAIMath\),\s*""GetClientScores"",\s*PatchKind\.Postfix\)\]"), "postfix on GetClientScores");
            Assert.Equal(4, Regex.Matches(code, @"\[ModPatch\(").Count, "no other patch");

            // the private fields of the window come in by Harmony's injection
            Assert.True(Regex.IsMatch(code, @"ScopeEnter\(ICompany ___currentProvider,\s*ICompany ___currentClient\)"));
            Assert.True(Regex.IsMatch(code, @"ProviderScores\(ICompany provider,\s*ref GameAIScores __result\)"));
            Assert.True(Regex.IsMatch(code, @"ClientScores\(ICompany client,\s*ref GameAIScores __result\)"));
        }

        [Test]
        public void TheFinalizer_ClosesTheScope_WithoutAsking_AndPassesTheExceptionOn()
        {
            string code = Code("Negotiation", "NegotiationPatches.cs");
            Match finalizer = Regex.Match(code, @"private static Exception ScopeExit\(Exception __exception\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);

            Assert.True(finalizer.Success, "the finalizer returns an Exception");
            string body = finalizer.Groups["body"].Value;
            Assert.True(body.Contains("NegotiationScope.Exit()"), "it closes the scope");
            Assert.True(body.Contains("return __exception;"), "pass-through: the game gets its own exception");
            Assert.False(body.Contains("CanRun"), "the scope is closed whatever the state of the feature is");
        }

        [Test]
        public void ThePostfixes_ReturnAtOnce_WhenTheScopeIsClosed_AndAreGuarded()
        {
            string code = Code("Negotiation", "NegotiationPatches.cs");

            foreach (string name in new[] { "ProviderScores", "ClientScores" })
            {
                Match method = Regex.Match(code, @"private static void " + name + @"\(.*?\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
                Assert.True(method.Success, name);
                string body = method.Groups["body"].Value;
                int closed = body.IndexOf("if (!NegotiationScope.Active) return;", StringComparison.Ordinal);
                int guard = body.IndexOf("CanRun", StringComparison.Ordinal);
                Assert.True(closed >= 0 && closed < guard, name + ": the AI-AI deals leave before anything else is done");
                Assert.True(body.Contains("ModHooks.ReportFault"), name + ": a failure is counted by the core");
            }
        }

        [Test]
        public void TheFeature_DeclaresThePatches_AndTheScopeReadsTheCompaniesOfTheWindow()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\.Patch\(typeof\(NegotiationPatches\)\);"), "declared with ctx.Patch: the self-check of the core covers the targets");

            string scope = Code("Negotiation", "NegotiationScope.cs");
            Assert.True(scope.Contains("ModCompanies.IsPlayer("), "the player is told by the core");
            Assert.True(scope.Contains("Time.frameCount"), "the stamp that tells a stale scope");
            Assert.True(scope.Contains("Book.IsDormant("), "a rival that is put aside gets no bonus");
            Assert.False(Regex.IsMatch(scope, @"\.Change\("), "reading only: the negotiation never changes the attitude");
        }
    }
}
