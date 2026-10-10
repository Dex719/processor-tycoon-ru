# Requirements: event-system

## Document Information

- **Feature Name**: event-system — движок событий с выбором, таймером и журналом
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Stakeholders**:
  - владелец проекта (одобрение, ручная проверка в игре);
  - игроки Processor Tycoon RU;
  - авторы спек-потребителей API движка: `market-shocks`, `design-flaw-recalls`, `historical-events`, `rival-personalities`;
  - косвенно: `scenarios-achievements` (условия по истории событий), `press-reviews` (общая `ModReputation`).
- **Идея**: I13 «Choice-driven events with visible timers and matching outcomes» (`pt-modding/IDEAS.md`), спрос medium, fun 4/5, усилие L.
- **Источники (из IDEAS.md)**:
  - <https://steamcommunity.com/app/239820/discussions/0/864977564555209294>
  - <https://steamcommunity.com/app/239820/discussions/0/864977564100483861>
  - <https://steamcommunity.com/app/239820/discussions/0/1291817208497283646>
  - <https://www.nexusmods.com/gamedevtycoon/mods/1/?tab=docs>
  - <https://itch.io/post/494949>
  - <https://itch.io/post/1062617>
- **Связанные документы**: `.kiro/steering/context.md` (контракт `PTMod.Core`), `.kiro/steering/owner-decisions.md` (решения владельца), `.kiro/specs/mod-framework/design.md` (API ядра v0.2), решения `0003` (тулчейн), `0004` (боковой файл), `0005` (UI из шаблонов игры), `0006` (контент как JSON); `design.md` и `tasks.md` этой спеки.

## Introduction

В Processor Tycoon 0.2.3 нет случайных событий. Единственное «событие» — переход рынка на PGA: `MarketEventHandler.TriggerPgaEvent` показывает окно `MarketPgaEvent.Show` один раз за игру. Партия идёт по одному сценарию, а решения игрока — только цены, исследования и линии.

Спека задаёт **движок** событий: определения в JSON, триггеры, цели (игрок или ИИ), выборы с видимым таймером, эффекты, журнал с точными числами. Плюс стартовый набор из 10 универсальных событий. Движок — инфраструктура для четырёх контент-спек (`market-shocks`, `design-flaw-recalls`, `historical-events`, `rival-personalities`). Они добавляют свои события и типы эффектов через API этой спеки и не трогают её код.

Главная жалоба игроков похожих игр — события «зависают» без таймера, а результат на экране не совпадает с применённым. Поэтому числа эффекта вычисляются один раз в момент срабатывания, сохраняются, и именно они показываются и применяются.

Версия 0.2 подгоняет спеку под ядро `mod-framework` v0.2 и решения владельца (`owner-decisions.md`): движок — обычная фича `[ModFeature] IModFeature` без собственных патчей Harmony; пакетный прогон месяца, письма с приоритетами и неактивными вариантами, сброс кеша каналов, идентификаторы с префиксом `es` берутся из ядра.

### Feature Summary

Движок событий `PTMod.Features.EventSystem` (точка входа `[ModFeature] sealed class EventSystemFeature : IModFeature`): определения событий в JSON, детерминированный выбор по ModRandom, выборы с таймером в игровых днях (письмо `ModMail` с кнопками, строка статуса, уведомление, модальное окно), эффекты через каналы `ModModifiers` и API игры, журнал событий и сохранение в `ModSave`; плюс 10 стартовых событий.

### Business Value

- **Для игрока:** у партии появляются развилки и неожиданности. Видно, почему случилось событие, сколько дней осталось на решение и что именно изменилось — до доллара и процента.
- **Для проекта:** одна точка расширения для четырёх контент-спек. Новое событие — это JSON и строки локализации, без кода. Новый тип эффекта — один класс и `EventEffects.Register`.
- **Доказательства спроса:**
  - I13 в `IDEAS.md`: спрос medium, fun 4/5.
  - Игроки похожих тайкунов жалуются на события без таймера и на расхождение текста и эффекта (обсуждения Steam выше).
  - Для Game Dev Tycoon есть мод событий (nexusmods, ссылка выше) — жанр давно расширяют событиями.
  - Идея I13 в `IDEAS.md` опирается также на комментарии itch.io к игре (itch.io/post/494949, itch.io/post/1062617).

### Scope

**Входит:**
- модель определения события (контракт API — в `design.md`) и загрузка контент-паков через `ModContent`;
- триггеры `Date`, `DailyChance`, `Condition`, `Manual`; условия `ModConditions` и предикаты движка `es.*`;
- цели `Player`, `RandomAI`, `AllCompanies`, `Specific` и расширение `RandomCompany` (любая компания, включая игрока) со взвешиванием;
- выборы с таймером, выбор по умолчанию, требования к выбору (неактивный вариант с причиной), политика выбора для ИИ (цепочка `EventAi.AddChooser`);
- стандартные типы эффектов: `Money`, `ModifierTimed`, `Popularity`, `Reputation`, `ResearchProgress`, `FactoryOutput`, `MarketDemand`, `SendMail`, `FireEvent`;
- API расширения: `EventRegistry.RegisterPack`, `EventEffects.Register`, `EventEffects.ApplyDirect`, `EventConditions.Register`, `EventBus.Fire`, `EventAi.AddChooser`, события шины для других фич;
- UI: письмо с кнопками и строкой таймера (`ModMail`), уведомления, модальное окно выбора с обратным отсчётом, окно «Журнал событий»;
- детерминизм через `ModRandom`, сохранение в `ModSave` (версию секции ведёт ядро), защиты времени через `ModTicks`;
- настройки фичи в хабе «Моды»;
- стартовый набор из 10 событий в JSON (8 самостоятельных + 2 исхода цепочки).

**Не входит:**
- контент рыночных шоков и их типы эффектов (`ModifierRamp`, `ModifierWave`, `InventoryLoss`) — спека `market-shocks`;
- брак и отзывы CPU — `design-flaw-recalls`;
- календарь реальных событий — `historical-events`;
- характеры, реакции и отношение ИИ (`attitude`) — `rival-personalities` (движок даёт только точку `IEventAiChooser`);
- хранение репутации, её дрейф к 50 и влияние на популярность — `mod-framework` (`ModReputation`) и `press-reviews`;
- патчи формул, читающие каналы `ModModifiers`, патчи `Company.AddCpu` / `AICompany.AddCpu` и метка пакетного прогона `DateController.NextMonth` — `mod-framework` (движок собственных патчей не ставит);
- сценарии, испытания, достижения — `scenarios-achievements`.

## Requirements

### FR-1: Определения событий, контент-паки и точка входа

**User Story:** Как автор контента, я хочу описывать события в JSON и подключать их паками, чтобы добавлять и балансировать события без перекомпиляции.

**Priority:** Must

#### Acceptance Criteria

1. WHEN мод запускается THEN event-system SHALL загрузить определения событий своего пака и всех паков, зарегистрированных через `EventRegistry.RegisterPack`, из ресурсов `PTMod.dll` через `ModContent`. Загрузка идёт один раз после `Register` всех фич — на первом `ModTicks.GameReady` сессии, поэтому порядок `Register` фич не важен. Файл событий пака называется `events.json`, если пак не задал другое имя (`PackOptions.EventsFile`).
2. WHERE в `<папка игры>/PTMod/content/<pack>/` лежит файл с тем же именем event-system SHALL использовать его вместо встроенного (правило `ModContent`).
3. IF запись события не проходит валидацию (нет `id`, дубликат `id`, ID не соответствует префиксу пака по `ModIds.IsEventIdAllowed`, неизвестный тип эффекта, неизвестный канал, неизвестный предикат, `defaultChoice` не найден или имеет `requires`, множитель вне допустимого диапазона) THEN event-system SHALL пропустить только эту запись и записать причину в `ModLog` (строка вида `[PTMod] [event-system] WARN ...`).
4. WHEN определение ссылается на текст THEN event-system SHALL принимать ключ `ModL10n` или объект `{ "ru": "...", "en": "..." }`.
5. IF фича-выключатель пака (`PackOptions.SettingsFeatureId`) перешла из состояния `Enabled` в другое (`ModFeatures.StateChanged`) THEN event-system SHALL не срабатывать события этого пака, а активные эффекты и ожидающие выборы пака заморозить по правилам FR-11 (только этого пака).
6. The event-system SHALL быть фичей `[ModFeature] sealed class EventSystemFeature : IModFeature` (`Id` = `event-system`, `NameKey` / `DescriptionKey`, `DependsOn` пуст, `EnabledByDefault = true`, `SupportsHotToggle = true`) и SHALL регистрировать тики, участник сейва, параметры и страницу хаба только через `FeatureContext`. Собственных патчей Harmony у event-system нет.

