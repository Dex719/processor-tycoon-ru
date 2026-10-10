Спайк 0002 (TSK-006): работает ли Harmony 2.3.6 на Unity 6 Mono
================================================================

Статус: код готов и собран, прогон в игре ждёт владельца. Скрипты игру не запускают; игру запускаете вы.
Спайк идёт после спайка 0001 (mod\spikes\loadspike\readme.txt): он использует ту же установку (tools\spike_install.py)
и ту же регистрацию PTMod.dll, поэтому сначала нужен результат TSK-005 (загрузка работает; нужна ли запись 0Harmony.dll).

Что проверяем
-------------
Решение 0002: фичи мода меняют формулы игры патчами Harmony (0Harmony.dll рядом с PTMod.dll). Спайк ставит десять патчей
S1 из design.md ("Патчи методов", строка S1) всех форм, которые понадобятся ядру, и считает, применились ли они и вызывались ли.
Патчи ничего не меняют в поведении игры: каждый только увеличивает счётчик и запоминает пару фактов.

  Патч (как в design.md)                            Форма                              Когда срабатывает в игре
  P1  Modifiers.GetModifier                         postfix, ref __result              каждый игровой день, много раз
  P5  Market.UpdatePopularities (приватный)         prefix + поле ___cpus              раз в день, если на рынке есть CPU
  P4  Market.CalculateRatings (приватный; в         postfix, только __instance и       12 раз в день (3 прохода x 4 сегмента)
      сигнатуре приватный тип Market.Population)    populationIndex
  P2  Factory.ProduceWafers                         prefix + postfix + finalizer       ежедневно по каждому CPU в производстве
                                                    с общим __state (структура)
  P10 BankruptcyHandler.TickBankruptcies (приватный) transpiler-пустышка                ежедневно (OnDayPassed)
  P8  Contract.CpuOffer.DefineChance (метод          postfix                            только изнутри GetBestOffers
      приватного вложенного типа)
  P23 Contract.GetBestOffers (приватный)            prefix + finalizer                 см. "Что запускает контракты" ниже
  P21 DateController.NextMonth                      prefix + finalizer, __state        кнопка "Следующий месяц"
  P20 Company.AddCpu (виртуальный)                  postfix                            игрок выпускает CPU (и загрузка сейва)
  P20 AICompany.AddCpu (виртуальный)                postfix                            ИИ выпускает CPU (и загрузка сейва)

Transpiler P10 сделан безвредным: он ничего не заменяет, а только (1) считает загрузки локальной V_0 (-DebtLimit) в цикле
TickBankruptcies, которых по design.md ровно 3, и (2) вставляет в начало метода один вызов счётчика. Так видно не только то,
что Harmony переписал метод, но и что переписанный метод выполняется. Это единственный патч, который меняет IL.

Кроме строк в Player.log, спайк пишет один маленький файл ptmod_spike_perf.csv (история замеров перемотки) в папке, где лежит
Player.log, и читает там же файл-маркер ptmod_spike_baseline.txt (если он есть, патчи не ставятся, нужен для замера скорости).
Файлы игры спайк не меняет: патчи живут только в памяти, меняются лишь файлы из спайка 0001 (два JSON и две DLL в Managed).

Что понадобится
---------------
  - результат спайка 0001: загрузка PTMod.dll работает; известно, нужна ли запись 0Harmony.dll (--register-harmony);
  - игра закрыта; русификатор установлен (в папке игры есть _original_en);
  - Python 3 и Roslyn csc (как для обычной сборки мода);
  - около 40 минут: один функциональный прогон и шесть коротких запусков для замера скорости.
  Ниже <ИГРА> - папка, где лежит "Processor Tycoon.exe"; команды выполняются из папки репозитория processor-tycoon-ru.
  PT_LOG - папка Player.log (команды для cmd.exe):
      set "PT_LOG=%USERPROFILE%\AppData\LocalLow\CoffeeHeaven\Processor Tycoon"

Шаг 0. Снять спайк 0001, если он ещё стоит
-------------------------------------------
  python tools\spike_install.py --game-dir "<ИГРА>" --remove
(Спайки 0001 и 0002 называются одинаково, PTMod.dll, и регистрируются одинаково; одновременно стоять они не могут.)

Шаг 1. Собрать DLL спайка (один раз)
------------------------------------
  python mod\build.py --game-dir "<ИГРА>" --src mod\spikes\harmonyspike --out spikes\harmonyspike\PTMod.dll

