# Requirements: mod-framework

## Document Information

- **Feature Name**: mod-framework — основа всех модов Processor Tycoon RU (`PTMod.Core`)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Stakeholders**: владелец форка (Dex719) — одобрил решения 0001–0006 и реализацию 2026-10-10, выполняет ручной прогон игры; игроки форка; авторы 15 зависимых спек (`first-year-onboarding`, `contract-transparency`, `research-tree-ux`, `bankruptcy-recovery`, `event-system`, `market-shocks`, `design-flaw-recalls`, `historical-events`, `press-reviews`, `rival-personalities`, `rival-acquisitions`, `scenarios-achievements`, `platform-generations`, `cpu-architectures`, `gpu-product-line`)
- **Related Documents**:
  - `.kiro/steering/context.md` — архитектурный контракт `PTMod.Core` (имена сервисов здесь — контракт для остальных спек)
  - `.kiro/steering/roadmap.md` — волна 0
  - `.kiro/steering/owner-decisions.md` — решения владельца по открытым вопросам (внесены в эту спеку, см. «Решения по умолчанию»)
  - `.kiro/decisions/0001`–`0006` (все в статусе accepted; 0003 обновлено: Roslyn `csc` из VS Build Tools, без .NET SDK)
  - `pt-modding/CODEMAP.md`, `pt-modding/code/`, `pt-modding/data/` — справочник по коду игры (локально)
- **Идеи и источники** (`pt-modding/IDEAS.md`): своей идеи у фреймворка нет; ближайшая — I26 «Data-driven catalogue, mod hooks…» (запрос на поддержку модов; разработчик ответил, что она «может появиться позже»). Фреймворк — инфраструктура для I01, I02, I03, I06, I09, I12, I13, I14, I16, I18, I22, I23, I24, I36, I37.
  - <https://coffeeheaven.itch.io/processor-tycoon/comments>
  - <https://coffeeheaven.itch.io/processor-tycoon/comments?after=65>
  - <https://coffeeheaven.itch.io/processor-tycoon/comments?after=87>
  - <https://coffeeheaven.itch.io/processor-tycoon/comments?after=99>
  - <https://coffeeheaven.itch.io/processor-tycoon/comments?after=105>
  - <https://itch.io/devlog/990382/0118-released.amp>
  - <https://steamcommunity.com/app/1342330/discussions/0/3881598799623269734> (I26: конфликты модов при перезаписи общих файлов)

## Introduction

Processor Tycoon 0.2.3 заброшена автором, а игроки в комментариях на itch.io просят обучение, мягкое банкротство, прозрачность контрактов, события, характеры соперников и новые продуктовые линии. Все эти моды упираются в одни и те же вещи: загрузить свой код рядом с `Assembly-CSharp.dll`, пропатчить методы игры, хранить своё состояние в сейве, показать письмо или окно, менять формулы рынка, не сломав старые сейвы и русификатор.

Спека `mod-framework` описывает сборку `PTMod.dll` (пространство имён `PTMod.Core`) и всё вокруг неё: загрузку игрой, патчи, тики, боковой файл сейва, настройки, локализацию, ГСЧ, JSON-контент, условия, модификаторы со стандартными каналами, репутацию бренда, письма с выбором, уведомления, рантайм-UI, хаб «Моды», самопроверку, установщик и сборку. Отдельно — спайки по решениям 0001–0003 и фолбэки на случай их провала.

Версия 0.2 вносит решения владельца (`.kiro/steering/owner-decisions.md`) и замечания кросс-ревью спек. В ядро перенесены общие механизмы, которые иначе дублировались бы в 8–10 фичах: события выпуска CPU (`ModEvents`), классификатор сегмента (`ModSegments`), признак пакетного прогона месяца (`ModTicks.IsBatchRun`), единый порог кредита (`ModCredit`), дрейф репутации, профиль с резервной копией, многоразовые кнопки и приоритеты писем (`ModMail`), контексты каналов (`Contract`, проект CPU) и балансовый диапазон (`ModModifiers`), секции окна новой игры (`ModNewGame`), реестр каналов и соглашение об идентификаторах.

### Feature Summary

Общая библиотека и инфраструктура, на которой 15 геймплейных спек строят моды без правки `Assembly-CSharp.dll` и `data.unity3d`.

### Business Value

- **Для игрока.** Моды из волн 1–4 (обучение, спасение от банкротства, события, соперники, GPU) становятся возможны и устанавливаются одной командой. Ванильные сейвы продолжают грузиться, мод снимается без потери прогресса, фичи включаются и выключаются в одном окне.
- **Доказательства спроса.** I01 (обучение) и I02 (банкротство) — спрос `high` в `IDEAS.md`. Комментарии itch.io: «старт запутанный, ранние банкротства даже на Easy», разработчик признал нужду в туториале. Запрос на поддержку модов есть в I26. 15 одобренных владельцем идей требуют одних и тех же механизмов: 12 из них используют письма или окна, 10 — формулы рынка или экономики, все — сохранение состояния.
- **Для проекта.** Единый слой исключает конфликт патчей (одна формула — один владелец), а общие соглашения по сейвам, локализации и контенту делают 15 спек независимыми.

### Scope

**Входит:**
- сервисы контракта `PTMod.Core`: `ModBootstrap`, `ModHooks`, `ModTicks`, `ModSave`, `ModModifiers`, `ModConditions`, `ModMail`, `ModNotify`, `ModUI`, `ModL10n`, `ModSettings`, `ModRandom`, `ModContent`, `ModLog`;
- дополнительно: `ModReputation` (хранение, API и ежемесячный дрейф к 50), `ModCompanies` (стабильные ключи компаний), `ModJson` (парсер), `ModProfile` (глобальные данные профиля с резервной копией и троттлингом записи), `ModNewGame` (слот, секции и предпросмотр в меню новой игры), реестр фич, `ModSelfCheck`;
- общие механизмы, перенесённые из фич по решению владельца: `ModEvents` (выпуск и перенос CPU), `ModSegments` (сегмент CPU по цене), `ModTicks.IsBatchRun` (пакетный прогон месяца), `ModCredit` (единый порог дефолта), `ModIds` (проверка соглашения об идентификаторах);
- стандартные каналы `ModModifiers`, реестр каналов (все каналы объявляются ядром до `Register` фич) и единственные патчи на формулы, которые их читают;
- хаб «Моды» (кнопка + окно: фичи, диагностика, о моде; подтверждение при выключении фичи);
- расширение `install.py` (`--with-mods`, `--mods-only`, `--remove-mods`, `--restore`, проверка версии, бэкап двух JSON, поиск готовых DLL и сборка через `csc`);
- сборка (`mod/build.py` на Roslyn `csc`, свой мини-раннер тестов), спайки 0001–0003 и фолбэки;
- инструменты разработчика: проверка ссылок `Class.Method` и имён API ядра в спеках, дамп путей шаблонов UI;
- служебная фича `framework-sample` (выключена по умолчанию, только в отладочной сборке) для ручной проверки владельцем.

**Не входит:**
- игровой эффект репутации (влияние на популярность) и её отображение — `press-reviews`;
- движок событий, журнал событий — `event-system`;
- содержимое опций новой игры («Обучение», выбор сценария) — `first-year-onboarding`, `scenarios-achievements`; фреймворк даёт слот, общую панель и секции;
- окна конкретных фич (меню спасения, панель соперников и т. п.);
- правка `data.unity3d`, свои AssetBundle, сеть, мультиплеер;
- перевод строк игры — это русификатор (`ptru/`).

## Requirements

### FR-1: Загрузка мода игрой (ModBootstrap, решение 0001)

**User Story:** Как владелец форка, я хочу, чтобы игра сама загружала `PTMod.dll` без пересборки `Assembly-CSharp.dll`, чтобы моды сосуществовали с русификатором и снимались откатом двух JSON.

#### Acceptance Criteria

1. WHEN игра стартует, а `PTMod.dll` лежит в `Processor Tycoon_Data/Managed/` и зарегистрирован в `ScriptingAssemblies.json` и `RuntimeInitializeOnLoads.json` THEN PTMod SHALL вызвать `ModBootstrap.Init` до загрузки первой сцены и записать в `Player.log` строку `[PTMod] Init <версия мода>`.
2. WHEN `ModBootstrap.Init` выполняется THEN PTMod SHALL создать ровно один объект `PTModHost` с `DontDestroyOnLoad`.
3. IF `ModBootstrap.Init` вызывается повторно в том же процессе THEN PTMod SHALL игнорировать повторный вызов.
4. IF при инициализации возникает исключение THEN PTMod SHALL записать его в лог, прекратить инициализацию и не пропустить исключение в код игры; игра SHALL продолжить работу как ванильная.
5. IF спайк решения 0001 провален THEN проект SHALL перейти на фолбэк (вызов `ModBootstrap.Init` из IL-хука в `GameManager.Awake`, вставленного патчером `ptru`), не меняя API `ModBootstrap`.

**Сценарии:**
- **AC-1.1.** Given мод установлен `install.py --with-mods`; When владелец запускает игру; Then в `Player.log` есть `[PTMod] Init 0.1.0` раньше первой строки загрузки сцены `level0`.
- **AC-1.2.** Given `PTMod.dll` удалён, а записи в JSON остались; When игра стартует; Then игра запускается как ванильная, а Unity пишет предупреждение о недостающей сборке (проверяется в спайке).
- **AC-1.3.** Given исключение в `ModBootstrap.Init` (служебный флаг `debug.failInit` в настройках); When игра стартует; Then главное меню открывается, в логе одна запись `[PTMod] [core] ERROR Init failed`.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium (High при фолбэке)
- **Dependencies**: решение 0001; FR-20 (установщик), FR-21 (сборка)
- **Assumptions**: Unity 6 Mono-плеер читает `ScriptingAssemblies.json` и `RuntimeInitializeOnLoads.json` при старте. Это не документировано и проверяется спайком.

### FR-2: Патчи методов и изоляция ошибок (ModHooks, решение 0002)

**User Story:** Как автор фичи, я хочу объявлять prefix/postfix/transpiler/finalizer на методы игры, чтобы менять поведение. Сбой моего патча должен отключать только мою фичу, а не ломать игру.

#### Acceptance Criteria

1. WHEN фича объявляет патч THEN ModHooks SHALL внести его в реестр: цель (`Class.Method` и сигнатура), вид, приоритет, ID фичи, признак `Optional`.
2. WHEN применяются патчи фичи THEN ModHooks SHALL применить все обязательные патчи фичи или ни одного. При сбое ModHooks SHALL откатить уже применённые патчи этой фичи, перевести её в состояние `Unavailable` и записать причину в лог.
3. IF тело патча бросает исключение THEN обёртка ModHooks SHALL перехватить его, вернуть игре ванильное поведение для этого вызова и записать стек в лог: в первый раз полностью, дальше только счётчик.
4. IF число исключений одной фичи за сессию достигает `hooks.faultThreshold` (5, контент) THEN ModHooks SHALL перевести фичу в `Faulted` и не вызывать её логику до перезапуска игры.
5. IF две фичи объявляют transpiler на один метод THEN ModHooks SHALL отклонить второй и показать конфликт в самопроверке.
6. IF фича объявляет патч на метод, зарезервированный ядром THEN ModHooks SHALL отклонить патч с ошибкой, называющей причину и API-замену. Резерв бывает трёх видов (колонка «Резерв» таблицы патчей в design.md): формула канала `ModModifiers` («формулу читает канал <name>, используйте ModModifiers»), служба ядра («выпуск CPU — событие `ModEvents.CpuReleased`, свой патч на `Company.AddCpu` не нужен») и формула другой фичи-владельца канала («цель закреплена за фичей <id>»).
7. IF Harmony недоступен (спайк 0002 провален) THEN ModHooks SHALL работать через запасной бэкенд. Фичи, которым нужен transpiler или вид патча, которого бэкенд не поддерживает, SHALL получить статус `Unavailable` с причиной.
8. WHEN ядро поднимает своё событие (`ModEvents.*`, `ModTicks.GameReady`, `ModTicks.BatchEnded`, `ModMail.Delivered`, `ModReputation.Changed`, `ModNewGame.*`, `ModFeatures.StateChanged`) THEN ModHooks SHALL вызывать каждый обработчик в `try/catch`, учитывать исключение в счётчике фичи-владельца обработчика (по пространству имён `PTMod.Features.<Name>`) и пропускать обработчики фич в состояниях `Disabled`, `Faulted`, `Unavailable`.

**Сценарии:**
- **AC-2.1.** Given фича `framework-sample` объявила патч на несуществующий метод (флаг `debug.brokenPatch`); When мод стартует; Then фича в состоянии `Unavailable`, её остальные патчи не применены, остальные фичи работают, в логе одна ошибка с именем цели.
- **AC-2.2.** Given патч `framework-sample` бросает исключение на каждый день (флаг `debug.throwOnTick`); When проходит 5 игровых дней; Then фича в `Faulted`, игра идёт, в логе 1 полный стек и сводка «повторено 4 раза».
- **AC-2.3.** Given фича пытается пропатчить `Market.CalculateRatings`; When применяются патчи; Then патч отклонён, в «Диагностике» — ошибка с подсказкой про канал `market.rating.cpu`.
- **AC-2.4.** Given фича объявляет postfix на `Company.AddCpu`; When применяются патчи; Then патч отклонён с подсказкой «используйте `ModEvents.CpuReleased`», фича `Unavailable`, остальные фичи работают.
- **AC-2.5.** Given обработчик `ModEvents.CpuReleased` фичи A бросает исключение; When ядро поднимает событие; Then обработчик фичи B вызван, в логе одна запись с ID фичи A, счётчик ошибок A увеличен на 1.

#### Additional Details
- **Priority**: Must
- **Complexity**: High
- **Dependencies**: решение 0002 (загрузка `0Harmony.dll` одобрена владельцем; версия 2.3.6, сборка net48, файл `mod/lib/0Harmony.dll` уже на месте); FR-3, FR-7
- **Assumptions**: Harmony 2.3.6 работает на Unity 6000.0.65f1 Mono, проверяется спайком.

### FR-3: Реестр фич и жизненный цикл

**User Story:** Как игрок, я хочу включать и выключать отдельные моды, чтобы собрать нужный набор. Как автор фичи, я хочу объявить зависимости и получать вызовы в правильном порядке.

#### Acceptance Criteria

