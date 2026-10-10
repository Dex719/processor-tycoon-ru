# Tasks: event-system

## Document Information

- **Feature Name**: event-system — движок событий с выбором, таймером и журналом
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: [requirements.md](requirements.md)
  - Design: [design.md](design.md)
  - Фреймворк: `.kiro/specs/mod-framework/tasks.md` (зависимости вида `mod-framework/TSK-NNN`, номера v0.2)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала строится чистая логика без Unity: модель контента, планировщик, эффекты, таймеры, сохранение. Она покрывается тестами в `mod/tests/PTMod.Tests/EventSystem/` (свой мини-раннер .NET Framework 4.8, `python mod/build.py --tests`). Затем подключаются сервисы `PTMod.Core` (`ModTicks`, `ModSave`, `ModModifiers`, `ModMail`, `ModUI`, `ModConditions`, `ModCompanies`, `ModCredit`) и UI. В конце — стартовый контент, самопроверка, замер производительности и ручная проверка владельцем.

### Implementation Strategy

- Игра за интерфейсом `IGameFacade`: логика тестируется без Unity, адаптер к игре тонкий.
- Каждый тип эффекта — отдельный класс `IEventEffectHandler`, регистрируется так же, как это будут делать контент-паки.
- Числа эффектов вычисляются один раз (D1). Тесты сверяют показанное с применённым.
- Фича — `[ModFeature] sealed class EventSystemFeature : IModFeature`. Тики, участник сейва, параметры и страница хаба регистрируются через `FeatureContext`; собственных патчей Harmony нет (пакетный прогон, выпуск CPU и почта — службы ядра).
- Версию секции сейва (`_schema`) ведёт ядро; в DTO фичи поля версии нет.
- Каждый публичный тип `Features/EventSystem/Logic` указывается в `[Covers]` тестов (процент строк не измеряется).

### Development Approach

- **Testing Strategy**: TDD для чистой логики (лотерея, цели, деньги, агрегатор, таймеры, сохранение); самопроверка на старте; ручные сценарии M1–M13 из `design.md`.
- **Integration Strategy**: подключение к `PTMod.Core` после готовности соответствующих задач `mod-framework`.
- **Deployment Strategy**: `install.py --with-mods`, фича включена по умолчанию, выключается в хабе «Моды».

## Фаза 1 — Модель и контент

- [ ] **TSK-001**: Модель контента и адаптер параметров
  - **Requirement**: FR-1, FR-6
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Model/EventDefinition.cs`: `EventDefinition` (`IContentRecord`), `EventTrigger`, `EventTargetSpec`, `EventChoice`, `EventEffect`, перечисления;
    - `mod/src/PTMod/Features/EventSystem/Model/EffectParams.cs`: `IEffectParams`, `FloatRange`, адаптер над `JsonObject`;
    - `mod/src/PTMod/Features/EventSystem/Model/DefinitionMapper.cs`: `JsonObject` → `EventDefinition`, условия через `ModConditions.Parse`.
  - **Acceptance**: типы компилируются `csc` (общее подмножество API .NET Framework 4.8 в `Logic`); `IEffectParams.GetRange` принимает число и пару `[lo, hi]`; тексты — `PTMod.Core.LocText`; имена соответствуют контракту из `design.md`
  - **Depends**: mod-framework/TSK-009, mod-framework/TSK-010 (`ModJson`), mod-framework/TSK-019 (`ModConditions.Parse`), mod-framework/TSK-067 (мини-раннер тестов)

- [ ] **TSK-002**: Реестр паков и валидатор контента
  - **Requirement**: FR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Content/EventRegistry.cs` (`RegisterPack`, `Get`, `All`, индекс категорий; `PackOptions` с `SettingsFeatureId`, `Frequency`, `Severity`, `EventsFile`, `RivalNewsTopN`);
    - `mod/src/PTMod/Features/EventSystem/Content/ContentValidator.cs` (в том числе `ModIds.IsEventIdAllowed` для префикса пака);
    - `mod/src/PTMod/Features/EventSystem/Content/EventConfig.cs` (`config.json` каждого пака через `ModContent.LoadConfig`: категории, `channelLimits`, `maxPendingChoices`, `logLimit`; слияние пределов — самый узкий).
  - **Acceptance**:
    - записи грузятся через `ModContent.LoadRecords` один раз, на первом `ModTicks.GameReady` после `Register` всех фич; файл событий пака — `PackOptions.EventsFile` (по умолчанию `events`);
    - битая запись пропускается с `ModLog.Warn` «skip '<id>': <причина>»;
    - ID события чужого префикса отклоняется (AC-1.4);
    - проверки из таблицы параметров `design.md` работают;
    - переопределение из `PTMod/content/<pack>/` подхватывается;
    - категории и пределы из конфигов нескольких паков сливаются.
  - **Depends**: TSK-001; mod-framework/TSK-015, mod-framework/TSK-021 (`ModContent`, `ModLog`), mod-framework/TSK-043 (`core.json`, таблица префиксов), mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-003**: Тесты загрузчика и валидатора
  - **Requirement**: FR-1
  - **Deliverables**: `mod/tests/PTMod.Tests/EventSystem/ContentValidatorTests.cs`, фикстуры `mod/tests/PTMod.Tests/EventSystem/Fixtures/*.json`
  - **Acceptance**: AC-1.1, AC-1.2, AC-1.4 воспроизведены тестами; дубликат `id`, неизвестный канал, `defaultChoice` с `requires`, Σ шансов группы > 1, предикат без префикса — каждый случай отдельным тестом, все зелёные; публичные типы указаны в `[Covers]`
  - **Depends**: TSK-002; mod-framework/TSK-067

