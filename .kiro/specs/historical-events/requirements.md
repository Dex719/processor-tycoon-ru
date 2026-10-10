# Requirements: historical-events

## Document Information

- **Feature Name**: historical-events — календарь реальных событий полупроводниковой индустрии 1971–2025, адаптированных к альтернативной истории партии
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Stakeholders**:
  - владелец форка (геймдизайн, выбор событий, баланс, ручная проверка);
  - игроки русификатора;
  - авторы смежных спек:
    - `event-system` — движок, окно выбора, журнал, `EventAi.AddChooser`;
    - `market-shocks` — случайные шоки, пересечение категорий;
    - `design-flaw-recalls` — API принудительного брака;
    - `mod-framework` — каналы `ModModifiers`, `ModReputation`, `ModSegments`, `ModCredit`, `ModMail`;
    - `press-reviews` — эффект репутации;
    - `rival-personalities` — поведение ИИ, ценовые войны (`IRivalPersonalitiesApi.ApplyPriceCut` / `RestorePriceCut`);
    - `rival-acquisitions` — канал `acquisition.price` (антимонопольная надбавка);
    - `gpu-product-line` — канал `gpu.demand` (криптобумы).
- **Решения владельца**: `.kiro/steering/owner-decisions.md` (внесены в спеку, см. «Решения по умолчанию»).
- **Идея**: `pt-modding/IDEAS.md`, раздел I37 «Historical event calendar: antitrust, export rules and price wars» (effort L, fun 4/5).
- **Источники идеи**:
  - <https://itch.io/devlog/1326987/020-released.amp>
  - <https://gulfnews.com/technology/intel-loses-court-challenge-against-144b-eu-fine-1.1346382>
  - <https://www.news4jax.com/business/2022/01/26/eu-court-annuls-intels-2009-billion-euro-antitrust-fine/>
  - <https://www.lawsociety.ie/gazette/top-stories/2023/september/eu-re-imposes-fine-on-intel-for-competition-breach/>
  - <https://globalcompetitionreview.com/article/general-court-slashes-re-imposed-penalty-against-intel>
  - <https://www.csis.org/analysis/where-chips-fall-us-export-controls-under-biden-administration-2022-2024>
  - <https://www.aiwiki.ai/wiki/nvidia_a800>

## Introduction

В игре одно скриптовое событие индустрии: выделение SolarFoundries из AMD 2009-03-02 (`CompanyEventHandler.CheckSolarFoundriesSpawnDate` → `CompanyEventHandler.TriggerSolarFoundriesSpinOff`). Рядом с ним — штраф к стоимости исследований Cyrix «из-за патентов» (`Modifiers.GetModifier`, тип 4, пока `LicenseHelper.X86LicenseHasBeenRevoked` ложно). Всё остальное время мир партии живёт без истории: нет кризисов, бумов, антимонопольных дел и ценовых войн.

Фича добавляет календарь из 42 событий 1971–2025 по реальным фактам:
- нефтяные кризисы, бумы ПК, ноутбуков, ИИ и криптовалют (спрос на GPU);
- землетрясения на фабриках, пандемия, дефицит чипов (категория `disaster` по умолчанию выключена);
- антимонопольные дела (FTC 1998, JFTC 2005, Еврокомиссия 2009 с цепочкой апелляций до 2024) и антимонопольная надбавка к цене поглощений;
- экспортный контроль 2022–2023, ценовые войны, патентные споры, FDIV и Meltdown.

Главное — **адаптация**. Событие ищет в мире партии того, кто играет историческую роль: лидера рынка, главного соперника, крупнейшего литейщика, «японскую» компанию-аналог. Если лидер — игрок, штраф Еврокомиссии получает игрок. Если роль некому играть, событие тихо отменяется или превращается в новость.

Версия 0.2 вносит решения владельца и замечания кросс-ревью: реальные названия компаний (Intel, AMD, Toshiba…); категория `disaster` выключена по умолчанию; криптобумы 2017 и 2021 годов с эффектом на спрос GPU; антимонопольные ограничения для поглощений; сегмент CPU — `ModSegments`; порог кредита — `ModCredit`; выбор ИИ — `EventAi.AddChooser`; цены ИИ в ценовых войнах — через `IRivalPersonalitiesApi`; письма — `ModMail` с тегами и приоритетами; версию секции сейва ведёт ядро.

### Feature Summary

Календарь 42 исторических событий индустрии с ролями, условиями и запасными вариантами. Каждое событие несёт справку «как было на самом деле» и источник, а их эффекты и выборы работают через `event-system`.

### Business Value

- **Ценность для игрока.**
  - Партия приобретает ритм эпох: кризисы, бумы и регуляторы.
  - Доминирование получает цену: антимонопольные дела бьют по лидеру, кем бы он ни был, а поглощения, концентрирующие рынок, дорожают.
  - Решения с отложенными последствиями: апелляция может обнулить штраф через 5 лет или увеличить его с процентами.
  - Образовательный слой: короткие справки с источниками о реальной истории индустрии.
- **Доказательства спроса.**
  - В `IDEAS.md` идея помечена «demand: unknown, fun: 4/5». Владелец выбрал группу «самое интересное по fun» целиком (`steering/roadmap.md`).
  - Девлог 0.2.0 показывает, что автор игры сам начал добавлять исторические события (спин-офф AMD) и патентные штрафы.
  - Запрос на события с выбором подтверждён источниками идеи I13.
- **Ожидаемый результат.** За партию 1975–2030 игрок видит 27–35 событий при настройках по умолчанию (30–38 при включённой категории `disaster`). 7–13 из них требуют решения игрока (8–14 при включённой `disaster`); остальные — новости с эффектами.

### Scope

**Входит:**
- контент-календарь: 42 записи с окнами дат, ролями, условиями, эффектами, справкой и источниками (из них 2 — криптобумы с эффектом на `gpu.demand`);
- планировщик: срабатывание, повторные проверки, запасные варианты, интервалы между крупными событиями, очередь решений игрока;
- распознавание ролей по состоянию партии;
- поздний старт: пропуск событий до старта, продолжение длительных эффектов;
- пак событий `event-system` (определения с текстами, справкой в `cause`, эффектами и выборами);
- новые типы эффектов: `AntitrustCase`, `AntitrustSettlement`, `ExportControl`, `PriceWar`, `PatentDispute`, `AttributeRace`, `ForceDesignFlaw` и служебный `HistFlag`;
- провайдеры стандартных каналов: `market.rating.cpu` (экспорт, гонки) и `acquisition.price` (антимонопольная надбавка);
- решения ИИ-целей;
- письма со справкой, окно «Хроника отрасли»;
- новости о встроенных событиях игры;
- настройки, секция `ModSave`, строки ru/en.

**Не входит:**
- движок событий, окно выбора, журнал, стандартные эффекты (это `event-system`);
- случайные шоки (это `market-shocks`); здесь только датированные исторические; общий антидемпинг — у `market-shocks`, реальные соглашения (США–Япония 1986) — здесь;
- механика брака и её окно (это `design-flaw-recalls`; используется API `FlawRegistry`);
- сами ценовые войны как механика цен ИИ (владеет `rival-personalities`: `ApplyPriceCut` / `RestorePriceCut`); здесь — календарь, выбор и фолбэк без неё;
- регионы рынка: экспортный контроль моделируется долей недоступных покупателей сегмента (решение владельца: без регионов);
- запрет сделок поглощения (антимонопольное ограничение выражено надбавкой к цене канала `acquisition.price`, без отказа в сделке);
- изменение встроенного события SolarFoundries и штрафа Cyrix;
- вымышленные события 2026–2030;
- игровой эффект репутации (это `press-reviews`).

## Requirements

### FR-1: Календарь исторических событий

**User Story:** Как игрок, я хочу, чтобы в партии происходили узнаваемые события истории индустрии, чтобы эпохи ощущались по-разному.

**Priority:** Must

#### Acceptance Criteria

1. WHEN мод загружает контент THEN календарь `Content/historical-events/calendar.json` SHALL содержать от 25 до 42 записей с якорными датами в диапазоне 1971-01-01 … 2025-12-31.
2. WHEN запись загружается THEN она SHALL содержать:
   - `id` с префиксом `hist.` (проверка `ModIds.IsEventIdAllowed`), категорию, якорную дату, окно `[from, to]`, роли, условия (в нотации `ModConditions`: `type` и параметры на верхнем уровне), эффекты и запасной вариант;
   - блок `history` с ключом заголовка, ключом справки «как было на самом деле» и минимум одним URL источника.
3. IF у записи нет источника или справки THEN валидатор SHALL отклонить запись с ошибкой в `ModLog`, а остальной календарь SHALL работать.
4. WHERE запись ссылается на событие выбора игрока (`playerEventId`) это событие SHALL существовать в `Content/historical-events/events.json`.
5. Компании в записях и событиях SHALL указываться по `UniqueID` или ключу роли, а отображаемое имя SHALL браться из игры (`ICompany.get_Name`); названия компаний в логике не вшиваются.