1. WHEN `ModBootstrap` инициализируется THEN PTMod SHALL найти все классы с атрибутом `[ModFeature]`, упорядочить их по зависимостям (`DependsOn`) и вызвать `Register` у каждой включённой фичи.
2. IF зависимость фичи отсутствует, выключена или находится в `Faulted`/`Unavailable` THEN PTMod SHALL перевести фичу в `Unavailable` с указанием зависимости.
3. IF зависимости образуют цикл THEN PTMod SHALL перевести все фичи цикла в `Unavailable`.
4. WHEN игрок выключает фичу в хабе и фича поддерживает горячее переключение THEN PTMod SHALL снять её патчи, перестать вызывать её обработчики тиков и событий ядра (`ModEvents`, `ModTicks.GameReady`, `ModMail.Delivered` и т. п.) и действий писем, и не применять её записи и провайдеры `ModModifiers`. Записи фичи при этом НЕ удаляются (FR-12 п. 8).
5. IF фича не поддерживает горячее переключение THEN PTMod SHALL сохранить новое значение в `ModSettings` и показать «Вступит в силу после загрузки сейва».
6. WHILE фича выключена PTMod SHALL переписывать её секцию `ModSave` при сохранении без изменений.
7. ID фичи SHALL совпадать со slug её спеки, а ключ секции `ModSave` — с ID фичи.
8. WHERE фича вызвала `FeatureContext.ConfirmBeforeDisable(provider)` и `provider()` вернул не `null` THEN хаб SHALL перед выключением показать модальное подтверждение с текстом фичи; выключение (`OnDisabled`) SHALL выполняться только после «Да», а «Отмена» SHALL оставить фичу включённой. Подтверждение SHALL запрашиваться только из хаба.

**Сценарии:**
- **AC-3.1.** Given `market-shocks` зависит от `event-system`, а `event-system` выключена; When мод стартует; Then `market-shocks` в `Unavailable` с текстом «нужна фича event-system».
- **AC-3.2.** Given `framework-sample` включена и добавила множитель `research.speed ×2`; When игрок выключает её в хабе; Then время исследования в дереве возвращается к ванильному в течение 1 игрового дня, а запись множителя остаётся в сейве.
- **AC-3.3.** Given `framework-sample` вызвала `ConfirmBeforeDisable` с текстом «Выключение закроет демо-обязательство»; When игрок выключает её в хабе и жмёт «Отмена»; Then фича осталась включённой, `OnDisabled` не вызван; When повторяет и жмёт «Да»; Then `OnDisabled` вызван один раз.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-2, FR-6
- **Assumptions**: все фичи живут в одной сборке `PTMod.dll`.

### FR-4: Тики игрового дня (ModTicks)

**User Story:** Как автор фичи, я хочу подписаться на фазы игрового дня с защитами от перемотки и загрузки, чтобы не писать проверки в каждой фиче и не получать двойных вызовов после загрузки сейва.

#### Acceptance Criteria

1. WHEN `DateController` поднимает событие фазы дня THEN ModTicks SHALL вызвать подписчиков этой фазы в порядке «приоритет, затем порядок регистрации». Вызов SHALL идти после обработчиков игры, подписанных в `Start`.
2. WHILE `DateController.IsFastFowardSimulating` = true ModTicks SHALL не вызывать подписчиков без флага `RunDuringFastForward`. Перемотка идёт под экраном загрузки, поэтому подписчику с этим флагом флаг `RunDuringLoading` не нужен.
3. WHILE `GameManager.get_IsLoading` = true ModTicks SHALL не вызывать подписчиков без флага `RunDuringLoading`. Исключение — фаза `FastForwardEnded`: к её моменту `IsFastFowardSimulating` уже сброшен, а экран загрузки ещё поднят, и фаза доходит до всех подписчиков (точка однократной инициализации состояния ИИ к старту, политика владельца; уточнено при реализации TSK-024).
4. WHEN игровая сцена загружается заново (загрузка сейва, новая игра) THEN ModTicks SHALL подписаться на новый экземпляр `DateController` ровно один раз. IF экземпляр тот же THEN ModTicks SHALL не подписываться повторно.
5. IF обработчик бросает исключение THEN ModTicks SHALL изолировать его по правилам FR-2 (п. 3–4), а остальные обработчики фазы SHALL выполниться.
6. WHEN состояние игры готово (после `SaveHandler.OnLastLoadedTick` или после `Player.OnNewGame`) THEN ModTicks SHALL поднять `GameReady` с причиной `Loaded` или `NewGame` ровно один раз на сцену.
7. ModTicks SHALL замерять время обработчиков по фазам (среднее и максимум за последние 30 игровых дней) для вкладки «Диагностика».
8. WHILE выполняется `DateController.NextMonth` (до 31 тика за один кадр) ModTicks SHALL держать `IsBatchRun` = true, в том числе при исключении внутри метода. Вложенные вызовы SHALL учитываться счётчиком. WHEN внешний вызов завершается THEN ModTicks SHALL поднять `BatchEnded` ровно один раз.
9. WHILE `IsBatchRun` = true подписчики фаз SHALL вызываться как обычно (по каждому дню), а ModMail и ModNotify SHALL откладывать показ до `BatchEnded`. Фичи SHALL не ставить собственные патчи на `DateController.NextMonth` (FR-2 п. 6).

**Сценарии:**
- **AC-4.1.** Given новая игра со стартом в 2000 году (перемотка ≈ 9 100 тиков); When идёт перемотка; Then подписчики `DayPassed` без флага не вызываются ни разу, а `GameReady(NewGame)` приходит один раз после `Player.OnNewGame`.
- **AC-4.2.** Given игрок трижды подряд загружает один сейв; When проходит 1 игровой день; Then каждый подписчик `DayPassed` вызван ровно 1 раз.
- **AC-4.3.** Given игрок нажимает «Следующий месяц» (31 тик за кадр), а `framework-sample` отправляет письмо в каждом `DayPassed`; When идёт прогон; Then в обработчиках `IsBatchRun` = true, ни одно письмо не появилось до конца прогона, `BatchEnded` пришёл один раз, а после него доставлено не больше лимита письма `Normal` и все `Critical`.
- **AC-4.4.** Given вложенный вход в пакетный прогон (глубина 2) и исключение во внутреннем (юнит-тест `BatchCounter`); When оба вызова завершены; Then глубина 0, `IsBatchRun` = false, `BatchEnded` поднят один раз.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-1, FR-2; патч `DateController.NextMonth` (P21)
- **Assumptions**: `DateController` — объект сцены `level1`. Пересоздаётся ли он при загрузке, не проверено (CODEMAP, Uncertain), поэтому ModTicks поддерживает оба случая.

### FR-5: Боковой файл сохранения (ModSave, решение 0004)

**User Story:** Как игрок, я хочу, чтобы состояние модов сохранялось вместе с сейвом, но ванильный сейв не менялся, чтобы можно было снять мод и не потерять игру.

#### Acceptance Criteria

1. WHEN игра записывает сейв через `SaveSystem.Save` (ручной сейв или автосейв) THEN ModSave SHALL атомарно записать `<имя>.ptmod.json` рядом с `<имя>.txt` в `persistentDataPath/Saves/`. Имя SHALL совпадать с тем, что использовала игра, включая фолбэк `Y_M_D` для пустого имени и `autosave`.
2. WHEN сейв загружается THEN ModSave SHALL прочитать боковой файл в `SaveHandler.OnLoaded` и раздать секции участникам до `GameReady`.
3. IF бокового файла нет или в нём нет секции фичи THEN ModSave SHALL выдать фиче значения по умолчанию, а загрузка SHALL завершиться без ошибок.
4. IF боковой файл не разбирается как JSON или нарушает формат THEN ModSave SHALL переименовать его в `<имя>.ptmod.json.corrupt-<yyyyMMddHHmmss>`, выдать значения по умолчанию, показать уведомление и записать причину в лог.
5. WHILE секция принадлежит неизвестной или выключенной фиче ModSave SHALL переписывать её при каждом сохранении байт-в-байт по содержанию (без изменений полей).
6. IF версия схемы секции (`_schema`) новее, чем поддерживает фича THEN ModSave SHALL выдать фиче значения по умолчанию и при записи сохранить исходную секцию без изменений.
7. WHEN игрок удаляет сейв в окне загрузки или сохранения THEN ModSave SHALL удалить соответствующий `<имя>.ptmod.json`.
8. WHEN начинается новая игра THEN ModSave SHALL сбросить всех участников в значения по умолчанию до начала перемотки.
9. ModSave SHALL не добавлять поля в классы игры и не изменять `<имя>.txt`.
10. IF запись бокового файла не удалась THEN ModSave SHALL оставить `<имя>.txt` нетронутым, показать уведомление «Состояние модов не сохранено» и записать ошибку в лог.
11. ModSave SHALL сам записывать в каждую секцию поле `_schema = participant.SchemaVersion` и сам читать его при загрузке, передавая значение в `Read(section, schema)`. Фичи SHALL не хранить собственное поле версии секции. IF `Write()` фичи вернул ключ `_schema` THEN ModSave SHALL перезаписать его и записать предупреждение.
12. IF в секции нет поля `_schema` THEN ModSave SHALL читать её как версию 1 и записать предупреждение один раз за сессию; при следующей записи поле SHALL появиться.

**Сценарии:**
- **AC-5.1.** Given игра с включённой `framework-sample`; When игрок сохраняет с пустым именем 1985-03-01; Then рядом с `1985_3_1.txt` лежит `1985_3_1.ptmod.json` с `"schema": 1` и секцией `framework-sample`.
- **AC-5.2.** Given ванильный сейв без бокового файла; When игрок его загружает; Then игра загружается, в «О моде» — «Нет файла модов (старый сейв)», фичи работают со значениями по умолчанию.
- **AC-5.3.** Given боковой файл с секцией `gpu-product-line`, а фича удалена из сборки; When игрок загружает и пересохраняет сейв; Then секция `gpu-product-line` в новом файле совпадает с исходной.
- **AC-5.4.** Given боковой файл обрезан на середине; When игрок загружает сейв; Then игра загружается, появляется уведомление, рядом лежит `*.corrupt-*`.
- **AC-5.5.** Given секция `event-system` в файле без поля `_schema` (ручная правка); When игрок загружает сейв; Then `Read` вызван с `schema = 1`, в логе одно предупреждение, а после пересохранения в секции есть `"_schema": 1`.
- **AC-5.6.** Given участник с `SchemaVersion = 2` и секция с `"_schema": 3`; When загрузка и пересохранение; Then участник получил значения по умолчанию, статус `Newer`, секция в новом файле совпадает с исходной (включая `"_schema": 3`).

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-10 (ModJson), FR-2
- **Assumptions**: `SaveSystem.Save` — единственная точка записи сейва. Проверено grep: вызывается только из `SaveHandler.Save`, а `SaveHandler.Autosave` вызывает `SaveHandler.Save("autosave")`.

### FR-6: Глобальные настройки и профиль (ModSettings, ModProfile)

**User Story:** Как игрок, я хочу, чтобы выбор включённых фич и ручек баланса сохранялся между запусками. Как автор фичи, я хочу хранить глобальные данные (например, достижения) вне сейвов.

#### Acceptance Criteria

1. WHEN мод стартует THEN ModSettings SHALL прочитать `persistentDataPath/ptmod_settings.json`. IF файла нет THEN ModSettings SHALL создать его со значениями по умолчанию.
2. WHEN фича объявляет параметр (`ParamSpec`: тип, по умолчанию, min, max, шаг) THEN ModSettings SHALL хранить значение в пределах [min, max]. IF значение в файле вне диапазона или неверного типа THEN ModSettings SHALL использовать значение по умолчанию и записать предупреждение.
3. WHEN игрок меняет настройку в хабе THEN ModSettings SHALL атомарно сохранить файл и поднять `ModSettings.Changed(featureId, key)`.
4. IF файл настроек повреждён THEN ModSettings SHALL переименовать его в `*.corrupt-<timestamp>` и создать новый со значениями по умолчанию.
5. ModProfile SHALL хранить глобальные данные фич (достижения, «видел обучение» и т. п.) в `persistentDataPath/ptmod_profile.json` по секциям фич и SHALL быть единственным писателем этого файла: фичи SHALL не открывать файл сами. Секции неизвестных или выключенных фич и секции с `_schema` новее поддерживаемой SHALL сохраняться без изменений (правило ModSave); версию секции (`_schema`) пишет и читает ядро (FR-5 п. 11–12).
6. WHEN фича вызывает `ModProfile.MarkDirty(featureId)` THEN ModProfile SHALL записать файл не раньше чем через `profile.saveThrottleSeconds` (2 с реального времени) и не чаще одного раза за это окно. `ModProfile.Save()` SHALL записывать немедленно. При выходе в главное меню, закрытии игры (`OnApplicationQuit`) и сворачивании приложения несохранённые изменения SHALL записываться принудительно.
7. WHEN ModProfile записывает файл THEN он SHALL делать это атомарно (временный файл → `File.Replace`) и сохранять предыдущую версию как `ptmod_profile.json.bak`.
8. IF `ptmod_profile.json` повреждён или нечитаем THEN ModProfile SHALL переименовать его в `*.corrupt-<timestamp>` и восстановить данные из `.bak` (статус `RestoredFromBackup`). IF `.bak` тоже недоступен THEN ModProfile SHALL создать пустой профиль (статус `Corrupt`) и показать одно уведомление.
9. ModProfile SHALL не зависеть от сейва: загрузка и новая игра профиль не сбрасывают.

**Сценарии:**
- **AC-6.1.** Given `ptmod_settings.json` содержит `"enabled": "yes"` для фичи; When мод стартует; Then используется значение по умолчанию, в логе предупреждение с путём `features.<id>.enabled`.
- **AC-6.2.** Given фича вызывает `ModProfile.MarkDirty` 10 раз в течение 1 секунды реального времени; When проходит 2 секунды; Then выполнена ровно 1 запись файла, а при `ModProfile.Save()` запись выполняется немедленно (юнит-тест на фейковых часах и фейковой ФС).
- **AC-6.3.** Given `ptmod_profile.json` обрезан, а `ptmod_profile.json.bak` валиден; When мод стартует; Then данные восстановлены из `.bak`, повреждённый файл переименован в `*.corrupt-*`, статус `RestoredFromBackup`.
- **AC-6.4.** Given профиль содержит секцию `scenarios-achievements` и секцию неизвестной фичи; When первая фича меняет свою секцию и профиль записывается; Then секция неизвестной фичи в файле совпадает с исходной.

#### Additional Details
- **Priority**: Must (ModSettings и ModProfile: ими пользуются `first-year-onboarding` и `scenarios-achievements`)
- **Complexity**: Low
- **Dependencies**: FR-10
- **Assumptions**: `persistentDataPath` доступен на запись (там уже лежат `Saves/` и `GameSettings/`).

### FR-7: Лог (ModLog)

**User Story:** Как владелец, я хочу видеть в `Player.log` понятные сообщения мода без спама, чтобы присылать лог при ошибке.

#### Acceptance Criteria

