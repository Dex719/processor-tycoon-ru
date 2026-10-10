# Tasks: contract-transparency

## Document Information

- **Feature Name**: contract-transparency
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/contract-transparency/requirements.md`
  - Design: `.kiro/specs/contract-transparency/design.md`
  - Framework: `.kiro/specs/mod-framework/` (сервисы `PTMod.Core`; номера задач — `.kiro/specs/mod-framework/tasks.md`)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала пишется чистая логика с тестами: шансы, требования, обязательства, политика подтверждения, решения автоматики, классификация исхода тендера, журнал. Затем адаптер к игре и патчи. В конце — окна `ModUI`, строки и проверки.

Единственный патч, который меняет поток игры, — prefix на `Contract.AddOffer` (TSK-007). Его делаем после логики риска и журнала. Он обязан падать «в ваниль»: при любой ошибке предложение уходит как без мода.

### Implementation Strategy
- Чистая логика (`mod/src/PTMod/Features/ContractTransparency/Logic/`) не ссылается на `UnityEngine` и типы игры, использует общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`, `Range`), поэтому тестируется мини-раннером ядра без запуска игры.
- Адаптер `ContractInspector` — единственное место, где читаются приватные члены игры. Он же сверяет результаты с `Contract.FailsAtAnyRequirement` и `Contract.ChanceToSign`.
- Патча на `Contract.GetBestOffers` нет (цель закреплена ядром, P23): метод только вызывается через `AccessTools`.
- Каждая задача заканчивается сборкой (`python mod/build.py --game-dir <папка игры>`) и зелёными тестами (`python mod/build.py --tests`). Запуск игры — только в TSK-022 (владелец).

### Development Approach
- **Testing Strategy**: TDD для `Logic/` (примеры чисел из design.md, раздел «Формулы и баланс»); каждый публичный тип `Logic/` указан в `[Covers]`; самопроверка целей патчей на старте; ручные сценарии M1–M14.
- **Integration Strategy**: патчи регистрируются через `ctx.Patch` (`ModHooks`) и снимаются ядром при выключении фичи; UI подключается последним.
- **Deployment Strategy**: в составе `PTMod.dll` через `install.py --with-mods` (`mod-framework`).

> Номера задач `mod-framework` в поле **Depends** сверены с `.kiro/specs/mod-framework/tasks.md` (v0.2): ModHooks — TSK-023, ModTicks — TSK-024, ModSave — TSK-025, ModCompanies — TSK-026, ModModifiers — TSK-027/059/060, ModConditions — TSK-019/032, ModNotify — TSK-033, ModUI — TSK-034…036/064, ModMail — TSK-037/038/062, ModIds — TSK-066, мини-раннер — TSK-067.

## Фаза 1 — Каркас и чистая логика

- [ ] **TSK-001**: Скелет фичи `ContractTransparencyFeature`
  - **Requirement**: FR-13, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractTransparencyFeature.cs` (`[ModFeature]`, `IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn`, `EnabledByDefault`, `SupportsHotToggle`, `Register(FeatureContext)`, `OnEnabled`/`OnDisabled`), `mod/src/PTMod/Features/ContractTransparency/ContractTransparencySettings.cs` (`ParamSpec`)
  - **Acceptance**:
    - фича с ID `contract-transparency` видна в `ModFeatures.All` и в хабе «Моды» с переключателем «Включено»;
    - `SupportsHotToggle = true`: `OnEnabled`/`OnDisabled` работают без перезапуска;
    - тела патчей обёрнуты в `ModHooks.Guard`; тестовый флаг `ModSettings.Debug("debug.brokenPatch")` доводит фичу до `Faulted`, игра работает, ядро показывает одно уведомление; своих счётчиков ошибок в коде фичи нет;
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`, `ModFeatures`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`), TSK-039 (хаб «Моды»), TSK-067 (мини-раннер)

- [ ] **TSK-002**: Контент `rules.json`, `ContractRules` и настройки
  - **Requirement**: FR-4, FR-13, NFR-6
  - **Deliverables**: `mod/src/PTMod/Content/contract-transparency/rules.json`, `mod/src/PTMod/Features/ContractTransparency/Logic/ContractRules.cs`, `mod/tests/PTMod.Tests/ContractTransparency/ContractRulesTests.cs`
  - **Acceptance**:
    - значения по умолчанию как в design.md (`lowActualChance` 0.10, `capacity` 500, `tenderHistoryYears` 10, `ai.contractMarginThreshold` 0.45);
    - значение вне диапазона заменяется дефолтом и пишется в `ModLog` (тест);
    - оверрайд из `<папка игры>/PTMod/content/contract-transparency/rules.json` перекрывает встроенный (`ModContent.LoadConfig`);
    - `ParamSpec` `confirmMode` (Int 0…2, по умолчанию 1 = «Только рискованные»), `showActualProbability`, `notifyTenderLoss`, `logAutomation` (Bool) объявлены через `ctx.Param` и видны в хабе.
  - **Depends**: TSK-001; mod-framework TSK-021 (`ModContent`), TSK-067

- [ ] **TSK-003**: `OddsMath` — шансы и фактическая вероятность
  - **Requirement**: FR-2, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/OddsMath.cs`, `mod/tests/PTMod.Tests/ContractTransparency/OddsMathTests.cs`
  - **Acceptance**:
    - тесты проходят: 60/40 → 78.9/21.1 %; 50/30/20 → 69.4/20.8/9.7 %; 1000/1 → шансы 100/1, P 100/0 %; 50/50 → 66.7/33.3 % (раньше предложивший первым);
    - `IpsForChance(0.5, 60) == 60`;
    - `WithCandidate` удаляет все CPU компании кандидата (сравнение по `CompanyKey`);
    - `PessimisticProbability` добавляет участников и пересчитывает;
    - точность P — 1e-9;
    - `OddsMath` указан в `[Covers]`.
  - **Depends**: mod-framework TSK-067 (мини-раннер)

