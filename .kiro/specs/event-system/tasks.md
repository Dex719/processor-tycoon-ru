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

Сначала строится чистая логика без Unity: модель контента, планировщик, эффекты, таймеры, сохранение. Она покрывается тестами в `mod/tests/PTMod.Tests/Features/EventSystem/` (свой мини-раннер .NET Framework 4.8, `python mod/build.py --tests`). Затем подключаются сервисы `PTMod.Core` (`ModTicks`, `ModSave`, `ModModifiers`, `ModMail`, `ModUI`, `ModConditions`, `ModCompanies`, `ModCredit`) и UI. В конце — стартовый контент, самопроверка, замер производительности и ручная проверка владельцем.

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

- [x] **TSK-001**: Модель контента и адаптер параметров (авто 2026-10-10)
  - **Requirement**: FR-1, FR-6
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Api/Model/EventDefinition.cs`: `EventDefinition` (`IContentRecord`), `EventTrigger`, `EventTargetSpec`, `EventChoice`, `EventEffect`, перечисления;
    - `mod/src/PTMod/Features/EventSystem/Logic/Model/EffectParams.cs`: `IEffectParams`, `FloatRange`, адаптер над `JsonObject`;
    - `mod/src/PTMod/Features/EventSystem/Logic/Model/DefinitionMapper.cs`: `JsonObject` → `EventDefinition`, условия через `ModConditions.Parse`.
  - **Acceptance**: типы компилируются `csc` (общее подмножество API .NET Framework 4.8 в `Logic`); `IEffectParams.GetRange` принимает число и пару `[lo, hi]`; тексты — `PTMod.Core.LocText`; имена соответствуют контракту из `design.md`
  - **Depends**: mod-framework/TSK-009, mod-framework/TSK-010 (`ModJson`), mod-framework/TSK-019 (`ModConditions.Parse`), mod-framework/TSK-067 (мини-раннер тестов)

- [x] **TSK-002**: Реестр паков и валидатор контента (авто 2026-10-11)
  - **Requirement**: FR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Api/EventRegistry.cs` (`RegisterPack`, `Get`, `All`, индекс категорий; `PackOptions` с `SettingsFeatureId`, `Frequency`, `Severity`, `EventsFile`, `RivalNewsTopN`);
    - `mod/src/PTMod/Features/EventSystem/Logic/Content/ContentValidator.cs` (в том числе `ModIds.IsEventIdAllowed` для префикса пака);
    - `mod/src/PTMod/Features/EventSystem/Logic/Content/EventConfig.cs` (`config.json` каждого пака через `ModContent.LoadConfig`: категории, `channelLimits`, `maxPendingChoices`, `logLimit`; слияние пределов — самый узкий).
  - **Acceptance**:
    - записи грузятся через `ModContent.LoadRecords` один раз, на первом `ModTicks.GameReady` после `Register` всех фич; файл событий пака — `PackOptions.EventsFile` (по умолчанию `events`);
    - битая запись пропускается с `ModLog.Warn` «skip '<id>': <причина>»;
    - ID события чужого префикса отклоняется (AC-1.4);
    - проверки из таблицы параметров `design.md` работают;
    - переопределение из `PTMod/content/<pack>/` подхватывается;
    - категории и пределы из конфигов нескольких паков сливаются.
  - **Depends**: TSK-001; mod-framework/TSK-015, mod-framework/TSK-021 (`ModContent`, `ModLog`), mod-framework/TSK-043 (`core.json`, таблица префиксов), mod-framework/TSK-066 (`ModIds`)
  - **Закрыто** (авто 2026-10-11): склейка сделана в TSK-006 и TSK-014. `EventEngine` создаёт `GamePackCatalog.Create(…)` и `PackLoader`; `EventSystemFeature.SubscribeCoreEvents` подписывает `EventEngine.OnGameReady` на `ModTicks.GameReady`, `LoadPacks` (`PackLoader.EnsureLoaded`) вызывается из `OnEnabled`; `IEventSystemApi.RegisterPack` / `GetDefinition` / `GetDefinitions` делегируются через `EventEngine` в `PackCatalog` (`Api/EventSystemApi.cs`).

