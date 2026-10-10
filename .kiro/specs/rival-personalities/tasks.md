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
- Чистые классы (`*Math`, `ShareCalculator`, `CharacterVisibility`, `IntelFog`, `CautionMath`, валидатор) — без `UnityEngine`, в `mod/src/PTMod/Features/RivalPersonalities/Logic/`. Тесты — мини-раннер .NET Framework 4.8 в `mod/tests/PTMod.Tests/RivalPersonalities/` (`python mod/build.py --tests`, атрибуты `[Test]`, `[Covers(typeof(X))]`); каждый публичный тип `Logic` указан в `[Covers]`. Код `Logic` — общее подмножество API .NET Framework 4.8 и mscorlib Unity (без `init`, `record`, `Index`, `Range`).
- Патчи — только через `ModHooks`, тело под `ModHooks.Guard`. Каждую цель задача добавляет в самопроверку. **Патчей на `Company.AddCpu` и `AICompany.AddCpu` нет** (ядро отклонит): выпуск CPU берётся из `ModEvents.CpuReleased`. Своего классификатора сегмента и счётчиков ошибок нет.
- Числа баланса — только в `mod/src/PTMod/Content/rival-personalities/*.json`.
- До готовности `event-system` реакции отключены флагом (`GateResult.NoEventSystem`). Поведение, отношение и панель работают раньше.
- Сборка — `python mod/build.py --game-dir <папка игры>` (Roslyn `csc`, без .NET SDK); отладочные флаги `debug.*` и отладочная страница хаба — только в отладочной сборке.

### Development Approach
- **Testing Strategy**: TDD для чистой логики; самопроверка целей патчей на старте; ручные сценарии владельца (design.md, Testing Strategy).
- **Integration Strategy**: фича регистрируется в ядре как `[ModFeature] IModFeature`. Зависимость от `event-system` мягкая: проверка доступности при старте. Связи с `design-flaw-recalls`, `rival-acquisitions`, `press-reviews` — мягкие (подключаются на `ModTicks.GameReady` и при `ModFeatures.StateChanged`).
- **Deployment Strategy**: `install.py --with-mods` (mod-framework). Фича по умолчанию включена в хабе, отладочный флаг выключен.

## Фаза 1 — Каркас, контент, чистая логика

- [ ] **TSK-001**: Скелет фичи и секция сохранения
  - **Requirement**: FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/RivalPersonalitiesFeature.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/State/RivalState.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/State/RivalSaveAdapter.cs` (`IModSaveParticipant`)
  - **Acceptance**:
    - `[ModFeature] sealed class RivalPersonalitiesFeature : IModFeature`: `Id` = `rival-personalities`, `NameKey`/`DescriptionKey` (`rival.feature.name`/`rival.feature.desc`), `DependsOn` пуст (мягкие связи не включаются), `EnabledByDefault` и `SupportsHotToggle` = true. Всё остальное идёт через `FeatureContext` (`ctx.Patch`, `ctx.OnTick`, `ctx.SaveParticipant`, `ctx.Param`, `ctx.HubPage`); прямых подписок на `DateController`, `Player.OnNewGame` и `SaveHandler` нет.
    - Фича видна в хабе «Моды» (вкл/выкл, `ctx.Param`): `randomCharacters` (по умолчанию выкл.), `hiddenCharacters` (вкл.), `intelFog` (вкл.); `debug.forceReactions` — только в отладочной сборке.
    - Участник сохранения: `SectionId` = `rival-personalities`, `SchemaVersion = 1`; `Write()` не содержит `_schema` (его пишет ядро); поля версии в `RivalState` нет.
    - Загрузка сейва без секции даёт состояние по умолчанию без ошибок.
  - **Depends**: mod-framework/TSK-022 (`FeatureContext`, `ModFeatures`), TSK-025 (`ModSave`), TSK-021 (`ModSettings`), TSK-067 (мини-раннер)

- [ ] **TSK-002**: DTO контента и валидатор
  - **Requirement**: FR-1, FR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Content/ArchetypeModels.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Content/ArchetypeCatalog.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/ContentValidationTests.cs`
  - **Acceptance**:
    - `ArchetypeCatalog.Validate` пропускает запись без `id`, клипует `approvalBias`/`researchFundingMult`/`launchPriceMult` к диапазонам 0.8–1.3 / 0.7–1.3 / 0.85–1.15 с предупреждением, отбрасывает неизвестные типы реакций и триггеров, принимает `negotiationDiscount` (bool) и `flawResponseBias` (каждый 0.5–2.0).
    - Назначение на несуществующий архетип даёт `conservative`; запись назначения без `key` — предупреждение.
    - Все тесты зелёные.
  - **Depends**: mod-framework/TSK-015 (`ModContent`), TSK-067