**Сценарии:**
- **AC-1.1** Given встроенный `events.json` с 10 событиями, When наступает первый `GameReady`, Then в реестре 10 определений, в логе нет ошибок.
- **AC-1.2** Given запись с эффектом типа `Teleport`, When загружается пак, Then запись пропущена, в `Player.log` есть строка `[PTMod] [event-system] WARN skip 'x.y': unknown effect type 'Teleport'`, остальные записи загружены.
- **AC-1.3** Given файл-переопределение в `PTMod/content/event-system/events.json` с изменённым `weight`, When мод стартует, Then используется новый `weight`.
- **AC-1.4** Given пак `market-shocks` с записью `id = "dfr.x"`, When загружается пак, Then запись пропущена: префикс `dfr` принадлежит другой фиче.

**Complexity:** Medium
**Dependencies:** mod-framework (`ModContent`, `ModL10n`, `ModLog`, `ModSettings`, `ModIds`, `ModFeatures`); решение 0006.
**Assumptions:** `ModContent` отдаёт записи как `JsonObject` (`ModJson`, mod-framework/design.md), поэтому `params` и `conditions` — вложенные объекты JSON. Типы эффектов, предикаты и паки фичи-потребители регистрируют в своём `Register`; движок читает контент после всех `Register`.

### FR-2: Триггеры, условия и темп

**User Story:** Как игрок, я хочу, чтобы события приходили в подходящий момент и не сыпались пачкой, чтобы они разнообразили игру, а не мешали.

**Priority:** Must

#### Acceptance Criteria

1. WHEN наступает игровой день (фаза `TickPhase.DayPassed`, подписка `FeatureContext.OnTick`) THEN event-system SHALL обработать день строго в порядке: истечение эффектов → обновление кривых → отложенные `FireEvent` → дедлайны выборов → триггеры `Date` → триггеры `Condition` → лотереи `DailyChance` по категориям.
2. WHEN обрабатывается категория с событиями `DailyChance` THEN event-system SHALL сделать один бросок с вероятностью `dailyChance × частота_настройки × частота_пака` и, при успехе, выбрать одно событие среди подходящих пропорционально `weight`.
3. IF условия события (`conditions`) ложны, событие на `cooldownDays` для этой цели (для `AllCompanies` — для события целиком) или уже сработало при `oncePerGame` THEN event-system SHALL исключить его из выбора.
4. WHEN наступает дата триггера `Date` (или первый обработанный день в пределах `graceDays` после неё) THEN event-system SHALL сработать событие один раз.
5. WHERE триггер `Condition` помечен `edge: true` event-system SHALL срабатывать только при переходе условий из «ложно» в «истинно».
6. IF у игрока уже `maxPendingChoices` (по умолчанию 2) ожидающих выборов THEN event-system SHALL отложить новые события с выбором для игрока: `DailyChance` — пропуск дня, `Date`/`Condition` — очередь до 30 дней.
7. IF с последнего события категории прошло меньше `minGapDays` или активных событий категории уже `maxActive` THEN event-system SHALL пропустить лотерею категории в этот день.
8. WHEN триггер `Manual` THEN event-system SHALL срабатывать событие только через `EventBus.Fire` или эффект `FireEvent`.

**Сценарии:**
- **AC-2.1** Given категория `universal` с `dailyChance = 0.011` и частотой «Обычно», When симулируется 36 500 дней без блокировок, Then число срабатываний 401 ± 60 (тест Монте-Карло).
- **AC-2.2** Given событие с `cooldownDays = 1095`, сработавшее 1980-03-01, When идёт 1982 год, Then событие не выбирается; 1983-03-01 снова может быть выбрано.
- **AC-2.3** Given у игрока 2 ожидающих выбора, When лотерея выбирает событие с выбором для игрока, Then оно не срабатывает, а число использованных чисел ГСЧ за день не меняется.

**Complexity:** High
**Dependencies:** FR-9; mod-framework (`ModTicks`, `ModConditions`).
**Assumptions:** `conditions` — узел `ModConditions` (`ModConditions.Parse`), вычисляется для компании-цели как подлежащего (`ModConditions.Evaluate(condition, subject)`); имена предикатов — каталог ядра без точки (`dateAtLeast`, `cpuOnMarket`, `researchActive` и др.) и предикаты движка `es.*` (FR-6).

### FR-3: Цели событий

**User Story:** Как игрок, я хочу, чтобы события случались и с соперниками по тем же правилам, чтобы мир был живым и честным.

**Priority:** Must

#### Acceptance Criteria

1. WHEN срабатывает событие с целью `Player` THEN event-system SHALL создать один экземпляр для `Player.Instance.Company`.
2. WHEN цель `RandomAI` THEN event-system SHALL выбрать одну не обанкротившуюся `AICompany`, прошедшую фильтр `targets.filter`, с весом по `targets.weighting` (`Uniform`, `FactoryCapacity`, `MarketShare`, `QuarterlyRevenue`).
3. WHEN цель `RandomCompany` (расширение контракта) THEN event-system SHALL выбирать среди всех компаний, включая игрока, по тем же правилам, что и `RandomAI`.
4. WHEN цель `AllCompanies` THEN event-system SHALL создать по экземпляру на каждую подходящую компанию в одной группе. Эффекты события с `scope: "Global"` применяются один раз на группу.
5. WHEN цель `Specific` THEN event-system SHALL найти компанию по ключу `ModCompanies` (`player` или `ai:<UniqueID>`, `ModCompanies.Resolve`); IF компании нет или она банкрот THEN SHALL пропустить срабатывание без расхода `cooldownDays`.
6. IF подходящих целей нет THEN event-system SHALL не срабатывать событие.
7. The event-system SHALL применять к ИИ те же правила, числа и формулы, что к игроку (в том числе денежные эффекты от квартальной выручки цели); эффекты для ИИ на низкой сложности SHALL не ослабляться — сложность и так влияет на ИИ в игре (решение владельца).

**Сценарии:**
- **AC-3.1** Given событие `RandomCompany` с весом `FactoryCapacity`, у игрока 30 линий из 150 у всех фабричных компаний, When в тесте 10 000 срабатываний, Then игрок — цель в 20 % ± 2 %.
- **AC-3.2** Given `AllCompanies` с глобальным эффектом `MarketDemand` и выбором у каждой компании, When событие срабатывает при 10 компаниях, Then канал спроса получает один вклад, а экземпляров выбора 10.

**Complexity:** Medium
**Dependencies:** FR-2, FR-4.
**Assumptions:** список компаний и их ключи берутся из кеша `ModCompanies` (`ModCompanies.All`, `ModCompanies.KeyOf`); `AICompany.UniqueID` стабилен между сохранениями (его задаёт `CompanySpawner`).

### FR-4: Выборы с таймером

**User Story:** Как игрок, я хочу видеть варианты решения с точными последствиями и сколько дней осталось, чтобы решать осознанно и не пропускать события.

**Priority:** Must

#### Acceptance Criteria