#### Scenarios

- **AC-1.1** Given встроенный контент, When фича стартует, Then загружено 42 записи, ошибок валидации 0.
- **AC-1.2** Given пользовательский файл в `<папка игры>/PTMod/content/historical-events/calendar.json` с записью без `history.sources`, When загрузка, Then эта запись пропущена, в логе `[PTMod] [historical-events] ERROR entry hist.x missing sources`.

**Complexity:** Medium
**Dependencies:** `mod-framework` (`ModContent`, `ModIds`), FR-17.
**Assumptions:** справки ссылаются на реальные компании (Intel, AMD, Toshiba); тексты внутри игры используют игровые названия, которые русификатор делает реальными (решение владельца).

### FR-2: Планировщик событий

**User Story:** Как игрок, я хочу, чтобы события приходили примерно в исторические даты, но не пачками, чтобы успевать на них реагировать.

**Priority:** Must

#### Acceptance Criteria

1. WHEN наступает игровой день (`ctx.OnTick(TickPhase.DayPassed, …)`) и дата ≥ якорь + сдвиг записи THEN планировщик SHALL проверить роли и условия записи и при успехе один раз за партию вызвать события пака `event-system` из стадии записи (`EventBus.Fire`) для каждой компании роли.
2. WHEN партия начинается THEN планировщик SHALL один раз выбрать сдвиг каждой записи из `[−jitterDays, +jitterDays]` через `ModRandom` и сохранить его.
3. IF роли не распознаны или условия не выполнены в день якоря THEN планировщик SHALL повторять проверку 1-го числа каждого месяца до конца окна `to`.
4. IF окно истекло без срабатывания THEN планировщик SHALL выполнить запасной вариант: `news` — письмо-новость без эффектов, `skip` — ничего; запись SHALL получить статус `Expired`.
5. IF запись помечена `major` и с последнего `major`-события прошло меньше `majorSpacingDays` (45) дней THEN планировщик SHALL отложить её на минимально нужный срок, но не дальше конца окна.
6. WHILE у игрока открыто окно решения исторического события новые события с решением игрока SHALL ждать в очереди. Новости без решения SHALL приходить сразу.
7. IF две записи срабатывают в один день THEN порядок SHALL определяться якорной датой, затем `id`.
8. WHILE идёт пакетный прогон (`ModTicks.IsBatchRun`, кнопка «Следующий месяц») тики дней SHALL выполняться как обычно, а показ писем и окон SHALL откладывать ядро и движок до `ModTicks.BatchEnded`. Фича SHALL NOT патчить `DateController.NextMonth`.

#### Scenarios

- **AC-2.1** Given запись `hist.1995.win95` с якорем 1995-08-24 и сдвигом +5, When наступает 1995-08-29, Then эффекты применены, статус `Fired`.
- **AC-2.2** Given `hist.2009.eufine` и лидер с долей 0.48 < 0.55, When 2009-05-13, Then событие не срабатывает; повторные проверки 1-го числа до 2010-12-31; затем письмо «Расследование закрыто без штрафа».
- **AC-2.3** Given `hist.1987.blackmonday` (`major`) и `hist.1987.arbitration` 30 днями позже, When наступает дата второго, Then он откладывается на 15 дней.

**Complexity:** Medium
**Dependencies:** FR-1, FR-3, FR-18; `mod-framework` (`ModTicks`, `ModRandom`).
**Assumptions:** нет.

### FR-3: Роли и адаптация к истории партии

**User Story:** Как игрок, я хочу, чтобы исторические события находили «своих» участников в моей партии, включая меня, чтобы история была альтернативной, а не предписанной.

**Priority:** Must

#### Acceptance Criteria

1. WHEN проверяется запись THEN мод SHALL распознать её роли одним из способов:
   - `leader` / `challenger` — первая и вторая компании по доле продаж за N полных месяцев в заданных сегментах (`Segment` или `all`), по `CpuBase.MarketData`;
   - `named` — AI-компания с заданным `AICompany.UniqueID`, не банкрот;
   - `foundryLeader` — литейщик с наибольшей `FactoryBase.ProductionCapacity` среди `CompanyDataProvider.GetFoundryCompanies`;
   - `bestPerfPerWatt` — владелец CPU на рынке с максимальным `Ips / PowerConsumption` среди выпущенных за 36 месяцев;
   - `newestEntrant` — компания с самой поздней `FoundationDate` и хотя бы одним CPU;
   - `player`;
   - группы `allCpuCompanies`, `allManufacturers`, `allFabless`.
2. IF у роли задан `minShare` и доля кандидата ниже THEN роль SHALL считаться нераспознанной.
3. IF роль не распознана и у неё есть цепочка `fallback` THEN мод SHALL пробовать варианты цепочки по порядку.
4. IF две роли записи указывают на одну компанию (например, истец и ответчик) THEN мод SHALL взять для второй роли следующего кандидата, а если его нет — считать роль нераспознанной.
5. WHERE настройка «историческая точность» = `strict` роли `named` SHALL NOT использовать `fallback`: если компании-аналога нет, срабатывает запасной вариант записи. По умолчанию действует режим `adaptive` (решение владельца).
6. WHEN роль распознана THEN письмо события SHALL называть компанию по её игровому имени (`ICompany.get_Name`).
7. WHEN фича определяет основной сегмент CPU (ценовые войны, `hist.HasCpuSince`, гонки) THEN она SHALL использовать единые правила ядра: для CPU ИИ — `CpuBase.AIStrategy.TargetMarket`; для CPU игрока — `ModSegments.FromSales` по продажам последнего полного месяца (`SoldByHighEnd` … `SoldByIndustries`), а при нулевых продажах (`Segment.Any`) — `ModSegments.Classify(cpu)`. Собственных классификаторов фича SHALL NOT вводить.

#### Scenarios

- **AC-3.1** Given игрок с долей 0.62 за 36 месяцев, Intel 0.20, When проверяется `hist.2009.eufine` (`leader`, `minShare` 0.55), Then цель — игрок, ему показывается выбор «платить, резерв, апелляция».
- **AC-3.2** Given Toshiba (`UniqueID` 6) обанкротилась, режим `adaptive`, When `hist.1987.koshiba` (роль `named:6`, `fallback` отсутствует), Then срабатывает запасной вариант `news`.
- **AC-3.3** Given TSMC не литейщик, а крупнейший литейщик — игрок, When `hist.1999.jiji` (`named:9` → `foundryLeader`, категория `disaster` включена), Then удар по выпуску получает игрок.
- **AC-3.4** Given CPU игрока без продаж за последний полный месяц и ценой выше порога HighEnd, When определяется основной сегмент, Then результат `Segment.HighEnd` (по `ModSegments.Classify`).

**Complexity:** High
**Dependencies:** FR-1; `mod-framework` (`ModSegments`, `ModCompanies`).
**Assumptions:** `CpuBase.MarketData` хранит помесячные списки, выровненные по `DateController.MonthsSinceStart` (`MarketDataHandler.HandleMissingIndexes`).

### FR-4: Поздний старт и перемотка

**User Story:** Как игрок, начинающий в 1990 году, я хочу видеть только события после старта, но чувствовать уже идущие процессы, чтобы не получить «прошлое» разом.

**Priority:** Must

#### Acceptance Criteria

1. WHILE идёт перемотка новой игры (`DateController.IsFastFowardSimulating`) планировщик SHALL NOT запускать события (политика владельца: перемотка нейтральна).
2. WHEN перемотка закончилась (`TickPhase.FastForwardEnded`; `IsFastFowardSimulating` к этому моменту уже сброшен) THEN мод SHALL один раз (флаг `initialized`) бросить сдвиги и перевести все записи с якорем раньше даты старта в статус `SkippedBeforeStart`.
3. IF у такой записи `catchUp = true`, её категория включена и на дату старта эффект какой-либо её стадии ещё бы действовал (якорь + `offsetDays` + длительность > старт) THEN мод SHALL перевести стандартные эффекты этой стадии (`MarketDemand`, `ModifierTimed`, `FactoryOutput`) в записи `ModModifiers.Add` с оставшейся длительностью. Выборы и нестандартные эффекты (дела, правила, войны) при догоне SHALL NOT применяться.
4. WHEN применены догоняющие эффекты THEN мод SHALL отправить одно письмо «Обстановка в отрасли» (`ModMail.Send`, приоритет `Normal`, тег `hist:catchup`; ядро доставляет его после `GameReady`) со списком действующих эффектов (Could: краткая «Хроника до вашего старта» — 3–5 строк о пропущенных событиях).
5. IF `TickPhase.FastForwardEnded` не пришёл (запуск без перемотки) THEN инициализация SHALL выполниться на `ModTicks.GameReady(NewGame)`; флаг `initialized` защищает от повтора.