1. ModLog SHALL писать через `Debug.Log`/`LogWarning`/`LogError` в формате `[PTMod] [<feature-id>] <LEVEL> <сообщение>`.
2. IF одно и то же сообщение (фича + текст без чисел) повторяется больше `log.repeatLimit` (3) раз за `log.repeatWindowSeconds` (60 с реального времени) THEN ModLog SHALL подавить повторы и затем записать одну сводку «повторено N раз».
3. WHERE в `ModSettings` включён подробный лог ModLog SHALL писать отладочные сообщения, иначе SHALL их отбрасывать.
4. ModLog SHALL хранить последние 50 ошибок и предупреждений в памяти для вкладки «Диагностика».

**Сценарии:**
- **AC-7.1.** Given одно и то же предупреждение фичи пишется каждый кадр 10 секунд; When проходит 60 секунд реального времени; Then в `Player.log` 3 записи этого предупреждения и одна сводка «повторено N раз».
- **AC-7.2.** Given `verboseLog: false`; When фича вызывает `ModLog.Debug`; Then в логе ничего не появляется.

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: —
- **Assumptions**: `Player.log` — штатный лог Unity-плеера.

### FR-8: Локализация (ModL10n)

**User Story:** Как игрок русского форка, я хочу видеть все тексты модов по-русски, а англоязычный игрок — по-английски.

#### Acceptance Criteria

1. WHEN строка запрашивается через `L(key, args)` THEN ModL10n SHALL вернуть текст на текущем языке (`ru` по умолчанию) с подстановкой `{0}…{n}`.
2. IF ключа нет в текущем языке THEN ModL10n SHALL вернуть `en`. IF его нет и там THEN ModL10n SHALL вернуть `ru`. IF ключа нет нигде THEN ModL10n SHALL вернуть `[key]` и записать предупреждение один раз за сессию.
3. WHEN запрашивается форма с числом (`Plural`) THEN ModL10n SHALL выбирать форму one/few/many для `ru` и one/other для `en`.
4. WHERE текст задан в контенте объектом `{"ru": "...", "en": "..."}` ModL10n SHALL разрешать его по тем же правилам.
5. ModL10n SHALL загружать встроенные таблицы `L10n/ru.json`, `L10n/en.json`, `L10n/<lang>/*.json` и переопределения из `<папка игры>/PTMod/l10n/`.
6. WHEN выполняется самопроверка THEN ModL10n SHALL сообщить ключи, которые есть в одном языке и отсутствуют в другом, а также ключи, чей префикс не принадлежит фиче-владельцу (FR-25).
7. Язык мода SHALL выбираться собственной настройкой `ModSettings.Language` (по умолчанию `ru`) и SHALL не зависеть от выбора языка в настройках игры.

**Сценарии:**
- **AC-8.1.** Given ключ `core.mail.daysLeft` с формами `{0} день|{0} дня|{0} дней`; When `Plural("core.mail.daysLeft", 22)`; Then «22 дня».

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-10
- **Assumptions**: строки игры переводит русификатор, ModL10n — только строки мода.

### FR-9: Детерминированный ГСЧ (ModRandom)

**User Story:** Как автор событий, я хочу случайность, которая после загрузки сейва продолжается так же, как без неё, чтобы игрок не «перебрасывал» события перезагрузкой.

#### Acceptance Criteria

1. WHEN начинается новая игра THEN ModRandom SHALL выбрать мировой сид (из времени или заданный через `ModNewGame`) и сохранить его в `ModSave`.
2. WHEN фича запрашивает поток по ID THEN ModRandom SHALL вернуть независимый поток, сид которого детерминированно выводится из мирового сида и ID потока.
3. WHEN сейв сохраняется THEN ModRandom SHALL сохранить состояние каждого потока. WHEN сейв загружается THEN ModRandom SHALL восстановить это состояние так, чтобы последовательность после загрузки совпала с последовательностью без сохранения и загрузки.
4. ModRandom SHALL не трогать ГСЧ игры (`System.Random`, `UnityEngine.Random`).
5. IF в сейве нет состояния ГСЧ (старый сейв) THEN ModRandom SHALL выбрать новый мировой сид и записать это в лог.

**Сценарии:**
- **AC-9.1.** Given поток `event-system` после 1 000 вызовов; When состояние сериализовано и восстановлено в новом экземпляре; Then следующие 1 000 значений совпадают (юнит-тест).

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-5
- **Assumptions**: —

### FR-10: JSON-контент и парсер (ModContent, ModJson, решение 0006)

**User Story:** Как автор контента или игрок-моддер, я хочу менять числа и тексты событий, сценариев и платформ в JSON без перекомпиляции, и чтобы одна битая запись не ломала остальные.

#### Acceptance Criteria

1. ModJson SHALL разбирать и записывать JSON (объекты, массивы, строки, числа, bool, null), поддерживать словари, сохранять порядок и неизвестные поля.
2. IF JSON синтаксически неверен THEN ModJson SHALL вернуть ошибку со строкой и столбцом, не выбрасывая исключение наружу.
3. WHEN фича загружает файл контента THEN ModContent SHALL взять встроенный ресурс `content/<feature>/<file>.json` и наложить переопределение из `<папка игры>/PTMod/content/<feature>/<file>.json` по полю `id`: запись с тем же `id` заменяет встроенную, новая добавляется, `"disabled": true` удаляет.
4. IF запись не проходит валидацию фичи THEN ModContent SHALL пропустить её и записать ошибку (файл, `id`, поле) в лог и в отчёт самопроверки, а остальные записи SHALL загрузиться.
5. IF файл переопределения имеет неизвестную схему (`schema`) или не разбирается THEN ModContent SHALL проигнорировать его целиком и использовать встроенный файл.
6. ModJson SHALL отклонять входы больше 8 МБ и вложенность глубже 64.

**Сценарии:**
- **AC-10.1.** Given переопределение `PTMod/content/framework-sample/samples.json` меняет `weight` записи `s1` и отключает `s2`; When мод стартует; Then в отчёте `ModContent`: заменено 1, удалено 1, ошибок 0.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: —
- **Assumptions**: `JsonUtility` не подходит: нет словарей, полиморфизма и сохранения неизвестных полей (решение 0006).

### FR-11: Библиотека условий (ModConditions)

**User Story:** Как автор обучения, событий или сценариев, я хочу описывать условия в JSON («год ≥ 1980 и есть контракт OEM») одними и теми же предикатами, чтобы не дублировать чтение состояния игры.

#### Acceptance Criteria

1. ModConditions SHALL предоставлять предикаты из таблицы в design.md, каждый с точным источником данных игры.
2. WHEN условие задано в JSON THEN ModConditions SHALL разобрать дерево `all`/`any`/`not` и предикаты. IF тип или параметр неизвестен THEN ModConditions SHALL вернуть ошибку разбора с путём (`conditions[1].params.year`).
3. WHEN условие вычисляется THEN ModConditions SHALL вернуть bool без исключений. IF источник данных недоступен (синглтон игры = null) THEN ModConditions SHALL вернуть false и записать предупреждение один раз.
4. WHEN фича регистрирует свой предикат THEN ModConditions SHALL сделать его доступным в JSON по имени. IF имя занято THEN ModConditions SHALL отклонить регистрацию.
5. ModConditions SHALL кешировать дорогие источники (списки CPU, контрактов, компаний ИИ) в пределах одного игрового дня.
6. IF фича (не `mod-framework`) регистрирует предикат с именем без своего префикса (`<prefix>.<name>`, таблица в design.md «Соглашение об идентификаторах») THEN ModConditions SHALL отклонить регистрацию с ошибкой. Имена предикатов ядра SHALL не содержать точки — так предикат ядра не может совпасть с предикатом фичи.

**Сценарии:**
- **AC-11.1.** Given `{"all":[{"type":"dateAtLeast","year":1980},{"type":"oemContractsActiveAtLeast","count":1}]}`; When 1980-02-01 и у игрока есть активный контракт OEM; Then true; When контракта нет; Then false.
- **AC-11.2.** Given фича `event-system` регистрирует предикаты `hasFab` и `es.hasFab`; When `ModConditions.Register`; Then первый отклонён («имя без префикса фичи `es.`»), второй принят и доступен в JSON как `{"type":"es.hasFab"}`.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-10, FR-13, FR-25, `ModCompanies`
- **Assumptions**: условия оцениваются с точки зрения игрока, если не указано иное (`company`).

### FR-12: Модификаторы и стандартные каналы (ModModifiers)

**User Story:** Как автор фичи, я хочу умножать формулы игры (скорость исследований, спрос, рейтинг, брак, стоимость, кредит) через именованные каналы, чтобы несколько фич могли влиять на одну формулу без конфликтующих патчей.

#### Acceptance Criteria

1. ModModifiers SHALL обслуживать стандартные каналы `research.speed`, `research.cost`, `production.unitCost`, `production.output`, `factory.operatingCost`, `market.demand.<segment>` (`highend|midrange|lowend|industries`), `market.rating.cpu`, `popularity.gain`, `cpu.failureRate`, `contract.reward`, `contract.signChance`, `bank.interest`, `bank.creditLimit`, `taxes`, а также новые каналы фреймворка `construction.cost` и `development.cost` (утверждены владельцем) и канал на проект CPU `development.cost.design` (п. 12). Всего в реестре ядра 27 записей (п. 15).
2. WHEN игра вычисляет формулу канала THEN ModModifiers SHALL умножить игровое значение на произведение всех применимых множителей (записей и провайдеров). Произведение SHALL ограничиваться сначала балансовым диапазоном канала (если он задан, п. 18), затем предохранителем [min, max] канала из контента.
3. IF у канала нет активных множителей THEN ModModifiers SHALL вернуть игровое значение без изменений и без выделений памяти.
4. WHERE запись имеет область `Player` ModModifiers SHALL применять её только к компании игрока. Компании ИИ SHALL получать множитель только при областях `AllCompanies`, `AIOnly` или `Company(<ключ ИИ>)`.
5. WHEN `Modifiers.GetModifier` вызывается с `company = null` (превью в меню) THEN ModModifiers SHALL не менять результат.
6. WHEN наступает дата окончания временной записи THEN ModModifiers SHALL перестать её применять с этого дня и удалить её в `DayPassed`.
7. WHEN сейв сохраняется или загружается THEN ModModifiers SHALL сохранять и восстанавливать постоянные записи. Провайдеры (функции в коде) фича SHALL регистрировать заново при инициализации.
8. WHILE фича — владелец записи выключена ModModifiers SHALL не применять запись, но SHALL сохранять её.
9. WHEN запрашивается `Explain(channel, context)` THEN ModModifiers SHALL вернуть разбивку: список применённых множителей с источником (ID фичи), причиной (ключ ModL10n и аргументы — у записи из её `reasonKey`, у провайдера из его `reasonKey` или результата `ProviderResult`) и сроком, а также произведение до ограничений (`Raw`), итог (`Total`) и признаки `IsBalanceCapped`/`IsFuseCapped`. Интерфейс SHALL дописывать «(ограничено)», когда сработал балансовый диапазон. Разбивка нужна подсказкам (`research-tree-ux`, `contract-transparency`).
10. ModModifiers SHALL быть единственным патчем на формулы, которые читают стандартные каналы (FR-2 п. 6). Новый канал на уже пропатченную формулу SHALL добавляться изменением этой спеки. Канал на новую формулу (например, рынок GPU) читает патч или код фичи-владельца, но имя и границы канала SHALL быть объявлены в реестре ядра (п. 15).
11. WHEN значение канала применяется к формуле THEN результат SHALL соответствовать формулам раздела «Формулы и баланс» design.md (место множителя в формуле игры).
12. WHEN вызывается `CpuMath.ProjectCost` THEN ядро (патч P19) SHALL умножить результат на `M(development.cost.design, {Company, Design})`, где `Design` (`DesignContext`) содержит пакет, техпроцесс, память и частоту проектируемого CPU. Значения этого канала SHALL не кешироваться (меняются при каждом движении ползунка конструктора). Фичи SHALL не патчить `CpuMath.ProjectCost`: per-design множители (архитектуры, платформы) регистрируются провайдерами этого канала.
13. WHEN `Contract.CpuOffer.DefineChance` вычисляет шанс THEN `ModifierContext.Contract` SHALL содержать контракт, для которого строится список предложений (захват в prefix `Contract.GetBestOffers`, патч P23). Для `contract.signChance` ID контракта SHALL входить в ключ дневного кеша. Вне P8 `ModifierContext.Contract` SHALL быть `null`. `Contract.ChanceToSign` (интерфейс) и `Contract.ChooseCpu` (розыгрыш) SHALL получать один и тот же контекст.
14. WHERE фича регистрирует провайдер THEN она SHALL иметь возможность вернуть причину множителя (ключ ModL10n и аргументы) для `Explain`: фиксированным `reasonKey` или результатом `ProviderResult` на каждый вызов. Провайдер без причины SHALL отображаться в разбивке без причины.
15. WHEN мод стартует THEN ядро SHALL объявить в реестре каналов (из `core.json`) все каналы, которые читают патчи ядра и фич, до вызова `Register` любой фичи: 19 стандартных (включая 4 канала `market.demand.*`), `development.cost.design`, `production.memoryCost`, `production.waferCost` (владелец — `market-shocks`), `gpu.demand`, семейство `gpu.demand.*`, `gpu.rating`, `gpu.vramCost` (владелец — `gpu-product-line`), `acquisition.price` (владелец — `rival-acquisitions`). Фичи могут добавлять каналы через `DeclareChannel` только для имён, которых нет в реестре.
16. IF фича регистрирует запись или провайдер на необъявленный канал THEN ModModifiers SHALL принять их и связать с каналом по имени лениво (порядок `Register` фич не важен); до объявления канал считается с предохранителем по умолчанию [0,1; 10]. ModSelfCheck SHALL после `Register` всех фич предупредить о каналах с записями или провайдерами, но без объявления.
17. WHEN состояние, от которого зависит провайдер, меняется внутри игрового дня (смена архитектуры, выпуск заплатки) THEN фича SHALL вызвать `ModModifiers.Invalidate(channel)`, и ModModifiers SHALL сбросить дневной кеш этого канала (всех каналов при `null`).
18. Балансовый диапазон каналов `market.rating.cpu` и `popularity.gain` SHALL быть [0,5; 2,0] на CPU (отдельно для каждой комбинации компания × CPU × сегмент) и применяться в ядре. Explain SHALL показывать «(ограничено)». Каждая спека-поставщик SHALL указывать свой максимальный вклад в эти каналы; суммарный предел обеспечивает ядро.
19. `bank.creditLimit` по компании SHALL применяться только к порогу дефолта в `BankruptcyHandler.TickBankruptcies` и к отображению кредита игрока; пороги ИИ в `AIBehaviour*` и лимиты кредитов `bankruptcy-recovery` SHALL не умножаться на него (без двойного применения). `production.unitCost` SHALL влиять только на фактические траты производства, а не на показанную себестоимость.