1. WHEN срабатывает событие с `choices` THEN event-system SHALL вычислить эффекты всех вариантов заранее и установить дедлайн `deadlineDay = max(firedDay, deliveredDay) + timerDays` (в игровых днях), где `deliveredDay` — день доставки письма (`ModMail.Delivered`, `ModMailRecord.DeliveredOn`). До доставки показывается предварительный дедлайн `firedDay + timerDays`; при доставке он пересчитывается только вперёд. Экземпляр игрока с недоставленным письмом SHALL не истекать. Для ИИ дедлайна нет (FR-4.6).
2. WHEN игрок выбирает вариант до дедлайна THEN event-system SHALL применить ровно сохранённые эффекты этого варианта.
3. WHEN наступает дедлайн без решения THEN event-system SHALL применить `defaultChoice` и отметить в журнале «выбрано автоматически».
4. IF условие `requires` варианта ложно THEN event-system SHALL оставить вариант в письме неактивным (`ModMailChoice.Enabled = false`, `DisabledReasonKey` с описанием условия) и показать его неактивным с причиной в окне выбора. WHILE выбор ожидает решения THEN event-system SHALL раз в игровой день пересчитывать доступность вариантов и обновлять кнопки письма (`ModMail.SetChoiceState`, `ModMail.Refresh`). IF вариант стал недоступен между пересчётами и игрок нажал его в письме THEN SHALL применить `defaultChoice` с пометкой «вариант стал недоступен».
5. IF трат `Money` варианта больше денег у компании THEN event-system SHALL не блокировать вариант, а подписать его предупреждением о минусе на счёте (игра допускает отрицательный баланс до порога дефолта). IF списание опустило бы баланс ниже порога дефолта (`ModCredit.ThresholdOf(company)`) THEN вариант SHALL быть неактивен с причиной «не хватит кредитного лимита». Вариант `defaultChoice` SHALL не блокироваться деньгами.
6. WHEN цель экземпляра — ИИ THEN event-system SHALL решить выбор в день срабатывания через цепочку `IEventAiChooser` (`EventAi.AddChooser`: список по убыванию приоритета, побеждает первый ненулевой ответ); если ответа нет — по умолчанию вес `aiWeight` среди доступных вариантов с учётом `aiMinCashMultiple`.
7. IF компания-цель обанкротилась до решения THEN event-system SHALL отменить экземпляр без применения эффектов, закрыть письмо (`ModMail.Resolve`) и записать это в журнал.

**Сценарии:**
- **AC-4.1** Given событие с `timerDays = 14` сработало 1985-06-01, письмо доставлено в тот же день, When игрок не отвечает, Then 1985-06-15 применяется `defaultChoice`, письмо закрыто с этим ответом, показано уведомление «Выбрано автоматически: …».
- **AC-4.2** Given вариант «Нанять» стоимостью $1 250 000, у игрока $900 000 и порог дефолта −$40 000 000, When открыт выбор, Then вариант подписан «уйдёте в минус на $350 000»; When игрок его выбирает, Then списано $1 250 000, баланс −$350 000.
- **AC-4.3** Given событие с выбором у `RandomAI`, When оно срабатывает, Then в тот же день выбор решён, в журнале запись с компанией ИИ и вариантом.
- **AC-4.4** Given вариант с `requires` `{ "type": "researchActive" }` и у игрока нет текущего исследования, When приходит письмо, Then кнопка варианта в письме серая, под ней причина «Нужно: текущее исследование»; When игрок запускает исследование, Then на следующий день кнопка активна, письмо не закрывалось.
- **AC-4.5** Given событие сработало на 5-й день пакетного прогона «Следующий месяц», а письмо доставлено после его окончания (день 31), `timerDays = 14`, When читается экземпляр, Then `deadlineDay` = день доставки + 14, а не день срабатывания + 14.
- **AC-4.6** Given вариант «Нанять» стоимостью $60 000 000 при деньгах $5 000 000 и пороге дефолта −$40 000 000 (после траты баланс был бы −$55 000 000), When открыт выбор, Then кнопка неактивна с причиной «Не хватит кредитного лимита на $15 000 000»; вариант по умолчанию остаётся доступным.

**Complexity:** High
**Dependencies:** FR-5, FR-7; mod-framework (`ModMail`, `ModCredit`).
**Assumptions:** `rival-personalities` и `historical-events` подключают свои политики ИИ через `EventAi.AddChooser`. Ядро (`ModMail.Send` с `Priority = Critical`) доставляет письмо с выбором в день отправки, кроме перемотки, загрузки и пакетного прогона.

### FR-5: Эффекты: вычисление один раз и применение

**User Story:** Как игрок, я хочу, чтобы обещанное в тексте события совпадало с тем, что реально изменилось, чтобы доверять игре.

**Priority:** Must

#### Acceptance Criteria

1. WHEN экземпляр создаётся THEN event-system SHALL для каждого эффекта вызвать `Resolve` один раз (все броски и формулы) и сохранить результат как `AppliedEffect`.
2. WHEN эффект применяется, показывается в письме, окне или журнале THEN event-system SHALL использовать только сохранённые числа `AppliedEffect`.
3. WHEN эффект имеет `durationDays > 0` THEN event-system SHALL держать его активным до `startDay + durationDays` и снять в день истечения.
4. WHEN эффект имеет `params.chance < 1` THEN event-system SHALL бросить шанс при `Resolve`, хранить исход скрытым до решения и применить эффект только при успехе.
5. WHERE несколько эффектов одного варианта имеют общий `params.exclusiveGroup` event-system SHALL одним броском выбрать не более одного из них по накопленным шансам.
6. WHEN эффекты событий используют канал `ModModifiers` THEN event-system SHALL отдавать вклад через один провайдер на канал (`ModModifiers.RegisterReasonedProvider`, регистрация один раз): произведение вкладов активных эффектов компании контекста и глобальных эффектов, ограниченное пределами событий (по умолчанию [0.25; 4.0], `production.output` — [0.1; 3.0]). Провайдер SHALL отдавать причину (`ReasonKey` движка и названия активных событий) для `ModModifiers.Explain`. WHEN вклад меняется (выбор игрока, ежедневный `Tick` кривой, истечение) THEN event-system SHALL вызвать `ModModifiers.Invalidate(channel, companyKey)`, чтобы изменение действовало сразу, а не со следующего игрового дня.
7. The event-system SHALL поддерживать стандартные типы: `Money`, `ModifierTimed`, `Popularity`, `Reputation`, `ResearchProgress`, `FactoryOutput`, `MarketDemand`, `SendMail`, `FireEvent`.
8. IF применение эффекта бросает исключение THEN event-system SHALL перехватить его, отметить эффект `Failed`, записать в лог и продолжить обработку дня.
9. WHERE эффект помечен `params.scalable` THEN event-system SHALL при `Resolve` применить `PackOptions.Severity` (множитель `1 + (m − 1)·s`, сумма `× s`) и затем зажать множитель пределами канала (самый узкий из `channelLimits` паков). Показанное и применённое число SHALL совпадать (значение уже зажато и помечено «ограничено»).
10. WHEN `Money` с `direction: Earn` не задаёт `origin` THEN event-system SHALL использовать `RevenueOrigin.Interest`: поступления событий не смешиваются с продажами в отчётах (решение владельца).

**Сценарии:**
- **AC-5.1** Given эффект `Money` с долей [0.04; 0.06] квартальной выручки $20 000 000, When событие срабатывает, Then в письме, журнале и балансе одна и та же сумма (например, −$1 041 000), и `Company.SpendMoney` вызван один раз с этой суммой.
- **AC-5.2** Given два активных `ModifierTimed` на `research.speed` ×1.12 и ×1.10 у игрока, When вклады выставлены, Then `ModModifiers.Get("research.speed", игрок)` = 1.232 (сразу после `Invalidate`), а `ModModifiers.Explain` показывает одну строку провайдера `event-system` с причиной.
- **AC-5.3** Given эффекты A (`chance` 0.6) и B (`chance` 0.4) в группе `verdict`, When `Resolve`, Then ровно один из них помечен как выпавший.
- **AC-5.4** Given игрок выбирает вариант на паузе в середине дня, When выбор применён, Then множитель канала виден `ModModifiers.Get` в тот же момент (вызван `Invalidate`).
- **AC-5.5** Given `scalable` эффект `FactoryOutput ×0.55` и интенсивность пака ×2, предел канала `production.output` [0.4; 1.5], When `Resolve`, Then значение 0.4, пометка «ограничено», в письме и в канале одно и то же число.

