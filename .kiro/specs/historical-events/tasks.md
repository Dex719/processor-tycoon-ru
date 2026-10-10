# Tasks: historical-events

## Document Information

- **Feature Name**: historical-events — календарь реальных событий индустрии 1971–2025
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/historical-events/requirements.md`
  - Design: `.kiro/specs/historical-events/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Работа идёт в четыре слоя:
1. Чистая логика: валидатор, доли рынка, роли, планировщик, машины состояния дел, правил, войн и споров, антимонопольная надбавка — с юнит-тестами на фейковом `IWorldView`.
2. Интеграция с `event-system` и ядром: пак, диспетчер срабатываний, выбор ИИ, обработчики нестандартных эффектов, провайдеры каналов.
3. Контент: 42 записи календаря и события пака тремя пакетами по эпохам, затем сверка фактов и ссылок.
4. Хроника, встроенные события, настройки, самопроверка, симуляция календаря и ручная проверка владельцем.

### Implementation Strategy

- Код — `mod/src/PTMod/Features/HistoricalEvents/`; чистая логика — в подпапке `Logic/` (общее подмножество API .NET Framework 4.8 и `mscorlib` Unity: без `init`, `record`, `Index`, `Range`), тесты — `mod/tests/PTMod.Tests/HistoricalEvents/` (мини-раннер ядра: `[Test]`, `[Covers(typeof(X))]`, `Assert.*`; запуск `python mod/build.py --tests`; каждый публичный тип `Logic/` указан в `[Covers]`).
- Сборка — `python mod/build.py --game-dir <папка игры> [--tests]` (Roslyn `csc` из VS Build Tools, результат `mod/build/PTMod.dll`).
- Контент — `mod/src/PTMod/Content/historical-events/{calendar,events,params}.json`; строки — `mod/src/PTMod/L10n/{ru,en}.json` (ключи `hist.*`).
- Эффекты, окна, таймеры, письма и журнал — `event-system`. Фича только решает, когда и с кем срабатывает событие.
- Каждый нестандартный тип эффекта — отдельный сервис со своим DTO в секции сохранения.
- Собственных патчей на `Company.AddCpu`, `AICompany.AddCpu`, `DateController.NextMonth`, `CentralBank.DebtLimit` нет.

### Development Approach

- **Testing Strategy**: TDD для `Logic/`; симуляция календаря на синтетических мирах; самопроверка; ручные сценарии владельца.
- **Integration Strategy**:
  - только стандартные каналы `ModModifiers` (рейтинг CPU и цена поглощений — провайдерами `market.rating.cpu` и `acquisition.price`) и стандартные эффекты движка;
  - цены ИИ — через `IRivalPersonalitiesApi` (`ApplyPriceCut` / `RestorePriceCut`);
  - единственный патч — постфикс для письма к встроенному событию игры (Could).
- **Deployment Strategy**: `install.py --with-mods`; фича включена, `debug` выключен, категория `disaster` выключена.

### Внешние зависимости

Номера TSK сверены с `tasks.md` соответствующих спек (2026-10-10).

- `mod-framework`:
  - точка входа и реестр фич: TSK-020, TSK-022 (`FeatureContext`, `[ModFeature]`);
  - настройки и контент: TSK-014 (`ModSettings`), TSK-015 (`ModContent`), TSK-021 (адаптеры);
  - сейв и случайность: TSK-016, TSK-025 (`ModSave`, `_schema`), TSK-012 (`ModRandom`), TSK-010 (`ModJson`);
  - тики и пакетный прогон: TSK-024 (`ModTicks`, `FastForwardEnded`, `GameReady`), TSK-057 (`IsBatchRun`);
  - модификаторы: TSK-017, TSK-027 (фасад), TSK-029 (P3/P4/P5), TSK-030 (P6/P8), TSK-059 (`RegisterReasonedProvider`, `Invalidate`, ленивое связывание);
  - компании, репутация: TSK-018, TSK-026; сегменты: TSK-055 (`ModSegments`); порог кредита: TSK-058 (`ModCredit`);
  - условия: TSK-019, TSK-032 (`ModConditions`); идентификаторы: TSK-066 (`ModIds`);
  - почта: TSK-037, TSK-062 (`ModMail`, теги, приоритеты);
  - UI: TSK-036 (виджеты), TSK-039 (`HubPage`); патчи: TSK-023 (`ModHooks`);
  - строки: TSK-013, TSK-043; самопроверка и тесты: TSK-042, TSK-067.
- `event-system`:
  - пак: TSK-002 (`EventRegistry.RegisterPack`, `PackOptions.Severity`);
  - условия: TSK-005 (`EventConditions`, предикат `es.activeInCategory`);
  - эффекты: TSK-010, TSK-011, TSK-012 (`IEventEffectHandler`, стандартные эффекты);
  - API: TSK-014 (`EventBus`, `EventEffects`, `EventAi.AddChooser`); выборы и таймеры: TSK-016; журнал: TSK-019.
