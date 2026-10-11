# Tasks: design-flaw-recalls

## Document Information

- **Feature Name**: design-flaw-recalls — скрытый брак в выпущенных CPU
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/design-flaw-recalls/requirements.md`
  - Design: `.kiro/specs/design-flaw-recalls/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала строится чистая логика (риск, обнаружение, котировки, политика ИИ, спешка) с юнит-тестами, затем адаптеры к игре, событиям ядра и `event-system`, затем UI. Каждая задача — 2–4 часа и заканчивается проверяемым результатом.

### Implementation Strategy

- Чистая логика без ссылок на Unity и игру — в `mod/src/PTMod/Features/DesignFlawRecalls/Logic/` (общее подмножество API .NET Framework 4.8 и `mscorlib` Unity: без `init`, `record`, `Index`, `Range`); тесты — `mod/tests/PTMod.Tests/DesignFlawRecalls/` (мини-раннер ядра: `[Test]`, `[Covers(typeof(X))]`, `Assert.*`; запуск `python mod/build.py --tests`; каждый публичный тип `Logic/` указан в `[Covers]`).
- Сборка — `python mod/build.py --game-dir <папка игры> [--tests]` (Roslyn `csc` из VS Build Tools, результат `mod/build/PTMod.dll`).
- Адаптеры к игре (`GameCpuView`, подписки на `ModEvents`, тики) — тонкие, с `ModHooks.Guard` на границе. Собственных патчей на `Company.AddCpu` и `AICompany.AddCpu` нет.
- Все числа — в `mod/src/PTMod/Content/design-flaw-recalls/flaws.json`, тексты — в `mod/src/PTMod/L10n/{ru,en}.json` (ключи `dfr.*`).

### Development Approach

- **Testing Strategy**: TDD для `Logic/`. Самопроверка на старте и ручные сценарии владельца — для интеграции.
- **Integration Strategy**: фича `[ModFeature] sealed class DesignFlawRecallsFeature : IModFeature` регистрируется в `PTMod.Core` и не трогает чужие патчи. Рейтинг CPU меняется только записями `ModModifiers.Add` на канал `market.rating.cpu` с областью CPU; выпуск и перенос CPU — подписка на `ModEvents`.
- **Deployment Strategy**: `install.py --with-mods`. Фича по умолчанию включена, `debug` выключен.

### Внешние зависимости

Номера TSK сверены с `tasks.md` соответствующих спек (2026-10-10).

- `mod-framework`:
  - реестр фич и точка входа: TSK-020 (граф), TSK-022 (`ModBootstrap`, `FeatureContext`);
  - настройки и контент: TSK-014 (`ModSettings`), TSK-015 (`ModContent`), TSK-021 (адаптеры);
  - сейв и случайность: TSK-016, TSK-025 (`ModSave`), TSK-012 (`ModRandom`, `IModRng`);
  - патчи и тики: TSK-023 (`ModHooks`), TSK-024 (`ModTicks`), TSK-057 (`IsBatchRun`);
  - события выпуска и переноса CPU: TSK-056 (`ModEvents.CpuReleased`, `CpuTransferred`);
  - модификаторы: TSK-017, TSK-027 (фасад), TSK-029 (P4 `market.rating.cpu`), TSK-030 (P8 `contract.signChance`), TSK-059 (причины, `Invalidate`, балансовый диапазон);
  - репутация и компании: TSK-018, TSK-026; порог дефолта: TSK-058 (`ModCredit`);
  - почта и уведомления: TSK-037, TSK-038, TSK-061, TSK-062 (`ModMail`, `Delivered`), TSK-033 (`ModNotify`);
  - UI: TSK-034–036 (шаблоны, виджеты), TSK-039 (хаб, `HubPage`), TSK-064 (`ModUI.Confirm`);
  - строки и идентификаторы: TSK-013, TSK-043 (`ModL10n`, `core.json`), TSK-066 (`ModIds`);
  - самопроверка и тесты: TSK-042, TSK-067 (мини-раннер).
- `event-system`:
  - пак и валидатор: TSK-002 (`EventRegistry.RegisterPack`);
  - эффекты: TSK-010 (`IEventEffectHandler`, `EventEffects.Register`);
  - публичный API: TSK-014 (`EventBus.Fire` / `EventFireContext` / `OnChoiceResolved`);
  - выборы и таймеры (от даты доставки письма): TSK-016; письмо и окно выбора: TSK-017, TSK-018; журнал: TSK-019.
- `press-reviews` (мягкая): `PressReviewsApi.RegisterAdjuster` — только TSK-026.
- `rival-personalities` (мягкая): потребитель `FlawRegistry.SetResponseBias` и `OnFlawStateChanged` — только TSK-021.
- `rival-acquisitions` (мягкая): поднимает `ModEvents.CpuTransferred` — только TSK-027.

## Фаза 1 — Каркас, контент, сохранение

- [x] **TSK-001**: Каркас фичи и настройки (авто 2026-10-10)
  - **Requirement**: FR-15, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/DesignFlawRecallsFeature.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/FlawSettings.cs`
  - **Acceptance**:
    - `[ModFeature] sealed class DesignFlawRecallsFeature : IModFeature` в `PTMod.Features.DesignFlawRecalls`: `Id = "design-flaw-recalls"`, `NameKey = "dfr.feature.name"`, `DescriptionKey = "dfr.feature.desc"`, `DependsOn = { "event-system" }` (мягкие связи не входят), `EnabledByDefault = true`, `SupportsHotToggle = true`;
    - `Register(FeatureContext)` объявляет через `ctx.Param` ключи `aiFlaws` (true), `showRiskHint` (true), `rushEnabled` (true), `flawFrequency` (1.0, 0.5–2.0, шаг 0.1), `debug` (false); включение фичи — общий переключатель `ModSettings`;
    - `OnDisabled` запоминает `lastActiveDay` и НЕ вызывает `ModModifiers.RemoveAll`; `OnEnabled` НЕ пересоздаёт записи (заглушка сдвига сроков до TSK-008);
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`, `[ModFeature]`), TSK-014 (`ModSettings`), TSK-020 (реестр фич)

- [x] **TSK-002**: Контент баланса и валидатор (авто 2026-10-10)
  - **Requirement**: FR-2, FR-3, FR-15, FR-20, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/design-flaw-recalls/flaws.json`, `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawBalance.cs` (DTO), `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawContentValidator.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawContentValidatorTests.cs`
  - **Acceptance**:
    - `flaws.json` совпадает с примером design (раздел Data Models), включая `risk.kRush`, `rush`, `responses.pendingGraceDays`; блока `aiCaution` нет;
    - валидатор проверяет каждый диапазон из раздела Security (в том числе `kRush`, `rush.timeCut`, `rush.costMultiplier`, `pendingGraceDays`, префикс `dfr.` у `nameKey`) и подставляет значения по умолчанию;
    - тесты: по одному на каждое правило + битый JSON → значения по умолчанию;
    - встроенные значения по умолчанию в коде равны значениям `flaws.json`.
  - **Depends**: TSK-001, mod-framework TSK-015 (`ModContent`), TSK-067 (мини-раннер)

- [x] **TSK-003**: Секция сохранения (авто 2026-10-10)
  - **Requirement**: FR-16, NFR-3, NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Save/FlawSaveData.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/Save/FlawSaveSection.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawSaveSectionTests.cs`
  - **Acceptance**:
    - `FlawSaveSection : IModSaveParticipant` (`SectionId = "design-flaw-recalls"`, `SchemaVersion = 1`), регистрация через `ctx.SaveParticipant`; `Read(section, schema)` с шагом миграции, `Write()` возвращает данные БЕЗ `_schema` (версию пишет ядро);
    - в `FlawSaveData` нет поля версии; есть `lastActiveDay`, `rushCpuIds`, `rushProjects`, `publicSinceDay`; блока `aiCaution` нет;
    - круговая сериализация `FlawSaveData` (пример из design) через `ModJson` без потерь;
    - `ResetToDefaults` при отсутствии секции → пустое состояние, и все текущие `SaveID` заносятся в `rolledCpuIds` (Migration);
    - схема новее → ядро выдаёт значения по умолчанию и сохраняет исходную секцию, фича выключается для сейва;
    - недопустимые enum и NaN → запись `Closed`.
  - **Depends**: TSK-001, mod-framework TSK-016, TSK-025 (`ModSave`, `_schema`), TSK-010 (`ModJson`)

## Фаза 2 — Чистая логика

- [x] **TSK-004**: Модель вероятности брака (авто 2026-10-10)
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawRiskModel.cs` (`Factors`, `Probability`), `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawRiskModelTests.cs`
  - **Acceptance**:
    - 4 примера из таблицы «Формулы и баланс», п. 1, совпадают с точностью 0.001; строка со спешкой (`RS = 1`) даёт 0.118;
    - ограничение `pMax`;
    - `unknownNodeImmaturity` при `HasNodeRecord = false`;
    - множители сложности 0 и 5;
    - `flawFrequency` 2.0 удваивает p до потолка;
    - `FlawRiskInput.Rush` игнорируется для ИИ (`IsAi`).
  - **Depends**: TSK-002

- [x] **TSK-005**: Выбор тяжести и типа (авто 2026-10-10)
  - **Requirement**: FR-3
  - **Deliverables**: `FlawRiskModel.PickSeverity`, `FlawRiskModel.PickType`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawPickTests.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FakeRng.cs` (детерминированный `IModRng`)
  - **Acceptance**:
    - при p = 0.233 распределение на 100 000 бросков с фиксированным сидом — [0.49, 0.36, 0.15] ± 0.01;
    - `security` не выпадает для 2010 года;
    - вес `stability` при OC = 0.4 равен 9;
    - без доступных типов — `arith`;
    - логика использует `IModRng` ядра (собственного интерфейса случайности нет).
  - **Depends**: TSK-004, mod-framework TSK-012 (`IModRng`)

- [x] **TSK-006**: Бросок при выпуске (подписка на `ModEvents.CpuReleased`) (авто 2026-10-10)
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawRoller.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/FlawRegistry.cs` (создание, `AttachVariant`, `WasRolled`, `IsRush`), `mod/src/PTMod/Features/DesignFlawRecalls/Game/GameCpuView.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawRollerTests.cs`
  - **Acceptance**:
    - в `Register` подписка `ModEvents.CpuReleased += FlawRoller.OnCpuReleased(owner, cpu, isVariant, duringFastForward)`, тело в `ModHooks.Guard`; патчей на `Company.AddCpu`/`AICompany.AddCpu` нет (самопроверка ядра их отклонила бы);
    - защиты: `duringFastForward` → записи нет; `rolledCpuIds`; `aiFlaws`; загрузку отсекает ядро (тест на фейке события);
    - `isVariant` привязывает вариант к записи базового CPU, а без записи базового записи не создаёт;
    - компания записи хранится ключом `ModCompanies.KeyOf`;
    - в лог пишется строка `roll cpu=<id> p=<p> result=<bool>`;
    - исключение внутри не доходит до игры.
  - **Depends**: TSK-003, TSK-005, mod-framework TSK-056 (`ModEvents`), TSK-026 (`ModCompanies`)

- [x] **TSK-007**: Модель обнаружения (авто 2026-10-10)
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/DiscoveryModel.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/DiscoveryModelTests.cs`
  - **Acceptance**:
    - таблица `h` из design (9 значений) совпадает с точностью 0.001;
    - принудительное обнаружение по `maxHiddenMonths`;
    - истечение по `expireAfterRetiredMonths` и `expireNoSalesMonths`;
    - `LeakRate` для `Ignore`, тихой ревизии и хвоста.
  - **Depends**: TSK-002

