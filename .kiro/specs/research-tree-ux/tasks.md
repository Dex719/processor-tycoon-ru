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

- [ ] **TSK-001**: Каркас фичи research-tree-ux
  - **Requirement**: FR-8, NFR-2, NFR-4, NFR-8
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/ResearchTreeUxFeature.cs` (`[ModFeature] sealed class … : IModFeature`: `Id` = `research-tree-ux`, `NameKey`, `DescriptionKey`, `DependsOn` = пусто, `EnabledByDefault` = true, `SupportsHotToggle` = true, `Register(FeatureContext)`, `OnEnabled`, `OnDisabled`), `ResearchUxPrefs.cs` (чтение и запись через `ModProfile.Section`), `ResearchUxSaveParticipant.cs` (`IModSaveParticipant`, `SchemaVersion` = 1, без собственного поля версии), `UxParts.cs`; параметры `notifyNewTech` и `notifyIdleLab` через `ctx.Param`; пустые ключи `rtux.*` в `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json`; `mod/src/PTMod/Content/research-tree-ux/ui.json` с данными из `design.md`
  - **Acceptance**: сборка проходит; ядро находит фичу по `[ModFeature]`, она видна в хабе «Моды», `ModFeatures.StateOf("research-tree-ux")` = Enabled; самопроверка пишет в лог список найденных целей (`ResearchTreeWindow.InitializeUI`, `ResearchTreeWindow.UpdateButtonStates`, `InspectorWindow.Start`) и приватных полей; при отсутствии поля соответствующий флаг `UxParts` снят; в коде фичи нет прямых подписок на `DateController`, `Player`, `SaveHandler` и собственных счётчиков ошибок
  - **Depends**: mod-framework/TSK-021 (`ModSettings`, `ModProfile`, `ModL10n`, `ModContent`), mod-framework/TSK-022 (`IModFeature`, `FeatureContext`), mod-framework/TSK-023 (`ModHooks`), mod-framework/TSK-025 (`ModSave`), mod-framework/TSK-043 (`core.json`: префикс `rtux`)

- [ ] **TSK-002**: Чистая модель технологий и вкладок
  - **Requirement**: FR-1, FR-2, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/TechSnapshot.cs`, `TechCategory.cs`, `TabDefinition.cs`, `CategoryFilter.cs`, `UnnecessaryRule.cs`, `UnlocksIndex.cs` (обратные зависимости); `mod/tests/PTMod.Tests/ResearchTreeUx/CategoryFilterTests.cs`, `UnnecessaryRuleTests.cs`
  - **Acceptance**: тесты покрывают 7 типов × «Все», линии с одним скрытым концом, `FocusTarget` (доступная/исследованная/пусто), правило «ненужной» для IDM/fabless/foundry (совпадает с `ResearchButton.TechnologyIsUnnecessary`); все публичные типы в `[Covers]`; все тесты зелёные
  - **Depends**: TSK-001, mod-framework/TSK-067 (мини-раннер)

- [ ] **TSK-003**: Индекс дерева и сопоставление линий
  - **Requirement**: FR-1, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tree/TreeIndex.cs`, `Logic/LineMatcher.cs`, `Patches/TreeWindowPatches.cs` (`[ModPatch]` postfix `ResearchTreeWindow.InitializeUI`, вызывающий `OnTreeBuilt`); `mod/tests/PTMod.Tests/ResearchTreeUx/LineMatcherTests.cs`
  - **Acceptance**: `LineMatcher` проходит тесты (точное совпадение, допуск 0,5 px, несопоставленные); в отладочном логе после загрузки сцены: «кнопок 97, линий N, сопоставлено N»; исключение в постфиксе не доходит до игры (`ModHooks.Guard`; проверка искусственным исключением в debug-сборке)
  - **Depends**: TSK-002, mod-framework/TSK-023 (`ModHooks`)

## Фаза 2 — Дерево: вкладки, метки, подсказки

- [ ] **TSK-004**: Полоса вкладок и фильтр дерева
  - **Requirement**: FR-1
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tree/TabStrip.cs`, `Tree/TreeFocus.cs` (центрирование через `ResearchTreeController.controller`); ключи `rtux.tab.*` в `L10n/ru.json`, `L10n/en.json`
  - **Acceptance**: 8 встроенных вкладок по `ui.json`; выбор вкладки скрывает чужие кнопки и линии, «Все» возвращает исходное состояние; вид центрируется на первой доступной технологии вкладки; последняя вкладка запоминается в `ModProfile` и восстанавливается после загрузки сейва; переключение ≤ 5 мс в отладочном замере
  - **Depends**: TSK-003, mod-framework/TSK-036 (виджеты `ModUI`), mod-framework/TSK-021 (`ModProfile`)