- [ ] **TSK-004**: `RequirementCheck`, `CommitmentMath`, `ConfirmPolicy`
  - **Requirement**: FR-1, FR-3, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/RequirementCheck.cs`, `.../Logic/CommitmentMath.cs`, `.../Logic/ConfirmPolicy.cs`, `mod/tests/PTMod.Tests/ContractTransparency/RequirementCheckTests.cs`, `.../CommitmentMathTests.cs`, `.../ConfirmPolicyTests.cs`
  - **Acceptance**:
    - границы `Ips == MinIps × 0.75` и `Power == Max` проходят; корпус 0/1/2 × DIP/PLCC/PGA — 9 тестов;
    - штраф за разрыв 50 000 шт. / 730 дней ≈ 888 166 667;
    - каждый из 7 `RiskFlags` срабатывает отдельно;
    - матрица `ConfirmMode × RiskFlags` покрыта.
  - **Depends**: TSK-002

- [ ] **TSK-005**: Адаптер `ContractInspector` со сверкой
  - **Requirement**: FR-1, FR-2, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractInspector.cs`, `.../OddsReport.cs`, `.../RequirementReport.cs`
  - **Acceptance**:
    - `ToleranceMultiplier` читает `Contract.mipsToleranceMultiplier`;
    - `GameChances` вызывает приватный `Contract.GetBestOffers` через AccessTools (не патчит) и читает `Contract.CpuOffer.Cpu` и `Chance`;
    - `BuildOdds` сверяет шанс кандидата с `Contract.ChanceToSign` (допуск 0.5 п. п.), `BuildRequirements` — с `Contract.FailsAtAnyRequirement`; расхождение → флаг и `ModLog`;
    - разбивка множителей — `ModModifiers.Explain("contract.signChance", {Company, CompanyKey, Cpu, Contract})` (`ModSources`, `ModsCapped`); провайдер без причины → строка без текста;
    - ключ компании — `ModCompanies.KeyOf`, при `null` — `sid:<ISaveID.SaveID>`; `SaveID` через интерфейс компании не читается;
    - `GetCpu == null` не роняет код;
    - `IsAvailable == false`, если член не найден.
  - **Depends**: TSK-003, TSK-004; mod-framework TSK-023 (`ModHooks`: AccessTools, самопроверка), TSK-026 (`ModCompanies`), TSK-027, TSK-059, TSK-060 (`ModModifiers.Explain`, `ModifierContext.Contract`)

## Фаза 2 — Журнал и патчи

