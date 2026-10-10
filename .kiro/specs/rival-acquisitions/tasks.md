# Tasks: rival-acquisitions

## Document Information

- **Feature Name**: rival-acquisitions (I06)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/rival-acquisitions/requirements.md`
  - Design: `.kiro/specs/rival-acquisitions/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`
  - Зависимости: `.kiro/specs/mod-framework/` (Core; номера задач взяты из его `tasks.md` v0.2, запись `mod-framework/TSK-0xx`), `.kiro/specs/bankruptcy-recovery/` (`CapitalLedger` — bankruptcy-recovery/TSK-007; общий таргет `BankruptcyHandler.TriggerDefault` — bankruptcy-recovery/TSK-010). Мягкие связи (вызовы под `ModFeatures.IsActive`, в `DependsOn` не входят): `cpu-architectures` (`ArchitectureApi.TransferCompanyState`), `gpu-product-line` (`GpuApi.TransferVendorProducts`).

## Implementation Overview

Сначала два спайка на тестовом сейве: перенос CPU-семейств и производственных связей (TSK-003) и перенос технологий/линий с оповещением о банкротстве (TSK-004). От их результатов зависит FR-8/FR-9. Затем чистая логика (снимки, оценка, правила, план, доли) по тестам мини-раннера, исполнитель сделок по шагам из design, хуки банкротства ИИ, UI, передача состояния смежных фич и проверка.

### Implementation Strategy

- «План, потом исполнение»: `AcquisitionPlanBuilder` — чистая функция; `AcquisitionExecutor` только выполняет план, оплата — последним шагом после выведения цели (D8).
- Объект `AICompany`, его `UniqueID`, `SaveID`, `Name` не трогаются; цель выводится `AICompany.TriggerBankruptcy` (D1).
- Все денежные операции игрока — через `CapitalLedger` из `bankruptcy-recovery` (D9) с `featureId = "rival-acquisitions"`.
- Точка входа — `[ModFeature] RivalAcquisitionsFeature : IModFeature`; тики — `ctx.OnTick`; старт — `ModTicks.GameReady`; состояние — `IModSaveParticipant` (поле версии секции не хранится: его ведёт ядро); перенос CPU — `ModEvents.RaiseCpuTransferred`; свои патчи на `Company.AddCpu` / `AICompany.AddCpu` и `EventBus.Fire` отсутствуют.
- Цена полных покупок — от капитала цели с нижней границей `C⁺ + E_min`; инвариант `NetCash < 0` проверяется перебором в тестах (TSK-007).

### Development Approach

- **Testing Strategy**: TDD для `ValuationMath`, `DealRules`, `AcquisitionPlanBuilder`, `StakeMath` и сериализации (`mod/tests/PTMod.Tests/RivalAcquisitions/`, мини-раннер ядра, `python mod/build.py --tests`; каждый публичный тип `Logic` в `[Covers]`); спайки — в игре на копии сейва; ручные сценарии M1–M9 (design) выполняет владелец.
- **Integration Strategy**: сначала сделки без CPU (линии + технологии), затем CPU и контракты после спайка, затем доли и поручения, затем передача состояния смежных фич.
- **Deployment Strategy**: `install.py --with-mods`; отладочные действия — только с флагом отладки.

Размер задач — 2–4 часа.

## Фаза 0 — Каркас и спайки

- [ ] **TSK-001**: Каркас фичи `[ModFeature]`, настройки и участник сейва
  - **Requirement**: FR-14, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsFeature.cs` (`IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn` пустой, `EnabledByDefault`, `SupportsHotToggle`, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsApi.cs` (заглушки), `mod/src/PTMod/Features/RivalAcquisitions/AcquisitionsSaveParticipant.cs` (`IModSaveParticipant`), `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/AcquisitionsState.cs` (DTO без поля версии)
  - **Acceptance**: фича `rival-acquisitions` видна в хабе (`ModFeatures.All`) и выключается/включается; секция пишется и читается (пустая), поле `_schema` в ней добавляет ядро; без секции — состояние по умолчанию; подписка на `BankruptcyHandler.OnCompanyWentBankrupt` ставится на `ModTicks.GameReady` и сбрасывается на `PTModHost.SceneLeaving`; своих счётчиков ошибок и флагов `Enabled` нет
  - **Depends**: mod-framework/TSK-022 (`ModFeatures`, `FeatureContext`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`, `ModLog`), TSK-024 (`ModTicks`), TSK-025 (`ModSave`), TSK-067 (мини-раннер)

