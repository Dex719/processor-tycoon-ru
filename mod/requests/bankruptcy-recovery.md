# Запросы bankruptcy-recovery

Публичный API фичи лежит в `mod/src/PTMod/Features/BankruptcyRecovery/Api/` и собирается вместе с зависимыми фичами (`rival-acquisitions`
берёт только папку `Api`). Поэтому всё, что видят потребители, самодостаточно внутри `Api` и не ссылается на `Logic`, `Services` и т. д.

## 1. `JournalEntryData` лежит в `Api/Contracts/`, а не в `Logic/` (для интегратора и агентов, пишущих `Logic`)

- `CapitalLedger.Journal` возвращает `IReadOnlyList<JournalEntryData>`, значит тип должен быть виден сборке зависимой фичи (`rival-acquisitions`: только
  `Core` + `Api`). Файл: `Api/Contracts/JournalEntryData.cs`, namespace остался как в design: `PTMod.Features.BankruptcyRecovery.Logic`. Файл чистый:
  без `PTMod.Core`, `UnityEngine`, `Assembly-CSharp`.
- Тесты (`build.py --tests`, `build_feature.py --tests`) собирают только `Features/<Pascal>/Logic/**`. `RecoveryState.journal`
  (`Logic/Model/RecoveryState.cs`) ссылается на `JournalEntryData`, поэтому в сборке тестов типа без правки не будет.
- **Нужная правка (та же, что просит `mod/requests/rival-acquisitions.md`, п. 1):** добавить `src/PTMod/Features/<Pascal>/Api/Contracts/` в обе сборки тестов
  (`build.py: build_tests` для каждой фичи с такой папкой и `build_feature.py: build_feature_tests`). Корневая папка `Api/` в тесты не входит: `RecoveryApi` и
  `CapitalLedger` используют `ModHooks`, `ModFeatures`, `ModLog` и типы игры. После правки в `Contracts/` окажется один публичный тип
  (`JournalEntryData`), его нужно перечислить в `[Covers]` (тест сериализации `RecoveryState`).
