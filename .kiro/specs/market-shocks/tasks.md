# Tasks: market-shocks

## Document Information

- **Feature Name**: market-shocks — рыночные шоки: аварии фабрик, скачки цен на память и сырьё, циклы спроса
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: [requirements.md](requirements.md)
  - Design: [design.md](design.md)
  - Движок: `.kiro/specs/event-system/tasks.md` (зависимости вида `event-system/TSK-NNN`)
  - Фреймворк: `.kiro/specs/mod-framework/tasks.md` (зависимости вида `mod-framework/TSK-NNN`, номера v0.2)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала — кодовая часть с чистой логикой и тестами:
- дневной кеш каналов `production.memoryCost` / `production.waferCost` (сами каналы объявляет ядро) и их читающие патчи;
- кривые `ShockCurves`;
- типы эффектов `ModifierRamp`, `ModifierWave`, `InventoryLoss`;
- предикаты `ms.sellsBelowCost`, `ms.hasRecentCpu`.

Затем — фича `MarketShocksFeature` (`[ModFeature] IModFeature`, регистрация пака в `event-system`) и контент (21 запись) по группам с локализацией. В конце — симулятор баланса на 50 лет, настройки, самопроверка и ручная проверка владельцем.

### Implementation Strategy

- Формулы (`ShockCurves.MemoryTerm`, кривые) — чистые функции, патчи — тонкие обёртки над ними.
- Контент пишется по таблице «Каталог шоков» из `design.md`. Числа в коде не дублируются.
- Баланс подтверждается симулятором до ручной проверки.
- Каналы памяти и пластин объявлены ядром (`core.json`, владелец `market-shocks`); фича их не объявляет (`ModModifiers.DeclareChannel` не вызывается). Патчи на `CpuMath.MaterialCost`, `ProductionMath.WaferMaterialCost`, `ProductionMath.WaferCost`, `Memory.GetDescriptions` — «формулы фичи», цели закреплены ядром за `market-shocks`.
- Собственной секции `ModSave` у пака нет; версию секции `event-system` ведёт ядро (`_schema`).

### Development Approach

- **Testing Strategy**: TDD для кривых, формул, кеша каналов и предикатов (свой мини-раннер .NET Framework 4.8, `python mod/build.py --tests`, `[Covers]` на публичных типах `Logic`); симулятор баланса на логике движка; самопроверка на старте; ручные сценарии M1–M13.
- **Integration Strategy**: `[ModFeature] IModFeature` с `DependsOn = { "event-system" }`; регистрация пака в `Register`, загрузку контента делает движок после всех `Register`. Стандартные каналы — патчи P1–P5 `mod-framework`.
- **Deployment Strategy**: `install.py --with-mods`; пак включён по умолчанию, выключается в хабе «Моды».

## Фаза 1 — Каналы и патчи

