# Tasks: scenarios-achievements

## Document Information

- **Feature Name**: scenarios-achievements (I18)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/scenarios-achievements/requirements.md`
  - Design: `.kiro/specs/scenarios-achievements/design.md`
  - Ядро: `.kiro/specs/mod-framework/{design,tasks}.md`; решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Фича большая (L). План построен так, чтобы рано получить проверяемое ядро:

1. Модель контента и валидатор, затем контент (8 сценариев, 8 испытаний, 37 достижений) и тексты.
2. Чистая логика с юнит-тестами мини-раннера: метрики, цели, гонки, провалы, счёт, испытания, достижения, assisted, кодек секции, модель профиля.
3. Интеграция с игрой через сервисы ядра: выборка метрик, события выпуска CPU, старт сценария в `ModNewGame.BeforeStart`, `RunDirector`, затем окна.
4. Самопроверка, замер производительности, плейтест баланса и ручная проверка владельцем.

### Implementation Strategy
- Вся логика решений — за интерфейсами (`IGameMetricsSource`, `IConditionAdapter`, `IValidationContext`) и тестируется без Unity мини-раннером ядра; файлы логики лежат в `mod/src/PTMod/Features/ScenariosAchievements/Logic/`, тесты — в `mod/tests/PTMod.Tests/ScenariosAchievements/`.
- Точка входа — `[ModFeature] ScenariosFeature : IModFeature` в пространстве имён `PTMod.Features.ScenariosAchievements`.
- Источник истины старта — `ModNewGame.BeforeStart`; UI меню — выбор и предпросмотр (секция панели «Моды…»).
- Собственные патчи — два: `ContractManager.PromoteContractToActive` (postfix) и `GameplaySettingsWindow.ChangeDifficulty` (prefix). Остальное — сервисы ядра (`ModEvents`, `ModTicks`, `ModNewGame`, `ModProfile`, `ModCredit`).
- Модификаторы — только стандартные каналы реестра `ModModifiers`: `Add` один раз на старте прогона, ID записей хранятся в секции прогона, заново после загрузки не регистрируются.
- Задачи по 2–4 часа, каждая с проверяемым результатом.

### Development Approach
- **Testing Strategy**: TDD для чистой логики (TSK-006…TSK-015; `python mod/build.py --tests`, каждый публичный тип `Logic` указан в `[Covers]`); самопроверка на старте (TSK-029); ручные сценарии S1–S17 (TSK-032) и плейтест баланса (TSK-031) выполняет владелец.
- **Integration Strategy**: фича обнаруживается ядром по `[ModFeature]`, выключается в хабе «Моды»; связь с `first-year-onboarding` — через `OnboardingBridge` (необязательная), с `rival-acquisitions` — через `AcquisitionBridge` (необязательная).
- **Deployment Strategy**: `install.py --with-mods`; контент встроен ресурсами, переопределяется из `<папка игры>/PTMod/content/scenarios-achievements/` по `id` записей.

**Ссылки на `mod-framework`.** Зависимость вида `mod-framework/TSK-0xx` — задача ядра (`.kiro/specs/mod-framework/tasks.md`): `FeatureContext`/`ModFeatures` — TSK-022; `ModHooks` (`Guard`, `Raise`, `ctx.Patch`) — TSK-023; `ModTicks` — TSK-024, `IsBatchRun`/`BatchEnded` — TSK-057; `ModSave` — TSK-025; `ModSettings`/`ModProfile` — TSK-014 (модель), TSK-021 (рантайм); `ModContent` — TSK-015, TSK-021; `ModL10n` — TSK-013, TSK-043; `ModConditions` — TSK-019, TSK-032; `ModModifiers` — TSK-017 (реестр, `Segment`), TSK-027 (фасад), TSK-059 (`IsDeclared`, причины, ленивое связывание); `ModCredit` — TSK-058; `ModCompanies` — TSK-026; `ModEvents` — TSK-056; `ModNotify` — TSK-033; `ModUI` — TSK-034…036, `Confirm` — TSK-064, хаб — TSK-039; `ModMail` — TSK-037, TSK-038, TSK-062; `ModNewGame` — TSK-041, TSK-063; `ModIds` — TSK-066; мини-раннер — TSK-067; самопроверка — TSK-042.

## Фаза 1 — Каркас, модель и контент

- [ ] **TSK-001**: Каркас фичи (`IModFeature`) и параметры
  - **Requirement**: FR-10 (п. 3), FR-15 (п. 7), NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/ScenariosFeature.cs` (`[ModFeature] sealed class ScenariosFeature : IModFeature`)
  - **Acceptance**: ядро находит фичу по `[ModFeature]` (`Id = scenarios-achievements`, `NameKey`/`DescriptionKey`, `DependsOn` пуст, `EnabledByDefault = true`, `SupportsHotToggle = true`); `Register(FeatureContext)` объявляет `ParamSpec` `strictAssisted` (по умолчанию нет), `trackerAutoOpen` (да), `toastsEnabled` (да); хаб показывает фичу и страницу настроек; выключение фичи вызывает `OnDisabled` (окна закрываются, проверки останавливаются; проверка логом `ModLog`)
  - **Depends**: mod-framework/TSK-022, mod-framework/TSK-021, mod-framework/TSK-023, mod-framework/TSK-066

