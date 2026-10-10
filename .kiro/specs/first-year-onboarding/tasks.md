# Tasks: first-year-onboarding

## Document Information

- **Feature Name**: first-year-onboarding (I01)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/first-year-onboarding/requirements.md`
  - Design: `.kiro/specs/first-year-onboarding/design.md`
  - Ядро: `.kiro/specs/mod-framework/{design,tasks}.md`; решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала строится чистая логика (каталог шагов, автомат, состояние, кодек) с юнит-тестами мини-раннера на фейковом мире. Затем адаптеры к игре: директор с опросом и подписками на сервисы ядра, письма, опция в окне новой игры, страница хаба. Последними идут самопроверка, замер производительности и ручная проверка владельцем. Контент (шаги и тексты) пишется параллельно с логикой: от него зависят только интеграционные проверки.

### Implementation Strategy
- Чистая логика (`mod/src/PTMod/Features/FirstYearOnboarding/Logic/`) за интерфейсами `IOnboardingWorld` / `IOnboardingOutput` / `IStepValidationContext` — тестируется без Unity мини-раннером ядра.
- Ни одного патча Harmony: всё через сервисы ядра (`ModNewGame`, `ModTicks`, `ModMail`, `ModConditions`, `ModSave`, `ModProfile`, `ModCredit`).
- Точка входа — `[ModFeature] OnboardingFeature : IModFeature` в пространстве имён `PTMod.Features.FirstYearOnboarding`.
- Каждая задача на 2–4 часа и заканчивается проверяемым результатом.

### Development Approach
- **Testing Strategy**: TDD для `OnboardingMachine`, `StepCatalog`, `OnboardingSaveCodec` (`python mod/build.py --tests`; каждый публичный тип `Logic` указан в `[Covers]`); самопроверка мода на старте; ручные сценарии M1–M14 из design.md (выполняет владелец).
- **Integration Strategy**: фича обнаруживается ядром по `[ModFeature]` (без ручной регистрации), выключается в хабе «Моды» (`ModSettings.IsFeatureEnabled`).
- **Deployment Strategy**: `install.py --with-mods` (из `mod-framework`); контент встроен ресурсом, переопределяется из `<папка игры>/PTMod/content/first-year-onboarding/`.

**Ссылки на `mod-framework`.** Зависимость вида `mod-framework/TSK-0xx` — задача ядра (`.kiro/specs/mod-framework/tasks.md`), которая реализует нужный сервис: `FeatureContext`/`ModFeatures` — TSK-022; `ModHooks` (`Guard`, `Raise`) — TSK-023; `ModTicks` — TSK-024, `IsBatchRun`/`BatchEnded` — TSK-057; `ModSave` — TSK-025; `ModSettings`/`ModProfile` — TSK-014 (модель), TSK-021 (рантайм); `ModContent` — TSK-015, TSK-021; `ModL10n` — TSK-013, TSK-021, строки ядра — TSK-043; `ModConditions` — TSK-019 (движок), TSK-032 (адаптер); `ModNotify` — TSK-033; `ModUI` — TSK-034…036, `Confirm` — TSK-064, хаб — TSK-039; `ModMail` — TSK-037, TSK-038 (действия), TSK-062 (приоритеты, `Delivered`); `ModNewGame` — TSK-041, TSK-063 (секции); `ModCredit` — TSK-058; `ModIds` — TSK-066; мини-раннер тестов — TSK-067; самопроверка — TSK-042.

## Фаза 1 — Каркас и контент

- [ ] **TSK-001**: Каркас фичи (`IModFeature`) и параметры
  - **Requirement**: FR-13 (параметры), NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingFeature.cs` (`[ModFeature] sealed class OnboardingFeature : IModFeature`), `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingProfile.cs`
  - **Acceptance**: ядро находит фичу по `[ModFeature]` (`Id = first-year-onboarding`, `NameKey`/`DescriptionKey`, `DependsOn` пуст, `EnabledByDefault = true`, `SupportsHotToggle = true`); `Register(FeatureContext)` объявляет `ParamSpec` `pauseOnStep` и `remindersEnabled` (Bool, по умолчанию да); хаб показывает фичу и строит страницу настроек; `OnboardingProfile` читает и пишет `hasSeenTutorial` и `defaultToggle` в `ModProfile.Section("first-year-onboarding")` (`MarkDirty`) без своего поля версии; выключение фичи в хабе вызывает `OnDisabled` и останавливает опрос (проверка логом `ModLog`)
  - **Depends**: mod-framework/TSK-022, mod-framework/TSK-021, mod-framework/TSK-066