- `design-flaw-recalls` (мягкая): `FlawRegistry.ForceFlaw` (design-flaw-recalls/TSK-020) — только TSK-015.
- `rival-personalities` (мягкая): `IRivalPersonalitiesApi.ApplyPriceCut` / `RestorePriceCut` (rival-personalities/TSK-020) — только TSK-012; выборщик ИИ приоритета 100 (TSK-016).
- `rival-acquisitions` (мягкая): читает канал `acquisition.price` — только TSK-028.
- `gpu-product-line` (мягкая): читает канал `gpu.demand` — криптобумы в TSK-019.

## Фаза 1 — Каркас, схема, сохранение

- [ ] **TSK-001**: Каркас фичи и настройки
  - **Requirement**: FR-17
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalEventsFeature.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistSettings.cs`
  - **Acceptance**:
    - `[ModFeature] sealed class HistoricalEventsFeature : IModFeature` в `PTMod.Features.HistoricalEvents`: `Id = "historical-events"`, `NameKey = "hist.feature.name"`, `DescriptionKey = "hist.feature.desc"`, `DependsOn = { "event-system" }` (мягкие связи не входят), `EnabledByDefault = true`, `SupportsHotToggle = true`;
    - `ctx.Param`: `mode` (0/1), `intensity` (1.0, 0.5–1.5), 12 переключателей категорий (все `true`, кроме `disaster` = `false`), `historyNotes`, `spoilers`, `debug`;
    - пак регистрируется `EventRegistry.RegisterPack("historical-events", …, PackOptions { Severity = intensity })` (пустой `events.json` до TSK-017);
    - `OnDisabled` не вызывает `ModModifiers.RemoveAll`; `OnEnabled` записи не пересоздаёт (заглушка до TSK-024);
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`), TSK-014 (`ModSettings`), TSK-020; event-system TSK-002 (`EventRegistry`)

- [ ] **TSK-002**: Схема календаря и валидатор
  - **Requirement**: FR-1, NFR-7, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/CalendarModel.cs` (`CalendarEntry`, `StageSpec`, `FireSpec`, `RoleSpec`, `HistoryInfo`), `mod/src/PTMod/Features/HistoricalEvents/Logic/CalendarValidator.cs`, `mod/src/PTMod/Content/historical-events/params.json`, `mod/tests/PTMod.Tests/HistoricalEvents/CalendarValidatorTests.cs`
  - **Acceptance**:
    - загрузка через `ModContent.LoadRecords<CalendarEntry>`; условия разбираются `ModConditions.Parse` (нотация `type` + параметры на верхнем уровне), сегменты ролей — в `Segment`;
    - валидатор проверяет все правила раздела Security design (по тесту на правило), включая существование `eventId` в паке, префикс `hist.` у `id` и ключей L10n (`ModIds`), блок `acquisitionAntitrust`;
    - битая запись пропускается с ошибкой в логе; меньше 25 валидных записей → фича `Unavailable`;
    - `params.json` совпадает с примером design (включая `acquisitionAntitrust`).
  - **Depends**: TSK-001, mod-framework TSK-015 (`ModContent`), TSK-019 (`ModConditions`), TSK-066 (`ModIds`), TSK-067

- [ ] **TSK-003**: Секция сохранения
  - **Requirement**: FR-18, NFR-3, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Save/HistSaveData.cs`, `mod/src/PTMod/Features/HistoricalEvents/Save/HistSaveSection.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/HistSaveSectionTests.cs`
  - **Acceptance**:
    - `HistSaveSection : IModSaveParticipant` (`SectionId = "historical-events"`, `SchemaVersion = 1`); `Read(section, schema)`; `Write()` возвращает данные БЕЗ `_schema` (версию пишет ядро);
    - в `HistSaveData` нет поля версии;
    - круговая сериализация примера секции из design (`ModJson`);
    - `ResetToDefaults` без секции → записи с якорем раньше текущей даты `SkippedBeforeStart`, остальные `Pending`, `initialized = true`;
    - схема новее → ядро выдаёт значения по умолчанию и сохраняет исходную секцию, фича выключается для сейва;
    - недопустимые значения нормализуются; компании — ключами `ModCompanies`.
  - **Depends**: TSK-001, mod-framework TSK-016, TSK-025 (`ModSave`), TSK-010 (`ModJson`)

## Фаза 2 — Роли, условия, планировщик, диспетчер