**Сценарии:**
- **AC-12.1.** Given запись `research.speed ×1.5` с областью `Player`; When игрок исследует технологию с финансированием F; Then `ResearchMath.ResearchSpeed(F, player)` = `sqrt(F) × GetModifier(0) × 1.5`, а для ИИ — без ×1.5.
- **AC-12.2.** Given записи `market.rating.cpu ×1.2` (фича A) и `×0.5` (фича B) на один CPU; When идёт продажа; Then `Overall` этого CPU после `Market.CalculateRatings` умножен на 0,6.
- **AC-12.3.** Given запись `cpu.failureRate ×3` на CPU с `FailureRate` 0,4; When `Factory.ProduceWafers`; Then брак считается по 0,95 (предел), а `cpu.FailureRate` после вызова снова 0,4.
- **AC-12.4.** Given временная запись `market.demand.lowend ×0.7` на 90 дней с 1990-01-01; When наступает 1990-04-01; Then множитель больше не применяется, запись удалена.
- **AC-12.5.** Given меню новой игры; When показываются превью модификаторов (`Modifiers.GetModifier(0, null)`); Then значения ванильные при любых записях.
- **AC-12.6.** Given на `market.rating.cpu` для одного CPU провайдеры ×1,8, ×1,6 и ×1,3 (произведение 3,744); When `Get` и `Explain`; Then `Get` = 2,0 (балансовый диапазон), `Explain.Raw` = 3,744, `Total` = 2,0, `IsBalanceCapped` = true, и подсказка показывает «(ограничено)». Given произведение 0,3; Then `Get` = 0,5.
- **AC-12.7.** Given два OEM-контракта делят один CPU 1 января и фича `platform-generations` регистрирует провайдер `contract.signChance`, читающий `ctx.Contract.ContractID`; When считаются шансы обоих контрактов; Then провайдер видит каждый контракт отдельно, значения в дневном кеше не смешиваются, а `Contract.ChanceToSign` в окне контракта совпадает с шансом розыгрыша.
- **AC-12.8.** Given провайдер `development.cost.design`, возвращающий ×1,15 для проектов с определённым `ctx.Design.Node` (незрелый техпроцесс); When игрок двигает ползунок в конструкторе и когда ИИ считает проект в `AIBehaviourCreation`; Then `CpuMath.ProjectCost` умножен на множитель в обоих случаях, ни одно значение не взято из кеша, а множитель `development.cost` (тип 3) применён отдельно один раз.
- **AC-12.9.** Given провайдер зарегистрирован с `reasonKey = "press.mod.hype"`, а запись фичи — с `reasonKey = "ms.event.recession"` и аргументом «рецессия»; When `Explain("market.demand.lowend", ctx)`; Then оба множителя в списке с источником и причиной, у провайдера причина «Ажиотаж» (из L10n).
- **AC-12.10.** Given `rival-personalities` регистрирует провайдер `acquisition.price` раньше, чем `rival-acquisitions` вызвал `Register`; When мод стартует; Then канал уже объявлен ядром, провайдер связан, самопроверка не предупреждает. Given провайдер на канал `typo.channel`; Then самопроверка предупреждает «канал без объявления».
- **AC-12.11.** Given провайдер зависит от выбранной архитектуры; When игрок меняет архитектуру в середине дня и фича вызывает `ModModifiers.Invalidate("market.rating.cpu")`; Then следующий `Get` возвращает новое значение, не дожидаясь смены дня.

#### Additional Details
- **Priority**: Must
- **Complexity**: High
- **Dependencies**: FR-2, FR-5, FR-10, FR-25, `ModCompanies`
- **Assumptions**: имена сегментов в `PopulationData.Name` переводятся русификатором (`High End` → «Высокий»), поэтому сегмент определяется по индексу в `Market`, а не по имени.

### FR-13: Репутация бренда (ModReputation)

**User Story:** Как автор отзывов, обзоров и событий, я хочу общий счётчик репутации каждой компании, чтобы разные фичи влияли на одну величину, а прессы её показывали.

#### Acceptance Criteria

1. ModReputation SHALL хранить репутацию каждой компании по стабильному ключу (`player`, `ai:<UniqueID>`) в диапазоне 0..100, по умолчанию 50.
2. WHEN вызывается `Add(company, delta, reason)` THEN ModReputation SHALL изменить значение, ограничить его [0, 100], добавить запись истории (дата, delta, итог, reason) и поднять `Changed`.
3. IF `company` = null, ключ не разрешается или `delta` не конечное число THEN `Add` SHALL ничего не менять и записать предупреждение.
4. WHEN появляется новая компания ИИ THEN `Get` SHALL возвращать 50 до первого изменения.
5. WHEN сейв сохраняется или загружается THEN ModReputation SHALL сохранять и восстанавливать значения и последние `reputation.historyPerCompany` (50) записей истории на компанию.
6. ModReputation SHALL не влиять на формулы игры сам по себе: игровой эффект и отображение задаёт `press-reviews`.
7. WHEN вызывается `Drift(company, rate, target)` THEN ModReputation SHALL сдвинуть значение к `target` на долю `rate` разницы (`v += (target − v) × rate`, при отклонении меньше 0,05 — установить `target`) без записи в историю и без события `Changed`.
8. ModReputation SHALL сам выполнять ежемесячный дрейф всех компаний, кроме банкротов, к 50 на `reputation.driftMonthlyRate` (0,03) в фазе `LateMonthPassed`, не во время перемотки. После дрейфа ModReputation SHALL поднять `Drifted` один раз и сбросить кеш каналов (`ModModifiers.Invalidate()`). Фичи (`press-reviews`) SHALL не дрейфовать репутацию сами.
9. История SHALL хранить только изменения `Add` и `Set`, поэтому дрейф не вытесняет значимые записи.

**Сценарии:**
- **AC-13.1.** Given репутация игрока 95; When `Add(player, +10, "review")`; Then значение 100, в истории delta +10 и итог 100.
- **AC-13.2.** Given репутация компании 80 и `driftMonthlyRate` 0,03; When наступает 1-е число; Then значение 79,1, число записей истории не изменилось, `Changed` не поднят, `Drifted` поднят один раз.
- **AC-13.3.** Given 48 месяцев дрейфа 14 компаний и 6 записей `Add` у игрока; When читается `History(player)`; Then все 6 записей на месте.

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-4, FR-5, `ModCompanies`
- **Assumptions**: `AICompany.UniqueID` стабилен между сейвами: это индекс в `CompanySpawner.historicalCompanies`, на нём держится `CompanySpawner.SpawnCompanyFromSave`.

### FR-14: Письма с выбором, действиями и приоритетами (ModMail)

**User Story:** Как автор обучения и событий, я хочу отправлять игроку письма во внутриигровую почту: с одноразовым выбором и сроком ответа, с многоразовыми кнопками-действиями («Подсказки», «Открыть журнал»), с неактивными вариантами и приоритетом — чтобы не строить для каждого события своё окно, а срочные письма не терялись в очереди из-за дневного лимита.

#### Acceptance Criteria

1. WHEN фича вызывает `ModMail.Send` THEN ModMail SHALL поставить письмо в очередь доставки и вернуть ID письма. ID ≥ 100000 выдаёт ядро из своего счётчика; фича ID не выбирает, блоков ID нет. Письма одной фичи различаются полем `Tag` (`<prefix>:<…>`, FR-25), поиск — `Find`/`FindAll`.
2. WHEN письмо доставляется THEN ModMail SHALL создать его через `Email.Initialize` и `EmailWindow.LoadEmail` с датой дня доставки, записать `DeliveredOn` и поднять `Delivered(record)`. Срок ответа (`TimeoutDays`) SHALL считаться от даты доставки, а не отправки.
3. WHERE в письме есть варианты выбора (`Choices`) ModMail SHALL показывать под текстом письма в `EmailWindow` кнопки выбора, пока выбор не сделан. WHEN игрок нажимает вариант THEN ModMail SHALL вызвать обработчик фичи-отправителя ровно один раз, сохранить выбор и заменить кнопки строкой «Ваш ответ: …».
4. WHERE в письме есть действия (`Actions`, `ModMailAction`) ModMail SHALL показывать их кнопками. WHEN игрок нажимает действие THEN ModMail SHALL вызвать обработчик действий фичи-отправителя и SHALL не закрывать письмо. Действие с `Repeatable = true` остаётся активным, с `Repeatable = false` становится неактивным после первого нажатия. Выбор письма и действия SHALL не влиять друг на друга.
5. IF у варианта или действия `Enabled = false` THEN кнопка SHALL быть неактивна, а причина (`DisabledReasonKey` с аргументами) SHALL показываться подсказкой и строкой под кнопкой. `ModMail.SetChoiceState`, `SetActionState` и `Refresh(mailId)` SHALL перерисовывать открытое письмо; состояние кнопок SHALL сохраняться в `ModSave`.
6. WHERE фича ведёт собственный таймер (например, `event-system`) THEN она SHALL выводить его строкой `ModMail.SetStatusLine(mailId, text)`; строка SHALL сохраняться в `ModSave` и показываться под текстом письма.
7. IF у письма есть срок ответа (`TimeoutDays` > 0) и он истёк THEN ModMail SHALL применить вариант по умолчанию и пометить ответ «по истечении срока».
8. ModMail SHALL поддерживать приоритеты `Critical`, `Normal`, `Low`. Порядок доставки: `Critical`, затем `Normal`, затем `Low`; внутри приоритета — порядок отправки. За игровой день SHALL доставляться не больше `mail.maxDeliveredPerDay` (3) писем `Normal` и `Low` и не больше `mail.maxPerFeaturePerDay` (2) таких писем от одной фичи; остальные ждут следующих дней. Письма `Critical` SHALL идти в обход обоих лимитов, но не раньше `GameReady` и не во время пакетного прогона.
9. IF очередь превышает `mail.queueLimit` (200) THEN ModMail SHALL отбросить самые старые письма `Low`, затем `Normal` (письма `Critical` — никогда) и записать это в лог с ID фичи.
10. WHEN игра сохраняется THEN письма мода SHALL не попадать в ванильный сейв (`SaveObject.Emails`) и SHALL сохраняться в `ModSave` вместе с очередью, выбором, состоянием кнопок и статусной строкой.
11. WHEN сейв загружается THEN ModMail SHALL восстановить письма мода с исходными датами, признаком прочтения, выбором и статусом, после чего SHALL отсортировать список почты по дате (новые сверху, при равных датах выше письмо с большим ID).
12. IF фича-отправитель выключена или недоступна THEN кнопки SHALL быть неактивны с пояснением, а письмо SHALL остаться в почте.
13. WHILE идёт перемотка, загрузка или пакетный прогон месяца (`ModTicks.IsBatchRun`) ModMail SHALL ставить письма в очередь и доставлять их после `GameReady` / `BatchEnded`.
14. ModMail SHALL хранить текст как ключ ModL10n и аргументы и перерисовывать его на текущем языке при восстановлении.

**Сценарии:**
- **AC-14.1.** Given `framework-sample` отправила письмо с вариантами «Да»/«Нет» и сроком 10 дней; When игрок открывает письмо и жмёт «Да»; Then обработчик вызван 1 раз, кнопки исчезли, видно «Ваш ответ: Да».
- **AC-14.2.** Given то же письмо без ответа; When проходит 10 игровых дней; Then применён вариант по умолчанию, в письме «Ответ по истечении срока: Нет».
- **AC-14.3.** Given в почте 2 письма мода; When игрок сохраняет; Then в `<имя>.txt` нет записей `EmailID` ≥ 100000, а в `<имя>.ptmod.json` есть 2 записи.
- **AC-14.4.** Given письмо с действиями «Подсказки» (`Repeatable`) и «Пропустить шаг» (`Repeatable = false`); When игрок трижды жмёт «Подсказки» и один раз «Пропустить шаг»; Then обработчик действия вызван 3 + 1 раза, письмо не закрыто, «Подсказки» активна, «Пропустить шаг» неактивна.
- **AC-14.5.** Given вариант «Купить» с `Enabled = false` и `DisabledReasonKey = "es.reason.noMoney"`; When игрок открывает письмо и наводит курсор на кнопку; Then кнопка неактивна, под ней причина «Не хватает денег»; When фича вызывает `SetChoiceState(mailId, "buy", true)` и `Refresh`; Then кнопка активна без закрытия письма.
- **AC-14.6.** Given за день отправлено 5 писем `Normal` от разных фич и 1 `Critical`; When наступает доставка; Then доставлено `Critical` и 3 `Normal`, остальные 2 `Normal` — на следующий день в порядке отправки.
- **AC-14.7.** Given одна фича отправила 4 письма `Normal`; When наступает доставка; Then в этот день доставлено 2 (квота на фичу), 2 — на следующий; письма других фич квотой не затронуты.
- **AC-14.8.** Given письмо `Normal` с `TimeoutDays = 10` отправлено 1 марта, а доставлено 4 марта из-за лимита; When читается запись; Then `Deadline` = 14 марта, `Delivered` поднят 4 марта.
- **AC-14.9.** Given фича вызвала `SetStatusLine(mailId, "Осталось 3 дня")`; When игрок сохраняет и загружает сейв; Then строка на месте.
- **AC-14.10.** Given в сейве письма мода с датами 1980, 1985 и ванильные письма «итоги года»; When сейв загружен; Then в окне почты письма стоят по дате, без «слипания» писем мода в начале списка.

#### Additional Details
- **Priority**: Must
- **Complexity**: High
- **Dependencies**: FR-4 (пакетный прогон), FR-5, FR-8, FR-16, FR-25
- **Assumptions**: `EmailWindow.GetEmails` вызывается только из `SaveHandler.Save` (проверено grep), поэтому фильтр в нём не влияет на интерфейс почты. `EmailWindow.InstantiateButton` ставит кнопку письма первой (`SetAsFirstSibling`), поэтому порядок после загрузки задаёт сортировка по `EmailButton.Email` и `Email.Date`.

### FR-15: Уведомления (ModNotify)

**User Story:** Как автор фичи, я хочу показывать короткие всплывающие уведомления в стиле игры без риска показать их во время перемотки.

#### Acceptance Criteria

1. WHEN фича вызывает `ModNotify.Show` THEN ModNotify SHALL показать уведомление через `PopupManager.InstantiateGenericNotification`.
2. WHILE идёт перемотка, загрузка или `PopupManager.Instance` = null ModNotify SHALL ставить уведомления в очередь (не больше `notify.queueLimit` = 20) и показывать их после `GameReady`.
3. IF фича превысила `notify.maxPerFeaturePerDay` (5) уведомлений за игровой день THEN ModNotify SHALL отбрасывать лишние и записывать это в лог.
4. WHILE идёт пакетный прогон месяца (`ModTicks.IsBatchRun`) ModNotify SHALL ставить уведомления в очередь и показывать после `BatchEnded`, применяя те же лимиты (лимит дня считается по дате показа).

