# Tasks: rival-personalities

## Document Information

- **Feature Name**: rival-personalities — характеры соперников и реакции на игрока
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/rival-personalities/requirements.md`
  - Design: `.kiro/specs/rival-personalities/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`
  - Задачи ядра: `.kiro/specs/mod-framework/tasks.md` (ссылки вида `mod-framework/TSK-0xx`)

## Implementation Overview

Порядок такой: сначала чистая логика и контент (тестируются без игры), потом патчи поведения ИИ, потом отношение и триггеры, потом реакции через `event-system`, в конце UI, API и надёжность. Каждая задача — 2–4 часа, оставляет сборку зелёной и не ломает игру при выключенной фиче. Задачи TSK-028…TSK-030 добавлены в v0.2 (видимость характеров, туманность, осторожность ИИ), их номера идут после TSK-027, чтобы не менять существующую нумерацию.

### Implementation Strategy
- Чистые классы (`*Math`, `ShareCalculator`, `CharacterVisibility`, `IntelFog`, `CautionMath`, валидатор) — без `UnityEngine`, в `mod/src/PTMod/Features/RivalPersonalities/Logic/`. Тесты — мини-раннер .NET Framework 4.8 в `mod/tests/PTMod.Tests/Features/RivalPersonalities/` (`python mod/build.py --tests`, атрибуты `[Test]`, `[Covers(typeof(X))]`); каждый публичный тип `Logic` указан в `[Covers]`. Код `Logic` — общее подмножество API .NET Framework 4.8 и mscorlib Unity (без `init`, `record`, `Index`, `Range`).
- Патчи — только через `ModHooks`, тело под `ModHooks.Guard`. Каждую цель задача добавляет в самопроверку. **Патчей на `Company.AddCpu` и `AICompany.AddCpu` нет** (ядро отклонит): выпуск CPU берётся из `ModEvents.CpuReleased`. Своего классификатора сегмента и счётчиков ошибок нет.
- Числа баланса — только в `mod/src/PTMod/Content/rival-personalities/*.json`.
- До готовности `event-system` реакции отключены флагом (`GateResult.NoEventSystem`). Поведение, отношение и панель работают раньше.
- Сборка — `python mod/build.py --game-dir <папка игры>` (Roslyn `csc`, без .NET SDK); отладочные флаги `debug.*` и отладочная страница хаба — только в отладочной сборке.

### Development Approach
- **Testing Strategy**: TDD для чистой логики; самопроверка целей патчей на старте; ручные сценарии владельца (design.md, Testing Strategy).
- **Integration Strategy**: фича регистрируется в ядре как `[ModFeature] IModFeature`. Зависимость от `event-system` мягкая: проверка доступности при старте. Связи с `design-flaw-recalls`, `rival-acquisitions`, `press-reviews` — мягкие (подключаются на `ModTicks.GameReady` и при `ModFeatures.StateChanged`).
- **Deployment Strategy**: `install.py --with-mods` (mod-framework). Фича по умолчанию включена в хабе, отладочный флаг выключен.

## Фаза 1 — Каркас, контент, чистая логика

- [x] **TSK-001**: Скелет фичи и секция сохранения (авто 2026-10-10)
  - **Requirement**: FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/RivalPersonalitiesFeature.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/State/RivalState.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/State/RivalSaveAdapter.cs` (`IModSaveParticipant`)
  - **Acceptance**:
    - `[ModFeature] sealed class RivalPersonalitiesFeature : IModFeature`: `Id` = `rival-personalities`, `NameKey`/`DescriptionKey` (`rival.feature.name`/`rival.feature.desc`), `DependsOn` пуст (мягкие связи не включаются), `EnabledByDefault` и `SupportsHotToggle` = true. Всё остальное идёт через `FeatureContext` (`ctx.Patch`, `ctx.OnTick`, `ctx.SaveParticipant`, `ctx.Param`, `ctx.HubPage`); прямых подписок на `DateController`, `Player.OnNewGame` и `SaveHandler` нет.
    - Фича видна в хабе «Моды» (вкл/выкл, `ctx.Param`): `randomCharacters` (по умолчанию выкл.), `hiddenCharacters` (вкл.), `intelFog` (вкл.); `debug.forceReactions` — только в отладочной сборке.
    - Участник сохранения: `SectionId` = `rival-personalities`, `SchemaVersion = 1`; `Write()` не содержит `_schema` (его пишет ядро); поля версии в `RivalState` нет.
    - Загрузка сейва без секции даёт состояние по умолчанию без ошибок.
  - **Depends**: mod-framework/TSK-022 (`FeatureContext`, `ModFeatures`), TSK-025 (`ModSave`), TSK-021 (`ModSettings`), TSK-067 (мини-раннер)

- [x] **TSK-002**: DTO контента и валидатор (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/Content/ArchetypeModels.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/Content/ArchetypeCatalog.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/ContentValidationTests.cs`
  - **Acceptance**:
    - `ArchetypeCatalog.Validate` пропускает запись без `id`, клипует `approvalBias`/`researchFundingMult`/`launchPriceMult` к диапазонам 0.8–1.3 / 0.7–1.3 / 0.85–1.15 с предупреждением, отбрасывает неизвестные типы реакций и триггеров, принимает `negotiationDiscount` (bool) и `flawResponseBias` (каждый 0.5–2.0).
    - Назначение на несуществующий архетип даёт `conservative`; запись назначения без `key` — предупреждение.
    - Все тесты зелёные.
  - **Depends**: mod-framework/TSK-015 (`ModContent`), TSK-067

- [x] **TSK-003**: Контент: архетипы, назначения, конфиг, события (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2, FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Content/rival-personalities/archetypes.json` (7 архетипов)
    - `mod/src/PTMod/Content/rival-personalities/assignments.json` (13 компаний, ключ `uniqueId` + `key`, без отображаемых имён)
    - `mod/src/PTMod/Content/rival-personalities/config.json` (включая `mailWaitDays`, `priceCut.externalMaxDays`, `flawCaution`, `intelFog`, `attitude.revealAttitude`/`discount*`)
    - `mod/src/PTMod/Content/rival-personalities/events.json` (5 событий `rival.*`)
  - **Acceptance**:
    - Значения совпадают с таблицами design.md; `segmentPriceBand` в `config.json` отсутствует (принадлежит ядру).
    - Тест загружает встроенный контент и получает 7 архетипов, 13 назначений (`uniqueId` 0–12), 5 событий `rival.*` (ID по правилу `<prefix>.<name>`) без ошибок валидации.
  - **Depends**: TSK-002

- [x] **TSK-004**: Чистая логика отношения (авто 2026-10-10)
  - **Requirement**: FR-4
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/AttitudeMath.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/AttitudeMathTests.cs`
  - **Acceptance**: тесты покрывают:
    - клип ±100;
    - дрейф 10 % (−40 → −36);
    - границы уровней −60/−20/20/60;
    - `ReactionMultiplier` (−100 → 2.0, 0 → 1.0, +100 → 0.25);
    - `InterestBonus` (40 → 8, −37 → −7);
    - `DiscountPoints` (40 → 4, 100 → 10, −30 → 0).
  - **Depends**: mod-framework/TSK-067

- [x] **TSK-005**: Чистая логика шансов и лимитов реакций (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/ReactionMath.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/ReactionMathTests.cs`
  - **Acceptance**:
    - `Chance(0.40, −50, сложность 2)` = 0.60; клип 0.9; множители сложности [0.4…1.6].
    - Тест на каждый `GateResult` (`GracePeriod`, `Cooldown`, `ConcurrentCap`, `YearlyCap`, `AttitudeTooHigh`, `SameTypeActive`, `Bankrupt`, `Dormant`, `NoEventSystem`), включая `anyAttitude`.
  - **Depends**: TSK-004

- [x] **TSK-006**: Расчёт долей рынка (классификатор сегмента — в ядре) (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/ShareCalculator.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/ShareCalculatorTests.cs`
  - **Acceptance**:
    - Пустой месяц даёт доли 0 без деления на ноль.
    - Сумма долей по сегменту = 1 ± 1e-5; индекс сегмента массива = `(int)Segment`.
    - Тест архитектуры: в `Features/RivalPersonalities` нет типа-классификатора сегмента по цене (`SegmentClassifier` удалён; используется `ModSegments.Classify`, проверка 312/311/187/186/99/98 живёт в `SegmentRuleTests` ядра).
  - **Depends**: mod-framework/TSK-067

## Фаза 2 — Поведение ИИ

- [x] **TSK-007**: Применение и откат характера (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/PersonalityRegistry.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/SpawnPatches.cs` (постфиксы `AICompany.Initialize`, `AIBehaviourController.ReformCyrux`)
  - **Acceptance**:
    - Снимок префабных `designFocus`/`powerConsumptionFocus`/`simulationPriority` берётся рефлексией из `AIBehaviourController.creators`.
    - `ChangePriorities` вызывается только при отличии от текущих значений.
    - Фокусы со значениями Inlet/EMD перечисления `CpuDesignFocus` не меняются; литейщики пропускаются.
    - Во время перемотки (`ModTicks.IsFastForwarding`) ничего не назначается и не применяется; `ctx.OnTick(TickPhase.FastForwardEnded, ...)` один раз назначает и применяет характеры всем существующим ИИ, ставит отношение = baseline и `graceStart`.
    - При загрузке (`ModTicks.IsLoading`) применение откладывается до `ModSave.AfterRead`.
    - `Revert` возвращает снимок.
    - Лог: `applied <archetype> to <company>` на каждую компанию (после перемотки, не во время).
  - **Depends**: TSK-003, mod-framework/TSK-023 (`ModHooks`), TSK-024 (`ModTicks`, `FastForwardEnded`), TSK-025 (`ModSave.AfterRead`)

- [x] **TSK-008**: Патчи темпа выпуска и финансирования исследований (авто 2026-10-10)
  - **Requirement**: FR-3, FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/BehaviourModifiers.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/CreationAndResearchPatches.cs` (постфиксы `AIBehaviourCreation.OnTick`, `AIBehaviourResearch.GetMaxFunding`)
  - **Acceptance**:
    - `OnTick`: NaN, ≤ 0 и < 1.05 не меняются; иначе × множитель, итог ≤ 10.
    - `GetMaxFunding`: × множитель, клип 0.01–5.0.
    - Бусты `ReactionBoost` по сегменту (`Segment`) учитываются.
    - Во время перемотки и до применения характера множитель = 1.
    - Обе цели добавлены в самопроверку.
  - **Depends**: TSK-007

- [x] **TSK-009**: Патч цен ИИ и PriceBook (авто 2026-10-10)
  - **Requirement**: FR-3, FR-7, FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/MarketPatches.cs` (prefix/postfix `AIBehaviourMarket.OnTick`)
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/PriceBook.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/PriceCutMath.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/PriceCutMathTests.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/PriceBookTests.cs`
  - **Acceptance**:
    - Новый CPU ИИ получает `TargetPrice = Price = max(TP × launchPriceMult, RealUnitCost × 1.05)`.
    - `PriceCutMath`: 239/120/0.15/0.08 → 203.15.
    - `Clamp` удерживает `TargetPrice` не выше цены удара.
    - `Restore` возвращает исходную цену; запись для отсутствующего CPU удаляется.
    - `ApplyCut(reactionId, rival, Segment, depth, floorMargin)` возвращает число затронутых CPU, 0 для банкрота и спящей компании; записи внешних вызывающих (id не `r-<n>`) снимает `ExpireExternal` через `priceCut.externalMaxDays` (400).
    - В дневном пути нет аллокаций (пул списков).
  - **Depends**: TSK-007

## Фаза 3 — Отношение и триггеры

- [x] **TSK-010**: Учёт отношения (авто 2026-10-10)
  - **Requirement**: FR-4, FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/Attitude/AttitudeLedger.cs`
  - **Acceptance**:
    - Начальное значение = baseline архетипа.
    - `Change` применяет sensitivity и клип, хранит 20 последних записей, поднимает `LevelChanged` и `ModNotify` при смене уровня.
    - `MonthlyDrift` вызывается в фазе `TickPhase.LateMonthPassed`.
    - Отношение банкротов заморожено.
    - Значения переживают сохранение и загрузку.
  - **Depends**: TSK-001, TSK-004, mod-framework/TSK-033 (`ModNotify`)

- [x] **TSK-011**: Триггеры событий игрока (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Triggers/PlayerActionMonitor.cs` (включая подписку на `ModEvents.CpuReleased` в `Register`)
    - `mod/src/PTMod/Features/RivalPersonalities/Triggers/TriggerPatches.cs`. Постфиксы:
      - `Company.AddTechnology`;
      - `ContractManager.PromoteContractToActive`;
      - `BusinessContractManager.BreakContract`.
    - Подписка `BusinessContractManager.OnPlayerSignedContract` (переустанавливается на каждом `ModTicks.GameReady`, без дублей).
  - **Acceptance**:
    - Ни один триггер не фиксируется при `ModTicks.IsLoading`, `ModTicks.IsFastForwarding` и `duringFastForward = true`; патчей на `Company.AddCpu`/`AICompany.AddCpu` нет.
    - `StrongLaunch` — по порогу 1.15; сегмент — `ModSegments.Classify(cpu)`, при `Segment.Any` триггера нет и есть предупреждение; варианты (`isVariant`) и `CpuTransferred` игнорируются.
    - `TechLead` — только Lithography/Frequency/Multicore и только для соперников без технологии.
    - `OemLoss` — только для соперников из `OffersIDs`.
    - Контракт: +15 при подписании, −25 при разрыве игроком.
    - Повторная подписка после перезагрузки сцены не дублирует обработчик (проверка счётчиком в логе).
  - **Depends**: TSK-006, TSK-010, mod-framework/TSK-055 (`ModSegments`), TSK-056 (`ModEvents`), TSK-024 (`GameReady`)

- [x] **TSK-012**: Месячная оценка: доли, демпинг, контракты (авто 2026-10-10)
  - **Requirement**: FR-5, FR-4
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Triggers/MonthlyEvaluator.cs`
  - **Acceptance**:
    - Снимок долей по `SoldByHighEnd/MidRange/LowEnd/Industries[Count − 2]` сохраняется в состоянии.
    - `ShareGain` срабатывает при +3 п. п. игрока и падении доли соперника.
    - `PriceUndercut` — при `Ips ≥ 0.9×` и `Price ≤ 0.85×`, кулдаун 90 дней на пару.
    - +2 в месяц за активный контракт.
    - Месячный проход вызывает `PriceBook.ExpireExternal` и чистку просроченной осторожности.
    - Время выполнения ≤ 3 мс на 1000 CPU (замер в логе отладки).
  - **Depends**: TSK-011

## Фаза 4 — Реакции

- [x] **TSK-013**: Планировщик реакций и письма разведки (авто 2026-10-10)
  - **Requirement**: FR-6, FR-7, FR-10, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/ReactionPlanner.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/IntelMail.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/UI/DebugReactionPage.cs` (только отладочная сборка)
  - **Acceptance**:
    - Броски идут по таблице архетипа через `ModRandom`; первая успешная реакция планируется.
    - Применяются все лимиты (льготный период, кулдаун, 2 одновременно, 4 в год, ворота отношения, тот же тип уже действует).
    - Письмо разведки: `ModMail.Send`, приоритет `Normal`, `Tag` = `rival:intel:<reactionId>`, действие `openIntel` (`ModMailAction`, `ModMail.RegisterActionHandler`), диапазон недель; возвращённый ID хранится в `PendingReaction.mailId` (блоков ID писем нет).
    - `fireOn = DeliveredOn + telegraphDays` (14–45) задаёт обработчик `ModMail.Delivered`; письмо не доставлено за 30 дней — реакция отменена, запись `cancelled`.
    - `pending` (включая `mailId`, `telegraphDays`) и счётчики переживают сохранение.
    - Отладочный флаг `debug.forceReactions` даёт шанс 1.0, телеграф 1 день, без льготного периода; отладочная страница хаба («Компания», «Тип реакции», «Запланировать») ставит выбранную реакцию.
  - **Depends**: TSK-005, TSK-010, mod-framework/TSK-061, TSK-062 (`ModMail`: приоритеты, `Tag`, `Delivered`, действия), TSK-025 (`ModRandom`)

- [x] **TSK-014**: Исполнитель реакций: ценовой удар и ответный запуск (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/ReactionExecutor.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalPriceCutHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalCounterLaunchHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalAttitudeHandler.cs`
  - **Acceptance**:
    - Пак `rival-personalities` зарегистрирован (`EventRegistry.RegisterPack`).
    - `EventBus.Fire("rival.priceCut", { CompanyKey = "ai:N", Args = [...] })` снижает цены через `PriceBook`. Через 180 дней `ITimedEffectHandler.Remove` возвращает их.
    - `rival.counterLaunch` добавляет буст 1.35/1.25 на 150 дней.
    - Начало и конец — `ModNotify` и запись в историю; первое начало реакции соперника вызывает `PersonalityRegistry.Reveal`.
    - `ITimedEffectHandler.Restore` после загрузки восстанавливает удержание цен и бусты.
    - Обработчики зарегистрированы через `EventEffects.Register`.
  - **Depends**: TSK-008, TSK-009, TSK-013, event-system/TSK-002 (реестр паков), TSK-010 (резолвер и жизненный цикл эффектов), TSK-014 (публичный API)

- [ ] **TSK-015**: OEM-натиск, переманивание инженеров и удар по репутации. Частично (авто 2026-10-10): всё сделано, кроме причины `rival.reason.oemPush` у стандартного `ModifierTimed` OEM-натиска; у этого эффекта нет параметра причины (см. `mod/requests/rival-personalities.md`, п. 10.1)
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalPoachCounterOfferHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalResearchBoostHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/PoachMath.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/PoachMathTests.cs`
    - событие `rival.smear` в `events.json` и текст письма `rival.mail.intel.smear`
  - **Acceptance**:
    - `rival.oemPush` (цель `ai:N`) даёт сопернику `contract.signChance` × 1.5 на 365 дней (стандартный `ModifierTimed`, `scope: Target`, причина `rival.reason.oemPush`).
    - `rival.poach` (цель `player`, соперник в `Args`) показывает модальный выбор с таймером 10 дней (письмо `Critical` ведёт `event-system`).
    - «Контрпредложение» списывает `max(250 000 × Inflation, 2 × ExpenseByResearch)` через `Company.SpendMoney(..., ExpenseOrigin.Research)`.
    - «Отпустить» по умолчанию даёт игроку `research.speed` 0.85 на 120 дней (`ModifierTimed`) и сопернику 1.10 на 120 дней (`ModModifiers.Add` в `RivalResearchBoost`, причина `rival.reason.poach`); запись не пересоздаётся и не удаляется при выключении фичи.
    - `rival.smear` (цель `player`) снижает репутацию игрока на 3 стандартным эффектом `Reputation` (`ModReputation.Add`, причина `event:rival.smear`); реакция телеграфируется письмом разведки и подчиняется лимитам FR-6.
    - Тест `PoachMath`: `Reports.Count < 2` → минимум.
  - **Depends**: TSK-014, event-system/TSK-011 (`ModifierTimed`), TSK-012 (эффекты `Reputation`, `Money`), TSK-016, TSK-018 (выбор, таймер, окно), mod-framework/TSK-027 (`ModModifiers`)

- [x] **TSK-016**: Замыкание ответного удара и деградация без event-system (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/CounterLaunchWatcher.cs` (подписка на `ModEvents.CpuReleased`)
  - **Acceptance**:
    - CPU соперника в сегменте активного `CounterLaunch` (событие ядра, не в перемотку; вариант `isVariant` не считается) даёт письмо «Ответный удар» (`Normal`, `Tag` `rival:counter:<reactionId>`) и запись `counterLaunchDone`.
    - Без `event-system` реакции не планируются: одна запись в лог за сессию, остальная фича работает.
  - **Depends**: TSK-014, mod-framework/TSK-056 (`ModEvents`)

- [x] **TSK-028**: Видимость характеров (авто 2026-10-10)
  - **Requirement**: FR-11 (п. 2–4)
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/CharacterVisibility.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/CharacterVisibilityTests.cs`
    - правки `PersonalityRegistry.cs` (`IsRevealed`, `Reveal`) и `RivalState.revealed`
  - **Acceptance**:
    - `IsVisible`: режим «Скрытые» выключен → виден; включён → скрыт без реакции и при отношении < 20; виден после первой начатой реакции или при отношении ≥ 20.
    - `Reveal` сохраняет `uid` в `revealed` (без дублей), показывает `ModNotify` «характер раскрыт» один раз и переживает сохранение.
    - Выключение режима не стирает `revealed`; повторное включение не скрывает раскрытых.
  - **Depends**: TSK-010, TSK-014, mod-framework/TSK-033

## Фаза 5 — Переговоры и интерфейс

- [x] **TSK-017**: Отношение в переговорах по бизнес-контрактам (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Negotiation/NegotiationPatches.cs`. Цели:
      - prefix + finalizer `ContractNegotiationWindow.UpdateScores`;
      - постфиксы `AIMath.GetProviderScores` и `AIMath.GetClientScores`.
    - `mod/src/PTMod/Features/RivalPersonalities/Negotiation/NegotiationScope.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/NegotiationScopeTests.cs`
  - **Acceptance**:
    - Вне scope очки не меняются (ИИ-ИИ сделки ванильные).
    - В scope `Interest` ИИ-стороны += `round(a/5)`; −1000 сохраняется.
    - Для архетипа с `negotiationDiscount` и ИИ-поставщика `Price` += `clamp(round(a/10), 0, 10)`; при отрицательном отношении скидки нет.
    - Scope закрывается даже при исключении (finalizer).
  - **Depends**: TSK-010, mod-framework/TSK-023

- [x] **TSK-018**: Окно «Разведка: соперники» (авто 2026-10-10)
  - **Requirement**: FR-9, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/UI/IntelWindow.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/UI/IntelRowView.cs`
  - **Acceptance**:
    - Кнопка «Соперники» на странице хаба «Моды» (`ctx.HubPage`) и действие «Открыть разведку» в письмах (письмо не закрывается) открывают окно (`ModUI.CreateWindow`).
    - Список компаний: архетип (или «Характер неизвестен» по `CharacterVisibility`), отношение, сегменты, доля, последний CPU.
    - Детали: угрозы с диапазоном недель, активные эффекты с датой окончания, 10 записей истории, вклад в переговоры (подсказка `ModUI.Tooltip`; для литейного партнёра — скидка).
    - Деньги и проекты соперников не показываются.
    - Обновление раз в день и при смене темы; тема применяется.
  - **Depends**: TSK-010, TSK-013, TSK-028, mod-framework/TSK-035, TSK-036 (`ModUI`), TSK-039 (хаб «Моды»), TSK-038 (`ModMailAction`)

- [x] **TSK-029**: Туманность разведки и строка осторожности в панели (авто 2026-10-10)
  - **Requirement**: FR-16, FR-9
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/IntelFog.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/IntelFogTests.cs`
    - правки `IntelWindow.cs` (параметр `intelFog`, строка «Осторожен после брака до …»)
  - **Acceptance**:
    - При включённой туманности доля округлена до 5 п. п. («≈ 15 %» для 17.3 %), `Ips` — до двух значащих цифр («≈ 120» для 123); при выключенной — точные значения.
    - Отношение, угрозы, активные эффекты и история от туманности не зависят.
    - Смена параметра обновляет открытую панель.
    - Строка осторожности показывается только для компании с действующей записью `cautions`.
  - **Depends**: TSK-018

- [x] **TSK-019**: Строки ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4, FR-7, FR-9
  - **Deliverables**:
    - `mod/src/PTMod/L10n/ru/rival-personalities.json` (ключи `rival.*`)
    - `mod/src/PTMod/L10n/en/rival-personalities.json` (ключи `rival.*`)
  - **Acceptance**:
    - Все ключи из design.md и кода есть в обоих файлах (включая `rival.feature.*`, `rival.window.unknownCharacter`, `rival.notify.revealed`, `rival.mail.intel.smear`, `rival.reason.*`).
    - Тест сверяет набор ключей `rival.*` в ru и en и находит ключи, используемые в коде и контенте, но отсутствующие в таблицах; ключи начинаются с `rival.` (`ModIds.IsL10nKeyAllowed`).
  - **Depends**: TSK-003, TSK-013, TSK-018, TSK-028, TSK-029

## Фаза 6 — Расширения

- [x] **TSK-020**: API для других фич и предикаты условий (авто 2026-10-10)
  - **Requirement**: FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Api/IRivalPersonalitiesApi.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Api/RivalPersonalitiesApi.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/RivalConditions.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/FlawResponseBias.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/FlawLink.cs` (регистрация множителей)
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/FlawBiasTests.cs`
  - **Acceptance**:
    - `GetAttitude`/`GetArchetypeId` возвращают `null` для игрока.
    - `SetDormant(true)` снимает модификации поведения, возвращает цены под ударами и отменяет запланированные реакции компании.
    - Предикаты `rival.attitudeAtMost/AtLeast/reactionsExecuted` (параметр `company` = `ai:<UniqueID>` | `anyAI`) зарегистрированы через `ModConditions.Register`.
    - `ApplyPriceCut(reactionId, rival, Segment, depth, floorMargin)` и `RestorePriceCut(reactionId)` работают через `PriceBook`; при выключенной фиче API — null-object (`IsEnabled == false`, `ApplyPriceCut` → 0).
    - `FlawResponseBias : IFlawResponseBias` с `float[] Multipliers(ICompany, bool isPublic)` (порядок Recall, Stepping, Patch, Ignore); `FlawLink` вызывает `FlawRegistry.SetResponseBias(...)` на `GameReady` и при включении `design-flaw-recalls`, `null` — при выключении своей фичи.
  - **Depends**: TSK-009, TSK-010, TSK-013, mod-framework/TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`), design-flaw-recalls/TSK-015 (`IFlawResponseBias`), TSK-020 (`FlawRegistry`) — мягкая зависимость

- [x] **TSK-021**: Случайные назначения характеров (авто 2026-10-10)
  - **Requirement**: FR-11 (п. 1, Could)
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/Behaviour/RandomAssignment.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/RandomAssignmentTests.cs`
  - **Acceptance**:
    - В режиме «Случайные» (`randomCharacters`) назначение выполняется на `TickPhase.FastForwardEnded`: литейщики — `foundry_partner`, остальные архетипы — не более чем у 3 компаний; результат детерминирован `ModRandom` (поток `rival-personalities/assign`) и сохраняется.
    - Смена режима посреди партии назначений не перераспределяет.
  - **Depends**: TSK-007, mod-framework/TSK-025

- [x] **TSK-022**: Предложение партнёрства (авто 2026-10-10)
  - **Requirement**: FR-12
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/PartnershipOffer.cs`
  - **Acceptance**:
    - Литейщик при отношении ≥ +40 без контракта с игроком с шансом 0.10 в месяц шлёт письмо (`Low`, `Tag` `rival:partner:<yyyy-MM>:<uid>`) с действием «Открыть переговоры» (`ModMailAction`, `ContractNegotiationWindow.Open`); не чаще раза в 6 месяцев на литейщика.
  - **Depends**: TSK-012, TSK-013, mod-framework/TSK-062

- [x] **TSK-026**: Решения ИИ в событиях по характеру (авто 2026-10-10)
  - **Requirement**: FR-14
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Events/RivalEventChooser.cs`
    - поле `eventStyle` в `archetypes.json`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/EventChooserTests.cs`
  - **Acceptance**:
    - `EventAi.AddChooser(chooser, 100)` вызывается при регистрации, если `event-system` доступна; `Choose(..., IModRng rng)`.
    - Для игрока и `neutral` — `null` (решает следующий выбирающий или политика по умолчанию).
    - `spend`/`save` умножают `AiWeight` самого дорогого или дешёвого доступного варианта на 1.5 (`ChoiceOption.Cost`, `Available`).
    - Недоступные варианты (`Available = false`) не выбираются.
    - Тест на 10 000 бросков с фиксированным сидом подтверждает сдвиг частот.
  - **Depends**: TSK-003, event-system/TSK-014 (`EventAi`, `IEventAiChooser`), TSK-016 (решения ИИ)

- [x] **TSK-027**: Связь с поглощениями (Could) (авто 2026-10-10)
  - **Requirement**: FR-15
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/AcquisitionLink.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/AcquisitionLinkTests.cs`
  - **Acceptance**:
    - Провайдер канала `acquisition.price` (`1 + max(0, −a)/200`, причина `rival.reason.acquisitionPrice`) регистрируется один раз в `Register` (ленивое связывание, порядок с `rival-acquisitions` не важен) и не пересоздаётся при `OnEnabled`/`OnDisabled`.
    - Подписка на `RivalAcquisitionsApi.DealCompleted` ставится на `GameReady` при активной `rival-acquisitions`: сделка с `DealRecord.type` = `Hostile` даёт −10 × sensitivity остальным соперникам, выведенная или переданная цель (`targetUniqueId`) — `SetDormant(true)`.
    - Без `rival-acquisitions` модуль не подключается.
    - Тесты: a = −60 → 1.3; a = +40 → 1.0.
  - **Depends**: TSK-010, TSK-020, mod-framework/TSK-059 (ленивое связывание провайдеров), rival-acquisitions/TSK-017 (публичный API, `DealCompleted`) — мягкая зависимость

- [x] **TSK-030**: Осторожность ИИ после брака (авто 2026-10-10)
  - **Requirement**: FR-17
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/CautionPatch.cs` (постфикс `AIBehaviourCreation.AcceptableFailureRate`, `Optional`)
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/CautionMath.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/FlawLink.cs` (подписка на `FlawRegistry.OnFlawStateChanged`, расширение TSK-020)
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/CautionMathTests.cs`
  - **Acceptance**:
    - Публичный брак `Major`/`Critical` у ИИ-компании создаёт запись `cautions {uid, flawId, until}` на `flawCaution.months` (24) месяцев один раз на запись брака.
    - Пока запись действует, `AcceptableFailureRate` этой компании × `flawCaution.factor` (0.85); без записи, при выключенной `design-flaw-recalls`, банкротстве, спящем режиме и в перемотке — исходное значение.
    - Просроченные записи удаляются на `LateMonthPassed`; секция хранит ≤ 50 записей.
    - Патч `Optional`: его отсутствие не делает фичу `Unavailable`.
  - **Depends**: TSK-010, TSK-020, mod-framework/TSK-023, design-flaw-recalls/TSK-020 (`FlawRegistry.OnFlawStateChanged`) — мягкая зависимость

## Фаза 7 — Надёжность и проверка

- [x] **TSK-023**: Выключение посреди партии, старые сейвы, миграция (авто 2026-10-10)
  - **Requirement**: FR-10, NFR-3
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/State/RivalStateMigrations.cs`
    - `mod/tests/PTMod.Tests/Features/RivalPersonalities/RivalStateSerializationTests.cs`
  - **Acceptance**:
    - `OnDisabled` возвращает цены и параметры дизайнеров, снимает бусты поведения и `FlawLink`; записи `ModModifiers` не удаляются и не пересоздаются (ядро хранит, но не применяет; срок сохраняется).
    - Повторное включение продолжает состояние.
    - Round-trip секции без потерь; в данных нет поля версии.
    - Клипы и лимиты размеров при чтении.
    - Секция новее мода (`_schema` больше `SchemaVersion`): `Read` получает значения по умолчанию, исходная секция сохраняется ядром.
    - Миграция версий — внутри `Read(section, schema)`.
  - **Depends**: TSK-014, TSK-007, mod-framework/TSK-025

- [x] **TSK-024**: Самопроверка, изоляция ошибок, замеры (авто 2026-10-10)
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Diagnostics/RivalSelfCheck.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Diagnostics/RivalTimings.cs`
  - **Acceptance**:
    - На старте одна строка `[PTMod] [rival-personalities] INFO self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)` или список отсутствующих целей с выключением фичи (патч `AcceptableFailureRate` — `Optional`).
    - В отладочном режиме раз в игровой месяц — среднее время фичи на день.
    - Искусственное исключение в патче не доходит до игры (тест через фейковый патч в отладочной сборке); собственных счётчиков ошибок нет — используются `ModHooks.Guard` и порог ядра.
  - **Depends**: TSK-008, TSK-009, TSK-011, TSK-017, TSK-030, mod-framework/TSK-042 (`ModSelfCheck`)

- [ ] **TSK-025**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-17, NFR-1 … NFR-5
  - **Deliverables**: заполненный протокол `.kiro/specs/rival-personalities/manual-check.md` (дата, версия, результат по каждому из 12 сценариев design.md)
  - **Acceptance**:
    - Сценарии 1–9 и 11–12 пройдены (2–5 и 11–12 — в отладочной сборке).
    - Сценарий 10 (баланс) даёт 3–10 враждебных реакций за 10 лет и не больше 4 в год. Иначе — задача на донастройку `config.json`.
  - **Depends**: TSK-001 … TSK-024, TSK-026, TSK-027, TSK-028, TSK-029, TSK-030

## Dependency graph

```
TSK-001 ─────────────────────────────┐
TSK-002 ──> TSK-003 ──> TSK-007 ──┬──> TSK-008 ──┐
                                  ├──> TSK-009 ──┤──> TSK-020, TSK-030
                                  └──> TSK-021   │
TSK-004 ──> TSK-005 ──┐                          │
   │                  │                          │
   └──> TSK-010 <─────┼── TSK-001                │
          │           │                          │
TSK-006 ──┴─> TSK-011 ──> TSK-012 ──> TSK-022    │
          │                                      │
          └─> TSK-013 <── TSK-005                │
                 │                               │
                 └──> TSK-014 <──────────────────┘
                        ├──> TSK-015
                        ├──> TSK-016
                        ├──> TSK-028 ──> TSK-018
                        └──> TSK-023
TSK-010 ──> TSK-017
TSK-010, TSK-013, TSK-028 ──> TSK-018 ──> TSK-019, TSK-029 (TSK-029 ──> TSK-019)
TSK-009, TSK-010, TSK-013 ──> TSK-020 ──> TSK-027 (Could, + TSK-010), TSK-030 (+ TSK-010)
TSK-003 ──> TSK-026
TSK-008, TSK-009, TSK-011, TSK-017, TSK-030 ──> TSK-024
всё ──> TSK-025 (владелец)

Внешние зависимости (mod-framework): TSK-022/025/021 ──> TSK-001; TSK-023/024/025 ──> TSK-007;
TSK-055, TSK-056 ──> TSK-011, TSK-016; TSK-061, TSK-062 ──> TSK-013, TSK-022; TSK-032, TSK-066 ──> TSK-020; TSK-042 ──> TSK-024.
Мягкие: event-system (TSK-002/010/011/012/014/016) ──> TSK-014, TSK-015, TSK-026; design-flaw-recalls (TSK-015/020) ──> TSK-020, TSK-030; rival-acquisitions (TSK-017) ──> TSK-027.
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Скелет фичи и секция сохранения | Complete (авто 2026-10-10, кусок 1: `RivalPersonalitiesFeature` (`[ModFeature]`, `Id` = `rival-personalities`, `NameKey`/`DescriptionKey` = `rival.feature.name`/`rival.feature.desc`, `DependsOn` пуст, `EnabledByDefault` и `SupportsHotToggle` = true; в `Register` только `RivalSettings.Declare(ctx)` (`ctx.Param`) и `ctx.SaveParticipant`, подписок на `DateController`, `Player.OnNewGame`, `SaveHandler` нет — проверяет тест по исходникам), `Logic/RivalParams` (три `ParamSpec` Bool: `randomCharacters` выкл., `hiddenCharacters` вкл., `intelFog` вкл.; в отладочной сборке четвёртый, `forceReactions`, по умолчанию выкл.; флаг файла настроек `rival-personalities.debug.forceReactions` читает `RivalSettings.ForceReactions` через `ModSettings.Debug`, в релизе всегда false), `State/RivalSaveAdapter` (`IModSaveParticipant`) поверх чистых `Logic/State/RivalSaveSection` + `RivalSaveCodec` + DTO `RivalState` (DTO и кодек лежат в `Logic/State/`, а не в `State/`: сборка тестов берёт только `Logic/`), `SectionId` = `rival-personalities`, `SchemaVersion` = 1, `Write()` без `_schema`, поля версии нет ни в одном DTO секции (тест по рефлексии); сейв без секции, без бокового файла, секция новее мода и новая игра дают состояние по умолчанию без предупреждений (проверено на настоящем `SideFileModel`); `Content/RivalContent` читает контент в `OnEnabled` под `ModHooks.Guard`; строки `rival.feature.*`, `rival.param.*` в ru/en; сборка фичи зелёная, тесты фичи зелёные (`FeatureSkeletonTests` — 10, `RivalSaveSectionTests` — 17); видимость переключателей в хабе «Моды» и горячее выключение/включение — по коду, проверить в игре; **для следующих кусков:** тесты фичи лежат в `mod/tests/PTMod.Tests/Features/RivalPersonalities/` (не в `RivalPersonalities/`: `build_feature.py --tests` отсеивает тесты по префиксу `Features/<Pascal>`), чистые типы — в `Features/RivalPersonalities/Logic/**` (подпапки `Content/`, `State/`), файлы записей контента — в формате ядра `records`, см. `mod/requests/rival-personalities.md`, пп. 4–7) |
| TSK-002 DTO контента и валидатор | Complete (авто 2026-10-10, кусок 1: `Logic/Content/ArchetypeModels` (`Archetype`, `ArchetypeBehaviour`, `ArchetypeAttitude`, `ReactionRule`, `AssignmentRecord`, перечисления `DesignFocusOverride`, `PowerFocusOverride`, `EventStyle`), `Logic/Content/ReactionKinds` (`TriggerType`, `ReactionType`, разбор имён), `Logic/Content/RivalConfig` (типизированный `config.json` со значениями design по умолчанию, `FromJson` + `Normalize` с клипом и сообщением на каждую правку), `Logic/Content/ArchetypeCatalog` (`MapArchetype`/`ValidateArchetype` и `MapAssignment`/`ValidateAssignment` — отображатель и проверка для `ModContent.LoadRecords`, `Build` — перекрёстные проверки, `Validate` — всё сразу на готовых записях, `DefaultArchetypeFor`, `TryGet`) и `ValidationReport`; файлы лежат в `Logic/Content/`, а не в `Content/`: сборка тестов берёт только `Logic/`, игровая часть — `Content/RivalContent`; запись без `id` пропускается, `approvalBias`/`researchFundingMult`/`launchPriceMult` клипуются к 0.8–1.3 / 0.7–1.3 / 0.85–1.15 с предупреждением, неизвестные триггеры и типы реакций (и `Partnership` в таблице) отбрасываются, принимаются `negotiationDiscount` и `flawResponseBias` (каждый 0.5–2.0), назначение на несуществующий архетип даёт `conservative`, запись назначения без `key` — предупреждение; `ContentValidationTests` (41 тест) зелёные) |
| TSK-003 Контент: архетипы, назначения, конфиг, события | Complete (авто 2026-10-10, кусок 1: `Content/rival-personalities/archetypes.json` (7 архетипов по таблице design), `assignments.json` (13 компаний, `uniqueId` 0–12, `key`, `archetype`, без отображаемых имён), `config.json` (все числа design, включая `mailWaitDays`, `priceCut.externalMaxDays`, `flawCaution`, `intelFog`, `attitude.revealAttitude`/`discount*`; `segmentPriceBand` нет, проверяет тест), `events.json` (5 событий `rival.priceCut`/`counterLaunch`/`oemPush`/`poach`/`smear`, ID по правилу `<prefix>.<name>`, у каждого добавлен `cause`; цель — `RandomAI` у ударов по рынку и `Player` у `poach`/`smear`, а не `Specific` без ключа: проверка движка событий отклоняет такую запись, соперника задаёт `EventFireContext.CompanyKey`, см. `mod/requests/rival-personalities.md`, п. 7); файлы записей лежат в формате ядра `{ "schema": 1, "records": [ { "id": ... } ] }` (в design.md массивы названы `archetypes`/`assignments`/`events`, но `ModContent.LoadRecords` читает только `records`), у записи назначения `id` = `key`; строки архетипов, событий, уровней отношения в `L10n/{ru,en}/rival-personalities.json`; тесты сверяют контент с таблицами design построчно) |
| TSK-004 Чистая логика отношения | Complete (авто 2026-10-10, кусок 1: `Logic/AttitudeMath` (`Apply`, `Drift`, `LevelOf`, `ReactionMultiplier`, `InterestBonus`, `DiscountPoints`, `Clamp`) и `AttitudeThresholds` (`Default`, `FromConfig`); клип ±100, дрейф −40 → −36, границы уровней −60 / −20 / +20 / +60 (≤ −60 вражда, ≤ −20 соперничество, < +20 нейтралитет, < +60 уважение), множитель −100 → 2.0, 0 → 1.0, +100 → 0.25, бонус 40 → 8, −37 → −7, скидка 40 → 4, 100 → 10, −30 → 0; NaN нигде не выходит; `AttitudeLevel` лежит в `Api/Contracts`, сборка тестов её не видит — временный двойник `Logic/AttitudeLevelTestShim.cs` под `#if PTMOD_TESTS`, его удаляют вместе с правкой из `mod/requests/rival-personalities.md`, п. 1; `AttitudeMathTests` (20 тестов) зелёные) |
| TSK-005 Шансы и лимиты реакций | Complete (авто 2026-10-10, кусок 1: `Logic/ReactionMath` (`Chance` с перегрузкой для множителя `Poach`, `Gate` с перегрузкой под конфиг), `GateInput`, `GateResult`; `Chance(0.40, −50, сложность 2)` = 0.60, клип 0.9, таблица сложности 0.4 / 0.7 / 1.0 / 1.2 / 1.4 / 1.6 и ближайший край вне 0…5; тест на каждый из десяти `GateResult` (в том числе `anyAttitude`, который снимает только шлюз отношения, и порядок причин: нет `event-system` → банкрот → спящий → льготный период → кулдаун → 2 одновременно → 4 в год → отношение → тот же тип); дружественная реакция лимитов враждебных не проходит; `ReactionMathTests` (24 теста) зелёные) |
| TSK-006 Расчёт долей рынка | Complete (авто 2026-10-10, кусок 1: `Logic/ShareCalculator` (`Shares`, `ShareOf`, `SegmentCount`) и `SalesRow`; пустой месяц даёт доли 0 без деления на ноль, сумма по сегменту = 1 ± 1e-5 (в том числе при 300 компаниях с `int.MaxValue`), индекс массива = `(int)Segment`, строки одной компании складываются, строки без ключа и отрицательные числа не считаются; архитектурный тест по исходникам фичи: нет типа-классификатора сегмента по цене и вызовов `SegmentRule.ByPrice` / бюджетов рынка (используется `ModSegments.Classify` ядра); `ShareCalculatorTests` (11 тестов) зелёные) |
| TSK-007 Применение и откат характера | Complete (авто 2026-10-10, кусок 2: `Behaviour/PersonalityRegistry` поверх чистых `Logic/Behaviour/PersonalityBook` (назначение из `assignments.json` при первой встрече, сохранение в `state.assignments`, `conservative` с предупреждением без записи, отношение = baseline для нового характера, `StartGrace`), `CreatorRecord`/`PriorityMath`/`CreatorPriorities` (снимок `designFocus`/`powerConsumptionFocus`/`simulationPriority` рефлексией из `AIBehaviourController.creators`; `ChangePriorities` только при отличии от текущих; Inlet/EMD сохраняют фокус; изменение игры (`ReformCyrux`) становится новым снимком и не откатывается; `Revert` возвращает снимок) и `PersonalityLifecycle` (порядок моментов сцены); `Behaviour/SpawnPatches` — постфиксы `AICompany.Initialize(int)` и `AIBehaviourController.ReformCyrux(bool)`; в перемотке ничего не назначается и не применяется, `ctx.OnTick(TickPhase.FastForwardEnded)` один раз назначает и применяет характеры всем ИИ, ставит отношение = baseline и `graceStart`; при загрузке применение ждёт `ModSave.AfterRead` (потом `ApplyAll`); `ModTicks.GameReady` — страховка, если момента не было; литейщики не трогаются (только отношение); лог `applied <archetype> to <company> (<момент>)` на компанию; `OnEnabled` в готовой игре применяет всем, `OnDisabled` откатывает дизайнеров; список ИИ — `ModCompanies.All` плюс `DataFinder.FindAllAICompanies` (на `AfterRead` список ядра может быть пуст, см. `mod/requests/rival-personalities.md`); тесты `PersonalityTests` (22); проверка в игре (AC-2.1–2.4, AC-3.3) — за владельцем) |
| TSK-008 Патчи темпа выпуска и финансирования | Complete (авто 2026-10-10, кусок 2: `Behaviour/BehaviourModifiers` поверх чистых `Logic/Behaviour/BehaviourModel` (числа архетипа по `UniqueID`, `ReactionBoost` с `AddBoost`/`RemoveBoost`, бусты одобрения — по сегменту дизайнера и `Segment.Any`, бусты финансирования — по компании) и `Logic/BehaviourMath`; `Behaviour/CreationAndResearchPatches` — постфиксы `AIBehaviourCreation.OnTick` (NaN, ≤ 0 и < 1.05 (`triggerThresholds.minApprovalForBoost`) не меняются, иначе × множитель, итог ≤ 10) и `AIBehaviourResearch.GetMaxFunding` (× множитель, клип 0.01–5.0); в перемотке, до применения характера, для литейщика и при множителе 1 — значение игры; обе цели объявлены через `ctx.Patch`, поэтому их проверяет самопроверка ядра (реестр `ModHooks`), строку фичи в самопроверке добавит TSK-024; дневной путь без аллокаций (тест); тесты `BehaviourModifierTests` (18)) |
| TSK-009 Патч цен ИИ и PriceBook | Complete (авто 2026-10-10, кусок 2: `Logic/PriceCutMath` (`CutPrice`: 239/120/0.15/0.08 → 203.15, не выше цены; `LaunchPrice`: 249 × 0.92 → 229.08, не ниже `RealUnitCost × 1.05`; ids `r-<n>`), `Logic/RivalDates`, чистое ядро книги `Logic/Prices/PriceBookCore<TCpu>` + `IPriceCpuAccess<TCpu>` + `ListPool<T>` и игровая `Behaviour/PriceBook` (записи — `state.priceBook` секции); `ApplyCut(reactionId, rival, Segment, depth, floorMargin)` возвращает число CPU под ударом, 0 для банкрота и спящей компании, не более 20 внешних ударов; `Clamp` держит `TargetPrice` не выше цены удара; `Restore` возвращает исходную цену (два удара на одном CPU кончаются ценой до обоих), запись отсутствующего CPU удаляется; `ReleaseAll` при выключении фичи возвращает цены и сохраняет записи; `ExpireExternal` снимает внешние удары через `priceCut.externalMaxDays` (400) — пока из своего обработчика `LateMonthPassed`, TSK-012 перенесёт вызов в `MonthlyEvaluator`; `Behaviour/MarketPatches` — prefix/postfix `AIBehaviourMarket.OnTick`: новый CPU ИИ получает `TargetPrice = Price = max(TP × launchPriceMult, RealUnitCost × 1.05)`, затем `Clamp`; дневной путь без аллокаций (пул списков, тест); тесты `PriceCutMathTests` (14), `PriceBookTests` (20); проверка в игре (AC-3.1) — за владельцем) |
| TSK-010 Учёт отношения | Complete (авто 2026-10-10, кусок 3: `Logic/Attitude/AttitudeLedger` (чистый: `Get`, `GetLevel`, `LevelOf`, `IsKnown`, `IsFrozen`, `Change`, `MonthlyDrift`, `History`, событие `LevelChanged(uid, from, to)`) поверх `PersonalityBook` и секции сохранения (`state.attitudes`); старт = baseline архетипа (`PersonalityBook.EnsureAssigned` пишет его при первом назначении; запись без значения или с NaN тоже читается как baseline); `Change` = `clamp(a + δ × sensitivity, −100, +100)` (AC-4.1: AMD −15 + (−8 × 1.3) = −25.4, уровень «Соперничество»; AC-4.3: +98 + 15 = +100); в историю идёт реально случившееся изменение (до сотых, причина до 64 знаков, дата игры), хранятся 20 последних (`attitude.historySize`); изменение, ничего не сдвинувшее (край шкалы), принимается (`true`), но в историю не пишется — триггер всё равно дойдёт до планировщика; `false` — для компании без архетипа, банкрота и NaN; `MonthlyDrift` — 10 % пути к baseline (AC-4.2: −40 → −36), в историю не пишется; банкрот заморожен (ни изменения, ни дрейфа); `LevelChanged` поднимается и при `Change`, и при дрейфе, ошибка обработчика не мешает следующим; игровая часть `Attitude/AttitudeNotifier`: `ModNotify.ShowKey("rival.notify.level")` (строка в `L10n/{ru,en}`), строка в журнале, банкротство по `ModCompanies.Resolve`; `MonthlyDrift` вызывает `MonthlyEvaluator` в `TickPhase.LateMonthPassed` (TSK-012); значения и история переживают сохранение: `RivalSaveCodec.Write` → текст JSON → `Read` (тест); `AttitudeLedgerTests` (25 тестов) зелёные. Отличие от списка файлов: класс лежит в `Logic/Attitude/`, а не в `Attitude/`: сборка тестов берёт только `Logic/` (см. `mod/requests/rival-personalities.md`, п. 9.2). Уведомление в игре проверит владелец (TSK-025)) |
| TSK-011 Триггеры событий игрока | Complete (авто 2026-10-10, кусок 3: чистые `Logic/Triggers/TriggerRules` (границы: `StrongLaunch` — `Ips` нового CPU ≥ 1.15 × лучший `Ips` соперника в сегменте, у активного в сегменте соперника без CPU — любой CPU с `Ips`; `PriceUndercut`; `ShareGain`; тип `TechLead`; `IsBlocked(loading, fastForwarding, duringFastForward, gameReady)`), `TriggerRecorder` (дельты из `config.json`: −8, −5, −4, −6, −3, контракты +15 / −25 / +2; причина в истории `StrongLaunch:1`; событие `TriggerRaised(PlayerTrigger)` после изменения отношения; спящий, банкрот и компания без архетипа пропускаются; при `Blocked` ничего не пишется), `TriggerEvaluator` (кто замечает запуск, технологию, выигранный OEM-контракт, контракт), `PlayerTrigger`, `RivalFacts`, `LaunchFacts`, `SingleSubscription<T>`; игровая часть `Triggers/PlayerActionMonitor` (читает игру и передаёт факты; подписка на `ModEvents.CpuReleased` — в `Register` фичи; сегмент — `ModSegments.Classify(cpu)`, при `Segment.Any` триггера нет и есть предупреждение в журнале; варианты и перемотка отбрасываются, `CpuTransferred` не слушается; подписка на `BusinessContractManager.OnPlayerSignedContract` переставляется на каждом `GameReady` и при включении фичи, без дублей, в журнал идёт `active subscriptions: 1, made in total: N`) и `Triggers/TriggerPatches` (три постфикса только на чтение: `Company.AddTechnology` (только игрок), `ContractManager.PromoteContractToActive`, `BusinessContractManager.BreakContract`; сигнатуры и имена параметров проверены рефлексией по `Assembly-CSharp.dll`; патчей на `Company.AddCpu`/`AICompany.AddCpu` нет — тест по исходникам). Ни один триггер не пишется при `ModTicks.IsLoading`, `IsFastForwarding`, `duringFastForward = true` и пока игра не готова (`!ModTicks.IsGameReady`). `TechLead` — только Lithography/Frequency/Multicore (`techLeadTypes` из конфига), только для соперников без технологии (`ResearchSector.IsUnlocked`), один раз на технологию; литейщики не получают `StrongLaunch` и `TechLead`. `OemLoss` — для соперников из `OffersIDs` (по разу), сегмент = `Strategy.TargetMarket` предложенного CPU. `TriggerTests` (36) и `TriggerWiringTests` (8, разбор исходников: цели патчей, подписки, фаза) зелёные. Отличие от списка файлов: чистые части лежат в `Logic/Triggers/`. Ограничение: работу игровой части (патчи, события, подписку на менеджер контрактов) в живой игре проверит владелец (TSK-025)) |
| TSK-012 Месячная оценка | Complete (авто 2026-10-10, кусок 3: чистый `Logic/Triggers/MonthlyEvaluatorCore.Run(MonthlyInput)` и книги `UndercutBook`, `ShareSnapshots`, `CautionCleaner` (`MonthlyBooks.cs`); игровой `Triggers/MonthlyEvaluator` собирает один проход по `CpuDataProvider.GetAllCpus` (продажи прошлого полного месяца — `Sold…[Count − 2]` четырёх сегментов по компаниям, лучший CPU каждого соперника в сегменте, CPU игрока в продаже с сегментом `ModSegments.Classify`, другая сторона каждого активного бизнес-контракта игрока) и вызывает ядро. Порядок: доли → демпинг → контракты → дрейф → уборка, затем `PriceBook.ExpireExternal` (временный обработчик `LateMonthPassed` удалён, вызов теперь в `MonthlyEvaluator`). Снимок долей (`state.shareSnapshot`, `monthIndex` = номер последнего полного месяца) сохраняется в секции и переживает сохранение; снимок не за предыдущий месяц (фича была выключена) не сравнивается, а заменяется. `ShareGain` — доля игрока +3 п. п. и падение доли соперника в том же сегменте (AC-5.4: 10 → 14 %, Intel 60 → 55 % — триггер для Intel), на каждый сегмент отдельно; `PriceUndercut` — `Ips ≥ 0.9×`, цена `≤ 0.85×` лучшего CPU соперника в сегменте, кулдаун 90 дней на пару «соперник — сегмент» (`state.undercutCooldowns`, 1 мая → 30 июля; пара без записанного триггера кулдаун не получает); +2 за каждый активный контракт (чувствительность архетипа учитывается); чистка: просроченные кулдауны и осторожность (`state.cautions`, пока `today < until` она действует; запись с нечитаемой датой удаляется). Шаг, упавший с исключением, пишется в журнал и не мешает остальным. `MonthlyEvaluatorTests` (30, в том числе 1000 строк продаж < 250 мс) зелёные. Замер игровой части («≤ 3 мс на 1000 CPU») пишется в журнал отладки строкой `monthly: N CPU(s) in X ms …` (при `ModSettings.VerboseLog`); сам замер в живой игре сделает владелец (TSK-025)) |
| TSK-013 Планировщик и письма разведки | Complete (авто 2026-10-10, кусок 4: чистое ядро `Logic/Reactions/ReactionPlannerCore` (порты `IPlannerEnvironment`, `IIntelOutput`, `IReactionLauncher`) и `ReactionBook` (списки `pending`/`active`, счётчики лимитов, история в секции) плюс игровые `Reactions/ReactionPlanner` (читает игру, поток `ModRandom.Stream("rival-personalities")`, `EventSystemApi.IsActive`) и `Reactions/IntelMail` (`ModMail.Send`, `Normal`, `Tag` `rival:intel:<id>`, действие `openIntel`, `Repeatable`, обработчик `ModMail.RegisterActionHandler`). Планировщик слушает `TriggerRecorder.TriggerRaised`. Порядок: строки таблицы архетипа подряд; строка пробуется, только если возможна (есть сегмент, для `PriceCut` есть CPU на продаже) и проходит лимиты `ReactionMath.Gate` (льготные 180 дней, кулдаун архетипа, 2 одновременно, 4 в год, ворота отношения −20 кроме `anyAttitude`, тот же тип уже на ходу); лимит, общий для всех строк, прекращает поиск, лимит одной строки (отношение, тот же тип) переходит к следующей; бросок `ModRandom` одна попытка на строку, первая удача планируется, остальные не бросаются (AC-6.3: 0.40 × 1.5 × 1.0 = 0.60, сложность и потолок 0.9; `Poach` × 1.5 при урезанном финансировании исследований). Реакция получает id `r-<n>`, срок телеграфа 14–45 дней бросается сразу, письмо уходит сразу, `mailId` хранится в `PendingReaction`, в письме диапазон недель (AC-7.1: 2–7); дату исполнения ставит `ModMail.Delivered`: `fireOn` = день доставки + телеграф (AC-7.6), пропущенную доставку подхватывает дневной тик через `ModMail.Get/Find`; письмо не доставлено за 30 дней — реакция отменена (запись `cancelled`, строка в логе), реакция, просроченная на 30 дней (event-system был выключен), тоже. Письмо не принято `ModMail` — реакция не планируется. Банкрот и спящий: реакции отменяются (`CancelFor`, `CancelBankrupt`). Без event-system реакции не планируются, в лог одна строка за сессию `reactions disabled: event-system unavailable` (AC-7.4). `forceReactions`: шанс 1.0, телеграф 1 день, письмо «1 нед.», льготного периода нет (остальные лимиты на месте); отладочная страница `UI/DebugReactionPage` (только `PTMOD_DEBUG`, «Компания», «Тип реакции», «Запланировать» → `PlanManual` без броска и без лимитов, кроме event-system/банкрот/спящий/тот же тип) зарегистрирована через `ctx.HubPage`; TSK-018 заменит страницу своей и вызовет `DebugReactionPage.Build` внутри неё. `pending` с `mailId`, `telegraphDays` и счётчики переживают сохранение (тест через `RivalSaveCodec`). Дневной тик `TickPhase.DayPassed`, `YearPassed` сбрасывает годовой счётчик. Тесты `ReactionPlannerTests` (37), `ReactionExecutorTests` (в т. ч. `ReactionBook`), `ReactionHelpersTests`; `ReactionWiringTests` читает исходники. Отличия от списка: чистые типы лежат в `Logic/Reactions/`, а не в `Reactions/` (сборка тестов берёт только `Logic/`); кулдаун считается от исполнения реакции, а у соперника с реакцией «на ходу» кулдаун уже идёт (у соперника одна реакция за раз)) |
| TSK-014 Ценовой удар и ответный запуск | Complete (авто 2026-10-10, кусок 4: `Logic/Reactions/ReactionExecutorCore` (порт `IReactionRuntime`) и игровой `Reactions/ReactionExecutor`. Пак `rival-personalities` зарегистрирован в `Register` (`EventRegistry.RegisterPack("rival-personalities", "rival-personalities", { SettingsFeatureId = FeatureId })`), пять типов эффектов — `EventEffects.Register`: `RivalPriceCut`, `RivalCounterLaunch`, `RivalResearchBoost`, `RivalPoachCounterOffer`, `RivalAttitude`. `Launch`: запись в `active` создаётся до `EventBus.Fire`, чтобы обработчики могли отчитаться; событие `rival.<тип>` с `CompanyKey` `ai:N` (удар, ответный запуск, OEM) или `player` (`Poach`, `Smear`) и `Args` `[имя, сегмент или цена, reactionId, ключ соперника]` (у `Poach` на месте сегмента цена контрпредложения — для текста события), `IgnoreConditions`; счётчики лимитов (год, кулдаун соперника) двигаются при `Fired`/`Scheduled`, `Disabled` — повтор завтра, остальное — отмена. `RivalPriceCutHandler`: глубина и пол из архетипа и `config.json` (`ReactionParams`, значения фиксируются в `Resolve`: `value` = глубина, `curve[0]` = пол), `Apply` → `PriceBook.ApplyCut`, `Remove` (через 180 дней) → `PriceBook.Restore`, `Restore` после загрузки только восстанавливает запись (цены держит ежедневный `Clamp`). `RivalCounterLaunchHandler`: буст 1.35/1.25 на 150 дней через `BehaviourModifiers.AddBoost/RemoveBoost`, `Restore` возвращает буст. `RivalAttitudeHandler`: `params.delta` (−100…100, проверяется в `Validate`) → `AttitudeLedger.Change`, причина `event:<id>`. Данные (id реакции, сегмент, ключ и имя соперника) обработчики кладут в `textArgs` через `Describe`, потому что движок перезаписывает `textArgs` выводом `Describe` (`EffectArgs`). Начало (`OnStarted` из `Apply` обработчика, а для `OemPush` и `Smear` — сразу после `Fire`) и конец (`OnEnded` из `Remove`, страховка `ExpireOverdue`) — `ModNotify` + запись `started`/`ended` в истории; первое начало реакции соперника вызывает `PersonalityRegistry.Reveal`; повторный `OnStarted` ничего не объявляет второй раз; событие `ReactionExecutor.Started(uid, тип)` готово для `ReactionStarted` API (TSK-020). Пока пак заморожен вместе с выключенной фичей, `Remove` не снимает запись (цены вернул `OnDisabled`), бусты `OnDisabled` очищает, `Restore` возвращает. `ReactionExecutorTests` (26) зелёные. Живую проверку цепочки с движком событий (его исполнитель эффектов ещё дописывается) сделает владелец (TSK-025))) |
| TSK-015 OEM-натиск, переманивание, удар по репутации | Partial (авто 2026-10-10, кусок 4: `Logic/PoachMath` (`CounterOfferCost` = `max(250 000 × Inflation, 2 × ExpenseByResearch[Count − 2])`, при `Reports.Count < 2` только минимум; числа из `config.json`; NaN, отрицательные и бесконечности не дают бессмысленной цены), `Reactions/Effects/RivalPoachCounterOfferHandler` (цена фиксируется в `Resolve` как `−value`, `Apply` → `Company.SpendMoney(..., ExpenseOrigin.Research)` через `ctx.Game.Spend`, единственная трата фичи) и `RivalResearchBoostHandler` (`ModModifiers.Add` на `research.speed` соперника, `ModifierScope.Company`, ×1.10 на 120 дней из архетипа и конфига, причина `rival.reason.poach`; запись создаётся один раз и нигде в коде не удаляется — проверяет тест), `events.json` уже содержал `rival.oemPush` (`ModifierTimed` `contract.signChance` ×1.5, `scope: Target`, 365 дней), `rival.poach` (таймер 10 дней, по умолчанию `letGo`: `ModifierTimed` `research.speed` ×0.85 на 120 дней + `RivalResearchBoost`) и `rival.smear` (`Reputation` −3); добавлены письмо разведки `rival.mail.intel.smear` (и остальные `rival.mail.intel.*`), уведомления и строки эффектов в ru/en; текст кнопки «Контрпредложение» больше не содержит `{0}` (кнопка письма не получает аргументов; цена — в тексте события, `{1}`, и в строке эффекта). `Smear` телеграфируется письмом и подчиняется лимитам. `PoachMathTests` (6) и `ReactionWiringTests` (в т. ч. числа стандартных эффектов в `events.json`) зелёные. **Не сделано:** причина `rival.reason.oemPush` для OEM-натиска — у стандартного `ModifierTimed` нет параметра причины (движок пишет в `Explain` название события); строки `rival.reason.oemPush` и `rival.reason.poachLoss` есть в ru/en, подключить их можно только правкой event-system (`mod/requests/rival-personalities.md`, п. 10.1))) |
| TSK-016 Замыкание ответного удара, деградация | Complete (авто 2026-10-10, кусок 4: чистые `Logic/Reactions/CounterLaunchRules` (`IsRelevantRelease`: CPU соперника, не вариант, не перемотка, не загрузка; `FindOpen`: активный `CounterLaunch` соперника в сегменте, который уже начался и ещё не отвечен; `MarkDone` пишет `counterLaunchDone`) и игровой `Reactions/CounterLaunchWatcher` (разбирает то же событие `ModEvents.CpuReleased`, на которое фича подписана один раз в `Register`; сегмент CPU соперника — `Strategy.TargetMarket`; письмо «Ответный удар» `Normal`, `Tag` `rival:counter:<reactionId>`, действие «Открыть разведку»; `CpuTransferred` не слушается). Один ответ на одну реакцию (следующий `CounterLaunch` того же соперника отвечается снова). Без event-system реакции не планируются (одна строка в лог за сессию, проверено тестом вместе с тем, что отношение, `Change` и остальной код фичи работают). `CounterLaunchTests` (10) зелёные)) |
| TSK-017 Отношение в переговорах | Complete (авто 2026-10-10, кусок 5: чистые `Logic/Negotiation/NegotiationRules` (`Bonus`: `Interest` += `round(a/5)` для ИИ-стороны в любой роли, `Price` += `clamp(round(a/10), 0, 10)` только для ИИ-поставщика с архетипом `negotiationDiscount` и только при положительном отношении; делители и предел из `attitude.*` конфига через `NegotiationNumbers`; `InterestAfter`/`Apply` не трогают вето `Interest = −1000`), `NegotiationBonus` и `NegotiationScopeCore` (`Enter` открывает область, только когда одна сторона игрок, другая ИИ-компания; ИИ-ИИ, игрок-игрок и неизвестная компания оставляют её закрытой; `Exit`; `Apply` для постфиксов; область, которую не закрыли и которая осталась с прошлого кадра, сбрасывается и считается в `StaleDropped`). Игровые `Negotiation/NegotiationScope` (компании → `NegotiationParty`, `Time.frameCount` как метка кадра, спящий соперник и компания без характера бонуса не получают) и `Negotiation/NegotiationPatches`: prefix и finalizer `ContractNegotiationWindow.UpdateScores` (поля `___currentProvider`/`___currentClient`; finalizer закрывает область без проверки состояния фичи и отдаёт `__exception` игре как есть), постфиксы `AIMath.GetProviderScores`/`GetClientScores` (при закрытой области возвращаются первой строкой, так что ИИ-ИИ сделки `AIBehaviourBusiness` дают ванильный результат); объявлены `ctx.Patch(typeof(NegotiationPatches))`. Цели, имена параметров и полей, тип `AIScores` проверены рефлексией по `Assembly-CSharp.dll`. AC-8.1 (TSMC +40: +8 к `Interest`, +4 к `Price`), AC-8.2, AC-8.3 (−30: −6, скидки нет) проверены на боевом контенте. `NegotiationScopeTests` (17) зелёные)) |
| TSK-018 Окно разведки | Complete (авто 2026-10-10, кусок 5: чистый `Logic/Intel/IntelBuilder` строит строки списка (`IntelRow`: имя, архетип или «Характер неизвестен» по `CharacterVisibility` — id и описание скрытого архетипа не покидают построитель, отношение числом с цветом уровня, сегменты, доля, последний CPU, статус «Банкрот» серым, внизу списка) и детали (`IntelDetails`: шапка с характером, «Отношение» с подсказкой и вкладом в переговоры `+8` и скидкой литейного партнёра `до N п. п.` (скидка видна, только когда характер показан), «Рынок», «Угрозы» с диапазоном недель, «Действует» с величиной и датой окончания (NFR-5: цены −15 %, буст ×1.35/×1.25, шансы в тендерах ×1.5), «История» из 10 последних записей отношения и реакций, новые сверху; строка `planned` скрыта, пока письмо не доставлено). Угроза видна после доставки письма (тогда выставлена дата исполнения); показывается диапазон `ThreatMath.WeeksLeft` (сразу после письма 2–7 нед., по мере хода дней сужается, настоящий остаток всегда внутри диапазона), дата не раскрывается. Нет денег и проектов соперников: их нет в `IntelCompanyFacts`, а тест читает исходники окна. Игровая часть: `UI/IntelWindow` (`ModUI.CreateWindow(…, WindowTemplate.Panel, …, 900×560)`, не модальное; два столбца со `ScrollView`; список — `IntelRowView` (кнопка с именем и отношением + приглушённая строка под ней), детали — `ModUI.Info`/`ModUI.Text`, подсказка на числе отношения — `ModUI.Tooltip`; обновление раз в игровой день (`DayPassed.intel`), при смене темы (`ModUI.ThemeChanged`), языка и параметров `intelFog`/`hiddenCharacters` (`ModSettings.Changed`), положение прокрутки сохраняется; окно пересоздаётся после смены сцены), `UI/IntelSource` (одним проходом по CPU: сегменты с CPU на продаже, `MarketDataProvider.MarketShare`, последний выпущенный CPU, `ModReputation.Get` только при активной `press-reviews`), `UI/IntelHubPage` (страница хаба: кнопка «Соперники» закрывает модальный хаб и открывает окно; в отладочной сборке в неё встроена `DebugReactionPage`, вторая регистрация `ctx.HubPage` убрана), `IntelMail.OpenIntel = IntelWindow.Open` (действие «Открыть разведку» в письмах открывает окно на компании письма, письмо не закрывается). `IntelBuilderTests` (32) на боевом контенте и боевых строках ru/en, `IntelWiringTests` (12) читают исходники. **Не проверено в игре:** вёрстка двух столбцов, прокрутка, подсказка, поведение при смене темы (ручная проверка владельцем, TSK-025). Репутация бренда показана числом: словесный уровень требует `PressReviewsApi` (см. `mod/requests/rival-personalities.md`, п. 11.1))) |
| TSK-019 Строки ru/en | Complete (авто 2026-10-10, кусок 5: все ключи таблицы design.md и кода лежат в `L10n/ru/rival-personalities.json` и `L10n/en/rival-personalities.json` (файлы для фичи, а не общие `ru.json`/`en.json`, как у остальных фич): новые — `rival.hub.*`, `rival.window.*` (заголовки, строки списка и деталей, диапазоны угроз, «Действует», «Осторожен после брака до …», подсказки), `rival.history.*` (исходы реакций и причины изменения отношения), `rival.trigger.*`, `rival.window.size.*`, письмо партнёрства `rival.mail.partner.*` и `rival.reason.acquisitionPrice` (их тексты использует TSK-022/027, ключи уже в таблице по design.md). `L10nKeysTests` (9): наборы ключей ru и en совпадают, каждый ключ начинается с `rival.` и проходит `ModIds.IsL10nKeyAllowed`, в обоих текстах одни и те же аргументы и они форматируются, все ключи таблицы design.md на месте, ключи, найденные чтением исходников всей фичи (включая `Logic/` и `Api/`) и четырёх файлов контента, есть в обеих таблицах (ключи, достроенные в коде из имён, — по списку имён; id событий и имена предикатов не считаются), проверка сама ловит пропущенный ключ. Тексты окна ещё проверены прогоном построителя на строках обоих языков: ни одного `[rival.…]`)) |
| TSK-020 API и предикаты | Complete (авто 2026-10-10, кусок 6: реализация `IRivalPersonalitiesApi` — игровой `Integration/RivalApiService` (`ICompany` → `UniqueID` через `AICompany`, игрок, `null` и не-ИИ → неизвестная компания; цены — `PriceBook`, реакции — `ReactionPlanner.CancelFor`, характеры — `PersonalityRegistry.Revert(uid)` и `EnsureAssigned` + `Apply`; `ReactionStarted` из `ReactionExecutor.Started` и из предложения партнёрства) поверх чистого `Logic/Integration/RivalApiCore`: чтения дают `null` для игрока и компании без характера; `ChangeAttitude` — `AttitudeLedger.Change` (чувствительность, клип, история, уведомление), пустая причина пишется как `api`; `SetDormant(true)` пишет запись `dormant`, отменяет запланированные и действующие реакции, возвращает цены под ударами (в том числе внешними) и снимает характер, `false` возвращает характер, повторный вызов ничего не меняет, сбой одного шага не останавливает остальные; `ApplyPriceCut`/`RestorePriceCut` отвергают пустой id, id длиннее 64 символов и id собственных реакций `r-<n>`, сегмент не из четырёх; сбой игры даёт 0. Регистрация `RivalPersonalitiesApi.Register(_api)` в `Register`; папка `Api/` с фасадом и null-object не менялась. Предикаты `rival.attitudeAtMost`, `rival.attitudeAtLeast`, `rival.reactionsExecuted` — чистый `Logic/Integration/RivalPredicates` (`company` = `ai:<UniqueID>` или `anyAI`, без параметра = `anyAI`; у `reactionsExecuted` без `company` — сумма по всем соперникам; опечатка в параметре — ошибка с путём узла) и игровой `Integration/RivalConditions` (`ModConditions.Register`, один раз, имена принимает ядро только для этой фичи). Для `reactionsExecuted` в секцию добавлен накопительный счётчик `counters.executed` (`ReactionBook.ExecutedBy/ExecutedTotal`, в `MarkExecuted`). `FlawResponseBias : IFlawResponseBias` — `Integration/FlawResponseBias` поверх `Logic/Integration/FlawBiasMath` (`flawResponseBias` архетипа в порядке Recall, Stepping, Patch, Ignore, клип 0.5–2.0, NaN = 1; `null` для игрока, неизвестной компании, спящей и архетипа из одних единиц); `Integration/FlawLink` ставит `FlawRegistry.SetResponseBias` на `ModTicks.GameReady`, при включении фичи в готовой игре и при включении `design-flaw-recalls` (`ModFeatures.StateChanged`), снимает (`null`) при выключении любой из двух. Отличия от пути в задаче: файлы лежат в `Integration/` и `Logic/Integration/`, а не в `Api/` (папка `Api/` собирается в соседние фичи отдельно, реализации в ней быть не может), тесты — в `Features/RivalPersonalities/` (`RivalApiTests`, `RivalPredicateTests`, `FlawBiasTests`, `IntegrationWiringTests`). В игре не запускалось: цепочку проверяет владелец, TSK-025.) |
| TSK-021 Случайные назначения характеров | Complete (авто 2026-10-10, кусок 6: чистый `Logic/Behaviour/RandomAssignment.Plan` — литейщики (`AICompany.IsFoundry`) всегда получают `foundry_partner` и больше его не получает никто; остальные берут случайный из шести архетипов, у которых меньше трёх компаний (`MaxPerArchetype` = 3); если мест нет (в моде больше 18 компаний), открываются наименее занятые, так что характер получают все; один бросок потока `rival-personalities/assign` на компанию в порядке `UniqueID` (одинаковое зерно мира — одинаковая раздача), порядок входа не важен, повторы и отрицательные id игнорируются. Сохранение — новые `PersonalityBook.Assign` (только компании без характера, архетип должен быть в каталоге, базовое отношение пишется) и `PersonalityBook.DealRandom` (компании с характером в раздаче не участвуют); вызов — `PersonalityRegistry.DealRandomCharacters` из `StartNewGame` (`TickPhase.FastForwardEnded`, а при сбое сигнала — страховка `GameReady`) только при `randomCharacters`, одной строкой в лог. Смена режима посреди партии ничего не раздаёт заново: раздача идёт только в `StartNewGame`, а `EnsureAssigned` сохранённое не трогает. Отличие: файл `Logic/Behaviour/RandomAssignment.cs`, не `Behaviour/RandomAssignment.cs` (тесты собирают только `Logic/`). Тесты: `RandomAssignmentTests` (300 зёрен: литейщики — `foundry_partner`, не больше трёх на архетип; детерминизм; отличие от исторической раздачи; сохранение и круговой обход секции).) |
| TSK-022 Предложение партнёрства | Complete (авто 2026-10-10, кусок 6: чистый `Logic/Reactions/PartnershipOfferCore.Run(MonthlyInput)` — шаг `partnership` месячного прохода `MonthlyEvaluatorCore` (после дрейфа отношения, перед уборкой): литейщик (`IsFoundry`, не банкрот и не спящий, с характером) с отношением ≥ `reactions.Partnership.minAttitude` (40), без контракта с игроком (его нет среди `MonthlyInput.ContractRivals`) и без предложения за последние `repeatMonths` (6) календарных месяцев (`ReactionBook.MonthsSincePartnerOffer`, дата в `counters.lastPartnerByUid`) бросает `monthlyChance` (0.10) один раз; не прошедшее условие не тратит бросок. Успех — письмо через `IPartnershipMail`, запись даты (кулдаун идёт от письма) и событие `Offered`; письмо, которое служба не приняла (или упала), не пишет запись, и бросок повторится в следующем месяце, сбой одного литейщика не мешает другому. Игровой `Reactions/PartnershipOffer`: письмо `Low`, тег `rival:partner:<yyyy-MM>:<uid>` (`ReactionTags.Partner`/`TryParsePartner`), заголовок `rival.mail.partner.subject`, текст `rival.mail.partner.body`, действие `openNegotiation` («Открыть переговоры», повторяемое, письмо остаётся открытым); строка «Предложение партнёрства: началась» в истории соперника и `ReactionStarted(uid, "Partnership")` API; поток бросков `rival-personalities/partner`. Действие обрабатывает ветка `openNegotiation` единого `IntelMail.OnAction` — `ContractNegotiationWindow.Open(литейщик, игрок, false)` на окне из `FindFirstObjectByType` (литейщик — поставщик, игрок — клиент; бонус `Interest` и скидку на наценку считают патчи TSK-017), сбой ловит `ModHooks.Guard`. Добавлена строка `rival.reaction.partnership` (ru/en). Тесты: `PartnershipTests` (AC-12.1, порог 40, контракт, кулдаун 5 и 6 месяцев по каждому литейщику, отказ службы, порядок «дрейф, потом бросок», круговой обход секции, источник письма и действия). В игре не запускалось (окно переговоров — TSK-025, AC-12.1).) |
| TSK-023 Выключение, старые сейвы, миграция | Complete (авто 2026-10-10, кусок 7: **миграция и проверки при чтении** — чистые `Logic/State/RivalStateMigrations` (шаги `N → N+1` над глубокой копией секции, объект ядра не меняется; `Steps` пуст, пока версия 1, тест `EveryVersionOfTheSection_HasItsMigrationStep` покраснеет в день, когда `SchemaVersion` станет 2; отсутствующий или упавший шаг останавливает цепочку с причиной, секция всё равно читается; версия ниже 1 читается как 1, выше целевой — как есть) и `Logic/State/RivalStateSanitizer` с `RivalStateLimits` (клипы и лимиты: даты строго `yyyy-MM-dd`; нечитаемый `graceStart` → пусто, иначе льготный период стоял бы вечно; отношение ±100, NaN остаётся «не задано»; история отношения ≤ 20, дельта ±200, причина ≤ 64 знаков; доли 0…1; `pending` ≤ 20, `active` ≤ 20, `cautions` ≤ 50, `revealed` ≤ 13 уникальных, `history` ≤ 100, `priceBook` ≤ 1000 (цена, которая не цена, отбрасывается: путь назад пишет её в CPU игры); одна запись на компанию или пару; запись без id, компании, типа или читаемой даты отбрасывается; `nextReactionSeq` всегда выше любого `r-<n>` секции, чтобы id не повторялся; на каждый список не больше двух строк в лог). `RivalSaveCodec.Read(section, schema, problems)` = миграция + привязка + проверки, `RivalSaveSection.Read(section, schema)` идёт через него; секция новее мода → значения по умолчанию (ядро и так зовёт `ResetToDefaults` и хранит исходную секцию, оно же пишет предупреждение). Round-trip: тест с полным образцом, в котором есть значение в каждом поле каждой записи (отражение следит, чтобы новое поле не осталось без покрытия), через текст и через настоящий `SideFileModel`; в данных нет поля версии. **Выключение посреди партии** — `OnDisabled` теперь выполняет шаги `Always(...)` в любом состоянии фичи, в том числе `Faulted` (там `ModHooks.Guard` тело не запускает, а цены и дизайнеры вернуться должны): заморозка секции, `FlawLink.Detach` (`SetResponseBias(null)`) и `AcquisitionLink.Detach`, монитор, окно, `PersonalityRegistry.OnDisabled` (дизайнеры получают снимок обратно, строка лога `OnDisabled: reverted the character of N`), бусты (`ClearBoosts`), `PriceBook.ReleaseAll` (строка лога `OnDisabled: restored N prices`); сбой одного шага считается за фичей (`ReportFault`) и не мешает следующим; записи `ModModifiers` не удаляются и не пересоздаются (тест по исходникам: в фиче один вызов `ModModifiers.Add` — бонус переманенной команды, и ни одного `Remove`). **Повторное включение продолжает состояние**: ядро отдаёт включённой заново фиче копию секции с последнего сейва или ничего, а не состояние на момент выключения, поэтому `RivalSaveSection.Freeze/UseFreeze/DropFrozen` (то же решение, что в bankruptcy-recovery) запоминает состояние в `OnDisabled` и отдаёт его первой выдаче секции после включения, только в той же игре (свойство `FileHeader` класса `ModSave` — новый объект на каждое чтение бокового файла) и только пока фича активна; новая игра, меню и загрузка другого сейва его сбрасывают. Если после `OnEnabled` ядро всё же отдало другую секцию (загрузили другой сейв, пока фича была выключена), `OnOwnSwitchOn` применяет характеры заново (`ApplyAll`, снимки дизайнеров сохраняются) и начинает льготный период, если его нет. Тесты (в `Features/RivalPersonalities/`, не в `RivalPersonalities/`: так собирает `build_feature.py`): `RivalStateSerializationTests` (41), `RivalDisableFlowTests` (20) (на настоящем `SideFileModel`: тот же сеанс, сейв между, новая игра, другой сейв, тот же сейв заново, выключенная фича в файле; цены, множители и приоритеты дизайнеров; разбор исходников `OnDisabled`). Ограничение ядра, которое фича обойти не может: сейв, записанный при выключенной фиче, содержит копию секции с последнего сейва при включённой (или не содержит её вовсе) — `mod/requests/rival-personalities.md` п. 13.1. В игре не запускалось (сценарий 7 — TSK-025).) |
| TSK-024 Самопроверка и замеры | Complete (авто 2026-10-10, кусок 7: **самопроверка** — чистые `Logic/Diagnostics/RivalSelfCheckRules` (таблица из 12 целей дизайна: 11 обязательных и необязательный `AIBehaviourCreation.AcceptableFailureRate`; запрещённые `AICompany.AddCpu`/`Company.AddCpu`; события пака, типы эффектов, предикаты `rival.*`, ключи строк) и игровой `Diagnostics/RivalSelfCheck` (собирает факты из `ModHooks.Report`, каталога, `EventRegistry`, `EventEffects`, `ModConditions`, `ModL10n`). На старте одна строка `[PTMod] [rival-personalities] INFO self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)` (тест сверяет строку со спекой через `LogFormatter`); при проблеме — `FAILED`, по строке на проблему и список `missing targets: …`; необязательный патч — только предупреждение (в строке 11 patches); отсутствующая обязательная цель или пустой каталог → `ModFeatures.MarkUnavailable` (ядро вызывает `OnDisabled`, цены и дизайнеры возвращаются). Когда: один раз за сеанс на `GameReady`, а при включении фичи в готовой игре — сразу; если событийная система активна, но пак ещё не прочитала, проверка ждёт первый игровой день (иначе ложная тревога). Без `event-system` проверка не падает: предупреждение «реакции не планируются». Если обязательный патч не применился при запуске, фичу делает `Unavailable` само ядро до `OnEnabled`, и список целей пишет самопроверка ядра (`ModSelfCheck`, вкладка «Диагностика»): строка фичи в этом случае не пишется. **Замеры** — чистые `Logic/Diagnostics/RivalTimingBook` (дни, среднее на игровой день, худший день, месячный проход; бюджеты 0.3 мс в день и 3 мс на проход) и `RivalTimingScope` (считается только внешний вход) и игровой `Diagnostics/RivalTimings` (`Stopwatch`; включён в отладочной сборке и при подробном логе мода; в релизе `Begin` возвращает 0): измерены пять ежедневных патчей, три хода игрока, `DayPassed`, `CpuReleased`, `ModMail.Delivered`; раз в игровой месяц после месячного прохода строка `timing 1984-03: 31 day(s), 0.121 ms a day on average, worst day … (within 0.3 ms); monthly pass 1.2 ms (within 3 ms)` (предупреждение, если выше бюджета). **Изоляция ошибок** — каждое из 14 тел патчей внутри своего `try` с `ModHooks.ReportFault(Feature, <площадка>, e)` и проверкой `ModHooks.CanRun` (кроме финализатора окна переговоров: он закрывает область всегда); собственных счётчиков ошибок и порогов нет (тест по исходникам; единственное поле «Failures» — результат одного месячного прохода). Искусственное исключение: `Diagnostics/RivalFaultProbe.Trip(site)` — `[Conditional("PTMOD_DEBUG")]`, при флаге `rival-personalities.debug.throwInPatch` раздела `debug` файла `ptmod_settings.json` каждое тело патча бросает в начале своего `try` (в финализаторе — после закрытия области); исключение не доходит до игры, пятое за сеанс переводит фичу в `Faulted` (порог ядра). Отдельный «фейковый» патч не объявлялся: он был бы 13-й целью и менял бы строку самопроверки; вместо него исключение вставляется в настоящие тела патчей. Тесты (`RivalDiagnosticsTests`, 36): правила самопроверки на фактах (здоровый старт, необязательный патч, обязательная цель, два патча на цель, `AddCpu`, пустой каталог, событийная система выключена или без события), сверка таблицы с `[ModPatch]` в исходниках (14 объявлений, 12 целей, имена через `using`-псевдонимы), часы и бюджеты, парные `Begin`/`End` в `finally`, изоляция. В игре не запускалось (AC-12 и замеры — TSK-025).) |
| TSK-025 Ручная проверка в игре (владелец) | Pending |
| TSK-026 Решения ИИ в событиях по характеру | Complete (авто 2026-10-10, кусок 6: чистый `Logic/Events/EventChoiceMath` (`Weights`, `Choose`, `HasCostDifference`, `CanPick`, `EventChoiceCandidate`) — `spend` умножает `AiWeight` самого дорогого из доступных вариантов на `eventChooser.styleWeight` (1.5), `save` — самого дешёвого (варианты с равной крайней ценой усиливаются все); недоступный (`Available` = false), не по карману (правило `aiMinCashMultiple × стоимость` политики по умолчанию) и вес не положительный или бесконечный не выбираются; `neutral`, один вариант и варианты с одинаковой ценой (нечего тратить или беречь) дают `null`; выбор — `rng.WeightedIndex` переданного движком `IModRng`, не больше одного броска. Игровой `Events/RivalEventChooser : IEventAiChooser, IEventAiChooserFilter`: `null` для игрока, неизвестной и спящей компании, при выключенной фиче и при исключении (`ModHooks.Guard<string>`); `EventAi.AddChooser(chooser, 100)` в `Register` (движок откладывает регистрацию до запуска `event-system` и воспроизводит её; если `eventChooser.priority` в конфиге другой, после чтения контента выбирающий добавляется ещё раз). `eventStyle` в `archetypes.json` уже было с TSK-003, тест сверяет семь значений с таблицей design (spend: hegemon, engineer; save: price_warrior, conservative, niche_specialist; neutral: fast_follower, foundry_partner). Тесты: `EventChooserTests` (10 000 бросков с фиксированным зерном: для `save` «ждать» из пары «ремонт за 5 млн» / «ждать» выбирается ≈ 60 %, без усиления ≈ 50 %; для `spend` наоборот; усиление 3 даёт ≈ 75 %; недоступный и не по карману вариант не выбирается ни разу). Отличия: файл чистой части — `Logic/Events/EventChoiceMath.cs`; проверку денег движок не кладёт в `Available`, поэтому выбирающий делает её сам (`requests`, п. 12.1).) |
| TSK-027 Связь с поглощениями (Could) | Complete (авто 2026-10-10, кусок 6: чистый `Logic/Integration/AcquisitionRules` (`PriceMultiplier(a, divisor)` = `1 + max(0, −a) / divisor`: −60 → 1.3, +40 → 1.0, −100 → 1.5, клип по шкале, делитель < 1 → 200) и `AcquisitionLinkCore.OnDeal(тип, цель, поглощена ли)`: сделка `Hostile` снижает отношение всех остальных соперников с характером (не цель, не банкроты, не спящие) на `acquisition.hostileDealAttitude` (−10, чувствительность применяет `AttitudeLedger.Change`, причина `hostile acquisition`, в окне «вы поглотили компанию против её воли»); цель, вышедшая из игры или переданная игроку (`BankruptAssets`, `Distressed`, `Hostile` или `IsAbsorbedByPlayer`), переводится в спящий режим через `RivalApiCore.SetDormant`; доля и приказ о спасении (`Stake`, `RescueOrder`) ничего не меняют; `SyncTargets` после загрузки проверяет цели прошлых сделок. Игровой `Integration/AcquisitionLink`: провайдер `ModModifiers.RegisterProvider(…, ModChannels.AcquisitionPrice, …, "rival.reason.acquisitionPrice")` регистрируется один раз в `Register` (`AcquisitionLink.RegisterProvider`) и не трогается в `OnEnabled`/`OnDisabled`; для игрока и компании без характера множитель 1; кэш цены компании сбрасывается новым событием `AttitudeLedger.Changed`; подписка на `RivalAcquisitionsApi.DealCompleted` ставится на `ModTicks.GameReady` (и при включении фичи или `rival-acquisitions`) только при активной `rival-acquisitions`, снимается при выключении любой из фич; без `rival-acquisitions` модуль не подключается. Тип сделки сравнивается как текст (`DealRecord.type`): `Api/Contracts` соседних фич в сборку тестов не входит (`requests`, п. 12.1). Тесты: `AcquisitionLinkTests` (a = −60 → 1.3, a = +40 → 1.0, AC-15.1; −10 × чувствительность для Intel, AMD, IBM и TSMC; спящие и банкроты не затронуты; загрузка).) |
| TSK-028 Видимость характеров | Complete (авто 2026-10-10, кусок 4: `Logic/CharacterVisibility.IsVisible(hiddenMode, revealed, attitude, revealAttitude)` = `!hiddenMode || revealed || attitude ≥ 20` (NaN не достигает уровня); `PersonalityBook.IsRevealed/Reveal` (запись `RivalState.revealed` без дублей, `true` только в первый раз, отрицательный id не пишется), `PersonalityRegistry.IsRevealed/Reveal` (при включённом режиме «Скрытые» показывает `ModNotify` `rival.notify.revealed` «<компания>: характер раскрыт — <архетип>» один раз, пишет в журнал, поднимает `Revealed`, откуда фича пишет строку `revealed` в историю), `RivalPersonalitiesFeature.IsCharacterVisible(uid)` для окна разведки (TSK-018). Запись переживает сохранение (тест через `RivalSaveCodec`); переключение режима `revealed` не трогает: при выключенном режиме всё видно, при повторном включении раскрытые остаются видны, нераскрытые с отношением < 20 скрываются снова; видимость только по отношению запись не создаёт. На боевых данных TSMC (+20) виден сразу, AMD (−15) и Intel (−10) скрыты (AC-11.2, AC-11.3). `CharacterVisibilityTests` (9) зелёные)) |
| TSK-029 Туманность разведки | Complete (авто 2026-10-10, кусок 5: чистый `Logic/IntelFog` (`RoundShare(0.173, 5)` = 0.15; `RoundSignificant(123, 2)` = 120; `FormatShare` → «≈ 15 %» / «17 %», `FormatIps` → «≈ 120» / «123»; шаг и число значащих цифр из `intelFog.*` конфига; пометка «≈» берётся из строки `rival.window.fog` через делегат; формат числа Ips в игре — `StringFormatter.FloatToIps`; NaN, бесконечности и 0 не ломают). Построитель окна применяет туманность только к доле и `Ips`: тест сравнивает построение с туманностью и без — отношение, угрозы, действующие эффекты, история и шапка совпадают построчно. Параметр `intelFog` читается при каждой перестройке, смена параметра (`ModSettings.Changed`) обновляет открытое окно. Строка «Осторожен после брака до …» идёт в блок «Действует» только при действующей записи `cautions` (`today < until`, как у `CautionCleaner`; позднейший срок побеждает; не показывается банкроту и спящему, FR-17 п. 4). `IntelFogTests` (11) и тесты построителя зелёные)) |
| TSK-030 Осторожность ИИ после брака | Complete (авто 2026-10-10, кусок 6: чистые `Logic/CautionMath` (`Factor(active, factor)`: 0.85 при осторожности, 1 без неё, множитель выше 1 и неверные числа → 1; `UntilDate(start, months)` = начало + месяцы, конец календаря → последний день; `IsSevereEnough(severity, minSeverity)`: `Major` и `Critical` при `Major` по умолчанию; `Applies`: фича `design-flaw-recalls` включена, компания не банкрот и не спящая, нет перемотки) и `Logic/Behaviour/CautionBook` (записи `cautions {uid, flawId, until}`: `OnPublicFlaw` пишет запись один раз на запись брака — срок считается от дня, когда брак впервые стал публичным (`FlawRecordView.PublicSinceDay`), поэтому повторное уведомление и поздняя проверка не создают вторую запись, а прошедший срок не создаёт её вовсе; записи разных браков не складываются; не больше 50 записей, при переполнении уходят закончившиеся, затем заканчивающиеся раньше; `IsActive`, `FactorFor`, `UntilOf`). Игровые `Behaviour/CautionPatch` (постфикс `Optional` на приватном `AIBehaviourCreation.AcceptableFailureRate(ProcessNode)`: `__result *= flawCaution.factor`, без выделений памяти, при сбое — результат игры; отдельный класс патча, объявлен `ctx.Patch`, единственный необязательный патч фичи, его отсутствие не делает фичу `Unavailable`; цель и поле `company` сверены рефлексией с `Assembly-CSharp.dll`) и `Integration/FlawLink` (подписка на `FlawRegistry.OnFlawStateChanged`: брак ИИ-компании в состоянии `Public` и тяжести не ниже `flawCaution.minSeverity` → `CautionBook.OnPublicFlaw`; при подключении один раз просматриваются все известные публичные браки; без `design-flaw-recalls` ничего не создаётся и патч возвращает значение игры). Просроченные записи на `LateMonthPassed` удаляет `CautionCleaner` (TSK-012); окно разведки уже показывает «Осторожен после брака до …» (TSK-029). Тесты: `CautionMathTests` (в том числе округление концов календаря, 60 записей → 50, круговой обход секции, месячный проход), `IntegrationWiringTests`. В игре не запускалось (AC-17.1 — TSK-025).) |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-002, TSK-003 |
| FR-2 | TSK-002, TSK-003, TSK-007 |
| FR-3 | TSK-007, TSK-008, TSK-009 |
| FR-4 | TSK-004, TSK-010, TSK-012 |
| FR-5 | TSK-006, TSK-011, TSK-012 |
| FR-6 | TSK-005, TSK-013 |
| FR-7 | TSK-003, TSK-008, TSK-009, TSK-013, TSK-014, TSK-015, TSK-016 |
| FR-8 | TSK-017 |
| FR-9 | TSK-018, TSK-029 |
| FR-10 | TSK-001, TSK-010, TSK-013, TSK-023 |
| FR-11 | TSK-021, TSK-028 |
| FR-12 | TSK-022 |
| FR-13 | TSK-009, TSK-020 |
| FR-14 | TSK-026 |
| FR-15 | TSK-027 |
| FR-16 | TSK-029 |
| FR-17 | TSK-030 |
| NFR-1, NFR-2 | TSK-024 |
| NFR-3 | TSK-023 |
| NFR-4 | TSK-019 |
| NFR-5 | TSK-013, TSK-018 |
