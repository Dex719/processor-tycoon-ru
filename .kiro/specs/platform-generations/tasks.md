# Tasks: platform-generations

## Document Information

- **Feature Name**: platform-generations
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/platform-generations/requirements.md`
  - Design: `.kiro/specs/platform-generations/design.md`
  - Framework: `.kiro/specs/mod-framework/` (каналы `market.rating.cpu`, `contract.signChance`; `ModEvents`, `ModTicks`; номера задач — `.kiro/specs/mod-framework/tasks.md`)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Фича строится снизу вверх:

1. контент и валидация;
2. чистые модели с тестами (каталог, сокеты, база, хронология, реконструкция, смена сокета, рейтинг, OEM, движок эффектов);
3. интеграция: провайдеры каналов, обработчики событий ядра `ModEvents.CpuReleased` / `CpuTransferred`, подписки, реконструкция;
4. окна и уведомления;
5. офлайн-симуляция баланса и ручная проверка владельца.

Эффекты на игру включаются только через провайдеры каналов (TSK-009). До этой задачи фича ничего не меняет в игре.

### Implementation Strategy
- Модели (`mod/src/PTMod/Features/PlatformGenerations/Logic/`) не ссылаются на `UnityEngine` и типы игры и используют общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`, `Range`). Вход — плоские структуры (`CpuLedgerView`, `SocketId`, `RatingInput`, `EffectCpu`), их строит один адаптер `GameAdapter`.
- Горячий путь (`Rating(ctx)`) — только поиск в кэше. Всё тяжёлое — раз в месяц.
- Числа баланса — только в `balance.json`. Тесты читают значения из встроенных файлов контента на диске, поэтому правка баланса не требует правки тестов, кроме примеров с явными числами.
- Собственных патчей на `Company.AddCpu` / `AICompany.AddCpu` нет: выпуск и перенос CPU приходят событиями ядра. Единственный патч фичи — `ProjectReleaseWindow.Open` (TSK-014).
- Каждая задача заканчивается сборкой (`python mod/build.py --game-dir <папка игры>`) и зелёными тестами (`python mod/build.py --tests`).

### Development Approach
- **Testing Strategy**: TDD для моделей (числа из design.md, «Формулы и баланс»); каждый публичный тип `Logic/` указан в `[Covers]`; офлайн-симуляция баланса (TSK-022); самопроверка целей; ручные сценарии M1–M14.
- **Integration Strategy**: провайдеры регистрируются в `ModModifiers` один раз в `Register`, события — через `ModEvents`, всё под переключателями `ModSettings`.
- **Deployment Strategy**: в составе `PTMod.dll` через `install.py --with-mods`.

> Номера задач `mod-framework` в поле **Depends** сверены с `.kiro/specs/mod-framework/tasks.md` (v0.2): ModHooks — TSK-023, ModTicks — TSK-024, ModSave/ModRandom — TSK-025, ModCompanies — TSK-026, ModModifiers — TSK-027/059/060 (рейтинг P4 — TSK-029, шанс P8 — TSK-030), ModConditions — TSK-019/032, ModNotify — TSK-033, ModUI — TSK-034…036/064, ModMail — TSK-037/038/062, ModEvents — TSK-056, ModIds — TSK-066, мини-раннер — TSK-067.

## Фаза 1 — Каркас и контент

- [ ] **TSK-001**: Скелет фичи, настройки, секция `ModSave`
  - **Requirement**: FR-12, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/PlatformGenerationsFeature.cs` (`[ModFeature]`, `IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn`, `EnabledByDefault`, `SupportsHotToggle`, `Register(FeatureContext)`, `OnEnabled`/`OnDisabled`; `IModSaveParticipant`), `.../PlatformSettings.cs` (`ParamSpec`), `.../Logic/SaveModels.cs` (DTO без поля версии)
  - **Acceptance**:
    - фича с ID `platform-generations` в `ModFeatures.All` и в хабе «Моды»: переключатель фичи + 3 `ParamSpec` (Bool: `ratingEffects`, `oemEffects`, `notifications`) FR-13.1;
    - `ResetToDefaults` при `ModSave.Status` = `NewGame` очищает состояние до перемотки;
    - секция пишется и читается (`ModJson.Bind` / `ModJson.From`, пустая); `Write()` не возвращает `_schema`, `SchemaVersion = 1`;
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`ModFeatures`, `FeatureContext`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`), TSK-025 (`ModSave`), TSK-039 (хаб «Моды»), TSK-067 (мини-раннер)

