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

- [ ] **TSK-001**: Каркас фичи `[ModFeature]` и участник сейва
  - **Requirement**: FR-11, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/BankruptcyRecoveryFeature.cs` (`IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn` пустой, `EnabledByDefault = true`, `SupportsHotToggle = true`, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `mod/src/PTMod/Features/BankruptcyRecovery/RecoveryApi.cs` (заглушки), `mod/src/PTMod/Features/BankruptcyRecovery/RecoverySaveParticipant.cs` (`IModSaveParticipant`), `mod/src/PTMod/Features/BankruptcyRecovery/Logic/Model/RecoveryState.cs` (DTO без поля версии)
  - **Acceptance**: фича с ID `bankruptcy-recovery` видна в хабе «Моды» (`ModFeatures.All`); выключается и включается; пять `ParamSpec` подфич (`preWarning`, `startupCredit`, `bridgeLoan`, `bailout`, `earlyExit`, `ParamType.Bool`, по умолчанию `true`) видны на странице фичи; пустая секция пишется в боковой файл и читается обратно, поле `_schema` в секции добавляет ядро; без секции — состояние по умолчанию; своих счётчиков ошибок и флагов `Enabled` нет
  - **Depends**: mod-framework/TSK-022 (`ModFeatures`, `FeatureContext`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`, `ModLog`), TSK-025 (`ModSave`), TSK-067 (мини-раннер)

- [ ] **TSK-002**: Контент баланса и валидатор
  - **Requirement**: FR-12, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/bankruptcy-recovery/balance.json`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/Model/RecoveryBalance.cs` (DTO + `Validate()`), `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryBalanceTests.cs`
  - **Acceptance**: JSON совпадает с примером из design (в том числе `limits.floorBase = 2 000 000`, `perGameByDifficulty = [2,1,1,1,1,0]`, `expenseOrigin = "Royalties"`); загрузка через `ModContent.LoadConfig`; каждое поле вне диапазона заменяется значением по умолчанию с записью в `ModLog`; массивы `*ByDifficulty` длиной ≠ 6 отклоняются целиком; тесты зелёные
  - **Depends**: TSK-001; mod-framework/TSK-015 (`ModContent`: слияние), TSK-021 (адаптер `ModContent`), TSK-067

- [ ] **TSK-003**: Спайк — приватные цели `BankruptcyHandler`, совместимость с P10 ядра, кнопка в окне предупреждения, реальный `DebtLimit`
  - **Requirement**: FR-2, FR-8, FR-9
  - **Deliverables**: временный отладочный код в `mod/src/PTMod/Features/BankruptcyRecovery/Debug/SpikeProbe.cs` (сборка спайка — `python mod/build.py --src … --out …`, удаляется после спайка); раздел «Результаты спайка» в `design.md`
  - **Acceptance**: в `Player.log` есть значения `CentralBank.DebtLimit` и `ModCredit.PlayerDebtLimit()` на 1975-01-01, 1980-01-01, 1985-01-01 новой игры; `AccessTools` находит `BankruptcyHandler.CancelDefault(ICompany)` и поле `defaultTimeLimit` (= 120); вызов `CancelDefault(игрок)` убирает баннер дефолта; prefix и postfix на `BankruptcyHandler.TickBankruptcies` применяются вместе с transpiler'ом P10 ядра (`ModHooks` не отклоняет, порог читается из `ModCredit.ThresholdOf`); в `BankruptcyWarningWindow` удаётся добавить кнопку клоном `ButtonUI`. Если `DebtLimit` = 1 000 000 всю игру — зафиксировать влияние на пол `limits.floorBase` и скорректировать его в `balance.json` до TSK-009
  - **Depends**: TSK-001; mod-framework/TSK-006 (Harmony-спайк), TSK-023, TSK-031 (P9–P11), TSK-058 (`ModCredit`), TSK-035 (`ModWindow`)

## Фаза 1 — Чистая логика

