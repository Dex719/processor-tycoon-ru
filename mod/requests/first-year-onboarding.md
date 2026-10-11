# Запросы first-year-onboarding к итоговому интегратору

## 1. Чистые типы публичного API недоступны чистой логике в тестах

Тестовые сборки (`build.py` -> `build_tests`, `build_feature.py` -> `build_feature_tests`) берут из `Features/<Pascal>/` только `Logic/**`. Папка `Api/` в них не входит, а
`Features/FirstYearOnboarding/Api/Dto/*.cs` (`PendingMode`, `OnboardingMode`, `OnboardingResult`, `OnboardingStatus`) не ссылаются ни на ядро, ни на Unity, ни на игру.

Обход, который предписан реализации фичи (пока Logic не написана): автомат (`Logic/OnboardingMachine.cs`) держит собственные `OnboardingMode` и `OnboardingResult` в пространстве `PTMod.Features.FirstYearOnboarding.Logic`
(как в design.md), а Unity-слой (директор, `OnboardingMailer`) переводит их в типы из `Api/Dto` при вызове `OnboardingService.RaiseCompleted`.

Просьба (необязательная): добавить `Features/<Pascal>/Api/Dto/**/*.cs` в оба списка файлов тестовой сборки. Тогда дубли в `Logic` можно убрать, а `[Covers]` и тесты смогут
ссылаться на публичные типы API напрямую. Остальная часть `Api/` (`OnboardingService`, `IOnboardingBackend`, `NullOnboardingBackend`) использует `PTMod.Core` и в тесты не попадает.

Статус (кусок 2, TSK-006…010): обход сделан. `Logic/OnboardingState.cs` содержит `OnboardingMode`, `OnboardingResult` (и `StepStatus`, `StepState`, `OnboardingState`, `OnboardingProgress`),
`Logic/IOnboardingOutput.cs` — интерфейс вывода. Unity-слой переводит их в типы `Api/Dto` через `OnboardingApiBridge` (`ToApi(...)`, `PublishCompleted(...)`). Если просьба выше будет принята,
дубли `OnboardingMode`/`OnboardingResult` в `Logic` можно заменить типами из `Api/Dto` (поля и числа значений совпадают).

Нужного в ядре и в чужом API, чего не хватало бы, нет.

Статус (кусок 3, TSK-011…015): чистому коду `Api/Dto` нужны тесты правил отложенного режима, поэтому два чистых файла API (`PendingMode.cs`, `PendingModeBook.cs`, правила `OnboardingService.SetPendingMode`
и итогового решения новой игры) скопированы побайтно в `mod/tests/PTMod.Tests/Features/FirstYearOnboarding/ApiShim/` и компилируются только в тестовой сборке; тест `ApiShimTests.ShimFiles_AreIdenticalToApiDto`
падает, если оригинал изменили, а копию не обновили. Это тот же обход, что у `event-system` (`ApiModelShim`). Если просьба выше принята, достаточно удалить папку `ApiShim/` и класс `ApiShimTests`
(в `OnboardingServiceTests.cs`); `[Covers(typeof(PendingMode), typeof(PendingModeBook))]` на `OnboardingServiceTests` останется верным. `OnboardingService` не ссылается на `Logic` (зависимые фичи
собираются с одной папкой `Api`); тест `TheApi_DoesNotReachTheLogicOfTheFeature_...` следит за этим.

## 2. Публичный доступ к «сырому» тексту строки (`ModL10n`)

Проверка шагов (`StepValidator`, задача TSK-002) сверяет индексы `{n}` в тексте письма с длиной списка `args`, префикс `[Наставник]` заголовка и длину тела (≤ 1200 символов).
Для этого нужен текст ключа в языке `ru` до подстановки аргументов. Публичный `ModL10n` отдаёт только готовую строку (`L`), поэтому `GameStepValidationContext.GetRussianText`
сейчас читает приватное поле `ModL10n.Tables` (`L10nTables`) через рефлексию. Если поле переименуют или уберут, проверка молча пропускает эти три правила (остальные работают), в лог идёт строка `DEBUG`.

Просьба: добавить в `ModL10n` внутренний (достаточно `internal`) метод, например
`internal static bool TryGetText(string language, string key, out string text)` — сырой текст ключа из таблицы языка (с `{0}`, без подстановки и без цепочки откатов), и тогда
`GameStepValidationContext.GetRussianText` заменить вызовом без рефлексии (один метод, `mod/src/PTMod/Features/FirstYearOnboarding/GameStepValidationContext.cs`).

## 3. Тесты `BuiltDll_*` в режиме `build_feature.py --tests`