- [ ] **TSK-002**: Модель шагов и загрузчик `StepCatalog` с валидацией (Logic)
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/Logic/StepDefinition.cs` (включая `ConditionRef`), `.../Logic/StepMapper.cs`, `.../Logic/StepCatalog.cs`, `.../Logic/StepValidator.cs`, `mod/src/PTMod/Features/FirstYearOnboarding/StepRecord.cs` (`IContentRecord`), `mod/tests/PTMod.Tests/FirstYearOnboarding/StepCatalogTests.cs`
  - **Acceptance**: тесты из design.md (дубликат `id`, неизвестный трек, неизвестное условие через фейковый `IStepValidationContext`, отсутствующий ключ строки, `afterDays` вне 1..3650, `completeWhen` и `completeOnShow` одновременно, `args` вне белого списка, индекс `{n}` вне длины `args`, пустой трек) зелёные; битая запись пропускается, остальные загружаются; загрузка идёт через `ModContent.LoadRecords` (`steps.json`) и `ModContent.LoadConfig` (`config.json`)
  - **Depends**: mod-framework/TSK-015, mod-framework/TSK-019, mod-framework/TSK-067

- [ ] **TSK-003**: Предикаты `onb.*` и адаптер `GameOnboardingWorld`
  - **Requirement**: FR-4, FR-2 (определение трека)
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingPredicates.cs`, `.../GameOnboardingWorld.cs`
  - **Acceptance**: 12 предикатов `onb.*` из таблицы design.md (`onb.cpuProjectActive`, `onb.factoryProjectActive`, `onb.producedToday`, `onb.soldToday`, `onb.missedOverSold`, `onb.anyOldCpu`, `onb.oemAvailable`, `onb.automate`, `onb.foundryServices`, `onb.isJan1`, `onb.dayOfMonth`, `onb.stepDone`) зарегистрированы через `ModConditions.Register` и приняты ядром (имя `<prefix>.<name>`); остальные предикаты берутся у ядра; `IsBlocked` истинно при `ModTicks.IsLoading` / `IsFastForwarding` или ложном `IsGameReady`; `IsBatchRun` отражает `ModTicks.IsBatchRun`; `DetectTrack` возвращает `idm`/`fabless`/`foundry` по `Company.get_IsIdmCompany`/`get_IsFabless`/`get_IsFoundry`; `IsMailDelivered` читает `ModMail.Get(id).IsDelivered`
  - **Depends**: mod-framework/TSK-019, mod-framework/TSK-032, mod-framework/TSK-066, mod-framework/TSK-024

- [ ] **TSK-004**: Контент `steps.json` и `config.json`: три трека и шесть советов
  - **Requirement**: FR-3, FR-7
  - **Deliverables**: `mod/src/PTMod/Content/first-year-onboarding/steps.json` (`records[]`), `mod/src/PTMod/Content/first-year-onboarding/config.json`
  - **Acceptance**: файлы соответствуют таблице «Полный набор шагов» design.md (idm — 10, fabless — 10, foundry — 7 последовательных шагов, 6 советов; приоритет `critical` у `welcome`, `tip_debt`, `tip_default`); условия записаны в нотации `ModConditions` (`{type, …}`, `all`/`any`); проходят `StepCatalog.Build` без ошибок
  - **Depends**: TSK-002, TSK-003

- [ ] **TSK-005**: Тексты обучения ru/en
  - **Requirement**: FR-5, NFR-4, NFR-5
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (ключи `onb.*`)
  - **Acceptance**: для каждого шага есть `title` (с префиксом «[Наставник]»/«[Mentor]»), `body` (и варианты `.fabless`/`.foundry`/`.rec`, где указано), подсказки, действия, строки хаба, `onb.start.notify`, `onb.toggle.*`, `onb.hint.none`; каждое тело — 3–6 абзацев, ≤ 1200 символов, заканчивается «Что сделать сейчас»; плейсхолдеры индексные (`{0}`…), число индексов не больше длины `args`; числа в текстах совпадают с таблицей «Числа, которые обучение цитирует»; расхождений ключей ru/en нет (`install.py --check`)
  - **Depends**: TSK-004, mod-framework/TSK-043

