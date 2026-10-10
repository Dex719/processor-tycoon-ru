# Requirements: market-shocks

## Document Information

- **Feature Name**: market-shocks — рыночные шоки: аварии фабрик, скачки цен на память и сырьё, циклы спроса
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Stakeholders**:
  - владелец проекта (одобрение, баланс, ручная проверка);
  - игроки Processor Tycoon RU;
  - автор `event-system` (движок и API);
  - автор `mod-framework` (каналы `ModModifiers`, реестр каналов, таблица патчей);
  - авторы `historical-events` (могут использовать новые каналы и типы эффектов; координация по категории `history`) и `rival-personalities` (поведение ИИ в шоках);
  - автор `gpu-product-line` (шоки пластин действуют на GPU).
- **Идея**: I12 «Market shocks: fab incidents, memory spikes and commodity cycles» (`pt-modding/IDEAS.md`), спрос medium, fun 4/5, усилие L.
- **Источники (из IDEAS.md)**:
  - <https://vgtimes.com/games/game-dev-tycoon/files/top-files/>
  - <https://steamcommunity.com/sharedfiles/filedetails/changelog/3291115942>
  - <https://cubiq.ru/luchshie-mody-dlya-game-dev-tycoon/>
  - <https://www.cnbc.com/amp/2021/02/10/whats-causing-the-chip-shortage-affecting-ps5-cars-and-more.html>
  - <https://econ.st/3FmuqFt>
  - <https://tedium.co/2016/11/24/1988-ram-shortage-history/>
  - <https://www.csmonitor.com/layout/set/amphtml/1988/0328/fchip.html>
  - <https://www.bgr.com/2210328/80s-ram-oil-crisis-explained/>
  - <https://itch.io/t/214142/planned-features>
- **Связанные документы**: `.kiro/specs/event-system/` (движок, API), `.kiro/specs/mod-framework/design.md` (API ядра v0.2, таблица патчей F1/F2), `.kiro/steering/context.md`, `.kiro/steering/owner-decisions.md`, решения `0003`–`0006`; `design.md` и `tasks.md` этой спеки.

## Introduction

В Processor Tycoon рынок предсказуем. Спрос растёт по десятилетним множителям `PopulationData.GetCurrentSize`, себестоимость меняется только от технологий, фабрики работают без сбоев. Реальная полупроводниковая индустрия живёт циклами и авариями:
- дефицит DRAM 1988 года;
- пожары и землетрясения на фабриках;
- энергетические кризисы;
- бумы ПК и серверов и рецессии после них.

Спека — **контент-пак** для движка `event-system`: 19 шоков (21 запись JSON с предупреждениями) с балансом и частотами. Плюс три новых типа эффектов (`ModifierRamp`, `ModifierWave`, `InventoryLoss`), два канала `ModModifiers` (`production.memoryCost`, `production.waferCost`), которые объявляет реестр ядра (владелец — эта фича) и читают её патчи, и два предиката условий (`ms.sellsBelowCost`, `ms.hasRecentCpu`). Шоки бьют по ИИ-соперникам по тем же правилам, что и по игроку. Авария у конкурента — это шанс для игрока, а глобальный шок — общая проблема, к которой можно подготовиться по предупреждению.

Версия 0.2 подгоняет спеку под ядро `mod-framework` v0.2 и решения владельца: каналы памяти и пластин объявлены в реестре ядра до `Register`; патчи на `CpuMath.MaterialCost`, `ProductionMath.WaferMaterialCost`, `ProductionMath.WaferCost` и `Memory.GetDescriptions` — «формулы фичи» (цель закреплена за `market-shocks`); предикаты получили префикс `ms.`; добавлен уровень интенсивности «Хаос» ×2; новости о шоках у ИИ — только по топ-5.

### Feature Summary

Пак рыночных шоков для `event-system`: инциденты на фабриках, отраслевые сбои, скачки цен на память, пластины и энергию, бумы, рецессии и циклы спроса, торговые шоки — с предупреждениями, выбором подготовки и одинаковым действием на игрока и ИИ.

### Business Value

- **Для игрока:**
  - середина партии перестаёт быть рутиной;
  - важны запас денег, диверсификация сегментов и реакция на предупреждения;
  - авария у соперника открывает окно для захвата доли рынка.
- **Для проекта:** первый крупный потребитель API `event-system`. Проверяет расширяемость движка новыми типами эффектов и каналами.
- **Доказательства спроса:**
  - I12 в `IDEAS.md`: спрос medium, fun 4/5;
  - паки событий — среди самых скачиваемых модов похожих тайкунов (vgtimes, cubiq);
  - идея опирается и на тему «Planned features» игры на itch.io (itch.io/t/214142), указанную в `IDEAS.md`;
  - исторический материал о шоках памяти и чипов (CNBC, Economist, Tedium, CSMonitor, BGR).

### Scope

**Входит:**
- 19 шоков в 5 лотерейных категориях и 1 категории циклов (таблица в `design.md`);
- новые типы эффектов `ModifierRamp`, `ModifierWave`, `InventoryLoss`;
- читающие патчи каналов `production.memoryCost` и `production.waferCost` (сами каналы объявляет ядро, владелец — `market-shocks`);
- предикаты `ms.sellsBelowCost`, `ms.hasRecentCpu`;
- правила темпа, пределы наложения, ручка интенсивности («Мягко» ×0,5 … «Хаос» ×2), координация с `historical-events` по категории `history`;
- отображение изменённых цен памяти и пластин в описаниях технологий игры;
- строки `ru` / `en`.

**Не входит:**
- сам движок событий, UI выбора, журнал, сохранение экземпляров — `event-system`;
- патчи стандартных каналов (`production.output`, `market.demand.*`, `production.unitCost`, `factory.operatingCost`, `cpu.failureRate`, `popularity.gain`, `research.speed`) — `mod-framework`;
- реальные исторические события с датами (дефицит DRAM 1988, землетрясение на Тайване 1999 и т. д.) — `historical-events`; здесь шоки обобщённые и случайные;
- криптобумы 2017 и 2021 годов и вообще спрос на GPU (`gpu.demand`, `gpu.demand.*`) — `historical-events` и `gpu-product-line` (решение владельца); шоки спроса этого пака затрагивают только CPU-сегменты `market.demand.<segment>`;
- новый «товарный» сегмент рынка с демпингом из I12 — отдельная XL-задача, не планируется;
- реакции соперников на игрока (в т. ч. снижение цен) — `rival-personalities`; исторические ценовые войны, антимонопольные дела, экспортный контроль и реальные торговые соглашения — `historical-events`;
- брак в выпущенных CPU — `design-flaw-recalls`.

## Каналы ModModifiers, которые читает пак

Оба канала объявлены в реестре ядра (`core.json`, владелец — `market-shocks`) до вызова `Register` любой фичи (`mod-framework` FR-12 п. 15). Эта фича их не объявляет (`ModModifiers.DeclareChannel` не вызывается).

