# 0002. Harmony For Method Patches

## Status
accepted

## Context
Большинство фич меняет формулы или поведение методов игры: `Modifiers.GetModifier`, `Market.CalculateRatings`, `BankruptcyHandler.TriggerDefault` и т. п. Подписки на события `DateController`/`SaveHandler` покрывают только часть потребностей. Нужен механизм prefix/postfix/transpiler в рантайме.

## Decision
Использовать Harmony 2 (`0Harmony.dll`, лицензия MIT), положив его рядом с `PTMod.dll` и зарегистрировав так же (решение 0001). Все патчи объявляются атрибутами в классах фич и применяются `ModHooks` поштучно, с изоляцией ошибок.

Загрузка `0Harmony.dll` (NuGet `Lib.Harmony` 2.3.x) одобрена владельцем 2026-10-10. Файл лежит в `mod/lib/` (вне git), в релизах распространяется с лицензией MIT.

Отклонённые альтернативы:
- Свой IL-инжектор на Python (расширение `ptru/patch_dll.py`): без зависимостей, но каждый хук — ручная работа с метаданными, нет transpiler'ов, долго.
- Только события без патчей: не покрывает формулы рынка, банкротства, контрактов.

## Consequences
- Плюс: стандарт де-факто для модов Unity Mono, быстрые итерации.
- Минус: внешняя зависимость. Работу на Unity 6 Mono нужно подтвердить спайком.
- Патчи на один метод из разных фич упорядочиваются через приоритеты Harmony. Реестр в `mod-framework` ловит конфликты.

## Процедура спайка (TSK-006)

Код спайка готов и собран (2026-10-10), прогон в игре делает владелец. Полная пошаговая инструкция со всеми командами, примерами строк лога и разбором неудач: `mod/spikes/harmonyspike/readme.txt`. Спайк идёт после спайка 0001 (TSK-005): он использует ту же установку (`tools/spike_install.py`) и ту же регистрацию `PTMod.dll`. Кратко (`<ИГРА>` — папка с `Processor Tycoon.exe`, игра закрыта, команды из корня репозитория, `PT_LOG` = `%USERPROFILE%\AppData\LocalLow\CoffeeHeaven\Processor Tycoon`):

1. Снять спайк 0001, если он стоит: `python tools\spike_install.py --game-dir "<ИГРА>" --remove`.
2. Сборка: `python mod\build.py --game-dir "<ИГРА>" --src mod\spikes\harmonyspike --out spikes\harmonyspike\PTMod.dll` → `mod\build\spikes\harmonyspike\PTMod.dll`. Проверка без игры: `python tools\spike_install.py --self-test` и `… --install --dll mod\build\spikes\harmonyspike\PTMod.dll --dry-run`. Если по итогам 0001 нужна запись для Harmony, ко всем `--install` добавляется `--register-harmony`.
3. **Прогон A (функциональный, патчи включены)**: `--install --dll mod\build\spikes\harmonyspike\PTMod.dll`, убедиться, что нет файла `%PT_LOG%\ptmod_spike_baseline.txt`. Запуск игры → новая игра, стартовый год **1985** → пройти **30 игровых дней** → один раз нажать **«Следующий месяц»** → **выпустить CPU игроком** (ИИ выпускает свои сам, в том числе во время стартовой перемотки) → вызвать контракты (`Contract.GetBestOffers`: окно контрактов с отправленным предложением или 1 января следующего года) → выйти кнопкой игры. Сохранить: `findstr /C:"[PTMod] spike" "%PT_LOG%\Player.log" > spike_run_A.txt`.
4. Что считается успехом: десять строк `S1 apply: … OK`; в итоговой таблице `S1 FINAL` у каждого патча `applied=yes` и `calls > 0` (в том числе приватные `Market.UpdatePopularities` и `Contract.GetBestOffers`, `Market.CalculateRatings` с приватным вложенным типом в сигнатуре, `Contract.CpuOffer.DefineChance`, виртуальные `Company.AddCpu` и `AICompany.AddCpu`); transpiler на `BankruptcyHandler.TickBankruptcies` насчитал 3 загрузки локальной `V_0` в цикле (`ldloc0InLoop=3`) и вставленный им вызов выполняется; `faults=0`; итоговая строка `S1 FINAL result: PASS`.
5. **Замер перемотки (допуск +5 %)**: игра при старте новой игры за один вызов `DateController.SimulateUntilYear` проигрывает дни с 1 января 1971 до стартового года; спайк замеряет время вызова по часам процесса и отдельно окно между 1 января 1975 и 1 января 1985 внутри него (нужен стартовый год 1985 и выше, во всех запусках один и тот же). Режимы: `patched` (по умолчанию) и `baseline` (есть файл `%PT_LOG%\ptmod_spike_baseline.txt`: таймер работает, патчи не ставятся). Шесть запусков по очереди (baseline, patched, baseline, patched, baseline, patched; один запуск игры — одно измерение): новая игра со стартовым годом 1985, дождаться конца экрана загрузки, выйти. Замеры копятся в `%PT_LOG%\ptmod_spike_perf.csv`, итог печатает мод: `S1 perf compare … -> OK` (медиана patched не больше +5 % к медиане baseline) или `OVER_LIMIT`.
6. Снятие: удалить маркер baseline, `--remove`, запуск игры (в `Player.log` нет `[PTMod]`).

