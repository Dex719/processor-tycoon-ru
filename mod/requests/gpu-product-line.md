# Запросы gpu-product-line

Кусок 1 (TSK-001…TSK-005). Блокирующих запросов нет: фича собирается (`python mod/build_feature.py gpu-product-line`) и её тесты зелёные
(`python mod/build_feature.py gpu-product-line --tests`, 117 тестов фичи в составе 1231).

## 1. Необязательные файлы контента: нужен способ не создавать пустые заглушки (необязательно, для интегратора)

- `ModContent.LoadRecords` считает отсутствие встроенного файла ошибкой (`ReadRecordsArray` пишет «файл не найден», `ModLog.Error` на каждом старте).
  Для файлов, которых этап ещё не наполнил, фича кладёт пустые `{ "schema": 1, "records": [] }`: `Content/gpu-product-line/consoles.json` (этап 3; `eras.json` и
  `vendors.json` наполнил кусок 7). Обход работает, лишнего в логе нет.
- Если в ядре появится признак «файл необязателен» (например, `ModContent.Exists(featureId, file)` или параметр `optional` у `LoadRecords`), заглушки можно
  удалить. Пока ничего менять не нужно; куски этапов 2 и 3 просто заполнят эти три файла.

## 2. Для кусков, которые будут писать дальше (справка, правок ядра не требует)

- Чистая логика лежит в `Features/GpuProductLine/Logic/**` (так её собирают тесты): DTO контента и сейва, `GpuContentDb`, `GpuState`, `GpuSectionParticipant`
  (`ISideFileParticipant`). Игровая сторона (`GpuFeature`, `GpuSaveParticipant : GpuSectionParticipant, IModSaveParticipant`, `GpuModContentSource`, `GpuWindow`)
  лежит рядом, вне `Logic`. Пути в `tasks.md` (`Content/`, `State/`) для чистых типов заменены на `Logic/Content/`, `Logic/State/`.
- Тесты фичи читают контент и фикстуры с диска через `TestEnv` (`GpuFiles.Shipped()`, `GpuFiles.Minimal()`, `TextContentSource` в
  `tests/PTMod.Tests/Features/GpuProductLine/GpuTestSupport.cs`): новые тесты на реальном контенте могут пользоваться ими же.

## 3. Кусок 2 (TSK-006…TSK-010): математика. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит, тесты `--tests` зелёные (1361, из них 130 новых: `GpuDesignMathTests` 23,
`PortEquivalenceTests` 21, `GpuResearchMathTests` 12, `GpuMarketMathTests` 49, `GpuProductionMathTests` 25).

- Файлы лежат в `Features/GpuProductLine/Logic/Math/` (а не в `Math/` из задач): `GpuDesignTypes.cs`, `GpuDesignMath.cs`, `GpuResearchMath.cs`, `GpuMarketTypes.cs`,
  `GpuMarketMath.cs`, `GpuProductionMath.cs`. Все в пространстве имён `PTMod.Features.GpuProductLine` (подпространства `Math` нет: оно закрыло бы `System.Math`).
- Отличия от сигнатур design.md, о которых должны знать TSK-011/013/015/016/017/018/019:
  - `GpuDesignContext` получил поля `Year` (год для цены памяти, F12) и `MaxDieMm2` (наибольший кристалл года, `GpuContentDb.MaxDie`; 0 = без ограничения);
    `GpuStats` получил `DpwFull`/`DpwYield`. `IGpuGame.Transistors` кладётся в `GpuDesignContext.Transistors`; 0 = взять плотность узла × площадь. Множитель контекста 0, отрицательный
    или не число читается как 1 (нулевой контекст пригоден к работе). `NodeSpec` объявлен в `GpuDesignTypes.cs`, `IGpuGame` (TSK-011) его использует.
  - `RealUnitCostPort` принимает последним необязательным параметром `waferMaterial` (по умолчанию 2000 = `ProductionMath.WaferMaterialCost(1.0)` без шока).
  - `GpuProductionMath.Produce`: параметр `waferMaterial` — цена материала ОДНОЙ пластины (значение `ProductionMath.WaferMaterialCost(1.0f)`, 2000 без шока); в design он назван
    `waferMaterial50`. `dieMaterial` и `perGoodExtras` вызывающий уже умножает на канал `production.unitCost` (`GpuDesignMath.PerGoodExtras` делает это для второго).
    `DailyOutput` получил поле `Passed` (кристаллы, вышедшие годными с пластины, до отбраковки); `Good` — карты в запас с учётом `production.output`.
  - `GpuProductionMath.AvailableLines` в режиме «CPU в приоритете» берёт меньшее из «вместимость − клиенты − `allocated`» и «вместимость − клиенты − линии CPU» (не меньше 0).
    Добавлены `ProduceOutsourced` (F18, fabless), `Royalty`, `LiquidationValue` (F31).
  - `GpuMarketMath` вместо несуществующего `GpuPopularityDto` берёт `GpuBalanceDto.PopularityBlock`. Параметры-массивы принимают `IList<float>`/`IList<int>` (массив подходит).
    `SellDay` сам считает сумму производительности по продаваемым карточкам (`Selling`), обнуляет дневные счётчики и ставит `Remaining = DailyBuyers`; деньги не двигает
    (`MarketItem.IncomeToday` платит сервис). Фон опознаётся по `OwnerKey == MarketItem.BackgroundOwner`: его популярность не меняется, но продажи входят в знаменатель долей.
    Добавлены `DailyBuyers`, `DecadeTransitionPort`, `NewProductPopularity`, `IsOld`, `EraIndex`, `VramRequirement` (лог-линейная), `RequiredFeatures`, `SegmentRuntime.FromDto`.
  - `GpuResearchMath`: добавлены `AheadFactor`, `ResearchSpeedPort`, `DaysLeft`.
- Расхождения самого design с игрой (решено в пользу игры, тесты это фиксируют):
  - F21: в игре прирост при пропущенных продажах умножается на целочисленное `sold / (sold − missed)` (`Market.UpdatePopularities`); порт делает так же.
  - AC-3.3: 800 мм² на пластине 100 мм по формуле игры дают 2 кристалла, а не 0. Правило блокировки — FR-3 п. 5 (`DiesPerWafer(die, wafer, 1.0) < 1`, ключ
    `gpu.design.block.die`); на пластине 100 мм оно срабатывает примерно с 1070 мм². Сценарий AC-3.3 в вкладке конструктора (TSK-024) надо проверять с таким кристаллом.
  - F2: число блоков считается с запасом в одну миллионную: `5,25e7 × 600` в `float` даёт 31 499 999 232, а калибровочный пример ждёт 21 000, не 20 999.
- Новые ключи строк (ru/en в `gpu-product-line.json`): `gpu.design.block.parts|numbers|node|dieSize|maxDie|die|clock|bus|vram` — причины, по которым `GpuDesignMath.Evaluate` ставит `Valid = false`.

## 4. Кусок 3 (TSK-011…TSK-016): мосты к игре и сервисы. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 957 КБ), тесты `--tests` зелёные (1443, из них 82 новых:
`GpuDivisionTests` 10, `GpuResearchTests` 15, `GpuProjectsTests` 12, `GpuProductionTests` 18, `GpuBridgeTests` 9, `GpuSelfCheckTests` 9, `GpuServicesSourceTests` 9).

- Где что лежит (отличия от путей задач):
  - Сервисы — в `Features/GpuProductLine/Logic/Services/` (`GpuDivision`, `GpuResearch`, `GpuDesigner`, `GpuProjects`, `GpuProduction`, `GpuLedgerBook`), а не в `Services/`:
    так их собирают тесты и проверяют на фейках.
  - `IGpuGame` (и `IGpuChannels`, `IGpuFactory`, перечисления `GpuExpense`, `GpuRevenue`, `GpuModifierKind`) — в `Logic/Game/IGpuGame.cs`. Интерфейс говорит ключами
    `ModCompanies` (`player`, `ai:<UniqueID>`), а не `ICompany`: тип игры в чистую логику не попадает. `KeyOf(ICompany)` и `Resolve(key)` есть у игровой реализации
    `Bridges/GpuGameAdapter`. Каналы читаются через `IGpuGame.Channels` (`UnitCost`, `Output`, `VramCost`, `Rating`, `PopularityGain`, `Demand(segmentId)`), а не через
    `Channel(string, ICompany)`: имена каналов в `Logic` не пишутся, их берёт из `ModChannels` игровой `GpuModifierBridge`.
  - `GpuCapability` перенесён в `Logic/Game/` (его использует чистая книга самопроверки `GpuSelfCheckBook`). Игровая самопроверка — `GpuSelfCheck.cs` в корне фичи.
  - Чистая половина моста каналов — `Logic/Game/GpuChannelReader.cs` (тот же вопрос `{ ключ компании, без CPU и сегмента }`, ответ не число → 1), таблица синергии —
    `Logic/Game/GpuSynergyTable.cs`.