- [ ] **TSK-003**: Контент: архетипы, назначения, конфиг, события
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

- [ ] **TSK-004**: Чистая логика отношения
  - **Requirement**: FR-4
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/AttitudeMath.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/AttitudeMathTests.cs`
  - **Acceptance**: тесты покрывают:
    - клип ±100;
    - дрейф 10 % (−40 → −36);
    - границы уровней −60/−20/20/60;
    - `ReactionMultiplier` (−100 → 2.0, 0 → 1.0, +100 → 0.25);
    - `InterestBonus` (40 → 8, −37 → −7);
    - `DiscountPoints` (40 → 4, 100 → 10, −30 → 0).
  - **Depends**: mod-framework/TSK-067

- [ ] **TSK-005**: Чистая логика шансов и лимитов реакций
  - **Requirement**: FR-6
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/ReactionMath.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/ReactionMathTests.cs`
  - **Acceptance**:
    - `Chance(0.40, −50, сложность 2)` = 0.60; клип 0.9; множители сложности [0.4…1.6].
    - Тест на каждый `GateResult` (`GracePeriod`, `Cooldown`, `ConcurrentCap`, `YearlyCap`, `AttitudeTooHigh`, `SameTypeActive`, `Bankrupt`, `Dormant`, `NoEventSystem`), включая `anyAttitude`.
  - **Depends**: TSK-004

- [ ] **TSK-006**: Расчёт долей рынка (классификатор сегмента — в ядре)
  - **Requirement**: FR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/ShareCalculator.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/ShareCalculatorTests.cs`
  - **Acceptance**:
    - Пустой месяц даёт доли 0 без деления на ноль.
    - Сумма долей по сегменту = 1 ± 1e-5; индекс сегмента массива = `(int)Segment`.
    - Тест архитектуры: в `Features/RivalPersonalities` нет типа-классификатора сегмента по цене (`SegmentClassifier` удалён; используется `ModSegments.Classify`, проверка 312/311/187/186/99/98 живёт в `SegmentRuleTests` ядра).
  - **Depends**: mod-framework/TSK-067

## Фаза 2 — Поведение ИИ

- [ ] **TSK-007**: Применение и откат характера
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

- [ ] **TSK-008**: Патчи темпа выпуска и финансирования исследований
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

- [ ] **TSK-009**: Патч цен ИИ и PriceBook
  - **Requirement**: FR-3, FR-7, FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/MarketPatches.cs` (prefix/postfix `AIBehaviourMarket.OnTick`)
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/PriceBook.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/PriceCutMath.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/PriceCutMathTests.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/PriceBookTests.cs`
  - **Acceptance**:
    - Новый CPU ИИ получает `TargetPrice = Price = max(TP × launchPriceMult, RealUnitCost × 1.05)`.
    - `PriceCutMath`: 239/120/0.15/0.08 → 203.15.
    - `Clamp` удерживает `TargetPrice` не выше цены удара.
    - `Restore` возвращает исходную цену; запись для отсутствующего CPU удаляется.
    - `ApplyCut(reactionId, rival, Segment, depth, floorMargin)` возвращает число затронутых CPU, 0 для банкрота и спящей компании; записи внешних вызывающих (id не `r-<n>`) снимает `ExpireExternal` через `priceCut.externalMaxDays` (400).
    - В дневном пути нет аллокаций (пул списков).
  - **Depends**: TSK-007

## Фаза 3 — Отношение и триггеры

- [ ] **TSK-010**: Учёт отношения
  - **Requirement**: FR-4, FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Attitude/AttitudeLedger.cs`
  - **Acceptance**:
    - Начальное значение = baseline архетипа.
    - `Change` применяет sensitivity и клип, хранит 20 последних записей, поднимает `LevelChanged` и `ModNotify` при смене уровня.
    - `MonthlyDrift` вызывается в фазе `TickPhase.LateMonthPassed`.
    - Отношение банкротов заморожено.
    - Значения переживают сохранение и загрузку.
  - **Depends**: TSK-001, TSK-004, mod-framework/TSK-033 (`ModNotify`)