- [ ] **TSK-006**: `ContractLog`, `ContractLogModel`, DTO, `ContractKey` и секция `ModSave`
  - **Requirement**: FR-8, NFR-3, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractLog.cs` (`IModSaveParticipant`, тонкий адаптер), `.../Logic/ContractLogModel.cs`, `.../Logic/SaveModels.cs` (DTO без поля версии), `.../Logic/LogRing.cs`, `.../Logic/ContractKeyBuilder.cs`, `mod/tests/PTMod.Tests/ContractTransparency/ContractLogModelTests.cs`, `.../ContractKeyTests.cs`
  - **Acceptance**:
    - `Read`/`Write` через `ModJson.Bind`/`ModJson.From`; `Write()` не возвращает `_schema` (его добавляет ядро), `SchemaVersion = 1`; `ResetToDefaults` даёт пустой журнал;
    - 520 записей → 500 последних;
    - итоги старше 10 лет удаляются при загрузке;
    - сериализация туда-обратно без потерь;
    - нет секции → пустой журнал; `FromSave` устойчив к пустому и частично битому DTO;
    - ключ стабилен, `|` в имени экранируется;
    - журнал из 500 записей ≤ 256 КБ.
  - **Depends**: TSK-002; mod-framework TSK-009, TSK-010 (`ModJson`), TSK-025 (`ModSave`)

- [ ] **TSK-007**: `OfferGate` — prefix и postfix `Contract.AddOffer`
  - **Requirement**: FR-3, FR-4, FR-14, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/OfferGate.cs`
  - **Acceptance**:
    - CPU ИИ, автоматика и `BypassScope` проходят без изменений;
    - ручное предложение при `NeedsConfirmation == true` не выполняется, вызывается открытие экрана (заглушка до TSK-013);
    - `ConfirmAndSend` вызывает `AddOffer` под обходом и `ContractWindow.TriggerUIUpdate`;
    - postfix пишет `OfferSent`/`OfferAutoConfirmed` только если `OffersIDs` вырос;
    - исключение в prefix → `true` и `ModLog`;
    - `Priority.First`.
  - **Depends**: TSK-005, TSK-006

- [ ] **TSK-008**: `TenderRecorder` — итоги тендеров и автоотмена
  - **Requirement**: FR-5, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/TenderRecorder.cs`, `.../Logic/TenderOutcomeClassifier.cs`, `mod/tests/PTMod.Tests/ContractTransparency/TenderOutcomeTests.cs`
  - **Acceptance**:
    - prefix `Contract.ChooseCpu` снимает `OffersIDs` и мощности;
    - postfix `Contract.ChooseCpu` пересобирает шансы розыгрыша повторным вызовом `Contract.GetBestOffers(OffersIDs)`; патча на `GetBestOffers` нет, самопроверка подтверждает, что цель не закреплена за ядром;
    - `TenderOutcomeClassifier.Classify` определяет исход (Won, Lost: outbid / autoReject / shadowed / retired, Void: noOffers / retired) — тесты на 7 случаев;
    - в перемотке (`ModTicks.IsFastForwarding`) и при загрузке (`ModTicks.IsLoading`) ничего не пишется;
    - `AutoRejected` содержит нужно, занято и мощность.
  - **Depends**: TSK-005, TSK-006; mod-framework TSK-024 (`ModTicks`)

- [ ] **TSK-009**: `AutomationAdvisor` и `AutomationLogger`
  - **Requirement**: FR-6, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/AutomationAdvisor.cs`, `.../Patches/AutomationLogger.cs`, `mod/tests/PTMod.Tests/ContractTransparency/AutomationAdvisorTests.cs`
  - **Acceptance**:
    - порядок проверок совпадает с `ContractAutomation.SendOffers` (снят → `SellOnContracts` → маржа → требования `AddOffer`);
    - маржа 0.35 при пороге 40 → `SkippedMargin` с числами;
    - финализатор сбрасывает `AutomationScope` даже при исключении;
    - сверка до и после → `Mismatch` в `ModLog`;
    - при выключенной автоматике — одна запись `AutomationOff`;
    - `Preview()` возвращает те же решения без вызова `AddOffer`.
  - **Depends**: TSK-005, TSK-006, TSK-007

