# Запросы фичи design-flaw-recalls к итоговому интегратору

Папка `mod/src/PTMod/Features/DesignFlawRecalls/Api/` (публичный API фичи) готова. Чужие файлы не менялись; ниже то, что нужно сделать вне Api.

## 1. Чистые типы Api/Model должны попасть в сборку тестов

Перечисления `FlawSeverity`, `FlawState`, `DiscoverySource`, `FlawResponse`, класс `FlawRecordView`, константы `FlawResponseBiasOrder` и `FlawTypeIds` лежат в `Features/DesignFlawRecalls/Api/Model/` (они публичные и нужны потребителям: `press-reviews`, `historical-events`, `rival-personalities` собираются с одной лишь папкой `Api`). Файлы чистые (только `System.Collections.Generic`, без Unity и игры, проверено компиляцией под .NET Framework 4.8).

Чистая логика фичи (`Features/DesignFlawRecalls/Logic/**`: `FlawRiskModel.PickSeverity`, `FlawStateMachine`, `DiscoveryModel.PickSource` и т.д.) использует эти типы, но `build.py --tests` и `build_feature.py <slug> --tests` собирают только `Core/Logic`, `Core/Compat`, `Features/*/Logic` и тесты, так что `Api/Model` в тестовой сборке не видно.

Просьба: в `build.py` (`build_tests`, строки около 430-440) и в `build_feature.py` (`build_feature_tests`) добавить в список папок `Features/<Pascal>/Api/Model` (если папка есть). Правило такое же, как для Logic: файлы в `Api/Model` не ссылаются на Unity, Harmony и `Assembly-CSharp`. Желательно распространить на архитектурный тест `LogicSources_DoNotReferenceUnityOrTheGame` (проверять и `Features/*/Api/Model`).

Пока этого нет: агентам, пишущим `Logic/**` и тесты, достаточно файла в Logic с зеркалом перечислений только для тестовой сборки (символ `PTMOD_TESTS` задаётся лишь в тестовой сборке, в DLL его нет, дубликатов не будет):

```csharp
#if PTMOD_TESTS
namespace PTMod.Features.DesignFlawRecalls { public enum FlawSeverity { Minor = 0, Major = 1, Critical = 2 } /* ... остальные из Api/Model/FlawEnums.cs */ }
#endif
```

Когда интегратор подключит `Api/Model` к тестам, зеркало нужно удалить (иначе CS0101).

## 2. DesignFlawRecallsFeature объявлен как partial

Спека (design.md, Component 1) помещает `public static FlawRegistry Registry` в класс `DesignFlawRecallsFeature`, а `historical-events` вызывает `DesignFlawRecallsFeature.Registry.ForceFlaw(...)`. Потребители собираются без реализации фичи, поэтому часть класса с `Registry` лежит в `Api/DesignFlawRecallsFeature.Api.cs`. Реализация обязана объявить свою часть так:

```csharp
[ModFeature]
public sealed partial class DesignFlawRecallsFeature : IModFeature { ... }   // без своего Registry
```

Если реализация объявит класс без `partial` или добавит второй `Registry`, будет CS0260 или CS0102.

## 3. FlawRegistry — фасад, хранилище отдельно

`FlawRegistry` (публичные члены из спеки) делегирует вызовы `IFlawRegistryBackend`, который реализация присоединяет в `Register`/`OnEnabled` вызовом `DesignFlawRecallsFeature.Registry.AttachBackend(store)` и снимает в `OnDisabled` (`DetachBackend(store)`). Пока бэкенда нет или фича не `Enabled`, все публичные методы возвращают `false` / пустой список / `null`. Внутренние члены из design.md (`Create`, `AttachVariant`, `WasRolled`, `MarkRolled`, `IsRush`, `MoveOwner`, `Active`) можно добавлять в `partial class FlawRegistry` в файлах фичи или держать в классе хранилища.

Спеки потребителей (`rival-personalities`, `press-reviews`) пишут `FlawRegistry.SetResponseBias(...)` и `FlawRegistry.OnFlawStateChanged += ...` как короткую запись; в коде это вызовы через экземпляр: `DesignFlawRecallsApi.Registry.SetResponseBias(...)` (или `DesignFlawRecallsFeature.Registry`).

## 4. Зеркало `Api/Model` в Logic и куда делись типы секции сохранения (кусок 1: TSK-001..003)

