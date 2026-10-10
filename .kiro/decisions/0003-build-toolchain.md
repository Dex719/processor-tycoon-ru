# 0003. Build Toolchain

## Status
accepted

## Context
`PTMod.dll` нужно компилировать против `Managed/*.dll` игры (Unity 6, Mono). .NET SDK на машине не установлен. Зато есть Visual Studio 2022 Build Tools с компилятором Roslyn: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe` (C# до 13). В `Managed/` игры лежат `mscorlib.dll`, `netstandard.dll`, `System*.dll` и модули Unity. Ещё есть старый `csc.exe` .NET Framework 4 (C# 5).

## Decision
- Сборка — Roslyn `csc.exe` из VS Build Tools, `-langversion:latest -nostdlib -target:library`, ссылки на `Managed/mscorlib.dll`, `netstandard.dll`, `System*.dll`, `UnityEngine*.dll`, `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`, английский оригинал `Assembly-CSharp.dll` (из `_original_en/`) и `mod/lib/0Harmony.dll`.
- Скрипт сборки `mod/build.py` (Python) находит `csc.exe` (VS Build Tools через `vswhere` или известные пути) и папку игры, собирает `mod/build/PTMod.dll`. Путь к игре — аргумент или переменная `PT_GAME_DIR`.
- Тесты чистой логики (`mod/tests/`) собираются тем же `csc` в консольный exe под .NET Framework 4.8 (ссылки на `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`) со своим мини-раннером, без xUnit и NuGet. Логика под тестами не зависит от Unity.
- Тесты установщика — Python `unittest`.
- .NET SDK не используется (загрузка не нужна).

## Consequences
- Плюс: современный C# без загрузок, сборка одной командой.
- Минус: сборка требует локальной копии игры и VS Build Tools. Для игроков без них DLL публикуются готовыми в GitHub Release.
- DLL игры и Harmony в репозиторий не попадают (`.gitignore`).

## Evidence

Спайк 0003 (TSK-003), 2026-10-10. Решение подтверждено, статус остаётся accepted. Что игра сама загрузит собранную DLL, проверяет спайк 0001 (TSK-005, запускает владелец); здесь только сборка и открытие для рефлексии.

### Компилятор и параметры

- `csc.exe`: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe`, версия `4.14.0-3.26424.7 (a528c90a)` (Roslyn 4.14; `-langversion:latest` даёт C# 13). `mod/build.py` ищет его через `vswhere`, затем по известным путям; `--csc` и `PT_CSC` переопределяют.
- Параметры из `mod/build.py`: `-nologo -utf8output -codepage:65001 -fullpaths -nostdlib -langversion:latest -nowarn:1701,1702 -deterministic -warn:4`; отладка — `-optimize- -debug:portable -define:PTMOD_DEBUG`, релиз — `-optimize+ -debug-`. Аргументы идут через response-файл (`mod/build/gen/<имя>.rsp`), `-noconfig` задаётся в командной строке. `-nowarn:1701,1702` гасит предупреждения об унификации версий сборок (Harmony собран против mscorlib 4.0.0.0, у Unity та же идентичность).
- Ссылки сборки мода (102 файла из папки игры и репозитория): `mscorlib.dll`, `netstandard.dll` (2.1.0.0), все `System*.dll` (22), все `UnityEngine*.dll` (75, включая `UnityEngine.UI.dll`), `Unity.TextMeshPro.dll`, `_original_en\Assembly-CSharp.dll` (английский оригинал, не русифицированная копия из `Managed`), `mod\lib\0Harmony.dll` (2.3.6.0, SHA-256 `df043f05…3e80`). Всего в `Managed\` 127 DLL; нужные подмножества `build.py` выбирает по именам, в выход и в репозиторий DLL игры не копируются.
- Рантайм-библиотека Unity: `mscorlib.dll` и `System*.dll` — сборка 4.0.0.0, файл 4.6.57.0 (класс-библиотека Mono игры); `netstandard.dll` — фасад 2.1.0.0. Тесты: .NET Framework 4.8 (`mscorlib` 4.8.9336.0), 6 ссылок из `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\` (`mscorlib`, `System`, `System.Core`, `System.Numerics`, `System.Xml`, `System.Xml.Linq`).

### Проба и открытие для рефлексии

- Файлы: `mod/spikes/toolchaincheck/modernsyntax.cs`, `check_reflection_load.ps1`. Сборка: `python mod/build.py --game-dir "<игра>" --src mod/spikes/toolchaincheck --out spikes/ModernSyntax.dll` → `mod/build/spikes/ModernSyntax.dll` (11 КБ), 0 ошибок и 0 предупреждений.
- В пробе: `?.`, `??`, `??=`, интерполяция с выравниванием и форматом, локальные и статические локальные функции, `readonly struct` с `in`-параметром, switch-выражения с реляционными паттернами, `is not null`, кортежи и деконструкция, `new()` по типу цели, `using`-объявление, разделители цифр; плюс вызовы `UnityEngine` (`Debug`, `Mathf`, `Vector2`), типы игры (`ProcessorTycoon.GameManager`, `ProcessorTycoon.TimeSystem.DateController`) и формы патчей Harmony из спайка 0002 (prefix с `__instance`/`out __state`, postfix, finalizer, transpiler на `IEnumerable<CodeInstruction>`).
- AssemblyRef у DLL: `mscorlib` 4.0.0.0, `UnityEngine.CoreModule`, `0Harmony` 2.3.6.0, `Assembly-CSharp` — ссылка на игру есть. `netstandard` в ссылки DLL не попал: типы разрешаются прямо в `mscorlib`.
- `check_reflection_load.ps1 -Dll mod\build\spikes\ModernSyntax.dll -GameDir "<игра>"` (Windows PowerShell 5.1): `Assembly.ReflectionOnlyLoadFrom` успешен, 4 ссылки разрешены (`Assembly-CSharp` — из `_original_en`), замыкание зависимостей 64 сборки, ни одной ненайденной, все 9 типов перечислены, код возврата 0. То же для `mod\build\PTMod.dll` (ссылки `mscorlib`, `UnityEngine.CoreModule`) и для спайка загрузки `mod\build\spikes\loadspike\PTMod.dll`. Скрипт возвращает 1, если нет нужной ссылки (`-Require`, по умолчанию `Assembly-CSharp`), 2 при неверных аргументах.
- Harmony 2.3.6 (net48) против mscorlib Unity: конфликтов типов нет, особого решения не потребовалось (ссылка добавляется как обычная). Компилируются `new Harmony(id)`, `CreateClassProcessor(type).Patch()`, `GetPatchedMethods()`, атрибуты `HarmonyPatch/Prefix/Postfix/Finalizer/Transpiler`, `CodeInstruction`, `OpCodes`. Сама `0Harmony.dll` ссылается только на `mscorlib`, `System`, `System.Core` — все три есть в `Managed\`. Работу Harmony в рантайме Unity проверяет спайк 0002.
- Ограничение проверки: при `GetTypes()` на `0Harmony.dll` один тип (`System.Dynamic.DynamicObject.GetMetaObject`) не грузится с жалобой на security transparency. Это следствие смеси `System.Core` игры (Mono) с `mscorlib` .NET Framework в процессе проверки, а не дефект DLL; скрипт показывает такие случаи предупреждением и не считает ошибкой, любые другие проблемы загрузки типов — ошибка.

### Конструкции языка: что компилируется

Результат `csc` 4.14 с флагами выше. «Unity» — ссылки на `Managed\` (сборка мода), «net48» — ссылки на .NET Framework 4.8 (сборка тестов).

| Конструкция | Unity | net48 | Вывод |
|---|---|---|---|
| `?.`, `??`, `??=`, интерполяция, локальные и статические локальные функции, `readonly struct`, `in`, `ref struct`, switch-выражения и реляционные паттерны, `is not`, `new()` по типу цели, `using var`, `ValueTuple` и деконструкция, `async`/`await`, `#nullable enable`, file-scoped namespace, raw string literal | да | да | можно в общем коде |
| `init`, `record`, `record struct`, `with` | CS0518 (нет `IsExternalInit`) | CS0518 | только с `Core/Compat/IsExternalInit.cs`; с ним компилируются в обоих окружениях |
| `required` | CS0656 (`RequiredMemberAttribute`) | CS0656 | не использовать |
| `^`, `..` (`Index`, `Range`), списочные паттерны `[a, b]` | да | CS0518 | не использовать в `Core/Logic`; полифил `Index`/`Range` не добавлять (в Unity они есть, своя копия даёт CS0436) |
| `Span<T>`, `stackalloc` в `Span`, `System.HashCode` | да | ошибка | не использовать в `Core/Logic` (хеш — вручную, например `h = h * 31 + x`) |
| методы интерфейса по умолчанию | да | CS8701 | не использовать |
| `static abstract` в интерфейсах, generic math (`INumber<T>`) | CS8919 / CS0234 | то же | недоступно в обоих |

### API: общее подмножество для `Core/Logic`

Компилируется в Unity, но не в net48 (в `Core/Logic` этого нет; в коде, который собирается только под Unity, можно): `Math.Clamp`, `MathF`, `string.StartsWith(char)`, `EndsWith(char)`, `Contains(char)` (в net48 без `using System.Linq` не собирается, с ним — через `Enumerable.Contains`, лучше `IndexOf(char) >= 0`), `string.Split(char, StringSplitOptions)`, `string.Join(char, …)`, `Replace(a, b, StringComparison)`, `Contains(string, StringComparison)`, `GetHashCode(StringComparison)`, `Dictionary.TryAdd`, `Dictionary.Remove(key, out value)`, `foreach (var (k, v) in dictionary)` (`KeyValuePair.Deconstruct`), `Queue.TryDequeue`, `Stack.TryPop`, `Enumerable.SkipLast`/`TakeLast`, `Enum.Parse<T>`, `Path.GetRelativePath`, `StringBuilder.AppendJoin`.

Нет ни в Unity, ни в net48: `File.Move(src, dst, overwrite)`, `Random.Shared`, `CollectionsMarshal`, `Convert.ToHexString`, `Enumerable.Chunk`/`MinBy`, `Enumerable.Zip(a, b)` без селектора (вариант с кортежами), `System.Text.Json`.

Есть в обоих: `Enum.TryParse<T>`, `Array.Empty<T>()`, `Enumerable.ToHashSet`/`Append`/`Prepend`, `File.Replace`, `Stopwatch`, `ConcurrentDictionary`, `HashSet.TryGetValue`, `BitConverter.ToString`, `SortedSet`/`SortedDictionary`, `IReadOnlyList`/`IReadOnlyDictionary`, `Func`/`Action`/`Predicate`/`EventHandler<T>`, `Math.Round(x, MidpointRounding)`, `string.Split(params char[])`, `Split(string[], options)`, `ToString("F2", CultureInfo.InvariantCulture)`.

### Полифилы

- Нужен один: `mod/src/PTMod/Core/Compat/IsExternalInit.cs` (`internal static class IsExternalInit` в `System.Runtime.CompilerServices`). Проверено на копии дерева: сборка мода (`PTMod.dll`, 3 файла) и сборка тестов (`PTMod.Tests.exe`, тест на `init`, `record`, `record struct`, `with`) проходят, 14 тестов зелёные; без файла обе сборки падают с CS0518.
- Остальное из таблицы полифилами не лечится или конфликтует с типами Unity, поэтому правило другое: не использовать (FR-21 п. 4).
- Для `UnityEngine.Object` операторы `?.`, `??`, `??=` и `is null` обходят перегруженное сравнение Unity с `null`; их применяют только к обычным объектам C# (в пробе это оговорено комментарием).

### Раннер и установщик

- `python mod/build.py --tests` (тот же `csc`, .NET Framework 4.8): собирается и возвращает 0 — 95 тестов, 0 упало (на момент проверки 21 файл: раннер, смоук-тесты и логика соседних пакетов); после интеграции волны 2: 219 тестов, 23 файла, `--coverage-types` зелёный, три прогона подряд без расхождений.
- `python mod/build.py --game-dir "<игра>"`: `PTMod.dll` (14 файлов) собирается вместе с `IsExternalInit.cs`.
- `python -m unittest discover -s mod/tests/installer`: 105 тестов OK (после интеграции волны 2 — 109, добавлены тесты `tools/spike_install.py`).

### Не проверено

- Загрузка DLL самой игрой и работа `Init` — спайк 0001 (TSK-005). Harmony в рантайме Unity — спайк 0002 (TSK-006).
- Ветка `check_reflection_load.ps1` для PowerShell 7 (перезапуск в `powershell.exe`): `pwsh` на машине нет, не запускалась.

### Как повторить

```
python mod/build.py --game-dir "<игра>" --src mod/spikes/toolchaincheck --out spikes/ModernSyntax.dll
powershell -NoProfile -ExecutionPolicy Bypass -File mod\spikes\toolchaincheck\check_reflection_load.ps1 -Dll mod\build\spikes\ModernSyntax.dll -GameDir "<игра>"
python mod/build.py --tests
```

## Links
- `.kiro/specs/mod-framework/`
- `.kiro/steering/owner-decisions.md`
- `mod/spikes/toolchaincheck/` — проба и проверка открытия для рефлексии
- `mod/src/PTMod/Core/Compat/IsExternalInit.cs` — единственный полифил
