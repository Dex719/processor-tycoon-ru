using System.Collections.Generic;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-5 items 2 and 6 on the table of the permanent perks (TSK-010): <see cref="VanillaModifierTable"/> against an independent transcription of
    /// <c>Modifiers.GetModifier</c> of the game, for all seven types and every combination of the five facts about a company (IDM, foundry, fabless,
    /// TCMS, the rival that lacks patents), the constants of the game one by one, and the reasons that go with them.
    /// </summary>
    [Covers(typeof(VanillaModifierTable), typeof(CompanyFacts))]
    public class VanillaModifierTableTests
    {
        private static CompanyFacts Facts(bool idm = false, bool foundry = false, bool fabless = false, bool tcms = false, bool unlucky = false)
        {
            return new CompanyFacts(idm, foundry, fabless, tcms, unlucky);
        }

        // The game's switch, line for line from the decompiled Modifiers.GetModifier (the gotos turned into ifs): the order of the float operations
        // is the game's, so a comparison with == is meant.
        private static float Game(int type, CompanyFacts f)
        {
            float v = 1.0f;
            if (type == 0)
            {
                if (f.IsIdm) v = 1.05f;
            }
            else if (type == 1)
            {
                if (f.IsFoundry) v = 0.5f;
            }
            else if (type == 2)
            {
                if (f.IsFoundry) v = 0.8f;
                if (f.IsTcms) v = v - 0.1f;
            }
            else if (type == 3)
            {
                if (f.IsFabless) v = 0.67f;
            }
            else if (type == 4)
            {
                if (f.IsUnlucky) v = 6.0f;
            }
            else if (type == 5)
            {
                if (f.IsTcms) v = 0.67f;
            }
            else if (type == 6)
            {
                if (f.IsTcms) v = 0.25f;
            }
            return v;
        }

        // ---- against the game ----

        [Test]
        public void SevenTypes_AgainstEveryCombinationOfFlags_MatchTheGame()
        {
            int compared = 0;
            for (int type = 0; type < VanillaModifierTable.TypeCount; type++)
            {
                for (int mask = 0; mask < 32; mask++)
                {
                    CompanyFacts f = Facts((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);
                    Assert.Equal(Game(type, f), VanillaModifierTable.Get(type, f), "type " + type + ", flags " + mask);
                    compared++;
                }
            }
            Assert.Equal(7 * 32, compared);
        }

        [Test]
        public void TheConstants_AreTheOnesOfTheGame()
        {
            // The values the spec names (design: Component 7) one by one.
            Assert.Equal(1.05f, VanillaModifierTable.Get(0, Facts(idm: true)));
            Assert.Equal(0.5f, VanillaModifierTable.Get(1, Facts(foundry: true)));
            Assert.Equal(0.8f, VanillaModifierTable.Get(2, Facts(foundry: true)));
            Assert.Equal(0.67f, VanillaModifierTable.Get(3, Facts(fabless: true)));
            Assert.Equal(6.0f, VanillaModifierTable.Get(4, Facts(unlucky: true)));
            Assert.Equal(0.67f, VanillaModifierTable.Get(5, Facts(tcms: true)));
            Assert.Equal(0.25f, VanillaModifierTable.Get(6, Facts(tcms: true)));
        }

        [Test]
        public void ConstructionCost_OfTcms_TakesTenPointsOffWhatTheFoundryLeaves()
        {
            Assert.Near(0.9, VanillaModifierTable.Get(2, Facts(tcms: true)), 1e-6);
            Assert.Near(0.7, VanillaModifierTable.Get(2, Facts(foundry: true, tcms: true)), 1e-6);
        }

        [Test]
        public void ACompanyWithNoPerks_GetsOneOnEveryType()
        {
            for (int type = 0; type < VanillaModifierTable.TypeCount; type++)
                Assert.Equal(1.0f, VanillaModifierTable.Get(type, Facts()), "type " + type);
        }

        [Test]
        public void TheFlagsOfAnotherType_DoNotLeak()
        {
            // A fabless company has the cut on development only; a foundry on upkeep and construction only.
            CompanyFacts fabless = Facts(fabless: true);
            for (int type = 0; type < VanillaModifierTable.TypeCount; type++)
            {
                if (type == 3) continue;
                Assert.Equal(1.0f, VanillaModifierTable.Get(type, fabless), "type " + type);
            }

            CompanyFacts foundry = Facts(foundry: true);
            Assert.Equal(1.0f, VanillaModifierTable.Get(0, foundry));
            Assert.Equal(1.0f, VanillaModifierTable.Get(3, foundry));
            Assert.Equal(1.0f, VanillaModifierTable.Get(5, foundry));
        }

        [Test]
        public void ATypeOutsideTheEnum_GivesOne()
        {
            Assert.Equal(1.0f, VanillaModifierTable.Get(-1, Facts(idm: true, foundry: true, fabless: true, tcms: true, unlucky: true)));
            Assert.Equal(1.0f, VanillaModifierTable.Get(7, Facts(idm: true, foundry: true, fabless: true, tcms: true, unlucky: true)));
        }

        // ---- the reasons ----

        [Test]
        public void TheReason_IsThePerkThatMakesTheValue()
        {
            Assert.Equal(VanillaModifierTable.ReasonIdm, VanillaModifierTable.ReasonKey(0, Facts(idm: true)));
            Assert.Equal(VanillaModifierTable.ReasonFoundry, VanillaModifierTable.ReasonKey(1, Facts(foundry: true)));
            Assert.Equal(VanillaModifierTable.ReasonFoundry, VanillaModifierTable.ReasonKey(2, Facts(foundry: true)));
            Assert.Equal(VanillaModifierTable.ReasonFabless, VanillaModifierTable.ReasonKey(3, Facts(fabless: true)));
            Assert.Equal(VanillaModifierTable.ReasonUnlucky, VanillaModifierTable.ReasonKey(4, Facts(unlucky: true)));
            Assert.Equal(VanillaModifierTable.ReasonTcms, VanillaModifierTable.ReasonKey(5, Facts(tcms: true)));
            Assert.Equal(VanillaModifierTable.ReasonTcms, VanillaModifierTable.ReasonKey(6, Facts(tcms: true)));
            Assert.Equal(VanillaModifierTable.ReasonTcms, VanillaModifierTable.ReasonKey(2, Facts(tcms: true)));
        }

        [Test]
        public void ThereIsNoReason_WhereTheValueIsPlainOne()
        {
            Assert.Null(VanillaModifierTable.ReasonKey(0, Facts()));
            Assert.Equal(0, VanillaModifierTable.Reasons(3, Facts(idm: true)).Length, "IDM says nothing about development");
            Assert.Null(VanillaModifierTable.ReasonKey(9, Facts(idm: true)));
        }

        [Test]
        public void ConstructionCost_OfAFoundryThatIsTcms_HasBothReasons_InTheOrderTheyApply()
        {
            string[] reasons = VanillaModifierTable.Reasons(2, Facts(foundry: true, tcms: true));

            Assert.Equal(new[] { VanillaModifierTable.ReasonFoundry, VanillaModifierTable.ReasonTcms }, reasons);
        }

        [Test]
        public void EveryReasonKey_ExistsInBothLanguages()
        {
            var keys = new HashSet<string>();
            for (int type = 0; type < VanillaModifierTable.TypeCount; type++)
            {
                foreach (string key in VanillaModifierTable.Reasons(type, Facts(true, true, true, true, true))) keys.Add(key);
            }
            Assert.Equal(5, keys.Count, "five reasons in all");

            foreach (string language in new[] { "ru", "en" })
            {
                var words = UxTestKit.Words(language);
                foreach (string key in keys)
                {
                    string text = words(key, new object[0]);
                    Assert.False(text.StartsWith("["), language + ": " + key + " has no text");
                }
            }
        }
    }
}