- [ ] **TSK-002**: Модель контента и `ContentValidator` (Logic)
  - **Requirement**: FR-1, FR-11 (п. 1), FR-12 (п. 1), NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Content/{ScenarioDefinition,ChallengeDefinition,AchievementDefinition,CheckSpec,ContentValidator,ContentMapper}.cs`, `mod/src/PTMod/Features/ScenariosAchievements/Content/{ScenarioCatalog,ScenarioRecord,ChallengeRecord,AchievementRecord}.cs` (загрузка через `ModContent.LoadRecords` / `LoadConfig`, обёртки `IContentRecord`), `mod/tests/PTMod.Tests/ScenariosAchievements/ContentValidatorTests.cs`
  - **Acceptance**: тесты на каждый пункт «Validation Rules» design.md (год вне набора, деньги вне диапазона, Impossible с не-IDM, `fabless` с линиями > 0, нет обязательных целей, дубликат ID, неизвестная метрика/событие, канал не объявлен в реестре ядра (`IsKnownChannel`), условие `condition` с неизвестным предикатом, пороги медалей не по возрастанию, `by` вне интервала) зелёные; вычисляется `ContentHash`, флаг `IsCustom` для переопределения с другим хешем
  - **Depends**: mod-framework/TSK-015, mod-framework/TSK-019, mod-framework/TSK-059, mod-framework/TSK-067

- [ ] **TSK-003**: Контент — 8 сценариев
  - **Requirement**: FR-2, NFR-8
  - **Deliverables**: `mod/src/PTMod/Content/scenarios-achievements/scenarios.json` (`{ "schema": 1, "records": [ … ] }`, 8 записей: `garage-1975`, `pc-boom-1980`, `fabless-1985`, `comeback-1985`, `foundry-1990`, `mhz-race-1990`, `ghz-race-1995`, `power-wall-2000`)
  - **Acceptance**: числа совпадают с таблицами «Стартовые параметры» и «Цели, дедлайны, медали» design.md; все 8 записей проходят `ContentValidator`; пороги медалей посчитаны по правилу design.md (бронза = R, серебро = R + 0,5E, золото = R + 0,85E); названия компаний в контенте не заданы
  - **Depends**: TSK-002

- [ ] **TSK-004**: Контент — 8 испытаний, 37 достижений, параметры
  - **Requirement**: FR-11, FR-12, NFR-8
  - **Deliverables**: `mod/src/PTMod/Content/scenarios-achievements/challenges.json`, `mod/src/PTMod/Content/scenarios-achievements/achievements.json` (формат `records[]`), `mod/src/PTMod/Content/scenarios-achievements/config.json` (`challengeYears`)
  - **Acceptance**: испытания соответствуют таблице «Контент: испытания» (каналы, множители, правила провала, бонусы, ограничения по типу компании; `tight-credit` с пометкой зависимости от `ModCredit.IsPerCompanyActive`); 37 достижений соответствуют таблице design.md (ID, условие, категория, скрытые: `phoenix`, `ahead-of-time`, `peoples-cpu`, `last-standing`); все файлы проходят `ContentValidator`
  - **Depends**: TSK-002

- [ ] **TSK-005**: Тексты ru/en
  - **Requirement**: FR-2 (п. 3), NFR-5, NFR-6
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (ключи `scn.*`, `chl.*`, `ach.*`)
  - **Acceptance**: для каждого сценария — `title`, `desc` (≤ 6 строк), `briefing`, тексты целей; для каждого испытания — `title`, `desc`, `fail`; для каждого достижения — `title`, `desc`; общие строки UI из таблицы ключей design.md (включая `scn.difficulty.locked`, `scn.badge.assisted.feature`, `chl.tight-credit.unavailable`); плейсхолдеры индексные (`{0}`…); самопроверка не находит отсутствующих ключей
  - **Depends**: TSK-003, TSK-004, mod-framework/TSK-043

## Фаза 2 — Чистая логика

- [ ] **TSK-006**: Метрики: `RollingWindow`, `MetricsState`, `MetricsCalculator`, `MetricsSnapshot` (Logic)
  - **Requirement**: FR-5 (п. 1), FR-16 (п. 1)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Metrics/{RollingWindow,MetricsState,MetricsCalculator,MetricsSnapshot,DaySample}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/RollingWindowTests.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/MetricsCalculatorTests.cs`
  - **Acceptance**: все метрики из таблицы design.md вычисляются из `DaySample`/`MonthSample`; окна долей индексируются `Segment` (`[0]` — все сегменты, `[1 + (int)Segment]`); тесты окна (кольцо 30 дней, ноль в знаменателе = 0), серий (прибыль, платёжеспособность, отрицательные дни, без упущенных, лидер IPS), `money.min`, `default.recoveredCount`, `share.minSegment`, исключения перенесённых CPU из метрик характеристик зелёные
  - **Depends**: TSK-002, mod-framework/TSK-017