- [ ] **TSK-002**: Контент `generations.json` и `balance.json`
  - **Requirement**: FR-1, NFR-5
  - **Deliverables**: `mod/src/PTMod/Content/platform-generations/generations.json` (10 поколений из таблицы design.md), `mod/src/PTMod/Content/platform-generations/balance.json`, `mod/src/PTMod/Features/PlatformGenerations/Logic/PlatformBalance.cs`
  - **Acceptance**:
    - все 16 корпусов и 9 стандартов памяти игры встречаются в поколениях (корпус — ровно в одном, память может повторяться);
    - годы совпадают с таблицей design.md;
    - имена поколений `ru`/`en` заполнены реальными названиями сокетов (DIP-40, PLCC-68, PGA-132, Socket 1–3, Socket 5/7, LGA 775 / Socket 939, LGA 1156 / AM3, LGA 1151 / AM4, LGA 1700 / AM5, LGA 2030 A/B), `id` поколений не менялись;
    - `balance.json` содержит все поля с дефолтами design.md, включая `oem.fullPlatformBonus` = 0.05, `oem.mode` = `auto`; Calculator в `contractTypes` отсутствует.
  - **Depends**: TSK-001; mod-framework TSK-021 (`ModContent`), TSK-043 (`core.json`, таблица префиксов)

- [ ] **TSK-003**: `GenerationCatalog` и `SocketIdentity`
  - **Requirement**: FR-1, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/GenerationCatalog.cs`, `.../Logic/SocketIdentity.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/GenerationCatalogTests.cs`, `.../SocketIdentityTests.cs`
  - **Acceptance**:
    - встроенный контент: 10 поколений без ошибок;
    - тесты на дубликат сокета, несуществующий корпус или память, дубликат `order`, выход за диапазон — запись пропущена, ошибка в списке;
    - `ByBasePackage` для 16 имён; `IsFullPlatform` (DDR для `gen-2000` — да, SDR — нет);
    - статусы Expected / Announced / EarlyWindow / Mature / Obsolete по датам;
    - ключи `pkg:`/`custom:`, совместимость по базе и по `compatibleSockets`.
  - **Depends**: TSK-002

## Фаза 2 — Модели

- [ ] **TSK-004**: `InstalledBaseLedger`
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/InstalledBaseLedger.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/InstalledBaseLedgerTests.cs`
  - **Acceptance**:
    - AC-4.1 (≈ 83 120 при допуске 1);
    - `ApplyMonth` × 120 ≡ `RebuildFull` (допуск 0.1 %);
    - смена `CompanyKey` у CPU переносит базу;
    - `null`/короткие ledger — вклад 0;
    - `MainSocketOf` исключает указанный сокет;
    - `RebuildFull` на 1000 × 720 синтетических данных ≤ 50 мс (тест-бенч).
  - **Depends**: TSK-003

- [ ] **TSK-005**: `PlatformTimeline` — анонсы, запуски, ранние последователи
  - **Requirement**: FR-2, FR-3
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/PlatformTimeline.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/PlatformTimelineTests.cs`
  - **Acceptance**:
    - анонс не дублируется;
    - запуск раньше `announceYear` → анонс и запуск в один день;
    - слоты (3) и окно (`earlyWindowMonths`) соблюдаются;
    - второй CPU компании на том же сокете не меняет статус;
    - `Preview` не меняет состояние;
    - сериализация в DTO туда-обратно.
  - **Depends**: TSK-003, TSK-004

- [ ] **TSK-006**: `ChurnDetector` и появления сокетов
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/ChurnDetector.cs`, расширение `PlatformTimeline.OnCpuReleased`, `mod/tests/PTMod.Tests/PlatformGenerations/ChurnDetectorTests.cs`
  - **Acceptance**:
    - AC-6.1 (`severity` 0.667, штраф 6.67 %);
    - возраст ≥ 36 → 0;
    - доля основного < 0.02 → 0;
    - совместимые по базе (AC-6.2) → 0;
    - появление сокета записывается для каждой пары «компания × сокет» один раз.
  - **Depends**: TSK-005