#### Scenarios

- **AC-4.1** Given старт 2021-01-01 (сдвиги не учитываются), категория `disaster` включена, When перемотка закончилась, Then:
  - `hist.2020.shortage` (якорь 2020-12-01, 730 дней, `catchUp`) применён с остатком 699 дней;
  - у `hist.2020.covid` первая стадия (спад, 90 дней) не применена: она закончилась;
  - вторая стадия `hist.2020.covid` (рост спроса, с 2020-06-09 на 540 дней) применена с остатком 334 дня.
- **AC-4.2** Given старт 2021-01-01 и настройки по умолчанию (`disaster` выключена), When перемотка закончилась, Then применён только `hist.2020.shortage` (699 дней); `hist.2020.covid` получил статус `Disabled`, письмо «Обстановка в отрасли» не упоминает пандемию.
- **AC-4.3** Given старт 2022-01-01 и включённый `gpu-product-line`, When перемотка закончилась, Then `hist.2021.cryptoboom` (якорь 2021-04-01, 365 дней) применён с остатком 90 дней на канал `gpu.demand` (275 дней прошло до старта).

**Complexity:** Medium
**Dependencies:** FR-2; `mod-framework` (`ModTicks`, `TickPhase.FastForwardEnded`, `ModMail`).
**Assumptions:** стартовый год игрока — `DateController.StartYear`.

### FR-5: Антимонопольные дела

**User Story:** Как лидер рынка, я хочу столкнуться с регуляторами и выбирать между оплатой, резервом и апелляцией, чтобы доминирование имело цену и требовало стратегии.

**Priority:** Must

#### Acceptance Criteria

1. WHEN срабатывает дело в режиме `fine` THEN мод SHALL рассчитать штраф `F = finePct × выручка цели за 12 полных месяцев` (`CompanyFinance.Reports`) и предложить цели выбор:
   - «Оплатить» — `SpendMoney(F, ExpenseOrigin.Fines)` сразу;
   - «Резерв» — оплата сейчас, возврат при отмене или снижении;
   - «Апелляция» — без оплаты сейчас, ежемесячная плата за банковскую гарантию.
2. WHEN выбрана апелляция или резерв THEN мод SHALL через `appeal.months` месяцев определить исход: отмена (`pAnnul`), снижение до `reduceTo` (`pReduce`), оставление в силе (остаток). При оставлении в силе к сумме SHALL добавляться проценты `interestPerYear` за прошедшие годы.
3. IF к моменту решения суда доля цели упала ниже `dominanceLostShare` (0.40) THEN `pAnnul` SHALL увеличиться на 0.10 за счёт `pUphold`.
4. IF штраф отменён и у дела включено повторное наложение THEN мод SHALL через `reimpose.delayMonths` месяцев с вероятностью `reimpose.p` наложить новый штраф `F × reimpose.shareOfOriginal` с выбором «оплатить» или «апелляция» (одна ступень, `pReduce` → `reduceTo`).
5. WHEN дело в режиме `consent` THEN выбор SHALL быть: «Мировое соглашение» (поведенческое ограничение без штрафа) или «Судиться» (судебные расходы, затем 50 % — дело закрыто, 50 % — соглашение плюс штраф).
6. WHEN дело в режиме `warning` THEN мод SHALL применить поведенческое ограничение без выбора.
7. WHEN выносится решение о штрафе THEN цель SHALL получить `ModReputation.Add(company, −5, "hist.rep.antitrust.fine")`; при немедленной оплате — дополнительно +2 (`hist.rep.antitrust.paid`). Причины репутации — ключи L10n фичи.
8. WHILE дело активно окно «Хроника отрасли» SHALL показывать стадию, сумму, ближайшую дату и уже уплаченное.
9. Правила дела SHALL быть одинаковы для игрока и ИИ-цели (политика владельца; ИИ-цели на низкой сложности не получают послаблений).

#### Scenarios

- **AC-5.1** Given игрок-лидер с выручкой 25 млрд за 12 месяцев, When `hist.2009.eufine` с `finePct` 0.04, Then F = 1 млрд; кнопки показывают 1 млрд, «резерв 1 млрд», «апелляция: 10 млн в год за гарантию».
- **AC-5.2** Given апелляция, через 60 месяцев исход «оставлено в силе», When решение, Then списано 1 млрд · (1 + 0.04 · 5) = 1.2 млрд.
- **AC-5.3** Given резерв и исход «отменён», When решение, Then игроку возвращён 1 млрд через `EarnMoney(..., RevenueOrigin.Interest)` (решение владельца: статья `Interest`); через 18 месяцев с вероятностью 0.5 повторный штраф 355 млн.

**Complexity:** High
**Dependencies:** FR-3, FR-13, FR-14; `event-system`; `mod-framework` (`ModReputation`).
**Assumptions:** выручка за 12 месяцев — сумма `TotalRevenues` 12 последних закрытых отчётов `CompanyFinance.Report`.

### FR-6: Иск пострадавшего конкурента

**User Story:** Как второй игрок рынка, я хочу взыскать компенсацию с оштрафованного лидера, чтобы антимонопольное дело давало мне выгоду.

**Priority:** Should

#### Acceptance Criteria

1. IF активно дело `hist.2009.eufine`, его цель — ИИ, а доля игрока за 36 месяцев ≥ 0.10 WHEN срабатывает `hist.2009.settlement` THEN мод SHALL предложить игроку: «Подать иск» (судебные расходы `legalCostPct` выручки игрока в месяц, 12 месяцев) или «Не подавать».
2. WHEN иск подан и прошло 12 месяцев THEN с вероятностью `pWin` (0.7) мод SHALL перевести игроку `S = settlementPct × выручка цели за 12 месяцев`:
   - цель: `SpendMoney(S, ExpenseOrigin.Fines)`;
   - игрок: `EarnMoney(S, RevenueOrigin.Royalties)`.
3. IF игрок не проходит условие THEN мод SHALL провести ту же сделку между целью и ИИ-соперником (`challenger`) без участия игрока и прислать новость.

#### Scenarios

- **AC-6.1** Given Intel оштрафована, выручка Intel 30 млрд, иск выигран, When 12 месяцев прошли, Then игрок получил 0.03 · 30 млрд = 900 млн.

**Complexity:** Medium
**Dependencies:** FR-5.
**Assumptions:** нет.

### FR-7: Экспортный контроль

**User Story:** Как производитель самых быстрых CPU, я хочу столкнуться с ограничениями экспорта и решать, урезать ли модели, лоббировать или просить лицензию, чтобы выпуск «урезанных» SKU имел смысл.

**Priority:** Must

#### Acceptance Criteria

1. WHEN срабатывает правило экспортного контроля THEN мод SHALL вычислить порог производительности `T0`: перцентиль `percentile` по `Ips` среди CPU на рынке (не снятых, `SellOnMarket`).
2. WHILE правило действует мод SHALL каждый месяц пересчитывать порог: `T = T0 × (Market.Instance.AverageIps / AverageIps0)^indexing`.
3. WHILE правило действует каждый CPU с `Ips ≥ T` в сегментах правила SHALL получать множитель рейтинга `1 − blockedShare × lobbyFactor(компания)` через провайдер фичи на канале `market.rating.cpu` (`ModModifiers.RegisterReasonedProvider`, причина `hist.mod.export` с порогом в аргументах), кроме компаний с действующей лицензией. Каждое изменение состояния внутри дня (введено или обновлено правило, выдана лицензия, успешное лоббирование, пересчёт порога) SHALL сопровождаться `ModModifiers.Invalidate("market.rating.cpu")`.
4. WHEN правило вводится или ужесточается и у игрока есть CPU выше порога THEN мод SHALL предложить выбор:
   - «Соблюдать» — письмо с числом порога, подсказка выпустить урезанную модель ниже порога;
   - «Запросить лицензию» — стоимость `licenseCostPct` выручки, успех `licenseSuccess` даёт освобождение на `licenseMonths`;
   - «Лоббировать» — стоимость `lobbyCostPct` выручки, успех `lobbySuccess` уменьшает `blockedShare` для компании в `lobbyFactor` раз на `lobbyMonths`.
5. WHEN срабатывает вторая ступень (2023-10-17) THEN мод SHALL обновить то же правило: новый перцентиль, новая доля и новый выбор игроку.
6. IF у игрока нет CPU выше порога THEN мод SHALL отправить новость с порогом без выбора.
7. Вклад провайдера экспорта в `market.rating.cpu` SHALL лежать в [0,80; 1,00] (`blockedShare` 0,15 на первой ступени и 0,20 на второй); рейтинг он не повышает. Общий предел [0,5; 2,0] на CPU применяет ядро.

#### Scenarios