- [ ] **TSK-007**: `GoalEvaluator`: виды целей, `holdDays`, `by`, прогресс (Logic)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Scenario/{GoalEvaluator,GoalState,IConditionAdapter}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/GoalEvaluatorTests.cs`
  - **Acceptance**: тесты видов `metric` (с `holdDays`), `cpuReleased` (частота, ядра, Вт, SMT, тип компании в момент выпуска, перенесённые CPU не считаются), `event`, `condition` (через фейковый адаптер над `ConditionRef`), `all`; провал по `by`; прогресс 0..1 для `percent`/`money`/`count` зелёные
  - **Depends**: TSK-006

- [ ] **TSK-008**: `RaceResolver` (Logic)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Scenario/{RaceResolver,RaceBook}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/RaceResolverTests.cs`
  - **Acceptance**: игрок первым → цель `Done`; ИИ первым → `Failed` с именем и датой, при `failOnLose` — сигнал поражения; выпуски до старта прогона не учитываются; два выпуска в один день — побеждает пришедший первым
  - **Depends**: TSK-007

- [ ] **TSK-009**: `FailEvaluator` и `ScenarioRuntime` (победа, поражение, дедлайн) (Logic)
  - **Requirement**: FR-5 (п. 2–4), FR-7
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Scenario/{FailEvaluator,ScenarioRuntime,ScenarioState}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/ScenarioRuntimeTests.cs`
  - **Acceptance**: победа по обязательным целям до дедлайна; поражение по дедлайну («Время вышло»), по банкротству, по `fail[]` с `holdDays`, по проигранной гонке; после итога статусы не меняются (E8); события «цель выполнена/провалена» для уведомлений
  - **Depends**: TSK-007, TSK-008

- [ ] **TSK-010**: `ScoreCalculator` и медали (Logic)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Scenario/ScoreCalculator.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/ScoreCalculatorTests.cs`
  - **Acceptance**: пример AC-8.1 даёт 600 и «серебро»; бонус испытаний аддитивный (15% + 10% = ×1,25); поражение = 0; значения на границах порогов получают медаль порога
  - **Depends**: TSK-009

- [ ] **TSK-011**: `ChallengeRuntime` (Logic)
  - **Requirement**: FR-11 (п. 2–5)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Challenges/{ChallengeRuntime,ChallengeStates}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/ChallengeRuntimeTests.cs`
  - **Acceptance**: провал в свободной игре → событие `challenge.failed` и команда снять записи модификаторов по ID; прохождение через `challengeYears` = 10 лет или в 2030 году → `challenge.passed`; несовместимость с типом компании, с `allowedChallenges` и «Жёсткий кредит» при `IsPerCompanyActive == false` отклоняется
  - **Depends**: TSK-006

- [ ] **TSK-012**: `AchievementEvaluator` (Logic)
  - **Requirement**: FR-12 (п. 2–7)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Achievements/{AchievementEvaluator,EventBook}.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/AchievementEvaluatorTests.cs`
  - **Acceptance**: каждый вид проверки (`event`, `metric`, `cpuReleased`, `all`, `profile`, `require`) покрыт тестом; уже полученные не проверяются; `countsOnlyNewGames` игнорирует `legacy`-прогоны; при > 3 новых — 3 + «и ещё N»; поглощённая компания не увеличивает `rivals.bankruptCount`
  - **Depends**: TSK-006, TSK-007