| Канал | Область | Читающий патч (эта спека, «формула фичи») | Что меняет в игре |
|---|---|---|---|
| `production.memoryCost` | глобальный (вся индустрия), предохранитель [0,5; 2,0] | postfix `CpuMath.MaterialCost` | Член памяти в себестоимости CPU: `(2.35·L1 + L2/1.25 + L3) · Memory.UnitCostPerCache / ProcessNode.CacheCostReduction` умножается на k |
| `production.waferCost` | глобальный, предохранитель [0,6; 2,0] | postfix `ProductionMath.WaferMaterialCost`; postfix `ProductionMath.WaferCost` (отображение) | Стоимость материала пластины 2000 · m умножается на k |

Каналы глобальные, потому что `CpuMath.MaterialCost` и `ProductionMath.WaferMaterialCost` не получают компанию. Целевые методы закреплены за этой фичей как `FeatureFormula` (`ModHooks.IsReserved`): патч фичи-владельца принимается, патч любой другой фичи отклоняется (таблица патчей ядра, строки F1 и F2). Каналы открыты для записи другим фичам (например, `historical-events` пишет в них через `event-system`). `ProductionMath.WaferMaterialCost` вызывается также из `Factory.ProduceWafers`, `CpuMath.RealUnitCost` и производства GPU (`gpu-product-line`), поэтому шоки пластин действуют и на GPU.

## Requirements

### FR-1: Пак шоков и регистрация в движке

**User Story:** Как игрок, я хочу, чтобы рыночные шоки подключались как отдельный пак и выключались независимо от других событий, чтобы выбирать, насколько живым будет рынок.

**Priority:** Must

#### Acceptance Criteria

1. WHEN мод запускается и фича `market-shocks` (`[ModFeature] sealed class MarketShocksFeature : IModFeature`, `DependsOn = { "event-system" }`) включена THEN market-shocks SHALL в своём `Register` зарегистрировать типы эффектов `ModifierRamp`, `ModifierWave`, `InventoryLoss`, предикаты `ms.sellsBelowCost`, `ms.hasRecentCpu`, пак `market-shocks` (`EventRegistry.RegisterPack` с `EventsFile = "shocks"`, `RivalNewsTopN = 5`, `SettingsFeatureId = "market-shocks"`) и свои патчи; контент пака движок грузит позже, на первом `ModTicks.GameReady`.
2. The пак SHALL содержать 19 шоков (21 запись) в категориях `ms.supply`, `ms.industry`, `ms.price`, `ms.demand`, `ms.trade`, `ms.cycle`; ID записей — `ms.<имя>` (`ModIds.IsEventIdAllowed`).
3. IF `event-system` выключен или не загрузился THEN market-shocks SHALL стать `Unavailable` (зависимость, правило реестра фич ядра) и записать причину в лог; её патчи не ставятся, поэтому себестоимость CPU и цена пластин остаются ванильными.
4. WHEN контент пака загружается THEN каждая запись SHALL проходить валидацию `event-system` и проверки типов пака; битая запись пропускается.
5. WHILE идёт перемотка новой игры market-shocks SHALL быть нейтральна: шоков, писем и эффектов нет (движок не обрабатывает дни), каналы памяти и пластин равны 1 (общая политика владельца).

**Сценарии:**
- **AC-1.1** Given мод установлен, When наступает первый `GameReady`, Then в логе `[PTMod] [market-shocks] INFO pack registered: 21 events, 3 effect types, 2 predicates`.
- **AC-1.2** Given `event-system` выключен в настройках, When игра запускается, Then `market-shocks` в `Unavailable` с текстом «нужна фича event-system», пак не зарегистрирован, себестоимость CPU не меняется.
- **AC-1.3** Given новая игра 1990 года, When перемотка завершена, Then журнал пуст, активных шоков нет, `production.memoryCost` = 1.

**Complexity:** Low
**Dependencies:** event-system (FR-1, FR-6); mod-framework (`ModFeatures`, `ModContent`, `ModSettings`, `ModIds`, `ModHooks`).
**Assumptions:** реестр фич ядра регистрирует `event-system` раньше зависящей от неё фичи (`IModFeature.DependsOn`); движок грузит контент паков после `Register` всех фич; каналы объявлены ядром.

### FR-2: Инциденты на фабриках компаний

**User Story:** Как игрок, я хочу, чтобы фабрики иногда выходили из строя — у меня и у соперников, — чтобы держать запас мощностей и денег и пользоваться чужими авариями.

**Priority:** Must

#### Acceptance Criteria

1. WHEN лотерея категории `ms.supply` выбирает шок THEN market-shocks SHALL выбрать цель среди всех компаний с фабрикой (`es.hasFab`), включая игрока, с весом по числу линий (`Factory.ProductionCapacity`).
2. The пак SHALL содержать шоки:
   - `ms.fab_fire` — пожар, выбор «ускоренный ремонт / обычный»;
   - `ms.cleanroom_contamination` — потеря части запасов и рост брака;
   - `ms.grid_failure` — авария энергосети;
   - `ms.litho_breakdown` — поломка литографа, выбор «аренда / ожидание».
3. WHEN шок снижает выпуск THEN market-shocks SHALL использовать стандартный эффект `FactoryOutput` (канал `production.output`) с фиксированной длительностью, а не снятие линий `Company.RemoveProductionLines`.
4. WHEN цель шока — игрок и у шока есть выбор THEN шок SHALL быть `important` (модальное окно с паузой).
5. WHEN цель — ИИ THEN игрок SHALL получить новость (`notifyPlayer`, письмо `Low`), если включена настройка «Новости о конкурентах» и цель входит в топ-5 ИИ-компаний по доле рынка (FR-11 п. 6).

**Сценарии:**
- **AC-2.1** Given у игрока 30 линий из 150 у всех компаний с фабриками, When в симуляции 10 000 инцидентов `ms.supply`, Then игрок — цель в 20 % ± 2 %.
- **AC-2.2** Given пожар у игрока, выбран «Обычный ремонт», When проходит 35 дней, Then выпуск фабрики все 35 дней ×0,55 и затем возвращается к норме; списана сумма ремонта из письма.
- **AC-2.3** Given пожар у соперника «X» из топ-5 по доле рынка, When он срабатывает, Then игрок получает новость «Пожар на фабрике X: выпуск −45 % на 14–35 дней».
- **AC-2.4** Given пожар у соперника, занимающего 8-е место по доле рынка, When он срабатывает, Then новости игроку нет, запись в журнале есть.

**Complexity:** Medium
**Dependencies:** FR-7, FR-9; event-system (`RandomCompany`, вес `FactoryCapacity`, `es.hasFab`, `PackOptions.RivalNewsTopN`); mod-framework (каналы `production.output`, `cpu.failureRate`, `factory.operatingCost`).
**Assumptions:** `production.output` компании X действует на пластины фабрики X, включая CPU клиентов-fabless: патч P2 `mod-framework` на `Factory.ProduceWafers` берёт множитель по `Factory.Company` (mod-framework/design.md). Стоимость производства при этом не снижается (считается от валового выпуска), поэтому авария — это ещё и потерянные деньги.

