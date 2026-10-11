# Запросы research-tree-ux к итоговому интегратору

## 1. Чистые типы публичного API недоступны чистой логике в тестах

Тестовые сборки (`build.py` -> `build_tests`, `build_feature.py` -> `build_feature_tests`) берут из `Features/<Pascal>/` только `Logic/**`. Папка `Api/` в них не входит, а
`Features/ResearchTreeUx/Api/Dto/*.cs` (`Better`, `CompareRow`, `ModifierLine`, `ModifierLineGroup`) не ссылаются ни на ядро, ни на Unity, ни на игру.

Почему эти типы лежат в `Api/`, а не в `Logic/`: их возвращают интерфейсы `ICompareRowProvider` и `IModifierLineSource`, а сборка фичи-потребителя (`cpu-architectures`) берёт ядро, свою фичу и только
`Api/` зависимостей (`build_feature.py: feature_sources`). Тип в `Logic/` потребителю недоступен. Чистая логика самой фичи (`CompareModel.Build` возвращает `List<CompareRow>`,
`ModifierReportBuilder.Build` принимает `ModifierLineGroup`) использует те же типы, и в тестовой сборке их нет (CS0246).

Просьба: добавить `Features/<Pascal>/Api/Dto/**/*.cs` в оба списка файлов тестовой сборки (в `build_tests` — для каждой фичи, у которой такая папка есть; в `build_feature_tests` — для фичи и для
`Api/Dto` её зависимостей). Папка совпадает с договорённостью `first-year-onboarding` (`Api/Dto`); у `rival-acquisitions` та же потребность названа `Api/Contracts`, имя папки стоит свести к одному.
Публичные типы из `Api/Dto` тогда попадут под проверку `[Covers]` (`EveryPublicLogicType_IsListedInCovers`): нужен один тестовый класс `ApiDtoTests` с `[Covers(typeof(CompareRow), ...)]`
(значения по умолчанию, `ModifierLineGroup.Lines` не `null`); его пишет исполнитель фичи.

Безопасность правки: все файлы `Api/Dto/**` чистые (только `System.Collections.Generic`), без `PTMod.Core`, `UnityEngine`, `Assembly-CSharp`; код — общее подмножество C# (без `init`, `record`, `required`).
Остальные файлы `Api/` (`ResearchTreeUxApi`, интерфейсы с `RectTransform`, `Cpu`, `ICompany`, `Technology`, `DeferredResearchTreeUxApi`) используют ядро, Unity и игру и в тесты не входят.

Текущий обход: пока правки нет, `Logic/` фичи не может ссылаться на `Better`, `CompareRow`, `ModifierLine`, `ModifierLineGroup` в тестовой сборке. Исполнителю предписано держать в `Logic` собственные
чистые модели строк (`Logic/CompareModel.cs`, `Logic/ModifierReportBuilder.cs` и их типы в пространстве `PTMod.Features.ResearchTreeUx.Logic`, не совпадающие по именам с типами из `Api/Dto`), а
слой окон переводит их в типы из `Api/Dto` на границе (строки сторонних провайдеров приходят как `CompareRow` и `ModifierLineGroup` и переводятся в модели `Logic` при добавлении в таблицу).
Сборка фичи (`python mod\build_feature.py research-tree-ux`) и сборка потребителя (`cpu-architectures`) от этого не зависят и проходят.

Если правку делать не будут: тот же обход остаётся постоянным; логика под тестами сохраняется, ценой небольшого слоя преобразования.

## 2. Подключение реализации (для исполнителей фичи, не для интегратора)

`ResearchTreeUxFeature.Register` должна один раз вызвать `ResearchTreeUxApi.Install(реализация)` (метод `internal`): реализация — класс фичи, реализующий `IResearchTreeUxApi`
(реестр провайдеров, `IsActive` = `ModFeatures.IsActive("research-tree-ux")`, `OpenTab`). Регистрации других фич, сделанные раньше (порядок `Register` у фич не задан, выключенная при старте фича
регистрируется только после включения в хабе), `Install` передаёт реализации сам по порядку вызовов; дубль `TabId`, исключения провайдеров (`ModHooks.ReportFault` на фичу-владельца провайдера) и
пропуск провайдеров при выключенной фиче — задача реализации. Статические методы `ResearchTreeUxApi.*` не бросают исключений наружу: потребитель не должен терять `Register` из-за сбоя фичи.