- [ ] **TSK-001**: Дневной кеш каналов и патч `CpuMath.MaterialCost`
  - **Requirement**: FR-8, NFR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/MarketShocks/Channels/ShockChannels.cs` (константы `ModChannels.ProductionMemoryCost` / `ModChannels.ProductionWaferCost`; дневной кеш `MemoryK` / `WaferK` по `ModTicks.Today`; быстрый путь `ModModifiers.HasAny`; `Invalidate` по смене состояния фичи, `ModTicks.GameReady` и `ModModifiers.ChannelChanged`). Каналы не объявляются: их объявляет ядро;
    - `mod/src/PTMod/Features/MarketShocks/Channels/ShockCurves.cs` (метод `MemoryTerm`);
    - `mod/src/PTMod/Features/MarketShocks/Patches/MaterialCostPatch.cs` (`[ModPatch(typeof(CpuMath), "MaterialCost", PatchKind.Postfix)]`).
  - **Acceptance**: при `MemoryK == 1` — ранний выход; при k ≠ 1 — `__result += memTerm · (k − 1)`; при `null` / `CacheCostReduction ≤ 0` / NaN — исходный результат; тело в `ModHooks.Guard`; `ModModifiers.Get` вызывается не чаще раза в игровой день (и после `ChannelChanged`); `ModHooks.IsReserved` для цели возвращает `FeatureFormula` с владельцем `market-shocks`, патч принимается
  - **Depends**: mod-framework/TSK-017, mod-framework/TSK-027, mod-framework/TSK-059 (`ModModifiers`: `Invalidate`, события), mod-framework/TSK-043 (реестр каналов `core.json`), mod-framework/TSK-023 (`ModHooks`, резерв `FeatureFormula`), mod-framework/TSK-024 (`ModTicks`)

- [ ] **TSK-002**: Патчи цены пластин и описаний
  - **Requirement**: FR-8, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Patches/WaferCostPatches.cs` (postfix `ProductionMath.WaferMaterialCost`; postfix `ProductionMath.WaferCost`, `Optional`), `mod/src/PTMod/Features/MarketShocks/Patches/MemoryDescriptionPatch.cs` (postfix `Memory.GetDescriptions`, `Optional`)
  - **Acceptance**:
    - `WaferMaterialCost × WaferK` (действует на `Factory.ProduceWafers`, `CpuMath.RealUnitCost` и производство GPU, AC-8.3);
    - `WaferCost += 2000 · m · (k − 1) / yield` при `yield > 0`;
    - описание памяти: замена `__result[2]` только при `Count ≥ 3` и k ≠ 1, текст через `ModL10n` (`ms.ui.cache_cost`);
    - все четыре цели закреплены за `market-shocks` как `FeatureFormula` (патч другой фичи отклоняется, AC-8.4).
  - **Depends**: TSK-001

- [ ] **TSK-003**: Тесты формул и кеша каналов
  - **Requirement**: FR-8, FR-13, NFR-1
  - **Deliverables**: `mod/tests/PTMod.Tests/MarketShocks/MemoryTermTests.cs`, `mod/tests/PTMod.Tests/MarketShocks/WaferCostTests.cs`, `mod/tests/PTMod.Tests/MarketShocks/ChannelCacheTests.cs`
  - **Acceptance**: AC-4.1 (130 → 186,4), строки таблицы примеров из `design.md` (+43 %, +53 %, 0 %), AC-8.2 (3 333 → 5 000 при k = 1,5), защиты `CacheCostReduction = 0` и `null`; кеш: одно чтение на день, сброс по `Invalidate` и `ChannelChanged` — зелёные; публичные типы в `[Covers]`
  - **Depends**: TSK-001, TSK-002; mod-framework/TSK-067

## Фаза 2 — Типы эффектов и предикаты