- [ ] **TSK-004**: `LoanMath` — аннуитет, месячный шаг, месячный процент
  - **Requirement**: FR-6, FR-7, FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/LoanMath.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/LoanMathTests.cs`
  - **Acceptance**: `AnnuityPayment(8 400 000, 0,30, 12) = 818 892 ± 1`; при ставке 0 платёж `A/n`; сумма тел за весь график = `A` (± 1 $); последний шаг гасит остаток; `MonthlyInterest(2 660 000, 0,10) = 22 166,67 ± 0,01`; расчёт в `double`; тип в `[Covers]`
  - **Depends**: TSK-001; mod-framework/TSK-067

- [ ] **TSK-005**: `CreditLineMath` — база лимитов, лимит, автозаём, гашение, окно срока
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/CreditLineMath.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/CreditLineMathTests.cs`
  - **Acceptance**: `LimitBase = max(D, floorBase × I)` (пример: `D = 1 млн`, `I = 2,0`, `floorBase = 2 млн` → 4 млн); лимит для 6 уровней сложности по формуле design; верх `3·D_L`; пример «−7,7 млн → автозаём 2,66 млн» при `D = 8,4 млн`; автозаём ≤ свободного лимита и 0 при балансе выше триггера; `Sweep` = 0 при балансе ≤ 0; `IsWindowOpen` верно на границах (день основания, +3 года − 1 день, +3 года)
  - **Depends**: TSK-002

- [ ] **TSK-006**: `RecoveryRules`, `BailoutMath` и `DisableSettlement`
  - **Requirement**: FR-1, FR-6, FR-8, FR-9, FR-11, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Logic/RecoveryRules.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/BailoutMath.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Logic/DisableSettlement.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryRulesTests.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/BailoutMathTests.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/DisableSettlementTests.cs`
  - **Acceptance**: `MustHoldForBailout` = `true` только для игрока в дефолте с `DaysInDefault + 1 ≥ T` и `money < порог`, при `bailoutsUsed < bailoutsAllowed` и без отказа; для ИИ — всегда `false`; на сложности 5 (`Allowed = 0`) — `false`; `BailoutMath.Allowed` отдаёт `[2,1,1,1,1,0]`; предупреждение: отправка → без повтора → сброс выше `−0,5·D` → повторная отправка; серия выхода считает и обнуляется; `CanTakeBridge` учитывает активный кредит и кулдаун 180 дней; `BailoutMath.Amount` для примера 1977 = 36 075 000; платёж инвестору при убытке 0 и не выше потолка; `DisableSettlement.Total` = остатки кредитов + выборка по линии + `cap − paid`, для пустого состояния 0
  - **Depends**: TSK-002

## Фаза 2 — Адаптеры, сервисы, патчи

- [ ] **TSK-007**: `PlayerFinanceAdapter` и `CapitalLedger`
  - **Requirement**: FR-6, FR-7, FR-8, FR-10, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/PlayerFinanceAdapter.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/CapitalLedger.cs`, `mod/tests/PTMod.Tests/BankruptcyRecovery/CapitalLedgerTests.cs` (на `FakePlayerFinance`)
  - **Acceptance**: `DebtLimit` и `Threshold` берутся из `ModCredit.PlayerDebtLimit()` / `ThresholdOf(игрок)` (прямого чтения `CentralBank.DebtLimit` нет); `CapitalIn/Out(featureId, …)` двигают баланс через `Company.AddMoney` и не попадают в отчёт месяца; `Expense` идёт через `Company.SpendMoney` с нужной `ExpenseOrigin`; суммы ≤ 0, `NaN`, `∞` отклоняются с `ModLog`; журнал — кольцевой на 200 записей и только для `featureId = "bankruptcy-recovery"` (вызовы `rival-acquisitions` пишутся лишь в `ModLog`); `SalesRevenueLastFullMonths(3)` ищет отчёты по `Year`/`Month`; `InterestMultiplier` берётся из канала `bank.interest` только для игрока; `Inflation` читается из `InflationHandler.Instance`
  - **Depends**: TSK-003; mod-framework/TSK-027 (`ModModifiers`), TSK-058 (`ModCredit`), TSK-021 (`ModLog`)

