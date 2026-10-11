# Запросы фичи rival-personalities к итоговому интегратору

Публичный API фичи лежит в `mod/src/PTMod/Features/RivalPersonalities/Api/` (интерфейс, null-object, фасад, `AttitudeLevel`). Ядро дало всё нужное (`ModFeatures.IsActive`, `ModHooks.Raise`, `ModHooks.ReportFault`, `Segment`); недостающего в ядре и в чужих API нет. Ниже — то, что нужно интегратору.

## 1. Сборки тестов должны компилировать `Api/Contracts/**` фичи

Тот же запрос, что в `mod/requests/rival-acquisitions.md` (пункт 1): одна правка `build.py: build_tests` и `build_feature.py: build_feature_tests` решает обе фичи.

**Что нужно.** Добавить в обе сборки тестов папку `src/PTMod/Features/<Pascal>/Api/Contracts/` рядом с `Features/<Pascal>/Logic/`.

**Почему.** Сборка потребителя (`historical-events`, `rival-acquisitions`) берёт ядро, свою фичу и только `Api/` зависимости. Поэтому `AttitudeLevel` (возвращается в `IRivalPersonalitiesApi.GetAttitudeLevel`) лежит в `Api/Contracts/AttitudeLevel.cs`, а не в `Logic/`. Чистая логика `rival-personalities` (`AttitudeMath.LevelOf`, TSK-004) тоже возвращает `AttitudeLevel`, а тестовая сборка `Api/` не видит: без правки `Logic` не соберётся под тестами (CS0246).

**Безопасность правки.** Файл `Api/Contracts/AttitudeLevel.cs` чистый: перечисление без ссылок на `PTMod.Core`, `UnityEngine` и игру. Остальные файлы `Api/` (`IRivalPersonalitiesApi`, `RivalPersonalities`, `RivalPersonalitiesApi`, `RivalPersonalitiesApiHost`, `NullRivalPersonalitiesApi`) используют `ICompany`, `ModFeatures`, `ModHooks` и в тесты попадать не должны.

**Текущий обход (для исполнителей фичи).** До правки не объявлять второй `AttitudeLevel` в фиче (CS0101 в сборке фичи): `Logic` возвращает `int` и отображается в `AttitudeLevel` в коде вне `Logic`, либо в `Logic/` лежит временная заглушка под `#if PTMOD_TESTS`, которую убирают вместе с правкой.

**Состояние (кусок 1, авто 2026-10-10).** Обход сделан: `mod/src/PTMod/Features/RivalPersonalities/Logic/AttitudeLevelTestShim.cs` объявляет тот же `AttitudeLevel` (те же имена и числа) целиком под `#if PTMOD_TESTS`; в сборке фичи и в полной сборке мода (`PTMOD_TESTS` не задан) файла как бы нет и работает настоящее перечисление из `Api/Contracts`. `AttitudeMath.LevelOf` возвращает `AttitudeLevel`, как в design. **При правке `build.py`/`build_feature.py` этот файл надо удалить**, иначе сборка тестов упадёт с CS0101.

## 2. Расхождение имён: `RivalPersonalities.Api` не компилируется в соседнем пространстве имён

Спеки (`rival-personalities/design.md`, Component 12; `historical-events/design.md`, `tasks.md`) пишут `RivalPersonalities.Api.ApplyPriceCut(...)`. Статический класс называется так же, как пространство имён `PTMod.Features.RivalPersonalities`. В коде из `PTMod.Features.HistoricalEvents` простое имя `RivalPersonalities` разрешается в пространство имён (оно найдено в `PTMod.Features` раньше `using`-директив файла), и сборка падает с CS0234. Проверено компиляцией.

**Что сделано.** Имя из спеки оставлено (`RivalPersonalities.Api`), и добавлена точка входа без конфликта: `RivalPersonalitiesApi.Instance` (тот же объект).

**Что нужно потребителям** (`historical-events`, при необходимости `scenarios-achievements`): в коде вне `PTMod.Features.RivalPersonalities` писать `RivalPersonalitiesApi.Instance` (с `using PTMod.Features.RivalPersonalities;`) или поставить внутри блока `namespace ... { }` алиас `using RivalPersonalities = PTMod.Features.RivalPersonalities.RivalPersonalities;`. Алиас на уровне файла (вне `namespace`) не помогает. Тексты спек потребителей можно поправить при интеграции; на код это не влияет.

## 3. Для исполнителей фичи rival-personalities (не для интегратора)

- Папка `Api/` собирается в каждую фичу-потребитель без остальных папок `rival-personalities`. Реализация (`ArchetypeCatalog`, `AttitudeLedger`, `PriceBook` и т.д.) не может лежать в `Api/`, хотя `tasks.md` (TSK-020) называет там `RivalPersonalitiesApi.cs`, `RivalConditions.cs`, `FlawResponseBias.cs`. Реализацию `IRivalPersonalitiesApi` класть вне `Api/` (например `Integration/RivalApiService.cs`; имя `RivalPersonalitiesApi` занято статическим классом), предикаты и `FlawResponseBias` — в `Integration/`.
- Подключение: в `Register` или `OnEnabled` вызвать `RivalPersonalitiesApi.Register(service)`; в `OnDisabled` можно вызвать `Unregister(service)`, но не обязательно: фасад сам отвечает как null-object, пока фича не `Enabled`. Событие `ReactionStarted` поднимает сама реализация (событие интерфейса), фасад его пересылает через `ModHooks.Raise`; отдельного вызова не нужно.
- `FeatureId` в `Api` — `RivalPersonalitiesApi.FeatureId` (internal, `"rival-personalities"`); значение должно совпадать с `RivalPersonalitiesFeature.FeatureId`.
- Фасад вызывает реализацию только при `ModFeatures.IsActive("rival-personalities")` и свёртывает исключения реализации (`ModHooks.ReportFault`, площадка `Api.<метод>`). Проверки аргументов (клип `depth` и `floorMargin` в 0…0.5, `reactionId` до 64 символов, не больше 20 внешних ударов, `null` для игрока и неизвестной компании) остаются за реализацией.
- Параметры `depth` и `floorMargin` имеют тип `float` (как в спеке): потребителям писать `0.10f`.

## 4. Формат файлов контента и расположение кода (для интегратора; правок кода не требует)

- **Записи — `records`, а не `archetypes` / `assignments` / `events`.** В `design.md` (раздел «Контент») массивы названы по смыслу, но `ModContent.LoadRecords` и `ContentMerger.MergeRecords` читают только `{ "schema": 1, "records": [ { "id": ... } ] }`. Так и записаны `archetypes.json`, `assignments.json`, `events.json`. У записи назначения нужен `id` (требование слияния) — это `key` компании (`intel`, `amd`...); `uniqueId` остаётся ключом смысла. Текст `design.md` и пример `events.json` при интеграции стоит привести к `records`.
- **Чистые типы лежат в `Logic/`.** Сборка тестов берёт только `Features/<Pascal>/Logic/**`, поэтому DTO контента, `ArchetypeCatalog`, `RivalConfig` лежат в `Logic/Content/`, а DTO секции сохранения и кодек — в `Logic/State/` (в `tasks.md`: `Content/ArchetypeModels.cs`, `Content/ArchetypeCatalog.cs`, `State/RivalState.cs`). Игровая часть — `Content/RivalContent.cs` (загрузка через `ModContent`) и `State/RivalSaveAdapter.cs`.
- **Тесты фичи лежат в `mod/tests/PTMod.Tests/Features/RivalPersonalities/`, а не в `mod/tests/PTMod.Tests/RivalPersonalities/`.** `build_feature.py --tests` отсеивает тесты чужих фич по префиксу `Features/<Pascal>`; тесты вне такой папки попали бы в сборку каждой фичи, где логики `rival-personalities` нет. Пути в `tasks.md` (`RivalPersonalities/ContentValidationTests.cs` и т. д.) читать с этой поправкой.