- **Зеркало.** Пока п. 1 не выполнен, `Features/DesignFlawRecalls/Logic/ApiModelMirror.cs` повторяет `Api/Model/FlawEnums.cs` (четыре перечисления, `FlawResponseBiasOrder`, `FlawTypeIds`) под `#if PTMOD_TESTS`; в `PTMod.dll` его нет. Тест `FlawFoundationTests.ApiModelMirror_IsACopyOfTheEnumsOfTheApi` следит, чтобы копия не разошлась с оригиналом. Когда интегратор добавит `Api/Model` в тестовые сборки (`build.py`, `build_feature.py`), файл `Logic/ApiModelMirror.cs` нужно удалить (иначе CS0101); тест при отсутствии файла ничего не проверяет и проходит. `FlawRecordView` в зеркало не включён (Logic им пока не пользуется): кому понадобится в `Logic/`, тот добавляет его в зеркало и в `[Covers]`.
- **Типы секции сохранения лежат в `Logic/Save/`.** По tasks.md они в `Save/`, но сборка тестов берёт только `Features/<Pascal>/Logic`, а тесты TSK-003 (круговая сериализация, `ResetToDefaults`, закрытие записей) должны их компилировать. Поэтому: `Logic/Save/FlawSaveData.cs` (DTO), `Logic/Save/FlawSaveCodec.cs` (Bind/From, миграция, проверка значений), `Logic/Save/FlawSaveSection.cs` (участник на логическом `ISideFileParticipant`), а `Save/FlawSaveSection.cs` — вторая половина того же `partial`-класса, которая добавляет `IModSaveParticipant` (пустой интерфейс-наследник `ISideFileParticipant`), нужный для `ctx.SaveParticipant`. Ничего менять не нужно; просьба не переносить файлы обратно.
- **Тесты фичи лежат в `mod/tests/PTMod.Tests/Features/DesignFlawRecalls/`**, а не в `.../PTMod.Tests/DesignFlawRecalls/`, как написано в tasks.md: `build_feature.py` отсекает только подпапки `Features/<чужая фича>`, а файл вне `Features/` попал бы в сборку тестов каждой фичи и сломал бы её (нет типов). Ссылки на путь тестов в tasks.md для следующих кусков читать с поправкой.

## 5. Просьба к ядру: узнать, что секция фичи удержана как «новее мода» (TSK-003)

Спека (design.md, «Error Handling») требует: секция со схемой новее мода → ядро отдаёт фиче значения по умолчанию и хранит исходную секцию, а фича **для этого сейва не работает**. Ядро это делает (`SideFileModel`: `ResetToDefaults`, `_locked`, исходный текст пишется обратно), но участник узнаёт о таком случае только как о вызове `ResetToDefaults()`, неотличимом от «секции нет».

Обход в фиче: `ModSave.AfterRead` → если фича включена, её секция не читалась (`FlawSaveSection.WasRead == false`) и `ModSave.Status == SideFileStatus.Newer`, то `FlawSaveSection.LockForNewerSchema()` (флаг `DesignFlawRecallsFeature.SaveLocked`; следующие задачи проверяют его перед созданием записей). Это консервативно: если «новее» оказалась секция другой фичи, а секции этой фичи в старом сейве нет, то фича тоже будет считаться заблокированной для этого сейва.

Нужно (минимум): публичный член ядра, который говорит, держит ли ядро секцию этой фичи как «новее» или «нечитаемую», например `ModSave.IsSectionHeldBack(string featureId)` (на основе `SideFileModel._locked`) или список `ModSave.NewerSections`. Тогда `OnSaveRead` заменяется на точную проверку (одна строка в `DesignFlawRecallsFeature.OnSaveRead`).

## 6. Кусок 2 (TSK-004..009): что лежит где и что нужно итоговому интегратору

Новых просьб к ядру и чужим фичам нет: бросок использует `ModEvents.CpuReleased`, `ModCompanies`, `ModRandom`, монитор — `ctx.OnTick`, `ModTicks.GameReady`, `ModHooks.Guard`, `ModCompanies.Resolve`, `BankruptcyHandler.OnCompanyWentBankrupt`. Для интегратора:

- **Чистая логика** (в сборке тестов): `Logic/FlawRiskModel.cs`, `Logic/DiscoveryModel.cs`, `Logic/ResponseCostCalculator.cs` + `Logic/FlawQuote.cs`, `Logic/FlawStateMachine.cs`, `Logic/FlawRecord.cs`, `Logic/FlawStore.cs`, `Logic/FlawCycle.cs` (месячный и дневной цикл за интерфейсом `IFlawCycleHost`), `Logic/FlawRollEngine.cs` (бросок при выпуске за `IFlawRollHost`), `Logic/FlawMath.cs`; дни — `Logic/FlawDays.cs` (добавлены `AddMonths`, `FullMonthsBetween`, `MonthKey`, `YearOf`).
- **Игровая сторона** (только в DLL): `FlawRoller.cs`, `FlawMonitor.cs`, `FlawViews.cs`, `FlawRegistry.cs` (внутренняя половина `partial`-класса `FlawRegistry`, публичная — в `Api/FlawRegistry.cs`), `Game/GameCpuView.cs`. Тесты `FlawRoller`/`FlawMonitor` без игры невозможны: правила проверены на чистых `FlawRollEngine`/`FlawCycle` с фейковым хостом, привязка к игре — тестами по исходнику (подписки, `ModHooks.Guard`, отсутствие `IsBatchRun`/`NextMonth` и патчей на `AddCpu`). Тесты лежат в `mod/tests/PTMod.Tests/Features/DesignFlawRecalls/` (см. п. 4).
- **Хранилище API ещё не присоединено.** `FlawRegistry.AttachBackend(...)` вызывает TSK-020; до него публичные методы реестра отвечают «пустым объектом». `FlawMonitor.Cycle.Reveal(record, source)` — готовая точка входа для `TryReveal`/`ForceFlaw`.
- **Места, которые заполнят следующие задачи** (помечены в `FlawMonitor`): `Discovered` (окно решения, выбор ИИ, базовая репутация и популярность — TSK-010/013/015), `Leaked` и `SteppingCompleted` (TSK-011), `IgnoreDrain` и `ClassActionFiled` (TSK-012), снятие записей `ModModifiers` в `StateChanged` для закрытой записи (TSK-014). Пока они не заполнены, найденный брак остаётся в `InternalKnown`/`Public` без решения — это ожидаемо до TSK-010.
- **Отличия от диаграммы состояний design.** Таблица переходов — диаграмма плюс два перехода из требований: `Closed` из любого не итогового состояния (FR-4 п. 5) и `Expired` для известного, но игнорированного брака (FR-9 п. 6). Тест `Table_IsTheDiagramOfTheDesign_CellByCell` фиксирует таблицу.
- **Допущения о данных игры.** Дату снятия CPU с рынка игра не хранит: «месяцев с момента снятия» считаются как число закрытых месяцев подряд без продаж у CPU с `IsRetired`. Текущий (незакрытый) месяц журнала `MarketData` в окно «нет продаж» не входит.