Нужного в ядре и в чужом API, чего не хватало бы, нет.

Сделано в TSK-014: `ResearchTreeUxFeature.Register` создаёт `ResearchTreeUxRegistry` (`Registry/ResearchTreeUxRegistry.cs`, реализует `IResearchTreeUxApi`) и вызывает `ResearchTreeUxApi.Install` до объявления патчей и тиков; швы пунктов 7 и 8 (`LabBusyTextProvider`, `ModifierGroupsProvider`, `CompareRowsProvider`, `TooltipLineProvider`) заполнены там же. Интегратору для этого пункта делать нечего.

## 3. `BuiltDll_*` в тестах и сборка одной фичи (не блокирует, касается всех фич)

`python mod\build_feature.py <slug>` кладёт `PTMod.dll` в `mod\build\features\<slug>\`, а `python mod\build_feature.py <slug> --tests` берёт эту же папку как `PTMOD_BUILD_DIR`. Тест
`ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` (и два соседних `BuiltDll_*`) считает такой DLL «свежим», если он новее всех исходников, и тогда ждёт в нём ресурсы **всех** фич из
`Content/` и `L10n/`, а в DLL одной фичи их три-шесть: тест падает не из-за ошибки фичи. Пока DLL старше исходников (любая правка после сборки), он пишет `skip`.

Просьба итоговому интегратору (по желанию): пропускать `BuiltDll_*`, когда `PTMOD_BUILD_DIR` лежит в `build\features\` (это сборка одной фичи), либо сравнивать ресурсы только для
`mod\build\PTMod.dll` полной сборки. Обход у исполнителя фичи: после проверки сборки удалить `mod\build\features\research-tree-ux\PTMod.dll` и `.pdb` (так и сделано), либо запускать `--tests`
после правки исходников.

## 4. `ModUI.Tooltip` не умеет снимать подсказку (TSK-006, не блокирует)

`ModUI.Tooltip(GameObject, ...)` вешает на объект компонент `ModTooltipTrigger`, а парного вызова, который его снимает, в ядре нет. Фиче он нужен при выключении в хабе (FR-8 п. 3: «снять подсказки
`ModUI.Tooltip`»): кнопки дерева игры живут до конца сцены, а компонент ядра остаётся на них.

Обход в фиче: `TechTooltips.Detach(target)` вызывает `ModUI.Tooltip(target, string.Empty, string.Empty)`: по документации ядра с двумя пустыми текстами подсказка не показывается, и провайдер фичи
(замыкание на её объекты) заменяется постоянным пустым текстом, то есть ссылка на состояние фичи с кнопки уходит. Сам компонент остаётся на кнопке до конца сцены.

Просьба (по желанию): добавить в `ModUI` вызов `RemoveTooltip(GameObject target)`, который уничтожает `ModTooltipTrigger` (и его `TooltipData` через `Release`), либо принимать `provider = null` как «снять».
После этого `TechTooltips.Detach` заменить одной строкой; поведение фичи не изменится.

## 5. Цвет текста `ModUI.Text` (TSK-005, не блокирует)

`ModUI.Text` красит текст цветом темы (`ThemeRole.Text`) и перекрашивает его при каждой смене темы, поэтому цвет компонента нельзя задать снаружи: он вернётся к цвету темы. Метка «НОВОЕ»
на кнопке дерева должна быть заметной, поэтому фича задаёт цвет тегом rich text (`<color=#…>` с `Colors.DynamicWarningColor()`) и обновляет текст на `ModUI.ThemeChanged` и `ModL10n.LanguageChanged`.

Просьба (по желанию): параметр `ThemeRole`/цвета в `ModUI.Text(parent, text, bold, role)` (например `Accent`, `Warning`), чтобы такие метки перекрашивались ядром. Сейчас обход работает без правок ядра.

## 6. Фон и ряды собственных панелей `ModUI` (TSK-009, не блокирует)