## Фаза 2 — Логика обучения

- [ ] **TSK-006**: `OnboardingMachine`: последовательные шаги, защёлки, «догоняющий» режим, лимит писем, итог (Logic)
  - **Requirement**: FR-2, FR-4, FR-9 (п. 5–6), FR-11 (п. 1, 4)
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/Logic/OnboardingMachine.cs`, `.../Logic/OnboardingState.cs`, `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingMachineTests.cs`
  - **Acceptance**: тесты «полный проход трека idm», «защёлка», «догоняющий режим (1 письмо со строкой «Уже сделано»)», «≤ 1 письма за игровой день», «письмо предыдущего шага не доставлено → следующее не уходит», «`IsBlocked` → ни одного вывода», «итог после последнего шага» зелёные
  - **Depends**: TSK-002, mod-framework/TSK-067

- [ ] **TSK-007**: Напоминания, ситуативные советы, срок обучения, прерывание, пакетный режим (Logic)
  - **Requirement**: FR-6, FR-7, FR-9 (п. 4), FR-11 (п. 2–3)
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/Logic/OnboardingMachine.cs` (расширение), `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingRemindersTests.cs`
  - **Acceptance**: напоминание показывается ровно один раз на `afterDays`, отсчёт от `DeliveredOn`, не больше одного в день; совет со `streakDays = 7` срабатывает на 7-й день подряд и сбрасывается при разрыве; совет откладывается, если письмо уже было в этот день; при `IsBatchRun = true` 30 вызовов `OnGameDay` не дают писем, а один `OnBatchEnded` — ровно одно; на 731-й день — итог; `Abort` даёт `Completed{Aborted=true}` без итогового письма
  - **Depends**: TSK-006

- [ ] **TSK-008**: Пропуск шага и обучения, повтор письма, перезапуск, подсказка (Logic)
  - **Requirement**: FR-8, FR-13 (п. 2–3), FR-5 (п. 6)
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/Logic/OnboardingMachine.cs` (`HandleAction`, `Restart`), `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingActionsTests.cs`
  - **Acceptance**: `skipStep` → шаг `Skipped`, следующий шаг на следующий игровой день; `skipAll` → `Skipped`, писем больше нет, `Completed` с `wasSkipped`; перезапуск засчитывает выполненное «догоняющим» режимом; повтор шлёт письмо с тегом `onb:<stepId>:repeat`; `hint` показывает ближайшую непоказанную подсказку, когда они кончились — `SetHintAvailable(false)`; неизвестный action id игнорируется
  - **Depends**: TSK-006

- [ ] **TSK-009**: Секция `ModSave`: кодек, участник сейва, профиль (Logic + адаптер)
  - **Requirement**: FR-10, FR-9 (п. 3, 7), NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/Logic/OnboardingSaveCodec.cs`, `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingSaveParticipant.cs` (`IModSaveParticipant`), `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingSaveCodecTests.cs`
  - **Acceptance**: круговая сериализация состояния из примера design.md без потерь; `Write()` не содержит поля версии (`_schema` ставит ядро); нет секции → `ResetToDefaults()` → `Off`; секция новее → `ResetToDefaults()`, исходный JSON не меняется (passthrough ядра); неизвестный статус → `Pending`; запись шага, которого нет в контенте, сохраняется; `SectionId = first-year-onboarding`, `SchemaVersion = 1`
  - **Depends**: TSK-006, mod-framework/TSK-025, mod-framework/TSK-010

- [ ] **TSK-010**: Сценарные юнит-тесты автомата на фейковом мире
  - **Requirement**: FR-4, FR-9, FR-11
  - **Deliverables**: `mod/tests/PTMod.Tests/FirstYearOnboarding/FakeOnboardingWorld.cs`, `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingScenarioTests.cs`
  - **Acceptance**: прогон сценариев «новичок по шагам», «игрок опережает обучение», «пакетный прогон 30 дней (`IsBatchRun` → `BatchEnded`)», «сохранение → загрузка в середине шага (через кодек)», «смена трека fabless → idm», «банкротство» — все зелёные; проверяется журнал отправленных писем (теги, порядок, даты)
  - **Depends**: TSK-007, TSK-008, TSK-009

