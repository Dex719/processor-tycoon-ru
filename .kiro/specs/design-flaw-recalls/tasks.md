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

- [ ] **TSK-001**: Каркас фичи и настройки
  - **Requirement**: FR-15, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/DesignFlawRecallsFeature.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/FlawSettings.cs`
  - **Acceptance**:
    - `[ModFeature] sealed class DesignFlawRecallsFeature : IModFeature` в `PTMod.Features.DesignFlawRecalls`: `Id = "design-flaw-recalls"`, `NameKey = "dfr.feature.name"`, `DescriptionKey = "dfr.feature.desc"`, `DependsOn = { "event-system" }` (мягкие связи не входят), `EnabledByDefault = true`, `SupportsHotToggle = true`;
    - `Register(FeatureContext)` объявляет через `ctx.Param` ключи `aiFlaws` (true), `showRiskHint` (true), `rushEnabled` (true), `flawFrequency` (1.0, 0.5–2.0, шаг 0.1), `debug` (false); включение фичи — общий переключатель `ModSettings`;
    - `OnDisabled` запоминает `lastActiveDay` и НЕ вызывает `ModModifiers.RemoveAll`; `OnEnabled` НЕ пересоздаёт записи (заглушка сдвига сроков до TSK-008);
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`, `[ModFeature]`), TSK-014 (`ModSettings`), TSK-020 (реестр фич)

- [ ] **TSK-002**: Контент баланса и валидатор
  - **Requirement**: FR-2, FR-3, FR-15, FR-20, NFR-7
  - **Deliverables**: `mod/src/PTMod/Content/design-flaw-recalls/flaws.json`, `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawBalance.cs` (DTO), `mod/src/PTMod/Features/DesignFlawRecalls/Logic/FlawContentValidator.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawContentValidatorTests.cs`
  - **Acceptance**:
    - `flaws.json` совпадает с примером design (раздел Data Models), включая `risk.kRush`, `rush`, `responses.pendingGraceDays`; блока `aiCaution` нет;
    - валидатор проверяет каждый диапазон из раздела Security (в том числе `kRush`, `rush.timeCut`, `rush.costMultiplier`, `pendingGraceDays`, префикс `dfr.` у `nameKey`) и подставляет значения по умолчанию;
    - тесты: по одному на каждое правило + битый JSON → значения по умолчанию;
    - встроенные значения по умолчанию в коде равны значениям `flaws.json`.
  - **Depends**: TSK-001, mod-framework TSK-015 (`ModContent`), TSK-067 (мини-раннер)

- [ ] **TSK-003**: Секция сохранения
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

- [ ] **TSK-004**: Модель вероятности брака
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

- [ ] **TSK-005**: Выбор тяжести и типа
  - **Requirement**: FR-3
  - **Deliverables**: `FlawRiskModel.PickSeverity`, `FlawRiskModel.PickType`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawPickTests.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FakeRng.cs` (детерминированный `IModRng`)
  - **Acceptance**:
    - при p = 0.233 распределение на 100 000 бросков с фиксированным сидом — [0.49, 0.36, 0.15] ± 0.01;
    - `security` не выпадает для 2010 года;
    - вес `stability` при OC = 0.4 равен 9;
    - без доступных типов — `arith`;
    - логика использует `IModRng` ядра (собственного интерфейса случайности нет).
  - **Depends**: TSK-004, mod-framework TSK-012 (`IModRng`)

- [ ] **TSK-006**: Бросок при выпуске (подписка на `ModEvents.CpuReleased`)
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

- [ ] **TSK-007**: Модель обнаружения
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/DiscoveryModel.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/DiscoveryModelTests.cs`
  - **Acceptance**:
    - таблица `h` из design (9 значений) совпадает с точностью 0.001;
    - принудительное обнаружение по `maxHiddenMonths`;
    - истечение по `expireAfterRetiredMonths` и `expireNoSalesMonths`;
    - `LeakRate` для `Ignore`, тихой ревизии и хвоста.
  - **Depends**: TSK-002

- [ ] **TSK-008**: Месячный и дневной монитор
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

- [ ] **TSK-009**: Калькулятор котировок
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

- [ ] **TSK-010**: События выбора и диспетчер
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

- [ ] **TSK-011**: Эффекты «Отзыв» и «Ревизия»
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

- [ ] **TSK-012**: Эффекты «Заплатка» и «Игнор»
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