- **AC-7.1** Given P90 по `Ips` = 2.0e9, When правило 2022-10-07 введено, Then CPU игрока с `Ips` 2.4e9 в сегменте HighEnd получает `Overall × 0.85`, а его урезанный вариант с `Ips` 1.8e9 — нет.
- **AC-7.2** Given лоббирование успешно (`lobbyFactor` 0.5), When расчёт рейтинга, Then множитель игрока = 1 − 0.15 · 0.5 = 0.925 на 24 месяца.

**Complexity:** High
**Dependencies:** FR-3, FR-13; `mod-framework` (канал `market.rating.cpu` с областью CPU и сегмента, `ModModifiers.RegisterReasonedProvider`, `Invalidate`).
**Assumptions:** регионов в игре нет. «Закрытый рынок» = доля покупателей сегмента, недоступная быстрым CPU; они уходят к CPU ниже порога через обычный расчёт рейтинга.

### FR-8: Ценовые войны

**User Story:** Как игрок, я хочу переживать исторические ценовые войны и выбирать, втягиваться ли в них, чтобы цены рынка менялись не только от моих действий.

**Priority:** Must

#### Acceptance Criteria

1. WHERE фича `rival-personalities` активна (`ModFeatures.IsActive("rival-personalities")`, то же, что `RivalPersonalities.Api.IsEnabled`) ценовая война SHALL снижать цены ИИ вызовом `RivalPersonalities.Api.ApplyPriceCut("hist:<warId>", rival, segment, depth, floorMargin)` (агрессор — `depth = cut`, остальные ИИ с CPU в сегментах войны — `followCut`) и возвращать вызовом `RivalPersonalities.Api.RestorePriceCut("hist:<warId>")`. При активной `rival-personalities` фича SHALL NOT менять цены ИИ напрямую (ценовыми войнами владеет `rival-personalities`).
2. WHERE `rival-personalities` неактивна (фолбэк допустим — решение владельца) мод SHALL для каждого не снятого CPU, выставленного на рынок, у ИИ в сегментах войны (`CpuBase.AIStrategy.TargetMarket`):
   - запомнить исходную `TargetPrice`;
   - установить `TargetPrice` и `Price` = `max(RealUnitCost × 1.05, исходное × (1 − cut))` (у последователей — `followCut`).
3. WHEN война заканчивается (`durationDays`) THEN мод SHALL вернуть исходные `TargetPrice` (или вызвать `RestorePriceCut`). Цены `Price` дальше SHALL меняться обычной логикой ИИ (`AIBehaviourMarket.ChangePrices`).
4. IF игрок — агрессор THEN мод SHALL предложить выбор: «Начать войну» (снижение цен всех рыночных CPU игрока, основной сегмент которых входит в войну, на `cut`) или «Не начинать» (агрессором станет следующий кандидат роли).
5. IF игрок не агрессор, но продаёт CPU в сегментах войны THEN мод SHALL предложить:
   - «Снизить цены» на `followCut`;
   - «Держать цены»;
   - «Маркетинг вместо скидок» — `marketingCostPct` выручки, `popularity.gain` × 1.10 на 180 дней.
6. WHEN определяется основной сегмент CPU игрока THEN мод SHALL использовать правило FR-3 п. 7 (`ModSegments.FromSales` по продажам последнего полного месяца, затем `ModSegments.Classify`).
7. Вклад маркетинга в `popularity.gain` SHALL быть ×1,10 на CPU компании, общий предел [0,5; 2,0] применяет ядро.

#### Scenarios

- **AC-8.1** Given `rival-personalities` неактивна, AMD-агрессор, CPU AMD HighEnd с ценой 500 и `RealUnitCost` 120, cut 0.20, When война, Then цена и цель = 400; через 180 дней цель = 500, а цена растёт на 5 в день только при дефиците (`ChangePrices`).
- **AC-8.2** Given `rival-personalities` активна, When война, Then вызван `ApplyPriceCut("hist:<warId>", AMD, сегмент, 0.20, 0.05)`; по окончании — `RestorePriceCut("hist:<warId>")`; прямых изменений `Price` фича не делает.

**Complexity:** Medium
**Dependencies:** FR-3, FR-13; `rival-personalities` (`IRivalPersonalitiesApi.ApplyPriceCut` / `RestorePriceCut`, мягкая).
**Assumptions:** `rival-personalities` предоставляет эти методы в публичном `IRivalPersonalitiesApi` (null-object при выключенной фиче); прямой фолбэк допустим только при `Api.IsEnabled == false`.

### FR-9: Патентные споры

**User Story:** Как претендент на рынок, я хочу проходить через патентные войны лидера (арбитраж, лицензия или своя разработка), чтобы обход чужой архитектуры имел цену.

**Priority:** Should

#### Acceptance Criteria

1. WHEN срабатывает спор в режиме `licenseWithdrawal` THEN ответчик SHALL выбрать:
   - «Арбитраж» — судебные расходы `legalCostPct` в месяц на `months` месяцев; выигрыш с `pWin` → истец платит `settlementPct` своей выручки ответчику;
   - «Лицензия» — платёж `licenseFeePct` выручки в месяц на 36 месяцев, `ExpenseOrigin.Royalties` у ответчика и `RevenueOrigin.Royalties` у истца;
   - «Своя разработка» — `research.cost` × `researchCostMultiplier` на 36 месяцев.
2. WHEN срабатывает спор в режиме `infringement` THEN ответчик SHALL выбрать: «Судиться» (`pWin`), «Мировое» (разово `settlementPct` своей выручки истцу) или «Переделать» (`research.cost` × 1.2 на 24 месяца).
3. IF ответчик — Cyrix и `CompanyHelper.IsUnluckyCompany` истинно (уже действует игровой штраф 6.0 к стоимости исследований) THEN мод SHALL NOT добавлять множитель `research.cost`; спор действует только через платежи.
4. IF игрок — истец THEN мод SHALL предложить «Подать в суд» или «Не подавать»; ответчик-ИИ решает по политике FR-13.

#### Scenarios

- **AC-9.1** Given игрок — ответчик, выбрана лицензия, выручка игрока 400 млн в месяц, `licenseFeePct` 0.0125, When месяц, Then игрок платит 5 млн, истец получает 5 млн.

**Complexity:** Medium
**Dependencies:** FR-3, FR-13.
**Assumptions:** нет.

### FR-10: Макроэкономические и отраслевые шоки и бумы

**User Story:** Как игрок, я хочу, чтобы кризисы и бумы меняли спрос, кредит и себестоимость, чтобы планировать мощности по эпохам.

**Priority:** Must

#### Acceptance Criteria

1. WHEN срабатывает событие-шок или бум THEN его эффекты SHALL быть стандартными эффектами `event-system` в определении пака:
   - `MarketDemand` — сегмент, множитель, длительность;
   - `FactoryOutput`;
   - `ModifierTimed` на стандартные каналы реестра ядра: `production.unitCost`, `bank.creditLimit`, `bank.interest`, `contract.reward`, `contract.signChance`, `popularity.gain`, `taxes`, `market.rating.cpu`, `research.cost`, `gpu.demand` (криптобумы).
2. WHEN эффект адресован роли THEN мод SHALL вызвать событие с целью — каждой компанией роли (`EventFireContext.CompanyKey`). Глобальные эффекты (`MarketDemand`, `gpu.demand`) действуют на всех.
3. WHEN эффект применяется THEN его величина SHALL масштабироваться настройкой интенсивности через `PackOptions.Severity` пака и флаг `scalable`: `m' = 1 + (m − 1) × intensity`, `intensity` ∈ [0.5, 1.5]. Интенсивность одинакова для игрока и ИИ-целей.
4. WHERE событие состоит из стадий (например, пандемия: спад, затем рост) запись SHALL описывать их списком `stages` со смещением `offsetDays` от срабатывания. Планировщик SHALL запускать каждую стадию в её день, а состояние стадий SHALL сохраняться (FR-18).
5. WHERE событие вводит эффект на канал `gpu.demand` запись SHALL иметь условие `featureActive` для `gpu-product-line`; при неактивной фиче запись ждёт до конца окна и уходит в запасной вариант `skip` (письма о неработающем эффекте нет).
6. WHERE записи 7, 8, 12 (память DRAM) задают стоимость производства THEN они SHALL использовать канал `production.unitCost` (только фактические траты), а не `production.memoryCost` (канал `market-shocks`).

#### Scenarios

- **AC-10.1** Given интенсивность 0.5, When `hist.2008.lehman` (спрос × 0.80), Then применён множитель 0.90.
- **AC-10.2** Given `gpu-product-line` активна и интенсивность 1.0, When срабатывает `hist.2017.cryptoboom`, Then добавлена запись `gpu.demand` × 1.35 на 365 дней и игрок получил письмо со справкой.
- **AC-10.3** Given `gpu-product-line` выключена, When наступает окно `hist.2017.cryptoboom`, Then записи и письма нет; запись уходит в `Expired` по запасному варианту `skip`.