- [ ] **TSK-013**: `AssistedRegistry` (Logic)
  - **Requirement**: FR-15 (п. 1–2, 4, 6–8)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Assisted/AssistedRegistry.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/AssistedRegistryTests.cs`
  - **Acceptance**: флаг «липкий» (снять нельзя); причины без дублей, длина ≤ 64 (`cheats.enabled`, `feature.disabled:<id>`, причины других фич через `Mark`); при `strictAssisted = true` открытие достижений блокируется, иначе они открываются с пометкой; рекорды в assisted-прогоне не обновляются
  - **Depends**: TSK-001, mod-framework/TSK-067

- [ ] **TSK-014**: `RunState`, `RunSaveCodec` и участник сейва `RunSaveParticipant` (секция `ModSave`)
  - **Requirement**: FR-16, FR-1 (п. 4), NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Run/{RunState,RunSaveCodec}.cs`, `mod/src/PTMod/Features/ScenariosAchievements/Integration/RunSaveParticipant.cs` (`IModSaveParticipant`), `mod/tests/PTMod.Tests/ScenariosAchievements/RunSaveCodecTests.cs`
  - **Acceptance**: круговая сериализация примера секции design.md без потерь (снимок сценария, окна долей, серии, гонки, флаги, `featuresAtStart`, ID записей модификаторов, `transferredCpuIds`, ID писем); в записанной секции нет поля версии (`_schema` ставит ядро); нет секции или секция новее → `ResetToDefaults()` (прогона нет, `legacy` рождается на `GameReady(Loaded)`); неизвестный статус → значение по умолчанию; `SectionId = scenarios-achievements`, `SchemaVersion = 1`
  - **Depends**: TSK-006, TSK-009, mod-framework/TSK-025, mod-framework/TSK-010

- [ ] **TSK-015**: `ProfileModel` над `ModProfile` и адаптер `ProfileGate`
  - **Requirement**: FR-13, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Logic/Profile/ProfileModel.cs`, `mod/src/PTMod/Features/ScenariosAchievements/Integration/ProfileGate.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/ProfileModelTests.cs`
  - **Acceptance**: `ProfileModel` работает над `JsonObject` секции `ModProfile.Section("scenarios-achievements", 1)`: `Unlock` не открывает повторно, `RecordResult` не обновляет лучший результат для assisted/custom, история обрезается до 50, `Clear` очищает секцию, `Revision` растёт только при изменении; `ProfileGate` вызывает `ModProfile.MarkDirty` после изменений, `ModProfile.Save()` — при показе итогов и сбросе, отдаёт `ModProfile.Status`; своих файлов, `.bak` и троттлинга в фиче нет
  - **Depends**: TSK-001, mod-framework/TSK-014, mod-framework/TSK-021

## Фаза 3 — Интеграция с игрой

- [ ] **TSK-016**: `GameMetricsSource` — выборка из игры
  - **Requirement**: FR-5, FR-9 (п. 2), NFR-1
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Integration/GameMetricsSource.cs`
  - **Acceptance**: `SampleDay` — один проход по `CpuDataProvider.Instance.GetAllCpus(reusableList)` без аллокаций (суммы `SoldLastDay*` по сегментам `Segment`, лидер по `Ips`, популярность игрока, исключение CPU из `transferredCpuIds`) плюс чтение компании игрока (`get_MoneyAmount`, `get_IsInDefault`, `get_Factory().ProductionCapacity`, `ContractHelper.PlayerContracts()`, `BusinessContractManager.Instance.GetSignedContracts`); `SampleLastFullMonth` ищет отчёт `CompanyFinance.Reports` по (Year, Month); `BuildComparison` — по `DataFinder.FindAllAICompanies(true)` и `CpuBase.Market.Sold[Count−2]`
  - **Depends**: TSK-006, mod-framework/TSK-026, mod-framework/TSK-017

- [ ] **TSK-017**: События игры: `ModEvents`, OEM-контракты, банкротства
  - **Requirement**: FR-5 (п. 6), FR-6, FR-7 (п. 1), FR-12
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Integration/GameEventHooks.cs`, `mod/src/PTMod/Features/ScenariosAchievements/Integration/ScenarioPatches.cs` (postfix `ContractManager.PromoteContractToActive`), `mod/src/PTMod/Features/ScenariosAchievements/Integration/AcquisitionBridge.cs`
  - **Acceptance**: подписки на `ModEvents.CpuReleased` (игнор при `duringFastForward`) и `ModEvents.CpuTransferred` (SaveID перенесённых игроку CPU в `transferredCpuIds`); патчей на `Company.AddCpu` и `AICompany.AddCpu` нет; постфикс `ContractManager.PromoteContractToActive` выходит первой строкой при `ModTicks.IsLoading` или `IsFastForwarding`; подписки на `BankruptcyHandler.OnCompanyWentBankrupt` (игрок → поражение, ИИ → счётчик, кроме компаний, поглощённых игроком по `AcquisitionBridge`) и `BusinessContractManager.OnPlayerSignedContract` ставятся в обработчике `GameReady`; при загрузке сейва гонки не срабатывают (AC-6.2)
  - **Depends**: TSK-008, TSK-012, mod-framework/TSK-056, mod-framework/TSK-023, mod-framework/TSK-024; rival-acquisitions/TSK-017 (мягкая: `IsAbsorbedByPlayer`)

- [ ] **TSK-018**: Запрет понижения сложности и причины assisted
  - **Requirement**: FR-15 (п. 1–3, 5)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Integration/DifficultyGuard.cs` (prefix `GameplaySettingsWindow.ChangeDifficulty`, `Optional`), `mod/src/PTMod/Features/ScenariosAchievements/Integration/FeatureWatch.cs`, `mod/tests/PTMod.Tests/ScenariosAchievements/DifficultyGuardTests.cs`
  - **Acceptance**: при активном сценарии понижение сложности откатывается (список и `Player.DifficultyLevel`), показывается `scn.difficulty.locked`, assisted не ставится; повышение и свободная игра не затронуты; `FeatureWatch` по `ModFeatures.StateChanged` ставит `feature.disabled:<id>` для фич из `featuresAtStart` при активном сценарии или испытании (включение фич и `Faulted` не помечаются); читы на старте (`Player.EnableCheats`) → `cheats.enabled`; патчей на `Cheats.AddMoney` и `DateController.NextMonth` нет
  - **Depends**: TSK-013, mod-framework/TSK-023, mod-framework/TSK-022