- Решения, о которых должны знать TSK-017…TSK-026, TSK-040:
  - Обработчики фаз называются не `OnPreContractSellTick`, `OnDayPassed`, `OnMonthPassed` (эти имена событий игры запрещены тестом `GpuFeatureSourceTests`): у `GpuFeature` —
    `ProductionTick`, `ResearchTick`, `ProjectsTick`, `MonthTick`; у сервисов — `GpuProduction.ProduceDay`, `GpuResearch.DailyTick`, `GpuProjects.DailyTick`.
  - `DateController` в коде фичи не упоминается (тот же тест): дата — `ModTicks.Today`, номер месяца бухгалтерии — `GpuLedgerBook.MonthIndex(date)` = `год × 12 + месяц − 1`
    (вместо `MonthsSinceStart`).
  - `FactoryAccess` пишет `Factory.totalAllocatedLines` / `totalDemandedLines` через `System.Reflection.FieldInfo`, а не `AccessTools.FieldRefAccess`: слово `HarmonyLib`
    в коде фичи запрещено тем же тестом. Вызов раз в день, накладные расходы ничтожны.
  - Самопроверка считает патчи из объявленных: до TSK-021 (GP1) и TSK-041 (GP2) строка в `Player.log` — `self-check OK (patches 0/0, fields 6/6, calls 12/12, ports 3/3)`,
    возможности `ProductionUi` и `SharedLearning` при этом выключены без предупреждения. Когда оба патча объявлены через `ctx.Patch`, строка станет `patches 2/2`.
    Цели патчей сверяются по имени из `ModHooks.Report`: `ProductionWindow.UpdateAvailableProductionLines` и `Company.TotalLinesAllocated` (`GpuSelfCheck.ProductionUiTarget`,
    `SharedLearningTarget`).
  - `GpuDesigner.Evaluate(input)` — общая оценка дизайна для вкладки «Конструктор» (TSK-024) и проекта: два прохода (второй ставит `CpuMath.RealUnitCost` с отказами дизайна),
    fabless — выход годных `Lerp(min, max, fablessYieldProgress)` и наценка 2.0. Скидка совместной разработки (F27) пока 1.0 — её подключает TSK-042 в `GpuDesigner.ContextFor`.
  - Снимок дизайна (`GpuDesigner.Snapshot`) хранит «сырые» `dieMaterial` (= `arch.dieMaterialCost`) и `perGoodExtras` (память года, плата, охлаждение без каналов); производство
    каждый день пересчитывает их с каналами и годом (F12), а без записей контента — снимок × `production.unitCost`.
  - `GpuProduction` держит в памяти (не в сейве): выданные/запрошенные линии дня (`IsShort(productId, out granted, out requested)` — предупреждение
    «Не хватает линий: N из M», ключ `gpu.lineup.shortage`), `ReservedOn(nodeName)` для GP2, себестоимость карты дня `UnitCostOf(productId)` (F12) и окно спроса за 7 дней
    для режима «авто». Рынок (TSK-019) должен звать `GpuFeature.Instance.Production.RecordDemand(productId, sold, missed)` раз в день; без данных «авто» держит текущие линии.
    `Reset()` зовётся при старте сессии.
  - Резерв делают карты игрока со статусом `ready` или `selling` (не `retired`), `lines > 0` или `auto`; карта на неизвестном техпроцессе линий не занимает (предупреждение
    в лог один раз). Fabless — резерва нет (ветка аутсорса — TSK-017).
  - Линии клиентов-литейщиков: по одному контракту на клиента среди владельцев `ICompany.OutsourcedCpus` → `BusinessContractManager.GetFoundryServicesContract(...)
    .ContractedProductionCapacity()`. Линии CPU для «CPU в приоритете»: `CurrentProductionLines + CurrentContractedProductionLines` по `Owner.GetCpus()` без аутсорса и не снятых.
  - Уведомления сервисов идут через `IGpuGame.Notify(key, argument, argumentIsKey)` → `ModNotify.ShowKey`; ключи `gpu.notify.researchDone` (аргумент — ключ имени детали),
    `gpu.notify.projectDone` (имя карты). Событие `GpuProjects.ReadyToRelease(productId)` пока никто не слушает — диалог выпуска (TSK-025) подпишется на него.
  - Синергия: провайдеры на `market.rating.cpu` и `gpu.rating` зарегистрированы в `Register` (один раз), читают `GpuSynergyTable`; таблица перестраивается из
    `State.Data.synergy.entries` при старте сессии. TSK-040 после месячного пересчёта зовёт `GpuFeature.RebuildSynergy()` (internal; он же сбрасывает кэш обоих каналов).
  - Кэш техпроцессов (`GpuGameAdapter.RefreshNodes`) строится при старте сессии и раз в месяц из узлов игрока, узлов карт и дизайнов из сейва и `timeline.nodeByYear`
    (для соперников TSK-018); это единственное место с `DataFinder.FindLithography`. `TryGetNode` в течение месяца ищет новое только в `ICompany.ProcessNodes` игрока.
- Новые ключи строк (ru/en): `gpu.hub.selfCheck.*`, `gpu.hub.capability.*`, `gpu.found.block.*`, `gpu.research.block.*`, `gpu.project.block.*`, `gpu.notify.researchDone`,
  `gpu.notify.projectDone`, `gpu.lineup.shortage`, `gpu.synergy.cpu|gpu`.

## 5. Кусок 4 (TSK-017…TSK-021): fabless, генератор, рынок, линейка, GP1. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 992 КБ), тесты `--tests` зелёные (1502, из них 59 новых:
`GpuFablessTests` 8, `GpuRivalGeneratorTests` 14, `GpuMarketServiceTests` 17, `GpuLineupTests` 15, `GpuProductionScreenTests` 5). Шестнадцать намеренных поломок
(статья аутсорса, наценка, переход типа компании, роялти, период фона, повторный засев, запас, «устарел», архив, стартовая популярность, бухгалтерия, порядок охлаждения,
степень двойки, снижение частоты, нижняя граница цены, «CPU в приоритете» на экране) — каждую поймал хотя бы один тест.

- Где что лежит (отличия от путей задач): `Logic/Rivals/GpuRivalGenerator.cs` и `Logic/Rivals/GpuRandom.cs` (не `Rivals/`), `Logic/Services/GpuMarketService.cs`,
  `GpuLineup.cs`, `GpuViews.cs` (`GpuSegmentView`, `GpuMarketEntry`, `GpuProductView`), `GpuProductionScreen.cs` (чистое правило GP1). Игровая сторона GP1 —
  `Patches/ProductionWindowPatch.cs`. Тесты — в `tests/PTMod.Tests/Features/GpuProductLine/` (не `tests/PTMod.Tests/GpuProductLine/`).
- `IGpuGame` получил `FrontierFrequencyKHz()` — наибольшая `MaxFrequency` среди `ModCompanies.All` (без банкротов) и игрока (F22, частота фона и виртуальных вендоров).
- Генератор (для TSK-033, TSK-034):
  - `GpuRivalTech` отличается от design: вместо `Transistors` (их нельзя знать до выбора площади) — `DensityIncrease`; добавлены `WaferDiameter` (0 → 300 мм),
    `Yield` (0 → `Lerp(min, max, fablessYieldProgress)`), `MaintenancePerLine`, `WaferMaterial`, `UnitCostMul`, `VramCostMul`, `DevModifier` и делегат `DieCost`
    `(die, material, dpwYield, fr) => CpuMath.RealUnitCost(компания, ...)` — привязанному вендору его передаёт сервис; без делегата считается порт.
  - `GpuRivalGenerator.Generate(seg, tech, db, year, rng, budget, priceFactor)` → `GpuRivalDesign { Input, Stats, Price, Valid }` с ценой F23
    `max(priceFloorMarkup × U, budget × priceFactor)`; `GenerateBackground(...)` — цена `budget × background.priceFactor`; `DesignFor` — только выбор (F22);
    `Snapshot` — снимок характеристик для продукта. Порядок случайных чисел фиксирован: частота, затем площадь (по одному `Next01`).
  - До первой записи списка (1981 с отставанием фона 2 года) берётся самая ранняя запись: иначе в первый год рынка фона бы не было.
  - Частота ±5 % — константа `GpuRivalGenerator.ClockJitter` (в `balance.rivals` такого поля нет; `dieJitter` — только для площади).
  - Потоки: `GpuRandom.RivalsStream` = `gpu-product-line/rivals` (вендоры), `GpuRandom.BackgroundStream` = `gpu-product-line/background` (фон; его уже берёт
    `GpuFeature` через `ModRandom.Stream`).
- Рынок (для TSK-026, TSK-030…TSK-033, TSK-040):
  - Обработчики: `GpuMarketService.SellDay` (`MarketSellTick`), `DayTick` (`DayPassed`), `MonthTick` (`MonthPassed`, приоритет `GpuFeature.MarketMonthPriority` = −10;
    кэш техпроцессов — 0, линейка — −20). Эпохам и вендорам этапа 2 нужен приоритет между 0 и −10 (например, −2 и −5), синергии — ниже −20.
  - Засев: `SeedForYear(year)` один раз за партию (`market.seeded`), событие `Seeded(year)` — сюда TSK-033 подключает «активных соперников года» (AC-9.1).
    `GpuFeature` зовёт засев на `TickPhase.FastForwardEnded` новой игры (сначала `RefreshNodes`) и `Market.StartSession()` на `GameReady` (сейв без секции).
    На `FastForwardEnded` кэш `ModCompanies.All` ещё пуст, а компания игрока не инициализирована: если частоты нет, фон не создаётся (одна строка в лог), флаг
    засева всё равно ставится, а `StartSession` на `GameReady(NewGame)` достраивает недостающий фон. Тот же `EnsureBackgrounds` раз в месяц даёт фон новому сегменту.
  - Открытые сегменты сейчас — правило режима `single`: `opensYear ≤ год < closesYear` (`OpenSegmentsAt`). `GpuEras` (TSK-030/031) должен заменить его на
    `market.openSegIds` (одно место — `OpenSegmentsAt`).
  - Фон — продукт `bg-<segmentId>`, `owner = background`, `companyKey = ""`, `name = ""` (вкладка показывает `gpu.vendor.background`, строка уже есть), `designId = ""`,
    запас 0 при бесконечном запасе на рынке; раз в `refreshMonths` обновляется на месте (тот же id, новая дата `released`).
  - Продукт вендора: `owner = vendor`, ключ владельца на рынке `vendor:<vendorId>` (`GpuMarketService.OwnerKeyOf`), выручку привязанному вендору рынок уже платит
    (`Earn(companyKey, ..., Sales)`); себестоимость проданных, пополнение запаса (F24) и лимит 2 продуктов на сегмент — TSK-033.
  - Роялти fabless (5 %, `GpuProduction.RoyaltyRate`) удерживает рынок при оплате продаж игрока, если компания fabless в день продажи (флага «произведено на аутсорсе»
    у продукта нет: поле в сейв не добавлялось).
  - Окна 30 дней по продуктам и владельцам (`SoldLast`, `ShareOf`, `TopProducts.Sold30`) в сейв не пишутся; в сейве — `market.playerShareDaily` (30 дней,
    `PlayerShare`) и `market.playerSoldMonthly` (24 полных месяца и текущий). `market.potentialSales` — покупатели дня по открытым сегментам.
  - Частоту карточки без дизайна (фон, вендоры) `TopProducts` выводит как `computeMops / units / opsPerClock` архитектуры с теми же функциями (в снимке частоты нет).