- [ ] **TSK-008**: `LoanService` и `BridgeLoanService`
  - **Requirement**: FR-6, FR-10, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/LoanService.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Services/BridgeLoanService.cs`
  - **Acceptance**: `OnMonth` (через `ctx.OnTick(TickPhase.LateMonthPassed, …)`) списывает проценты (`Interest`) и тело (`CapitalOut`) по каждому кредиту; закрытие кредита → `LoanClosed` и `ModNotify`; досрочное погашение работает при достаточном балансе; бридж-кредит: проверка `CanTakeBridge`, зачисление `A` (`A_max` от `D_L`), комиссия 5 %, кредит 12 мес.; при выключенной подфиче `BridgeLoan` новые кредиты не выдаются, а существующие обслуживаются; состояние пишется через `RecoverySaveParticipant`; вычисление `D_L` — `CreditLineMath.LimitBase`
  - **Depends**: TSK-004, TSK-006, TSK-007; mod-framework/TSK-024 (`ModTicks`)

- [ ] **TSK-009**: `StartupCreditLineService`
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/StartupCreditLineService.cs`
  - **Acceptance**: линия открывается на `ModTicks.GameReady(NewGame)` (после перемотки) сроком 3 года; при `GameReady(Loaded)` и при включении фичи — на остаток срока, если состояние «не открыта» и с основания прошло < 3 лет; закрытая линия (`closedReason`) повторно не открывается; в `LateTick` ежедневно: лимит, автозаём (если включён); в `LateMonthPassed`: проценты `drawn·r·M/12` одной строкой `Interest`, затем гашение 25 % положительного баланса, затем при `сегодня ≥ closesOn` остаток → срочный кредит 24 мес. под 15 % через `LoanService`; при `StartupCredit = false` новые выборки и открытие недоступны, существующая выборка обслуживается; во время перемотки и загрузки ничего не делает
  - **Depends**: TSK-005, TSK-007, TSK-008; mod-framework/TSK-024

- [ ] **TSK-010**: Патчи `BankruptcyHandler` и `BankruptcyWarningWindow`, самопроверка
  - **Requirement**: FR-2, FR-8, FR-9, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Patches/BankruptcyPatches.cs` (P1–P4 из design, `[ModPatch]`, подключение `ctx.Patch`), регистрация целей самопроверки
  - **Acceptance**: P1 удерживает `DaysInDefault` игрока по `MustHoldForBailout` (порог — `ModCredit.ThresholdOf`) и ничего не делает для ИИ; P2 вызывает `CancelDefault(игрок)` после `streakDays` дней подряд выше порога; P3 вызывает `RecoveryMonitor.OnPlayerDefault` только для игрока; P4 добавляет кнопку один раз за сцену; каждый патч — в `ModHooks.Guard` с проверкой `ModFeatures.IsActive`, при исключении игра продолжает (счётчик и `Faulted` ведёт ядро); `ModHooks` принимает prefix/postfix на `TickBankruptcies` (резерв ядра — только transpiler); самопроверка перечисляет все цели из design и пишет результат в `Player.log`
  - **Depends**: TSK-003, TSK-006, TSK-007; mod-framework/TSK-023 (`ModHooks`)

- [ ] **TSK-011**: `BailoutService` — предложение, принятие, отказ, доля инвестора
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/BailoutService.cs`
  - **Acceptance**: предложение открывается один раз на запрос P1, а в пакетном прогоне (`ModTicks.IsBatchRun`) — после `ModTicks.BatchEnded` при непрерывном удержании счётчика; «Кредит спасения» — `CapitalIn(B)`, `CancelDefault`, кредит 36 мес. под 40 % × `bank.interest`; «Доля инвестора» — `CapitalIn(B)`, `CancelDefault`, ежемесячная выплата 30 % положительной прибыли прошлого месяца (по `Year`/`Month`) через статью из контента (`Royalties`), 60 мес., потолок `3·B`; в обоих вариантах `bailoutsUsed++` и `ModReputation.Add(игрок, −10, "rec.reputation.bailout")`; лимит спасений по сложности `[2,1,1,1,1,0]`; отказ ставит `bailoutDeclined`, и следующий тик идёт ванильно; флаг «предложение открыто» не сохраняется; флаг assisted не ставится
  - **Depends**: TSK-008, TSK-010; mod-framework/TSK-026 (`ModReputation`), TSK-057 (`IsBatchRun`, `BatchEnded`)