`ModUI` не даёт панели с фоном: `ThemeRole` внутренний (`Text`, `ScrollTrack`, `ScrollHandle`), а `ModUI.Text`, `VerticalList` и `ScrollView` прозрачные. Панели списка исследований нужен непрозрачный фон, закрывающий
дерево, и цветные ряды, и она красит их сама: читает `ThemeManager.Instance.CurrentTheme` (`ResearchTreeBackground` для фона, `ResearchNormal`, `ResearchCurrentTech`, `ResearchUnnecessary` для рядов, цвет
предупреждения `Colors.DynamicWarningColor()` для меток), обёрнуто в `try/catch` с запасными цветами, и перекрашивает на `ModUI.ThemeChanged`.

Просьба (по желанию): сделать `ThemeRole` публичным и добавить роли фона (`Background`, `Background2`, `Warning`) и вызов `ModUI.Panel(parent, ThemeRole)`, который возвращает перекрашиваемый фон. После этого
`ResearchListView.ReadPalette` / `ReadTheme` заменить вызовом ядра; поведение фичи не изменится. Обход работает без правок ядра.

## 7. Источник занятости лаборатории для списка (TSK-009 -> TSK-014)

Список исследований показывает строку «Лаборатория занята: …» (FR-2 п. 9), но `ILabOccupancySource` подключает TSK-014. Шов уже есть: `ResearchTreeUxFeature.LabBusyTextProvider` (`Func<string>`, внутренний,
`null` — строки нет) и обёртка `ResearchTreeUxFeature.LabBusyText()` с `try/catch`. Исполнителю TSK-014: в реализации `IResearchTreeUxApi` при `RegisterLabOccupancy` присвоить провайдеру функцию, которая
для каждого зарегистрированного источника (по порядку) вызывает `IsLabBusy()` и при `true` возвращает `BusyText()`; исключение источника — `ModHooks.ReportFault` на фичу-владельца и отключение источника.
Для напоминаний (TSK-015) нужен булев ответ «занята ли лаборатория» без текста: завести рядом `LabIsBusy()` на тех же источниках. Ядру ничего не нужно.

## 8. Швы для TSK-014: блоки панели модификаторов и строки сравнения (TSK-011, TSK-013)

Панель «Все модификаторы» и окно сравнения уже умеют показывать то, что дают другие фичи, но источников они не знают: шов у `ResearchTreeUxFeature`, как `LabBusyTextProvider` (п. 7).
Исполнителю TSK-014 в реализации `IResearchTreeUxApi` присвоить два внутренних свойства:

- `ModifierGroupsProvider` (`Func<ICompany, IReadOnlyList<ModifierLineGroup>>`): для каждого зарегистрированного `IModifierLineSource` по порядку вызвать `GetGroup(company)` и собрать
  непустые группы. Исключение источника: `ModHooks.ReportFault` на фичу-владельца (`ModFeatures.FeatureOfType(source.GetType())`) и отключение этого источника. Перевод заголовков и подписей
  (`TitleKey`, `LabelKey`, `Args`) делает уже сама фича (`ModifierReports.ConvertGroups`), провайдеру отдавать ключи, не тексты. `IsActive` = false: возвращать пустой список.
- `CompareRowsProvider` (`Func<Cpu, Cpu, IReadOnlyList<CompareRow>>`): то же для `ICompareRowProvider.GetRows(a, b)`; обе стороны могут быть `null` (сторона ещё не выбрана), строки в таблице
  сортируются по `Order`, занятый `Id` (в том числе встроенный: `ips`, `year`...) отбрасывается. Фича копирует `CompareRow` в свою модель сама (`CompareRows.FromApi`) и сама считает разницу,
  если `DeltaText` не заполнен.

Ядру ничего не нужно.

## 9. Обновление списка выпадающего меню `ModUI.Dropdown` без вызова обработчика (TSK-013, не блокирует)

`ModUI.Dropdown` возвращает `ProcessorTycoon.UI.DropdownUI`, а родной `DropdownUI.UpdateOptions(List<string>)` выбирает последний пункт и вызывает `onValueChanged`, то есть обработчик, который
фича передала в `ModUI.Dropdown`. Окну сравнения нужно менять пункты второго списка (процессоры) при выборе компании в первом без лишнего вызова. Обход в фиче: `CompareWindow.SetOptions` берёт
`TMP_Dropdown` с того же объекта и делает `ClearOptions`, `AddOptions`, `SetValueWithoutNotify`, `RefreshShownValue` (так делает и сам `ModUI.Dropdown` при создании).