- Линейка (для TSK-025): `Release(productId, price, sell, lines)` — для fabless `lines` означает шт./день; `SuggestedPrice()` = 0,9 × бюджет первого открытого
  сегмента; события `Released`, `Retired`; `Retire` возвращает выплату F31 (подтверждение `ModUI.Confirm` — во вкладке); `SetLines`/`SetUnitsPerDay` выключают «авто».
  `GpuProjects.ReadyToRelease` по-прежнему никто не слушает — диалог выпуска подпишется на него.
- Производство: `GpuProduction.FablessToday`, `OutsourcedToday`; для fabless `GrantedLines(id)` — заказанные сегодня шт. Смена типа компании отслеживается в памяти:
  после загрузки первый день переходом не считается.
- GP1: патч объявлен через `ctx.Patch`, поэтому строка самопроверки до TSK-041 будет `patches 1/1`. Число на экране — резерв дня (`GpuFeature.ProductionScreenLines`;
  0 без подразделения, для fabless и при выключенной `GpuCapability.ProductionUi`). Как выглядит экран в игре, проверяет владелец.
- Новые ключи строк (ru/en): `gpu.productionUi.reserved`, `gpu.lineup.block.unknown|notReady|retired`, `gpu.lineup.old`, `gpu.vendor.background`.

## 6. Кусок 5 (TSK-022…TSK-027): окно «Видеокарты», вкладки, диалоги, строки этапа 1. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1134 КБ), тесты `--tests` зелёные (1608, из них 106 новых:
`GpuUiBasicsTests` 10, `GpuFoundAndResearchTabTests` 19, `GpuDesignerModelTests` 23, `GpuSegmentEstimatorTests` 7, `GpuLineupAndReleaseTabTests` 18, `GpuMarketTabModelTests` 11,
`GpuL10nKeysTests` 7, `GpuUiSourceTests` 11). Восемнадцать намеренных поломок в чистых моделях (формула потерь на ПСП, привязка ползунка, предупреждения, порядок месяцев, подпись
вкладки, цена выпуска, коэффициент VRAM, цена оценки, всегда разрешённое основание и т. д.) — каждую поймал хотя бы один тест.

- **Необязательный запрос к ядру (дублирование).** В `ModUI` нет виджетов «поле ввода», «полоса прогресса» и «группа вкладок» (в задачах — `InputFieldUI`, `ProgressBar`, `ButtonTabGroup`
  игры), поэтому фича собрала свои: `Ui/GpuUi.cs` — `GpuInput` (`TMP_InputField` из частей TextMeshPro, шрифт и цвет — через `ModUI.Text`), `GpuBar` (две картинки, цвета темы
  `ProgressBar`/`ProgressBackground`), вкладки — кнопки `ModUI.Button` (текущая — `ButtonStyle.CallToAction`), как в хабе. Похожие собственные полосы уже есть у `cpu-architectures`
  (`ThemedBar`) и `platform-generations`. Если интегратор добавит `ModUI.InputField` / `ModUI.ProgressBar` / `ModUI.Tabs`, `GpuInput` и `GpuBar` можно заменить без изменения моделей.
- Где что лежит (отличия от путей задач): папка называется `Ui/` (как у `GpuWindow` с куска 1), не `UI/`. Чистые модели вкладок — `Logic/Ui/` (их собирают тесты):
  `GpuUiBasics.cs` (`IGpuText`, `GpuTone`, `GpuTab`/`GpuTabs`, `GpuSliderRange`, `GpuUiMath`), `GpuNames`, `GpuFoundModel`, `GpuResearchTabModel`, `GpuDesignerModel`,
  `GpuSegmentEstimator`, `GpuLineupModel`, `GpuReleaseModel`, `GpuMarketTabModel`. Игровая часть — `Ui/GpuUi.cs` (`GameGpuText : IGpuText` поверх `ModL10n` и `StringFormatter`,
  `GpuUi`, `GpuStatLineView`, `GpuBar`, `GpuInput`), `GpuWindow`, `GpuFoundDialog`, `GpuReleaseDialog`, `GpuResearchTab`, `GpuDesignerTab`, `GpuLineupTab`, `GpuMarketTab`.
  Тесты — `tests/PTMod.Tests/Features/GpuProductLine/` (не `GpuProductLine/`), строки — `L10n/ru|en/gpu-product-line.json` (не `ru.json`/`en.json`: так принято у всех фич).
- Окно и диалоги (для TSK-030…TSK-049):
  - `GpuWindow.Open(GpuTab tab = Auto)` — единственный вход (кнопка страницы фичи в хабе); до основания вместо окна открывается `GpuFoundDialog` (модальный), после основания окно
    открывается на вкладке `Research`. Последняя вкладка — `ModSave.Flags.SetInt("gpu-product-line", "lastTab", n)` (1…4). Окно — `Window.Panel` 960x620, диалоги — `Window.Modal`.
    Из письма-предложения консольного заказа (TSK-049) окно открывает `GpuWindow.Open(GpuTab.Lineup)`.
  - Раз в игровой день окно обновляется обработчиком `DayPassed` с приоритетом `GpuFeature.UiDayPriority` = −50 (после рынка и линейки): `GpuWindow.RefreshDaily` ничего не делает при
    закрытом окне (AC-8.2). Вкладки обновляют тексты на месте и пересобираются, только если изменилась форма (новый узел, строка линейки, сегмент). Интерфейс `IGpuTabView`.
  - Диалог выпуска открывается на событие `GpuProjects.ReadyToRelease` (`GpuFeature.OnProjectReady` → `GpuReleaseDialog.OnReady`, кадром позже); несколько готовых карт идут по очереди.
    «Позже» или крестик оставляют карту `ready`; кнопка «Выпустить» есть в строке линейки.
  - `GpuWindow.Close()` закрывает и оба диалога (его зовёт `GpuFeature.OnDisabled`); `GpuDesignerTab.Forget()` сбрасывает конструктор в работе (зовётся на `GameReady` и при выключении).
- Чистые модели, на которые встанут этапы 2 и 3:
  - `GpuMarketTabModel` (TSK-037): блок сегмента (`GpuMarketSegmentBlock`), доли (`Shares`; сейчас «Прочие» и игрок, вендоры этапа 2 добавляются там же через `MakerOf` и `ShareOf("vendor:<id>")`),
    строка эпохи и строка синергии добавляются в `GpuMarketTabModel` и `GpuMarketTab.Draw`. Подпись формы вкладки (`Signature`) надо дополнить новыми блоками.
  - Скидка совместной разработки (TSK-042): `GpuDesignerModel.CoDevText` берёт `GpuDesignContext.CoDevFactor`; когда `GpuDesigner.ContextFor` начнёт отдавать коэффициент < 1, строка
    «Совместная разработка: −N %» появится сама (скрыта при 1).
  - Консольные заказы (TSK-049): блок добавляется в `GpuLineupModel` / `GpuLineupTab`; предупреждение о расторжении — в тексте `gpu.lineup.retire.message` (сейчас без него).
- `IGpuGame` получил `PlayerNodes()` (техпроцессы игрока, от старого к новому; у fabless-компании ещё и техпроцессы литейщиков — как список в окне выбора оборудования игры), реализован
  в `GpuGameAdapter` и в фейке `FakeGpuGame`. Конструктор выбирает техпроцесс только из них.
- Конструктор: пересчёт — только по событию своего элемента (`OnValueChanged` ползунков и списков) и один раз на изменение (`GpuDesignerModel.Recomputes` в тестах); замер каждого изменения пишется
  в лог подробного режима (`designer: a change of an input took N ms`, раз в 50 изменений или при превышении 1 мс). Тип памяти меняет шины и объёмы VRAM, поэтому его выбор
  пересобирает вкладку (модель с выбором сохраняется). Частота — доля (25…100 %) от наибольшей частоты архитектуры, площадь — шаг «круглых» значений (300 мм² достижим), VRAM — степени
  двойки (до 12 значений вниз от предела типа памяти). Цена и название вводятся в поле `GpuInput`; если поле не удалось создать, у цены остаются кнопки «−» / «+» (шаг 5 % бюджета).
- Разбор AC-3.3: на ползунке площади предел — наибольший кристалл года, поэтому «Кристалл не помещается на пластину» достижим только на маленькой пластине (тест берёт 20 мм).
  Слишком горячий дизайн (брак 100 %) блокируется с причиной «Не удалось рассчитать характеристики», а не предупреждением: себестоимость кристалла бесконечна (правило FR-3 п. 5).
- Новые ключи строк (ru/en) — около 250, все с префиксом `gpu.`: `gpu.window.*`, `gpu.tab.*`, `gpu.fmt.days|years` (множественные формы одним текстом через `|`, поэтому в коде
  нет составных ключей-групп), `gpu.found.*`, `gpu.research.*`, `gpu.design.*` (включая `gpu.design.stat.<ключ>` и `.tip` для 13 характеристик), `gpu.lineup.*`, `gpu.release.*`,
  `gpu.market.*`, `gpu.feat.<id функции>` (9). Удалён `gpu.window.placeholder`.