- [ ] **TSK-010**: `TenderMailer` — сводка и уведомление
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/TenderMailer.cs`
  - **Acceptance**:
    - подписка `ctx.OnTick(TickPhase.DayPassed, …)` без `RunDuringFastForward`; в перемотке и при загрузке не вызывается;
    - при непустом буфере — одно письмо `ModMail.Send` «Итоги тендеров {год}»: `Tag = "ct:tenders:<yyyy-MM-dd>"`, `Priority = MailPriority.Normal`, строка на контракт, действие `open-log` «Открыть журнал» (`ModMailAction`, `Repeatable = true`); блоков ID писем нет;
    - `ModMail.RegisterActionHandler` открывает окно журнала с фильтром «Итоги тендеров» (после TSK-014), письмо не закрывается;
    - при проигрыше и `notifyTenderLoss` — одно `ModNotify`;
    - в пакетном прогоне (`ModTicks.IsBatchRun`) письмо и уведомление показываются ядром после `BatchEnded`, по одному на день;
    - пустой буфер не делает ничего.
  - **Depends**: TSK-008; mod-framework TSK-024 (`ModTicks`), TSK-033 (`ModNotify`), TSK-037, TSK-038 (`ModMail`: теги, действия), TSK-057 (`IsBatchRun`), TSK-062 (приоритеты, квоты)

## Фаза 3 — Интерфейс

- [ ] **TSK-011**: `ContractWindowAugmenter` — кнопки и «(факт. N %)»
  - **Requirement**: FR-2, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/ContractWindowAugmenter.cs`
  - **Acceptance**:
    - postfix `ContractWindow.Start` добавляет «Почему?» рядом с вероятностью и «Журнал» (клоны `ButtonUI`);
    - postfix `ContractWindow.UpdateProbabilityText` дописывает `ct.odds.actual`, только если CPU прошёл требования и включён `showActualProbability`;
    - при выключении фичи кнопки скрыты;
    - смена темы перекрашивает.
  - **Depends**: TSK-005; mod-framework TSK-034, TSK-035, TSK-036 (`ModUI`)

- [ ] **TSK-012**: Окно `OddsPanel` — разбор шанса
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/OddsPanel.cs`
  - **Acceptance**:
    - три строки требований с цветом и числами;
    - таблица предложений в порядке розыгрыша (компания, CPU, IPS, ванильный и игровой шанс, факт.);
    - строки «Модификаторы модов» с источниками из `ModModifiers.Explain` (фича, множитель, текст причины; «(ограничено)» при `IsBalanceCapped`/`IsFuseCapped`), «затенено», «что изменить», «предложений нет»;
    - пометка «расхождение с игрой» при флаге;
    - открывается и закрывается без ошибок для контракта без предложений.
  - **Depends**: TSK-011

- [ ] **TSK-013**: Модальный `OfferReviewDialog`
  - **Requirement**: FR-3, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/OfferReviewDialog.cs`
  - **Acceptance**:
    - все поля FR-3.2 и предупреждения FR-3.3;
    - окно на `WindowTemplate.Modal`: время на паузе, пока окно открыто;
    - «Предложить» → `OfferGate.ConfirmAndSend`; «Отмена» и закрытие → запись `OfferCancelledOnReview`;
    - переключатель «Больше не спрашивать для безопасных» меняет `confirmMode` на `Risky`;
    - перед отправкой проверка `ContractManager.ContractExists`;
    - окно помещается в 1280×720.
  - **Depends**: TSK-007, TSK-012

- [ ] **TSK-014**: Окно `ContractLogWindow`
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/ContractLogWindow.cs`
  - **Acceptance**:
    - открывается из окна контрактов, хаба «Моды» и действия письма;
    - 6 фильтров `DropdownUI`;
    - записи от новых к старым;
    - панель подробностей показывает `data` и итог тендера целиком.
  - **Depends**: TSK-006, TSK-011

- [ ] **TSK-015**: Вкладка `AutomationPreviewPanel`
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/AutomationPreviewPanel.cs`
  - **Acceptance**:
    - показывает `AutomationLogger.Preview()` с датой следующего прогона и днями до него;
    - «Обновить» пересчитывает по текущему `Player.ContractProfitMargin`;
    - при выключенной автоматике — пометка.
  - **Depends**: TSK-009, TSK-014

- [ ] **TSK-016**: `BusinessSignGate` — подтверждение подписи сделки через `ModUI.Confirm`
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/BusinessSignGate.cs`
  - **Acceptance**:
    - prefix `ContractNegotiationWindow.SignContract` откладывает подпись (кроме режима «Никогда») и вызывает `ModUI.Confirm` с текстом: срок, линии, наценка, эксклюзивность, штрафы, оценка согласия;
    - «Подписать» вызывает оригинал под обходом `BypassScope`;
    - postfix пишет `BusinessSigned`;
    - «Нет» оставляет окно переговоров без изменений;
    - своё окно для сделок не создаётся.
  - **Depends**: TSK-004, TSK-006; mod-framework TSK-064 (`ModUI.Confirm`)

- [ ] **TSK-017**: Оценка возможных конкурентов
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractInspector.cs` (`PotentialAiBidders`), блок в `UI/OddsPanel.cs`
  - **Acceptance**:
    - список CPU ИИ, прошедших `FailsAtAnyRequirement`, не снятых, `SellOnContracts`, с маржой ≥ `ai.contractMarginThreshold`;
    - пессимистичная вероятность;
    - пометки «оценка» и «на сложности 5 ИИ активнее» (при `Player.DifficultyLevel == 5`).
  - **Depends**: TSK-012