- [ ] **TSK-002**: Контент `balance.json` и валидатор
  - **Requirement**: FR-14, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/rival-acquisitions/balance.json`, `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/AcquisitionsBalance.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/AcquisitionsBalanceTests.cs`
  - **Acceptance**: JSON совпадает с примером design; загрузка через `ModContent.LoadConfig`; поля вне диапазонов заменяются значениями по умолчанию с `ModLog`; `maxShare ≥ 0,5` отклоняется; варианты доли больше `maxShare` отбрасываются; `rescueTargetDebtLimitShare` вне (−1; 0] заменяется на −0,5; тесты зелёные
  - **Depends**: TSK-001; mod-framework/TSK-015 (`ModContent`: слияние), TSK-021 (адаптер `ModContent`), TSK-067

- [ ] **TSK-003**: Спайк — перенос CPU-семейства и производственных связей
  - **Requirement**: FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Debug/SpikeCpuTransfer.cs` (временный, сборка спайка — `python mod/build.py --src … --out …`, удаляется после спайка); раздел «Результаты спайков» в `design.md`
  - **Acceptance**: на копии сейва перенос семейства (база + вариант) от ИИ к игроку по шагам 5 и 8 design: CPU в окне производства игрока, продаётся, OEM-контракты на нём выплачивают игроку; `ModEvents.RaiseCpuTransferred` поднимает `CpuTransferred` на каждый CPU и не поднимает `CpuReleased`; сохранение → загрузка: CPU у игрока, `Cpu.Base` найден, ошибок в `Player.log` нет; CPU, производившийся у литейщика без контракта, производится (или зафиксирован отказ → FR-8 п. 3 расширяется на такие CPU); контракт цели-клиента, переназначенный на игрока (`BusinessContract.Initialize(поставщик, игрок)`), работает, а CPU производится по контракту; сброс `Cpu.manufacturer`/`Cpu.currentContract` через рефлексию работает; вывод — FR-8 «как в design» или «снятие с компенсацией» (решение владельца)
  - **Depends**: TSK-001; mod-framework/TSK-006 (Harmony-спайк), TSK-023, TSK-056 (`ModEvents`)

- [ ] **TSK-004**: Спайк — технологии без окон, линии, флаг литейных услуг, оповещение о банкротстве
  - **Requirement**: FR-7, FR-9, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Debug/SpikeAssets.cs` (временный); раздел «Результаты спайков» в `design.md`
  - **Acceptance**: разблокировка 5+ технологий с зависимостями (`Progress = 1`, повтор `ResearchSector.Update(true)`) при `temporarilySupressPopup = true` — без окон «Исследование завершено»; `Company.ExpandFactory` на открытой пластине добавляет линии; перенесённый контракт поставщика (`BusinessContract.Initialize(игрок, клиент)`) производит CPU клиента на заводе игрока — с флагом `Divisions.FoundryServices` (включённым через `ProductionWindow.SetFoundryServicesToggle`) или без (зафиксировать); вызов делегата `OnCompanyWentBankrupt` через рефлексию для Intel приводит к ванильному «У Cyrix больше нет проблем с патентами!»; prefix на `AICompany.BankruptcyWarning`, поднявший баланс выше `negativeBalanceLimit`, заставляет метод выйти без `ConsiderGoingFabless`, `SellProductionLinesToAvoidBankruptcy` и `CompanyEventHandler.TriggerSolarFoundriesSpinOff`, а `TickBankruptcies` вызывает `CancelDefault`
  - **Depends**: TSK-001; mod-framework/TSK-006, TSK-023

## Фаза 1 — Чистая логика

- [ ] **TSK-005**: Снимки состояния и адаптер игры
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/Snapshots.cs` (`CompanySnapshot`, `CpuSnapshot`, `ContractSnapshot`, `PlayerSnapshot` и др.), `mod/src/PTMod/Features/RivalAcquisitions/GameSnapshotAdapter.cs`
  - **Acceptance**: адаптер заполняет все поля снимка из источников, перечисленных в design (деньги, порог цели `ModCredit.DebtLimitOf`, линии по пластинам, `ManufacturingDivisionValue`, разрыв и частичный прогресс технологий, опыт узлов, CPU с выручкой за 12 мес. (`CpuBase.MarketData.Income`), контракты поставщика и клиента, флаг `Divisions.FoundryServices`, доля рынка); статус компании определяется по `IsBankrupt`, `IsInDefault`, аукционам и журналу; снимок 13 компаний строится ≤ 50 мс (лог отладки)
  - **Depends**: TSK-001; mod-framework/TSK-058 (`ModCredit`), TSK-026 (`ModCompanies`)