Результат: mod\build\spikes\harmonyspike\PTMod.dll (около 40 КБ). Это не mod\build\PTMod.dll (настоящая сборка) и не
спайк 0001; скрипт установки отказывается ставить DLL без строки "[PTMod] spike Init".

Шаг 2. Проверить скрипт и пробный прогон (ничего не меняют)
------------------------------------------------------------
  python tools\spike_install.py --self-test
  python tools\spike_install.py --game-dir "<ИГРА>" --install --dll mod\build\spikes\harmonyspike\PTMod.dll --dry-run

Если по итогам спайка 0001 запись для Harmony нужна, добавляйте --register-harmony ко всем командам --install ниже.

Шаг 3. Прогон A: функциональный (патчи включены)
-------------------------------------------------
  python tools\spike_install.py --game-dir "<ИГРА>" --install --dll mod\build\spikes\harmonyspike\PTMod.dll
  del "%PT_LOG%\ptmod_spike_baseline.txt"           (если файла нет, команда просто скажет об этом; он нужен выключенным)

Запустите игру. Новая игра: стартовый год 1985, остальное любое (сложность Normal). Игра сама "догонит" 1971 -> 1985 за один
вызов (экран загрузки): это и есть перемотка, мод замеряет её сам (см. Шаг 5). Дальше играйте:

  1. Пройдите 30 игровых дней (максимальная скорость, пауза не мешает). В логе появится
     "S1 milestone: 30 live game days have passed".
  2. Один раз нажмите "Следующий месяц" (кнопка рядом с кнопками скорости времени). Строка "S1 NextMonth: N game days in X ms".
  3. Выпустите процессор: создайте самый простой CPU (окно создания процессора), дождитесь конца проекта (удобно ускорять
     "Следующий месяц") и в окне релиза задайте цену и нажмите "Выпустить". Это Company.AddCpu (P20). ИИ-компании выпускают
     свои CPU сами, AICompany.AddCpu (P20) уже срабатывает во время стартовой перемотки.
  4. Контракты (P23 и P8), см. "Что запускает контракты" ниже. Если после шагов 1-3 их счётчики ещё 0, нажимайте "Следующий
     месяц" до 1 января 1986 (около 12 нажатий): в январе у контрактов наступает дата принятия.
  5. Как только все десять патчей вызваны, мод пишет "S1 milestone: all 10 patches were called" и таблицу "S1 table".
  6. Выйдите из игры кнопкой выхода (не убивайте процесс): при выходе пишется итоговая таблица "S1 FINAL".

Сохраните все строки спайка в файл и покажите итог:
  findstr /C:"[PTMod] spike" "%PT_LOG%\Player.log" > spike_run_A.txt
  findstr /C:"spike S1 FINAL" "%PT_LOG%\Player.log"
  python tools\spike_install.py --game-dir "<ИГРА>" --status
(--status печатает только первые 60 строк спайка и общий вердикт по загрузке; полный набор строк - в spike_run_A.txt.)
Player.log перезаписывается при каждом запуске (прошлый - Player-prev.log): сохраняйте вывод после каждого прогона.

Что запускает контракты (P23 Contract.GetBestOffers и P8 DefineChance)
-----------------------------------------------------------------------
GetBestOffers приватный и вызывается из двух мест, DefineChance - только из него (счётчик inScope должен быть равен calls):
  - Contract.ChooseCpu: когда у контракта наступает дата принятия (январь года, следующего за годом создания контракта) и у
    него есть хотя бы одно предложение. Контракты создаются 1 января каждого года. На стартовой перемотке до 1985 это уже
    происходит каждый январь, поэтому calls у P23 может быть больше 0 ещё до первого живого дня (live=0);
  - Contract.ChanceToSign: окно контрактов считает вероятность подписания, когда выбран контракт, у которого уже есть
    предложения, и выбран подходящий CPU игрока.
Если нужно вызвать их вручную: нужен выпущенный CPU, подходящий контракту (по производительности, энергопотреблению и корпусу).
Откройте окно контрактов, выберите контракт, выберите свой CPU и отправьте предложение. Затем выберите тот же контракт и свой CPU
ещё раз: окно покажет вероятность подписания (ChanceToSign -> GetBestOffers -> DefineChance). Либо оставьте предложение и дождитесь
1 января следующего года ("Следующий месяц"): у контрактов с предложениями вызывается ChooseCpu.
Если у P23 после года игры calls = 0, пришлите это как результат: патч применён, но вызова не было, значит предложений не было
(для ядра это тоже сведения: проверка P23 на живой игре потребует отправленного предложения).