- [ ] **TSK-007**: `RatingEffectModel`
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/RatingEffectModel.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/RatingEffectModelTests.cs`
  - **Acceptance**:
    - AC-5.1 (1.075 / 1.0375), AC-5.2 (`B_ib` 0.0375);
    - затухание `B_early` (0.048 через 12 мес.) и `P_churn` (0.0333 через 12 мес.);
    - клампы `minMult`/`maxMult`; вклад по умолчанию в [0.90; 1.16], для «Промышленности» [0.95; 1.08];
    - `IB_всего = 0` → 1.0.
  - **Depends**: TSK-004, TSK-006

- [ ] **TSK-008**: `OemAdoptionModel` (с полной платформой), `Roll`, `OemPreferences`, `ContractKeyBuilder`
  - **Requirement**: FR-8, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/OemAdoptionModel.cs`, `.../Logic/OemPreferences.cs`, `.../Logic/ContractKeyBuilder.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/OemAdoptionModelTests.cs`
  - **Acceptance**:
    - AC-8.1 (0.475);
    - незапущенное → 0;
    - устаревшее на дату создания или на сегодня пропускается;
    - `PlatformMultiplier` без полной платформы: 1.20 / 0.75 / 0.50 / 1.0; с полной платформой: 1.25 / 0.80 / 0.55 / 1.0; AC-8.5; Calculator (тип 0) → 1.0;
    - `MarketAverageMultiplier`: AC-8.4 (≈ 1.023, ≈ 1.237 и ≈ 0.976), `Σ p = 1`, `order < marketAverageMinGenOrder` → 1.0;
    - `WithLoyalty` и клампы (максимум 1.375, минимум 0.50);
    - `Roll(key, gen, ModRandom.WorldSeed)` детерминирован и равномерен (χ² на 10 000 ключей, p > 0.01);
    - `Prune` удаляет исчезнувшие ключи; `ContractKeyBuilder.Build` стабилен и экранирует `|`.
  - **Depends**: TSK-003, TSK-004; mod-framework TSK-025 (`ModRandom.WorldSeed`)

## Фаза 3 — Интеграция с игрой