- [ ] **TSK-004**: Доли рынка и `IWorldView`
  - **Requirement**: FR-3, NFR-1
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/IWorldView.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/MarketShareCache.cs`, `mod/src/PTMod/Features/HistoricalEvents/Game/GameWorldView.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/ShareMathTests.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/FakeWorld.cs`
  - **Acceptance**:
    - доля по полным месяцам без текущего (индексы `Count-1-M .. Count-2` журналов `CpuBase.MarketData`);
    - сегменты (`Segment`, маска) и «все» (`Sold` + `SoldByContracts`); нулевой знаменатель → 0; ничья — детерминированно;
    - `CpuInfo.Segment` — основной сегмент по правилу D15: ИИ — `AIStrategy.TargetMarket`, игрок — `ModSegments.FromSales` по последнему полному месяцу, затем `ModSegments.Classify`; собственного классификатора нет;
    - `GameWorldView` читает `ModCompanies.All`, `CpuDataProvider.Instance.GetAllCpus`, `CompanyFinance.Reports`, `Market.Instance.AverageIps`; `CashHeadroom` = `MoneyAmount − ModCredit.ThresholdOf(company)` (в тестах порог — фейк; `CentralBank.DebtLimit` не читается); `IsFeatureActive` = `ModFeatures.IsActive`;
    - перестройка кэша на 400 CPU × 36 месяцев ≤ 2 мс (замер на фейке).
  - **Depends**: TSK-001, mod-framework TSK-026 (`ModCompanies`), TSK-055 (`ModSegments`), TSK-058 (`ModCredit`), TSK-067

- [ ] **TSK-005**: Роли и условия
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/RoleResolver.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/ConditionEvaluator.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistConditions.cs` (регистрация `hist.*` в `EventConditions`), `mod/src/PTMod/Features/HistoricalEvents/HistRoleScope.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/RoleResolverTests.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/ConditionEvaluatorTests.cs`
  - **Acceptance**:
    - все виды ролей из таблицы design;
    - `minShare`, цепочка `fallback`, режим `strict`, истец ≠ ответчик;
    - предикаты `hist.ShareAtLeast`, `hist.HasCpuSince`, `hist.CaseActive`, `hist.PlayerHasCpuAbove`, `hist.IsFabless` зарегистрированы `EventConditions.Register` (имена с префиксом `hist.`, иначе `ModConditions.Register` отклонил бы) и дают те же результаты, что прямой вызов; роли записи передаются через `HistRoleScope`;
    - стандартные условия берутся из ядра и движка (`dateAtLeast`, `moneyAtLeast`, `cpuOnMarket`, `featureActive`, `es.hasFab`, `es.activeInCategory`);
    - отладочный флаг `debugForceLeader`;
    - AC-3.1–3.4.
  - **Depends**: TSK-004, event-system TSK-005 (`EventConditions`), mod-framework TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

- [ ] **TSK-006**: Планировщик календаря
  - **Requirement**: FR-2, FR-17, NFR-5, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/CalendarScheduler.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/SchedulerCore.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/CalendarSchedulerTests.cs`
  - **Acceptance**:
    - сдвиг бросается один раз на партию (`ModRandom.Stream("historical-events")`) и сохраняется;
    - очередь `due` с указателем, дни от 1971-01-01: в обычный день O(1);
    - повторная проверка 1-го числа до конца окна; истечение → `news`/`skip`, `Expired`;
    - интервал `major` 45 дней с переносом в пределах окна;
    - порядок в один день (якорь, затем `id`);
    - выключенная категория → `Disabled` (по умолчанию `disaster`); `skipIfFlag`;
    - инициализация (сдвиги, `SkippedBeforeStart`) на `TickPhase.FastForwardEnded` ровно один раз (`initialized`); резерв — `ModTicks.GameReady(NewGame)` без перемотки; `GameReady(Loaded)` восстанавливает `due`;
    - в пакетном прогоне (`ModTicks.IsBatchRun`) тики идут по дням, фича не патчит `NextMonth`;
    - AC-2.1–2.3.
  - **Depends**: TSK-003, TSK-005, mod-framework TSK-024 (`ModTicks`), TSK-057 (`IsBatchRun`), TSK-012 (`ModRandom`)

- [ ] **TSK-007**: Поздний старт и догоняющие эффекты
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/CatchUpService.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/CatchUpServiceTests.cs`
  - **Acceptance**:
    - на `TickPhase.FastForwardEnded` (новая игра) записи до старта → `SkippedBeforeStart`;
    - стандартные эффекты ещё действующих стадий записей с `catchUp` → `ModModifiers.Add` с остатком срока (каналы по таблице design, причина `hist.catchup`); идентификаторы записей сохраняются; записи выключенных категорий (по умолчанию `disaster`) и с ложным `featureActive` не догоняются;
    - одно письмо «Обстановка в отрасли» (`ModMail.Send`, `MailPriority.Normal`, `Tag = "hist:catchup"`);
    - AC-4.1 (699 и 334 дня при включённой `disaster`), AC-4.2 (по умолчанию), AC-4.3 (криптобум, остаток 90 дней).
  - **Depends**: TSK-006, mod-framework TSK-027, TSK-059 (`ModModifiers.Add`), TSK-062 (`ModMail`)