### FR-3: Отраслевые сбои поставок

**User Story:** Как игрок, я хочу редкие крупные сбои, затрагивающие всю отрасль, чтобы чувствовать, что рынок — общая система.

**Priority:** Should

#### Acceptance Criteria

1. The пак SHALL содержать шоки категории `ms.industry` с целью `AllCompanies`:
   - `ms.earthquake` — выпуск −15 % на 21 день у всех фабрик, ремонт и глобальный рост цены памяти;
   - `ms.photoresist_shortage` — себестоимость +10 % и выпуск −7 % на 90 дней у компаний с фабрикой;
   - `ms.logistics_crunch` — себестоимость +6 % на 120 дней у всех компаний с CPU на рынке, с 1985 года.
2. WHEN отраслевой шок срабатывает THEN market-shocks SHALL применять эффекты на компанию к каждой подходящей компании, а глобальные — один раз (правило групп `event-system`).

**Сценарии:**
- **AC-3.1** Given 9 компаний с фабрикой, When срабатывает `ms.earthquake`, Then у каждой из 9 активен `production.output` ×0,85 на 21 день, а вклад в `production.memoryCost` один (не девять).

**Complexity:** Low
**Dependencies:** FR-7; event-system (`AllCompanies`, `scope: Global`).
**Assumptions:** нет.

### FR-4: Ценовые шоки памяти, пластин и энергии

**User Story:** Как игрок, я хочу, чтобы цены на память, кремний и энергию менялись волнами и меня предупреждали заранее, чтобы планировать цены и запасы.

**Priority:** Must

#### Acceptance Criteria

1. The пак SHALL содержать ценовые шоки:
   - `ms.dram_spike_warning` → `ms.dram_spike` — память до ×1,6;
   - `ms.dram_glut` — память до ×0,7;
   - `ms.wafer_shortage` — пластины до ×1,6;
   - `ms.energy_crisis` — расходы фабрик до ×1,3;
   - `ms.memory_cycle` — волна цены памяти ±15 % с периодом 4 года.
2. WHEN срабатывает `ms.dram_spike_warning` THEN market-shocks SHALL разослать предупреждение всем компаниям с CPU на рынке и запланировать `ms.dram_spike` через 21 день с вероятностью 0,8.
3. WHERE игрок получил предупреждение market-shocks SHALL дать выбор: «Закупить память впрок» (сейчас 2–3 % квартальной выручки, затем `production.unitCost` ×0,95 на 180 дней) или «Ждать» (по умолчанию).
4. WHEN действует шок цены памяти THEN себестоимость каждого CPU SHALL расти пропорционально доле памяти в его материальной стоимости (канал `production.memoryCost`), а не на фиксированный процент.
5. WHEN ценовой шок заканчивается THEN множитель канала SHALL вернуться к 1,0 плавно (спад кривой), без скачка.

**Сценарии:**
- **AC-4.1** Given CPU с L1 = 2, L2 = 0, L3 = 0, памятью DRAM (`UnitCostPerCache` 20), `CacheCostReduction` = 1 и `BaseUnitCost` пакета 40 при брака 10 %, When множитель памяти 1,6, Then `CpuMath.MaterialCost` = 40·0,9 + 94·1,6 = 186,4 вместо 130.
- **AC-4.2** Given предупреждение 1988-03-01, When игрок выбрал «Закупить впрок», Then списана сумма из письма, `production.unitCost` игрока ×0,95 до 1988-08-28; с вероятностью 0,8 на 1988-03-22 начинается рост цены памяти.

**Complexity:** Medium
**Dependencies:** FR-7, FR-8; event-system (`FireEvent`, `chance`, `AllCompanies` с выбором).
**Assumptions:** канал `production.unitCost` `mod-framework` действует на себестоимость CPU компании (только на фактические траты, решение владельца).

### FR-5: Шоки и циклы спроса

**User Story:** Как игрок, я хочу бумы и рецессии в сегментах рынка, чтобы диверсификация продуктов имела смысл.

**Priority:** Must

#### Acceptance Criteria

1. The пак SHALL содержать шоки спроса:
   - `ms.recession_warning` → `ms.recession` — спрос до ×0,8 / ×0,8 / ×0,85 / ×0,9 по сегментам High End / Mid Range / Low End / Industries;
   - `ms.pc_boom` — Mid Range ×1,25 и Low End ×1,2, только 1981–2005;
   - `ms.server_boom` — High End ×1,3, с 1990;
   - `ms.embedded_boom` — Industries ×1,3, с 1978;
   - `ms.silicon_cycle` — волна спроса ±8 % (Industries ±5 %) с периодом 5 лет.
2. WHEN действует шок спроса THEN market-shocks SHALL менять спрос только через стандартные каналы `market.demand.<segment>` (`highend`, `midrange`, `lowend`, `industries`; имена сегментов — как в `ModChannels.MarketDemand`).
3. WHERE игрок получил `ms.recession_warning` market-shocks SHALL дать выбор: «Сократить издержки» (`factory.operatingCost` ×0,92 и `research.speed` ×0,9 на 365 дней) или «Держать курс» (по умолчанию).
4. The шоки спроса SHALL иметь форму трапеции (нарастание, плато, спад), волны — целое число периодов, начиная и заканчивая на 1,0.

**Сценарии:**
- **AC-5.1** Given `ms.recession` началась 1991-02-15, When наступает 1991-04-16 (60 дней), Then множитель High End 0,8; на 1991-10-13 (240 дней) спад начинается; на 1992-02-10 (360 дней) множитель 1,0 и эффект закрыт.
- **AC-5.2** Given новая игра 2010 года, When идут годы, Then `ms.pc_boom` не срабатывает (условие ≤ 2005).

**Complexity:** Medium
**Dependencies:** FR-7; mod-framework (каналы `market.demand.*`, патч `PopulationData.GetCurrentSize`).
**Assumptions:** нет.

### FR-6: Торговые и рыночные шоки

**User Story:** Как игрок, я хочу последствий за демпинг и неожиданных всплесков интереса к новинкам, чтобы рынок реагировал не только на цифры производительности.

**Priority:** Should

#### Acceptance Criteria

1. The пак SHALL содержать `ms.dumping_complaint`: цель — компания, у которой CPU продаётся дешевле 95 % реальной себестоимости (`Cpu.get_RealUnitCost`) при доле рынка ≥ 5 % (`ms.sellsBelowCost`). Выбор:
   - «Принять пошлину» (по умолчанию): `production.unitCost` ×1,12 на 365 дней;
   - «Лоббировать»: 2–3 % квартальной выручки, затем пошлина с вероятностью 0,4.
