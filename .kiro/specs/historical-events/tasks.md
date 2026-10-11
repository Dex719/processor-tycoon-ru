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

- Код — `mod/src/PTMod/Features/HistoricalEvents/`; чистая логика — в подпапке `Logic/` (общее подмножество API .NET Framework 4.8 и `mscorlib` Unity: без `init`, `record`, `Index`, `Range`), тесты — `mod/tests/PTMod.Tests/Features/HistoricalEvents/` (мини-раннер ядра: `[Test]`, `[Covers(typeof(X))]`, `Assert.*`; запуск `python mod/build.py --tests`; каждый публичный тип `Logic/` указан в `[Covers]`).
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

- [x] **TSK-001**: Каркас фичи и настройки (авто 2026-10-10)
  - **Requirement**: FR-17
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalEventsFeature.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistSettings.cs`
  - **Acceptance**:
    - `[ModFeature] sealed class HistoricalEventsFeature : IModFeature` в `PTMod.Features.HistoricalEvents`: `Id = "historical-events"`, `NameKey = "hist.feature.name"`, `DescriptionKey = "hist.feature.desc"`, `DependsOn = { "event-system" }` (мягкие связи не входят), `EnabledByDefault = true`, `SupportsHotToggle = true`;
    - `ctx.Param`: `mode` (0/1), `intensity` (1.0, 0.5–1.5), 12 переключателей категорий (все `true`, кроме `disaster` = `false`), `historyNotes`, `spoilers`, `debug`;
    - пак регистрируется `EventRegistry.RegisterPack("historical-events", …, PackOptions { Severity = intensity })` (пустой `events.json` до TSK-017);
    - `OnDisabled` не вызывает `ModModifiers.RemoveAll`; `OnEnabled` записи не пересоздаёт (заглушка до TSK-024);
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`), TSK-014 (`ModSettings`), TSK-020; event-system TSK-002 (`EventRegistry`)

- [x] **TSK-002**: Схема календаря и валидатор (авто 2026-10-10)
  - **Requirement**: FR-1, NFR-7, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/CalendarModel.cs` (`CalendarEntry`, `StageSpec`, `FireSpec`, `RoleSpec`, `HistoryInfo`), `mod/src/PTMod/Features/HistoricalEvents/Logic/CalendarValidator.cs`, `mod/src/PTMod/Content/historical-events/params.json`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/CalendarValidatorTests.cs`
  - **Acceptance**:
    - загрузка через `ModContent.LoadRecords<CalendarEntry>`; условия разбираются `ModConditions.Parse` (нотация `type` + параметры на верхнем уровне), сегменты ролей — в `Segment`;
    - валидатор проверяет все правила раздела Security design (по тесту на правило), включая существование `eventId` в паке, префикс `hist.` у `id` и ключей L10n (`ModIds`), блок `acquisitionAntitrust`;
    - битая запись пропускается с ошибкой в логе; меньше 25 валидных записей → фича `Unavailable`;
    - `params.json` совпадает с примером design (включая `acquisitionAntitrust`).
  - **Depends**: TSK-001, mod-framework TSK-015 (`ModContent`), TSK-019 (`ModConditions`), TSK-066 (`ModIds`), TSK-067

- [x] **TSK-003**: Секция сохранения (авто 2026-10-11)
  - **Requirement**: FR-18, NFR-3, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/Save/HistSaveData.cs`, `mod/src/PTMod/Features/HistoricalEvents/Save/HistSaveSection.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/HistSaveSectionTests.cs`
  - **Acceptance**:
    - `HistSaveSection : IModSaveParticipant` (`SectionId = "historical-events"`, `SchemaVersion = 1`); `Read(section, schema)`; `Write()` возвращает данные БЕЗ `_schema` (версию пишет ядро);
    - в `HistSaveData` нет поля версии;
    - круговая сериализация примера секции из design (`ModJson`);
    - `ResetToDefaults` без секции → записи с якорем раньше текущей даты `SkippedBeforeStart`, остальные `Pending`, `initialized = true`;
    - схема новее → ядро выдаёт значения по умолчанию и сохраняет исходную секцию, фича выключается для сейва;
    - недопустимые значения нормализуются; компании — ключами `ModCompanies`.
  - **Depends**: TSK-001, mod-framework TSK-016, TSK-025 (`ModSave`), TSK-010 (`ModJson`)
  - **Закрыто** (авто 2026-10-11): пункт «фича выключается для сейва» сделан. `HistoricalEventsFeature.SaveLocked` читает публичный признак ядра `ModSave.IsSectionNewer(FeatureId)`, `CalendarScheduler` выходит при `SaveLocked`, `WarnIfLocked` пишет одно предупреждение. Юнит-теста ветки `SaveLocked` в репозитории нет; сценарий «сейв с секцией новее мода» проверяется в игре владельцем.

## Фаза 2 — Роли, условия, планировщик, диспетчер

- [x] **TSK-004**: Доли рынка и `IWorldView` (авто 2026-10-10)
  - **Requirement**: FR-3, NFR-1
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/IWorldView.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/MarketShareCache.cs`, `mod/src/PTMod/Features/HistoricalEvents/Game/GameWorldView.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/ShareMathTests.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/FakeWorld.cs`
  - **Acceptance**:
    - доля по полным месяцам без текущего (индексы `Count-1-M .. Count-2` журналов `CpuBase.MarketData`);
    - сегменты (`Segment`, маска) и «все» (`Sold` + `SoldByContracts`); нулевой знаменатель → 0; ничья — детерминированно;
    - `CpuInfo.Segment` — основной сегмент по правилу D15: ИИ — `AIStrategy.TargetMarket`, игрок — `ModSegments.FromSales` по последнему полному месяцу, затем `ModSegments.Classify`; собственного классификатора нет;
    - `GameWorldView` читает `ModCompanies.All`, `CpuDataProvider.Instance.GetAllCpus`, `CompanyFinance.Reports`, `Market.Instance.AverageIps`; `CashHeadroom` = `MoneyAmount − ModCredit.ThresholdOf(company)` (в тестах порог — фейк; `CentralBank.DebtLimit` не читается); `IsFeatureActive` = `ModFeatures.IsActive`;
    - перестройка кэша на 400 CPU × 36 месяцев ≤ 2 мс (замер на фейке).
  - **Depends**: TSK-001, mod-framework TSK-026 (`ModCompanies`), TSK-055 (`ModSegments`), TSK-058 (`ModCredit`), TSK-067

- [x] **TSK-005**: Роли и условия (авто 2026-10-10)
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/RoleResolver.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/ConditionEvaluator.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistConditions.cs` (регистрация `hist.*` в `EventConditions`), `mod/src/PTMod/Features/HistoricalEvents/Logic/HistRoleScope.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/RoleResolverTests.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/ConditionEvaluatorTests.cs`
  - **Acceptance**:
    - все виды ролей из таблицы design;
    - `minShare`, цепочка `fallback`, режим `strict`, истец ≠ ответчик;
    - предикаты `hist.ShareAtLeast`, `hist.HasCpuSince`, `hist.CaseActive`, `hist.PlayerHasCpuAbove`, `hist.IsFabless` зарегистрированы `EventConditions.Register` (имена с префиксом `hist.`, иначе `ModConditions.Register` отклонил бы) и дают те же результаты, что прямой вызов; роли записи передаются через `HistRoleScope`;
    - стандартные условия берутся из ядра и движка (`dateAtLeast`, `moneyAtLeast`, `cpuOnMarket`, `featureActive`, `es.hasFab`, `es.activeInCategory`);
    - отладочный флаг `debugForceLeader`;
    - AC-3.1–3.4.
  - **Depends**: TSK-004, event-system TSK-005 (`EventConditions`), mod-framework TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

- [x] **TSK-006**: Планировщик календаря (авто 2026-10-10)
  - **Requirement**: FR-2, FR-17, NFR-5, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/CalendarScheduler.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/SchedulerCore.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/CalendarSchedulerTests.cs`
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