## Фаза 2 — Планировщик, цели, детерминизм

- [ ] **TSK-004**: Игровой фасад и каталог компаний
  - **Requirement**: FR-3, NFR-1
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Game/IGameFacade.cs`;
    - `mod/src/PTMod/Features/EventSystem/Game/GameFacade.cs` (`InflationHandler.Instance.Inflation`, `CompanyFinance.QuarterlyRevenue` с защитой `Reports.Count < 3`, `MarketDataProvider.MarketShare` и `IsTopByMarketShare`, `CpuDataProvider.GetAllCpus`, `ResearchSector.TechnologyAsResearch`, `ModTicks.Today` → номер дня, `ModCredit.ThresholdOf` → `DefaultThreshold`, `ModTicks.IsBatchRun` → `InBatch`);
    - `mod/src/PTMod/Features/EventSystem/Game/CompanyDirectory.cs` (обёртка над `ModCompanies.All` / `KeyOf` / `Resolve`).
  - **Acceptance**: фасад не бросает при `Reports.Count < 3` (возвращает 0); каталог исключает банкротов; нет вызовов `DataFinder.FindAllCompanies`; `IsTopByMarketShare` считает только ИИ (игрок не входит в рейтинг)
  - **Depends**: mod-framework/TSK-024 (`ModTicks`), mod-framework/TSK-026 (`ModCompanies`), mod-framework/TSK-057 (`ModTicks.IsBatchRun`), mod-framework/TSK-058 (`ModCredit`)

- [ ] **TSK-005**: Условия событий
  - **Requirement**: FR-2, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Conditions/EventConditions.cs` (фасад `Register` / `Parse` / `Evaluate` над `ModConditions`), `BuiltinPredicates.cs` (`es.eventFired`, `es.choiceMade`, `es.eventFlag`, `es.activeInCategory` со `scope` = `subject` | `any`, `es.hasRevenueHistory`, `es.hasFab`)
  - **Acceptance**: предикаты регистрируются в `ModConditions` с владельцем `event-system` и именами `es.*`; подлежащее — компания-цель; `es.activeInCategory` с `scope: "any"` видит ожидающие выборы и активные эффекты любой компании и глобальные эффекты категории (AC-6.5); синтаксис `all` / `any` / `not` работает в `conditions`, `filter`, `requires`; неизвестный тип — ошибка валидации записи; регистрация без префикса отклоняется
  - **Depends**: TSK-004; mod-framework/TSK-019, mod-framework/TSK-032 (`ModConditions`), mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-006**: Фича, планировщик дня и триггеры
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/EventSystemFeature.cs` (`[ModFeature]`, `Id = "event-system"`, `NameKey` / `DescriptionKey`, `DependsOn` пуст, `EnabledByDefault = true`, `SupportsHotToggle = true`, `Register(FeatureContext)` по списку из `design.md`: `ctx.SaveParticipant`, `ctx.OnTick(TickPhase.DayPassed, …)`, `ctx.Param`, `ctx.HubPage`, регистрация API, подписки на `ModTicks.GameReady`, `ModTicks.BatchEnded`, `ModMail.Delivered`, `ModFeatures.StateChanged`), `mod/src/PTMod/Features/EventSystem/Scheduling/EventScheduler.cs`, `Pacing.cs`
  - **Acceptance**:
    - порядок шагов дня — как в FR-2.1;
    - `Date` с `graceDays`, `Condition` (уровень, фронт, `checkEvery`), `Manual`;
    - откат по паре (событие, компания), для `AllCompanies` — по событию; `oncePerGame`;
    - `minGapDays`, `maxActive`, `maxPendingChoices` с очередью до 30 дней;
    - у фичи нет ни одного `ctx.Patch`;
    - тесты фичи собираются без Unity (регистрация за фейком `FeatureContext`).
  - **Depends**: TSK-002, TSK-005, TSK-008; mod-framework/TSK-020, mod-framework/TSK-022, mod-framework/TSK-024

- [ ] **TSK-007**: Выбор целей и групп
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Scheduling/TargetSelector.cs`
  - **Acceptance**: `Player`, `RandomAI`, `RandomCompany`, `AllCompanies` (одна группа, глобальные эффекты один раз), `Specific` (пропуск без расхода отката); веса `Uniform` / `FactoryCapacity` / `MarketShare` / `QuarterlyRevenue` по формулам `design.md`; правила одинаковы для игрока и ИИ
  - **Depends**: TSK-004, TSK-005