2. The пак SHALL содержать `ms.launch_hype`: цель — компания с CPU не старше 1 года (`ms.hasRecentCpu`), вес по доле рынка; популярность новейшего CPU +0,10 и `popularity.gain` ×1,2 на 60 дней.
3. WHEN торговый шок касается игрока с выбором THEN шок SHALL быть `important`.

**Сценарии:**
- **AC-6.1** Given игрок продаёт CPU по $90 при `RealUnitCost` $100 и доле 12 %, When срабатывает лотерея `ms.trade` и выбрана жалоба, Then игрок — допустимая цель; при цене $96 — нет.

**Complexity:** Medium
**Dependencies:** FR-12; event-system.
**Assumptions:** границы с `historical-events` (реальные торговые соглашения, антимонопольные дела, экспортный контроль) — решение владельца: общая антидемпинговая жалоба здесь, реальные дела там.

### FR-7: Новые типы эффектов

**User Story:** Как автор контента, я хочу кривые нарастания и спада, волны и потерю запасов как типы эффектов, чтобы шоки выглядели реалистично и были переиспользуемы.

**Priority:** Must

#### Acceptance Criteria

1. WHEN эффект `ModifierRamp` активен THEN market-shocks SHALL каждый день выставлять множитель канала по трапеции: `m(t) = 1 + (peak − 1) · r(t) · s`, где `r` растёт 0→1 за `rampIn` дней, держится `hold` дней, падает 1→0 за `rampOut` дней, `s` — интенсивность.
2. WHEN эффект `ModifierWave` активен THEN market-shocks SHALL выставлять `m(t) = 1 + A · s · sin(2π·t/P + φ)`, `φ ∈ {0, π}`, длительность = `P · cycles`; эффекты с общим `phaseGroup` в одном экземпляре SHALL иметь одну фазу.
3. WHEN применяется `InventoryLoss` THEN market-shocks SHALL списать долю `f` запаса (`CpuBase.MarketData.Stock`) у всех не снятых с продажи CPU, произведённых фабрикой цели (`Cpu.get_Manufacturer`), кроме вариантов (`Cpu.Base != null`). Число списанных единиц SHALL вычисляться при `Resolve` и сохраняться.
4. The типы SHALL поддерживать `scope` `Target` и `Global`, работать через агрегатор `event-system` и восстанавливаться после загрузки (`Restore`) по сохранённым параметрам кривой.
5. IF параметры вне диапазонов (peak ∉ [0,3; 3], `rampIn + hold + rampOut` ∉ [1; 1500], A ∉ [0; 0,5], P ∉ [30; 3650], f ∉ [0; 0,5]) THEN запись SHALL отклоняться валидатором.
6. WHEN интенсивность масштабирует пик или амплитуду THEN market-shocks SHALL зажать итоговое значение пределами канала пака (`channelLimits`) при `Resolve`, чтобы показанное в письме число совпадало с применённым.

**Сценарии:**
- **AC-7.1** Given `ModifierRamp` peak 1,6, rampIn 30, hold 90, rampOut 90, When t = 15, 30, 120, 165, 210, Then m = 1,3; 1,6; 1,6; 1,3; 1,0 (эффект закрыт).
- **AC-7.2** Given `ModifierWave` A = 0,15, P = 1460, φ = 0, When t = 365, 730, 1095, Then m = 1,15; 1,0; 0,85.
- **AC-7.3** Given у цели два CPU с запасом 10 000 и 3 000 и f = 0,1, When применяется `InventoryLoss`, Then запасы 9 000 и 2 700, в журнале «Списано 1 300 шт.».
- **AC-7.4** Given `ModifierRamp` peak 1,6 на `production.memoryCost` и интенсивность «Хаос» ×2, When `Resolve`, Then пик 2,0 (предел канала), а не 2,2; в письме «до +100 %», пометка «ограничено».

**Complexity:** Medium
**Dependencies:** event-system (`ITimedEffectHandler`, `ModifierAggregator`, `EventEffects.Register`, `IEffectModifiers`).
**Assumptions:** агрегатор `event-system` принимает ежедневное обновление значения эффекта (`Tick`) и предоставляет пределы канала (`LimitMin` / `LimitMax`).

### FR-8: Читающие патчи каналов памяти и пластин

**User Story:** Как игрок, я хочу, чтобы скачок цен на память бил сильнее по CPU с большим кэшем, а дефицит пластин — по реальным затратам производства, чтобы шок был честным и объяснимым.

**Priority:** Must

#### Acceptance Criteria

1. WHEN множитель `production.memoryCost` = k ≠ 1 THEN postfix `CpuMath.MaterialCost` SHALL прибавить к результату `memTerm · (k − 1)`, где `memTerm = (2.35·l1 + l2/1.25 + l3) · memory.UnitCostPerCache / processNode.CacheCostReduction`.
2. WHEN множитель `production.waferCost` = k ≠ 1 THEN postfix `ProductionMath.WaferMaterialCost` SHALL умножить результат на k.
3. The патчи SHALL действовать на всех вызывающих: `CpuUnitCostUpdater.UpdateUnitCost` (ежедневная себестоимость всех CPU), `CreatorCpu` (предпросмотр игрока), `AIBehaviourCreation` (проектирование ИИ), `Factory.ProduceWafers` и `CpuMath.RealUnitCost` (материал пластин), а также производство GPU фичи `gpu-product-line`, которая вызывает `ProductionMath.WaferMaterialCost` напрямую.
4. IF `memory` или `processNode` равны `null`, `CacheCostReduction ≤ 0` или результат не конечен THEN патч SHALL вернуть исходное значение без изменений.
5. WHILE k = 1 патчи SHALL выходить сразу, без вычислений.
6. The значение k SHALL кешироваться на игровой день: при первом вызове патча в новый день (`ModTicks.Today` сменился) читается `ModModifiers.Get` (быстрый путь — `ModModifiers.HasAny`), дальше берётся кеш. Кеш SHALL сбрасываться при смене состояния фичи, загрузке сейва и по событию `ModModifiers.ChannelChanged` двух своих каналов (изменение вклада посреди дня).
7. The патчи SHALL быть зарегистрированы как «формулы фичи» ядра (`ReservedKind.FeatureFormula`, владелец `market-shocks`): `ModHooks` принимает их от этой фичи и отклоняет от любой другой.

**Сценарии:**
- **AC-8.1** Given k_mem = 1,6, When `CpuUnitCostUpdater` на следующий день пересчитывает `Cpu.UnitCost`, Then у CPU с большим L3 рост себестоимости больше, чем у CPU без L3.
- **AC-8.2** Given k_wafer = 1,5, When фабрика производит 1 линию за день, Then материал пластин в расходе дня = 2000·50·1/30·1,5 = 5 000 вместо 3 333.
- **AC-8.3** Given скачок пластин ×1,5 и производство GPU фичей `gpu-product-line`, When считается себестоимость пластины GPU, Then она выросла в 1,5 раза через тот же патч.
- **AC-8.4** Given другая фича пытается поставить патч на `CpuMath.MaterialCost`, When `ModHooks.ApplyFeature`, Then патч отклонён с указанием владельца `market-shocks`, фича `Unavailable`.