- Что может проверить только владелец в игре (игру из инструментов не запускаем): размеры и раскладка окна 960x620 и диалогов, вид кнопок-вкладок и узлов дерева (цвет текста кнопки
  тегами), поле ввода из частей TextMeshPro (фокус, ввод, Enter / потеря фокуса, предел 32 знака), подсказки на строках характеристик и узлах, перекраска при смене темы, остановка
  времени в диалогах основания и выпуска, обновление окна раз в день на скорости 3 и 12, сохранение последней вкладки в сейве.

## 7. Кусок 6 (TSK-028): интеграционная симуляция этапа 1 и замер времени. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1140 КБ), тесты `--tests` зелёные (1633, из них 25 новых:
`GpuSimulationTests` 12, `GpuPerfMeterTests` 10, `GpuSellDayEquivalenceTests` 3). Намеренные поломки (резерв без урезания по мощности, потеря выручки рынка, фон без
обновления, кэш весов в `SellDay` без проверки быстрейшего продукта) каждая ловится хотя бы одним тестом.

- Где что лежит (отличия от путей задачи): тесты — `tests/PTMod.Tests/Features/GpuProductLine/` (не `GpuProductLine/`). Каркас прогона — `GpuSimulation.cs` (`GpuSimulation`, `SimPlan`,
  `SimMonth`; internal), тесты — `GpuSimulationTests.cs`. Фейки — прежние `FakeGpuGame`, `FakeFactory`, `GpuWorld` (у `GpuWorld` новый необязательный параметр `content`).
- **Замер времени (NFR-1), игровая сторона.** Новый чистый класс `Logic/Services/GpuPerfMeter` (часы, флаг и запись строки передаются снаружи): складывает работу обработчиков одного игрового
  дня (или месяца) в один отсчёт, раз в игровой месяц отдаёт строку `perf 1995-02: day n=28 avg=0.0123 ms max=0.2100 ms (budget 0.5 ms: ok); month n=1 avg=… (budget 5.0 ms: ok)`.
  Виды: `GpuPerfMeter.Day` (бюджет 0,5 мс), `Month` (5 мс), `UiDay` (обновление открытого окна, 2 мс). В `GpuFeature` — `Perf` (`ModSettings.VerboseLog`, `Stopwatch.GetTimestamp`,
  `LogPerf`: `ModLog.Debug`, а при среднем выше бюджета `ModLog.Warn`) и `internal void Timed(string kind, string siteId, Action body)` = `Guard` плюс замер. Без подробного журнала
  вызов стоит один опрос флага. **Следующим кускам:** обработчики дня и месяца этапов 2 и 3 (эпохи, соперники, ИИ вендоров, синергия, консольные заказы) подписывать через
  `Timed(GpuPerfMeter.Day|Month, "<site>", …)`, а не через голый `Guard`, иначе их время не попадёт в строку. Тесты-тексты, которые смотрели на `Guard("production", …)` и подобные
  (`GpuServicesSourceTests`, `GpuProductionScreenTests`, `GpuUiSourceTests`), переписаны под `Timed(...)`; `GpuPerfMeterTests.TheFeature_TimesEveryHandler…` перечисляет все девять
  обработчиков и окно.
- **Бот и прогон.** `SimPlan` (годы 1981–2010; основание 1 января 1995; карта каждые 2 года; 60 линий, 20 из них занимают CPU после резерва; `ReplaceOnRelease`, `CpuFirst`, `FablessFromYear`,
  `Seed`, `Content`, `AfterSeed`). Бот раз в месяц: основывает подразделение, держит лабораторию занятой (узлы с годом не позже текущего), стартует проект из исследованных частей
  (лучший допустимый дизайн из перебора шин, площадей, частот и кулеров, себестоимость карты не выше 55 % бюджета), выпускает по `Lineup.SuggestedPrice()` с «авто», снимает поколение
  с продажи. Ход дня — как в ядре: `ProduceDay` → CPU берут остаток линий → `Research`/`Projects` → `SellDay` → `DayTick` рынка и линейки → сброс фабрики; 1-го числа — `MonthTick`
  рынка и линейки. Игровые числа бота (частота отрасли, пластина, технологии игры, инфляция 3 %) — в `GpuSimulation.UpdateWorld`. **Для TSK-033/045:** режим `eras` добавляется
  `SimPlan.Content` (свой набор файлов) и `AfterSeed` (вендоры года); `GpuSimulation.Report()` печатает доли по годам, `Violations` — нарушенные инварианты.
- **Итоги прогона на машине, где работали тесты (эталонный бот):** день ≈ 0,005 мс при бюджете 0,5 мс, месяц ≈ 0,003 мс при бюджете 5 мс (лучший из трёх прогонов; самый долгий
  один день 0,3–2,5 мс — компиляция и сборка мусора); «Прочие» не ниже 45 % до 2000 года (бюджет NFR-7: 15 %); игрок 8 карт за 30 лет, секция сейва 19 КБ (бюджет 2 МБ), загрузка секции
  без единого предупреждения. Прогоны с тем же сидом дают одну и ту же секцию (NFR-6). Игрок без своей фабрики (fabless с 1995 и продажа фабрики в 2002 — один перенос линий в штуки,
  одна строка в логе) и режим «CPU в приоритете» (карты не берут больше, чем оставили CPU) проходят без нарушений.
- **Худший день NFR-1** (150 карт — 44 игрока, 100 вендоров, 6 фонов — в 6 сегментах, каждая карта оценивается в каждом сегменте): 0,41–0,47 мс при бюджете 0,5 (поэтому тест берёт лучший из
  четырёх прогонов года). Было 0,88 мс; ускорение в `GpuMarketMath.SellDay` без изменения чисел (600 случайных дней совпадают точь-в-точь с «простым» днём, `GpuSellDayEquivalenceTests`):
  проходы идут по массивам, владельцы нумеруются один раз за день (без словаря в `Dilute`), рейтинг каждого продукта в каждом сегменте и вес «отстал от быстрейшего» считаются один раз за
  день, а второй и третий проход только вычёркивают проданные дочиста продукты (и пересчитывают веса, если выбыл быстрейший); `GpuMarketService.SellDay` использует пул `MarketItem`
  (`BuildItems` для вкладок отдаёт новые), окна дней по 30 дней переиспользуют словарь, выбывший из окна. Публичные сигнатуры прежние, кроме `GpuMarketMath.FeatureFactor(IList<string> have, …)`
  (массив подходит). **Запас тонкий:** игровые обращения (`GpuGameAdapter`, каналы `ModModifiers`) в симуляции заменены фейком, то есть в игре день дороже. Настоящее время покажет журнал
  игры: включить подробный журнал и смотреть строки `[gpu-product-line] perf …` в `Player.log` (проверка владельца в TSK-029/TSK-045).
- **Находки для калибровки (TSK-045), ничего в контенте не менялось:**
  1. Игрок, который держит на продаже два поколения сразу (тест `ThePlayerWhoKeepsEveryCardOnSale…`), оставляет «Прочим» около 11 % в 1997–98 годах (бюджет 15 %). Причина в контенте этапа 1:
     у сегмента `consumer` пустые `featureReqs`, поэтому рынок не ценит 3D, а по сырой производительности старая GUI-карта 1995 года (≈ 740: 53 блока по 25 000 транзисторов на 84 мм²)
     обгоняет первые 3D-карты (≈ 130–280: 400 000 транзисторов на блок дают 7 блоков на 106 мм²). При цене на 10 % ниже бюджета и популярности 1,0 против 0,35 у фона доля игрока
     доходит до 88 %. Эталонный бот снимает прежнее поколение при выпуске нового
     (тогда «Прочие» ≥ 45 %). Варианты: `featureReqs` для `consumer` (`3d` с 1996–97, как у `gaming.high`), пересмотр чисел `arch.3d`, `background.popularity`/`techLagYears`. Тест держит порог 10 %
     как «хуже не становится» и печатает фактическое значение.
  2. Первая карта бота выходит в августе 1995 года на архитектуре GUI: `arch.3d` исследуется только к январю 1996; это часть той же картины.
  3. Сегмент `consumer` в симуляции никогда не закрывается и единственный: доли вендоров (NFR-7, «ни один вендор > 70 % дольше 3 лет») проверяются только в TSK-033/045.

## 8. Кусок 7 (TSK-030…TSK-035): эпохи, вендоры, ИИ вендоров. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1178 КБ), тесты `--tests` зелёные (1731, из них 98 новых: `GpuErasTests` 20, `GpuRivalsTests` 25,
`GpuRivalAITests` 19, `GpuErasContentTests` 12, `GpuErasLineupTests` 7, расширение `GpuSimulationTests` режимом `eras` 12, проверки по тексту кода 2, контент этапа 1 в режиме `single` 1).
Девятнадцать намеренных поломок (закрытие года включительно, нет стартовой популярности, миграция ×0,5, слияние без смены компании, игнор `fallbackVirtual`, дефолт не проверяется, нестрогий порог,
ёмкость целиком, лимит 3, нет +0–3 месяцев, строгий срок, вендоры всегда включены, нет оплаты разработки и себестоимости, вход до года, засев с событиями, запас не новейшей карте, нет догона закрытий,
вендор входит во все сегменты) — каждую поймал хотя бы один тест.

- Где что лежит (отличия от путей задач): `Logic/Services/GpuEras.cs`, `Logic/Rivals/GpuRivals.cs` (вместе с `GpuVendorRuntime`) и `Logic/Rivals/GpuRivalAI.cs` (не `Services/` и `Rivals/`: так их собирают тесты);
  тесты — `tests/PTMod.Tests/Features/GpuProductLine/` (не `GpuProductLine/`).
