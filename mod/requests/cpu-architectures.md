# Запросы cpu-architectures к итоговому интегратору

Кусок 1 (TSK-001, TSK-002, TSK-003): каркас, контент и валидатор каталога, реестр состояния и секция `ModSave`. Блокирующих запросов к ядру и чужим фичам нет;
ниже — обходы, на которые стоит посмотреть при интеграции, и расхождения кода со спекой, которые нужно занести в `design.md`.

## 1. Для интегратора: как устроены файлы фичи

Тестовые сборки (`build.py --tests`, `build_feature.py <slug> --tests`) берут из `Features/<Pascal>/` только `Logic/**`. Типы, которые нужны тестам, но частично знают игру,
разделены на две половины одного `partial`-класса с одним именем:

- `ArchCatalog` — чистая половина `Features/CpuArchitectures/Logic/ArchCatalog.cs`, половина с загрузкой через `ModContent` — `Data/ArchCatalog.cs`;
- `ArchRegistry` — чистая половина `Logic/State/ArchRegistry.cs`, половина с `ICompany` и `Cpu` — `State/ArchRegistry.cs`.

Полная сборка (`python mod\build.py`) компилирует обе половины; сборка тестов видит только чистую (в ней нет ни одного члена, который ссылался бы на вторую). Если правило «в тестах только
`Logic/**`» изменится, делить классы не придётся. Участник сейва: `State/ArchSaveParticipant.cs` (`IModSaveParticipant`) оборачивает чистый `Logic/State/ArchSaveSection.cs`
(`ISideFileParticipant`), потому что `IModSaveParticipant` лежит в `Core/Save/ModSave.cs`, а тесты его не собирают.

## 2. Обходы там, где в ядре нет точки расширения

1. **Фича не может сама пометить себя `Unavailable`.** `ModFeatures.MarkUnavailable` — `internal`. Нужна по FR-1 п. 4: нет семейства `proprietary` — фича выключается целиком. Обход:
   `ArchitecturesFeature.Register` бросает `InvalidOperationException`, ядро ставит `Unavailable` с текстом «Register failed: …» (так и описано в `IModFeature`). Менять ядро не нужно;
   если появится публичный `FeatureContext.MarkUnavailable(reasonKey)`, причину в хабе можно сделать читаемее.
2. **Самопроверка ядра (`ModSelfCheck`) не принимает строки фичи**: таблицы `MemberRow`/`TargetRow` внутренние, а TSK-001 требует писать в лог состояние целей патчей и приватных полей
   из `design.md`. Обход: `ArchSelfCheck` (в корне папки фичи) сам ищет их рефлексией (с проверкой `ModHooks.IsReserved` для целей патчей и `ModModifiers.IsDeclared` для двух каналов),
   пишет по строке на часть в `ModLog` под `cpu-architectures` и возвращает `ArchParts` в `ArchitecturesFeature.Parts`. Строк на вкладке «Диагностика» ядра у фичи пока нет.
   Если вкладке они нужны — публичная точка расширения вроде `ModSelfCheck.AddFeatureRows(featureId, rows)`.
3. **Признак «секция моей фичи записана более новой версией»** (NFR-3, предупреждение в хабе, TSK-031): публичного флага по секции нет, `ModSave.Status == SideFileStatus.Newer` общий для всех
   секций. Пока не нужно (TSK-031 в этом куске нет); при реализации TSK-031 придётся либо принять общий признак, либо запросить `ModSave.IsSectionNewer(featureId)`.

## 3. Расхождения кода со спекой (занести в `design.md`)

1. **Файлы записей контента** читает `ContentMerger.MergeRecords`: массив лежит в поле `records`, а не `families`/`companies`, как в примерах `design.md`
   (разделы «Контент: architectures.json» и «ai-preferences.json»). В коде и во встроенных файлах — `records`.
2. **`balance.json`**: у уровня поддержки `off` поле `rate` равно `0.0`, а не `null` (биндер ядра оставляет значение по умолчанию при `null`; `0` означает «затухание по `decayRate`
   семейства»); у расширения `smt` добавлено `minCores: 1` (поле есть у `ExtensionDef`). Классы `RatingConfig`, `SeedConfig`, `AiConfig`, `ExtensionDef` из `design.md` созданы здесь
   (`Logic/RatingConfig.cs`, `Logic/BalanceConfig.cs`); поля — `lowerCamelCase` в виде полей, как ключи JSON, потому что `ModJson.Bind` связывает по точному имени.
3. **Пути тестов**: `mod/tests/PTMod.Tests/Features/CpuArchitectures/` (так их отбирает `build_feature.py`), а не `mod/tests/PTMod.Tests/CpuArchitectures/` из `tasks.md`/`design.md`.
   Имена тестовых классов те же: `ArchCatalogValidatorTests`, `ContentDefaultsTests`, `SaveDataTests`.
4. **Строки**: `L10n/ru/cpu-architectures.json` и `L10n/en/cpu-architectures.json` (файлы фич, FR-25 п. 2) вместо `L10n/ru.json` и `L10n/en.json`. Ключи те же (`arch.*`); уже есть
   `arch.feature.*`, `arch.param.*`, `arch.hub.*` и `arch.family.<id>.name/.desc` для семи семейств.
5. **`ArchRegistry`**: `Bind` возвращает `bool`; ожидающие привязки хранятся как `PendingSave` (то же, что в секции), а не как `CpuFingerprint` — тип `CpuFingerprint` появится в TSK-008, там же
   `AddPending(CpuFingerprint, …)`/`TakePending(CpuFingerprint)` станут обёртками над `AddPending(PendingSave)`/`TakePending(Predicate<PendingSave>)`. `GetOrSeed(ICompany)` и `ResolveCpu(Cpu)`
   добавляют TSK-014/TSK-008. `MigrationNeeded` равно `!Seeded` (пустое состояние после `ResetToDefaults` и секция с `seeded: false`); `MarkSeeded()` ставит `seeded: true` (его вызывает посев, TSK-015).
6. **Загрузка секции** лечит значения: прогресс и поддержка вне диапазона приводятся к границам (NaN — к 0), неизвестное слово `pauseReason`/`tier` — к `none`/`off`, запись без пригодного `id`
   отбрасывается; всё с предупреждением в лог. Лимиты: 5 000 привязок CPU и 200 ожидающих проектов (как в спеке), дополнительно 1 000 компаний, 64 семейства на компанию, 1 000 весов
   `aiOverrides`, 100 вех — защита от враждебного файла.
7. **`ArchCatalogValidator`** требует от записи ещё: `isa` — `[a-z0-9_]{1,16}`, `requires` не длиннее 8 и без повторов, `fade.min` в [0,5; 1,0], `support.decayRate` в [0; 1], `support.targetCap` в [0; 100],
   точки `eraCost` с возрастающими годами и множителем в [0,1; 5]; блок `research` обязателен, `design` и `support` — нет (по умолчанию 1/1/1 и нулевое содержание).

## 4. Замечание по общему прогону тестов

`python mod\build_feature.py cpu-architectures --tests` собирает и запускает все тесты ядра и других фич, кроме тестов чужих фич из `Features/<другая>/`. На момент сдачи 99 тестов фичи и все остальные зелёные, кроме
`CreditMathTests.Sources_OnlyTheCreditClasses_ReadCentralBankDebtLimit`: его сканер видит чужой недописанный код `Features/BankruptcyRecovery/Logic/RecoveryRules.cs` (читает `CentralBank.DebtLimit` вне `ModCredit`).
К `cpu-architectures` это не относится (в её коде `CentralBank` не упоминается), итоговому интегратору — проверить после завершения `bankruptcy-recovery`.

# Кусок 2 (TSK-004 … TSK-009)

Математика исследований и правила лаборатории, сервис ежедневного исследования, математика рейтинга, провайдер `market.rating.cpu`, отпечатки проектов и привязка CPU,
список «Архитектура» в конструкторе. Блокирующих запросов к ядру и чужим фичам нет; ниже — что видеть интегратору и что занести в `design.md`.

## 5. Для интегратора

1. **Архитектурный тест на собранную DLL в сборке одной фичи.** `build_feature.py <slug> --tests` передаёт тестам `PTMOD_BUILD_DIR = mod/build/features/<slug>/`, где лежит
   `PTMod.dll` только с ядром и этой фичей. Пока эта DLL старше хотя бы одного исходника (обычно — недописанного кода другой фичи), `ArchitectureTests.BuiltDll_*`
   пропускаются; когда она свежее всех исходников, `BuiltDll_EmbedsContentAndL10nResources` падает со списком контента и строк ДРУГИХ фич (их в этой DLL нет по
   замыслу `build_feature`). Файлы `cpu-architectures` в ней есть. На момент сдачи: 1263 теста, из них 150 тестов фичи — все зелёные; в разных прогонах падал
   один чужой тест: либо этот (DLL фичи свежее всех исходников), либо `CreditMathTests.Sources_OnlyTheCreditClasses_ReadCentralBankDebtLimit` (сканер видит
   `CentralBank.DebtLimit` в недописанном `Features/BankruptcyRecovery/RecoveryService.cs`, см. п. 4). Предложение: `build_feature.py --tests` не должен давать тестам свою частичную DLL (например, `PTMOD_BUILD_DIR` на папку без `PTMod.dll`, тогда тесты DLL
   пропускаются), полная проверка DLL — в `python mod\build.py --tests`. Свои файлы тестов это не затрагивает.
2. **Общая лаборатория до TSK-034 неполна** (так и задумано разбиением задач: «семантику общей лаборатории добавляет TSK-034»). `Services/LabArbiter.cs` сейчас
   стоит только на публичном API игры: `StatusOf`, `IsOccupiedByArchitecture`, `CheckOccupy` (подтверждение игроку при технологии в слоте), `Occupy`
   (запомнить технологию игрока в `ResumeTechnologyId`, `ResearchSector.StopResearching`), `Release` (вернуть технологию игрока через `SetCurrentResearch` и
   `ModNotify arch.lab.returned`). Нет: арбитра на `TickPhase.EarlyTick`, `LabUiSync` (панель `CurrentResearchUI` не скрывается), префикса
   `AIBehaviourResearch.ChooseANewResearch` (ИИ после `StopResearching` может сразу выбрать технологию — тогда идут обе), диалога `ModUI.Confirm`,
   `ILabOccupancySource`. TSK-034 дополняет этот класс, а не создаёт новый.
3. **Порядок месячного обработчика.** `ArchitecturesFeature.OnMonthPassed` — единственный обработчик `TickPhase.MonthPassed`; сейчас в нём только сброс кеша
   рейтинга (последним). Экосистема, поддержка, роялти и решения ИИ (TSK-017, TSK-022, TSK-024, TSK-035) вставляются перед ним.

## 6. Расхождения кода со спекой (занести в `design.md`)