## 7. Кусок 3 (TSK-010..014): что лежит где, что нужно интегратору

Новых правок ядра и чужих файлов не требуется. Фича использует из `event-system` только публичный `Api` (`EventRegistry.RegisterPack`, `EventEffects.Register`, `EventBus.Fire` / `OnChoiceResolved`, `EventFireContext`, `EventInstance`, `AppliedEffect`, `IEventEffectHandler`); из ядра — `ModModifiers.Add/Remove/Explain`, `ModReputation.Add`, `ModMail.Send`, `ModHooks.Guard/ReportFault`, `ModRandom.Stream`.

- **Чистая логика** (в сборке тестов): `Logic/FlawReputationTable.cs`, `Logic/FlawPopularity.cs`, `Logic/FlawChannelBook.cs`, `Logic/FlawResponseEngine.cs`, `Logic/FlawDecisionPlanner.cs`, `Logic/FlawDecisionEngine.cs`, `Logic/FlawEffectModel.cs`. В `FlawCycle` добавлен пересчёт котировки при утечке.
- **Игровая сторона** (только в DLL): `FlawChannels.cs`, `FlawResponseApplier.cs`, `FlawDecisionDispatcher.cs`, `FlawText.cs`, `Effects/*.cs`; `FlawMonitor.cs` и `DesignFlawRecallsFeature.cs` подключены к ним.
- **Контент:** `Content/design-flaw-recalls/events.json` (6 событий) и `config.json` (категория `design-flaw`). Оба файла проверены реальными `DefinitionMapper`, `ContentValidator` и `PackCatalog` из event-system через временную сборку вне репозитория (6 событий, ни одной ошибки или предупреждения). В сборку тестов самой фичи код event-system не входит, поэтому такого теста в репозитории нет: итоговому интегратору стоит добавить в `mod/tests/PTMod.Tests/Features/EventSystem/` (или в общий набор полной сборки) тест, который грузит пак `design-flaw-recalls` через `EventTestKit.WithCatalog` и четыре типа эффектов (`FlawRecall`, `FlawStepping`, `FlawSoftwarePatch`, `FlawIgnore`) в `FakeEnvironment.EffectTypes`.
- **Допущения о поведении `event-system`**, которые надо сверить, когда будет готова фабрика экземпляров (сейчас `EventEngine` работает с `DryRunRuntime`, а `Fire` без неё ничего не создаёт; фича в этом случае применяет выбор по умолчанию и шлёт письмо `dfr:auto:<id>`, зависших записей нет):
  1. `EventBus.OnChoiceResolved` поднимается после `Apply` выбранного эффекта;
  2. в `EventContext.Instance.Data.args` на момент `Resolve` лежат `EventFireContext.Args` (`Args[0]` = id записи);
  3. `Instance.Data.auto` выставлен до `Apply`, когда выбор делает таймер (иначе флаг `automaticChoice` записи фича всё равно выставляет в `OnChoiceResolved`);
  4. исключение из `Apply` не уходит выше движка: эффект получает `Failed`, запись остаётся в ожидании, а `OnChoiceResolved` фича обрабатывает повторной попыткой и, если и она не вышла, ставит запись в конец очереди окон;
     (если движок отложил срабатывание (`Scheduled`) и сбросил его через `DeferLimitDays` = 30, окно так и не появится: раз в месяц `FlawDecisionEngine.Sweep` применяет выбор по умолчанию к окну, открытому дольше 74 дней, и шлёт письмо);
  5. `AppliedEffect.id`, `startDay`, `endDay` заполняет движок (обработчики фичи их не трогают, `state = Pending`).
- **Тест ядра, который ломается от пофичевой сборки.** `ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` (и два соседних `BuiltDll_*`) берёт `PTMod.dll` из каталога сборки теста и, если он свежее всех исходников, требует в нём ресурсы ВСЕХ фич. После `python mod/build_feature.py <slug>` в `mod/build/features/<slug>/` лежит DLL только этой фичи, и при «тихом» дереве (никто не менял исходники после сборки) тест падает с длинным списком «не встроено». Обычно его спасает то, что соседние фичи правят файлы и DLL оказывается старой (тест пропускается), но это случайность. Просьба: пропускать `BuiltDll_*` для сборок из `mod/build/features/`, либо искать только `mod/build/PTMod.dll`. Перед сдачей куска файлы `PTMod.dll`/`PTMod.pdb` в `mod/build/features/design-flaw-recalls/` удалены, чтобы тест пропускался.
- **Известные пробелы (не требуют действий интегратора, для следующих кусков):**
  - ветка ИИ в `FlawDecisionDispatcher.DispatchAi` возвращает `false` (TSK-015, TSK-016): брак ИИ после обнаружения остаётся в `InternalKnown`/`Public` без ответа, но базовая репутация и популярность публичного брака уже применяются;
  - CPU-вариант, выпущенный во время действия заплатки, записи `market.rating.cpu` не получает (запись делается для CPU, которые есть в записи в момент выбора);
  - флаг `recallable` типа брака не используется: кнопка «Отзыв» есть всегда (в design нет события без неё);
  - после переноса CPU при поглощении (TSK-027) новое окно выбора строится по `FlawDecisionPlanner.KindOf`: запись с `coverUp` и источником `Leak` получит событие `leaked*` (без кнопки игнора); если нужно иное, TSK-027 передаёт вид явно;
  - отзыв внутреннего (ещё неизвестного публике) брака даёт по таблице только `+recall`, «публичную базу» не добавляет: таблица design п. 7 так сформулирована, при желании это правится одной строкой в `FlawResponseEngine.Recall`.