Что должно быть в логе при успехе (значения примерные)
------------------------------------------------------
  [PTMod] spike Init: harmony spike, unity=6000.0.65f1, mono=4.0.30319.42000        <- ровно одна такая строка
  [PTMod] spike S1 mode: patched (baseline marker not present: ...\ptmod_spike_baseline.txt; perf history: ...)
  [PTMod] spike harmony: loaded 2.3.6.0 from "...\Managed\0Harmony.dll", type HarmonyLib.Harmony
  [PTMod] spike S1 apply: P1 Modifiers.GetModifier [postfix] OK, public static ProcessorTycoon.CompanySystem.Modifiers, 391.7 ms
  ... (ещё девять строк "OK")
  [PTMod] spike S1 transpiler: TickBankruptcies has 80 instructions, local 0: 3 loads (3 in the loop at 9..73, 1 backward branches), 1 stores
  [PTMod] spike S1 apply: done, 10/10 patches applied, Harmony reports 10 patched methods, 466.3 ms in total
  [PTMod] spike S1 perf: fast-forward done, start year 1985, 5114 game days, total ... ms (... ms/day), window 1975-1985 ... ms, mode patched
  [PTMod] spike S1 NextMonth: 31 game days in 640 ms, now 1985-02-01, no exception
  [PTMod] spike S1 FINAL summary (reason=quit, mode=patched, liveDays=44, harmony=2.3.6.0):
  [PTMod] spike S1 FINAL 01/10 P1  Modifiers.GetModifier  postfix  applied=yes calls=... live=... nullCompany=... typesMask=0x7F ...
  ... (по строке на каждый патч)
  [PTMod] spike S1 FINAL result: PASS (applied 10/10, called 10/10, ldloc.0 in loop = 3 (total 3, expected 3), faults 0)

Как читать строку патча: calls - все вызовы с начала игры (включая стартовую перемотку), live - только после неё. Дополнительные
счётчики: P1 typesMask (какие типы модификаторов встретились, ожидается 0x7F) и oddResult (результат не число или <= 0, ожидается 0);
P5 cpuSum/cpuMax (поле ___cpus прочиталось), noField=0; P4 segmentMask (ожидается 0xF); P2 postfix/finalizer (должны сходиться с calls),
stateLost=0 (__state дошёл до postfix и finalizer целым); P10 ldloc0InLoop=3; P8 inScope/outOfScope; P23 finalizer, maxDepth;
P21 days/ms; P20 liveRelease/loading/fastForward/distinctCpus (живые выпуски отдельно от загрузки сейва и перемотки; distinctCpus
меньше calls значит, что один CPU передавался несколько раз - ядро это учтёт).

Критерии успеха (acceptance TSK-006)
------------------------------------
  а) десять строк "S1 apply: ... OK" и "10/10 patches applied";
  б) в "S1 FINAL" у каждого патча applied=yes и calls > 0, в том числе P5 (Market.UpdatePopularities), P23 (GetBestOffers),
     P4 (CalculateRatings), P8 (DefineChance), P20 (Company.AddCpu) и P20ai (AICompany.AddCpu);
  в) P10: ldloc0InLoop=3 (три загрузки V_0 в цикле), calls > 0 (переписанный метод выполняется);
  г) faults=0 у всех, stateLost=0;
  д) итоговая строка "S1 FINAL result: PASS";
  е) перемотка 1975 -> 1985 с патчами не медленнее чем на 5 % (Шаг 5): "S1 perf compare ... -> OK".
Статус INCOMPLETE в итоговой строке означает "ничего не сломано, но не всё вызвано": доиграйте до нужного (см. таблицу выше).
Статус FAIL означает проблему: в строке перечислены патчи, которые не применились или упали.

Шаг 4. Если что-то не так
-------------------------
  - "harmony: NOT loaded": Mono не нашёл 0Harmony.dll сам. Это прогон C спайка 0001: --remove, затем --install ... --register-harmony,
    снова запуск и --status. Если и так NOT loaded, пришлите текст исключения из строки.
  - "S1 apply: ... FAILED: ...": патч не применился. Пришлите строку целиком (там исключение Harmony с подробностями) - это
    главный результат спайка. Остальные патчи ставятся независимо, поэтому остальные строки тоже интересны. Варианты по design.md:
    фолбэк A (Harmony 2.2.2; пакет нужно скачать, нужно отдельное одобрение), фолбэк B (TSK-008, вставка стабов), фолбэк C (только события).
  - patched, но P8 DefineChance calls = 0 при P23 calls > 0: JIT встроил короткий метод в вызывающий, патч не срабатывает. Для ядра это
    важный результат (P8 тогда надо делать иначе, например патчем самого GetBestOffers); пришлите таблицу.
  - игра не запускается или вылетает: --remove, потом пришлите последние 100 строк Player.log (и Player-prev.log).
  - игра идёт заметно хуже, чем без спайка, или зависает на первом дне: --remove и пришлите лог (подозрение - переписанный TickBankruptcies).
  - счётчик 0, хотя игра шла долго: покажите всю таблицу "S1 FINAL", ничего не меняя.