1. **`CpuFingerprint`** — чистый `readonly struct` в `Logic/` (`CompanyId`, `Name`, `Year`, `Ips` как свойства; `Matches` с относительным допуском 1e-4 от большего
   значения; `ToPending`/`FromPending`; `IsUsable`). Фабрики из типов игры (`From(SaveObject.CpuProject)`, `From(Cpu)` в `design.md`) вынесены в
   `CpuArchBinder.FingerprintOf(...)`: `Logic/` не может ссылаться на `Assembly-CSharp`.
2. **`ArchRegistry`**: `AddPending(CpuFingerprint, familyId)` возвращает `bool`, `TakePending(CpuFingerprint)` — id семейства или null (FIFO). Добавлены
   `ResolveOrBind(cpuSaveId, fallback)`, `IsBindableCpuId` (id ≥ 1: у `Cpu` без id по умолчанию 0) и заглушка `DefaultFamilyFor(companyId, cpuYear)` — активное
   семейство владельца, если оно существовало в год CPU, иначе `proprietary`; TSK-014 заменяет её на `ArchSeedPolicy.DefaultForCpu`. **Важно:** ленивое разрешение
   (`ResolveCpu`) запоминает привязку только после посева (`Seeded`): до миграции старого сейва (FR-8.3) значение по умолчанию ещё не известно, и запомненная
   заглушка помешала бы миграции. Явные привязки (по проекту) пишутся всегда. Игровая половина `State/ArchRegistry.ResolveCpu(Cpu)` идёт по `Cpu.Base`
   (до 8 уровней, защита от цикла).
3. **Привязка вариантов**: у CPU с `Cpu.Base` ожидающий проект тоже вынимается из очереди (чтобы не висел), но семейство берётся у базового CPU.
4. **`DesignContext` фичи** (`Designer/DesignContext.cs`, как в `design.md`) совпадает по имени с `PTMod.Core.DesignContext` (контекст P19). Внутри
   пространства имён фичи имя означает класс фичи; `DesignCostProvider` (TSK-010) должен брать контекст ядра через `ModifierContext.Design` (тип выводится) или
   писать `PTMod.Core.DesignContext` полностью. Добавлены `SelectedForPlayer()` (без аллокаций, для постфиксов, которые зовутся каждый кадр), `FamilyFor(ICompany)`,
   `ForCompany(ICompany)`, `Neutral`, `Reset()`.
5. **Список конструктора**: чистая часть — `Logic/DesignerChoices.cs` (опции в порядке каталога: исследованные + исследуемые сразу, `proprietary` всегда;
   выбор по умолчанию — `Active` игрока, иначе самое новое; при равном годе — позже в каталоге) с тестами `DesignerChoicesTests` (в `design.md` её нет).
   Виджет — `ModUI.Dropdown` (клон `Widget.Dropdown`), а не клон `coreCountDropdown`, как в таблице патчей: так он получает тему и защиту ядра.
   **Размещение — эвристика** (дампа иерархии окна `CreatorCpu` в `pt-modding` нет): если родитель `coreCountDropdown` — строка с `HorizontalLayoutGroup`,
   новая строка встаёт после этой строки у деда; если у родителя есть `LayoutGroup` — следующим ребёнком после списка ядер; иначе — те же якоря, на строку ниже.
   В `Player.log` пишется строка «designer: the list of architectures is in the CPU designer (… in the layout of / placed under …)». Проверить в игре; если
   положение неудачное — нужен дамп иерархии окна (`tools/dump_ui_templates.py`) и точный путь.
6. **`RatingInputs.Ecosystem`** < 0 означает «не измерено» (`f_eco` = 1) — пока нет `EcosystemService` (TSK-024). Провайдер на этапе 2 берёт поддержку
   у ВЛАДЕЛЬЦА CPU (`Cpu.Company`); на этапе 1 `f_sup` = `f_eco` = `f_ext` = 1. Год для `fade` — сегодняшний (`ModTicks.Today`), не год CPU.
7. **`ArchResearchService`**: результат команд — `StartResult` (`Started`, `Completed`, `NeedsConfirmation`, `AlreadyResearched`, `Locked`, `UnknownFamily`,
   `NothingToResume`, `Unavailable`, `NoCompany`); `Stop` возвращает `bool`, `SetFunding` — записанное значение; `Forecast(company, familyId)` даёт для панели
   дни, прогресс в день, ETA и стоимость в месяц. Пауза по дефолту (игрок — `IsInDefault`, ИИ — деньги < 0) снимается сама, когда дефолт прошёл; другие причины
   паузы — только кнопкой. Семейство с `baseDays` 0 исследуется сразу и лабораторию не занимает. ИИ при старте получает `ResearchSector.Funding` по таблице
   сложности. При завершении: `Support` поднимается до `support.startOnResearch` (наследование ISA — TSK-025), событие `OnFamilyResearched`, `ModNotify`
   игроку (если включены уведомления).
8. **Самопроверка**: в часть `Research` добавлены 6 вызываемых методов (`ResearchSector.IsUnlocked/IsLocked`, `ResearchMath.ResearchSpeed`,
   `Modifiers.GetModifier`, `ResearchDataProvider.GetTechnology/GetAllTechnologies`; сверены рефлексией с `Assembly-CSharp.dll`). Патчи `BinderPatches` и
   `DesignerPatches` — `Optional`: их потеря выключает часть, а не фичу.
9. **Новые ключи** (`L10n/ru|en/cpu-architectures.json`): `arch.designer.label`, `arch.designer.tip`, `arch.reason.rating`, `arch.notify.researched`,
   `arch.notify.pausedDefault`, `arch.lab.returned`.
10. **Пути тестов**, как в куске 1: `mod/tests/PTMod.Tests/Features/CpuArchitectures/` — `ArchResearchMathTests`, `LabRulesTests`, `ArchRatingMathTests`,
    `RatingStackingTests` (вклад 1,30 с вкладами других фич через `ChannelRegistry` ядра и объявление канала из `core.json`), `CpuFingerprintTests`,
    `DesignerChoicesTests`.

# Кусок 3 (TSK-010, TSK-011)

Множители дизайна для игрока и ИИ (патчи конструктора и ИИ, провайдер `development.cost.design`, строка прогноза рейтинга) и инспектор со строками
`research-tree-ux` (поле «Architecture», строки сравнения, группа в «Все модификаторы»). Блокирующих запросов к ядру и чужим фичам нет.

## 7. Для интегратора

1. **Окно сравнения и панель модификаторов `research-tree-ux` на момент сдачи ещё не написаны** (в `Features/ResearchTreeUx/` нет ни `CompareWindow`, ни вызовов `GetRows`/`GetGroup`). `ArchCompareRows` и
   `ArchModifierLines` зарегистрированы в `Register` через `ResearchTreeUxApi.RegisterCompareRows` / `RegisterModifierLines` (при неактивной фиче записываются и
   отдаются позже, это штатная работа `DeferredResearchTreeUxApi`). Проверить при интеграции: окно сравнения вызывает `GetRows(a, b)` и переводит
   `CompareRow.LabelKey` через `ModL10n.L` (ключи `arch.compare.arch`, `arch.compare.rating`), панель модификаторов вызывает `GetGroup(company)` и переводит
   `TitleKey` (`arch.mods.title`) и `ModifierLine.LabelKey` с `Args` (ключи `arch.mods.*`); `Multiplier` задан у строк `arch.mods.ipc|power|devCost`.
2. **Порядок строк сравнения**: «Архитектура» — `Order` 205, «Множитель рейтинга» — 210, то есть после встроенных (10…200, `Content/research-tree-ux/ui.json`). Это текстовые
   строки без подсветки (`Better.None`, `NumA`/`NumB` пусты). Если владельцу нужно выше (например, сразу после IPS), достаточно поменять две константы в `UI/ArchCompareRows.cs`.
3. **Проверить в игре** (игра не запускалась): положение строки «Рейтинг по сегментам» под списком архитектур в окне `CreatorCpu` (то же эвристическое размещение, что и у
   списка, см. п. 6.5; при неудаче строка просто окажется не там, остальное работает); сценарии AC-4.1 (IPC 10 → 10,5; 20 Вт → 22 Вт; 50M → 57,5M; температура и брак),
   AC-4.2, AC-4.3 (CPU Intel на x86 с IPS на 5 % выше; нужен посев ИИ из TSK-014/TSK-015: до него у ИИ нет активного семейства и множители не применяются);
   «Architecture» в инспекторе для CPU игрока и ИИ; время провайдера ≤ 5 мкс и постфиксов ≤ 0,05 мс/кадр.
4. **Что видно сейчас**: пока нет посева (TSK-014/TSK-015) у ИИ нет `Active`, поэтому `DesignContext.FamilyFor` для них даёт null и их CPU и проекты ванильные; для игрока
   множители работают с выбранным в списке исследованным семейством (в начале игры доступна только `proprietary` с множителями 1/1/1, остальное появится после TSK-005
   и исследования x86).

## 8. Расхождения кода со спекой (занести в `design.md`)

1. **`DesignCostProvider`** — не `static class` с `static Provide`, а `internal sealed class` с экземпляром и `Register()`/`ResetReasons()`: массивы `ReasonArgs` с названием
   семейства хранятся по семействам (без аллокаций на вызов) и сбрасываются при смене языка, как у `ArchRatingProvider`. Провайдер зарегистрирован один раз в `Register`.
2. **Постфиксы конструктора вызываются каждый кадр**, поэтому в `DesignerPatches.PostfixConsumptionAndTemperature` / `PostfixPerformance` и в `AiDesignPatches` вместо
   `ModHooks.Guard(лямбда)` стоят `ModHooks.CanRun` + `try/catch` + `ModHooks.ReportFault`: без замыкания на кадр, поведение защиты то же (`Guard` у остальных патчей).
3. **`DesignContext`** (папка `Designer/`): добавлен `ForPlayer()` (множители выбранного игроком семейства, без аллокаций); `FamilyFor` теперь проверяет и часть `Designer`
   самопроверки для ИИ (раньше — только для игрока), чтобы стоимость проектов, IPC и мощность ИИ выключались одной частью. `ImmatureCostFactor` из `design.md` не заведена:
   провайдер зовёт `DesignMath.CostFactor(design, 0f)`, параметр `immaturity` заготовлен (TSK-025 подставит наценку незрелых инструментов этапа 2).
4. **Новые типы `Logic/`** (чистые, под тестами): `DesignMath` (IPC/IPS, мощность, множитель стоимости), `RatingForecast` (вклад семейства в 4 сегмента и числа для текстов; им же пользуется
   `ArchRatingProvider.ContributionOf`, поэтому строка конструктора, строка сравнения и рынок считают одной функцией), `ArchDisplay` (название семейства и «Неизвестная (id)»).
   `ArchRatingProvider` получил публичный `ContributionOf(family, cpu, segment)`; поведение провайдера не менялось.