**Сценарии:**
- **AC-15.1.** Given новая игра со стартом в 2000 году; When фича вызывает `ModNotify.Show` из обработчика с `RunDuringFastForward`; Then уведомление не появляется во время перемотки и показывается после `GameReady(NewGame)`.
- **AC-15.2.** Given фича вызывает `ModNotify.Show` 8 раз за один игровой день; When день проходит; Then показано 5 уведомлений, в логе одна запись об отброшенных 3.
- **AC-15.3.** Given игрок нажимает «Следующий месяц» и фича вызывает `ModNotify.Show` в каждом `DayPassed`; When идёт прогон; Then ни одно уведомление не показано до `BatchEnded`, затем показаны не больше 5 от фичи.

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-4
- **Assumptions**: `PopupManager.InstantiateGenericNotification` не проверяет перемотку сам (CODEMAP, Gotchas).

### FR-16: Рантайм-UI из шаблонов игры (ModUI, решение 0005)

**User Story:** Как автор фичи, я хочу строить окна, которые выглядят как родные (тема, шрифты с кириллицей, кнопки), не имея своих ассетов.

#### Acceptance Criteria

1. WHEN загружается сцена (`level0` или `level1`) THEN ModUI SHALL найти шаблоны из `ModUI.Templates`. IF шаблон не найден THEN ModUI SHALL отметить это в самопроверке, а окна на его основе SHALL быть недоступны без исключений.
2. WHEN фича создаёт окно THEN ModUI SHALL клонировать шаблон под неактивным родителем, удалить компоненты не из белого списка (включая скрипты-синглтоны окна), отключить persistent-слушатели кнопок и заново привязать закрытие.
3. WHERE окно модальное ModUI SHALL оставить на нём `PauseTrigger`, чтобы время стояло, пока окно открыто.
4. WHEN игрок меняет тему THEN элементы окон мода SHALL перекрашиваться: через `Themeable*`-компоненты клонов и через подписку на `ThemeManager.OnThemeChanged`, которую ModUI ставит заново при каждой смене сцены.
5. ModUI SHALL давать виджеты: кнопка (обычная и CTA), переключатель, слайдер, выпадающий список, `InfoText`, текст, вертикальный список с прокруткой, подсказка.
6. ModUI SHALL не уничтожать окна мода в пределах сцены, только скрывать: `PauseHandler` не умеет удалять триггеры.
7. ModUI SHALL не изменять `data.unity3d` и не загружать сторонние ассеты.
8. ModUI SHALL давать модальное подтверждение `ModUI.Confirm(title, message, onYes, onNo)` на шаблоне `Window.Modal` с кнопками «Да»/«Нет» (ключи `core.common.yes`/`no`); пока окно открыто, время SHALL стоять. Подтверждение используют хаб (выключение фичи), `contract-transparency` и `scenarios-achievements`.
9. ModUI SHALL давать общий мост к подсказкам игры: `ModUI.Tooltip` (статическая и динамическая подсказка на элементе), `ModUI.ShowTooltip` и `ModUI.HideTooltip`. Мост SHALL создавать runtime-`TooltipData` один раз на элемент, вызывать `TooltipHandler.StartTrigger` / `TooltipHandler.CancelTrigger` с родной задержкой и видом и SHALL не бросать исключение при `TooltipHandler.Instance` = null. Фичи SHALL не писать собственные мосты к `TooltipHandler`.

**Сценарии:**
- **AC-16.1.** Given игровая сцена; When фича открывает модальное окно; Then время на паузе, окно в текущей теме, крестик закрывает окно и снимает паузу, `BreakContractWindow.Instance` не изменился.
- **AC-16.2.** Given открыто окно мода; When игрок переключает тему на тёмную; Then окно перекрашено без переоткрытия.
- **AC-16.3.** Given `ModUI.Confirm` вызван с `onYes` и `onNo`; When игрок жмёт «Да»; Then вызван только `onYes`, окно закрыто, время идёт; повторное нажатие невозможно.
- **AC-16.4.** Given динамическая подсказка на кнопке с провайдером текста; When курсор наведён, затем уведён; Then показана родная подсказка с задержкой игры, провайдер вызван при наведении, после ухода подсказка закрыта. В главном меню (`TooltipHandler.Instance` = null) подсказка не показывается, исключений нет.

#### Additional Details
- **Priority**: Must
- **Complexity**: High
- **Dependencies**: FR-8
- **Assumptions**: русификатор меняет тексты, но не имена GameObject. Имена шаблонов взяты из `pt-modding/data` (таблица в design.md).

### FR-17: Хаб «Моды»

**User Story:** Как игрок, я хочу одно окно, где видно, какие моды установлены, включены ли они и всё ли в порядке.

#### Acceptance Criteria

1. WHEN игровая сцена готова THEN ModUI SHALL показать кнопку «Моды» (клон `DoorButton`). WHEN игрок нажимает её THEN SHALL открыться модальное окно хаба.
2. Вкладка «Фичи» SHALL показывать для каждой фичи название, описание, состояние (`Enabled`/`Disabled`/`Faulted`/`Unavailable` с причиной), переключатель и кнопку «Настройки», если фича зарегистрировала страницу или параметры.
3. WHERE фича объявила `ParamSpec` хаб SHALL строить страницу настроек автоматически (слайдер для чисел, переключатель для bool).
4. Вкладка «Диагностика» SHALL показывать отчёт самопроверки, список патчей и их статус, последние 50 ошибок, время тиков по фазам, реестр каналов (число записей и провайдеров, признак «ограничено» для каналов с балансовым диапазоном) и очередь писем (всего / `Critical`).
5. Вкладка «О моде» SHALL показывать версию мода, версию игры, статус бокового файла текущего сейва (`Загружен`/`Нет файла`/`Повреждён`/`Новее мода`), пути к настройкам и папке переопределений.
6. WHILE хаб открыт время SHALL стоять.
7. IF шаблон кнопки на панели не найден THEN хаб SHALL открываться запасной кнопкой (клон `GameplaySettingsButton` в меню паузы), а в «Диагностике» SHALL быть предупреждение.
8. WHEN игрок выключает фичу, у которой есть подтверждение (FR-3 п. 8) THEN хаб SHALL показать `ModUI.Confirm` с текстом фичи и выключить её только после «Да».

**Сценарии:**
- **AC-17.1.** Given игровая сцена загружена; When игрок нажимает «Моды»; Then открывается окно хаба, время стоит, на вкладке «Фичи» — все фичи сборки с состояниями.
- **AC-17.2.** Given `framework-sample` объявила параметр `boost` (Float, 1,0…2,0, шаг 0,05); When игрок открывает её «Настройки» и ставит 1,5; Then в `ptmod_settings.json` записано `"boost": 1.5`, а `ModSettings.GetFloat` возвращает 1,5.
- **AC-17.3.** Given `framework-sample` вызвала `ConfirmBeforeDisable`; When игрок выключает её в хабе; Then открыт `ModUI.Confirm`, время на паузе; «Нет» — переключатель вернулся во «Включена», «Да» — фича выключена и `OnDisabled` вызван один раз.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-3, FR-6, FR-16, FR-19
- **Assumptions**: место кнопки — клон `DoorButton` на панели (решение владельца, см. «Решения по умолчанию»).

### FR-18: Слот, секции и предпросмотр в меню новой игры (ModNewGame)

**User Story:** Как автор обучения и сценариев, я хочу добавить свои опции и блоки в окно новой игры через общий слот, чтобы две фичи не ломали одно окно своими вставками и не дублировали перехват старта.

#### Acceptance Criteria

1. WHEN открывается окно новой игры (`MenuGameSetup`) и хотя бы одна активная фича зарегистрировала опцию (`RegisterOption`) или секцию (`RegisterSection`) THEN ModUI SHALL показать кнопку «Моды…», которая открывает одну общую панель. Опции и секции всех фич SHALL находиться на этой панели (решение владельца: не под переключателем «Читы»).
2. IF опций и секций нет THEN кнопка SHALL не показываться.
3. WHEN игрок нажимает «Старт» THEN ModNewGame SHALL до загрузки сцены и перемотки поднять `BeforeStart(PlayerInitialData)` с выбранными опциями и сохранить выбранные значения в секции `mod-framework` ModSave.
4. Фичи SHALL менять `PlayerInitialData` только в `BeforeStart` (объект замыкается `GameManager.StartNewGame` и передаётся в `Player.InitializeAsNewGame`). Патчи фич на `GameManager.StartNewGame`, `MenuGameSetup.InitializeUI` и `MenuGameSetup.UpdateInitialData` SHALL быть отклонены (FR-2 п. 6).
5. WHERE фича регистрирует секцию `RegisterSection(featureId, build, order)` THEN ModNewGame SHALL при построении панели вызвать `build` с контейнером секции (фича наполняет его виджетами `ModUI`: кнопки «Испытания…», блок предпросмотра и т. п.), упорядочив секции по `order`, затем по ID фичи. Исключение в `build` SHALL изолироваться, а секция показывать строку об ошибке.
6. WHEN меняются настройки окна (год старта, тип компании, сложность, читы, имя) THEN ModNewGame SHALL после `MenuGameSetup.UpdateInitialData` поднять `PreviewChanged`. Обработчики SHALL использовать его только для обновления своих текстов и SHALL не менять `PlayerInitialData` (`ModNewGame.Preview` — только чтение).
7. Содержимое опций и секций SHALL задаваться спеками-владельцами (`first-year-onboarding`, `scenarios-achievements`), а не фреймворком.

**Сценарии:**
- **AC-18.1.** Given `framework-sample` зарегистрировала опцию-переключатель; When игрок открывает окно новой игры, включает опцию в «Моды…» и жмёт «Старт»; Then `BeforeStart` получает `true` до начала перемотки, а в боковом файле первого сейва `newGameOptions` содержит эту опцию.
- **AC-18.2.** Given ни одна активная фича не зарегистрировала опций и секций; When открывается окно новой игры; Then кнопки «Моды…» нет.
- **AC-18.3.** Given `framework-sample` зарегистрировала секцию с текстом «Год старта: N»; When игрок меняет дату старта в окне; Then `PreviewChanged` поднят один раз за изменение, текст в секции обновлён, `PlayerInitialData` не изменён.
- **AC-18.4.** Given секции `scenarios-achievements` (order 10) и `first-year-onboarding` (order 20); When открыта панель; Then секции стоят в порядке 10, 20, а исключение в `build` первой не мешает второй.

#### Additional Details
- **Priority**: Must (на нём стоят `first-year-onboarding` и `scenarios-achievements`)
- **Complexity**: Medium
- **Dependencies**: FR-16, FR-5
- **Assumptions**: `PlayerInitialData` правится до `DateController.SimulateUntilYear`. Изменения `StartingFunds`/`FactorySize` во время перемотки игра перезаписывает (CODEMAP, Gotchas).

### FR-19: Самопроверка на старте

**User Story:** Как владелец, я хочу сразу после запуска понимать, всё ли у мода в порядке, особенно после обновления игры, не играя час до первой ошибки.

#### Acceptance Criteria

1. WHEN мод стартует THEN ModSelfCheck SHALL проверить: Harmony загружен (версия 2.3.x), версия игры (`GameVersion.Current` = `0.2.3`, читается рефлексией), все цели патчей разрешаются, зарезервированные цели не заняты фичами, контент валиден, ключи ModL10n полны и принадлежат своим фичам по префиксам (FR-25), настройки и профиль разобраны.
2. WHEN загружается главное меню и игровая сцена THEN ModSelfCheck SHALL проверить шаблоны UI этой сцены, синглтоны игры (`DateController.Instance`, `SaveHandler.Instance`, `EmailWindow.Instance`, `PopupManager.Instance`, `ThemeManager.Instance`, `Market.Instance`, `Player.Instance`) и приватные члены, на которые опирается ядро (`EmailWindow.emailButtonsParent`, `MenuGameSetup.playerInitialData`, `Contract.GetBestOffers`).
3. ModSelfCheck SHALL писать в лог сводку `[PTMod] self-check <этап>: OK` или `N errors, M warnings`.
4. IF есть ошибки THEN ModSelfCheck SHALL показать одно уведомление со ссылкой на «Моды → Диагностика».
5. IF версия игры отличается от `0.2.3` THEN мод SHALL продолжить работу, а фичи с ненайденными целями SHALL быть `Unavailable`.
6. ModSelfCheck SHALL выполняться не дольше 300 мс на этап.
7. WHEN `Register` всех фич выполнен THEN ModSelfCheck SHALL предупредить о каналах с записями или провайдерами без объявления в реестре (FR-12 п. 16) и сообщить о предикатах, отклонённых из-за отсутствия префикса фичи (FR-11 п. 6).

**Сценарии:**
- **AC-19.1.** Given чистая игра 0.2.3 с переводом и модами; When владелец запускает игру и начинает новую; Then в логе три строки `self-check Boot: OK`, `MainMenu: OK`, `GameScene: OK`.
- **AC-19.2.** Given включён флаг `debug.brokenPatch`; When игра стартует; Then показано одно уведомление со ссылкой на «Моды → Диагностика», где указана ненайденная цель.
- **AC-19.3.** Given у `framework-sample` включён флаг `debug.badIds` (провайдер на канал `typo.channel` и предикат `hasFab` без префикса); When мод стартует; Then в «Диагностике» предупреждение «канал без объявления `typo.channel`» и ошибка «предикат `hasFab` отклонён: нет префикса `smp.`»; остальные фичи работают.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-2, FR-8, FR-10, FR-16, FR-25
- **Assumptions**: `GameVersion.Current` — const, компилятор встроит литерал в `PTMod.dll`, поэтому читать его нужно рефлексией.

### FR-20: Установщик (`install.py --with-mods`)

**User Story:** Как игрок, я хочу установить и удалить моды одной командой, как русификатор, с автоматическим бэкапом.

#### Acceptance Criteria