- [ ] **TSK-013**: Репутация и популярность
  - **Requirement**: FR-10
  - **Deliverables**: дополнения `FlawResponseApplier` (`ApplyReputation`, `ApplyPopularityHit`), `mod/tests/PTMod.Tests/DesignFlawRecalls/ReputationTableTests.cs`
  - **Acceptance**:
    - дельты из таблицы design (п. 7) для всех реакций и тяжестей;
    - `CoverUp` удваивает сумму базовой и ответной дельт;
    - причина в `ModReputation.Add` — ключ L10n (`dfr.rep.public`, `dfr.rep.recall`, `dfr.rep.stepping`, `dfr.rep.patch`, `dfr.rep.ignore`, `dfr.rep.drain`, `dfr.rep.leak`), идентификатор записи пишется в `ModLog`;
    - популярность умножается на `(1 − hit)` и не уходит ниже 0; индекс списка совпадает с `(int)Segment`.
  - **Depends**: TSK-011, mod-framework TSK-026 (`ModReputation`)

- [ ] **TSK-014**: Записи каналов (`market.rating.cpu` для CPU, `contract.signChance` для компании)
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

- [ ] **TSK-015**: Политика выбора ИИ
  - **Requirement**: FR-11, FR-17
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/AiFlawPolicy.cs`, `mod/src/PTMod/Features/DesignFlawRecalls/IFlawResponseBias.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/AiFlawPolicyTests.cs`
  - **Acceptance**:
    - отзыв отсекается при `CashHeadroom` < 1.5 · `RecallCost`; запас денег = `MoneyAmount − ModCredit.ThresholdOf(company)` (в тестах порог подставляется фейком; `CentralBank.DebtLimit` напрямую не читается);
    - без заплатки её вес 0;
    - множители `IFlawResponseBias` применяются (порядок Recall, Stepping, Patch, Ignore; [0.5, 1, 1, 2] даёт [0.15, 0.30, 0.25, 0.30]);
    - все веса 0 → `Ignore`;
    - распределение на 10 000 бросков совпадает с весами ± 0.02.
  - **Depends**: TSK-009, mod-framework TSK-058 (`ModCredit`)

- [ ] **TSK-016**: Поток брака ИИ и новости
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

- [ ] **TSK-017**: Страница «Брак и отзывы»
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

- [ ] **TSK-018**: Пометка на карточке CPU
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Patches/CpuBadgePatch.cs`
  - **Acceptance**:
    - постфикс `CpuSalesUI.UpdateDailyTexts` (`Optional`) дописывает «Брак: <статус>» цветом `Colors.Instance.DynamicNegativeHex` для известных незакрытых браков игрока;
    - без брака текст карточки не меняется.
  - **Depends**: TSK-008, mod-framework TSK-023 (`ModHooks`)

- [ ] **TSK-019**: Подсказка риска в конструкторе (Could)
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Patches/DesignerRiskHint.cs`
  - **Acceptance**:
    - постфикс `CreatorCpu.UpdateInfoTexts` (`Optional`) показывает «Риск скрытого брака: ~N %», шаг 5 %, пока включён `showRiskHint` (без привязки к сложности);
    - при изменении слайдера частоты значение меняется;
    - `ModRandom` не вызывается;
    - если приватные поля `CreatorCpu` не найдены, подсказка выключается с записью в лог;
    - оценка учитывает переключатель спешки после TSK-029.
  - **Depends**: TSK-004, mod-framework TSK-023 (`ModHooks`)

- [ ] **TSK-020**: API `FlawRegistry` для других фич
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

- [ ] **TSK-021**: Точка расширения для rival-personalities (`SetResponseBias`, данные для осторожности)
  - **Requirement**: FR-17
  - **Deliverables**: дополнения `FlawRegistry` (`SetResponseBias`), подключение смещения в `AiFlawPolicy`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawResponseBiasTests.cs`
  - **Acceptance**:
    - `FlawRegistry.SetResponseBias(IFlawResponseBias)` — один слот, `null` снимает; смещение подключается в `AiFlawPolicy` (порядок Recall, Stepping, Patch, Ignore);
    - исключение в `Multipliers` → множители 1.0, ошибка засчитывается вызывающей фиче (`ModHooks.Guard`);
    - `OnFlawStateChanged` при переходе записи ИИ в `Public` передаёт `CompanyKey`, `Severity`, `State`, `PublicSinceDay`;
    - патча `AIBehaviourCreation.AcceptableFailureRate` в фиче нет (решение владельца: осторожность ИИ ведёт `rival-personalities`); описание контракта дублируется в спеке `rival-personalities` (её TSK-020/TSK-021 вызывают `SetResponseBias`).
  - **Depends**: TSK-015, TSK-020; rival-personalities (потребитель, мягкая зависимость)