5. **Ключи L10n**: вместо `arch.designer.rating` («Рейтинг по сегментам: {0}») используются заголовок `arch.designer.ratingTitle` и значение `arch.segments` в `InfoText` (title/value
   — как устроен `InfoText`); добавлены `arch.designer.ratingTip`, `arch.reason.devCost`, `arch.inspector.unknown`, `arch.compare.arch|rating|ratingValue`,
   `arch.mods.title|research|paused|cost|ipc|power|devCost`, `arch.pause.technology|default|noFunds|disabled` (причины паузы в панели модификаторов; TSK-012 может использовать их же в
   карточках). `arch.effects` (карточка семейства) появится с TSK-012. Число в `arch.segments` пишется с запятой для `ru` и с точкой для остальных (`RatingForecast.Format`), без `CultureInfo`.
6. **Самопроверка**: в часть `Designer` добавлена вызываемая игровая функция `CpuMath.Temperature(float, IPackage)`; имена и типы приватных полей, подставляемых Harmony
   (`___consumption`, `___temperature`, `___ipc`, `___ips`, `___ipsWithoutBottleneck`, `___currentPackage`, `___architectureText`), сверены рефлексией с `Assembly-CSharp.dll`.
7. **Тесты** (`mod/tests/PTMod.Tests/Features/CpuArchitectures/`): `DesignMathTests` (AC-4.1, AC-4.2, AC-4.3, нейтральность, негодные множители, наценка незрелости, диапазоны встроенного
   контента), `RatingForecastTests` (совпадение с `ArchRatingMath` на сетке семейств, сегментов, лет и силы, AC-6.1/6.2, примеры таблицы 1,135/1,102/0,857/0,875, ручка 0 и 150 %, тексты),
   `ArchDisplayTests` (название, «Неизвестная (id)», тире; все литералы ключей `arch.*` из кода фичи есть в `ru` и `en`).

# Кусок 4 (TSK-012 … TSK-017)

Панель архитектур (карточки, блок лаборатории, вкладка и окно хаба), политика и посев (новая игра, старый сейв, поздние ИИ), политика и месячный цикл ИИ. Блокирующих запросов к ядру
и чужим фичам нет; ниже — что видеть интегратору и что занести в `design.md`.

## 9. Для интегратора

1. **`research-tree-ux` в этом снимке не вызывает методы вкладок.** В `Features/ResearchTreeUx/` нет вызовов `IResearchTabProvider.BuildContent`, `OnShown`, `OnHidden`, `BadgeCount` (есть только интерфейс и
   `DeferredResearchTreeUxApi`, который запоминает регистрации). `ArchitecturesTab` зарегистрирована в `Register` (`ResearchTreeUxApi.RegisterTab`, `TabId` = `cpu-architectures`, `Order` = 200,
   `IsAvailable` = фича включена) и ждёт полосу вкладок. Проверить при интеграции: окно дерева зовёт `BuildContent(parent)` лениво при первом выборе вкладки (панель берёт высоту корня
   `parent.rect.height - 8`, а для неизвестной — 520 единиц; ширина по родителю), потом `OnShown`/`OnHidden` и спрашивает `BadgeCount()` при перерисовке полосы. Тело каждого члена обёрнуто в `ModHooks.Guard`,
   исключение не выключит вкладку. Фолбэк — окно из хаба (`ArchitecturesWindow`) — работает независимо от `research-tree-ux`.
2. **`Patches/LabPatches.cs` — ИИ-половина TSK-034, сделана здесь, потому что без неё TSK-017 не работает.** Prefix `AIBehaviourResearch.ChooseANewResearch` (необязательный, поле `___company`) пропускает
   выбор технологии, пока архитектура занимает лабораторию этой ИИ (`LabArbiter.IsOccupiedByArchitecture`); `ArchitectureAi.MayResearch` включает исследование архитектур ИИ только при части `Ai`
   самопроверки и `ModHooks.IsApplied(typeof(LabPatches))`. TSK-034 должна расширить файл, а не создавать новый. В ту же сторону `ArchResearchService.TickCompany` останавливает технологию ИИ,
   которую игра выбрала, пока его архитектура стояла на паузе (`LabRules.ResolveConflict` для ИИ). **Не сделано (TSK-034):** арбитр игрока на `TickPhase.EarlyTick` («выбрал технологию → архитектура на паузе»),
   `LabUiSync` и `ILabOccupancySource`. Пока `LabUiSync` нет, панель «Текущее исследование» игры (`CurrentResearchUI.FixedUpdate`) каждый кадр переписывает `ResearchSector.Funding` из своего слайдера, пока показывает
   технологию, — слайдер блока лаборатории и этот слайдер тогда перебивают друг друга; это исчезнет, когда архитектура скроет устаревшую панель.
3. **Вопрос «Остановить технологию?»** уже есть в `ArchitecturesUi` (`ModUI.Confirm`, ключи `arch.lab.confirm.title`, `arch.lab.confirm`) для кнопок «Исследовать» и «Продолжить»: `ArchResearchService.Start/Resume`
   возвращают `StartResult.NeedsConfirmation`, панель спрашивает и зовёт ту же команду с подтверждением. В список TSK-034 диалог входит; доводить там нужно только работу с `LabArbiter`.
4. **Полоса прогресса** — собственная (два `Image`, цвета `Theme.ProgressBackground` / `Theme.ProgressBar`, перекраска по `ModUI.ThemeChanged`): у `ModUI` полосы нет, а `ProcessorTycoon.UI.ProgressBar` собирается
   префабом (поля `fill`, `bar`, `progressText` задаёт Unity). Если ядро добавит `ModUI.ProgressBar`, `ThemedBar` заменяется одним классом.
5. **Посев на `FastForwardEnded`**: `ModCompanies.All` строится ядром на `GameReady` и по событиям спавна; к концу перемотки список может быть неполон, поэтому `ArchSeeder.OnGameReady(NewGame)` ещё раз засевает ИИ, у которых
   нет состояния (повторный вызов безвреден). Проверить в игре: в `Player.log` строки `seed: the fast-forward ended, N AI companies sown` и `seed: player U=…`.
6. **Тесты на DLL.** Как в п. 5.1 куска 2: `ArchitectureTests.BuiltDll_*` пропускаются, пока `mod/build/features/cpu-architectures/PTMod.dll` старше любого исходника, и падает
   `BuiltDll_EmbedsContentAndL10nResources` (список чужого контента), когда DLL фичи свежее всех исходников. Полный прогон 1365 тестов зелёный, если DLL старше исходников (после правки файла фичи) —
   предложение п. 5.1 остаётся в силе.

## 10. Расхождения кода со спекой (занести в `design.md`)

1. **`ArchRegistry`**: конструктор `ArchRegistry(catalog, preferences, seed)` (одноаргументный остался: таблица предпочтений — запасная запись, числа посева — по умолчанию); свойства `Preferences`, `Seed`,
   метод `PreferenceOf(uniqueId)`. Заглушка `DefaultFamilyFor(int companyId, int cpuYear)` убрана: теперь `DefaultFamilyFor(bool isPlayer, int aiUniqueId, int cpuYear)` на `ArchSeedPolicy.DefaultForCpu`; результат
   не зависит от состояния компании. Игровая половина: `DefaultFamilyForOwner(ICompany, year)`, `AiUniqueIdOf(ICompany)` (через `ModCompanies.KeyOf` и `CompanyKey.TryGetAiId`) и
   `GetOrSeed(ICompany)` (+ перегрузка с датой); игрока `GetOrSeed` не засевает (нужен его `ResearchSector`, это `ArchSeeder`).
2. **`ArchSeedPolicy`**: помимо `SeedAi`, `SeedPlayer`, `DefaultForCpu` публичны `Closure` (замыкание по `requires`) и `PathOf` (`proprietary` + `seed.playerPath`); `SeedResult.ApplyTo(state)` — идемпотентная запись
   в `CompanyArchState`. `DefaultForCpu` для ИИ не требует исследования семейства: выбирается по весу и году CPU (спека: «для ИИ — семейство с наибольшим весом среди доступных по году выпуска CPU»).
3. **`ArchAiPolicy`**: в `AiView` добавлены `IsFoundry`, `IsBankrupt` (спека пропускает foundry в службе; чистая политика теперь тоже возвращает пустое решение для них), `AiDecision.IsEmpty`. Публичны
   `PickActive` (две перегрузки: по множеству и по словарю состояния), `PickCandidate`, `Reserve`. Резерв — `reserveMonths × monthlyCost × M4` БЕЗ множителя финансирования (как в `requirements.md`, FR-7 п. 1).
   `DecideMvp` не знает про этап 2 (`Tiers` = null).
4. **ИИ и ARM для Intel**: по `ai-preferences.json` у Intel вес ARM 0,1 (> 0), поэтому после x86 Intel с 1987 года исследует ещё и ARM (резерв 6 × 8M), а с 2003 x86-64. Это следует из правила «вес > 0 — кандидат»,
   а не из особого случая в коде; если владельцу это не нужно, достаточно обнулить веса в контенте.
5. **Панель**: в `design.md` нет `ArchitecturesUi` (владелец вкладки, окна, панелей, счётчика и команд), `PanelKit` и `ThemedBar`, а также чистых `FamilyCardModel`, `LabBlockModel`, `NewFamilyTracker`
   (`Logic/`, под тестами `PanelModelTests`). `LabStatusBlock`, `FamilyCard`, `ArchitecturesPanel`, `ArchitecturesTab`, `ArchitecturesWindow` — как в design.md (`ArchitecturesPanel.Build(parent, height)` вместо `Build(parent)`:
   панели нужна высота прокручиваемой области). Окно хаба: `ModUI.CreateWindow("cpu-architectures.window", WindowTemplate.Panel, "arch.window.title", 820×680)`.
6. **Счётчик вкладки (FR-3 п. 7)**: «новое доступное семейство» — не исследовано, требования исследованы, год семейства наступил (`Year ≤` года даты) и оно не из исследуемых сразу. Первый набор после старта игры или
   загрузки — базовая линия (не новость). Счётчик хранится только в памяти (в `ModSave` полей для него нет), после перезагрузки начинается заново.
7. **Ключи L10n**: `arch.tab.title`, `arch.window.title`, `arch.hub.intro|open|openTab`, `arch.panel.hint|families|error`, `arch.lab.free|tech|arch|paused|numbers|funding|fundingTip|confirm|confirm.title`,
   `arch.card.research|resume|stop|done|meta|numbers|affinity`, `arch.effects`, `arch.status.researched|available|availableProgress|inProgress|paused|locked`, `arch.isa.own|x86|risc|arm|riscv`;
   `arch.hub.stub` убран. Названия ISA берутся по ключу `arch.isa.<isa>`, для ISA из пользовательского контента без ключа показывается сам `isa`.