**Complexity:** Medium
**Dependencies:** mod-framework (`ModModifiers.Get`, `ModModifiers.HasAny`, `ModHooks`, реестр каналов `core.json`, таблица патчей F1/F2).
**Assumptions:** `mod-framework` не патчит `CpuMath.MaterialCost` и `ProductionMath.WaferMaterialCost` (цели закреплены за этой фичей). Канал `production.unitCost` он применяет в `Factory.ProduceWafers` (P2) поверх `Cpu.UnitCost`, поэтому шок памяти и множитель себестоимости компании перемножаются.

### FR-9: Одинаковое влияние на ИИ и игрока

**User Story:** Как игрок, я хочу, чтобы соперники страдали от шоков так же, как я, чтобы рынок был честным, а чужая авария становилась моим шансом.

**Priority:** Must

#### Acceptance Criteria

1. The каждый шок SHALL применять к ИИ те же числа и длительности, что и к игроку; суммы денег — по той же формуле от квартальной выручки цели.
2. WHEN цель выбирается случайно THEN игрок SHALL быть кандидатом наравне с ИИ (`RandomCompany` или `AllCompanies`).
3. WHEN у ИИ выбор THEN решение SHALL приниматься цепочкой политик `event-system` (`EventAi.AddChooser`, затем `aiWeight` / `aiMinCashMultiple`), в том числе подключённой `rival-personalities`.
4. The глобальные каналы (`production.memoryCost`, `production.waferCost`, `market.demand.*`) SHALL действовать на все компании одновременно. Каналы памяти и пластин меняют `Cpu.UnitCost` и `Cpu.get_RealUnitCost`, которые видит ИИ (в отличие от `production.unitCost`, который `mod-framework` применяет только к расходу производства — решение владельца). ИИ реагирует существующей логикой игры:
   - `AIBehaviourMarket.HandleUnprofitableCpus` поднимает цену, когда себестоимость выше;
   - `AIBehaviourMarket.ChangePrices` поднимает цену при дефиците;
   - `AIBehaviourCreation` проектирует CPU с учётом новой себестоимости.
5. The эффекты шоков для ИИ-целей SHALL не ослабляться на низкой сложности: сложность и так влияет на ИИ в игре (решение владельца).

**Сценарии:**
- **AC-9.1** Given симуляция 50 лет с фиксированными долями мощностей, When считаются цели шоков `ms.supply`, Then доля игрока совпадает с его долей мощностей ± 2 п. п.
- **AC-9.2** Given скачок памяти ×1,6, When проходит 30 игровых дней, Then цены CPU ИИ, проданных ниже новой себестоимости, выросли (наблюдение в ручной проверке M5).
- **AC-9.3** Given низкий и высокий уровни сложности (`Player.DifficultyLevel`) и одинаковая квартальная выручка цели, When срабатывает `ms.fab_fire` у ИИ, Then суммы и длительность одинаковы.

**Complexity:** Medium
**Dependencies:** event-system (FR-3, FR-4).
**Assumptions:** нет.

### FR-10: Темп, наложение, интенсивность и координация

**User Story:** Как игрок, я хочу, чтобы шоки были ощутимы, но не валили компанию подряд, и чтобы можно было выбрать их силу.

**Priority:** Must

#### Acceptance Criteria

1. The категории SHALL иметь частоты и темп из `design.md`. На «Обычно» в среднем:
   - `ms.supply` ≈ 1,3 срабатывания в год по всей индустрии;
   - `ms.industry` ≈ 0,4;
   - `ms.price` ≈ 0,7;
   - `ms.demand` ≈ 0,4;
   - `ms.trade` ≈ 1,0;
   - волны `ms.cycle` идут непрерывно (память — период 4 года, спрос — 5 лет).
2. The пак SHALL задавать пределы агрегированных множителей каналов:
   - `production.memoryCost` [0,5; 2,0], `production.waferCost` [0,6; 2,0];
   - `market.demand.*` [0,6; 1,6];
   - `production.output` [0,4; 1,5], `production.unitCost` [0,7; 1,6], `factory.operatingCost` [0,7; 1,8].
3. WHERE игрок выбрал интенсивность «Мягко» ×0,5, «Обычно» ×1, «Жёстко» ×1,5 или «Хаос» ×2 market-shocks SHALL масштабировать отклонения множителей и денежные суммы (`PackOptions.Severity`), зажимая множители пределами пака (FR-7 п. 6).
4. IF у компании уже активен шок `ms.supply` THEN она SHALL не становиться целью нового шока `ms.supply` до его окончания (фильтр цели `{ "not": { "type": "es.activeInCategory", "category": "ms.supply" } }`, предикат `event-system`).
5. WHERE идёт историческое событие категории `history` (`historical-events`) THEN случайные шоки с глобальным эффектом (отраслевые, ценовые и бумы/рецессия: №5–8, №10–12, №14, №16–18 каталога) SHALL не запускаться: условие `{ "not": { "type": "es.activeInCategory", "category": "history", "scope": "any" } }`. Координация односторонняя: исторические события шоками не блокируются.

**Сценарии:**
- **AC-10.1** Given симуляция 30 лет, When считаются срабатывания по категориям, Then средние в пределах ±25 % от FR-10.1.
- **AC-10.2** Given активны землетрясение (память ×1,3 на плато) и скачок памяти (×1,6), When они на плато одновременно, Then канал = min(2,08; 2,0) = 2,0, эффект помечен «ограничено».
- **AC-10.3** Given интенсивность «Хаос» и `ms.fab_fire` (`FactoryOutput ×0,55`), When `Resolve`, Then множитель 0,4 (предел `production.output`), суммы ×2.
- **AC-10.4** Given идёт историческое событие категории `history` с глобальным эффектом, When лотерея `ms.price` выбирает `ms.dram_spike_warning`, Then событие исключено из выбора до окончания исторического.

**Complexity:** Medium
**Dependencies:** event-system (категории, `channelLimits`, `PackOptions`, `es.activeInCategory` со `scope`).
**Assumptions:** движок берёт самый узкий предел канала из всех паков (event-system, design «Агрегатор»); категория исторических событий называется `history` (config пака `historical-events`).

### FR-11: Объяснимость: причина, срок, предупреждения, новости

**User Story:** Как игрок, я хочу понимать, почему случился шок, на сколько он и что именно изменилось, чтобы не чувствовать произвола.

**Priority:** Must

#### Acceptance Criteria

