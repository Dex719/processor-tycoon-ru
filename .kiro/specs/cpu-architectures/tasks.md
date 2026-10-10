# Tasks: cpu-architectures

## Document Information

- **Feature Name**: cpu-architectures
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/cpu-architectures/requirements.md`
  - Design: `.kiro/specs/cpu-architectures/design.md`
  - API вкладок и занятости лаборатории: `.kiro/specs/research-tree-ux/` (TSK-014)
  - Ядро: `.kiro/specs/mod-framework/design.md`, `.kiro/specs/mod-framework/tasks.md` (номера `mod-framework/TSK-0xx` ниже)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

XL-фича делится на два этапа с гейтом между ними.

- **Этап 1 — MVP** (фазы 1–3, TSK-001…TSK-020 и TSK-034): каталог, исследование в общей лаборатории, вкладка, конструктор, привязка CPU, рейтинг по сродству и устареванию, базовый ИИ, посев и миграция, выключение. Заканчивается ручной проверкой владельцем (TSK-020) — гейт перед этапом 2.
- **Этап 2 — полная версия** (фазы 4–6, TSK-021…TSK-033 и TSK-035): поддержка ПО, экосистема, опыт ISA, ворота расширений, адаптивный ИИ, роялти x86, обзор, балансовая симуляция, финальная проверка.

Номера TSK-034 и TSK-035 добавлены в v0.2 (общая лаборатория и роялти x86); номера TSK-001…033 не меняются, потому что на них ссылаются другие спеки, а новые задачи вставлены в график по зависимостям.

### Implementation Strategy

- Чистая логика (`*Math`, `*Policy`, `LabRules`, `CpuFingerprint`, валидатор) лежит в `Features/CpuArchitectures/Logic/**` — первой и с тестами; она не ссылается на `UnityEngine` и типы игры (сегмент — `Segment` ядра), код — общее подмножество C# и API .NET Framework 4.8. Сервисы и патчи — тонкие адаптеры поверх неё.
- Схема `ModSave` с полями этапа 2 закладывается сразу (TSK-003), чтобы сейвы этапа 1 открывались на этапе 2 без миграции; версию секции (`_schema`) ведёт ядро, DTO фичи поля версии не содержит.
- Этап переключается полем `stage` в `balance.json`: формулы этапа 2 есть в коде, но до TSK-020 выключены.
- Фича подключается к ядру только через сервисы `PTMod.Core` (`FeatureContext.OnTick`, `ModTicks.GameReady`, `ModEvents`, `ModSegments`, провайдеры `ModModifiers`, `IModSaveParticipant`); патчей на `Company.AddCpu`, `AICompany.AddCpu`, `CpuMath.ProjectCost`, `Market.CalculateRatings` нет.
- Каждая задача 2–4 часа; после каждой — сборка (`python mod/build.py --game-dir <папка игры>`) и тесты (`python mod/build.py --tests`) зелёные.

### Development Approach

- **Testing Strategy**: TDD для чистой логики на мини-раннере .NET Framework 4.8 (`[Test]`, `[Covers]`, `Assert.*`); балансовая симуляция (TSK-029); ручные сценарии M1–M15 (`design.md`) выполняет владелец.
- **Integration Strategy**: части фичи (`ArchParts`) подключаются независимо; самопроверка `ModHooks` выключает часть при сбое.
- **Deployment Strategy**: `install.py --with-mods` (`mod-framework`); этап 2 — новая версия мода с `stage: 2`.

## Фаза 1 — Каркас (этап 1, MVP)

- [ ] **TSK-001**: Каркас фичи cpu-architectures
  - **Requirement**: FR-9, NFR-2, NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/ArchitecturesFeature.cs` (`[ModFeature] sealed class … : IModFeature`: `Id` = `cpu-architectures`, `NameKey`, `DescriptionKey`, `DependsOn` = пусто, `EnabledByDefault` = true, `SupportsHotToggle` = true, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `ArchParts.cs`, `ArchStage.cs`; параметры `ratingStrength` (Float 0–1,5, шаг 0,05, по умолчанию 1,0) и `notifications` (Bool) через `ctx.Param`; страница хаба `ctx.HubPage` (заглушка); заготовки ключей `arch.*` в `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json`
  - **Acceptance**: сборка проходит; ядро находит фичу по `[ModFeature]`, она видна в хабе «Моды» с переключателем и слайдером силы; самопроверка пишет в лог состояние всех целей патчей и приватных полей из `design.md` (пока без самих патчей); `IsNeutralNow` = true при перемотке и выключенной фиче; в коде фичи нет прямых подписок на `DateController`, `Player`, `SaveHandler` и собственных счётчиков ошибок
  - **Depends**: mod-framework/TSK-021 (`ModSettings`, `ModL10n`, `ModContent`), mod-framework/TSK-022 (`IModFeature`, `FeatureContext`), mod-framework/TSK-023 (`ModHooks`), mod-framework/TSK-043 (`core.json`: префикс `arch`)

- [ ] **TSK-002**: Контент и валидатор каталога
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Content/cpu-architectures/architectures.json`, `balance.json` (включая `ai.noInterruptTimeLeftDays` и `x86License`), `ai-preferences.json` (по `uniqueId`) — данные из `design.md`; `mod/src/PTMod/Features/CpuArchitectures/Logic/ArchFamily.cs`, `ArchCatalogValidator.cs`, `BalanceConfig.cs`, `AiPreference.cs`; `Data/ArchCatalog.cs` (загрузка через `ModContent`); `mod/tests/PTMod.Tests/CpuArchitectures/ArchCatalogValidatorTests.cs`, `ContentDefaultsTests.cs`
  - **Acceptance**: тесты: повтор `id`, неизвестная зависимость, цикл, диапазоны, отсутствие `proprietary` (фича выключается), ровно одна запись `default` и уникальные `uniqueId` в предпочтениях; встроенные JSON валидны; в логе «7 семейств загружено»; пользовательский файл из `<папка игры>/PTMod/content/cpu-architectures/` переопределяет встроенный; все публичные типы в `[Covers]`
  - **Depends**: TSK-001, mod-framework/TSK-015 (логика `ModContent`), mod-framework/TSK-021, mod-framework/TSK-067 (мини-раннер)

- [ ] **TSK-003**: Реестр состояния и секция ModSave
  - **Requirement**: FR-5, FR-8, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/State/ArchRegistry.cs`, `CompanyArchState.cs`, `FamilyState.cs`, `ArchSaveData.cs` (все поля этапов 1 и 2, без поля версии), `ArchSaveParticipant.cs` (`IModSaveParticipant`, `SchemaVersion` = 1); `mod/tests/PTMod.Tests/CpuArchitectures/SaveDataTests.cs`
  - **Acceptance**: round-trip секции без потерь (`ModJson.From` / `Bind`); отсутствующая секция → `ResetToDefaults` и признак `MigrationNeeded`; значения вне диапазонов и лимиты списков отбрасываются с предупреждением; неизвестные `arch` сохраняются как есть; DTO не содержит поля версии, секция новее мода обрабатывается ядром (значения по умолчанию, исходная секция записывается как есть — проверка в `mod-framework/TSK-016`)
  - **Depends**: TSK-002, mod-framework/TSK-016 (модель бокового файла), mod-framework/TSK-025 (`ModSave`)

## Фаза 2 — Ядро MVP

- [ ] **TSK-004**: Математика исследований архитектур и правила общей лаборатории
  - **Requirement**: FR-2, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/ArchResearchMath.cs`, `Logic/LabRules.cs` (`Status`, `NeedsConfirmation`, `CanInterruptForAi`, `ResolveConflict`, `AiFunding`, `PlayerFundingLimit`); `mod/tests/PTMod.Tests/CpuArchitectures/ArchResearchMathTests.cs`, `LabRulesTests.cs`
  - **Acceptance**: тесты: годовой множитель (будущее, прошлое, текущий, предел 0,75) совпадает с формулой `TechnologyTimeUpdater.UpdateTechnologyTimes`; x86 в 1980 году → 194 дня при типе компании IDM; `baseDays` = 0 → мгновенно; стоимость с M4 = 6; ETA при скорости 0 → «∞»; доступность по зависимостям; правила лаборатории: подтверждение только игроку при технологии, ИИ не прерывает технологию с остатком ≤ 30 дней, конфликт (игрок → пауза архитектуры, ИИ → остановка технологии), финансирование ИИ 0,6–1,6, предел слайдера 1,0/5,0
  - **Depends**: TSK-002

- [ ] **TSK-005**: Сервис ежедневного исследования
  - **Requirement**: FR-2, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/ArchResearchService.cs` (`ctx.OnTick(TickPhase.Tick)`, `Start`/`Stop`/`Resume`/`SetFunding`, прогресс и стоимость по `ResearchSector.Funding` и `ResearchMath.ResearchSpeed`, пауза при `IsInDefault` для игрока и при отрицательных деньгах у ИИ, событие `OnFamilyResearched`, `ModNotify` игроку)
  - **Acceptance**: в отладочной сессии: прогресс растёт по формуле FR-2 п. 2; списание идёт в статью `ExpenseOrigin.Research`; прогресс сохраняется при переключении семейства (AC-2.2) и через сохранение/загрузку; при перемотке тиков нет; дневной тик ≤ 0,1 мс. Семантику общей лаборатории (остановка технологии, возврат, конфликты) добавляет TSK-034
  - **Depends**: TSK-003, TSK-004, mod-framework/TSK-024 (`ModTicks`), mod-framework/TSK-033 (`ModNotify`)

- [ ] **TSK-006**: Математика рейтинга
  - **Requirement**: FR-6, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/ArchRatingMath.cs`, `Logic/RatingConfig.cs`; `mod/tests/PTMod.Tests/CpuArchitectures/ArchRatingMathTests.cs`, `RatingStackingTests.cs`
  - **Acceptance**: тесты: `fade` в 1984/1990/1996 (1,0 / 0,95 / 0,90), только сегменты `Segment.HighEnd`…`LowEnd`; `f_sup`, `f_eco`, `f_ext` (функции готовы для этапа 2); клэмп [0,80; 1,20] на сетке параметров; `ApplyStrength` при 0 и 1,5; примеры из таблицы «Формулы и баланс» (1,135 / 1,102 / 0,857 / 0,875) с точностью 0,001; вклад 1,30 в произведении с вкладами других фич упирается в балансовый диапазон ядра [0,5; 2,0]
  - **Depends**: TSK-002, mod-framework/TSK-059 (`BalanceRange`)

- [ ] **TSK-007**: Провайдер стандартного канала `market.rating.cpu`
  - **Requirement**: FR-6, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/ArchRatingProvider.cs` (`ModModifiers.RegisterReasonedProvider` на `ModChannels.MarketRatingCpu`, один провайдер на все сегменты по `ctx.Segment`, причина `arch.reason.rating`, `Invalidate` по месяцу/событиям/смене ручки, 1,0 при `IsNeutralNow`, `Segment.Any` и пустом CPU)
  - **Acceptance**: в отладочной сессии 1996 года CPU на `proprietary` в «Высоком сегменте» получает 0,90, на x86 — 1,05 (AC-6.1), ARM в «Промышленности» — 1,10 (AC-6.2); игрок и ИИ получают одинаковые множители; `ModModifiers.Explain` показывает вклад архитектуры с причиной; провайдер зарегистрирован один раз и не пересоздаётся при `OnEnabled`/`OnDisabled`; суммарное время провайдера ≤ 0,2 мс/день; новых каналов не объявлено
  - **Depends**: TSK-006, TSK-003, mod-framework/TSK-027 (`ModModifiers`), mod-framework/TSK-029 (P4), mod-framework/TSK-059 (причины провайдеров, `Invalidate`)

- [ ] **TSK-008**: Отпечатки проектов и привязка CPU
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/CpuFingerprint.cs`, `Services/CpuArchBinder.cs`, `Patches/BinderPatches.cs` (postfix `Project.SetProjectType(SaveObject.CpuProject)`, postfix `ProjectCompletedHandler.AddCpu`), подписки на `ModEvents.CpuReleased` (ИИ) и `ModEvents.CpuTransferred`; `ArchRegistry.ResolveCpu` с ленивым разрешением (через заглушку `DefaultFor` до TSK-014); `mod/tests/PTMod.Tests/CpuArchitectures/CpuFingerprintTests.cs`
  - **Acceptance**: тесты: совпадение после JSON round-trip (допуск 1e-4), FIFO для дублей; в игре сценарий AC-5.1 (сохранение посреди проекта → загрузка → выпуск → верная архитектура); при загрузке сейва привязки не меняются (`CpuReleased` ядро не поднимает); при перемотке события пропускаются; вариант CPU (`cpu.Base` ≠ null) берёт архитектуру базового; патчей на `Company.AddCpu`/`AICompany.AddCpu` нет
  - **Depends**: TSK-003, mod-framework/TSK-023, mod-framework/TSK-056 (`ModEvents`)

- [ ] **TSK-009**: Конструктор CPU: список «Архитектура»
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Designer/DesignerIntegration.cs`, `Designer/DesignContext.cs`, `Patches/DesignerPatches.cs` (postfix `CreatorCpu.Initialize`, postfix `CreatorCpu.UpdateUI`, prefix/postfix `CreatorCpu.CreateCpu`); ключи `arch.designer.*`
  - **Acceptance**: в окне конструктора рядом со списком ядер появляется список «Архитектура» с исследованными семействами игрока; по умолчанию — последнее выбранное или самое новое; выбор запоминается в `CompanyArchState.Active` игрока; `PendingForCreate` захватывается при «Create» и сбрасывается после; при выключенной фиче список скрыт
  - **Depends**: TSK-003, mod-framework/TSK-036 (виджеты `ModUI`)

- [ ] **TSK-010**: Множители дизайна для игрока и ИИ
  - **Requirement**: FR-4
  - **Deliverables**: в `Patches/DesignerPatches.cs`: postfix `CreatorCpu.ConsumptionAndTemperature`, postfix `CreatorCpu.Performance`; `Patches/AiDesignPatches.cs`: postfix `DataConverter.CpuDesignToCpu`; `Designer/DesignCostProvider.cs` (провайдер канала `development.cost.design` через `ModModifiers.RegisterReasonedProvider`, без postfix на `CpuMath.ProjectCost`); строка прогноза рейтинга в конструкторе (`InfoText`)
  - **Acceptance**: сценарий AC-4.1 (IPC 10 → 10,5; 20 Вт → 22 Вт; 50M → 57,5M через канал; температура пересчитана; брак считается от новой температуры); у CPU ИИ IPS на 5 % выше для x86 (AC-4.3), стоимость проектов ИИ идёт через тот же провайдер; при перемотке и выключенной фиче значения ванильные (провайдер возвращает 1,0); `Explain` канала показывает множитель; провайдер ≤ 5 мкс, постфиксы ≤ 0,05 мс/кадр; строка «Рейтинг по сегментам» совпадает с `ArchRatingMath`
  - **Depends**: TSK-009, TSK-006, mod-framework/TSK-060 (P19 `CpuMath.ProjectCost`, `DesignContext`), mod-framework/TSK-059

- [ ] **TSK-011**: Инспектор, строки сравнения и модификаторов
  - **Requirement**: FR-5, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/UI/InspectorIntegration.cs`, `Patches/InspectorPatches.cs` (postfix `CpuUI.SetCpu`), `UI/ArchCompareRows.cs`, `UI/ArchModifierLines.cs` (регистрация через `ResearchTreeUxApi`)
  - **Acceptance**: инспектор показывает название архитектуры вместо «Default» для CPU игрока и ИИ; «Неизвестная (id)» для удалённого семейства; в окне сравнения `research-tree-ux` есть строки «Архитектура» и «Множитель рейтинга»; в панели «Все модификаторы» — группа «Архитектуры» (текущее исследование, стоимость в месяц); без `research-tree-ux` — без ошибок
  - **Depends**: TSK-008, TSK-007, research-tree-ux/TSK-014

## Фаза 3 — UI, ИИ, посев (этап 1, MVP)

- [ ] **TSK-012**: Панель архитектур: карточки семейств
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/UI/ArchitecturesPanel.cs`, `UI/FamilyCard.cs`; ключи `arch.family.*`, `arch.card.*`, `arch.status.*`, `arch.effects`, `arch.segments`
  - **Acceptance**: 7 карточек: название, ISA, год, статус с причиной блокировки или паузы, время и стоимость, множители, сродство с 4 сегментами, кнопка «Исследовать» (неактивна у исследованных и заблокированных, «Продолжить» у приостановленного); обновление раз в день (`TickPhase.DayPassed`), если панель видна; тема игры применяется; подсказки через `ModUI.Tooltip`
  - **Depends**: TSK-005, mod-framework/TSK-036, mod-framework/TSK-064 (`ModUI.Tooltip`)

- [ ] **TSK-013**: Блок лаборатории, вкладка и окно-фолбэк
  - **Requirement**: FR-3, FR-2
  - **Deliverables**: `UI/LabStatusBlock.cs` (строка состояния лаборатории, `ProgressBar`, ETA, стоимость, `SliderUI` финансирования, кнопки «Остановить» и «Продолжить»), `UI/ArchitecturesTab.cs` (`IResearchTabProvider`, `Order` 200, `BadgeCount`), `UI/ArchitecturesWindow.cs` (страница хаба через `ctx.HubPage`)
  - **Acceptance**: AC-3.1 (вкладка последней в полосе дерева, 7 карточек) и AC-3.2 (при выключенной research-tree-ux — окно из хаба с тем же содержимым; при включении research-tree-ux вкладка появляется без перезагрузки); слайдер пишет `ResearchSector.Funding` (1–100 %, до 500 % при инновациях) и сразу меняет ETA и стоимость; счётчик на вкладке при появлении нового доступного семейства, снимается после просмотра
  - **Depends**: TSK-012, TSK-034, research-tree-ux/TSK-014, mod-framework/TSK-039 (хаб, `HubPage`), mod-framework/TSK-064 (`ModUI.Confirm`)

- [ ] **TSK-014**: Политика посева
  - **Requirement**: FR-8, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/ArchSeedPolicy.cs` (`SeedAi`, `SeedPlayer`, `DefaultForCpu`); замена заглушки в `ArchRegistry.ResolveCpu`; `mod/tests/PTMod.Tests/CpuArchitectures/ArchSeedPolicyTests.cs`
  - **Acceptance**: тесты: игрок с U = 1993 → `proprietary` + x86 (AC-8.1); 2008: Intel (`UniqueID` 0) → активна `x86_64`, Texas Instruments (3) → `arm` (AC-8.2); замыкание зависимостей; запись `default`; CPU игрока 2001 → x86; начальная поддержка по `seed`
  - **Depends**: TSK-002

- [ ] **TSK-015**: Посев новой игры, миграция старых сейвов, поздние ИИ
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/ArchSeeder.cs` (`ctx.OnTick(TickPhase.FastForwardEnded)` — посев всех существующих ИИ один раз; `ModTicks.GameReady`: `NewGame` — посев игрока, `Loaded` — миграция при `MigrationNeeded`; ленивый посев ИИ в `ArchRegistry.GetOrSeed`; отложенный посев игрока, если `ResearchSector` ещё не готов)
  - **Acceptance**: сценарии M7 (старт 1995 → x86 у игрока, соперники засеяны) и миграция старого сейва без ошибок (M8, первая часть); ИИ засеяны после перемотки, а не во время неё; заспавненная позже ИИ получает состояние в первом месячном решении; `seeded` = true пишется в `ModSave`
  - **Depends**: TSK-014, TSK-003, TSK-008, mod-framework/TSK-024

- [ ] **TSK-016**: Политика ИИ (MVP)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/ArchAiPolicy.cs` (`Match` по `UniqueId` и `default`, `DecideMvp`), `Logic/AiView.cs`; `mod/tests/PTMod.Tests/CpuArchitectures/ArchAiPolicyTests.cs` (MVP)
  - **Acceptance**: тесты: выбор по весу с задержкой, ничьи (год, `id`), резерв 6 месяцев × стоимость × M4, отложенный старт при остатке технологии ≤ 30 дней (`DeferInterrupt`), пауза при отрицательных деньгах, смена активной при завершении (AC-7.1, AC-7.2, AC-7.3); foundry пропускается; решения детерминированы (повторный прогон — тот же результат)
  - **Depends**: TSK-002, TSK-004

- [ ] **TSK-017**: Сервис ИИ: месячный цикл
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/ArchitectureAi.cs` (`ctx.OnTick(TickPhase.MonthPassed)`; `ModCompanies.All` → `AICompany`; фильтр `IsFoundry`/`IsBankrupt`; сопоставление предпочтений по `UniqueID`; применение `AiDecision` через `LabArbiter.Occupy`; событие `OnActiveChanged`; финансирование по `Player.DifficultyLevel` в `ResearchSector.Funding`)
  - **Acceptance**: в отладочной сессии 1979–1990: Intel начинает x86 в 1979 и переходит на неё (~8 мес.), прерывая технологию, если ей осталось больше 30 дней; Texas Instruments переходит на ARM после 1989; пока архитектура занимает лабораторию ИИ, он не выбирает технологию, после завершения игра выбирает сама; месячный цикл ≤ 3 мс; при перемотке не вызывается
  - **Depends**: TSK-016, TSK-005, TSK-015, TSK-034, mod-framework/TSK-026 (`ModCompanies`)

- [ ] **TSK-018**: Выключение посреди игры и ручка силы
  - **Requirement**: FR-9
  - **Deliverables**: `ArchitecturesFeature.OnEnabled/OnDisabled` (пауза исследования игрока, возврат лаборатории, `ModModifiers.Invalidate`); ранние выходы во всех постфиксах; `ApplyStrength` в `ArchRatingProvider`; слайдер силы и переключатель уведомлений (`ParamSpec`, страница хаба); ключ `arch.param.ratingStrength`
  - **Acceptance**: AC-9.1 (3 месяца без списаний, прогресс и уровни на месте) и AC-9.2 (лаборатория вернулась к прерванной технологии); при выключении инспектор показывает «Default», список в конструкторе и вкладка скрыты, провайдеры дают 1; сила 0 % → все множители рейтинга = 1; CPU, созданные при выключенной фиче, после включения получают архитектуру по умолчанию; провайдеры и записи не пересоздаются
  - **Depends**: TSK-007, TSK-010, TSK-011, TSK-013, TSK-017, TSK-034

- [ ] **TSK-019**: Локализация MVP и тест ключей
  - **Requirement**: NFR-5
  - **Deliverables**: все ключи `arch.*` этапа 1 в `mod/src/PTMod/L10n/ru.json` и `mod/src/PTMod/L10n/en.json` (включая `arch.lab.*`, `arch.reason.*`); `mod/tests/PTMod.Tests/CpuArchitectures/L10nKeysTests.cs`
  - **Acceptance**: тест находит каждый ключ из кода и контента (`nameKey`, `descKey`) в обоих файлах; все ключи с префиксом `arch.`; в игре на `en` нет «сырых» ключей
  - **Depends**: TSK-012, TSK-013, TSK-018, mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-020**: Ручная проверка MVP в игре (владелец) — гейт этапа 2
  - **Requirement**: FR-1 … FR-9, NFR-1 … NFR-7
  - **Deliverables**: отметки о прохождении сценариев M1–M8 из `design.md`; остаточные вопросы 1–2 из `requirements.md` решены или подтверждены
  - **Acceptance**: M1–M8 пройдены; в `Player.log` за сессию ≥ 2 часов нет исключений `[PTMod]` от фичи; владелец явно разрешил этап 2
  - **Depends**: TSK-019

- [ ] **TSK-034**: Общая лаборатория: арбитр слота, возврат технологии, пропуск выбора ИИ
  - **Requirement**: FR-2, FR-7, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/LabArbiter.cs` (`Occupy`, `Release`, `OnEarlyTick` на `TickPhase.EarlyTick`, `IsOccupiedByArchitecture`), `Services/LabUiSync.cs` (скрытие устаревшей панели `CurrentResearchUI` через приватные `targetTechnology` и `Hide`, необязательный `LoadFunding`), `Services/LabOccupancySource.cs` (`ILabOccupancySource` и `ResearchTreeUxApi.RegisterLabOccupancy`), `Patches/LabPatches.cs` (prefix `AIBehaviourResearch.ChooseANewResearch`, `Optional`); подтверждение прерывания технологии через `ModUI.Confirm`; доводка `ArchResearchService.Start/Stop/Resume`; ключи `arch.lab.*`
  - **Acceptance**: AC-2.3 и AC-2.4 в игре: запуск архитектуры при идущей технологии (подтверждение → технология остановлена с сохранённым прогрессом, деньги платятся только за архитектуру, после завершения лаборатория возвращается к технологии); выбор технологии при активной архитектуре → на следующий день пауза с уведомлением и кнопка «Продолжить»; ИИ не выбирает технологию, пока лаборатория занята архитектурой, и выбирает сама после неё; `AIBehaviourResearch.ChooseANewResearch` пропускается только при занятой лаборатории; без патча или без приватных членов отключаются только части `Ai` и `LabUi`; при выключении фичи лаборатория возвращается; `research-tree-ux` получает «Лаборатория занята» и не показывает напоминание о простое (AC-9.2 там)
  - **Depends**: TSK-005, mod-framework/TSK-023 (`ModHooks`), mod-framework/TSK-064 (`ModUI.Confirm`), research-tree-ux/TSK-014 (`ILabOccupancySource`, мягко)

## Фаза 4 — Программная поддержка (этап 2)

- [ ] **TSK-021**: Математика поддержки
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/SupportMath.cs`; `mod/tests/PTMod.Tests/CpuArchitectures/SupportMathTests.cs`
  - **Acceptance**: тесты: AC-10.1 (30 → ≈59 за 12 мес. на «обычном»), AC-10.2 (60 → ≈37 на «выкл» при 0,04), `targetCap` 60 для `proprietary`, `eraCost` ARM (1987: 1,3; 1995: ≈1,12; 2010+: 0,6), содержание с инфляцией, прогноз на N месяцев
  - **Depends**: TSK-020

- [ ] **TSK-022**: Сервис поддержки: месячный цикл и влияние на рейтинг
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Services/SupportService.cs` (обновление S, списание `ExpenseOrigin.Development`, дефолт игрока → «выкл» + `ModNotify`); единый месячный обработчик `ctx.OnTick(TickPhase.MonthPassed)` с порядком сервисов; включение `f_sup` в `ArchRatingProvider` при `stage` = 2 и `ModModifiers.Invalidate`
  - **Acceptance**: в отладочной сессии: списание раз в месяц в статье «Development»; при дефолте уровни игрока → «выкл», уведомление (M14); `f_sup` меняет множитель рейтинга по формуле; кеш канала сбрасывается после обновления
  - **Depends**: TSK-021, TSK-007

- [ ] **TSK-023**: UI поддержки
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/UI/SupportSection.cs` (на семейство: `ProgressBar` 0–100, `DropdownUI` уровня, стоимость в месяц, прогноз на 12 мес.); подсказки с формулой эффекта (NFR-6, `ModUI.Tooltip`); ключи `arch.support.*`
  - **Acceptance**: M9 (уровень «Высокая» → стоимость ×2, рост к 95, прогноз совпадает с фактом ±1); смена уровня сразу меняет показанную стоимость; применяется со следующего месяца
  - **Depends**: TSK-022, TSK-012

## Фаза 5 — Экосистема, опыт ISA, расширения, адаптивный ИИ, роялти (этап 2)

- [ ] **TSK-024**: Экосистема архитектур
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/EcosystemMath.cs`, `Services/EcosystemService.cs` (окно 12 полных месяцев: `CpuBase.MarketData.Sold[i]` + `CpuBase.MarketData.SoldByContracts[i]`, `i` до `Count − 2`); `f_eco` в рейтинге; доля и изменение за год в панели; `mod/tests/PTMod.Tests/CpuArchitectures/EcosystemMathTests.cs`
  - **Acceptance**: тесты: AC-11.1 (0,9 → 1,045; 0,1 → 0,982), пустые продажи → `proprietary` = 1; в игре доли в панели суммируются в 100 %; месячный цикл ≤ 3 мс при 300 CPU
  - **Depends**: TSK-022

- [ ] **TSK-025**: Опыт ISA и незрелые инструменты
  - **Requirement**: FR-12
  - **Deliverables**: `isa`-множитель в `ArchResearchMath`/`ArchResearchService`; начальная поддержка с наследованием в `ArchResearchService`; `DesignContext.ImmatureCostFactor` в провайдере `DesignCostProvider` (канал `development.cost.design`); строка `arch.designer.immature` в конструкторе; тесты в `ArchResearchMathTests.cs`
  - **Acceptance**: AC-12.1 (x86-64 при x86 = 80: время ×0,6, старт 40) и AC-12.2 (ARM через 3 месяца: стоимость × 0,85 × 1,1875); M11
  - **Depends**: TSK-022, TSK-010

- [ ] **TSK-026**: Ворота расширений
  - **Requirement**: FR-13
  - **Deliverables**: `f_ext` в `ArchRatingProvider` (расширения из `balance.json`); предупреждение в конструкторе (`arch.designer.extWarn`); `UI/ArchTechTooltipLines.cs` (`ITechTooltipLineSource` для технологий типа Multicore); тесты в `ArchRatingMathTests.cs`
  - **Acceptance**: AC-13.1 (4 ядра, S = 20 → `f_ext` = 0,96); M12; подсказка технологии многоядерности в дереве содержит строку о требуемой поддержке
  - **Depends**: TSK-022, research-tree-ux/TSK-014

- [ ] **TSK-027**: Адаптивная политика ИИ
  - **Requirement**: FR-14
  - **Deliverables**: `ArchAiPolicy.DecideFull` (привлекательность по долям сегментов за 3 месяца, порог 1,10 и S ≥ 35, уровни поддержки, вес × (1 + E)); сбор `AiView.SegmentShare` из `CpuBase.MarketData.SoldByHighEnd/SoldByMidRange/SoldByLowEnd/SoldByIndustries`, запасной источник — `ModSegments.Classify` по ценам CPU; `ArchitectureApi.SetAiWeightOverride`; тесты в `ArchAiPolicyTests.cs`
  - **Acceptance**: тесты: AC-14.1 (Motorola → RISC), без продаж — голоса `ModSegments.Classify`, без CPU — равные доли, переопределение веса через API; решения детерминированы; собственного классификатора сегмента в фиче нет
  - **Depends**: TSK-024, TSK-017, mod-framework/TSK-055 (`ModSegments`)

- [ ] **TSK-028**: Обзор архитектур и уведомления
  - **Requirement**: FR-15
  - **Deliverables**: `UI/RivalsSection.cs` (таблица: компания, активная, исследование); `Services/ArchNotifier.cs` (`ModNotify` о смене активной; `ModMail` о вехах 25/50/75 % с тегом `arch:eco:<familyId>:<порог>`, приоритетом `Normal` и записью в `milestones`); ключи `arch.notify.*`, `arch.mail.*`
  - **Acceptance**: M13 (уведомления о переходах, письма при 25/50 %, каждая веха один раз за игру, в том числе после загрузки); выключенные уведомления в хабе не показываются; в перемотку и пакетный прогон показ управляется ядром
  - **Depends**: TSK-027, mod-framework/TSK-033 (`ModNotify`), mod-framework/TSK-037 и TSK-062 (`ModMail`, `Tag`, приоритеты)

- [ ] **TSK-035**: Роялти за x86 без лицензии
  - **Requirement**: FR-16
  - **Deliverables**: `mod/src/PTMod/Features/CpuArchitectures/Logic/RoyaltyMath.cs`, `Services/RoyaltyService.cs` (вызывается из месячного обработчика; доход `CpuBase.MarketData.Income[Count − 2]` по CPU семейств ISA `x86`; `SpendMoney(…, ExpenseOrigin.Royalties)`; `EarnMoney(…, RevenueOrigin.Royalties)` живым держателям; пропуск при `LicenseHelper.X86LicenseHasBeenRevoked` и при перемотке); строка «Роялти x86» в панели; ключ `arch.royalty.line`; `mod/tests/PTMod.Tests/CpuArchitectures/RoyaltyMathTests.cs`
  - **Acceptance**: тесты: AC-16.1 (10M → 300k, по 150k держателям), нет живых держателей, доход 0, держатель освобождён (AC-16.3); в игре M15 и AC-16.2; правила одинаковы для игрока и ИИ; месячный цикл ≤ 3 мс
  - **Depends**: TSK-022, TSK-008, TSK-020

## Фаза 6 — Проверка

- [ ] **TSK-029**: Балансовая симуляция чистой логики
  - **Requirement**: NFR-4, FR-6, FR-14
  - **Deliverables**: `mod/tests/PTMod.Tests/CpuArchitectures/BalanceSimulationTests.cs` (помесячный прогон 1975–2025, 11 ИИ-проектировщиков, упрощённая модель продаж ∝ m)
  - **Acceptance**: метрики `requirements.md`: x86 активна у ≥ 3 из 5 «ПК-компаний» (Intel, AMD, Cyrix, VIA, IBM) к 1995; ARM/ARM64 — у ≥ 2 из 3 «встраиваемых» (Texas Instruments, Toshiba, Zilog) к 2015; вклад m ∈ [0,80; 1,20] всегда; до 1985 ни одно семейство не даёт > +20 % к `proprietary`; отчёт симуляции (таблица по годам) прикладывается к PR для владельца
  - **Depends**: TSK-027, TSK-025, TSK-026

- [ ] **TSK-030**: Самопроверка и замеры производительности
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**: замеры `ModLog` (`ModSettings.Debug`) для дневного тика и арбитра, месячного цикла, провайдеров рейтинга и стоимости проекта, постфиксов конструктора; проверка деградации частей (искусственно скрытое поле в debug-сборке)
  - **Acceptance**: дневной ≤ 0,1 мс, месячный ≤ 3 мс, рейтинг ≤ 0,2 мс/день, провайдер стоимости ≤ 5 мкс, конструктор ≤ 0,05 мс/кадр; при отсутствии поля `CreatorCpu` выключается только часть `Designer`, при отсутствии `CurrentResearchUI.targetTechnology` — только `LabUi`
  - **Depends**: TSK-028, TSK-035

- [ ] **TSK-031**: Совместимость сейвов
  - **Requirement**: NFR-3, FR-8, FR-9
  - **Deliverables**: расширение `mod/tests/PTMod.Tests/CpuArchitectures/SaveDataTests.cs`: сейв этапа 1 → этап 2; битые записи; неизвестное семейство; `resumeTech` вне диапазона; перенос состояния `ArchitectureApi.TransferCompanyState` (объединение исследованного, максимум поддержки)
  - **Acceptance**: все тесты зелёные; ручная часть M8 повторена на версии этапа 2
  - **Depends**: TSK-028

- [ ] **TSK-032**: Локализация полной версии
  - **Requirement**: NFR-5, NFR-6
  - **Deliverables**: ключи этапа 2 (`arch.support.*`, `arch.eco.*`, `arch.notify.*`, `arch.mail.*`, `arch.royalty.*`, `arch.designer.immature`, `arch.designer.extWarn`) в `ru.json`/`en.json`; подсказки с формулой эффекта для карточек и строк поддержки
  - **Acceptance**: `L10nKeysTests` зелёный; подсказка карточки показывает разложение «×сродство × поддержка × экосистема = итог»
  - **Depends**: TSK-028, TSK-023, TSK-035

- [ ] **TSK-033**: Ручная проверка полной версии в игре (владелец)
  - **Requirement**: FR-10 … FR-16, NFR-1 … NFR-7
  - **Deliverables**: отметки о прохождении M9–M15 (и повтор M1–M8 на финальной сборке); подтверждение ставки роялти (остаточный вопрос 1)
  - **Acceptance**: все сценарии пройдены; 0 исключений `[PTMod]` от фичи за ≥ 2 часа игры; отчёт TSK-029 принят владельцем
  - **Depends**: TSK-029, TSK-030, TSK-031, TSK-032

## Dependency graph

```
mod-framework (TSK-015/021 ModContent/ModSettings, TSK-022 IModFeature, TSK-023 ModHooks, TSK-024 ModTicks, TSK-025 ModSave, TSK-026 ModCompanies,
               TSK-027/029/059/060 ModModifiers + P4/P19, TSK-033/037/062 ModNotify/ModMail, TSK-036/039/064 ModUI/хаб/Tooltip/Confirm,
               TSK-055 ModSegments, TSK-056 ModEvents, TSK-043 core.json, TSK-066 ModIds, TSK-067 мини-раннер)
research-tree-ux/TSK-014 (ResearchTreeUxApi, ILabOccupancySource)

ЭТАП 1 (MVP)
TSK-001 ──> TSK-002
TSK-002 ──> TSK-003, TSK-004, TSK-006, TSK-014
TSK-003 + TSK-004 ──────────────> TSK-005 ──> TSK-034 (+ ModHooks, ModUI.Confirm, research-tree-ux/TSK-014)
TSK-005 ──> TSK-012 ──> TSK-013 (+ TSK-034, research-tree-ux/TSK-014)
TSK-003 + TSK-006 ──────────────> TSK-007
TSK-003 ────────────────────────> TSK-008, TSK-009
TSK-009 + TSK-006 ──────────────> TSK-010
TSK-008 + TSK-007 ──────────────> TSK-011 (+ research-tree-ux/TSK-014)
TSK-014 + TSK-003 + TSK-008 ────> TSK-015
TSK-002 + TSK-004 ──────────────> TSK-016
TSK-016 + TSK-005 + TSK-015 + TSK-034 ──> TSK-017
TSK-007 + TSK-010 + TSK-011 + TSK-013 + TSK-017 + TSK-034 ──> TSK-018
TSK-012 + TSK-013 + TSK-018 ────> TSK-019 ──> TSK-020 [ГЕЙТ: ручная проверка MVP владельцем]

ЭТАП 2 (полная)
TSK-020 ──> TSK-021 ──> TSK-022 (+ TSK-007)
TSK-022 + TSK-012 ──────────────> TSK-023
TSK-022 ────────────────────────> TSK-024
TSK-022 + TSK-010 ──────────────> TSK-025
TSK-022 ────────────────────────> TSK-026 (+ research-tree-ux/TSK-014)
TSK-024 + TSK-017 ──────────────> TSK-027 ──> TSK-028 (+ ModNotify/ModMail)
TSK-022 + TSK-008 + TSK-020 ────> TSK-035
TSK-025 + TSK-026 + TSK-027 ────> TSK-029
TSK-028 + TSK-035 ──────────────> TSK-030
TSK-028 ────────────────────────> TSK-031
TSK-028 + TSK-023 + TSK-035 ────> TSK-032
TSK-029 + TSK-030 + TSK-031 + TSK-032 ──> TSK-033 [ручная проверка полной версии владельцем]
```

## Progress

| Задача | Этап | Статус |
|---|---|---|
| TSK-001 Каркас фичи | 1 | Pending |
| TSK-002 Контент и валидатор | 1 | Pending |
| TSK-003 Реестр состояния и ModSave | 1 | Pending |
| TSK-004 Математика исследований и правила лаборатории | 1 | Pending |
| TSK-005 Сервис исследования | 1 | Pending |
| TSK-006 Математика рейтинга | 1 | Pending |
| TSK-007 Провайдер канала рейтинга | 1 | Pending |
| TSK-008 Отпечатки и привязка CPU | 1 | Pending |
| TSK-009 Конструктор: список архитектур | 1 | Pending |
| TSK-010 Множители дизайна | 1 | Pending |
| TSK-011 Инспектор, сравнение, модификаторы | 1 | Pending |
| TSK-012 Панель: карточки | 1 | Pending |
| TSK-013 Блок лаборатории, вкладка, окно | 1 | Pending |
| TSK-014 Политика посева | 1 | Pending |
| TSK-015 Посев и миграция | 1 | Pending |
| TSK-016 Политика ИИ (MVP) | 1 | Pending |
| TSK-017 Сервис ИИ | 1 | Pending |
| TSK-018 Выключение и ручка силы | 1 | Pending |
| TSK-019 Локализация MVP | 1 | Pending |
| TSK-020 Ручная проверка MVP (владелец, гейт) | 1 | Pending |
| TSK-021 Математика поддержки | 2 | Pending |
| TSK-022 Сервис поддержки | 2 | Pending |
| TSK-023 UI поддержки | 2 | Pending |
| TSK-024 Экосистема | 2 | Pending |
| TSK-025 Опыт ISA и незрелые инструменты | 2 | Pending |
| TSK-026 Ворота расширений | 2 | Pending |
| TSK-027 Адаптивный ИИ | 2 | Pending |
| TSK-028 Обзор и уведомления | 2 | Pending |
| TSK-029 Балансовая симуляция | 2 | Pending |
| TSK-030 Самопроверка и производительность | 2 | Pending |
| TSK-031 Совместимость сейвов | 2 | Pending |
| TSK-032 Локализация полной версии | 2 | Pending |
| TSK-033 Ручная проверка полной версии (владелец) | 2 | Pending |
| TSK-034 Общая лаборатория: арбитр слота | 1 | Pending |
| TSK-035 Роялти x86 без лицензии | 2 | Pending |

## Трассировка FR → TSK

| Требование | Задачи |
|---|---|
| FR-1 | TSK-002 |
| FR-2 | TSK-004, TSK-005, TSK-011, TSK-013, TSK-034 |
| FR-3 | TSK-012, TSK-013 |
| FR-4 | TSK-009, TSK-010 |
| FR-5 | TSK-003, TSK-008, TSK-011, TSK-014 |
| FR-6 | TSK-006, TSK-007 |
| FR-7 | TSK-005, TSK-016, TSK-017, TSK-034 |
| FR-8 | TSK-003, TSK-014, TSK-015 |
| FR-9 | TSK-001, TSK-007, TSK-018, TSK-034 |
| FR-10 | TSK-021, TSK-022, TSK-023 |
| FR-11 | TSK-024 |
| FR-12 | TSK-025 |
| FR-13 | TSK-026 |
| FR-14 | TSK-027, TSK-029 |
| FR-15 | TSK-028 |
| FR-16 | TSK-035 |
| NFR-1, NFR-2 | TSK-001, TSK-030 |
| NFR-3 | TSK-003, TSK-031 |
| NFR-4 | TSK-006, TSK-029 |
| NFR-5 | TSK-019, TSK-032 |
| NFR-6 | TSK-023, TSK-032 |
| NFR-7 | TSK-001, TSK-007, TSK-008, TSK-010 |