- [ ] **TSK-011**: Триггеры событий игрока
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

- [ ] **TSK-012**: Месячная оценка: доли, демпинг, контракты
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

- [ ] **TSK-013**: Планировщик реакций и письма разведки
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

- [ ] **TSK-014**: Исполнитель реакций: ценовой удар и ответный запуск
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

- [ ] **TSK-015**: OEM-натиск, переманивание инженеров и удар по репутации
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalPoachCounterOfferHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/Effects/RivalResearchBoostHandler.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/PoachMath.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/PoachMathTests.cs`
    - событие `rival.smear` в `events.json` и текст письма `rival.mail.intel.smear`
  - **Acceptance**:
    - `rival.oemPush` (цель `ai:N`) даёт сопернику `contract.signChance` × 1.5 на 365 дней (стандартный `ModifierTimed`, `scope: Target`, причина `rival.reason.oemPush`).
    - `rival.poach` (цель `player`, соперник в `Args`) показывает модальный выбор с таймером 10 дней (письмо `Critical` ведёт `event-system`).
    - «Контрпредложение» списывает `max(250 000 × Inflation, 2 × ExpenseByResearch)` через `Company.SpendMoney(..., ExpenseOrigin.Research)`.
    - «Отпустить» по умолчанию даёт игроку `research.speed` 0.85 на 120 дней (`ModifierTimed`) и сопернику 1.10 на 120 дней (`ModModifiers.Add` в `RivalResearchBoost`, причина `rival.reason.poach`); запись не пересоздаётся и не удаляется при выключении фичи.
    - `rival.smear` (цель `player`) снижает репутацию игрока на 3 стандартным эффектом `Reputation` (`ModReputation.Add`, причина `event:rival.smear`); реакция телеграфируется письмом разведки и подчиняется лимитам FR-6.
    - Тест `PoachMath`: `Reports.Count < 2` → минимум.
  - **Depends**: TSK-014, event-system/TSK-011 (`ModifierTimed`), TSK-012 (эффекты `Reputation`, `Money`), TSK-016, TSK-018 (выбор, таймер, окно), mod-framework/TSK-027 (`ModModifiers`)

- [ ] **TSK-016**: Замыкание ответного удара и деградация без event-system
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/CounterLaunchWatcher.cs` (подписка на `ModEvents.CpuReleased`)
  - **Acceptance**:
    - CPU соперника в сегменте активного `CounterLaunch` (событие ядра, не в перемотку; вариант `isVariant` не считается) даёт письмо «Ответный удар» (`Normal`, `Tag` `rival:counter:<reactionId>`) и запись `counterLaunchDone`.
    - Без `event-system` реакции не планируются: одна запись в лог за сессию, остальная фича работает.
  - **Depends**: TSK-014, mod-framework/TSK-056 (`ModEvents`)