- [ ] **TSK-008**: Детерминированные потоки ГСЧ
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Scheduling/EventRandom.cs` (дневной поток `ModRandom.Stream("event-system/daily")`; `DerivedRng : IModRng` — xoshiro128** с сидом `hash32(ModRandom.WorldSeed, ключ)`)
  - **Acceptance**: лотерея категории всегда берёт 3 числа; производные генераторы с одним сидом дают одну последовательность; в коде фичи нет `UnityEngine.Random` и `System.Random` (проверка в TSK-026)
  - **Depends**: mod-framework/TSK-012, mod-framework/TSK-025 (`ModRandom`)

- [ ] **TSK-009**: Тесты планировщика, целей и детерминизма
  - **Requirement**: FR-2, FR-3, FR-9
  - **Deliverables**: `mod/tests/PTMod.Tests/EventSystem/CategoryLotteryTests.cs`, `TriggerTests.cs`, `TargetSelectorTests.cs`, `DeterminismTests.cs`, `PredicateTests.cs`
  - **Acceptance**: AC-2.1 (401 ± 60 на 36 500 дней), AC-2.2, AC-2.3, AC-3.1 (20 % ± 2 %), AC-3.2, AC-9.2, AC-6.5 — зелёные
  - **Depends**: TSK-006, TSK-007, TSK-008; mod-framework/TSK-067

## Фаза 3 — Эффекты

- [ ] **TSK-010**: Резолвер и жизненный цикл эффектов
  - **Requirement**: FR-5, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Effects/IEventEffectHandler.cs`, `AppliedEffect.cs`, `EffectResolver.cs`, `InstanceFactory.cs`
  - **Acceptance**:
    - `Resolve` вызывается один раз на эффект при создании экземпляра (все варианты + `common`);
    - шанс и эксклюзивные группы — по формулам `design.md`;
    - `Severity` применяется к эффектам с `scalable`, и итог зажимается пределами канала (`IEffectModifiers.LimitMin` / `LimitMax`) с пометкой «ограничено» (AC-5.5);
    - исключение в обработчике → `Failed` без остановки дня;
    - `Describe` берёт числа только из `AppliedEffect`.
  - **Depends**: TSK-001, TSK-008

