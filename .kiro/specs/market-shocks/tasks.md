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

- [x] **TSK-001**: Дневной кеш каналов и патч `CpuMath.MaterialCost` (авто 2026-10-10)
  - **Requirement**: FR-8, NFR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/MarketShocks/Channels/ShockChannels.cs` (константы `ModChannels.ProductionMemoryCost` / `ModChannels.ProductionWaferCost`; дневной кеш `MemoryK` / `WaferK` по `ModTicks.Today`; быстрый путь `ModModifiers.HasAny`; `Invalidate` по смене состояния фичи, `ModTicks.GameReady` и `ModModifiers.ChannelChanged`). Каналы не объявляются: их объявляет ядро;
    - `mod/src/PTMod/Features/MarketShocks/Logic/ShockCurves.cs` (метод `MemoryTerm`);
    - `mod/src/PTMod/Features/MarketShocks/Patches/MaterialCostPatch.cs` (`[ModPatch(typeof(CpuMath), "MaterialCost", PatchKind.Postfix)]`).
  - **Acceptance**: при `MemoryK == 1` — ранний выход; при k ≠ 1 — `__result += memTerm · (k − 1)`; при `null` / `CacheCostReduction ≤ 0` / NaN — исходный результат; тело в `ModHooks.Guard`; `ModModifiers.Get` вызывается не чаще раза в игровой день (и после `ChannelChanged`); `ModHooks.IsReserved` для цели возвращает `FeatureFormula` с владельцем `market-shocks`, патч принимается
  - **Depends**: mod-framework/TSK-017, mod-framework/TSK-027, mod-framework/TSK-059 (`ModModifiers`: `Invalidate`, события), mod-framework/TSK-043 (реестр каналов `core.json`), mod-framework/TSK-023 (`ModHooks`, резерв `FeatureFormula`), mod-framework/TSK-024 (`ModTicks`)

- [x] **TSK-002**: Патчи цены пластин и описаний (авто 2026-10-10)
  - **Requirement**: FR-8, FR-13
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Patches/WaferCostPatches.cs` (postfix `ProductionMath.WaferMaterialCost`; postfix `ProductionMath.WaferCost`, `Optional`), `mod/src/PTMod/Features/MarketShocks/Patches/MemoryDescriptionPatch.cs` (postfix `Memory.GetDescriptions`, `Optional`)
  - **Acceptance**:
    - `WaferMaterialCost × WaferK` (действует на `Factory.ProduceWafers`, `CpuMath.RealUnitCost` и производство GPU, AC-8.3);
    - `WaferCost += 2000 · m · (k − 1) / yield` при `yield > 0`;
    - описание памяти: замена `__result[2]` только при `Count ≥ 3` и k ≠ 1, текст через `ModL10n` (`ms.ui.cache_cost`);
    - все четыре цели закреплены за `market-shocks` как `FeatureFormula` (патч другой фичи отклоняется, AC-8.4).
  - **Depends**: TSK-001

- [x] **TSK-003**: Тесты формул и кеша каналов (авто 2026-10-10)
  - **Requirement**: FR-8, FR-13, NFR-1
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/MarketShocks/MemoryTermTests.cs`, `mod/tests/PTMod.Tests/Features/MarketShocks/WaferCostTests.cs`, `mod/tests/PTMod.Tests/Features/MarketShocks/ChannelCacheTests.cs`
  - **Acceptance**: AC-4.1 (130 → 186,4), строки таблицы примеров из `design.md` (+43 %, +53 %, 0 %), AC-8.2 (3 333 → 5 000 при k = 1,5), защиты `CacheCostReduction = 0` и `null`; кеш: одно чтение на день, сброс по `Invalidate` и `ChannelChanged` — зелёные; публичные типы в `[Covers]`
  - **Depends**: TSK-001, TSK-002; mod-framework/TSK-067

## Фаза 2 — Типы эффектов и предикаты

- [x] **TSK-004**: Кривые и эффект `ModifierRamp` (авто 2026-10-10)
  - **Requirement**: FR-7, FR-11, NFR-3
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Logic/ShockCurves.cs` (`Trapezoid`, `Ramp`, `TotalDays`), `mod/src/PTMod/Features/MarketShocks/Effects/ModifierRampEffect.cs`
  - **Acceptance**:
    - `Validate` по диапазонам `design.md`;
    - `Resolve` пишет `curve = [peak', rampIn, hold, rampOut]` с учётом интенсивности и зажимает `peak'` пределами канала (`IEffectModifiers.LimitMin` / `LimitMax`), AC-7.4;
    - `Tick` / `Restore` выставляют значение через `ctx.Contributions.Set`, `Remove` — `Clear`;
    - `Describe` — ключ `ms.fx.ramp` с датой окончания.
  - **Depends**: event-system/TSK-010, event-system/TSK-011

