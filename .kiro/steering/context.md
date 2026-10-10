# Контекст проекта: Processor Tycoon RU — форк с модами

## Overview

`processor-tycoon-ru` — русификатор игры **Processor Tycoon 0.2.3** (CoffeeHeaven; Unity 6000.0.65f1, бэкенд Mono; игра бесплатная, автор её забросил). Проект развивается как форк: поверх перевода добавляем геймплейные моды. Они закрывают то, о чём игроки просят в комментариях на itch.io, и добавляют глубины (события, характеры соперников, новые продуктовые линии).

- Перевод: Python-патчер `install.py` + `ptru/`. Подменяет строки в `data.unity3d` и `Assembly-CSharp.dll`, оригиналы бэкапит в `_original_en/`.
- Моды (план): отдельная C#-сборка `PTMod.dll`. Игра загружает её сама, `Assembly-CSharp.dll` не пересобираем.
- Справочные материалы (локально, **не в репозитории**): `C:\Users\gacha\Desktop\New folder\pt-modding\`.
  - `CODEMAP.md` — карта кода по подсистемам.
  - `code/` — псевдо-C# всех классов игры.
  - `data/` — значения сериализованных полей.
  - `IDEAS.md` — 43 идеи с оценкой реализуемости.

## Technology Stack

| Слой | Технология | Примечание |
|---|---|---|
| Игра | Unity 6000.0.65f1, Mono, uGUI + TextMeshPro, LeanTween | 626 типов в `Assembly-CSharp.dll`, пространства имён `ProcessorTycoon.*` |
| Сохранения игры | `JsonUtility` → `Application.persistentDataPath + "/Saves/<name>.txt"` | `SaveHandler.Save(string)` / `SaveHandler.Load(string)`, автосейв 1 января |
| Загрузка мода | `PTMod.dll` в `Processor Tycoon_Data/Managed/` + запись в `ScriptingAssemblies.json` (type 16, как у `Assembly-CSharp.dll`) + запись в `RuntimeInitializeOnLoads.json` | решение 0001 (accepted) |
| Патчи методов | Harmony 2.3.6 (`0Harmony.dll`, net48, MIT), лежит в `mod/lib/` (вне git), в релизах рядом с `PTMod.dll` | решение 0002 (accepted) |
| Сборка мода | Roslyn `csc.exe` из VS 2022 Build Tools (`-langversion:latest -nostdlib`), ссылки на `Managed/*.dll`; скрипт `mod/build.py`; .NET SDK не нужен | решение 0003 (accepted, обновлено) |
| Тесты | свой мини-раннер на .NET Framework 4.8 (логика), Python `unittest` (установщик); без xUnit и NuGet | решение 0003 |
| Установщик | Python 3, `install.py` (+ `--with-mods`, `--mods-only`); готовые DLL — из GitHub Release | расширяется в `mod-framework` |

## Архитектурный контракт мод-фреймворка

Спека `mod-framework` задаёт детали, а имена ниже — контракт, на который опираются все остальные спеки. Пространство имён: `PTMod.Core`.

- **`ModBootstrap`** — точка входа (`[RuntimeInitializeOnLoadMethod]`): применяет патчи Harmony и создаёт постоянный `PTModHost` (`DontDestroyOnLoad`). Загрузка сейва перезагружает сцену, поэтому подписки на синглтоны сцены переустанавливаются.
- **`ModHooks`** — реестр патчей. Каждая фича объявляет патчи в своём классе. Если патч упал, фича отключается и пишется в лог, игра продолжает работать. При старте идёт самопроверка: все целевые методы существуют.
- **`ModTicks`** — подписка на события `DateController` с защитами:
  - по умолчанию не срабатывает во время перемотки новой игры (`DateController.Instance.IsFastFowardSimulating`) и во время загрузки (`GameManager.Instance.IsLoading`);
  - срабатывает строго в порядке дня игры (см. ниже).
- **`ModSave`** — боковой файл `<saveName>.ptmod.json` рядом с сейвом игры.
  - Формат: `{ "schema": n, "features": { "<feature-id>": {...} } }`.
  - Запись — postfix `SaveSystem.Save` (P12; сюда приходит и `SaveHandler.Save`, и автосейв), чтение — на `SaveHandler.OnLoaded`. Версию секции (`_schema`) ведёт ядро.
  - Нет файла или секции — значения по умолчанию: старые сейвы грузятся. Неизвестные секции сохраняются как есть.
  - Поля в классы игры **не добавляем**.
- **`ModModifiers`** — постфикс на `Modifiers.GetModifier(ModifierType, ICompany)`: умножает игровое значение на зарегистрированные множители (по компании и по типу). Для каналов, которых нет в `ModifierType` (спрос, популярность, брак и т. п.), фичи объявляют свои именованные каналы, и их читают патчи соответствующих формул.
- **`ModConditions`** — библиотека предикатов над состоянием игры: год/дата, деньги, есть CPU, подписан контракт, доля рынка, исследована технология, банкротство соперника и т. п. Общая для онбординга, событий и сценариев.
- **`ModMail`** — письма во внутриигровой почте через `EmailWindow.Instance.LoadEmail(Email)` + `Email.Initialize(EmailMessage, int, bool)` с runtime-`EmailMessage`.
  - ID писем мода ≥ 100000 (ID 0 — Year Summary).
  - Письма мода хранятся в `ModSave` и восстанавливаются при загрузке.
  - Письмо может содержать кнопки выбора.
- **`ModNotify`** — всплывающие уведомления через `PopupManager.Instance.InstantiateGenericNotification(string, string)`.
- **`ModUI`** — окна строятся в рантайме.
  - Шаблон — клон существующего окна игры; компоненты игры: `ButtonUI`, `ToggleUI`, `SliderUI`, `DropdownUI`, `InfoText`, `Themeable*`.
  - Тема — через `ThemeManager.Instance`.
  - Кнопка «Моды» на рабочем столе открывает хаб модов.
  - Модальные окна паузят время через `PauseTrigger`.
- **`ModL10n`** — таблицы строк `ru` (по умолчанию) и `en`, вызов `L("key", args)`.
- **`ModSettings`** — глобальные настройки (`persistentDataPath/ptmod_settings.json`): вкл/выкл фич, ручки баланса.
- **`ModRandom`** — детерминированный ГСЧ. Сид хранится в `ModSave`, поэтому после загрузки события воспроизводимы.
- **`ModContent`** — JSON-контент (события, сценарии, характеры, достижения, платформы и т. д.). Встроен ресурсами в `PTMod.dll`, переопределяется из `<папка игры>/PTMod/content/`.
- **`ModLog`** — лог в `Player.log` с префиксом `[PTMod]`.
- **Общие механизмы ядра (v0.2, по решениям владельца)** — фичи используют их вместо собственных патчей и копий кода:
  - `ModEvents.CpuReleased` / `CpuTransferred` — выпуск и перенос CPU (один postfix ядра на `Company.AddCpu` / `AICompany.AddCpu`);
  - `ModSegments.Classify` — сегмент CPU по цене;
  - `ModTicks.IsBatchRun` / `BatchEnded` — пакетный прогон `DateController.NextMonth`;
  - `ModCredit.ThresholdOf` / `PlayerDebtLimit` / `IsPerCompanyActive` — порог дефолта;
  - `ModReputation.Drift` — ежемесячный дрейф к 50 в ядре, без записи в историю;
  - `ModProfile` — единственный писатель глобального профиля (`.bak`, троттлинг);
  - `ModNewGame.RegisterSection` / `PreviewChanged` — общая панель «Моды…» в окне новой игры;
  - `ModMail`: `ModMailAction`, `Enabled`/`DisabledReasonKey`, `Refresh`, `SetStatusLine`, приоритеты `Critical`/`Normal`/`Low`, квота на фичу, `Tag`, `Delivered`;
  - `ModModifiers`: `ModifierContext.Contract` и `Design`, причины провайдеров, балансовый диапазон [0,5; 2,0] у `market.rating.cpu` и `popularity.gain`, `Invalidate`, реестр всех каналов в ядре.
  Подробности и сигнатуры — `.kiro/specs/mod-framework/design.md`.

## Факты об игре, нужные каждой фиче

Источник — `pt-modding/CODEMAP.md`. Все `Class.Method` ниже существуют в `pt-modding/code/`.

- **Время.** `DateController.Instance`: календарь с 1971-01-01, скорости `[0, 1, 4, 12]` дней в секунду, не больше одного дня за кадр.
  - Порядок событий дня: `OnEarlyTick` → `OnAIBehaviourTick` → `OnPreContractSellTick` → `OnClientProductionTick` → `OnContractProductionTick` → `OnContractSellTick` → `OnContractTick` → `OnProductionTick` → `OnMarketSellTick` → `OnTick` → `OnLateTick` → `OnDayPassed`.
  - 1-го числа дальше идут: `OnCompanySpawnTick` → `OnMonthCacheUpdate` → `OnEarlyMonthPassed` → `OnMonthPassed` → (1 января) `OnYearPassed` → `OnLateMonthPassed` → (1 января) `OnLateYearPassed`.
  - Ещё есть `FirstEverTick` и `OnFastFowardEnded`.
  - Пауза: `PauseHandler` останавливает время, пока активен хоть один `PauseTrigger`.
- **Новая игра.** `GameManager.StartNewGame(PlayerInitialData)` → `Player.InitializeAsNewGame`. Она перематывает время с 1971 года (`DateController.SimulateUntilYear`) со всеми тиками и ИИ, затем вызывает `Company.InitializeAsNewGame` и событие `Player.OnNewGame`.
- **Загрузка.** `SaveHandler.Load(string)` перезагружает сцену, затем восстанавливает состояние и поднимает события `DuringLoad` → `OnLoaded` → `OnLateLoaded` → `OnLastLoadedTick`. После сохранения — `OnSaved`.
- **Компании.**
  - Игрок: `Player.Instance.Company` (`Company`, `IsPlayer`).
  - 13 ИИ-соперников (`AICompany`, индекс `UniqueID`), их создаёт `CompanySpawner`. `CompanyHelper` хардкодит часть ID.
  - Деньги: `Company.EarnMoney` / `Company.SpendMoney` с `RevenueOrigin` / `ExpenseOrigin` — попадают в отчёты. `Company.AddMoney` — мимо учёта.
- **Модификаторы.** `Modifiers.GetModifier(ModifierType, ICompany)`. Типы: `ResearchSpeed 0, MaintainanceCost 1, ConstructionCost 2, DevelopmentCost 3, ResearchCost 4, Taxes 5, Interest 6`. Значения зависят от типа компании (fabless / foundry / IDM).
- **Исследования.** `ResearchSector` компании, `Update` на `OnTick`: прогресс = `sqrt(Funding) * ResearchSpeed / Technology.Time`. Платные исследовательские проекты дают постоянные `ResearchModifiers`.
- **Рынок.** `Market.Instance` на `OnMarketSellTick`: 4 сегмента (High End, Mid Range, Low End, Industries), рейтинги, популярность CPU (`Cpu.Popularities`), продажи через `Company.EarnMoney(RevenueOrigin.Sales)`.
- **Контракты.** `ContractManager` / `Contract` (OEM-клиенты), `BusinessContractManager` (сделки между компаниями), автоматизация `ContractAutomation`.
- **Банк.** `CentralBank` (кредитный лимит, `DebtLimit`, проценты), `TaxationHandler`, `InflationHandler`, `BankruptcyHandler` (дефолт → банкротство).
- **UI.** Синглтоны: `EmailWindow.Instance`, `PopupManager.Instance`, `ThemeManager.Instance` (`OnThemeChanged`), `TooltipHandler.Instance`.
- **Важно.** Классы сохранений наследуют runtime-классы (`SaveObject.Team : Team` и др.), поэтому новые публичные поля этих классов попали бы в сейв автоматически. Моды этим **не пользуются**: состояние хранится только в `ModSave`.

## Конвенции

- **Язык.** Спеки и тексты интерфейса — на русском (форк русский). Идентификаторы и комментарии в коде — на английском. Строки мода — через `ModL10n`, с английским фолбэком.
- **Фичи.** ID фичи = slug спеки (`event-system`). Пространство имён — `PTMod.Features.<PascalName>`. Ключ секции в `ModSave` = ID фичи. Точка входа — `[ModFeature] sealed class … : IModFeature` с `Register(FeatureContext)`; версию секции (`_schema`) ведёт ядро, не фича.
- **Префиксы идентификаторов** (полная таблица — `mod-framework/design.md`, «Соглашение об идентификаторах»). Ключи L10n, предикаты `ModConditions`, ID событий — `<prefix>.<name>`; теги писем — `<prefix>:<…>`; предикаты ядра — без точки. Полный slug — только для ID фичи, секций, Harmony ID, лога, потоков ГСЧ.

  | Фича | Префикс | Фича | Префикс |
  |---|---|---|---|
  | `mod-framework` | `core` | `rival-personalities` | `rival` |
  | `framework-sample` | `smp` | `rival-acquisitions` | `acq` |
  | `first-year-onboarding` | `onb` | `scenarios-achievements` | `scn` / `chl` / `ach` |
  | `contract-transparency` | `ct` | `platform-generations` | `pg` |
  | `research-tree-ux` | `rtux` | `cpu-architectures` | `arch` |
  | `bankruptcy-recovery` | `rec` | `gpu-product-line` | `gpu` |
  | `event-system` | `es` | `design-flaw-recalls` | `dfr` |
  | `market-shocks` | `ms` | `historical-events` | `hist` |
  | `press-reviews` | `press` | | |

  Имена каналов `ModModifiers` — `<домен>.<имя>` (`research`, `production`, `factory`, `market`, `popularity`, `cpu`, `contract`, `bank`, `taxes`, `construction`, `development`, `gpu`, `acquisition`); префикс фичи в канале не используется, список каналов — реестр ядра.
- **Раскладка репозитория (план):**
  - `mod/src/PTMod/Core/` — фреймворк;
  - `mod/src/PTMod/Features/<Name>/` — фичи;
  - `mod/src/PTMod/Content/*.json` — контент;
  - `mod/src/PTMod/L10n/{ru,en}.json` — строки;
  - `mod/tests/` — тесты чистой логики;
  - `mod/build/` — результаты сборки (в `.gitignore`);
  - `install.py --with-mods` — установка модов.
- **Ссылки на игру.** Каждый `Class.Method` игры в спеке должен существовать в `pt-modding/code/`; это проверяется скриптом. Патч описывается как «цель `Class.Method`, prefix/postfix, что меняет».
- **Баланс.** Любое число — с указанием, какую формулу или константу игры оно меняет (по CODEMAP). Числа лежат в контент-JSON, а не в коде.
- **Каждая фича:**
  - выключается в `ModSettings`;
  - не ломает сейв, если её выключить или удалить мод;
  - не бросает исключения в код игры (try/catch на границе хука);
  - игнорирует тики во время перемотки новой игры, если явно не сказано иное.
- **ИИ-соперники** получают тот же модификатор, что и игрок, только если спека это явно говорит.

## Ограничения

- В репозиторий не попадают файлы игры и декомпилированный код (только локальный `pt-modding`).
- Моды не правят `data.unity3d`: UI строится в рантайме. Моды сосуществуют с русификатором: патчат методы, а не строки.
- Запуск игры для проверки — ручной шаг владельца, и не во время онлайн-игр с античитом. Автоматически проверяется:
  - сборка;
  - тесты чистой логики;
  - самопроверка мода на старте (все цели патчей найдены);
  - скрипт проверки ссылок в спеках.
- Сторонние загрузки — только с одобрения владельца. Одобрена и сделана только `Lib.Harmony` 2.3.6 (решение 0002); .NET SDK, xUnit и NuGet не используются (решение 0003).
- Одиночная игра, без сети.
- Git: автор коммитов — только владелец, без AI-атрибуции.

## Глоссарий

| Термин | Значение |
|---|---|
| IDM | компания со своим производством и разработкой (тип по умолчанию) |
| Fabless | компания без фабрики, производит у литейщиков |
| Foundry | литейщик: производит чипы для других |
| Техпроцесс (node) | `ProcessNode`, литография в нм |
| IPS | производительность CPU (instructions per second) в модели игры |
| Сегмент | один из 4 сегментов рынка |
| OEM-клиент | компания-покупатель из `ContractNameData` (Apple, Dell, …), с которой заключается `Contract` |
| BusinessContract | сделка между компаниями (поставщик ↔ клиент) |
| Популярность | `Cpu.Popularities[4]`, растёт от продаж, влияет на спрос |
| Тик | один игровой день (`DateController.TriggerTicks`) |
| Перемотка | симуляция с 1971 года до стартового года при новой игре |
| Боковой файл | `<save>.ptmod.json` с состоянием модов |