- [ ] **TSK-019**: `RunDirector` — жизненный цикл прогона
  - **Requirement**: FR-16, FR-4 (п. 2), FR-1 (п. 4), NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Integration/RunDirector.cs`, `.../Integration/ModifierBinder.cs`, `.../Integration/OnboardingBridge.cs`
  - **Acceptance**: на `ModTicks.GameReady(NewGame)` создаётся `RunState` (runId, снимок сценария, испытания, assisted, `featuresAtStart`); модификаторы добавляются `ModModifiers.Add` один раз, ID записей хранятся в секции и после `GameReady(Loaded)` заново не регистрируются; `legacy`-прогон для сейва без секции или с секцией, которую ядро сбросило; подписки через `ctx.OnTick` на `DayPassed`/`LateMonthPassed`/`YearPassed` и `ModTicks.BatchEnded`; отдельный `ModHooks.Guard` на блоки метрик, сценария, испытаний, достижений; выключение фичи останавливает проверки, записи модификаторов остаются (не применяются); `OnboardingBridge` вызывает `OnboardingService.SetPendingMode` и подписывается на `Completed`, ничего не делая без фичи обучения
  - **Depends**: TSK-009, TSK-011, TSK-012, TSK-014, TSK-015, TSK-016, mod-framework/TSK-024, mod-framework/TSK-025, mod-framework/TSK-027, mod-framework/TSK-057; first-year-onboarding/TSK-014 (мягкая)

- [ ] **TSK-020**: `ScenarioStarter` — применение старта в `ModNewGame.BeforeStart`
  - **Requirement**: FR-4 (п. 1, 3–5), NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/Integration/ScenarioStarter.cs`
  - **Acceptance**: обработчик `ModNewGame.BeforeStart` фиксирует `PendingRun` и пишет в `PlayerInitialData` год, тип, сложность, `StartingTechnology`, деньги, линии, читы до `DateController.SimulateUntilYear`; патчей на `GameManager.StartNewGame`, `Player.InitializeAsNewGame`, `MenuGameSetup.*` нет; при исключении поля восстанавливаются из копии, игра стартует ванильно и показывает уведомление; при `techYear > 0` вызывается `ResearchSector.UnlockTechnologies(techYear, 0)`; после старта деньги и линии сверяются со сценарием (расхождение → `ModLog`)
  - **Depends**: TSK-002, TSK-019, mod-framework/TSK-041

- [ ] **TSK-021**: `ScenarioSetupSection` на панели «Моды…»
  - **Requirement**: FR-3 (п. 1–3, 5–7), FR-2 (п. 1, 3)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/ScenarioSetupSection.cs`
  - **Acceptance**: `ModNewGame.RegisterSection` (порядок 10) показывает «Режим», «Испытания (N)…», «Достижения», описание и блок предпросмотра; выбор сценария выставляет и блокирует `companyTypeDropdown`/`difficultyDropdown`/`startDateDropdown` и переключатель читов (чтением приватных полей `MenuGameSetup` через `AccessTools`; не найдены → блокировка пропускается), вызывает `OnboardingBridge.ApplyScenarioMode`; `ModNewGame.PreviewChanged` обновляет только тексты секции по `ModNewGame.Preview`; «Свободная игра» возвращает прежние значения игрока и режим обучения `Allowed`; описание показывает лучший результат из профиля
  - **Depends**: TSK-003, TSK-005, TSK-015, TSK-020, mod-framework/TSK-036, mod-framework/TSK-041, mod-framework/TSK-063; first-year-onboarding/TSK-014 (мягкая)

- [ ] **TSK-022**: `ChallengesWindow` в меню
  - **Requirement**: FR-3 (п. 4), FR-11 (п. 3)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/ChallengesWindow.cs`
  - **Acceptance**: окно в сцене 0 (шаблон `Window.MenuPanel`) со списком испытаний (`ModUI.Toggle`), бонусами и причинами недоступности (в том числе «Жёсткий кредит» при `ModCredit.IsPerCompanyActive == false`); итоговый бонус «+X%» (аддитивно); выбор попадает в `PendingRun` и на кнопку «Испытания (N)…»
  - **Depends**: TSK-004, TSK-021, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-058