- **Режим по умолчанию теперь `eras`.** `balance.json` ставит `marketMode: "eras"`; DTO `GpuBalanceDto` по-прежнему по умолчанию `single` (так же читают фикстуры тестов). Режим этапа 1 остаётся рабочим:
  правка `balance.json` в `PTMod/content/gpu-product-line/` возвращает один сегмент `consumer`. В тестах `GpuWorld.Shipped()` и `GpuFiles.ShippedSingle()` — поставляемый контент в режиме `single`
  (на нём идут тесты этапа 1), `GpuWorld.ShippedEras()` и `GpuFiles.Shipped()` — как поставляется. `GpuWorld` получил `Eras`, `Rivals`, `RivalsOn` (по умолчанию выключено: миры этапа 1 без вендоров),
  `MonthTicks()`; `FakeGpuGame` — `Companies` (ИИ-компании `ai:1`, `ai:0` с деньгами, дефолтом, банкротством, узлом), `SpentBy`.
- Новые поля секции (только добавления, без смены схемы: недостающие поля берут значения по умолчанию, `GpuState.Sanitize` их проверяет): `market.erasCheckedOn` (дата последней сверки с календарём эр),
  `GpuSegLaunch.extraMonths` (0–3 месяца к `designIntervalMonths`, выпавшие при запуске), `GpuStatsSave.unitCost` (себестоимость карты вендора на момент запуска, F12: нужна, чтобы рынок списывал
  себестоимость проданного у привязанной компании).
- `IGpuGame` получил `CompanyExists`, `IsInDefault`, `CompanyName`, `TryRivalTech` (реализация — `GpuGameAdapter`: самый плотный из `ICompany.ProcessNodes`, `MaxFrequency`, `ResearchModifiers.DensityIncrease`,
  пластина и выход годных, `CpuMath.RealUnitCost` компании; fabless-компания платит наценку литейщика). Самопроверка не менялась (12 членов игры, 6 полей).
- `GpuFeature`: свойства `Eras`, `Rivals`; подписки: `MarketSellTick` — `RivalsStockTick` (приоритет `RivalsStockPriority` = 10, до рынка), `DayPassed` — `RivalsDayTick` (`CheckBoundCompanies`),
  `MonthPassed` — узлы (0), `ErasMonthTick` (−2), `RivalsMonthTick` (−5), рынок (−10), линейка (−20); все под `Timed(...)` (сайты `rivals/stock`, `rivals/day`, `eras/month`, `rivals/month`).
  На `GameReady` (и при включении фичи в партии): `Eras.ApplyModeSwitchIfNeeded` → `Market.StartSession` → `Rivals.StartSession`. Засев рынка (`GpuMarketService.Seeded`) засевает и вендоров.
  Переключатель `rivals` читается каждый день (`GpuMarketService.RivalsEnabled`).
- Как работают вендоры (для TSK-036, TSK-037, TSK-043):
  - Карта вендора — продукт `owner = vendor` с одним сегментом в `segIds` (как у фона), удаляется при снятии (архива у вендоров нет: иначе за 30 лет список упёрся бы в лимит 500). Название `<series> <launchCount>`.
    Ключ владельца на рынке `vendor:<id>`, `GpuMarketEntry.VendorId`, `GpuRivals.NameOf(vendor, russian)` (имя компании из игры для привязанного, имя из контента для виртуального), цвет — `Def.color`.
  - События (все без полезной нагрузки сверх объектов): `GpuRivals.Entered`/`Exited`/`Merged(поглощённый, поглотитель)`, `GpuRivals.AI.Launched(вендор, карта)` (не при засеве), `GpuEras.SegmentOpened`/`SegmentClosed`/
    `EraStarted`. Дата события — 1 января года из контента (`entryYear`, `exitYear`, `mergeYear`, `opensYear`, `closesYear`, `startYear`): письма «не старше предыдущего месяца» новости отбирают по ней.
    События поднимаются и на месячном тике после простоя за все пропущенные годы сразу. `Rivals.StartSession` (на `GameReady`) может поднять `Exited`, если компании вендора уже нет в партии, —
    новостям нужно молчать при загрузке и перемотке (`ModTicks` их тика не вызывает, но `GameReady` и засев идут вне тика). `NewLeader` из design не сделан — это TSK-036.
  - Тексты: `gpu.era.<id>` (название эпохи, заголовок письма) и `gpu.mail.era.<id>` (текст письма) есть в ru/en; строки сегментов `gpu.seg.*`. Строк вендоров нет: имена в контенте (`name.ru`/`name.en`).
  - Привязка: сид привязывает `bindTo` без проверки (компании новой игры при засеве ещё не созданы), а `CheckBoundCompanies` на `GameReady` и каждый день приводит в порядок: `fallbackVirtual` — вендор виртуальный
    (`companyKey` пуст и у его карт), иначе уходит. На входе (`MonthTick`): компания есть и не банкрот — привязан, нет компании и `fallbackVirtual` — виртуальный (AC-12.4), нет и без запасного — ждёт (одна строка в лог).
  - Слияние: карты поглощённого переходят к поглотителю (`vendorId`, `companyKey`, популярность сохраняется), лимит двух карт в сегменте применяется к обоим вендорам; поглотитель, которого ещё нет, входит досрочно;
    серия с тем же `series` (Radeon у ATI и AMD) продолжает нумерацию.
  - Деньги привязанного вендора: разработка `projectCost` при запуске (`Development`), себестоимость проданных `unitCost × продано` (`Factories`, списывает рынок в `SellDay`), выручка (`Sales`, платит рынок).
    Запуск откладывается при дефолте, банкротстве и когда `деньги − стоимость < ModCredit.ThresholdOf` (баланс не меняется). Засев ничего не платит.
  - Первая популярность карты вендора — `max(brandCarry × лучшая карта вендора в сегменте, strength × background.popularity)` (поле `strength` в design формул не имело: использовано здесь).
  - Ёмкость (F24): запас вендора в сегменте — до `round(capacityShare × N_s)`, добавку получает новейшая карта; линий фабрики вендоры не занимают.
- Для TSK-037 (UI рынка этапа 2): вкладка «Рынок» этапа 1 уже строит блоки по `OpenSegments` (4–6 сегментов), но производитель карты вендора пока выводится как id вендора (`GpuMarketTabModel.MakerOf`),
  а доли — только «Прочие» и «Вы» (долей вендоров нет; нужны `GpuMarketService.ShareOf("vendor:<id>")`, имена и цвета вендоров). Пометки «Нет: <функция>» строятся из `GpuEras.RequiredFeatures` и
  `GpuMarketMath.FeatureFactor`. Для окон этапа 1 добавлен «опорный сегмент» — открытый сегмент с наибольшим пулом денег (покупатели дня × бюджет): `GpuMarketService.ReferenceSegmentAt(date)`; от него начинает
  конструктор (площадь кристалла и VRAM по умолчанию) и от него цена без названной карты `GpuLineup.SuggestedPrice()`; для готовой карты диалог выпуска берёт `GpuLineup.SuggestedPrice(productId)` —
  0,9 × бюджет сегмента, который карта «подходит» (`GpuLineup.FitSegment`: все функции есть и VRAM не меньше 0,75 от требования, из таких — с наибольшим бюджетом).
- Для TSK-043 (`GpuApi.TransferVendorProducts`, `GpuRivals.TransferProducts`) не сделано. Нужное уже есть: `GpuRivals.Find(id)`, `AI.CardsOf(vendorId)`, состояния `Save.state`/`Save.mergedInto`,
  `Merged`. Продукт вендора → игроку: `owner = player`, `companyKey = "player"`, `vendorId = ""`, линии 0, статус `selling`, имя и популярность сохраняются; сам вендор — `merged` с `mergedInto = "player"`.
- **Находки для калибровки (TSK-045).** В контенте ничего не менялось, кроме того, что названо в строке TSK-030 (контрольная точка VRAM 2004 у `gaming.high`):
  1. «Прочие производители» (фон) в режиме эр держат до 2000 года минимум 0,8 % рынка (цель NFR-7: 15 %): именованные вендоры забирают почти всё. Рычаги: `capacityShare` (в офисном сегменте 1990 года
     сумма Matrox + ATI + S3 = 1,25 от покупателей), `background.popularity`, `strength`. Третий пункт NFR-7 (ни один вендор не держит > 70 % сегмента дольше 3 лет) выполнен: самая длинная серия — 12 месяцев.
  2. Первый год 3D (1996): фон держит около 92 % игровых сегментов, карты вендоров почти не продаются до 1997 года. Причина та же, что у находки TSK-028: сырая производительность карты на `arch.gui`
     (25 000 транзисторов на блок, 2 операции) выше первой 3D-карты (400 000 транзисторов на блок), штрафа ×0,35 за отсутствие `3d` не хватает.
  3. 2016–2018: карты на HBM дорогие из-за платы (`boardCostPerBit` × 1024 бит = 82 + `boardBase` 20): в 2017 году в `gaming.low` фон держит 93 %, в `gaming.mid` — 38 %.
  4. Офисный сегмент в 2006–2009 годах без вендоров (Matrox ушёл в 2005, ATI влилась в AMD, у AMD в design нет `office2d`): продают фон и карты ATI, которые остались у AMD.
  5. `compute` в 2025 году требует 49 152 МБ, а память не больше 32 768 МБ (`vram.hbm`, `vram.gddr7`): коэффициент VRAM не выше 0,67 (числа design).
  6. Финансы привязанных за прогон 1990–2035 (виртуальных вендоров нет, деньги ИИ-компаний в тесте не ограничены): AMD за 30 лет — продажи 337 млрд, разработка 79 млрд, себестоимость 64 млрд;
     Intel за 14 лет — продажи 43 млрд, разработка 29 млрд, себестоимость 13 млрд (почти ноль в плюсе). Стоит проверить в игре, что разработка GPU не топит AMD и Intel (пауза по порогу есть,
     но свободные деньги идут и на процессоры).
  7. Время: день 0,05 мс при бюджете 0,5 мс, месяц 0,04 мс при бюджете 5 мс (1990–2035, семь вендоров, шесть сегментов); самый тяжёлый день NFR-1 (150 карт × 6 сегментов) остаётся 0,2–0,4 мс.