- [ ] **TSK-011**: Агрегатор модификаторов и временные эффекты
  - **Requirement**: FR-5, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Effects/ModifierAggregator.cs`, `Handlers/ModifierTimedEffect.cs`, `Handlers/FactoryOutputEffect.cs`, `Handlers/MarketDemandEffect.cs`
  - **Acceptance**:
    - `ModifierAggregator` реализует публичный `IEffectModifiers` (`Set` / `Clear` / `WasClamped` / `LimitMin` / `LimitMax`) для обработчиков паков;
    - по одному провайдеру `ModModifiers.RegisterReasonedProvider("event-system", channel, …)` на используемый канал, регистрация один раз, причина `es.reason.events` с названиями активных событий для `ModModifiers.Explain`;
    - каждое изменение вклада вызывает `ModModifiers.Invalidate(channel, companyKey)` (AC-5.4);
    - пределы `channelLimits` (самый узкий шаблон), в том числе `popularity.gain` [0,7; 1,6]; `Suspend(packId)` / `Resume`; `ClearAll`;
    - `OnDisabled` провайдеры и вклады не удаляет;
    - AC-5.2 (×1.232 после `Invalidate`).
  - **Depends**: TSK-010; mod-framework/TSK-017, mod-framework/TSK-027, mod-framework/TSK-059 (`ModModifiers`: провайдеры с причиной, `Invalidate`, ленивое связывание), mod-framework/TSK-043 (реестр каналов)

- [ ] **TSK-012**: Разовые эффекты: деньги, популярность, репутация, исследование
  - **Requirement**: FR-5, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Effects/Handlers/MoneyEffect.cs`, `PopularityEffect.cs`, `ReputationEffect.cs`, `ResearchProgressEffect.cs`
  - **Acceptance**:
    - `Money`: формула `round_to_1000(clamp(f × QR, min × I, max × I) × s)`, вызов `ICompany.SpendMoney` / `ICompany.EarnMoney` с `origin` из параметров; `Earn` без `origin` — `RevenueOrigin.Interest`;
    - `Popularity`: ограничение [0,05; 1,0];
    - `ResearchProgress`: потолок 0,99, без исследования — `Skipped`;
    - `Reputation` через `ModReputation.Add` (причина — ключ `es.*`).
  - **Depends**: TSK-010, TSK-004; mod-framework/TSK-026 (`ModReputation`)

- [ ] **TSK-013**: Письма-эффекты, цепочки и флаги
  - **Requirement**: FR-5, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Effects/Handlers/SendMailEffect.cs`, `FireEventEffect.cs`, `mod/src/PTMod/Features/EventSystem/Scheduling/ScheduledFires.cs`, `ChainFlags.cs`
  - **Acceptance**: `FireEvent` ставит срабатывание на `today + delay`; `target: Same/Player/Definition`; глубина цепочки ≤ 8; самовызов с задержкой 0 отклоняется валидатором; `SendMail` — `ModMail.Send` без вариантов, тег `es:<…>`, приоритет `Normal`
  - **Depends**: TSK-010, TSK-006; mod-framework/TSK-037, mod-framework/TSK-062 (`ModMail`)

- [ ] **TSK-014**: Публичный API: EventBus, EventEffects, EventAi
  - **Requirement**: FR-6, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Api/EventBus.cs`, `EventEffects.cs` (вкл. `ApplyDirect`, `ICurveEffectHandler`), `EventAi.cs` (`AddChooser`, `RemoveChooser`, `IEventAiChooserFilter`), `PackOptions.cs`
  - **Acceptance**: `EventBus.Fire` возвращает `Fired` / `Scheduled` / `Skipped` / `UnknownEvent` / `Disabled`; подписчики `OnEventFired`, `OnChoiceResolved`, `OnEffectExpired` изолированы `try/catch` (AC-6.2); повторная регистрация типа — предупреждение, первая остаётся; `ApplyDirect` создаёт экземпляр `Direct`, эффект сохраняется и истекает как обычный (AC-6.3); `EventAi.AddChooser` ведёт список по убыванию приоритета, первый ненулевой ответ побеждает, исключение chooser'а = `null` (AC-6.4)
  - **Depends**: TSK-006, TSK-010