- [ ] **TSK-028**: Видимость характеров
  - **Requirement**: FR-11 (п. 2–4)
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/CharacterVisibility.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/CharacterVisibilityTests.cs`
    - правки `PersonalityRegistry.cs` (`IsRevealed`, `Reveal`) и `RivalState.revealed`
  - **Acceptance**:
    - `IsVisible`: режим «Скрытые» выключен → виден; включён → скрыт без реакции и при отношении < 20; виден после первой начатой реакции или при отношении ≥ 20.
    - `Reveal` сохраняет `uid` в `revealed` (без дублей), показывает `ModNotify` «характер раскрыт» один раз и переживает сохранение.
    - Выключение режима не стирает `revealed`; повторное включение не скрывает раскрытых.
  - **Depends**: TSK-010, TSK-014, mod-framework/TSK-033

## Фаза 5 — Переговоры и интерфейс

- [ ] **TSK-017**: Отношение в переговорах по бизнес-контрактам
  - **Requirement**: FR-8
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Negotiation/NegotiationPatches.cs`. Цели:
      - prefix + finalizer `ContractNegotiationWindow.UpdateScores`;
      - постфиксы `AIMath.GetProviderScores` и `AIMath.GetClientScores`.
    - `mod/src/PTMod/Features/RivalPersonalities/Negotiation/NegotiationScope.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/NegotiationScopeTests.cs`
  - **Acceptance**:
    - Вне scope очки не меняются (ИИ-ИИ сделки ванильные).
    - В scope `Interest` ИИ-стороны += `round(a/5)`; −1000 сохраняется.
    - Для архетипа с `negotiationDiscount` и ИИ-поставщика `Price` += `clamp(round(a/10), 0, 10)`; при отрицательном отношении скидки нет.
    - Scope закрывается даже при исключении (finalizer).
  - **Depends**: TSK-010, mod-framework/TSK-023

- [ ] **TSK-018**: Окно «Разведка: соперники»
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

- [ ] **TSK-029**: Туманность разведки и строка осторожности в панели
  - **Requirement**: FR-16, FR-9
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/IntelFog.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/IntelFogTests.cs`
    - правки `IntelWindow.cs` (параметр `intelFog`, строка «Осторожен после брака до …»)
  - **Acceptance**:
    - При включённой туманности доля округлена до 5 п. п. («≈ 15 %» для 17.3 %), `Ips` — до двух значащих цифр («≈ 120» для 123); при выключенной — точные значения.
    - Отношение, угрозы, активные эффекты и история от туманности не зависят.
    - Смена параметра обновляет открытую панель.
    - Строка осторожности показывается только для компании с действующей записью `cautions`.
  - **Depends**: TSK-018

- [ ] **TSK-019**: Строки ru/en
  - **Requirement**: NFR-4, FR-7, FR-9
  - **Deliverables**:
    - `mod/src/PTMod/L10n/ru.json` (ключи `rival.*`)
    - `mod/src/PTMod/L10n/en.json` (ключи `rival.*`)
  - **Acceptance**:
    - Все ключи из design.md и кода есть в обоих файлах (включая `rival.feature.*`, `rival.window.unknownCharacter`, `rival.notify.revealed`, `rival.mail.intel.smear`, `rival.reason.*`).
    - Тест сверяет набор ключей `rival.*` в ru и en и находит ключи, используемые в коде и контенте, но отсутствующие в таблицах; ключи начинаются с `rival.` (`ModIds.IsL10nKeyAllowed`).
  - **Depends**: TSK-003, TSK-013, TSK-018, TSK-028, TSK-029

## Фаза 6 — Расширения

- [ ] **TSK-020**: API для других фич и предикаты условий
  - **Requirement**: FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Api/IRivalPersonalitiesApi.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Api/RivalPersonalitiesApi.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Api/RivalConditions.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Api/FlawResponseBias.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/FlawLink.cs` (регистрация множителей)
    - `mod/tests/PTMod.Tests/RivalPersonalities/FlawBiasTests.cs`
  - **Acceptance**:
    - `GetAttitude`/`GetArchetypeId` возвращают `null` для игрока.
    - `SetDormant(true)` снимает модификации поведения, возвращает цены под ударами и отменяет запланированные реакции компании.
    - Предикаты `rival.attitudeAtMost/AtLeast/reactionsExecuted` (параметр `company` = `ai:<UniqueID>` | `anyAI`) зарегистрированы через `ModConditions.Register`.
    - `ApplyPriceCut(reactionId, rival, Segment, depth, floorMargin)` и `RestorePriceCut(reactionId)` работают через `PriceBook`; при выключенной фиче API — null-object (`IsEnabled == false`, `ApplyPriceCut` → 0).
    - `FlawResponseBias : IFlawResponseBias` с `float[] Multipliers(ICompany, bool isPublic)` (порядок Recall, Stepping, Patch, Ignore); `FlawLink` вызывает `FlawRegistry.SetResponseBias(...)` на `GameReady` и при включении `design-flaw-recalls`, `null` — при выключении своей фичи.
  - **Depends**: TSK-009, TSK-010, TSK-013, mod-framework/TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`), design-flaw-recalls/TSK-015 (`IFlawResponseBias`), TSK-020 (`FlawRegistry`) — мягкая зависимость

- [ ] **TSK-021**: Случайные назначения характеров
  - **Requirement**: FR-11 (п. 1, Could)
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/RandomAssignment.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/RandomAssignmentTests.cs`
  - **Acceptance**:
    - В режиме «Случайные» (`randomCharacters`) назначение выполняется на `TickPhase.FastForwardEnded`: литейщики — `foundry_partner`, остальные архетипы — не более чем у 3 компаний; результат детерминирован `ModRandom` (поток `rival-personalities/assign`) и сохраняется.
    - Смена режима посреди партии назначений не перераспределяет.
  - **Depends**: TSK-007, mod-framework/TSK-025