- [ ] **TSK-006**: `ValuationMath` и тесты
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/ValuationMath.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/ValuationMathTests.cs`
  - **Acceptance**: пример 1985 из design даёт `E = 434,6 млн ± 0,1 млн`; пустая компания → `E_min`; маржа ограничена [0; 1]; CPU снимка банкрота × 0,5; для банкрота `s = 0`; расчёт в `double`
  - **Depends**: TSK-002, TSK-005

- [ ] **TSK-007**: `DealRules` и тесты (в том числе инвариант `NetCash < 0`)
  - **Requirement**: FR-2, FR-3, FR-4, FR-5, FR-6, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/DealRules.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/DealRulesTests.cs`
  - **Acceptance**: доступность каждой сделки по статусу цели (здорова, дефолт, аукцион, аукцион истёк, поглощена) и игрока (в дефолте — всё недоступно); цены и итоги из примеров design (696,9 / 304,2 / 141,8 млн; 927,8 и 1 027,8 млн для нижней границы `C⁺ + E_min`); множитель 1,75 для большей цели; кулдаун 365 дней с датой доступности; `minCashAfter`; учёт доли игрока `(1 − σ_p)`; канал `acquisition.price`; округление вверх до 1 000 $; причины недоступности — ключи L10n; **перебор сетки** `E`, `C`, `σ_p`, `Ch`, `D` (список в design, «Testing Strategy») подтверждает `NetCash < 0` для Distressed и Hostile
  - **Depends**: TSK-006

- [ ] **TSK-008**: `AcquisitionPlanBuilder` и тесты
  - **Requirement**: FR-7, FR-8, FR-9, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/AcquisitionPlanBuilder.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/AcquisitionPlanBuilderTests.cs`
  - **Acceptance**: план содержит разблокировки технологий, частичный прогресс, подъём опыта узлов, перемещения или продажи линий (с отображением отсутствующей пластины на меньшую), семейства CPU (база первой, снятая база включена), CPU под снятие («Продать линии» или игрок-литейщик), контракты поставщика, контракты клиента (переход при переносе CPU, расторжение при конфликте эксклюзивности), `EnableFoundryServices`, CPU игрока для перевода в собственное производство, последствия для `UniqueID` 0, 1, 8 и смену типа компании, прирост расходов на завод; «Продать линии» запрещено, если цель делает CPU игрока; без галочки последствий — `BlockingErrors` не пуст
  - **Depends**: TSK-005, TSK-007

- [ ] **TSK-009**: `StakeMath` и тесты
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/StakeMath.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/StakeMathTests.cs`
  - **Acceptance**: цена доли 25 % у здоровой цели из примера = 116,15 млн; множитель 0,6 для цели в дефолте; дивиденд = 0 при убытке и при нехватке денег у цели; поручение: `J = 94,5 млн`, `σ ≈ 35 %` для примера FR-6, баланс цели после вливания строго выше порога; отказ при превышении лимита, денег игрока или доли 49 % с ключом причины
  - **Depends**: TSK-006

## Фаза 2 — Исполнение сделок и хуки

- [ ] **TSK-010**: Исполнитель, часть 1 — технологии, опыт узлов, линии
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.cs` (шаги 1–4, 6), `mod/src/PTMod/Features/RivalAcquisitions/GameAccess.cs` (рефлексия)
  - **Acceptance**: перепроверка цели (`SaveID`, `UniqueID`, статус) до изменений; технологии открываются без окон; опыт узлов поднимается до уровня цели; линии снимаются у цели и добавляются игроку на ту же пластину (или продаются по `SaleCost`); `CompanyTypeUpdater.TriggerUpdate(true)` вызван; каждый шаг в `ModHooks.Guard` с пометкой `partial`
  - **Depends**: TSK-004, TSK-008

- [ ] **TSK-011**: Исполнитель, часть 2 — CPU-семейства, производственные связи, контракты, флаг литейных услуг
  - **Requirement**: FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.Cpus.cs`
  - **Acceptance**: семейства отделяются от списков ИИ до выведения цели и присоединяются к игроку (база первой; `Player.AddCpu`, `ProductionWindow.AddCpu`, `CpuDataProvider.OnNewCpu`); после каждого CPU вызывается `ModEvents.RaiseCpuTransferred("rival-acquisitions", цель, игрок, cpu)` (не при загрузке; `CpuReleased` не поднимается); CPU, делавшиеся у цели, переводятся в собственное производство; контракты поставщика переназначены, CPU клиентов производятся у игрока; контракты цели-клиента переназначены на игрока с сохранением срока, наценки, штрафов и эксклюзивности (при конфликте — остаются расторгаемыми); при `EnableFoundryServices` включены `Divisions.FoundryServices` и переключатель окна производства; CPU из `RetireCpuIds` сняты; поведение соответствует итогам спайка TSK-003
  - **Depends**: TSK-003, TSK-010; mod-framework/TSK-056 (`ModEvents`)