- [ ] **TSK-015**: Тесты эффектов
  - **Requirement**: FR-5, FR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/EventSystem/MoneyEffectTests.cs`, `ModifierAggregatorTests.cs`, `ChanceGroupTests.cs`, `ChainTests.cs`
  - **Acceptance**: AC-5.1 (сумма в письме = сумма в `SpendMoney`), AC-5.2, AC-5.3, AC-5.4, AC-5.5, AC-6.1 (регистрация тестового типа), AC-6.3 (`ApplyDirect` на логике) — зелёные; пример из `design.md` (1990, QR $20M, f = 0,052 → −$1 040 000) воспроизводится; `Invalidate` вызывается на каждое изменение вклада
  - **Depends**: TSK-011, TSK-012, TSK-013, TSK-014; mod-framework/TSK-067

## Фаза 4 — Выборы и UI

- [ ] **TSK-016**: Выборы, таймеры и решения ИИ
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Choices/ChoiceController.cs`, `DefaultAiChooser.cs`, `ChoiceAvailability.cs` (требования варианта, порог дефолта)
  - **Acceptance**:
    - дедлайн от доставки письма (`max(firedDay, deliveredDay) + timerDays`, только вперёд), экземпляр с недоставленным письмом не истекает; `defaultChoice` (AC-4.1, AC-4.5);
    - предупреждение о минусе без блокировки (AC-4.2); блокировка варианта, если списание опустило бы баланс ниже `ModCredit.ThresholdOf`, `defaultChoice` не блокируется (AC-4.6);
    - ИИ решает в день срабатывания (AC-4.3) через цепочку `EventAi` и политику по умолчанию `aiWeight` + `aiMinCashMultiple`;
    - вариант с ложным `requires` серый в письме (`Enabled = false`, `DisabledReasonKey`) и в окне; ежедневный пересчёт `SetChoiceState` + `Refresh`; ставший ложным между пересчётами → `defaultChoice` (AC-4.4);
    - отмена при банкротстве цели; повторный ответ игнорируется.
  - **Depends**: TSK-010, TSK-014; mod-framework/TSK-038 (`ModMail.SetChoiceState`, `Refresh`), mod-framework/TSK-058 (`ModCredit`), mod-framework/TSK-062 (`ModMail.Delivered`)

- [ ] **TSK-017**: Письмо с вариантами, строка таймера, уведомления, напоминание
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/UI/EventUiController.cs`, `EventMailFormatter.cs`
  - **Acceptance**:
    - письмо: `ModMail.Send` с `Tag = "es:<instanceId>"`, `Priority` (`Critical` — выбор для игрока, `Normal` — событие без выбора, `Low` — новость об ИИ), `TimeoutDays = 0`, заголовком, причиной, вариантами с эффектами;
    - по `ModMail.Delivered` (поиск экземпляра по `Tag`, не по `mailId`): дедлайн от дня доставки и `ModMail.SetStatusLine` «Ответить до …»; затем ежедневно «Осталось N из M дн.» (AC-7.2);
    - ответ — через `ModMail.RegisterChoiceHandler`; автоматическое решение и отмена — `ModMail.Resolve`;
    - уведомления `ModNotify.ShowKey`: новое событие, напоминание за 3 дня, «Выбрано автоматически»;
    - новости по `notifyPlayer` с учётом параметра `rivalNews` и `PackOptions.RivalNewsTopN` (AC-7.4).
  - **Depends**: TSK-016; mod-framework/TSK-033 (`ModNotify`), mod-framework/TSK-037, mod-framework/TSK-038, mod-framework/TSK-061, mod-framework/TSK-062 (`ModMail`), mod-framework/TSK-013 (`ModL10n`)

- [ ] **TSK-018**: Модальное окно выбора с обратным отсчётом
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/UI/EventChoiceWindow.cs`
  - **Acceptance**:
    - окно `ModUI.CreateWindow(..., WindowTemplate.Modal, ...)` с паузой (`PauseTrigger` шаблона);
    - строка «Осталось N из M дн.» с полосой делений обновляется по дням;
    - кнопки вариантов с подсказкой эффектов (`ModUI.Tooltip`), недоступные неактивны с причиной; кнопка «Решить позже»;
    - очередь окон по порядку срабатывания; во время пакетного прогона (`ModTicks.IsBatchRun`) окна откладываются до `ModTicks.BatchEnded`;
    - AC-7.1 проверяется вручную (M6).
  - **Depends**: TSK-017; mod-framework/TSK-034, mod-framework/TSK-035, mod-framework/TSK-036 (`ModUI`), mod-framework/TSK-064 (`ModUI.Tooltip`), mod-framework/TSK-057 (`ModTicks.BatchEnded`)