## 8. Кусок 4 (TSK-015, TSK-016): ИИ-соперники и новости

Правок ядра и чужих файлов не требуется. Просьб к `event-system` нет, но есть три места, которые надо сверить глазами в игре (см. ниже).

- **Чистая логика** (в сборке тестов): `Logic/AiFlawPolicy.cs` (`AiDecisionInput`, `AiFlawPolicy`), `Logic/FlawAiEngine.cs` (`IFlawAiHost`, `FlawAiEngine`), `Logic/FlawNewsPlanner.cs` (`FlawNewsKind`, `FlawNewsPlan`, `FlawNewsPlanner`); в `Logic/FlawDecisionEngine.cs` добавлен `RetryAi` (его зовёт `Sweep`), в `Logic/FlawEffectModel.cs` — `FlawEffectTypes.News` и `FlawEffectModel.NewsKey`.
- **Игровая сторона** (только в DLL): `FlawNews.cs`, `Effects/FlawNewsEffect.cs`; `FlawDecisionDispatcher` теперь реализует `IFlawAiHost` (ветка ИИ: `DispatchAi` → `FlawAiEngine.Decide`), `DesignFlawRecallsFeature` создаёт `News` и регистрирует эффект `FlawNews`. `IFlawResponseBias.cs` остался в `Api/` (публичный контракт, сигнатура прежняя), а не в корне папки фичи, как написано в tasks.md.
- **Решения, которых нет в спеке дословно:**
  1. После утечки (`CoverUp` и источник `Leak`) ИИ выбирает без игнора, как игрок (три кнопки). Если все веса нулевые, он берёт ревизию (у обычного брака — игнор по формуле 8).
  2. Отзыв внутреннего (ещё не публичного) брака ИИ считается новостью: отзыв делается открыто. Ревизия, заплатка и игнор такого брака — тихая реакция, новости нет до утечки (FR-11 п. 5).
  3. Строка журнала событий для публичного инцидента пишется через `EventEffects.ApplyDirect` с собственным эффектом `FlawNews` (он ничего не меняет в игре, `Apply` пустой). Записи в `EventLog` напрямую публичный API `event-system` не даёт (только чтение и окно). `EventEffect.params` оставлен `null`: `ApplyDirect` подставляет пустые параметры сам, а `EffectParams` лежит вне `Api/` и в пофичевую сборку не попадает. Если появится `EventLog.AddNote(featureId, key, args)`, `FlawNews.WriteJournal` стоит перевести на него и удалить эффект.
  4. Месячный проход (`FlawMonitor.OnMonth` → `Dispatcher.Sweep` → `RetryAi`) повторно предлагает политике ИИ запись, которая осталась без ответа (исключение политики или оплаты): иначе такой брак ждал бы вечно. Возврат `Sweep` по-прежнему считает только окна игрока.
- **Что проверить в игре (не запускалось):**
  1. Окно «Журнал событий» `event-system` должно показать `Direct`-экземпляр с `eventId = dfr.news.ai` (у него нет определения события): строка `dfr.fx.news` под ним и разумный заголовок (иначе он может показать сырой id). Если заголовок плох, это правка окна журнала, не фичи.
  2. Письмо `Normal` с тегом `dfr:news:<id>` и уведомление `Minor` приходят один раз на инцидент; в перемотку и загрузку их держит ядро.
  3. Деньги: `AICompany.SpendMoney` вызывается через `ICompany`; у ИИ с отрицательным запасом отзыв отсечён политикой, ревизия и заплатка не проверяют деньги (как у игрока).
- **Известное:** смещение `IFlawResponseBias` читается через `FlawRegistry.Shared.GetResponseBiasMultipliers` (внутренний метод Api-фасада); публичный `SetResponseBias` уже был. Подключение самой `rival-personalities` — TSK-021 и её спека.

## 9. Кусок 5 (TSK-017..021, TSK-026): страница, пометка, подсказка, API, смещение ИИ, пресса

Правок ядра и чужих файлов не требуется, но есть одна просьба по сборке (п. 9.1) и два места, которые нужно проверить глазами в игре (п. 9.4).