- [ ] **TSK-009**: `PlatformEffectEngine` и `PlatformModifierProvider` — каналы и кэш
  - **Requirement**: FR-5, FR-8, FR-10, FR-13, NFR-1, NFR-2, NFR-5, NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/PlatformEffectEngine.cs`, `.../PlatformModifierProvider.cs`, `.../GameAdapter.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/PlatformEffectEngineTests.cs`
  - **Acceptance**:
    - `ModModifiers.RegisterProvider(FeatureId, ModChannels.MarketRatingCpu, Rating, reasonKey: "pg.reason.rating")` и `ModModifiers.RegisterReasonedProvider(FeatureId, ModChannels.ContractSignChance, Chance)` зарегистрированы один раз в `Register`; `OnEnabled`/`OnDisabled` их не пересоздают;
    - `Rating` — поиск в `Dictionary<Cpu, float[2]>` по `ctx.Cpu`/`ctx.Segment` (`Segment.Industries` → индекс 1) без аллокаций (проверка в тест-бенче);
    - `Chance`: режим выбирается по `ctx.Contract` (есть — «по контрактам», нет — «рыночный»); `oem.mode = marketAverage` принудительно включает «рыночный» (тесты `PlatformEffectEngineTests` на плоских входах); причина `pg.reason.oemPrefers` / `oemLag` / `oemAdoption` / `oemLoyalty` в `ProviderResult`, массивы аргументов подготовлены заранее;
    - при `ModTicks.IsFastForwarding` и выключенных эффектах провайдеры возвращают 1.0; изменение настройки вызывает `ModModifiers.Invalidate` (эффект сразу), выключенная фича — сразу (ядро пропускает провайдеры);
    - после выпуска CPU и смены владельца кэш CPU обновляется и вызывается `ModModifiers.Invalidate(channel, companyKey)`;
    - исключение → 1.0 через `ModHooks.Guard`, `ModLog`;
    - игрок и ИИ обрабатываются одним кодом.
  - **Depends**: TSK-007, TSK-008; mod-framework TSK-026 (`ModCompanies`), TSK-027, TSK-059 (`RegisterReasonedProvider`, `Invalidate`), TSK-060 (`ModifierContext.Contract`), TSK-029 (P4), TSK-030 (P8)

- [ ] **TSK-010**: Обработчики `ModEvents.CpuReleased` и `CpuTransferred`
  - **Requirement**: FR-3, FR-4, FR-6
  - **Deliverables**: обработчики в `mod/src/PTMod/Features/PlatformGenerations/PlatformGenerationsFeature.cs` (или `.../ReleaseHandlers.cs`), подписка `ModEvents.CpuReleased += …`, `ModEvents.CpuTransferred += …` в `Register`
  - **Acceptance**:
    - при `duringFastForward = true` или `isVariant = true` обработчик выходит сразу;
    - иначе вызывает `Timeline.OnCpuReleased`, `Provider.UpdateCpu`, `ModModifiers.Invalidate` и уведомления (`Notifier`);
    - `CpuTransferred` обновляет кэш множителей CPU;
    - исключение изолирует `ModHooks.Raise` и засчитывает фиче; своих патчей на `Company.AddCpu` / `AICompany.AddCpu` нет (самопроверка: `ModHooks.IsReserved` для них — `true`, фича их не патчит);
    - загрузка сейва событий не вызывает (проверка в TSK-024, M8/M9).
  - **Depends**: TSK-005, TSK-006, TSK-009; mod-framework TSK-056 (`ModEvents`)

- [ ] **TSK-011**: Подписки: год, месяц, конец перемотки, контракты, загрузка
  - **Requirement**: FR-2, FR-4, FR-8, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/PlatformSubscriptions.cs`
  - **Acceptance**:
    - `ctx.OnTick(TickPhase.YearPassed / LateMonthPassed, …)` без `RunDuringFastForward`: в перемотку и при загрузке не вызываются;
    - `ctx.OnTick(TickPhase.FastForwardEnded, …)` → `Reconstruction.Run` (TSK-012) + `RebuildFull` + кэш + проверка инвариантов, без писем и уведомлений;
    - режим «по контрактам»: `ContractManager.OnNewContracts` (переподписка на `PTModHost.SceneReady`) назначает предпочтения только контрактам без них (`ContractKey`, `t_создания = AcceptanceDate − 12 мес.`); в перемотку выход сразу; контракты из перемотки получают предпочтение лениво;
    - `Read` восстанавливает секцию; `ModTicks.GameReady` пересобирает базу и кэш.
  - **Depends**: TSK-009; mod-framework TSK-024 (`ModTicks`), TSK-025 (`ModSave`, `PTModHost`)

- [ ] **TSK-012**: `Reconstruction` — один алгоритм для конца перемотки, старых сейвов и повторного включения
  - **Requirement**: FR-12, FR-13, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Logic/Reconstruction.cs`, `mod/tests/PTMod.Tests/PlatformGenerations/ReconstructionTests.cs`
  - **Acceptance**:
    - на синтетическом наборе CPU (год, `SaveID`, компания, корпус): анонсы по `announceYear ≤ года`, запуск — минимальный `Cpu.Year` (1 января), ранние последователи по `(Year, SaveID)` в окне, появления сокетов с `severity` 0;
    - идемпотентность: повторный запуск ничего не меняет; результат один для «конец перемотки», «старый сейв без секции» и «повторное включение»;
    - письма, уведомления и события не создаются;
    - неизвестные `id` поколений в секции сохраняются;
    - `OnEnabled` после выключения фичи догоняет пропущенный период и делает `RebuildFull` (FR-13.3).
  - **Depends**: TSK-011

## Фаза 4 — Интерфейс и уведомления

- [ ] **TSK-013**: `PlatformNotifier` — письма и уведомления
  - **Requirement**: FR-2, FR-3, FR-6
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/PlatformNotifier.cs`
  - **Acceptance**:
    - анонс — одно письмо (`Tag = "pg:announce:<genId>"`, `MailPriority.Normal`) и одно уведомление;
    - запуск — одно уведомление;
    - ранний последователь (игрок) — письмо (`pg:early:<genId>`) и уведомление;
    - штраф игроку — уведомление;
    - ничего в перемотке, при загрузке и при `notifications = false`; в пакетном прогоне показ откладывает ядро.
  - **Depends**: TSK-010, TSK-011; mod-framework TSK-033 (`ModNotify`), TSK-037, TSK-062 (`ModMail`: теги, приоритеты)