**Complexity:** Low
**Dependencies:** `event-system` (стандартные эффекты); FR-3.
**Assumptions:** `market-shocks` не дублирует датированные шоки; её события той же природы получают условие `{ "not": { "type": "es.activeInCategory", "category": "history", "scope": "any" } }` (координация; предикат `es.activeInCategory` получает параметр `scope`).

### FR-11: Гонки характеристик

**User Story:** Как игрок, я хочу, чтобы в эпоху «гонки гигагерц» и «гонки ядер» рынок переоценивал соответствующую характеристику, чтобы дизайн подстраивался под моду эпохи.

**Priority:** Should

#### Acceptance Criteria

1. WHILE действует гонка характеристики (`frequency` или `coreCount`) CPU из топ-N рынка по этой характеристике (пересчёт раз в месяц) SHALL получать множитель рейтинга `bonus` в указанных сегментах через провайдер фичи на канале `market.rating.cpu` (причина `hist.mod.race.frequency` / `hist.mod.race.cores`). Пересчёт топа SHALL сопровождаться `ModModifiers.Invalidate("market.rating.cpu")`.
2. WHEN гонка заканчивается THEN мод SHALL снять вклад из провайдера.
3. Вклад гонки в `market.rating.cpu` SHALL лежать в [1,00; 1,10]; общий предел [0,5; 2,0] применяет ядро.

#### Scenarios

- **AC-11.1** Given гонка частот 2000–2004, топ-3, бонус 1.10, When у игрока самый высокочастотный CPU, Then его `Overall` × 1.10 в HighEnd, MidRange и LowEnd.

**Complexity:** Low
**Dependencies:** `mod-framework` (канал `market.rating.cpu`, `ModModifiers.RegisterReasonedProvider`).
**Assumptions:** нет.

### FR-12: Связь с механикой брака

**User Story:** Как игрок, я хочу, чтобы FDIV 1994 года и Meltdown 2018 года стали настоящими инцидентами брака у участников моей партии.

**Priority:** Should

#### Acceptance Criteria

1. WHEN срабатывает эффект `ForceDesignFlaw` и фича `design-flaw-recalls` включена THEN мод SHALL выбрать CPU по правилу `pick` и вызвать `FlawRegistry.ForceFlaw(cpuSaveId, typeId, severity, discovery)`. Правила `pick`: самый продаваемый HighEnd за 24 месяца, или по одному CPU на компанию с `Ips ≥ Market.Instance.AverageIps`, выпущенному за 60 месяцев.
2. IF `design-flaw-recalls` выключена или подходящего CPU нет THEN мод SHALL применить `fallbackEffects` записи: репутация и временный `market.rating.cpu` цели.

#### Scenarios

- **AC-12.1** Given игрок — лидер HighEnd в 1994 году и `design-flaw-recalls` включена, When `hist.1994.fdiv`, Then у игрока открывается окно решения брака (`arith`, `Major`, внешнее обнаружение) со справкой о FDIV.

**Complexity:** Low
**Dependencies:** `design-flaw-recalls` (FR-13 той спеки).
**Assumptions:** нет.

### FR-13: Решения ИИ-целей

**User Story:** Как игрок, я хочу, чтобы соперники проходили те же исторические испытания и принимали правдоподобные решения, чтобы мир был честным.

**Priority:** Must

#### Acceptance Criteria

1. WHEN цель события с выбором — ИИ THEN мод SHALL вызвать событие для этой компании (`CompanyKey = "ai:<UniqueID>"`). Выбор делает механизм движка (`aiWeight` вариантов).
2. WHEN движок выбирает за ИИ в событии `hist.decide.*` THEN зарегистрированный фичей `IEventAiChooser` (`EventAi.AddChooser(chooser, priority: 10)`) SHALL обнулять вес вариантов с оплатой больше денежного запаса (`MoneyAmount − ModCredit.ThresholdOf(company)`, то есть деньги плюс лимит долга, который применяет игра). Стоимость нестандартных эффектов берётся из их предвычисленных значений.
3. WHERE `rival-personalities` регистрирует свой выборщик с более высоким приоритетом (100) THEN движок SHALL опрашивать выборщики по убыванию приоритета, первый ненулевой ответ SHALL побеждать; при `null` решение остаётся за выборщиком фичи, затем за политикой движка. Для чужих событий (`eventId` не начинается с `hist.decide.`) выборщик фичи SHALL возвращать `null`.
4. IF все веса равны 0 THEN мод SHALL выбрать `defaultChoice` события.
5. WHEN ИИ выбрал THEN движок SHALL применить эффекты варианта к ИИ и прислать игроку новость (`notifyPlayer`).
6. Правила и эффекты для ИИ-целей SHALL совпадать с правилами для игрока; на низкой сложности они не ослабляются (политика владельца).

#### Scenarios

- **AC-13.1** Given Intel оштрафована на 2 млрд, запас Intel 1.5 млрд, When выбор, Then «Оплатить» и «Резерв» имеют вес 0, выбрана апелляция; игрок получил новость с выбором Intel.
- **AC-13.2** Given зарегистрированы выборщики `rival-personalities` (100) и `historical-events` (10), When ИИ решает `hist.decide.antitrust.fine` и первый вернул `null`, Then решение за выборщиком фичи.

**Complexity:** Medium
**Dependencies:** FR-5–FR-9; `event-system` (`EventAi.AddChooser`, `notifyPlayer`); `mod-framework` (`ModCredit`).
**Assumptions:** политика движка по умолчанию учитывает `aiWeight` и отдаёт решение зарегистрированным выборщикам в порядке убывания приоритета.

### FR-14: Письма со справкой и журнал

**User Story:** Как игрок, я хочу получать письмо о каждом событии с короткой справкой «как было на самом деле» и источником, чтобы понимать контекст.

**Priority:** Must

#### Acceptance Criteria

1. WHEN событие пака срабатывает для игрока или для ИИ с `notifyPlayer` THEN игрок SHALL получить письмо движка:
   - заголовок;
   - что произошло в партии (`text`, игровые имена из `Args`);
   - эффекты с числами и сроками (`Describe` движка);
   - справка «Как это было» (`cause`, 2–4 предложения);
   - домен источника.

   События с выбором игрока помечены `important: true` (письмо `Critical`, таймер от доставки — правила `event-system`), остальные — `Normal`.
2. WHEN событие требует решения игрока THEN окно `event-system` SHALL содержать ту же справку (`cause`).
3. WHEN событие срабатывает THEN `EventLog` SHALL получить запись с категорией `history` (средствами движка).
4. WHERE настройка `historyNotes` выключена события SHALL приходить без справки (фича подставляет пустой `cause` через свой ключ-переключатель); отдельных текстов без упоминания реальных событий нет.
5. WHEN запись уходит в запасной вариант `news` THEN мод SHALL вызвать событие `hist.fizzle` с заголовком записи.
6. Собственные письма фичи (письмо догона) SHALL нести тег `hist:<…>` и приоритет `Normal`.

#### Scenarios

- **AC-14.1** Given `hist.1995.win95`, When срабатывает, Then письмо «Выход Windows 95» содержит «MidRange +25 % спроса на 24 месяца» и справку с доменом `wikipedia.org`.

**Complexity:** Low
**Dependencies:** `event-system` (письма экземпляров, `cause`, `EventLog`); `mod-framework` (`ModMail` — письмо догона).
**Assumptions:** нет.

### FR-15: Окно «Хроника отрасли»

**User Story:** Как игрок, я хочу видеть прошедшие события, действующие правила и дела, чтобы планировать.

**Priority:** Should

#### Acceptance Criteria

1. WHEN игрок открывает хаб «Моды» → «Хроника отрасли» THEN мод SHALL показать страницу хаба (`ModUI`) с вкладками:
   - «Прошедшие» — дата, событие, кто был в роли, итог;
   - «Действует сейчас» — эффекты с остатком дней, экспортные правила с порогом, ценовые войны;
   - «Дела и споры» — стадия, суммы, ближайшая дата.
2. WHERE настройка `spoilers` выключена (по умолчанию) окно SHALL NOT показывать будущие события.
3. WHERE настройка `spoilers` включена окно SHALL показывать год ближайших трёх будущих событий без деталей.

#### Scenarios

- **AC-15.1** Given активная апелляция, When открыта вкладка «Дела и споры», Then строка: «Еврокомиссия против вас: 1 млрд, апелляция, решение ~ май 2014, уплачено 0, гарантия 10 млн в год».

**Complexity:** Medium
**Dependencies:** `mod-framework` (`ModUI`).
**Assumptions:** нет.

### FR-16: Новости о встроенных событиях игры

**User Story:** Как игрок, я хочу получать справку и к событиям самой игры (выделение SolarFoundries, отзыв x86-лицензии), чтобы календарь выглядел цельным.

**Priority:** Could

#### Acceptance Criteria