- [ ] **TSK-012**: Исполнитель, часть 3 — выведение цели, оплата, запись, события, письмо
  - **Requirement**: FR-3, FR-4, FR-5, FR-10, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.Finish.cs`, `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsApi.cs` (события и чтение)
  - **Acceptance**: запись сделки добавляется в `State.deals` до вызова делегата, поэтому `IsAbsorbedByPlayer` внутри обработчика `OnCompanyWentBankrupt` возвращает `true`; цель: `set_MoneyAmount(0)`, `AICompany.TriggerBankruptcy()`, вызов делегата `OnCompanyWentBankrupt` (собственные хуки его игнорируют через `ExecutingTarget`); оплата последней через `CapitalLedger` (цена, остаток `C` по типу сделки; итог всегда отрицателен); при ошибках шагов 3–8 цена не списывается; `DealRecord` в журнале; `ModReputation.Add` по типу (причины — ключи `acq.reputation.*`); `DealCompleted`; `RivalBankrupt` для выведенной цели не поднимается; публикации через `EventBus` нет; письмо-итог (тег `acq:deal:<dealId>`); объект цели, `UniqueID`, `SaveID`, `Name` не изменены (проверка в тесте на фейках)
  - **Depends**: TSK-011; bankruptcy-recovery/TSK-007 (`CapitalLedger`); mod-framework/TSK-026 (`ModReputation`), TSK-038 и TSK-062 (`ModMail`)

- [ ] **TSK-013**: Хуки банкротства ИИ — P1, P2, событие банкротства, аукционы, бэкфилл
  - **Requirement**: FR-3, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs` (P1, P2), `mod/src/PTMod/Features/RivalAcquisitions/Services/RivalDistressMonitor.cs`
  - **Acceptance**: P1 — письмо «X в дефолте» (`Normal`, тег `acq:default:<uniqueId>`, многоразовые действия «Сделки»/«Поручение на спасение») один раз за эпизод, только для ИИ, не при перемотке/загрузке, событие `RivalDefaulted`; P2 — снимок не снятых CPU перед ванильным банкротством (кроме `ExecutingTarget`); событие банкротства — аукцион на 365 дней, письмо (`acq:auction:<uniqueId>`), `RivalBankrupt`, списание доли; истёкшие аукционы закрываются в `LateMonthPassed`; бэкфилл аукционов на `GameReady(Loaded)` старого сейва; на новой игре банкроты перемотки без аукционов; самопроверка целей P1, P2; патчей на `Company.AddCpu` / `AICompany.AddCpu` нет
  - **Depends**: TSK-012; bankruptcy-recovery/TSK-010 (общий таргет `TriggerDefault`, согласованность веток); mod-framework/TSK-024 (`ModTicks`), TSK-023