- [x] **TSK-005**: Эффект `ModifierWave` (авто 2026-10-10)
  - **Requirement**: FR-7, FR-11, NFR-3
  - **Deliverables**: `ShockCurves.Wave` в `mod/src/PTMod/Features/MarketShocks/Logic/ShockCurves.cs`, `mod/src/PTMod/Features/MarketShocks/Effects/ModifierWaveEffect.cs`
  - **Acceptance**: фаза `0` / `180` / `random` (бросок из генератора экземпляра); общая фаза для одного `phaseGroup` внутри экземпляра; длительность `period · cycles`; амплитуда масштабируется интенсивностью и зажимается пределом канала; `Describe` — `ms.fx.wave` с «сначала рост / спад»
  - **Depends**: TSK-004

- [x] **TSK-006**: Эффект `InventoryLoss` (авто 2026-10-10)
  - **Requirement**: FR-7
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Effects/InventoryLossEffect.cs`
  - **Acceptance**:
    - `Resolve` считает списание по CPU с `Cpu.get_Manufacturer() == цель`, `Base == null`, `!IsRetired` и сохраняет пары `[saveId, lost]` в `curve`;
    - `Apply` уменьшает `CpuBase.MarketData.Stock` не ниже 0 и пропускает ненайденные CPU (`CpuDataProvider.GetCpu` → null);
    - `Describe` — `ms.fx.inventory`.
  - **Depends**: event-system/TSK-010

- [x] **TSK-007**: Предикаты `ms.sellsBelowCost` и `ms.hasRecentCpu` (авто 2026-10-10)
  - **Requirement**: FR-12
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Conditions/ShockPredicates.cs`
  - **Acceptance**: логика по FR-12 и `design.md`; регистрация через `EventConditions.Register("ms.sellsBelowCost", "market-shocks", …)` и `("ms.hasRecentCpu", "market-shocks", …)` в каталог `ModConditions`; имена проходят `ModIds.IsPredicateNameAllowed`; параметры на верхнем уровне узла; `MarketDataProvider.Instance == null` → ложь; значения по умолчанию `priceRatio = 0.95`, `minShare = 0.05`, `maxAgeYears = 1`
  - **Depends**: event-system/TSK-005; mod-framework/TSK-019 (`ModConditions`), mod-framework/TSK-066 (`ModIds`)

- [x] **TSK-008**: Тесты типов эффектов и предикатов (авто 2026-10-10)
  - **Requirement**: FR-7, FR-12, NFR-3, NFR-6
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/MarketShocks/ShockCurvesTests.cs`, `InventoryLossTests.cs`, `PredicateTests.cs`
  - **Acceptance**: AC-7.1, AC-7.2, AC-7.3, AC-7.4, AC-6.1, AC-12.1 — зелёные (предикаты — на `FakeGameState`); `Restore` на середине кривой даёт то же значение, что непрерывный `Tick`; `Restore` с испорченными параметрами (пик 10, период 0) отбрасывает эффект; интенсивность 0,5 / 1,5 / 2 масштабирует отклонение, «Хаос» зажимается пределом канала
  - **Depends**: TSK-004, TSK-005, TSK-006, TSK-007; mod-framework/TSK-067

## Фаза 3 — Регистрация и контент

- [x] **TSK-009**: Фича пака и конфиг категорий (авто 2026-10-10)
  - **Requirement**: FR-1, FR-10, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/MarketShocksFeature.cs` (`[ModFeature]`, `Id = "market-shocks"`, `NameKey` / `DescriptionKey`, `DependsOn = { "event-system" }`, `EnabledByDefault = true`, `SupportsHotToggle = true`), `mod/src/PTMod/Content/market-shocks/config.json`
  - **Acceptance**:
    - `Register` регистрирует 3 типа, 2 предиката, пак (`EventRegistry.RegisterPack` с `EventsFile = "shocks"`, `RivalNewsTopN = 5`, `SettingsFeatureId = "market-shocks"`, `Severity` и `Frequency` из параметров) и 3 класса патчей (`ctx.Patch`) в порядке из `design.md`; `ModModifiers.DeclareChannel` не вызывается;
    - параметры `intensity` (Float 0.5…2.0, шаг 0.5) и `frequency`;
    - категории и `channelLimits` из `design.md`;
    - `event-system` недоступна → фича `Unavailable`, патчей нет;
    - лог AC-1.1; перемотка нейтральна (AC-1.3).
  - **Depends**: TSK-001, TSK-002, TSK-004, TSK-005, TSK-006, TSK-007; event-system/TSK-002, event-system/TSK-006, event-system/TSK-014; mod-framework/TSK-020, mod-framework/TSK-022 (реестр фич), mod-framework/TSK-043 (реестр каналов), mod-framework/TSK-066 (`ModIds`)