- [ ] **TSK-004**: Кривые и эффект `ModifierRamp`
  - **Requirement**: FR-7, FR-11, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Channels/ShockCurves.cs` (`Trapezoid`, `Ramp`, `TotalDays`), `mod/src/PTMod/Features/MarketShocks/Effects/ModifierRampEffect.cs`
  - **Acceptance**:
    - `Validate` по диапазонам `design.md`;
    - `Resolve` пишет `curve = [peak', rampIn, hold, rampOut]` с учётом интенсивности и зажимает `peak'` пределами канала (`IEffectModifiers.LimitMin` / `LimitMax`), AC-7.4;
    - `Tick` / `Restore` выставляют значение через `ctx.Contributions.Set`, `Remove` — `Clear`;
    - `Describe` — ключ `ms.fx.ramp` с датой окончания.
  - **Depends**: event-system/TSK-010, event-system/TSK-011

- [ ] **TSK-005**: Эффект `ModifierWave`
  - **Requirement**: FR-7, FR-11, NFR-3
  - **Deliverables**: `ShockCurves.Wave` в `mod/src/PTMod/Features/MarketShocks/Channels/ShockCurves.cs`, `mod/src/PTMod/Features/MarketShocks/Effects/ModifierWaveEffect.cs`
  - **Acceptance**: фаза `0` / `180` / `random` (бросок из генератора экземпляра); общая фаза для одного `phaseGroup` внутри экземпляра; длительность `period · cycles`; амплитуда масштабируется интенсивностью и зажимается пределом канала; `Describe` — `ms.fx.wave` с «сначала рост / спад»
  - **Depends**: TSK-004

- [ ] **TSK-006**: Эффект `InventoryLoss`
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Effects/InventoryLossEffect.cs`
  - **Acceptance**:
    - `Resolve` считает списание по CPU с `Cpu.get_Manufacturer() == цель`, `Base == null`, `!IsRetired` и сохраняет пары `[saveId, lost]` в `curve`;
    - `Apply` уменьшает `CpuBase.MarketData.Stock` не ниже 0 и пропускает ненайденные CPU (`CpuDataProvider.GetCpu` → null);
    - `Describe` — `ms.fx.inventory`.
  - **Depends**: event-system/TSK-010

- [ ] **TSK-007**: Предикаты `ms.sellsBelowCost` и `ms.hasRecentCpu`
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Conditions/ShockPredicates.cs`
  - **Acceptance**: логика по FR-12 и `design.md`; регистрация через `EventConditions.Register("ms.sellsBelowCost", "market-shocks", …)` и `("ms.hasRecentCpu", "market-shocks", …)` в каталог `ModConditions`; имена проходят `ModIds.IsPredicateNameAllowed`; параметры на верхнем уровне узла; `MarketDataProvider.Instance == null` → ложь; значения по умолчанию `priceRatio = 0.95`, `minShare = 0.05`, `maxAgeYears = 1`
  - **Depends**: event-system/TSK-005; mod-framework/TSK-019 (`ModConditions`), mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-008**: Тесты типов эффектов и предикатов
  - **Requirement**: FR-7, FR-12, NFR-3, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/MarketShocks/ShockCurvesTests.cs`, `InventoryLossTests.cs`, `PredicateTests.cs`
  - **Acceptance**: AC-7.1, AC-7.2, AC-7.3, AC-7.4, AC-6.1, AC-12.1 — зелёные (предикаты — на `FakeGameState`); `Restore` на середине кривой даёт то же значение, что непрерывный `Tick`; `Restore` с испорченными параметрами (пик 10, период 0) отбрасывает эффект; интенсивность 0,5 / 1,5 / 2 масштабирует отклонение, «Хаос» зажимается пределом канала
  - **Depends**: TSK-004, TSK-005, TSK-006, TSK-007; mod-framework/TSK-067

## Фаза 3 — Регистрация и контент

- [ ] **TSK-009**: Фича пака и конфиг категорий
  - **Requirement**: FR-1, FR-10, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/MarketShocksFeature.cs` (`[ModFeature]`, `Id = "market-shocks"`, `NameKey` / `DescriptionKey`, `DependsOn = { "event-system" }`, `EnabledByDefault = true`, `SupportsHotToggle = true`), `mod/src/PTMod/Content/market-shocks/config.json`
  - **Acceptance**:
    - `Register` регистрирует 3 типа, 2 предиката, пак (`EventRegistry.RegisterPack` с `EventsFile = "shocks"`, `RivalNewsTopN = 5`, `SettingsFeatureId = "market-shocks"`, `Severity` и `Frequency` из параметров) и 3 класса патчей (`ctx.Patch`) в порядке из `design.md`; `ModModifiers.DeclareChannel` не вызывается;
    - параметры `intensity` (Float 0.5…2.0, шаг 0.5) и `frequency`;
    - категории и `channelLimits` из `design.md`;
    - `event-system` недоступна → фича `Unavailable`, патчей нет;
    - лог AC-1.1; перемотка нейтральна (AC-1.3).
  - **Depends**: TSK-001, TSK-002, TSK-004, TSK-005, TSK-006, TSK-007; event-system/TSK-002, event-system/TSK-006, event-system/TSK-014; mod-framework/TSK-020, mod-framework/TSK-022 (реестр фич), mod-framework/TSK-043 (реестр каналов), mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-010**: Контент: фабричные и отраслевые шоки
  - **Requirement**: FR-1, FR-2, FR-3, FR-10
  - **Deliverables**: записи №1–7 (`ms.fab_fire`, `ms.cleanroom_contamination`, `ms.grid_failure`, `ms.litho_breakdown`, `ms.earthquake`, `ms.photoresist_shortage`, `ms.logistics_crunch`) в `mod/src/PTMod/Content/market-shocks/shocks.json`
  - **Acceptance**: числа совпадают с каталогом `design.md`; цели `RandomCompany` / `AllCompanies` с фильтрами `es.hasFab` и `not es.activeInCategory(ms.supply)`; условие К1 (`not es.activeInCategory(history, scope any)`) у №5–7; `important` у шоков с выбором; записи проходят валидатор (в том числе префикс `ms.`)
  - **Depends**: TSK-009

- [ ] **TSK-011**: Контент: ценовые шоки
  - **Requirement**: FR-4, FR-10
  - **Deliverables**: записи №8–13 (`ms.dram_spike_warning`, `ms.dram_spike`, `ms.dram_glut`, `ms.wafer_shortage`, `ms.energy_crisis`, `ms.memory_cycle`) в `shocks.json`
  - **Acceptance**: предупреждение планирует `ms.dram_spike` через 21 день с шансом 0,8 (глобальный `FireEvent` с носителем `Player`); выбор `hedge` / `wait`; кривые по каталогу; `ms.memory_cycle` — `Condition` раз в месяц, откат 1460; условие К1 у №8, №10–12
  - **Depends**: TSK-009

- [ ] **TSK-012**: Контент: шоки и циклы спроса
  - **Requirement**: FR-5, FR-10
  - **Deliverables**: записи №14–19 (`ms.recession_warning`, `ms.recession`, `ms.pc_boom`, `ms.server_boom`, `ms.embedded_boom`, `ms.silicon_cycle`) в `shocks.json`
  - **Acceptance**: только каналы `market.demand.<segment>`; годы бумов (`dateAtLeast` / `dateBefore`); рецессия через 45–75 дней с шансом 0,85; общая фаза `phaseGroup: silicon`; условие К1 у №14, №16–18; AC-5.1 и AC-5.2 проверяются на логике движка
  - **Depends**: TSK-009

- [ ] **TSK-013**: Контент: торговые шоки и политики ИИ
  - **Requirement**: FR-6, FR-9, NFR-7
  - **Deliverables**: записи №20–21 (`ms.dumping_complaint`, `ms.launch_hype`) в `shocks.json`; проверка `aiWeight` / `aiMinCashMultiple` во всех выборах пака
  - **Acceptance**: `ms.sellsBelowCost` и `ms.hasRecentCpu` в фильтрах; у каждого выбора задана политика ИИ по каталогу; шанс пошлины при `lobby` — 0,4; единственный вклад пака в `popularity.gain` — `ms.launch_hype` ×1,2 (NFR-7); в `market.rating.cpu` пак не пишет
  - **Depends**: TSK-009, TSK-007

- [ ] **TSK-014**: Строки пака `ru` / `en`
  - **Requirement**: FR-11, NFR-4
  - **Deliverables**: ключи `ms.*` в `mod/src/PTMod/L10n/ru.json` и `mod/src/PTMod/L10n/en.json`
  - **Acceptance**: у всех 21 записи есть `title`, `text`, `cause`; у вариантов — тексты; у фабричных шоков причина упоминает долю мощностей; предупреждение о штрафах (`ms.warn.contract_fines`); ключи эффектов `ms.fx.*`, каналов `ms.channel.*`, настроек `ms.settings.*`, фичи `ms.feature.*` — в обоих языках; все ключи начинаются с `ms.`
  - **Depends**: TSK-010, TSK-011, TSK-012, TSK-013; mod-framework/TSK-013 (`ModL10n`)

## Фаза 4 — Баланс, настройки, проверка

- [ ] **TSK-015**: Симулятор баланса и тесты паритета
  - **Requirement**: FR-2, FR-9, FR-10, FR-11, NFR-5
  - **Deliverables**: `mod/tests/PTMod.Tests/MarketShocks/BalanceSimulationTests.cs`, `ParityTests.cs`, `PackContentTests.cs`; отчёт симуляции (частоты, доля игрока, максимумы каналов) в разделе «Evidence» этой задачи
  - **Acceptance**:
    - 50 лет × 20 прогонов: частоты категорий ±25 % от FR-10.1 (AC-10.1);
    - доля игрока = доля мощностей ± 2 п. п. (AC-2.1, AC-9.1);
    - пределы каналов не превышены, AC-10.2 воспроизведён; на «Хаосе» выпуск не ниже 0,4 и память не выше 2,0 (AC-10.3);
    - одиночный шок на «Обычно»: выпуск ≥ 55 %, себестоимость ≤ +60 %;
    - одинаковые числа для игрока и ИИ при равной QR и любой сложности (AC-9.3);
    - новости об ИИ только по топ-5 (AC-2.3, AC-2.4); координация К1 (AC-10.4);
    - `PackContentTests`: ID с префиксом `ms.`, условие К1 у №5–8, №10–12, №14, №16–18.
  - **Depends**: TSK-010, TSK-011, TSK-012, TSK-013; event-system/TSK-006, event-system/TSK-007, event-system/TSK-010, event-system/TSK-017; mod-framework/TSK-067

- [ ] **TSK-016**: Настройки пака и выключение посреди игры
  - **Requirement**: FR-10, FR-14
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Settings/MarketShocksSettings.cs` (`ParamSpec` `intensity` 0.5 / 1 / 1.5 / 2, `frequency` → `PackOptions`; `ModSettings.Changed`)
  - **Acceptance**:
    - в хабе «Моды»: переключатель фичи, слайдеры интенсивности (до «Хаоса» ×2) и частоты (0,5 / 1 / 1,5);
    - выключение: `event-system` замораживает эффекты пака (`ModFeatures.StateChanged`), ядро снимает патчи фичи (формулы ванильные);
    - включение возвращает неистёкшие эффекты (AC-14.1).
  - **Depends**: TSK-009; event-system/TSK-023; mod-framework/TSK-014 (`ModSettings`), mod-framework/TSK-039 (хаб)

- [ ] **TSK-017**: Самопроверка пака
  - **Requirement**: FR-1, NFR-4
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/SelfCheck/MarketShocksSelfCheck.cs` (вклад в `ModSelfCheck`)
  - **Acceptance**: проверены цели 4 патчей (закреплены за `market-shocks` как `FeatureFormula`), каналы объявлены в реестре ядра (`ModModifiers.IsDeclared`), зарегистрированные типы и предикаты, имена с префиксом `ms`, загрузка 21 записи, ключи `ms.*` в `ru` / `en`; итог одной строкой `selfcheck OK: 21 events, 4 patches` или список проблем
  - **Depends**: TSK-009, TSK-014; event-system/TSK-026; mod-framework/TSK-042

- [ ] **TSK-018**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-14
  - **Deliverables**: отметки по сценариям M1–M13 из `design.md` в разделе «Evidence» этой задачи
  - **Acceptance**: все сценарии дали ожидаемый результат; замечания по балансу оформлены правками `shocks.json` / `config.json` или новыми задачами
  - **Depends**: TSK-015, TSK-016, TSK-017; event-system/TSK-028; mod-framework/TSK-027, mod-framework/TSK-028, mod-framework/TSK-029 (стандартные каналы P1–P5), mod-framework/TSK-053 (фреймворк проверен в игре)

## Dependency graph

```
mod-framework: ModModifiers(017,027,059) core.json(043) ModHooks(023) ModTicks(024) ModConditions(019) ModIds(066)
               ModFeatures(020,022) ModSettings(014) хаб(039) ModL10n(013) ModSelfCheck(042) каналы P1–P5(027,028,029) раннер(067)
event-system:  TSK-002 TSK-005 TSK-006 TSK-007 TSK-010 TSK-011 TSK-014 TSK-017 TSK-023 TSK-026 TSK-028

TSK-001 ──> TSK-002 ──> TSK-003
   │           │
event-system/TSK-010,011 ──> TSK-004 ──> TSK-005
event-system/TSK-010 ─────> TSK-006
event-system/TSK-005 ─────> TSK-007
TSK-004, TSK-005, TSK-006, TSK-007 ──> TSK-008
TSK-001, TSK-002, TSK-004..007 + event-system/TSK-002,006,014 ──> TSK-009
TSK-009 ──┬──> TSK-010 ──┐
          ├──> TSK-011 ──┤
          ├──> TSK-012 ──┼──> TSK-014 ──> TSK-017 <── event-system/TSK-026
          └──> TSK-013 ──┘
TSK-010..013 + event-system/TSK-006,007,010,017 ──> TSK-015
TSK-009 + event-system/TSK-023 ──> TSK-016
TSK-015, TSK-016, TSK-017 + event-system/TSK-028 ──> TSK-018
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Дневной кеш каналов и патч `CpuMath.MaterialCost` | Pending |
| TSK-002 Патчи пластин и описаний | Pending |
| TSK-003 Тесты формул и кеша каналов | Pending |
| TSK-004 `ModifierRamp` | Pending |
| TSK-005 `ModifierWave` | Pending |
| TSK-006 `InventoryLoss` | Pending |
| TSK-007 Предикаты `ms.*` | Pending |
| TSK-008 Тесты типов и предикатов | Pending |
| TSK-009 Фича пака и конфиг | Pending |
| TSK-010 Контент: фабрики и отрасль | Pending |
| TSK-011 Контент: цены | Pending |
| TSK-012 Контент: спрос и циклы | Pending |
| TSK-013 Контент: торговые и ИИ | Pending |
| TSK-014 Строки ru/en | Pending |
| TSK-015 Симулятор баланса | Pending |
| TSK-016 Настройки пака | Pending |
| TSK-017 Самопроверка | Pending |
| TSK-018 Ручная проверка (владелец) | Pending |

## Покрытие требований

| Требование | Задачи |
|---|---|
| FR-1 | TSK-009, TSK-010, TSK-017 |
| FR-2 | TSK-009, TSK-010, TSK-015 |
| FR-3 | TSK-010 |
| FR-4 | TSK-011 |
| FR-5 | TSK-012 |
| FR-6 | TSK-013 |
| FR-7 | TSK-004, TSK-005, TSK-006, TSK-008 |
| FR-8 | TSK-001, TSK-002, TSK-003 |
| FR-9 | TSK-013, TSK-015 |
| FR-10 | TSK-009, TSK-010, TSK-011, TSK-012, TSK-015, TSK-016 |
| FR-11 | TSK-004, TSK-005, TSK-009, TSK-014, TSK-015 |
| FR-12 | TSK-007, TSK-008 |
| FR-13 | TSK-002, TSK-003 |
| FR-14 | TSK-016 |
| NFR-1 | TSK-001, TSK-003 |
| NFR-2 | TSK-001, TSK-009 |
| NFR-3 | TSK-004, TSK-005, TSK-008 |
| NFR-4 | TSK-014, TSK-017 |
| NFR-5 | TSK-015 |
| NFR-6 | TSK-004, TSK-005, TSK-006, TSK-008 |
| NFR-7 | TSK-013, TSK-015 |
