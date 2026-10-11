using System;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// Which choices the player can take (FR-4 items 4-5, TSK-016/020): a false <c>requires</c> greys the button with "Нужно: ..." and the buttons follow the
    /// conditions every day (AC-4.4), a spending above the balance is a warning, not a block (AC-4.2), a spending that would break the default threshold greys the button
    /// (AC-4.6) but never the <c>defaultChoice</c>; a choice that became unavailable between two checks is replaced by the default one.
    /// </summary>
    [Covers(typeof(ChoiceAvailability), typeof(ChoiceState), typeof(ChoiceBlock), typeof(RequirementText), typeof(ChoiceWindowModel), typeof(ChoiceButtonModel))]
    public class ChoiceAvailabilityTests
    {
        private static readonly int Start = Defs.Day(1985, 6, 1);

        private static ChoiceRig RigWith(params EventDefinition[] definitions)
        {
            var rig = new ChoiceRig(definitions);
            rig.Text.With("es.t.hire", "Нанять", "Hire").With("es.t.decline", "Отказаться", "Decline").With("es.t.invest", "Вложиться", "Invest");
            rig.SetDay(Start);
            return rig;
        }

        private static ChoiceRig RigWithHire(float cost)
        {
            return RigWith(ChoiceEvents.Make("es.hire", new[] { Fx.Choice("decline"), ChoiceEvents.Costing("hire", cost) }));
        }

        private static ChoiceState State(ChoiceRig rig, EventInstanceDto instance, string choiceId)
        {
            foreach (ChoiceState state in rig.Choices.StatesOf(instance))
            {
                if (state.ChoiceId == choiceId) return state;
            }
            return null;
        }

        [Test]
        public void AC_4_2_ASpendingAboveTheBalance_IsAWarning_NotABlock_AndTheMoneyIsSpentWhenChosen()
        {
            ChoiceRig rig = RigWithHire(1250000f);
            rig.World.MoneyOf[CompanyKey.Player] = 900000f;
            rig.World.Threshold = -40000000f;

            EventInstanceDto instance = rig.FireAndDeliver("es.hire");
            ChoiceState hire = State(rig, instance, "hire");
            Assert.True(hire.Enabled, "not blocked: the game lets the balance go below zero");
            Assert.Near(1250000, hire.Cost, 1);
            Assert.Near(350000, hire.InTheRed, 1, "the balance would be -$350 000");
            Assert.Equal(0f, State(rig, instance, "decline").InTheRed);

            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            Assert.True(letter.Status.Contains("«Нанять»: уйдёте в минус на $350K"), letter.Status);
            Assert.True(letter.Buttons["hire"].Enabled);

            ChoiceWindowModel model = ChoiceWindowModel.Build(instance, rig.Rig.Catalog.Get("es.hire"), rig.Choices.StatesOf(instance), Start, rig.Text);
            Assert.Equal("Нанять — уйдёте в минус на $350K", model.Choices[1].Label, "the button of the window is signed");
            Assert.True(model.Choices[1].Enabled);
            Assert.Equal("Отказаться", model.Choices[0].Label);

            rig.ClickLetter(instance.id, "hire");
            Assert.Equal(-1250000f, rig.Rig.Game.Money[0].Value, "$1 250 000 are spent");
            Assert.Equal(CompanyKey.Player, rig.Rig.Game.Money[0].Key);
            Assert.Near(-350000, 900000 + rig.Rig.Game.Money[0].Value, 1, "the balance: -$350 000");
            Assert.Equal("hire", rig.Rig.Entries(EventLogKinds.Resolved)[0].choiceId);
        }

        [Test]
        public void AC_4_6_ASpendingThatBreaksTheDefaultThreshold_GreysTheChoice_ButNeverTheDefaultChoice()
        {
            ChoiceRig rig = RigWith(
                ChoiceEvents.Make("es.hire", new[] { Fx.Choice("decline"), ChoiceEvents.Costing("hire", 60000000f) }),
                ChoiceEvents.Make("es.big", new[] { ChoiceEvents.Costing("pay", 60000000f), ChoiceEvents.Costing("other", 60000000f) }));
            rig.World.MoneyOf[CompanyKey.Player] = 5000000f;
            rig.World.Threshold = -40000000f;

            EventInstanceDto hireInstance = rig.FireAndDeliver("es.hire");
            ChoiceState hire = State(rig, hireInstance, "hire");
            Assert.False(hire.Enabled, "the balance would be -$55 000 000, below -$40 000 000");
            Assert.Equal(ChoiceBlock.CreditLimit, hire.Block);
            Assert.Near(15000000, hire.ShortBy, 1);
            Assert.Equal("es.reason.credit_limit", hire.ReasonKey);
            Assert.Equal("Не хватит кредитного лимита на $15M", rig.Text.L(hire.ReasonKey, hire.ReasonArgs));
            Assert.True(State(rig, hireInstance, "decline").Enabled);

            FakeMail.Letter letter = rig.Mail.OfInstance(hireInstance.id);
            Assert.False(letter.Buttons["hire"].Enabled, "the button of the letter is grey");
            Assert.Equal("es.reason.credit_limit", letter.Buttons["hire"].ReasonKey);
            Assert.True(letter.Buttons["decline"].Enabled);

            // the default choice costs 60M too: it is warned about, not blocked
            EventInstanceDto bigInstance = rig.FireAndDeliver("es.big");
            ChoiceState pay = State(rig, bigInstance, "pay");
            Assert.True(pay.Enabled, "defaultChoice is not blocked by money");
            Assert.Near(55000000, pay.InTheRed, 1);
            Assert.False(State(rig, bigInstance, "other").Enabled, "the other one is blocked");

            // a free choice is never blocked, whatever the balance
            rig.World.MoneyOf[CompanyKey.Player] = -90000000f;
            Assert.True(State(rig, hireInstance, "decline").Enabled);
            Assert.Equal(0f, State(rig, hireInstance, "decline").InTheRed);
        }

        [Test]
        public void AC_4_4_AChoiceWithAFalseRequirement_IsGrey_WithTheReason_AndWakesUpTheNextDay_TheLetterStaysOpen()
        {
            bool researching = false;
            EventChoice invest = Fx.Choice("invest", Fx.Effect("Reputation", "{ 'delta': 2 }"));
            invest.requires = ChoiceEvents.Flag("researchActive", () => researching);
            ChoiceRig rig = RigWith(ChoiceEvents.Make("es.lab", new[] { Fx.Choice("decline"), invest }));

            EventInstanceDto instance = rig.FireAndDeliver("es.lab");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            Assert.False(letter.Buttons["invest"].Enabled, "the button of the letter is grey");
            Assert.Equal("es.reason.requires", letter.Buttons["invest"].ReasonKey);
            Assert.Equal("Нужно: текущее исследование", rig.Text.L(letter.Buttons["invest"].ReasonKey, letter.Buttons["invest"].ReasonArgs));
            Assert.Equal(ChoiceBlock.Requirement, State(rig, instance, "invest").Block);

            ChoiceWindowModel model = ChoiceWindowModel.Build(instance, rig.Catalog.Get("es.lab"), rig.Choices.StatesOf(instance), Start, rig.Text);
            Assert.False(model.Choices[1].Enabled, "the window shows it grey");
            Assert.Equal("Нужно: текущее исследование", model.Choices[1].Reason);

            // the player starts a research; the next day the button is active, the letter was never closed
            researching = true;
            int refreshes = letter.Refreshes;
            rig.Day(Start + 1);
            Assert.True(letter.Buttons["invest"].Enabled, "active on the next day");
            Assert.True(letter.Refreshes > refreshes, "the letter was drawn again");
            Assert.Null(letter.Answer);
            Assert.NotNull(rig.Rig.Instance("es.lab"));
        }

        [Test]
        public void AChoiceThatBecameUnavailableBetweenTwoChecks_IsReplacedByTheDefaultChoice_WithANote()
        {
            bool researching = true;
            EventChoice invest = Fx.Choice("invest", Fx.Effect("Reputation", "{ 'delta': 2 }"));
            invest.requires = ChoiceEvents.Flag("researchActive", () => researching);
            ChoiceRig rig = RigWith(ChoiceEvents.Make("es.lab", new[] { Fx.Choice("decline", Fx.Effect("Reputation", "{ 'delta': 1 }")), invest }));

            EventInstanceDto instance = rig.FireAndDeliver("es.lab");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            Assert.True(letter.Buttons["invest"].Enabled);

            researching = false;   // the research ended after the last daily check; the player presses the button of the letter
            rig.ClickLetter(instance.id, "invest");

            EventLogEntry entry = rig.Rig.Entries(EventLogKinds.AutoResolved)[0];
            Assert.Equal("decline", entry.choiceId, "the default choice is applied");
            Assert.Equal(1f, rig.Rig.Game.Reputation[CompanyKey.Player], "its effects, not the effects of the pressed choice");
            Assert.Equal(1, rig.Notify.CountContaining("Вариант стал недоступен: Вложиться"), string.Join("; ", rig.Notify.Shown));
            Assert.Equal(0, rig.Notify.CountContaining("Выбрано автоматически"), "one note, not two");
            Assert.Equal(ChoiceOutcome.Ignored, rig.Choices.Answer(instance.id, "invest", Start).Outcome);
        }

        [Test]
        public void AChoiceThatBecameUnaffordableBetweenTwoChecks_IsReplacedByTheDefaultChoice_Too()
        {
            ChoiceRig rig = RigWithHire(30000000f);
            rig.World.MoneyOf[CompanyKey.Player] = 50000000f;
            EventInstanceDto instance = rig.FireAndDeliver("es.hire");
            Assert.True(State(rig, instance, "hire").Enabled);

            rig.World.MoneyOf[CompanyKey.Player] = -20000000f;   // the money went away: -50M would break -40M
            ChoiceAnswer answer = rig.Choices.Answer(instance.id, "hire", Start);
            Assert.Equal(ChoiceOutcome.FellBack, answer.Outcome);
            Assert.Equal("hire", answer.Requested);
            Assert.Equal("decline", answer.Applied);
        }

        [Test]
        public void TheStateOfAChoice_IsReadFromTheSavedNumbers_AndAnUnknownChoiceHasNone()
        {
            ChoiceRig rig = RigWithHire(1000000f);
            EventInstanceDto instance = rig.FireAndDeliver("es.hire");
            Assert.Null(rig.Choices.Availability.StateOf(instance, rig.Catalog.Get("es.hire"), "nope"));
            Assert.Equal(2, rig.Choices.Availability.Evaluate(instance, rig.Catalog.Get("es.hire")).Count);
            Assert.Equal(0, rig.Choices.Availability.Evaluate(null, null).Count);
            Assert.True(rig.Choices.Availability.StateOf(instance, null, "hire").Enabled, "an event no longer in the registry: nothing to check");
        }

        [Test]
        public void TheWordsOfARequirement_NameTheFirstUnmetPart_TheAlternatives_OrFallBackToTheDescription()
        {
            var text = new TableText();
            var requirements = new RequirementText(text.Find);
            Func<ICondition, bool> evaluate = c => c.Evaluate(new ConditionContext(null, new DateTime(1985, 6, 1)));

            ICondition met = new DelegateCondition("moneyAtLeast(amount=5)", ctx => true);
            ICondition research = new DelegateCondition("researchActive", ctx => false);
            ICondition unknown = new DelegateCondition("somethingNew(x=1)", ctx => false);

            Assert.Equal("текущее исследование", requirements.Describe(research, evaluate));
            Assert.Equal("текущее исследование", requirements.Describe(new AllCondition(new[] { met, research }), evaluate), "the first part that is not met");
            Assert.Equal("somethingNew(x=1)", requirements.Describe(unknown, evaluate), "no words for it: the description of the core");
            Assert.Equal("текущее исследование / somethingNew(x=1)", requirements.Describe(new AnyCondition(new[] { research, unknown }), evaluate));
            Assert.Equal(string.Empty, requirements.Describe(null, evaluate));

            text.Language = "en";
            Assert.Equal("a research in progress", requirements.Describe(research, evaluate));
            Assert.Equal("x", new RequirementText(key => { throw new InvalidOperationException("broken"); }).Describe(new DelegateCondition("x", ctx => false), evaluate),
                "a lookup that fails falls back to the description");
        }
    }
}