- [ ] **TSK-008**: Диспетчер срабатываний
  - **Requirement**: FR-2, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalDispatcher.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/HistoricalDispatcherTests.cs`
  - **Acceptance**:
    - для каждой `FireSpec` стадии и каждой компании роли вызывается `EventBus.Fire(eventId, EventFireContext { CompanyKey, Args, IgnoreConditions = true })`; `global` → цель «игрок»; групповые роли → по компании;
    - `Args` по таблице design (id записи, имена ролей, ключ второй роли, `ExtraArgs`);
    - `onlyIf` (нотация `ModConditions`) проверяется по компании;
    - результат `UnknownEvent` → запись `Error`; `Disabled` → `Disabled`;
    - AC-10.1 (интенсивность через `PackOptions.Severity`) на фейке движка.
  - **Depends**: TSK-005, event-system TSK-014 (`EventBus`)

## Фаза 3 — Нестандартные эффекты и выбор ИИ

- [ ] **TSK-009**: Антимонопольные дела (`AntitrustCase`)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/AntitrustCaseMachine.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/CaseService.cs`, события `hist.decide.antitrust.fine`, `hist.decide.antitrust.consent`, `hist.decide.antitrust.reimpose` в `mod/src/PTMod/Content/historical-events/events.json`, `mod/tests/PTMod.Tests/HistoricalEvents/AntitrustCaseMachineTests.cs`
  - **Acceptance**:
    - обработчик реализует `IEventEffectHandler`: `Resolve` считает штраф один раз, `Describe` показывает сумму;
    - режимы `fine`, `consent`, `warning`;
    - оплата, резерв, апелляция с гарантией (`ExpenseOrigin.Interest`);
    - решение суда с поправкой `dominanceLostShare`; возврат (`RevenueOrigin.Interest`);
    - повторный штраф через `EventBus.Fire(hist.decide.antitrust.reimpose)` и вторая апелляция;
    - поведенческое ограничение — запись `ModModifiers.Add` на `contract.reward` для компании;
    - репутация через `ModReputation.Add` с причинами-ключами `hist.rep.antitrust.fine` и `hist.rep.antitrust.paid`;
    - правила одинаковы для игрока и ИИ-цели;
    - AC-5.1–5.3; ожидаемая потеря ≈ 0.58F ± 0.01F на 100 000 бросков;
    - банкротство цели закрывает дело; открытие и закрытие дела вызывают `ModModifiers.Invalidate("acquisition.price")` (для TSK-028).
  - **Depends**: TSK-008, mod-framework TSK-026 (`ModReputation`), TSK-027, TSK-059 (`ModModifiers`), event-system TSK-010 (`EventEffects.Register`)

- [ ] **TSK-010**: Иск пострадавшего (`AntitrustSettlement`)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/SettlementService.cs`, событие `hist.decide.settlement`, `mod/tests/PTMod.Tests/HistoricalEvents/SettlementTests.cs`
  - **Acceptance**:
    - условие `hist.CaseActive` и порог доли игрока 0.10;
    - 12 месяцев судебных расходов, затем выигрыш с `pWin` → перевод 3 % выручки ответчика (`Fines` → `Royalties`);
    - без игрока — событие для `challenger`-ИИ и новость;
    - AC-6.1.
  - **Depends**: TSK-009

- [ ] **TSK-011**: Экспортный контроль (`ExportControl`)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/ExportRuleMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/ExportRuleService.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistRatingProvider.cs`, события `hist.2022.export.rule`, `hist.2023.export.update`, `hist.decide.export`, `hist.decide.export.update`, `mod/tests/PTMod.Tests/HistoricalEvents/ExportRuleMathTests.cs`
  - **Acceptance**:
    - перцентиль по `Ips` CPU на рынке; месячная индексация к `Market.Instance.AverageIps`;
    - провайдер `ModModifiers.RegisterReasonedProvider("historical-events", "market.rating.cpu", …)` за O(1) по `ModifierContext.Cpu`/`Segment`, один раз в `Register`; причина `hist.mod.export` с порогом в аргументах;
    - `ModModifiers.Invalidate("market.rating.cpu")` при вводе и ужесточении правила, выдаче лицензии, успешном лоббировании и месячном пересчёте порога;
    - вклад провайдера лежит в [0,80; 1,00] (тест на обеих ступенях и с лоббированием);
    - лицензия и лоббирование с вероятностями и сроками; ступень `update`;
    - выбор только у компаний с CPU выше порога (`onlyIf`), остальным — новость;
    - AC-7.1, AC-7.2; после загрузки провайдер читает восстановленное правило; при выключении фичи провайдер возвращает 1.0.
  - **Depends**: TSK-008, mod-framework TSK-029 (P4), TSK-059 (`RegisterReasonedProvider`, `Invalidate`)

- [ ] **TSK-012**: Ценовые войны (`PriceWar`)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/PriceWarService.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/PriceWarFallback.cs`, события `hist.decide.pricewar.aggressor`, `hist.decide.pricewar.follower`, `mod/tests/PTMod.Tests/HistoricalEvents/PriceWarTests.cs`
  - **Acceptance**:
    - при `ModFeatures.IsActive("rival-personalities")` — `RivalPersonalities.Api.ApplyPriceCut("hist:<warId>", rival, segment, depth, floorMargin)` и `RestorePriceCut("hist:<warId>")` (фейк в тесте), прямых изменений цен нет (AC-8.2);
    - иначе (`Api.IsEnabled == false`) fallback: срез `Cpu.Price` и `CpuBase.AIStrategy.TargetPrice` с полом `RealUnitCost × 1.05`, возврат исходных целей по окончании (AC-8.1);
    - выбор игрока (начать, присоединиться, держать, маркетинг); основной сегмент CPU игрока — `ModSegments.FromSales` по последнему полному месяцу, затем `ModSegments.Classify`;
    - выключение фичи и банкротство возвращают цены;
    - сегмент передаётся типом `Segment`.
  - **Depends**: TSK-008, mod-framework TSK-055 (`ModSegments`); rival-personalities/TSK-020 (`IRivalPersonalitiesApi`, мягкая)

- [ ] **TSK-013**: Патентные споры (`PatentDispute`)
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/DisputeMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/DisputeService.cs`, события `hist.decide.patent.plaintiff`, `hist.decide.patent.withdrawal`, `hist.decide.patent.infringement`, `mod/tests/PTMod.Tests/HistoricalEvents/DisputeMathTests.cs`
  - **Acceptance**:
    - все ветки п. 8 «Формул и баланса»;
    - платежи `ExpenseOrigin.Royalties` и `RevenueOrigin.Royalties`;
    - `research.cost` — записи `ModModifiers.Add` для компании;
    - пропуск множителя при `CompanyHelper.IsUnluckyCompany`;
    - AC-9.1.
  - **Depends**: TSK-008