8. **Самопроверка**: в часть `Research` добавлены `Technology.get_Year`, `ResearchSector.get_Funding|set_Funding|get_InnovationEffort`; в `Binder` — необязательный `CpuDataProvider.GetAllCpus()`
   (миграция существующих CPU). Остальные члены, которые зовёт новый код, проверяет компилятор (сборка идёт против `Assembly-CSharp.dll` игры).
9. **Посев `proprietary`**: `ArchSeedPolicy.SeedAi/SeedPlayer` всегда добавляют `proprietary` (даже при весе 0 или U = 0), `Active` у ИИ — только среди семейств с весом > 0 (иначе `proprietary`).
10. **Тесты** (`mod/tests/PTMod.Tests/Features/CpuArchitectures/`): `ArchSeedPolicyTests` (AC-8.1, AC-8.2, замыкание, `default`, задержка, начальная поддержка, `ApplyTo`), `ArchAiPolicyTests`
    (AC-7.1…AC-7.3, резерв и M4, граница 30 дней, пауза, ничьи, foundry, детерминизм), `PanelModelTests` (карточки, слайдер, счётчик); `ArchTestKit` получил `BuiltInPreferences`, `BuiltInBalance`, `BuiltInRegistry`;
    в `CpuFingerprintTests` тест заглушки заменён тестом политики.

# Кусок 5 (TSK-018, TSK-019, TSK-034)

Общая лаборатория целиком (арбитр на `EarlyTick`, панель `CurrentResearchUI`, занятость для `research-tree-ux`), выключение фичи посреди игры и тест ключей строк. Блокирующих запросов к ядру и чужим фичам нет;
ниже — что видеть интегратору и что занести в `design.md`.

## 11. Для интегратора

1. **`research-tree-ux` и занятая лаборатория.** `Services/LabOccupancySource` (`ILabOccupancySource`) регистрируется в `Register` через `ResearchTreeUxApi.RegisterLabOccupancy`. Он говорит «занята», пока архитектура
   игрока — текущее исследование и не на паузе (`LabArbiter.IsOccupiedByArchitecture`), текст — `arch.lab.busyText` («Архитектура «x86» — 43 %»). Проверить при интеграции, что список исследований показывает строку
   «Лаборатория занята: …» над строками, а ежедневное напоминание о простое молчит, пока архитектура идёт; при паузе архитектуры напоминание вправе вернуться. Исключение из любого члена источника выключит его до
   перезапуска (поэтому оба члена обёрнуты в `ModHooks.Guard`).
2. **Что игра делает с панелью «текущее исследование» (`CurrentResearchUI`).** Пока в панели есть технология (`targetTechnology`), `FixedUpdate` каждый кадр пишет позицию её слайдера в `ResearchSector.Funding`.
   Поэтому `Services/LabUiSync` (часть `LabUi` самопроверки) при занятии лаборатории игрока обнуляет приватное `targetTechnology` и зовёт приватный `Hide()`, а при возврате технологии зовёт необязательный
   приватный `LoadFunding()` (слайдер панели встаёт на `Funding`, который игрок выставил в блоке лаборатории), затем публичный `SetTechnology(Technology)` и публичный `ResearchTreeWindow.ForceUIUpdate()`
   (кнопки дерева перерисовываются сразу, а не на следующий день). Объекты `CurrentResearchUI` и `ResearchTreeWindow` ищутся `FindFirstObjectByType(Include)` и кешируются до смены сцены. Ошибки этого слоя пишутся в лог
   одной строкой на вид вызова и НЕ идут в счётчик сбоев (`ReportFault`): это best-effort синхронизация чужого UI, пять таких сбоев не должны выключить фичу. **Проверить в игре:** запуск архитектуры при идущей
   технологии (панель исчезает, слайдер блока лаборатории не «отскакивает»), возврат технологии после завершения (панель показывает её, ползунок на прежнем месте).
3. **Выключение фичи.** `OnDisabled` теперь: ставит исследование архитектуры игрока на паузу с `PauseReason.FeatureDisabled` и возвращает лабораторию прерванной технологии (без уведомления: `ModNotify`
   игнорирует неактивные фичи), прячет список конструктора, закрывает окно хаба и сбрасывает вкладку, сбрасывает кеш каналов `market.rating.cpu` и `development.cost.design`. Реестр, провайдеры и секция сейва не
   трогаются. Каждый шаг в своём `ModHooks.Guard`, чтобы сбой одного не оставил остальные несделанными. При включении ничего не возобновляется: пауза `FeatureDisabled` снимается только кнопкой «Продолжить»
   (`LabRules.AutoResumes` истинна лишь для паузы по дефолту). ИИ при выключении не трогаются: пока фича выключена, игра выбирает им технологии как обычно (патч `ChooseANewResearch` снят), а при включении арбитр
   на `EarlyTick` останавливает такую технологию (прогресс остаётся в игре), если у ИИ идёт архитектура.
4. **Ранние выходы.** Каждый обработчик патча начинается с проверки `ArchitecturesFeature.NeutralNow` (выключена фича или идёт перемотка новой игры; `NeutralNow` истинна и до создания фичи). Патчи при
   горячем выключении снимает ядро, но `ModHooks.CanRun` для выключенной (`Disabled`) фичи истинна, так что проверка в самом обработчике нужна. Это проверяет `PatchGuardsTests` по тексту исходников.
5. **Тесты на DLL** — как в п. 5.1 и 9.6: пропускаются, пока `mod/build/features/cpu-architectures/PTMod.dll` старше любого исходника (на момент сдачи — старше `DisableSettlementEngine.cs` соседней фичи). Полный
   прогон фичи: 1412 тестов, 298 из них — тесты `CpuArchitectures`, все зелёные.

## 12. Расхождения кода со спекой (занести в `design.md`)

1. **`LabArbiter`**: `Occupy(ICompany, CompanyArchState)` и `Release(ICompany, CompanyArchState, bool notify = true)` берут состояние компании, а не `familyId`/`PauseReason`, как в схеме Component 4. Добавлены
   `Arbitrate(company, state)` (общий для `EarlyTick` и второго взгляда в дневном тике `ArchResearchService`), `CheckOccupy` (шлюз перед занятием: подтверждение / «сначала остановите технологию»),
   `TechnologyBlocksStart`, `LabUiWorks`, `OnFeatureDisabled`. `StatusOf` показывает приостановленную архитектуру приостановленной, даже когда в слоте технология (`LabRules.StatusForPanel`), чтобы блок
   лаборатории сохранял кнопку «Продолжить» (`LabRules.Status` оставлен как был).
2. **`LabRules`** (чистая логика, под тестами): `Gate` + `StartGate` (`Open` / `NeedsConfirmation` / `StopTechnologyFirst`), `TechnologyBlocksStart`, `StatusForPanel`, `AutoResumes`, `Arbitrate(state, slotHasTechnology)`
   (применяет решение к состоянию: пауза `TechnologyChosen` у игрока), `Remember`, `TakeReturnTarget`. Новый тип `LabSwitch.Disable(state, slotHasTechnology)` → `DisableOutcome { Paused, GiveBack }`
   (выключение фичи, FR-9.2). В `design.md` их нет.
3. **Часть `LabUi` без приватных членов панели**: `ArchResearchService.Start/Resume` возвращают новый `StartResult.StopTechnologyFirst`, если у игрока в лаборатории технология, — вместо подтверждения; карточки и
   кнопка «Продолжить» неактивны с подсказкой `arch.lab.stopTechFirst` (`FamilyCardInfo.BlockedByTechnology`, `FamilyCardModel.Of(..., technologyBlocks)`). Без `LabUi` пустая лаборатория занимается как обычно.
   В самопроверку части `LabUi` добавлены публичные `CurrentResearchUI.SetTechnology(Technology)` (обязательно) и `ResearchTreeWindow.ForceUIUpdate()` (необязательно).
4. **Арбитр на `EarlyTick`** регистрируется в `Register` (`ctx.OnTick(TickPhase.EarlyTick, _lab.OnEarlyTick)`); он обходит `ModCompanies.All` (≤ 14), пропускает компании без бегущей архитектуры. Для ИИ остановленной
   технологии возвращается финансирование по сложности (`ArchResearchService.ApplyAiFunding`), потому что `AIBehaviourResearch.AvaliateFunding` успела переписать `Funding` под технологию.
5. **Ключи L10n**: добавлены `arch.lab.pausedTech` (два аргумента: архитектура и технология; в design.md был один), `arch.lab.busyText` (`Архитектура «{0}» — {1} %`: процент, а не готовая строка), `arch.lab.stopTechFirst`.
   Остальные ключи этапа 1 уже были (`arch.lab.*`, `arch.reason.*`, `arch.notify.*`, `arch.pause.*` и др.).
6. **Тесты** (`mod/tests/PTMod.Tests/Features/CpuArchitectures/`): `SharedLabTests` (шлюз и подтверждение, AC-2.3, AC-2.4, AC-2.2, ИИ сохраняет архитектуру, возврат лаборатории и случаи, когда вернуть нельзя, карточки при
   `StopTechnologyFirst`), `FeatureSwitchTests` (AC-9.1, AC-9.2, пауза не снимается сама, ИИ не трогаются, реестр не пересоздаётся, CPU без привязки получают семейство по умолчанию, ручка силы 0 % и `m′ = 1 + (m − 1)k`),
   `PatchGuardsTests` (по исходникам: ранний выход первой строкой в каждом патче, все патчи `Optional`, нет патчей на закреплённые ядром цели, каждый класс патчей объявлен один раз, префикс ИИ, `EarlyTick`,
   `RegisterLabOccupancy`, `OnDisabled` ничего не создаёт и не очищает, параметры хаба), `L10nKeysTests` (TSK-019: одинаковые ключи в ru/en через `L10nTables.Compare`, пустых и «сырых» нет, в en нет кириллицы, префикс `arch.`
   по `ModIds` и таблице `core.json`, каждый ключ кода и контента (`nameKey`, `descKey`, `arch.isa.*`) есть в обоих файлах, число аргументов каждого вызова `ModL10n.L` / `ModNotify.ShowKey` / `Line` совпадает с
   плейсхолдерами в ru и en, у каждого ключа таблицы есть пользователь).
7. **Известные упрощения.** (а) Слайдер финансирования в блоке лаборатории при приостановленной «из-за технологии» архитектуре можно двигать, но панель технологии игры каждый кадр возвращает своё значение —
   слайдер «отскакивает», пока технология в лаборатории. (б) Ожидающие проекты (`Pending`), чьи CPU вышли, пока фича была выключена, остаются в очереди (до 200, самые старые вытесняются) — CPU получает семейство
   по умолчанию, как требует FR-9.3. (в) Уведомление `arch.lab.pausedTech` приходит в `EarlyTick` следующего дня после выбора технологии (AC-2.4), а не сразу.

## 13. Для интегратора (кусок 6: программная поддержка, TSK-021…TSK-023)