- [ ] **TSK-022**: Предложение партнёрства
  - **Requirement**: FR-12
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Reactions/PartnershipOffer.cs`
  - **Acceptance**:
    - Литейщик при отношении ≥ +40 без контракта с игроком с шансом 0.10 в месяц шлёт письмо (`Low`, `Tag` `rival:partner:<yyyy-MM>:<uid>`) с действием «Открыть переговоры» (`ModMailAction`, `ContractNegotiationWindow.Open`); не чаще раза в 6 месяцев на литейщика.
  - **Depends**: TSK-012, TSK-013, mod-framework/TSK-062

- [ ] **TSK-026**: Решения ИИ в событиях по характеру
  - **Requirement**: FR-14
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Events/RivalEventChooser.cs`
    - поле `eventStyle` в `archetypes.json`
    - `mod/tests/PTMod.Tests/RivalPersonalities/EventChooserTests.cs`
  - **Acceptance**:
    - `EventAi.AddChooser(chooser, 100)` вызывается при регистрации, если `event-system` доступна; `Choose(..., IModRng rng)`.
    - Для игрока и `neutral` — `null` (решает следующий выбирающий или политика по умолчанию).
    - `spend`/`save` умножают `AiWeight` самого дорогого или дешёвого доступного варианта на 1.5 (`ChoiceOption.Cost`, `Available`).
    - Недоступные варианты (`Available = false`) не выбираются.
    - Тест на 10 000 бросков с фиксированным сидом подтверждает сдвиг частот.
  - **Depends**: TSK-003, event-system/TSK-014 (`EventAi`, `IEventAiChooser`), TSK-016 (решения ИИ)

- [ ] **TSK-027**: Связь с поглощениями (Could)
  - **Requirement**: FR-15
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/AcquisitionLink.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/AcquisitionLinkTests.cs`
  - **Acceptance**:
    - Провайдер канала `acquisition.price` (`1 + max(0, −a)/200`, причина `rival.reason.acquisitionPrice`) регистрируется один раз в `Register` (ленивое связывание, порядок с `rival-acquisitions` не важен) и не пересоздаётся при `OnEnabled`/`OnDisabled`.
    - Подписка на `RivalAcquisitionsApi.DealCompleted` ставится на `GameReady` при активной `rival-acquisitions`: сделка с `DealRecord.type` = `Hostile` даёт −10 × sensitivity остальным соперникам, выведенная или переданная цель (`targetUniqueId`) — `SetDormant(true)`.
    - Без `rival-acquisitions` модуль не подключается.
    - Тесты: a = −60 → 1.3; a = +40 → 1.0.
  - **Depends**: TSK-010, TSK-020, mod-framework/TSK-059 (ленивое связывание провайдеров), rival-acquisitions/TSK-017 (публичный API, `DealCompleted`) — мягкая зависимость

- [ ] **TSK-030**: Осторожность ИИ после брака
  - **Requirement**: FR-17
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/Behaviour/CautionPatch.cs` (постфикс `AIBehaviourCreation.AcceptableFailureRate`, `Optional`)
    - `mod/src/PTMod/Features/RivalPersonalities/Logic/CautionMath.cs`
    - `mod/src/PTMod/Features/RivalPersonalities/Integration/FlawLink.cs` (подписка на `FlawRegistry.OnFlawStateChanged`, расширение TSK-020)
    - `mod/tests/PTMod.Tests/RivalPersonalities/CautionMathTests.cs`
  - **Acceptance**:
    - Публичный брак `Major`/`Critical` у ИИ-компании создаёт запись `cautions {uid, flawId, until}` на `flawCaution.months` (24) месяцев один раз на запись брака.
    - Пока запись действует, `AcceptableFailureRate` этой компании × `flawCaution.factor` (0.85); без записи, при выключенной `design-flaw-recalls`, банкротстве, спящем режиме и в перемотке — исходное значение.
    - Просроченные записи удаляются на `LateMonthPassed`; секция хранит ≤ 50 записей.
    - Патч `Optional`: его отсутствие не делает фичу `Unavailable`.
  - **Depends**: TSK-010, TSK-020, mod-framework/TSK-023, design-flaw-recalls/TSK-020 (`FlawRegistry.OnFlawStateChanged`) — мягкая зависимость