## 5. `config.json` пака `rival-personalities` читает и `event-system`

`RegisterPack("rival-personalities", "rival-personalities", ...)` (TSK-014) отдаёт движку событий папку `Content/rival-personalities/`, а он читает из папки пака необязательный `config.json` (категории, пределы каналов, темп; `PackConfigReader` в `Features/EventSystem/Logic/Content/EventConfig.cs`). Файл самой фичи с тем же именем (глобальные числа: `difficultyMultipliers`, `attitude`, `reactions`...) называется так по `tasks.md` и `design.md` фичи и не содержит ни `categories`, ни `channelLimits`.

**Что получится.** `PackConfigReader.Read` кончает вызовом `ReportUnknown("schema")`: каждое неизвестное поле верхнего уровня (их у файла фичи около 17) попадёт в лог предупреждением. Для событий пака с категорией `rival`, которой нет ни в одном `config.json`, `ContentValidator` добавит предупреждение «category ... is not defined by any pack, no pacing rules apply to it» (не ошибка: все пять событий `Manual`, лотерея категорий им не нужна). Работе это не мешает, но засоряет лог при каждом старте.

**Что предлагается интегратору (на выбор).** (а) Дать `PackOptions` поле `ConfigFile` (по умолчанию `config`): тогда `rival-personalities` при TSK-014 укажет другое имя (например `pack`) и положит в такой файл `{ "schema": 1, "categories": [ { "id": "rival", "dailyChance": 0, "minGapDays": 0, "maxActive": 4 } ] }`. (б) Научить `PackConfigReader` не жаловаться на поля, которых он не знает, если у пака собственный `config.json`. (в) Переименовать файл фичи (тогда поправить `RivalContent.ConfigFile`, `tasks.md`, `design.md`). До решения исполнитель TSK-014 оставляет как есть: предупреждения безвредны. Сейчас `config.json` фичи читает только `RivalContent` через `ModContent.LoadConfig`.

## 6. Отладочный флаг `forceReactions`

Реализованы оба пути: флаг `rival-personalities.debug.forceReactions` в разделе `debug` файла `ptmod_settings.json` (`ModSettings.Debug`) и переключатель `forceReactions` на странице фичи в хабе (объявляется только при `PTMOD_DEBUG`). `RivalSettings.ForceReactions` — их «или»; в релизной сборке всегда `false`. Сам эффект (шанс 1.0, телеграф 1 день, льготный период 0) подключают задачи планировщика (TSK-013).

## 7. Цель событий пака: `Player` / `RandomAI` вместо `Specific` без ключа

`design.md` фичи пишет в `events.json` `"targets": { "type": "Specific" }` и говорит «цель задаётся при вызове: `EventFireContext.CompanyKey`». Но проверка движка (`ContentValidator.CheckTargets`, `Features/EventSystem/Logic/Content/ContentValidator.cs`) отклоняет `Specific` без `companyKey` («a Specific target needs 'companyKey'»), и все пять событий не загрузились бы. Поэтому в `events.json`: `rival.priceCut`, `rival.counterLaunch`, `rival.oemPush` — `"targets": { "type": "RandomAI" }`, `rival.poach` и `rival.smear` — `"targets": { "type": "Player" }`; конкретного соперника задаёт вызов `EventBus.Fire(..., new EventFireContext { CompanyKey = "ai:<UniqueID>" ... })` (TSK-014).

**Что нужно подтвердить при интеграции `event-system`.** Непустой `EventFireContext.CompanyKey` заменяет выбор по `targets` определения для любого типа цели (так написано в `event-system/design.md`: «null — по targets определения»), а не только для `Specific`; тогда события работают как задумано. Если движок не подменяет цель, нужно либо разрешить ему `Specific` без ключа для событий `Manual`, либо дать вызову право указать цель; поправка в самой фиче — только текст `events.json` и этот пункт.


## 8. Кусок 2 (TSK-007…009, авто 2026-10-10)

### 8.1. Для интегратора: `ModCompanies.All` пуст на `ModSave.AfterRead` (ядро; не блокирует)

`ModCompanies` обновляет список на `CompanySpawner.OnCompanySpawned`, на `MonthCacheUpdate` и перед `GameReady`, а `CompanySpawner.SpawnCompanyFromSave` событие не поднимает. Поэтому на `ModSave.AfterRead` (он идёт до `GameReady(Loaded)`) список ИИ может быть пуст. По FR-2 п. 4 характеры применяются именно после `AfterRead`.

**Обход у себя.** `PersonalityRegistry.AiCompanies()` берёт `ModCompanies.All` и дополняет его `ProcessorTycoon.DataFinder.FindAllAICompanies(true)`: один поиск по сцене на загрузку, конец перемотки или включение фичи. Плюс `ModTicks.GameReady` — страховка: если `AfterRead` не пришёл, характеры применяются там.

**Что предлагается.** Пусть ядро обновляет `ModCompanies` (глубокий `Rebuild`) до `ModSave.AfterRead`. Тогда обход можно убрать.

### 8.2. Для интегратора: `BuiltDll_*` в тестах сборки фичи

`ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` берёт `PTMod.dll` из каталога тестов (`TestEnv.BuildDir`). В `build_feature.py --tests` это `mod/build/features/<slug>/`, а там лежит DLL фичи. В неё по замыслу встроены только контент и строки ядра и самой фичи. Если её собрать (`build_feature.py <slug>`) и сразу запустить тесты без правок исходников, DLL «свежая». Тогда тест падает: в ней нет контента других фич. Если DLL старше исходников, тест пропускается (так было в куске 1). Предлагается в сборке фичи пропускать `BuiltDll_*` или проверять DLL полной сборки.

### 8.3. Для исполнителей следующих задач фичи

- **Где что лежит.**
  - Чистое ядро ценовой книги — `Logic/Prices/PriceBookCore<TCpu>`. В `tasks.md` оно указано как `Behaviour/PriceBook.cs`, но тесты собирают только `Logic/`. Игровая обёртка — `Behaviour/PriceBook.cs`, доступ к ней: `RivalPersonalitiesFeature.Instance.Prices`.
  - Ядро уже проверяет:
    - id (1–64 символа), сегмент (не `Any`), банкрота и спящую компанию (`state.dormant`);
    - лимит в 20 внешних ударов;
    - клип `depth` и `floorMargin` к 0…0.5.
  - TSK-020 остаётся перевести `ICompany` в `AICompany` (игрок и неизвестные компании → 0).
- **Восстановление удара после загрузки.** Записи книги хранятся в секции. Ежедневный `Clamp` (постфикс `AIBehaviourMarket.OnTick`) снова удерживает все записи сам: отдельный `Reapply` не нужен. `ITimedEffectHandler.Restore` из TSK-014 может ничего не делать с ценами. `Remove` из TSK-014 вызывает `Prices.Restore(reactionId)`.
- **Выключение фичи.**
  - `OnDisabled` вызывает `Prices.ReleaseAll()`: исходные `TargetPrice` возвращаются, записи остаются. При повторном включении удары продолжаются.
  - `OnDisabled` откатывает дизайнеров (`Registry.OnDisabled`).
  - Бусты `ReactionBoost` при выключении не снимаются: патчи ядро снимает само, а судьбу бустов решают TSK-014/023.