Шаг 5. Замер перемотки (допуск +5 %)
------------------------------------
Что меряем. Игра начинается с 1 января 1971 и при старте новой игры за один вызов (DateController.SimulateUntilYear) проигрывает все
дни до выбранного стартового года; это самая тяжёлая последовательность дней, какую делает игра. Спайк замеряет время такого вызова
по часам процесса и отдельно время между 1 января 1975 и 1 января 1985 внутри него ("window 1975-1985"). Для окна нужен стартовый
год 1985 или больше; во всех прогонах берите один и тот же год (рекомендуется 1985). Сравниваются медианы: патчи включены (patched) и
патчи выключены (baseline: спайк работает, таймер работает, но ничего не патчится). Допуск: patched не медленнее baseline больше чем на 5 %.

Правила: один запуск игры - одно измерение (повторная новая игра в том же запуске быстрее из-за прогретого JIT); закройте тяжёлые
программы; чередуйте режимы; нужно по 3 измерения каждого режима.

  Включить режим baseline:   type nul > "%PT_LOG%\ptmod_spike_baseline.txt"        (PowerShell: ni "$env:USERPROFILE\AppData\LocalLow\CoffeeHeaven\Processor Tycoon\ptmod_spike_baseline.txt")
  Выключить (patched):       del "%PT_LOG%\ptmod_spike_baseline.txt"
  Точный путь печатается в строке "S1 mode: ... baseline marker ...: <путь>" при старте игры.

Порядок запусков: baseline, patched, baseline, patched, baseline, patched. Прогон A (Шаг 3), если в нём был стартовый год 1985, - первый
patched. Для каждого запуска: переключить маркер, запустить игру, Новая игра, стартовый год 1985, дождаться конца экрана загрузки,
выйти из игры. После каждого запуска:
  findstr /C:"spike S1 perf" "%PT_LOG%\Player.log"
Строка "S1 perf: fast-forward done, ... mode baseline|patched" - одно измерение; "S1 perf per-year ms" - по годам. После шестого запуска
последняя строка "S1 perf compare ... -> OK" (или OVER_LIMIT) - итог. Все измерения накапливаются в
  %PT_LOG%\ptmod_spike_perf.csv       (строки вида mode;startYear;days;totalMs;windowMs;stamp)
приложите этот файл к результатам. Пока измерений меньше трёх на режим, итог "PROVISIONAL"; если разброс между одинаковыми
режимами сам больше 5 %, повторите несколько запусков, медиана последних пяти сглаживает выбросы.

Шаг 6. Снять спайк и проверить, что игра ванильная
---------------------------------------------------
  del "%PT_LOG%\ptmod_spike_baseline.txt"
  python tools\spike_install.py --game-dir "<ИГРА>" --remove

Скрипт напишет, совпали ли оба JSON с оригиналами из _original_en. Запустите игру в последний раз: в Player.log не должно быть
ни одной строки "[PTMod]" (--status это покажет). Файл ptmod_spike_perf.csv можно оставить или удалить.

Что записать после прогонов
---------------------------
В .kiro\decisions\0002-harmony-for-method-patches.md есть раздел Evidence (пока пустой, "ждёт владельца"). Заполните его (или
перешлите материалы, и это сделаем мы):
  1. spike_run_A.txt (или хотя бы строки "S1 apply", "S1 transpiler", "S1 FINAL", "S1 NextMonth", "S1 perf");
  2. ptmod_spike_perf.csv и последнюю строку "S1 perf compare";
  3. вердикт: все десять патчей применены и вызваны / какие нет; перемотка в допуске / нет;
  4. версия Unity и Mono из строки "Init", путь, откуда загружен 0Harmony.dll, нужна ли была запись для него;
  5. Status решения 0002: оставить accepted (всё прошло) или перейти к фолбэку (A, TSK-008, C).
После этого интегратор отметит TSK-006 в tasks.md. TSK-008 (фолбэк без Harmony) условный: он включается только при провале.