1. WHEN запускается `install.py <папка> --with-mods` THEN установщик SHALL установить перевод (как сейчас), скопировать `PTMod.dll` и `0Harmony.dll` в `Managed/`, добавить записи в `ScriptingAssemblies.json` и `RuntimeInitializeOnLoads.json`, создать `<папка игры>/PTMod/` (`content/`, `l10n/`, `README.txt`) и записать манифест установки.
2. WHEN JSON правятся впервые THEN установщик SHALL сохранить их оригиналы в `_original_en/` (если там их ещё нет). Правка SHALL быть идемпотентной (повторный запуск не дублирует записи) и атомарной (временный файл → замена).
3. IF хэш оригинального `Assembly-CSharp.dll` не совпадает с `translation/meta.json` THEN установщик SHALL отказать в установке модов с объяснением. С флагом `--force` установщик SHALL установить моды, предупредив о риске.
4. WHEN запускается `--remove-mods` THEN установщик SHALL удалить файлы мода по манифесту и вернуть два JSON из бэкапа. Перевод и пользовательские файлы в `PTMod/content`, `PTMod/l10n` SHALL остаться.
5. WHEN запускается `--restore` THEN установщик SHALL вернуть все оригиналы, включая два JSON, и удалить файлы мода по манифесту.
6. IF в JSON уже есть запись `PTMod`, а манифеста нет THEN установщик SHALL сообщить о ручной установке и предложить `--remove-mods --force`.
7. WHEN запускается `--check` THEN установщик SHALL дополнительно проверить контент и L10n мода без игры (JSON разбирается, `id` уникальны, ключи ru/en совпадают).
8. Установщик SHALL не трогать сейвы и выводить сообщения на русском.
9. WHEN запускается `install.py <папка> --with-mods --mods-only` THEN установщик SHALL установить только моды и SHALL не трогать `data.unity3d` и `Assembly-CSharp.dll` (игра остаётся английской или с уже установленным переводом). Проверка версии игры (п. 3) применяется и в этом режиме: по `_original_en/Assembly-CSharp.dll`, а если его нет — по текущему файлу как по оригиналу.
10. WHEN установщик ищет `PTMod.dll` и `0Harmony.dll` THEN он SHALL искать их по порядку: аргумент `--mod-dir`, папка `mod/build/`, папка рядом с `install.py` (распакованный GitHub Release). IF файлов нет, но найден `csc.exe` (Visual Studio 2022 Build Tools, `vswhere` или известные пути) THEN установщик SHALL собрать мод сам (`mod/build.py`); IF нет ни DLL, ни `csc` THEN установщик SHALL остановиться с инструкцией «скачайте готовые DLL из GitHub Release». Установщик SHALL не обращаться к сети.
11. Готовые DLL SHALL публиковаться в GitHub Release вместе с `0Harmony.dll`, лицензией MIT Harmony и контрольными суммами. Релизная сборка SHALL не содержать служебную фичу `framework-sample` (она входит только в отладочную сборку).

**Сценарии:**
- **AC-20.1.** Given чистая игра 0.2.3; When `install.py "<папка>" --with-mods` дважды; Then в `ScriptingAssemblies.json` ровно одна запись `PTMod.dll`, в `RuntimeInitializeOnLoads.json` ровно одна запись `ModBootstrap.Init`.
- **AC-20.2.** Given установлены перевод и моды; When `--remove-mods`; Then `PTMod.dll`, `0Harmony.dll` удалены, JSON совпадают с бэкапом побайтно, перевод на месте.
- **AC-20.3.** Given английская игра без перевода; When `install.py "<папка>" --with-mods --mods-only`; Then `data.unity3d` и `Assembly-CSharp.dll` побайтно не изменились, моды установлены, `--remove-mods` возвращает два JSON.
- **AC-20.4.** Given неизвестная версия игры (хэш `Assembly-CSharp.dll` не совпал); When `--with-mods` без `--force`; Then отказ с объяснением и кодом возврата ≠ 0, файлы игры не тронуты; с `--force` — установка с предупреждением.
- **AC-20.5.** Given `mod/build/` пуст, `PTMod.dll` рядом с `install.py` нет, а `csc.exe` найден; When `--with-mods`; Then установщик запускает `mod/build.py`, затем устанавливает результат; без `csc` — остановка с инструкцией про GitHub Release, файлы игры не тронуты.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-1, FR-21; решение 0001
- **Assumptions**: формат JSON Unity — `ScriptingAssemblies.json`: `{"names": [...], "types": [...]}`; `RuntimeInitializeOnLoads.json`: `{"root": [...]}`. Подтверждается в спайке 0001.

### FR-21: Сборка и тесты (решение 0003, обновлено)

**User Story:** Как разработчик, я хочу воспроизводимо собирать `PTMod.dll` одной командой без .NET SDK и гонять тесты чистой логики без запуска игры и без загрузок из сети.

#### Acceptance Criteria

1. WHEN выполняется `python mod/build.py --game-dir <папка игры>` (или задана переменная `PT_GAME_DIR`) THEN скрипт SHALL найти Roslyn `csc.exe` из Visual Studio 2022 Build Tools (`vswhere` или известные пути) и собрать `mod/build/PTMod.dll` с параметрами `-langversion:latest -nostdlib -target:library` и ссылками на `Managed/mscorlib.dll`, `netstandard.dll`, `System*.dll`, `UnityEngine*.dll`, `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`, `_original_en/Assembly-CSharp.dll` и `mod/lib/0Harmony.dll`, не копируя DLL игры в выход и в репозиторий. Без `csc` или без папки игры скрипт SHALL остановиться с понятной подсказкой на русском.
2. Сборка SHALL встраивать `Content/**` и `L10n/**` как ресурсы (`-resource:<файл>,<LogicalName>`: `content/<feature>/<file>.json`, `l10n/<lang>.json`, `l10n/<lang>/<feature>.json`) и класть результат в `mod/build/`.
3. WHEN выполняется `python mod/build.py --tests` THEN скрипт SHALL собрать тесты тем же `csc` в консольный exe под .NET Framework 4.8 (ссылки на `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`) со своим мини-раннером и файлами `Core/Logic/**`, запустить его и вернуть ненулевой код при падении теста. Тесты чистой логики SHALL проходить без Unity, без файлов игры, без xUnit и NuGet. Тесты установщика — Python `unittest`.
4. Код `Core/Logic/**` SHALL компилироваться и под .NET Framework 4.8 (тесты), и под `mscorlib` Unity (мод): только общее подмножество API; конструкции C#, требующие отсутствующих в рантайме Unity типов (`init`, `record`, `Index`/`Range`), допускаются только вместе с типами-полифилами из `Core/Compat/`.
5. Версия мода SHALL задаваться в одном месте (`mod/VERSION`) и попадать в сборку (`BuildInfo.g.cs`, `AssemblyVersion`) и в `ModSave.modVersion`.
6. `mod/build.py` SHALL поддерживать режимы: по умолчанию — отладочная сборка (определён символ `PTMOD_DEBUG`, в сборку входит `framework-sample` и флаги `debug.*`), `--release` — релизная сборка без `framework-sample` и пакет `mod/build/release/PTMod-<версия>.zip` (`PTMod.dll`, `0Harmony.dll`, лицензия Harmony, `SHA256SUMS.txt`).

**Сценарии:**
- **AC-21.1.** Given чистый клон репозитория, локальная игра и VS Build Tools; When `python mod/build.py --game-dir <игра>`; Then в `mod/build/` есть `PTMod.dll`, а `git status` не показывает новых файлов, кроме игнорируемых.
- **AC-21.2.** Given машина без игры; When `python mod/build.py --tests`; Then все тесты проходят (exe собран `csc` под .NET Framework 4.8).
- **AC-21.3.** Given `python mod/build.py --release`; When просматривается архив и `PTMod.dll`; Then в архиве нет `framework-sample`-типов и ресурсов, есть `0Harmony.dll` и `SHA256SUMS.txt`.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: решение 0003 (accepted: Roslyn `csc`, без .NET SDK); `mod/lib/0Harmony.dll` (Harmony 2.3.6, net48, уже скачан, в `.gitignore`)
- **Assumptions**: на машине есть копия игры и VS 2022 Build Tools (`csc.exe`); путь к игре задаётся `--game-dir` или `PT_GAME_DIR`. Игроки без Build Tools берут готовые DLL из GitHub Release.

### FR-22: Инструменты разработчика

**User Story:** Как автор спек, я хочу автоматически проверять, что упомянутые методы игры существуют и что шаблоны UI находятся, чтобы спеки не расходились с кодом.

#### Acceptance Criteria

1. WHEN запускается `tools/check_spec_refs.py --code <pt-modding/code>` THEN скрипт SHALL найти в `.kiro/specs/**/*.md` ссылки вида `Class.Method` в обратных кавычках, у которых `Class` — класс игры, и сообщить те, которых нет в `pt-modding/code`. Код возврата 1 при ошибках.
2. WHEN запускается `tools/dump_ui_templates.py <папка игры>` THEN скрипт SHALL вывести пути GameObject всех шаблонов `ModUI.Templates` в `level0` и `level1`, используя `ptru.game`.
3. Инструменты SHALL только читать файлы игры.
4. WHEN запускается `tools/check_spec_refs.py --core-api <design.md фреймворка>` THEN скрипт SHALL собрать публичные имена API ядра из блоков C# в `design.md` mod-framework и проверить в спеках фич ссылки вида `ModХ.Член` и `ModХ.Событие` в обратных кавычках: несуществующее имя (например, `ModTicks.OnDayPassed`, `ModTicks.IsBatchRunning`) SHALL сообщаться как ошибка «файл:строка: нет в API ядра» с кодом возврата 1.

**Сценарии:**
- **AC-22.1.** Given тестовая спека со ссылкой на несуществующий метод класса `Market`; When запускается `check_spec_refs.py`; Then код возврата 1 и строка «файл:строка: метод не найден».
- **AC-22.2.** Given папка игры с переводом; When запускается `dump_ui_templates.py`; Then для каждого ключа `ModUI.Templates` выведен путь GameObject в своей сцене.
- **AC-22.3.** Given тестовая спека фичи со ссылкой `ModTicks.OnDayPassed`; When запускается `check_spec_refs.py --core-api`; Then код возврата 1 и строка «файл:строка: нет в API ядра (используйте FeatureContext.OnTick(TickPhase.DayPassed))».

#### Additional Details
- **Priority**: Should
- **Complexity**: Low
- **Dependencies**: `ptru/game.py`
- **Assumptions**: `pt-modding/` есть только локально.

### FR-23: Общие события игры и классификатор сегмента (ModEvents, ModSegments)

**User Story:** Как автор фичи (обзоры, отзывы, характеры соперников, платформы, сценарии), я хочу подписаться на «CPU выпущен» и получать сегмент CPU по цене из ядра, чтобы не ставить свой патч на `Company.AddCpu` / `AICompany.AddCpu` (у пяти спек защиты были разными, и CPU обрабатывались дважды или пропускались) и не копировать правило сегмента.

#### Acceptance Criteria

1. WHEN выполняется `Company.AddCpu` или `AICompany.AddCpu` THEN ядро (один postfix P20 на каждый из двух методов, владелец `mod-framework`) SHALL поднять `ModEvents.CpuReleased(owner, cpu, isVariant, duringFastForward)`, где `owner` — `ICompany` компании-владельца, `isVariant` = `cpu.Base != null`, `duringFastForward` = `DateController.IsFastFowardSimulating`.
2. WHILE `GameManager.get_IsLoading` = true (вызовы из `SaveHandler.Load` и `SaveObjectInstantiator`) ядро SHALL не поднимать `CpuReleased`. IF `cpu` = null THEN событие не поднимается. WHILE идёт перемотка новой игры событие SHALL подниматься с `duringFastForward = true`; фичи SHALL по политике владельца не создавать в этом случае событий, писем и реакций.
3. Ядро SHALL поднимать `CpuReleased` для одного объекта CPU не более одного раза за сессию сцены (повторный `AddCpu` игнорируется).
4. WHEN фича переносит CPU между компаниями (поглощение в `rival-acquisitions`, перенос через `Player.AddCpu`) THEN она SHALL вызвать `ModEvents.RaiseCpuTransferred(featureId, from, to, cpu)`, а ядро SHALL поднять `CpuTransferred(from, to, cpu)` и SHALL не поднимать `CpuReleased`. Не во время загрузки.
5. Обработчики SHALL вызываться в порядке подписки, изолированно (FR-2 п. 8), только для активных фич. Фичи SHALL не патчить `Company.AddCpu` и `AICompany.AddCpu` (FR-2 п. 6).
6. `ModSegments.Classify(cpu)` и `ModSegments.Classify(price)` SHALL определять сегмент по цене и текущим бюджетам рынка: `HighEnd`, если цена > `Market.get_MidRangeBudget × band`; `MidRange`, если цена > `Market.get_LowEndBudget × band`; `LowEnd`, если цена > `Market.get_IndustriesBudget × band`; иначе `Industries` (`band` = `segments.priceBand` = 1,25). IF `Market.Instance` = null или цена не конечное число THEN результат `Segment.Any`.
7. `ModSegments.FromSales(highEnd, midRange, lowEnd, industries)` SHALL возвращать сегмент с наибольшими продажами (при равенстве — с меньшим индексом) и `Segment.Any`, если продаж нет. Фичи SHALL использовать эти методы вместо собственных классификаторов.

**Сценарии:**
- **AC-23.1.** Given бюджеты сегментов 599 / 249 / 149 / 79 и `band` 1,25; When `Classify(price)` для 312, 311, 187, 186, 99, 98; Then `HighEnd`, `MidRange`, `MidRange`, `LowEnd`, `LowEnd`, `Industries`.
- **AC-23.2.** Given загрузка сейва, при которой игра вызывает `AddCpu` для 40 CPU; When идёт загрузка; Then `CpuReleased` не поднят ни разу.
- **AC-23.3.** Given игрок выпускает CPU-вариант в окне релиза; When вызывается `Company.AddCpu`; Then `CpuReleased` поднят один раз с `isVariant = true`, `duringFastForward = false`, а 5 подписанных фич получили его ровно по разу.
- **AC-23.4.** Given ИИ выпускает CPU во время перемотки новой игры; When вызывается `AICompany.AddCpu`; Then `CpuReleased` поднят с `duringFastForward = true`.
- **AC-23.5.** Given `rival-acquisitions` переносит 3 CPU игроку и вызывает `RaiseCpuTransferred`; When обрабатываются подписчики; Then `CpuTransferred` поднят 3 раза, `CpuReleased` — 0.

#### Additional Details
- **Priority**: Must
- **Complexity**: Medium
- **Dependencies**: FR-2, FR-4, FR-12 (тип `Segment`)
- **Assumptions**: `Company.AddCpu` и `AICompany.AddCpu` — единственные точки выпуска CPU (проверено grep: вызывают `ProjectReleaseWindow`, `AIBehaviourCreation`, загрузка сейва). Бюджеты читаются из `Market` (`get_HighEndBudget`, `get_MidRangeBudget`, `get_LowEndBudget`, `get_IndustriesBudget`).

### FR-24: Единый порог кредита (ModCredit)

**User Story:** Как автор `bankruptcy-recovery`, обучения и поглощений, я хочу брать порог дефолта компании из одного места, чтобы предупреждения, автозаём, строка «до порога» и спасение ИИ считались от того порога, который реально применяет игра, а не от глобального `CentralBank.DebtLimit`.

#### Acceptance Criteria