- **Бусты `CounterLaunch` (TSK-014).** Добавлять и снимать через `RivalPersonalitiesFeature.Instance.Modifiers.AddBoost(new ReactionBoost(id, uid, segment, approvalBoost, fundingBoost))` и `RemoveBoost(id)`.
  - Буст одобрения действует на дизайнеров своего сегмента (`AIBehaviourCreation.MarketFocus`) и сегмента `Segment.Any`.
  - Буст финансирования действует на всю компанию.
  - До применения характера и в перемотке множитель равен 1.
- **`ExpireExternal` (TSK-012).** Сейчас его вызывает временный обработчик `LateMonthPassed` в `RivalPersonalitiesFeature.OnLateMonthPassed`. TSK-012 переносит вызов в `MonthlyEvaluator` и убирает этот обработчик.
- **`SetDormant` (TSK-020).** `PersonalityRegistry.Apply` не применяет характер спящей компании, а уже применённый откатывает. Для `SetDormant(true)` вызвать `Registry.Revert(company)` и `Prices.RestoreRival(uid)`. Для `false` вызвать `Registry.EnsureAssigned` + `Apply`.
- **Отношение (TSK-010).** `PersonalityBook.EnsureAssigned` пишет начальное отношение (baseline) в `state.attitudes` при первом назначении. `StartNewGame` (конец перемотки) ставит baseline всем. `AttitudeLedger` берёт эти записи как есть.
- **Случайные назначения (TSK-021).** Их место — `PersonalityRegistry.StartNewGame()`, до цикла `EnsureAssigned`: заполнить `state.assignments` случайно, а `EnsureAssigned` сохранённое не трогает.

## 9. Кусок 3 (TSK-010…012, авто 2026-10-10)

### 9.1. Для интегратора (код не блокирует)

- **`BuiltDll_*` падает на «свежей» DLL фичи.** То же, что в п. 8.2: `python mod/build_feature.py rival-personalities` кладёт в `mod/build/features/rival-personalities/` DLL с контентом только этой фичи и ядра. Если запустить `--tests` сразу после сборки (DLL новее исходников), `BuiltDll_EmbedsContentAndL10nResources` ожидает ресурсы всех фич и падает; если DLL старше любого исходника, все три `BuiltDll_*` пропускаются. Это не ошибка фичи. Предлагается в `build_feature.py --tests` пропускать эти три теста или проверять DLL полной сборки.
- **Три новые цели патчей** (все обязательные, только чтение): `Company.AddTechnology(Technology, bool)`, `ContractManager.PromoteContractToActive(Contract)`, `BusinessContractManager.BreakContract(ICompany, ICompany, BusinessContract)`. Сигнатуры и имена параметров проверены рефлексией по `Assembly-CSharp.dll`. `contract-transparency` читает тот же `PromoteContractToActive`: оба патча только читают, порядок не важен. Строку фичи в самопроверку добавит TSK-024.
- **`ModEvents.CpuReleased`** подписывается в `Register` (`RivalPersonalitiesFeature.OnCpuReleased`). Ядро поднимает событие через `ModHooks.Raise`, поэтому при выключенной фиче обработчик пропускается.

### 9.2. Где что лежит (отличия от `tasks.md`)

- `AttitudeLedger` — `Logic/Attitude/AttitudeLedger.cs` (чистый; в `tasks.md`: `Attitude/AttitudeLedger.cs`, но сборка тестов берёт только `Logic/`). Игровая часть — `Attitude/AttitudeNotifier.cs`: уведомление `ModNotify` (`rival.notify.level`, строка добавлена в `L10n/{ru,en}`), лог и проверка банкротства по `ModCompanies.Resolve`.
- Решения по триггерам — чистые `Logic/Triggers/TriggerRules`, `TriggerRecorder`, `TriggerEvaluator`, `SingleSubscription<T>`; игровая часть — `Triggers/PlayerActionMonitor.cs` (читает игру, передаёт факты) и `Triggers/TriggerPatches.cs`.
- Месячный проход — чистый `Logic/Triggers/MonthlyEvaluatorCore.cs` (+ `MonthlyBooks.cs`: `UndercutBook`, `ShareSnapshots`, `CautionCleaner`) и игровой `Triggers/MonthlyEvaluator.cs` (собирает CPU, вызывает ядро, затем `PriceBook.ExpireExternal`). Временный обработчик `LateMonthPassed` из п. 8.3 удалён: `RivalPersonalitiesFeature.OnLateMonthPassed` теперь вызывает `MonthlyEvaluator.Run`.

### 9.3. Для исполнителей следующих задач фичи

- **Триггеры (TSK-013).** Планировщик подписывается на `RivalPersonalitiesFeature.Instance.Triggers.TriggerRaised` (`Action<PlayerTrigger>`): событие приходит после того, как отношение соперника изменено. Поля: `Type`, `UniqueId`, `Segment`, `Detail` (имя CPU, технологии или заказчика).
  - `Segment` бывает `Segment.Any`: у `TechLead` сегмента нет. Для `PriceCut` и `CounterLaunch` планировщик берёт основной сегмент соперника (например, сегмент его лучшего CPU).
  - У `OemLoss` сегмент — `Strategy.TargetMarket` предложенного CPU соперника, либо `Any`.
  - `ShareGain` приходит отдельно на каждый сегмент (пара «соперник — сегмент»).
  - Триггеры не поднимаются при `ModTicks.IsLoading`, `IsFastForwarding`, `duringFastForward` и пока `!ModTicks.IsGameReady` (`TriggerRules.IsBlocked`); для спящей и банкрота — тоже.
- **Отношение (TSK-013, 017, 018, 020).** `RivalPersonalitiesFeature.Instance.Attitudes` (`AttitudeLedger`): `Get`, `GetLevel`, `Change(uid, delta, reason)`, `History(uid)`, `IsKnown(uid)`, `LevelOf(value)`, событие `LevelChanged`. `Change` возвращает true, если изменение принято (и когда отношение уже на краю шкалы), false — для неизвестной компании, банкрота и NaN. Для API (TSK-020): `GetAttitude`/`GetAttitudeLevel` → null, если `!IsKnown(uid)`; `ChangeAttitude` → `Change`.
- **Бонус контракта.** +2 за каждый активный контракт, а не за соперника: два контракта с одним соперником дают +4 до чувствительности архетипа. Если баланс покажется щедрым, правится `triggerDeltas.BusinessActiveMonthly` в `config.json`.
- **Расширение месячного прохода.** `MonthlyEvaluatorCore.Run` идёт по шагам: доли → демпинг → контракты → дрейф → уборка. Бросок партнёрства (TSK-022) вставляется шагом после дрейфа (`Step("partnership", ...)`), перед уборкой. `CautionCleaner.RemoveExpired` уже чистит `state.cautions` (пока `until` не наступил, осторожность действует), `CautionBook` (TSK-030) может на него опираться.
- **Снимок долей.** `state.shareSnapshot.monthIndex` — номер последнего полного месяца (`ShareSnapshots.MonthNumber`: `год × 12 + месяц − 2` на дату прохода). Снимок другого месяца для сравнения не годится (фича была выключена): он только заменяется.
- **Подписка на `BusinessContractManager.OnPlayerSignedContract`.** `PlayerActionMonitor.OnSceneReady` вызывается на каждом `ModTicks.GameReady` и при включении фичи в готовой игре; `SingleSubscription<T>` не даёт подписаться дважды на тот же менеджер и снимает обработчик со старого. В лог идёт строка `triggers: BusinessContractManager.OnPlayerSignedContract subscribed|kept (active subscriptions: 1, made in total: N)`.
- **Список ИИ-компаний.** `PersonalityRegistry.Rivals()` — живые ИИ по `UniqueID` (список ядра, при его пустоте — скан сцены).