**Complexity:** High
**Dependencies:** FR-9; mod-framework (`ModModifiers`, `ModReputation`, `ModMail`).
**Assumptions:** `ModModifiers.RegisterReasonedProvider`, `ModModifiers.Invalidate` и реестр каналов ядра (все каналы объявлены в `core.json` до `Register` фич) работают по mod-framework/design.md; провайдер выключенной фичи не применяется; регистрация на ещё не объявленный канал связывается лениво.

### FR-6: API расширения для других фич

**User Story:** Как автор спеки `market-shocks`, `design-flaw-recalls`, `historical-events` или `rival-personalities`, я хочу добавлять свои события, типы эффектов, предикаты и вызывать события из кода, чтобы не менять код движка.

**Priority:** Must

#### Acceptance Criteria

1. WHEN фича вызывает `EventEffects.Register(type, handler)` в своём `Register` THEN event-system SHALL использовать обработчик для эффектов этого типа во всех паках.
2. WHEN фича вызывает `EventConditions.Register(type, ownerFeature, predicate)` THEN event-system SHALL зарегистрировать предикат в `ModConditions` и вычислять его в `conditions`, `targets.filter` и `requires`. Имя предиката SHALL иметь вид `<prefix>.<name>` с префиксом фичи-владельца (`es.` у движка, `ms.` у market-shocks); `ModConditions.Register` отклоняет остальные имена.
3. WHEN код вызывает `EventBus.Fire(eventId, context)` THEN event-system SHALL сработать событие с указанной целью и задержкой, соблюдая `oncePerGame` и, если не задано `IgnoreConditions`, его условия.
4. WHEN событие срабатывает, решается или его эффект истекает THEN event-system SHALL поднять `EventBus.OnEventFired`, `EventBus.OnChoiceResolved` или `EventBus.OnEffectExpired`; исключения подписчиков SHALL перехватываться по одному.
5. IF тип эффекта зарегистрирован повторно THEN event-system SHALL оставить первую регистрацию и записать предупреждение.
6. WHEN фича вызывает `EventEffects.ApplyDirect(spec, companyKey, sourceId, args)` THEN event-system SHALL вычислить и применить эффект вне события (служебный экземпляр `Direct`), вести его срок, сохранение и журнал так же, как у эффектов событий.
7. WHEN фича вызывает `EventAi.AddChooser(chooser, priority)` THEN event-system SHALL вести список chooser'ов по убыванию приоритета; решение ИИ SHALL принадлежать первому chooser'у, вернувшему не `null` (chooser возвращает `null` для чужих событий либо реализует `IEventAiChooserFilter.Handles`); если все вернули `null` — действует политика по умолчанию.
8. WHERE предикат `es.activeInCategory` вызван с параметром `scope` THEN event-system SHALL понимать значения `subject` (по умолчанию: у подлежащего есть ожидающий выбор или активный эффект события категории) и `any` (у любой компании, а также активные глобальные эффекты категории) — для координации паков, например «не запускать шок памяти, пока идёт историческое событие».
9. The event-system SHALL регистрировать предикаты `es.eventFired`, `es.choiceMade`, `es.eventFlag`, `es.activeInCategory`, `es.hasRevenueHistory`, `es.hasFab` с владельцем `event-system`.
10. WHEN пак регистрируется `EventRegistry.RegisterPack(packId, folder, options)` THEN event-system SHALL принять `PackOptions`: `SettingsFeatureId`, `Frequency`, `Severity`, `EventsFile` (имя файла событий, по умолчанию `events`), `RivalNewsTopN` (новости о событиях ИИ только по N компаниям с наибольшей долей рынка; 0 — обо всех).

**Сценарии:**
- **AC-6.1** Given `market-shocks` зарегистрировал `ModifierRamp`, When загружается его пак, Then события с `ModifierRamp` валидны и работают.
- **AC-6.2** Given подписчик `OnChoiceResolved` бросает исключение, When игрок выбирает вариант, Then эффект применён, другие подписчики вызваны, в логе есть ошибка.
- **AC-6.3** Given `historical-events` вызывает `EventEffects.ApplyDirect` с `ModifierTimed market.demand.lowend ×0.9` на 90 дней, When сейв сохраняется и загружается на 30-й день, Then эффект активен ещё 60 дней и виден в журнале с источником.
- **AC-6.4** Given два chooser'а: приоритет 100 возвращает `null` для события `es.x`, приоритет 10 возвращает `"settle"`, When ИИ решает `es.x`, Then выбран `"settle"`; When приоритет 100 возвращает `"fight"`, Then выбран `"fight"`, второй chooser не вызывается.
- **AC-6.5** Given идёт активное событие категории `history` у игрока, а в `market-shocks` условие `{ "not": { "type": "es.activeInCategory", "category": "history", "scope": "any" } }`, When лотерея выбирает шок, Then шок не подходит; с `scope: "subject"` и целью-ИИ — подходит.

**Complexity:** Medium
**Dependencies:** FR-1, FR-5.
**Assumptions:** регистрация расширений идёт в `Register` фич-потребителей; контент паков грузится после всех `Register` (FR-1).

### FR-7: UI выбора: письмо, строка таймера, попап, модальное окно

**User Story:** Как игрок, я хочу получать событие письмом с кнопками и видеть обратный отсчёт, чтобы решить сразу или позже, не теряя событие.

**Priority:** Must

#### Acceptance Criteria

1. WHEN событие с выбором срабатывает для игрока THEN event-system SHALL отправить письмо через `ModMail.Send` (`FeatureId = "event-system"`, `Tag = "es:<instanceId>"`, `Priority = Critical`, без собственного срока `TimeoutDays = 0`) с заголовком, причиной, описанием, вариантами с точными эффектами и кнопкой на каждый вариант. Событие без выбора для игрока SHALL уходить письмом `Normal`, новость о событии ИИ (`notifyPlayer`) — письмом `Low`.
2. WHEN событие срабатывает для игрока THEN event-system SHALL показать уведомление через `ModNotify` с названием и числом дней на решение.
3. WHERE событие помечено `important` или в настройках включено «Пауза на всех событиях с выбором» event-system SHALL открыть модальное окно выбора с обратным отсчётом («Осталось 9 из 14 дн.» и полоса из 14 делений); окно ставит игру на паузу (`PauseTrigger` шаблона `WindowTemplate.Modal`). Модальное окно с паузой — только для `important` или по настройке (решение владельца).
4. WHEN игрок нажимает «Решить позже» THEN event-system SHALL закрыть окно, сохранив письмо и таймер.
5. WHEN до дедлайна остаётся 3 дня и решения нет THEN event-system SHALL показать напоминание через `ModNotify`.
6. WHEN выбор применён автоматически или из окна THEN event-system SHALL закрыть письмо этим ответом через `ModMail.Resolve` (панель выбора скрывается, ответ виден в письме); при автоматическом решении — показать уведомление «Выбрано автоматически: …».
7. IF одновременно нужно открыть несколько модальных окон THEN event-system SHALL показывать их по очереди в порядке срабатывания.
8. WHEN событие без выбора срабатывает для игрока или с флагом `notifyPlayer` для ИИ THEN event-system SHALL отправить информационное письмо (новость). IF у пака задано `PackOptions.RivalNewsTopN` > 0 THEN новость о событии ИИ SHALL отправляться только при условии, что компания входит в N ИИ-компаний с наибольшей долей рынка (`MarketDataProvider.MarketShare`) на день срабатывания; настройка «Новости о конкурентах» выключает новости целиком.
9. WHEN письмо доставлено (`ModMail.Delivered`) и далее раз в игровой день, пока выбор ожидает решения, THEN event-system SHALL выводить срок строкой статуса письма (`ModMail.SetStatusLine`): «Ответить до 15.06.1985 (14 дн.)», затем «Осталось 9 из 14 дн.».

