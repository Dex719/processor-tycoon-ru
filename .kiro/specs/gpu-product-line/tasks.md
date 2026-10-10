# Tasks: gpu-product-line

## Document Information

- **Feature Name**: gpu-product-line — линейка видеокарт (GPU) рядом с CPU (идея I16)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `requirements.md` (FR-1…FR-19, NFR-1…NFR-8)
  - Design: `design.md` (компоненты, патчи GP1–GP2, формулы F1–F35, тест-план)
  - Ядро: `.kiro/specs/mod-framework/tasks.md` (номера TSK-001…069 указаны ниже по сервисам), решения владельца — `.kiro/steering/owner-decisions.md`

## Implementation Overview

XL-фича делится на три поставки. Каждая заканчивается ручной проверкой владельцем и может выйти отдельно:
- **этап 1 (MVP, фазы 1–4)** — подразделение, исследования, конструктор, разработка, производство на общих линиях (в том числе режим «CPU в приоритете»), один рынок «Видеокарты» с «Прочими производителями»;
- **этап 2 (фаза 5)** — эпохи и сегменты, именные соперники (реальные названия, привязка к AMD и Intel по `UniqueID`), новости;
- **этап 3 (фаза 6)** — синергия CPU+GPU, общий опыт техпроцесса, скидка совместной разработки, `GpuApi` с передачей линейки при поглощении, консольные OEM-заказы (Could), баланс-прогон.

### Implementation Strategy
- Сначала чистая математика с тестами (порты формул игры с контрольными значениями из design.md), потом сервисы и UI.
- Все вызовы игры идут через `GpuGameAdapter` (`IGpuGame`), все вызовы ядра — через `GpuFeature`, `GpuModifierBridge` и UI. Сервисы тестируются на фейках.
- Контент — данные: балансные правки не требуют пересборки; этап 2 включается полем `marketMode`.
- Точка входа — `[ModFeature] GpuFeature : IModFeature`; фича не ставит патчи на `Company.AddCpu` / `AICompany.AddCpu`, `DateController.NextMonth`, `MenuGameSetup.InitializeUI`, не подписывается напрямую на `Player.OnNewGame` и `SaveHandler.OnLoaded`, не ведёт собственных счётчиков ошибок и блоков ID писем.
- Каждая задача 2–4 часа и заканчивается зелёной сборкой и тестами.

### Development Approach
- **Testing Strategy**: TDD для `Gpu*Math` и генератора. Тесты — мини-раннер ядра на .NET Framework 4.8 (`[Test]`, `[Covers(typeof(X))]`, `Assert.*`), файлы в `mod/tests/PTMod.Tests/GpuProductLine/`, запуск `python mod/build.py --tests`; каждый публичный тип `Logic` указан в `[Covers]`. Интеграционная симуляция без Unity (`GpuSimulationTests`). Самопроверка на старте. Ручные сценарии владельца из design.md.
- **Integration Strategy**: через сервисы `PTMod.Core` из спеки `mod-framework`; зависимости указаны номерами TSK ядра (TSK-022 — `ModFeatures` / `FeatureContext`, TSK-023 — `ModHooks`, TSK-024 — `ModTicks`, TSK-025 — `ModSave`, TSK-027 / TSK-059 — `ModModifiers`, TSK-037 / TSK-038 / TSK-062 — `ModMail`, TSK-039 — хаб и `HubPage`, TSK-058 — `ModCredit`, TSK-065 — `ConfirmBeforeDisable`).
- **Deployment Strategy**: `install.py --with-mods` (из `mod-framework`, TSK-044 / TSK-068). Фича по умолчанию включена и выключается одним переключателем в хабе «Моды».

## Фаза 1 — Каркас и данные (этап 1)

- [ ] **TSK-001**: Скелет фичи `GpuFeature : IModFeature` и регистрация в ядре
  - **Requirement**: FR-1, FR-5, FR-9, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/GpuFeature.cs` (`[ModFeature] sealed class GpuFeature : IModFeature`: `Id` = `gpu-product-line`, `NameKey`, `DescriptionKey`, `DependsOn` пустой, `EnabledByDefault` = true, `SupportsHotToggle` = true, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `GpuCapability.cs`; пять `ParamSpec` через `ctx.Param` (`rivals`, `synergy`, `news`, `consoleOrders` — включены; `cpuPriority` — выключен); страница хаба через `ctx.HubPage` с кнопкой «Видеокарты» (окно пока пустое); ключи `gpu.feature.*`, `gpu.hub.title`, `gpu.settings.*`
  - **Acceptance**: при включённой фиче на странице фичи в хабе «Моды» есть кнопка «Видеокарты» и 5 переключателей; при выключенной окно недоступно, `ModTicks` подписчиков не вызывает. Все обработчики вызываются через `GpuFeature.Guard` = `ModHooks.Guard(FeatureId, "gpu/<site>", …)`: исключение пишет `[gpu-product-line]` в лог и засчитывается фиче; собственных счётчиков ошибок и `Trip` нет. Фича не подписана напрямую на `Player.OnNewGame`, `SaveHandler.OnLoaded`, `BankruptcyHandler.OnCompanyWentBankrupt` (проверяется поиском по коду фичи)
  - **Depends**: mod-framework: TSK-022 (`ModFeatures`, `FeatureContext`), TSK-021 (`ModSettings`, `ModLog`), TSK-023 (`ModHooks.Guard`), TSK-024 (`ModTicks`), TSK-039 (хаб, `HubPage`, страница настроек)

- [ ] **TSK-002**: DTO контента и `GpuContentDb` с валидацией
  - **Requirement**: FR-10, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Content/GpuContentDtos.cs` (плоские DTO с `List<T>`, записи реализуют `IContentRecord`), `GpuContentDb.cs`, `IModContentSource` (обёртка над `ModContent.LoadRecords` / `LoadConfig`, в тестах — фикстуры)
  - **Acceptance**: загрузка файлов `{ "schema": 1, "records": [ … ] }` и объектов `balance` / `timeline`; переопределение из `PTMod/content/gpu-product-line/` по `id` (замена, `"disabled": true` удаляет). Правила валидации из design.md «Правила валидации контента» реализованы: дубликаты, ссылки, циклы `requiresNodes`, 7 значений по десятилетиям, диапазоны, формат `bindTo` (`ai:<число>`), записи `consoles`. Битая запись пропускается с логом; `IsUsable == false` при пустых обязательных наборах
  - **Depends**: TSK-001; mod-framework: TSK-010 (`ModJson.Bind`), TSK-015 и TSK-021 (`ModContent`), TSK-066 (`ModIds`)