После `python mod/build_feature.py <slug>` в `mod/build/features/<slug>/` лежит свежий, но неполный `PTMod.dll` (ядро, одна фича, её ресурсы). Затем `build_feature.py <slug> --tests` передаёт тестам этот каталог как `PTMOD_BUILD_DIR`,
и тест `ArchitectureTests.BuiltDll_EmbedsContentAndL10nResources` (ожидает ресурсы всех фич из дерева исходников) падает: «files of Content/ and L10n/ that PTMod.dll does not embed». Пока DLL старше исходников,
тест пропускается (`skip`), поэтому ошибка видна только сразу после сборки. На результат фичи это не влияет; для сборки целиком (`python mod/build.py`) тест корректен.

Просьба (необязательная): пропускать тесты `BuiltDll_*` в режиме фичи (например, `build_feature.py` задаёт `PTMOD_FEATURE_BUILD=1`, тест смотрит на неё) или проверять только ресурсы фичи и ядра.
Автор куска убрал свои `PTMod.dll`/`PTMod.pdb` из `mod/build/features/first-year-onboarding/` после проверки сборки.

## 4. Идея для ядра: признак «время идёт»

Для паузы при новом шаге (FR-12) нужно знать, идёт ли игровое время: `DateController.ManualPause` на уже остановленной игре перезаписывает `speedButtonBeforeManualPause` кнопкой паузы, и «▶» потом ничего не возобновляет.
Публичного признака нет ни в игре, ни в `ModTicks`; фича читает приватное поле `DateController.currentTimeSpeed` через `AccessTools` (`GameOnboardingHost`); если поле не найдено, пауза не ставится
(безопасная сторона, одно предупреждение в журнале).

Просьба (необязательная): ядро даёт `ModTicks.IsTimeRunning` (и, например, `ModTicks.TimeSpeed`) и общий метод «поставить на паузу, только если время идёт». Тогда `GameOnboardingHost.TimeRunning`/`PauseTime`
заменяются вызовом ядра; логика `PausePolicy` остаётся.

## 5. Идея для ядра: дописать строку к тексту письма в языке игрока

Письмо `ModMail` рисуется из ключа и аргументов; добавить к нему строку нельзя. Строка «Уже сделано: …» (письмо после «догнанных» шагов) и список шагов в итоговом письме выводятся
через `ModMail.SetStatusLine` (ядро показывает её под текстом и хранит с письмом), но это готовый текст на языке игры в момент отправки: после смены языка он не перерисовывается. Повтор письма
(`onb:<id>:repeat`) по той же причине несёт заголовок готовым текстом (`Title = LocText.Of(...)`, ключ + «(повтор)»).

Просьба (необязательная): у `ModMailMessage` поле «дополнительные строки» (ключ + аргументы, перерисовывается при смене языка) или суффикс заголовка. Тогда `OnboardingMailer` отдаёт ядру ключи
вместо готового текста; `MailPlanner` уже собирает нужные ключи отдельно от текста.

## 6. Идея для ядра: точка расширения самопроверки для фич

`ModSelfCheck` (TSK-016, самопроверка на старте) держит таблицы проверяемых членов игры внутри ядра и не принимает строки фич. Фича проверяет себя сама и пишет в `Player.log`
(`OnboardingSelfCheck`: `[first-year-onboarding] INFO self-check: OK` или `self-check: N error(s), M warning(s)` и по строке на находку: члены игры, приём предикатов `onb.*`, каталог шагов,
русские ключи). Во вкладке «Диагностика» хаба этих находок нет. Тот же запрос уже стоит у `contract-transparency` и `cpu-architectures`.

Просьба (необязательная): публичный `ModSelfCheck.AddFeatureFindings(featureId, Func<IEnumerable<SelfCheckItem>>)` (или регистрация строк `MemberRow`). Тогда `OnboardingSelfCheck.Run` отдаёт
`SelfCheckReport.Problems` ядру (серьёзность, область, сообщение), а строку `self-check: OK` продолжает писать сама; чистая часть (`OnboardingSelfCheckRules`) не меняется.

## 7. Замер производительности работает только в отладочной сборке

Флаг замера (`TSK-017`) читает `ModSettings.Debug("first-year-onboarding.perf")`; в релизной сборке он всегда ложь (`BuildInfo.IsDebug` — константа), поэтому владелец снимает числа из отладочной сборки
(`python mod/build.py` без `--release`, она и есть сборка по умолчанию). Если нужен замер на релизе, достаточно, чтобы ядро дало флаг, не привязанный к `IsDebug`; фича перейдёт на него заменой одной
строки в `OnboardingDirector` (`PerfFlag`). Просьба необязательная.