**Сценарии:**
- **AC-7.1** Given `important` событие, When оно срабатывает на скорости 3, Then время на паузе, окно показывает «Осталось 14 дн.», после выбора время продолжает идти с прежней скоростью.
- **AC-7.2** Given игрок закрыл окно «Решить позже», When прошло 11 дней, Then появилось напоминание «Осталось 3 дн.», письмо по-прежнему с активными кнопками, строка статуса письма «Осталось 3 из 14 дн.».
- **AC-7.3** Given в один день сработали выбор для игрока (`Critical`) и три новости о событиях ИИ (`Low`) при полном дневном лимите писем `Normal`, When ядро доставляет почту, Then письмо с выбором доставлено в этот же день, новости — по остатку лимита.
- **AC-7.4** Given `RivalNewsTopN = 5` и событие у ИИ с 6-м местом по доле рынка, When оно срабатывает, Then новость игроку не отправляется, а в журнале запись есть.

**Complexity:** High
**Dependencies:** mod-framework (`ModMail`, `ModNotify`, `ModUI`, `ModL10n`, `ModTicks`); решение 0005.
**Assumptions:** `ModMail.Send` с `Choices`, `Tag`, `Priority`, `ModMail.RegisterChoiceHandler`, `ModMail.Delivered`, `ModMail.SetChoiceState`, `ModMail.SetStatusLine`, `ModMail.Resolve` (mod-framework/design.md); `Critical` идёт в обход дневного лимита, но не раньше `GameReady` и не во время пакетного прогона. Шаблон `WindowTemplate.Modal` (клон `BreakContractWindow`) содержит `PauseTrigger`.

### FR-8: Журнал событий

**User Story:** Как игрок, я хочу видеть историю событий и активные эффекты с точными числами и сроками, чтобы понимать, что сейчас влияет на мою компанию и рынок.

**Priority:** Must

#### Acceptance Criteria

1. WHEN событие срабатывает, решается, отменяется или его эффект истекает THEN event-system SHALL добавить запись в журнал: дата, событие, компания, вариант, отметка «авто», эффекты с сохранёнными числами и сроками.
2. WHEN игрок открывает «Журнал событий» из хаба «Моды» THEN event-system SHALL показать вкладки «История» (новые сверху) и «Активные эффекты» (канал, величина, компания или «весь рынок», дата окончания, осталось дней).
3. WHERE включён фильтр «Только моя компания» event-system SHALL показывать записи игрока и глобальные эффекты.
4. IF записей больше 400 THEN event-system SHALL удалять самые старые.
5. WHEN язык интерфейса меняется THEN event-system SHALL перерисовать записи из ключей и сохранённых аргументов.

**Сценарии:**
- **AC-8.1** Given активен `research.speed` ×1.12 до 1986-06-01, When 1985-12-01 открывается вкладка «Активные эффекты», Then видна строка «Скорость исследований +12 % — до 01.06.1986 (182 дн.)».
- **AC-8.2** Given журнал на 400 записей, When добавляется 401-я, Then самая старая удалена.

**Complexity:** Medium
**Dependencies:** FR-5, FR-10; mod-framework (`ModUI`).
**Assumptions:** нет.

### FR-9: Детерминизм и защита от переигрывания

**User Story:** Как игрок, я хочу, чтобы после загрузки сейва события не переигрывались заново, чтобы решения имели вес.

**Priority:** Must

#### Acceptance Criteria

1. The event-system SHALL брать случайность только из потока `ModRandom.Stream("event-system/daily")` и из производных генераторов ниже; `UnityEngine.Random` и `System.Random` SHALL не использоваться.
2. WHEN обрабатывается день THEN event-system SHALL расходовать фиксированное число значений дневного потока: 3 на каждую категорию с событиями `DailyChance`, независимо от исхода и подходящих событий.
3. WHEN вычисляются эффекты экземпляра THEN event-system SHALL использовать производный генератор с сидом `hash(ModRandom.WorldSeed, id экземпляра)`, не сдвигая дневной поток.
4. WHEN нужен бросок для триггеров `Date`, `Condition` или `Manual` (шанс, выбор цели) THEN event-system SHALL использовать производный генератор с сидом `hash(ModRandom.WorldSeed, id события, день)`.
5. WHEN сейв загружается и игрок повторяет те же действия THEN event-system SHALL воспроизвести ту же последовательность случайных решений мода. События при загрузке не перебрасываются, настройки «перебрасывать при загрузке» нет (решение владельца).

**Сценарии:**
- **AC-9.1** Given сейв 1990-01-01, When дважды загружается и 365 дней идёт без действий игрока, Then журналы событий за год совпадают, если совпадают подходящие события.
- **AC-9.2** Given в тесте добавлено новое событие в категорию, When прогоняется 1000 дней, Then последовательность бросков категории (u1) не изменилась.

**Complexity:** Medium
**Dependencies:** mod-framework (`ModRandom`).
**Assumptions:** `ModRandom.Stream(streamId)` сохраняет состояние потока в `ModSave`, `ModRandom.WorldSeed` постоянен для партии (mod-framework/design.md). Производные генераторы — локальная детерминированная реализация `IModRng` в движке.

### FR-10: Сохранение и загрузка

**User Story:** Как игрок, я хочу сохранить игру посреди события и после загрузки увидеть тот же таймер, те же варианты и активные эффекты.

**Priority:** Must

#### Acceptance Criteria

1. WHEN игра сохраняется (postfix ядра P12 на `SaveSystem.Save`, включая автосейв; ядро вызывает `IModSaveParticipant.Write`) THEN event-system SHALL записать секцию `event-system` в `ModSave`: экземпляры, активные эффекты, отложенные `FireEvent`, откаты, флаги цепочек, журнал. Поле версии секции (`_schema`) пишет и читает ядро: в данных секции своего поля версии нет.
2. WHEN сейв загружается (`IModSaveParticipant.Read` на `SaveHandler.OnLoaded`, затем `ModTicks.GameReady(Loaded)`) THEN event-system SHALL в обработчике `GameReady(Loaded)` восстановить состояние, вернуть вклады активных эффектов в провайдеры (`Restore`), связать ожидающие выборы с письмами `ModMail` по `Tag` (`ModMail.Find`) и пересчитать состояние кнопок и строку статуса писем (`ModMail.SetChoiceState`, `ModMail.SetStatusLine`).
3. IF секции нет (старый сейв или мод добавлен позже) THEN event-system SHALL начать с пустого состояния без ошибок (`IModSaveParticipant.ResetToDefaults`).
4. IF в сохранённом состоянии есть событие, которого нет в контенте THEN event-system SHALL отменить его экземпляры и активные эффекты, закрыть их письма (`ModMail.Resolve`) и записать это в журнал.
5. WHEN начинается новая игра (`ModTicks.GameReady(NewGame)`, после перемотки) THEN event-system SHALL сбросить состояние в памяти и начать отсчёт с текущей даты.
6. IF мод удалён THEN сейв игры SHALL загружаться без мода: состояние движка хранится только в боковом файле.

**Сценарии:**
- **AC-10.1** Given ожидающий выбор с дедлайном 1988-04-20 и активный `production.output` ×0.6, When сохранить 1988-04-10 и загрузить, Then письмо с кнопками на месте, «Осталось 10 дн.», множитель снова зарегистрирован.
- **AC-10.2** Given сейв ванильной игры без бокового файла, When он загружается с модом, Then ошибок нет, журнал пуст, события начинают приходить.
- **AC-10.3** Given письмо с серой кнопкой варианта (нет исследования) сохранено и загружено, When наступает `GameReady(Loaded)`, Then состояние кнопки пересчитано по текущему условию.

**Complexity:** Medium
**Dependencies:** mod-framework (`ModSave`, `ModMail`); решение 0004.
**Assumptions:** `ModMail` сам хранит и восстанавливает письма мода (с датами, признаком прочтения, состоянием кнопок и строкой статуса); движок находит письмо экземпляра по `Tag` и обрабатывает ответы через `ModMail.RegisterChoiceHandler`.

### FR-11: Настройки и выключение посреди игры

**User Story:** Как игрок, я хочу настроить частоту событий или выключить их, чтобы подстроить сложность и темп.

**Priority:** Should

#### Acceptance Criteria

