# mod/lib: сторонние библиотеки мода

Здесь лежит единственная сторонняя зависимость мода, `0Harmony.dll`. Сама DLL **не хранится в git** (`.gitignore`: `mod/lib/*.dll`), в репозитории только лицензия.

| Файл | Что это | Откуда |
|---|---|---|
| `0Harmony.dll` | Harmony 2.3.6, сборка `net48`, лицензия MIT | NuGet-пакет `Lib.Harmony` 2.3.6, файл `lib/net48/0Harmony.dll` |
| `Harmony.LICENSE` | текст лицензии MIT (Andreas Pardeike) | тот же пакет; кладётся в релизный архив рядом с DLL |

Решение о Harmony: `.kiro/decisions/0002-harmony-for-method-patches.md` (NuGet `Lib.Harmony` 2.3.x; фактическая версия файла 2.3.6.0, см. проверку ниже). Загрузка выполнена один раз с одобрения владельца (2026-10-10), других загрузок нет: .NET SDK, NuGet-клиент и xUnit не используются.

## Как проверить файл

```
AssemblyName : 0Harmony, Version=2.3.6.0
ProductVersion : 2.3.6.0+f41da7608c5c3e9c7bf16551462b14c0b5392db3
SHA-256 : df043f05c6e43602fa8e8f38f52a3a2cf962aab7a65a9d85573ea25f01a63e80
```

Проверка хэша в PowerShell: `Get-FileHash mod\lib\0Harmony.dll -Algorithm SHA256`.

## Как восстановить

Если `0Harmony.dll` потерян:

1. Скачайте пакет `Lib.Harmony` 2.3.6 с nuget.org (`https://www.nuget.org/packages/Lib.Harmony/2.3.6`, кнопка «Download package»).
2. Откройте `.nupkg` как zip и возьмите `lib/net48/0Harmony.dll`.
3. Положите его сюда и сверьте хэш.

Любая другая версия Harmony требует отдельного одобрения владельца (фолбэк A спайка 0002).

## Как используется

- `python mod/build.py` берёт `mod/lib/0Harmony.dll` как ссылку для компиляции и копирует рядом с `PTMod.dll` в `mod/build/`.
- `python mod/build.py --release` кладёт `0Harmony.dll`, `Harmony.LICENSE` и `SHA256SUMS.txt` в `mod/build/release/PTMod-<версия>.zip`.
- Тесты (`--tests`) Harmony не используют и собираются без него.
- DLL игры (`Processor Tycoon_Data/Managed/*.dll`, `_original_en/Assembly-CSharp.dll`) сюда не копируются никогда: они нужны только как ссылки и читаются из папки игры.
