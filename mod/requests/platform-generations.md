# Запросы фичи platform-generations к итоговому интегратору

Кусок 1 (TSK-001…TSK-003): `python mod/build_feature.py platform-generations` собирается без ошибок и предупреждений, `python mod/build_feature.py platform-generations --tests` зелёный (121 тест фичи). Правок в ядре и в чужих файлах нет. Ниже — только то, что нужно поправить в документах (не в коде).

## 1. design.md: массив записей в `generations.json` называется `records`

`ModContent.LoadRecords` (и `ContentMerger.MergeRecords`) читают массив записей только из члена `records`: `{ "schema": 1, "records": [ ... ] }`. В `design.md` (Component 2: класс `GenerationsFile { schema; generations }`; раздел «Контент `generations.json`», пример JSON) массив назван `generations`. Файл `Content/platform-generations/generations.json` написан по правилу ядра (`records`), класса `GenerationsFile` в коде нет: запись читает `GenerationDef.FromJson`, проверяет `GenerationCatalog.Validate`.

Просьба: заменить `generations` на `records` в примере JSON и убрать `GenerationsFile` из Component 2 (или оставить пометку, что файл — это обычный файл записей ядра).

## 2. tasks.md: путь тестов

В Deliverables TSK-003 и далее тесты указаны как `mod/tests/PTMod.Tests/PlatformGenerations/...`. `build_feature.py` и `build.py` подхватывают тесты фичи только из `mod/tests/PTMod.Tests/Features/<Pascal>/`, поэтому тесты лежат в `mod/tests/PTMod.Tests/Features/PlatformGenerations/` (namespace `PTMod.Tests.Features.PlatformGenerations`). Просьба поправить пути в `tasks.md` и в design.md («Testing Strategy») при итоговой сверке.

## 3. Для следующих кусков и интегратора (не запрос, а контракт)

- Каталог поколений нельзя проверить до сцены игры: `DataFinder.FindPackage/FindMemory` ищут объекты сцены `level1`. Поэтому `PlatformGenerationsFeature.Catalog` пуст до `ModTicks.GameReady` (или `OnEnabled` в игровой сцене); признак — `CatalogReady`. Баланс (`Balance`) читается в `OnEnabled` и сцены не требует.
- `GenerationCatalog.StatusAt` принимает `IPlatformHistory` (`IsAnnounced`, `IsLaunched`), а не `PlatformTimeline`: `PlatformTimeline` (TSK-005) должен реализовать этот интерфейс (сигнатуры совпадают с design.md).
- Типы `GameDate`, `GenerationStatus`, `OemMode`, `SocketId` уже объявлены в `Logic/` — повторно объявлять их нельзя (CS0101).

## 4. Сборка фичи ломает тест `BuiltDll_EmbedsContentAndL10nResources`

Если выполнить `python mod/build_feature.py <slug>` и после этого `python mod/build_feature.py <slug> --tests`, архитектурный тест `BuiltDll_EmbedsContentAndL10nResources` падает: он находит свежий `mod/build/features/<slug>/PTMod.dll` (в `TestEnv.BuildDir`) и ждёт в нём ресурсы всех фич дерева, а фичевая DLL несёт только ядро и свою фичу. Обход на стороне фичи: после проверки сборки удаляется `mod/build/features/platform-generations/PTMod.dll` (и `.pdb`), тогда три теста `BuiltDll_*` пропускаются («PTMod.dll is not built»). Просьба: в `build_feature.py --tests` не подставлять фичевую папку как `PTMOD_BUILD_DIR` для тестов с DLL либо в `BuiltDll` пропускать тест для сборки, которая не полная (по имени папки `features/`).

---

## Кусок 2 (TSK-004…TSK-008): модели `Logic/`

`python mod/build_feature.py platform-generations` собирается без ошибок и предупреждений; `python mod/build_feature.py platform-generations --tests` — тесты фичи зелёные (146 новых тестов в 5 файлах). Правок в ядре и в чужих файлах нет. Один тест ядра сейчас падает не из-за этой фичи: `CreditMathTests.Sources_OnlyTheCreditClasses_ReadCentralBankDebtLimit` находит чтение `CentralBank.DebtLimit` в `Features/BankruptcyRecovery/Logic/RecoveryRules.cs` и `Features/RivalAcquisitions/Logic/ValuationMath.cs` (их владельцам нужно перейти на `ModCredit.ThresholdOf` / `PlayerDebtLimit`).

### 5. design.md: `Roll` — FNV-1a 64 с финализатором