- [ ] **TSK-014**: Гонки характеристик (`AttributeRace`)
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/RaceService.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/RaceServiceTests.cs`
  - **Acceptance**:
    - месячный пересчёт топ-N по `Frequency` или `CoreCount` (ничья — `Ips`);
    - вклад в провайдер `market.rating.cpu` за O(1) в диапазоне [1,00; 1,10], причины `hist.mod.race.frequency` / `hist.mod.race.cores`; `ModModifiers.Invalidate("market.rating.cpu")` после пересчёта; снятие по окончании;
    - AC-11.1.
  - **Depends**: TSK-011

- [ ] **TSK-015**: Мост к механике брака (`ForceDesignFlaw`) и `HistFlag`
  - **Requirement**: FR-12, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/FlawBridge.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/HistFlagEffect.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/FlawBridgeTests.cs`
  - **Acceptance**:
    - правила `topSellingHighEnd24m` и `perCompanyAboveAvgIps60m`;
    - вызов `DesignFlawRecallsFeature.Registry.ForceFlaw` при активной `design-flaw-recalls`, иначе (или нет CPU) — `EventBus.Fire(fallbackEventId)`;
    - `HistFlag` ставит флаг записи для `skipIfFlag`;
    - AC-12.1 на фейке.
  - **Depends**: TSK-008, design-flaw-recalls/TSK-020 (мягкая)