- Что может проверить только владелец (игру не запускаем): как выглядит рынок с вендорами в живой партии (цены, доли, письма — после TSK-036); что `ModCompanies.Resolve("ai:1"/"ai:0")` в игре даёт AMD и Intel
  (самопроверка проверяет `IsEMD(1)`, `IsInlet(0)`); что `TryRivalTech` у ИИ-компании возвращает узел и частоту (в логе нет строк `rivals: ... cannot make a card`); выручка и расходы AMD/Intel в окне финансов компании;
  загрузка сейва этапа 1 (его карты переезжают в сегменты эр с той же популярностью, фон и вендоры появляются), банкротство Intel (карты уходят) и сценарий «AMD нет в партии».

## 9. Кусок 8 (TSK-036…TSK-038): письма рынка, вкладка «Рынок» этапа 2, строки. Справка для следующих кусков (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1204 КБ), тесты `--tests` зелёные (1780, из них 49 новых: `GpuNewsTests` 28,
`GpuMarketStage2Tests` 18, `GpuL10nKeysTests` +3). Двадцать одна намеренная поломка нового кода (окно свежести письма, письмо сегмента вместе с эпохой, нет паузы лидера, лидеры
не слушают переключатель, эпоха за переключателем, счётчик тишины, нет защиты от повтора, пауза записана за отвергнутое письмо, перепутаны имена слияния, дата ухода всегда сегодня,
карта без функции считается соперником, ничья ведёт, пометки у карты на паузе, пометки только по `segIds`, нет счёта по сегментам, окно сегментов не сбрасывается, старт сессии не
тихий, проверка «игра в обычном ходу» всегда истина, имена вендоров по id, нет блока эпохи, потеряна строка игрока) — каждую поймал хотя бы один тест (две поломки сначала выжили:
ничью и пометки «только по `segIds`», на них добавлены `ATieOrNoSpeedAtAll_LeadsNobody` и `ACardOfThePlayer_IsMarkedInEverySegmentOpen…`).

- Где что лежит (отличия от путей задач): `Logic/Services/GpuNews.cs` (не `Services/`; вместе с портом `IGpuMail` и `GpuMailLetter`), `Bridges/GpuMailAdapter.cs` (единственное место, где
  фича зовёт `ModMail`: только `Send` и `Find`), `Logic/Ui/GpuFeatureMarks.cs` (пометки «Нет: <функция>»), `GpuMarketTabModel` и `Ui/GpuMarketTab.cs` расширены; тесты —
  `tests/PTMod.Tests/Features/GpuProductLine/` (`GpuNewsTests`, `GpuMarketStage2Tests`).
- **Письма** (`GpuNews`, `GpuFeature.News`). Подписки (`Bind()`): `GpuEras.EraStarted`/`SegmentOpened`, `GpuRivals.Entered`/`Exited`/`Merged`, `GpuRivalAI.Launched`; каждый обработчик идёт под
  `GpuFeature.Guard` (сайты `news/era|segment|vendor/enter|exit|merge|leader`), поэтому сбой письма не прерывает месяц. Отличия от design: события `NewLeader` в `GpuRivals` нет — «новый лидер»
  определяется в `GpuNews` при запуске карты вендора (карта строго быстрее по `stats.effectivePerf` всех продаваемых карт сегмента, которые вообще могут в нём продаваться, то есть с
  `FeatureFactor > 0`; ничья не лидер, `GpuNews.IsNewLeader`); `Bind` не принимает аргументов (зависимости в конструкторе).
  - Теги и приоритеты: эпоха — `Normal`, `gpu:era:<eraId>` (заголовок `era.nameKey`, текст `era.mailKey`, без аргументов); сегмент, открывшийся в год без эпохи (в поставляемом контенте только
    `gaming.low`, 1998), — `Normal`, `gpu:segment:<id>` (требования года и узел дерева, который даёт нужные функции); вендор — `Normal`, `gpu:vendor:<id>:enter|exit|merge` (для слияния `<id>` —
    поглощённый); лидер — `Low`, `gpu:leader:<segmentId>`, не чаще раза в `balance.news.leaderCooldownMonths` (3) месяца на сегмент, дата последнего письма — `news.lastLeaderMail` в секции.
    Уход банкрота отличается текстом (`gpu.mail.vendor.exit.bankrupt`). Имя вендора в письме — имя компании игры для привязанного, имя из контента на языке игрока для остальных.
  - **Переключатель `news` гасит только письма о лидерах** (FR-14 п. 5 называет только их): письма об эпохах, сегментах и вендорах приходят всегда. Подсказка переключателя
    (`gpu.settings.news.tip`) это говорит. Если владелец захочет, чтобы переключатель гасил и письма о вендорах, это одна проверка в `GpuNews.CanSendAbout`.
  - **Тишина.** Писем нет при перемотке новой игры и загрузке (FR-14 п. 4): `GpuNews.Silently(...)` оборачивает старт сессии рынка (`GpuFeature.StartMarket`) и засев вендоров; плюс проверка
    `MailAllowed()` = `ModTicks.IsGameReady && !IsFastForwarding && !IsLoading`. Событие, дата которого раньше 1-го числа предыдущего календарного месяца, письма не получает (`GpuNews.IsFresh`,
    FR-14 п. 6, AC-14.2): датой события служит 1 января года из контента (`entryYear`, `exitYear`, `mergeYear`, `startYear`, `opensYear`); уход без года (банкротство) датируется сегодняшним днём.
    Один тег — одно письмо (`ModMail.Find` перед отправкой; тег лидера не проверяется, его ограничивает пауза). Отвергнутое ядром письмо (`Send` < 0) — одна строка в логе, пауза лидера не записывается.
  - Фича не выбирает id писем и не вводит дневных лимитов: очередь, доставка и лимиты (`mail.maxDeliveredPerDay`, `mail.maxPerFeaturePerDay`) — ядра. Тест проверяет по тексту, что `ModMail` зовёт только `GpuMailAdapter`.
  - Язык: аргументы писем (имена) подставляются при отправке и не меняются со сменой языка; названия и тексты эр (`gpu.era.*`, `gpu.mail.era.*`) ядро перерисовывает по ключам.
- **Вкладка «Рынок» этапа 2** (`GpuMarketTabModel.Build(content, market, state, game, text, rivals, eras)`; прежняя пятиаргументная перегрузка осталась и тоже даёт имена вендоров из контента). Сверху плашка
  эпохи (название текущей, название и год следующей; только в режиме `eras`). В блоке сегмента: требования года (`GpuNames.Requirements`, один текст с письмом сегмента), строки «Ваша карта «X»: Нет:
  <функция> — рейтинг ×k» (`GpuMarketMarkRow`; `×0` — «рейтинг 0: в сегменте не продаётся»), топ-5 с производителем под именем вендора, доли производителей в этом сегменте за 30 дней (квадрат цвета
  вендора `GpuSwatch`, имя, полоса, процент; самые крупные первыми). Доли по всему рынку выводятся ниже, если сегментов больше одного. Новые методы `GpuMarketService`: `SegmentShares(segmentId, days)`,
  `OwnerShares(days)`, `ShareOf(ownerKey, segmentId, days)` (окно 30 дней в сессии, считается в `SellDay` по `MarketItem.SoldBySegment`, сбрасывается `Reset()`).
  - **Карта игрока играет во всех открытых сегментах** (рынок даёт ей стартовую популярность там, где записи нет), поэтому пометки считаются по всем открытым сегментам, а не по `segIds`; не показываются
    для карты не в продаже, на паузе (`sell = false`) и для карты, ждущей выпуска.
  - «Линейка»: у карты на продаже строка пометок (`GpuProductRow.MarksText`, `GpuProductView.Features`). «Конструктор» уже писал «нет: <функции> (×k)» в таблице оценки по сегментам (TSK-024).
  - `IGpuText` получил `IsRussian` (имена вендоров из контента на языке игрока); `GameGpuText` и `TestGpuText` его реализуют.
  - Обновление окна: `GpuUi.Set` не пишет текст, который не изменился; `GpuBar.SetFraction` не двигает полосу, которая не сдвинулась; цвет надписи перекрашивается только при смене тона.
    Модель вкладки при пяти открытых сегментах (из шести сегментов эр одновременно открыты не больше пяти: офисный закрывается в 2010, ИИ открывается в 2016) строится за ≈0,06 мс на 16 картах (тест
    держит < 2 мс на лучшем из прогонов). Время самих виджетов Unity без игры не измерить — смотреть строку `perf … ui/day` в `Player.log` при включённом подробном журнале.
- Строки: `gpu.mail.*` (сегмент, вендор вход/уход/банкротство/слияние, лидер, подсказка узла), `gpu.market.era*`, `gpu.market.segmentShares`, `gpu.market.mark`, `gpu.mark.*`, `gpu.series.<vendorId>`
  (7 вендоров). Серии вендоров — фирменные названия, одинаковые на обоих языках; `GpuNames.Series` берёт их из таблиц, при отсутствии — `series` из `vendors.json` (тест следит, чтобы они совпадали:
  карты именуются из контента при выпуске).
- Что может проверить только владелец (игру не запускаем): как письма выглядят в почте игры и приходят ли в порядке приоритетов и лимитов ядра; что в новой игре и при загрузке сейва писем о рынке нет; письмо при
  банкротстве Intel и при отсутствии AMD/Intel в партии; вид плашки эпохи, строк долей с квадратиками цвета и пометок «Нет: …» (перенос строк в карточке, цвета на светлой и тёмной теме); время обновления
  окна «Рынок» при пяти сегментах (`perf … ui/day`, бюджет 2 мс).