1. ModCredit SHALL давать `ThresholdOf(company)` (порог дефолта — отрицательное число, как баланс: дефолт при `MoneyAmount < ThresholdOf`), `DebtLimitOf(company)` (положительный лимит), `PlayerDebtLimit()` (то, что показывает интерфейс) и `IsPerCompanyActive`.
2. IF transpiler P10 применён THEN `IsPerCompanyActive` = true и `ThresholdOf(c)` = `−CentralBank.DebtLimit × M(bank.creditLimit, c)`. IF нет THEN `IsPerCompanyActive` = false и `ThresholdOf(c)` = `−CentralBank.DebtLimit` (глобальный лимит с учётом P9): ModCredit возвращает ровно тот порог, который применяет игра в этот момент.
3. `PlayerDebtLimit()` = `−ThresholdOf(player)`; те же значения SHALL показывать transpiler'ы P11, чтобы интерфейс игры и фичи были согласованы.
4. Фичи (`bankruptcy-recovery`, `first-year-onboarding`, `rival-acquisitions`, `scenarios-achievements`) SHALL брать пороги из ModCredit, а не читать `CentralBank.DebtLimit` напрямую. `scenarios-achievements` SHALL запрещать испытание «Жёсткий кредит» (множитель на `bank.creditLimit`), когда `IsPerCompanyActive` = false.
5. Пороги ИИ в `AIBehaviour*` и лимиты кредитов `bankruptcy-recovery` SHALL не умножаться на `bank.creditLimit` по компании (решение владельца; см. FR-12 п. 19).

**Сценарии:**
- **AC-24.1.** Given `DebtLimit` = 100 млн, `M(bank.creditLimit, player)` = 0,5, `M(…, ai)` = 1; When P10 применён; Then `ThresholdOf(player)` = −50 млн, `PlayerDebtLimit()` = 50 млн, `ThresholdOf(ai)` = −100 млн, `IsPerCompanyActive` = true.
- **AC-24.2.** Given то же, но transpiler P10 не применился (паттерн не найден); When читается ModCredit; Then `IsPerCompanyActive` = false, `ThresholdOf` = −100 млн для всех, в «Диагностике» предупреждение.
- **AC-24.3.** Given глобальный `bank.creditLimit` ×0,8 (P9); When читается ModCredit; Then оба режима дают `−DebtLimit` уже после P9 (80 млн).

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-12 (P9–P11)
- **Assumptions**: паттерн P10 подтверждается спайком 0002 (ровно 3 загрузки локальной `V_0` в цикле `BankruptcyHandler.TickBankruptcies`).

### FR-25: Соглашение об идентификаторах

**User Story:** Как автор спеки фичи, я хочу знать, какие префиксы использовать в ключах строк, предикатах, ID событий и тегах писем, чтобы идентификаторы разных фич не конфликтовали, а ядро могло это проверять.

#### Acceptance Criteria

1. Ядро SHALL хранить таблицу «фича → префикс(ы)» (`prefixes` в `core.json`, раздел design.md «Соглашение об идентификаторах»); добавление фичи — строка этой таблицы (изменение спеки).
2. Ключи ModL10n фичи SHALL начинаться с `<prefix>.` (ядро — `core.`). Предикаты `ModConditions` фичи SHALL называться `<prefix>.<name>`, ID событий `event-system` — `<prefix>.<name>`, теги писем — `<prefix>:<…>`, причины (`reasonKey` каналов и `reason` репутации) — ключи L10n той же фичи. Имена предикатов ядра SHALL не содержать точки.
3. ID фичи, секции `ModSave`/`ModProfile`, Harmony ID (`ptmod.<feature-id>`), строки в `ModLog` и ID потоков `ModRandom` SHALL использовать полный slug фичи.
4. Имена каналов `ModModifiers` SHALL иметь вид `<домен>.<имя>` (домен: `research`, `production`, `factory`, `market`, `popularity`, `cpu`, `contract`, `bank`, `taxes`, `construction`, `development`, `gpu`, `acquisition`) и SHALL не использовать префикс фичи; все каналы объявлены в реестре ядра (FR-12 п. 15).
5. `ModIds` SHALL предоставлять чистые проверки (`IsL10nKeyAllowed`, `IsPredicateNameAllowed`, `IsEventIdAllowed`, `IsTagAllowed`) для ModConditions, ModL10n (самопроверка), ModMail (предупреждение при отправке) и `event-system` (валидатор контента).

**Сценарии:**
- **AC-25.1.** Given фича `market-shocks` с префиксом `ms`; When проверяются `ms.earthquake`, `earthquake`, `es.earthquake` как ID события; Then разрешён только первый.
- **AC-25.2.** Given `ModMail.Send` с `Tag = "welcome"` от `first-year-onboarding`; When письмо ставится в очередь; Then в логе одно предупреждение «тег без префикса `onb:`», письмо отправлено.
- **AC-25.3.** Given `scenarios-achievements` с префиксами `scn`, `chl`, `ach`; When проверяются ключи `chl.tightCredit.title`, `ach.first.name`, `onb.step1`; Then первые два разрешены, третий — нет.

#### Additional Details
- **Priority**: Must
- **Complexity**: Low
- **Dependencies**: FR-8, FR-11, FR-14
- **Assumptions**: единый список префиксов лежит в одном месте; спеки фич обязаны привести свои ключи в соответствие с таблицей.

## Non-Functional Requirements

### NFR-1: Производительность на тик
- WHEN игра идёт на скорости 3 (12 игровых дней в секунду), в мире 14 компаний и 200 CPU на рынке THEN накладные расходы ядра PTMod (диспетчер тиков, `ModModifiers` со всеми патчами, кеш `ModConditions`, очередь `ModMail`, дрейф репутации) SHALL быть не больше 0,5 мс на игровой день в среднем за 365 дней и не больше 2 мс в день 1 января.
- WHEN у канала нет записей THEN патч канала SHALL возвращаться за O(1) без выделения памяти.
- Патч P19 (`CpuMath.ProjectCost`) вызывается ИИ тысячи раз за месяц: WHEN у `development.cost.design` нет записей и провайдеров THEN он SHALL возвращаться за O(1); WHEN провайдеры есть THEN вызов SHALL занимать не больше 5 мкс (кеша нет, провайдеры обязаны быть дешёвыми).
- Postfix P20 (`Company.AddCpu`, `AICompany.AddCpu`) SHALL занимать не больше 20 мкс без подписчиков.
- WHILE идёт перемотка новой игры (до ≈ 9 100 тиков) PTMod SHALL добавлять не больше 5 % ко времени перемотки.

### NFR-2: Время старта
- WHEN игра запускается THEN `ModBootstrap.Init` вместе с применением патчей и загрузкой контента SHALL занимать не больше 1,0 с.
- Самопроверка SHALL занимать не больше 300 мс на этап.

### NFR-3: Надёжность
- IF в коде мода возникает исключение THEN оно SHALL перехватываться на границе хука, события или UI-обработчика и не попадать в код игры.
- IF сервис мода недоступен THEN игра SHALL работать как ванильная (с переводом).
- WHEN фича выключена посреди игры THEN игра SHALL продолжаться без ошибок, а состояние фичи SHALL сохраняться.

### NFR-4: Совместимость сейвов
- Ванильные сейвы SHALL загружаться с модом (значения по умолчанию).
- Сейвы, сделанные с модом, SHALL загружаться без мода: `<имя>.txt` ванильный по формату, письма мода в нём отсутствуют.
- Формат бокового файла SHALL иметь версию схемы, а каждая секция — свою `_schema` с миграциями. Поле `_schema` секции пишет и читает ядро (FR-5 п. 11–12); фичи свою версию в секции не хранят.

### NFR-5: Локализация
- Все строки мода SHALL идти через ModL10n. `ru` — по умолчанию, таблица `en` полная (самопроверка сообщает расхождения).

### NFR-6: Удобство
- Хаб SHALL открываться одним кликом с рабочего стола.
- Каждая ошибка мода SHALL быть видна в «Диагностике» с понятным русским текстом и рекомендацией (например, «переустановите моды: `install.py --with-mods`»).

### NFR-7: Безопасность
- PTMod SHALL не обращаться к сети: в сборке нет ссылок на `System.Net` и `UnityEngine.Networking` (проверяется тестом).
- Контент, настройки и боковые файлы SHALL валидироваться перед использованием. Пути файлов SHALL не выходить за `persistentDataPath/Saves/`, `persistentDataPath/` (настройки и профиль) и `<папка игры>/PTMod/`.

### NFR-8: Сопровождаемость
- Чистая логика (`*/Logic/`) SHALL не ссылаться на `UnityEngine` и `Assembly-CSharp` и SHALL быть покрыта юнит-тестами: каждый публичный тип `Core/Logic/**` SHALL быть указан в атрибуте `[Covers]` хотя бы одного теста, а ключевые случаи таблицы «Юнит-тесты» design.md SHALL быть реализованы. Процент покрытия строк не измеряется: инструмента без сторонних загрузок нет (открытый вопрос 1).
- Имена шаблонов UI и целей патчей SHALL храниться в одном месте (`ModUI.Templates`, классы патчей).

### NFR-9: Сосуществование с русификатором
- PTMod SHALL не патчить строки игры и SHALL работать поверх переведённой `Assembly-CSharp.dll` и `data.unity3d`.
- Логика мода SHALL не опираться на переводимые строки игры (имена компаний, сегментов): только на индексы и ID.

### NFR-10: Размер и скорость бокового файла
- Боковой файл после 10 игровых лет с включённым ядром и `framework-sample` SHALL весить не больше 1 МБ. Запись SHALL занимать не больше 50 мс.

## Edge Cases

- Сейв с пустым именем: игра пишет `Y_M_D.txt`, боковой файл получает то же имя.
- Автосейв (`autosave.txt`, 1 января) перезаписывает и `autosave.ptmod.json`.
- Имя сейва с разделителями пути (игра их не чистит): боковой файл строится той же конкатенацией. Если игра не смогла записать `.txt`, постфикс не выполняется.
- Загрузка из главного меню (`level0`) и из игры (`level1`): `SaveHandler.Load` обнуляет `OnLoaded` до смены сцены. Подписка ставится после загрузки новой сцены.
- `SaveHandler.Load` с несуществующим файлом: игра пишет «No save was found!» и сцену не меняет, ожидающее имя не запоминается.
- Выход в меню и новая игра: состояние модов сбрасывается при загрузке `level0`.
- `DateController` пересоздаётся или переживает загрузку сцены: оба случая поддержаны (FR-4 п. 4).
- Подписчики `ThemeManager.OnThemeChanged` стираются при каждой смене сцены: ModUI переподписывается.
- Окно мода с `PauseTrigger` уничтожено: `PauseHandler` хранил бы мёртвый триггер, поэтому окна не уничтожаются.
- Клон окна с синглтоном в `Awake` (`BreakContractWindow.Instance`): клон создаётся под неактивным родителем, скрипт удаляется до активации.
- Боковой файл новее мода (секция с `_schema` больше поддерживаемой): фича работает по умолчанию, секция переписывается как есть.
- Диск полон при записи бокового файла: ванильный сейв уже записан, мод сообщает об ошибке.
- Компания ИИ обанкротилась: ключ `ai:<UniqueID>` и её записи (репутация, модификаторы) сохраняются.
- Игра другой версии: самопроверка отмечает отсутствующие цели, такие фичи `Unavailable`, остальные работают.
- Русификатор переустановлен после модов: `install.py` пересобирает только `data.unity3d` и `Assembly-CSharp.dll`, моды остаются.
- `PopulationData.GetCurrentSize` вызывается до создания `Market` (в `Market.Start`): канал спроса возвращает ванильное значение.
- `ProductionMath.RequiredProductionLines` зацикливается при `DiesPerWafer` ≤ 0: `production.output` не трогает `DiesPerWafer`.
- Письмо мода открыто, когда фичу выключили: кнопки становятся неактивными при следующем показе письма.
- `Company.AddCpu` и `AICompany.AddCpu` игра вызывает и при загрузке сейва (`SaveHandler`, `SaveObjectInstantiator`): `ModEvents.CpuReleased` при загрузке не поднимается; CPU, перенесённые между компаниями, поднимают только `CpuTransferred`.
- `DateController.NextMonth` проводит до 31 тика за кадр: письма и уведомления откладываются до `BatchEnded`; при исключении внутри метода счётчик пакетного прогона всё равно сбрасывается (finalizer).
- Письма `Critical` обходят дневной лимит, но не `GameReady` и не пакетный прогон; очередь переполнена — отбрасываются `Low`, затем `Normal`.
- Срок ответа письма считается от доставки, а не от отправки: письмо, задержанное лимитом, не теряет срок.
- Провайдер или запись на канал, объявленный позже (порядок `Register` фич не гарантирован): связываются по имени лениво; для необъявленного канала действует предохранитель по умолчанию, самопроверка предупреждает.
- Несколько OEM-контрактов делят один CPU 1 января: `Contract.GetBestOffers` вызывается на каждый контракт отдельно, захват контракта (P23) и ключ кеша с ID контракта не смешивают значения. `Contract.ChanceToSign` из окна контракта вызывает тот же `GetBestOffers`.
- `CpuMath.ProjectCost` вызывается из конструктора (на каждое движение ползунка) и из `AIBehaviourCreation` (много раз в месяц): канал `development.cost.design` не кешируется, провайдеры обязаны быть дешёвыми.
- Два быстрых выхода из игры подряд: профиль записывается принудительно при выходе, `.bak` хранит предыдущую версию; повреждённый основной файл восстанавливается из `.bak`.
- Секция в боковом файле без поля `_schema` (ручная правка): читается как версия 1 с предупреждением.
- Исключение в `build` секции окна новой игры: секция показывает ошибку, остальные секции и старт игры не затронуты.
- Фича выключена в хабе через подтверждение «Отмена»: состояние не меняется, `OnDisabled` не вызывается.

## Constraints and Assumptions

### Technical Constraints
- Unity 6000.0.65f1, Mono, uGUI + TextMeshPro. Сборка мода — Roslyn `csc.exe` из VS 2022 Build Tools, C# latest, `-nostdlib` с ссылками на `Managed/*.dll` (решение 0003, обновлено); .NET SDK не используется. Тесты — свой мини-раннер на .NET Framework 4.8; `Core/Logic/**` компилируется в обоих окружениях (FR-21 п. 4).
- Без правки `data.unity3d` и без своих ассетов: UI только из клонов окон игры (решение 0005).
- Без полей в классах игры: состояние только в боковом файле (решение 0004).
- Сторонние загрузки — только после одобрения владельца. Одобрена и выполнена загрузка `Lib.Harmony` 2.3.6 (`mod/lib/0Harmony.dll`, net48, MIT, в `.gitignore`, в релизах — рядом с `PTMod.dll`). Других загрузок нет: ни .NET SDK, ни xUnit, ни NuGet-пакетов тестов.
- Файлы игры и декомпилированный код не попадают в репозиторий.
- Запуск игры — ручной шаг владельца, не во время онлайн-игр с античитом.