Просьба (по желанию): в `ModUI` добавить `SetOptions(DropdownUI dropdown, IList<string> options, int index)`, который делает то же без вызова обработчика. После этого `CompareWindow.SetOptions`
заменить вызовом ядра; поведение фичи не изменится.

## 10. Кнопка рядом с кнопкой игры (TSK-013, не блокирует)

`ModUI.Button(parent, ...)` в родителе без layout-группы ставит кнопку на всю ширину, прижатой к верху, поэтому кнопка «Сравнить» в `InspectorWindow` после создания получает якоря, размер
и положение соседней кнопки «Detailed/Simplified», сдвинутые на её ширину в сторону, где есть место (`InspectorButton.Place`); в родителе с layout-группой она встаёт следующей после соседа.
Устройство иерархии инспектора в сцене неизвестно (сцены нет в выгрузке), поэтому место и размер кнопки проверяет владелец (M8). Просьба (по желанию): `ModUI.PlaceBeside(ButtonUI made, Component neighbour)`
с этой логикой в ядре, чтобы каждая фича, которой нужна кнопка рядом с кнопкой игры, не писала своё.

## 11. Открытие окна дерева из `ResearchTreeUxApi.OpenTab` (TSK-014, не блокирует)

`OpenTab(tabId)` должен показать окно дерева исследований, если оно закрыто. Окна игры — объекты сцены, которые гасит `WindowStartBehaviour` и включает кнопка панели задач; общего вызова «открыть окно игры» в ядре нет.
Фича включает объекты окна сама: все неактивные предки `ResearchTreeWindow` сверху вниз (`TreeUxView.ActivateWindow`), затем `WindowHierarchyBehaviour.UpdateHierarchy` ближайшего предка (`WindowFront`), чтобы окно легло поверх
остальных. Что не проверено без игры: совпадает ли состояние кнопки панели задач (подсветка «окно открыто») с тем, что фича включила окно напрямую, и закрывается ли такое окно обычным крестиком. Владелец проверяет при
M-сценарии с кнопкой «Открыть вкладку» в хабе `cpu-architectures` (`arch.hub.openTab`).

Просьба (по желанию): в `ModUI` добавить `OpenGameWindow(Component inWindow)` — единый способ открыть и поднять окно игры тем же путём, что и кнопка панели задач, — и заменить им `TreeUxView.ActivateWindow` и `WindowFront`.

## 12. Страница вкладки другой фичи (TSK-014, не блокирует)

Страница, в которой строит содержимое провайдер вкладки (`IResearchTabProvider.BuildContent`), — непрозрачная панель поверх области дерева под полосой вкладок (`Tree/ExternalTabPane.cs`), такая же, как у списка. Фон она красит сама:
`ThemeManager.Instance.CurrentTheme.ResearchTreeBackground` с запасным цветом, перекраска на `ModUI.ThemeChanged`; та же потребность, что в пункте 6 (панель с фоном и роль цвета в `ModUI`). Корень, который вернул провайдер, растягивается
на всю страницу layout-группой (`childControlWidth/Height`, `childForceExpand*` = true). Размер, который провайдер читает у `parent.rect` в `BuildContent`, — размер этой страницы. Проверка вида — владельцем (вкладка «Архитектуры»).

## 13. Секция выключенной фичи теряется, если сохранение — первое после загрузки (TSK-019, не блокирует)

`SideFileModel.ReadSection` после успешного `Read` удаляет копию секции из `_retained`, а `BuildSection` для неактивного участника отдаёт именно `_retained`. Поэтому если игрок загрузил сейв
с секцией `research-tree-ux`, выключил фичу в хабе и сохранил игру **до** любого другого сохранения в этой сессии (до первого `Build` при включённой фиче), секции в новом файле не будет:
`{"schema":1,"features":{}}`. Проверено прогоном `SideFileModel` с участником `UxSaveSection` и `isActive`, который меняется с `true` на `false` между `Apply` и `Build`. Если сохранение уже было
при включённой фиче, последнее состояние держится (тест `AFeatureSwitchedOffAfterASave_KeepsItsLastSection`), как и секция, которой фича не владела при загрузке
(`ASectionWhenTheFeatureIsOff_PassesThroughUnchanged_AndIsNotGivenToTheHolder`).