1. WHEN выполняется `CompanyEventHandler.TriggerSolarFoundriesSpinOff` (постфикс, не во время загрузки и перемотки) THEN мод SHALL отправить письмо-справку о выделении GlobalFoundries из AMD (2009) без игровых эффектов.
2. WHEN срабатывает `LicenseHelper.OnX86LicenseRevoked` вне загрузки THEN мод SHALL отправить письмо-справку о кросс-лицензировании x86.

#### Scenarios

- **AC-16.1** Given 2009-03-02 и AMD жива, When игра выделяет SolarFoundries, Then приходит письмо «Выделение фабрик AMD» со справкой.

**Complexity:** Low
**Dependencies:** FR-14.
**Assumptions:** `LicenseHelper.OnX86LicenseRevoked` вызывается и на `SaveHandler.OnLastLoadedTick` при загрузке, поэтому нужна защита (`ModTicks.IsLoading`).

### FR-17: Настройки

**User Story:** Как игрок, я хочу настроить историчность, интенсивность и категории событий, чтобы подогнать фичу под свой стиль.

**Priority:** Must

#### Acceptance Criteria

1. WHEN игрок открывает настройки фичи в `ModSettings` THEN доступны:
   - `enabled` (true);
   - `mode`: `adaptive` (по умолчанию) | `strict`;
   - `intensity` 0.5–1.5 (1.0);
   - переключатели категорий: `macro`, `boom`, `shock`, `disaster`, `antitrust`, `export`, `priceWar`, `patent`, `flaw`, `race`, `brand`, `oem`. Все включены по умолчанию, кроме `disaster` (катастрофы и пандемия — отдельная категория, по умолчанию выключена);
   - `historyNotes` (true), `spoilers` (false).
2. WHEN категория выключена THEN её записи SHALL получать статус `Disabled` при наступлении даты, без эффектов и писем, а их догоняющие эффекты (FR-4) SHALL NOT применяться. Уже действующие эффекты SHALL доживать срок.
3. WHEN фича выключается посреди партии THEN мод SHALL перевести провайдеры каналов (экспорт, гонки, надбавка поглощений) в нейтральное значение 1.0, остановить дела и споры (без новых платежей) и вернуть цены ИИ в активных ценовых войнах (`RivalPersonalities.Api.RestorePriceCut` или исходные `TargetPrice`). Записи `ModModifiers` фичи (догоняющие эффекты) SHALL храниться и не применяться; `RemoveAll` в `OnDisabled` не вызывается.
4. WHEN категория `antitrust` выключена THEN антимонопольная надбавка к цене поглощений (FR-19) SHALL возвращать 1.0.

#### Scenarios

- **AC-17.1** Given `disaster` выключена (по умолчанию), When наступает 2011-03-11, Then `hist.2011.tohoku` получает статус `Disabled`, письма нет.
- **AC-17.2** Given игрок включил `disaster` посреди партии в 2010 году, When наступает 2011-03-11, Then `hist.2011.tohoku` срабатывает как обычная запись.

**Complexity:** Low
**Dependencies:** `mod-framework` (`ModSettings`).
**Assumptions:** нет.

### FR-18: Сохранение и загрузка

**User Story:** Как игрок, я хочу, чтобы дела, правила, ценовые войны и статусы событий переживали сохранение и загрузку.

**Priority:** Must

#### Acceptance Criteria

1. WHEN игра сохраняется THEN мод SHALL записать в секцию `historical-events`:
   - статусы и сдвиги всех записей;
   - активные дела, споры, правила, войны (с исходными ценами), гонки;
   - дату последнего `major`-события и очередь решений.

   Версию схемы секции (`_schema`) пишет ядро; в данных фичи собственного поля версии SHALL NOT быть.
2. WHEN сейв загружается THEN мод SHALL восстановить состояние планировщика и сервисов. Провайдеры каналов `market.rating.cpu` и `acquisition.price` регистрируются при старте фичи (`Register`) и читают восстановленное состояние; экземпляры событий и их эффекты восстанавливает `event-system`, догоняющие записи `ModModifiers` — фреймворк.
3. IF секции нет THEN мод SHALL считать все записи с якорем раньше текущей даты `SkippedBeforeStart` (без догоняющих эффектов), а остальные — `Pending`.
4. IF мод удалён во время ценовой войны THEN цены ИИ SHALL остаться сниженными, а `AIBehaviourMarket` SHALL постепенно вернуть их своей логикой. Это допустимо и описано в Migration.

#### Scenarios

- **AC-18.1** Given апелляция с решением через 30 месяцев, When сохранение и загрузка, Then решение по-прежнему через 30 месяцев, гарантия списывается ежемесячно.

**Complexity:** Medium
**Dependencies:** `mod-framework` (`ModSave`).
**Assumptions:** нет.

### FR-19: Антимонопольная надбавка к цене поглощений

**User Story:** Как игрок, скупающий соперников, я хочу, чтобы регуляторы удорожали сделки, которые чрезмерно концентрируют рынок, чтобы поглощения не превращались в простую скупку.

**Priority:** Should

#### Acceptance Criteria

1. WHERE категория `antitrust` включена мод SHALL зарегистрировать в `Register` провайдер канала `acquisition.price` (`ModModifiers.RegisterReasonedProvider`; канал объявлен ядром, владелец `rival-acquisitions`; регистрация не зависит от порядка `Register` фич).
2. WHEN `rival-acquisitions` считает цену цели (контекст `ModifierContext.Company` = компания-цель) THEN провайдер SHALL вернуть множитель по доле рынка после сделки `S' = Share(игрок, все сегменты, 36 мес.) + Share(цель, …)`:
   - если `S' < softShare` (0,40) и нет активного дела против игрока — 1,0, причины в `Explain` нет;
   - иначе `m = 1 + slope × max(0, S' − softShare)` (`slope` 2,5), а при активном деле `AntitrustCase` против игрока — дополнительно `× caseSurcharge` (1,25);
   - итог `min(m, maxMultiplier)` (3,0, предел канала).
3. WHEN множитель отличен от 1,0 THEN провайдер SHALL отдать причину `hist.mod.acqAntitrust` с аргументами (доля после сделки в процентах), чтобы `Explain` и окно сделки показали «Антимонопольная надбавка».
4. WHEN месячный пересчёт долей меняет `S'` или меняется состояние дела THEN фича SHALL вызвать `ModModifiers.Invalidate("acquisition.price")`.
5. Фича SHALL NOT запрещать сделку: ограничение выражено только ценой (решение по умолчанию; запрет потребовал бы хука в `rival-acquisitions`).
6. Вклад провайдера в `acquisition.price` SHALL лежать в [1,00; 3,00]; цену он не снижает.

#### Scenarios