## Фаза 7 — Надёжность и проверка

- [ ] **TSK-023**: Выключение посреди партии, старые сейвы, миграция
  - **Requirement**: FR-10, NFR-3
  - **Deliverables**:
    - `mod/src/PTMod/Features/RivalPersonalities/State/RivalStateMigrations.cs`
    - `mod/tests/PTMod.Tests/RivalPersonalities/RivalStateSerializationTests.cs`
  - **Acceptance**:
    - `OnDisabled` возвращает цены и параметры дизайнеров, снимает бусты поведения и `FlawLink`; записи `ModModifiers` не удаляются и не пересоздаются (ядро хранит, но не применяет; срок сохраняется).
    - Повторное включение продолжает состояние.
    - Round-trip секции без потерь; в данных нет поля версии.
    - Клипы и лимиты размеров при чтении.
    - Секция новее мода (`_schema` больше `SchemaVersion`): `Read` получает значения по умолчанию, исходная секция сохраняется ядром.
    - Миграция версий — внутри `Read(section, schema)`.
  - **Depends**: TSK-014, TSK-007, mod-framework/TSK-025

- [ ] **TSK-024**: Самопроверка, изоляция ошибок, замеры
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
| TSK-001 Скелет фичи и секция сохранения | Pending |
| TSK-002 DTO контента и валидатор | Pending |
| TSK-003 Контент: архетипы, назначения, конфиг, события | Pending |
| TSK-004 Чистая логика отношения | Pending |
| TSK-005 Шансы и лимиты реакций | Pending |
| TSK-006 Расчёт долей рынка | Pending |
| TSK-007 Применение и откат характера | Pending |
| TSK-008 Патчи темпа выпуска и финансирования | Pending |
| TSK-009 Патч цен ИИ и PriceBook | Pending |
| TSK-010 Учёт отношения | Pending |
| TSK-011 Триггеры событий игрока | Pending |
| TSK-012 Месячная оценка | Pending |
| TSK-013 Планировщик и письма разведки | Pending |
| TSK-014 Ценовой удар и ответный запуск | Pending |
| TSK-015 OEM-натиск, переманивание, удар по репутации | Pending |
| TSK-016 Замыкание ответного удара, деградация | Pending |
| TSK-017 Отношение в переговорах | Pending |
| TSK-018 Окно разведки | Pending |
| TSK-019 Строки ru/en | Pending |
| TSK-020 API и предикаты | Pending |
| TSK-021 Случайные назначения характеров | Pending |
| TSK-022 Предложение партнёрства | Pending |
| TSK-023 Выключение, старые сейвы, миграция | Pending |
| TSK-024 Самопроверка и замеры | Pending |
| TSK-025 Ручная проверка в игре (владелец) | Pending |
| TSK-026 Решения ИИ в событиях по характеру | Pending |
| TSK-027 Связь с поглощениями (Could) | Pending |
| TSK-028 Видимость характеров | Pending |
| TSK-029 Туманность разведки | Pending |
| TSK-030 Осторожность ИИ после брака | Pending |

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