## 10. Кусок 4 (TSK-013…016, TSK-028, авто 2026-10-10)

### 10.1. Для интегратора и для event-system

- **Причина `rival.reason.oemPush` (TSK-015 выполнена частично).** OEM-натиск — стандартный `ModifierTimed` из `events.json`, а у него нет параметра причины: движок показывает в `ModModifiers.Explain` название события («События: …»), причину фичи подставить нечем. Строки `rival.reason.oemPush` и `rival.reason.poachLoss` лежат в `L10n/{ru,en}`. Чтобы подключить причину, нужно одно из двух в `event-system`: необязательный параметр `reasonKey` у `ModifierTimed`/`FactoryOutput`/`MarketDemand` (тогда в `events.json` добавится `"reasonKey": "rival.reason.oemPush"`), либо брать причину из `cause` события. Бонус соперника от `Poach` (`rival.reason.poach`) создаёт сама фича через `ModModifiers.Add`, там причина есть.
- **Цель события (закрывает п. 7).** По коду `TargetSelector` непустой `EventFireContext.CompanyKey` заменяет цель любого типа (`RandomAI`, `Player`): события `rival.*` стреляют ровно в названную компанию. Правки `events.json` не нужны.
- **`AppliedEffect.textArgs` перезаписывается выводом `Describe`.** `EffectResolver.ResolveOne` после `Resolve` вызывает `Describe` и пишет результат в `textKey`/`textArgs`. Фича на это опирается: обработчики кладут в `textArgs` четыре фиксированных члена (id реакции, сегмент, ключ и имя соперника) и числа строки (`EffectArgs`), а `Describe` отдаёт их назад в том же порядке, так что id реакции переживает сохранение, даже если живого события уже нет. Если движок когда-нибудь перестанет подставлять вывод `Describe` в `textArgs`, обработчики откатятся на `ctx.Instance.Data.args` (их ставит `Fire`); строки `rival.fx.*` рассчитаны на этот порядок (`{3}` — имя соперника).
- **Заморозка пака вместе с фичей.** `RivalPriceCutHandler.Remove` и `RivalCounterLaunchHandler.Remove` различают «срок вышел» и «пак заморожен выключением фичи» проверкой `ModFeatures.IsActive("rival-personalities")`. Для этого движок должен вызывать `Remove` на заморозке уже после перехода фичи в `Disabled`, а `Restore` на разморозке — уже после `Enabled`. Если порядок другой, при выключении посреди ценового удара запись книги цен потеряется (цены всё равно вернутся, но удар не продолжится при повторном включении).
- **Тексты события `rival.poach`.** `Args` события для `Poach`: `[имя соперника, цена контрпредложения, id реакции, ключ соперника]` (у остальных реакций вторым идёт номер сегмента). Текст `rival.poach.body` берёт цену как `{1}`. Текст кнопки `rival.poach.choice.counter` больше не содержит `{0}`: письмо подставляет в кнопку выбора только свой текст, а литерал `{0}` остался бы на экране.
- **Окно выбора и письмо `Critical` ведёт `event-system`.** Фича их не строит. Исполнитель эффектов движка (`EffectRuntime`, `InstanceFactory`) на момент куска ещё дописывался, поэтому цепочку «письмо разведки → доставка → `EventBus.Fire` → `Apply` обработчика → `Remove` через 180 дней» проверяют только тесты ядра фичи и разбор исходников; живую проверку делает владелец (TSK-025).
- **Файл `config.json` пака** — прежняя заметка из п. 5 остаётся в силе (предупреждения `PackConfigReader` о неизвестных полях безвредны).
- **`BuiltDll_*`** — как в п. 8.2 и 9.1.

### 10.2. Где что лежит (отличия от `tasks.md`)

- Чистое ядро реакций — `Logic/Reactions/` (`ReactionPlannerCore`, `ReactionExecutorCore`, `ReactionBook`, `ReactionTags`, `ReactionParams`, `EffectArgs`, `CounterLaunchRules`), `Logic/PoachMath.cs`, `Logic/CharacterVisibility.cs`: сборка тестов берёт только `Logic/`. Игровая часть — `Reactions/ReactionPlanner.cs`, `Reactions/ReactionExecutor.cs`, `Reactions/IntelMail.cs`, `Reactions/CounterLaunchWatcher.cs`, `Reactions/Effects/*.cs`, `UI/DebugReactionPage.cs` (только `PTMOD_DEBUG`).
- Тесты — `mod/tests/PTMod.Tests/Features/RivalPersonalities/` (`ReactionPlannerTests`, `ReactionExecutorTests`, `ReactionHelpersTests`, `PoachMathTests`, `CounterLaunchTests`, `CharacterVisibilityTests`, `ReactionWiringTests`), общий стенд — `ReactionRig.cs`.
- **Кулдаун.** `lastByUid` пишется при исполнении реакции (`Launch`), а пока у соперника есть запланированная реакция, планировщик считает, что его кулдаун идёт (`DaysSinceLastReactionOfRival = 0`): у соперника не бывает двух реакций «на ходу» сразу. `ReactionMath` не менялся.
- **Планировщик гейтит до броска.** Строка таблицы, которую запрещает лимит, не бросается и не расходует поток `ModRandom`; общий для всех строк лимит прекращает поиск, лимит одной строки (ворота отношения, тот же тип) переходит к следующей. «Первая успешная реакция планируется» читается как «первая успешная из разрешённых».
- **`Poach` тоже телеграфируется письмом разведки** (`rival.mail.intel.poach`, как остальные типы): так конвейер у всех реакций один. Само событие остаётся предупреждением с таймером 10 дней (NFR-5).

### 10.3. Для исполнителей следующих задач

- **TSK-018 (окно).** Подключить окно: `IntelMail.OpenIntel = uid => IntelWindow.Open(uid)` в `Register` (до тех пор действие «Открыть разведку» только пишет строку в лог). Страницу хаба (`ctx.HubPage`) TSK-018 регистрирует своей; в отладочной сборке в неё нужно вставить `DebugReactionPage.Build(root)` (сейчас отладочная страница занимает `ctx.HubPage` сама, вторая регистрация её заменит). Видимость характера: `RivalPersonalitiesFeature.Instance.IsCharacterVisible(uid)`. Данные для панели: `Reactions.PendingSnapshot()` (угрозы: `type`, `segment`, `plannedOn`, диапазон недель `ReactionPlannerCore.WeekRange`; точной даты `fireOn` игроку не показывать), `Reactions.ActiveSnapshot()` / `ActiveOf(uid)` (`end` — дата окончания), `Reactions.HistoryOf(uid, 10)` (исходы — `HistoryOutcomes`, выбор в `Poach` — `counterOffer`/`letGo`, с `:auto`), `Reactions.GraceStart`.
- **TSK-020 (API).** `ReactionStarted` поднимать из `RivalPersonalitiesFeature.Instance.Executor.Started` (`(uniqueId, имя реакции)`). `SetDormant(true)`: `Planner.CancelFor(uid, "dormant")` (снимает запланированные и действующие реакции, возвращает цены и бусты через `Cleanup`) и `Registry.Revert(company)`; `Cleanup` возвращает цены только ценовых ударов самой фичи (по id реакции); для внешних ударов (`ApplyPriceCut` других фич) вызвать ещё `Prices.RestoreRival(uid)`.
- **TSK-022 (партнёрство).** Дружественное предложение — не событие (`ReactionTags.EventIdOf(Partnership) == null`): письмо `Low` и свой тег; кулдаун — `Reactions.MarkExecuted(uid, today, false)`. Обработчик действий писем у фичи один (`IntelMail.OnAction`): добавить в него ветку `openNegotiation`.
- **TSK-023.** Записи `pending`/`active` с нечитаемой датой планировщик и исполнитель сами отбрасывают с записью в лог и `cancelled`; проверку размеров (`pending` ≤ 20, `active` ≤ 20, `revealed` ≤ 13) при чтении секции делает эта задача (`ReactionBook` ограничивает только добавление).
- **TSK-024 (самопроверка).** Сайты `ModHooks.Guard` фичи: `TriggerRaised.plan`, `DayPassed.reactions.{bankrupt,planner,expire}`, `YearPassed.reactions`, `ModMail.Delivered`, `EventBus.OnChoiceResolved`, `ModEvents.CpuReleased.counter`, `feature/disabled.boosts`. Типы эффектов (`ReactionTags.Effect*`) и пак `rival-personalities` регистрируются в `Register` независимо от состояния event-system.