1. The event-system SHALL давать в хабе «Моды» (`FeatureContext.Param`, `FeatureContext.HubPage`) настройки:
   - вкл/выкл фичи;
   - частота: «Выкл» ×0, «Редко» ×0.5, «Обычно» ×1, «Часто» ×1.5;
   - «Пауза на всех событиях с выбором» (по умолчанию выкл);
   - «Новости о конкурентах» (по умолчанию вкл).
2. WHEN фича выключается посреди игры THEN event-system SHALL перестать срабатывать события; её провайдеры `ModModifiers` перестают действовать (правило ядра для выключенной фичи), записи и состояние сохраняются без изменений (заморозка): `OnDisabled` провайдеры и вклады не удаляет и не пересоздаёт.
3. WHEN фича включается снова THEN event-system SHALL вернуть множители эффектов, у которых не прошёл `endDay`; истёкшие за это время эффекты SHALL закрываться записью в журнале.
4. WHILE фича выключена event-system SHALL не уменьшать таймеры ожидающих выборов: WHEN фича включается снова THEN дедлайны выборов игрока SHALL сдвигаться на число дней выключения. Кнопки писем выключенной фичи неактивны (правило ядра).
5. WHERE в отладочной сборке включён флаг `ModSettings.Debug("debug.events")` event-system SHALL показывать в хабе список загруженных событий с кнопкой «Запустить сейчас» (цель — игрок, через `EventBus.Fire` с `IgnoreConditions`) для ручной проверки. В релизной сборке флаг всегда ложен.

**Сценарии:**
- **AC-11.1** Given активный `market.demand.highend` ×0.8 до 1992-01-01, When фича выключена 1991-06-01, Then `ModModifiers.Get("market.demand.highend")` = 1.0, запись в сейве осталась; When включена 1991-09-01, Then снова 0.8, срок прежний.
- **AC-11.2** Given отладочная сборка и режим отладки, When владелец нажимает «Запустить сейчас» у `es.trade_show`, Then в тот же день приходит письмо события с тремя вариантами.
- **AC-11.3** Given ожидающий выбор с дедлайном через 10 дней, фича выключена на 20 дней, When включена, Then осталось 10 дней.

**Complexity:** Low
**Dependencies:** mod-framework (`ModSettings`, `ModFeatures`, `FeatureContext.HubPage`, `FeatureContext.Param`).
**Assumptions:** фича поддерживает горячее выключение (`IModFeature.SupportsHotToggle = true`); флаги `debug.*` существуют только в отладочной сборке.

### FR-12: Стартовый набор событий

**User Story:** Как игрок, я хочу получить набор универсальных событий сразу с движком, чтобы фича давала геймплей без контент-паков.

**Priority:** Must

#### Acceptance Criteria

1. The event-system SHALL поставлять `Content/event-system/events.json` с 10 записями:
   - самостоятельные: `es.star_engineer`, `es.university_grant`, `es.trade_show`, `es.tax_audit`, `es.factory_strike`, `es.patent_troll`, `es.charity_gala`, `es.rival_scandal`;
   - исходы цепочки: `es.patent_win`, `es.patent_loss`.
2. The набор SHALL использовать каждый стандартный тип эффекта, кроме `MarketDemand`, хотя бы один раз, и демонстрировать выбор с таймером, шанс, эксклюзивную группу, цепочку `FireEvent`, цели `Player`, `RandomAI` и `RandomCompany`.
3. WHEN у события есть денежный эффект THEN сумма SHALL считаться от квартальной выручки цели с границами, умноженными на инфляцию игры (`InflationHandler.Instance.Inflation`).
4. The строки набора SHALL быть в `ru` и `en`; названия компаний в текстах SHALL подставляться из игры (`ICompany.get_Name`) и не хардкодиться.
5. The стартовый набор SHALL писать в общие каналы только множители `popularity.gain` ×1,25 / ×1,10 (`es.trade_show`) и ×0,85 (`es.rival_scandal`) и SHALL не писать в `market.rating.cpu` (NFR-8).

**Сценарии:**
- **AC-12.1** Given новая игра 1980 года на «Обычно», When идёт 10 игровых лет, Then приходит в среднем 2–4 стартовых события в год для игрока, ни одно не повторяется для той же компании чаще своего `cooldownDays`.

**Complexity:** Medium
**Dependencies:** FR-1…FR-7.
**Assumptions:** баланс — черновой, окончательные числа после ручной проверки владельцем.

### FR-13: Защиты времени

**User Story:** Как игрок, я хочу, чтобы события не срабатывали во время перемотки новой игры и загрузки и не «сгорали» при пакетной прокрутке месяца.

**Priority:** Must

#### Acceptance Criteria

1. WHILE `DateController.Instance.IsFastFowardSimulating` или `GameManager.Instance.IsLoading` event-system SHALL не обрабатывать дни (защиты `ModTicks`: `ModTicks.IsFastForwarding`, `ModTicks.IsLoading`). Во время перемотки фича нейтральна: событий, писем и эффектов нет (общая политика владельца).
2. WHILE `ModTicks.IsBatchRun` (выполняется `DateController.NextMonth`, до 31 дня в одном кадре) event-system SHALL не уменьшать таймеры выборов игрока (дедлайны ожидающих выборов смещаются на +1 за каждый обработанный день прогона), не открывать модальные окна и не экспирировать экземпляры с недоставленным письмом; окна SHALL открываться по `ModTicks.BatchEnded`. Собственного патча на `DateController.NextMonth` у event-system нет.
3. WHEN после загрузки первый обработанный день отстоит от `lastProcessedDay` больше чем на 1 THEN event-system SHALL обработать только текущий день, без догоняющей симуляции.

**Сценарии:**
- **AC-13.1** Given новая игра 1990 года (перемотка с 1971), When перемотка завершена, Then журнал пуст и активных эффектов нет.
- **AC-13.2** Given ожидающий выбор «Осталось 5 дн.», When нажата «Следующий месяц», Then после прогона по-прежнему «Осталось 5 дн.».

**Complexity:** Low
**Dependencies:** mod-framework (`ModTicks`).
**Assumptions:** `NextMonthButton` вызывает `DateController.NextMonth` синхронно (`pt-modding/code/.../NextMonthButton.cs`); признак прогона ведёт патч ядра P21.

## Non-Functional Requirements

### NFR-1: Производительность

- WHEN обрабатывается обычный день без срабатываний THEN event-system SHALL укладываться в среднем в 0,3 мс (замер `Stopwatch` в отладочной сборке, 365 дней, сейв 2000 года с 13 ИИ).
- WHEN в день срабатывает событие THEN обработка дня (без построения окна) SHALL занимать не больше 3 мс.
- The event-system SHALL брать список компаний только из кеша `ModCompanies.All` и не вызывать `DataFinder.FindAllCompanies` / `Object.FindObjectsByType`.
- Ежедневное обновление строк статуса и состояния кнопок писем затрагивает не больше `maxPendingChoices` писем.

### NFR-2: Надёжность

- IF внутри обработчика дня, кнопки, загрузки или сохранения возникает исключение THEN event-system SHALL перехватить его на границе хука (`ModHooks.Guard`), записать в `ModLog` и не пропустить в код игры. Счётчик сбоев фичи ведёт ядро (`ModHooks.Guard`, `ModHooks.ReportFault`).
- IF одно событие трижды за сессию падает при обработке THEN event-system SHALL отключить его до конца сессии.
- The event-system SHALL не менять поля классов игры, кроме разрешённых эффектами (`CpuBase.Popularities`, `ResearchSector.Research.Progress`, деньги через `ICompany.EarnMoney`/`ICompany.SpendMoney`).

### NFR-3: Совместимость сейвов

- The event-system SHALL хранить состояние только в секции `event-system` бокового файла `ModSave`. Версию секции (`_schema`, начиная с 1) пишет и читает ядро (`IModSaveParticipant.SchemaVersion = 1`); собственного поля версии в данных секции нет.
- IF `_schema` секции больше поддерживаемой THEN event-system SHALL работать с пустым состоянием (`ResetToDefaults`), а исходная секция SHALL переписываться без изменений (passthrough `ModSave`).
- The сейвы игры SHALL оставаться ванильными: удаление мода не ломает их загрузку.

### NFR-4: Локализация

