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

- [x] **TSK-001**: Скелет фичи и секция сохранения (авто 2026-10-10)
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

- [x] **TSK-002**: DTO контента и валидатор (авто 2026-10-10)
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

- [x] **TSK-003**: Контент: издания, конфиг обзора, заметки, цитаты (авто 2026-10-10)
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

- [x] **TSK-004**: Математика обзора (оси, эталоны, планка, строгость, ограничение) (авто 2026-10-10)
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

- [x] **TSK-005**: Издания и метаоценка (авто 2026-10-10)
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

- [x] **TSK-006**: Выбор эталонов и сегмент обзора (авто 2026-10-10)
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

- [x] **TSK-007**: Очередь обзоров на событии ядра CpuReleased (авто 2026-10-10)
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

- [x] **TSK-008**: Репутация: изменения, эффект; провайдер канала (авто 2026-10-10)
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

- [x] **TSK-009**: Ажиотаж CPU в провайдере popularity.gain (авто 2026-10-10)
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

- [x] **TSK-010**: Письма обзоров CPU игрока (авто 2026-10-10)
  - **Requirement**: FR-4, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/PressMail.cs`
  - **Acceptance**:
    - Обзор CPU игрока — письмо «Обзоры: X — M/100» (`ModMail.Send`, `Normal`, `Tag` = `press:review:<cpuSaveId>`) с вердиктом, изданиями, цитатами, эффектами словами и действием «Открыть обзор» (`ModMailAction`, `ModMail.RegisterActionHandler`; письмо не закрывается). Письмо не паузит игру. Возвращённый ID хранится в `ReviewRecord.mailId`; блоков ID писем нет.
    - Обзоры соперников отдельных писем и уведомлений `ModNotify` не создают (дайджест — TSK-023).
  - **Depends**: TSK-007, TSK-008, mod-framework/TSK-061, TSK-062 (`ModMail`: приоритеты, `Tag`), TSK-038 (`ModMailAction`)

- [x] **TSK-011**: Карточка обзора (со спайком шаблона) (авто 2026-10-10)
  - **Requirement**: FR-4, NFR-5
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/ReviewCardWindow.cs`
    - заметка о результате спайка `ReviewWindow` в `.kiro/specs/press-reviews/design.md` (раздел Open Issues)
  - **Acceptance**:
    - Окно по умолчанию строится на общем шаблоне `ModUI.CreateWindow(…, WindowTemplate.Panel, …)`. Спайк: пригоден ли объект `ReviewWindow` из `level1` как шаблон. Результат записан; при непригодности остаётся общий шаблон.
    - Карточка показывает: метаоценку и вердикт с цветом темы, 4 оси (число + слово + полоса), строки изданий с цитатами, «Сравнивали с», эффекты и примечания.
    - Тема переключается.
  - **Depends**: TSK-010, mod-framework/TSK-035, TSK-036 (`ModUI`)

- [x] **TSK-012**: Окно «Пресса»: вкладка «Обзоры» (авто 2026-10-10)
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

- [x] **TSK-013**: Окно «Пресса»: «Репутация», «Итоги года», «Статьи»; строка в хабе (авто 2026-10-10)
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

- [x] **TSK-014**: Годовые итоги прессы (авто 2026-10-10)
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

- [x] **TSK-015**: Квартальные заметки прессы (авто 2026-10-10)
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

- [x] **TSK-016**: Публичный API для других фич (авто 2026-10-10)
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