Что делает спайк: `ModBootstrap.Init` (`PTMod.Core`, вызывается из `RuntimeInitializeOnLoads.json`) пишет `[PTMod] spike Init: harmony spike, unity=…, mono=…`, строку `info`, режим (`S1 mode: patched|baseline`), пробу Harmony (`Assembly.Load("0Harmony")`) и, если Harmony найден и режим не baseline, применяет десять патчей S1 одним вызовом `SpikePatcher.ApplyAll` (патчи ставятся императивно через `Harmony.Patch`, цели разрешаются `AccessTools.DeclaredMethod`/`AccessTools.Inner`, как будет делать `HarmonyBackend`; сбой одного патча не мешает остальным; каждая строка `S1 apply:` содержит `OK` или `FAILED` с исключением). Затем создаёт `PTModHost`, который при загрузке сцены 1 подписывается на события нового `DateController` (живые дни, таймер перемотки), раз в минуту пишет короткую строку прогресса (`S1 progress: P1=… P5=… …`), по достижении 30 живых дней, окончания стартовой перемотки и момента «все десять вызваны» пишет таблицу `S1 table`, а при выходе — итоговую `S1 FINAL`. Все тела патчей обёрнуты в try/catch (сбой считается в `faults` и логируется, в игру не пробрасывается), prefix'ы ничего не возвращают (оригинал выполняется всегда), finalizer'ы возвращают полученное исключение (спайк ничего не проглатывает).

Что не покрывает офлайн-проверка (см. Evidence): рантайм Unity 6 Mono, инлайнинг мелких методов JIT'ом (в первую очередь `DefineChance`), реальные игровые вызовы `Market.CalculateRatings`, `Company.AddCpu`, `AICompany.AddCpu`, время применения патчей на Mono и цену патчей на горячих методах. Это и есть содержание прогона A и замера.

Если спайк провален: фолбэк A (Harmony 2.2.2, нужен отдельный запрос на загрузку пакета), фолбэк B (TSK-008, `StaticStubBackend`), фолбэк C (режим «только события») — см. «Спайки и фолбэки» в `design.md`. TSK-008 условный и не планируется до результата.

## Evidence

**Статус: ждёт владельца.** Раздел заполняется после прогонов; до этого статус решения остаётся accepted, TSK-006 не закрыта.

### Известно до прогона (без запуска игры, 2026-10-10)