- [x] **TSK-008**: Месячный и дневной монитор (авто 2026-10-10)
  - **Requirement**: FR-4, FR-7, FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawMonitor.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawStateMachine.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawStateMachineTests.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawPauseTests.cs`
  - **Acceptance**:
    - `OnMonth` и `OnDay` подписаны через `ctx.OnTick(TickPhase.MonthPassed / DayPassed)`; монитор не проверяет `IsBatchRun` и не патчит `DateController.NextMonth`;
    - сроки хранятся датами окончания (дни от 1971-01-01); пропущенные дни ревизии доплачиваются по `lastChargedDay`;
    - `OnResume` (из `OnEnabled` и `GameReady(Loaded)`) сдвигает `steppingEndDay` и `lastChargedDay` на `today − lastActiveDay − 1`; срок заплатки не сдвигается; `lastActiveDay` обновляется в конце `OnDay`;
    - все переходы диаграммы состояний покрыты тестами, недопустимые переходы отвергаются;
    - `U` считается по `MarketData.Sold` + `SoldByContracts` и кэшируется в записи раз в месяц;
    - банкротство (`BankruptcyHandler.OnCompanyWentBankrupt`, подписка на каждой сцене через `ModTicks.GameReady`) и `FindCpu == null` закрывают запись;
    - ошибка в одной записи не останавливает остальные (3 подряд → `Closed`).
  - **Depends**: TSK-006, TSK-007, mod-framework TSK-024 (`ModTicks`)

- [x] **TSK-009**: Калькулятор котировок (авто 2026-10-10)
  - **Requirement**: FR-5, FR-6, FR-7, FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/ResponseCostCalculator.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawQuote.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/ResponseCostCalculatorTests.cs`
  - **Acceptance**:
    - пример 1994 года из design: `C_recall` = 66 000 000, `C_step` ≈ 21 428 571, `C_patch` ≈ 4 071 429, иск 24 000 000;
    - потолок отзыва 30 % выручки за 12 месяцев ставит `RecallCapped`;
    - нулевая цена → цена варианта;
    - `PatchAvailable = false` при `patchFromYear` > года;
    - котировка неизменна после сериализации.
  - **Depends**: TSK-002

## Фаза 3 — Решения игрока и последствия

