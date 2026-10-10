Спайк 0001 (TSK-005): загружает ли игра PTMod.dll
==================================================

Статус: код и скрипты готовы, прогон в игре ждёт владельца. Скрипты игру не запускают; игру запускаете вы.

Что проверяем
-------------
Решение 0001: мод лежит в Processor Tycoon_Data\Managed\PTMod.dll и подключается двумя записями в JSON Unity.
Спайк отвечает на три вопроса:

  1. Загружает ли Unity 6 (Mono) такую DLL и вызывает ли PTMod.Core.ModBootstrap.Init до загрузки первой сцены (level0)?
  2. Создаётся ли из этой DLL объект PTModHost (MonoBehaviour, DontDestroyOnLoad) и живёт ли он при смене сцен?
  3. Нужна ли отдельная запись для 0Harmony.dll в ScriptingAssemblies.json, или Mono находит её в Managed сама?

Спайк ничего не патчит и ничего не пишет, кроме строк "[PTMod] spike ..." в Player.log. В игре меняются только:
  - два файла в Managed: PTMod.dll (спайк) и 0Harmony.dll;
  - две записи в ScriptingAssemblies.json и RuntimeInitializeOnLoads.json (перед правкой оригиналы сохраняются
    в _original_en\). data.unity3d и Assembly-CSharp.dll не трогаются.
Снятие (--remove) возвращает JSON к исходным байтам и удаляет оба файла.

Что понадобится
---------------
  - игра закрыта; русификатор установлен (в папке игры есть _original_en);
  - Python 3 и Roslyn csc из Visual Studio 2022 Build Tools (как для обычной сборки мода);
  - ~10 минут. Ниже <ИГРА> - папка, где лежит "Processor Tycoon.exe"; команды выполняются из папки репозитория
    processor-tycoon-ru.

Шаг 0. Собрать DLL спайка (один раз)
------------------------------------
  python mod\build.py --game-dir "<ИГРА>" --src mod\spikes\loadspike --out spikes\loadspike\PTMod.dll

Результат: mod\build\spikes\loadspike\PTMod.dll. (Рядом лежит обычная сборка мода PTMod.dll - это другой файл, его для
спайка брать нельзя; скрипт установки это проверяет.)

Шаг 1. Проверить сам скрипт и пробный прогон (ничего не меняют)
----------------------------------------------------------------
  python tools\spike_install.py --self-test
  python tools\spike_install.py --game-dir "<ИГРА>" --install --dry-run

Первая команда гоняет установку и снятие на временной копии структуры игры. Вторая показывает план и проверяет, что оба
JSON читаются. Если что-то из этого не прошло, дальше не идите и пришлите вывод.

Шаг 2. Прогон A: без записи для Harmony
---------------------------------------
  python tools\spike_install.py --game-dir "<ИГРА>" --install

Запустите игру, дождитесь главного меню, один раз начните новую игру (или загрузите сейв), чтобы сменилась сцена, и закройте
игру кнопкой выхода (не убивайте процесс). Затем:

  python tools\spike_install.py --game-dir "<ИГРА>" --status

--status читает Player.log и печатает все строки "[PTMod] spike ..." и предварительный вердикт. Сам лог лежит здесь:
  %USERPROFILE%\AppData\LocalLow\CoffeeHeaven\Processor Tycoon\Player.log
Player.log перезаписывается при каждом запуске (прошлый запуск - Player-prev.log), поэтому после каждого прогона
сделайте --status и сохраните вывод (или скопируйте Player.log).

Пока спайк установлен, скопируйте для Evidence хвост обоих JSON (там видны наши записи):
  python -c "print(open(r'<ИГРА>\Processor Tycoon_Data\ScriptingAssemblies.json', encoding='utf-8').read()[-250:])"
  python -c "print(open(r'<ИГРА>\Processor Tycoon_Data\RuntimeInitializeOnLoads.json', encoding='utf-8').read()[-330:])"