**9.1. `press-reviews` нет в зависимостях сборки фичи (`mod/feature_deps.json`).** Корректировщик обзоров (TSK-026) регистрируется через `PressReviewsApi.RegisterAdjuster`, а `build_feature.py design-flaw-recalls` собирает ядро, эту фичу и `Api` только тех фич, что перечислены в `feature_deps.json` (для неё это `event-system`). Прямой `class FlawPressAdjuster : IReviewAdjuster` в такой сборке не компилируется (CS0246), а добавить `press-reviews` в список нельзя без цикла (`press-reviews` уже перечисляет `design-flaw-recalls`) и не мой файл. Обход, который собирается и так, и так:
- `Logic/AdjusterBridge.cs` создаёт объект, реализующий интерфейс по имени, через `System.Reflection.Emit` (публичный запечатанный тип `PTMod.Features.DesignFlawRecalls.FlawPressAdjusterProxy`; пространство имён решает, какой фиче засчитываются сбои, `ModFeatures.FeatureOfType`). `Press/FlawPressBridge.cs` находит `PressReviewsApi` и `IReviewAdjuster` в сборке мода по имени (в пофичевой сборке без прессы связь просто отсутствует, в полной работает) и вызывает `RegisterAdjuster`/`UnregisterAdjuster` через рефлексию. `Press/FlawPressAdjuster.cs` — сам корректировщик; черновик обзора читается и пишется через публичные поля `OverallAdjustment` и `Notes`.
- Проверено без игры: `AdjusterBridge` на интерфейсах той же формы (тесты) и на настоящем `IReviewAdjuster` из временной сборки фичи вместе с `PressReviews/Api` (в PowerShell: объект создаётся, `Id` = `design-flaw-recalls`, `Adjust` доходит до делегата).
- Когда интегратор добавит `press-reviews` в зависимости `design-flaw-recalls` (или фичи соберут одной сборкой — полная сборка так и делает), мост можно заменить обычным классом: `FlawPressAdjuster : IReviewAdjuster { Id => FlawPressRules.AdjusterId; Adjust(cpu, draft) => FlawPressRules.Apply(...) }`, регистрация — прямые `PressReviewsApi.RegisterAdjuster`/`UnregisterAdjuster`; `AdjusterBridge.cs` и его тесты удалить. Пока этого нет, ничего менять не нужно.

**9.2. Тестовое зеркало снимка.** `Logic/ApiViewMirror.cs` (только `PTMOD_TESTS`) повторяет `Api/Model/FlawRecordView.cs`, потому что чистая логика теперь строит снимки (`Logic/FlawViews.cs`, `FlawApiEngine`, `FlawPressRules`). Удалить вместе с `Logic/ApiModelMirror.cs`, когда `Api/Model` попадёт в тестовые сборки (п. 1 и п. 4). Тест `ApiViewMirror_IsACopyOfTheSnapshotOfTheApi` следит за совпадением.

**9.3. Что где лежит.**
- Чистая логика (в сборке тестов): `Logic/FlawApiEngine.cs` (`FlawApiEngine`, `IFlawApiHost`, `FlawCpuInfo`: `TryReveal`, `ForceFlaw`, списки, `FindByCpu`), `Logic/FlawViews.cs` (снимки; игровая половина `partial` — `FlawViews.cs`), `Logic/FlawJournalModel.cs` (строки страницы), `Logic/FlawBadgeModel.cs`, `Logic/FlawRiskHint.cs`, `Logic/FlawPressRules.cs`, `Logic/AdjusterBridge.cs`.
- Игровая сторона (только в DLL): `FlawApiBackend.cs` (хранилище за `FlawRegistry`; `AttachBackend` в `OnEnabled`, `DetachBackend` в `OnDisabled`), `UI/FlawJournalPage.cs` + `UI/FlawUi.cs`, `Patches/CpuBadgePatch.cs`, `Patches/DesignerRiskHint.cs`, `Press/FlawPressAdjuster.cs`, `Press/FlawPressBridge.cs`; подключение — `DesignFlawRecallsFeature.RegisterInterface`.
- Строки: в ru/en добавлены `dfr.journal.*`, `dfr.badge*`, `dfr.hint.*` (48 ключей на язык). Тест сверяет наборы ключей и плейсхолдеры в двух языках и что каждый литерал `"dfr.…"` в коде есть в таблице.

**9.4. Решения, которых нет в спеке дословно, и что проверить в игре (не запускалось).**
1. **Пометка на карточке не в имени CPU.** `CpuSalesUI.FixedUpdate` на каждом шаге сравнивает `cpuNameText.text` с `cpu.Name` и затирает отличающийся текст, поэтому пометка «Брак: …» дописывается к тексту популярности (`popularityText`), который пишет только `UpdateDailyTexts` (так же игра дописывает пропущенные продажи к тексту продаж). Проверить: пометка видна на карточке и не копится день за днём; при смене состояния брака обновляется сразу (`CpuBadgePatch.RefreshCards` вызывает `UpdateDailyTexts` у карточек на экране через `FindObjectsByType`). Если текст популярности не подходит по вёрстке, правка — смена поля в одной строке.
2. **Подсказка в конструкторе** — строка `ModUI.Info` («Риск скрытого брака» / «~N %»), вставленная сразу под строкой процента брака (`failureRateText`) по приёму `DesignerIntegration` из `cpu-architectures`; обновляется не чаще 10 раз в секунду. Проверить расположение (вёрстка конструктора неизвестна) и что она двигается со слайдером частоты и переключателем разгона.
3. **Смещение спешки (FR-14 п. 2).** `DesignerRiskHint.RushSource` (`Func<bool>`) должна выставить TSK-029 при создании переключателя «Ускоренный проект»; пока её нет, оценка считается для обычного проекта. Вероятность при спешке уже учитывается в `FlawRiskHint.Probability(..., rush)`.
4. **Вкладка «Соперники» показывает только публичное.** Внутренний брак ИИ, тихая ревизия и тихая заплатка не показываются, пока не просочились (FR-11 п. 5, «слухов нет»); отзыв показывается всегда (он делается открыто). По той же причине корректировщик обзоров не трогает обзор CPU соперника с внутренним браком (`FlawPressRules.Applies`: у соперника нужен `PublicSinceDay > 0`), а брак игрока считается «известным», как только он не `Hidden` (как в FR-12/FR-18).
5. **`ForceFlaw`**: возвращает `false`, не создавая запись, для неизвестного CPU, невыпущенного/снятого с рынка, владельца не в мире/банкрота, типа вне контента, лишних значений перечислений, ИИ при выключенном `aiFlaws`, брака, который уже известен или закрыт. Для скрытой записи только повышает тяжесть (тип остаётся). Вариант CPU обрабатывается через базовый; год выпуска для записи — середина `Cpu.Year` (игра хранит только год).
6. **Окно подтверждения поверх хаба.** Кнопка «Профинансировать ревизию» зовёт `ModUI.Confirm` со страницы хаба (модальное окно над модальным хабом, как в `first-year-onboarding`). Проверить, что подтверждение видно и после «Да» список перерисовывается.
7. **`FindObjectsByType`.** Игра собрана на Unity с `Object.FindObjectsByType` (старый `FindObjectsOfType` помечен устаревшим); `RefreshCards` использует новый.