Для этой фичи потеря безвредна: при следующем включении секции нет, и всё доступное считается просмотренным (FR-3 п. 5), метки «НОВОЕ» не появляются. Но design.md («Миграция») обещает
«секция сохраняется ядром как есть и возвращается при включении», и для фич с настоящими данными (`bankruptcy-recovery`, `design-flaw-recalls`) та же дыра стоила бы данных.

Просьба (по желанию, в `mod-framework`): при `Read` оставлять в `_retained` исходный узел секции, пока участник не отдал новые данные (`Write`), либо при переходе участника в неактивные
(`ModSave` знает о `ModFeatures.StateChanged`) снимать с него `Write()` в `_retained`. Фича менять ничего не будет: тест `AFeatureSwitchedOffAfterASave_KeepsItsLastSection` останется верным.

## 14. Замеры NFR-1 включаются подробным журналом (TSK-018, справка)

Замеры `PerfProbe` (`Tree/PerfProbe.cs`) работают при `ModSettings.VerboseLog` (поле `verboseLog` в `ptmod_settings.json`). Без него `PerfProbe.Begin()` возвращает 0 и не строит ни одной строки,
так что дневной тик и постфикс `UpdateButtonStates` ничего не выделяют. Строки в `Player.log`: `[PTMod] [research-tree-ux] DEBUG perf: tab switch 0.42 ms (limit 5 ms) tab memory applied (...)`; превышение бюджета
— одно `WARN` на операцию за сессию, остальное в `DEBUG`; итоги сессии (`perf summary: ...`) пишутся при выключении фичи и при выходе из сцены. Ядру ничего не нужно; ядерная вкладка «Диагностика» уже показывает
время обработчиков тиков (`ModTicks.StatsOf`), и ею можно перепроверить дневной тик.

## 15. Ширина боковой панели: итог спайка по коду и что остаётся проверить в игре (TSK-021, не блокирует)

Спайк «структура `SideWindow` позволяет менять ширину без патчей» сделан по декомпилированному коду и выгрузке данных (запуск игры запрещён). Что известно твёрдо: `SideWindow` двигает свой `RectTransform` только в
`UpdateWindowVisibility` — `LeanMoveX(rectTransform, isOpen ? visiblePosition : hiddenPosition, 0.35 с)` — а оба положения лежат в приватных полях (`-150` и `130` в сцене `level1`, объект «Side Window (dark)»). Поэтому
ширину можно менять без патча: размер `rectTransform` и два поля пишет мод (`Sidebar/SidebarResizer.cs`, поля через `AccessTools`), а все дальнейшие движения игры (добавление проекта, кнопка-переключатель) идут к новым
положениям сами. Расчёт положений (`SidebarWidthRule`) не зависит от того, где точка опоры панели: открытая панель сохраняет правый край, закрытая — левый край, то есть скрывается ровно настолько, насколько скрывала игра.

Чего в выгрузке нет (иерархии сцены нет): якорей и точки опоры (`pivot`) корня панели, нет ли над ним layout-группы или `ContentSizeFitter`, растягиваются ли карточки проектов и CPU по ширине `scrollableContent`, есть ли на корне
`Mask`. Поэтому: (1) точка опоры, масштаб и размер читаются из самой панели во время работы; (2) после установки размера панель спрашивают, какую ширину она на самом деле получила (через кадр после запуска и в конце
каждого перетаскивания) — если не ту, мод пишет одно предупреждение `sidebar: the side panel did not take the width …`, возвращает исходные размер и положения и выключает только `UxParts.Sidebar`; (3) полоска-ручка — последний
дочерний объект корня панели, шириной 8 единиц (5 снаружи от левого края, 3 внутри), поэтому её не перекроет фон-потомок, но она занимает эту узкую полосу у края.

Владельцу в M11 проверить: видна ли едва заметная светлая полоска на левом краю открытой панели и подсвечивается ли под курсором; растёт ли панель влево до 1,6 × исходной ширины; растягиваются ли карточки по новой
ширине (если карточки имеют фиксированную ширину, свободное место останется пустым — это уже предел структуры, патч не нужен и не делался); та же ширина после перезагрузки сцены; закрытая панель целиком за экраном, и кнопка-вкладка
переключателя не стала шире, чем была; двойной щелчок по полоске и кнопка «Исходная ширина панели» на странице фичи в хабе возвращают ширину; при выключении фичи в хабе панель сразу становится прежней. Ядру ничего не нужно.