- [ ] **TSK-019**: Журнал событий и окно журнала
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Log/EventLog.cs`, `mod/src/PTMod/Features/EventSystem/UI/EventLogWindow.cs`
  - **Acceptance**: окно `WindowTemplate.Panel`; вкладки «История» / «Активные эффекты»; фильтр «Только моя компания»; лимит 400 (AC-8.2); перерисовка по `ModL10n.LanguageChanged`; строка из AC-8.1 совпадает по формату
  - **Depends**: TSK-010, TSK-016; mod-framework/TSK-035, mod-framework/TSK-036 (`ModUI`), mod-framework/TSK-013 (`ModL10n`)

- [ ] **TSK-020**: Тесты выборов и текстов
  - **Requirement**: FR-4, FR-6, FR-8, NFR-5
  - **Deliverables**: `mod/tests/PTMod.Tests/EventSystem/ChoiceTimerTests.cs`, `AiChooserTests.cs`, `DescribeTests.cs`
  - **Acceptance**: AC-4.1–AC-4.6, AC-6.4 (два chooser'а), AC-8.2, AC-13.2 (пакетный прогон не уменьшает таймер) — зелёные; дедлайн от доставки и недоставленное письмо; смена языка не меняет числа в описаниях
  - **Depends**: TSK-016, TSK-019; mod-framework/TSK-067

## Фаза 5 — Сохранение, защиты, настройки

- [ ] **TSK-021**: Сохранение и загрузка состояния
  - **Requirement**: FR-10, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Persistence/EventSystemSaveDto.cs`, `EventPersistence.cs` (`IModSaveParticipant`, `SectionId = "event-system"`, `SchemaVersion = 1`; без собственного поля версии в данных), `mod/src/PTMod/Features/EventSystem/EventRuntime.cs` (`AfterLoad`, `ResetForNewGame` по `ModTicks.GameReady`)
  - **Acceptance**:
    - секция `event-system` по примеру `design.md` (без `_schema`: его пишет ядро);
    - `Read` разбирает и проверяет DTO и сбрасывает вклады прошлого сейва; в `GameReady(Loaded)`: отмена неизвестных событий (`ModMail.Resolve`, журнал) → `Restore` активных → связь с письмами по `Tag` (`ModMail.Find`) → пересчёт `SetChoiceState` / `SetStatusLine` (AC-10.3);
    - старый сейв без секции — `ResetToDefaults`;
    - `GameReady(NewGame)` сбрасывает состояние.
  - **Depends**: TSK-011, TSK-016; mod-framework/TSK-016, mod-framework/TSK-025 (`ModSave`), mod-framework/TSK-037, mod-framework/TSK-062 (`ModMail.Find`)

- [ ] **TSK-022**: Защиты времени и пакетный прогон
  - **Requirement**: FR-13
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Runtime/BatchGuard.cs` (чтение `ModTicks.IsBatchRun`, сдвиг дедлайнов игрока на +1 за день прогона, очередь окон до `ModTicks.BatchEnded`), правки `ChoiceController`. Файла патча на `DateController.NextMonth` нет
  - **Acceptance**:
    - в перемотке и загрузке дни не обрабатываются (`ModTicks`);
    - во время `NextMonth` (`IsBatchRun`) таймеры игрока не убывают, окна откладываются до `BatchEnded`, экземпляры с недоставленным письмом не истекают;
    - после загрузки нет догоняющей симуляции;
    - патч фичи на `DateController.NextMonth` в `ModHooks.Report` отсутствует;
    - AC-13.1, AC-13.2, AC-4.5.
  - **Depends**: TSK-016, TSK-018; mod-framework/TSK-024 (`ModTicks`), mod-framework/TSK-057 (`ModTicks.IsBatchRun`, `BatchEnded`), mod-framework/TSK-023 (`ModHooks.Guard`)

- [ ] **TSK-023**: Настройки, выключение посреди игры, отладочный запуск
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Settings/EventSettings.cs` (`ParamSpec`: `frequency`, `pauseAllChoices`, `rivalNews`), `mod/src/PTMod/Features/EventSystem/UI/EventSettingsPage.cs` (`FeatureContext.HubPage`), `EventRuntime.OnFeatureStateChanged`
  - **Acceptance**:
    - параметры из FR-11.1 в хабе «Моды», кнопка «Журнал событий»;
    - `OnDisabled` — заморозка (провайдеры и вклады не удаляются и не пересоздаются, таймеры стоят), `OnEnabled` — неистёкшие эффекты возвращаются, дедлайны выборов игрока сдвигаются на длительность выключения (AC-11.1, AC-11.3);
    - то же для отдельного пака по `ModFeatures.StateChanged` его выключателя (`PackOptions.SettingsFeatureId`), остальные паки не затронуты;
    - список «Запустить сейчас» только при `ModSettings.Debug("debug.events")` в отладочной сборке (AC-11.2).
  - **Depends**: TSK-011, TSK-016; mod-framework/TSK-014 (`ModSettings`), mod-framework/TSK-020, mod-framework/TSK-022 (`ModFeatures`), mod-framework/TSK-039 (хаб)