- [ ] **TSK-012**: `AssetSaleService` — линии, склад, снятие CPU, выход в fabless
  - **Requirement**: FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/Services/AssetSaleService.cs`
  - **Acceptance**: превью линий = `ProductionMath.SaleCost(N, W)`; продажа = `Company.RemoveProductionLines` + `EarnMoney(Sales)` + `ManageLinesWindow.UpdateWafers()`; минимум 1 линия; предупреждение, если мощность после продажи < `ContractHelper.ContractedProductionLines(Player.Instance)`; склад — `StorageMath.Income`/`SellStock`; снятие — штраф `ContractHelper.FinesFromContracts(cpu)` в превью и `Cpu.TriggerRetirement()`; список CPU без снятых, по возрастанию дневной маржи; «Продать производство» неактивна при `IsFoundry`, 0 линий или `DifficultyLevel = 5`, иначе открывает `ManageLinesWindow`
  - **Depends**: TSK-007

## Фаза 3 — UI

- [ ] **TSK-013**: Меню спасения — вкладки «Сводка» и «Активы»
  - **Requirement**: FR-2, FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.cs`
  - **Acceptance**: окно модальное (`ModUI.CreateWindow`, `PauseTrigger`), тема переключается вместе с игрой; сводка показывает 6 строк из design; на «Активах» каждая операция проходит двухшаговое подтверждение через `ModUI.Confirm` с суммой и балансом после (собственного диалога подтверждения нет); недоступные опции неактивны с причиной (`ModUI.Tooltip`); окно строится ≤ 50 мс (лог отладки)
  - **Depends**: TSK-012; mod-framework/TSK-035 (`ModWindow`), TSK-036 (виджеты), TSK-064 (`ModUI.Confirm`, `ModUI.Tooltip`)