- [ ] **TSK-016**: Выбор ИИ (`HistoricalAiChooser`)
  - **Requirement**: FR-13
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalAiChooser.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/HistoricalAiChooserCore.cs`, `mod/tests/PTMod.Tests/HistoricalEvents/HistoricalAiChooserTests.cs`
  - **Acceptance**:
    - регистрация `EventAi.AddChooser(chooser, priority из params.json)`; `Choose(…, IModRng rng)`;
    - для чужих событий — `null`; два выборщика (100 и 10): первый ненулевой ответ побеждает, `null` первого передаёт решение второму;
    - стоимость вариантов — из предвычисленных `AppliedEffect.value` (типы фичи и `Money`);
    - отсечение вариантов дороже запаса (`MoneyAmount − ModCredit.ThresholdOf(company)`); `defaultChoice` при нулевых весах;
    - распределение по `aiWeight` на 10 000 бросков ± 0.02;
    - правила для ИИ те же, что для игрока;
    - AC-13.1, AC-13.2.
  - **Depends**: TSK-009, event-system TSK-014 (`EventAi.AddChooser`), TSK-016, mod-framework TSK-058 (`ModCredit`)

## Фаза 4 — Контент календаря и пака

- [ ] **TSK-017**: Записи 1971–1989 (№ 1–12) и их события
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 1–12 в `mod/src/PTMod/Content/historical-events/calendar.json`; события пака для них (глобальные эффекты, `hist.decide.secondsource`) в `events.json`; ключи `hist.<id>.title` / `.text` / `.fact` в `mod/src/PTMod/L10n/{ru,en}.json`
  - **Acceptance**:
    - 12 записей проходят валидатор календаря, события — валидацию движка;
    - числа и роли совпадают с таблицей design; стандартные эффекты помечены `scalable`; компании в ролях — по `UniqueID` (Toshiba = 6, Intel = 0, AMD = 1), имена не вшиты;
    - записи DRAM (7, 8, 12) используют канал `production.unitCost`, не `production.memoryCost`;
    - у каждой записи `_note` с фактом и датой, `history.sources` ≥ 1;
    - справки ru/en (`cause`) по 2–4 предложения, нейтральный тон.
  - **Depends**: TSK-002, TSK-009, TSK-012, TSK-013

- [ ] **TSK-018**: Записи 1990–2005 (№ 13–24)
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 13–24 в `calendar.json`; события пака, включая `hist.decide.brand.leader`, `hist.decide.brand.coop`, `hist.1999.jiji.hit`, `hist.1999.jiji.market`, `hist.1999.jiji.recovery`; ключи L10n
  - **Acceptance**: как TSK-017 для записей 13–24; запись 20 (`disaster`) по умолчанию получает статус `Disabled`.
  - **Depends**: TSK-017, TSK-014, TSK-015

- [ ] **TSK-019**: Записи 2006–2025 (№ 25–42), включая криптобумы
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 25–42 в `calendar.json`; события пака, включая `hist.2011.tohoku.hit`, `hist.decide.shortage.priority`, `hist.decide.insourcing`, `hist.2017.cryptoboom`, `hist.2021.cryptoboom`, `hist.fizzle`, `hist.catchup.summary`; ключи L10n
  - **Acceptance**:
    - как TSK-017 для записей 25–42;
    - криптобумы: `gpu.demand` ×1.35 (2017) и ×1.45 (2021) на 365 дней, глобальный `ModifierTimed`, `catchUp`, условие `featureActive{id: "gpu-product-line"}`, запасной вариант `skip`; AC-10.2, AC-10.3, AC-4.3;
    - итог календаря — 42 записи, распределение по категориям совпадает с design (9/10/2/3/3/2/4/2/2/2/1/2: boom, macro, shock, disaster, priceWar, patent, antitrust, export, flaw, race, brand, oem);
    - записи `disaster` (20, 30, 35) по умолчанию `Disabled`; AC-4.1/4.2.
  - **Depends**: TSK-018, TSK-010, TSK-011

- [ ] **TSK-020**: Сверка фактов и источников
  - **Requirement**: FR-1, NFR-5, NFR-7
  - **Deliverables**: обновлённые `calendar.json` и ключи `hist.*.fact`; таблица сверки в описании PR (`id` → дата → URL → «проверено»)
  - **Acceptance**:
    - каждый URL открыт человеком и подтверждает дату и суть справки;
    - все пометки [проверить] из design (в том числе источники записей 41–42) заменены на проверенные URL или записи переписаны;
    - справки — пересказ своими словами без цитат длиннее 15 слов; тексты о катастрофах нейтральны.
  - **Depends**: TSK-019

## Фаза 5 — Хроника, встроенные события, настройки

- [ ] **TSK-021**: Справки в событиях и письмо догона
  - **Requirement**: FR-14
  - **Deliverables**: дополнения `HistoricalDispatcher` (`hist.fizzle`, `historyNotes`), ключ `hist.mail.source`, проверка `cause` в валидаторе
  - **Acceptance**:
    - у каждого события пака заполнены `text` и `cause`; последняя строка `text` — домен источника;
    - при `historyNotes = false` справка скрыта;
    - запасной вариант `news` вызывает `hist.fizzle` с заголовком записи;
    - события пака видны в `EventLog` с категорией `history`;
    - события с выбором игрока имеют `important: true` (письмо `Critical`), остальные — `Normal`; собственное письмо догона несёт тег `hist:catchup`;
    - AC-14.1.
  - **Depends**: TSK-008, event-system TSK-019 (`EventLog`), mod-framework TSK-062 (`ModMail`)

- [ ] **TSK-022**: Страница «Хроника отрасли»
  - **Requirement**: FR-15
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/UI/ChroniclePage.cs`
  - **Acceptance**:
    - страница `ctx.HubPage("hist.chronicle.title", build)`, 3 вкладки по design;
    - будущие события скрыты при `spoilers = false`; при `true` — год трёх ближайших;
    - отладка (`debug`, только отладочная сборка): выбор записи, «Запустить сейчас», `debugForceLeader`;
    - AC-15.1.
  - **Depends**: TSK-009, TSK-011, TSK-012, TSK-013, mod-framework TSK-036 (виджеты), TSK-039 (`HubPage`)

- [ ] **TSK-023**: Письма к встроенным событиям игры (Could)
  - **Requirement**: FR-16
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/BuiltInEventNews.cs`, события `hist.news.solarfoundries`, `hist.news.x86license`
  - **Acceptance**:
    - `Optional`-постфикс `CompanyEventHandler.TriggerSolarFoundriesSpinOff` и подписка на `LicenseHelper.OnX86LicenseRevoked` вызывают события-справки;
    - при загрузке и перемотке писем нет; игровая логика не меняется;
    - AC-16.1.
  - **Depends**: TSK-021, mod-framework TSK-023 (`ModHooks`)

- [ ] **TSK-024**: Выключение посреди партии и самопроверка
  - **Requirement**: FR-17, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistSelfCheck.cs`, дополнения `HistoricalEventsFeature.OnDisabled/OnEnabled`
  - **Acceptance**:
    - выключение: провайдеры `market.rating.cpu` и `acquisition.price` возвращают 1.0, дела и споры на паузе, цены войн возвращены (`RestorePriceCut` или исходные цели); записи `ModModifiers` не удаляются (`RemoveAll` не вызывается); включение возобновляет без пересоздания записей;
    - самопроверка — пункты из design (цели, пак, `eventId`, типы эффектов, предикаты, каналы `market.rating.cpu` с `PerCpu`/`PerSegment`, `acquisition.price`, `gpu.demand`, календарь ≥ 25, префикс `hist.`, отсутствие патчей на `AddCpu`/`NextMonth`);
    - провал канала `market.rating.cpu` → записи `export` и `race` получают `Disabled`;
    - AC-17.1, AC-17.2.
  - **Depends**: TSK-011, TSK-012, TSK-014, TSK-023, TSK-028, mod-framework TSK-042 (`ModSelfCheck`), TSK-066 (`ModIds`)