- Находка для калибровки (TSK-045): письма «Новый лидер» могут идти часто, пока рынок только что засеян: первые запуски вендоров почти в каждом сегменте быстрее фона (фон отстаёт на 2 года). Пауза 3 месяца на сегмент
  ограничивает поток; если на живой партии писем много, поднять `news.leaderCooldownMonths` в `balance.json` (поле уже есть).

## 10. Кусок 9 (TSK-040…TSK-045): синергия, общий опыт техпроцесса, скидка совместной разработки, `GpuApi`, предикаты, тесты этапа 3, баланс-прогон. Справка итоговому интегратору (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1236 КБ), тесты `--tests` зелёные (1846, из них 66 новых: `GpuSynergyMathTests` 8, `GpuSynergyTests` 25,
`GpuCoDevTests` 7, `GpuApiTests` 23, `BalanceReference*` 3). Тринадцать намеренных поломок нового кода: десять поймано сразу, две выжили (передача линий вендора, день без продаж в кольце) и поймали
добавленные проверки, одна равнозначная (поле «активна» вида при выключенной синергии: бонусы там и так 0). Если перед `--tests` запускать обычную сборку фичи, падает
`ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` (свежий DLL одной фичи без чужих ресурсов) — как и в прошлых кусках; к gpu-product-line это не относится.

- Где что лежит (отличия от путей задач): `Logic/Math/GpuSynergyMath.cs`; `Logic/Services/GpuSynergy.cs`, `GpuSharedLearning.cs`, `GpuApiService.cs`, `GpuPredicates.cs` (не `Math/` и `Services/`: так их собирают тесты);
  `Logic/Ui/GpuSynergyBlock.cs`; `GpuConditions.cs` в корне фичи; `Patches/TotalLinesAllocatedPatch.cs`; **`Api/GpuApi.cs`** (не корень фичи: `build_feature.py` копирует зависимым фичам именно `Api/`; тип зависит от `GpuFeature`
  и, как `ArchitectureApi`, без остальной фичи не соберётся — добавлять `gpu-product-line` в `feature_deps.json` чужой фиче можно, только если собирать с ней и остальную фичу). Тесты — `tests/PTMod.Tests/Features/GpuProductLine/`;
  отчёт баланса — `Features/GpuProductLine/balance-report.md` (не `GpuProductLine/balance-report.md`).
- **Для `rival-acquisitions`**: `GpuApi.IsDivisionFounded(ICompany)` и `GpuApi.TransferVendorProducts(ICompany from, ICompany to)` — `public static` в `PTMod.Features.GpuProductLine.GpuApi`, ровно с теми сигнатурами, по которым их ищет
  `GameRelatedFeatures` (по имени, через `MethodInfo`). После полной сборки в `Player.log` должна быть строка `related features: … GpuApi.IsDivisionFounded found, GpuApi.TransferVendorProducts found`. Передача: у `from` должен быть вендор на рынке,
  `to` — игрок; карты переходят на месте (не меняя размер списка продуктов): владелец `player`, статус «в продаже», линии 0, шт./день 0, без «авто», запас, цена, имя, снимок и популярность по сегментам сохраняются; вендор
  `merged` в `player`, его `companyKey` пуст (деньги за карты ему больше не идут). Если подразделение игрока не основано — `GpuDivision.FoundFree()` на дату сделки (с догоняющей разблокировкой узлов); отказ (`false`) ничего не меняет и
  ничего не основывает. Событие `GpuRivals.Merged` не поднимается (поглотителя-вендора нет), письма о вендорах по этой передаче не приходят.
- Новое в `IGpuGame`: `CpuMarketShare(key)` (`MarketDataProvider.MarketShare`, доля CPU за последний день; вызов обёрнут: сбой пишется в лог один раз и даёт 0), `HasCpuOnMarket(key)` (выпущенный, не снят, `SellOnMarket`, цена > 0 — как в
  `GameStateAdapter` ядра), `CpusOf(key)` (год и техпроцесс выпущенных CPU из `ICompanyOwner.GetCpus()`). Самопроверка не менялась (12 членов игры): `MarketShare` и `GetCpus` защищены `try/catch`. `MarketDataProvider.MarketShare`
  пробегает все CPU игры; фича вызывает её раз в день для игрока и каждой привязанной компании ИИ (до трёх вызовов) — тысячи CPU к концу партии дают порядка 0,02 мс; настоящее время покажет `perf … synergy/day` в `Player.log`.
- Синергия (`GpuSynergy`): кольца в секции (`synergy.entries[].cpuShareDaily/gpuShareDaily`), окно `synergy.shareWindowDays` (30, не меньше 30 при загрузке); день пишется на `DayPassed` после рынка (приоритет −10), месяц считается на
  `MonthPassed` последним (−30); `Changed` → `GpuFeature.OnSynergyChanged` → `GpuModifierBridge.InvalidateSynergy`. Записи для компаний, у которых больше нет вендора (ушёл, поглощён, `rivals` выключен), удаляются на месячном тике. Лимит
  колец — 64 компании. `GpuSynergy.Table` читают оба провайдера; конструктор провайдеров в `Register` не менялся.
- GP2: самопроверка и страница хаба показывают `patches 2/2`. `Company.TotalLinesAllocated` — виртуальный метод; патч стоит на `Company`, `AICompany.TotalLinesAllocated` — отдельный метод (в нём GPU не участвуют). Игрок — `ModCompanies.IsPlayer(__instance)`.
- Предикаты `gpu.*` регистрируются в `GpuFeature.Register` (`GpuConditions.Register`, один раз за сессию); в журнал идёт строка `predicates: 4 gpu.* predicates registered`. Имена в коде — `GpuPredicateNames.Prefix + "…"`, потому что тесты ключей
  строк (`GpuL10nKeysTests`, `GpuFeatureSourceTests`) принимают любой литерал `"gpu.xxx"` в коде за ключ текста.
- Скидка совместной разработки не зависит от переключателя `synergy` (FR-17 его не называет); считается при каждом пересчёте конструктора и при старте проекта по списку CPU компании.
- **Баланс (TSK-045).** Подробности и причины — в `balance-report.md`. Менялись только числа контента: `arch.2d`/`arch.gui` `transistorsPerUnit` (450 000 / 750 000), `rivalDieShare`/`rivalBusTier` у `office2d`, `gaming.mid`, `gaming.low`,
  `sizeByDecade` игровых сегментов (1990-е ×2,5, позже ×2,2), `capacityShare` вендоров ×0,6, `background.popularity` 0,8 и `priceFactor` 0,8, `console.unitsShare` 0,15; те же значения стоят в умолчаниях DTO
  (`GpuBalanceDto.BackgroundBlock`, `ConsoleBlock`). Обновлены тесты, сверявшие контент с таблицами design (в комментариях названы старые числа). **Что записать в design.md (вне моих файлов):**
  таблицу архитектур (строки `arch.2d`, `arch.gui`), калибровочный пример 1985 года (`S = 1`, `C = 8`, отношение 0,5, `P = 4,92`; примеры 1997, 2005, 2022 не менялись), числа `balance.json` `background`/`console`,
  таблицы сегментов (размеры, `rival*`) и вендоров (`capacityShare`), а в «Components» — `GpuSynergy`, `GpuSharedLearning`, `GpuApiService`, `IGpuQueries`, `GpuPredicates`, `GpuSynergyBlock`, `Api/GpuApi.cs`.
- Находки: (1) выручка GPU у бота-эталона колеблется с циклом карт (2006 год — 4 %, 2005 — 28 %): когда выпуск нового поколения опаздывает, доля падает; ориентир 1 читается по календарному 2005 году, как в NFR-7;
  (2) бот отстаёт от вендоров на 1–1,5 года (исследование узла кончается через 0,5 года после его года, проект 300–420 дней), поэтому ближе к Matrox и S3, чем к NVIDIA; (3) письма «Новый лидер» — около 2,3 в год за 46 лет,
  приоритет `Low`; (4) 2D-архитектуры раньше считали на транзистор в 8–26 раз больше 3D (эффективная производительность при равных площади и техпроцессе: 1997 год — GUI 1935 против 3D 242, 2000 год — 42 005 против 1 322);
  после правки это 2–3 раза (1997: GUI 475 против 3D 242; 2000: 3 830 против 1 322 и 3 047 у `arch.tnl`) — остаток даёт меньшая нагрузка на память у старых архитектур (`bytesPerOp` 2,0 против 8,0). Карта на старой
  архитектуре с множителем ×0,35 за каждую недостающую функцию уступает карте с нужными функциями; если на живой партии кто-то найдёт обходной путь, следующий рычаг — `bytesPerOp` у `arch.2d`/`arch.gui`.
- Что может проверить только владелец (игру не запускаем): блок «Синергия» во вкладке «Рынок» (подсказки на строках бонусов, перенос строк); что в живой партии после первого 1-го числа с картой и CPU в продаже рейтинг CPU и
  карт действительно растёт (`Explain` канала `market.rating.cpu` называет «Синергия с видеокартами: +N %»); что GP2 применяется (страница хаба: `patches 2/2`) и общий опыт ускоряет освоение техпроцесса; что строка «Совместная
  разработка: −15 %» появляется в конструкторе при CPU года-двух на том же техпроцессе; что `GpuApi.TransferVendorProducts` работает в сделке `rival-acquisitions` (6 карт, подразделение основывается бесплатно, если его нет);
  что предикаты `gpu.*` доступны в JSON сценариев; на реальных числах — как выглядят «Прочие» (дешёвые безымянные карты на 40–50 % рынка в 2000-х) и выручка GPU в финансовом окне при настоящей выручке CPU.

