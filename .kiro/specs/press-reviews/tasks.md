# Tasks: press-reviews

## Document Information

- **Feature Name**: press-reviews — обзоры и бенчмарки в прессе, репутация бренда
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/press-reviews/requirements.md`
  - Design: `.kiro/specs/press-reviews/design.md`
  - Решения владельца: `.kiro/steering/owner-decisions.md`
  - Задачи ядра: `.kiro/specs/mod-framework/tasks.md` (ссылки вида `mod-framework/TSK-0xx`)

## Implementation Overview

Порядок такой:
1. Чистая математика обзора и изданий, полностью покрытая тестами и калибровочным тестом на синтетическом рынке.
2. Очередь обзоров на событии ядра `ModEvents.CpuReleased`.
3. Эффекты через `ModReputation` и провайдер стандартного канала `popularity.gain` (репутация компании × ажиотаж CPU) в общем пределе ядра [0,5; 2,0].
4. Письма и окна, дайджест соперников, годовые итоги и заметки.
5. API для других фич, надёжность.

Каждая задача — 2–4 часа и оставляет игру рабочей при выключенной фиче. Задача TSK-023 (дайджест) добавлена в v0.2 и стоит после TSK-022, чтобы не менять нумерацию.

### Implementation Strategy
- Математика (`ReviewMath`, `OutletPanel`, `EffectMath`, `DigestBuilder`, `MarketTrends`) — без `UnityEngine`, в `mod/src/PTMod/Features/PressReviews/Logic/`. Тесты — мини-раннер .NET Framework 4.8 в `mod/tests/PTMod.Tests/PressReviews/` (`python mod/build.py --tests`, атрибуты `[Test]`, `[Covers(typeof(X))]`); каждый публичный тип `Logic` указан в `[Covers]`. Код `Logic` — общее подмножество API .NET Framework 4.8 и mscorlib Unity (без `init`, `record`, `Index`, `Range`).
- Формулы рынка не патчим. Эффекты — только провайдеры каналов (единый патч рынка в `mod-framework`). **Патчей на `Company.AddCpu` и `AICompany.AddCpu` нет** (ядро отклонит): выпуск CPU берётся из `ModEvents.CpuReleased`. Своего классификатора сегмента нет — `ModSegments`. Дрейфа репутации нет — его ведёт ядро.
- Числа — в `mod/src/PTMod/Content/press-reviews/*.json`.
- Если в ядре недоступен канал `popularity.gain` (сбой P5) или `ModReputation`, соответствующий эффект отключается с предупреждением. Обзоры и письма работают.
- Сборка — `python mod/build.py --game-dir <папка игры>` (Roslyn `csc`, без .NET SDK); отладочные флаги `debug.*` только в отладочной сборке.

### Development Approach
- **Testing Strategy**: TDD для математики; калибровочный тест распределения оценок; самопроверка на старте; ручные сценарии владельца.
- **Integration Strategy**: регистрация в ядре как `[ModFeature] IModFeature`; API (`PressReviewsApi`) для `design-flaw-recalls`, `rival-personalities`, `historical-events`. Связь с `design-flaw-recalls` — мягкая (подключается на `ModTicks.GameReady` и при `ModFeatures.StateChanged`).
- **Deployment Strategy**: `install.py --with-mods`. Фича включена по умолчанию, мгновенные обзоры и отладка выключены, дайджест соперников включён.

## Фаза 1 — Каркас, контент, математика

- [ ] **TSK-001**: Скелет фичи и секция сохранения
  - **Requirement**: FR-12
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/PressReviewsFeature.cs`
    - `mod/src/PTMod/Features/PressReviews/State/PressState.cs`
    - `mod/src/PTMod/Features/PressReviews/State/PressSaveAdapter.cs` (`IModSaveParticipant`)
  - **Acceptance**:
    - `[ModFeature] sealed class PressReviewsFeature : IModFeature`: `Id` = `press-reviews`, `NameKey`/`DescriptionKey` (`press.feature.name`/`press.feature.desc`), `DependsOn` пуст (мягкие связи не включаются), `EnabledByDefault` и `SupportsHotToggle` = true. Тики, параметры и страница хаба — через `FeatureContext` (`ctx.OnTick`, `ctx.SaveParticipant`, `ctx.Param`, `ctx.HubPage`); прямых подписок на `DateController`, `Player.OnNewGame` и `SaveHandler` нет.
    - Фича видна в хабе «Моды» (вкл/выкл) с параметрами `ctx.Param`: «Мгновенные обзоры» (`instantReviews`, выкл.), «Дайджест обзоров соперников» (`rivalDigest`, вкл.), «Репутация влияет на ИИ» (`reputationAffectsAI`, вкл.); `debug.verbose` — только в отладочной сборке.
    - Участник сохранения: `SectionId` = `press-reviews`, `SchemaVersion = 1`; `Write()` не содержит `_schema` (его пишет ядро); поля версии в `PressState` нет.
    - Сейв без секции загружается с пустым состоянием.
  - **Depends**: mod-framework/TSK-022 (`FeatureContext`, `ModFeatures`), TSK-025 (`ModSave`), TSK-021 (`ModSettings`), TSK-067 (мини-раннер)

- [ ] **TSK-002**: DTO контента и валидатор
  - **Requirement**: FR-3, FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Content/PressContentModels.cs`
    - `mod/src/PTMod/Features/PressReviews/Content/PressContent.cs`
    - `mod/tests/PTMod.Tests/PressReviews/ContentValidationTests.cs`
  - **Acceptance**:
    - Битое издание пропускается.
    - Отрицательные веса клипуются, веса нормируются.
    - `g` ∈ [1, 2], `τ` ∈ [0, 1.5], строгость ∈ [0.5, 2].
    - Неизвестный тип условия заметки отбрасывается.
    - Проверка покрытия «каждый сегмент в каждом году 1971–2030 имеет издание (с fallback)» возвращает список дыр.
    - Поле `segmentPriceBand` в контенте отсутствует (правило сегмента принадлежит ядру).
  - **Depends**: mod-framework/TSK-015 (`ModContent`), TSK-067

- [ ] **TSK-003**: Контент: издания, конфиг обзора, заметки, цитаты
  - **Requirement**: FR-2, FR-3, FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Content/press-reviews/outlets.json` (6 вымышленных изданий)
    - `mod/src/PTMod/Content/press-reviews/review-config.json` (включая `digest`; без `drift` и `segmentPriceBand`)
    - `mod/src/PTMod/Content/press-reviews/notes.json` (не менее 8 заметок)
  - **Acceptance**:
    - Значения совпадают с design.md.
    - Тест загрузки встроенного контента: 0 ошибок, полное покрытие сегментов по годам.
    - Для каждой оси минимум 3 цитаты `high` и 3 `low` (ключи `press.quote.*` перечислены в контенте).
  - **Depends**: TSK-002

- [ ] **TSK-004**: Математика обзора (оси, эталоны, планка, строгость, ограничение)
  - **Requirement**: FR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Logic/ReviewMath.cs`
    - `mod/src/PTMod/Features/PressReviews/Logic/CpuSnapshot.cs`
    - `mod/tests/PTMod.Tests/PressReviews/ReviewMathTests.cs`
  - **Acceptance**: тесты проходят:
    - `AxisFromRatio`: 1.0 → 65, 1.5 → 100, 0.8 → 45.7, 0.5 → 5.2; строгость 1.25 → 45.7 при r = 1.
    - `TrendFactor(1, 1.45, 0.6)` = 1.2497.
    - Нагрев: Ts−35 → 100, Ts−15 → 50, Ts+5 → 0.
    - AC-2.1: оси 73.2/65.0/55.9/62.5, B = 67.4.
    - AC-2.4: итог ≤ 40.
    - «Без аналогов» → 70.
    - Цена 0 → ось 0.
    - Мощность ≈ 0 → защищённое деление.
  - **Depends**: mod-framework/TSK-067

- [ ] **TSK-005**: Издания и метаоценка
  - **Requirement**: FR-3
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Logic/OutletPanel.cs`
    - `mod/src/PTMod/Features/PressReviews/Logic/IRandomSource.cs`
    - `mod/tests/PTMod.Tests/PressReviews/OutletPanelTests.cs`
  - **Acceptance**:
    - 1990 High End → 3 издания.
    - Fallback для года без покрытия.
    - Нормировка сдвигов.
    - Один сид — одинаковые оценки (адаптер `IRandomSource` над `IModRng`, поток `ModRandom.Stream("press-reviews")`).
    - Вердикты на границах 90/80/70/55/40.
    - Цитата по сильнейшей оси или по слабой, если та < 50.
  - **Depends**: TSK-004

- [ ] **TSK-006**: Выбор эталонов и сегмент обзора
  - **Requirement**: FR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Game/PeerSelector.cs`
    - `mod/src/PTMod/Features/PressReviews/Game/CpuSnapshotFactory.cs`
    - `mod/tests/PTMod.Tests/PressReviews/PeerSelectionLogicTests.cs` (чистая часть фильтра)
  - **Acceptance**:
    - Сегмент CPU игрока — `ModSegments.FromSales` по накопленным продажам, при `Segment.Any` или в мгновенном режиме — `ModSegments.Classify(cpu)`; сегмент CPU ИИ — `Strategy.TargetMarket`. Типов `SegmentInference` и собственного классификатора нет (тест архитектуры); значения 312/311/187/186/99/98 проверяются в `SegmentRuleTests` ядра.
    - `PeerSelector` берёт `MarketDataProvider.Instance.MostSoldCpus(15, 3, …)` и фильтрует: другая компания, не на пенсии, продажи сегмента за 3 полных месяца > 0. Отдаёт до 5 эталонов.
    - `PreviousOwnCpu` — fallback.
    - Дата выпуска эталона берётся из архива обзоров, иначе `Year + 0.5`.
  - **Depends**: TSK-004, mod-framework/TSK-055 (`ModSegments`)

## Фаза 2 — Очередь и эффекты

- [ ] **TSK-007**: Очередь обзоров на событии ядра CpuReleased
  - **Requirement**: FR-1
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Game/ReviewScheduler.cs` (подписка `ModEvents.CpuReleased` в `Register`)
  - **Acceptance**:
    - Патчей на `Company.AddCpu` / `AICompany.AddCpu` нет. Обработчик отсекает `duringFastForward`, `isVariant` и `cpu.IsReleased == false`, дедуплицирует по `SaveID`; загрузку и повтор на один объект CPU отсекает ядро; `CpuTransferred` обзор не создаёт.
    - Задержка 7–21 день (`ModRandom`) или 0 в мгновенном режиме.
    - Ежедневное накопление `SoldLastDayBy*` для CPU игрока.
    - Публикация в срок через `ReviewMath` и `OutletPanel`.
    - Отмена, если CPU на пенсии или компания банкрот.
    - До 3 попыток при исключении.
    - Загрузка сейва не создаёт новых обзоров (проверка по логу); CPU, выпущенные в перемотку, не рецензируются.
  - **Depends**: TSK-005, TSK-006, TSK-001, mod-framework/TSK-056 (`ModEvents`), TSK-024 (`ModTicks`), TSK-025 (`ModRandom`)

- [ ] **TSK-008**: Репутация: изменения, эффект; провайдер канала
  - **Requirement**: FR-6, FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Effects/ReviewEffects.cs` (репутационная часть + `Provide`)
    - `mod/src/PTMod/Features/PressReviews/Logic/EffectMath.cs`
    - `mod/tests/PTMod.Tests/PressReviews/EffectMathTests.cs`
  - **Acceptance**:
    - После обзора `ModReputation.Add(company, clamp(round((M−65)/5), −6, 6), "press.review:<id>")`.
    - `ModModifiers.RegisterReasonedProvider("press-reviews", "popularity.gain", …)` зарегистрирован один раз в `Register` и не пересоздаётся при `OnEnabled`/`OnDisabled`. Для компании провайдер возвращает `R = 1 + 0.3 × (rep − 50)/50` (кэш на день, сброс по `ModReputation.Changed` + `ModModifiers.Invalidate`), для ИИ — по параметру `reputationAffectsAI`; причина `press.review`.
    - **Дрейфа репутации в фиче нет:** `ModReputation.Add` для дрейфа и причина `press.drift` не используются (дрейф ведёт ядро, `ModReputation.Drift`).
    - Тесты: 90 → +5, 40 → −5, rep 70 → 1.12; `MaxContribution` = 0.42…1.95; `EffectMath` не содержит `Drift`.
  - **Depends**: TSK-007, mod-framework/TSK-018, TSK-026 (`ModReputation`, дрейф ядра), TSK-027, TSK-029 (канал `popularity.gain`, P5), TSK-059 (причины, `Invalidate`, балансовый диапазон)

- [ ] **TSK-009**: Ажиотаж CPU в провайдере popularity.gain
  - **Requirement**: FR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Effects/ReviewEffects.cs` (часть ажиотажа)
    - дополнение `EffectMathTests.cs`
  - **Acceptance**:
    - При `ModifierContext.Cpu != null` провайдер возвращает `R × H`, где `H = clamp(1 + 0.5 × (M−65)/35 × decay, 0.6, 1.5)`. Для вариантов берётся `H` базового CPU, для CPU без обзора — 1.0.
    - Через 365 дней запись ажиотажа удаляется из кэша.
    - Тесты: 90 → 1.357, через 183 дня → 1.178, 30 → 0.6.
    - Итоговое произведение с вкладами других фич ограничивает ядро [0,5; 2,0]: фича собственного клипа сверх `H` не делает; `ModModifiers.Explain("popularity.gain", ctx)` показывает вклад `press-reviews` для CPU игрока и «(ограничено)» при срабатывании предела (проверка на записи `ModifierTimed` ×1.3).
  - **Depends**: TSK-008

## Фаза 3 — Письма и интерфейс

- [ ] **TSK-010**: Письма обзоров CPU игрока
  - **Requirement**: FR-4, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/PressMail.cs`
  - **Acceptance**:
    - Обзор CPU игрока — письмо «Обзоры: X — M/100» (`ModMail.Send`, `Normal`, `Tag` = `press:review:<cpuSaveId>`) с вердиктом, изданиями, цитатами, эффектами словами и действием «Открыть обзор» (`ModMailAction`, `ModMail.RegisterActionHandler`; письмо не закрывается). Письмо не паузит игру. Возвращённый ID хранится в `ReviewRecord.mailId`; блоков ID писем нет.
    - Обзоры соперников отдельных писем и уведомлений `ModNotify` не создают (дайджест — TSK-023).
  - **Depends**: TSK-007, TSK-008, mod-framework/TSK-061, TSK-062 (`ModMail`: приоритеты, `Tag`), TSK-038 (`ModMailAction`)

- [ ] **TSK-011**: Карточка обзора (со спайком шаблона)
  - **Requirement**: FR-4, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/ReviewCardWindow.cs`
    - заметка о результате спайка `ReviewWindow` в `.kiro/specs/press-reviews/design.md` (раздел Open Issues)
  - **Acceptance**:
    - Окно по умолчанию строится на общем шаблоне `ModUI.CreateWindow(…, WindowTemplate.Panel, …)`. Спайк: пригоден ли объект `ReviewWindow` из `level1` как шаблон. Результат записан; при непригодности остаётся общий шаблон.
    - Карточка показывает: метаоценку и вердикт с цветом темы, 4 оси (число + слово + полоса), строки изданий с цитатами, «Сравнивали с», эффекты и примечания.
    - Тема переключается.
  - **Depends**: TSK-010, mod-framework/TSK-035, TSK-036 (`ModUI`)

- [ ] **TSK-012**: Окно «Пресса»: вкладка «Обзоры»
  - **Requirement**: FR-9
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/PressWindow.cs` (перечисление вкладок `PressTab`)
    - `mod/src/PTMod/Features/PressReviews/UI/ReviewsTab.cs`
  - **Acceptance**:
    - Список (новые сверху) с фильтрами компании, сегмента, года на `DropdownUI` и пагинацией по 50.
    - Клик открывает карточку.
    - Архив больше 2000 записей сжимается (тест на `PressState`).
    - Кнопка «Пресса» на странице хаба «Моды» (`ctx.HubPage`).
  - **Depends**: TSK-011, mod-framework/TSK-039 (хаб)

- [ ] **TSK-013**: Окно «Пресса»: «Репутация», «Итоги года», «Статьи»; строка в хабе
  - **Requirement**: FR-7
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/ReputationTab.cs`
    - `mod/src/PTMod/Features/PressReviews/UI/AwardsTab.cs`
    - `mod/src/PTMod/Features/PressReviews/UI/ArticlesTab.cs`
  - **Acceptance**:
    - Таблица репутации (игрок первым): значение, уровень, изменение за 12 месяцев, эффект «×1.12», 3 причины. Изменение и причины берутся из `ModReputation.History` (только `Add`/`Set`, дрейф ядра в истории отсутствует); причины, начинающиеся с ключей L10n фич (`press.review`, `press.award`, `dfr.*`, `rec.*`, `acq.*`) или `event:<id>`, переводятся в текст, неизвестные — «прочее».
    - Строка «Репутация: N — уровень» в хабе.
    - `FormatReputation` и `GetReputationLevelKey` доступны в API.
    - Вкладки итогов и статей читают состояние.
  - **Depends**: TSK-008, TSK-012

## Фаза 4 — Итоги, заметки, дайджест, API, строки

- [ ] **TSK-014**: Годовые итоги прессы
  - **Requirement**: FR-8
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Game/AwardsService.cs`
    - `mod/tests/PTMod.Tests/PressReviews/AwardsTests.cs`
  - **Acceptance**:
    - В фазе `TickPhase.LateYearPassed`:
      - «Процессор года» по сегментам (≥ 75);
      - «Компания года» (≥ 2 обзора);
      - «Разочарование года» (< 45);
      - репутация +4 / +3 / −3 (причины `press.award:*`);
      - письмо (`Normal`, `Tag` = `press:awards:<year>`) с действием «Открыть итоги» (`ModMailAction`);
      - запись в архив.
    - Пустой год — без письма.
  - **Depends**: TSK-008, TSK-010

- [ ] **TSK-015**: Квартальные заметки прессы
  - **Requirement**: FR-10
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Logic/MarketTrends.cs`
    - `mod/src/PTMod/Features/PressReviews/Game/PressNotesService.cs`
    - `mod/tests/PTMod.Tests/PressReviews/MarketTrendsTests.cs`
  - **Acceptance**:
    - Годовой снимок лидера и медианы цен по сегментам.
    - В месяцы 1/4/7/10 — до 2 заметок по условиям и кулдаунам; письмо `Low`, `Tag` = `press:notes:<yyyy-MM>`.
    - Числа только качественные («около +40 %»).
    - Тест округления: 0.42 → «около +40 %».
  - **Depends**: TSK-003, TSK-010

- [ ] **TSK-016**: Публичный API для других фич
  - **Requirement**: FR-11
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Api/PressReviewsApi.cs`
    - `mod/src/PTMod/Features/PressReviews/Api/IReviewAdjuster.cs`
    - `mod/src/PTMod/Features/PressReviews/Api/PressArticle.cs`
    - `mod/src/PTMod/Features/PressReviews/Game/FlawBridge.cs`
    - `mod/tests/PTMod.Tests/PressReviews/AdjusterPipelineTests.cs`
  - **Acceptance**:
    - Событие `ReviewPublished` поднимается после сохранения записи.
    - `PublishArticle` даёт письмо (`Normal`, `Tag` = `press:article:<n>`) и запись во вкладке «Статьи».
    - Поправки корректировщиков клипуются ±30 (ось) / ±20 (итог); исключение корректировщика не ломает обзор.
    - `GetReview` возвращает запись или `null`.
    - При выключенной фиче `IsEnabled == false`, вызовы — no-op.
    - При активной `design-flaw-recalls` бросок `flawReveal.chance` вызывает `FlawRegistry.TryReveal(id, DiscoverySource.External)`. Подписка на `FlawRegistry.OnFlawStateChanged` даёт статью при публичном браке. `FlawBridge` подключается на `ModTicks.GameReady` и при `ModFeatures.StateChanged`, а не в `Register`; без `design-flaw-recalls` не подключается.
  - **Depends**: TSK-007, TSK-013, design-flaw-recalls/TSK-020 (API `FlawRegistry`, мягкая зависимость), mod-framework/TSK-024 (`GameReady`)

- [ ] **TSK-017**: Строки ru/en
  - **Requirement**: NFR-4
  - **Deliverables**:
    - `mod/src/PTMod/L10n/ru.json` (ключи `press.*`)
    - `mod/src/PTMod/L10n/en.json` (ключи `press.*`)
  - **Acceptance**:
    - Все ключи из design.md, контента и кода есть в обоих файлах (включая `press.feature.*`, `press.mail.digest.*`, `press.review`).
    - Тест сверки наборов ключей `press.*` зелёный; ключи и теги начинаются с `press.` / `press:` (`ModIds`).
  - **Depends**: TSK-003, TSK-010, TSK-013, TSK-015, TSK-023

## Фаза 5 — Надёжность, калибровка, проверка

- [ ] **TSK-018**: Сохранение, загрузка, выключение, миграция
  - **Requirement**: FR-12, NFR-3
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/State/PressStateMigrations.cs`
    - `mod/tests/PTMod.Tests/PressReviews/PressStateSerializationTests.cs`
  - **Acceptance**:
    - Round-trip секции без потерь; в данных нет поля версии (`_schema` пишет ядро).
    - Клипы и лимиты при чтении.
    - Очередь переживает сохранение и загрузку с теми же датами.
    - После `ModSave.AfterRead` провайдер не регистрируется повторно (регистрация одна, в `Register`).
    - `OnDisabled` замораживает очередь; провайдер не удаляется и не пересоздаётся (ядро пропускает его, пока фича выключена); после повторного включения ажиотаж продолжается с прежним сроком.
    - Секция новее мода (`_schema` больше `SchemaVersion`): `Read` получает значения по умолчанию, исходная секция сохраняется ядром. Миграции версий — внутри `Read(section, schema)`.
  - **Depends**: TSK-008, TSK-009, TSK-014, mod-framework/TSK-025

- [ ] **TSK-019**: Самопроверка, изоляция ошибок, замеры
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Diagnostics/PressSelfCheck.cs`
    - `mod/src/PTMod/Features/PressReviews/Diagnostics/PressTimings.cs`
  - **Acceptance**:
    - Строка самопроверки `[PTMod] [press-reviews] INFO self-check OK (… patches, 6 outlets, …, channels OK, reputation OK)`: цели патчей (только `HardwareWindow.ShowMarket` при включённой колонке), издания, заметки, канал `popularity.gain` объявлен, `ModReputation`, `ModEvents`.
    - Исключение в провайдере: ядро засчитывает ×1 и ошибку фичи; провайдер пишет лог не чаще раза в день. Собственных счётчиков ошибок нет (`ModHooks.Guard`, порог ядра).
    - В отладке — среднее время на день раз в месяц и годовая сводка распределения метаоценок: медиана, доля ≥ 85, доля < 40.
  - **Depends**: TSK-007, TSK-008, TSK-009, mod-framework/TSK-042 (`ModSelfCheck`)

- [ ] **TSK-020**: Спайк и колонка «Пресса» в таблице рынка (Could)
  - **Requirement**: FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/MarketColumnPatch.cs` (постфикс `HardwareWindow.ShowMarket`, `Optional`, под флагом)
    - результат спайка в design.md
  - **Acceptance**:
    - Спайк определяет, можно ли добавить заголовок колонки без поломки `Spreadsheet`.
    - Если да — колонка с метаоценкой или «—».
    - Если нет — патч не регистрируется, решение записано.
  - **Depends**: TSK-012

- [ ] **TSK-021**: Калибровочный тест распределения оценок
  - **Requirement**: FR-2 (Acceptance Metrics)
  - **Deliverables**:
    - `mod/tests/PTMod.Tests/PressReviews/CalibrationTests.cs`
  - **Acceptance**:
    - Синтетический рынок: 5000 CPU с лог-нормальным разбросом ±25 % по осям относительно эталонов, возраст эталонов 0–2 года, Normal.
    - Результат: медиана 55–70, доля ≥ 85 ≤ 15 %, доля < 40 ≤ 15 %.
    - CPU = годовалому лидеру при средних прочих осях → 40–60.
    - Падение теста — сигнал правки `review-config.json`.
  - **Depends**: TSK-004, TSK-005

- [ ] **TSK-022**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1 … FR-13, NFR-1 … NFR-5
  - **Deliverables**: протокол `.kiro/specs/press-reviews/manual-check.md` (дата, версия, результат по 12 сценариям design.md)
  - **Acceptance**:
    - Сценарии 1–11 пройдены (сценарий 5 — в отладочной сборке).
    - Сценарий 12 (баланс 1980–2000) укладывается в метрики. Иначе — задача на донастройку `τ`, `g` или весов.
  - **Depends**: TSK-001 … TSK-021, TSK-023

- [ ] **TSK-023**: Дайджест обзоров соперников
  - **Requirement**: FR-4
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Logic/DigestBuilder.cs`
    - `mod/src/PTMod/Features/PressReviews/Game/DigestService.cs`
    - `mod/tests/PTMod.Tests/PressReviews/DigestTests.cs`
  - **Acceptance**:
    - В фазе `TickPhase.MonthPassed` (1-е число) при включённом параметре `rivalDigest` отправляется одно письмо «Пресса: обзоры соперников» (`ModMail.Send`, `Low`, `Tag` = `press:digest:<yyyy-MM>`) с обзорами CPU ИИ за прошлый месяц.
    - В письме до 10 строк в порядке убывания |M − 65|: обзоры с M ≥ 85 или ≤ 40 и обзоры в сегментах, где у игрока есть продаваемый CPU; остальные — «и ещё N обзоров в архиве». Нет подходящих — письма нет.
    - `lastDigest` защищает от дубля после загрузки и пакетного прогона месяца (`ModTicks.IsBatchRun`: письмо откладывает ядро).
    - Отдельных писем и `ModNotify` на обзоры соперников нет; архив пополняется независимо от параметра.
  - **Depends**: TSK-007, TSK-010, mod-framework/TSK-061, TSK-062 (`ModMail`), TSK-057 (`IsBatchRun`)

## Dependency graph

```
TSK-002 ──> TSK-003
TSK-004 ──┬──> TSK-005 ──┐
          ├──> TSK-006 ──┤
          └──> TSK-021 <─┘ (TSK-004 + TSK-005)

TSK-001, TSK-005, TSK-006 ──> TSK-007 ──> TSK-008 ──> TSK-009

TSK-007, TSK-008 ──> TSK-010 ──> TSK-011 ──> TSK-012 ──┬──> TSK-013
                                                       └──> TSK-020
TSK-008, TSK-010 ──> TSK-014
TSK-003, TSK-010 ──> TSK-015
TSK-007, TSK-010 ──> TSK-023 (дайджест)
TSK-007, TSK-013 ──> TSK-016
TSK-003, TSK-010, TSK-013, TSK-015, TSK-023 ──> TSK-017
TSK-008, TSK-009, TSK-014 ──> TSK-018
TSK-007, TSK-008, TSK-009 ──> TSK-019
всё ──> TSK-022 (владелец)

Внешние зависимости (mod-framework): TSK-022/025/021/067 ──> TSK-001; TSK-055 ──> TSK-006; TSK-056, TSK-024 ──> TSK-007;
TSK-018/026/027/029/059 ──> TSK-008; TSK-061/062/038 ──> TSK-010, TSK-023; TSK-035/036/039 ──> TSK-011/012; TSK-042 ──> TSK-019.
Мягкая: design-flaw-recalls/TSK-020 ──> TSK-016.
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Скелет фичи и секция сохранения | Pending |
| TSK-002 DTO контента и валидатор | Pending |
| TSK-003 Контент: издания, конфиг, заметки, цитаты | Pending |
| TSK-004 Математика обзора | Pending |
| TSK-005 Издания и метаоценка | Pending |
| TSK-006 Выбор эталонов и сегмент обзора | Pending |
| TSK-007 Очередь обзоров на CpuReleased | Pending |
| TSK-008 Репутация: изменения, эффект; провайдер | Pending |
| TSK-009 Ажиотаж CPU в провайдере popularity.gain | Pending |
| TSK-010 Письма обзоров CPU игрока | Pending |
| TSK-011 Карточка обзора | Pending |
| TSK-012 Окно «Пресса»: обзоры | Pending |
| TSK-013 Окно «Пресса»: репутация, итоги, статьи | Pending |
| TSK-014 Годовые итоги | Pending |
| TSK-015 Квартальные заметки | Pending |
| TSK-016 Публичный API | Pending |
| TSK-017 Строки ru/en | Pending |
| TSK-018 Сохранение, загрузка, выключение | Pending |
| TSK-019 Самопроверка и замеры | Pending |
| TSK-020 Колонка в таблице рынка (Could) | Pending |
| TSK-021 Калибровочный тест | Pending |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Дайджест обзоров соперников | Pending |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-007 |
| FR-2 | TSK-003, TSK-004, TSK-006, TSK-021 |
| FR-3 | TSK-002, TSK-003, TSK-005 |
| FR-4 | TSK-010, TSK-011, TSK-023 |
| FR-5 | TSK-009 |
| FR-6 | TSK-008 |
| FR-7 | TSK-008, TSK-013 |
| FR-8 | TSK-014 |
| FR-9 | TSK-012 |
| FR-10 | TSK-002, TSK-003, TSK-015 |
| FR-11 | TSK-016 |
| FR-12 | TSK-001, TSK-018 |
| FR-13 | TSK-020 |
| NFR-1, NFR-2 | TSK-019 |
| NFR-3 | TSK-018 |
| NFR-4 | TSK-017 |
| NFR-5 | TSK-010, TSK-011 |