- The все строки движка и стартового набора SHALL идти через `ModL10n` с ключами `es.*` на `ru` (по умолчанию) и `en`.
- IF ключ отсутствует в текущем языке THEN SHALL показываться `en`, затем сам ключ; самопроверка на старте SHALL перечислить недостающие ключи и ключи с чужим префиксом.

### NFR-5: Удобство

- The число на экране и применённое число SHALL совпадать побитно: оба берутся из одного поля `AppliedEffect.value`.
- WHEN появляется событие с выбором THEN игрок SHALL добраться до кнопок решения не больше чем за 2 клика от уведомления.
- The каждое событие SHALL показывать причину (`cause`) и срок действия эффектов.

### NFR-6: Расширяемость

- WHEN добавляется новое событие THEN SHALL требоваться только запись JSON и строки L10n.
- WHEN добавляется новый тип эффекта THEN SHALL требоваться один класс `IEventEffectHandler` и вызов `EventEffects.Register`.

### NFR-7: Безопасность данных

- WHEN читается контент или секция сохранения THEN event-system SHALL проверять типы и диапазоны чисел (те же пределы, что при загрузке контента) и отбрасывать некорректные записи, чтобы ручная правка файлов не давала множителей вне пределов.
- The event-system SHALL не выполнять сетевых запросов и не читать файлы, кроме как через `ModContent` и `ModSave`.
- IF цепочка `FireEvent` глубже 8 уровней или событие вызывает само себя с задержкой 0 THEN event-system SHALL отклонить запись при загрузке.

### NFR-8: Вклад в общие каналы

- The event-system SHALL указывать свой максимальный вклад в каналы, где складываются несколько фич. `popularity.gain`: вклад всех событий всех паков ограничен [0,7; 1,6] (`channelLimits`); стартовый набор пишет ×0,85 … ×1,25. `market.rating.cpu`: стартовый набор не пишет; вклад паков ограничивается их собственными `channelLimits`, а итог на CPU по всем фичам — балансовым диапазоном ядра [0,5; 2,0] (в тестах эффектов учитывать это ограничение).

## Edge Cases

| # | Ситуация | Ожидаемое поведение |
|---|---|---|
| E1 | Цель обанкротилась до решения или во время действия эффекта | Экземпляр отменён; эффекты компании сняты на следующий день; запись в журнале «компания обанкротилась» |
| E2 | Деньги изменились между срабатыванием и решением | Предупреждение о минусе и доступность варианта пересчитываются ежедневно и в окне; сумма эффекта не пересчитывается |
| E3 | `Finance.Reports.Count < 3` (новая компания, `CompanyFinance.QuarterlyRevenue` упал бы по индексу) | Квартальная выручка = 0, работает нижняя граница суммы |
| E4 | Нет текущего исследования для `ResearchProgress` | Эффект пропущен с пометкой «нет текущего исследования»; в стартовом наборе это исключено условием |
| E5 | У компании нет CPU на рынке для `Popularity` | Пустое действие с пометкой в журнале |
| E6 | Пак удалён между сессиями | Экземпляры неизвестных событий отменены, множители сняты, запись в журнале |
| E7 | Открыт инбокс: `EmailWindow.OnEnable` помечает все письма прочитанными | Таймер и кнопки не зависят от флага «прочитано»; напоминание за 3 дня приходит всё равно |
| E8 | Два важных события в один день | Окна показываются по очереди |
| E9 | Дата `Date`-триггера раньше стартового года игры | Срабатывание пропускается, если старт позже даты больше чем на `graceDays` |
| E10 | Смена языка в середине события | Тексты перерисовываются из ключей; числа не меняются |
| E11 | Автосейв 1 января идёт после `OnDayPassed` в том же тике | В боковой файл попадает состояние после обработки дня |
| E12 | Загрузка другого сейва в той же сессии | Вклады в провайдерах `event-system` полностью сбрасываются перед восстановлением |
| E13 | Компания ИИ появилась позже (`CompanySpawner`) | Становится целью после обновления кеша `ModCompanies` (событие `CompanySpawner.OnCompanySpawned`) |
| E14 | Произведение множителей канала вышло за предел | Значение ограничено пределом; в журнале у эффекта пометка «ограничено» |
| E15 | Игрок выбрал вариант на паузе посреди дня | Деньги и разовые эффекты — сразу; множители каналов — сразу после `ModModifiers.Invalidate` для затронутых каналов |
| E16 | Лимит доставки `ModMail` (3 письма `Normal`/`Low` в день) | Письмо выбора (`Critical`) идёт в обход лимита; если доставка всё же задержана (перемотка, загрузка, пакетный прогон), дедлайн считается от дня доставки, а окно и уведомление показываются сразу по предварительному сроку |
| E17 | Событие сработало посреди пакетного прогона `NextMonth` | Письмо и модальное окно ждут `BatchEnded`; дедлайн — от дня доставки; экземпляр до доставки не истекает |
| E18 | Фича-выключатель пака выключена, потом включена | Эффекты и выборы пака заморожены только на это время; сроки выборов сдвинуты, сроки эффектов остаются абсолютными |
| E19 | Пользователь удалил `PTMod/content/<pack>/events.json`-переопределение | Используется встроенный файл (правило `ModContent`) |

## Constraints and Assumptions

### Technical Constraints