## 11. Кусок 5 (TSK-017, TSK-018, TSK-019, TSK-029, авто 2026-10-10)

### 11.1. Для интегратора (код не блокирует)

- **Репутация бренда в окне разведки — числом, не словом.** При активной `press-reviews` окно показывает строку «Репутация бренда: 68» (`ModReputation.Get`, ядро). Словесный уровень («68 — Уважаемый») отдаёт `PressReviewsApi.FormatReputation(company)`, но в `mod/feature_deps.json` у `rival-personalities` нет `press-reviews`, поэтому сборка фичи его `Api/` не видит. Если нужно слово: добавить `press-reviews` в зависимости фичи и в `UI/IntelSource.Collect` заменить число на текст `PressReviewsApi.FormatReputation(rival)` (поле `IntelCompanyFacts.Reputation` станет строкой). Мягкая зависимость уже на месте: без `press-reviews` строки нет.
- **Вёрстку окна в игре никто не видел.** Два столбца (`UI/IntelUi.Columns` + `Column`: `HorizontalLayoutGroup` на всю высоту окна, слева `ModUI.ScrollView` шириной 310, справа остаток), размер окна 900 × 560, подсказка `ModUI.Tooltip` на паре «К вам: …», сохранение положения прокрутки при ежедневном обновлении. Если столбцы не растянутся или прокрутка прыгнет, правится только `IntelUi` и `IntelWindow.Rebuild`; чистая часть (`Logic/Intel/*`) от вёрстки не зависит. Ручной сценарий — TSK-025.
- **Патчи переговоров не запускались в игре.** Цели, имена параметров (`provider`, `client`) и полей (`currentProvider`, `currentClient`), тип `AIScores` сверены рефлексией с `Assembly-CSharp.dll`; поведение (область между prefix и finalizer, сброс области из прошлого кадра, вето `−1000`) покрыто тестами чистого ядра. Живая проверка — AC-8.1…8.3 в ручном сценарии.
- **`BuiltDll_*`** — как в п. 8.2 и 9.1.

### 11.2. Где что лежит (отличия от `tasks.md`)

- Чистая логика — `Logic/Negotiation/` (`NegotiationRules`, `NegotiationScopeCore`), `Logic/IntelFog.cs`, `Logic/Intel/` (`IntelBuilder`, `IntelModels`, `ThreatMath`): сборка тестов берёт только `Logic/`. Игровая часть — `Negotiation/` (`NegotiationScope`, `NegotiationPatches`) и `UI/` (`IntelWindow`, `IntelRowView`, `IntelSource` с `GameIntelText`, `IntelUi`, `IntelHubPage`).
- Тесты — `mod/tests/PTMod.Tests/Features/RivalPersonalities/`: `NegotiationScopeTests`, `IntelFogTests`, `IntelBuilderTests` (стенд `IntelRig.cs` читает боевые строки ru/en и боевой контент), `IntelWiringTests`, `L10nKeysTests`.
- Строки лежат в `L10n/ru/rival-personalities.json` и `L10n/en/rival-personalities.json` (а не в общих `L10n/ru.json`/`en.json`, как написано в TSK-019): так устроены файлы всех фич.
- **Страница хаба.** Одна страница на фичу: `ctx.HubPage(IntelHubPage.TitleKey, IntelHubPage.Build)` заменила отладочную `ctx.HubPage("rival.debug.title", …)`; в отладочной сборке `DebugReactionPage.Build` встроена в эту страницу. Тест `ReactionWiringTests.TheDebugPage_ExistsInTheDebugBuildOnly` поправлен под это.
- **Величина эффекта в окне (NFR-5).** Берётся теми же функциями, что у обработчиков (`ReactionParams`: цена −8…15 %, буст ×1.35/×1.25). Для OEM-натиска множитель ×1.5 живёт только в `events.json`; число продублировано в `ReactionParams.OemPushSignChance`, тест `TheOemPushFactor_OfThePanel_IsTheOneOfTheEventContent` держит их равными.
- **Угрозы и история.** Угроза видна игроку, когда письмо доставлено (в записи `pending` выставлена дата `fireOn`): до этого ни угрозы, ни строки `planned` в истории нет. История окна — слияние записей отношения (`AttitudeLedger.History`) и реакций (`ReactionBook.HistoryOf`), последние 10 по дате.
- **Ключи, которые понадобятся позже, уже в таблицах:** `rival.mail.partner.{subject,body,open}` (TSK-022) и `rival.reason.acquisitionPrice` (TSK-027) — по таблице design.md.

### 11.3. Для исполнителей следующих задач

- **TSK-020 (API).** Окно читает отношение, архетип и спящих только через книги фичи (`Attitudes`, `Book`, `Reactions`), публичный API на него не опирается и от него не зависит.
- **TSK-022 (партнёрство).** Открыть окно на компании: `IntelWindow.Open(uid)`. Письмо с кнопкой «Открыть переговоры» ведёт в окно переговоров игры; бонус `Interest`/`Price` уже посчитают патчи TSK-017.
- **TSK-027 (поглощения).** Соперник из `state.dormant` показан в окне серой строкой «Вне игры», бонуса в переговорах у него нет (`NegotiationScope.BonusFor` и `IntelBuilder` проверяют `Book.IsDormant`).
- **TSK-030 (осторожность).** Окно уже читает `state.cautions` (`uid`, `until` как `yyyy-MM-dd`) и показывает «Осторожен после брака до …» в блоке «Действует», пока `today < until`, для компании в игре и не спящей; запись `CautionEntry` создаёт `CautionBook`, больше ничего делать не нужно.
- **TSK-024 (самопроверка).** Новые цели патчей: `ContractNegotiationWindow.UpdateScores` (prefix и finalizer), `AIMath.GetProviderScores`, `AIMath.GetClientScores` (postfix). Новые сайты `ModHooks.Guard`: `IntelWindow/Open`, `IntelWindow/Refresh`, `DayPassed.intel`, `ModUI.ThemeChanged`, `ModL10n.LanguageChanged`, `ModSettings.Changed`, `feature/disabled.window`, `IntelSource/walk` (`ReportFault`) и четыре сайта `NegotiationPatches/…`.

## 12. Кусок 6 (TSK-020, TSK-021, TSK-022, TSK-026, TSK-027, TSK-030, авто 2026-10-10)

### 12.1. Для интегратора и для соседних фич (код не блокирует)