## Фаза 6 — Антимонопольная надбавка, локализация и проверка

- [ ] **TSK-028**: Антимонопольная надбавка к цене поглощений (`acquisition.price`)
  - **Requirement**: FR-19, NFR-9
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/AcquisitionAntitrustMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/AcquisitionAntitrust.cs` (провайдер), `mod/tests/PTMod.Tests/HistoricalEvents/AcquisitionAntitrustMathTests.cs`
  - **Acceptance**:
    - провайдер `ModModifiers.RegisterReasonedProvider("historical-events", "acquisition.price", …)` регистрируется один раз в `Register` (порядок `Register` фич не важен, связывание ленивое); читает кэш `MarketShareCache` за O(1);
    - таблица формулы 11: S' 0,30 → 1,00 без причины; 0,50 → 1,25; 0,65 с делом → ≈ 2,03; 0,97 с делом → предел 3,00 (AC-19.1–19.3);
    - причина `hist.mod.acqAntitrust` с долей после сделки в аргументах (видна в `Explain`);
    - `ModModifiers.Invalidate("acquisition.price")` после месячной пересборки долей и при открытии и закрытии дела;
    - категория `antitrust` выключена или фича выключена → 1,00;
    - запрета сделки нет; вклад в [1,00; 3,00];
    - параметры `acquisitionAntitrust` читаются из `params.json`.
  - **Depends**: TSK-004, TSK-009, mod-framework TSK-059 (`RegisterReasonedProvider`, ленивое связывание, `Invalidate`); rival-acquisitions (читает канал, мягкая)

- [ ] **TSK-025**: Строки ru/en
  - **Requirement**: NFR-4
  - **Deliverables**: ключи `hist.*` (страница, кнопки, тексты и справки событий, письмо догона, причины `hist.rep.*` и `hist.mod.*`, `hist.feature.desc`) в `mod/src/PTMod/L10n/ru.json` и `en.json`
  - **Acceptance**:
    - все ключи из `calendar.json`, `events.json` и UI есть в обоих языках и начинаются с `hist.` (`ModIds.IsL10nKeyAllowed`);
    - тест полноты ключей фичи зелёный;
    - аргументы позиционные, деньги и даты отформатированы игровыми `StringFormatter`;
    - названия компаний — из игры; в справках допустимы реальные названия как исторические факты.
  - **Depends**: TSK-020, TSK-022, TSK-028, mod-framework TSK-013, TSK-043 (`ModL10n`, `core.json`), TSK-066 (`ModIds`)

- [ ] **TSK-026**: Симуляция календаря
  - **Requirement**: FR-2, FR-3, NFR-1, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/HistoricalEvents/CalendarSimulationTests.cs`, генератор синтетических миров `mod/tests/PTMod.Tests/HistoricalEvents/SyntheticWorlds.cs`
  - **Acceptance**:
    - 100 миров 1975–2030: срабатывает 27–35 записей по умолчанию (30–38 с включённой `disaster`);
    - в «среднем» мире 7–13 решений игрока (8–14 с `disaster`);
    - между `major` ≥ 45 дней;
    - мир «доминирование» → ≥ 2 антимонопольных записи против игрока; мир «слабый игрок» → 0;
    - одинаковый сид → одинаковый результат.
  - **Depends**: TSK-019, TSK-016, mod-framework TSK-067

- [ ] **TSK-027**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1–FR-19, NFR-1–NFR-9
  - **Deliverables**: протокол прогона сценариев 1–12 из design → Testing (дата, версия, результат, отклонения) в описании PR
  - **Acceptance**:
    - сценарии пройдены или отклонения заведены задачами (сценарий 11 — после готовности `rival-acquisitions`);
    - 0 ошибок `[PTMod]` уровня Error в `Player.log` за 20 игровых лет;
    - сейв грузится с модом и без него.
  - **Depends**: TSK-024, TSK-025, TSK-026

## Dependency graph