- [x] **TSK-003**: Тесты загрузчика и валидатора (авто 2026-10-10)
  - **Requirement**: FR-1
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/EventSystem/ContentValidatorTests.cs`, фикстуры `mod/tests/PTMod.Tests/Features/EventSystem/Fixtures/*.json`
  - **Acceptance**: AC-1.1, AC-1.2, AC-1.4 воспроизведены тестами; дубликат `id`, неизвестный канал, `defaultChoice` с `requires`, Σ шансов группы > 1, предикат без префикса — каждый случай отдельным тестом, все зелёные; публичные типы указаны в `[Covers]`
  - **Depends**: TSK-002; mod-framework/TSK-067

## Фаза 2 — Планировщик, цели, детерминизм

- [x] **TSK-004**: Игровой фасад и каталог компаний (авто 2026-10-10)
  - **Requirement**: FR-3, NFR-1
  - **Deliverables**:
    - `mod/src/PTMod/Features/EventSystem/Api/EventContext.cs`;
    - `mod/src/PTMod/Features/EventSystem/Game/GameFacade.cs` (`InflationHandler.Instance.Inflation`, `CompanyFinance.QuarterlyRevenue` с защитой `Reports.Count < 3`, `MarketDataProvider.MarketShare` и `IsTopByMarketShare`, `CpuDataProvider.GetAllCpus`, `ResearchSector.TechnologyAsResearch`, `ModTicks.Today` → номер дня, `ModCredit.ThresholdOf` → `DefaultThreshold`, `ModTicks.IsBatchRun` → `InBatch`);
    - `mod/src/PTMod/Features/EventSystem/Game/CompanyDirectory.cs` (обёртка над `ModCompanies.All` / `KeyOf` / `Resolve`).
  - **Acceptance**: фасад не бросает при `Reports.Count < 3` (возвращает 0); каталог исключает банкротов; нет вызовов `DataFinder.FindAllCompanies`; `IsTopByMarketShare` считает только ИИ (игрок не входит в рейтинг)
  - **Depends**: mod-framework/TSK-024 (`ModTicks`), mod-framework/TSK-026 (`ModCompanies`), mod-framework/TSK-057 (`ModTicks.IsBatchRun`), mod-framework/TSK-058 (`ModCredit`)

- [x] **TSK-005**: Условия событий (авто 2026-10-10)
  - **Requirement**: FR-2, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Api/EventConditions.cs` (фасад `Register` / `Parse` / `Evaluate` над `ModConditions`), `mod/src/PTMod/Features/EventSystem/Logic/Conditions/BuiltinPredicates.cs` (`es.eventFired`, `es.choiceMade`, `es.eventFlag`, `es.activeInCategory` со `scope` = `subject` | `any`, `es.hasRevenueHistory`, `es.hasFab`)
  - **Acceptance**: предикаты регистрируются в `ModConditions` с владельцем `event-system` и именами `es.*`; подлежащее — компания-цель; `es.activeInCategory` с `scope: "any"` видит ожидающие выборы и активные эффекты любой компании и глобальные эффекты категории (AC-6.5); синтаксис `all` / `any` / `not` работает в `conditions`, `filter`, `requires`; неизвестный тип — ошибка валидации записи; регистрация без префикса отклоняется
  - **Depends**: TSK-004; mod-framework/TSK-019, mod-framework/TSK-032 (`ModConditions`), mod-framework/TSK-066 (`ModIds`)

- [x] **TSK-006**: Фича, планировщик дня и триггеры (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/EventSystemFeature.cs` (`[ModFeature]`, `Id = "event-system"`, `NameKey` / `DescriptionKey`, `DependsOn` пуст, `EnabledByDefault = true`, `SupportsHotToggle = true`, `Register(FeatureContext)` по списку из `design.md`: `ctx.SaveParticipant`, `ctx.OnTick(TickPhase.DayPassed, …)`, `ctx.Param`, `ctx.HubPage`, регистрация API, подписки на `ModTicks.GameReady`, `ModTicks.BatchEnded`, `ModMail.Delivered`, `ModFeatures.StateChanged`), `mod/src/PTMod/Features/EventSystem/Logic/Scheduling/EventScheduler.cs`, `Pacing.cs`
  - **Acceptance**:
    - порядок шагов дня — как в FR-2.1;
    - `Date` с `graceDays`, `Condition` (уровень, фронт, `checkEvery`), `Manual`;
    - откат по паре (событие, компания), для `AllCompanies` — по событию; `oncePerGame`;
    - `minGapDays`, `maxActive`, `maxPendingChoices` с очередью до 30 дней;
    - у фичи нет ни одного `ctx.Patch`;
    - тесты фичи собираются без Unity (регистрация за фейком `FeatureContext`).
  - **Depends**: TSK-002, TSK-005, TSK-008; mod-framework/TSK-020, mod-framework/TSK-022, mod-framework/TSK-024

- [x] **TSK-007**: Выбор целей и групп (авто 2026-10-10)
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Scheduling/TargetSelector.cs`
  - **Acceptance**: `Player`, `RandomAI`, `RandomCompany`, `AllCompanies` (одна группа, глобальные эффекты один раз), `Specific` (пропуск без расхода отката); веса `Uniform` / `FactoryCapacity` / `MarketShare` / `QuarterlyRevenue` по формулам `design.md`; правила одинаковы для игрока и ИИ
  - **Depends**: TSK-004, TSK-005

- [x] **TSK-008**: Детерминированные потоки ГСЧ (авто 2026-10-10)
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Scheduling/EventRandom.cs` (дневной поток `ModRandom.Stream("event-system/daily")`; `DerivedRng : IModRng` — xoshiro128** с сидом `hash32(ModRandom.WorldSeed, ключ)`)
  - **Acceptance**: лотерея категории всегда берёт 3 числа; производные генераторы с одним сидом дают одну последовательность; в коде фичи нет `UnityEngine.Random` и `System.Random` (проверка в TSK-026)
  - **Depends**: mod-framework/TSK-012, mod-framework/TSK-025 (`ModRandom`)

- [x] **TSK-009**: Тесты планировщика, целей и детерминизма (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3, FR-9
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/EventSystem/CategoryLotteryTests.cs`, `TriggerTests.cs`, `TargetSelectorTests.cs`, `DeterminismTests.cs`, `PredicateTests.cs`
  - **Acceptance**: AC-2.1 (401 ± 60 на 36 500 дней), AC-2.2, AC-2.3, AC-3.1 (20 % ± 2 %), AC-3.2, AC-9.2, AC-6.5 — зелёные
  - **Depends**: TSK-006, TSK-007, TSK-008; mod-framework/TSK-067

## Фаза 3 — Эффекты

- [x] **TSK-010**: Резолвер и жизненный цикл эффектов (авто 2026-10-10)
  - **Requirement**: FR-5, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Api/EventEffectHandlers.cs`, `mod/src/PTMod/Features/EventSystem/Api/Model/EventInstanceDto.cs` (в нём `AppliedEffect`), `mod/src/PTMod/Features/EventSystem/Logic/Effects/EffectResolver.cs`, `mod/src/PTMod/Features/EventSystem/Logic/Effects/InstanceFactory.cs`
  - **Acceptance**:
    - `Resolve` вызывается один раз на эффект при создании экземпляра (все варианты + `common`);
    - шанс и эксклюзивные группы — по формулам `design.md`;
    - `Severity` применяется к эффектам с `scalable`, и итог зажимается пределами канала (`IEffectModifiers.LimitMin` / `LimitMax`) с пометкой «ограничено» (AC-5.5);
    - исключение в обработчике → `Failed` без остановки дня;
    - `Describe` берёт числа только из `AppliedEffect`.
  - **Depends**: TSK-001, TSK-008

- [x] **TSK-011**: Агрегатор модификаторов и временные эффекты (авто 2026-10-10)
  - **Requirement**: FR-5, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Effects/ModifierAggregator.cs`, `Handlers/ModifierTimedEffect.cs`, `Handlers/FactoryOutputEffect.cs`, `Handlers/MarketDemandEffect.cs`
  - **Acceptance**:
    - `ModifierAggregator` реализует публичный `IEffectModifiers` (`Set` / `Clear` / `WasClamped` / `LimitMin` / `LimitMax`) для обработчиков паков;
    - по одному провайдеру `ModModifiers.RegisterReasonedProvider("event-system", channel, …)` на используемый канал, регистрация один раз, причина `es.reason.events` с названиями активных событий для `ModModifiers.Explain`;
    - каждое изменение вклада вызывает `ModModifiers.Invalidate(channel, companyKey)` (AC-5.4);
    - пределы `channelLimits` (самый узкий шаблон), в том числе `popularity.gain` [0,7; 1,6]; `Suspend(packId)` / `Resume`; `ClearAll`;
    - `OnDisabled` провайдеры и вклады не удаляет;
    - AC-5.2 (×1.232 после `Invalidate`).
  - **Depends**: TSK-010; mod-framework/TSK-017, mod-framework/TSK-027, mod-framework/TSK-059 (`ModModifiers`: провайдеры с причиной, `Invalidate`, ленивое связывание), mod-framework/TSK-043 (реестр каналов)

- [x] **TSK-012**: Разовые эффекты: деньги, популярность, репутация, исследование (авто 2026-10-10)
  - **Requirement**: FR-5, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Effects/Handlers/MoneyEffect.cs`, `PopularityEffect.cs`, `ReputationEffect.cs`, `ResearchProgressEffect.cs`
  - **Acceptance**:
    - `Money`: формула `round_to_1000(clamp(f × QR, min × I, max × I) × s)`, вызов `ICompany.SpendMoney` / `ICompany.EarnMoney` с `origin` из параметров; `Earn` без `origin` — `RevenueOrigin.Interest`;
    - `Popularity`: ограничение [0,05; 1,0];
    - `ResearchProgress`: потолок 0,99, без исследования — `Skipped`;
    - `Reputation` через `ModReputation.Add` (причина — ключ `es.*`).
  - **Depends**: TSK-010, TSK-004; mod-framework/TSK-026 (`ModReputation`)

- [x] **TSK-013**: Письма-эффекты, цепочки и флаги (авто 2026-10-10)
  - **Requirement**: FR-5, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Effects/Handlers/SendMailEffect.cs`, `FireEventEffect.cs`, `mod/src/PTMod/Features/EventSystem/Logic/Scheduling/ScheduledFires.cs`, `ChainFlags.cs`
  - **Acceptance**: `FireEvent` ставит срабатывание на `today + delay`; `target: Same/Player/Definition`; глубина цепочки ≤ 8; самовызов с задержкой 0 отклоняется валидатором; `SendMail` — `ModMail.Send` без вариантов, тег `es:<…>`, приоритет `Normal`
  - **Depends**: TSK-010, TSK-006; mod-framework/TSK-037, mod-framework/TSK-062 (`ModMail`)

- [x] **TSK-014**: Публичный API: EventBus, EventEffects, EventAi (авто 2026-10-10)
  - **Requirement**: FR-6, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Api/EventBus.cs`, `EventEffects.cs` (вкл. `ApplyDirect`, `ICurveEffectHandler`), `EventAi.cs` (`AddChooser`, `RemoveChooser`, `IEventAiChooserFilter`), `mod/src/PTMod/Features/EventSystem/Api/Model/PackOptions.cs`
  - **Acceptance**: `EventBus.Fire` возвращает `Fired` / `Scheduled` / `Skipped` / `UnknownEvent` / `Disabled`; подписчики `OnEventFired`, `OnChoiceResolved`, `OnEffectExpired` изолированы `try/catch` (AC-6.2); повторная регистрация типа — предупреждение, первая остаётся; `ApplyDirect` создаёт экземпляр `Direct`, эффект сохраняется и истекает как обычный (AC-6.3); `EventAi.AddChooser` ведёт список по убыванию приоритета, первый ненулевой ответ побеждает, исключение chooser'а = `null` (AC-6.4)
  - **Depends**: TSK-006, TSK-010

- [x] **TSK-015**: Тесты эффектов (авто 2026-10-10)
  - **Requirement**: FR-5, FR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/EventSystem/MoneyEffectTests.cs`, `ModifierAggregatorTests.cs`, `ChanceGroupTests.cs`, `ChainTests.cs`
  - **Acceptance**: AC-5.1 (сумма в письме = сумма в `SpendMoney`), AC-5.2, AC-5.3, AC-5.4, AC-5.5, AC-6.1 (регистрация тестового типа), AC-6.3 (`ApplyDirect` на логике) — зелёные; пример из `design.md` (1990, QR $20M, f = 0,052 → −$1 040 000) воспроизводится; `Invalidate` вызывается на каждое изменение вклада
  - **Depends**: TSK-011, TSK-012, TSK-013, TSK-014; mod-framework/TSK-067

## Фаза 4 — Выборы и UI

- [x] **TSK-016**: Выборы, таймеры и решения ИИ (авто 2026-10-10)
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Choices/ChoiceController.cs`, `DefaultAiChooser.cs`, `ChoiceAvailability.cs` (требования варианта, порог дефолта)
  - **Acceptance**:
    - дедлайн от доставки письма (`max(firedDay, deliveredDay) + timerDays`, только вперёд), экземпляр с недоставленным письмом не истекает; `defaultChoice` (AC-4.1, AC-4.5);
    - предупреждение о минусе без блокировки (AC-4.2); блокировка варианта, если списание опустило бы баланс ниже `ModCredit.ThresholdOf`, `defaultChoice` не блокируется (AC-4.6);
    - ИИ решает в день срабатывания (AC-4.3) через цепочку `EventAi` и политику по умолчанию `aiWeight` + `aiMinCashMultiple`;
    - вариант с ложным `requires` серый в письме (`Enabled = false`, `DisabledReasonKey`) и в окне; ежедневный пересчёт `SetChoiceState` + `Refresh`; ставший ложным между пересчётами → `defaultChoice` (AC-4.4);
    - отмена при банкротстве цели; повторный ответ игнорируется.
  - **Depends**: TSK-010, TSK-014; mod-framework/TSK-038 (`ModMail.SetChoiceState`, `Refresh`), mod-framework/TSK-058 (`ModCredit`), mod-framework/TSK-062 (`ModMail.Delivered`)

- [x] **TSK-017**: Письмо с вариантами, строка таймера, уведомления, напоминание (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/UI/EventUiController.cs`, `EventMailFormatter.cs`
  - **Acceptance**:
    - письмо: `ModMail.Send` с `Tag = "es:<instanceId>"`, `Priority` (`Critical` — выбор для игрока, `Normal` — событие без выбора, `Low` — новость об ИИ), `TimeoutDays = 0`, заголовком, причиной, вариантами с эффектами;
    - по `ModMail.Delivered` (поиск экземпляра по `Tag`, не по `mailId`): дедлайн от дня доставки и `ModMail.SetStatusLine` «Ответить до …»; затем ежедневно «Осталось N из M дн.» (AC-7.2);
    - ответ — через `ModMail.RegisterChoiceHandler`; автоматическое решение и отмена — `ModMail.Resolve`;
    - уведомления `ModNotify.ShowKey`: новое событие, напоминание за 3 дня, «Выбрано автоматически»;
    - новости по `notifyPlayer` с учётом параметра `rivalNews` и `PackOptions.RivalNewsTopN` (AC-7.4).
  - **Depends**: TSK-016; mod-framework/TSK-033 (`ModNotify`), mod-framework/TSK-037, mod-framework/TSK-038, mod-framework/TSK-061, mod-framework/TSK-062 (`ModMail`), mod-framework/TSK-013 (`ModL10n`)

- [x] **TSK-018**: Модальное окно выбора с обратным отсчётом (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/UI/EventChoiceWindow.cs`
  - **Acceptance**:
    - окно `ModUI.CreateWindow(..., WindowTemplate.Modal, ...)` с паузой (`PauseTrigger` шаблона);
    - строка «Осталось N из M дн.» с полосой делений обновляется по дням;
    - кнопки вариантов с подсказкой эффектов (`ModUI.Tooltip`), недоступные неактивны с причиной; кнопка «Решить позже»;
    - очередь окон по порядку срабатывания; во время пакетного прогона (`ModTicks.IsBatchRun`) окна откладываются до `ModTicks.BatchEnded`;
    - AC-7.1 проверяется вручную (M6).
  - **Depends**: TSK-017; mod-framework/TSK-034, mod-framework/TSK-035, mod-framework/TSK-036 (`ModUI`), mod-framework/TSK-064 (`ModUI.Tooltip`), mod-framework/TSK-057 (`ModTicks.BatchEnded`)

- [x] **TSK-019**: Журнал событий и окно журнала (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Api/EventLog.cs`, `mod/src/PTMod/Features/EventSystem/UI/EventLogWindow.cs`
  - **Acceptance**: окно `WindowTemplate.Panel`; вкладки «История» / «Активные эффекты»; фильтр «Только моя компания»; лимит 400 (AC-8.2); перерисовка по `ModL10n.LanguageChanged`; строка из AC-8.1 совпадает по формату
  - **Depends**: TSK-010, TSK-016; mod-framework/TSK-035, mod-framework/TSK-036 (`ModUI`), mod-framework/TSK-013 (`ModL10n`)

- [x] **TSK-020**: Тесты выборов и текстов (авто 2026-10-10)
  - **Requirement**: FR-4, FR-6, FR-8, NFR-5
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/EventSystem/ChoiceTimerTests.cs`, `AiChooserTests.cs`, `DescribeTests.cs`
  - **Acceptance**: AC-4.1–AC-4.6, AC-6.4 (два chooser'а), AC-8.2, AC-13.2 (пакетный прогон не уменьшает таймер) — зелёные; дедлайн от доставки и недоставленное письмо; смена языка не меняет числа в описаниях
  - **Depends**: TSK-016, TSK-019; mod-framework/TSK-067

## Фаза 5 — Сохранение, защиты, настройки

- [x] **TSK-021**: Сохранение и загрузка состояния (авто 2026-10-10)
  - **Requirement**: FR-10, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Persistence/EventSystemSaveDto.cs`, `mod/src/PTMod/Features/EventSystem/Persistence/EventPersistence.cs` (`IModSaveParticipant`, `SectionId = "event-system"`, `SchemaVersion = 1`; без собственного поля версии в данных), `mod/src/PTMod/Features/EventSystem/Logic/Runtime/EventRuntime.cs` (`AfterLoad`, `ResetForNewGame` по `ModTicks.GameReady`)
  - **Acceptance**:
    - секция `event-system` по примеру `design.md` (без `_schema`: его пишет ядро);
    - `Read` разбирает и проверяет DTO и сбрасывает вклады прошлого сейва; в `GameReady(Loaded)`: отмена неизвестных событий (`ModMail.Resolve`, журнал) → `Restore` активных → связь с письмами по `Tag` (`ModMail.Find`) → пересчёт `SetChoiceState` / `SetStatusLine` (AC-10.3);
    - старый сейв без секции — `ResetToDefaults`;
    - `GameReady(NewGame)` сбрасывает состояние.
  - **Depends**: TSK-011, TSK-016; mod-framework/TSK-016, mod-framework/TSK-025 (`ModSave`), mod-framework/TSK-037, mod-framework/TSK-062 (`ModMail.Find`)

- [x] **TSK-022**: Защиты времени и пакетный прогон (авто 2026-10-10)
  - **Requirement**: FR-13
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Runtime/BatchGuard.cs` (чтение `ModTicks.IsBatchRun`, сдвиг дедлайнов игрока на +1 за день прогона, очередь окон до `ModTicks.BatchEnded`), правки `ChoiceController`. Файла патча на `DateController.NextMonth` нет
  - **Acceptance**:
    - в перемотке и загрузке дни не обрабатываются (`ModTicks`);
    - во время `NextMonth` (`IsBatchRun`) таймеры игрока не убывают, окна откладываются до `BatchEnded`, экземпляры с недоставленным письмом не истекают;
    - после загрузки нет догоняющей симуляции;
    - патч фичи на `DateController.NextMonth` в `ModHooks.Report` отсутствует;
    - AC-13.1, AC-13.2, AC-4.5.
  - **Depends**: TSK-016, TSK-018; mod-framework/TSK-024 (`ModTicks`), mod-framework/TSK-057 (`ModTicks.IsBatchRun`, `BatchEnded`), mod-framework/TSK-023 (`ModHooks.Guard`)

- [x] **TSK-023**: Настройки, выключение посреди игры, отладочный запуск (авто 2026-10-10)
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/Settings/EventSettings.cs` (`ParamSpec`: `frequency`, `pauseAllChoices`, `rivalNews`), `mod/src/PTMod/Features/EventSystem/UI/EventSettingsPage.cs` (`FeatureContext.HubPage`), `EventRuntime.OnFeatureStateChanged`
  - **Acceptance**:
    - параметры из FR-11.1 в хабе «Моды», кнопка «Журнал событий»;
    - `OnDisabled` — заморозка (провайдеры и вклады не удаляются и не пересоздаются, таймеры стоят), `OnEnabled` — неистёкшие эффекты возвращаются, дедлайны выборов игрока сдвигаются на длительность выключения (AC-11.1, AC-11.3);
    - то же для отдельного пака по `ModFeatures.StateChanged` его выключателя (`PackOptions.SettingsFeatureId`), остальные паки не затронуты;
    - список «Запустить сейчас» только при `ModSettings.Debug("debug.events")` в отладочной сборке (AC-11.2).
  - **Depends**: TSK-011, TSK-016; mod-framework/TSK-014 (`ModSettings`), mod-framework/TSK-020, mod-framework/TSK-022 (`ModFeatures`), mod-framework/TSK-039 (хаб)

- [x] **TSK-024**: Тесты сохранения и совместимости (авто 2026-10-10)
  - **Requirement**: FR-10, NFR-3, NFR-7
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/EventSystem/PersistenceTests.cs`
  - **Acceptance**: сериализация туда-обратно без потерь (`ModJson`); в данных секции нет поля версии; пустая секция; неизвестное событие отменяется с записью в журнале; вручную испорченные числа (множитель 50, отрицательная длительность) отбрасываются (NFR-7); AC-10.1 на уровне логики (дедлайн и вклад восстановлены)
  - **Depends**: TSK-021; mod-framework/TSK-067

## Фаза 6 — Контент, самопроверка, проверка

- [x] **TSK-025**: Стартовый набор событий и строки (авто 2026-10-10)
  - **Requirement**: FR-12, NFR-4, NFR-8
  - **Deliverables**:
    - `mod/src/PTMod/Content/event-system/config.json` (в том числе `channelLimits` с `popularity.gain` [0,7; 1,6]);
    - `mod/src/PTMod/Content/event-system/events.json` — 10 записей из таблицы баланса `design.md`;
    - ключи `es.*` в `mod/src/PTMod/L10n/ru/event-system.json` и `mod/src/PTMod/L10n/en/event-system.json`;
    - `mod/tests/PTMod.Tests/Features/EventSystem/StarterContentTests.cs`.
  - **Acceptance**: контент проходит валидатор; использованы все стандартные типы, кроме `MarketDemand`; есть выбор с таймером, шанс, эксклюзивная группа, `FireEvent`, цели `Player` / `RandomAI` / `RandomCompany`; условия — предикаты каталога ядра (`dateAtLeast`, `cpuOnMarket`, `researchActive`) и движка с префиксом (`es.hasRevenueHistory`, `es.hasFab`); ключи есть в обоих языках и начинаются с `es.`; в `market.rating.cpu` стартовый набор не пишет
  - **Depends**: TSK-002, TSK-005, TSK-011, TSK-012, TSK-013; mod-framework/TSK-066

- [x] **TSK-026**: Самопроверка фичи на старте (авто 2026-10-10)
  - **Requirement**: NFR-2, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/EventSystem/Logic/SelfCheck/EventSelfCheck.cs` (вклад в `ModSelfCheck`)
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
  - **Частично** (авто 2026-10-10, кусок 6; задача не закрыта: приёмка требует сейва 2000 года в игре, а игру запускать нельзя): отладочный таймер готов и проверен тестами,
    замер на логике без игры лежит в «Evidence», замер в игре остаётся владельцу (шаги ниже).
  - **Evidence**:
    - *Замер на логике без игры* (`PerformanceTests.NFR_1_...`, `python mod/build_feature.py event-system --tests --filter PerformanceTests`): игрок и 13 ИИ, все с историей выручки, фабрикой и CPU на рынке;
      стартовый набор из 10 событий, настоящие планировщик, рантайм эффектов (девять стандартных типов), агрегатор над движком каналов ядра, журнал; без UI. Прогрев — 2000 год, замер — годы 2001–2005
      (1826 дней), из них 183 дня со срабатыванием (каждый десятый день событие ставится в очередь на завтра, так что `ProcessDay` создаёт его сам, плюс лотерея). Часы — `Stopwatch`. Пять запусков:
      среднее дня без срабатывания 0,0024 – 0,0030 мс (бюджет 0,3), самый долгий день со срабатыванием 0,026 – 0,178 мс (бюджет 3), UI в этом замере нет. Запас — два порядка.
      Единичные выбросы дня без срабатывания (до 1,7 мс) — сборщик мусора или процесс рядом; на среднее они не влияют.
    - *Замер в игре* (владелец, это часть TSK-028): включить подробный журнал (`"verboseLog": true` в `ptmod_settings.json`; таймер работает только пока он включён), загрузить сейв 2000 года с 13 ИИ,
      прокрутить год на скорости 3 (до 1 января следующего года); в `Player.log` появится строка `[PTMod] [event-system] INFO day timing 2000: 365 days, quiet N mean … ms max … ms, firing M mean … ms max … ms
      (with the UI … ms), ui … ms in all; within the budget (0.3 / 3 ms); core: the handlers of all features on DayPassed, last 30 days, mean … ms, max … ms, K handlers`. Критерий: «within the budget».
      Первое число после `quiet` — среднее дня без срабатывания (в бюджет 0,3 мс), `firing … max` — самый долгий день со срабатыванием без UI (в бюджет 3 мс); `core: …` — тот же день по `ModTicks.StatsOf(TickPhase.DayPassed)`
      (все обработчики всех фич, вкладка «Диагностика»). Таймер показывает время без письма, уведомления и окна события: их строит UI, они внутри `UiTimed` движка.

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
| TSK-001 Модель контента | Complete (авто 2026-10-10, кусок 1: типы модели лежат в `Api/Model` (их создал автор API: `EventDefinition`, `EventTrigger`, `EventTargetSpec`, `EventChoice`, `EventEffect`, `IEffectParams`, `FloatRange`, перечисления); сделано: `Logic/Model/EffectParams` (адаптер `IEffectParams` над `JsonObject`: пропущенный ключ, JSON null и ключ не того вида дают запасное значение, `GetRange` принимает число и пару `[lo, hi]`, `GetInt` — только целые), `Logic/Model/DefinitionMapper` (`JsonObject` → `EventDefinition`: тексты `LocText` — ключ или `{ru, en}`, условия `conditions` / `targets.filter` / `choices[].requires` через переданный разбор `ModConditions.Parse`, значения по умолчанию контракта, все ошибки записи собираются сразу, неизвестное поле — предупреждение, `disabled` и `_*` не считаются), `Logic/Model/RecordReader` (внутренний читатель полей); имена по `design.md`; `EffectParamsTests` (11), `DefinitionMapperTests` (21), `ApiModelContractTests` (9) зелёные; в тестовой сборке `Api/Model` не видна — рабочий обход `tests/PTMod.Tests/Features/EventSystem/ApiModelShim` (копии 5 файлов + тест на расхождение), см. `mod/requests/event-system.md`, п. 1) |
| TSK-002 Реестр и валидатор | Complete (авто 2026-10-11, кусок 1; склейка сделана в TSK-006/014: `Logic/Content/PackCatalog` (`RegisterPack` — папка без `content/`, повторный пак и небезопасное имя папки отклоняются с предупреждением, `Get`, `All`, `InCategory`, `Config`; чтение один раз в `LoadPending` в порядке id паков: сначала `config.json` всех паков, затем файлы событий через `ModContent.LoadRecords` с переопределением из `PTMod/content/<папка>/`; имя файла — `PackOptions.EventsFile`, по умолчанию `events`; битая запись пропускается со строкой `skip '<id>': <причина>`; id другого пака — дубликат; события с `FireEvent` на несуществующее событие выпадают цепочкой), `Logic/Content/ContentValidator` (префикс id по `ModIds.IsEventIdAllowed` у владельца пака, неизвестный тип эффекта и канал, категория, `Date`, `Condition`, `Specific`, варианты и таймер, `defaultChoice` не найден или с `requires`, общие параметры `scope`/`chance`/`exclusiveGroup`/`scalable`, Σ шансов группы ≤ 1, `FireEvent` на себя без задержки, ответ `IEventEffectHandler.Validate`), `Logic/Content/StandardEffectRules` (таблица параметров design.md для девяти стандартных типов), `Logic/Content/EventConfig` (`config.json` каждого пака: категории объединяются, пределы каналов — самый узкий: внутри пака побеждает самая точная запись, между паками — наибольший минимум и наименьший максимум; скаляры — наименьшее значение), `Content/event-system/config.json` по примеру design.md; `Content/CoreContentSource`, `CoreContentEnvironment`, `GamePackCatalog`, `PackLoader` (склейка с ядром, в сборке тестов не участвует); `ContentValidatorTests` (42), `StandardEffectRulesTests` (21), `EventConfigTests` (17), `PackCatalogTests` (27) зелёные; подписка `PackLoader.OnGameReady` на `ModTicks.GameReady` и `EnsureLoaded` из `OnEnabled` (`EventSystemFeature`), делегирование `IEventSystemApi.RegisterPack` / `GetDefinition` / `GetDefinitions` в `PackCatalog` через `EventEngine`; владелец префикса пака — id пака, если у него есть строка таблицы префиксов, иначе фича из `PackOptions.SettingsFeatureId`) |
| TSK-003 Тесты валидатора | Complete (авто 2026-10-10, кусок 1: тесты лежат в `mod/tests/PTMod.Tests/Features/EventSystem/` (не в `EventSystem/` из текста задачи — так требует сборка `build_feature.py`), фикстуры в `Fixtures/`; `ContentValidatorTests` воспроизводит AC-1.1 (10 событий стартового набора грузятся без единой строки в логе), AC-1.2 (запись с `Teleport` пропущена со строкой `skip 'es.teleport': unknown effect type 'Teleport' (effects[0].type)`, остальные загружены), AC-1.3 (файл игрока заменяет встроенную запись, остальные на месте, `Replaced`/`Added` в отчёте) и AC-1.4 (`dfr.x` в паке `market-shocks` отклонён); отдельными тестами: дубликат `id` (в одном файле и между паками), неизвестный канал (в `ModifierTimed` и в типах паков), `defaultChoice` с `requires`, Σ шансов группы > 1 (и ровно 1, и 0,1 + 0,2 + 0,7), предикат без префикса (реестр отклоняет, запись с ним пропущена), множитель вне диапазона; все публичные типы `Logic` и модели указаны в `[Covers]`; `python mod/build_feature.py event-system --tests` — 1263 теста зелёные, из них 149 относятся к event-system) |
| TSK-004 Фасад и каталог компаний | Complete (авто 2026-10-10, кусок 2: интерфейс `IGameFacade` и `CompanyRef` уже лежали в `Api/EventContext.cs`; сделано: `Game/GameFacade.cs` (`InflationHandler.Instance.Inflation`, `CompanyFinance.QuarterlyRevenue` только при `Reports.Count ≥ 3`, иначе 0, `MarketDataProvider.MarketShare`, `IsTopByMarketShare` — рейтинг только ИИ, `CpuDataProvider.GetAllCpus` с отбором выпущенных, не снятых и продающихся CPU компании, `ResearchSector.TechnologyAsResearch(CurrentResearch())`, `ModCredit.ThresholdOf` → `DefaultThreshold`, `ModTicks.Today` → номер дня от 1971-01-01, `ModTicks.IsBatchRun` → `InBatch`; каждое чтение под try/catch: отказ даёт 0 / null и одну строку в логе), `Game/CompanyDirectory.cs` (обёртка над `ModCompanies.All` / `KeyOf` / `Resolve`, банкроты исключены, список берётся из кеша ядра по номер версии набора компаний в `ModCompanies` (внутренний член); реализует `IEventWorld` для планировщика и предикатов); правила вынесены в чистую логику `Logic/Game/CompanyFacts.cs` (`CompanyFacts`, `MarketRanking`, `CompanyRoster`); `CompanyFactsTests` (5): `Reports.Count < 3` → 0 без исключения, банкроты и компании без ключа не попадают в список, игрок не входит в рейтинг по доле рынка, в исходниках фичи нет `DataFinder.FindAllCompanies` / `FindObjectsByType`) |
| TSK-005 Условия событий | Complete (авто 2026-10-10, кусок 2: фасад `EventConditions` (`Register` / `Parse` / `Evaluate` над `ModConditions`) уже был в `Api`; сделано: `Logic/Conditions/BuiltinPredicates.cs` — `es.eventFired` (`withinDays`), `es.choiceMade`, `es.eventFlag`, `es.activeInCategory` (`scope` = `subject` по умолчанию | `any`), `es.hasRevenueHistory`, `es.hasFab` регистрируются через `ModConditions.Register` с владельцем `event-system` (в игре — в `Register` фичи), параметры проверяются при разборе условия (лишний или пропущенный параметр — ошибка записи); подлежащее — `ctx.SubjectKey` компании-цели; данные: `Logic/Conditions/EventHistory` (последнее срабатывание события по компании, сделанные выборы, флаги цепочек; сохраняется в секции) и `ActiveEventIndex` (ожидающие выборы и активные эффекты по категориям и компаниям, включая глобальные; его наполняет рантайм экземпляров — TSK-010/016); `PredicateTests` (11): AC-6.5, `es.activeInCategory` с активным и глобальным эффектом, `es.hasRevenueHistory`, `es.hasFab`, история, `all` / `any` / `not` в `conditions`, `targets.filter` и `requires` записи, неизвестный тип — запись отклонена, имя без префикса и с чужим префиксом отклоняется) |
| TSK-006 Фича, планировщик и триггеры | Complete (авто 2026-10-10, кусок 2: `EventSystemFeature` (`[ModFeature]`, `event-system`, `es.feature.name` / `es.feature.desc`, `DependsOn` пуст, включена по умолчанию, горячее переключение); `Register` идёт через `Logic/Feature/EventFeatureSetup` в порядке design.md: `ctx.SaveParticipant` (секция `event-system`: `EventPersistence` над чистым `EventSaveSection`), `ctx.OnTick(TickPhase.DayPassed, …)`, `ctx.Param` (`frequency` 0..3, `pauseAllChoices`, `rivalNews`; строки в `L10n/{ru,en}/event-system.json`), `EventSystemApi.Install(EventEngine)`, предикаты `es.*` и пак `event-system`, подписка `ModTicks.GameReady` (чтение паков, новая игра); `OnEnabled` дочитывает паки; `Logic/Scheduling/EventScheduler` (шаги дня по FR-2.1; `Date` с `graceDays`; `Condition` по уровню и по фронту, `checkEvery`, `chance`; `Manual` только через `Fire` с ответами `Fired` / `Scheduled` / `Skipped` / `UnknownEvent` / `Disabled`; отложенные срабатывания; очередь до 30 дней за лимитом выборов игрока; событие, упавшее 3 раза, выключено до конца сессии), `Pacing` (частота настройки и пака, `minGapDays`, `maxActive`, откат по паре (событие, компания), для `AllCompanies` — по событию (`*`), `oncePerGame`, `maxPendingChoices`), `SchedulerState`; заодно доведена склейка TSK-002 (реестр паков за `IEventSystemApi`, `PackLoader` на `GameReady` и в `OnEnabled`); `ctx.Patch` в фиче нет (тест по исходникам); регистрация проверена на записывающем фейке `IEventFeatureHost` без Unity; экземпляры создаёт `ISchedulerRuntime` — до TSK-010 стоит `DryRunRuntime` (срабатывание пишется в лог, откат не тратится); подписки `ModTicks.BatchEnded`, `ModMail.Delivered`, `ModFeatures.StateChanged`, `ModL10n.LanguageChanged` и `ctx.HubPage` добавят TSK-017 / 018 / 019 / 022 / 023 вместе со своими обработчиками, см. `mod/requests/event-system.md`, раздел 8) |
| TSK-007 Выбор целей | Complete (авто 2026-10-10, кусок 2: `Logic/Scheduling/TargetSelector.cs`: `Player`, `RandomAI` (только ИИ), `RandomCompany` (все компании, игрок по тем же весам), `AllCompanies` (одна группа; `FiringPlan.Expand` кладёт эффекты события с `scope: "Global"` только на первый экземпляр группы, выборы — на каждый), `Specific` (нет компании или банкрот — пропуск без расхода отката); веса `Uniform` 1, `FactoryCapacity` — линии (0 — не кандидат), `MarketShare` — доля + 0,01, `QuarterlyRevenue` — max(QR, 0) + 1; `targets.filter` и `conditions` проверяются для каждой кандидатки как подлежащего, правила одинаковы для игрока и ИИ; `TargetSelectorTests` (11)) |
| TSK-008 Потоки ГСЧ | Complete (авто 2026-10-10, кусок 2: `Logic/Scheduling/EventRandom.cs`: дневной поток `ModRandom.Stream("event-system/daily")` (подключается в `EventEngine`), `DrawCategory` всегда берёт 3 числа; `DerivedRng : IModRng` — xoshiro128** ядра с сидом `hash32(WorldSeed, ключ)` по правилу потоков `ModRandom` (`WorldSeed XOR FNV-1a` над `event-system/inst/<id>` и `event-system/trg/<eventId>/<day>`); броски `Date` / `Condition` / `Manual` идут из производного генератора и не сдвигают дневной поток; в исходниках фичи нет `System.Random` и `UnityEngine.Random` — тест по исходникам добавлен уже сейчас (`DeterminismTests`), в TSK-026 на него достаточно сослаться) |
| TSK-009 Тесты планировщика | Complete (авто 2026-10-10, кусок 2: тесты в `mod/tests/PTMod.Tests/Features/EventSystem/` (не в `EventSystem/` — так требует `build_feature.py`): `CategoryLotteryTests` (11: AC-2.1 — 36 500 дней при 0,011 дают 401 ± 60, три числа дневного потока на категорию в день при любом исходе, частота настройки и пака, вес, `minGapDays`, `maxActive`, AC-2.3), `TriggerTests` (24: порядок шагов FR-2.1, `Date` / `graceDays` / E9, `Condition` по уровню, фронту, раз в месяц и с шансом, `Manual` и ответы `Fire`, AC-2.2, откат по компании и по событию для `AllCompanies`, `Specific`, `oncePerGame`, очередь 30 дней, 3 сбоя), `TargetSelectorTests` (11: AC-3.1 — 20 % ± 2 % на 10 000, AC-3.2 на уровне плана срабатывания: 10 экземпляров с выбором, глобальный эффект один раз; сам вклад в канал проверит TSK-015 с агрегатором), `DeterminismTests` (8: одинаковый сид — одинаковые события, AC-9.2, производные генераторы, сохранение и загрузка посреди прогона не меняют последовательность), `PredicateTests` (11, AC-6.5), а также `CompanyFactsTests` (5) и `FeatureSetupTests` (6); `python mod/build_feature.py event-system --tests` — 1339 тестов зелёные, из них 225 — event-system) |
| TSK-010 Резолвер эффектов | Complete (авто 2026-10-10, кусок 3: типы `IEventEffectHandler` / `ITimedEffectHandler` / `ICurveEffectHandler` и `AppliedEffect` уже лежали в `Api`; сделано: `Logic/Effects/EffectPorts.cs` (`EffectCall` — чистая часть `EventContext`, `IEffectHandlerPort`, `IEffectTable`, `IEffectWorld`, `IChainHost`), `ChanceRoller` (шанс и эксклюзивные группы по формулам design.md: одно число на эффект, группа — число первого члена и накопленные суммы), `EffectResolver` (`Resolve` один раз на эффект всех вариантов и `common` при создании экземпляра; id `<экземпляр>/<вариант|common>/<индекс в определении>`, компания `global` для `scope: Global`, `chance` / `hit` / `group`, состояние `Pending`; `Severity` пака для `scalable` передаётся обработчику, тот зажимает множитель пределами канала `LimitMin` / `LimitMax` и ставит `AppliedEffect.clamped` — AC-5.5; нет обработчика, исключение или нечисловое значение → `Failed`; текст — `Describe` только по `AppliedEffect`), `InstanceFactory` (экземпляры плана, общий `groupId`, предварительный дедлайн `firedDay + timerDays`, генератор `ForInstance(id)`), `EffectRuntime` (реализует `ISchedulerRuntime` и подключён в `EventEngine` вместо `DryRunRuntime`: применение `common` при срабатывании, промах шанса — `Skipped`, исключение в `Apply` / `Tick` / `Restore` → `Failed` и запись `EffectFailed`, день продолжается; временный эффект активен до `startDay + durationDays`; индекс `ActiveEventIndex`; выбор `ResolveChoice`, дедлайн с `defaultChoice`, отмена; сохранение `instances` / `nextInstanceId` / `log` и `AfterLoad` с `Restore`); расширения API: `EventContext.Severity`, `EventContext.Spec`, `AppliedEffect.clamped`, `EventInstanceDto.chainDepth`; склейка с игрой — `Effects/EffectHandlerAdapters.cs`; тесты `EffectRuntimeTests` (17) и `ChanceGroupTests` (6); подробности — `mod/requests/event-system.md`, раздел 9) |
| TSK-011 Агрегатор и временные эффекты | Complete (авто 2026-10-10, кусок 3: `Logic/Effects/ModifierAggregator.cs` реализует `IEffectModifiers` (`Set` / `Clear` / `WasClamped` / `LimitMin` / `LimitMax`), а также `Current`, `ReasonArgs`, `Suspend` / `Resume` пака, `ClearAll`; `Current = clamp(Π global × Π компании, пределы)`, пределы — самый узкий шаблон `channelLimits` (`EventConfig`), в том числе `popularity.gain` [0,7; 1,6]; провайдер регистрируется один раз на канал через `IModifierChannelSink` (в игре `ModModifiersSink` → `ModModifiers.RegisterReasonedProvider("event-system", channel, …)`) с причиной `es.reason.events` и названиями событий; каждое изменение вклада → `Invalidate(channel, companyKey)`, для `global` — `Invalidate(channel)` (AC-5.4); `OnDisabled` ничего не удаляет (тест по исходникам: в фиче нет `RemoveProviders` / `ModModifiers.Remove*`, тело `OnDisabled` пустое); обработчики `Logic/Effects/Handlers/ModifierTimedEffect.cs`, `FactoryOutputEffect.cs`, `MarketDemandEffect.cs` (база `TimedModifierEffect`; `MarketDemand` — `global`, `all` → четыре канала); заморозка пака подключена: `EffectRuntime.SyncPacks` каждый день, на `GameReady` и на `ModFeatures.StateChanged`, `ModL10n.LanguageChanged` → `RefreshNames`; `ModifierAggregatorTests` (13): AC-5.2 через движок каналов ядра `ChannelRegistry` (×1,12 × ×1,10 = 1,232 сразу после `Invalidate`, одна строка `Explain` с причиной), AC-5.4 (и контроль: без `Invalidate` значение дня не меняется), AC-5.5, пределы, заморозка, `ClearAll`, один провайдер на канал) |
| TSK-012 Разовые эффекты | Complete (авто 2026-10-10, кусок 3: `Logic/Effects/Handlers/MoneyEffect.cs` (`round_to_1000(clamp(f × QR, min × I, max × I) × s)`, `f` — одно число генератора экземпляра, `inflation: false` — без `I`, трата — отрицательное `value`; `Apply` — один вызов `ICompany.SpendMoney` / `EarnMoney` с `origin` из параметров, `Earn` без `origin` — `RevenueOrigin.Interest`, `Spend` без `origin` — `ExpenseOrigin.Fines`), `PopularityEffect.cs` (`EffectMath.Popularity`: [0,05; 1,0], без сдвига против знака δ; `AllMarket` / `Newest`, сегмент), `ResearchProgressEffect.cs` (`min(Progress + δ, 0,99)`, без исследования и для `global` — `Skipped`), `ReputationEffect.cs` (`ModReputation.Add`, причина — ключ `title` события или `es.reason.event`); игровая сторона — `Effects/GameEffectWorld.cs`; формулы и форматы — `EffectMath`, `EffectFormat`; `MoneyEffectTests` (9: пример design.md 1990 / QR $20M / f = 0,052 → −$1 040 000, пределы × инфляция, округление, QR = 0 без трёх отчётов, `Severity`, origin, AC-5.1 — сумма в тексте, журнале и `SpendMoney` одна, списание одно) и `StandardEffectTests` (6)) |
| TSK-013 Письма, цепочки, флаги | Complete (авто 2026-10-10, кусок 3: `Logic/Effects/Handlers/FireEventEffect.cs` (задержка тянется при `Resolve`, `Apply` ставит запись в `scheduled` на `today + delay` и никогда не срабатывает сразу; `target` `Same` / `Player` / `Definition`; глубина цепочки ≤ 8 — `EventInstanceDto.chainDepth`, `ScheduledFire.depth`, `FiringPlan.ChainDepth`, девятый шаг `Skipped`; необязательный `flag` ставит флаг для `es.eventFlag`), `SendMailEffect.cs` (`ModMail.Send` без вариантов, приоритет `Normal`, тег `es:fx:<id эффекта>`, только игроку; отказ почты — `Failed`), `Logic/Scheduling/ScheduledFires.cs` (`IChainHost` над `SchedulerState` и `EventHistory`), `ChainFlags.cs` (глубина, цель, имя флага); самовызов с задержкой 0 отклоняет валидатор (TSK-002), параметр `flag` проверяется в `StandardEffectRules`; `ChainTests` (8: срабатывание в день `today + delay`, родитель и глубина, остановка на 8 уровнях, цели, флаг, валидатор, `SendMail`, сохранение глубины)) |
| TSK-014 Публичный API | Complete (авто 2026-10-10, кусок 3: фасады `Api/EventBus.cs`, `EventEffects.cs`, `EventAi.cs`, `Model/PackOptions.cs` уже были; за ними теперь настоящая логика в `EventEngine`: `EventBus.Fire` → планировщик с реальным рантаймом (`Fired` / `Scheduled` / `Skipped` / `UnknownEvent` / `Disabled`, тест с рантаймом), `OnEventFired` / `OnChoiceResolved` / `OnEffectExpired` поднимаются после эффектов через `ModHooks.Raise`, исключение слушателя ловится и в рантайме (AC-6.2 на логике); таблица типов `TypeTable<T>` (повторная регистрация — предупреждение, первая остаётся, AC-6.1), девять стандартных типов регистрируются через `EventEffects.Register`; `ApplyDirect` → `EffectRuntime.ApplyDirect` (экземпляр `Direct`, `eventId = sourceId`, сохраняется, истекает, журнал, AC-6.3); `EventAi` → `PriorityList<T>` + `AiChooserChain` (по убыванию приоритета, фильтр `IEventAiChooserFilter`, первый ответ с доступным вариантом, исключение = `null`, AC-6.4), до TSK-016 без ответа берётся `defaultChoice`; `EventLog.Entries` → `EventLogStore`; окно журнала — TSK-019) |
| TSK-015 Тесты эффектов | Complete (авто 2026-10-10, кусок 3: тесты в `mod/tests/PTMod.Tests/Features/EventSystem/` (так требует `build_feature.py`): `MoneyEffectTests` (9), `ModifierAggregatorTests` (13), `ChanceGroupTests` (6), `ChainTests` (8), а также `EffectRuntimeTests` (17) и `StandardEffectTests` (6), общий набор фейков `EffectsTestKit`; AC-5.1, AC-5.2 (через `ChannelRegistry` ядра), AC-5.3, AC-5.4, AC-5.5, AC-6.1, AC-6.3 (сохранение на 30-й день, загрузка, эффект до 90-го дня, журнал с источником) зелёные; пример design.md (1990, QR $20M, f = 0,052 → −$1 040 000) воспроизводится; `Invalidate` проверен на каждое изменение вклада; копии `Api/Model` для тестов — `ApiModelShim` (+6 файлов); проверка мутациями: без `Invalidate` и без проверки промаха шанса тесты падают; `python mod/build_feature.py event-system --tests` — 1398 тестов, все зелёные) |
| TSK-016 Выборы и ИИ | Complete (авто 2026-10-10, кусок 4: `Logic/Choices/ChoiceAvailability.cs` (`ChoiceState`: серый вариант с причиной `es.reason.requires` — «Нужно: {слова условия}» — при ложном `requires`; трата выше баланса — не блок, а предупреждение `InTheRed` («уйдёте в минус на $350K», AC-4.2); трата, после которой баланс ниже `ModCredit.ThresholdOf`, — серый вариант `es.reason.credit_limit` («Не хватит кредитного лимита на $15M», AC-4.6); `defaultChoice` деньгами не блокируется; `RequirementText` — первая невыполненная часть `all`, части `any`, текст `es.req.<предикат>` или `Describe()` ядра), `ChoiceController.cs` (дедлайн `max(firedDay, deliveredDay) + timerDays` только вперёд — `Delivered`; экземпляр игрока с недоставленным письмом не истекает; на дедлайне — `defaultChoice` с `auto`, AC-4.1; пакетный прогон сдвигает дедлайны игрока на +1 за день и ничего не истекает, AC-4.5, AC-13.2; `Answer`: вариант, ставший недоступным между проверками, заменяется на `defaultChoice` (`FellBack`, AC-4.4), повторный ответ и неизвестный вариант игнорируются; цель-банкрот — экземпляр отменяется без эффектов, `TargetGone`), `DefaultAiChooser.cs` и `AiDecision.cs` (цепочка `EventAi` → `aiWeight` среди вариантов, на которые хватает денег (`aiMinCashMultiple`) и у которых `requires` истинно → `defaultChoice`; одно число генератора на решение; ИИ решает в день срабатывания, AC-4.3), подключены в `EventEngine` (`DeadlineDue`, `TargetGone`, `DecideForAi`); в `EffectRuntimeOptions` добавлены `Cancelled` и `TargetGone`; `ChoiceTimerTests` (11), `ChoiceAvailabilityTests` (7), `AiChooserTests` (13); мутации (дедлайн не пересчитывается при доставке, нет сдвига в пакетном прогоне, нет ожидания доставки) валят тесты) |
| TSK-017 Письмо, строка таймера, уведомления | Complete (авто 2026-10-10, кусок 4: `Logic/UI/EventMailFormatter.cs` — письмо выбора для игрока (`Critical`, тег `es:<instanceId>`, заголовок `[Событие] …`, текст, «Почему: …», «Сразу:» — эффекты события, которые сработали, «Варианты:» — каждый вариант с точными эффектами и шансом `(50% шанс)`, пометка `(ограничено)`; кнопка на вариант, серые с причиной), письмо события без выбора (`Normal`) и новость об ИИ (`Low`); `EventUiController` (порты `IMailPort`, `INotifyPort`, `IChoiceWindowPort`, `ITextSource`, `IUiSettings`): уведомление «Новое событие: …» со сроком, по `ModMail.Delivered` (по тегу, не по `mailId`) — дедлайн от дня доставки и строка «Ответить до 15.06.1985 (14 дн.)», далее ежедневно «Осталось N из M дн.» (AC-7.2) и строки о минусе; кнопки письма пересчитываются раз в день (`SetChoiceState` + `Refresh`, только при изменении); напоминание за `reminderDaysBefore` (3) дня один раз; «Выбрано автоматически: …» и «Вариант стал недоступен: …»; ответ — `ModMail.RegisterChoiceHandler`, автоматическое решение и отмена закрывают письмо `ModMail.Resolve`; новости по `notifyPlayer` с `rivalNews` и `RivalNewsTopN` (AC-7.4); письмо, не принятое почтой, не мешает решить: открывается окно; игровая склейка `UI/GameUiPorts.cs`; подписки `ModMail.Delivered`, `ModTicks.BatchEnded`, обработчик кнопок — в `EventSystemFeature.Host`; `UiControllerTests` (25), `DescribeTests` (11); подробности — `mod/requests/event-system.md`, раздел 10) |
| TSK-018 Модальное окно | Complete (авто 2026-10-10, кусок 4: `UI/EventChoiceWindow.cs` — `ModUI.CreateWindow("event-system.choice", WindowTemplate.Modal, …)` (пауза — `PauseTrigger` шаблона), заголовок, причина, текст, строка «Осталось N из M дн. ■■■■■■■■■□□□□□» (`CountdownBar`, не длиннее 30 делений), кнопка на вариант (подпись с предупреждением о минусе, подсказка — точные эффекты через `ModUI.Tooltip`, серая кнопка с причиной) и «Решить позже»; окно рисует модель `ChoiceWindowModel` и обновляется раз в день; очередь `ChoiceWindowQueue` — одно окно за раз в порядке срабатываний, во время пакетного прогона ничего не открывается, накопленное открывается по `ModTicks.BatchEnded`; окно открывается для `important` и по настройке `pauseAllChoices`; AC-7.1 проверяет владелец вручную (M6); тесты очереди, обратного отсчёта и «Решить позже» — в `UiControllerTests`) |
| TSK-019 Журнал событий | Complete (авто 2026-10-10, кусок 4: хранилище (`EventLogStore`, лимит `logLimit` = 400, AC-8.2) и фасад `Api/EventLog.cs` уже были; сделано: `Logic/Log/JournalView.cs` (история — новые сверху: дата, компания, событие, что произошло — «Сработало» / «Выбрано: …» / «Выбрано автоматически: …» / «Отменено» / «Эффект закончился» / «Эффект не применён», эффекты с сохранёнными числами; «Активные эффекты» — `Скорость исследований +12 % — до 01.06.1986 (182 дн.)` (AC-8.1), компания или «весь рынок», заморожено; фильтры «Только моя компания» и категория; текущее значение меняющегося эффекта — `ICurveEffectHandler.ValueAt`), `UI/EventLogWindow.cs` (`WindowTemplate.Panel`, вкладки «История» / «Активные эффекты», `ModUI.Toggle`, `ModUI.Dropdown`, `ModUI.ScrollView`, по 50 записей и «Показать ещё»; перерисовка раз в день, по записи, по `ModL10n.LanguageChanged` и смене темы); `EffectRecord` получил `textKey` / `textArgs`, поэтому записи перерисовываются из ключей и сохранённых аргументов при смене языка; `EventLog.OpenWindow()` → `EventEngine.OpenLogWindow`; `JournalTests` (11)) |
| TSK-020 Тесты выборов и текстов | Complete (авто 2026-10-10, кусок 4: тесты в `mod/tests/PTMod.Tests/Features/EventSystem/` (так требует `build_feature.py`), общий набор фейков `ChoiceTestKit` (мир выборов, таблицы текстов из настоящих файлов `L10n/{ru,en}/event-system.json`, почта, уведомления, окно, `ChoiceRig` — планировщик, рантайм эффектов, контроллер выборов и интерфейса вместе); `ChoiceTimerTests`: AC-4.1, AC-4.5, дедлайн от доставки и недоставленное письмо, AC-13.2, повторный ответ, банкротство; `ChoiceAvailabilityTests`: AC-4.2, AC-4.4, AC-4.6; `AiChooserTests`: AC-6.4 (два chooser'а), AC-4.3, политика по умолчанию; `DescribeTests`: числа строк эффектов не меняются со сменой языка, письмо, строки под письмом, обратный отсчёт, таблицы обоих языков (одни и те же ключи, все ключи кода на месте); `JournalTests`: AC-8.1, AC-8.2, фильтры, смена языка; `UiControllerTests`: письма, окна, напоминание, новости (AC-7.2, AC-7.4); `python mod/build_feature.py event-system --tests` — 1476 тестов, все зелёные, из них 362 — event-system) |
| TSK-021 Сохранение и загрузка | Complete (авто 2026-10-10, кусок 5: секция `event-system` по ключам примера `design.md` без собственного поля версии (`_schema` пишет ядро; `SchemaVersion = 1`): `Logic/Persistence/EventSystemSaveDto.cs` (бывший `SchedulerSaveDto`, переименован под имя из задачи), `EventSaveSection` (`ModJson.Bind` / `From`, испорченная запись отбрасывается со строкой в логе, остальное читается), тонкий `Persistence/EventPersistence.cs` (`IModSaveParticipant`, не в тестовой сборке) за новым `Logic/Persistence/SessionGate.cs`; `Read` сбрасывает вклады прошлого сейва (`Import` → `Clear` → `ModifierAggregator.ClearAll` с инвалидацией каналов); `Logic/Runtime/EventRuntime.cs` (чистая логика; вместо `EventRuntime.cs` в корне фичи, иначе тесты её не соберут): `OnGameReady(Loaded)` — отмена экземпляров неизвестных событий (письмо закрывается `Resolve`, запись `Cancelled` в журнале) → `Restore` активных эффектов → связь с письмами по тегу (`ModMail.Find`) → пересчёт `SetChoiceState` / `SetStatusLine` (AC-10.3); `OnGameReady(NewGame)` — состояние, журнал, вклады, окна сброшены, отсчёт от сегодняшней даты (FR-10.5, AC-13.1); сейв без секции — пустое состояние (`ResetToDefaults`, AC-10.2). Добавлено сверх design: `SessionGate` — ядро после горячего выключения/включения фичи отдаёт участнику `ResetToDefaults` или прошлый сохранённый текст (обработчик `OnFeatureStateChanged` в `ModSave` → `Restore`), что стёрло бы замороженное состояние в памяти (AC-11.1); пока состояние привязано к игре (после `GameReady`), такая выдача игнорируется, а выдача фиче, которая была выключена в момент `GameReady`, ведёт к «позднему старту» (`EventRuntime.CompleteLateStart`); если сохранённое состояние старше игры (фича не работала), дедлайны выборов игрока сдвигаются на разницу дней; `SaveChecks` расширен диапазонами (NFR-7: шанс вне [0; 1], срок > 100 лет, сумма > 1 трлн., день вне игровых дат, `lastProcessedDay`); `mod/requests/event-system.md`, раздел 11. В игре не проверялось)
| TSK-022 Защиты времени (пакетный прогон через ядро) | Complete (авто 2026-10-10, кусок 5: `Logic/Runtime/BatchGuard.cs` — чтение `ModTicks.IsBatchRun` через `Func<bool>`, `Hold` сдвигает дедлайн ожидающего выбора игрока на +1 за каждый обработанный день прогона и ничего не истекает, `WindowsAllowed` для очереди окон (окна открывает `ModTicks.BatchEnded` → `EventUiController.OnBatchEnded`), `Ended()` считает сдвиги для строки в логе, `CanProcessDay(fastForwarding, loading, hasGameDate)` — то же правило ядра у собственного входа `EventEngine.OnDayPassed`; `ChoiceController` берёт защиту (`Batch`) и дополнительно держит таймер выбора пака, чей выключатель выключен (+1 за день, D10); экземпляр с недоставленным письмом не истекает (`ChoiceController.WaitsForDelivery`, уже было); файла патча на `DateController.NextMonth` нет, подписка на тик — без `TickOptions` (проверяет `BatchGuardTests` по исходникам); после загрузки обрабатывается только текущий день, догоняющей симуляции нет (FR-13.3, тест с разрывом в 100 дней); `BatchGuardTests`: AC-13.2 (31 день прогона, «Осталось 5 из 14 дн.» до и после, окно важного события ждёт `BatchEnded`), AC-4.5 (письмо, отправленное во время прогона), AC-13.1 в `PersistenceTests`; `ModHooks.Report` проверить в игре нельзя, а патчей у фичи нет вовсе)
| TSK-023 Настройки и выключение | Complete (авто 2026-10-10, кусок 5: `Logic/Settings/EventSettings.cs` — `EventParams` (`frequency` Int 0..3, `pauseAllChoices`, `rivalNews`; перенесены из `EventFeatureSetup.cs`) и `EventSettings` (подписи уровней частоты `es.hub.frequency.0..3`, строка-пояснение под ползунком, флаг `debug.events`, `DebugFire()` = игрок + `IgnoreConditions` + сегодня, `DebugRows` — события реестра по пакам, строка серая для замороженного пака); `UI/EventSettingsPage.cs` (`FeatureContext.HubPage("es.hub.title", …)`: параметры рисует хаб, страница добавляет пояснение частоты, кнопку «Журнал событий» (закрывает хаб, `EventLog.OpenWindow`) и только при `ModSettings.Debug("debug.events")` список «Запустить сейчас» через `EventBus.Fire`); `EventRuntime.OnDisabled` запоминает день и ничего не удаляет и не создаёт заново (провайдеры, вклады, состояние остаются), `OnEnabled` сдвигает дедлайны выборов игрока на число дней выключения (`ChoiceController.ShiftDeadlines`), закрывает записью в журнале эффекты, истёкшие за это время, привязывает письма, доставленные во время выключения (`EventUiController.AfterEnable`, `deliveredDay` по `ModMail.Find`), сверяет паки; пак с выключенным выключателем (`PackOptions.SettingsFeatureId`, `ModFeatures.StateChanged`) замораживается как раньше, а его выборы игрока теперь стоят (+1 к дедлайну за день), остальные паки не затронуты; `OnSceneLeaving` (подписка на `PTModHost.SceneLeaving`) снимает привязку; ключи `es.hub.*` в обеих таблицах; `LifecycleTests` (AC-11.1 с настоящим правилом ядра «провайдер выключенной фичи пропускается», AC-11.3, закрытие истёкших, письма, пак, шлюз `SessionGate`), `SettingsTests` (AC-11.2 на логике: `es.trade_show` через `DebugFire()` даёт письмо с тремя вариантами в тот же день); виджеты хаба и кнопка в игре не проверялись: нужны сценарии владельца)
| TSK-024 Тесты сохранения | Complete (авто 2026-10-10, кусок 5: `mod/tests/PTMod.Tests/Features/EventSystem/PersistenceTests.cs` (14 тестов; в `EventSystem/` из текста задачи нельзя — так требует `build_feature.py`) и общий набор `LifecycleTestKit` (`LifecycleRig` — весь движок без игры: выборы, письма, окна, секция за `SessionGate`, `EventRuntime`): ключи секции и отсутствие поля версии, круговая запись через `ModJson` без потерь (повторная запись даёт тот же текст), пустая и отсутствующая секция, испорченные типы, AC-10.1 (письмо, дедлайн 20.04.1988 и «Осталось 10 из 14 дн.» и множитель `production.output` ×0,6 вернулись, потом кончаются в свой день), загрузка в работающий движок сбрасывает вклады прошлого сейва, AC-10.2, AC-10.3 (серая кнопка пересчитана), неизвестное событие отменяется (письмо закрыто, запись `Cancelled`), событие с идущим эффектом не оставляет вклада, новая игра (AC-13.1), испорченные числа (множитель 50, отрицательная длительность, шанс 7, сумма 1e15, срок 10 млн. дней, дедлайн `int.MaxValue`, чужая компания) отбрасываются (NFR-7); мутационные проверки (шлюз всегда пускает, `ShiftDeadlines` ничего не двигает, нет `Hold` в прогоне, нет удержания пака, нет закрытия истёкших, нет привязки писем, нет очистки при новой игре, нет позднего старта, `OnEnabled` ничего не делает) валят тесты; `python mod/build_feature.py event-system --tests`: 1516 тестов, 0 упало (402 — event-system)
| TSK-025 Стартовый контент и строки | Complete (авто 2026-10-10, кусок 6: `Content/event-system/events.json` — 10 записей таблицы баланса `design.md` (`es.star_engineer`, `es.university_grant`, `es.trade_show`, `es.tax_audit`, `es.factory_strike`, `es.patent_troll`, `es.patent_win`, `es.patent_loss`, `es.charity_gala`, `es.rival_scandal`), файл схемы 1 с `records`; `config.json` из куска 1 проверен: категория `universal` 0,011 / 45 / 2, пределы каналов с `popularity.gain` [0,7; 1,6], `maxPendingChoices` 2, напоминание за 3 дня, журнал 400; в `es.university_grant` у варианта `fund` добавлено `requires: researchActive` (сценарий M11: кнопка «Профинансировать» серая с «Нужно: текущее исследование» без текущего исследования; вариант по умолчанию `decline` без `requires`); у `small_booth` источник трат — `Development`, как у `big_booth` (в таблице `design.md` он не указан, без него трата шла бы как `Fines`); тексты: 47 ключей `es.<событие>.title|text|cause|<вариант>|mail.title|mail.text` в `L10n/ru/event-system.json` и `L10n/en/event-system.json` (не в `L10n/ru.json` / `en.json` из текста задачи: строки фич лежат в файлах по фичам), без аргументов и без названий реальных компаний; `StarterContentTests` (13; тесты лежат в `mod/tests/PTMod.Tests/Features/EventSystem/`, а не в `EventSystem/` из текста задачи — так требует `build_feature.py`): файл — запись схемы 1 с десятью id в порядке таблицы, проходит валидатор без строки в логе, каждая запись — строка таблицы баланса (триггер, цель, вес, откат, таймер, вариант по умолчанию, каждый эффект с параметрами и длительностью), использованы все стандартные типы кроме `MarketDemand`, выбор с таймером, шанс, группа `verdict`, `FireEvent`, цели `Player` / `RandomAI` / `RandomCompany`, условия `dateAtLeast` / `cpuOnMarket` / `researchActive` / `es.hasRevenueHistory` / `es.hasFab`, в `market.rating.cpu` пишут ноль эффектов, множители `popularity.gain` 0,85 / 1,1 / 1,25, все ключи в обоих языках под `es.` и ни одной мёртвой строки, русский текст русский и английский английский, все 10 событий срабатывают в стенде выборов без `Failed` и письма читаются на обоих языках, кнопка гранта (M11), цепочка суда за 60–120 дней ровно одним исходом; тесты читают сам `Content/event-system/events.json` (раньше — копию-фикстуру `events.starter.json`, она удалена: `EventTestKit.Fixture(StarterEvents)` отдаёт настоящий файл)) |
| TSK-026 Самопроверка | Complete (авто 2026-10-10, кусок 6: `Logic/SelfCheck/EventSelfCheck.cs` (чистая логика: `EventCheckFacts` — факты, `EventSelfCheck.Evaluate` — правила, `EventCheckVerdict` — итог и строка, `EventCheckProblem` / `EventCheckLevel`; путь `SelfCheck/EventSelfCheck.cs` из задачи перенесён под `Logic/`, иначе его не соберут тесты) проверяет: все девять стандартных типов зарегистрированы, у каждого типа в событиях есть обработчик (`types`, ошибка); каналы, которые пишут стандартные обработчики (`production.output`, четыре `market.demand.*`), и каналы, названные в `params.channel`, объявлены в реестре ядра (`channels`, предупреждение: эффект на неизвестном канале ничего не делает); каждый `FireEvent` ссылается на существующее событие (`events`, ошибка); id события несёт префикс владельца пака (`ids`, ошибка); шесть предикатов движка зарегистрированы в `ModConditions` и несут префикс `es.` (`predicates`, ошибка); ключи событий собственного пака (заголовок, текст, причина, подписи вариантов, письмо `SendMail`) есть в `ru` и `en` и несут префикс `es.` (`strings`, предупреждения; строки чужих паков проверяет их фича); провайдер агрегатора на каждый канал ровно один (`providers`, ошибка; `ModModifiersSink` считает регистрации); пустой реестр — ошибка; ответ, который игра не смогла дать, — находка, а не исключение; итог в лог одной строкой `selfcheck OK: 10 events, 9 effect types` либо `selfcheck found N problems` и строка на каждую находку; `SelfCheck/EventSelfCheckRunner.cs` собирает факты из `ModModifiers.IsDeclared`, `ModIds`, `ModConditions.KnownTypes`, внутренний метод `Keys` класса `ModL10n`, запускается после чтения паков (`EventEngine.LoadPacks`: первый `GameReady` или `OnEnabled`) и повторно, если прочитано больше паков или событий; под `ModHooks.Guard`; `ModSelfCheck` ядра не даёт фичам точки для своих находок, поэтому, как у соседних фич, находки идут в `Player.log`, в Диагностику хаба они не попадают (mod/requests/event-system.md, раздел 12); `SelfCheckTests` (24): стартовый набор проходит с точной строкой, каждое правило ломается отдельным тестом (без обработчика, неизвестный тип, необъявленный канал и каналы стандартных обработчиков, `FireEvent` в никуда, чужой префикс id, незарегистрированный предикат, ключ только в одном языке, ключ чужого префикса, второй провайдер, пустой реестр, исключение в ответе игры, «и ещё N»), в исходниках фичи нет `System.Random` / `UnityEngine.Random`, нет патчей и поля для целей патчей в фактах, склейка проверена по исходникам; мутации (убрать проверку `FireEvent`, проверку `en`) валят тесты) |
| TSK-027 Замер производительности | Частично (авто 2026-10-10, кусок 6: `Logic/Scheduling/DayTimer.cs` — отладочный таймер `EventScheduler.ProcessDay` (`SchedulerOptions.Timer`; два чтения часов в день, пока включён; дни без срабатывания и со срабатыванием отдельно, время UI (письма, уведомления, окна события) вынесено через `BeginUi` / `EndUi`, окно на игровой год отдаётся `windowClosed`, бюджеты NFR-1 0,3 / 3 мс в `DayTimings.WithinBudget`), в движке включён только при `ModSettings.VerboseLog`, пишет одну строку за год `day timing 2000: 365 days, quiet … mean … ms, firing … max … ms (with the UI … ms), …; within the budget` и рядом цифры ядра `ModTicks.StatsOf(TickPhase.DayPassed)`; UI-вызовы срабатывания, автоматического решения и отмены обёрнуты в `UiTimed`; `EventScheduler.FiringsLastDay`; `DayTimerTests` (13), `PerformanceTests`: игрок и 13 ИИ, 5 лет (2001–2005, 1826 дней, 183 со срабатыванием), настоящие планировщик, рантайм и агрегатор, 10 событий: среднее дня без срабатывания 0,0024 – 0,0030 мс при бюджете 0,3, самый долгий день со срабатыванием 0,026 – 0,178 мс при бюджете 3 (пять запусков), см. «Evidence» задачи; не сделано: замер на сейве 2000 года в самой игре (игру запускать нельзя) — шаги для владельца в «Evidence», они же часть TSK-028) |
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