- [ ] **TSK-023**: Брифинг и письмо-итог
  - **Requirement**: FR-4 (п. 2), FR-9 (п. 5)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/BriefingWindow.cs`, `mod/src/PTMod/Features/ScenariosAchievements/Integration/ScenarioMailer.cs`
  - **Acceptance**: на старте сценария — письмо `ModMail.Send` (`Critical`, тег `scn:briefing:<runId>`, ID сохранён в секции) и модальное окно брифинга с паузой; по итогам — письмо (`Normal`, тег `scn:result:<runId>`) с исходом, счётом, медалью и целями; блоков ID писем нет; числа через `StringFormatter.FloatToMoney` / `FloatToPercentageString` / `FloatToFrequency`
  - **Depends**: TSK-005, TSK-019, mod-framework/TSK-037, mod-framework/TSK-038, mod-framework/TSK-062, mod-framework/TSK-035

- [ ] **TSK-024**: `GoalTrackerPanel`
  - **Requirement**: FR-10, FR-7 (п. 4)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/GoalTrackerPanel.cs`
  - **Acceptance**: немодальная панель с целями, прогрессом, гонками (лучший CPU игрока и соперников), дедлайном, значками assisted/custom; обновление раз в игровой день; закрытие и открытие из страницы хаба «Моды»; `trackerAutoOpen` учитывается при загрузке
  - **Depends**: TSK-019, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-039

- [ ] **TSK-025**: `ScorecardWindow` и таблица сравнения
  - **Requirement**: FR-9 (п. 1–4, 6), FR-8 (п. 4)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/ScorecardWindow.cs`
  - **Acceptance**: модальное окно с паузой: исход, причина, цели ✓/✗ с датами, разбивка счёта, медаль, флаги, достижения прогона, таблица (игрок + до 6 ИИ) за ≤ 50 мс; «Продолжить игру» снимает паузу и оставляет игру свободной; «В главное меню» показывает `ModUI.Confirm` и при «Да» вызывает `GameManager.QuitToMenu` без автосейва; если результат определён при `ModTicks.IsBatchRun`, окно открывается после `BatchEnded`; результат записан в профиль (`ProfileModel.RecordResult`, `ModProfile.Save()`)
  - **Depends**: TSK-010, TSK-016, TSK-019, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-064, mod-framework/TSK-057

- [ ] **TSK-026**: `AchievementsWindow`: достижения, рекорды, сброс профиля
  - **Requirement**: FR-14, FR-13 (п. 5)
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/AchievementsWindow.cs`
  - **Acceptance**: открывается из секции окна новой игры и из хаба «Моды»; «Достижения N / 37» по категориям, «???» для скрытых, пометка «с помощью»; вкладка «Рекорды» (лучший счёт, медаль, попытки, победы, испытания); «Сбросить профиль» с двойным `ModUI.Confirm`, очищает секцию через `ProfileModel.Clear` и `ModProfile.Save()`; в игре — пауза
  - **Depends**: TSK-005, TSK-015, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-064, mod-framework/TSK-039

- [ ] **TSK-027**: Очередь уведомлений о достижениях и целях
  - **Requirement**: FR-12 (п. 3–4), FR-5 (п. 2), NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/AchievementToastQueue.cs`
  - **Acceptance**: уведомления через `ModNotify`; не больше 3 за проверку плюс сводное «и ещё N»; ядро откладывает показ при загрузке, перемотке и `IsBatchRun`; `toastsEnabled = false` выключает всплывающие (запись в профиль остаётся)
  - **Depends**: TSK-012, mod-framework/TSK-033, mod-framework/TSK-057

- [ ] **TSK-028**: «Итоги карьеры» в 2030 году
  - **Requirement**: FR-17
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/UI/CareerSummaryWindow.cs`
  - **Acceptance**: на `TickPhase.YearPassed` при `Year == GameDefines.EndYear` без активного сценария открывается окно с таблицей сравнения, достижениями прогона и испытаниями; `EndGameWindow` игры открывается как обычно; событие `endyear.reached`
  - **Depends**: TSK-025, mod-framework/TSK-024

## Фаза 4 — Проверка