1. The каждый шок SHALL иметь текст причины (`cause`). Для фабричных шоков причина SHALL упоминать долю мощностей цели («чем больше линий, тем выше риск»).
2. WHEN шок срабатывает для игрока THEN письмо SHALL перечислять каждое изменение с числом и сроком: «Выпуск фабрики −45 % на 35 дн.», «Цена памяти до +60 %: рост 30 дн., пик 90 дн., спад 90 дн.».
3. WHEN шок снижает выпуск игрока THEN письмо SHALL предупреждать о риске штрафов по OEM-контрактам (штраф `FinePerUnit` при недопоставке в `ContractSeller.SellContracts`).
4. The глобальные ценовые шоки и рецессия SHALL предваряться предупреждением (`ms.dram_spike_warning`, `ms.recession_warning`) за 21 и 45–75 дней соответственно.
5. WHEN шок попадает в журнал THEN запись SHALL показывать текущее значение кривой и дату окончания.
6. WHEN шок срабатывает у ИИ THEN новость игроку (письмо `Low`) SHALL отправляться только если цель входит в пятёрку ИИ-компаний с наибольшей долей рынка на день срабатывания (`PackOptions.RivalNewsTopN = 5`, решение владельца) и включена настройка «Новости о конкурентах»; в журнал событие попадает всегда.

**Сценарии:**
- **AC-11.1** Given `ms.dram_spike` начался 1988-03-22 (30 + 90 + 90 дней), When на 40-й день игрок открывает «Активные эффекты», Then строка «Цена памяти +60 % (пик), до 18.10.1988».

**Complexity:** Low
**Dependencies:** event-system (FR-7, FR-8).
**Assumptions:** нет.

### FR-12: Предикаты условий пака

**User Story:** Как автор контента, я хочу условия «продаёт ниже себестоимости» и «есть свежий CPU», чтобы торговые шоки били по тем, кого касаются.

**Priority:** Must

#### Acceptance Criteria

1. The market-shocks SHALL зарегистрировать в `ModConditions` (через `EventConditions.Register`, владелец `market-shocks`) предикат `ms.sellsBelowCost` (`priceRatio` = 0.95, `minShare` = 0.05): истина, если у подлежащего есть CPU с `SellOnMarket`, `IsReleased`, `!IsRetired`, `SoldLastDay > 0` и `Price < RealUnitCost · priceRatio`, и `MarketDataProvider.MarketShare(company) ≥ minShare`.
2. The market-shocks SHALL зарегистрировать `ms.hasRecentCpu` (`maxAgeYears` = 1): истина, если у подлежащего есть CPU на рынке с `текущий год − CpuBase.Year ≤ maxAgeYears`.
3. IF данные недоступны (`MarketDataProvider.Instance == null`) THEN предикат SHALL возвращать «ложь» без исключения.
4. The имена предикатов SHALL соответствовать соглашению об идентификаторах (`<prefix>.<name>`, префикс `ms`); `ModConditions.Register` отклоняет остальные.

**Сценарии:**
- **AC-12.1** Given CPU игрока выпущен в 1994 году, When идёт 1995 год, Then `ms.hasRecentCpu` истинен; в 1996 — ложен.

**Complexity:** Low
**Dependencies:** event-system (`EventConditions.Register`); mod-framework (`ModIds`).
**Assumptions:** `CpuBase.Year` — год создания CPU (`CreatorCpu`, `AIBehaviourCreation` его выставляют).

### FR-13: Отображение изменённых цен в интерфейсе игры

**User Story:** Как игрок, я хочу видеть в описаниях памяти и техпроцесса текущую цену с учётом шока, чтобы проектировать CPU на реальных цифрах.

**Priority:** Could

#### Acceptance Criteria

1. WHILE `production.memoryCost` ≠ 1 market-shocks SHALL в postfix `Memory.GetDescriptions` заменять строку цены кэша (индекс 2 списка) на `UnitCostPerCache · k` с пометкой «(шок ×k)».
2. WHILE `production.waferCost` ≠ 1 market-shocks SHALL в postfix `ProductionMath.WaferCost` прибавлять `2000 · m · (k − 1) / yield`, чтобы `ProcessNode.GetDescriptions` показывал цену пластины с шоком.
3. IF длина списка описаний меньше 3 THEN патч описания SHALL не менять список.

**Сценарии:**
- **AC-13.1** Given k_mem = 1,5, When открыт выбор памяти в конструкторе CPU, Then у DRAM цена кэша $30,0 (шок ×1,5) вместо $20,0.

**Complexity:** Low
**Dependencies:** FR-8.
**Assumptions:** русификатор переводит литералы строк, поэтому замена идёт по индексу, а не по тексту.

### FR-14: Настройки и выключение пака

**User Story:** Как игрок, я хочу включать, выключать и настраивать силу шоков отдельно от остальных событий.

**Priority:** Should

#### Acceptance Criteria

1. The market-shocks SHALL давать в хабе «Моды» (`FeatureContext.Param`) настройки: вкл/выкл пака, интенсивность («Мягко» ×0,5 / «Обычно» ×1 / «Жёстко» ×1,5 / «Хаос» ×2), частота пака (×0,5 / ×1 / ×1,5).
2. WHEN пак выключается посреди игры THEN новые шоки пака SHALL перестать срабатывать, активные эффекты пака SHALL быть заморожены по правилу `event-system` (D10), а патчи каналов `production.memoryCost` / `production.waferCost` SHALL быть сняты ядром (патчи выключенной фичи не действуют), т. е. работать как при k = 1.
3. WHEN пак включается THEN неистёкшие эффекты SHALL вернуться с прежними датами.

**Сценарии:**
- **AC-14.1** Given активен скачок памяти, When пак выключен, Then на следующий день `Cpu.UnitCost` вернулся к базовому; When включён, Then кривая продолжается с текущего дня.

**Complexity:** Low
**Dependencies:** event-system (FR-11); mod-framework (`ModSettings`, `ModFeatures`).
**Assumptions:** движок умеет замораживать эффекты одного пака (по `packId`, по `ModFeatures.StateChanged`), не трогая остальные.

## Non-Functional Requirements

### NFR-1: Производительность

- The postfix `CpuMath.MaterialCost` SHALL выполняться за O(1) с кэшированным k. Вызовы идут ежедневно для каждого CPU (`CpuUnitCostUpdater.UpdateCpusUnitCost`) и много раз в поиске конструкции ИИ (`AIBehaviourCreation`).
- WHILE k = 1 патчи SHALL не делать ничего, кроме сравнения.
- WHEN обновляются кривые `ModifierRamp` / `ModifierWave` THEN затраты SHALL быть ≤ 0,05 мс на активный эффект в день.

### NFR-2: Надёжность

- The патчи SHALL перехватывать исключения (`ModHooks.Guard`) и возвращать исходный результат метода игры.
- IF тип эффекта пака бросает исключение THEN эффект SHALL помечаться `Failed` движком, остальные эффекты применяются.
- The вклад событий в `production.output` SHALL не опускаться ниже 0,4 (предел пака; предел движка — 0,1), чтобы шоки не останавливали производство полностью, в том числе на «Хаосе».

### NFR-3: Совместимость сейвов