- [ ] **TSK-024**: Тесты сохранения и совместимости
  - **Requirement**: FR-10, NFR-3, NFR-7
  - **Deliverables**: `mod/tests/PTMod.Tests/EventSystem/PersistenceTests.cs`
  - **Acceptance**: сериализация туда-обратно без потерь (`ModJson`); в данных секции нет поля версии; пустая секция; неизвестное событие отменяется с записью в журнале; вручную испорченные числа (множитель 50, отрицательная длительность) отбрасываются (NFR-7); AC-10.1 на уровне логики (дедлайн и вклад восстановлены)
  - **Depends**: TSK-021; mod-framework/TSK-067

## Фаза 6 — Контент, самопроверка, проверка

- [ ] **TSK-025**: Стартовый набор событий и строки
  - **Requirement**: FR-12, NFR-4, NFR-8
  - **Deliverables**:
    - `mod/src/PTMod/Content/event-system/config.json` (в том числе `channelLimits` с `popularity.gain` [0,7; 1,6]);
    - `mod/src/PTMod/Content/event-system/events.json` — 10 записей из таблицы баланса `design.md`;
    - ключи `es.*` в `mod/src/PTMod/L10n/ru.json` и `mod/src/PTMod/L10n/en.json`;
    - `mod/tests/PTMod.Tests/EventSystem/StarterContentTests.cs`.
  - **Acceptance**: контент проходит валидатор; использованы все стандартные типы, кроме `MarketDemand`; есть выбор с таймером, шанс, эксклюзивная группа, `FireEvent`, цели `Player` / `RandomAI` / `RandomCompany`; условия — предикаты каталога ядра (`dateAtLeast`, `cpuOnMarket`, `researchActive`) и движка с префиксом (`es.hasRevenueHistory`, `es.hasFab`); ключи есть в обоих языках и начинаются с `es.`; в `market.rating.cpu` стартовый набор не пишет
  - **Depends**: TSK-002, TSK-005, TSK-011, TSK-012, TSK-013; mod-framework/TSK-066

- [ ] **TSK-026**: Самопроверка фичи на старте
  - **Requirement**: NFR-2, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/SelfCheck/EventSelfCheck.cs` (вклад в `ModSelfCheck`)
  - **Acceptance**:
    - проверено: типы эффектов зарегистрированы, каналы объявлены в реестре ядра, `FireEvent` ссылается на существующие события, ID событий и предикаты соответствуют префиксам, ключи L10n есть и с верным префиксом;
    - итог одной строкой `selfcheck OK: N events, M effect types` или список проблем;
    - в исходниках фичи нет `UnityEngine.Random` и `System.Random`;
    - проверок целей патчей нет (патчей у фичи нет).
  - **Depends**: TSK-022, TSK-025; mod-framework/TSK-042 (`ModSelfCheck`)

- [ ] **TSK-027**: Замер производительности дня
  - **Requirement**: NFR-1
  - **Deliverables**: отладочный таймер в `EventScheduler.ProcessDay`; отчёт замера (`ModTicks.StatsOf(TickPhase.DayPassed)` и таймер фичи) в разделе «Evidence» этой задачи
  - **Acceptance**: на сейве 2000 года с 13 ИИ, 365 дней: среднее ≤ 0,3 мс, день со срабатыванием ≤ 3 мс без UI
  - **Depends**: TSK-025

- [ ] **TSK-028**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-13
  - **Deliverables**: отметки по сценариям M1–M13 из `design.md` (раздел Testing Strategy) в разделе «Evidence» этой задачи
  - **Acceptance**: все сценарии M1–M13 дали ожидаемый результат; найденные расхождения оформлены задачами
  - **Depends**: TSK-023, TSK-024, TSK-026, TSK-027; mod-framework/TSK-053 (фреймворк проверен в игре)

## Dependency graph

```
mod-framework: ModJson(009,010) ModConditions(019,032) ModContent+ModLog(015,021) ModRandom(012,025)
               ModTicks(024) IsBatchRun(057) ModCompanies+ModReputation(026) ModModifiers(017,027,059) core.json(043)
               ModMail(037,038,061,062) ModNotify(033) ModUI(034-036,064) ModSave(016,025) ModHooks(023)
               ModFeatures(020,022) ModSettings(014) хаб(039) ModCredit(058) ModIds(066) ModSelfCheck(042) раннер(067)