### Business Constraints
- Автор коммитов — только владелец, без AI-атрибуции.
- Реализация — строго по `tasks.md` (спека одобрена владельцем 2026-10-10). Любое решение владельца можно переиграть: каскадом правятся `requirements.md` → `design.md` → `tasks.md`.

### Assumptions
- Unity 6 Mono загружает сборку из `ScriptingAssemblies.json` и вызывает метод из `RuntimeInitializeOnLoads.json` (спайк 0001).
- Harmony 2.3.6 (сборка net48) работает на этом рантайме, включая патчи приватных методов (`Market.UpdatePopularities`, `Contract.GetBestOffers`, `MenuGameSetup.UpdateInitialData`) и методов с приватными вложенными типами в сигнатуре (спайк 0002).
- `SaveSystem.Save`, `SaveSystem.Delete`, `SaveHandler.Load` — единственные точки записи, удаления и загрузки сейвов (проверено grep по `pt-modding/code`).
- Имена GameObject шаблонов не меняются русификатором (он меняет тексты TMP и строковые поля).

## Success Criteria

### Definition of Done
- [x] Решения 0001–0006 одобрены владельцем (2026-10-10, статус accepted). Остаётся закрыть спайки 0001–0003 (или задействовать фолбэки) и дописать Evidence в решения.
- [ ] Все FR с приоритетом Must реализованы, их критерии проверены тестами или ручными сценариями из design.md.
- [ ] `python mod/build.py` и `python mod/build.py --tests` зелёные; каждый публичный тип `Core/Logic/**` указан в `[Covers]`.
- [ ] `python install.py --check` и тесты установщика (`unittest`) зелёные.
- [ ] `tools/check_spec_refs.py` не находит несуществующих `Class.Method` в спеках, а с `--core-api` — несуществующих имён API ядра в спеках фич.
- [ ] Владелец прошёл ручные сценарии M1–M24 из design.md (включая прогон «все фичи сразу» M18), результат зафиксирован в `tasks.md`.
- [ ] Самопроверка на старте: `OK` на чистой игре 0.2.3 с переводом.

### Acceptance Metrics
- Накладные расходы ядра ≤ 0,5 мс на игровой день (вкладка «Диагностика», среднее за 365 дней на скорости 3).
- Прирост времени старта ≤ 1,0 с, перемотки ≤ 5 %.
- 0 исключений с префиксом `PTMod` в стеке исключений игры за 10 игровых лет с `framework-sample`.
- Цикл «сохранить → загрузить» не меняет содержимое секций бокового файла (кроме метаданных времени).
- Ванильный сейв, сделанный с модом, загружается после `install.py --remove-mods`.
- Ни один CPU не обработан дважды при выпуске и не пропущен при загрузке сейва (`CpuReleased`: 0 событий при загрузке, 1 на выпуск).
- Суммарный множитель `market.rating.cpu` и `popularity.gain` на любой CPU при всех включённых фичах лежит в [0,5; 2,0].

## Решения по умолчанию

Владелец ответил на все открытые вопросы v0.1 в `.kiro/steering/owner-decisions.md` (2026-10-10). Ниже — ответы, применённые в этой спеке, и решения, принятые по общему правилу («если варианта нет — самый простой безопасный»). Любое решение можно переиграть: каскадом правятся `requirements.md` → `design.md` → `tasks.md`.

### Ответы на вопросы v0.1

| № | Вопрос v0.1 | Решение | Источник |
|---|---|---|---|
| 1 | Одобрить решения 0001–0006, `0Harmony.dll`, .NET SDK | Решения 0001–0006 accepted; `Lib.Harmony` одобрена; .NET SDK не нужен (0003 обновлено: Roslyn `csc` из VS Build Tools) | owner-decisions, «Общие политики» п. 7, 8; решения 0002, 0003 |
| 2 | Версия Harmony 2.2.2 или 2.3.x | 2.3.x, фактически 2.3.6 (net48), лежит в `mod/lib/0Harmony.dll`; 2.2.2 остаётся фолбэком спайка 0002 | п. 8 |
| 3 | Где кнопка «Моды» | клон `DoorButton` на панели; запасная кнопка в меню паузы остаётся фолбэком (FR-17 п. 7) | п. 11 |
| 4 | Как распространять DLL | готовые DLL — в GitHub Release; `install.py --with-mods` ставит их, а если найден `csc` — умеет собрать сам (FR-20 п. 10–11) | п. 9 |
| 5 | Неизвестная версия игры | отказ без `--force` | п. 10 |
| 6 | Режим `--mods-only` | нужен (FR-20 п. 9) | п. 10 |
| 7 | Код ядра в подмножестве C# 5 | не нужно: C# latest; ограничение — общее подмножество API с .NET Framework 4.8 для `Core/Logic/**` (FR-21 п. 4) | п. 7 |
| 8 | `bank.creditLimit` к порогам ИИ в `AIBehaviour*` | не применяется (FR-12 п. 19, FR-24 п. 5) | п. 12 |
| 9 | `production.unitCost` | только фактические траты (FR-12 п. 19) | п. 12 |
| 10 | Переупорядочивать почту по датам | да, после восстановления писем мода почта сортируется по дате (FR-14 п. 11) | п. 11 |
| 11 | Язык мода | своя настройка `ModSettings.Language`, по умолчанию `ru` (FR-8 п. 7) | п. 6 |
| 12 | `framework-sample` в релизе | только в отладочной сборке (FR-21 п. 6, FR-20 п. 11) | п. 10 |
| 13 | Каналы `construction.cost` и `development.cost` | утверждены (FR-12 п. 1) | п. 12 |
| 14 | `ModProfile` во фреймворке | во фреймворке: с `.bak`, троттлингом записи и единственным писателем файла (FR-6 п. 5–9) | п. 5 (`ModProfile`) |

### Общие политики владельца, внесённые в ядро

| Политика | Где в спеке | Источник |
|---|---|---|
| Перемотка новой игры: фичи нейтральны (нет событий, писем, реакций и новых эффектов); состояние, нужное ИИ к старту, инициализируется один раз на `DateController.OnFastFowardEnded` | FR-4 п. 2, FR-14 п. 13, FR-15 п. 2, FR-23 п. 2; тип фичи `RunDuringFastForward` — исключение, а не правило | п. 1 |
| Реальные названия компаний, архитектур, GPU-соперников; издания прессы вымышленные; в контенте компании указываются по `UniqueID` / ключу исторической компании, имя берётся из игры | соглашение FR-25, NFR-9 (логика не опирается на переводимые строки) | п. 2 |
| Одинаковые правила для ИИ и игрока; эффекты для ИИ на низкой сложности не ослабляются | FR-12 п. 4 (области), без ослабления в ядре | п. 3 |
| Предел суммарного эффекта: балансовый диапазон `market.rating.cpu` и `popularity.gain` — [0,5; 2,0] на CPU, «(ограничено)» в Explain | FR-12 п. 2, 9, 18 | п. 4 |
| `ModSegments.Classify(Cpu)` | FR-23 п. 6–7 | п. 5 |
| `ModEvents.CpuReleased` / `CpuTransferred`: один postfix ядра на `Company.AddCpu` / `AICompany.AddCpu` | FR-23 п. 1–5 | п. 5 |
| `ModTicks.IsBatchRun` | FR-4 п. 8–9 | п. 5 |
| `ModMail`: `ModMailAction`, `Enabled`/`DisabledReasonKey`, `Refresh`, `SetStatusLine`, приоритеты и квота на фичу, обход лимита для `Critical`, письма различаются по `Tag`, `Delivered` | FR-14 | п. 5 |
| `ModModifiers`: `ModifierContext.Contract` (P8), причины провайдеров в Explain, сброс кеша по запросу, канал на `CpuMath.ProjectCost` | FR-12 п. 12–14, 17 | п. 5 |
| `ModCredit.ThresholdOf` / `PlayerDebtLimit` / `IsPerCompanyActive` | FR-24 | п. 5 |
| `ModReputation.Drift`: дрейф к 50 в ядре, без записи в историю | FR-13 п. 7–9 | п. 5 |
| `ModProfile` с `.bak` и троттлингом | FR-6 п. 5–9 | п. 5 |
| `ModNewGame.RegisterSection` и `PreviewChanged`, все опции на одной панели за кнопкой «Моды…» | FR-18 | п. 5 |
| Версия секции сейва — поле `_schema`, его пишет и читает ядро | FR-5 п. 11–12 | п. 5 |
| Реестр каналов ядра: все каналы объявлены до `Register` фич (включая `production.memoryCost`, `production.waferCost`, каналы GPU, `acquisition.price`) | FR-12 п. 15–16 | п. 5 |
| Тулчейн: Roslyn `csc` из VS Build Tools, мини-раннер тестов на .NET Framework 4.8, `unittest` для установщика | FR-21 | п. 7 |
| Распространение и установщик (`--mods-only`, отказ на неизвестной версии, `framework-sample` только в debug) | FR-20, FR-21 п. 6 | п. 9, 10 |
| Почта сортируется по дате; кнопка «Моды» — клон `DoorButton` | FR-14 п. 11, FR-17 | п. 11 |
| Каналы `construction.cost`, `development.cost` утверждены; `bank.creditLimit` по компании не применяется к порогам ИИ и лимитам `bankruptcy-recovery`; `production.unitCost` — только фактические траты | FR-12 п. 1, 19; FR-24 п. 5 | п. 12 |

### Решения, принятые по общему правилу при переработке v0.2

| Вопрос | Решение | Обоснование |
|---|---|---|
| Имя канала на проект CPU | `development.cost.design`, отдельный от `development.cost` (тип 3); без кеша | один патч P19 на `CpuMath.ProjectCost`; `development.cost` не видит параметров проекта |
| Квота писем на фичу и лимит дня | 2 письма на фичу и 3 в день для `Normal`/`Low`; `Critical` без лимитов; очередь ≤ 200 | самый простой вариант, ручки в `core.json` |
| Дрейф репутации | 3 % в месяц к 50 (`reputation.driftMonthlyRate`) — значение из `press-reviews`; не во время перемотки; банкроты не дрейфуют | сохраняет баланс `press-reviews` |
| Захват контракта для P8 | prefix/finalizer на приватный `Contract.GetBestOffers` (патч P23) | `Contract.CpuOffer` ссылки на контракт не хранит |
| Балансовый диапазон других каналов | задан только для `market.rating.cpu` и `popularity.gain`; для `gpu.rating` — нет (задаётся в `core.json` без правки кода) | решение владельца ограничено двумя каналами |
| Выбор префиксов фич | существующие префиксы спек закреплены таблицей; `bankruptcy-recovery` → `rec`, `rival-acquisitions` → `acq`, `framework-sample` → `smp` | устраняет полные slug в ключах; таблица в design.md |
| Подтверждение выключения фичи | `FeatureContext.ConfirmBeforeDisable` + `ModUI.Confirm` в хабе | нужно `bankruptcy-recovery` (перевод остатка в обычный долг после подтверждения) |
| Измерение покрытия | покрытие по типам (`[Covers]`) вместо процента строк | инструмент покрытия требует сторонней загрузки |

## Открытые вопросы владельцу

Остались только вопросы, которых `owner-decisions.md` не решает. Оба не блокируют реализацию: действует решение по умолчанию.

1. **Процент покрытия строк.** Для измерения «≥ 80 % строк» нужен сторонний инструмент (OpenCover/coverlet), то есть новая загрузка, что противоречит решению 0003 («без загрузок»). По умолчанию: покрытие по типам (`[Covers]`) и обязательные ключевые случаи (NFR-8). Одобрить загрузку инструмента?
2. **Балансовый диапазон `gpu.rating`.** Нужен ли ему тот же [0,5; 2,0], что у `market.rating.cpu`? По умолчанию: нет, действует только предохранитель [0,25; 4,0]. Решает `gpu-product-line` вместе с владельцем.

## Glossary

| Термин | Значение |
|---|---|
| Хармони (Harmony) | библиотека патчей методов .NET в рантайме (`0Harmony.dll`, MIT) |
| Prefix / Postfix | код мода, выполняемый до / после метода игры |
| Transpiler | правка IL-кода метода игры при патче |
| Finalizer | код мода, выполняемый после метода даже при исключении |
| Фича | модуль мода с ID = slug спеки, включается в `ModSettings` |
| Канал | именованный множитель `ModModifiers` (`market.rating.cpu`), который читает патч формулы игры |
| Запись (entry) | сохраняемый множитель канала: область, значение, срок, фича-владелец |
| Провайдер | функция фичи, вычисляющая множитель канала на лету (не сохраняется) |
| Область (scope) | к чему применяется множитель: `Global`, `AllCompanies`, `Player`, `AIOnly`, `Company`, `Cpu` |
| Ключ компании | стабильный идентификатор: `player` или `ai:<UniqueID>` |
| Боковой файл | `<сейв>.ptmod.json` с состоянием модов |
| Секция | объект фичи в боковом файле, ключ = ID фичи |
| Шаблон окна | существующее окно игры, которое ModUI клонирует |
| Перемотка | симуляция с 1971 года до стартового года новой игры (`DateController.SimulateUntilYear`) |
| GameReady | событие ModTicks: состояние игры готово после загрузки или новой игры |
| Самопроверка | проверка целей патчей, шаблонов, контента и локализации при старте |
| Спайк | короткий эксперимент, подтверждающий или отвергающий решение |
| Фолбэк | запасной путь при провале спайка |
| `Faulted` | фича отключена до перезапуска после серии исключений |
| `Unavailable` | фича не может работать: нет зависимости или цели патча |
| Балансовый диапазон | границы произведения множителей канала по балансу ([0,5; 2,0] для `market.rating.cpu` и `popularity.gain`); отличается от предохранителя |
| Предохранитель | широкие границы [min, max] канала в `core.json`: защита от ошибки контента, а не баланс |
| Реестр каналов | список всех каналов (27 записей) с границами и владельцем; объявлен ядром до `Register` фич |
| Пакетный прогон | `DateController.NextMonth`: до 31 тика за один кадр; признак `ModTicks.IsBatchRun` |
| Действие письма | `ModMailAction`: кнопка в письме, которая вызывает обработчик фичи и не закрывает письмо |
| Приоритет письма | `Critical` / `Normal` / `Low`; `Critical` идёт в обход дневного лимита и квоты на фичу |
| Тег письма | строка `<prefix>:<…>`, по которой фича различает свои письма (`ModMail.Find`) |
| Префикс фичи | короткий код фичи в ключах строк, предикатах, ID событий и тегах писем (таблица в design.md) |
| Проект CPU (`DesignContext`) | пакет, техпроцесс, память и частота проектируемого CPU; контекст канала `development.cost.design` |