- [x] **TSK-007**: Поздний старт и догоняющие эффекты (авто 2026-10-10)
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/CatchUpService.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/CatchUpServiceTests.cs`
  - **Acceptance**:
    - на `TickPhase.FastForwardEnded` (новая игра) записи до старта → `SkippedBeforeStart`;
    - стандартные эффекты ещё действующих стадий записей с `catchUp` → `ModModifiers.Add` с остатком срока (каналы по таблице design, причина `hist.catchup`); идентификаторы записей сохраняются; записи выключенных категорий (по умолчанию `disaster`) и с ложным `featureActive` не догоняются;
    - одно письмо «Обстановка в отрасли» (`ModMail.Send`, `MailPriority.Normal`, `Tag = "hist:catchup"`);
    - AC-4.1 (699 и 334 дня при включённой `disaster`), AC-4.2 (по умолчанию), AC-4.3 (криптобум, остаток 90 дней).
    - Could (FR-4.4, «Хроника до вашего старта» — 3–5 строк о пропущенных событиях): Отложено (2026-10-11): Could, не реализовано в первой версии.
  - **Depends**: TSK-006, mod-framework TSK-027, TSK-059 (`ModModifiers.Add`), TSK-062 (`ModMail`)

- [x] **TSK-008**: Диспетчер срабатываний (авто 2026-10-10)
  - **Requirement**: FR-2, FR-10
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalDispatcher.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/HistoricalDispatcherTests.cs`
  - **Acceptance**:
    - для каждой `FireSpec` стадии и каждой компании роли вызывается `EventBus.Fire(eventId, EventFireContext { CompanyKey, Args, IgnoreConditions = true })`; `global` → цель «игрок»; групповые роли → по компании;
    - `Args` по таблице design (id записи, имена ролей, ключ второй роли, `ExtraArgs`);
    - `onlyIf` (нотация `ModConditions`) проверяется по компании;
    - результат `UnknownEvent` → запись `Error`; `Disabled` → `Disabled`;
    - AC-10.1 (интенсивность через `PackOptions.Severity`) на фейке движка.
  - **Depends**: TSK-005, event-system TSK-014 (`EventBus`)

## Фаза 3 — Нестандартные эффекты и выбор ИИ