F8 и Component 5 пишут `FNV1a64(key + "#" + G.id + "#" + WorldSeed) / 2^64`. Чистый FNV-1a почти не меняет старшие биты, когда различаются только последние символы строки (два мировых сида, отличающиеся последней цифрой, давали бы одинаковые броски с точностью 1e-5; проверено тестом). Поэтому `OemAdoptionModel.Roll` после FNV-1a применяет финализатор `fmix64` из MurmurHash3 и берёт старшие 53 бита (`/ 2^53`, результат строго в [0, 1)). Эталонные значения в тестах посчитаны независимой реализацией. Просьба: в design.md (F8, Component 5, D6) заменить «FNV1a64» на «FNV-1a 64 + fmix64».

### 6. Для `GameAdapter` (TSK-009): новое поле `CpuLedgerView.BasePackageName`

`PlatformTimeline` оценивает совместимость нового сокета с основным сокетом компании (`SocketIdentity.Compatible`), а основной сокет приходит из `InstalledBaseLedger` только ключом (`pkg:...`/`custom:N`), у `custom:` базовый корпус по ключу не восстановить. Поэтому в `CpuLedgerView` добавлено поле `BasePackageName` (= `SocketId.BasePackageName`, null, если базу не прочесть); адаптер обязан его заполнять. Остальные поля — как в design.md (`CpuId`, `CompanyKey`, `SocketKey`, `GenId`, `Sold`, `SoldByContracts`). `InstalledBaseLedger.SocketOf(key)` отдаёт `SocketId` по ключу.

### 7. Что добавлено к сигнатурам design.md (для следующих кусков)

- `InstalledBaseLedger`: `Clear()`, `OfCompany`, `SocketOf`, `CpuCount`, `HasState`, `LastMonth`, `IsConsistent(tol)` (инвариант «сумма по компаниям = сумма по CPU», для TSK-011). `ApplyMonth` при первом вызове, пропущенном месяце и откате времени сам делает полную пересборку; повтор того же месяца только обновляет владельцев (двойного затухания нет). `Rows()` возвращает `IReadOnlyList<IbRow>`, отсортированный по базе.
- `PlatformTimeline`: `Announce` возвращает `bool`; примитивы `SetLaunched`, `AddEarlyAdopter(maxSlots)`, `AddSocketIntro` (для `Reconstruction`, TSK-012), `AnnouncedOn`, `EarlyAdoptersOf`, `Clear`, `GenerationCount`, `IntroCount`, `WriteTo(PlatformGenerationsSave)`, `ReadFrom(save, today, problems)` (проверки дат/ключей/severity из «Validation Rules»). `ReleaseOutcome` дополнен `EarlySlot` (номер занятого слота, 1 = первопроходец). `SocketIntro` содержит `Exists`, `Date`, `MainSocketKey`, `MainAgeMonths`, `Severity`. Основной сокет без записи появления (сейв старше мода, CPU пришли с покупкой) считается достаточно старым: штрафа нет, возраст в записи = `minSocketLifeMonths`.
- `OemAdoptionModel`: `BuildInputs(catalog, history, ib, at, into)` собирает `GenAdoptionInput` «от новых к старым» на дату; `MarketDistribution` (веса `p_G`, `p_none`); `PlatformMultiplier` с дополнительным аргументом `contractType` (Calculator → 1.0 по `contractTypes`); константы причин `ReasonPrefers/ReasonLag/ReasonAdoption/ReasonLoyalty` (ключи `pg.reason.*`; тексты добавлены в `L10n/ru|en/platform-generations.json` вместе с `pg.reason.rating`).
- `OemPreferences`: `Assign` возвращает `bool` и оставляет первый ответ (предпочтение назначается один раз, D6); `Prune` возвращает число удалённых; `WriteTo`/`ReadFrom(save, problems)` для `contractPrefs`.
- Провайдеру (TSK-009) в `Roll`/`AssignPreference` нужно передавать `ModRandom.WorldSeed` (тип `uint`).

### 8. `ContractKeyBuilder`: схема экранирования

