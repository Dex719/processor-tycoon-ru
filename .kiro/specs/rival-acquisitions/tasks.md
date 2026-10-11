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

- [x] **TSK-001**: Каркас фичи `[ModFeature]`, настройки и участник сейва (авто 2026-10-10)
  - **Requirement**: FR-14, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsFeature.cs` (`IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn` пустой, `EnabledByDefault`, `SupportsHotToggle`, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsApi.cs` (заглушки), `mod/src/PTMod/Features/RivalAcquisitions/AcquisitionsSaveParticipant.cs` (`IModSaveParticipant`), `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/AcquisitionsState.cs` (DTO без поля версии)
  - **Acceptance**: фича `rival-acquisitions` видна в хабе (`ModFeatures.All`) и выключается/включается; секция пишется и читается (пустая), поле `_schema` в ней добавляет ядро; без секции — состояние по умолчанию; подписка на `BankruptcyHandler.OnCompanyWentBankrupt` ставится на `ModTicks.GameReady` и сбрасывается на `PTModHost.SceneLeaving`; своих счётчиков ошибок и флагов `Enabled` нет
  - **Depends**: mod-framework/TSK-022 (`ModFeatures`, `FeatureContext`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`, `ModLog`), TSK-024 (`ModTicks`), TSK-025 (`ModSave`), TSK-067 (мини-раннер)

- [x] **TSK-002**: Контент `balance.json` и валидатор (авто 2026-10-10)
  - **Requirement**: FR-14, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/rival-acquisitions/balance.json`, `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/AcquisitionsBalance.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/AcquisitionsBalanceTests.cs`
  - **Acceptance**: JSON совпадает с примером design; загрузка через `ModContent.LoadConfig`; поля вне диапазонов заменяются значениями по умолчанию с `ModLog`; `maxShare ≥ 0,5` отклоняется; варианты доли больше `maxShare` отбрасываются; `rescueTargetDebtLimitShare` вне (−1; 0] заменяется на −0,5; тесты зелёные
  - **Depends**: TSK-001; mod-framework/TSK-015 (`ModContent`: слияние), TSK-021 (адаптер `ModContent`), TSK-067

- [ ] **TSK-003**: Спайк — перенос CPU-семейства и производственных связей
  - **Requirement**: FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Debug/SpikeCpuTransfer.cs` (временный, сборка спайка — `python mod/build.py --src … --out …`, удаляется после спайка); раздел «Результаты спайков» в `design.md`
  - **Acceptance**: на копии сейва перенос семейства (база + вариант) от ИИ к игроку по шагам 5 и 8 design: CPU в окне производства игрока, продаётся, OEM-контракты на нём выплачивают игроку; `ModEvents.RaiseCpuTransferred` поднимает `CpuTransferred` на каждый CPU и не поднимает `CpuReleased`; сохранение → загрузка: CPU у игрока, `Cpu.Base` найден, ошибок в `Player.log` нет; CPU, производившийся у литейщика без контракта, производится (или зафиксирован отказ → FR-8 п. 3 расширяется на такие CPU); контракт цели-клиента, переназначенный на игрока (`BusinessContract.Initialize(поставщик, игрок)`), работает, а CPU производится по контракту; сброс `Cpu.manufacturer`/`Cpu.currentContract` через рефлексию работает; вывод — FR-8 «как в design» или «снятие с компенсацией» (решение владельца)
  - **Depends**: TSK-001; mod-framework/TSK-006 (Harmony-спайк), TSK-023, TSK-056 (`ModEvents`)
  - **Частично** (авто 2026-10-10, кусок 1; игра не запускалась): сделана статическая проверка по декомпилированному коду игры — шаги 5, 7 и 8 design сходятся с кодом (отделение CPU до `TriggerBankruptcy`, переназначение контрактов до банкротства, база семейства перед вариантами, сброс `Cpu.manufacturer`/`currentContract`); итоги и новые находки лежат в `mod/requests/rival-acquisitions.md`, п. 3 (интегратор переносит их в раздел «Результаты спайков» в `design.md`). **Не сделано**: `Debug/SpikeCpuTransfer.cs` не писался (непроверяемый код попал бы в `PTMod.dll`), все поведенческие пункты Acceptance (перенос семейства, `CpuTransferred` без `CpuReleased`, сохранение → загрузка, CPU у литейщика без контракта, переназначенный контракт цели-клиента, вывод «как в design» или «с компенсацией») требуют запуска игры — их закрывают ручные сценарии M1, M7 владельца (TSK-022).

- [ ] **TSK-004**: Спайк — технологии без окон, линии, флаг литейных услуг, оповещение о банкротстве
  - **Requirement**: FR-7, FR-9, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Debug/SpikeAssets.cs` (временный); раздел «Результаты спайков» в `design.md`
  - **Acceptance**: разблокировка 5+ технологий с зависимостями (`Progress = 1`, повтор `ResearchSector.Update(true)`) при `temporarilySupressPopup = true` — без окон «Исследование завершено»; `Company.ExpandFactory` на открытой пластине добавляет линии; перенесённый контракт поставщика (`BusinessContract.Initialize(игрок, клиент)`) производит CPU клиента на заводе игрока — с флагом `Divisions.FoundryServices` (включённым через `ProductionWindow.SetFoundryServicesToggle`) или без (зафиксировать); вызов делегата `OnCompanyWentBankrupt` через рефлексию для Intel приводит к ванильному «У Cyrix больше нет проблем с патентами!»; prefix на `AICompany.BankruptcyWarning`, поднявший баланс выше `negativeBalanceLimit`, заставляет метод выйти без `ConsiderGoingFabless`, `SellProductionLinesToAvoidBankruptcy` и `CompanyEventHandler.TriggerSolarFoundriesSpinOff`, а `TickBankruptcies` вызывает `CancelDefault`
  - **Depends**: TSK-001; mod-framework/TSK-006, TSK-023
  - **Частично** (авто 2026-10-10, кусок 1; игра не запускалась): статическая проверка по декомпилированному коду — подтверждены все четыре члена рефлексии (`ResearchSector.temporarilySupressPopup`, `Cpu.manufacturer`, `Cpu.currentContract`, поле события `BankruptcyHandler.OnCompanyWentBankrupt`), механизм отсутствия окон, цепочка «делегат → `LicenseHelper` → `ReformCyrux`», выход оригинала `BankruptcyWarning` при балансе выше лимита и `CancelDefault` в `TickBankruptcies`, prefix на `BankruptcyWarning` не зарезервирован ядром. **Находки для TSK-010**: за один проход `Update(true)` открывается один уровень зависимостей, самая длинная цепочка — 26, а `techUnlockMaxPasses` = 20 (открывать по глубине зависимостей с `Update(true)` после каждой технологии); `Company.ExpandFactory` бросает исключение на пластине, которой нет у игрока, а `AddLinesToWafer` молча ничего не делает без группы линий (сверять `AmountOfLines`). Подробности: `mod/requests/rival-acquisitions.md`, п. 3. **Не сделано**: `Debug/SpikeAssets.cs` не писался; пункты Acceptance в игре не проверены (5+ технологий без окон, прирост линий, производство CPU клиента по контракту с флагом и без, «У Cyrix больше нет проблем…», prefix в живом `TickBankruptcies`) — закрываются ручными сценариями M5–M7 владельца (TSK-022).

## Фаза 1 — Чистая логика

- [x] **TSK-005**: Снимки состояния и адаптер игры (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/Model/Snapshots.cs` (`CompanySnapshot`, `CpuSnapshot`, `ContractSnapshot`, `PlayerSnapshot` и др.), `mod/src/PTMod/Features/RivalAcquisitions/GameSnapshotAdapter.cs`
  - **Acceptance**: адаптер заполняет все поля снимка из источников, перечисленных в design (деньги, порог цели `ModCredit.DebtLimitOf`, линии по пластинам, `ManufacturingDivisionValue`, разрыв и частичный прогресс технологий, опыт узлов, CPU с выручкой за 12 мес. (`CpuBase.MarketData.Income`), контракты поставщика и клиента, флаг `Divisions.FoundryServices`, доля рынка); статус компании определяется по `IsBankrupt`, `IsInDefault`, аукционам и журналу; снимок 13 компаний строится ≤ 50 мс (лог отладки)
  - **Depends**: TSK-001; mod-framework/TSK-058 (`ModCredit`), TSK-026 (`ModCompanies`)

- [x] **TSK-006**: `ValuationMath` и тесты (авто 2026-10-10)
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/ValuationMath.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/ValuationMathTests.cs`
  - **Acceptance**: пример 1985 из design даёт `E = 434,6 млн ± 0,1 млн`; пустая компания → `E_min`; маржа ограничена [0; 1]; CPU снимка банкрота × 0,5; для банкрота `s = 0`; расчёт в `double`
  - **Depends**: TSK-002, TSK-005

- [x] **TSK-007**: `DealRules` и тесты (в том числе инвариант `NetCash < 0`) (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3, FR-4, FR-5, FR-6, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/DealRules.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/DealRulesTests.cs`
  - **Acceptance**: доступность каждой сделки по статусу цели (здорова, дефолт, аукцион, аукцион истёк, поглощена) и игрока (в дефолте — всё недоступно); цены и итоги из примеров design (696,9 / 304,2 / 141,8 млн; 927,8 и 1 027,8 млн для нижней границы `C⁺ + E_min`); множитель 1,75 для большей цели; кулдаун 365 дней с датой доступности; `minCashAfter`; учёт доли игрока `(1 − σ_p)`; канал `acquisition.price`; округление вверх до 1 000 $; причины недоступности — ключи L10n; **перебор сетки** `E`, `C`, `σ_p`, `Ch`, `D` (список в design, «Testing Strategy») подтверждает `NetCash < 0` для Distressed и Hostile
  - **Depends**: TSK-006

- [x] **TSK-008**: `AcquisitionPlanBuilder` и тесты (авто 2026-10-10)
  - **Requirement**: FR-7, FR-8, FR-9, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/AcquisitionPlanBuilder.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/AcquisitionPlanBuilderTests.cs`
  - **Acceptance**: план содержит разблокировки технологий, частичный прогресс, подъём опыта узлов, перемещения или продажи линий (с отображением отсутствующей пластины на меньшую), семейства CPU (база первой, снятая база включена), CPU под снятие («Продать линии» или игрок-литейщик), контракты поставщика, контракты клиента (переход при переносе CPU, расторжение при конфликте эксклюзивности), `EnableFoundryServices`, CPU игрока для перевода в собственное производство, последствия для `UniqueID` 0, 1, 8 и смену типа компании, прирост расходов на завод; «Продать линии» запрещено, если цель делает CPU игрока; без галочки последствий — `BlockingErrors` не пуст
  - **Depends**: TSK-005, TSK-007

- [x] **TSK-009**: `StakeMath` и тесты (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Logic/StakeMath.cs`, `mod/tests/PTMod.Tests/RivalAcquisitions/StakeMathTests.cs`
  - **Acceptance**: цена доли 25 % у здоровой цели из примера = 116,15 млн; множитель 0,6 для цели в дефолте; дивиденд = 0 при убытке и при нехватке денег у цели; поручение: `J = 94,5 млн`, `σ ≈ 35 %` для примера FR-6, баланс цели после вливания строго выше порога; отказ при превышении лимита, денег игрока или доли 49 % с ключом причины
  - **Depends**: TSK-006

## Фаза 2 — Исполнение сделок и хуки

- [x] **TSK-010**: Исполнитель, часть 1 — технологии, опыт узлов, линии (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.cs` (шаги 1–4, 6), `mod/src/PTMod/Features/RivalAcquisitions/GameAccess.cs` (рефлексия)
  - **Acceptance**: перепроверка цели (`SaveID`, `UniqueID`, статус) до изменений; технологии открываются без окон; опыт узлов поднимается до уровня цели; линии снимаются у цели и добавляются игроку на ту же пластину (или продаются по `SaleCost`); `CompanyTypeUpdater.TriggerUpdate(true)` вызван; каждый шаг в `ModHooks.Guard` с пометкой `partial`
  - **Depends**: TSK-004, TSK-008

- [ ] **TSK-011**: Исполнитель, часть 2 — CPU-семейства, производственные связи, контракты, флаг литейных услуг
  - **Requirement**: FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.Cpus.cs`
  - **Acceptance**: семейства отделяются от списков ИИ до выведения цели и присоединяются к игроку (база первой; `Player.AddCpu`, `ProductionWindow.AddCpu`, `CpuDataProvider.OnNewCpu`); после каждого CPU вызывается `ModEvents.RaiseCpuTransferred("rival-acquisitions", цель, игрок, cpu)` (не при загрузке; `CpuReleased` не поднимается); CPU, делавшиеся у цели, переводятся в собственное производство; контракты поставщика переназначены, CPU клиентов производятся у игрока; контракты цели-клиента переназначены на игрока с сохранением срока, наценки, штрафов и эксклюзивности (при конфликте — остаются расторгаемыми); при `EnableFoundryServices` включены `Divisions.FoundryServices` и переключатель окна производства; CPU из `RetireCpuIds` сняты; поведение соответствует итогам спайка TSK-003
  - **Depends**: TSK-003, TSK-010; mod-framework/TSK-056 (`ModEvents`)
  - **Частично** (авто 2026-10-10, кусок 3; игра не запускалась): `Services/AcquisitionExecutor.Cpus.cs` реализует шаги 5, 7, 8 по design и по статической проверке спайка TSK-003 (отделение семейств от трёх списков ИИ до выведения цели, база первой, семья без найденной базы остаётся у цели; `RetireCpuIds` снимаются, пока CPU ещё у цели; `Player.AddCpu` + `ProductionWindow.AddCpu` + `CpuDataProvider.OnNewCpu` + `ModEvents.RaiseCpuTransferred` на каждый CPU, `Company.AddCpu` не вызывается, `CpuReleased` не поднимается; сброс `Cpu.manufacturer`/`currentContract` рефлексией; CPU игрока, делавшиеся у цели, и CPU цели — в собственное производство; при «Забрать линии» контракты поставщика `Initialize(игрок, клиент)` и CPU клиентов `SetNewManufacturer(игрок)`, `FoundryServices` и переключатель окна производства; контракты цели-клиента `Initialize(поставщик, игрок)` с прежними сроком, наценкой, штрафами и эксклюзивностью, расторгаемые не трогаются). Порядок и «частичность» шагов проверены тестами на фейках (`DealExecutionTests`), вызовы — чтением исходника (`ExecutorSourceTests`). **Не сделано**: пункт «поведение соответствует итогам спайка TSK-003» — спайк в игре не проводился (TSK-003 «Частично»), выбор «как в design» или «снятие с компенсацией» за владельцем; перенос в живой игре проверяется сценариями M1, M3, M7 владельца (TSK-022).

- [x] **TSK-012**: Исполнитель, часть 3 — выведение цели, оплата, запись, события, письмо (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4, FR-5, FR-10, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/AcquisitionExecutor.Finish.cs`, `mod/src/PTMod/Features/RivalAcquisitions/RivalAcquisitionsApi.cs` (события и чтение)
  - **Acceptance**: запись сделки добавляется в `State.deals` до вызова делегата, поэтому `IsAbsorbedByPlayer` внутри обработчика `OnCompanyWentBankrupt` возвращает `true`; цель: `set_MoneyAmount(0)`, `AICompany.TriggerBankruptcy()`, вызов делегата `OnCompanyWentBankrupt` (собственные хуки его игнорируют через `ExecutingTarget`); оплата последней через `CapitalLedger` (цена, остаток `C` по типу сделки; итог всегда отрицателен); при ошибках шагов 3–8 цена не списывается; `DealRecord` в журнале; `ModReputation.Add` по типу (причины — ключи `acq.reputation.*`); `DealCompleted`; `RivalBankrupt` для выведенной цели не поднимается; публикации через `EventBus` нет; письмо-итог (тег `acq:deal:<dealId>`); объект цели, `UniqueID`, `SaveID`, `Name` не изменены (проверка в тесте на фейках)
  - **Depends**: TSK-011; bankruptcy-recovery/TSK-007 (`CapitalLedger`); mod-framework/TSK-026 (`ModReputation`), TSK-038 и TSK-062 (`ModMail`)

- [x] **TSK-013**: Хуки банкротства ИИ — P1, P2, событие банкротства, аукционы, бэкфилл (авто 2026-10-10)
  - **Requirement**: FR-3, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs` (P1, P2), `mod/src/PTMod/Features/RivalAcquisitions/Services/RivalDistressMonitor.cs`
  - **Acceptance**: P1 — письмо «X в дефолте» (`Normal`, тег `acq:default:<uniqueId>`, многоразовые действия «Сделки»/«Поручение на спасение») один раз за эпизод, только для ИИ, не при перемотке/загрузке, событие `RivalDefaulted`; P2 — снимок не снятых CPU перед ванильным банкротством (кроме `ExecutingTarget`); событие банкротства — аукцион на 365 дней, письмо (`acq:auction:<uniqueId>`), `RivalBankrupt`, списание доли; истёкшие аукционы закрываются в `LateMonthPassed`; бэкфилл аукционов на `GameReady(Loaded)` старого сейва; на новой игре банкроты перемотки без аукционов; самопроверка целей P1, P2; патчей на `Company.AddCpu` / `AICompany.AddCpu` нет
  - **Depends**: TSK-012; bankruptcy-recovery/TSK-010 (общий таргет `TriggerDefault`, согласованность веток); mod-framework/TSK-024 (`ModTicks`), TSK-023

- [x] **TSK-014**: `StakeService` — доли, дивиденды, списание, поручение на спасение (P3, prefix) (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/Services/StakeService.cs`, P3 (prefix) в `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs`
  - **Acceptance**: покупка доли списывает с игрока и вливает в цель; репутация +3 при покупке у цели в дефолте; дивиденды в `LateMonthPassed` по отчёту прошлого месяца (по `Year`/`Month`) через `EarnMoney(Interest)` с проверкой денег цели; P3 — prefix на `AICompany.BankruptcyWarning` (возвращает `true`): срабатывает только при активном поручении, `negativeBalanceLimit < 0`, цели в дефолте и `MoneyAmount ≤ negativeBalanceLimit`; вливание доводит баланс цели до `−0,5 × ModCredit.DebtLimitOf(цель)`, оригинал сразу выходит (нет spin-off GlobalFoundries, fabless и продажи линий), `TickBankruptcies` вызывает `CancelDefault`; неудача → письмо с причиной (`acq:rescueFailed:<uniqueId>`), оригинал идёт как обычно; при полном поглощении цена учитывает долю, доля закрывается; при выключенной фиче — ничего
  - **Depends**: TSK-009, TSK-012, TSK-013; mod-framework/TSK-058 (`ModCredit`)

## Фаза 3 — UI

- [x] **TSK-015**: Окно «Сделки» — вкладки «Компании», «Мои доли», «История» (авто 2026-10-10)
  - **Requirement**: FR-2, FR-14
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/AcquisitionsWindow.cs`, `mod/src/PTMod/Features/RivalAcquisitions/UI/AcquisitionsHubPage.cs` (`ctx.HubPage`, раздел «Сделки»)
  - **Acceptance**: модальное окно с паузой (`ModUI.CreateWindow`); таблица компаний со статусом и оценкой; кнопки сделок с ценой; неактивные — с причиной в подсказке (`ModUI.Tooltip`); вкладка долей с поручением; история сделок с пометкой «частичная»; открытие ≤ 50 мс
  - **Depends**: TSK-007, TSK-014; mod-framework/TSK-035 (`ModWindow`), TSK-036 (виджеты), TSK-064 (`ModUI.Tooltip`), TSK-039 (хаб)

- [x] **TSK-016**: Превью сделки и подтверждение (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-12, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/DealPreviewWindow.cs`
  - **Acceptance**: блоки «Оценка» (с пометкой о нижней границе цены), «Деньги», «Что переходит» (в том числе контракты клиента и флаг литейных услуг), «Что снимается/расторгается», «Последствия» по design; переключатель «Забрать / Продать линии» (заблокирован, если обязательно); кнопка «Купить» активна только после галочки последствий (если они есть); двухшаговое подтверждение через `ModUI.Confirm` (собственного диалога нет); повторное нажатие во время исполнения невозможно
  - **Depends**: TSK-012, TSK-015; mod-framework/TSK-064 (`ModUI.Confirm`)

- [x] **TSK-017**: Диалог доли, письма, действия писем, публичный API (авто 2026-10-10)
  - **Requirement**: FR-6, FR-11, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/RivalAcquisitions/UI/StakeDialog.cs`; шаблоны писем фичи (теги `acq:*`, `ModMailAction`, обработчик `ModMail.RegisterActionHandler`); доработка `RivalAcquisitionsApi.cs` (`OpenWindow`, `GetPlayerShare`, `IsAbsorbedByPlayer`, `StakeChanged`)
  - **Acceptance**: диалог доли показывает цену, вливание, ожидаемый дивиденд и поручение; письма «дефолт», «аукцион», «итог», «доля списана», «спасти не удалось» приходят с приоритетом `Normal` и рабочими действиями («Сделки» открывает окно с фокусом на компании, письмо не закрывается); ID письма не хранится (поиск `ModMail.Find`); от письма до превью ≤ 2 кликов; события API вызываются (тест-подписчик в отладке), ошибка одного подписчика не мешает остальным
  - **Depends**: TSK-013, TSK-014, TSK-015; mod-framework/TSK-033 (`ModNotify`), TSK-038 (`ModMailAction`, `RegisterActionHandler`), TSK-062 (приоритеты, `Find`)

- [x] **TSK-018**: Локализация ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru/rival-acquisitions.json`, `mod/src/PTMod/L10n/en/rival-acquisitions.json` (ключи `acq.*` из design)
  - **Acceptance**: все строки окон, писем и причин — из `ModL10n`; в коде фичи нет жёсткого текста (проверка поиском); все ключи начинаются с `acq.` (самопроверка ядра не предупреждает о чужом префиксе); названия компаний в текстах реальные (Intel, AMD, GlobalFoundries, Cyrix), имена компаний в списках берутся из игры; фолбэк на `en` работает; `install.py --check` не находит расхождений ru/en
  - **Depends**: TSK-015, TSK-016, TSK-017; mod-framework/TSK-043 (строки ядра), TSK-066 (`ModIds`)

- [x] **TSK-019**: Пометка «поглощена игроком» в таблице компаний (Could) (авто 2026-10-10)
  - **Requirement**: FR-15
  - **Deliverables**: P4 в `mod/src/PTMod/Features/RivalAcquisitions/Patches/AcquisitionPatches.cs`
  - **Acceptance**: в `CompaniesSpreadsheetWindow` у поглощённых — «поглощена игроком» вместо «банкрот»; при ошибке патча таблица показывается ванильно
  - **Depends**: TSK-012

## Фаза 4 — Проверка и документация

- [x] **TSK-020**: Тесты сохранения и пограничных случаев (авто 2026-10-10)
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
  - **Частично** (авто 2026-10-10, кусок 5): текст раздела «Сделки с соперниками» написан целиком и лежит в `mod/requests/rival-acquisitions.md`, п. 8: пять видов сделок, цены словами (оценка завода, технологий и CPU, множители, нижняя граница, округление), почему полная покупка всегда дороже денег цели, что переходит, последствия для Intel, AMD и GlobalFoundries (x86-лицензия не передаётся), что остаётся после удаления мода, ссылка на спеку. **Не сделано**: вставка в `README.md` (файл вне списка файлов куска, его параллельно правят другие фичи; вставляет интегратор) и сверка текста с итогами ручной проверки владельцем (TSK-022, от которой зависит эта задача).

- [x] **TSK-024**: Передача состояния смежных фич при сделке (авто 2026-10-10)
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
| TSK-001 Каркас фичи [ModFeature], настройки, участник сейва | Complete (авто 2026-10-10, кусок 1: `RivalAcquisitionsFeature` (`[ModFeature]`, `DependsOn` пустой, `SupportsHotToggle = true`, `Register` читает `balance.json`, регистрирует участника сейва и подписки на `ModTicks.GameReady` / `PTModHost.SceneLeaving`; подписка на `BankruptcyHandler.OnCompanyWentBankrupt` ставится на `GameReady` (и в `OnEnabled` посреди сцены), снимается на `SceneLeaving` и в `OnDisabled`; собственных счётчиков ошибок и флагов нет), `RivalAcquisitionsService` (реализация `IRivalAcquisitionsService`, регистрируется в `OnEnabled`), `AcquisitionsSaveParticipant` (`ModJson.Bind`/`From`, `Sanitize`), `Logic/Model/AcquisitionsState` (+ `StakeData`, `AuctionData`, `RescueOrderData`; без поля версии, чтения для API, `Sanitize` по NFR-7), строки `acq.feature.*`, `acq.hub.title` ru/en; корневого `RivalAcquisitionsApi.cs` нет: публичный фасад уже лежит в `Api/`; видимость в хабе и горячее переключение проверяются в игре владельцем (TSK-022); для тестовой сборки `Logic/Model/ContractStubs.cs` (только `#if PTMOD_TESTS`), его надо удалить после правки №1 из `mod/requests/rival-acquisitions.md`) |
| TSK-002 Контент и валидатор | Complete (авто 2026-10-10, кусок 1: `Content/rival-acquisitions/balance.json` = пример design; `Logic/Model/AcquisitionsBalance` (+ `ValuationConfig`, `DealsConfig`, `BankruptDealConfig`, `DistressedDealConfig`, `HostileDealConfig`, `StakeConfig`, `ReputationConfig`): `FromJson` не бросает, поле вне диапазона, не того типа или неизвестное → значение по умолчанию + строка для `ModLog` (фича пишет их через `ModLog.Warn`); `maxShare` ≥ 0,5 отклоняется, варианты доли выше `maxShare` отбрасываются, `rescueTargetDebtLimitShare` вне (−1; 0] → −0,5; 47 тестов кусков 1 (`AcquisitionsBalanceTests` 26, `AcquisitionsStateTests` 16, `RivalAcquisitionsFeatureTests` 5) зелёные в `python mod/build_feature.py rival-acquisitions --tests` (1161 тест всего, 0 упало); тесты лежат в `mod/tests/PTMod.Tests/Features/RivalAcquisitions/`) |
| TSK-003 Спайк: перенос CPU и связей | In Progress (авто 2026-10-10, кусок 1: статическая проверка шагов 5, 7, 8 по коду игры сделана, итоги в `mod/requests/rival-acquisitions.md`, п. 3; прогон в игре и `Debug/SpikeCpuTransfer.cs` не сделаны — игра агентом не запускается, остаётся владельцу: M1, M7 в TSK-022) |
| TSK-004 Спайк: технологии, линии, оповещение, prefix спасения | In Progress (авто 2026-10-10, кусок 1: статическая проверка по коду игры сделана — члены рефлексии подтверждены; находка: цепочка зависимостей до 26 против `techUnlockMaxPasses` = 20, `ExpandFactory` бросает на неизвестной пластине; итоги в `mod/requests/rival-acquisitions.md`, п. 3; прогон в игре и `Debug/SpikeAssets.cs` не сделаны — остаётся владельцу: M5–M7 в TSK-022) |
| TSK-005 Снимки и адаптер | Complete (авто 2026-10-10, кусок 2: `Logic/Model/Snapshots.cs` (`CompanySnapshot`, `PlayerSnapshot`, `CpuSnapshot`, `ContractSnapshot`, `WaferLinesSnapshot`, `TechSnapshot` с глубиной зависимостей, `TechProgressSnapshot`, `NodeSnapshot`, `CompanyStatus`, `CompanyStatusRules`: статус по `IsBankrupt`/`IsInDefault`/аукционам/журналу, пара `UniqueID`+`SaveID` обязательна) и `GameSnapshotAdapter.cs` (`Player()`, `Of(...)`, `All(...)`, `PriceChannelOf`): деньги, порог через `ModCredit`, линии и `SaleCost` по пластинам, `ManufacturingDivisionValue`, разрыв и частичный прогресс технологий, опыт узлов, CPU с выручкой за прошлые полные месяцы, контракты поставщика/клиента, флаг `FoundryServices`, доли рынка; дорогие поиски игры один раз на `All`; время сборки пишет `ModLog.Debug`; собирается без предупреждений, чистая часть покрыта `SnapshotTests` (16), чтение источников — `GameSnapshotAdapterTests` (4) по исходнику. **В игре не запускалось**: правильность чтения и бюджет ≤ 50 мс на 13 компаний проверяются владельцем (M1, TSK-022) по строке `snapshots: N companies in X ms` в `Player.log`) |
| TSK-006 ValuationMath | Complete (авто 2026-10-10, кусок 2: `Logic/ValuationMath.cs` (`Valuation`: `AFab`, `ATech`, `ACpu`, `Share`, `SharePremium`, `E`, `EMin`, `FloorApplied`); пример 1985 даёт `E` = 434,592 млн, пустая компания — `E_min`, маржа в [0; 1], CPU снимка банкрота × 0,5 и `s` = 0, расчёт в `double`, мусорные числа дают 0; 13 тестов `ValuationMathTests`) |
| TSK-007 DealRules (инвариант NetCash < 0) | Complete (авто 2026-10-10, кусок 2: `Logic/DealRules.cs` (`Quote`, `DealQuote`, `RoundUp`) и `Logic/Model/AcqKeys.cs` (ключи причин, ошибок плана, последствий; строки ru/en добавлены в `L10n`): доступность по статусу цели и игрока, цены примеров design (696,9 / 304,2 / 141,8 млн; 927,8 и 1 027,8 млн по нижней границе), 1,75 для большей цели, кулдаун с датой `dd.MM.yyyy`, `minCashAfter`, `(1 − σ_p)`, канал `acquisition.price`, округление вверх до 1 000, а также доступность доли и поручения; инвариант `NetCash < 0` проверен перебором сетки из 432 случаев (E, C, σ_p, Ch, D × Distressed/Hostile), плюс при D = 0 цена минимум на шаг выше кэша; 39 тестов `DealRulesTests`) |
| TSK-008 AcquisitionPlanBuilder | Complete (авто 2026-10-10, кусок 2: `Logic/AcquisitionPlanBuilder.cs` и `Logic/Model/AcquisitionPlan.cs` (`AcquisitionPlan`, `PlanOptions`, `LineMove`, `LineSale`, `CpuFamily`, `FamilyMember`, `CpuFate`, `PlanNote`): разблокировки по глубине зависимостей, частичный прогресс, подъём опыта узлов, перемещение линий (отсутствующая пластина — на наибольшую меньшую) или продажа, семейства CPU (база первой, снятая база включена, семья без живых членов остаётся у цели), CPU под снятие, контракты поставщика, контракты клиента (переход, расторжение при конфликте эксклюзивности или без CPU у этого поставщика), `EnableFoundryServices`, CPU игрока в собственное производство, CPU цели, делавшиеся у игрока, последствия для `UniqueID` 0, 1, 8 и смену fabless → IDM, прирост расходов на завод, «Продать линии» запрещено при CPU игрока у цели, без галочки `BlockingErrors` не пуст; 45 тестов `AcquisitionPlanBuilderTests`) |
| TSK-009 StakeMath | Complete (авто 2026-10-10, кусок 2: `Logic/StakeMath.cs` (`StakePrice`, `Dividend`, `Rescue`, `RescueDecision`, `FitsShare`, `FittingOptions`, `SmallestFittingOption`): цена 25 % у здоровой цели = 116,15 млн, множитель 0,6 в дефолте, дивиденд 0 при убытке и при нехватке денег у цели, поручение `J` = 94,5 млн и `σ` = 35 % для примера FR-6 с балансом цели строго выше порога, отказы по лимиту, деньгам игрока и доле 49 % с ключом причины; в `Rescue` добавлены необязательные `channel` и `minCashAfter`, в `StakePrice` — `roundTo`; 19 тестов `StakeMathTests`) |
| TSK-010 Исполнитель: технологии, узлы, линии | Complete (авто 2026-10-10, кусок 3: порядок шагов и правила — чистая `Logic/DealExecution.cs` (`DealExecution.Run`/`Validate`/`Draft`, `IDealWorld`, `DealRequest`, `DealOutcome`, `DealLog`, `DealStep`), игровая сторона — `Services/AcquisitionExecutor.cs` (вход `Execute(...)`: снимки, котировка и план заново в том же вызове, сверка с показанной ценой и статусом; шаг 1 — цель по `SaveID`, `UniqueID`, статус, `CapitalLedger.IsAvailable`, одна сделка за раз; шаг 3 — `temporarilySupressPopup` рефлексией, `Progress = 1` по уровням глубины с `Update(true)` после каждого уровня и до `techUnlockMaxPasses` добивочных проходов, частичный прогресс < 1; шаг 4 — `OptimizeNode` до опыта цели; шаг 6 — `RemoveProductionLines` → `ExpandFactory` со сверкой `AmountOfLines` или продажа через `CapitalLedger.Revenue(Sales)` по `SaleCost`, неизвестная игроку пластина — на наибольшую меньшую, затем `CompanyTypeUpdater.TriggerUpdate(true)`) и `GameAccess.cs` (четыре члена рефлексии); каждый шаг в `ModHooks.Guard`, сбой → `partial`; тесты `DealExecutionTests` (24) на фейках и `ExecutorSourceTests` (8) по исходнику. **В игре не запускалось**: окна исследований, прирост линий и опыт узлов проверяет владелец (M1, M5–M7, TSK-022)) |
| TSK-011 Исполнитель: CPU, связи, контракты | In Progress (авто 2026-10-10, кусок 3: `Services/AcquisitionExecutor.Cpus.cs` — шаги 5, 7, 8 по design и статической проверке спайка TSK-003 (семейства, база первой, `RetireCpuIds`, `Player.AddCpu`/`ProductionWindow.AddCpu`/`OnNewCpu`/`RaiseCpuTransferred`, собственное производство, контракты поставщика и клиента, `FoundryServices` с переключателем окна), проверено тестами на фейках и чтением исходника; не закрыт пункт «поведение соответствует итогам спайка TSK-003» — спайк в игре не проводился, остаётся владельцу: M1, M3, M7 в TSK-022) |
| TSK-012 Исполнитель: выведение, оплата, события | Complete (авто 2026-10-10, кусок 3: `Services/AcquisitionExecutor.Finish.cs` + `DealExecution`: запись в `State.deals` (`AcquisitionsState.AddDeal`) до выведения цели — тест подтверждает `IsAbsorbed = true` внутри события банкротства; выведение — `MoneyAmount = 0`, `TriggerBankruptcy()`, обработчики `OnCompanyWentBankrupt` по одному через рефлексию (свои хуки игнорируют `ExecutingTarget`, `RivalBankrupt` не поднимается); оплата последней через `CapitalLedger` (`acq.price`, затем `acq.cash` или `acq.debt`), при сбое шагов 3–8 не списывается ничего; закрытие аукциона, доли и поручения цели, `lastHostileOn`, `ModReputation.Add` с ключами `acq.reputation.*`, `DealCompleted`, письмо `acq:deal:<dealId>` (`Services/AcquisitionMail.cs`); идентичность объекта цели проверена тестом на фейке и чтением исходника; `EventBus` не используется; корневой `RivalAcquisitionsApi.cs` не нужен — фасад в `Api/`. **В игре не запускалось**: M1, M2, M6 (TSK-022)) |
| TSK-013 Хуки банкротства ИИ и аукционы | Complete (авто 2026-10-10, кусок 3: `Patches/AcquisitionPatches.cs` — P1 (postfix `BankruptcyHandler.TriggerDefault`), P2 (prefix `AICompany.TriggerBankruptcy`, пропускает `ExecutingTarget`), самопроверка целей и членов рефлексии в лог на `Register`; `Services/RivalDistressMonitor.cs` и чистая `Logic/DistressRules.cs`: письмо «X в дефолте» один раз за эпизод (`Normal`, `acq:default:<uniqueId>`, многоразовые «Сделки» и «Поручение на спасение» при доле), только ИИ, не при загрузке и перемотке, `RivalDefaulted`; аукцион на `auctionDays` со снимком CPU из P2, письмо `acq:auction:<uniqueId>`, `RivalBankrupt`, списание доли; закрытие истёкших аукционов и конец эпизодов в `LateMonthPassed`; бэкфилл на `GameReady(Loaded)` (и при включении посреди сцены), на новой игре — нет; патчей на `AddCpu` нет; минимальный обработчик действий писем (`OpenWindow`), доводка — TSK-017; тесты `DistressRulesTests` (8), `MailTagsTests` (3), `ExecutorSourceTests`. **В игре не запускалось**: M2, M3 (TSK-022)) |
| TSK-014 StakeService и поручение (prefix) | Complete (авто 2026-10-10, кусок 3: `Services/StakeService.cs` + чистая `Logic/StakeBook.cs`: покупка доли (`CapitalOut` у игрока, `MoneyAmount += P` у цели, +3 репутации для цели в дефолте, запись `Stake` в журнале, `StakeChanged`, `DealCompleted`); дивиденды в `LateMonthPassed` по отчёту прошлого месяца по `Year`/`Month` через `CapitalLedger.Revenue(Interest)` с проверкой денег цели и уведомлением `acq.stake.dividend`; P3 — prefix `AICompany.BankruptcyWarning` (`Priority` 600, всегда `true`) при активном поручении, пороге < 0, дефолте и `MoneyAmount ≤ порог`: вливание `StakeMath.Rescue` до `rescueTargetDebtLimitShare × ModCredit.DebtLimitOf(цель)`, доля, +3, запись `RescueOrder`, письмо-итог; отказ → письмо `acq:rescueFailed:<uniqueId>` с причиной, оригинал идёт как обычно; списание доли и поручения при банкротстве с письмом `acq:stakeLost:<uniqueId>`; полное поглощение учитывает долю в цене и закрывает её; при выключенной фиче ничего; тесты `StakeBookTests` (9), `ExecutorSourceTests`. **В игре не запускалось**: M4, M5, M8 (TSK-022)) |
| TSK-015 Окно «Сделки» | Complete (авто 2026-10-10, кусок 4: `UI/AcquisitionsWindow.cs` (модальное окно `ModUI.CreateWindow(…, WindowTemplate.Modal, …)`: пауза, три вкладки, чтение игры один раз при открытии со временем в `ModLog.Debug`, фокус на компании по `OpenWindow(uid)` и действию письма, сохранение прокрутки при выборе строки), `UI/CompaniesTab.cs` (таблица: цвет и название из игры, статус, деньги, линии, активные CPU, доля рынка, оценка E, доля игрока; у выбранной строки детали и кнопки сделок с ценой, закрытые — неактивны с причиной в подсказке `ModUI.Tooltip`, при дефолте игрока все закрыты), `UI/StakesTab.cs` (доля, вложено, дивиденды всего, последний дивиденд, переключатель и лимит поручения через `UI/RescueOrderControls.cs`), `UI/HistoryTab.cs` (журнал, пометка «частичная» и примечания), `UI/AcquisitionsHubPage.cs` (`ctx.HubPage("acq.hub.title", …)`, хаб закрывается перед окном); содержимое строят чистые модели `Logic/Ui/DealsTable.cs`, `StakeDialogModel.cs` (`StakesModel`, `HistoryModel`, `RescueScale`) — тесты `DealsTableTests` (18) и `StakesAndHistoryModelTests` (12); в `StakeData` добавлено поле `lastDividend` (старый сейв читает 0), `StakeBook.RecordDividend` пишет его, два теста в `AcquisitionsStateTests` и усилен `StakeBookTests`. **В игре не запускалось**: раскладка колонок, цветная метка компании (`<mark>`), подсказки на неактивных кнопках, прокрутка и время открытия проверяет владелец (M1, TSK-022)) |
| TSK-016 Превью сделки | Complete (авто 2026-10-10, кусок 4: `UI/DealPreviewWindow.cs` (модальное окно поверх «Сделок»; содержимое строит чистая `Logic/Ui/DealPreviewModel.cs`: блоки «Оценка» с пометкой о нижней границе цены и подсказкой со списком технологий и CPU, «Деньги» (цена, деньги или долг компании, итог, выручка от продажи линий, баланс после, репутация), «Что переходит» (линии по пластинам с переносом на меньшую, расходы на завод, технологии, опыт узлов, CPU, OEM-контракты, контракты поставщика и клиента, флаг литейных услуг), «Что снимается и расторгается», «Предупреждения», «Последствия»; выбор «Забрать / Продать линии (+X)» заблокирован, если линии обязательны; кнопка «Купить» включается чистым `Logic/Ui/PreviewGate.cs` только при плане без блокирующих ошибок, то есть после галочки «Я понимаю последствия»; двухшаговое подтверждение — `ModUI.Confirm` (своего диалога нет), затем `AcquisitionExecutor.Execute` с показанными ценой и статусом; `PreviewGate.TryBegin` не даёт начать вторую сделку, пока идёт первая; отказ исполнителя показывается причиной, данные читаются заново); тесты `DealPreviewModelTests` (29) и `PreviewGateTests` (11), проверки исходника в `UiSourceTests`. **В игре не запускалось**: вид окна, подсказки, стек модальных окон (превью, подтверждение) проверяет владелец (M1, M2, M6, M7, TSK-022). Блоки смежных фич (FR-16) добавит TSK-024: строки идут в `DealPreviewModel.AddTransfers`) |
| TSK-017 Диалог доли, письма, API | Complete (авто 2026-10-10, кусок 4: `UI/StakeDialog.cs` (выпадающий список долей, цена как вливание в компанию, баланс и доля после, ожидаемый дивиденд по прибыли прошлого месяца — `StakeService.LastMonthProfit` — и переключатель с лимитом поручения; «Купить долю» → `ModUI.Confirm` → `StakeService.Buy`; модель — чистая `StakeDialogModel` (20 тестов `StakeDialogModelTests`), шкала ползунка `RescueScale` держит около 100 шагов в любую эпоху); письма (кусок 3: «дефолт», «аукцион», «итог», «доля списана», «спасти не удалось», `Normal`, теги `acq:*`, многоразовые действия) и обработчик действий доработан: «Сделки» открывает окно с фокусом на компании, «Поручение на спасение» — вкладку «Мои доли» с выбранной компанией, письмо не закрывается, id письма не хранится; от письма до превью два клика (действие, кнопка сделки); API: `RivalAcquisitionsService.OpenWindow` открывает окно, `GetPlayerShare`, `IsAbsorbedByPlayer`, события с изоляцией подписчиков (`ModHooks.Raise`) — как в куске 1; тест-подписчик `Debug/ApiEventProbe.cs` (только `PTMOD_DEBUG`, флаги `debug.acqApiProbe` и `debug.acqApiProbeThrow`: один подписчик падает, второй пишет в лог). **В игре не запускалось**: M2, M4, M5 (TSK-022)) |
| TSK-018 Локализация ru/en | Complete (авто 2026-10-10, кусок 4: в `L10n/ru|en/rival-acquisitions.json` 214 ключей, все с префиксом `acq.`, ru и en совпадают по ключам и плейсхолдерам; ключи чистых моделей — `Logic/Model/UiKeys.cs`, окон — литералы в `UI/`; тесты `UiSourceTests` (15) и `ViewFormatTests` (12): каждый ключ кода есть в обеих таблицах, ни один ключ таблиц не остался без кода, в коде окон нет предложений (поиск по литералам, кириллице), тексты последствий называют Intel, AMD, GlobalFoundries, Cyrix, названия компаний в списках — из игры (`ICompany.Name`); `install.py --check` — 0 ошибок; фолбэк на en — ядра `ModL10n`)) |
| TSK-019 Пометка в таблице компаний (Could) | Complete (авто 2026-10-10, кусок 4: P4 в `Patches/AcquisitionPatches.cs` — необязательный postfix `CompaniesSpreadsheetWindow.ShowCompanies` с полями окна `___spreadsheet` и `___companies`: в колонке «Bankrupt» у банкротов, которых игрок поглотил (`RivalAcquisitionsApi.IsAbsorbedByPlayer`), через `Spreadsheet.EditRow(i, 8, …)` стоит «Поглощена игроком» (`acq.table.absorbed`) вместо «Yes»; тело под `ModHooks.Guard`, правит только ячейку уже построенной строки, поэтому при ошибке таблица остаётся ванильной; самопроверка цели в логе. **В игре не запускалось**: AC-15.1 проверяет владелец (M1))
| TSK-020 Тесты сохранения и пограничных случаев | Complete (авто 2026-10-10, кусок 5: логика секции вынесена в чистую `Logic/Model/AcquisitionsSection.cs` (`Read`, `Write`, `IsNewerThanSupported`), `AcquisitionsSaveParticipant` стал тонкой оболочкой; `AcquisitionsStateSerializationTests` (19) с `Data/sample.ptmod.json` (целый боковой файл: секция фичи, секция ядра, две чужие) прогоняют ту же логику через настоящую `SideFileModel` ядра: круг DTO ↔ `JsonObject` без потерь и без поля версии (восемь полей книги, `_schema` добавляет ядро), чужие секции и шапка файла не теряются, фича без секции получает пустую книгу, устаревшие записи (другая пара `UniqueID`/`SaveID`) игнорируются чтениями, не удаляются `Sanitize` и записываются обратно, фича выключена между сделкой и сохранением (секция остаётся как была) и включена посреди сессии, повреждённая секция читается насколько возможно, секция новее оставлена нетронутой; `EdgeCaseTests` (19): пограничные случаи 1–18 requirements, по тесту на случай (план, правила цен, арифметика долей, порядок шагов на фейке; случаи 9 и 16 — проверкой исходника и правил ядра `ModTicks`); случаи `_schema` (нет поля, новее) проверяет ядро (mod-framework/TSK-016), здесь только то, что добавляет фича. Заодно закрыт хвост NFR-3 «фича для сейва новее не предлагает сделок»: на `ModSave.AfterRead` при статусе `Newer` фича читает боковой файл, сверяет `_schema` своей секции и вызывает `AcquisitionsState.CloseOffers()`; `DealRules.Quote` для всех сделок отвечает `acq.reason.saveNewer` (свойство не сохраняется), см. `mod/requests/rival-acquisitions.md`, п. 9. Файлы тестов лежат в `mod/tests/PTMod.Tests/Features/RivalAcquisitions/` (принятый путь репозитория, а не `…/RivalAcquisitions/` из Deliverables). **В игре не запускалось**: загрузка настоящего сейва и работа блокировки на сейве новее — ручные сценарии M1, M9 (TSK-022)) |
| TSK-021 Отладочные действия | Pending |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Документация | In Progress (авто 2026-10-10, кусок 5: готовый текст раздела README «Сделки с соперниками» в `mod/requests/rival-acquisitions.md`, п. 8; вставка в `README.md` (вне файлов куска) и сверка с итогами TSK-022 — за интегратором) |
| TSK-024 Передача состояния смежных фич | Complete (авто 2026-10-10, кусок 5: шаг 9 в `Services/AcquisitionExecutor.Finish.cs` (`TransferRelatedState`) исполняет чистые правила `Logic/RelatedStateTransfer.cs` (+ `IRelatedFeatures`, `RelatedStatePlan`) через `Services/GameRelatedFeatures.cs`: при активной `cpu-architectures` перед выведением цели вызывается `ArchitectureApi.TransferCompanyState(цель, игрок, true)` (все три покупки), при активной `gpu-product-line`, живой цели (дефолт, враждебное) и `GpuApi.IsDivisionFounded(цель)` — `GpuApi.TransferVendorProducts(цель, игрок)` до `TriggerBankruptcy`; результат `false` — пометка «GPU lineup was not transferred» в `DealRecord.notes` (сделка не частичная); у банкрота `GpuApi` не вызывается вовсе, в журнале пометка; выключенные фичи и отсутствие их API пропускаются без блокировки сделки; исключение вызова под своим `ModHooks.Guard` делает сделку `partial`, другой вызов и оплата идут дальше; `DependsOn` пуст и ссылок на типы смежных фич в коде нет — API ищется по имени в сборке мода (их папок `Api/` в дереве пока нет, `feature_deps.json` их не знает), при старте в лог пишется, найдены ли три метода (см. `mod/requests/rival-acquisitions.md`, п. 9); блоки превью: `DealPreviewModel.Build(…, RelatedStatePlan)` — строки «архитектуры CPU переходят к вам», «GPU-линейка переходит к вам» и предупреждение для банкрота (ключи `acq.preview.relatedArch`, `relatedGpu`, `gpuNotPassed` в ru и en); тесты порядка шагов на фейках `ExecutionOrderTests` (20). **В игре не запускалось**: настоящие API смежных фич вызываются только в живой игре и после появления этих фич в сборке — M1, M7 (TSK-022)) |

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