- **AC-19.1** Given доля игрока 0.50, доля цели 0.15 (S' = 0.65) и действует дело ЕС против игрока, When `rival-acquisitions` считает цену, Then множитель = (1 + 2.5 · 0.25) × 1.25 ≈ 2.03, причина `hist.mod.acqAntitrust` видна в `Explain`.
- **AC-19.2** Given S' = 0.30 и нет дел, When считается цена, Then множитель 1.0 и записи в `Explain` нет.
- **AC-19.3** Given S' = 0.97 (доля игрока 0.80, доля цели 0.17) и дело против игрока, When считается цена, Then расчётное значение (1 + 2.5 · 0.57) × 1.25 ≈ 3.03 ограничено 3.0.

**Complexity:** Medium
**Dependencies:** FR-5, FR-3; `mod-framework` (`ModModifiers`, канал `acquisition.price`); `rival-acquisitions` (читает канал, мягкая).
**Assumptions:** `acquisition.price` — множитель цены по компании-цели, предел [0,5; 3,0] (реестр ядра); покупателем в `rival-acquisitions` всегда выступает игрок.

## Non-Functional Requirements

### NFR-1: Производительность

- WHEN проходит игровой день THEN проверка планировщика SHALL занимать не больше 0.05 мс: указатель на ближайшую запись, без перебора календаря.
- WHEN проходит месяц THEN пересчёт долей рынка (кэш ролей), порогов экспорта и топов гонок SHALL занимать не больше 3 мс при 400 CPU и 36 месяцах истории.
- Провайдеры фичи на каналах `market.rating.cpu` и `acquisition.price` SHALL работать за O(1) на вызов (сравнение с кэшированным порогом, поиск в множестве топ-N, чтение кэшированных долей).

### NFR-2: Надёжность

- IF обработчик тика, эффекта, события или патча бросает исключение THEN мод SHALL перехватить его на границе (`ModHooks.Guard`), записать в `ModLog` с `id` записи и SHALL NOT пропустить исключение в игру. Запись получает статус `Error` и больше не запускается. Свои счётчики ошибок фича не ведёт.
- IF роль ссылается на несуществующую компанию THEN запись SHALL уйти в запасной вариант, без исключения.

### NFR-3: Совместимость сейвов

- Поля в классы игры SHALL NOT добавляться. Состояние хранится только в `ModSave`.
- Сейв без секции SHALL загружаться (FR-18 п. 3). Сейв с секцией SHALL загружаться без мода.
- Версию секции (`_schema`) ведёт ядро; в данных фичи поля `schema` SHALL NOT быть.

### NFR-4: Локализация

- Все тексты (заголовки, справки, кнопки, письма, окно хроники, причины репутации и модификаторов) SHALL идти через `ModL10n` с ключами `hist.*`, ru и en.
- Даты SHALL форматироваться по `DateFormat.Current` игры.
- Названия компаний SHALL браться из игры; в справках допустимы реальные названия как исторические факты.

### NFR-5: Удобство и тон

- Между двумя событиями с решением игрока SHALL проходить не меньше 45 игровых дней (FR-2 п. 5).
- Тексты о катастрофах и пандемии SHALL быть нейтральными: факт и влияние на индустрию, без драматизации и оценок.
- Каждое число в письме SHALL совпадать с применённым эффектом: расчёт один раз, хранение в состоянии.

### NFR-6: Детерминизм

- Сдвиги, исходы апелляций, выбор ИИ и успех лоббирования SHALL идти через `ModRandom` фичи (`ModRandom.Stream("historical-events")`) и повторяться после загрузки.

### NFR-7: Качество контента

- Каждая запись SHALL иметь минимум один источник. URL SHALL быть проверен человеком до релиза: задача сверки фактов в `tasks.md`.
- Даты якорей SHALL совпадать с датами в справке.

### NFR-8: Безопасность и валидация данных

- WHEN загружается контент (встроенный или пользовательский из `<папка игры>/PTMod/content/historical-events/`) THEN мод SHALL проверить даты, диапазоны множителей и вероятностей, существование событий выбора и ключей строк (раздел Security в design) и SHALL пропускать записи с ошибками.
- WHEN загружается секция сейва THEN мод SHALL нормализовать недопустимые значения (отрицательные счётчики, нечисловые суммы) и проверять ссылки на `SaveID` при использовании.
- URL источников SHALL только отображаться (домен в письме); фича SHALL NOT открывать их и SHALL NOT обращаться к сети.

### NFR-9: Вклад в общие каналы и баланс

- Фича SHALL использовать только каналы реестра ядра; новых каналов она не объявляет.
- Максимальные вклады фичи (пределы суммарного эффекта задаёт ядро; тесты эффектов учитывают это ограничение):

| Канал | Вклад |
|---|---|
| `market.rating.cpu` | провайдер: экспорт [0,80; 1,00], гонки [1,00; 1,10]; записи `ModifierTimed`: `hist.1987.koshiba` ×0,90 (365 дн.), `hist.1994.fdiv` (запасной вариант) ×0,95 (180 дн.), `hist.2018.meltdown` (запасной вариант) ×0,95 (365 дн.). Худшее совпадение экспорта и записи: 0,80 × 0,90 = 0,72 > 0,5, нижний предел ядра [0,5; 2,0] из-за этой фичи не достигается |
| `popularity.gain` | только повышающие записи: бренд-программа ×1,20 (730 дн.), кооперативный маркетинг и маркетинг в ценовой войне ×1,10, приоритет рознице ×1,10; худшее совпадение 1,20 × 1,10 × 1,10 ≈ 1,45; снижающих вкладов нет |
| `acquisition.price` | провайдер антимонопольной надбавки [1,00; 3,00] |
| `gpu.demand` | криптобумы ×1,35 (2017) и ×1,45 (2021), не пересекаются во времени |
| `contract.signChance`, `contract.reward`, `production.unitCost`, `bank.*`, `taxes`, `research.cost` | записи `ModifierTimed` из таблицы календаря (в пределах предохранителей каналов) |

## Edge Cases

| Случай | Ожидаемое поведение |
|---|---|
| Старт игры после 2025 года (если меню позволяет) | Все записи `SkippedBeforeStart`, догоняющие эффекты по FR-4 |
| Игрок — единственная CPU-компания с продажами | `leader` = игрок, `challenger` не распознан → запасной вариант для записей, где он обязателен |
| Доли рынка равны | Побеждает меньший `SaveID` (детерминизм) |
| Цель обанкротилась во время апелляции | Дело закрывается без платежей; в хронике «закрыто: банкротство» |
| Цель — игрок, денег нет | Оплата проходит, компания уходит в минус; дальше логика `BankruptcyHandler`; апелляция остаётся доступной |
| CPU ИИ создан во время ценовой войны | Не затронут; новые цены ставит ИИ |
| Ценовая война и `ConsiderReducingPrices` ИИ (сложность ≥ 3) | Обе логики снижают цену; пол `RealUnitCost × 1.05` защищает от убытка |
| Экспортный порог выше всех CPU | Множитель ни к кому не применяется; правило ждёт следующего месяца |
| Две записи требуют решения игрока в один день | Очередь (FR-2 п. 6), интервал 45 дней (FR-2 п. 5) |
| `design-flaw-recalls` выключена | `ForceDesignFlaw` → `fallbackEffects` (FR-12 п. 2) |
| `event-system` выключена | Фича не может показывать выбор: игроку применяется `defaultChoice` с письмом; новости работают |
| `gpu-product-line` выключена | Криптобумы не срабатывают (условие `featureActive`), уходят в `skip` |
| `rival-acquisitions` выключена | Провайдер `acquisition.price` не читается; побочных эффектов нет |
| Категория `disaster` выключена (по умолчанию) | `hist.1999.jiji`, `hist.2011.tohoku`, `hist.2020.covid` → `Disabled`, догоняющие эффекты не применяются |
| Загрузка сейва, сделанного до 2009, в момент дела | Секция восстанавливает дело; новых бросков нет |
| Отзыв x86-лицензии при загрузке | Письмо не отправляется (FR-16, защита загрузки) |
| Игрок нажал «Следующий месяц» с несколькими срабатываниями записей внутри месяца | Тики идут по дням; письма и окна показываются после `BatchEnded`, очередь решений и интервал 45 дней соблюдаются |
| Игра запущена без перемотки (старт в первый год) | Инициализация на `GameReady(NewGame)` (FR-4 п. 5) |

## Constraints and Assumptions

### Technical Constraints

- Рейтинг CPU меняется только через стандартный канал `market.rating.cpu` (провайдер фичи с областью CPU и сегмента). Метод `Market.CalculateRatings` зарезервирован каналом.
- Деньги идут только через `ICompany.SpendMoney` и `ICompany.EarnMoney` (напрямую или эффектом `Money` движка) с существующими `ExpenseOrigin` и `RevenueOrigin`.
- Цены ИИ меняются через `RivalPersonalities.Api.ApplyPriceCut` / `RestorePriceCut`. Только при неактивной `rival-personalities` (`Api.IsEnabled == false`) допустимо прямое разовое изменение `Cpu.Price` и `CpuBase.AIStrategy.TargetPrice`. Патчей `AIBehaviour*` у фичи нет.
- Компании в состоянии фичи хранятся по ключу `ModCompanies.KeyOf` (`player` / `ai:<UniqueID>`).
- Собственных патчей на `Company.AddCpu`, `AICompany.AddCpu`, `DateController.NextMonth`, `CentralBank.DebtLimit` нет; единственный патч — Optional-постфикс для письма к встроенному событию (FR-16).
- Код `Logic/` — общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`, `Range`); тесты — мини-раннер ядра (`[Test]`, `[Covers(typeof(X))]`), запуск `python mod/build.py --tests`.

### Business Constraints

- Числа баланса — в контенте.
- Справки — короткие пересказы своими словами со ссылкой, без цитирования источников.

### Assumptions

- `event-system` предоставляет `EventRegistry.RegisterPack` с `PackOptions.Severity`, `EventBus.Fire` с `EventFireContext` (`CompanyKey`, `Args`), `EventDefinition` с `choices`, `timerDays`, `defaultChoice`, `cause`, `important`, `notifyPlayer`, стандартные эффекты, `EventEffects.Register`, `EventConditions.Register`, `EventAi.AddChooser` и предикат `es.activeInCategory` с `scope`.
- `mod-framework` предоставляет `ModModifiers.RegisterReasonedProvider` и `ModModifiers.Add` со сроком, `ModModifiers.Invalidate`, `ModSegments`, `ModCredit`, `ModMail` с тегами и приоритетами.
- Средний `Ips` рынка доступен через `Market.Instance.AverageIps` (`Market.get_AverageIps`).

## Success Criteria

### Definition of Done

- [ ] 42 записи календаря с проверенными источниками и справками на ru и en.
- [ ] Все Must-FR реализованы; Should — реализованы или перенесены владельцем.
- [ ] Юнит-тесты (`RoleResolver`, `CalendarScheduler`, `AntitrustCaseMachine`, `ExportRule`, `PriceWarPlan`, `PatentDispute`, `AcquisitionAntitrust`, валидатор, сохранение) зелёные в мини-раннере; каждый публичный тип `Logic/` указан в `[Covers]`.
- [ ] Симуляция календаря на синтетических мирах: нет ошибок и нужное число событий (метрики ниже).
- [ ] Ручные сценарии владельца пройдены.

### Acceptance Metrics

- Симуляция 1975–2030 (старт 1975, 100 синтетических миров):
  - срабатывает 27–35 записей при настройках по умолчанию (30–38 при включённой `disaster`);
  - решений игрока 7–13 (8–14 при включённой `disaster`);
  - нет двух `major`-событий ближе 45 дней.
- Мир «игрок доминирует» (доля > 0.6 с 1995): игрок — цель минимум двух антимонопольных записей из трёх.
- Мир «игрок слаб» (доля < 0.05): игрок не цель ни одной антимонопольной записи.
- 0 ошибок `[PTMod]` уровня Error в `Player.log` за 20 игровых лет.

## Решения по умолчанию

Владелец ответил на открытые вопросы v0.1 в `.kiro/steering/owner-decisions.md` (2026-10-10). Ниже — ответы, применённые в этой спеке, и решения, принятые по общему правилу («если варианта нет — самый простой безопасный»). Любое решение можно переиграть: каскадом правятся `requirements.md` → `design.md` → `tasks.md`.

### Ответы на вопросы v0.1

| № | Вопрос v0.1 | Решение | Где в спеке |
|---|---|---|---|
| 1 | Реальные названия в справках | реальные названия везде, как в русификаторе (Intel, AMD, Toshiba, TSMC…); в контенте компании указываются по `UniqueID`, имя берётся из игры | FR-1 п. 5, NFR-4 |
| 2 | Катастрофы и пандемия | отдельная категория `disaster`, по умолчанию выключена | FR-17 п. 1, AC-4.2 |
| 3 | Режим по умолчанию | `adaptive` | FR-3 п. 5 |
| 4 | Статья возврата штрафа | `RevenueOrigin.Interest` (принято событиями `event-system` для доходов) | FR-5, AC-5.3 |
| 5 | Доступ к `PriceBook` / фолбэк без `rival-personalities` | `rival-personalities` владеет ценовыми войнами (`IRivalPersonalitiesApi.ApplyPriceCut` / `RestorePriceCut`); фолбэк без неё допустим | FR-8 |
| 6 | Экспортный контроль без регионов | принято: доля покупателей сегмента, недоступная быстрым CPU | FR-7 |
| 7 | Спойлеры в хронике | будущие события скрыты (`spoilers = false`) | FR-15 |
| 8 | Пересечение с `market-shocks` | её события той же природы проверяют `not es.activeInCategory(history, scope any)`; DRAM-записи остаются на `production.unitCost`; граница: общий антидемпинг — `market-shocks`, реальные соглашения — здесь | FR-10 п. 6, Assumptions |
| 9 | Вымышленные события 2026–2030 | не нужны (вне скоупа) | Scope |
| 10 | Интенсивность для ИИ | одинаковые правила для ИИ и игрока, без ослабления на низкой сложности | FR-5 п. 9, FR-10 п. 3, FR-13 п. 6 |
| 11 | Справка при `historyNotes = false` | скрывается только `cause`; отдельных текстов нет | FR-14 п. 4 |
| 12 | Криптобумы 2017 и 2021 годов (вопрос `gpu-product-line`) | здесь, с эффектом на спрос GPU (канал `gpu.demand`); записи 41–42 | FR-10, AC-10.2, AC-4.3 |
| 13 | Антимонопольные ограничения для поглощений (вопрос `rival-acquisitions`) | здесь, как надбавка к цене канала `acquisition.price` | FR-19 |

### Общие политики владельца

| Политика | Где в спеке |
|---|---|
| Перемотка новой игры нейтральна; состояние, нужное ИИ к старту, инициализируется один раз на `TickPhase.FastForwardEnded` | FR-4 п. 1, 2, 5 |
| Реальные названия; издания вымышленные (здесь изданий нет) | FR-1 п. 5, NFR-4 |
| Одинаковые правила для ИИ и игрока | FR-5 п. 9, FR-13 п. 6 |
| Предел суммарного эффекта `market.rating.cpu` и `popularity.gain` [0,5; 2,0], в спеке-поставщике указан максимальный вклад | FR-7 п. 7, FR-11 п. 3, NFR-9 |
| `ModSegments.Classify` / `FromSales` вместо собственного «основного сегмента» | FR-3 п. 7, FR-8 п. 6 |
| `ModCredit.ThresholdOf` вместо `CentralBank.DebtLimit` | FR-13 п. 2 |
| `EventAi.AddChooser`: список по убыванию приоритета, побеждает первый ненулевой ответ | FR-13 п. 3 |
| `ModTicks.IsBatchRun`; фича не патчит `NextMonth` | FR-2 п. 8 |
| `ModMail`: теги `hist:<…>`, приоритеты, блоков ID нет | FR-4 п. 4, FR-14 п. 6 |
| Версию секции сейва (`_schema`) ведёт ядро | FR-18 п. 1, NFR-3 |
| Каналы только из реестра ядра; записи выключенной фичи хранятся и не применяются | NFR-9, FR-17 п. 3 |

### Решения, принятые по общему правилу при переработке v0.2

| Вопрос | Решение | Обоснование |
|---|---|---|
| Форма антимонопольного ограничения поглощений | надбавка к цене через провайдер `acquisition.price`: `1 + 2,5 × (S' − 0,40)`, ×1,25 при активном деле, предел 3,0; запрета нет | канал уже объявлен ядром и читается `rival-acquisitions`; запрет потребовал бы нового хука в чужой спеке |
| Параметры криптобумов | 2017: якорь 2017-10-01, `gpu.demand` ×1,35 на 365 дней; 2021: якорь 2021-04-01, ×1,45 на 365 дней; оба `catchUp`, условие `featureActive{gpu-product-line}` | эффект имеет смысл только при включённой линейке GPU; значения лежат в контенте |
| Нотация условий календаря | нотация `ModConditions` (`type` и параметры на верхнем уровне), роли через область вызова `HistRoleScope` | единый синтаксис с `requires`/`filter` движка |
| Катастрофы и догоняющие эффекты | выключенная категория не догоняется | эффект не должен действовать там, где письма нет |
| Причины репутации и модификаторов | ключи L10n фичи (`hist.rep.*`, `hist.mod.*`) | соглашение об идентификаторах (правило 5) |
| Предикаты календаря | `hist.ShareAtLeast`, `hist.HasCpuSince`, `hist.CaseActive`, `hist.PlayerHasCpuAbove`, `hist.IsFabless`; стандартные — `dateAtLeast`, `moneyAtLeast`, `cpuOnMarket`, `es.hasFab`, `featureActive` | префиксы ядра (правило 2) |
| Фолбэк `ForceDesignFlaw` при отсутствии CPU | запасные эффекты записи | поведение v0.1 |
| Приоритет письма догона | `Normal`, тег `hist:catchup` | не выбор с таймером |

## Открытые вопросы владельцу

Открытых вопросов нет: все вопросы v0.1 закрыты решениями владельца или решениями по общему правилу (таблицы выше). Числа надбавки поглощений и криптобумов лежат в контенте (`params.json`, `events.json`) и правятся без сборки.

## Glossary

| Термин | Значение |
|---|---|
| Запись календаря | Одно историческое событие: якорь, окно, роли, условия, эффекты, справка |
| Якорь | Историческая дата события (например, 2009-05-13) |
| Окно | Период, в который событие ещё может сработать при невыполненных условиях |
| Сдвиг (jitter) | Случайное смещение якоря на ±N дней, фиксируется на партию |
| Роль | Участник события в мире партии: лидер, соперник, литейщик, аналог |
| Аналог | Игровая компания, соответствующая исторической (названия — реальные, как в русификаторе): UniqueID 0 — Intel, 1 — AMD, 6 — Toshiba, 9 — TSMC, 10 — Cyrix |
| Адаптация | Подбор роли по состоянию партии вместо жёсткой привязки к компании |
| Запасной вариант | Что происходит, если роль не найдена или окно истекло: новость или ничего |
| `major` | Событие с выбором или сильным эффектом; соблюдает интервал 45 дней |
| Догоняющий эффект | Эффект события до старта, который ещё действует на дату старта |
| Дело | Антимонопольный процесс с состояниями: решение, апелляция, повторный штраф |
| Резерв | Оплата штрафа сейчас с возвратом при отмене |
| Порог экспорта | Производительность (`Ips`), выше которой CPU попадает под ограничения |
| Ценовая война | Временное снижение цен ИИ в сегментах |
| Антимонопольная надбавка | Множитель цены поглощения (канал `acquisition.price`), растущий с долей рынка игрока после сделки |
| `ModSegments` | Служба ядра: сегмент CPU по цене и по продажам |