- [x] **TSK-010**: События выбора и диспетчер (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Content/design-flaw-recalls/events.json` (6 событий `dfr.decide.*`), `mod/src/PTMod/Features/DesignFlawRecalls/FlawDecisionDispatcher.cs`
  - **Acceptance**:
    - пак регистрируется `EventRegistry.RegisterPack("design-flaw-recalls", …)`; 6 событий проходят валидацию движка (ID с префиксом `dfr.`, `important: true`);
    - при обнаружении брака игрока вызывается `EventBus.Fire(id, EventFireContext { CompanyKey = "player", Args = … })` с нужным вариантом события; `Args[0]` = `recordId`, остальные — по таблице design;
    - письмо выбора отправляет движок (`Critical`); таймер считается от доставки письма (проверяется в тестах `event-system`, здесь — ручной сценарий 7 и 13);
    - очередь: следующее решение отправляется по `EventBus.OnChoiceResolved` (FR-5 п. 6);
    - запись `ModModifiers` `contract.signChance` × 0.8 (причина `dfr.mod.pending`) со сроком `timerDays + pendingGraceDays` снимается при решении;
    - результат `Fire` = `Disabled` → применяется `defaultChoice` и отправляется письмо `ModMail` (`Normal`, тег `dfr:auto:<recordId>`).
  - **Depends**: TSK-008, TSK-009, event-system TSK-002 (`RegisterPack`), TSK-014 (`EventBus`), TSK-016 (выборы и таймеры), mod-framework TSK-062 (`ModMail`, `Delivered`)

- [x] **TSK-011**: Эффекты «Отзыв» и «Ревизия» (авто 2026-10-10)
  - **Requirement**: FR-6, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Effects/FlawRecallEffect.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/Effects/FlawSteppingEffect.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/FlawResponseApplier.cs` (Recall, Stepping, `CompleteStepping`, `OnLeak`), `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawEffectHandlersTests.cs`
  - **Acceptance**:
    - типы реализуют `IEventEffectHandler` (`Type`, `Validate`, `Resolve`, `Apply`, `Describe`) и зарегистрированы `EventEffects.Register`;
    - `Resolve` берёт сумму из котировки записи (`Args[0]`), `Describe` показывает её под кнопкой;
    - отзыв списывает ровно `quote.RecallCost` с `ExpenseOrigin.Fines`;
    - ревизия списывает `SteppingDailyCost` ежедневно с `ExpenseOrigin.Development` и завершается через `SteppingDays`;
    - публичная ревизия по завершении списывает `ResidualClaimsCost`;
    - утечка тихой ревизии → `Public` + `CoverUp` + полный `RecallCost`;
    - повторный вызов обработчика для закрытой записи ничего не делает.
  - **Depends**: TSK-010, event-system TSK-010 (`IEventEffectHandler`, `EventEffects`)

- [x] **TSK-012**: Эффекты «Заплатка» и «Игнор» (авто 2026-10-10)
  - **Requirement**: FR-8, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Effects/FlawSoftwarePatchEffect.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/Effects/FlawIgnoreEffect.cs`, дополнения `FlawResponseApplier` (Patch, Ignore, `ExpireMitigation`, `FundSteppingForMitigated`)
  - **Acceptance**:
    - заплатка добавляет записи `ModModifiers` (`market.rating.cpu`, `ModifierScope.Cpu`, причина `dfr.mod.patch`) с множителем `1 − ipsLoss` всем CPU записи на `PatchMonths` месяцев; по истечении запись `Resolved`; оплаченная ревизия снимает записи досрочно (`ModModifiers.Remove`);
    - вклад заплатки лежит в [0,75; 1,00] при контенте по умолчанию (тест на всех типах и тяжестях, учитывающий общий предел ядра [0,5; 2,0]);
    - игнор публичного брака:
      - разрывает контракты через `ContractManager.RemoveContract` с вероятностью `oemCancel`;
      - ставит `contract.signChance` на 12 месяцев (причина `dfr.mod.ignore`);
      - запускает дренаж репутации (`dfr.rep.drain`) и бросок иска (не больше одного);
    - утечка игнорированного внутреннего брака открывает событие `dfr.decide.leaked*` без кнопки игнора.
  - **Depends**: TSK-011, TSK-014

- [x] **TSK-013**: Репутация и популярность (авто 2026-10-10)
  - **Requirement**: FR-10
  - **Deliverables**: дополнения `FlawResponseApplier` (`ApplyReputation`, `ApplyPopularityHit`), `mod/tests/PTMod.Tests/DesignFlawRecalls/ReputationTableTests.cs`
  - **Acceptance**:
    - дельты из таблицы design (п. 7) для всех реакций и тяжестей;
    - `CoverUp` удваивает сумму базовой и ответной дельт;
    - причина в `ModReputation.Add` — ключ L10n (`dfr.rep.public`, `dfr.rep.recall`, `dfr.rep.stepping`, `dfr.rep.patch`, `dfr.rep.ignore`, `dfr.rep.drain`, `dfr.rep.leak`), идентификатор записи пишется в `ModLog`;
    - популярность умножается на `(1 − hit)` и не уходит ниже 0; индекс списка совпадает с `(int)Segment`.
  - **Depends**: TSK-011, mod-framework TSK-026 (`ModReputation`)

- [x] **TSK-014**: Записи каналов (`market.rating.cpu` для CPU, `contract.signChance` для компании) (авто 2026-10-10)
  - **Requirement**: FR-5, FR-8, FR-9, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawChannels.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawChannelsTests.cs`
  - **Acceptance**:
    - `AddCpuRating` и `AddSignChance` вызывают `ModModifiers.Add` с областями `ModifierScope.Cpu` и `ModifierScope.Company`, сроком в днях и ключом причины; идентификаторы записей (`m-…`) сохраняются в записи брака;
    - `Remove`/`RemoveAllOfRecord`/`RemoveCompanyEntries` снимают записи по идентификаторам;
    - `Reconcile` после `GameReady(Loaded)` сверяет идентификаторы с `ModModifiers.Explain`, пишет предупреждение о потерянных и снимает устаревшие записи ожидания реакции;
    - при выключении фичи `ModModifiers.RemoveAll` не вызывается, при включении записи не пересоздаются — проверено тестом на фейке (срок окончания прежний);
    - контент не допускает множитель канала ниже 0,5 (валидатор).
  - **Depends**: TSK-001, mod-framework TSK-027 (`ModModifiers`), TSK-029 (P4), TSK-030 (P8), TSK-059 (причины и `Explain`)

## Фаза 4 — ИИ

- [x] **TSK-015**: Политика выбора ИИ (авто 2026-10-10)
  - **Requirement**: FR-11, FR-17
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/AiFlawPolicy.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/IFlawResponseBias.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/AiFlawPolicyTests.cs`
  - **Acceptance**:
    - отзыв отсекается при `CashHeadroom` < 1.5 · `RecallCost`; запас денег = `MoneyAmount − ModCredit.ThresholdOf(company)` (в тестах порог подставляется фейком; `CentralBank.DebtLimit` напрямую не читается);
    - без заплатки её вес 0;
    - множители `IFlawResponseBias` применяются (порядок Recall, Stepping, Patch, Ignore; [0.5, 1, 1, 2] даёт [0.15, 0.30, 0.25, 0.30]);
    - все веса 0 → `Ignore`;
    - распределение на 10 000 бросков совпадает с весами ± 0.02.
  - **Depends**: TSK-009, mod-framework TSK-058 (`ModCredit`)

- [x] **TSK-016**: Поток брака ИИ и новости (авто 2026-10-10)
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawNews.cs`, дополнения `FlawDecisionDispatcher` (ветка ИИ)
  - **Acceptance**:
    - публичный брак ИИ применяет реакцию сразу (деньги через `AICompany.SpendMoney`);
    - `Major` и `Critical` → письмо `ModMail` (`MailPriority.Normal`, `Tag = "dfr:news:<recordId>"`), `Minor` → `ModNotify.ShowKey`;
    - внутренний брак ИИ с тихой реакцией не сообщается до утечки (слухов нет);
    - публичные инциденты ИИ пишутся в `EventLog`;
    - при `duringFastForward` записей и новостей нет; показ в пакетном прогоне откладывает ядро.
  - **Depends**: TSK-013, TSK-015, mod-framework TSK-062 (`ModMail`), TSK-033 (`ModNotify`)

## Фаза 5 — UI и API

- [x] **TSK-017**: Страница «Брак и отзывы» (авто 2026-10-10)
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/UI/FlawJournalPage.cs`
  - **Acceptance**:
    - страница зарегистрирована `ctx.HubPage("dfr.journal.title", build)` и открывается из хаба «Моды»;
    - вкладки «Мои CPU» и «Соперники»;
    - `Hidden` не показывается;
    - кнопка «Профинансировать ревизию» для `Mitigated` вызывает `FundSteppingForMitigated` после подтверждения `ModUI.Confirm`;
    - отладочная кнопка «Принудительный брак» видна только при `debug = true` в отладочной сборке;
    - тема — общая логика `ModUI`.
  - **Depends**: TSK-012, TSK-016, mod-framework TSK-036 (виджеты), TSK-039 (`HubPage`), TSK-064 (`ModUI.Confirm`)

- [x] **TSK-018**: Пометка на карточке CPU (авто 2026-10-10)
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Patches/CpuBadgePatch.cs`
  - **Acceptance**:
    - постфикс `CpuSalesUI.UpdateDailyTexts` (`Optional`) дописывает «Брак: <статус>» цветом `Colors.Instance.DynamicNegativeHex` для известных незакрытых браков игрока;
    - без брака текст карточки не меняется.
  - **Depends**: TSK-008, mod-framework TSK-023 (`ModHooks`)

- [x] **TSK-019**: Подсказка риска в конструкторе (Could) (авто 2026-10-10; переключатель спешки подключён в TSK-029)
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Patches/DesignerRiskHint.cs`
  - **Acceptance**:
    - постфикс `CreatorCpu.UpdateInfoTexts` (`Optional`) показывает «Риск скрытого брака: ~N %», шаг 5 %, пока включён `showRiskHint` (без привязки к сложности);
    - при изменении слайдера частоты значение меняется;
    - `ModRandom` не вызывается;
    - если приватные поля `CreatorCpu` не найдены, подсказка выключается с записью в лог;
    - оценка учитывает переключатель спешки после TSK-029.
  - **Depends**: TSK-004, mod-framework TSK-023 (`ModHooks`)

- [x] **TSK-020**: API `FlawRegistry` для других фич (авто 2026-10-10)
  - **Requirement**: FR-13
  - **Deliverables**: дополнения `FlawRegistry` (`TryReveal`, `ForceFlaw`, `GetKnownFlaws`, `FindByCpu`, `OnFlawStateChanged`), `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawRegistryApiTests.cs`
  - **Acceptance**:
    - `TryReveal` для CPU без брака → `false` без побочных эффектов;
    - `ForceFlaw` создаёт запись и запускает обнаружение;
    - `GetKnownFlaws` не возвращает `Hidden`;
    - при выключенной фиче методы безопасны (`false` или пусто);
    - `FlawRecordView.PublicSinceDay` заполняется при переходе в `Public`;
    - API описан XML-комментариями.
  - **Depends**: TSK-008

- [x] **TSK-021**: Точка расширения для rival-personalities (`SetResponseBias`, данные для осторожности) (авто 2026-10-10)
  - **Requirement**: FR-17
  - **Deliverables**: дополнения `FlawRegistry` (`SetResponseBias`), подключение смещения в `AiFlawPolicy`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawResponseBiasTests.cs`
  - **Acceptance**:
    - `FlawRegistry.SetResponseBias(IFlawResponseBias)` — один слот, `null` снимает; смещение подключается в `AiFlawPolicy` (порядок Recall, Stepping, Patch, Ignore);
    - исключение в `Multipliers` → множители 1.0, ошибка засчитывается вызывающей фиче (`ModHooks.Guard`);
    - `OnFlawStateChanged` при переходе записи ИИ в `Public` передаёт `CompanyKey`, `Severity`, `State`, `PublicSinceDay`;
    - патча `AIBehaviourCreation.AcceptableFailureRate` в фиче нет (решение владельца: осторожность ИИ ведёт `rival-personalities`); описание контракта дублируется в спеке `rival-personalities` (её TSK-020/TSK-021 вызывают `SetResponseBias`).
  - **Depends**: TSK-015, TSK-020; rival-personalities (потребитель, мягкая зависимость)

- [x] **TSK-026**: Корректировщик обзоров прессы (авто 2026-10-10)
  - **Requirement**: FR-18
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Press/FlawPressAdjuster.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawPressAdjusterTests.cs`
  - **Acceptance**:
    - при `ModFeatures.IsActive("press-reviews")` регистрируется `PressReviewsApi.RegisterAdjuster` с `Id = "design-flaw-recalls"`, при выключении — `UnregisterAdjuster`;
    - для известного незакрытого брака `OverallAdjustment` уменьшается на `press.overallAdjustment[тяжесть]` (−8 / −15 / −15), добавляется примечание;
    - `Hidden` и закрытые записи обзор не меняют;
    - статьи фича не публикует (их пишет `press-reviews` по `OnFlawStateChanged`);
    - AC-18.1 на фейке `ReviewDraft`.
  - **Depends**: TSK-020, press-reviews (`PressReviewsApi`, `IReviewAdjuster`)

- [x] **TSK-027**: Передача брака при поглощении CPU (авто 2026-10-10)
  - **Requirement**: FR-19
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawTransferHandler.cs`, дополнения `FlawRegistry` (`MoveOwner`), `FlawChannels` (`RemoveCompanyEntries`), `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawTransferTests.cs`
  - **Acceptance**:
    - в `Register` подписка `ModEvents.CpuTransferred += FlawTransferHandler.OnCpuTransferred(from, to, cpu)`, тело в `ModHooks.Guard`;
    - запись базового CPU получает владельца `ModCompanies.KeyOf(to)`; записи уровня компании (`contract.signChance`) снимаются, записи уровня CPU остаются; вариант игнорируется;
    - `Hidden`, `InternalKnown`, `Stepping`, `Mitigated` сохраняют состояние и сроки, ревизия списывается с нового владельца;
    - `Public` с игнором: дренаж и иск сброшены, `CoverUp` снят, игроку открывается выбор `dfr.decide.public*`, ИИ решает политикой;
    - `CpuReleased` при переносе не вызывается, `rolledCpuIds` не меняется;
    - при обратном порядке событий (банкротство раньше переноса) запись закрывается по FR-4 п. 5 с записью в лог.
  - **Depends**: TSK-008, TSK-014, mod-framework TSK-056 (`ModEvents.CpuTransferred`); rival-acquisitions (источник события, мягкая)

- [x] **TSK-028**: Модель спешки (`RushMath`) и учёт в риске (авто 2026-10-10)
  - **Requirement**: FR-20, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/RushMath.cs` (`ApplyTime`, `ApplyCost`), `mod/tests/PTMod.Tests/DesignFlawRecalls/RushMathTests.cs`
  - **Acceptance**:
    - 400 дней и 80 млн при `timeCut` 0.25 и `costMultiplier` 1.10 → 300 дней и 88 млн; срок не меньше 1 дня;
    - `FlawRiskModel` добавляет `kRush × RS` (AC-2.4: 0.118); для ИИ признак игнорируется;
    - параметры читаются из `flaws.json` (`rush`, `risk.kRush`).
  - **Depends**: TSK-002, TSK-004

- [x] **TSK-029**: Переключатель «Ускоренный проект» в конструкторе (авто 2026-10-10)
  - **Requirement**: FR-20, FR-14
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Patches/RushProjectPatches.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/RushProjectOption.cs`
  - **Acceptance**:
    - патчи `Optional`: postfix `CreatorCpu.Project` (приватный; срок и стоимость через `RushMath`), prefix `CreatorCpu.CreateCpu` (запоминает ожидающий проект `{ имя, плановая дата }`), postfix `ProjectCompletedHandler.AddCpu` (превращает ожидающий проект в `rushCpuIds`, допуск ±3 дня);
    - переключатель `ModUI.Toggle` «Ускоренный проект» (`dfr.rush.toggle`) создаётся рядом с кнопкой создания в панели конструктора, по умолчанию выключен для каждого нового проекта; значения в конструкторе (срок, стоимость, «в месяц», дата завершения) показываются изменёнными;
    - вариант CPU наследует признак базового; ИИ спешку не использует;
    - цели или приватные поля не найдены → переключателя нет, одно предупреждение, остальная фича работает; параметр `rushEnabled = false` скрывает переключатель;
    - после выпуска CPU в логе `roll cpu=<id> p=…` учитывает `kRush`; ожидающие проекты переживают сохранение и загрузку (`rushProjects`).
  - **Depends**: TSK-006, TSK-019, TSK-028, mod-framework TSK-023 (`ModHooks`), TSK-036 (`ModUI.Toggle`)

## Фаза 6 — Локализация, самопроверка, проверка

- [x] **TSK-022**: Строки ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (ключи `dfr.*` из таблицы design, включая `dfr.feature.desc`, `dfr.mail.auto.*`, `dfr.rep.*`, `dfr.mod.*`, `dfr.rush.*`)
  - **Acceptance**:
    - все ключи из design есть в обоих языках и начинаются с `dfr.` (`ModIds.IsL10nKeyAllowed`);
    - тест на полноту ключей фичи зелёный;
    - суммы форматируются `StringFormatter.FloatToMoney`;
    - названия компаний в текстах берутся из игры, в строки не вшиваются.
  - **Depends**: TSK-010, TSK-017, TSK-029, mod-framework TSK-013, TSK-043 (`ModL10n`, `core.json`), TSK-066 (`ModIds`)

- [x] **TSK-023**: Самопроверка и изоляция ошибок (авто 2026-10-10)
  - **Requirement**: NFR-2, NFR-3, NFR-8, FR-15
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/FlawSelfCheck.cs`
  - **Acceptance**:
    - на старте проверяются:
      - цели патчей (все `Optional`): `CpuSalesUI.UpdateDailyTexts`, `CreatorCpu.UpdateInfoTexts`, `CreatorCpu.Project`, `CreatorCpu.CreateCpu`, `ProjectCompletedHandler.AddCpu`; провал отключает только соответствующую возможность (пометка, подсказка, спешка);
      - подписки на `ModEvents.CpuReleased` и `ModEvents.CpuTransferred`;
      - 6 событий `dfr.decide.*`;
      - регистрация 4 типов эффектов;
      - каналы `market.rating.cpu` (с `PerCpu = true`) и `contract.signChance` объявлены;
      - префикс `dfr.` у ключей L10n и ID событий;
    - провал обязательного пункта (события, эффекты, каналы, пак) делает фичу `Unavailable` с записью в лог, игра работает;
    - выключение посреди партии: записи `ModModifiers` не применяются и не пересоздаются, ревизия стоит и после включения продолжается со сдвигом сроков (ручной сценарий 10).
  - **Depends**: TSK-014, TSK-018, TSK-019, TSK-020, TSK-029, mod-framework TSK-042 (`ModSelfCheck`), TSK-066 (`ModIds`)

- [x] **TSK-024**: Тест баланса (Монте-Карло) (авто 2026-10-10)
  - **Requirement**: FR-2, FR-4, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawBalanceSimulationTests.cs`, генератор синтетических дизайнов `mod/tests/PTMod.Tests/DesignFlawRecalls/SyntheticDesigns.cs`
  - **Acceptance**:
    - на 10 000 дизайнов (сложность 2, без спешки): средняя p в [0.08, 0.15];
    - доля `Critical` ≤ 20 %;
    - среднее время до обнаружения `Major` при U = 1 млн — в [6, 18] месяцев;
    - одинаковый сид даёт одинаковый результат (детерминизм).
  - **Depends**: TSK-005, TSK-007

- [ ] **TSK-025**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1–FR-20, NFR-1–NFR-8
  - **Deliverables**: заполненный протокол прогона (дата, версия, сценарии 1–13 из design → Testing, результат, отклонения) в описании PR
  - **Acceptance**:
    - все 13 ручных сценариев пройдены или отклонения заведены задачами (сценарий 12 — после готовности `rival-acquisitions`);
    - в `Player.log` нет ошибок `[PTMod]` уровня Error за 10 игровых лет на скорости 3;
    - сейв грузится без мода и с модом.
  - **Depends**: TSK-022, TSK-023, TSK-024, TSK-026, TSK-027, TSK-029

## Dependency graph

```
Внешние: mod-framework ──> TSK-001 (020, 022, 014), TSK-002 (015, 067), TSK-003 (016, 025, 010), TSK-005 (012),
                           TSK-006 (056, 026), TSK-008 (024), TSK-010 (062), TSK-013 (026), TSK-014 (027, 029, 030, 059),
                           TSK-015 (058), TSK-016 (062, 033), TSK-017 (036, 039, 064), TSK-018/019 (023),
                           TSK-022 (013, 043, 066), TSK-023 (042, 066), TSK-027 (056), TSK-029 (023, 036)
         event-system  ──> TSK-010 (002, 014, 016), TSK-011 (010)
         press-reviews ──> TSK-026 (мягкая)
         rival-personalities ──> TSK-021 (потребитель, мягкая)
         rival-acquisitions ──> TSK-027 (источник события, мягкая)

TSK-001 ──> TSK-002, TSK-003, TSK-014
TSK-002 ──> TSK-004, TSK-007, TSK-009
TSK-004 ──> TSK-005, TSK-019, TSK-028
TSK-003 + TSK-005 ──> TSK-006
TSK-006 + TSK-007 ──> TSK-008
TSK-009 ──> TSK-015
TSK-008 + TSK-009 ──> TSK-010 ──> TSK-011
TSK-008 ──> TSK-018, TSK-020
TSK-020 ──> TSK-026
TSK-015 + TSK-020 ──> TSK-021
TSK-011 + TSK-014 ──> TSK-012
TSK-011 ──> TSK-013
TSK-013 + TSK-015 ──> TSK-016
TSK-012 + TSK-016 ──> TSK-017
TSK-008 + TSK-014 ──> TSK-027
TSK-006 + TSK-019 + TSK-028 ──> TSK-029
TSK-005 + TSK-007 ──> TSK-024
TSK-010 + TSK-017 + TSK-029 ──> TSK-022
TSK-014 + TSK-018 + TSK-019 + TSK-020 + TSK-029 ──> TSK-023
TSK-022 + TSK-023 + TSK-024 + TSK-026 + TSK-027 + TSK-029 ──> TSK-025 (владелец)

Критический путь:
TSK-001 ─> 002 ─> 004 ─> 005 ─> 006 ─> 008 ─> 010 ─> 011 ─> 013 ─> 016 ─> 017 ─> 022 ─> 025
```

Слои (задачи слоя можно делать параллельно):

| Слой | Задачи |
|---|---|
| 0 | TSK-001 |
| 1 | TSK-002, TSK-003, TSK-014 |
| 2 | TSK-004, TSK-007, TSK-009 |
| 3 | TSK-005, TSK-015, TSK-019, TSK-028 |
| 4 | TSK-006, TSK-024 |
| 5 | TSK-008, TSK-029 |
| 6 | TSK-010, TSK-018, TSK-020, TSK-027 |
| 7 | TSK-011, TSK-021, TSK-023, TSK-026 |
| 8 | TSK-012, TSK-013 |
| 9 | TSK-016 |
| 10 | TSK-017 |
| 11 | TSK-022 |
| 12 | TSK-025 |

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи и настройки | Complete (авто 2026-10-10, кусок 1: `DesignFlawRecallsFeature` (`[ModFeature]`, `partial`: `Registry` в `Api/DesignFlawRecallsFeature.Api.cs`; `Id` = `design-flaw-recalls`, `NameKey` = `dfr.feature.name`, `DescriptionKey` = `dfr.feature.desc`, `DependsOn` = `event-system`, `EnabledByDefault` и `SupportsHotToggle` = true), `FlawSettings` + чистая `Logic/FlawParams` (пять `ParamSpec`: `aiFlaws`, `showRiskHint`, `rushEnabled` = true, `flawFrequency` Float 1,0 / 0,5–2,0 / шаг 0,1, `debug` = false; включение фичи — переключатель хаба); `Register` объявляет параметры и участника сейва; `OnDisabled` запоминает `lastActiveDay` (в секции сохранения, `FlawDays.FromDate(ModTicks.Today)`), `ModModifiers.RemoveAll` нигде не вызывается (тест по исходникам), `OnEnabled` читает `flaws.json` и ничего не пересоздаёт (сдвиг сроков ревизии — TSK-008, место отмечено в комментарии); строки `dfr.feature.*`, `dfr.param.*` в ru/en; сборка фичи и тесты зелёные (22 теста `FlawFoundationTests`: параметры, дни, числа перечислений, строки, правила класса фичи по исходнику); видимость в хабе «Моды» и горячее выключение/включение — по коду, проверить в игре) |
| TSK-002 Контент баланса и валидатор | Complete (авто 2026-10-10, кусок 1: `Content/design-flaw-recalls/flaws.json` = пример из design (Data Models; тест сравнивает с design.md, если спека рядом), включая `risk.kRush`, `rush`, `responses.pendingGraceDays`, без `aiCaution`; `Logic/FlawBalance` (DTO со значениями по умолчанию = файлу, тест сравнивает поле за полем; `FlawTypeDef`, `AiPolicyDef`), `Logic/FlawContentValidator` (`Validate(JsonObject)` / `Parse(string)`, `FlawContentReport`): каждый диапазон раздела Security (вероятности и доли 0…1, множители 0…10, дни 1…3650, месяцы 1…120, годы 1971…2030, массивы по тяжести ровно 3, `difficultyMultiplier` 6, веса ИИ — 4 числа 0…1 по именам, `ipsLoss` ≤ 0,5, `kRush` 0…1, `rush.timeCut` 0…0,5, `rush.costMultiplier` 1…2, `pendingGraceDays` 0…365, префикс `dfr.` у `nameKey` через `ModIds.IsL10nKeyAllowed`, отсутствие ключа в таблицах — предупреждение), значение вне диапазона → значение по умолчанию + строка вида `risk.pMax out of range [0,1]: 3; the default 0.5 is used` (AC-15.1), неизвестное поле — предупреждение, битый тип пропускается, без типов — запасной `arith`, битый JSON → значения по умолчанию; сверх design: `pendingSignChanceMultiplier` и `ignore.signChance` ≥ 0,5 (NFR-8, TSK-014), `drainPerMonth` −100…0, репутация −100…100, денежные базы 0…1e9; загрузка `FlawContent` через `ModContent.LoadConfig`, ошибки в `ModLog.Error`; 55 тестов `FlawContentValidatorTests`, включая по тесту на каждое правило) |
| TSK-003 Секция сохранения | Complete (авто 2026-10-10, кусок 1: `FlawSaveSection` (`SectionId` = `design-flaw-recalls`, `SchemaVersion` = 1, регистрация `ctx.SaveParticipant`; логика — `Logic/Save/FlawSaveSection.cs` на логическом `ISideFileParticipant`, второй половиной `partial` в `Save/FlawSaveSection.cs` добавлен `IModSaveParticipant`), `FlawSaveData` + DTO записей/котировки/спешки (без поля версии, без `aiCaution`; файлы в `Logic/Save/`, чтобы их собирали тесты, см. `mod/requests/design-flaw-recalls.md`, п. 4), `FlawSaveCodec` (Bind/From, шаг миграции `Migrate`, проверка значений); круговая сериализация примера из design без потерь, `Write()` без `_schema`; без секции — пустое состояние и все `SaveID` существующих CPU в `rolledCpuIds` (`GameCpuIds` через `DataFinder.FindAllCpus`); схема новее — ядро (проверено на `SideFileModel`) отдаёт умолчания и хранит исходную секцию, фича помечает сейв `SaveLocked` (точное определение — запрос к ядру, п. 5; проверять `SaveLocked` должны TSK-006/008 и далее); недопустимые enum, отрицательные дни, NaN/бесконечность, неверный ключ компании, поле неверного типа → запись `Closed`; 55 тестов `FlawSaveSectionTests`) |
| TSK-004 Модель вероятности брака | Complete (авто 2026-10-10, кусок 2: `Logic/FlawRiskModel.cs` — `FlawRiskInput`, `FlawRiskFactors`, `FlawRiskModel.Factors` и `Probability` (плюс `DifficultyMultiplier`, `EraMultiplier`); пять строк таблицы design (0,070 / 0,118 со спешкой / 0,233 / 0,167 / 0,488) совпадают с точностью 0,001, `pMax` (также не выше 1), `unknownNodeImmaturity` при `HasNodeRecord = false` (при записи с прогрессом 0 `NI` = 1), множители сложности 0 и 5, `flawFrequency` 2,0 удваивает p до потолка, спешка игнорируется для ИИ, `aiRiskMultiplier`, безопасная температура ограничена 95; битые числа (NaN, бесконечность, нулевой потолок частоты) считаются «нет эффекта», непригодная настройка `flawFrequency` — 1; 19 тестов `FlawRiskModelTests`) |
| TSK-005 Выбор тяжести и типа | Complete (авто 2026-10-10, кусок 2: `FlawRiskModel.SeverityWeights`, `PickSeverity`, `TypeWeight`, `PickType` на `IModRng` ядра (собственного интерфейса случайности нет — тест проверяет исходник); при p = 0,233 веса [0,60; 0,44; 0,19], на 100 000 бросков `Xoshiro128` с сидом доли [0,49; 0,36; 0,15] ± 0,01; `security` не выпадает для 2010 года; вес `stability` при OC = 0,4 равен 9; без доступных типов — `arith` из списка или встроенный, без броска; `tests/.../FakeRng.cs` — детерминированный `IModRng` со сценарием чисел и счётчиком бросков; 18 тестов `FlawPickTests`) |
| TSK-006 Бросок при выпуске (ModEvents.CpuReleased) | Complete (авто 2026-10-10, кусок 2: чистое правило — `Logic/FlawRollEngine.cs` (защиты по порядку: перемотка новой игры, сейв заблокирован «новее мода», `rolledCpuIds`; вариант привязывается к записи базового CPU и сам не бросает, без открытой записи базового записи не создаёт; ИИ — только при `aiFlaws`; бросок, тяжесть, тип, запись `Hidden`; строка лога `roll cpu=<id> p=<p> result=<bool>`), `Logic/FlawStore.cs` + `Logic/FlawRecord.cs` (записи и отметки `rolled`/`rush` поверх `FlawSaveData`, ничего не копируется); игровая сторона — `FlawRoller.cs` (подписка `ModEvents.CpuReleased += _roller.OnCpuReleased` в `Register`, тело в `ModHooks.Guard`, контекст `SaveID` в исключении, компания записи — `ModCompanies.KeyOf`), `Game/GameCpuView.cs` (читает `FailureRate`, частоту против потолка владельца с исследованиями, температуру против безопасной, прогресс производителя и наличие `NodeOptimization`, год, сложность), `FlawRegistry.cs` (внутренняя половина partial-класса: `Create`, `AttachVariant`, `WasRolled`, `MarkRolled`, `IsRush`, `MoveOwner`, `Active`); патчей на `AddCpu` нет; потоки случайности `design-flaw-recalls/roll` и `/monitor`; 23 теста `FlawRollerTests` (фейковое событие; загрузку сейва отсекает настоящее правило ядра `ReleaseDeduper`) и 18 `FlawStoreTests`; в игре не проверено) |
| TSK-007 Модель обнаружения | Complete (авто 2026-10-10, кусок 2: `Logic/DiscoveryModel.cs` — `MonthlyHazard` (девять значений таблицы `h` с точностью 0,001; потолок `hMax`), `IsForced` (≥ `maxHiddenMonths` и продано > 0), `IsExpired` (снят дольше `expireAfterRetiredMonths` либо ни одной продажи за окно `expireNoSalesMonths` при том, что CPU на рынке не меньше окна), `PickSource` (`qInternal`), `LeakRate` (игнор, тихая ревизия, хвост; остальное 0). Отступление от сигнатуры design: в `IsExpired` последний аргумент — продажи за ОКНО «нет продаж», а не за всё время (так правило честнее); 16 тестов `DiscoveryModelTests`) |
| TSK-008 Месячный и дневной монитор | Complete (авто 2026-10-10, кусок 2: `Logic/FlawStateMachine.cs` (таблица переходов = диаграмма design плюс закрытие из любого не итогового состояния (FR-4 п. 5) и истечение игнорированного известного брака (FR-9 п. 6); операции `Discover`, `Expire`, `Close`, `Leak`, `Recall`, `BeginStepping`, `CompleteStepping`, `BeginMitigation`, `ExpireMitigation`, `Ignore`, `Move` отвергают недопустимое, не трогая запись), `Logic/FlawCycle.cs` (чистый цикл: `OnMonth` по порядку `Id` — закрытие при смерти владельца или потере CPU, истечение, принудительное обнаружение, бросок `h`, источник, сохранение котировки и `unitsAtDiscovery`, утечка игнора, тихой ревизии и хвоста, дренаж и иск публичного игнора; `OnDay` — оплата ревизии за пропущенные дни по `lastChargedDay`, завершение ревизии и заплатки по датам окончания; `OnResume` — сдвиг `steppingEndDay` и `lastChargedDay` на `today − lastActiveDay − 1`, заплатка не сдвигается, повторный вызов ничего не двигает; `lastActiveDay` обновляется в конце `OnDay`; ошибка записи не останавливает остальные, 3 подряд закрывают запись; `Reveal` — вход для API), `FlawMonitor.cs` (игровая сторона: `OnMonth`/`OnDay` через `ctx.OnTick(TickPhase.MonthPassed / DayPassed)`, без `IsBatchRun` и без патча `NextMonth`; один обход компаний на проход; подписка на `BankruptcyHandler.OnCompanyWentBankrupt` на каждой сцене из `ModTicks.GameReady`; `OnResume` из `OnEnabled` и `GameReady(Loaded)`; ежедневное списание `SpendMoney(…, Development)`; `OnFlawStateChanged` через `FlawViews`), `U` из `MarketData.Sold` + `SoldByContracts` (`GameCpuView.ReadFacts`) кэшируется в записи раз в месяц (`CachedUnits`, не сохраняется); «месяцев с момента снятия» оцениваются числом закрытых месяцев подряд без продаж у снятого с рынка CPU (даты снятия игра не хранит). Часть реакций оставлена местами в `FlawMonitor` с пометками TSK-010/011/012/013/014/015: показ окна решения и выбор ИИ, базовая репутация и удар по популярности, полная стоимость отзыва при утечке, остаточные претензии, дренаж репутации, списание иска, снятие записей `ModModifiers` закрытой записи; 49 тестов `FlawCycleTests`, 12 `FlawPauseTests`, 25 `FlawStateMachineTests`, 11 `FlawDaysTests`; в игре не проверено) |
| TSK-009 Калькулятор котировок | Complete (авто 2026-10-10, кусок 2: `Logic/ResponseCostCalculator.cs` + `Logic/FlawQuote.cs` — `QuoteInput`, `FlawQuote` (поля один в один с `FlawQuoteDto`, `ToDto`/`FromDto`), `Quote`; пример 1994 года: `C_recall` = 66 000 000 ровно, `C_step` ≈ 21 428 571 за 90 дней, `C_patch` ≈ 4 071 429, иск 24 000 000, остаточные претензии 18 000 000; потолок отзыва 30 % выручки за 12 месяцев ставит `RecallCapped` (без известной выручки потолок не применяется, чтобы отзыв не стал бесплатным); нулевая цена → наибольшая цена варианта (`QuoteInput.MaxVariantPrice`); `PatchAvailable = false` при `patchFromYear` > текущего года (цена заплатки всё равно считается, как в примере design); инфляция: фиксированные части умножаются на текущую, стоимость разработки приводится к ценам дня; деньги округляются до целых, коэффициенты берутся как десятичные числа (`0,15f` = 0,15), поэтому сумма под кнопкой совпадает с примером; `QuoteInput.Quiet` выбирает репутацию тихой ревизии; котировка неизменна после сериализации через файл сохранения; 25 тестов `ResponseCostCalculatorTests`) |
| TSK-010 События выбора и диспетчер | Complete (авто 2026-10-10, кусок 3: `Content/design-flaw-recalls/events.json` (шесть событий `dfr.decide.public|internal|leaked` и `.nopatch`: `Manual`, цель `Specific player`, `important`, `timerDays` 30, `defaultChoice` и набор кнопок по design: у `.nopatch` нет «Заплатки», у `leaked*` нет «Игнорировать», `defaultChoice` = заплатка либо игнор, после утечки заплатка либо ревизия) и `config.json` (категория `design-flaw`); оба файла проверены реальными `DefinitionMapper`/`ContentValidator`/`PackCatalog` event-system через временную сборку (6 событий, ни одного замечания). Чистая логика: `Logic/FlawDecisionPlanner.cs` (id событий, выбор события по ситуации `FlawDecisionKind` (публичный / внутренний / утечка) и по доступности заплатки, выбор по умолчанию, 11 позиционных аргументов `Args` по таблице design, чтение `recordId` из `Args[0]`, `IFlawFormat`), `Logic/FlawDecisionEngine.cs` (`FlawDecisionQueue` поверх `FlawSaveData.pendingDecisionQueue`; `FlawDecisionEngine.Dispatch`: игрок получает `EventBus.Fire`, ИИ — `IFlawDecisionHost.DispatchAi`; одно окно за раз — открытое окно это запись игрока, которая ждёт ответа и не в очереди, второе решение встаёт в очередь; `OnChoiceResolved` снимает запись ожидания, запоминает `automaticChoice` и открывает следующее; если эффект не применил выбор, выбор применяется повторно, если и это не вышло — запись возвращается в конец очереди; `Pump` после загрузки; `Sweep` раз в месяц (`FlawMonitor.OnMonth`): окно, которое открыто дольше `timerDays` + 30 дней удержания движком + `pendingGraceDays` (74 по умолчанию), считается потерянным (движок мог сбросить отложенное срабатывание) и получает выбор по умолчанию с письмом, очередь идёт дальше; результат `Fire` `Disabled`/`UnknownEvent`/`Skipped` или исключение → применяется `defaultChoice` и уходит письмо; запись `contract.signChance` × `pendingSignChanceMultiplier` (0,8) со сроком `timerDays + pendingGraceDays` ставится для публичного брака (и обновляется, когда окно открывается из очереди), снимается при решении). Игровая сторона: `FlawDecisionDispatcher.cs` (`EventBus.Fire` с `CompanyKey`, `Args`, `IgnoreConditions`; письмо `ModMail.Send` — `Normal`, тег `dfr:auto:<recordId>`, ключи `dfr.mail.auto.*`, id письма в записи не хранится; охрана `ModHooks.Guard` на каждом входе), `FlawText.cs` (деньги через `StringFormatter.FloatToMoney`, названия серьёзности, типа и CPU), регистрация пака и `EventBus.OnChoiceResolved` в `Register`, `Pump` в `GameReady`, `FlawMonitor.Discovered/Leaked/StateChanged` отдают брак диспетчеру; ветка ИИ — место для TSK-015/016; строки `dfr.event.*`, `dfr.choice.*`, `dfr.mail.auto.*`, `dfr.severity.*`; 51 тест `FlawDecisionTests`; игровые классы (`FlawDecisionDispatcher`, `FlawResponseApplier`, шлюз `FlawChannels`, обработчики эффектов) собираются, но без игры не исполнялись, а `EventEngine` event-system пока без фабрики экземпляров, так что окно в игре заработает после неё) |
| TSK-011 Эффекты «Отзыв» и «Ревизия» | Complete (авто 2026-10-10, кусок 3: `Effects/FlawEffectBase.cs` (`IEventEffectHandler`: `Validate` без параметров, `Resolve` берёт `recordId` из `Args[0]` экземпляра и числа из сохранённой котировки (value = минус сумма, `curve` = id записи и числа для строки), `Apply` вызывает `FlawResponseApplier.Apply` (исключение учитывается и пробрасывается — движок пометит эффект `Failed`, запись останется в ожидании), `Describe` из сохранённых чисел), `FlawRecallEffect.cs`, `FlawSteppingEffect.cs` (плюс `FlawSoftwarePatchEffect.cs`, `FlawIgnoreEffect.cs`), все четыре зарегистрированы `EventEffects.Register` в `Register`. Чистое правило — `Logic/FlawEffectModel.cs` (`FlawEffectTypes`, `FlawEffectNumbers`: числа и строки `dfr.fx.*`) и `Logic/FlawResponseEngine.cs` за `IFlawResponseHost`; игровая сторона — `FlawResponseApplier.cs` (`SpendMoney` с `ExpenseOrigin.Fines`/`Development`, `ModReputation.Add`, `Popularities`, `ContractManager.RemoveContract`, оповещение `OnFlawStateChanged`). Отзыв: ровно `RecallCost` с `Fines` до смены состояния (сбой оплаты оставляет запись в ожидании), `Resolved`, репутация и удар по популярности, контракты не рвутся. Ревизия: деньги не списываются сразу, ежедневное списание делает монитор (`Development`), `SteppingDays` дней, публичная ревизия в конце списывает `ResidualClaimsCost`, тихая оставляет хвост утечки. Утечка тихой ревизии: `Public` + `CoverUp`, полный `RecallCost` с `Fines`, репутация (база + 0) × 2 = −10 для Major. Второй вызов для не ожидающей записи ничего не делает (предупреждение в лог). `FlawCycle.Leak` теперь пересчитывает котировку по сегодняшним фактам (`unitsAtDiscovery` не трогает). Общие дела с TSK-010: `FlawMonitor` передаёт утечку, окончание ревизии и заплатки, дренаж и иск в `FlawResponseApplier`; 23 теста `FlawEffectHandlersTests`) |
| TSK-012 Эффекты «Заплатка» и «Игнор» | Complete (авто 2026-10-10, кусок 3: `FlawResponseEngine` (Patch, Ignore, `ExpireMitigation`, `FundSteppingForMitigated`, `IgnoreDrain`, `ClassActionFiled`) и `Effects/FlawSoftwarePatchEffect.cs`, `Effects/FlawIgnoreEffect.cs`. Заплатка: `PatchCost` с `Development`, `Mitigated`, по записи `market.rating.cpu` (`ModifierScope.Cpu`, причина `dfr.mod.patch` с названием CPU, множитель `1 − ipsLoss`, срок до `mitigationEndDay`) на каждый CPU записи (база и варианты), идентификаторы в `modifierEntryIds`; заплатка недоступна (тип/год) — отказ без списаний; срок истёк — `Resolved`, ключи забыты; оплаченная ревизия оставляет заплатку на время работы и снимает записи в момент завершения (`ModModifiers.Remove`, FR-8 п. 5, FR-7 п. 6); тихая заплатка + ревизия = тихая ревизия. Вклад заплатки на всех типах и тяжестях при контенте по умолчанию в [0,75; 1,00] и внутри диапазона ядра [0,5; 2,0] (тест читает `core.json`), книга каналов сама не пускает множитель ниже 0,5 и выше 1. Игнор публичного брака: репутация, популярность, `contract.signChance` × `signChance[sev]` на 12 месяцев (`dfr.mod.ignore`), разрыв контрактов с вероятностью `oemCancel` (`ContractHelper.ContractsWithCpu` + `ContractManager.RemoveContract`, без штрафа), дренаж −1 в месяц до 12 месяцев (`dfr.rep.drain`) и не больше одного иска (`ClassActionCost`, `Fines`); игнор внутреннего брака — никаких последствий до утечки. Утечка игнорированного внутреннего брака: последствия игнора, репутация (−5 + −8) × 2 = −26 для Major, `KindOf` = `Leaked`, окно `dfr.decide.leaked*` без кнопки игнора; 21 тест `FlawMitigationTests`) |
| TSK-013 Репутация и популярность | Complete (авто 2026-10-10, кусок 3: `Logic/FlawReputationTable.cs` (таблица design п. 7: публичность −2/−5/−10, отзыв +1/+2/+4, публичная ревизия 0/−1/−3, тихая 0, заплатка −1/−2/−4, игнор −3/−8/−15, дренаж −1; `CoverUp` = (база + реакция) × `coverUpMultiplier`, округление от нуля, множитель < 1 считается 1; `FlawReputationKeys` — семь ключей `dfr.rep.*`), `Logic/FlawPopularity.cs` (`(1 − hit)` для каждого элемента `Popularities`, не ниже 0, `HitOf` по таблице `popHit`); игровая сторона — `FlawResponseApplier.ApplyReputation` (`ModReputation.Add(owner, delta, ключ)`, id записи в `ModLog.Info`, не в причине) и `ApplyPopularityHit` (все CPU записи, индекс списка = `(int)Segment`). Базовая репутация и удар по популярности публичного брака — `OnDiscovered` (при обнаружении, любого владельца); отзыв внутреннего брака по таблице даёт только +отзыв (база не добавляется — отступление в notes); 20 тестов `ReputationTableTests`, включая AC-10.1 (−5 и +2 = −3) и AC-10.2) |
| TSK-014 Записи каналов | Complete (авто 2026-10-10, кусок 3: `Logic/FlawChannelBook.cs` (чистая логика за `IFlawModifierGateway` — `Add`, `Remove`, `LiveEntries`, `CpuName`; `RemoveAll` в интерфейсе нет намеренно): `AddCpuRating` (`ModifierScope.Cpu`, срок в днях не меньше 1, причина `dfr.mod.patch`, аргумент — название CPU), `AddSignChance` (`ModifierScope.Company`, причина `dfr.mod.pending` или `dfr.mod.ignore`), множитель держится в [0,5; 1], id `m-…` в `modifierEntryIds`, `Remove`, `RemoveAllOfRecord`, `RemoveCompanyEntries` (только `contract.signChance`, заплатка CPU остаётся), `RemovePending`, `RemoveRatings`, `Reconcile(today, ожидает ли запись)`: id, которых нет в ядре, забываются (срок вышел) или попадают в предупреждение (заплатка должна идти, а записей нет), устаревшая запись ожидания и запись ожидания без записи брака снимаются, чужие причины не трогаются; `FlawChannels.cs` — игровой шлюз к `ModModifiers.Add/Remove/Explain` и `Reconcile()` после `GameReady(Loaded)`; при выключении фичи ничего не снимается, при включении ничего не создаётся (тест на фейке: срок окончания прежний, `AddCount` не меняется; плюс проверка исходников на `ModModifiers.RemoveAll`); 29 тестов `FlawChannelsTests`) |
| TSK-015 Политика выбора ИИ | Complete (авто 2026-10-10, кусок 4: `Logic/AiFlawPolicy.cs` — `AiDecisionInput` (`IsPublic`, `PatchAvailable`, `CashHeadroom`, `Quote`, `BiasMultipliers`, плюс `IgnoreClosed` для утёкшего брака) и `AiFlawPolicy` (`CashHeadroom(money, threshold)` = деньги минус порог дефолта, `Weights`, `Probabilities`, `Choose`, `Fallback`). Порядок как в формуле 8 design: веса `aiPolicy.public|internal` из контента, без заплатки её вес 0, отзыв отсекается при `CashHeadroom` < `recallRequiresHeadroomFactor` · `RecallCost` (ровно 1,5 · стоимость ещё проходит; NaN — не хватает), затем множители `IFlawResponseBias` в порядке Recall, Stepping, Patch, Ignore ([0,5; 1; 1; 2] на базовых [0,30; 0,30; 0,25; 0,15] даёт [0,15; 0,30; 0,25; 0,30]; null, неверная длина, отрицательное, NaN и бесконечность — все 1,0; отсечённая реакция множителем не возвращается), один `IModRng.WeightedIndex`; все веса 0 → `Ignore` (после утечки, где игнор закрыт, — `Stepping`, как у окна игрока), без броска. Распределение на 10 000 бросков `Xoshiro128` совпадает с весами ± 0,02 (публичный, внутренний, без отзыва, со смещением, без заплатки). Порог дефолта подставляется фейком (`FakeAiHost.Threshold`), игровой хозяин берёт его только из `ModCredit.ThresholdOf(owner)`; `CentralBank.DebtLimit` не читается (тест по исходникам). `IFlawResponseBias.cs` остаётся в `Api/` (публичный контракт, сигнатура прежняя); смещение читается через `FlawRegistry.Shared.GetResponseBiasMultipliers` (под `ModHooks.Guard` фичи-владельца, бесплатно для выключенной `rival-personalities`). 28 тестов `AiFlawPolicyTests`)
| TSK-016 Поток брака ИИ и новости | Complete (авто 2026-10-10, кусок 4: `Logic/FlawAiEngine.cs` (`IFlawAiHost`, `FlawAiEngine.Decide`: запись ждёт ответа и не игрока → `AiFlawPolicy.Choose` → `FlawResponseEngine.Apply(…, automatic: true)` с теми же правилами, что у игрока: деньги через `ICompany.SpendMoney` (`AICompany.SpendMoney` у соперника) со статьями `Fines`/`Development`, репутация, популярность, записи каналов, разрыв контрактов; исключение политики или оплаты не выходит наружу, запись продолжает ждать), `Logic/FlawNewsPlanner.cs` (`FlawNewsKind`, `FlawNewsPlan`, `FlawNewsPlanner.Plan`: публичный брак ИИ — новость при любой реакции, `Major`/`Critical` → письмо `ModMail` `Normal` с тегом `dfr:news:<recordId>` (для утёкшего — тексты `dfr.news.leaked.*`), `Minor` → `ModNotify.ShowKey("dfr.news.rival.title")`, каждый публичный инцидент — строка в `EventLog`; внутренний брак с тихой реакцией (ревизия, заплатка, игнор) не сообщается до утечки, слухов нет; отзыв внутреннего брака — новость, отзыв делается открыто; при `duringFastForward` ни новостей, ни строк журнала; брак игрока новостью не является), `FlawNews.cs` (игровая сторона: `ModMail.Send` / `ModNotify.ShowKey` / `EventEffects.ApplyDirect`, каждая часть под своим `ModHooks.Guard`; тексты: компания, CPU, тип, тяжесть, реакция) и `Effects/FlawNewsEffect.cs` (пятый тип эффекта `FlawNews`, ничего не меняет в игре: это строка журнала событий; не входит в `FlawEffectTypes.All`, `events.json` его не знает). `FlawDecisionDispatcher` — ветка ИИ: реализует `IFlawAiHost`, `DispatchAi` → `FlawAiEngine.Decide`, запас денег = `MoneyAmount − ModCredit.ThresholdOf(owner)`, смещение, поток `design-flaw-recalls/ai`, перемотка `ModTicks.IsFastForwarding`. Месячный проход (`FlawDecisionEngine.Sweep` → `RetryAi`) повторно предлагает политике ИИ запись, которая осталась без ответа. Строки ru/en: `dfr.news.*` (9 ключей), `dfr.fx.news`. 45 тестов `FlawAiNewsTests`. Не проверено в игре: показ строки журнала для `Direct`-экземпляра `dfr.news.ai` окном журнала `event-system`, реальная доставка письма и уведомления)
| TSK-017 Страница «Брак и отзывы» | Complete (авто 2026-10-10, кусок 5: `Logic/FlawJournalModel.cs` (`FlawJournalTab`, `FlawJournalRow`, `Rows`, `RowOf`, `StatusKey`: «Мои CPU» — известные записи игрока вместе с закрытыми (не больше 20 закрытых, сначала открытые, новые выше); «Соперники» — только публичное (`publicSinceDay > 0` или отзыв), тайный брак ИИ не виден; `Hidden` не показывается нигде; для `Mitigated` игрока с котировкой — данные кнопки: цена и срок ревизии из сохранённой котировки), `UI/FlawJournalPage.cs` + `UI/FlawUi.cs` (страница хаба `ctx.HubPage("dfr.journal.title", FlawJournalPage.Build)`: две вкладки `ModUI.Button`, список в `ModUI.ScrollView`, строка = `ModUI.Info` («Заплатка, −12 %, 200 дн.», AC-12.1) + строка с типом, тяжестью и затратами; кнопка «Профинансировать ревизию — {сумма}, {срок} дн.» спрашивает `ModUI.Confirm` и только по «Да» вызывает `FlawResponseApplier.FundSteppingForMitigated`, потом перерисовывает список; блок «Принудительный брак» (список CPU игрока, тяжесть, две кнопки → `FlawRegistry.ForceFlaw`) компилируется только при `PTMOD_DEBUG` и показывается только при `debug = true`; тема — виджеты `ModUI`); строки `dfr.journal.*` в ru/en; тесты `FlawJournalTests`: выбор записей по вкладкам, скрытая запись, потолок закрытых, AC-12.1, кнопка только у заплатки игрока, тексты статусов в двух языках, подтверждение перед оплатой и отладочный блок по исходнику; в игре не запускалось — расположение и перерисовку проверить в игре, см. `mod/requests/design-flaw-recalls.md` п. 9) |
| TSK-018 Пометка на карточке CPU | Complete (авто 2026-10-10, кусок 5: `Logic/FlawBadgeModel.cs` (`StateKeyOf`: только известный незакрытый брак игрока; слова `waiting`/`ignored`/`stepping`/`patched`) и `Patches/CpuBadgePatch.cs` (postfix `CpuSalesUI.UpdateDailyTexts`, `Optional`, приоритет 200, поля `cpu` и `popularityText` по инъекции Harmony): дописывает `<color=Colors.Instance.DynamicNegativeHex()>[Брак: статус]</color>` к тексту популярности; без брака текст не трогается (проверка выхода стоит раньше записи); в имя CPU пометка не пишется — `CpuSalesUI.FixedUpdate` возвращает `cpu.Name` на каждом шаге; `RefreshCards` перерисовывает карточки на экране при смене состояния брака игрока; строки `dfr.badge*`; тесты `FlawBadgeAndHintTests` (модель и правила патча по исходнику); в игре не запускалось, см. `mod/requests/design-flaw-recalls.md` п. 9.4) |
| TSK-019 Подсказка риска в конструкторе (Could) | Complete (авто 2026-10-10, кусок 5; переключатель спешки подключён в куске 6: `Logic/FlawRiskHint.cs` (`Probability` — та же `FlawRiskModel.Probability`, что у броска, с `flawFrequency` и флагом спешки, множитель ИИ никогда; `Percent` — округление до 5 %, `IsBelowStep` — «меньше 5 %») и `Patches/DesignerRiskHint.cs` (postfix `CreatorCpu.UpdateInfoTexts`, `Optional`; строка `ModUI.Info` «Риск скрытого брака» / «~N %» под строкой процента брака, не чаще 10 раз в секунду, пишется только при смене текста; показывается, пока включён `showRiskHint`, независимо от сложности; приватные поля `failureRate`, `frequency`, `maxFrequency`, `temperature`, `currentPackage`, `currentLithography`, `nodeManufacturer`, `failureRateText` (и необязательное `projectTime`) ищутся через `AccessTools`, не найдены — подсказка выключается на сессию с `ModLog.Warn`; `ModRandom` нигде не вызывается; AC-14.1 проверен на модели). **Учёт переключателя спешки** (кусок 6, TSK-029): `DesignFlawRecallsFeature.Register` ставит `DesignerRiskHint.RushSource = Rush.IsRushOn`, оценка добавляет `kRush`, пока переключатель «Ускоренный проект» включён (тест `RushProjectTests`), и считается для обычного проекта, когда его нет или он скрыт; тесты `FlawBadgeAndHintTests`; в игре не запускалось, см. `mod/requests/design-flaw-recalls.md` п. 9.4) |
| TSK-020 API FlawRegistry | Complete (авто 2026-10-10, кусок 5: `Logic/FlawApiEngine.cs` (`FlawApiEngine`, `IFlawApiHost`, `FlawCpuInfo`) — `TryReveal` (CPU без брака, известный брак, неверный источник, нет владельца или CPU, заблокированный сейв → `false` без побочных эффектов; иначе `FlawCycle.Reveal`), `ForceFlaw` (создаёт запись с вариантами, помечает CPU как прошедший бросок и запускает обнаружение — у игрока откроется окно решения, у ИИ выберет политика; скрытой записи только повышает тяжесть; `false` для неизвестного CPU, невыпущенного, снятого с рынка, без владельца или с банкротом, неизвестного типа, ИИ при `aiFlaws` выкл., уже известного или закрытого брака), `Known`/`AllKnown`/`FindByCpu` (никогда не `Hidden`); `Logic/FlawViews.cs` (снимки, `PublicSinceDay` заполняет автомат при первом переходе в `Public`); `FlawApiBackend.cs` — `IFlawRegistryBackend` для фасада `Api/FlawRegistry.cs`, присоединяется в `OnEnabled`, снимается в `OnDisabled`; при выключенной фиче фасад отвечает пустым объектом (`false` / пусто / `null`) и не бросает, все члены с XML-комментариями (проверено по исходнику); тесты `FlawRegistryApiTests`, зеркало `Logic/ApiViewMirror.cs` для сборки тестов) |
| TSK-021 Точка расширения для rival-personalities | Complete (авто 2026-10-10, кусок 5: `SetResponseBias` (один слот, `null` снимает, замена другого смещения — предупреждение, слот хранится и при выключенной фиче) и `GetResponseBiasMultipliers` (вызов `Multipliers` в `ModHooks.Guard` фичи-владельца класса смещения — сбой засчитывается ей, ответ `null` = все 1.0; смещение фичи, которая выключена, не применяется; неверная длина, отрицательные, NaN, бесконечности → все 1.0) были в `Api/FlawRegistry.cs`, `FlawDecisionDispatcher` и `AiFlawPolicy` с кусков 1 и 4; в этом куске их контракт закреплён тестами `FlawResponseBiasTests` (AC-17.1: [0.5, 1, 1, 2] → [0.15, 0.30, 0.25, 0.30], порядок Recall/Stepping/Patch/Ignore, отсечённая реакция не возвращается множителем, правила слота и охраны по исходнику реестра) и данными для осторожности: `OnFlawStateChanged` идёт со снимком с `CompanyKey`, `Severity`, `State`, `PublicSinceDay` (AC-17.2, тест на записи ИИ); патча `AIBehaviourCreation.AcceptableFailureRate` нет (тест: слова нет в коде фичи)) |
| TSK-022 Строки ru/en | Complete (авто 2026-10-10, кусок 7: таблицы `L10n/ru/design-flaw-recalls.json` и `L10n/en/design-flaw-recalls.json` (112 → 113 ключей на язык; в этом проекте строки фич лежат в таблицах по фичам, а не в общих `ru.json`/`en.json`). Все 44 ключа таблицы design («Ключи ModL10n») есть в обоих языках; не хватало `dfr.news.rival.body` — добавлен, а `dfr.news.rival.title` приведён к тексту design («{0}: дефект в {1}»), остальное перенесено во вторую строку уведомления о незначительном браке соперника: `FlawNews.Notify` теперь `ModNotify.Show(заголовок, вторая строка)`, `FlawNewsPlan.NotifyBodyKey`. Тесты `FlawStringsTests` (17): ключи таблицы design (список в тесте сверяется с самой таблицей design.md, когда спека рядом); все ключи начинаются с `dfr.` и проходят `ModIds.IsL10nKeyAllowed` (с настоящей таблицей префиксов `core.json`), ключи кода и id событий/теги писем — тоже; одинаковые ключи и `{n}` в ru/en, нет пустых текстов и пробелов по краям; каждый текст форматируется с 11 аргументами окна решения, а семейства (`dfr.event.*` 11, `dfr.news.*` и `dfr.fx.news` 5, `dfr.fx.*` 3, `dfr.mail.auto.*` 3, `dfr.rep.*`/`choice`/`severity`/`type` 0) используют только аргументы, которые передаёт их вызывающий; в русских текстах нет существительных во множественном числе после числа (только «дн.», «мес.»); ключи, которые код строит из префикса (`dfr.severity.N`, `dfr.choice.*`, `dfr.news.reaction.*`, `dfr.badge.state.*`, `dfr.rep.*`, причины каналов, `dfr.fx.*`), существуют для каждого значения, а у каждого префикса в коде есть ключи; события пака и названия типов `flaws.json` используют только имеющиеся ключи; суммы пишет только `StringFormatter.FloatToMoney` (одно место, `FlawText.cs`; нет знака валюты в литералах и своих числовых форматов, кроме групп разрядов у количества единиц) и все суммы в окне, эффектах, письме и журнале идут через `IFlawFormat.Money`; в строках нет названий компаний (список реальных названий), название компании в новостях берётся из `ICompany.Name`, а CPU — из игры по `SaveID`)
| TSK-023 Самопроверка и изоляция ошибок | Complete (авто 2026-10-10, кусок 7: `Logic/FlawSelfCheckRules.cs` (чистые правила: `FlawSelfCheckRules.Run(FlawSelfCheckInput)` → `FlawSelfCheckReport`) и игровая сторона `FlawSelfCheck.cs`. Ядро (`ModSelfCheck`) не даёт фиче добавить находки, поэтому фича пишет в `Player.log` сама (`self-check: OK` либо `self-check: N error(s), M warning(s)` и строка на находку; запрос к ядру — `mod/requests/design-flaw-recalls.md`, п. 11.2). Проверяется: цели патчей из `ModHooks.Report` (пять из design и `CreatorCpu.UpdateUI`; все `Optional`; промах выключает только свою возможность — пометку, подсказку или переключатель спешки — одной строкой `[abilities]` со всеми причинами, фича идёт дальше); приватные поля `CpuSalesUI` и `CreatorCpu` по имени (`AccessTools`; `projectTime` у подсказки необязательное); подписки на `ModEvents.CpuReleased` и `CpuTransferred` и что ядро поднимает событие выпуска; шесть событий `dfr.decide.*` в `EventRegistry.All`; четыре типа эффектов в `EventEffects.RegisteredTypes` (пятый — предупреждение); каналы `market.rating.cpu` (`PerCpu = true`) и `contract.signChance` объявлены; префикс `dfr.` у id событий и ключей (`ModIds`) и существование ключей; сколько значений `flaws.json` отвергнуто. Провал обязательного пункта (события/пак, эффекты, каналы) → `ModLog.Error` с причиной и `ModFeatures.MarkUnavailable`, игра работает; нет подписки или события выпуска — `Error` в логе без `Unavailable`. Запуск: конец `OnEnabled` и, если на старте определений событий ещё не было, первый `GameReady`. Изоляция: `FlawMonitor.OnMonth`/`OnDay` теперь под `ModHooks.Guard` (цикл и `Sweep` по отдельности); тесты по исходникам проверяют, что тело каждого из семи патчей и каждого подписанного обработчика под охраной (`ModHooks.Guard`/`CanRun`+`ReportFault`), эффекты ловят свои исключения, запись с исключением пропускается, три подряд закрывают её. Выключение посреди партии (ручной сценарий 10) проверено без игры на чистых движках: идущие ревизия и заплатка, 10 дней без тиков фичи — ни списаний, ни `Remove`; при включении срок ревизии сдвинут на `today − lastActiveDay − 1`, срок заплатки и её запись в ядре прежние, новых `Add` нет, ревизия оплачена ровно на `C_step`. Тесты `FlawSelfCheckTests` (39); в игре не запускалось, см. `mod/requests/design-flaw-recalls.md`, п. 11.5)
| TSK-024 Тест баланса | Complete (авто 2026-10-10, кусок 7: `FlawBalanceSimulationTests` (19 тестов) и генератор `SyntheticDesigns.cs` (в `mod/tests/PTMod.Tests/Features/DesignFlawRecalls/`, см. `mod/requests/design-flaw-recalls.md`, п. 4). Расчёт идёт по поставляемому `flaws.json` (его читает валидатор, тест сверяет с умолчаниями кода). Популяция «типичных дизайнов» — допущения записаны в `SyntheticPopulation`: кампания 1980–2010, сложность 2, без спешки и без ИИ; FR растёт с 0,18 (1980) до 0,58 (2018) ±0,08; 15 % разогнано на 5–40 %, 8 % перегрето до +15 %, 80 % на изученном техпроцессе. Результаты на 10 000 дизайнов: средняя p **0,133** (в [0,08; 0,15]; 20 разных сидов подряд тоже), доля `Critical` среди брака **0,139** (≤ 0,20; бросками и по весам, совпадают в пределах 0,03), среднее время до обнаружения `Major` при 1 млн единиц **11,7 мес.** (в [6; 18]) — через настоящий `FlawCycle.OnMonth`, 4000–6000 записей, совпадает с законом `h = 0,0817` и принудительным обнаружением на 36-м месяце в пределах 0,8; один и тот же сид даёт одинаковые числа (сравнение всех полей результата), другой сид — другие. Побочные проверки: сложность масштабирует среднюю p множителями таблицы, `flawFrequency` — линейно, спешка добавляет `kRush`, p в пределах (0; `pMax`], тип `security` до 2015 не выпадает, средняя p растёт с эпохой, чем больше продано единиц и тяжелее брак — тем раньше находка. Заметка владельцу: в позднюю игру средняя p выходит за 15 % (2000–10 — 0,167, 2015–25 — 0,202; вся игра 1971–2030 — 0,147), см. п. 11.4)
| TSK-025 Ручная проверка в игре (владелец) | Pending |
| TSK-026 Корректировщик обзоров прессы | Complete (авто 2026-10-10, кусок 5: `Logic/FlawPressRules.cs` (`IFlawReviewDraft`, `FlawPressRules.Applies`/`Adjustment`/`Apply`: известный незакрытый брак — `OverallAdjustment` += `press.overallAdjustment[тяжесть]` (−8 / −15 / −15) и примечание `press.note.knownFlaw`; `Hidden` и закрытые не меняют обзор; тайный брак соперника (без `PublicSinceDay`) не меняет; AC-18.1 на фейке черновика), `Press/FlawPressAdjuster.cs` (читает запись через публичный `DesignFlawRecallsApi.Registry.FindByCpu`, черновик — через публичные поля `OverallAdjustment` и `Notes`, без побочных эффектов), `Press/FlawPressBridge.cs` (`RegisterAdjuster` с `Id = "design-flaw-recalls"`, пока включены обе фичи; `UnregisterAdjuster`, когда любая выключена; следит за `GameReady`, `ModFeatures.StateChanged`, `OnEnabled`/`OnDisabled`), `Logic/AdjusterBridge.cs` — объект `IReviewAdjuster` собирается через `Reflection.Emit`, потому что `press-reviews` не входит в `feature_deps.json` этой фичи (прямой класс не компилируется в пофичевой сборке; см. `mod/requests/design-flaw-recalls.md` п. 9.1); проверено на настоящем `IReviewAdjuster` во временной сборке; статьи фича не публикует (тест: в коде нет `PublishArticle`); тесты `FlawPressAdjusterTests`) |
| TSK-027 Передача брака при поглощении CPU | Complete (авто 2026-10-10, кусок 6: чистое правило — `Logic/FlawTransferEngine.cs` (`FlawTransferEngine.OnTransfer`, `IFlawTransferHost`, `FlawTransferOutcome`): запись базового CPU получает владельца `ModCompanies.KeyOf(to)` (`FlawStore.MoveOwner`), перед сменой ключа `FlawChannelBook.RemoveCompanyEntries` снимает записи `contract.signChance` прежней компании (после загрузки находит их через `Explain` по старому ключу), записи `market.rating.cpu` остаются; вариант (`Cpu.Base != null`), CPU без записи и запись в итоговом состоянии (`Resolved`/`Expired`/`Closed`) не двигаются (для итоговой — строка в лог); `Hidden`, `InternalKnown`, `Stepping`, `Mitigated` сохраняют состояние и сроки, а монитор ищет владельца по ключу, поэтому ежедневная плата за ревизию, утечка и истечение идут к новому владельцу (AC-19.2); запись, которая ждёт решения, предлагается новому владельцу (игроку — окно `dfr.decide.*` через `FlawDecisionEngine.Reoffer`, старое место в очереди отдаётся; ИИ — политикой), перед этим котировка делается заново по книгам нового владельца (`FlawCycle.Requote`; потолок отзыва зависит от выручки владельца); публичный брак с игнором: `FlawStateMachine.ReopenForNewOwner` сбрасывает ответ, `ignoreDrainUntilDay`, `suitFiled`, `coverUp`, и запись снова ждёт решения → окно `dfr.decide.public*` (AC-19.1), дренаж и иск больше не идут; `CpuReleased` не поднимается, `rolledCpuIds`/`rushCpuIds` и новые записи не трогаются (тест по состоянию и по исходнику); обратный порядок: если прежний владелец уже банкрот (`from.IsBankrupt`) в момент переноса — запись закрывается (`FlawCycle.CloseRecord`, FR-4 п. 5) со строкой в лог, а если монитор успел закрыть её сам по `OnCompanyWentBankrupt` — перенос видит закрытую запись и ничего не меняет, тоже со строкой в лог; после переноса `OnFlawStateChanged` идёт с новым `CompanyKey` (карточка CPU игрока обновляется). Игровая сторона: `FlawTransferHandler.cs` (подписка `ModEvents.CpuTransferred += Transfer.OnCpuTransferred` в `Register`, тело в `ModHooks.Guard`, `ModCompanies.KeyOf` для обеих компаний, `IsBankrupt` прежнего владельца), `FlawDecisionDispatcher.Reoffer`; `FlawRegistry.MoveOwner` и `FlawChannels.RemoveCompanyEntries` были с кусков 2 и 3, движок вызывает их чистые половины (`FlawStore.MoveOwner`, `FlawChannelBook.RemoveCompanyEntries`); 41 тест `FlawTransferTests`; в игре не проверялось: событие приходит от `rival-acquisitions`, см. `mod/requests/design-flaw-recalls.md` п. 10) |
| TSK-028 Модель спешки (RushMath) | Complete (авто 2026-10-10, кусок 6: `Logic/RushMath.cs` — `ApplyTime` (`max(1, round(t · (1 − timeCut)))`, половины вверх, коэффициент берётся как десятичное число файла; проект короче дня не трогается; срок не меньше 1 дня), `ApplyCost` (`cost · costMultiplier`; коэффициент < 1 или NaN считается 1: спешка никогда не дешевле), `TimeCutPercent` и `CostPremiumPercent` (числа подсказки переключателя), перегрузки с `FlawBalance` и `FlawBalance.RushBalance`; 400 дней и 80 млн при 0,25 и 1,10 → 300 дней и 88 млн (в месяц 6,0 → 8,8 млн); `FlawRiskModel` уже добавляет `kRush · RS` (AC-2.4: 0,118; для ИИ признак игнорируется) — тесты `RushMathTests` проверяют оба случая, `flawFrequency` и `pMax`; параметры читаются из `flaws.json` (`rush`, `risk.kRush`): тест читает поставляемый файл, переопределение (`timeCut` 0,4 → 240 дней) и плохое значение → умолчание; 21 тест `RushMathTests`) |
| TSK-029 Переключатель «Ускоренный проект» | Complete (авто 2026-10-10, кусок 6: `Patches/RushProjectPatches.cs` — пять `Optional`-патчей: postfix `CreatorCpu.Project` (`ref int ___projectTime`, `ref float ___projectCost` → `RushMath`; без замыканий, потому что вызывается каждый кадр: охрана `CanRun`/`ReportFault` на месте; `UpdateInfoTexts` затем показывает срок, стоимость, «в месяц» и дату окончания уже изменёнными, а `CreateCpu` строит проект и `DevelopmentCost` из изменённых полей), prefix `CreatorCpu.CreateCpu` (`___nameInput`, `___projectTime` → ожидающий проект `{ имя, сегодня + срок }` в `rushProjects`), postfix `CreateCpu` (переключатель снова выключен для следующего проекта), postfix `ProjectCompletedHandler.AddCpu` (ожидающий проект с тем же именем и концом в пределах ±3 дней становится `SaveID` в `rushCpuIds`; это не `Company.AddCpu` ядра) и postfix `CreatorCpu.UpdateUI` (переключатель создаётся при открытии окна, показывается или скрывается); `RushProjectOption.cs` — `ModUI.Toggle` «Ускоренный проект» (`dfr.rush.toggle`, подсказка `dfr.rush.tooltip` с процентами из файла) перед кнопкой создания (`createCpuButton`, по `AccessTools`), выключен для каждого нового проекта; предлагается, только если фича включена, `rushEnabled` = true, сейв не заблокирован, применены ВСЕ патчи класса (`ModHooks.IsApplied`) и поле кнопки найдено — иначе переключателя нет, скрытый переключатель выключен, пишется одно предупреждение и фича работает дальше; `OnDisabled` скрывает его; `DesignerRiskHint.RushSource = Rush.IsRushOn` (подсказка риска видит переключатель). Чистая часть — `Logic/RushProjectBook.cs` (`Register`, `Complete`, `Prune`, `Snapshot`: допуск 3 дня, побеждает ближайший конец, проекты других компаний не засчитываются, потерянные проекты старше 60 дней и свыше 64 штук отбрасываются; хранение в `FlawSaveData.rushProjects` переживает сохранение и загрузку — тест через `FlawSaveSection`). Вариант CPU не бросает и присоединяется к записи базового (метка спешки базового уже учтена в её броске); ИИ метку не получает, а формула её для него игнорирует. В логе `roll cpu=<id> p=…` для CPU со спешкой p включает `kRush` (0,118 против 0,070, AC-20.2). Строки `dfr.rush.*` в ru/en; 30 тестов `RushProjectTests`; в игре не запускалось: расположение переключателя и подмену полей проверить в игре, см. `mod/requests/design-flaw-recalls.md` п. 10) |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-006 |
| FR-2 | TSK-002, TSK-004, TSK-024, TSK-028 |
| FR-3 | TSK-002, TSK-005 |
| FR-4 | TSK-007, TSK-008, TSK-024 |
| FR-5 | TSK-009, TSK-010, TSK-014 |
| FR-6 | TSK-009, TSK-011 |
| FR-7 | TSK-008, TSK-009, TSK-011 |
| FR-8 | TSK-008, TSK-009, TSK-012, TSK-014 |
| FR-9 | TSK-008, TSK-009, TSK-012, TSK-014 |
| FR-10 | TSK-013 |
| FR-11 | TSK-015, TSK-016 |
| FR-12 | TSK-017, TSK-018 |
| FR-13 | TSK-020 |
| FR-14 | TSK-019, TSK-029 |
| FR-15 | TSK-001, TSK-002, TSK-023 |
| FR-16 | TSK-003 |
| FR-17 | TSK-015, TSK-021 |
| FR-18 | TSK-026 |
| FR-19 | TSK-027 |
| FR-20 | TSK-028, TSK-029 |
| NFR-1 | TSK-014, TSK-025 |
| NFR-2 | TSK-001, TSK-008, TSK-023 |
| NFR-3 | TSK-003 |
| NFR-4 | TSK-022 |
| NFR-5 | TSK-009, TSK-010 |
| NFR-6 | TSK-005, TSK-024 |
| NFR-7 | TSK-002, TSK-003 |
| NFR-8 | TSK-012, TSK-014, TSK-023 |