- **`ChoiceOption.Available` у движка событий не включает проверку денег.** `design.md` фичи и `requirements.md` (FR-14 п. 3) говорят: вариант недоступен, если не выполнены `requires` **или** денег меньше `aiMinCashMultiple × стоимость`. Но `AiDecision.OptionsOf` (`event-system/Logic/Choices/AiDecision.cs`) кладёт в `Available` только `requires`, а деньги проверяет у себя `DefaultAiChooser.CanAfford`. `RivalEventChooser` повторяет эту проверку сам (`CanAfford`, те же числа: `aiMinCashMultiple` варианта и `ICompany.MoneyAmount`), поэтому поведение совпадает с политикой по умолчанию. Если движок позже перенесёт проверку в `Available`, дубль станет безвредным. Достаточно либо поправить текст спеков, либо перенести правило в движок.
- **Движок спрашивает выбирающих, не глядя на выключатель их фичи.** `EventEngine.DecideForAi` вызывает `IEventAiChooser.Choose` у всех зарегистрированных, даже если фича-владелец выключена. `RivalEventChooser` сам проверяет `ModFeatures.IsActive("rival-personalities")` (в `Handles` и в `Choose`) и в выключенном состоянии отвечает `null`. Если в движке появится такая проверка (как у `ModHooks.Raise`), в фиче ничего менять не нужно.
- **Приоритет выбирающего.** `Register` вызывает `EventAi.AddChooser(chooser, 100)`; регистрация до запуска `event-system` откладывается и воспроизводится при `Install`. Если `eventChooser.priority` в `config.json` (после чтения контента в `OnEnabled`) отличается от 100, выбирающий добавляется ещё раз с новым приоритетом: `PriorityList.Add` переносит уже добавленный.
- **`Api/Contracts/**` соседних фич не входит в сборку тестов** (тот же запрос, что в п. 1). Поэтому чистая логика связи с поглощениями (`Logic/Integration/AcquisitionRules`) сравнивает тип сделки как текст (`"Hostile"`, `"Distressed"`, `"BankruptAssets"` = `DealRecord.type`), а не через `DealType`. Если правка сборки тестов будет сделана, можно перейти на `DealType` и `DealRecord` напрямую.
- **`rival-acquisitions` может не вызывать `SetDormant` сама.** Связь делает эта фича: на `DealCompleted` цель, вышедшая из игры или переданная игроку (`RivalAcquisitionsApi.IsAbsorbedByPlayer`), переводится в спящий режим, а после загрузки и при включении фичи просматриваются все записи `RivalAcquisitionsApi.Deals`. Повторный вызов `SetDormant(true)` от `rival-acquisitions` безвреден (идемпотентен: второй вызов ничего не меняет).
- **Окно переговоров.** Действие письма «Открыть переговоры» вызывает `ContractNegotiationWindow.Open(foundry, player, false)` на окне, найденном через `FindFirstObjectByType(FindObjectsInactive.Include)`. Метод игры сразу обращается к `hierarchyBehaviour`, который заполняет `Start()`. Если окно ни разу не было активно, это даст исключение: оно перехватывается (`ModHooks.Guard`, строка в логе), письмо остаётся открытым. Живую проверку делает владелец (TSK-025, AC-12.1): тот же вызов делает и вкладка «Бизнес» игры, поэтому окно должно быть уже запущено.
- **Нового в ядре не нужно.** Использованы `ModConditions.Register`, `ModModifiers.RegisterProvider`/`Invalidate`, `ModMail.Send`, `ModRandom.Stream`, `ModFeatures.StateChanged`, `EventAi.AddChooser`, `FlawRegistry.SetResponseBias`/`OnFlawStateChanged`, `RivalAcquisitionsApi.DealCompleted`/`Deals`/`IsAbsorbedByPlayer`. Недостающих мест нет.
- **`BuiltDll_*`** — как в п. 8.2, 9.1.

### 12.2. Где что лежит (отличия от `tasks.md`)

- **TSK-020.** Папка `Api/` не тронута (это интерфейс, null-object, фасад, `AttitudeLevel`: она собирается в соседние фичи без остальной фичи). Реализации лежат так:
  - `IRivalPersonalitiesApi` — `Integration/RivalApiService.cs` (игровая часть: `ICompany` → `UniqueID`, цены, планировщик, реестр характеров) поверх чистого `Logic/Integration/RivalApiCore.cs` (решения и порт `IRivalApiEffects`). В `tasks.md`: `Api/RivalPersonalitiesApi.cs` — это имя занято статическим фасадом.
  - Предикаты — чистый `Logic/Integration/RivalPredicates.cs` (+ `IRivalFacts`, `RivalPredicateNames`) и игровой `Integration/RivalConditions.cs` (`RivalPredicateFacts`, `ModConditions.Register` один раз из `Register`). В `tasks.md`: `Api/RivalConditions.cs`.
  - `FlawResponseBias : IFlawResponseBias` — `Integration/FlawResponseBias.cs` поверх чистого `Logic/Integration/FlawBiasMath.cs`. В `tasks.md`: `Api/FlawResponseBias.cs`.
  - `Integration/FlawLink.cs` — как в `tasks.md`; в нём же вся часть TSK-030.
- **TSK-021.** Правила — `Logic/Behaviour/RandomAssignment.cs`; сохранение — `PersonalityBook.Assign` и `PersonalityBook.DealRandom` (новые методы чистой книги); вызов — `PersonalityRegistry.DealRandomCharacters` из `StartNewGame` (конец перемотки). В `tasks.md`: `Behaviour/RandomAssignment.cs`.
- **TSK-022.** Игровая часть — `Reactions/PartnershipOffer.cs`; правила — `Logic/Reactions/PartnershipOfferCore.cs`; шаг `partnership` в `MonthlyEvaluatorCore.Run` (после дрейфа, перед уборкой); обработчик действия — ветка `openNegotiation` в `IntelMail.OnAction`.
- **TSK-026.** `Events/RivalEventChooser.cs` (игровая часть) и `Logic/Events/EventChoiceMath.cs` (чистая, тесты). Поле `eventStyle` в `archetypes.json` уже было с TSK-003.
- **TSK-027.** `Integration/AcquisitionLink.cs` и `Logic/Integration/AcquisitionRules.cs` (`AcquisitionRules`, `AcquisitionLinkCore`).
- **TSK-030.** `Behaviour/CautionPatch.cs`, `Logic/CautionMath.cs` (как в `tasks.md`) и `Logic/Behaviour/CautionBook.cs` (записи секции; в `design.md` — «CautionBook»).
- **Тесты** — `mod/tests/PTMod.Tests/Features/RivalPersonalities/`: `RivalApiTests`, `RivalPredicateTests`, `FlawBiasTests`, `RandomAssignmentTests`, `PartnershipTests`, `EventChooserTests`, `AcquisitionLinkTests`, `CautionMathTests`, `IntegrationWiringTests`.
- **Строки.** Добавлены `rival.reaction.partnership` (имя предложения в истории окна) и `rival.history.hostileDeal` (причина в истории отношения). Ключи `rival.mail.partner.*` и `rival.reason.acquisitionPrice` уже были.

### 12.3. Решения, которых нет в спеках дословно