## 10. Кусок 6 (TSK-027, TSK-028, TSK-029): поглощение CPU, модель спешки, переключатель

Правок ядра и чужих файлов не требуется. Все изменения в папках фичи; одна поправка в собственном тесте (п. 10.4).

**10.1. Что где лежит.**
- Чистая логика (в сборке тестов): `Logic/FlawTransferEngine.cs` (`FlawTransferEngine`, `IFlawTransferHost`, `FlawTransferOutcome`), `Logic/RushMath.cs`, `Logic/RushProjectBook.cs`; дополнения `FlawStateMachine.ReopenForNewOwner`, `FlawCycle.Requote` и `FlawCycle.CloseRecord`, `FlawDecisionEngine.Reoffer`.
- Игровая сторона (только в DLL): `FlawTransferHandler.cs` (подписка в `Register`: `ModEvents.CpuTransferred += Transfer.OnCpuTransferred`), `RushProjectOption.cs` (переключатель `ModUI.Toggle`), `Patches/RushProjectPatches.cs` (пять `Optional`-патчей), `FlawDecisionDispatcher.Reoffer`. Подсказка риска получает источник `DesignerRiskHint.RushSource = Rush.IsRushOn` в `RegisterInterface`.
- Строки `dfr.rush.toggle` и `dfr.rush.tooltip` в ru/en. Тесты: `FlawTransferTests` (41), `RushMathTests` (21), `RushProjectTests` (30).

**10.2. Решения, которых нет в спеке дословно.**
1. **Новые цены при переносе.** Запись, которая после переноса ждёт решения (публичный брак, прежде проигнорированный; брак, который не успел решить ИИ), получает котировку заново по книгам нового владельца (`FlawCycle.Requote`, как после утечки; `unitsAtDiscovery` не трогается): потолок отзыва зависит от выручки владельца за 12 месяцев, и игрок не должен платить по выручке прежней компании. Записи `Stepping` и `Mitigated` и ещё не обнаруженные котировку не меняют (D9).
2. **`InternalKnown` с игнором** (тихо проигнорированный брак) состояние сохраняет: утечка пойдёт к новому владельцу (FR-19 п. 2). Сбрасывается ответ только у публичного брака с игнором (п. 3). Публичный брак, который ещё ждал ответа, предлагается новому владельцу без сброса.
3. **Обратный порядок.** Прежний владелец уже банкрот (`from.IsBankrupt`) в момент переноса, а запись ещё не итоговая → запись закрывается (`FlawCycle.CloseRecord`, FR-4 п. 5) со строкой в лог. Если монитор успел закрыть записи компании по `OnCompanyWentBankrupt`, перенос находит итоговую запись, ничего не меняет и пишет строку. По контракту `rival-acquisitions` это и есть «аукцион за активы обанкротившейся компании»: CPU приходят без брака.
4. **Округление срока.** Половины дней округляются вверх (10 дней при 0,25 → 8); срок короче одного дня не трогается, результат не меньше одного дня.
5. **Пересчёт конструктора.** При переключении `CreatorCpu.UpdateUI` не вызывается: `Project()` и `UpdateInfoTexts` выполняются каждый кадр открытого конструктора, и значения меняются сами. Приватный `UpdateUI` нужен только для создания переключателя (postfix).
6. **`FlawRegistry.MoveOwner`** и **`FlawChannels.RemoveCompanyEntries`** были с кусков 2 и 3; чистый движок (в `Logic/`, без `FlawRegistry` с типом `Cpu`) вызывает их чистые половины `FlawStore.MoveOwner` и `FlawChannelBook.RemoveCompanyEntries`. Порядок важен: записи прежней компании снимаются по её ключу до смены ключа (тест).
7. **Проект без сохранённой метки.** Отметка живёт в `rushProjects` до завершения проекта; проект, чьё имя не пришло в течение 60 дней после плановой даты, отбрасывается, список не длиннее 64. Если фича выключена в момент завершения ускоренного проекта, патчи сняты и метка не ставится (CPU считается обычным).