- [ ] **TSK-005**: Метки «новое» и счётчики вкладок
  - **Requirement**: FR-3
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/NewTechTracker.cs`, `Tree/NewBadges.cs`; постфикс `ResearchTreeWindow.UpdateButtonStates` в `Patches/TreeWindowPatches.cs` (ранний выход при перемотке); `ResearchUxSaveParticipant.Read`/`Write` (секция `research-tree-ux`, `List<int> seenAvailable`, без поля версии); обработчик `ModTicks.GameReady` (`NewGame`: всё доступное просмотрено; `Loaded` без секции: то же); `mod/tests/PTMod.Tests/ResearchTreeUx/NewTechTrackerTests.cs`
  - **Acceptance**: тесты трекера зелёные (непросмотренные, отметка, «всё доступное просмотрено», сериализация, отбрасывание неверных ID); в игре метка «НОВОЕ» появляется у новой доступной технологии, снимается при уходе с вкладки и при наведении; после сохранения/загрузки не возвращается; старый сейв без секции — меток нет; на старте новой игры меток нет
  - **Depends**: TSK-004, mod-framework/TSK-024 (`ModTicks`, `GameReady`), mod-framework/TSK-025 (`ModSave`)

- [ ] **TSK-006**: Подсказки через `ModUI.Tooltip` и отметка просмотра
  - **Requirement**: FR-3, FR-4, FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Tooltips/TechTooltips.cs` (адаптер `Attach`/`Detach` к `ModUI.Tooltip`), `Tooltips/HoverSeenTracker.cs` (время под курсором, `hoverSeenSeconds`)
  - **Acceptance**: `ModUI.Tooltip` на тестовой кнопке показывает родной `Tooltip` с родной задержкой, текст строится при наведении; `HoverSeenTracker` вызывает `OnSeen` после `hoverSeenSeconds` и не срабатывает при коротком проходе курсора; при `TooltipHandler.Instance` = null — без ошибок (гарантия ядра); в коде фичи нет собственного `TooltipData`, `TooltipHandler.StartTrigger` и локального моста
  - **Depends**: TSK-001, mod-framework/TSK-064 (`ModUI.Tooltip`)

- [ ] **TSK-007**: Подсказка технологии
  - **Requirement**: FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/TechTooltipBuilder.cs`, `Logic/ResearchContext.cs`, `Tree/TechSnapshotReader.cs`; подключение `TechTooltips.Attach` к каждой `ResearchButton`; ключи `rtux.tip.*`; `mod/tests/PTMod.Tests/ResearchTreeUx/TechTooltipBuilderTests.cs`
  - **Acceptance**: тесты: DDR6 в 1990 → «+13 300 %», 130nm в 2005 → «−45 %», BaseTime = 0 → «мгновенно», Funding ≈ 0 → «∞», ненужная для fabless, внешние строки; в игре время в подсказке совпадает с временем на кнопке дерева
  - **Depends**: TSK-002, TSK-006

## Фаза 3 — Список

- [ ] **TSK-008**: Модель списка исследований
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/ResearchListModel.cs`, `Logic/ListFilter.cs`; `mod/tests/PTMod.Tests/ResearchTreeUx/ResearchListModelTests.cs`
  - **Acceptance**: тесты: три фильтра в комбинациях, три сортировки, ничьи по ID, группы с «исследовано/всего», пустой результат; все зелёные
  - **Depends**: TSK-002