## Фаза 3 — Интеграция с игрой

- [ ] **TSK-011**: `OnboardingDirector`: подписки на сервисы ядра, опрос 2 Гц, защиты
  - **Requirement**: FR-2, FR-9, FR-11 (п. 3), NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingDirector.cs`
  - **Acceptance**: подписки на `ModTicks.GameReady`, `ctx.OnTick(TickPhase.DayPassed)`, `ModTicks.BatchEnded`, `ModMail.Delivered`, `ModMail.RegisterActionHandler`; на `GameReady` ставится подписка на `BankruptcyHandler.OnCompanyWentBankrupt` сцены; опрос — корутина `PTModHost.Run` с `WaitForSecondsRealtime(0,5)`, пропуск при `IsBlocked` и `IsBatchRun`, выход при `OnDisabled`; итоговый режим считается на `GameReady(NewGame)` (`PendingMode` перекрывает `PendingStart`); каждый вход обёрнут в `ModHooks.Guard` (собственных счётчиков ошибок нет)
  - **Depends**: TSK-003, TSK-006, TSK-009, mod-framework/TSK-022, mod-framework/TSK-023, mod-framework/TSK-024, mod-framework/TSK-057, mod-framework/TSK-038, mod-framework/TSK-062

- [ ] **TSK-012**: `OnboardingMailer`, `PlaceholderResolver`, действия писем, пауза по доставке
  - **Requirement**: FR-5, FR-6 (подсказки), FR-8 (кнопки), FR-12
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingMailer.cs`, `.../GamePlaceholderSource.cs`, `.../Logic/PlaceholderResolver.cs`, `.../Logic/PausePolicy.cs`, `mod/tests/PTMod.Tests/FirstYearOnboarding/PlaceholderResolverTests.cs`, `mod/tests/PTMod.Tests/FirstYearOnboarding/PausePolicyTests.cs`
  - **Acceptance**: письма уходят через `ModMail.Send` (`Tag = onb:<stepId>`, `Priority` из шага, ID хранится в состоянии шага) с заголовком `[Наставник] …`; аргументы подставляются из источников таблицы design.md (`Market.get_HighEndBudget`, `get_MidRangeBudget`, `get_LowEndBudget`, `get_IndustriesBudget`, `ModCredit.PlayerDebtLimit()`, `BankruptcyHandler.defaultTimeLimit` с фолбэком 120), деньги — `StringFormatter.FloatToMoney`; действия `ModMailAction` («Пропустить шаг» и «Пропустить обучение» — `Repeatable = false`, «Подсказка», «Открыть конструктор CPU», «Открыть расширение фабрики» — `Repeatable = true`) вызывают `CreatorManager.OpenCreatorWindowOfType` и не закрывают письмо; пауза через `DateController.ManualPause(false)` по событию `ModMail.Delivered`, только если шаг имеет `pauseOnShow`, `pauseOnStep = true` и время идёт; подсказки и уведомление на старте — через `ModNotify`; тесты резолвера и политики паузы зелёные
  - **Depends**: TSK-005, TSK-006, mod-framework/TSK-037, mod-framework/TSK-038, mod-framework/TSK-062, mod-framework/TSK-033, mod-framework/TSK-058, mod-framework/TSK-013