**10.3. Что проверить в игре (не запускалось).**
1. Переключатель «Ускоренный проект» виден в панели конструктора перед кнопкой создания CPU, ничего не перекрывает и не ломает вёрстку; в журнале строка `designer: the switch 'Rush project' is in the CPU designer (before <имя>)`. Вёрстка панели неизвестна: если `createCpuButton` лежит в ряду, переключатель встаёт перед рядом; если родитель без layout-группы, он ставится на ряд выше кнопки по её якорям. Расположение правится в `RushProjectOption.PlaceBefore`.
2. Включённый переключатель: срок −25 %, стоимость +10 %, «в месяц» и дата окончания пересчитаны, строка «Риск скрытого брака» выросла на порядка 5 п. п.; после «Создать» переключатель выключен; в окне проектов срок короче.
3. По завершении проекта в логе `rush project '<имя>' finished: cpu <id> is marked as rushed`, а после выпуска `roll cpu=<id> p=…` включает `kRush`.
4. Сохранение и загрузка посреди ускоренного проекта не теряют метку (`rushProjects` в секции `design-flaw-recalls` файла `.ptmod.json`).
5. Поглощение соперника через `rival-acquisitions`: в логе строки `transfer: flaw #… follows cpu … from ai:… to player`; публичный брак с игнором открывает у игрока окно `dfr.decide.public*`; ревизия соперника продолжается и платится игроком.
6. Если одна из целей патчей не найдена (`CreatorCpu.Project`/`CreateCpu`/`UpdateUI`, `ProjectCompletedHandler.AddCpu`, поля `projectTime`, `projectCost`, `nameInput`, `createCpuButton`), переключателя нет, в журнале одно предупреждение `rush project: the switch is not offered: …`, остальная фича работает.

**10.4. Тест, который пришлось поправить в своей папке.** `FlawFoundationTests.FeatureFolder_HasNoPatchOfTheReleaseOfACpu` запрещал любой `ModPatch` с именем `AddCpu`. Теперь он запрещает такой патч только на типах, кроме `ProjectCompletedHandler`: очередь готовых проектов игрока (`ProjectCompletedHandler.AddCpu`) не зарезервирована ядром (резерв P20 — только `Company.AddCpu` и `AICompany.AddCpu`), а `cpu-architectures` патчит её же.

**10.5. Для самопроверки (TSK-023).** Часть «Ускоренный проект» объединяет патчи класса `RushProjectPatches` (цели выше). Условие её работы — `ModHooks.IsApplied(typeof(RushProjectPatches))` и поле `CreatorCpu.createCpuButton`; `RushProjectOption.Wanted()` уже проверяет оба и пишет одно предупреждение.

## 11. Кусок 7 (TSK-022, TSK-023, TSK-024): строки, самопроверка, тест баланса

Правок чужих файлов нет. Одна просьба к ядру (11.2); остальное лежит в папках фичи.

**11.1. Что где лежит.**
- Чистая логика (в сборке тестов): `Logic/FlawSelfCheckRules.cs` (`FlawSelfCheckRules`, `FlawSelfCheckInput`, `FlawSelfCheckReport`, `FlawCheckFinding`, `FlawCheckSeverity`, `FlawCapability`, `FlawPatchFact`, `FlawPatchTarget`, `FlawMemberSpec`, `FlawChannelFact`); в `Logic/FlawNewsPlanner.cs` добавлен `NotifyBodyKey` (второй ряд уведомления о незначительном браке соперника).
- Игровая сторона (только в DLL): `FlawSelfCheck.cs`. Его `Run` вызывает `DesignFlawRecallsFeature.OnEnabled` последней строкой (под `ModHooks.Guard`), `OnGameReady` — из `GameReady` (повторная проверка только если на старте определения событий ещё не были загружены). `FlawMonitor.OnMonth`/`OnDay` теперь сами под `ModHooks.Guard` (раньше полагались на охрану диспетчера тиков). `FlawNews` показывает уведомление `Minor` через `ModNotify.Show` (заголовок и вторая строка).
- Строки: ключ `dfr.news.rival.body` из таблицы design был пропущен, добавлен; `dfr.news.rival.title` приведён к тексту design («{0}: дефект в {1}»), остальное (тип, тяжесть, реакция) перенесено во вторую строку: «{0} признала дефект ({2}, {3}): {4}.» (в design стоит «и выбрала: {4}», но `{4}` у нас готовая фраза «отзывает проданные процессоры», с «и выбрала» она не склеивается).
- Тесты (`mod/tests/PTMod.Tests/Features/DesignFlawRecalls/`, см. п. 4): `FlawSelfCheckTests`, `FlawStringsTests`, `FlawBalanceSimulationTests` + генератор `SyntheticDesigns.cs`. Поправлены `FlawAiNewsTests` (уведомление в два ряда).

**11.2. Просьба к ядру: точка самопроверки для фичи.** `ModSelfCheck` не даёт фиче добавить свои находки (так же сделали `OnboardingSelfCheck`, `PlatformSelfCheck`, `PressSelfCheck`), поэтому фича сама пишет в `Player.log` под своим id: `self-check: OK` либо `self-check: N error(s), M warning(s)` и строка на находку (`self-check: [abilities] …`). Просьба: публичный `ModSelfCheck.RegisterFeatureCheck(string featureId, SelfCheckStage stage, Func<IEnumerable<SelfCheckItem>> check)`, чтобы находки фич попадали во вкладку «Диагностика» вместе с остальными. Готовая основа: `FlawSelfCheck.Last.Findings` (серьёзность, область, английское сообщение); перевод в `SelfCheckItem` — одна функция.