- Unity 6 Mono, сборка `PTMod.dll` (решения 0001–0003). Сборка — Roslyn `csc` (C# latest); код `Logic` использует общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`/`Range`). Тесты — мини-раннер .NET Framework 4.8 (`python mod/build.py --tests`).
- Состояние только в `ModSave` (решение 0004); поля в классы игры не добавляются.
- UI строится в рантайме из шаблонов игры (решение 0005).
- Контент — JSON (решение 0006).
- Патчи Harmony — только через `ModHooks` и только у ядра: у движка собственных патчей нет. `Company.AddCpu` / `AICompany.AddCpu`, `DateController.NextMonth`, `EmailWindow.GetEmails` и `EmailWindow.UpdateEmailText` для фич закрыты (`ModHooks.IsReserved`).

### Business Constraints

- Одиночная игра, без сети.
- Проверка в игре — ручной шаг владельца, не во время онлайн-игр с античитом.
- Реализация — по одобренной спеке; одобрена владельцем 2026-10-10.

### Assumptions

- API `mod-framework` v0.2 (`ModTicks`, `ModSave`, `ModModifiers`, `ModConditions`, `ModMail`, `ModNotify`, `ModUI`, `ModL10n`, `ModSettings`, `ModRandom`, `ModContent`, `ModLog`, `ModReputation`, `ModCredit`, `ModIds`) реализован по контракту `steering/context.md`.
- Стандартные каналы `ModModifiers` (`research.speed`, `production.output`, `market.demand.<segment>`, `popularity.gain`, `factory.operatingCost`, `taxes` и др.) читаются патчами `mod-framework`; все они и каналы фич объявлены реестром ядра до `Register` фич.

## Success Criteria

### Definition of Done

- [ ] Все критерии приёмки FR-1…FR-13 выполнены, сценарии AC-* проверены (юнит-тестами или вручную).
- [ ] NFR-1…NFR-8 выполнены; замер производительности приложен к задаче TSK-027.
- [ ] Юнит-тесты чистой логики зелёные (`python mod/build.py --tests`, `mod/tests/PTMod.Tests/EventSystem/`); каждый публичный тип `Logic` указан в `[Covers]`.
- [ ] Самопроверка на старте: все типы эффектов зарегистрированы, ключи L10n на месте и с верным префиксом, ошибок контента нет.
- [ ] Ручная проверка владельцем (TSK-028) пройдена.
- [ ] Скрипты проверки ссылок (`tools/check_spec_refs.py` и режим `--core-api`) зелёные.

### Acceptance Metrics

- Частота: на «Обычно» игрок получает 2–4 стартовых события в год (среднее за 10 лет симуляции).
- Расхождение показанного и применённого: 0 случаев (тест сверяет `AppliedEffect.value` с суммой в `Company.SpendMoney` или `Company.EarnMoney`).
- Воспроизводимость: 100 % совпадение бросков категорий при повторной загрузке (тест).
- Потеря событий: 0 ожидающих выборов, «сгоревших» без записи в журнале.
- Дедлайн: 0 выборов, истёкших до доставки письма.

## Решения по умолчанию

Владелец ответил на открытые вопросы v0.1 в `.kiro/steering/owner-decisions.md` (2026-10-10). Ниже — ответы, применённые в этой спеке, и решения, принятые по общему правилу («если варианта нет — самый простой безопасный»). Любое решение можно переиграть: каскадом правятся `requirements.md` → `design.md` → `tasks.md`.

### Ответы на вопросы v0.1

| № | Вопрос v0.1 | Решение | Где в спеке | Источник |
|---|---|---|---|---|
| 1 | Частота (~4 события в год, игроку ~3) | Частоты приняты | FR-2, FR-12, `config.json` | owner-decisions, «event-system» |
| 2 | Модальность | Модальное окно с паузой — только для `important`; «Пауза на всех событиях с выбором» остаётся настройкой (по умолчанию выкл) | FR-7 п. 3, FR-11 | owner-decisions, «event-system» |
| 3 | События во время перемотки | Фичи нейтральны: нет событий, писем и эффектов | FR-13 п. 1 | owner-decisions, «Общие политики» п. 1 |
| 4 | Переигрывание при загрузке | События при загрузке не перебрасываются; настройки «перебрасывать» нет | FR-9 п. 5 | owner-decisions, «event-system» |
| 5 | Деньги для ИИ | Одинаковые правила для ИИ и игрока, эффекты для ИИ не ослабляются | FR-3 п. 7 | owner-decisions, «Общие политики» п. 3 |
| 6 | Метка пакетного прогона | Переехала в ядро: `ModTicks.IsBatchRun` / `BatchEnded`; собственный патч на `DateController.NextMonth` убран | FR-13 п. 2 | owner-decisions, «Общие политики» п. 5 |
| 7 | Вид дохода | Поступления событий — `RevenueOrigin.Interest` | FR-5 п. 10 | owner-decisions, «event-system» |
| 8 | Размер журнала | 400 записей | FR-8 п. 4 | owner-decisions, «event-system» |
| 9 | Задержка множителей | Сброс кеша по запросу: `ModModifiers.Invalidate`; множитель действует сразу | FR-5 п. 6, AC-5.4 | owner-decisions, «Общие политики» п. 5 |
| 10 | Недоступные варианты в письме | `ModMailChoice.Enabled` / `DisabledReasonKey`, `ModMail.SetChoiceState` / `Refresh`; вариант с ложным `requires` остаётся в письме серым | FR-4 п. 4 | owner-decisions, «Общие политики» п. 5 |

### Решения по замечаниям кросс-ревью и общим политикам

| Тема | Решение | Источник |
|---|---|---|
| Политика ИИ | `EventAi.AddChooser(chooser, priority)`: список по убыванию приоритета, побеждает первый ненулевой ответ; `SetChooser` из v0.1 заменён | owner-decisions, «event-system»; замечание `eventai-chooser-semantics` |
| Письма и таймеры | Приоритеты `ModMail` (выбор — `Critical`, событие без выбора — `Normal`, новость об ИИ — `Low`); дедлайн от доставки (`ModMail.Delivered`), строка таймера `SetStatusLine` | замечание `modmail-global-daily-limit`; общие политики п. 5 |
| Идентификаторы | Предикаты движка с префиксом: `es.eventFired`, `es.choiceMade`, `es.eventFlag`, `es.activeInCategory`, `es.hasRevenueHistory`, `es.hasFab`; ID событий `es.<имя>`; тег письма `es:<instanceId>` | замечание `id-prefix-convention`; соглашение об идентификаторах |
| Координация паков | `es.activeInCategory` получает параметр `scope` (`subject` / `any`) | замечание `active-in-category-scope` |
| Версия секции сейва | Поле `_schema` пишет и читает ядро; из примера секции убрано | замечание `section-schema-key`; общие политики п. 5 |
| Имена API ядра | `TickPhase.DayPassed` через `FeatureContext.OnTick`, `ModTicks.GameReady`, `ModTicks.IsBatchRun`, патч P12 на `SaveSystem.Save` вместо несуществующих «ModTicks.OnDayPassed», «после SaveHandler.Save» и т. п. | замечание `decision-and-api-name-drift` |
| Пределы суммарного эффекта | Вклад событий в `popularity.gain` ограничен [0,7; 1,6], итог на CPU — балансовым диапазоном [0,5; 2,0] ядра | замечание `market-effect-stacking`; общие политики п. 4 |
| Нехватка денег для варианта | Вариант не блокируется, но подписывается предупреждением о минусе; блокируется (серая кнопка), только если списание опустило бы баланс ниже порога дефолта `ModCredit.ThresholdOf`; `defaultChoice` деньгами не блокируется | по общему правилу: самый простой безопасный вариант; пороги — только через `ModCredit` |
| Новости об ИИ | `PackOptions.RivalNewsTopN` (0 — обо всех); `market-shocks` ставит 5 | owner-decisions, «market-shocks» |
| Имя файла событий пака | `PackOptions.EventsFile` (по умолчанию `events`); `market-shocks` использует `shocks` | по общему правилу: опциональное поле, контракт не ломается |
| Порядок загрузки | Контент паков читается на первом `GameReady` после всех `Register` фич; восстановление состояния — в `GameReady(Loaded)`, не в `Read` | по общему правилу: порядок `Register` не должен влиять на результат |
| Реальные названия | Названия компаний в текстах подставляются из игры, ключи контента — по `UniqueID`; пародийных названий в спеке нет | owner-decisions, «Общие политики» п. 2 |
| Флаги отладки | `debug.events` только в отладочной сборке | owner-decisions, «Общие политики» п. 10 |
| Тулчейн и тесты | Roslyn `csc`, мини-раннер .NET Framework 4.8, `[Covers]` на публичных типах `Logic`; xUnit/NUnit не используются | решение 0003; owner-decisions, «Общие политики» п. 7 |

## Открытые вопросы владельцу

Открытых вопросов нет: все десять вопросов v0.1 закрыты `owner-decisions.md`, остальные решения приняты по общему правилу (таблица выше) и могут быть пересмотрены владельцем.

## Glossary

| Термин | Значение |
|---|---|
| Определение события (`EventDefinition`) | Запись JSON: триггер, условия, цели, выборы, эффекты |
| Экземпляр события (`EventInstance`) | Конкретное срабатывание для одной компании: дата, дедлайн, заранее вычисленные эффекты |
| Группа | Экземпляры одного срабатывания `AllCompanies`; глобальные эффекты применяются один раз на группу |
| Категория | Набор событий с общей дневной лотереей и правилами темпа (`dailyChance`, `minGapDays`, `maxActive`) |
| Пак | Контент одной фичи (`event-system`, `market-shocks` и т. д.), регистрируется `EventRegistry.RegisterPack` |
| `AppliedEffect` | Эффект с вычисленными и сохранёнными числами; единственный источник для показа и применения |
| Агрегатор | Компонент, сводящий множители активных эффектов в один множитель на пару (канал, компания) в `ModModifiers` |
| Ключ компании | `player` или `ai:<UniqueID>`; `global` — эффект на весь рынок |
| Квартальная выручка | `CompanyFinance.QuarterlyRevenue()` — сумма выручки трёх последних месячных отчётов |
| Инфляция | `InflationHandler.Instance.Inflation`: 0,5 в 1970-х … 8,5 в 2030-х |
| Дедлайн | День, когда без решения применяется `defaultChoice`; считается от дня доставки письма |
| Пакетный прогон | Синхронный прогон дней в одном кадре (`DateController.NextMonth`); признак `ModTicks.IsBatchRun` |
| Эксклюзивная группа | Эффекты одного варианта, из которых одним броском выпадает не более одного |
| Тег письма | `ModMailMessage.Tag` вида `es:<instanceId>`, по которому движок находит письмо экземпляра |
| Приоритет письма | `MailPriority` (`Critical` / `Normal` / `Low`): порядок доставки и обход дневного лимита |