- [ ] **TSK-026**: Корректировщик обзоров прессы
  - **Requirement**: FR-18
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Press/FlawPressAdjuster.cs`, `mod/tests/PTMod.Tests/DesignFlawRecalls/FlawPressAdjusterTests.cs`
  - **Acceptance**:
    - при `ModFeatures.IsActive("press-reviews")` регистрируется `PressReviewsApi.RegisterAdjuster` с `Id = "design-flaw-recalls"`, при выключении — `UnregisterAdjuster`;
    - для известного незакрытого брака `OverallAdjustment` уменьшается на `press.overallAdjustment[тяжесть]` (−8 / −15 / −15), добавляется примечание;
    - `Hidden` и закрытые записи обзор не меняют;
    - статьи фича не публикует (их пишет `press-reviews` по `OnFlawStateChanged`);
    - AC-18.1 на фейке `ReviewDraft`.
  - **Depends**: TSK-020, press-reviews (`PressReviewsApi`, `IReviewAdjuster`)

- [ ] **TSK-027**: Передача брака при поглощении CPU
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

- [ ] **TSK-028**: Модель спешки (`RushMath`) и учёт в риске
  - **Requirement**: FR-20, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/DesignFlawRecalls/Logic/RushMath.cs` (`ApplyTime`, `ApplyCost`), `mod/tests/PTMod.Tests/DesignFlawRecalls/RushMathTests.cs`
  - **Acceptance**:
    - 400 дней и 80 млн при `timeCut` 0.25 и `costMultiplier` 1.10 → 300 дней и 88 млн; срок не меньше 1 дня;
    - `FlawRiskModel` добавляет `kRush × RS` (AC-2.4: 0.118); для ИИ признак игнорируется;
    - параметры читаются из `flaws.json` (`rush`, `risk.kRush`).
  - **Depends**: TSK-002, TSK-004

- [ ] **TSK-029**: Переключатель «Ускоренный проект» в конструкторе
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

- [ ] **TSK-022**: Строки ru/en
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (ключи `dfr.*` из таблицы design, включая `dfr.feature.desc`, `dfr.mail.auto.*`, `dfr.rep.*`, `dfr.mod.*`, `dfr.rush.*`)
  - **Acceptance**:
    - все ключи из design есть в обоих языках и начинаются с `dfr.` (`ModIds.IsL10nKeyAllowed`);
    - тест на полноту ключей фичи зелёный;
    - суммы форматируются `StringFormatter.FloatToMoney`;
    - названия компаний в текстах берутся из игры, в строки не вшиваются.
  - **Depends**: TSK-010, TSK-017, TSK-029, mod-framework TSK-013, TSK-043 (`ModL10n`, `core.json`), TSK-066 (`ModIds`)

- [ ] **TSK-023**: Самопроверка и изоляция ошибок
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

- [ ] **TSK-024**: Тест баланса (Монте-Карло)
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
| TSK-001 Каркас фичи и настройки | Pending |
| TSK-002 Контент баланса и валидатор | Pending |
| TSK-003 Секция сохранения | Pending |
| TSK-004 Модель вероятности брака | Pending |
| TSK-005 Выбор тяжести и типа | Pending |
| TSK-006 Бросок при выпуске (ModEvents.CpuReleased) | Pending |
| TSK-007 Модель обнаружения | Pending |
| TSK-008 Месячный и дневной монитор | Pending |
| TSK-009 Калькулятор котировок | Pending |
| TSK-010 События выбора и диспетчер | Pending |
| TSK-011 Эффекты «Отзыв» и «Ревизия» | Pending |
| TSK-012 Эффекты «Заплатка» и «Игнор» | Pending |
| TSK-013 Репутация и популярность | Pending |
| TSK-014 Записи каналов | Pending |
| TSK-015 Политика выбора ИИ | Pending |
| TSK-016 Поток брака ИИ и новости | Pending |
| TSK-017 Страница «Брак и отзывы» | Pending |
| TSK-018 Пометка на карточке CPU | Pending |
| TSK-019 Подсказка риска в конструкторе (Could) | Pending |
| TSK-020 API FlawRegistry | Pending |
| TSK-021 Точка расширения для rival-personalities | Pending |
| TSK-022 Строки ru/en | Pending |
| TSK-023 Самопроверка и изоляция ошибок | Pending |
| TSK-024 Тест баланса | Pending |
| TSK-025 Ручная проверка в игре (владелец) | Pending |
| TSK-026 Корректировщик обзоров прессы | Pending |
| TSK-027 Передача брака при поглощении CPU | Pending |
| TSK-028 Модель спешки (RushMath) | Pending |
| TSK-029 Переключатель «Ускоренный проект» | Pending |

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