- [ ] **TSK-014**: `StakeService` — доли, дивиденды, списание, поручение на спасение (P3, prefix)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/StakeService.cs`, P3 (prefix) в `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs`
  - **Acceptance**: покупка доли списывает с игрока и вливает в цель; репутация +3 при покупке у цели в дефолте; дивиденды в `LateMonthPassed` по отчёту прошлого месяца (по `Year`/`Month`) через `EarnMoney(Interest)` с проверкой денег цели; P3 — prefix на `AICompany.BankruptcyWarning` (возвращает `true`): срабатывает только при активном поручении, `negativeBalanceLimit < 0`, цели в дефолте и `MoneyAmount ≤ negativeBalanceLimit`; вливание доводит баланс цели до `−0,5 × ModCredit.DebtLimitOf(цель)`, оригинал сразу выходит (нет spin-off GlobalFoundries, fabless и продажи линий), `TickBankruptcies` вызывает `CancelDefault`; неудача → письмо с причиной (`acq:rescueFailed:<uniqueId>`), оригинал идёт как обычно; при полном поглощении цена учитывает долю, доля закрывается; при выключенной фиче — ничего
  - **Depends**: TSK-009, TSK-012, TSK-013; mod-framework/TSK-058 (`ModCredit`)

## Фаза 3 — UI

- [ ] **TSK-015**: Окно «Сделки» — вкладки «Компании», «Мои доли», «История»
  - **Requirement**: FR-2, FR-14
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/AcquisitionsWindow.cs`, `mod/src/PTMod/Features/RivalAcquisitions/UI/AcquisitionsHubPage.cs` (`ctx.HubPage`, раздел «Сделки»)
  - **Acceptance**: модальное окно с паузой (`ModUI.CreateWindow`); таблица компаний со статусом и оценкой; кнопки сделок с ценой; неактивные — с причиной в подсказке (`ModUI.Tooltip`); вкладка долей с поручением; история сделок с пометкой «частичная»; открытие ≤ 50 мс
  - **Depends**: TSK-007, TSK-014; mod-framework/TSK-035 (`ModWindow`), TSK-036 (виджеты), TSK-064 (`ModUI.Tooltip`), TSK-039 (хаб)

- [ ] **TSK-016**: Превью сделки и подтверждение
  - **Requirement**: FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-12, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/DealPreviewWindow.cs`
  - **Acceptance**: блоки «Оценка» (с пометкой о нижней границе цены), «Деньги», «Что переходит» (в том числе контракты клиента и флаг литейных услуг), «Что снимается/расторгается», «Последствия» по design; переключатель «Забрать / Продать линии» (заблокирован, если обязательно); кнопка «Купить» активна только после галочки последствий (если они есть); двухшаговое подтверждение через `ModUI.Confirm` (собственного диалога нет); повторное нажатие во время исполнения невозможно
  - **Depends**: TSK-012, TSK-015; mod-framework/TSK-064 (`ModUI.Confirm`)

- [ ] **TSK-017**: Диалог доли, письма, действия писем, публичный API
  - **Requirement**: FR-6, FR-11, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/StakeDialog.cs`; шаблоны писем фичи (теги `acq:*`, `ModMailAction`, обработчик `ModMail.RegisterActionHandler`); доработка `RivalAcquisitionsApi.cs` (`OpenWindow`, `GetPlayerShare`, `IsAbsorbedByPlayer`, `StakeChanged`)
  - **Acceptance**: диалог доли показывает цену, вливание, ожидаемый дивиденд и поручение; письма «дефолт», «аукцион», «итог», «доля списана», «спасти не удалось» приходят с приоритетом `Normal` и рабочими действиями («Сделки» открывает окно с фокусом на компании, письмо не закрывается); ID письма не хранится (поиск `ModMail.Find`); от письма до превью ≤ 2 кликов; события API вызываются (тест-подписчик в отладке), ошибка одного подписчика не мешает остальным
  - **Depends**: TSK-013, TSK-014, TSK-015; mod-framework/TSK-033 (`ModNotify`), TSK-038 (`ModMailAction`, `RegisterActionHandler`), TSK-062 (приоритеты, `Find`)