- [ ] **TSK-018**: События и предикаты `ct.*` для других фич
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractTransparencyEvents.cs`, регистрация предикатов в `ContractTransparencyFeature.cs`
  - **Acceptance**:
    - `OfferSent` поднимается один раз на отправку (вручную, автоматика, API) через `ModHooks.Raise`;
    - `TenderResolved` — на итог с игроком, не в перемотке;
    - типы `ct.offersSent` и `ct.tendersWon` (параметр `atLeast`) зарегистрированы через `ModConditions.Register`, не отклонены проверкой префикса (`ModIds.IsPredicateNameAllowed`), разбираются `ModConditions.Parse`, счётчики переживают сохранение.
  - **Depends**: TSK-007, TSK-008; mod-framework TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

## Фаза 4 — Строки, проверки, документация

- [ ] **TSK-019**: Строки `ct.*` (ru, en)
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (раздел `ct.*`)
  - **Acceptance**:
    - каждый ключ из кода есть в обоих файлах; `python install.py --check` не находит расхождений ru/en и чужих префиксов;
    - термины совпадают с `ptru` («Предложить», «Отозвать», «Вероятность:», «Автоотмена предложений», «Автоподписание контрактов»);
    - деньги и IPS форматируются через `StringFormatter.FloatToMoney` и `StringFormatter.FloatToIps`.
  - **Depends**: TSK-011…TSK-016; mod-framework TSK-013 (`ModL10n`), TSK-043 (строки ядра, `prefixes`), TSK-045 (`install.py --check`)

- [ ] **TSK-020**: Самопроверка целей и замеры
  - **Requirement**: FR-12, NFR-1, NFR-2
  - **Deliverables**: регистрация целей и членов в `ContractTransparencyFeature.cs`; таймеры `ModLog` в `OfferGate`, `TenderRecorder`, `AutomationLogger`, `TenderMailer`
  - **Acceptance**:
    - самопроверка находит все 6 целей патчей и приватные члены из design.md («Самопроверка на старте»), включая метод `Contract.GetBestOffers`; `ModHooks.IsReserved` для целей фичи — `false`;
    - эмуляция отсутствия члена (тестовый флаг) отключает только зависящую часть;
    - таймеры пишут время в `ModLog` при `ModSettings.VerboseLog`.
  - **Depends**: TSK-007…TSK-016; mod-framework TSK-042 (`ModSelfCheck`)

- [ ] **TSK-021**: Тесты совместимости сейвов
  - **Requirement**: NFR-3
  - **Deliverables**: `mod/tests/PTMod.Tests/ContractTransparency/SaveCompatibilityTests.cs`, фикстуры `mod/tests/PTMod.Tests/ContractTransparency/Fixtures/*.ptmod.json`
  - **Acceptance**:
    - фикстуры без секции, с `_schema` 1 и с `_schema` 99 грузятся (для `_schema` 99 секция сохраняется ядром нетронутой, журнал пуст);
    - неизвестные поля игнорируются;
    - запись не трогает чужие секции.
  - **Depends**: TSK-006; mod-framework TSK-016 (`SideFileModel`)

- [ ] **TSK-022**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1…FR-14, NFR-1, NFR-5
  - **Deliverables**: отметки о прохождении сценариев M1–M14 из design.md в разделе «Progress» (в Evidence: дата, версия мода, замечания)
  - **Acceptance**:
    - все сценарии M1–M13 пройдены (M14 — если включена `platform-generations`);
    - в `Player.log` нет исключений с префиксом `[PTMod]` от фичи;
    - метрика «≤ 0.05 мс в день» подтверждена замером.
  - **Depends**: TSK-001…TSK-021; mod-framework TSK-044, TSK-068 (установка `install.py --with-mods`)

- [ ] **TSK-023**: Описание фичи для игроков
  - **Requirement**: FR-13
  - **Deliverables**: текст описания в хабе «Моды» (ключи `ct.hub.*` в `mod/src/PTMod/L10n/*.json`); раздел «Прозрачность контрактов» в README модов (`mod/README.md`)
  - **Acceptance**:
    - описание объясняет режимы подтверждения, «фактическую вероятность» и журнал;
    - есть пример с числами 60/40 → 21 %.
  - **Depends**: TSK-019

## Dependency graph

```
mod-framework (TSK-022 FeatureContext, TSK-023 ModHooks, TSK-021 ModSettings/ModContent, TSK-025 ModSave, TSK-026 ModCompanies,
               TSK-027/059/060 ModModifiers, TSK-024 ModTicks, TSK-033 ModNotify, TSK-037/038/062 ModMail, TSK-034…036/064 ModUI,
               TSK-019/032 ModConditions, TSK-066 ModIds, TSK-067 мини-раннер)
        │
TSK-001 ──> TSK-002 ──> TSK-004 ──┐
                 │                 ├──> TSK-005 ──┬──> TSK-007 ──┬──> TSK-009 ──> TSK-015
TSK-003 ─────────┼─────────────────┘              │              │
                 └──> TSK-006 ────────────────────┼──> TSK-008 ──┼──> TSK-010
                          │                       │              └──> TSK-018
                          │                       └──> TSK-011 ──> TSK-012 ──┬──> TSK-013
                          │                                                  └──> TSK-017
                          ├──> TSK-014 (после TSK-011) ──> TSK-015
                          ├──> TSK-016 (+TSK-004, mod-framework TSK-064)
                          └──> TSK-021
TSK-011…TSK-016 ──> TSK-019 ──> TSK-023
TSK-007…TSK-016 ──> TSK-020
TSK-001…TSK-021 ──> TSK-022 (владелец)
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Скелет фичи | Pending |
| TSK-002 rules.json, ContractRules, настройки | Pending |
| TSK-003 OddsMath | Pending |
| TSK-004 RequirementCheck, CommitmentMath, ConfirmPolicy | Pending |
| TSK-005 ContractInspector со сверкой | Pending |
| TSK-006 ContractLog, ContractLogModel, DTO, ContractKey, ModSave | Pending |
| TSK-007 OfferGate (Contract.AddOffer) | Pending |
| TSK-008 TenderRecorder (ChooseCpu), TenderOutcomeClassifier | Pending |
| TSK-009 AutomationAdvisor, AutomationLogger | Pending |
| TSK-010 TenderMailer | Pending |
| TSK-011 ContractWindowAugmenter | Pending |
| TSK-012 OddsPanel | Pending |
| TSK-013 OfferReviewDialog | Pending |
| TSK-014 ContractLogWindow | Pending |
| TSK-015 AutomationPreviewPanel | Pending |
| TSK-016 BusinessSignGate (ModUI.Confirm) | Pending |
| TSK-017 Возможные конкуренты | Pending |
| TSK-018 События и предикаты ct.* | Pending |
| TSK-019 Строки ct.* | Pending |
| TSK-020 Самопроверка и замеры | Pending |
| TSK-021 Совместимость сейвов | Pending |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Описание для игроков | Pending |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-004, TSK-005, TSK-012 |
| FR-2 | TSK-003, TSK-005, TSK-011, TSK-012 |
| FR-3 | TSK-004, TSK-007, TSK-013 |
| FR-4 | TSK-002, TSK-004, TSK-007, TSK-013 |
| FR-5 | TSK-008, TSK-010 |
| FR-6 | TSK-009 |
| FR-7 | TSK-008 |
| FR-8 | TSK-006, TSK-014 |
| FR-9 | TSK-009, TSK-015 |
| FR-10 | TSK-016 |
| FR-11 | TSK-003, TSK-017 |
| FR-12 | TSK-005, TSK-020 |
| FR-13 | TSK-001, TSK-002, TSK-023 |
| FR-14 | TSK-007, TSK-018 |
| NFR-1 | TSK-020, TSK-022 |
| NFR-2 | TSK-001, TSK-007, TSK-020 |
| NFR-3 | TSK-006, TSK-021 |
| NFR-4 | TSK-019 |
| NFR-5 | TSK-011, TSK-013, TSK-022 |
| NFR-6 | TSK-002, TSK-006 |
| NFR-7 | TSK-002, TSK-006, TSK-021 |