- [ ] **TSK-014**: `ReleaseWarning` — предупреждение в окне выпуска
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/Patches/ReleaseWarning.cs`
  - **Acceptance**:
    - postfix `ProjectReleaseWindow.Open(Cpu)` (`Optional`) показывает уведомление по `Timeline.Preview` (штраф или ранний переход);
    - для CPU без изменений — ничего;
    - состояние не меняется.
  - **Depends**: TSK-010, TSK-013

- [ ] **TSK-015**: Окно «Платформы» — вкладка «Поколения»
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/UI/PlatformBrowserWindow.cs`, `.../UI/GenerationsTab.cs`
  - **Acceptance**:
    - окно открывается из хаба;
    - по каждому поколению: имя (реальный сокет), сокеты, память, статус, даты, первопроходец, ранние последователи, `A_G` (полоса и процент);
    - тема игры применяется;
    - при 0 валидных поколений — «нет поколений».
  - **Depends**: TSK-011; mod-framework TSK-034, TSK-035, TSK-036 (`ModUI`)

- [ ] **TSK-016**: Вкладки «Установленная база» и «Мои CPU»
  - **Requirement**: FR-9, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/UI/InstalledBaseTab.cs`, `.../UI/MyCpusTab.cs`
  - **Acceptance**:
    - таблица базы по убыванию с фильтром «мои / все», имя сокета (`CustomSocket.Name` или `Package.Name`), стандарт или свой, доля, возраст;
    - «Мои CPU»: `B_ib`, `B_early`, `P_churn`, итоги по потребительским сегментам и «Промышленности», признак «полная платформа» (память CPU входит в стандарты поколения), число контрактов с отставанием и средний множитель (режим «по контрактам»), `c̄` (запасной режим);
    - строка риска смены сокета (`ModUI.Tooltip` с динамическим текстом).
  - **Depends**: TSK-015; mod-framework TSK-064 (мост подсказок `ModUI.Tooltip`)

- [ ] **TSK-017**: Причины множителей для `ModModifiers.Explain` (`PlatformInsights`)
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/PlatformInsights.cs` (`RatingOf`, `OemOf`, `GenerationOf`), доводка причин провайдеров из TSK-009
  - **Acceptance**:
    - `ModModifiers.Explain("market.rating.cpu", ctx)` для множителя ≠ 1.0 возвращает запись `platform-generations` с `ReasonKey` = `pg.reason.rating`;
    - `ModModifiers.Explain("contract.signChance", ctx)` с контрактом в контексте возвращает запись с `pg.reason.oemPrefers` / `oemLag` / `oemAdoption` / `oemLoyalty` и аргументами;
    - публичного `Explain` у фичи нет; составляющие `B_ib`, `B_early`, `P_churn` строятся лениво только для окна «Платформы» (не в рыночном тике);
    - с `contract-transparency` видна строка AC-10.1 (проверяется в TSK-024).
  - **Depends**: TSK-009; mod-framework TSK-059 (`Explain`, `ProviderResult`)

