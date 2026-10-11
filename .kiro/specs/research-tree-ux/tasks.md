# Tasks: research-tree-ux

## Document Information

- **Feature Name**: research-tree-ux
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/research-tree-ux/requirements.md`
  - Design: `.kiro/specs/research-tree-ux/design.md`
  - Ядро: `.kiro/specs/mod-framework/design.md`, `.kiro/specs/mod-framework/tasks.md` (номера `mod-framework/TSK-0xx` ниже)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала — каркас фичи и чистая логика с тестами (фильтры, модели, тексты), затем UI по частям в порядке ценности: вкладки → подсказки → список → модификаторы → сравнение. Точки расширения (TSK-014) нужны `cpu-architectures`, поэтому их делаем до проверки. Каждая UI-часть включается отдельно (`UxParts`), так что фичу можно показывать владельцу по частям. Регулируемая ширина боковой панели (TSK-021, Could) делается последней и не входит в MVP.

### Implementation Strategy

- Вся работа с объектами игры — в тонких адаптерах (`TreeIndex`, `TechSnapshotReader`, `ModifierSnapshotReader`, `CompareWindow`); логика — в чистых статических классах `Features/ResearchTreeUx/Logic/**` с тестами. Логика не ссылается на `UnityEngine` и на типы игры (тип технологии и модификатора — целые значения), код — общее подмножество C# и API .NET Framework 4.8.
- Ни одного патча формул: только постфиксы `ResearchTreeWindow.InitializeUI`, `ResearchTreeWindow.UpdateButtonStates`, `InspectorWindow.Start` (через `[ModPatch]` и `ModHooks.Guard`).
- Фича подключается к ядру только через сервисы `PTMod.Core` (`FeatureContext.OnTick`, `ModTicks.GameReady`, `IModSaveParticipant`, `ModProfile`, `ModUI.Tooltip`, `ModModifiers.Explain`); прямых подписок на `DateController`/`Player`/`SaveHandler` нет.
- Каждая задача 2–4 часа; после каждой — сборка (`python mod/build.py --game-dir <папка игры>`) и тесты (`python mod/build.py --tests`) зелёные.

### Development Approach

- **Testing Strategy**: тесты чистой логики на мини-раннере `.NET Framework 4.8` (`[Test]`, `[Covers(typeof(X))]`, `Assert.*`) пишутся вместе с кодом (TDD для моделей); UI проверяется ручными сценариями M1–M11 (`design.md`).
- **Integration Strategy**: части фичи подключаются флагами `UxParts`; самопроверка `ModHooks` выключает часть, если её цели нет.
- **Deployment Strategy**: `install.py --with-mods` из `mod-framework`; фича включена по умолчанию, выключается в хабе «Моды».

## Фаза 1 — Каркас и чистая логика

- [x] **TSK-001**: Каркас фичи research-tree-ux (авто 2026-10-10)
  - **Requirement**: FR-8, NFR-2, NFR-4, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/ResearchTreeUxFeature.cs` (`[ModFeature] sealed class … : IModFeature`: `Id` = `research-tree-ux`, `NameKey`, `DescriptionKey`, `DependsOn` = пусто, `EnabledByDefault` = true, `SupportsHotToggle` = true, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `ResearchUxPrefs.cs` (чтение и запись через `ModProfile.Section`), `ResearchUxSaveParticipant.cs` (`IModSaveParticipant`, `SchemaVersion` = 1, без собственного поля версии), `mod/src/PTMod/Features/ResearchTreeUx/Logic/UxParts.cs`; параметры `notifyNewTech` и `notifyIdleLab` через `ctx.Param`; пустые ключи `rtux.*` в `mod/src/PTMod/L10n/ru/research-tree-ux.json`, `mod/src/PTMod/L10n/en/research-tree-ux.json`; `mod/src/PTMod/Content/research-tree-ux/ui.json` с данными из `design.md`
  - **Acceptance**: сборка проходит; ядро находит фичу по `[ModFeature]`, она видна в хабе «Моды», `ModFeatures.StateOf("research-tree-ux")` = Enabled; самопроверка пишет в лог список найденных целей (`ResearchTreeWindow.InitializeUI`, `ResearchTreeWindow.UpdateButtonStates`, `InspectorWindow.Start`) и приватных полей; при отсутствии поля соответствующий флаг `UxParts` снят; в коде фичи нет прямых подписок на `DateController`, `Player`, `SaveHandler` и собственных счётчиков ошибок
  - **Depends**: mod-framework/TSK-021 (`ModSettings`, `ModProfile`, `ModL10n`, `ModContent`), mod-framework/TSK-022 (`IModFeature`, `FeatureContext`), mod-framework/TSK-023 (`ModHooks`), mod-framework/TSK-025 (`ModSave`), mod-framework/TSK-043 (`core.json`: префикс `rtux`)

- [x] **TSK-002**: Чистая модель технологий и вкладок (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/TechSnapshot.cs`, `TechCategory.cs`, `TabDefinition.cs`, `CategoryFilter.cs`, `UnnecessaryRule.cs`, `UnlocksIndex.cs` (обратные зависимости); `mod/tests/PTMod.Tests/Features/ResearchTreeUx/CategoryFilterTests.cs`, `UnnecessaryRuleTests.cs`
  - **Acceptance**: тесты покрывают 7 типов × «Все», линии с одним скрытым концом, `FocusTarget` (доступная/исследованная/пусто), правило «ненужной» для IDM/fabless/foundry (совпадает с `ResearchButton.TechnologyIsUnnecessary`); все публичные типы в `[Covers]`; все тесты зелёные
  - **Depends**: TSK-001, mod-framework/TSK-067 (мини-раннер)

- [x] **TSK-003**: Индекс дерева и сопоставление линий (авто 2026-10-10)
  - **Requirement**: FR-1, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tree/TreeIndex.cs`, `Logic/LineMatcher.cs`, `Patches/TreeWindowPatches.cs` (`[ModPatch]` postfix `ResearchTreeWindow.InitializeUI`, вызывающий `OnTreeBuilt`); `mod/tests/PTMod.Tests/Features/ResearchTreeUx/LineMatcherTests.cs`
  - **Acceptance**: `LineMatcher` проходит тесты (точное совпадение, допуск 0,5 px, несопоставленные); в отладочном логе после загрузки сцены: «кнопок 97, линий N, сопоставлено N»; исключение в постфиксе не доходит до игры (`ModHooks.Guard`; проверка искусственным исключением в debug-сборке)
  - **Depends**: TSK-002, mod-framework/TSK-023 (`ModHooks`)

## Фаза 2 — Дерево: вкладки, метки, подсказки

- [x] **TSK-004**: Полоса вкладок и фильтр дерева (авто 2026-10-10)
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tree/TabStrip.cs`, `Tree/TreeFocus.cs` (центрирование через `ResearchTreeController.controller`); ключи `rtux.tab.*` в `mod/src/PTMod/L10n/ru/research-tree-ux.json`, `mod/src/PTMod/L10n/en/research-tree-ux.json`
  - **Acceptance**: 8 встроенных вкладок по `ui.json`; выбор вкладки скрывает чужие кнопки и линии, «Все» возвращает исходное состояние; вид центрируется на первой доступной технологии вкладки; последняя вкладка запоминается в `ModProfile` и восстанавливается после загрузки сейва; переключение ≤ 5 мс в отладочном замере
  - **Depends**: TSK-003, mod-framework/TSK-036 (виджеты `ModUI`), mod-framework/TSK-021 (`ModProfile`)

- [x] **TSK-005**: Метки «новое» и счётчики вкладок (авто 2026-10-10)
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/NewTechTracker.cs`, `Tree/NewBadges.cs`; постфикс `ResearchTreeWindow.UpdateButtonStates` в `Patches/TreeWindowPatches.cs` (ранний выход при перемотке); `ResearchUxSaveParticipant.Read`/`Write` (секция `research-tree-ux`, `List<int> seenAvailable`, без поля версии); обработчик `ModTicks.GameReady` (`NewGame`: всё доступное просмотрено; `Loaded` без секции: то же); `mod/tests/PTMod.Tests/Features/ResearchTreeUx/NewTechTrackerTests.cs`
  - **Acceptance**: тесты трекера зелёные (непросмотренные, отметка, «всё доступное просмотрено», сериализация, отбрасывание неверных ID); в игре метка «НОВОЕ» появляется у новой доступной технологии, снимается при уходе с вкладки и при наведении; после сохранения/загрузки не возвращается; старый сейв без секции — меток нет; на старте новой игры меток нет
  - **Depends**: TSK-004, mod-framework/TSK-024 (`ModTicks`, `GameReady`), mod-framework/TSK-025 (`ModSave`)

- [x] **TSK-006**: Подсказки через `ModUI.Tooltip` и отметка просмотра (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tooltips/TechTooltips.cs` (адаптер `Attach`/`Detach` к `ModUI.Tooltip`), `Tooltips/HoverSeenTracker.cs` (время под курсором, `hoverSeenSeconds`)
  - **Acceptance**: `ModUI.Tooltip` на тестовой кнопке показывает родной `Tooltip` с родной задержкой, текст строится при наведении; `HoverSeenTracker` вызывает `OnSeen` после `hoverSeenSeconds` и не срабатывает при коротком проходе курсора; при `TooltipHandler.Instance` = null — без ошибок (гарантия ядра); в коде фичи нет собственного `TooltipData`, `TooltipHandler.StartTrigger` и локального моста
  - **Depends**: TSK-001, mod-framework/TSK-064 (`ModUI.Tooltip`)

- [x] **TSK-007**: Подсказка технологии (авто 2026-10-10)
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/TechTooltipBuilder.cs`, `Logic/ResearchContext.cs`, `Tree/TechSnapshotReader.cs`; подключение `TechTooltips.Attach` к каждой `ResearchButton`; ключи `rtux.tip.*`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/TechTooltipBuilderTests.cs`
  - **Acceptance**: тесты: DDR6 в 1990 → «+13 300 %», 130nm в 2005 → «−45 %», BaseTime = 0 → «мгновенно», Funding ≈ 0 → «∞», ненужная для fabless, внешние строки; в игре время в подсказке совпадает с временем на кнопке дерева
  - **Depends**: TSK-002, TSK-006

## Фаза 3 — Список

- [x] **TSK-008**: Модель списка исследований (авто 2026-10-10)
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/ResearchListModel.cs`, `Logic/ListFilter.cs`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/ResearchListModelTests.cs`
  - **Acceptance**: тесты: три фильтра в комбинациях, три сортировки, ничьи по ID, группы с «исследовано/всего», пустой результат; все зелёные
  - **Depends**: TSK-002

- [x] **TSK-009**: Вид «Список» и выбор исследования (авто 2026-10-10)
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/List/ResearchListView.cs`, `List/ResearchListRow.cs`; переключатель «Дерево / Список» в `Tree/TabStrip.cs`; строка «Лаборатория занята: …» (читает `ILabOccupancySource` после TSK-014); ключи `rtux.list.*`, `rtux.status.*`, `rtux.lab.busy`
  - **Acceptance**: список заменяет область дерева; строки обновляются раз в день (`TickPhase.DayPassed`); «Исследовать» вызывает `ResearchSector.SetCurrentResearch(Technology)`, `CurrentResearchUI.SetTechnology(Technology)`, `ResearchTreeWindow.ForceUIUpdate()`; для «ненужной» — `UnnecessaryTechnologyWarning.Open`; кнопка неактивна у исследованных, заблокированных и текущей; пустой фильтр — строка «Нет исследований по фильтру»; обновление ≤ 3 мс; вид и фильтры запоминаются в `ModProfile`
  - **Depends**: TSK-004, TSK-008, TSK-007, mod-framework/TSK-036

## Фаза 4 — Модификаторы и сравнение

- [x] **TSK-010**: Таблица ванильных модификаторов и отчёт (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/VanillaModifierTable.cs`, `Logic/ModifierReportBuilder.cs`, `Logic/ModifierSnapshot.cs` (включая `ExplainData`/`ExplainRow`); `mod/tests/PTMod.Tests/Features/ResearchTreeUx/VanillaModifierTableTests.cs`, `ModifierReportBuilderTests.cs`
  - **Acceptance**: тесты: 7 типов × флаги IDM/foundry/fabless/TCMS/«невезучая» совпадают с кодом `Modifiers.GetModifier`; строки источников из `ExplainData` (фича, причина, множитель, срок); «(ограничено)» при `IsBalanceCapped`/`IsFuseCapped`; `Explain` недоступен → «Моды ×k» = итог/ваниль; проекты в работе; внешние группы; пример AC-5.1 даёт «×1,29»
  - **Depends**: TSK-002

- [x] **TSK-011**: Кнопка и панель «Все модификаторы» (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Modifiers/ModifiersPanel.cs`, `Modifiers/ModifierSnapshotReader.cs` (чтение `Company.ResearchModifiers`, `TimerScheduler.GetTimedObjectsOfType<Project>()`, `Project.GetResearchProject()`, `Modifiers.GetModifier`, `ResearchMath.ResearchSpeed`, `ModModifiers.Explain` для 7 каналов типов 0–6 с `ModifierContext {Company = игрок}`); ключи `rtux.mod.*`
  - **Acceptance**: наведение на кнопку показывает подсказку (`ModUI.Tooltip`), клик открывает закреплённую панель; сценарии AC-5.1…AC-5.4 выполняются в игре; построение ≤ 1 мс
  - **Depends**: TSK-010, TSK-006, mod-framework/TSK-027 (`ModModifiers`), mod-framework/TSK-059 (`Explain`, причины провайдеров), mod-framework/TSK-035 (`ModWindow`)

- [x] **TSK-012**: Модель сравнения CPU (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/CompareModel.cs`, `Logic/CpuFacts.cs`, `Logic/IFormatter.cs`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/CompareModelTests.cs`
  - **Acceptance**: тесты: дельта (B − A)/|A|, «—» при A = 0 и нечисловых, порог 2,5 %, направления higher/lower/none, A = B → 0 %, внешние строки по `Order`; все зелёные
  - **Depends**: TSK-002, mod-framework/TSK-067

- [x] **TSK-013**: Окно сравнения и кнопка в инспекторе (авто 2026-10-10)
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Compare/CompareWindow.cs`, `Compare/CpuFactsReader.cs`, `Compare/GameFormatter.cs` (обёртка `StringFormatter`), `Patches/InspectorPatches.cs` (`[ModPatch]` postfix `InspectorWindow.Start`); страница хаба с кнопкой «Сравнить CPU» (`ctx.HubPage`); ключи `rtux.compare.*`
  - **Acceptance**: кнопка «Сравнить» рядом с «Detailed/Simplified»; окно открывается с A = текущий CPU инспектора; выбор компании/CPU для B (включая соперников, только поля, доступные в инспекторе), «Поменять местами», подсветка лучшей стороны цветами `Colors`; CPU обанкротившейся компании сбрасывается без исключений (AC-6.3); из хаба A = последний CPU игрока
  - **Depends**: TSK-012, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-039 (хаб, `HubPage`)

## Фаза 5 — Расширения, напоминания, выключение

- [x] **TSK-014**: API расширения ResearchTreeUxApi (авто 2026-10-10)
  - **Requirement**: FR-7, NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Api/ResearchTreeUxApi.cs`, `Api/IResearchTabProvider.cs`, `Api/ICompareRowProvider.cs`, `Api/IModifierLineSource.cs`, `Api/ITechTooltipLineSource.cs`, `Api/ILabOccupancySource.cs`; подключение в `TabStrip`, `CompareWindow`, `ModifiersPanel`, `TechTooltipBuilder`, `ResearchListView`, `NotifierPolicy`; отладочная вкладка-заглушка (только debug-сборка); `mod/tests/PTMod.Tests/Features/ResearchTreeUx/ApiRegistryTests.cs`
  - **Acceptance**: регистрация до и после построения окна работает; дубль `TabId` отклоняется с предупреждением; исключение провайдера отключает только его и записывается через `ModHooks.ReportFault` на фичу-владельца (AC-7.2); `IsActive` = false при выключенной фиче, регистрации запоминаются и действуют после включения; `ILabOccupancySource` опрашивается списком и политикой напоминаний (AC-2.4, AC-9.2); отладочная вкладка видна после «Ядра»
  - **Depends**: TSK-004, TSK-007, TSK-011, TSK-013, mod-framework/TSK-022 (`ModFeatures.FeatureOfType`, `StateChanged`), mod-framework/TSK-023 (`ReportFault`)

- [x] **TSK-015**: Напоминания о новых исследованиях и простое лаборатории (авто 2026-10-10)
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Notify/ResearchUxNotifier.cs`, `Logic/NotifierPolicy.cs`; переключатели в хабе (`ParamSpec`); ключи `rtux.notify.*`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/NotifierPolicyTests.cs`
  - **Acceptance**: тесты политики зелёные (включая «лаборатория занята источником»); в игре сценарий AC-9.1: одно уведомление на 8-й день простоя, без повторов 30 дней; AC-9.2: лаборатория, занятая архитектурой, простоем не считается; во время перемотки напоминаний нет; в пакетном прогоне `NextMonth` показ откладывает `ModNotify`; выключенный переключатель — уведомлений нет
  - **Depends**: TSK-005, TSK-014, mod-framework/TSK-033 (`ModNotify`), mod-framework/TSK-062 (отложенный показ на `BatchEnded`)

- [x] **TSK-016**: Выключение посреди игры и восстановление вида (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**: `ResearchTreeUxFeature.OnDisabled/OnEnabled` (снятие и восстановление UI, `TechTooltips.Detach`, снятие `HoverSeenTracker`), страница фичи в хабе «Моды»
  - **Acceptance**: сценарий M9: после выключения дерево идентично ванильному (все кнопки и линии `activeSelf` = true, объектов мода нет), окно сравнения закрыто; после включения UI восстановлен без перезагрузки сцены; дневные обработчики фичи не вызываются, пока она выключена
  - **Depends**: TSK-004, TSK-009, TSK-011, TSK-013, mod-framework/TSK-039 (хаб: горячее переключение)

## Фаза 6 — Проверка

- [x] **TSK-017**: Локализация ru/en и тест полноты ключей (авто 2026-10-10)
  - **Requirement**: NFR-5
  - **Deliverables**: все ключи `rtux.*` в `mod/src/PTMod/L10n/ru/research-tree-ux.json` и `mod/src/PTMod/L10n/en/research-tree-ux.json`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/L10nKeysTests.cs`
  - **Acceptance**: тест находит каждый ключ, используемый в коде фичи, в обоих файлах; все ключи имеют префикс `rtux.` (кроме `core.*` ядра); в игре с языком `en` нет «сырых» ключей
  - **Depends**: TSK-009, TSK-011, TSK-013, TSK-015, mod-framework/TSK-043, mod-framework/TSK-066 (`ModIds`)

- [x] **TSK-018**: Самопроверка, замеры производительности и тесты контента (авто 2026-10-10)
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**: замеры `ModLog` (`ModSettings.Debug`) в `TabStrip.Select`, `ResearchListView.Refresh`, постфиксе `UpdateButtonStates`, построении подсказки; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/ContentSchemaTests.cs`
  - **Acceptance**: в отладочной сессии: вкладка ≤ 5 мс, список ≤ 3 мс, дневной тик при закрытом окне ≤ 0,1 мс, подсказка ≤ 1 мс; тест битого `ui.json` проходит (битая запись пропущена, при полном отказе — значения по умолчанию)
  - **Depends**: TSK-016

- [x] **TSK-019**: Совместимость сейвов (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/tests/PTMod.Tests/Features/ResearchTreeUx/SaveSectionTests.cs` (нет секции, пустая, ID вне диапазона, дубликаты, > 512 элементов, `ResetToDefaults`)
  - **Acceptance**: все тесты зелёные; участник не читает и не пишет поле версии (`_schema` — дело ядра, проверяется `mod-framework/TSK-016`); секция новее мода сохраняется ядром как есть при выключенной фиче
  - **Depends**: TSK-005, mod-framework/TSK-016 (модель бокового файла)

- [ ] **TSK-020**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-9, NFR-1 … NFR-8
  - **Deliverables**: отметки о прохождении сценариев M1–M10 из `design.md` (заполняет владелец в этом файле или в PR)
  - **Acceptance**: все сценарии M1–M10 пройдены; в `Player.log` за сессию ≥ 2 часов нет исключений `[PTMod]` от research-tree-ux; остаточный вопрос из `requirements.md` («сравнение чужих CPU и туманность») подтверждён или решён
  - **Depends**: TSK-017, TSK-018, TSK-019

## Фаза 7 — Could (после гейта)

- [x] **TSK-021**: Регулируемая ширина боковой панели (авто 2026-10-10)
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/SidebarWidthRule.cs`, `Sidebar/SidebarResizer.cs` (ручка на левом крае `SideWindow`, масштаб `RectTransform`, пересчёт `visiblePosition`/`hiddenPosition` через `AccessTools`, запись множителя в `ModProfile`); флаг `UxParts.Sidebar`; ключи `rtux.sidebar.*`; `mod/tests/PTMod.Tests/Features/ResearchTreeUx/SidebarWidthRuleTests.cs`
  - **Acceptance**: спайк: структура `SideWindow` позволяет менять ширину без патчей; тесты зажима [1,0; 1,6] и позиции скрытого состояния зелёные; сценарий M11 (ширина до 1,6 ×, запоминается, закрытая панель за экраном); при выключении фичи исходные ширина и позиции возвращаются; нет поля — только эта часть выключена
  - **Depends**: TSK-016, mod-framework/TSK-021 (`ModProfile`)

## Dependency graph

```
mod-framework (TSK-021 ModSettings/ModProfile/ModL10n/ModContent, TSK-022 IModFeature/FeatureContext, TSK-023 ModHooks, TSK-024 ModTicks,
               TSK-025 ModSave, TSK-027/059 ModModifiers.Explain, TSK-033/062 ModNotify, TSK-035/036 ModUI, TSK-039 хаб,
               TSK-043 core.json, TSK-064 ModUI.Tooltip, TSK-066 ModIds, TSK-067 мини-раннер)
   │
TSK-001 ─┬─> TSK-002 ─┬─> TSK-003 ──> TSK-004 ─┬─> TSK-005 ─┬─> TSK-015 (+ TSK-014)
         │            │                        │            └─> TSK-019
         │            ├─> TSK-008 ─────────────┴─> TSK-009 (+ TSK-007)
         │            ├─> TSK-010 ──> TSK-011 (+ TSK-006)
         │            └─> TSK-012 ──> TSK-013
         └─> TSK-006 ──> TSK-007 (+ TSK-002)

TSK-004 + TSK-007 + TSK-011 + TSK-013 ──────────> TSK-014 (API для cpu-architectures: вкладка, строки, занятость лаборатории)
TSK-004 + TSK-009 + TSK-011 + TSK-013 ──────────> TSK-016 ──> TSK-018
TSK-009 + TSK-011 + TSK-013 + TSK-015 ──────────> TSK-017
TSK-017 + TSK-018 + TSK-019 ────────────────────> TSK-020 (ручная проверка, владелец)
TSK-016 ────────────────────────────────────────> TSK-021 (Could, после гейта)
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Каркас фичи | Done (авто 2026-10-10): сборка и 109 тестов фичи зелёные; хаб, `StateOf` = Enabled и строки самопроверки в `Player.log` проверит владелец в игре |
| TSK-002 Чистая модель технологий и вкладок | Done (авто 2026-10-10) |
| TSK-003 Индекс дерева и сопоставление линий | Done (авто 2026-10-10): `LineMatcher` под тестами; строку «buttons/lines/matched» и искусственное исключение (флаг `debug.rtuxThrowOnTreeBuilt`) проверит владелец в игре |
| TSK-004 Полоса вкладок и фильтр дерева | Done (авто 2026-10-10): сборка и тесты зелёные (план вкладки `TabViewPlan`, выбор вкладки `TabSelection`); вид полосы, центрирование (`TreeFocus`: сдвиг `ResearchTreeController.controller` в мировых координатах), запоминание вкладки и строку «tab: … applied in N ms» в логе проверит владелец в игре. Вкладки других фич — TSK-014 |
| TSK-005 Метки «новое» и счётчики | Done (авто 2026-10-10): `NewTechTracker` и `TabBadges` под тестами, секция сейва (`seenAvailable`) пишется и читается трекером, `GameReady` отмечает доступное просмотренным (новая игра, сейв без секции); вид меток «НОВОЕ» и счётчиков, снятие при уходе с вкладки и при наведении проверит владелец в игре |
| TSK-006 Подсказки через ModUI.Tooltip | Done (авто 2026-10-10): `TechTooltips` — только `ModUI.Tooltip`, `HoverSeenTimer`/`HoverSeenTracker` под тестами, в коде фичи нет `TooltipData`/`StartTrigger` (проверяет тест исходников); показ родного `Tooltip` с родной задержкой проверит владелец в игре (запрос к ядру: снятие подсказки, `mod/requests/research-tree-ux.md` п. 4) |
| TSK-007 Подсказка технологии | Done (авто 2026-10-10): `TechTooltipBuilder` под тестами (DDR6 в 1990 — «+13 300 %», 130nm в 2005 — «−45 %», «мгновенно», «∞», fabless, внешние строки), `TechSnapshotReader` читает игру; совпадение времени в подсказке с временем на кнопке (оба через `StringFormatter.DaysToDate`) проверит владелец в игре |
| TSK-008 Модель списка | Done (авто 2026-10-10): `ListFilter` (три независимых фильтра, сортировка, перевод в `UxPrefsModel` и обратно), `ResearchListModel` (группы по типу с «исследовано/всего» всего типа, три сортировки, ничьи по ID, пустой результат), `ListRowRules` (статус, активность кнопки «Исследовать», предупреждение для «ненужной»), `ListRowTextBuilder` (тексты колонок и заголовка группы) под тестами; «Только доступные» убирает заблокированные, «Скрыть исследованные» — исследованные, «Скрыть ненужные» не трогает исследованную и текущую технологию |
| TSK-009 Вид «Список» и выбор исследования | Done (авто 2026-10-10): `ResearchListView` и `ResearchListRow` (виджеты `ModUI`, панель поверх области дерева под полосой вкладок, строки создаются по требованию и переиспользуются, пишутся только изменившиеся тексты), переключатель «Дерево / Список» в `TabStrip`, обновление в `TickPhase.DayPassed` (и в постфиксе `UpdateButtonStates`, не чаще раза за кадр), «Исследовать» = `SetCurrentResearch` + `CurrentResearchUI.SetTechnology` + `ForceUIUpdate` (для «ненужной» через `UnnecessaryTechnologyWarning.Open`), вид и фильтры в `ModProfile`, строка «Лаборатория занята: …» через шов `ResearchTreeUxFeature.LabBusyTextProvider` (источник `ILabOccupancySource` подключает TSK-014, `mod/requests/research-tree-ux.md` п. 7). Частично не проверено: вид панели, клик, предупреждение и замер обновления (≤ 3 мс, строка `list: … written in N ms` в отладочном логе) проверит владелец в игре |
| TSK-010 Ванильные модификаторы и отчёт | Done (авто 2026-10-10): `VanillaModifierTable` (7 типов × 32 набора флагов IDM/foundry/fabless/TCMS/«невезучая» против независимой транскрипции `Modifiers.GetModifier`, причины), `ModifierSnapshot` с `ExplainData`/`ExplainRow`/`PendingProject`, `ModifierReportBuilder` (формула скорости AC-5.1 «×1,29», строки источников с причиной и сроком AC-5.4, «(ограничено)», «Моды: ×k» без `Explain`, накопленные бонусы и проекты в работе AC-5.2, прочие модификаторы AC-5.3, стоимость текущего исследования, блоки других фич, подсказка из тех же строк) под тестами; `TipFormat` дополнен `Frequency` и `Has`. Знак «√» в шрифте игры проверит владелец |
| TSK-011 Панель «Все модификаторы» | Done (авто 2026-10-10): `ModifierSnapshotReader` (`Company.ResearchModifiers`, `TimerScheduler.GetTimedObjectsOfType<Project>()`, `GetResearchProject()`, `Modifiers.GetModifier`, `ResearchMath.ResearchSpeed`, `ModModifiers.Explain` для 7 каналов типов 0–6 с игроком, каждая часть под своим try/catch), `ModifierReports`, кнопка «Модификаторы» в полосе вкладок (`TabStrip.AddModifiersButton`) с подсказкой через `ModUI.Tooltip` (строится при наведении) и закреплённая панель `ModifiersPanel` (окно `Window.Panel`, не ставит игру на паузу; строки переиспользуются; обновление на `TickPhase.DayPassed`, в постфиксе `UpdateButtonStates` не чаще раза за кадр и при смене языка); шов `ResearchTreeUxFeature.ModifierGroupsProvider` для блоков других фич (подключает TSK-014, `mod/requests/research-tree-ux.md` п. 8). Частично не проверено: вид и место кнопки в полосе, вид окна, сценарии AC-5.1…AC-5.4 в игре и замер построения (≤ 1 мс, строка `modifiers: report built in N ms` в отладочном логе) проверит владелец в игре |
| TSK-012 Модель сравнения CPU | Done (авто 2026-10-10): `CompareModel` (26 встроенных строк по `ui.json`, дельта (B − A)/|A| и «—» при A = 0 и нечисловых, A = B → «0 %», порог 2,5 % включительно, направления higher/lower/none, внешние строки по `Order` с отбрасыванием занятого `Id`, не больше 64 строк), `CpuFacts`, `IFormatter`, `CompareConfig` (раздел `compare` в `ui.json` с проверкой; в файле теперь 26 строк, текстовые строки и «SMT», «Память» добавлены), `CompareSelection` и `CpuRef` (стороны по ключу компании и `SaveID`, «Поменять местами», сброс стороны обанкротившейся компании без исключений, AC-6.3) под тестами |
| TSK-013 Окно сравнения и кнопка в инспекторе | Done (авто 2026-10-10): `CompareWindow` (окно `Window.Panel`; по паре списков «Компания» и «CPU» на сторону, «Поменять местами», таблица с колонкой разницы, лучшая сторона цветами `Colors`; перерисовка при открытии, выборе, смене языка и темы и каждый день; в выборе только ключ компании и `SaveID`), `CpuFactsReader` (поля инспектора и окон рынка, ничего из `AIStrategy`, линий и склада), `GameFormatter` (обёртка `StringFormatter`), `InspectorButton` и `InspectorPatches` (одиночный optional-постфикс `InspectorWindow.Start`: кнопка «Сравнить» рядом с «Detailed/Simplified», A = `currentCpu` инспектора), страница фичи в хабе «Моды» (`ctx.HubPage`, кнопка «Сравнить CPU», A = последний выпущенный CPU игрока, хаб закрывается перед открытием окна); шов `ResearchTreeUxFeature.CompareRowsProvider` для строк других фич (подключает TSK-014, `mod/requests/research-tree-ux.md` п. 8). Частично не проверено: место и размер кнопки в инспекторе (иерархия окна неизвестна, положение вычисляется от соседней кнопки, п. 10 запросов), вид и размер окна, подсветка, работа выпадающих списков, сброс стороны в игре (AC-6.3) проверит владелец в игре (M8) |
| TSK-014 API расширения | Done (авто 2026-10-10): `ResearchTreeUxRegistry` за `ResearchTreeUxApi` (`Install` в `Register`, регистрации до и после построения окна, дубль `TabId` и id встроенной вкладки отклоняются с предупреждением, каждый вызов провайдера под `ProviderCalls` с `ModHooks.ReportFault` на фичу-владельца по `ModFeatures.FeatureOfType`, провайдер отключается один, провайдеры выключенной фичи не опрашиваются и опрашиваются снова при включении, `IsActive` = состояние фичи); чистые `ProviderBook`/`ProviderCalls`/`TabMerge`/`LabOccupancy` под тестами `ApiRegistryTests`; вкладки других фич в полосе (`TabStrip.SetTabs`, страница `ExternalTabPane`, `BuildContent` один раз при открытом окне, `OnShown`/`OnHidden`, счётчик `BadgeCount`, перестройка полосы на регистрацию и на `ModFeatures.StateChanged`), `OpenTab`, швы строк сравнения, блоков модификаторов, строк подсказки и занятости лаборатории заполнены; отладочная вкладка `rtux-debug` (порядок 100, только PTMOD_DEBUG, флаг `debug.rtuxThrowInTab`). Вкладку «Архитектуры» после «Ядра», открытие окна из `OpenTab`, вид страницы вкладки и одну запись в `Player.log` при сбое провайдера проверит владелец в игре |
| TSK-015 Напоминания | Done (авто 2026-10-10): `NotifierPolicy`/`NotifierState`/`NotifyConfig` (числа 7 и 30 дней и «раз в месяц» из `ui.json`) под тестами `NotifierPolicyTests` — AC-9.1 (одно уведомление на 8-й день простоя, повтор не раньше чем через 30 дней), AC-9.2 (лаборатория, занятая источником, простоем не считается), нет доступных технологий, окно дерева открыто, не больше одного раза в месяц; `ResearchUxNotifier` на тиках ядра `DayPassed`/`MonthPassed` (перемотку и загрузку отсекает ядро), переключатели `notifyIdleLab`/`notifyNewTech` читаются при каждом вызове, показ — `ModNotify.ShowKey` (в пакетном прогоне `NextMonth` откладывает ядро); `TechSnapshotReader.ReadLab` читает лабораторию одним вызовом, пока исследование идёт. Сценарии AC-9.1, AC-9.2 и отложенный показ в `NextMonth` проверит владелец в игре |
| TSK-016 Выключение посреди игры | Done (авто 2026-10-10): `OnDisabled` под отдельными `Guard` убирает вид (`TreeUxView.Destroy`: страницы вкладок других фич с `OnHidden`, список, панель модификаторов, полоса, метки, `TreeFilter.Reset` возвращает `SetActive(true)` каждой кнопке и линии, `TechTooltips.Detach`, `HoverSeenTracker.Remove`), окно сравнения и кнопку в инспекторе, ссылки на сцену и счётчики напоминаний; `OnEnabled` по шагам под `Guard` (самопроверка, «просмотренные», уже построенное окно, инспектор) возвращает вид из профиля без перезагрузки сцены; дневные и месячные обработчики — только `ctx.OnTick`; страница фичи в хабе: кнопка «Сравнить CPU», переключатели напоминаний (`ParamSpec`) и строка о выключенных самопроверкой частях. Проверяют исходники (`LifecycleSourceTests`). Сценарий M9 (вкладка «Кэш», выключить, все кнопки и линии на месте, «Сравнить» пропала, включить — вид вернулся) проверит владелец в игре; компонент `ModTooltipTrigger` на кнопках дерева остаётся до конца сцены (запрос 4) |
| TSK-017 Локализация ru/en | Done (авто 2026-10-10): таблицы строк лежат в `L10n/ru/research-tree-ux.json` и `L10n/en/research-tree-ux.json` (151 ключ, префикс `rtux.`, как у остальных фич, а не в общих `ru.json`/`en.json`); тест `L10nKeysTests` (22 теста): наборы ключей ru и en совпадают, каждый ключ проходит `ModIds.IsL10nKeyAllowed`, каждый ключ из кода (литералы, константы, ключи `ui.json`: названия вкладок и подписи 26 строк сравнения) есть в обоих файлах, ни один ключ таблицы не остался без пользователя, в английском файле нет кириллицы, нет «сырых» ключей `[rtux.…]` при разрешении через `LocTextResolver` в обоих языках, число аргументов в каждом вызове (`format.L`, `ModL10n.L`, `ShowKey`, константы-ключи) совпадает с числом `{n}` текста, в коде нет русских строковых литералов, чужие таблицы не содержат `rtux.*`, ключи ядра (`core.explain.capped`) есть в таблицах ядра. В английском тексте «Now "{0}"» вместо «Now «{0}»». Вид строк в игре на языке `en` (нет ли «сырых» ключей, влезают ли тексты и знаки «√ → ≈ × −» в шрифт) проверит владелец |
| TSK-018 Самопроверка и производительность | Done (авто 2026-10-10): `Tree/PerfProbe.cs` (замер включён при `ModSettings.VerboseLog`; без него `Begin()` даёт 0, ни одна строка не строится и ничего не выделяется) и чистые `Logic/PerfBudget.cs` (`PerfOp`, `PerfBudget`: вкладка 5 мс, список 3 мс, дневной тик при закрытых окнах 0,1 мс, подсказка и отчёт модификаторов 1 мс, постфикс `UpdateButtonStates` при закрытом окне 0,1 мс; `PerfStats`: первый вызов — прогрев и не оценивается, превышение — одно `WARN` на операцию за сессию, остальное `DEBUG`, итог сессии `perf summary: …` пишется при выключении фичи и выходе из сцены; `PerfDaySum` складывает обработчики одного игрового дня). Замеры стоят в `TreeUxView.SelectTab` (до перерисовки списка), `ResearchListView.Refresh` (с чтением игры), `ResearchTreeUxFeature.OnButtonStatesUpdated` (после раннего выхода на перемотке), `OnDay` и `OnDayReminders` (сумма дня, оценивается только когда не открыто ни дерево, ни панель модификаторов, ни сравнение), `TreeUxView.BuildTip`, `ModifierReports.Build`; часы читает только `PerfProbe`. `Logic/UxContent.cs` читает `ui.json` одним входом (`LoadContent`): битая запись пропускается со строкой в журнал, секция неверной формы даёт умолчания секции, непригодный файл — умолчания всех секций, неизвестный верхний член называется в журнале; `ContentSchemaTests` (20 тестов) идут через `ContentMerger.MergeConfig` ядра (тот же путь, что `ModContent.LoadConfig`) и проверяют поставляемый файл, битую запись, неверную форму секции, нечитаемый встроенный файл, чужую схему и «не JSON» в файле игрока; `PerfBudgetTests` (29 тестов) — бюджеты, арифметику сессии и места замеров в коде. Сами числа (≤ 5 / 3 / 0,1 / 1 мс) в отладочной сессии с `verboseLog` проверит владелец по строкам `perf:` в `Player.log` (запрос 14 в `mod/requests/research-tree-ux.md`) |
| TSK-019 Совместимость сейвов | Done (авто 2026-10-10): правила секции вынесены в чистый `Logic/UxSaveSection.cs` (`ISideFileParticipant`), `ResearchUxSaveParticipant` — тонкий наследник, дающий число технологий и журнал; `SaveSectionTests` (41 тест) гоняют держатель и внутри настоящего `SideFileModel` ядра: нет секции, секция `{}`, пустой список, пример спеки, id вне диапазона, повторы, больше 512, элементы не целые числа, список не массив, `ResetToDefaults`, повторное чтение, `Write` без поля версии, круговой обход, сейв без бокового файла и новая игра, секция новее мода (участник получает умолчания, ядро пишет секцию обратно как прочитано), секция при выключенной фиче (проходит насквозь и отдаётся участнику через `RestoreParticipant`), последнее состояние выключенной фичи; сканы исходников: ни одного упоминания `_schema`/`SchemaKey`, DTO только список, фича не пишет в файлы игры и не трогает `SaveHandler`. Решения: секция без списка `seenAvailable` (или с не-массивом) читается как отсутствующая, а не как «ничего не просмотрено» (иначе все доступные технологии стали бы «НОВОЕ»); `Write()` до первого подсчёта «просмотренных» возвращает `null`, секция в файл не попадает и следующая загрузка идёт как со старого сейва. Выяснено, что ядро теряет секцию, если фичу выключили сразу после загрузки и сохранились до первого `Build` (запрос 13; для этой фичи безвредно). Ручной сценарий M10 (сейв без мода и сейв с модом при удалённом моде) — за владельцем |
| TSK-020 Ручная проверка (владелец) | Pending |
| TSK-021 Ширина боковой панели (Could) | Done (авто 2026-10-10): чистые `Logic/SidebarWidthRule.cs` (зажим фактора в [1,0; 1,6], ширина, положения открытой и закрытой панели при любой точке опоры: правый край открытой панели и левый край закрытой остаются на месте, то есть закрытая панель скрывается ровно настолько, насколько скрывала игра, а панель, скрытую игрой целиком, остаётся целиком за экраном; фактор при перетаскивании за левый край) и `Logic/SidebarConfig.cs` (раздел `sidebar` из `ui.json`: `widthMin` ≥ 1,0, `widthMax` ≤ 2,0, минимум не выше максимума; читается через `UxContent`) под тестами `SidebarWidthRuleTests` (26), `SidebarConfigTests` (11); `Sidebar/SidebarResizer.cs` — полоска у левого края `SideWindow` (последний дочерний объект корня, 5 единиц снаружи края и 3 внутри, подсветка под курсором, подсказка через `ModUI.Tooltip`), перетаскивание только при открытой панели, размер панели через `SetSizeWithCurrentAnchors`, положения — через поля `visiblePosition`/`hiddenPosition` (`AccessTools`), точка опоры, масштаб и размер читаются из самой панели, движение игры (`LeanTween`) останавливается перед постановкой панели на место; множитель в `ModProfile` (`sidebarWidthFactor`, пишется один раз в конце перетаскивания) и применяется заново при построении сцены (`PTModHost.SceneReady`) и при включении фичи; двойной щелчок по полоске и кнопка «Исходная ширина панели» на странице фичи в хабе возвращают ширину; `Detach` в `OnDisabled` и при выходе из сцены возвращает исходные размер и оба положения; нет любого из трёх полей — выключена только часть `UxParts.Sidebar` (самопроверка), а панель, не принявшая ширину (над ней layout или `ContentSizeFitter`), выключает ту же часть одним предупреждением и возвращается как была. Патчей на `SideWindow` нет. `SidebarSourceTests` (23) проверяют форму кода. Спайк «структура `SideWindow` позволяет менять ширину без патчей» сделан по коду и данным (`mod/requests/research-tree-ux.md` п. 15): панель двигается только через `UpdateWindowVisibility` по двум полям. Частично не проверено: иерархии сцены в выгрузке нет (якоря, точка опоры, layout над панелью, растягиваются ли карточки по ширине) — сценарий M11 (потянуть левый край, ширина до 1,6 ×, та же после перезагрузки сцены, закрытая панель целиком за экраном, возврат при выключении фичи) проверит владелец в игре |

## Трассировка FR → TSK

| Требование | Задачи |
|---|---|
| FR-1 | TSK-002, TSK-003, TSK-004 |
| FR-2 | TSK-002, TSK-008, TSK-009 |
| FR-3 | TSK-005, TSK-006 |
| FR-4 | TSK-002, TSK-006, TSK-007 |
| FR-5 | TSK-006, TSK-010, TSK-011 |
| FR-6 | TSK-012, TSK-013 |
| FR-7 | TSK-014 |
| FR-8 | TSK-001, TSK-016 |
| FR-9 | TSK-015 |
| FR-10 | TSK-021 |
| NFR-1, NFR-2 | TSK-003, TSK-018 |
| NFR-3 | TSK-009 (те же вызовы, что кнопка дерева), TSK-020 |
| NFR-4 | TSK-005, TSK-019 |
| NFR-5 | TSK-017 |
| NFR-6 | TSK-004, TSK-013 |
| NFR-7 | TSK-014 |
| NFR-8 | TSK-001, TSK-006, TSK-011, TSK-014 |