Что должно быть в логе при успехе (значения в "..." примерные):
  [PTMod] spike Init: unity=6000.0.65f1, mono=4.0.30319.42000     <- ровно одна такая строка
  [PTMod] spike info: runtime=..., assembly=PTMod, ..., sceneCount=0, ...   <- сцена ещё не загружена
  [PTMod] spike harmony: loaded 2.3.6.0 from "...\Managed\0Harmony.dll", instance created=True   (или NOT loaded - см. прогон C)
  [PTMod] spike host: Awake, frame=0 ...
  [PTMod] spike host: created=True, awakeCalls=1
  [PTMod] spike host: Start / first Update ...
  [PTMod] spike scene loaded: buildIndex=0, name=level0, ...      <- ПОСЛЕ строки Init
  [PTMod] spike scene loaded: buildIndex=1, ...                   <- после старта игры; hostFrames растёт
  [PTMod] spike host: quit, frames=..., sceneLoadsSeen=...        <- при закрытии игры

Критерии успеха (acceptance TSK-005):
  а) строка "spike Init" есть и стоит раньше "scene loaded: buildIndex=0";
  б) PTModHost создан (host: created=True) и не уничтожен до выхода (строка host: destroyed идёт после host: quit);
  в) в логе нет "Init failed" и "creation failed".

Шаг 3. Прогон B: повторный запуск
---------------------------------
Ничего не меняя, запустите игру ещё раз, дойдите до меню, закройте, снова выполните --status.
Критерий: опять ровно одна строка "spike Init" (Init не вызывается дважды; если Unity вызвал его повторно, это будет
видно по строке "spike repeat: Init called again").

Шаг 4. Прогон C: только если в A было "harmony: NOT loaded"
-----------------------------------------------------------
  python tools\spike_install.py --game-dir "<ИГРА>" --remove
  python tools\spike_install.py --game-dir "<ИГРА>" --install --register-harmony
Запустите игру, закройте, --status. Если теперь "harmony: loaded", установщику мода (TSK-044) нужна запись 0Harmony.dll
в ScriptingAssemblies.json (константа HARMONY_ASSEMBLY в ptru\mods.py); если по-прежнему NOT loaded - пришлите текст
исключения из строки: от этого зависит TSK-006.

Шаг 5. Снять спайк и проверить, что игра ванильная
--------------------------------------------------
  python tools\spike_install.py --game-dir "<ИГРА>" --remove

Скрипт напишет, совпали ли оба JSON с оригиналами из _original_en. Запустите игру в последний раз: в Player.log не должно
быть ни одной строки "[PTMod]" (--status это покажет), игра работает как обычно.

Если что-то пошло не так
------------------------
  - Игра не запускается или вылетает после установки: сразу выполните --remove. Если скрипт недоступен, вручную:
    верните из _original_en\ файлы ScriptingAssemblies.json и RuntimeInitializeOnLoads.json в Processor Tycoon_Data\,
    удалите Processor Tycoon_Data\Managed\PTMod.dll и 0Harmony.dll. Это полностью откатывает спайк.
  - Строк "[PTMod] spike" нет вообще: проверьте --status (PTMod.dll в Managed и обе записи должны быть "да") и дату
    Player.log (он должен быть свежим). Если всё на месте, а строки нет, Unity не подхватывает вручную добавленную запись:
    это провал спайка, переходим к фолбэку TSK-007 (IL-хук в GameManager.Awake). Пришлите --status и фрагмент
    Player.log вокруг строк с "Exception" и "PTMod".
  - Init есть, но "host: created=False" или "creation failed": DLL загружена, а компонент из неё не создаётся.
    Это не провал загрузки, но проект PTModHost придётся пересмотреть; пришлите текст ошибки.
  - "Игра запущена" при установке или снятии: закройте игру (в диспетчере задач не должно быть Processor Tycoon.exe).

Что записать после прогонов
---------------------------
В .kiro\decisions\0001-mod-loading-via-scripting-assemblies.md есть раздел Evidence (пока пустой, "ждёт владельца").
Заполните его (или перешлите материалы, и это сделаем мы):
  1. вывод --status после прогонов A, B (и C);
  2. хвосты обоих JSON с нашими записями (Шаг 2);
  3. вердикт: загрузка работает / не работает; нужна ли запись для 0Harmony.dll;
  4. обновите Status решения: accepted (если всё прошло) или "переход к TSK-007".
После этого интегратор отметит TSK-005 в tasks.md.
