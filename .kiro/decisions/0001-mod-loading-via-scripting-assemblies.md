# 0001. Mod Loading Via Scripting Assemblies

## Status
accepted

## Context
Моды должны жить отдельно от `Assembly-CSharp.dll`: русификатор уже патчит её строки, а пересобирать её IL ради каждой фичи хрупко. Игра собрана на Mono. В `Processor Tycoon_Data/` лежат `ScriptingAssemblies.json` (список сборок; у пользовательских, например `Assembly-CSharp.dll`, `"types": 16`) и `RuntimeInitializeOnLoads.json` (методы `[RuntimeInitializeOnLoadMethod]`: `assemblyName`, `nameSpace`, `className`, `methodName`, `loadTypes`).

## Decision
`PTMod.dll` кладётся в `Processor Tycoon_Data/Managed/`. Установщик добавляет `"PTMod.dll"` с типом 16 в `ScriptingAssemblies.json`, а в `RuntimeInitializeOnLoads.json` — запись `{"assemblyName":"PTMod","nameSpace":"PTMod.Core","className":"ModBootstrap","methodName":"Init","loadTypes":1,"isUnityClass":false}` (BeforeSceneLoad). Оба JSON бэкапятся вместе с остальными оригиналами в `_original_en/`, `--restore` их возвращает.

Отклонённые альтернативы:
- BepInEx 5: отдельный загрузчик и прокси-DLL, лишняя загрузка и сложность установки.
- Внедрение вызова в IL `Assembly-CSharp.dll`: хрупко, конфликтует с патчером строк.

## Consequences
- Плюс: ни одного изменения в коде игры ради загрузки, удаление мода = откат двух JSON и удаление DLL.
- Минус: поведение не документировано Unity. Нужен спайк (TSK в `mod-framework`): DLL грузится, `Init` вызывается.
- Если спайк провалится — fallback: однострочный IL-хук в `GameManager.Awake` нашим патчером.

## Процедура спайка (TSK-005)

Код и скрипты спайка готовы (2026-10-10), прогон в игре делает владелец. Полная пошаговая инструкция со всеми командами и разбором неудач: `mod/spikes/loadspike/readme.txt`. Кратко (`<ИГРА>` — папка с `Processor Tycoon.exe`, игра закрыта, команды из корня репозитория):