- [ ] **TSK-009**: Вид «Список» и выбор исследования
  - **Requirement**: FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/List/ResearchListView.cs`, `List/ResearchListRow.cs`; переключатель «Дерево / Список» в `Tree/TabStrip.cs`; строка «Лаборатория занята: …» (читает `ILabOccupancySource` после TSK-014); ключи `rtux.list.*`, `rtux.status.*`, `rtux.lab.busy`
  - **Acceptance**: список заменяет область дерева; строки обновляются раз в день (`TickPhase.DayPassed`); «Исследовать» вызывает `ResearchSector.SetCurrentResearch(Technology)`, `CurrentResearchUI.SetTechnology(Technology)`, `ResearchTreeWindow.ForceUIUpdate()`; для «ненужной» — `UnnecessaryTechnologyWarning.Open`; кнопка неактивна у исследованных, заблокированных и текущей; пустой фильтр — строка «Нет исследований по фильтру»; обновление ≤ 3 мс; вид и фильтры запоминаются в `ModProfile`
  - **Depends**: TSK-004, TSK-008, TSK-007, mod-framework/TSK-036

## Фаза 4 — Модификаторы и сравнение

- [ ] **TSK-010**: Таблица ванильных модификаторов и отчёт
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/VanillaModifierTable.cs`, `Logic/ModifierReportBuilder.cs`, `Logic/ModifierSnapshot.cs` (включая `ExplainData`/`ExplainRow`); `mod/tests/PTMod.Tests/ResearchTreeUx/VanillaModifierTableTests.cs`, `ModifierReportBuilderTests.cs`
  - **Acceptance**: тесты: 7 типов × флаги IDM/foundry/fabless/TCMS/«невезучая» совпадают с кодом `Modifiers.GetModifier`; строки источников из `ExplainData` (фича, причина, множитель, срок); «(ограничено)» при `IsBalanceCapped`/`IsFuseCapped`; `Explain` недоступен → «Моды ×k» = итог/ваниль; проекты в работе; внешние группы; пример AC-5.1 даёт «×1,29»
  - **Depends**: TSK-002

- [ ] **TSK-011**: Кнопка и панель «Все модификаторы»
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Modifiers/ModifiersPanel.cs`, `Modifiers/ModifierSnapshotReader.cs` (чтение `Company.ResearchModifiers`, `TimerScheduler.GetTimedObjectsOfType<Project>()`, `Project.GetResearchProject()`, `Modifiers.GetModifier`, `ResearchMath.ResearchSpeed`, `ModModifiers.Explain` для 7 каналов типов 0–6 с `ModifierContext {Company = игрок}`); ключи `rtux.mod.*`
  - **Acceptance**: наведение на кнопку показывает подсказку (`ModUI.Tooltip`), клик открывает закреплённую панель; сценарии AC-5.1…AC-5.4 выполняются в игре; построение ≤ 1 мс
  - **Depends**: TSK-010, TSK-006, mod-framework/TSK-027 (`ModModifiers`), mod-framework/TSK-059 (`Explain`, причины провайдеров), mod-framework/TSK-035 (`ModWindow`)

- [ ] **TSK-012**: Модель сравнения CPU
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/CompareModel.cs`, `Logic/CpuFacts.cs`, `Logic/IFormatter.cs`; `mod/tests/PTMod.Tests/ResearchTreeUx/CompareModelTests.cs`
  - **Acceptance**: тесты: дельта (B − A)/|A|, «—» при A = 0 и нечисловых, порог 2,5 %, направления higher/lower/none, A = B → 0 %, внешние строки по `Order`; все зелёные
  - **Depends**: TSK-002, mod-framework/TSK-067