- [ ] **TSK-018**: Предикаты `pg.*` и события `PlatformEvents`
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/PlatformGenerations/PlatformConditions.cs`, `.../PlatformEvents.cs`
  - **Acceptance**:
    - типы `pg.generationLaunched` (`gen`), `pg.earlyAdopter` (`gen`, `company`), `pg.installedBaseShare` (`atLeast`, `company`) зарегистрированы через `ModConditions.Register`, не отклонены проверкой префикса и верны на синтетическом состоянии;
    - события поднимаются через `ModHooks.Raise` вне перемотки один раз.
  - **Depends**: TSK-010, TSK-011; mod-framework TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

## Фаза 5 — Строки, проверки, баланс

- [ ] **TSK-019**: Строки `pg.*` (ru, en)
  - **Requirement**: NFR-6
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (раздел `pg.*`)
  - **Acceptance**:
    - каждый ключ из кода есть в обоих файлах; `python install.py --check` не находит расхождений ru/en и чужих префиксов;
    - числа через `StringFormatter.FloatToIntString`.
  - **Depends**: TSK-013…TSK-017; mod-framework TSK-013 (`ModL10n`), TSK-043, TSK-045 (`install.py --check`)

- [ ] **TSK-020**: Самопроверка и замеры производительности
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**: регистрация целей и членов в `PlatformGenerationsFeature.cs`; таймеры `ModLog` в `ApplyMonth`, `RebuildFull`, `Reconstruction.Run`, `RebuildCache`; счётчик вызовов провайдера
  - **Acceptance**:
    - самопроверка находит `ProjectReleaseWindow.Open`, `CustomSocket.CustomHardwareID`, `Cpu.Memory`, `Memory.Name`, `CpuBase.Market`, `DateController.get_MonthsSinceStart`, `ContractManager.OnNewContracts`;
    - подписки на `ModEvents.CpuReleased` / `CpuTransferred` и провайдеры зарегистрированы;
    - отладочный лог (`ModSettings.VerboseLog`) показывает время операций.
  - **Depends**: TSK-009…TSK-016; mod-framework TSK-042 (`ModSelfCheck`)

- [ ] **TSK-021**: Тесты совместимости сейвов и детерминизма
  - **Requirement**: NFR-3, NFR-4
  - **Deliverables**: `mod/tests/PTMod.Tests/PlatformGenerations/SaveModelTests.cs`, фикстуры `mod/tests/PTMod.Tests/PlatformGenerations/Fixtures/*.ptmod.json`
  - **Acceptance**:
    - фикстуры без секции, с `_schema` 1 и 99 грузятся (для `_schema` 99 секцию сохраняет ядро, фича работает с реконструкцией);
    - повторное назначение предпочтений тому же набору контрактов с тем же сидом даёт 100 % совпадение;
    - запись не трогает чужие секции.
  - **Depends**: TSK-008, TSK-012; mod-framework TSK-016 (`SideFileModel`)

- [ ] **TSK-022**: Офлайн-симуляция баланса
  - **Requirement**: NFR-5, NFR-7, FR-5, FR-6, FR-8
  - **Deliverables**: `mod/tests/PTMod.Tests/PlatformGenerations/BalanceSimulationTests.cs`
  - **Acceptance**:
    - 14 синтетических компаний 1971–2030;
    - min/max множителя рейтинга в `[0.85; 1.20]` (практически `[0.90; 1.16]`), шанса — в `[0.40; 1.40]`;
    - закрытый сокет с 40 % базы → ровно +10 %;
    - ИИ, переходящий раз в 5 лет на свой сокет на новой базе, получает 0 штрафов;
    - при подмешанных синтетических множителях других фич итог на CPU ограничен [0,5; 2,0] (имитация ядра), собственный вклад фичи кламп не повышает;
    - отчёт min/max/среднее печатается в вывод теста (для владельца).
  - **Depends**: TSK-007, TSK-008

- [ ] **TSK-023**: Описание фичи для игроков
  - **Requirement**: FR-9, FR-13
  - **Deliverables**: ключи `pg.hub.*` с описанием в `mod/src/PTMod/L10n/*.json`; раздел «Поколения платформ» в `mod/README.md` (как работает база, ранний переход, смена сокета, OEM, полная платформа; таблица поколений с реальными названиями сокетов)
  - **Acceptance**: описание совпадает с числами `balance.json` по умолчанию; есть пример «30 % базы → +7.5 %».
  - **Depends**: TSK-019

- [ ] **TSK-024**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1…FR-13, NFR-1, NFR-3
  - **Deliverables**: отметки о прохождении сценариев M1–M14 из design.md в разделе «Progress» (Evidence: дата, версия мода, замечания по балансу)
  - **Acceptance**:
    - все сценарии M1–M14 пройдены;
    - в `Player.log` нет исключений `[PTMod]` от фичи;
    - перемотка 1971→2000 медленнее не больше чем на 5 %;
    - стартовые величины (`ibMaxBonus`, `earlyBonus`, `churnPenalty`, `adoptBonus`, `lagPenalty`, `fullPlatformBonus`) подтверждены или скорректированы в `balance.json`;
    - ключ дневного кеша ядра различает контракты одного шаблона (M7).
  - **Depends**: TSK-001…TSK-023; mod-framework TSK-044, TSK-068 (установка `install.py --with-mods`); contract-transparency (для M7, необязательно)

## Dependency graph

```
mod-framework (TSK-022 FeatureContext, TSK-023 ModHooks, TSK-021 ModSettings/ModContent, TSK-025 ModSave/ModRandom, TSK-026 ModCompanies,
               TSK-027/059/060 ModModifiers [контекст Contract, причины, Invalidate], TSK-029/030 каналы P4/P8, TSK-024 ModTicks,
               TSK-056 ModEvents, TSK-034…036/064 ModUI, TSK-033 ModNotify, TSK-037/038/062 ModMail, TSK-019/032 ModConditions, TSK-066 ModIds, TSK-067 мини-раннер)
        │
TSK-001 ──> TSK-002 ──> TSK-003 ──> TSK-004 ──┬──> TSK-005 ──> TSK-006 ──┐
                                               │                          ├──> TSK-007 ──┐
                                               └──> TSK-008 ──────────────┼──────────────┼──> TSK-009 ──┬──> TSK-010 ──┬──> TSK-013 ──> TSK-014
                                                        │                 │              │              │              └──> TSK-018
                                                        │                 │              │              ├──> TSK-011 ──┬──> TSK-012 ──> TSK-021
                                                        │                 │              │              │              ├──> TSK-015 ──> TSK-016
                                                        │                 │              │              │              └──> TSK-018
                                                        │                 │              │              └──> TSK-017
                                                        └─────────────────┴──> TSK-022 <─┘
TSK-013…TSK-017 ──> TSK-019 ──> TSK-023
TSK-009…TSK-016 ──> TSK-020
TSK-001…TSK-023 ──> TSK-024 (владелец)
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Скелет, настройки, секция ModSave | Pending |
| TSK-002 generations.json, balance.json | Pending |
| TSK-003 GenerationCatalog, SocketIdentity | Pending |
| TSK-004 InstalledBaseLedger | Pending |
| TSK-005 PlatformTimeline | Pending |
| TSK-006 ChurnDetector | Pending |
| TSK-007 RatingEffectModel | Pending |
| TSK-008 OemAdoptionModel, Roll, OemPreferences | Pending |
| TSK-009 PlatformEffectEngine, PlatformModifierProvider | Pending |
| TSK-010 Обработчики ModEvents.CpuReleased / CpuTransferred | Pending |
| TSK-011 Подписки | Pending |
| TSK-012 Reconstruction | Pending |
| TSK-013 PlatformNotifier | Pending |
| TSK-014 ReleaseWarning | Pending |
| TSK-015 Окно: Поколения | Pending |
| TSK-016 Окно: База и Мои CPU | Pending |
| TSK-017 Причины множителей (Explain) | Pending |
| TSK-018 Предикаты pg.* и события | Pending |
| TSK-019 Строки pg.* | Pending |
| TSK-020 Самопроверка и замеры | Pending |
| TSK-021 Совместимость сейвов, детерминизм | Pending |
| TSK-022 Офлайн-симуляция баланса | Pending |
| TSK-023 Описание для игроков | Pending |
| TSK-024 Ручная проверка в игре (владелец) | Pending |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-002, TSK-003 |
| FR-2 | TSK-005, TSK-011, TSK-013 |
| FR-3 | TSK-005, TSK-010, TSK-013 |
| FR-4 | TSK-004, TSK-010, TSK-011 |
| FR-5 | TSK-007, TSK-009, TSK-016, TSK-022 |
| FR-6 | TSK-003, TSK-006, TSK-010, TSK-013, TSK-022 |
| FR-7 | TSK-014 |
| FR-8 | TSK-008, TSK-009, TSK-011, TSK-022 |
| FR-9 | TSK-015, TSK-016, TSK-023 |
| FR-10 | TSK-009, TSK-017 |
| FR-11 | TSK-018 |
| FR-12 | TSK-001, TSK-011, TSK-012 |
| FR-13 | TSK-001, TSK-009, TSK-012, TSK-023 |
| NFR-1 | TSK-009, TSK-020, TSK-024 |
| NFR-2 | TSK-009, TSK-010, TSK-020 |
| NFR-3 | TSK-012, TSK-021, TSK-024 |
| NFR-4 | TSK-008, TSK-021 |
| NFR-5 | TSK-002, TSK-022 |
| NFR-6 | TSK-019 |
| NFR-7 | TSK-009, TSK-022 |
| NFR-8 | TSK-003, TSK-012, TSK-021 |