- **Обход, пока правки нет** (без изменения сборочных скриптов): в `Logic/Model/RecoveryState.cs` добавить заглушку только для сборки тестов
  (в сборке мода и в `build_feature.py <slug>` символа `PTMOD_TESTS` нет, там работает настоящий тип из `Api/Contracts`):

  ```csharp
  #if PTMOD_TESTS
  namespace PTMod.Features.BankruptcyRecovery.Logic
  {
      [System.Serializable]
      public sealed class JournalEntryData
      {
          public string date = ""; public string op = ""; public double amount; public string details = "";
      }
  }
  #endif
  ```

  Поля заглушки должны совпадать с `Api/Contracts/JournalEntryData.cs`. Когда правка интегратора попадёт в скрипты, заглушку удалить (иначе CS0101 в сборке тестов).

  **Статус (кусок #1, 2026-10-10):** обход применён — заглушка стоит в конце `Logic/Model/RecoveryState.cs` под `#if PTMOD_TESTS`; `JournalEntryData`
  перечислен в `[Covers]` теста `RecoverySaveSectionTests`. Сборка мода (`build_feature.py bankruptcy-recovery`, `build.py` без тестов) использует настоящий тип.
  Если интегратор добавит `Api/Contracts/` в сборки тестов, заглушку надо удалить (иначе CS0101 в сборке тестов).

## 2. Регистрация реализаций (для агентов, пишущих реализацию)

API сделан как статические фасады с null-object по умолчанию; реализацию подключает сама фича:

- `RecoveryApi.SetService(IRecoveryService)` — `internal`, вызвать один раз в `BankruptcyRecoveryFeature.Register` (реализация читает `RecoveryState`
  и сервисы). Пока не вызвано, а также пока фича не `Enabled`, `RecoveryApi` отвечает пустыми значениями.
- `CapitalLedger.SetBackend(ICapitalLedgerBackend)` — `internal`, вызвать **до** любых проверок состояния фичи, и не снимать в `OnDisabled`
  (**уточнение, п. 4:** не в `Register`, который у выключенной фичи не вызывается, а в конструкторе `BankruptcyRecoveryFeature`):
  `CapitalLedger` по спеке работает при выключенной фиче (на нём держится оплата в `rival-acquisitions`). Без регистрации операции только пишут
  предупреждение в `ModLog` и ничего не двигают (`CapitalLedger.IsAvailable == false`).
- События `RecoveryApi.LoanOpened/LoanClosed/BailoutAccepted` поднимаются фичей через `RecoveryApi.RaiseLoanOpened/RaiseLoanClosed/RaiseBailoutAccepted`
  (`internal`, обёртка над `ModHooks.Raise`).
- Фасад `CapitalLedger` сам отклоняет пустой `featureId` и сумму `<= 0`, `NaN`, `∞` (предупреждение в `ModLog`) и ловит исключения бэкенда
  (`ModHooks.ReportFault("bankruptcy-recovery", ...)`); бэкенд может на это опираться и не повторять проверки. Бэкенд отвечает за `Company.AddMoney` /
  `SpendMoney` / `EarnMoney` игрока, `(float)`-приведение, журнал (только для `featureId == "bankruptcy-recovery"`, кольцо на `CapitalLedger.JournalCapacity = 200`)
  и строку `ModLog` для остальных фич.

## 3. Результаты спайка TSK-003 (для интегратора: перенести в `design.md`, раздел «Результаты спайка»)

Раздел `design.md` вне файлов агента этого куска, поэтому текст лежит здесь готовым к переносу. Спайк выполнен **статически** (игру запускать нельзя): декомпилят
`pt-modding\code`, сериализованные значения сцены `pt-modding\data`, `CODEMAP.md` и исходники ядра (`Core\Hooks`, `Core\Credit`). Рантайм-часть остаётся владельцу
(см. «Не проверено»).

### Результаты спайка (2026-10-10, статический разбор)

**Приватные цели `BankruptcyHandler` — найдены.**

- `BankruptcyHandler : MonoBehaviour`. `TickBankruptcies()`, `TriggerDefault(ICompany)`, `CancelDefault(ICompany)`, `Bankrupt(ICompany)`, `UpdateUI()` — `private`,
  экземплярные, по одной перегрузке; `Instance` — `public static`.
- `defaultTimeLimit` — `[SerializeField] private int`; значение сцены `level1` = **120** (`data\ProcessorTycoon.Bank.BankruptcyHandler.json`).
- Значит `AccessTools.Method(typeof(BankruptcyHandler), "CancelDefault", new[] { typeof(ProcessorTycoon.CompanySystem.ICompany) })` и
  `AccessTools.Field(typeof(BankruptcyHandler), "defaultTimeLimit")` находят цели; вызывать нужно на `BankruptcyHandler.Instance`.
- `CancelDefault(игрок)`: `DaysInDefault = 0` (`IsInDefault` это `DaysInDefault >= 1`), прячет `bankruptcyEvent` (баннер «Bankruptcy in N days»), включает
  `bankruptcySuccess` на 10 с, звук 5. Для ИИ — только `DaysInDefault = 0`. Баннер дефолта, таким образом, снимается самим методом.
- `TickBankruptcies` подписан на `DateController.OnDayPassed` (лямбда `<Start>b__20_0`) и зовёт `this.TriggerDefault`, `this.Bankrupt`, `this.CancelDefault` — патч P3
  (postfix `TriggerDefault`) сработает. Для P1: prefix ставит `DaysInDefault = defaultTimeLimit − 2`, метод увеличит до `T − 1`, проверка `DaysInDefault < T` проходит, ветка
  банкротства пропущена; `UpdateUI` покажет «1 день».
- Порядок дня (CODEMAP, `DateController.TriggerTicks`): `OnTick` (`CentralBank.UpdateCreditLimit`) → `OnLateTick` (`ctx.OnTick(LateTick)`, автозаём) → `OnDayPassed`
  (`TickBankruptcies`). Автозаём успевает поднять баланс до проверки дефолта, а `DebtLimit` к этому моменту свежий. `OnLateMonthPassed` — в день 1, после `OnMonthPassed`.

**Совместимость prefix и postfix с transpiler'ом P10 ядра — `ModHooks` их примет.**

- В `ReservedTargets` P10 и P11 заведены как `Channel(..., PatchKind.Transpiler)`: `Kinds = { Transpiler }`. `ReservedTarget.Rejects("bankruptcy-recovery", Prefix | Postfix)` даёт
  `false` (`Covers` ложно); правило «второй transpiler на методе» (`ModHooks.OtherTranspiler`) считается только для `PatchKind.Transpiler`.
- Harmony оборачивает оригинал prefix'ом и postfix'ом, не меняя его IL, поэтому шаблон P10 (локальная `V_0` пишется один раз после `get_DebtLimit; neg`, читается трижды в
  цикле) не затрагивается. Патчи ядра и фичи живут в разных экземплярах Harmony (`ptmod.mod-framework`, `ptmod.bankruptcy-recovery`).
- Порог читается через `ModCredit.ThresholdOf(игрок)` (учитывает P9/P10), `CentralBank.DebtLimit` напрямую не читается.

**Реальный `DebtLimit` — не застревает на 1 000 000; `limits.floorBase` оставлен 2 000 000.**

- `CentralBank`: сериализованное значение автосвойства `<DebtLimit>k__BackingField` в сцене = **10 000 000**; `Awake` копирует его в `baseCreditLimit`, дальше
  `UpdateCreditLimit()` каждый день: `DebtLimit = max(1e6, baseCreditLimit × I × (I × (1,5 + clamp(год − 1971, 0, 60) / 25)) − 17,5e6)`.
- `I = InflationHandler.Inflation` — линейная интерполяция между десятилетиями (1970-е 0,5; 1980-е 2,0; 1990-е 3,0; 2000-е 5,0; 2010-е 6,5; 2020-е 7,5; 2030-е 8,5) по доле
  десятилетия, прошедшей с 1 января (`DateHelper.DecadeTransitionValue`).
- Расчёт на 1 января (множители `bank.creditLimit` = 1, т. е. `ModCredit.PlayerDebtLimit()` = `DebtLimit`). Годы старта новой игры — 1975, 1980, …, 2000 (`MenuGameSetup`):

  | Дата | `I` | `D` = `DebtLimit` | `floorBase × I` (2 млн) | `D_L = max(D, floorBase × I)` |
  |---|---|---|---|---|
  | 1971-01-01 | 0,649 | 1 000 000 (пол банка) | 1 298 356 | 1 298 356 |
  | 1974-01-01 | 1,100 | 2 087 356 | 2 199 178 | 2 199 178 |
  | 1975-01-01 | 1,250 | 8 420 448 | 2 499 178 | 8 420 448 |
  | 1980-01-01 | 2,000 | 56 900 000 | 4 000 000 | 56 900 000 |
  | 1985-01-01 | 2,500 | 111 221 782 | 4 999 452 | 111 221 782 |
  | 1990-01-01 | 3,000 | 185 900 000 | 6 000 000 | 185 900 000 |
  | 1995-01-01 | 4,000 | 375 992 172 | 7 998 904 | 375 992 172 |
  | 2000-01-01 | 5,000 | 647 500 000 | 10 000 000 | 647 500 000 |

  (В design для 1985 стоит «111,3 млн»; точное значение 111,22 млн — расхождение в округлении, на формулы не влияет.) Пол `limits.floorBase` действует только в 1971–1974 годах
  и при очень сильном множителе `bank.creditLimit` < 1 (испытание «Жёсткий кредит»); для стартов 1975+ он не срабатывает. **Решение: `floorBase = 2 000 000` без изменений
  в `balance.json`.** Если `baseCreditLimit` в игре окажется 0 (значение сцены не восстановилось), `DebtLimit` = 1 000 000 всю игру, и пол держит лимиты (1980-е: 4 млн,
  1990-е: 6 млн) — запасной сценарий design работает как описано.

**`BankruptcyWarningWindow`.**

- `Open()` — `public`; ставит `gameObject.SetActive(true)`, `hasShownWarningToThePlayer = true`, звук 4; при повторном вызове выходит сразу (флаг после первого показа не
  сбрасывается, как и сказано в design; при загрузке сейва в дефолте флаг ставится сразу).
- На объекте `BankruptcyWarningWindow` сцены `level1` есть `PauseTrigger`; `OnDisable` после первой инициализации зовёт `DateController.ResumeAndOverride(0)` (пауза). Закрытие
  окна оставляет игру на паузе; порядок пауз при открытии нашего меню из этого окна проверяется в игре (сценарий M2).
- Для кнопки есть готовый путь: `ModUI.Button(Transform parent, string text, Action onClick, ...)` — клон `Widget.Button` в любой родитель.

**`ExpenseOrigin`** содержит `Royalties` (2) и `Interest` (6), так что `expenseOrigin` из `balance.json` разбирается через `Enum.Parse` без таблицы.

**Не проверено (нужен запуск игры — владелец/интегратор):**

1. Строки `Player.log` со значениями `CentralBank.DebtLimit` и `ModCredit.PlayerDebtLimit()` на 1975-01-01, 1980-01-01, 1985-01-01 новой игры (цифры выше — расчёт по формуле
   и данным сцены, подтвердить в игре).
2. Фактическое применение prefix и postfix вместе с P10 (`ModHooks.Report` по `BankruptcyHandler.TickBankruptcies` — это сделает самопроверка TSK-010/TSK-018; спайк 0002
   `mod/spikes/harmonyspike` ставит на тот же метод пустой transpiler).
3. Вызов `CancelDefault(игрок)` в живой игре: исчезает ли баннер, появляется ли «успех» (по коду — да).
4. Клон `ButtonUI` в `BankruptcyWarningWindow`: раскладка окна в коде неизвестна (дерево сцены), положение и размер кнопки подбираются в игре (TSK-010/TSK-015, сценарий M2).

Код `Debug/SpikeProbe.cs` не создан намеренно: пункты 1-4 закрываются без отдельного пробника (п. 1 — строкой `ModLog.Info` в самопроверке TSK-010 или отладочной
строкой в хабе TSK-019; п. 2-3 — самопроверкой и сценарием M2; п. 4 — реализацией P4 в TSK-010). Временный код, который нельзя запустить, остался бы в репозитории без пользы.

## 4. Для TSK-007 и TSK-022: `Register` вызывается только у включённых фич (расхождение с допущением design)

В `ModFeatures.Activate` вызов `info.Feature.Register(...)` стоит за проверкой `_machine.IsActive(info.Id)`: у фичи, выключенной игроком в `ptmod_settings.json`, `Register` не
вызывается вовсе (документация `CoreInitStage.AfterRegister`: «Every enabled feature has run Register»). Класс `[ModFeature]` при этом создаётся при обнаружении для **каждой**
фичи (`Activator.CreateInstance`, `ModFeatures.cs`), то есть конструктор отрабатывает всегда. Следствия:

- `CapitalLedger.SetBackend(...)` (п. 2 выше) нельзя оставлять в `Register`: при выключенной `bankruptcy-recovery` `CapitalLedger` остался бы без бэкенда, а по design он обязан
  работать при выключенной фиче (на нём оплата в `rival-acquisitions`). Регистрировать бэкенд нужно в конструкторе `BankruptcyRecoveryFeature` (или в статическом
  инициализаторе). Бэкенд не должен обращаться к состоянию, которого ещё нет (игрок, `Instance`), в момент регистрации.
- Допущение design (раздел «BankruptcyRecoveryFeature», примечание про `ModTicks.GameReady`) «ядро вызывает `Register` для всех найденных фич и читает секции всех участников,
  в том числе выключенных» **не выполняется**: у выключенной фичи нет ни подписки на `GameReady`, ни зарегистрированного участника сохранения, секция остаётся в боковом файле как
  есть (passthrough). Перевод обязательств в обычный долг при загрузке (FR-12 п. 5, AC-12.5) из `Register` не получится. Варианты для TSK-022: (а) подписаться на
  `ModTicks.GameReady` в конструкторе фичи и читать нужную секцию самостоятельно (нужен доступ к тексту секции — в `ModSave` его сейчас нет; запрос к ядру: отдавать сырую секцию
  неактивной фичи, например `ModSave.GetRawSection(featureId)`); (б) зафиксировать ограничение в README, как позволяет Acceptance TSK-022 («или зафиксировано как ограничение в
  README»): выключать фичу нужно только в хабе, где перевод в долг работает.
  **Статус (кусок #5):** выбран вариант (б); почему (а) невозможен даже с `GetRawSection`, и что ещё делает ядро при повторном включении фичи — п. 9.2 и 9.3.
- `RecoverySaveParticipant` и подписки на тики остаются в `Register` (они нужны только у включённой фичи).

## 5. Что сделано в куске #1 (TSK-001, TSK-002; TSK-003 — частично, см. п. 3)

- Каркас: `BankruptcyRecoveryFeature` (`[ModFeature]`), `RecoverySettings` (5 переключателей `ParamSpec`), `RecoveryContent` (загрузка `balance.json`), `RecoveryService` (заглушка
  `IRecoveryService` по состоянию), `RecoverySaveParticipant` (`IModSaveParticipant`).
- Чистая логика (`Logic/Model`): `RecoveryState` и вложенные DTO, `RecoveryStateCodec` (запись/чтение/проверка секции), `RecoverySaveSection` (участник для `SideFileModel`),
  `RecoveryBalance` (+ `Validate`, `FromJson`), `RecoveryParams` и `SubFeature`, `RecoveryDates` (internal).
- Чего в этом ещё нет (следующие задачи): `IPlayerFinance`/`PlayerFinanceAdapter` и бэкенд `CapitalLedger` (TSK-007), сервисы и тики (TSK-008, TSK-009), патчи и самопроверка (TSK-010),
  меню и хаб (TSK-013-015), строки окон и писем (TSK-016; в L10n уже лежат только `rec.feature.*`, `rec.hub.title`, `rec.param.*`).

## 6. Что сделано в куске #2 (TSK-004, TSK-005, TSK-006: чистая логика) и что нужно от интегратора

Файлы: `Logic/LoanMath.cs`, `Logic/CreditLineMath.cs`, `Logic/BailoutMath.cs`, `Logic/RecoveryRules.cs`, `Logic/DisableSettlement.cs`, `Logic/Model/BankruptcyView.cs` (`BankruptcyView`,
`PreWarningDecision`, `Eligibility`); тесты `LoanMathTests`, `CreditLineMathTests`, `BailoutMathTests`, `RecoveryRulesTests`, `DisableSettlementTests` (143 теста).

### 6.1. Архитектурный тест `CreditMathTests.Sources_OnlyTheCreditClasses_ReadCentralBankDebtLimit` слишком широк (запрос к ядру)

Регулярное выражение `\.\s*DebtLimit\b` (`mod/tests/PTMod.Tests/Credit/CreditMathTests.cs`) ловит любое обращение к члену с именем `DebtLimit`, а не только чтение
`CentralBank.DebtLimit`. Под него попадают законные имена: `BankruptcyView.DebtLimit` и `IPlayerFinance.DebtLimit` (так названо в design, и это порог из `ModCredit`),
`ValuationMath.cs:65` в `rival-acquisitions` (сейчас единственная красная строка этого теста в сборке `bankruptcy-recovery`). Что сделано у нас: в `Logic` нигде нет текста
`.DebtLimit` (поле читается только внутри `BankruptcyView` и через его `IsBelowShareOfLimit` / `IsAtOrAboveShareOfLimit`), тест от нашей `Logic` зелёный.
Для TSK-007 (`IPlayerFinance.DebtLimit` по design) такой обход не работает: либо сузить регулярное выражение до чтения самого банка (например,
`CentralBank\s*\.\s*(Instance\s*\.\s*)?DebtLimit\b` либо `\bCentralBank\b[^;]*\.DebtLimit\b`), либо переименовать свойство адаптера. Предпочтителен первый вариант; правка теста
вне файлов этой фичи.

**Статус (кусок #3):** решено переименованием у нас: свойство адаптера называется `PlayerDebtLimit`, правка теста ядра не нужна (п. 7.2). Красная строка `ValuationMath.cs:65`
в `rival-acquisitions` в последнем прогоне уже не встречается.

### 6.2. Отличия от сигнатур design (расширения, ничего не убрано)

- `BankruptcyView` получил поле `IsPlayer` (конструктор: `money, debtLimit, inDefault, isBankrupt, daysInDefault, defaultTimeLimit, today, isPlayer`; значения по умолчанию нет,
  чтобы чужую компанию нельзя было принять за игрока по недосмотру) и члены `Threshold`, `HasThreshold`, `IsBelowThreshold`, `IsBelowShareOfLimit(share)`,
  `IsAtOrAboveShareOfLimit(share)`. Без положительного конечного `DebtLimit` порога нет, и правила молчат (нет вмешательства, письма, удержания).
- `Eligibility` получил `CooldownDaysLeft` (аргумент `{0}` строки причины «кулдаун») и фабрики `Yes()` / `No(key)`; ключи причин — константы `RecoveryRules.Reason*`:
  `rec.bridge.unavailable.active` (был в design), `.cooldown`, `.notNeeded`, `.bankrupt` (новые; строки добавлены в `L10n/ru` и `L10n/en`, design нужно дополнить таблицу ключей).
- `LoanStep` получил свойство `Total`; `LoanMath` — `EffectiveRate(rate, multiplier)` (ставка × канал `bank.interest`, мусор даёт 0).
- `CreditLineMath` — `Available`, `ClosesOn(c, foundation)`, `HasExpired(closesOn, today)`; `BailoutMath` — `InvestorCap(c, amount)`; `RecoveryRules` — `IsStreakComplete(streak, c)`;
  `DisableSettlement` — `BalanceAfter(money, total)`, `WouldDefault(money, total, debtLimit)` (для текста подтверждения в `DisableSettlementService.BuildConfirmation`).
- `DisableSettlement.Total` учитывает договор инвестора только при `active = true`; остаток `max(0, cap − paid)`.

### 6.3. Что важно знать авторам TSK-007 ... TSK-011

- Вся логика отдаёт `0` (или `false` / `None`) на мусорный вход (`NaN`, `∞`, отрицательные суммы) вместо исключения: сервис должен проверить `payment > 0` / `amount > 0` перед операцией.
- Проценты и шаг считать от `LoanMath.EffectiveRate(loan.annualRate, M)`; `payment` хранится в `LoanData.payment` с момента открытия, а если канал `bank.interest` вырос позже,
  `MonthStep` вернёт `Principal = 0` на месяц (долг не растёт), последний платёж (`monthsLeft <= 1`) гасит весь остаток.
- Окно линии: `CreditLineMath.ClosesOn(cfg, foundationDate)` даёт `closesOn` для секции состояния (`RecoveryDates.ToText`), `HasExpired(closesOn, today)` — условие «сегодня ≥ closesOn».
- `RecoveryRules.MustHoldForBailout` не знает про подфичи: вызывающий код (P1) сам не зовёт правило, если `Bailout` выключена; лимит спасений берёт из `BailoutMath.Allowed(cfg, сложность)`.

## 7. Что сделано в куске #3 (TSK-007 ... TSK-012) и что нужно следующим задачам и интегратору

### 7.1. Состав

- Чистая логика (`Logic/`, тесты на фейках; сервисы игры — тонкие обёртки над ней): `IPlayerFinance` + `IRecoveryMoney`, `RecoveryJournal` (кольцо на 200), `CapitalLedgerCore`
  (правила `CapitalLedger`), `ReportMonths`, `RecoveryEnvironment` (состояние, баланс, переключатели подфич, R_3 на месяц), `LoanBook`, `CreditLineEngine`, `BridgeLoanEngine`,
  `BailoutEngine` (+ `BailoutOffer`, `IBailoutPresenter`, `BailoutChoice`), `DefaultCycleEngine` (P1/P2), `AssetMath`, `RecoveryTargets` (+ `TargetCheck`, самопроверка).
- Сторона игры: `PlayerFinanceAdapter` (`IPlayerFinance`), `CapitalLedgerBackend` + `LedgerMoney`, `Services/{LoanService, BridgeLoanService, StartupCreditLineService, BailoutService,
  AssetSaleService, RecoveryMonitor (шов), RecoveryViews}`, `Patches/{BankruptcyPatches, WarningWindowButton}`, `RecoverySelfCheck`; `BankruptcyRecoveryFeature` теперь создаёт и
  подключает всё (`Register`: патчи P1-P4, тики `LateTick` и `LateMonthPassed`, `GameReady`, `BatchEnded`, `ModSettings.Changed`, самопроверка в конце). Бэкенд `CapitalLedger`
  регистрируется в конструкторе фичи (п. 4).
- Тесты: `CapitalLedgerTests`, `LoanBookTests`, `CreditLineEngineTests`, `BridgeLoanEngineTests`, `BailoutEngineTests`, `DefaultCycleEngineTests`, `AssetMathTests`,
  `RecoveryTargetsTests`, `RecoveryLifecycleTests`, `RecoveryWiringTests` (разбор исходников: патчи, подключение, деньги только через адаптер и правила реестра, ИИ не затрагивается).
  Поддержка: `RecoveryRig.cs` (фейк `FakePlayerFinance`, `CoreMoney`, `RecoveryRig`).
- 20 членов игры из `RecoveryTargets` проверены офлайн по `_original_en/Assembly-CSharp.dll` (reflection-only, игра не запускалась): все найдены, имя параметра патча
  (`company` у `TriggerDefault`) совпадает.

### 7.2. Отличия от design (расширения; в Api ничего не менялось)

- **`IPlayerFinance.DebtLimit` назван `PlayerDebtLimit`.** Так закрыт п. 6.1 без правки теста ядра: регулярное выражение `\.\s*DebtLimit\b` не срабатывает на `finance.PlayerDebtLimit`.
  По той же причине `RecoveryService.GetSummary` собирает `RecoverySummary` инициализатором объекта (поле структуры Api называется `DebtLimit`). В `IPlayerFinance` добавлены
  `AddMoney/SpendMoney/EarnMoney` (номера enum'ов игры): на них стоит `CapitalLedgerCore`, и фейк в тестах видит движение денег.
- **Движки в `Logic/`.** Design держит логику в сервисах; здесь сервис = обёртка (события, `ModNotify`, `RecoveryApi`), правила в движке. Сигнатуры сервисов расширены:
  `LoanService.Open` возвращает `LoanView?`, `BridgeLoanService.Take` — `BridgeTakeResult`, `BailoutService.OnHoldRequested` — `bool` (патч P1 удерживает счётчик только если окно
  показано или откроется после пакета), `Prepay` — `PrepayResult`.
- **Порядок `LateMonthPassed` у линии:** проценты, затем гашение 25 % положительного баланса, затем при `today >= closesOn` остаток -> срочный кредит (порядок Acceptance TSK-009).
  В AC-7.4 («создан кредит на 3 000 000») баланс не положительный, поэтому гашения нет; при положительном балансе в месяц окончания в кредит уходит остаток после гашения.
- **Договор инвестора один.** `InvestorShareData` в design одна; второй договор при втором спасении (сложность 0) не предлагается, пока первый идёт
  (`BailoutOffer.InvestorAvailable = false`, `BailoutEngine.Accept(InvestorShare)` возвращает false). Окно TSK-015 должно сделать кнопку неактивной.
- **Номера и ключи.** Идентификаторы кредитов `bridge-N`, `term-N`, `rescue-N` с общей нумерацией (`nextLoanSeq`). Месячный платёж пишется двумя записями журнала
  `loan.interest` и `loan.principal` (в примере design — одна `loan.payment`). Прочие коды журнала: `line.open`, `line.draw`, `line.autodraw`, `line.interest`, `line.sweep`, `line.repay`,
  `line.convert` (сумма 0, в `details` — id срочного кредита), `bridge.open`, `bridge.fee`, `bailout.loan`, `bailout.investor`, `investor.payment`, `loan.prepay`, `lines.sell`,
  `stock.sell`, `cpu.retire`. Записи `disable.convert` — TSK-022.
- **P4 `Optional = true`** (это только кнопка); P1-P3 обязательные. Самопроверка (`RecoverySelfCheck`, в конце `Register`) бросает `InvalidOperationException`, если нет обязательного члена
  игры: ядро помечает фичу `Unavailable` (путь «Register failed»). У `ModSelfCheck` нет точки расширения для фич, поэтому проверка своя; строка итога `self-check: N of N game members
  found` и по строке `WARN` на каждый пропавший член.
- **Кнопка P4** ставится внизу по центру окна (раскладка окна в коде неизвестна); место и размер подбираются в игре (сценарий M2).
- **Новые строки** (ru/en): `rec.menu.open`, `rec.loan.kind.{bridge,term,rescue}`, `rec.bridge.unavailable.disabled`, `rec.credit.unavailable.{off,noLine,expired,full,bankrupt}`,
  `rec.lines.{minOne,none}`, `rec.division.{foundry,noLines,impossible}`, `rec.cpu.noStock`, `rec.reputation.bailout`, `rec.notify.{loanClosed,autoDraw,lineConverted,investorEnded}`.

### 7.3. Что нужно TSK-013 ... TSK-015 и TSK-022 (швы)

- **Окно предложения (TSK-015):** реализовать `IBailoutPresenter.Open(BailoutOffer)` (возвращает true, когда окно показано) и присвоить `BankruptcyRecoveryFeature.Instance.Bailout.Presenter` в
  `Register` фичи. **Пока presenter не задан, предложение никогда не делается и счётчик дефолта не удерживается** (ванильное банкротство). Кнопки окна вызывают
  `Bailout.Accept(BailoutKind)`, `Bailout.Decline()`, а крестик — `Bailout.Dismissed()` (не отказ). Данные окна: `Bailout.CurrentOffer()`.
- **Письма и предупреждение (TSK-015):** `Services/RecoveryMonitor.cs` — шов: `OnPlayerDefault()` вызывает патч P3 (только игрок), `OnDay()` нужно подписать на `LateTick` (строка-комментарий в
  `Register`). Флаги эпизода (`defaultMailSent`, `bailoutDeclined`, `recoveryStreak`) сбрасывает `DefaultCycleEngine.AfterTick`, когда игрок не в дефолте; `preWarningSent` — только правило
  `RecoveryRules.PreWarning` (сброс выше `-0,5·D`).
- **Меню (TSK-013/014):** `RecoveryService.OpenMenu` пока пишет строку в отладочный лог; заменить. Источники данных: `BankruptcyRecoveryFeature.Instance.{Finance, Environment, Journal, Loans,
  BridgeLoans, CreditLine, Bailout, Assets}` (все `internal`). Недоступность кнопок: `BridgeLoans.Check()` (`Eligibility.ReasonKey`, `CooldownDaysLeft`), `CreditLine.Engine.DrawBlock()`,
  `AssetMath.*` (ключи в `ReasonXxx`), `Assets.QuoteDivision().BlockReason`. Суммы форматировать `RecoveryViews.Money`. Двухшаговое подтверждение — `ModUI.Confirm` (в сервисах его нет).
- **Выключение фичи (TSK-022):** `OnDisabled` сейчас только сбрасывает сессионные флаги предложения. Для `Settle`: `DisableSettlement.Total(state.loans, line.drawn, state.investorShare)`, одна
  операция `CapitalLedger.CapitalOut(FeatureId, total, "disable.convert", ...)`, затем закрыть кредиты (`state.loans.Clear()`, `lastBridgeClosedOn` при бридж-кредите), линию
  (`state = closed`, `closedReason = disabled`, `drawn = 0`), договор (`active = false`). `CreditLineEngine.EnsureOpened` не откроет закрытую линию.

### 7.4. Запросы к интегратору / ядру

- **Тест `ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` падает на свежесобранной DLL одной фичи.** `build_feature.py <slug>` кладёт `PTMod.dll` в `mod/build/features/<slug>/`
  (там только ресурсы этой фичи), а `build_feature.py <slug> --tests` берёт её как «собранную и свежую», если она новее всех исходников; тест сравнивает ресурсы со всем деревом `Content/` и
  `L10n/` и падает (в параллельной работе DLL почти всегда «старше» чьих-то исходников, и тесты `BuiltDll_*` пропускаются). Предложение: пропускать три теста `BuiltDll_*`, когда
  `PTMOD_BUILD_DIR` лежит под `build/features/`, либо сравнивать ресурсы только с фичами, которые попали в сборку. Мои прогоны тестов шли при устаревшей DLL (3 пропуска); после проверки
  сборки DLL фичи удалена из `mod/build/features/bankruptcy-recovery/`.
- Ничего не нужно в чужих API: фича зависимостей не имеет, `rival-acquisitions` берёт только `Api/` (не менялась).

## 8. Что сделано в куске #4 (TSK-013 ... TSK-016: меню спасения, окно «Последний шанс», письма, страница хаба, строки) и что проверить в игре

### 8.1. Состав

- Чистая логика (`Logic/`, тесты на фейках; окна — тонкие обёртки над ней): `IMenuText` + `MenuTone` (слова по ключам и деньги/даты игры через интерфейс), `SummaryModel` (шесть строк
  вкладки «Сводка»), `ConfirmPlanner` + `Confirmation` (текст вопроса `ModUI.Confirm` и баланс после для каждой операции), `JournalLines` + `JournalRow` (50 последних записей, новые сверху,
  имена операций `rec.journal.op.<код>`), `AmountSteps` (доли лимита для слайдеров: шаг 5 %, бридж-кредит 25…100 %), `OfferTerms` + `OfferTexts` (тексты окна предложения),
  `RecoveryMonitorEngine` + `IRecoveryLetters` (письма «Риск дефолта» и «Компания в дефолте», флаги эпизода).
- Сторона игры: `UI/GameMenuText` (`IMenuText` на `ModL10n` и `StringFormatter`), `UI/RecoveryUi` (строки, ряды, ячейки, кнопки с причиной отказа), `UI/RecoveryWindow` (+ `.Loans`, `.Journal`; четыре
  вкладки в одном модальном окне 780x640), `UI/BailoutOfferWindow` (`IBailoutPresenter`, окно 620x620), `UI/RecoveryHubPage` (`ctx.HubPage`), `Services/RecoveryMonitor.cs`
  (`RecoveryLetters` на `ModMail` + `RecoveryMonitor`). `BankruptcyRecoveryFeature.Register`: `_bailout.Presenter = _offerWindow`, `LateTick` -> `_monitor.OnDay`,
  `ModMail.RegisterActionHandler`, `ctx.HubPage`; `RecoveryService.OpenMenu` открывает окно; `OnDisabled` закрывает окна.
- Тесты (84 новых, всего 495 у фичи): `SummaryModelTests`, `ConfirmPlannerTests`, `JournalLinesTests` (в том числе: каждый код журнала, который пишет фича, найден разбором исходников и имеет имя в ru/en),
  `AmountStepsTests`, `OfferTermsTests`, `RecoveryMonitorEngineTests` (AC-1.1…AC-1.3, письмо дефолта раз за эпизод), `RecoveryUiSeamsTests` (разбор исходников: окна модальные и из шаблонов `ModUI`, своих диалогов и
  мостов подсказок нет, каждая операция вызывается только после `Ask(plan, …)` -> `ModUI.Confirm`, крестик предложения не отказ, письма Critical с многоразовым действием и без хранения id, нет русского и
  вообще набранного в коде текста, таблицы ru/en совпадают по ключам и плейсхолдерам, **каждый вызов `L("rec.…", …)` и `ShowKey` передаёт столько аргументов, сколько плейсхолдеров в строке**).
  Поддержка: `FakeMenuText.cs`.
- Строки TSK-016: 185 ключей `rec.*` в ru и en (`install.py --check`: 0 ошибок и предупреждений). Добавлены и ключи подтверждения выключения (`rec.disable.confirm`, `rec.disable.defaultRisk`,
  `rec.disable.confirmLabel`, `rec.notify.converted` — слова из design); код, который их использует, — TSK-022.

### 8.2. Отличия от design (расширения; в Api ничего не менялось)

- **Ключи строк сводки.** В design `summary.threshold`, `summary.deficit`, `summary.daysLeft` — целые фразы («Порог дефолта: {0}»). Строки `InfoText` — пары «заголовок: значение», поэтому ключи другие:
  `rec.summary.label.{balance,threshold,deficit,headroom,bankruptcy,debt,payment}` (заголовки) и значения `rec.summary.daysLeft` («{0} дн.»), `rec.summary.notInDefault`, `rec.summary.payment`, `rec.summary.noPayment`.
  Строка «Дефицит до порога» над порогом превращается в «Запас до порога» (баланс + D). Стрелка в «20 -> 15 лин.» — ASCII: шрифт игры может не иметь «→».
- **Вопрос перед отказом от спасения.** «Отказаться — объявить банкротство» спрашивает ещё раз через `ModUI.Confirm` (банкротство заканчивает игру; «Нет» оставляет окно). В design отказ без вопроса; убрать можно, вызвав
  `Decline()` прямо из `OnDecline`.
- **Суммы на слайдерах.** Слайдер ядра показывает своё число, а не деньги, поэтому сумма выбирается долей лимита (5…100 %, шаг 5; бридж-кредит 25…100 %), а сама сумма пишется на кнопке и в условиях.
  Доля одна для «Взять» и «Погасить» по линии.
- **Досрочное погашение — целиком.** Кнопка «Погасить досрочно» гасит весь остаток (кнопка неактивна, если баланс меньше остатка, причина в подсказке). Частичное погашение умеет `LoanBook.Prepay`; в окне его нет
  (линию можно гасить частями слайдером).
- **Выбор пластины — `DropdownUI`**, как в design; всё, что от него зависит (слайдер числа линий, превью, кнопка), перерисовывается в отдельном контейнере, а не вместе с самим списком.
- **Кнопка «Продать производство»** сначала открывает ванильное `ManageLinesWindow` (`AssetSaleService.OpenManageLines`), потом закрывает меню: модальное меню лежало бы поверх окна линий.
- **Письма.** Перед отправкой письмо ищется по тегу (`ModMail.Find`): если письмо такого вида ещё в очереди (пакетный прогон, экран загрузки), второе не отправляется. Если ядро письмо не приняло, флаг эпизода не
  ставится и отправка повторяется на следующий день. Флаг `preWarningSent` сбрасывается при восстановлении баланса и при выключенной подфиче «предупреждение» (чтобы после включения не остался старый флаг).
  Дневная проверка `RecoveryMonitorEngine.OnDay` не читает отчёты и не создаёт `BankruptcyView` с рефлексией (`DefaultTimeLimit`): только баланс, порог и три флага.
- **Страница хаба** показывает четыре строки сводки (баланс, порог, дефицит или запас, до банкротства) и кнопку «Меню спасения»; вне игры (нет компании игрока) — одну строку `rec.hub.noGame`. Хаб закрывается
  перед открытием меню. Отладочные действия — TSK-019.

### 8.3. Что проверить в игре (владелец; игра не запускалась, UI собирался только компилятором и разбором исходников)

- Размер и раскладка окон: меню 780x640 (`RecoveryWindow.WindowSize`), предложение 620x620 (`BailoutOfferWindow.WindowSize`); высота прокручиваемой области вкладки (минимум 300, желаемая 460, остальное
  берёт раскладка окна); ширины колонок таблицы кредитов и журнала (`RecoveryWindow.Loans.cs`, `RecoveryWindow.Journal.cs`).
- Время сборки вкладки (≤ 50 мс): строка `menu: the tab X was built in N ms` в `Player.log` при включённом подробном журнале.
- Выпадающий список пластин внутри прокручиваемой области (не обрезается ли список); `ModUI.Slider` внутри прокрутки; подсказки на неактивных кнопках (`ModUI.Tooltip`).
- Цвета тона (`MenuTone`) в `InfoText` и тексте: теги `<color>`/`<alpha>` берутся из `Colors.Instance`; при смене темы вкладка перестраивается.
- Письма «Риск дефолта» и «Компания в дефолте»: действие «Меню спасения» открывает меню поверх окна почты, письмо остаётся и действие нажимается снова (AC-1.4).
- Окно предложения: открывается из P1 посреди тика, время стоит; крестик закрывает без отказа, и на следующем тике окно открывается снова; «Принять» вызывает `CancelDefault` (баннер «успех»);
  «Отказаться» -> вопрос -> ванильное банкротство на следующем тике (M3, M4).
- Кнопка «Меню спасения» в `BankruptcyWarningWindow` (место и размер — `WarningWindowButton`, сценарий M2).

### 8.4. Запросы к интегратору / ядру

- Нового нет. Остаётся п. 7.4 (тесты `BuiltDll_*` при свежесобранной DLL одной фичи): после проверки сборки DLL фичи удалена из `mod/build/features/bankruptcy-recovery/`.

## 9. Что сделано в куске #5 (TSK-017, TSK-018, TSK-021, TSK-022: выключение фичи, тесты сейва и изоляции ИИ, самопроверка, текст для README) и что нужно интегратору

### 9.1. Состав

- **TSK-022.** Чистая логика: `Logic/DisableSettlementEngine.cs` (`Quote()` — вопрос хаба, `Settle()` — перевод остатка в обычный долг; `DisablePlan`, `SettleResult`, `SettleStatus`), `Logic/SettlementMarks.cs` (отметки о переводе в флагах сейва, `Reconcile`, `ISettlementFlags`), `Logic/Model/RecoverySaveSection.cs` (+ `UseMarks`, `Freeze`, `DropFrozen`, `Reconcile`; полей в секции не прибавилось, их по-прежнему восемь). Сторона игры: `Services/DisableSettlementService.cs` (`BuildConfirmation`, `Settle(reason)`), `SaveMarks.cs` (`ISettlementFlags` поверх `ModSave.Flags`), `RecoverySaveParticipant` (`Freeze`/`DropFrozen`/`Reconcile`), `BankruptcyRecoveryFeature`: `ctx.ConfirmBeforeDisable(_settlement.BuildConfirmation)`; `OnDisabled` = закрыть окна, `Settle("disabled")`, `Freeze()`; `OnGameReady` = `DropFrozen()` + `Reconcile()`; `ModSave.AfterRead` → `Reconcile()`; `ModFeatures.StateChanged(Enabled)` → ещё раз `EnsureOpened` (ядро отдаёт секцию участнику уже после `OnEnabled`). Операция одна: `CapitalOut(Total)` с кодом `disable.convert` (запись в журнал делает `CapitalLedger`), затем закрываются кредиты (`lastBridgeClosedOn` = сегодня, если среди них был бридж-кредит), линия с долгом (`closed`/`disabled`), договор с инвестором (`active = false`), пишутся отметки; уведомление `ModNotify` `rec.notify.converted`. Строки (`rec.disable.*`, `rec.notify.converted`, имя `disable.convert`) лежали в таблицах с куска #4.
- **TSK-018.** `PlayerFinanceAdapter.PlayerCompany()` отдаёт компанию, только если `company.IsPlayer` (`Company` = true, `AICompany` = false): ни движок, ни `CapitalLedger` не доберутся до компании ИИ даже при ошибке в `Player.Instance.Company`. Самопроверка: `TargetReport.Entries` (все цели, найденные и нет), `TargetEntry.Line`, `TargetReport.FailureText`; `RecoverySelfCheck.Run` пишет в `Player.log` по строке `self-check: [P1, P2] BankruptcyHandler.TickBankruptcies(): found` для P1–P4 и членов рефлексии (остальные цели — на уровне Debug) и `WARN` на каждую пропавшую; `RecoverySelfCheck.VerifyPatches()` (из `OnEnabled`, когда ядро уже применило патчи) пишет `self-check: the patches P1-P4 are applied` или `WARN`.
- **TSK-017.** `RecoveryStateSerializationTests` и `Data/sample.ptmod.json` (пример из design с `_schema`, заголовком и чужим разделом `rival-acquisitions`).
- **TSK-021.** Текст раздела для `README.md` готов (п. 9.6); сам `README.md` не входит в файлы этой фичи, вставку делает интегратор.
- **Тесты (119 новых, у фичи теперь 614):** `DisableSettlementEngineTests` (вопрос и перевод, AC-12.2, AC-12.6, идемпотентность, «никогда не богаче», нет компании или леджера), `SettlementMarksTests`, `DisableFlowTests` (выключить и включить на настоящем `SideFileModel` ядра: тот же сеанс, сохранение при выключенной фиче, перезапуск, тот же сейв загружен заново, новая игра, другой сейв), `DisableWiringTests` (разбор исходников), `AiIsolationTests`, `SelfCheckIntegrationTests` (фейковый реестр на `FeatureStateMachine` ядра), `RecoveryStateSerializationTests`. Поддержка: `SettlementKit.cs` (`FakeSettlementFlags`, готовые состояния).
- Пути тестов из Deliverables TSK-017/018 (`mod/tests/PTMod.Tests/BankruptcyRecovery/...`) заменены на `mod/tests/PTMod.Tests/Features/BankruptcyRecovery/...`: так собирает `build_feature.py` и так лежат тесты остальных кусков.

### 9.2. Что делает ядро при повторном включении фичи, и что сделано (интегратору: прочитать и по возможности поправить в ядре)

Когда фича снова `Enabled`, `ModSave` отдаёт её участнику **копию секции, которую модель держит с последней записи**, а не состояние на момент выключения (`SideFileModel._retained` заполняется только в `BuildSection` для активного участника, после успешного `Read` запись удаляется; выключенного участника `BuildSection` не вызывает). Разобрано по `SideFileModel.cs`, `ModSave.OnFeatureStateChanged`/`Restore`, `ModFeatures.Deactivate`. Для `bankruptcy-recovery` из этого следует:

- загрузили сейв (секция прочитана, копии нет), выключили фичу, перевели долг, сохранили, включили: модель отдаёт **пустое** состояние (раздела в файле уже нет). Счётчик спасений = 0, пауза бридж-кредита потеряна, линия снова «не открывалась» и открылась бы заново;
- сохранялись при включённой фиче, потом выключили и перевели долг: модель отдаёт **старую** копию с уже переведёнными кредитами. Долг появится второй раз (или вернётся договор с инвестором);
- то же после перезапуска игры (фича выключена в `ptmod_settings.json`, `Register` не вызывается, раздел идёт как чужой), потом включили.

Это противоречит критерию TSK-022 («повторное включение не восстанавливает кредиты и линию, счётчик спасений сохраняется») и design D9, поэтому в самой фиче два слоя защиты, без правок ядра:

1. **Отметки в флагах сейва** (`SettlementMarks`, `ModSave.Flags` под id фичи: `settled`, `settledLoanSeq`, `settledLineClosed`, `settledBailouts`, `settledBridgeDay`). Их пишет каждый `Settle`, в том числе когда переводить нечего: счётчик спасений и пауза бридж-кредита должны пережить переключение. Значения только растут. `Reconcile` применяется после **каждой** выдачи секции (`RecoverySaveSection.Read` и `ResetToDefaults`), после загрузки (`ModSave.AfterRead`) и на `GameReady`: убирает кредиты с номером ниже `settledLoanSeq`, держит линию закрытой, закрывает договор инвестора переведённого спасения (`bailoutsUsed ≤ settledBailouts`), поднимает `bailoutsUsed`, `nextLoanSeq` и дату паузы бридж-кредита до отметок. Флаги лежат в разделе ядра `mod-framework` (он читается и пишется всегда, даже при выключенной фиче) и сбрасываются ядром в новой игре.
2. **Сохранённое состояние** (`RecoverySaveSection.Freeze`/`TakeFrozen`): `OnDisabled` запоминает состояние, каким оно стало после перевода (в нём журнал с записью `disable.convert`, флаги эпизода, переключатель автозайма), и первая выдача секции после включения **в том же сеансе и той же игре** берёт его вместо копии ядра. «Та же игра» — тот же объект `ModSave.FileHeader` (ядро создаёт новый при каждом чтении бокового файла, в том числе того же сейва: после перезагрузки сейва, сделанного до выключения, мир вернулся к состоянию, когда деньги не списывались, и кредиты в нём действительно есть). Новая игра, меню и загрузка другого сейва это состояние сбрасывают.

Просьба к ядру (необязательная; тогда слои фичи станут страховкой, а не единственной защитой): **после `OnDisabled` обновлять копию секции** — вызвать `Write()` участника ещё раз и положить результат в `_retained` (например, `SideFileModel.CaptureParticipant(sectionId)` из `ModSave.OnFeatureStateChanged` на переходе `Enabled → Disabled` по выбору игрока). Тогда сейв, записанный при выключенной фиче, содержит переведённое состояние, а включение отдаёт его же. Совместимость сохранится: когда секция и флаги согласованы, `Reconcile` ничего не меняет.

### 9.3. FR-12 п. 5 / AC-12.5 (перевод остатка при загрузке сейва, если фича выключена или недоступна) выполнить из фичи нельзя

Допущение design «ядро читает секции выключенных фич» не выполняется (п. 4 выше это уже показывал). Второе открытие, ещё жёстче:

- у выключенной в `ptmod_settings.json` фичи **нет кода, который ядро позовёт на `GameReady(Loaded)`**. `Register` не вызывается (подписки нет). Подписчиков событий `ModTicks`/`ModSave`/`ModFeatures` ядро вызывает через `ModHooks.Raise`, а он пропускает обработчики фич не в состоянии `Enabled`. Патчи выключенной фичи сняты. Для `Unavailable` и `Faulted` то же. Обработчик вне пространства имён фичи добавить нельзя (мои файлы — только папка фичи);
- `ModSave` не отдаёт сырую секцию неактивной фичи (`_retained` закрыт), а чтение файла из фичи — обход, который нарушает «файлы читает только ядро» (Security design) и всё равно некому вызвать;
- перевод «из памяти» неактивного участника опасен: его состояние при загрузке другого сейва не обновляется (`SideFileModel.Apply` неактивных не трогает), перевод списал бы долги чужой партии.

Поэтому AC-12.5 **зафиксирован как ограничение** (Acceptance TSK-022 это допускает). README требует выключать фичу в окне «Моды», где перевод работает. Выключение правкой `ptmod_settings.json` оставляет обязательства в боковом файле без платежей и процентов, при включении они продолжаются как были (отметки им не мешают: их пишет только настоящий перевод). Если владелец захочет закрыть и это, нужно ядро: либо точка в `FeatureContext` вида `ctx.OnLoadWhileOff(Action<JsonObject rawSection>)`, которую ядро зовёт после чтения файла для фич `Disabled`/`Unavailable` (с сырой секцией и правом её перезаписать), либо проверка обязательств самим ядром. Логика к этому готова: `DisableSettlementEngine.Settle()` работает на любом `RecoveryState` (`RecoveryStateCodec.Read(section)` и движок на окружении с этим состоянием), отметки делают повтор безопасным.

### 9.4. Отличия от design (расширения; в `Api\` ничего не менялось)

- `DisableSettlementService` — класс с экземпляром, не статический: ему нужно окружение фичи. `Settle` не спрашивает подтверждения никогда, спрашивает только хаб.
- **Линия без долга не закрывается.** Design: `Settle` закрывает линию (`closed`/`disabled`). Здесь закрывается линия, **по которой есть долг** (`drawn > 0`). Открытая линия без выборки остаётся открытой: денег она не даёт (лимит, срок и проценты те же), и выключение при нулевом долге не отнимает то, чем игрок не пользовался. Если переводить нечего (`Quote` = null), `Settle` не меняет ничего, кроме отметок.
- События `RecoveryApi.LoanClosed` при переводе **не поднимаются** (кредит не погашен, а переведён), уведомления `rec.notify.loanClosed` по каждому кредиту тоже нет: одно `rec.notify.converted` с общей суммой.
- Запись журнала одна: `disable.convert` на сумму `−Total` (её делает `CapitalLedger.CapitalOut`), в `details` состав («2 loans, line, investor»). Если денег переводить нечего (договор с нулевым остатком), записи нет.
- Если игрока в игре нет (главное меню) или `CapitalLedger` не зарегистрирован, `Settle` ничего не меняет и пишет WARN (`SettleStatus.Unavailable`): обязательства без списания не закрываются, бесплатных денег нет.
- `Faulted`/`Unavailable` (автоотключение): `OnDisabled` идёт под `ModHooks.Guard`, который у такой фичи тело не запускает (`CanRun` = false), поэтому перевода нет. Платежи и проценты стоят до перезапуска (граничный случай 16).
- `PlayerFinanceAdapter.PlayerCompany()` требует `IsPlayer` (см. 9.1).
- `Register` по-прежнему бросает `InvalidOperationException(report.FailureText)` при нехватке обязательной цели самопроверки (ядро помечает фичу `Unavailable`), а список целей и проверка применения патчей — новые строки в `Player.log`.

### 9.5. Запросы к интегратору / ядру

1. **Правка ядра по 9.2** (копия секции после `OnDisabled`) — желательно.
2. **По 9.3** — решение владельца: оставить ограничение (README его описывает) или добавить в ядро точку для выключенных фич.
3. **TSK-021, `README.md`:** вставить текст п. 9.6 подразделом `### Финансы и спасение от банкротства` в раздел «Моды» (например, после `### Хаб «Моды»`). Предложение владельцу ядра (mod-framework TSK-045, вне этой спеки): `install.py --remove-mods` пусть читает боковые файлы сейвов и предупреждает, если в разделе `bankruptcy-recovery` есть кредиты, линия с долгом или действующий договор («выключите фичу перед удалением мода»). Общее предупреждение в README («Перед снятием мода выключите его фичи») уже есть.
4. Остаётся п. 7.4 (тесты `BuiltDll_*` при свежесобранной DLL одной фичи): после проверки сборки каталог `mod/build/features/bankruptcy-recovery/` очищен.

### 9.6. Текст раздела README (TSK-021), готов к вставке

````markdown
### Финансы и спасение от банкротства

Фича **«Спасение от банкротства»** (раздел «Финансы» в окне «Моды», включена по умолчанию) превращает банкротство игрока из таймера в цепочку решений с ценой. Она работает только с вашей компанией: ИИ-компании банкротятся по обычным правилам игры, а пороги и формулы банка мод не меняет. Подробности и точные формулы: [требования](.kiro/specs/bankruptcy-recovery/requirements.md), [дизайн](.kiro/specs/bankruptcy-recovery/design.md), [задачи](.kiro/specs/bankruptcy-recovery/tasks.md).

Три обозначения в тексте ниже:
- **дефолт** — баланс ниже порога; на 120-й день дефолта игра объявляет банкротство;
- **`D`** — порог дефолта (лимит долга, который банк определяет для вашей компании);
- **`D_L`** — база лимитов мода: `D`, а если он слишком мал (ранние годы), то `2 000 000 $`, умноженные на инфляцию. **`R_3`** — ваша выручка от продаж и контрактов за последние три полных месяца.

**Как открыть меню спасения.** Три входа: действие «Меню спасения» в письмах «Риск дефолта» и «Компания в дефолте» (письмо остаётся, действие можно нажимать снова); кнопка «Меню спасения» в окне предупреждения о дефолте (игра показывает это окно один раз после загрузки сцены, поэтому основной вход — письмо); страница «Финансы» фичи в окне «Моды» (доступна в любой момент, в том числе вне дефолта). Меню модальное: пока оно открыто, время стоит. Вкладки: «Сводка» (баланс, порог, дефицит или запас, дни до банкротства, долг по кредитам мода, ближайший платёж), «Активы», «Кредиты», «Журнал» (последние 50 операций; в сейве хранятся 200).

**Письма.** «Риск дефолта» приходит, когда баланс падает ниже `−0,75·D`. Второй раз в том же падении оно не повторяется, а после подъёма выше `−0,5·D` может прийти снова. «Компания в дефолте» приходит в день начала дефолта.

Каждая операция с деньгами идёт в два шага: настройка и превью на вкладке, затем вопрос с суммой, побочными эффектами и балансом после. Недоступные опции неактивны, причина в подсказке.

| Опция | Где | Цена и условия |
|---|---|---|
| Продать линии | «Активы» | Выручка по ванильной цене окна «Управление линиями», мощность падает. Остаётся минимум 1 линия. Если мощность станет меньше линий под контракты, до подтверждения будет предупреждение о штрафах |
| Распродать склад CPU | «Активы» | Ванильная цена, 29 $ за штуку; склад обнуляется |
| Снять CPU с производства | «Активы» | Штраф по контрактам этого CPU плюс выручка за склад; CPU уходит из производства. Список отсортирован по дневной марже, самые убыточные сверху |
| Продать производство (fabless) | «Активы» | Открывает ванильное окно линий и показывает ожидаемую сумму. Неактивна для литейщиков, без линий и на сложности «Невозможно» |
| Стартовая кредитная линия | «Кредиты» | Первые 3 года от основания компании. Лимит `a·D_L + 0,5·R_3`, не больше `3·D_L`; `a` — от 1,5 на самой лёгкой сложности до 0,25 на «Невозможно». 10 % годовых (с множителем ставки банка), проценты раз в месяц одной строкой «Проценты». Каждый месяц, если баланс положителен, `25 %` баланса (не больше долга) гасят линию. По истечении срока выборка закрывается, остаток становится срочным кредитом на 24 месяца под 15 %. «Автозаём» (включён по умолчанию): если баланс ниже `−0,9·D`, линия сама берёт сумму, возвращающую его к `−0,6·D`, до ежедневной проверки банкротства |
| Бридж-кредит | «Кредиты» | Доступен, когда баланс ниже `−0,75·D` или компания в дефолте, нет другого бридж-кредита и прошло 180 дней с погашения предыдущего. Сумма до `D_L + 0,25·R_3` (ползунок 25–100 %). Комиссия 5 % списывается сразу, кредит на 12 месяцев под 30 % годовых (с множителем ставки банка), платёж 1-го числа |
| Последний шанс | окно в день банкротства | На последнем дне дефолта вместо банкротства открывается окно (время стоит). Сумма `B = 0,25·D − баланс`, после спасения баланс `+0,25·D`. Вариант 1: кредит спасения на 36 месяцев под 40 % годовых. Вариант 2: 30 % положительной чистой прибыли каждого месяца в течение 60 месяцев, всего не больше `3·B` (статья «Роялти»); договор с инвестором в каждый момент один. Любое спасение снижает репутацию на 10. Спасений за партию: 2 / 1 / 1 / 1 / 1 / 0 по сложностям от самой лёгкой до «Невозможно» (на «Невозможно» спасения нет). Закрытие окна крестиком не отказ: оно откроется снова на следующем дне. «Отказаться» (с вопросом) приводит к обычному банкротству |

**Досрочный выход из дефолта.** Если баланс 7 дней подряд не ниже порога, дефолт снимается сразу. В ванильной игре он снимался бы только на 120-й день.

**Настройки.** На странице «Настройки» фичи в окне «Моды» пять переключателей: предупреждение, стартовая линия, бридж-кредит, последний шанс, досрочный выход. Каждый запрещает только **новые** операции своей части; уже взятые кредиты продолжают погашаться. Числа баланса лежат в `mod\src\PTMod\Content\bankruptcy-recovery\balance.json` и переопределяются файлом `PTMod\content\bankruptcy-recovery\balance.json` в папке игры (см. «Свои правки контента и текстов»). Ошибочное число заменяется значением по умолчанию с записью в `Player.log`.

**Выключение фичи.** Выключайте её в окне «Моды». Если за вами числятся обязательства (кредиты мода, выбранное по линии, остаток договора с инвестором), мод спросит: покажет сумму, баланс после и предупредит, если баланс окажется ниже порога дефолта. «Отмена» оставит фичу включённой, график платежей не изменится. «Выключить и перевести в долг» списывает остаток с баланса одной операцией («Перевод в обычный долг» в журнале), закрывает кредиты, линию с долгом и договор с инвестором и показывает уведомление. Баланс может уйти в минус, дальше на него начисляет проценты банк по обычной ставке игры. По договору с инвестором переводится остаток его потолка (потолок минус уже выплаченное), поэтому выключение не дешевле выполнения договора. Деньги при этом не возвращаются: при повторном включении закрытые кредиты и линия не восстанавливаются, число использованных спасений и пауза бридж-кредита (180 дней) сохраняются.

> **Выключайте фичу именно в окне «Моды».** Если выключить её правкой `ptmod_settings.json` вне игры, мод не работает и ничего перевести не может: кредиты остаются в боковом файле без платежей и процентов и продолжатся, когда фичу включат. Если фича остановлена из-за ошибок («Ошибка — отключена до перезапуска»), платежи и проценты тоже стоят до перезапуска игры.

> **Перед удалением мода выключите эту фичу в окне «Моды».** Кредиты и линия существуют только в моде. Если снять мод без выключения, игра их не узнает: долги исчезнут вместе с боковым файлом, а полученные деньги останутся у вас.

**Боковой файл сейва.** Кредиты, линия, договор с инвестором, счётчик спасений, журнал и отметки о выключении фичи лежат в `<имя>.ptmod.json` рядом с сейвом (разделы `bankruptcy-recovery` и `mod-framework`). Копируйте сейв **вместе с** этим файлом: без него сейв загрузится, но все обязательства мода в нём исчезнут. Сейв, сделанный до установки мода, загружается без ошибок: стартовая линия откроется на остаток первых трёх лет, если они ещё не прошли.
````

### 9.7. Что проверить в игре (владелец; игра не запускалась)

- **M10** целиком (design, «Ручные сценарии»). Ожидаемая строка `Player.log` при выключении в хабе: `[bankruptcy-recovery] ... settle (disabled): <сумма> became ordinary debt, N loan(s) closed ...`; после включения на вкладке «Журнал» запись «Перевод в обычный долг» на месте. Окно подтверждения: «Непогашенные обязательства (…) станут обычным долгом банка. Баланс после: ….» и, если баланс уйдёт ниже порога, через пробел предупреждение о дефолте.
- **Выключить, сохранить, выйти из игры, запустить (фича включена), загрузить:** кредитов нет, линия закрыта, счётчик спасений и пауза бридж-кредита на месте (отметки `settled*` в разделе `mod-framework` файла `.ptmod.json`).
- **Выключить, загрузить тот же сейв, включить:** в том мире долг не списывался, кредиты должны быть на месте. Это проверяет допущение, что `ModSave.FileHeader` после чтения бокового файла — новый объект (п. 9.2, слой 2).
- В `Player.log` после запуска: пять строк `self-check: [P1, P2] BankruptcyHandler.TickBankruptcies(): found`, `[P3] ...`, `[P4] ...`, `[reflection] ...` (две), затем `self-check: 20 of 20 game members found`, а когда фича включилась, `self-check: the patches P1-P4 are applied`.
- Выключить фичу правкой `ptmod_settings.json` при кредите в сейве, загрузить, включить: кредиты на месте, платежей за время выключения не было (ограничение п. 9.3, AC-12.5 не выполняется).