- [ ] **TSK-018**: Локализация ru/en
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru/rival-acquisitions.json`, `mod/src/PTMod/L10n/en/rival-acquisitions.json` (ключи `acq.*` из design)
  - **Acceptance**: все строки окон, писем и причин — из `ModL10n`; в коде фичи нет жёсткого текста (проверка поиском); все ключи начинаются с `acq.` (самопроверка ядра не предупреждает о чужом префиксе); названия компаний в текстах реальные (Intel, AMD, GlobalFoundries, Cyrix), имена компаний в списках берутся из игры; фолбэк на `en` работает; `install.py --check` не находит расхождений ru/en
  - **Depends**: TSK-015, TSK-016, TSK-017; mod-framework/TSK-043 (строки ядра), TSK-066 (`ModIds`)

- [ ] **TSK-019**: Пометка «поглощена игроком» в таблице компаний (Could)
  - **Requirement**: FR-15
  - **Deliverables**: P4 в `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs`
  - **Acceptance**: в `CompaniesSpreadsheetWindow` у поглощённых — «поглощена игроком» вместо «банкрот»; при ошибке патча таблица показывается ванильно
  - **Depends**: TSK-012

## Фаза 4 — Проверка и документация

- [ ] **TSK-020**: Тесты сохранения и пограничных случаев
  - **Requirement**: NFR-2, NFR-3, FR-14
  - **Deliverables**: `mod/tests/PTMod.Tests/RivalAcquisitions/AcquisitionsStateSerializationTests.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/Data/sample.ptmod.json`, `mod/tests/PTMod.Tests/RivalAcquisitions/EdgeCaseTests.cs`
  - **Acceptance**: round-trip DTO ↔ `JsonObject` без потерь и без поля версии; устаревшие записи (нет компании / не тот `UniqueID`) игнорируются и сохраняются; чужие секции не теряются (через `ModSave`); пограничные случаи 1–18 из requirements покрыты тестами плана/правил там, где это чистая логика; случаи `_schema` (нет поля, новее) проверяет ядро (mod-framework/TSK-016)
  - **Depends**: TSK-008, TSK-012, TSK-014; mod-framework/TSK-016, TSK-025

- [ ] **TSK-021**: Отладочные действия для ручной проверки
  - **Requirement**: все FR (поддержка проверки)
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Debug/AcquisitionsDebugActions.cs` (только отладочная сборка `PTMOD_DEBUG`)
  - **Acceptance**: при флаге отладки в разделе «Сделки» хаба: «Дать игроку N млн», «Цель: баланс = k × D», «Цель: DaysInDefault = N», «Цель: обанкротить» (через ванильный путь: баланс ниже порога и `DaysInDefault = 119`); без флага и в релизной сборке не видны; действия помечаются в журнале как `debug`
  - **Depends**: TSK-015; mod-framework/TSK-039

- [ ] **TSK-022**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1…FR-16, NFR-1…NFR-7
  - **Deliverables**: протокол сценариев M1–M9 из `design.md` (дата, версия, результат по шагам), `Player.log` прогона
  - **Acceptance**: все сценарии пройдены, дефекты заведены задачами; 10 сделок разных типов с сохранением/загрузкой после каждой — без `[PTMod]`-ошибок; сейв после M1 грузится без мода
  - **Depends**: TSK-016, TSK-017, TSK-018, TSK-020, TSK-021, TSK-024; mod-framework/TSK-044 (`install.py --with-mods`), TSK-052

- [ ] **TSK-023**: Документация
  - **Requirement**: NFR-3, NFR-5
  - **Deliverables**: раздел «Сделки с соперниками» в `README.md` (виды сделок, как считается цена и почему полная покупка всегда дороже кэша цели, последствия для Intel/AMD/GlobalFoundries и что x86-лицензия не передаётся, что остаётся после удаления мода)
  - **Acceptance**: раздел описывает все сделки и формулу оценки словами; ссылка на спеку
  - **Depends**: TSK-022; mod-framework/TSK-050 (раздел README «Моды»)

- [ ] **TSK-024**: Передача состояния смежных фич при сделке
  - **Requirement**: FR-16
  - **Deliverables**: шаг 9 в `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.Finish.cs` (вызовы `ArchitectureApi.TransferCompanyState`, `GpuApi.TransferVendorProducts` под `ModFeatures.IsActive`), блоки превью, тесты порядка шагов на фейках (`mod/tests/PTMod.Tests/RivalAcquisitions/ExecutionOrderTests.cs`)
  - **Acceptance**: при активной `cpu-architectures` перед выведением цели вызван `TransferCompanyState(цель, игрок, true)`; при активной `gpu-product-line`, живой цели (FR-4/FR-5) и `GpuApi.IsDivisionFounded(цель)` вызван `TransferVendorProducts` до `TriggerBankruptcy`, результат `false` попал в журнал сделки; при покупке активов банкрота GPU не вызывается (пометка в журнале); при выключенных фичах вызовов нет, сделка проходит; исключение шага → `partial`, сделка не блокируется; жёсткой зависимости (`DependsOn`) нет
  - **Depends**: TSK-012; мягко: `cpu-architectures` (`ArchitectureApi`), `gpu-product-line` (`GpuApi`) — API этих фич вызывается только при их активности

## Dependency graph