1. **Этап 2 выключен контентом.** Встроенный `Content/cpu-architectures/balance.json` по-прежнему `"stage": 1` (гейт владельца TSK-020 не пройден, поэтому я его не менял). При этапе 1 `SupportService.CanRun`
   ложно: ни списаний, ни обновления уровней, раздел «Программная поддержка» скрыт, а `f_sup` в рейтинге = ×1 (`RatingForecast.One` берёт `mvpSupport`). Чтобы увидеть поддержку в игре, нужен пользовательский
   файл `<папка игры>/PTMod/content/cpu-architectures/balance.json` с `{ "schema": 1, "stage": 2 }` (ядро сливает его со встроенным). Для финальной сборки владелец решает, поднимать ли `stage` во встроенном файле.
   Тест `SupportSectionModelTests.TheBuiltInBalanceFile_StartsAtStageOne_...` фиксирует текущее значение — при смене этапа его надо поправить вместе с `ContentDefaultsTests`.
2. **Месячный порядок.** `ArchitecturesFeature.OnMonthPassed` (единственный обработчик `TickPhase.MonthPassed` у фичи): `_support.OnMonth` (на собственном `ModHooks.Guard`, сайт `feature/month/support`) → `_ai.OnMonth` →
   `_rating.InvalidateAll`. Места для экосистемы (TSK-024) и роялти (TSK-035): `EcosystemService` — перед `_support` (по design.md), `RoyaltyService` — после него, оба перед ИИ.
3. **Новая часть самопроверки `ArchParts.Support`** (= 256): `ICompany.SpendMoney(float, ExpenseOrigin)`, `get_IsInDefault`, `get_MoneyAmount`, `InflationHandler.get_Instance`/`get_Inflation`; все члены сверены рефлексией
   с `Assembly-CSharp.dll`. Поддержка работает только при частях `Support` и `Market` (без канала рейтинга платить за поддержку незачем). В итоговой строке самопроверки часть стоит последней.
4. **Патчей и запросов к ядру нет.** `SupportService` не патчит игру и не подписывается ни на какие её события; деньги — `ICompany.SpendMoney(sum, ExpenseOrigin.Development)` один раз на компанию в месяц.
5. **Проверить в игре (не запускалось):** при `"stage": 2` — раз в месяц одна строка расхода в статье «Development» (по ней же видно, что игрок платит за «обычный» уровень самого нового семейства сразу после посева);
   дефолт игрока → все уровни «выкл» и одно уведомление (M14); строка «Рейтинг по сегментам» в конструкторе и `Explain` канала `market.rating.cpu` показывают `f_sup`; раздел в панели: шкала, список уровня, стоимость,
   прогноз, подсказка (M9: «Высокая» → стоимость ×2, рост к 95, прогноз = факту ±1); подпись списка уровня при смене языка (строка пересоздаётся); раскладка списка внутри карточки.
6. **Тесты на DLL** — как в п. 5.1, 9.6 и 11.5. Полный прогон фичи: 1482 теста, 70 из них новые (`SupportMathTests`, `SupportCycleTests`, `SupportSectionModelTests`, `SupportRatingTests`, `SupportWiringTests`).

## 14. Расхождения кода со спекой и решения (занести в `design.md`)

1. **`SupportMath`**: вместо `TierTable` берётся уже существующий `SupportConfig` (`balance.support`, `Tier(SupportTier)`). Сверх схемы Component 10 добавлены `Target`, `Rate`, `Clamp`, `SafeInflation`, `Score`,
   `PercentOf` и константы `MaxSupport`, `ForecastMonths` (= 12). Ставка уровня «выкл» — `decayRate` семейства (`tiers.off.rate` из файла не читается, спека: «r = decayRate семейства»); `targetCap` ограничивает цель
   всех уровней, кроме «выкл». Точки `eraCost` соединяются отрезками, до первой и после последней держатся крайние значения; пара не из двух чисел пропускается.
2. **`SupportService` и чистое ядро `SupportCycle`**: правила месяца лежат в `Logic/SupportCycle` (`RunMonth`, `ForceOff`, `SetTier` + `SupportMonthResult`, `SupportSetResult`) — под тестами; сервис в игре только собирает
   вход (дата, инфляция, деньги) и списывает **одну сумму на компанию** (сумма содержания всех её исследованных семейств), а не по строке на семейство. `SetTier` возвращает `SupportSetResult` (в схеме — `void`).
   Порядок семейств при списании — порядок каталога, поэтому результат не зависит от порядка добавления в словарь состояния.
3. **ИИ платит только тем, что у него есть** (в спеке этого нет): если содержание уровня больше остатка денег компании, уровень в этот месяц работает как «выкл» (без списания; сам уровень в состоянии не меняется,
   поддержка затухает по `decayRate`). Игрок платит всегда, даже уходя в минус (FR-10.3 и «Edge cases»). Причина: бедный ИИ не должен банкротиться из-за статьи, которой в ванили нет (NFR-4, «не ломает баланс игры»).
   Если владелец хочет буквальное поведение — передавать в `SupportCycle.RunMonth` `PositiveInfinity` и для ИИ.
4. **Дефолт игрока (FR-10.6)** проверяется раз в месяц, до списания: `SupportCycle.ForceOff` ставит «выкл» всем семействам и возвращает, сколько уровней было не «выкл»; уведомление
   `arch.notify.defaultTiers` — один раз, только если было что отключать, и только при включённых уведомлениях хаба. «До выхода из дефолта» понято так: пока дефолт, уровень нельзя поднять
   (`SupportSetResult.InDefault`, список в панели недоступен, над ним строка «Дефолт: поддержка ПО отключена»); после выхода уровни остаются «выкл», пока игрок не выставит их сам (прежние не запоминаются: поля для
   этого в схеме сейва нет).
5. **Кого пропускаем:** foundry и обанкротившиеся компании (и игрока, и ИИ) не платят и не обновляются; компания без состояния (ИИ, ещё не засеянный) пропускается до следующего месяца — её засевает решение ИИ.
6. **Рейтинг.** `ArchRatingProvider.ContributionOf` уже читал поддержку владельца CPU при `balance.IsFullStage` (кусок 2), поэтому в провайдере код не менялся; добавлены сброс кеша после обновления уровней
   (`SupportService.OnMonth` зовёт `Rating.InvalidateAll`, если какой-то уровень сдвинулся, плюс общий сброс в конце месячного обработчика) и тесты `SupportRatingTests`.
7. **UI.** `UI/SupportSection` (контейнер, строка о дефолте, итог «содержание всей поддержки в месяц») и `UI/SupportRow` (карточка на семейство: название с уровнем «N из 100», `ThemedBar` 0–100, `ModUI.Dropdown`
   с четырьмя уровнями, «стоимость/мес, через 12 мес.: N», «влияние на рейтинг ×…», подсказка `ModUI.Tooltip` с формулами: рейтинг `0,90 + 0,20 × S/100 = ×…`, куда ведёт уровень и с какой скоростью, предел
   семейства, содержание как произведение четырёх сомножителей). Строка создаётся лениво, когда семейство впервые попадает в список (исследованное), и ставится по порядку каталога; при смене языка строка
   пересоздаётся (подпись списка нельзя сменить на месте). Раздел следует за карточками в `ArchitecturesPanel` и скрыт при этапе 1; команда уровня — `ArchitecturesUi.SetSupportTier` →
   `SupportService.SetTier` → панели перерисовываются сразу (стоимость и прогноз показывают новый уровень до следующего месяца); `ArchitecturesUi.OnSupportMonth` перерисовывает видимые панели после месячного обновления.
8. **Ключи L10n** (ru и en, 19 новых): `arch.support.title/.hint/.tierLabel/.tierTip/.row/.cost/.effect/.total`, `arch.support.tier.off/.low/.normal/.high`, `arch.support.tip.rating/.tier/.off/.cap/.cost/.free`
   и `arch.notify.defaultTiers` (он же — строка о дефолте в разделе). Текст `arch.support.cost` по таблице design.md: «{0}/мес, через 12 мес.: {1}»; «12» зашито в тексте и равно `SupportMath.ForecastMonths`.
9. **Ограничения, которые закроют другие задачи.** (а) Уровни поддержки ИИ, кроме посева («активная — обычный, прочие — выкл»), выставляет адаптивная политика TSK-027: пока её нет, у ИИ, сменившего активное семейство,
   новое семейство стоит на «выкл», и его поддержка затухает с начальных 30 (`support.startOnResearch`) — это штраф рейтинга до ×0,90 по `f_sup`. (б) Наследование начальной поддержки от той же ISA —
   TSK-025 (`startOnResearch` = 30 всегда). (в) Экосистема и ворота расширений по-прежнему ×1 (TSK-024, TSK-026).

# Кусок 7 (TSK-024 … TSK-028, TSK-035)

Экосистема, опыт ISA и незрелые инструменты, ворота расширений, адаптивный ИИ, обзор соперников с уведомлениями и роялти за x86. Сборка `python mod\build_feature.py cpu-architectures` чистая,
`--tests` проходит 1583 из 1583 (три `BuiltDll_*` пропущены, как в прошлых кусках). Ядро и чужие файлы не менялись; запросов к ядру и к другим фичам нет.

## 15. Для интегратора (кусок 7)

1. **Этап 2 по-прежнему выключен контентом.** Встроенный `balance.json` остаётся `"stage": 1` (гейт владельца TSK-020 не пройден, поэтому я его не менял). На этапе 1 не работает ничего из этого куска:
   `EcosystemService`, `RoyaltyService`, ворота расширений, надбавка за незрелые инструменты, `DecideFull` ИИ, разделы «Экосистема» и «Соперники», строка роялти и письма скрыты или молчат. Чтобы увидеть всё в игре,
   нужен пользовательский `<папка игры>/PTMod/content/cpu-architectures/balance.json` с `{ "schema": 1, "stage": 2 }` (так же, как для поддержки, п. 13.1). Тесты включают этап в коде (`stage = Stage2`).
2. **Месячный порядок** (`ArchitecturesFeature.OnMonthPassed`, по design.md): экосистема (`feature/month/ecosystem`) → поддержка (`.../support`) → роялти (`.../royalty`) → ИИ → сброс кеша рейтинга. Каждый шаг этапа 2
   на собственном `ModHooks.Guard`. Первое измерение экосистемы делается тихо в `ModTicks.GameReady` (после посева, чтобы у всех CPU уже было семейство) и при включении фичи; в сейв оно не пишется.
3. **Новые части самопроверки** (`ArchParts`): `Ecosystem` = 512 (`CpuDataProvider.Instance`, `GetAllCpus(List<Cpu>)`, `CpuBase.Market`, ряды `CpuBase.MarketData` — `Sold`, `SoldByContracts`, `SoldByHighEnd/MidRange/LowEnd/Industries`,
   `Income`, `Cpu.Company`), `Royalty` = 1024 (`ICompany.SpendMoney/EarnMoney`, `LicenseHelper.Instance`, `X86LicenseHasBeenRevoked`), `Extensions` = 2048 (`CreatorCpu.UpdateNumbers`, поля `currentCoreCount`, `smtEnabled`). Члены
   проверены компиляцией (код обращается к ним напрямую) и по декомпилированному коду игры. Потеря `Extensions` отключает только предупреждение о воротах в конструкторе; потеря `Ecosystem` — экосистему (множитель ×1),
   долю сегментов ИИ (по четверти) и роялти (ему нужны те же ряды).