- [x] **TSK-010**: Контент: фабричные и отраслевые шоки (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2, FR-3, FR-10
  - **Deliverables**: записи №1–7 (`ms.fab_fire`, `ms.cleanroom_contamination`, `ms.grid_failure`, `ms.litho_breakdown`, `ms.earthquake`, `ms.photoresist_shortage`, `ms.logistics_crunch`) в `mod/src/PTMod/Content/market-shocks/shocks.json`
  - **Acceptance**: числа совпадают с каталогом `design.md`; цели `RandomCompany` / `AllCompanies` с фильтрами `es.hasFab` и `not es.activeInCategory(ms.supply)`; условие К1 (`not es.activeInCategory(history, scope any)`) у №5–7; `important` у шоков с выбором; записи проходят валидатор (в том числе префикс `ms.`)
  - **Depends**: TSK-009

- [x] **TSK-011**: Контент: ценовые шоки (авто 2026-10-10)
  - **Requirement**: FR-4, FR-10
  - **Deliverables**: записи №8–13 (`ms.dram_spike_warning`, `ms.dram_spike`, `ms.dram_glut`, `ms.wafer_shortage`, `ms.energy_crisis`, `ms.memory_cycle`) в `shocks.json`
  - **Acceptance**: предупреждение планирует `ms.dram_spike` через 21 день с шансом 0,8 (глобальный `FireEvent` с носителем `Player`); выбор `hedge` / `wait`; кривые по каталогу; `ms.memory_cycle` — `Condition` раз в месяц, откат 1460; условие К1 у №8, №10–12
  - **Depends**: TSK-009

- [x] **TSK-012**: Контент: шоки и циклы спроса (авто 2026-10-10)
  - **Requirement**: FR-5, FR-10
  - **Deliverables**: записи №14–19 (`ms.recession_warning`, `ms.recession`, `ms.pc_boom`, `ms.server_boom`, `ms.embedded_boom`, `ms.silicon_cycle`) в `shocks.json`
  - **Acceptance**: только каналы `market.demand.<segment>`; годы бумов (`dateAtLeast` / `dateBefore`); рецессия через 45–75 дней с шансом 0,85; общая фаза `phaseGroup: silicon`; условие К1 у №14, №16–18; AC-5.1 и AC-5.2 проверяются на логике движка
  - **Depends**: TSK-009

- [x] **TSK-013**: Контент: торговые шоки и политики ИИ (авто 2026-10-10)
  - **Requirement**: FR-6, FR-9, NFR-7
  - **Deliverables**: записи №20–21 (`ms.dumping_complaint`, `ms.launch_hype`) в `shocks.json`; проверка `aiWeight` / `aiMinCashMultiple` во всех выборах пака
  - **Acceptance**: `ms.sellsBelowCost` и `ms.hasRecentCpu` в фильтрах; у каждого выбора задана политика ИИ по каталогу; шанс пошлины при `lobby` — 0,4; единственный вклад пака в `popularity.gain` — `ms.launch_hype` ×1,2 (NFR-7); в `market.rating.cpu` пак не пишет
  - **Depends**: TSK-009, TSK-007

- [x] **TSK-014**: Строки пака `ru` / `en` (авто 2026-10-10)
  - **Requirement**: FR-11, NFR-4
  - **Deliverables**: ключи `ms.*` в `mod/src/PTMod/L10n/ru/market-shocks.json` и `mod/src/PTMod/L10n/en/market-shocks.json`
  - **Acceptance**: у всех 21 записи есть `title`, `text`, `cause`; у вариантов — тексты; у фабричных шоков причина упоминает долю мощностей; предупреждение о штрафах (`ms.warn.contract_fines`); ключи эффектов `ms.fx.*`, каналов `ms.channel.*`, настроек `ms.settings.*`, фичи `ms.feature.*` — в обоих языках; все ключи начинаются с `ms.`
  - **Depends**: TSK-010, TSK-011, TSK-012, TSK-013; mod-framework/TSK-013 (`ModL10n`)

## Фаза 4 — Баланс, настройки, проверка

- [x] **TSK-015**: Симулятор баланса и тесты паритета (авто 2026-10-10)
  - **Requirement**: FR-2, FR-9, FR-10, FR-11, NFR-5
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/MarketShocks/BalanceSimulationTests.cs`, `ParityTests.cs`, `PackContentTests.cs`; отчёт симуляции (частоты, доля игрока, максимумы каналов) в разделе «Evidence» этой задачи
  - **Acceptance**:
    - 50 лет × 20 прогонов: частоты категорий ±25 % от FR-10.1 (AC-10.1);
    - доля игрока = доля мощностей ± 2 п. п. (AC-2.1, AC-9.1);
    - пределы каналов не превышены, AC-10.2 воспроизведён; на «Хаосе» выпуск не ниже 0,4 и память не выше 2,0 (AC-10.3);
    - одиночный шок на «Обычно»: выпуск ≥ 55 %, себестоимость ≤ +60 %;
    - одинаковые числа для игрока и ИИ при равной QR и любой сложности (AC-9.3);
    - новости об ИИ только по топ-5 (AC-2.3, AC-2.4); координация К1 (AC-10.4);
    - `PackContentTests`: ID с префиксом `ms.`, условие К1 у №5–8, №10–12, №14, №16–18.
  - **Depends**: TSK-010, TSK-011, TSK-012, TSK-013; event-system/TSK-006, event-system/TSK-007, event-system/TSK-010, event-system/TSK-017; mod-framework/TSK-067

- [x] **TSK-016**: Настройки пака и выключение посреди игры (авто 2026-10-10)
  - **Requirement**: FR-10, FR-14
  - **Deliverables**: `mod/src/PTMod/Features/MarketShocks/Settings/MarketShocksSettings.cs` (`ParamSpec` `intensity` 0.5 / 1 / 1.5 / 2, `frequency` → `PackOptions`; `ModSettings.Changed`)
  - **Acceptance**:
    - в хабе «Моды»: переключатель фичи, слайдеры интенсивности (до «Хаоса» ×2) и частоты (0,5 / 1 / 1,5);
    - выключение: `event-system` замораживает эффекты пака (`ModFeatures.StateChanged`), ядро снимает патчи фичи (формулы ванильные);
    - включение возвращает неистёкшие эффекты (AC-14.1).
  - **Depends**: TSK-009; event-system/TSK-023; mod-framework/TSK-014 (`ModSettings`), mod-framework/TSK-039 (хаб)

- [x] **TSK-017**: Самопроверка пака (авто 2026-10-10)
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
| TSK-001 Дневной кеш каналов и патч `CpuMath.MaterialCost` | Complete (авто 2026-10-10, кусок 1: `Logic/DailyValueCache.cs` (чистый кеш: одно чтение канала в игровой день, сброс по `Invalidate` и `OnChannelChanged`, быстрый путь без `HasAny`), `Channels/ShockChannels.cs` (адаптер: `ModTicks.Today`, `ModModifiers.HasAny/Get`, подписка на `ModModifiers.ChannelChanged`, `ModTicks.GameReady`, `ModFeatures.StateChanged`; каналы не объявляются), `Logic/ShockCurves.cs` (`MemoryTerm`, `AdjustMaterialCost`), `Patches/MaterialCostPatch.cs`; `ShockCurves` лежит в `Logic/`, а не в `Channels/`, потому что тестовая сборка берёт из фичи только `Logic/**`; патч начнёт действовать после `ctx.Patch` в `MarketShocksFeature.Register` (TSK-009), `ShockChannels.Attach()` и `Invalidate()` в `Register`/`OnEnabled`/`OnDisabled` — тоже там; `IsReserved` → `FeatureFormula` с владельцем `market-shocks` сверено чтением `ReservedTargets` и сигнатуры цели — рефлексией по `_original_en/Assembly-CSharp.dll`; сборка фичи и тесты зелёные; в игре не запускалось) |
| TSK-002 Патчи пластин и описаний | Complete (авто 2026-10-10, кусок 1: `Patches/WaferCostPatches.cs` (`WaferMaterialCost` × `WaferK`, `WaferCost` + `2000·m·(k−1)/yield`, `Optional`), `Patches/MemoryDescriptionPatch.cs` (`Optional`, замена `__result[2]` при `Count ≥ 3` и k ≠ 1 через `ModL10n` `ms.ui.cache_cost`, деньги — `StringFormatter.FloatToMoney` игры), `Logic/ShockCurves.cs` (`WaferMaterial`, `WaferCostDisplay`), `Logic/ShockFormat.cs` (множитель в тексте), строки `ms.ui.cache_cost` в `L10n/{ru,en}/market-shocks.json`; все четыре цели — `FeatureFormula` ядра за `market-shocks`; сигнатуры целей сверены рефлексией; на горячем пути при k = 1 замыканий нет; патчи начнут действовать после `ctx.Patch` (TSK-009); в игре не запускалось) |
| TSK-003 Тесты формул и кеша каналов | Complete (авто 2026-10-10, кусок 1: 52 теста в `mod/tests/PTMod.Tests/Features/MarketShocks/{MemoryTermTests,WaferCostTests,ChannelCacheTests}.cs`: AC-4.1 (130 → 186,4), строки таблицы примеров (+43 %, +53 %, 0 %), AC-8.1, AC-8.2 (3 333 → 5 000), AC-8.3, AC-13.1 (формат множителя), защиты `CacheCostReduction = 0`, `null`, NaN; кеш: одно чтение на день, сброс по `Invalidate` и `ChannelChanged`, быстрый путь, неизвестный день; `[Covers]` на `ShockCurves`, `ShockFormat`, `DailyValueCache`; `python moduild_feature.py market-shocks --tests` — 1166 прошло / 0 упало; запросы интегратору — `mod/requests/market-shocks.md`) |
| TSK-004 `ModifierRamp` | Complete (авто 2026-10-10, кусок 2: `Logic/ShockCurves.cs` (`Trapezoid`, `Ramp`, `TotalDays`, зажим пика пределами канала `ScalePeak`), `Logic/ModifierRampLogic.cs` (`RampSpec` — проверки диапазонов design.md и `Resolve` с интенсивностью, `RampCurve` — сохранённые числа `[peak', rampIn, hold, rampOut]` плюс пятое число-пометка «ограничено» и проверка при чтении из сейва), `Logic/CurveRuntime.cs` (`Apply` / `Tick` / `Restore` — один и тот же расчёт по возрасту эффекта), `Effects/ModifierRampEffect.cs` (`ITimedEffectHandler` + `ICurveEffectHandler`: `Resolve` берёт `LimitMin` / `LimitMax` из `IEffectModifiers`, `Tick` / `Restore` — `Contributions.Set`, `Remove` — `Clear`, `Describe` — `ms.fx.ramp` с датой окончания), `Effects/ContributionSink.cs`, `Effects/ShockText.cs`, `ShockPack.cs` (общий `PackOptions` пака, откуда типы читают интенсивность — его же отдаст `RegisterPack` в TSK-009); строки `ms.fx.*` и `ms.channel.*` в `L10n/{ru,en}/market-shocks.json`; регистрация `EventEffects.Register` — в `Register` фичи (TSK-009); собрано, тесты зелёные, в игре и под движком не запускалось (движок `event-system` ещё не написан, допущения о контракте — `mod/requests/market-shocks.md`, п. 4)) |
| TSK-005 `ModifierWave` | Complete (авто 2026-10-10, кусок 2: `ShockCurves.Wave` / `ScaleAmplitude`, `Logic/ModifierWaveLogic.cs` (`WaveSpec` — `amplitude` 0…0,5, `period` 30…3650, `cycles` 1…3, `phase` 0 / 180 / "random", `phaseGroup`; `WaveCurve` — `[A', period, phaseRad]`; `WavePhaseBook` — общая фаза группы в одном срабатывании), `Effects/ModifierWaveEffect.cs` (одно число из `ctx.Rng` на волну всегда, `durationDays = period · cycles`, амплитуда масштабируется и зажимается местом, которое пределы канала оставляют по обе стороны от 1; `Describe` — `ms.fx.wave` со «сначала рост / спад»); собрано, тесты зелёные, в игре не запускалось) |
| TSK-006 `InventoryLoss` | Complete (авто 2026-10-10, кусок 2: `Logic/InventoryLossLogic.cs` (`LossSpec` — `fraction` числом или `[lo, hi]` в [0; 0,5], бросок и интенсивность; `InventoryLossMath` — `floor(Stock · f)` с защитой от крошки float, план пар `[saveId, lost]`, `ApplyPlan` не опускает склад ниже 0 и пропускает ненайденные CPU; `StockCandidate`, `IStockBook`), `Effects/InventoryLossEffect.cs` + `Effects/GameStockBook.cs` (CPU с `Manufacturer == цель`, `Base == null`, `!IsRetired`; запись только в `CpuBase.MarketData.Stock`; `Describe` — `ms.fx.inventory` с суммой по `Cpu.UnitCost` или `ms.fx.inventory.units` без неё); собрано, тесты зелёные, в игре не запускалось) |
| TSK-007 Предикаты `ms.*` | Complete (авто 2026-10-10, кусок 2: `Logic/ShockPredicateLogic.cs` (`SellsBelowCost` — доля рынка за последний день из метод `MarketShare` интерфейса `IGameState` ≥ `minShare` и CPU в продаже с `SoldLastDay > 0` и `Price < RealUnitCost · priceRatio`; `HasRecentCpu` — `Today.Year − CpuBase.Year ≤ maxAgeYears` у CPU в продаже; значения по умолчанию 0,95 / 0,05 / 1; нет источника, нет доли рынка (`MarketDataProvider.Instance == null`), нет даты, плохие параметры → ложь без исключения; `Validate` для проверок контента), `Conditions/GameCpuSource.cs` (`CpuDataProvider.GetAllCpus`, `RealUnitCost` считается только для CPU в продаже с продажами), `Conditions/ShockPredicates.cs` (`Register()` через `EventConditions.Register`, владелец `market-shocks`); имена проходят `ModIds.IsPredicateNameAllowed`; собрано, тесты зелёные, регистрация сработает после `Register` фичи (TSK-009), в игре не запускалось) |
| TSK-008 Тесты типов и предикатов | Complete (авто 2026-10-10, кусок 2: 135 новых тестов в `mod/tests/PTMod.Tests/Features/MarketShocks/`: `ShockCurvesTests` (AC-7.1, AC-7.2, AC-7.4, таблица интенсивности из design.md, границы трапеции, зажим), `EffectTypeTests` (проверки параметров `ModifierRamp` / `ModifierWave`, `Resolve` на 0,5 / 1 / 1,5 / 2 и «Хаос» с зажимом пределом канала, разбор сохранённых кривых, `Restore` на середине кривой = непрерывный `Tick`, испорченные числа (пик 10, период 0) отбрасывают эффект, общая фаза `phaseGroup`, числа строк), `InventoryLossTests` (AC-7.3: 10 000 и 3 000 при 0,1 → 9 000 и 2 700, «1 300 шт.»; варианты, снятые с продажи и чужие CPU исключены, склад не ниже 0, ненайденный CPU пропущен), `PredicateTests` (AC-6.1: $90 / $96 при $100 и доле 12 %; AC-12.1: 1994 → 1995 да, 1996 нет; источники отсутствуют; имена против `ModIds`; на `FakeGameState`); `[Covers]` на всех публичных типах `Logic`; `python mod/build_feature.py market-shocks --tests` — 1300 прошло / 1 упал (`ct.tender.lost.autoReject` в L10n фичи contract-transparency, не этой фичи), 187 тестов фичи — зелёные) |
| TSK-009 Фича пака и конфиг | Complete (авто 2026-10-10, кусок 3: `MarketShocksFeature.cs` (`[ModFeature]`, `Id = "market-shocks"`, `DependsOn = { "event-system" }`, `EnabledByDefault`/`SupportsHotToggle` = true, ключи `ms.feature.*`; `Register` в порядке design.md: три типа эффектов через `EventEffects.Register`, два предиката (`ShockPredicates.Register`), пак (`EventRegistry.RegisterPack` с `ShockPack.Options`: `EventsFile = "shocks"`, `RivalNewsTopN = 5`, `SettingsFeatureId = "market-shocks"`), три класса патчей (`ctx.Patch`), параметры `intensity` (Float 0,5…2, шаг 0,5) и `frequency` (0,5…1,5, шаг 0,5) с переносом значений в `Severity`/`Frequency` пака на месте (`ModSettings.Changed`), `ShockChannels.Attach()`; `ModModifiers.DeclareChannel` не вызывается (тест сканирует исходники); `OnEnabled`/`OnDisabled` сбрасывают кеш каналов; строка лога AC-1.1 `pack registered: 21 events, 3 effect types, 2 predicates` по числу событий пака в `EventRegistry.All` на `GameReady` (если движок ещё не загрузил паки — через кадр); `Logic/ShockRegistration.cs` (`ShockRegistration`, `ShockParamSpecs`: чистые списки и параметры, которые читает класс фичи), `Content/market-shocks/config.json` (6 категорий и 6 `channelLimits` как в design.md); `event-system` недоступна → фича `Unavailable` по правилу `DependsOn` ядра (проверено на `FeatureStateMachine`), патчи не ставятся; перемотка нейтральна: каналы = 1, хуки выходят сразу; тесты `ShockRegistrationTests` (11) и `ShockCatalogTests.Config_*` (3); `python mod/build_feature.py market-shocks --tests` — 231 тест фичи зелёные; в игре и под настоящим движком не запускалось) |
| TSK-010 Контент: фабрики и отрасль | Complete (авто 2026-10-10, кусок 3: записи №1–7 в `Content/market-shocks/shocks.json` (`ms.fab_fire` по примеру design.md, `ms.cleanroom_contamination`, `ms.grid_failure`, `ms.litho_breakdown`, `ms.earthquake`, `ms.photoresist_shortage`, `ms.logistics_crunch`): числа каталога, цели `RandomCompany` (вес `FactoryCapacity`, фильтр `es.hasFab` и `not es.activeInCategory(ms.supply)`) и `AllCompanies` (`es.hasFab`; №7 — `cpuOnMarket`), условие К1 у №5–7, `important` у №1 и №4, `notifyPlayer` у шоков одной ИИ-компании, `aiMinCashMultiple` 3 у `expedite` и `rent`; записи прошли настоящий `DefinitionMapper` + `ContentValidator` движка (одноразовая сборка вне репозитория: 21 событие, 0 предупреждений); `ShockCatalogTests` (23 теста) сверяет каждое число с каталогом) |
| TSK-011 Контент: цены | Complete (авто 2026-10-10, кусок 3: записи №8–13 (`ms.dram_spike_warning`, `ms.dram_spike`, `ms.dram_glut`, `ms.wafer_shortage`, `ms.energy_crisis`, `ms.memory_cycle`): предупреждение планирует `ms.dram_spike` через 21 день с шансом 0,8 (глобальный `FireEvent`, `target: Player`), выбор `hedge` (`aiMinCashMultiple` 4) / `wait`, кривые `ModifierRamp` по каталогу (1,6 — 30/90/90; 0,7 — 30/120/90; пластины 1,6 — 20/60/60; расходы фабрик 1,25 — 30/90/60), `ms.memory_cycle` — `Condition` раз в месяц, откат 1460 = длина волны, `ModifierWave` ±15 %, фаза `random`; К1 у №8, №10–12; тесты: даты AC-4.2 и AC-11.1, кривая AC-7.1 на данных файла) |
| TSK-012 Контент: спрос и циклы | Complete (авто 2026-10-10, кусок 3: записи №14–19 (`ms.recession_warning`, `ms.recession`, `ms.pc_boom`, `ms.server_boom`, `ms.embedded_boom`, `ms.silicon_cycle`): только каналы `market.demand.<segment>`, годы бумов через `dateAtLeast`/`dateBefore` (`ms.pc_boom` 1981–2005, `ms.server_boom` с 1990, `ms.embedded_boom` с 1978), рецессия через 45–75 дней с шансом 0,85 (выбор `cut_costs`/`hold`, веса ИИ 1/1), общая фаза `phaseGroup: silicon` и `phase: random` у четырёх волн, откат цикла = его длина (1825), К1 у №14, №16–18; AC-5.1 (даты 1991-04-16 / 10-13 / 1992-02-10) и AC-5.2 (2010 — условие ложно) проверены тестами на кривых пака и разборе условий ядра) |
| TSK-013 Контент: торговые и ИИ | Complete (авто 2026-10-10, кусок 3: записи №20–21 (`ms.dumping_complaint`: цель `RandomCompany` по доле рынка с фильтром `ms.sellsBelowCost`, выбор `accept_duty`/`lobby`, пошлина при `lobby` с шансом 0,4, `important`; `ms.launch_hype`: фильтр `ms.hasRecentCpu`, `Popularity` +0,10 новейшему CPU и `popularity.gain` ×1,2 на 60 дней); проверка политик ИИ по всем 10 выборам пака: `aiWeight` задан у каждого, `aiMinCashMultiple` ровно у выборов с тратой денег (3 / 3 / 4 / 3); единственный вклад в `popularity.gain` — `ms.launch_hype`, в `market.rating.cpu` пак не пишет (тесты NFR-7)) |
| TSK-014 Строки ru/en | Complete (авто 2026-10-10, кусок 3: `L10n/{ru,en}/market-shocks.json` (путь вместо `L10n/ru.json` из-за раскладки ядра): `title`/`text`/`cause` всех 21 записи, 10 текстов выборов, `ms.warn.contract_fines` (и та же фраза в текстах пяти шоков со снижением выпуска), причины фабричных шоков с долей мировых мощностей, `ms.feature.*`, `ms.settings.*` (подписи, подсказки, названия уровней «Мягко/Обычно/Жёстко/Хаос»), `ms.fx.*` (добавлен `ms.fx.forecast_missed`), `ms.channel.*`; соглашение об аргументах: `{0}` — название компании-цели, глобальные шоки без аргументов; `ShockTextTests` (10): ключи записей и кода есть в обоих языках, плейсхолдеры совпадают, все ключи с `ms.`) |
| TSK-015 Симулятор баланса | Complete (авто 2026-10-10, кусок 4: `ShockSimulator.cs` — модель правил `event-system` поверх настоящих `config.json` и `shocks.json` (лотерея из трёх чисел в день, `minGapDays` / `maxActive`, откаты на компанию и `*` у групп, групповые экземпляры, `Condition` раз в месяц, `FireEvent` с задержкой и шансом, деньги по формуле движка, масштаб и зажим множителей при `Resolve`, агрегатор каналов с пределами пака; игрок ждёт таймер и берёт `defaultChoice`, ИИ выбирает по `aiWeight` / `aiMinCashMultiple`) и тесты в `Features/MarketShocks/`: `BalanceSimulationTests` (18), `ParityTests` (12), `PackContentTests` (11). **Evidence** (20 прогонов × 50 лет с 1971-01-01, «Обычно», частота 1; срабатываний лотереи в год, в скобках FR-10.1): снабжение 1,34 (1,3), отрасль 0,34 (0,4), цены 0,85 (0,7), спрос 0,33 (0,4), торговля 1,08 (1,0); циклы 0,42 (память раз в 4 года с 1976, кремний раз в 5 лет с 1980, без разрывов); все в ±25 % и на 50 годах, и на 1990–2020; частота 0,5 / 1 / 1,5 даёт для снабжения 0,72 / 1,34 / 1,82. Доля игрока среди 13 051 инцидента `ms.supply` (200 прогонов) — 19,0 % при 20 % линий; игрок с 5 из 100 линий — 6,0 %; каждая из десяти компаний в ±2 п. п. от своей доли линий. Пределы на 0,5 / 1 / 1,5 / 2: выпуск не ниже 0,72 / 0,47 / 0,40 / 0,40 (до зажима 0,31 и 0,28), цена памяти не выше 1,60 / 2,00 / 2,00 / 2,00 (до зажима 2,36 на «Обычно», 3,31 и 4,06), пластины не выше 1,30 / 1,60 / 1,90 / 2,00, спрос 0,60…1,60, расходы фабрик не выше 1,80; AC-10.2: 1,6 × 1,3 = 2,08 → 2,0 с пометкой; AC-10.3: на «Хаосе» пожар даёт выпуск 0,4 («ограничено»), суммы вдвое. Одиночный шок на «Обычно»: у всех 21 записей при обоих вариантах выбора выпуск ≥ 0,55 и себестоимость ≤ ×1,6. AC-9.3: суммы, множители и длительности шести шоков на одну компанию (четыре инцидента, пошлина, «хайп») одинаковы у игрока и ИИ при равной выручке на четырёх интенсивностях; в коде фичи нет `Difficulty` / `IsPlayer` / `CompanyKey.Player`, в контенте нет `difficultyAtLeast`. Новости об ИИ (AC-2.3, AC-2.4) — только ранги 1–5, журнал хранит всех; `notifyPlayer` только у шоков на одну случайную компанию. К1 (AC-10.4): 0 глобальных случайных шоков внутри окна исторического события (30 прогонов × 10 лет), снаружи те же шоки срабатывают, удар по прогнозу, данному до окна, не отменяется. `PackContentTests`: 21 запись, `ms.`, К1 ровно у №5–8, №10–12, №14, №16–18 (по тексту условий и по истинности при идущем историческом событии), `FireEvent` указывают на `Manual`-удары, выбор имеет таймер, `defaultChoice` и политику ИИ. **Баланс**: `ms.industry.dailyChance` 0,0018 → 0,0035 (`config.json`, `ShockCatalogTests`): по таблице категория давала 0,28 в год (< 0,30), потолок откатов 0,43; `design.md` нужно обновить (`mod/requests/market-shocks.md`, п. 7.1). Модель один раз сверена с настоящим движком вне репозитория (`PackCatalog` + `EventScheduler` + `BuiltinPredicates`): 21 запись принята, 0 пропущено, 0 предупреждений; 200 × 50 лет — 1,29 / 0,34 / 0,83 / 0,33 / 1,05, доля игрока 18,5 % (п. 7.2). Мутационные проверки (К1 снята у `ms.pc_boom`, выпуск пожара 0,5, шанс 0,0018, предел выпуска 0,1, `difficultyAtLeast` в условии) роняют свои тесты. В игре не запускалось) |
| TSK-016 Настройки пака | Complete (авто 2026-10-10, кусок 4: `Settings/MarketShocksSettings.cs` — `Declare(ctx)` (два `ParamSpec` из `ShockParamSpecs`: `intensity` 0,5 / 1 / 1,5 / 2 и `frequency` 0,5 / 1 / 1,5, страница хаба `ctx.HubPage` со строкой «Сейчас: интенсивность «Хаос» (×2), частота ×1,5»), `Apply()` кладёт значения в `ShockPack.Options.Severity` / `.Frequency` при объявлении, в `OnEnabled` и по каждому `ModSettings.Changed` двух ключей, `Attach()`; значения вне сетки прилипают к ближайшему уровню (`ShockParamSpecs.SnapIntensity` / `SnapFrequency`, `SliderMath.Snap` ядра); логика настроек из `MarketShocksFeature` перенесена в этот класс; строки `ms.settings.title`, `ms.settings.now` в `ru` и `en`; выключение посреди шока: `PackOptions.SettingsFeatureId = market-shocks` (движок читает `ModFeatures.IsActive`), патчи снимает ядро, `OnDisabled` / `OnEnabled` сбрасывают кеш каналов; тесты `ShockSettingsTests` (10: уровни и защита от мусора, строки страницы в двух языках, проводка в исходниках) и `FreezeThawTests` (7: AC-14.1 на заменителе движка — на следующий день после выключения себестоимость 130, при включении кривая идёт с текущего дня (186,4 на плато, 1,33 на спаде), дата окончания прежняя, истёкший шок не возвращается, выключение и включение в любой день шока оставляет канал на кривой); не запускалось ни в игре, ни под настоящей фабрикой экземпляров `event-system` (её ещё нет): замораживание самих эффектов и `Restore` при включении — её дело, допущения в `mod/requests/market-shocks.md`, п. 7.3) |
| TSK-017 Самопроверка | Complete (авто 2026-10-10, кусок 4: `SelfCheck/MarketShocksSelfCheck.cs` собирает факты из игры и ядра (цели четырёх патчей через рефлексию по точным типам параметров, `ModHooks.IsReserved` — `FeatureFormula` с владельцем `market-shocks`, `ModHooks.Report` — патч объявлен и применён; `ModModifiers.IsDeclared` для `production.memoryCost`, `production.waferCost` и каналов записей пака; `EventEffects.RegisteredTypes`, `ModConditions.KnownTypes`, `ModIds`; события пака в `EventRegistry.All`; ключи в внутренний метод `Keys` класса `ModL10n` для обоих языков), правила — чистый `Logic/ShockSelfCheck.cs` (`Evaluate`, `ShockCheckFacts`, `ShockCheckVerdict`, `ExpectedEventIds`, `PatchTargets`, `CodeKeys`); запускается из `MarketShocksFeature.ReportPack` на `GameReady` после чтения пака; итог одной строкой `selfcheck OK: 21 events, 4 patches` или строка на находку и `selfcheck found N problems: ...`; необязательный патч, которого нет в игре, и недостающая строка — предупреждения, остальное — ошибки; тесты `ShockSelfCheckTests` (29: норма, каждая неисправность отдельно с уровнем и местом, обрезка длинных списков, привязка списков к файлам — цели патчей к `[ModPatch]` в `Patches/`, 21 id к `shocks.json`, каналы к `core.json`, ключи кода к таблицам, весь пак на реальных файлах); в игре не запускалось; в ядре нет входа для находок фич во вкладку «Диагностика» — они только в логе, просьба в `mod/requests/market-shocks.md`, п. 7.4) |
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