## 11. Кусок 10 (TSK-046, TSK-048, TSK-049): документация, консольные заказы. Справка итоговому интегратору (правок ядра не требует)

Блокирующих запросов нет. Сборка `python mod/build_feature.py gpu-product-line` проходит (PTMod.dll 1268 КБ), тесты `--tests` зелёные (1946, из них 100 новых: `GpuConsoleMathTests` 24, `GpuConsoleOrdersTests` 58,
`GpuReadmeTests` 18). `python install.py --check`: 0 ошибок, 0 предупреждений. Сорок намеренных поломок нового кода (порог расторжения «не меньше» вместо «больше», расторжение до `graceDays`, цена `min`, округление
объёма, порог без доли, поставка без предела запаса, доля недопоставки без недопоставки, окно без обрезки, выручка по статье «Продажи», запас не списывается, нет книг карты, недопоставка не пишется, предложение без
подразделения / при выключенном переключателе / во время перемотки / в любой год, письмо `Normal`, без срока ответа, «Принять» по умолчанию, кнопка всегда активна, расторжение без штрафа, снятие карты без последствий,
штраф за истёкший срок, срок не кончается, «Принять» без карты, вопрос хаба всегда / без суммы штрафа, загрузка не пересчитывает кнопки, доставка письма не пересчитывает кнопку, выключение без штрафа / не закрывает
предложения, не тот сегмент, наценка 1, нет подписки на `ContractSellTick`, нет `ConfirmBeforeDisable`, спрос заказа не входит в спрос карты, планка считает вендоров при выключенных соперниках, порядок блока вкладки,
поле README, год предложения в контенте) — тридцать девять поймано сразу, одна выжила (планка считает карты вендоров при выключенном переключателе `rivals` и карты вне сегмента) и поймана добавленным
`TheBar_IsTheBestCardOnSaleInTheSegment_TheVendorsCountOnlyWhileTheyTakePart`.

- Где что лежит (отличия от путей задач): `Logic/Math/GpuConsoleMath.cs` и `Logic/Services/GpuConsoleOrders.cs` (не `Math/` и `Services/`: так их собирают тесты; в файле службы лежат и `IGpuOrderMail`,
  `GpuDisableConfirmation`, `GpuConsoleOrderView`); `GpuConsoleDto` и блок `balance.console` были с TSK-002/003 (`consoles.json` теперь с шестью записями); вкладка «Линейка» - `GpuConsoleRow`,
  `GpuLineupModel.Build(..., consoleOrders)` (необязательный последний параметр) и `Ui/GpuLineupTab.DrawConsole`; тесты - `tests/PTMod.Tests/Features/GpuProductLine/` (`GpuConsoleMathTests`, `GpuConsoleOrdersTests`,
  `GpuReadmeTests`, не `GpuProductLine/`). `GpuConsoleMath.OrderPrice` принимает четыре числа (`unitCost, segmentBudget, priceFactor, priceFloorMarkup`): наценка 1,15 лежит в балансе, а не в формуле.
- **Новое в интерфейсах фичи**: `IGpuGame.AddReputation(companyKey, delta, reasonKey)` (адаптер: `ModReputation.Add`, ключ причины `gpu.console.failed`; фейк тестов ведёт `Reputation`); `GpuMailLetter` получил необязательные
  `TimeoutDays`, `DefaultChoiceId`, `Choices`, `Actions` (письма новостей их не задают); `IGpuOrderMail : IGpuMail` (`SetChoiceState`, `Refresh`, `Resolve`) реализует `GpuMailAdapter` (там же `ModMail.RegisterChoiceHandler`,
  `RegisterActionHandler` и подписка на `ModMail.Delivered` в `GpuMailAdapter.BindConsoleHandlers`; `ModMail.` по-прежнему вызывает только этот файл); `GpuProduction.SetContractDemand/ContractDemandOf`;
  `GpuMarketService.BestPerfIn(segment, year)`; `GpuFeature.ConsoleOrders`.
- **Как идёт заказ** (решения там, где задача молчит):
  - Предложение уходит в календарный год `offerYear` (в первый день года, когда выполнены условия: подразделение основано, переключатель `consoleOrders` включён, игра в обычном ходу), один раз на запись. Пропущенные
    годы не предлагаются (основали подразделение в 2010-м - предложения 1998-2004 не придут). Нет целевого сегмента (`console.segment`, иначе `fallbackSegment`) - предложение откладывается на следующий день того же года.
  - Подходит карта игрока в продаже (`selling`, `sell`, цена больше нуля), у которой есть **все** функции целевого сегмента на этот год (строго, без прощения `missingFeatureFactor`) и производительность не ниже
    `minPerfShare` лучшей карты в продаже, которая может продаваться в сегменте (карты вендоров считаются, пока включён `rivals`). При приёме берётся лучшая такая карта.
  - Цена и объём фиксируются в момент нажатия «Принять» (в письме - оценка на день отправки: «по цене около ...»). Себестоимость берётся из `GpuProduction.UnitCostOf` (последний день производства); у карты, которую ещё не
    производили, она 0, и цена равна `priceFactor × бюджет`.
  - Поставка идёт со склада карты независимо от флага «Продавать» (можно выключить розницу и работать только на заказ). Объём заказа входит в спрос карты для режима «Авто» (`SetContractDemand`), иначе автокарта
    не наращивала бы линии под заказ и срывала его; без «Авто» и с одной линией заказ сорвётся (тест `AnAutomaticCard_FollowsTheOrder_AndTheOrderIsNotBroken`). Если розница выключена (`sell = false`), спрос карты
    не записывается рынком и «Авто» держит последние линии - замечание для владельца при калибровке.
  - Выключение переключателя `consoleOrders` останавливает только новые предложения; принятые заказы идут своим чередом.
  - Расторжение: срыв (после `graceDays` дней и при доле недопоставки строго выше `failCancelShare`), снятие закреплённой карты, выключение фичи - штраф `reputationPenalty` за каждый заказ; письмо `Normal` о срыве и о
    снятии карты, при выключении фичи писем нет, открытые предложения закрываются `ModMail.Resolve(..., "decline")`. Истёкший срок - письмо `Low`, без штрафа. Вопрос хаба считает штраф как «число заказов × штраф».
  - Кнопка «Принять» пересчитывается: при отправке, при доставке письма (`ModMail.Delivered`), раз в `revalidateDays` дней и на `GameReady` (загрузка, новая партия, включение фичи); ядру отдаётся только изменившееся состояние.
  - Выручка идёт `RevenueOrigin.Contracts` и в книги карты (продано и доход прошлого месяца во вкладке «Линейка»), в канал `contract.reward` не заходит; в розничные счётчики рынка (`SoldLast`, доли) не попадает.
- Строки (ru/en): `gpu.console.*` (клиенты, письмо-предложение, кнопки, причины, письма о срыве / снятии карты / окончании, вопрос хаба, строки блока вкладки и статусов) и `gpu.lineup.retire.console` (предупреждение в
  подтверждении снятия карты). Текст `gpu.console.offer.body` берёт ровно 8 аргументов (предел теста ядра - `{0}`...`{7}`); подсказка переключателя `consoleOrders` уточнена.
- Документация (TSK-046): раздел «Видеокарты» в `README.md` проекта (старт, боковой файл сейва, пять переключателей, режим «Процессоры в приоритете», консольные заказы) и `Content/gpu-product-line/README.md` (все поля
  контента с единицами, правила переопределения, примеры; их запускает `GpuReadmeTests`, включая AC-10.2: бюджет `consumer` 999 виден в окне рынка в режиме `single`). Файл `README.md` в `Content/` в DLL не
  попадает (в ресурсы идут только `*.json`).
- Что записать в design.md (вне моих файлов): путь `Logic/Math/GpuConsoleMath.cs`, четыре аргумента `OrderPrice`, интерфейс `IGpuOrderMail`, `GpuDisableConfirmation`, `GpuProduction.SetContractDemand`,
  `GpuMarketService.BestPerfIn`, `IGpuGame.AddReputation`, `gpu.console.*` в таблице строк, число аргументов письма (8), правило «предложение только в год `offerYear`», приоритет `ConsoleDayPriority = -20` для `DayPassed`.
- Что может проверить только владелец (игру не запускаем): как письмо-предложение выглядит в почте (серая кнопка «Принять» с причиной, кнопка «Открыть линейку», срок ответа 60 дней, «Отказаться» по умолчанию); что кнопка
  становится активной без закрытия письма после выхода подходящей карты и после загрузки сейва; что `ModMail.RegisterChoiceHandler` принят в `Register` (если ядро его отвергнет, `OnEnabled` повторит регистрацию; в
  `Player.log` не должно быть строки о том, что обработчика ответов нет); вопрос хаба при выключении фичи с действующим заказом («Отмена» оставляет фичу включённой); блок «Консольные заказы» во вкладке «Линейка»
  (перенос строк, цвет недопоставки); запись «Срыв консольного заказа» в истории репутации; строки `perf ... console/delivery`, `console/day` в `Player.log` при подробном журнале; что README читается человеком.
- Находки для калибровки: (1) `unitsShare` 0,15 (после TSK-045) даёт на 1998 год от 800 карт в день для `gaming.mid` (больше, когда рынок идёт к следующему десятилетию) - одна линия карты (около 300 в день при кристалле 300 мм²) его не покрывает, нужен режим «Авто» или несколько линий; письмо
  этого не говорит, говорит блок во вкладке «Линейка» (недопоставка за 90 дней); если на живой партии заказы часто срываются сразу, поднять `console.graceDays` или снизить `unitsShare` в `balance.json`.
  (2) Цена заказа по умолчанию (`0,55 × бюджет` сегмента) ниже обычной розничной цены карты (карты продаются около 0,9 бюджета), зато без риска нераспроданного запаса: выручка на карту ниже, объём стабильнее.