- [ ] **TSK-014**: Меню спасения — вкладки «Кредиты» и «Журнал»
  - **Requirement**: FR-6, FR-7, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.Loans.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryWindow.Journal.cs`
  - **Acceptance**: таблица кредитов (вид, остаток, ставка, платёж, месяцев осталось, «Погасить досрочно»); блок линии (лимит/выбрано/свободно/дата, слайдер суммы, «Взять», «Погасить», переключатель «Автозаём»); блок бридж-кредита (25–100 %, условия из `rec.bridge.terms`, «Взять»); журнал — 50 последних записей (включая записи `disable.convert`)
  - **Depends**: TSK-008, TSK-009, TSK-013

- [ ] **TSK-015**: Окно «Последний шанс», кнопка в окне предупреждения, страница хаба, письма, монитор предупреждения
  - **Requirement**: FR-1, FR-2, FR-8
  - **Deliverables**: `mod/src/PTMod/Features/BankruptcyRecovery/UI/BailoutOfferWindow.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/Services/RecoveryMonitor.cs`, `mod/src/PTMod/Features/BankruptcyRecovery/UI/RecoveryHubPage.cs` (`ctx.HubPage`, раздел «Финансы»)
  - **Acceptance**: окно предложения модальное, два варианта с условиями, «Отказаться»; закрытие крестиком не считается отказом; кнопка «Меню спасения» в `BankruptcyWarningWindow` открывает меню; письма «Риск дефолта» (`rec:prewarning`) и «Компания в дефолте» (`rec:default`) отправляются с приоритетом `Critical` и многоразовым `ModMailAction` «Меню спасения» (обработчик `ModMail.RegisterActionHandler`), не дублируются в эпизоде; ID письма не хранится (поиск `ModMail.Find`); страница фичи в хабе открывает меню в любой момент
  - **Depends**: TSK-010, TSK-011, TSK-013; mod-framework/TSK-033 (`ModNotify`), TSK-038 (`ModMailAction`, `RegisterActionHandler`), TSK-062 (приоритеты), TSK-039 (хаб)

- [ ] **TSK-016**: Локализация ru/en
  - **Requirement**: NFR-4 (все FR с UI)
  - **Deliverables**: `mod/src/PTMod/L10n/ru/bankruptcy-recovery.json`, `mod/src/PTMod/L10n/en/bankruptcy-recovery.json` (ключи `rec.*` из design)
  - **Acceptance**: все строки окон, писем, уведомлений и подтверждения выключения берутся из `ModL10n`; нет жёстко прописанного текста в коде фичи (проверка поиском по каталогу фичи); все ключи начинаются с `rec.` (самопроверка ядра не предупреждает о чужом префиксе); при отсутствии ключа в `ru` показывается `en`; `install.py --check` не находит расхождений ru/en
  - **Depends**: TSK-013, TSK-014, TSK-015, TSK-022; mod-framework/TSK-043 (строки ядра), TSK-066 (`ModIds`)

## Фаза 4 — Проверка и документация

- [ ] **TSK-017**: Тесты сохранения и совместимости
  - **Requirement**: NFR-3, FR-12
  - **Deliverables**: `mod/tests/PTMod.Tests/BankruptcyRecovery/RecoveryStateSerializationTests.cs`, тестовый файл `mod/tests/PTMod.Tests/BankruptcyRecovery/Data/sample.ptmod.json` (пример из design)
  - **Acceptance**: round-trip DTO ↔ `JsonObject` без потерь и без поля версии; отсутствующая секция → значения по умолчанию; состояния линии `none`/`open`/`closed` читаются и пишутся; битые даты и суммы отброшены с `ModLog`; пример бокового файла из design читается; чужая секция в том же файле не теряется (через `ModSave`); случаи `_schema` (нет поля, новее) проверяет ядро (mod-framework/TSK-016)
  - **Depends**: TSK-008, TSK-009, TSK-011, TSK-022; mod-framework/TSK-016, TSK-025

- [ ] **TSK-018**: Тесты изоляции ИИ и интеграционная самопроверка
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

- [ ] **TSK-022**: Выключение фичи — подтверждение и перевод остатка в обычный долг
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
| TSK-001 Каркас фичи [ModFeature] и участник сейва | Pending |
| TSK-002 Контент баланса и валидатор | Pending |
| TSK-003 Спайк: приватные цели, совместимость с P10, DebtLimit | Pending |
| TSK-004 LoanMath | Pending |
| TSK-005 CreditLineMath | Pending |
| TSK-006 RecoveryRules, BailoutMath, DisableSettlement | Pending |
| TSK-007 PlayerFinanceAdapter и CapitalLedger | Pending |
| TSK-008 LoanService и BridgeLoanService | Pending |
| TSK-009 StartupCreditLineService | Pending |
| TSK-010 Патчи и самопроверка | Pending |
| TSK-011 BailoutService | Pending |
| TSK-012 AssetSaleService | Pending |
| TSK-013 Меню: сводка и активы | Pending |
| TSK-014 Меню: кредиты и журнал | Pending |
| TSK-015 Последний шанс, кнопка, хаб, письма | Pending |
| TSK-016 Локализация ru/en | Pending |
| TSK-017 Тесты сохранения | Pending |
| TSK-018 Тесты изоляции ИИ и самопроверка | Pending |
| TSK-019 Отладочные действия | Pending |
| TSK-020 Ручная проверка в игре (владелец) | Pending |
| TSK-021 Документация | Pending |
| TSK-022 Выключение фичи: подтверждение и перевод в долг | Pending |

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