- [ ] **TSK-029**: Самопроверка на старте
  - **Requirement**: NFR-2, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/ScenariosAchievements/ScenariosSelfCheck.cs` (вклад в `ModSelfCheck`)
  - **Acceptance**: в `Player.log` строка `[PTMod] [scenarios-achievements] self-check: OK` или список проблем: цели двух патчей (`ContractManager.PromoteContractToActive`, `GameplaySettingsWindow.ChangeDifficulty`), поля `MenuGameSetup` (`companyTypeDropdown`, `difficultyDropdown`, `startDateDropdown`, `enableCheatsToggle`), поле `GameplaySettingsWindow.difficultySettings`, метод `GameManager.QuitToMenu`, валидность контента, ключи `scn.*`/`chl.*`/`ach.*` в `ru`, объявленность каналов модификаторов в реестре ядра
  - **Depends**: TSK-017, TSK-018, TSK-020, TSK-021, mod-framework/TSK-042

- [ ] **TSK-030**: Замер производительности
  - **Requirement**: NFR-1
  - **Deliverables**: счётчики времени в `RunDirector.cs` за флагом отладки (`ModSettings.Debug`), результаты в `mod/tests/PTMod.Tests/ScenariosAchievements/PERF.md`
  - **Acceptance**: на сейве 2010+ года (≥ 300 CPU) среднее время дневной обработки ≤ 0,5 мс, месячной ≤ 2 мс, таблица сравнения ≤ 50 мс; при перемотке новой игры 2000 года — 0 вызовов
  - **Depends**: TSK-019, TSK-025, mod-framework/TSK-024

- [ ] **TSK-031**: Плейтест баланса 8 сценариев (владелец)
  - **Requirement**: FR-2, NFR-8
  - **Deliverables**: правки чисел в `mod/src/PTMod/Content/scenarios-achievements/scenarios.json`, журнал плейтеста `mod/tests/PTMod.Tests/ScenariosAchievements/BALANCE.md` (сценарий, исход, дата, счёт, что поменяли)
  - **Acceptance**: каждый сценарий пройден владельцем хотя бы на бронзу на скорости 4–12; для `comeback-1985`, `power-wall-2000`, `foundry-1990` проверены рискованные пороги (долг −$20 млн, 65 Вт, 3 клиента) и записано решение; пороги медалей пересчитаны по правилу design.md после изменения целей
  - **Depends**: TSK-021, TSK-025, TSK-024

- [ ] **TSK-032**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-17, NFR-3
  - **Deliverables**: отметки прохождения сценариев S1–S17 из design.md в разделе Progress этого файла, фрагменты `Player.log` при ошибках
  - **Acceptance**: S1–S17 пройдены с ожидаемым результатом; 0 исключений `[scenarios-achievements]` в `Player.log`; 10 циклов «сохранить → загрузить» без расхождения статусов целей; совместимость сейвов (выключение фичи, `install.py --restore`, возврат мода) подтверждена
  - **Depends**: TSK-022, TSK-023, TSK-026, TSK-027, TSK-028, TSK-029, TSK-030, TSK-031

- [ ] **TSK-033**: Документация для авторов контента
  - **Requirement**: FR-1, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/scenarios-achievements/README.md`
  - **Acceptance**: описаны схема сценария, виды целей и проверок, метрики, события, каналы испытаний, правило порогов медалей, формат `records[]` и переопределение по `id` (`<папка игры>/PTMod/content/scenarios-achievements/`), флаг `custom`; пример из README проходит `ContentValidator`
  - **Depends**: TSK-003, TSK-004

## Dependency graph