4. **Новый необязательный патч:** `Patches/ExtensionPatches` — postfix `CreatorCpu.UpdateNumbers` (метод вызывается каждый кадр, пока открыто окно конструктора CPU). Он читает ядра и SMT текущего дизайна через
   инъекцию Harmony (`___currentCoreCount`, `___smtEnabled`) и передаёт `DesignContext.NoteDesign`; строки конструктора пишутся заново только при изменении. Объявлен `ctx.Patch(typeof(ExtensionPatches))`.
5. **Подсказка технологии.** `ResearchTreeUxApi.RegisterTechTooltipLines(new ArchTechTooltipLines(this))` в `Register`. Проверить, что `research-tree-ux` в итоговой сборке зовёт `GetLines` при построении подсказки
   (в снимке при кусках 4–5 вкладки, строки и подсказки она ещё не опрашивала).
6. **`ArchitectureApi` создан** (`Features/CpuArchitectures/Api/ArchitectureApi.cs`, по design.md Component 15: `IsActive`, `GetArchitectureId`, `GetActiveArchitectureId`, `GetRatingMultiplier`, `TransferCompanyState`,
   `SetAiWeightOverride`, события `OnFamilyResearched` и `OnActiveChanged`). `rival-acquisitions` ищет `PTMod.Features.CpuArchitectures.ArchitectureApi.TransferCompanyState(ICompany, ICompany, bool)` по имени —
   после полной сборки проверить в `Player.log` строку `related features: ArchitectureApi.TransferCompanyState found` (см. `rival-acquisitions.md`, п. «Смежные фичи»). Слияние состояния — чистая `Logic/StateTransfer`
   (исследованное объединяется, поддержка — максимум при `mergeKeepBest`, незаконченное исследование, активное семейство и уровни не переносятся); тесты `StateTransferTests`. Сам TSK-031 (совместимость сейвов) остаётся
   за следующим куском: ему нужно лишь дописать проверки в `SaveDataTests`.
   `SetAiWeightOverride` пишет в секцию сейва (`aiOverrides`, до 1000); вызова из `rival-personalities` в дереве пока нет — стоит проверить при интеграции.
7. **Проверить в игре (не запускалось), при `"stage": 2`:** (а) панель: разделы «Экосистема» (по семейству «x86: 62 % (+4 п. п. за год)», итог «Всего: 100 %», подсказка с формулой), «Роялти x86» и «Соперники» после раздела
   поддержки; (б) конструктор: строки «Незрелые инструменты: +N % …» (первый год после исследования семейства) и «Расширения: Многоядерность раскроется при поддержке ≥ 40 (сейчас N)» при 2+ ядрах или SMT, строка «Рейтинг по
   сегментам» уже с ядрами и экосистемой; (в) подсказка технологии Dual Core…Octa Core и SMT в дереве; (г) M11 (x86-64 при поддержке x86 = 80: время ×0,6, старт 40, 12 месяцев надбавки), M12, M13 (переходы ИИ с
   уведомлениями, письма об экосистеме при 25 и 50 %, каждая веха один раз — в том числе после загрузки), M15 и AC-16.2 (роялти 3 %, статья «Royalties», Intel и AMD получают поровну, после банкротства — нет);
   (д) время месячного цикла ≤ 3 мс при 300 CPU (замеры — TSK-030).
8. **Тесты на DLL** — как в п. 5.1, 9.6 и 11.5.

## 16. Расхождения кода со спекой и решения (занести в `design.md`)