- [ ] **TSK-013**: Окно сравнения и кнопка в инспекторе
  - **Requirement**: FR-6
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Compare/CompareWindow.cs`, `Compare/CpuFactsReader.cs`, `Compare/GameFormatter.cs` (обёртка `StringFormatter`), `Patches/InspectorPatches.cs` (`[ModPatch]` postfix `InspectorWindow.Start`); страница хаба с кнопкой «Сравнить CPU» (`ctx.HubPage`); ключи `rtux.compare.*`
  - **Acceptance**: кнопка «Сравнить» рядом с «Detailed/Simplified»; окно открывается с A = текущий CPU инспектора; выбор компании/CPU для B (включая соперников, только поля, доступные в инспекторе), «Поменять местами», подсветка лучшей стороны цветами `Colors`; CPU обанкротившейся компании сбрасывается без исключений (AC-6.3); из хаба A = последний CPU игрока
  - **Depends**: TSK-012, mod-framework/TSK-035, mod-framework/TSK-036, mod-framework/TSK-039 (хаб, `HubPage`)

## Фаза 5 — Расширения, напоминания, выключение

- [ ] **TSK-014**: API расширения ResearchTreeUxApi
  - **Requirement**: FR-7, NFR-7
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Api/ResearchTreeUxApi.cs`, `Api/IResearchTabProvider.cs`, `Api/ICompareRowProvider.cs`, `Api/IModifierLineSource.cs`, `Api/ITechTooltipLineSource.cs`, `Api/ILabOccupancySource.cs`; подключение в `TabStrip`, `CompareWindow`, `ModifiersPanel`, `TechTooltipBuilder`, `ResearchListView`, `NotifierPolicy`; отладочная вкладка-заглушка (только debug-сборка); `mod/tests/PTMod.Tests/ResearchTreeUx/ApiRegistryTests.cs`
  - **Acceptance**: регистрация до и после построения окна работает; дубль `TabId` отклоняется с предупреждением; исключение провайдера отключает только его и записывается через `ModHooks.ReportFault` на фичу-владельца (AC-7.2); `IsActive` = false при выключенной фиче, регистрации запоминаются и действуют после включения; `ILabOccupancySource` опрашивается списком и политикой напоминаний (AC-2.4, AC-9.2); отладочная вкладка видна после «Ядра»
  - **Depends**: TSK-004, TSK-007, TSK-011, TSK-013, mod-framework/TSK-022 (`ModFeatures.FeatureOfType`, `StateChanged`), mod-framework/TSK-023 (`ReportFault`)

- [ ] **TSK-015**: Напоминания о новых исследованиях и простое лаборатории
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Notify/ResearchUxNotifier.cs`, `Logic/NotifierPolicy.cs`; переключатели в хабе (`ParamSpec`); ключи `rtux.notify.*`; `mod/tests/PTMod.Tests/ResearchTreeUx/NotifierPolicyTests.cs`
  - **Acceptance**: тесты политики зелёные (включая «лаборатория занята источником»); в игре сценарий AC-9.1: одно уведомление на 8-й день простоя, без повторов 30 дней; AC-9.2: лаборатория, занятая архитектурой, простоем не считается; во время перемотки напоминаний нет; в пакетном прогоне `NextMonth` показ откладывает `ModNotify`; выключенный переключатель — уведомлений нет
  - **Depends**: TSK-005, TSK-014, mod-framework/TSK-033 (`ModNotify`), mod-framework/TSK-062 (отложенный показ на `BatchEnded`)

- [ ] **TSK-016**: Выключение посреди игры и восстановление вида
  - **Requirement**: FR-8
  - **Deliverables**: `ResearchTreeUxFeature.OnDisabled/OnEnabled` (снятие и восстановление UI, `TechTooltips.Detach`, снятие `HoverSeenTracker`), страница фичи в хабе «Моды»
  - **Acceptance**: сценарий M9: после выключения дерево идентично ванильному (все кнопки и линии `activeSelf` = true, объектов мода нет), окно сравнения закрыто; после включения UI восстановлен без перезагрузки сцены; дневные обработчики фичи не вызываются, пока она выключена
  - **Depends**: TSK-004, TSK-009, TSK-011, TSK-013, mod-framework/TSK-039 (хаб: горячее переключение)

## Фаза 6 — Проверка

- [ ] **TSK-017**: Локализация ru/en и тест полноты ключей
  - **Requirement**: NFR-5
  - **Deliverables**: все ключи `rtux.*` в `mod/src/PTMod/L10n/ru.json` и `mod/src/PTMod/L10n/en.json`; `mod/tests/PTMod.Tests/ResearchTreeUx/L10nKeysTests.cs`
  - **Acceptance**: тест находит каждый ключ, используемый в коде фичи, в обоих файлах; все ключи имеют префикс `rtux.` (кроме `core.*` ядра); в игре с языком `en` нет «сырых» ключей
  - **Depends**: TSK-009, TSK-011, TSK-013, TSK-015, mod-framework/TSK-043, mod-framework/TSK-066 (`ModIds`)

- [ ] **TSK-018**: Самопроверка, замеры производительности и тесты контента
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**: замеры `ModLog` (`ModSettings.Debug`) в `TabStrip.Select`, `ResearchListView.Refresh`, постфиксе `UpdateButtonStates`, построении подсказки; `mod/tests/PTMod.Tests/ResearchTreeUx/ContentSchemaTests.cs`
  - **Acceptance**: в отладочной сессии: вкладка ≤ 5 мс, список ≤ 3 мс, дневной тик при закрытом окне ≤ 0,1 мс, подсказка ≤ 1 мс; тест битого `ui.json` проходит (битая запись пропущена, при полном отказе — значения по умолчанию)
  - **Depends**: TSK-016

- [ ] **TSK-019**: Совместимость сейвов
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/tests/PTMod.Tests/ResearchTreeUx/SaveSectionTests.cs` (нет секции, пустая, ID вне диапазона, дубликаты, > 512 элементов, `ResetToDefaults`)
  - **Acceptance**: все тесты зелёные; участник не читает и не пишет поле версии (`_schema` — дело ядра, проверяется `mod-framework/TSK-016`); секция новее мода сохраняется ядром как есть при выключенной фиче
  - **Depends**: TSK-005, mod-framework/TSK-016 (модель бокового файла)