- [ ] **TSK-003**: Контент этапа 1
  - **Requirement**: FR-10, FR-2, FR-3, FR-6
  - **Deliverables**: `mod/src/PTMod/Content/gpu-product-line/balance.json` (`marketMode: "single"`, блок `console` для этапа 3), `architectures.json` (9), `memory.json` (9), `coolers.json` (4), `research.json` (22 узла: 9 + 9 + 4), `segments.json` (`consumer`), `timeline.json`
  - **Acceptance**: значения совпадают с design.md «Контент-JSON»; файлы проходят валидацию TSK-002 без предупреждений; у каждого узла `research.json` есть `unlocks`; `requiresGameTechs` — существующие имена технологий игры (`DDR2`, `130nm`, `14nm` …)
  - **Depends**: TSK-002

- [ ] **TSK-004**: DTO сохранения, `GpuState` и `GpuSaveParticipant`
  - **Requirement**: FR-9, NFR-3, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/State/GpuSaveDtos.cs` (без поля версии), `GpuState.cs` (проверка, лимиты, `Migrate_n_to_n+1`, генератор id), `GpuSaveParticipant.cs` (`IModSaveParticipant`: `SectionId` = `gpu-product-line`, `SchemaVersion` = 1, `ResetToDefaults`, `Read(section, schema)`, `Write()` без `_schema`), регистрация `ctx.SaveParticipant`
  - **Acceptance**: правила проверки при загрузке из design.md («Правила проверки при загрузке») выполнены: NaN → умолчание, клампы, лимиты 500/32/600/32, неизвестные id, ключи компаний `^(player|ai:\d+)?$` (числовой SaveID компании в секцию не пишется). Секция отсутствует → `ResetToDefaults`. `Read(…, schema)` с `schema` меньше текущей мигрирует; секцию «новее» ядро отдаёт как значения по умолчанию с passthrough — фича их не перезаписывает
  - **Depends**: TSK-001; mod-framework: TSK-016 (модель бокового файла), TSK-025 (`ModSave`), TSK-026 (`ModCompanies`), TSK-010

- [ ] **TSK-005**: Тесты контента и сохранения
  - **Requirement**: FR-9, FR-10, NFR-3
  - **Deliverables**: `mod/tests/PTMod.Tests/GpuProductLine/GpuContentValidationTests.cs`, `GpuSaveRoundTripTests.cs`, фикстуры `mod/tests/PTMod.Tests/GpuProductLine/fixtures/*.json`
  - **Acceptance**: зелёные тесты: дубликаты id, битые ссылки, цикл узлов, неверная длина массивов, `bindTo` неверного формата; round-trip секции из design.md без потерь и без поля версии; `stock = -5` → 0; `archId` неизвестен → продукт жив по снимку; `Read(section, 0 или 1)` на старой схеме мигрирует; секция без `_schema` читается как версия 1
  - **Depends**: TSK-002, TSK-004; mod-framework: TSK-067 (мини-раннер)

## Фаза 2 — Чистая математика (этап 1)

- [ ] **TSK-006**: `GpuDesignMath` — характеристики (F2–F9)
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Math/GpuDesignMath.cs` (Units, MaxClockMHz, ComputeMops, BandwidthMBs, EffectivePerf, PowerW, TemperatureC, FailureRate); `mod/tests/PTMod.Tests/GpuProductLine/GpuDesignMathTests.cs`
  - **Acceptance**: 4 калибровочных примера из design.md (1985, 1997, 2005, 2022) совпадают с допуском 0.5 %; граничные случаи: S ≥ 1, `r` не ниже `bandwidthRatioMin`, `Temp > 95` → член `π·(T−Ts)`, нулевая частота → `Valid = false` без NaN
  - **Depends**: TSK-003; mod-framework: TSK-067

- [ ] **TSK-007**: `GpuDesignMath` — стоимость и проект (F1, F10–F14, F27) и порты формул игры
  - **Requirement**: FR-3, FR-4, FR-17
  - **Deliverables**: `GpuDesignMath.UnitCost/ProjectCost/ProjectDays/TransistorsPort/DiesPerWaferPort/RealUnitCostPort`, `Evaluate`; `mod/tests/PTMod.Tests/GpuProductLine/PortEquivalenceTests.cs`
  - **Acceptance**: порты дают те же значения, что ручной расчёт по `CpuMath.TransistorCount`, `ProductionMath.DiesPerWafer`, `CpuMath.RealUnitCost` (контрольные значения из CODEMAP, например `DPW(300, 300, 1.0) = 197`); удешевление VRAM по F12 (`sgram` 1998: `4 × 0.7^4`); стоимость проекта AC-4.1 = 1.45e8, AC-4.4 ≈ 9.72e7; `coDev` 0.85 применяется только при совпадении техпроцесса и возраста CPU ≤ 2 лет; проект не использует `CpuMath.ProjectCost` и канал `development.cost.design`
  - **Depends**: TSK-006

- [ ] **TSK-008**: `GpuResearchMath` (F15–F16)
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Math/GpuResearchMath.cs`; `mod/tests/PTMod.Tests/GpuProductLine/GpuResearchMathTests.cs`
  - **Acceptance**: AC-2.1 (`T_eff = 200` на 1 января 1996), AC-2.2 (`≈ 3700` дней для 1995 года в 1990), бонус ограничен 75 %; дневной расход = `monthlyCost × mod / 30 × funding`
  - **Depends**: TSK-003; mod-framework: TSK-067

- [ ] **TSK-009**: `GpuMarketMath` — порт формул рынка (F19–F21, F33–F34)
  - **Requirement**: FR-6, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Math/GpuMarketMath.cs` (`MarketItem`, `SegmentRuntime`, YearlySize, DecadeLerp, CalculateRatings, Normalize, Dilute, PotentialBuyers, SellDay, UpdatePopularity, VramFactor, FeatureFactor); `mod/tests/PTMod.Tests/GpuProductLine/GpuMarketMathTests.cs`
  - **Acceptance**: тест «как `Market`» на ручном примере из 3 продуктов совпадает с расчётом по формулам `Market.CalculateRatings`, `Market.NormalizeRatings`, `Market.DiluteRatings`, `Market.CalculatePotentialBuyers`. Покрыты случаи: `price ≥ 3 × budget` → 0; бесконечный запас фона; три прохода; пол и потолок популярности, затухание устаревших, `brandCarry`; `VramFactor` и `FeatureFactor` (0^0 = 1); множители каналов `gpu.demand`, `gpu.rating`, `popularity.gain` приходят числами (математика чистая)
  - **Depends**: TSK-003; mod-framework: TSK-067

- [ ] **TSK-010**: `GpuProductionMath` (F17, F18, F28, F28', F29, F31, F32)
  - **Requirement**: FR-5, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Math/GpuProductionMath.cs` (`AvailableLines`, `ScaleLines`, `Produce`, `AutoLines`, `StorageCostPerDay`); `mod/tests/PTMod.Tests/GpuProductLine/GpuProductionMathTests.cs`
  - **Acceptance**: `Produce` совпадает с формулой `Factory.ProduceWafers` на ручном примере (VRAM, плата и охлаждение только на годные; материал пластины приходит параметром — значение `ProductionMath.WaferMaterialCost`, поэтому шок `production.waferCost` проходит в затраты); `ScaleLines` — метод наибольших остатков, сумма = доступному; `AvailableLines` для режима по умолчанию (F28) и «CPU в приоритете» (F28': AC-5.5, нулевой и отрицательный остаток → 0); `AutoLines` не зацикливается при `DPW = 0` и уважает `autoMaxShare`; хранение и ликвидация по F31/F32; AC-7.1 (400 000)
  - **Depends**: TSK-003; mod-framework: TSK-067

## Фаза 3 — Игровые системы этапа 1

- [ ] **TSK-011**: `GpuGameAdapter`, `FactoryAccess` и самопроверка
  - **Requirement**: FR-3, FR-5, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Bridges/GpuGameAdapter.cs` (`IGpuGame` с `KeyOf` / `Resolve` через `ModCompanies`, `DebtThreshold` через `ModCredit.ThresholdOf`, `Channel` через `ModModifiers.Get`; кэш `ProcessNode` по имени через `DataFinder.FindLithography`), `Bridges/FactoryAccess.cs`, `GpuSelfCheck.cs`
  - **Acceptance**: самопроверка находит 6 полей, 12 вызываемых членов игры и сверяет порты F1/F10/F11 на 3 наборах; проверяет `CompanyHelper.IsEMD(1)` и `CompanyHelper.IsInlet(0)`. Строка `[gpu-product-line] self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)` в `Player.log` и на странице фичи в хабе. При отсутствии поля `Factory.totalAllocatedLines` выключается только `GpuCapability.Production`; статус патчей GP1/GP2 берётся из `ModHooks.Report`. Кэш техпроцессов строится на `GameReady` и раз в месяц, в дневных тиках `FindObjectsByType` не вызывается
  - **Depends**: TSK-007; mod-framework: TSK-023 (`ModHooks`: `Report`, `Optional`), TSK-042 (`ModSelfCheck`), TSK-026 (`ModCompanies`), TSK-058 (`ModCredit`)

- [ ] **TSK-012**: `GpuModifierBridge` — чтение каналов и провайдеры синергии
  - **Requirement**: FR-18, FR-6, FR-15
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Bridges/GpuModifierBridge.cs` (`Get`, `Demand`, `Explain`, `RegisterSynergyProviders`, `InvalidateSynergy`); собственного класса констант каналов нет — используются `ModChannels`
  - **Acceptance**: каналы `gpu.demand`, `gpu.demand.<segmentId>`, `gpu.rating`, `gpu.vramCost` объявлены ядром (`ModModifiers.IsDeclared` = true), фича `DeclareChannel` не вызывает (проверка поиском по коду). `Get` возвращает 1.0 без вкладов и произведение при нескольких; контекст `{Company, CompanyKey}` без CPU и сегмента; `company == null` — глобальный контекст. Чтение `production.unitCost`, `production.output`, `popularity.gain` подключено в F12/F17/F21. Регистрация провайдеров один раз; повторный вызов того же делегата ядро игнорирует
  - **Depends**: TSK-001; mod-framework: TSK-027 (`ModModifiers`), TSK-059 (`RegisterReasonedProvider`, `Invalidate`, `Explain`, ленивое связывание), TSK-043 (`core.json` с каналами)

- [ ] **TSK-013**: Основание GPU-подразделения (`GpuDivision`)
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuDivision.cs` (`Check` с порядком проверок: уже основано → год рынка → чистый литейщик (`ICompany.get_IsCpuCompany`) → кредит по `ModCredit.ThresholdOf`; `Found` — стоимость F30, списание `ExpenseOrigin.Development`, `GpuResearch.UnlockUpToYear`; `FoundFree` для передачи линейки)
  - **Acceptance**: AC-1.1, AC-1.3, AC-1.4, AC-1.5 на фейковом `IGpuGame`; `CentralBank.DebtLimit` в коде не читается; повторное основание невозможно; дата основания сохраняется
  - **Depends**: TSK-004, TSK-011, TSK-014; mod-framework: TSK-058 (`ModCredit`)

- [ ] **TSK-014**: `GpuResearch` — лаборатория GPU
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuResearch.cs` (очередь до 5, финансирование 10–100 %, `TickPhase.Tick` через `ctx.OnTick`, расходы `ExpenseOrigin.Research`, `Completed`, `ModNotify`, предпосылки на технологии игры через предикат ядра `techResearched`)
  - **Acceptance**: AC-2.1…AC-2.4 на фейках; `UnlockUpToYear(1992)` открывает ровно 5 узлов этапа 1 (`arch.2d`, `arch.gui`, `vram.dram`, `vram.vram`, `cool.passive`); во время перемотки и загрузки прогресс не растёт (подписчик без `RunDuringFastForward`)
  - **Depends**: TSK-008, TSK-011; mod-framework: TSK-024 (`ModTicks`), TSK-032 (`ModConditions`), TSK-033 (`ModNotify`)

- [ ] **TSK-015**: `GpuProjects` — разработка
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuProjects.cs`
  - **Acceptance**: AC-4.1, AC-4.2; лимит `maxParallelProjects`; дневное списание по F14 с поправкой на инфляцию (фаза `TickPhase.Tick`); отмена без возврата денег; по завершении — продукт со статусом `ready` и событие `ReadyToRelease`; снимок `stats` сохранён в дизайне; множитель стоимости берётся из `Modifiers.GetModifier(ModifierType.DevelopmentCost, …)`, `CpuMath.ProjectCost` не вызывается
  - **Depends**: TSK-007, TSK-011; mod-framework: TSK-024

- [ ] **TSK-016**: `GpuProduction` — резерв линий и выпуск на своей фабрике
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuProduction.cs` (фаза `TickPhase.PreContractSellTick`, F28 / F28' с клиентами-литейщиками через `BusinessContractManager.GetFoundryServicesContract` / `BusinessContract.ContractedProductionCapacity` и суммой линий CPU (`CpuLinesInUse`) в режиме `cpuPriority`, F17 с вызовом `ProductionMath.WaferMaterialCost`, F29, пересчёт `U` по F12, `ReservedOn(node)`)
  - **Acceptance**: на фейковой фабрике выполнены AC-5.1…AC-5.3, AC-5.5 и AC-5.6; `totalAllocatedLines` после резерва ≤ `ProductionCapacity`; расходы по статье `Factories`; режим «авто» по F29; предупреждение «Не хватает линий» в состоянии продукта; переключатель `cpuPriority` читается каждый день (`ModSettings`)
  - **Depends**: TSK-010, TSK-011, TSK-015; mod-framework: TSK-024

- [ ] **TSK-017**: `GpuProduction` — fabless и смена типа компании
  - **Requirement**: FR-5
  - **Deliverables**: ветка аутсорса в `GpuProduction` (F18: наценка `ProductionMath.BaseMarkupPrice`, роялти `ProductionMath.BaseRoyalties`, выход годных `fablessYieldProgress`); переход «стала fabless» (линии → шт./день) и урезание при уменьшении мощности
  - **Acceptance**: AC-5.4; при `get_IsFabless() == true` резерв в `Factory` не делается; после продажи линий резерв урезан в тот же день, ручные значения сохранены
  - **Depends**: TSK-016

- [ ] **TSK-018**: `GpuRivalGenerator` — генератор дизайна по году (F22–F23)
  - **Requirement**: FR-6 (фон), FR-13
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Rivals/GpuRivalGenerator.cs`, `IGpuRandom` поверх `ModRandom` (потоки `gpu-product-line/rivals` и `gpu-product-line/background`); `mod/tests/PTMod.Tests/GpuProductLine/GpuRivalGeneratorTests.cs`
  - **Acceptance**: одинаковый сид → одинаковые дизайны; выбор архитектуры, памяти и охлаждения по `techYear` (AC-13.2); `Temp ≤ 85`; VRAM — степень двойки ≤ `maxVramMB`; цена ≥ `1.3 × U`
  - **Depends**: TSK-007, TSK-009; mod-framework: TSK-012 (логика `ModRandom`), TSK-025 (`ModRandom` в сейве)

- [ ] **TSK-019**: `GpuMarketService` — дневной рынок этапа 1, фон и засев
  - **Requirement**: FR-6, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuMarketService.cs` (фазы `MarketSellTick`, `DayPassed`, `MonthPassed`; `SeedForYear`, фон по `background`, чтение каналов `gpu.demand`, `gpu.rating`, `popularity.gain` через `GpuModifierBridge`, выручка `EarnMoney(…, Sales)`); засев на `TickPhase.FastForwardEnded` (новая игра) и `ModTicks.GameReady(Loaded)` (сейв без секции)
  - **Acceptance**: AC-6.1…AC-6.4 на фейках; рынок идёт без подразделения игрока (FR-6 п. 8); засев один раз (флаг `market.seeded`) на старте новой игры и при загрузке сейва без секции (AC-9.1); фон обновляется раз в 12 месяцев
  - **Depends**: TSK-009, TSK-012, TSK-018; mod-framework: TSK-024 (`FastForwardEnded`, `GameReady`), TSK-025 (`ModSave`)

- [ ] **TSK-020**: `GpuLineup` — управление линейкой
  - **Requirement**: FR-7, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuLineup.cs` (Release, SetPrice, SetSelling, SetLines, SetUnitsPerDay, SetAuto, Retire F31, хранение F32, помесячная бухгалтерия, метка «устарел»)
  - **Acceptance**: AC-7.1…AC-7.3; цена применяется со следующего рыночного тика; бухгалтерия не длиннее 600 месяцев; снятый продукт не участвует в рынке и резерве
  - **Depends**: TSK-016, TSK-019

- [ ] **TSK-021**: Патч GP1 — линии GPU на экране производства
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Patches/ProductionWindowPatch.cs` (`[ModPatch(…, PatchKind.Postfix, Optional = true)]` на `ProductionWindow.UpdateAvailableProductionLines`; поля `linesUsedText`, `productionUIs`, `cpus`, `clientCpus`), регистрация `ctx.Patch`
  - **Acceptance**: при резерве N > 0 текст «Использовано» дополнен « (+N GPU)», в режиме по умолчанию строки CPU получают `ProductionUI.UpdateAvailableProductionLines(free − N)`, при «CPU в приоритете» — без уменьшения; при N = 0 вывод ванильный; исключение в постфиксе (`ModHooks.Guard`) не ломает экран, а неприменённый патч выключает только `GpuCapability.ProductionUi`
  - **Depends**: TSK-011, TSK-016; mod-framework: TSK-023

## Фаза 4 — UI этапа 1 и проверка

- [ ] **TSK-022**: Окно «Видеокарты» и диалог основания
  - **Requirement**: FR-1, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/UI/GpuWindow.cs`, `UI/GpuFoundDialog.cs` (вкладки `ButtonTabGroup`, модальность `PauseTrigger`, последняя открытая вкладка — `ModFlags.SetInt`); ярлык рабочего стола не создаётся (`ModUI` его не предоставляет)
  - **Acceptance**: до основания открывается диалог со стоимостью и списком узлов, блокировки с причинами (AC-1.3, AC-1.4, AC-1.5); после — 4 вкладки, последняя открытая запоминается; тема светлая и тёмная применяется (перекраска — `ModUI`)
  - **Depends**: TSK-013; mod-framework: TSK-035 (`ModWindow`), TSK-036 (виджеты), TSK-039 (хаб)

- [ ] **TSK-023**: Вкладка «Исследования»
  - **Requirement**: FR-2, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/UI/GpuResearchTab.cs`
  - **Acceptance**: три ветки; состояния узлов и подсказки (год, `T_eff`, расход, предпосылки, «опережает время»); `ProgressBar` текущего узла; ползунок финансирования сразу меняет срок и расход в UI; очередь до 5 с удалением; подсказки через `ModUI.Tooltip`
  - **Depends**: TSK-014, TSK-022; mod-framework: TSK-064 (мост подсказок)

- [ ] **TSK-024**: Вкладка «Конструктор»
  - **Requirement**: FR-3, FR-17 (строка скидки), NFR-1, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/UI/GpuDesignerTab.cs`
  - **Acceptance**: AC-3.1…AC-3.3 в UI; пересчёт только по `OnValueChanged` (замер ≤ 1 мс); таблица «Оценка по сегментам»; предупреждения «Упор в память», «Перегрев», блок «Кристалл не помещается»; имя ≤ 32 символов без TMP-тегов; «Разработать» создаёт проект; подсказки характеристик через `ModUI.Tooltip`
  - **Depends**: TSK-015, TSK-022; mod-framework: TSK-064

- [ ] **TSK-025**: Диалог «Выпуск GPU» и вкладка «Линейка»
  - **Requirement**: FR-4, FR-7, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/UI/GpuReleaseDialog.cs`, `UI/GpuLineupTab.cs` (строка режима линий «GPU / CPU в приоритете»)
  - **Acceptance**: AC-4.3; «Позже» оставляет статус «Готов к выпуску» с кнопкой «Выпустить»; в линейке редактируются цена, продажа, линии / шт. в день, «Авто»; проекты с паузой и отменой; снятие с подтверждением `ModUI.Confirm`; P&L за прошлый месяц
  - **Depends**: TSK-020, TSK-022; mod-framework: TSK-064 (`ModUI.Confirm`)

- [ ] **TSK-026**: Вкладка «Рынок» (этап 1)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/UI/GpuMarketTab.cs`
  - **Acceptance**: для `consumer`: покупатели в месяц, бюджет, требование VRAM, топ-5, доли «Прочих» и игрока за 30 дней, продажи игрока за 24 месяца; обновление раз в день только при открытом окне (AC-8.2)
  - **Depends**: TSK-019, TSK-022

- [ ] **TSK-027**: Строки ru/en этапа 1
  - **Requirement**: NFR-4
  - **Deliverables**: ключи `gpu.*` в `mod/src/PTMod/L10n/ru.json` и `mod/src/PTMod/L10n/en.json` (окно, вкладки, конструктор, линейка, рынок, уведомления, настройки, названия архитектур, памяти, охлаждения, сегмента `consumer`)
  - **Acceptance**: тест `mod/tests/PTMod.Tests/GpuProductLine/GpuL10nKeysTests.cs` находит все ключи, используемые в коде и контенте, в обоих файлах; все ключи начинаются с `gpu.` (проверка `ModIds`); числа форматируются `StringFormatter.FloatToMoney` / `FloatToFrequency` / `FloatToPercentageString`
  - **Depends**: TSK-022…TSK-026; mod-framework: TSK-013 и TSK-043 (`ModL10n`, строки ядра), TSK-066 (`ModIds`)

- [ ] **TSK-028**: Интеграционная симуляция этапа 1 и замер производительности
  - **Requirement**: FR-5, FR-6, NFR-1, NFR-2, NFR-7
  - **Deliverables**: `mod/tests/PTMod.Tests/GpuProductLine/GpuSimulationTests.cs` (фейковые `IGpuGame` и фабрика, бот-игрок: основание 1995, карта каждые 2 года, авто-линии), отладочный `Stopwatch` в обработчиках
  - **Acceptance**: прогон 1981–2010 без исключений; дневная работа ≤ 0.5 мс и месячная ≤ 5 мс на машине владельца (лог); «Прочие» ≥ 15 % рынка до 2000
  - **Depends**: TSK-019, TSK-020

- [ ] **TSK-029**: Ручная проверка этапа 1 в игре (владелец)
  - **Requirement**: FR-1…FR-10
  - **Deliverables**: отчёт владельца по сценариям 1–11, 20 и 21 из design.md «Ручные сценарии» (отметки ✔/✘ и `Player.log` при сбое) в `mod/tests/manual/gpu/stage1-report.md`; сейвы сценариев в `mod/tests/manual/gpu/`
  - **Acceptance**: все сценарии пройдены; найденные дефекты заведены отдельными задачами и закрыты
  - **Depends**: TSK-021, TSK-023…TSK-028; mod-framework: TSK-044 (`install.py --with-mods`), TSK-053

## Фаза 5 — Эпохи и соперники (этап 2)

- [ ] **TSK-030**: Контент эпох
  - **Requirement**: FR-11
  - **Deliverables**: `segments.json` — 6 сегментов режима `eras` (`office2d`, `gaming.high`, `gaming.mid`, `gaming.low`, `compute`, `ai`) по таблице design.md; `eras.json` (6 эпох); в `balance.json` `marketMode: "eras"` для этапа 2
  - **Acceptance**: валидация без предупреждений; требования функций и VRAM заданы контрольными годами; в 2004 открыты 4 сегмента (AC-8.1)
  - **Depends**: TSK-003

- [ ] **TSK-031**: `GpuEras` — открытие сегментов, требования, миграция режима
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuEras.cs`; подключение F33/F34 в `GpuMarketService`; `ApplyModeSwitchIfNeeded` (вызывается на `GameReady(Loaded)`)
  - **Acceptance**: AC-11.1…AC-11.3; при открытии сегмента у активных продуктов популярность 0.05 и событие `SegmentOpened`; закрытый сегмент не продаёт; сейв этапа 1 мигрирует (`consumer` → открытые сегменты эпох с коэффициентом 1.0); открытие и закрытие по разности дат (догоняет после простоя)
  - **Depends**: TSK-019, TSK-030

- [ ] **TSK-032**: Контент вендоров
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Content/gpu-product-line/vendors.json` (7 вендоров из design.md: Matrox, ATI, S3, NVIDIA, 3dfx, AMD (`ai:1`), Intel (`ai:0`); поле `series` для названий продуктов; реальные названия — решение владельца)
  - **Acceptance**: валидация: сегменты существуют, `mergeInto` указывает на вендора, `bindTo` ∈ {"", "ai:0", "ai:1"}; в контенте нет пародийных имён (Envidia, ATY, 3Dfox, Matrax, S4, EMD, Inlet)
  - **Depends**: TSK-030

- [ ] **TSK-033**: `GpuRivals` — реестр, вход, уход, слияние, привязка к ИИ
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Rivals/GpuRivals.cs` (`ResolveBinding` через `ModCompanies.Resolve("ai:<UniqueID>")`; `CheckBoundCompanies` на `TickPhase.DayPassed` по `ICompany.get_IsBankrupt` вместо подписки на `BankruptcyHandler.OnCompanyWentBankrupt`; `fallbackVirtual`; переключатель `rivals`; название продукта `<series> <launchCount>`)
  - **Acceptance**: AC-12.1…AC-12.4 на фейках; виртуальные вендоры не создают компаний игры; при выключенном `rivals` на рынке только фон; после загрузки сейва состояния вендоров восстановлены; прямых подписок на события игры нет
  - **Depends**: TSK-018, TSK-032; mod-framework: TSK-026 (`ModCompanies`), TSK-024

- [ ] **TSK-034**: `GpuRivalAI` — каденция, цены, мощность, финансы привязанных
  - **Requirement**: FR-13
  - **Deliverables**: месячный цикл в `GpuRivals` (`designIntervalMonths` + 0–3 мес. `ModRandom`, ≤ 2 продукта на сегмент), F23–F24; деньги привязанных — `AICompany.EarnMoney(…, Sales)`, `AICompany.SpendMoney(…, Factories / Development)`; пауза при `ICompany.get_IsInDefault` и при уходе баланса ниже `ModCredit.ThresholdOf`
  - **Acceptance**: AC-13.1, AC-13.3, AC-13.4 на фейках; запас вендора каждый день пополняется до `capacityShare × N_s`; самый старый из 3 продуктов снимается; вендоры не резервируют линий фабрики
  - **Depends**: TSK-033; mod-framework: TSK-058 (`ModCredit`)

- [ ] **TSK-035**: Тесты эпох и соперников
  - **Requirement**: FR-11, FR-12, FR-13, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/GpuProductLine/GpuErasTests.cs`, `GpuRivalsTests.cs`, расширение `GpuSimulationTests` режимом `eras`
  - **Acceptance**: детерминизм: два прогона 1990–2010 с одним сидом дают одинаковые журналы запусков; слияние ATI → AMD в 2006 переносит продукты с популярностью; слияние 3dfx → NVIDIA в 2001; ни один вендор не держит > 70 % сегмента дольше 3 лет (NFR-7)
  - **Depends**: TSK-031, TSK-034; mod-framework: TSK-067

- [ ] **TSK-036**: `GpuNews` — письма рынка GPU
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuNews.cs` (`ModMail.Send`: эпохи и вендоры — `Normal`, новый лидер с паузой 3 мес. на сегмент — `Low`; теги `gpu:era:…`, `gpu:segment:…`, `gpu:vendor:<id>:<enter|exit|merge>`, `gpu:leader:<segmentId>`; переключатель `news`; письма только о событиях не старше предыдущего месяца)
  - **Acceptance**: AC-14.1, AC-14.2; во время перемотки и загрузки писем нет; письма об эпохах приходят и при выключенном `news`; фича не выбирает ID писем и не вводит дневных лимитов (лимиты и очередь — у ядра)
  - **Depends**: TSK-031, TSK-033; mod-framework: TSK-037 и TSK-062 (`ModMail`: приоритеты, `Tag`), TSK-066 (`ModIds.IsTagAllowed`)

- [ ] **TSK-037**: UI рынка этапа 2
  - **Requirement**: FR-8, FR-11, FR-12
  - **Deliverables**: расширение `GpuMarketTab` (блоки сегментов, требования функций и VRAM, доли вендоров с цветами, текущая эпоха и год следующей); пометки «Нет: <функция>» в линейке и конструкторе
  - **Acceptance**: AC-8.1, AC-11.2 в UI; при 6 сегментах обновление окна ≤ 2 мс
  - **Depends**: TSK-026, TSK-031, TSK-033

- [ ] **TSK-038**: Строки ru/en этапа 2
  - **Requirement**: NFR-4
  - **Deliverables**: ключи `gpu.seg.*`, `gpu.era.*`, `gpu.mail.*`, названия серий вендоров в `ru.json` / `en.json`
  - **Acceptance**: `GpuL10nKeysTests` зелёный для контента этапа 2
  - **Depends**: TSK-036, TSK-037

- [ ] **TSK-039**: Ручная проверка этапа 2 в игре (владелец)
  - **Requirement**: FR-11…FR-14
  - **Deliverables**: отчёт по сценариям 12–15 и 19 (миграция этап 1 → 2) в `mod/tests/manual/gpu/stage2-report.md`
  - **Acceptance**: все сценарии пройдены; сейв этапа 1 продолжает партию в сборке этапа 2
  - **Depends**: TSK-035…TSK-038; mod-framework: TSK-053

## Фаза 6 — Синергия и интеграция (этап 3)

- [ ] **TSK-040**: `GpuSynergy` — платформенная синергия
  - **Requirement**: FR-15
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Math/GpuSynergyMath.cs`, `Services/GpuSynergy.cs` (кольца долей за 30 дней по `MarketDataProvider.MarketShare` и продажам GPU, месячный пересчёт в словарь по ключам компаний, провайдеры `market.rating.cpu` и `gpu.rating` через `RegisterReasonedProvider` для игрока и привязанных компаний ИИ, `ModModifiers.Invalidate`); блок синергии с `Explain` во вкладке «Рынок»
  - **Acceptance**: AC-15.1, AC-15.2, AC-15.3; вклад в `market.rating.cpu` ≤ ×1,08 и в `gpu.rating` ≤ ×1,10 (тест `GpuSynergyTests`); при выключенном `synergy` провайдеры возвращают 1,0 в тот же кадр после `Invalidate`; кольца долей сохраняются в секции; записей `ModModifiers.Add` и `RemoveAll` фича не использует
  - **Depends**: TSK-012, TSK-019, TSK-033; mod-framework: TSK-027 и TSK-059 (`ModModifiers`: провайдеры, балансовый диапазон), TSK-067

- [ ] **TSK-041**: Патч GP2 — общий опыт техпроцесса
  - **Requirement**: FR-16
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Patches/TotalLinesAllocatedPatch.cs` (`[ModPatch(…, PatchKind.Postfix, Optional = true)]` на `Company.TotalLinesAllocated`)
  - **Acceptance**: AC-16.1 на фейке; только для `Player.Instance.get_Company()`; при выключенной синергии прибавка 0; неприменённый патч выключает только `GpuCapability.SharedLearning`
  - **Depends**: TSK-016; mod-framework: TSK-023

- [ ] **TSK-042**: Скидка совместной разработки
  - **Requirement**: FR-17
  - **Deliverables**: вычисление `coDev` (F27) в `GpuDesignerTab` и `GpuProjects`
  - **Acceptance**: AC-17.1; строка «Совместная разработка: −15 %» видна только при выполненном условии; список CPU читается по запросу (`GetCpus` компании), подписки на `ModEvents.CpuReleased` нет
  - **Depends**: TSK-024, TSK-015

- [ ] **TSK-043**: `GpuApi`, предикаты `ModConditions`, передача линейки
  - **Requirement**: FR-18, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/GpuApi.cs` (методы из design.md, событие `ProductReleased`), регистрация `gpu.divisionFounded`, `gpu.hasProductOnMarket`, `gpu.marketShareAtLeast`, `gpu.researched` через `ModConditions.Register(type, "gpu-product-line", factory)`; `GpuRivals.TransferProducts`, `GpuApi.TransferVendorProducts` (с `GpuDivision.FoundFree`, если подразделение игрока не основано)
  - **Acceptance**: AC-18.1 (запись `gpu.demand` × 1.5 на 90 дней через `ModifierTimed` на фейке), AC-18.2, AC-18.3, AC-18.4; при выключенной фиче `GpuApi.IsAvailable == false`, предикаты возвращают false; имена предикатов проходят `ModIds.IsPredicateNameAllowed`
  - **Depends**: TSK-019, TSK-033; mod-framework: TSK-019 и TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

- [ ] **TSK-044**: Тесты этапа 3
  - **Requirement**: FR-15, FR-16, FR-17, FR-18
  - **Deliverables**: `mod/tests/PTMod.Tests/GpuProductLine/GpuSynergyTests.cs`, `GpuSynergyMathTests.cs`, `GpuApiTests.cs`
  - **Acceptance**: F25 — клампы и нули; перемножение с чужим вкладом в канале и ограничение 2,0 (AC-15.3); `TransferProducts` переносит продукты с популярностью и ключом компании владельца; предикаты на граничных значениях
  - **Depends**: TSK-040…TSK-043; mod-framework: TSK-067

- [ ] **TSK-045**: Баланс-прогон и калибровка контента
  - **Requirement**: NFR-7, NFR-1
  - **Deliverables**: сценарий `GpuSimulationTests.BalanceReference` (IDM 1990, Normal, основание 1995) и отчёт `mod/tests/PTMod.Tests/GpuProductLine/balance-report.md`; правки чисел только в `Content/gpu-product-line/*.json`; калибровка черновых чисел `balance.console` и `consoles.json`
  - **Acceptance**: все 4 ориентира NFR-7 выполнены; дневная работа ≤ 0.5 мс при 150 продуктах; изменения баланса перечислены в отчёте с причиной
  - **Depends**: TSK-035, TSK-044

- [ ] **TSK-046**: Документация для игроков и моддеров
  - **Requirement**: FR-10, FR-9
  - **Deliverables**: раздел «Видеокарты» в `README.md` проекта (как начать, копирование сейва вместе с `.ptmod.json`, переключатели, режим «CPU в приоритете»); `mod/src/PTMod/Content/gpu-product-line/README.md` (поля контента, единицы, формат `{ "schema": 1, "records": [ … ] }`, переопределение по `id` и `disabled`, примеры)
  - **Acceptance**: каждое поле контент-DTO описано с единицами; пример переопределения бюджета сегмента работает (AC-10.2)
  - **Depends**: TSK-030, TSK-032

- [ ] **TSK-047**: Ручная проверка этапа 3 и совместимости сейвов (владелец)
  - **Requirement**: FR-9, FR-15, FR-16, FR-17, FR-18, FR-19, NFR-3
  - **Deliverables**: отчёт по сценариям 16–19, 22, 23 и повтор 9–11 в `mod/tests/manual/gpu/stage3-report.md`; результат `python tools/check_spec_refs.py --code <pt-modding/code> --core-api .kiro/specs/mod-framework/design.md` для этой спеки
  - **Acceptance**: все сценарии пройдены; цепочка «ванильный сейв → этап 1 → этап 2 → этап 3 → без мода» загружается без ошибок; проверка ссылок без ошибок
  - **Depends**: TSK-040…TSK-046, TSK-048, TSK-049; mod-framework: TSK-047 (`check_spec_refs.py`), TSK-054

- [ ] **TSK-048**: Консольные заказы — контент и чистая логика (Could)
  - **Requirement**: FR-19
  - **Deliverables**: `mod/src/PTMod/Content/gpu-product-line/consoles.json` (6 записей из design.md), блок `console` в `balance.json`, `mod/src/PTMod/Features/GpuProductLine/Math/GpuConsoleMath.cs` (F35: `Qualifies`, `OrderPrice`, `DailyUnits`, `MissedShare`, `ShouldCancel`), `GpuConsoleDto`; `mod/tests/PTMod.Tests/GpuProductLine/GpuConsoleMathTests.cs`
  - **Acceptance**: F35 на ручных примерах (порог 0,6 от лучшей карты, цена `max(1,15·U, 0,55·бюджет)`, объём `floor(0,25·N_s)`); расторжение не раньше `graceDays` и только при недопоставке > 25 %; валидация `consoles.json` без предупреждений
  - **Depends**: TSK-003, TSK-009; mod-framework: TSK-067

- [ ] **TSK-049**: Консольные заказы — сервис, письмо-предложение и блок во вкладке «Линейка» (Could)
  - **Requirement**: FR-19, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/GpuProductLine/Services/GpuConsoleOrders.cs` (предложения по `offerYear`, письмо `Critical` с тегом `gpu:console:<orderId>`, `TimeoutDays` 60, `RegisterChoiceHandler`, `SetChoiceState` + `Refresh` на `ModMail.Delivered` и раз в `revalidateDays`, поставка на `TickPhase.ContractSellTick`, `EarnMoney(…, Contracts)`, штраф `ModReputation.Add`, `BuildDisableConfirmation` для `ctx.ConfirmBeforeDisable`, расторжение в `OnDisabled` и при снятии закреплённого GPU), блок «Консольные заказы» в `GpuLineupTab`, ключи `gpu.console.*`; `mod/tests/PTMod.Tests/GpuProductLine/GpuConsoleOrdersTests.cs` (на фейках)
  - **Acceptance**: AC-19.1…AC-19.5 на фейках; состояние кнопок писем пересчитывается после загрузки (`GameReady(Loaded)`); ID письма хранится в `GpuConsoleOrderSave.mailId`; заказ не создаётся во время перемотки и загрузки; выручка не проходит через канал `contract.reward`
  - **Depends**: TSK-048, TSK-019, TSK-020, TSK-025; mod-framework: TSK-024, TSK-026 (`ModReputation`), TSK-038 и TSK-062 (`ModMail`: выбор, `SetChoiceState`, `Delivered`), TSK-065 (`ConfirmBeforeDisable`)

## Dependency graph

Записи `mod-framework: TSK-NNN` — задачи ядра (`.kiro/specs/mod-framework/tasks.md`).

```
mod-framework (TSK-022 ModFeatures, TSK-023 ModHooks, TSK-024 ModTicks, TSK-025 ModSave, TSK-021 ModSettings/ModContent/ModLog,
               TSK-039 хаб, TSK-027+059 ModModifiers, TSK-043 core.json)
  └─> TSK-001

Этап 1
  TSK-001 ──> TSK-002 ──> TSK-003
  TSK-001 ──> TSK-004 ──┐
  TSK-002 ──────────────┴─> TSK-005   (+ mod-framework TSK-067)
  TSK-001 ──> TSK-012                 (+ mod-framework TSK-027, TSK-059, TSK-043)
  TSK-003 ──> TSK-006 ──> TSK-007 ──> TSK-011   (+ mod-framework TSK-023, TSK-042, TSK-026, TSK-058)
  TSK-003 ──> TSK-008 ; TSK-003 ──> TSK-009 ; TSK-003 ──> TSK-010
  TSK-008 + TSK-011 ─────────────> TSK-014   (+ mod-framework TSK-024, TSK-032, TSK-033)
  TSK-004 + TSK-011 + TSK-014 ───> TSK-013   (+ mod-framework TSK-058)
  TSK-007 + TSK-011 ─────────────> TSK-015
  TSK-010 + TSK-011 + TSK-015 ───> TSK-016 ──> TSK-017
  TSK-007 + TSK-009 ─────────────> TSK-018   (+ mod-framework TSK-025)
  TSK-009 + TSK-012 + TSK-018 ───> TSK-019   (+ mod-framework TSK-024, TSK-025)
  TSK-016 + TSK-019 ─────────────> TSK-020
  TSK-011 + TSK-016 ─────────────> TSK-021   (+ mod-framework TSK-023)
  TSK-013 ──> TSK-022 ──┬─> TSK-023 (+TSK-014)   (+ mod-framework TSK-035, TSK-036, TSK-039, TSK-064)
                        ├─> TSK-024 (+TSK-015)
                        ├─> TSK-025 (+TSK-020)
                        └─> TSK-026 (+TSK-019)
  TSK-022…TSK-026 ───────────────> TSK-027   (+ mod-framework TSK-013, TSK-043, TSK-066)
  TSK-019 + TSK-020 ─────────────> TSK-028
  TSK-021 + TSK-023…TSK-028 ─────> TSK-029   [этап 1 принят владельцем]   (+ mod-framework TSK-044, TSK-053)

Этап 2
  TSK-003 ──> TSK-030 ──┬─> TSK-031 (+TSK-019)
                        └─> TSK-032 ──> TSK-033 (+TSK-018; mod-framework TSK-026) ──> TSK-034 (+ mod-framework TSK-058)
  TSK-031 + TSK-034 ─────────────> TSK-035
  TSK-031 + TSK-033 ─────────────> TSK-036   (+ mod-framework TSK-037, TSK-062, TSK-066)
  TSK-026 + TSK-031 + TSK-033 ───> TSK-037
  TSK-036 + TSK-037 ─────────────> TSK-038
  TSK-035…TSK-038 ───────────────> TSK-039   [этап 2 принят владельцем]

Этап 3
  TSK-012 + TSK-019 + TSK-033 ───> TSK-040   (+ mod-framework TSK-027, TSK-059)
  TSK-016 ───────────────────────> TSK-041   (+ mod-framework TSK-023)
  TSK-015 + TSK-024 ─────────────> TSK-042
  TSK-019 + TSK-033 ─────────────> TSK-043   (+ mod-framework TSK-019, TSK-032, TSK-066)
  TSK-040…TSK-043 ───────────────> TSK-044 ──> TSK-045 (+TSK-035)
  TSK-030 + TSK-032 ─────────────> TSK-046
  TSK-003 + TSK-009 ─────────────> TSK-048 (Could) ──> TSK-049 (+TSK-019, TSK-020, TSK-025; mod-framework TSK-038, TSK-062, TSK-065)
  TSK-040…TSK-046 + TSK-048 + TSK-049 ──> TSK-047   [этап 3 принят владельцем]   (+ mod-framework TSK-047, TSK-054)
```

## Progress

| Задача | Этап | Статус |
|---|---|---|
| TSK-001 Скелет фичи и регистрация | 1 | Pending |
| TSK-002 DTO контента и валидация | 1 | Pending |
| TSK-003 Контент этапа 1 | 1 | Pending |
| TSK-004 DTO сохранения, GpuState, GpuSaveParticipant | 1 | Pending |
| TSK-005 Тесты контента и сохранения | 1 | Pending |
| TSK-006 GpuDesignMath: характеристики | 1 | Pending |
| TSK-007 GpuDesignMath: стоимость, проект, порты | 1 | Pending |
| TSK-008 GpuResearchMath | 1 | Pending |
| TSK-009 GpuMarketMath | 1 | Pending |
| TSK-010 GpuProductionMath | 1 | Pending |
| TSK-011 GpuGameAdapter, FactoryAccess, самопроверка | 1 | Pending |
| TSK-012 GpuModifierBridge | 1 | Pending |
| TSK-013 Основание подразделения | 1 | Pending |
| TSK-014 GpuResearch | 1 | Pending |
| TSK-015 GpuProjects | 1 | Pending |
| TSK-016 GpuProduction: резерв и выпуск | 1 | Pending |
| TSK-017 GpuProduction: fabless и переходы | 1 | Pending |
| TSK-018 GpuRivalGenerator | 1 | Pending |
| TSK-019 GpuMarketService | 1 | Pending |
| TSK-020 GpuLineup | 1 | Pending |
| TSK-021 Патч GP1 (экран производства) | 1 | Pending |
| TSK-022 Окно и диалог основания | 1 | Pending |
| TSK-023 Вкладка «Исследования» | 1 | Pending |
| TSK-024 Вкладка «Конструктор» | 1 | Pending |
| TSK-025 Выпуск и «Линейка» | 1 | Pending |
| TSK-026 Вкладка «Рынок» (этап 1) | 1 | Pending |
| TSK-027 Строки ru/en этапа 1 | 1 | Pending |
| TSK-028 Интеграционная симуляция и замеры | 1 | Pending |
| TSK-029 Ручная проверка этапа 1 (владелец) | 1 | Pending |
| TSK-030 Контент эпох | 2 | Pending |
| TSK-031 GpuEras | 2 | Pending |
| TSK-032 Контент вендоров | 2 | Pending |
| TSK-033 GpuRivals | 2 | Pending |
| TSK-034 GpuRivalAI | 2 | Pending |
| TSK-035 Тесты эпох и соперников | 2 | Pending |
| TSK-036 GpuNews | 2 | Pending |
| TSK-037 UI рынка этапа 2 | 2 | Pending |
| TSK-038 Строки ru/en этапа 2 | 2 | Pending |
| TSK-039 Ручная проверка этапа 2 (владелец) | 2 | Pending |
| TSK-040 GpuSynergy | 3 | Pending |
| TSK-041 Патч GP2 (общий опыт техпроцесса) | 3 | Pending |
| TSK-042 Скидка совместной разработки | 3 | Pending |
| TSK-043 GpuApi и предикаты | 3 | Pending |
| TSK-044 Тесты этапа 3 | 3 | Pending |
| TSK-045 Баланс-прогон | 3 | Pending |
| TSK-046 Документация | 3 | Pending |
| TSK-047 Ручная проверка этапа 3 и сейвов (владелец) | 3 | Pending |
| TSK-048 Консольные заказы: контент и логика (Could) | 3 | Pending |
| TSK-049 Консольные заказы: сервис, письмо, UI (Could) | 3 | Pending |

## Покрытие требований

| Требование | Задачи |
|---|---|
| FR-1 | TSK-001, TSK-013, TSK-022 |
| FR-2 | TSK-003, TSK-008, TSK-014, TSK-023 |
| FR-3 | TSK-003, TSK-006, TSK-007, TSK-011, TSK-024 |
| FR-4 | TSK-007, TSK-015, TSK-020, TSK-025 |
| FR-5 | TSK-001, TSK-010, TSK-011, TSK-016, TSK-017, TSK-021, TSK-028 |
| FR-6 | TSK-003, TSK-009, TSK-012, TSK-018, TSK-019, TSK-028 |
| FR-7 | TSK-010, TSK-020, TSK-025, TSK-049 |
| FR-8 | TSK-026, TSK-037 |
| FR-9 | TSK-001, TSK-004, TSK-005, TSK-019, TSK-046, TSK-047 |
| FR-10 | TSK-002, TSK-003, TSK-005, TSK-046 |
| FR-11 | TSK-009, TSK-030, TSK-031, TSK-035, TSK-037 |
| FR-12 | TSK-032, TSK-033, TSK-035, TSK-037, TSK-043 |
| FR-13 | TSK-018, TSK-034, TSK-035 |
| FR-14 | TSK-036 |
| FR-15 | TSK-012, TSK-040, TSK-044 |
| FR-16 | TSK-041, TSK-044 |
| FR-17 | TSK-007, TSK-024, TSK-042, TSK-044 |
| FR-18 | TSK-012, TSK-043, TSK-044 |
| FR-19 | TSK-048, TSK-049 |
| NFR-1…NFR-8 | TSK-028, TSK-045 (1, 7); TSK-001, TSK-011 (2); TSK-005, TSK-047 (3); TSK-027, TSK-038 (4); TSK-022…TSK-026 (5); TSK-035 (6); TSK-002, TSK-004 (8) |