```
mod-framework (Core): TSK-022 Feature/Context, TSK-023 Hooks, TSK-021 Settings/Log/Content, TSK-024 Ticks, TSK-025 Save,
                      TSK-058 Credit, TSK-056 ModEvents, TSK-026 Reputation/Companies, TSK-035/036/064 UI, TSK-039 Hub,
                      TSK-033 Notify, TSK-038/062 Mail, TSK-067 Runner
bankruptcy-recovery/TSK-007 (CapitalLedger)      bankruptcy-recovery/TSK-010 (общий таргет TriggerDefault)
      |                                                       |
   TSK-001 ──> TSK-002 ──┐                                    |
      |                  |                                    |
      ├──> TSK-003 ──────┼──────────────────┐                 |
      ├──> TSK-004 ──────┼────────────────┐ |                 |
      └──> TSK-005 ──> TSK-006 ──┬──> TSK-007 ──> TSK-008     |
                                 └──> TSK-009        |        |
                                                     v        |
                          TSK-004 ──────────────> TSK-010 ──> TSK-011 (+TSK-003)
                                                                 |
                                                                 v
                                                TSK-012 <── CapitalLedger
                                                   |
                                                   ├──> TSK-013 <───────────────────┘
                                                   |       |
                                     TSK-009 ──> TSK-014 <─┘
                                                   |
                                   TSK-007 ──> TSK-015 ──> TSK-016 (+TSK-012)
                                                   |
                                                   ├──> TSK-017 (+TSK-013, TSK-014)
                                                   └──> TSK-021
                                   TSK-012 ──> TSK-019 (Could)
                                   TSK-012 ──> TSK-024 (мягко: cpu-architectures, gpu-product-line)
                     TSK-016, TSK-017 ──> TSK-018
                     TSK-008, TSK-012, TSK-014 ──> TSK-020
                     TSK-016, TSK-017, TSK-018, TSK-020, TSK-021, TSK-024 ──> TSK-022 ──> TSK-023
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи [ModFeature], настройки, участник сейва | Pending |
| TSK-002 Контент и валидатор | Pending |
| TSK-003 Спайк: перенос CPU и связей | Pending |
| TSK-004 Спайк: технологии, линии, оповещение, prefix спасения | Pending |
| TSK-005 Снимки и адаптер | Pending |
| TSK-006 ValuationMath | Pending |
| TSK-007 DealRules (инвариант NetCash < 0) | Pending |
| TSK-008 AcquisitionPlanBuilder | Pending |
| TSK-009 StakeMath | Pending |
| TSK-010 Исполнитель: технологии, узлы, линии | Pending |
| TSK-011 Исполнитель: CPU, связи, контракты | Pending |
| TSK-012 Исполнитель: выведение, оплата, события | Pending |
| TSK-013 Хуки банкротства ИИ и аукционы | Pending |
| TSK-014 StakeService и поручение (prefix) | Pending |
| TSK-015 Окно «Сделки» | Pending |
| TSK-016 Превью сделки | Pending |
| TSK-017 Диалог доли, письма, API | Pending |
| TSK-018 Локализация ru/en | Pending |
| TSK-019 Пометка в таблице компаний (Could) | Pending |
| TSK-020 Тесты сохранения и пограничных случаев | Pending |
| TSK-021 Отладочные действия | Pending |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Документация | Pending |
| TSK-024 Передача состояния смежных фич | Pending |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-005, TSK-006 |
| FR-2 | TSK-005, TSK-007, TSK-015 |
| FR-3 | TSK-007, TSK-012, TSK-013, TSK-016 |
| FR-4 | TSK-007, TSK-012, TSK-016 |
| FR-5 | TSK-007, TSK-012, TSK-016 |
| FR-6 | TSK-007, TSK-009, TSK-014, TSK-017 |
| FR-7 | TSK-004, TSK-008, TSK-010, TSK-016 |
| FR-8 | TSK-003, TSK-008, TSK-011, TSK-016 |
| FR-9 | TSK-003, TSK-004, TSK-008, TSK-011, TSK-016 |
| FR-10 | TSK-004, TSK-012 |
| FR-11 | TSK-013, TSK-017 |
| FR-12 | TSK-007, TSK-008, TSK-016 |
| FR-13 | TSK-012, TSK-017 |
| FR-14 | TSK-001, TSK-002, TSK-015, TSK-020 |
| FR-15 | TSK-019 |
| FR-16 | TSK-024 |
| NFR-1…NFR-7 | TSK-002, TSK-005, TSK-015, TSK-018, TSK-020, TSK-022, TSK-023, TSK-024 |