```
Внешние: mod-framework ──> TSK-001 (020, 022, 014), TSK-002 (015, 019, 066, 067), TSK-003 (016, 025, 010),
                           TSK-004 (026, 055, 058, 067), TSK-005 (019, 032, 066), TSK-006 (024, 057, 012),
                           TSK-007 (027, 059, 062), TSK-009 (026, 027, 059), TSK-011 (029, 059), TSK-012 (055),
                           TSK-016 (058), TSK-021 (062), TSK-022 (036, 039), TSK-023 (023), TSK-024 (042, 066),
                           TSK-025 (013, 043, 066), TSK-028 (059)
         event-system  ──> TSK-001 (002), TSK-005 (005), TSK-008 (014), TSK-009 (010), TSK-016 (014, 016), TSK-021 (019)
         design-flaw-recalls/TSK-020 ──> TSK-015 (мягкая)
         rival-personalities/TSK-020 ──> TSK-012 (мягкая)
         rival-acquisitions (канал acquisition.price) ──> TSK-028 (мягкая)

TSK-001 ──> TSK-002, TSK-003, TSK-004
TSK-004 ──> TSK-005
TSK-003 + TSK-005 ──> TSK-006 ──> TSK-007
TSK-005 ──> TSK-008
TSK-008 ──> TSK-009, TSK-011, TSK-012, TSK-013, TSK-015, TSK-021
TSK-009 ──> TSK-010, TSK-016
TSK-011 ──> TSK-014
TSK-002 + TSK-009 + TSK-012 + TSK-013 ──> TSK-017
TSK-017 + TSK-014 + TSK-015 ──> TSK-018
TSK-018 + TSK-010 + TSK-011 ──> TSK-019 ──> TSK-020
TSK-004 + TSK-009 ──> TSK-028
TSK-021 ──> TSK-023
TSK-009 + TSK-011 + TSK-012 + TSK-013 ──> TSK-022
TSK-011 + TSK-012 + TSK-014 + TSK-023 + TSK-028 ──> TSK-024
TSK-020 + TSK-022 + TSK-028 ──> TSK-025
TSK-019 + TSK-016 ──> TSK-026
TSK-024 + TSK-025 + TSK-026 ──> TSK-027 (владелец)

Критический путь:
TSK-001 ─> 004 ─> 005 ─> 008 ─> 009 ─> 017 ─> 018 ─> 019 ─> 020 ─> 025 ─> 027
```

| Слой | Задачи (параллельно внутри слоя) |
|---|---|
| 0 | TSK-001 |
| 1 | TSK-002, TSK-003, TSK-004 |
| 2 | TSK-005 |
| 3 | TSK-006, TSK-008 |
| 4 | TSK-007, TSK-009, TSK-011, TSK-012, TSK-013, TSK-015, TSK-021 |
| 5 | TSK-010, TSK-014, TSK-016, TSK-017, TSK-022, TSK-023, TSK-028 |
| 6 | TSK-018, TSK-024 |
| 7 | TSK-019 |
| 8 | TSK-020, TSK-026 |
| 9 | TSK-025 |
| 10 | TSK-027 |

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи и настройки | Pending |
| TSK-002 Схема календаря и валидатор | Pending |
| TSK-003 Секция сохранения | Pending |
| TSK-004 Доли рынка и IWorldView | Pending |
| TSK-005 Роли и условия | Pending |
| TSK-006 Планировщик календаря | Pending |
| TSK-007 Поздний старт и догоняющие эффекты | Pending |
| TSK-008 Диспетчер срабатываний | Pending |
| TSK-009 Антимонопольные дела | Pending |
| TSK-010 Иск пострадавшего | Pending |
| TSK-011 Экспортный контроль | Pending |
| TSK-012 Ценовые войны | Pending |
| TSK-013 Патентные споры | Pending |
| TSK-014 Гонки характеристик | Pending |
| TSK-015 Мост к механике брака и HistFlag | Pending |
| TSK-016 Выбор ИИ | Pending |
| TSK-017 Записи 1971–1989 | Pending |
| TSK-018 Записи 1990–2005 | Pending |
| TSK-019 Записи 2006–2025 (включая криптобумы) | Pending |
| TSK-020 Сверка фактов и источников | Pending |
| TSK-021 Справки в событиях и письмо догона | Pending |
| TSK-022 Страница «Хроника отрасли» | Pending |
| TSK-023 Письма к встроенным событиям (Could) | Pending |
| TSK-024 Выключение посреди партии и самопроверка | Pending |
| TSK-025 Строки ru/en | Pending |
| TSK-026 Симуляция календаря | Pending |
| TSK-027 Ручная проверка в игре (владелец) | Pending |
| TSK-028 Антимонопольная надбавка к цене поглощений | Pending |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-002, TSK-017, TSK-018, TSK-019, TSK-020 |
| FR-2 | TSK-006, TSK-008, TSK-015, TSK-026 |
| FR-3 | TSK-004, TSK-005, TSK-026 |
| FR-4 | TSK-007 |
| FR-5 | TSK-009 |
| FR-6 | TSK-010 |
| FR-7 | TSK-011 |
| FR-8 | TSK-012 |
| FR-9 | TSK-013 |
| FR-10 | TSK-008, TSK-017, TSK-018, TSK-019 |
| FR-11 | TSK-014 |
| FR-12 | TSK-015 |
| FR-13 | TSK-016 |
| FR-14 | TSK-021 |
| FR-15 | TSK-022 |
| FR-16 | TSK-023 |
| FR-17 | TSK-001, TSK-006, TSK-024 |
| FR-18 | TSK-003 |
| FR-19 | TSK-028 |
| NFR-1 | TSK-004, TSK-026 |
| NFR-2 | TSK-024 |
| NFR-3 | TSK-003 |
| NFR-4 | TSK-025 |
| NFR-5 | TSK-006, TSK-020 |
| NFR-6 | TSK-006, TSK-026 |
| NFR-7 | TSK-002, TSK-020 |
| NFR-8 | TSK-002, TSK-003 |
| NFR-9 | TSK-011, TSK-014, TSK-019, TSK-028 |