- [ ] **TSK-020**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-9, NFR-1 … NFR-8
  - **Deliverables**: отметки о прохождении сценариев M1–M10 из `design.md` (заполняет владелец в этом файле или в PR)
  - **Acceptance**: все сценарии M1–M10 пройдены; в `Player.log` за сессию ≥ 2 часов нет исключений `[PTMod]` от research-tree-ux; остаточный вопрос из `requirements.md` («сравнение чужих CPU и туманность») подтверждён или решён
  - **Depends**: TSK-017, TSK-018, TSK-019

## Фаза 7 — Could (после гейта)

- [ ] **TSK-021**: Регулируемая ширина боковой панели
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/ResearchTreeUx/Logic/SidebarWidthRule.cs`, `Sidebar/SidebarResizer.cs` (ручка на левом крае `SideWindow`, масштаб `RectTransform`, пересчёт `visiblePosition`/`hiddenPosition` через `AccessTools`, запись множителя в `ModProfile`); флаг `UxParts.Sidebar`; ключи `rtux.sidebar.*`; `mod/tests/PTMod.Tests/ResearchTreeUx/SidebarWidthRuleTests.cs`
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
| TSK-001 Каркас фичи | Pending |
| TSK-002 Чистая модель технологий и вкладок | Pending |
| TSK-003 Индекс дерева и сопоставление линий | Pending |
| TSK-004 Полоса вкладок и фильтр дерева | Pending |
| TSK-005 Метки «новое» и счётчики | Pending |
| TSK-006 Подсказки через ModUI.Tooltip | Pending |
| TSK-007 Подсказка технологии | Pending |
| TSK-008 Модель списка | Pending |
| TSK-009 Вид «Список» и выбор исследования | Pending |
| TSK-010 Ванильные модификаторы и отчёт | Pending |
| TSK-011 Панель «Все модификаторы» | Pending |
| TSK-012 Модель сравнения CPU | Pending |
| TSK-013 Окно сравнения и кнопка в инспекторе | Pending |
| TSK-014 API расширения | Pending |
| TSK-015 Напоминания | Pending |
| TSK-016 Выключение посреди игры | Pending |
| TSK-017 Локализация ru/en | Pending |
| TSK-018 Самопроверка и производительность | Pending |
| TSK-019 Совместимость сейвов | Pending |
| TSK-020 Ручная проверка (владелец) | Pending |
| TSK-021 Ширина боковой панели (Could) | Pending |

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