- **Цели в английской `Assembly-CSharp.dll`** (сверено по декомпиляции из `pt-modding/code` и применением патчей, см. ниже): `Modifiers.GetModifier(ModifierType, ICompany)` — public static (`ProcessorTycoon.CompanySystem`); `Market.UpdatePopularities()` — private, поле `cpus` типа `List<Cpu>` (`ProcessorTycoon.MarketSystem`); `Market.CalculateRatings(Market.Population, int, bool)` — private, `Market.Population` — приватный вложенный класс; `Factory.ProduceWafers(Cpu, bool, bool, int, float)` — public (`ProcessorTycoon.Production`); `BankruptcyHandler.TickBankruptcies()` — private (`ProcessorTycoon.Bank`); `Contract.GetBestOffers(List<int>)` — private, возвращает `List<Contract.CpuOffer>`, `Contract.CpuOffer` — приватный вложенный класс, `DefineChance(float)` — public в нём (`ProcessorTycoon.ContractSystem`); `DateController.NextMonth()` — public (`ProcessorTycoon.TimeSystem`); `Company.AddCpu(Cpu)` и `AICompany.AddCpu(Cpu)` — public virtual, объявлены в двух независимых классах (`AICompany` не наследует `Company`).
- **Кто вызывает**: `GetBestOffers` — из `Contract.ChooseCpu` (в `OnTick` контракта, когда наступает дата принятия: январь года после создания; при пустых предложениях метод не вызывается) и из `Contract.ChanceToSign` (подсчёт вероятности в окне контрактов, тоже только при непустых предложениях); `DefineChance` — только из `GetBestOffers`; `NextMonth` — из `NextMonthButton`; `Company.AddCpu` — `ProjectReleaseWindow.ReleaseCpu` и загрузка сейва, `AICompany.AddCpu` — ИИ и загрузка сейва. Поэтому спайк делит вызовы `AddCpu` на живые, при загрузке и при перемотке.
- **Старт новой игры**: `DateController.Awake` ставит дату 1971-01-01, `Player.InitializeAsNewGame` вызывает `DateController.SimulateUntilYear(стартовый год)`; стартовые годы меню: 1975 + 5·k. Поэтому «перемотка 1975→1985» замеряется внутри перемотки при стартовом годе 1985 как время между событиями `OnYearPassed` 1 января 1975 и 1 января 1985.
- **`0Harmony.dll` 2.3.6.0** ссылается только на `mscorlib`, `System`, `System.Core`: дополнительных DLL в `Managed` не требует.
- **Офлайн-проверка** (консольная программа под .NET Framework 4.8, не Unity Mono; в репозиторий не входит; игра не запускалась, файлы игры только читались): все десять патчей спайка применяются к настоящей `_original_en\Assembly-CSharp.dll` (10/10, порядка 0,5 с вместе с первым JIT); сигнатуры патчей, `ref __result`, инъекция приватного поля `___cpus`, `__state`-структуры, finalizer'ы, `object __instance` для приватного вложенного типа, `AccessTools.Inner` проходят проверку Harmony. **Transpiler на настоящем IL `BankruptcyHandler.TickBankruptcies`: 80 инструкций, локальная 0: 3 загрузки, все 3 в цикле (инструкции 9–73), 1 запись, 1 обратный переход — совпадает с ожиданием P10 (ровно 3).** Патчи выполнены на неинициализированных объектах игры: P1 (7 типов модификаторов, `typesMask=0x7F`), P8 (оригинал сохраняет результат, `Chance=50`), P5 (поле `___cpus` подставлено), P2 (prefix → postfix → finalizer, `__state` цел), P23 (полный прогон на пустом списке), P21 (исключение оригинала проходит через finalizer без изменений), P10 (вставленный вызов выполняется раньше падения оригинала). Не выполнялись (оригиналу нужна работающая игра): `Market.CalculateRatings` и обе `AddCpu` — для них проверено только применение. Арифметика итога (`PASS/INCOMPLETE/FAIL`, сравнение baseline и patched с медианой и допуском +5 %, разбор истории замеров) проверена на синтетических данных. Это не заменяет прогон в игре.

### Прогон A (функциональный, патчи включены)

- Дата, версия игры: ждёт владельца.
- Unity и Mono (строка `Init`), откуда загружен `0Harmony.dll`, нужна ли запись для него: ждёт владельца.
- Строки `S1 apply:` (десять, время применения на Mono): ждёт владельца.
- Таблица `S1 FINAL` (счётчики `calls`/`live` и дополнительные): ждёт владельца.
- Transpiler P10: `ldloc0InLoop` и `calls` (вставленный вызов выполняется): ждёт владельца.
- Строка `S1 NextMonth`: ждёт владельца.
- P8 `DefineChance` относительно P23: `inScope` равен `calls`, нет ли инлайнинга: ждёт владельца.
- P20: `liveRelease`/`loading`/`fastForward`/`distinctCpus` для `Company.AddCpu` и `AICompany.AddCpu`: ждёт владельца.
- Итоговая строка `S1 FINAL result`: ждёт владельца.

### Замер перемотки (допуск +5 %)

- Таблица шести замеров (baseline/patched, окно 1975–1985, полная перемотка, `ms/day`), файл `ptmod_spike_perf.csv`: ждёт владельца.
- Строка `S1 perf compare`: ждёт владельца.

### Снятие

- `--remove`, JSON совпали с оригиналами, игра ванильная (нет строк `[PTMod]`): ждёт владельца.

### Вердикт

- Все десять патчей S1 применены и вызваны на Unity 6 Mono: ждёт владельца (да / нет → фолбэк).
- Перемотка 1975→1985 с патчами не медленнее чем на 5 %: ждёт владельца (да / нет).
- Статус решения после прогона: ждёт владельца (accepted / фолбэк A, TSK-008 или C).

## Links
- `.kiro/specs/mod-framework/`
- `.kiro/decisions/0001-mod-loading-via-scripting-assemblies.md`
- `mod/spikes/harmonyspike/` — код спайка и `readme.txt` для владельца
- `tools/spike_install.py` — установка и снятие (общая со спайком 0001)