TSK-001 ──> TSK-002 ──> TSK-003
   │           │
   │           ├──────────────────────────────┐
TSK-004 ──> TSK-005 ──> TSK-006 <── TSK-008   │
   │           │           │                  │
   └──> TSK-007 <──────────┘                  │
            └──────> TSK-009 <── TSK-006, TSK-008
TSK-001, TSK-008 ──> TSK-010 ──> TSK-011 ──┐
                        │──> TSK-012 ──────┤
                        │──> TSK-013 ──────┤
                        └──> TSK-014 ──────┴──> TSK-015
TSK-010, TSK-014 ──> TSK-016 ──> TSK-017 ──> TSK-018
                        │──> TSK-019 ──> TSK-020
TSK-011, TSK-016 ──> TSK-021 ──> TSK-024
TSK-016, TSK-018 ──> TSK-022
TSK-011, TSK-016 ──> TSK-023
TSK-002, TSK-005, TSK-011..013 ──> TSK-025 ──> TSK-026 <── TSK-022
                                      └──> TSK-027
TSK-023, TSK-024, TSK-026, TSK-027 ──> TSK-028
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Модель контента | Pending |
| TSK-002 Реестр и валидатор | Pending |
| TSK-003 Тесты валидатора | Pending |
| TSK-004 Фасад и каталог компаний | Pending |
| TSK-005 Условия событий | Pending |
| TSK-006 Фича, планировщик и триггеры | Pending |
| TSK-007 Выбор целей | Pending |
| TSK-008 Потоки ГСЧ | Pending |
| TSK-009 Тесты планировщика | Pending |
| TSK-010 Резолвер эффектов | Pending |
| TSK-011 Агрегатор и временные эффекты | Pending |
| TSK-012 Разовые эффекты | Pending |
| TSK-013 Письма, цепочки, флаги | Pending |
| TSK-014 Публичный API | Pending |
| TSK-015 Тесты эффектов | Pending |
| TSK-016 Выборы и ИИ | Pending |
| TSK-017 Письмо, строка таймера, уведомления | Pending |
| TSK-018 Модальное окно | Pending |
| TSK-019 Журнал событий | Pending |
| TSK-020 Тесты выборов и текстов | Pending |
| TSK-021 Сохранение и загрузка | Pending |
| TSK-022 Защиты времени (пакетный прогон через ядро) | Pending |
| TSK-023 Настройки и выключение | Pending |
| TSK-024 Тесты сохранения | Pending |
| TSK-025 Стартовый контент и строки | Pending |
| TSK-026 Самопроверка | Pending |
| TSK-027 Замер производительности | Pending |
| TSK-028 Ручная проверка (владелец) | Pending |

## Покрытие требований

| Требование | Задачи |
|---|---|
| FR-1 | TSK-001, TSK-002, TSK-003, TSK-006 |
| FR-2 | TSK-005, TSK-006, TSK-009 |
| FR-3 | TSK-004, TSK-007, TSK-009 |
| FR-4 | TSK-004, TSK-014, TSK-016, TSK-020 |
| FR-5 | TSK-010, TSK-011, TSK-012, TSK-013, TSK-015 |
| FR-6 | TSK-001, TSK-005, TSK-013, TSK-014, TSK-015 |
| FR-7 | TSK-017, TSK-018 |
| FR-8 | TSK-019, TSK-020 |
| FR-9 | TSK-008, TSK-009 |
| FR-10 | TSK-021, TSK-024 |
| FR-11 | TSK-023 |
| FR-12 | TSK-012, TSK-025 |
| FR-13 | TSK-022 |
| NFR-1 | TSK-004, TSK-027 |
| NFR-2 | TSK-002, TSK-026 |
| NFR-3 | TSK-021, TSK-024 |
| NFR-4 | TSK-025, TSK-026 |
| NFR-5 | TSK-010, TSK-020 |
| NFR-6 | TSK-014 |
| NFR-7 | TSK-002, TSK-013, TSK-021, TSK-024 |
| NFR-8 | TSK-011, TSK-025 |