- The пак SHALL не иметь собственной секции `ModSave`: экземпляры и параметры кривых сохраняет `event-system` в `AppliedEffect.curve` (версию секции ведёт ядро).
- WHEN сейв с активными эффектами пака загружается без пака THEN `event-system` SHALL отменить их по правилу «событие удалено из контента».
- The удаление мода SHALL не влиять на сейвы игры: себестоимость пересчитывается игрой ежедневно (`CpuUnitCostUpdater`), след шока не остаётся.

### NFR-4: Локализация

- The все тексты пака SHALL иметь ключи `ms.*` в `ru` и `en`; самопроверка SHALL перечислять недостающие и ключи с чужим префиксом.

### NFR-5: Удобство и баланс

- The одиночный шок SHALL не снижать выпуск компании ниже 55 % и не поднимать себестоимость больше чем на 60 % (на «Обычно»). На «Хаосе» (×2) гарантии задают пределы пака: выпуск не ниже 40 %, цена памяти не выше ×2,0, пластин не выше ×2,0.
- The каждый шок SHALL иметь фиксированную, заранее показанную длительность.

### NFR-6: Безопасность данных

- WHEN восстанавливаются параметры кривых из сохранения (`AppliedEffect.curve`) THEN market-shocks SHALL проверять их по тем же диапазонам, что при загрузке контента (пик, длительность, амплитуда, период, доля списания), и отбрасывать некорректный эффект с записью в лог.
- The патчи SHALL только читать каналы и менять возвращаемое значение. Единственная запись в данные игры — `CpuBase.MarketData.Stock` в `InventoryLoss`, не ниже 0.
- The пак SHALL не выполнять сетевых запросов и не читать файлы, кроме как через `ModContent`.

### NFR-7: Вклад в общие каналы

- The market-shocks SHALL указывать свой максимальный вклад в каналы, где складываются несколько фич. `popularity.gain`: единственный вклад пака — `ms.launch_hype` ×1,2 (минимум ×1,0, максимум ×1,2 при «Обычно»; при «Хаосе» ×1,4) на одну компанию, вместе со вкладами других событий ограничен пределом событий [0,7; 1,6]; итог на CPU по всем фичам ограничивает ядро [0,5; 2,0] (в тестах эффектов учитывать это ограничение). `market.rating.cpu`: пак не пишет. Каналы `market.demand.*`, `production.*`, `factory.operatingCost` в общий бюджет рейтинга и популярности не входят и ограничены пределами пака.

## Edge Cases

| # | Ситуация | Ожидаемое поведение |
|---|---|---|
| E1 | У цели фабричного шока 0 линий к моменту применения | Фильтр `es.hasFab` исключает её при выборе; если линии пропали за 3 дня ожидания выбора, множитель выпуска ни на что не влияет |
| E2 | Литейщик с клиентами-fabless получил пожар | Выпуск пластин фабрики литейщика падает, CPU клиентов тоже (P2 `mod-framework` берёт множитель по `Factory.Company`); `InventoryLoss` списывает запасы CPU клиентов, произведённых на этой фабрике |
| E3 | Снижение выпуска ниже контрактных объёмов | Игра штрафует по `FinePerUnit` (`ContractSeller.SellContracts`) — это часть шока; письмо предупреждает |
| E4 | Наложение шоков памяти (землетрясение + скачок + волна) | Произведение ограничено [0,5; 2,0], пометка «ограничено» |
| E5 | Рецессия во время бума | Множители перемножаются: 1,25 × 0,8 = 1,0; пределы [0,6; 1,6] |
| E6 | Нет CPU на рынке (`Market` пропускает день) | Каналы спроса ни на что не влияют; шок идёт по календарю |
| E7 | Игрок проектирует CPU во время скачка памяти | Предпросмотр `CreatorCpu` показывает возросшую себестоимость; после шока `CpuUnitCostUpdater` пересчитает `Cpu.UnitCost` обратно |
| E8 | `Memory.GetDescriptions` вернул список короче 3 строк (изменение игры) | Список не меняется |
| E9 | Старт игры в 2010 году | Шоки с условием по годам (`ms.pc_boom` ≤ 2005) не срабатывают; остальные — по своим условиям |
| E10 | Предупреждение пришло, а скачок не случился (шанс 0,2) | Игрок, «закупивший впрок», всё равно получает `production.unitCost` ×0,95 на 180 дней; журнал показывает «прогноз не оправдался» |
| E11 | Цель-ИИ обанкротилась во время шока | Эффекты на компанию снимаются движком; глобальные остаются |
| E12 | Сейв загружен посреди кривой | `Restore` выставляет значение кривой на текущий день по сохранённым параметрам |
| E13 | Интенсивность «Хаос» (×2) | Отклонения и суммы масштабируются, множители зажимаются пределами пака при `Resolve`; письмо показывает уже зажатое число |
| E14 | Идёт историческое событие категории `history` | Глобальные случайные шоки (FR-10 п. 5) не запускаются до его окончания; уже идущие шоки не прерываются |
| E15 | Шок пластин во время производства GPU | Цена пластины GPU растёт тем же патчем `ProductionMath.WaferMaterialCost` |
| E16 | Фича `market-shocks` выключена, а `gpu-product-line` считает пластину | Патч снят, цена ванильная |

## Constraints and Assumptions

### Technical Constraints