- **`rival.reactionsExecuted`.** `count` обязателен (0…100000). Без `company` — сумма реакций всех соперников; с `ai:<UniqueID>` — реакции этого соперника; с `anyAI` — хотя бы один живой соперник сделал не меньше `count`. Считаются исполненные враждебные реакции (пять типов), накопительно за все годы: счётчик `counters.executed` в секции (в истории всего 100 строк, по ней считать нельзя). Предложение партнёрства не считается.
- **`rival.attitudeAtMost/AtLeast`.** `company` без параметра = `anyAI`; `player` отвергается разбором (`CompanyScope.AiOrAnyAi`). Банкроты при `anyAI` пропускаются ядром.
- **Литейщик для предложения** — `AICompany.IsFoundry` игры. Кулдаун шесть календарных месяцев (`год × 12 + месяц`) считается от письма, а не от ответа игрока; записан в `counters.lastPartnerByUid`. Бросок идёт в потоке `rival-personalities/partner`, чтобы предложения не сдвигали кубики враждебных реакций. Если письмо не принято службой, запись не делается и бросок повторится в следующем месяце. Предложение — строка «Предложение партнёрства: началась» в истории и `ReactionStarted(uid, "Partnership")` в API.
- **Случайные характеры.** Литейщики (`AICompany.IsFoundry`) всегда получают `foundry_partner`, и никто больше его не получает. Остальные берут случайный из шести архетипов с местом (не больше трёх); если мест нет (в моде больше 18 компаний), открываются наименее занятые. Один бросок потока `rival-personalities/assign` на компанию, в порядке `UniqueID`. Режим читается один раз, на `FastForwardEnded`; компании с уже сохранённым характером в раздаче не участвуют, поздно появившиеся берут характер из `assignments.json`.
- **Осторожность.** Срок отсчитывается от `FlawRecordView.PublicSinceDay` (день, когда брак впервые стал публичным), а не от дня уведомления. Повторное уведомление о той же записи или поздняя проверка не создают вторую запись, а срок, который уже прошёл, не создаёт её вовсе. Записи разных браков одной компании не складываются (множитель применяется один раз). При подключении `FlawLink` один раз просматриваются все известные публичные браки (фича могла быть выключена, когда они стали публичными).
- **Враждебное поглощение.** `acquisition.hostileDealAttitude` (−10) идёт через `AttitudeLedger.Change` — чувствительность архетипа применяет он (−10 × sensitivity). Получают все остальные соперники с характером, не банкроты и не спящие; причина в истории — `hostile acquisition` (в окне «вы поглотили компанию против её воли»).
- **Цена поглощения** кэшируется ядром до конца дня; `AttitudeLedger.Changed` (новое событие, `UniqueID`) сбрасывает кэш этой компании (`ModModifiers.Invalidate(channel, key)`).

### 12.4. Для исполнителей следующих задач

- **TSK-023 (миграция, размеры).**
  - В секцию добавлены `counters.executed` (список `{uid, count}`) и `counters.lastPartnerByUid` (список `{uid, date}`); `RivalSaveCodec.Clean` их чистит. Поля добавочные, версия схемы не менялась: старая секция читается с пустыми списками.
  - Размеры при чтении: `cautions` ≤ 50 (при записи держит `CautionBook.MaxEntries`, при чтении — на TSK-023), `executed` ≤ 13 записей по смыслу, `revealed` ≤ 13.
  - Выключение: `OnDisabled` вызывает `DetachLinks` (`FlawLink.Detach` снимает `SetResponseBias(null)` и подписку, `AcquisitionLink.Detach` снимает подписку на сделки). Провайдер цены поглощения не снимается: ядро само не применяет его, пока фича не `Enabled`.
- **TSK-024 (самопроверка).**
  - Целей патчей теперь 12: 11 обязательных и одна `Optional` — `AIBehaviourCreation.AcceptableFailureRate` (`CautionPatch`). Тест `IntegrationWiringTests.ThePatchesOfTheFeature_AreTheTwelveTargetsOfTheDesign` считает их по исходникам.
  - Новые площадки `ModHooks.Guard`/`ReportFault`: `CautionPatch/AIBehaviourCreation.AcceptableFailureRate.postfix`, `FlawLink/OnFlawStateChanged`, `FlawLink/sync`, `AcquisitionLink/DealCompleted`, `AcquisitionLink/sync`, `RivalEventChooser/Choose`, `PartnershipOffer/openNegotiation`, `ModFeatures.StateChanged.flawLink`, `ModFeatures.StateChanged.acquisitionLink`, `ModTicks.GameReady.links`, `feature/enabled.links`, `feature/enabled.chooser`, `feature/disabled.links`, `Api.ReactionStarted`.
  - Фасад API по-прежнему ловит свои сбои под площадкой `Api.<метод>`.
- **TSK-025 (ручная проверка).** Для новых частей: письмо «Предложение партнёрства» (AC-12.1: отношение TSMC ≥ 40 поднимается отладочной страницей, бросок 0.10 — в отладочной сборке повторять месяцы), «Случайные характеры» (AC-11.1: переключатель в хабе до новой игры), сценарий с условием `rival.attitudeAtLeast` (AC-13.1), осторожность после публичного `Critical` брака ИИ (AC-17.1, через `FlawRegistry.ForceFlaw` соперника из отладки `design-flaw-recalls`), враждебное поглощение (AC-15.1: цена × 1.3 при −60), выбор в событии у `price_warrior` (AC-14.1).

## 13. Кусок 7 (TSK-023, TSK-024, авто 2026-10-10)

### 13.1. Для интегратора: секция фичи, выключенной в игре, в боковом файле (ядро; не блокирует)

То же, что `bankruptcy-recovery.md` п. 9.2 и `event-system.md` (наблюдения о ядре). Разобрано по `SideFileModel.ReadSection`/`BuildSection`/`RestoreParticipant` и `ModSave.OnFeatureStateChanged`:

- После успешного `Read` модель удаляет копию секции (`_retained.Remove`), а `BuildSection` для неактивного участника отдаёт только `_retained`. Загрузили сейв, выключили фичу, сохранились (до этого не сохранялись при включённой): **секции в файле нет**. Отношения, история и запланированные реакции пропадут при следующей загрузке. Сохранялись при включённой, потом выключили, поиграли, сохранились: в файле **копия старого сейва**, а не состояние на момент выключения.
- Включённой заново фиче `RestoreParticipant` отдаёт ту же копию или `ResetToDefaults`, если копии нет.
- **Просьба к ядру (необязательная).** На переходе фичи в не-`Enabled` по выбору игрока один раз вызвать `Write()` участника и положить результат в `_retained` (например, `SideFileModel.CaptureParticipant(sectionId)` из `ModSave.OnFeatureStateChanged`). Тогда файл при выключенной фиче хранит состояние на момент выключения, а включение отдаёт его же.
- **Что сделано в фиче без правок ядра.** `RivalSaveSection.Freeze` в `OnDisabled` запоминает состояние (тот же объект, что в памяти), а первая выдача секции после включения (`Read` или `ResetToDefaults`) отдаёт его вместо копии ядра, но только если фича активна и игра та же (`ModSave.FileHeader` — новый объект на каждое чтение бокового файла; новая игра, меню и загрузка другого сейва заморозку сбрасывают). Так же поступил `bankruptcy-recovery`. Это закрывает «выключили и включили в одной партии». **Не закрывает** сейв, записанный при выключенной фиче, и перезапуск игры с выключенной фичей: там ядро хранит копию последнего сейва при включённой фиче (или ничего). Для цен это безвредно: `OnDisabled` вернул `TargetPrice`, и игра их сохранила.
- Если после `OnEnabled` ядро отдало другую секцию (загрузили другой сейв, пока фича была выключена; фича включена в игре, а при загрузке была выключена), `OnOwnSwitchOn` применяет характеры заново. `OnEnabled` идёт до выдачи секции: `ModFeatures` вызывает `OnEnabled`, затем поднимает `StateChanged`, на который подписан `ModSave`. Порядок подписчиков (ядро первое) стоит закрепить в документации ядра.

### 13.2. Для интегратора: самопроверка и `ModSelfCheck`