```
mod-framework (TSK-022 FeatureContext, TSK-023 ModHooks, TSK-024 ModTicks, TSK-057 IsBatchRun, TSK-025 ModSave,
               TSK-021/014 ModProfile, TSK-015 ModContent, TSK-019/032 ModConditions, TSK-017/027/059 ModModifiers,
               TSK-056 ModEvents, TSK-041/063 ModNewGame, TSK-058 ModCredit, TSK-037/038/062 ModMail, TSK-033 ModNotify,
               TSK-035/036/064/039 ModUI, TSK-042 ModSelfCheck, TSK-067 мини-раннер)
first-year-onboarding/TSK-014 (OnboardingService) ── необязательно ──> TSK-019, TSK-021
rival-acquisitions/TSK-017 (RivalAcquisitionsApi) ── необязательно ──> TSK-017

TSK-001 ──┬──> TSK-013 ──> TSK-018
          └──> TSK-015
TSK-002 ──┬──> TSK-003 ──┐
          ├──> TSK-004 ──┼──> TSK-005
          │              └──> TSK-033
          └──> TSK-006 ──┬──> TSK-007 ──┬──> TSK-008 ──┐
                         │              │              ▼
                         │              └──────────> TSK-009 ──> TSK-010
                         ├──> TSK-011
                         ├──> TSK-012      (+ TSK-007)
                         ├──> TSK-014      (+ TSK-009)
                         └──> TSK-016
TSK-009, TSK-011, TSK-012, TSK-014, TSK-015, TSK-016 ──> TSK-019 ──> TSK-020
TSK-008, TSK-012 ──> TSK-017
TSK-003, TSK-005, TSK-015, TSK-020 ──> TSK-021 ──> TSK-022 (TSK-004)
TSK-019 ──> TSK-023, TSK-024
TSK-010, TSK-016, TSK-019 ──> TSK-025 ──> TSK-028
TSK-005, TSK-015 ──> TSK-026
TSK-012 ──> TSK-027
TSK-017, TSK-018, TSK-020, TSK-021 ──> TSK-029
TSK-019, TSK-025 ──> TSK-030
TSK-021, TSK-024, TSK-025 ──> TSK-031
TSK-022, TSK-023, TSK-026, TSK-027, TSK-028, TSK-029, TSK-030, TSK-031 ──> TSK-032
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи (`IModFeature`) и параметры | Pending |
| TSK-002 Модель контента и валидатор | Pending |
| TSK-003 Контент: 8 сценариев | Pending |
| TSK-004 Контент: испытания, достижения, параметры | Pending |
| TSK-005 Тексты ru/en | Pending |
| TSK-006 Метрики | Pending |
| TSK-007 `GoalEvaluator` | Pending |
| TSK-008 `RaceResolver` | Pending |
| TSK-009 `FailEvaluator` и `ScenarioRuntime` | Pending |
| TSK-010 `ScoreCalculator` и медали | Pending |
| TSK-011 `ChallengeRuntime` | Pending |
| TSK-012 `AchievementEvaluator` | Pending |
| TSK-013 `AssistedRegistry` | Pending |
| TSK-014 `RunState`, кодек и участник сейва | Pending |
| TSK-015 `ProfileModel` над `ModProfile` | Pending |
| TSK-016 `GameMetricsSource` | Pending |
| TSK-017 События игры (`ModEvents`, OEM, банкротства) | Pending |
| TSK-018 Запрет понижения сложности, причины assisted | Pending |
| TSK-019 `RunDirector` | Pending |
| TSK-020 `ScenarioStarter` (`BeforeStart`) | Pending |
| TSK-021 Секция в панели «Моды…» | Pending |
| TSK-022 Окно испытаний | Pending |
| TSK-023 Брифинг и письмо-итог | Pending |
| TSK-024 Трекер целей | Pending |
| TSK-025 Экран итогов | Pending |
| TSK-026 Окно достижений и рекордов | Pending |
| TSK-027 Очередь уведомлений | Pending |
| TSK-028 Итоги карьеры 2030 | Pending |
| TSK-029 Самопроверка | Pending |
| TSK-030 Замер производительности | Pending |
| TSK-031 Плейтест баланса (владелец) | Pending |
| TSK-032 Ручная проверка в игре (владелец) | Pending |
| TSK-033 Документация контента | Pending |

## Трассировка FR → TSK

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-002, TSK-014, TSK-019, TSK-033 |
| FR-2 | TSK-003, TSK-005, TSK-021, TSK-031 |
| FR-3 | TSK-021, TSK-022 |
| FR-4 | TSK-019, TSK-020, TSK-023 |
| FR-5 | TSK-006, TSK-007, TSK-009, TSK-016, TSK-017, TSK-027 |
| FR-6 | TSK-008, TSK-017 |
| FR-7 | TSK-009, TSK-017, TSK-024 |
| FR-8 | TSK-010, TSK-025 |
| FR-9 | TSK-016, TSK-023, TSK-025 |
| FR-10 | TSK-001, TSK-024 |
| FR-11 | TSK-002, TSK-004, TSK-011, TSK-022 |
| FR-12 | TSK-002, TSK-004, TSK-012, TSK-017, TSK-027 |
| FR-13 | TSK-015, TSK-026 |
| FR-14 | TSK-026 |
| FR-15 | TSK-001, TSK-013, TSK-018 |
| FR-16 | TSK-006, TSK-014, TSK-019 |
| FR-17 | TSK-028 |
| NFR-1 | TSK-016, TSK-030 |
| NFR-2 | TSK-001, TSK-019, TSK-020, TSK-029 |
| NFR-3 | TSK-014, TSK-032 |
| NFR-4 | TSK-015 |
| NFR-5 | TSK-005, TSK-029 |
| NFR-6 | TSK-005, TSK-027 |
| NFR-7 | TSK-002, TSK-033 |
| NFR-8 | TSK-003, TSK-004, TSK-031 |