1. Сборка: `python mod\build.py --game-dir "<ИГРА>" --src mod\spikes\loadspike --out spikes\loadspike\PTMod.dll` → `mod\build\spikes\loadspike\PTMod.dll`. Сборка называется `PTMod.dll` (имя сборки `PTMod`, как у настоящего мода), поэтому регистрация в JSON та же, что будет у установщика.
2. Проверка скрипта без игры: `python tools\spike_install.py --self-test`; пробный прогон на игре (только чтение): `python tools\spike_install.py --game-dir "<ИГРА>" --install --dry-run`.
3. **Прогон A** (без записи для Harmony): `--install`, запуск игры, главное меню, старт игры или загрузка сейва (смена сцены), выход кнопкой игры; `python tools\spike_install.py --game-dir "<ИГРА>" --status` печатает строки `[PTMod] spike ...` из `Player.log` и предварительный вердикт. Пока спайк стоит, сохранить хвосты обоих JSON.
4. **Прогон B**: ещё один запуск без изменений — снова ровно одна строка `spike Init`.
5. **Прогон C** (только если в A было `harmony: NOT loaded`): `--remove`, затем `--install --register-harmony`, запуск, `--status`.
6. Снятие: `--remove` (оба JSON возвращаются к исходным байтам, DLL удаляются, скрипт сообщает, совпали ли JSON с оригиналами из `_original_en\`), запуск игры: в `Player.log` нет строк `[PTMod]`.

Что делает спайк: `ModBootstrap.Init` (`PTMod.Core`, вызывается из `RuntimeInitializeOnLoads.json`) пишет `[PTMod] spike Init: unity=<Application.unityVersion>, mono=<Environment.Version>`, затем `spike info` (рантайм, имя сборки, кадр, число сцен), пробу Harmony (`Assembly.Load("0Harmony")` по имени через рефлексию, без патчей: `harmony: loaded … / NOT loaded (…)`) и создаёт `PTModHost` (`DontDestroyOnLoad`). Хост пишет `host: Awake/Start/first Update`, `scene loaded: buildIndex=…` (первые 8 загрузок сцен, по ним видно порядок «Init раньше level0»), `host: quit` и `host: destroyed`. Повторный вызов `Init` даёт строку `spike repeat: …`, а не вторую `spike Init`.

Что установлено скриптом: `Managed\PTMod.dll`, `Managed\0Harmony.dll`, записи `PTMod.dll` (тип 16) в `ScriptingAssemblies.json` и `PTMod.Core.ModBootstrap.Init` (`loadTypes` 1) в `RuntimeInitializeOnLoads.json`; с `--register-harmony` ещё запись `0Harmony.dll`. Оригиналы JSON — в `_original_en\`, манифест — `_original_en\ptmod_spike_install.json`. Скрипт отказывает, если игра запущена, если DLL не спайковая, если в игре уже есть чужая установка мода или нет `_original_en\`.

Проверено без игры: `--self-test` (25 проверок: пробный прогон, установка, повторная установка, снятие, откат при сбое посередине, отказы, разбор лога); полный цикл установки и снятия на копии двух настоящих JSON игры дал побайтно исходные файлы; `--install --dry-run`, `--status` и `--remove --dry-run` на настоящей папке игры ничего не изменили (хэши JSON и состав `Managed\`, `_original_en\` до и после совпали).

Критерии (acceptance TSK-005): строка `spike Init` в `Player.log` раньше `scene loaded: buildIndex=0`; повторный запуск — одна строка; после `--remove` игра ванильная; статус решения обновлён (accepted или переход к TSK-007).

## Evidence

**Статус: ждёт владельца.** Раздел заполняется после прогонов; до этого статус решения остаётся accepted, TSK-005 не закрыта.

### Известно до прогона (файлы игры, только чтение, 2026-10-10)

- `ScriptingAssemblies.json`: `{"names":[…],"types":[…]}`, одна строка UTF-8 без BOM и без перевода строки в конце, 104 записи. Тип 2 — модули `UnityEngine*` (74), тип 16 — `Assembly-CSharp.dll` и остальные не-движковые сборки (30): пакеты Unity, а также `System.IO.Hashing.dll`, `System.Runtime.CompilerServices.Unsafe.dll`, `Unity.VisualScripting.Antlr3.Runtime.dll`. Сторонние библиотеки в `Managed` игра, судя по этому, регистрирует с типом 16, так что запись для `0Harmony.dll` вероятно понадобится; окончательно покажет прогон A.
- `RuntimeInitializeOnLoads.json`: `{"root":[…]}`, одна строка UTF-8 без BOM с завершающим `\r\n`, 15 записей вида `{"assemblyName","nameSpace","className","methodName","loadTypes","isUnityClass"}`; `loadTypes`: 0 — 6, 1 — 1 (`Unity.VisualScripting.Core`, `RuntimeVSUsageUtility.RuntimeInitializeOnLoadBeforeSceneLoad`), 2 — 7, 3 — 1. Записей `Assembly-CSharp` нет. Формат, который пишет `ptru/mods.py`, совпадает с ним.

### Прогон A (без записи для Harmony)

- Дата, версия игры: ждёт владельца.
- Фрагмент `Player.log` (строки `[PTMod] spike …` из `--status`): ждёт владельца.
- Хвост `ScriptingAssemblies.json` и `RuntimeInitializeOnLoads.json` с нашими записями (фактический формат): ждёт владельца.
- `Application.unityVersion` / `Environment.Version` / рантайм из строки `spike info`: ждёт владельца.
- `Init` раньше `scene loaded: buildIndex=0`: ждёт владельца.
- `PTModHost` создан и пережил смену сцены: ждёт владельца.
- Harmony найден без записи (`harmony: loaded` / `NOT loaded`, текст исключения): ждёт владельца.

### Прогон B (повторный запуск)

- Ровно одна строка `spike Init` (и нет `spike repeat`): ждёт владельца.

### Прогон C (только при `NOT loaded` в A: `--install --register-harmony`)

- Результат пробы Harmony с записью: ждёт владельца / не требовался.

### Снятие

- `--remove`, JSON совпали с оригиналами, игра ванильная (нет строк `[PTMod]`): ждёт владельца.

### Вердикт

- Загрузка по двум JSON работает: ждёт владельца (да / нет → TSK-007).
- Запись `0Harmony.dll` в `ScriptingAssemblies.json` нужна: ждёт владельца (да / нет; если да — `HARMONY_ASSEMBLY` в `ptru/mods.py` и установщик TSK-044 регистрируют её).
- Статус решения после прогона: ждёт владельца (accepted / переход к TSK-007).

## Links
- `.kiro/specs/mod-framework/`
- `.kiro/steering/context.md`
- `mod/spikes/loadspike/` — код спайка и `readme.txt` для владельца
- `tools/spike_install.py` — установка и снятие спайка