- **В `ModSelfCheck` нет точки, куда фича добавляет свои находки** (о том же писали `design-flaw-recalls` и другие). Фича пишет свою строку сама: `self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)` на `ModTicks.GameReady` (один раз за сеанс) или сразу при включении в готовой игре.
- **Если обязательная цель патча не применилась при запуске**, ядро переводит фичу в `Unavailable` ещё до `OnEnabled`, и строки фичи нет. Список отсутствующих целей в этом случае пишет `ModSelfCheck` (стадия патчей, вкладка «Диагностика»): он обходит `ModHooks.Report` всех фич. Строка фичи с `FAILED` и `missing targets: …` появляется, когда цель потерялась позже (статус патча не `Applied` у запущенной фичи) или нет ни одного архетипа.
- **`ModFeatures.MarkUnavailable` — внутренняя часть ядра** (`internal`). Самопроверка вызывает её, как `design-flaw-recalls`, чтобы выключить фичу при пустом каталоге архетипов (design.md «Error Handling»: «фича выключается с логом»). Если владельцу нужна публичная точка, нужно `FeatureContext.MarkUnavailable(reason)`.
- **Событийная система читает паки на первом `GameReady`**, и её обработчик может идти после обработчика фичи. Поэтому, когда `event-system` активна, но пака `rival-personalities` в `EventRegistry.All` ещё нет, самопроверка ждёт первый игровой день, а не пишет ложную тревогу.
- **`BuiltDll_*`** — как в п. 8.2, 9.1.

### 13.3. Где что лежит (отличия от `tasks.md`)

- **TSK-023.** `State/RivalStateMigrations.cs` из `tasks.md` лежит в `Logic/State/` (сборка тестов берёт только `Logic/`): `RivalStateMigrations` (шаги), `RivalStateSanitizer` и `RivalStateLimits` (клипы и лимиты). `RivalSaveCodec.Read(section, schema, problems)` — миграция, привязка к DTO и проверки; `RivalSaveSection.Read` идёт через него. Заморозка — `RivalSaveSection.Freeze/UseFreeze/DropFrozen/IsFrozen`, `State/RivalSaveAdapter` подключает её к `ModFeatures.IsActive` и `ModSave.FileHeader`. Тесты — `mod/tests/PTMod.Tests/Features/RivalPersonalities/RivalStateSerializationTests.cs` и `RivalDisableFlowTests.cs`.
- **TSK-024.** `Diagnostics/RivalSelfCheck.cs` и `Diagnostics/RivalTimings.cs` — как в `tasks.md`; добавлены `Diagnostics/RivalFaultProbe.cs` и чистые `Logic/Diagnostics/RivalSelfCheckRules.cs`, `Logic/Diagnostics/RivalTimingBook.cs` (классы `RivalTimingBook`, `RivalTimingSummary`, `RivalTimingScope`). Тесты — `RivalDiagnosticsTests.cs`.
- В `ReactionTags` добавлены списки `EventIds` и `EffectTypes` (пять событий пака, пять типов эффектов).
- `PersonalityRegistry.RevertAll()` теперь возвращает число компаний (раньше `void`), `OnDisabled` пишет `OnDisabled: reverted the character of N AI company(ies)`; строка про цены — `OnDisabled: restored N prices` (сценарий 7 design.md).
- Три существующих теста правились по более строгим правилам: `RivalSaveSectionTests` (образец с `nextReactionSeq` выше `r-18`), `ReactionPlannerTests.ThePlannedReactions_AndTheCounters_SurviveTheSave` (настоящий id вместо `r-90` при счётчике 2), `ReactionWiringTests` (шаги выключения не под `ModHooks.Guard`, а под `Always`).

### 13.4. Решения, которых нет в спеках дословно

- **Шаги `OnDisabled` не под `ModHooks.Guard`.** `Guard` не запускает тело у `Faulted` и `Unavailable` фичи, а фича, которую остановил автомат отключения (пять сбоев за сеанс), всё равно должна вернуть цены и приоритеты дизайнеров: иначе срезанные `TargetPrice` остались бы навсегда (патчи сняты, держать некому, возвращать некому). Шаг `Always(site, step)` — `try/catch` с `ModHooks.ReportFault`, следующий шаг идёт в любом случае.
- **«Фейковый патч» в отладочной сборке** — не отдельный объявленный патч, а флаг `rival-personalities.debug.throwInPatch` в разделе `debug` файла `ptmod_settings.json` (`"debug": { "rival-personalities.debug.throwInPatch": true }`): `RivalFaultProbe.Trip(site)` (`[Conditional("PTMOD_DEBUG")]`, в релизе вызова нет) бросает исключение в начале `try` каждого тела патча. Отдельный патч был бы 13-й целью, менял бы строку самопроверки (12 patches) и мог бы поломать подсчёт целей в ядре. Исключение вставляется в настоящие тела, так что проверяется именно то, что играет.
- **Замеры включены в отладочной сборке и при подробном логе мода** (`ModSettings.VerboseLog`), а не только под `PTMOD_DEBUG`: так бюджет NFR-1 можно проверить и на релизной сборке игрока. В релизе при выключенном подробном логе `RivalTimings.Begin` возвращает 0 после одного чтения константы и настройки.
- **Что измеряется.** Ежедневные патчи (два патча поведения, два рынка, осторожность), три хода игрока (`TriggerPatches`), обработчик `DayPassed`, `ModEvents.CpuReleased`, `ModMail.Delivered`. Вложенные входы не считаются дважды (`RivalTimingScope`: считается только внешний). Месячный проход считается отдельно (`MonthlyEvaluator` → `RivalTimings.AddMonthly`), бюджет 3 мс. Среднее — по всем игровым дням месяца, в том числе по тем, где фича ничего не делала (`NoteDay` из `DayPassed`).
- **Проверки при чтении отбрасывают `pending` и `active` с нечитаемой датой** (design.md «Validation Rules»). Эффекты, которые держит у себя `event-system`, вернутся через обработчики (`Restore`) с собственными датами, поэтому потеря записи `active` не оставляет сирот: цены держит `priceBook` и снимает `Restore` по id, а не по списку `active`.
- **`nextReactionSeq` поднимается над любым `r-<n>` секции.** Иначе следующий id мог бы совпасть с id занятой реакции. В нормальном сейве число уже выше, ничего не меняется.
- **История отношения ограничена 20 строками при чтении** (design.md «Security Considerations»), хотя `attitude.historySize` в `config.json` допускает до 100. При значении выше 20 лишнее уходит при загрузке; панель всё равно показывает десять.
- **`revealed` — не больше 13 уникальных**, остальные списки «по одной записи на компанию» — не больше 64 (в моде больше 13 компаний быть может).
- **Число патчей в строке самопроверки — число целей (12), а не объявлений (14):** пары «префикс и постфикс `AIBehaviourMarket.OnTick`» и «префикс и финализатор `ContractNegotiationWindow.UpdateScores`» дают по одной цели; цель считается применённой, только если применены оба патча.

### 13.5. Для исполнителей следующих задач

- **TSK-025 (ручная проверка).** Сценарий 7 (выключение): в логе `OnDisabled: restored N prices` и `OnDisabled: reverted the character of N AI company(ies)`; после повторного включения в той же партии отношения, история, запланированные и активные реакции на месте (проверить окно разведки). Затем сохраниться при выключенной фиче и загрузиться: состояние будет с последнего сейва при включённой фиче (п. 13.1). Самопроверка: в `Player.log` после загрузки одна строка `self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)`. Замеры: в отладочной сборке раз в игровой месяц строка `timing <гггг-мм>: …` (за 10 лет на нормальной сложности ни одной `OVER THE BUDGET`). Изоляция: добавить флаг `rival-personalities.debug.throwInPatch` в `ptmod_settings.json`, запустить новую игру: в логе по строке на площадку патча, игра идёт как без мода, после пятого сбоя фича `Faulted` с уведомлением в хабе; в окне «Моды» её можно включить только после перезапуска.