1. **Где тесты.** Файлы тестов лежат в `mod\tests\PTMod.Tests\Features\CpuArchitectures\` (как в прошлых кусках), а не в `mod\tests\PTMod.Tests\CpuArchitectures\`: `EcosystemMathTests`, `RoyaltyMathTests`, `IsaExperienceTests`,
   `ExtensionWarningsTests`, `AiMarketTests`, `StateTransferTests`, `StageTwoWiringTests`; тесты `DecideFull` дописаны в `ArchAiPolicyTests`, AC-12.1 — ещё и в `ArchResearchMathTests` (по спеке); AC-13.1 в `ArchRatingMathTests`
   уже был с куска 2, в `ExtensionWarningsTests` он проверен ещё и через `RatingForecast.One` (то, что считает провайдер).
2. **Что было сделано раньше.** Множитель опыта ISA (`isa` 0,6 — `ArchResearchMath.IsaFactor`, `ArchResearchService.EffectiveDays`) и `f_ext` в `ArchRatingMath`/`ArchRatingProvider` уже стояли с кусков 2–3; в этом куске добавлены
   остальные части TSK-025 и TSK-026 (начальная поддержка с наследованием, надбавка, предупреждение, подсказка технологии).
3. **Экосистема.** (а) Считается по рядам игры (`Sold[i] + SoldByContracts[i]`, `i` от `Count − 2` вниз, `ecosystem.windowMonths`); «год назад» — то же окно, сдвинутое на 12 месяцев из тех же рядов, поэтому в сейв
   ничего не пишется. (б) CPU семейства, которого нет в каталоге, в сумму не входит, чтобы доли семейств каталога давали 100 %. (в) Проценты строк панели округляются методом наибольших остатков, чтобы сумма была ровно 100;
   изменение за год считается по долям, а не по округлённым процентам, и может на единицу отличаться от разности показанных чисел. (г) Для CPU без привязки `ResolveCpu` создаёт привязку по умолчанию (как делает посев).
   (д) Раздел «Экосистема» — отдельный, а не строка внутри `SupportRow`, как рисовал design.md: так в нём видны все семейства, в том числе неисследованные (FR-11 п. 4).
4. **Письма об экосистеме.** (а) Для семейства `ecosystem.fallbackFamily` (`proprietary`) писем нет: это собственная ISA каждой компании, а не экосистема; (б) пока в окне нет продаж, веха не проверяется; (в) если за месяц пройдено
   несколько порогов, отправляется одно письмо — для старшего, а записаны все; (г) первое измерение игры (загрузка, новая игра, включение фичи) тихое: пройденные пороги записываются в `milestones`, письма нет;
   (д) выключатель `notifications` глушит и всплывающие уведомления, и письма, но пороги всё равно записываются, чтобы при включении не пришла пачка старых писем; (е) повтор исключён секцией `milestones` и запросом
   `ModMail.Find(тег)`. Уведомление «{Компания} переходит на архитектуру «{…}»» показывается только на этапе 2 (FR-15 — требование этапа 2), не для первого семейства из посева и не для игрока.
5. **Адаптивный ИИ (FR-14).** (а) **Уровень «низкий» кандидату** (дополнено к тексту спеки): кроме `E ≥ 0,2`, кандидат получает «низкий», если компания любит его не меньше, чем активное семейство (вес кандидата ≥ веса
   активного — то же условие, что в FR-7 п. 5). Без этого свежеисследованное с нуля семейство (старт `startOnResearch` = 30, экосистемы ещё нет) не доходит до порога 35: его уровень «выкл» затухает, и Intel, исследовав x86,
   навсегда остался бы на собственной ISA (в чистой ИИ-игре с 1971 года `E(x86)` не поднимется, пока на x86 никто не продаёт); тест `AFamilyResearchedFromScratch_IsFundedUntilItsSupportAllowsTheMove_IntelOnX86` показывает
   переход за ≤ 12 месяцев, а `ANicheCandidateThatTheCompanyLikesLess_IsNotFunded_AndALikedOneIs` — что нелюбимая нишевая ISA не финансируется. Возврат к буквальному тексту — одно условие в `ArchAiPolicy.PlanTiers`; для
   балансовой симуляции TSK-029 это точка настройки. (б) Завершение исследования на этапе 2 больше не переводит ИИ на семейство сразу (`ArchitectureAi.OnFamilyResearched` выходит): переход решает месячный цикл по
   привлекательности и поддержке (FR-14 п. 2). (в) Если активное семейство ИИ неизвестно или не исследовано, компания переходит на самое привлекательное без запаса 10 % и без порога поддержки (проектировать надо на том, что есть).
   (г) `AiDecision.Tiers` содержит только изменения. (д) Веса переопределений (`AiOverrides.Apply`) применяются на обоих этапах: `ArchitectureAi.DecideFor` берёт запись компании с наложенными весами и для `DecideMvp`.
   (е) Доли сегментов: продажи CPU компании за `ai.salesWindowMonths` (3) последних полных месяцев по рядам `SoldByHighEnd/MidRange/LowEnd/Industries`; нет продаж — по голосу `ModSegments.Classify` на каждый выпущенный
   и не снятый с рынка CPU; нет CPU — по четверти. Собственного классификатора в фиче нет (тест исходников ищет бюджеты рынка и полосу цен).
   (ж) Исследование на этапе 2: вес × (1 + E), `E` берётся из экосистемы (не измерена — ×1). (з) `DecideFull` получил необязательный седьмой параметр `SupportConfig` (для стоимости «обычного» содержания), `AiView` — поле `Inflation`.
6. **Роялти (FR-16).** (а) Доход — `CpuBase.MarketData.Income[Count − 2]` (там уже вместе рынок и контракты) по CPU, чей владелец `Cpu.Company` — компания; семейства ISA `x86` — это и `x86`, и `x86_64`; держатели —
   по `AICompany.UniqueID` из `x86License.holderIds`. (б) Игра ставит `LicenseHelper.X86LicenseHasBeenRevoked` при банкротстве **любой** из двух компаний (Inlet или EMD), а спека говорит «оба»; я пользуюсь флагом игры, как
   требует FR-16 п. 3, и отдельно не беру роялти, если нет ни одного живого держателя. (в) Плательщик списывается без проверки денег (как всё в игре; уход в минус — дело игры), одна сумма на компанию.
   (г) План месяца хранится для строки панели; после загрузки он считается лениво по рядам игры. (д) Правила одинаковы для игрока и ИИ; foundry и банкрот не платят.
7. **Конструктор.** (а) Ключ `arch.designer.immature` из design.md разбит на заголовок строки (`arch.designer.immatureTitle`) и значение («+N % к стоимости, осталось M мес.»): строка — пара `ModUI.Info` «название: значение»
   и читается как в FR-12 п. 4. Предупреждение о воротах — `arch.designer.extTitle` + `arch.designer.extWarn` (текст из design.md; несколько предупреждений через «; »). (б) Размещение строк — той же эвристикой, что у
   списка и строки прогноза; без своего текста строка скрыта. (в) Ядра и SMT дизайна берутся патчем на `UpdateNumbers`, а не из `UpdateUI`: смена списка ядер и переключатель SMT не вызывают `UpdateUI`.
8. **Новые ключи L10n (30 в ru и en):** `arch.designer.immatureTitle/.immature/.immatureTip/.extTitle/.extWarn/.extTip`, `arch.ext.multicore/.smt`, `arch.techtip.ext`, `arch.eco.title/.hint/.row/.total/.empty/.tip`,
   `arch.royalty.title/.line/.free/.tip`, `arch.rivals.title/.hint/.colCompany/.colActive/.colResearch/.research/.paused/.empty`, `arch.notify.switch`, `arch.mail.eco.subject/.body`. Подсказка технологии
   (`arch.techtip.ext`) принимает пять аргументов: расширение, нужная поддержка, множитель при нулевой поддержке, семейство игрока, его уровень.
9. **Не сделано (чужие задачи).** Замеры времени (TSK-030), балансовая симуляция (TSK-029), расширение `SaveDataTests` (TSK-031), подсказки карточек с разложением «×сродство × поддержка × экосистема» (TSK-032),
   ручные проверки (TSK-033). `FamilyCard` не получил строки экосистемы (она в своём разделе).

# Кусок 8 (TSK-029 … TSK-032)

Балансовая симуляция чистой логики, замеры времени и проверка деградации частей, совместимость сейвов и локализация полной версии с подсказками-формулами. Сборка `python mod\build_feature.py cpu-architectures`
чистая, `--tests` проходит 1650 из 1650 (три `BuiltDll_*` пропущены, как в п. 5.1 и 9.6), `--coverage-types` чист, `PTMod.dll` открывается `ReflectionOnlyLoadFrom` (`check_reflection_load.ps1`).
Ядро и чужие файлы не менялись; запросов к ядру и к другим фичам нет.

## 17. Для интегратора и владельца (кусок 8)

1. **Отчёт балансовой симуляции (TSK-029)** прикладывается к PR для владельца. Его пишет тест `BalanceSimulationTests.TheReport_HasATableByYear_TheEventsAndTheMetrics` в
   `mod\build\features\cpu-architectures\cpu-architectures-balance-report.txt` (полная таблица, по строке на год 1975–2025); ниже — сжатая. Помесячный прогон на тех же чистых функциях, что вызывают
   сервисы (`EcosystemMath.Shares`, `SupportCycle.RunMonth`, `ArchAiPolicy.DecideFull`, `ArchResearchMath`, `ArchSeedPolicy.SeedAi`, `RatingForecast.One`): 11 ИИ-проектировщиков из `ai-preferences.json`
   (Cyrix появляется в 1987, VIA в 1988), продажи сегмента делятся ∝ `размер × присутствие × m`, где `m` — множитель рейтинга архитектур в модельном ряду компании (ряд сдвигается за активной архитектурой
   с постоянной времени 18 мес.), проданные CPU приписываются семействам ряда — это и есть экосистема. Состояние на начало года, после январского цикла:

   ```
   Год   Intel AMD   Zilog TI    IBM   Fchld Tosh  Motor Cyrix VIA   DD    | ПК x86 | встр. ARM | own  x86  risc arm  x64  a64  rv  | поддержка, млн/год
   1975  own   own   own   own   own   own   own   own   -     -     own   | 0/3    | 0/3       | 100% 0%   0%   0%   0%   0%   0%  | 3.1
   1978  own   own   own   own   own   own   own   own   -     -     own   | 0/3    | 0/3       | 100% 0%   0%   0%   0%   0%   0%  | 40.6
   1980  x86   x86   own   own   own   own   own   own   -     -     own   | 2/3    | 0/3       | 88%  12%  0%   0%   0%   0%   0%  | 70.1
   1985  x86   x86   own   own   own   own   own   own   -     -     own   | 2/3    | 0/3       | 49%  51%  0%   0%   0%   0%   0%  | 97.5
   1990  x86   x86   own   own   risc  own   arm   own   x86   x86   own   | 4/5    | 1/3       | 36%  57%  7%   0%   0%   0%   0%  | 203.8
   1995  x86   x86   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 16%  57%  12%  16%  0%   0%   0%  | 295.0
   2000  x86   x86   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  57%  12%  18%  0%   0%   0%  | 366.0
   2005  x86   x86   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  57%  12%  19%  0%   0%   0%  | 642.5
   2010  x86   x64   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  45%  12%  19%  11%  0%   0%  | 789.2
   2015  x86   x64   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  43%  12%  19%  13%  0%   0%  | 1069.1
   2020  x86   x64   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  43%  12%  19%  13%  0%   0%  | 1269.8
   2025  x86   x64   own   arm   risc  own   arm   own   x86   x86   risc  | 4/5    | 2/3       | 13%  43%  12%  19%  13%  0%   0%  | 1508.1
   ```

   Сокращения: `own` — собственная ISA, `x64` — x86-64, `a64` — ARM64, `rv` — RISC-V, `Fchld` — Fairchild, `Tosh` — Toshiba, `Motor` — Motorola, `DD` — Digital Devices; «ПК x86» — сколько из Intel, AMD,
   Cyrix, VIA, IBM (живых на тот год) на x86, «встр. ARM» — сколько из Texas Instruments, Toshiba, Zilog на ARM/ARM64; справа доли экосистемы по семействам и суммарное содержание поддержки всех компаний
   за предыдущий год. Основные события (полный список в файле): Intel исследует x86 в августе 1978 и переходит на неё в ноябре; AMD — в августе 1979, переход в ноябре; IBM исследует RISC в октябре 1986 и
   переходит в июне 1987; Toshiba — ARM в 1989, переход в октябре; Texas Instruments — ARM в 1989, переход в апреле 1992; Digital Devices — RISC в 1989, переход в мае 1990; AMD исследует x86-64 в июле 2004
   и переходит в январе 2006.

   **Метрики (тесты `BalanceSimulationTests`)**: x86 активна у 4 из 5 «ПК-компаний» уже с 1988 (Intel, AMD, Cyrix, VIA; IBM ушла на RISC) — норма ≥ 3 к 1995; ARM — у 2 из 3 «встраиваемых» (Toshiba, Texas
   Instruments; Zilog весит ARM вдвое меньше собственной ISA и остаётся на ней, как задумано) с 1993 — норма ≥ 2 к 2015; вклад `m` за весь прогон (27 440 вычислений) от 0,800 до 1,152, на сетке всего
   (семейства × годы 1971–2050 × сегменты × поддержка × экосистема × ядра × SMT, ручка 100 % и 150 %) — в [0,80; 1,20] и [0,70; 1,30]; до 1985 года лучшее семейство даёт `m` = 1,147 (≤ 1,20).
   Те же метрики держатся в десяти вариантах рынка (деньги ×0,5 и ×2, рынок «в основном промышленность» и «в основном ПК», оборот модельных рядов 6 и 36 мес., рост рынка 8 %, лёгкая и тяжёлая
   сложность, мало стартовых денег), прогон детерминирован, ни одна компания не уходит в минус, никто не меняет архитектуру больше трёх раз за 50 лет.
2. **Открытый вопрос владельцу — как читать «до 1985 ни одно семейство не даёт > +20 % над `proprietary`».** (а) Вклад `m` любого семейства до 1985 — не больше 1,147; собственное преимущество семейства
   (одинаковые поддержка и экосистема у обоих, по сетке) — 1,100 (RISC и x86-64 в высоком сегменте, они появятся только в 1986 и 2003); обе величины ≤ 1,20 — тест требует именно их. (б) Если сравнивать CPU Intel на x86 с
   CPU Intel на брошенной собственной ISA, чья поддержка за пять лет без финансирования упала почти до 0 (`f_sup` 0,90 против 1,04), отношение выходит 1,245 (Intel, x86, средний сегмент, 1984): запас
   даёт сама шкала поддержки ±10 %, а не семейство. Тест держит это число в пределах 1,20 / 0,80 (это предел коридора) и пишет его в отчёт; чтобы строгое чтение тоже сходилось, надо менять шкалу
   `supportBase`/`supportSpan` или распад поддержки брошенной ISA — это решение владельца, в контент я не вмешивался.
3. **Наблюдения симуляции, без правок контента** (веса `ai-preferences.json` утверждены владельцем, `ContentDefaultsTests` их фиксирует): (а) Intel, VIA и все, кто весит x86-64 и x86 поровну, исследуют
   x86-64 (150M × 12 мес.), но остаются на x86: у неё экосистема 57 % против нуля у новичка, а сродство x86-64 выше лишь в высоком сегменте и всего на 5 %; на x86-64 переходит только AMD (вес 1,2). Если хочется, чтобы
   Intel тоже переходила, нужен вес x86-64 у Intel около 1,2 или правило «наследник той же ISA, исследованный намеренно, заменяет предшественника при поддержке ≥ 35 без запаса 10 %». (б) Motorola,
   Fairchild и Zilog остаются на собственной ISA: у Motorola веса `proprietary` и `risc` равны, и из-за малой экосистемы RISC (≈ 12 %) её `att` не проходит запас 10 %; у Fairchild и Zilog вес ARM/RISC ниже
   веса собственной ISA. (в) Экосистема x86 к 1995 — 57 % и уже не растёт: рынок моделируется без игрока и без «вымирания» собственных ISA; в игре картина будет другой.
4. **Замеры времени (TSK-030).** Включаются в debug-сборке флагом `"cpu-architectures.perf": true` в разделе `debug` файла `ptmod_settings.json` (после перезапуска игры; релизная сборка раздел не читает,
   `ArchPerf.Begin()` там константа −1). Раз в игровой месяц в `Player.log` одна строка `[PTMod] [cpu-architectures] INFO perf 1990-02: Day n=28 avg=0.0123 ms max=0.0400 ms (budget 0.1 ms: ok); Month n=1 …`,
   при превышении среднего — `WARN` и пометка `OVER`; при выходе из сцены и при выключении фичи — итог сессии `perf total 1990-01..1991-06: …`. Пробы и бюджеты NFR-1: `Day` (дневной тик
   исследований `Tick` + арбитр `EarlyTick`, сумма за игровой день) 0,1 мс; `Month` (весь месячный обработчик) 3 мс, его шаги `Month.ecosystem/.support/.royalty/.ai` пишутся без бюджета, чтобы видеть,
   какой тормозит; `Rating` (все вызовы провайдера `market.rating.cpu` за игровой день) 0,2 мс; `Cost` (один вызов провайдера `development.cost.design`) 5 мкс; `Designer` (все постфиксы конструктора:
   `Initialize`, `UpdateUI`, `ConsumptionAndTemperature`, `Performance`, `UpdateNumbers`, за кадр) 0,05 мс. Бюджет проверяется по среднему (как в NFR-1), максимум пишется рядом. Чистая половина
   хуков, измеренная отдельной консольной сборкой: 1 200 вызовов `RatingForecast.One` — 0,054 мс (бюджет рейтинга за день 0,2 мс), одна математика цены проекта — 3 нс (бюджет 5 мкс), месячная математика
   при 300 CPU и 14 компаниях (экосистема, поддержка, решения ИИ) — 0,07 мс (бюджет 3 мс); в тестовой сборке без оптимизации (`-optimize-`, JIT не оптимизирует) те же числа 0,27 мс, 45 нс и 0,14 мс.
   **В игре не измерялось** (игра не запускалась): вклад игровой половины хуков (`ResolveCpu`, словари ядра, `ProviderResult`) покажет только лог.
5. **Проверка деградации частей (TSK-030).** В debug-сборке самопроверка читает флаг `"cpu-architectures.hide.<тип>.<член>": true` (тип без пространства имён: `CreatorCpu`, `CurrentResearchUI`,
   `CpuUI`, `AIBehaviourResearch`, `MarketData`, …) и считает такой член отсутствующим (`hidden by the debug flag`), выключается только часть, которая на нём стоит. Ожидаемое: `…hide.CreatorCpu.coreCountDropdown` →
   `self-check Designer: OFF [… CreatorCpu.coreCountDropdown hidden by the debug flag …]`, остальные части `ok`: список архитектур и множители дизайна пропадают, рейтинг на рынке и исследования идут;
   `…hide.CurrentResearchUI.targetTechnology` → выключена только `LabUi` (игрок не прерывает технологию из панели архитектур и запускает архитектуру на свободной лаборатории); `…hide.CreatorCpu.currentCoreCount`
   или `.smtEnabled` → только `Extensions` (нет предупреждения о воротах в конструкторе); `…hide.CpuUI.architectureText` → `Inspector`; `…hide.AIBehaviourResearch.company` → `Ai`. Таблицу членов тесты
   `PartDegradationTests` читают из исходника `ArchSelfCheck.cs` и проверяют по каждому обязательному члену, какие части он выключает: двум частям принадлежат только `ICompany.SpendMoney` (`Support` и
   `Royalty`) и `Cpu.get_Company` (`Ecosystem` и `Royalty`). Ядро эти флаги не знает — это договорённость фичи; в игре проверять после запуска, строки самопроверки идут в `Player.log` при включении фичи.
6. **Сейвы (TSK-031).** Код не менялся, `SaveDataTests` расширен восемью тестами: сейв этапа 1 (игрок и два ИИ после посева, исследование в работе, привязки CPU, ожидающий проект) открывается на этапе 2 без миграции и
   без предупреждений, шаги этапа 2 (поддержка, надбавка за незрелые инструменты, вехи экосистемы, решение ИИ, переопределения весов) работают на нём с засеянных значений, а после записи секция та же;
   секция старой формы без списков поздних этапов читается; битые записи списков этапа 2 (дубли, отрицательные компании, NaN, вес 99, пороги 1 и 100) отбрасываются или приводятся в границы с предупреждением;
   семейство, которого нет в каталоге, проходит через поддержку, решение ИИ, слияние и запись без потерь; `resumeTech` вне диапазона (121 при 120 технологиях, `int.MaxValue`, −7) забывается при загрузке и
   лаборатория ничего не возвращает; слияние состояния поглощённой компании (объединение исследованного, максимум поддержки, ярусы и активное семейство покупателя остаются, незавершённое исследование не
   переносится) переживает запись и повторно ничего не меняет; сейв с двумя игроками или без них не ломает реестр. Ручная часть M8 на версии этапа 2 — владельцу (TSK-033).
7. **Локализация и подсказки (TSK-032).** Ключи этапа 2 (`arch.support.*`, `arch.eco.*`, `arch.notify.*`, `arch.mail.*`, `arch.royalty.*`, `arch.rivals.*`, `arch.designer.immature*`, `arch.designer.ext*`,
   `arch.techtip.ext`) уже были в `ru` и `en` с кусков 6–7; `L10nKeysTests` получил список из этих ключей (`StageTwoKeys`) и проверки, что формулы в текстах на месте. Новые 14 ключей в обоих языках — подсказки
   с разложением: `arch.segment.high/.mid/.low/.industry`, `arch.card.tip.rating/.line/.affinity/.fade/.support/.eco/.ext/.limit/.strength/.assumed`. Подсказка карточки семейства теперь заканчивается
   строками «Высокий сегмент: ×1,05 сродство ×1,04 поддержка ×1,03 экосистема = ×1,12» по четырём сегментам (на этапе 1 — только сродство и возраст), числа те же, что считает провайдер рейтинга
   (`RatingBreakdown` вызывает `ArchRatingMath`; тест сверяет с `RatingForecast.One` на сетке из более чем 20 тысяч точек); для неисследованного семейства поддержка берётся стартовая
   (`SupportMath.StartSupport`) с пометкой; если коридор [0,80; 1,20] срезал произведение — «(предел вклада архитектуры)»; если ручка силы не 100 % — отдельная строка с тем, что получит рынок. То же — в подсказке
   строки «Рейтинг по сегментам» в конструкторе для семейства из списка и ядер/SMT дизайна на экране (подсказка самого выпадающего списка осталась прежней). Подсказки строк поддержки с формулой
   (`0,90 + 0,20 × S/100`, содержание = цена × уровень × эпоха × инфляция) и раздела экосистемы были в кусках 6–7. **Вид подсказок в игре не проверялся.**
8. **Что не проверялось в игре (игра не запускалась):** все замеры из п. 4; строки самопроверки со скрытым членом (п. 5); вид подсказки карточки и строки конструктора (длина четырёх строк, перенос); поведение ИИ по
   п. 18.1 на живых CPU (в модели — продажи ∝ m); M8 на версии этапа 2.

## 18. Расхождения кода со спекой и решения (занести в `design.md`)

1. **Политика ИИ, этап 2: поддержка «преследуемого» семейства (FR-14 п. 3 дополнен).** Симуляция TSK-029 показала на утверждённых весах, что чистое правило «`att` лучшего ≥ 1,10 × `att` активного и поддержка ≥ 35»
   с уровнем «низкий» только лучшему кандидату даёт тупик: свежеисследованное семейство стартует с поддержкой 30 и пустой экосистемой, проигрывает поддержанной собственной ISA, кандидатом не становится,
   остаётся на «выкл», затухает, и исследование потрачено зря. До правки только Toshiba из «встраиваемых» переходила на ARM (метрика ARM ≥ 2 из 3 к 2015 не выполнялась: 1 из 3), IBM, Motorola, Fairchild,
   Texas Instruments и AMD не использовали исследованные RISC, ARM и x86-64. Правка (`ArchAiPolicy.IsPursued`, `PlanTiers`): каждое исследованное семейство, которое компания любит не меньше активного
   (вес ≥ веса активного, > 0 — условие FR-7 п. 5), финансируется как активное — «обычный» при деньгах на 12 месяцев содержания, иначе «низкий»; переход по-прежнему решает `att` с запасом 10 % и порогом поддержки
   35, так что при равных весах семейство ещё должно реально выиграть. Кандидат с `E ≥ 0,2`, которого компания любит меньше, получает «низкий», как в спеке; остальные — «выкл». Результат: IBM → RISC в 1987,
   Texas Instruments → ARM в 1992, AMD → x86-64 в 2006 (Toshiba и Digital Devices переходили и раньше); метрики сходятся во всех вариантах рынка. Тесты `ArchAiPolicyTests`: два прежних теста (`ACompany_DoesNotMove_WhenTheSupportOfTheBestIsUnder35_…`,
   `ANicheCandidateThatTheCompanyLikesLess_IsNotFunded_AndALikedOneIs`) переписаны под новое правило (кандидат с экосистемой, которого любят меньше, — «низкий»; любимый — «обычный»), добавлены
   `AFamilyTheCompanyLikesAsMuchAsItsOwn_IsBroughtUpToTheLevelOfTheActiveOne_…`, `AFamilyTheCompanyLikesLess_IsNotPursued_ItDecays`, `TexasInstruments_MovesToArm_OnceItsSupportHasCaughtUp_AndNotBefore35`. Вернуться к
   буквальному тексту FR-14 п. 3 — убрать ветку `IsPursued` из `PlanTiers` (тест симуляции тогда упадёт на ARM).
2. **Метрика «до 1985» читается в тестах как `m ≤ 1,20` и «собственное преимущество семейства ≤ 1,20»** (п. 17.2); отношение с брошенной ISA (1,245) в отчёте, не в проверке.
3. **Где тесты.** Файлы тестов — в `mod\tests\PTMod.Tests\Features\CpuArchitectures\` (как в прошлых кусках), а не в `mod\tests\PTMod.Tests\CpuArchitectures\`: `BalanceSimulator.cs` (внутренний движок симуляции), `BalanceSimulationTests`,
   `ArchPerfMeterTests`, `PartDegradationTests`, `PerfWiringTests`, `RatingBreakdownTests`; расширены `SaveDataTests`, `L10nKeysTests`, `ArchAiPolicyTests`, `StageTwoWiringTests` (строка про `UpdateNumbers`).
4. **Новые публичные типы чистой логики** (все в `[Covers]`): `ArchPerfMeter`, `PartCheck`, `RatingBreakdown` (+ `RatingTerms`, `RatingTerm`, `RatingTermValue`). Игровая часть: `Services/ArchPerf.cs` (обёртка над
   `Stopwatch` и флагом), `UI/RatingTooltipText.cs` (передаёт `ModL10n.L` в `RatingBreakdown.Lines`). `ArchSelfCheck` теперь складывает вердикты в `PartCheck` (формат строк в логе прежний).
5. **Что именно меряется.** NFR-1 говорит о «дневном тике и арбитре ≤ 0,1 мс»: меряется сумма `Tick` исследований и `EarlyTick` арбитра за игровой день (две подписки, одна проба `Day`); «рейтинг ≤ 0,2 мс/день» —
   сумма всех вызовов провайдера за игровой день (кеш ядра делает их не больше 1 200 в день); «конструктор ≤ 0,05 мс/кадр» — сумма постфиксов за кадр (`Time.frameCount`). Только вызовы после раннего выхода
   `NeutralNow`: выключенная фича за замер не платит. Никаких своих счётчиков ошибок замер не заводит.
6. **Микрооптимизации математики рейтинга не сделаны.** Пробовал (счётчик вида триггера в `ExtensionDef`, ход по `List` без интерфейса, цикл вместо `Array.IndexOf`): в оптимизированной сборке математика и так
   занимает ≈ 27 % бюджета (45 нс на вызов), в тестовой сборке без оптимизации выигрыша не было; изменения откатил.
7. **Тестовый бюджет чистой половины хуков** — в 3 раза больше бюджета NFR-1 (`UnoptimizedBuildAllowance`): тесты собираются без оптимизации, и та же математика там примерно в пять раз медленнее, чем в
   поставляемой сборке; проверка ловит порядок величины, а не доли.