Формат `Company|Year-Month|Units|round(PaymentPerUnit)|Type` (месяц без нуля слева, оплата округляется от нуля). В имени клиента `\` заменяется на `\\`, `|` — на `\|`. Ключи хранятся только внутри секции этой фичи, поэтому точное совпадение с копией в `contract-transparency` не обязательно; если интегратор захочет единый ключ, достаточно привести обе копии к одной схеме экранирования.

### 9. tasks.md: пути тестов

Как в п. 2: фактический путь тестов куска 2 — `mod/tests/PTMod.Tests/Features/PlatformGenerations/{InstalledBaseLedgerTests,PlatformTimelineTests,ChurnDetectorTests,RatingEffectModelTests,OemAdoptionModelTests}.cs` (+ вспомогательный `LedgerTestData.cs`). Тест `OemAdoptionModelTests` покрывает также `OemPreferences` и `ContractKeyBuilder`.

---

## Кусок 3 (TSK-009…TSK-012): движок эффектов, провайдеры, обработчики событий, подписки, реконструкция

`python mod/build_feature.py platform-generations` собирается без ошибок и предупреждений, `python mod/build_feature.py platform-generations --tests` — зелёный (1451 тест, из них 70 новых в `PlatformEffectEngineTests`, `ReconstructionTests`, `PlatformIntegrationTests`). Правок в ядре и в чужих файлах нет, запросов к ядру и к другим фичам нет. Ниже — решения, которые нужно знать следующим кускам и интегратору, и расхождения с формулировками документов.

### 10. Что где лежит (для TSK-013…TSK-020)

- `Logic/PlatformEffectEngine.cs` (+ `EffectCpu`, `EffectContract`, `ChanceResult`): `RatingFor(company, socket, genId, ib, timeline, today, balance)` (F2–F6), `EffectiveMode`, `Chance` (F9a–F9c). `Logic/PlatformReasonArgs.cs`: заранее подготовленные массивы аргументов причин → `ProviderResult`. `Logic/Reconstruction.cs` (+ `ReconCpu`, `ReconstructionReport`).
- Корень фичи (игровая сторона): `GameAdapter` (единственное место, где читается игра: дата, CPU, сокеты, контракты; методы с `NoInlining` и try/catch), `PlatformModifierProvider` (кэш по CPU, оба провайдера), `ContractPreferences` (назначение/чистка предпочтений контрактов), `PlatformReleaseHandlers` (`CpuReleased`/`CpuTransferred`), `PlatformSubscriptions` (тики, `Resync`, контракты, сцена, настройки), `IPlatformNotifier` + `NullNotifier`.
- `PlatformGenerationsFeature` хранит состояние: `Catalog`, `Balance`, `InstalledBase`, `Timeline`, `Preferences`, `Provider`, `ContractPrefs`, `Notifier`. Для окна (TSK-015/016) и `PlatformInsights` (TSK-017): `Provider.Inputs` (принятие поколений на дату, от новых к старым), `Provider.CachedCount`, `Provider.IsReady`; запись кэша по CPU приватна — `PlatformInsights.RatingOf(cpu)` должен звать `PlatformEffectEngine.RatingFor` с теми же аргументами, что `PlatformModifierProvider.Fill`.

### 11. Шов для TSK-013 (`PlatformNotifier`)

Обработчики (`OnCpuReleased`, `OnYearPassed`) только сообщают, что событие случилось, через `internal IPlatformNotifier Notifier` фичи (`Announced`, `Launched(gen, byCompanyKey, player)`, `EarlyAdopter(gen, slot)`, `Churn(socket, penalty, months)`; `penalty` — доля рейтинга, 0.067 = 6.7 %). По умолчанию стоит `NullNotifier` (ничего не показывает). `PlatformNotifier` (TSK-013) реализует интерфейс, сам проверяет `ModTicks.IsFastForwarding`/`IsLoading` и переключатель `notifications` и присваивает `PlatformGenerationsFeature.Instance.Notifier = new PlatformNotifier()` в `Register`. Реконструкция (`Resync`) уведомителя не вызывает вообще — это проверяет тест `PlatformIntegrationTests`.

### 12. Расхождения и уточнения к design.md

- **Лояльность без предпочтения.** В design.md «контракт без предпочтения → 1.0» (тесты) и «затем F9c» (Component 5) читаются по-разному. Реализовано: F9a даёт 1.0, затем применяется F9c (лояльность к базе), поэтому контракт без предпочтения у компании с большой базой получает до ×1.10 с причиной `pg.reason.oemLoyalty`, а у компании без базы — ровно 1.0. Исключения (всегда 1.0 без причины): перемотка, CPU вне поколений, тип контракта вне `contractTypes` (AC-8.3), «рыночный» режим для поколения с `order < marketAverageMinGenOrder` (FR-8.5). Если нужно строго «без предпочтения = 1.0», достаточно одной строки в `PlatformEffectEngine.PerContract`.
- **Одна причина на ответ.** `ProviderResult` несёт одну причину: платформа клиента (`oemPrefers`/`oemLag`, режим «по контрактам»; `oemAdoption` — «рыночный»), а если платформа множитель не меняет — `oemLoyalty`. Множитель в `Explain` — итоговый (с лояльностью). Аргументы: `oemPrefers` {0} = название предпочитаемой платформы, `oemLag` {0} = число поколений отставания, `oemAdoption` {0} = название лидирующего (самого нового запущенного неустаревшего с принятием > 0) поколения, {1} = его принятие в процентах с одним знаком и точкой (`47.5`), `oemLoyalty` без аргументов.
- **«Рыночный» режим без кэша по CPU.** Вместо `Dictionary<Cpu, float>` из design.md считается на вызове по месячному списку `GenAdoptionInput` (10 поколений, ~0.1 мкс; тест-бенч `Chance_IsFast_ForTheProvider`): результат тот же, а кэш не расходится с поколениями между пересчётами. Кэш по CPU хранит только рейтинг (потребительский / «Промышленность»), поколение, признак полной платформы и `effShare`.
- **Сбой в провайдерах.** `ModHooks.Guard` создаёт делегат на вызов, а `Rating` не должен выделять память (NFR-1), поэтому провайдеры ловят исключение сами и зовут `ModHooks.ReportFault` (путь, который описан в документации `ModHooks.CanRun` для горячих мест); результат тот же: 1.0, запись в `ModLog`, счётчик фичи.
- **Запись кэша для нового CPU.** Для варианта (`isVariant`) обработчик выпуска выходит сразу (по Acceptance TSK-010), поэтому запись его кэша делается лениво при первом вопросе провайдера (однократная аллокация на CPU), а не ждёт месячного пересчёта. Ленивая запись — только когда кэш готов и каталог загружен.
- **Реконструкция** (`Reconstruction.Run`): варианты CPU (`Cpu.Base != null`) адаптер в список не включает (у них тот же сокет и поколение, что у базы); CPU с годом после текущего, без поколения, компании или сокета пропускаются (счётчик в отчёте). Если поколение запущено раньше `announceYear`, анонс ставится на 1 января года первого CPU (как при живом выпуске). Дата раннего последователя не раньше даты запуска (для живого запуска в середине года). Запись «первый CPU компании на сокете» создаётся для каждой пары, `severity = 0`, основной сокет пуст. Повторный запуск ничего не добавляет; три сценария (конец перемотки, старый сейв, повторное включение) дают одинаковую хронологию (тесты).
- **Когда делается реконструкция.** `FastForwardEnded` → `Resync(true)`; `GameReady(NewGame)` → `Resync(true)` (повтор, ничего не добавит); `GameReady(Loaded)` → `Resync(NeedsReconstruction)` (секции нет, либо в ней нет ни одного поколения); `OnEnabled` в готовой игре → `Resync(true)`; первый месячный тик, если каталог загрузился поздно или кэш не готов → `Resync`. Цепочка каждого `Resync`: реконструкция → `RebuildFull` → проверка инвариантов (`IsConsistent`) → `RebuildCache` → предпочтения контрактов без предпочтения → одна строка `ModLog.Info`.
- **Каталог поколений и сцена.** `DataFinder.FindPackage` ищет объекты сцены; `PTModHost.SceneReady` приходит на кадр позже `sceneLoaded`, а перемотка новой игры идёт в колбэке `SceneTransitionHandler.LoadScene(1, …)` из `GameManager.StartNewGame` (`Player.InitializeAsNewGame` → `DateController.SimulateUntilYear`) и может закончиться до `SceneReady`. Поэтому «объекты сцены есть» = `SceneReady` ИЛИ дата игры уже есть (`ModTicks.Today != default`). Загрузка, давшая 0 поколений, не запоминается и повторяется не больше 3 раз за сцену (счётчик сбрасывается на `SceneReady`); до успеха эффекты выключены, реконструкция откладывается на следующий случай (`GameReady` / первый месячный тик). Если интегратор знает точный момент, когда объекты сцены доступны, условие в `PlatformGenerationsFeature.SceneObjectsExist` можно упростить.
- **Контракты.** Подписка на `ContractManager.OnNewContracts` — прямая (ядро не оборачивает это событие), поэтому обработчик сам проверяет `ModFeatures.IsActive` и стоит под `ModHooks.Guard`; переподписка на `PTModHost.SceneReady` и на месячном тике (менеджера могло не быть). `t_создания = AcceptanceDate − 12 мес.` (`GameAdapter.CreatedOn`). Предпочтения чистятся на месячном тике по ключам доступных и активных контрактов; если менеджер недоступен, не чистится ничего.
- **Что не сделано в этом куске.** Самопроверка `ModHooks.IsReserved(Company.AddCpu / AICompany.AddCpu) == true` и замеры — TSK-020; проверка «загрузка сейва событий не вызывает» — TSK-024 (владелец). Тест `PlatformIntegrationTests` проверяет по исходникам, что у фичи нет `[ModPatch]` на `AddCpu`.

---

## Кусок 4 (TSK-013…TSK-018): уведомления, предупреждение выпуска, окно «Платформы», причины, предикаты и события

`python mod/build_feature.py platform-generations` собирается без ошибок и предупреждений (201 файл), `python mod/build_feature.py platform-generations --tests` — зелёный (1547 тестов, из них около 100 новых в `PlatformNumbersTests`, `PlatformNoticesTests`, `GenerationsViewTests`, `InstalledBaseViewTests`, `MyCpusViewTests`, `PlatformPredicatesTests`, `PlatformPresentationTests` и `BestSocketShareOf` в `InstalledBaseLedgerTests`). Правок в ядре и в чужих файлах нет. Игра не запускалась: код окна, патч и уведомления проверены компиляцией против `Assembly-CSharp.dll`, чтением кода ядра и тестами по исходникам; ручная проверка — TSK-024 (M1…M14).

### 13. Что где лежит (для TSK-019/020/023/024)

- Чистая логика (`Logic/`, всё под `[Covers]`): `PlatformNumbers` (запись чисел: точка, пробел между тысячами, без хвостовых нулей), `ViewText` (таблицы строк, имена поколений/компаний/сокетов — подставляет игровая сторона), `PlatformNotices` (+ `Notice`, `MailPlan`: уведомления, письма, предупреждения окна выпуска), `GenerationsView` / `InstalledBaseView` / `MyCpusView` (модели вкладок окна; `OemView`, `OemViewBuilder`, `SocketRiskView`, `PlatformSnapshot`), `PlatformPredicates` (+ `IPlatformFacts`, `PlatformPredicateNames`). `InstalledBaseLedger.BestSocketShareOf(company)` — для `pg.installedBaseShare`.
- Игровая сторона (корень фичи): `PlatformNotifier` (реализует шов `IPlatformNotifier`, стоит по умолчанию в `PlatformGenerationsFeature.Notifier`), `Patches/ReleaseWarning.cs` (единственный патч), `PlatformInsights` (`RatingOf`, `OemOf`, `GenerationOf`), `PlatformConditions` (+ `PlatformFacts`), `PlatformEvents`, `PlatformViewText`, `UI/{PlatformBrowserWindow,GenerationsTab,InstalledBaseTab,MyCpusTab,PlatformUi,PlatformWindowData}.cs`. В `GameAdapter` добавлены `CompanyNameOf`, `SocketNameOf` (имя своего сокета через `CustomHardwareHelper.GetCustomHardware(id).Name`), `CollectPlayerCpus`, `CollectOemContracts` (только доступные контракты), `IsVariant`.
- `PlatformEvents` и `PlatformInsights` лежат в корне фичи (как в Deliverables), а не в `Api\`. Фич-зависимостей от `platform-generations` в `feature_deps.json` сейчас нет; если `press-reviews` или `scenarios-achievements` захотят вызывать их напрямую, интегратор переносит оба файла в `Features\PlatformGenerations\Api\` (сигнатуры публичные и зависят только от публичных типов `Logic/` и `ProcessorTycoon.Hardware.Cpu`).

### 14. Расхождения с design.md и решения

- **Аргументы письма — один общий список для заголовка и тела.** `ModMailMessage.Args` один на оба текста, поэтому в письмах `{0}` — название платформы, остальное сдвинуто на единицу: анонс `{0}` имя, `{1}` сокеты, `{2}` память, `{3}` число мест, `{4}` окно в месяцах, `{5}` бонус %, `{6}` срок бонуса, `{7}` год устаревания; ранний последователь `{0}` имя, `{1}` бонус %, `{2}` срок, `{3}` место, `{4}` число мест. В design.md (пример `pg.mail.announce.body`) тело нумеруется с `{0}` = сокеты — при общем списке так не получается.
- **Текст анонса без склонения «компании».** Вместо «Первые {2} компании…» — «Мест для ранних последователей: {3}. Выпустите CPU на этой платформе в течение {4} мес. после запуска, и вы получите бонус +{5} % …» (нет согласования числа и существительного). Срок устаревания (FR-2.2) — отдельный ключ тела `pg.mail.announce.bodyObsolete` для поколений с `obsoleteYear > 0` (предложение либо есть, либо нет).
- **Письма раннего последователя:** `pg.mail.early.title`, `pg.mail.early.body` и `pg.mail.early.bodyPioneer` (для места 1) — в design.md есть только уведомление `pg.notify.early`. Добавлены также `pg.notify.announced` («Анонс платформы: {0}»), `pg.warn.early` («Ранний переход на {0}: +{1} % на {2} мес.», FR-7.2) и все строки окна (`pg.tab.*`, `pg.gen.*`, `pg.ib.*`, `pg.cpu.*`, `pg.risk.*`, `pg.window.*`, `pg.hub.open*`).
- **Запуск игроком.** `Launched` показывает уведомление «{компания} первой выпустила CPU на платформе …» для любой компании, в том числе для игрока (название — игровое имя его компании); сразу за ним идут уведомление и письмо раннего последователя (место 1). Если владельцу это покажется лишним, достаточно строки `if (player) return;` в `PlatformNotifier.Launched`.
- **Шов `IPlatformNotifier.Churn`** получил два аргумента — ключ основного сокета и его возраст (`Churn(socket, penalty, months, mainSocketKey, mainAgeMonths)`): уведомление FR-6.5 / FR-7.1 называет основной сокет и «поддерживается N мес. из 36». Интерфейс внутренний, снаружи его никто не вызывает.
- **События `PlatformEvents`** поднимаются для любой компании (не только для игрока): `EarlyAdopterGranted` и `SocketChurnApplied` нужны сценариям и про ИИ, а `SocketChurnApplied` пишется ещё и в `ModLog` уровня Info (M12). Из реконструкции и `Resync` события не поднимаются (проверка в тестах). `Quiet()` дублирует защиту: `IsFastForwarding || IsLoading`.
- **Предикаты.** `pg.installedBaseShare.atLeast` — доля 0…1 (как `share` у `marketShareAtLeast`), а не процент; «лучший сокет компании» = сокет с наибольшей базой компании, доля — от всей базы рынка. Параметр `company`: `player` / `ai:<id>` / `anyAI`; без него — субъект условия (игрок).
- **`PlatformInsights.OemOf`** возвращает `OemView` (тип в `Logic/MyCpusView.cs`: режим, число подходящих контрактов `Considered`, `Lagging`, средний множитель по отстающим `AverageLagging` и рыночный `Market` с лояльностью); считает тем же `PlatformEffectEngine.Chance`, что и провайдер. Переключатели «эффекты на рейтинг / на контракты» ответы `PlatformInsights` не меняют; окно при выключенном рейтинге показывает итоги ×1.00 (M10), компоненты `B_ib`, `B_early`, `P_churn` при этом остаются видны.
- **Окно открывается кнопкой на странице фичи в хабе** («Моды» → «Платформы» → «Настройки» → «Открыть окно «Платформы»»), как окно GPU: хаб — модальное окно и держит время, поэтому перед открытием окна хаб закрывается. Окно строится заново при каждом открытии, смене вкладки, смене фильтра и смене языка, живого обновления нет.
- **Полоса принятия OEM** — две картинки Unity UI (серая дорожка и зелёная заливка), а не текст из блочных символов, как в design.md: этих символов может не быть в шрифте игры (в логе были бы предупреждения TMP). Рядом остаётся процент.
- **Предупреждение выпуска** (`ReleaseWarning`) пропускает вариант CPU (`Cpu.Base != null`), CPU вне поколений и выключенный переключатель «Уведомления и письма» (его подсказка уже называет предупреждения); состояние не пишется (`Timeline.Preview`). Параметр постфикса назван `cpu`, как у `ProjectReleaseWindow.Open(Cpu cpu)` в декомпиляте; `__instance` не берётся.
- **Минус в строках:** `pg.notify.churn` по design.md с «−» (U+2212, как в других таблицах мода), `pg.risk.penalty` с ASCII «-».

### 15. Что стоит знать ядру (не блокирует)

- `HubWidgets.Row` и `SetWidth` в ядре внутренние («не для фич»), публичной горизонтальной строки в `ModUI` нет; фича собирает свой `HorizontalLayoutGroup` (`UI/PlatformUi.cs`) и откладывает действия на кадр через `PTModHost.Run`. Если несколько фич делают окна с таблицами, имеет смысл вынести `ModUI.Row`, `ModUI.Defer` и ячейку фиксированной ширины в публичный API.
- `ModUI.Text` создаёт текст с `raycastTarget = false`: чтобы повесить `ModUI.Tooltip` на текст, фича включает `raycastTarget` вручную (в описании `Tooltip` это сказано). Удобнее был бы необязательный аргумент `tooltip` у `ModUI.Text`.
- `ModUI.TooltipText` — вложенный тип `ModUI` (в CORE_API.md он показан как `struct TooltipText` без внешнего типа).

### 16. Не сделано в этом куске (по плану)

- Описание фичи для игроков и раздел `README` (TSK-023), самопроверка `ModSelfCheck` для `ProjectReleaseWindow.Open`, `CustomHardwareHelper.GetCustomHardware`, `ICompany.Name` и таймеры (TSK-020), офлайн-симуляция (TSK-022), ручная проверка в игре (TSK-024).

---

## Кусок 5 (TSK-019…TSK-023): строки, самопроверка и замеры, сейвы, баланс, описание для игроков

`python mod/build_feature.py platform-generations` собирается без ошибок и предупреждений (205 файлов), `python mod/build_feature.py platform-generations --tests` — 1649 прошло / 0 упало (из них около 100 новых: `PlatformStringsTests`, `PlatformPerfTests`, `PlatformSelfCheckTests`, `SaveModelTests`, `BalanceSimulationTests`, `PlatformGuideTests`). После проверки сборки удалены `mod/build/features/platform-generations/PTMod.dll` и `.pdb` (см. п. 4). Правок в ядре и в чужих файлах нет, игра не запускалась.

### 17. Просьбы к ядру

- **Слот самопроверки для членов фичи.** У `ModSelfCheck` таблица членов закрытая (`Members`, `ExpectedTargets`), фича не может добавить в неё свои строки: TSK-020 просит «регистрацию целей и членов в `PlatformGenerationsFeature.cs`». Поэтому фича проверяет себя сама (`PlatformSelfCheck`, один раз в первом `OnEnabled`, после применения патчей; результат в `Player.log` и строкой на странице фичи в хабе). Просьба: публичный `ModSelfCheck.Require(featureId, MemberRow...)` или `FeatureContext.RequireMember(...)`, чтобы строки попадали во вкладку «Диагностика»; таблица `PlatformSelfCheckTable.Members` (36 строк, `Logic/PlatformSelfCheckBook.cs`) тогда переезжает как есть.
- **Подписки на события ядра нельзя проверить снаружи.** Самопроверка читает приватное поле-обёртку события `ModEvents.CpuReleased` / `CpuTransferred` рефлексией и ищет в списке вызова обработчик `PlatformReleaseHandlers`. Если ядро заменит событие аксессорами, проверка запишет в журнал отладки «cannot be looked into» и не будет считать это проблемой. Публичный `ModEvents.SubscriberCount(...)` убрал бы рефлексию.

### 18. Что где лежит (для интегратора и TSK-024)

- **Раздел README.** `mod/README.md` в дереве нет и это не файл фичи, поэтому раздел «Поколения платформ» лежит в `mod/src/PTMod/Features/PlatformGenerations/README.section.md`. Просьба перенести его в `mod/README.md` (или в нужное место корневого `README.md`, раздел «Моды») как есть. Раздел не пишется руками: его создаёт `PlatformGuide` из строк `pg.hub.guide.*` с числами `balance.json` по умолчанию и из `generations.json`, а тест `PlatformGuideTests.TheReadmeSection_IsTheGuideOfTheDefaultBalance_WithTheTableOfTheGenerations` требует, чтобы файл совпадал с этим текстом. После правки баланса, строк или поколений тест упадёт; новый текст получается так: `PTMOD_README_OUT=<файл> mod/build/features/platform-generations/PTMod.Tests.exe --filter TheReadmeSection_IsTheGuide` (переменная окружения `PTMOD_MOD_DIR` — папка `mod`), затем файл копируется на место `README.section.md`.
- **Числа в игре.** Страница фичи в хабе строит то же описание из действующего баланса (`Balance`, `Catalog`), поэтому текст в игре всегда говорит о числах, которые действуют, даже если игрок переопределил `balance.json`.
- **Штуки.** `StringFormatter.FloatToIntString` (NFR-6) вызывается из `PlatformViewText` через `ViewText.CountText`; чистая логика (`InstalledBaseView`) сама игру не вызывает и без `CountText` пишет число через `PlatformNumbers.Count` (пробел между тысячами, одинаково на любой машине). Проценты, месяцы и множители пишет `PlatformNumbers` (точка, одного десятичного знака, без хвостовых нулей).
- **`python install.py --check` целиком не запускался**: ему нужен модуль `pefile`, а скачивать запрещено. Часть, которая касается мода (`ptru.mods.check_mod_sources(mods.mod_sources_dir('.'))`: JSON, id, ключи ru и en, префиксы), запущена напрямую и дала 0 ошибок и 0 предупреждений (42 файла контента, 32 таблицы строк). Остальное (файлы перевода) фиче не принадлежит.

### 19. Замеры производительности (NFR-1)

- `Logic/PlatformPerf.cs` хранит число запусков, сумму, среднее, последнее и наибольшее время `ApplyMonth`, `RebuildFull`, `Reconstruction.Run`, `RebuildCache` и сравнивает каждый запуск с бюджетом из дизайна (на 1000 CPU; для большего рынка бюджет растёт пропорционально). Общие бюджеты дизайна разделены так: месяц 5 мс = `ApplyMonth` 3 + `RebuildCache` 2; реконструкция с полной пересборкой 100 мс = по 50 мс каждой. Первое превышение за сессию даёт одну строку `ModLog.Warn`, остальные времена идут в `ModLog.Debug` только при `ModSettings.VerboseLog`.
- Счётчики вызовов провайдеров (`Rating`, `Chance`) — простые приращения на горячем пути (аллокаций нет, тест `TheRatingProvider_HasNoAllocationAndNoWalkOnItsHotPath` зелёный); день закрывает тик `DayPassed` (два сложения), в строку месячного прохода (`VerboseLog`) попадают среднее и пик вызовов за день.
- Настоящие операции на 1000 CPU × 720 месяцев (`PlatformPerfTests`): `RebuildFull`, месячный шаг и 1000 множителей рейтинга укладываются в бюджеты с запасом (лучший из пяти запусков).

### 20. Баланс по результатам симуляции (для владельца, TSK-024)

Значения по умолчанию из `balance.json`, 14 синтетических компаний, 1971–2030 (отчёт печатает `BalanceSimulationTests.Report_PrintsMinMaxAndAverage_ForTheOwner`):

- рейтинг, потребительские сегменты: 0.944 … 1.159 (среднее 1.023), «Промышленность»: 0.972 … 1.080;
- шанс подписания контракта: 0.502 … 1.375 (среднее 1.049), «рыночный» режим 0.508 … 1.292;
- закрытый сокет с 40 % базы получает ровно +10 % (30 % базы — +7.5 %, выше 40 % потолок +10 %);
- ИИ, меняющий сокет раз в 5 лет (со снятием старых CPU с продажи), штрафов не получает; ИИ, меняющий сокет каждый год, получил 4 штрафа, худший 6.7 %, рейтинг не ниже 0.944;
- сумма с множителями других фич на `market.rating.cpu` (настоящий `ChannelRegistry`, диапазон [0.5; 2.0]) держится в пределах, а кламп вклад фичи не повышает;
- замечание к балансу: штраф за смену сокета бывает редко. Основной сокет — тот, у которого самая большая база (а база затухает 60 месяцев), поэтому компания, которая бросает сокет, но ещё сидит на старой базе, штрафа не получает; штраф платит в основном молодая компания, которая быстро меняет сокет. Если владельцу нужно, чтобы штраф чувствовали чаще, смотреть на `churnMinBaseShare` (2 %) и `minSocketLifeMonths` (36).
- симуляция разомкнутая: продажи не зависят от множителей, поэтому она показывает, что дают формулы, а не как поведёт себя игрок.

### 21. Расхождения с формулировками задач

- **Пути тестов и фикстур**: `mod/tests/PTMod.Tests/Features/PlatformGenerations/{SaveModelTests,BalanceSimulationTests}.cs` и `.../Fixtures/*.ptmod.json` (как в п. 2; `build_feature.py` берёт тесты фичи только оттуда).
- **TSK-019**: ключи лежат не в `L10n/ru.json` и `L10n/en.json`, а в `L10n/ru|en/platform-generations.json` (так требует ядро для фич).
- **TSK-020**: «таймеры `ModLog`» сделаны через `PlatformPerf` и `ReportPerf`; сам `ModLog.Debug` отбрасывается ядром без `VerboseLog`, строки собираются только если он включён.
- **Подмешанные множители (TSK-022)** идут через настоящий `ChannelRegistry` и поле `BalanceMin/BalanceMax` канала `market.rating.cpu`, а не через самодельную имитацию клампа.

## Закрыто после QA (2026-10-11)

- Дневной кеш `contract.signChance`: `ContractID` — индекс шаблона десятилетия и не уникален, поэтому множитель OEM-предпочтения одного контракта мог достаться другому контракту того же шаблона. Канал объявлен в `core.json` с `"cacheable": false` (каждый запрос доходит до провайдеров), design.md ядра обновлён, регрессионный тест `LazyBindingTests.SignChance_TwoContractsOfOneTemplate_AreNotMixedUpByTheCache`.
