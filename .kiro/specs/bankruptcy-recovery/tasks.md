# Tasks: bankruptcy-recovery

## Document Information

- **Feature Name**: bankruptcy-recovery (I02)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/bankruptcy-recovery/requirements.md`
  - Design: `.kiro/specs/bankruptcy-recovery/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`
  - Зависимость: `.kiro/specs/mod-framework/` (Core). Номера задач ядра взяты из его `tasks.md` v0.2 (запись `mod-framework/TSK-0xx`).

## Implementation Overview

Фича строится снизу вверх: каркас и контент → спайк приватных целей и реального `DebtLimit` → чистая логика с тестами → адаптеры и сервисы → патчи → UI → безопасное выключение → проверка. Чистая логика (`LoanMath`, `CreditLineMath`, `BailoutMath`, `RecoveryRules`, `DisableSettlement`) не зависит от Unity и пишется первой, по тестам мини-раннера ядра.

### Implementation Strategy

- Сначала спайк (TSK-003): подтвердить, что приватные члены `BankruptcyHandler` доступны, prefix/postfix на `TickBankruptcies` уживаются с transpiler'ом P10 ядра, кнопку можно добавить в `BankruptcyWarningWindow`, и замерить реальный `DebtLimit`. От результата зависят числа в `balance.json` (в первую очередь `limits.floorBase`).
- Все денежные операции — только через `CapitalLedger` (D1, D2 в design).
- Каждый патч и обработчик — с `ModHooks.Guard` на границе и проверкой «фича/подфича включена» в начале.
- Точка входа — `[ModFeature] BankruptcyRecoveryFeature : IModFeature`; тики — `ctx.OnTick`; старт — `ModTicks.GameReady`; состояние — `IModSaveParticipant` (поле версии секции не хранится: его ведёт ядро).

### Development Approach

- **Testing Strategy**: TDD для чистой логики (`mod/tests/PTMod.Tests/BankruptcyRecovery/`, мини-раннер ядра, запуск `python mod/build.py --tests`; каждый публичный тип `Logic` указан в `[Covers]`); самопроверка на старте для целей патчей; ручные сценарии M1–M10 (design) выполняет владелец.
- **Integration Strategy**: подфичи включаются по одной в `ModSettings` (`ctx.Param`); UI подключается после сервисов.
- **Deployment Strategy**: `install.py --with-mods` (из `mod-framework`); фича по умолчанию включена, отладочные действия — только с флагом отладки.

Размер задач — 2–4 часа.

## Фаза 0 — Каркас и спайк

- [x] **TSK-001**: Каркас фичи `[ModFeature]` и участник сейва (авто 2026-10-10)
  - **Requirement**: FR-11, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/BankruptcyRecoveryFeature.cs` (`IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn` пустой, `EnabledByDefault = true`, `SupportsHotToggle = true`, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `mod/src/PTMod/Features/BankruptcyRecovery/RecoveryApi.cs` (заглушки), `mod/src/PTMod/Features/BankruptcyRecovery/RecoverySaveParticipant.cs` (`IModSaveParticipant`), `mod/src/PTMod/Features/BankruptcyRecovery/Logic/Model/RecoveryState.cs` (DTO без поля версии)
  - **Acceptance**: фича с ID `bankruptcy-recovery` видна в хабе «Моды» (`ModFeatures.All`); выключается и включается; пять `ParamSpec` подфич (`preWarning`, `startupCredit`, `bridgeLoan`, `bailout`, `earlyExit`, `ParamType.Bool`, по умолчанию `true`) видны на странице фичи; пустая секция пишется в боковой файл и читается обратно, поле `_schema` в секции добавляет ядро; без секции — состояние по умолчанию; своих счётчиков ошибок и флагов `Enabled` нет
  - **Depends**: mod-framework/TSK-022 (`ModFeatures`, `FeatureContext`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`, `ModLog`), TSK-025 (`ModSave`), TSK-067 (мини-раннер)

- [x] **TSK-002**: Контент баланса и валидатор (авто 2026-10-10)
  - **Requirement**: FR-12, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/bankruptcy-recovery/balance.json`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/Model/RecoveryBalance.cs` (DTO + `Validate()`), `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryBalanceTests.cs`
  - **Acceptance**: JSON совпадает с примером из design (в том числе `limits.floorBase = 2 000 000`, `perGameByDifficulty = [2,1,1,1,1,0]`, `expenseOrigin = "Royalties"`); загрузка через `ModContent.LoadConfig`; каждое поле вне диапазона заменяется значением по умолчанию с записью в `ModLog`; массивы `*ByDifficulty` длиной ≠ 6 отклоняются целиком; тесты зелёные
  - **Depends**: TSK-001; mod-framework/TSK-015 (`ModContent`: слияние), TSK-021 (адаптер `ModContent`), TSK-067

- [ ] **TSK-003**: Спайк — приватные цели `BankruptcyHandler`, совместимость с P10 ядра, кнопка в окне предупреждения, реальный `DebtLimit`
  - **Requirement**: FR-2, FR-8, FR-9
  - **Deliverables**: временный отладочный код в `mod/src/PTMod/Features/BankruptcyRecovery/Debug/SpikeProbe.cs` (сборка спайка — `python mod/build.py --src … --out …`, удаляется после спайка); раздел «Результаты спайка» в `design.md`
  - **Acceptance**: в `Player.log` есть значения `CentralBank.DebtLimit` и `ModCredit.PlayerDebtLimit()` на 1975-01-01, 1980-01-01, 1985-01-01 новой игры; `AccessTools` находит `BankruptcyHandler.CancelDefault(ICompany)` и поле `defaultTimeLimit` (= 120); вызов `CancelDefault(игрок)` убирает баннер дефолта; prefix и postfix на `BankruptcyHandler.TickBankruptcies` применяются вместе с transpiler'ом P10 ядра (`ModHooks` не отклоняет, порог читается из `ModCredit.ThresholdOf`); в `BankruptcyWarningWindow` удаётся добавить кнопку клоном `ButtonUI`. Если `DebtLimit` = 1 000 000 всю игру — зафиксировать влияние на пол `limits.floorBase` и скорректировать его в `balance.json` до TSK-009
  - **Depends**: TSK-001; mod-framework/TSK-006 (Harmony-спайк), TSK-023, TSK-031 (P9–P11), TSK-058 (`ModCredit`), TSK-035 (`ModWindow`)
  - **Частично** (авто 2026-10-10, кусок 1; игра не запускалась, поэтому задача не закрыта): **сделан статический разбор** по декомпилированному коду, сцене и исходникам ядра, текст
    «Результаты спайка» готов к переносу в `design.md` в `mod/requests/bankruptcy-recovery.md`, п. 3. Подтверждено: `CancelDefault(ICompany)` и `defaultTimeLimit` (= 120 в сцене `level1`) — приватные
    члены `BankruptcyHandler`, `AccessTools` их находит; `CancelDefault(игрок)` по коду снимает баннер дефолта; prefix и postfix на `TickBankruptcies` `ModHooks` примет (резерв P10/P11 — только
    `PatchKind.Transpiler`, конфликт «второй transpiler» не касается prefix/postfix), шаблон P10 они не меняют; `CentralBank.DebtLimit` не застревает на 1 000 000 (значение сцены 10 000 000):
    1975-01-01 — 8 420 448, 1980-01-01 — 56 900 000, 1985-01-01 — 111 221 782 при множителях ядра, равных 1, поэтому `limits.floorBase = 2 000 000` оставлен без изменений.
    **Не проверено (нужна игра, владелец):** строки `Player.log` с `DebtLimit` и `PlayerDebtLimit()` на трёх датах; фактическое применение prefix и postfix вместе с P10 (покажет самопроверка TSK-010/TSK-018);
    вызов `CancelDefault(игрок)` в живой игре; клон `ButtonUI` в `BankruptcyWarningWindow` (раскладка окна неизвестна, подбирается в TSK-010/TSK-015, сценарий M2). Файл `Debug/SpikeProbe.cs` не создан
    намеренно (причина — в п. 3 запросов). Побочная находка для TSK-007 и TSK-022: `Register` у выключенной фичи не вызывается (`mod/requests/bankruptcy-recovery.md`, п. 4).

## Фаза 1 — Чистая логика

- [x] **TSK-004**: `LoanMath` — аннуитет, месячный шаг, месячный процент (авто 2026-10-10)
  - **Requirement**: FR-6, FR-7, FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/LoanMath.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/LoanMathTests.cs`
  - **Acceptance**: `AnnuityPayment(8 400 000, 0,30, 12) = 818 892 ± 1`; при ставке 0 платёж `A/n`; сумма тел за весь график = `A` (± 1 $); последний шаг гасит остаток; `MonthlyInterest(2 660 000, 0,10) = 22 166,67 ± 0,01`; расчёт в `double`; тип в `[Covers]`
  - **Depends**: TSK-001; mod-framework/TSK-067

- [x] **TSK-005**: `CreditLineMath` — база лимитов, лимит, автозаём, гашение, окно срока (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/CreditLineMath.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/CreditLineMathTests.cs`
  - **Acceptance**: `LimitBase = max(D, floorBase × I)` (пример: `D = 1 млн`, `I = 2,0`, `floorBase = 2 млн` → 4 млн); лимит для 6 уровней сложности по формуле design; верх `3·D_L`; пример «−7,7 млн → автозаём 2,66 млн» при `D = 8,4 млн`; автозаём ≤ свободного лимита и 0 при балансе выше триггера; `Sweep` = 0 при балансе ≤ 0; `IsWindowOpen` верно на границах (день основания, +3 года − 1 день, +3 года)
  - **Depends**: TSK-002

- [x] **TSK-006**: `RecoveryRules`, `BailoutMath` и `DisableSettlement` (авто 2026-10-10)
  - **Requirement**: FR-1, FR-6, FR-8, FR-9, FR-11, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/RecoveryRules.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/BailoutMath.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/DisableSettlement.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryRulesTests.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/BailoutMathTests.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/DisableSettlementTests.cs`
  - **Acceptance**: `MustHoldForBailout` = `true` только для игрока в дефолте с `DaysInDefault + 1 ≥ T` и `money < порог`, при `bailoutsUsed < bailoutsAllowed` и без отказа; для ИИ — всегда `false`; на сложности 5 (`Allowed = 0`) — `false`; `BailoutMath.Allowed` отдаёт `[2,1,1,1,1,0]`; предупреждение: отправка → без повтора → сброс выше `−0,5·D` → повторная отправка; серия выхода считает и обнуляется; `CanTakeBridge` учитывает активный кредит и кулдаун 180 дней; `BailoutMath.Amount` для примера 1977 = 36 075 000; платёж инвестору при убытке 0 и не выше потолка; `DisableSettlement.Total` = остатки кредитов + выборка по линии + `cap − paid`, для пустого состояния 0
  - **Depends**: TSK-002

## Фаза 2 — Адаптеры, сервисы, патчи

- [x] **TSK-007**: `PlayerFinanceAdapter` и `CapitalLedger` (авто 2026-10-10)
  - **Requirement**: FR-6, FR-7, FR-8, FR-10, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/PlayerFinanceAdapter.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/CapitalLedger.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/CapitalLedgerTests.cs` (на `FakePlayerFinance`)
  - **Acceptance**: `DebtLimit` и `Threshold` берутся из `ModCredit.PlayerDebtLimit()` / `ThresholdOf(игрок)` (прямого чтения `CentralBank.DebtLimit` нет); `CapitalIn/Out(featureId, …)` двигают баланс через `Company.AddMoney` и не попадают в отчёт месяца; `Expense` идёт через `Company.SpendMoney` с нужной `ExpenseOrigin`; суммы ≤ 0, `NaN`, `∞` отклоняются с `ModLog`; журнал — кольцевой на 200 записей и только для `featureId = "bankruptcy-recovery"` (вызовы `rival-acquisitions` пишутся лишь в `ModLog`); `SalesRevenueLastFullMonths(3)` ищет отчёты по `Year`/`Month`; `InterestMultiplier` берётся из канала `bank.interest` только для игрока; `Inflation` читается из `InflationHandler.Instance`
  - **Depends**: TSK-003; mod-framework/TSK-027 (`ModModifiers`), TSK-058 (`ModCredit`), TSK-021 (`ModLog`)

- [x] **TSK-008**: `LoanService` и `BridgeLoanService` (авто 2026-10-10)
  - **Requirement**: FR-6, FR-10, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/LoanService.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Services/BridgeLoanService.cs`
  - **Acceptance**: `OnMonth` (через `ctx.OnTick(TickPhase.LateMonthPassed, …)`) списывает проценты (`Interest`) и тело (`CapitalOut`) по каждому кредиту; закрытие кредита → `LoanClosed` и `ModNotify`; досрочное погашение работает при достаточном балансе; бридж-кредит: проверка `CanTakeBridge`, зачисление `A` (`A_max` от `D_L`), комиссия 5 %, кредит 12 мес.; при выключенной подфиче `BridgeLoan` новые кредиты не выдаются, а существующие обслуживаются; состояние пишется через `RecoverySaveParticipant`; вычисление `D_L` — `CreditLineMath.LimitBase`
  - **Depends**: TSK-004, TSK-006, TSK-007; mod-framework/TSK-024 (`ModTicks`)

- [x] **TSK-009**: `StartupCreditLineService` (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/StartupCreditLineService.cs`
  - **Acceptance**: линия открывается на `ModTicks.GameReady(NewGame)` (после перемотки) сроком 3 года; при `GameReady(Loaded)` и при включении фичи — на остаток срока, если состояние «не открыта» и с основания прошло < 3 лет; закрытая линия (`closedReason`) повторно не открывается; в `LateTick` ежедневно: лимит, автозаём (если включён); в `LateMonthPassed`: проценты `drawn·r·M/12` одной строкой `Interest`, затем гашение 25 % положительного баланса, затем при `сегодня ≥ closesOn` остаток → срочный кредит 24 мес. под 15 % через `LoanService`; при `StartupCredit = false` новые выборки и открытие недоступны, существующая выборка обслуживается; во время перемотки и загрузки ничего не делает
  - **Depends**: TSK-005, TSK-007, TSK-008; mod-framework/TSK-024

- [x] **TSK-010**: Патчи `BankruptcyHandler` и `BankruptcyWarningWindow`, самопроверка (авто 2026-10-10)
  - **Requirement**: FR-2, FR-8, FR-9, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Patches/BankruptcyPatches.cs` (P1–P4 из design, `[ModPatch]`, подключение `ctx.Patch`), регистрация целей самопроверки
  - **Acceptance**: P1 удерживает `DaysInDefault` игрока по `MustHoldForBailout` (порог — `ModCredit.ThresholdOf`) и ничего не делает для ИИ; P2 вызывает `CancelDefault(игрок)` после `streakDays` дней подряд выше порога; P3 вызывает `RecoveryMonitor.OnPlayerDefault` только для игрока; P4 добавляет кнопку один раз за сцену; каждый патч — в `ModHooks.Guard` с проверкой `ModFeatures.IsActive`, при исключении игра продолжает (счётчик и `Faulted` ведёт ядро); `ModHooks` принимает prefix/postfix на `TickBankruptcies` (резерв ядра — только transpiler); самопроверка перечисляет все цели из design и пишет результат в `Player.log`
  - **Depends**: TSK-003, TSK-006, TSK-007; mod-framework/TSK-023 (`ModHooks`)

- [x] **TSK-011**: `BailoutService` — предложение, принятие, отказ, доля инвестора (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/BailoutService.cs`
  - **Acceptance**: предложение открывается один раз на запрос P1, а в пакетном прогоне (`ModTicks.IsBatchRun`) — после `ModTicks.BatchEnded` при непрерывном удержании счётчика; «Кредит спасения» — `CapitalIn(B)`, `CancelDefault`, кредит 36 мес. под 40 % × `bank.interest`; «Доля инвестора» — `CapitalIn(B)`, `CancelDefault`, ежемесячная выплата 30 % положительной прибыли прошлого месяца (по `Year`/`Month`) через статью из контента (`Royalties`), 60 мес., потолок `3·B`; в обоих вариантах `bailoutsUsed++` и `ModReputation.Add(игрок, −10, "rec.reputation.bailout")`; лимит спасений по сложности `[2,1,1,1,1,0]`; отказ ставит `bailoutDeclined`, и следующий тик идёт ванильно; флаг «предложение открыто» не сохраняется; флаг assisted не ставится
  - **Depends**: TSK-008, TSK-010; mod-framework/TSK-026 (`ModReputation`), TSK-057 (`IsBatchRun`, `BatchEnded`)

- [x] **TSK-012**: `AssetSaleService` — линии, склад, снятие CPU, выход в fabless (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/AssetSaleService.cs`
  - **Acceptance**: превью линий = `ProductionMath.SaleCost(N, W)`; продажа = `Company.RemoveProductionLines` + `EarnMoney(Sales)` + `ManageLinesWindow.UpdateWafers()`; минимум 1 линия; предупреждение, если мощность после продажи < `ContractHelper.ContractedProductionLines(Player.Instance)`; склад — `StorageMath.Income`/`SellStock`; снятие — штраф `ContractHelper.FinesFromContracts(cpu)` в превью и `Cpu.TriggerRetirement()`; список CPU без снятых, по возрастанию дневной маржи; «Продать производство» неактивна при `IsFoundry`, 0 линий или `DifficultyLevel = 5`, иначе открывает `ManageLinesWindow`
  - **Depends**: TSK-007

## Фаза 3 — UI

- [x] **TSK-013**: Меню спасения — вкладки «Сводка» и «Активы» (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.cs`
  - **Acceptance**: окно модальное (`ModUI.CreateWindow`, `PauseTrigger`), тема переключается вместе с игрой; сводка показывает 6 строк из design; на «Активах» каждая операция проходит двухшаговое подтверждение через `ModUI.Confirm` с суммой и балансом после (собственного диалога подтверждения нет); недоступные опции неактивны с причиной (`ModUI.Tooltip`); окно строится ≤ 50 мс (лог отладки)
  - **Depends**: TSK-012; mod-framework/TSK-035 (`ModWindow`), TSK-036 (виджеты), TSK-064 (`ModUI.Confirm`, `ModUI.Tooltip`)

- [x] **TSK-014**: Меню спасения — вкладки «Кредиты» и «Журнал» (авто 2026-10-10)
  - **Requirement**: FR-6, FR-7, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.Loans.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.Journal.cs`
  - **Acceptance**: таблица кредитов (вид, остаток, ставка, платёж, месяцев осталось, «Погасить досрочно»); блок линии (лимит/выбрано/свободно/дата, слайдер суммы, «Взять», «Погасить», переключатель «Автозаём»); блок бридж-кредита (25–100 %, условия из `rec.bridge.terms`, «Взять»); журнал — 50 последних записей (включая записи `disable.convert`)
  - **Depends**: TSK-008, TSK-009, TSK-013

- [x] **TSK-015**: Окно «Последний шанс», кнопка в окне предупреждения, страница хаба, письма, монитор предупреждения (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2, FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/BailoutOfferWindow.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Services/RecoveryMonitor.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryHubPage.cs` (`ctx.HubPage`, раздел «Финансы»)
  - **Acceptance**: окно предложения модальное, два варианта с условиями, «Отказаться»; закрытие крестиком не считается отказом; кнопка «Меню спасения» в `BankruptcyWarningWindow` открывает меню; письма «Риск дефолта» (`rec:prewarning`) и «Компания в дефолте» (`rec:default`) отправляются с приоритетом `Critical` и многоразовым `ModMailAction` «Меню спасения» (обработчик `ModMail.RegisterActionHandler`), не дублируются в эпизоде; ID письма не хранится (поиск `ModMail.Find`); страница фичи в хабе открывает меню в любой момент
  - **Depends**: TSK-010, TSK-011, TSK-013; mod-framework/TSK-033 (`ModNotify`), TSK-038 (`ModMailAction`, `RegisterActionHandler`), TSK-062 (приоритеты), TSK-039 (хаб)

- [x] **TSK-016**: Локализация ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4 (все FR с UI)
  - **Deliverables**: `mod/src/PTMod/L10n/ru/bankruptcy-recovery.json`, `mod/src/PTMod/L10n/en/bankruptcy-recovery.json` (ключи `rec.*` из design)
  - **Acceptance**: все строки окон, писем, уведомлений и подтверждения выключения берутся из `ModL10n`; нет жёстко прописанного текста в коде фичи (проверка поиском по каталогу фичи); все ключи начинаются с `rec.` (самопроверка ядра не предупреждает о чужом префиксе); при отсутствии ключа в `ru` показывается `en`; `install.py --check` не находит расхождений ru/en
  - **Depends**: TSK-013, TSK-014, TSK-015, TSK-022; mod-framework/TSK-043 (строки ядра), TSK-066 (`ModIds`)

## Фаза 4 — Проверка и документация

- [x] **TSK-017**: Тесты сохранения и совместимости (авто 2026-10-10)
  - **Requirement**: NFR-3, FR-12
  - **Deliverables**: `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryStateSerializationTests.cs`, тестовый файл `mod/tests/PTMod.Tests/BankruptcyRecovery/Data/sample.ptmod.json` (пример из design)
  - **Acceptance**: round-trip DTO ↔ `JsonObject` без потерь и без поля версии; отсутствующая секция → значения по умолчанию; состояния линии `none`/`open`/`closed` читаются и пишутся; битые даты и суммы отброшены с `ModLog`; пример бокового файла из design читается; чужая секция в том же файле не теряется (через `ModSave`); случаи `_schema` (нет поля, новее) проверяет ядро (mod-framework/TSK-016)
  - **Depends**: TSK-008, TSK-009, TSK-011, TSK-022; mod-framework/TSK-016, TSK-025

- [x] **TSK-018**: Тесты изоляции ИИ и интеграционная самопроверка (авто 2026-10-10)
  - **Requirement**: FR-11, NFR-2
  - **Deliverables**: `mod/tests/PTMod.Tests/BankruptcyRecovery/AiIsolationTests.cs`; расширение самопроверки (список целей из design → `Player.log`)
  - **Acceptance**: правила и сервисы не выполняют действий для `IsPlayer = false` (тесты на фейках); `CapitalLedger` отказывается работать с компанией, не являющейся игроком; самопроверка перечисляет все цели P1–P4 и члены рефлексии; искусственно сломанная цель (тест на фейковом реестре) делает фичу `Unavailable` без исключения наружу
  - **Depends**: TSK-010, TSK-011

- [ ] **TSK-019**: Отладочные действия для ручной проверки
  - **Requirement**: все FR (поддержка проверки)
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Debug/RecoveryDebugActions.cs` (только отладочная сборка `PTMOD_DEBUG`)
  - **Acceptance**: при флаге отладки в `ptmod_settings.json` на странице фичи в хабе есть «Баланс = k × D» (k: −0,8; −0,95; −1,1; −1,2; +0,1) и «DaysInDefault = N» (N: 1; 118); без флага и в релизной сборке действий нет; действия пишут в журнал с пометкой `debug`
  - **Depends**: TSK-015; mod-framework/TSK-039

- [ ] **TSK-020**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1…FR-12, NFR-1…NFR-7
  - **Deliverables**: заполненный протокол сценариев M1–M10 (раздел Testing в `design.md`): дата, версия, результат по каждому шагу; `Player.log` прогона без `[PTMod]`-ошибок
  - **Acceptance**: все сценарии M1–M10 пройдены владельцем, найденные дефекты заведены задачами; бюджет `LateTick` ≤ 0,05 мс по логу отладки
  - **Depends**: TSK-016, TSK-017, TSK-018, TSK-019, TSK-022; mod-framework/TSK-044 (`install.py --with-mods`), TSK-052

- [ ] **TSK-021**: Документация
  - **Requirement**: NFR-3, NFR-5, FR-12
  - **Deliverables**: раздел «Финансы и спасение от банкротства» в `README.md` (как работают линия, бридж-кредит, последний шанс; что выключение фичи в хабе переводит остаток в обычный долг; что перед удалением мода фичу нужно выключить, иначе кредиты мода исчезнут; копирование сейва вместе с `.ptmod.json`)
  - **Acceptance**: раздел описывает все опции меню и их цену; есть предупреждение о боковом файле и инструкция «выключите фичу перед удалением мода»; ссылки на спеку. Предложение владельцу ядра: `install.py --remove-mods` предупреждает о непогашенных кредитах в боковых файлах (правка mod-framework/TSK-045, вне этой спеки)
  - **Depends**: TSK-020; mod-framework/TSK-050 (раздел README «Моды»)
  - **Частично** (авто 2026-10-10, кусок 5): **текст раздела готов** в `mod/requests/bankruptcy-recovery.md`, п. 9.6 (все опции меню и их цена, линия, бридж-кредит, последний шанс, досрочный выход, выключение фичи с переводом остатка в обычный долг, предупреждение «выключайте фичу в окне «Моды» и перед удалением мода», боковой файл `.ptmod.json`, ссылки на спеку). `README.md` не входит в файлы этого куска, вставку делает интегратор (п. 9.5). Предложение владельцу ядра про `install.py --remove-mods` записано там же. Зависимость от TSK-020 (ручная проверка владельцем) не снята: цифры и тексты надо сверить с игрой.

- [x] **TSK-022**: Выключение фичи — подтверждение и перевод остатка в обычный долг (авто 2026-10-10)
  - **Requirement**: FR-12, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/DisableSettlementService.cs` (`BuildConfirmation`, `Settle`), подключение `ctx.ConfirmBeforeDisable` и `OnDisabled`/`OnEnabled`/`GameReady(Loaded)` в `BankruptcyRecoveryFeature`
  - **Acceptance**: провайдер подтверждения возвращает `null` без обязательств и `DisableConfirmation` с суммой, балансом после и предупреждением о риске дефолта — с обязательствами; «Отмена» оставляет фичу включённой, график платежей не меняется; «Да» и программное `SetEnabled(false)` вызывают `Settle`: одна операция `CapitalOut(Total)`, кредиты, линия (`closed`/`disabled`) и договор с инвестором закрыты, `lastBridgeClosedOn` обновлён, запись `disable.convert` в журнале, `ModNotify`; `Settle` идемпотентна; при `GameReady(Loaded)` и состоянии фичи `Disabled`/`Unavailable` с обязательствами в секции перевод выполняется без подтверждения (допущение «ядро читает секции выключенных фич» проверено в игре или зафиксировано как ограничение в README); повторное включение не восстанавливает кредиты и линию, счётчик спасений сохраняется; тесты `DisableSettlementTests` (TSK-006) и сценарий M10 пройдены
  - **Depends**: TSK-008, TSK-009, TSK-011; mod-framework/TSK-065 (`ConfirmBeforeDisable`), TSK-064 (`ModUI.Confirm`)

## Dependency graph

```
mod-framework (Core): TSK-022 Feature/Context, TSK-023 Hooks, TSK-021 Settings/Log/Content, TSK-025 Save, TSK-024 Ticks,
                      TSK-027 Modifiers, TSK-058 Credit, TSK-026 Reputation, TSK-057 BatchRun, TSK-035/036/064 UI,
                      TSK-033 Notify, TSK-038/062 Mail, TSK-039 Hub, TSK-065 ConfirmBeforeDisable, TSK-067 Runner
        |
     TSK-001 ──> TSK-002 ──┬──> TSK-005 ──────────────┐
        |                  └──> TSK-006 ──┐            |
        ├──> TSK-003 ──────────────────┐  |            |
        └──> TSK-004 ──────────────┐   |  |            |
                                   v   v  v            v
                              TSK-007 (PlayerFinanceAdapter, CapitalLedger)
                                   |
             ┌─────────────────────┼──────────────────────────┐
             v                     v                          v
          TSK-008 ──> TSK-009   TSK-010 (патчи, + TSK-003)   TSK-012
             |           |         |                          |
             ├───────────┴──> TSK-022 (+ TSK-011)             v
             └──> TSK-011 <────────┘                       TSK-013
                     |      |                                 |
                     |      └──> TSK-022                      |
                     └────────> TSK-015 <─────────────────────┤
                                   |          TSK-014 <───────┘ (+ TSK-008, TSK-009)
                                   v             |
                                TSK-016 <────────┘ (+ TSK-022)
                                   |
        TSK-017 (TSK-008, 009, 011, 022)   TSK-018 (TSK-010, 011)   TSK-019 (TSK-015)
                   └──────────────────────┬──────────────────────┘
                                          v
                                       TSK-020 (+ TSK-016, TSK-022) ──> TSK-021
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи [ModFeature] и участник сейва | Complete (авто 2026-10-10, кусок 1: `BankruptcyRecoveryFeature` (`[ModFeature]`, `DependsOn` пуст, `EnabledByDefault` и `SupportsHotToggle` = true), `RecoverySettings` + `Logic/Model/RecoveryParams` (пять `ParamSpec` Bool, по умолчанию true: `preWarning`, `startupCredit`, `bridgeLoan`, `bailout`, `earlyExit`), `RecoverySaveParticipant` (`IModSaveParticipant`, `SchemaVersion = 1`, поле версии не хранится) поверх чистой `Logic/Model/RecoverySaveSection` + `RecoveryStateCodec` + DTO `RecoveryState`, `RecoveryService` (заглушка `IRecoveryService` по состоянию, зарегистрирована через `RecoveryApi.SetService`), строки `rec.feature.*`, `rec.hub.title`, `rec.param.*` в ru/en; своих счётчиков ошибок и флагов `Enabled` нет (проверяет тест); сборка фичи и 75 тестов фичи зелёные; видимость в хабе «Моды» и горячее выключение/включение — по коду, проверить в игре; `JournalEntryData` в сборке тестов — заглушка `#if PTMOD_TESTS`, см. `mod/requests/bankruptcy-recovery.md`, п. 1) |
| TSK-002 Контент баланса и валидатор | Complete (авто 2026-10-10, кусок 1: `Content/bankruptcy-recovery/balance.json` = пример из design (`limits.floorBase` = 2 000 000, `perGameByDifficulty` = [2,1,1,1,1,0], `expenseOrigin` = `Royalties`), `Logic/Model/RecoveryBalance` (DTO для `ModJson.Bind` + `Validate()` + `FromJson`), загрузка `ModContent.LoadConfig` в `RecoveryContent`; поле вне диапазона получает значение по умолчанию и строку для `ModLog` (`bridgeLoan.annualRate out of range, default 0.30 used`), массивы `*ByDifficulty` длиной ≠ 6 или с плохим значением отклоняются целиком, пары `preWarning.reset ≤ threshold` и `autoDrawTarget ≤ autoDrawTrigger` проверяются вместе; тесты `RecoveryBalanceTests` (в том числе: значения кода = файл, каждое число вне диапазона) зелёные; диапазоны, которых нет в таблице design, выбраны автором: `startupCredit.years` 1…10, `bailout.investor.capMultiple` 0…100, `bailout.reputationDelta` −100…100, `assets.minLinesLeft` 1…100, элементы `perGameByDifficulty` 0…100) |
| TSK-003 Спайк: приватные цели, совместимость с P10, DebtLimit | Частично (авто 2026-10-10, кусок 1: статический разбор готов, «Результаты спайка» для `design.md` лежат в `mod/requests/bankruptcy-recovery.md`, п. 3; `limits.floorBase` = 2 000 000 без изменений; рантайм-часть (`Player.log` с `DebtLimit` на трёх датах, применение prefix и postfix с P10, `CancelDefault` в живой игре, кнопка в `BankruptcyWarningWindow`) требует запуска игры и не выполнена; `Debug/SpikeProbe.cs` не создан) |
| TSK-004 LoanMath | Complete (авто 2026-10-10, кусок 2: `Logic/LoanMath` (`AnnuityPayment`, `MonthlyInterest`, `MonthStep` + `LoanStep`, добавлен `EffectiveRate` для ставки × канал `bank.interest`), расчёт в `double`; `AnnuityPayment(8 400 000; 0,30; 12)` = 818 892 ± 1, при ставке 0 платёж `A/n`, сумма тел по графику = `A` ± 1 $ для ставок 0…100 % и сроков 1…120 мес., последний шаг гасит остаток, `MonthlyInterest(2 660 000; 0,10)` = 22 166,67 ± 0,01; кредит спасения из AC-8.2 даёт ≈ 1 735 600; мусорный вход (`NaN`, `∞`, отрицательное) даёт 0 вместо исключения; `LoanMathTests` (28 тестов) зелёные, `LoanMath` и `LoanStep` в `[Covers]`) |
| TSK-005 CreditLineMath | Complete (авто 2026-10-10, кусок 2: `Logic/CreditLineMath` (`LimitBase`, `Limit`, `AutoDraw`, `Sweep`, `IsWindowOpen`; добавлены `Available`, `ClosesOn`, `HasExpired`); `LimitBase(1 млн; 2,0; 2 млн)` = 4 млн, лимит для 6 уровней сложности по формуле design, верх `3·D_L`, AC-7.1/7.2/7.6 в тестах, автозаём −7,7 млн при `D = 8,4 млн` = 2,66 млн, не выше свободного лимита, 0 на границе триггера и выше, `Sweep` = 0 при балансе ≤ 0, `IsWindowOpen` на границах (день основания, +3 года − 1 день, +3 года, 29 февраля); `CreditLineMathTests` (33 теста) зелёные, тип в `[Covers]`) |
| TSK-006 RecoveryRules, BailoutMath, DisableSettlement | Complete (авто 2026-10-10, кусок 2: `Logic/RecoveryRules` (`MustHoldForBailout`, `PreWarning`, `NextRecoveryStreak`, `CanTakeBridge`; добавлен `IsStreakComplete`), `Logic/BailoutMath` (`Amount`, `Allowed`, `InvestorPayment`; добавлен `InvestorCap`), `Logic/DisableSettlement` (`Total`, `Breakdown`; добавлены `BalanceAfter`, `WouldDefault`), `Logic/Model/BankruptcyView` (+ `PreWarningDecision`, `Eligibility`; в `BankruptcyView` добавлено поле `IsPlayer`); удержание только для игрока в дефолте с `DaysInDefault + 1 ≥ T` и балансом ниже порога, при `bailoutsUsed < Allowed` и без отказа, для ИИ и на сложности 5 — `false`; `Allowed` = [2,1,1,1,1,0]; предупреждение «отправка → без повтора → сброс выше −0,5·D → повторная отправка», серия выхода считает и обнуляется (7 дней подряд = выход), бридж-кредит с причинами отказа (активный, кулдаун 180 дн., банкрот, баланс далеко от порога); `Amount` примера 1977 = 36 075 000; платёж инвестору при убытке 0 и не выше потолка; `DisableSettlement.Total` = остатки кредитов + выборка + `cap − paid` (для работающего договора), пустое состояние 0; тесты `RecoveryRulesTests` (47), `BailoutMathTests` (17), `DisableSettlementTests` (18) зелёные, все публичные типы в `[Covers]`; в `L10n` ru/en добавлены `rec.bridge.unavailable.{active,cooldown,notNeeded,bankrupt}`; отличия от design и замечание к архитектурному тесту `DebtLimit` — `mod/requests/bankruptcy-recovery.md`, п. 6) |
| TSK-007 PlayerFinanceAdapter и CapitalLedger | Complete (авто 2026-10-10, кусок 3: `Logic/IPlayerFinance` + `IRecoveryMoney`, `PlayerFinanceAdapter` (порог и `DebtLimit` только из `ModCredit.PlayerDebtLimit()` / `ThresholdOf(игрок)`, прямого чтения `CentralBank.DebtLimit` нет; свойство названо `PlayerDebtLimit`, чтобы не попасть под регулярное выражение теста ядра, `mod/requests/bankruptcy-recovery.md`, п. 6.1/7.2; `Inflation` из `InflationHandler.Instance`; отчёты ищутся по `Year`/`Month` через `ReportMonths`; `InterestMultiplier` — канал `bank.interest` только для игрока; `CancelDefault` и `defaultTimeLimit` через `AccessTools`), бэкенд `CapitalLedger` = `CapitalLedgerBackend` над чистым `CapitalLedgerCore` + `RecoveryJournal`: `CapitalIn/Out` двигают баланс через `Company.AddMoney` и не попадают в отчёт, `Expense` идёт через `SpendMoney`, `Revenue` через `EarnMoney` с нужным пунктом; суммы ≤ 0, `NaN`, `∞` отклоняются (предупреждение в `ModLog`); журнал — кольцо на 200 записей только для `bankruptcy-recovery`, вызовы других фич идут в `ModLog`; бэкенд регистрируется в конструкторе фичи и работает при выключенной фиче; `CapitalLedgerTests` (23, на `FakePlayerFinance`) зелёные; в игре не проверялось) |
| TSK-008 LoanService и BridgeLoanService | Complete (авто 2026-10-10, кусок 3: чистый `Logic/LoanBook` + обёртки `Services/LoanService`, `BridgeLoanService` (`Logic/BridgeLoanEngine`); `OnMonth` на `LateMonthPassed` списывает проценты (`Interest`, расход отчёта) и тело (`CapitalOut`) по каждому кредиту, даже в минус; последний платёж гасит остаток; закрытие → `LoanClosed` и `ModNotify`; кредит, открытый сегодня, платит со следующего месяца; досрочное погашение (`Prepay`) требует баланса; бридж-кредит: `CanTakeBridge` + переключатель подфичи, `A_max = β·D_L + γ·R_3`, зачисление `A` через `CapitalIn`, комиссия 5 % через `Expense(Interest)`, аннуитет 12 мес. под 30 % × `bank.interest`; при выключенной подфиче новые кредиты не выдаются, существующие обслуживаются; состояние — в секции сохранения (`RecoveryState`); `D_L` = `CreditLineMath.LimitBase`; `LoanBookTests` (22), `BridgeLoanEngineTests` (15) с примерами AC-6.1…AC-6.3, AC-10.2, AC-12.4; события `RecoveryApi.LoanOpened/LoanClosed` поднимаются; в игре не проверялось) |
| TSK-009 StartupCreditLineService | Complete (авто 2026-10-10, кусок 3: чистый `Logic/CreditLineEngine` + обёртка `Services/StartupCreditLineService`; линия открывается на `GameReady` (новая игра после перемотки, загрузка, старый сейв — на остаток срока), при включении фичи и при включении подфичи; закрытая линия не открывается повторно; `LateTick`: автозаём (дёшево, пока баланс далеко от порога); `LateMonthPassed`: проценты одной строкой `Interest`, затем гашение 25 % положительного баланса, затем при `today ≥ closesOn` остаток → срочный кредит 24 мес. под 15 % через `LoanBook`; при выключенной подфиче новые выборки и открытие недоступны, выборка обслуживается; тики не идут во время перемотки и загрузки (`TickOptions.None`); `CreditLineEngineTests` (37) с AC-7.1…AC-7.6, `RecoveryLifecycleTests` (5: три года линии, автозаём годами, сейв посреди жизни); в игре не проверялось) |
| TSK-010 Патчи и самопроверка | Complete (авто 2026-10-10, кусок 3: `Patches/BankruptcyPatches` (P1 prefix `TickBankruptcies` с Harmony `Priority.High` и `return true`, P2 postfix того же метода, P3 postfix `TriggerDefault`, P4 postfix `BankruptcyWarningWindow.Open` с `Optional = true`), каждый патч — `ModFeatures.IsActive` и `ModHooks.Guard`; решения принимает чистый `DefaultCycleEngine` (удержание по `MustHoldForBailout` на живом пороге `ModCredit`, серия досрочного выхода, сброс флагов эпизода); P3 — только для игрока → `RecoveryMonitor.OnPlayerDefault`; P4 → `WarningWindowButton` (клон `ModUI.Button`, один раз за сцену); самопроверка `RecoverySelfCheck` (20 членов игры, список `RecoveryTargets`, строка итога и по строке `WARN` в `Player.log`; нехватка обязательного члена в `Register` делает фичу `Unavailable`), 20 имён проверены офлайн по `Assembly-CSharp.dll`; ядро резервирует на `TickBankruptcies` только transpiler (prefix и postfix допустимы; проверено по `ReservedTargets`); `DefaultCycleEngineTests` (21), `RecoveryTargetsTests` (13), `RecoveryWiringTests` (12). **Не проверено (нужна игра, владелец):** применение prefix и postfix вместе с P10 (покажет самопроверка ядра), положение кнопки в окне предупреждения (сценарий M2); `RecoveryMonitor` — шов, письма делает TSK-015) |
| TSK-011 BailoutService | Complete (авто 2026-10-10, кусок 3: чистый `Logic/BailoutEngine` + обёртка `Services/BailoutService`; предложение открывается один раз на запрос P1, в пакетном прогоне — после `ModTicks.BatchEnded` при непрерывном удержании; «Кредит спасения» — `CapitalIn(B)`, кредит 36 мес. под 40 % × `bank.interest`; «Доля инвестора» — `CapitalIn(B)`, ежемесячно 30 % положительной прибыли прошлого месяца (по `Year`/`Month`) по статье из контента (`Royalties`), 60 мес., потолок `3·B`; в обоих: `CancelDefault` ванильным методом, `bailoutsUsed++`, `ModReputation.Add(игрок, −10, "rec.reputation.bailout")`, событие `BailoutAccepted`; лимит `[2,1,1,1,1,0]`; отказ ставит `bailoutDeclined`, следующий тик ванильный; крестик не отказ; окно не построилось или не задано → счётчик не удерживается; флаг «предложение открыто» не сохраняется; флаг assisted не ставится; второй договор инвестора не предлагается, пока идёт первый; `BailoutEngineTests` (31) с AC-8.1…AC-8.6. **Зависимость:** окно предложения — TSK-015 (`BailoutService.Presenter`); пока его нет, предложение не делается и банкротство ванильное) |
| TSK-012 AssetSaleService | Complete (авто 2026-10-10, кусок 3: `Services/AssetSaleService` + чистый `Logic/AssetMath`; превью линий = `ProductionMath.SaleCost(N, W)`, продажа = цена до снятия (как в игре) + `Company.RemoveProductionLines` + доход «Sales» через `CapitalLedger.Revenue` + `ManageLinesWindow.UpdateWafers()`; минимум линий из `assets.minLinesLeft` (1); предупреждение, если мощность после продажи < `ContractHelper.ContractedProductionLines(Player.Instance)`; склад — `StorageMath.Income`/`SellStock`; снятие — штраф `FinesFromContracts` и доход склада в превью, `Cpu.TriggerRetirement()`; список CPU без снятых, по возрастанию дневной маржи; «Продать производство» неактивна при `IsFoundry`, 0 линий или сложности 5, иначе открывает `ManageLinesWindow`; все продажи пишутся в журнал; `AssetMathTests` (14); в игре не проверялось) |
| TSK-013 Меню: сводка и активы | Complete (авто 2026-10-10, кусок 4: `UI/RecoveryWindow.cs` — модальное окно `ModUI.CreateWindow(…, WindowTemplate.Modal, "rec.menu.title", 780x640)` с четырьмя вкладками; содержимое вкладки строится при каждом открытии и после каждой операции в одной прокручиваемой области, время сборки пишется в отладочный лог (`menu: the tab … was built in N ms`), тема — по `ModUI.ThemeChanged`. **Сводка**: шесть строк `SummaryModel` (баланс, порог −D, дефицит или запас до порога, «До банкротства: N дн.» / «Не в дефолте», долг по кредитам мода, ближайший платёж с датой) и три быстрых действия с причиной недоступности. **Активы**: линии (`DropdownUI` пластины, `SliderUI` числа линий, превью выручки `SaleCost` и мощности «X -> Y», предупреждение о контрактах, «Продать»), склад и CPU (по возрастанию дневной маржи; «Распродать склад», «Снять с производства» со штрафом), производство (стоимость подразделения, «Продать производство» открывает `ManageLinesWindow`). Каждая операция проходит два шага: настройка и превью на вкладке, затем `ModUI.Confirm` с суммой, побочными эффектами и балансом после (`ConfirmPlanner`); своего диалога нет; недоступные кнопки неактивны, причина — в `ModUI.Tooltip` и строкой под кнопкой. Чистая логика: `IMenuText`, `SummaryModel`, `ConfirmPlanner`; тесты `SummaryModelTests` (7), `ConfirmPlannerTests` (13), `RecoveryUiSeamsTests`. **Не проверено (нужна игра, владелец):** раскладка и размеры окна, время сборки вкладки ≤ 50 мс, пауза и смена темы в живой игре, выпадающий список внутри прокрутки (M1–M5); подробности — `mod/requests/bankruptcy-recovery.md`, п. 8) |
| TSK-014 Меню: кредиты и журнал | Complete (авто 2026-10-10, кусок 4: `UI/RecoveryWindow.Loans.cs` — таблица кредитов (вид, остаток, ставка с учётом `bank.interest`, платёж, месяцев осталось, «Погасить досрочно» целиком; неактивна, если баланс меньше остатка), строка договора с инвестором; блок кредитной линии (лимит/выбрано/свободно/дата закрытия, слайдер доли суммы, «Взять», «Погасить», переключатель «Автозаём», причины отказа); блок бридж-кредита (слайдер 25–100 % от `A_max`, условия `rec.bridge.terms`, комиссия, «Взять»; при отказе кнопка неактивна с причиной — AC-2.3). `UI/RecoveryWindow.Journal.cs` — 50 последних записей журнала, новые сверху, с именем операции (в том числе `disable.convert`). Чистая логика: `JournalLines`, `AmountSteps`; тесты `JournalLinesTests` (11, включая проверку, что каждый код журнала из исходников имеет имя в ru и en), `AmountStepsTests` (7). Отличия от design: сумма на слайдере — доля лимита (слайдер ядра показывает своё число), досрочное погашение только целиком (`LoanBook.Prepay` умеет и часть) — `mod/requests/bankruptcy-recovery.md`, п. 8.2. **Не проверено (нужна игра, владелец):** ширины колонок, прокрутка, подсказки неактивных кнопок (M5–M7)) |
| TSK-015 Последний шанс, кнопка, хаб, письма | Complete (авто 2026-10-10, кусок 4: `UI/BailoutOfferWindow.cs` — модальное окно, `IBailoutPresenter` движка: сумма `B`, баланс после, условия обоих вариантов, «Отказаться — объявить банкротство» (с вопросом `ModUI.Confirm`: банкротство заканчивает игру); крестик не отказ (`Dismissed`, предложение вернётся на следующем тике); «Доля инвестора» неактивна с причиной, пока идёт договор; `Register` присваивает `Bailout.Presenter`, так что предложение теперь делается. `Services/RecoveryMonitor.cs` — `RecoveryLetters` на `ModMail` (приоритет `Critical`, теги `rec:prewarning` и `rec:default`, многоразовое действие «Меню спасения», `ModMail.RegisterActionHandler`; id письма нигде не хранится, `ModMail.Find` ищет письмо вида, ещё лежащее в очереди) и `RecoveryMonitor` над чистым `RecoveryMonitorEngine` (предупреждение раз в день на `LateTick` по `RecoveryRules.PreWarning`, письмо дефолта из P3, флаги эпизода в сейве, повтор отправки, если ядро письмо не приняло). `UI/RecoveryHubPage.cs` — страница «Финансы» (`ctx.HubPage`): сводка и кнопка «Меню спасения» (хаб закрывается, меню открывается). Кнопка в `BankruptcyWarningWindow` (TSK-010) теперь открывает меню. Тесты: `OfferTermsTests` (6, пример 1977: B = 36 075 000, кредит 36 мес. под 40 %, доля 30 %/60 мес./потолок 3B), `RecoveryMonitorEngineTests` (19, AC-1.1…AC-1.3), `RecoveryUiSeamsTests` (разбор исходников). **Не проверено (нужна игра, владелец):** открытие окна из P1 посреди тика, пауза, действие письма поверх окна почты, место и размер кнопки в окне предупреждения (M2–M4)) |
| TSK-016 Локализация ru/en | Complete (авто 2026-10-10, кусок 4: `L10n/ru/bankruptcy-recovery.json` и `L10n/en/bankruptcy-recovery.json` — 185 ключей `rec.*` (окна, подтверждения, письма, уведомления, имена операций журнала, подфичи), все с префиксом `rec.`, наборы ключей и плейсхолдеры ru и en совпадают; `python install.py --check` — 0 ошибок и предупреждений; в коде фичи нет набранного текста (тесты `RecoveryUiSeamsTests`: нет кириллицы, ни одна строка-предложение не уходит в окна, письма и уведомления, каждый вызов `L("rec.…", …)` и `ShowKey` передаёт столько аргументов, сколько плейсхолдеров в строке); запасной язык en при отсутствии ключа в ru — работа `ModL10n` ядра. Ключи подтверждения выключения (`rec.disable.confirm`, `rec.disable.defaultRisk`, `rec.disable.confirmLabel`, `rec.notify.converted`) уже в таблицах; код, который их использует, — TSK-022 (зависимость TSK-016 от TSK-022 в списке выше означает это)) |
| TSK-017 Тесты сохранения | Complete (авто 2026-10-10, кусок 5: `RecoveryStateSerializationTests` (17 тестов) и `Data/sample.ptmod.json` — пример из design с `_schema`, заголовком и чужим разделом `rival-acquisitions`; файлы лежат в `mod/tests/PTMod.Tests/Features/BankruptcyRecovery/` (не в `…/BankruptcyRecovery/` из Deliverables: так собирает `build_feature.py`). Проверено: обмен DTO ↔ `JsonObject` без потерь (в том числе числа вроде `1/3` и `0.1+0.2`) и без поля версии, секция из ровно восьми членов; путь через текст файла и настоящий `SideFileModel` ядра; сейв без секции даёт значения новой игры; состояния линии `none`/`open`/`closed` пишутся и читаются; битые дата и сумма (`openedOn`, `outstanding`, `payment`, `annualRate`, `drawn`, дата журнала, `lastBridgeClosedOn`, доля инвестора, значение не того типа) отбрасывают свою запись и дают по одной строке для `ModLog`, остальное читается; пример бокового файла читается без замечаний и пишется обратно как был; чужая секция (живой участник и неизвестная) не теряется и не затрагивается ни переводом, ни сбросом нашей; состояние после перевода в обычный долг — те же восемь членов и читается без замечаний; сейв без отметок перевода ничего не применяет. Случаи `_schema` (нет поля, новее) — у ядра (mod-framework TSK-016); новее — одним тестом `RecoverySaveSectionTests` из куска 1) |
| TSK-018 Тесты изоляции ИИ и самопроверка | Complete (авто 2026-10-10, кусок 5: `AiIsolationTests` (15) и `SelfCheckIntegrationTests` (14). Правила `MustHoldForBailout`, `PreWarning`, `NextRecoveryStreak`, `CanTakeBridge` для `IsPlayer = false` ничего не делают (с контролем для игрока); при отсутствии компании игрока все движки (цикл дефолта, письма, спасение, кредиты, линия, бридж-кредит, перевод) не двигают деньги, счётчик дефолта, окна, письма, репутацию и состояние; ИИ в дефолте на 119-й день не виден движкам (AC-11.1); `CapitalLedgerCore` отказывает всем четырём операциям без компании игрока, в том числе чужим фичам, и не пишет журнал. `PlayerFinanceAdapter.PlayerCompany()` теперь отдаёт только компанию с `IsPlayer = true`; разбором исходников: к компании ведёт один путь (`.Company` только в адаптере), счётчик дефолта пишется только через `IPlayerFinance`, репутация меняется только игроку, P3 отсекает ИИ первой строкой, P1/P2 не принимают компаний и не обходят их. Самопроверка: `TargetReport.Entries`/`TargetEntry.Line`/`FailureText`; `RecoverySelfCheck.Run` пишет в `Player.log` по строке на P1–P4 и каждый член рефлексии (`self-check: [P1, P2] BankruptcyHandler.TickBankruptcies(): found` и далее), остальные цели на уровне Debug, пропавшие — `WARN`; `RecoverySelfCheck.VerifyPatches()` из `OnEnabled` пишет, применены ли P1–P4 (`ModHooks.IsApplied`). Сломанная цель в фейковом реестре на `FeatureStateMachine` ядра делает фичу `Unavailable` без исключения наружу, для каждой из 20 целей (необязательна только кнопка P4). Не проверено (нужна игра): сами строки `Player.log` и реальное применение патчей) |
| TSK-019 Отладочные действия | Pending |
| TSK-020 Ручная проверка в игре (владелец) | Pending |
| TSK-021 Документация | Частично (авто 2026-10-10, кусок 5: текст подраздела «Финансы и спасение от банкротства» для `README.md` готов в `mod/requests/bankruptcy-recovery.md`, п. 9.6 — все опции меню и их цена, линия, бридж-кредит, последний шанс, досрочный выход, настройки, выключение фичи с переводом остатка в обычный долг, предупреждения «выключайте фичу в окне «Моды»» и «перед удалением мода выключите фичу», боковой файл `.ptmod.json`, ссылки на спеку. `README.md` не входит в файлы этого куска: вставку делает интегратор (п. 9.5); предложение для `install.py --remove-mods` записано там же. Цифры и тексты надо сверить с игрой (TSK-020)) |
| TSK-022 Выключение фичи: подтверждение и перевод в долг | Complete (авто 2026-10-10, кусок 5: чистые `Logic/DisableSettlementEngine` (`Quote`, `Settle`, `DisablePlan`, `SettleResult`) и `Logic/SettlementMarks`, обёртка `Services/DisableSettlementService` (`BuildConfirmation`, `Settle`), `SaveMarks`; в `BankruptcyRecoveryFeature`: `ctx.ConfirmBeforeDisable`, `OnDisabled` = окна, `Settle("disabled")`, сохранение состояния для включения; `OnGameReady`, `ModSave.AfterRead`, `ModFeatures.StateChanged` — применение отметок. Провайдер даёт `null` без обязательств и `DisableConfirmation` (`rec.disable.confirm`: сумма, баланс после, предупреждение о дефолте; кнопка `rec.disable.confirmLabel`) — с обязательствами; опрос ничего не меняет. `Settle` (одинаково по «Да» в хабе и по `SetEnabled(false)` из кода, без вопроса): ОДНА операция `CapitalOut(Total)` (код `disable.convert`, запись журнала делает `CapitalLedger`), кредиты закрыты, линия с долгом `closed`/`disabled`, договор с инвестором закрыт, `lastBridgeClosedOn` = сегодня, если был бридж-кредит, уведомление `rec.notify.converted`; идемпотентна; баланс не растёт никогда; без игрока или леджера ничего не меняет. Повторное включение не восстанавливает кредиты, линию и договор, счётчик спасений и пауза бридж-кредита сохраняются: ядро при включении отдаёт участнику копию секции последней записи (или пустую), поэтому добавлены отметки в флагах сейва (`settled*`) и сохранённое состояние на один сеанс, `mod/requests/bankruptcy-recovery.md`, п. 9.2 (там же просьба к ядру). Тесты: `DisableSettlementEngineTests` (26: AC-12.2, AC-12.6), `SettlementMarksTests` (22), `DisableFlowTests` (15, на настоящем `SideFileModel`: тот же сеанс, сохранение при выключенной фиче, перезапуск, тот же сейв загружен заново, новая игра, другой сейв), `DisableWiringTests` (10); `DisableSettlementTests` (TSK-006) зелёные. **Отступления:** (1) перевод при `GameReady(Loaded)` для фичи `Disabled`/`Unavailable` (FR-12 п. 5, AC-12.5) из фичи сделать нельзя: ядро не вызывает код неактивной фичи и не отдаёт её секцию (п. 9.3); зафиксировано как ограничение (Acceptance это допускает), текст для README — п. 9.6; (2) линия без долга при выключении остаётся открытой (закрывается линия с долгом), п. 9.4. **Не проверено (нужна игра, владелец):** сценарий M10 и проверки п. 9.7) |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-006, TSK-015, TSK-020 |
| FR-2 | TSK-003, TSK-010, TSK-013, TSK-015 |
| FR-3 | TSK-012, TSK-013 |
| FR-4 | TSK-012, TSK-013 |
| FR-5 | TSK-012, TSK-013 |
| FR-6 | TSK-004, TSK-006, TSK-007, TSK-008, TSK-014 |
| FR-7 | TSK-004, TSK-005, TSK-007, TSK-009, TSK-014 |
| FR-8 | TSK-003, TSK-004, TSK-006, TSK-010, TSK-011, TSK-015 |
| FR-9 | TSK-003, TSK-006, TSK-010 |
| FR-10 | TSK-007, TSK-008, TSK-014 |
| FR-11 | TSK-001, TSK-006, TSK-007, TSK-010, TSK-018 |
| FR-12 | TSK-001, TSK-002, TSK-006, TSK-008, TSK-017, TSK-022 |
| NFR-1…NFR-7 | TSK-002, TSK-013, TSK-016, TSK-017, TSK-018, TSK-020, TSK-021, TSK-022 |