- [ ] **TSK-013**: Опция «Обучение» и секция в окне новой игры (`ModNewGame`), `PendingStart`
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingNewGame.cs`, `.../PendingStart.cs`
  - **Acceptance**: `ModNewGame.RegisterOption` (`FeatureId = first-year-onboarding`, `Id = tutorial`, `Toggle`, `DefaultOn = !hasSeenTutorial || defaultToggle`) показывает переключатель на панели «Моды…»; `ModNewGame.RegisterSection(…, order: 20)` показывает строку «Задано сценарием: …» при `PendingMode` ≠ `Allowed` и обновляется по `OnboardingService.PendingModeChanged`; `BeforeStart` читает `ModNewGame.GetToggle`, пишет `defaultToggle` в профиль и `PendingStart`, `PlayerInitialData` не меняет; при выключенной фиче опции нет; патчей на `MenuGameSetup.*` и `GameManager.StartNewGame` нет
  - **Depends**: TSK-001, TSK-014, mod-framework/TSK-041, mod-framework/TSK-063, mod-framework/TSK-021

- [ ] **TSK-014**: API `OnboardingService` для других фич
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingService.cs`, `mod/tests/PTMod.Tests/FirstYearOnboarding/OnboardingServiceTests.cs`
  - **Acceptance**: `SetPendingMode` действует только на ближайшую новую игру и сбрасывается в `Allowed` после `GameReady(NewGame)`; поднимает `PendingModeChanged`; событие `Completed` поднимается при завершении, пропуске и прерывании через `ModHooks.Raise` — исключение подписчика изолировано и засчитано его фиче; `GetStatus` возвращает режим, трек, номер и ID текущего шага
  - **Depends**: TSK-006, mod-framework/TSK-023

- [ ] **TSK-015**: Страница «Обучение» в хабе «Моды»
  - **Requirement**: FR-13
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingHubPage.cs`
  - **Acceptance**: `ctx.HubPage("onb.hub.title", …)` показывает режим, трек, «Шаг N из M»; кнопки «Повторить письмо шага», «Пропустить шаг», «Пропустить обучение», «Перезапустить обучение» (`ModUI.Confirm`, время на паузе) работают; параметры «Пауза при новом шаге» и «Подсказки-напоминания» применяются сразу (страница настроек строится ядром из `ParamSpec`); для старого сейва статус `onb.hub.status.oldSave`
  - **Depends**: TSK-008, TSK-014, mod-framework/TSK-039, mod-framework/TSK-064

## Фаза 4 — Проверка

- [ ] **TSK-016**: Самопроверка на старте
  - **Requirement**: NFR-2, NFR-4, FR-3 (п. 3)
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingSelfCheck.cs` (вклад в `ModSelfCheck`)
  - **Acceptance**: в `Player.log` строка `[PTMod] [first-year-onboarding] self-check: OK` или список проблем: члены игры `CreatorManager.OpenCreatorWindowOfType`, `DateController.ManualPause`, `Market.get_HighEndBudget`/`get_MidRangeBudget`/`get_LowEndBudget`/`get_IndustriesBudget`, поле `BankruptcyHandler.defaultTimeLimit` (фолбэк 120), приём предикатов `onb.*` ядром, валидность каталога, наличие всех ключей `onb.*` в `ru`; при битом каталоге страница хаба показывает `onb.error.content`
  - **Depends**: TSK-011, TSK-012, TSK-013, mod-framework/TSK-042

- [ ] **TSK-017**: Замер производительности опроса
  - **Requirement**: NFR-1
  - **Deliverables**: `mod/src/PTMod/Features/FirstYearOnboarding/OnboardingDirector.cs` (счётчики времени за флагом отладки `ModSettings.Debug`), запись результатов в `mod/tests/PTMod.Tests/FirstYearOnboarding/PERF.md`
  - **Acceptance**: при включённом флаге отладки в `Player.log` раз в игровой месяц пишутся среднее и максимум `Poll` и `OnGameDay`; на треке idm за 2 игровых года среднее `Poll` ≤ 0,2 мс, `OnGameDay` ≤ 0,1 мс; при перемотке новой игры 2000 года обработчики обучения не вызываются (0 записей)
  - **Depends**: TSK-011

- [ ] **TSK-018**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-14, NFR-3
  - **Deliverables**: отметки прохождения сценариев M1–M14 из design.md в этом файле (раздел Progress) и фрагменты `Player.log` при ошибках
  - **Acceptance**: все сценарии M1–M13 пройдены с ожидаемым результатом (M14 — после реализации `scenarios-achievements`); 0 исключений `[first-year-onboarding]` в `Player.log`; 5 циклов «сохранить → загрузить» без дубликатов писем
  - **Depends**: TSK-010, TSK-015, TSK-016, TSK-017