**11.3. Решения, которых нет в спеке дословно.**
1. **Шестая цель патча.** К пяти целям из design (`CpuSalesUI.UpdateDailyTexts`, `CreatorCpu.UpdateInfoTexts`, `CreatorCpu.Project`, `CreatorCpu.CreateCpu`, `ProjectCompletedHandler.AddCpu`) добавлена `CreatorCpu.UpdateUI`: на ней стоит postfix переключателя спешки. Тест сверяет таблицу самопроверки со всеми `[ModPatch]` фичи (и что каждый `Optional`), так что список не разойдётся с кодом.
2. **Что считается обязательным.** Фатальны (фича становится `Unavailable` через `ModFeatures.MarkUnavailable`, одна строка `Error` с причиной, игра идёт дальше): отсутствие любого из шести событий `dfr.decide.*` (пак не загружен или пуст — отдельная формулировка), любого из четырёх типов эффектов, канала `market.rating.cpu` (или без `PerCpu`) и `contract.signChance`. Отсутствие подписки на `CpuReleased`/`CpuTransferred` и необъявленное ядром событие выпуска (`ModEvents.IsReleaseTracked`) — `Error` в логе без `Unavailable`: в списке обязательных пунктов tasks.md подписок нет, а без выпуска фича просто не бросает, ничего не ломая. Пятый тип эффекта (`FlawNews`, строка журнала новостей соперников) — предупреждение.
3. **Одно предупреждение на возможность.** Если нет цели патча или поля, выключается только своя возможность (пометка на карточке, подсказка риска, переключатель спешки), в логе одна строка `[abilities] … is off: …` со всеми причинами. Сами возможности отключаются уже так: патчи `Optional`, подсказка и переключатель сами проверяют поля; самопроверка ничего не переключает, она сообщает.
4. **Когда проверяется.** В конце `OnEnabled` (определения событий `event-system` читает её `OnEnabled`, он идёт раньше по порядку зависимостей) и ещё раз на первом `GameReady`, если на старте определений не было. Повтор с тем же результатом пишет только строку Debug.
5. **Сведения берутся у ядра, не из копий:** патчи из `ModHooks.Report` (имя цели `Class.Method`, как его строит `ModHooks.NameOf`), события — `EventRegistry.All`, типы эффектов — `EventEffects.RegisteredTypes` (пустой список считается «не знаем», чтобы выключенный `event-system` не дал ложного «Unavailable»), каналы — `ModModifiers.IsDeclared`/`SpecOf(...).PerCpu`, префиксы — `ModIds.IsEventIdAllowed`/`IsL10nKeyAllowed`, подписки — чтение поля-события `ModEvents` по имени (как в `PlatformSelfCheck`; если ядро поменяет хранение события, проверка молча пропускается с пометкой в Debug).
6. **Выключение посреди партии (ручной сценарий 10)** проверен без игры на чистых движках (`ManualScenario10_…`): ревизия и заплатка идут, фича выключается, 10 дней нет ни списаний, ни вызовов `Remove`, при включении срок ревизии сдвигается на `today − lastActiveDay − 1`, срок заплатки и её запись в ядре не меняются, новых `Add` нет, ревизия оплачена ровно на `C_step`. В игре сценарий остаётся за владельцем.

**11.4. Баланс по симуляции (для владельца).** Популяция (допущения записаны в `SyntheticDesigns`): кампания 1980–2010, сложность 2, без спешки; FR растёт с 0,18 (1980) до 0,58 (2018) с разбросом ±0,08; 15 % разогнано на 5–40 %, 8 % перегрето до +15 %, 80 % на изученном техпроцессе. Расчёт по `flaws.json` из поставки:
- средняя p: 1980–2010 — **0,133** (коридор 0,08–0,15 выполнен с запасом, 20 разных сидов подряд); 1980–84 — 0,083; 1985–99 — 0,125; 2000–10 — 0,167; 2015–25 — **0,202**; вся игра 1971–2030 — 0,147;
- доля `Critical` среди брака: 0,139 (допустимо ≤ 0,20; по эпохам 0,125–0,152);
- время до обнаружения `Major` при 1 млн единиц: **11,7 мес.** (закон: h = 0,0817, с принудительным обнаружением на 36-м месяце); через настоящий месячный монитор получается то же (допуск 0,8).
- **Заметка:** в позднюю игру средняя p выходит из коридора 8–15 % (после 2015 около 20 %: множитель эпохи 1,1 и FR до 0,6–0,7). Если цель коридора относится ко всей игре, правится `risk.era` для 2000–2030 или `risk.kFailureRate` в `flaws.json`; тест при этом трогать не нужно, кроме допущений популяции.

**11.5. Что проверить в игре (не запускалось).**
1. В `Player.log` после старта: `[design-flaw-recalls] INFO self-check: OK`. Любая строка `WARN`/`ERROR` с `self-check:` называет область (`abilities`, `subscriptions`, `events`, `effects`, `channels`, `ids`, `content`).
2. К моменту `OnEnabled` определения событий `event-system` уже загружены (иначе будет одно предупреждение «definitions are not loaded yet» и повтор на первом `GameReady`).
3. Подписки читаются по полям-событиям `ModEvents.CpuReleased`/`CpuTransferred` (в Mono поле-событие названо так же); если проверка их не видит, в Debug строка `note: … cannot be looked into`.
4. Уведомление о незначительном браке соперника — две строки: «AMD: дефект в Nova 3» и «AMD признала дефект (тип, незначительный): …» (название компании подставляет игра).