- [x] **TSK-009**: Антимонопольные дела (`AntitrustCase`) (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/AntitrustCaseMachine.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/CaseService.cs`, события `hist.decide.antitrust.fine`, `hist.decide.antitrust.consent`, `hist.decide.antitrust.reimpose` в `mod/src/PTMod/Content/historical-events/events.json`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/AntitrustCaseMachineTests.cs`
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

- [x] **TSK-010**: Иск пострадавшего (`AntitrustSettlement`) (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/SettlementService.cs`, событие `hist.decide.settlement`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/SettlementTests.cs`
  - **Acceptance**:
    - условие `hist.CaseActive` и порог доли игрока 0.10;
    - 12 месяцев судебных расходов, затем выигрыш с `pWin` → перевод 3 % выручки ответчика (`Fines` → `Royalties`);
    - без игрока — событие для `challenger`-ИИ и новость;
    - AC-6.1.
  - **Depends**: TSK-009

- [x] **TSK-011**: Экспортный контроль (`ExportControl`) (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/ExportRuleMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/ExportRuleService.cs`, `mod/src/PTMod/Features/HistoricalEvents/HistRatingProvider.cs`, события `hist.2022.export.rule`, `hist.2023.export.update`, `hist.decide.export`, `hist.decide.export.update`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/ExportRuleMathTests.cs`
  - **Acceptance**:
    - перцентиль по `Ips` CPU на рынке; месячная индексация к `Market.Instance.AverageIps`;
    - провайдер `ModModifiers.RegisterReasonedProvider("historical-events", "market.rating.cpu", …)` за O(1) по `ModifierContext.Cpu`/`Segment`, один раз в `Register`; причина `hist.mod.export` с порогом в аргументах;
    - `ModModifiers.Invalidate("market.rating.cpu")` при вводе и ужесточении правила, выдаче лицензии, успешном лоббировании и месячном пересчёте порога;
    - вклад провайдера лежит в [0,80; 1,00] (тест на обеих ступенях и с лоббированием);
    - лицензия и лоббирование с вероятностями и сроками; ступень `update`;
    - выбор только у компаний с CPU выше порога (`onlyIf`), остальным — новость;
    - AC-7.1, AC-7.2; после загрузки провайдер читает восстановленное правило; при выключении фичи провайдер возвращает 1.0.
  - **Depends**: TSK-008, mod-framework TSK-029 (P4), TSK-059 (`RegisterReasonedProvider`, `Invalidate`)

- [x] **TSK-012**: Ценовые войны (`PriceWar`) (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/PriceWarService.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/PriceWarFallback.cs`, события `hist.decide.pricewar.aggressor`, `hist.decide.pricewar.follower`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/PriceWarTests.cs`
  - **Acceptance**:
    - при `ModFeatures.IsActive("rival-personalities")` — `RivalPersonalities.Api.ApplyPriceCut("hist:<warId>", rival, segment, depth, floorMargin)` и `RestorePriceCut("hist:<warId>")` (фейк в тесте), прямых изменений цен нет (AC-8.2);
    - иначе (`Api.IsEnabled == false`) fallback: срез `Cpu.Price` и `CpuBase.AIStrategy.TargetPrice` с полом `RealUnitCost × 1.05`, возврат исходных целей по окончании (AC-8.1);
    - выбор игрока (начать, присоединиться, держать, маркетинг); основной сегмент CPU игрока — `ModSegments.FromSales` по последнему полному месяцу, затем `ModSegments.Classify`;
    - выключение фичи и банкротство возвращают цены;
    - сегмент передаётся типом `Segment`.
  - **Depends**: TSK-008, mod-framework TSK-055 (`ModSegments`); rival-personalities/TSK-020 (`IRivalPersonalitiesApi`, мягкая)

- [x] **TSK-013**: Патентные споры (`PatentDispute`) (авто 2026-10-10)
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/DisputeMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/DisputeService.cs`, события `hist.decide.patent.plaintiff`, `hist.decide.patent.withdrawal`, `hist.decide.patent.infringement`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/DisputeMathTests.cs`
  - **Acceptance**:
    - все ветки п. 8 «Формул и баланса»;
    - платежи `ExpenseOrigin.Royalties` и `RevenueOrigin.Royalties`;
    - `research.cost` — записи `ModModifiers.Add` для компании;
    - пропуск множителя при `CompanyHelper.IsUnluckyCompany`;
    - AC-9.1.
  - **Depends**: TSK-008

- [x] **TSK-014**: Гонки характеристик (`AttributeRace`) (авто 2026-10-10)
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/RaceService.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/RaceServiceTests.cs`
  - **Acceptance**:
    - месячный пересчёт топ-N по `Frequency` или `CoreCount` (ничья — `Ips`);
    - вклад в провайдер `market.rating.cpu` за O(1) в диапазоне [1,00; 1,10], причины `hist.mod.race.frequency` / `hist.mod.race.cores`; `ModModifiers.Invalidate("market.rating.cpu")` после пересчёта; снятие по окончании;
    - AC-11.1.
  - **Depends**: TSK-011

- [x] **TSK-015**: Мост к механике брака (`ForceDesignFlaw`) и `HistFlag` (авто 2026-10-10)
  - **Requirement**: FR-12, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Effects/FlawBridge.cs`, `mod/src/PTMod/Features/HistoricalEvents/Effects/HistFlagEffect.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/FlawBridgeTests.cs`
  - **Acceptance**:
    - правила `topSellingHighEnd24m` и `perCompanyAboveAvgIps60m`;
    - вызов `DesignFlawRecallsFeature.Registry.ForceFlaw` при активной `design-flaw-recalls`, иначе (или нет CPU) — `EventBus.Fire(fallbackEventId)`;
    - `HistFlag` ставит флаг записи для `skipIfFlag`;
    - AC-12.1 на фейке.
  - **Depends**: TSK-008, design-flaw-recalls/TSK-020 (мягкая)

- [x] **TSK-016**: Выбор ИИ (`HistoricalAiChooser`) (авто 2026-10-10)
  - **Requirement**: FR-13
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistoricalAiChooser.cs`, `mod/src/PTMod/Features/HistoricalEvents/Logic/HistoricalAiChooserCore.cs`, `mod/tests/PTMod.Tests/Features/HistoricalEvents/HistoricalAiChooserTests.cs`
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

- [x] **TSK-017**: Записи 1971–1989 (№ 1–12) и их события (авто 2026-10-10)
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 1–12 в `mod/src/PTMod/Content/historical-events/calendar.json`; события пака для них (глобальные эффекты, `hist.decide.secondsource`) в `events.json`; ключи `hist.<id>.title` / `.text` / `.fact` в `mod/src/PTMod/L10n/{ru,en}/historical-events.json`
  - **Acceptance**:
    - 12 записей проходят валидатор календаря, события — валидацию движка;
    - числа и роли совпадают с таблицей design; стандартные эффекты помечены `scalable`; компании в ролях — по `UniqueID` (Toshiba = 6, Intel = 0, AMD = 1), имена не вшиты;
    - записи DRAM (7, 8, 12) используют канал `production.unitCost`, не `production.memoryCost`;
    - у каждой записи `_note` с фактом и датой, `history.sources` ≥ 1;
    - справки ru/en (`cause`) по 2–4 предложения, нейтральный тон.
  - **Depends**: TSK-002, TSK-009, TSK-012, TSK-013

- [x] **TSK-018**: Записи 1990–2005 (№ 13–24) (авто 2026-10-10)
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 13–24 в `calendar.json`; события пака, включая `hist.decide.brand.leader`, `hist.decide.brand.coop`, `hist.1999.jiji.hit`, `hist.1999.jiji.market`, `hist.1999.jiji.recovery`; ключи L10n
  - **Acceptance**: как TSK-017 для записей 13–24; запись 20 (`disaster`) по умолчанию получает статус `Disabled`.
  - **Depends**: TSK-017, TSK-014, TSK-015

- [x] **TSK-019**: Записи 2006–2025 (№ 25–42), включая криптобумы (авто 2026-10-10)
  - **Requirement**: FR-1, FR-10
  - **Deliverables**: записи 25–42 в `calendar.json`; события пака, включая `hist.2011.tohoku.hit`, `hist.decide.shortage.priority`, `hist.decide.insourcing`, `hist.2017.cryptoboom`, `hist.2021.cryptoboom`, `hist.fizzle`, `hist.catchup.summary`; ключи L10n
  - **Acceptance**:
    - как TSK-017 для записей 25–42;
    - криптобумы: `gpu.demand` ×1.35 (2017) и ×1.45 (2021) на 365 дней, глобальный `ModifierTimed`, `catchUp`, условие `featureActive{id: "gpu-product-line"}`, запасной вариант `skip`; AC-10.2, AC-10.3, AC-4.3;
    - итог календаря — 42 записи, распределение по категориям совпадает с design (9/10/2/3/3/2/4/2/2/2/1/2: boom, macro, shock, disaster, priceWar, patent, antitrust, export, flaw, race, brand, oem);
    - записи `disaster` (20, 30, 35) по умолчанию `Disabled`; AC-4.1/4.2.
  - **Depends**: TSK-018, TSK-010, TSK-011

- [ ] **TSK-020**: Сверка фактов и источников — Частично: все пометки `[проверить]` из design перенесены в `_note` записей (7, 8, 12, 13, 19, 23, 41, 42); автоматические части (2–4 предложения справок, нейтральные тексты, https-источники) проверены тестами. Сверка каждого URL и даты человеком, замена `[проверить]` и справок без цитат длиннее 15 слов не выполнялась: ручная задача владельца (`mod/requests/historical-events.md`, п. 18)
  - **Requirement**: FR-1, NFR-5, NFR-7
  - **Deliverables**: обновлённые `calendar.json` и ключи `hist.*.fact`; таблица сверки в описании PR (`id` → дата → URL → «проверено»)
  - **Acceptance**:
    - каждый URL открыт человеком и подтверждает дату и суть справки;
    - все пометки [проверить] из design (в том числе источники записей 41–42) заменены на проверенные URL или записи переписаны;
    - справки — пересказ своими словами без цитат длиннее 15 слов; тексты о катастрофах нейтральны.
  - **Depends**: TSK-019

## Фаза 5 — Хроника, встроенные события, настройки

- [x] **TSK-021**: Справки в событиях и письмо догона (авто 2026-10-10)
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

- [x] **TSK-022**: Страница «Хроника отрасли» (авто 2026-10-10)
  - **Requirement**: FR-15
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/UI/ChroniclePage.cs`
  - **Acceptance**:
    - страница `ctx.HubPage("hist.chronicle.title", build)`, 3 вкладки по design;
    - будущие события скрыты при `spoilers = false`; при `true` — год трёх ближайших;
    - отладка (`debug`, только отладочная сборка): выбор записи, «Запустить сейчас», `debugForceLeader`;
    - AC-15.1.
  - **Depends**: TSK-009, TSK-011, TSK-012, TSK-013, mod-framework TSK-036 (виджеты), TSK-039 (`HubPage`)

- [x] **TSK-023**: Письма к встроенным событиям игры (Could) (авто 2026-10-10)
  - **Requirement**: FR-16
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/BuiltInEventNews.cs`, события `hist.news.solarfoundries`, `hist.news.x86license`
  - **Acceptance**:
    - `Optional`-постфикс `CompanyEventHandler.TriggerSolarFoundriesSpinOff` и подписка на `LicenseHelper.OnX86LicenseRevoked` вызывают события-справки;
    - при загрузке и перемотке писем нет; игровая логика не меняется;
    - AC-16.1.
  - **Depends**: TSK-021, mod-framework TSK-023 (`ModHooks`)

- [x] **TSK-024**: Выключение посреди партии и самопроверка (авто 2026-10-10)
  - **Requirement**: FR-17, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/HistSelfCheck.cs`, дополнения `HistoricalEventsFeature.OnDisabled/OnEnabled`
  - **Acceptance**:
    - выключение: провайдеры `market.rating.cpu` и `acquisition.price` возвращают 1.0, дела и споры на паузе, цены войн возвращены (`RestorePriceCut` или исходные цели); записи `ModModifiers` не удаляются (`RemoveAll` не вызывается); включение возобновляет без пересоздания записей;
    - самопроверка — пункты из design (цели, пак, `eventId`, типы эффектов, предикаты, каналы `market.rating.cpu` с `PerCpu`/`PerSegment`, `acquisition.price`, `gpu.demand`, календарь ≥ 25, префикс `hist.`, отсутствие патчей на `AddCpu`/`NextMonth`);
    - провал канала `market.rating.cpu` → записи `export` и `race` получают `Disabled`;
    - AC-17.1, AC-17.2.
  - **Depends**: TSK-011, TSK-012, TSK-014, TSK-023, TSK-028, mod-framework TSK-042 (`ModSelfCheck`), TSK-066 (`ModIds`)

## Фаза 6 — Антимонопольная надбавка, локализация и проверка

- [x] **TSK-028**: Антимонопольная надбавка к цене поглощений (`acquisition.price`) (авто 2026-10-10)
  - **Requirement**: FR-19, NFR-9
  - **Deliverables**: `mod/src/PTMod/Features/HistoricalEvents/Logic/AcquisitionAntitrustMath.cs`, `mod/src/PTMod/Features/HistoricalEvents/AcquisitionAntitrust.cs` (провайдер), `mod/tests/PTMod.Tests/Features/HistoricalEvents/AcquisitionAntitrustMathTests.cs`
  - **Acceptance**:
    - провайдер `ModModifiers.RegisterReasonedProvider("historical-events", "acquisition.price", …)` регистрируется один раз в `Register` (порядок `Register` фич не важен, связывание ленивое); читает кэш `MarketShareCache` за O(1);
    - таблица формулы 11: S' 0,30 → 1,00 без причины; 0,50 → 1,25; 0,65 с делом → ≈ 2,03; 0,97 с делом → предел 3,00 (AC-19.1–19.3);
    - причина `hist.mod.acqAntitrust` с долей после сделки в аргументах (видна в `Explain`);
    - `ModModifiers.Invalidate("acquisition.price")` после месячной пересборки долей и при открытии и закрытии дела;
    - категория `antitrust` выключена или фича выключена → 1,00;
    - запрета сделки нет; вклад в [1,00; 3,00];
    - параметры `acquisitionAntitrust` читаются из `params.json`.
  - **Depends**: TSK-004, TSK-009, mod-framework TSK-059 (`RegisterReasonedProvider`, ленивое связывание, `Invalidate`); rival-acquisitions (читает канал, мягкая)

- [x] **TSK-025**: Строки ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**: ключи `hist.*` (страница, кнопки, тексты и справки событий, письмо догона, причины `hist.rep.*` и `hist.mod.*`, `hist.feature.desc`) в `mod/src/PTMod/L10n/ru/historical-events.json` и `mod/src/PTMod/L10n/en/historical-events.json`
  - **Acceptance**:
    - все ключи из `calendar.json`, `events.json` и UI есть в обоих языках и начинаются с `hist.` (`ModIds.IsL10nKeyAllowed`);
    - тест полноты ключей фичи зелёный;
    - аргументы позиционные, деньги и даты отформатированы игровыми `StringFormatter`;
    - названия компаний — из игры; в справках допустимы реальные названия как исторические факты.
  - **Depends**: TSK-020, TSK-022, TSK-028, mod-framework TSK-013, TSK-043 (`ModL10n`, `core.json`), TSK-066 (`ModIds`)

- [x] **TSK-026**: Симуляция календаря (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3, NFR-1, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/HistoricalEvents/CalendarSimulationTests.cs`, генератор синтетических миров `mod/tests/PTMod.Tests/Features/HistoricalEvents/SyntheticWorlds.cs`
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
| TSK-001 Каркас фичи и настройки | Complete (авто 2026-10-10, кусок 1: `HistoricalEventsFeature` (`[ModFeature]`, `Id` = `historical-events`, `NameKey` = `hist.feature.name`, `DescriptionKey` = `hist.feature.desc`, `DependsOn` = { `event-system` }, `EnabledByDefault` и `SupportsHotToggle` = true) + `HistSettings` + чистая таблица `Logic/HistParamSpecs`: 17 `ParamSpec` (`mode` Int 0/1, `intensity` Float 1.0 в 0.5…1.5, 12 переключателей категорий — все включены, кроме `disaster`, `historyNotes`, `spoilers`, `debug`); пак регистрируется `EventRegistry.RegisterPack("historical-events", "content/historical-events", PackOptions { SettingsFeatureId, Severity = intensity })`, `Severity` меняется на месте по `ModSettings.Changed`; `OnDisabled` пуст, `RemoveAll` нигде не вызывается, `OnEnabled` только читает календарь, записи `ModModifiers` не создаёт; заглушки `events.json` и `calendar.json` (`{ "schema": 1, "records": [] }`); строки `hist.feature.*` и `hist.param.*` в ru/en; сборка фичи без предупреждений, тесты (в том числе проверки исходника класса: объявление, пак, отсутствие `RemoveAll` и патчей) зелёные; видимость в хабе и горячее переключение — в игре, TSK-027) |
| TSK-002 Схема календаря и валидатор | Complete (авто 2026-10-10, кусок 1: `Logic/CalendarModel` (`CalendarEntry`, `StageSpec`, `FireSpec`, `RoleSpec`, `HistoryInfo`, `ConditionUse`; маппер `CalendarEntry.FromJson` проверяет форму, отсутствующее читается по умолчанию), `Logic/SimpleDate`, `Logic/HistVocabulary` (`EntryStatus`, 12 категорий, виды ролей), `Logic/CalendarValidator` (все правила Security: даты и окно, `jitterDays` 0…60, `offsetDays` 0…1825, `minShare` 0…1, `uniqueId` 0…12, уникальные `id` с префиксом `hist.` через `ModIds`, существование `eventId` в паке (две фазы: при загрузке и `ValidateEventsExist`, когда паки уже прочитаны), ≥ 1 источник `https://`, ключи `hist.` и предупреждение о ключе без текста, предикаты только ядра, `es.` и `hist.`, роли условий объявлены, блок `acquisitionAntitrust`), `HistContentParams` и `params.json` = пример design; загрузка `CalendarContent` через `ModContent.LoadRecords<CalendarEntry>` и `ModConditions.Parse`, на первом `GameReady` меньше 25 валидных записей → `ModFeatures.MarkUnavailable`; тесты `CalendarValidatorTests` (по тесту на правило), `CalendarModelTests`, `HistParamsTests` зелёные; диапазоны, которых нет в design, выбраны автором: `months` роли 1…120, `majorSpacingDays` 0…365, `aiChooserPriority` ±1000, стадия 0 имеет `offsetDays` 0 и стадии идут по возрастанию, `fires` стадии не пусты, `skipIfFlag` — простое имя до 40 знаков; проверка «42 записи, у каждой источник» появится с контентом TSK-017…019; до него фича на первом `GameReady` будет `Unavailable` — ожидаемо, см. `mod/requests/historical-events.md`, п. 5) |
| TSK-003 Секция сохранения | Complete (авто 2026-10-11, кусок 1 и закрытие пункта «фича выключается для сейва»: `HistSaveSection : HistSaveSectionBase, IModSaveParticipant` (`SectionId` = `historical-events`, `SchemaVersion` = 1; чистая часть `Logic/Save/HistSaveSectionBase`, DTO `HistSaveData` и `HistSaveCodec` лежат в `Logic/Save/`, потому что сборка тестов берёт только `Logic/`), `Write()` без `_schema`, в DTO поля версии нет (проверяет тест по рефлексии), круговая сериализация примера design и секции со всеми видами объектов, нормализация (отрицательные дни и суммы → 0, статус вне enum → `Pending`, сдвиг в ±60, ключи не `CompanyKey` отбрасываются вместе с объектом, повторные `id`, нулевые элементы), `ResetToDefaults` без секции: якорь раньше текущей даты → `SkippedBeforeStart`, остальные `Pending`, `initialized` = true (календарь и дата ещё неизвестны при чтении файла — статусы выставляет `TryApplyDefaults` на `GameReady(Loaded)`), новая секция и отсутствующая секция проверены на настоящем `SideFileModel`; пункт «фича выключается для сейва» сделан через `ModSave.IsSectionNewer` (`HistoricalEventsFeature.SaveLocked`, `CalendarScheduler`, `WarnIfLocked`); юнит-теста ветки нет) |
| TSK-004 Доли рынка и IWorldView | Complete (авто 2026-10-10, кусок 2: `Logic/IWorldView` (`IWorldView`, `CompanyInfo`, `CpuInfo` с журналами `MarketData` как есть, `SegmentMask`, `WorldMath`: основной сегмент CPU по D15 — ИИ `AIStrategy.TargetMarket`, игрок `ModSegments.FromSales` по последнему полному месяцу, затем `ModSegments.Classify`; `CashHeadroom` = деньги − порог; возраст CPU по году), `Logic/MarketShareCache` (окно полных месяцев `Count-1-M … Count-2` без текущего, сегменты и «все» = `Sold` + `SoldByContracts`, нулевой знаменатель → 0, `Ranking`: ничья — игрок, затем меньший `UniqueID`, банкроты вне рейтинга, но в знаменателе; перестройка раз в месяц (`EnsureFresh` по `MonthsSinceStart`), ответы запоминаются до следующей), `Game/GameWorldView` (`ModCompanies.All`, `CpuDataProvider.GetAllCpus`, `CompanyFinance.Reports` — выручка за 12 месяцев перед текущим, `Market.AverageIps`, `ModCredit.ThresholdOf`, `ModFeatures.IsActive`; снимок раз в игровой день и при смене версии `ModCompanies`, `CentralBank.DebtLimit` не читается), `HistTickPriority`; тесты `ShareMathTests` + `FakeWorld`: окно, сегменты, ничья, перестройка 400 CPU × 36 месяцев ≤ 2 мс, AC-3.4, проверка исходника `GameWorldView`; пути, отличающиеся от списка Deliverables, — `mod/requests/historical-events.md`, п. 7) |
| TSK-005 Роли и условия | Complete (авто 2026-10-10, кусок 2: `Logic/RoleResolver` (`RoleResult`, `RoleOptions`): все 12 видов ролей, `minShare` (для группы — отсев участников), цепочка `fallback` (в `strict` у `named` не используется), одна компания — одна роль, пустая группа — роль не распознана, встроенные роли (`player`, `allCpuCompanies`, `allManufacturers`, `allFabless`), которые называют срабатывания и условия без объявления; `Logic/HistPredicates` + `HistConditions` (`hist.ShareAtLeast`, `hist.HasCpuSince`, `hist.CaseActive`, `hist.PlayerHasCpuAbove`, `hist.IsFabless` — `EventConditions.Register` в `Register` фичи, имена с префиксом `hist.`; та же функция, что прямой вызов в тестах), `Logic/ConditionEvaluator` + `Logic/HistRoleScope` (роли записи в области видимости на время вычисления, условия записи — конъюнкция; стандартные условия ядра и движка работают рядом), флаг `debugForceLeader` (`RoleOptions.ForceLeader`, `HistState.ForceLeader`, `HistRuntime.ForceLeader` — только при включённом `debug`), `HistRuntime` (стык с `ModConditions`, `ModRandom`, настройками); тесты `RoleResolverTests`, `ConditionEvaluatorTests`: AC-3.1…3.4; `HistRoleScope` лежит в `Logic/`, а не в корне фичи — `mod/requests/historical-events.md`, п. 7) |
| TSK-006 Планировщик календаря | Complete (авто 2026-10-10, кусок 2: `Logic/SchedulerCore` (очередь «проверить запись / сыграть стадию» по дням от 1971-01-01 с указателем, в тихий день одно сравнение; порядок в один день — якорь, затем `id`; сдвиг бросается один раз на партию, один `rng.Value()` на запись, и сохраняется; повторная проверка 1-го числа и в последний день окна, истечение → `news`/`skip` и `Expired`, запись с окном, закончившимся больше 31 дня назад, закрывается без новости; `major` с интервалом `majorSpacingDays` (45) и переносом в пределах окна; категория выключена → `Disabled` (запись возвращается на месячном проходе, если категорию включили и окно открыто); `skipIfFlag`; стадии по сохранённым ролям; `Error` при исключении с `id` и отчёт в `ModHooks`; `Initialize` ровно один раз по `initialized`, `Rebuild` после загрузки и при смене объекта секции; `DebugFireNow`) и `CalendarScheduler` (`DayPassed`, `MonthPassed` после пересчёта долей, `FastForwardEnded`, резерв `GameReady(NewGame)`, восстановление на `GameReady(Loaded)` и `OnEnabled`; всё под `ModHooks.Guard`; `ModRandom.Stream("historical-events")`; патчей нет, пакетный прогон идёт по дням); тесты `CalendarSchedulerTests`, сквозной `HistoricalPipelineTests`: AC-2.1…2.3, AC-2.2 до конца окна; подписки и порядок тиков проверены по исходнику, в игре — TSK-027) |
| TSK-007 Поздний старт и догоняющие эффекты | Complete (авто 2026-10-10, кусок 2: `Logic/CatchUpPlanner` (записи с `catchUp` и якорем до старта, категория включена, роли найдены и условия верны сейчас; стадии, что уже началась и ещё не закончилась: остаток = якорь + `offsetDays` + `durationDays` − старт; `MarketDemand` по сегментам или по четырём, `ModifierTimed` на весь рынок или на компании роли, `FactoryOutput` → `production.output`; `onlyIf` по компаниям; множитель с интенсивностью `IntensityMath`; выборы и нестандартные эффекты не догоняются) и `CatchUpService` (`ModModifiers.Add` с причиной `hist.catchup`, идентификаторы записей — в `catchUpEntryIds` секции; одно письмо «Обстановка в отрасли» `ModMail.Send`, `MailPriority.Normal`, тег `hist:catchup`, если что-то применено; ждёт пака событий, если движок ещё не прочитал его: на `FastForwardEnded`, на `GameReady(NewGame)` и ежедневно до 31 дня, остаток считается от дня применения; только для новой игры); сдвиги не учитываются; тесты `CatchUpServiceTests`: AC-4.1 (699 и 334 дня при включённой `disaster`), AC-4.2, AC-4.3 (остаток 90 дней, без `gpu-product-line` не применяется); строки `hist.catchup.*` в ru/en; обход ожидания паков — `mod/requests/historical-events.md`, п. 8) |
| TSK-008 Диспетчер срабатываний | Complete (авто 2026-10-10, кусок 2: `Logic/DispatcherCore` (для каждой `FireSpec` и каждой компании роли — `IEventFirer.Fire`; `global` и `player` → игрок, групповые роли — по компании в порядке нахождения; `Args` по таблице design: id записи, имя адресата, имя и ключ второй компании, `extraArgs`; `onlyIf` по компании; `UnknownEvent` → запись `Error`, `Disabled` → `Disabled` (остальные срабатывания стадии не посылаются), исключение шины — неудавшееся срабатывание; новость `hist.fizzle` с `[заголовок, id]` или `fallbackFires`) и `HistoricalDispatcher` + `EventBusFirer` (`EventBus.Fire(eventId, EventFireContext { CompanyKey, Args, IgnoreConditions = true })`); тесты `HistoricalDispatcherTests`: AC-10.1 на фейке движка (интенсивность доходит до эффектов через `PackOptions.Severity`, диспетчер ничего не масштабирует), проверка исходника шины; договорённости об аргументах для контента — `mod/requests/historical-events.md`, п. 9) |
| TSK-009 Антимонопольные дела | Complete (авто 2026-10-10, кусок 3: `Logic/AntitrustCaseMachine` (правила без игры: `CaseParams` с диапазонами, `CaseStages`, `CaseStep` с запросами «заплатить / вернуть / репутация / ограничение / событие»; штраф `finePct × выручка 12 мес.`, `pay` (−5 и +2 к репутации, `Fines`), `provision` (резерв, возврат `RevenueOrigin.Interest`), `appeal` (гарантия `Interest` в месяц), решение суда с поправкой +0.10 к отмене при доле ниже `dominanceLostShare`, снижение, оставление в силе с процентами, повторный штраф `F × 0.355` с шансом `reimposeP` и вторая апелляция; режимы `consent` (соглашение −3, суд 9 мес. и 50/50: +1 или ограничение, штраф 1 %, −5) и `warning`) и `Logic/CaseCore` (состояние в `cases` секции сейва, `Open` / `Choose` / `OnMonth`, один бросок на решение, дело, которое не выбирали 180 дней, получает вариант по умолчанию, дело цели-банкрота закрывается без платежей, три сбоя подряд закрывают дело, `ModModifiers.Invalidate("acquisition.price")` при открытии и закрытии открытого дела, `IsActive` для `hist.CaseActive` и `HasOpenCase` для TSK-028) + `Effects/CaseService` (`IEventEffectHandler`: `Resolve` считает штраф один раз, `Describe` показывает суммы; id дела — `Args[4]`) + события `hist.decide.antitrust.fine`, `.consent`, `.reimpose` и строки ru/en; тесты `AntitrustCaseMachineTests`: AC-5.1…5.3, ожидаемая потеря 0.58 F ± 0.01 F на 100 000 бросков, поправка доминирования, режимы, месячный проход с поддельной экономикой; работа в игре и порядок вызовов движка — TSK-027; пути и допущения — `mod/requests/historical-events.md`, п. 11…13) |
| TSK-010 Иск пострадавшего | Complete (авто 2026-10-10, кусок 3: `Logic/SettlementCore` (12 месяцев судебных расходов `legalCostPct × выручка истца`, затем бросок `pWin` и перевод `settlementPct × выручка ответчика 12 мес.`: `Fines` ответчика → `Royalties` истца; иск прекращается без выплат, если истец или ответчик обанкротился; числа иска хранятся в `SettlementDto` — поля `pWin`, `settlementPct` добавлены в секцию) + `Effects/SettlementService` (`sue` / `decline`; иск подаётся, только пока дело активно (`hist.CaseActive`); ответчик — `Args[3]`, иначе цель дела) + событие `hist.decide.settlement` (кнопка «Подать иск» закрыта условием доли игрока ≥ 0.10 за 36 мес.; для ИИ-соперника то же событие с новостью — через `notifyPlayer`, выбор роли истца — в записи календаря, TSK-018); тесты `SettlementTests`: AC-6.1 (900 млн), расходы по месяцам, проигрыш, банкротство, числа иска) |
| TSK-011 Экспортный контроль | Complete (авто 2026-10-10, кусок 3: `Logic/ExportRuleMath` (перцентиль по `Ips` CPU на рынке с линейной интерполяцией, индексация `T0 × (AverageIps / AverageIps0)^indexing`, множитель `1 − blockedShare × lobbyFactor` в [0,80; 1,00] (доля не выше 0,20), `ExportCore`: правило, ужесточение, лицензия и лоббирование с шансом и сроком (бросок берёт `Resolve` из генератора экземпляра), месячный пересчёт порога и срока, `ModModifiers.Invalidate("market.rating.cpu")` при каждом изменении, плоская копия для провайдера за O(правил) без аллокаций, пересборка при замене секции загрузкой) + `HistRatingProvider` (один провайдер `market.rating.cpu`: экспорт × гонка, причина `hist.mod.export` с порогом; 1.0 при выключенной фиче и категории) + `Effects/ExportRuleService` + события `hist.2022.export.rule`, `hist.2023.export.update`, `hist.decide.export`, `hist.decide.export.update`; `HistState.ExportThreshold` для `hist.PlayerHasCpuAbove`; тесты `ExportRuleMathTests`: AC-7.1, AC-7.2, обе ступени, диапазон, инвалидации, правило после загрузки. Выбор только у компаний с CPU выше порога зависит от `onlyIf` в записи (TSK-018): предикат `anyCompany` смотрит на CPU любой компании — `mod/requests/historical-events.md`, п. 12) |
| TSK-012 Ценовые войны | Complete (авто 2026-10-10, кусок 3: `Logic/PriceWarFallback` (пол `RealUnitCost × (1 + floorMargin)`, срез цены и цели, цена никогда не растёт) и `PriceWarCore` (агрессор `cut`, остальные ИИ `followCut`, при `IPriceCutApi.IsEnabled` — только `ApplyPriceCut("hist:<warId>", …)` и `RestorePriceCut` без записи цен, иначе fallback с сохранением исходных `TargetPrice` в секции; игрок снижает свои цены один раз и только по выбору; маркетинг (`marketingCostPct`, `popularity.gain` × 1.10 на 180 дней), отказ игрока-агрессора передаёт войну следующему ИИ с наибольшей долей, конец по `endDay`, выключение фичи и банкротство возвращают цены) + `Effects/PriceWarService` + `Effects/GamePrices` (единственная запись `Cpu.Price` / `AIStrategy.TargetPrice`) + события `hist.decide.pricewar.aggressor`, `.follower`; сегмент передаётся типом `Segment`, основной сегмент CPU игрока — из D15 (`CpuInfo.Segment`); числа записи (сегменты, глубина, срок) приходят в `extraArgs` (`WarParams.ApplyArgs`); тесты `PriceWarTests` на фейках API и цен: AC-8.1 (500 → 400, цель назад 500), AC-8.2 (вызовы API, прямых записей нет)) |
| TSK-013 Патентные споры | Complete (авто 2026-10-10, кусок 3: `Logic/DisputeMath` (`DisputeParams` из `disputeDefaults`, `DisputeSpec`, `DisputeMath`, `DisputeCore`: арбитраж 48 мес. с расходами и броском `pWinArbitration` (выигрыш — истец платит 3 % своей выручки `Fines` → `Royalties`, проигрыш — `research.cost` × 1.3 на 730 дней), лицензия 1,25 % месячной выручки 36 мес. (`Royalties` обоим), своя разработка × 1.3 на 36 мес., «судиться» с броском `pWinInfringement` (проигрыш — 1.5 × мировое), мировое 3 % разово, «переделать» × 1.2 на 24 мес.; множитель `research.cost` пропускается при `CompanyHelper.IsUnluckyCompany`; иск истца шлёт решение ответчику через день; брошенный спор снимается через 180 дней, банкрот закрывает спор) + `Effects/DisputeService` + события `hist.decide.patent.plaintiff`, `.withdrawal`, `.infringement`; тесты `DisputeMathTests`: все ветки п. 8, AC-9.1 (5 млн), пропуск для Cyrix. Длина «судиться» (24 мес.), коэффициент 1.5 и срок штрафа проигранного арбитража вынесены в необязательные числа `fightMonths`, `fightLossFactor`, `lostArbitrationDays` блока `disputeDefaults`) |
| TSK-014 Гонки характеристик | Complete (авто 2026-10-10, кусок 3: `Logic/RaceMath` (`RaceParams` с бонусом в [1,00; 1,10], `RaceMath.TopCpus` — топ-N рынка по `Frequency` или `CoreCount`, ничья по `Ips`, затем по `SaveId`; `RaceCore`: запуск, месячный пересчёт топа, `ModModifiers.Invalidate("market.rating.cpu")` после пересчёта, снятие по окончании, вклад за O(гонок) с множеством `SaveId`; `HistRatingMath.Combine` — произведение экспорта и гонки с причиной большего отклонения) + `Effects/RaceService` + вклад в `HistRatingProvider` с причинами `hist.mod.race.frequency` / `hist.mod.race.cores`; тесты `RaceServiceTests`: AC-11.1, ничья, месячный пересчёт, конец гонки, гонка после загрузки) |
| TSK-015 Мост к механике брака и HistFlag | Complete (авто 2026-10-10, кусок 4: `Logic/FlawBridgeCore` (`ForceFlawParams`: `pick`, `typeId`, `severityLeader`, `severityOthers` (без него равен `severityLeader`), `discovery`, `maxPerCompany`, `leaderMonths`, `fallbackEventId`; `FlawPicker`: `topSellingHighEnd24m` — лучший по продажам в HighEnd за 24 полных месяца, `perCompanyAboveAvgIps60m` — `Ips ≥ Market.AverageIps` и дизайн не старше 60 месяцев, лучший по продажам за 12 месяцев; ничья по `Ips`, затем по `SaveId`; `FlawBridgeCore.Plan` / `Run`: тяжесть лидера HighEnd и остальных, `IFlawGate.Force` для каждого выбранного CPU, при неактивной `design-flaw-recalls`, без CPU или при отказе механики — `fallbackEventId` той же компании через `EventBus.Fire` с `DelayDays = 1`) + `Logic/HistFlagCore` (`HistFlagParams`: `entry` (без него — запись, выпустившая событие), `flag`, необязательный `textKey`; флаг ставит `SchedulerCore.SetFlag`, где его читает `skipIfFlag`) + `Effects/FlawBridge` (`GameFlawGate` → `DesignFlawRecallsFeature.Registry.ForceFlaw`) и `Effects/HistFlagEffect`; проводка в `HistServices` и `HistoricalEventsFeature` (восемь обработчиков); строки `hist.fx.flaw*`, `hist.fx.flag`, `hist.flaw.*` ru/en; тесты `FlawBridgeTests`: оба правила, окно 24 месяца без текущего, AC-12.1 на фейке (лидер HighEnd 1994 года, `arith`, `Major`, `External`, вызов `ForceFlaw` для самого продаваемого CPU), Meltdown (лидер `Major`, прочие `Minor`, по одному CPU), запасное событие, `HistFlag`, проверка содержимого пака; договорённости для контента — `mod/requests/historical-events.md`, п. 14) |
| TSK-016 Выбор ИИ | Complete (авто 2026-10-10, кусок 4: `Logic/HistoricalAiChooserCore` (`IsOwnEvent` — только `hist.decide.*`; `UpfrontCost` — сумма отрицательных `AppliedEffect.value` типов фичи и `Money` среди сработавших и не провалившихся эффектов; `Choose(OptionCost[], AiWeights, cashHeadroom, defaultChoice, roll01)`: вес 0 у недоступного варианта и у варианта дороже запаса (строго «больше»; бесплатный не отсекается), при нулевой сумме — `defaultChoice`, выбор по доле суммарного веса; `AiWeights` — `aiWeight` и `aiWeightOverrides` из `params.json`) + `HistoricalAiChooser` (`IEventAiChooser` + `IEventAiChooserFilter`; запас `MoneyAmount − ModCredit.ThresholdOf(company)`, один бросок на решение, `ModHooks.Guard`, при выключенной фиче не вызывается) + регистрация `EventAi.AddChooser(chooser, aiChooserPriority)` в `Register` и перенос на приоритет из `params.json` после его чтения; тесты `HistoricalAiChooserTests`: AC-13.1 (штраф 2 млрд, запас 1,5 млрд — выбрана апелляция), AC-13.2 (цепочка 100 и 10, `null` первого передаёт решение второму), `defaultChoice`, распределение на 10 000 бросков ± 0,02 (до и после отсечения), переопределения весов, проверка исходника выборщика; поправка о `ChoiceOption.Cost` — `mod/requests/historical-events.md`, п. 15) |
| TSK-017 Записи 1971–1989 | Complete (авто 2026-10-10, кусок 5: `calendar.json` записи 1–12 по таблице design (якоря, категории, роли по `UniqueID`: Intel 0, AMD 1, Toshiba 6; записи DRAM 7, 8, 12 на `production.unitCost`); их события пака (глобальные эффекты, `hist.decide.secondsource`, ценовые войны через `extraArgs`, `hist.decide.patent.plaintiff`); ключи `hist.<id>.title/.fact` и текстов событий в ru/en; тесты `BuiltinCalendarTests`: все 42 записи проходят `CalendarValidator` с реальным паком и строками, стандартные эффекты `scalable` в диапазонах, справки 2–4 предложения; разовая проверка движком (`PackCatalog`, scratchpad, не в репозитории): 63 события, 0 пропусков; отклонения от design и запросы — `mod/requests/historical-events.md`, п. 16–17) |
| TSK-018 Записи 1990–2005 | Complete (авто 2026-10-10, кусок 5: записи 13–24 и их события: бренд-программа (лидер с флагом `launched`, кооперация игрока, стадия +31 дн. для претендента), `hist.1994.fdiv` с запасным вариантом (`ForceDesignFlaw arith`), Jiji (`hist.1999.jiji.hit/market/recovery`, стадия +30 дн. с `skipIfFlag recovered`), суд ФТС `hist.decide.antitrust.consent`, предупреждение JFTC `hist.2005.jftc`, гонка `hist.2000.ghzrace`; запись 20 (`disaster`) выключена по умолчанию (тест); отклонения — `mod/requests/historical-events.md`, п. 17) |
| TSK-019 Записи 2006–2025 (включая криптобумы) | Complete (авто 2026-10-10, кусок 5: записи 25–42 и их события: ценовые войны 2006 и 2017, Tohoku (`hist.2011.tohoku.hit`: перенос ×0.80 или ожидание ×0.60), Meltdown (`ForceDesignFlaw security`, Major/Minor), пандемия со стадией +90 (`hist.2020.covid.rebound`), дефицит чипов (приоритет OEM или рознице; `allFabless` ×0.90), скидка OEM (приближение, см. п. 17), CHIPS Act (`taxes` ×0.75 на 1825 дн.), криптобумы `hist.2017.cryptoboom` и `hist.2021.cryptoboom` (`gpu.demand` ×1.35 и ×1.45 на 365 дн., глобально, `featureActive`, запасной `skip`); события `hist.fizzle` и `hist.catchup.summary`; распределение категорий 9/10/2/3/3/2/4/2/2/2/1/2 и AC-4.1–4.3 (остатки 699, 334, 90 дн.) на реальных датах: тесты; отклонения — `mod/requests/historical-events.md`, п. 17) |
| TSK-020 Сверка фактов и источников | Частично (авто 2026-10-10, кусок 5: пометки `[проверить]` из design перенесены в `_note` записей 7, 8, 12, 13, 19, 23, 41, 42; автоматические проверки справок и источников — тестами; сверка URL и дат человеком не выполнялась: ручная задача владельца, см. `mod/requests/historical-events.md`, п. 18) |
| TSK-021 Справки в событиях и письмо догона | Complete (авто 2026-10-10, кусок 6: `Logic/HistNotes` (`Decorate`, `DomainOf`, `EntryIdOf`) кладёт в аргументы события два слота — `Args[6]` строку «Источник: домен» (ключ `hist.mail.source`, домен первого источника записи) и `Args[7]` справку (`history.factKey` записи; пусто при `historyNotes = false`); слоты стоят сразу за двумя `extraArgs`, потому что архитектурный тест ядра форматирует каждую строку мода восемью аргументами (`{8}` и выше он роняет), поэтому числа ценовой войны в `calendar.json` сжаты в `[warId, "сегменты;cut;followCut;дни"]` (`WarParams.ApplyArgs` читает обе формы); дверь `EventBusFirer.WithNotes` (через неё идёт любой запуск события фичи) вызывает `Decorate`; `text` всех событий пака оканчивается на `{6}`, `cause` — общий ключ `hist.cause` = `{7}` (кроме служебных `hist.fizzle` и `hist.catchup.summary`), справка у каждого события — факт запустившей его записи; валидатор: `IEventTexts` (предупреждение о событии без `text` или `cause`) и не больше двух `extraArgs`; тесты `HistNotesTests`: домен, слоты, `historyNotes` выкл. → справка пуста, последняя строка `text` каждого запуска каждой из 42 записей на двух языках — домен её источника, у каждого события есть `text` и `cause`, события с выбором `important: true`, остальные нет, категория `history` объявлена в `config.json`, AC-14.1; `hist.fizzle` с заголовком записи — TSK-008, письмо догона с тегом `hist:catchup` и `Normal` — TSK-007; справка подставляется на языке игры в момент запуска; в игре не проверено — TSK-027, подробности — `mod/requests/historical-events.md`, п. 20) |
| TSK-022 Страница «Хроника отрасли» | Complete (авто 2026-10-10, кусок 6: `UI/ChroniclePage.cs` — `ctx.HubPage("hist.chronicle.title", build)`, три вкладки на `ModUI.Button`, список в `ModUI.ScrollView` строками `ModUI.Info`, блок отладки (список записей, «Запустить сейчас», `debugForceLeader`) внутри `#if PTMOD_DEBUG` и только при `debug`; `UI/ChronicleUi.cs`; `Game/ChronicleWorld.cs` (только чтение: календарь, секция сейва, `EventLog`, определения пака, формат игры); строки — чистая `Logic/ChronicleModel.cs` (`IChronicleWorld`, `ChronicleRow`, `CatchUpMarks`): Прошедшие (дата — заголовок: компании · итог и ваш выбор), Действует сейчас (стандартные эффекты с остатком дней и интенсивностью, догоняющие эффекты по флагу записи `caughtUp-<день старта>`, экспортные правила с порогом, долей и исключениями, ценовые войны, гонки), Дела и споры (открытые дела, иски, патентные споры); при `spoilers = false` ни одна строка не называет будущий год, при `true` первая строка Прошедших — годы трёх ближайших записей; тесты `ChronicleModelTests`: AC-15.1 («Еврокомиссия против вас: 1 млрд, апелляция, решение ~ май 2014, уплачено 0, гарантия 10 млн в год»), порядок, итоги, спойлеры, интенсивность, стадии, догон, ключи в обоих языках; `CatchUpService` ставит записи флаг при применении догона; раскладка в игре не проверена — TSK-027, подробности — `mod/requests/historical-events.md`, п. 21) |
| TSK-023 Письма к встроенным событиям (Could) | Complete (авто 2026-10-10, кусок 6: `BuiltInEventNews.cs` — единственный патч фичи, Optional-постфикс `CompanyEventHandler.TriggerSolarFoundriesSpinOff` (выделение узнаётся по появившейся компании SolarFoundries, потому что метод игры вызывается и без выделения) и подписка на `LicenseHelper.OnX86LicenseRevoked` (`Attach` на `GameReady` и при включении фичи); решения — чистая `Logic/BuiltInNewsCore` (письма не шлются при загрузке и перемотке, при `historyNotes = false`, повторно; игровая логика не меняется: ни результата, ни аргументов, ни пропуска оригинала); `Logic/BuiltInNews` — история новостей (заголовок, факт, источник); события `hist.news.solarfoundries` («Выделение фабрик AMD», AC-16.1) и `hist.news.x86license` в `events.json`, строки ru/en; тесты `BuiltInNewsTests`; применение патча и приход письма в игре не проверены — TSK-027, подробности — `mod/requests/historical-events.md`, п. 22) |
| TSK-024 Выключение посреди партии и самопроверка | Complete (авто 2026-10-10, кусок 6: `OnDisabled` возвращает цены войн, сбрасывает кэши экспорта и гонок и каналы `market.rating.cpu` / `acquisition.price`, `RemoveAll` нигде не вызывается; `OnEnabled` перестраивает очередь из сохранённого состояния и ничего не создаёт заново; `Logic/HistSelfCheckRules` (чистые правила) + `HistSelfCheck.cs` (сбор фактов, строка `selfcheck OK: …` или построчно): цели в игре, пак и события календаря, 8 типов эффектов, 5 предикатов, каналы `market.rating.cpu` (`PerCpu` / `PerSegment`), `acquisition.price`, `gpu.demand`, календарь ≥ 25, префикс `hist.`, нет патчей на `AddCpu` / `NextMonth` / `DebtLimit`, тексты пака; провал `market.rating.cpu` блокирует категории `export` и `race` на сессию (`HistSettings.Blocks`, `CategoryBlocks`) — их записи получают `Disabled`; тесты `HistSelfCheckTests`: каждый пункт, AC-17.1 и AC-17.2 на настоящей записи `hist.2011.tohoku`, жизненный цикл по исходнику; провайдера `acquisition.price` (TSK-028) ещё нет — пункт «возвращает 1.0» выполняется механизмом ядра (провайдеры неактивной фичи не опрашиваются) и сбросом кэша канала; `ModHooks.Guard` на каждой границе; подробности — `mod/requests/historical-events.md`, п. 23) |
| TSK-025 Строки ru/en | Complete (авто 2026-10-10, кусок 7: строки лежат в `L10n/{ru,en}/historical-events.json` (420 ключей в ru и 417 в en: у русских счётных слов формы one/few/many, у английских one/other), в этом куске добавлена причина `hist.mod.acqAntitrust`; тест `HistStringsTests`: все ключи таблицы design (пример `hist.fx.fine` в коде называется `hist.fx.case.fine`), все ключи `calendar.json` и `events.json`, все ключи в коде и все семейства ключей, которые код строит по префиксу (месяцы, сегменты, стадии дел, виды брака, гонки, споры, параметры, регуляторы дел), есть в обоих языках, начинаются с `hist.` и проходят `ModIds.IsL10nKeyAllowed`; ru и en совпадают по ключам и по `{n}`, аргументы только позиционные и не выше `{7}`, каждая строка форматируется восемью аргументами, русский не ставит существительное во множественном числе после числа; деньги пишет только `StringFormatter.FloatToMoney` (`HistText.Money`, три ядра получают его в `HistServices`), показатель Ips — `FloatToIps`, даты — `DateToString` (только `ChronicleWorld`), валютных знаков и своих числовых форматов в литералах нет; реальные названия компаний стоят только в заголовках и справках истории и в четырёх текстах, которые называют настоящий повод события (список в тесте), везде остальное — имена из игры через `{0}`/`{1}`) |
| TSK-026 Симуляция календаря | Complete (авто 2026-10-10, кусок 7: `SyntheticWorlds.cs` — генератор миров 1975–2030 (`WorldProfile`: ведущая компания от 0,15 до 0,70 рынка, гиганты 1990-х / 2000-х / всей эпохи в трети партий, игрок от 4 до 21 %, наличие Intel, AMD, Toshiba, Cyrix, литейщика, банкротства; `SyntheticWorld` — `IWorldView` из журналов продаж 16 компаний и несколько сотен CPU, показывает только прошедшее; миры `Typical`, `Average`, `Dominating`, `Weak`), `ShippedContent` (настоящие `calendar.json` и `events.json`) и `CalendarRun` (настоящие `SchedulerCore`, `RoleResolver`, `ConditionEvaluator` с предикатами ядра и `hist.*`, `DispatcherCore`, `MarketShareCache` и поддельный движок, который помнит открытые дела и порог экспорта); `CalendarSimulationTests` на 100 мирах: по умолчанию срабатывает 29–35 записей (среднее 31,7; требование 27–35), с `disaster` 31–38 (30–38), в среднем мире 9 решений игрока и 9 с `disaster` (7–13 и 8–14; в среднем по сотне 8,7), самое малое расстояние между `major` 124 дня на 2524 парах (≥ 45), мир «доминирование» — игрок цель трёх дел из трёх (ЕС, ФТС, JFTC), мир «слабый игрок» — ни одного, один сид — одна игра событие в событие (отпечаток всех запусков), ни одной ошибки и предупреждения в журнале, у каждой записи конечный статус; день без дел стоит меньше 0,05 мс (NFR-1); числа требования верны для партии без `gpu-product-line`: с ним две криптозаписи добавляют ещё две записи к счёту (отдельный тест); симуляция нашла дефект роли `player` (см. `mod/requests/historical-events.md`, п. 28) и он исправлен; тесты лежат в `mod/tests/PTMod.Tests/Features/HistoricalEvents/`) |
| TSK-027 Ручная проверка в игре (владелец) | Pending |
| TSK-028 Антимонопольная надбавка к цене поглощений | Complete (авто 2026-10-10, кусок 7: `Logic/AcquisitionAntitrustMath.cs` — `AcquisitionAntitrustMath` (формула 11: `S' = доля игрока + доля цели`, `1 + slope × max(0, S' − softShare)`, × `caseSurcharge` при открытом деле против игрока, не выше `maxMultiplier` и 3,0, никогда не ниже 1,0; причина `hist.mod.acqAntitrust` с долей в процентах) и `AcquisitionAntitrustCore` (ответ провайдера: 1,0 без фичи, без категории `antitrust`, для вопроса об игроке, без компании и о ключе, который не компания; причина только при множителе ≠ 1; доли из `MarketShareCache` за `shareMonths`, дело из `CaseCore.HasOpenCase(player)`); `AcquisitionAntitrust.cs` — провайдер: один `ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.AcquisitionPrice, …)` в `RegisterServices`, числа из `params.json` (`Params.AcquisitionAntitrust`), вне запущенной игры отвечает 1,0 и мир не читает; `Invalidate("acquisition.price")` после месячной пересборки долей, при готовности игры, при смене категории `antitrust` и при открытии и закрытии дела (в `CaseCore`, TSK-009); заодно `Invalidate("market.rating.cpu")` при смене категорий `export` и `race`; тесты `AcquisitionAntitrustMathTests`: таблица формулы 11, AC-19.1–19.3 на настоящих кэше долей и деле, выключатели, причина только при ≠ 1, диапазон [1,00; 3,00], параметры и переопределения `params.json`, цена ответа (20 000 ответов < 0,5 с без пересборки кэша), проводка в `HistoricalEventsFeature` (один вызов регистрации, порядок «пересборка → Invalidate»); чтение канала и строка «Антимонопольная надбавка» в окне сделки в игре не проверены — TSK-027, сценарий 11) |

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