- [x] **TSK-017**: Строки ru/en (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**:
    - `mod/src/PTMod/L10n/ru.json` (ключи `press.*`)
    - `mod/src/PTMod/L10n/en.json` (ключи `press.*`)
  - **Acceptance**:
    - Все ключи из design.md, контента и кода есть в обоих файлах (включая `press.feature.*`, `press.mail.digest.*`, `press.review`).
    - Тест сверки наборов ключей `press.*` зелёный; ключи и теги начинаются с `press.` / `press:` (`ModIds`).
  - **Depends**: TSK-003, TSK-010, TSK-013, TSK-015, TSK-023

## Фаза 5 — Надёжность, калибровка, проверка

- [x] **TSK-018**: Сохранение, загрузка, выключение, миграция (авто 2026-10-10)
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

- [x] **TSK-019**: Самопроверка, изоляция ошибок, замеры (авто 2026-10-10)
  - **Requirement**: NFR-1, NFR-2
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/Diagnostics/PressSelfCheck.cs`
    - `mod/src/PTMod/Features/PressReviews/Diagnostics/PressTimings.cs`
  - **Acceptance**:
    - Строка самопроверки `[PTMod] [press-reviews] INFO self-check OK (… patches, 6 outlets, …, channels OK, reputation OK)`: цели патчей (только `HardwareWindow.ShowMarket` при включённой колонке), издания, заметки, канал `popularity.gain` объявлен, `ModReputation`, `ModEvents`.
    - Исключение в провайдере: ядро засчитывает ×1 и ошибку фичи; провайдер пишет лог не чаще раза в день. Собственных счётчиков ошибок нет (`ModHooks.Guard`, порог ядра).
    - В отладке — среднее время на день раз в месяц и годовая сводка распределения метаоценок: медиана, доля ≥ 85, доля < 40.
  - **Depends**: TSK-007, TSK-008, TSK-009, mod-framework/TSK-042 (`ModSelfCheck`)

- [ ] **TSK-020**: Спайк и колонка «Пресса» в таблице рынка (Could) — Частично (авто 2026-10-10): спайк по коду игры и патч готовы, колонка выключена по умолчанию; проверка в игре — у владельца
  - **Requirement**: FR-13
  - **Deliverables**:
    - `mod/src/PTMod/Features/PressReviews/UI/MarketColumnPatch.cs` (постфикс `HardwareWindow.ShowMarket`, `Optional`, под флагом)
    - результат спайка в design.md
  - **Acceptance**:
    - Спайк определяет, можно ли добавить заголовок колонки без поломки `Spreadsheet`.
    - Если да — колонка с метаоценкой или «—».
    - Если нет — патч не регистрируется, решение записано.
  - **Depends**: TSK-012

- [x] **TSK-021**: Калибровочный тест распределения оценок (авто 2026-10-10)
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

- [x] **TSK-023**: Дайджест обзоров соперников (авто 2026-10-10)
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
| TSK-001 Скелет фичи и секция сохранения | Complete (авто 2026-10-10, кусок 1: `PressReviewsFeature` (`[ModFeature]`, `DependsOn` пуст, `EnabledByDefault` и `SupportsHotToggle` = true), три `ctx.Param` Bool (`instantReviews` выкл., `rivalDigest` и `reputationAffectsAI` вкл.) и `debug.verbose` только под `PTMOD_DEBUG`, `PressSaveAdapter` (`IModSaveParticipant`, `SectionId` = `press-reviews`, `SchemaVersion = 1`) поверх чистых `PressState` и `PressStateCodec` (в `Logic/`, чтобы тестовая сборка их видела; поля версии нет, проверяет тест), сейв без секции даёт пустое состояние; прямых подписок на `DateController`/`Player.OnNewGame`/`SaveHandler` нет (тест архитектуры); `ctx.HubPage` («Пресса», строка репутации) придёт с TSK-012/013, настройки уже видны в хабе по параметрам; видимость в хабе и выключение — по коду, проверить в игре) |
| TSK-002 DTO контента и валидатор | Complete (авто 2026-10-10, кусок 1: `Outlet`, `NoteTemplate`, `ReviewConfig` с вложенными конфигами, `ValidationReport`, `PressContentValidator` (`ParseOutlet`, `ParseNote`, `ParseConfig`, `FindCoverageHoles`, `Validate`, `StubOutlet`) лежат в `Logic/` (тестовая сборка берёт только эту папку), загрузчик `PressContent` через `ModContent` — в `Content/PressContent.cs`; `ContentValidationTests`: битое издание пропускается, веса клипуются и нормируются, `g` 1…2, `tau` 0…1,5, строгость 0,5…2, неизвестный тип условия заметки отбрасывается, дыры покрытия 1971–2030 возвращаются списком, `segmentPriceBand` и `drift` — предупреждение и не хранятся) |
| TSK-003 Контент: издания, конфиг, заметки, цитаты | Complete (авто 2026-10-10, кусок 1: `outlets.json` (6 изданий), `review-config.json` (все числа design.md, `digest`, 32 цитаты по 4 на пул «ось × high/low»; без `drift` и `segmentPriceBand`), `notes.json` (12 заметок, все 6 типов условий), строки ru/en `press-reviews.json` для всего, что называет контент; файлы записей используют массив `records` ядра, а не `outlets`/`notes` из примеров design.md; `BuiltinContentTests`: 0 ошибок и предупреждений, полное покрытие сегментов по годам, значения совпадают с design.md, все ключи есть в ru и en) |
| TSK-004 Математика обзора | Complete (авто 2026-10-10, кусок 1: `ReviewMath` (`TrendFactor`, `EraGrowth`, `AxisFromRatio`, `ThermalAxis`, `Compute`, `ApplyCap`, `WeightedScore`, `NormalizeWeights`), `CpuSnapshot`, `ReviewInput`, `ReviewDraft`, `Axis`; `ReviewMathTests`: 1,0 → 65, 1,5 → 100, 0,8 → 45,7, 0,5 → 5,2, строгость 1,25 → 45,7, `TrendFactor(1; 1,45; 0,6)` = 1,2497, нагрев Ts−35/Ts−15/Ts+5 → 100/50/0, AC-2.1 (73,2/65,0/55,9/62,5; B = 67,4), AC-2.2/2.3 (≈46 и ≈26,5), AC-2.4 (≤ 40), «без аналогов» → 70, цена 0 → ось 0, мощность ≈ 0 без деления на ноль) |
| TSK-005 Издания и метаоценка | Complete (авто 2026-10-10, кусок 1: `OutletPanel` (`ActiveFor` с fallback, `WeightsFor`, `ScoreBy`, `Metascore`, `VerdictKey`, `PickQuoteKey`), `IRandomSource` и адаптер `ModRngSource` над `IModRng` (Box-Muller без запасного значения, чтобы поток восстанавливался из сейва); `OutletPanelTests`: 1990 High End → 3 издания, fallback, нормировка сдвигов, один сид — одни оценки (и после восстановления состояния потока), вердикты на 90/80/70/55/40, цитата по слабой оси < 50 или по сильнейшей; сигнатура `PickQuoteKey` отличается от design.md (см. `mod/requests/press-reviews.md`)) |
| TSK-006 Выбор эталонов и сегмент обзора | Complete (авто 2026-10-10, кусок 1: `Game/PeerSelector` (`MostSoldCpus(15, 3, …)` + чистый `PeerFilter`: другая компания, не на пенсии, продажи сегмента за 3 полных месяца > 0, до 5; `PreviousOwnCpu` как fallback), `Game/CpuSnapshotFactory` (сегмент игрока — `ModSegments.FromSales`, при `Segment.Any` или мгновенном режиме — `ModSegments.Classify`; сегмент ИИ — `Strategy.TargetMarket`; дата выпуска из архива обзоров через `ReleaseDateIndex`, иначе `Year + 0.5`); `PeerSelectionLogicTests` и тест архитектуры «нет `SegmentInference` и своего классификатора»; работа с живым рынком игры — по коду, проверить в игре вместе с TSK-007) |
| TSK-007 Очередь обзоров на CpuReleased | Complete (авто 2026-10-10, кусок 2: `Game/ReviewScheduler` (подписка на `ModEvents.CpuReleased` один раз в `Register`, обработчик в `ModHooks.Guard`; патчей на `AddCpu` нет, `CpuTransferred` не слушается; тик `ctx.OnTick(DayPassed)`: накопление `SoldLastDayBy*` для CPU игрока, публикация в срок, отмена при пенсии CPU, банкротстве компании или пропаже CPU, до 3 попыток с одной ошибкой фичи на третьем сбое) поверх чистых `ReviewQueue` (`Admit`: перемотка, вариант, не выпущен, уже известен; `PickDelay`: 7–21 день из потока `ModRandom` или 0 в мгновенном режиме; `Enqueue`, `DueOn`, `AddSales`, `Fail`, `Cancel`, `Publish` с пределом архива) и `ReviewComposer` (оси `ReviewMath`, оценки изданий `OutletPanel`, метаоценка и вердикт в `ReviewRecord`); тесты `ReviewQueueTests`, `ReviewComposerTests` и проверки архитектуры; мгновенный режим пишет обзор на ближайшем тике дня, а не внутри события; «загрузка не создаёт обзоров» и работа с живым рынком — по коду, проверить в игре по журналу) |
| TSK-008 Репутация: изменения, эффект; провайдер | Complete (авто 2026-10-10, кусок 2: `Effects/ReviewEffects` (`OnReviewPublished`: `ModReputation.Add(company, Δ, "press.review:<id>")`, изменение 0 не пишется; кэш `R` на день, сброс по `ModReputation.Changed` + `ModModifiers.Invalidate`; провайдер `ModModifiers.RegisterReasonedProvider("press-reviews", "popularity.gain", …)` один раз в `Register`, `OnEnabled` только сбрасывает кэши) поверх чистых `EffectMath` (`ReputationDelta`, `ReputationEffect`, `Hype`, `MinContribution`/`MaxContribution`, `Combine`) и `PopularityModel` (`R × H`, параметр `reputationAffectsAI`, причина `press.review` / `press.review.brand`); дрейфа в фиче нет (тест архитектуры); `EffectMathTests`: 90 → +5, 40 → −5, rep 70 → 1,12, вклад 0,42…1,95; строки `press.review` и `press.review.brand` в ru/en) |
| TSK-009 Ажиотаж CPU в провайдере popularity.gain | Complete (авто 2026-10-10, кусок 2: провайдер возвращает `R × H` при `ModifierContext.Cpu != null` (для варианта — H базового CPU, для CPU без обзора — 1,0), клипа сверх H у фичи нет; `HypeBook` строится из архива обзоров (после загрузки и новой игры), обновляется раз в день и удаляет запись на 365-й день; тесты: 90 → 1,357, через 183 дня → 1,178, 30 → 0,6, запись уходит на 365-й день; `PopularityChannelTests` на движке каналов ядра: вклад в `Explain` с причиной, «(ограничено)» при записи ×1,3, ядро поднимает 0,42 до 0,5; вариант берёт H базового и вклад в `Explain` живого CPU — по коду, проверить в игре) |
| TSK-010 Письма обзоров CPU игрока | Complete (авто 2026-10-10, кусок 3: `UI/PressMail` (`SendReview`: `ModMail.Send`, `Normal`, `Tag` = `press:review:<cpuSaveId>`, тема «Обзоры: X — M/100», вердикт, издания с цитатами, эффекты словами, действие «Открыть обзор» (`ModMailAction`, `Repeatable`; `ModMail.RegisterActionHandler` один раз в `Register`, письмо не закрывается), id письма в `ReviewRecord.mailId`; письмо только для CPU игрока, у соперников ни писем, ни `ModNotify` (их дайджест — TSK-023); подписка на `ReviewScheduler.Published` одна, в `Register`) поверх чистых `ReviewMailText` (тег, разбор тега, аргументы письма) и `ReviewCard.EffectLines` в `Logic/`; тесты `ReviewCardTests` (AC-4.1: «Обзоры: X — 84/100», «Отлично», тег, по строке на издание с цитатой, эффекты словами, архивная запись) и `PressUiSourceTests` (путь `ModMail.Send`, `Normal`, действие, нет `ModNotify`); письмо и кнопку «Открыть обзор» проверить в игре: `mod/requests/press-reviews.md`, п. 10) |
| TSK-011 Карточка обзора | Complete (авто 2026-10-10, кусок 3: `UI/ReviewCardWindow` на общем шаблоне `ModUI.CreateWindow(…, WindowTemplate.Panel, …)`, не модальное; метаоценка и вердикт цветом темы (≥ 70 положительный, < 55 отрицательный), 4 оси (число, слово «слабо/средне/хорошо/отлично», полоса), издания с оценкой и цитатой, «Сравнивали с» по именам, эффекты и примечания (в том числе «цена не объявлена»), перекраска по `ModUI.ThemeChanged`; модель карточки — чистый `ReviewCard` в `Logic/` (тесты: тон, границы слов осей, цитаты, эталоны, «без аналогов», эффекты, архивная запись, ru и en). Спайк `ReviewWindow`: непригоден (по дампу сцены: `PauseTrigger`, нет заголовка, кнопки закрытия и `Themeable*`), остаётся общий шаблон; текст заметки для Open Issues в `design.md` — в `mod/requests/press-reviews.md`, п. 7.9 (design.md не в моих файлах); вид окна проверить в игре) |
| TSK-012 Окно «Пресса»: обзоры | Complete (авто 2026-10-10, кусок 3: `UI/PressWindow` (`PressTab`: Reviews, Reputation, Awards, Articles; шаблон Panel, не модальное) и `UI/ReviewsTab`: список «новые сверху» (дата, компания, CPU, сегмент, оценка, вердикт), фильтры компании, сегмента и года на `DropdownUI` (`ModUI.Dropdown`), страницы по 50, клик по строке открывает карточку; логика — чистый `ReviewList` (AC-9.1: 300 обзоров, фильтр «AMD, Mid Range»); архив больше 2000 записей сжимается (`ReviewArchive.Compact`, вызывается из `ReviewQueue.Publish`; тест на `PressState` и круг через `PressStateCodec`); кнопка «Открыть окно «Пресса»» на странице фичи в хабе (`ctx.HubPage`, `UI/PressHubPage`); ряд фильтров и колонки проверить в игре) |
| TSK-013 Окно «Пресса»: репутация, итоги, статьи | Complete (авто 2026-10-10, кусок 3: `UI/ReputationTab` (таблица, игрок первым: значение, уровень, изменение за 12 месяцев, эффект «×1.12», 3 причины из `ModReputation.History`, без дрейфа), `UI/AwardsTab` и `UI/ArticlesTab` читают `PressState`; чистые `ReputationTable`, `ReputationReasons` (`press.review`, `press.award`, `dfr.*`, `rec.*`, `acq.*`, `event:<id>`, остальное «прочее»), `ReputationLevels`, `PressTabViews`; AC-7.1 («68 Уважаемый, +8 за год, ×1.11») и уровни 80/65/36/21 в `ReputationTableTests`; строка «Репутация: N — уровень» на странице фичи в хабе; `PressReviewsApi.FormatReputation` и `GetReputationLevelKey` в `Api/PressReviewsApi.cs` (`partial`, остальное API допишет TSK-016); отклонения: `event:<id>` показывается как «событие», строка репутации живёт на странице фичи; см. `mod/requests/press-reviews.md`, п. 7–8) |
| TSK-014 Годовые итоги | Complete (авто 2026-10-10, кусок 4: `Logic/AwardsCalculator` (CPU года по сегментам ≥ 75, компания года при ≥ 2 обзорах, разочарование < 45; ничья — раньше вышедший обзор, затем меньший `SaveID`; выплаты +4 / +3 / −3 с причинами `press.award:cpu:<год>:<сегмент>`, `press.award:company:<год>`, `press.award:flop:<год>`), `Logic/AwardsMailText`, `Game/AwardsService` (`TickPhase.LateYearPassed`: запись в `PressState.awards`, `ModReputation.Add`, письмо `PressMail.SendAwards`: `Normal`, `Tag` = `press:awards:<год>`, действие «Открыть итоги» открывает вкладку «Итоги года»; год без наград и год с готовыми итогами пропускаются); тесты `AwardsTests` (AC-8.1: 88 в High End → «Процессор года», +4 игроку; пороги 75/45, ничьи, выплаты, пустой год, круг через `PressStateCodec`, письмо ru/en); итоги не досчитываются за год, когда фича была выключена; см. `mod/requests/press-reviews.md`, п. 12.6) |
| TSK-015 Квартальные заметки | Complete (авто 2026-10-10, кусок 4: `Logic/MarketTrends` (рост лидера и изменение медианы цен, `Qualitative`: 0.42 → «около +40 %», округление до десятков процентов, слова «растут / падают / почти не меняются»), `MarketStatsBook` (годовые снимки `yearStats` на 3 года × 4 сегмента, факты, новая компания), `Logic/PressNotes` (`NoteEvaluator` — все 6 типов условий, `NoteSelection` — до 2 заметок по весам без возвращения, `NoteCooldowns`, `NoteRenderer`, `NotesMailText`), `Game/PressNotesService` (`MonthPassed` в месяцы 1/4/7/10: письмо `Low`, `Tag` = `press:notes:<yyyy-MM>`; `LateYearPassed`: снимок рынка; январская заметка сама снимает снимок прошлого года), `MarketSampler` (лидеры сегмента по `MostSoldCpus`); тесты `MarketTrendsTests` (AC-10.1, округление, условия, кулдауны, выбор, тексты всех заметок контента ru/en без коэффициентов); условия вычисляет фича, не `ModConditions`; PGA — по `MarketEventHandler`; см. `mod/requests/press-reviews.md`, п. 12.7) |
| TSK-016 Публичный API | Complete (авто 2026-10-10, кусок 4: `Api/PressReviewsApi` (фасад: `IsEnabled`, `ReviewPublished`, `PublishArticle`, `RegisterAdjuster`/`UnregisterAdjuster`, `GetReview`; при выключенной фиче вызовы — no-op; ошибки хранилища ловятся и засчитываются), `Api/IReviewAdjuster`, `Api/PressArticle`, `Api/IPressApiBackend`, `Api/Model/ReviewModel` (`Axis`, `ReviewDraft`, `OutletLine`, `ReviewRecord` перенесены из `Logic/` в `Api/Model`, чтобы потребители собирались с одной папкой `Api`; для тестовой сборки есть зеркало `Logic/ApiModelMirror.cs`, тест следит за совпадением); `Logic/AdjusterPipeline` (±30 на ось и ±20 на итог на каждого корректировщика и в сумме, исключение корректировщика — его поправка пропущена, обзор опубликован), `AdjusterBook`, `ArticleLog` (письмо `Normal`, `Tag` = `press:article:<n>`, журнал 200 статей), `ReviewRecordCopy`; `Game/PressApiBackend`, `Game/FlawBridge` (бросок `flawReveal.chance` → `FlawRegistry.TryReveal(id, DiscoverySource.External)`; статья и примечание при публичном браке; подключается на `ModTicks.GameReady` и при `ModFeatures.StateChanged`, не в `Register`); тесты `AdjusterPipelineTests` (AC-11.1: −15 → метаоценка ниже на 15), `PressApiLogicTests`, `PressApiSourceTests`; без `design-flaw-recalls` мост не подключается; реальное взаимодействие с той фичей и письмо статьи проверяются в игре; см. `mod/requests/press-reviews.md`, п. 11–12) |
| TSK-017 Строки ru/en | Complete (авто 2026-10-10, кусок 4: в `L10n/ru/press-reviews.json` и `L10n/en/press-reviews.json` по 184 ключа (добавлено 20: итоги года, заметки, статья о браке, `press.note.knownFlaw`, `press.note.flawPublic`, `press.mail.digest.*` для TSK-023); `PressLocalizationTests`: наборы ключей и аргументов ru/en совпадают, все ключи с префиксом `press.` по таблице идентификаторов ядра (`ModIds`), теги писем начинаются с `press:`, ключи design.md, контента и кода есть в обоих языках; отклонения: строки лежат в файлах фичи, а не в общих `ru.json`/`en.json`; `press.effects.hype` разбит на `.up/.down/.none`; см. `mod/requests/press-reviews.md`, п. 12.8) |
| TSK-018 Сохранение, загрузка, выключение | Complete (авто 2026-10-10, кусок 5: `Logic/PressStateHolder` (чистый `ISideFileParticipant`: `State`, `ResetToDefaults`, `Read`, `Write`, поля версии нет), `State/PressSaveAdapter` стал тонкой обёрткой над ним; `Logic/PressStateMigrations` (шаги «версия N → N+1» внутри `Read(section, schema)` на копии секции, ядровой объект не меняется; шагов пока нет, `SectionVersion` = 1, тест `MigrationsCoverEveryVersionOfTheSection` краснеет, если версию подняли без шага; `PressStateCodec.Read` вызывает миграцию первой); `PressStateSerializationTests` идут через настоящую `SideFileModel` ядра: круг без потерь, `_schema` пишет ядро, очередь возвращается с теми же датами (AC-12.1), сейв без секции и секция без `_schema`, секция новее мода (фича получает значения по умолчанию, исходная секция записывается как была), секция выключенной фичи не меняется, клипы и лимиты со строками `save: …` в журнале, ажиотаж после повторного включения кончается в тот же день, провайдер `popularity.gain` регистрируется ровно один раз в `Register` и не затрагивается ни `AfterRead`, ни `OnEnabled`/`OnDisabled`; отклонения: миграции и холдер лежат в `Logic/` (тестовая сборка берёт только эту папку), тесты — в `Features/PressReviews/`; выключение/включение и загрузка в живой игре — сценарии 8-10 владельца) |
| TSK-019 Самопроверка и замеры | Complete (авто 2026-10-10, кусок 5: `Diagnostics/PressSelfCheck` (на `ModTicks.GameReady` и при включении фичи посреди игры) пишет `self-check OK (0 patches, 6 outlets, 12 notes, channels OK, reputation OK)` и по строке на каждую находку: патчи фичи (только `HardwareWindow.ShowMarket` при включённой колонке, ничего на `AddCpu`/`UpdatePopularities`), контент (издания, заметки, ошибки, дыры покрытия), канал `popularity.gain` объявлен и с провайдером, патчи ядра P5 и P20 применены, `ModReputation`, теги `press:` по `ModIds`, строки в таблицах; чистые `Logic/SelfCheckReport` и `SelfCheckRules`; `Diagnostics/PressTimings` (только при `debug.verbose`: среднее и худший день раз в месяц, годовая сводка медианы, доли ≥ 85 и доли < 40 с пометкой «вне целей») поверх `Logic/TimingBook` и `Logic/ScoreDistribution`; исключение в провайдере логируется не чаще раза в день и пробрасывается, ядро засчитывает ×1 и ошибку фичи (тест на движке каналов ядра), собственных счётчиков ошибок нет (тест); `PressDiagnosticsTests`; сама строка и замер в живой игре — у владельца) |
| TSK-020 Колонка в таблице рынка (Could) | Частично (авто 2026-10-10, кусок 5): спайк по декомпилированному коду (`Spreadsheet.AddTitle`, `Row.Extend`, `UpdateRows`, `HardwareWindow.ShowMarket`) и `UI/MarketColumnPatch` (опциональный постфикс на `HardwareWindow.ShowMarket`: заголовок через `AddTitle` раньше любой ячейки, ячейки «метаоценка или —» через `Row.Extend`, повторный `UpdateRows`; строки сопоставляются с CPU по числу, иначе ничего не пишется; всё под `ModHooks.Guard`); патч объявляется в `Register` только если параметр `marketColumn` (по умолчанию выключен) был включён к запуску игры, поэтому по умолчанию `HardwareWindow` не патчится (FR-13 п. 2); чистая часть `Logic/MarketColumn` и `MarketColumnTests`. Не сделано офлайн: ширина гибкой колонки «Industries» после добавления 70 px (ширина таблицы — в сцене) и раскладка заголовка, добавленного после первой раскладки, из дампа не видны; решение «колонка по умолчанию» принимает владелец после проверки в игре; текст для Open Issues design.md — `mod/requests/press-reviews.md`, п. 16.3 |
| TSK-021 Калибровочный тест | Complete (авто 2026-10-10, кусок 5: `CalibrationTests` на настоящих `review-config.json` и `outlets.json`, сложность Normal: 5000 CPU с лог-нормальным разбросом ±25 % (σ = ln 1,25) по трём осям отношений вокруг приведённых эталонов возрастом 0-2 года, нагрев 62 ± 25, годы 1980-2000, сегменты случайно — медиана 64, доля ≥ 85 — 2,9 %, доля < 40 — 2,3 % (цели 55-70 / ≤ 15 % / ≤ 15 %), по сегментам медианы 63 / 63 / 64 / 65; CPU, равный по `Ips` годовалому лидеру при средних прочих осях, получает 52-57 по формулам (и то же по медиане 200 прогонов с шумом изданий; цель 40-60); проверка чувствительности: при сложности 5 тест краснеет; числа целей — `ScoreDistribution`, общие с годовой сводкой отладки) |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Дайджест обзоров соперников | Complete (авто 2026-10-10, кусок 5: `Logic/DigestBuilder` (`ReviewsOfMonth` — обзоры ИИ за прошлый месяц, `Build` — до 10 строк «Компания CPU — M/100 (вердикт)» из громких (M ≥ 85, M ≤ 40, сегмент с продаваемым CPU игрока) в порядке убывания \|M − 65\|, остальные обзоры месяца считаются в «и ещё N обзоров в архиве», пустой месяц — письма нет; `IsDue` по `lastDigest`), `DigestMailText` (тег `press:digest:<yyyy-MM>`, строка «и ещё N» по числу: `press.mail.digest.more.one/.few/.many` и `.one/.other`), `Game/DigestService` (`TickPhase.MonthPassed`, параметр `rivalDigest`, `lastDigest` и поиск письма по тегу защищают от дубля после загрузки и пакетного прогона, письма в пакетном прогоне откладывает ядро), `PressMail.SendDigest` (`Low`, без действий, без `ModNotify`); архив пополняется независимо от параметра; `DigestTests` (AC-4.2: «Intel Y — 91/100 (Шедевр)», AC-4.3: пустой месяц без письма, порядок, границы 85/40, ничьи, лимит 10, плюрал ru/en, источники сервиса); сегменты игрока и само письмо в игре — у владельца) |

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