- [ ] **TSK-019**: Документация для игроков и авторов контента
  - **Requirement**: FR-3 (п. 6), NFR-4
  - **Deliverables**: `mod/src/PTMod/Content/first-year-onboarding/README.md` (формат `steps.json` и `config.json`, список предикатов ядра и `onb.*`, плейсхолдеры, как переопределить контент по `id`)
  - **Acceptance**: README описывает все поля шага, треки, предикаты из design.md и путь переопределения `<папка игры>/PTMod/content/first-year-onboarding/`; пример из README проходит `StepCatalog.Build`
  - **Depends**: TSK-004

## Dependency graph

```
mod-framework (TSK-022 FeatureContext, TSK-023 ModHooks, TSK-024 ModTicks, TSK-057 IsBatchRun,
               TSK-025 ModSave, TSK-021 ModSettings/ModProfile, TSK-015 ModContent, TSK-019/032 ModConditions,
               TSK-037/038/062 ModMail, TSK-033 ModNotify, TSK-041/063 ModNewGame, TSK-058 ModCredit,
               TSK-039/064 ModUI, TSK-042 ModSelfCheck, TSK-067 мини-раннер)
   │
   ├──> TSK-001 ──> TSK-013 (+ TSK-014)
   ├──> TSK-003
   └──> TSK-002 ──┬──> TSK-004 (+ TSK-003) ──┬──> TSK-005
                  │                          └──> TSK-019
                  └──> TSK-006 ──┬──> TSK-007 ──┐
                                 ├──> TSK-008 ──┼──> TSK-010
                                 ├──> TSK-009 ──┘
                                 ├──> TSK-012      (+ TSK-005)
                                 └──> TSK-014 ──> TSK-015 (+ TSK-008)

TSK-003 + TSK-006 + TSK-009 ──> TSK-011 ──> TSK-017
TSK-011 + TSK-012 + TSK-013 ──> TSK-016
TSK-010 + TSK-015 + TSK-016 + TSK-017 ──> TSK-018
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи (`IModFeature`) и параметры | Pending |
| TSK-002 Модель шагов и `StepCatalog` | Pending |
| TSK-003 Предикаты `onb.*` и `GameOnboardingWorld` | Pending |
| TSK-004 Контент `steps.json` и `config.json` | Pending |
| TSK-005 Тексты ru/en | Pending |
| TSK-006 `OnboardingMachine`: ядро | Pending |
| TSK-007 Напоминания, советы, срок, прерывание, пакетный режим | Pending |
| TSK-008 Пропуск, повтор, перезапуск, подсказка | Pending |
| TSK-009 Секция `ModSave`, кодек, профиль | Pending |
| TSK-010 Сценарные тесты автомата | Pending |
| TSK-011 `OnboardingDirector` | Pending |
| TSK-012 Письма, плейсхолдеры, действия, пауза | Pending |
| TSK-013 Опция и секция в окне новой игры | Pending |
| TSK-014 API `OnboardingService` | Pending |
| TSK-015 Страница в хабе «Моды» | Pending |
| TSK-016 Самопроверка | Pending |
| TSK-017 Замер производительности | Pending |
| TSK-018 Ручная проверка в игре (владелец) | Pending |
| TSK-019 Документация контента | Pending |

## Трассировка FR → TSK

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-001, TSK-013, TSK-018 |
| FR-2 | TSK-003, TSK-006, TSK-011, TSK-012 |
| FR-3 | TSK-002, TSK-004, TSK-016, TSK-019 |
| FR-4 | TSK-003, TSK-006, TSK-010 |
| FR-5 | TSK-005, TSK-008, TSK-012 |
| FR-6 | TSK-007, TSK-012 |
| FR-7 | TSK-004, TSK-007 |
| FR-8 | TSK-008, TSK-012 |
| FR-9 | TSK-006, TSK-007, TSK-009, TSK-010, TSK-011 |
| FR-10 | TSK-009 |
| FR-11 | TSK-006, TSK-007, TSK-011 |
| FR-12 | TSK-012 |
| FR-13 | TSK-001, TSK-008, TSK-015 |
| FR-14 | TSK-013, TSK-014 |
| NFR-1 | TSK-017 |
| NFR-2 | TSK-001, TSK-011, TSK-016 |
| NFR-3 | TSK-009, TSK-018 |
| NFR-4 | TSK-005, TSK-016, TSK-019 |
| NFR-5 | TSK-005, TSK-006 |
| NFR-6 | TSK-002, TSK-009 |