- Только API `event-system` и `PTMod.Core`; поля классов игры не добавляются.
- Собственные патчи — только на чтение каналов `production.memoryCost` / `production.waferCost` и отображение: `CpuMath.MaterialCost`, `ProductionMath.WaferMaterialCost`, `ProductionMath.WaferCost`, `Memory.GetDescriptions`. Это формулы фичи (ядро закрепило цели за `market-shocks`); больше собственных патчов нет. `DeclareChannel` не используется.
- Сборка — Roslyn `csc` (C# latest, решение 0003); `Logic` (кривые, формулы) — общее подмножество API .NET Framework 4.8 и `mscorlib` Unity. Тесты — мини-раннер (`python mod/build.py --tests`).
- Все числа — в контент-JSON пака (`mod/src/PTMod/Content/market-shocks/`).

### Business Constraints

- Одиночная игра, без сети; ручная проверка — владелец, не во время онлайн-игр с античитом.
- Реализация по одобренной спеке (владелец, 2026-10-10) и после готовности `event-system` (волна 2 роадмапа).

### Assumptions

- `ModModifiers` объявляет каналы фич в реестре ядра, поддерживает глобальные каналы, ленивое связывание провайдеров, `Get`, `HasAny`, `Invalidate` и событие `ChannelChanged` (mod-framework/design.md).
- `event-system` реализован по своему `design.md`: агрегатор-провайдер с пределами по паку, `PackOptions` (`Severity`, `EventsFile`, `RivalNewsTopN`), заморозка по паку, группы `AllCompanies`, предикаты `es.hasFab`, `es.activeInCategory` (`scope`).

## Success Criteria

### Definition of Done

- [ ] FR-1 … FR-14 выполнены, сценарии AC-* проверены тестами или вручную.
- [ ] NFR-1 … NFR-7 выполнены.
- [ ] Юнит-тесты (`mod/tests/PTMod.Tests/MarketShocks/`, мини-раннер) зелёные, включая симуляцию баланса на 50 лет; каждый публичный тип `Logic` в `[Covers]`.
- [ ] Самопроверка пака на старте без ошибок.
- [ ] Ручная проверка владельцем (TSK-018) пройдена.
- [ ] Все `Class.Method` игры в спеке проходят скрипт проверки ссылок, имена `ModX.Member` — режим `--core-api`.

### Acceptance Metrics

- Частоты категорий в симуляции 30 лет — в пределах ±25 % от FR-10.1.
- Доля шоков `ms.supply`, попавших в игрока, = доля его мощностей ± 2 п. п.
- 0 случаев выхода агрегированных множителей за пределы пака.
- Себестоимость CPU после окончания любого ценового шока возвращается к базовой не позже чем через 1 игровой день.

## Решения по умолчанию

Владелец ответил на открытые вопросы v0.1 в `.kiro/steering/owner-decisions.md` (2026-10-10). Ниже — ответы, применённые в этой спеке, и решения, принятые по общему правилу («если варианта нет — самый простой безопасный»). Любое решение можно переиграть: каскадом правятся `requirements.md` → `design.md` → `tasks.md`.

### Ответы на вопросы v0.1

| № | Вопрос v0.1 | Решение | Где в спеке | Источник |
|---|---|---|---|---|
| 1 | Частоты шоков | Частоты приняты | FR-10 п. 1, `config.json` | owner-decisions, «market-shocks» |
| 2 | Нужен ли уровень «Хаос» ×2 | Есть, как значение настройки интенсивности (пределы пака зажимают множители) | FR-10 п. 3, FR-14 п. 1, FR-7 п. 6 | owner-decisions, «market-shocks» |
| 3 | Новости о шоках у ИИ | Только по топ-5 по доле рынка (`PackOptions.RivalNewsTopN = 5`) | FR-2 п. 5, FR-11 п. 6 | owner-decisions, «market-shocks» |
| 4 | Где живут патчи новых каналов | Каналы объявлены в реестре ядра (владелец `market-shocks`); читающие патчи остаются в этой фиче как «формулы фичи» (F1, F2 таблицы патчей ядра) | вводный раздел «Каналы», FR-8 п. 7 | owner-decisions, «Общие политики» п. 5 (каналы в реестре ядра); замечание `shock-channels-not-registered-in-core` |
| 5 | Граница с `historical-events` | Как предложено: общая антидемпинговая жалоба здесь, реальные соглашения и дела там | FR-6, «Не входит» | owner-decisions, «market-shocks» |
| 6 | «Товарный» сегмент рынка из I12 | Вне объёма, отдельной спекой позже | «Не входит» | по общему правилу: самый простой безопасный вариант |
| 7 | Предупреждение, которое не сбылось | Остаётся (шансы 0,8 для памяти и 0,85 для рецессии) | FR-4 п. 2, E10 | по общему правилу: поведение из черновика не меняется |
| 8 | Видимость `production.unitCost` для ИИ | `production.unitCost` — только фактические траты; ИИ не реагирует на хедж, пошлину и сбои поставок, а на сырьё реагирует через `production.memoryCost` и `production.waferCost` (они меняют `Cpu.get_RealUnitCost`) | FR-9 п. 4 | owner-decisions, «Общие политики» п. 12 |

### Решения по замечаниям кросс-ревью и общим политикам

| Тема | Решение | Источник |
|---|---|---|
| Каналы пластин и памяти | Объявлены ядром до `Register`; патчи — «формулы фичи»; шоки пластин действуют и на GPU (производство GPU вызывает `ProductionMath.WaferMaterialCost` напрямую) | замечание `shock-channels-not-registered-in-core` |
| Идентификаторы | Предикаты `ms.sellsBelowCost`, `ms.hasRecentCpu`; предикаты движка — `es.hasFab`, `es.hasRevenueHistory`, `es.activeInCategory`; ID событий `ms.<имя>` | замечание `id-prefix-convention`; соглашение об идентификаторах |
| Координация с историческими событиями | Условие `es.activeInCategory` со `scope = "any"` и категорией `history` для глобальных случайных шоков; односторонне | замечание `active-in-category-scope` |
| Интенсивность и пределы | `Severity` применяется при `Resolve`, итог зажимается пределами пака | по общему правилу; согласовано с event-system D15 |
| Перемотка | Фича нейтральна: шоков и эффектов нет, каналы = 1 | owner-decisions, «Общие политики» п. 1 |
| Одинаковые правила | Эффекты для ИИ на низкой сложности не ослабляются | owner-decisions, «Общие политики» п. 3 |
| Вклад в общие каналы | В `popularity.gain` пишет только `ms.launch_hype` ×1,2; в `market.rating.cpu` пак не пишет; общий предел ядра [0,5; 2,0] | замечание `market-effect-stacking`; «Общие политики» п. 4 |
| Названия | Названия компаний подставляются из игры, пародийных названий в спеке нет; реальные торговые соглашения — `historical-events` | owner-decisions, «Общие политики» п. 2 |
| Версия секции | Своей секции `ModSave` нет; версию секции `event-system` ведёт ядро (`_schema`) | owner-decisions, «Общие политики» п. 5 |
| Тулчейн | Roslyn `csc`, мини-раннер, `[Covers]` на публичных типах `Logic` | решение 0003; owner-decisions, «Общие политики» п. 7 |

## Открытые вопросы владельцу

Открытых вопросов нет: все восемь вопросов v0.1 закрыты `owner-decisions.md` или решены по общему правилу (таблицы выше) и могут быть пересмотрены владельцем.

## Glossary

| Термин | Значение |
|---|---|
| Шок | Событие пака: временное изменение производства, цен или спроса |
| Носитель | Экземпляр события у игрока, к которому привязаны глобальные эффекты события без выбора (письмо-новость) |
| Трапеция (`ModifierRamp`) | Множитель: нарастание `rampIn` дней → плато `hold` → спад `rampOut` |
| Волна (`ModifierWave`) | Синусоидальный множитель с амплитудой A и периодом P |
| Член памяти (`memTerm`) | Часть `CpuMath.MaterialCost`, зависящая от кэша и `Memory.UnitCostPerCache` |
| Материал пластины | `ProductionMath.WaferMaterialCost(m) = 2000 · m` — затраты на кремний в `Factory.ProduceWafers` |
| Интенсивность (severity) | Множитель отклонений шоков от 1,0 и денежных сумм: «Мягко» ×0,5, «Обычно» ×1, «Жёстко» ×1,5, «Хаос» ×2 |
| Формула фичи | Цель патча, закреплённая ядром за фичей-владельцем канала (`ReservedKind.FeatureFormula`) |
| Доля мощностей | Линии компании / линии всех компаний с фабрикой |
| QR | Квартальная выручка компании (`CompanyFinance.QuarterlyRevenue`) |
